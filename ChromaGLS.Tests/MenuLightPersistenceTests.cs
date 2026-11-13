using System;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using NUnit.Framework;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class MenuLightPersistenceTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [Test]
    public void SaveReloadPreservesEveryAppearanceAndPoseValue()
    {
        object config = NewConfig();
        object store = NewStore(config);
        object light = Default(store, "Sphere");
        Type settings = PluginType("ParabolicFloorLightSettings");
        float[] appearance = { 0.7f, 0.2f, 1.1f, 3.4f, 2.3f, 0f, 0f, 0f, 0.031f, 1.3f, 2.2f, 1.9f, 0.07f, 5.2f };
        object[] arguments = new object[appearance.Length];
        for (int index = 0; index < arguments.Length; index++)
            arguments[index] = appearance[index];
        object lighting = settings.GetConstructors(Members)[0].Invoke(arguments);
        Set(light, "Appearance", lighting);
        string[] properties =
        {
            "PositionX", "PositionZ", "Height", "Depth", "Size", "EndLength", "FadeStart",
            "QuaternionX", "QuaternionY", "QuaternionZ", "QuaternionW", "RotationX", "RotationY", "RotationZ",
        };
        float[] values = { 1.2f, -0.8f, 0.43f, 0.17f, 2.7f, 3.1f, 0.6f, 0.1f, 0.2f, 0.3f, 0.9f, 37f, -28f, 91f };
        for (int index = 0; index < properties.Length; index++)
            Set(light, properties[index], values[index]);
        Set(light, "Enabled", false);
        Invoke(store, "Upsert", light);

        object restored = Lights(NewStore(config))[0]!;
        Assert.That(Get(restored, "Id"), Is.EqualTo(Get(light, "Id")));
        Assert.That(Get(restored, "Type").ToString(), Is.EqualTo("Sphere"));
        Assert.That(Get(restored, "Enabled"), Is.EqualTo(false));
        foreach (string property in properties)
            Assert.That(Get(restored, property), Is.EqualTo(Get(light, property)), property);
        string[] fields =
        {
            "Brightness", "Red", "Green", "Blue", "WidthScale", "OffsetX", "OffsetZ", "Rotation",
            "LightWidthScale", "BloomWidthScale", "BloomBrightness", "HdrGlowThreshold", "HdrGlowWidthScale", "HdrGlowBrightness",
        };
        object restoredLighting = Get(restored, "Appearance");
        foreach (string field in fields)
            Assert.That(settings.GetField(field, Members)!.GetValue(restoredLighting), Is.EqualTo(settings.GetField(field, Members)!.GetValue(lighting)), field);
    }

    [Test]
    public void EditingAndResettingOneLightDoesNotMutateItsSavedNeighbor()
    {
        object config = NewConfig();
        object store = NewStore(config);
        object first = Default(store, "Square");
        object second = Default(store, "Circle");
        Set(second, "Height", 0.8f);
        Invoke(store, "Upsert", first);
        Invoke(store, "Upsert", second);
        string original = (string)Get(config, "MenuLightsJson");
        object session = NewSession(store);
        Invoke(session, "OpenExisting", Get(first, "Id"));
        Set(Get(session, "Working"), "Height", 2.4f);
        Assert.That(Get(Lights(store)[0]!, "Height"), Is.EqualTo(0.006f), "working state must be an independent copy");
        Invoke(session, "Reset");
        Assert.That(Get(Get(session, "Working"), "Id"), Is.EqualTo(Get(first, "Id")));
        Assert.That(Get(config, "MenuLightsJson"), Is.EqualTo(original), "Reset is a preview, not a save");
        Set(Get(session, "Working"), "Height", 1.7f);
        Invoke(session, "Save");
        IList saved = Lights(NewStore(config));
        Assert.That(Get(saved[0]!, "Height"), Is.EqualTo(1.7f));
        Assert.That(Get(saved[1]!, "Id"), Is.EqualTo(Get(second, "Id")));
        Assert.That(Get(saved[1]!, "Height"), Is.EqualTo(0.8f));
    }

    [Test]
    public void NewLightCancelRestoresItsExactPreviewIdWithoutConsumingASlot()
    {
        object config = NewConfig();
        object store = NewStore(config);
        string? restored = null;
        object session = NewSession(store, id => restored = id);
        Invoke(session, "OpenNew", Enum.Parse(PluginType("MenuLightType"), "Cube"));
        string previewId = (string)Get(Get(session, "Working"), "Id");
        Invoke(session, "Cancel");
        Assert.That(restored, Is.EqualTo(previewId));
        Assert.That(Lights(store).Count, Is.EqualTo(0));
        Assert.That(Get(config, "MenuLightsJson"), Is.EqualTo("[]"));
        Invoke(session, "OpenNew", Enum.Parse(PluginType("MenuLightType"), "Sphere"));
        Invoke(session, "Save");
        Assert.That(Lights(store).Count, Is.EqualTo(1));
        Invoke(store, "Remove", Get(Lights(store)[0]!, "Id"));
        Assert.That(Lights(NewStore(config)).Count, Is.EqualTo(0), "a saved empty list must not migrate the old parabola again");
    }

    [Test]
    public void RemovingAnEarlierRowDoesNotChangeTheStableIdUsedForEditing()
    {
        object config = NewConfig();
        object store = NewStore(config);
        object first = Default(store, "Square");
        object second = Default(store, "Circle");
        Invoke(store, "Upsert", first);
        Invoke(store, "Upsert", second);
        Invoke(store, "Remove", Get(first, "Id"));
        object session = NewSession(store);
        Invoke(session, "OpenExisting", Get(second, "Id"));
        Set(Get(session, "Working"), "Size", 2.5f);
        Invoke(session, "Save");
        IList saved = Lights(NewStore(config));
        Assert.That(saved.Count, Is.EqualTo(1));
        Assert.That(Get(saved[0]!, "Id"), Is.EqualTo(Get(second, "Id")));
        Assert.That(Get(saved[0]!, "Size"), Is.EqualTo(2.5f));
    }

    [Test]
    public void MigrationRetainsAnIndependentMasterSwitchWithoutWritingConfig()
    {
        object config = NewConfig();
        Set(config, "MenuLightsJson", string.Empty);
        Set(config, "ParabolicFloorLightEnabled", false);
        Set(config, "ParabolicFloorLightRotation", 315f);
        object light = Lights(NewStore(config))[0]!;
        Assert.That(Get(light, "Enabled"), Is.EqualTo(true));
        Assert.That(Get(light, "RotationY"), Is.EqualTo(-45f));
        Assert.That(Get(config, "ParabolicFloorLightEnabled"), Is.EqualTo(false));
        Assert.That(Get(config, "MenuLightsJson"), Is.EqualTo(string.Empty), "opening must not silently persist a migration");
    }

    [Test]
    public void MarkerOneUpgradeSwapsOnlyTheDefaultPairColors()
    {
        object config = NewConfig();
        object seed = NewStore(config);
        object floor = Default(seed, "Floor");
        SetRgb(floor, 10f, 0f, 0f);
        SetBrightness(floor, 0.11f);
        Set(floor, "Enabled", false);
        Set(floor, "Height", 0.125f);
        object preset = Default(seed, "Parabola");
        Set(preset, "Id", "default-raised-parabola");
        SetRgb(preset, 0f, 1f, 2.5f);
        SetBrightness(preset, 0.43f);
        Set(preset, "Height", 5f);
        object customParabola = Default(seed, "Parabola");
        Set(customParabola, "Id", "3d1f7ade000000000000000000000000");
        SetRgb(customParabola, 0f, 1f, 2.5f);
        Set(customParabola, "Enabled", false);
        object customFloor = Default(seed, "Floor");
        SetRgb(customFloor, 5f, 5f, 5f);
        SetBrightness(customFloor, 0.7f);
        Set(config, "MenuLightsJson", Newtonsoft.Json.JsonConvert.SerializeObject(new[] { floor, preset, customParabola, customFloor }));
        Set(config, "MenuLightDefaultsVersion", 1);

        IList lights = Lights(NewStore(config));
        Assert.That(lights.Count, Is.EqualTo(4), "the color migration must not add or remove lights");
        object upgradedFloor = lights[0]!;
        Assert.That(Get(upgradedFloor, "Id"), Is.EqualTo(Get(floor, "Id")));
        Assert.That(Get(upgradedFloor, "Enabled"), Is.EqualTo(false), "the migration must not re-enable a disabled floor");
        Assert.That((float)Get(upgradedFloor, "Height"), Is.EqualTo(0.125f).Within(1e-5f));
        Assert.That(Channel(upgradedFloor, "Brightness"), Is.EqualTo(0.43f).Within(1e-5f), "the brightness swap must land on the migrated floor");
        Assert.That(Channel(upgradedFloor, "LightWidthScale"), Is.EqualTo(1f).Within(1e-5f), "non-RGB appearance fields must survive the swap");
        Assert.That(Channel(upgradedFloor, "Red"), Is.EqualTo(0f).Within(1e-5f));
        Assert.That(Channel(upgradedFloor, "Green"), Is.EqualTo(1f).Within(1e-5f));
        Assert.That(Channel(upgradedFloor, "Blue"), Is.EqualTo(2.5f).Within(1e-5f));
        object upgradedPreset = lights[1]!;
        Assert.That(Get(upgradedPreset, "Id"), Is.EqualTo("default-raised-parabola"));
        Assert.That((float)Get(upgradedPreset, "Height"), Is.EqualTo(5f).Within(1e-5f));
        Assert.That(Channel(upgradedPreset, "Red"), Is.EqualTo(10f).Within(1e-5f));
        Assert.That(Channel(upgradedPreset, "Green"), Is.EqualTo(0f).Within(1e-5f));
        Assert.That(Channel(upgradedPreset, "Blue"), Is.EqualTo(0f).Within(1e-5f));
        Assert.That(Channel(upgradedPreset, "Brightness"), Is.EqualTo(0.11f).Within(1e-5f), "the named preset must also take the new brightness");
        Assert.That(Channel(lights[2]!, "Red"), Is.EqualTo(0f).Within(1e-5f), "a non-preset parabola keeps its saved color even when it matches the old preset");
        Assert.That(Channel(lights[2]!, "Blue"), Is.EqualTo(2.5f).Within(1e-5f));
        Assert.That(Channel(lights[3]!, "Red"), Is.EqualTo(5f).Within(1e-5f), "a custom floor color must not be rewritten");
        Assert.That(Channel(lights[3]!, "Brightness"), Is.EqualTo(0.7f).Within(1e-5f), "a custom floor keeps its saved brightness");
        Assert.That((int)Get(config, "MenuLightDefaultsVersion"), Is.EqualTo(3));

        IList reloaded = Lights(NewStore(config));
        Assert.That(Channel(reloaded[0]!, "Blue"), Is.EqualTo(2.5f).Within(1e-5f), "the swap must be persisted to the saved JSON");
        Assert.That(Channel(reloaded[1]!, "Red"), Is.EqualTo(10f).Within(1e-5f));
    }

    [Test]
    public void MarkerTwoLeavesDeliberatelyRestoredOldColorsAlone()
    {
        object config = NewConfig();
        object seed = NewStore(config);
        object floor = Default(seed, "Floor");
        SetRgb(floor, 10f, 0f, 0f);
        object preset = Default(seed, "Parabola");
        Set(preset, "Id", "default-raised-parabola");
        SetRgb(preset, 0f, 1f, 2.5f);
        Set(config, "MenuLightsJson", Newtonsoft.Json.JsonConvert.SerializeObject(new[] { floor, preset }));
        Set(config, "MenuLightDefaultsVersion", 2);

        IList lights = Lights(NewStore(config));
        Assert.That(Channel(lights[0]!, "Red"), Is.EqualTo(10f).Within(1e-5f), "colors restored after the migration must not swap again");
        Assert.That(Channel(lights[1]!, "Blue"), Is.EqualTo(2.5f).Within(1e-5f));
        Assert.That((int)Get(config, "MenuLightDefaultsVersion"), Is.EqualTo(3), "unmatched entries still advance the marker");
    }

    [Test]
    public void MarkerTwoUpgradeSwapsOnlyTheDefaultPairBrightness()
    {
        object config = NewConfig();
        object seed = NewStore(config);
        object floor = Default(seed, "Floor");
        SetRgb(floor, 0f, 1f, 2.5f);
        SetBrightness(floor, 0.11f);
        Set(floor, "Enabled", false);
        Set(floor, "Height", 0.125f);
        object preset = Default(seed, "Parabola");
        Set(preset, "Id", "default-raised-parabola");
        SetRgb(preset, 10f, 0f, 0f);
        SetBrightness(preset, 0.43f);
        Set(preset, "Height", 5f);
        Set(config, "MenuLightsJson", Newtonsoft.Json.JsonConvert.SerializeObject(new[] { floor, preset }));
        Set(config, "MenuLightDefaultsVersion", 2);

        IList lights = Lights(NewStore(config));
        Assert.That(lights.Count, Is.EqualTo(2));
        Assert.That(Channel(lights[0]!, "Brightness"), Is.EqualTo(0.43f).Within(1e-5f), "the saved floor must take the new brightness");
        Assert.That(Channel(lights[0]!, "Blue"), Is.EqualTo(2.5f).Within(1e-5f), "colors must not be re-swapped at marker two");
        Assert.That(Get(lights[0]!, "Enabled"), Is.EqualTo(false), "the migration must not re-enable a disabled floor");
        Assert.That((float)Get(lights[0]!, "Height"), Is.EqualTo(0.125f).Within(1e-5f));
        Assert.That(Channel(lights[1]!, "Brightness"), Is.EqualTo(0.11f).Within(1e-5f));
        Assert.That(Channel(lights[1]!, "Red"), Is.EqualTo(10f).Within(1e-5f));
        Assert.That((float)Get(lights[1]!, "Height"), Is.EqualTo(5f).Within(1e-5f));
        Assert.That((int)Get(config, "MenuLightDefaultsVersion"), Is.EqualTo(3));
        IList reloaded = Lights(NewStore(config));
        Assert.That(Channel(reloaded[0]!, "Brightness"), Is.EqualTo(0.43f).Within(1e-5f), "the brightness swap must persist");
    }

    [Test]
    public void MarkerThreeLeavesDeliberatelyRestoredBrightnessAlone()
    {
        object config = NewConfig();
        object seed = NewStore(config);
        object floor = Default(seed, "Floor");
        SetRgb(floor, 0f, 1f, 2.5f);
        SetBrightness(floor, 0.11f);
        Set(config, "MenuLightsJson", Newtonsoft.Json.JsonConvert.SerializeObject(new[] { floor }));
        Set(config, "MenuLightDefaultsVersion", 3);

        IList lights = Lights(NewStore(config));
        Assert.That(Channel(lights[0]!, "Brightness"), Is.EqualTo(0.11f).Within(1e-5f), "a brightness restored after the migration must not change again");
    }

    [Test]
    public void EmptyListStillAdvancesToMarkerThreeWithoutResurrectingLights()
    {
        object config = NewConfig();
        Set(config, "MenuLightsJson", "[]");
        Set(config, "MenuLightDefaultsVersion", 1);
        object store = NewStore(config);
        Assert.That(Lights(store).Count, Is.EqualTo(0));
        Assert.That((int)Get(config, "MenuLightDefaultsVersion"), Is.EqualTo(3));
        Assert.That((string)Get(config, "MenuLightsJson"), Is.EqualTo("[]"), "nothing to swap means the JSON is not rewritten");
    }

    private static void SetBrightness(object light, float brightness)
        => Set(light, "Appearance", Invoke(Get(light, "Appearance"), "WithBrightness", brightness));

    private static void SetRgb(object light, float red, float green, float blue)
    {
        object appearance = Get(light, "Appearance");
        appearance = Invoke(appearance, "WithRed", red);
        appearance = Invoke(appearance, "WithGreen", green);
        appearance = Invoke(appearance, "WithBlue", blue);
        Set(light, "Appearance", appearance);
    }

    private static float Channel(object light, string name)
    {
        object appearance = Get(light, "Appearance");
        return (float)appearance.GetType().GetField(name, Members)!.GetValue(appearance)!;
    }

    private static object NewSession(object store, Action<string>? restore = null)
    {
        Type type = PluginType("MenuLightEditSession");
        ParameterInfo[] parameters = type.GetConstructors(Members)[0].GetParameters();
        object[] arguments = new object[4];
        arguments[0] = store;
        for (int index = 1; index < arguments.Length; index++)
        {
            Type callback = parameters[index].ParameterType;
            ParameterExpression argument = Expression.Parameter(callback.GetMethod("Invoke")!.GetParameters()[0].ParameterType);
            Expression body = index == 2 && restore != null
                ? Expression.Invoke(Expression.Constant(restore), argument)
                : Expression.Empty();
            arguments[index] = Expression.Lambda(callback, body, argument).Compile();
        }

        return Activator.CreateInstance(type, Members, null, arguments, null)!;
    }

    private static object NewConfig()
    {
        object config = Activator.CreateInstance(PluginType("Config"), true)!;
        Set(config, "MenuLightsJson", "[]");
        return config;
    }

    private static Type PluginType(string name)
        => Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings." + name, true)!;

    private static object NewStore(object config)
        => Activator.CreateInstance(PluginType("MenuLightStore"), Members, null, new[] { config }, null)!;

    private static object Default(object store, string type)
        => Invoke(store, "CreateDefault", Enum.Parse(PluginType("MenuLightType"), type));

    private static IList Lights(object store)
        => (IList)Get(store, "Lights");

    private static object Get(object instance, string name)
        => instance.GetType().GetProperty(name, Members)!.GetValue(instance)!;

    private static void Set(object instance, string name, object value)
        => instance.GetType().GetProperty(name, Members)!.SetValue(instance, value);

    private static object Invoke(object instance, string name, params object[] arguments)
        => instance.GetType().GetMethod(name, Members)!.Invoke(instance, arguments)!;

    [Test]
    public void FreshConfigurationStartsWithDefaultFloorLight()
    {
        object config = Activator.CreateInstance(PluginType("Config"), true)!;
        Assert.That((string)Get(config, "MenuLightsJson")!, Is.EqualTo(string.Empty), "the initial config must carry no saved-light JSON so migration runs");
        Assert.That((bool)Get(config, "ParabolicFloorLightEnabled")!, Is.True, "the master toggle defaults to on");
        object store = NewStore(config);
        IList lights = Lights(store);
        Assert.That(lights.Count, Is.EqualTo(2), "a fresh config seeds the floor light and the raised parabola");
        object light = lights[0]!;
        Assert.That((string)Get(light, "Id")!, Is.Not.Empty);
        Assert.That(Get(light, "Type"), Is.EqualTo(Enum.Parse(PluginType("MenuLightType"), "Floor")));
        Assert.That((bool)Get(light, "Enabled")!, Is.True);
        Assert.That(Get(light, "Appearance"), Is.EqualTo(PluginType("ParabolicFloorLightSettings").GetField("FloorDefault", Members)!.GetValue(null)!));
        Assert.That((float)Get(light, "PositionX")!, Is.EqualTo(0f));
        Assert.That((float)Get(light, "PositionZ")!, Is.EqualTo(0f));
        Assert.That((float)Get(light, "Height")!, Is.EqualTo(0.02f));
        Assert.That((float)Get(light, "Depth")!, Is.EqualTo(0.02f));
        Assert.That((float)Get(light, "Size")!, Is.EqualTo(1f));
        Assert.That((float)Get(light, "EndLength")!, Is.EqualTo(0f));
        Assert.That((float)Get(light, "FadeStart")!, Is.EqualTo(0.13f));
        Assert.That((float)Get(light, "QuaternionX")!, Is.EqualTo(0f));
        Assert.That((float)Get(light, "QuaternionY")!, Is.EqualTo(0f));
        Assert.That((float)Get(light, "QuaternionZ")!, Is.EqualTo(0f));
        Assert.That((float)Get(light, "QuaternionW")!, Is.EqualTo(1f));
        Assert.That((float)Get(light, "RotationX")!, Is.EqualTo(0f));
        Assert.That((float)Get(light, "RotationY")!, Is.EqualTo(0f));
        Assert.That((float)Get(light, "RotationZ")!, Is.EqualTo(0f));
        Assert.That((string)Get(config, "MenuLightsJson")!, Is.EqualTo(string.Empty), "merely opening the store must not write the JSON");
    }

    [Test]
    public void DefaultFloorLightSurvivesAddingAndReloading()
    {
        object config = Activator.CreateInstance(PluginType("Config"), true)!;
        object store = NewStore(config);
        object first = Lights(store)[0]!;
        string firstId = (string)Get(first, "Id")!;
        object square = Default(store, "Square");
        Invoke(store, "Upsert", square);
        string squareId = (string)Get(square, "Id")!;
        IList reloaded = Lights(NewStore(config));
        Assert.That(reloaded.Count, Is.EqualTo(3), "the seeded floor, raised parabola, and added square must all persist");
        object reloadedFirst = reloaded[0]!;
        object reloadedSquare = reloaded[2]!;
        Assert.That((string)Get(reloadedFirst, "Id")!, Is.EqualTo(firstId));
        Assert.That(Get(reloadedFirst, "Type"), Is.EqualTo(Enum.Parse(PluginType("MenuLightType"), "Floor")));
        Assert.That(Get(reloadedFirst, "Appearance"), Is.EqualTo(PluginType("ParabolicFloorLightSettings").GetField("FloorDefault", Members)!.GetValue(null)!));
        Assert.That((float)Get(reloadedFirst, "Height")!, Is.EqualTo(0.02f));
        Assert.That((float)Get(reloadedFirst, "Depth")!, Is.EqualTo(0.02f));
        Assert.That((string)Get(reloadedSquare, "Id")!, Is.EqualTo(squareId));
        Assert.That(Get(reloadedSquare, "Type"), Is.EqualTo(Enum.Parse(PluginType("MenuLightType"), "Square")));
    }

    [Test]
    public void ExplicitlySavedHeightIsNotSilentlyMigrated()
    {
        object config = NewConfig();
        object store = NewStore(config);
        object light = Default(store, "Parabola");
        Set(light, "Height", 0.03f);
        Invoke(store, "Upsert", light);
        IList reloaded = Lights(NewStore(config));
        Assert.That((float)Get(reloaded[0]!, "Height")!, Is.EqualTo(0.03f), "an explicitly saved pose must keep its stored height, not silently receive the new default");
        object session = NewSession(store);
        Invoke(session, "OpenExisting", Get(reloaded[0]!, "Id"));
        Assert.That((float)Get(Get(session, "Working"), "Height")!, Is.EqualTo(0.03f), "opening the saved light must show its stored height");
        Invoke(session, "Reset");
        Assert.That((float)Get(Get(session, "Working"), "Height")!, Is.EqualTo(4.3f), "reset previews the new default");
        Assert.That((float)Get(Lights(store)[0]!, "Height")!, Is.EqualTo(0.03f), "the stored light keeps its saved height until an explicit save");
        Invoke(session, "Cancel");
        reloaded = Lights(NewStore(config));
        Assert.That((float)Get(reloaded[0]!, "Height")!, Is.EqualTo(0.03f), "cancel must leave the saved height untouched");
    }
}
