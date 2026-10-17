using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CustomJSONData.CustomBeatmap;
using HarmonyLib;
using UnityEngine;

#pragma warning disable SA1201, SA1202, SA1204, SA1512

#if !PRE_V1_37_1
using _LightColorBaseData = BeatmapSaveDataVersion3.LightColorBaseData;
using _LightColorEventBox = BeatmapSaveDataVersion3.LightColorEventBox;
using _LightColorEventBoxConverter = BeatmapDataLoaderVersion3.BeatmapDataLoader.LightColorEventBoxConverter;
using _TransitionType = BeatmapSaveDataVersion3.TransitionType;
#else
using _LightColorBaseData = BeatmapSaveDataVersion3.BeatmapSaveData.LightColorBaseData;
using _LightColorEventBox = BeatmapSaveDataVersion3.BeatmapSaveData.LightColorEventBox;
using _LightColorEventBoxConverter = BeatmapDataLoader.LightColorEventBoxConvertor;
using _TransitionType = BeatmapSaveDataVersion3.BeatmapSaveData.TransitionType;
#endif

namespace ChromaGLS.HarmonyPatches
{
    [HarmonyPatch]
    internal static class GlsConverterPatches
    {
        // Parse each source node once while loading, then share its instructions across all selected lights.
        private static readonly ConditionalWeakTable<LightColorBeatmapEventDataBox, List<ColorEventMetadata>> _boxEventMetadata
            = new();

        private static readonly ConditionalWeakTable<LightColorBeatmapEventDataBox, BoxColorDistributionContext> _boxColorDistributionContexts
            = new();

        // Keep strobeColorEasing on Instant nodes, but apply it only when the light has an incoming transition.
        // Extend must still discard the predecessor's interval easing.
        private static readonly (string Key, bool RequiresTransition, Func<object?, bool> IsValid)[] EasingKeyRules =
        {
            ("easing", true, IsValidEasingId),
            ("colorEasing", true, IsValidTrackEasingId),
            ("strobeColorEasing", false, IsValidTrackEasingId),

            // Only HSV needs metadata. RGB and unknown values use the native RGB default.
            ("easingType", true, IsValidEasingType),
        };

        private static bool IsValidEasingId(object? raw)
        {
            return GlsEasingRuntimePatch.TryGetCustomEasing(raw, out _);
        }

        private static bool IsValidTrackEasingId(object? raw)
        {
            return GlsEasingRuntimePatch.TryGetTrackEasing(raw, out _);
        }

        private static bool IsValidEasingType(object? raw)
        {
            return raw is string easingType && easingType == "HSV";
        }

        internal static bool TryGetColorDistributionContext(BeatmapEventData? eventData, out EventColorDistributionContext? context)
        {
            if (eventData == null)
            {
                context = null;
                return false;
            }

            // Read context from the event copy, since transforms can replace the original instance.
            context = (eventData as ColorDistributedLightColorEventData)?.ColorDistributionContext
                ?? (eventData as ExtendedLightColorBeatmapEventData)?.ColorDistributionContext;
            return context != null;
        }

