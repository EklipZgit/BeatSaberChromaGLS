using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ChromaGLS.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class GlsScalarParityTests
{
    private const BindingFlags All =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private readonly GlsEasingTests lifecycle = new();
    private Harmony services = null!;
    private MethodInfo convert = null!;

    [SetUp]
    public void SetUp()
    {
        lifecycle.SetUp();
        convert = (MethodInfo)typeof(GlsEasingTests).GetField("convert", All)!.GetValue(lifecycle)!;
        services = new Harmony("ChromaGLS.Tests.ScalarParity");
        Type manager = AccessTools.TypeByName("Tweening.TweeningManager");
        foreach (string method in new[] { "ResumeTween", "KillAllTweens" })
        {
            services.Patch(manager.GetMethod(method, All),
                prefix: new HarmonyMethod(typeof(GlsScalarParityTests), nameof(SkipService)));
        }
    }

    [TearDown]
    public void TearDown()
    {
        services?.UnpatchSelf();
        lifecycle.TearDown();
    }

    [TestCase("unequal-103")]
    [TestCase("unequal-104")]
    [TestCase("repeat-103")]
    [TestCase("repeat-104")]
    [TestCase("held-103")]
    [TestCase("held-104")]
    public void FloatFxMatchesCapturedChroMapperTarget(string name)
    {
        if (AccessTools.TypeByName("FloatFxGroupEffect") == null)
        {
            Assert.Ignore("This Beat Saber version has no GLS FloatFX.");
        }
        JObject reference = Load("float");
        JObject input = reference["cases"]!.Children<JObject>().Single(c => (string)c["name"]! == name);
        Compare("FloatFx", input["generated"]!.Children<JObject>().ToArray(),
            input["samples"]!.Children<JObject>().ToArray(), 0.00001f);
    }

    [Test]
    public void RotationMatchesCapturedChroMapperAngles() => CompareRotationReference("rotation");

    [Test]
    public void RotationSmoothedMatchesCapturedChroMapperAngles() => CompareRotationReference("rotation-smoothed");

    private void CompareRotationReference(string family)
    {
        JObject reference = Load(family);
        foreach (IGrouping<(int Axis, int Light), JObject> channel in reference["generated"]!
            .Children<JObject>().GroupBy(n => ((int)n["axis"]!, (int)n["light"]!)))
        {
            JObject[] samples = reference["samples"]!.Children<JObject>()
                .Where(s => (int)s["axis"]! == channel.Key.Axis && (int)s["light"]! == channel.Key.Light)
                .ToArray();
            Compare("LightRotation", channel.ToArray(), samples, 0.0005f);
        }
    }

    [TestCase("FloatFx", 0.25f, false, 0, 0.02734375f, 0.17578125f, 0.3125f)]
    [TestCase("LightRotation", 90f, false, 0, 9.84375f, 63.28125f, 112.5f)]
    [TestCase("LightRotation", 90f, true, 0, -9.84375f, -63.28125f, 247.5f)]
    [TestCase("LightRotation", 90f, false, 1, 49.21875f, 316.40625f, 202.5f)]
    public void SmoothedScalarMatchesChroMapperDistanceProfile(
        string family,
        float step,
        bool counterclockwise,
        int loops,
        float firstQuarter,
        float thirdQuarter,
        float middleQuarter)
    {
        if (family == "FloatFx" && AccessTools.TypeByName("FloatFxGroupEffect") == null)
        {
            Assert.Ignore("This Beat Saber version has no GLS FloatFX.");
        }

        var records = new JObject[4];
        for (var index = 0; index < records.Length; index++)
        {
            records[index] = new JObject
            {
                ["time"] = index,
                ["value"] = counterclockwise && index > 0 ? 360f - (index * step) : index * step,
                ["easing"] = index == 0 ? 0 : 104,
                ["usePrevious"] = false,
                ["axis"] = 0,
                ["light"] = 0,
                ["loops"] = index == 0 ? 0 : loops,
                ["direction"] = counterclockwise ? 2 : 0,
            };
        }

        var samples = new[]
        {
            new JObject { ["time"] = 0f, ["value"] = 0f },
            new JObject { ["time"] = 0.25f, ["value"] = firstQuarter },
            new JObject { ["time"] = 0.75f, ["value"] = thirdQuarter },
            new JObject { ["time"] = 1f, ["value"] = records[1]["value"]!.DeepClone() },
            new JObject { ["time"] = 1.25f, ["value"] = middleQuarter },
            new JObject { ["time"] = 3f, ["value"] = records[3]["value"]!.DeepClone() },
        };
        Compare(family, records, samples, 0.0005f);
    }

    private void Compare(string family, JObject[] records, JObject[] samples, float tolerance)
    {
        Type nodeType = AccessTools.TypeByName(family + "BeatmapEventData");
        Type effectType = AccessTools.TypeByName(family + "GroupEffect");
        string setter = family == "FloatFx" ? "SetValue" : "SetRotation";
        services.Patch(effectType.GetMethod(setter, All),
            prefix: new HarmonyMethod(typeof(GlsScalarParityTests), nameof(SkipService)));
        Type dataType = AccessTools.TypeByName("BeatmapData");
        object data = Activator.CreateInstance(dataType, 4)!;
        dataType.GetProperty("updateAllBeatmapDataOnInsert")!.SetValue(data, true);
        var nodes = new List<object>();
        foreach (JObject record in records.OrderBy(n => (float)n["time"]!))
        {
            ConstructorInfo constructor = nodeType.GetConstructors().Single();
            object node = constructor.Invoke(constructor.GetParameters().Select(p => p.Name switch
            {
                "time" => (object)(float)record["time"]!,
                "usePreviousEventValue" => (bool)record["usePrevious"]!,
                "easeType" => convert.Invoke(null,
                    [Enum.ToObject(convert.GetParameters()[0].ParameterType, (int)record["easing"]!)]),
                "rotation" or "value" => (float)record["value"]!,
                "loopCount" => (int?)record["loops"] ?? 0,
                "rotationDirection" => Enum.ToObject(p.ParameterType, (int)record["direction"]!),
                "axis" => Enum.ToObject(p.ParameterType, (int)record["axis"]!),
                "elementId" => (int?)record["light"] ?? 0,
                _ => Activator.CreateInstance(p.ParameterType),
            }).ToArray());
            dataType.GetMethod("InsertBeatmapEventData")!.Invoke(data, [node]);
            nodes.Add(node);
        }

        Type controllerType = AccessTools.TypeByName("BeatmapCallbacksController");
        object controllerInit = Activator.CreateInstance(
            controllerType.GetNestedType("InitData")!, data, float.MinValue, false)!;
        object controller = Activator.CreateInstance(controllerType, controllerInit)!;
        Type initType = effectType.GetNestedType("InitData")!;
        ConstructorInfo initConstructor = initType.GetConstructors().Single();
        object init = initConstructor.Invoke(initConstructor.GetParameters().Select(p => p.Name switch
        {
            "axis" => Enum.ToObject(p.ParameterType, (int?)records[0]["axis"] ?? 0),
            "elementId" => (object)((int?)records[0]["light"] ?? 0),
            _ => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null,
        }).ToArray());
        Type managerType = AccessTools.TypeByName("Tweening.SongTimeTweeningManager");
        object manager = TestHostCompat.GetUninitializedObject(managerType);
        object effect = Activator.CreateInstance(effectType, init, manager, controller)!;
        try
        {
            string field = family == "FloatFx" ? "_floatTween" : "_rotationTween";
            string handlerName = family == "FloatFx"
                ? "HandleFloatFxBeatmapEventCallback"
                : "HandleRotationChangeBeatmapEvent";
            object tween = effectType.GetField(field, All)!.GetValue(effect)!;
            MethodInfo handler = effectType.GetMethod(handlerName, All)!;
            Type tweenType = tween.GetType();
            var random = new Random(397);
            foreach (IEnumerable<JObject> order in new[]
                { samples, samples.Reverse(), samples.OrderBy(_ => random.Next()) })
            {
                foreach (JObject sample in order)
                {
                    float time = (float)sample["time"]!;
                    int index = Array.FindLastIndex(records, n => (float)n["time"]! <= time);
                    float actual = 0f;
                    if (index >= 0)
                    {
                        handler.Invoke(effect, [nodes[index]]);
                        float start = (float)tweenType.GetProperty("startTime")!.GetValue(tween)!;
                        float duration = (float)tweenType.GetProperty("duration")!.GetValue(tween)!;
                        if (index == records.Length - 1 || (int)records[index + 1]["easing"]! == -1)
                        {
                            actual = family == "FloatFx"
                                ? (float)nodeType.GetProperty("value")!.GetValue(nodes[index])!
                                : UnityEngine.Mathf.Repeat((float)nodeType.GetProperty("rotation")!.GetValue(nodes[index])!, 360f);
                        }
                        else
                        {
                            float progress = duration > 0f
                                ? UnityEngine.Mathf.Clamp01((time - start) / duration) : 1f;
                            actual = (float)tweenType.GetMethod("GetValue")!.Invoke(tween, [progress])!;
                        }
                    }
                    Assert.That(actual, Is.EqualTo((float)sample["value"]!).Within(tolerance),
                        $"{family}, time {time}, sample {sample}");
                }
            }
        }
        finally
        {
            effectType.GetMethod("Cleanup")!.Invoke(effect, null);
            controllerType.GetMethod("Dispose")!.Invoke(controller, null);
            services.Unpatch(effectType.GetMethod(setter, All), HarmonyPatchType.Prefix, services.Id);
        }
    }

    private static JObject Load(string family) => JObject.Parse(File.ReadAllText(Path.Combine(
        TestContext.CurrentContext.TestDirectory, "Fixtures", "chromapper-" + family + "-reference.json")));

    private static bool SkipService() => false;
}
