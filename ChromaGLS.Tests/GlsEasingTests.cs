using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CustomJSONData.CustomBeatmap;
using HarmonyLib;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

// Exercise the built plugin's real lifecycle and the selected game's converter/tween, not a copy of the mapping.
[TestFixture]
[NonParallelizable]
internal sealed class GlsEasingTests
{
    private const string HarmonyId = "com.eklipz.ChromaGLS";
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    // Delay detour initialization so a non-Mono host can be reported as a skip instead of crashing the runner.
    private Harmony harmony = null!;
    private MethodInfo convert = null!;
    private MethodInfo setEnabled = null!;
    private Type pluginType = null!;
    private bool legacyStrobe;

    // Native names and numeric save IDs deliberately come from the file format, not the production patch.
    // Shared numeric expectations also drive the no-detour legacy runtime tests, whose old MonoMod only works under Unity's Mono.
    internal static readonly object[] Curves =
    [
        new object[] { 4, "InSine", 0.0761204675, 0.6173165676 },
        new object[] { 5, "OutSine", 0.3826834324, 0.9238795325 },
        new object[] { 6, "InOutSine", 0.1464466094, 0.8535533906 },
        new object[] { 7, "InCubic", 0.015625, 0.421875 },
        new object[] { 8, "OutCubic", 0.578125, 0.984375 },
        new object[] { 9, "InOutCubic", 0.0625, 0.9375 },
        new object[] { 10, "InQuart", 0.00390625, 0.31640625 },
        new object[] { 11, "OutQuart", 0.68359375, 0.99609375 },
        new object[] { 12, "InOutQuart", 0.03125, 0.96875 },
        new object[] { 13, "InQuint", 0.0009765625, 0.2373046875 },
        new object[] { 14, "OutQuint", 0.7626953125, 0.9990234375 },
        new object[] { 15, "InOutQuint", 0.015625, 0.984375 },
        new object[] { 16, "InExpo", 0.0055242717, 0.1767766953 },
        new object[] { 17, "OutExpo", 0.8232233047, 0.9944757283 },
        new object[] { 18, "InOutExpo", 0.015625, 0.984375 },
    ];

    // ColorEasingHotPathUsesOnePerEffectStateCache prevents each active GLS light from performing one hash lookup per independently authored easing track every frame.
    [Test]
    public void ColorEasingHotPathUsesOnePerEffectStateCache()
    {
        Type chromafier = Assembly.Load("ChromaGLS").GetType("ChromaGLS.HarmonyPatches.GlsColorChromafier", true)!;
        string[] splitCaches =
        [
            "ColorEasings",
            "ColorEasingOverrides",
            "StrobeColorEasings",
            "StrobeFadeEasings",
            "HsvTransitions",
        ];
        foreach (string cache in splitCaches)
        {
            Assert.That(chromafier.GetField(cache, All), Is.Null,
                cache + " would add another hash lookup to the per-light, per-frame color path.");
        }

        FieldInfo? states = chromafier.GetField("ColorEasingStates", All);
        Assert.That(states, Is.Not.Null, "All staged easing delegates and HSV state must share one per-effect cache.");
    }

    [SetUp]
    public void SetUp()
    {
        // The game's MonoMod detours only run on Unity Mono; other hosts crash in PrepareMethod or
        // cannot load CustomJSONData's default interface members. GlsEasingNativeTests still exercises
        // the selected game's production mapping and actual tween without detouring.
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
        Type loader = AccessTools.TypeByName("BeatmapTypeConverters") ?? AccessTools.TypeByName("BeatmapDataLoader");
        convert = loader.GetMethod("ConvertEaseType", All)!;
        Assert.That(convert, Is.Not.Null, "The selected runtime must expose its actual save-to-runtime easing converter.");
        // 1.29.1 events lack strobeBrightness/strobeFade fields; the legacy prefix backports them via cg_sb/cg_sf
        // customData keys and uses the inclusive phase >= 0.5 strobe boundary with a deferred first faded frame.
        legacyStrobe = typeof(CustomData).Assembly
            .GetType("CustomJSONData.CustomBeatmap.CustomLightColorBeatmapEventData", true)!
            .GetConstructors().Single().GetParameters().All(parameter => parameter.Name != "strobeBrightness");
    }

    [TearDown]
    public void TearDown()
    {
        setEnabled?.Invoke(null, [false]);
        // An unsupported host skips before Harmony initialization; retain cleanup for every successfully installed test patch.
        harmony?.UnpatchSelf();
        pluginType?.GetField("_instance", All)!.SetValue(null, null);
    }