        // Select the LightColorEventBox overload rather than the base converter's Convert method.
        private static void LightColorEventBoxConverterPostfix(
            _LightColorEventBox saveData,
            BeatmapEventDataBox __result)
        {
            if (__result is not LightColorBeatmapEventDataBox box)
            {
                return;
            }

            List<ColorEventMetadata> perEventData = new(saveData.lightColorBaseDataList?.Count ?? 0);
            bool anyCustom = false;
            GlsDeserializePatch.TryGetBoxCustomData(saveData, out CustomData? boxCustomData);
            bool anyColorDistributions = HasColorDistributionData(boxCustomData);
            foreach (_LightColorBaseData item in saveData.lightColorBaseDataList ?? new List<_LightColorBaseData>())
            {
                bool isExtension = item.transitionType == _TransitionType.Extend;
                CustomData? authoredData = (item as ICustomData)?.customData;

                // Defer Extend inheritance until CopyColorDataFrom establishes the predecessor for each light.
                // Inheriting here would incorrectly share one light's data across the box.
                CustomData? effectiveCustomData = authoredData?.Count > 0 ? authoredData : null;

                // Extend inherits color and strobeEasing, but incoming transition easing belongs to this node.
                // Copy before removing invalid keys so the save data and predecessor remain unchanged.
                foreach ((string easingKey, bool requiresTransition, Func<object?, bool> isValid) in EasingKeyRules)
                {
                    object? authoredEase = authoredData?.Get<object>(easingKey);
                    bool validEase = (!requiresTransition || item.transitionType == _TransitionType.Interpolate || isExtension)
                        && isValid(authoredEase);
                    bool inheritedEase = effectiveCustomData?.ContainsKey(easingKey) == true;
                    if ((isExtension && (inheritedEase || validEase)) || (!validEase && inheritedEase))
                    {
                        effectiveCustomData = effectiveCustomData?.Copy() ?? new CustomData();

                        effectiveCustomData.TryRemove(easingKey, out _);
                        if (validEase)
                        {
                            effectiveCustomData[easingKey] = authoredEase;
                        }
                    }
                }

                perEventData.Add(new ColorEventMetadata(effectiveCustomData));

                // Even an Extend node with no customData needs an entry so Unpack can attach inherited data later.
                anyCustom |= isExtension || effectiveCustomData != null;
                anyColorDistributions |= HasColorDistributionData(effectiveCustomData);
            }

            if (anyCustom || anyColorDistributions)
            {
                _boxEventMetadata.Add(box, perEventData);
            }

            if (anyColorDistributions)
            {
                _boxColorDistributionContexts.Add(box, CreateBoxColorDistributionContext(saveData, box));
            }
        }

        private static bool HasColorDistributionData(CustomData? customData)
        {
            return customData != null
                && (customData.ContainsKey("colorDistributions")
                    || customData.ContainsKey("strobeColorDistributions"));
        }

