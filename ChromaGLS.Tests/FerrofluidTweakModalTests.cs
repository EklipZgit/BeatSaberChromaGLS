using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using NUnit.Framework;

namespace ChromaGLS.Tests;

// The TWEAK FERRO modal holds every ferro control behind one button on the tweaks tab;
// the root BSML keeps only the toggles, and the edit session previews without persisting.
[TestFixture]
[NonParallelizable]
internal sealed class FerrofluidTweakModalTests
{
    private static Assembly Plugin => Assembly.Load("ChromaGLS");

    [Test]
    public void TweakTabOpensFerroModalAfterSphereRow()
    {
        XElement root = LoadBsml("ChromaGLS.Settings.modifiers.bsml");
        List<XElement> controls = root.Descendants().ToList();
        int sphereRow = controls.FindIndex(e => e.Attribute("value")?.Value == "ferrofluid-sphere-enabled");
        Assert.That(sphereRow, Is.GreaterThanOrEqualTo(0), "Sphere enable row missing in tweaks root.");
        List<XElement> after = controls.Skip(sphereRow + 1).ToList();
        XElement? ferroOpen = after.FirstOrDefault(e => e.Attribute("on-click")?.Value == "ferro-open");
        Assert.That(ferroOpen, Is.Not.Null, "TWEAK FERRO button must follow the sphere row.");
        Assert.That((string)ferroOpen!.Attribute("text"), Is.EqualTo("TWEAK FERRO"));
        int floorIndex = controls.FindIndex(e => e.Attribute("id")?.Value == "menu-floor-light-tweak");
        Assert.That(floorIndex, Is.GreaterThan(controls.IndexOf(ferroOpen)), "Floor light row must stay below the TWEAK FERRO button.");
    }

