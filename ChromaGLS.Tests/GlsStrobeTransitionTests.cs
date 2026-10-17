using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CustomJSONData.CustomBeatmap;
using HarmonyLib;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

// Exercise the built plugin's real strobe-interval endpoint staging and per-frame SetColor convergence
// through the actual HandleColorChangeBeatmapEvent + FloatTween path, not a copy of the math.
// Scene services are stubbed exactly like GlsEasingTests; every event/interval value is authored here.
[TestFixture]
[NonParallelizable]
internal sealed class GlsStrobeTransitionTests
{
    private const string HarmonyId = "com.eklipz.ChromaGLS";
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    private Harmony harmony = null!;
    private MethodInfo setEnabled = null!;
    private Type pluginType = null!;
    private Type effectType = null!;
    private ConstructorInfo eventConstructor = null!;
    private Type eventEaseType = null!;
    private Type tweenEaseType = null!;
    private FieldInfo nextEventField = null!;
    private FieldInfo fromFrequencyField = null!;
    private FieldInfo toFrequencyField = null!;
    private MethodInfo handler = null!;
    private ConstructorInfo distributedEventConstructor = null!;
    private ConstructorInfo distributionContextConstructor = null!;
    private MethodInfo instructionParser = null!;
    private Type progressType = null!;

    [SetUp]
    public void SetUp()
    {
        // The game's MonoMod detours only run on Unity Mono; other hosts crash during patch installation.
        if (Type.GetType("Mono.Runtime") == null)
        {
            Assert.Ignore("Harmony detour verification requires Unity's Mono runtime.");
        }

        harmony = new Harmony(HarmonyId);
        Assembly plugin = Assembly.Load("ChromaGLS");
        pluginType = plugin.GetType("ChromaGLS.Plugin", true)!;
        object instance = TestHostCompat.GetUninitializedObject(pluginType);
        pluginType.GetField("_harmonyInstance", All)!.SetValue(instance, harmony);
        pluginType.GetField("_patchesApplied", All)!.SetValue(instance, false);
        pluginType.GetField("_instance", All)!.SetValue(null, instance);
        setEnabled = pluginType.GetMethod("SetEnabled", All)!;
        setEnabled.Invoke(null, [true]);

        Type eventType = typeof(CustomData).Assembly.GetType("CustomJSONData.CustomBeatmap.CustomLightColorBeatmapEventData", true)!;
        eventConstructor = eventType.GetConstructors().Single();
        // Modern runtimes tween on easeType; 1.29.1 events carry a BeatmapEventTransitionType instead.
        eventEaseType = eventConstructor.GetParameters()
            .Single(parameter => parameter.Name is "easeType" or "transitionType").ParameterType;
        tweenEaseType = AccessTools.TypeByName("Tweening.FloatTween")
            .GetConstructors().Single(ctor => ctor.GetParameters().Length == 6)
            .GetParameters()[4].ParameterType;
        nextEventField = AccessTools.TypeByName("BeatmapEventData").GetField("<nextSameTypeEventData>k__BackingField", All)!;
        effectType = AccessTools.TypeByName("LightColorGroupEffect");
        fromFrequencyField = effectType.GetField("_fromStrobeFrequency", All)!;
        toFrequencyField = effectType.GetField("_toStrobeFrequency", All)!;
        handler = effectType.GetMethod("HandleColorChangeBeatmapEvent", All)!;

        // InheritedStrobeChannelRendersDistributedNormalColor attaches real parsed distribution
        // context through the same nested production types the converter installs on emitted events.
        Type converterType = plugin.GetType("ChromaGLS.HarmonyPatches.GlsConverterPatches", true)!;
        distributedEventConstructor = converterType
            .GetNestedType("ColorDistributedLightColorEventData", BindingFlags.NonPublic)!
            .GetConstructors()
            .Single();
        distributionContextConstructor = converterType
            .GetNestedType("EventColorDistributionContext", BindingFlags.NonPublic)!
            .GetConstructors()
            .Single();
        Type distributionType = plugin.GetType("ChromaGLS.HarmonyPatches.GlsColorDistribution", true)!;
        instructionParser = distributionType.GetMethod("Parse", All)!;
        progressType = distributionType.GetNestedType("Progress", BindingFlags.Public | BindingFlags.NonPublic)!;
    }

    [TearDown]
    public void TearDown()
    {
        setEnabled?.Invoke(null, [false]);
        harmony?.UnpatchSelf();
        pluginType?.GetField("_instance", All)!.SetValue(null, null);
    }

    // A native-frequency source transitioning into a custom-interval destination must stage the
    // destination endpoint from strobeInterval, not leave the destination's native f rate in place.
    // Otherwise the strobe rate snaps when the destination event activates at its own interval rate.
    [Test]
    public void NativeFrequencyTweensIntoCustomStrobeInterval()
    {
        WithColorEffect((effect, tween) =>
        {
            object current = CreateEvent(0f, strobeFrequency: 5, fade: false, customData: null);
            object next = CreateEvent(1f, strobeFrequency: 1, fade: false, new CustomData { ["strobeInterval"] = 5f });
            nextEventField.SetValue(current, next);
            handler.Invoke(effect, [current]);

            Assert.That(FromFrequency(effect), Is.EqualTo(5f).Within(0.000001f),
                "The source endpoint keeps the native f-derived frequency (5 / oneBeatDuration=1).");
            Assert.That(ToFrequency(effect), Is.EqualTo(0.2f).Within(0.000001f),
                "A strobeInterval=5 destination must stage 1/(5*oneBeatDuration)=0.2 Hz, not its native f=1 rate.");
        });
    }

    // The reported map's rel6 (f=0) -> rel18.75 (strobeInterval=5) boundary: a stopped source must
    // still tween into the destination's interval rate instead of snapping at the boundary.
    [Test]
    public void StoppedStrobeTweensIntoCustomStrobeInterval()
    {
        WithColorEffect((effect, tween) =>
        {
            object current = CreateEvent(0f, strobeFrequency: 0, fade: true, customData: null);
            object next = CreateEvent(7.65f, strobeFrequency: 1, fade: true, new CustomData { ["strobeInterval"] = 5f });
            nextEventField.SetValue(current, next);
            handler.Invoke(effect, [current]);

            Assert.That(FromFrequency(effect), Is.EqualTo(0f).Within(0.000001f));
            Assert.That(ToFrequency(effect), Is.EqualTo(0.2f).Within(0.000001f),
                "The destination strobeInterval must stage even when the source does not strobe.");
        });
    }

    // Control: a custom-interval source already tweens to a native-frequency destination (README contract).
    [Test]
    public void CustomStrobeIntervalTweensToNativeFrequency()
    {
        WithColorEffect((effect, tween) =>
        {
            object current = CreateEvent(0f, strobeFrequency: 1, fade: false, new CustomData { ["strobeInterval"] = 2f });
            object next = CreateEvent(1f, strobeFrequency: 3, fade: false, customData: null);
            nextEventField.SetValue(current, next);
            handler.Invoke(effect, [current]);

            Assert.That(FromFrequency(effect), Is.EqualTo(0.5f).Within(0.000001f));
            Assert.That(ToFrequency(effect), Is.EqualTo(3f).Within(0.000001f),
                "A destination without strobeInterval keeps its native f-derived frequency.");
        });
    }

    // Control: both custom endpoints stage independently.
    [Test]
    public void CustomStrobeIntervalTweensToCustomStrobeInterval()
    {
        WithColorEffect((effect, tween) =>
        {
            object current = CreateEvent(0f, strobeFrequency: 1, fade: false, new CustomData { ["strobeInterval"] = 5f });
            object next = CreateEvent(1f, strobeFrequency: 1, fade: false, new CustomData { ["strobeInterval"] = 1f });
            nextEventField.SetValue(current, next);
            handler.Invoke(effect, [current]);

            Assert.That(FromFrequency(effect), Is.EqualTo(0.2f).Within(0.000001f));
            Assert.That(ToFrequency(effect), Is.EqualTo(1f).Within(0.000001f));
        });
    }

    // Control: without a transition the interval holds a constant rate until the next event, matching
    // native SetData collapsing both endpoints, and the tween window still spans to the next event.
    [Test]
    public void CustomStrobeIntervalWithoutTransitionHoldsConstantRate()
    {
        WithColorEffect((effect, tween) =>
        {
            object current = CreateEvent(0f, strobeFrequency: 0, fade: true, new CustomData { ["strobeInterval"] = 2f });
            object next = CreateEvent(10f, strobeFrequency: 0, fade: true, new CustomData { ["strobeInterval"] = 1f }, ease: "None");
            nextEventField.SetValue(current, next);
            handler.Invoke(effect, [current]);

            Assert.That(FromFrequency(effect), Is.EqualTo(0.5f).Within(0.000001f));
            Assert.That(ToFrequency(effect), Is.EqualTo(0.5f).Within(0.000001f),
                "A non-transition destination keeps the source interval rate, not its own interval.");
            Assert.That(Duration(tween), Is.EqualTo(10f).Within(0.000001f),
                "The resumed strobe tween must span until the next event even without a color transition.");
        });
    }

