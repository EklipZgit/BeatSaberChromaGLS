using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;

namespace ChromaGLS.Tests;

[TestFixture]
public class MenuLightPresetTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [Test]
    public void EmptyListOffersBothDefaultsAndUpperRestoreKeepsAStableId()
    {
        object config = Config();
        object light = New("ParabolicFloorLight", config);
        object store = Get(light, "Store");
        object list = New("MenuLightListView", config, light, store, (Action<string?>)(_ => { }));
        Assert.That(Get(list, "NeedsDefaultFloor"), Is.EqualTo(true));
        Assert.That(Get(list, "NeedsDefaultRaisedParabola"), Is.EqualTo(true));

        Call(list, "RestoreDefaultRaised");
        IList lights = Lights(store);
        Assert.That(lights.Count, Is.EqualTo(1), "the upper restore must add only the named preset");
        object upper = lights[0]!;
        Assert.That(Get(upper, "Id"), Is.EqualTo("default-raised-parabola"));
        AssertUpperPreset(upper);
        Assert.That(Get(list, "NeedsDefaultRaisedParabola"), Is.EqualTo(false));
        Assert.That(Get(list, "NeedsDefaultFloor"), Is.EqualTo(true), "restoring the upper preset must not satisfy the floor button");
        Call(list, "RestoreDefaultRaised");
        Assert.That(lights.Count, Is.EqualTo(1), "the upper restore must not duplicate the preset");

        Call(store, "Remove", "default-raised-parabola");
        Call(list, "Rebuild");
        Assert.That(Get(list, "NeedsDefaultRaisedParabola"), Is.EqualTo(true));
        Call(list, "RestoreDefaultRaised");
        Assert.That(Get(Lights(store)[0]!, "Id"), Is.EqualTo("default-raised-parabola"), "recreated upper keeps the stable id");
        Assert.That(Lights(New("MenuLightStore", config)).Count, Is.EqualTo(1), "a reload must not resurrect other presets");
    }

    [Test]
    public void RestoringOneDefaultLeavesTheOtherButtonAvailable()
    {
        object config = Config();
        object light = New("ParabolicFloorLight", config);
        object store = Get(light, "Store");
        object list = New("MenuLightListView", config, light, store, (Action<string?>)(_ => { }));
        Call(list, "RestoreDefault");
        Assert.That(Get(list, "NeedsDefaultFloor"), Is.EqualTo(false));
        Assert.That(Get(list, "NeedsDefaultRaisedParabola"), Is.EqualTo(true));
        Assert.That(Get(Lights(store)[0]!, "Type"), Is.EqualTo(Kind("Floor")));
    }

    [Test]
    public void UpperRowResetRestoresPresetPoseAndColorsInPlace()
    {
        object config = Config();
        object light = New("ParabolicFloorLight", config);
        object store = Get(light, "Store");
        object upper = Call(store, "CreateDefault", Kind("Parabola"));
        Set(upper, "Id", "default-raised-parabola");
        Set(upper, "Height", 5f);
        Call(upper, "RotateAxis", 0, 25f);
        SetBrightness(upper, 0.7f);
        Set(upper, "Enabled", false);
        Call(store, "Upsert", upper);
        object other = Call(store, "CreateDefault", Kind("Parabola"));
        Call(store, "Upsert", other);

        int rebuilds = 0;
        object row = New("MenuLightRow", upper, 0, (Action<string?>)(_ => { }), store, light, (Action)(() => rebuilds++));
        Assert.That(Get(row, "ShowDefault"), Is.EqualTo(true), "a modified upper preset must offer Default");
        Assert.That((string)Get(row, "Label"), Does.Contain("Upper (Parabola)"));
        Call(row, "RestoreDefault");
        Assert.That(rebuilds, Is.EqualTo(1));

        IList lights = Lights(store);
        Assert.That(lights.Count, Is.EqualTo(2), "reset must not add or remove lights");
        object restored = lights[0]!;
        Assert.That(Get(restored, "Id"), Is.EqualTo("default-raised-parabola"));
        AssertUpperPreset(restored);
        Assert.That(Get(lights[1]!, "Id"), Is.EqualTo(Get(other, "Id")), "the neighbouring parabola must be untouched");

        object resetRow = New("MenuLightRow", restored, 0, (Action<string?>)(_ => { }), store, light, (Action)(() => rebuilds++));
        Assert.That(Get(resetRow, "ShowDefault"), Is.EqualTo(false));
        Set(resetRow, "Enabled", false);
        object disabledRow = New("MenuLightRow", Lights(store)[0]!, 0, (Action<string?>)(_ => { }), store, light, (Action)(() => { }));
        Assert.That(Get(disabledRow, "ShowDefault"), Is.EqualTo(true), "a disabled preset must offer the Default button");
    }

    [Test]
    public void NamedUpperKeepsPresetIdentityAcrossTypeChangesAndRejectsStrays()
    {
        object config = Config();
        object light = New("ParabolicFloorLight", config);
        object store = Get(light, "Store");
        object upper = Call(store, "CreateDefault", Kind("Parabola"));
        Set(upper, "Id", "default-raised-parabola");
        Set(upper, "Type", Kind("Cube"));
        Call(store, "Upsert", upper);

        object list = New("MenuLightListView", config, light, store, (Action<string?>)(_ => { }));
        Assert.That(Get(list, "NeedsDefaultRaisedParabola"), Is.EqualTo(false), "a converted preset still counts as the upper preset");
        object row = New("MenuLightRow", Lights(store)[0]!, 0, (Action<string?>)(_ => { }), store, light, (Action)(() => { }));
        Assert.That(Get(row, "ShowDefault"), Is.EqualTo(true), "a converted preset must offer reset back to Parabola");
        Assert.That((string)Get(row, "Label"), Does.Contain("Upper (Cube)"));
        Call(row, "RestoreDefault");
        Assert.That(Get(Lights(store)[0]!, "Type"), Is.EqualTo(Kind("Parabola")), "reset must return the converted preset to Parabola");
        Assert.That(Lights(store).Count, Is.EqualTo(1));

        object stray = Call(store, "CreateDefault", Kind("Parabola"));
        Call(store, "Upsert", stray);
        object strayRow = New("MenuLightRow", stray, 1, (Action<string?>)(_ => { }), store, light, (Action)(() => { }));
        Assert.That(Get(strayRow, "ShowDefault"), Is.EqualTo(false), "an unnamed parabola is never a preset");
        Assert.That((string)Get(strayRow, "Label"), Does.Not.Contain("Upper"));

        Assert.That(Call(store, "RestoreDefaultPreset", Kind("Parabola"), "missing-id"), Is.EqualTo(false), "a stale id must be rejected");
        Assert.That(Call(store, "RestoreDefaultPreset", Kind("Square"), (object?)null), Is.EqualTo(false), "only Floor and Parabola presets exist");
    }

    [Test]
    public void ResetWorksAtCapacityButRecreationNeverReplacesOthers()
    {
        object config = Config();
        object light = New("ParabolicFloorLight", config);
        object store = Get(light, "Store");
        object upper = Call(store, "CreateDefault", Kind("Parabola"));
        Set(upper, "Id", "default-raised-parabola");
        Set(upper, "Height", 5f);
        Call(store, "Upsert", upper);
        for (int index = 0; index < 7; index++)
            Call(store, "Upsert", Call(store, "CreateDefault", Kind("Cube")));
        Assert.That(Lights(store).Count, Is.EqualTo(8));
        string firstCubeId = (string)Get(Lights(store)[1]!, "Id");

        Assert.That(Call(store, "RestoreDefaultPreset", Kind("Floor"), (object?)null), Is.EqualTo(false), "recreating at the cap must not replace another light");
        Assert.That(Call(store, "RestoreDefaultPreset", Kind("Parabola"), (object?)null), Is.EqualTo(false), "the upper presence check must also block recreation");
        Assert.That(Lights(store).Count, Is.EqualTo(8));
        Assert.That(Get(Lights(store)[1]!, "Id"), Is.EqualTo(firstCubeId));

        Assert.That(Call(store, "RestoreDefaultPreset", Kind("Parabola"), "default-raised-parabola"), Is.EqualTo(true), "resetting an existing preset is always allowed");
        Assert.That(Lights(store).Count, Is.EqualTo(8));
        AssertUpperPreset(Lights(store)[0]!);
        Assert.That(Get(Lights(store)[1]!, "Id"), Is.EqualTo(firstCubeId), "other lights must survive an in-place reset");
    }

    private static void AssertUpperPreset(object light)
    {
        Assert.That(Get(light, "Type"), Is.EqualTo(Kind("Parabola")));
        Assert.That(Get(light, "Enabled"), Is.EqualTo(true));
        Assert.That((float)Get(light, "PositionX"), Is.EqualTo(0f).Within(1e-5f));
        Assert.That((float)Get(light, "PositionZ"), Is.EqualTo(0f).Within(1e-5f));
        Assert.That((float)Get(light, "Height"), Is.EqualTo(4.3f).Within(1e-5f));
        Assert.That((float)Get(light, "Depth"), Is.EqualTo(0.2f).Within(1e-5f));
        Assert.That((float)Get(light, "Size"), Is.EqualTo(1f).Within(1e-5f));
        Assert.That((float)Get(light, "EndLength"), Is.EqualTo(0f).Within(1e-5f));
        Assert.That((float)Get(light, "FadeStart"), Is.EqualTo(0.15f).Within(1e-5f));
        Assert.That((float)Get(light, "RotationX"), Is.EqualTo(20f).Within(1e-5f));
        Assert.That((float)Get(light, "RotationY"), Is.EqualTo(0f).Within(1e-5f));
        Assert.That((float)Get(light, "RotationZ"), Is.EqualTo(0f).Within(1e-5f));
        object appearance = Get(light, "Appearance");
        string[] fields = { "Brightness", "Red", "Green", "Blue", "LightWidthScale", "BloomWidthScale", "BloomBrightness", "HdrGlowThreshold", "HdrGlowWidthScale", "HdrGlowBrightness" };
        float[] expected = { 0.4f, 2.4f, 0f, 0f, 1.05f, 0.96f, 1.25f, 1.3f, 0.04f, 4f };
        for (int index = 0; index < fields.Length; index++)
            Assert.That(Field(appearance, fields[index]), Is.EqualTo(expected[index]).Within(1e-5f), fields[index]);
    }

    private static void SetBrightness(object light, float brightness)
        => Set(light, "Appearance", Call(Get(light, "Appearance"), "WithBrightness", brightness));

    private static Type TypeOf(string name) => Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings." + name, true)!;
    private static object New(string name, params object?[] args) => Activator.CreateInstance(TypeOf(name), Members, null, args, null)!;
    private static object Kind(string name) => Enum.Parse(TypeOf("MenuLightType"), name);
    private static object Config()
    {
        object config = New("Config");
        Set(config, "MenuLightsJson", "[]");
        return config;
    }

    private static IList Lights(object store) => (IList)Get(store, "Lights");
    private static object Get(object target, string name) => target.GetType().GetProperty(name, Members)!.GetValue(target)!;
    private static object Field(object target, string name) => target.GetType().GetField(name, Members)!.GetValue(target)!;
    private static void Set(object target, string name, object? value) => target.GetType().GetProperty(name, Members)!.SetValue(target, value);
    private static object Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Members)!.Invoke(target, args)!;
}
