using CustomJSONData.CustomBeatmap;
#if !PRE_V1_37_1
using _EnvironmentColorType = BeatmapSaveDataCommon.EnvironmentColorType;
using _LightColorBaseData = BeatmapSaveDataVersion3.LightColorBaseData;
using _TransitionType = BeatmapSaveDataVersion3.TransitionType;
#else
using _EnvironmentColorType = BeatmapSaveDataVersion3.BeatmapSaveData.EnvironmentColorType;
using _LightColorBaseData = BeatmapSaveDataVersion3.BeatmapSaveData.LightColorBaseData;
using _TransitionType = BeatmapSaveDataVersion3.BeatmapSaveData.TransitionType;
#endif

namespace ChromaGLS
{
    // Mirror of CustomJSONData's LightColorBaseDataSaveData (which only exists on our CJD branch).
    // We define our own copy here so the standalone plugin does not require any CJD source changes.
    // Part B (GlsConverterPatches) detects these via the ICustomData interface.
    public class LightColorBaseDataSaveData : _LightColorBaseData, ICustomData
    {
#if !V1_29_1
        public LightColorBaseDataSaveData(
            float beat,
            _TransitionType transitionType,
            _EnvironmentColorType colorType,
            float brightness,
            int strobeBeatFrequency,
            float strobeBrightness,
            bool strobeFade,
            CustomData customData)
            : base(beat, transitionType, colorType, brightness, strobeBeatFrequency, strobeBrightness, strobeFade)
#else
        public LightColorBaseDataSaveData(
            float beat,
            _TransitionType transitionType,
            _EnvironmentColorType colorType,
            float brightness,
            int strobeBeatFrequency,
            CustomData customData)
            : base(beat, transitionType, colorType, brightness, strobeBeatFrequency)
#endif
        {
            this.customData = customData;
        }

        public CustomData customData { get; }
    }
}