    // Control: no strobeInterval anywhere leaves both endpoints at the native f-derived rates.
    [Test]
    public void NativeOnlyStrobeKeepsNativeEndpoints()
    {
        WithColorEffect((effect, tween) =>
        {
            object current = CreateEvent(0f, strobeFrequency: 2, fade: true, customData: null);
            object next = CreateEvent(1f, strobeFrequency: 3, fade: true, customData: null);
            nextEventField.SetValue(current, next);
            handler.Invoke(effect, [current]);

            Assert.That(FromFrequency(effect), Is.EqualTo(2f).Within(0.000001f));
            Assert.That(ToFrequency(effect), Is.EqualTo(3f).Within(0.000001f));
        });
    }

    // The supplied 88->93 pair starts a black-alternate strobe: 0->1 over five beats reaches phase 0.5 just before native resets to zero.
    [TestCase(true)]
    [TestCase(false)]
    public void StartingBlackStrobeDoesNotSnapBrightAtBeat93(bool authoredColors)
    {
        WithColorEffect((effect, tween) =>
        {
            CustomData sourceData = new() { ["cg_sb"] = 0f, ["cg_sf"] = true };
            CustomData targetData = new() { ["cg_sb"] = 0f, ["cg_sf"] = true };
            if (authoredColors)
            {
                sourceData["color"] = new List<object> { 1f, 0.4f, 0.913f };
                sourceData["colorDistributions"] = new List<object> { "h,-0.2,L,l" };
                sourceData["colorEasing"] = 30;
                targetData["color"] = new List<object> { 0.678f, 0.4f, 1f };
                targetData["colorDistributions"] = new List<object> { "h,0.1,L,l" };
                targetData["strobeColorDistributions"] = new List<object> { "s,-0.2,IOBk" };
                targetData["strobeColor"] = new List<object> { 0.969f, 0.384f, 0.71f };
                targetData["colorEasing"] = 22;
            }

            // The test host uses one-second beats, preserving the report's five-beat duration and 2.5 integrated cycles.
            object current = CreateEvent(88f, 0, true, sourceData, brightness: 0.5f, strobeBrightness: 0f);
            object next = CreateEvent(93f, 1, true, targetData, brightness: 0.5f, strobeBrightness: 0f);
            nextEventField.SetValue(current, next);
            handler.Invoke(effect, [current]);
            Assert.That(Duration(tween), Is.EqualTo(5f).Within(0.0001f));
            MethodInfo sample = tween.GetType().GetMethod("Sample")!;
            sample.Invoke(tween, [0.001f]);
            sample.Invoke(tween, [0.99998f]);
            Color before = capturedColor;
            handler.Invoke(effect, [next]);
            sample.Invoke(tween, [0f]);
            Color at = capturedColor;
            TestContext.WriteLine($"[BlackStrobeStart] authored={authoredColors} before={before} at={at}");
            Assert.That(before.a, Is.EqualTo(at.a).Within(0.002f),
                "The incoming black strobe phase must not snap to the destination's primary brightness at beat 93.");
            Assert.That(at.a, Is.EqualTo(0.5f).Within(0.002f),
                "The active node retains its native phase-zero primary brightness.");
            Assert.That(before.r, Is.EqualTo(at.r).Within(0.002f));
            Assert.That(before.g, Is.EqualTo(at.g).Within(0.002f));
            Assert.That(before.b, Is.EqualTo(at.b).Within(0.002f));
        });
    }

    // A faded strobe transitioning into a non-strobing destination must converge to the destination
    // primary color. The phase integral freezes at an arbitrary point (duration 7 at 1 Hz ends on
    // phase 0.5, the full-strobe extreme; 3.5 ends at 0.75, a half blend), so the strobe contribution
    // must decay with the instantaneous frequency instead of snapping to primary at the boundary.
    // The destination's unused strobeColor must not leak into the final output either.
    [TestCase(7f)]
    [TestCase(3.5f)]
    public void FadeStrobeToNoStrobeConvergesToDestinationPrimary(float duration)
    {
        WithColorEffect((effect, tween) =>
        {
            CustomData sourceData = new()
            {
                ["color"] = new List<object> { 0.179f, 1f, 0f },
                ["strobeColor"] = new List<object> { 0.969f, 0f, 0.942f },
                ["strobeInterval"] = 1f,
                // 1.29.1 backports strobe brightness/fade through customData keys; modern runtimes ignore them.
                ["cg_sb"] = 1f,
                ["cg_sf"] = true,
            };
            CustomData destinationData = new()
            {
                ["color"] = new List<object> { 0f, 0f, 1f },
                ["strobeColor"] = new List<object> { 0.969f, 0f, 0f },
                ["cg_sb"] = 1f,
                ["cg_sf"] = true,
            };
            object current = CreateEvent(0f, strobeFrequency: 1, fade: true, sourceData);
            object next = CreateEvent(duration, strobeFrequency: 0, fade: true, destinationData);
            nextEventField.SetValue(current, next);
            handler.Invoke(effect, [current]);

            Assert.That(FromFrequency(effect), Is.EqualTo(1f).Within(0.000001f));
            Assert.That(ToFrequency(effect), Is.EqualTo(0f).Within(0.000001f));

            MethodInfo sample = tween.GetType().GetMethod("Sample")!;
            // The 1.29.1 path intentionally renders one deferred t=0 frame before live samples.
            sample.Invoke(tween, [0.001f]);
            sample.Invoke(tween, [0.5f]);
            Assert.That(capturedColor.r, Is.GreaterThan(0.3f).And.LessThan(0.98f),
                "The strobe must still visibly blend mid-transition; convergence must not disable the strobe.");

            sample.Invoke(tween, [0.999f]);
            Assert.That(capturedColor.r, Is.LessThan(0.05f),
                "At the end of a transition into a non-strobing destination the output must converge to primary, not freeze on a strobe/primary mix.");
            Assert.That(capturedColor.b, Is.GreaterThan(0.95f),
                "The final blend must reach the destination primary (blue) regardless of the accumulated phase.");
        });
    }

    // The same convergence applies when the strobing source uses a native f frequency (the map's
    // box0 rel0 f=5 -> rel6 f=0 boundary shape) whenever the custom path owns the frame.
    [Test]
    public void NativeFadeStrobeToNoStrobeConvergesToDestinationPrimary()
    {
        WithColorEffect((effect, tween) =>
        {
            CustomData sourceData = new()
            {
                ["color"] = new List<object> { 0.179f, 1f, 0f },
                ["strobeColor"] = new List<object> { 0.969f, 0f, 0.942f },
                ["cg_sb"] = 1f,
                ["cg_sf"] = true,
            };
            CustomData destinationData = new()
            {
                ["color"] = new List<object> { 0f, 0f, 1f },
                ["strobeColor"] = new List<object> { 0.969f, 0f, 0f },
                ["cg_sb"] = 1f,
                ["cg_sf"] = true,
            };
            object current = CreateEvent(0f, strobeFrequency: 2, fade: true, sourceData);
            // Duration 3.5s at 2 Hz ends the phase integral at 3.5 cycles: the full-strobe extreme.
            object next = CreateEvent(3.5f, strobeFrequency: 0, fade: true, destinationData);
            nextEventField.SetValue(current, next);
            handler.Invoke(effect, [current]);

            MethodInfo sample = tween.GetType().GetMethod("Sample")!;
            sample.Invoke(tween, [0.001f]);
            sample.Invoke(tween, [0.999f]);
            Assert.That(capturedColor.r, Is.LessThan(0.05f),
                "A native-frequency strobe fading into a stopped destination must also converge to primary.");
            Assert.That(capturedColor.b, Is.GreaterThan(0.95f));
        });
    }

