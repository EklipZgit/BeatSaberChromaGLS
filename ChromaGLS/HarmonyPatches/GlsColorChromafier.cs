using System;
using System.Collections.Generic;

#if PRE_V1_37_1
using System.Reflection;
using System.Reflection.Emit;
#endif
using CustomJSONData.CustomBeatmap;
using HarmonyLib;
using Tweening;
using UnityEngine;

// Harmony requires injected private-field parameter names, while version-conditional patch members produce unavoidable ordering diagnostics.
#pragma warning disable SA1005, SA1118, SA1201, SA1202, SA1312, SA1507, SA1508, SA1512, SA1515

namespace ChromaGLS.HarmonyPatches
{
    // SetData has no current event argument, so prepare custom colors around HandleColorChangeBeatmapEvent.
    // Replace SetColor only for easing, HSV, or strobe colors that the native fields cannot represent.
    [HarmonyPatch(typeof(LightColorGroupEffect), nameof(LightColorGroupEffect.HandleColorChangeBeatmapEvent))]
    internal static class GlsColorChromafier
    {
        // Store the extra strobe RGBA track separately because native fields cannot represent phase-specific custom alpha.
        private static readonly Dictionary<LightColorGroupEffect, StrobeColorState> StrobeColorStates = new();

        private static readonly Dictionary<LightColorGroupEffect, ColorEasingState> ColorEasingStates = new();

#if V1_29_1
        private static readonly Dictionary<LightColorGroupEffect, LegacyStrobeState> LegacyStrobeStates = new();
        private static readonly Dictionary<LightColorGroupEffect, LegacyFogState> LegacyFogStates = new();
#endif

        // ResumeTween can call SetColor immediately, so resolve easing before the native handler runs.
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void StageColorEasing(LightColorGroupEffect __instance, LightColorBeatmapEventData currentEventData)
        {
            // Transition easing belongs to this light's next event, including when that event comes from another box.
            LightColorBeatmapEventData? nextEventData = currentEventData.nextSameTypeEventData as LightColorBeatmapEventData;
            TryGetColorEasing(nextEventData, out Func<float, float>? intervalEase);

            // colorEasing and strobeColorEasing describe the incoming transition.
            // strobeEasing controls the active event's pulse fade, even without a transition.
            TryGetIntervalEasing(nextEventData, "colorEasing", out Func<float, float>? colorEase);
            bool hasColorTween = HasColorTweenFromStaged(nextEventData, intervalEase != null);
            Func<float, float>? strobeColorEase = null;
            if (hasColorTween)
            {
                TryGetIntervalEasing(nextEventData, "strobeColorEasing", out strobeColorEase);
            }

            TryGetTrackEasing(currentEventData, "strobeEasing", out Func<float, float>? fadeEase);

            bool hsv = nextEventData != null && PermitsIntervalEase(nextEventData) && IsHsvEasingType(nextEventData);
            float? fromStrobePulseWidth = ResolveStrobePulseWidth(currentEventData);
            float? toStrobePulseWidth = hasColorTween && nextEventData != null
                ? ResolveStrobePulseWidth(nextEventData)
                : fromStrobePulseWidth;
            if (intervalEase != null || colorEase != null || strobeColorEase != null || fadeEase != null || hsv
                || fromStrobePulseWidth.HasValue || toStrobePulseWidth.HasValue)
            {
                ColorEasingStates[__instance] = new ColorEasingState(intervalEase, colorEase, strobeColorEase, fadeEase, hsv, fromStrobePulseWidth, toStrobePulseWidth);
            }
            else
            {
                ColorEasingStates.Remove(__instance);
            }
        }

        internal static bool IsTrueHsvTransition(LightColorGroupEffect instance)
        {
            return ColorEasingStates.TryGetValue(instance, out ColorEasingState state) && state.Hsv;
        }

        private static bool IsHsvEasingType(LightColorBeatmapEventData eventData)
        {
            return eventData is ICustomData customEvent
                && customEvent.customData.Get<object>("easingType") is string easingType
                && easingType == "HSV";
        }

        // GLS hue follows the shortest path around the color wheel, including across red at 0/1.
        internal static Color LerpTrueHsv(Color start, Color end, float t)
        {
            Color.RGBToHSV(start, out float startHue, out float startSaturation, out float startValue);
            Color.RGBToHSV(end, out float endHue, out float endSaturation, out float endValue);
            float hue = Mathf.LerpAngle(startHue * 360f, endHue * 360f, t);
            Color color = Color.HSVToRGB(
                Mathf.Repeat(hue, 360f) / 360f,
                Mathf.LerpUnclamped(startSaturation, endSaturation, t),
                Mathf.LerpUnclamped(startValue, endValue, t));
            color.a = Mathf.LerpUnclamped(start.a, end.a, t);
            return color;
        }

        internal static Color LerpStrobeFade(Color normal, Color strobe, float fade, bool hsv) =>
            hsv
                ? LerpTrueHsv(normal, strobe, fade)
                : Color.LerpUnclamped(normal, strobe, fade);

        // Events created outside the V3 deserializer still need the transition check.
        private static bool TryGetColorEasing(LightColorBeatmapEventData? eventData, out Func<float, float>? easing)
        {
            easing = null;
            if (eventData == null)
            {
                return false;
            }

            if (PermitsIntervalEase(eventData) && eventData is ICustomData customEvent
                && GlsEasingRuntimePatch.TryGetCustomEasing(customEvent.customData.Get<object>("easing"), out easing))
            {
                return true;
            }

#if !PRE_V1_37_1
            return GlsEasingRuntimePatch.TryGetAdditionalNativeEasing(eventData.easeType, out easing);
#else
            return false;
#endif
        }

