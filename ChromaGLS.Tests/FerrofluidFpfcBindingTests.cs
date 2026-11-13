using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class FerrofluidFpfcBindingTests
{
    private static bool TweaksEnabled
    {
        get
        {
            FieldInfo? flag = GetPluginType("ChromaGLS.Settings.ChromaGLSSettingsUI")
                .GetField("FerrofluidTweakMenuEnabled", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(flag, Is.Not.Null, "ChromaGLSSettingsUI.FerrofluidTweakMenuEnabled must exist as the build flag.");
            return (bool)flag!.GetRawConstantValue()!;
        }
    }

    [Test]
    public void KeyboardEmulationExistsExactlyInTweakBuilds()
    {
        bool tweaks = TweaksEnabled;
        Type magnet = GetPluginType("ChromaGLS.Settings.FerrofluidMagnetController");
        Type physics = GetPluginType("ChromaGLS.Settings.FerrofluidGrabPhysics");
        BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        Assembly plugin = Assembly.Load("ChromaGLS");

        Assert.That(
            physics.GetMethod("EvaluateControllerKeyVector", all) != null,
            Is.EqualTo(tweaks),
            "EvaluateControllerKeyVector must exist only in FERROFLUID_TWEAKS builds.");
        Assert.That(magnet.GetMethod("SampleRoleKeys", all) != null, Is.EqualTo(tweaks));
        Assert.That(magnet.GetMethod("IsFpfc", all) != null, Is.EqualTo(tweaks));
        if (!tweaks)
        {
            Assert.That(magnet.GetMethod("ResolveFpfc", all), Is.Null, "The legacy FPFC discovery must never compile without FERROFLUID_TWEAKS.");
            Assert.That(magnet.GetField("_fpfc", all), Is.Null, "The legacy FPFC cache must never compile without FERROFLUID_TWEAKS.");
        }
        Assert.That(
            plugin.GetType("ChromaGLS.HarmonyPatches.FerrofluidFpfcTogglePatch", throwOnError: false) != null,
            Is.EqualTo(tweaks),
            "The FPFC toggle patch must only exist in tweak builds.");
        Assert.That(
            plugin.GetType("ChromaGLS.HarmonyPatches.FerrofluidKeyboardKeyCodePatch", throwOnError: false) != null,
            Is.EqualTo(tweaks));
        Assert.That(
            plugin.GetType("ChromaGLS.HarmonyPatches.FerrofluidKeyboardKeyNamePatch", throwOnError: false) != null,
            Is.EqualTo(tweaks));
        Assert.That(
            plugin.GetType("ChromaGLS.Settings.FerrofluidKeyboardInput", throwOnError: false) != null,
            Is.EqualTo(tweaks),
            "The raw-key helper must only exist in tweak builds.");

        List<string> keyCalls = FindInputKeyCalls(plugin);
        Assert.That(
            keyCalls.Count > 0,
            Is.EqualTo(tweaks),
            tweaks
                ? "The tweak build must compile the FPFC key reads; the same scan finding none means it missed real instructions."
                : $"The normal binary must not contain any UnityEngine.Input key reads; found {string.Join(", ", keyCalls)}.");
    }

    [Test]
    public void FpfcTogglePrefixSuppressesReservedKeys()
    {
        if (!TweaksEnabled)
            Assert.Ignore("Normal builds do not compile the FPFC toggle patch.");

        MethodInfo prefix = GetPluginType("ChromaGLS.HarmonyPatches.FerrofluidFpfcTogglePatch")
            .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new AssertionException("FerrofluidFpfcTogglePatch must expose a Prefix.");
        Type controllerType = SiraUtilType("SiraUtil.Tools.FPFC.FPFCSettingsController");

        foreach ((object key, bool expected) in new (object, bool)[]
        {
            (Enum.Parse(typeof(KeyCode), "G"), false),
            (Enum.Parse(typeof(KeyCode), "H"), false),
            (Enum.Parse(typeof(KeyCode), "T"), false),
            (Enum.Parse(typeof(KeyCode), "Z"), false),
            (Enum.Parse(typeof(KeyCode), "U"), true),
            (Enum.Parse(typeof(KeyCode), "E"), true),
            (Enum.Parse(typeof(KeyCode), "None"), true),
        })
        {
            object controller = CreateFpfcController(controllerType, key);
            Assert.That(
                (bool)prefix.Invoke(null, [controller])!,
                Is.EqualTo(expected),
                $"Prefix must suppress every reserved ferro key and allow unreserved remaps like {key}.");
        }
    }

    [Test]
    public void ReservedKeySetMatchesTheTenFerroBindings()
    {
        if (!TweaksEnabled)
            Assert.Ignore("Normal builds do not compile the keyboard helper.");

        MethodInfo reserved = GetPluginType("ChromaGLS.Settings.FerrofluidKeyboardInput")
            .GetMethod("IsReservedKey", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new AssertionException("FerrofluidKeyboardInput.IsReservedKey must exist.");

        foreach (KeyCode key in new[]
        {
            KeyCode.T, KeyCode.F, KeyCode.G, KeyCode.H, KeyCode.I,
            KeyCode.J, KeyCode.K, KeyCode.L, KeyCode.Z, KeyCode.X,
        })
        {
            Assert.That((bool)reserved!.Invoke(null, [key])!, Is.True, $"{key} is a ferro binding and must be reserved.");
        }

        foreach (KeyCode key in new[]
        {
            KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.Q, KeyCode.E,
            KeyCode.R, KeyCode.U, KeyCode.Escape, KeyCode.LeftControl, KeyCode.LeftShift,
            KeyCode.Space, KeyCode.Keypad5, KeyCode.None, KeyCode.F5,
        })
        {
            Assert.That((bool)reserved!.Invoke(null, [key])!, Is.False, $"{key} must stay unreserved so other mods keep their binds.");
        }
    }

    [Test]
    public void ReservedKeyNamesAreCaseInsensitiveAndLeaveOthersAlone()
    {
        if (!TweaksEnabled)
            Assert.Ignore("Normal builds do not compile the keyboard helper.");

        MethodInfo reservedName = GetPluginType("ChromaGLS.Settings.FerrofluidKeyboardInput")
            .GetMethod("IsReservedKeyName", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new AssertionException("FerrofluidKeyboardInput.IsReservedKeyName must exist.");

        foreach (string name in new[] { "t", "T", "f", "F", "g", "G", "h", "H", "i", "I", "j", "J", "k", "K", "l", "L", "z", "Z", "x", "X" })
        {
            Assert.That((bool)reservedName!.Invoke(null, [name])!, Is.True, $"\"{name}\" must resolve to a reserved key.");
        }

        foreach (string name in new[] { "w", "W", "u", "space", "escape", "f5", "left ctrl", "mouse 0", "keypad5", "" })
        {
            Assert.That((bool)reservedName!.Invoke(null, [name])!, Is.False, $"\"{name}\" must forward untouched.");
        }
    }

    [Test]
    public void PatchedKeyWrappersMaskReservedButRawReadStillWorks()
    {
        if (!TweaksEnabled)
            Assert.Ignore("Normal builds do not compile the keyboard ownership patches.");

        RegisterKeyIcallStubs();
        MethodInfo? getKeyInt = typeof(Input).GetMethod("GetKeyInt", BindingFlags.Static | BindingFlags.NonPublic);
        bool stubBound = false;
        try
        {
            stubBound = getKeyInt?.Invoke(null, [KeyCode.W]) is true;
        }
        catch (TargetInvocationException)
        {
        }

        Type codePatch = GetPluginType("ChromaGLS.HarmonyPatches.FerrofluidKeyboardKeyCodePatch");
        Type namePatch = GetPluginType("ChromaGLS.HarmonyPatches.FerrofluidKeyboardKeyNamePatch");
        MethodInfo rawRead = GetPluginType("ChromaGLS.Settings.FerrofluidKeyboardInput")
            .GetMethod("GetKey", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new AssertionException("FerrofluidKeyboardInput.GetKey must exist as the raw bypass.");

        var harmony = new Harmony("dev.chromagls.keymask-test");
        try
        {
            harmony.CreateClassProcessor(codePatch).Patch();
            harmony.CreateClassProcessor(namePatch).Patch();

            Assert.That(Input.GetKey(KeyCode.T), Is.False, "Reserved T must read false for other managed callers.");
            Assert.That(Input.GetKeyDown(KeyCode.G), Is.False, "Reserved G must mask GetKeyDown too.");
            Assert.That(Input.GetKeyUp(KeyCode.X), Is.False, "Reserved X must mask GetKeyUp too.");
            Assert.That(Input.GetKey("h"), Is.False, "Reserved string name must mask string overloads.");
            Assert.That(Input.GetKeyDown("z"), Is.False);

            if (!stubBound)
            {
                Assert.Throws<MissingMethodException>(
                    () => Input.GetKey(KeyCode.W),
                    "Unreserved W must still reach the managed wrapper's native read; the stub just cannot satisfy it in this host.");
                Assert.Inconclusive("The mono host did not bind the GetKeyInt icall stub, so raw-read and stubbed-forward assertions could not run.");
            }

            Assert.That(Input.GetKey(KeyCode.W), Is.True, "Unreserved keys must forward to the real read; the stub reports W held.");
            Assert.That(Input.GetKey(KeyCode.A), Is.False, "The stub reports A unheld, so forwarding must preserve its false too.");

            Assert.That(
                (bool)rawRead!.Invoke(null, [KeyCode.T])!,
                Is.True,
                "The raw bypass must still see T held while the managed wrapper reports false.");
        }
        finally
        {
            harmony.UnpatchSelf();
        }
    }

    [Test]
    public void PatchedTickLeavesEnabledUntouchedForDefaultG()
    {
        if (!TweaksEnabled)
            Assert.Ignore("Normal builds do not compile the FPFC toggle patch.");

        Type patchType = GetPluginType("ChromaGLS.HarmonyPatches.FerrofluidFpfcTogglePatch");
        Type controllerType = SiraUtilType("SiraUtil.Tools.FPFC.FPFCSettingsController");
        MethodInfo tick = controllerType.GetMethod("Tick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new AssertionException("SiraUtil FPFCSettingsController.Tick must exist.");
        object controller = CreateFpfcController(controllerType, Enum.Parse(typeof(KeyCode), "G"));
        object enabledBefore = ReadMember(controller, "Enabled")!;

        var harmony = new Harmony("dev.chromagls.fpfc-test");
        try
        {
            harmony.CreateClassProcessor(patchType).Patch();
            tick.Invoke(controller, null);
        }
        finally
        {
            harmony.UnpatchSelf();
        }

        Assert.That(
            ReadMember(controller, "Enabled"),
            Is.EqualTo(enabledBefore),
            "With the default G bind suppressed, Tick must return before the original toggle runs.");

        object hController = CreateFpfcController(controllerType, Enum.Parse(typeof(KeyCode), "H"));
        try
        {
            harmony.CreateClassProcessor(patchType).Patch();
            tick.Invoke(hController, null);
        }
        finally
        {
            harmony.UnpatchSelf();
        }

        Assert.That(
            ReadMember(hController, "Enabled"),
            Is.EqualTo(enabledBefore),
            "An H toggle bind must stay suppressed too since H is a reserved ferro key.");
    }

    [Test]
    public void PersistentPatchTypesOwnsFpfcPatchInTweakBuildOnly()
    {
        bool tweaks = TweaksEnabled;
        FieldInfo persistent = GetPluginType("ChromaGLS.Plugin")
            .GetField("PersistentPatchTypes", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new AssertionException("Plugin.PersistentPatchTypes must exist.");
        var types = (Type[])persistent.GetValue(null)!;
        foreach (string name in new[] { "FerrofluidFpfcTogglePatch", "FerrofluidKeyboardKeyCodePatch", "FerrofluidKeyboardKeyNamePatch" })
        {
            Assert.That(
                types.Any(t => t.Name == name),
                Is.EqualTo(tweaks),
                $"{name} must be a persistent patch in tweak builds and absent otherwise.");
        }
    }

    private delegate bool KeyIntDelegate(KeyCode key);

    private static readonly KeyIntDelegate KeyIntStub = key => key is KeyCode.T or KeyCode.G or KeyCode.Z or KeyCode.X or KeyCode.H or KeyCode.W;
    private static bool _keyIcallsRegistered;

    [System.Runtime.InteropServices.DllImport("mono-2.0-sgen.dll")]
    private static extern void mono_add_internal_call(string name, IntPtr method);

    private static void RegisterKeyIcallStubs()
    {
        if (_keyIcallsRegistered)
            return;

        _keyIcallsRegistered = true;
        IntPtr stub = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(KeyIntStub);
        mono_add_internal_call("UnityEngine.Input::GetKeyInt", stub);
        mono_add_internal_call("UnityEngine.Input::GetKeyDownInt", stub);
        mono_add_internal_call("UnityEngine.Input::GetKeyUpInt", stub);
    }

    private static object CreateFpfcController(Type controllerType, object toggleKey)
    {
        FieldInfo optionsField = controllerType.GetField("_fpfcOptions", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertionException("SiraUtil FPFCSettingsController must keep its _fpfcOptions field.");
        object options = FormatterServices.GetUninitializedObject(optionsField.FieldType);
        WriteMember(options, "ToggleKeyCode", toggleKey);
        object controller = FormatterServices.GetUninitializedObject(controllerType);
        optionsField.SetValue(controller, options);
        return controller;
    }

    private static object? ReadMember(object instance, string name)
    {
        Type type = instance.GetType();
        FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
            return field.GetValue(instance);

        PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(property, Is.Not.Null, $"{type.Name} must expose {name}.");
        return property!.GetValue(instance);
    }

    private static void WriteMember(object instance, string name, object value)
    {
        Type type = instance.GetType();
        FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
        {
            field.SetValue(instance, value);
            return;
        }

        PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(property?.CanWrite ?? false, Is.True, $"{type.Name} must let tests set {name}.");
        property!.SetValue(instance, value);
    }

    private static List<string> FindInputKeyCalls(Assembly assembly)
    {
        var hits = new List<string>();
        using AssemblyDefinition definition = AssemblyDefinition.ReadAssembly(assembly.Location);
        var types = new Stack<TypeDefinition>(definition.MainModule.Types);
        while (types.Count > 0)
        {
            TypeDefinition type = types.Pop();
            foreach (TypeDefinition nested in type.NestedTypes)
                types.Push(nested);

            foreach (MethodDefinition method in type.Methods)
            {
                if (!method.HasBody)
                    continue;

                foreach (Instruction instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                        continue;

                    if (instruction.Operand is MethodReference callee
                        && callee.DeclaringType.FullName == "UnityEngine.Input"
                        && (callee.Name == "GetKey" || callee.Name == "GetKeyDown" || callee.Name == "GetKeyUp" || callee.Name == "GetKeyInt"))
                        hits.Add($"{type.Name}::{method.Name}->{callee.Name}");
                }
            }
        }

        return hits;
    }

    private static Type SiraUtilType(string name)
    {
        return Assembly.Load("SiraUtil").GetType(name, throwOnError: false)
            ?? throw new AssertionException($"Missing SiraUtil type: {name}");
    }

    private static Type GetPluginType(string typeName)
    {
        return Assembly.Load("ChromaGLS").GetType(typeName, throwOnError: false)
            ?? throw new AssertionException($"Missing plugin type: {typeName}");
    }
}
