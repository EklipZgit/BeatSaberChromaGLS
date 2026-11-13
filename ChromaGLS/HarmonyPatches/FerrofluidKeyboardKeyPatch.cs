#if FERROFLUID_TWEAKS
using System.Collections.Generic;
using System.Reflection;
using ChromaGLS.Settings;
using HarmonyLib;
using UnityEngine;

namespace ChromaGLS.HarmonyPatches
{
    [HarmonyPatch]
    internal static class FerrofluidKeyboardKeyCodePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return typeof(Input).GetMethod(nameof(Input.GetKey), [typeof(KeyCode)])!;
            yield return typeof(Input).GetMethod(nameof(Input.GetKeyDown), [typeof(KeyCode)])!;
            yield return typeof(Input).GetMethod(nameof(Input.GetKeyUp), [typeof(KeyCode)])!;
        }

        private static bool Prefix(KeyCode key, ref bool __result)
        {
            if (!FerrofluidKeyboardInput.IsReservedKey(key))
                return true;

            __result = false;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class FerrofluidKeyboardKeyNamePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return typeof(Input).GetMethod(nameof(Input.GetKey), [typeof(string)])!;
            yield return typeof(Input).GetMethod(nameof(Input.GetKeyDown), [typeof(string)])!;
            yield return typeof(Input).GetMethod(nameof(Input.GetKeyUp), [typeof(string)])!;
        }

        private static bool Prefix(string name, ref bool __result)
        {
            if (!FerrofluidKeyboardInput.IsReservedKeyName(name))
                return true;

            __result = false;
            return false;
        }
    }
}
#endif