        private static bool PermitsIntervalEase(LightColorBeatmapEventData eventData)
        {
#if PRE_V1_37_1
            return eventData.transitionType == BeatmapEventTransitionType.Interpolate
                || eventData.transitionType == BeatmapEventTransitionType.Extend;
#else
            return eventData.easeType != EaseType.None || eventData.usePreviousValue;
#endif
        }

        private static bool TryGetIntervalEasing(
            LightColorBeatmapEventData? eventData,
            string key,
            out Func<float, float>? easing)
        {
            easing = null;
            return eventData != null
                && PermitsIntervalEase(eventData)
                && TryGetTrackEasing(eventData, key, out easing);
        }

        private static bool TryGetTrackEasing(
            LightColorBeatmapEventData? eventData,
            string key,
            out Func<float, float>? easing)
        {
            easing = null;
            return eventData is ICustomData customEvent
                && GlsEasingRuntimePatch.TryGetTrackEasing(customEvent.customData.Get<object>(key), out easing);
        }

        private static float GetColorProgress(LightColorGroupEffect instance, float t)
        {
            ColorEasingStates.TryGetValue(instance, out ColorEasingState state);
            return CalculateColorProgress(in state, t);
        }

        private static float CalculateColorProgress(in ColorEasingState state, float t)
        {
            // Clamp the curve input to avoid NaN on Beat Saber's final tween frame, but allow the eased output to overshoot.
            return state.Interval != null
                ? state.Interval(Mathf.Clamp01(t))
                : t;
        }

        private static float GetTrackProgress(Func<float, float>? easing, float t, float fallback) =>
            easing != null
                ? easing(Mathf.Clamp01(t))
                : fallback;

        private static float GetNormalColorProgress(LightColorGroupEffect instance, float t)
        {
            ColorEasingStates.TryGetValue(instance, out ColorEasingState state);
            return GetTrackProgress(state.Color, t, CalculateColorProgress(in state, t));
        }

        private static float GetStrobeColorProgress(LightColorGroupEffect instance, float t)
        {
            ColorEasingStates.TryGetValue(instance, out ColorEasingState state);
            return GetTrackProgress(state.StrobeColor, t, CalculateColorProgress(in state, t));
        }

        private static Func<float, float> GetStrobeFadeEasing(LightColorGroupEffect instance)
        {
            ColorEasingStates.TryGetValue(instance, out ColorEasingState state);
            return ResolveStrobeFadeEasing(in state);
        }

        private static Func<float, float> ResolveStrobeFadeEasing(in ColorEasingState state) =>
            state.StrobeFade ?? InOutCubicFunc;

        private static bool NeedsFadeInPhaseAlignment(float fromFrequency, float toFrequency, bool fade) =>
            fade && fromFrequency <= 0f && toFrequency > 0f;

        // Align a strobe fading in from zero with the next event's starting phase so it does not jump on arrival.
        private static float AlignStartingStrobePhase(
            float phase,
            float fromFrequency,
            float toFrequency,
            float duration,
            bool fade,
            float inheritedPhase = 0f)
        {
            if (!NeedsFadeInPhaseAlignment(fromFrequency, toFrequency, fade))
            {
                return phase;
            }

            return Mathf.Repeat(phase - inheritedPhase - ((fromFrequency + toFrequency) * duration * 0.5f), 1f);
        }

        private static bool HasColorTween(LightColorBeatmapEventData? nextEventData)
        {
            return HasColorTweenFromStaged(nextEventData, null);
        }

        private static bool HasColorTweenFromStaged(LightColorBeatmapEventData? nextEventData, bool? hasColorEasing)
        {
            if (nextEventData == null)
            {
                return false;
            }

#if PRE_V1_37_1
            return nextEventData.transitionType == BeatmapEventTransitionType.Interpolate
                || (nextEventData.transitionType == BeatmapEventTransitionType.Extend
                    && (hasColorEasing ?? TryGetColorEasing(nextEventData, out _)));
#else
            return nextEventData.easeType != EaseType.None;
#endif
        }

#if PRE_V1_37_1
        // Change the handler's tween decision without changing Extend inheritance or strobe continuity.
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> CustomExtensionTweenTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo? getter = AccessTools.PropertyGetter(typeof(LightColorBeatmapEventData), nameof(LightColorBeatmapEventData.transitionType));
            FieldInfo? field = AccessTools.Field(typeof(LightColorBeatmapEventData), nameof(LightColorBeatmapEventData.transitionType));
            MethodInfo replacement = AccessTools.Method(typeof(GlsColorChromafier), nameof(GetTweenTransitionType));
            foreach (CodeInstruction instruction in instructions)
            {
                if ((getter != null && instruction.Calls(getter))
                    || (field != null && instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, field)))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                }

                yield return instruction;
            }
        }

        private static BeatmapEventTransitionType GetTweenTransitionType(LightColorBeatmapEventData eventData)
        {
            return HasColorTween(eventData) ? BeatmapEventTransitionType.Interpolate : eventData.transitionType;
        }
#endif

