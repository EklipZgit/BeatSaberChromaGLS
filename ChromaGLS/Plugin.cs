using System;
using System.IO;
using System.Reflection;
using ChromaGLS.HarmonyPatches;
using ChromaGLS.Settings;
using ChromaGLS.Updates;
using HarmonyLib;
using IPA;
using IPA.Config.Stores;
using IPA.Utilities;
using JetBrains.Annotations;
using SiraUtil.Zenject;
using SongCore;
using Logger = IPA.Logging.Logger;

namespace ChromaGLS
{
    [Plugin(RuntimeOptions.DynamicInit)]
    internal class Plugin
    {
        // Beat Saber caches converted maps, so keep metadata patches installed while ChromaGLS is disabled.
        // Re-enabling can then use maps loaded earlier in the session.
        private static readonly Type[] PersistentPatchTypes =
        {
            typeof(GlsDeserializePatch),
            typeof(GlsConverterPatches),
            typeof(GlsConverterPatches.LightColorEventBoxConverterPatch),
            typeof(GlsEasingRuntimePatch),
            typeof(ChromaGlsRequirementSelectionPatch),
        };

        private static Plugin? _instance;
        private readonly Harmony _harmonyInstance = new("com.eklipz.ChromaGLS");
        private readonly Config _config;
        private readonly StartupUpdateCheck _updateCheck;
        private bool _patchesApplied;

#pragma warning disable CA1822
        [UsedImplicitly]
        [Init]
        public Plugin(Logger pluginLogger, IPA.Config.Config conf, Zenjector zenjector)
        {
            _instance = this;
            Log = pluginLogger;
            _config = conf.Generated<Config>();
            _updateCheck = new StartupUpdateCheck(
                StartupUpdateCheck.FetchLatestReleaseAsync,
                typeof(Plugin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion,
                UnityGame.GameVersion.ToString(),
                StartupUpdateCheck.IsDebugBuild);

            // Install the config binding in the app container before the menu installer constructs the settings UI.
            zenjector.Install<ChromaGLSAppInstaller>(Location.App, _config, _updateCheck);
            zenjector.Install<ChromaGLSMenuInstaller>(Location.Menu);
            zenjector.Install<ChromaGLSPlayerInstaller>(Location.Player);
        }

        internal static Logger Log { get; private set; } = null!;

        [UsedImplicitly]
        [OnEnable]
        public void OnEnable()
        {
            _ = _updateCheck.CheckOnceAsync();

            ApplyFerrofluidSettings(_config);

            InstallPersistentPatches();

            SetEnabled(_config.Enabled);
            SetFerrofluidBorderEnabled(_config.FerrofluidBorderEnabled);
            SetFerrofluidCubeEnabled(_config.FerrofluidCubeEnabled);
            SetFerrofluidSphereEnabled(_config.FerrofluidSphereEnabled);
        }

        [UsedImplicitly]
        [OnDisable]
        public void OnDisable()
        {
            ApplyEnabled(false);
        }

        internal static void SetEnabled(bool enabled)
        {
            _instance?.ApplyEnabled(enabled);
        }

        internal static void SetFerrofluidBorderEnabled(bool enabled)
        {
            ChromaGlsRequirementEffect.SetFerrofluidEnabled(enabled);
        }

        internal static void SetFerrofluidCubeEnabled(bool enabled)
        {
            FerrofluidEnvironmentObjects.SetCubeEnabled(enabled);
        }

        internal static void SetFerrofluidSphereEnabled(bool enabled)
        {
            FerrofluidEnvironmentObjects.SetSphereEnabled(enabled);
        }

        internal static void ApplyFerrofluidSettings(Config config)
        {
            ChromaGlsRequirementEffect.ApplyConfiguration(config);
            FerrofluidEnvironmentObjects.ApplyConfiguration(config);
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

                InstallPersistentPatches();
#if !PRE_V1_37_1
                Collections.DeregisterCapability("ChromaGLS");
#endif
                _patchesApplied = false;
                return;
            }

            Collections.RegisterCapability("ChromaGLS");
#if DEBUG
            var assemblyPath = typeof(Plugin).Assembly.Location;
            Log?.Info($"[ChromaGLS Debug Assembly] path={assemblyPath} creationUtc={File.GetCreationTimeUtc(assemblyPath):O} lastWriteUtc={File.GetLastWriteTimeUtc(assemblyPath):O}");
#endif

            // Harmony adds duplicate patches. Remove the existing ones first or the parser can consume the same JSON twice.
            _harmonyInstance.UnpatchSelf();
            _harmonyInstance.PatchAll(typeof(Plugin).Assembly);
            _patchesApplied = true;

            Log?.Info($"[ChromaGLS] Gameplay patch installation completed; assembly={typeof(Plugin).Assembly.GetName().Version} lastWriteUtc={File.GetLastWriteTimeUtc(typeof(Plugin).Assembly.Location):O}.");
        }

        private void InstallPersistentPatches()
        {
            foreach (Type patchType in PersistentPatchTypes)
            {
                _harmonyInstance.CreateClassProcessor(patchType).Patch();
            }
        }
#pragma warning restore CA1822
    }
}
