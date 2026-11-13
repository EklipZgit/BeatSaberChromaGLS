using System;
using System.Collections.Generic;
using System.Globalization;
using ChromaGLS.Helpers;
using HarmonyLib;
#if V1_29_1
using UnityEngine;
#endif

#if PRE_V1_37_1
using _EaseType = BeatmapSaveDataVersion3.BeatmapSaveData.EaseType;
#else
using _EaseType = BeatmapSaveDataCommon.EaseType;
#endif

namespace ChromaGLS.HarmonyPatches
{
    // 1.29.1 maps easing IDs beyond Quad to None despite supporting most curves at runtime.
    // Newer loaders also omit fifteen curves, so patch their conversion as well.
#if PRE_V1_37_1
    [HarmonyPatch(typeof(BeatmapDataLoader), "ConvertEaseType")]
#else
    [HarmonyPatch(typeof(BeatmapTypeConverters), nameof(BeatmapTypeConverters.ConvertEaseType))]
#endif
    internal static class GlsEasingRuntimePatch
    {
        private static readonly Dictionary<int, ColorCurve> Curves = new()
        {
            { 0, new ColorCurve(EaseType.Linear) },
            { 1, new ColorCurve(EaseType.InQuad) },
            { 2, new ColorCurve(EaseType.OutQuad) },
            { 3, new ColorCurve(EaseType.InOutQuad) },
            { 4, new ColorCurve(EaseType.InSine) },
            { 5, new ColorCurve(EaseType.OutSine) },
            { 6, new ColorCurve(EaseType.InOutSine) },
            { 7, new ColorCurve(EaseType.InCubic) },
            { 8, new ColorCurve(EaseType.OutCubic) },
            { 9, new ColorCurve(EaseType.InOutCubic) },

            // Save-data IDs differ from the runtime Quart and Quint enum values.
            { 10, new ColorCurve(EaseType.InQuart) },
            { 11, new ColorCurve(EaseType.OutQuart) },
            { 12, new ColorCurve(EaseType.InOutQuart) },
            { 13, new ColorCurve(EaseType.InQuint) },
            { 14, new ColorCurve(EaseType.OutQuint) },
            { 15, new ColorCurve(EaseType.InOutQuint) },
            { 16, new ColorCurve(EaseType.InExpo) },
            { 17, new ColorCurve(EaseType.OutExpo) },
            { 18, new ColorCurve(EaseType.InOutExpo) },
            { 19, new ColorCurve(EaseType.InCirc) },
            { 20, new ColorCurve(EaseType.OutCirc) },
            { 21, new ColorCurve(EaseType.InOutCirc) },
            { 22, new ColorCurve(EaseType.InBack) },
            { 23, new ColorCurve(EaseType.OutBack) },
            { 24, new ColorCurve(EaseType.InOutBack) },
            { 25, new ColorCurve(EaseType.InElastic) },
            { 26, new ColorCurve(EaseType.OutElastic) },
            { 27, new ColorCurve(EaseType.InOutElastic) },
            { 28, new ColorCurve(EaseType.InBounce) },
            { 29, new ColorCurve(EaseType.OutBounce) },
            { 30, new ColorCurve(EaseType.InOutBounce) },

            // 1.29.1 lacks these three Beat Saber InOut curves.
#if V1_29_1
            { 100, new ColorCurve(BeatSaberInOutBack) },
            { 101, new ColorCurve(BeatSaberInOutElastic) },
            { 102, new ColorCurve(BeatSaberInOutBounce) },
#else
            { 100, new ColorCurve(EaseType.BeatSaberInOutBack) },
            { 101, new ColorCurve(EaseType.BeatSaberInOutElastic) },
            { 102, new ColorCurve(EaseType.BeatSaberInOutBounce) },
#endif
        };