    // Regression: modern builds omitted the patch entirely; legacy builds substituted quadratic curves.
    [TestCaseSource(nameof(Curves))]
    public void PluginEnablePreservesExactCurveAndNativeTween(int saveId, string runtimeName, double quarter, double threeQuarters)
    {
        object actual = Convert(saveId);
        Assert.That(actual.ToString(), Is.EqualTo(runtimeName), $"Save easing {saveId} must reach the real {runtimeName} curve, never None/Quad.");
        Assert.That(Harmony.GetPatchInfo(convert)?.Owners, Does.Contain(HarmonyId), "The production enable path must install the converter patch.");
        AssertTween(actual, 0f, 0d);
        AssertTween(actual, 0.25f, quarter);
        AssertTween(actual, 0.75f, threeQuarters);
        AssertTween(actual, 1f, 1d);
    }

    // Establish that every supported installation already contains the true curves before asking conversion to preserve them.
    [TestCaseSource(nameof(Curves))]
    public void NativeTweenAlreadyImplementsRequestedCurve(int saveId, string runtimeName, double quarter, double threeQuarters)
    {
        object ease = Enum.Parse(convert.ReturnType, runtimeName);
        AssertTween(ease, 0f, 0d);
        AssertTween(ease, 0.25f, quarter);
        AssertTween(ease, 0.75f, threeQuarters);
        AssertTween(ease, 1f, 1d);
    }

    // Compare against the unpatched game to retain all standard/BS variants and unknown-value behavior on each version.
    [Test]
    public void ExistingAndUnknownEasingsKeepNativeConversion()
    {
        int[] ids = [-1, 0, 1, 2, 3, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 100, 101, 102, -2, 31, 99, 103, int.MaxValue];
        // LegacyRecognizedFallbacksRemainCompatible intentionally remaps 19-30 and 100-102 on 1.29.1,
        // whose native converter only recognizes None/Linear/Quad; parity there is asserted for the other IDs.
        bool legacyShim = convert.ReturnType.Assembly.GetName().Name == "HMLib";
        if (legacyShim)
        {
            ids = ids.Where(id => id is < 19 or > 30).Where(id => id is < 100 or > 102).ToArray();
        }

        setEnabled.Invoke(null, [false]);
        string[] native = ids.Select(id => Convert(id).ToString()!).ToArray();
        setEnabled.Invoke(null, [true]);
        for (int index = 0; index < ids.Length; index++)
        {
            Assert.That(Convert(ids[index]).ToString(), Is.EqualTo(native[index]), $"Existing/unknown save easing {ids[index]} must retain this game's native conversion.");
        }
    }

    // Traverse real CJD color parsing so box easing is not confused with V3 node i (transition mode).
    [TestCaseSource(nameof(Curves))]
    public void V3ColorDistributionSurvivesDeserializer(int saveId, string runtimeName, double quarter, double threeQuarters)
    {
        Type patch = Assembly.Load("ChromaGLS").GetType("ChromaGLS.HarmonyPatches.GlsDeserializePatch", true)!;
        MethodInfo prefix = patch.GetMethod("Prefix", All)!;
        Type saveList = prefix.GetParameters()[1].ParameterType;
        Type cjd = AccessTools.TypeByName("CustomJSONData.CustomBeatmap.Version3CustomBeatmapSaveData")
            ?? AccessTools.TypeByName("CustomJSONData.CustomBeatmap.CustomBeatmapSaveData");
        MethodInfo deserialize = cjd.GetMethod("DeserializeLightColorEventBoxGroupArray", All)!;
        IList groups = (IList)Activator.CreateInstance(saveList)!;
        string json = "[{\"b\":0,\"g\":1,\"e\":[{\"f\":{\"f\":1,\"p\":0,\"t\":0},\"i\":" + saveId + ",\"r\":1,\"t\":1,\"e\":[{\"b\":0,\"i\":1,\"c\":0,\"s\":1}]}]}]";
        using JsonTextReader reader = new(new StringReader(json));
        deserialize.Invoke(null, [reader, groups]);
        object group = groups[0]!;
        object box = ((IEnumerable)group.GetType().GetProperty("eventBoxes")!.GetValue(group)!).Cast<object>().Single();
        object saveEase = box.GetType().GetProperty("brightnessDistributionEaseType")!.GetValue(box)!;
        Assert.That(System.Convert.ToInt32(saveEase), Is.EqualTo(saveId), "CJD must retain the serialized box easing ID.");
        object runtimeEase = convert.Invoke(null, [saveEase])!;
        Assert.That(runtimeEase.ToString(), Is.EqualTo(runtimeName), "V3 Color brightness distribution must use the actual curve.");
        AssertTween(runtimeEase, 0.25f, quarter);
    }

