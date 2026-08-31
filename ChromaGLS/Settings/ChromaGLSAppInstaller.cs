using JetBrains.Annotations;
using Zenject;

namespace ChromaGLS.Settings
{
    [UsedImplicitly]
    internal class ChromaGLSAppInstaller : Installer
    {
        private readonly Config _config;

        private ChromaGLSAppInstaller(Config config)
        {
            _config = config;
        }

        public override void InstallBindings()
        {
            // Bind the BSIPA-generated config into the app container so ChromaGLSSettingsUI can resolve it in the menu container.
            Container.BindInstance(_config);
        }
    }
}
