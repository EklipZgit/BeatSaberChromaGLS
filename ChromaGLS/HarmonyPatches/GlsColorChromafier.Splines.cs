using ChromaGLS.Helpers;
using CustomJSONData.CustomBeatmap;
using UnityEngine;

namespace ChromaGLS.HarmonyPatches
{
    internal static partial class GlsColorChromafier
    {
        private static ColorSplineState? PrepareColorSplines(
            LightColorGroupEffect instance,
            LightColorBeatmapEventData current)
        {
            ColorEasingStates.TryGetValue(instance, out var old);
            var state = old.Splines;
            var end = current.nextSameTypeEventData as LightColorBeatmapEventData;
            var channels = 0;
            var paced = 0;
            if (end != null && end.time > current.time)
            {
                ReadColorSplineModes(end, out channels, out paced);
            }

            // The native default before the first event stays dark instead of becoming a spline control.
            if (channels != 0 && ReferenceEquals(current, current.GetDefault()))
            {
                channels = 0;
            }

            if (channels == 0)
            {
                if (state != null)
                {
                    state.Channels = 0;
                }

                return state;
            }

            state ??= new ColorSplineState();
            state.Channels = channels;
            state.Start = current.time;
            state.End = end!.time;
            state.NormalPacing = default;
            state.BoostPacing = default;
            var previous = current.previousSameTypeEventData as LightColorBeatmapEventData;
            var next = end!.nextSameTypeEventData as LightColorBeatmapEventData;
            var duration = end.time - current.time;
            var t0 = previous?.time ?? current.time - duration;
            var t3 = next?.time ?? end.time + duration;
            var hsv = IsHsvEasingType(end);
            if (paced != 0 && ColorDistanceStates.TryGetValue(instance, out var paths))
            {
                state.NormalPacing = paths.Normal?.Bind(paced, hsv ? 2 : 0) ?? default;
                state.BoostPacing = paths.Boost?.Bind(paced, hsv ? 2 : 0) ?? default;
            }

            PrepareColorSplineTrack(ref state.NormalColors, instance, current, end, previous, next, t0, t3, channels, hsv, false);
            PrepareColorSplineTrack(ref state.BoostColors, instance, current, end, previous, next, t0, t3, channels, hsv, true);
            return state;
        }

        internal static bool UsesDistancePacing(LightColorBeatmapEventData node)
        {
            ReadColorSplineModes(node, out _, out var paced);
            return paced != 0;
        }

        private static void ReadColorSplineModes(
            LightColorBeatmapEventData node,
            out int channels,
            out int paced)
        {
            channels = 0;
            paced = 0;
            if (!PermitsIntervalEase(node) || HoldsColor(node))
            {
                return;
            }
#if PRE_V1_37_1
            var easing = ReadSplineEasing(node, "easing", 0);
#else
            var easing = ReadSplineEasing(node, "easing", (int)node.easeType);
#endif
            var color = ReadSplineEasing(node, "colorEasing", easing);
            var strobe = ReadSplineEasing(node, "strobeColorEasing", easing);
            channels = (GlsSplineMath.IsCatmullRom(easing) ? 1 : 0)
                | (GlsSplineMath.IsCatmullRom(color) ? 2 : 0)
                | (GlsSplineMath.IsCatmullRom(strobe) ? 4 : 0);
            paced = (GlsSplineMath.IsDistancePaced(easing) ? 1 : 0)
                | (GlsSplineMath.IsDistancePaced(color) ? 2 : 0)
                | (GlsSplineMath.IsDistancePaced(strobe) ? 4 : 0);
        }

        private static bool HoldsColor(LightColorBeatmapEventData node)
        {
#if PRE_V1_37_1
            return node.transitionType == BeatmapEventTransitionType.Extend;
#else
            return node.usePreviousValue;
#endif
        }

        private static int ReadSplineEasing(LightColorBeatmapEventData node, string key, int fallback) =>
            node is ICustomData custom && GlsEasingRuntimePatch.TryParseEasingId(custom.customData.Get<object>(key), out var id)
                ? id
                : fallback;

