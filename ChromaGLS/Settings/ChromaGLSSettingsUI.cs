using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.GameplaySetup;
using JetBrains.Annotations;

namespace ChromaGLS.Settings
{
    internal class ChromaGLSSettingsUI : System.IDisposable
    {
        private readonly Config _config;
        private readonly GameplaySetup _gameplaySetup;

        // Constructing the menu binding eagerly avoids the unavailable modern Zenject initialization interface while preserving Chroma's GameplaySetup tab behavior.
        [UsedImplicitly]
#if !V1_29_1
        private ChromaGLSSettingsUI(
            Config config,
            GameplaySetup gameplaySetup)
#else
        private ChromaGLSSettingsUI(Config config)
#endif
        {
            _config = config;
#if !V1_29_1
            _gameplaySetup = gameplaySetup;
#else
            _gameplaySetup = GameplaySetup.instance;
#endif
            _gameplaySetup.AddTab("ChromaGLS", "ChromaGLS.Settings.modifiers.bsml", this);
        }

#pragma warning disable CA1822
        [UsedImplicitly]
        [UIValue("enabled")]
        public bool Enabled
        {
            get => _config.Enabled;

            // Persist the choice and apply it immediately so the menu toggle controls ChromaGLS without requiring a game restart.
            set
            {
                _config.Enabled = value;
                Plugin.SetEnabled(value);
            }
        }
#pragma warning restore CA1822

        public void Dispose()
        {
            _gameplaySetup.RemoveTab("ChromaGLS");
        }
    }
}
