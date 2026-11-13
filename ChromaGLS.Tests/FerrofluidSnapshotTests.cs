using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

// The F12 snapshot pipeline is tweak-build-only, so every production assertion here also guards
// that the normal binary stays slim: no snapshot types, no snapshot members, and no new module
// references such as ScreenCapture/ImageConversion/GUIUtility/System.Diagnostics.
[TestFixture]
internal sealed class FerrofluidSnapshotTests
{
    private static bool TweaksEnabled
    {
        get
        {
            FieldInfo? flag = GetPluginType("ChromaGLS.Settings.ChromaGLSSettingsUI")
                .GetField("FerrofluidTweakMenuEnabled", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(flag, Is.Not.Null, "ChromaGLSSettingsUI.FerrofluidTweakMenuEnabled must exist as the build flag.");
            return (bool)flag!.GetRawConstantValue()!;
        }
    }

    private static string TestRoot
    {
        get
        {
            // Dedicated scratch root: never the real %TEMP%\CglsFerroSnapshots capture directory.
            string root = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..", "..", "Scratch", "ferro-snapshots", "test-runs", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(root);
            return root;
        }
    }

    [Test]
    public void SnapshotTypesAndOwnerMembersExistExactlyInTweakBuilds()
    {
        bool tweaks = TweaksEnabled;
        Assembly plugin = Assembly.Load("ChromaGLS");
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (string name in new[]
        {
            "FerrofluidSnapshotLifecycle",
            "FerrofluidSnapshotBehaviour",
            "FerrofluidSnapshotCapture",
            "FerrofluidSnapshotStore",
            "FerrofluidSnapshotIo",
            "FerrofluidCaptureResult",
            "FerrofluidSnapshotData",
            "FerrofluidTorusChartGeometry",
        })
        {
            Assert.That(
                plugin.GetType("ChromaGLS.Settings." + name, throwOnError: false) != null,
                Is.EqualTo(tweaks),
                $"{name} must compile only in FERROFLUID_TWEAKS builds.");
        }

        Type simulation = GetPluginType("ChromaGLS.Settings.FerrofluidGpuSimulation");
        Assert.That(simulation.GetMethod("ExportSnapshot", all) != null, Is.EqualTo(tweaks), "The GPU export must exist only in tweak builds.");
        Type primitive = GetPluginType("ChromaGLS.Settings.FerrofluidPrimitiveEffect");
        Assert.That(primitive.GetMethod("BuildObjectSnapshot", all) != null, Is.EqualTo(tweaks));
        Assert.That(primitive.GetMethod("IsRendered", all) != null, Is.EqualTo(tweaks));
        Type requirement = GetPluginType("ChromaGLS.Settings.ChromaGlsRequirementEffect");
        Assert.That(requirement.GetMethod("BuildObjectSnapshot", all) != null, Is.EqualTo(tweaks));
        Type magnet = GetPluginType("ChromaGLS.Settings.FerrofluidMagnetController");
        Assert.That(magnet.GetMethod("ExportHands", all) != null, Is.EqualTo(tweaks));
        Assert.That(magnet.GetMethod("TryGetPointerRay", all) != null, Is.EqualTo(tweaks));
        Type grabbable = GetPluginType("ChromaGLS.Settings.FerrofluidGrabbable");
        Assert.That(grabbable.GetMethod("ExportGrabState", all) != null, Is.EqualTo(tweaks));
        bool hasRecorder = simulation.GetMethods(all).Any(m => m.Name.StartsWith("RecordSnapshot", StringComparison.Ordinal));
        Assert.That(hasRecorder, Is.EqualTo(tweaks), "Erased partial hook methods must exist only in tweak builds.");

        if (tweaks)
        {
            // The menu installer must bind the snapshot lifecycle so it lives and dies with the menu container.
            Type installer = GetPluginType("ChromaGLS.Settings.ChromaGLSMenuInstaller");
            MethodInfo install = installer.GetMethod("InstallBindings", all)!;
            bool bound = MethodReferencesType(install, "ChromaGLS.Settings.FerrofluidSnapshotLifecycle");
            Assert.That(bound, Is.True, "ChromaGLSMenuInstaller must bind FerrofluidSnapshotLifecycle in tweak builds.");
        }
        else
        {
            List<string> snapshotTypes = plugin.GetTypes()
                .Where(type => type.Name.Contains("Snapshot"))
                .Select(type => type.FullName!)
                .ToList();
            Assert.That(snapshotTypes, Is.Empty, "The normal binary must not contain any snapshot type.");
        }
    }

    [Test]
    public void SnapshotModuleReferencesExistExactlyInTweakBuilds()
    {
        bool tweaks = TweaksEnabled;
        Assembly plugin = Assembly.Load("ChromaGLS");
        List<string> hits = FindMemberReferences(plugin, new[]
        {
            "UnityEngine.ScreenCapture",
            "UnityEngine.ImageConversion",
            "UnityEngine.GUIUtility",
            "UnityEngine.Screen::",
            "System.Diagnostics.Process",
            "UnityEngine.Input::GetMouseButton",
            "UnityEngine.Input::GetKeyDownInt",
        });

        Assert.That(
            hits.Count > 0,
            Is.EqualTo(tweaks),
            tweaks
                ? "The tweak build must compile the F12/screenshot/clipboard snapshot calls."
                : $"The normal binary must not contain snapshot module references; found {string.Join(", ", hits)}.");

        // The PNG shell-launch must be a real Process.Start call in the tweak build, not a #if that
        // compiles out: assert the instruction specifically rather than the aggregate hit count.
        List<string> processStarts = hits.Where(hit => hit.EndsWith("System.Diagnostics.Process::Start", StringComparison.Ordinal)).ToList();
        Assert.That(
            processStarts.Count > 0,
            Is.EqualTo(tweaks),
            tweaks ? "OpenPath must launch the screenshot via Process.Start(UseShellExecute)." : "The normal binary must not spawn viewer processes.");
    }

    [Test]
    public void CaptureDirectoryStampIsUtcIsoAndUnique()
    {
        if (!TweaksEnabled)
            Assert.Ignore("Normal builds do not compile the snapshot store.");

        Type store = GetPluginType("ChromaGLS.Settings.FerrofluidSnapshotStore");
        MethodInfo format = store.GetMethod("FormatStamp", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        Assert.That(format, Is.Not.Null);
        DateTime utc = new(2026, 3, 4, 5, 6, 7, 890, DateTimeKind.Utc);
        utc = utc.AddTicks(1234);
        string stamp = (string)format.Invoke(null, [utc])!;
        Assert.That(stamp, Does.Match(@"^\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}\.\d{7}Z$"));
        Assert.That(stamp.IndexOfAny(Path.GetInvalidFileNameChars()), Is.EqualTo(-1), "The stamp must be a Windows-safe directory name.");

        string root = TestRoot;
        MethodInfo create = store.GetMethod("CreateCaptureDirectory", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        string first = (string)create.Invoke(null, [root, utc])!;
        string second = (string)create.Invoke(null, [root, utc])!;
        Assert.That(first, Is.Not.EqualTo(second), "A colliding stamp must produce a unique directory rather than overwriting.");
        Assert.That(Directory.Exists(first), Is.True);
        Assert.That(Directory.Exists(second), Is.True);
        Assert.That(Path.GetFileName(second), Does.StartWith(Path.GetFileName(first)));
    }

    [Test]
    public void DefaultRootUsesTempCglsFerroSnapshots()
    {
        if (!TweaksEnabled)
            Assert.Ignore("Normal builds do not compile the snapshot capture.");

        Type capture = GetPluginType("ChromaGLS.Settings.FerrofluidSnapshotCapture");
        string root = (string)capture.GetMethod("DefaultRootDirectory", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
            .Invoke(null, null)!;
        Assert.That(root, Is.EqualTo(Path.Combine(Path.GetTempPath(), "CglsFerroSnapshots")));
    }

    [Test]
    public void ShouldScheduleIgnoresKeyRepeatWhileBusy()
    {
        if (!TweaksEnabled)
            Assert.Ignore("Normal builds do not compile the snapshot capture.");

        MethodInfo schedule = GetPluginType("ChromaGLS.Settings.FerrofluidSnapshotCapture")
            .GetMethod("ShouldSchedule", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        Assert.That((bool)schedule.Invoke(null, [false, true])!, Is.True, "A fresh F12 press must schedule the capture.");
        Assert.That((bool)schedule.Invoke(null, [true, true])!, Is.False, "Held or repeated F12 must not schedule a second coroutine while busy.");
        Assert.That((bool)schedule.Invoke(null, [false, false])!, Is.False);
        Assert.That((bool)schedule.Invoke(null, [true, false])!, Is.False);
    }

    [Test]
    public void IsRenderedRequiresActiveRenderedAndNotFaded()
    {
        if (!TweaksEnabled)
            Assert.Ignore("Normal builds do not compile the snapshot capture.");

        MethodInfo rendered = GetPluginType("ChromaGLS.Settings.FerrofluidSnapshotCapture")
            .GetMethod("IsRendered", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        Assert.That((bool)rendered.Invoke(null, [true, true, 1f])!, Is.True);
        Assert.That((bool)rendered.Invoke(null, [false, true, 1f])!, Is.False, "Inactive objects are excluded.");
        Assert.That((bool)rendered.Invoke(null, [true, false, 1f])!, Is.False, "Disabled renderers are excluded.");
        Assert.That((bool)rendered.Invoke(null, [true, true, 0f])!, Is.False, "Fully faded renderers are excluded.");
        // Eligibility must never depend on camera visibility or frustum tests.
        Assert.That((bool)rendered.Invoke(null, [true, true, 0.5f])!, Is.True, "Off-screen but rendered objects still export.");
    }

    [Test]
    public void SuccessfulCaptureWritesScreenshotAndOneJsonPerObject()
    {
        if (!TweaksEnabled)
            Assert.Ignore("Normal builds do not compile the snapshot capture.");

        string root = TestRoot;
        DateTime utc = new DateTime(2026, 4, 5, 6, 7, 8, 123, DateTimeKind.Utc).AddTicks(4567);
        var io = NewIo(utc);
        var written = new List<(string Path, string? Text, byte[]? Bytes)>();
        SetIoDelegate(io, "WriteText", new Action<string, string>((path, text) =>
        {
            written.Add((path, text, null));
            File.WriteAllText(path, text);
        }));
        SetIoDelegate(io, "WriteBytes", new Action<string, byte[]>((path, bytes) =>
        {
            written.Add((path, null, bytes));
            File.WriteAllBytes(path, bytes);
        }));

        var clipboard = new List<string>();
        SetIoDelegate(io, "SetClipboardText", new Action<string>(clipboard.Add));
        var opened = new List<string>();
        SetIoDelegate(io, "OpenFile", new Action<string>(opened.Add));

        JObject shared = new() { ["schemaVersion"] = 1, ["capturedUtc"] = utc.ToString("O"), ["frame"] = 42 };
        JObject[] subjects =
        [
            new JObject { ["name"] = "ChromaGLS Ferrofluid Sphere", ["shape"] = "Sphere" },
            new JObject { ["name"] = "ChromaGLS Ferrofluid Requirement Border", ["shape"] = "Torus" },
        ];
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

        object result = InvokeWriteCapture(root, utc, shared, subjects, png, io)!;
        string directory = (string)ReadField(result, "Directory")!;

        Assert.That(Directory.Exists(directory), Is.True);
        Assert.That(Path.GetFileName(directory), Does.Match(@"^\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}\.\d{7}Z"));
        string pngPath = Path.Combine(directory, "screenshot.png");
        Assert.That(File.Exists(pngPath), Is.True, "Each successful capture writes exactly one screenshot.png.");
        CollectionAssert.AreEqual(png, File.ReadAllBytes(pngPath));

        List<string> jsonFiles = Directory.GetFiles(directory, "*.json").OrderBy(path => path).ToList();
        Assert.That(jsonFiles.Count, Is.EqualTo(subjects.Length), "One JSON must be written per exported ferro object.");
        foreach ((string path, int index) in jsonFiles.Select((path, index) => (path, index)))
        {
            JObject body = JObject.Parse(File.ReadAllText(path));
            Assert.That(
                body["shared"]!.ToString(Newtonsoft.Json.Formatting.None),
                Is.EqualTo(shared.ToString(Newtonsoft.Json.Formatting.None)),
                $"Every object JSON embeds the identical same-frame shared context ({path}).");
            Assert.That(body["subject"]!["name"]!.Value<string>(), Is.EqualTo(subjects[index]["name"]!.Value<string>()));
        }

        Assert.That(clipboard, Is.EqualTo(new[] { directory }), "The clipboard receives the absolute capture folder after a successful write.");
        Assert.That(opened, Is.EqualTo(new[] { pngPath }), "The PNG opens in the shell viewer after a successful write.");
    }

    [Test]
    public void FailedWriteRemovesCaptureDirectoryAndSkipsSideEffects()
    {
        if (!TweaksEnabled)
            Assert.Ignore("Normal builds do not compile the snapshot capture.");

        string root = TestRoot;
        Directory.CreateDirectory(Path.Combine(root, "2020-01-01T00-00-00.0000000Z"));
        Directory.CreateDirectory(Path.Combine(root, "unrelated-dir"));

        var io = NewIo(new DateTime(2026, 4, 5, 6, 7, 8, 123, DateTimeKind.Utc).AddTicks(4567));
        int writes = 0;
        SetIoDelegate(io, "WriteText", new Action<string, string>((path, text) =>
        {
            writes++;
            if (writes == 2)
                throw new IOException("simulated disk failure");
            File.WriteAllText(path, text);
        }));
        var clipboard = new List<string>();
        SetIoDelegate(io, "SetClipboardText", new Action<string>(clipboard.Add));

        JObject shared = new() { ["schemaVersion"] = 1 };
        JObject[] subjects =
        [
            new JObject { ["name"] = "a" },
            new JObject { ["name"] = "b" },
        ];

        Assert.Throws<IOException>(
            () => Unwrap(() => InvokeWriteCapture(root, DateTime.UtcNow, shared, subjects, [1, 2, 3], io)),
            "A failed write must abort the capture.");

        Assert.That(Directory.GetDirectories(root).Length, Is.EqualTo(2), "The partially written capture directory must be removed and no older directory pruned.");
        Assert.That(Directory.Exists(Path.Combine(root, "unrelated-dir")), Is.True);
        Assert.That(clipboard, Is.Empty, "Clipboard must not run when the capture failed.");
    }

    [Test]
    public void ViewerAndClipboardFailuresKeepCaptureSuccessful()
    {
        if (!TweaksEnabled)
            Assert.Ignore("Normal builds do not compile the snapshot capture.");

        string root = TestRoot;
        var io = NewIo(DateTime.UtcNow);
        SetIoDelegate(io, "OpenFile", new Action<string>(_ => throw new InvalidOperationException("no viewer")));
        SetIoDelegate(io, "SetClipboardText", new Action<string>(_ => throw new InvalidOperationException("no clipboard")));

        object result = InvokeWriteCapture(
            root,
            DateTime.UtcNow,
            new JObject { ["schemaVersion"] = 1 },
            [new JObject { ["name"] = "cube" }],
            [9, 9],
            io)!;

        string directory = (string)ReadField(result, "Directory")!;
        Assert.That(File.Exists(Path.Combine(directory, "screenshot.png")), Is.True, "A viewer failure must not discard a valid capture.");
        Assert.That(Directory.GetFiles(directory, "*.json").Length, Is.EqualTo(1));
    }

    [Test]
    public void PruningKeepsNewestTwentyAndPreservesUnrelatedAndReparse()
    {
        if (!TweaksEnabled)
            Assert.Ignore("Normal builds do not compile the snapshot store.");

        string root = TestRoot;
        for (int index = 0; index < 23; index++)
        {
            string name = $"2026-02-{index + 1:D2}T01-02-03.000000{index % 10}Z";
            Directory.CreateDirectory(Path.Combine(root, name));
        }

        string unrelated = Path.Combine(root, "keep-me-not-a-capture");
        Directory.CreateDirectory(unrelated);
        File.WriteAllText(Path.Combine(unrelated, "note.txt"), "stay");

        // A reparse point nested inside a doomed capture must be unlinked, never traversed.
        string junction = Path.Combine(root, "2026-02-01T01-02-03.0000000Z", "junction");
        bool junctionCreated = TryCreateJunction(junction, unrelated);
        string reparseCapture = Path.Combine(root, "2026-02-24T01-02-03.0000000Z");
        bool junctionCaptureCreated = TryCreateJunction(reparseCapture, unrelated);

        Type store = GetPluginType("ChromaGLS.Settings.FerrofluidSnapshotStore");
        store.GetMethod("PruneCaptureDirectories", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
            .Invoke(null, [root, 20]);

        List<string> remaining = Directory.GetDirectories(root).Select(Path.GetFileName).OrderBy(name => name).ToList();
        int captureDirs = remaining.Count(name =>
            IsCaptureName(name!)
            && (new DirectoryInfo(Path.Combine(root, name!)).Attributes & FileAttributes.ReparsePoint) == 0);
        Assert.That(captureDirs, Is.EqualTo(20), $"Only the newest 20 real timestamp directories may remain, saw {remaining.Count}.");
        Assert.That(remaining, Does.Not.Contain("2026-02-01T01-02-03.0000000Z"));
        Assert.That(remaining, Does.Not.Contain("2026-02-02T01-02-03.0000000Z"));
        Assert.That(remaining, Does.Not.Contain("2026-02-03T01-02-03.0000000Z"));
        Assert.That(remaining, Does.Contain("2026-02-23T01-02-03.0000002Z"));
        Assert.That(Directory.Exists(unrelated), Is.True, "Unrelated directories must never be pruned.");
        Assert.That(File.Exists(Path.Combine(unrelated, "note.txt")), Is.True, "The reparse target must survive pruning of the doomed capture.");
        if (junctionCaptureCreated)
        {
            Assert.That(Directory.Exists(reparseCapture), Is.True, "A timestamp-named reparse point is not owned by the captures and must be skipped.");
        }

        if (!junctionCreated && !junctionCaptureCreated)
        {
            Assert.Inconclusive("mklink /J could not create a junction in this environment; reparse assertions could not run.");
        }
    }

    [Test]
    public void SpikeDecodingRecordsEveryChannel()
    {
        if (!TweaksEnabled)
            Assert.Ignore("Normal builds do not compile the spike decoder.");

        Type data = GetPluginType("ChromaGLS.Settings.FerrofluidSnapshotData");
        MethodInfo decode = data.GetMethod("DecodeSpikeList", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        object sphereShape = Enum.ToObject(GetPluginType("ChromaGLS.Settings.FerrofluidSurfaceShape"), 2);
        Color[] state =
        [
            new Color(0.1f, 0.2f, 0.0000005f, 0f),
            new Color(-0.3f, 0.4f, 1.5f, -2f),
        ];
        Color[] rest =
        [
            new Color(-0.1f, -0.2f, 3f, 0f),
            new Color(-0.5f, -0.6f, 4f, 0f),
        ];
        Color[] spring =
        [
            new Color(0.05f, 0.5f, 0f, 0f),
            new Color(-0.02f, -1f, 0f, 0f),
        ];
        Color[] siteSurface =
        [
            new Color(0f, 0.5f, 0f, 0.2f),
            new Color(0.5f, 0f, 0f, 0.3f),
        ];
        Color[] siteDeformation =
        [
            new Color(0f, 1f, 0f, 0.7f),
            new Color(0f, -1f, 0f, 0.9f),
        ];
        Color[] siteBulge =
        [
            new Color(0f, 0f, 0f, 0.01f),
            new Color(0f, 0f, 0f, -0.02f),
        ];

        JArray spikes = (JArray)decode.Invoke(
            null,
            [sphereShape, 2, state, rest, spring, siteSurface, siteDeformation, siteBulge, 0.2f, 0f, null])!;

        Assert.That(spikes.Count, Is.EqualTo(2));
        JToken first = spikes[0];
        Assert.That(first["index"]!.Value<int>(), Is.EqualTo(0));
        Assert.That(first["chartPosition"]!.Values<float>().ToArray(), Is.EqualTo(new[] { 0.1f, 0.2f }));
        Assert.That(first["chartVelocityPerSecond"]!.Type, Is.EqualTo(JTokenType.Null), "Near-zero chart velocity must be omitted as null.");
        Assert.That(first["homeChartPosition"]!.Values<float>().ToArray(), Is.EqualTo(new[] { -0.1f, -0.2f }));
        Assert.That(first["restDensity"]!.Value<float>(), Is.EqualTo(3f));
        Assert.That(first["springHeightScale"]!.Value<float>(), Is.EqualTo(0.05f), "Spring state is a dimensionless scale, not an additive height.");
        Assert.That(first["springHeightScaleVelocityPerSecond"]!.Value<float>(), Is.EqualTo(0.5f));
        Assert.That(first["siteSpacing"]!.Value<float>(), Is.EqualTo(0.2f));
        Assert.That(first["localBasePosition"]!.Values<float>().ToArray(), Is.EqualTo(new[] { 0f, 0.5f, 0f }), "The live site surface texel is the spike base.");
        Assert.That(first["deformationDirectionWorld"]!.Values<float>().ToArray(), Is.EqualTo(new[] { 0f, 1f, 0f }), "Site deformation stores the world-space spike direction.");
        Assert.That(first["apexSharpening"]!.Value<float>(), Is.EqualTo(0.7f));
        Assert.That(first["densityBulgeHeightWorld"]!.Value<float>(), Is.EqualTo(0.01f));
        Assert.That(first["localHomePosition"]!.Type, Is.Not.EqualTo(JTokenType.Null));
        Assert.That(first["localSurfaceNormal"]!.Type, Is.Not.EqualTo(JTokenType.Null));
        Assert.That(first["spikeOrientation"]!.Type, Is.Not.EqualTo(JTokenType.Null));

        JToken second = spikes[1];
        Assert.That(second["chartVelocityPerSecond"]!.Values<float>().ToArray(), Is.EqualTo(new[] { 1.5f, -2f }), "Non-trivial chart velocity must be recorded.");
        Assert.That(second["densityBulgeHeightWorld"]!.Value<float>(), Is.EqualTo(-0.02f));
    }

    [Test]
    public void TorusSpikeBaseMappingUsesCenterlineGeometry()
    {
        if (!TweaksEnabled)
            Assert.Ignore("Normal builds do not compile the spike decoder.");

        Type data = GetPluginType("ChromaGLS.Settings.FerrofluidSnapshotData");
        MethodInfo decode = data.GetMethod("DecodeSpikeList", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        Type geometryType = GetPluginType("ChromaGLS.Settings.FerrofluidTorusChartGeometry");
        object torusChart = Activator.CreateInstance(
            geometryType,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null,
            [new Vector2(0f, 0f), new Vector2(5f, 2f), 1f, -1f, 1.5f],
            null)!;
        object torusShape = Enum.ToObject(GetPluginType("ChromaGLS.Settings.FerrofluidSurfaceShape"), 0);
        Color[] state = [new Color(0.4f, -0.2f, 0f, 0f)];

        JArray spikes = (JArray)decode.Invoke(
            null,
            [torusShape, 1, state, null, null, null, null, null, 0.15f, 0f, torusChart])!;

        JToken spike = spikes[0];
        float[] basePosition = spike["localBasePosition"]!.Values<float>().ToArray();

        // Reproduce the shader's torus site basis: perimeter centerline + outward radial offset + tube depth.
        Type simulation = GetPluginType("ChromaGLS.Settings.FerrofluidGpuSimulation");
        MethodInfo centerline = simulation.GetMethod("EvaluateRoundedRectangleCenterline", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo outward = simulation.GetMethod("EvaluateRoundedRectangleOutward", BindingFlags.Static | BindingFlags.NonPublic)!;
        float radialCenter = (float)simulation.GetField("RadialCenter", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.GetRawConstantValue()!;
        float radialRadius = (float)simulation.GetField("RadialRadius", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.GetRawConstantValue()!;
        Vector2 siteUv = new((0.4f * 0.5f) + 0.5f, (-0.2f * 0.5f) + 0.5f);
        Vector2 center = (Vector2)centerline.Invoke(null, [siteUv.x, new Vector2(0f, 0f), new Vector2(5f, 2f), 1f])!;
        Vector2 outwardDirection = (Vector2)outward.Invoke(null, [center, new Vector2(0f, 0f), new Vector2(5f, 2f), 1f])!;
        float angle = (siteUv.y - Mathf.Floor(siteUv.y)) * Mathf.PI * 2f;
        float radial = radialCenter + (radialRadius * Mathf.Cos(angle));
        Vector3 expected = new(
            center.x + (outwardDirection.x * radial),
            center.y + (outwardDirection.y * radial),
            -1f * 1.5f * Mathf.Sin(angle));

        Assert.That(Vector3.Distance(new Vector3(basePosition[0], basePosition[1], basePosition[2]), expected), Is.LessThan(0.001f));
        Assert.That(spike["localSurfaceNormal"]!.Type, Is.Not.EqualTo(JTokenType.Null));
    }

    [Test]
    public void ConfigDumpCapturesScalarProperties()
    {
        if (!TweaksEnabled)
            Assert.Ignore("Normal builds do not compile the config dump.");

        Type configType = GetPluginType("ChromaGLS.Settings.Config");
        object config = Activator.CreateInstance(configType, nonPublic: true)!;
        configType.GetProperty("FerrofluidSpikeDensity")!.SetValue(config, 0.77f);
        configType.GetProperty("GasConstant")!.SetValue(config, 99f);
        MethodInfo dump = GetPluginType("ChromaGLS.Settings.FerrofluidSnapshotData")
            .GetMethod("ConfigToJObject", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        JObject json = (JObject)dump.Invoke(null, [config])!;
        Assert.That(json["FerrofluidSpikeDensity"]!.Value<float>(), Is.EqualTo(0.77f).Within(0.0001f));
        Assert.That(json["GasConstant"]!.Value<float>(), Is.EqualTo(99f));
        Assert.That(json["FerrofluidCubeEnabled"]!.Value<bool>(), Is.EqualTo(false));
    }

    [Test]
    public void PoseAndMatrixSerializeAsPlainNumbers()
    {
        if (!TweaksEnabled)
            Assert.Ignore("Normal builds do not compile the snapshot DTOs.");

        Type poseType = GetPluginType("ChromaGLS.Settings.FerrofluidSnapshotPose");
        MethodInfo from = poseType.GetMethod("From", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        object pose = from.Invoke(null, [new Vector3(1f, 2f, 3f), Quaternion.identity])!;
        JObject json = JObject.FromObject(pose);

        Assert.That(json["Position"]!["X"]!.Value<float>(), Is.EqualTo(1f));
        Assert.That(json["Rotation"]!["W"]!.Value<float>(), Is.EqualTo(1f));
        Assert.That(
            json["Rotation"]!.ToObject<Dictionary<string, JToken>>()!.Keys.OrderBy(key => key),
            Is.EqualTo(new[] { "W", "X", "Y", "Z" }),
            "The quaternion DTO must contain only its four components, never Unity's recursive computed properties.");
        Assert.That(
            json["Position"]!.ToObject<Dictionary<string, JToken>>()!.Keys.OrderBy(key => key),
            Is.EqualTo(new[] { "X", "Y", "Z" }));

        Type matrixType = GetPluginType("ChromaGLS.Settings.FerrofluidSnapshotMatrix4x4");
        MethodInfo matrixFrom = matrixType.GetMethod("From", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        Matrix4x4 matrix = new();
        matrix.SetRow(0, new Vector4(1f, 2f, 3f, 4f));
        matrix.SetRow(1, new Vector4(5f, 6f, 7f, 8f));
        matrix.SetRow(2, new Vector4(9f, 10f, 11f, 12f));
        matrix.SetRow(3, new Vector4(13f, 14f, 15f, 16f));
        object snapshot = matrixFrom.Invoke(null, [matrix])!;
        JObject matrixJson = JObject.FromObject(snapshot);
        Assert.That(
            matrixJson["M"]!.Values<float>().ToArray(),
            Is.EqualTo(new[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f, 11f, 12f, 13f, 14f, 15f, 16f }),
            "Matrices export row-major m00..m33.");
    }

    [Test]
    public void EffectiveTriggerReportsFullPullForFpfcMouseHold()
    {
        if (!TweaksEnabled)
            Assert.Ignore("Normal builds do not compile the trigger conversion.");

        MethodInfo effective = GetPluginType("ChromaGLS.Settings.FerrofluidMagnetController")
            .GetMethod("EvaluateEffectiveTrigger", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        Assert.That((float)effective.Invoke(null, [true, true, 0f])!, Is.EqualTo(1f), "FPFC mouse hold must export a full trigger pull.");
        Assert.That((float)effective.Invoke(null, [true, false, 0.4f])!, Is.EqualTo(0f), "FPFC without a held button is a released trigger regardless of the sampled value.");
        Assert.That((float)effective.Invoke(null, [false, false, 0.35f])!, Is.EqualTo(0.35f), "VR keeps its analog pull (0.35 -> 35%).");
        Assert.That((float)effective.Invoke(null, [false, true, 0.35f])!, Is.EqualTo(0.35f));
    }

    [Test]
    public void FailedScreenshotRefusesPublishAndPrune()
    {
        if (!TweaksEnabled)
            Assert.Ignore("Normal builds do not compile the snapshot capture.");

        string root = TestRoot;
        Directory.CreateDirectory(Path.Combine(root, "2020-01-01T00-00-00.0000000Z"));
        var io = NewIo(DateTime.UtcNow);
        var clipboard = new List<string>();
        SetIoDelegate(io, "SetClipboardText", new Action<string>(clipboard.Add));
        var opened = new List<string>();
        SetIoDelegate(io, "OpenFile", new Action<string>(opened.Add));

        Assert.Throws<InvalidOperationException>(
            () => Unwrap(() => InvokeWriteCapture(
                root,
                DateTime.UtcNow,
                new JObject { ["schemaVersion"] = 1 },
                [new JObject { ["name"] = "cube" }],
                null,
                io)),
            "A failed screenshot must abort the capture before any directory exists.");

        Assert.That(Directory.GetDirectories(root).Length, Is.EqualTo(1), "No capture directory may be created or pruned for a failed screenshot.");
        Assert.That(Directory.Exists(Path.Combine(root, "2020-01-01T00-00-00.0000000Z")), Is.True);
        Assert.That(clipboard, Is.Empty);
        Assert.That(opened, Is.Empty);
    }

    [Test]
    public void PruningRanksCollisionSuffixesNumerically()
    {
        if (!TweaksEnabled)
            Assert.Ignore("Normal builds do not compile the snapshot store.");

        string root = TestRoot;
        // 22 directories sharing one stamp plus an older stamp: ordinal sort would delete
        // "..._10" before "..._2"; numeric suffix order must keep the newest 20.
        Directory.CreateDirectory(Path.Combine(root, "2026-02-01T01-02-03.0000000Z"));
        for (int suffix = 2; suffix <= 23; suffix++)
        {
            Directory.CreateDirectory(Path.Combine(root, $"2026-02-02T01-02-03.0000000Z_{suffix}"));
        }

        Directory.CreateDirectory(Path.Combine(root, "2026-02-02T01-02-03.0000000Z"));

        Type store = GetPluginType("ChromaGLS.Settings.FerrofluidSnapshotStore");
        store.GetMethod("PruneCaptureDirectories", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
            .Invoke(null, [root, 20]);

        List<string> remaining = Directory.GetDirectories(root).Select(Path.GetFileName).ToList();
        Assert.That(remaining.Count, Is.EqualTo(20));
        Assert.That(remaining, Does.Contain("2026-02-02T01-02-03.0000000Z_23"), "The newest collision suffix must survive.");
        Assert.That(remaining, Does.Contain("2026-02-02T01-02-03.0000000Z_22"));
        Assert.That(remaining, Does.Not.Contain("2026-02-01T01-02-03.0000000Z"), "The older stamp is deleted first.");
        Assert.That(remaining, Does.Not.Contain("2026-02-02T01-02-03.0000000Z"), "The base name sorts before its collision suffixes.");
        Assert.That(remaining, Does.Not.Contain("2026-02-02T01-02-03.0000000Z_2"));
        Assert.That(remaining, Does.Not.Contain("2026-02-02T01-02-03.0000000Z_3"));
        Assert.That(remaining, Does.Contain("2026-02-02T01-02-03.0000000Z_10"), "Ordinal sort would have deleted _10 before _2; numeric ordering keeps it.");
    }

    [Test]
    public void ParsedCaptureStampRoundTripsUtc()
    {
        if (!TweaksEnabled)
            Assert.Ignore("Normal builds do not compile the snapshot store.");

        Type store = GetPluginType("ChromaGLS.Settings.FerrofluidSnapshotStore");
        MethodInfo parse = store.GetMethod("TryParseCaptureName", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        DateTime utc = new(2026, 3, 4, 5, 6, 7, 890, DateTimeKind.Utc);
        utc = utc.AddTicks(1234);
        string stamp = (string)store.GetMethod("FormatStamp", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
            .Invoke(null, [utc])!;
        object?[] arguments = { stamp + "_12", null, null };
        Assert.That((bool)parse.Invoke(null, arguments)!, Is.True);
        DateTime parsed = (DateTime)arguments[1]!;
        Assert.That(parsed, Is.EqualTo(utc), "The parsed stamp must equal the original UTC instant, not a relabeled local time.");
        Assert.That(parsed.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That((int)arguments[2]!, Is.EqualTo(12));
    }

    private static bool IsCaptureName(string name)
    {
        Type store = GetPluginType("ChromaGLS.Settings.FerrofluidSnapshotStore");
        return (bool)store.GetMethod("IsCaptureDirectoryName", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
            .Invoke(null, [name])!;
    }

    private static bool TryCreateJunction(string link, string target)
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            using System.Diagnostics.Process? process = System.Diagnostics.Process.Start(info);
            process!.WaitForExit(10000);
            return process.ExitCode == 0 && Directory.Exists(link);
        }
        catch
        {
            return false;
        }
    }

    private static object NewIo(DateTime utc)
    {
        Type ioType = GetPluginType("ChromaGLS.Settings.FerrofluidSnapshotIo");
        object io = Activator.CreateInstance(ioType, nonPublic: true)!;
        SetIoDelegate(io, "Now", new Func<DateTime>(() => utc));
        return io;
    }

    private static void SetIoDelegate(object io, string memberName, Delegate value)
    {
        Type type = io.GetType();
        FieldInfo? field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field != null)
        {
            field.SetValue(io, value);
            return;
        }

        PropertyInfo? property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.That(property?.CanWrite ?? false, Is.True, $"FerrofluidSnapshotIo.{memberName} must exist and be writable.");
        property!.SetValue(io, value);
    }

    private static object? InvokeWriteCapture(string root, DateTime utc, JObject shared, JObject[] subjects, byte[]? png, object io)
    {
        Type capture = GetPluginType("ChromaGLS.Settings.FerrofluidSnapshotCapture");
        MethodInfo write = capture.GetMethod("WriteCapture", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        return write.Invoke(null, [root, utc, shared, subjects, png, io]);
    }

    private static void Unwrap(Action action)
    {
        try
        {
            action();
        }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            throw error.InnerException;
        }
    }

    private static object? ReadField(object instance, string name)
    {
        Type type = instance.GetType();
        FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field != null)
            return field.GetValue(instance);
        return type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .GetValue(instance);
    }

    private static bool MethodReferencesType(MethodInfo method, string typeName)
    {
        using AssemblyDefinition definition = AssemblyDefinition.ReadAssembly(method.Module.Assembly.Location);
        MethodDefinition? target = definition.MainModule.Types
            .SelectMany(AllTypes)
            .SelectMany(type => type.Methods)
            .FirstOrDefault(candidate => candidate.Name == method.Name && candidate.DeclaringType.FullName == method.DeclaringType!.FullName.Replace('+', '/'));
        if (target == null || !target.HasBody)
            return false;

        foreach (Instruction instruction in target.Body.Instructions)
        {
            if (instruction.Operand is TypeReference reference && reference.FullName.Replace('/', '+') == typeName)
                return true;
            // BindInterfacesTo<T> carries the bound type only in the generic instance operand.
            if (instruction.Operand is GenericInstanceMethod generic
                && generic.GenericArguments.Any(argument => argument.FullName.Replace('/', '+') == typeName))
                return true;
        }

        return false;
    }

    private static IEnumerable<TypeDefinition> AllTypes(TypeDefinition root)
    {
        yield return root;
        foreach (TypeDefinition nested in root.NestedTypes)
        {
            foreach (TypeDefinition child in AllTypes(nested))
                yield return child;
        }
    }

    private static List<string> FindMemberReferences(Assembly assembly, string[] prefixes)
    {
        var hits = new List<string>();
        using AssemblyDefinition definition = AssemblyDefinition.ReadAssembly(assembly.Location);
        var types = new Stack<TypeDefinition>(definition.MainModule.Types);
        while (types.Count > 0)
        {
            TypeDefinition type = types.Pop();
            foreach (TypeDefinition nested in type.NestedTypes)
                types.Push(nested);

            foreach (MethodDefinition method in type.Methods)
            {
                if (!method.HasBody)
                    continue;

                foreach (Instruction instruction in method.Body.Instructions)
                {
                    string? signature = instruction.Operand switch
                    {
                        MethodReference callee => callee.DeclaringType.FullName + "::" + callee.Name,
                        FieldReference field => field.DeclaringType.FullName + "::" + field.Name,
                        _ => null,
                    };
                    if (signature != null && prefixes.Any(prefix => signature.StartsWith(prefix, StringComparison.Ordinal)))
                        hits.Add($"{type.Name}::{method.Name}->{signature}");
                }
            }
        }

        return hits;
    }

    private static Type GetPluginType(string typeName)
    {
        return Assembly.Load("ChromaGLS").GetType(typeName, throwOnError: false)
            ?? throw new AssertionException($"Missing plugin type: {typeName}");
    }
}
