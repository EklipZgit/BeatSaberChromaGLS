using System.IO;
using ChromaGLS.Settings;
using HarmonyLib;
using IPA;
using IPA.Config.Stores;
using JetBrains.Annotations;
using SiraUtil.Zenject;
using SongCore;
using Logger = IPA.Logging.Logger;

namespace ChromaGLS
{
    [Plugin(RuntimeOptions.DynamicInit)]
    internal class Plugin
    {
        private static Plugin? _instance;
        private readonly Harmony _harmonyInstance = new("com.eklipz.ChromaGLS");
        private readonly Config _config;
        private bool _patchesApplied;

#pragma warning disable CA1822
        [UsedImplicitly]
        [Init]
        public Plugin(Logger pluginLogger, IPA.Config.Config conf, Zenjector zenjector)
        {
            _instance = this;
            Log = pluginLogger;
            _config = conf.Generated<Config>();

            // Install the config binding in the app container before the menu installer constructs the settings UI.
            zenjector.Install<ChromaGLSAppInstaller>(Location.App, _config);
            zenjector.Install<ChromaGLSMenuInstaller>(Location.Menu);
        }

        internal static Logger Log { get; private set; } = null!;

        [UsedImplicitly]
        [OnEnable]
        public void OnEnable()
        {
            // Apply the persisted state through the same lifecycle used by the settings toggle so startup and runtime transitions cannot diverge.
            SetEnabled(_config.Enabled);
        }

        [UsedImplicitly]
        [OnDisable]
        public void OnDisable()
        {
            // Remove all runtime behavior through the same lifecycle used by the in-game toggle when BSIPA disables the plugin.
            ApplyEnabled(false);
        }

        internal static void SetEnabled(bool enabled)
        {
            _instance?.ApplyEnabled(enabled);
        }

        private void ApplyEnabled(bool enabled)
        {
            if (enabled == _patchesApplied)
            {
                return;
            }

            if (!enabled)
            {
                _harmonyInstance.UnpatchSelf();
#if !PRE_V1_37_1
                // Keep SongCore's registry accurate when the in-game toggle disables ChromaGLS.
                Collections.DeregisterCapability("ChromaGLS");
#endif
                _patchesApplied = false;
                return;
            }

            // Expose the exact Info.dat requirement name to SongCore's song-details UI only when ChromaGLS is active.
            Collections.RegisterCapability("ChromaGLS");
#if DEBUG
            // Prove which ChromaGLS assembly the game loaded while keeping this diagnostic out of Release builds.
            var assemblyPath = typeof(Plugin).Assembly.Location;
            Log.Info($"[ChromaGLS Debug Assembly] path={assemblyPath} creationUtc={File.GetCreationTimeUtc(assemblyPath):O} lastWriteUtc={File.GetLastWriteTimeUtc(assemblyPath):O}");
#endif

            _harmonyInstance.PatchAll(typeof(Plugin).Assembly);
            _patchesApplied = true;
        }
#pragma warning restore CA1822
    }
}