    // Verify each actual GLS loader boundary calls the patched converter; no standalone helper can satisfy this test.
    [Test]
    public void AllGlsConverterPathsUsePatchedEasingBoundary()
    {
        Type loader = AccessTools.TypeByName("BeatmapDataLoaderVersion3.BeatmapDataLoader") ?? AccessTools.TypeByName("BeatmapDataLoader");
        List<MethodInfo> callers = loader.GetNestedTypes(All)
            .SelectMany(type => type.GetMethods(All | BindingFlags.DeclaredOnly))
            .Where(CallsConvert).ToList();
        foreach (string family in new[] { "LightColorEventBox", "LightRotationEventBox", "LightRotationBaseData", "LightTranslationEventBox", "LightTranslationBaseData", "FloatVfxEventBox", "FloatVfxBaseData" })
        {
            // FloatFX was added after 1.29.1 and has no native GLS runtime type on that version.
            if (family.StartsWith("FloatVfx", StringComparison.Ordinal) && AccessTools.TypeByName("FloatFxBaseData") == null)
            {
                TestContext.WriteLine("FloatFX is absent from this game's native runtime.");
                continue;
            }
            Assert.That(callers.Any(method => method.DeclaringType!.Name.StartsWith(family, StringComparison.Ordinal)), Is.True, $"V3 {family} must route easing through the patched boundary.");
        }

        Type? v4 = AccessTools.TypeByName("BeatmapDataLoaderVersion4.LightColorBaseDataConverter");
        if (v4 == null)
        {
            TestContext.WriteLine("Native V4 loader is absent; BeatToTheFuture owns V4-to-V3 conversion on this game.");
            return;
        }
        foreach (string family in new[] { "LightColor", "LightRotation", "LightTranslation", "FloatFx" })
        {
            Type box = AccessTools.TypeByName($"BeatmapDataLoaderVersion4.{family}EventBoxGroupConverter");
            Assert.That(box.GetMethods(All | BindingFlags.DeclaredOnly).Any(CallsConvert), Is.True, $"V4 {family} box distribution must use the patched boundary.");
        }
    }

    // V4 stores event easing in e on all four node types; invoke the actual native converters after JSON serialization round-trip.
    [TestCaseSource(nameof(Curves))]
    public void V4EventSerializationAndConversionRetainCurves(int saveId, string runtimeName, double quarter, double threeQuarters)
    {
        if (AccessTools.TypeByName("BeatmapDataLoaderVersion4.LightColorBaseDataConverter") == null)
        {
            Assert.Ignore("This game has no native V4 loader; BeatToTheFuture owns the V4-to-V3 handoff.");
        }
        foreach (string family in new[] { "LightColor", "LightRotation", "LightTranslation", "FloatFx" })
        {
            // Newer games moved rotation's V4 conversion into the BeatmapBoxConverterNoConvert instance helper;
            // 1.37.1 still exposes the uniform static LightRotationBaseDataConverter.Convert signature.
            Type? converter = AccessTools.TypeByName($"BeatmapDataLoaderVersion4.{family}BaseDataConverter");
            MethodInfo? method = converter?.GetMethods(All).SingleOrDefault(info => info.Name == "Convert" && info.GetParameters().Length == 2);
            if (method == null && family == "LightRotation")
            {
                converter = AccessTools.TypeByName("BeatmapBoxConverterNoConvert");
                method = converter?.GetMethod("ConvertRotationEvent", All);
            }
            if (converter == null || method == null)
            {
                Assert.Ignore($"This runtime has no {family} V4 converter; BeatToTheFuture owns the V4-to-V3 handoff.");
            }
            Type saveType = method.GetParameters()[1].ParameterType;
            object save = JsonConvert.DeserializeObject("{\"e\":" + saveId + "}", saveType)!;
            string json = JsonConvert.SerializeObject(save);
            save = JsonConvert.DeserializeObject(json, saveType)!;
            object?[] args = method.GetParameters().Length == 3
                ? [0f, save, Activator.CreateInstance(method.GetParameters()[2].ParameterType)]
                : [0f, save];
            object runtime = method.Invoke(method.IsStatic ? null : Activator.CreateInstance(converter), args)!;
            object ease = runtime.GetType().GetField("easeType", All)?.GetValue(runtime)
                ?? runtime.GetType().GetProperty("easeType", All)!.GetValue(runtime)!;
            Assert.That(ease.ToString(), Is.EqualTo(runtimeName), $"V4 {family} must not discard easing during loading.");
            AssertTween(ease, 0.25f, quarter);
        }
    }

