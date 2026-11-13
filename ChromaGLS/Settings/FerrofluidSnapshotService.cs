#if FERROFLUID_TWEAKS
using System;
using System.Collections;
using UnityEngine;
using Zenject;

namespace ChromaGLS.Settings
{
    // Menu-scoped lifetime: the behaviour is created with the menu container and destroyed with it.
    internal sealed class FerrofluidSnapshotLifecycle : IInitializable, IDisposable
    {
        private GameObject? _host;

        public void Initialize()
        {
            _host = new GameObject("ChromaGLS Ferrofluid Snapshots", typeof(FerrofluidSnapshotBehaviour));
        }

        public void Dispose()
        {
            if (_host == null)
                return;
            UnityEngine.Object.Destroy(_host);
            _host = null;
        }
    }

    // F12 polls only in the tweak build; GetKeyDownInt is the same raw read the keyboard session uses.
    internal sealed class FerrofluidSnapshotBehaviour : MonoBehaviour
    {
        private bool _busy;

        internal bool CaptureInProgress => _busy;

        private void Update()
        {
            if (!FerrofluidSnapshotCapture.ShouldSchedule(_busy, Input.GetKeyDownInt(KeyCode.F12)))
                return;
            _busy = true;
            StartCoroutine(CaptureRoutine());
        }

        // WaitForEndOfFrame samples the same rendered frame: transforms, cameras, GPU textures,
        // and the screenshot all belong to what the user just saw.
        private IEnumerator CaptureRoutine()
        {
            yield return new WaitForEndOfFrame();
            try
            {
                FerrofluidCaptureResult result = FerrofluidSnapshotCapture.CaptureFrame();

                // First successful F12 starts the one-shot 120-frame timing diagnostic; a
                // repeat F12 while it runs keeps collecting rather than restarting it.
                FerrofluidMenuTimingProbe.NotifyCapture(this, result.Directory);
            }
            catch (Exception error)
            {
                Plugin.Log?.Error($"[ChromaGLS Ferrofluid Snapshot] capture failed: {error}");
            }
            finally
            {
                _busy = false;
            }
        }

        private void OnDestroy()
        {
            FerrofluidMenuTimingProbe.Cancel("menu-exit");
        }
    }
}
#endif
