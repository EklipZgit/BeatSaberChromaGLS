using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
public class MenuLightFloorTuningTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [Test]
    public void FreshFloorUsesRequestedDefaultsButMovableDefaultsStayUnchanged()
    {
        object config = New("Config");
        object store = New("MenuLightStore", config);
        object floor = Lights(store)[0]!;
        AssertFloor(floor);
        Assert.That(Call(store, "IsDefaultFloor", floor), Is.True);
        object movable = Call(store, "CreateDefault", Kind("Parabola"));
        Assert.That(Field(Get(movable, "Appearance"), "Brightness"), Is.EqualTo(.4f));
        Assert.That(Field(Get(movable, "Appearance"), "Red"), Is.EqualTo(2.4f));
    }

    [TestCase("ParabolaRed", "Red")]
    [TestCase("ParabolaGreen", "Green")]
    [TestCase("ParabolaBlue", "Blue")]
    public void ColorSliderReachesTwentyAndRoundTripsSavedColor(string property, string channel)
    {
        object config = Config();
        object light = New("ParabolicFloorLight", config);
        object ui = New("ParabolicFloorLightSettingsUI", config, light);
        Call(ui, "BeginEditor", (object?)null, (Action)(() => { }));
        Set(ui, property, 1f);
        object session = Field(ui, "_session");
        Assert.That(Field(Get(Get(session, "Working"), "Appearance"), channel), Is.EqualTo(20f));
        Set(ui, "TypeValue", Kind("Floor"));
        Call(ui, "HandleReset");
        if (channel == "Blue")
        {
            float slider = (float)Get(ui, property);
            Set(ui, property, slider);
            Assert.That(Field(Get(Get(session, "Working"), "Appearance"), channel), Is.EqualTo(2.5f).Within(1e-5f));
        }
        Call(ui, "HandleCancel");
        Assert.That(Lights(Get(light, "Store")), Is.Empty);
    }

    [Test]
    public void FloorHeightPreviewSaveAndCancelKeepDepthFollowingHeight()
    {
        object config = Config();
        object light = New("ParabolicFloorLight", config);
        object store = Get(light, "Store");
        Call(store, "RestoreDefaultFloor", (object?)null);
        object original = Lights(store)[0]!;
        string id = (string)Get(original, "Id");
        object ui = New("ParabolicFloorLightSettingsUI", config, light);
        Call(ui, "BeginEditor", id, (Action)(() => { }));
        Set(ui, "FloorHeightValue", .125f);
        object working = Get(Field(ui, "_session"), "Working");
        Assert.That(Get(working, "Height"), Is.EqualTo(.125f));
        Assert.That(Get(working, "Depth"), Is.EqualTo(.125f));
        Assert.That(Get(original, "Height"), Is.EqualTo(.02f));
        Call(ui, "HandleCancel");
        Assert.That(Get(Lights(store)[0]!, "Height"), Is.EqualTo(.02f));
        Call(ui, "BeginEditor", id, (Action)(() => { }));
        Set(ui, "FloorHeightValue", .25f);
        Call(ui, "HandleSave");
        object reloaded = Lights(New("MenuLightStore", config))[0]!;
        Assert.That(Get(reloaded, "Height"), Is.EqualTo(.25f));
        Assert.That(Get(reloaded, "Depth"), Is.EqualTo(.25f));
    }

    [Test]
    public void ListDefaultRestoresDeletedFloorOnlyOnceAndUpdatesAvailability()
    {
        object config = Config();
        object light = New("ParabolicFloorLight", config);
        object store = Get(light, "Store");
        object list = New("MenuLightListView", config, light, store, (Action<string?>)(_ => { }));
        Assert.That(Get(list, "NeedsDefaultFloor"), Is.True);
        Call(list, "RestoreDefault");
        Assert.That(Get(list, "NeedsDefaultFloor"), Is.False);
        Assert.That(Lights(store).Count, Is.EqualTo(1));
        AssertFloor(Lights(store)[0]!);
        Call(list, "RestoreDefault");
        Assert.That(Lights(store).Count, Is.EqualTo(1));
        Call(store, "Remove", Get(Lights(store)[0]!, "Id"));
        Call(list, "Rebuild");
        Assert.That(Get(list, "NeedsDefaultFloor"), Is.True);
        Call(list, "RestoreDefault");
        Assert.That(Lights(New("MenuLightStore", config)).Count, Is.EqualTo(1));
    }

    [Test]
    public void DefaultDoesNotReplaceOtherLightsAtCapacityOrResetNonFloorIds()
    {
        object store = New("MenuLightStore", Config());
        for (int index = 0; index < 8; index++)
            Call(store, "Upsert", Call(store, "CreateDefault", Kind("Cube")));
        string id = (string)Get(Lights(store)[0]!, "Id");
        Assert.That(Call(store, "RestoreDefaultFloor", (object?)null), Is.False);
        Assert.That(Call(store, "RestoreDefaultFloor", id), Is.False);
        Assert.That(Call(store, "RestoreDefaultFloor", "removed"), Is.False);
        Assert.That(Lights(store).Count, Is.EqualTo(8));
        Assert.That(Get(Lights(store)[0]!, "Id"), Is.EqualTo(id));
    }

    [Test]
    public void RowDefaultResetsItsFloorInPlaceAndLeavesOtherFloorsAlone()
    {
        object config = Config();
        object light = New("ParabolicFloorLight", config);
        object store = Get(light, "Store");
        object first = Call(store, "CreateDefault", Kind("Floor"));
        object second = Call(store, "CreateDefault", Kind("Floor"));
        Set(first, "Enabled", false);
        Set(first, "Height", .3f);
        Set(second, "PositionX", 2f);
        Call(store, "Upsert", first);
        Call(store, "Upsert", second);
        int rebuilds = 0;
        object row = New("MenuLightRow", first, 0, (Action<string?>)(_ => { }), store, light, (Action)(() => rebuilds++));
        Assert.That(Get(row, "ShowDefault"), Is.True);
        Call(row, "RestoreDefault");
        Assert.That(rebuilds, Is.EqualTo(1));
        Assert.That(Get(Lights(store)[0]!, "Id"), Is.EqualTo(Get(first, "Id")));
        AssertFloor(Lights(store)[0]!);
        Assert.That(Get(Lights(store)[1]!, "PositionX"), Is.EqualTo(2f));
        object resetRow = New("MenuLightRow", Lights(store)[0]!, 0, (Action<string?>)(_ => { }), store, light, (Action)(() => rebuilds++));
        Assert.That(Get(resetRow, "ShowDefault"), Is.False);
        Set(resetRow, "Enabled", false);
        Assert.That(rebuilds, Is.EqualTo(2), "toggling a default floor must refresh its Default button");
        object disabledRow = New("MenuLightRow", Lights(store)[0]!, 0, (Action<string?>)(_ => { }), store, light, (Action)(() => { }));
        Assert.That(Get(disabledRow, "ShowDefault"), Is.True);
    }

    private static void AssertFloor(object floor)
    {
        Assert.That(Get(floor, "Type"), Is.EqualTo(Kind("Floor")));
        Assert.That(Get(floor, "Enabled"), Is.True);
        Assert.That(Get(floor, "Height"), Is.EqualTo(.02f));
        Assert.That(Get(floor, "Depth"), Is.EqualTo(.02f));
        Assert.That(Get(floor, "FadeStart"), Is.EqualTo(.13f));
        object appearance = Get(floor, "Appearance");
        string[] fields = { "Brightness", "Red", "Green", "Blue", "LightWidthScale", "BloomWidthScale", "BloomBrightness", "HdrGlowThreshold", "HdrGlowWidthScale", "HdrGlowBrightness" };
        float[] expected = { .43f, 0f, 1f, 2.5f, 1f, .6f, 1.42f, 1.8f, .2f, 2f };
        for (int index = 0; index < fields.Length; index++)
            Assert.That(Field(appearance, fields[index]), Is.EqualTo(expected[index]), fields[index]);
    }


    [TestCase(0f, 0f)]
    [TestCase(.1f, .007f)]
    [TestCase(.5f, .875f)]
    [TestCase(.75f, 2.953125f)]
    [TestCase(1f, 7f)]
    public void CubicHeightSliderReachesSevenMeters(float slider, float expected)
    {
        object config = Config();
        object light = New("ParabolicFloorLight", config);
        object ui = New("ParabolicFloorLightSettingsUI", config, light);
        Call(ui, "BeginEditor", (object?)null, (Action)(() => { }));
        object session = Field(ui, "_session");
        Set(ui, "HeightValue", slider);
        Assert.That((float)Get(Get(session, "Working"), "Height"), Is.EqualTo(expected).Within(1e-5f), $"slider {slider} must preview {expected}m");
        Assert.That((float)Get(ui, "HeightValue"), Is.EqualTo(slider).Within(1e-4f), "the displayed slider must round-trip");
        Call(ui, "HandleCancel");
    }

    [Test]
    public void FreshConfigSeedsFloorAndRaisedParabola()
    {
        object config = New("Config");
        object store = New("MenuLightStore", config);
        Assert.That(Lights(store).Count, Is.EqualTo(2), "a fresh config must seed the floor light and the raised parabola");
        object parabola = Lights(store)[1]!;
        Assert.That(Get(parabola, "Type"), Is.EqualTo(Kind("Parabola")));
        Assert.That((float)Get(parabola, "Height"), Is.EqualTo(4.3f).Within(1e-5f));
        Assert.That((float)Get(parabola, "Depth"), Is.EqualTo(.2f).Within(1e-5f));
        Assert.That((float)Get(parabola, "RotationX"), Is.EqualTo(20f).Within(1e-5f));
        Assert.That((float)Get(parabola, "FadeStart"), Is.EqualTo(.15f).Within(1e-5f));
        object appearance = Get(parabola, "Appearance");
        Assert.That((float)Field(appearance, "Brightness"), Is.EqualTo(.4f).Within(1e-5f));
        Assert.That((float)Field(appearance, "Red"), Is.EqualTo(2.4f).Within(1e-5f));
        Assert.That((float)Field(appearance, "Green"), Is.EqualTo(0f).Within(1e-5f));
        Assert.That((float)Field(appearance, "Blue"), Is.EqualTo(0f).Within(1e-5f));
        Assert.That((float)Field(appearance, "LightWidthScale"), Is.EqualTo(1.05f).Within(1e-5f));
    }

    [Test]
    public void ExistingListGetsRaisedParabolaAppendedOnce()
    {
        object seedConfig = New("Config");
        object square = Call(New("MenuLightStore", seedConfig), "CreateDefault", Kind("Square"));
        object config = New("Config");
        Set(config, "MenuLightsJson", Newtonsoft.Json.JsonConvert.SerializeObject(new[] { square }));
        object store = New("MenuLightStore", config);
        Assert.That(Lights(store).Count, Is.EqualTo(2), "an existing list must gain the raised parabola once");
        Assert.That((string)Get(Lights(store)[1]!, "Id"), Is.EqualTo("default-raised-parabola"));
        Assert.That((string)Get(Lights(store)[0]!, "Id"), Is.EqualTo((string)Get(square, "Id")), "the original entry keeps its id");
        Assert.That(Get(Lights(store)[0]!, "Type"), Is.EqualTo(Get(square, "Type")), "the original entry keeps its type");
        object reloaded = New("MenuLightStore", config);
        Assert.That(Lights(reloaded).Count, Is.EqualTo(2), "a reload must not duplicate the preset");
    }

    [Test]
    public void RaisedParabolaIsNotRespawnedOrAddedAtCap()
    {
        object config = New("Config");
        object store = New("MenuLightStore", config);
        Assert.That(Lights(store).Count, Is.EqualTo(2));
        Call(store, "Remove", "default-raised-parabola");
        Assert.That(Lights(New("MenuLightStore", config)).Count, Is.EqualTo(1), "a deleted preset must not respawn");

        object empty = New("Config");
        Set(empty, "MenuLightsJson", "[]");
        Assert.That(Lights(New("MenuLightStore", empty)).Count, Is.EqualTo(0), "deliberately saved empty must stay empty");

        object capConfig = New("Config");
        object capSeed = New("MenuLightStore", capConfig);
        object[] defs = new object[8];
        for (int index = 0; index < 8; index++)
            defs[index] = Call(capSeed, "CreateDefault", Kind("Cube"));
        Set(capConfig, "MenuLightsJson", Newtonsoft.Json.JsonConvert.SerializeObject(defs));
        Set(capConfig, "MenuLightDefaultsVersion", 0);
        object capStore = New("MenuLightStore", capConfig);
        Assert.That(Lights(capStore).Count, Is.EqualTo(8), "a full list must not grow past the cap");
        Assert.That((int)Get(capConfig, "MenuLightDefaultsVersion"), Is.EqualTo(3), "the marker still advances so no later load re-checks");
    }


    [TestCase(-.1f, 0f, 0f)]
    [TestCase(1.1f, 7f, 1f)]
    public void CubicHeightSliderClampsOutsideTheNormalizedRange(float slider, float expected, float displayed)
    {
        object config = Config();
        object light = New("ParabolicFloorLight", config);
        object ui = New("ParabolicFloorLightSettingsUI", config, light);
        Call(ui, "BeginEditor", (object?)null, (Action)(() => { }));
        object session = Field(ui, "_session");
        Set(ui, "HeightValue", slider);
        Assert.That((float)Get(Get(session, "Working"), "Height"), Is.EqualTo(expected).Within(1e-5f));
        Assert.That((float)Get(ui, "HeightValue"), Is.EqualTo(displayed).Within(1e-4f));
        Call(ui, "HandleCancel");
    }

    [Test]
    public void RaisedParabolaHeightRoundTripsThroughTheNormalizedSlider()
    {
        object config = Config();
        object light = New("ParabolicFloorLight", config);
        object ui = New("ParabolicFloorLightSettingsUI", config, light);
        Call(ui, "BeginEditor", (object?)null, (Action)(() => { }));
        Set(ui, "HeightValue", Mathf.Pow(4.3f / 7f, 1f / 3f));
        Assert.That((float)Get(Get(Field(ui, "_session"), "Working"), "Height"), Is.EqualTo(4.3f).Within(1e-4f));
        Call(ui, "HandleCancel");
    }

    private static Type TypeOf(string name) => Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings." + name, true)!;
    private static object New(string name, params object[] args) => Activator.CreateInstance(TypeOf(name), Members, null, args, null)!;
    private static object Kind(string name) => Enum.Parse(TypeOf("MenuLightType"), name);
    private static object Config() { object config = New("Config"); Set(config, "MenuLightsJson", "[]"); return config; }
    private static IList Lights(object store) => (IList)Get(store, "Lights");
    private static object Get(object target, string name) => target.GetType().GetProperty(name, Members)!.GetValue(target)!;
    private static object Field(object target, string name) => target.GetType().GetField(name, Members)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetProperty(name, Members)!.SetValue(target, value);
    private static object Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Members)!.Invoke(target, args)!;
}
