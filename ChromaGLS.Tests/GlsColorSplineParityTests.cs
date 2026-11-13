using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CustomJSONData.CustomBeatmap;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class GlsColorSplineParityTests
{
    private const BindingFlags All =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly GlsEasingTests lifecycle = new();

    [SetUp]
    public void SetUp() => lifecycle.SetUp();

    [TearDown]
    public void TearDown() => lifecycle.TearDown();

    [TestCase("GrayRampMapJson-103")]
    [TestCase("GrayRampMapJson-104")]
    [TestCase("BrightnessMapJson-103")]
    [TestCase("BrightnessMapJson-104")]
    [TestCase("StrobeMapJson-103")]
    [TestCase("StrobeMapJson-104")]
    [TestCase("true-hsv-wrap-hdr")]
    [TestCase("true-hsv-wrap-hdr-104")]
    [TestCase("held-103")]
    [TestCase("held-104")]
    public void ColorCallbackMatchesCapturedChroMapperOutput(string name)
    {
        JObject reference = JObject.Parse(File.ReadAllText(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "Fixtures", "chromapper-color-reference.json")));
        JObject input = reference["cases"]!.Children<JObject>().Single(c => (string)c["name"]! == name);
        JObject[] records = input["map"]!["lightColorEventBoxGroups"]![0]!["e"]![0]!["e"]!
            .Children<JObject>().ToArray();
        object[] nodes = records.Select(CreateEvent).ToArray();
        MethodInfo connect = nodes[0].GetType().GetMethod("__ConnectWithNextSameTypeEventData", All)!;
        for (int i = 0; i < nodes.Length - 1; i++)
        {
            connect.Invoke(nodes[i], [nodes[i + 1]]);
        }

        // WithColorEffect intercepts scene services only. Native handlers, tweens and plugin patches run normally.
        typeof(GlsEasingTests).GetMethod("WithColorEffectEvents", All)!.Invoke(lifecycle,
        [
            nodes,
            (Action<object, object>)((effect, tween) =>
            {
                MethodInfo handler = effect.GetType().GetMethod("HandleColorChangeBeatmapEvent", All)!;
                MethodInfo sample = tween.GetType().GetMethod("Sample")!;
                FieldInfo captured = typeof(GlsEasingTests).GetField("capturedColor", All)!;
                JObject[] samples = input["samples"]!.Children<JObject>().ToArray();
                var random = new System.Random(59);
                foreach (IEnumerable<JObject> order in new[]
                    { samples, samples.Reverse(), samples.OrderBy(_ => random.Next()) })
                {
                    foreach (JObject expected in order)
                    {
                        float time = (float)expected["time"]!;
                        int index = Array.FindLastIndex(records, node => (float)node["b"]! <= time);
                        handler.Invoke(effect, [nodes[index]]);
                        float end = index + 1 < records.Length ? (float)records[index + 1]["b"]! : time;
                        float start = (float)records[index]["b"]!;
                        sample.Invoke(tween, [end > start ? (time - start) / (end - start) : 0f]);
                        Color actual = (Color)captured.GetValue(null)!;
                        for (int component = 0; component < 4; component++)
                        {
                            Assert.That(actual[component],
                                Is.EqualTo((float)expected["value"]![component]!).Within(0.00001f),
                                $"{name}, time {time}, component {component}");
                        }
                    }
                }
            }),
        ]);
    }

    private static object CreateEvent(JObject record)
    {
        ConstructorInfo constructor = typeof(CustomData).Assembly
            .GetType("CustomJSONData.CustomBeatmap.CustomLightColorBeatmapEventData", true)!
            .GetConstructors().Single();
        CustomData metadata = Metadata(record["customData"] as JObject);
        metadata["cg_sb"] = (float)record["sb"]!;
        metadata["cg_sf"] = (int)record["sf"]! == 1;
        object source = constructor.Invoke(constructor.GetParameters().Select(p => p.Name switch
        {
            "time" => (object)(float)record["b"]!,
            "groupId" => 1,
            "brightness" => (float)record["s"]!,
            "easeType" => Enum.Parse(p.ParameterType, "Linear"),
            "usePreviousValue" => (int?)record["i"] == 2,
            "transitionType" => Enum.Parse(
                p.ParameterType, (int?)record["i"] == 2 ? "Extend" : "Interpolate"),
            "strobeBeatFrequency" => (int)record["f"]!,
            "strobeBrightness" => (float)record["sb"]!,
            "strobeFade" => (int)record["sf"]! == 1,
            "customData" => metadata,
            _ => Activator.CreateInstance(p.ParameterType),
        }).ToArray());
        Type wrapper = Assembly.Load("ChromaGLS")
            .GetType("ChromaGLS.HarmonyPatches.GlsConverterPatches", true)!
            .GetNestedType("ExtendedLightColorBeatmapEventData", All)!;
        return wrapper.GetConstructors(All).Single().Invoke([source, metadata, null]);
    }

    [TestCase(-0.25f, 0.2f, 0.9f)]
    [TestCase(1.25f, 0.8f, 0.4f)]
    public void ColorOnlySplineClampsProgressWithoutClampingOutput(float progress, float gray, float brightness)
    {
        JObject[] records =
        [
            JObject.Parse("{\"b\":0,\"s\":0.2,\"f\":0,\"sb\":0,\"sf\":0,\"customData\":{\"color\":[0.1,0.1,0.1,1],\"colorEasing\":\"Cat\",\"easing\":\"L\"}}"),
            JObject.Parse("{\"b\":1,\"s\":0.9,\"f\":0,\"sb\":0,\"sf\":0,\"customData\":{\"color\":[0.2,0.2,0.2,1],\"colorEasing\":\"Cat\",\"easing\":\"L\"}}"),
            JObject.Parse("{\"b\":2.5,\"s\":0.4,\"f\":0,\"sb\":0,\"sf\":0,\"customData\":{\"color\":[0.8,0.8,0.8,1],\"colorEasing\":\"Cat\",\"easing\":\"L\"}}"),
            JObject.Parse("{\"b\":5,\"s\":0.7,\"f\":0,\"sb\":0,\"sf\":0,\"customData\":{\"color\":[0.4,0.4,0.4,1],\"colorEasing\":\"Cat\",\"easing\":\"L\"}}"),
        ];
        object[] nodes = records.Select(CreateEvent).ToArray();
        MethodInfo connect = nodes[0].GetType().GetMethod("__ConnectWithNextSameTypeEventData", All)!;
        for (int i = 0; i < nodes.Length - 1; i++)
        {
            connect.Invoke(nodes[i], [nodes[i + 1]]);
        }
        typeof(GlsEasingTests).GetMethod("WithColorEffectEvents", All)!.Invoke(lifecycle,
        [
            nodes,
            (Action<object, object>)((effect, tween) =>
            {
                effect.GetType().GetMethod("HandleColorChangeBeatmapEvent", All)!.Invoke(effect, [nodes[1]]);
                MethodInfo sample = tween.GetType().GetMethod("Sample")!;
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    sample.Invoke(tween, [progress]);
                    Color actual = (Color)typeof(GlsEasingTests).GetField("capturedColor", All)!.GetValue(null)!;
                    Assert.That(actual.r, Is.EqualTo(gray).Within(0.00001f));
                    Assert.That(actual.a, Is.EqualTo(brightness).Within(0.00001f));
                }
            }),
        ]);
    }

    [TestCase("brightness", false, 0.25f, 0.02734375f)]
    [TestCase("normal", false, 0.25f, 0.02734375f)]
    [TestCase("normal", true, 0.25f, 0.02734375f)]
    [TestCase("alpha", false, 0.25f, 0.02734375f)]
    [TestCase("alpha", true, 0.25f, 0.02734375f)]
    [TestCase("strobe", false, 0.55f, 0.10965625f)]
    [TestCase("strobe", false, 0.75f, 0.17578125f)]
    [TestCase("strobeAlpha", false, 0.75f, 0.17578125f)]
    public void SmoothedColorMatchesChroMapperDistanceProfile(string track, bool hsv, float time, float expected)
    {
        var records = new JObject[4];
        for (int index = 0; index < records.Length; index++)
        {
            float value = 0.25f * index;
            bool strobed = track == "strobe" || track == "strobeAlpha";
            var metadata = new JObject
            {
                ["color"] = track == "normal"
                    ? new JArray(value, value, value, value)
                    : track == "alpha"
                        ? new JArray(1, 1, 1, value)
                        : strobed ? new JArray(0.5f, 0, 0, 1) : new JArray(1, 1, 1, 1),
                ["easing"] = index > 0 && (track == "brightness" || track == "strobeAlpha") ? 104 : 0,
                ["colorEasing"] = index > 0 && (track == "normal" || track == "alpha") ? 104 : 0,
                ["strobeColorEasing"] = index > 0 && track == "strobe" ? 104 : 0,
            };
            if (strobed)
            {
                metadata["strobeColor"] = track == "strobe"
                    ? new JArray(value, value, value, 1) : new JArray(1, 1, 1, 1);
            }
            if (hsv)
            {
                metadata["easingType"] = "HSV";
            }
            records[index] = new JObject
            {
                ["b"] = index,
                ["s"] = track == "brightness" ? value : 1,
                ["f"] = strobed ? 1 : 0,
                ["sb"] = track == "strobeAlpha" ? value : strobed ? 1 : 0,
                ["sf"] = 0,
                ["customData"] = metadata,
            };
        }
        object[] nodes = records.Select(CreateEvent).ToArray();
        typeof(GlsEasingTests).GetMethod("WithColorEffectEvents", All)!.Invoke(lifecycle,
        [
            nodes,
            (Action<object, object>)((effect, tween) =>
            {
                MethodInfo handler = effect.GetType().GetMethod("HandleColorChangeBeatmapEvent", All)!;
                MethodInfo sample = tween.GetType().GetMethod("Sample")!;
                FieldInfo captured = typeof(GlsEasingTests).GetField("capturedColor", All)!;
                foreach (int seek in new[] { 0, 1, 3, 2, 0 })
                {
                    handler.Invoke(effect, [nodes[seek]]);
                    handler.Invoke(effect, [nodes[0]]);
                    sample.Invoke(tween, [time]);
                    Color actual = (Color)captured.GetValue(null)!;
                    Color reference = track == "normal" ? new Color(expected, expected, expected, expected)
                        : track == "strobe" ? new Color(expected, expected, expected, 1)
                        : new Color(1, 1, 1, expected);
                    for (int component = 0; component < 4; component++)
                    {
                        Assert.That(actual[component], Is.EqualTo(reference[component]).Within(0.00001f),
                            $"{track}, HSV {hsv}, time {time}, component {component}, after seek {seek}");
                    }
                }
            }),
        ]);
    }

    private static CustomData Metadata(JObject? input)
    {
        var result = new CustomData();
        if (input == null)
        {
            return result;
        }
        foreach (JProperty property in input.Properties())
        {
            result[property.Name] = property.Value is JArray array
                ? array.Values<object>().ToList()
                : ((JValue)property.Value).Value!;
        }
        return result;
    }
}
