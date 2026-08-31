using System;
using System.Collections.Generic;
using CustomJSONData.CustomBeatmap;
using HarmonyLib;
using Tweening;
using UnityEngine;

// Harmony requires injected private-field parameter names, while version-conditional patch members produce unavoidable ordering diagnostics.
#pragma warning disable SA1005, SA1118, SA1201, SA1202, SA1312, SA1507, SA1508, SA1512, SA1515

namespace ChromaGLS.HarmonyPatches
{
    // Applies Chroma custom colors to GLS (LightColorGroupEffect) events.
    //
    // Patch strategy:
    //   PRODUCTION, EVENT-TIME: HandleColorChangeBeatmapEvent runs once when a node activates and supplies
    //     currentEventData, which SetData does not receive. Its prefix/postfix stage custom normal/strobe endpoints
    //     around the native SetData call through Harmony ref-field injection.
    //   PRODUCTION, PER-FRAME: SetColor remains native unless an active strobe needs a distinct custom RGB endpoint
    //     or custom alpha multiplier, because phase-dependent RGBA selection cannot be represented by SetData alone.
    [HarmonyPatch(typeof(LightColorGroupEffect), nameof(LightColorGroupEffect.HandleColorChangeBeatmapEvent))]
    internal static class GlsColorChromafier
    {
        // Store the extra strobe RGBA track separately because native fields cannot represent phase-specific custom alpha.
        private static readonly Dictionary<LightColorGroupEffect, StrobeColorState> StrobeColorStates = new();

#if V1_29_1
        private static readonly Dictionary<LightColorGroupEffect, LegacyStrobeState> LegacyStrobeStates = new();
        private static readonly Dictionary<LightColorGroupEffect, LegacyFogState> LegacyFogStates = new();
#endif

#if !V1_29_1
        // 1.29.1 does not define the modern strobe brightness/fade fields; use LegacySetColorPrefix below for that runtime.
        // PRODUCTION HOT PATH: replace SetColor only when a strobe needs phase-specific custom RGB or alpha.
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
            // Preserve staged strobe rendering verbatim, but let native SetColor own non-strobe light-ID transitions.
            if (____fromStrobeFrequency <= 0f && ____toStrobeFrequency <= 0f)
            {
                return true;
            }

            // Native SetColor remains exact when neither a separate strobe RGB value nor a custom alpha multiplier is active.
            if (!StrobeColorStates.TryGetValue(__instance, out StrobeColorState? state) || !state.RequiresCustomRendering)
            {
                return true;
            }

            // Preserve independent, unclamped HDR RGBA channels so downstream material, fog, and bloom renderers retain their native behavior.
            Color normalFrom = ____fromColor;
            Color normalTo = ____toColor;
            Color color = Color.LerpUnclamped(normalFrom, normalTo, t);
            Color strobeColor = state.GetColor(t, ____fromStrobeBrightness, ____toStrobeBrightness);
            float duration = ____floatTween.duration;
            float elapsed = t * duration;
            float elapsedHalf = duration > 0f
                ? elapsed * elapsed / (2f * duration)
                : 0f;
            float phase = ((-____fromStrobeFrequency * elapsedHalf) + (____fromStrobeFrequency * elapsed) + (____toStrobeFrequency * elapsedHalf)) % 1f;
            if (____strobeFade)
            {
                float fade = InOutCubic(1f - Mathf.Abs((phase * 2f) - 1f));
                // Straight RGBA interpolation matches basic-event Chroma and leaves HDR whitening and fog response to each renderer.
                color = Color.LerpUnclamped(color, strobeColor, fade);
            }
            else if (phase >= 0.5f)
            {
                color = strobeColor;
            }

