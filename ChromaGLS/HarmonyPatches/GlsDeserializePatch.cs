using System.Collections.Generic;
using CustomJSONData;
using CustomJSONData.CustomBeatmap;
using HarmonyLib;
using Newtonsoft.Json;
using _DistributionParamType = BeatmapSaveDataCommon.DistributionParamType;
using _EaseType = BeatmapSaveDataCommon.EaseType;
using _EnvironmentColorType = BeatmapSaveDataCommon.EnvironmentColorType;
using _IndexFilter = BeatmapSaveDataVersion3.IndexFilter;
using _LightColorBaseData = BeatmapSaveDataVersion3.LightColorBaseData;
using _LightColorEventBox = BeatmapSaveDataVersion3.LightColorEventBox;
using _LightColorEventBoxGroup = BeatmapSaveDataVersion3.LightColorEventBoxGroup;
using _TransitionType = BeatmapSaveDataVersion3.TransitionType;

namespace ChromaGLS.HarmonyPatches
{
    // Prefix-skip replacement for CustomJSONData's public
    // Version3CustomBeatmapSaveData.DeserializeLightColorEventBoxGroupArray.
    //
    // This is a verbatim clone of CJD master's method body, plus the single addition that reads
    // per-light-color-base-data "customData" and constructs our LightColorBaseDataSaveData
    // (which implements ICustomData) instead of a plain LightColorBaseData.
    //
    // We only clone THIS one public method; the rest of CJD's deserializer pipeline is untouched
    // and still calls into this (now patched) method. Because the method fills the passed `list`
    // (a reference type) and we return false, the original never runs and the reader is advanced
    // identically. This keeps the ICustomData all the way through to CustomLightColorBeatmapEventData
    // so no time-based color matching is ever needed.
    //
    // Constant literals below correspond to CJD's private consts:
    //   "b" = beat, "g" = groupId, "e" = eventBoxes, "f" = indexFilter,
    //   "w" = beatDistributionParam, "d" = beatDistributionParamType,
    //   "c" = colorType, "customData" = customData.
    [HarmonyPatch(typeof(Version3CustomBeatmapSaveData), nameof(Version3CustomBeatmapSaveData.DeserializeLightColorEventBoxGroupArray))]
    internal static class GlsDeserializePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(JsonReader reader, List<_LightColorEventBoxGroup> list)
        {
            reader.ReadArray(() =>
            {
                float beat = default;
                List<_LightColorEventBox> eventBoxes = new();
                int groupId = default;
                CustomData data = new();
                return reader.ReadObject(objectName =>
                {
                    switch (objectName)
                    {
                        case "b":
                            beat = (float?)reader.ReadAsDouble() ?? beat;
                            break;

                        case "g":
                            groupId = reader.ReadAsInt32Safe() ?? groupId;
                            break;

                        case "e":
                            reader.ReadArray(() =>
                            {
                                _IndexFilter? indexFilter = default;
                                float beatDistributionParam = default;
                                _DistributionParamType beatDistributionParamType = default;
                                float brightnessDistributionParam = default;
                                bool brightnessDistributionShouldAffectFirstBaseEvent = default;
                                _DistributionParamType brightnessDistributionParamType = default;
                                _EaseType brightnessDistributionEaseType = default;
                                List<_LightColorBaseData> lightColorBaseDataList = new();
                                return reader.ReadObject(eventName =>
                                {
                                    switch (eventName)
                                    {
                                        case "f":
                                            indexFilter = Version3CustomBeatmapSaveData.DeserializeIndexFilter(reader);
                                            break;

                                        case "w":
                                            beatDistributionParam =
                                                (float?)reader.ReadAsDouble() ?? beatDistributionParam;
                                            break;

                                        case "d":
                                            beatDistributionParamType =
                                                (_DistributionParamType?)reader.ReadAsInt32Safe() ??
                                                beatDistributionParamType;
                                            break;

                                        case "r":
                                            brightnessDistributionParam = (float?)reader.ReadAsDouble() ??
                                                                          brightnessDistributionParam;
                                            break;

                                        case "b":
                                            brightnessDistributionShouldAffectFirstBaseEvent =
                                                reader.ReadIntAsBoolean() ??
                                                brightnessDistributionShouldAffectFirstBaseEvent;
                                            break;

                                        case "t":
                                            brightnessDistributionParamType =
                                                (_DistributionParamType?)reader.ReadAsInt32Safe() ??
                                                brightnessDistributionParamType;
                                            break;

                                        case "i":
                                            brightnessDistributionEaseType = (_EaseType?)reader.ReadAsInt32Safe() ??
                                                                             brightnessDistributionEaseType;
                                            break;

                                        case "e":
                                            reader.ReadArray(() =>
                                            {
                                                float lightBeat = default;
                                                _TransitionType transitionType = default;
                                                _EnvironmentColorType colorType = default;
                                                float brightness = default;
                                                int strobeFrequency = default;
                                                float strobeBrightness = default;
                                                bool strobeFade = default;
                                                CustomData lightData = new();
                                                return reader.ReadObject(lightName =>
                                                {
                                                    switch (lightName)
                                                    {
                                                        case "b":
                                                            lightBeat = (float?)reader.ReadAsDouble() ?? lightBeat;
                                                            break;

                                                        case "i":
                                                            transitionType =
                                                                (_TransitionType?)reader.ReadAsInt32Safe() ??
                                                                transitionType;
                                                            break;

                                                        case "c":
                                                            colorType =
                                                                (_EnvironmentColorType?)reader.ReadAsInt32Safe() ??
                                                                colorType;
                                                            break;

                                                        case "s":
                                                            brightness = (float?)reader.ReadAsDouble() ?? brightness;
                                                            break;

                                                        case "f":
                                                            strobeFrequency = reader.ReadAsInt32Safe() ??
                                                                              strobeFrequency;
                                                            break;

                                                        case "sb":
                                                            strobeBrightness = (float?)reader.ReadAsDouble() ??
                                                                               strobeBrightness;
                                                            break;

                                                        case "sf":
                                                            strobeFade = reader.ReadIntAsBoolean() ??
                                                                         strobeFade;
                                                            break;

                                                        case "customData":
                                                            reader.ReadToDictionary(lightData);
                                                            break;

                                                        default:
                                                            reader.Skip();
                                                            break;
                                                    }
                                                }).Finish(() => lightColorBaseDataList.Add(new LightColorBaseDataSaveData(
                                                    lightBeat,
                                                    transitionType,
                                                    colorType,
                                                    brightness,
                                                    strobeFrequency,
                                                    strobeBrightness,
                                                    strobeFade,
                                                    lightData)));
                                            });
                                            break;

                                        default:
                                            reader.Skip();
                                            break;
                                    }
                                }).Finish(() => eventBoxes.Add(new _LightColorEventBox(
                                    indexFilter,
                                    beatDistributionParam,
                                    beatDistributionParamType,
                                    brightnessDistributionParam,
                                    brightnessDistributionShouldAffectFirstBaseEvent,
                                    brightnessDistributionParamType,
                                    brightnessDistributionEaseType,
                                    lightColorBaseDataList)));
                            });
                            break;

                        case "customData":
                            reader.ReadToDictionary(data);
                            break;

                        default:
                            reader.Skip();
                            break;
                    }
                }).Finish(() => list.Add(new Version3CustomBeatmapSaveData.LightColorEventBoxGroupSaveData(beat, groupId, eventBoxes, data)));
            });

            return false;
        }
    }
}
