using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace ChromaGLS.Tests;

// Old game MonoMod versions fatally abort net8 during detour initialization. These tests invoke the production
// conversion patch directly, then sample actual game tweens; GlsEasingTests separately verifies Harmony activation.
[TestFixture]
[NonParallelizable]
internal sealed class GlsEasingNativeTests
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private MethodInfo converter = null!;
    private Type? patch;
    private Type tweenType = null!;

    [SetUp]
    public void SetUp()
    {
        Assembly plugin = Assembly.Load("ChromaGLS");
        foreach (AssemblyName reference in plugin.GetReferencedAssemblies().Where(reference => reference.Name is "Main" or "BeatmapCore" or "DataModels" or "Tweening"))
        {
            Assembly.Load(reference);
        }
        Type loader = FindType("BeatmapTypeConverters") ?? FindType("BeatmapDataLoader")!;
        converter = loader.GetMethod("ConvertEaseType", All)!;
        patch = plugin.GetType("ChromaGLS.HarmonyPatches.GlsEasingRuntimePatch");
        tweenType = FindType("Tweening.FloatTween")!;
    }

    // Independent numeric samples catch the old Quad approximation as well as incorrect save/runtime enum casts.
    [TestCaseSource(typeof(GlsEasingTests), nameof(GlsEasingTests.Curves))]
    public void ProductionConversionAndNativeTweenUseExactCurve(int id, string name, double quarter, double threeQuarters)
    {
        object ease = Convert(id, applyPatch: true);
        Assert.That(ease.ToString(), Is.EqualTo(name), "The production conversion patch must preserve the named curve on this runtime.");
        AssertSample(ease, 0f, 0d);
        AssertSample(ease, 0.25f, quarter);
        AssertSample(ease, 0.75f, threeQuarters);
        AssertSample(ease, 1f, 1d);
        AssertDistribution(ease, quarter);
    }

    // Controls prove native tween support independently of whether the plugin conversion is implemented yet.
    [TestCaseSource(typeof(GlsEasingTests), nameof(GlsEasingTests.Curves))]
    public void NativeRuntimeAlreadyContainsExactCurve(int id, string name, double quarter, double threeQuarters)
    {
        object ease = Enum.Parse(converter.ReturnType, name);
        AssertSample(ease, 0f, 0d);
        AssertSample(ease, 0.25f, quarter);
        AssertSample(ease, 0.75f, threeQuarters);
        AssertSample(ease, 1f, 1d);
        AssertDistribution(ease, quarter);
    }

    // Baseline every non-added ID against this game's unmodified converter, including its unknown fallback.
    [Test]
    public void ProductionConversionPreservesSupportedAndUnknownIds()
    {
        foreach (int id in new[] { -1, 0, 1, 2, 3, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 100, 101, 102, -2, 31, 99, 103, int.MaxValue })
        {
            // LegacyRecognizedFallbacksRemainCompatible separately preserves the pre-existing 1.29.1 shim for recognized IDs outside this feature.
            if (IsLegacy129 && ((id >= 19 && id <= 30) || (id >= 100 && id <= 102)))
            {
                continue;
            }
            Assert.That(Convert(id, applyPatch: true), Is.EqualTo(Convert(id, applyPatch: false)), $"Save ID {id} must retain the native result.");
        }
    }

    // The fifteen new loader curves must not remove compatibility approximations previously supplied for other recognized 1.29.1 save IDs.
    [Test]
    public void LegacyRecognizedFallbacksRemainCompatible()
    {
        if (!IsLegacy129)
        {
            Assert.Ignore("Only 1.29.1 used the pre-existing recognized-ID fallback.");
        }
        string[] expected = ["InQuad", "OutQuad", "InOutQuad"];
        for (int id = 19; id <= 30; id++)
        {
            Assert.That(Convert(id, applyPatch: true).ToString(), Is.EqualTo(expected[(id - 19) % 3]), $"Recognized legacy save ID {id} must keep its previous fallback.");
        }
        foreach (int id in new[] { 100, 101, 102 })
        {
            Assert.That(Convert(id, applyPatch: true).ToString(), Is.EqualTo("InOutQuad"));
        }
    }

    private bool IsLegacy129 => converter.ReturnType.Assembly.GetName().Name == "HMLib";

    private object Convert(int id, bool applyPatch)
    {
        object[] args = [Enum.ToObject(converter.GetParameters()[0].ParameterType, id)];
        // Before the fix, 1.29.1 uses a ref-argument prefix; retaining this path makes the unfixed regression observable.
        if (applyPatch)
        {
            patch?.GetMethod("Prefix", All)?.Invoke(null, args);
        }
        object result = converter.Invoke(null, args)!;
        if (applyPatch && patch?.GetMethod("Postfix", All) is MethodInfo postfix)
        {
            object[] postfixArgs = [args[0], result];
            postfix.Invoke(null, postfixArgs);
            result = postfixArgs[1];
        }
        return result;
    }

    // Every GLS box uses this native spatial-distribution method; test Wave and Step independently of the temporal tween.
    private static void AssertDistribution(object ease, double quarter)
    {
        Type box = FindType("BeatmapEventDataBox")!;
        MethodInfo distribution = box.GetMethod("EventDistributionParamToStep", All)!;
        Type mode = distribution.GetParameters()[2].ParameterType;
        float wave = (float)distribution.Invoke(null, [1, 2f, Enum.Parse(mode, "Wave"), 5, ease])!;
        float step = (float)distribution.Invoke(null, [1, 2f, Enum.Parse(mode, "Step"), 4, ease])!;
        Assert.That(wave, Is.EqualTo(2d * quarter).Within(0.000002d), "Wave distribution must use the true easing curve.");
        Assert.That(step, Is.EqualTo(8d * quarter).Within(0.000008d), "Step distribution must use the true easing curve.");
    }

    private void AssertSample(object ease, float t, double expected)
    {
        float sample = float.NaN;
        object tween = tweenType.GetConstructors().Single(ctor => ctor.GetParameters().Length == 6)
            .Invoke([0f, 1f, new Action<float>(value => sample = value), 1f, ease, 0f]);
        tweenType.GetMethod("Sample")!.Invoke(tween, [t]);
        Assert.That(sample, Is.EqualTo(expected).Within(0.000002d), $"Native {ease} at {t} must not be approximated.");
    }

    private static Type? FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(assembly => assembly.GetType(name)).FirstOrDefault(type => type != null);
}
