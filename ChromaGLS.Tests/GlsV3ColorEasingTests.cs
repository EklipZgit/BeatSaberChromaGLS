using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using CustomJSONData.CustomBeatmap;
using HarmonyLib;
using Newtonsoft.Json;
using NUnit.Framework;

namespace ChromaGLS.Tests;

// Exercise real V3 parsing/conversion/unpacking and copy boundaries without requiring legacy MonoMod detours on net8.
[TestFixture]
[NonParallelizable]
internal sealed class GlsV3ColorEasingTests
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private Assembly plugin = null!;
    private Type chromafier = null!;

    internal static readonly object[] KnownEasings =
    [
        new object[] { 1, "InQuad" }, new object[] { 2, "OutQuad" }, new object[] { 3, "InOutQuad" },
        new object[] { 4, "InSine" }, new object[] { 5, "OutSine" }, new object[] { 6, "InOutSine" },
        new object[] { 7, "InCubic" }, new object[] { 8, "OutCubic" }, new object[] { 9, "InOutCubic" },
        new object[] { 10, "InQuart" }, new object[] { 11, "OutQuart" }, new object[] { 12, "InOutQuart" },
        new object[] { 13, "InQuint" }, new object[] { 14, "OutQuint" }, new object[] { 15, "InOutQuint" },
        new object[] { 16, "InExpo" }, new object[] { 17, "OutExpo" }, new object[] { 18, "InOutExpo" },
        new object[] { 19, "InCirc" }, new object[] { 20, "OutCirc" }, new object[] { 21, "InOutCirc" },
        new object[] { 22, "InBack" }, new object[] { 23, "OutBack" }, new object[] { 24, "InOutBack" },
        new object[] { 25, "InElastic" }, new object[] { 26, "OutElastic" }, new object[] { 27, "InOutElastic" },
        new object[] { 28, "InBounce" }, new object[] { 29, "OutBounce" }, new object[] { 30, "InOutBounce" },
        new object[] { 100, "BeatSaberInOutBack" }, new object[] { 101, "BeatSaberInOutElastic" }, new object[] { 102, "BeatSaberInOutBounce" },
    ];

    [SetUp]
    public void SetUp()
    {
        plugin = Assembly.Load("ChromaGLS");
        foreach (AssemblyName reference in plugin.GetReferencedAssemblies().Where(reference => reference.Name is "Main" or "BeatmapCore" or "DataModels" or "Tweening"))
        {
            Assembly.Load(reference);
        }
        chromafier = plugin.GetType("ChromaGLS.HarmonyPatches.GlsColorChromafier", true)!;
    }

    // Controls expose native capabilities rather than assuming that 1.29.1 has the modern enum's BS-specific variants.
    [Test]
    public void ReportNativeCurveNames()
    {
        Type tween = FindType("Tweening.FloatTween")!;
        Type ease = tween.GetConstructors().Single(ctor => ctor.GetParameters().Length == 6).GetParameters()[4].ParameterType;
        TestContext.WriteLine("Native easing enum: " + ease.AssemblyQualifiedName);
        TestContext.WriteLine(string.Join(", ", Enum.GetNames(ease)));
        // Retain native sample diagnostics to document the exact three curves absent from 1.29.1's runtime.
        MethodInfo interpolate = FindType("Interpolation")!.GetMethod("Interpolate", All)!;
        foreach (string name in Enum.GetNames(ease).Where(name => name.StartsWith("BeatSaber", StringComparison.Ordinal)))
        {
            TestContext.WriteLine(name + ": " + string.Join(", ", new[] { 0f, 0.25f, 0.75f, 1f }.Select(t => ((float)interpolate.Invoke(null, [t, Enum.Parse(ease, name)])!).ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
        }
        Assert.That(Enum.GetNames(ease), Does.Contain("InSine"));
    }

    // OrdinaryV3NodesAvoidCustomDataAllocation proves that native-only maps retain native save-data nodes while an authored custom payload still receives ChromaGLS's carrier.
    [Test]
    public void OrdinaryV3NodesAvoidCustomDataAllocation()
    {
        const int ordinaryNodeCount = 512;
        StringBuilder json = new("[{\"b\":0,\"g\":1,\"e\":[{\"f\":{\"f\":2,\"p\":0,\"t\":1},\"e\":[");
        for (int index = 0; index < ordinaryNodeCount; index++)
        {
            if (index > 0)
            {
                json.Append(',');
            }

            json.Append("{\"b\":").Append(index).Append(",\"i\":1,\"c\":0,\"s\":1}");
        }

        json.Append(",{\"b\":513,\"i\":1,\"c\":1,\"s\":1,\"customData\":{\"color\":[1,0,0,1]}}]}]}]");
        Type deserializer = plugin.GetType("ChromaGLS.HarmonyPatches.GlsDeserializePatch", true)!;
        MethodInfo deserialize = deserializer.GetMethod("Prefix", All)!;
        IList groups = (IList)Activator.CreateInstance(deserialize.GetParameters()[1].ParameterType)!;
        using JsonTextReader reader = new(new StringReader(json.ToString()));
        deserialize.Invoke(null, [reader, groups]);

        object saveBox = ((IEnumerable)groups[0]!.GetType().GetProperty("eventBoxes")!.GetValue(groups[0])!).Cast<object>().Single();
        object[] nodes = ((IEnumerable)saveBox.GetType().GetProperty("lightColorBaseDataList")!.GetValue(saveBox)!).Cast<object>().ToArray();
        Assert.That(nodes.Length, Is.EqualTo(ordinaryNodeCount + 1));
        Assert.That(nodes.Take(ordinaryNodeCount).Any(node => node is ICustomData), Is.False,
            "Native-only GLS nodes must not allocate CustomData carriers.");
        Assert.That(nodes[ordinaryNodeCount], Is.InstanceOf<ICustomData>(),
            "An authored customData payload must retain its carrier through conversion.");
    }

    // V3 i is transition mode, so only customData.easing can select the new curve. GetCopy must not erase it.
    [TestCase(1)]
    [TestCase(2)]
    public void V3CustomEaseSurvivesUnpackCopiesAndUsesUpcomingTrack(int transition)
    {
        object[] events = UnpackEvents(transition, "4", elementId: 0);
        object[] otherTrack = UnpackEvents(1, "8", elementId: 1);
        object effect = TestHostCompat.GetUninitializedObject(FindType("LightColorGroupEffect")!);
        try
        {
            Assert.That(events[1], Is.InstanceOf<ICustomData>());
            Assert.That(((ICustomData)events[1]).customData.Get<object>("easing"), Is.EqualTo(4L), "Extend must retain its own requested curve, not inherit the predecessor's easing.");
            AssertTransition(events[1], transition);
            if (transition == 2)
            {
                // Extend resolves its inherited payload at insert time through the same propagation the processor runs.
                Propagate(events[1], events[0]);
            }

            // Extending a value never adopts the raw replacement color; only the interval's easing belongs to the new node.
            IList effectiveColor = (IList)((ICustomData)events[1]).customData.Get<object>("color")!;
            Assert.That(System.Convert.ToSingle(effectiveColor[0]), Is.EqualTo(transition == 2 ? 0f : 1f));
            for (int generation = 0; generation < 3; generation++)
            {
                events = events.Select(evt => evt.GetType().GetMethod("GetCopy")!.Invoke(evt, null)!).ToArray();
                AssertTransition(events[1], transition);
                Link(events[0], events[1]);
                Stage(effect, events[0]);
                Assert.That(chromafier.GetMethod("HasColorTween", All)!.Invoke(null, [events[1]]), Is.True, "Custom Extend must run a tween without rewriting its Extend identity.");
                Assert.That(Progress(effect, 0.25f), Is.EqualTo(0.0761204675d).Within(0.000002d), "The upcoming copied V3 node's InSine curve must replace linear interpolation.");
            }
            Link(otherTrack[0], otherTrack[1]);
            Stage(effect, otherTrack[0]);
            Assert.That(Progress(effect, 0.25f), Is.EqualTo(0.578125d).Within(0.000002d), "The active element's upcoming event must win over another element at the same time.");
        }
        finally
        {
            chromafier.GetMethod("CleanupPostfix", All)!.Invoke(null, [effect]);
        }
    }

    // Legacy native events expose Extend and Interpolate as mutually exclusive flags; only the render predicate may reinterpret an eased Extend.
    [Test]
    public void LegacyTweenPredicatePreservesExtendIdentity()
    {
        Type eventType = FindType("LightColorBeatmapEventData")!;
        FieldInfo? field = eventType.GetField("transitionType", All);
        MethodInfo? getter = eventType.GetProperty("transitionType", All)?.GetGetMethod(true);
        if (field == null && getter == null)
        {
            Assert.Ignore("Modern events store usePrevious and easing separately; the legacy predicate patch is unnecessary.");
        }
        MethodInfo? transpiler = chromafier.GetMethod("CustomExtensionTweenTranspiler", All);
        Assert.That(transpiler, Is.Not.Null, "The legacy handler must recognize an eased Extend without rewriting the event's transitionType.");
        CodeInstruction read = field != null ? new(OpCodes.Ldfld, field) : new(OpCodes.Callvirt, getter);
        IEnumerable<CodeInstruction> result = (IEnumerable<CodeInstruction>)transpiler!.Invoke(null, [new[] { read }])!;
        CodeInstruction replaced = result.Single();
        Assert.That(replaced.opcode, Is.EqualTo(OpCodes.Call));
        MethodInfo replacement = (MethodInfo)replaced.operand;
        Assert.That(replacement.Name, Is.EqualTo("GetTweenTransitionType"));
        object valid = UnpackEvents(2, "4", 0)[1];
        Assert.That(System.Convert.ToInt32(replacement.Invoke(null, [valid])), Is.EqualTo(1), "An explicitly eased Extend must enter the native tween branch.");
        AssertTransition(valid, 2);
        object invalid = UnpackEvents(2, "\"4\"", 0)[1];
        Assert.That(System.Convert.ToInt32(replacement.Invoke(null, [invalid])), Is.EqualTo(2), "Invalid metadata must leave native Extend behavior intact.");
        AssertTransition(invalid, 2);
    }

    // JSON readers can box integral numbers as floating point; those are numeric IDs, unlike strings or fractions.
    [TestCase("4.0")]
    [TestCase("4e0")]
    public void V3IntegralNumericEasingIsAccepted(string raw)
    {
        object[] events = UnpackEvents(1, raw, 0);
        object effect = TestHostCompat.GetUninitializedObject(FindType("LightColorGroupEffect")!);
        try
        {
            Link(events[0], events[1]);
            Stage(effect, events[0]);
            Assert.That(Progress(effect, 0.25f), Is.EqualTo(0.0761204675d).Within(0.000002d));
        }
        finally
        {
            chromafier.GetMethod("CleanupPostfix", All)!.Invoke(null, [effect]);
        }
    }

    // Numeric save IDs are not runtime IDs; all recognized families must be resolved by name, not enum casts.
    [TestCaseSource(nameof(KnownEasings))]
    public void V3CustomEaseUsesExactNativeCurve(int id, string runtimeName)
    {
        object[] events = UnpackEvents(1, id.ToString(System.Globalization.CultureInfo.InvariantCulture), 0);
        object effect = TestHostCompat.GetUninitializedObject(FindType("LightColorGroupEffect")!);
        try
        {
            Link(events[0], events[1]);
            Stage(effect, events[0]);
            Type interpolation = FindType("Interpolation")!;
            MethodInfo interpolate = interpolation.GetMethod("Interpolate", All)!;
            Type easeType = interpolate.GetParameters()[1].ParameterType;
            bool available = Enum.IsDefined(easeType, runtimeName);
            // ReportNativeCurveNames captured these exact 1.44.1 native samples, including intentional endpoint overshoot; 1.29.1 needs identical backports.
            float[]? backportSamples = available ? null : id switch
            {
                100 => [0f, 0.07834375f, 1.0999923f, 1.0000017f],
                101 => [0f, 0.5787031f, 1f, 1.0033829f],
                102 => [0f, 0.3249375f, 0.97265625f, 1f],
                _ => null,
            };
            Assert.That(available || (easeType.Assembly.GetName().Name == "HMLib" && backportSamples != null), Is.True, "Only the three documented 1.29.1 native omissions may use exact backports.");
            object? nativeEase = available ? Enum.Parse(easeType, runtimeName) : null;
            float[] times = [0f, 0.25f, 0.75f, 1f];
            for (int index = 0; index < times.Length; index++)
            {
                float t = times[index];
                float expected = available ? (float)interpolate.Invoke(null, [t, nativeEase])! : backportSamples![index];
                Assert.That(Progress(effect, t), Is.EqualTo(expected).Within(0.000002f), $"V3 save easing {id} must dispatch exactly {runtimeName} once at t={t}.");
            }
        }
        finally
        {
            chromafier.GetMethod("CleanupPostfix", All)!.Invoke(null, [effect]);
        }
    }

    // colorEasing, strobeColorEasing, and strobeEasing are three independent GLS tracks: the first two follow
    // the per-light interval endpoint (normal RGB and strobe RGB respectively), and the last the active node itself.
    [TestCase(1)]
    [TestCase(2)]
    public void V3TrackEasingsStageIndependentlyFromSharedInterval(int transition)
    {
        object[] events = UnpackTrackEvents(
            transition,
            "\"strobeEasing\":16",
            "\"easing\":6,\"colorEasing\":4,\"strobeColorEasing\":5,\"strobeEasing\":17",
            elementId: 0);
        object effect = TestHostCompat.GetUninitializedObject(FindType("LightColorGroupEffect")!);
        try
        {
            Link(events[0], events[1]);
            Stage(effect, events[0]);
            Type interpolation = FindType("Interpolation")!;
            MethodInfo interpolate = interpolation.GetMethod("Interpolate", All)!;
            Type easeType = interpolate.GetParameters()[1].ParameterType;
            double MasterAt(float t, string name) =>
                (float)interpolate.Invoke(null, [t, Enum.Parse(easeType, name)])!;

            Assert.That(Progress(effect, 0.25f), Is.EqualTo(MasterAt(0.25f, "InOutSine")).Within(0.000002d),
                "The interval's own easing must stay on its authored curve.");
            Assert.That(NormalColorProgress(effect, 0.25f), Is.EqualTo(MasterAt(0.25f, "InSine")).Within(0.000002d),
                "customData.colorEasing must override only the normal color track.");
            Assert.That(StrobeColorProgress(effect, 0.25f), Is.EqualTo(MasterAt(0.25f, "OutSine")).Within(0.000002d),
                "customData.strobeColorEasing must override only the strobe color track.");
            Func<float, float> fadeEase = StrobeFadeEasing(effect);
            Assert.That(fadeEase(0.5f), Is.EqualTo(MasterAt(0.5f, "InExpo")).Within(0.000002d),
                "customData.strobeEasing must come from the active node, not the upcoming one.");
        }
        finally
        {
            chromafier.GetMethod("CleanupPostfix", All)!.Invoke(null, [effect]);
        }
    }

    [TestCase("colorEasing", 1, false)]
    [TestCase("colorEasing", 1, true)]
    [TestCase("colorEasing", 2, false)]
    [TestCase("colorEasing", 2, true)]
    [TestCase("strobeColorEasing", 1, false)]
    [TestCase("strobeColorEasing", 1, true)]
    [TestCase("strobeColorEasing", 2, false)]
    [TestCase("strobeColorEasing", 2, true)]
    public void V3ExplicitLinearTracksSurviveConversionAndCopies(string key, int transition, bool copy)
    {
        object[] events = UnpackTrackEvents(transition, "\"strobeEasing\":0", "\"easing\":7,\"" + key + "\":0", 0);
        if (transition == 2)
        {
            Propagate(events[1], events[0]);
        }

        if (copy)
        {
            events = events.Select(evt => evt.GetType().GetMethod("GetCopy")!.Invoke(evt, null)!).ToArray();
        }

        CustomData data = ((ICustomData)events[1]).customData;
        Assert.That(data.ContainsKey(key), Is.True, "Linear is an explicit track override, not absent metadata.");
        Assert.That(Convert.ToInt32(data.Get<object>(key)), Is.EqualTo(0));
        object effect = TestHostCompat.GetUninitializedObject(FindType("LightColorGroupEffect")!);
        try
        {
            Link(events[0], events[1]);
            Stage(effect, events[0]);
            float progress = key == "colorEasing" ? NormalColorProgress(effect, 0.25f) : StrobeColorProgress(effect, 0.25f);
            Assert.That(progress, Is.EqualTo(0.25f).Within(0.000002f));
            Assert.That(Progress(effect, 0.25f), Is.EqualTo(0.015625f).Within(0.000002f));
            Assert.That(StrobeFadeEasing(effect)(0.3f), Is.EqualTo(0.3f).Within(0.000002f));
        }
        finally
        {
            chromafier.GetMethod("CleanupPostfix", All)!.Invoke(null, [effect]);
        }
    }

    // Nodes without the new keys must render exactly like native: interval progress and InOutCubic strobe fade.
    [Test]
    public void V3AbsentTrackEasingsFallBackToNativeIntervalAndFade()
    {
        object[] events = UnpackTrackEvents(1, "\"easing\":6", "\"color\":[1,1,1,1]", elementId: 0);
        object effect = TestHostCompat.GetUninitializedObject(FindType("LightColorGroupEffect")!);
        try
        {
            Link(events[0], events[1]);
            Stage(effect, events[0]);
            Assert.That(NormalColorProgress(effect, 0.25f), Is.EqualTo(Progress(effect, 0.25f)).Within(0.000002d),
                "Missing colorEasing must reuse the interval's own easing.");
            Assert.That(StrobeColorProgress(effect, 0.25f), Is.EqualTo(Progress(effect, 0.25f)).Within(0.000002d),
                "Missing strobeColorEasing must reuse the interval's own easing.");
            Func<float, float> fadeEase = StrobeFadeEasing(effect);
            Assert.That(fadeEase(0.5f), Is.EqualTo(0.5d).Within(0.000002d),
                "Missing strobeEasing must retain the native InOutCubic fade curve.");
        }
        finally
        {
            chromafier.GetMethod("CleanupPostfix", All)!.Invoke(null, [effect]);
        }
    }

    // Instant nodes can never own an interval endpoint's keys, so interval-bound keys are dropped there;
    // Extend nodes keep strobeEasing as inherited node state but must not adopt a predecessor's interval easings.
    [Test]
    public void V3TrackEasingsNormalizeAcrossInstantAndExtend()
    {
        object[] instant = UnpackTrackEvents(
            0,
            "\"strobeEasing\":16",
            "\"easing\":6,\"colorEasing\":4,\"strobeColorEasing\":5,\"strobeEasing\":17",
            elementId: 0);
        CustomData instantData = ((ICustomData)instant[1]).customData;
        Assert.That(instantData.ContainsKey("colorEasing"), Is.False,
            "An Instant node's colorEasing has no interval to drive and must be stripped.");
        Assert.That(instantData.ContainsKey("strobeColorEasing"), Is.True,
            "strobeColorEasing is inert ahead-node metadata on Instant nodes; only a per-light interval endpoint can apply it.");
        Assert.That(instantData.ContainsKey("strobeEasing"), Is.True,
            "strobeEasing is node-scoped and remains valid on Instant nodes.");
        Assert.That(instantData.ContainsKey("easing"), Is.False,
            "The existing interval easing strip must stay intact.");

        object[] extend = UnpackTrackEvents(
            2,
            "\"easing\":6,\"colorEasing\":4,\"strobeColorEasing\":5,\"strobeEasing\":16",
            "\"color\":[1,1,1,1]",
            elementId: 0);
        // Extend resolves its inherited payload at insert time through the same propagation the processor runs.
        Propagate(extend[1], extend[0]);
        CustomData extendData = ((ICustomData)extend[1]).customData;
        Assert.That(extendData.ContainsKey("colorEasing"), Is.False,
            "Extend must not inherit the predecessor interval's color easing.");
        Assert.That(extendData.ContainsKey("strobeColorEasing"), Is.False,
            "Extend must not inherit the predecessor interval's strobe color easing.");
        Assert.That(extendData.ContainsKey("strobeEasing"), Is.True,
            "strobeEasing is node state and inherits across Extend like color.");
    }

    private float NormalColorProgress(object effect, float t) => TrackProgress("GetNormalColorProgress", effect, t);

    private float StrobeColorProgress(object effect, float t) => TrackProgress("GetStrobeColorProgress", effect, t);

    private float TrackProgress(string method, object effect, float t)
    {
        MethodInfo? progress = chromafier.GetMethod(method, All);
        Assert.That(progress, Is.Not.Null, $"The chromafier must expose {method} for the independent track caches.");
        return (float)progress!.Invoke(null, [effect, t])!;
    }

    private Func<float, float> StrobeFadeEasing(object effect)
    {
        MethodInfo? easing = chromafier.GetMethod("GetStrobeFadeEasing", All);
        Assert.That(easing, Is.Not.Null, "The chromafier must expose GetStrobeFadeEasing for the custom strobe fade cache.");
        return (Func<float, float>)easing!.Invoke(null, [effect])!;
    }

    // Emit a two-node box where each event's customData fragment is supplied verbatim.
    private object[] UnpackTrackEvents(int transition, string currentExtra, string nextExtra, int elementId)
    {
        string json = "[{\"b\":0,\"g\":1,\"e\":[{\"f\":{\"f\":2,\"p\":0,\"t\":1},\"d\":1,\"t\":1,\"e\":[" +
            "{\"b\":0,\"i\":1,\"c\":0,\"s\":1,\"customData\":{\"color\":[0,0,0,1]," + currentExtra + "}}," +
            "{\"b\":1,\"i\":" + transition + ",\"c\":1,\"s\":1,\"customData\":" + nextCustom(nextExtra) + "}]}]}]";
        return UnpackJson(json, elementId);
    }

    private static string nextCustom(string nextExtra) =>
        string.IsNullOrEmpty(nextExtra)
            ? "{\"color\":[1,1,1,1]}"
            : "{\"color\":[1,1,1,1]," + nextExtra + "}";

    // Decimal metadata must not round a tiny fraction into a recognized integer ID when converted to double.
    [Test]
    public void V3InvalidClrNumericEasingCannotRoundIntoKnownId()
    {
        Type easingPatch = plugin.GetType("ChromaGLS.HarmonyPatches.GlsEasingRuntimePatch", true)!;
        MethodInfo? parse = easingPatch.GetMethod("TryGetCustomEasing", All);
        Assert.That(parse, Is.Not.Null, "V3 requires a strict shared numeric extension parser.");
        foreach (object raw in new object[] { 4.000000000000000000001m, double.NaN, double.PositiveInfinity, double.NegativeInfinity, "4", true })
        {
            object?[] args = [raw, null];
            Assert.That(parse!.Invoke(null, args), Is.False, $"Invalid numeric metadata {raw} must not become a known curve ID.");
        }
        object?[] integral = [4m, null];
        Assert.That(parse!.Invoke(null, integral), Is.True);
    }

    // Invalid values and Instant's stale metadata must not enable an eased interval or leak through Extend inheritance.
    [TestCase("\"4\"")]
    [TestCase("\"easeInSine\"")]
    [TestCase("4.5")]
    [TestCase("31")]
    [TestCase("99")]
    [TestCase("103")]
    [TestCase("-1")]
    [TestCase("0")]
    [TestCase("null")]
    [TestCase("true")]
    [TestCase("{}")]
    public void V3InvalidOrInstantEasingIsIgnored(string raw)
    {
        foreach (int transition in new[] { 0, 1, 2 })
        {
            object[] events = UnpackEvents(transition, raw, 0);
            CustomData data = ((ICustomData)events[1]).customData;
            Assert.That(data.ContainsKey("easing"), Is.False, "Unpack must not retain a stale or invalid easing as active inherited metadata.");
            AssertTransition(events[1], transition);
        }
        object[] instant = UnpackEvents(0, "4", 0);
        Assert.That(((ICustomData)instant[1]).customData.ContainsKey("easing"), Is.False, "Instant ignores even a valid stale curve.");
    }

    // easingType is an interval-owned string key: authored "HSV" opts the transition into true angular HSV
    // interpolation, while "RGB" and unknown values normalize back to the absent native default.
    [TestCase(1)]
    [TestCase(2)]
    public void V3EasingTypeHsvStagesAheadTransition(int transition)
    {
        object[] events = UnpackTrackEvents(transition, "\"strobeEasing\":16", "\"easingType\":\"HSV\"", elementId: 0);
        object effect = TestHostCompat.GetUninitializedObject(FindType("LightColorGroupEffect")!);
        try
        {
            Assert.That(((ICustomData)events[1]).customData.Get<object>("easingType"), Is.EqualTo("HSV"),
                "A valid authored easingType must survive converter normalization.");
            Link(events[0], events[1]);
            Stage(effect, events[0]);
            Assert.That(UsesTrueHsv(effect), Is.True,
                "easingType=HSV on the ahead node must activate true angular HSV for the transition.");
        }
        finally
        {
            chromafier.GetMethod("CleanupPostfix", All)!.Invoke(null, [effect]);
        }
    }

    // The interval's color space belongs to the ahead node alone: authored "RGB" is the absent default and a
    // source node's own easingType never leaks into the outgoing transition.
    [Test]
    public void V3EasingTypeRgbAndSourceHsvStayRgb()
    {
        object[] rgb = UnpackTrackEvents(1, "\"strobeEasing\":16", "\"easingType\":\"RGB\"", elementId: 0);
        Assert.That(((ICustomData)rgb[1]).customData.ContainsKey("easingType"), Is.False,
            "easingType=RGB is equivalent to leaving the key out and must normalize away.");
        object[] sourceOnly = UnpackTrackEvents(1, "\"easingType\":\"HSV\"", string.Empty, elementId: 0);
        object effect = TestHostCompat.GetUninitializedObject(FindType("LightColorGroupEffect")!);
        try
        {
            Link(rgb[0], rgb[1]);
            Stage(effect, rgb[0]);
            Assert.That(UsesTrueHsv(effect), Is.False,
                "An ahead node without authored HSV must keep the native RGB interpolation.");

            Link(sourceOnly[0], sourceOnly[1]);
            Stage(effect, sourceOnly[0]);
            Assert.That(UsesTrueHsv(effect), Is.False,
                "The source node's own easingType must not affect the transition it feeds.");
        }
        finally
        {
            chromafier.GetMethod("CleanupPostfix", All)!.Invoke(null, [effect]);
        }
    }

    // Instant endpoints own no interval, so authored easingType is stripped there; Extend keeps only its own
    // authored key and must not inherit the predecessor interval's color space.
    [Test]
    public void V3EasingTypeNormalizesAcrossInstantAndExtend()
    {
        object[] instant = UnpackTrackEvents(0, "\"strobeEasing\":16", "\"easingType\":\"HSV\"", elementId: 0);
        Assert.That(((ICustomData)instant[1]).customData.ContainsKey("easingType"), Is.False,
            "An Instant node's easingType has no interval to drive and must be stripped.");

        object[] extend = UnpackTrackEvents(2, "\"easingType\":\"HSV\"", string.Empty, elementId: 0);
        Assert.That(((ICustomData)extend[1]).customData.ContainsKey("easingType"), Is.False,
            "Extend must not inherit the predecessor interval's easingType.");
    }

    // GLSEasingTypeRibbonInputTest shares the angular contract: purple (hue 0.9) to orange (hue 0.1) crosses
    // the hue seam through red at the midpoint, never the normalized-hue detour through cyan.
    [Test]
    public void TrueHsvLerpTakesShortestAngularHuePath()
    {
        MethodInfo? lerp = chromafier.GetMethod("LerpTrueHsv", All);
        Assert.That(lerp, Is.Not.Null, "The chromafier must expose the shared angular HSV lerp for both render paths.");
        UnityEngine.Color start = UnityEngine.Color.HSVToRGB(0.9f, 1f, 1f);
        UnityEngine.Color end = UnityEngine.Color.HSVToRGB(0.1f, 1f, 1f);
        UnityEngine.Color mid = (UnityEngine.Color)lerp!.Invoke(null, [start, end, 0.5f])!;
        Assert.That(mid.r, Is.EqualTo(1f).Within(0.0001f), "Angular HSV must cross the 0/1 seam through red.");
        Assert.That(mid.g, Is.EqualTo(0f).Within(0.0001f));
        Assert.That(mid.b, Is.EqualTo(0f).Within(0.0001f));
    }

    // HsvStrobeFadeUsesAngularColorBlend keeps the pulse blend on the transition's staged color space
    // in every supported runtime path instead of reverting to RGB after both color tracks were resolved.
    [Test]
    public void HsvStrobeFadeUsesAngularColorBlend()
    {
        MethodInfo? lerp = chromafier.GetMethod("LerpStrobeFade", All);
        Assert.That(lerp, Is.Not.Null,
            "The chromafier must expose one shared strobe-fade blend for modern and legacy render paths.");
        UnityEngine.Color normal = UnityEngine.Color.HSVToRGB(0.9f, 1f, 1f);
        UnityEngine.Color strobe = UnityEngine.Color.HSVToRGB(0.1f, 1f, 1f);
        UnityEngine.Color mid = (UnityEngine.Color)lerp!.Invoke(null, [normal, strobe, 0.5f, true])!;
        Assert.That(mid.r, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(mid.g, Is.EqualTo(0f).Within(0.0001f));
        Assert.That(mid.b, Is.EqualTo(0f).Within(0.0001f));
    }

    private bool UsesTrueHsv(object effect)
    {
        MethodInfo? accessor = chromafier.GetMethod("IsTrueHsvTransition", All);
        Assert.That(accessor, Is.Not.Null, "The chromafier must expose the staged transition color-space for rendering and tests.");
        return (bool)accessor!.Invoke(null, [effect])!;
    }

    private object[] UnpackEvents(int transition, string rawEasing, int elementId)
    {
        // The predecessor deliberately has a different easing and color; Extend inherits only values, never its easing.
        string json = "[{\"b\":0,\"g\":1,\"e\":[{\"f\":{\"f\":2,\"p\":0,\"t\":1},\"d\":1,\"t\":1,\"e\":[" +
            "{\"b\":0,\"i\":1,\"c\":0,\"s\":0,\"customData\":{\"color\":[0,0,0,1],\"easing\":7}}," +
            "{\"b\":1,\"i\":" + transition + ",\"c\":1,\"s\":1,\"customData\":{\"color\":[1,1,1,1],\"easing\":" + rawEasing + "}}]}]}]";
        return UnpackJson(json, elementId);
    }

    private object[] UnpackJson(string json, int elementId)
    {
        Type deserializer = plugin.GetType("ChromaGLS.HarmonyPatches.GlsDeserializePatch", true)!;
        MethodInfo deserialize = deserializer.GetMethod("Prefix", All)!;
        IList groups = (IList)Activator.CreateInstance(deserialize.GetParameters()[1].ParameterType)!;
        using JsonTextReader reader = new(new StringReader(json));
        deserialize.Invoke(null, [reader, groups]);
        object saveBox = ((IEnumerable)groups[0]!.GetType().GetProperty("eventBoxes")!.GetValue(groups[0])!).Cast<object>().Single();
        Type loader = FindType("BeatmapDataLoaderVersion3.BeatmapDataLoader") ?? FindType("BeatmapDataLoader")!;
        Type nativeConverter = loader.GetNestedTypes(All).Single(type => type.Name.StartsWith("LightColorEventBoxConvert", StringComparison.Ordinal));
        MethodInfo convert = nativeConverter.GetMethods(All | BindingFlags.DeclaredOnly).Single(method => method.Name == "Convert" && method.GetParameters().Length == 2 && method.GetParameters()[0].ParameterType.IsInstanceOfType(saveBox));
        // Legacy converters accept LightGroupSO directly rather than ILightGroup; initialize only its managed element-count data.
        Type lightGroupType = convert.GetParameters()[1].ParameterType;
        object lightGroup = lightGroupType.IsInterface
            ? TestHostCompat.CreateProxy(lightGroupType, typeof(LightGroupProxy))
            : TestHostCompat.GetUninitializedObject(lightGroupType);
        if (!lightGroupType.IsInterface)
        {
            lightGroupType.GetField("_numberOfElements", All)!.SetValue(lightGroup, 2);
        }
        object box = convert.Invoke(convert.IsStatic ? null : TestHostCompat.GetUninitializedObject(nativeConverter), [saveBox, lightGroup])!;
        Assert.That(box, Is.Not.Null);
        Type converter = plugin.GetType("ChromaGLS.HarmonyPatches.GlsConverterPatches", true)!;
        converter.GetMethod("LightColorEventBoxConverterPostfix", All)!.Invoke(null, [saveBox, box]);
        MethodInfo unpack = box.GetType().GetMethod("Unpack", All)!;
        IList? output = null;
        object?[] args = unpack.GetParameters().Select(parameter =>
        {
            if (parameter.ParameterType.IsGenericType && parameter.ParameterType.GetGenericTypeDefinition() == typeof(List<>))
            {
                output = (IList)Activator.CreateInstance(parameter.ParameterType)!;
                return output;
            }
            if (parameter.ParameterType.IsInterface && parameter.ParameterType.Name.Contains("BeatToTime"))
            {
                return TestHostCompat.CreateProxy(parameter.ParameterType, typeof(BeatTimeProxy));
            }
            if (parameter.ParameterType.Name == "IBeatmapLightEventConverter")
            {
                // Native converter constructor options changed between 1.40.8 and 1.42.1; honor the selected runtime's declared defaults.
                ConstructorInfo constructor = FindType("BeatmapLightEventConverterNoConvert")!.GetConstructors().Single();
                return constructor.Invoke(constructor.GetParameters().Select(option => option.HasDefaultValue
                    ? option.DefaultValue
                    : Activator.CreateInstance(option.ParameterType)).ToArray());
            }
            return parameter.Name switch
            {
                "groupId" => (object)1,
                "elementId" => elementId,
                "maxBeat" => float.MaxValue,
                _ => Activator.CreateInstance(parameter.ParameterType),
            };
        }).ToArray();
        object? unpacked = unpack.Invoke(box, args);
        if (unpacked is IEnumerable enumerable)
        {
            Type listType = typeof(List<>).MakeGenericType(FindType("BeatmapEventData")!);
            output = (IList)Activator.CreateInstance(listType)!;
            foreach (object evt in enumerable)
            {
                output.Add(evt);
            }
        }
        Assert.That(output, Is.Not.Null);
        object?[] postfixArgs = [box, 0f, elementId, 0, float.MaxValue, output];
        converter.GetMethod("LightColorBeatmapEventDataBoxUnpackPostfix", All)!.Invoke(null, postfixArgs);
        return ((IEnumerable)postfixArgs[5]!).Cast<object>().ToArray();
    }

    private void Stage(object effect, object current)
    {
        MethodInfo? stage = chromafier.GetMethod("StageColorEasing", All);
        Assert.That(stage, Is.Not.Null, "V3 colors need an event-time easing stage on legacy as well as modern versions.");
        stage!.Invoke(null, [effect, current]);
    }

    private float Progress(object effect, float t) => (float)chromafier.GetMethod("GetColorProgress", All)!.Invoke(null, [effect, t])!;

    private static void Link(object current, object next)
    {
        Type baseType = FindType("BeatmapEventData")!;
        (baseType.GetField("<nextSameTypeEventData>k__BackingField", All)
            ?? baseType.GetField("nextSameTypeEventData", All))!.SetValue(current, next);
    }

    // GlsExtensionInheritanceTests moved Extend inheritance to the game's own propagation point: the insert
    // processor calls CopyColorDataFrom once the same-lane predecessor link exists. This host cannot run the
    // MonoMod detour, so mirror the processor exactly — native field copy first, then the production postfix.
    private void Propagate(object extension, object predecessor)
    {
        FindType("LightColorBeatmapEventData")!.GetMethod("CopyColorDataFrom", All)!.Invoke(extension, [predecessor]);
        plugin.GetType("ChromaGLS.HarmonyPatches.GlsConverterPatches", true)!
            .GetMethod("LightColorBeatmapEventDataCopyColorDataFromPostfix", All)!
            .Invoke(null, [extension, predecessor]);
    }

    private static void AssertTransition(object evt, int transition)
    {
        Type eventType = FindType("LightColorBeatmapEventData")!;
        object? nativeTransition = eventType.GetProperty("transitionType", All)?.GetValue(evt)
            ?? eventType.GetField("transitionType", All)?.GetValue(evt);
        if (nativeTransition != null)
        {
            Assert.That(System.Convert.ToInt32(nativeTransition), Is.EqualTo(transition));
        }
        else
        {
            // 1.37.1 stores this flag as a field; later runtimes expose a property with the same semantics.
            object? usePrevious = eventType.GetProperty("usePreviousValue", All)?.GetValue(evt)
                ?? eventType.GetField("usePreviousValue", All)?.GetValue(evt);
            Assert.That(usePrevious, Is.EqualTo(transition == 2), "Easing must not erase the Extend/usePrevious flag.");
        }
    }

    private static Type? FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name)).FirstOrDefault(type => type != null);

    public class LightGroupProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => 2;
    }

    public class BeatTimeProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => args![0];
    }
}
