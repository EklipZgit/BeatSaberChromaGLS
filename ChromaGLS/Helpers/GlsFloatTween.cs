using System;
using Tweening;
using UnityEngine;

namespace ChromaGLS.Helpers
{
    // Catmull-Rom-capable FloatTween. Scalar channels precompute their four-point controls when a
    // Cat/CatS destination arrives; without them the tween is byte-for-byte the native one, and
    // marker ids keep the native linear fallback while the plugin is disabled.
    internal sealed class GlsFloatTween : FloatTween
    {
        private float _splineP1;
        private float _splineP2;
        private float _splineM1;
        private float _splineM2;
        private GlsTranslationRuntime? _translation;
        private GlsDistancePath? _scalarPath;
        private bool _distancePaced;
        private int _axis;

        public GlsFloatTween()
        {
        }

        public GlsFloatTween(
            float fromValue,
            float toValue,
            Action<float> onUpdate,
            float duration,
            EaseType easeType,
            float delay)
            : base(fromValue, toValue, onUpdate, duration, easeType, delay)
        {
        }

        public bool UseSpline { get; private set; }

        // Controls/times mirror ChroMapper SetSpline: the handler supplies every generated endpoint
        // and tangent inputs once, so GetValue never re-derives tangents per frame.
        public void SetSpline(
            float previousValue,
            float previousTime,
            float startValue,
            float startTime,
            float endValue,
            float endTime,
            float nextValue,
            float nextTime)
        {
            UseSpline = true;
            _distancePaced = false;
            _splineP1 = startValue;
            _splineP2 = endValue;
            GlsSplineMath.CatmullRomTimeTangents(
                previousValue,
                startValue,
                endValue,
                nextValue,
                previousTime,
                startTime,
                endTime,
                nextTime,
                out _splineM1,
                out _splineM2);
        }

        public void ClearSpline()
        {
            UseSpline = false;
            _distancePaced = false;
        }

        internal void SetScalarPath(GlsDistancePath? path) => _scalarPath = path;

        internal void SetDistancePacing(bool distancePaced) => _distancePaced = distancePaced;

        internal void SetTranslationPath(GlsTranslationRuntime? translation, int axis)
        {
            _translation = translation;
            _axis = axis;
        }

        public override float GetValue(float t)
        {
            if (!GlsSplineRuntime.Enabled)
            {
                return base.GetValue(t);
            }

            if (_translation != null && _translation.TryEvaluate(out var position))
            {
                return position[_axis];
            }

            if (!UseSpline)
            {
                return base.GetValue(t);
            }

            var u = Mathf.Clamp01(t);
            if (_distancePaced && _scalarPath != null
                && _scalarPath.TryMapTime(startTime + (duration * u), out var mappedTime))
            {
                u = (mappedTime - startTime) / duration;
            }

            return GlsSplineMath.Hermite(_splineP1, _splineP2, _splineM1, _splineM2, u);
        }
    }
}