    // Native V4 color nodes carry easing, but SetColor currently interpolates linearly even after a correct conversion.
    [TestCaseSource(nameof(Curves))]
    public void ColorEventEasesRgbaWithoutWarpingStrobeClock(int saveId, string runtimeName, double quarter, double threeQuarters)
    {
        Type eventType = AccessTools.TypeByName("LightColorBeatmapEventData");
        if (eventType.GetProperty("easeType") == null && eventType.GetField("easeType", All) == null)
        {
            Assert.Ignore("This runtime's color nodes expose transition mode only; their box distributions are covered separately.");
        }

        // Share the real effect/tween setup with the final-frame regression rather than constructing a second simulated color path.
        WithColorEffect((effect, tween) =>
        {
            Type effectType = effect.GetType();
            Type tweenType = tween.GetType();
            object ease = Enum.Parse(convert.ReturnType, runtimeName);
            MethodInfo handler = effectType.GetMethod("HandleColorChangeBeatmapEvent", All)!;
            ConstructorInfo eventConstructor = eventType.GetConstructors().Single();
            object CreateEvent(float time, float brightness, int strobe) => eventConstructor.Invoke(eventConstructor.GetParameters().Select(parameter => parameter.Name switch
            {
                "time" => (object)time,
                "easeType" => ease,
                "brightness" => brightness,
                "strobeBrightness" => 0.2f,
                "strobeBeatFrequency" => strobe,
                // Custom endpoints deliberately match OEM normal RGB but use an independent, visibly different strobe track.
                "customData" => new CustomData
                {
                    ["color"] = new List<object> { brightness, brightness, brightness, 1f },
                    ["strobeColor"] = new List<object> { 0.8f, 0.4f, 0.2f, 1f },
                    // V3 uses numeric save IDs in customData, independently of the runtime enum stored on native V4 nodes.
                    ["easing"] = saveId,
                },
                _ => Activator.CreateInstance(parameter.ParameterType),
            }).ToArray());

            // Both OEM and CJD custom colors must follow the same curve, including an independently colored strobe half-cycle.
            foreach (bool custom in new[] { false, true })
            {
                // The V3 branch must consume customData.easing, not accidentally pass by reusing native V4 easing metadata.
                ease = Enum.Parse(convert.ReturnType, custom ? "Linear" : runtimeName);
                Type selectedEventType = custom
                    ? typeof(CustomData).Assembly.GetType("CustomJSONData.CustomBeatmap.CustomLightColorBeatmapEventData", true)!
                    : eventType;
                eventConstructor = selectedEventType.GetConstructors().Single();
                foreach (int frequency in new[] { 0, 3 })
                {
                    object current = CreateEvent(0f, 0f, frequency);
                    object next = CreateEvent(1f, 1f, frequency);
                    eventType.BaseType!.GetField("<nextSameTypeEventData>k__BackingField", All)!.SetValue(current, next);
                    handler.Invoke(effect, [current]);
                    tweenType.GetMethod("Sample")!.Invoke(tween, [0.25f]);
                    double expectedRed = custom && frequency > 0 ? 0.8d : quarter;
                    Assert.That(capturedColor.r, Is.EqualTo(expectedRed).Within(0.000002d), "The actual color output must use the upcoming node's curve, not linear t.");
                    // At t=.25 and 3 Hz, phase=.75 is the strobe half regardless of the easing curve.
                    Assert.That(capturedColor.a, Is.EqualTo(frequency == 0 ? quarter : 0.2d).Within(0.000002d), "Color easing must not warp elapsed time or the native strobe phase.");
                    tweenType.GetMethod("Sample")!.Invoke(tween, [0.75f]);
                    Assert.That(capturedColor.r, Is.EqualTo(threeQuarters).Within(0.000002d));
                }
            }

            // A following ordinary interval must replace, not inherit, the previous eased interval's cached curve.
            ease = Enum.Parse(convert.ReturnType, "Linear");
            object linearCurrent = CreateEvent(0f, 0f, 0);
            object linearNext = CreateEvent(1f, 1f, 0);
            // None/Linear have no serialized easing override; the next interval must clear the cached custom curve.
            ((ICustomData)linearCurrent).customData.TryRemove("easing", out _);
            ((ICustomData)linearNext).customData.TryRemove("easing", out _);
            eventType.BaseType!.GetField("<nextSameTypeEventData>k__BackingField", All)!.SetValue(linearCurrent, linearNext);
            handler.Invoke(effect, [linearCurrent]);
            tweenType.GetMethod("Sample")!.Invoke(tween, [0.25f]);
            Assert.That(capturedColor.r, Is.EqualTo(0.25f).Within(0.000002f));
        });
    }