#if !V1_29_1
        [HarmonyPrefix]
        [HarmonyPatch(typeof(LightColorGroupEffect), nameof(LightColorGroupEffect.SetColor))]
        private static bool SetColorPrefix(
            LightColorGroupEffect __instance,
            float t,
            Color ____fromColor,
            Color ____toColor,
            float ____fromStrobeFrequency,
            float ____toStrobeFrequency,
            float ____fromStrobeBrightness,
            float ____toStrobeBrightness,
            bool ____strobeFade,
            FloatTween ____floatTween,
            LightWithIdManager ____lightManager,
            int ____lightId)
        {
            ColorEasingStates.TryGetValue(__instance, out ColorEasingState easingState);
            float intervalT = CalculateColorProgress(in easingState, t);
            bool hasStrobe = ____fromStrobeFrequency > 0f || ____toStrobeFrequency > 0f;
            float colorT = GetTrackProgress(easingState.Color, t, intervalT);
            float strobeColorT = hasStrobe
                ? GetTrackProgress(easingState.StrobeColor, t, intervalT)
                : intervalT;
            // HSV still needs custom rendering when the transition easing is Linear.
            bool hsv = easingState.Hsv;
            bool hasEasing = intervalT != t || colorT != t || strobeColorT != t;
            if (!hasStrobe && !hasEasing && !hsv)
            {
                return true;
            }

            bool customStrobe = StrobeColorStates.TryGetValue(__instance, out StrobeColorState? state) && state.RequiresCustomRendering;
            bool strobeFadeOverride = hasStrobe && ____strobeFade && easingState.StrobeFade != null;
            bool alignStrobePhase = NeedsFadeInPhaseAlignment(____fromStrobeFrequency, ____toStrobeFrequency, ____strobeFade);
            if (!customStrobe && !hasEasing && !strobeFadeOverride && !hsv && !alignStrobePhase && !easingState.HasStrobePulseWidth)
            {
                return true;
            }

            // Ease colors and brightness without changing the elapsed time used to calculate strobe phase.
            Color normalFrom = ____fromColor;
            Color normalTo = ____toColor;
            Color color = hsv
                ? LerpTrueHsv(normalFrom, normalTo, colorT)
                : Color.LerpUnclamped(normalFrom, normalTo, colorT);
            // colorEasing affects RGB only. Brightness follows the interval easing.
            color.a = Mathf.LerpUnclamped(normalFrom.a, normalTo.a, intervalT);
            if (!hasStrobe)
            {
                ____lightManager.SetColorForId(____lightId, color);
                return false;
            }

            Color strobeColor = customStrobe
                ? state!.GetColor(strobeColorT, intervalT, ____fromStrobeBrightness, ____toStrobeBrightness, hsv)
                : WithAlpha(
                    hsv
                        ? LerpTrueHsv(normalFrom, normalTo, strobeColorT)
                        : Color.LerpUnclamped(normalFrom, normalTo, strobeColorT),
                    Mathf.LerpUnclamped(____fromStrobeBrightness, ____toStrobeBrightness, intervalT));
            float strobePulseWidth = easingState.GetStrobePulseWidth(t);
            float duration = ____floatTween.duration;
            float elapsed = t * duration;
            float elapsedHalf = duration > 0f
                ? elapsed * elapsed / (2f * duration)
                : 0f;
            float phase = ((-____fromStrobeFrequency * elapsedHalf) + (____fromStrobeFrequency * elapsed) + (____toStrobeFrequency * elapsedHalf)) % 1f;
            phase = AlignStartingStrobePhase(phase, ____fromStrobeFrequency, ____toStrobeFrequency, duration, ____strobeFade);
            // ApplyCustomColors gives a stopped strobe its normal color at that endpoint, so both pulse phases converge on it.
            if (____strobeFade)
            {
                float fade = EvaluateStrobeFade(phase, strobePulseWidth, ResolveStrobeFadeEasing(in easingState));
                color = LerpStrobeFade(color, strobeColor, fade, hsv);
            }

            // Keep Beat Saber's strict half-cycle boundary when no custom strobe track or pulse width is present.
            else if (customStrobe || easingState.HasStrobePulseWidth
                ? phase >= strobePulseWidth
                : phase > 0.5f)
            {
                color = strobeColor;
            }

            // Avoid reflection and boxing in the per-frame custom strobe path.
            ____lightManager.SetColorForId(____lightId, color);
            return false;
        }
