using System;

namespace ChromaGLS.Settings
{
    // Conservative frame-budget admission for the menu-atlas capture. A pending tile or the
    // final publish step may only launch when the last four completed frames are consecutive
    // IDs ending at the previous frame and each fits the deadline minus a 2ms reserve, and the
    // quiet period has elapsed since the last invalidation. Missing/stale/zero timings defer.
    internal sealed class FerrofluidMenuReflectionBudget
    {
        internal const int HistoryLength = 4;
        internal const float ReserveMs = 2f;
        internal const float QuietSeconds = 0.5f;

        private readonly double[] _cpuTimes = new double[HistoryLength];
        private readonly double[] _gpuTimes = new double[HistoryLength];
        private readonly long[] _frameIds = new long[HistoryLength];
        private int _filled;
        private float _targetFrameMs = -1f;
        private float _lastInvalidateTime = float.NegativeInfinity;
        private long _admittedFrame = -1;

        internal string DeferReason { get; private set; } = "idle";

        internal bool Capturing { get; set; }

        internal void SetTargetRefresh(float fps)
        {
            _targetFrameMs = fps > 1f && !float.IsNaN(fps) && !float.IsInfinity(fps) ? 1000f / fps : -1f;
        }

        internal void Invalidate(float time)
        {
            _lastInvalidateTime = time;
        }

        // Feeds a completed-frame sample; invalid or non-consecutive IDs restart the history.
        internal void RecordFrame(long frameId, double cpuMs, double gpuMs)
        {
            if (frameId <= 0
                || double.IsNaN(cpuMs) || double.IsNaN(gpuMs)
                || double.IsInfinity(cpuMs) || double.IsInfinity(gpuMs)
                || cpuMs <= 0 || gpuMs <= 0)
            {
                _filled = 0;
                return;
            }

            if (_filled > 0)
            {
                long previous = _frameIds[_filled - 1];
                if (frameId == previous)
                    return; // duplicate sample: keep the window, don't double-count
                if (frameId != previous + 1)
                    _filled = 0; // a gap means stale history: start over
            }

            if (_filled < HistoryLength)
            {
                _frameIds[_filled] = frameId;
                _cpuTimes[_filled] = cpuMs;
                _gpuTimes[_filled] = gpuMs;
                _filled++;
            }
            else
            {
                Array.Copy(_frameIds, 1, _frameIds, 0, HistoryLength - 1);
                Array.Copy(_cpuTimes, 1, _cpuTimes, 0, HistoryLength - 1);
                Array.Copy(_gpuTimes, 1, _gpuTimes, 0, HistoryLength - 1);
                _frameIds[HistoryLength - 1] = frameId;
                _cpuTimes[HistoryLength - 1] = cpuMs;
                _gpuTimes[HistoryLength - 1] = gpuMs;
            }
        }

        // True when a pending tile may launch this frame; at most one admit per frame.
        internal bool AdmitTile(long frameId, float time)
        {
            if (Capturing)
            {
                DeferReason = "capture-in-flight";
                return false;
            }

            if (_admittedFrame == frameId)
            {
                DeferReason = "already-admitted-this-frame";
                return false;
            }

            if (time - _lastInvalidateTime < QuietSeconds)
            {
                DeferReason = "quiet-period";
                return false;
            }

            if (_targetFrameMs <= 0f)
            {
                DeferReason = "refresh-unknown";
                return false;
            }

            if (_filled < HistoryLength || _frameIds[HistoryLength - 1] != frameId - 1)
            {
                DeferReason = "timing-history";
                return false;
            }

            double deadline = _targetFrameMs - ReserveMs;
            for (int index = 0; index < HistoryLength; index++)
            {
                if (_cpuTimes[index] > deadline || _gpuTimes[index] > deadline)
                {
                    DeferReason = "frame-over-budget";
                    return false;
                }
            }

            _admittedFrame = frameId;
            DeferReason = string.Empty;
            return true;
        }

        // The publish/finalize step shares the same admission rule as a tile.
        internal bool AdmitPublish(long frameId, float time) => AdmitTile(frameId, time);
    }
}
