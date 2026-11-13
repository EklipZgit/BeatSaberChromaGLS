using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class ParabolicFloorLightSettingsTests
{
    [Test]
    public void IntensityMappingHitsExactKnots()
    {
        MethodInfo intensity = GetMappingMethod("EvaluateIntensity");
        Assert.That((float)intensity.Invoke(null, [0f])!, Is.EqualTo(0f).Within(0.000001f));
        Assert.That((float)intensity.Invoke(null, [0.25f])!, Is.EqualTo(0.5f).Within(0.000001f));
        Assert.That((float)intensity.Invoke(null, [0.5f])!, Is.EqualTo(1f).Within(0.000001f));
        Assert.That((float)intensity.Invoke(null, [0.75f])!, Is.EqualTo(3.25f).Within(0.000001f));
        Assert.That((float)intensity.Invoke(null, [1f])!, Is.EqualTo(10f).Within(0.000001f));
    }

    [Test]
    public void IntensityAndWidthInversesRoundTrip()
    {
        MethodInfo intensity = GetMappingMethod("EvaluateIntensity");
        MethodInfo intensityInverse = GetMappingMethod("EvaluateIntensitySlider");
        MethodInfo width = GetMappingMethod("EvaluateWidthScale");
        MethodInfo widthInverse = GetMappingMethod("EvaluateWidthScaleSlider");

        foreach (float value in new[] { 0f, 0.5f, 1f, 2f, 3.25f, 7.5f, 10f })
        {
            float slider = (float)intensityInverse.Invoke(null, [value])!;
            Assert.That((float)intensity.Invoke(null, [slider])!, Is.EqualTo(value).Within(0.0001f), $"intensity {value} must round-trip");
        }

        Assert.That((float)intensityInverse.Invoke(null, [1f])!, Is.EqualTo(0.5f).Within(0.000001f), "the v=1 boundary must map to s=0.5");
        foreach (float slider in new[] { 0f, 0.1f, 0.49f, 0.5f, 0.51f, 0.9f, 1f })
        {
            float value = (float)intensity.Invoke(null, [slider])!;
            Assert.That((float)intensityInverse.Invoke(null, [value])!, Is.EqualTo(slider).Within(0.0001f), $"slider {slider} must round-trip");
        }

        Assert.That((float)width.Invoke(null, [0f])!, Is.EqualTo(0.5f).Within(0.000001f));
        Assert.That((float)width.Invoke(null, [1f])!, Is.EqualTo(8f).Within(0.000001f));
        Assert.That((float)width.Invoke(null, [0.5f])!, Is.EqualTo(2.375f).Within(0.000001f));
        Assert.That((float)widthInverse.Invoke(null, [2f])!, Is.EqualTo(0.4472136f).Within(0.0001f), "the shipped width scale must invert to its slider position");
        foreach (float slider in new[] { 0f, 0.3f, 0.5f, 0.8f, 1f })
        {
            float value = (float)width.Invoke(null, [slider])!;
            Assert.That((float)widthInverse.Invoke(null, [value])!, Is.EqualTo(slider).Within(0.0001f));
        }
    }

    [Test]
    public void LightWidthMappingHitsKnotsAndRoundTrips()
    {
        MethodInfo lightWidth = GetMappingMethod("EvaluateLightWidthScale");
        MethodInfo lightWidthInverse = GetMappingMethod("EvaluateLightWidthScaleSlider");

        Assert.That((float)lightWidth.Invoke(null, [0f])!, Is.EqualTo(0.001f).Within(0.000001f));
        Assert.That((float)lightWidth.Invoke(null, [0.5f])!, Is.EqualTo(1.00075f).Within(0.000001f));
        Assert.That((float)lightWidth.Invoke(null, [1f])!, Is.EqualTo(4f).Within(0.000001f));
        Assert.That((float)lightWidthInverse.Invoke(null, [1f])!, Is.EqualTo(Mathf.Sqrt(0.999f / 3.999f)).Within(0.0001f), "the shipped light width must invert to its slider position");
        Assert.That((float)lightWidth.Invoke(null, [-1f])!, Is.EqualTo(0.001f).Within(0.000001f), "slider inputs clamp to the mapped range");
        Assert.That((float)lightWidthInverse.Invoke(null, [0.0001f])!, Is.EqualTo(0f).Within(0.000001f), "values below the .001 minimum clamp to slider 0");
        Assert.That((float)lightWidthInverse.Invoke(null, [9f])!, Is.EqualTo(1f).Within(0.000001f), "values above range clamp to slider 1");
        foreach (float slider in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        {
            float value = (float)lightWidth.Invoke(null, [slider])!;
            Assert.That((float)lightWidthInverse.Invoke(null, [value])!, Is.EqualTo(slider).Within(0.0001f));
        }

        foreach (float value in new[] { 0.001f, 0.002f, 0.01f, 0.1f, 1f, 4f })
        {
            float slider = (float)lightWidthInverse.Invoke(null, [value])!;
            Assert.That((float)lightWidth.Invoke(null, [slider])!, Is.EqualTo(value).Within(0.0001f), $"light width {value} must survive the round trip");
        }
    }

    [Test]
    public void BloomWidthMappingHitsKnotsAndRoundTrips()
    {
        MethodInfo bloomWidth = GetMappingMethod("EvaluateBloomWidthScale");
        MethodInfo bloomWidthInverse = GetMappingMethod("EvaluateBloomWidthScaleSlider");

        Assert.That((float)bloomWidth.Invoke(null, [0f])!, Is.EqualTo(0f).Within(0.000001f));
        Assert.That((float)bloomWidth.Invoke(null, [0.5f])!, Is.EqualTo(1f).Within(0.000001f), "the default slider position keeps the authored bloom width");
        Assert.That((float)bloomWidth.Invoke(null, [1f])!, Is.EqualTo(4f).Within(0.000001f));
        Assert.That((float)bloomWidthInverse.Invoke(null, [1f])!, Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That((float)bloomWidth.Invoke(null, [-1f])!, Is.EqualTo(0f).Within(0.000001f), "slider inputs clamp to the mapped range");
        Assert.That((float)bloomWidthInverse.Invoke(null, [9f])!, Is.EqualTo(1f).Within(0.000001f));
        foreach (float slider in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        {
            float value = (float)bloomWidth.Invoke(null, [slider])!;
            Assert.That((float)bloomWidthInverse.Invoke(null, [value])!, Is.EqualTo(slider).Within(0.0001f));
        }
    }

    [Test]
    public void ConfigDefaultsPreserveShippedAppearanceAndPersist()
    {
        Type configType = GetPluginType("ChromaGLS.Settings.Config");
        Type ignoreType = typeof(IPA.Config.Stores.GeneratedStore).Assembly.GetType("IPA.Config.Stores.Attributes.IgnoreAttribute", true)!;
        object config = Activator.CreateInstance(configType, nonPublic: true)!;

        AssertConfigDefault(config, "ParabolicFloorLightEnabled", true, ignoreType);
        AssertConfigDefault(config, "ParabolicFloorLightBrightness", 0.43f, ignoreType);
        AssertConfigDefault(config, "ParabolicFloorLightRed", 0f, ignoreType);
        AssertConfigDefault(config, "ParabolicFloorLightGreen", 1f, ignoreType);
        AssertConfigDefault(config, "ParabolicFloorLightBlue", 2.5f, ignoreType);
        AssertConfigDefault(config, "ParabolicFloorLightWidthScale", 3.2f, ignoreType);
        AssertConfigDefault(config, "ParabolicFloorLightOffsetX", 0f, ignoreType);
        AssertConfigDefault(config, "ParabolicFloorLightOffsetZ", 0f, ignoreType);
        AssertConfigDefault(config, "ParabolicFloorLightRotation", 0f, ignoreType);
        AssertConfigDefault(config, "ParabolicFloorLightLightWidthScale", 1f, ignoreType);
        AssertConfigDefault(config, "ParabolicFloorLightBloomWidthScale", 0.6f, ignoreType);
        AssertConfigDefault(config, "ParabolicFloorLightBloomBrightness", 1.42f, ignoreType);
        AssertConfigDefault(config, "ParabolicFloorLightHdrGlowThreshold", 1.8f, ignoreType);
        AssertConfigDefault(config, "ParabolicFloorLightHdrGlowWidthScale", 0.20f, ignoreType);
        AssertConfigDefault(config, "ParabolicFloorLightHdrGlowBrightness", 2f, ignoreType);
    }

    [Test]
    public void SettingsDefaultsMatchRequestedAppearance()
    {
        Type settingsType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettings");
        Type configType = GetPluginType("ChromaGLS.Settings.Config");

        object shipped = settingsType.GetField("Default", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null)!;
        AssertSettings(shipped, 0.43f, 0f, 0.86f, 2.47f, 3.2f, 0f, 0f, 0f, 1f, 0.96f, 1.25f, 1.3f, 0.04f, 4f, "the shipped Default tuple must match the requested appearance");

        object config = Activator.CreateInstance(configType, nonPublic: true)!;
        object fromConfig = settingsType.GetMethod("FromConfig", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, [config])!;
        AssertSettings(fromConfig, 0.43f, 0f, 1f, 2.5f, 3.2f, 0f, 0f, 0f, 1f, 0.6f, 1.42f, 1.8f, 0.20f, 2f, "a fresh config must resolve to the floor defaults");

        ConstructorInfo? ctor = settingsType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate => candidate.GetParameters().Length == 14);
        Assert.That(ctor, Is.Not.Null);
        object omitted = ctor!.Invoke([1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing]);
        Assert.That(SettingsField(omitted, "BloomWidthScale"), Is.EqualTo(0.96f).Within(0.000001f), "omitted bloom width must default to the shipped .96");
        Assert.That(SettingsField(omitted, "BloomBrightness"), Is.EqualTo(1.25f).Within(0.000001f), "omitted bloom brightness must default to the shipped 1.25");
        Assert.That(SettingsField(omitted, "HdrGlowThreshold"), Is.EqualTo(1.3f).Within(0.000001f), "omitted inner threshold must default to the shipped 1.3");
        Assert.That(SettingsField(omitted, "HdrGlowWidthScale"), Is.EqualTo(0.04f).Within(0.000001f), "omitted inner width must default to the shipped .04");
        Assert.That(SettingsField(omitted, "HdrGlowBrightness"), Is.EqualTo(4f).Within(0.000001f), "omitted inner brightness must default to the shipped 1");
    }

    [Test]
    public void EditSessionPreviewsWithoutTouchingSavedConfig()
    {
        object session = OpenSession(out Type settingsType, out object config);
        List<object> recorded = _recordedSettings;

        object changed = NewSettings(settingsType, 5f, 1f, 0.5f, 0.25f, 4f, 1.5f, -2f, 90f, 3.5f);
        Invoke(session, "Preview", changed);
        Assert.That(recorded.Count, Is.EqualTo(1), "slider writes must forward exactly one preview");
        AssertSettings(recorded[0], 5f, 1f, 0.5f, 0.25f, 4f, 1.5f, -2f, 90f, 3.5f);
        foreach (string name in SettingProperties)
            Assert.That(GetConfigProperty(config, name), Is.EqualTo(DefaultConfigValues[name]), $"preview must not write {name}");

        recorded.Clear();
        Invoke(session, "Cancel");
        Assert.That(recorded.Count, Is.EqualTo(1), "cancel must push the saved snapshot back through preview");
        Invoke(session, "Preview", changed);
        Assert.That(recorded.Count, Is.EqualTo(1), "preview calls outside editing are ignored");
    }

    [Test]
    public void EditSessionCancelRestoresEverySavedValue()
    {
        object session = OpenSession(out Type settingsType, out object _);
        Invoke(session, "Preview", NewSettings(settingsType, 9f, 1f, 1f, 1f, 7f, 5f, 5f, 300f));
        _recordedSettings.Clear();
        Invoke(session, "Cancel");
        Assert.That(_recordedSettings.Count, Is.EqualTo(1), "cancel must push the saved snapshot back through preview");
        AssertSettings(_recordedSettings[0], 0.43f, 0f, 1f, 2.5f, 3.2f, 0f, 0f, 0f, 1f, 0.6f, 1.42f, 1.8f, 0.20f, 2f);
        Invoke(session, "Cancel");
        Assert.That(_recordedSettings.Count, Is.EqualTo(1), "a second cancel while closed must not fire again");
    }

    [Test]
    public void EditSessionCancelRestoresSavedBloomValues()
    {
        object session = OpenSession(out Type settingsType, out object config);
        Invoke(session, "Cancel");
        config.GetType().GetProperty("ParabolicFloorLightBloomWidthScale")!.SetValue(config, 0.5f);
        config.GetType().GetProperty("ParabolicFloorLightBloomBrightness")!.SetValue(config, 2f);
        Invoke(session, "Open");
        AssertSettings(ReadSettings(session, "Working"), 0.43f, 0f, 1f, 2.5f, 3.2f, 0f, 0f, 0f, 1f, 0.5f, 2f, 1.8f, 0.20f, 2f, "reopening must read the saved bloom values");

        Invoke(session, "Preview", NewSettings(settingsType, 0.43f, 0f, 0.86f, 2.47f, 3.2f, 0f, 0f, 0f, 1f, 3f, 7f));
        _recordedSettings.Clear();
        Invoke(session, "Cancel");
        Assert.That(_recordedSettings.Count, Is.EqualTo(1), "cancel must push the saved snapshot back through preview");
        AssertSettings(_recordedSettings[0], 0.43f, 0f, 1f, 2.5f, 3.2f, 0f, 0f, 0f, 1f, 0.5f, 2f, 1.8f, 0.20f, 2f, "cancel must restore the saved bloom values, not the defaults");
        AssertConfigValue(config, "ParabolicFloorLightBloomWidthScale", 0.5f);
        AssertConfigValue(config, "ParabolicFloorLightBloomBrightness", 2f);
    }

    private static readonly (string Name, float Value)[] DistinctSavedValues =
    [
        ("ParabolicFloorLightBrightness", 6f), ("ParabolicFloorLightRed", 2f), ("ParabolicFloorLightGreen", 1f),
        ("ParabolicFloorLightBlue", 0.25f), ("ParabolicFloorLightWidthScale", 7f), ("ParabolicFloorLightOffsetX", -4f),
        ("ParabolicFloorLightOffsetZ", 3f), ("ParabolicFloorLightRotation", 200f), ("ParabolicFloorLightLightWidthScale", 3f),
        ("ParabolicFloorLightBloomWidthScale", 2f), ("ParabolicFloorLightBloomBrightness", 8f),
        ("ParabolicFloorLightHdrGlowThreshold", 2.3f), ("ParabolicFloorLightHdrGlowWidthScale", 0.25f), ("ParabolicFloorLightHdrGlowBrightness", 2f),
    ];

    [Test]
    public void CloseForGameplayCancelsTheSessionAndIsRepeatable()
    {
        object config = Activator.CreateInstance(GetPluginType("ChromaGLS.Settings.Config"), nonPublic: true)!;
        object session = OpenControllerSession(config, out object controller);
        Invoke(controller, "CloseForGameplay");
        Assert.That(IsEditing(session), Is.False, "gameplay entry must cancel any live preview");
        Invoke(controller, "CloseForGameplay");
        Invoke(controller, "CloseForGameplay");
        Assert.That(IsEditing(session), Is.False, "repeat cleanup must stay a no-op");
    }

    private static object OpenControllerSession(object config, out object controller)
    {
        foreach ((string name, float value) in DistinctSavedValues)
            config.GetType().GetProperty(name)!.SetValue(config, value);

        controller = CreateController(config, CreateService(config));
        object session = controller.GetType().GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        Invoke(session, "OpenExisting", "legacy-parabola");
        return session;
    }

    private static bool IsEditing(object session)
        => (bool)session.GetType().GetProperty("IsEditing", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(session)!;

    [Test]
    public void EditSessionResetPreviewsDefaultsAndLeavesSessionOpen()
    {
        object config = Activator.CreateInstance(GetPluginType("ChromaGLS.Settings.Config"), nonPublic: true)!;
        object session = OpenControllerSession(config, out object controller);
        Type uiType = controller.GetType();

        ((PropertyInfo)FindUIValue(uiType, "parabola-brightness")!).SetValue(controller, 0.9f);
        Assert.That(SettingsField(ReadSettings(session, "Working"), "Brightness"), Is.EqualTo(6.76f).Within(0.001f));

        MemberInfo reset = FindUIAction(uiType, "parabola-reset")!;
        Assert.That(reset, Is.Not.Null, "the controller must bind the parabola-reset action");
        ((MethodInfo)reset).Invoke(controller, null);

        AssertSettings(ReadSettings(session, "Working"), 0.43f, 0f, 1f, 2.5f, 3.2f, 0f, 0f, 0f, 1f, 0.6f, 1.42f, 1.8f, 0.20f, 2f, "reset must preview the shipped defaults");
        Assert.That(IsEditing(session), Is.True, "reset leaves the session editing and the modal open");
        foreach ((string name, float value) in DistinctSavedValues)
            AssertConfigValue(config, name, value);

        (string UIName, float Slider)[] defaults =
        [
            ("parabola-brightness", 0.215f), ("parabola-red", 0f),
            ("parabola-green", 0.5f), ("parabola-blue", 0.5f + (0.5f * Mathf.Sqrt(1.5f / 19f))), ("parabola-width", Mathf.Sqrt((3.2f - 0.5f) / 7.5f)),
            ("parabola-light-width", Mathf.Sqrt((1f - 0.001f) / 3.999f)), ("parabola-bloom-width", Mathf.Sqrt(0.15f)),
            ("parabola-bloom-brightness", 0.5f + (0.5f * Mathf.Sqrt(0.42f / 9f))),
            ("parabola-hdr-glow-threshold", 0.5f + (0.5f * Mathf.Sqrt(0.8f / 9f))), ("parabola-hdr-glow-width", Mathf.Sqrt(0.20f)),
            ("parabola-hdr-glow-brightness", 0.5f + (0.5f * Mathf.Sqrt(1f / 9f))),
            ("parabola-offset-z", 0f), ("parabola-offset-x", 0f), ("parabola-rotation", 0f),
        ];
        foreach ((string uiName, float slider) in defaults)
            Assert.That((float)((PropertyInfo)FindUIValue(uiType, uiName)!).GetValue(controller)!, Is.EqualTo(slider).Within(0.001f), $"{uiName} must show the default slider position after reset");
    }

    [Test]
    public void EditSessionResetThenCancelRestoresSavedValues()
    {
        object config = Activator.CreateInstance(GetPluginType("ChromaGLS.Settings.Config"), nonPublic: true)!;
        object session = OpenControllerSession(config, out object controller);

        MemberInfo reset = FindUIAction(controller.GetType(), "parabola-reset")!;
        Assert.That(reset, Is.Not.Null, "the controller must bind the parabola-reset action");
        ((MethodInfo)reset).Invoke(controller, null);
        ((MethodInfo)FindUIAction(controller.GetType(), "parabola-cancel")!).Invoke(controller, null);

        Assert.That(IsEditing(session), Is.False, "cancel after reset must still close the session");
        foreach ((string name, float value) in DistinctSavedValues)
            AssertConfigValue(config, name, value);

        Invoke(session, "OpenExisting", "legacy-parabola");
        AssertSettings(ReadSettings(session, "Working"), 6f, 2f, 1f, 0.25f, 7f, -4f, 3f, -160f, 3f, 2f, 8f, 2.3f, 0.25f, 2f, "reopening after reset+cancel must read the saved values with equivalent normalized yaw");
    }

    [Test]
    public void EditSessionResetThenSavePersistsDefaultsAndReopenMatches()
    {
        object config = Activator.CreateInstance(GetPluginType("ChromaGLS.Settings.Config"), nonPublic: true)!;
        object session = OpenControllerSession(config, out object controller);

        MemberInfo reset = FindUIAction(controller.GetType(), "parabola-reset")!;
        Assert.That(reset, Is.Not.Null, "the controller must bind the parabola-reset action");
        ((MethodInfo)reset).Invoke(controller, null);
        ((MethodInfo)FindUIAction(controller.GetType(), "parabola-save")!).Invoke(controller, null);

        Assert.That(IsEditing(session), Is.False, "save after reset must close the session");
        AssertSettings(DefinitionSettings(ReadPersistedDefinition(config)), 0.43f, 0f, 1f, 2.5f, 3.2f, 0f, 0f, 0f, 1f, 0.6f, 1.42f, 1.8f, 0.20f, 2f, "reset must persist all fourteen values in the saved light collection");
        foreach ((string name, float value) in DistinctSavedValues)
            AssertConfigValue(config, name, value);

        Invoke(session, "OpenExisting", "legacy-parabola");
        AssertSettings(ReadSettings(session, "Working"), 0.43f, 0f, 1f, 2.5f, 3.2f, 0f, 0f, 0f, 1f, 0.6f, 1.42f, 1.8f, 0.20f, 2f, "reopening after reset+save must read the defaults");
    }

    [Test]
    public void EditSessionSavePersistsAllFourteenAndReopenReadsThem()
    {
        object session = OpenSession(out Type settingsType, out object config);
        object changed = NewSettings(settingsType, 2.5f, 0.2f, 0.4f, 0.9f, 3.5f, -1.25f, 2.75f, 45f, 2.5f, 3f, 2f, 2.3f, 0.25f, 2f);
        Invoke(session, "Preview", changed);
        Invoke(session, "Save");
        AssertConfigValue(config, "ParabolicFloorLightBrightness", 2.5f);
        AssertConfigValue(config, "ParabolicFloorLightRed", 0.2f);
        AssertConfigValue(config, "ParabolicFloorLightGreen", 0.4f);
        AssertConfigValue(config, "ParabolicFloorLightBlue", 0.9f);
        AssertConfigValue(config, "ParabolicFloorLightWidthScale", 3.5f);
        AssertConfigValue(config, "ParabolicFloorLightOffsetX", -1.25f);
        AssertConfigValue(config, "ParabolicFloorLightOffsetZ", 2.75f);
        AssertConfigValue(config, "ParabolicFloorLightRotation", 45f);
        AssertConfigValue(config, "ParabolicFloorLightLightWidthScale", 2.5f);
        AssertConfigValue(config, "ParabolicFloorLightBloomWidthScale", 3f);
        AssertConfigValue(config, "ParabolicFloorLightBloomBrightness", 2f);

        Invoke(session, "Open");
        AssertSettings(ReadSettings(session, "Working"), 2.5f, 0.2f, 0.4f, 0.9f, 3.5f, -1.25f, 2.75f, 45f, 2.5f, 3f, 2f, 2.3f, 0.25f, 2f);
    }

    [Test]
    public void EditSessionReopeningDoesNotOverwriteActiveDraft()
    {
        object session = OpenSession(out Type settingsType, out object _);
        Invoke(session, "Preview", NewSettings(settingsType, 4f, 0f, 0f, 0f, 1f, 0f, 0f, 0f));
        Invoke(session, "Open");
        AssertSettings(ReadSettings(session, "Working"), 4f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 1f, 1f, 1.3f, 0.04f, 4f, "reopening must keep the live draft");
    }

    [Test]
    public void ConfiguredWidthScalesReshapeTheSameStrip()
    {
        object enclosure = DefaultEnclosure();
        Type settingsType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettings");
        foreach (float scale in new[] { 0.5f, 2f, 8f })
        {
            (Vector3[] vertices, _) = CreateConfiguredLight(enclosure, Vector3.zero, Vector3.forward, NewSettings(settingsType, 1f, 0f, 0f, 1f, scale, 0f, 0f, 0f));
            Vector3 apex = RingCenter(vertices, 48);
            Assert.That(apex.z, Is.EqualTo(-0.35f).Within(0.0001f));
            float coefficient = 0.75f / (scale * scale);
            for (int station = 0; station < 97; station++)
            {
                Vector3 center = RingCenter(vertices, station);
                float xRelative = center.x - apex.x;
                Assert.That(center.z - apex.z, Is.EqualTo(coefficient * xRelative * xRelative).Within(0.001f), $"scale {scale} station {station}");
            }

            Assert.That(Vector3.Distance(vertices[0], vertices[1]), Is.EqualTo(0.175f).Within(0.0001f), "strip thickness must not scale");
        }
    }

    [Test]
    public void TranslationAndRotationMoveVertexButKeepBaseFrameAndClipping()
    {
        object enclosure = DefaultEnclosure();
        Type settingsType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettings");
        (Vector3[] vertices, _) = CreateConfiguredLight(enclosure, Vector3.zero, Vector3.forward, NewSettings(settingsType, 1f, 0f, 0f, 1f, 2f, 0.4f, 0.2f, 90f));

        Vector3 apex = RingCenter(vertices, 48);
        Assert.That(apex.x, Is.EqualTo(0.4f).Within(0.001f), "OffsetX stays along the unrotated player-right axis");
        Assert.That(apex.z, Is.EqualTo(-0.15f).Within(0.001f), "OffsetZ stays along the unrotated player-forward axis");

        Vector3 rotatedForward = Quaternion.AngleAxis(90f, Vector3.up) * Vector3.forward;
        Vector3 rotatedRight = Quaternion.AngleAxis(90f, Vector3.up) * Vector3.right;
        AssertBranchDoublesPastCircle(vertices, apex, 0, rotatedForward, rotatedRight, new Vector3(0f, 0f, -0.5f), 2.5f);
        AssertBranchDoublesPastCircle(vertices, apex, 96, rotatedForward, rotatedRight, new Vector3(0f, 0f, -0.5f), 2.5f);
    }

    private static void AssertBranchDoublesPastCircle(Vector3[] vertices, Vector3 apex, int endStation, Vector3 forward, Vector3 right, Vector3 circleCenter, float radius)
    {
        Vector3 endpoint = RingCenter(vertices, endStation);
        float oldX = Vector3.Dot(endpoint - apex, right) / Mathf.Sqrt(2f);
        float oldZ = Vector3.Dot(endpoint - apex, forward) / 2f;
        Assert.That(oldZ, Is.GreaterThanOrEqualTo(0f), "the reference reach must point forward of the apex");

        Vector3 reference = apex + (right * oldX) + (forward * oldZ);
        Vector3 tangent = right + (forward * (2f * 0.1875f * oldX));
        Vector3 normal = Vector3.Cross(Vector3.up, tangent).normalized;
        float outermost = Mathf.Max(
            Mathf.Sqrt(((reference.x + (normal.x * 0.0875f) - circleCenter.x) * (reference.x + (normal.x * 0.0875f) - circleCenter.x)) + ((reference.z + (normal.z * 0.0875f) - circleCenter.z) * (reference.z + (normal.z * 0.0875f) - circleCenter.z))),
            Mathf.Sqrt(((reference.x - (normal.x * 0.0875f) - circleCenter.x) * (reference.x - (normal.x * 0.0875f) - circleCenter.x)) + ((reference.z - (normal.z * 0.0875f) - circleCenter.z) * (reference.z - (normal.z * 0.0875f) - circleCenter.z))));
        Assert.That(outermost, Is.InRange(radius - 0.02f, radius + 0.0001f), $"the old menu boundary must sit at the half-depth reference point for station {endStation}");
        float endpointRadial = Mathf.Sqrt(((endpoint.x - circleCenter.x) * (endpoint.x - circleCenter.x)) + ((endpoint.z - circleCenter.z) * (endpoint.z - circleCenter.z)));
        Assert.That(endpointRadial, Is.GreaterThan(radius), $"the doubled tip must pass the old menu boundary for station {endStation}");
        Assert.That(Vector3.Dot(endpoint - apex, forward), Is.EqualTo(2f * oldZ).Within(0.001f), "the tip's forward depth must be twice the reference");
    }

    [Test]
    public void TranslatedVertexOutsideCircleHidesInsteadOfDegenerating()
    {
        object enclosure = DefaultEnclosure();
        Type settingsType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettings");
        MethodInfo tryCreate = GetGeometryMethod("TryCreateConfigured");
        object[] arguments = [enclosure, Vector3.zero, Vector3.forward, NewSettings(settingsType, 1f, 0f, 0f, 1f, 2f, 0f, 5f, 0f), null!];
        Assert.That(tryCreate.Invoke(null, arguments), Is.EqualTo(false), "a vertex pushed outside the circle must hide the strip");
        Assert.That(arguments[4], Is.Null);

        arguments = [enclosure, Vector3.zero, Vector3.forward, NewSettings(settingsType, 1f, 0f, 0f, 1f, 2f, 0f, 2.3f, 0f), null!];
        Assert.That(tryCreate.Invoke(null, arguments), Is.EqualTo(false), "a vertex inside but too close for its strip corners must also hide");

        arguments = [enclosure, Vector3.zero, Vector3.forward, NewSettings(settingsType, 1f, 0f, 0f, 1f, 2f, 0f, 0f, 0f), null!];
        Assert.That(tryCreate.Invoke(null, arguments), Is.EqualTo(true), "moving the vertex back inside must produce the strip again");
        Assert.That(arguments[4], Is.Not.Null);
    }

    [Test]
    public void DefaultConfiguredGeometryMatchesExistingTryCreate()
    {
        object enclosure = DefaultEnclosure();
        Type settingsType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettings");
        (Vector3[] defaults, int[] defaultTriangles) = CreateLight(enclosure, Vector3.zero, Vector3.forward);
        (Vector3[] configured, int[] configuredTriangles) = CreateConfiguredLight(enclosure, Vector3.zero, Vector3.forward, NewSettings(settingsType, 0.43f, 0f, 0.86f, 2.47f, 3.2f, 0f, 0f, 0f, 1f, 0.96f, 1.25f));
        Assert.That(configured.Length, Is.EqualTo(defaults.Length));
        for (int index = 0; index < defaults.Length; index++)
            Assert.That(Vector3.Distance(configured[index], defaults[index]), Is.LessThan(0.000001f));
        Assert.That(configuredTriangles, Is.EqualTo(defaultTriangles));
    }

    [Test]
    public void LightWidthScaleChangesStripThicknessOnly()
    {
        object enclosure = DefaultEnclosure();
        Type settingsType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettings");
        foreach ((float lightWidth, float expectedWidth) in new[] { (0.5f, 0.0875f), (1f, 0.175f), (4f, 0.7f) })
        {
            (Vector3[] vertices, _) = CreateConfiguredLight(enclosure, Vector3.zero, Vector3.forward, NewSettings(settingsType, 1f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, lightWidth));
            Assert.That(Vector3.Distance(vertices[0], vertices[1]), Is.EqualTo(expectedWidth).Within(0.001f), $"LightWidthScale {lightWidth} must scale the strip thickness");
            Vector3 apex = RingCenter(vertices, 48);
            Assert.That(apex.z, Is.EqualTo(-0.35f).Within(0.001f), "strip thickness must not move the vertex");
            foreach (Vector3 vertex in vertices)
                Assert.That(IsFinite(vertex.x) && IsFinite(vertex.y) && IsFinite(vertex.z), Is.True, $"non-finite vertex at scale {lightWidth}");
        }
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    [Test]
    public void GlowMeshTracesCoreStationsWithLateralAndLongitudinalFade()
    {
        object enclosure = DefaultEnclosure();
        Type settingsType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettings");
        object coreSettings = NewSettings(settingsType, 1f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f);
        (Vector3[] coreVertices, _) = CreateConfiguredLight(enclosure, Vector3.zero, Vector3.forward, coreSettings);
        object coreData = GetCoreMeshData(enclosure, coreSettings);
        Color[] coreColors = (Color[])coreData.GetType().GetField("Colors", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(coreData)!;
        object glow = CreateGlow(enclosure, coreData, coreSettings);

        Type glowType = glow.GetType();
        Vector3[] vertices = (Vector3[])glowType.GetField("Vertices", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(glow)!;
        int[] triangles = (int[])glowType.GetField("Triangles", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(glow)!;
        Color[] colors = (Color[])glowType.GetField("Colors", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(glow)!;
        Vector2[] uv = (Vector2[])glowType.GetField("UV", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(glow)!;

        Assert.That(vertices.Length, Is.EqualTo((97 * 14) + (97 * 34)), "97 stations times fourteen broad offsets plus thirty-four narrow HDR offsets");
        Assert.That(triangles.Length, Is.EqualTo((96 * 12 * 2 * 3) + (96 * 32 * 2 * 3)), "twelve broad quads plus thirty-two narrow quads per segment times two triangles");
        Assert.That(colors.Length, Is.EqualTo(vertices.Length));
        Assert.That(uv.Length, Is.EqualTo(vertices.Length));
        foreach (int index in triangles)
            Assert.That(index, Is.InRange(0, vertices.Length - 1));

        foreach (Vector3 vertex in vertices)
            Assert.That(vertex.y, Is.EqualTo(0.001f).Within(0.0001f), "the glow must hover 1mm above the floor");

        float spillReach = 1.5f;
        float whiteReach = Mathf.Min(spillReach * 0.125f, 0.12f);
        float[] distances = { spillReach, 0.75f * spillReach, 0.5f * spillReach, 0.25f * spillReach, whiteReach, whiteReach * 0.5f, 0f, 0f, whiteReach * 0.5f, whiteReach, 0.25f * spillReach, 0.5f * spillReach, 0.75f * spillReach, spillReach };
        for (int station = 0; station < 97; station++)
        {
            Vector3 center = RingCenter(coreVertices, station);
            float longitudinal = coreColors[station * 4].a;
            Assert.That(vertices[(station * 14) + 6].x, Is.EqualTo(coreVertices[(station * 4) + 1].x), $"the negative broad inner boundary must be exactly the core edge X at station {station}");
            Assert.That(vertices[(station * 14) + 6].z, Is.EqualTo(coreVertices[(station * 4) + 1].z), $"the negative broad inner boundary must be exactly the core edge Z at station {station}");
            Assert.That(vertices[(station * 14) + 7].x, Is.EqualTo(coreVertices[station * 4].x), $"the positive broad inner boundary must be exactly the core edge X at station {station}");
            Assert.That(vertices[(station * 14) + 7].z, Is.EqualTo(coreVertices[station * 4].z), $"the positive broad inner boundary must be exactly the core edge Z at station {station}");
            for (int lateral = 0; lateral < 14; lateral++)
            {
                int index = (station * 14) + lateral;
                float progress = distances[lateral] / spillReach;
                float expectedLateral = (1f - progress) * (1f - progress);
                Assert.That(colors[index].a, Is.EqualTo(expectedLateral * longitudinal).Within(0.0001f), $"glow alpha must be the lateral distance profile times the core's end fade at station {station} lateral {lateral}");
            }
        }

        for (int lateral = 0; lateral < 14; lateral++)
        {
            Assert.That(colors[lateral].a, Is.EqualTo(0f).Within(0.000001f), "the left glow tip must fade to zero");
            Assert.That(colors[(96 * 14) + lateral].a, Is.EqualTo(0f).Within(0.000001f), "the right glow tip must fade to zero");
        }

        for (int lateral = 0; lateral < 34; lateral++)
        {
            Assert.That(colors[(97 * 14) + lateral].a, Is.EqualTo(0f).Within(0.000001f), "the left narrow halo tip must fade to zero");
            Assert.That(colors[(97 * 14) + (96 * 34) + lateral].a, Is.EqualTo(0f).Within(0.000001f), "the right narrow halo tip must fade to zero");
        }
    }

    [Test]
    public void EvaluateHdrGlowAlphaGatesBrightestChannel()
    {
        Type settingsType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettings");
        MethodInfo? hdrAlpha = settingsType.GetMethod("EvaluateHdrGlowAlpha", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(hdrAlpha, Is.Not.Null, "the settings snapshot needs the HDR inner-halo gain seam");

        Assert.That((float)hdrAlpha!.Invoke(NewSettings(settingsType, 0.43f, 0f, 0f, 1.3f, 2f, 0f, 0f, 0f, 1f), null)!, Is.EqualTo(0f).Within(0.000001f), "ordinary color at the 1.3 boundary stays dark");
        Assert.That((float)hdrAlpha.Invoke(NewSettings(settingsType, 0.43f, 1.3f, 1.3f, 1.3f, 2f, 0f, 0f, 0f, 1f), null)!, Is.EqualTo(0f).Within(0.000001f), "all channels at 1.3 stay dark");
        Assert.That((float)hdrAlpha.Invoke(NewSettings(settingsType, 0.43f, 0.8f, 0.8f, 0f, 2f, 0f, 0f, 0f, 1f), null)!, Is.EqualTo(0f).Within(0.000001f), "mixed channels below 1.3 stay dark");
        Assert.That((float)hdrAlpha.Invoke(NewSettings(settingsType, 0.43f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f), null)!, Is.EqualTo(0f).Within(0.000001f));
        Assert.That((float)hdrAlpha.Invoke(NewSettings(settingsType, 0.43f, 0f, 0f, 2.3f, 2f, 0f, 0f, 0f, 1f), null)!, Is.EqualTo(1f).Within(0.0001f), "blue 2.3 reaches full gain");
        Assert.That((float)hdrAlpha.Invoke(NewSettings(settingsType, 0.43f, 0f, 0f, 10f, 2f, 0f, 0f, 0f, 1f), null)!, Is.EqualTo(Mathf.Sqrt(8.7f)).Within(0.0001f), "the gain keeps growing with the brightest channel");
        Assert.That((float)hdrAlpha.Invoke(NewSettings(settingsType, 0.43f, 2.3f, 0f, 0f, 2f, 0f, 0f, 0f, 1f), null)!, Is.EqualTo(1f).Within(0.0001f), "any brightest channel gates, not just blue");
        Assert.That((float)hdrAlpha.Invoke(NewSettings(settingsType, 0.43f, 0f, 2.3f, 0f, 2f, 0f, 0f, 0f, 1f), null)!, Is.EqualTo(1f).Within(0.0001f));

        float dim = (float)hdrAlpha.Invoke(NewSettings(settingsType, 0.43f, 0f, 0f, 10f, 2f, 0f, 0f, 0f, 1f), null)!;
        float bright = (float)hdrAlpha.Invoke(NewSettings(settingsType, 10f, 0f, 0f, 10f, 2f, 0f, 0f, 0f, 1f), null)!;
        Assert.That(bright, Is.EqualTo(dim).Within(0.000001f), "brightness must not scale the HDR gate");

        Assert.That((float)hdrAlpha.Invoke(NewSettings(settingsType, 0.43f, 0f, 0f, 2f, 2f, 0f, 0f, 0f, 1f, 1f, 1f, 2.3f), null)!, Is.EqualTo(0f).Within(0.000001f), "raising the threshold above the channel must switch the inner halo off");
        Assert.That((float)hdrAlpha.Invoke(NewSettings(settingsType, 0.43f, 0f, 0f, 2f, 2f, 0f, 0f, 0f, 1f, 1f, 1f, 1f), null)!, Is.EqualTo(1f).Within(0.0001f), "lowering the threshold to 1 lets a dimmer channel through");
    }

    [Test]
    public void NarrowHaloAppendsRawHdrStripWithTenthReach()
    {
        object enclosure = DefaultEnclosure();
        Type settingsType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettings");
        object hdrBlue = NewSettings(settingsType, 10f, 0f, 0f, 10f, 2f, 0f, 0f, 0f, 1f, 1f, 1f, 1.3f, 0.1f, 1f);
        object coreData = GetCoreMeshData(enclosure, hdrBlue);
        (Vector3[] coreVertices, _) = CreateConfiguredLight(enclosure, Vector3.zero, Vector3.forward, hdrBlue);
        Color[] coreColors = (Color[])coreData.GetType().GetField("Colors", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(coreData)!;
        object glow = CreateGlow(enclosure, coreData, hdrBlue);
        Type glowType = glow.GetType();
        Vector3[] vertices = (Vector3[])glowType.GetField("Vertices", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(glow)!;
        Color[] colors = (Color[])glowType.GetField("Colors", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(glow)!;

        float halfWidth = 0.0875f;
        float narrowReach = 1.5f;
        float gain = Mathf.Sqrt(8.7f);
        float[] nativeTable = { 1.000000f, 0.878906f, 0.765625f, 0.660156f, 0.562500f, 0.472656f, 0.390625f, 0.316406f, 0.250000f, 0.191406f, 0.140625f, 0.097656f, 0.062500f, 0.035156f, 0.015625f, 0.003906f, 0.000000f };

        for (int station = 0; station < 97; station++)
        {
            Vector3 center = RingCenter(coreVertices, station);
            Vector3 positive = coreVertices[station * 4];
            Vector3 negative = coreVertices[(station * 4) + 1];
            Vector3 unit = (positive - negative).normalized;
            float longitudinal = coreColors[station * 4].a;
            for (int lateral = 0; lateral < 34; lateral++)
            {
                int index = (97 * 14) + (station * 34) + lateral;
                bool left = lateral < 17;
                float dist = left ? narrowReach * (16f - lateral) / 16f : narrowReach * (lateral - 17f) / 16f;
                Vector3 anchor = left ? coreVertices[(station * 4) + 1] : coreVertices[station * 4];
                Vector3 expected = anchor + (unit * (left ? -1f : 1f) * dist);
                Assert.That(vertices[index].x, Is.EqualTo(expected.x).Within(0.001f), $"narrow vertex {lateral} must anchor to the actual core edge at station {station}");
                Assert.That(vertices[index].z, Is.EqualTo(expected.z).Within(0.001f));
                if (lateral == 16)
                {
                    Assert.That(vertices[index].x, Is.EqualTo(coreVertices[(station * 4) + 1].x), "the negative inner boundary must be exactly the core edge X");
                    Assert.That(vertices[index].z, Is.EqualTo(coreVertices[(station * 4) + 1].z), "the negative inner boundary must be exactly the core edge Z");
                }

                if (lateral == 17)
                {
                    Assert.That(vertices[index].x, Is.EqualTo(coreVertices[station * 4].x), "the positive inner boundary must be exactly the core edge X");
                    Assert.That(vertices[index].z, Is.EqualTo(coreVertices[station * 4].z), "the positive inner boundary must be exactly the core edge Z");
                }

                float expectedAlpha = (left ? nativeTable[16 - lateral] : nativeTable[lateral - 17]) * gain * longitudinal;
                Assert.That(colors[index].r, Is.EqualTo(0f).Within(0.0001f), $"the narrow halo keeps raw HDR channels, no whitening at station {station}");
                Assert.That(colors[index].b, Is.EqualTo(10f).Within(0.0001f));
                Assert.That(colors[index].a, Is.EqualTo(expectedAlpha).Within(0.0001f), $"narrow alpha must follow the native falloff at station {station} lateral {lateral}");
            }
        }

        object dim = NewSettings(settingsType, 10f, 0f, 0f, 1.3f, 2f, 0f, 0f, 0f, 1f, 1f, 1f, 1.3f, 0.1f, 1f);
        object dimGlow = CreateGlow(enclosure, GetCoreMeshData(enclosure, dim), dim);
        Color[] dimColors = (Color[])dimGlow.GetType().GetField("Colors", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(dimGlow)!;
        for (int station = 0; station < 97; station++)
            for (int lateral = 0; lateral < 34; lateral++)
                Assert.That(dimColors[(97 * 14) + (station * 34) + lateral].a, Is.EqualTo(0f).Within(0.000001f), $"channels at or below 1.3 must keep the narrow strip dark at station {station}");

        foreach (float widthScale in new[] { 0.05f, 0.25f })
        {
            object scaled = NewSettings(settingsType, 10f, 0f, 0f, 10f, 2f, 0f, 0f, 0f, 1f, 1f, 1f, 1.3f, widthScale, 1f);
            object scaledGlow = CreateGlow(enclosure, coreData, scaled);
            Vector3[] scaledVerts = (Vector3[])scaledGlow.GetType().GetField("Vertices", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(scaledGlow)!;
            Vector3 center = RingCenter(coreVertices, 48);
            float outer = Mathf.Sqrt(((scaledVerts[(97 * 14) + (48 * 34)].x - center.x) * (scaledVerts[(97 * 14) + (48 * 34)].x - center.x)) + ((scaledVerts[(97 * 14) + (48 * 34)].z - center.z) * (scaledVerts[(97 * 14) + (48 * 34)].z - center.z)));
            Assert.That(outer - halfWidth, Is.EqualTo(15f * widthScale).Within(0.001f), $"inner halo width {widthScale} must scale the narrow reach");
        }

        foreach (float innerGain in new[] { 0f, 1f, 3f, 10f })
        {
            object gained = NewSettings(settingsType, 10f, 0f, 0f, 10f, 2f, 0f, 0f, 0f, 1f, 1f, 1f, 1.3f, 0.1f, innerGain);
            object gainedGlow = CreateGlow(enclosure, coreData, gained);
            Color[] gainedColors = (Color[])gainedGlow.GetType().GetField("Colors", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(gainedGlow)!;
            for (int lateral = 0; lateral < 14; lateral++)
                Assert.That(gainedColors[(48 * 14) + lateral], Is.EqualTo(colors[(48 * 14) + lateral]), $"inner brightness {innerGain} must not touch broad colors");
            for (int lateral = 0; lateral < 34; lateral++)
            {
                Color narrow = gainedColors[(97 * 14) + (48 * 34) + lateral];
                Assert.That(narrow.b, Is.EqualTo(10f * innerGain).Within(0.001f), $"narrow RGB must scale with inner brightness {innerGain}");
                Assert.That(narrow.a, Is.EqualTo(colors[(97 * 14) + (48 * 34) + lateral].a).Within(0.0001f), $"inner brightness {innerGain} must not change narrow alpha");
            }
        }

        int[] narrowTriangles = (int[])glowType.GetField("Triangles", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(glow)!;
        for (int triangle = 96 * 12 * 2; triangle < narrowTriangles.Length / 3; triangle++)
        {
            int[] laterals = new int[3];
            for (int corner = 0; corner < 3; corner++)
                laterals[corner] = (narrowTriangles[(triangle * 3) + corner] - (97 * 14)) % 34;
            bool touchesLeft = laterals.Any(value => value <= 16);
            bool touchesRight = laterals.Any(value => value >= 17);
            Assert.That(touchesLeft && touchesRight, Is.False, $"narrow triangle {triangle} must not bridge across the core center");
        }

        object shifted = NewSettings(settingsType, 10f, 0f, 0f, 10f, 2f, 0.7f, -0.4f, 37f, 0.5f, 1f, 1f, 1.3f, 0.1f, 1f);
        object shiftedCore = GetCoreMeshData(enclosure, shifted);
        (Vector3[] shiftedCoreVerts, _) = CreateConfiguredLight(enclosure, Vector3.zero, Vector3.forward, shifted);
        object shiftedGlow = CreateGlow(enclosure, shiftedCore, shifted);
        Vector3[] shiftedVerts = (Vector3[])shiftedGlow.GetType().GetField("Vertices", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(shiftedGlow)!;
        for (int station = 0; station < 97; station++)
        {
            Assert.That(shiftedVerts[(97 * 14) + (station * 34) + 16].x, Is.EqualTo(shiftedCoreVerts[(station * 4) + 1].x), $"offset/rotated inner boundary must track the core edge at station {station}");
            Assert.That(shiftedVerts[(97 * 14) + (station * 34) + 17].x, Is.EqualTo(shiftedCoreVerts[station * 4].x), $"offset/rotated inner boundary must track the core edge at station {station}");
        }

        object zeroWidth = NewSettings(settingsType, 10f, 0f, 0f, 10f, 2f, 0f, 0f, 0f, 1f, 1f, 1f, 1.3f, 0f, 1f);
        object zeroGlow = CreateGlow(enclosure, coreData, zeroWidth);
        Color[] zeroColors = (Color[])zeroGlow.GetType().GetField("Colors", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(zeroGlow)!;
        for (int station = 0; station < 97; station++)
            for (int lateral = 0; lateral < 34; lateral++)
                Assert.That(zeroColors[(97 * 14) + (station * 34) + lateral].a, Is.EqualTo(0f).Within(0.000001f), $"zero inner width must contribute no narrow alpha at station {station}");
    }

    [Test]
    public void GlowSpillWidensWithBrightnessAndKeepsColoredEdge()
    {
        object enclosure = DefaultEnclosure();
        Type settingsType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettings");
        object dimSettings = NewSettings(settingsType, 1f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f);
        object brightSettings = NewSettings(settingsType, 10f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f);
        Vector3[] dimGlow = GlowVertices(enclosure, GetCoreMeshData(enclosure, dimSettings), dimSettings);
        Vector3[] brightGlow = GlowVertices(enclosure, GetCoreMeshData(enclosure, brightSettings), brightSettings);
        (Vector3[] coreVertices, _) = CreateConfiguredLight(enclosure, Vector3.zero, Vector3.forward, dimSettings);

        Vector3 apex = RingCenter(coreVertices, 48);
        float dimEdge = Mathf.Sqrt(((dimGlow[48 * 14].x - apex.x) * (dimGlow[48 * 14].x - apex.x)) + ((dimGlow[48 * 14].z - apex.z) * (dimGlow[48 * 14].z - apex.z)));
        float brightEdge = Mathf.Sqrt(((brightGlow[48 * 14].x - apex.x) * (brightGlow[48 * 14].x - apex.x)) + ((brightGlow[48 * 14].z - apex.z) * (brightGlow[48 * 14].z - apex.z)));
        Assert.That(dimEdge, Is.EqualTo(0.0875f + 1.5f).Within(0.001f), "at brightness one the spill reaches a meter and a half past the strip edge");
        Assert.That(brightEdge, Is.GreaterThan(dimEdge * 2f), "brightness must widen the spill");

        object brightCore = GetCoreMeshData(enclosure, brightSettings);
        object glow = CreateGlow(enclosure, brightCore, brightSettings);
        Color[] brightColors = (Color[])glow.GetType().GetField("Colors", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(glow)!;
        Assert.That(brightColors[48 * 14 + 6].r, Is.EqualTo(1f).Within(0.001f), "the apex inner vertex must be neutral at high brightness");
        Assert.That(brightColors[48 * 14 + 6].b, Is.EqualTo(1f).Within(0.001f));
        Assert.That(brightColors[(48 * 14) + 8].r, Is.GreaterThan(0.2f).And.LessThan(0.9f), "the white border must blend into the colored edge");
        Assert.That(brightColors[(48 * 14) + 10].r, Is.EqualTo(0f).Within(0.0001f), "the spill past the white band stays raw color");
        Assert.That(brightColors[48 * 14].r, Is.EqualTo(0f).Within(0.0001f), "the outer edge stays colored");
    }

    [Test]
    public void GlowWhiteBleedExtentIsNarrowAndBrightnessGated()
    {
        object enclosure = DefaultEnclosure();
        Type settingsType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettings");
        object coreSettings = NewSettings(settingsType, 1f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f);
        object coreData = GetCoreMeshData(enclosure, coreSettings);
        (Vector3[] coreVertices, _) = CreateConfiguredLight(enclosure, Vector3.zero, Vector3.forward, coreSettings);

        foreach (float brightness in new[] { 0.43f, 0.68f, 1f })
        {
            object dim = NewSettings(settingsType, brightness, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f, 1f, 10f);
            object dimGlow = CreateGlow(enclosure, coreData, dim);
            Color[] dimColors = (Color[])dimGlow.GetType().GetField("Colors", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(dimGlow)!;
            for (int station = 0; station < 97; station++)
                for (int lateral = 0; lateral < 14; lateral++)
                    Assert.That(dimColors[(station * 14) + lateral].r, Is.EqualTo(0f).Within(0.0001f), $"brightness {brightness} must never whiten the halo regardless of bloom brightness");
        }

        foreach (float bloomWidth in new[] { 0.01f, 1f, 4f })
        {
            object bright = NewSettings(settingsType, 10f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f, bloomWidth);
            Vector3[] glowVerts = GlowVertices(enclosure, coreData, bright);
            object glow = CreateGlow(enclosure, coreData, bright);
            Color[] glowColors = (Color[])glow.GetType().GetField("Colors", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(glow)!;
            float whiteBound = Mathf.Min(1.5f * bloomWidth * Mathf.Sqrt(10f) * 0.125f, 0.12f);
            for (int lateral = 0; lateral < 14; lateral++)
            {
                int index = (48 * 14) + lateral;
                Vector3 center = RingCenter(coreVertices, 48);
                float distance = Mathf.Sqrt(((glowVerts[index].x - center.x) * (glowVerts[index].x - center.x)) + ((glowVerts[index].z - center.z) * (glowVerts[index].z - center.z))) - 0.0875f;
                if (distance >= whiteBound)
                    Assert.That(glowColors[index].r, Is.EqualTo(0f).Within(0.0001f), $"bloom width {bloomWidth}: beyond {whiteBound:F3}m the spill must stay blue (vertex {lateral} at {distance:F3}m)");
            }

            float innerTint = glowColors[(48 * 14) + 6].r;
            Assert.That(innerTint, Is.GreaterThan(0.9f), "the strip edge stays white at high brightness");
        }
    }

    [Test]
    public void GlowSpillReachScalesWithBrightnessAndHdrChannelPeak()
    {
        object enclosure = DefaultEnclosure();
        Type settingsType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettings");
        (Vector3[] coreVertices, _) = CreateConfiguredLight(enclosure, Vector3.zero, Vector3.forward, NewSettings(settingsType, 1f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f));

        AssertSpill(enclosure, coreVertices, NewSettings(settingsType, 0.43f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f), 1.5f, "low brightness with unit blue reaches a meter and a half");
        AssertSpill(enclosure, coreVertices, NewSettings(settingsType, 0.43f, 0f, 0f, 10f, 2f, 0f, 0f, 0f, 1f), 1.5f * Mathf.Sqrt(10f), "HDR blue must widen the spill at the same brightness");
        AssertSpill(enclosure, coreVertices, NewSettings(settingsType, 0.43f, 10f, 0f, 0f, 2f, 0f, 0f, 0f, 1f), 1.5f * Mathf.Sqrt(10f), "HDR red widens identically");
        AssertSpill(enclosure, coreVertices, NewSettings(settingsType, 0.43f, 0f, 10f, 0f, 2f, 0f, 0f, 0f, 1f), 1.5f * Mathf.Sqrt(10f), "HDR green widens identically");
        AssertSpill(enclosure, coreVertices, NewSettings(settingsType, 1f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f), 1.5f, "brightness one keeps the authored reach");
        AssertSpill(enclosure, coreVertices, NewSettings(settingsType, 4f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f), 3f, "brightness four doubles the reach");
        AssertSpill(enclosure, coreVertices, NewSettings(settingsType, 10f, 10f, 0f, 0f, 2f, 0f, 0f, 0f, 1f), 15f, "brightness and peak multiply the reach");
        AssertSpill(enclosure, coreVertices, NewSettings(settingsType, 1f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 2f), 1.5f, "light width must not change the spill reach");

        object hdr = NewSettings(settingsType, 0.43f, 10f, 0f, 0f, 2f, 0f, 0f, 0f, 1f);
        object hdrGlow = CreateGlow(enclosure, GetCoreMeshData(enclosure, hdr), hdr);
        Color[] hdrColors = (Color[])hdrGlow.GetType().GetField("Colors", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(hdrGlow)!;
        Assert.That(hdrColors[48 * 14].r, Is.EqualTo(10f).Within(0.001f), "the colored spill must keep its HDR tint, never clamped");
    }

    [Test]
    public void BloomWidthScalesSpillReachWithoutTouchingCore()
    {
        object enclosure = DefaultEnclosure();
        Type settingsType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettings");
        object coreSettings = NewSettings(settingsType, 1f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f);
        object coreData = GetCoreMeshData(enclosure, coreSettings);
        (Vector3[] coreVertices, _) = CreateConfiguredLight(enclosure, Vector3.zero, Vector3.forward, coreSettings);
        Vector3 apex = RingCenter(coreVertices, 48);

        foreach ((float bloomWidth, float expectedReach) in new[] { (0.25f, 0.375f), (1f, 1.5f), (4f, 6f) })
        {
            object settings = NewSettings(settingsType, 1f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f, bloomWidth);
            Vector3[] glow = GlowVertices(enclosure, coreData, settings);
            float outer = Mathf.Sqrt(((glow[48 * 14].x - apex.x) * (glow[48 * 14].x - apex.x)) + ((glow[48 * 14].z - apex.z) * (glow[48 * 14].z - apex.z)));
            Assert.That(outer - 0.0875f, Is.EqualTo(expectedReach).Within(0.001f), $"BloomWidthScale {bloomWidth} must scale the spill reach independently of the strip");
        }

        object zero = NewSettings(settingsType, 1f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f, 0f);
        Vector3[] zeroGlow = GlowVertices(enclosure, coreData, zero);
        float zeroOuter = Mathf.Sqrt(((zeroGlow[48 * 14].x - apex.x) * (zeroGlow[48 * 14].x - apex.x)) + ((zeroGlow[48 * 14].z - apex.z) * (zeroGlow[48 * 14].z - apex.z)));
        Assert.That(zeroOuter, Is.EqualTo(0.0875f).Within(0.001f), "zero bloom width collapses the spill to the strip edge without producing invalid geometry");
        foreach (Vector3 vertex in zeroGlow)
            Assert.That(IsFinite(vertex.x) && IsFinite(vertex.y) && IsFinite(vertex.z), Is.True, "zero bloom width must keep glow vertices finite");

        object wideCore = GetCoreMeshData(enclosure, NewSettings(settingsType, 1f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f, 4f));
        Type meshDataType = coreData.GetType();
        Vector3[] wideVertices = (Vector3[])meshDataType.GetField("Vertices", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(wideCore)!;
        Color[] baseColors = (Color[])meshDataType.GetField("Colors", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(coreData)!;
        Color[] wideColors = (Color[])meshDataType.GetField("Colors", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(wideCore)!;
        Assert.That(wideVertices.Length, Is.EqualTo(coreVertices.Length));
        for (int index = 0; index < wideVertices.Length; index++)
        {
            Assert.That(wideVertices[index], Is.EqualTo(coreVertices[index]), $"bloom width must not move core vertex {index}");
            Assert.That(wideColors[index].a, Is.EqualTo(baseColors[index].a).Within(0.000001f), $"bloom width must not change core vertex alpha {index}");
        }
    }

    [Test]
    public void BloomBrightnessScalesGlowAlphaOnly()
    {
        Type settingsType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettings");
        MethodInfo? evaluateGlow = settingsType.GetMethod("EvaluateGlowColor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo? evaluateColor = settingsType.GetMethod("EvaluateColor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(evaluateGlow, Is.Not.Null);
        Assert.That(evaluateColor, Is.Not.Null);

        foreach ((float bloomBrightness, float expectedAlpha) in new[] { (0f, 0f), (0.5f, 12.5f), (1f, 25f), (10f, 250f) })
        {
            object settings = NewSettings(settingsType, 10f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f, 1f, bloomBrightness);
            Color glow = (Color)evaluateGlow!.Invoke(settings, null)!;
            Assert.That(glow.a, Is.EqualTo(expectedAlpha).Within(0.001f), $"bloom brightness {bloomBrightness} must scale glow alpha linearly");
            Assert.That(glow.r, Is.EqualTo(1f).Within(0.0001f), "glow material stays white");
            Color core = (Color)evaluateColor!.Invoke(settings, null)!;
            Assert.That(core.a, Is.EqualTo(10f).Within(0.0001f), "bloom brightness must not touch the core payload");
            Assert.That(core.b, Is.EqualTo(20f).Within(0.001f));
        }

        MethodInfo evaluateSpread = settingsType.GetMethod("EvaluateGlowSpread", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        object enclosure = DefaultEnclosure();
        object baselineSettings = NewSettings(settingsType, 10f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f, 1f, 1f);
        object coreData = GetCoreMeshData(enclosure, baselineSettings);
        Vector3[] baselineGlow = GlowVertices(enclosure, coreData, baselineSettings);
        float baselineSpread = (float)evaluateSpread.Invoke(baselineSettings, null)!;
        foreach (float bloomBrightness in new[] { 0f, 0.5f, 1f, 10f })
        {
            object settings = NewSettings(settingsType, 10f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f, 1f, bloomBrightness);
            Assert.That((float)evaluateSpread.Invoke(settings, null)!, Is.EqualTo(baselineSpread).Within(0.000001f), $"bloom brightness {bloomBrightness} must not alter the spill spread");
            Vector3[] glow = GlowVertices(enclosure, coreData, settings);
            Assert.That(glow.Length, Is.EqualTo(baselineGlow.Length));
            for (int index = 0; index < glow.Length; index++)
                Assert.That(glow[index], Is.EqualTo(baselineGlow[index]), $"bloom brightness must not move glow vertex {index}");
        }
    }

    [Test]
    public void WithBuildersPreserveHdrControls()
    {
        Type settingsType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettings");
        object settings = NewSettings(settingsType, 1f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f, 3.5f, 2.5f, 2f, 0.4f);
        foreach (string name in new[] { "WithBrightness", "WithRed", "WithGreen", "WithBlue", "WithWidthScale", "WithOffsetX", "WithOffsetZ", "WithRotation", "WithLightWidthScale" })
        {
            MethodInfo? with = settingsType.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(with, Is.Not.Null, $"missing settings builder {name}");
            object copy = with!.Invoke(settings, [0.5f])!;
            Assert.That(SettingsField(copy, "BloomWidthScale"), Is.EqualTo(3.5f).Within(0.000001f), $"{name} must preserve the bloom width");
            Assert.That(SettingsField(copy, "BloomBrightness"), Is.EqualTo(2.5f).Within(0.000001f), $"{name} must preserve the bloom brightness");
            Assert.That(SettingsField(copy, "HdrGlowThreshold"), Is.EqualTo(2f).Within(0.000001f), $"{name} must preserve the inner threshold");
            Assert.That(SettingsField(copy, "HdrGlowWidthScale"), Is.EqualTo(0.4f).Within(0.000001f), $"{name} must preserve the inner width");
        }

        foreach (string name in new[] { "WithBloomWidthScale", "WithBloomBrightness", "WithHdrGlowThreshold", "WithHdrGlowWidthScale" })
            Assert.That(settingsType.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), Is.Not.Null, $"missing settings builder {name}");

        object widened = settingsType.GetMethod("WithBloomWidthScale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(settings, [0.25f])!;
        Assert.That(SettingsField(widened, "BloomWidthScale"), Is.EqualTo(0.25f).Within(0.000001f));
        Assert.That(SettingsField(widened, "Brightness"), Is.EqualTo(1f).Within(0.000001f), "WithBloomWidthScale must preserve the other fields");
        Assert.That(SettingsField(widened, "BloomBrightness"), Is.EqualTo(2.5f).Within(0.000001f));
        Assert.That(SettingsField(widened, "HdrGlowThreshold"), Is.EqualTo(2f).Within(0.000001f), "WithBloomWidthScale must preserve the inner threshold");
        Assert.That(SettingsField(widened, "HdrGlowWidthScale"), Is.EqualTo(0.4f).Within(0.000001f), "WithBloomWidthScale must preserve the inner width");
        object brightened = settingsType.GetMethod("WithBloomBrightness", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(settings, [8f])!;
        Assert.That(SettingsField(brightened, "BloomBrightness"), Is.EqualTo(8f).Within(0.000001f));
        Assert.That(SettingsField(brightened, "BloomWidthScale"), Is.EqualTo(3.5f).Within(0.000001f));
        Assert.That(SettingsField(brightened, "HdrGlowThreshold"), Is.EqualTo(2f).Within(0.000001f), "WithBloomBrightness must preserve the inner threshold");
        Assert.That(SettingsField(brightened, "HdrGlowWidthScale"), Is.EqualTo(0.4f).Within(0.000001f), "WithBloomBrightness must preserve the inner width");
        object thresholded = settingsType.GetMethod("WithHdrGlowThreshold", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(settings, [0.5f])!;
        Assert.That(SettingsField(thresholded, "HdrGlowThreshold"), Is.EqualTo(0.5f).Within(0.000001f));
        Assert.That(SettingsField(thresholded, "BloomWidthScale"), Is.EqualTo(3.5f).Within(0.000001f), "WithHdrGlowThreshold must preserve the other controls");
        object narrowed = settingsType.GetMethod("WithHdrGlowWidthScale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(settings, [0.05f])!;
        Assert.That(SettingsField(narrowed, "HdrGlowWidthScale"), Is.EqualTo(0.05f).Within(0.000001f));
        Assert.That(SettingsField(narrowed, "HdrGlowThreshold"), Is.EqualTo(2f).Within(0.000001f), "WithHdrGlowWidthScale must preserve the threshold");
    }

    [Test]
    public void GeneratedStorePersistsAllFourteenThroughJsonRoundTrip()
    {
        Type configType = GetPluginType("ChromaGLS.Settings.Config");
        Type uiType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettingsUI");

        MethodInfo? create = typeof(IPA.Config.Stores.GeneratedStore).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method => method.Name == "Create" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0);
        Assert.That(create, Is.Not.Null, "generated store Create<T> must exist");
        object config = create!.MakeGenericMethod(configType).Invoke(null, null)!;
        Assert.That(config, Is.InstanceOf<IPA.Config.IConfigStore>(), "the generated config must implement IConfigStore");
        IPA.Config.IConfigStore store = (IPA.Config.IConfigStore)config;

        object service = CreateService(config);
        object controller = CreateController(config, service);
        FieldInfo sessionField = uiType.GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object session = sessionField.GetValue(controller)!;
        Invoke(session, "OpenExisting", "legacy-parabola");

        (string UIName, float Slider)[] drives =
        [
            ("parabola-brightness", 0.75f), ("parabola-red", 0.5f), ("parabola-green", 0.75f), ("parabola-blue", 0.25f),
            ("parabola-width", 0.5f), ("parabola-light-width", 0.75f), ("parabola-bloom-width", 0.75f), ("parabola-bloom-brightness", 0.75f),
            ("parabola-hdr-glow-threshold", 0.75f), ("parabola-hdr-glow-width", 0.5f), ("parabola-hdr-glow-brightness", 0.75f),
            ("parabola-offset-z", 1.5f), ("parabola-offset-x", -2.5f), ("parabola-rotation", 90f),
        ];
        foreach ((string uiName, float slider) in drives)
        {
            MemberInfo member = FindUIValue(uiType, uiName)!;
            ((PropertyInfo)member).SetValue(controller, slider);
        }

        MemberInfo saveAction = FindUIAction(uiType, "parabola-save")!;
        ((MethodInfo)saveAction).Invoke(controller, null);

        Dictionary<string, float> expected = new()
        {
            ["ParabolicFloorLightBrightness"] = 3.25f, ["ParabolicFloorLightRed"] = 1f, ["ParabolicFloorLightGreen"] = 5.75f,
            ["ParabolicFloorLightBlue"] = 0.5f, ["ParabolicFloorLightWidthScale"] = 2.375f, ["ParabolicFloorLightLightWidthScale"] = 2.2504375f,
            ["ParabolicFloorLightBloomWidthScale"] = 2.25f, ["ParabolicFloorLightBloomBrightness"] = 3.25f,
            ["ParabolicFloorLightHdrGlowThreshold"] = 3.25f, ["ParabolicFloorLightHdrGlowWidthScale"] = 0.25f, ["ParabolicFloorLightHdrGlowBrightness"] = 3.25f,
            ["ParabolicFloorLightOffsetZ"] = 1.5f, ["ParabolicFloorLightOffsetX"] = -2.5f, ["ParabolicFloorLightRotation"] = 90f,
        };
        AssertSavedValues(config);

        FieldInfo? requiresSave = typeof(IPA.Config.Config).Assembly.GetType("IPA.Config.ConfigRuntime", false)?.GetField("requiresSave", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(requiresSave, Is.Not.Null, "the IPA autosave queue field must exist");
        object[] queued = ((System.Collections.IEnumerable)requiresSave!.GetValue(null)!).Cast<object>().ToArray();
        Assert.That(queued.OfType<IPA.Config.IConfigStore>().Any(candidate => ReferenceEquals(candidate.WriteSyncObject, store.WriteSyncObject)), Is.True, "saving must queue a dirty store sharing the generated config's WriteSyncObject");

        FileInfo file = new(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"probe-config-{Guid.NewGuid():N}.json"));
        ConstructorInfo providerCtor = typeof(IPA.Config.ConfigProvider).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [typeof(FileInfo), typeof(IPA.Config.IConfigProvider)],
            null)!;
        Assert.That(providerCtor, Is.Not.Null, "ConfigProvider(FileInfo, IConfigProvider) must exist");
        Type jsonProviderType = typeof(IPA.Config.ConfigProvider).Assembly.GetType("IPA.Config.Providers.JsonConfigProvider", true)!;
        object jsonImpl = Activator.CreateInstance(jsonProviderType, nonPublic: true)!;
        object provider = providerCtor.Invoke([file, jsonImpl]);

        store.WriteSyncObject.EnterReadLock();
        try
        {
            store.WriteTo((IPA.Config.ConfigProvider)provider);
        }
        finally
        {
            store.WriteSyncObject.ExitReadLock();
        }

        Assert.That(File.Exists(file.FullName), Is.True, "the generated store must serialize through the JSON provider");
        JObject json = JObject.Parse(File.ReadAllText(file.FullName));
        JProperty? lightsJson = json.Descendants().OfType<JProperty>().FirstOrDefault(candidate => candidate.Name == "MenuLightsJson");
        Assert.That(lightsJson, Is.Not.Null, "the generated config must serialize the saved light collection");
        Assert.That(lightsJson!.Value.Value<string>(), Is.EqualTo(GetConfigProperty(config, "MenuLightsJson")));
        Assert.That(JArray.Parse(lightsJson.Value.Value<string>()!).Count, Is.EqualTo(2));

        Assert.That(json.Descendants().OfType<JProperty>().Any(candidate => candidate.Name == "Enabled"), Is.True, "unrelated persisted toggles must survive in the same file");

        object fresh = create.MakeGenericMethod(configType).Invoke(null, null)!;
        IPA.Config.IConfigStore freshStore = (IPA.Config.IConfigStore)fresh;
        freshStore.WriteSyncObject.EnterWriteLock();
        try
        {
            freshStore.ReadFrom((IPA.Config.ConfigProvider)provider);
        }
        finally
        {
            freshStore.WriteSyncObject.ExitWriteLock();
        }

        AssertSavedValues(fresh);

        object freshController = CreateController(fresh, CreateService(fresh));
        object freshSession = uiType.GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(freshController)!;
        Invoke(freshSession, "OpenExisting", "legacy-parabola");
        AssertSettings(ReadSettings(freshSession, "Working"), 3.25f, 1f, 5.75f, 0.5f, 2.375f, -2.5f, 1.5f, 90f, 2.2504375f, 2.25f, 3.25f, 3.25f, 0.25f, 3.25f, "a fresh session must open the persisted values");

        string beforeCancel = (string)GetConfigProperty(fresh, "MenuLightsJson");
        ((PropertyInfo)FindUIValue(uiType, "parabola-brightness")!).SetValue(freshController, 0.95f);
        ((MethodInfo)FindUIAction(uiType, "parabola-cancel")!).Invoke(freshController, null);
        Assert.That(IsEditing(freshSession), Is.False);
        Assert.That(GetConfigProperty(fresh, "MenuLightsJson"), Is.EqualTo(beforeCancel), "cancel must preserve the persisted collection");
        AssertSavedValues(fresh);
        Invoke(freshSession, "OpenExisting", "legacy-parabola");
        AssertSettings(ReadSettings(freshSession, "Working"), 3.25f, 1f, 5.75f, 0.5f, 2.375f, -2.5f, 1.5f, 90f, 2.2504375f, 2.25f, 3.25f, 3.25f, 0.25f, 3.25f, "cancel and reopen must restore all fourteen persisted values");
        Invoke(controller, "Dispose");
        Invoke(freshController, "Dispose");

        void AssertSavedValues(object targetConfig)
        {
            object saved = DefinitionSettings(ReadPersistedDefinition(targetConfig));
            foreach (KeyValuePair<string, float> pair in expected)
            {
                string field = pair.Key.Substring("ParabolicFloorLight".Length);
                Assert.That(SettingsField(saved, field), Is.EqualTo(pair.Value).Within(0.0001f), $"saved {field} must round-trip");
            }
        }
    }

    private static void AssertSpill(object enclosure, Vector3[] coreVertices, object settings, float expectedReach, string message)
    {
        Vector3[] glow = GlowVertices(enclosure, GetCoreMeshData(enclosure, settings), settings);
        Type settingsType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettings");
        float lightWidth = (float)settingsType.GetField("LightWidthScale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(settings)!;
        float halfWidth = 0.0875f * lightWidth;
        Vector3 apex = RingCenter(coreVertices, 48);
        float outer = Mathf.Sqrt(((glow[48 * 14].x - apex.x) * (glow[48 * 14].x - apex.x)) + ((glow[48 * 14].z - apex.z) * (glow[48 * 14].z - apex.z)));
        Assert.That(outer - halfWidth, Is.EqualTo(expectedReach).Within(0.001f), message);
    }

    [Test]
    public void GlowReachFromStripEdgesDoesNotScaleWithLightWidthOrCurveScale()
    {
        object enclosure = DefaultEnclosure();
        Type settingsType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettings");
        object wideCurve = NewSettings(settingsType, 1f, 0f, 0f, 1f, 4f, 0f, 0f, 0f, 1f);
        object thickStrip = NewSettings(settingsType, 1f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 2f);
        Vector3[] glowWide = GlowVertices(enclosure, GetCoreMeshData(enclosure, wideCurve), wideCurve);
        Vector3[] glowThick = GlowVertices(enclosure, GetCoreMeshData(enclosure, thickStrip), thickStrip);
        (Vector3[] coreWide, _) = CreateConfiguredLight(enclosure, Vector3.zero, Vector3.forward, wideCurve);
        (Vector3[] coreThick, _) = CreateConfiguredLight(enclosure, Vector3.zero, Vector3.forward, thickStrip);

        for (int station = 0; station < 97; station++)
        {
            Vector3 centerWide = RingCenter(coreWide, station);
            Vector3 centerThick = RingCenter(coreThick, station);
            float wideLateral = Mathf.Max(
                Mathf.Sqrt(((glowWide[station * 14].x - centerWide.x) * (glowWide[station * 14].x - centerWide.x)) + ((glowWide[station * 14].z - centerWide.z) * (glowWide[station * 14].z - centerWide.z))),
                Mathf.Sqrt(((glowWide[(station * 14) + 13].x - centerWide.x) * (glowWide[(station * 14) + 13].x - centerWide.x)) + ((glowWide[(station * 14) + 13].z - centerWide.z) * (glowWide[(station * 14) + 13].z - centerWide.z))));
            float thickLateral = Mathf.Max(
                Mathf.Sqrt(((glowThick[station * 14].x - centerThick.x) * (glowThick[station * 14].x - centerThick.x)) + ((glowThick[station * 14].z - centerThick.z) * (glowThick[station * 14].z - centerThick.z))),
                Mathf.Sqrt(((glowThick[(station * 14) + 13].x - centerThick.x) * (glowThick[(station * 14) + 13].x - centerThick.x)) + ((glowThick[(station * 14) + 13].z - centerThick.z) * (glowThick[(station * 14) + 13].z - centerThick.z))));
            Assert.That(thickLateral - 0.175f, Is.EqualTo(wideLateral - 0.0875f).Within(0.001f), $"strip thickness must not change the broad bloom reach from its edge (station {station})");

            int narrowStart = (97 * 14) + (station * 34);
            Vector3 wideSpan = glowWide[narrowStart] - glowWide[narrowStart + 16];
            Vector3 thickSpan = glowThick[narrowStart] - glowThick[narrowStart + 16];
            Assert.That(thickSpan.magnitude, Is.EqualTo(wideSpan.magnitude).Within(0.001f), $"strip thickness must not change the inner bloom width (station {station})");
        }
    }

    private static object GetCoreMeshData(object enclosure, object settings)
    {
        MethodInfo tryCreate = GetGeometryMethod("TryCreateConfigured");
        object[] arguments = [enclosure, Vector3.zero, Vector3.forward, settings, null!];
        Assert.That(tryCreate.Invoke(null, arguments), Is.EqualTo(true));
        return arguments[4]!;
    }

    private static object CreateGlow(object enclosure, object coreData, object settings)
    {
        Type? geometryType = Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings.ParabolicFloorLightGeometry", false);
        MethodInfo? createGlow = geometryType!.GetMethod("CreateGlow", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(createGlow, Is.Not.Null, "the floor glow needs a managed geometry seam");
        object? glow = createGlow!.Invoke(null, [enclosure, coreData, settings]);
        Assert.That(glow, Is.Not.Null);
        return glow!;
    }

    private static Vector3[] GlowVertices(object enclosure, object coreData, object settings)
    {
        object glow = CreateGlow(enclosure, coreData, settings);
        return (Vector3[])glow.GetType().GetField("Vertices", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(glow)!;
    }

    [Test]
    public void BothMenuViewsExposeTweakButtonLeftOfParabolaToggle()
    {
        foreach (string resource in new[] { "ChromaGLS.Settings.modifiers.bsml", "ChromaGLS.Settings.modifiers-minimal.bsml" })
        {
            XElement root = LoadResourceXml(resource);
            XElement? row = root.Descendants().FirstOrDefault(element =>
                element.Name.LocalName == "bool-setting" && (string?)element.Attribute("value") == "parabola-light-enabled");
            Assert.That(row, Is.Not.Null, $"{resource} needs the menu floor light toggle");
            Assert.That((string?)row!.Attribute("id"), Is.EqualTo("menu-floor-light-setting"));
            Assert.That((string?)row.Attribute("text"), Is.EqualTo("Menu lights"));
            Assert.That((string?)row.Attribute("apply-on-change"), Is.EqualTo("true"));
            Assert.That(row.Parent?.Name.LocalName, Is.EqualTo("settings-container"), "the row must sit directly in the settings container so its switch aligns with the other toggles");

            XElement? button = row.Elements().FirstOrDefault(element => element.Name.LocalName == "primary-button");
            Assert.That(button, Is.Not.Null, "the Tweak button must be nested inside the toggle row");
            Assert.That((string?)button!.Attribute("id"), Is.EqualTo("menu-floor-light-tweak"));
            Assert.That((string?)button.Attribute("text"), Is.EqualTo("Tweak"));
            Assert.That((string?)button.Attribute("on-click"), Is.EqualTo("parabola-open"));
            Assert.That(row.Elements().Count(), Is.EqualTo(1), "only the Tweak button belongs inside the toggle row");
        }

        Type settings = GetPluginType("ChromaGLS.Settings.ChromaGLSSettingsUI");
        string[] componentIds = settings.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .SelectMany(field => field.GetCustomAttributes(false))
            .Select(attribute =>
                (attribute.GetType().GetProperty("Id")?.GetValue(attribute)
                    ?? attribute.GetType().GetField("id")?.GetValue(attribute)) as string)
            .Where(id => id != null)
            .Select(id => id!)
            .ToArray();
        Assert.That(componentIds, Does.Contain("menu-floor-light-setting"), "the toggle row must bind a UIComponent for switch anchoring");
        Assert.That(componentIds, Does.Contain("menu-floor-light-tweak"), "the nested Tweak button must bind a UIComponent for post-parse positioning");
    }

    [Test]
    public void TweakModalBindsAllLightSlidersWithExpectedRanges()
    {
        XElement root = LoadResourceXml("ChromaGLS.Settings.parabolic-floor-light.bsml");
        XElement? modal = root.Descendants().FirstOrDefault(element => element.Name.LocalName == "modal");
        Assert.That(modal, Is.Not.Null, "the tweak view needs a dedicated modal");
        Assert.That((string?)modal!.Attribute("click-off-closes"), Is.EqualTo("false"));
        Assert.That((string?)modal.Attribute("show-event"), Is.EqualTo("show-parabola"));
        Assert.That((string?)modal.Attribute("hide-event"), Is.EqualTo("hide-parabola"));

        Assert.That((string?)modal.Attribute("size-delta-x"), Is.Not.Null, "the modal rect needs an explicit width; the tag anchors it centered with zero size");
        Assert.That((string?)modal.Attribute("size-delta-x"), Is.EqualTo("110"), "the modal must be wide enough for the long labels to fit without wrapping");
        Assert.That((string?)modal.Attribute("size-delta-y"), Is.EqualTo("72"), "the modal stays bounded while settings scroll instead of pushing buttons off-screen");
        XElement? vertical = modal.Descendants().FirstOrDefault(element => element.Name.LocalName == "vertical");
        Assert.That(vertical, Is.Not.Null);
        Assert.That((string?)vertical!.Elements().FirstOrDefault()?.Attribute("text"), Is.EqualTo("Menu light"), "the editor identifies one light rather than the whole collection");
        Assert.That((string?)vertical!.Attribute("vertical-fit"), Is.EqualTo("Unconstrained"), "the vertical layout must not fit-content inside the fixed modal");

        XElement? scrollView = modal.Descendants().FirstOrDefault(element => element.Name.LocalName == "scroll-view");
        Assert.That(scrollView, Is.Not.Null, "eleven sliders exceed the bounded modal and need a native scroll view");
        Assert.That((string?)scrollView!.Attribute("min-height"), Is.EqualTo("50"), "the scroll viewport must be bounded so scrolling actually engages");
        Assert.That((string?)scrollView.Attribute("preferred-height"), Is.EqualTo("50"));
        Assert.That((string?)scrollView.Attribute("flexible-height"), Is.EqualTo("0"), "the scroll viewport must not grow beyond its bounded height");

        XElement? scrollContent = scrollView.Descendants().FirstOrDefault(element => element.Name.LocalName == "vertical");
        Assert.That(scrollContent, Is.Not.Null, "the scroll view needs a vertical content container");
        Assert.That((string?)scrollContent!.Attribute("vertical-fit"), Is.EqualTo("PreferredSize"), "content must size to the sliders so it overflows the viewport");
        Assert.That((string?)scrollContent.Attribute("horizontal-fit"), Is.EqualTo("PreferredSize"));
        Assert.That((string?)scrollContent.Attribute("child-control-height"), Is.EqualTo("true"));
        Assert.That((string?)scrollContent.Attribute("preferred-width"), Is.EqualTo("96"), "the content must be wide enough for the full labels plus the scrollbar");
        Assert.That((string?)scrollContent.Attribute("spacing"), Is.EqualTo("1.5"));

        XElement? buttonRow = modal.Descendants().FirstOrDefault(element => element.Name.LocalName == "horizontal" && element.Descendants().Any(descendant => descendant.Name.LocalName == "button"));
        Assert.That(buttonRow, Is.Not.Null, "Cancel and Save must share a button row");
        Assert.That(buttonRow!.Ancestors().Any(ancestor => ancestor.Name.LocalName == "scroll-view"), Is.False, "Cancel/Save must stay pinned outside the scroll view");
        XElement[] headerElements = vertical.Elements().ToArray();
        Assert.That(Array.IndexOf(headerElements, buttonRow), Is.LessThan(Array.IndexOf(headerElements, scrollView)), "the button row must sit between the title and the scroll view");

        foreach (XElement slider in modal.Descendants().Where(element => element.Name.LocalName == "slider-setting"))
        {
            Assert.That((string?)slider.Attribute("preferred-height"), Is.EqualTo("7"), "slider rows must share a bounded preferred height");
            Assert.That((string?)slider.Attribute("min-height"), Is.EqualTo("7"), "slider rows must keep a bounded minimum height inside the scroll content");
            Assert.That((string?)slider.Attribute("word-wrapping"), Is.EqualTo("false"), "labels must not wrap onto the next row");
            Assert.That((string?)slider.Attribute("enable-auto-sizing"), Is.EqualTo("true"));
            Assert.That((string?)slider.Attribute("font-size-min"), Is.EqualTo("2.5"));
            Assert.That((string?)slider.Attribute("font-size-max"), Is.EqualTo("3.5"));
            Assert.That((string?)slider.Attribute("overflow-mode"), Is.EqualTo("Ellipsis"), "overlong labels must truncate instead of drawing over the slider");
        }

        Assert.That(modal.Descendants().Where(element => element.Name.LocalName == "slider-setting").All(slider => slider.Ancestors().Any(ancestor => ancestor.Name.LocalName == "scroll-view")), Is.True, "every slider must live inside the scroll content");

        XElement[] contentSliders = scrollContent!.Descendants().Where(element => element.Name.LocalName == "slider-setting").ToArray();
        float contentSpacing = (float)scrollContent.Attribute("spacing")!;
        float contentMinHeight = contentSliders.Select(slider => (float)slider.Attribute("min-height")!).Sum() + contentSpacing * (contentSliders.Length - 1);
        float viewportHeight = (float)scrollView.Attribute("preferred-height")!;
        Assert.That(contentMinHeight, Is.GreaterThan(viewportHeight), "the slider content must be taller than the viewport or scrolling never engages");

        float outerSpacing = (float)vertical!.Attribute("spacing")!;
        float outerPad = (float)vertical.Attribute("pad")!;
        float headerHeight = headerElements.Sum(element => (float)element.Attribute("preferred-height")!) + (outerSpacing * (headerElements.Length - 1)) + (outerPad * 2f);
        float modalHeight = (float)modal.Attribute("size-delta-y")!;
        Assert.That(headerHeight, Is.LessThanOrEqualTo(modalHeight), "title, button row, and viewport must fit the modal");
        Assert.That((string?)buttonRow.Attribute("min-height"), Is.EqualTo("8"), "the pinned button row must reserve its height outside the scroll region");

        (string Value, string Label, float Min, float Max)[] expected =
        [
            ("parabola-brightness", "Brightness", 0f, 1f),
            ("parabola-red", "R", 0f, 1f),
            ("parabola-green", "G", 0f, 1f),
            ("parabola-blue", "B", 0f, 1f),
            ("parabola-width", "Parabola scale", 0f, 1f),
            ("parabola-light-width", "Light width", 0f, 1f),
            ("parabola-bloom-width", "Floor bloom width", 0f, 1f),
            ("parabola-bloom-brightness", "Floor bloom brightness", 0f, 1f),
            ("parabola-hdr-glow-threshold", "Inner bloom threshold", 0f, 1f),
            ("parabola-hdr-glow-width", "Inner bloom width", 0f, 1f),
            ("parabola-hdr-glow-brightness", "Inner bloom brightness", 0f, 1f),
            ("parabola-offset-z", "Forward/back", -10f, 10f),
            ("parabola-offset-x", "Left/right", -10f, 10f),
            ("parabola-rotation", "Yaw", -180f, 180f),
            ("light-height", "Up/down", 0f, 1f),
            ("light-depth", "Underside depth", 0.001f, 2f),
            ("floor-height", "Floor light height", 0.001f, 2f),
            ("light-size", "Side length / diameter", 0.1f, 10f),
            ("light-end-length", "End length (0 = automatic)", 0f, 20f),
            ("light-fade-start", "Fade start", 0f, 0.95f),
            ("light-rotation-x", "Pitch", -180f, 180f),
            ("light-rotation-z", "Roll", -180f, 180f),
        ];
        XElement[] sliders = scrollContent!.Descendants().Where(element => element.Name.LocalName == "slider-setting").ToArray();
        Assert.That(sliders.Length, Is.EqualTo(expected.Length), "the fourteen original and seven new sliders must live inside the scroll content");
        foreach ((string value, string label, float min, float max) in expected)
        {
            XElement slider = sliders.Single(element => (string?)element.Attribute("value") == value);
            Assert.That((string?)slider.Attribute("text"), Is.EqualTo(label));
            Assert.That((float)slider.Attribute("min")!, Is.EqualTo(min));
            Assert.That((float)slider.Attribute("max")!, Is.EqualTo(max));
            Assert.That((string?)slider.Attribute("get-event"), Is.EqualTo("refresh-parabola"), "each slider refreshes only from the session snapshot");
            Assert.That((string?)slider.Attribute("apply-on-change"), Is.EqualTo("true"));
        }

        XElement[] buttons = modal.Descendants().Where(element => element.Name.LocalName == "button").ToArray();
        Assert.That(buttons.Select(element => (string?)element.Attribute("on-click")).ToArray(), Is.EqualTo(new[] { "parabola-cancel", "parabola-reset", "parabola-save" }), "the pinned row must order Cancel, Reset, Save");

        Type uiType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettingsUI");
        foreach (string name in expected.Select(pair => pair.Value))
            Assert.That(FindUIValue(uiType, name), Is.Not.Null, $"the controller must bind UIValue {name}");
        Assert.That(FindUIValue(uiType, "parabola-light-width"), Is.Not.Null);
        foreach (string action in new[] { "parabola-cancel", "parabola-reset", "parabola-save" })
            Assert.That(FindUIAction(uiType, action), Is.Not.Null, $"the controller must bind UIAction {action}");
    }

    [Test]
    public void EvaluateColorFeedsNativeQuadraticBloomPayload()
    {
        Type settingsType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettings");
        MethodInfo evaluateColor = settingsType.GetMethod("EvaluateColor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        MethodInfo? evaluateBloom = settingsType.GetMethod("EvaluateBloomMultiplier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(evaluateBloom, Is.Not.Null, "the settings snapshot needs the bloom multiplier seam");

        object dim = NewSettings(settingsType, 0.25f, 10f, 2f, 0f, 2f, 0f, 0f, 0f, 1f);
        Color dimColor = (Color)evaluateColor!.Invoke(dim, null)!;
        Assert.That(dimColor.r, Is.EqualTo(5f).Within(0.001f), "below the white threshold the tint passes through HDR channels");
        Assert.That(dimColor.g, Is.EqualTo(1f).Within(0.001f));
        Assert.That(dimColor.b, Is.EqualTo(0f).Within(0.001f));
        Assert.That(dimColor.a, Is.EqualTo(0.25f).Within(0.0001f));

        object blue = NewSettings(settingsType, 1f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f);
        Color blueColor = (Color)evaluateColor.Invoke(blue, null)!;
        Assert.That(blueColor.r, Is.EqualTo(2f).Within(0.001f), "at brightness one the core is neutral white");
        Assert.That(blueColor.g, Is.EqualTo(2f).Within(0.001f));
        Assert.That(blueColor.b, Is.EqualTo(2f).Within(0.001f));
        Assert.That(blueColor.a, Is.EqualTo(1f).Within(0.0001f));

        object half = NewSettings(settingsType, 0.75f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f);
        Color halfColor = (Color)evaluateColor.Invoke(half, null)!;
        Assert.That(halfColor.r, Is.EqualTo(0.75f).Within(0.001f), "mid-range brightness blends toward white smoothly");
        Assert.That(halfColor.b, Is.EqualTo(1.5f).Within(0.001f));

        foreach (float brightness in new[] { 0f, 0.25f, 0.5f, 1f, 4f, 10f })
        {
            object settings = NewSettings(settingsType, brightness, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f);
            Color color = (Color)evaluateColor.Invoke(settings, null)!;
            float bloom = (float)evaluateBloom!.Invoke(settings, null)!;
            Assert.That(color.a * bloom, Is.EqualTo(brightness * brightness).Within(0.001f), $"alpha times bloom multiplier must give the quadratic payload at brightness {brightness}");
            Assert.That(bloom, Is.EqualTo(Mathf.Max(0f, brightness)).Within(0.000001f));
        }
    }

    [Test]
    public void GlowTintWhitenOnlyAboveBrightnessOneInsideTwelveCm()
    {
        Type settingsType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettings");
        MethodInfo? evaluateTint = settingsType.GetMethod("EvaluateGlowTint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo? evaluateGlow = settingsType.GetMethod("EvaluateGlowColor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo? evaluateSpread = settingsType.GetMethod("EvaluateGlowSpread", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(evaluateTint, Is.Not.Null);
        Assert.That(evaluateGlow, Is.Not.Null);
        Assert.That(evaluateSpread, Is.Not.Null);

        foreach (float brightness in new[] { 0.43f, 0.68f, 0.75f, 1f })
        {
            foreach (float channelPeak in new[] { 1f, 10f })
            {
                foreach (float bloomBrightness in new[] { 0f, 5f, 10f })
                {
                    object settings = NewSettings(settingsType, brightness, 0f, 0f, channelPeak, 2f, 0f, 0f, 0f, 1f, 1f, bloomBrightness);
                    foreach (float distance in new[] { 0f, 0.03f, 0.06f, 0.12f, 0.5f })
                    {
                        Color tint = (Color)evaluateTint!.Invoke(settings, [distance, 0.12f])!;
                        Assert.That(tint.r, Is.EqualTo(0f).Within(0.0001f), $"brightness {brightness} peak {channelPeak} bloom {bloomBrightness} at {distance}m must stay raw blue");
                        Assert.That(tint.b, Is.EqualTo(channelPeak).Within(0.001f));
                    }
                }
            }
        }

        object blue = NewSettings(settingsType, 10f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f);
        Color inner = (Color)evaluateTint.Invoke(blue, [0f, 0.12f])!;
        Assert.That(inner.r, Is.EqualTo(1f).Within(0.001f));
        Assert.That(inner.b, Is.EqualTo(1f).Within(0.001f), "at brightness ten the strip edge is neutral white");

        Color mid = (Color)evaluateTint.Invoke(blue, [0.06f, 0.12f])!;
        Assert.That(mid.r, Is.EqualTo(0.5f).Within(0.001f), "halfway through the white band blends white to blue");
        Assert.That(mid.b, Is.EqualTo(1f).Within(0.001f));

        Color border = (Color)evaluateTint.Invoke(blue, [0.12f, 0.12f])!;
        Assert.That(border.r, Is.EqualTo(0f).Within(0.0001f), "at the white band edge the spill is raw color");

        object halfStrength = NewSettings(settingsType, 1.5f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f);
        Color halfTint = (Color)evaluateTint.Invoke(halfStrength, [0f, 0.12f])!;
        Assert.That(halfTint.r, Is.EqualTo(0.5f).Within(0.001f), "brightness 1.5 blends halfway to the white peak");
        Color barelyAbove = (Color)evaluateTint.Invoke(NewSettings(settingsType, 1.01f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f), [0f, 0.12f])!;
        Assert.That(barelyAbove.r, Is.GreaterThan(0f).And.LessThan(0.05f), "just above one the whiteness is still faint");

        object hdr = NewSettings(settingsType, 10f, 10f, 0f, 0f, 2f, 0f, 0f, 0f, 1f);
        Color hdrTint = (Color)evaluateTint.Invoke(hdr, [0f, 0.12f])!;
        Assert.That(hdrTint.r, Is.EqualTo(10f).Within(0.001f), "the HDR peak is preserved as the white level");

        object black = NewSettings(settingsType, 10f, 0f, 0f, 0f, 2f, 0f, 0f, 0f, 1f);
        Color blackTint = (Color)evaluateTint.Invoke(black, [0f, 0.12f])!;
        Assert.That(blackTint.r, Is.EqualTo(0f).Within(0.0001f), "zero channels cannot whiten");

        Assert.That(((Color)evaluateGlow!.Invoke(blue, null)!).a, Is.EqualTo(25f).Within(0.001f), "glow alpha is a quarter of brightness squared");
        Assert.That(((Color)evaluateGlow.Invoke(NewSettings(settingsType, 1f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f), null)!).a, Is.EqualTo(0.25f).Within(0.0001f));
        Assert.That(((Color)evaluateGlow.Invoke(blue, null)!).r, Is.EqualTo(1f).Within(0.0001f), "glow material stays white; tint lives in vertex colors");

        Assert.That((float)evaluateSpread!.Invoke(NewSettings(settingsType, 0f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f), null)!, Is.EqualTo(1f).Within(0.000001f));
        Assert.That((float)evaluateSpread.Invoke(NewSettings(settingsType, 0.5f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f), null)!, Is.EqualTo(1f).Within(0.000001f));
        Assert.That((float)evaluateSpread.Invoke(NewSettings(settingsType, 1f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f), null)!, Is.EqualTo(1f).Within(0.000001f));
        Assert.That((float)evaluateSpread.Invoke(NewSettings(settingsType, 4f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f), null)!, Is.EqualTo(2f).Within(0.000001f));
        Assert.That((float)evaluateSpread.Invoke(NewSettings(settingsType, 10f, 0f, 0f, 1f, 2f, 0f, 0f, 0f, 1f), null)!, Is.EqualTo(Mathf.Sqrt(10f)).Within(0.00001f));
        Assert.That((float)evaluateSpread.Invoke(NewSettings(settingsType, 0.43f, 0f, 0f, 10f, 2f, 0f, 0f, 0f, 1f), null)!, Is.EqualTo(Mathf.Sqrt(10f)).Within(0.00001f), "HDR channels must widen the spill even at low brightness");
        Assert.That((float)evaluateSpread.Invoke(NewSettings(settingsType, 10f, 10f, 0f, 0f, 2f, 0f, 0f, 0f, 1f), null)!, Is.EqualTo(10f).Within(0.00001f), "brightness and channel peak multiply the spill");
    }

    [Test]
    public void EvaluateGlowColorFeedsQuarterBrightnessPremultiply()
    {
        Type settingsType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettings");
        MethodInfo? evaluateGlow = settingsType.GetMethod("EvaluateGlowColor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(evaluateGlow, Is.Not.Null, "the settings snapshot needs the glow color seam");

        Color glow = (Color)evaluateGlow!.Invoke(NewSettings(settingsType, 10f, 10f, 2f, 0f, 2f, 0f, 0f, 0f, 1f), null)!;
        Assert.That(glow.r, Is.EqualTo(1f).Within(0.0001f), "the glow material stays white; the tint lives in vertex colors");
        Assert.That(glow.g, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(glow.b, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(glow.a, Is.EqualTo(25f).Within(0.001f), "the glow alpha carries a quarter of brightness squared");
    }

    [Test]
    public void ServiceEnableStateTracksWithoutTouchingUnrelatedConfig()
    {
        object config = Activator.CreateInstance(GetPluginType("ChromaGLS.Settings.Config"), nonPublic: true)!;
        object service = CreateService(config);
        FieldInfo enabled = service.GetType().GetField("_enabled", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.That(enabled, Is.Not.Null);
        MethodInfo setEnabled = GetInstanceMethod(service, "SetEnabled");

        setEnabled.Invoke(service, [false]);
        Assert.That(enabled.GetValue(service), Is.EqualTo(false));
        setEnabled.Invoke(service, [true]);
        Assert.That(enabled.GetValue(service), Is.EqualTo(true));
        setEnabled.Invoke(service, [false]);

        Assert.That(GetConfigProperty(config, "Enabled"), Is.EqualTo(true));
        Assert.That(GetConfigProperty(config, "FerrofluidBorderEnabled"), Is.EqualTo(true));
        Assert.That(GetConfigProperty(config, "FerrofluidCubeEnabled"), Is.EqualTo(false));
        Assert.That(GetConfigProperty(config, "FerrofluidSphereEnabled"), Is.EqualTo(false));
        Assert.That(GetConfigProperty(config, "ParabolicFloorLightEnabled"), Is.EqualTo(true), "the service only tracks state; the toggle owns the saved preference");
    }

    [Test]
    public void ControllerSetEnabledSavesPreferenceAndTracksService()
    {
        object config = Activator.CreateInstance(GetPluginType("ChromaGLS.Settings.Config"), nonPublic: true)!;
        object service = CreateService(config);
        object controller = CreateController(config, service);
        FieldInfo enabled = service.GetType().GetField("_enabled", BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo setEnabled = GetInstanceMethod(controller, "SetEnabled");

        setEnabled.Invoke(controller, [false]);
        Assert.That(GetConfigProperty(config, "ParabolicFloorLightEnabled"), Is.EqualTo(false), "the toggle must persist immediately");
        Assert.That(enabled.GetValue(service), Is.EqualTo(false), "the toggle must reach the service");
        setEnabled.Invoke(controller, [true]);
        Assert.That(GetConfigProperty(config, "ParabolicFloorLightEnabled"), Is.EqualTo(true));
        Assert.That(enabled.GetValue(service), Is.EqualTo(true));
        Invoke(controller, "Dispose");
    }

    [Test]
    public void SliderSettersRoundTripThroughSessionAndRefreshGuardPreservesDraft()
    {
        object config = Activator.CreateInstance(GetPluginType("ChromaGLS.Settings.Config"), nonPublic: true)!;
        object service = CreateService(config);
        object controller = CreateController(config, service);
        FieldInfo sessionField = controller.GetType().GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object session = sessionField.GetValue(controller)!;
        Invoke(session, "OpenExisting", "legacy-parabola");

        PropertyInfo brightness = controller.GetType().GetProperty("ParabolaBrightness", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.That((float)brightness.GetValue(controller)!, Is.EqualTo(0.215f).Within(0.001f), "the saved .43 brightness must display as slider .215");
        brightness.SetValue(controller, 0.75f);
        Assert.That(SettingsField(ReadSettings(session, "Working"), "Brightness"), Is.EqualTo(3.25f).Within(0.001f), "slider 0.75 must preview intensity 3.25");

        PropertyInfo width = controller.GetType().GetProperty("ParabolaWidth", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.That((float)width.GetValue(controller)!, Is.EqualTo(Mathf.Sqrt((3.2f - 0.5f) / 7.5f)).Within(0.001f), "saved width 3.2 must display at its slider position");
        width.SetValue(controller, 0.5f);
        Assert.That(SettingsField(ReadSettings(session, "Working"), "WidthScale"), Is.EqualTo(2.375f).Within(0.001f));

        PropertyInfo lightWidth = controller.GetType().GetProperty("ParabolaLightWidth", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.That(lightWidth, Is.Not.Null, "the controller must bind the light-width slider");
        Assert.That((float)lightWidth.GetValue(controller)!, Is.EqualTo(Mathf.Sqrt(0.999f / 3.999f)).Within(0.001f), "saved light width 1 must display at its slider position");
        lightWidth.SetValue(controller, 0.5f);
        Assert.That(SettingsField(ReadSettings(session, "Working"), "LightWidthScale"), Is.EqualTo(1.00075f).Within(0.001f));
        brightness.SetValue(controller, 0.6f);
        Assert.That(SettingsField(ReadSettings(session, "Working"), "LightWidthScale"), Is.EqualTo(1.00075f).Within(0.001f), "brightness edits must not disturb the light width draft");

        PropertyInfo bloomWidth = controller.GetType().GetProperty("ParabolaBloomWidth", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.That(bloomWidth, Is.Not.Null, "the controller must bind the bloom-width slider");
        Assert.That((float)bloomWidth.GetValue(controller)!, Is.EqualTo(Mathf.Sqrt(0.6f / 4f)).Within(0.001f), "saved bloom width .6 must display at its slider position");
        bloomWidth.SetValue(controller, 0f);
        Assert.That(SettingsField(ReadSettings(session, "Working"), "BloomWidthScale"), Is.EqualTo(0f).Within(0.001f));
        bloomWidth.SetValue(controller, 0.5f);
        Assert.That(SettingsField(ReadSettings(session, "Working"), "BloomWidthScale"), Is.EqualTo(1f).Within(0.001f));
        bloomWidth.SetValue(controller, 1f);
        Assert.That(SettingsField(ReadSettings(session, "Working"), "BloomWidthScale"), Is.EqualTo(4f).Within(0.001f));

        PropertyInfo bloomBrightness = controller.GetType().GetProperty("ParabolaBloomBrightness", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.That(bloomBrightness, Is.Not.Null, "the controller must bind the bloom-brightness slider");
        Assert.That((float)bloomBrightness.GetValue(controller)!, Is.EqualTo(0.5f + (0.5f * Mathf.Sqrt(0.42f / 9f))).Within(0.001f), "saved bloom brightness 1.42 must display at its slider position");
        bloomBrightness.SetValue(controller, 0f);
        Assert.That(SettingsField(ReadSettings(session, "Working"), "BloomBrightness"), Is.EqualTo(0f).Within(0.001f));
        bloomBrightness.SetValue(controller, 0.5f);
        Assert.That(SettingsField(ReadSettings(session, "Working"), "BloomBrightness"), Is.EqualTo(1f).Within(0.001f));
        bloomBrightness.SetValue(controller, 0.75f);
        Assert.That(SettingsField(ReadSettings(session, "Working"), "BloomBrightness"), Is.EqualTo(3.25f).Within(0.001f));
        Assert.That(SettingsField(ReadSettings(session, "Working"), "BloomWidthScale"), Is.EqualTo(4f).Within(0.001f), "bloom brightness edits must not disturb the bloom width draft");
        Assert.That(SettingsField(ReadSettings(session, "Working"), "Brightness"), Is.EqualTo(1.36f).Within(0.001f), "bloom edits must not disturb the core brightness draft");

        FieldInfo loading = controller.GetType().GetField("_loading", BindingFlags.Instance | BindingFlags.NonPublic)!;
        PropertyInfo hdrThreshold = controller.GetType().GetProperty("ParabolaHdrGlowThreshold", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.That(hdrThreshold, Is.Not.Null, "the controller must bind the inner-threshold slider");
        Assert.That((float)hdrThreshold.GetValue(controller)!, Is.EqualTo(0.5f + (0.5f * Mathf.Sqrt(0.8f / 9f))).Within(0.001f), "saved threshold 1.8 must display at its slider position");
        hdrThreshold.SetValue(controller, 0.5f);
        Assert.That(SettingsField(ReadSettings(session, "Working"), "HdrGlowThreshold"), Is.EqualTo(1f).Within(0.001f));
        hdrThreshold.SetValue(controller, 0.75f);
        Assert.That(SettingsField(ReadSettings(session, "Working"), "HdrGlowThreshold"), Is.EqualTo(3.25f).Within(0.001f));

        PropertyInfo hdrWidth = controller.GetType().GetProperty("ParabolaHdrGlowWidth", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.That(hdrWidth, Is.Not.Null, "the controller must bind the inner-width slider");
        Assert.That((float)hdrWidth.GetValue(controller)!, Is.EqualTo(Mathf.Sqrt(0.2f)).Within(0.001f), "saved inner width .2 must display at its slider position");
        hdrWidth.SetValue(controller, 0.5f);
        Assert.That(SettingsField(ReadSettings(session, "Working"), "HdrGlowWidthScale"), Is.EqualTo(0.25f).Within(0.001f));
        hdrWidth.SetValue(controller, 1f);
        Assert.That(SettingsField(ReadSettings(session, "Working"), "HdrGlowWidthScale"), Is.EqualTo(1f).Within(0.001f));

        loading.SetValue(controller, true);
        brightness.SetValue(controller, 0.9f);
        bloomWidth.SetValue(controller, 0.2f);
        bloomBrightness.SetValue(controller, 0.2f);
        hdrThreshold.SetValue(controller, 0.2f);
        hdrWidth.SetValue(controller, 0.2f);
        Assert.That(SettingsField(ReadSettings(session, "Working"), "Brightness"), Is.EqualTo(1.36f).Within(0.001f), "the refresh round-trip must not disturb the draft");
        Assert.That(SettingsField(ReadSettings(session, "Working"), "BloomWidthScale"), Is.EqualTo(4f).Within(0.001f), "the loading guard must block bloom width writes");
        Assert.That(SettingsField(ReadSettings(session, "Working"), "BloomBrightness"), Is.EqualTo(3.25f).Within(0.001f), "the loading guard must block bloom brightness writes");
        Assert.That(SettingsField(ReadSettings(session, "Working"), "HdrGlowThreshold"), Is.EqualTo(3.25f).Within(0.001f), "the loading guard must block inner-threshold writes");
        Assert.That(SettingsField(ReadSettings(session, "Working"), "HdrGlowWidthScale"), Is.EqualTo(1f).Within(0.001f), "the loading guard must block inner-width writes");
        loading.SetValue(controller, false);

        Assert.That(InvokeFormat(controller, "FormatIntensity", 1f), Is.EqualTo("10.00"));
        Assert.That(InvokeFormat(controller, "FormatWidth", 1f), Is.EqualTo("8.00"));
        Assert.That(InvokeFormat(controller, "FormatLightWidth", 1f), Is.EqualTo("4.00"));
        Assert.That(InvokeFormat(controller, "FormatLightWidth", 0f), Is.EqualTo("0.001"), "the formatter must not round the .001 minimum away");
        Assert.That(InvokeFormat(controller, "FormatBloomWidth", 1f), Is.EqualTo("4.00x"));
        Assert.That(InvokeFormat(controller, "FormatHdrGlowWidth", 0.5f), Is.EqualTo("25%"), "the inner-width formatter must show the mapped percentage");
        Assert.That(InvokeFormat(controller, "FormatOffset", -1.25f), Is.EqualTo("-1.25"));
        Assert.That(InvokeFormat(controller, "FormatRotation", 90f), Is.EqualTo("90"));
        Invoke(controller, "Dispose");
    }

    private static readonly string[] SettingProperties =
    {
        "ParabolicFloorLightBrightness", "ParabolicFloorLightRed", "ParabolicFloorLightGreen", "ParabolicFloorLightBlue",
        "ParabolicFloorLightWidthScale", "ParabolicFloorLightOffsetX", "ParabolicFloorLightOffsetZ", "ParabolicFloorLightRotation",
        "ParabolicFloorLightLightWidthScale", "ParabolicFloorLightBloomWidthScale", "ParabolicFloorLightBloomBrightness",
        "ParabolicFloorLightHdrGlowThreshold", "ParabolicFloorLightHdrGlowWidthScale", "ParabolicFloorLightHdrGlowBrightness",
    };

    private static readonly Dictionary<string, object> DefaultConfigValues = new()
    {
        ["ParabolicFloorLightBrightness"] = 0.43f,
        ["ParabolicFloorLightRed"] = 0f,
        ["ParabolicFloorLightGreen"] = 1f,
        ["ParabolicFloorLightBlue"] = 2.5f,
        ["ParabolicFloorLightWidthScale"] = 3.2f,
        ["ParabolicFloorLightOffsetX"] = 0f,
        ["ParabolicFloorLightOffsetZ"] = 0f,
        ["ParabolicFloorLightRotation"] = 0f,
        ["ParabolicFloorLightLightWidthScale"] = 1f,
        ["ParabolicFloorLightBloomWidthScale"] = 0.6f,
        ["ParabolicFloorLightBloomBrightness"] = 1.42f,
        ["ParabolicFloorLightHdrGlowThreshold"] = 1.8f,
        ["ParabolicFloorLightHdrGlowWidthScale"] = 0.20f,
        ["ParabolicFloorLightHdrGlowBrightness"] = 2f,
    };

    private static readonly List<object> _recordedSettings = new();

    private static void RecordPreview<T>(T value) => _recordedSettings.Add(value!);

    private static object CreateService(object config)
    {
        Type serviceType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLight");
        ConstructorInfo? ctor = serviceType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [GetPluginType("ChromaGLS.Settings.Config")],
            null);
        Assert.That(ctor, Is.Not.Null, "the floor service needs a config-injected constructor");
        return ctor!.Invoke([config]);
    }

    private static object CreateController(object config, object service)
    {
        Type uiType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettingsUI");
        ConstructorInfo? ctor = uiType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [GetPluginType("ChromaGLS.Settings.Config"), GetPluginType("ChromaGLS.Settings.ParabolicFloorLight")],
            null);
        Assert.That(ctor, Is.Not.Null, "the tweak controller needs a config + service constructor");
        return ctor!.Invoke([config, service]);
    }

    private static MethodInfo GetInstanceMethod(object target, string name)
    {
        MethodInfo? method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"missing method {name} on {target.GetType().Name}");
        return method!;
    }

    private static float SettingsField(object settings, string field)
    {
        return (float)settings.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(settings)!;
    }

    private static object? InvokeFormat(object controller, string method, float value)
    {
        return GetInstanceMethod(controller, method).Invoke(controller, [value]);
    }

    private static object OpenSession(out Type settingsType, out object config)
    {
        config = Activator.CreateInstance(GetPluginType("ChromaGLS.Settings.Config"), nonPublic: true)!;
        return OpenSessionOn(config, out settingsType);
    }

    private static object OpenSessionOn(object config, out Type settingsType)
    {
        _recordedSettings.Clear();
        settingsType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightSettings");
        Type actionType = typeof(Action<>).MakeGenericType(settingsType);
        MethodInfo recorder = typeof(ParabolicFloorLightSettingsTests)
            .GetMethod(nameof(RecordPreview), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(settingsType);
        Delegate preview = Delegate.CreateDelegate(actionType, recorder);
        Type sessionType = GetPluginType("ChromaGLS.Settings.ParabolicFloorLightEditSession");
        ConstructorInfo? ctor = sessionType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [GetPluginType("ChromaGLS.Settings.Config"), actionType],
            null);
        Assert.That(ctor, Is.Not.Null, "the edit session needs a config + preview constructor");
        object session = ctor!.Invoke([config, preview]);
        Invoke(session, "Open");
        return session;
    }

    private static void AssertConfigDefault(object config, string name, object expected, Type ignoreType)
    {
        PropertyInfo? property = config.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(property, Is.Not.Null, $"missing config property {name}");
        Assert.That(property!.GetValue(config), Is.EqualTo(expected), $"{name} default must match the shipped appearance");
        Assert.That(property.IsDefined(ignoreType, true), Is.False, $"{name} must persist");
    }

    private static void AssertConfigValue(object config, string name, float expected)
    {
        Assert.That(GetConfigProperty(config, name), Is.EqualTo(expected).Within(0.00001f), name);
    }

    private static object GetConfigProperty(object config, string name)
    {
        return config.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!.GetValue(config)!;
    }

    private static object ReadSettings(object session, string property)
    {
        object working = session.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(session)!;
        return DefinitionSettings(working);
    }

    private static object ReadPersistedDefinition(object config)
    {
        Type storeType = GetPluginType("ChromaGLS.Settings.MenuLightStore");
        object store = Activator.CreateInstance(
            storeType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [config],
            null)!;
        var definitions = (System.Collections.IList)storeType.GetProperty("Lights", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        foreach (object? definition in definitions)
        {
            if ((string)definition!.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(definition)! == "legacy-parabola")
                return definition;
        }

        Assert.Fail("the saved legacy parabola must remain in the collection");
        return null!;
    }

    private static object DefinitionSettings(object working)
    {
        PropertyInfo? appearance = working.GetType().GetProperty("Appearance");
        if (appearance == null)
            return working;

        object settings = appearance.GetValue(working)!;
        return NewSettings(
            settings.GetType(),
            SettingsField(settings, "Brightness"),
            SettingsField(settings, "Red"),
            SettingsField(settings, "Green"),
            SettingsField(settings, "Blue"),
            SettingsField(settings, "WidthScale"),
            (float)working.GetType().GetProperty("PositionX")!.GetValue(working)!,
            (float)working.GetType().GetProperty("PositionZ")!.GetValue(working)!,
            (float)working.GetType().GetProperty("RotationY")!.GetValue(working)!,
            SettingsField(settings, "LightWidthScale"),
            SettingsField(settings, "BloomWidthScale"),
            SettingsField(settings, "BloomBrightness"),
            SettingsField(settings, "HdrGlowThreshold"),
            SettingsField(settings, "HdrGlowWidthScale"),
            SettingsField(settings, "HdrGlowBrightness"));
    }

    private static void Invoke(object target, string method, params object[] args)
    {
        MethodInfo? info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(info, Is.Not.Null, $"missing session method {method}");
        info!.Invoke(target, args);
    }

    private static object NewSettings(Type settingsType, float brightness, float red, float green, float blue, float widthScale, float offsetX, float offsetZ, float rotation, float lightWidthScale = 1f, float bloomWidthScale = 1f, float bloomBrightness = 1f, float hdrGlowThreshold = 1.3f, float hdrGlowWidthScale = 0.04f, float hdrGlowBrightness = 4f)
    {
        ConstructorInfo? ctor = settingsType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Enumerable.Repeat(typeof(float), 14).ToArray(),
            null);
        Assert.That(ctor, Is.Not.Null, "the settings snapshot needs a fourteen-float constructor");
        return ctor!.Invoke([brightness, red, green, blue, widthScale, offsetX, offsetZ, rotation, lightWidthScale, bloomWidthScale, bloomBrightness, hdrGlowThreshold, hdrGlowWidthScale, hdrGlowBrightness]);
    }

    private static void AssertSettings(object settings, float brightness, float red, float green, float blue, float widthScale, float offsetX, float offsetZ, float rotation, float lightWidthScale = 1f, float bloomWidthScale = 1f, float bloomBrightness = 1f, float hdrGlowThreshold = 1.3f, float hdrGlowWidthScale = 0.04f, float hdrGlowBrightness = 4f, string? message = null)
    {
        (string Field, float Expected)[] expected =
        [
            ("Brightness", brightness), ("Red", red), ("Green", green), ("Blue", blue),
            ("WidthScale", widthScale), ("OffsetX", offsetX), ("OffsetZ", offsetZ), ("Rotation", rotation),
            ("LightWidthScale", lightWidthScale), ("BloomWidthScale", bloomWidthScale), ("BloomBrightness", bloomBrightness),
            ("HdrGlowThreshold", hdrGlowThreshold), ("HdrGlowWidthScale", hdrGlowWidthScale), ("HdrGlowBrightness", hdrGlowBrightness),
        ];
        foreach ((string field, float value) in expected)
        {
            Assert.That(
                (float)settings.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(settings)!,
                Is.EqualTo(value).Within(0.00001f),
                message ?? field);
        }
    }

    private static object DefaultEnclosure()
    {
        Type enclosureType = GetPluginType("ChromaGLS.Settings.FerrofluidGrabEnclosure");
        return enclosureType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float)],
            null)!.Invoke([new Vector3(0f, 0f, -0.5f), 2.5f, new Vector3(0f, 0f, -2f), Vector3.forward, 0f]);
    }

    private static (Vector3[] Vertices, int[] Triangles) CreateLight(object enclosure, Vector3 playerPosition, Vector3 forward)
    {
        MethodInfo tryCreate = GetGeometryMethod("TryCreate");
        object[] arguments = [enclosure, playerPosition, forward, null!];
        Assert.That(tryCreate.Invoke(null, arguments), Is.EqualTo(true));
        return ReadMesh(arguments[3]);
    }

    private static (Vector3[] Vertices, int[] Triangles) CreateConfiguredLight(object enclosure, Vector3 playerPosition, Vector3 forward, object settings)
    {
        MethodInfo tryCreate = GetGeometryMethod("TryCreateConfigured");
        object[] arguments = [enclosure, playerPosition, forward, settings, null!];
        Assert.That(tryCreate.Invoke(null, arguments), Is.EqualTo(true));
        return ReadMesh(arguments[4]);
    }

    private static (Vector3[] Vertices, int[] Triangles) ReadMesh(object? mesh)
    {
        Assert.That(mesh, Is.Not.Null);
        Type meshType = mesh!.GetType();
        Vector3[] vertices = (Vector3[])meshType.GetField("Vertices", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(mesh)!;
        int[] triangles = (int[])meshType.GetField("Triangles", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(mesh)!;
        return (vertices, triangles);
    }

    private static MethodInfo GetGeometryMethod(string name)
    {
        Type? geometryType = Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings.ParabolicFloorLightGeometry", false);
        Assert.That(geometryType, Is.Not.Null, "the floor light needs a managed geometry seam");
        MethodInfo? method = geometryType!.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"missing geometry method {name}");
        return method!;
    }

    private static MethodInfo GetMappingMethod(string name)
    {
        Type? mappingType = Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings.ParabolicFloorLightSliderMapping", false);
        Assert.That(mappingType, Is.Not.Null, "the tweak sliders need a pure mapping helper");
        MethodInfo? method = mappingType!.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"missing slider mapping {name}");
        return method!;
    }

    private static Vector3 RingCenter(Vector3[] vertices, int station)
    {
        Vector3 sum = Vector3.zero;
        for (int corner = 0; corner < 4; corner++)
            sum += vertices[(station * 4) + corner];

        return sum / 4;
    }

    private static XElement LoadResourceXml(string resourceName)
    {
        using Stream? stream = Assembly.Load("ChromaGLS").GetManifestResourceStream(resourceName);
        Assert.That(stream, Is.Not.Null, $"missing embedded resource {resourceName}");
        using StreamReader reader = new(stream!);
        return XElement.Parse(reader.ReadToEnd());
    }

    private static MemberInfo? FindUIValue(Type host, string name)
    {
        return host.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(member => member.GetCustomAttributes(false).Any(attribute => attribute.GetType().Name == "UIValue" && AttributeValue(attribute) == name));
    }

    private static MemberInfo? FindUIAction(Type host, string name)
    {
        return host.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(member => member.GetCustomAttributes(false).Any(attribute => attribute.GetType().Name == "UIAction" && AttributeValue(attribute) == name));
    }

    private static string? AttributeValue(object attribute)
    {
        return (string?)attribute.GetType().GetProperty("Id")?.GetValue(attribute)
            ?? (string?)attribute.GetType().GetProperty("Name")?.GetValue(attribute)
            ?? (string?)attribute.GetType().GetField("Id")?.GetValue(attribute)
            ?? (string?)attribute.GetType().GetField("Name")?.GetValue(attribute);
    }

    private static Type GetPluginType(string typeName)
    {
        return Assembly.Load("ChromaGLS").GetType(typeName, throwOnError: false)
            ?? throw new AssertionException($"Missing plugin type: {typeName}");
    }
}