    // Native Tween.Update intentionally reaches progress > 1; only the curve input should be bounded, not its intentional overshooting output.
    [TestCase(19, 1f)]
    [TestCase(20, 1f)]
    [TestCase(16, 1f)]
    [TestCase(100, 1.0000017f)]
    [TestCase(101, 1.0033829f)]
    public void ColorCurveInputIsClampedAtNativeTweenBoundaries(int saveId, float expectedEnd)
    {
        WithColorEffect((effect, tween) =>
        {
            BeginColorInterval(effect, saveId);
            // This is the native update path, not a duplicate of its timing formula or a mocked progress value.
            tween.GetType().GetMethod("Update")!.Invoke(tween, [2f]);
            Assert.That((float)tween.GetType().GetProperty("progress")!.GetValue(tween)!, Is.GreaterThan(1f), "Keep native tween overshoot intact; clamp only the easing input.");
            AssertCapturedRgba(expectedEnd, "The actual final native Update must render a finite, stable eased endpoint.");

            MethodInfo sample = tween.GetType().GetMethod("Sample")!;
            sample.Invoke(tween, [1.001f]);
            AssertCapturedRgba(expectedEnd, "Raw t=1.001 must sample the curve's endpoint, not produce NaN or extrapolated RGBA.");
            sample.Invoke(tween, [-0.25f]);
            AssertCapturedRgba(0f, "Negative raw progress must sample the curve's start.");
        });
    }

    // At a 1/6-second interval and 3 Hz, raw t=1.001 passes the half-cycle boundary; clamping the clock would incorrectly show the normal alpha.
    [Test]
    public void ColorCurveInputClampingLeavesStrobeClockRaw()
    {
        WithColorEffect((effect, tween) =>
        {
            BeginColorInterval(effect, 19, 1f / 6f, 3);
            tween.GetType().GetMethod("Sample")!.Invoke(tween, [1.001f]);
            Assert.That(capturedColor.a, Is.EqualTo(0.2f).Within(0.000001f), "Strobe phase must still use the original overshooting elapsed time.");
            Assert.That(capturedColor.r, Is.EqualTo(1f).Within(0.000001f), "Only the color curve's input is clamped.");
        });
    }

    // customData.colorEasing must ease only the normal RGB track: brightness and strobe output keep interval progress.
    [Test]
    public void ColorEasingEasesOnlyTheNormalColorTrack()
    {
        WithColorEffect((effect, tween) =>
        {
            MethodInfo handler = effect.GetType().GetMethod("HandleColorChangeBeatmapEvent", All)!;
            MethodInfo sample = tween.GetType().GetMethod("Sample")!;
            object current = CreateTrackEvent(0f, 0f, strobe: 2, fade: false, null);
            object next = CreateTrackEvent(1f, 1f, strobe: 2, fade: false, new CustomData { ["colorEasing"] = 4 });
            AccessTools.TypeByName("BeatmapEventData").GetField("<nextSameTypeEventData>k__BackingField", All)!.SetValue(current, next);
            handler.Invoke(effect, [current]);

            // At 2 Hz, t=0.25 lands exactly on the half-cycle boundary. Modern runtimes keep the strict native
            // boundary for non-custom strobes (normal side); the 1.29.1 backport boundary is inclusive (strobe side).
            sample.Invoke(tween, [0.25f]);
            if (legacyStrobe)
            {
                Assert.That(capturedColor.r, Is.EqualTo(0.25d).Within(0.000002d),
                    "The inclusive legacy boundary selects the strobe track's linear lerp at phase 0.5.");
                Assert.That(capturedColor.a, Is.EqualTo(0.2d).Within(0.000002d));
            }
            else
            {
                Assert.That(capturedColor.r, Is.EqualTo(0.0761204675d).Within(0.000002d),
                    "colorEasing must drive the normal color track (InSine), not the interval's Linear easing.");
                Assert.That(capturedColor.a, Is.EqualTo(0.25d).Within(0.000002d),
                    "The brightness/alpha track must keep the interval's own Linear progress.");
            }

            // t=0.375 is the strobe half; the strobe RGB track must remain on interval progress without strobeColorEasing.
            sample.Invoke(tween, [0.375f]);
            Assert.That(capturedColor.r, Is.EqualTo(0.375d).Within(0.000002d),
                "colorEasing must not leak into the strobe color track.");
            Assert.That(capturedColor.a, Is.EqualTo(0.2d).Within(0.000002d));
        });
    }