        private static void PrepareColorSplineTrack(
            ref SplineColorTrack track,
            LightColorGroupEffect instance,
            LightColorBeatmapEventData current,
            LightColorBeatmapEventData end,
            LightColorBeatmapEventData? previous,
            LightColorBeatmapEventData? next,
            float t0,
            float t3,
            int channels,
            bool hsv,
            bool boost)
        {
            var from = ResolveSplineColor(instance, current, boost);
            var to = ResolveSplineColor(instance, end, boost);
            track.Prepare(
                previous == null ? from : ResolveSplineColor(instance, previous, boost),
                from,
                to,
                next == null ? to : ResolveSplineColor(instance, next, boost),
                t0,
                current.time,
                end.time,
                t3,
                channels,
                hsv);
        }

        private static SplineColorControl ResolveSplineColor(
            LightColorGroupEffect instance,
            LightColorBeatmapEventData node,
            bool boost)
        {
            var palette = instance.GetColor(node.colorType, boost, 1f);
            var custom = ResolveCustomColor(node, "color");
            var customStrobe = ResolveCustomColor(node, "strobeColor");
            GlsConverterPatches.TryGetColorDistributionContext(node, out var distribution);
            var normal = ApplyNormalColorDistributions(palette, custom, distribution) ?? palette;
            var strobe = ApplyStrobeColorDistributions(palette, custom, customStrobe, distribution)
                ?? customStrobe ?? normal;
            var active = node.strobeBeatFrequency > 0 || ResolveStrobeInterval(node).HasValue;
            return new SplineColorControl
            {
                Normal = normal,
                Strobe = active ? strobe : normal,
                Brightness = node.brightness,
                StrobeBrightness = active ? ResolveStrobeBrightness(node) : node.brightness,
            };
        }

        private struct SplineColorControl
        {
            internal Color Normal;
            internal Color Strobe;
            internal float Brightness;
            internal float StrobeBrightness;
        }

        private readonly struct ScalarColorSpline
        {
            private readonly float _from;
            private readonly float _to;
            private readonly float _m0;
            private readonly float _m1;

            internal ScalarColorSpline(
                float p0,
                float p1,
                float p2,
                float p3,
                float t0,
                float t1,
                float t2,
                float t3)
            {
                _from = p1;
                _to = p2;
                GlsSplineMath.CatmullRomTimeTangents(p0, p1, p2, p3, t0, t1, t2, t3, out _m0, out _m1);
            }

            internal float Evaluate(float t) => GlsSplineMath.Hermite(_from, _to, _m0, _m1, t);

            internal GlsColorDistancePaths.Curve Export() =>
                GlsColorDistancePaths.Scalar(_from, _to, _m0, _m1, true);
        }

        private readonly struct VectorColorSpline
        {
            private readonly Vector4 _from;
            private readonly Vector4 _to;
            private readonly Vector4 _m0;
            private readonly Vector4 _m1;

            internal VectorColorSpline(
                Color p0,
                Color p1,
                Color p2,
                Color p3,
                float t0,
                float t1,
                float t2,
                float t3,
                bool hsv)
            {
                var c0 = GlsSplineMath.ToSplineSpace(p0, hsv);
                var c1 = GlsSplineMath.ToSplineSpace(p1, hsv);
                var c2 = GlsSplineMath.ToSplineSpace(p2, hsv);
                var c3 = GlsSplineMath.ToSplineSpace(p3, hsv);
                GlsSplineMath.UnwrapSplineHues(hsv, ref c0, c1, ref c2, ref c3);
                _from = c1;
                _to = c2;
                GlsSplineMath.CatmullRomTimeTangents(c0, c1, c2, c3, t0, t1, t2, t3, out _m0, out _m1);
            }

            internal Color Evaluate(float t, bool hsv) =>
                GlsSplineMath.InterpolateSpline(_from, _to, _m0, _m1, t, hsv);

            internal GlsColorDistancePaths.Curve Export() => new()
            {
                From = _from,
                To = _to,
                M0 = _m0,
                M1 = _m1,
                Spline = true,
            };
        }

