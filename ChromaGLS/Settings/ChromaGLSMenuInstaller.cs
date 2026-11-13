using ChromaGLS.Updates;
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

            // FerrofluidReflectionLifecycle orchestrates every menu session, including the environment objects.
            Container.BindInterfacesTo<FerrofluidReflectionLifecycle>().AsSingle().NonLazy();
            Container.BindInterfacesAndSelfTo<ParabolicFloorLight>().AsSingle().NonLazy();
            Container.BindInterfacesTo<UpdateMenuButton>().AsSingle().NonLazy();
            Container.BindInterfacesAndSelfTo<FerrofluidReflectionDownloadUI>().AsSingle().NonLazy();
#if FERROFLUID_TWEAKS
            Container.BindInterfacesTo<FerrofluidSnapshotLifecycle>().AsSingle().NonLazy();
#endif
        }
    }
}