    // customData.strobeColorEasing must ease only the strobe RGB track while normal color and alpha stay linear.
    [Test]
    public void StrobeColorEasingEasesOnlyTheStrobeColorTrack()
    {
        WithColorEffect((effect, tween) =>
        {
            MethodInfo handler = effect.GetType().GetMethod("HandleColorChangeBeatmapEvent", All)!;
            MethodInfo sample = tween.GetType().GetMethod("Sample")!;
            object current = CreateTrackEvent(0f, 0f, strobe: 2, fade: false,
                new CustomData { ["strobeColor"] = new List<object> { 0f, 0f, 0f, 1f } });
            object next = CreateTrackEvent(1f, 1f, strobe: 2, fade: false,
                new CustomData
                {
                    ["strobeColor"] = new List<object> { 0.8f, 0.4f, 0.2f, 1f },
                    ["strobeColorEasing"] = 7,
                });
            AccessTools.TypeByName("BeatmapEventData").GetField("<nextSameTypeEventData>k__BackingField", All)!.SetValue(current, next);
            handler.Invoke(effect, [current]);

            // t=0.375 is the strobe half; InCubic(0.375)=0.052734 must drive only the strobe RGB lerp.
            sample.Invoke(tween, [0.375f]);
            Assert.That(capturedColor.r, Is.EqualTo(0.8d * 0.052734d).Within(0.000002d),
                "strobeColorEasing must ease the strobe color track (InCubic), not interval Linear.");
            Assert.That(capturedColor.a, Is.EqualTo(0.2d).Within(0.000002d));

            // The normal half must stay on the interval's Linear progress. With a custom strobe track the
            // boundary is inclusive (phase >= 0.5), so t=0.125 keeps the phase at 0.25 inside the normal half.
            sample.Invoke(tween, [0.125f]);
            Assert.That(capturedColor.r, Is.EqualTo(0.125d).Within(0.000002d));
            Assert.That(capturedColor.a, Is.EqualTo(0.125d).Within(0.000002d));
        });
    }

    // customData.strobeEasing replaces only the native InOutCubic fade curve; omitting it on the next interval restores OEM.
    [Test]
    public void StrobeEasingReplacesTheNativeFadeCurve()
    {
        WithColorEffect((effect, tween) =>
        {
            MethodInfo handler = effect.GetType().GetMethod("HandleColorChangeBeatmapEvent", All)!;
            MethodInfo sample = tween.GetType().GetMethod("Sample")!;
            CustomData strobes = new() { ["strobeColor"] = new List<object> { 0.6f, 0.6f, 0.6f, 1f } };
            object current = CreateTrackEvent(0f, 0f, strobe: 2, fade: true,
                new CustomData
                {
                    ["strobeColor"] = new List<object> { 0.6f, 0.6f, 0.6f, 1f },
                    ["strobeEasing"] = 22,
                });
            object next = CreateTrackEvent(1f, 1f, strobe: 2, fade: true, strobes);
            AccessTools.TypeByName("BeatmapEventData").GetField("<nextSameTypeEventData>k__BackingField", All)!.SetValue(current, next);
            handler.Invoke(effect, [current]);

            // The 1.29.1 path intentionally renders one deferred t=0 frame before live samples.
            sample.Invoke(tween, [0.001f]);
            // t=0.125 at 2 Hz puts the raw strobe phase at 0.25; the fade input 1-|2p-1| is 0.5.
            // InBack(0.5)=-0.0876975 vs native InOutCubic(0.5)=0.5, so the fade overshoots outward.
            sample.Invoke(tween, [0.125f]);
            Assert.That(capturedColor.r, Is.EqualTo(0.125d - (0.0876975d * 0.475d)).Within(0.000002d),
                "strobeEasing must replace the native InOutCubic fade curve (InBack here).");
            Assert.That(capturedColor.a, Is.EqualTo(0.125d - (0.0876975d * 0.075d)).Within(0.000002d));

            // A following interval without strobeEasing must restore the native InOutCubic fade.
            object plainCurrent = CreateTrackEvent(0f, 0f, strobe: 2, fade: true, strobes);
            object plainNext = CreateTrackEvent(1f, 1f, strobe: 2, fade: true, strobes);
            AccessTools.TypeByName("BeatmapEventData").GetField("<nextSameTypeEventData>k__BackingField", All)!.SetValue(plainCurrent, plainNext);
            handler.Invoke(effect, [plainCurrent]);
            sample.Invoke(tween, [0.125f]);
            Assert.That(capturedColor.r, Is.EqualTo(0.125d + (0.5d * 0.475d)).Within(0.000002d),
                "The next interval without strobeEasing must restore the native InOutCubic fade.");
            Assert.That(capturedColor.a, Is.EqualTo(0.125d + (0.5d * 0.075d)).Within(0.000002d));
        });
    }