        // Use native filter order so random, reverse, limit, chunk, and step filters match the game.
        private static BoxColorDistributionContext CreateBoxColorDistributionContext(
            _LightColorEventBox saveData,
            LightColorBeatmapEventDataBox box)
        {
            List<int> elementIds = new(box.indexFilter.Count);
            List<int> distributionOrders = new(box.indexFilter.Count);
            foreach (var entry in box.indexFilter)
            {
                elementIds.Add(entry.element);
                distributionOrders.Add(entry.distributionOrder);
            }

            GlsDeserializePatch.TryGetBoxCustomData(saveData, out CustomData? boxCustomData);

            object? colorDistributions = boxCustomData?.Get<object>("colorDistributions");
            object? strobeColorDistributions = boxCustomData?.Get<object>("strobeColorDistributions");
            return new BoxColorDistributionContext(
                GlsColorDistribution.Parse(colorDistributions),
                GlsColorDistribution.Parse(strobeColorDistributions),
                GlsColorDistribution.BuildProgress(elementIds.ToArray(), distributionOrders.ToArray()));
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(LightColorBeatmapEventDataBox), "Unpack")]
#if PRE_V1_42_1
        private static void LightColorBeatmapEventDataBoxUnpackPostfix(
            LightColorBeatmapEventDataBox __instance,
            float groupBoxBeat,
            int elementId,
            int durationOrderIndex,
            float maxBeat,
            List<BeatmapEventData> output)
#else
        private static void LightColorBeatmapEventDataBoxUnpackPostfix(
            LightColorBeatmapEventDataBox __instance,
            float groupBoxBeat,
            int elementId,
            int durationOrderIndex,
            float maxBeat,
            ref IEnumerable<BeatmapEventData> __result)
#endif
        {
            bool hasEventMetadata = _boxEventMetadata.TryGetValue(
                __instance,
                out List<ColorEventMetadata> perEventData);
            bool hasColorDistributionContext = _boxColorDistributionContexts.TryGetValue(__instance, out BoxColorDistributionContext boxColorDistributionContext);
            if (!hasEventMetadata)
            {
                return;
            }

            // Main.dll is publicized, so read the same private data directly rather than reflecting every emitted node.
            // Reproduce the game's predicate so custom data follows emitted nodes rather than raw node indices.
            IReadOnlyList<LightColorBaseData> baseData = __instance._lightColorBaseDataList;
            float beatStep = __instance._beatStep;

            List<ColorEventMetadata> emittedMetadata = new(baseData.Count);
            for (int i = 0; i < baseData.Count; i++)
            {
                float beat = groupBoxBeat + baseData[i].beat + (durationOrderIndex * beatStep);
                if (beat < maxBeat)
                {
                    emittedMetadata.Add(perEventData[i]);
                }
            }

            perEventData = emittedMetadata;
            int boxCount = perEventData.Count;
#if PRE_V1_42_1
            List<BeatmapEventData> outputList = output;
#else
            List<BeatmapEventData> outputList = __result as List<BeatmapEventData>
                ?? __result?.ToList()
                ?? new List<BeatmapEventData>();
            __result = outputList;
#endif
            int outputStart = outputList.Count - boxCount;
            if (outputStart < 0)
            {
                return;
            }

            for (int i = 0; i < boxCount; i++)
            {
                int outIdx = outputStart + i;
                if (outputList[outIdx] is not LightColorBeatmapEventData eventData)
                {
                    continue;
                }

                ColorEventMetadata metadata = perEventData[i];
                CustomData? customData = metadata.CustomData;
                IReadOnlyList<GlsColorDistribution.Instruction> eventNormalColorDistributions = metadata.NormalColorDistributions;
                IReadOnlyList<GlsColorDistribution.Instruction> eventStrobeColorDistributions = metadata.StrobeColorDistributions;
                EventColorDistributionContext? distributionContext = hasColorDistributionContext
                    && boxColorDistributionContext.TryGetProgress(elementId, out GlsColorDistribution.Progress progress)
                    && (boxColorDistributionContext.HasColorDistributions
                        || eventNormalColorDistributions.Count > 0
                        || eventStrobeColorDistributions.Count > 0)
                        ? new EventColorDistributionContext(
                            boxColorDistributionContext.NormalColorDistributions,
                            boxColorDistributionContext.StrobeColorDistributions,
                            eventNormalColorDistributions,
                            eventStrobeColorDistributions,
                            progress)
                        : null;

#if PRE_V1_37_1
                bool isExtension = eventData.transitionType == BeatmapEventTransitionType.Extend;
#else
                bool isExtension = eventData.usePreviousValue;
#endif
                if (isExtension)
                {
                    // The per-light predecessor exists only after insertion. CopyColorDataFrom will merge its customData and distributions.
                    outputList[outIdx] = new ExtendedLightColorBeatmapEventData(
                        eventData,
                        customData ?? new CustomData(),
                        distributionContext);
                    continue;
                }

                if (customData != null && eventData is not CustomLightColorBeatmapEventData)
                {
                    CustomLightColorBeatmapEventData customEvent = new(
                        eventData.time,
                        eventData.groupId,
                        eventData.elementId,
#if PRE_V1_37_1
                        eventData.transitionType,
#else
                        eventData.usePreviousValue,

                        // Preserve Extend/usePrevious and its easing metadata while leaving the native tween clock linear.
                        GlsEasingRuntimePatch.GetCustomRuntimeEase(customData.Get<object>("easing"), eventData.easeType),
#endif
                        eventData.colorType,
                        eventData.brightness,
                        eventData.strobeBeatFrequency,
#if !V1_29_1
                        eventData.strobeBrightness,
                        eventData.strobeFade,
#endif
                        customData);
                    outputList[outIdx] = customEvent;
                    eventData = customEvent;
                }

                if (distributionContext != null)
                {
                    // Box distributions still need an event wrapper when the node has no customData.
                    outputList[outIdx] = new ColorDistributedLightColorEventData(
                        eventData,
                        customData ?? new CustomData(),
                        distributionContext);
                }
            }
        }

        // CopyColorDataFrom runs after the per-light predecessor is linked, including across boxes.
        // Rebuild customData on each call so changing the predecessor cannot leave old keys behind.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(LightColorBeatmapEventData), nameof(LightColorBeatmapEventData.CopyColorDataFrom))]
        private static void LightColorBeatmapEventDataCopyColorDataFromPostfix(
            LightColorBeatmapEventData __instance,
            LightColorBeatmapEventData lightColorBeatmapEventData)
        {
            (__instance as ExtendedLightColorBeatmapEventData)?.MergeInheritedData(lightColorBeatmapEventData);
        }

