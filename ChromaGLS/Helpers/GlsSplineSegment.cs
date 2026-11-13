using UnityEngine;

namespace ChromaGLS.Helpers
{
    internal readonly struct GlsSplineSegment
    {
        internal readonly float Start;
        internal readonly float End;
        internal readonly float From;
        internal readonly float To;
        internal readonly bool IsSpline;
        private readonly float _previous;
        private readonly float _previousTime;
        private readonly float _next;
        private readonly float _nextTime;
        private readonly bool _distancePaced;

        private GlsSplineSegment(
            float start,
            float end,
            float from,
            float to,
            float previous,
            float previousTime,
            float next,
            float nextTime,
            bool spline,
            bool distancePaced = false)
        {
            Start = start;
            End = end;
            From = from;
            To = to;
            _previous = previous;
            _previousTime = previousTime;
            _next = next;
            _nextTime = nextTime;
            IsSpline = spline;
            _distancePaced = distancePaced;
        }

        internal void Configure(GlsFloatTween tween)
        {
            if (!IsSpline)
            {
                tween.ClearSpline();
                return;
            }

            tween.SetSpline(
                _previous,
                _previousTime,
                From,
                Start,
                To,
                End,
                _next,
                _nextTime);
            tween.SetDistancePacing(_distancePaced);
        }

        internal GlsDistancePath.PreparedSpan Prepare()
        {
            float m0 = 0f, m1 = 0f;
            if (IsSpline)
            {
                GlsSplineMath.CatmullRomTimeTangents(
                    _previous,
                    From,
                    To,
                    _next,
                    _previousTime,
                    Start,
                    End,
                    _nextTime,
                    out m0,
                    out m1);
            }

            return new GlsDistancePath.PreparedSpan(
                Start,
                End,
                From,
                To,
                m0,
                m1,
                IsSpline && _distancePaced && End - Start > 1e-5f,
                IsSpline && End - Start > 1e-5f);
        }

        internal static GlsSplineSegment Translation(
            LightTranslationBeatmapEventData current,
            Vector2 translationLimits,
            Vector2 distributionLimits,
            bool mirrored)
        {
            var end = (LightTranslationBeatmapEventData?)current.nextSameTypeEventData;
            var previous = (LightTranslationBeatmapEventData?)current.previousSameTypeEventData;
            var next = (LightTranslationBeatmapEventData?)end?.nextSameTypeEventData;
            var endTime = end?.time ?? float.MaxValue;
            var duration = endTime - current.time;
            var from = TranslationValue(current, translationLimits, distributionLimits, mirrored);
            var to = end == null || end.usePreviousEventValue
                ? from
                : TranslationValue(end, translationLimits, distributionLimits, mirrored);
            var spline = end != null && !end.usePreviousEventValue
                && GlsSplineMath.IsCatmullRom((int)end.easeType) && duration > 0f;

            // The game's event processors already copy held values. Keep the immediate neighbor's
            // timestamp rather than searching back to the node that originally supplied its value.
            return new GlsSplineSegment(
                current.time,
                endTime,
                from,
                to,
                previous == null ? from : TranslationValue(previous, translationLimits, distributionLimits, mirrored),
                previous?.time ?? current.time - duration,
                next == null ? to : TranslationValue(next, translationLimits, distributionLimits, mirrored),
                next?.time ?? endTime + duration,
                spline,
                end != null && GlsSplineMath.IsDistancePaced((int)end.easeType));
        }

        private static float TranslationValue(
            LightTranslationBeatmapEventData node,
            Vector2 translationLimits,
            Vector2 distributionLimits,
            bool mirrored) =>
            LightTranslationGroupEffect.ComputeTranslation(
                node.translation,
                translationLimits,
                node.distribution,
                distributionLimits,
                mirrored);

        internal static GlsSplineSegment Rotation(LightRotationBeatmapEventData current)
        {
            var end = (LightRotationBeatmapEventData?)current.nextSameTypeEventData;
            var previous = (LightRotationBeatmapEventData?)current.previousSameTypeEventData;
            var next = (LightRotationBeatmapEventData?)end?.nextSameTypeEventData;
            var from = Mathf.Repeat(current.rotation, 360f);
            var to = end == null
                ? from
                : LightRotationGroupEffect.ComputeTargetAngle(
                    from,
                    end.usePreviousEventValue ? from : Mathf.Repeat(end.rotation, 360f),
                    end.loopCount,
                    end.rotationDirection);
            var p0 = from;
            if (previous != null)
            {
                var previousAngle = Mathf.Repeat(previous.rotation, 360f);
                p0 = from - (LightRotationGroupEffect.ComputeTargetAngle(
                    previousAngle,
                    from,
                    current.loopCount,
                    current.rotationDirection) - previousAngle);
            }

            var p3 = to;
            if (next != null && (!next.usePreviousEventValue || next.loopCount > 0))
            {
                p3 = LightRotationGroupEffect.ComputeTargetAngle(
                    to,
                    next.usePreviousEventValue ? to : Mathf.Repeat(next.rotation, 360f),
                    next.loopCount,
                    next.rotationDirection);
            }

            var endTime = end?.time ?? float.MaxValue;
            var duration = endTime - current.time;
            var spline = end != null && (!end.usePreviousEventValue || end.loopCount > 0)
                && GlsSplineMath.IsCatmullRom((int)end.easeType) && duration > 0f;
            return new GlsSplineSegment(
                current.time,
                endTime,
                from,
                to,
                p0,
                previous?.time ?? current.time - duration,
                p3,
                next?.time ?? endTime + duration,
                spline,
                end != null && GlsSplineMath.IsDistancePaced((int)end.easeType));
        }

#if !V1_29_1
        internal static GlsSplineSegment FloatFx(FloatFxBeatmapEventData current)
        {
            var end = (FloatFxBeatmapEventData?)current.nextSameTypeEventData;
            var previous = (FloatFxBeatmapEventData?)current.previousSameTypeEventData;
            var next = (FloatFxBeatmapEventData?)end?.nextSameTypeEventData;
            var endTime = end?.time ?? float.MaxValue;
            var duration = endTime - current.time;
            var to = end == null || end.usePreviousEventValue ? current.value : end.value;
            var spline = end != null && !end.usePreviousEventValue
                && GlsSplineMath.IsCatmullRom((int)end.easeType) && duration > 0f;
            return new GlsSplineSegment(
                current.time,
                endTime,
                current.value,
                to,
                previous?.value ?? current.value,
                previous?.time ?? current.time - duration,
                next?.value ?? to,
                next?.time ?? endTime + duration,
                spline,
                end != null && GlsSplineMath.IsDistancePaced((int)end.easeType));
        }
#endif
    }
}
