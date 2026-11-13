using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ChromaGLS.HarmonyPatches;

namespace ChromaGLS.Helpers
{
    internal sealed class GlsSplineEventIndex
    {
        private static readonly ConditionalWeakTable<BeatmapCallbacksController, GlsSplineEventIndex> Indexes = new();

        // A controller owns the generated links. Scalar tracks only retain their first node, rather
        // than copy every event reference into another list for each environment element.
        private GlsSplineEventIndex(BeatmapCallbacksController controller)
        {
            foreach (var item in controller._beatmapData.allBeatmapDataItems)
            {
                switch (item)
                {
                    case LightTranslationBeatmapEventData node:
                    {
                        var key = (node.groupId, node.elementId);
                        if (!Translation.TryGetValue(key, out var channels))
                        {
                            channels = new TranslationChannels();
                            Translation.Add(key, channels);
                        }

                        channels.Axes[(int)node.axis].Add(node);
                        channels.DistancePaced |= !node.usePreviousEventValue
                            && GlsSplineMath.IsDistancePaced((int)node.easeType);
                        break;
                    }

                    case LightRotationBeatmapEventData node:
                    {
                        var key = (node.groupId, node.elementId, (int)node.axis);
                        if (!Rotation.TryGetValue(key, out var channel))
                        {
                            channel = new ScalarChannel<LightRotationBeatmapEventData>(node);
                            Rotation.Add(key, channel);
                        }

                        channel.DistancePaced |= (!node.usePreviousEventValue || node.loopCount > 0)
                            && GlsSplineMath.IsDistancePaced((int)node.easeType);
                        break;
                    }

                    case LightColorBeatmapEventData node:
                    {
                        var key = (node.groupId, node.elementId);
                        if (!Color.TryGetValue(key, out var channel))
                        {
                            channel = new ScalarChannel<LightColorBeatmapEventData>(node);
                            Color.Add(key, channel);
                        }

                        channel.DistancePaced |= GlsColorChromafier.UsesDistancePacing(node);
                        break;
                    }

#if !V1_29_1
                    case FloatFxBeatmapEventData node:
                    {
                        var key = (node.groupId, node.elementId);
                        if (!FloatFx.TryGetValue(key, out var channel))
                        {
                            channel = new ScalarChannel<FloatFxBeatmapEventData>(node);
                            FloatFx.Add(key, channel);
                        }

                        channel.DistancePaced |= !node.usePreviousEventValue
                            && GlsSplineMath.IsDistancePaced((int)node.easeType);
                        break;
                    }

#endif
                }
            }
        }

        internal Dictionary<(int Group, int Element), TranslationChannels> Translation { get; } = new();

        internal Dictionary<(int Group, int Element, int Axis), ScalarChannel<LightRotationBeatmapEventData>> Rotation { get; } = new();

        internal Dictionary<(int Group, int Element), ScalarChannel<LightColorBeatmapEventData>> Color { get; } = new();

#if !V1_29_1
        internal Dictionary<(int Group, int Element), ScalarChannel<FloatFxBeatmapEventData>> FloatFx { get; } = new();

#endif

        internal static GlsSplineEventIndex Get(BeatmapCallbacksController controller) =>
            Indexes.GetValue(controller, key => new GlsSplineEventIndex(key));

        internal sealed class TranslationChannels
        {
            internal List<LightTranslationBeatmapEventData>[] Axes { get; } = new List<LightTranslationBeatmapEventData>[] { new(), new(), new() };

            internal bool DistancePaced { get; set; }
        }

        internal sealed class ScalarChannel<TEvent>
            where TEvent : BeatmapEventData
        {
            internal ScalarChannel(TEvent first)
            {
                First = first;
            }

            internal TEvent First { get; }

            internal bool DistancePaced { get; set; }
        }
    }
}