        private struct SplineColorTrack
        {
            internal SplineColorControl From;
            internal SplineColorControl To;
            internal ScalarColorSpline Brightness;
            internal ScalarColorSpline StrobeAlpha;
            internal VectorColorSpline Normal;
            internal VectorColorSpline Strobe;

            internal void Prepare(
                SplineColorControl p0,
                SplineColorControl p1,
                SplineColorControl p2,
                SplineColorControl p3,
                float t0,
                float t1,
                float t2,
                float t3,
                int channels,
                bool hsv)
            {
                From = p1;
                To = p2;
                if ((channels & 1) != 0)
                {
                    Brightness = new ScalarColorSpline(
                        p0.Brightness,
                        p1.Brightness,
                        p2.Brightness,
                        p3.Brightness,
                        t0,
                        t1,
                        t2,
                        t3);
                    StrobeAlpha = new ScalarColorSpline(
                        p0.Strobe.a * p0.StrobeBrightness,
                        p1.Strobe.a * p1.StrobeBrightness,
                        p2.Strobe.a * p2.StrobeBrightness,
                        p3.Strobe.a * p3.StrobeBrightness,
                        t0,
                        t1,
                        t2,
                        t3);
                }

                if ((channels & 2) != 0)
                {
                    Normal = new VectorColorSpline(p0.Normal, p1.Normal, p2.Normal, p3.Normal, t0, t1, t2, t3, hsv);
                }

                if ((channels & 4) != 0)
                {
                    Strobe = new VectorColorSpline(p0.Strobe, p1.Strobe, p2.Strobe, p3.Strobe, t0, t1, t2, t3, hsv);
                }
            }
        }

        // Retained inside the existing per-effect easing cache and reused across node changes.
        private sealed class ColorSplineState
        {
            private SplineColorTrack _normalColors;
            private SplineColorTrack _boostColors;

            internal int Channels { get; set; }

            internal ref SplineColorTrack NormalColors => ref _normalColors;

            internal ref SplineColorTrack BoostColors => ref _boostColors;

            internal GlsColorDistancePaths.Binding NormalPacing { get; set; }

            internal GlsColorDistancePaths.Binding BoostPacing { get; set; }

            internal float Start { get; set; }

            internal float End { get; set; }

            internal Color GetNormal(bool boost, bool hsv, float t, float intervalT, float colorT)
            {
                ref var track = ref (boost ? ref BoostColors : ref NormalColors);
                var pacing = boost ? BoostPacing : NormalPacing;
                var color = (Channels & 2) != 0
                    ? track.Normal.Evaluate(MapProgress(pacing.Color, t), hsv)
                    : LerpStrobeFade(track.From.Normal, track.To.Normal, colorT, hsv);
                var brightness = (Channels & 1) != 0
                    ? track.Brightness.Evaluate(MapProgress(pacing.Alpha, t))
                    : Mathf.LerpUnclamped(track.From.Brightness, track.To.Brightness, intervalT);
                color.a *= brightness;
                return color;
            }

            internal Color GetStrobe(bool boost, bool hsv, float t, float intervalT, float colorT)
            {
                ref var track = ref (boost ? ref BoostColors : ref NormalColors);
                var pacing = boost ? BoostPacing : NormalPacing;
                var color = (Channels & 4) != 0
                    ? track.Strobe.Evaluate(MapProgress(pacing.StrobeColor, t), hsv)
                    : LerpStrobeFade(track.From.Strobe, track.To.Strobe, colorT, hsv);
                color.a = (Channels & 1) != 0
                    ? track.StrobeAlpha.Evaluate(MapProgress(pacing.StrobeAlpha, t))
                    : Mathf.LerpUnclamped(
                        track.From.Strobe.a * track.From.StrobeBrightness,
                        track.To.Strobe.a * track.To.StrobeBrightness,
                        intervalT);
                return color;
            }

            private float MapProgress(GlsDistancePath? path, float t) =>
                path != null && path.TryMapTime(Start + ((End - Start) * t), out var mapped)
                    ? Mathf.InverseLerp(Start, End, mapped)
                    : t;
        }
    }
}