    // A hard (non-fade) strobe ending on its on-phase must converge to the destination primary too,
    // rather than holding the strobe color until the boundary and snapping.
    [Test]
    public void HardStrobeToNoStrobeConvergesToDestinationPrimary()
    {
        WithColorEffect((effect, tween) =>
        {
            CustomData sourceData = new()
            {
                ["color"] = new List<object> { 0.179f, 1f, 0f },
                ["strobeColor"] = new List<object> { 0.969f, 0f, 0.942f },
                ["strobeInterval"] = 1f,
                ["cg_sb"] = 1f,
                ["cg_sf"] = false,
            };
            CustomData destinationData = new()
            {
                ["color"] = new List<object> { 0f, 0f, 1f },
                ["strobeColor"] = new List<object> { 0.969f, 0f, 0f },
                ["cg_sb"] = 1f,
                ["cg_sf"] = false,
            };
            object current = CreateEvent(0f, strobeFrequency: 1, fade: false, sourceData);
            // Duration 7.5s at 1 Hz ends the phase integral at 3.75 cycles: inside the strobe half.
            object next = CreateEvent(7.5f, strobeFrequency: 0, fade: false, destinationData);
            nextEventField.SetValue(current, next);
            handler.Invoke(effect, [current]);

            MethodInfo sample = tween.GetType().GetMethod("Sample")!;
            sample.Invoke(tween, [0.999f]);
            Assert.That(capturedColor.r, Is.LessThan(0.05f),
                "A hard strobe landing on its on-phase must decay to primary instead of snapping at the boundary.");
            Assert.That(capturedColor.b, Is.GreaterThan(0.95f));
        });
    }

    // Control: while both endpoints strobe, the fade blend must not be attenuated; at phase 0.5 the
    // output is the full strobe endpoint exactly as native renders it.
    [Test]
    public void ActiveStrobeTransitionKeepsFullAmplitude()
    {
        WithColorEffect((effect, tween) =>
        {
            CustomData sourceData = new()
            {
                ["color"] = new List<object> { 0.179f, 1f, 0f },
                ["strobeColor"] = new List<object> { 0.969f, 0f, 0.942f },
                ["cg_sb"] = 1f,
                ["cg_sf"] = true,
            };
            CustomData destinationData = new()
            {
                ["color"] = new List<object> { 0f, 0f, 1f },
                ["strobeColor"] = new List<object> { 0.969f, 0f, 0.942f },
                ["cg_sb"] = 1f,
                ["cg_sf"] = true,
            };
            object current = CreateEvent(0f, strobeFrequency: 1, fade: true, sourceData);
            // Duration 4s at a constant 1 Hz puts t=0.125 exactly on phase 0.5 (full strobe).
            object next = CreateEvent(4f, strobeFrequency: 1, fade: true, destinationData);
            nextEventField.SetValue(current, next);
            handler.Invoke(effect, [current]);

            MethodInfo sample = tween.GetType().GetMethod("Sample")!;
            sample.Invoke(tween, [0.001f]);
            sample.Invoke(tween, [0.125f]);
            Assert.That(capturedColor.r, Is.EqualTo(0.969f).Within(0.0001f),
                "An uninterrupted strobe must keep its full blend at the phase peak; no decay may apply.");
            Assert.That(capturedColor.b, Is.EqualTo(0.942f).Within(0.0001f));
        });
    }

    // CM's shared LightColorTween prep performs ENDPOINT COLLAPSE for a zero-frequency endpoint, not
    // frequency-amplitude attenuation: the stopping endpoint's strobe channel equals its primary RGBA,
    // so every interior sample must be identical no matter what strobeColor the destination authors.
    [Test]
    public void StoppedDestinationIgnoresUnusedStrobeColor()
    {
        CustomData sourceData = new()
        {
            ["color"] = new List<object> { 0.179f, 1f, 0f },
            ["strobeColor"] = new List<object> { 0.969f, 0f, 0.942f },
            ["cg_sb"] = 1f,
            ["cg_sf"] = true,
        };
        float[] times = [0.1f, 0.25f, 0.5f, 0.75f, 0.9f, 0.999f];
        List<Color> redDestination = SampleTransition(
            sourceData,
            new CustomData
            {
                ["color"] = new List<object> { 0f, 0f, 1f },
                ["strobeColor"] = new List<object> { 0.969f, 0f, 0f },
                ["cg_sb"] = 1f,
                ["cg_sf"] = true,
            },
            sourceFrequency: 1,
            destinationFrequency: 0,
            fade: true,
            duration: 4f,
            sampleTimes: times);
        List<Color> greenDestination = SampleTransition(
            sourceData,
            new CustomData
            {
                ["color"] = new List<object> { 0f, 0f, 1f },
                ["strobeColor"] = new List<object> { 0f, 1f, 0.3f },
                ["cg_sb"] = 1f,
                ["cg_sf"] = true,
            },
            sourceFrequency: 1,
            destinationFrequency: 0,
            fade: true,
            duration: 4f,
            sampleTimes: times);
        List<Color> noStrobeDestination = SampleTransition(
            sourceData,
            new CustomData
            {
                ["color"] = new List<object> { 0f, 0f, 1f },
                ["cg_sb"] = 1f,
                ["cg_sf"] = true,
            },
            sourceFrequency: 1,
            destinationFrequency: 0,
            fade: true,
            duration: 4f,
            sampleTimes: times);

        AssertSameSamples(redDestination, greenDestination, times,
            "A stopped destination's unused strobeColor must never reach the rendered interval.");
        AssertSameSamples(redDestination, noStrobeDestination, times,
            "Authoring an unused strobeColor on a stopped destination must be a no-op.");
    }

    // The same endpoint collapse ignores the stopping endpoint's strobe brightness: the strobe channel's
    // alpha endpoint must equal the destination's composed primary alpha, not the authored sb value.
    [Test]
    public void StoppedDestinationIgnoresUnusedStrobeBrightness()
    {
        CustomData sourceData = new()
        {
            ["color"] = new List<object> { 0.179f, 1f, 0f },
            ["strobeColor"] = new List<object> { 0.969f, 0f, 0.942f },
            ["cg_sb"] = 1f,
            ["cg_sf"] = true,
        };
        float[] times = [0.1f, 0.25f, 0.5f, 0.75f, 0.9f, 0.999f];
        List<Color> dimStrobe = SampleTransition(
            sourceData,
            new CustomData
            {
                ["color"] = new List<object> { 0f, 0f, 1f },
                ["strobeColor"] = new List<object> { 0.969f, 0f, 0f },
                ["cg_sb"] = 0.15f,
                ["cg_sf"] = true,
            },
            sourceFrequency: 1,
            destinationFrequency: 0,
            fade: true,
            duration: 4f,
            sampleTimes: times,
            destinationStrobeBrightness: 0.15f);
        List<Color> brightStrobe = SampleTransition(
            sourceData,
            new CustomData
            {
                ["color"] = new List<object> { 0f, 0f, 1f },
                ["strobeColor"] = new List<object> { 0.969f, 0f, 0f },
                ["cg_sb"] = 1f,
                ["cg_sf"] = true,
            },
            sourceFrequency: 1,
            destinationFrequency: 0,
            fade: true,
            duration: 4f,
            sampleTimes: times,
            destinationStrobeBrightness: 1f);

        AssertSameSamples(dimStrobe, brightStrobe, times,
            "A stopped destination's unused strobeBrightness must never reach the rendered interval.");
    }

    // A zero-frequency source transitioning INTO a strobe begins with both channels equal to the source
    // primary; the source's unused strobeColor/sb must not leak into the fade-in anywhere in the interval.
    [Test]
    public void StoppedSourceIgnoresUnusedStrobeChannel()
    {
        CustomData destinationData = new()
        {
            ["color"] = new List<object> { 0f, 0f, 1f },
            ["strobeColor"] = new List<object> { 0.969f, 0f, 0.942f },
            ["cg_sb"] = 1f,
            ["cg_sf"] = false,
        };
        float[] times = [0.25f, 0.5f, 0.75f];
        List<Color> unusedRed = SampleTransition(
            new CustomData
            {
                ["color"] = new List<object> { 0f, 1f, 0f },
                ["strobeColor"] = new List<object> { 0.969f, 0f, 0f },
                ["cg_sb"] = 0.2f,
                ["cg_sf"] = false,
            },
            destinationData,
            sourceFrequency: 0,
            destinationFrequency: 1,
            fade: false,
            duration: 4f,
            sampleTimes: times,
            sourceStrobeBrightness: 0.2f);
        List<Color> noStrobeSource = SampleTransition(
            new CustomData
            {
                ["color"] = new List<object> { 0f, 1f, 0f },
                ["cg_sb"] = 1f,
                ["cg_sf"] = false,
            },
            destinationData,
            sourceFrequency: 0,
            destinationFrequency: 1,
            fade: false,
            duration: 4f,
            sampleTimes: times);

        AssertSameSamples(unusedRed, noStrobeSource, times,
            "A stopped source's unused strobe channel must collapse onto the source primary.");
        // At t=0.5 the phase integral (f 0->1 over 4s) lands exactly on 0.5, the strobe half: the output
        // is the strobe track's midpoint between the collapsed source primary and the destination strobe.
        Assert.That(noStrobeSource[1].g, Is.EqualTo(0.5f).Within(0.001f),
            "The collapsed strobe channel starts on the source primary (green), not the unused red strobe.");
        Assert.That(noStrobeSource[1].r, Is.EqualTo(0.969f * 0.5f).Within(0.001f));
        Assert.That(noStrobeSource[1].a, Is.EqualTo(1f).Within(0.001f),
            "The collapsed source endpoint must carry the source primary alpha, not the unused sb.");
    }

