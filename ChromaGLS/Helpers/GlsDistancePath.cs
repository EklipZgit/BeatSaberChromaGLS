using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChromaGLS.Helpers
{
// Distance-paced Catmull-Rom: reparameterizes each contiguous eligible run so it rests
// at both ends and interior node speeds come from a PCHIP-style weighted harmonic mean of secant
// speeds. The per-axis spatial polynomials are preserved exactly; only the time mapping changes.
internal sealed class GlsDistancePath
{
    private const int InverseTableSize = 32;
    private const float GaussA = 0.33998104358485626f;
    private const float GaussWa = 0.6521451548625461f;
    private const float GaussB = 0.8611363115940526f;
    private const float GaussWb = 0.3478548451374538f;

    private readonly List<Interval> intervals = new();
    private int lastInterval = -1;

    private static float Eval(AxisSpan s, float u) =>
        (((((s.H0 * u) + s.H1) * u) + s.H2) * u) + s.H3;

    private static float Deriv(AxisSpan s, float u) =>
        ((((3f * s.H0) * u) + (2f * s.H1)) * u) + s.H2;

    private float SpatialSpeed(Interval interval, float s)
    {
        var dx = Deriv(interval.X, s);
        var dy = Deriv(interval.Y, s);
        var dz = Deriv(interval.Z, s);
        var dw = Deriv(interval.W, s);
        return Mathf.Sqrt((dx * dx) + (dy * dy) + (dz * dz) + (dw * dw));
    }

    private Vector3 Evaluate(Interval interval, float s)
    {
        return new Vector3(
            Eval(interval.X, s),
            Eval(interval.Y, s),
            Eval(interval.Z, s));
    }

    // Cumulative arc length built once per eligible interval: weighted 4-point Gauss-Legendre per
    // 1/32 subinterval, accumulated in double for a strictly monotone prefix table.
    private void BuildInverseTable(Interval interval)
    {
        var samples = interval.ArcSamples = new float[InverseTableSize + 1];
        var slopes = interval.ArcSlopes = new float[InverseTableSize + 1];
        double cumulative = 0;
        samples[0] = 0f;
        slopes[0] = Reciprocal(interval, 0f);
        for (var i = 0; i < InverseTableSize; i++)
        {
            var s0 = i / (float)InverseTableSize;
            var s1 = (i + 1) / (float)InverseTableSize;
            var half = (s1 - s0) * 0.5f;
            var mid = s0 + half;
            cumulative += half * ((GaussWa * SpatialSpeed(interval, mid - (half * GaussA)))
                + (GaussWb * SpatialSpeed(interval, mid - (half * GaussB)))
                + (GaussWb * SpatialSpeed(interval, mid + (half * GaussB)))
                + (GaussWa * SpatialSpeed(interval, mid + (half * GaussA))));
            samples[i + 1] = (float)cumulative;
            slopes[i + 1] = Reciprocal(interval, s1);
        }

        interval.Length = (float)cumulative;

        // Clamp shared endpoint slopes once against both adjacent table secants so adjacent cells
        // agree on the derivative and the inverse stays monotone through zero-speed boundaries.
        for (var i = 0; i <= InverseTableSize; i++)
        {
            var left = i > 0 ? 1f / (InverseTableSize * (samples[i] - samples[i - 1])) : float.PositiveInfinity;
            var right = i < InverseTableSize ? 1f / (InverseTableSize * (samples[i + 1] - samples[i])) : float.PositiveInfinity;
            var limit = 3f * Math.Max(Math.Min(left, right), 0f);
            if (slopes[i] > limit)
            {
                slopes[i] = limit;
            }
        }
    }

    private float Reciprocal(Interval interval, float s)
    {
        var speed = SpatialSpeed(interval, s);
        return speed > 1e-6f * interval.DerivativeScale ? 1f / speed : 0f;
    }

    private float InverseArcLength(Interval interval, float distance)
    {
        var samples = interval.ArcSamples;
        if (distance <= 0f)
        {
            return 0f;
        }

        if (distance >= interval.Length)
        {
            return 1f;
        }

        var low = 0;
        var high = InverseTableSize;
        while (high - low > 1)
        {
            var mid = (low + high) >> 1;
            if (samples[mid] <= distance)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        var d0 = samples[low];
        var d1 = samples[low + 1];
        var span = d1 - d0;
        var u = (distance - d0) / span;

        // Monotone cubic inverse over [s_low, s_high]: the build-clamped ds/dd tangents convert to
        // ds/du for Hermite's normalized domain.
        var s0 = low / (float)InverseTableSize;
        var s1 = (low + 1) / (float)InverseTableSize;
        var s = GlsSplineMath.Hermite(
            s0,
            s1,
            interval.ArcSlopes[low] * span,
            interval.ArcSlopes[low + 1] * span,
            u);
        return Mathf.Clamp(s, s0, s1);
    }

    private float DistanceAlongInterval(Interval interval, float time)
    {
        var duration = interval.End - interval.Start;
        if (duration <= 1e-6f)
        {
            return 0f;
        }

        var u = Mathf.InverseLerp(interval.Start, interval.End, time);
        var v0 = interval.SpeedAtStart * duration;
        var v1 = interval.SpeedAtEnd * duration;
        return GlsSplineMath.Hermite(0f, interval.Length, v0, v1, u);
    }

    /// <summary>
    /// Evaluates the shared spatial position for an element at <paramref name="time"/> inside a
    /// reparameterized run. Returns false outside runs so the caller keeps the legacy tween path;
    /// exact interval ends return the exact endpoint. Membership is right-open except the last end.
    /// </summary>
    internal bool TryEvaluate(float time, out Vector3 position)
    {
        position = default;
        if (!TryGetSpatialParameter(time, out var interval, out var s))
        {
            return false;
        }

        position = Evaluate(interval, s);
        return true;
    }

    // Maps real time onto the reparameterized time inside a CatS run: the shared distance-time
    // profile gives the spatial parameter, converted back to a timestamp so callers can evaluate
    // their own tween geometry unchanged. Returns false outside runs so the caller keeps real time.
    internal bool TryMapTime(float time, out float mappedTime)
    {
        mappedTime = time;
        if (!TryGetSpatialParameter(time, out var interval, out var s))
        {
            return false;
        }

        mappedTime = interval.Start + ((interval.End - interval.Start) * s);
        return true;
    }

    // Shared interval find + distance + inverse-arc-length lookup for TryEvaluate/TryMapTime.
    private bool TryGetSpatialParameter(float time, out Interval interval, out float s)
    {
        interval = null!;
        s = 0f;
        if (intervals.Count == 0)
        {
            return false;
        }

        var index = lastInterval;
        if (index < 0 || index >= intervals.Count
            || time < intervals[index].Start || time >= intervals[index].End)
        {
            index = FindInterval(time);
            if (index < 0)
            {
                lastInterval = -1;
                return false;
            }

            lastInterval = index;
        }

        interval = intervals[index];
        if (time >= interval.End)
        {
            s = 1f;
            return true;
        }

        var distance = DistanceAlongInterval(interval, time);
        s = InverseArcLength(interval, distance);
        return true;
    }

    private int FindInterval(float time)
    {
        var low = 0;
        var high = intervals.Count - 1;
        while (low < high)
        {
            var mid = (low + high + 1) >> 1;
            if (intervals[mid].Start <= time)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return time >= intervals[low].Start && time <= intervals[low].End ? low : -1;
    }

    /// <summary>
    /// Rebuilds the shared distance-time mapping for one element from the per-axis prepared spans.
    /// <paramref name="spans"/> is [axis][interval] already sorted by start; a missing axis stays
    /// constant inside covered union intervals. Ineligible/zero-motion union intervals split runs
    /// and produce no arc tables.
    /// </summary>
    internal void Build(List<PreparedSpan>?[] spans, Vector3 initialPosition)
    {
        var expanded = new List<PreparedSpan>?[4];
        expanded[0] = spans.Length > 0 ? spans[0] : null;
        expanded[1] = spans.Length > 1 ? spans[1] : null;
        expanded[2] = spans.Length > 2 ? spans[2] : null;
        Build(expanded, new Vector4(initialPosition.x, initialPosition.y, initialPosition.z, 0f));
    }

    // Generic distance path: up to four independent scalar tracks share one arc-length profile.
    // A null or empty axis entry keeps that component constant inside covered union intervals.
    internal void Build(List<PreparedSpan>?[] spans, Vector4 initialPosition)
    {
        intervals.Clear();
        lastInterval = -1;
        var cursors = new int[spans.Length];
        var current = float.MaxValue;
        foreach (var axis in spans)
        {
            if (axis != null && axis.Count > 0)
            {
                current = Math.Min(current, axis[0].Start);
            }
        }

        var constant = initialPosition;

        // The last moving union interval and its shape flags; Cat<->CatS boundaries share speed
        // through it without retaining extra tables for time-paced spans.
        Interval? previousUnion = null;
        var previousUnionSpline = false;
        var previousUnionPaced = false;
        while (current < float.MaxValue)
        {
            var end = float.MaxValue;

            // Resolve all three boundaries before restricting any polynomial to their common span.
            for (var a = 0; a < spans.Length; a++)
            {
                var list = spans[a];
                if (list == null)
                {
                    continue;
                }

                while (cursors[a] < list.Count && list[cursors[a]].End <= current)
                {
                    constant[a] = list[cursors[a]].P1;
                    cursors[a]++;
                }

                if (cursors[a] >= list.Count)
                {
                    continue;
                }

                var s = list[cursors[a]];
                end = Math.Min(end, s.Start > current ? s.Start : s.End);
            }

            if (end == float.MaxValue)
            {
                break;
            }

            var eligible = true;
            var spline = true;
            var interval = new Interval
            {
                Start = current,
                End = end,
                X = ConstantSpan(constant.x),
                Y = ConstantSpan(constant.y),
                Z = ConstantSpan(constant.z),
                W = ConstantSpan(constant.w)
            };
            for (var a = 0; a < spans.Length; a++)
            {
                var list = spans[a];
                if (list == null || cursors[a] >= list.Count || list[cursors[a]].Start > current)
                {
                    continue;
                }

                var s = list[cursors[a]];
                var stationary = s.P0 == s.P1 && s.M0 == 0f && s.M1 == 0f;
                eligible &= s.Eligible || stationary;
                spline &= s.IsSpline || stationary;
                var restricted = stationary
                    ? ConstantSpan(s.P0)
                    : RestrictSpan(s, (current - s.Start) / (s.End - s.Start), (end - s.Start) / (s.End - s.Start));
                switch (a)
                {
                    case 0:
                        interval.X = restricted;
                        break;
                    case 1:
                        interval.Y = restricted;
                        break;
                    case 2:
                        interval.Z = restricted;
                        break;
                    default:
                        interval.W = restricted;
                        break;
                }
            }

            var moving = HasMotion(interval);
            if (moving)
            {
                interval.DerivativeScale = DerivativeScaleOf(interval);
            }

            if (eligible && moving)
            {
                var index = intervals.Count;
                AddSplittingAtCusps(interval);

                // A CatS run that follows a contiguous fully-spline time-paced interval inherits
                // that interval's boundary speed instead of stopping at the mode change.
                if (previousUnion != null
                    && previousUnion.End == interval.Start
                    && previousUnionSpline
                    && !previousUnionPaced
                    && !BoundarySpeedZero(previousUnion, interval))
                {
                    intervals[index].SpeedAtStart =
                        SpatialSpeed(previousUnion, 1f)
                            / (previousUnion.End - previousUnion.Start);
                }
            }
            else if (spline && moving && intervals.Count > 0
                && intervals[intervals.Count - 1].End == interval.Start
                && !BoundarySpeedZero(intervals[intervals.Count - 1], interval))
            {
                // The reverse direction: a time-paced spline interval hands the CatS run its own
                // boundary speed so the CatS->Cat transition does not slam to a stop.
                intervals[intervals.Count - 1].SpeedAtEnd =
                    SpatialSpeed(interval, 0f) / (interval.End - interval.Start);
            }

            if (moving)
            {
                previousUnion = interval;
                previousUnionSpline = spline;
                previousUnionPaced = eligible;
            }
            else
            {
                previousUnion = null;
            }

            current = end;
        }

        for (var n = 1; n < intervals.Count; n++)
        {
            var prev = intervals[n - 1];
            var next = intervals[n];
            if (prev.End != next.Start || BoundarySpeedZero(prev, next))
            {
                continue;
            }

            var hPrev = prev.End - prev.Start;
            var hNext = next.End - next.Start;
            var dPrev = prev.Length / hPrev;
            var dNext = next.Length / hNext;
            var w1 = (2f * hNext) + hPrev;
            var w2 = hNext + (2f * hPrev);
            var speed = (w1 + w2) / ((w1 / dPrev) + (w2 / dNext));
            prev.SpeedAtEnd = speed;
            next.SpeedAtStart = speed;
        }
    }

    private static bool HasMotion(Interval interval) =>
        HasMotion(interval.X) || HasMotion(interval.Y) || HasMotion(interval.Z) || HasMotion(interval.W);

    private static bool HasMotion(AxisSpan span) =>
        span.H0 != 0f || span.H1 != 0f || span.H2 != 0f;

    // A boundary node with a zero geometric tangent (cusp/reversal) must stop, not force speed
    // through the direction change. The same applies when the tangent direction changes: a held or
    // finished axis can turn the shared path into a geometric corner even while other axes keep
    // moving. Zero tests stay relative to the intervals' derivative scale so tiny-but-real motion
    // survives.
    private bool BoundarySpeedZero(Interval prev, Interval next)
    {
        var scale = Math.Max(prev.DerivativeScale, next.DerivativeScale);
        var zero = 1e-6f * scale;
        var left = new Vector4(Deriv(prev.X, 1f), Deriv(prev.Y, 1f), Deriv(prev.Z, 1f), Deriv(prev.W, 1f));
        var right = new Vector4(Deriv(next.X, 0f), Deriv(next.Y, 0f), Deriv(next.Z, 0f), Deriv(next.W, 0f));
        var leftMag = left.magnitude;
        var rightMag = right.magnitude;
        if (leftMag <= zero || rightMag <= zero)
        {
            return true;
        }

        var turn = (left / leftMag) - (right / rightMag);
        return turn.sqrMagnitude > 1e-8f;
    }

    // Interior cusps (e.g. the 1 -> 1 uniform overshoot that reverses at midpoint) split into
    // separately timed parts at build time so the run stops exactly at the turning point rather
    // than forcing speed through a geometric reversal. Root solving happens only here, never
    // per frame.
    private void AddSplittingAtCusps(Interval interval)
    {
        var found = CommonZeroRoots(interval, interval.DerivativeScale, out var root0, out var root1);
        if (found == 0)
        {
            BuildInverseTable(interval);
            intervals.Add(interval);
            return;
        }

        AddPart(interval, 0.0, root0);
        if (found == 2)
        {
            AddPart(interval, root0, root1);
        }

        AddPart(interval, found == 2 ? root1 : root0, 1.0);
    }

    private void AddPart(Interval parent, double a, double b)
    {
        var part = new Interval
        {
            Start = (float)(parent.Start + ((parent.End - parent.Start) * a)),
            End = (float)(parent.Start + ((parent.End - parent.Start) * b)),
            X = RestrictNormalized(parent.X, a, b),
            Y = RestrictNormalized(parent.Y, a, b),
            Z = RestrictNormalized(parent.Z, a, b),
            W = RestrictNormalized(parent.W, a, b),
        };
        part.DerivativeScale = DerivativeScaleOf(part);
        BuildInverseTable(part);
        intervals.Add(part);
    }

    // Largest derivative coefficient magnitude across axes; zero-velocity tests stay relative to it.
    private static float DerivativeScaleOf(Interval interval) =>
        Math.Max(
            Math.Max(DerivativeScaleOf(interval.X), DerivativeScaleOf(interval.Y)),
            Math.Max(DerivativeScaleOf(interval.Z), DerivativeScaleOf(interval.W)));

    private static float DerivativeScaleOf(AxisSpan s) =>
        Math.Max(Math.Abs(3f * s.H0), Math.Max(Math.Abs(2f * s.H1), Math.Abs(s.H2)));

    // Roots strictly inside (0,1) where every axis derivative is zero within relative tolerance.
    // Coefficients normalize by the dominant axis's largest magnitude before solving so the
    // quadratic degeneracy test is scale-independent; roots use the stable q form and dedupe.
    private static int CommonZeroRoots(
        Interval interval,
        float scale,
        out double root0,
        out double root1)
    {
        root0 = double.NaN;
        root1 = double.NaN;
        var tolerance = 1e-6f * Math.Max(scale, 1e-12f);
        var axis = DominantDerivativeAxis(interval);
        var coefficient = Math.Max(
            Math.Abs(3.0 * axis.H0),
            Math.Max(Math.Abs(2.0 * axis.H1), Math.Abs(axis.H2)));
        if (coefficient <= 1e-12)
        {
            return 0;
        }

        var a = 3.0 * axis.H0 / coefficient;
        var b = 2.0 * axis.H1 / coefficient;
        var c = axis.H2 / coefficient;
        var count = 0;
        if (Math.Abs(a) > 1e-6)
        {
            var disc = (b * b) - (4 * a * c);
            if (disc >= 0)
            {
                var sd = Math.Sqrt(disc);
                var q = -0.5 * (b + (b >= 0.0 ? sd : -sd));
                if (q != 0.0)
                {
                    var r1 = q / a;
                    var r2 = c / q;
                    if (Math.Abs(r1 - r2) < 1e-9)
                    {
                        count = AddRoot(
                            interval,
                            tolerance,
                            (r1 + r2) * 0.5,
                            ref root0,
                            ref root1,
                            count);
                    }
                    else
                    {
                        count = AddRoot(interval, tolerance, r1, ref root0, ref root1, count);
                        count = AddRoot(interval, tolerance, r2, ref root0, ref root1, count);
                    }
                }
                else if (c == 0.0)
                {
                    count = AddRoot(interval, tolerance, 0.0, ref root0, ref root1, count);
                }
            }
        }
        else if (Math.Abs(b) > 1e-6)
        {
            count = AddRoot(interval, tolerance, -c / b, ref root0, ref root1, count);
        }

        if (count == 2 && root0 > root1)
        {
            (root0, root1) = (root1, root0);
        }

        return count;
    }

    // Accepted roots fill slots in order regardless of which quadratic candidate produced them,
    // so a rejected first candidate cannot leave root0 as NaN while root1 holds the valid root.
    private static int AddRoot(
        Interval interval,
        float tolerance,
        double r,
        ref double root0,
        ref double root1,
        int count)
    {
        if (r <= 1e-9 || r >= 1.0 - 1e-9)
        {
            return count;
        }

        var timestamp = (float)(interval.Start + ((interval.End - interval.Start) * r));
        if (timestamp <= interval.Start || timestamp >= interval.End)
        {
            return count;
        }

        var f = (float)r;
        if (Math.Abs(Deriv(interval.X, f)) > tolerance
            || Math.Abs(Deriv(interval.Y, f)) > tolerance
            || Math.Abs(Deriv(interval.Z, f)) > tolerance
            || Math.Abs(Deriv(interval.W, f)) > tolerance)
        {
            return count;
        }

        if (count == 0)
        {
            root0 = r;
        }
        else
        {
            root1 = r;
        }

        return count + 1;
    }

    private static AxisSpan DominantDerivativeAxis(Interval interval)
    {
        var best = interval.X;
        var bestScale = DerivativeScaleOf(interval.X);
        foreach (var span in new[] { interval.Y, interval.Z, interval.W })
        {
            var s = DerivativeScaleOf(span);
            if (s > bestScale)
            {
                bestScale = s;
                best = span;
            }
        }

        return best;
    }

    // Restrict a normalized cubic over [a,b] to [0,1] by direct substitution s = a + (b-a)w.
    private static AxisSpan RestrictNormalized(AxisSpan s, double a, double b)
    {
        var d = b - a;
        var d2 = d * d;
        var d3 = d2 * d;
        var a2 = a * a;
        var a3 = a2 * a;
        return new AxisSpan
        {
            H0 = (float)(s.H0 * d3),
            H1 = (float)(((3 * s.H0 * a) + s.H1) * d2),
            H2 = (float)(((3 * s.H0 * a2) + (2 * s.H1 * a) + s.H2) * d),
            H3 = (float)((s.H0 * a3) + (s.H1 * a2) + (s.H2 * a) + s.H3),
        };
    }

    private static AxisSpan ConstantSpan(float value) =>
        new() { H0 = 0f, H1 = 0f, H2 = 0f, H3 = value };

    // Restrict a Hermite (P0,P1,M0,M1 in parent parameter u) to [u0,u1] and normalize to a cubic
    // polynomial in s so derivatives need no parameter rescaling at evaluation time.
    private static AxisSpan RestrictSpan(PreparedSpan s, float u0, float u1)
    {
        var p0 = GlsSplineMath.Hermite(s.P0, s.P1, s.M0, s.M1, u0);
        var p1 = GlsSplineMath.Hermite(s.P0, s.P1, s.M0, s.M1, u1);
        var d = u1 - u0;
        var m0 = HermiteDerivative(s.P0, s.P1, s.M0, s.M1, u0) * d;
        var m1 = HermiteDerivative(s.P0, s.P1, s.M0, s.M1, u1) * d;

        // Standard Hermite basis to monomial coefficients.
        var h0 = (2f * p0) - (2f * p1) + m0 + m1;
        var h1 = (-3f * p0) + (3f * p1) - (2f * m0) - m1;
        return new AxisSpan { H0 = h0, H1 = h1, H2 = m0, H3 = p0 };
    }

    private static float HermiteDerivative(float p0, float p1, float m0, float m1, float u)
    {
        var u2 = u * u;
        return (((6f * u2) - (6f * u)) * p0)
            + (((3f * u2) - (4f * u) + 1f) * m0)
            + (((6f * u) - (6f * u2)) * p1)
            + (((3f * u2) - (2f * u)) * m1);
    }

    // Normalized restricted Hermite coefficients for one axis inside a union interval: position is
    // h0*s^3+h1*s^2+h2*s+h3 over s in [0,1]; derivative w.r.t. s is directly usable for arc length.
    private struct AxisSpan
    {
        public float H0;
        public float H1;
        public float H2;
        public float H3;
    }

    /// <summary>
    /// One axis's prepared interval: the spatial Hermite the existing tween already produces, plus
    /// whether the interval is reparameterizable (Catmull-Rom destination, not held, nonzero
    /// duration).
    /// </summary>
    internal readonly struct PreparedSpan
    {
        public readonly float Start;
        public readonly float End;
        public readonly float P0;
        public readonly float P1;
        public readonly float M0;
        public readonly float M1;
        public readonly bool Eligible;
        public readonly bool IsSpline;

        public PreparedSpan(
            float start,
            float end,
            float p0,
            float p1,
            float m0,
            float m1,
            bool eligible,
            bool isSpline)
        {
            Start = start;
            End = end;
            P0 = p0;
            P1 = p1;
            M0 = m0;
            M1 = m1;
            Eligible = eligible;
            IsSpline = isSpline;
        }
    }

    private sealed class Interval
    {
        public float Start { get; set; }

        public float End { get; set; }

        public float Length { get; set; }

        public AxisSpan X { get; set; }

        public AxisSpan Y { get; set; }

        public AxisSpan Z { get; set; }

        public AxisSpan W { get; set; }

        public float[] ArcSamples { get; set; } = Array.Empty<float>();

        public float[] ArcSlopes { get; set; } = Array.Empty<float>();

        public float SpeedAtStart { get; set; }

        public float SpeedAtEnd { get; set; }

        public float DerivativeScale { get; set; }
    }
}
}