        // Derive directly from the native event so CustomJSONData's one-level type lookup still finds LightColorBeatmapEventData.
        private sealed class ColorDistributedLightColorEventData : LightColorBeatmapEventData, ICustomData
        {
            public ColorDistributedLightColorEventData(
                LightColorBeatmapEventData source,
                CustomData data,
                EventColorDistributionContext context)
                : base(
                    source.time,
                    source.groupId,
                    source.elementId,
#if PRE_V1_37_1
                    source.transitionType,
#else
                    source.usePreviousValue,
                    source.easeType,
#endif
                    source.colorType,
                    source.brightness,
#if V1_29_1
                    source.strobeBeatFrequency)
#else
                    source.strobeBeatFrequency,
                    source.strobeBrightness,
                    source.strobeFade)
#endif
            {
                customData = data;
                ColorDistributionContext = context;
            }

            public CustomData customData { get; }

            public EventColorDistributionContext ColorDistributionContext { get; }

            // Event copies own their mutable customData but can share parsed distributions and filter coordinates.
            public override BeatmapDataItem GetCopy()
            {
                return new ColorDistributedLightColorEventData(this, customData.Copy(), ColorDistributionContext);
            }
        }

        // Keep authored data separate from inherited data so CopyColorDataFrom can rebuild it when the predecessor changes.
        // Direct inheritance from the native event also preserves CustomJSONData callback matching.
        private sealed class ExtendedLightColorBeatmapEventData : LightColorBeatmapEventData, ICustomData
        {
            public ExtendedLightColorBeatmapEventData(
                LightColorBeatmapEventData source,
                CustomData authoredData,
                EventColorDistributionContext? context)
                : base(
                    source.time,
                    source.groupId,
                    source.elementId,
#if PRE_V1_37_1
                    source.transitionType,
#else
                    source.usePreviousValue,
                    GlsEasingRuntimePatch.GetCustomRuntimeEase(authoredData.Get<object>("easing"), source.easeType),
#endif
                    source.colorType,
                    source.brightness,
#if V1_29_1
                    source.strobeBeatFrequency)
#else
                    source.strobeBeatFrequency,
                    source.strobeBrightness,
                    source.strobeFade)