    // Exact endpoint-collapse expectations: the normal track follows the destination's colorEasing
    // (InCubic) while the collapsed strobe track follows the same per-light endpoint's strobeColorEasing
    // (InSine); the native integrated phase selects between the two channels without any amplitude envelope.
    [Test]
    public void StoppedDestinationCollapsesStrobeTrackWithIndependentEasings()
    {
        CustomData sourceData = new()
        {
            ["color"] = new List<object> { 0f, 1f, 0f },
            ["strobeColor"] = new List<object> { 0.969f, 0f, 0.942f },
            ["cg_sb"] = 1f,
            ["cg_sf"] = true,
        };
        CustomData destinationData = new()
        {
            ["color"] = new List<object> { 0f, 0f, 1f },
            ["strobeColor"] = new List<object> { 0.969f, 0f, 0f },
            ["colorEasing"] = 7,
            ["strobeColorEasing"] = 4,
            ["cg_sb"] = 0.35f,
            ["cg_sf"] = true,
        };
        float[] times = [0.2929f, 0.5f, 0.75f];
        List<Color> samples = SampleTransition(
            sourceData,
            destinationData,
            sourceFrequency: 1,
            destinationFrequency: 0,
            fade: true,
            duration: 4f,
            sampleTimes: times,
            destinationStrobeBrightness: 0.35f);

        Color strobeFrom = new(0.969f, 0f, 0.942f, 1f);
        Color primaryTo = new(0f, 0f, 1f, 1f);
        Color normalFrom = new(0f, 1f, 0f, 1f);
        for (int index = 0; index < times.Length; index++)
        {
            float t = times[index];
            // Phase: e = t*4; phase = (fromF*e - fromF*e^2/(2d) + toF*e^2/(2d)) mod 1 with toF = 0.
            float elapsed = t * 4f;
            float phase = Mathf.Repeat(elapsed - (elapsed * elapsed / 8f), 1f);
            float fade = InOutCubic(1f - Mathf.Abs((phase * 2f) - 1f));
            Color normal = Color.LerpUnclamped(normalFrom, primaryTo, InCubic(t));
            normal.a = 1f;
            Color strobe = Color.LerpUnclamped(strobeFrom, primaryTo, InSine(t));
            strobe.a = Mathf.LerpUnclamped(1f, 1f, t);
            Color expected = Color.LerpUnclamped(normal, strobe, fade);
            Assert.That(samples[index].r, Is.EqualTo(expected.r).Within(0.001f),
                $"t={t}: endpoint collapse + independent strobe easing expected r={expected.r:F4}.");
            Assert.That(samples[index].g, Is.EqualTo(expected.g).Within(0.001f),
                $"t={t}: endpoint collapse + independent strobe easing expected g={expected.g:F4}.");
            Assert.That(samples[index].b, Is.EqualTo(expected.b).Within(0.001f),
                $"t={t}: endpoint collapse + independent strobe easing expected b={expected.b:F4}.");
            Assert.That(samples[index].a, Is.EqualTo(expected.a).Within(0.001f),
                $"t={t}: endpoint collapse + independent strobe easing expected a={expected.a:F4}.");
        }
    }

    // Across a group/box boundary the stopping endpoint AND the strobe track's easing must come from the
    // authoritative per-light nextSameTypeEventData (D); a same-box successor (B) that is not the light's
    // next event supplies neither its strobeColor nor its strobeColorEasing.
    [Test]
    public void StoppedDestinationUsesPerLightNextEventAcrossBoxBoundary()
    {
        CustomData sourceData = new()
        {
            ["color"] = new List<object> { 0f, 1f, 0f },
            ["strobeColor"] = new List<object> { 0.969f, 0f, 0.942f },
            ["cg_sb"] = 1f,
            ["cg_sf"] = false,
        };
        CustomData perLightNextData = new()
        {
            ["color"] = new List<object> { 0f, 0f, 1f },
            ["colorEasing"] = 7,
            ["strobeColorEasing"] = 4,
            ["cg_sb"] = 0.2f,
            ["cg_sf"] = false,
        };
        float[] times = [0.25f, 0.5f, 0.999f];
        List<Color> samples = SampleTransition(
            sourceData,
            perLightNextData,
            sourceFrequency: 1,
            destinationFrequency: 0,
            fade: false,
            duration: 4f,
            sampleTimes: times,
            destinationBrightness: 0.6f,
            destinationStrobeBrightness: 0.2f);

        Color strobeFrom = new(0.969f, 0f, 0.942f, 1f);
        Color primaryTo = new(0f, 0f, 1f, 0.6f);
        Color normalFrom = new(0f, 1f, 0f, 1f);
        for (int index = 0; index < times.Length; index++)
        {
            float t = times[index];
            float elapsed = t * 4f;
            float phase = Mathf.Repeat(elapsed - (elapsed * elapsed / 8f), 1f);
            Color normal = Color.LerpUnclamped(normalFrom, primaryTo, InCubic(t));
            normal.a = Mathf.LerpUnclamped(1f, 0.6f, t);
            Color strobe = Color.LerpUnclamped(strobeFrom, primaryTo, InSine(t));
            strobe.a = Mathf.LerpUnclamped(1f, 0.6f, t);
            // The custom path selects the strobe channel at phase >= 0.5 and keeps the normal channel below.
            Color expected = phase >= 0.5f ? strobe : normal;
            Assert.That(samples[index].r, Is.EqualTo(expected.r).Within(0.001f),
                $"t={t} phase={phase:F3}: strobe endpoint must collapse to the per-light blue primary, not the stale box color.");
            Assert.That(samples[index].g, Is.EqualTo(expected.g).Within(0.001f),
                $"t={t} phase={phase:F3}: stale box-local strobeColor must not leak across the boundary.");
            Assert.That(samples[index].b, Is.EqualTo(expected.b).Within(0.001f),
                $"t={t} phase={phase:F3}: expected b={expected.b:F4}.");
            Assert.That(samples[index].a, Is.EqualTo(expected.a).Within(0.001f),
                $"t={t} phase={phase:F3}: the strobe brightness endpoint must equal the per-light primary alpha (0.6).");
        }
    }

    // CM prepares the strobe track from the same authoritative per-light nextSameTypeEventData as the
    // normal track: the transition into B/C approaches that node's authored strobeColor on that node's
    // strobeColorEasing.
    [Test]
    public void PerLightNextOwnsStrobeEndpointAcrossBoxBoundary()
    {
        CustomData sourceData = new()
        {
            ["color"] = new List<object> { 0f, 1f, 0f },
            ["strobeColor"] = new List<object> { 0.969f, 0f, 0.942f },
            ["cg_sb"] = 1f,
            ["cg_sf"] = false,
        };
        CustomData perLightNextData = new()
        {
            ["color"] = new List<object> { 0f, 0f, 1f },
            ["strobeColor"] = new List<object> { 0f, 1f, 1f },
            ["strobeColorEasing"] = 4,
            ["cg_sb"] = 1f,
            ["cg_sf"] = false,
        };
        float[] times = [0.375f, 0.5f, 0.875f, 0.999f];
        List<Color> samples = SampleTransition(
            sourceData,
            perLightNextData,
            sourceFrequency: 1,
            destinationFrequency: 1,
            fade: false,
            duration: 4f,
            sampleTimes: times);

        Color strobeFrom = new(0.969f, 0f, 0.942f, 1f);
        Color strobeTo = new(0f, 1f, 1f, 1f);
        Color normalFrom = new(0f, 1f, 0f, 1f);
        Color normalTo = new(0f, 0f, 1f, 1f);
        for (int index = 0; index < times.Length; index++)
        {
            float t = times[index];
            // Constant 1 Hz both ends over 4s: phase = frac(4t); the custom strobe branch covers >= 0.5.
            float phase = Mathf.Repeat(t * 4f, 1f);
            Color normal = Color.LerpUnclamped(normalFrom, normalTo, t);
            Color strobe = Color.LerpUnclamped(strobeFrom, strobeTo, InSine(t));
            Color expected = phase >= 0.5f ? strobe : normal;
            Assert.That(samples[index].r, Is.EqualTo(expected.r).Within(0.001f),
                $"t={t} phase={phase:F3}: the strobe track must approach the per-light next node's strobeColor on its strobeColorEasing.");
            Assert.That(samples[index].g, Is.EqualTo(expected.g).Within(0.001f),
                $"t={t} phase={phase:F3}: expected g={expected.g:F4}.");
            Assert.That(samples[index].b, Is.EqualTo(expected.b).Within(0.001f),
                $"t={t} phase={phase:F3}: expected b={expected.b:F4}.");
            Assert.That(samples[index].a, Is.EqualTo(expected.a).Within(0.001f),
                $"t={t} phase={phase:F3}: expected a={expected.a:F4}.");
        }
    }

