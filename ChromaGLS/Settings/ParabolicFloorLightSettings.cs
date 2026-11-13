using System;
using Newtonsoft.Json;
using UnityEngine;

namespace ChromaGLS.Settings
{
    internal readonly struct ParabolicFloorLightSettings
    {
        internal const float WhiteBleedReach = 0.12f;

        internal static readonly ParabolicFloorLightSettings Default = new(0.43f, 0f, 0.86f, 2.47f, 3.2f, 0f, 0f, 0f, 1f, 0.96f, 1.25f, 1.3f, 0.04f, 4f);

        internal static readonly ParabolicFloorLightSettings FloorDefault = new(0.11f, 10f, 0f, 0f, 3.2f, 0f, 0f, 0f, 1.2f, 0.96f, 4.69f, 9.33f, 0.10f, 4f);

        [JsonProperty]
        internal readonly float Brightness;
        [JsonProperty]
        internal readonly float Red;
        [JsonProperty]
        internal readonly float Green;
        [JsonProperty]
        internal readonly float Blue;
        [JsonProperty]
        internal readonly float WidthScale;
        [JsonProperty]
        internal readonly float OffsetX;
        [JsonProperty]
        internal readonly float OffsetZ;
        [JsonProperty]
        internal readonly float Rotation;
        [JsonProperty]
        internal readonly float LightWidthScale;
        [JsonProperty]
        internal readonly float BloomWidthScale;
        [JsonProperty]
        internal readonly float BloomBrightness;
        [JsonProperty]
        internal readonly float HdrGlowThreshold;
        [JsonProperty]
        internal readonly float HdrGlowWidthScale;
        [JsonProperty]
        internal readonly float HdrGlowBrightness;

        [JsonConstructor]
        internal ParabolicFloorLightSettings(
            float brightness,
            float red,
            float green,
            float blue,
            float widthScale,
            float offsetX,
            float offsetZ,
            float rotation,
            float lightWidthScale,
            float bloomWidthScale = 0.96f,
            float bloomBrightness = 1.25f,
            float hdrGlowThreshold = 1.3f,
            float hdrGlowWidthScale = 0.04f,
            float hdrGlowBrightness = 4f)
        {
            Brightness = brightness;
            Red = red;
            Green = green;
            Blue = blue;
            WidthScale = widthScale;
            OffsetX = offsetX;
            OffsetZ = offsetZ;
            Rotation = rotation;
            LightWidthScale = lightWidthScale;
            BloomWidthScale = bloomWidthScale;
            BloomBrightness = bloomBrightness;
            HdrGlowThreshold = hdrGlowThreshold;
            HdrGlowWidthScale = hdrGlowWidthScale;
            HdrGlowBrightness = hdrGlowBrightness;
        }

        internal static ParabolicFloorLightSettings FromConfig(Config config)
            => new(
                config.ParabolicFloorLightBrightness,
                config.ParabolicFloorLightRed,
                config.ParabolicFloorLightGreen,
                config.ParabolicFloorLightBlue,
                config.ParabolicFloorLightWidthScale,
                config.ParabolicFloorLightOffsetX,
                config.ParabolicFloorLightOffsetZ,
                config.ParabolicFloorLightRotation,
                config.ParabolicFloorLightLightWidthScale,
                config.ParabolicFloorLightBloomWidthScale,
                config.ParabolicFloorLightBloomBrightness,
                config.ParabolicFloorLightHdrGlowThreshold,
                config.ParabolicFloorLightHdrGlowWidthScale,
                config.ParabolicFloorLightHdrGlowBrightness);

        internal Color EvaluateColor()
        {
            float brightness = Mathf.Max(0f, Brightness);
            Color tint = EvaluateTint(Mathf.SmoothStep(0f, 1f, (brightness - 0.5f) * 2f));
            return new Color(2f * brightness * tint.r, 2f * brightness * tint.g, 2f * brightness * tint.b, brightness);
        }

        internal float EvaluateBloomMultiplier()
            => Mathf.Max(0f, Brightness);

        internal float EvaluateHdrGlowAlpha()
            => Mathf.Sqrt(Mathf.Max(0f, Mathf.Max(Red, Mathf.Max(Green, Blue)) - HdrGlowThreshold));

        internal Color EvaluateGlowColor()
        {
            float brightness = Mathf.Max(0f, Brightness);
            return new Color(1f, 1f, 1f, 0.25f * brightness * brightness * Mathf.Max(0f, BloomBrightness));
        }

        internal float EvaluateGlowSpread()
        {
            float peak = Mathf.Max(Red, Mathf.Max(Green, Blue));
            return Mathf.Sqrt(Mathf.Max(1f, Brightness)) * Mathf.Sqrt(Mathf.Max(1f, peak));
        }

        internal Color EvaluateGlowTint(float distanceFromEdge, float whiteBleedReach = WhiteBleedReach)
        {
            float whitenStrength = Mathf.SmoothStep(0f, 1f, Brightness - 1f);
            float spatialWhite = whiteBleedReach > 0f
                ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, distanceFromEdge) / whiteBleedReach)
                : 0f;
            return EvaluateTint(whitenStrength * spatialWhite);
        }

        private Color EvaluateTint(float whiteMix)
        {
            float peak = Mathf.Max(Red, Mathf.Max(Green, Blue));
            return Color.Lerp(new Color(Red, Green, Blue, 1f), new Color(peak, peak, peak, 1f), whiteMix);
        }

        internal ParabolicFloorLightSettings WithBrightness(float brightness)
            => new(brightness, Red, Green, Blue, WidthScale, OffsetX, OffsetZ, Rotation, LightWidthScale, BloomWidthScale, BloomBrightness, HdrGlowThreshold, HdrGlowWidthScale, HdrGlowBrightness);

        internal ParabolicFloorLightSettings WithRed(float red)
            => new(Brightness, red, Green, Blue, WidthScale, OffsetX, OffsetZ, Rotation, LightWidthScale, BloomWidthScale, BloomBrightness, HdrGlowThreshold, HdrGlowWidthScale, HdrGlowBrightness);

        internal ParabolicFloorLightSettings WithGreen(float green)
            => new(Brightness, Red, green, Blue, WidthScale, OffsetX, OffsetZ, Rotation, LightWidthScale, BloomWidthScale, BloomBrightness, HdrGlowThreshold, HdrGlowWidthScale, HdrGlowBrightness);

        internal ParabolicFloorLightSettings WithBlue(float blue)
            => new(Brightness, Red, Green, blue, WidthScale, OffsetX, OffsetZ, Rotation, LightWidthScale, BloomWidthScale, BloomBrightness, HdrGlowThreshold, HdrGlowWidthScale, HdrGlowBrightness);

        internal ParabolicFloorLightSettings WithWidthScale(float widthScale)
            => new(Brightness, Red, Green, Blue, widthScale, OffsetX, OffsetZ, Rotation, LightWidthScale, BloomWidthScale, BloomBrightness, HdrGlowThreshold, HdrGlowWidthScale, HdrGlowBrightness);

        internal ParabolicFloorLightSettings WithOffsetX(float offsetX)
            => new(Brightness, Red, Green, Blue, WidthScale, offsetX, OffsetZ, Rotation, LightWidthScale, BloomWidthScale, BloomBrightness, HdrGlowThreshold, HdrGlowWidthScale, HdrGlowBrightness);

        internal ParabolicFloorLightSettings WithOffsetZ(float offsetZ)
            => new(Brightness, Red, Green, Blue, WidthScale, OffsetX, offsetZ, Rotation, LightWidthScale, BloomWidthScale, BloomBrightness, HdrGlowThreshold, HdrGlowWidthScale, HdrGlowBrightness);

        internal ParabolicFloorLightSettings WithRotation(float rotation)
            => new(Brightness, Red, Green, Blue, WidthScale, OffsetX, OffsetZ, rotation, LightWidthScale, BloomWidthScale, BloomBrightness, HdrGlowThreshold, HdrGlowWidthScale, HdrGlowBrightness);

        internal ParabolicFloorLightSettings WithLightWidthScale(float lightWidthScale)
            => new(Brightness, Red, Green, Blue, WidthScale, OffsetX, OffsetZ, Rotation, lightWidthScale, BloomWidthScale, BloomBrightness, HdrGlowThreshold, HdrGlowWidthScale, HdrGlowBrightness);

        internal ParabolicFloorLightSettings WithBloomWidthScale(float bloomWidthScale)
            => new(Brightness, Red, Green, Blue, WidthScale, OffsetX, OffsetZ, Rotation, LightWidthScale, bloomWidthScale, BloomBrightness, HdrGlowThreshold, HdrGlowWidthScale, HdrGlowBrightness);

        internal ParabolicFloorLightSettings WithBloomBrightness(float bloomBrightness)
            => new(Brightness, Red, Green, Blue, WidthScale, OffsetX, OffsetZ, Rotation, LightWidthScale, BloomWidthScale, bloomBrightness, HdrGlowThreshold, HdrGlowWidthScale, HdrGlowBrightness);

        internal ParabolicFloorLightSettings WithHdrGlowThreshold(float hdrGlowThreshold)
            => new(Brightness, Red, Green, Blue, WidthScale, OffsetX, OffsetZ, Rotation, LightWidthScale, BloomWidthScale, BloomBrightness, hdrGlowThreshold, HdrGlowWidthScale, HdrGlowBrightness);

        internal ParabolicFloorLightSettings WithHdrGlowWidthScale(float hdrGlowWidthScale)
            => new(Brightness, Red, Green, Blue, WidthScale, OffsetX, OffsetZ, Rotation, LightWidthScale, BloomWidthScale, BloomBrightness, HdrGlowThreshold, hdrGlowWidthScale, HdrGlowBrightness);

        internal ParabolicFloorLightSettings WithHdrGlowBrightness(float hdrGlowBrightness)
            => new(Brightness, Red, Green, Blue, WidthScale, OffsetX, OffsetZ, Rotation, LightWidthScale, BloomWidthScale, BloomBrightness, HdrGlowThreshold, HdrGlowWidthScale, hdrGlowBrightness);
    }

    internal static class ParabolicFloorLightSliderMapping
    {
        internal static float EvaluateIntensity(float slider)
        {
            float clamped = Mathf.Clamp01(slider);
            return clamped <= 0.5f
                ? 2f * clamped
                : 1f + (9f * ((2f * clamped) - 1f) * ((2f * clamped) - 1f));
        }

        internal static float EvaluateIntensitySlider(float intensity)
        {
            float clamped = Mathf.Clamp(intensity, 0f, 10f);
            return clamped <= 1f
                ? clamped * 0.5f
                : 0.5f + (0.5f * Mathf.Sqrt((clamped - 1f) / 9f));
        }

        internal static float EvaluateWidthScale(float slider)
        {
            float clamped = Mathf.Clamp01(slider);
            return 0.5f + (7.5f * clamped * clamped);
        }

        internal static float EvaluateWidthScaleSlider(float widthScale)
            => Mathf.Sqrt((Mathf.Clamp(widthScale, 0.5f, 8f) - 0.5f) / 7.5f);

        internal static float EvaluateLightWidthScale(float slider)
        {
            float clamped = Mathf.Clamp01(slider);
            return 0.001f + (3.999f * clamped * clamped);
        }

        internal static float EvaluateLightWidthScaleSlider(float lightWidthScale)
            => Mathf.Sqrt((Mathf.Clamp(lightWidthScale, 0.001f, 4f) - 0.001f) / 3.999f);

        internal static float EvaluateBloomWidthScale(float slider)
        {
            float clamped = Mathf.Clamp01(slider);
            return 4f * clamped * clamped;
        }

        internal static float EvaluateHdrGlowWidthScale(float slider)
        {
            float clamped = Mathf.Clamp01(slider);
            return clamped * clamped;
        }

        internal static float EvaluateHdrGlowWidthScaleSlider(float hdrGlowWidthScale)
            => Mathf.Sqrt(Mathf.Clamp01(hdrGlowWidthScale));

        internal static float EvaluateBloomWidthScaleSlider(float bloomWidthScale)
            => Mathf.Sqrt(Mathf.Clamp(bloomWidthScale, 0f, 4f) / 4f);
    }

    internal sealed class ParabolicFloorLightEditSession
    {
        private readonly Config _config;
        private readonly Action<ParabolicFloorLightSettings> _preview;
        private readonly Action<ParabolicFloorLightSettings>? _saved;
        private ParabolicFloorLightSettings _working;

        internal ParabolicFloorLightEditSession(Config config, Action<ParabolicFloorLightSettings> preview)
        {
            _config = config;
            _preview = preview;
        }

        internal ParabolicFloorLightEditSession(
            Config config,
            Action<ParabolicFloorLightSettings> preview,
            Action<ParabolicFloorLightSettings> saved)
            : this(config, preview)
        {
            _saved = saved;
        }

        internal bool IsEditing { get; private set; }

        internal ParabolicFloorLightSettings Working => _working;

        internal void Open()
        {
            if (IsEditing)
                return;

            _working = ParabolicFloorLightSettings.FromConfig(_config);
            IsEditing = true;
        }

        internal void Preview(ParabolicFloorLightSettings settings)
        {
            if (!IsEditing)
                return;

            _working = settings;
            _preview(settings);
        }

        internal void Save()
        {
            if (!IsEditing)
                return;

            _config.ParabolicFloorLightBrightness = _working.Brightness;
            _config.ParabolicFloorLightRed = _working.Red;
            _config.ParabolicFloorLightGreen = _working.Green;
            _config.ParabolicFloorLightBlue = _working.Blue;
            _config.ParabolicFloorLightWidthScale = _working.WidthScale;
            _config.ParabolicFloorLightOffsetX = _working.OffsetX;
            _config.ParabolicFloorLightOffsetZ = _working.OffsetZ;
            _config.ParabolicFloorLightRotation = _working.Rotation;
            _config.ParabolicFloorLightLightWidthScale = _working.LightWidthScale;
            _config.ParabolicFloorLightBloomWidthScale = _working.BloomWidthScale;
            _config.ParabolicFloorLightBloomBrightness = _working.BloomBrightness;
            _config.ParabolicFloorLightHdrGlowThreshold = _working.HdrGlowThreshold;
            _config.ParabolicFloorLightHdrGlowWidthScale = _working.HdrGlowWidthScale;
            _config.ParabolicFloorLightHdrGlowBrightness = _working.HdrGlowBrightness;
            IsEditing = false;
            _saved?.Invoke(_working);
        }

        internal void Cancel()
        {
            if (!IsEditing)
                return;

            IsEditing = false;
            _preview(ParabolicFloorLightSettings.FromConfig(_config));
        }
    }
}
