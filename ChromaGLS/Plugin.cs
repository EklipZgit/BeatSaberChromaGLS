using HarmonyLib;
using IPA;
using JetBrains.Annotations;
using SongCore;
using Logger = IPA.Logging.Logger;

namespace ChromaGLS
{
    [Plugin(RuntimeOptions.DynamicInit)]
    internal class Plugin
    {
        private readonly Harmony _harmonyInstance = new("com.eklipz.ChromaGLS");

#pragma warning disable CA1822
        [UsedImplicitly]
        [Init]
        public Plugin(Logger pluginLogger)
        {
            Log = pluginLogger;
        }

        internal static Logger Log { get; private set; } = null!;

        [UsedImplicitly]
        [OnEnable]
        public void OnEnable()
        {
            // Expose the exact Info.dat requirement name to SongCore's song-details UI.
            Collections.RegisterCapability("ChromaGLS");
            _harmonyInstance.PatchAll(typeof(Plugin).Assembly);
        }

        [UsedImplicitly]
        [OnDisable]
        public void OnDisable()
        {
            _harmonyInstance.UnpatchSelf();

#if !PRE_V1_37_1
            // Keep SongCore's registry accurate if BSIPA disables this plugin at runtime.
            Collections.DeregisterCapability("ChromaGLS");
#endif
        }
#pragma warning restore CA1822
    }
}
