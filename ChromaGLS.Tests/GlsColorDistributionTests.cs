using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CustomJSONData.CustomBeatmap;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class GlsColorDistributionTests
{
    // ChunkAndLightModesUseDenseAffectedOrder locks mode B to selected chunks and optional l mode to selected lights without counting skipped IDs.
    [Test]
    public void ChunkAndLightModesUseDenseAffectedOrder()
    {
        MethodInfo buildProgress = GetColorDistributionMethod("BuildProgressForTesting");

        Vector2[] progress = (Vector2[])buildProgress.Invoke(
            null,
            [new[] { 0, 1, 4, 5 }, new[] { 0, 0, 2, 2 }])!;

        AssertProgress(progress[0], 0f, 0f);
        AssertProgress(progress[1], 0f, 1f / 3f);
        AssertProgress(progress[2], 1f, 2f / 3f);
        AssertProgress(progress[3], 1f, 1f);
    }

    // ParserRetainsRecognizedFieldsAndIgnoresFutureSyntax prevents unknown targets, fourth-slot modes, extra commas, and non-string entries from rejecting otherwise valid instructions.
    [Test]
    public void ParserRetainsRecognizedFieldsAndIgnoresFutureSyntax()
    {
        MethodInfo evaluate = GetColorDistributionMethod("EvaluateForTesting");
        List<object> boxInstructions =
        [
            "rx,0.5,L,l,ignored-fifth-field",
            "q,100,L,l",
            123,
        ];
        List<object> eventInstructions =
        [
            "gy,0.25,L,unknown-mode,future",
            "b,0.5,L,ignored-mode,another,future",
            "r,not-a-number,L,l",
            "r,1,unknown-easing,l",
        ];

        Color actual = (Color)evaluate.Invoke(
            null,
            [new Color(0.1f, 0.2f, 0.3f, 0.4f), boxInstructions, eventInstructions, 0.4f, 0.8f])!;

        AssertColor(actual, new Color(0.5f, 0.3f, 0.5f, 0.4f));
    }

    // CanonicalDisplayAbbreviationsAreCaseInsensitiveAndKeepBeatSaberCurvesDistinct locks the ChroMapper-authored vocabulary without conflating native and Beat Saber InOut variants.
    [Test]
    public void CanonicalDisplayAbbreviationsAreCaseInsensitiveAndKeepBeatSaberCurvesDistinct()
    {
        MethodInfo evaluate = GetColorDistributionMethod("EvaluateForTesting");
        List<object> standard = ["r,1,iotbk,c", "g,1,IOTEL", "b,1,IotBo,c"];
        List<object> beatSaber = ["r,1,iobK,C", "g,1,ioeL", "b,1,IObO,c"];

        Color standardColor = (Color)evaluate.Invoke(null, [Color.black, standard, Array.Empty<object>(), 0.25f, 0.75f])!;
        Color beatSaberColor = (Color)evaluate.Invoke(null, [Color.black, beatSaber, Array.Empty<object>(), 0.25f, 0.75f])!;

        Assert.That(beatSaberColor.r, Is.Not.EqualTo(standardColor.r).Within(0.0001f));
        Assert.That(beatSaberColor.g, Is.Not.EqualTo(standardColor.g).Within(0.0001f));
        Assert.That(beatSaberColor.b, Is.Not.EqualTo(standardColor.b).Within(0.0001f));
    }

    // EveryCanonicalDisplayAbbreviationParsesCaseInsensitively protects the complete ChroMapper-authored token set rather than sampling only the new InOut families.
    [Test]
    public void EveryCanonicalDisplayAbbreviationParsesCaseInsensitively()
    {
        MethodInfo parse = GetColorDistributionMethod("Parse");
        string[] abbreviations =
        [
            "L", "I^2", "O^2", "IO^2", "I^3", "O^3", "IO^3", "I^4", "O^4", "IO^4",
            "I^5", "O^5", "IO^5", "ISn", "OSn", "IOSn", "IEx", "OEx", "IOEx", "ICr",
            "OCr", "IOCr", "IBk", "OBk", "IOTBk", "IEl", "OEl", "IOTEl", "IBo", "OBo",
            "IOTBo", "IOBk", "IOEl", "IOBo", "N",
        ];

        foreach (string abbreviation in abbreviations)
        {
            ICollection parsed = (ICollection)parse.Invoke(null, [new[] { $"r,1,{abbreviation.ToLowerInvariant()}" }])!;
            Assert.That(parsed.Count, Is.EqualTo(1), abbreviation);
        }
    }

    // ExplicitChunkModeMatchesOmittedModeWhileLightModeRemainsDistinct documents c as a supported fourth segment rather than merely relying on unknown-token fallback.
    [Test]
    public void ExplicitChunkModeMatchesOmittedModeWhileLightModeRemainsDistinct()
    {
        MethodInfo evaluate = GetColorDistributionMethod("EvaluateForTesting");

        Color omitted = (Color)evaluate.Invoke(null, [Color.black, new[] { "r,1,L" }, Array.Empty<object>(), 0.25f, 0.75f])!;
        Color explicitChunk = (Color)evaluate.Invoke(null, [Color.black, new[] { "r,1,L,c" }, Array.Empty<object>(), 0.25f, 0.75f])!;
        Color explicitLight = (Color)evaluate.Invoke(null, [Color.black, new[] { "r,1,L,l" }, Array.Empty<object>(), 0.25f, 0.75f])!;

        AssertColor(explicitChunk, omitted);
        Assert.That(explicitLight.r, Is.EqualTo(0.75f).Within(0.0001f));
        Assert.That(explicitLight.r, Is.Not.EqualTo(explicitChunk.r).Within(0.0001f));
    }

    // BoxThenEventColorDistributionsComposeWithIndependentBrightness verifies ordered scope composition and f alpha offsets without scaling HDR RGB.
    [Test]
    public void BoxThenEventColorDistributionsComposeWithIndependentBrightness()
    {
        MethodInfo evaluate = GetColorDistributionMethod("EvaluateForTesting");
        List<object> boxInstructions = ["r,0.4,L", "f,0.2,L"];
        List<object> eventInstructions = ["g,0.3,L,l", "f,0.1,L,l"];

        Color actual = (Color)evaluate.Invoke(
            null,
            [new Color(0.2f, 0.1f, 2f, 0.5f), boxInstructions, eventInstructions, 0.5f, 1f])!;

        AssertColor(actual, new Color(0.4f, 0.4f, 2f, 0.7f));
    }

    // HsvColorDistributionsWrapHueClampSaturationAndKeepHdrValue matches ChroMapper semantics for cyclic hue, bounded saturation, and unbounded positive value.
    [Test]
    public void HsvColorDistributionsWrapHueClampSaturationAndKeepHdrValue()
    {
        MethodInfo evaluate = GetColorDistributionMethod("EvaluateForTesting");
        Color source = new(0.969f, 0f, 0.941f, 0.75f);
        List<object> instructions = ["hs,0.4,L,l", "v,2,L,l"];

        Color actual = (Color)evaluate.Invoke(null, [source, Array.Empty<object>(), instructions, 0f, 1f])!;
        Color.RGBToHSV(source, out float hue, out float saturation, out float value);
        Color expected = Color.HSVToRGB(
            Mathf.Repeat(hue + 0.4f, 1f),
            Mathf.Clamp01(saturation + 0.4f),
            value + 2f,
            true);
        expected.a = source.a;

        AssertColor(actual, expected);
        Assert.That(actual.maxColorComponent, Is.GreaterThan(1f));
    }

    // RuntimePipelineCarriesBoxAndEventColorDistributionContext prevents the tested evaluator from remaining disconnected from GLS deserialization and emitted runtime events.
    [Test]
    public void RuntimePipelineCarriesBoxAndEventColorDistributionContext()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type? deserializePatch = pluginAssembly.GetType("ChromaGLS.HarmonyPatches.GlsDeserializePatch");
        Type? converterPatch = pluginAssembly.GetType("ChromaGLS.HarmonyPatches.GlsConverterPatches");

        Assert.That(deserializePatch, Is.Not.Null);
        Assert.That(
            deserializePatch!.GetMethod("TryGetBoxCustomData", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public),
            Is.Not.Null,
            "Box customData must survive into the V3/V4-compatible converter path.");
        Assert.That(converterPatch, Is.Not.Null);
        Assert.That(
            converterPatch!.GetMethod("TryGetColorDistributionContext", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public),
            Is.Not.Null,
            "Every emitted light event must retain parsed box/event color distributions and both spatial progress modes.");
    }

    // StrobeChannelIgnoresNormalDistributionsOnceStrobeDistributionsExist pins the corrected shared
    // contract through the real deserialize/convert/unpack pipeline plus the chromafier's event-time
    // helpers: an authored strobeColor isolates the strobe channel; otherwise ANY parsed
    // strobeColorDistributions (box or event) restart the strobe chain on the raw primary base so
    // normal colorDistributions never leak into it. Only when no strobe instructions exist at all
    // does the strobe channel inherit the fully normal-distributed endpoint.
    [TestCaseSource(nameof(StrobeInheritanceCases))]
    public void StrobeChannelIgnoresNormalDistributionsOnceStrobeDistributionsExist(
        NormalColorOverride normalOverride,
        StrobeColorOverride strobeOverride,
        DistributionChannels channels,
        DistributionScope scope)
    {
        Assembly plugin = Assembly.Load("ChromaGLS");
        Type converter = plugin.GetType("ChromaGLS.HarmonyPatches.GlsConverterPatches", true)!;
        string distributionJson = DistributionKeysJson(
            channels,
            $"\"h,{NormalHueShift.ToString(CultureInfo.InvariantCulture)},L,l\"",
            $"\"h,{StrobeHueShift.ToString(CultureInfo.InvariantCulture)},L,l\"");
        List<string?> eventCustomParts =
        [
            normalOverride == NormalColorOverride.Custom ? ColorKeyJson("color", CustomNormalColor) : null,
            strobeOverride == StrobeColorOverride.Explicit ? ColorKeyJson("strobeColor", ExplicitStrobeColor) : null,
            scope == DistributionScope.Event ? distributionJson : null,
        ];
        string? eventCustomJson = eventCustomParts.Any(part => part != null)
            ? string.Join(",", eventCustomParts.Where(part => part != null))
            : null;
        object[] events = UnpackColorDistributedEvents(
            plugin,
            converter,
            scope == DistributionScope.Box ? distributionJson : null,
            eventCustomJson);
        Type chromafier = plugin.GetType("ChromaGLS.HarmonyPatches.GlsColorChromafier", true)!;
        MethodInfo normal = chromafier.GetMethod("ApplyNormalColorDistributions", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo strobe = chromafier.GetMethod("ApplyStrobeColorDistributions", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo resolveColor = chromafier.GetMethod("ResolveCustomColor", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo lookup = converter.GetMethod("TryGetColorDistributionContext", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool hasNormalDistributions = channels is DistributionChannels.NormalOnly or DistributionChannels.NormalAndStrobe;
        bool hasStrobeDistributions = channels is DistributionChannels.StrobeOnly or DistributionChannels.NormalAndStrobe;
        for (int index = 0; index < events.Length; index++)
        {
            float light = index / 3f;
            object emittedEvent = events[index];
            object?[] lookupArgs = [emittedEvent, null];
            Assert.That(lookup.Invoke(null, lookupArgs), Is.True, $"Unpack must attach context for light {index}.");
            object context = lookupArgs[1]!;
            // ResolveCustomColor is the same event-time customData read the gameplay postfix performs.
            Color? customColor = (Color?)resolveColor.Invoke(null, [emittedEvent, "color"]);
            Color? customStrobeColor = (Color?)resolveColor.Invoke(null, [emittedEvent, "strobeColor"]);
            Color? actualNormal = (Color?)normal.Invoke(null, [OemColor, BoxNullable(customColor), context]);
            Color? actualStrobe = (Color?)strobe.Invoke(
                null,
                [OemColor, BoxNullable(customColor), BoxNullable(customStrobeColor), context]);

            float normalShift = hasNormalDistributions ? NormalHueShift * light : 0f;
            float strobeShift = hasStrobeDistributions ? StrobeHueShift * light : 0f;
            // WithAlpha(oem, 1) is the evaluator's OEM seed, so the distributed OEM endpoint reports alpha 1;
            // an untouched endpoint keeps the native alpha through the caller's fallback instead.
            Color normalBase = normalOverride == NormalColorOverride.Custom
                ? CustomNormalColor
                : hasNormalDistributions ? WithAlpha(OemColor, 1f) : OemColor;
            Color expectedNormal = OffsetHsv(normalBase, normalShift);
            Color strobeBase = strobeOverride == StrobeColorOverride.Explicit
                ? ExplicitStrobeColor
                : normalOverride == NormalColorOverride.Custom ? CustomNormalColor : WithAlpha(OemColor, 1f);
            // Corrected contract: explicit strobeColor or any parsed strobe instruction owns the
            // strobe chain on the raw base alone; only a strobe-instruction-free event inherits the
            // distributed normal endpoint.
            Color expectedStrobe = strobeOverride == StrobeColorOverride.Explicit || hasStrobeDistributions
                ? OffsetHsv(strobeBase, strobeShift)
                : expectedNormal;

            AssertColor(
                actualNormal ?? customColor ?? OemColor,
                expectedNormal);
            AssertColor(
                actualStrobe ?? customColor ?? OemColor,
                expectedStrobe);
        }
    }

    // CancelledBoxAndEventSaturationOffsetsClampOnceOnBothChannels proves each channel's OWN
    // box->event saturation chain stays unclamped until materialization: normal and strobe offsets
    // authored at both scopes each cancel back to the authored 0.5, and because strobe instructions
    // exist the strobe channel evaluates them independently on the raw base rather than extending
    // the normal chain. Either sign order must avoid a mid-chain Clamp01 losing the gray saturation
    // (and, once clamped to 0, the hue) before the remaining instructions run.
    [TestCase("s,1,L,l", "s,-1,L,l", false)]
    [TestCase("s,-1,L,l", "s,1,L,l", false)]
    [TestCase("s,1,L,l", "s,-1,L,l", true)]
    [TestCase("s,-1,L,l", "s,1,L,l", true)]
    public void CancelledBoxAndEventSaturationOffsetsClampOnceOnBothChannels(
        string boxInstruction,
        string eventInstruction,
        bool explicitStrobe)
    {
        Assembly plugin = Assembly.Load("ChromaGLS");
        Type converter = plugin.GetType("ChromaGLS.HarmonyPatches.GlsConverterPatches", true)!;
        Color saturatedBase = new(1f, 0.75f, 0.5f, 0.8f);
        Color explicitStrobeColor = new(1f, 0.75f, 0.5f, 0.6f);
        string boxJson = DistributionKeysJson(
            DistributionChannels.NormalAndStrobe,
            InstructionJson(boxInstruction),
            InstructionJson(boxInstruction));
        List<string?> eventParts =
        [
            ColorKeyJson("color", saturatedBase),
            explicitStrobe ? ColorKeyJson("strobeColor", explicitStrobeColor) : null,
            DistributionKeysJson(
                DistributionChannels.NormalAndStrobe,
                InstructionJson(eventInstruction),
                InstructionJson(eventInstruction)),
        ];
        string eventJson = string.Join(",", eventParts.Where(part => part != null));
        object[] events = UnpackColorDistributedEvents(plugin, converter, boxJson, eventJson);
        Type chromafier = plugin.GetType("ChromaGLS.HarmonyPatches.GlsColorChromafier", true)!;
        MethodInfo normal = chromafier.GetMethod("ApplyNormalColorDistributions", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo strobe = chromafier.GetMethod("ApplyStrobeColorDistributions", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo resolveColor = chromafier.GetMethod("ResolveCustomColor", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo lookup = converter.GetMethod("TryGetColorDistributionContext", BindingFlags.Static | BindingFlags.NonPublic)!;

        // The last filter element sits at light progress 1, so each instruction applies its full offset.
        object eventData = events[events.Length - 1];
        object?[] lookupArgs = [eventData, null];
        Assert.That(lookup.Invoke(null, lookupArgs), Is.True);
        object context = lookupArgs[1]!;
        Color? customColor = (Color?)resolveColor.Invoke(null, [eventData, "color"]);
        Color? customStrobeColor = (Color?)resolveColor.Invoke(null, [eventData, "strobeColor"]);

        AssertColor((Color?)normal.Invoke(null, [OemColor, BoxNullable(customColor), context]) ?? OemColor, saturatedBase);
        AssertColor(
            (Color?)strobe.Invoke(null, [OemColor, BoxNullable(customColor), BoxNullable(customStrobeColor), context]) ?? OemColor,
            explicitStrobe ? explicitStrobeColor : saturatedBase);
    }

    // CrossScopeSaturationOffsetsClampIndependentlyWhenStrobeDistributionsExist exercises the
    // corrected channel split: a parsed strobeColorDistributions anywhere (box or event) makes the
    // strobe channel independent of the normal chain, so box normal + event strobe and the inverse
    // scope split each clamp only their own offset. The earlier shared-chain expectation that the
    // strobe nets back to the authored .5 was the invalid cross-channel cancellation contract.
    [TestCase(1f, -1f, false)]
    [TestCase(-1f, 1f, false)]
    [TestCase(-1f, 1f, true)]
    [TestCase(1f, -1f, true)]
    public void CrossScopeSaturationOffsetsClampIndependentlyWhenStrobeDistributionsExist(
        float normalOffset,
        float strobeOffset,
        bool normalOnEventScope)
    {
        Assembly plugin = Assembly.Load("ChromaGLS");
        Type converter = plugin.GetType("ChromaGLS.HarmonyPatches.GlsConverterPatches", true)!;
        Color saturatedBase = new(1f, 0.75f, 0.5f, 0.8f);
        string normalInstruction = FormattableString.Invariant($"\"s,{normalOffset},L,l\"");
        string strobeInstruction = FormattableString.Invariant($"\"s,{strobeOffset},L,l\"");
        string boxJson = normalOnEventScope
            ? $"\"strobeColorDistributions\":[{strobeInstruction}]"
            : $"\"colorDistributions\":[{normalInstruction}]";
        string eventJson = string.Join(
            ",",
            ColorKeyJson("color", saturatedBase),
            normalOnEventScope
                ? $"\"colorDistributions\":[{normalInstruction}]"
                : $"\"strobeColorDistributions\":[{strobeInstruction}]");
        object[] events = UnpackColorDistributedEvents(plugin, converter, boxJson, eventJson);
        Type chromafier = plugin.GetType("ChromaGLS.HarmonyPatches.GlsColorChromafier", true)!;
        MethodInfo normal = chromafier.GetMethod("ApplyNormalColorDistributions", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo strobe = chromafier.GetMethod("ApplyStrobeColorDistributions", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo resolveColor = chromafier.GetMethod("ResolveCustomColor", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo lookup = converter.GetMethod("TryGetColorDistributionContext", BindingFlags.Static | BindingFlags.NonPublic)!;
        object eventData = events[events.Length - 1];
        object?[] lookupArgs = [eventData, null];
        Assert.That(lookup.Invoke(null, lookupArgs), Is.True);
        object context = lookupArgs[1]!;
        Color? customColor = (Color?)resolveColor.Invoke(null, [eventData, "color"]);

        // Independent saturation oracle: each channel clamps only its own offset against the same
        // raw base; the strobe chain no longer composes over the normal result.
        Color.RGBToHSV(saturatedBase, out float hue, out float saturation, out float value);
        Color expectedNormal = Color.HSVToRGB(hue, Mathf.Clamp01(saturation + normalOffset), value, true);
        expectedNormal.a = saturatedBase.a;
        Color expectedStrobe = Color.HSVToRGB(hue, Mathf.Clamp01(saturation + strobeOffset), value, true);
        expectedStrobe.a = saturatedBase.a;
        AssertColor(
            (Color?)normal.Invoke(null, [OemColor, BoxNullable(customColor), context]) ?? OemColor,
            expectedNormal);
        AssertColor(
            (Color?)strobe.Invoke(null, [OemColor, BoxNullable(customColor), null, context]) ?? OemColor,
            expectedStrobe);
    }

    public enum NormalColorOverride
    {
        Oem,
        Custom,
    }

    public enum StrobeColorOverride
    {
        Inherited,
        Explicit,
    }

    public enum DistributionChannels
    {
        NormalOnly,
        StrobeOnly,
        NormalAndStrobe,
    }

    public enum DistributionScope
    {
        Box,
        Event,
    }

    private static IEnumerable<TestCaseData> StrobeInheritanceCases()
    {
        foreach (NormalColorOverride normal in Enum.GetValues(typeof(NormalColorOverride)))
        foreach (StrobeColorOverride strobe in Enum.GetValues(typeof(StrobeColorOverride)))
        foreach (DistributionChannels channels in Enum.GetValues(typeof(DistributionChannels)))
        foreach (DistributionScope scope in Enum.GetValues(typeof(DistributionScope)))
        {
            yield return new TestCaseData(normal, strobe, channels, scope)
                .SetName($"StrobeInheritance_{normal}_{strobe}_{channels}_{scope}");
        }
    }

    private const float NormalHueShift = 0.25f;

    private const float StrobeHueShift = 0.5f;

    private static readonly Color OemColor = new(0.2f, 0.85f, 0.45f, 0.7f);

    private static readonly Color CustomNormalColor = new(0.9f, 0.5f, 0.2f, 0.8f);

    private static readonly Color ExplicitStrobeColor = new(0.1f, 0.4f, 0.9f, 0.6f);

    private static string InstructionJson(string instruction) => $"\"{instruction}\"";

    private static string DistributionKeysJson(DistributionChannels channels, string normalInstructions, string strobeInstructions)
    {
        List<string?> parts =
        [
            channels is DistributionChannels.NormalOnly or DistributionChannels.NormalAndStrobe
                ? $"\"colorDistributions\":[{normalInstructions}]"
                : null,
            channels is DistributionChannels.StrobeOnly or DistributionChannels.NormalAndStrobe
                ? $"\"strobeColorDistributions\":[{strobeInstructions}]"
                : null,
        ];
        return string.Join(",", parts.Where(part => part != null));
    }

    private static string ColorKeyJson(string key, Color color) => FormattableString.Invariant(
        $"\"{key}\":[{color.r},{color.g},{color.b},{color.a}]");

    private static object? BoxNullable(Color? color) => color.HasValue ? color.Value : null;

    private static Color WithAlpha(Color color, float alpha) => new(color.r, color.g, color.b, alpha);

    // The HSV oracle shares only Unity's primitive conversion with the evaluator under test; offset
    // composition stays unclamped here so a regression in saturation ordering cannot agree with itself.
    private static Color OffsetHsv(Color color, float hueShift)
    {
        Color.RGBToHSV(color, out float hue, out float saturation, out float value);
        Color result = Color.HSVToRGB(Mathf.Repeat(hue + hueShift, 1f), saturation, value, true);
        result.a = color.a;
        return result;
    }

    // UnpackedColorDistributionContextSurvivesGameplayCopies crosses the actual CJD/native GetCopy boundary used by BeatmapDataTransformHelper, including boxes with no event customData.
    [TestCase(false)]
    [TestCase(true)]
    public void UnpackedColorDistributionContextSurvivesGameplayCopies(bool boxOnly)
    {
        Assembly plugin = Assembly.Load("ChromaGLS");
        Type converter = plugin.GetType("ChromaGLS.HarmonyPatches.GlsConverterPatches", true)!;
        object[] events = UnpackColorDistributedEvents(plugin, converter, boxOnly);
        MethodInfo lookup = converter.GetMethod("TryGetColorDistributionContext", BindingFlags.Static | BindingFlags.NonPublic)!;
        Type chromafier = plugin.GetType("ChromaGLS.HarmonyPatches.GlsColorChromafier", true)!;
        MethodInfo normal = chromafier.GetMethod("ApplyNormalColorDistributions", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo strobe = chromafier.GetMethod("ApplyStrobeColorDistributions", BindingFlags.Static | BindingFlags.NonPublic)!;
        int[] lightIds = [0, 1, 4, 5];
        for (int index = 0; index < events.Length; index++)
        {
            object original = events[index];
            Type cjdBeatmap = typeof(CustomData).Assembly.GetType("CustomJSONData.CustomBeatmap.CustomBeatmapData", true)!;
            Type nativeEvent = typeof(CustomData).Assembly.GetType("CustomJSONData.CustomBeatmap.CustomLightColorBeatmapEventData", true)!.BaseType!;
            Assert.That(cjdBeatmap.GetMethod("GetCustomType")!.Invoke(null, [original]), Is.EqualTo(nativeEvent), "CJD must register color-distributed events on the native color-event callback channel.");
            object?[] lookupArgs = [original, null];
            Assert.That(lookup.Invoke(null, lookupArgs), Is.True, "Unpack must first attach context to the original event.");
            object originalContext = lookupArgs[1]!;
            object copied = original;
            for (int generation = 0; generation < 2; generation++)
            {
                copied = copied.GetType().GetMethod("GetCopy")!.Invoke(copied, null)!;
                Assert.That(copied, Is.Not.SameAs(original));
                Assert.That(
                    nativeEvent.GetProperty("elementId")?.GetValue(copied)
                        ?? nativeEvent.GetField("elementId")!.GetValue(copied),
                    Is.EqualTo(lightIds[index]));
                lookupArgs = [copied, null];
                Assert.That(
                    lookup.Invoke(null, lookupArgs),
                    Is.True,
                    $"Gameplay GetCopy lost color distributions for light {lightIds[index]} at copy {generation + 1}; raw JSON surviving is insufficient.");
                object context = lookupArgs[1]!;
                Assert.That(context, Is.SameAs(originalContext), "Immutable parsed instructions and progress should be shared, not reparsed.");
                float chunk = index < 2 ? 0f : 1f;
                float light = index / 3f;
                Color expectedNormal = new(0.1f + (0.6f * light), 0.2f + (0.4f * chunk), 0.3f, 1f);
                Color expectedStrobe = new(0.2f, 0.1f, 0.4f + (0.3f * light), 1f - (0.5f * chunk));
                AssertColor(
                    (Color)normal.Invoke(null, [Color.white, new Color(0.1f, 0.2f, 0.3f, 1f), context])!,
                    expectedNormal);
                AssertColor(
                    (Color)strobe.Invoke(null, [Color.white, Color.green, new Color(0.2f, 0.1f, 0.4f, 1f), context])!,
                    expectedStrobe);
            }
        }
    }

    [Test]
    public void EventInstructionsAreCompiledBeforeUnpackAndSharedAcrossLights()
    {
        Assembly plugin = Assembly.Load("ChromaGLS");
        Type converter = plugin.GetType("ChromaGLS.HarmonyPatches.GlsConverterPatches", true)!;
        object[] events = UnpackColorDistributedEvents(
            plugin,
            converter,
            null,
            "\"colorDistributions\":[\"r,0.6,L,l\"]",
            saveBox =>
            {
                object saveNode = ((IEnumerable)saveBox.GetType().GetProperty("lightColorBaseDataList")!.GetValue(saveBox)!).Cast<object>().Single();
                ((ICustomData)saveNode).customData["colorDistributions"] = new List<object> { "r,100,L,l" };
            });
        MethodInfo lookup = converter.GetMethod("TryGetColorDistributionContext", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? sharedInstructions = null;
        for (int index = 0; index < events.Length; index++)
        {
            object?[] args = [events[index], null];
            Assert.That(lookup.Invoke(null, args), Is.True);
            object context = args[1]!;
            Color color = (Color)context.GetType().GetMethod("ApplyNormal")!.Invoke(context, [Color.black])!;
            Assert.That(color.r, Is.EqualTo(0.6f * index / 3f).Within(0.0001f), "Unpack must use instructions compiled during conversion, even if the raw strings later change.");
            object instructions = context.GetType().GetProperty("EventNormalColorDistributions")!.GetValue(context)!;
            if (index == 0)
                sharedInstructions = instructions;
            else
                Assert.That(instructions, Is.SameAs(sharedInstructions), "All lights selected by one node must share its compiled instructions.");
        }
    }

    private static object[] UnpackColorDistributedEvents(Assembly plugin, Type converter, bool boxOnly)
    {
        const string colorDistributionData = "\"colorDistributions\":[\"r,0.6,L,l\",\"g,0.4,L\"],\"strobeColorDistributions\":[\"b,0.3,L,l\",\"f,-0.5,L\"]";
        return UnpackColorDistributedEvents(
            plugin,
            converter,
            boxOnly ? colorDistributionData : null,
            boxOnly ? null : colorDistributionData);
    }

    // StrobeChannelIgnoresNormalDistributionsOnceStrobeDistributionsExist needs the same
    // real deserialize/convert/unpack path driven by per-case JSON rather than one fixed fixture body.
    private static object[] UnpackColorDistributedEvents(
        Assembly plugin,
        Type converter,
        string? boxCustomData,
        string? eventCustomData,
        Action<object>? afterConversion = null)
    {
        string json = "[{\"b\":0,\"g\":1,\"e\":[{\"f\":{\"f\":2,\"p\":0,\"t\":2,\"c\":4},\"i\":0,\"d\":1,\"t\":1," +
            (boxCustomData != null ? "\"customData\":{" + boxCustomData + "}," : string.Empty) +
            "\"e\":[{\"b\":0,\"i\":1,\"c\":0,\"s\":0.5,\"f\":1,\"sb\":0.25" +
            (eventCustomData != null ? ",\"customData\":{" + eventCustomData + "}" : string.Empty) + "}]}]}]";
        Type deserializer = plugin.GetType("ChromaGLS.HarmonyPatches.GlsDeserializePatch", true)!;
        MethodInfo deserialize = deserializer.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!;
        IList groups = (IList)Activator.CreateInstance(deserialize.GetParameters()[1].ParameterType)!;
        using JsonTextReader reader = new(new StringReader(json));
        deserialize.Invoke(null, [reader, groups]);
        Assert.That(groups.Count, Is.EqualTo(1));
        object group = groups[0]!;
        object saveBox = ((IEnumerable)group.GetType().GetProperty("eventBoxes")!.GetValue(group)!).Cast<object>().Single();
        MethodInfo unpack = converter.GetMethod("LightColorBeatmapEventDataBoxUnpackPostfix", BindingFlags.Static | BindingFlags.NonPublic)!;
        Type boxType = unpack.GetParameters()[0].ParameterType;
        Type filterType = boxType.BaseType!.GetProperty("indexFilter")!.PropertyType;
        ConstructorInfo filterConstructor = filterType.GetConstructors().Single(ctor => ctor.GetParameters().Length == 9);
        object[] filterArgs = [0, 2, 2, 8, 0, 0, 2, 0f, 0];
        for (int index = 0; index < filterArgs.Length; index++)
        {
            Type type = filterConstructor.GetParameters()[index].ParameterType;
            if (type.IsEnum)
            {
                filterArgs[index] = Enum.ToObject(type, filterArgs[index]);
            }
        }
        object filter = filterConstructor.Invoke(filterArgs);
        object runtimeBox = TestHostCompat.GetUninitializedObject(boxType);
        FieldInfo baseDataField = boxType.GetField("_lightColorBaseDataList", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        Type baseDataType = baseDataField.FieldType.GenericTypeArguments[0];
        Array baseData = Array.CreateInstance(baseDataType, 1);
        baseData.SetValue(TestHostCompat.GetUninitializedObject(baseDataType), 0);
        baseDataField.SetValue(runtimeBox, baseData);
        boxType.BaseType.GetField("<indexFilter>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(runtimeBox, filter);
        converter.GetMethod("LightColorEventBoxConverterPostfix", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [saveBox, runtimeBox]);
        afterConversion?.Invoke(saveBox);
        Type eventType = typeof(CustomData).Assembly.GetType("CustomJSONData.CustomBeatmap.CustomLightColorBeatmapEventData", true)!.BaseType!;
        Type eventBase = eventType.BaseType!;
        Type outputType = typeof(List<>).MakeGenericType(eventBase);
        int[] ids = [0, 1, 4, 5];
        object[] events = new object[ids.Length];
        for (int index = 0; index < ids.Length; index++)
        {
            ConstructorInfo ctor = eventType.GetConstructors().Single();
            object?[] args = ctor.GetParameters().Select(parameter => parameter.Name switch
            {
                "groupId" => (object)1,
                "elementId" => ids[index],
                "brightness" => 0.5f,
                "strobeBrightness" => 0.25f,
                _ => parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null,
            }).ToArray();
            IList output = (IList)Activator.CreateInstance(outputType)!;
            output.Add(ctor.Invoke(args));
            object?[] unpackArgs = [runtimeBox, 0f, ids[index], index < 2 ? 0 : 1, float.MaxValue, output];
            unpack.Invoke(null, unpackArgs);
            events[index] = ((IEnumerable)unpackArgs[5]!).Cast<object>().Single();
        }
        return events;
    }

    // Reflection keeps the initial regression compilable against the unfixed plugin while still exercising the built production implementation afterward.
    private static MethodInfo GetColorDistributionMethod(string name)
    {
        Type? type = Assembly.Load("ChromaGLS").GetType("ChromaGLS.HarmonyPatches.GlsColorDistribution");
        Assert.That(type, Is.Not.Null, "The GLS color-distribution evaluator is not implemented.");
        MethodInfo? method = type!.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.That(method, Is.Not.Null, $"The GLS color-distribution contract {name} is not implemented.");
        return method!;
    }

    private static void AssertProgress(Vector2 actual, float chunk, float light)
    {
        Assert.That(actual.x, Is.EqualTo(chunk).Within(0.0001f));
        Assert.That(actual.y, Is.EqualTo(light).Within(0.0001f));
    }

    private static void AssertColor(Color actual, Color expected)
    {
        Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0001f));
        Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0001f));
        Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0001f));
        Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.0001f));
    }
}
