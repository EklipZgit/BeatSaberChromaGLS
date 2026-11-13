using System;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using HMUI;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ChromaGLS.Settings
{
    /// <summary>Modal offering the optional reflection-map download; auto-offer once on main-menu activation, manual open from the settings tab.</summary>
    [UsedImplicitly]
    internal sealed class FerrofluidReflectionDownloadUI : IDisposable
    {
        private const string Bsml = @"
<modal id='reflection-pack-modal' click-off-closes='false' move-to-center='true' size-delta-x='110' size-delta-y='66'>
  <vertical preferred-width='100' preferred-height='62' pad='3' spacing='2' horizontal-fit='Unconstrained' vertical-fit='Unconstrained' child-control-width='true' child-expand-width='true' child-control-height='true' child-expand-height='false'>
    <text text='ChromaGLS Ferrofluid reflection maps' font-size='5' preferred-height='10' min-height='10' flexible-height='0'/>
    <text id='reflection-pack-info' text='' word-wrapping='true' font-size='3.5' preferred-height='28' min-height='28' flexible-height='0' align='TopLeft'/>
    <horizontal preferred-height='10' min-height='10' flexible-height='0' spacing='2'>
      <primary-button id='reflection-pack-download' text='Download' preferred-width='30' preferred-height='8' min-height='8' flexible-height='0' on-click='reflection-pack-download'/>
      <button id='reflection-pack-decline' text='No thanks' preferred-width='30' preferred-height='8' min-height='8' flexible-height='0' on-click='reflection-pack-decline'/>
      <button id='reflection-pack-close' text='Close' preferred-width='30' preferred-height='8' min-height='8' flexible-height='0' on-click='reflection-pack-close'/>
    </horizontal>
  </vertical>
</modal>";

        private readonly MainMenuViewController _mainMenu;
        private readonly FerrofluidReflectionPackService _service;
        private RectTransform? _requestedParent;
        private bool _disposed;
        private bool _modalShown;
        private ModalView? _modal;
        private GameObject? _modalRoot;
        private RectTransform? _modalParent;
        private FerrofluidReflectionDownloadAnchor? _anchor;
        private TMP_Text? _downloadLabel;
        private TMP_Text? _declineLabel;

        [UIObject("reflection-pack-modal")]
        private GameObject? _modalObject;

        [UIComponent("reflection-pack-info")]
        private TMP_Text? _info;

        [UIComponent("reflection-pack-download")]
        private Button? _downloadButton;

        [UIComponent("reflection-pack-decline")]
        private Button? _declineButton;

        [UIComponent("reflection-pack-close")]
        private Button? _closeButton;

        private FerrofluidReflectionDownloadUI(MainMenuViewController mainMenu, FerrofluidReflectionPackService service)
        {
            _mainMenu = mainMenu;
            _service = service;
            _mainMenu.didActivateEvent += HandleMenuActivated;
            _mainMenu.didDeactivateEvent += HandleMenuDeactivated;
            _service.StatusChanged += HandleStatusChanged;
            _service.DownloadPromptRequested += HandleDownloadPromptRequested;
            TryAutoOffer();
        }

        /// <summary>Forwarded service quality event so menu dropdowns stay in sync without holding the service.</summary>
        internal event Action<ReflectionQuality>? QualityChanged
        {
            add => _service.QualityChanged += value;
            remove => _service.QualityChanged -= value;
        }

        /// <summary>Manual dropdown selection; remembers the visible parent the consent modal opens on.</summary>
        internal void RequestQuality(ReflectionQuality quality, RectTransform parent)
        {
            _requestedParent = parent;
            _service.RequestQuality(quality);
        }

        private void HandleDownloadPromptRequested()
        {
            if (_disposed)
                return;
            RectTransform parent = _requestedParent != null ? _requestedParent : (RectTransform)_mainMenu.transform;
            Open(parent);
        }

        public void Dispose()
        {
            Hide();
            _disposed = true;
            _mainMenu.didActivateEvent -= HandleMenuActivated;
            _mainMenu.didDeactivateEvent -= HandleMenuDeactivated;
            _service.StatusChanged -= HandleStatusChanged;
            _service.DownloadPromptRequested -= HandleDownloadPromptRequested;
            _requestedParent = null;
            DestroyModal();
        }

        internal void Open(RectTransform parent)
        {
            if (_disposed)
                return;
            EnsureModal(parent);
            if (_modal != null)
            {
                _modal.Show(true, true);
                _modalShown = true;
            }

            Refresh();
        }

        internal static string DescribeState(FerrofluidReflectionPackService.PackState state, FerrofluidReflectionPackManifest? manifest, string? lastError, int progress = 0, bool usesLocalSource = false)
        {
            switch (state)
            {
                case FerrofluidReflectionPackService.PackState.Checking:
                    return "Checking installed reflection maps...";
                case FerrofluidReflectionPackService.PackState.Downloading:
                    return $"Downloading reflection maps... {progress}%";
                case FerrofluidReflectionPackService.PackState.Ready:
                    return "Reflection maps installed.";
                case FerrofluidReflectionPackService.PackState.Failed:
                    return "Download failed: " + lastError;
                case FerrofluidReflectionPackService.PackState.Missing:
                    if (usesLocalSource)
                        return $"Development local-file source: optional HDR room reflections (~{manifest?.ByteLength / 1000000m:F0} MB). Ferrofluid works without them.";
                    if (manifest != null && manifest.CanDownload)
                        return $"Optional HDR room reflections (~{manifest.ByteLength / 1000000m:F0} MB). Ferrofluid works without them.";
                    return "Reflection maps are not published for this build. Install its matching local pack in UserData/ChromaGLS/Ferrofluid and restart.";
                default:
                    return "Optional reflection maps are unavailable for this build.";
            }
        }

        internal void Hide()
        {
            // The GameplaySetup anchor can deactivate without a MainMenu view event.
            if (_modal != null && _modalShown)
                _modal.Hide(false);
            _modalShown = false;
        }

        private void TryAutoOffer()
        {
            if (_disposed || _modalShown || !_mainMenu.isActivated || !_service.TryOfferDownload())
                return;
            Open((RectTransform)_mainMenu.transform);
        }

        private void HandleMenuActivated(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (addedToHierarchy)
                TryAutoOffer();
        }

        private void HandleMenuDeactivated(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            Hide();
        }

        private void HandleStatusChanged()
        {
            if (_disposed)
                return;
            TryAutoOffer();
            Refresh();
        }

        private void EnsureModal(RectTransform parent)
        {
            if (_modalRoot != null && parent == _modalParent)
                return;

            DestroyModal();

            _modalParent = parent;
            _modalRoot = new GameObject("FerrofluidReflectionDownloadUI", typeof(RectTransform));
            _modalRoot.transform.SetParent(parent, false);
            _anchor = _modalRoot.AddComponent<FerrofluidReflectionDownloadAnchor>();
            _anchor.Initialize(this);
#if PRE_V1_40_8
            BSMLParser.instance.Parse(Bsml, _modalRoot, this);
#else
            BSMLParser.Instance.Parse(Bsml, _modalRoot, this);
#endif
            if (_modalObject != null && _modalObject.TryGetComponent(out ModalView modalView))
                _modal = modalView;
            if (_downloadButton != null)
                _downloadLabel = _downloadButton.GetComponentInChildren<TMP_Text>(true);
            if (_declineButton != null)
                _declineLabel = _declineButton.GetComponentInChildren<TMP_Text>(true);
        }

        private void DestroyModal()
        {
            // Destroy runs later, so the old anchor must not hide a newly created dialog.
            if (_anchor != null)
                _anchor.Detach();
            if (_modalRoot != null)
                Object.Destroy(_modalRoot);
            _modalRoot = null;
            _modal = null;
            _modalObject = null;
            _anchor = null;
            _downloadLabel = null;
            _declineLabel = null;
            _modalShown = false;
        }

        private void Refresh()
        {
            if (_modal == null)
                return;
            if (_info != null)
                _info.text = DescribeState(_service.State, _service.Manifest, _service.LastError, _service.Progress, _service.UsesLocalSource);
            bool downloading = _service.State == FerrofluidReflectionPackService.PackState.Downloading;
            bool missing = _service.State == FerrofluidReflectionPackService.PackState.Missing && _service.CanDownload;
            bool failed = _service.State == FerrofluidReflectionPackService.PackState.Failed && _service.CanDownload;
            if (_downloadButton != null)
                _downloadButton.gameObject.SetActive(missing || failed);
            if (_downloadLabel != null)
                _downloadLabel.SetText(failed ? "Retry" : "Download");

            if (_declineButton != null)
                _declineButton.gameObject.SetActive(missing || downloading);
            if (_declineLabel != null)
                _declineLabel.SetText(downloading ? "Cancel download" : "No thanks");
            if (_closeButton != null)
                _closeButton.gameObject.SetActive(!downloading && !missing);
        }

        [UIAction("reflection-pack-download")]
        private void DownloadClicked()
        {
            _ = _service.DownloadAsync();
        }

        [UIAction("reflection-pack-decline")]
        private void DeclineClicked()
        {
            if (_service.State == FerrofluidReflectionPackService.PackState.Downloading)
                _service.CancelDownload();
            else
                _service.Decline();
            Hide();
        }

        [UIAction("reflection-pack-close")]
        private void CloseClicked()
        {
            if (_service.State == FerrofluidReflectionPackService.PackState.Missing
                || _service.State == FerrofluidReflectionPackService.PackState.Checking
                || _service.State == FerrofluidReflectionPackService.PackState.Failed)
            {
                _service.Decline();
            }

            Hide();
        }
    }

    internal sealed class FerrofluidReflectionDownloadAnchor : MonoBehaviour
    {
        private FerrofluidReflectionDownloadUI? _owner;

        internal void Initialize(FerrofluidReflectionDownloadUI owner) => _owner = owner;

        internal void Detach() => _owner = null;

        private void OnDisable() => _owner?.Hide();
    }
}