#endif

        private static float? ResolveStrobePulseWidth(LightColorBeatmapEventData eventData)
        {
            object? value = (eventData as ICustomData)?.customData.Get<object>("strobePulseWidth");
            if (value is not (float or double or decimal or int or long or short or byte or uint or ulong or ushort or sbyte))
            {
                return null;
            }

            float pulseWidth = Convert.ToSingle(value);
            if (float.IsNaN(pulseWidth))
            {
                return null;
            }

            pulseWidth = Mathf.Clamp01(pulseWidth);
            return pulseWidth == 0.5f ? null : pulseWidth;
        }

        private static float EvaluateStrobeFade(float phase, float pulseWidth, Func<float, float> easing) =>
            phase < pulseWidth
                ? easing(phase / pulseWidth)
                : 1f - easing((phase - pulseWidth) / (1f - pulseWidth));

        private static Color WithAlpha(Color color, float alpha) => new(color.r, color.g, color.b, alpha);

        // Beat Saber's InOutCubic, simplified.
        private static float InOutCubic(float t) =>
            t < 0.5f
                ? 4f * t * t * t
                : 1f - (Mathf.Pow((-2f * t) + 2f, 3f) / 2f);

        // Cache the delegate to avoid allocating it during each strobe frame.
        private static readonly Func<float, float> InOutCubicFunc = InOutCubic;

        // Call the unmodified private SetColor implementation at event time without reflection or boxing.
        [HarmonyReversePatch]
        [HarmonyPatch(typeof(LightColorGroupEffect), nameof(LightColorGroupEffect.SetColor))]
        private static void InvokeOriginalSetColor(LightColorGroupEffect instance, float t) =>
            throw new NotImplementedException("Harmony reverse patch stub");

        // Remove explicit dictionary state when the game disposes the corresponding effect instance.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(LightColorGroupEffect), nameof(LightColorGroupEffect.Cleanup))]
        private static void CleanupPostfix(LightColorGroupEffect __instance)
        {
            StrobeColorStates.Remove(__instance);
            ColorEasingStates.Remove(__instance);
#if V1_29_1
            LegacyStrobeStates.Remove(__instance);
            LegacyFogStates.Remove(__instance);
#endif
        }

        // Native SetData must establish brightness and tween state before custom endpoints are applied.
        [HarmonyPostfix]
        private static void Postfix(
            LightColorGroupEffect __instance,
            LightColorBeatmapEventData currentEventData,
            ref Color ____fromColor,
            ref Color ____toColor,
            ref Color ____alternativeFromColor,
            ref Color ____alternativeToColor,
            LightWithIdManager ____lightManager,
            int ____lightId,
            ref float ____fromStrobeFrequency,
            ref float ____toStrobeFrequency,
#if !V1_29_1
            // 1.29.1 stores strobe brightness in LegacyStrobeState because the native fields do not exist.
            ref float ____fromStrobeBrightness,
            ref float ____toStrobeBrightness,
#endif
            IBpmController ____bpmController,
            FloatTween ____floatTween,
            SongTimeTweeningManager ____tweeningManager)
        {
            // Re-read injected fields after the native handler has prepared its brightness and frequency endpoints.
            ApplyStrobeInterval(currentEventData, ____bpmController, ref ____fromStrobeFrequency, ref ____toStrobeFrequency);
            ResumeStrobeTweenIfNeeded(__instance, currentEventData, ____floatTween, ____tweeningManager, ____fromStrobeFrequency, ____toStrobeFrequency);
            ApplyCustomColors(
                __instance,
                currentEventData,
                ref ____fromColor,
                ref ____toColor,
                ref ____alternativeFromColor,
                ref ____alternativeToColor,
#if !V1_29_1
                ref ____fromStrobeBrightness,
                ref ____toStrobeBrightness,
#endif
                ____lightManager,
                ____lightId,
                ____fromStrobeFrequency,
                ____toStrobeFrequency);
        }

        private static void ResumeStrobeTweenIfNeeded(
            LightColorGroupEffect instance,
            LightColorBeatmapEventData currentEventData,
            FloatTween floatTween,
            SongTimeTweeningManager tweeningManager,
            float fromStrobeFrequency,
            float toStrobeFrequency)
        {
            if (fromStrobeFrequency <= 0f)
            {
                return;
            }

            if (currentEventData.strobeBeatFrequency > 0)
            {
                // Native handler already resumed the tween.
                return;
            }

            LightColorBeatmapEventData? nextEventData = currentEventData.nextSameTypeEventData as LightColorBeatmapEventData;
            bool hasTween = HasColorTween(nextEventData);

            if (hasTween)
            {
                // Native handler already resumed the tween in the transitioning branch.
                return;
            }

            float endTime = nextEventData == null
                ? currentEventData.time + 1000f
                : nextEventData.time;
            floatTween.SetStartTimeAndEndTime(currentEventData.time, endTime);
            tweeningManager.ResumeTween(floatTween, instance);
        }

        private static void ApplyStrobeInterval(
            LightColorBeatmapEventData currentEventData,
            IBpmController bpmController,
            ref float fromStrobeFrequency,
            ref float toStrobeFrequency)
        {
            LightColorBeatmapEventData? nextEventData = currentEventData.nextSameTypeEventData as LightColorBeatmapEventData;
            bool hasTween = HasColorTween(nextEventData);

            float? fromStrobeInterval = ResolveStrobeInterval(currentEventData);
            float? toStrobeInterval = hasTween && nextEventData != null
                ? ResolveStrobeInterval(nextEventData)
                : fromStrobeInterval;

            // Resolve the destination interval even when the source has none, or its strobe frequency jumps when it activates.
            bool fromOverride = fromStrobeInterval is > 0f;
            bool toOverride = toStrobeInterval is > 0f;
            if (!fromOverride && !toOverride)
            {
                return;
            }

#if V1_29_1
            float oneBeatDuration = TimeExtensions.OneBeatDuration(bpmController.currentBpm);
#else
            float oneBeatDuration = bpmController.oneBeatDuration;
#endif

            if (fromOverride)
            {
                fromStrobeFrequency = 1f / (fromStrobeInterval!.Value * oneBeatDuration);
            }

            if (toOverride)
            {
                toStrobeFrequency = 1f / (toStrobeInterval!.Value * oneBeatDuration);
            }

        }

        // Resolves customData.strobeInterval when a GLS color event activates.
        private static float? ResolveStrobeInterval(LightColorBeatmapEventData eventData)
        {
            if (eventData is not ICustomData customDataEvent)
            {
                return null;
            }

            object? value = customDataEvent.customData.Get<object>("strobeInterval");
            if (value is not IConvertible convertible)
            {
                return null;
            }

            return Convert.ToSingle(convertible);
        }

        private static Color? ApplyNormalColorDistributions(
            Color oemColor,
            Color? customColor,
            GlsConverterPatches.EventColorDistributionContext? context)
        {
            if (context == null || !context.HasNormalColorDistributions)
            {
                return customColor;
            }

            return context.ApplyNormal(customColor ?? WithAlpha(oemColor, 1f));
        }

        // An omitted strobeColor inherits the distributed normal color only when there are no strobe distributions.
        // Otherwise, apply strobe distributions to the raw primary color so normal distributions do not affect both tracks.
        private static Color? ApplyStrobeColorDistributions(
            Color oemColor,
            Color? customNormalColor,
            Color? customStrobeColor,
            GlsConverterPatches.EventColorDistributionContext? context)
        {
            if (customStrobeColor is { } explicitStrobeColor)
            {
                return context == null || !context.HasStrobeColorDistributions
                    ? explicitStrobeColor
                    : context.ApplyStrobe(explicitStrobeColor);
            }

            if (context == null || (!context.HasNormalColorDistributions && !context.HasStrobeColorDistributions))
            {
                return null;
            }

            return context.HasStrobeColorDistributions
                ? context.ApplyStrobe(customNormalColor ?? WithAlpha(oemColor, 1f))
                : context.ApplyNormal(customNormalColor ?? WithAlpha(oemColor, 1f));
        }

        private static void ApplyCustomColors(
            LightColorGroupEffect __instance,
            LightColorBeatmapEventData currentEventData,
            ref Color fromField,
            ref Color toField,
            ref Color alternativeFromField,
            ref Color alternativeToField,
#if !V1_29_1
            // Write the collapsed primary alpha to native brightness fields so native and custom rendering agree.
            ref float fromStrobeBrightness,
            ref float toStrobeBrightness,
#endif
            LightWithIdManager lightManager,
            int lightId,
            float fromStrobeFrequency,
            float toStrobeFrequency)
        {
#if V1_29_1
            // Resolve legacy renderer internals once after native event setup instead of reflecting from SetColor every frame.
            PrepareLegacyFogState(__instance, lightManager, lightId);
#endif
            Color oemFromColor = fromField;
            Color oemToColor = toField;
            Color? rawFromColor = ResolveCustomColor(currentEventData, "color");
            Color? rawCustomStrobeColor = ResolveCustomColor(currentEventData, "strobeColor");
            GlsConverterPatches.TryGetColorDistributionContext(
                currentEventData,
                out GlsConverterPatches.EventColorDistributionContext? currentColorDistributionContext);
            // Use this light's next event across all boxes for both color tracks, rather than the box-local strobe chain.
            LightColorBeatmapEventData? nextEventData = currentEventData.nextSameTypeEventData as LightColorBeatmapEventData;
            // 1.29.1 and 1.34.2 require interpolation on the upcoming node. An Extend node with explicit easing also transitions.
            bool hasTween = HasColorTween(nextEventData);
            Color? rawToColor = hasTween
                ? ResolveCustomColor(nextEventData!, "color")
                : rawFromColor;
            Color? rawNextExplicitStrobeColor = hasTween
                ? ResolveCustomColor(nextEventData!, "strobeColor")
                : rawCustomStrobeColor;
            GlsConverterPatches.TryGetColorDistributionContext(
                nextEventData!,
                out GlsConverterPatches.EventColorDistributionContext? nextColorDistributionContext);
            Color? fromColor = ApplyNormalColorDistributions(oemFromColor, rawFromColor, currentColorDistributionContext);
            Color? customStrobeColor = ApplyStrobeColorDistributions(
                oemFromColor,
                rawFromColor,
                rawCustomStrobeColor,
                currentColorDistributionContext);
            Color? toColor = hasTween
                ? ApplyNormalColorDistributions(oemToColor, rawToColor, nextColorDistributionContext)
                : fromColor;
            Color? nextStrobeNormalColor = hasTween
                ? toColor
                : fromColor;
            Color? nextExplicitStrobeColor = hasTween
                ? ApplyStrobeColorDistributions(
                    oemToColor,
                    rawToColor,
                    rawNextExplicitStrobeColor,
                    nextColorDistributionContext)
                : customStrobeColor;
            // As with basic-event Chroma, custom alpha multiplies native brightness while HDR RGB stays unscaled.
            Color normalFromColor = fromColor.HasValue
                ? ComposeCustomColor(oemFromColor, fromColor.Value)
                : oemFromColor;
            Color normalToColor = hasTween
                ? toColor.HasValue
                    ? ComposeCustomColor(oemToColor, toColor.Value)
                    : oemToColor
                : normalFromColor;
            Color currentStrobeColor = customStrobeColor
                ?? rawFromColor
                ?? WithAlpha(oemFromColor, 1f);
            Color nextStrobeColor = hasTween
                ? nextExplicitStrobeColor
                    ?? rawToColor
                    ?? WithAlpha(oemToColor, 1f)
                : currentStrobeColor;
            // At an endpoint with zero strobe frequency, use its normal RGBA for both pulse phases.
            // This makes the entire transition converge on the normal color without exposing an unused strobeColor or sb.
            bool collapseFromStrobe = hasTween && fromStrobeFrequency <= 0f;
            bool collapseToStrobe = hasTween && toStrobeFrequency <= 0f;
            if (collapseFromStrobe)
            {
                currentStrobeColor = WithAlpha(normalFromColor, 1f);
                customStrobeColor = null;
            }

            if (collapseToStrobe)
            {
                nextStrobeColor = WithAlpha(normalToColor, 1f);
                nextExplicitStrobeColor = null;
            }

#if !V1_29_1
            if (collapseFromStrobe)
            {
                fromStrobeBrightness = normalFromColor.a;
            }

            if (collapseToStrobe)
            {
                toStrobeBrightness = normalToColor.a;
            }
#endif
#if V1_29_1
            if (currentEventData is ICustomData legacyData
                && (legacyData.customData.ContainsKey("cg_sb")
                    || legacyData.customData.ContainsKey("cg_sf")
                    || collapseFromStrobe
                    || collapseToStrobe))
            {
                // Stage 1.29.1 brightness before ResumeTween can call SetColor, then refresh frequencies after SetData.
                // Instant nodes change brightness only when they activate.
                GetOrCreateLegacyStrobeState(__instance).Set(
                    collapseFromStrobe ? normalFromColor.a : ResolveStrobeBrightness(currentEventData),
                    hasTween && nextEventData != null
                        ? collapseToStrobe ? normalToColor.a : ResolveStrobeBrightness(nextEventData)
                        : ResolveStrobeBrightness(currentEventData),
                    GetStrobeFade(currentEventData),
                    currentEventData.time,
                    currentEventData.transitionType == BeatmapEventTransitionType.Extend,
                    fromStrobeFrequency,
                    toStrobeFrequency);
            }
            else
            {
                LegacyStrobeStates.Remove(__instance);
            }
#endif
            bool requiresCustomStrobeRendering = customStrobeColor.HasValue
                || nextExplicitStrobeColor.HasValue
                || (currentColorDistributionContext?.HasNormalColorDistributions == true && !collapseFromStrobe)
                || (nextColorDistributionContext?.HasNormalColorDistributions == true && !collapseToStrobe)
                || (fromColor.HasValue && fromColor.Value.a != 1f && !collapseFromStrobe)
                || (nextStrobeNormalColor.HasValue && nextStrobeNormalColor.Value.a != 1f && !collapseToStrobe);
            // Inheriting the distributed normal color does not make strobeColor explicit.
            bool hasExplicitStrobeColor =
                (customStrobeColor.HasValue
                    && (rawCustomStrobeColor.HasValue
                        || currentColorDistributionContext?.HasStrobeColorDistributions == true))
                || (nextExplicitStrobeColor.HasValue
                    && (rawNextExplicitStrobeColor.HasValue
                        || nextColorDistributionContext?.HasStrobeColorDistributions == true));
#if V1_29_1
            // The legacy replacement needs composed normal endpoints whenever either end of the interval contains custom RGBA.
            if (fromColor.HasValue || toColor.HasValue || customStrobeColor.HasValue || nextExplicitStrobeColor.HasValue)
            {
                GetOrCreateStrobeColorState(__instance).Set(
                    currentStrobeColor,
                    nextStrobeColor,
                    normalFromColor,
                    normalToColor,
                    hasExplicitStrobeColor,
                    requiresCustomStrobeRendering);
            }
            else
            {
                StrobeColorStates.Remove(__instance);
            }
#else
            // Modern native SetColor remains authoritative unless phase-specific custom RGB or alpha changes its output.
            if (requiresCustomStrobeRendering)
            {
                GetOrCreateStrobeColorState(__instance).Set(
                    currentStrobeColor,
                    nextStrobeColor,
                    normalFromColor,
                    normalToColor,
                    hasExplicitStrobeColor,
                    requiresCustomStrobeRendering: true);
            }
            else
            {
                StrobeColorStates.Remove(__instance);
            }
#endif
            if (fromColor.HasValue)
            {
                // Preserve independent HDR RGB while multiplying each regular endpoint's native brightness by custom alpha.
                ApplyCustomColor(ref fromField, fromColor.Value);
                // Apply the same RGBA composition to the boost endpoint without replacing its native brightness multiplier.
                ApplyCustomColor(ref alternativeFromField, fromColor.Value);

                if (!hasTween)
                {
                    // The native handler collapses both endpoint pairs for an instant node, so compose the same custom RGBA at both ends.
                    ApplyCustomColor(ref toField, fromColor.Value);
                    // Keep the instant boost endpoint's custom RGBA aligned with its native alpha.
                    ApplyCustomColor(ref alternativeToField, fromColor.Value);
                    InvokeOriginalSetColor(__instance, 0f);

                    return;
                }
            }

            if (toColor.HasValue)
            {
                // Compose the destination RGBA before native interpolation so alpha and brightness tween as one endpoint value.
                ApplyCustomColor(ref toField, toColor.Value);
                // Apply identical custom RGBA semantics to the destination boost endpoint while retaining its own native alpha.
                ApplyCustomColor(ref alternativeToField, toColor.Value);
            }

#if V1_29_1
            // 1.29.1 can defer the first tween frame. Render t=0 now so a faded strobe starts at its normal color.
            if (customStrobeColor.HasValue
                && GetStrobeFade(currentEventData)
                && currentEventData.strobeBeatFrequency > 0)
            {
                InvokeOriginalSetColor(__instance, 0f);
            }
#endif
        }

