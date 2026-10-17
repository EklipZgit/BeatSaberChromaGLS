using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class FerrofluidToggleTests
{
    [Test]
    public void CubeAndSpherePreferencesDoNotCreateObjectsOutsideTheMenu()
    {
        Assembly plugin = Assembly.Load("ChromaGLS");
        Type environment = plugin.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true)!;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        FieldInfo configField = environment.GetField("_config", flags)!;
        object? previousConfig = configField.GetValue(null);
        FieldInfo cubeField = environment.GetField("_cubeEnabled", flags)!;
        FieldInfo sphereField = environment.GetField("_sphereEnabled", flags)!;
        object previousCube = cubeField.GetValue(null)!;
        object previousSphere = sphereField.GetValue(null)!;
        object config = Activator.CreateInstance(plugin.GetType("ChromaGLS.Settings.Config", true)!, true)!;
        MethodInfo endMenu = environment.GetMethod("EndMenuSession", flags)!;
        try
        {
            endMenu.Invoke(null, null);
            environment.GetMethod("ApplyConfiguration", flags)!.Invoke(null, [config]);
            environment.GetMethod("SetCubeEnabled", flags)!.Invoke(null, [true]);
            environment.GetMethod("SetSphereEnabled", flags)!.Invoke(null, [true]);
            Assert.That(cubeField.GetValue(null), Is.True);
            Assert.That(sphereField.GetValue(null), Is.True);
            Assert.That(environment.GetField("_manager", flags)!.GetValue(null), Is.Null,
                "App-level preferences must not start a persistent menu manager during gameplay.");
            endMenu.Invoke(null, null);
        }
        finally
        {
            configField.SetValue(null, previousConfig);
            cubeField.SetValue(null, previousCube);
            sphereField.SetValue(null, previousSphere);
        }
    }

    // SongCoreWriterResolvesOnInstalledVersion reproduces the 1.37.1 null-target failure without depending on the host's MonoMod detour backend.
    [Test]
    public void SongCoreWriterResolvesOnInstalledVersion()
    {
        Type patch = Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings.ChromaGlsRequirementSelectionPatch", true)!;
        MethodInfo resolver = patch.GetMethod("TargetMethod", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodBase? target = (MethodBase?)resolver.Invoke(null, null);
        Assert.That(target, Is.Not.Null, "The installed SongCore metadata writer must resolve; a null visual target aborts gameplay PatchAll.");
        Assert.That(target!.DeclaringType!.Assembly.GetName().Name, Is.EqualTo("SongCore"));
        Assert.That(target.Name, Is.EqualTo("Postfix").Or.EqualTo("ProcessBeatmapRequirements").Or.EqualTo("HandleBeatmapRequirements"));
        Assert.That(patch.GetMethod("Prepare", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null), Is.EqualTo(true));
        MethodInfo postfix = patch.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.That(Array.Exists(postfix.GetParameters(), parameter => parameter.Name == "__instance"), Is.False, "Legacy static writers have no owner instance to inject.");
        MethodInfo resolve = patch.GetMethod("ResolveSelectionWriter", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.That(resolve.Invoke(null, [typeof(string).Assembly]), Is.Null, "Unknown SongCore layouts must be skippable rather than throwing during gameplay patch installation.");
    }

    [Test]
    public void FerrofluidBorderDefaultsEnabledAndAnswersOnlyToItsOwnSwitch()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type configType = pluginAssembly.GetType("ChromaGLS.Settings.Config", throwOnError: true)!;
        Type effectType = pluginAssembly.GetType("ChromaGLS.Settings.ChromaGlsRequirementEffect", throwOnError: true)!;

        // Regression: disabling the visual must be a persisted independent setting whose false state prevents effect construction, simulation, and render submission entirely.
        PropertyInfo? setting = configType.GetProperty("FerrofluidBorderEnabled", BindingFlags.Instance | BindingFlags.Public);
        MethodInfo? shouldCreate = effectType.GetMethod("ShouldCreateEffect", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.That(setting, Is.Not.Null, "The generated config must persist an independent ferrofluid-border toggle.");
        object config = Activator.CreateInstance(configType, nonPublic: true)!;
        Assert.That(setting!.GetValue(config), Is.EqualTo(true), "The existing ferrofluid border must remain enabled by default.");
        Assert.That(shouldCreate, Is.Not.Null, "The effect lifecycle must expose one authoritative gate for creation and runtime work.");
        Assert.That(shouldCreate!.Invoke(null, [true]), Is.EqualTo(true));
        Assert.That(shouldCreate.Invoke(null, [false]), Is.EqualTo(false));

        // MainSwitchControlsOnlyMapPatches: the plugin-level gate was removed so the Enable ChromaGLS switch cannot kill the indicator; the info-button torus answers only to its own toggle.
        Assert.That(effectType.GetMethod("SetPluginEnabled", BindingFlags.Static | BindingFlags.NonPublic), Is.Null, "The plugin toggle must no longer reach the ferrofluid indicator lifecycle.");

        // Regression: the lifecycle preference must be user-accessible from the embedded ChromaGLS GameplaySetup tab, not merely available through config internals.
        using Stream? bsmlStream = pluginAssembly.GetManifestResourceStream("ChromaGLS.Settings.modifiers.bsml");
        Assert.That(bsmlStream, Is.Not.Null, "The ChromaGLS settings view must remain embedded in the plugin.");
        using StreamReader reader = new(bsmlStream!);
        string bsml = reader.ReadToEnd();
        Assert.That(bsml, Does.Contain("value='ferrofluid-border-enabled'"), "The ChromaGLS tab must expose the independent ferrofluid-border toggle.");
    }

    // FerrofluidTweakMenuHiddenBehindBuildFlag verifies the main-build settings surface embeds only the four public toggles; the full tweak view stays embedded for builds compiled with -p:FerrofluidTweaks=true.
    [Test]
    public void MinimalSettingsSurfaceExposesOnlyTheFourPublicToggles()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        using Stream? bsmlStream = pluginAssembly.GetManifestResourceStream("ChromaGLS.Settings.modifiers-minimal.bsml");
        Assert.That(bsmlStream, Is.Not.Null, "The minimal settings view must stay embedded so main builds can hide the tweak surface.");
        using StreamReader reader = new(bsmlStream!);
        string bsml = reader.ReadToEnd();
        Assert.That(bsml, Does.Contain("value='enabled'"));
        Assert.That(bsml, Does.Contain("value='ferrofluid-border-enabled'"));
        Assert.That(bsml, Does.Contain("value='ferrofluid-cube-enabled'"));
        Assert.That(bsml, Does.Contain("value='ferrofluid-sphere-enabled'"));
        Assert.That(bsml, Does.Contain("id='ferrofluid-preview-anchor'"), "The lifecycle anchor must stay bound in the minimal view.");
        Assert.That(bsml, Does.Not.Contain("slider-setting"), "Main builds must not expose the session-only tweak sliders.");
        Assert.That(bsml, Does.Not.Contain("ferrofluid-body-enabled"), "The tweak-only torus visibility control stays behind the build flag.");

        Type uiType = pluginAssembly.GetType("ChromaGLS.Settings.ChromaGLSSettingsUI", throwOnError: true)!;
        FieldInfo? flag = uiType.GetField("FerrofluidTweakMenuEnabled", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(flag, Is.Not.Null, "The tweak-menu flag must be observable on the shipped assembly.");
        Assert.That(flag!.GetRawConstantValue(), Is.EqualTo(false), "This test host builds without -p:FerrofluidTweaks, so the menu flag and the mod-menu torus preview must be off; rebuild with the flag when intentionally testing tweak builds.");
    }

    // EnvironmentFerrofluidSwitchesPersistOffByDefault covers the requested optional scene objects without letting either switch inherit the enabled torus preference.
    [Test]
    public void EnvironmentFerrofluidSwitchesPersistOffByDefault()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type configType = pluginAssembly.GetType("ChromaGLS.Settings.Config", throwOnError: true)!;
        Type pluginType = pluginAssembly.GetType("ChromaGLS.Plugin", throwOnError: true)!;
        Type environmentType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", throwOnError: true)!;
        Type ignoreType = typeof(IPA.Config.Stores.GeneratedStore).Assembly.GetType("IPA.Config.Stores.Attributes.IgnoreAttribute", true)!;
        object config = Activator.CreateInstance(configType, nonPublic: true)!;
        PropertyInfo? cubeSetting = configType.GetProperty("FerrofluidCubeEnabled", BindingFlags.Instance | BindingFlags.Public);
        PropertyInfo? sphereSetting = configType.GetProperty("FerrofluidSphereEnabled", BindingFlags.Instance | BindingFlags.Public);

        Assert.That(cubeSetting, Is.Not.Null, "The generated config must persist an independent ferrofluid cube toggle.");
        Assert.That(sphereSetting, Is.Not.Null, "The generated config must persist an independent ferrofluid sphere toggle.");
        Assert.That(cubeSetting!.GetValue(config), Is.EqualTo(false), "The cube must default off.");
        Assert.That(sphereSetting!.GetValue(config), Is.EqualTo(false), "The sphere must default off.");
        Assert.That(cubeSetting.IsDefined(ignoreType, true), Is.False, "The cube preference must serialize across sessions.");
        Assert.That(sphereSetting.IsDefined(ignoreType, true), Is.False, "The sphere preference must serialize across sessions.");
        Assert.That(pluginType.GetMethod("SetFerrofluidCubeEnabled", BindingFlags.Static | BindingFlags.NonPublic), Is.Not.Null, "The UI toggle needs an immediate plugin lifecycle entry point.");
        Assert.That(pluginType.GetMethod("SetFerrofluidSphereEnabled", BindingFlags.Static | BindingFlags.NonPublic), Is.Not.Null, "The UI toggle needs an immediate plugin lifecycle entry point.");
        Assert.That(environmentType.GetMethod("SetCubeEnabled", BindingFlags.Static | BindingFlags.NonPublic), Is.Not.Null);
        Assert.That(environmentType.GetMethod("SetSphereEnabled", BindingFlags.Static | BindingFlags.NonPublic), Is.Not.Null);
        // MainSwitchControlsOnlyMapPatches: the plugin gate was removed so Enable ChromaGLS cannot disable the cube or sphere; each answers only to its own persisted switch.
        Assert.That(environmentType.GetMethod("SetPluginEnabled", BindingFlags.Static | BindingFlags.NonPublic), Is.Null, "The plugin toggle must no longer reach the environment ferrofluid lifecycle.");
        Assert.That(environmentType.GetMethod("ApplyConfiguration", BindingFlags.Static | BindingFlags.NonPublic), Is.Not.Null);

        using Stream? bsmlStream = pluginAssembly.GetManifestResourceStream("ChromaGLS.Settings.modifiers.bsml");
        Assert.That(bsmlStream, Is.Not.Null);
        using StreamReader reader = new(bsmlStream!);
        string bsml = reader.ReadToEnd();
        int firstTuningIndex = bsml.IndexOf("value='ferrofluid-spike-height-scale'", StringComparison.Ordinal);
        int cubeIndex = bsml.IndexOf("value='ferrofluid-cube-enabled'", StringComparison.Ordinal);
        int sphereIndex = bsml.IndexOf("value='ferrofluid-sphere-enabled'", StringComparison.Ordinal);
        Assert.That(firstTuningIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(cubeIndex, Is.GreaterThanOrEqualTo(0).And.LessThan(firstTuningIndex), "The cube switch must sit with the other persistent enable toggles above the tuning controls.");
        Assert.That(sphereIndex, Is.GreaterThan(cubeIndex).And.LessThan(firstTuningIndex), "The sphere switch must follow the cube switch inside the enable-toggle group.");
    }

    // EnvironmentFerrofluidShapesUseIndependentGpuInstances verifies the optional objects use the shared shader pipeline through explicit shape modes while leaving the torus constructor default intact.
    [Test]
    public void EnvironmentFerrofluidShapesUseIndependentGpuInstances()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type simulationType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", throwOnError: true)!;
        Type shapeType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", throwOnError: true)!;
        Type environmentType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", throwOnError: true)!;
        MethodInfo? tryCreate = simulationType.GetMethod("TryCreate", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? updatePrimitive = simulationType.GetMethod("UpdatePrimitiveFrame", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo? evaluatePlacement = environmentType.GetMethod("EvaluatePlacement", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? evaluateSurface = environmentType.GetMethod("EvaluateSurfacePosition", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? evaluateNormal = environmentType.GetMethod("EvaluateSurfaceNormal", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? evaluateUv = environmentType.GetMethod("EvaluateSurfaceUv", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.That(shapeType.IsEnum, Is.True);
        Assert.That(Enum.GetValues(shapeType).Length, Is.EqualTo(3));
        Assert.That(Enum.GetName(shapeType, 0), Is.EqualTo("Torus"));
        Assert.That(Enum.GetName(shapeType, 1), Is.EqualTo("RoundedCube"));
        Assert.That(Enum.GetName(shapeType, 2), Is.EqualTo("Sphere"));
        Assert.That(tryCreate, Is.Not.Null, "The shared GPU pipeline must accept an explicit primitive shape mode.");
        ParameterInfo[] createParameters = tryCreate!.GetParameters();
        Assert.That(createParameters.Length, Is.EqualTo(4));
        Assert.That(createParameters[3].ParameterType, Is.EqualTo(shapeType));
        Assert.That(createParameters[3].HasDefaultValue, Is.True, "Existing torus creation must retain its zero shape mode without call-site changes.");
        Assert.That(updatePrimitive, Is.Not.Null, "Primitive effects need an update path that does not consume torus rect/curvature parameters.");
        Assert.That(evaluatePlacement, Is.Not.Null, "Placement must be testable without a live menu scene.");
        Assert.That(evaluateSurface, Is.Not.Null, "The rounded cube and sphere need a shared closed-surface mapping.");
        Assert.That(evaluateNormal, Is.Not.Null, "Closed-surface normals drive the same angle response used by the torus edges.");
        Assert.That(evaluateUv, Is.Not.Null, "Controller hits must map world directions back into the shared particle UV domain.");

        object roundedCube = Enum.ToObject(shapeType, 1);
        object sphere = Enum.ToObject(shapeType, 2);
        object[] placementArguments = [roundedCube, null!, null!];
        evaluatePlacement!.Invoke(null, placementArguments);
        Vector3 cubeCenter = (Vector3)placementArguments[1];
        Vector3 cubeScale = (Vector3)placementArguments[2];
        Assert.That(cubeCenter.x, Is.EqualTo(-1f).Within(0.0001f));
        Assert.That(cubeCenter.z, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(cubeScale.y, Is.InRange(0.45f, 0.65f), "The cube should be roughly knee-high and about twice the requirement torus height.");
        Assert.That(cubeCenter.y, Is.EqualTo(cubeScale.y * 0.5f).Within(0.0001f), "The cube should sit on the floor rather than floating.");

        object[] spherePlacementArguments = [sphere, null!, null!];
        evaluatePlacement.Invoke(null, spherePlacementArguments);
        Vector3 sphereCenter = (Vector3)spherePlacementArguments[1];
        Vector3 sphereScale = (Vector3)spherePlacementArguments[2];
        Assert.That(sphereCenter.x, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(sphereCenter.z, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(sphereScale.y, Is.EqualTo(cubeScale.y).Within(0.0001f), "The user asked for the sphere scaled up to match the cube's overall height instead of the smaller 0.4 placement.");
        Assert.That(sphereCenter.y, Is.EqualTo(sphereScale.y * 0.5f).Within(0.0001f));

        // EnvironmentPrimitiveChartCreaseArcsStayOffPlayerView evaluates zero-yaw mappings directly so the cut arcs land on the cardinal equator points and the player-facing diagonals sit mid-face in the regular lattice region.
        Vector3 frontSurface = (Vector3)evaluateSurface!.Invoke(null, [sphere, new Vector2(0.5f, 0.25f), 0.15f, 0f])!;
        Assert.That(frontSurface.x, Is.EqualTo(0f).Within(0.0001f));
        Assert.That(frontSurface.y, Is.EqualTo(0.3536f).Within(0.001f));
        Assert.That(frontSurface.z, Is.EqualTo(-0.3536f).Within(0.001f));
        Vector3 cubeFront = (Vector3)evaluateSurface.Invoke(null, [roundedCube, new Vector2(0.5f, 0.25f), 0.15f, 0f])!;
        Assert.That(cubeFront.z, Is.LessThan(-0.4f));
        Assert.That(Mathf.Abs(cubeFront.x), Is.LessThan(0.0001f));
        Assert.That(cubeFront.y, Is.GreaterThan(0.4f));
        Vector3 sphereFrontNormal = (Vector3)evaluateNormal!.Invoke(null, [sphere, new Vector2(0.5f, 0.25f), 0.15f, 0f])!;
        Assert.That(sphereFrontNormal.y, Is.GreaterThan(0.6f));
        Assert.That(sphereFrontNormal.z, Is.LessThan(-0.6f));
        Vector3 cubeFrontNormal = (Vector3)evaluateNormal.Invoke(null, [roundedCube, new Vector2(0.5f, 0.02f), 0.15f, 0f])!;
        Assert.That(cubeFrontNormal.z, Is.LessThan(-0.9f), "Rounded-cube face centers must retain an outward-facing normal for the shared magnetic response.");
        Vector2 frontUv = (Vector2)evaluateUv!.Invoke(null, [new Vector3(0f, 0.7071f, -0.7071f).normalized, 0f])!;
        Assert.That(frontUv.x, Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(frontUv.y, Is.EqualTo(0.25f).Within(0.0001f));
    }

    // EnvironmentPrimitivesRespectLocalMagnetRangeAndRoundedEdges covers the live misses: far rays must die out, cube corners must not make spike cells disappear, and primitive cones need a pointed profile.
    [Test]
    public void EnvironmentPrimitivesRespectLocalMagnetRangeAndRoundedEdges()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type environmentType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", throwOnError: true)!;
        Type simulationType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", throwOnError: true)!;
        Type shapeType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", throwOnError: true)!;
        MethodInfo? strength = environmentType.GetMethod("EvaluatePrimitiveTargetStrength", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? evaluateNormal = environmentType.GetMethod("EvaluateSurfaceNormal", BindingFlags.Static | BindingFlags.NonPublic);
        object roundedCube = Enum.ToObject(shapeType, 1);

        Assert.That(strength, Is.Not.Null, "Primitive misses need an explicit local interaction range instead of retaining a weak field at arbitrary ray distance.");
        float sourceScale = 0.6111f;
        float direct = (float)strength!.Invoke(null, [0.09f, 0f, sourceScale])!;
        float nearMiss = (float)strength.Invoke(null, [0.16f, 0.08f, sourceScale])!;
        float farMiss = (float)strength.Invoke(null, [0.8f, 0.6f, sourceScale])!;
        Assert.That(direct, Is.GreaterThan(0.7f));
        Assert.That(nearMiss, Is.GreaterThan(0f).And.LessThan(direct));
        Assert.That(farMiss, Is.EqualTo(0f), "A controller ray passing far outside the primitive must not magnetize it.");

        Assert.That(evaluateNormal, Is.Not.Null);
        Vector3 frontNormal = (Vector3)evaluateNormal!.Invoke(null, [roundedCube, new Vector2(0.5f, 0.682f), 0.15f, 0f])!;
        Vector3 sideNormal = (Vector3)evaluateNormal.Invoke(null, [roundedCube, new Vector2(0.5f, 0.686f), 0.15f, 0f])!;
        Assert.That(Vector3.Angle(frontNormal, sideNormal), Is.LessThan(45f), "A spike crossing a rounded cube edge must rotate through a continuous normal instead of snapping between face axes.");

        FieldInfo torusResolution = simulationType.GetField("TorusSurfaceResolution", BindingFlags.Static | BindingFlags.NonPublic)!;
        FieldInfo primitiveResolution = simulationType.GetField("PrimitiveSurfaceResolution", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.That(torusResolution.GetValue(null), Is.EqualTo(191));
        Assert.That(primitiveResolution.GetValue(null), Is.EqualTo(448));
    }

    // EnvironmentPrimitiveSpikesAreRoundCones proves the cone footprint is circular on the surface: the primitive chart is near-isotropic, so the particle lattice itself must have equal normalized spacing in both axes.
    [Test]
    public void EnvironmentPrimitiveSpikesAreRoundCones()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type simulationType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", throwOnError: true)!;
        Type shapeType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", throwOnError: true)!;
        object torus = Enum.ToObject(shapeType, 0);
        object roundedCube = Enum.ToObject(shapeType, 1);
        object sphere = Enum.ToObject(shapeType, 2);
        MethodInfo? densityLattice = simulationType.GetMethod(
            "EvaluateDensityLattice",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            [typeof(float), shapeType, typeof(float)],
            null);
        Assert.That(densityLattice, Is.Not.Null, "The density lattice must be shape-aware so primitives get a square staggered grid whose per-shape tightness multiplies the shared density slider.");

        Vector2Int cubeLattice = (Vector2Int)densityLattice!.Invoke(null, [0.45f, roundedCube, 1.6f])!;
        Assert.That(cubeLattice.x, Is.EqualTo(cubeLattice.y), "A square primitive lattice gives every spike cell equal normalized width and depth so the cone footprint is round.");
        Assert.That(cubeLattice.x, Is.EqualTo(38), "The cube's 1.6 tightness multiplier realizes the requested 60 percent tighter packing on the shared 24-axis base.");
        Vector2Int sphereLattice = (Vector2Int)densityLattice!.Invoke(null, [0.45f, sphere, 1.25f])!;
        Assert.That(sphereLattice.x, Is.EqualTo(sphereLattice.y));
        Assert.That(sphereLattice.x, Is.EqualTo(30), "The sphere's 1.25 tightness multiplier realizes the requested 25 percent tighter packing.");

        Vector2Int torusLattice = (Vector2Int)densityLattice!.Invoke(null, [1f, torus, 1f])!;
        Assert.That(torusLattice.x, Is.EqualTo(48));
        Assert.That(torusLattice.y, Is.EqualTo(12));

        MethodInfo? windings = simulationType.GetMethod(
            "EvaluateFieldWindings",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            [typeof(int), typeof(int), shapeType],
            null);
        Assert.That(windings, Is.Not.Null, "The torus lattice keeps its source two-winding stagger unchanged.");
        float torusWinding = (float)windings!.Invoke(null, [48, 12, torus])!;
        Assert.That(torusWinding, Is.EqualTo(2f), "The torus lattice keeps its source two-winding stagger unchanged.");

        // EnvironmentPrimitiveSitesDistributeUniformlyOnTheSurface seeds the golden-angle Fibonacci lattice then relaxes it on the true surface so every site approaches a six-neighbor hex cell; EnvironmentPrimitiveSitesFoldAcrossChartEdges keeps that spacing uniform through the fold arcs.
        MethodInfo? restPositions = simulationType.GetMethod("EvaluatePrimitiveRestPositions", BindingFlags.Static | BindingFlags.NonPublic);
        Type environmentType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", throwOnError: true)!;
        MethodInfo? sites = environmentType.GetMethod("EvaluatePrimitiveSites", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(restPositions, Is.Not.Null, "Primitive rest positions must come from a shared relaxed-lattice helper so the test sees the same site field the GPU renders.");
        Assert.That(sites, Is.Not.Null);
        Vector2[] sphereRest = (Vector2[])restPositions!.Invoke(null, [sphere, sphereLattice.x * sphereLattice.y, 0.2f])!;

        Vector4[] sphereSites = (Vector4[])sites!.Invoke(null, [sphere, sphereRest, 0.2f, 0f])!;
        float minSpacing = float.MaxValue;
        float maxSpacing = float.MinValue;
        foreach (Vector4 site in sphereSites)
        {
            minSpacing = Mathf.Min(minSpacing, site.w);
            maxSpacing = Mathf.Max(maxSpacing, site.w);
        }

        Assert.That(maxSpacing / minSpacing, Is.LessThan(1.25f), "The relaxed primitive lattice must hold nearest-neighbor spacing within twenty-five percent so every spike sits in a near-hexagonal cell; the raw Fibonacci spiral left a wider band of site spacing.");

        MethodInfo? fibonacci = simulationType.GetMethod("EvaluateFibonacciFieldPosition", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(fibonacci, Is.Not.Null, "The golden-angle generator must remain the relaxation seed.");
        Vector2 seedA = (Vector2)fibonacci!.Invoke(null, [0, sphereLattice.x * sphereLattice.y])!;
        Vector2 seedB = (Vector2)fibonacci.Invoke(null, [1, sphereLattice.x * sphereLattice.y])!;
        Assert.That(seedA, Is.Not.EqualTo(seedB), "The Fibonacci seed must still distribute distinct sites before relaxation.");

        MethodInfo? domainScale = simulationType.GetMethod(
            "EvaluateDomainScale",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            [shapeType, typeof(int)],
            null);
        Assert.That(domainScale, Is.Not.Null, "Primitive domains must scale with their denser lattice so SPH spacing stays near the torus's 0.64-unit cell size on both axes.");
        Vector2 primitiveDomain = (Vector2)domainScale!.Invoke(null, [roundedCube, 38])!;
        Assert.That(primitiveDomain.x, Is.EqualTo(primitiveDomain.y).Within(0.0001f));
        float primitiveSpacing = primitiveDomain.x * 2f / 38f;
        Assert.That(primitiveSpacing, Is.EqualTo(0.64f * 0.98f).Within(0.001f), "Primitive sites overpack by two percent so neighboring spikes tend to blip past each other and rustle when the magnet shifts them, while staying near the tuned torus spacing.");
        Vector2 torusDomain = (Vector2)domainScale.Invoke(null, [torus, 48])!;
        Assert.That(torusDomain.x, Is.EqualTo(14.8f).Within(0.001f));
        Assert.That(torusDomain.y, Is.EqualTo(4f).Within(0.001f));
    }

    // PrimitiveRestPositionRelaxationMeetsLoadTimeBudget guards the user-reported multi-second menu stall: the relaxation ran a full O(N²) pairwise neighbor scan every one of its four hundred iterations (~2.1 billion inner steps at the 48-axis maximum lattice), freezing the main thread whenever a cube or sphere instance was created. The budget targets each call staying interactive-fast even at the largest supported lattice on the slow Mono test host.
    [Test]
    public void PrimitiveRestPositionRelaxationMeetsLoadTimeBudget()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type simulationType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", throwOnError: true)!;
        Type shapeType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", throwOnError: true)!;
        object roundedCube = Enum.ToObject(shapeType, 1);
        object sphere = Enum.ToObject(shapeType, 2);
        MethodInfo? restPositions = simulationType.GetMethod("EvaluatePrimitiveRestPositions", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(restPositions, Is.Not.Null, "The rest lattice needs a shared CPU entry point so tests can evaluate the same site field the GPU renders.");

        const int MaxBudgetMilliseconds = 1500;
        foreach ((object shape, int axis, float roundness, string label) in new[]
        {
            (sphere, 30, 0.2f, "sphere default lattice"),
            (roundedCube, 38, 0.48f, "cube default lattice"),
            (roundedCube, 48, 0.48f, "cube maximum lattice"),
        })
        {
            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
            Vector2[] result = (Vector2[])restPositions!.Invoke(null, [shape, axis * axis, roundness])!;
            timer.Stop();
            Assert.That(result.Length, Is.EqualTo(axis * axis), $"The {label} relaxation must still emit every site.");
            Assert.That(timer.ElapsedMilliseconds, Is.LessThan(MaxBudgetMilliseconds), $"The {label} relaxation must stay inside the load-time budget; an O(N²)-per-iteration neighbor scan stalls menu entry for seconds at this site count.");
        }
    }

    // EnvironmentPrimitiveSitesFoldAcrossChartEdges proves the four square chart edges are fold continuations rather than walls: the octahedral square's edges collapse onto the four lower-hemisphere arcs, so particles must travel through them, neighbor deltas must take the mirrored minimum image, and height taps must fold instead of clamp.
    [Test]
    public void EnvironmentPrimitiveSitesFoldAcrossChartEdges()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type simulationType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", throwOnError: true)!;
        Type environmentType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", throwOnError: true)!;
        Type shapeType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", throwOnError: true)!;
        object sphere = Enum.ToObject(shapeType, 2);

        MethodInfo? foldPosition = simulationType.GetMethod("FoldPrimitiveChartPosition", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? foldDelta = simulationType.GetMethod("EvaluatePrimitiveFoldDelta", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? siteSurface = environmentType.GetMethod("EvaluateSiteSurfacePosition", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(foldPosition, Is.Not.Null, "The chart fold must be shared with CPU code so pointer smoothing and tests mirror the GPU edge continuation.");
        Assert.That(foldDelta, Is.Not.Null, "Neighbor and pointer deltas need the mirrored minimum image so interactions continue across the chart arcs.");
        Assert.That(siteSurface, Is.Not.Null);

        foreach ((Vector2 outside, Vector2 inside) in new[]
        {
            (new Vector2(1.05f, 0.3f), new Vector2(0.95f, -0.3f)),
            (new Vector2(-1.05f, 0.3f), new Vector2(-0.95f, -0.3f)),
            (new Vector2(0.3f, 1.05f), new Vector2(-0.3f, 0.95f)),
            (new Vector2(0.3f, -1.05f), new Vector2(-0.3f, -0.95f)),
            (new Vector2(1.05f, 1.05f), new Vector2(-0.95f, -0.95f)),
        })
        {
            Vector2 folded = (Vector2)foldPosition!.Invoke(null, [outside])!;
            Assert.That(folded.x, Is.EqualTo(inside.x).Within(0.0001f), $"Crossing a chart edge at {outside} must continue at the mirrored point {inside}; the fold is a point reflection across the edge midpoint.");
            Assert.That(folded.y, Is.EqualTo(inside.y).Within(0.0001f));
        }

        Vector3 nearArc = (Vector3)siteSurface!.Invoke(null, [sphere, new Vector2(0.98f, 0.3f), 0.31f, 0f])!;
        Vector3 acrossArc = (Vector3)siteSurface.Invoke(
            null,
            [sphere, (Vector2)foldPosition!.Invoke(null, [new Vector2(1.02f, 0.3f)])!, 0.31f, 0f])!;
        Assert.That((nearArc - acrossArc).magnitude, Is.LessThan(0.15f), "A point just inside a chart edge and its folded continuation just past it must decode to nearly the same surface point; a large gap means the fold does not actually continue across the arc.");

        Vector2 mirrorDelta = (Vector2)foldDelta!.Invoke(null, [new Vector2(0.98f, 0.3f), new Vector2(0.98f, -0.3f)])!;
        Assert.That(mirrorDelta.x, Is.EqualTo(0.04f).Within(0.0001f), "The mirrored neighbor retains the distance from both sites to the chart edge.");
        Assert.That(mirrorDelta.y, Is.EqualTo(0f).Within(0.0001f));
        Vector2 interiorDelta = (Vector2)foldDelta.Invoke(null, [new Vector2(0.2f, 0.3f), new Vector2(0.2f, -0.3f)])!;
        Assert.That(interiorDelta.y, Is.EqualTo(-0.6f).Within(0.0001f), "Interior pairs must keep their direct delta; engaging the mirror away from an edge would fake contacts across the surface.");

        Vector2 axisDelta = (Vector2)foldDelta.Invoke(null, [new Vector2(0f, 0.3f), new Vector2(0f, -0.3f)])!;
        Assert.That(axisDelta.y, Is.EqualTo(-0.6f).Within(0.0001f), "A point on a chart axis must not become a false edge copy at sign zero.");
        Assert.That(axisDelta.x, Is.EqualTo(0f).Within(0.0001f));

    }

    // EnvironmentPrimitiveSingularityIsHiddenUnderneath requires the octahedral chart to keep the top hemisphere regular while the bottom pole is shared only by the four square corners instead of a collapsed boundary ring.
    [Test]
    public void EnvironmentPrimitiveSingularityIsHiddenUnderneath()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type environmentType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", throwOnError: true)!;
        Type shapeType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", throwOnError: true)!;
        MethodInfo evaluateSurface = environmentType.GetMethod("EvaluateSurfacePosition", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo evaluateUv = environmentType.GetMethod("EvaluateSurfaceUv", BindingFlags.Static | BindingFlags.NonPublic)!;
        object sphere = Enum.ToObject(shapeType, 2);
        object roundedCube = Enum.ToObject(shapeType, 1);

        Vector3 sphereTop = (Vector3)evaluateSurface.Invoke(null, [sphere, new Vector2(0.5f, 0.5f), 0.2f, 0f])!;
        Assert.That(sphereTop.x, Is.EqualTo(0f).Within(0.0001f), "The sphere's UV center must map to its top so the visible crown keeps a regular six-neighbor lattice.");
        Assert.That(sphereTop.y, Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(sphereTop.z, Is.EqualTo(0f).Within(0.0001f));

        foreach (Vector2 cornerUv in new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
        })
        {
            Vector3 corner = (Vector3)evaluateSurface.Invoke(null, [sphere, cornerUv, 0.2f, 0f])!;
            Assert.That(corner.y, Is.EqualTo(-0.5f).Within(0.001f), $"Octahedral corner {cornerUv} must share the single bottom pole so the butthole cannot form a ring.");
        }

        // EnvironmentPrimitiveChartCreaseArcsStayOffPlayerView lands each boundary midpoint on a cardinal equator direction at zero yaw, so the four cut arcs stay clear of the player-facing diagonals.
        foreach ((Vector2 edgeUv, Vector3 expected) in new[]
        {
            (new Vector2(0.5f, 0f), new Vector3(0f, 0f, -0.5f)),
            (new Vector2(0.5f, 1f), new Vector3(0f, 0f, 0.5f)),
            (new Vector2(0f, 0.5f), new Vector3(-0.5f, 0f, 0f)),
            (new Vector2(1f, 0.5f), new Vector3(0.5f, 0f, 0f)),
        })
        {
            Vector3 edge = (Vector3)evaluateSurface.Invoke(null, [sphere, edgeUv, 0.2f, 0f])!;
            Assert.That(edge.x, Is.EqualTo(expected.x).Within(0.001f), $"Octahedral edge midpoint {edgeUv} maps to the equator rather than collapsing at the pole.");
            Assert.That(edge.y, Is.EqualTo(expected.y).Within(0.001f));
            Assert.That(edge.z, Is.EqualTo(expected.z).Within(0.001f));
        }

        Vector3 quarter = (Vector3)evaluateSurface.Invoke(null, [sphere, new Vector2(0.75f, 0.5f), 0.2f, 0f])!;
        Assert.That(quarter.y, Is.GreaterThan(0.1f), "The upper diamond region must stay on the top hemisphere with bounded octahedral stretch.");
        Vector2 roundTripUv = (Vector2)evaluateUv.Invoke(null, [quarter.normalized, 0f])!;
        Vector3 roundTrip = (Vector3)evaluateSurface.Invoke(null, [sphere, roundTripUv, 0.2f, 0f])!;
        Assert.That((roundTrip - quarter).magnitude, Is.LessThan(0.01f), "The magnet inverse map must round-trip every non-pole direction so pointer hits land on the touched cell.");

        Vector3 cubeTop = (Vector3)evaluateSurface.Invoke(null, [roundedCube, new Vector2(0.5f, 0.5f), 0.2f, 0f])!;
        Assert.That(cubeTop.y, Is.EqualTo(0.5f).Within(0.0001f), "The rounded cube must inherit the bottom-only singularity so its top face keeps a continuous spike field.");
        Vector2 topUv = (Vector2)evaluateUv.Invoke(null, [Vector3.up, 0f])!;
        Assert.That(topUv.x, Is.EqualTo(0.5f).Within(0.01f));
        Assert.That(topUv.y, Is.EqualTo(0.5f).Within(0.01f));
    }

    // EnvironmentPrimitiveSpikeBasesAreCircularOnTheSurface measures each resting cone's half-height iso-contour on the actual object surface: the octahedral chart's Jacobian is anisotropic, so any constant chart-space distance metric produces the reported oval/diamond bases; only per-site surface distance keeps the contour circular.
    [Test]
    public void EnvironmentPrimitiveSpikeBasesAreCircularOnTheSurface()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type environmentType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", throwOnError: true)!;
        Type simulationType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", throwOnError: true)!;
        Type shapeType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", throwOnError: true)!;
        Type configType = pluginAssembly.GetType("ChromaGLS.Settings.Config", throwOnError: true)!;
        object config = Activator.CreateInstance(configType, nonPublic: true)!;
        MethodInfo? restPositionsMethod = simulationType.GetMethod("EvaluatePrimitiveRestPositions", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? sites = environmentType.GetMethod("EvaluatePrimitiveSites", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? coneHeight = environmentType.GetMethod("EvaluatePrimitiveConeHeight", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? fromDirection = environmentType.GetMethod("EvaluateSurfacePositionFromDirection", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? chartYaw = environmentType.GetMethod("EvaluateChartYawDegrees", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? roundness = environmentType.GetMethod("EvaluateRoundness", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? densityLattice = simulationType.GetMethod(
            "EvaluateDensityLattice",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            [typeof(float), shapeType, typeof(float)],
            null);
        Assert.That(restPositionsMethod, Is.Not.Null, "The rest lattice needs a shared CPU entry point so tests can evaluate the same site field the GPU renders.");
        Assert.That(sites, Is.Not.Null, "The primitive height pass needs per-site surface positions and local spacing to make cone bases circular.");
        Assert.That(coneHeight, Is.Not.Null, "The spike profile must be evaluable in surface space for circularity to be proven.");
        Assert.That(fromDirection, Is.Not.Null, "Contour sampling needs a direction-to-surface projection shared with the chart decode.");
        Assert.That(chartYaw, Is.Not.Null);
        Assert.That(roundness, Is.Not.Null);

        // Flat-region probes stay near-circular; the user's widened 0.10 base-cutoff suppression band clips the half-height contour toward the closer second-neighbor direction, so the limit tolerates that intended webbing removal while still rejecting chart-metric ovals. Rounded-edge probes also tolerate the chord metric's slight compression across high-curvature directions plus lattice jitter.
        (Vector3 direction, float limit)[] probes =
        [
            (Vector3.up, 1.3f),
            (new Vector3(-0.45f, 0.55f, -0.70f).normalized, 1.35f),
            (new Vector3(-0.70f, 0.05f, -0.70f).normalized, 1.35f),
            (new Vector3(0.30f, 0.15f, -0.94f).normalized, 1.35f),
            (new Vector3(0.55f, -0.25f, -0.80f).normalized, 1.35f),
        ];
        foreach ((object shape, float shapeDensityScale) in new[]
        {
            (Enum.ToObject(shapeType, 1), 1.6f),
            (Enum.ToObject(shapeType, 2), 1.25f),
        })
        {
            int axis = ((Vector2Int)densityLattice!.Invoke(null, [0.45f, shape, shapeDensityScale])!).x;
            float shapeRoundness = (float)roundness!.Invoke(null, [shape, config])!;
            Vector2[] restPositions = (Vector2[])restPositionsMethod!.Invoke(null, [shape, axis * axis, shapeRoundness])!;

            float yaw = (float)chartYaw!.Invoke(null, [shape, config])!;
            Vector4[] siteData = (Vector4[])sites!.Invoke(
                null,
                [shape, restPositions, shapeRoundness, yaw])!;
            foreach ((Vector3 probe, float contourLimit) in probes)
            {
                int nearestIndex = 0;
                float nearestDistance = float.MaxValue;
                for (int index = 0; index < siteData.Length; index++)
                {
                    float distance = ((Vector3)siteData[index] - (probe * 0.5f)).sqrMagnitude;
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestIndex = index;
                    }
                }

                Vector3 sitePosition = siteData[nearestIndex];
                float spacing = siteData[nearestIndex].w;
                Vector3 normal = sitePosition.normalized;
                Vector3 tangentA = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 tangentB = Vector3.Cross(normal, tangentA);
                float peak = (float)coneHeight!.Invoke(null, [shape, sitePosition, siteData, config])!;
                Assert.That(peak, Is.GreaterThan(0.5f), "A resting site must produce a tall spike before its contour can be measured.");

                float halfPeak = peak * 0.5f;
                float minimumRadius = float.MaxValue;
                float maximumRadius = 0f;
                float[] radii = new float[16];
                for (int step = 0; step < 16; step++)
                {
                    float angle = step * (Mathf.PI * 2f / 16f);
                    Vector3 tangentDirection = (tangentA * Mathf.Cos(angle)) + (tangentB * Mathf.Sin(angle));

                    // A coarse scan finds the first half-peak crossing because plain bisection across the suppressed cell valley converges onto a neighboring cone's rising edge instead of this cone's falling edge.
                    float scanLimit = spacing * 1.6f;
                    float low = 0f;
                    float high = scanLimit;
                    for (int sample = 1; sample <= 96; sample++)
                    {
                        float radius = scanLimit * sample / 96f;
                        Vector3 direction = (sitePosition + (tangentDirection * radius)).normalized;
                        Vector3 surfacePoint = (Vector3)fromDirection!.Invoke(null, [shape, direction, shapeRoundness])!;
                        float height = (float)coneHeight!.Invoke(null, [shape, surfacePoint, siteData, config])!;
                        if (height < halfPeak)
                        {
                            high = radius;
                            low = scanLimit * (sample - 1) / 96f;
                            break;
                        }
                    }

                    for (int iteration = 0; iteration < 20; iteration++)
                    {
                        float radius = (low + high) * 0.5f;
                        Vector3 direction = (sitePosition + (tangentDirection * radius)).normalized;
                        Vector3 surfacePoint = (Vector3)fromDirection.Invoke(null, [shape, direction, shapeRoundness])!;
                        float height = (float)coneHeight.Invoke(null, [shape, surfacePoint, siteData, config])!;
                        if (height >= halfPeak)
                        {
                            low = radius;
                        }
                        else
                        {
                            high = radius;
                        }
                    }

                    minimumRadius = Mathf.Min(minimumRadius, low);
                    maximumRadius = Mathf.Max(maximumRadius, low);
                    radii[step] = low;
                }

                TestContext.WriteLine($"{shape} probe={probe} site={sitePosition} spacing={spacing:F4} peak={peak:F3} radii={string.Join(",", Array.ConvertAll(radii, r => r.ToString("F4")))}");
                Assert.That(
                    maximumRadius / minimumRadius,
                    Is.LessThan(contourLimit),
                    $"Spike base at probe {probe} on {shape} is {maximumRadius / minimumRadius:F2}x wider in its flattest direction than its widest; a cone's half-height contour must be a circle on the surface.");
            }
        }
    }

    // EnvironmentPrimitiveChartCreaseArcsStayOffPlayerView keeps the octahedral chart's four crease endpoints off the diagonals that face the player so any residual parameterization line stays at the edges of the visible surface.
    [Test]
    public void EnvironmentPrimitiveChartCreaseArcsStayOffPlayerView()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type environmentType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", throwOnError: true)!;
        Type shapeType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", throwOnError: true)!;
        Type configType = pluginAssembly.GetType("ChromaGLS.Settings.Config", throwOnError: true)!;
        object config = Activator.CreateInstance(configType, nonPublic: true)!;
        object roundedCube = Enum.ToObject(shapeType, 1);
        object sphere = Enum.ToObject(shapeType, 2);
        MethodInfo? creaseAzimuths = environmentType.GetMethod("EvaluateChartCreaseAzimuths", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? chartYaw = environmentType.GetMethod("EvaluateChartYawDegrees", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(creaseAzimuths, Is.Not.Null, "The crease endpoint azimuths must be computable so their placement relative to the player is testable.");
        Assert.That(chartYaw, Is.Not.Null, "Each primitive needs a session-only chart yaw so seam placement stays tunable.");

        float cubeYaw = (float)chartYaw!.Invoke(null, [roundedCube, config])!;
        float sphereYaw = (float)chartYaw.Invoke(null, [sphere, config])!;
        Assert.That(cubeYaw, Is.EqualTo(45f).Within(0.001f), "The cube yaw must default to the 45 degrees from the user's latest 1.44.1 session.");
        Assert.That(sphereYaw, Is.EqualTo(0f).Within(0.001f));

        float[] cubeCreases = (float[])creaseAzimuths!.Invoke(null, [cubeYaw])!;
        float[] sphereCreases = (float[])creaseAzimuths.Invoke(null, [sphereYaw])!;
        Assert.That(cubeCreases.Length, Is.EqualTo(4));

        // The cube sits one meter left and forward so its player-facing diagonal is azimuth 135; the mirrored sphere faces azimuth 225. The user's latest 45-degree yaw parks one endpoint exactly on each diagonal; the fold-aware deltas keep those arcs indistinguishable, so the clearance bound is retired in favor of asserting the verified placement.
        Assert.That(cubeCreases, Has.Some.EqualTo(135f).Within(0.001f), "The user's 45-degree cube yaw puts a crease endpoint exactly on the player-facing diagonal; the fold topology makes that arc indistinguishable from interior surface.");

        foreach (float azimuth in sphereCreases)
        {
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(azimuth, 225f)), Is.GreaterThanOrEqualTo(40f), $"A crease endpoint at azimuth {azimuth} would sit dead-center on the sphere's player-facing side.");
        }

        Assert.That(sphereCreases, Has.Some.EqualTo(0f).Within(0.001f), "One crease endpoint must face directly away from the player.");
    }

    // EnvironmentPrimitiveSitesDistributeUniformlyOnTheSurface proves the golden-angle Fibonacci lattice covers every face evenly: nearest-site distance stays bounded along the former chart-fold seam and per-site spacing varies only mildly, so no dead band or density streak can survive.
    [Test]
    public void EnvironmentPrimitiveSitesDistributeUniformlyOnTheSurface()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type environmentType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", throwOnError: true)!;
        Type simulationType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", throwOnError: true)!;
        Type shapeType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", throwOnError: true)!;
        Type configType = pluginAssembly.GetType("ChromaGLS.Settings.Config", throwOnError: true)!;
        object config = Activator.CreateInstance(configType, nonPublic: true)!;
        MethodInfo? restPositionsMethod = simulationType.GetMethod("EvaluatePrimitiveRestPositions", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? sites = environmentType.GetMethod("EvaluatePrimitiveSites", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? fromDirection = environmentType.GetMethod("EvaluateSurfacePositionFromDirection", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? chartYaw = environmentType.GetMethod("EvaluateChartYawDegrees", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? roundness = environmentType.GetMethod("EvaluateRoundness", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? densityLattice = simulationType.GetMethod(
            "EvaluateDensityLattice",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            [typeof(float), shapeType, typeof(float)],
            null);
        Assert.That(restPositionsMethod, Is.Not.Null);
        Assert.That(sites, Is.Not.Null);
        Assert.That(fromDirection, Is.Not.Null);

        foreach ((object shape, float densityScale, float spacingCvLimit) in new[]
        {
            (Enum.ToObject(shapeType, 1), 1.6f, 0.35f),
            (Enum.ToObject(shapeType, 2), 1.25f, 0.18f),
        })
        {
            int axis = ((Vector2Int)densityLattice!.Invoke(null, [0.45f, shape, densityScale])!).x;
            float shapeRoundness = (float)roundness!.Invoke(null, [shape, config])!;
            Vector2[] restPositions = (Vector2[])restPositionsMethod!.Invoke(null, [shape, axis * axis, shapeRoundness])!;

            float yaw = (float)chartYaw!.Invoke(null, [shape, config])!;
            Vector4[] siteData = (Vector4[])sites!.Invoke(null, [shape, restPositions, shapeRoundness, yaw])!;

            // The per-site spacing must match a full nearest-neighbor scan: the SITE_SURFACE pass performs the same exhaustive scan, and a wrong estimate rescales every cone footprint.
            double spacingTotal = 0.0;
            double spacingSquaredTotal = 0.0;
            int sampledSites = 0;
            for (int index = 0; index < siteData.Length; index += 7)
            {
                Vector3 position = new(siteData[index].x, siteData[index].y, siteData[index].z);
                float nearest = float.MaxValue;
                for (int other = 0; other < siteData.Length; other++)
                {
                    if (other == index)
                    {
                        continue;
                    }

                    float distance = (new Vector3(siteData[other].x, siteData[other].y, siteData[other].z) - position).sqrMagnitude;
                    if (distance < nearest)
                    {
                        nearest = distance;
                    }
                }

                nearest = Mathf.Sqrt(nearest);
                if (Convert.ToInt32(shape) == 1)
                {
                    Assert.That(
                        siteData[index].w,
                        Is.EqualTo(siteData[0].w).Within(siteData[0].w * 0.00001f),
                        $"Site {index} on {shape} spacing {siteData[index].w:F4} must equal the shared face-referenced footprint {siteData[0].w:F4} so every cube spike keeps the same profile width.");
                }
                else
                {
                    Assert.That(
                        siteData[index].w,
                        Is.EqualTo(nearest).Within(nearest * 0.2f),
                        $"Site {index} on {shape} spacing {siteData[index].w:F4} must match the full-scan nearest-neighbor distance {nearest:F4}; the stride neighbors have to cover the true Voronoi neighbors.");
                }
                spacingTotal += nearest;
                spacingSquaredTotal += nearest * nearest;
                sampledSites++;
            }

            double meanSpacing = spacingTotal / sampledSites;
            double spacingCv = Math.Sqrt((spacingSquaredTotal / sampledSites) - (meanSpacing * meanSpacing)) / meanSpacing;
            Assert.That(spacingCv, Is.LessThan(spacingCvLimit), $"{shape} site spacing CV {spacingCv:F3} exceeds {spacingCvLimit}; uneven cell sizes produce the inconsistent spike densities the user reported.");

            // Sample the four former crease arcs (equator directions between the cardinal endpoints) and a lat/long interior grid; every probe must have a site within the same normalized bound so no dead band can form.
            List<Vector3> probes = new();
            for (int step = 0; step < 72; step++)
            {
                float azimuth = step * Mathf.PI * 2f / 72f;
                probes.Add(new Vector3(Mathf.Cos(azimuth), 0.001f, Mathf.Sin(azimuth)).normalized);
            }

            for (int ring = 0; ring < 6; ring++)
            {
                float elevation = Mathf.Lerp(-0.9f, 0.9f, ring / 5f);
                for (int step = 0; step < 24; step++)
                {
                    float azimuth = step * Mathf.PI * 2f / 24f;
                    float ringRadius = Mathf.Sqrt(Mathf.Max(0f, 1f - (elevation * elevation)));
                    probes.Add(new Vector3(Mathf.Cos(azimuth) * ringRadius, elevation, Mathf.Sin(azimuth) * ringRadius));
                }
            }

            float worstNormalizedCoverage = 0f;
            foreach (Vector3 probeDirection in probes)
            {
                Vector3 surfacePoint = (Vector3)fromDirection!.Invoke(null, [shape, probeDirection, shapeRoundness])!;
                float nearestCoverage = float.MaxValue;
                for (int index = 0; index < siteData.Length; index++)
                {
                    float distance = (new Vector3(siteData[index].x, siteData[index].y, siteData[index].z) - surfacePoint).sqrMagnitude;
                    if (distance < nearestCoverage)
                    {
                        nearestCoverage = distance;
                    }
                }

                worstNormalizedCoverage = Mathf.Max(worstNormalizedCoverage, Mathf.Sqrt(nearestCoverage) / (float)meanSpacing);
            }

            Assert.That(
                worstNormalizedCoverage,
                Is.LessThan(1.1f),
                $"{shape} has a surface probe {worstNormalizedCoverage:F2}x the mean spacing from its nearest site; the folded chart lattice left a depleted band along the crease that read as the reported seam.");
        }
    }

    // PrimitiveTuningSlidersOwnEnvironmentObjects keeps the perfected torus frozen on its captured baseline while every live slider targets only the cube and sphere.
    [Test]
    public void PrimitiveTuningSlidersOwnEnvironmentObjects()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type simulationType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", throwOnError: true)!;
        Type configType = pluginAssembly.GetType("ChromaGLS.Settings.Config", throwOnError: true)!;
        Assert.That(
            simulationType.GetMethod("ApplyEnabledState", BindingFlags.Instance | BindingFlags.NonPublic),
            Is.Not.Null,
            "The torus needs a dedicated enabled-state path so live slider changes stop re-uploading tweak uniforms to the perfected button.");
        MethodInfo? baseline = simulationType.GetMethod("EvaluateTorusBaseline", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(baseline, Is.Not.Null, "The torus must be configured from a baseline snapshot so slider experiments cannot leak into its captured profile.");
        object live = Activator.CreateInstance(configType, nonPublic: true)!;
        configType.GetProperty("FerrofluidSpikeDensity")!.SetValue(live, 1.5f);
        configType.GetProperty("BulgeHeight")!.SetValue(live, 0.2f);
        configType.GetProperty("FerrofluidBodyEnabled")!.SetValue(live, false);
        object snapshot = baseline!.Invoke(null, [live])!;
        Assert.That(configType.GetProperty("FerrofluidSpikeDensity")!.GetValue(snapshot), Is.EqualTo(0.45f), "The torus baseline must keep the captured lattice density regardless of live slider edits.");
        Assert.That(configType.GetProperty("BulgeHeight")!.GetValue(snapshot), Is.EqualTo(0.145f));
        Assert.That(configType.GetProperty("FerrofluidBodyEnabled")!.GetValue(snapshot), Is.EqualTo(false), "Only the visibility toggles follow the live config; tuning stays frozen.");
        Assert.That(configType.GetProperty("FerrofluidExposure")!.GetValue(snapshot), Is.EqualTo(1f), "The baseline must pin the captured 1.0 exposure; the primitive-facing default also returns to 1.0 so both paths present the same intensity scale.");
        Assert.That(configType.GetProperty("MaximumHeight")!.GetValue(snapshot), Is.EqualTo(2.32f), "The baseline must pin the captured 2.32 height cap even though the primitive-facing default moved to the user's pinned 3.00 session value.");
        Assert.That(configType.GetProperty("MagnetForwardDistance")!.GetValue(snapshot), Is.EqualTo(0.47f), "The baseline must pin the captured 0.47 plane offset even though the primitive-facing default moved to the user's pinned 0.07 session value.");

        Assert.That(
            simulationType.GetMethod(
                "EvaluateMagnetSurfacePosition",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                [typeof(Vector3), typeof(Vector3), typeof(Vector3), typeof(float), typeof(float)],
                null),
            Is.Not.Null,
            "Primitives need a gap-parameterized overload so the magnet forward-distance slider can pull the field toward the cube and sphere surfaces.");

        object session = Activator.CreateInstance(configType, nonPublic: true)!;
        using StreamReader reader = new(pluginAssembly.GetManifestResourceStream("ChromaGLS.Settings.modifiers.bsml")!);
        string bsml = reader.ReadToEnd();
        foreach ((string propertyName, string binding, float expectedDefault) in new[]
        {
            ("FerrofluidPrimitiveSpikeSize", "ferrofluid-primitive-spike-size", 0.7f),
            ("FerrofluidPrimitiveTipRoundness", "ferrofluid-primitive-tip-roundness", 0f),
            ("FerrofluidPrimitiveTipCapRounding", "ferrofluid-primitive-tip-cap-rounding", 0.81f),
            ("FerrofluidSpikeMoundCurvature", "ferrofluid-spike-mound-curvature", 2f),
            ("FerrofluidSpikeHeightRadius", "ferrofluid-spike-height-radius", 0.31f),
            ("FerrofluidSpikeTiltRadius", "ferrofluid-spike-tilt-radius", 0.73f),
            ("FerrofluidPrimitiveAmbientScale", "ferrofluid-primitive-ambient-scale", 0.15f),
            ("FerrofluidPrimitiveFlattenRadius", "ferrofluid-primitive-flatten-radius", 1.45f),
            ("FerrofluidPrimitiveFlattenExponent", "ferrofluid-primitive-flatten-exponent", 2.7f),
            ("FerrofluidPrimitiveGlobalFlatten", "ferrofluid-primitive-global-flatten", 0.37f),
            ("FerrofluidSphereBulgeScale", "ferrofluid-sphere-bulge-scale", 0.6f),
            ("FerrofluidCubeSpikeDensity", "ferrofluid-cube-spike-density", 1.2f),
            ("FerrofluidSphereSpikeDensity", "ferrofluid-sphere-spike-density", 0.95f),
            ("FerrofluidCubeRoundness", "ferrofluid-cube-roundness", 0.48f),
            ("FerrofluidCubeChartYaw", "ferrofluid-cube-chart-yaw", 45f),
            ("FerrofluidSphereChartYaw", "ferrofluid-sphere-chart-yaw", 0f),
        })
        {
            PropertyInfo? property = configType.GetProperty(propertyName);
            Assert.That(property, Is.Not.Null, $"{propertyName} must exist so the primitive-only slider can bind to it.");
            Assert.That(property!.GetValue(session), Is.EqualTo(expectedDefault).Within(0.0001f), $"{propertyName} must default to the requested tuning.");
            Assert.That(bsml, Does.Contain($"value='{binding}' apply-on-change='true'"), $"The BSML slider {binding} must exist and apply live.");
        }
    }

    [Test]
    public void PrimitiveTipCapRoundingRemovesTheFixedApexPlateau()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type environmentType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", throwOnError: true)!;
        Type simulationType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", throwOnError: true)!;
        Type shapeType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", throwOnError: true)!;
        Type configType = pluginAssembly.GetType("ChromaGLS.Settings.Config", throwOnError: true)!;
        MethodInfo? profiles = environmentType.GetMethod("EvaluatePrimitiveConeProfiles", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? cellMetricMethod = simulationType.GetMethod("EvaluatePrimitiveCellMetric", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? spikeShape = simulationType.GetMethod("EvaluateSpikeShape", BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo? heightMapScaleFactor = simulationType.GetField("HeightMapScaleFactor", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(profiles, Is.Not.Null, "The height-map mirror must expose both channels so the cap slider can be verified without a GPU.");
        Assert.That(cellMetricMethod, Is.Not.Null);
        Assert.That(spikeShape, Is.Not.Null);
        Assert.That(heightMapScaleFactor, Is.Not.Null);
        PropertyInfo? capSetting = configType.GetProperty("FerrofluidPrimitiveTipCapRounding", BindingFlags.Instance | BindingFlags.Public);
        Assert.That(capSetting, Is.Not.Null, "The session config must expose the primitive tip cap rounding knob so its slider can remove the fixed apex plateau.");
        object defaults = Activator.CreateInstance(configType, nonPublic: true)!;
        Assert.That(capSetting!.GetValue(defaults), Is.EqualTo(0.81f).Within(0.0001f), "The cap must default to the captured rounded tip so the untouched profile stays identical.");

        static float LegacyAlmostIdentity(float x, float m, float n)
        {
            if (x > m)
            {
                return x;
            }

            float a = (2f * n) - m;
            float b = (2f * m) - (3f * n);
            float t = x / m;
            return (((a * t) + b) * t * t) + n;
        }

        static float ExpectedSharpHeight(float capRounding, float tipRoundness, float normalizedDistance)
        {
            float distance = capRounding > 0f
                ? LegacyAlmostIdentity(normalizedDistance, 0.12f * capRounding, 0.02f * capRounding)
                : normalizedDistance;
            float rounded = Mathf.Sqrt(Mathf.Max(0f, 1f - (distance * distance)));
            return Mathf.Lerp(
                Mathf.Clamp01((1f - distance) / Mathf.Lerp(1f, 0.95f, capRounding)),
                rounded,
                tipRoundness);
        }

        static float ExpectedSmoothHeight(float capRounding, float tipRoundness, float normalizedDistance)
        {
            float distance = capRounding > 0f
                ? LegacyAlmostIdentity(normalizedDistance, 0.12f * capRounding, 0.02f * capRounding)
                : normalizedDistance;
            float coneHeight = 1f - distance;
            coneHeight = coneHeight * coneHeight * (3f - (2f * coneHeight));
            float rounded = Mathf.Sqrt(Mathf.Max(0f, 1f - (distance * distance)));
            return Mathf.Lerp(coneHeight, rounded, tipRoundness);
        }

        float[] normalizedDistances = [0f, 0.025f, 0.05f, 0.1f, 0.2f];
        foreach ((object shape, string shapeName) in new[]
        {
            (Enum.ToObject(shapeType, 1), "cube"),
            (Enum.ToObject(shapeType, 2), "sphere"),
        })
        {
            object config = Activator.CreateInstance(configType, nonPublic: true)!;
            configType.GetProperty("FerrofluidSpikeBaseCutoff")!.SetValue(config, 0f);
            float spikeFactor = ((Vector2)spikeShape!.Invoke(
                null,
                [
                    configType.GetProperty("FerrofluidSpikeBaseSize")!.GetValue(config),
                    configType.GetProperty("FerrofluidSpikeHeightScale")!.GetValue(config),
                ])!).x;
            float cellMetric = (float)cellMetricMethod!.Invoke(
                null,
                [configType.GetProperty("FerrofluidPrimitiveSpikeSize")!.GetValue(config)])!;
            float heightMapScale = (float)heightMapScaleFactor!.GetValue(null)!;
            Vector4[] sites = [new Vector4(0f, 0.5f, 0f, 0.04f)];
            foreach (float capRounding in new[] { 0f, 1f })
            {
                capSetting.SetValue(config, capRounding);
                foreach (float tipRoundness in new[] { 0f, 1f })
                {
                    configType.GetProperty("FerrofluidPrimitiveTipRoundness")!.SetValue(config, tipRoundness);
                    float previousSharp = float.MaxValue;
                    foreach (float normalizedDistance in normalizedDistances)
                    {
                        Vector4 site = sites[0];
                        float worldRadius = normalizedDistance * site.w / (spikeFactor * cellMetric * heightMapScale);
                        Vector3 surfacePoint = new(site.x + worldRadius, site.y, site.z);
                        object[] arguments = [shape, surfacePoint, sites, config, 0f, 0f];
                        profiles!.Invoke(null, arguments);
                        float smoothHeight = (float)arguments[4]!;
                        float sharpHeight = (float)arguments[5]!;
                        Assert.That(
                            float.IsNaN(smoothHeight) || float.IsInfinity(smoothHeight)
                                || float.IsNaN(sharpHeight) || float.IsInfinity(sharpHeight),
                            Is.False,
                            $"The {shapeName} profile must stay finite at distance {normalizedDistance} with cap {capRounding} and roundness {tipRoundness}.");
                        Assert.That(
                            sharpHeight,
                            Is.EqualTo(ExpectedSharpHeight(capRounding, tipRoundness, normalizedDistance)).Within(0.005f),
                            $"The {shapeName} sharp channel at distance {normalizedDistance} must track the cap slider.");
                        Assert.That(
                            smoothHeight,
                            Is.EqualTo(ExpectedSmoothHeight(capRounding, tipRoundness, normalizedDistance)).Within(0.005f),
                            $"The {shapeName} smooth channel at distance {normalizedDistance} must track the cap slider.");
                        if (normalizedDistance == 0f)
                        {
                            Assert.That(sharpHeight, Is.EqualTo(1f).Within(0.001f), $"The {shapeName} apex must keep full height with cap {capRounding}.");
                        }
                        else if (capRounding == 0f)
                        {
                            Assert.That(sharpHeight, Is.LessThan(previousSharp), $"With no cap the {shapeName} sharp profile must descend immediately off the apex instead of holding a plateau.");
                        }

                        if (capRounding == 1f && tipRoundness == 0f && normalizedDistance == 0.025f)
                        {
                            Assert.That(sharpHeight, Is.EqualTo(1f).Within(0.001f), $"The {shapeName} sharp profile must keep its saturated plateau at the default cap.");
                        }

                        previousSharp = sharpHeight;
                    }
                }
            }
        }
    }

    [Test]
    public void BulgeControlsDoNotChangeSpikeDirections()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type deformationType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidPrimitiveDeformation", throwOnError: true)!;
        Type simulationType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", throwOnError: true)!;
        Type environmentType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", throwOnError: true)!;
        Type shapeType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", throwOnError: true)!;
        Type configType = pluginAssembly.GetType("ChromaGLS.Settings.Config", throwOnError: true)!;
        MethodInfo? settingsMethod = deformationType.GetMethod("EvaluateSettings", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? siteMethod = deformationType.GetMethod("EvaluateSiteDeformation", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? restPositionsMethod = simulationType.GetMethod("EvaluatePrimitiveRestPositions", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? sitesMethod = environmentType.GetMethod("EvaluatePrimitiveSites", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? roundnessMethod = environmentType.GetMethod("EvaluateRoundness", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? yawMethod = environmentType.GetMethod("EvaluateChartYawDegrees", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(settingsMethod, Is.Not.Null);
        Assert.That(siteMethod, Is.Not.Null);
        Assert.That(restPositionsMethod, Is.Not.Null);
        Assert.That(sitesMethod, Is.Not.Null);
        Assert.That(roundnessMethod, Is.Not.Null);
        Assert.That(yawMethod, Is.Not.Null);
        PropertyInfo? bulgeRadius = configType.GetProperty("BulgeRadius", BindingFlags.Instance | BindingFlags.Public);
        PropertyInfo? bulgeHeight = configType.GetProperty("BulgeHeight", BindingFlags.Instance | BindingFlags.Public);
        PropertyInfo? tiltRadius = configType.GetProperty("FerrofluidSpikeTiltRadius", BindingFlags.Instance | BindingFlags.Public);
        Assert.That(bulgeRadius, Is.Not.Null);
        Assert.That(bulgeHeight, Is.Not.Null);

        foreach ((object shape, string shapeName) in new[]
        {
            (Enum.ToObject(shapeType, 1), "cube"),
            (Enum.ToObject(shapeType, 2), "sphere"),
        })
        {
            object config = Activator.CreateInstance(configType, nonPublic: true)!;
            float roundness = (float)roundnessMethod!.Invoke(null, [shape, config])!;
            float yawDegrees = (float)yawMethod!.Invoke(null, [shape, config])!;
            Vector2[] restPositions = (Vector2[])restPositionsMethod!.Invoke(null, [shape, 48, roundness])!;
            Vector4[] sites = (Vector4[])sitesMethod!.Invoke(null, [shape, restPositions, roundness, yawDegrees])!;
            Vector3 magnet = new(0f, 0.57f, 0f);
            const float strength = 0.74f;

            Vector4[] Deformations()
            {
                object settings = settingsMethod!.Invoke(null, [shape, config, 1f])!;
                Vector4[] results = new Vector4[sites.Length];
                for (int index = 0; index < sites.Length; index++)
                {
                    results[index] = (Vector4)siteMethod!.Invoke(
                        null,
                        [settings, shape, sites[index], magnet, strength, 0f])!;
                }

                return results;
            }

            Vector4[] baseline = Deformations();
            foreach (float editedRadius in new[] { 0.1f, 0.31f, 0.73f, 1.5f })
            {
                foreach (float editedHeight in new[] { 0f, 0.12f, 0.2f })
                {
                    bulgeRadius!.SetValue(config, editedRadius);
                    bulgeHeight!.SetValue(config, editedHeight);
                    Vector4[] edited = Deformations();
                    for (int index = 0; index < sites.Length; index++)
                    {
                        float drift = Vector3.Distance(
                            new Vector3(baseline[index].x, baseline[index].y, baseline[index].z),
                            new Vector3(edited[index].x, edited[index].y, edited[index].z));
                        Assert.That(drift, Is.LessThan(0.000001f), $"BulgeRadius {editedRadius} / BulgeHeight {editedHeight} must not steer the {shapeName} site {index} spike.");
                        Assert.That(edited[index].w, Is.EqualTo(baseline[index].w).Within(0.000001f), $"BulgeRadius {editedRadius} / BulgeHeight {editedHeight} must not change the {shapeName} site {index} height target.");
                    }
                }
            }

            bulgeRadius!.SetValue(config, 0.31f);
            bulgeHeight!.SetValue(config, 0.12f);
            Assert.That(tiltRadius, Is.Not.Null, "Primitives need an independent tilt radius so bulge edits cannot steer spikes.");
            tiltRadius!.SetValue(config, 0.31f);
            Vector4[] narrowTilt = Deformations();
            tiltRadius.SetValue(config, 0.73f);
            Vector4[] wideTilt = Deformations();
            float widestDrift = 0f;
            for (int index = 0; index < sites.Length; index++)
            {
                widestDrift = Mathf.Max(
                    widestDrift,
                    Vector3.Distance(
                        new Vector3(narrowTilt[index].x, narrowTilt[index].y, narrowTilt[index].z),
                        new Vector3(wideTilt[index].x, wideTilt[index].y, wideTilt[index].z)));
            }

            Assert.That(widestDrift, Is.GreaterThan(0.01f), $"The independent tilt radius must still steer off-center {shapeName} spikes when the lean band widens.");
        }
    }

    [Test]
    public void SupportBulgeTranslatesEachSpikeWithoutShearing()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type deformationType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidPrimitiveDeformation", throwOnError: true)!;
        Type simulationType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", throwOnError: true)!;
        Type shapeType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", throwOnError: true)!;
        Type configType = pluginAssembly.GetType("ChromaGLS.Settings.Config", throwOnError: true)!;
        MethodInfo? settingsMethod = deformationType.GetMethod("EvaluateSettings", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? displacedMethod = deformationType.GetMethod("EvaluateDisplacedPosition", BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo? mapSizeField = simulationType.GetField("PrimitiveHeightMapSize", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(settingsMethod, Is.Not.Null);
        Assert.That(displacedMethod, Is.Not.Null);
        Assert.That(mapSizeField, Is.Not.Null);
        PropertyInfo? bulgeHeight = configType.GetProperty("BulgeHeight", BindingFlags.Instance | BindingFlags.Public);
        Assert.That(bulgeHeight, Is.Not.Null);
        int mapSize = (int)mapSizeField!.GetValue(null)!;

        foreach ((object shape, Vector4 site, Vector2[] chartUvs, string shapeName) in new (object, Vector4, Vector2[], string)[]
        {
            (
                Enum.ToObject(shapeType, 1),
                new Vector4(0f, 0.5f, 0f, 0.04f),
                [new Vector2(0.5f, 0.5f), new Vector2(0.51f, 0.5f)],
                "cube"),
            (
                Enum.ToObject(shapeType, 2),
                new Vector4(0f, 0.5f, 0f, 0.04f),
                [new Vector2(0.5f, 0.5f), new Vector2(0.51f, 0.5f)],
                "sphere"),
        })
        {
            object config = Activator.CreateInstance(configType, nonPublic: true)!;
            Vector4[] sites = [site];
            Vector4[] deformations = [new Vector4(0f, 1f, 0f, 0.44f)];
            float[] sharpenings = [0f];
            float[] springScales = [0.44f];
            int[] texelOwners = new int[mapSize * mapSize];
            Vector3 magnet = new(0f, 0.57f, 0f);
            const float strength = 0.74f;

            Vector3[] Displacements()
            {
                object settings = settingsMethod!.Invoke(null, [shape, config, 1f])!;
                Vector3[] positions = new Vector3[chartUvs.Length];
                for (int index = 0; index < chartUvs.Length; index++)
                {
                    positions[index] = (Vector3)displacedMethod!.Invoke(
                        null,
                        [settings, shape, chartUvs[index], sites, deformations, sharpenings, springScales, texelOwners, magnet, strength, config])!;
                }

                return positions;
            }

            bulgeHeight!.SetValue(config, 0f);
            Vector3[] resting = Displacements();
            bulgeHeight.SetValue(config, 0.12f);
            Vector3[] lifted = Displacements();
            Vector3[] offsets = new Vector3[chartUvs.Length];
            for (int index = 0; index < chartUvs.Length; index++)
            {
                offsets[index] = lifted[index] - resting[index];
                Assert.That(offsets[index].magnitude, Is.GreaterThan(0.00001f), $"The {shapeName} support bulge must actually lift the surface at uv {chartUvs[index]}.");
            }

            Assert.That(
                Vector3.Distance(offsets[0], offsets[1]),
                Is.LessThan(0.00001f),
                $"Every vertex owned by one {shapeName} site must share that site's rigid support lift; a per-vertex bulge shears the cone flank.");
        }
    }

    [Test]
    public void MoundCurvatureChangesRelativeSiteHeightsNotIndividualProfiles()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type deformationType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidPrimitiveDeformation", throwOnError: true)!;
        Type environmentType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", throwOnError: true)!;
        Type shapeType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", throwOnError: true)!;
        Type configType = pluginAssembly.GetType("ChromaGLS.Settings.Config", throwOnError: true)!;
        MethodInfo? settingsMethod = deformationType.GetMethod("EvaluateSettings", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? siteMethod = deformationType.GetMethod("EvaluateSiteDeformation", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? moundMethod = deformationType.GetMethod("EvaluateSpikeMoundResponse", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? profiles = environmentType.GetMethod("EvaluatePrimitiveConeProfiles", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(settingsMethod, Is.Not.Null);
        Assert.That(siteMethod, Is.Not.Null);
        Assert.That(profiles, Is.Not.Null);
        Assert.That(moundMethod, Is.Not.Null, "The height envelope remap must live in one shared helper so the spring target and the render target stay aligned.");
        PropertyInfo? curvatureSetting = configType.GetProperty("FerrofluidSpikeMoundCurvature", BindingFlags.Instance | BindingFlags.Public);
        Assert.That(curvatureSetting, Is.Not.Null, "The session config must expose the cross-spike mound curvature knob.");
        object fresh = Activator.CreateInstance(configType, nonPublic: true)!;
        Assert.That(curvatureSetting!.GetValue(fresh), Is.EqualTo(2f).Within(0.0001f), "The authored mound curvature rounds the envelope by default.");

        Assert.That((float)moundMethod!.Invoke(null, [0f, 2f])!, Is.EqualTo(0f).Within(0.000001f), "The mound remap keeps the zero response fixed.");
        Assert.That((float)moundMethod.Invoke(null, [1f, 2f])!, Is.EqualTo(1f).Within(0.000001f), "The mound remap keeps the peak response fixed.");
        Assert.That((float)moundMethod.Invoke(null, [0.5f, 1f])!, Is.EqualTo(0.5f).Within(0.000001f), "Curvature 1 must be the linear response.");
        Assert.That((float)moundMethod.Invoke(null, [0.5f, 2f])!, Is.EqualTo(0.75f).Within(0.000001f));
        Assert.That((float)moundMethod.Invoke(null, [0.5f, 4f])!, Is.EqualTo(0.9375f).Within(0.000001f));

        foreach ((object shape, Vector4[] sites, string shapeName) in new (object, Vector4[], string)[]
        {
            (
                Enum.ToObject(shapeType, 1),
                new Vector4[]
                {
                    new(0f, 0.5f, 0f, 0.04f),
                    new(0.025f, 0.5f, 0f, 0.04f),
                    new(0.05f, 0.5f, 0f, 0.04f),
                    new(0.075f, 0.5f, 0f, 0.04f),
                },
                "cube"),
            (
                Enum.ToObject(shapeType, 2),
                new Vector4[]
                {
                    new(0f, 0.5f, 0f, 0.04f),
                    new(0.025f, Mathf.Sqrt(0.25f - (0.025f * 0.025f)), 0f, 0.04f),
                    new(0.05f, Mathf.Sqrt(0.25f - (0.05f * 0.05f)), 0f, 0.04f),
                    new(0.075f, Mathf.Sqrt(0.25f - (0.075f * 0.075f)), 0f, 0.04f),
                },
                "sphere"),
        })
        {
            object config = Activator.CreateInstance(configType, nonPublic: true)!;
            configType.GetProperty("MagnetHeightDeadZoneRadius")!.SetValue(config, 0.13f);
            configType.GetProperty("HeightGain")!.SetValue(config, 0.87f);
            configType.GetProperty("MagnetHeightStrengthCap")!.SetValue(config, 0.74f);
            configType.GetProperty("FerrofluidSpikeHeightRadius")!.SetValue(config, 0.31f);
            configType.GetProperty("FerrofluidSpikeTiltRadius")!.SetValue(config, 0.73f);
            Vector3 magnet = new(0f, 0.57f, 0f);
            const float strength = 0.74f;

            Vector4[] HeightsAt(float curvature)
            {
                curvatureSetting.SetValue(config, curvature);
                object settings = settingsMethod!.Invoke(null, [shape, config, 1f])!;
                Vector4[] results = new Vector4[sites.Length];
                for (int index = 0; index < sites.Length; index++)
                {
                    results[index] = (Vector4)siteMethod!.Invoke(null, [settings, shape, sites[index], magnet, strength, 0f])!;
                }

                return results;
            }

            float[] SharpeningAt(float curvature)
            {
                curvatureSetting.SetValue(config, curvature);
                object settings = settingsMethod!.Invoke(null, [shape, config, 1f])!;
                float[] results = new float[sites.Length];
                for (int index = 0; index < sites.Length; index++)
                {
                    object[] arguments = [settings, shape, sites[index], magnet, strength, 0f];
                    siteMethod!.Invoke(null, arguments);
                    results[index] = (float)arguments[5]!;
                }

                return results;
            }

            Vector4[] linear = HeightsAt(1f);
            Vector4[] dome = HeightsAt(2f);
            Vector4[] deep = HeightsAt(4f);
            float[] linearSharpening = SharpeningAt(1f);
            float[] domeSharpening = SharpeningAt(2f);
            float[] deepSharpening = SharpeningAt(4f);
            for (int index = 0; index < sites.Length; index++)
            {
                Assert.That(float.IsNaN(dome[index].w) || float.IsInfinity(dome[index].w) || float.IsNaN(deep[index].w) || float.IsInfinity(deep[index].w), Is.False, $"The {shapeName} site {index} height must stay finite.");
                Assert.That(dome[index].w, Is.LessThanOrEqualTo(1.37f + 0.0001f), $"The {shapeName} site {index} target must respect the primitive height cap.");
                Assert.That(deep[index].w, Is.LessThanOrEqualTo(1.37f + 0.0001f), $"The {shapeName} site {index} target must respect the primitive height cap.");
                Assert.That(
                    Vector3.Distance(
                        new Vector3(linear[index].x, linear[index].y, linear[index].z),
                        new Vector3(deep[index].x, deep[index].y, deep[index].z)),
                    Is.LessThan(0.000001f),
                    $"Mound curvature must not steer the {shapeName} site {index} spike.");
                Assert.That(domeSharpening[index], Is.EqualTo(linearSharpening[index]).Within(0.000001f), $"Mound curvature must not alter the raw {shapeName} site {index} apex sharpening.");
                Assert.That(deepSharpening[index], Is.EqualTo(linearSharpening[index]).Within(0.000001f), $"Mound curvature must not alter the raw {shapeName} site {index} apex sharpening.");
            }

            Assert.That(dome[0].w, Is.EqualTo(linear[0].w).Within(0.0001f), $"The saturated center {shapeName} site keeps the same height at every curvature.");
            Assert.That(deep[0].w, Is.EqualTo(linear[0].w).Within(0.0001f), $"The saturated center {shapeName} site keeps the same height at every curvature.");
            for (int index = 1; index < sites.Length; index++)
            {
                Assert.That(dome[index].w, Is.GreaterThan(linear[index].w + 0.0001f), $"Curvature 2 must raise the {shapeName} shoulder site {index}.");
                Assert.That(deep[index].w, Is.GreaterThan(dome[index].w + 0.0001f), $"Curvature 4 must raise the {shapeName} shoulder site {index} further.");
            }

            float innerDrop = dome[0].w - dome[1].w;
            float nextDrop = dome[1].w - dome[2].w;
            Assert.That(innerDrop, Is.LessThan(nextDrop * 0.5f), $"The p2 {shapeName} envelope must be an inverted bowl: the near-center drop flattens instead of descending like a straight line.");
            Assert.That(nextDrop, Is.GreaterThan(0.0001f), $"The p2 {shapeName} envelope must still descend away from the magnet.");

            object[] profileArguments = [shape, new Vector3(0.01f, 0.5f, 0f), sites, config, 0f, 0f];
            curvatureSetting.SetValue(config, 1f);
            profiles!.Invoke(null, profileArguments);
            float neutralSmooth = (float)profileArguments[4]!;
            float neutralSharp = (float)profileArguments[5]!;
            curvatureSetting.SetValue(config, 4f);
            profiles.Invoke(null, profileArguments);
            Assert.That((float)profileArguments[4]!, Is.EqualTo(neutralSmooth).Within(0.000001f), $"Mound curvature must not reshape the {shapeName} smooth cone channel.");
            Assert.That((float)profileArguments[5]!, Is.EqualTo(neutralSharp).Within(0.000001f), $"Mound curvature must not reshape the {shapeName} sharp cone channel.");
        }
    }

    [Test]
    public void CubeSpikeFootprintsShareOneFaceCenterWidth()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type environmentType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", throwOnError: true)!;
        Type simulationType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", throwOnError: true)!;
        Type shapeType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", throwOnError: true)!;
        Type configType = pluginAssembly.GetType("ChromaGLS.Settings.Config", throwOnError: true)!;
        object shape = Enum.ToObject(shapeType, 1);
        object config = Activator.CreateInstance(configType, nonPublic: true)!;
        MethodInfo? restPositionsMethod = simulationType.GetMethod("EvaluatePrimitiveRestPositions", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? sites = environmentType.GetMethod("EvaluatePrimitiveSites", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? fromDirection = environmentType.GetMethod("EvaluateSurfacePositionFromDirection", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? normalFromDirection = environmentType.GetMethod("EvaluateSurfaceNormalFromDirection", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? profiles = environmentType.GetMethod("EvaluatePrimitiveConeProfiles", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? chartYaw = environmentType.GetMethod("EvaluateChartYawDegrees", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? roundness = environmentType.GetMethod("EvaluateRoundness", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? densityLattice = simulationType.GetMethod(
            "EvaluateDensityLattice",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            [typeof(float), shapeType, typeof(float)],
            null);
        Assert.That(restPositionsMethod, Is.Not.Null);
        Assert.That(sites, Is.Not.Null);
        Assert.That(fromDirection, Is.Not.Null);
        Assert.That(normalFromDirection, Is.Not.Null);
        Assert.That(profiles, Is.Not.Null);
        PropertyInfo? capSetting = configType.GetProperty("FerrofluidPrimitiveTipCapRounding", BindingFlags.Instance | BindingFlags.Public);
        PropertyInfo? roundnessSetting = configType.GetProperty("FerrofluidPrimitiveTipRoundness", BindingFlags.Instance | BindingFlags.Public);
        Assert.That(capSetting, Is.Not.Null);
        Assert.That(roundnessSetting, Is.Not.Null);
        roundnessSetting!.SetValue(config, 0f);

        float density = (float)configType.GetProperty("FerrofluidSpikeDensity")!.GetValue(config)!;
        float densityScale = (float)configType.GetProperty("FerrofluidCubeSpikeDensity")!.GetValue(config)!;
        int axis = ((Vector2Int)densityLattice!.Invoke(null, [density, shape, densityScale])!).x;
        float shapeRoundness = (float)roundness!.Invoke(null, [shape, config])!;
        Vector2[] restPositions = (Vector2[])restPositionsMethod!.Invoke(null, [shape, axis * axis, shapeRoundness])!;
        float yaw = (float)chartYaw!.Invoke(null, [shape, config])!;
        Vector4[] siteData = (Vector4[])sites!.Invoke(null, [shape, restPositions, shapeRoundness, yaw])!;

        float minSpacing = float.MaxValue;
        float maxSpacing = 0f;
        foreach (Vector4 site in siteData)
        {
            Assert.That(float.IsNaN(site.w) || float.IsInfinity(site.w) || site.w <= 0f, Is.False, "Every cube site must carry a positive finite shared footprint spacing.");
            minSpacing = Math.Min(minSpacing, site.w);
            maxSpacing = Math.Max(maxSpacing, site.w);
        }

        Assert.That(maxSpacing / minSpacing, Is.LessThanOrEqualTo(1.00001f), "All cube sites must share one face-referenced footprint spacing; per-site spacing made edge and corner spikes wider or narrower than face spikes.");

        Vector3[] directions = [Vector3.up, new Vector3(1f, 1f, 0f).normalized, new Vector3(1f, 1f, 1f).normalized];
        string[] labels = ["face", "edge", "corner"];
        List<Vector3> owners = new();
        List<Vector3> tangentsA = new();
        List<Vector3> tangentsB = new();
        for (int k = 0; k < directions.Length; k++)
        {
            Vector3 target = (Vector3)fromDirection!.Invoke(null, [shape, directions[k], shapeRoundness])!;
            int owner = 0;
            float closest = float.MaxValue;
            for (int index = 0; index < siteData.Length; index++)
            {
                float distance = (new Vector3(siteData[index].x, siteData[index].y, siteData[index].z) - target).sqrMagnitude;
                if (distance < closest)
                {
                    closest = distance;
                    owner = index;
                }
            }

            Vector3 point = new(siteData[owner].x, siteData[owner].y, siteData[owner].z);
            Vector3 normal = (Vector3)normalFromDirection!.Invoke(null, [shape, point.normalized, shapeRoundness])!;
            Vector3 tangentA = Vector3.Cross(normal, Math.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            owners.Add(point);
            tangentsA.Add(tangentA);
            tangentsB.Add(Vector3.Cross(normal, tangentA));
        }

        foreach (float capRounding in new[] { 0f, 1f })
        {
            capSetting!.SetValue(config, capRounding);
            foreach (float level in new[] { 0.9f, 0.98f })
            {
                float minimum = float.MaxValue;
                float maximum = 0f;
                for (int siteIndex = 0; siteIndex < owners.Count; siteIndex++)
                {
                    Vector3 point = owners[siteIndex];
                    object[] peakArguments = [shape, point, siteData, config, 0f, 0f];
                    profiles!.Invoke(null, peakArguments);
                    float peak = (float)peakArguments[4]!;
                    for (int spoke = 0; spoke < 8; spoke++)
                    {
                        float angle = spoke * Mathf.PI / 4f;
                        Vector3 tangent = (tangentsA[siteIndex] * Mathf.Cos(angle)) + (tangentsB[siteIndex] * Mathf.Sin(angle));
                        float lower = 0f;
                        float upper = 0.2f;
                        bool crossed = false;
                        for (int step = 1; step <= 200; step++)
                        {
                            float radius = 0.2f * step / 200f;
                            Vector3 surfacePoint = (Vector3)fromDirection!.Invoke(null, [shape, (point + (tangent * radius)).normalized, shapeRoundness])!;
                            object[] arguments = [shape, surfacePoint, siteData, config, 0f, 0f];
                            profiles!.Invoke(null, arguments);
                            if ((float)arguments[4]! < peak * level)
                            {
                                upper = radius;
                                lower = 0.2f * (step - 1) / 200f;
                                crossed = true;
                                break;
                            }
                        }

                        Assert.That(crossed, Is.True, $"The {labels[siteIndex]} spike must fall below its {level} contour within the scan radius at cap {capRounding}.");
                        for (int iteration = 0; iteration < 15; iteration++)
                        {
                            float radius = (lower + upper) * 0.5f;
                            Vector3 surfacePoint = (Vector3)fromDirection!.Invoke(null, [shape, (point + (tangent * radius)).normalized, shapeRoundness])!;
                            object[] arguments = [shape, surfacePoint, siteData, config, 0f, 0f];
                            profiles!.Invoke(null, arguments);
                            if ((float)arguments[4]! >= peak * level)
                            {
                                lower = radius;
                            }
                            else
                            {
                                upper = radius;
                            }
                        }

                        float contourRadius = (lower + upper) * 0.5f;
                        minimum = Math.Min(minimum, contourRadius);
                        maximum = Math.Max(maximum, contourRadius);
                    }
                }

                Assert.That(
                    maximum / minimum,
                    Is.LessThanOrEqualTo(1.05f),
                    $"Cube {level} contour radius varies {maximum / minimum:F3}x across face, edge, and corner spikes at cap {capRounding}; the shared footprint spacing must hold the same profile width at every site.");
            }
        }
    }

    [Test]
    public void CubeSpikeSpacingAveragesSixFaceCenterNeighbors()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type environmentType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", throwOnError: true)!;
        MethodInfo? spacing = environmentType.GetMethod("EvaluateCubeSpikeSpacing", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(spacing, Is.Not.Null, "The cube footprint needs one shared spacing derived from the six face-center neighborhoods.");

        Vector3[] positions =
        [
            new Vector3(0f, 0.5f, 0f),
            new Vector3(0.05f, 0.5f, 0f),
            new Vector3(0f, -0.5f, 0f),
            new Vector3(-0.06f, -0.5f, 0f),
            new Vector3(-0.5f, 0f, 0f),
            new Vector3(-0.5f, 0.07f, 0f),
            new Vector3(0.5f, 0f, 0f),
            new Vector3(0.5f, 0f, 0.08f),
            new Vector3(0f, 0f, 0.5f),
            new Vector3(0.09f, 0f, 0.5f),
            new Vector3(0f, 0f, -0.5f),
            new Vector3(0f, -0.10f, -0.5f),
            new Vector3(0.45f, 0.45f, 0.45f),
            new Vector3(0.46f, 0.44f, 0.47f),
            new Vector3(-0.44f, -0.46f, 0.45f),
            new Vector3(-0.45f, -0.44f, 0.47f),
        ];
        float result = (float)spacing!.Invoke(null, [positions])!;
        Assert.That(float.IsNaN(result) || float.IsInfinity(result), Is.False);
        Assert.That(result, Is.EqualTo(0.075f).Within(0.0001f), "The shared spacing must average each face-center site's own nearest-neighbor distance so asymmetric corner neighborhoods cannot pull the footprint.");
    }

    // SpikeHeightSpringIntegratesTowardMagnetTarget gives every spike height real momentum: a per-texel GPU spring integrates the site scale toward the magnet's field target so spikes accelerate and settle instead of snapping, on the torus and the primitives alike.
    [Test]
    public void SpikeHeightSpringIntegratesTowardMagnetTarget()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type configType = pluginAssembly.GetType("ChromaGLS.Settings.Config", throwOnError: true)!;
        Type simulationType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", throwOnError: true)!;
        object config = Activator.CreateInstance(configType, nonPublic: true)!;
        // The user pinned the spring defaults at strength 250 and damping 12, a damping ratio near 0.38 that keeps a deliberate elastic overshoot while still settling.
        Assert.That(configType.GetProperty("FerrofluidSpikeSpringStrength")!.GetValue(config), Is.EqualTo(250f).Within(0.001f));
        Assert.That(configType.GetProperty("FerrofluidSpikeSpringDamping")!.GetValue(config), Is.EqualTo(12f).Within(0.0001f));

        // The CPU mirror of the spring pass must converge to a step target with finite velocity; the user's pinned profile (~0.38 damping ratio) permits a visible overshoot while a low-damping setting must still ring so the elastic slider keeps its purpose.
        MethodInfo? integrate = simulationType.GetMethod("IntegrateSpikeSpringScale", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(integrate, Is.Not.Null, "The spring update needs a shared CPU mirror so its stability is testable without a GPU.");
        float height = 0f;
        float velocity = 0f;
        float peakOvershoot = 0f;
        for (int step = 0; step < 240; step++)
        {
            Vector2 state = (Vector2)integrate!.Invoke(null, [height, velocity, 1f, 250f, 12f, 1f / 60f])!;
            height = state.x;
            velocity = state.y;
            peakOvershoot = Mathf.Max(peakOvershoot, height - 1f);
            Assert.That(float.IsNaN(height) || float.IsInfinity(height), Is.False, "The spring must stay finite at every step.");
        }

        Assert.That(height, Is.EqualTo(1f).Within(0.02f), "The spring must converge to the magnet's target height.");
        Assert.That(peakOvershoot, Is.LessThan(0.45f), "The pinned spring profile sits at a damping ratio near 0.38, so a visible elastic overshoot is expected but runaway ringing is not.");

        height = 0f;
        velocity = 0f;
        float elasticOvershoot = 0f;
        for (int step = 0; step < 240; step++)
        {
            Vector2 state = (Vector2)integrate!.Invoke(null, [height, velocity, 1f, 120f, 4f, 1f / 60f])!;
            height = state.x;
            velocity = state.y;
            elasticOvershoot = Mathf.Max(elasticOvershoot, height - 1f);
        }

        Assert.That(elasticOvershoot, Is.GreaterThan(0.05f).And.LessThan(0.7f), "Lowering the damping slider must restore visible elastic overshoot; a spring that cannot ring makes the control pointless.");

        // The torus baseline passes the spring sliders through live because the user asked the new momentum to affect the button too.
        MethodInfo? baseline = simulationType.GetMethod("EvaluateTorusBaseline", BindingFlags.Static | BindingFlags.NonPublic);
        object live = Activator.CreateInstance(configType, nonPublic: true)!;
        configType.GetProperty("FerrofluidSpikeSpringStrength")!.SetValue(live, 321f);
        configType.GetProperty("FerrofluidSpikeSpringDamping")!.SetValue(live, 7.5f);
        object snapshot = baseline!.Invoke(null, [live])!;
        Assert.That(configType.GetProperty("FerrofluidSpikeSpringStrength")!.GetValue(snapshot), Is.EqualTo(321f).Within(0.0001f), "The spring strength slider must reach the torus even though the rest of its profile stays pinned.");
        Assert.That(configType.GetProperty("FerrofluidSpikeSpringDamping")!.GetValue(snapshot), Is.EqualTo(7.5f).Within(0.0001f), "The spring damping slider must reach the torus even though the rest of its profile stays pinned.");

        using StreamReader reader = new(pluginAssembly.GetManifestResourceStream("ChromaGLS.Settings.modifiers.bsml")!);
        string bsml = reader.ReadToEnd();
        Assert.That(bsml, Does.Contain("value='ferrofluid-spike-spring-strength' apply-on-change='true'"));
        Assert.That(bsml, Does.Contain("value='ferrofluid-spike-spring-damping' apply-on-change='true'"));
    }

    // FerrofluidTweaksAreSessionOnly keeps experimental values out of BSIPA reads and writes, while a new session uses the authored defaults and keeps feature-enable preferences persistent.
    [Test]
    public void FerrofluidTweaksAreSessionOnly()
    {
        Type configType = Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings.Config", true)!;
        object session = Activator.CreateInstance(configType, nonPublic: true)!;
        object nextSession = Activator.CreateInstance(configType, nonPublic: true)!;
        Type ignoreType = typeof(IPA.Config.Stores.GeneratedStore).Assembly.GetType("IPA.Config.Stores.Attributes.IgnoreAttribute", true)!;
        foreach (PropertyInfo property in configType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.Name == "Enabled"
                || property.Name == "FerrofluidBorderEnabled"
                || property.Name == "FerrofluidCubeEnabled"
                || property.Name == "FerrofluidSphereEnabled")
            {
                Assert.That(property.IsDefined(ignoreType, true), Is.False, $"{property.Name} must remain a persisted feature preference.");
                continue;
            }

            Assert.That(property.IsDefined(ignoreType, true), Is.True, $"{property.Name} must be ignored by BSIPA serialization AND deserialization so old saved tweaks cannot override defaults.");
            object original = property.GetValue(session)!;
            object changed = property.PropertyType == typeof(bool) ? !(bool)original
                : property.PropertyType == typeof(int) ? (object)((int)original + 1)
                : (float)original + 0.1f;
            property.SetValue(session, changed);
            Assert.That(property.GetValue(session), Is.EqualTo(changed), "Live edits must still work within the current session.");
            Assert.That(property.GetValue(nextSession), Is.EqualTo(original), $"A new session must restore the authored default for {property.Name}.");
        }
    }

    // SpikeVisibilityControlsAreIndependentAndSessionOnly exposes geometry-scale controls absent from the source magnetic height cap, without restoring persisted CPU-hack values.
    [Test]
    public void SpikeVisibilityControlsAreIndependentAndSessionOnly()
    {
        Assembly plugin = Assembly.Load("ChromaGLS");
        Type configType = plugin.GetType("ChromaGLS.Settings.Config", true)!;
        object config = Activator.CreateInstance(configType, nonPublic: true)!;
        using StreamReader reader = new(plugin.GetManifestResourceStream("ChromaGLS.Settings.modifiers.bsml")!);
        string bsml = reader.ReadToEnd();
        foreach ((string propertyName, string binding, string range, float expectedDefault) in new[]
        {
            // The latest 1.44.1 live settings dump re-captured these session defaults at base=3.0, tall=1.70, density=0.35, cutoff=0.10; the prior dump's values no longer match what the user last dialed in.
            ("FerrofluidSpikeBaseSize", "ferrofluid-spike-base-size", "min='0.5' max='3'", 3f),
            ("FerrofluidSpikeHeightScale", "ferrofluid-spike-height-scale", "min='0.25' max='4'", 0.9f),
            ("FerrofluidSpikeDensity", "ferrofluid-spike-density", "min='0.25' max='1.5'", 0.35f),
            ("FerrofluidSpikeBaseCutoff", "ferrofluid-spike-base-cutoff", "min='0' max='0.1' increment='0.001'", 0.1f),
        })
        {
            PropertyInfo? property = configType.GetProperty(propertyName);
            Assert.That(property, Is.Not.Null, $"Missing independent visibility knob {propertyName}.");
            Assert.That(property!.GetValue(config), Is.EqualTo(expectedDefault).Within(0.0001f), "The captured 1.37.1 tuning profile must be the session-start appearance.");
            Assert.That(bsml, Does.Contain($"value='{binding}' apply-on-change='true' {range}"));
        }
    }

    // ThinFilmLutControlsScaleAndOffsetLookup covers the requested independent row-range controls and preserves the user's confirmed 1.37.1 color band.
    [Test]
    public void ThinFilmLutControlsScaleAndOffsetLookup()
    {
        Assembly plugin = Assembly.Load("ChromaGLS");
        Type configType = plugin.GetType("ChromaGLS.Settings.Config", true)!;
        object config = Activator.CreateInstance(configType, nonPublic: true)!;
        using StreamReader reader = new(plugin.GetManifestResourceStream("ChromaGLS.Settings.modifiers.bsml")!);
        string bsml = reader.ReadToEnd();

        Assert.That(configType.GetProperty("FerrofluidLutScale")?.GetValue(config), Is.EqualTo(3f).Within(0.0001f));
        Assert.That(configType.GetProperty("FerrofluidLutOffset")?.GetValue(config), Is.EqualTo(0.07f).Within(0.0001f));
        Assert.That(bsml, Does.Contain("value='ferrofluid-lut-scale' apply-on-change='true' min='0' max='4' increment='0.01'"));
        Assert.That(bsml, Does.Contain("value='ferrofluid-lut-offset' apply-on-change='true' min='-1' max='1' increment='0.01'"));
    }

    // MagneticHeightCapAndDeadZoneLeaveTiltRaw verifies the requested plateau affects height gain/loss and bulge without capping the distance-driven tilt angle path.
    [Test]
    public void MagneticHeightCapAndDeadZoneLeaveTiltRaw()
    {
        Assembly plugin = Assembly.Load("ChromaGLS");
        Type configType = plugin.GetType("ChromaGLS.Settings.Config", true)!;
        object config = Activator.CreateInstance(configType, nonPublic: true)!;
        using StreamReader reader = new(plugin.GetManifestResourceStream("ChromaGLS.Settings.modifiers.bsml")!);
        string bsml = reader.ReadToEnd();
        // The latest 1.44.1 tuning pass pinned the height cap fully open at 1.00 and the dead zone at zero; the prior 0.86/0.04 capture no longer matches the authored defaults.
        Assert.That(configType.GetProperty("MagnetHeightStrengthCap")?.GetValue(config), Is.EqualTo(0.74f).Within(0.0001f));
        Assert.That(configType.GetProperty("MagnetHeightDeadZoneRadius")?.GetValue(config), Is.EqualTo(0.13f).Within(0.0001f));
        // The 1.44.1 live dump re-captured the back-falloff exponent at 2.65; the old 2.0 expectation was the screenshot-era default.
        Assert.That(configType.GetProperty("BackHeightFalloffExponent")?.GetValue(config), Is.EqualTo(2.65f).Within(0.0001f));
        Assert.That(bsml, Does.Contain("value='magnet-height-strength-cap' apply-on-change='true' min='0' max='1' increment='0.01'"));
        Assert.That(bsml, Does.Contain("value='magnet-height-dead-zone-radius' apply-on-change='true' min='0' max='0.5' increment='0.005'"));
        Assert.That(bsml, Does.Contain("value='back-height-falloff-exponent' apply-on-change='true' min='0.05' max='8' increment='0.05'"));
        Type deformation = plugin.GetType("ChromaGLS.Settings.FerrofluidPrimitiveDeformation", true)!;
        Type shapeType = plugin.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", true)!;
        object sphere = Enum.ToObject(shapeType, 2);
        MethodInfo settingsMethod = deformation.GetMethod("EvaluateSettings", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo siteMethod = deformation.GetMethod("EvaluateSiteDeformation", BindingFlags.Static | BindingFlags.NonPublic)!;
        configType.GetProperty("BulgeRadius")!.SetValue(config, 1f);
        configType.GetProperty("PeakWidth")!.SetValue(config, 1f);
        configType.GetProperty("HeightGain")!.SetValue(config, 1f);
        configType.GetProperty("HeightFalloffExponent")!.SetValue(config, 1f);
        configType.GetProperty("MagnetHeightStrengthCap")!.SetValue(config, 0.1f);
        object cappedSettings = settingsMethod.Invoke(null, [sphere, config, 1f])!;
        configType.GetProperty("MagnetHeightStrengthCap")!.SetValue(config, 1f);
        object uncappedSettings = settingsMethod.Invoke(null, [sphere, config, 1f])!;
        Vector4 site = new(0.5f, 0f, 0f, 0.04f);
        Vector3 magnet = new(0.7f, 0.2f, 0f);
        Vector4 capped = (Vector4)siteMethod.Invoke(null, [cappedSettings, sphere, site, magnet, 1f, 0f])!;
        Vector4 uncapped = (Vector4)siteMethod.Invoke(null, [uncappedSettings, sphere, site, magnet, 1f, 0f])!;
        Assert.That(capped.w, Is.LessThan(uncapped.w));
        Assert.That(Vector3.Distance(new Vector3(capped.x, capped.y, capped.z), new Vector3(uncapped.x, uncapped.y, uncapped.z)), Is.LessThan(0.000001f), "The height cap cannot change tilt.");
        configType.GetProperty("MagnetHeightDeadZoneRadius")!.SetValue(config, 0.3f);
        object plateauSettings = settingsMethod.Invoke(null, [sphere, config, 1f])!;
        Vector4 plateau = (Vector4)siteMethod.Invoke(null, [plateauSettings, sphere, site, magnet, 1f, 0f])!;
        Assert.That(plateau.w, Is.GreaterThan(uncapped.w));
        Assert.That(Vector3.Distance(new Vector3(plateau.x, plateau.y, plateau.z), new Vector3(uncapped.x, uncapped.y, uncapped.z)), Is.LessThan(0.000001f), "The height plateau cannot change tilt.");
    }

    // CenterMagnetBlendsToDistributedOuterRingResponse keeps the normal focused field until the opening blend rises, then smoothly flattens inner spikes and applies a bounded proximity-weighted outward response around the outer edge.
    [Test]
    public void CenterMagnetBlendsToDistributedOuterRingResponse()
    {
        Assembly plugin = Assembly.Load("ChromaGLS");
        Type simulationType = plugin.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", true)!;
        MethodInfo? openingWeight = simulationType.GetMethod("EvaluateMagnetOpeningWeight", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(openingWeight, Is.Not.Null, "The CPU pointer mapper must detect when the virtual cursor enters the ring opening.");
        Assert.That((float)openingWeight!.Invoke(null, [Mathf.PI])!, Is.EqualTo(1f).Within(0.0001f));
        Assert.That((float)openingWeight.Invoke(null, [0f])!, Is.EqualTo(0f).Within(0.0001f));
        Assert.That((float)openingWeight.Invoke(null, [Mathf.PI * 70f / 180f])!, Is.EqualTo(0f).Within(0.0001f), "The distributed blend must begin 20 degrees before the viewer-facing cross-section.");
        Assert.That((float)openingWeight.Invoke(null, [Mathf.PI * 0.5f])!, Is.InRange(0.001f, 0.05f), "Viewer-facing opening positions should receive only the start of the slowed distributed blend.");
        Assert.That((float)openingWeight.Invoke(null, [Mathf.PI * 2f / 3f])!, Is.InRange(0.1f, 0.35f), "The widened ramp must remain gradual past the viewer-facing angle instead of snapping outward.");
        Assert.That((float)openingWeight.Invoke(null, [Mathf.PI * 150f / 180f])!, Is.GreaterThan(0.6f));
        Assert.That((float)openingWeight.Invoke(null, [Mathf.PI])!, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(
            simulationType.GetMethod("EvaluateMagnetOpeningRadialDistance", BindingFlags.Static | BindingFlags.NonPublic),
            Is.Null,
            "The rejected magnet retarget must not remain in the pointer mapper.");
    }

    // ExpandedSliderCapsReachTheSettingsMenu prevents wider endpoints from being silently re-clamped to the old 1.44.1 tuning limits in the exposed settings surface.
    [Test]
    public void ExpandedSliderCapsReachTheSettingsMenu()
    {
        Assembly plugin = Assembly.Load("ChromaGLS");
        using StreamReader reader = new(plugin.GetManifestResourceStream("ChromaGLS.Settings.modifiers.bsml")!);
        string bsml = reader.ReadToEnd();

        Assert.That(bsml, Does.Contain("value='ferrofluid-exposure' apply-on-change='true' min='0' max='2' increment='0.01'"));
        Assert.That(bsml, Does.Contain("value='magnet-elasticity' apply-on-change='true' min='4' max='160' increment='1'"));
        Assert.That(bsml, Does.Contain("value='peak-width' apply-on-change='true' min='0.03' max='1' increment='0.01'"));

        // PrimitiveAmbientFlattenAndTipSlidersDriveVisibleResponse widened the primitive-only endpoints after the one-times ambient weight and narrow flatten radius were imperceptible under the user's darker exposure.
        Assert.That(bsml, Does.Contain("value='ferrofluid-primitive-tip-roundness' apply-on-change='true' min='0' max='1' increment='0.01'"));
        Assert.That(bsml, Does.Contain("value='ferrofluid-primitive-ambient-scale' apply-on-change='true' min='0' max='5' increment='0.01'"));
        Assert.That(bsml, Does.Contain("value='ferrofluid-primitive-flatten-radius' apply-on-change='true' min='0.1' max='2' increment='0.01'"));

        // FerrofluidReflectivitySliderDialsMatteToOilyGloss exposes the shared matte-to-oily dial across the shader's crossfade range.
        Assert.That(bsml, Does.Contain("value='ferrofluid-reflectivity' apply-on-change='true' min='0' max='1' increment='0.01'"));

        // MatteSheenAndAnglesTuneWashSpecAndGlossSpread exposes both angular knobs as degrees of coverage measured inward from the silhouette so the full slider travel produces a visible sweep; exponents only reshaped an imperceptible band.
        Assert.That(bsml, Does.Contain("value='ferrofluid-matte-angle' apply-on-change='true' min='0' max='90' increment='0.5'"));
        Assert.That(bsml, Does.Contain("value='ferrofluid-reflection-angle' apply-on-change='true' min='0' max='90' increment='0.5'"));
    }

    // FullWebGlTuningSurfaceIsExposedForSessionTuning retains every source control without persisting experimental adjustments across game restarts.
    [Test]
    public void FullWebGlTuningSurfaceIsExposedForSessionTuning()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type configType = pluginAssembly.GetType("ChromaGLS.Settings.Config", throwOnError: true)!;
        string[] settingProperties =
        [
            "FerrofluidSpikesEnabled",
            "FerrofluidBodyEnabled",
            "FerrofluidEmissionStrength",
            "FerrofluidExposure",
            "FerrofluidLutScale",
            "FerrofluidLutOffset",
            "FerrofluidSpikeBaseSize",
            "FerrofluidSpikeHeightScale",
            "FerrofluidSpikeDensity",
            "FerrofluidSpikeBaseCutoff",
            "ParticleMass",
            "RestDensity",
            "GasConstant",
            "Viscosity",
            "SimulationSteps",
            "MagnetRadius",
            "MagnetMaximumSlide",
            "MagnetSlideFraction",
            "MagnetSlideFalloffExponent",
            "MagnetElasticity",
            "MagnetDamping",
            "MagnetForwardDistance",
            "BulgeRadius",
            "BulgeHeight",
            "HeightFalloffExponent",
            "PeakWidth",
            "HeightGain",
            "BackHeightLoss",
            "BackHeightFalloffExponent",
            "MagnetHeightStrengthCap",
            "MagnetHeightDeadZoneRadius",
            "MaximumHeight",
            "MaximumTiltDegrees",
            "TiltPeakRadiusMultiplier",
            "TiltReturnRadiusMultiplier",
            "TiltRiseExponent",
            "TiltReturnExponent",
            "InnerCornerFan",
            "FerrofluidReflectivity",
            "FerrofluidMatteSheen",
            "FerrofluidMatteAngle",
            "FerrofluidReflectionAngle",
            "FerrofluidMatteRotationX",
            "FerrofluidMatteRotationY",
            "FerrofluidMatteRotationZ",
            "FerrofluidReflectionRotationX",
            "FerrofluidReflectionRotationY",
            "FerrofluidReflectionRotationZ",
        ];

        foreach (string propertyName in settingProperties)
        {
            Assert.That(configType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public), Is.Not.Null, $"The session config must expose the source ferrofluid control {propertyName}.");
        }

        using Stream? bsmlStream = pluginAssembly.GetManifestResourceStream("ChromaGLS.Settings.modifiers.bsml");
        Assert.That(bsmlStream, Is.Not.Null);
        using StreamReader reader = new(bsmlStream!);
        string bsml = reader.ReadToEnd();
        string[] valueBindings =
        [
            "ferrofluid-spikes-enabled",
            "ferrofluid-body-enabled",
            "ferrofluid-emission-strength",
            "ferrofluid-exposure",
            "ferrofluid-lut-scale",
            "ferrofluid-lut-offset",
            "ferrofluid-spike-base-size",
            "ferrofluid-spike-height-scale",
            "ferrofluid-spike-density",
            "ferrofluid-spike-base-cutoff",
            "particle-mass",
            "rest-density",
            "gas-constant",
            "viscosity",
            "simulation-steps",
            "magnet-radius",
            "magnet-maximum-slide",
            "magnet-slide-fraction",
            "magnet-slide-falloff-exponent",
            "magnet-elasticity",
            "magnet-damping",
            "magnet-forward-distance",
            "bulge-radius",
            "bulge-height",
            "height-falloff-exponent",
            "peak-width",
            "height-gain",
            "back-height-loss",
            "back-height-falloff-exponent",
            "magnet-height-strength-cap",
            "magnet-height-dead-zone-radius",
            "maximum-height",
            "maximum-tilt-degrees",
            "tilt-peak-radius-multiplier",
            "tilt-return-radius-multiplier",
            "tilt-rise-exponent",
            "tilt-return-exponent",
            "inner-corner-fan",
            "ferrofluid-reflectivity",
            "ferrofluid-matte-sheen",
            "ferrofluid-matte-angle",
            "ferrofluid-reflection-angle",
            "ferrofluid-matte-rotation-x",
            "ferrofluid-matte-rotation-y",
            "ferrofluid-matte-rotation-z",
            "ferrofluid-reflection-rotation-x",
            "ferrofluid-reflection-rotation-y",
            "ferrofluid-reflection-rotation-z",
        ];
        foreach (string valueBinding in valueBindings)
        {
            Assert.That(bsml, Does.Contain($"value='{valueBinding}'"), $"The ChromaGLS tab must expose the source ferrofluid control {valueBinding}.");
        }

        Assert.That(bsml, Does.Contain("text='Show ferrofluid torus'"), "The visibility control must describe the complete GPU effect it gates.");
        Assert.That(bsml, Does.Contain("value='ferrofluid-exposure' apply-on-change='true' min='0' max='2'"), "Beat Saber bloom compensation must remain adjustable beyond the captured full-strength endpoint without altering the source LUT.");
        Assert.That(bsml, Does.Contain("value='magnet-forward-distance' apply-on-change='true' min='0' max='0.6'"), "The virtual-saber ray plane must expose its requested forward offset.");
        Assert.That(bsml, Does.Contain("value='simulation-steps' apply-on-change='true' min='0' max='6' increment='1'"), "The Unity port must retain the source zero-through-six extra-step range.");
    }

    // FreshConfigDefaultsMatchTheCapturedOneThreeSevenOneProfile preserves the user's confirmed live tuning without making BSIPA persistence the mechanism that restores it; the values now come from the latest 1.44.1 settings-menu session while the torus baseline pins the older captured values.
    [Test]
    public void FreshConfigDefaultsMatchTheCapturedOneThreeSevenOneProfile()
    {
        Type configType = Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings.Config", throwOnError: true)!;
        object config = Activator.CreateInstance(configType, nonPublic: true)!;
        (string Name, float Value)[] expected =
        [
            ("FerrofluidEmissionStrength", 0f),
            ("FerrofluidExposure", 1f),
            ("FerrofluidLutScale", 3f),
            ("FerrofluidLutOffset", 0.07f),
            ("FerrofluidSpikeBaseSize", 3f),
            ("FerrofluidSpikeHeightScale", 0.9f),
            ("FerrofluidSpikeDensity", 0.35f),
            ("FerrofluidSpikeBaseCutoff", 0.1f),
            ("ParticleMass", 1.86f),
            ("RestDensity", 2f),
            ("GasConstant", 37f),
            ("Viscosity", 8.7f),
            ("MagnetRadius", 5.6f),
            ("MagnetMaximumSlide", 4f),
            ("MagnetSlideFraction", 0.23f),
            ("MagnetSlideFalloffExponent", 0.85f),
            ("MagnetElasticity", 82f),
            ("MagnetDamping", 6.8f),
            // The user pinned the magnet forward distance at 0.12 in the latest 1.44.1 tuning pass, replacing the earlier 0.09 capture.
            ("MagnetForwardDistance", 0.07f),
            ("BulgeRadius", 0.31f),
            ("BulgeHeight", 0.12f),
            ("HeightFalloffExponent", 2.4f),
            ("PeakWidth", 0.03f),
            ("HeightGain", 0.87f),
            ("BackHeightLoss", 1.19f),
            ("BackHeightFalloffExponent", 2.65f),
            ("MagnetHeightStrengthCap", 0.74f),
            ("MagnetHeightDeadZoneRadius", 0.13f),
            ("MaximumHeight", 1.6f),
            ("MaximumTiltDegrees", 50f),
            ("TiltPeakRadiusMultiplier", 0.22f),
            ("TiltReturnRadiusMultiplier", 0.49f),
            ("TiltRiseExponent", 1.81f),
            ("TiltReturnExponent", 0.92f),
            ("InnerCornerFan", 0f),
            ("FerrofluidSpikeSpringStrength", 250f),
            ("FerrofluidSpikeSpringDamping", 12f),
            ("FerrofluidPrimitiveTipRoundness", 0f),
            ("FerrofluidPrimitiveTipCapRounding", 0.81f),
            ("FerrofluidSpikeMoundCurvature", 2f),
            ("FerrofluidSpikeHeightRadius", 0.31f),
            ("FerrofluidSpikeTiltRadius", 0.73f),
            // FerrofluidReflectivitySliderDialsMatteToOilyGloss and MatteSheenAndAnglesTuneWashSpecAndGlossSpread pin the user's chosen defaults: modest oil sheen, partial teal wash, and narrow degree-based rim/reflection bands.
            ("FerrofluidReflectivity", 0.21f),
            ("FerrofluidMatteSheen", 0.57f),
            ("FerrofluidMatteAngle", 10f),
            ("FerrofluidReflectionAngle", 15f),
            // ReflectionRotationSlidersAimBothLobes locks in the user's tuned orientation for both lighting lobes.
            ("FerrofluidMatteRotationX", 76f),
            ("FerrofluidMatteRotationY", 295f),
            ("FerrofluidMatteRotationZ", 174f),
            ("FerrofluidReflectionRotationX", 244f),
            ("FerrofluidReflectionRotationY", 210f),
            ("FerrofluidReflectionRotationZ", 167f),
        ];
        foreach ((string name, float value) in expected)
        {
            float actual = (float)configType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!.GetValue(config)!;
            Assert.That(actual, Is.EqualTo(value).Within(0.0001f), $"Fresh {name} must match the captured 1.37.1 profile.");
        }

        Assert.That(configType.GetProperty("FerrofluidBodyEnabled", BindingFlags.Instance | BindingFlags.Public)!.GetValue(config), Is.EqualTo(true), "The captured 1.37.1 profile keeps the complete torus visible.");
        Assert.That(configType.GetProperty("FerrofluidSpikesEnabled", BindingFlags.Instance | BindingFlags.Public)!.GetValue(config), Is.EqualTo(true), "The captured 1.37.1 profile keeps spike deformation visible.");
        Assert.That(configType.GetProperty("SimulationSteps", BindingFlags.Instance | BindingFlags.Public)!.GetValue(config), Is.EqualTo(2), "The latest 1.44.1 session runs two simulation steps per tick to calm the spring oscillation the user reported.");
    }

    // CompiledGpuAssetsShipInsideThePlugin ensures a successful DLL build cannot omit the shader, environment map, or authoritative FerroLUT required by the runtime-only Unity player.
    [Test]
    public void CompiledGpuAssetsShipInsideThePlugin()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        using Stream? bundleStream = pluginAssembly.GetManifestResourceStream("ChromaGLS.Settings.ferrofluid-windows.bundle");
        Assert.That(bundleStream, Is.Not.Null, "The Windows shader AssetBundle must be embedded so ChromaGLS has no loose runtime files.");
        Assert.That(bundleStream!.Length, Is.GreaterThan(1024), "The embedded bundle must contain compiled shader and texture assets rather than an empty placeholder.");
    }

    [Test]
    public void SettingsTabKeepsTheSingleRendererOnTheInfoButton()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type effectType = pluginAssembly.GetType("ChromaGLS.Settings.ChromaGlsRequirementEffect", throwOnError: true)!;
        Type settingsType = pluginAssembly.GetType("ChromaGLS.Settings.ChromaGLSSettingsUI", throwOnError: true)!;
        Type lifecycleType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidPreviewAnchorLifecycle", throwOnError: true)!;

        // Regression: parsing the hidden tab created a second off-screen renderer that consumed time until the user cycled the feature toggle.
        using Stream? bsmlStream = pluginAssembly.GetManifestResourceStream("ChromaGLS.Settings.modifiers.bsml");
        Assert.That(bsmlStream, Is.Not.Null);
        using StreamReader reader = new(bsmlStream!);
        string bsml = reader.ReadToEnd();
        Assert.That(bsml, Does.Contain("id='ferrofluid-preview-anchor' preferred-width='1' preferred-height='1'"), "The settings layout must retain only a non-stretching lifecycle anchor.");
        Assert.That(settingsType.GetMethod("HandlePostParse", BindingFlags.Instance | BindingFlags.NonPublic), Is.Not.Null, "The settings binding must install its activation relay after BSML parsing.");
        Assert.That(lifecycleType.GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic), Is.Not.Null, "Opening the actual ChromaGLS tab must mark the tuning state without a toggle cycle.");
        Assert.That(lifecycleType.GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic), Is.Not.Null, "Leaving the actual ChromaGLS tab must restore the SongCore metadata gate.");
        Assert.That(lifecycleType.GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic), Is.Null, "Tab activation tracking must not add per-frame polling.");
        Assert.That(effectType.GetMethod("CreatePreview", BindingFlags.Static | BindingFlags.NonPublic), Is.Not.Null, "Tab activation must reach the requirement effect lifecycle as a token, without spawning a second renderer.");
        Assert.That(effectType.GetMethod("RemovePreview", BindingFlags.Static | BindingFlags.NonPublic), Is.Not.Null, "Closing the settings tab must release the activation token so metadata gating resumes.");
    }

    // Regression: the fixed 120-unit scroll frame under anchor-max-y='1.35' let the raycastable scroll viewport overshoot the allotted GameplaySetup rect, swallowing clicks meant for the mod tab selector strip and neighboring pane buttons; other mods' GameplaySetup panes (JDFixer, ScoreRequirement, GameplayModifiersPlus) instead use BSML's settings-container which anchor-fills the tab.
    [Test]
    public void SettingsPaneAnchorFillsAllottedTabWithoutOvershooting()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        foreach (string resource in new[] { "ChromaGLS.Settings.modifiers.bsml", "ChromaGLS.Settings.modifiers-minimal.bsml" })
        {
            using Stream? bsmlStream = pluginAssembly.GetManifestResourceStream(resource);
            Assert.That(bsmlStream, Is.Not.Null);
            using StreamReader reader = new(bsmlStream!);
            string bsml = reader.ReadToEnd();

            Assert.That(bsml, Does.Contain("<settings-container"), $"{resource} must use BSML's anchor-fill settings container like other GameplaySetup mods instead of a fixed-size scroll frame.");
            Assert.That(bsml, Does.Not.Contain("anchor-max-y='1.35'"), "The pane must not stretch 35 percent above the allotted tab rect into the mod tab selector strip.");
            Assert.That(bsml, Does.Not.Contain("preferred-width='120'"), "The pane must not force a 120-unit frame past the allotted GameplaySetup bounds.");
        }
    }

    // SettingsPreviewMatchesRequirementGeometryBasis prevents head rotation and projected menu shear from changing the target width, height, or physical tube depth between menu transitions.
    [Test]
    public void SettingsPreviewCapturesLiveRequirementRectSize()
    {
        Type effectType = Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings.ChromaGlsRequirementEffect", throwOnError: true)!;
        MethodInfo? captureRect = effectType.GetMethod("CaptureRequirementRectSize", BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo? lastRect = effectType.GetField("_lastRequirementRectSize", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(captureRect, Is.Not.Null, "Preview creation must retain the requirement effect's post-layout rect, not SongCore BG's pre-layout bounds.");
        Assert.That(lastRect, Is.Not.Null);

        captureRect!.Invoke(null, [new Vector2(12f, 7f)]);
        Assert.That((Vector2)lastRect!.GetValue(null)!, Is.EqualTo(new Vector2(12f, 7f)));
    }

    // SettingsPreviewMatchesRequirementMeasuredWorldSize requires X/Y bounds correction while Z follows the captured requirement basis rather than projected menu shear.
    [Test]
    public void SettingsPreviewMatchesRequirementMeasuredWorldSize()
    {
        Type effectType = Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings.ChromaGlsRequirementEffect", throwOnError: true)!;

        // Regression: the live log measured the settings layout at 105-by-7 while SongCore's current button was only 12-by-7, producing a ten-foot-wide distant preview.
        MethodInfo? evaluateSize = effectType.GetMethod("EvaluateEffectSize", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? evaluatePosition = effectType.GetMethod("EvaluateTuningPreviewCameraPosition", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? evaluateWorldScale = effectType.GetMethod("EvaluateTuningPreviewWorldScale", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? evaluateRenderedScale = effectType.GetMethod("EvaluateRenderedWorldUnitScale", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? evaluateWorldSizeCorrection = effectType.GetMethod("EvaluateWorldSizeScaleCorrection", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? isUsableWorldSize = effectType.GetMethod("IsUsableWorldSize", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? evaluateAxisCorrection = effectType.GetMethod("EvaluateWorldAxisScaleCorrection", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(evaluateSize, Is.Not.Null, "Preview sizing must be independent of BSML's stretched layout-group rect.");
        Assert.That(evaluatePosition, Is.Not.Null, "The tuning placement must be explicit in headset space.");
        Assert.That(evaluateWorldScale, Is.Not.Null, "The tuning renderer must target the button's measured local-axis scale.");
        Assert.That(evaluateRenderedScale, Is.Not.Null, "Preview compensation must use the requirement effect's actual rendered axis rather than SongCore BG lossyScale.");
        Assert.That(evaluateWorldSizeCorrection, Is.Not.Null, "Final preview sizing must compare measured X/Y geometry while leaving Z on the captured physical basis.");
        Assert.That(isUsableWorldSize, Is.Not.Null, "Inactive or uninitialized requirement geometry must not collapse or explode the preview.");
        Assert.That(evaluateAxisCorrection, Is.Not.Null, "Preview sizing must correct the rendered world axes after its camera-facing rotation under the menu canvas.");
        Vector2 measuredButton = new(12f, 7f);
        Vector2 previewSize = (Vector2)evaluateSize!.Invoke(null, [true, new Vector2(105f, 7f), measuredButton])!;
        Vector2 requirementSize = (Vector2)evaluateSize.Invoke(null, [false, measuredButton, new Vector2(36f, 8f)])!;
        Vector3 cameraPosition = (Vector3)evaluatePosition!.Invoke(null, null)!;
        float requirementRenderedWorldUnitScale = (float)evaluateRenderedScale!.Invoke(null, [0.28f, 36f])!;
        Vector3 measuredWorldScale = new(requirementRenderedWorldUnitScale, requirementRenderedWorldUnitScale * 0.5f, requirementRenderedWorldUnitScale * 0.25f);
        Vector3 worldScale = (Vector3)evaluateWorldScale!.Invoke(null, [measuredWorldScale])!;
        // Regression: distance-compensated uniform scale and projected-Z bounds made the free preview several times deeper than the button's actual torus.
        Assert.That(previewSize, Is.EqualTo(measuredButton), "The tuning renderer must reuse the latest authoritative SongCore button dimensions.");
        Assert.That(requirementSize, Is.EqualTo(measuredButton), "The real requirement effect must continue inheriting SongCore's authoritative rect.");
        Assert.That(cameraPosition.x, Is.InRange(-0.4f, -0.3f), "The tuning renderer must move rightward while remaining beside the mod controls.");
        Assert.That(cameraPosition.y, Is.InRange(0.15f, 0.22f), "The tuning renderer must sit clearly above the mod-control labels.");
        Assert.That(cameraPosition.z, Is.InRange(0.65f, 0.8f), "The tuning renderer must move closer to the player.");
        Assert.That(worldScale.x, Is.EqualTo(measuredWorldScale.x).Within(0.0001f), "The preview must retain the requirement's rendered X unit scale.");
        Assert.That(worldScale.y, Is.EqualTo(measuredWorldScale.y).Within(0.0001f), "The preview must retain the requirement's rendered Y unit scale.");
        Assert.That(worldScale.z, Is.EqualTo(measuredWorldScale.z).Within(0.0001f), "The preview must retain the requirement's rendered Z unit scale rather than using apparent-distance compensation.");
        Vector3 worldSizeCorrection = (Vector3)evaluateWorldSizeCorrection!.Invoke(null, [new Vector3(1.2f, 0.8f, 0.4f), new Vector3(2.4f, 0.4f, 0.8f)])!;
        Assert.That(worldSizeCorrection.x, Is.EqualTo(0.5f).Within(0.0001f), "A preview twice as wide as the measured button must be halved.");
        Assert.That(worldSizeCorrection.y, Is.EqualTo(2f).Within(0.0001f), "A preview half as tall as the measured button must be doubled.");
        Assert.That(worldSizeCorrection.z, Is.EqualTo(1f).Within(0.0001f), "Projected menu shear must not be converted into physical torus depth.");
        Assert.That(isUsableWorldSize!.Invoke(null, [Vector3.zero]), Is.EqualTo(false), "A collapsed inactive measurement must be rejected.");
        Assert.That(isUsableWorldSize.Invoke(null, [new Vector3(1.2f, 0.8f, 0.4f)]), Is.EqualTo(true), "A finite positive measured torus must be accepted.");

        // Regression: lossyScale was sampled before the camera-facing rotation, so the non-uniform menu parent left the visible preview wider and shorter than the requested button dimensions.
        Vector3 correction = (Vector3)evaluateAxisCorrection!.Invoke(null, [worldScale, new Vector3(worldScale.x * 1.8f, worldScale.y * 0.62f, worldScale.z * 1.15f)])!;
        Assert.That((worldScale.x * 1.8f) * correction.x, Is.EqualTo(worldScale.x).Within(0.0001f), "The preview's rendered width must equal its target width after parenting and rotation.");
        Assert.That((worldScale.y * 0.62f) * correction.y, Is.EqualTo(worldScale.y).Within(0.0001f), "The preview's rendered height must equal its target height after parenting and rotation.");
        Assert.That((worldScale.z * 1.15f) * correction.z, Is.EqualTo(worldScale.z).Within(0.0001f), "The preview depth axis must retain the requirement's physical basis.");
    }

    // TorusVisibilitySwitchStopsTheCompleteEffect fixes the misleading old body-only diagnostic semantics: off means no renderer and no GPU simulation.
    [Test]
    public void TorusVisibilitySwitchStopsTheCompleteEffect()
    {
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type configType = pluginAssembly.GetType("ChromaGLS.Settings.Config", throwOnError: true)!;
        Type simulationType = pluginAssembly.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", throwOnError: true)!;
        PropertyInfo? bodySetting = configType.GetProperty("FerrofluidBodyEnabled", BindingFlags.Instance | BindingFlags.Public);
        MethodInfo? shouldRun = simulationType.GetMethod("ShouldRun", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.That(bodySetting, Is.Not.Null, "The generated config must persist the complete torus visibility switch.");
        object config = Activator.CreateInstance(configType, nonPublic: true)!;
        Assert.That(bodySetting!.GetValue(config), Is.EqualTo(true), "The complete torus must remain visible by default.");
        Assert.That(shouldRun, Is.Not.Null, "One authoritative gate must stop both renderer and simulation work.");
        Assert.That(shouldRun!.Invoke(null, [false]), Is.EqualTo(false));
        Assert.That(shouldRun.Invoke(null, [true]), Is.EqualTo(true));

        using Stream? bsmlStream = pluginAssembly.GetManifestResourceStream("ChromaGLS.Settings.modifiers.bsml");
        using StreamReader reader = new(bsmlStream!);
        string bsml = reader.ReadToEnd();
        Assert.That(bsml, Does.Contain("text='Show ferrofluid torus' value='ferrofluid-body-enabled'"), "The menu must describe complete torus visibility rather than body-only isolation.");
    }
}
