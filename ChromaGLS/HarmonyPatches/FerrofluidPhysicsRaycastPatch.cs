using ChromaGLS.Settings;
using HarmonyLib;
using UnityEngine;
using VRUIControls;

namespace ChromaGLS.HarmonyPatches
{
    // The ferrofluid colliders live in an isolated local physics scene, so the menu's own cached
    // physics raycaster can no longer see them. Each cache miss additionally probes the local
    // scene and merges the nearer hit back into the same cache entry, preserving canvas ordering.
    [HarmonyPatch(typeof(PhysicsRaycasterWithCache), "Raycast")]
    internal static class FerrofluidPhysicsRaycastPatch
    {
        private static void Prefix(PhysicsRaycasterWithCache __instance, out int __state)
        {
            if (FerrofluidPhysicsWorld.Active == null)
            {
                __state = -1;
                return;
            }

            __state = __instance._lastFrameCount == Time.frameCount
                ? __instance._cachedRaycasts.Count
                : 0;
        }

        private static void Postfix(
            PhysicsRaycasterWithCache __instance,
            int __state,
            Ray ray,
            ref RaycastHit hitInfo,
            float maxDistance,
            int layerMask,
            ref bool __result)
        {
            FerrofluidPhysicsWorld? world = FerrofluidPhysicsWorld.Active;
            if (__state < 0 || world == null)
                return;

            System.Collections.Generic.List<PhysicsRaycasterWithCache.CachedRaycast> cache = __instance._cachedRaycasts;
            if (cache.Count != __state + 1)
                return;

            world.TrackRaycaster(__instance);

            if (!world.Raycast(ray, out RaycastHit localHit, maxDistance, layerMask))
                return;

            // Equal distances keep the default-scene winner so existing sorting stays stable.
            if (__result && hitInfo.distance <= localHit.distance)
                return;

            hitInfo = localHit;
            __result = true;
            cache[cache.Count - 1] = new PhysicsRaycasterWithCache.CachedRaycast(
                __result,
                ray,
                hitInfo,
                maxDistance,
                layerMask);
        }
    }
}
