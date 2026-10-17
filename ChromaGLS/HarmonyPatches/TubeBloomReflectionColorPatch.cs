using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ChromaGLS.HarmonyPatches
{
    // Beat Saber refreshes reflection colors only when their active state changes.
    // An already active reflection would otherwise keep an earlier GLS color.
    [HarmonyPatch]
    internal static class TubeBloomReflectionColorPatch
    {
        internal static MethodBase? ResolveBounceMethod(string methodName)
        {
            Type? reflectionEffect = AccessTools.TypeByName("TubeBloomPrePassLightReflectionEffect");
            Type? bounceType = reflectionEffect == null
                ? null
                : AccessTools.Inner(reflectionEffect, "TubeBloomPrePassLightWithHitPoint");
            return bounceType == null
                ? null
                : AccessTools.Method(bounceType, methodName);
        }

        // Older Beat Saber versions have no reflection effect. Skip this patch so GLS lighting can still load.
        [HarmonyPrepare]
        private static bool Prepare() => TargetMethod() != null;

        // The renderer holder is a private nested type, so resolve it by name when installing the patch.
        [HarmonyTargetMethod]
        private static MethodBase? TargetMethod() => ResolveBounceMethod("SetActive");

        // Refresh the color before the native active-state check can skip it.
        [HarmonyPrefix]
        private static void Prefix(TubeBloomPrePassLight ___light, bool enabled, Color mainLightColor)
        {
            if (!enabled)
            {
                return;
            }

            ___light.color = mainLightColor;
        }
    }

#if !PRE_V1_37_1
    // Earlier versions have no _originalColor field or reflection hit-point effect.
    // The runway hit-point material also caches its own source color.
    [HarmonyPatch]
    internal static class TubeBloomReflectionHitPointPatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => TargetMethod() != null;

        // Update the cached color before the native intensity setter copies it to the material property block.
        [HarmonyPrefix]
        private static void Prefix(
            TubeBloomPrePassLight ___light,
            bool ___showHitPoint,
            InstancedMaterialLightWithId ___hitPointLightWithId,
            bool rayHitGeometry)
        {
            if (!rayHitGeometry || !___showHitPoint)
            {
                return;
            }

            ___hitPointLightWithId._originalColor = ___light.color;
        }

        // SetCollisionLength reapplies intensity after this prefix, updating the material in the same frame.
        [HarmonyTargetMethod]
        private static MethodBase? TargetMethod() => TubeBloomReflectionColorPatch.ResolveBounceMethod("SetCollisionLength");
    }
#endif
}
