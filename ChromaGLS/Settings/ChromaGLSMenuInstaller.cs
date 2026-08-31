using JetBrains.Annotations;
using Zenject;

namespace ChromaGLS.Settings
{
    [UsedImplicitly]
    internal class ChromaGLSMenuInstaller : Installer
    {
        public override void InstallBindings()
        {
            // Bind and eagerly construct the UI so its GameplaySetup tab is registered as soon as the menu container is installed.
            Container.BindInterfacesTo<ChromaGLSSettingsUI>().AsSingle().NonLazy();
        }
    }
}
