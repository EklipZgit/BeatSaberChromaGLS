using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChromaGLS.Helpers
{
    internal static class GlsScalarPath
    {
        internal static GlsDistancePath? Rotation(
            LightRotationGroupEffect.InitData init,
            BeatmapCallbacksController controller)
        {
            GlsSplineEventIndex.Get(controller).Rotation.TryGetValue(
                (init.groupId, init.elementId, (int)init.axis), out var channel);
            return Build(channel, GlsSplineSegment.Rotation, true);
        }

#if !V1_29_1
        internal static GlsDistancePath? FloatFx(
            FloatFxGroupEffect.InitData init,
            BeatmapCallbacksController controller)
        {
            GlsSplineEventIndex.Get(controller).FloatFx.TryGetValue(
                (init.groupId, init.elementId), out var channel);
            return Build(channel, GlsSplineSegment.FloatFx, false);
        }
#endif

        private static GlsDistancePath? Build<TEvent>(
            GlsSplineEventIndex.ScalarChannel<TEvent>? channel,
            Func<TEvent, GlsSplineSegment> prepare,
            bool unwrapAngles)
            where TEvent : BeatmapEventData
        {
            if (channel == null || !channel.DistancePaced)
            {
                return null;
            }

            var spans = new List<GlsDistancePath.PreparedSpan>();
            var offset = 0f;
            var previousEnd = 0f;
            for (TEvent? node = channel.First; node != null; node = (TEvent?)node.nextSameTypeEventData)
            {
                var span = prepare(node).Prepare();
                if (unwrapAngles && spans.Count > 0)
                {
                    offset += Mathf.Round((previousEnd - (span.P0 + offset)) / 360f) * 360f;
                }

                spans.Add(new GlsDistancePath.PreparedSpan(
                    span.Start,
                    span.End,
                    span.P0 + offset,
                    span.P1 + offset,
                    span.M0,
                    span.M1,
                    span.Eligible,
                    span.IsSpline));
                previousEnd = span.P1 + offset;
            }

            var path = new GlsDistancePath();
            path.Build(new[] { spans }, Vector4.zero);
            return path;
        }
    }
}
