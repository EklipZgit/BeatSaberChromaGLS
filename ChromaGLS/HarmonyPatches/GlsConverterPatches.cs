using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CustomJSONData.CustomBeatmap;
using HarmonyLib;

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

        [HarmonyPostfix]
        [HarmonyPatch(
            typeof(BeatmapDataLoaderVersion3.BeatmapDataLoader.LightColorEventBoxConverter),
            "Convert")]
        private static void LightColorEventBoxConverterPostfix(
            BeatmapSaveDataVersion3.LightColorEventBox saveData,
            BeatmapEventDataBox __result)
        {
            if (__result is not LightColorBeatmapEventDataBox box)
            {
                return;
            }

            List<CustomData?> perEventData = new();
            bool anyCustom = false;
            foreach (BeatmapSaveDataVersion3.LightColorBaseData item in saveData.lightColorBaseDataList ?? new List<BeatmapSaveDataVersion3.LightColorBaseData>())
            {
                if (item is ICustomData cd && cd.customData.Count > 0)
                {
                    perEventData.Add(cd.customData);
                    anyCustom = true;
                }
                else
                {
                    perEventData.Add(null);
                }
            }

            if (anyCustom)
            {
                _boxCustomData.Add(box, perEventData);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(LightColorBeatmapEventDataBox), "Unpack")]
        private static void LightColorBeatmapEventDataBoxUnpackPostfix(
            LightColorBeatmapEventDataBox __instance,
            List<BeatmapEventData> output)
        {
            if (!_boxCustomData.TryGetValue(__instance, out List<CustomData?> perEventData))
            {
                return;
            }

            // output may contain events from multiple boxes (appended); scan from the end
            // matching the count of items we know this box produced.
            int boxCount = perEventData!.Count;
            int outputStart = output.Count - boxCount;
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
                if (output[outIdx] is not LightColorBeatmapEventData ev || ev is CustomLightColorBeatmapEventData)
                {
                    continue;
                }

                output[outIdx] = new CustomLightColorBeatmapEventData(
                    ev.time,
                    ev.groupId,
                    ev.elementId,
                    ev.usePreviousValue,
                    ev.easeType,
                    ev.colorType,
                    ev.brightness,
                    ev.strobeBeatFrequency,
                    ev.strobeBrightness,
                    ev.strobeFade,
                    customData);
            }
        }
    }
}