    [TestCase(0.9f, 0.75f, false, 1f)]
    [TestCase(0.9f, 0.95f, false, 0f)]
    [TestCase(0f, 0.25f, false, 0f)]
    [TestCase(1f, 0.75f, false, 1f)]
    [TestCase(-1f, 0.25f, false, 0f)]
    [TestCase(2f, 0.75f, false, 1f)]
    [TestCase(0.9f, 0.45f, true, 0.875f)]
    [TestCase(0.9f, 0.95f, true, 0.125f)]
    [TestCase(0.5f, 0.75f, true, 0.125f)]
    [TestCase(0f, 0.25f, true, 0.015625f)]
    [TestCase(1f, 0.25f, true, 0.984375f)]
    public void StrobePulseWidthControlsNativeAndCustomTracks(float pulseWidth, float phase, bool fade, float expectedAlpha)
    {
        CustomData data = new() { ["strobePulseWidth"] = pulseWidth };
        if (fade)
        {
            data["strobeEasing"] = 7;
            data["cg_sf"] = true;
        }

        data["cg_sb"] = 0f;
        List<Color> samples = SampleTransition(data, new CustomData { ["cg_sb"] = 0f, ["strobePulseWidth"] = pulseWidth },
            1, 1, fade, 1f, [phase], sourceStrobeBrightness: 0f, destinationStrobeBrightness: 0f);
        Assert.That(samples[0].a, Is.EqualTo(expectedAlpha).Within(0.0001f));
    }