#if V1_29_1
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
        private static bool LegacySetColorPrefix(
            LightColorGroupEffect __instance,
            float t,
            Color ____fromColor,
            Color ____toColor,
            float ____fromStrobeFrequency,
            float ____toStrobeFrequency,
            FloatTween ____floatTween,
            LightWithIdManager ____lightManager,
            int ____lightId)
        {
            ColorEasingStates.TryGetValue(__instance, out ColorEasingState easingState);
            StrobeColorStates.TryGetValue(__instance, out StrobeColorState? colorState);
            LegacyStrobeStates.TryGetValue(__instance, out LegacyStrobeState? state);
            // Render the deferred 1.29.1 startup frame at t=0, before applying any elapsed-time progress.
            float outputT = t;
            if (colorState is { HasExplicitStrobeColor: true }
                && state is { Fade: true, InitialFrameRendered: false })
            {
                outputT = 0f;
                state.InitialFrameRendered = true;
            }

            // Ease color progress without changing outputT, which drives the strobe clock.
            float intervalT = CalculateColorProgress(in easingState, outputT);
            float colorT = GetTrackProgress(easingState.Color, outputT, intervalT);
            bool hsv = easingState.Hsv;
            Color normalFrom = colorState?.NormalFrom ?? ____fromColor;
            Color normalTo = colorState?.NormalTo ?? ____toColor;
            Color color = hsv
                ? LerpTrueHsv(normalFrom, normalTo, colorT)
                : Color.LerpUnclamped(normalFrom, normalTo, colorT);
            // Brightness follows the interval easing, independently of colorEasing.
            color.a = Mathf.LerpUnclamped(normalFrom.a, normalTo.a, intervalT);

            if (____fromStrobeFrequency <= 0f && ____toStrobeFrequency <= 0f)
            {
                // Native 1.29.1 already handles non-strobe custom RGB. Replace it only when easing or HSV changes the output.
                SetLegacyFogCompensation(__instance, 1f);
                if (intervalT == outputT && colorT == outputT && !hsv)
                {
                    return true;
                }

                ____lightManager.SetColorForId(____lightId, color);
                return false;
            }

            if (____fromStrobeFrequency > 0f || ____toStrobeFrequency > 0f)
            {
                float strobeBrightness = state == null
                    ? 0f
                    : Mathf.LerpUnclamped(state.FromBrightness, state.ToBrightness, intervalT);
                float strobePulseWidth = easingState.GetStrobePulseWidth(outputT);
                float duration = ____floatTween.duration;
                float elapsed = outputT * duration;
                float elapsedHalf = duration > 0f
                    ? elapsed * elapsed / (2f * duration)
                    : 0f;
                // Extension callbacks restart the native tween at t=0, so add the completed predecessor phase to keep cycling continuous.
                float phase = (state?.PhaseOffset ?? 0f)
                    - (____fromStrobeFrequency * elapsedHalf)
                    + (____fromStrobeFrequency * elapsed)
                    + (____toStrobeFrequency * elapsedHalf);
                phase = Mathf.Repeat(phase, 1f);
                phase = AlignStartingStrobePhase(
                    phase,
                    ____fromStrobeFrequency,
                    ____toStrobeFrequency,
                    duration,
                    state is { Fade: true },
                    state?.PhaseOffset ?? 0f);

                float strobeColorT = GetTrackProgress(easingState.StrobeColor, outputT, intervalT);
                Color strobeColor = colorState is { RequiresCustomRendering: true } && state != null
                    ? colorState.GetColor(strobeColorT, intervalT, state.FromBrightness, state.ToBrightness, hsv)
                    : WithAlpha(
                        hsv
                            ? LerpTrueHsv(normalFrom, normalTo, strobeColorT)
                            : Color.LerpUnclamped(normalFrom, normalTo, strobeColorT),
                        strobeBrightness);
                // As on newer versions, a stopped strobe uses its normal RGBA for both pulse phases.
                if (state is { Fade: true })
                {
                    float fade = EvaluateStrobeFade(phase, strobePulseWidth, ResolveStrobeFadeEasing(in easingState));
                    color = LerpStrobeFade(color, strobeColor, fade, hsv);
                }
                else if (phase >= strobePulseWidth)
                {
                    color = strobeColor;
                }
            }

            // The legacy fog path halves both normal and explicit custom-strobe colors, so both halves need the same 2x compensation.
            // Keeping this constant through faded strobes also prevents the custom strobe endpoint from becoming progressively darker.
            float legacyFogCompensation = colorState is { HasExplicitStrobeColor: true } && (____fromStrobeFrequency > 0f || ____toStrobeFrequency > 0f)
                ? 2f
                : 1f;
            SetLegacyFogCompensation(__instance, legacyFogCompensation);
            // Avoid reflection, boxing, and an argument-array allocation in the legacy per-frame path.
            ____lightManager.SetColorForId(____lightId, color);

            return false;
        }

        // Resolve the 1.29.1 tube renderer once after event setup and retain it until Cleanup.
        private static void PrepareLegacyFogState(
            LightColorGroupEffect instance,
            LightWithIdManager lightManager,
            int lightId)
        {
            if (LegacyFogStates.ContainsKey(instance))
            {
                return;
            }

            if (lightId < 0 || lightId >= lightManager._lights.Length)
            {
                return;
            }

            foreach (ILightWithId renderer in lightManager._lights[lightId])
            {
                if (renderer is not TubeBloomPrePassLightWithId tubeLight)
                {
                    continue;
                }

                // This compensation is specific to the legacy tube-bloom renderer.
                LegacyFogStates.Add(instance, new LegacyFogState(tubeLight._tubeBloomPrePassLight));
                return;
            }
        }

        private static void SetLegacyFogCompensation(LightColorGroupEffect instance, float compensation)
        {
            if (LegacyFogStates.TryGetValue(instance, out LegacyFogState? fogState))
            {
                fogState.SetCompensated(compensation);
            }
        }

        private sealed class LegacyFogState
        {
            private readonly TubeBloomPrePassLight _tube;
            private readonly float _baseMultiplier;

            public LegacyFogState(
                TubeBloomPrePassLight tube)
            {
                _tube = tube;
                _baseMultiplier = tube._bloomFogIntensityMultiplier;
            }

            public void SetCompensated(float compensation)
            {
                _tube._bloomFogIntensityMultiplier = _baseMultiplier * compensation;
            }
        }

        private static LegacyStrobeState GetOrCreateLegacyStrobeState(LightColorGroupEffect instance)
        {
            // Reuse one explicitly cleaned state object per effect with a direct dictionary lookup in SetColor.
            if (LegacyStrobeStates.TryGetValue(instance, out LegacyStrobeState? state))
            {
                return state;
            }

            state = new LegacyStrobeState();
            LegacyStrobeStates.Add(instance, state);

            return state;
        }

        private sealed class LegacyStrobeState
        {
            public float FromBrightness { get; private set; }

            public float ToBrightness { get; private set; }

            public bool Fade { get; private set; }

            private float CurrentEventTime { get; set; }

            private float FromFrequency { get; set; }

            private float ToFrequency { get; set; }

            private bool HasInterval { get; set; }

            public float PhaseOffset { get; private set; }

            // Track whether the deferred legacy tween startup frame has been rendered for this interval.
            public bool InitialFrameRendered { get; set; }

            public void Set(
                float fromBrightness,
                float toBrightness,
                bool fade,
                float currentEventTime,
                bool isExtension,
                float fromFrequency,
                float toFrequency)
            {
                if (!Mathf.Approximately(CurrentEventTime, currentEventTime))
                {
                    // Extend starts a new tween but continues the predecessor's integrated strobe cycle at the boundary.
                    float previousDuration = currentEventTime - CurrentEventTime;
                    PhaseOffset = isExtension && HasInterval && previousDuration > 0f
                        ? Mathf.Repeat(PhaseOffset + (((FromFrequency + ToFrequency) * previousDuration) / 2f), 1f)
                        : 0f;
                    // A new tween interval needs one fresh synthetic t=0 startup frame.
                    InitialFrameRendered = false;
                }

                FromBrightness = fromBrightness;
                ToBrightness = toBrightness;
                Fade = fade;
                CurrentEventTime = currentEventTime;
                FromFrequency = fromFrequency;
                ToFrequency = toFrequency;
                HasInterval = true;
            }
        }
