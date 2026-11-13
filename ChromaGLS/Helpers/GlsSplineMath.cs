using UnityEngine;

namespace ChromaGLS.Helpers
{
    // Uniform Catmull-Rom spline math shared by every GLS channel. Time-scaled tangents use the
    // outer secant at each node multiplied by the segment duration, identical to ChroMapper's
    // Easings.CatmullRomTimeTangents, so adjoining unequal spans stay C1 continuous.
    internal static class GlsSplineMath
    {
        internal const int CatmullRomId = 103;
        internal const int CatmullRomSmoothedId = 104;

        internal static bool IsCatmullRom(int easing) =>
            easing is CatmullRomId or CatmullRomSmoothedId;

        internal static bool IsDistancePaced(int easing) =>
            easing == CatmullRomSmoothedId;

        public static float Hermite(float p0, float p1, float m0, float m1, float t)
        {
            var t2 = t * t;
            var t3 = t2 * t;
            return (((2f * t3) - (3f * t2) + 1f) * p0)
                + ((t3 - (2f * t2) + t) * m0)
                + (((-2f * t3) + (3f * t2)) * p1)
                + ((t3 - t2) * m1);
        }

        public static Vector4 Hermite(Vector4 p0, Vector4 p1, Vector4 m0, Vector4 m1, float t) =>
            new(
                Hermite(p0.x, p1.x, m0.x, m1.x, t),
                Hermite(p0.y, p1.y, m0.y, m1.y, t),
                Hermite(p0.z, p1.z, m0.z, m1.z, t),
                Hermite(p0.w, p1.w, m0.w, m1.w, t));

        public static void CatmullRomTimeTangents(
            float p0,
            float p1,
            float p2,
            float p3,
            float t0,
            float t1,
            float t2,
            float t3,
            out float m1,
            out float m2)
        {
            var duration = t2 - t1;
            m1 = ((p2 - p0) / (t2 - t0)) * duration;
            m2 = ((p3 - p1) / (t3 - t1)) * duration;
        }

        public static void CatmullRomTimeTangents(
            Vector4 p0,
            Vector4 p1,
            Vector4 p2,
            Vector4 p3,
            float t0,
            float t1,
            float t2,
            float t3,
            out Vector4 m1,
            out Vector4 m2)
        {
            var duration = t2 - t1;
            m1 = ((p2 - p0) / (t2 - t0)) * duration;
            m2 = ((p3 - p1) / (t3 - t1)) * duration;
        }

        public static float CatmullRom(float p0, float p1, float p2, float p3, float t) =>
            0.5f * ((2f * p1)
                + ((-p0 + p2) * t)
                + (((2f * p0) - (5f * p1) + (4f * p2) - p3) * t * t)
                + ((-p0 + (3f * p1) - (3f * p2) + p3) * t * t * t));

        public static Vector4 ToSplineSpace(Color color, bool hsv)
        {
            if (!hsv)
            {
                return color;
            }

            Color.RGBToHSV(color, out var h, out var s, out var v);
            return new Vector4(h, s, v, color.a);
        }

        // TrueHSV hue unwraps: outer controls move onto the inner endpoints' far side so the spline
        // never wraps through the seam, matching BasicEventColorLerp.UnwrapSplineHues.
        public static void UnwrapSplineHues(
            bool trueHsv,
            ref Vector4 p0,
            Vector4 p1,
            ref Vector4 p2,
            ref Vector4 p3)
        {
            if (!trueHsv)
            {
                return;
            }

            p2.x = p1.x + (Mathf.DeltaAngle(p1.x * 360f, p2.x * 360f) / 360f);
            p0.x = p1.x - (Mathf.DeltaAngle(p0.x * 360f, p1.x * 360f) / 360f);
            p3.x = p2.x + (Mathf.DeltaAngle(p2.x * 360f, p3.x * 360f) / 360f);
        }

        // Evaluate a precomputed color spline in lerp space and convert back to RGB. Saturation stays
        // clamped while HDR value and alpha overshoot are preserved, matching CM InterpolateSpline.
        public static Color InterpolateSpline(
            Vector4 p1,
            Vector4 p2,
            Vector4 m1,
            Vector4 m2,
            float t,
            bool trueHsv)
        {
            var result = Hermite(p1, p2, m1, m2, t);
            if (!trueHsv)
            {
                return new Color(result.x, result.y, result.z, result.w);
            }

            var hue = Mathf.Repeat(result.x, 1f);
            var color = Color.HSVToRGB(hue, Mathf.Clamp01(result.y), result.z);
            color.a = result.w;
            return color;
        }
    }
}
