using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml.Linq;
using NUnit.Framework;

namespace ChromaGLS.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class FerrofluidFluidObjectsTests
{
    private static Assembly GetPlugin() => Assembly.Load("ChromaGLS");

    private static XElement LoadBsml(string resource)
    {
        using Stream stream = GetPlugin().GetManifestResourceStream($"ChromaGLS.Settings.{resource}")
            ?? throw new InvalidOperationException($"embedded resource missing: {resource}");
        return XElement.Load(stream);
    }

    [Test]
    public void BothViewsExposeTheTwoFluidSwitches()
    {
        foreach (string view in new[] { "modifiers.bsml", "modifiers-minimal.bsml" })
        {
            XElement bsml = LoadBsml(view);
            foreach ((string binding, string label) in new[]
            {
                ("fluid-cube-enabled", "Add Fluid cube"),
                ("fluid-sphere-enabled", "Add Fluid Sphere"),
            })
            {
                XElement? row = bsml.Descendants("bool-setting")
                    .SingleOrDefault(element => (string?)element.Attribute("value") == binding);
                Assert.That(row, Is.Not.Null, $"{view} is missing a '{label}' switch bound to {binding}");
                Assert.That((string?)row!.Attribute("text"), Is.EqualTo(label), $"{view} switch text must read '{label}'");
            }
        }
    }

    [Test]
    public void FluidFlagsArePersistedAndIndependent()
    {
        object config = Activator.CreateInstance(GetPlugin().GetType("ChromaGLS.Settings.Config", true)!, nonPublic: true)!;
        Type ignoreType = typeof(IPA.Config.Stores.GeneratedStore).Assembly
            .GetType("IPA.Config.Stores.Attributes.IgnoreAttribute", true)!;
        foreach (string name in new[] { "FluidCubeEnabled", "FluidSphereEnabled" })
        {
            PropertyInfo? property = config.GetType().GetProperty(name);
            Assert.That(property, Is.Not.Null, $"Config.{name} must exist");
            Assert.That(property!.IsDefined(ignoreType, true), Is.False, $"{name} must persist");
            Assert.That((bool)property.GetValue(config)!, Is.False, $"{name} defaults off");
        }

        config.GetType().GetProperty("FluidCubeEnabled")!.SetValue(config, true);
        Assert.That((bool)config.GetType().GetProperty("FluidSphereEnabled")!.GetValue(config)!, Is.False, "flags are independent");
        Assert.That((bool)config.GetType().GetProperty("FerrofluidCubeEnabled")!.GetValue(config)!, Is.False, "originals untouched");
    }