            // Avoid reflection and boxing in the per-frame custom strobe path.
            ____lightManager.SetColorForId(____lightId, color);
            return false;
        }
#endif

// #if false
//         // DIAGNOSTICS ONLY: observe native non-custom strobe output; this postfix never changes game state.
//         [HarmonyPostfix]
//         [HarmonyPatch(typeof(LightColorGroupEffect), nameof(LightColorGroupEffect.SetColor))]
//         private static void SetColorPostfix(
//             LightColorGroupEffect __instance,
//             float t,
//             Color ____fromColor,
//             Color ____toColor,
//             float ____fromStrobeFrequency,
//             float ____toStrobeFrequency,
//             float ____fromStrobeBrightness,
//             float ____toStrobeBrightness,
//             bool ____strobeFade,
//             FloatTween ____floatTween,
//             LightWithIdManager ____lightManager,
//             int ____lightId)
//         {
//             // Harmony field injection includes the target field's leading underscore after the three-underscore prefix.
//             Color ___fromColor = ____fromColor;
//             Color ___toColor = ____toColor;
//             float ___fromStrobeFrequency = ____fromStrobeFrequency;
//             float ___toStrobeFrequency = ____toStrobeFrequency;
//             float ___fromStrobeBrightness = ____fromStrobeBrightness;
//             float ___toStrobeBrightness = ____toStrobeBrightness;
//             bool ___strobeFade = ____strobeFade;
//             FloatTween ___floatTween = ____floatTween;
//             LightWithIdManager ___lightManager = ____lightManager;
//             int ___lightId = ____lightId;
//
// #if !PRE_V1_37_1
//             // DIAGNOSTICS ONLY: compare actual paired output timing after native/custom SetColor dispatch.
//             if ((___lightId == 50 || ___lightId == 80) && StartupTransitionFrameDiagnosticCount++ < 5000)
//             {
//                 StrobeColorStates.TryGetValue(__instance, out StrobeColorState? startupState);
//                 Plugin.Log.Info($"[ChromaGLS startup-frame] frame={Time.frameCount} realtime={Time.realtimeSinceStartup:F4} lightId={___lightId} t={t:F6} duration={___floatTween.duration:F6} fromFrequency={___fromStrobeFrequency:F6} toFrequency={___toStrobeFrequency:F6} fromBrightness={___fromStrobeBrightness:F4} toBrightness={___toStrobeBrightness:F4} fade={___strobeFade} fieldFrom={___fromColor} fieldTo={___toColor} explicitStrobe={startupState?.HasExplicitStrobeColor} strobeFrom={startupState?.From} strobeTo={startupState?.To} renderer={GetDetailedRendererState(___lightManager, ___lightId)}");
//             }
// #endif
//
//             if (StrobeColorStates.TryGetValue(__instance, out StrobeColorState? state) && state.HasCustomColor)
//             {
//                 return;
//             }
//
//             if (___fromStrobeFrequency <= 0f && ___toStrobeFrequency <= 0f)
//             {
//                 return;
//             }
//
// #if PRE_V1_37_1
//             // Capture the 1.34.2 native-owned native-to-custom transition that bypasses the custom SetColor prefix.
//             if (___lightId == 80
//                 && t >= 0.98f
//                 && ReferenceNativeSparseOutputDiagnosticCount++ < 40)
//             {
//                 float duration = ___floatTween.duration;
//                 float strobeBrightness = Mathf.LerpUnclamped(___fromStrobeBrightness, ___toStrobeBrightness, t);
//                 float elapsed = t * duration;
//                 float elapsedHalf = duration > 0f ? elapsed * elapsed / (2f * duration) : 0f;
//                 float phase = ((-___fromStrobeFrequency * elapsedHalf) + (___fromStrobeFrequency * elapsed) + (___toStrobeFrequency * elapsedHalf)) % 1f;
//                 Plugin.Log.Info($"[ChromaGLS 1.34 native reference] lightId={___lightId} duration={duration:F4} t={t:F4} fromFrequency={___fromStrobeFrequency:F4} toFrequency={___toStrobeFrequency:F4} phase={phase:F4} fade={___strobeFade} fromBrightness={___fromStrobeBrightness:F4} toBrightness={___toStrobeBrightness:F4} strobeBrightness={strobeBrightness:F4} fieldFrom={___fromColor} fieldTo={___toColor} renderers={GetReferenceRendererState(___lightManager, ___lightId)}");
//             }
// #endif
//             if (NativeStrobeDiagnosticCount >= 20000)
//             {
//                 return;
//             }
//
//             FieldInfo lightsField = ___lightManager.GetType().GetField("_lights", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
//             if (lightsField?.GetValue(___lightManager) is not Array lights
//                 || lights.GetValue(___lightId) is not IEnumerable renderers)
//             {
//                 return;
//             }
//
//             foreach (object renderer in renderers)
//             {
//                 if (renderer?.GetType().FullName != "TubeBloomPrePassLightWithId")
//                 {
//                     continue;
//                 }
//
//                 object tube = renderer.GetType().GetField("_tubeBloomPrePassLight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
//                 object? tubeColor = tube?.GetType().GetField("_color", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tube);
//                 Plugin.Log.Info($"[ChromaGLS native-strobe] lightId={___lightId} t={t:F4} tubeColor={tubeColor}");
//                 NativeStrobeDiagnosticCount++;
//                 break;
//             }
//
//             // Compare native material interpretation against the custom path at the same strobe phase.
//             LogMaterialStrobeState(___lightManager, ___lightId, "native", t, null);
//         }
// #endif

// #if false
// #if PRE_V1_37_1
//         // DIAGNOSTICS ONLY: inspect post-native renderer colors for matched 1.34.2 samples.
//         private static string GetReferenceRendererState(object lightManager, int lightId)
//         {
//             // Read all post-native renderer colors for the matched 1.34.2 endpoint sample.
//             FieldInfo lightsField = lightManager.GetType().GetField("_lights", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
//             if (lightsField?.GetValue(lightManager) is not Array lights
//                 || lights.GetValue(lightId) is not IEnumerable renderers)
//             {
//                 return "missing";
//             }
//
//             List<string> states = new();
//             foreach (object renderer in renderers)
//             {
//                 Type? rendererType = renderer?.GetType();
//                 if (rendererType == null)
//                 {
//                     continue;
//                 }
//
//                 if (rendererType.FullName == "TubeBloomPrePassLightWithId")
//                 {
//                     object tube = rendererType.GetField("_tubeBloomPrePassLight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
//                     Type? tubeType = tube?.GetType();
//                     object? tubeColor = tubeType?.GetField("_color", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tube);
//                     object? bloomMultiplier = tubeType?.GetField("_bloomFogIntensityMultiplier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tube);
//                     states.Add($"tube={tubeColor},bloom={bloomMultiplier}");
//                     continue;
//                 }
//
//                 // Read the disabled diagnostic renderer color without changing its state.
//                 object? rendererColor = rendererType.GetField("_color", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
//                 states.Add($"{rendererType.FullName}={rendererColor}");
//             }
//
//             return string.Join(";", states);
//         }
// #endif
// #endif

