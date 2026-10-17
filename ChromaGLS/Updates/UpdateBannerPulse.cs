using UnityEngine;
using UnityEngine.UI;

namespace ChromaGLS.Updates
{
    internal sealed class UpdateBannerPulse : MonoBehaviour
    {
        private const float PulseFrequency = 0.65f;
        private const float PulseAmplitude = 0.3f;
        private const float PulseCenter = 0.7f;

        private Image[] _borders = null!;
        private Color[] _baseColors = null!;

        internal void Initialize(Image[] borders)
        {
            _borders = borders;
            _baseColors = new Color[borders.Length];
            for (int i = 0; i < borders.Length; i++)
            {
                _baseColors[i] = borders[i].color;
            }
        }

        internal static float EvaluatePulseIntensity(float time)
        {
            return PulseCenter + (PulseAmplitude * Mathf.Sin(time * PulseFrequency * 2f * Mathf.PI));
        }

        private void Update()
        {
            float intensity = EvaluatePulseIntensity(Time.unscaledTime);
            for (int i = 0; i < _borders.Length; i++)
            {
                // The no-glow material writes dst*(1-srcA) into alpha, so vertex alpha must stay 1 to
                // keep the border opaque and the glow mask black; pulse the RGB intensity instead.
                Color baseColor = _baseColors[i];
                _borders[i].color = new Color(baseColor.r * intensity, baseColor.g * intensity, baseColor.b * intensity, baseColor.a);
            }
        }
    }
}
