using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CustomJSONData.CustomBeatmap;
using HarmonyLib;

// Version-conditional Harmony patch nesting intentionally places members in this order; the disabled diagnostic marker is retained for context.
#pragma warning disable SA1201, SA1204, SA1512

#if !PRE_V1_37_1
using _LightColorBaseData = BeatmapSaveDataVersion3.LightColorBaseData;
using _LightColorEventBox = BeatmapSaveDataVersion3.LightColorEventBox;
using _LightColorEventBoxConverter = BeatmapDataLoaderVersion3.BeatmapDataLoader.LightColorEventBoxConverter;
using _TransitionType = BeatmapSaveDataVersion3.TransitionType;
#else
using _LightColorBaseData = BeatmapSaveDataVersion3.BeatmapSaveData.LightColorBaseData;
using _LightColorEventBox = BeatmapSaveDataVersion3.BeatmapSaveData.LightColorEventBox;
using _LightColorEventBoxConverter = BeatmapDataLoader.LightColorEventBoxConvertor;
using _TransitionType = BeatmapSaveDataVersion3.BeatmapSaveData.TransitionType;
#endif

namespace ChromaGLS.HarmonyPatches
{
    // Part B of the CustomJSONData GLS branch, replicated on the game types (no CJD source changes).
    //
    // For V3 GLS, LightColorBeatmapEventData instances are NOT created in
    // LightColorEventBoxConverter.Convert. Instead:
    //   1. Convert(LightColorEventBox, ILightGroup) turns the ICustomData save-data items into
    //      plain LightColorBaseData game-structs (custom data is dropped) inside a
    //      LightColorBeatmapEventDataBox._lightColorBaseDataList.
    //   2. LightColorBeatmapEventDataBox.Unpack(...) is called later (per event at play time) and
    //      creates LightColorBeatmapEventData items from those plain structs. At that point there is
    //      nothing carrying ICustomData.
    //
    // Solution (two postfixes + ConditionalWeakTable):
    //   - Postfix Convert to read ICustomData from the (still intact) save-data list and stash
    //     per-index CustomData keyed by the returned LightColorBeatmapEventDataBox.
    //   - Postfix Unpack to look the box up and replace output LightColorBeatmapEventData items
    //     with CustomLightColorBeatmapEventData carrying the custom data.
    [HarmonyPatch]
    internal static class GlsConverterPatches
    {
        // Maps each LightColorBeatmapEventDataBox instance -> ordered list of per-event CustomData
        // (null entry = no custom data for that event index).
        private static readonly ConditionalWeakTable<LightColorBeatmapEventDataBox, List<CustomData?>> _boxCustomData
            = new();

        private static readonly ConditionalWeakTable<BeatmapEventData, NextEventInBox> _nextEventsInBox
            = new();

        // Bounded diagnostics identify custom-data ownership before changing the 1.29.1 mapping again.
        // Disabled because per-box list formatting may contribute to slow 1.29.1 map loading.
        // private static int ConverterDiagnosticCount;

        internal static bool TryGetNextEventInBox(
            BeatmapEventData eventData,
            out LightColorBeatmapEventData? nextEvent)
        {
            if (_nextEventsInBox.TryGetValue(eventData, out NextEventInBox result))
            {
                nextEvent = result.EventData;
                return true;
            }

            nextEvent = null;
            return false;
        }

