using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using CustomJSONData;
using CustomJSONData.CustomBeatmap;
using HarmonyLib;
using Newtonsoft.Json;
#if !PRE_V1_37_1
using _DistributionParamType = BeatmapSaveDataCommon.DistributionParamType;
using _EaseType = BeatmapSaveDataCommon.EaseType;
using _EnvironmentColorType = BeatmapSaveDataCommon.EnvironmentColorType;
using _IndexFilter = BeatmapSaveDataVersion3.IndexFilter;
using _LightColorBaseData = BeatmapSaveDataVersion3.LightColorBaseData;
using _LightColorEventBox = BeatmapSaveDataVersion3.LightColorEventBox;
using _LightColorEventBoxGroup = BeatmapSaveDataVersion3.LightColorEventBoxGroup;
using _TransitionType = BeatmapSaveDataVersion3.TransitionType;
#else
using _DistributionParamType = BeatmapSaveDataVersion3.BeatmapSaveData.EventBox.DistributionParamType;
using _EaseType = BeatmapSaveDataVersion3.BeatmapSaveData.EaseType;
using _EnvironmentColorType = BeatmapSaveDataVersion3.BeatmapSaveData.EnvironmentColorType;
using _IndexFilter = BeatmapSaveDataVersion3.BeatmapSaveData.IndexFilter;
using _LightColorBaseData = BeatmapSaveDataVersion3.BeatmapSaveData.LightColorBaseData;
using _LightColorEventBox = BeatmapSaveDataVersion3.BeatmapSaveData.LightColorEventBox;
using _LightColorEventBoxGroup = BeatmapSaveDataVersion3.BeatmapSaveData.LightColorEventBoxGroup;
using _TransitionType = BeatmapSaveDataVersion3.BeatmapSaveData.TransitionType;
#endif
#if !PRE_V1_37_1
using _SaveData = CustomJSONData.CustomBeatmap.Version3CustomBeatmapSaveData;
#endif

#pragma warning disable SA1202

namespace ChromaGLS.HarmonyPatches
{
    // Replace the V3 GLS reader so customData stays attached to each light event through conversion.
    // Fill the supplied list and advance the reader here because the original parser is skipped.
#if PRE_V1_37_1
    [HarmonyPatch("CustomJSONData.CustomBeatmap.CustomBeatmapSaveData", "DeserializeLightColorEventBoxGroupArray")]
#else
    [HarmonyPatch(typeof(_SaveData), nameof(_SaveData.DeserializeLightColorEventBoxGroupArray))]
#endif
    internal static class GlsDeserializePatch
    {
        // Retain box customData until the converter creates the runtime box.
        private static readonly ConditionalWeakTable<_LightColorEventBox, CustomData> BoxCustomData = new();

#if PRE_V1_37_1
        // Cache the legacy CJD parser delegate because this is called once per event box on the map-loading hot path.
        private static readonly Func<JsonReader, _IndexFilter> DeserializeIndexFilterDelegate = CreateDeserializeIndexFilterDelegate();

