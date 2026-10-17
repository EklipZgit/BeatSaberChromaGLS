using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using NUnit.Framework;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class PluginToggleTests
{
    private const string HarmonyId = "com.eklipz.ChromaGLS";

    [Test]
    public void DisableThenEnableRestoresEveryHarmonyPatch()
    {
        // The patch cycle requires Unity Mono: its MonoMod detours must regenerate wrappers and the
        // desktop CLR cannot even load CustomJSONData's default interface members. NUnitLite runs this
        // suite under the Unity-bundled mono.exe; skip anywhere else rather than crashing the host.
        if (Type.GetType("Mono.Runtime") == null)
        {
            Assert.Ignore("Harmony patch-cycle verification requires Unity's Mono runtime.");
        }

        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type pluginType = pluginAssembly.GetType("ChromaGLS.Plugin", throwOnError: true)!;
        object plugin = TestHostCompat.GetUninitializedObject(pluginType);
        Harmony harmony = new(HarmonyId);

        // Construct only the lifecycle state needed by SetEnabled so this test exercises the real plugin patch/unpatch implementation without starting Beat Saber.
        SetField(pluginType, plugin, "_harmonyInstance", harmony);
        SetField(pluginType, plugin, "_patchesApplied", false);
        SetField(pluginType, null, "_instance", plugin);
        MethodInfo setEnabled = pluginType.GetMethod("SetEnabled", BindingFlags.Static | BindingFlags.NonPublic)!;

        // OnEnable installs the persistent annotation set before evaluating the persisted
        // toggle; the test must mirror that sequence or the double-registration crash
        // (PatchAll re-patching the persistent methods) cannot occur here.
        MethodInfo installPersistent = pluginType.GetMethod("InstallPersistentPatches", BindingFlags.Instance | BindingFlags.NonPublic)!;
        installPersistent.Invoke(plugin, null);

        try
        {
            setEnabled.Invoke(null, [true]);
            MethodBase[] initiallyPatchedMethods = GetOwnedPatchedMethods();
            Assert.That(initiallyPatchedMethods, Is.Not.Empty, "The initial enable must install ChromaGLS's Harmony patches.");
            AssertNoDuplicatePatchEntries(initiallyPatchedMethods, "after the initial enable");

            setEnabled.Invoke(null, [false]);
            MethodBase[] remainingAfterDisable = GetOwnedPatchedMethods();
            AssertNoDuplicatePatchEntries(remainingAfterDisable, "after disabling");

            // Regression: toggling ChromaGLS off and back on left GLS colors disabled until Beat Saber
            // restarted. Beat Saber caches converted BeatmapData for the whole session
            // (BeatmapDataLoader._lastUsedBeatmapDataCache and SongCore's stored beatmap-data task), so
            // stripping the load-time annotation patches lets a map deserialize and convert without any
            // ICustomData — state no later PatchAll can retroactively fix. Disabling must therefore keep
            // the deserialize/convert/unpack and ease-conversion patches installed while removing only
            // the effect patches. The required survivors are named explicitly here (rather than read
            // from the plugin's own list) so the assertion pins the intended boundary independently.
            MethodBase[] expectedPersistent = GetMethodsPatchedByTypes(RequiredPersistentPatchTypes(pluginAssembly));
            Assert.That(
                remainingAfterDisable.Select(GetMethodIdentity),
                Is.EquivalentTo(expectedPersistent.Select(GetMethodIdentity)),
                "Disabling must remove only the effect patches; the load-time data-annotation patch set must remain installed.");

            setEnabled.Invoke(null, [true]);
            MethodBase[] reappliedMethods = GetOwnedPatchedMethods();
            AssertNoDuplicatePatchEntries(reappliedMethods, "after re-enabling");

            Assert.That(
                reappliedMethods.Select(GetMethodIdentity),
                Is.EquivalentTo(initiallyPatchedMethods.Select(GetMethodIdentity)),
                "Re-enabling must restore the complete ChromaGLS patch set in the same process.");
        }
        finally
        {
            harmony.UnpatchSelf();
            SetField(pluginType, null, "_instance", null);
        }
    }

    // Harmony's PatchProcessor.Patch appends a new patch entry on every call instead of
    // deduplicating already-patched methods: installing the persistent annotation set and
    // then running PatchAll registered GlsDeserializePatch.Prefix twice, and the second
    // invocation consumed the already-advanced JsonReader and crashed map loading. The
    // owner-set assertions cannot see duplicates, so every patch kind must hold at most
    // one ChromaGLS entry per target method.
    private static void AssertNoDuplicatePatchEntries(MethodBase[] methods, string when)
    {
        foreach (MethodBase method in methods)
        {
            Patches? info = Harmony.GetPatchInfo(method);
            foreach (ReadOnlyCollection<Patch>? patches in new[]
            {
                info?.Prefixes, info?.Postfixes, info?.Transpilers, info?.Finalizers, info?.ILManipulators
            })
            {
                Assert.That(
                    patches?.Count(patch => patch.owner == HarmonyId) ?? 0,
                    Is.LessThanOrEqualTo(1),
                    $"ChromaGLS must not register duplicate patch entries on {GetMethodIdentity(method)} {when}.");
            }
        }
    }

    private static MethodBase[] GetOwnedPatchedMethods()
    {
        return Harmony.GetAllPatchedMethods()
            .Where(method => Harmony.GetPatchInfo(method)?.Owners.Contains(HarmonyId) == true)
            .ToArray();
    }

    // The session-cached-data regression only requires that these specific load-time annotation
    // patches survive the toggle; every other patch class is an effect patch the toggle may remove.
    private static Type[] RequiredPersistentPatchTypes(Assembly pluginAssembly)
    {
        Type converterPatches = pluginAssembly.GetType("ChromaGLS.HarmonyPatches.GlsConverterPatches", throwOnError: true)!;
        return new[]
        {
            pluginAssembly.GetType("ChromaGLS.HarmonyPatches.GlsDeserializePatch", throwOnError: true)!,
            converterPatches,
            converterPatches.GetNestedType("LightColorEventBoxConverterPatch", BindingFlags.NonPublic)!,
            pluginAssembly.GetType("ChromaGLS.HarmonyPatches.GlsEasingRuntimePatch", throwOnError: true)!,
            pluginAssembly.GetType("ChromaGLS.Settings.ChromaGlsRequirementSelectionPatch", throwOnError: true)!,
        };
    }

    // Resolve each required type's real target under a scratch Harmony owner so the assertion compares
    // the exact methods the plugin must keep installed while disabled, then restores prior patch state.
    // PatchClassProcessor.Patch returns the generated DMD replacements, so the originals are read back
    // from the global patch registry through the scratch owner instead.
    private static MethodBase[] GetMethodsPatchedByTypes(Type[] patchTypes)
    {
        const string scratchHarmonyId = "com.eklipz.ChromaGLS.persistent-probe";
        Harmony scratchHarmony = new(scratchHarmonyId);
        try
        {
            foreach (Type patchType in patchTypes)
            {
                scratchHarmony.CreateClassProcessor(patchType).Patch();
            }

            return Harmony.GetAllPatchedMethods()
                .Where(method => Harmony.GetPatchInfo(method)?.Owners.Contains(scratchHarmonyId) == true)
                .ToArray();
        }
        finally
        {
            scratchHarmony.UnpatchSelf();
        }
    }

    private static string GetMethodIdentity(MethodBase method)
    {
        return $"{method.DeclaringType?.AssemblyQualifiedName}::{method}";
    }

    private static void SetField(Type pluginType, object? instance, string name, object? value)
    {
        FieldInfo field = pluginType.GetField(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(pluginType.FullName, name);
        field.SetValue(instance, value);
    }
}
