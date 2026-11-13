using System;
using System.Collections;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    [DefaultExecutionOrder(2000)]
    public sealed class FerrofluidValidationFrameRunner : MonoBehaviour
    {
        private IEnumerator _frames = null!;
        private Action<int> _finish = null!;

        public void Begin(IEnumerator frames, Action<int> finish)
        {
            _frames = frames;
            _finish = finish;
        }

        // Rigidbody interpolation has published the displayed pose before LateUpdate.
        private void LateUpdate()
        {
            try
            {
                if (_frames.MoveNext())
                    return;
            }
            catch (Exception exception)
            {
                (_frames as IDisposable)?.Dispose();
                Debug.LogException(exception);
                enabled = false;
                _finish(1);
                return;
            }

            (_frames as IDisposable)?.Dispose();
            enabled = false;
            _finish(0);
        }
    }
}
