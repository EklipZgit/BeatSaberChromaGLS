using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class GlsTranslationParityTests
{
    private const BindingFlags All =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly GlsEasingTests lifecycle = new();
    private MethodInfo convert = null!;
    private Harmony services = null!;

    [SetUp]
    public void SetUp()
    {
        lifecycle.SetUp();
        convert = (MethodInfo)typeof(GlsEasingTests).GetField("convert", All)!.GetValue(lifecycle)!;
        services = new Harmony("ChromaGLS.Tests.TranslationParity");
        Type manager = AccessTools.TypeByName("Tweening.TweeningManager");
        foreach (string method in new[] { "ResumeTween", "KillAllTweens" })
        {
            services.Patch(
                manager.GetMethod(method, All),
                prefix: new HarmonyMethod(typeof(GlsTranslationParityTests), nameof(SkipService)));
        }
    }

    [TearDown]
    public void TearDown()
    {
        services?.UnpatchSelf();
        lifecycle.TearDown();
    }

    [TestCase("cat-square")]
    [TestCase("cats-square")]
    [TestCase("mixed-staggered")]
    [TestCase("cats-distribution")]
    [TestCase("cats-distribution-mirrored")]
    [TestCase("cats-held")]
    public void GeneratedGamePlaybackMatchesChroMapperTransforms(string name)
    {
        JObject reference = JObject.Parse(File.ReadAllText(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "Fixtures", "chromapper-translation-reference.json")));
        JObject input = reference["cases"]!.Children<JObject>().Single(c => (string)c["name"]! == name);
        Type dataType = AccessTools.TypeByName("BeatmapData");
        object data = Activator.CreateInstance(dataType, 4)!;
        dataType.GetProperty("updateAllBeatmapDataOnInsert")!.SetValue(data, true);
        Type nodeType = AccessTools.TypeByName("LightTranslationBeatmapEventData");
        var nodes = new Dictionary<(int Axis, int Light), List<object>>();
        foreach (JObject record in input["generated"]!.Children<JObject>().OrderBy(n => (float)n["time"]!))
        {
            ConstructorInfo constructor = nodeType.GetConstructors().Single();
            object node = constructor.Invoke(constructor.GetParameters().Select(p => p.Name switch
            {
                "time" => (object)(float)record["time"]!,
                "groupId" => 6,
                "elementId" => (int)record["light"]!,
                "usePreviousEventValue" => (bool)record["usePrevious"]!,
                "axis" => Enum.ToObject(p.ParameterType, (int)record["axis"]!),
                "easeType" => convert.Invoke(null,
                    [Enum.ToObject(convert.GetParameters()[0].ParameterType, (int)record["easing"]!)]),
                "translation" => (float)record["value"]!,
                "distribution" => (float)record["distribution"]!,
                _ => Activator.CreateInstance(p.ParameterType),
            }).ToArray());
            dataType.GetMethod("InsertBeatmapEventData")!.Invoke(data, [node]);
            var key = ((int)record["axis"]!, (int)record["light"]!);
            if (!nodes.TryGetValue(key, out List<object>? channel))
            {
                nodes.Add(key, channel = new List<object>());
            }
            channel.Add(node);
        }

        Type controllerType = AccessTools.TypeByName("BeatmapCallbacksController");
        object controllerInit = Activator.CreateInstance(
            controllerType.GetNestedType("InitData")!, data, float.MinValue, false)!;
        object controller = Activator.CreateInstance(controllerType, controllerInit)!;
        Type effectType = AccessTools.TypeByName("LightTranslationGroupEffect");
        Type initType = effectType.GetNestedType("InitData")!;
        Type managerType = AccessTools.TypeByName("Tweening.SongTimeTweeningManager");
        object manager = TestHostCompat.GetUninitializedObject(managerType);
        for (int light = 0; light < 4; light++)
        {
            ConstructorInfo initConstructor = initType.GetConstructors().Single();
            object init = initConstructor.Invoke(initConstructor.GetParameters().Select(p =>
            {
                if (p.Name == "groupId")
                {
                    return (object)6;
                }
                if (p.Name == "elementId")
                {
                    return light;
                }
                if (p.ParameterType == typeof(bool))
                {
                    return (bool)input["mirrored"]!;
                }
                if (p.ParameterType == typeof(Vector2))
                {
                    return new Vector2(-10f, 10f);
                }
                return null;
            }).ToArray());
            object effect = Activator.CreateInstance(effectType, init, manager, controller)!;
            try
            {
                var tweens = new object[3];
                var handlers = new Delegate[3];
                for (int axis = 0; axis < 3; axis++)
                {
                    string field = new[] { "_xTranslationTween", "_yTranslationTween", "_zTranslationTween" }[axis];
                    tweens[axis] = effectType.GetField(field, All)!.GetValue(effect)!;
                    handlers[axis] = (Delegate)effectType.GetMethod("GetTranslationEventHandler", All)!
                        .Invoke(effect, [tweens[axis], new Vector2(-10f, 10f), new Vector2(-10f, 10f), (bool)input["mirrored"]!])!;
                }

                JObject[] samples = input["samples"]!.Children<JObject>()
                    .Where(s => (int)s["light"]! == light).ToArray();
                var random = new System.Random(193);
                foreach (IEnumerable<JObject> order in new[]
                    { samples, samples.Reverse(), samples.OrderBy(_ => random.Next()) })
                {
                    foreach (JObject sample in order)
                    {
                        float time = (float)sample["time"]!;
                        controllerType.GetField("_songTime", All)!.SetValue(controller, time);
                        for (int axis = 0; axis < 3; axis++)
                        {
                            if (nodes.TryGetValue((axis, light), out List<object>? channel))
                            {
                                object current = channel.Last(n => (float)nodeType.GetProperty("time")!.GetValue(n)! <= time);
                                handlers[axis].DynamicInvoke(current);
                            }
                        }
                        for (int axis = 0; axis < 3; axis++)
                        {
                            object tween = tweens[axis];
                            Type type = tween.GetType();
                            float duration = (float)type.GetProperty("duration")!.GetValue(tween)!;
                            float start = (float)type.GetProperty("startTime")!.GetValue(tween)!;
                            float progress = duration > 0f ? Mathf.Clamp01((time - start) / duration) : 1f;
                            float actual = (float)type.GetMethod("GetValue")!.Invoke(tween, [progress])!;
                            Assert.That(actual, Is.EqualTo((float)sample["value"]![axis]!).Within(0.0001f),
                                $"{name}, light {light}, axis {axis}, time {time}");
                        }
                    }
                }
            }
            finally
            {
                effectType.GetMethod("Cleanup")!.Invoke(effect, null);
            }
        }
        controllerType.GetMethod("Dispose")!.Invoke(controller, null);
    }

    private static bool SkipService() => false;
}
