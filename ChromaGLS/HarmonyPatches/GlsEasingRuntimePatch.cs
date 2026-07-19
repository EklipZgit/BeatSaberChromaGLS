#if V1_29_1
using HarmonyLib;
using _EaseType = BeatmapSaveDataVersion3.BeatmapSaveData.EaseType;

namespace ChromaGLS.HarmonyPatches
{
    // Beat Saber 1.29.1 only supports None, Linear, InQuad, OutQuad, and InOutQuad.
    // ConvertEaseType otherwise maps newer easing IDs to None, so remap them before conversion.
    [HarmonyPatch]
    internal static class GlsEasingRuntimePatch
    {
        private static int RemapEasing(int easing)
        {
            // Map unsupported easing values to supported ones for 1.29.1
            return easing switch
            {
                -1 => -1, // None
                0 => 0, // Linear
                1 => 1, // InQuadratic
                2 => 2, // OutQuadratic
                3 => 3, // InOutQuadratic

                // In* variants → InQuadratic
                4 => 1, // InSinusoidal
                7 => 1, // InCubic
                10 => 1, // InQuartic
                13 => 1, // InQuintic
                16 => 1, // InExponential
                19 => 1, // InCircular
                22 => 1, // InBack (IBa)
                25 => 1, // InElastic
                28 => 1, // InBounce

                // Out* variants → OutQuadratic
                5 => 2, // OutSinusoidal
                8 => 2, // OutCubic
                11 => 2, // OutQuartic
                14 => 2, // OutQuintic
                17 => 2, // OutExponential
                20 => 2, // OutCircular
                23 => 2, // OutBack
                26 => 2, // OutElastic
                29 => 2, // OutBounce

                // InOut* variants → InOutQuadratic
                6 => 3, // InOutSinusoidal
                9 => 3, // InOutCubic
                12 => 3, // InOutQuartic
                15 => 3, // InOutQuintic
                18 => 3, // InOutExponential
                21 => 3, // InOutCircular
                24 => 3, // InOutBack
                27 => 3, // InOutElastic
                30 => 3, // InOutBounce

                // BeatSaber-specific InOut variants → InOutQuadratic
                100 => 3, // BeatSaberInOutBack
                101 => 3, // BeatSaberInOutElastic
                102 => 3, // BeatSaberInOutBounce

                // Catch-all: map any other value to Linear
                _ => 0
            };
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BeatmapDataLoader), "ConvertEaseType")]
        private static void Prefix(ref _EaseType __0)
        {
            int easing = (int)__0;
            int remappedEasing = RemapEasing(easing);
            __0 = (_EaseType)remappedEasing;
        }
    }
}
#endif
