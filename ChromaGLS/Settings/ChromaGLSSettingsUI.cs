using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.GameplaySetup;
using JetBrains.Annotations;
using UnityEngine;

namespace ChromaGLS.Settings
{
    internal class ChromaGLSSettingsUI : System.IDisposable
    {
        internal const bool FerrofluidTweakMenuEnabled =
#if FERROFLUID_TWEAKS
            true;
#else
            false;
#endif

        private readonly Config _config;
        private readonly GameplaySetup _gameplaySetup;

        [UIComponent("ferrofluid-preview-anchor")]
        private RectTransform _ferrofluidPreviewAnchor = null!;

        [UsedImplicitly]
#if !V1_29_1
        private ChromaGLSSettingsUI(
            Config config,
            GameplaySetup gameplaySetup)
#else
        private ChromaGLSSettingsUI(Config config)
#endif
        {
            _config = config;
#if !V1_29_1
            _gameplaySetup = gameplaySetup;
#else
            _gameplaySetup = GameplaySetup.instance;
#endif

            // Build with -p:FerrofluidTweaks=true to show the session-only tuning controls.
#if FERROFLUID_TWEAKS
            _gameplaySetup.AddTab("ChromaGLS", "ChromaGLS.Settings.modifiers.bsml", this);
#else
            _gameplaySetup.AddTab("ChromaGLS", "ChromaGLS.Settings.modifiers-minimal.bsml", this);
#endif
        }

#pragma warning disable CA1822
        [UsedImplicitly]
        [UIValue("enabled")]
        public bool Enabled
        {
            get => _config.Enabled;
            set
            {
                _config.Enabled = value;
                Plugin.SetEnabled(value);
            }
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-border-enabled")]
        public bool FerrofluidBorderEnabled
        {
            get => _config.FerrofluidBorderEnabled;
            set
            {
                _config.FerrofluidBorderEnabled = value;
                Plugin.SetFerrofluidBorderEnabled(value);
            }
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-cube-enabled")]
        public bool FerrofluidCubeEnabled
        {
            get => _config.FerrofluidCubeEnabled;
            set
            {
                _config.FerrofluidCubeEnabled = value;
                Plugin.SetFerrofluidCubeEnabled(value);
            }
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-sphere-enabled")]
        public bool FerrofluidSphereEnabled
        {
            get => _config.FerrofluidSphereEnabled;
            set
            {
                _config.FerrofluidSphereEnabled = value;
                Plugin.SetFerrofluidSphereEnabled(value);
            }
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-spikes-enabled")]
        public bool FerrofluidSpikesEnabled
        {
            get => _config.FerrofluidSpikesEnabled;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidSpikesEnabled = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-body-enabled")]
        public bool FerrofluidBodyEnabled
        {
            get => _config.FerrofluidBodyEnabled;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidBodyEnabled = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-emission-strength")]
        public float FerrofluidEmissionStrength
        {
            get => _config.FerrofluidEmissionStrength;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidEmissionStrength = value);
        }

        // Compensate for Beat Saber's HDR bloom when displaying the ferrofluid shader's colors.
        [UsedImplicitly]
        [UIValue("ferrofluid-exposure")]
        public float FerrofluidExposure
        {
            get => _config.FerrofluidExposure;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidExposure = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-lut-scale")]
        public float FerrofluidLutScale
        {
            get => _config.FerrofluidLutScale;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidLutScale = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-lut-offset")]
        public float FerrofluidLutOffset
        {
            get => _config.FerrofluidLutOffset;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidLutOffset = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-reflectivity")]
        public float FerrofluidReflectivity
        {
            get => _config.FerrofluidReflectivity;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidReflectivity = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-matte-sheen")]
        public float FerrofluidMatteSheen
        {
            get => _config.FerrofluidMatteSheen;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidMatteSheen = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-matte-angle")]
        public float FerrofluidMatteAngle
        {
            get => _config.FerrofluidMatteAngle;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidMatteAngle = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-reflection-angle")]
        public float FerrofluidReflectionAngle
        {
            get => _config.FerrofluidReflectionAngle;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidReflectionAngle = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-matte-rotation-x")]
        public float FerrofluidMatteRotationX
        {
            get => _config.FerrofluidMatteRotationX;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidMatteRotationX = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-matte-rotation-y")]
        public float FerrofluidMatteRotationY
        {
            get => _config.FerrofluidMatteRotationY;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidMatteRotationY = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-matte-rotation-z")]
        public float FerrofluidMatteRotationZ
        {
            get => _config.FerrofluidMatteRotationZ;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidMatteRotationZ = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-reflection-rotation-x")]
        public float FerrofluidReflectionRotationX
        {
            get => _config.FerrofluidReflectionRotationX;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidReflectionRotationX = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-reflection-rotation-y")]
        public float FerrofluidReflectionRotationY
        {
            get => _config.FerrofluidReflectionRotationY;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidReflectionRotationY = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-reflection-rotation-z")]
        public float FerrofluidReflectionRotationZ
        {
            get => _config.FerrofluidReflectionRotationZ;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidReflectionRotationZ = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-spike-base-size")]
        public float FerrofluidSpikeBaseSize
        {
            get => _config.FerrofluidSpikeBaseSize;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidSpikeBaseSize = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-spike-height-scale")]
        public float FerrofluidSpikeHeightScale
        {
            get => _config.FerrofluidSpikeHeightScale;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidSpikeHeightScale = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-spike-mound-curvature")]
        public float FerrofluidSpikeMoundCurvature
        {
            get => _config.FerrofluidSpikeMoundCurvature;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidSpikeMoundCurvature = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-spike-density")]
        public float FerrofluidSpikeDensity
        {
            get => _config.FerrofluidSpikeDensity;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidSpikeDensity = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-spike-base-cutoff")]
        public float FerrofluidSpikeBaseCutoff
        {
            get => _config.FerrofluidSpikeBaseCutoff;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidSpikeBaseCutoff = value);
        }

        [UsedImplicitly]
        [UIValue("particle-mass")]
        public float ParticleMass
        {
            get => _config.ParticleMass;
            set => ApplyFerrofluidSetting(() => _config.ParticleMass = value);
        }

        [UsedImplicitly]
        [UIValue("rest-density")]
        public float RestDensity
        {
            get => _config.RestDensity;
            set => ApplyFerrofluidSetting(() => _config.RestDensity = value);
        }

        [UsedImplicitly]
        [UIValue("gas-constant")]
        public float GasConstant
        {
            get => _config.GasConstant;
            set => ApplyFerrofluidSetting(() => _config.GasConstant = value);
        }

        [UsedImplicitly]
        [UIValue("viscosity")]
        public float Viscosity
        {
            get => _config.Viscosity;
            set => ApplyFerrofluidSetting(() => _config.Viscosity = value);
        }

        [UsedImplicitly]
        [UIValue("simulation-steps")]
        public float SimulationSteps
        {
            get => _config.SimulationSteps;
            set => ApplyFerrofluidSetting(() => _config.SimulationSteps = Mathf.RoundToInt(value));
        }

        [UsedImplicitly]
        [UIValue("magnet-radius")]
        public float MagnetRadius
        {
            get => _config.MagnetRadius;
            set => ApplyFerrofluidSetting(() => _config.MagnetRadius = value);
        }

        [UsedImplicitly]
        [UIValue("magnet-maximum-slide")]
        public float MagnetMaximumSlide
        {
            get => _config.MagnetMaximumSlide;
            set => ApplyFerrofluidSetting(() => _config.MagnetMaximumSlide = value);
        }

        [UsedImplicitly]
        [UIValue("magnet-slide-fraction")]
        public float MagnetSlideFraction
        {
            get => _config.MagnetSlideFraction;
            set => ApplyFerrofluidSetting(() => _config.MagnetSlideFraction = value);
        }

        [UsedImplicitly]
        [UIValue("magnet-slide-falloff-exponent")]
        public float MagnetSlideFalloffExponent
        {
            get => _config.MagnetSlideFalloffExponent;
            set => ApplyFerrofluidSetting(() => _config.MagnetSlideFalloffExponent = value);
        }

        [UsedImplicitly]
        [UIValue("magnet-elasticity")]
        public float MagnetElasticity
        {
            get => _config.MagnetElasticity;
            set => ApplyFerrofluidSetting(() => _config.MagnetElasticity = value);
        }

        [UsedImplicitly]
        [UIValue("magnet-damping")]
        public float MagnetDamping
        {
            get => _config.MagnetDamping;
            set => ApplyFerrofluidSetting(() => _config.MagnetDamping = value);
        }

        [UsedImplicitly]
        [UIValue("magnet-forward-distance")]
        public float MagnetForwardDistance
        {
            get => _config.MagnetForwardDistance;
            set => ApplyFerrofluidSetting(() => _config.MagnetForwardDistance = value);
        }

        [UsedImplicitly]
        [UIValue("bulge-radius")]
        public float BulgeRadius
        {
            get => _config.BulgeRadius;
            set => ApplyFerrofluidSetting(() => _config.BulgeRadius = value);
        }

        [UsedImplicitly]
        [UIValue("bulge-height")]
        public float BulgeHeight
        {
            get => _config.BulgeHeight;
            set => ApplyFerrofluidSetting(() => _config.BulgeHeight = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-spike-height-radius")]
        public float FerrofluidSpikeHeightRadius
        {
            get => _config.FerrofluidSpikeHeightRadius;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidSpikeHeightRadius = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-spike-tilt-radius")]
        public float FerrofluidSpikeTiltRadius
        {
            get => _config.FerrofluidSpikeTiltRadius;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidSpikeTiltRadius = value);
        }

        [UsedImplicitly]
        [UIValue("height-falloff-exponent")]
        public float HeightFalloffExponent
        {
            get => _config.HeightFalloffExponent;
            set => ApplyFerrofluidSetting(() => _config.HeightFalloffExponent = value);
        }

        [UsedImplicitly]
        [UIValue("peak-width")]
        public float PeakWidth
        {
            get => _config.PeakWidth;
            set => ApplyFerrofluidSetting(() => _config.PeakWidth = value);
        }

        [UsedImplicitly]
        [UIValue("height-gain")]
        public float HeightGain
        {
            get => _config.HeightGain;
            set => ApplyFerrofluidSetting(() => _config.HeightGain = value);
        }

        [UsedImplicitly]
        [UIValue("back-height-loss")]
        public float BackHeightLoss
        {
            get => _config.BackHeightLoss;
            set => ApplyFerrofluidSetting(() => _config.BackHeightLoss = value);
        }

        [UsedImplicitly]
        [UIValue("back-height-falloff-exponent")]
        public float BackHeightFalloffExponent
        {
            get => _config.BackHeightFalloffExponent;
            set => ApplyFerrofluidSetting(() => _config.BackHeightFalloffExponent = value);
        }

        [UsedImplicitly]
        [UIValue("magnet-height-strength-cap")]
        public float MagnetHeightStrengthCap
        {
            get => _config.MagnetHeightStrengthCap;
            set => ApplyFerrofluidSetting(() => _config.MagnetHeightStrengthCap = value);
        }

        [UsedImplicitly]
        [UIValue("magnet-height-dead-zone-radius")]
        public float MagnetHeightDeadZoneRadius
        {
            get => _config.MagnetHeightDeadZoneRadius;
            set => ApplyFerrofluidSetting(() => _config.MagnetHeightDeadZoneRadius = value);
        }

        [UsedImplicitly]
        [UIValue("maximum-height")]
        public float MaximumHeight
        {
            get => _config.MaximumHeight;
            set => ApplyFerrofluidSetting(() => _config.MaximumHeight = value);
        }

        [UsedImplicitly]
        [UIValue("maximum-tilt-degrees")]
        public float MaximumTiltDegrees
        {
            get => _config.MaximumTiltDegrees;
            set => ApplyFerrofluidSetting(() => _config.MaximumTiltDegrees = value);
        }

        [UsedImplicitly]
        [UIValue("tilt-peak-radius-multiplier")]
        public float TiltPeakRadiusMultiplier
        {
            get => _config.TiltPeakRadiusMultiplier;
            set => ApplyFerrofluidSetting(() => _config.TiltPeakRadiusMultiplier = value);
        }

        [UsedImplicitly]
        [UIValue("tilt-return-radius-multiplier")]
        public float TiltReturnRadiusMultiplier
        {
            get => _config.TiltReturnRadiusMultiplier;
            set => ApplyFerrofluidSetting(() => _config.TiltReturnRadiusMultiplier = value);
        }

        [UsedImplicitly]
        [UIValue("tilt-rise-exponent")]
        public float TiltRiseExponent
        {
            get => _config.TiltRiseExponent;
            set => ApplyFerrofluidSetting(() => _config.TiltRiseExponent = value);
        }

        [UsedImplicitly]
        [UIValue("tilt-return-exponent")]
        public float TiltReturnExponent
        {
            get => _config.TiltReturnExponent;
            set => ApplyFerrofluidSetting(() => _config.TiltReturnExponent = value);
        }

        [UsedImplicitly]
        [UIValue("inner-corner-fan")]
        public float InnerCornerFan
        {
            get => _config.InnerCornerFan;
            set => ApplyFerrofluidSetting(() => _config.InnerCornerFan = value);
        }

        // These two controls also update the info-button torus. The remaining controls affect only the cube and sphere.
        [UsedImplicitly]
        [UIValue("ferrofluid-spike-spring-strength")]
        public float FerrofluidSpikeSpringStrength
        {
            get => _config.FerrofluidSpikeSpringStrength;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidSpikeSpringStrength = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-spike-spring-damping")]
        public float FerrofluidSpikeSpringDamping
        {
            get => _config.FerrofluidSpikeSpringDamping;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidSpikeSpringDamping = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-primitive-spike-size")]
        public float FerrofluidPrimitiveSpikeSize
        {
            get => _config.FerrofluidPrimitiveSpikeSize;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidPrimitiveSpikeSize = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-primitive-tip-roundness")]
        public float FerrofluidPrimitiveTipRoundness
        {
            get => _config.FerrofluidPrimitiveTipRoundness;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidPrimitiveTipRoundness = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-primitive-tip-cap-rounding")]
        public float FerrofluidPrimitiveTipCapRounding
        {
            get => _config.FerrofluidPrimitiveTipCapRounding;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidPrimitiveTipCapRounding = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-primitive-ambient-scale")]
        public float FerrofluidPrimitiveAmbientScale
        {
            get => _config.FerrofluidPrimitiveAmbientScale;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidPrimitiveAmbientScale = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-primitive-flatten-radius")]
        public float FerrofluidPrimitiveFlattenRadius
        {
            get => _config.FerrofluidPrimitiveFlattenRadius;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidPrimitiveFlattenRadius = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-primitive-flatten-exponent")]
        public float FerrofluidPrimitiveFlattenExponent
        {
            get => _config.FerrofluidPrimitiveFlattenExponent;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidPrimitiveFlattenExponent = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-primitive-global-flatten")]
        public float FerrofluidPrimitiveGlobalFlatten
        {
            get => _config.FerrofluidPrimitiveGlobalFlatten;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidPrimitiveGlobalFlatten = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-sphere-bulge-scale")]
        public float FerrofluidSphereBulgeScale
        {
            get => _config.FerrofluidSphereBulgeScale;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidSphereBulgeScale = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-cube-spike-density")]
        public float FerrofluidCubeSpikeDensity
        {
            get => _config.FerrofluidCubeSpikeDensity;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidCubeSpikeDensity = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-sphere-spike-density")]
        public float FerrofluidSphereSpikeDensity
        {
            get => _config.FerrofluidSphereSpikeDensity;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidSphereSpikeDensity = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-cube-roundness")]
        public float FerrofluidCubeRoundness
        {
            get => _config.FerrofluidCubeRoundness;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidCubeRoundness = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-cube-chart-yaw")]
        public float FerrofluidCubeChartYaw
        {
            get => _config.FerrofluidCubeChartYaw;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidCubeChartYaw = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-sphere-chart-yaw")]
        public float FerrofluidSphereChartYaw
        {
            get => _config.FerrofluidSphereChartYaw;
            set => ApplyFerrofluidSetting(() => _config.FerrofluidSphereChartYaw = value);
        }
#pragma warning restore CA1822

        public void Dispose()
        {
            // Return the torus to SongCore when BSML destroys the settings tab.
            if (_ferrofluidPreviewAnchor != null)
            {
                ChromaGlsRequirementEffect.RemovePreview(_ferrofluidPreviewAnchor);
            }

            _gameplaySetup.RemoveTab("ChromaGLS");
        }

        // Create the preview GPU resources only while the ChromaGLS tab is visible.
        [UsedImplicitly]
        [UIAction("#post-parse")]
        private void HandlePostParse()
        {
#if FERROFLUID_TWEAKS
            FerrofluidPreviewAnchorLifecycle lifecycle = _ferrofluidPreviewAnchor.gameObject.AddComponent<FerrofluidPreviewAnchorLifecycle>();
            lifecycle.Initialize(_ferrofluidPreviewAnchor);
#endif
        }

        // Keep tweaks across tab changes for this session without saving them to disk.
        private void ApplyFerrofluidSetting(System.Action updateConfig)
        {
            updateConfig();
            Plugin.ApplyFerrofluidSettings(_config);
        }
    }

    internal sealed class FerrofluidPreviewAnchorLifecycle : MonoBehaviour
    {
        private RectTransform _anchor = null!;
        private bool _initialized;

        // OnEnable can run before BSML binds the fields, so initialize from either callback order.
        internal void Initialize(RectTransform anchor)
        {
            _anchor = anchor;
            _initialized = true;
            if (gameObject.activeInHierarchy)
            {
                ChromaGlsRequirementEffect.CreatePreview(_anchor);
            }
        }

        private void OnEnable()
        {
            if (_initialized)
            {
                ChromaGlsRequirementEffect.CreatePreview(_anchor);
            }
        }

        private void OnDisable()
        {
            if (_initialized)
            {
                ChromaGlsRequirementEffect.RemovePreview(_anchor);
            }
        }
    }
}