        private static Color WithAlpha(Color color, float alpha) => new(color.r, color.g, color.b, alpha);

        // GAMES FORM FOR THIS IS:
        // k *= 2f;
        // return k < 1f
        //     ? 0.5f * k * k * k
        //     : 0.5f * (((k -= 2f) * k * k) + 2f);
        // Which can be simplified to the below
        private static float InOutCubic(float t) =>
            t < 0.5f
                ? 4f * t * t * t
                : 1f - (Mathf.Pow((-2f * t) + 2f, 3f) / 2f);

// #if false
//         // DIAGNOSTICS ONLY: reflect nested renderer state after SetColorForId; no values are mutated.
//         private static string GetDetailedRendererState(object lightManager, int lightId)
//         {
//             // Resolve the nested per-ID renderer children and their parent configuration because their output is produced after SetColorForId returns.
//             FieldInfo lightsField = lightManager.GetType().GetField("_lights", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
//             if (lightsField?.GetValue(lightManager) is not Array lights
//                 || lights.GetValue(lightId) is not IEnumerable renderers)
//             {
//                 return "missing";
//             }
//
//             List<string> states = new();
//             foreach (object renderer in renderers)
//             {
//                 Type? rendererType = renderer?.GetType();
//                 if (rendererType == null)
//                 {
//                     continue;
//                 }
//
//                 string rendererName = rendererType.FullName ?? rendererType.Name;
//                 if (rendererName.Contains("RuntimeLightWithIds+LightIntensitiesWithId"))
//                 {
//                     object? parent = GetInheritedFieldValue(renderer, "_parentLightWithIds");
//                     states.Add($"runtime(childColor={GetInheritedFieldValue(renderer, "_color")},childIntensity={GetInheritedFieldValue(renderer, "_intensity")},parentIntensity={GetInheritedFieldValue(parent, "_intensity")},maxIntensity={GetInheritedFieldValue(parent, "_maxIntensity")},multiplyAlpha={GetInheritedFieldValue(parent, "_multiplyColorByAlpha")},mix={GetInheritedFieldValue(parent, "_mixType")})");
//                     continue;
//                 }
//
//                 if (rendererName.Contains("LightmapLightWithIds+LightIntensitiesWithId"))
//                 {
//                     object? parent = GetInheritedFieldValue(renderer, "_parentLightWithIds");
//                     states.Add($"lightmap(childColor={GetInheritedFieldValue(renderer, "_color")},childIntensity={GetInheritedFieldValue(renderer, "_intensity")},probeMultiplier={GetInheritedFieldValue(renderer, "_probeHighlightsIntensityMultiplier")},parentIntensity={GetInheritedFieldValue(parent, "_intensity")},probeIntensity={GetInheritedFieldValue(parent, "_probeIntensity")},mix={GetInheritedFieldValue(parent, "_mixType")},normalizer={GetInheritedFieldValue(parent, "_isNormalizerInScene")},calculated={GetInheritedFieldValue(parent, "_calculatedColorPreNormalization")})");
//                     continue;
//                 }
//
//                 if (rendererName == "TubeBloomPrePassLightWithId")
//                 {
//                     object? tube = GetInheritedFieldValue(renderer, "_tubeBloomPrePassLight");
//                     states.Add($"tube(color={GetInheritedFieldValue(tube, "_color")},bloom={GetInheritedFieldValue(tube, "_bloomFogIntensityMultiplier")})");
//                     continue;
//                 }
//
//                 states.Add($"{rendererName}(color={GetInheritedFieldValue(renderer, "_color")})");
//             }
//
//             return string.Join(";", states);
//         }
//
//         // DIAGNOSTICS ONLY: shared reflection helper for renderer-state logging.
//         private static object? GetInheritedFieldValue(object? instance, string fieldName)
//         {
//             // Private renderer state is distributed across nested child and base classes in both legacy and modern HMRendering assemblies.
//             for (Type? type = instance?.GetType(); type != null; type = type.BaseType)
//             {
//                 FieldInfo? field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
//                 if (field != null)
//                 {
//                     return field.GetValue(instance);
//                 }
//             }
//
//             return null;
//         }
//
// #endif

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
#if V1_29_1
            LegacyStrobeStates.Remove(__instance);
            LegacyFogStates.Remove(__instance);
#endif
        }

        // PRODUCTION EVENT-TIME PATH: finalize endpoints after native SetData establishes brightness and tween state.
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
#if PRE_V1_37_1
            bool hasTween = nextEventData != null && nextEventData.transitionType == BeatmapEventTransitionType.Interpolate;