        private static Func<JsonReader, _IndexFilter> CreateDeserializeIndexFilterDelegate()
        {
            Type saveDataType = AccessTools.TypeByName("CustomJSONData.CustomBeatmap.CustomBeatmapSaveData")
                ?? AccessTools.TypeByName("CustomJSONData.CustomBeatmap.Custom2_6_0AndEarlierBeatmapSaveData");
            MethodInfo method = saveDataType.GetMethod(
                "DeserializeIndexFilter",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return (Func<JsonReader, _IndexFilter>)method.CreateDelegate(typeof(Func<JsonReader, _IndexFilter>));
        }
#endif

        internal static bool TryGetBoxCustomData(_LightColorEventBox box, out CustomData? customData)
        {
            return BoxCustomData.TryGetValue(box, out customData);
        }

        private static _IndexFilter? DeserializeIndexFilter(JsonReader reader)
        {
#if PRE_V1_37_1
            return DeserializeIndexFilterDelegate(reader);
#else
            return _SaveData.DeserializeIndexFilter(reader);
#endif
        }

        private static _LightColorBaseData CreateLightColorBaseData(
            float beat,
            _TransitionType transitionType,
            _EnvironmentColorType colorType,
            float brightness,
            int strobeBeatFrequency,
#if !V1_29_1
            float strobeBrightness,
            bool strobeFade,
#endif
            CustomData? customData)
        {
            if (customData == null || customData.Count == 0)
            {
#if V1_29_1
                return new _LightColorBaseData(
                    beat,
                    transitionType,
                    colorType,
                    brightness,
                    strobeBeatFrequency);
#else
                return new _LightColorBaseData(
                    beat,
                    transitionType,
                    colorType,
                    brightness,
                    strobeBeatFrequency,
                    strobeBrightness,
                    strobeFade);
#endif
            }

#if V1_29_1
            return new LightColorBaseDataSaveData(
                beat,
                transitionType,
                colorType,
                brightness,
                strobeBeatFrequency,
                customData);
#else
            return new LightColorBaseDataSaveData(
                beat,
                transitionType,
                colorType,
                brightness,
                strobeBeatFrequency,
                strobeBrightness,
                strobeFade,
                customData);
#endif
        }

        [HarmonyPrefix]
        private static bool Prefix(JsonReader reader, List<_LightColorEventBoxGroup> list)
        {
            reader.ReadArray(() =>
            {
                float beat = default;
                List<_LightColorEventBox> eventBoxes = new();
                int groupId = default;
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

                                CustomData? boxData = null;
                                return reader.ReadObject(eventName =>
                                {
                                    switch (eventName)
                                    {
                                        case "f":
                                            indexFilter = DeserializeIndexFilter(reader);
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

                                        case "customData":
                                            // Keep distribution data intact for the converter to parse.
                                            boxData = new CustomData();
                                            reader.ReadToDictionary(boxData);
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

                                                // Allocate customData only for authored keys or strobe fields backported to 1.29.1.
                                                CustomData? lightData = null;
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
                                                            strobeFade = reader.ReadIntAsBoolean() ?? strobeFade;
                                                            break;

                                                        case "customData":
                                                            lightData = new CustomData();
                                                            reader.ReadToDictionary(lightData);
                                                            break;

                                                        default:
                                                            reader.Skip();
                                                            break;
                                                    }
                                                }).Finish(() =>
                                                {
#if V1_29_1
                                                    if (strobeBrightness > 0)
                                                    {
                                                        // Backport strobeBrightness to 1.29.1.
                                                        lightData ??= new CustomData();
                                                        lightData["cg_sb"] = strobeBrightness;
                                                    }

                                                    if (strobeFade)
                                                    {
                                                        // Backport strobeFade to 1.29.1.
                                                        lightData ??= new CustomData();
                                                        lightData["cg_sf"] = strobeFade;
                                                    }
#endif

                                                    lightColorBaseDataList.Add(CreateLightColorBaseData(
                                                        lightBeat,
                                                        transitionType,
                                                        colorType,
                                                        brightness,
                                                        strobeFrequency,
#if !V1_29_1
                                                        strobeBrightness,
                                                        strobeFade,
#endif
                                                        lightData));
                                                });
                                            });
                                            break;

                                        default:
                                            reader.Skip();
                                            break;
                                    }
                                }).Finish(() =>
                                {
                                    _LightColorEventBox eventBox = new(
                                        indexFilter,
                                        beatDistributionParam,
                                        beatDistributionParamType,
                                        brightnessDistributionParam,
                                        brightnessDistributionShouldAffectFirstBaseEvent,
                                        brightnessDistributionParamType,
                                        brightnessDistributionEaseType,
                                        lightColorBaseDataList);

                                    if (boxData != null && boxData.Count > 0)
                                    {
                                        BoxCustomData.Add(eventBox, boxData);
                                    }

                                    eventBoxes.Add(eventBox);
                                });
                            });
                            break;

                        case "customData":
                            // ChromaGLS does not use group-level customData.
                            reader.Skip();
                            break;

                        default:
                            reader.Skip();
                            break;
                    }
                }).Finish(() => list.Add(new _LightColorEventBoxGroup(beat, groupId, eventBoxes)));
            });

            return false;
        }
    }
}
