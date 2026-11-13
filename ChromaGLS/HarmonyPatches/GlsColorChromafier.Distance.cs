using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ChromaGLS.Helpers;
using HarmonyLib;
using Tweening;

namespace ChromaGLS.HarmonyPatches
{
    internal static partial class GlsColorChromafier
    {
        private static readonly ConditionalWeakTable<LightColorGroupEffect, ColorDistanceState> ColorDistanceStates = new();

        internal static void PrepareColorDistancePaths(
            LightColorGroupEffect instance,
            LightColorGroupEffect.InitData init,
            BeatmapCallbacksController controller)
        {
            if (!GlsSplineEventIndex.Get(controller).Color.TryGetValue(
                (init.groupId, init.elementId), out var channel) || !channel.DistancePaced)
            {
                return;
            }

            var states = ColorStates(channel.First);
            var normal = GlsColorDistancePaths.Create(
                states,
                ColorDistanceModes,
                node => PrepareColorDistanceSpan(instance, node, false));
            var boost = GlsColorDistancePaths.Create(
                states,
                ColorDistanceModes,
                node => PrepareColorDistanceSpan(instance, node, true),
                normal);
            ColorDistanceStates.Add(instance, new ColorDistanceState(normal, boost));
        }

        private static IEnumerable<LightColorBeatmapEventData> ColorStates(LightColorBeatmapEventData first)
        {
            for (var node = first; node != null; node = node.nextSameTypeEventData as LightColorBeatmapEventData)
            {
                yield return node;
            }
        }

        private static GlsColorDistancePaths.Modes ColorDistanceModes(LightColorBeatmapEventData node)
        {
            var end = node.nextSameTypeEventData as LightColorBeatmapEventData;
            var paced = 0;
            if (end != null && end.time > node.time)
            {
                ReadColorSplineModes(end, out _, out paced);
            }

            return new GlsColorDistancePaths.Modes(
                paced,
                end != null && IsHsvEasingType(end) ? 2 : 0,
                node.strobeBeatFrequency > 0 || ResolveStrobeInterval(node).HasValue);
        }

        private static GlsColorDistancePaths.Span PrepareColorDistanceSpan(
            LightColorGroupEffect instance,
            LightColorBeatmapEventData node,
            bool boost)
        {
            var end = node.nextSameTypeEventData as LightColorBeatmapEventData;
            var channels = 0;
            var modes = ColorDistanceModes(node);
            var hsv = modes.Space == 2;
            var track = default(SplineColorTrack);
            if (end != null)
            {
                ReadColorSplineModes(end, out channels, out _);
                var previous = node.previousSameTypeEventData as LightColorBeatmapEventData;
                var next = end.nextSameTypeEventData as LightColorBeatmapEventData;
                var duration = end.time - node.time;
                PrepareColorSplineTrack(
                    ref track,
                    instance,
                    node,
                    end,
                    previous,
                    next,
                    previous?.time ?? node.time - duration,
                    next?.time ?? end.time + duration,
                    channels,
                    hsv,
                    boost);
            }
            else
            {
                track.From = track.To = ResolveSplineColor(instance, node, boost);
            }

            return new GlsColorDistancePaths.Span
            {
                Start = node.time,
                End = end?.time ?? float.MaxValue,
                Paced = modes.Paced,
                Space = modes.Space,
                Alpha = (channels & 1) != 0
                    ? track.Brightness.Export()
                    : GlsColorDistancePaths.Scalar(track.From.Brightness, track.To.Brightness, 0, 0, false),
                StrobeAlpha = (channels & 1) != 0
                    ? track.StrobeAlpha.Export()
                    : GlsColorDistancePaths.Scalar(
                        track.From.Strobe.a * track.From.StrobeBrightness,
                        track.To.Strobe.a * track.To.StrobeBrightness,
                        0,
                        0,
                        false),
                Color = (channels & 2) != 0
                    ? track.Normal.Export()
                    : new GlsColorDistancePaths.Curve
                    {
                        From = GlsSplineMath.ToSplineSpace(track.From.Normal, hsv),
                        To = GlsSplineMath.ToSplineSpace(track.To.Normal, hsv),
                    },
                StrobeColor = (channels & 4) != 0
                    ? track.Strobe.Export()
                    : new GlsColorDistancePaths.Curve
                    {
                        From = GlsSplineMath.ToSplineSpace(track.From.Strobe, hsv),
                        To = GlsSplineMath.ToSplineSpace(track.To.Strobe, hsv),
                    },
            };
        }

        private sealed class ColorDistanceState
        {
            internal ColorDistanceState(GlsColorDistancePaths? normal, GlsColorDistancePaths? boost)
            {
                Normal = normal;
                Boost = boost;
            }

            internal GlsColorDistancePaths? Normal { get; }

            internal GlsColorDistancePaths? Boost { get; }
        }
    }

    // Keep preparation installed while disabled, like scalar spline constructors, so a later toggle
    // can use maps and effects already created during this level.
    [HarmonyPatch(
        typeof(LightColorGroupEffect),
        MethodType.Constructor,
        new[] { typeof(LightColorGroupEffect.InitData), typeof(LightWithIdManager), typeof(SongTimeTweeningManager), typeof(ColorManager), typeof(BeatmapCallbacksController), typeof(IBpmController) })]
    internal static class GlsColorDistancePreparePatch
    {
        private static void Postfix(
            LightColorGroupEffect __instance,
            LightColorGroupEffect.InitData initData,
            BeatmapCallbacksController beatmapCallbacksController) =>
            GlsColorChromafier.PrepareColorDistancePaths(__instance, initData, beatmapCallbacksController);
    }
}
