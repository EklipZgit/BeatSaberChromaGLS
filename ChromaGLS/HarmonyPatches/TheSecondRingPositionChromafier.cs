using CustomJSONData.CustomBeatmap;
using HarmonyLib;
using Tweening;
using UnityEngine;

namespace ChromaGLS.HarmonyPatches
{
    // The Second uses SmoothStepPositionGroupEventEffect, whose i value is a position scalar rather than the normal ring spawner's step selector.
    [HarmonyPatch]
    internal static class TheSecondRingPositionChromafier
    {
        private const string StepKey = "step";

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SmoothStepPositionGroupEventEffect), "HandleBeatmapEvent")]
        private static bool HandleBeatmapEventPrefix(
            SmoothStepPositionGroupEventEffect __instance,
            BasicBeatmapEventData basicBeatmapEventData,
            Vector3 ____movementVector,
            float ____stepSize,
            Vector3 ____baseOffset,
            bool ____clampValue,
            int ____eventValueMin,
            int ____eventValueMax,
            Vector3Tween ____positionTween,
            SongTimeTweeningManager ____tweeningManager)
        {
            ____positionTween.Kill();
            ____positionTween.fromValue = GetPositionForEvent(
                basicBeatmapEventData,
                ____movementVector,
                ____stepSize,
                ____baseOffset,
                ____clampValue,
                ____eventValueMin,
                ____eventValueMax);
            ____positionTween.toValue = ____positionTween.fromValue;

            BasicBeatmapEventData? nextEvent = basicBeatmapEventData.nextSameTypeEventData;
            if (nextEvent == null)
            {
                return false;
            }

            ____positionTween.toValue = GetPositionForEvent(
                nextEvent,
                ____movementVector,
                ____stepSize,
                ____baseOffset,
                ____clampValue,
                ____eventValueMin,
                ____eventValueMax);

            // The Second has no native ring-speed parameter, so retain its next-event timing.
            ____positionTween.SetStartTimeAndEndTime(basicBeatmapEventData.time, nextEvent.time);
            ____tweeningManager.ResumeTween(____positionTween, __instance);

            return false;
        }

        private static Vector3 GetPositionForEvent(
            BasicBeatmapEventData eventData,
            Vector3 movementVector,
            float defaultStep,
            Vector3 baseOffset,
            bool clampValue,
            int eventValueMin,
            int eventValueMax)
        {
            int value = eventData.value;
            if (clampValue)
            {
                value = Mathf.Clamp(value, eventValueMin, eventValueMax);
            }

            float step = GetCustomValue(eventData, StepKey) ?? defaultStep;
            return (movementVector * (step * value)) + baseOffset;
        }

        private static float? GetCustomValue(BasicBeatmapEventData? eventData, string key)
        {
            return eventData is CustomBasicBeatmapEventData customEvent
                ? customEvent.customData.Get<float?>(key)
                : null;
        }
    }
}