    [Test]
    public void UiSettersDriveConfigAndEnvironmentWithoutManager()
    {
        object config = Activator.CreateInstance(GetPlugin().GetType("ChromaGLS.Settings.Config", true)!, nonPublic: true)!;
        config.GetType().GetProperty("FerrofluidCubeEnabled")!.SetValue(config, true);
        config.GetType().GetProperty("FerrofluidSphereEnabled")!.SetValue(config, true);

        Type uiType = GetPlugin().GetType("ChromaGLS.Settings.ChromaGLSSettingsUI", true)!;
        Type envType = GetPlugin().GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true)!;
        object ui = FormatterServices.GetUninitializedObject(uiType);
        uiType.GetField("_config", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(ui, config);

        FieldInfo envConfig = envType.GetField("_config", BindingFlags.Static | BindingFlags.NonPublic)!;
        FieldInfo menuActive = envType.GetField("_menuActive", BindingFlags.Static | BindingFlags.NonPublic)!;
        FieldInfo cubeEnabled = envType.GetField("_cubeEnabled", BindingFlags.Static | BindingFlags.NonPublic)!;
        FieldInfo sphereEnabled = envType.GetField("_sphereEnabled", BindingFlags.Static | BindingFlags.NonPublic)!;
        FieldInfo? fluidCube = envType.GetField("_fluidCubeEnabled", BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo? fluidSphere = envType.GetField("_fluidSphereEnabled", BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo? manager = envType.GetField("_manager", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(fluidCube, Is.Not.Null, "environment must expose _fluidCubeEnabled");
        Assert.That(fluidSphere, Is.Not.Null, "environment must expose _fluidSphereEnabled");

        object[] previous =
        {
            envConfig.GetValue(null)!,
            menuActive.GetValue(null)!,
            cubeEnabled.GetValue(null)!,
            sphereEnabled.GetValue(null)!,
            fluidCube!.GetValue(null)!,
            fluidSphere!.GetValue(null)!,
        };
        try
        {
            envConfig.SetValue(null, config);
            menuActive.SetValue(null, false);
            cubeEnabled.SetValue(null, true);
            sphereEnabled.SetValue(null, true);
            fluidCube.SetValue(null, false);
            fluidSphere.SetValue(null, false);

            foreach ((string uiName, FieldInfo ownFlag, FieldInfo otherFlag) in new (string, FieldInfo, FieldInfo)[]
            {
                ("FluidCubeEnabled", fluidCube, fluidSphere),
                ("FluidSphereEnabled", fluidSphere, fluidCube),
            })
            {
                PropertyInfo? property = uiType.GetProperty(uiName);
                Assert.That(property, Is.Not.Null, $"UI property {uiName} must exist");
                foreach (bool state in new[] { true, false })
                {
                    property!.SetValue(ui, state);
                    Assert.That((bool)config.GetType().GetProperty(uiName)!.GetValue(config)!, Is.EqualTo(state), $"{uiName} writes config");
                    Assert.That((bool)ownFlag.GetValue(null)!, Is.EqualTo(state), $"{uiName} sets its own flag");
                    Assert.That((bool)otherFlag.GetValue(null)!, Is.False, "the other fluid flag is independent");
                    Assert.That((bool)cubeEnabled.GetValue(null)!, Is.True, "original cube flag unchanged");
                    Assert.That((bool)sphereEnabled.GetValue(null)!, Is.True, "original sphere flag unchanged");
                    Assert.That((bool)config.GetType().GetProperty("FerrofluidCubeEnabled")!.GetValue(config)!, Is.True);
                    Assert.That((bool)config.GetType().GetProperty("FerrofluidSphereEnabled")!.GetValue(config)!, Is.True);
                    Assert.That(manager?.GetValue(null), Is.Null, "no manager is created outside the menu");
                }
            }
        }
        finally
        {
            envConfig.SetValue(null, previous[0]);
            menuActive.SetValue(null, previous[1]);
            cubeEnabled.SetValue(null, previous[2]);
            sphereEnabled.SetValue(null, previous[3]);
            fluidCube.SetValue(null, previous[4]);
            fluidSphere.SetValue(null, previous[5]);
        }
    }

    [Test]
    public void FluidPlacementKeepsOriginalsAndMovesBehindThem()
    {
        Type env = GetPlugin().GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true)!;
        Type shapeType = GetPlugin().GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", true)!;
        MethodInfo? fluidMethod = env.GetMethod("EvaluateFluidPlacement", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(fluidMethod, Is.Not.Null, "EvaluateFluidPlacement must exist");
        foreach (int shape in new[] { 1, 2 })
        {
            object[] placementArgs = { Enum.ToObject(shapeType, shape), null!, null! };
            env.GetMethod("EvaluatePlacement", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, placementArgs);
            object[] fluidArgs = { Enum.ToObject(shapeType, shape), null!, null! };
            fluidMethod!.Invoke(null, fluidArgs);
            UnityEngine.Vector3 original = (UnityEngine.Vector3)placementArgs[1];
            UnityEngine.Vector3 fluid = (UnityEngine.Vector3)fluidArgs[1];
            Assert.That(fluid.x, Is.EqualTo(original.x));
            Assert.That(fluid.y, Is.EqualTo(original.y));
            Assert.That(fluid.z, Is.EqualTo(0f), $"fluid {shape} parks behind the originals");
            Assert.That(original.z, Is.EqualTo(1f));
            Assert.That(fluidArgs[2], Is.EqualTo(placementArgs[2]), "fluid keeps the same scale");
        }
    }
}
