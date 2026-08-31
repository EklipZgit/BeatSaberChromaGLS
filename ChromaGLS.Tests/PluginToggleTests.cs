using System;
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
        Assembly pluginAssembly = Assembly.Load("ChromaGLS");
        Type pluginType = pluginAssembly.GetType("ChromaGLS.Plugin", throwOnError: true)!;
        object plugin = RuntimeHelpers.GetUninitializedObject(pluginType);
        Harmony harmony = new(HarmonyId);

        // Construct only the lifecycle state needed by SetEnabled so this test exercises the real plugin patch/unpatch implementation without starting Beat Saber.
        SetField(pluginType, plugin, "_harmonyInstance", harmony);
        SetField(pluginType, plugin, "_patchesApplied", false);
        SetField(pluginType, null, "_instance", plugin);
        MethodInfo setEnabled = pluginType.GetMethod("SetEnabled", BindingFlags.Static | BindingFlags.NonPublic)!;

        try
        {
            setEnabled.Invoke(null, [true]);
            MethodBase[] initiallyPatchedMethods = GetOwnedPatchedMethods();
            Assert.That(initiallyPatchedMethods, Is.Not.Empty, "The initial enable must install ChromaGLS's Harmony patches.");

            setEnabled.Invoke(null, [false]);
            Assert.That(GetOwnedPatchedMethods(), Is.Empty, "Disabling must remove every ChromaGLS Harmony patch.");

            setEnabled.Invoke(null, [true]);
            MethodBase[] reappliedMethods = GetOwnedPatchedMethods();

            // Regression: toggling ChromaGLS off and back on left GLS colors disabled until Beat Saber restarted.
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

    private static MethodBase[] GetOwnedPatchedMethods()
    {
        return Harmony.GetAllPatchedMethods()
            .Where(method => Harmony.GetPatchInfo(method)?.Owners.Contains(HarmonyId) == true)
            .ToArray();
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
