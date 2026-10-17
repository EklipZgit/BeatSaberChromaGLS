using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

// TubeBloomReflectionColorTests reproduces the 1.40.8 reflection bounce retaining an earlier GLS color:
// Beat Saber's nested SetActive writes the bounce color only when active state changes, so ChromaGLS must
// supply the current main-light color on every active frame rather than waiting for reactivation.
[TestFixture]
[NonParallelizable]
internal sealed class TubeBloomReflectionColorTests
{
    private const string HarmonyId = "com.eklipz.ChromaGLS";
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    private Harmony harmony = null!;
    private MethodInfo setEnabled = null!;
    private Type pluginType = null!;

    [SetUp]
    public void SetUp()
    {
        if (Type.GetType("Mono.Runtime") == null)
        {
            Assert.Ignore("Harmony detour verification requires Unity's Mono runtime.");
        }

        harmony = new Harmony(HarmonyId);
        Assembly plugin = Assembly.Load("ChromaGLS");
        pluginType = plugin.GetType("ChromaGLS.Plugin", true)!;
        object instance = TestHostCompat.GetUninitializedObject(pluginType);
        pluginType.GetField("_harmonyInstance", All)!.SetValue(instance, harmony);
        pluginType.GetField("_patchesApplied", All)!.SetValue(instance, false);
        pluginType.GetField("_instance", All)!.SetValue(null, instance);
        setEnabled = pluginType.GetMethod("SetEnabled", All)!;
        setEnabled.Invoke(null, [true]);
    }

    [TearDown]
    public void TearDown()
    {
        setEnabled?.Invoke(null, [false]);
        harmony?.UnpatchSelf();
        pluginType?.GetField("_instance", All)!.SetValue(null, null);
    }

    // The reported red -> green -> red sequence keeps the same reflection collision active. The ChromaGLS
    // prefix must therefore copy both later colors even though Beat Saber's active-state branch does not run.
    [Test]
    public void ActiveBounceCopiesEveryIncomingColor()
    {
        MethodInfo setActive = RequireBounceMethod("SetActive");
        Patch? prefix = Harmony.GetPatchInfo(setActive)?.Prefixes.FirstOrDefault(patch => patch.owner == HarmonyId);

        Assert.That(prefix, Is.Not.Null,
            "An already-active reflection bounce has no ChromaGLS prefix and keeps its previous activation color.");

        object light = TestHostCompat.GetUninitializedObject(AccessTools.TypeByName("TubeBloomPrePassLight"));
        Type lightType = light.GetType();
        lightType.GetField("_initialized", All)!.SetValue(light, true);

        // The game's color setter unconditionally dereferences _mainEffectPostProcessEnabled through
        // ObservableVariableSO's implicit conversion, so the fabricated light needs a stand-in BoolSO
        // to satisfy the initialized-light invariant the production prefix relies on.
        lightType.GetField("_mainEffectPostProcessEnabled", All)!
            .SetValue(light, TestHostCompat.GetUninitializedObject(AccessTools.TypeByName("BoolSO")));
        FieldInfo colorField = lightType.GetField("_color", All)!;

        Color green = new(0f, 1f, 0f, 1f);
        prefix!.PatchMethod.Invoke(null, [light, true, green]);
        Assert.That((Color)colorField.GetValue(light)!, Is.EqualTo(green),
            "The active bounce must adopt the green GLS color.");

        Color red = new(1f, 0f, 0f, 1f);
        prefix.PatchMethod.Invoke(null, [light, true, red]);
        Assert.That((Color)colorField.GetValue(light)!, Is.EqualTo(red),
            "The still-active bounce must adopt the later red GLS color instead of retaining green.");
    }

    // The live 1.44.1 diagnostics showed the surface hit point keeping its first red _originalColor while
    // the tube advanced through later GLS colors; SetCollisionLength must refresh that source before the
    // native intensity setter reapplies it to the material property block.
    [Test]
    public void CollisionHitPointRefreshesSourceColorBeforeIntensity()
    {
        MethodInfo setCollisionLength = RequireBounceMethod("SetCollisionLength");
        Patch? prefix = Harmony.GetPatchInfo(setCollisionLength)?.Prefixes.FirstOrDefault(patch => patch.owner == HarmonyId);

        Assert.That(prefix, Is.Not.Null,
            "The hit point has no ChromaGLS prefix to replace its stale remembered source color.");

        object light = TestHostCompat.GetUninitializedObject(AccessTools.TypeByName("TubeBloomPrePassLight"));
        object hitPoint = TestHostCompat.GetUninitializedObject(typeof(InstancedMaterialLightWithId));
        FieldInfo tubeColorField = light.GetType().GetField("_color", All)!;
        FieldInfo originalColorField = typeof(InstancedMaterialLightWithId).GetField("_originalColor", All)!;
        Color red = new(1f, 0f, 0f, 1f);
        Color green = new(0f, 1f, 0.059f, 1f);
        tubeColorField.SetValue(light, green);
        originalColorField.SetValue(hitPoint, red);

        prefix!.PatchMethod.Invoke(null, [light, true, hitPoint, true]);

        Assert.That((Color)originalColorField.GetValue(hitPoint)!, Is.EqualTo(green),
            "The hit point must consume the current tube color instead of replaying its stale source color.");
    }

    // Mirrors the production patch's version-optional target lookup: games without the nested bounce
    // type skip the patch in HarmonyPrepare, so the tests skip instead of dereferencing a null method.
    private static MethodInfo RequireBounceMethod(string name)
    {
        Type? reflectionEffect = AccessTools.TypeByName("TubeBloomPrePassLightReflectionEffect");
        Type? bounceType = reflectionEffect == null
            ? null
            : AccessTools.Inner(reflectionEffect, "TubeBloomPrePassLightWithHitPoint");
        MethodInfo? method = bounceType == null
            ? null
            : AccessTools.Method(bounceType, name);
        if (method == null)
        {
            Assert.Ignore($"This game has no reflection-bounce {name}; the optional patch stays uninstalled.");
        }

        return method;
    }
}
