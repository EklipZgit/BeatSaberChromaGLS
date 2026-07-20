using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CustomJSONData.CustomBeatmap;
using HarmonyLib;
#if !PRE_V1_37_1
using _LightColorBaseData = BeatmapSaveDataVersion3.LightColorBaseData;
using _LightColorEventBox = BeatmapSaveDataVersion3.LightColorEventBox;
using _LightColorEventBoxConverter = BeatmapDataLoaderVersion3.BeatmapDataLoader.LightColorEventBoxConverter;
#else
using _LightColorBaseData = BeatmapSaveDataVersion3.BeatmapSaveData.LightColorBaseData;
using _LightColorEventBox = BeatmapSaveDataVersion3.BeatmapSaveData.LightColorEventBox;
using _LightColorEventBoxConverter = BeatmapDataLoader.LightColorEventBoxConvertor;
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

        private static readonly ConditionalWeakTable<BeatmapEventData, LightColorBeatmapEventDataBox> _eventOrigins
            = new();

        // Bounded diagnostics identify custom-data ownership before changing the 1.29.1 mapping again.
        private static int ConverterDiagnosticCount;

        internal static bool TryGetEventOrigin(
            BeatmapEventData eventData,
            out LightColorBeatmapEventDataBox origin)
        {
            return _eventOrigins.TryGetValue(eventData, out origin);
        }

        // Unpack skips base nodes whose calculated beat is at or beyond maxBeat on every supported version.
        // The emitted-node predicate must be shared with the custom-data mapping or indices drift into later boxes.
        private static readonly FieldInfo BaseDataListField =
            typeof(LightColorBeatmapEventDataBox).GetField("_lightColorBaseDataList", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo BeatStepField =
            typeof(LightColorBeatmapEventDataBox).GetField("_beatStep", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo BaseDataBeatField =
            BaseDataListField.FieldType.GetGenericArguments()[0].GetField("beat", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

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
                bool isExtension = Convert.ToInt32(item.transitionType) == 2;
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
            if (ConverterDiagnosticCount++ < 80)
            {
                string customFlags = string.Join(",", perEventData.Select(data => data == null ? "0" : "1"));
                string beats = string.Join(",", saveData.lightColorBaseDataList.Select(data => data.beat.ToString("F3")));
                Plugin.Log.Info($"[ChromaGLS] 1.29 Convert box={box.GetHashCode()} count={perEventData.Count} anyCustom={anyCustom} flags={customFlags} beats={beats}");
            }
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

            // Reproduce the game's predicate so custom data follows emitted nodes rather than raw node indices.
            IList baseData = (IList)BaseDataListField.GetValue(__instance);
            float beatStep = (float)BeatStepField.GetValue(__instance);
            List<CustomData?> emittedCustomData = new();
            for (int i = 0; i < baseData.Count; i++)
            {
                object baseDataItem = baseData[i];
                float baseBeat = (float)BaseDataBeatField.GetValue(baseDataItem);
                float beat = groupBoxBeat + baseBeat + (durationOrderIndex * beatStep);
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
            if (ConverterDiagnosticCount++ < 160)
            {
                string customFlags = string.Join(",", perEventData.Select(data => data == null ? "0" : "1"));
                Plugin.Log.Info($"[ChromaGLS] 1.29 Unpack box={__instance.GetHashCode()} groupBeat={groupBoxBeat:F3} maxBeat={maxBeat:F3} duration={durationOrderIndex} outputBefore={outputList.Count - boxCount} emitted={boxCount} flags={customFlags}");
            }
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
                _eventOrigins.Add(customEvent, __instance);
            }
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
