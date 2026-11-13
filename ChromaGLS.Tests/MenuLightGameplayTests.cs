using System;
using System.Reflection;
using NUnit.Framework;

namespace ChromaGLS.Tests;

[TestFixture]
public class MenuLightGameplayTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void GameplayGateRejectsLateEnableAndPreservesSavedMaster()
    {
        object config = New("Config");
        Set(config, "ParabolicFloorLightEnabled", true);
        object light = New("ParabolicFloorLight", config);
        Invoke(light, "SetEnabled", true);
        Assert.That(Enabled(light), Is.True);

        Invoke(light, "SetMenuActive", false);
        Assert.That(Enabled(light), Is.False, "gameplay must suppress the menu-light master");
        Invoke(light, "SetEnabled", true);
        Assert.That(Enabled(light), Is.False, "a late UI callback cannot reenable menu lights in gameplay");
        Assert.That(Get(config, "ParabolicFloorLightEnabled"), Is.True, "runtime suspension is not a saved edit");
    }

    [Test]
    public void ReturningToMenuRestoresSavedMasterWithoutWritingTheStore()
    {
        object config = New("Config");
        Set(config, "ParabolicFloorLightEnabled", true);
        object light = New("ParabolicFloorLight", config);
        object originalJson = Get(config, "MenuLightsJson");
        Invoke(light, "SetEnabled", true);
        Invoke(light, "SetMenuActive", false);
        Invoke(light, "SetMenuActive", true);
        Assert.That(Enabled(light), Is.True);
        Assert.That(Get(config, "MenuLightsJson"), Is.EqualTo(originalJson));
    }

    [Test]
    public void ReturningToMenuRespectsTheSavedDisabledMaster()
    {
        object config = New("Config");
        Set(config, "ParabolicFloorLightEnabled", false);
        object light = New("ParabolicFloorLight", config);
        Invoke(light, "SetMenuActive", false);
        Invoke(light, "SetEnabled", true);
        Invoke(light, "SetMenuActive", true);
        Assert.That(Enabled(light), Is.False);
        Assert.That(Get(config, "ParabolicFloorLightEnabled"), Is.False);
    }

    private static object New(string name, params object[] args)
    {
        Type type = Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings." + name, true)!;
        return Activator.CreateInstance(type, Members, null, args, null)!;
    }

    private static object Get(object value, string name) => value.GetType().GetProperty(name, Members)!.GetValue(value)!;

    private static void Set(object value, string name, object setting) => value.GetType().GetProperty(name, Members)!.SetValue(value, setting);

    private static void Invoke(object value, string name, params object[] args) => value.GetType().GetMethod(name, Members)!.Invoke(value, args);

    private static bool Enabled(object light) => (bool)light.GetType().GetField("_enabled", Members)!.GetValue(light)!;
}
