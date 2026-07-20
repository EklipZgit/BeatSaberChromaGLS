using System;
using System.Collections;
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
        private static int MaterialStrobeDiagnosticCount;
#if !PRE_V1_37_1
        private static int PerLightTransitionDiagnosticCount;
        private static int PerLightOutputDiagnosticCount;
#endif
#if V1_29_1
        private static readonly ConditionalWeakTable<LightColorGroupEffect, LegacyStrobeState> LegacyStrobeStates = new();
        private static readonly ConditionalWeakTable<object, LegacyFogState> LegacyFogStates = new();
        private static int LegacyOutputDiagnosticCount;
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
        private static int FilteredStrobeDiagnosticCount;
        private static int DifferentColorStrobeDiagnosticCount;
        private static int NativeStrobeDiagnosticCount;

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
                bool strobeFade = (bool)StrobeFadeField.GetValue(__instance);
                int diagnosticLightId = (int)LightIdField.GetValue(__instance);
                if (diagnosticLightId == 221 && FilteredStrobeDiagnosticCount++ < 160)
                {
                    Plugin.Log.Info($"[ChromaGLS filtered-strobe] lightId={diagnosticLightId} t={t:F4} fromFrequency={fromFrequency:F4} toFrequency={toFrequency:F4} fromBrightness={(float)FromStrobeBrightnessField.GetValue(__instance):F4} toBrightness={(float)ToStrobeBrightnessField.GetValue(__instance):F4} phase={phase:F4} fade={strobeFade} normal={color} strobe={strobeColor}");
                }

                // Verify the serialized sf flag independently from strobe brightness and color state.
                if (strobeFade)
                {
                    float fade = InOutCubic(1f - Mathf.Abs((phase * 2f) - 1f));
                    if (StrobeFadeDiagnosticCount++ < 120)
                    {
                        Plugin.Log.Info($"[ChromaGLS] strobeFade t={t:F4} phase={phase:F4} fade={fade:F4} normalAlpha={color.a:F4} strobeBrightness={strobeBrightness:F4} normal={color} strobe={strobeColor} stateFrom={state?.From} stateTo={state?.To}");
                    }
                    // Alpha is HDR intensity, so interpolate emitted RGB energy instead of independently amplifying mixed RGB.
                    color = LerpHdrColor(color, WithAlpha(strobeColor, strobeBrightness), fade);

                    if (fade < 0.01f || fade > 0.95f)
                    {
                        Plugin.Log.Info($"[ChromaGLS] strobeFade output phase={phase:F4} fade={fade:F4} output={color}");
                    }
                }
                else if (phase > 0.5f)
                {
                    color = WithAlpha(strobeColor, strobeBrightness);
                }

                Color currentNormalColor = Color.LerpUnclamped(normalFrom, normalTo, t);
                bool differentRgb = !Mathf.Approximately(currentNormalColor.r, strobeColor.r)
                    || !Mathf.Approximately(currentNormalColor.g, strobeColor.g)
                    || !Mathf.Approximately(currentNormalColor.b, strobeColor.b);
                bool nearExtremum = Mathf.Abs(phase - 0.5f) < 0.08f || phase < 0.08f || phase > 0.92f;
                // Diagnose the strobeColor-only material flicker without altering the staged fade output.
                if (strobeFade && differentRgb && nearExtremum && DifferentColorStrobeDiagnosticCount++ < 120)
                {
                    int diagnosticId = (int)LightIdField.GetValue(__instance);
                    object diagnosticManager = LightManagerField.GetValue(__instance);
                    FieldInfo lightsField = diagnosticManager.GetType().GetField("_lights", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    Array lights = (Array)lightsField?.GetValue(diagnosticManager);
                    List<string> rendererTypes = new();
                    if (lights?.GetValue(diagnosticId) is IEnumerable renderers)
                    {
                        foreach (object renderer in renderers)
                        {
                            string rendererType = renderer?.GetType().FullName ?? "null";
                            if (rendererType == "TubeBloomPrePassLightWithId")
                            {
                                object tube = renderer.GetType().GetField("_tubeBloomPrePassLight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
                                Type? tubeType = tube?.GetType();
                                object? boostToWhite = tubeType?.GetField("_boostToWhite", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tube);
                                object? currentTubeColor = tubeType?.GetField("_color", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tube);
                                object? limitAlpha = tubeType?.GetField("_limitAlpha", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tube);
                                object? minAlpha = tubeType?.GetField("_minAlpha", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tube);
                                object? maxAlpha = tubeType?.GetField("_maxAlpha", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tube);
                                rendererType += $"(color={currentTubeColor},boostToWhite={boostToWhite},limitAlpha={limitAlpha},minAlpha={minAlpha},maxAlpha={maxAlpha})";
                            }

                            rendererTypes.Add(rendererType);
                        }
                    }

                    Plugin.Log.Info($"[ChromaGLS different-strobe] lightId={diagnosticId} t={t:F4} phase={phase:F4} normal={currentNormalColor} strobe={strobeColor} strobeBrightness={strobeBrightness:F4} output={color} renderers={string.Join(",", rendererTypes)}");
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
            // MaterialLightWithId can reinterpret alpha as RGB or multiply RGB by HDR alpha; record its actual configured mode.
            LogMaterialStrobeState(lightManager, lightId, "custom", t, color);
            return false;
        }

        // Measure native non-custom strobe output before changing custom alpha semantics.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(LightColorGroupEffect), nameof(LightColorGroupEffect.SetColor))]
        private static void SetColorPostfix(LightColorGroupEffect __instance, float t)
        {
            if (NativeStrobeDiagnosticCount >= 120
                || (StrobeColorStates.TryGetValue(__instance, out StrobeColorState? state) && state.HasCustomColor))
            {
                return;
            }

            float fromFrequency = (float)FromStrobeFrequencyField.GetValue(__instance);
            float toFrequency = (float)ToStrobeFrequencyField.GetValue(__instance);
            if (fromFrequency <= 0f && toFrequency <= 0f)
            {
                return;
            }

            object lightManager = LightManagerField.GetValue(__instance);
            int lightId = (int)LightIdField.GetValue(__instance);
            FieldInfo lightsField = lightManager.GetType().GetField("_lights", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (lightsField?.GetValue(lightManager) is not Array lights
                || lights.GetValue(lightId) is not IEnumerable renderers)
            {
                return;
            }

            foreach (object renderer in renderers)
            {
                if (renderer?.GetType().FullName != "TubeBloomPrePassLightWithId")
                {
                    continue;
                }

                object tube = renderer.GetType().GetField("_tubeBloomPrePassLight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
                object? tubeColor = tube?.GetType().GetField("_color", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tube);
                Plugin.Log.Info($"[ChromaGLS native-strobe] lightId={lightId} t={t:F4} tubeColor={tubeColor}");
                NativeStrobeDiagnosticCount++;
                break;
            }

            // Compare native material interpretation against the custom path at the same strobe phase.
            LogMaterialStrobeState(lightManager, lightId, "native", t, null);
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
            // 1.34.2 starts a fade only when the upcoming node requests interpolation.
            bool hasTween = nextEventData != null && nextEventData.transitionType == BeatmapEventTransitionType.Interpolate;
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
            Color oemToColor = (Color)ToColorField.GetValue(__instance);
            // Keep custom RGB, but preserve native brightness alpha for the normal half of each strobe cycle.
            Color normalFromColor = fromColor.HasValue
                ? WithAlpha(fromColor.Value, oemFromColor.a)
                : oemFromColor;
            Color normalToColor = hasTween
                ? toColor.HasValue
                    ? WithAlpha(toColor.Value, oemToColor.a)
                    : oemToColor
                : normalFromColor;
            StrobeColorStates.GetOrCreateValue(__instance).Set(
                currentStrobeColor,
                nextCustomStrobeColor,
                normalFromColor,
                normalToColor);
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
            StrobeColorStates.TryGetValue(__instance, out StrobeColorState? colorState);
            // Match the modern path by restoring custom normal-color endpoints instead of lerping stale native fields.
            Color normalFrom = colorState?.NormalFrom ?? (Color)FromColorField.GetValue(__instance);
            Color normalTo = colorState?.NormalTo ?? (Color)ToColorField.GetValue(__instance);
            Color color = Color.LerpUnclamped(normalFrom, normalTo, t);

            float fromFrequency = (float)FromStrobeFrequencyField.GetValue(__instance);
            float toFrequency = (float)ToStrobeFrequencyField.GetValue(__instance);
            float diagnosticPhase = 0f;
            float diagnosticFade = 0f;
            float diagnosticStrobeBrightness = 0f;
            LegacyStrobeStates.TryGetValue(__instance, out LegacyStrobeState? state);
            if (fromFrequency > 0 || toFrequency > 0)
            {
                float strobeBrightness = state == null
                    ? 0f
                    : Mathf.LerpUnclamped(state.FromBrightness, state.ToBrightness, t);

                object tween = FloatTweenField.GetValue(__instance);
                float duration = (float)(TweenDurationProperty?.GetValue(tween) ?? 0f);
                float elapsed = t * duration;
                float elapsedHalf = duration > 0 ? elapsed * elapsed / (2f * duration) : 0f;
                float phase = ((-fromFrequency * elapsedHalf) + (fromFrequency * elapsed) + (toFrequency * elapsedHalf)) % 1f;
                diagnosticPhase = phase;
                diagnosticStrobeBrightness = strobeBrightness;

                if (state is { Fade: true })
                {
                    float fade = InOutCubic(1f - Mathf.Abs((phase * 2f) - 1f));
                    diagnosticFade = fade;
                    Color strobeColor = colorState?.GetColor(
                        t,
                        (Color)FromColorField.GetValue(__instance),
                        (Color)ToColorField.GetValue(__instance)) ?? color;
                    // The 1.29.1 fog pipeline needs the game's straight color fade; HDR source weighting makes the dim-color half collapse.
                    color = Color.LerpUnclamped(color, WithAlpha(strobeColor, strobeBrightness), fade);
                }
                else if (phase > 0.5f)
                {
                    Color strobeColor = colorState?.GetColor(
                        t,
                        (Color)FromColorField.GetValue(__instance),
                        (Color)ToColorField.GetValue(__instance)) ?? color;
                    color = WithAlpha(strobeColor, strobeBrightness);
                }
            }

            object lightManager = LightManagerField.GetValue(__instance);
            int lightId = (int)LightIdField.GetValue(__instance);
            // Compensate only legacy fog during a hard strobe's custom normal half; preserve tube and material RGBA.
            bool compensateNormalFog = colorState is { HasCustomColor: true }
                && state is { Fade: false }
                && diagnosticPhase <= 0.5f;
            SetLegacyFogCompensation(lightManager, lightId, compensateNormalFog);
            SetColorForIdMethod?.Invoke(lightManager, new object[] { lightId, color });
            if (LegacyOutputDiagnosticCount++ < 240)
            {
                string tubeState = "missing";
                FieldInfo lightsField = lightManager.GetType().GetField("_lights", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (lightsField?.GetValue(lightManager) is Array lights
                    && lights.GetValue(lightId) is IEnumerable renderers)
                {
                    foreach (object renderer in renderers)
                    {
                        if (renderer?.GetType().FullName != "TubeBloomPrePassLightWithId")
                        {
                            continue;
                        }

                        object tube = renderer.GetType().GetField("_tubeBloomPrePassLight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
                        Type? tubeType = tube?.GetType();
                        object? tubeColor = tubeType?.GetField("_color", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tube);
                        object? bloomMultiplier = tubeType?.GetField("_bloomFogIntensityMultiplier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tube);
                        tubeState = $"color={tubeColor},bloomMultiplier={bloomMultiplier}";
                        break;
                    }
                }

                Plugin.Log.Info($"[ChromaGLS 1.29 output] lightId={lightId} t={t:F4} phase={diagnosticPhase:F4} fade={diagnosticFade:F4} strobeBrightness={diagnosticStrobeBrightness:F4} output={color} tube={tubeState}");
            }

            return false;
        }

        private static void SetLegacyFogCompensation(object lightManager, int lightId, bool compensate)
        {
            FieldInfo lightsField = lightManager.GetType().GetField("_lights", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (lightsField?.GetValue(lightManager) is not Array lights
                || lights.GetValue(lightId) is not IEnumerable renderers)
            {
                return;
            }

            foreach (object renderer in renderers)
            {
                if (renderer?.GetType().FullName != "TubeBloomPrePassLightWithId")
                {
                    continue;
                }

                object tube = renderer.GetType().GetField("_tubeBloomPrePassLight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
                FieldInfo? multiplierField = tube?.GetType().GetField("_bloomFogIntensityMultiplier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (tube == null || multiplierField == null)
                {
                    continue;
                }

                LegacyFogState fogState = LegacyFogStates.GetValue(
                    tube,
                    key => new LegacyFogState((float)multiplierField.GetValue(key)));
                multiplierField.SetValue(tube, compensate ? fogState.BaseMultiplier * 2f : fogState.BaseMultiplier);
            }
        }

        private static readonly PropertyInfo TweenDurationProperty =
            FloatTweenField?.FieldType.GetProperty("duration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly MethodInfo SetColorForIdMethod =
            LightManagerField?.FieldType.GetMethod("SetColorForId", new[] { typeof(int), typeof(Color) });

        private static Color WithAlpha(Color color, float alpha) => new(color.r, color.g, color.b, alpha);

        private static float InOutCubic(float t) =>
            t < 0.5f ? 4f * t * t * t : 1f - (Mathf.Pow((-2f * t) + 2f, 3f) / 2f);

        private sealed class LegacyFogState
        {
            public LegacyFogState(float baseMultiplier)
            {
                BaseMultiplier = baseMultiplier;
            }

            public float BaseMultiplier { get; }
        }

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

        private static void LogMaterialStrobeState(object lightManager, int lightId, string path, float t, Color? input)
        {
            if (MaterialStrobeDiagnosticCount >= 240)
            {
                return;
            }

            FieldInfo lightsField = lightManager.GetType().GetField("_lights", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (lightsField?.GetValue(lightManager) is not Array lights
                || lights.GetValue(lightId) is not IEnumerable renderers)
            {
                return;
            }

            foreach (object renderer in renderers)
            {
                Type? rendererType = renderer?.GetType();
                if (rendererType?.FullName != "MaterialLightWithId")
                {
                    continue;
                }

                object? color = rendererType.GetField("_color", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
                object? setAlphaOnly = rendererType.GetField("_setAlphaOnly", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
                object? alphaIntoColor = rendererType.GetField("_alphaIntoColor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
                object? setColorOnly = rendererType.GetField("_setColorOnly", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
                object? alphaIntensity = rendererType.GetField("_alphaIntensity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
                object? multiplyColorWithAlpha = rendererType.GetField("_multiplyColorWithAlpha", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
                object? multiplyColor = rendererType.GetField("_multiplyColor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
                object? colorMultiplier = rendererType.GetField("_colorMultiplier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
                Plugin.Log.Info($"[ChromaGLS material-strobe] path={path} lightId={lightId} t={t:F4} input={input} result={color} setAlphaOnly={setAlphaOnly} alphaIntoColor={alphaIntoColor} setColorOnly={setColorOnly} alphaIntensity={alphaIntensity} multiplyColorWithAlpha={multiplyColorWithAlpha} multiplyColor={multiplyColor} colorMultiplier={colorMultiplier}");
                MaterialStrobeDiagnosticCount++;
                break;
            }
        }

        private static Color LerpHdrColor(Color from, Color to, float t)
        {
            float alpha = Mathf.LerpUnclamped(from.a, to.a, t);
            if (Mathf.Abs(alpha) < 0.000001f)
            {
                return new Color(
                    Mathf.LerpUnclamped(from.r, to.r, t),
                    Mathf.LerpUnclamped(from.g, to.g, t),
                    Mathf.LerpUnclamped(from.b, to.b, t),
                    alpha);
            }

            // Fade dim-color energy faster than HDR intensity so white does not linger around a saturated bright peak.
            float fromWeight = from.a * (1f - t) * (1f - t);
            float toWeight = alpha - fromWeight;
            return new Color(
                ((from.r * fromWeight) + (to.r * toWeight)) / alpha,
                ((from.g * fromWeight) + (to.g * toWeight)) / alpha,
                ((from.b * fromWeight) + (to.b * toWeight)) / alpha,
                alpha);
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
