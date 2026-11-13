using ChromaGLS.Helpers;
using HarmonyLib;
using Tweening;
using UnityEngine;

namespace ChromaGLS.HarmonyPatches
{
    [HarmonyPatch(typeof(LightTranslationGroupEffect), nameof(LightTranslationGroupEffect.GetTranslationEventHandler))]
    internal static class GlsSplineTranslationEventPatch
    {
        private static void Postfix(
            FloatTween translationTween,
            Vector2 translationLimits,
            Vector2 distributionLimits,
            bool mirrored,
            ref BeatmapDataCallback<LightTranslationBeatmapEventData> __result)
        {
            var callback = __result;
            var tween = (GlsFloatTween)translationTween;
            __result = current =>
            {
                GlsSplineSegment.Translation(current, translationLimits, distributionLimits, mirrored).Configure(tween);
                callback(current);
            };
        }
    }

    [HarmonyPatch(typeof(LightRotationGroupEffect), nameof(LightRotationGroupEffect.HandleRotationChangeBeatmapEvent))]
    internal static class GlsSplineRotationEventPatch
    {
        private static void Prefix(LightRotationBeatmapEventData currentEventData, FloatTween ____rotationTween) =>
            GlsSplineSegment.Rotation(currentEventData).Configure((GlsFloatTween)____rotationTween);
    }

#if !V1_29_1
    [HarmonyPatch(typeof(FloatFxGroupEffect), nameof(FloatFxGroupEffect.HandleFloatFxBeatmapEventCallback))]
    internal static class GlsSplineFloatFxEventPatch
    {
        private static void Prefix(FloatFxBeatmapEventData currentEvent, FloatTween ____floatTween) =>
            GlsSplineSegment.FloatFx(currentEvent).Configure((GlsFloatTween)____floatTween);
    }
#endif
}