    // Construct a real CustomJSONData light color event so production handlers read authored metadata verbatim.
    private object CreateTrackEvent(float time, float brightness, int strobe, bool fade, CustomData? customData)
    {
        Type customType = typeof(CustomData).Assembly.GetType("CustomJSONData.CustomBeatmap.CustomLightColorBeatmapEventData", true)!;
        ConstructorInfo constructor = customType.GetConstructors().Single();
        CustomData data = customData ?? new CustomData();
        // 1.29.1 backports strobe brightness/fade through customData keys; modern runtimes ignore them.
        data["cg_sb"] = 0.2f;
        data["cg_sf"] = fade;
        return constructor.Invoke(constructor.GetParameters().Select(parameter => parameter.Name switch
        {
            "time" => (object)time,
            "brightness" => brightness,
            "easeType" => Enum.Parse(convert.ReturnType, "Linear"),
            "transitionType" => Enum.Parse(parameter.ParameterType, "Interpolate"),
            "strobeBeatFrequency" => strobe,
            "strobeBrightness" => 0.2f,
            "strobeFade" => fade,
            "customData" => data,
            _ => Activator.CreateInstance(parameter.ParameterType),
        }).ToArray());
    }

    // Invoke native Cleanup with its production Harmony postfix, then probe the same callback to prove cache removal and unchanged raw fallback behavior.
    [Test]
    public void ColorEffectCleanupClearsEasingAndRetainsRawFallback()
    {
        WithColorEffect((effect, tween) =>
        {
            BeginColorInterval(effect, 19);
            MethodInfo sample = tween.GetType().GetMethod("Sample")!;
            sample.Invoke(tween, [0.25f]);
            Assert.That(capturedColor.r, Is.EqualTo(1d - Math.Sqrt(1d - (0.25d * 0.25d))).Within(0.000001d));
            effect.GetType().GetMethod("Cleanup", All)!.Invoke(effect, null);
            sample.Invoke(tween, [1.001f]);
            AssertCapturedRgba(1.001f, "Disposed effects must not retain cached easing or clamp the native fallback.");
            sample.Invoke(tween, [-0.25f]);
            AssertCapturedRgba(-0.25f, "The no-easing fallback must retain negative raw progress as well.");
        });
    }

