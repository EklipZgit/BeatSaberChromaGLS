using UnityEngine;

namespace ChromaGLS.Updates
{
    internal sealed class UpdateBannerLifecycle : MonoBehaviour
    {
        private UpdateBannerPresenter _presenter = null!;

        internal void Initialize(UpdateBannerPresenter presenter)
        {
            _presenter = presenter;
        }

        private void OnEnable()
        {
            _presenter.SetMainMenuActive(true);
        }

        private void OnDisable()
        {
            _presenter.SetMainMenuActive(false);
        }
    }
}