#endif

        private static float ResolveStrobeBrightness(LightColorBeatmapEventData eventData)
        {
#if V1_29_1
            if (eventData is ICustomData customDataEvent)
            {
                object? value = customDataEvent.customData.Get<object>("cg_sb");
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
                object? value = customDataEvent.customData.Get<object>("cg_sf");
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

        private static StrobeColorState GetOrCreateStrobeColorState(LightColorGroupEffect instance)
        {
            // Reuse one state object per effect without ConditionalWeakTable lookup/allocation behavior in the frame loop.
            if (!StrobeColorStates.TryGetValue(instance, out StrobeColorState? state))
            {
                state = new StrobeColorState();
                StrobeColorStates.Add(instance, state);
            }

            return state;
        }

        private readonly struct ColorEasingState
        {
            public ColorEasingState(
                Func<float, float>? interval,
                Func<float, float>? color,
                Func<float, float>? strobeColor,
                Func<float, float>? strobeFade,
                bool hsv,
                float? fromStrobePulseWidth,
                float? toStrobePulseWidth)
            {
                Interval = interval;
                Color = color;
                StrobeColor = strobeColor;
                StrobeFade = strobeFade;
                Hsv = hsv;
                FromStrobePulseWidth = fromStrobePulseWidth;
                ToStrobePulseWidth = toStrobePulseWidth;
            }

            public Func<float, float>? Interval { get; }

            public Func<float, float>? Color { get; }

            public Func<float, float>? StrobeColor { get; }

            public Func<float, float>? StrobeFade { get; }

            public bool Hsv { get; }

            public float? FromStrobePulseWidth { get; }

            public float? ToStrobePulseWidth { get; }

            public bool HasStrobePulseWidth => FromStrobePulseWidth.HasValue || ToStrobePulseWidth.HasValue;

            public float GetStrobePulseWidth(float t) => Mathf.Lerp(FromStrobePulseWidth ?? 0.5f, ToStrobePulseWidth ?? 0.5f, t);
        }

        private sealed class StrobeColorState
        {
            public Color From { get; private set; }

            public Color To { get; private set; }

            public Color? NormalFrom { get; private set; }

            public Color? NormalTo { get; private set; }

            public bool HasExplicitStrobeColor { get; private set; }

            public bool RequiresCustomRendering { get; private set; }

            public void Set(
                Color from,
                Color to,
                Color normalFrom,
                Color normalTo,
                bool hasExplicitStrobeColor,
                bool requiresCustomStrobeRendering)
            {
                From = from;
                To = to;
                NormalFrom = normalFrom;
                NormalTo = normalTo;
                HasExplicitStrobeColor = hasExplicitStrobeColor;
                RequiresCustomRendering = requiresCustomStrobeRendering;
            }

            // strobeColorEasing controls RGB while alpha follows the interval easing.
            public Color GetColor(
                float tColor,
                float tAlpha,
                float fromStrobeBrightness,
                float toStrobeBrightness,
                bool hsv)
            {
                // Multiply custom alpha by strobe brightness before interpolation, matching basic-event Chroma.
                Color color = hsv
                    ? LerpTrueHsv(From, To, tColor)
                    : Color.LerpUnclamped(From, To, tColor);
                color.a = Mathf.LerpUnclamped(From.a * fromStrobeBrightness, To.a * toStrobeBrightness, tAlpha);
                return color;
            }
        }

        private static Color ComposeCustomColor(Color nativeColor, Color customColor)
        {
            // Basic-event Chroma preserves unclamped custom RGB and multiplies custom alpha by the native light level.
            return new Color(customColor.r, customColor.g, customColor.b, nativeColor.a * customColor.a);
        }

        private static void ApplyCustomColor(ref Color existing, Color customColor)
        {
            // Compose rather than replace alpha so independent HDR RGB, custom alpha, and GLS brightness all reach native renderers.
            existing = ComposeCustomColor(existing, customColor);
        }

        private static Color? ResolveCustomColor(LightColorBeatmapEventData eventData, string field)
        {
            if (eventData is not ICustomData customDataEvent)
            {
                return null;
            }

            List<object>? color = customDataEvent.customData.Get<List<object>>(field);
            if (color == null || color.Count < 3)
            {
                return null;
            }

            return new Color(
                Convert.ToSingle(color[0]),
                Convert.ToSingle(color[1]),
                Convert.ToSingle(color[2]),
                color.Count > 3
                    ? Convert.ToSingle(color[3])
                    : 1f);
        }
    }
}