        private static readonly Dictionary<string, int> EasingIdByName = new(StringComparer.Ordinal)
        {
            { "N", -1 }, { "L", 0 },
            { "I^2", 1 }, { "O^2", 2 }, { "IO^2", 3 },
            { "ISn", 4 }, { "OSn", 5 }, { "IOSn", 6 },
            { "I^3", 7 }, { "O^3", 8 }, { "IO^3", 9 },
            { "I^4", 10 }, { "O^4", 11 }, { "IO^4", 12 },
            { "I^5", 13 }, { "O^5", 14 }, { "IO^5", 15 },
            { "IEx", 16 }, { "OEx", 17 }, { "IOEx", 18 },
            { "ICr", 19 }, { "OCr", 20 }, { "IOCr", 21 },
            { "IBk", 22 }, { "OBk", 23 }, { "IOTBk", 24 },
            { "IEl", 25 }, { "OEl", 26 }, { "IOTEl", 27 },
            { "IBo", 28 }, { "OBo", 29 }, { "IOTBo", 30 },
            { "IOBk", 100 }, { "IOEl", 101 }, { "IOBo", 102 },
            { "Cat", 103 }, { "CatS", 104 },
            { "easeStep", -1 }, { "easeNone", -1 }, { "easeLinear", 0 },
            { "easeInQuad", 1 }, { "easeOutQuad", 2 }, { "easeInOutQuad", 3 },
            { "easeInSine", 4 }, { "easeOutSine", 5 }, { "easeInOutSine", 6 },
            { "easeInCubic", 7 }, { "easeOutCubic", 8 }, { "easeInOutCubic", 9 },
            { "easeInQuart", 10 }, { "easeOutQuart", 11 }, { "easeInOutQuart", 12 },
            { "easeInQuint", 13 }, { "easeOutQuint", 14 }, { "easeInOutQuint", 15 },
            { "easeInExpo", 16 }, { "easeOutExpo", 17 }, { "easeInOutExpo", 18 },
            { "easeInCirc", 19 }, { "easeOutCirc", 20 }, { "easeInOutCirc", 21 },
            { "easeInBack", 22 }, { "easeOutBack", 23 }, { "easeInOutBack", 24 },
            { "easeInElastic", 25 }, { "easeOutElastic", 26 }, { "easeInOutElastic", 27 },
            { "easeInBounce", 28 }, { "easeOutBounce", 29 }, { "easeInOutBounce", 30 },
            { "easeBeatSaberInOutBack", 100 },
            { "easeBeatSaberInOutElastic", 101 },
            { "easeBeatSaberInOutBounce", 102 },
            { "splineCatmullRom", 103 },
            { "splineCatmullRomSmoothed", 104 },
        };

        private static readonly Dictionary<EaseType, Func<float, float>> AdditionalNativeCurves = CreateAdditionalNativeCurves();

        internal static bool TryGetAdditionalNativeEasing(EaseType ease, out Func<float, float> easing)
        {
            return AdditionalNativeCurves.TryGetValue(ease, out easing);
        }

        internal static bool TryGetCustomEasing(object? raw, out Func<float, float>? easing) =>
            TryGetEasing(raw, out easing, false);

        internal static bool TryGetTrackEasing(object? raw, out Func<float, float>? easing) =>
            TryGetEasing(raw, out easing, true);

        internal static EaseType GetCustomRuntimeEase(object? raw, EaseType fallback)
        {
            return TryGetCustomCurve(raw, out ColorCurve? curve)
                ? curve!.NativeType
                : fallback;
        }

        private static bool TryGetEasing(object? raw, out Func<float, float>? easing, bool includeLinear)
        {
            bool valid = TryGetCustomCurve(raw, out ColorCurve? curve, includeLinear);
            easing = valid ? curve!.Evaluate : null;
            return valid;
        }

