using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChromaGLS.Helpers
{
    internal sealed class GlsColorDistancePaths
    {
        private GlsDistancePath? _alpha;
        private GlsDistancePath? _strobeAlpha;
        private readonly GlsDistancePath?[] _normal = new GlsDistancePath?[3];
        private readonly GlsDistancePath?[] _strobe = new GlsDistancePath?[3];

        internal static GlsColorDistancePaths? Create<T>(
            IEnumerable<T> states,
            Func<T, Modes> getModes,
            Func<T, Span> prepare,
            GlsColorDistancePaths? sharedBrightness = null)
        {
            var paced = 0;
            var normalModes = 0;
            var strobeModes = 0;
            var hasStrobe = false;
            foreach (var state in states)
            {
                var modes = getModes(state);
                paced |= modes.Paced;
                hasStrobe |= modes.Strobe;
                if ((modes.Paced & 2) != 0)
                {
                    normalModes |= 1 << modes.Space;
                }

                if ((modes.Paced & 4) != 0)
                {
                    strobeModes |= 1 << modes.Space;
                }
            }

            if (paced == 0)
            {
                return null;
            }

            var alphaTrack = (paced & 1) != 0 && sharedBrightness == null ? NewTrack(1) : null;
            var strobeAlphaTrack = (paced & 1) != 0 && hasStrobe ? NewTrack(1) : null;
            var normalTracks = new List<GlsDistancePath.PreparedSpan>[3][];
            var strobeTracks = new List<GlsDistancePath.PreparedSpan>[3][];
            for (var space = 0; space < 3; space++)
            {
                if ((normalModes & (1 << space)) != 0)
                {
                    normalTracks[space] = NewTrack(4);
                }

                if (hasStrobe && (strobeModes & (1 << space)) != 0)
                {
                    strobeTracks[space] = NewTrack(4);
                }
            }

            foreach (var state in states)
            {
                var span = prepare(state);
                Add(alphaTrack, span, span.Alpha, (span.Paced & 1) != 0, true, false);
                Add(strobeAlphaTrack, span, span.StrobeAlpha, (span.Paced & 1) != 0, true, false);
                for (var space = 0; space < 3; space++)
                {
                    var sameSpace = span.Space == space;
                    var unwrapHue = space == 2;
                    Add(
                        normalTracks[space],
                        span,
                        span.Color,
                        (span.Paced & 2) != 0,
                        sameSpace,
                        unwrapHue);
                    Add(
                        strobeTracks[space],
                        span,
                        span.StrobeColor,
                        (span.Paced & 4) != 0,
                        sameSpace,
                        unwrapHue);
                }
            }

            var result = new GlsColorDistancePaths
            {
                _alpha = sharedBrightness == null ? Build(alphaTrack) : sharedBrightness._alpha,
                _strobeAlpha = Build(strobeAlphaTrack),
            };
            for (var space = 0; space < 3; space++)
            {
                result._normal[space] = Build(normalTracks[space]);
                result._strobe[space] = Build(strobeTracks[space]);
            }

            return result;
        }

        internal Binding Bind(int paced, int space) => new(
            (paced & 1) != 0 ? _alpha : null,
            (paced & 1) != 0 ? _strobeAlpha : null,
            (paced & 2) != 0 ? _normal[space] : null,
            (paced & 4) != 0 ? _strobe[space] : null);

        internal static Curve Scalar(float from, float to, float m0, float m1, bool spline) => new()
        {
            From = new Vector4(from, 0, 0, 0),
            To = new Vector4(to, 0, 0, 0),
            M0 = new Vector4(m0, 0, 0, 0),
            M1 = new Vector4(m1, 0, 0, 0),
            Spline = spline,
        };

        private static List<GlsDistancePath.PreparedSpan>[] NewTrack(int dimensions)
        {
            var track = new List<GlsDistancePath.PreparedSpan>[dimensions];
            for (var axis = 0; axis < dimensions; axis++)
            {
                track[axis] = new List<GlsDistancePath.PreparedSpan>();
            }

            return track;
        }

        private static void Add(
            List<GlsDistancePath.PreparedSpan>[]? track,
            Span span,
            Curve curve,
            bool paced,
            bool sameSpace,
            bool unwrapHue)
        {
            if (track == null)
            {
                return;
            }

            // Local hue controls differ by whole turns at the seam. Align them before testing
            // continuity without changing the color curve evaluated during playback.
            var hueOffset = unwrapHue && track[0].Count > 0
                ? Mathf.Round(track[0][track[0].Count - 1].P1 - curve.From.x)
                : 0f;
            for (var axis = 0; axis < track.Length; axis++)
            {
                var offset = axis == 0 ? hueOffset : 0f;
                track[axis].Add(new GlsDistancePath.PreparedSpan(
                    span.Start,
                    span.End,
                    curve.From[axis] + offset,
                    curve.To[axis] + offset,
                    curve.M0[axis],
                    curve.M1[axis],
                    paced && sameSpace && curve.Spline,
                    sameSpace && curve.Spline));
            }
        }

        private static GlsDistancePath? Build(List<GlsDistancePath.PreparedSpan>[]? track)
        {
            if (track == null)
            {
                return null;
            }

            var path = new GlsDistancePath();
            path.Build(track, Vector4.zero);
            return path;
        }

        internal readonly struct Modes
        {
            internal readonly int Paced;
            internal readonly int Space;
            internal readonly bool Strobe;

            internal Modes(int paced, int space, bool strobe)
            {
                Paced = paced;
                Space = space;
                Strobe = strobe;
            }
        }

        internal struct Curve
        {
            internal Vector4 From;
            internal Vector4 To;
            internal Vector4 M0;
            internal Vector4 M1;
            internal bool Spline;
        }

        internal struct Span
        {
            internal float Start;
            internal float End;
            internal int Paced;
            internal int Space;
            internal Curve Alpha;
            internal Curve StrobeAlpha;
            internal Curve Color;
            internal Curve StrobeColor;
        }

        internal readonly struct Binding
        {
            internal readonly GlsDistancePath? Alpha;
            internal readonly GlsDistancePath? StrobeAlpha;
            internal readonly GlsDistancePath? Color;
            internal readonly GlsDistancePath? StrobeColor;

            internal Binding(
                GlsDistancePath? alpha,
                GlsDistancePath? strobeAlpha,
                GlsDistancePath? color,
                GlsDistancePath? strobeColor)
            {
                Alpha = alpha;
                StrobeAlpha = strobeAlpha;
                Color = color;
                StrobeColor = strobeColor;
            }
        }
    }
}