#else
            bool hasTween = nextEventData != null && nextEventData.easeType != EaseType.None;
#endif

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
            float? fromStrobeInterval = ResolveStrobeInterval(currentEventData);
            if (fromStrobeInterval == null)
            {
                return;
            }

            float interval = fromStrobeInterval.Value;
            if (interval <= 0f)
            {
                return;
            }

#if V1_29_1
            float oneBeatDuration = TimeExtensions.OneBeatDuration(bpmController.currentBpm);
#else
            float oneBeatDuration = bpmController.oneBeatDuration;
#endif

            LightColorBeatmapEventData? nextEventData = currentEventData.nextSameTypeEventData as LightColorBeatmapEventData;
#if PRE_V1_37_1
            bool hasTween = nextEventData != null && nextEventData.transitionType == BeatmapEventTransitionType.Interpolate;
#else
            bool hasTween = nextEventData != null && nextEventData.easeType != EaseType.None;
#endif

            float? toStrobeInterval = hasTween && nextEventData != null
                ? ResolveStrobeInterval(nextEventData)
                : fromStrobeInterval;

            fromStrobeFrequency = 1f / (interval * oneBeatDuration);

            if (toStrobeInterval != null)
            {
                float toInterval = toStrobeInterval.Value;
                if (toInterval > 0f)
                {
                    toStrobeFrequency = 1f / (toInterval * oneBeatDuration);
                }
            }
        }

        // Resolves customData.strobeInterval when a GLS color event activates.
        // This is called only from the HandleColorChangeBeatmapEvent postfix (event-time), never from the per-frame SetColor path.
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

        private static void ApplyCustomColors(
            LightColorGroupEffect __instance,
            LightColorBeatmapEventData currentEventData,
            ref Color fromField,
            ref Color toField,
            ref Color alternativeFromField,
            ref Color alternativeToField,
            LightWithIdManager lightManager,
            int lightId,
            float fromStrobeFrequency,
            float toStrobeFrequency)
        {
#if V1_29_1
            // Resolve legacy renderer internals once after native event setup instead of reflecting from SetColor every frame.
            PrepareLegacyFogState(__instance, lightManager, lightId);
#endif
            Color? fromColor = ResolveCustomColor(currentEventData, "color");
            Color? customStrobeColor = ResolveCustomColor(currentEventData, "strobeColor");
            Color oemFromColor = fromField;
            // Use the native event chain for normal transitions while keeping strobe fallback endpoints box-local.
            LightColorBeatmapEventData? nextEventData = currentEventData.nextSameTypeEventData as LightColorBeatmapEventData;
            LightColorBeatmapEventData? nextStrobeEventData = FindNextEventInSameBox(currentEventData);
#if PRE_V1_37_1
            // 1.29.1 and 1.34.2 start a fade only when the upcoming node requests interpolation.
            bool hasTween = nextEventData != null && nextEventData.transitionType == BeatmapEventTransitionType.Interpolate;
#else
            bool hasTween = nextEventData != null && nextEventData.easeType != EaseType.None;
#endif
            Color? toColor = hasTween
                ? ResolveCustomColor(nextEventData!, "color")
                : fromColor;
            Color? nextStrobeNormalColor = hasTween
                ? ResolveCustomColor(nextStrobeEventData!, "color")
                : fromColor;
// #if false
//             // A bounded event-time trace verifies that an extension received inherited custom RGB instead of its raw node payload.
//             if (currentEventData.usePreviousValue && _modernExtensionDiagnosticCount++ < 96)
//             {
//                 Color? globalNextColor = nextEventData == null ? null : ResolveCustomColor(nextEventData, "color");
//                 Color? boxNextColor = nextStrobeEventData == null ? null : ResolveCustomColor(nextStrobeEventData, "color");
//                 Plugin.Log.Info($"[ChromaGLS extension] current=({currentEventData.groupId},{currentEventData.elementId},{currentEventData.time:F3}) currentColor={fromColor} currentStrobe={customStrobeColor} tween={hasTween} globalNext=({nextEventData?.groupId},{nextEventData?.elementId},{nextEventData?.time:F3},ease={nextEventData?.easeType},color={globalNextColor}) boxNext=({nextStrobeEventData?.groupId},{nextStrobeEventData?.elementId},{nextStrobeEventData?.time:F3},color={boxNextColor}) fields=from:{fromField},to:{toField}");
//             }
// #endif
#if V1_29_1
            if (currentEventData is ICustomData legacyData
                && (legacyData.customData.ContainsKey("cg_sb")
                    || legacyData.customData.ContainsKey("cg_sf")))
            {
                // OEM 1.29.1 nodes still need their backported brightness/fade metadata even without custom colors.
                // A non-transition node changes the level at its boundary; it is not the endpoint of the current interval's brightness tween.
                // Stage brightness in the prefix before ResumeTween can synchronously call SetColor, then refresh frequencies in the postfix after SetData.
                GetOrCreateLegacyStrobeState(__instance).Set(
                    ResolveStrobeBrightness(currentEventData),
                    hasTween && nextEventData != null
                        ? ResolveStrobeBrightness(nextEventData)
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
            Color? nextExplicitStrobeColor = hasTween
                ? ResolveCustomColor(nextStrobeEventData!, "strobeColor")
                : customStrobeColor;
            Color oemToColor = toField;
            // Compose normal endpoint alpha exactly like basic-event Chroma: custom alpha multiplies native brightness without scaling HDR RGB.
            Color normalFromColor = fromColor.HasValue
                ? ComposeCustomColor(oemFromColor, fromColor.Value)
                : oemFromColor;
            Color normalToColor = hasTween
                ? toColor.HasValue
                    ? ComposeCustomColor(oemToColor, toColor.Value)
                    : oemToColor
                : normalFromColor;
            // Strobe source alpha remains a separate multiplier so normal brightness cannot leak into the independent sb light level.
            Color currentStrobeColor = customStrobeColor
                ?? fromColor
                ?? WithAlpha(oemFromColor, 1f);
            Color nextStrobeColor = hasTween
                ? nextExplicitStrobeColor
                    ?? nextStrobeNormalColor
                    ?? WithAlpha(oemToColor, 1f)
                : currentStrobeColor;
            bool requiresCustomStrobeRendering = customStrobeColor.HasValue
                || nextExplicitStrobeColor.HasValue
                || (fromColor.HasValue && fromColor.Value.a != 1f)
                || (nextStrobeNormalColor.HasValue && nextStrobeNormalColor.Value.a != 1f);
#if V1_29_1
            // The legacy replacement needs composed normal endpoints whenever either end of the interval contains custom RGBA.
            if (fromColor.HasValue || toColor.HasValue || customStrobeColor.HasValue || nextExplicitStrobeColor.HasValue)
            {
                GetOrCreateStrobeColorState(__instance).Set(
                    currentStrobeColor,
                    nextStrobeColor,
                    normalFromColor,
                    normalToColor,
                    customStrobeColor.HasValue || nextExplicitStrobeColor.HasValue,
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
                    customStrobeColor.HasValue || nextExplicitStrobeColor.HasValue,
                    requiresCustomStrobeRendering: true);
            }
            else
            {
                StrobeColorStates.Remove(__instance);
            }
#endif
// #if V1_29_1
//             // DIAGNOSTICS ONLY: distinguish stale endpoints from interpolation timing on the test track.
//             if (currentEventData.groupId == 3
//                 && currentEventData.elementId == 0
//                 && (currentEventData.strobeBeatFrequency > 0 || (nextEventData?.strobeBeatFrequency ?? 0) > 0)
//                 && LegacyColorEndpointDiagnosticCount++ < 160)
//             {
//                 Plugin.Log.Info($"[ChromaGLS 1.29 color endpoint] current={currentEventData.time:F4} next={nextEventData?.time:F4} tween={hasTween} colorFrom={fromColor} colorTo={toColor} strobeFrom={customStrobeColor} strobeTo={nextExplicitStrobeColor} normalFrom={normalFromColor} normalTo={normalToColor} fieldFrom={fromField} fieldTo={toField}");
//             }
// #endif
//             // DIAGNOSTICS ONLY: record boxes using a custom strobe endpoint.
//             if (customStrobeColor.HasValue || nextCustomStrobeColor.HasValue)
//             {
//                 Plugin.Log.Info($"[ChromaGLS] strobeColor group={currentEventData.groupId} element={currentEventData.elementId} time={currentEventData.time:F4} from={customStrobeColor.HasValue} to={nextCustomStrobeColor.HasValue}");
//             }


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
                    // Confirmed after testing: removing this makes instant nodes transition to later colors at the wrong time.
                    InvokeOriginalSetColor(__instance, 0f);

// #if !PRE_V1_37_1
//                     // DIAGNOSTICS ONLY: record the endpoints after all custom no-tween mutations have completed.
//                     if (forceNoTweenColor
//                         && currentEventData.groupId == 3
//                         && currentEventData.elementId == 0
//                         && currentEventData.time <= 1.2f)
//                     {
//                         Plugin.Log.Info($"[ChromaGLS startup-final] frame={Time.frameCount} realtime={Time.realtimeSinceStartup:F4} lightId={lightId} current={currentEventData.time:F4} frequency={currentEventData.strobeBeatFrequency} fieldFrom={fromField} fieldTo={toField} altFrom={alternativeFromField} altTo={alternativeToField}");
//                     }
// #endif

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
            // 1.29.1 can defer the first legacy tween frame; explicitly render t=0 so faded strobes do not start already mixed.
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
        // PRODUCTION HOT PATH, 1.29.1 ONLY: backport modern phase/brightness behavior that legacy SetData fields cannot express.
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
            StrobeColorStates.TryGetValue(__instance, out StrobeColorState? colorState);
            LegacyStrobeStates.TryGetValue(__instance, out LegacyStrobeState? state);
            // 1.29.1 can defer the first legacy tween update; render that one explicit faded-strobe frame from t=0.
            float outputT = t;
            if (colorState is { HasExplicitStrobeColor: true }
                && state is { Fade: true, InitialFrameRendered: false })
            {
                outputT = 0f;
                state.InitialFrameRendered = true;
            }

            // Match the modern path by restoring custom normal-color endpoints instead of lerping stale native fields.
            Color normalFrom = colorState?.NormalFrom ?? ____fromColor;
            Color normalTo = colorState?.NormalTo ?? ____toColor;
            Color color = Color.LerpUnclamped(normalFrom, normalTo, outputT);

            if (____fromStrobeFrequency <= 0f && ____toStrobeFrequency <= 0f)
            {
                // Native 1.29.1 SetColor already handles non-strobe custom RGB fields and preserves its exact transition timing.
                SetLegacyFogCompensation(__instance, 1f);
                return true;
            }

            // float diagnosticStrobeBrightness = 0f;
            // // DIAGNOSTICS ONLY: retain maximum tween progress for timing logs.
            // state?.RecordProgress(t, ____floatTween.duration);
            if (____fromStrobeFrequency > 0f || ____toStrobeFrequency > 0f)
            {
                float strobeBrightness = state == null
                    ? 0f
                    : Mathf.LerpUnclamped(state.FromBrightness, state.ToBrightness, outputT);
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

                // Custom strobe endpoints compose alpha with sb before tweening; native fallback retains its interpolated normal RGB.
                Color strobeColor = colorState is { RequiresCustomRendering: true } && state != null
                    ? colorState.GetColor(outputT, state.FromBrightness, state.ToBrightness)
                    : WithAlpha(color, strobeBrightness);
                if (state is { Fade: true })
                {
                    float fade = InOutCubic(1f - Mathf.Abs((phase * 2f) - 1f));
                    // Straight RGBA interpolation preserves every HDR channel for downstream legacy material and fog handling.
                    color = Color.LerpUnclamped(color, strobeColor, fade);
                }
                else if (phase >= 0.5f)
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

// #if false
//         // DIAGNOSTICS ONLY: inspect legacy renderer output after SetColorForId; no values are mutated.
//         private static string GetLegacyRendererState(object lightManager, int lightId)
//         {
//             // Capture actual renderer state after SetColorForId to locate any downstream group-specific visual delay.
//             FieldInfo lightsField = lightManager.GetType().GetField("_lights", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
//             if (lightsField?.GetValue(lightManager) is not Array lights
//                 || lights.GetValue(lightId) is not IEnumerable renderers)
//             {
//                 return "missing";
//             }
//
//             List<string> states = new();
//             foreach (object renderer in renderers)
//             {
//                 Type? rendererType = renderer?.GetType();
//                 if (rendererType == null)
//                 {
//                     continue;
//                 }
//
//                 if (rendererType.FullName == "TubeBloomPrePassLightWithId")
//                 {
//                     object tube = rendererType.GetField("_tubeBloomPrePassLight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
//                     Type? tubeType = tube?.GetType();
//                     object? tubeColor = tubeType?.GetField("_color", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tube);
//                     object? bloomMultiplier = tubeType?.GetField("_bloomFogIntensityMultiplier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tube);
//                     states.Add($"tube(color={tubeColor},bloom={bloomMultiplier})");
//                     continue;
//                 }
//
//                 // Read the disabled diagnostic renderer color without changing its state.
//                 object? rendererColor = rendererType.GetField("_color", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
//                 states.Add($"{rendererType.FullName ?? rendererType.Name}(color={rendererColor})");
//             }
//
//             return string.Join(";", states);
//         }
// #endif

        // Discovers the 1.29.1 tube renderer once after native event setup.
        // HMRendering is publicized for this target, so concrete renderer types are available directly.
        // Called from ApplyCustomColors only during the HandleColorChangeBeatmapEvent postfix (forceNoTweenColor == true).
        // Subsequent calls for the same instance are no-ops until Cleanup removes the entry from LegacyFogStates.
        private static void PrepareLegacyFogState(
            LightColorGroupEffect instance,
            LightWithIdManager lightManager,
            int lightId)
        {
            // Event-time guard: at most once per effect instance; per-frame work is delegated to SetLegacyFogCompensation.
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
                // Scale only the cached legacy fog multiplier; renderer RGBA and strobe brightness remain unchanged.
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

            public Color GetColor(float t, float fromStrobeBrightness, float toStrobeBrightness)
            {
                // Compose each endpoint before interpolation so HDR alpha follows basic-event ColorTween semantics instead of multiplying two tweens.
                Color from = WithAlpha(From, From.a * fromStrobeBrightness);
                Color to = WithAlpha(To, To.a * toStrobeBrightness);
                return Color.LerpUnclamped(from, to, t);
            }
        }

// #if false
//         // DIAGNOSTICS ONLY: inspect material renderer configuration; this method never mutates renderer state.
//         private static void LogMaterialStrobeState(object lightManager, int lightId, string path, float t, Color? input)
//         {
//             if (MaterialStrobeDiagnosticCount >= 20000)
//             {
//                 return;
//             }
//
//             FieldInfo lightsField = lightManager.GetType().GetField("_lights", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
//             if (lightsField?.GetValue(lightManager) is not Array lights
//                 || lights.GetValue(lightId) is not IEnumerable renderers)
//             {
//                 return;
//             }
//
//             foreach (object renderer in renderers)
//             {
//                 Type? rendererType = renderer?.GetType();
//                 if (rendererType?.FullName != "MaterialLightWithId")
//                 {
//                     continue;
//                 }
//
//                 object? color = rendererType.GetField("_color", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
//                 object? setAlphaOnly = rendererType.GetField("_setAlphaOnly", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
//                 object? alphaIntoColor = rendererType.GetField("_alphaIntoColor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
//                 object? setColorOnly = rendererType.GetField("_setColorOnly", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
//                 object? alphaIntensity = rendererType.GetField("_alphaIntensity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
//                 object? multiplyColorWithAlpha = rendererType.GetField("_multiplyColorWithAlpha", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
//                 object? multiplyColor = rendererType.GetField("_multiplyColor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
//                 object? colorMultiplier = rendererType.GetField("_colorMultiplier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(renderer);
//                 Plugin.Log.Info($"[ChromaGLS material-strobe] path={path} lightId={lightId} t={t:F4} input={input} result={color} setAlphaOnly={setAlphaOnly} alphaIntoColor={alphaIntoColor} setColorOnly={setColorOnly} alphaIntensity={alphaIntensity} multiplyColorWithAlpha={multiplyColorWithAlpha} multiplyColor={multiplyColor} colorMultiplier={colorMultiplier}");
//                 MaterialStrobeDiagnosticCount++;
//                 break;
//             }
//         }
// #endif

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

        private static LightColorBeatmapEventData? FindNextEventInSameBox(LightColorBeatmapEventData currentEventData)
        {
            return GlsConverterPatches.TryGetNextEventInBox(currentEventData, out LightColorBeatmapEventData? nextEvent)
                ? nextEvent
                : currentEventData.nextSameTypeEventData as LightColorBeatmapEventData;
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
