using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.GameplaySetup;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChromaGLS.Settings
{
    internal class ChromaGLSSettingsUI : System.IDisposable
    {
        internal const bool FerrofluidTweakMenuEnabled =
#if FERROFLUID_TWEAKS
            true;
#else
            false;
#endif

        private readonly Config _config;
        private readonly ParabolicFloorLightSettingsUI _parabolaUI;
#if FERROFLUID_TWEAKS
        private readonly FerrofluidSettingsUI _ferroUI;
#endif
        private readonly GameplaySetup _gameplaySetup;
        private readonly FerrofluidReflectionDownloadUI _packUI;

        [UIComponent("ferrofluid-preview-anchor")]
        private RectTransform _ferrofluidPreviewAnchor = null!;

        [UIComponent("menu-floor-light-setting")]
        private BeatSaberMarkupLanguage.Components.Settings.ToggleSetting _menuFloorLightSetting = null!;

        [UIComponent("menu-floor-light-tweak")]
        private UnityEngine.UI.Button _menuFloorLightTweak = null!;

        [UIComponent("chroma-menu-settings")]
        private HMUI.ScrollView _menuSettingsScrollView = null!;

        [UIObject("reflection-maps-button")]
        private GameObject? _reflectionMapsButton;

        [UsedImplicitly]
#if !V1_29_1
        private ChromaGLSSettingsUI(
            Config config,
            ParabolicFloorLight parabolicFloorLight,
            GameplaySetup gameplaySetup,
            FerrofluidReflectionDownloadUI packUI)
#else
        private ChromaGLSSettingsUI(
            Config config,
            ParabolicFloorLight parabolicFloorLight,
            FerrofluidReflectionDownloadUI packUI)
#endif
        {
            _config = config;
            _packUI = packUI;
            _parabolaUI = new ParabolicFloorLightSettingsUI(config, parabolicFloorLight, packUI);
#if FERROFLUID_TWEAKS
            _ferroUI = new FerrofluidSettingsUI(config);
#endif
#if !V1_29_1
            _gameplaySetup = gameplaySetup;
#else
            _gameplaySetup = GameplaySetup.instance;
#endif

            // Build with -p:FerrofluidTweaks=true to show the session-only tuning controls.
#if FERROFLUID_TWEAKS
            _gameplaySetup.AddTab("ChromaGLS", "ChromaGLS.Settings.modifiers.bsml", this);
#else
            _gameplaySetup.AddTab("ChromaGLS", "ChromaGLS.Settings.modifiers-minimal.bsml", this);
#endif
            _packUI.StatusChanged += RefreshReflectionMapsButton;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        }

#pragma warning disable CA1822
        [UsedImplicitly]
        [UIValue("enabled")]
        public bool Enabled
        {
            get => _config.Enabled;
            set
            {
                _config.Enabled = value;
                Plugin.SetEnabled(value);
            }
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-border-enabled")]
        public bool FerrofluidBorderEnabled
        {
            get => _config.FerrofluidBorderEnabled;
            set
            {
                _config.FerrofluidBorderEnabled = value;
                Plugin.SetFerrofluidBorderEnabled(value);
            }
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-cube-enabled")]
        public bool FerrofluidCubeEnabled
        {
            get => _config.FerrofluidCubeEnabled;
            set
            {
                _config.FerrofluidCubeEnabled = value;
                Plugin.SetFerrofluidCubeEnabled(value);
            }
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-sphere-enabled")]
        public bool FerrofluidSphereEnabled
        {
            get => _config.FerrofluidSphereEnabled;
            set
            {
                _config.FerrofluidSphereEnabled = value;
                Plugin.SetFerrofluidSphereEnabled(value);
            }
        }

        [UsedImplicitly]
        [UIValue("fluid-cube-enabled")]
        public bool FluidCubeEnabled
        {
            get => _config.FluidCubeEnabled;
            set
            {
                _config.FluidCubeEnabled = value;
                Plugin.SetFluidCubeEnabled(value);
            }
        }

        [UsedImplicitly]
        [UIValue("fluid-sphere-enabled")]
        public bool FluidSphereEnabled
        {
            get => _config.FluidSphereEnabled;
            set
            {
                _config.FluidSphereEnabled = value;
                Plugin.SetFluidSphereEnabled(value);
            }
        }

        [UsedImplicitly]
        [UIValue("parabola-light-enabled")]
        public bool ParabolaLightEnabled
        {
            get => _config.ParabolicFloorLightEnabled;
            set => _parabolaUI.SetEnabled(value);
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode _)
        {
            if (scene.name == "MainMenu")
            {
                _packUI.SetMenuActive(true);
                return;
            }

            if (scene.name != "GameCore")
                return;

            _packUI.SetMenuActive(false);
            _parabolaUI.CloseForGameplay();
#if FERROFLUID_TWEAKS
            _ferroUI.CloseForGameplay();
#endif
        }

        private void HandleActiveSceneChanged(Scene previous, Scene next)
            => HandleSceneLoaded(next, LoadSceneMode.Additive);

#pragma warning restore CA1822

        public void Dispose()
        {
            _packUI.StatusChanged -= RefreshReflectionMapsButton;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
#if FERROFLUID_TWEAKS
            _ferroUI.Dispose();
#endif
            _parabolaUI.Dispose();

            _gameplaySetup.RemoveTab("ChromaGLS");
        }

        [UsedImplicitly]
        [UIAction("#post-parse")]
        private void HandlePostParse()
        {
            // The cloned settings scroll view inherits EULA's vertical viewport padding,
            // which otherwise clips content at the top and bottom of the Mods tab.
            ConfigureMenuViewport(_menuSettingsScrollView.viewportTransform);
            _menuSettingsScrollView.ScrollTo(0f, false);
            RefreshReflectionMapsButton();

            ParabolicFloorLightAnchorLifecycle parabolaLifecycle = _ferrofluidPreviewAnchor.gameObject.AddComponent<ParabolicFloorLightAnchorLifecycle>();
            parabolaLifecycle.Initialize(_parabolaUI);
            ParabolicFloorLightSettingsUI.PositionTweakButtonLeftOfSwitch(
                (RectTransform)_menuFloorLightTweak.transform,
                (RectTransform)_menuFloorLightSetting.transform.Find("SwitchView"));
#if FERROFLUID_TWEAKS
            FerrofluidSettingsAnchorLifecycle lifecycle = _ferrofluidPreviewAnchor.gameObject.AddComponent<FerrofluidSettingsAnchorLifecycle>();
            lifecycle.Initialize(_ferroUI);
#endif
        }

        private void RefreshReflectionMapsButton()
        {
            if (_reflectionMapsButton != null)
                _reflectionMapsButton.SetActive(_packUI.NeedsDownload);
        }

        internal static void ConfigureMenuViewport(RectTransform viewport)
        {
            viewport.offsetMin = new Vector2(viewport.offsetMin.x, 0f);
            viewport.offsetMax = new Vector2(viewport.offsetMax.x, 0f);
        }

        [UsedImplicitly]
        [UIAction("parabola-open")]
        private void OpenParabolaTweaks() => _parabolaUI.Open(_ferrofluidPreviewAnchor);

        [UsedImplicitly]
        [UIAction("reflection-maps-open")]
        private void OpenReflectionMaps() => _packUI.Open(_ferrofluidPreviewAnchor);

#if FERROFLUID_TWEAKS
        [UsedImplicitly]
        [UIAction("ferro-open")]
        private void OpenFerroTweaks() => _ferroUI.Open(_ferrofluidPreviewAnchor);
#endif

    }
}
