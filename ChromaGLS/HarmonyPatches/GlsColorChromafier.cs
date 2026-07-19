using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using ChromaGLS;
using CustomJSONData.CustomBeatmap;
using HarmonyLib;
using UnityEngine;

namespace ChromaGLS.HarmonyPatches
{
    // Applies Chroma custom colors to GLS (LightColorGroupEffect) events.
    //
    // Plain-Harmony port of the Heck/Chroma GlsColorChromafier (originally a SiraUtil.Affinity patch).
    // This plugin does not depend on SiraUtil/Zenject, so it is a static Harmony postfix instead.
    //
    // Patch strategy:
    //   POSTFIX HandleColorChangeBeatmapEvent - after SetData has run, patch _fromColor/_toColor directly.
    //     SetColor(t) does Color.LerpUnclamped(_fromColor, _toColor, t) every frame, so these two fields
    //     fully control color interpolation. We preserve the game's alpha (brightness) from each field
    //     and replace only the RGB with the custom color. _fromColor gets currentEventData's color;
    //     _toColor gets nextSameTypeEventData's color (if it has a custom color), otherwise left alone.
    [HarmonyPatch(typeof(LightColorGroupEffect), nameof(LightColorGroupEffect.HandleColorChangeBeatmapEvent))]
    internal static class GlsColorChromafier
    {
        private static readonly FieldInfo FromColorField =
            typeof(LightColorGroupEffect).GetField("_fromColor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo ToColorField =
            typeof(LightColorGroupEffect).GetField("_toColor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo AltFromColorField =
            typeof(LightColorGroupEffect).GetField("_alternativeFromColor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo AltToColorField =
            typeof(LightColorGroupEffect).GetField("_alternativeToColor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly MethodInfo SetColorMethod =
            typeof(LightColorGroupEffect).GetMethod("SetColor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly object[] NoTweenIndicator = { 0f };
        private static readonly ConditionalWeakTable<LightColorGroupEffect, StrobeColorState> StrobeColorStates = new();

        // Keep diagnostics shared across game-version-specific strobe implementations.
        private static int EventColorDiagnosticCount;
#if !PRE_V1_37_1
        private static int PerLightTransitionDiagnosticCount;
        private static int PerLightOutputDiagnosticCount;
#endif
#if V1_29_1
        private static readonly ConditionalWeakTable<LightColorGroupEffect, LegacyStrobeState> LegacyStrobeStates = new();
#else
        private static readonly FieldInfo FromStrobeFrequencyField =
            typeof(LightColorGroupEffect).GetField("_fromStrobeFrequency", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo ToStrobeFrequencyField =
            typeof(LightColorGroupEffect).GetField("_toStrobeFrequency", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo FromStrobeBrightnessField =
            typeof(LightColorGroupEffect).GetField("_fromStrobeBrightness", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo ToStrobeBrightnessField =
            typeof(LightColorGroupEffect).GetField("_toStrobeBrightness", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo StrobeFadeField =
            typeof(LightColorGroupEffect).GetField("_strobeFade", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo FloatTweenField =
            typeof(LightColorGroupEffect).GetField("_floatTween", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo LightManagerField =
            typeof(LightColorGroupEffect).GetField("_lightManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo LightIdField =
            typeof(LightColorGroupEffect).GetField("_lightId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly PropertyInfo TweenDurationProperty =
            FloatTweenField?.FieldType.GetProperty("duration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly MethodInfo SetColorForIdMethod =
            LightManagerField?.FieldType.GetMethod("SetColorForId", new[] { typeof(int), typeof(Color) });

        // Temporary bounded diagnostics isolate whether bright-period flicker comes from phase or brightness inputs.
        private static int StrobeFadeDiagnosticCount;
        private static int StrobeOutputDiagnosticCount;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(LightColorGroupEffect), nameof(LightColorGroupEffect.SetColor))]
        private static bool SetColorPrefix(LightColorGroupEffect __instance, float t)
        {
            Color color = Color.LerpUnclamped(
                (Color)FromColorField.GetValue(__instance),
                (Color)ToColorField.GetValue(__instance),
                t);
            if (!StrobeColorStates.TryGetValue(__instance, out StrobeColorState? state) || !state.HasCustomColor)
            {
                // Preserve the game's native strobe/material behavior when the event has no custom GLS color.
                return true;
            }

            float fromFrequency = (float)FromStrobeFrequencyField.GetValue(__instance);
            float toFrequency = (float)ToStrobeFrequencyField.GetValue(__instance);
            // Preserve staged strobe rendering verbatim, but let native SetColor own non-strobe light-ID transitions.
            if (fromFrequency <= 0 && toFrequency <= 0)
            {
                return true;
            }

            Color normalFrom = state.NormalFrom ?? (Color)FromColorField.GetValue(__instance);
            Color normalTo = state.NormalTo ?? (Color)ToColorField.GetValue(__instance);
            color = Color.LerpUnclamped(normalFrom, normalTo, t);

            if (fromFrequency > 0 || toFrequency > 0)
            {
                float strobeBrightness = Mathf.LerpUnclamped(
                    (float)FromStrobeBrightnessField.GetValue(__instance),
                    (float)ToStrobeBrightnessField.GetValue(__instance),
                    t);
                object tween = FloatTweenField.GetValue(__instance);
                float duration = (float)(TweenDurationProperty?.GetValue(tween) ?? 0f);
                float elapsed = t * duration;
                float elapsedHalf = duration > 0 ? elapsed * elapsed / (2f * duration) : 0f;
                float phase = ((-fromFrequency * elapsedHalf) + (fromFrequency * elapsed) + (toFrequency * elapsedHalf)) % 1f;
                Color strobeColor = state?.GetColor(
                    t,
                    (Color)FromColorField.GetValue(__instance),
                    (Color)ToColorField.GetValue(__instance)) ?? color;
                if ((bool)StrobeFadeField.GetValue(__instance))
                {
                    float fade = InOutCubic(1f - Mathf.Abs((phase * 2f) - 1f));
                    if (StrobeFadeDiagnosticCount++ < 120)
                    {
                        Plugin.Log.Info($"[ChromaGLS] strobeFade t={t:F4} phase={phase:F4} fade={fade:F4} normalAlpha={color.a:F4} strobeBrightness={strobeBrightness:F4} normal={color} strobe={strobeColor} stateFrom={state?.From} stateTo={state?.To}");
                    }
                    color = Color.LerpUnclamped(color, WithAlpha(strobeColor, strobeBrightness), fade);
                    if (fade < 0.01f || fade > 0.95f)
                    {
                        Plugin.Log.Info($"[ChromaGLS] strobeFade output phase={phase:F4} fade={fade:F4} output={color}");
                    }
                }
                else if (phase > 0.5f)
                {
                    color = WithAlpha(strobeColor, strobeBrightness);
                }
            }

            object lightManager = LightManagerField.GetValue(__instance);
            int lightId = (int)LightIdField.GetValue(__instance);
            if (StrobeOutputDiagnosticCount++ < 160 && (color.a < 0.1f || color.a > 0.95f))
            {
                Plugin.Log.Info($"[ChromaGLS] SetColorForId lightId={lightId} color={color}");
            }

#if !PRE_V1_37_1
            // Diagnose whether early per-light transitions come from the native tween input or custom endpoint interpolation.
            if (PerLightOutputDiagnosticCount++ < 220)
            {
                Color outputNormalFrom = state?.NormalFrom ?? (Color)FromColorField.GetValue(__instance);
                Color outputNormalTo = state?.NormalTo ?? (Color)ToColorField.GetValue(__instance);
                object tween = FloatTweenField.GetValue(__instance);
                float duration = (float)(TweenDurationProperty?.GetValue(tween) ?? 0f);
                Plugin.Log.Info($"[ChromaGLS 1.40] output lightId={lightId} t={t:F4} duration={duration:F4} normalFrom={outputNormalFrom} normalTo={outputNormalTo} customFrom={state?.From} customTo={state?.To} output={color}");
            }
#endif
            SetColorForIdMethod?.Invoke(lightManager, new object[] { lightId, color });
            return false;
        }

        private static Color WithAlpha(Color color, float alpha) => new(color.r, color.g, color.b, alpha);

        private static float InOutCubic(float t) =>
            t < 0.5f ? 4f * t * t * t : 1f - (Mathf.Pow((-2f * t) + 2f, 3f) / 2f);
#endif

        [HarmonyPrefix]
        private static void Prefix(
            LightColorGroupEffect __instance,
            LightColorBeatmapEventData currentEventData)
        {
            ApplyCustomColors(__instance, currentEventData, false);
        }

        [HarmonyPostfix]
        private static void Postfix(
            LightColorGroupEffect __instance,
            LightColorBeatmapEventData currentEventData)
        {
            ApplyCustomColors(__instance, currentEventData, true);
        }

        private static void ApplyCustomColors(
            LightColorGroupEffect __instance,
            LightColorBeatmapEventData currentEventData,
            bool forceNoTweenColor)
        {
            Color? fromColor = ResolveCustomColor(currentEventData, "color");
            Color oemFromColor = (Color)FromColorField.GetValue(__instance);
            Color currentStrobeColor = ResolveCustomColor(currentEventData, "strobeColor") ?? fromColor ?? oemFromColor;
            // Use the native event chain for normal color transitions; custom strobe-color endpoints must remain box-local.
            LightColorBeatmapEventData? nextEventData = currentEventData.nextSameTypeEventData as LightColorBeatmapEventData;
            LightColorBeatmapEventData? nextStrobeEventData = FindNextEventInSameBox(currentEventData);
#if V1_29_1
            // 1.29.1 uses the upcoming event's transition mode to decide whether this interval interpolates.
            bool hasTween = nextEventData != null && nextEventData.transitionType == BeatmapEventTransitionType.Interpolate;
            if (EventColorDiagnosticCount++ < 200)
            {
                Plugin.Log.Info($"[ChromaGLS 1.29] transition current={currentEventData.transitionType} next={nextEventData?.transitionType} hasTween={hasTween} currentStrobeFade={GetStrobeFade(currentEventData)} nextStrobeFade={(nextEventData != null ? GetStrobeFade(nextEventData) : false)} currentStrobeBrightness={ResolveStrobeBrightness(currentEventData)} nextStrobeBrightness={(nextEventData != null ? ResolveStrobeBrightness(nextEventData) : 0f)} currentFrequency={currentEventData.strobeBeatFrequency} nextFrequency={(nextEventData?.strobeBeatFrequency ?? 0)}");
            }
#elif PRE_V1_37_1
            bool hasTween = nextEventData != null && currentEventData.transitionType != BeatmapEventTransitionType.Instant;
#else
            bool hasTween = nextEventData != null && nextEventData.easeType != EaseType.None;
#endif
            Color? toColor = hasTween ? ResolveCustomColor(nextEventData!, "color") : fromColor;
            Color? customStrobeColor = ResolveCustomColor(currentEventData, "strobeColor");
#if !PRE_V1_37_1
            if (PerLightTransitionDiagnosticCount++ < 240)
            {
                int lightId = (int)LightIdField.GetValue(__instance);
                Plugin.Log.Info($"[ChromaGLS 1.40] lightId={lightId} group={currentEventData.groupId} element={currentEventData.elementId} currentTime={currentEventData.time:F4} nextTime={nextEventData?.time:F4} currentBrightness={currentEventData.brightness:F3} nextBrightness={nextEventData?.brightness:F3} currentEase={currentEventData.easeType} nextEase={nextEventData?.easeType} hasTween={hasTween} currentCustom={fromColor.HasValue || customStrobeColor.HasValue} nextCustom={ResolveCustomColor(nextEventData!, "color").HasValue} nativeFrom={(Color)FromColorField.GetValue(__instance)} nativeTo={(Color)ToColorField.GetValue(__instance)}");
            }
#endif
#if V1_29_1
            if (currentEventData is ICustomData legacyData
                && (legacyData.customData.ContainsKey("__chromaGLS_strobeBrightness")
                    || legacyData.customData.ContainsKey("__chromaGLS_strobeFade")))
            {
                // OEM 1.29.1 nodes still need their backported brightness/fade metadata even without custom colors.
                LegacyStrobeStates.GetOrCreateValue(__instance).Set(
                    ResolveStrobeBrightness(currentEventData),
                    nextEventData == null ? ResolveStrobeBrightness(currentEventData) : ResolveStrobeBrightness(nextEventData),
                    GetStrobeFade(currentEventData),
                    null);
            }
            else
            {
                LegacyStrobeStates.Remove(__instance);
            }
#endif
            if (!fromColor.HasValue && !customStrobeColor.HasValue)
            {
                // OEM current nodes still need the approaching custom primary color installed as the native transition endpoint.
                StrobeColorStates.Remove(__instance);
                if (toColor.HasValue)
                {
                    ApplyColorWithAlpha(ToColorField, __instance, toColor.Value);
                    ApplyColorWithAlpha(AltToColorField, __instance, toColor.Value);
                }

                return;
            }

            Color? nextCustomStrobeColor = hasTween
                ? ResolveCustomColor(nextStrobeEventData!, "strobeColor")
                    ?? ResolveCustomColor(nextStrobeEventData!, "color")
                    ?? (Color)ToColorField.GetValue(__instance)
                : currentStrobeColor;
            // A strobe endpoint follows the next node's explicit strobeColor, custom color, or native color in that order.
            if (EventColorDiagnosticCount++ < 160 && (fromColor.HasValue || customStrobeColor.HasValue || nextCustomStrobeColor.HasValue))
            {
                Plugin.Log.Info($"[ChromaGLS] prepare forceNoTween={forceNoTweenColor} group={currentEventData.groupId} element={currentEventData.elementId} time={currentEventData.time:F4} currentColor={fromColor} currentStrobe={currentStrobeColor} transition={hasTween} nextColor={toColor} nextStrobe={nextCustomStrobeColor} nativeFrom={(Color)FromColorField.GetValue(__instance)} nativeTo={(Color)ToColorField.GetValue(__instance)}");
            }
            Color toStrobeColor = nextCustomStrobeColor ?? currentStrobeColor;
            StrobeColorStates.GetOrCreateValue(__instance).Set(
                currentStrobeColor,
                nextCustomStrobeColor,
                fromColor ?? oemFromColor,
                hasTween
                    ? toColor ?? (Color)ToColorField.GetValue(__instance)
                    : fromColor ?? oemFromColor);
            if (customStrobeColor.HasValue || nextCustomStrobeColor.HasValue)
            {
                Plugin.Log.Info($"[ChromaGLS] strobeColor group={currentEventData.groupId} element={currentEventData.elementId} time={currentEventData.time:F4} from={customStrobeColor.HasValue} to={nextCustomStrobeColor.HasValue}");
            }


            if (fromColor.HasValue)
            {
                ApplyColorWithAlpha(FromColorField, __instance, fromColor.Value);
                // Keep the game's alternate/boost color path synchronized with the custom strobe color.
                ApplyColorWithAlpha(AltFromColorField, __instance, currentStrobeColor);

                if (!hasTween)
                {
#if V1_29_1
                    // The native handler overwrites _toColor before this postfix; keep both endpoints at the current custom color for an instant node.
                    ApplyColorWithAlpha(ToColorField, __instance, fromColor.Value);
                    ApplyColorWithAlpha(AltToColorField, __instance, currentStrobeColor);
#endif
                    // Without this we would deviate from the out of the box behavior and change to the
                    // next color immediately instead of waiting for its node. The default implementation
                    // sets the color to 0f when there is no tween.
                    if (forceNoTweenColor && currentEventData.strobeBeatFrequency <= 0)
                    {
                        SetColorMethod.Invoke(__instance, NoTweenIndicator);
                    }

                    return;
                }
            }

            if (toColor.HasValue)
            {
                ApplyColorWithAlpha(ToColorField, __instance, toColor.Value);
                // Keep the game's alternate/boost color path synchronized with the next custom strobe color.
                ApplyColorWithAlpha(AltToColorField, __instance, toStrobeColor);
            }
        }

#if V1_29_1
        private static readonly FieldInfo FromStrobeFrequencyField =
            typeof(LightColorGroupEffect).GetField("_fromStrobeFrequency", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo ToStrobeFrequencyField =
            typeof(LightColorGroupEffect).GetField("_toStrobeFrequency", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo FloatTweenField =
            typeof(LightColorGroupEffect).GetField("_floatTween", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo LightManagerField =
            typeof(LightColorGroupEffect).GetField("_lightManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo LightIdField =
            typeof(LightColorGroupEffect).GetField("_lightId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        // Beat Saber 1.29.1 lacks the newer strobe brightness/fade fields in its runtime event data.
        // Backport the 1.34.2 SetColor strobe behavior. The 1.29.1 original strobes to the static
        // LightColorGroupEffect.offColor (always off), so no amount of field rewriting can produce a
        // nonzero strobe level. We replace the method and reimplement the newer algorithm:
        //   color = Lerp(_fromColor, _toColor, t)
        //   strobeBrightness = Lerp(fromStrobeBrightness, toStrobeBrightness, t)
        //   strobeFade ? Lerp(color, color.WithAlpha(sb), InOutCubic(1-|phase*2-1|))
        //              : (phase > 0.5 ? strobeColor.WithAlpha(sb) : color)
        [HarmonyPrefix]
        [HarmonyPatch(typeof(LightColorGroupEffect), nameof(LightColorGroupEffect.SetColor))]
        private static bool LegacySetColorPrefix(LightColorGroupEffect __instance, float t)
        {
            Color color = Color.LerpUnclamped(
                (Color)FromColorField.GetValue(__instance),
                (Color)ToColorField.GetValue(__instance),
                t);

            float fromFrequency = (float)FromStrobeFrequencyField.GetValue(__instance);
            float toFrequency = (float)ToStrobeFrequencyField.GetValue(__instance);
            if (fromFrequency > 0 || toFrequency > 0)
            {
                LegacyStrobeStates.TryGetValue(__instance, out LegacyStrobeState? state);
                float strobeBrightness = state == null
                    ? 0f
                    : Mathf.LerpUnclamped(state.FromBrightness, state.ToBrightness, t);

                object tween = FloatTweenField.GetValue(__instance);
                float duration = (float)(TweenDurationProperty?.GetValue(tween) ?? 0f);
                float elapsed = t * duration;
                float elapsedHalf = duration > 0 ? elapsed * elapsed / (2f * duration) : 0f;
                float phase = ((-fromFrequency * elapsedHalf) + (fromFrequency * elapsed) + (toFrequency * elapsedHalf)) % 1f;

                if (state is { Fade: true })
                {
                    float fade = InOutCubic(1f - Mathf.Abs((phase * 2f) - 1f));
                    Color strobeColor = StrobeColorStates.TryGetValue(__instance, out StrobeColorState? colorState)
                        ? colorState.GetColor(
                            t,
                            (Color)FromColorField.GetValue(__instance),
                            (Color)ToColorField.GetValue(__instance)) ?? color
                        : color;
                    color = Color.LerpUnclamped(color, WithAlpha(strobeColor, strobeBrightness), fade);
                }
                else if (phase > 0.5f)
                {
                    Color strobeColor = StrobeColorStates.TryGetValue(__instance, out StrobeColorState? colorState)
                        ? colorState.GetColor(
                            t,
                            (Color)FromColorField.GetValue(__instance),
                            (Color)ToColorField.GetValue(__instance)) ?? color
                        : color;
                    color = WithAlpha(strobeColor, strobeBrightness);
                }
            }

            object lightManager = LightManagerField.GetValue(__instance);
            int lightId = (int)LightIdField.GetValue(__instance);
            SetColorForIdMethod?.Invoke(lightManager, new object[] { lightId, color });
            return false;
        }

        private static readonly PropertyInfo TweenDurationProperty =
            FloatTweenField?.FieldType.GetProperty("duration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly MethodInfo SetColorForIdMethod =
            LightManagerField?.FieldType.GetMethod("SetColorForId", new[] { typeof(int), typeof(Color) });

        private static Color WithAlpha(Color color, float alpha) => new(color.r, color.g, color.b, alpha);

        private static float InOutCubic(float t) =>
            t < 0.5f ? 4f * t * t * t : 1f - (Mathf.Pow((-2f * t) + 2f, 3f) / 2f);

        private sealed class LegacyStrobeState
        {
            public float FromBrightness { get; private set; }

            public float ToBrightness { get; private set; }

            public bool Fade { get; private set; }

            public bool HasStrobeColor { get; private set; }

            public Color StrobeColor { get; private set; }

            public void Set(float fromBrightness, float toBrightness, bool fade, Color? strobeColor)
            {
                FromBrightness = fromBrightness;
                ToBrightness = toBrightness;
                Fade = fade;
                HasStrobeColor = strobeColor.HasValue;
                StrobeColor = strobeColor ?? default;
            }
        }
#endif

        private static float ResolveStrobeBrightness(LightColorBeatmapEventData eventData)
        {
#if V1_29_1
            if (eventData is ICustomData customDataEvent)
            {
                object? value = customDataEvent.customData.Get<object>("__chromaGLS_strobeBrightness");
                if (value != null)
                {
                    return Convert.ToSingle(value);
                }
            }

            return 0f;
#else
            return eventData.strobeBrightness;
#endif
        }

        private static bool GetStrobeFade(LightColorBeatmapEventData eventData)
        {
#if V1_29_1
            if (eventData is ICustomData customDataEvent)
            {
                object? value = customDataEvent.customData.Get<object>("__chromaGLS_strobeFade");
                if (value != null)
                {
                    return Convert.ToBoolean(value);
                }
            }

            return false;
#else
            return eventData.strobeFade;
#endif
        }

        private sealed class StrobeColorState
        {
            private Color? _from;
            private Color? _to;
            private Color? _normalFrom;
            private Color? _normalTo;

            public Color? From => _from;

            public Color? To => _to;

            public Color? NormalFrom => _normalFrom;

            public Color? NormalTo => _normalTo;

            public bool HasCustomColor => _from.HasValue || _to.HasValue || _normalFrom.HasValue || _normalTo.HasValue;

            public void Set(Color? from, Color? to, Color normalFrom, Color normalTo)
            {
                _from = from;
                _to = to;
                _normalFrom = normalFrom;
                _normalTo = normalTo;
            }

            public Color? GetColor(float t, Color normalFrom, Color normalTo)
            {
                Color from = _from ?? normalFrom;
                Color to = _to ?? normalTo;
                return Color.LerpUnclamped(from, to, t);
            }
        }

        private static void ApplyColorWithAlpha(FieldInfo field, LightColorGroupEffect instance, Color customColor)
        {
            Color existing = (Color)field.GetValue(instance);
            field.SetValue(instance, new Color(customColor.r, customColor.g, customColor.b, existing.a));
        }

        private static LightColorBeatmapEventData? FindNextEventInSameBox(LightColorBeatmapEventData currentEventData)
        {
            if (!GlsConverterPatches.TryGetEventOrigin(currentEventData, out LightColorBeatmapEventDataBox origin))
            {
                return currentEventData.nextSameTypeEventData as LightColorBeatmapEventData;
            }

            BeatmapEventData? candidate = currentEventData.nextSameTypeEventData;
            while (candidate is LightColorBeatmapEventData next)
            {
                if (GlsConverterPatches.TryGetEventOrigin(next, out LightColorBeatmapEventDataBox candidateOrigin)
                    && ReferenceEquals(origin, candidateOrigin))
                {
                    return next;
                }

                candidate = next.nextSameTypeEventData;
            }

            return null;
        }

        private static Color? ResolveCustomColor(LightColorBeatmapEventData eventData, string field)
        {
            if (eventData is ICustomData customDataEvent)
            {
                List<object>? color = customDataEvent.customData.Get<List<object>>(field);
                if (color == null || color.Count < 3)
                {
                    return null;
                }

                return new Color(
                    Convert.ToSingle(color[0]),
                    Convert.ToSingle(color[1]),
                    Convert.ToSingle(color[2]),
                    color.Count > 3 ? Convert.ToSingle(color[3]) : 1f);
            }

            return null;
        }
    }
}