    [Test]
    public void TweakRootHasNoFerroSlidersAndModalExists()
    {
        XElement root = LoadBsml("ChromaGLS.Settings.modifiers.bsml");
        List<string> sliders = root.Descendants()
            .Where(e => e.Name.LocalName == "slider-setting")
            .Select(e => (string)e.Attribute("text") ?? "")
            .Where(text => text.StartsWith("FERRO", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.That(sliders, Is.Empty, $"Ferro sliders must live in the modal, not the root view: {string.Join(",", sliders)}");
        Assert.That(
            root.Descendants().Where(e => e.Name.LocalName == "slider-setting"),
            Is.Empty,
            "Every remaining root slider is a ferro control and must live in the modal.");

        using Stream? stream = Plugin.GetManifestResourceStream("ChromaGLS.Settings.ferrofluid-settings.bsml");
        Assert.That(stream, Is.Not.Null, "Embedded ferrofluid-settings.bsml modal missing.");
        XElement modal = XDocument.Load(stream!).Root!;
        List<string> names = modal.Descendants().Select(e => e.Name.LocalName).ToList();
        Assert.That(names, Does.Contain("modal").And.Contain("vertical"), "Modal must wrap a scrollable settings panel.");
        foreach (string action in new[] { "ferro-save", "ferro-cancel", "ferro-reset" })
        {
            Assert.That(
                modal.Descendants().Any(e => e.Attribute("on-click")?.Value == action),
                Is.True,
                $"Modal must expose a {action} button.");
        }

        string[] retained = { "spike", "home", "density", "base", "strength", "saturation", "film" };
        string markup = modal.ToString(SaveOptions.DisableFormatting);
        foreach (string token in retained)
        {
            Assert.That(
                markup,
                Does.Contain(token),
                $"Modal must retain a control binding for '{token}'.");
        }
    }

    [Test]
    public void MinimalViewKeepsSevenTogglesAndNoFerroOpen()
    {
        XElement root = LoadBsml("ChromaGLS.Settings.modifiers-minimal.bsml");
        Assert.That(
            root.Descendants().Any(e => e.Attribute("on-click")?.Value == "ferro-open"),
            Is.False,
            "Minimal view must not expose the TWEAK FERRO button.");
        Assert.That(
            root.Descendants().Count(e => e.Name.LocalName == "bool-setting"),
            Is.EqualTo(7),
            "Minimal view keeps exactly the seven public toggles.");
    }

    [Test]
    public void RoundedCubeChartYawKeepsApprovedOrientation()
    {
        Type configType = Plugin.GetType("ChromaGLS.Settings.Config", throwOnError: true)!;
        object config = Activator.CreateInstance(configType, nonPublic: true)!;
        PropertyInfo? yaw = configType.GetProperty("FerrofluidCubeChartYaw", BindingFlags.Instance | BindingFlags.Public);
        yaw?.SetValue(config, 70f);
        Type shapeType = Plugin.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", throwOnError: true)!;
        object cube = Enum.Parse(shapeType, "RoundedCube");
        MethodInfo method = Plugin.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", throwOnError: true)!
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(m => m.Name == "EvaluateChartYawDegrees")
            ?? throw new Exception("EvaluateChartYawDegrees missing.");
        object result = method.Invoke(null, new[] { cube, config })!;
        Assert.That((float)result, Is.EqualTo(45f), "Rounded cube chart yaw must keep the approved 45-degree orientation.");
    }

    [Test]
    public void CubeFloorPhysicsControlsPreviewLiveAndCancel()
    {
        XElement root = LoadBsml("ChromaGLS.Settings.ferrofluid-settings.bsml");
        XElement? floorGroup = root.Descendants("vertical")
            .FirstOrDefault(section => (string?)section.Elements().FirstOrDefault()?.Attribute("text") == "Cube floor physics");
        Assert.That(floorGroup, Is.Not.Null, "The Cube floor physics group must lead the modal.");
        Assert.That(
            (string?)floorGroup!.Elements("bool-setting").First().Attribute("value"),
            Is.EqualTo("ferrofluid-floor-assistance-enabled"),
            "the floor assistance toggle must lead the floor physics group");
        Assert.That(
            floorGroup!.Elements("slider-setting").Select(element => (string?)element.Attribute("value")).ToArray(),
            Is.EqualTo(new[]
            {
                "ferrofluid-cube-floor-friction",
                "ferrofluid-cube-corner-friction",
                "ferrofluid-cube-face-landing-strength",
                "ferrofluid-cube-face-damping-strength",
                "ferrofluid-cube-face-damping-exponent",
                "ferrofluid-cube-face-damping-start-angle",
            }),
            "the floor physics group must hold the grip and landing sliders ahead of the damping controls");
        foreach ((string binding, float maximum, float increment) in new[]
        {
            ("ferrofluid-cube-floor-friction", 0.5f, 0.005f),
            ("ferrofluid-cube-corner-friction", 0.05f, 0.001f),
            ("ferrofluid-cube-face-landing-strength", 1f, 0.05f),
            ("ferrofluid-cube-face-damping-strength", 10f, 0.05f),
            ("ferrofluid-cube-face-damping-exponent", 6f, 0.05f),
            ("ferrofluid-cube-face-damping-start-angle", 30f, 0.5f),
        })
        {
            XElement? slider = root.Descendants("slider-setting")
                .SingleOrDefault(element => (string?)element.Attribute("value") == binding);
            Assert.That(slider, Is.Not.Null, $"ferrofluid-settings.bsml is missing the {binding} slider");
            Assert.That((float)slider!.Attribute("min")!, Is.EqualTo(0f));
            Assert.That((float)slider.Attribute("max")!, Is.EqualTo(maximum));
            Assert.That((float)slider.Attribute("increment")!, Is.EqualTo(increment));
        }

        Type configType = Plugin.GetType("ChromaGLS.Settings.Config", true)!;
        Type uiType = Plugin.GetType("ChromaGLS.Settings.FerrofluidSettingsUI", true)!;
        object live = Activator.CreateInstance(configType, nonPublic: true)!;
        int applies = 0;
        object apply = CreateTypedCallback(configType, _ => applies++);
        object ui = Activator.CreateInstance(
            uiType,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null,
            new[] { live, apply },
            null)!;
        object session = uiType.GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui)!;
        MethodInfo open = session.GetType().GetMethod("Open", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        MethodInfo cancel = session.GetType().GetMethod("Cancel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        open.Invoke(session, null);

        uiType.GetProperty("FloorAssistanceEnabled")!.SetValue(ui, false);
        foreach ((string property, float value) in new (string, float)[]
        {
            ("CubeFloorFriction", 0.14f),
            ("CubeCornerFriction", 0.04f),
            ("CubeFaceLandingStrength", 0.7f),
            ("CubeFaceDampingStrength", 3f),
            ("CubeFaceDampingExponent", 3f),
            ("CubeFaceDampingStartAngle", 12f),
        })
        {
            PropertyInfo? setter = uiType.GetProperty(property);
            Assert.That(setter, Is.Not.Null, $"UI property {property} must exist");
            setter!.SetValue(ui, value);
        }

        Assert.That(applies, Is.EqualTo(7), "each control writes through the preview callback");
        Assert.That((bool)configType.GetProperty("FerrofluidFloorAssistanceEnabled")!.GetValue(live)!, Is.False);
        Assert.That((float)configType.GetProperty("FerrofluidCubeFloorFriction")!.GetValue(live)!, Is.EqualTo(0.14f));
        Assert.That((float)configType.GetProperty("FerrofluidCubeCornerFriction")!.GetValue(live)!, Is.EqualTo(0.04f));
        Assert.That((float)configType.GetProperty("FerrofluidCubeFaceLandingStrength")!.GetValue(live)!, Is.EqualTo(0.7f));
        Assert.That((float)configType.GetProperty("FerrofluidCubeFaceDampingStrength")!.GetValue(live)!, Is.EqualTo(3f));
        Assert.That((float)configType.GetProperty("FerrofluidCubeFaceDampingExponent")!.GetValue(live)!, Is.EqualTo(3f));
        Assert.That((float)configType.GetProperty("FerrofluidCubeFaceDampingStartAngle")!.GetValue(live)!, Is.EqualTo(12f));

        cancel.Invoke(session, null);
        Assert.That((bool)configType.GetProperty("FerrofluidFloorAssistanceEnabled")!.GetValue(live)!, Is.True);
        Assert.That((float)configType.GetProperty("FerrofluidCubeFloorFriction")!.GetValue(live)!, Is.EqualTo(0.25f));
        Assert.That((float)configType.GetProperty("FerrofluidCubeCornerFriction")!.GetValue(live)!, Is.EqualTo(0.01f));
        Assert.That((float)configType.GetProperty("FerrofluidCubeFaceLandingStrength")!.GetValue(live)!, Is.EqualTo(0.55f));
        Assert.That((float)configType.GetProperty("FerrofluidCubeFaceDampingStrength")!.GetValue(live)!, Is.EqualTo(1.2f), "cancel restores defaults");
        Assert.That((float)configType.GetProperty("FerrofluidCubeFaceDampingExponent")!.GetValue(live)!, Is.EqualTo(2f));
        Assert.That((float)configType.GetProperty("FerrofluidCubeFaceDampingStartAngle")!.GetValue(live)!, Is.EqualTo(10f));
        Assert.That(applies, Is.EqualTo(8), "cancel must reapply the opening values");

        open.Invoke(session, null);
        uiType.GetProperty("FloorAssistanceEnabled")!.SetValue(ui, false);
        uiType.GetProperty("CubeFloorFriction")!.SetValue(ui, 0.14f);
        uiType.GetProperty("CubeCornerFriction")!.SetValue(ui, 0.04f);
        uiType.GetProperty("CubeFaceLandingStrength")!.SetValue(ui, 0.7f);
        uiType.GetProperty("CubeFaceDampingStrength")!.SetValue(ui, 3f);
        uiType.GetProperty("CubeFaceDampingExponent")!.SetValue(ui, 3f);
        uiType.GetProperty("CubeFaceDampingStartAngle")!.SetValue(ui, 12f);
        Invoke(session, "Save");
        cancel.Invoke(session, null);
        Assert.That((bool)configType.GetProperty("FerrofluidFloorAssistanceEnabled")!.GetValue(live)!, Is.False);
        Assert.That((float)configType.GetProperty("FerrofluidCubeFloorFriction")!.GetValue(live)!, Is.EqualTo(0.14f));
        Assert.That((float)configType.GetProperty("FerrofluidCubeCornerFriction")!.GetValue(live)!, Is.EqualTo(0.04f));
        Assert.That((float)configType.GetProperty("FerrofluidCubeFaceLandingStrength")!.GetValue(live)!, Is.EqualTo(0.7f));
        Assert.That((float)configType.GetProperty("FerrofluidCubeFaceDampingStrength")!.GetValue(live)!, Is.EqualTo(3f));
        Assert.That((float)configType.GetProperty("FerrofluidCubeFaceDampingExponent")!.GetValue(live)!, Is.EqualTo(3f));
        Assert.That((float)configType.GetProperty("FerrofluidCubeFaceDampingStartAngle")!.GetValue(live)!, Is.EqualTo(12f));
        Assert.That(applies, Is.EqualTo(15), "saving or canceling a closed session must not reapply");

        open.Invoke(session, null);
        Invoke(session, "Reset");
        Assert.That((bool)configType.GetProperty("FerrofluidFloorAssistanceEnabled")!.GetValue(live)!, Is.True);
        Assert.That((float)configType.GetProperty("FerrofluidCubeFloorFriction")!.GetValue(live)!, Is.EqualTo(0.25f));
        Assert.That((float)configType.GetProperty("FerrofluidCubeCornerFriction")!.GetValue(live)!, Is.EqualTo(0.01f));
        Assert.That((float)configType.GetProperty("FerrofluidCubeFaceLandingStrength")!.GetValue(live)!, Is.EqualTo(0.55f));
        Assert.That((float)configType.GetProperty("FerrofluidCubeFaceDampingStrength")!.GetValue(live)!, Is.EqualTo(1.2f));
        Assert.That((float)configType.GetProperty("FerrofluidCubeFaceDampingExponent")!.GetValue(live)!, Is.EqualTo(2f));
        Assert.That((float)configType.GetProperty("FerrofluidCubeFaceDampingStartAngle")!.GetValue(live)!, Is.EqualTo(10f));
        Assert.That(applies, Is.EqualTo(16), "reset must apply all defaults live");
        ((IDisposable)ui).Dispose();

        Type ignoreType = typeof(IPA.Config.Stores.GeneratedStore).Assembly.GetType("IPA.Config.Stores.Attributes.IgnoreAttribute", true)!;
        foreach (string name in new[] { "FerrofluidFloorAssistanceEnabled", "FerrofluidCubeFloorFriction", "FerrofluidCubeCornerFriction", "FerrofluidCubeFaceLandingStrength", "FerrofluidCubeFaceDampingStrength", "FerrofluidCubeFaceDampingExponent", "FerrofluidCubeFaceDampingStartAngle" })
            Assert.That(configType.GetProperty(name)!.IsDefined(ignoreType, true), Is.True, $"{name} must be session-only");

        Type tweakSettings = Plugin.GetType("ChromaGLS.Settings.FerrofluidTweakSettings", true)!;
        MethodInfo copy = tweakSettings.GetMethod("Copy", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        object source = Activator.CreateInstance(configType, nonPublic: true)!;
        object target = Activator.CreateInstance(configType, nonPublic: true)!;
        configType.GetProperty("FerrofluidFloorAssistanceEnabled")!.SetValue(source, false);
        configType.GetProperty("FerrofluidCubeFloorFriction")!.SetValue(source, 0.08f);
        configType.GetProperty("FerrofluidCubeCornerFriction")!.SetValue(source, 0.04f);
        configType.GetProperty("FerrofluidCubeFaceLandingStrength")!.SetValue(source, 0.3f);
        copy.Invoke(null, new[] { source, target });
        Assert.That((bool)configType.GetProperty("FerrofluidFloorAssistanceEnabled")!.GetValue(target)!, Is.False, "the explicit tweak copy must carry floor assistance");
        Assert.That((float)configType.GetProperty("FerrofluidCubeFloorFriction")!.GetValue(target)!, Is.EqualTo(0.08f), "the explicit tweak copy must carry floor friction");
        Assert.That((float)configType.GetProperty("FerrofluidCubeCornerFriction")!.GetValue(target)!, Is.EqualTo(0.04f), "the explicit tweak copy must carry corner friction");
        Assert.That((float)configType.GetProperty("FerrofluidCubeFaceLandingStrength")!.GetValue(target)!, Is.EqualTo(0.3f), "the explicit tweak copy must carry landing strength");
    }

    [Test]
    public void ThinFilmAngleRatePreviewsSavesCancelsAndResets()
    {
        XElement root = LoadBsml("ChromaGLS.Settings.ferrofluid-settings.bsml");
        XElement? filmGroup = root.Descendants("vertical")
            .FirstOrDefault(section => (string?)section.Elements().FirstOrDefault()?.Attribute("text") == "Thin film");
        Assert.That(filmGroup, Is.Not.Null, "The Thin film group must keep its titled section.");
        XElement[] filmSliders = filmGroup!.Elements("slider-setting").ToArray();
        int strengthIndex = Array.FindIndex(
            filmSliders,
            element => (string?)element.Attribute("value") == "ferrofluid-thin-film-strength");
        Assert.That(strengthIndex, Is.GreaterThanOrEqualTo(0), "The film strength slider must stay in the Thin film group.");
        Assert.That(strengthIndex + 1, Is.LessThan(filmSliders.Length), "The angle rate slider must sit directly after strength.");
        XElement rate = filmSliders[strengthIndex + 1];
        Assert.That((string?)rate.Attribute("value"), Is.EqualTo("ferrofluid-thin-film-angle-rate"));
        Assert.That((string?)rate.Attribute("text"), Is.EqualTo("Thin Film LUT angle rate"));
        Assert.That((float)rate.Attribute("min")!, Is.EqualTo(0f));
        Assert.That((float)rate.Attribute("max")!, Is.EqualTo(4f));
        Assert.That((float)rate.Attribute("increment")!, Is.EqualTo(0.01f));
        Assert.That((string?)rate.Attribute("apply-on-change"), Is.EqualTo("true"));
        Assert.That((string?)rate.Attribute("get-event"), Is.EqualTo("refresh-ferro"));

        Type configType = Plugin.GetType("ChromaGLS.Settings.Config", true)!;
        Type uiType = Plugin.GetType("ChromaGLS.Settings.FerrofluidSettingsUI", true)!;
        object live = Activator.CreateInstance(configType, nonPublic: true)!;
        PropertyInfo rateProperty = configType.GetProperty("FerrofluidThinFilmAngleRate")!;
        Assert.That(rateProperty, Is.Not.Null, "The session config must expose the thin-film LUT angle rate.");
        Assert.That((float)rateProperty.GetValue(live)!, Is.EqualTo(1f), "A fresh session must keep the original LUT traversal rate.");
        Type ignoreType = typeof(IPA.Config.Stores.GeneratedStore).Assembly.GetType("IPA.Config.Stores.Attributes.IgnoreAttribute", true)!;
        Assert.That(rateProperty.IsDefined(ignoreType, true), Is.True, "The angle rate must stay session-only.");

        int applies = 0;
        object apply = CreateTypedCallback(configType, _ => applies++);
        object ui = Activator.CreateInstance(
            uiType,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null,
            new[] { live, apply },
            null)!;
        object session = uiType.GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui)!;
        Invoke(session, "Open");
        ((PropertyInfo)FindBinding(uiType, "UIValue", "ferrofluid-thin-film-angle-rate")!).SetValue(ui, 0.5f);
        Assert.That(applies, Is.EqualTo(1), "the angle-rate slider must preview through the apply callback");
        Assert.That((float)rateProperty.GetValue(live)!, Is.EqualTo(0.5f));

        Invoke(session, "Cancel");
        Assert.That((float)rateProperty.GetValue(live)!, Is.EqualTo(1f), "cancel restores the opening rate");

        Invoke(session, "Open");
        ((PropertyInfo)FindBinding(uiType, "UIValue", "ferrofluid-thin-film-angle-rate")!).SetValue(ui, 0.5f);
        Invoke(session, "Save");
        Invoke(session, "Cancel");
        Assert.That((float)rateProperty.GetValue(live)!, Is.EqualTo(0.5f), "save keeps the previewed rate");

        Invoke(session, "Open");
        Invoke(session, "Reset");
        Assert.That((float)rateProperty.GetValue(live)!, Is.EqualTo(1f), "reset restores the original rate");
        ((IDisposable)ui).Dispose();

        rateProperty.SetValue(live, 2.5f);
        MethodInfo baseline = Plugin.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", true)!
            .GetMethod("EvaluateTorusBaseline", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        object clone = baseline.Invoke(null, new[] { live })!;
        Assert.That((float)rateProperty.GetValue(clone)!, Is.EqualTo(2.5f), "the torus baseline clone must carry the live angle rate");
    }

    [Test]
    public void TweakSessionPreviewsAndCancelsWithoutPersisting()
    {
        Type? sessionType = Plugin.GetType("ChromaGLS.Settings.FerrofluidTweakEditSession", throwOnError: false);
        Assert.That(sessionType, Is.Not.Null, "Planned feature unavailable: FerrofluidTweakEditSession missing.");
        Type configType = Plugin.GetType("ChromaGLS.Settings.Config", throwOnError: true)!;
        object live = Activator.CreateInstance(configType, nonPublic: true)!;
        int applies = 0;
        object apply = CreateTypedCallback(configType, _ => applies++);
        object session = Activator.CreateInstance(sessionType, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { live, apply }, null)!;
        PropertyInfo editing = sessionType.GetProperty("IsEditing", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        MethodInfo open = sessionType.GetMethod("Open", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        MethodInfo preview = sessionType.GetMethod("Preview", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        MethodInfo reset = sessionType.GetMethod("Reset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        MethodInfo save = sessionType.GetMethod("Save", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        MethodInfo cancel = sessionType.GetMethod("Cancel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        PropertyInfo exposure = configType.GetProperty("FerrofluidExposure")!;

        // Preview while closed must not apply or mutate.
        preview.Invoke(session, new object[] { CreateTypedCallback(configType, c => exposure.SetValue(c, 9f)) });
        Assert.That(applies, Is.EqualTo(0), "Closed preview must not apply.");
        Assert.That((float)exposure.GetValue(live)!, Is.EqualTo(1f), "Closed preview must not mutate.");

        open.Invoke(session, null);
        Assert.That((bool)editing.GetValue(session)!, Is.True);

        preview.Invoke(session, new object[] { CreateTypedCallback(configType, c => exposure.SetValue(c, 0.33f)) });
        Assert.That(applies, Is.EqualTo(1), "Open preview applies once.");
        Assert.That((float)exposure.GetValue(live)!, Is.EqualTo(0.33f));

        // Reset pushes the new defaults into live while staying open.
        reset.Invoke(session, null);
        Assert.That((float)exposure.GetValue(live)!, Is.EqualTo(1f), "Reset restores the new defaults.");
        Assert.That((bool)editing.GetValue(session)!, Is.True, "Reset keeps editing.");

        // Cancel restores the values captured at Open, discarding post-Reset previews.
        preview.Invoke(session, new object[] { CreateTypedCallback(configType, c => exposure.SetValue(c, 0.5f)) });
        cancel.Invoke(session, null);
        Assert.That((float)exposure.GetValue(live)!, Is.EqualTo(1f), "Cancel restores the opening snapshot.");
        Assert.That((bool)editing.GetValue(session)!, Is.False);

        // Save keeps the current values; a later Open captures them as the new snapshot.
        open.Invoke(session, null);
        preview.Invoke(session, new object[] { CreateTypedCallback(configType, c => exposure.SetValue(c, 0.7f)) });
        save.Invoke(session, null);
        Assert.That((bool)editing.GetValue(session)!, Is.False);
        open.Invoke(session, null);
        cancel.Invoke(session, null);
        Assert.That((float)exposure.GetValue(live)!, Is.EqualTo(0.7f), "Save then reopen captures the saved state.");
    }

    [Test]
    public void TweakSessionCancelLeavesPersistedPreferencesAlone()
    {
        Type? sessionType = Plugin.GetType("ChromaGLS.Settings.FerrofluidTweakEditSession", throwOnError: false);
        Assert.That(sessionType, Is.Not.Null, "Planned feature unavailable: FerrofluidTweakEditSession missing.");
        Type configType = Plugin.GetType("ChromaGLS.Settings.Config", throwOnError: true)!;
        object live = Activator.CreateInstance(configType, nonPublic: true)!;
        PropertyInfo cube = configType.GetProperty("FerrofluidCubeEnabled")!;
        PropertyInfo sphere = configType.GetProperty("FerrofluidSphereEnabled")!;
        cube.SetValue(live, true);
        sphere.SetValue(live, true);
        object apply = CreateTypedCallback(configType, _ => { });
        object session = Activator.CreateInstance(sessionType, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { live, apply }, null)!;
        sessionType.GetMethod("Open", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(session, null);
        sessionType.GetMethod("Preview", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(session, new object[] { CreateTypedCallback(configType, c => { cube.SetValue(c, false); sphere.SetValue(c, false); }) });
        sessionType.GetMethod("Cancel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(session, null);
        Assert.That((bool)cube.GetValue(live)!, Is.False, "Preview mutations to persisted prefs are applied but");
        Assert.That((bool)sphere.GetValue(live)!, Is.False, "Cancel restores only tweak properties — it must not resurrect the persisted toggles the preview changed.");
    }

    private static object CreateTypedCallback(Type configType, Action<object> body)
    {
        var parameter = System.Linq.Expressions.Expression.Parameter(configType, "config");
        var call = System.Linq.Expressions.Expression.Call(
            System.Linq.Expressions.Expression.Constant(body.Target),
            body.Method,
            System.Linq.Expressions.Expression.Convert(parameter, typeof(object)));
        return System.Linq.Expressions.Expression
            .Lambda(typeof(Action<>).MakeGenericType(configType), call, parameter)
            .Compile();
    }

    [Test]
    public void CancelAndResetCoverEverySessionOnlyParameter()
    {
        Type configType = Plugin.GetType("ChromaGLS.Settings.Config", true)!;
        object config = Activator.CreateInstance(configType, true)!;
        object defaults = Activator.CreateInstance(configType, true)!;
        PropertyInfo[] tweaks = configType.GetProperties().Where(property =>
            property.GetCustomAttributes(false).Any(attribute => attribute.GetType().Name == "IgnoreAttribute")).ToArray();
        Assert.That(tweaks.Length, Is.GreaterThan(50), "Exercise the complete tweak set, not just appearance.");
        Dictionary<string, object?> opening = tweaks.ToDictionary(property => property.Name, property => (object?)property.GetValue(config));
        Type sessionType = Plugin.GetType("ChromaGLS.Settings.FerrofluidTweakEditSession", true)!;
        object session = Activator.CreateInstance(
            sessionType,
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { config, CreateTypedCallback(configType, _ => { }) },
            null)!;
        Invoke(session, "Open");
        Invoke(session, "Preview", CreateTypedCallback(configType, ChangedValues));
        Invoke(session, "Cancel");
        foreach (PropertyInfo property in tweaks)
            Assert.That(property.GetValue(config), Is.EqualTo(opening[property.Name]), $"Cancel must restore {property.Name}.");

        ChangedValues(config);
        Dictionary<string, object?> nonDefaults = tweaks.ToDictionary(property => property.Name, property => (object?)property.GetValue(config));
        Invoke(session, "Open");
        Invoke(session, "Reset");
        Assert.That(sessionType.GetProperty("IsEditing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session), Is.True);
        foreach (PropertyInfo property in tweaks)
            Assert.That(property.GetValue(config), Is.EqualTo(property.GetValue(defaults)), $"Reset must preview the default {property.Name}.");

        Invoke(session, "Cancel");
        foreach (PropertyInfo property in tweaks)
            Assert.That(property.GetValue(config), Is.EqualTo(nonDefaults[property.Name]), $"Cancel after Reset must restore {property.Name}.");

        void ChangedValues(object target)
        {
            foreach (PropertyInfo property in tweaks)
            {
                object value = property.GetValue(target)!;
                property.SetValue(target, value switch
                {
                    bool flag => !flag,
                    int number => number + 1,
                    float number => number + 0.12345f,
                    _ => throw new InvalidOperationException($"Unsupported tweak type {property.PropertyType}."),
                });
            }
        }
    }

    [Test]
    public void EveryModalControlResolvesOnItsCompiledHost()
    {
        XElement view = LoadBsml("ChromaGLS.Settings.ferrofluid-settings.bsml");
        Type host = Plugin.GetType("ChromaGLS.Settings.FerrofluidSettingsUI", true)!;
        foreach (XElement control in view.Descendants().Where(element =>
            element.Name.LocalName == "slider-setting" || element.Name.LocalName == "bool-setting"))
        {
            string name = (string)control.Attribute("value")!;
            Assert.That(FindBinding(host, "UIValue", name), Is.Not.Null, $"The modal host must bind {name}.");
        }

        foreach (XElement button in view.Descendants().Where(element => element.Attribute("on-click") != null))
        {
            string name = (string)button.Attribute("on-click")!;
            Assert.That(FindBinding(host, "UIAction", name), Is.Not.Null, $"The modal host must handle {name}.");
        }
    }

    [Test]
    public void ClosingTheControllerDiscardsUncommittedPreviews()
    {
        Type configType = Plugin.GetType("ChromaGLS.Settings.Config", true)!;
        object config = Activator.CreateInstance(configType, true)!;
        Type controllerType = Plugin.GetType("ChromaGLS.Settings.FerrofluidSettingsUI", true)!;
        int applies = 0;
        object controller = Activator.CreateInstance(
            controllerType,
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { config, CreateTypedCallback(configType, _ => applies++) },
            null)!;
        object session = controllerType.GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        Invoke(session, "Open");
        ((PropertyInfo)FindBinding(controllerType, "UIValue", "ferrofluid-exposure")!).SetValue(controller, 0.33f);
        Assert.That(applies, Is.EqualTo(1));
        Invoke(controller, "CloseWithoutSaving");
        Assert.That(configType.GetProperty("FerrofluidExposure")!.GetValue(config), Is.EqualTo(1f));
        Assert.That(applies, Is.EqualTo(2));
        Invoke(controller, "CloseWithoutSaving");
        Assert.That(applies, Is.EqualTo(2), "Repeated disable/dispose callbacks must not reapply a closed session.");
    }

    [Test]
    public void FirstShowSetupViewDisableMustNotCancelTheOpeningSession()
    {
        Type configType = Plugin.GetType("ChromaGLS.Settings.Config", true)!;
        object config = Activator.CreateInstance(configType, true)!;
        Type controllerType = Plugin.GetType("ChromaGLS.Settings.FerrofluidSettingsUI", true)!;
        int applies = 0;
        object controller = Activator.CreateInstance(
            controllerType,
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { config, CreateTypedCallback(configType, _ => applies++) },
            null)!;
        object session = controllerType.GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        MethodInfo openSession = controllerType.GetMethod("OpenSession", BindingFlags.Instance | BindingFlags.NonPublic)!;
        openSession.Invoke(controller, new object[] { (Action)(() => Invoke(controller, "CloseWithoutSaving")) });
        Assert.That((bool)session.GetType().GetProperty("IsEditing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!, Is.True, "the show must precede the editing snapshot so SetupView's disable cannot cancel it");
        ((PropertyInfo)FindBinding(controllerType, "UIValue", "ferrofluid-exposure")!).SetValue(controller, 0.33f);
        Assert.That(applies, Is.EqualTo(1), "a slider setter right after first open must preview normally");
        ((PropertyInfo)FindBinding(controllerType, "UIValue", "ferrofluid-spikes-enabled")!).SetValue(controller, false);
        Assert.That(applies, Is.EqualTo(2), "a toggle setter right after first open must preview normally");
        Invoke(controller, "CloseWithoutSaving");
        Assert.That(configType.GetProperty("FerrofluidExposure")!.GetValue(config), Is.EqualTo(1f), "cancel must restore the persisted exposure");
        Assert.That(configType.GetProperty("FerrofluidSpikesEnabled")!.GetValue(config), Is.EqualTo(true), "cancel must restore the persisted toggle");
        Assert.That(applies, Is.EqualTo(3));
        Invoke(controller, "CloseWithoutSaving");
        Assert.That(applies, Is.EqualTo(3), "repeated disable callbacks must not reapply a closed session");
    }

    [Test]
    public void TweakModalGroupsControlsIntoFourteenTitledSections()
    {
        XElement root = LoadBsml("ChromaGLS.Settings.ferrofluid-settings.bsml");
        XElement scroll = root.Descendants().First(element => element.Name.LocalName == "scroll-view");
        XElement[] sections = scroll.Elements().First().Elements().Where(element => element.Name.LocalName == "vertical").ToArray();
        Assert.That(sections.Length, Is.EqualTo(14), "the scroll content must organize controls into fourteen sections");
        foreach (XElement section in sections)
        {
            XElement header = section.Elements().First();
            Assert.That(header.Name.LocalName, Is.EqualTo("text"), "each section must open with a title");
            Assert.That((string?)header.Attribute("font-size"), Is.EqualTo("4.5"));
            Assert.That((string?)header.Attribute("bold"), Is.EqualTo("true"));
        }

        Assert.That((string?)sections[0].Elements().First().Attribute("text"), Is.EqualTo("Cube floor physics"), "the floor physics group must lead the scroll content");

        XElement[] controls = sections.SelectMany(section => section.Elements().Skip(1)).ToArray();
        Assert.That((string?)controls[0].Attribute("value"), Is.EqualTo("ferrofluid-floor-assistance-enabled"), "the floor assistance toggle must lead the list");
        Assert.That((string?)controls[1].Attribute("value"), Is.EqualTo("ferrofluid-cube-floor-friction"));
        Assert.That((string?)controls[2].Attribute("value"), Is.EqualTo("ferrofluid-cube-corner-friction"));
        Assert.That((string?)controls[3].Attribute("value"), Is.EqualTo("ferrofluid-cube-face-landing-strength"));
        Assert.That((string?)controls[7].Attribute("value"), Is.EqualTo("ferrofluid-body-enabled"), "the visibility toggles must follow the floor physics group");
        Assert.That((string?)controls[8].Attribute("value"), Is.EqualTo("ferrofluid-spikes-enabled"));
        Assert.That(controls.Skip(9).All(element => element.Name.LocalName != "bool-setting"), Is.True, "all on/off toggles must precede the remaining sliders");
        Assert.That(controls.Count(element => element.Attribute("value") != null), Is.EqualTo(76), "all seventy-six control bindings must remain after regrouping");
        Assert.That(controls.Select(element => (string?)element.Attribute("value")).Distinct().Count(), Is.EqualTo(controls.Count(element => element.Attribute("value") != null)), "every binding must appear exactly once");
        Assert.That((string?)scroll.Elements().First().Attribute("spacing"), Is.EqualTo("5"), "section spacing must visually separate the groups");
    }

    [Test]
    public void CloseForGameplayCancelsAnOpenSessionAndAllowsReopen()
    {
        Type configType = Plugin.GetType("ChromaGLS.Settings.Config", true)!;
        object config = Activator.CreateInstance(configType, true)!;
        Type controllerType = Plugin.GetType("ChromaGLS.Settings.FerrofluidSettingsUI", true)!;
        int applies = 0;
        object controller = Activator.CreateInstance(
            controllerType,
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { config, CreateTypedCallback(configType, _ => applies++) },
            null)!;
        object session = controllerType.GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        MethodInfo openSession = controllerType.GetMethod("OpenSession", BindingFlags.Instance | BindingFlags.NonPublic)!;
        PropertyInfo isEditing = session.GetType().GetProperty("IsEditing", BindingFlags.Instance | BindingFlags.NonPublic)!;

        openSession.Invoke(controller, new object[] { (Action)(() => { }) });
        ((PropertyInfo)FindBinding(controllerType, "UIValue", "ferrofluid-exposure")!).SetValue(controller, 0.33f);
        Assert.That(applies, Is.EqualTo(1));

        Invoke(controller, "CloseForGameplay");
        Assert.That((bool)isEditing.GetValue(session)!, Is.False, "gameplay entry must cancel any live preview");
        Assert.That(applies, Is.EqualTo(2), "the cancel must restore the persisted snapshot once");
        Invoke(controller, "CloseForGameplay");
        Assert.That(applies, Is.EqualTo(2), "repeat cleanup must stay a no-op");

        openSession.Invoke(controller, new object[] { (Action)(() => Invoke(controller, "CloseWithoutSaving")) });
        Assert.That((bool)isEditing.GetValue(session)!, Is.True, "a fresh open after gameplay cleanup must still work");
    }

    private static MemberInfo? FindBinding(Type host, string attributeName, string binding)
    {
        return host.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(member => member.GetCustomAttributes(false).Any(attribute =>
            {
                Type type = attribute.GetType();
                if (type.Name != attributeName)
                    return false;

                object? value = type.GetProperty("Id")?.GetValue(attribute)
                    ?? type.GetProperty("Name")?.GetValue(attribute)
                    ?? type.GetField("Id")?.GetValue(attribute)
                    ?? type.GetField("Name")?.GetValue(attribute);
                return Equals(value, binding);
            }));
    }

    private static void Invoke(object target, string name, params object[] arguments)
        => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments);

    private static XElement LoadBsml(string resource)
    {
        using Stream? stream = Plugin.GetManifestResourceStream(resource);
        Assert.That(stream, Is.Not.Null, $"Embedded BSML {resource} missing.");
        return XDocument.Load(stream!).Root!;
    }
}