        // Target the protected LightColorEventBox converter overload explicitly; the base converter also exposes Convert.
        private static void LightColorEventBoxConverterPostfix(
            _LightColorEventBox saveData,
            BeatmapEventDataBox __result)
        {
            if (__result is not LightColorBeatmapEventDataBox box)
            {
                return;
            }

            List<CustomData?> perEventData = new();
            bool anyCustom = false;
            CustomData? previousEffectiveCustomData = null;
            foreach (_LightColorBaseData item in saveData.lightColorBaseDataList ?? new List<_LightColorBaseData>())
            {
                // Extension nodes are exact copies within this event box/filter lane, including the predecessor's Chroma payload.
                bool isExtension = item.transitionType == _TransitionType.Extend;
                CustomData? effectiveCustomData = isExtension
                    ? previousEffectiveCustomData
                    : item is ICustomData cd && cd.customData.Count > 0
                        ? cd.customData
                        : null;
                perEventData.Add(effectiveCustomData);
                previousEffectiveCustomData = effectiveCustomData;
                anyCustom |= effectiveCustomData != null;
            }

            if (anyCustom)
            {
                _boxCustomData.Add(box, perEventData);
            }

#if V1_29_1
            // if (ConverterDiagnosticCount++ < 80)
            // {
            //     string customFlags = string.Join(",", perEventData.Select(data => data == null ? "0" : "1"));
            //     string beats = string.Join(",", saveData.lightColorBaseDataList.Select(data => data.beat.ToString("F3")));
            //     Plugin.Log.Info($"[ChromaGLS] 1.29 Convert box={box.GetHashCode()} count={perEventData.Count} anyCustom={anyCustom} flags={customFlags} beats={beats}");
            // }
#endif
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(LightColorBeatmapEventDataBox), "Unpack")]
#if PRE_V1_42_1
        private static void LightColorBeatmapEventDataBoxUnpackPostfix(
            LightColorBeatmapEventDataBox __instance,
            float groupBoxBeat,
            int durationOrderIndex,
            float maxBeat,
            List<BeatmapEventData> output)
#else
        private static void LightColorBeatmapEventDataBoxUnpackPostfix(
            LightColorBeatmapEventDataBox __instance,
            float groupBoxBeat,
            int durationOrderIndex,
            float maxBeat,
            ref IEnumerable<BeatmapEventData> __result)
#endif
        {
            if (!_boxCustomData.TryGetValue(__instance, out List<CustomData?> perEventData))
            {
                return;
            }

            // Main.dll is publicized, so read the same private data directly rather than reflecting every emitted node.
            // Reproduce the game's predicate so custom data follows emitted nodes rather than raw node indices.
            IReadOnlyList<LightColorBaseData> baseData = __instance._lightColorBaseDataList;
            float beatStep = __instance._beatStep;
            List<CustomData?> emittedCustomData = new();
            for (int i = 0; i < baseData.Count; i++)
            {
                float beat = groupBoxBeat + baseData[i].beat + (durationOrderIndex * beatStep);
                if (beat < maxBeat)
                {
                    emittedCustomData.Add(perEventData![i]);
                }
            }

            perEventData = emittedCustomData;
            int boxCount = perEventData.Count;
#if PRE_V1_42_1
            List<BeatmapEventData> outputList = output;
#else
            List<BeatmapEventData> outputList = __result?.ToList() ?? new List<BeatmapEventData>();
            __result = outputList;
#endif
#if V1_29_1
            // if (ConverterDiagnosticCount++ < 160)
            // {
            //     string customFlags = string.Join(",", perEventData.Select(data => data == null ? "0" : "1"));
            //     Plugin.Log.Info($"[ChromaGLS] 1.29 Unpack box={__instance.GetHashCode()} groupBeat={groupBoxBeat:F3} maxBeat={maxBeat:F3} duration={durationOrderIndex} outputBefore={outputList.Count - boxCount} emitted={boxCount} flags={customFlags}");
            // }
#endif
            int outputStart = outputList.Count - boxCount;
            if (outputStart < 0)
            {
                return;
            }

            for (int i = 0; i < boxCount; i++)
            {
                CustomData? customData = perEventData[i];
                if (customData == null)
                {
                    continue;
                }

                int outIdx = outputStart + i;
                if (outputList[outIdx] is not LightColorBeatmapEventData ev || ev is CustomLightColorBeatmapEventData)
                {
                    continue;
                }

                CustomLightColorBeatmapEventData customEvent = new(
                    ev.time,
                    ev.groupId,
                    ev.elementId,
#if PRE_V1_37_1
                    ev.transitionType,
#else
                    ev.usePreviousValue,
                    ev.easeType,
#endif
                    ev.colorType,
                    ev.brightness,
                    ev.strobeBeatFrequency,
#if !V1_29_1
                    ev.strobeBrightness,
                    ev.strobeFade,
#endif
                    customData);
                outputList[outIdx] = customEvent;
            }

            // Precompute the box-local custom event chain once instead of scanning the global event chain per callback.
            LightColorBeatmapEventData? nextEventInBox = null;
            for (int i = boxCount - 1; i >= 0; i--)
            {
                if (outputList[outputStart + i] is not CustomLightColorBeatmapEventData customEvent)
                {
                    continue;
                }

                _nextEventsInBox.Add(customEvent, new NextEventInBox(nextEventInBox));
                nextEventInBox = customEvent;
            }
        }

        private sealed class NextEventInBox
        {
            public NextEventInBox(LightColorBeatmapEventData? eventData)
            {
                EventData = eventData;
            }

            public LightColorBeatmapEventData? EventData { get; }
        }

        [HarmonyPatch]
        private static class LightColorEventBoxConverterPatch
        {
            [HarmonyTargetMethod]
            private static MethodBase TargetMethod()
            {
                return AccessTools.GetDeclaredMethods(typeof(_LightColorEventBoxConverter))
                    .Single(method => method.Name == "Convert"
                        && method.GetParameters().Length == 2
                        && method.GetParameters()[0].ParameterType == typeof(_LightColorEventBox));
            }

            [HarmonyPostfix]
            private static void Postfix(
                _LightColorEventBox saveData,
                BeatmapEventDataBox __result)
            {
                LightColorEventBoxConverterPostfix(saveData, __result);
            }
        }
    }
}
