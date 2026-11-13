using System.Collections.Generic;
using UnityEngine;

namespace ChromaGLS.Helpers
{
    internal sealed class GlsTranslationRuntime
    {
        private readonly BeatmapCallbacksController _controller;
        private readonly GlsDistancePath _path = new();
        private float _lastTime = float.NaN;
        private bool _covered;
        private Vector3 _position;

        private GlsTranslationRuntime(
            LightTranslationGroupEffect.InitData init,
            BeatmapCallbacksController controller,
            GlsSplineEventIndex.TranslationChannels channels)
        {
            _controller = controller;
            var spans = new List<GlsDistancePath.PreparedSpan>[3];
            var initial = Vector3.zero;
            for (var axis = 0; axis < 3; axis++)
            {
                var limits = axis == 0 ? init.xTranslationLimits
                    : axis == 1 ? init.yTranslationLimits : init.zTranslationLimits;
                var distribution = axis == 0 ? init.xDistributionLimits
                    : axis == 1 ? init.yDistributionLimits : init.zDistributionLimits;
                var mirrored = axis == 0 ? init.xMirrored : axis == 1 ? init.yMirrored : init.zMirrored;
                var nodes = channels.Axes[axis];
                var prepared = spans[axis] = new List<GlsDistancePath.PreparedSpan>(nodes.Count);
                if (nodes.Count > 0)
                {
                    initial[axis] = LightTranslationGroupEffect.ComputeTranslation(
                        0f,
                        limits,
                        0f,
                        distribution,
                        mirrored);
                }

                foreach (var node in nodes)
                {
                    prepared.Add(GlsSplineSegment.Translation(node, limits, distribution, mirrored).Prepare());
                }
            }

            _path.Build(spans, initial);
        }

        internal static GlsTranslationRuntime? Create(
            LightTranslationGroupEffect.InitData init,
            BeatmapCallbacksController controller)
        {
            var index = GlsSplineEventIndex.Get(controller);
            return index.Translation.TryGetValue((init.groupId, init.elementId), out var channels)
                && channels.DistancePaced
                ? new GlsTranslationRuntime(init, controller, channels)
                : null;
        }

        internal bool TryEvaluate(out Vector3 position)
        {
            var time = _controller.songTime;
            if (time != _lastTime)
            {
                _covered = _path.TryEvaluate(time, out _position);
                _lastTime = time;
            }

            position = _position;
            return _covered;
        }
    }
}
