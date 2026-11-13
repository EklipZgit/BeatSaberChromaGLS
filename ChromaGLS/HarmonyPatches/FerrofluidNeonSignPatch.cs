using ChromaGLS.Settings;
using HarmonyLib;

namespace ChromaGLS.HarmonyPatches
{
    [HarmonyPatch(typeof(FlickeringNeonSign), "SetOn")]
    internal static class FerrofluidNeonSignPatch
    {
        private static void Postfix(FlickeringNeonSign __instance, bool on)
            => FerrofluidRoomReflections.NotifyNeonState(__instance, on);
    }
}
