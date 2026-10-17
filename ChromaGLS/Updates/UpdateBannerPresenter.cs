using System;
using System.Threading.Tasks;

namespace ChromaGLS.Updates
{
    internal sealed class UpdateBannerPresenter : IDisposable
    {
        internal const string ReleasesPageUrl = "https://github.com/EklipZgit/BeatSaberChromaGLS/releases";

        private readonly StartupUpdateCheck _updateCheck;
        private readonly Action<bool> _setVisible;
        private readonly Action<string> _openUrl;
        private bool _mainMenuActive;
        private bool _updateAvailable;
        private bool _visible;
        private bool _disposed;

        public UpdateBannerPresenter(StartupUpdateCheck updateCheck, Action<bool> setVisible, Action<string> openUrl)
        {
            _updateCheck = updateCheck;
            _setVisible = setVisible;
            _openUrl = openUrl;
        }

        internal string VersionText { get; private set; } = string.Empty;

        public async Task InitializeAsync()
        {
            AvailablePluginUpdate? update = await _updateCheck.CheckOnceAsync();
            if (_disposed)
            {
                return;
            }

            _updateAvailable = update != null;
            VersionText = update == null ? string.Empty : $"{update.InstalledVersion} -> {update.Version}";
            Refresh();
        }

        internal void SetMainMenuActive(bool active)
        {
            _mainMenuActive = active;
            Refresh();
        }

        internal void OpenReleases()
        {
            if (_disposed || !_mainMenuActive || !_updateAvailable)
            {
                return;
            }

            _openUrl(ReleasesPageUrl);
        }

        public void Dispose()
        {
            _disposed = true;
            Refresh();
        }

        private void Refresh()
        {
            bool visible = !_disposed && _mainMenuActive && _updateAvailable;
            if (visible == _visible)
            {
                return;
            }

            _visible = visible;
            _setVisible(visible);
        }
    }
}
