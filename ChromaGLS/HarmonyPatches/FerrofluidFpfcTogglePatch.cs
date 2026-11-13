#if FERROFLUID_TWEAKS
using ChromaGLS.Settings;
using HarmonyLib;
using SiraUtil.Tools.FPFC;

namespace ChromaGLS.HarmonyPatches
{
    [HarmonyPatch(typeof(FPFCSettingsController), nameof(FPFCSettingsController.Tick))]
    internal static class FerrofluidFpfcTogglePatch
    {
        private static bool Prefix(FPFCSettingsController __instance)
            => !FerrofluidKeyboardInput.IsReservedKey(__instance._fpfcOptions.ToggleKeyCode);
    }
}
#endif