    // Replace only scene/native services; the plugin event patches, native handler and FloatTween all execute normally.
    private void WithColorEffect(Action<object, object> run)
    {
        Harmony services = new("ChromaGLS.Tests.EasingServices");
        Type effectType = AccessTools.TypeByName("LightColorGroupEffect");
        Type managerType = AccessTools.TypeByName("LightWithIdManager");
        Type tweenManagerType = AccessTools.TypeByName("SongTimeTweeningManager");
        Type nativeTweenManager = AccessTools.TypeByName("Tweening.TweeningManager");
        object effect = TestHostCompat.GetUninitializedObject(effectType);
        try
        {
            // Install scene-service interception within the cleanup scope so a failed fixture cannot leak test patches.
            services.Patch(effectType.GetMethod("GetColor", All), prefix: new HarmonyMethod(typeof(GlsEasingTests), nameof(GetOemColor)));
            services.Patch(managerType.GetMethod("SetColorForId", All), prefix: new HarmonyMethod(typeof(GlsEasingTests), nameof(CaptureColor)));
            services.Patch(nativeTweenManager.GetMethod("ResumeTween", All), prefix: new HarmonyMethod(typeof(GlsEasingTests), nameof(SkipResume)));
            services.Patch(nativeTweenManager.GetMethod("KillAllTweens", All), prefix: new HarmonyMethod(typeof(GlsEasingTests), nameof(SkipResume)));
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
                .Invoke([0f, 1f, setColor.CreateDelegate(typeof(Action<float>), effect), 1f, Enum.Parse(convert.ReturnType, "Linear"), 0f]);
            effectType.GetField("_floatTween", All)!.SetValue(effect, tween);
            run(effect, tween);
        }
        finally
        {
            // Native Cleanup would enter Unity's tween manager without the service stub; now its real production postfix is exercised too.
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

    // Supply plain V3 custom metadata to the actual handler; no production easing evaluator or color callback is replaced by the test.
    private void BeginColorInterval(object effect, int saveId, float duration = 1f, int strobeFrequency = 0)
    {
        Type customType = typeof(CustomData).Assembly.GetType("CustomJSONData.CustomBeatmap.CustomLightColorBeatmapEventData", true)!;
        ConstructorInfo constructor = customType.GetConstructors().Single();
        object CreateEvent(float time, float brightness) => constructor.Invoke(constructor.GetParameters().Select(parameter => parameter.Name switch
        {
            "time" => (object)time,
            "brightness" => brightness,
            "easeType" => Enum.Parse(convert.ReturnType, "Linear"),
            "transitionType" => Enum.Parse(parameter.ParameterType, "Interpolate"),
            "strobeBeatFrequency" => strobeFrequency,
            "strobeBrightness" => 0.2f,
            // cg_sb/cg_sf carry the strobe brightness/hard-strobe shape on 1.29.1; modern runtimes ignore them.
            "customData" => new CustomData { ["easing"] = saveId, ["cg_sb"] = 0.2f, ["cg_sf"] = false },
            _ => Activator.CreateInstance(parameter.ParameterType),
        }).ToArray());
        object current = CreateEvent(0f, 0f);
        object next = CreateEvent(duration, 1f);
        AccessTools.TypeByName("BeatmapEventData").GetField("<nextSameTypeEventData>k__BackingField", All)!.SetValue(current, next);
        effect.GetType().GetMethod("HandleColorChangeBeatmapEvent", All)!.Invoke(effect, [current]);
    }

    private static void AssertCapturedRgba(float expected, string message)
    {
        Assert.That(capturedColor.r, Is.EqualTo(expected).Within(0.000001f), message + " (red)");
        Assert.That(capturedColor.g, Is.EqualTo(expected).Within(0.000001f), message + " (green)");
        Assert.That(capturedColor.b, Is.EqualTo(expected).Within(0.000001f), message + " (blue)");
        Assert.That(capturedColor.a, Is.EqualTo(expected).Within(0.000001f), message + " (alpha)");
    }

    private static Color capturedColor;

    private static bool GetOemColor(float brightness, ref Color __result)
    {
        __result = new Color(brightness, brightness, brightness, brightness);
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

    private bool CallsConvert(MethodInfo method)
    {
        return method.GetMethodBody() != null && PatchProcessor.GetOriginalInstructions(method)
            .Any(instruction => instruction.operand is MethodInfo called && called == convert);
    }

    private object Convert(int saveId)
    {
        return convert.Invoke(null, [Enum.ToObject(convert.GetParameters()[0].ParameterType, saveId)])!;
    }

    private static void AssertTween(object ease, float progress, double expected)
    {
        Type tweenType = AccessTools.TypeByName("Tweening.FloatTween");
        ConstructorInfo constructor = tweenType.GetConstructors().Single(ctor => ctor.GetParameters().Length == 6);
        float sample = float.NaN;
        object tween = constructor.Invoke([0f, 1f, new Action<float>(value => sample = value), 1f, ease, 0f]);
        tweenType.GetMethod("Sample", All)!.Invoke(tween, [progress]);
        Assert.That(sample, Is.EqualTo(expected).Within(0.000002d), $"Native FloatTween must dispatch {ease} at t={progress}, not a quadratic/linear approximation.");
    }
}