    [Test]
    public void StrobePulseWidthAloneOverridesNativeHardStrobe()
    {
        List<Color> samples = SampleTransition(new CustomData { ["strobePulseWidth"] = 0.9f }, null,
            1, 1, false, 1f, [0.75f, 0.95f], sourceStrobeBrightness: 0f, destinationStrobeBrightness: 0f,
            destinationEase: "None");
        Assert.That(samples[0].a, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(samples[1].a, Is.EqualTo(0f).Within(0.0001f));
    }

    [Test]
    public void MissingStrobePulseWidthUsesVerticallyFlippedFallingEasing()
    {
        CustomData data = new() { ["strobeEasing"] = 7, ["cg_sf"] = true, ["cg_sb"] = 0f };
        List<Color> samples = SampleTransition(data, new CustomData { ["cg_sb"] = 0f },
            1, 1, true, 1f, [0.75f], sourceStrobeBrightness: 0f, destinationStrobeBrightness: 0f);
        Assert.That(samples[0].a, Is.EqualTo(0.125f).Within(0.0001f));
    }

    [TestCase(0.1f, 0.9f, 0.25f, true, false)]
    [TestCase(0.9f, 0.1f, 0.75f, true, false)]
    [TestCase(0.1f, 0.9f, 0.25f, true, true)]
    [TestCase(0.9f, 0.1f, 0.75f, true, true)]
    [TestCase(0.5f, 0.9f, 0.75f, true, false)]
    [TestCase(0.9f, 0.5f, 0.75f, true, false)]
    [TestCase(0f, 1f, 0.25f, true, true)]
    [TestCase(1f, 0f, 0.75f, true, true)]
    [TestCase(0f, 1f, 0.25f, true, false)]
    [TestCase(0.1f, 0.9f, 0.25f, false, false)]
    [TestCase(0.9f, 0.1f, 0.75f, false, true)]
    public void StrobePulseWidthInterpolatesOnlyForTransitions(float from, float to, float progress, bool transition, bool fade)
    {
        CustomData source = new() { ["cg_sb"] = 0f, ["cg_sf"] = fade };
        CustomData destination = new() { ["cg_sb"] = 0f };
        if (from != 0.5f)
        {
            source["strobePulseWidth"] = from;
        }

        if (to != 0.5f)
        {
            destination["strobePulseWidth"] = to;
        }

        if (fade)
        {
            source["strobeEasing"] = 7;
        }

        if (transition)
        {
            destination["easing"] = 7;
        }

        List<Color> samples = SampleTransition(source, destination, 1, 1, fade, 1f, [progress],
            sourceStrobeBrightness: 0f, destinationStrobeBrightness: 0f,
            destinationEase: transition ? "Linear" : "None");
        float pulseWidth = transition ? from + ((to - from) * progress) : from;
        float amount = progress < pulseWidth
            ? InCubic(progress / pulseWidth)
            : 1f - InCubic((progress - pulseWidth) / (1f - pulseWidth));
        float alpha = fade ? 1f - amount : progress >= pulseWidth ? 0f : 1f;
        Assert.That(samples[0].a, Is.EqualTo(alpha).Within(0.0001f),
            "Pulse width follows the strobe clock toward the transition endpoint, not its color easing.");
    }

    [TestCase(0.05f, 0.25f)]
    [TestCase(0.15f, 0.75f)]
    [TestCase(0.4f, 0.75f)]
    [TestCase(0.8f, 0.25f)]
    public void ExplicitLinearStrobeEasingRendersReportedPulseWidthAndDistribution(float phase, float blend)
    {
        WithColorEffect((effect, tween) =>
        {
            CustomData data = new()
            {
                ["color"] = new List<object> { 0f, 1f, 0.197f },
                ["strobeColorDistributions"] = new List<object> { "h,0.3,L,l" },
                ["a"] = "b",
                ["strobePulseWidth"] = 0.2f,
                ["strobeEasing"] = 0,
                ["cg_sb"] = 0.2f,
                ["cg_sf"] = true,
            };
            object current = CreateDistributedEvent(0f, 1, true, data,
                CreateDistributionContext(null, ["h,0.3,L,l"]), ease: "None", strobeBrightness: 0.2f);
            object next = CreateEvent(1f, 1, true,
                new CustomData { ["cg_sb"] = 0.2f, ["cg_sf"] = true }, ease: "None", strobeBrightness: 0.2f);
            nextEventField.SetValue(current, next);
            handler.Invoke(effect, [current]);
            MethodInfo sample = tween.GetType().GetMethod("Sample")!;
            sample.Invoke(tween, [0.001f]);
            sample.Invoke(tween, [phase]);
            Color normal = new(0f, 1f, 0.197f, 1f);
            Color strobe = ShiftHue(normal, 0.3f);
            strobe.a = 0.2f;
            Color expected = Color.LerpUnclamped(normal, strobe, blend);
            Assert.That(capturedColor.r, Is.EqualTo(expected.r).Within(0.0001f));
            Assert.That(capturedColor.g, Is.EqualTo(expected.g).Within(0.0001f));
            Assert.That(capturedColor.b, Is.EqualTo(expected.b).Within(0.0001f));
            Assert.That(capturedColor.a, Is.EqualTo(expected.a).Within(0.0001f),
                "Explicit strobeEasing=0 must blend linearly on both sides of the Pulse width peak.");
        });
    }

    [TestCase("colorEasing", true)]
    [TestCase("colorEasing", false)]
    [TestCase("strobeColorEasing", true)]
    [TestCase("strobeColorEasing", false)]
    public void ExplicitLinearColorTracksOverrideCubicInterval(string key, bool authored)
    {
        CustomData source = new()
        {
            ["color"] = new List<object> { 0f, 0f, 0f },
            ["strobeColor"] = new List<object> { 0f, 0f, 0f },
            ["cg_sb"] = 0.2f,
        };
        CustomData destination = new()
        {
            ["color"] = new List<object> { 1f, 1f, 1f },
            ["strobeColor"] = new List<object> { 1f, 1f, 1f },
            ["easing"] = 7,
            ["cg_sb"] = 0.2f,
        };
        if (authored)
        {
            destination[key] = 0;
        }

        List<Color> samples = SampleTransition(source, destination, 2, 2, false, 1f, [0.125f, 0.375f],
            sourceStrobeBrightness: 0.2f, destinationStrobeBrightness: 0.2f);
        float normal = authored && key == "colorEasing" ? 0.125f : InCubic(0.125f);
        float strobe = authored && key == "strobeColorEasing" ? 0.375f : InCubic(0.375f);
        Assert.That(samples[0].r, Is.EqualTo(normal).Within(0.0001f));
        Assert.That(samples[0].a, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(samples[1].r, Is.EqualTo(strobe).Within(0.0001f));
        Assert.That(samples[1].a, Is.EqualTo(0.2f).Within(0.0001f));
    }

    // Control: a no-strobe transition still renders the plain custom-color lerp through the native path.
    [Test]
    public void NoStrobeTransitionUsesNativeColor()
    {
        WithColorEffect((effect, tween) =>
        {
            CustomData sourceData = new() { ["color"] = new List<object> { 0f, 1f, 0f } };
            CustomData destinationData = new() { ["color"] = new List<object> { 0f, 0f, 1f } };
            object current = CreateEvent(0f, strobeFrequency: 0, fade: false, sourceData);
            object next = CreateEvent(1f, strobeFrequency: 0, fade: false, destinationData);
            nextEventField.SetValue(current, next);
            handler.Invoke(effect, [current]);

            tween.GetType().GetMethod("Sample")!.Invoke(tween, [0.5f]);
            Assert.That(capturedColor.g, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(capturedColor.b, Is.EqualTo(0.5f).Within(0.0001f));
        });
    }

    // InheritedStrobeChannelRendersDistributedNormalColor proves the whole rendered path, not just the
    // evaluator: with no authored strobeColor the strobe phase must display the normal-distributed
    // endpoint instead of flashing the undistributed authored color at each cycle.
    [Test]
    public void InheritedStrobeChannelRendersDistributedNormalColor()
    {
        WithColorEffect((effect, tween) =>
        {
            CustomData data = new() { ["color"] = new List<object> { 0.9f, 0.5f, 0.2f } };
            object context = CreateDistributionContext(["h,0.25,L,l"], null);
            object current = CreateDistributedEvent(0f, 1, false, data, context, ease: "None");
            object next = CreateEvent(1f, 0, false, customData: null, ease: "None");
            nextEventField.SetValue(current, next);
            handler.Invoke(effect, [current]);

            MethodInfo sample = tween.GetType().GetMethod("Sample")!;
            // 1 Hz across a one-second interval: phase = frac(t), so t=0.75 lands inside the strobe half-cycle.
            sample.Invoke(tween, [0.25f]);
            Color normalPhase = capturedColor;
            sample.Invoke(tween, [0.75f]);
            Color strobePhase = capturedColor;
            Color.RGBToHSV(new Color(0.9f, 0.5f, 0.2f), out float hue, out float saturation, out float value);
            Color expected = Color.HSVToRGB(Mathf.Repeat(hue + 0.25f, 1f), saturation, value, true);
            expected.a = 1f;
            Assert.That(normalPhase.r, Is.EqualTo(expected.r).Within(0.001f), "Normal phase keeps the distributed endpoint.");
            Assert.That(normalPhase.g, Is.EqualTo(expected.g).Within(0.001f));
            Assert.That(normalPhase.b, Is.EqualTo(expected.b).Within(0.001f));
            Assert.That(strobePhase.r, Is.EqualTo(expected.r).Within(0.001f), "An inherited strobe channel must render the distributed normal color, not the authored base.");
            Assert.That(strobePhase.g, Is.EqualTo(expected.g).Within(0.001f));
            Assert.That(strobePhase.b, Is.EqualTo(expected.b).Within(0.001f));
        });
    }

    // Control for the same path: an authored strobeColor owns the strobe channel alone, so normal
    // color distributions must never reach the rendered strobe phase. cg_sb=1 mirrors a serialized
    // sb=1 through the real deserializer, which V1_29_1 requires to create the legacy strobe state the
    // custom strobe render path gates on.
    [Test]
    public void ExplicitStrobeChannelIgnoresNormalDistributions()
    {
        WithColorEffect((effect, tween) =>
        {
            CustomData data = new()
            {
                ["color"] = new List<object> { 0.9f, 0.5f, 0.2f },
                ["strobeColor"] = new List<object> { 0.1f, 0.4f, 0.9f },
                ["cg_sb"] = 1f,
            };
            object context = CreateDistributionContext(["h,0.25,L,l"], null);
            object current = CreateDistributedEvent(0f, 1, false, data, context, ease: "None");
            object next = CreateEvent(1f, 0, false, customData: null, ease: "None");
            nextEventField.SetValue(current, next);
            handler.Invoke(effect, [current]);

            MethodInfo sample = tween.GetType().GetMethod("Sample")!;
            sample.Invoke(tween, [0.75f]);
            Assert.That(capturedColor.r, Is.EqualTo(0.1f).Within(0.001f), "An explicit strobeColor must render untouched by normal color distributions.");
            Assert.That(capturedColor.g, Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(capturedColor.b, Is.EqualTo(0.9f).Within(0.001f));
        });
    }

    // InheritedStrobeTrackLerpsBetweenDistributedEndpoints covers the transitioning from/to pair: the
    // strobe track must interpolate between each endpoint's own distributed normal color.
    [Test]
    public void InheritedStrobeTrackLerpsBetweenDistributedEndpoints()
    {
        WithColorEffect((effect, tween) =>
        {
            CustomData sourceData = new() { ["color"] = new List<object> { 1f, 0f, 0f } };
            CustomData destinationData = new() { ["color"] = new List<object> { 0f, 0f, 1f } };
            object current = CreateDistributedEvent(
                0f, 1, false, sourceData, CreateDistributionContext(["h,0.25,L,l"], null));
            object next = CreateDistributedEvent(
                4f, 1, false, destinationData, CreateDistributionContext(["h,0.5,L,l"], null), ease: "Linear");
            nextEventField.SetValue(current, next);
            handler.Invoke(effect, [current]);

            MethodInfo sample = tween.GetType().GetMethod("Sample")!;
            // Duration 4 at a constant 1 Hz: phase = frac(4t); t=0.625 is mid-strobe at 62.5% transition progress.
            sample.Invoke(tween, [0.625f]);
            Color strobePhase = capturedColor;
            Color from = ShiftHue(new Color(1f, 0f, 0f), 0.25f);
            Color to = ShiftHue(new Color(0f, 0f, 1f), 0.5f);
            Color expected = Color.LerpUnclamped(from, to, 0.625f);
            Assert.That(strobePhase.r, Is.EqualTo(expected.r).Within(0.001f), "The strobe track must lerp the distributed from/to endpoints, not the raw authored colors.");
            Assert.That(strobePhase.g, Is.EqualTo(expected.g).Within(0.001f));
            Assert.That(strobePhase.b, Is.EqualTo(expected.b).Within(0.001f));
        });
    }

    // OemChromaticsWithoutColorOverrideInheritStrobeChannel covers the reported bug directly: an OEM
    // (environment-scheme) color with no authored color/strobeColor must still show the
    // normal-distributed hue on the strobe phase instead of flashing the undistributed scheme color.
    [Test]
    public void OemChromaticsWithoutColorOverrideInheritStrobeChannel()
    {
        Color oemChromatic = new(0.2f, 0.6f, 1f);
        oemColorOverride = oemChromatic;
        try
        {
            WithColorEffect((effect, tween) =>
            {
                object context = CreateDistributionContext(["h,0.25,L,l"], null);
                object current = CreateDistributedEvent(0f, 1, false, new CustomData(), context, ease: "None");
                object next = CreateEvent(1f, 0, false, customData: null, ease: "None");
                nextEventField.SetValue(current, next);
                handler.Invoke(effect, [current]);

                MethodInfo sample = tween.GetType().GetMethod("Sample")!;
                sample.Invoke(tween, [0.25f]);
                Color normalPhase = capturedColor;
                sample.Invoke(tween, [0.75f]);
                Color strobePhase = capturedColor;
                Color expected = ShiftHue(oemChromatic, 0.25f);
                Assert.That(normalPhase.r, Is.EqualTo(expected.r).Within(0.001f), "Normal phase keeps the distributed OEM color.");
                Assert.That(normalPhase.g, Is.EqualTo(expected.g).Within(0.001f));
                Assert.That(normalPhase.b, Is.EqualTo(expected.b).Within(0.001f));
                Assert.That(strobePhase.r, Is.EqualTo(expected.r).Within(0.001f), "The strobe phase of an OEM-colored event must render the distributed normal color.");
                Assert.That(strobePhase.g, Is.EqualTo(expected.g).Within(0.001f));
                Assert.That(strobePhase.b, Is.EqualTo(expected.b).Within(0.001f));
            });
        }
        finally
        {
            oemColorOverride = null;
        }
    }

    // OemStrobeColorDistributionRendersShiftedOemOnStrobePhase covers the distinct strobe-only channel
    // the normal-inheritance OEM test cannot: with no authored color or strobeColor, a strobe-scoped hue
    // distribution must shift ONLY the strobe phase off the OEM base while the normal phase keeps the
    // unshifted scheme color. cg_sb=1 mirrors serialized sb=1 so the V1_29_1 deserializer creates the
    // legacy strobe state the custom strobe render path requires.
    [Test]
    public void OemStrobeColorDistributionRendersShiftedOemOnStrobePhase()
    {
        Color oemChromatic = new(0.2f, 0.6f, 1f);
        oemColorOverride = oemChromatic;
        try
        {
            WithColorEffect((effect, tween) =>
            {
                CustomData data = new() { ["cg_sb"] = 1f };
                object context = CreateDistributionContext(null, ["h,0.25,L,l"]);
                object current = CreateDistributedEvent(0f, 1, false, data, context, ease: "None");
                object next = CreateEvent(1f, 0, false, customData: null, ease: "None");
                nextEventField.SetValue(current, next);
                handler.Invoke(effect, [current]);

                MethodInfo sample = tween.GetType().GetMethod("Sample")!;
                sample.Invoke(tween, [0.25f]);
                Color normalPhase = capturedColor;
                sample.Invoke(tween, [0.75f]);
                Color strobePhase = capturedColor;
                Color expectedStrobe = ShiftHue(oemChromatic, 0.25f);
                Assert.That(normalPhase.r, Is.EqualTo(oemChromatic.r).Within(0.001f), "The normal phase has no normal distribution and must keep the OEM color.");
                Assert.That(normalPhase.g, Is.EqualTo(oemChromatic.g).Within(0.001f));
                Assert.That(normalPhase.b, Is.EqualTo(oemChromatic.b).Within(0.001f));
                Assert.That(strobePhase.r, Is.EqualTo(expectedStrobe.r).Within(0.001f), "The strobe phase must render the OEM base shifted by the strobe distribution.");
                Assert.That(strobePhase.g, Is.EqualTo(expectedStrobe.g).Within(0.001f));
                Assert.That(strobePhase.b, Is.EqualTo(expectedStrobe.b).Within(0.001f));
            });
        }
        finally
        {
            oemColorOverride = null;
        }
    }

    // OemStrobeChannelIgnoresNormalDistributionsOnceStrobeDistributionsExist locks the corrected
    // cross-channel split end-to-end through the patched handler: an event-scope normal h+.25 plus a
    // box-scope strobe h+.5 on one OEM-colored event must render +.25 on the normal phase and ONLY
    // +.5 on the strobe phase — the strobe chain restarts on the raw OEM base instead of composing
    // over the distributed normal (+.75). cg_sb=1 mirrors serialized sb=1 so the V1_29_1 path also
    // creates the legacy strobe state the custom strobe render requires.
    [Test]
    public void OemStrobeChannelIgnoresNormalDistributionsOnceStrobeDistributionsExist()
    {
        Color oemChromatic = new(0.2f, 0.6f, 1f);
        oemColorOverride = oemChromatic;
        try
        {
            WithColorEffect((effect, tween) =>
            {
                CustomData data = new() { ["cg_sb"] = 1f };
                object context = CreateDistributionContext(null, ["h,0.5,L,l"], ["h,0.25,L,l"], null);
                object current = CreateDistributedEvent(0f, 1, false, data, context, ease: "None");
                object next = CreateEvent(1f, 0, false, customData: null, ease: "None");
                nextEventField.SetValue(current, next);
                handler.Invoke(effect, [current]);

                MethodInfo sample = tween.GetType().GetMethod("Sample")!;
                sample.Invoke(tween, [0.25f]);
                Color normalPhase = capturedColor;
                sample.Invoke(tween, [0.75f]);
                Color strobePhase = capturedColor;
                Color expectedNormal = ShiftHue(oemChromatic, 0.25f);
                Color expectedStrobe = ShiftHue(oemChromatic, 0.5f);
                Assert.That(normalPhase.r, Is.EqualTo(expectedNormal.r).Within(0.001f), "The normal phase renders only the normal hue offset.");
                Assert.That(normalPhase.g, Is.EqualTo(expectedNormal.g).Within(0.001f));
                Assert.That(normalPhase.b, Is.EqualTo(expectedNormal.b).Within(0.001f));
                Assert.That(strobePhase.r, Is.EqualTo(expectedStrobe.r).Within(0.001f), "The strobe phase must shift the raw OEM base by the strobe offset alone, not compose over the normal distribution.");
                Assert.That(strobePhase.g, Is.EqualTo(expectedStrobe.g).Within(0.001f));
                Assert.That(strobePhase.b, Is.EqualTo(expectedStrobe.b).Within(0.001f));
            });
        }
        finally
        {
            oemColorOverride = null;
        }
    }

    // OemChromaticTransitionLerpsInheritedStrobeEndpoints covers the same inherited channel across a
    // source->destination transition where neither event authors color or strobeColor.
    [Test]
    public void OemChromaticTransitionLerpsInheritedStrobeEndpoints()
    {
        Color oemChromatic = new(0.2f, 0.6f, 1f);
        oemColorOverride = oemChromatic;
        try
        {
            WithColorEffect((effect, tween) =>
            {
                object current = CreateDistributedEvent(
                    0f, 1, false, new CustomData(), CreateDistributionContext(["h,0.25,L,l"], null), ease: "None");
                object next = CreateDistributedEvent(
                    4f, 1, false, new CustomData(), CreateDistributionContext(["h,0.5,L,l"], null), ease: "Linear");
                nextEventField.SetValue(current, next);
                handler.Invoke(effect, [current]);

                MethodInfo sample = tween.GetType().GetMethod("Sample")!;
                // Constant 1 Hz over a four-second interval: phase = frac(4t), so t=0.625 is mid-strobe at 62.5% progress.
                sample.Invoke(tween, [0.625f]);
                Color strobePhase = capturedColor;
                Color expected = Color.LerpUnclamped(
                    ShiftHue(oemChromatic, 0.25f),
                    ShiftHue(oemChromatic, 0.5f),
                    0.625f);
                Assert.That(strobePhase.r, Is.EqualTo(expected.r).Within(0.001f), "The OEM strobe track must lerp each endpoint's distributed normal color.");
                Assert.That(strobePhase.g, Is.EqualTo(expected.g).Within(0.001f));
                Assert.That(strobePhase.b, Is.EqualTo(expected.b).Within(0.001f));
            });
        }
        finally
        {
            oemColorOverride = null;
        }
    }

    // ShiftHue is an independent oracle: it shares only Unity's RGB<->HSV primitives with the evaluator.
    private static Color ShiftHue(Color color, float shift)
    {
        Color.RGBToHSV(color, out float hue, out float saturation, out float value);
        Color result = Color.HSVToRGB(Mathf.Repeat(hue + shift, 1f), saturation, value, true);
        result.a = color.a;
        return result;
    }

    // CreateDistributionContext composes the real production parser and context constructor; the
    // one-element filter coordinates are staged at full progress so each instruction applies its offset.
    private object CreateDistributionContext(IEnumerable<string>? normalInstructions, IEnumerable<string>? strobeInstructions) =>
        CreateDistributionContext(normalInstructions, strobeInstructions, null, null);

    // OemStrobeChannelIgnoresNormalDistributionsOnceStrobeDistributionsExist needs box and event
    // scope lists staged independently to prove a strobe list at either scope isolates the channel.
    private object CreateDistributionContext(
        IEnumerable<string>? boxNormalInstructions,
        IEnumerable<string>? boxStrobeInstructions,
        IEnumerable<string>? eventNormalInstructions,
        IEnumerable<string>? eventStrobeInstructions)
    {
        object boxNormal = instructionParser.Invoke(null, [boxNormalInstructions?.Cast<object>().ToList()])!;
        object boxStrobe = instructionParser.Invoke(null, [boxStrobeInstructions?.Cast<object>().ToList()])!;
        object eventNormal = instructionParser.Invoke(null, [eventNormalInstructions?.Cast<object>().ToList()])!;
        object eventStrobe = instructionParser.Invoke(null, [eventStrobeInstructions?.Cast<object>().ToList()])!;
        object progress = Activator.CreateInstance(progressType, 1f, 1f);
        return distributionContextConstructor.Invoke([boxNormal, boxStrobe, eventNormal, eventStrobe, progress]);
    }

    // Wrap the ordinary CJD event in the same distribution-carrying runtime type the Unpack postfix emits.
    private object CreateDistributedEvent(
        float time,
        int strobeFrequency,
        bool fade,
        CustomData customData,
        object context,
        string ease = "Linear",
        float strobeBrightness = 1f)
    {
        object inner = CreateEvent(time, strobeFrequency, fade, customData, ease, strobeBrightness: strobeBrightness);
        return distributedEventConstructor.Invoke([inner, customData, context]);
    }

    // Drive a full HandleColorChangeBeatmapEvent + FloatTween transition and record every sampled output.
    // The burn sample covers 1.29.1's deferred t=0 startup frame so interior times stay comparable.
    private List<Color> SampleTransition(
        CustomData? sourceData,
        CustomData? destinationData,
        int sourceFrequency,
        int destinationFrequency,
        bool fade,
        float duration,
        float[] sampleTimes,
        float sourceBrightness = 1f,
        float destinationBrightness = 1f,
        float sourceStrobeBrightness = 1f,
        float destinationStrobeBrightness = 1f,
        string destinationEase = "Linear")
    {
        List<Color> samples = new();
        WithColorEffect((effect, tween) =>
        {
            object current = CreateEvent(
                0f,
                sourceFrequency,
                fade,
                sourceData,
                brightness: sourceBrightness,
                strobeBrightness: sourceStrobeBrightness);
            object next = CreateEvent(
                duration,
                destinationFrequency,
                fade,
                destinationData,
                ease: destinationEase,
                brightness: destinationBrightness,
                strobeBrightness: destinationStrobeBrightness);
            nextEventField.SetValue(current, next);
            handler.Invoke(effect, [current]);
            MethodInfo sample = tween.GetType().GetMethod("Sample")!;
            sample.Invoke(tween, [0.001f]);
            foreach (float t in sampleTimes)
            {
                sample.Invoke(tween, [t]);
                samples.Add(capturedColor);
            }
        });
        return samples;
    }

    private static void AssertSameSamples(List<Color> expected, List<Color> actual, float[] times, string message)
    {
        Assert.That(actual, Has.Count.EqualTo(expected.Count));
        for (int index = 0; index < expected.Count; index++)
        {
            Assert.That(actual[index].r, Is.EqualTo(expected[index].r).Within(0.0005f), $"t={times[index]}: {message}");
            Assert.That(actual[index].g, Is.EqualTo(expected[index].g).Within(0.0005f), $"t={times[index]}: {message}");
            Assert.That(actual[index].b, Is.EqualTo(expected[index].b).Within(0.0005f), $"t={times[index]}: {message}");
            Assert.That(actual[index].a, Is.EqualTo(expected[index].a).Within(0.0005f), $"t={times[index]}: {message}");
        }
    }

    // The exact native curves the plugin resolves for save IDs 4 (InSine) and 7 (InCubic), plus the
    // native InOutCubic strobe-fade curve, so expectations stay version-independent.
    private static float InSine(float t) => 1f - Mathf.Cos(t * Mathf.PI / 2f);

    private static float InCubic(float t) => t * t * t;

    private static float InOutCubic(float t) =>
        t < 0.5f
            ? 4f * t * t * t
            : 1f - (Mathf.Pow((-2f * t) + 2f, 3f) / 2f);

    private float FromFrequency(object effect) => (float)fromFrequencyField.GetValue(effect)!;

    private float ToFrequency(object effect) => (float)toFrequencyField.GetValue(effect)!;

    private static float Duration(object tween) => (float)tween.GetType().GetProperty("duration", All)!.GetValue(tween)!;

    // Supply plain V3 custom metadata to the actual handler; no production staging or color callback is replaced.
    private object CreateEvent(
        float time,
        int strobeFrequency,
        bool fade,
        CustomData? customData,
        string ease = "Linear",
        float brightness = 1f,
        float strobeBrightness = 1f)
    {
        ParameterInfo[] parameters = eventConstructor.GetParameters();
        object?[] args = new object?[parameters.Length];
        for (int index = 0; index < parameters.Length; index++)
        {
            ParameterInfo parameter = parameters[index];
            object? value = parameter.Name switch
            {
                "time" => time,
                "brightness" => brightness,
                "easeType" => Enum.Parse(eventEaseType, ease),
                "transitionType" => Enum.Parse(eventEaseType, ease == "None" ? "Instant" : "Interpolate"),
                "strobeBeatFrequency" => strobeFrequency,
                "strobeBrightness" => strobeBrightness,
                "strobeFade" => fade,
                "customData" => customData ?? new CustomData(),
                _ => null,
            };
            if (value != null
                && parameter.ParameterType.IsValueType
                && !parameter.ParameterType.IsInstanceOfType(value))
            {
                value = Convert.ChangeType(value, parameter.ParameterType);
            }

            args[index] = value ?? (parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null);
        }

        return eventConstructor.Invoke(args);
    }

    // Replace only scene/native services; the plugin event patches, native handler, and FloatTween all execute normally.
    private void WithColorEffect(Action<object, object> run)
    {
        Harmony services = new("ChromaGLS.Tests.StrobeServices");
        Type managerType = AccessTools.TypeByName("LightWithIdManager");
        Type tweenManagerType = AccessTools.TypeByName("SongTimeTweeningManager");
        Type nativeTweenManager = AccessTools.TypeByName("Tweening.TweeningManager");
        object effect = TestHostCompat.GetUninitializedObject(effectType);
        try
        {
            services.Patch(effectType.GetMethod("GetColor", All), prefix: new HarmonyMethod(typeof(GlsStrobeTransitionTests), nameof(GetOemColor)));
            services.Patch(managerType.GetMethod("SetColorForId", All), prefix: new HarmonyMethod(typeof(GlsStrobeTransitionTests), nameof(CaptureColor)));
            services.Patch(nativeTweenManager.GetMethod("ResumeTween", All), prefix: new HarmonyMethod(typeof(GlsStrobeTransitionTests), nameof(SkipResume)));
            services.Patch(nativeTweenManager.GetMethod("KillAllTweens", All), prefix: new HarmonyMethod(typeof(GlsStrobeTransitionTests), nameof(SkipResume)));
            object lightManager = TestHostCompat.GetUninitializedObject(managerType);
            // The 1.29.1 postfix resolves legacy tube-fog state through LightWithIdManager._lights;
            // an empty registry keeps the stubbed light id out of range so that lookup is a no-op.
            FieldInfo? lights = managerType.GetField("_lights", All);
            if (lights != null)
            {
                lights.SetValue(lightManager, Array.CreateInstance(lights.FieldType.GetElementType()!, 0));
            }

            effectType.GetField("_lightManager", All)!.SetValue(effect, lightManager);
            effectType.GetField("_tweeningManager", All)!.SetValue(effect, TestHostCompat.GetUninitializedObject(tweenManagerType));
            Type bpmType = effectType.GetField("_bpmController", All)!.FieldType;
            effectType.GetField("_bpmController", All)!.SetValue(effect, TestHostCompat.CreateProxy(bpmType, typeof(BpmProxy)));
            MethodInfo setColor = effectType.GetMethod("SetColor", All)!;
            Type tweenType = AccessTools.TypeByName("Tweening.FloatTween");
            object tween = tweenType.GetConstructors().Single(ctor => ctor.GetParameters().Length == 6)
                .Invoke([0f, 1f, setColor.CreateDelegate(typeof(Action<float>), effect), 1f, Enum.Parse(tweenEaseType, "Linear"), 0f]);
            effectType.GetField("_floatTween", All)!.SetValue(effect, tween);
            run(effect, tween);
        }
        finally
        {
            // Native Cleanup would enter Unity's tween manager without the service stub; the real production postfix is exercised too.
            try
            {
                effectType.GetMethod("Cleanup", All)!.Invoke(effect, null);
            }
            finally
            {
                services.UnpatchSelf();
            }
        }
    }

    private static Color capturedColor;

    // OemChromaticsWithoutColorOverrideInheritStrobeChannel needs a chromatic scheme color, so the
    // stub scales an optional override instead of a hard-coded gray; the default stays byte-identical.
    private static Color? oemColorOverride;

    private static bool GetOemColor(float brightness, ref Color __result)
    {
        Color rgb = oemColorOverride ?? Color.white;
        __result = new Color(rgb.r * brightness, rgb.g * brightness, rgb.b * brightness, brightness);
        return false;
    }

    private static bool CaptureColor(Color __1)
    {
        capturedColor = __1;
        return false;
    }

    private static bool SkipResume() => false;

    public class BpmProxy : DispatchProxy
    {
        // oneBeatDuration is a direct property on modern runtimes; 1.29.1 derives it from currentBpm,
        // so 60 BPM yields the same one-second beat length on every target.
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                "get_oneBeatDuration" => 1f,
                "get_currentBpm" => 60f,
                _ => targetMethod?.ReturnType is { IsValueType: true } returnType
                    ? Activator.CreateInstance(returnType)
                    : null,
            };
        }
    }
}
