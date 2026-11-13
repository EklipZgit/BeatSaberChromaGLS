using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ChromaGLS.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class GlsCustomNodeEasingIntegrationTests
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static readonly (string Abbreviation, int Id, string InternalName)[] Aliases =
    [
        ("I^2", 1, "easeInQuad"),
        ("O^2", 2, "easeOutQuad"),
        ("IO^2", 3, "easeInOutQuad"),
        ("ISn", 4, "easeInSine"),
        ("OSn", 5, "easeOutSine"),
        ("IOSn", 6, "easeInOutSine"),
        ("I^3", 7, "easeInCubic"),
        ("O^3", 8, "easeOutCubic"),
        ("IO^3", 9, "easeInOutCubic"),
        ("I^4", 10, "easeInQuart"),
        ("O^4", 11, "easeOutQuart"),
        ("IO^4", 12, "easeInOutQuart"),
        ("I^5", 13, "easeInQuint"),
        ("O^5", 14, "easeOutQuint"),
        ("IO^5", 15, "easeInOutQuint"),
        ("IEx", 16, "easeInExpo"),
        ("OEx", 17, "easeOutExpo"),
        ("IOEx", 18, "easeInOutExpo"),
        ("ICr", 19, "easeInCirc"),
        ("OCr", 20, "easeOutCirc"),
        ("IOCr", 21, "easeInOutCirc"),
        ("IBk", 22, "easeInBack"),
        ("OBk", 23, "easeOutBack"),
        ("IOTBk", 24, "easeInOutBack"),
        ("IEl", 25, "easeInElastic"),
        ("OEl", 26, "easeOutElastic"),
        ("IOTEl", 27, "easeInOutElastic"),
        ("IBo", 28, "easeInBounce"),
        ("OBo", 29, "easeOutBounce"),
        ("IOTBo", 30, "easeInOutBounce"),
        ("IOBk", 100, "easeBeatSaberInOutBack"),
        ("IOEl", 101, "easeBeatSaberInOutElastic"),
        ("IOBo", 102, "easeBeatSaberInOutBounce"),
    ];

    private GlsEasingTests lifecycle = null!;

    [SetUp]
    public void EnablePlugin() => (lifecycle = new GlsEasingTests()).SetUp();

    [TearDown]
    public void DisablePlugin() => lifecycle.TearDown();

    private static Type Patch(string name) =>
        Assembly.Load("ChromaGLS").GetType("ChromaGLS.HarmonyPatches." + name, true)!;

    private static bool Resolve(object? raw, bool track, out Func<float, float>? curve)
    {
        object?[] args = [raw, null];
        bool found = (bool)Patch("GlsEasingRuntimePatch")
            .GetMethod(track ? "TryGetTrackEasing" : "TryGetCustomEasing", All)!.Invoke(null, args)!;
        curve = (Func<float, float>?)args[1];
        return found;
    }

    [TestCaseSource(nameof(Aliases))]
    public void AbbreviationAndInternalNameMatchLegacyInteger(
        (string Abbreviation, int Id, string InternalName) alias)
    {
        Assert.That(Resolve(alias.Id, false, out var legacy), Is.True);
        foreach (string text in new[] { alias.Abbreviation, alias.InternalName })
        {
            Assert.That(Resolve(text, false, out var curve), Is.True, text);
            foreach (float time in new[] { 0.125f, 0.25f, 0.75f, 1f })
            {
                Assert.That(curve!(time), Is.EqualTo(legacy!(time)), $"{text} at {time}");
            }
        }
    }

    [Test]
    public void LinearTracksAndInvalidMetadataKeepExistingSemantics()
    {
        Assert.That(Resolve("L", false, out _), Is.False);
        Assert.That(Resolve("L", true, out var linear), Is.True);
        Assert.That(linear!(0.25f), Is.EqualTo(0.25f));
        foreach (object? invalid in new object?[]
                 {
                     "4", "unknown", null, true, 9.5, double.NaN, double.PositiveInfinity,
                     double.NegativeInfinity, double.MaxValue, long.MaxValue, ulong.MaxValue,
                     4.000000000000000000001m, 99, 107, -2, new object()
                 })
        {
            Assert.That(Resolve(invalid, true, out _), Is.False);
        }

        foreach (var (raw, expectedId) in new (object Raw, int Id)[]
                 {
                     ("Cat", 103), ("CatS", 104), ("splineCatmullRom", 103),
                     ("splineCatmullRomSmoothed", 104), (103, 103), (104, 104)
                 })
        {
            object[] args = [raw, 0];
            Assert.That((bool)Patch("GlsEasingRuntimePatch").GetMethod("TryParseEasingId", All)!.Invoke(null, args)!, Is.True);
            Assert.That((int)args[1], Is.EqualTo(expectedId));
            Assert.That(Resolve(raw, false, out _), Is.False, "Splines must not masquerade as scalar curves.");
            Assert.That(Resolve(raw, true, out _), Is.False, "Splines must not masquerade as scalar curves.");
        }

        foreach (object removed in new object[]
                 {
                     "Cat.5", "Cat1", "splineCatmullRomCentripetal", "splineCatmullRomChordal", 105, 106
                 })
        {
            object[] args = [removed, 0];
            Assert.That((bool)Patch("GlsEasingRuntimePatch").GetMethod("TryParseEasingId", All)!.Invoke(null, args)!, Is.False,
                $"Retired spline id/alias {removed} must no longer parse.");
            Assert.That(Resolve(removed, false, out _), Is.False);
            Assert.That(Resolve(removed, true, out _), Is.False);
        }
    }

    private static Type SaveDataType =>
        typeof(CustomJSONData.CustomBeatmap.CustomData).Assembly.GetType(
            "CustomJSONData.CustomBeatmap.Version3CustomBeatmapSaveData", false)
        ?? typeof(CustomJSONData.CustomBeatmap.CustomData).Assembly.GetType(
            "CustomJSONData.CustomBeatmap.CustomBeatmapSaveData", true)!;

    private static object[] ParseNodes(int family, JArray nodes, bool nested)
    {
        string methodName = family switch
        {
            2 => "DeserializeLightRotationEventBoxGroupArray",
            3 => "DeserializeLightTranslationEventBoxGroupArray",
            _ => "DeserializeFxEventCollection",
        };
        MethodInfo? method = SaveDataType.GetMethod(methodName, All);
        if (method == null)
        {
            Assert.Ignore("This game has no FloatFX save-data reader.");
        }

        JToken payload = family == 4
            ? new JObject { ["_il"] = new JArray(new JObject { ["b"] = 0, ["v"] = 7 }), ["_fl"] = nodes }
            : new JArray(new JObject
            {
                ["b"] = 0, ["g"] = 0,
                ["e"] = new JArray(new JObject
                {
                    ["f"] = new JObject { ["f"] = 2, ["p"] = 0, ["t"] = 1 },
                    ["l"] = nodes,
                }),
            });
        string json = (nested ? new JObject { ["payload"] = payload, ["sentinel"] = 7 } : payload).ToString();
        using JsonTextReader reader = new(new StringReader(json));
        if (nested)
        {
            reader.Read();
            reader.Read();
        }

        object parsed;
        if (family == 4)
        {
            parsed = method!.Invoke(null, [reader])!;
        }
        else
        {
            parsed = Activator.CreateInstance(method!.GetParameters()[1].ParameterType)!;
            method.Invoke(null, [reader, parsed]);
        }

        Assert.That(reader.TokenType, Is.EqualTo(family == 4 ? JsonToken.EndObject : JsonToken.EndArray));
        if (nested)
        {
            Assert.That(reader.Read(), Is.True, "The wrapper must leave the caller's reader open.");
            Assert.That(reader.Value, Is.EqualTo("sentinel"));
            reader.Read();
            Assert.That(Convert.ToInt32(reader.Value), Is.EqualTo(7));
        }

        if (family == 4)
        {
            var ints = (IEnumerable)parsed.GetType().GetProperty("intEventsList")!.GetValue(parsed)!;
            object first = ints.Cast<object>().Single();
            Assert.That(first.GetType().GetProperty("value")!.GetValue(first), Is.EqualTo(7));
            return ((IEnumerable)parsed.GetType().GetProperty("floatEventsList")!.GetValue(parsed)!).Cast<object>().ToArray();
        }

        object group = ((IList)parsed)[0]!;
        object box = ((IEnumerable)group.GetType().GetProperty("eventBoxes")!.GetValue(group)!).Cast<object>().Single();
        string property = family == 2 ? "lightRotationBaseDataList" : "lightTranslationBaseDataList";
        return ((IEnumerable)box.GetType().GetProperty(property)!.GetValue(box)!).Cast<object>().ToArray();
    }

    private static object RuntimeEase(object node) =>
        node.GetType().GetField("easeType", All)?.GetValue(node)
        ?? node.GetType().GetProperty("easeType", All)!.GetValue(node)!;

    private void AssertNativeCurve(object actual, int expectedId)
    {
        object expected = typeof(GlsEasingTests).GetMethod("Convert", All)!.Invoke(lifecycle, [expectedId])!;
        Assert.That(actual, Is.EqualTo(expected));
        MethodInfo interpolate = AccessTools.TypeByName("Interpolation").GetMethod("Interpolate", All)!;
        MethodInfo assertTween = typeof(GlsEasingTests).GetMethod("AssertTween", All)!;
        foreach (float time in new[] { 0.25f, 0.75f })
        {
            float value = (float)interpolate.Invoke(null, [time, expected])!;
            assertTween.Invoke(null, [actual, time, (double)value]);
        }
    }

    [TestCase(2, false)]
    [TestCase(2, true)]
    [TestCase(3, false)]
    [TestCase(3, true)]
    [TestCase(4, false)]
    [TestCase(4, true)]
    public void V3RealReadersApplyOverridesAndKeepCallerPosition(int family, bool nested)
    {
        string key = family == 4 ? "i" : "e";
        var nodes = new JArray();
        var expectedIds = new System.Collections.Generic.List<int>();
        foreach (var entry in new[] { (4, "ISn"), (9, "IO^3"), (18, "IOEx"), (100, "IOBk") })
        {
            foreach (JToken metadata in new JToken[] { new JValue(entry.Item1), new JValue(entry.Item2) })
            {
                var node = new JObject { ["b"] = nodes.Count, ["r"] = 30, ["t"] = 0.5, ["v"] = 0.5 };
                // Exercise both orders because metadata is allowed before or after the native field.
                if (nodes.Count % 2 == 0)
                {
                    node[key] = 21;
                }

                node["customData"] = new JObject { ["easing"] = metadata };
                node[key] = 21;
                nodes.Add(node);
                expectedIds.Add(entry.Item1);
            }
        }

        nodes.Add(new JObject { ["b"] = nodes.Count, ["customData"] = new JObject { ["easing"] = "IO^3" } });
        expectedIds.Add(9);
        nodes.Add(new JObject { ["b"] = nodes.Count, [key] = 9 });
        expectedIds.Add(9);
        foreach (var (value, id) in new (JToken Value, int Id)[]
                 {
                     (new JValue("Cat"), 103), (new JValue("CatS"), 104),
                     (new JValue("splineCatmullRom"), 103), (new JValue("splineCatmullRomSmoothed"), 104),
                     (new JValue(103), 103), (new JValue(104), 104),
                 })
        {
            nodes.Add(new JObject { ["b"] = nodes.Count, [key] = 21, ["customData"] = new JObject { ["easing"] = value } });
            expectedIds.Add(id);
        }

        // Retired variants leave the written native fallback unchanged.
        foreach (JToken spline in new JToken[]
                 {
                     new JValue("Cat.5"), new JValue("Cat1"), new JValue("splineCatmullRomCentripetal"),
                     new JValue("splineCatmullRomChordal"), new JValue(105), new JValue(106)
                 })
        {
            nodes.Add(new JObject { ["b"] = nodes.Count, [key] = 21, ["customData"] = new JObject { ["easing"] = spline } });
            expectedIds.Add(21);
        }
        foreach (JToken custom in new JToken[]
                 {
                     new JArray(9), new JValue(true), new JObject { ["easing"] = new JArray(9) },
                     new JObject { ["easing"] = "4" }, new JObject { ["easing"] = "unknown" }
                 })
        {
            nodes.Add(new JObject { ["b"] = nodes.Count, [key] = 21, ["customData"] = custom });
            expectedIds.Add(21);
        }

        object[] parsed = ParseNodes(family, nodes, nested);
        Assert.That(parsed.Length, Is.EqualTo(expectedIds.Count));
        foreach (var pair in parsed.Zip(expectedIds, (node, id) => (node, id)))
        {
            object saveEase = RuntimeEase(pair.node);
            Assert.That(Convert.ToInt32(saveEase), Is.EqualTo(pair.id));
            object native = typeof(GlsEasingTests).GetMethod("Convert", All)!.Invoke(lifecycle, [Convert.ToInt32(saveEase)])!;
            AssertNativeCurve(native, pair.id);
        }
    }

    [Test]
    public void EasingReadersStayPatchedAcrossPluginToggle()
    {
        var readers = new List<MethodInfo>
        {
            SaveDataType.GetMethod("DeserializeLightRotationEventBoxGroupArray", All)!,
            SaveDataType.GetMethod("DeserializeLightTranslationEventBoxGroupArray", All)!,
        };
        MethodInfo? fx = SaveDataType.GetMethod("DeserializeFxEventCollection", All);
        if (fx != null)
        {
            readers.Add(fx);
        }

        MethodInfo toggle = Assembly.Load("ChromaGLS").GetType("ChromaGLS.Plugin", true)!
            .GetMethod("SetEnabled", All)!;
        foreach (MethodInfo reader in readers)
        {
            Assert.That(Harmony.GetPatchInfo(reader)?.Owners, Does.Contain("com.eklipz.ChromaGLS"), reader.Name);
        }

        toggle.Invoke(null, [false]);
        foreach (MethodInfo reader in readers)
        {
            Assert.That(Harmony.GetPatchInfo(reader)?.Owners, Does.Contain("com.eklipz.ChromaGLS"), reader.Name);
        }

        toggle.Invoke(null, [true]);
        foreach (MethodInfo reader in readers)
        {
            Assert.That(Harmony.GetPatchInfo(reader)?.Owners, Does.Contain("com.eklipz.ChromaGLS"), reader.Name);
        }
    }
}
