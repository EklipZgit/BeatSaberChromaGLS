using System;
using HarmonyLib;
using Newtonsoft.Json;
#if !PRE_V1_37_1
using _SaveData = CustomJSONData.CustomBeatmap.Version3CustomBeatmapSaveData;
#else
using _SaveData = CustomJSONData.CustomBeatmap.CustomBeatmapSaveData;
#endif

namespace ChromaGLS.HarmonyPatches
{
    internal static class GlsNodeEasingDeserializePatch
    {
        private static NodeEasingReader WrapReader(ref JsonReader reader, bool fxCollection)
        {
            NodeEasingReader wrapper = new(reader, fxCollection);
            reader = wrapper;
            return wrapper;
        }

        // Only the wrapper's buffered replay is disposable; the underlying reader belongs to the map
        // parser and must stay open at the matching end token.
        private static void DisposeState(NodeEasingReader state) => ((IDisposable)state).Dispose();

        [HarmonyPatch(typeof(_SaveData), nameof(_SaveData.DeserializeLightRotationEventBoxGroupArray))]
        internal static class RotationPatch
        {
            private static void Prefix(ref JsonReader reader, out NodeEasingReader __state) =>
                __state = WrapReader(ref reader, false);

            private static void Finalizer(NodeEasingReader __state) => DisposeState(__state);
        }

        [HarmonyPatch(typeof(_SaveData), nameof(_SaveData.DeserializeLightTranslationEventBoxGroupArray))]
        internal static class TranslationPatch
        {
            private static void Prefix(ref JsonReader reader, out NodeEasingReader __state) =>
                __state = WrapReader(ref reader, false);

            private static void Finalizer(NodeEasingReader __state) => DisposeState(__state);
        }

#if !V1_29_1
        [HarmonyPatch(typeof(_SaveData), nameof(_SaveData.DeserializeFxEventCollection))]
        internal static class FloatFxPatch
        {
            private static void Prefix(ref JsonReader reader, out NodeEasingReader __state) =>
                __state = WrapReader(ref reader, true);

            private static void Finalizer(NodeEasingReader __state) => DisposeState(__state);
        }
#endif
    }
}
