using System;
using System.Collections.Generic;
using System.Globalization;
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

        // Independent color tracks accept Linear because an absent key inherits the interval curve.
        private static bool TryGetCustomCurve(object? raw, out ColorCurve? curve, bool includeLinear = false)
        {
            curve = null;
            if (raw is not (sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal))
            {
                return false;
            }

            // Check the decimal before converting to double, which can round a small fractional part away.
            if (raw is decimal precise && precise != decimal.Truncate(precise))
            {
                return false;
            }

            double value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            return value >= (includeLinear ? 0d : 1d) && value <= 102d && value == Math.Truncate(value)
                && Curves.TryGetValue((int)value, out curve);
        }

        [HarmonyPostfix]
        private static void Postfix(_EaseType __0, ref EaseType __result)
        {
            int id = (int)__0;
            if (id >= 4 && id <= 18)
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
