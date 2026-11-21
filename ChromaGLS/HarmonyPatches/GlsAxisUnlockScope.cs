using System;
using System.Collections.Generic;
using System.Reflection;
using CustomJSONData.CustomBeatmap;
using HarmonyLib;
using Zenject;

namespace ChromaGLS.HarmonyPatches
{
    [HarmonyPatch]
    internal static class GlsAxisUnlockScope
    {
        [ThreadStatic]
        private static bool _enabled;

        internal static bool IsEnabled => _enabled;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(LightRotationGroupEffectManager), "Start");
            yield return AccessTools.DeclaredMethod(typeof(LightTranslationGroupEffectManager), "Start");
        }

        private static void Prefix(DiContainer ____container, out bool __state)
        {
            // InitData is built synchronously inside Start. Read the active difficulty once per manager.
            __state = _enabled;
            _enabled = ____container.TryResolve<IReadonlyBeatmapData>() is CustomBeatmapData beatmap
                && ChromaGlsDifficultyMetadata.OptsIntoChromaGls(beatmap.beatmapCustomData, includeUnprefixedKeys: true);
        }

        private static void Finalizer(bool __state) => _enabled = __state;
    }
}