#endif
            {
                _authoredData = authoredData;
                customData = authoredData;
                ColorDistributionContext = context;
            }

            private readonly CustomData _authoredData;

            public CustomData customData { get; private set; }

            public EventColorDistributionContext? ColorDistributionContext { get; private set; }

            // Extend inherits the preceding light event's customData, except easing for the incoming transition.
            // Only valid easing authored on this node can override that transition.
            internal void MergeInheritedData(LightColorBeatmapEventData source)
            {
                CustomData merged = (source as ICustomData)?.customData.Copy() ?? new CustomData();
                foreach ((string easingKey, _, Func<object?, bool> isValid) in EasingKeyRules)
                {
                    merged.TryRemove(easingKey, out _);
                    object? authored = _authoredData.Get<object>(easingKey);
                    if (isValid(authored))
                    {
                        merged[easingKey] = authored;
                    }
                }

                customData = merged;

                // usePrevious inherits the predecessor's distributions and filter progress along with its raw colors.
                // Without this context, the extension would render the unshifted color.
                TryGetColorDistributionContext(source, out EventColorDistributionContext? inheritedContext);
                ColorDistributionContext = inheritedContext;
            }

            // Keep both the merged data for immediate use and the authored data for copies that are inserted again.
            public override BeatmapDataItem GetCopy()
            {
                ExtendedLightColorBeatmapEventData copy = new(this, _authoredData.Copy(), ColorDistributionContext);
                copy.customData = customData.Copy();
                return copy;
            }
        }

        private readonly struct ColorEventMetadata
        {
            public ColorEventMetadata(CustomData? customData)
            {
                CustomData = customData;
                NormalColorDistributions = GlsColorDistribution.Parse(customData?.Get<object>("colorDistributions"));
                StrobeColorDistributions = GlsColorDistribution.Parse(customData?.Get<object>("strobeColorDistributions"));
            }

            public CustomData? CustomData { get; }

            public IReadOnlyList<GlsColorDistribution.Instruction> NormalColorDistributions { get; }

            public IReadOnlyList<GlsColorDistribution.Instruction> StrobeColorDistributions { get; }
        }

        private sealed class BoxColorDistributionContext
        {
            private readonly IReadOnlyDictionary<int, GlsColorDistribution.Progress> _progressByElement;

            public BoxColorDistributionContext(
                IReadOnlyList<GlsColorDistribution.Instruction> colorDistributions,
                IReadOnlyList<GlsColorDistribution.Instruction> strobeColorDistributions,
                IReadOnlyDictionary<int, GlsColorDistribution.Progress> progressByElement)
            {
                NormalColorDistributions = colorDistributions;
                StrobeColorDistributions = strobeColorDistributions;
                _progressByElement = progressByElement;
            }

            public IReadOnlyList<GlsColorDistribution.Instruction> NormalColorDistributions { get; }

            public IReadOnlyList<GlsColorDistribution.Instruction> StrobeColorDistributions { get; }

            public bool HasColorDistributions => NormalColorDistributions.Count > 0 || StrobeColorDistributions.Count > 0;

            public bool TryGetProgress(int elementId, out GlsColorDistribution.Progress progress)
            {
                return _progressByElement.TryGetValue(elementId, out progress);
            }
        }

        internal sealed class EventColorDistributionContext
        {
            public EventColorDistributionContext(
                IReadOnlyList<GlsColorDistribution.Instruction> boxNormalColorDistributions,
                IReadOnlyList<GlsColorDistribution.Instruction> boxStrobeColorDistributions,
                IReadOnlyList<GlsColorDistribution.Instruction> eventNormalColorDistributions,
                IReadOnlyList<GlsColorDistribution.Instruction> eventStrobeColorDistributions,
                GlsColorDistribution.Progress progress)
            {
                BoxNormalColorDistributions = boxNormalColorDistributions;
                BoxStrobeColorDistributions = boxStrobeColorDistributions;
                EventNormalColorDistributions = eventNormalColorDistributions;
                EventStrobeColorDistributions = eventStrobeColorDistributions;
                Progress = progress;
            }

            public IReadOnlyList<GlsColorDistribution.Instruction> BoxNormalColorDistributions { get; }

            public IReadOnlyList<GlsColorDistribution.Instruction> BoxStrobeColorDistributions { get; }

            public IReadOnlyList<GlsColorDistribution.Instruction> EventNormalColorDistributions { get; }

            public IReadOnlyList<GlsColorDistribution.Instruction> EventStrobeColorDistributions { get; }

            public GlsColorDistribution.Progress Progress { get; }

            public bool HasNormalColorDistributions => BoxNormalColorDistributions.Count > 0 || EventNormalColorDistributions.Count > 0;

            public bool HasStrobeColorDistributions => BoxStrobeColorDistributions.Count > 0 || EventStrobeColorDistributions.Count > 0;

            public Color ApplyNormal(Color color)
            {
                return GlsColorDistribution.Apply(color, BoxNormalColorDistributions, EventNormalColorDistributions, Progress);
            }

            public Color ApplyStrobe(Color color)
            {
                return GlsColorDistribution.Apply(color, BoxStrobeColorDistributions, EventStrobeColorDistributions, Progress);
            }
        }

        // Plugin.PersistentPatchTypes needs this class to be accessible when it restores metadata patches.
        [HarmonyPatch]
        internal static class LightColorEventBoxConverterPatch
        {
            [HarmonyTargetMethod]
            private static MethodBase TargetMethod()
            {
                return AccessTools.GetDeclaredMethods(typeof(_LightColorEventBoxConverter))
                    .Single(method => method.Name == "Convert"
                        && method.GetParameters().Length == 2
                        && method.GetParameters()[0].ParameterType == typeof(_LightColorEventBox));
            }

            [HarmonyPostfix]
            private static void Postfix(
                _LightColorEventBox saveData,
                BeatmapEventDataBox __result)
            {
                LightColorEventBoxConverterPostfix(saveData, __result);
            }
        }
    }
}