        internal static bool TryParseEasingId(object? raw, out int id)
        {
            id = 0;
            switch (raw)
            {
                case string text:
                    return EasingIdByName.TryGetValue(text, out id);

                case sbyte or byte or short or ushort or int or uint or long or ulong
                    or float or double or decimal:
                    if (raw is decimal precise && precise != decimal.Truncate(precise))
                    {
                        return false;
                    }

                    double value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                    if (!(value is -1 or >= 0 and <= 30 or >= 100 and <= 104)
                        || value != Math.Truncate(value))
                    {
                        return false;
                    }

                    id = (int)value;
                    return true;

                default:
                    return false;
            }
        }

        internal static bool IsSupportedCurveId(int id) => id == -1 || Curves.ContainsKey(id);

        private static bool TryGetCustomCurve(object? raw, out ColorCurve? curve, bool includeLinear = false)
        {
            curve = null;
            return TryParseEasingId(raw, out int id)
                && id >= (includeLinear ? 0 : 1)
                && Curves.TryGetValue(id, out curve);
        }

        [HarmonyPostfix]
        private static void Postfix(_EaseType __0, ref EaseType __result)
        {
            int id = (int)__0;
            if (GlsSplineMath.IsCatmullRom(id))
            {
                // Spline markers stay as dedicated runtime ids; no native enum maps to them.
                __result = (EaseType)id;
            }
            else if (id >= 4 && id <= 18)
            {
                __result = Curves[id].NativeType;
            }
#if V1_29_1
            // Keep support for Circular, Back, Elastic, and Bounce on older versions.
            else if (id >= 19 && id <= 30)
            {
                __result = ((id - 19) % 3) switch
                {
                    0 => EaseType.InQuad,
                    1 => EaseType.OutQuad,
                    _ => EaseType.InOutQuad,
                };
            }
            else if (id >= 100 && id <= 102)
            {
                __result = EaseType.InOutQuad;
            }
#endif

        }

        private static Dictionary<EaseType, Func<float, float>> CreateAdditionalNativeCurves()
        {
            Dictionary<EaseType, Func<float, float>> result = new();
            for (int id = 4; id <= 18; id++)
            {
                result.Add(Curves[id].NativeType, Curves[id].Evaluate);
            }

            return result;
        }

#if V1_29_1
        // These Beat Saber curves are absent from 1.29.1 and intentionally overshoot at the endpoints.
        private static float BeatSaberInOutBack(float t)
        {
            if (t < 0.517f)
            {
                return 5.014f * t * t * t;
            }

            float value = (1.665f * (t + -0.4f)) - 1f;
            return 1f + (2.70158f * Mathf.Pow(value, 3f)) + (1.70158f * Mathf.Pow(value, 2f));
        }

        private static float BeatSaberInOutElastic(float t)
        {
            if (t < 0.3f)
            {
                return 37.037f * t * t * t;
            }

            return (Mathf.Pow(2f, -10f * (t - 0.2f)) * Mathf.Sin(t * 10f * (Mathf.PI * 2f / 3f))) + 1f;
        }

        private static float BeatSaberInOutBounce(float t)
        {
            if (t < 0.72727275f)
            {
                if (t < 0.36363637f)
                {
                    return 20.796f * t * t * t;
                }

                t -= 0.54545456f;
                return (7.5625f * t * t) + 0.75f;
            }

            if (t < 0.90909094f)
            {
                t -= 0.8181818f;
                return (7.5625f * t * t) + 0.9375f;
            }

            t -= 21f / 22f;
            return (7.5625f * t * t) + (63f / 64f);
        }
#endif

        private sealed class ColorCurve
        {
            public ColorCurve(EaseType nativeType)
            {
                NativeType = nativeType;
                Evaluate = t => Interpolation.Interpolate(t, nativeType);
            }

#if V1_29_1
            public ColorCurve(Func<float, float> evaluate)
            {
                NativeType = EaseType.Linear;
                Evaluate = evaluate;
            }
#endif

            public EaseType NativeType { get; }

            public Func<float, float> Evaluate { get; }
        }
    }
}
