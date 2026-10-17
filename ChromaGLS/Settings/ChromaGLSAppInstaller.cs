using ChromaGLS.Updates;
using JetBrains.Annotations;
using Zenject;

namespace ChromaGLS.Settings
{
    [UsedImplicitly]
    internal class ChromaGLSAppInstaller : Installer
    {
        private readonly Config _config;
        private readonly StartupUpdateCheck _updateCheck;

        private ChromaGLSAppInstaller(Config config, StartupUpdateCheck updateCheck)
        {
            _config = config;
            _updateCheck = updateCheck;
        }

        public override void InstallBindings()
        {
            // Bind the BSIPA-generated config into the app container so ChromaGLSSettingsUI can resolve it in the menu container.
            Container.BindInstance(_config);
            Container.BindInstance(_updateCheck);
        }
    }
}
