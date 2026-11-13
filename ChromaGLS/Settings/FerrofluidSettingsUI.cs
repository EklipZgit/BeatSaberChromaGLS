using System;
using System.IO;
using System.Reflection;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Parser;
using HMUI;
using JetBrains.Annotations;
using UnityEngine;

namespace ChromaGLS.Settings
{
    internal sealed class FerrofluidSettingsUI : System.IDisposable
    {
        private const string ModalResourceName = "ChromaGLS.Settings.ferrofluid-settings.bsml";

        private readonly Config _config;
        private readonly FerrofluidTweakEditSession _session;
        private GameObject? _markupRoot;
        private RectTransform? _previewAnchor;
        private BSMLParserParams? _parserParams;
        private bool _loading;

        [UIObject("ferro-settings-modal")]
        private GameObject? _modalObject;

        internal FerrofluidSettingsUI(Config config)
            : this(config, Plugin.ApplyFerrofluidSettings)
        {
        }

        internal FerrofluidSettingsUI(Config config, Action<Config> apply)
        {
            _config = config;
            _session = new FerrofluidTweakEditSession(config, apply);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-floor-assistance-enabled")]
        public bool FloorAssistanceEnabled
        {
            get => _config.FerrofluidFloorAssistanceEnabled;
            set => Preview(config => config.FerrofluidFloorAssistanceEnabled = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-cube-floor-friction")]
        public float CubeFloorFriction
        {
            get => _config.FerrofluidCubeFloorFriction;
            set => Preview(config => config.FerrofluidCubeFloorFriction = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-cube-corner-friction")]
        public float CubeCornerFriction
        {
            get => _config.FerrofluidCubeCornerFriction;
            set => Preview(config => config.FerrofluidCubeCornerFriction = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-cube-face-landing-strength")]
        public float CubeFaceLandingStrength
        {
            get => _config.FerrofluidCubeFaceLandingStrength;
            set => Preview(config => config.FerrofluidCubeFaceLandingStrength = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-cube-face-damping-strength")]
        public float CubeFaceDampingStrength
        {
            get => _config.FerrofluidCubeFaceDampingStrength;
            set => Preview(config => config.FerrofluidCubeFaceDampingStrength = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-cube-face-damping-exponent")]
        public float CubeFaceDampingExponent
        {
            get => _config.FerrofluidCubeFaceDampingExponent;
            set => Preview(config => config.FerrofluidCubeFaceDampingExponent = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-cube-face-damping-start-angle")]
        public float CubeFaceDampingStartAngle
        {
            get => _config.FerrofluidCubeFaceDampingStartAngle;
            set => Preview(config => config.FerrofluidCubeFaceDampingStartAngle = value);
        }

        [UsedImplicitly]
        [UIValue("simulation-steps")]
        public float SimulationSteps
        {
            get => _config.SimulationSteps;
            set => Preview(config => config.SimulationSteps = Mathf.RoundToInt(value));
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-base-red")]
        public float FerrofluidBaseRed
        {
            get => _config.FerrofluidBaseRed;
            set => Preview(config => config.FerrofluidBaseRed = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-base-green")]
        public float FerrofluidBaseGreen
        {
            get => _config.FerrofluidBaseGreen;
            set => Preview(config => config.FerrofluidBaseGreen = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-base-blue")]
        public float FerrofluidBaseBlue
        {
            get => _config.FerrofluidBaseBlue;
            set => Preview(config => config.FerrofluidBaseBlue = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-room-reflection-strength")]
        public float FerrofluidRoomReflectionStrength
        {
            get => _config.FerrofluidRoomReflectionStrength;
            set => Preview(config => config.FerrofluidRoomReflectionStrength = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-feet-reflection-strength")]
        public float FerrofluidFeetReflectionStrength
        {
            get => _config.FerrofluidFeetReflectionStrength;
            set => Preview(config => config.FerrofluidFeetReflectionStrength = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-parabola-reflection-strength")]
        public float FerrofluidParabolaReflectionStrength
        {
            get => _config.FerrofluidParabolaReflectionStrength;
            set => Preview(config => config.FerrofluidParabolaReflectionStrength = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-parabola-reflection-saturation")]
        public float FerrofluidParabolaReflectionSaturation
        {
            get => _config.FerrofluidParabolaReflectionSaturation;
            set => Preview(config => config.FerrofluidParabolaReflectionSaturation = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-thin-film-strength")]
        public float FerrofluidThinFilmStrength
        {
            get => _config.FerrofluidThinFilmStrength;
            set => Preview(config => config.FerrofluidThinFilmStrength = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-thin-film-angle-rate")]
        public float FerrofluidThinFilmAngleRate
        {
            get => _config.FerrofluidThinFilmAngleRate;
            set => Preview(config => config.FerrofluidThinFilmAngleRate = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-spikes-enabled")]
        public bool FerrofluidSpikesEnabled
        {
            get => _config.FerrofluidSpikesEnabled;
            set => Preview(config => config.FerrofluidSpikesEnabled = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-body-enabled")]
        public bool FerrofluidBodyEnabled
        {
            get => _config.FerrofluidBodyEnabled;
            set => Preview(config => config.FerrofluidBodyEnabled = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-emission-strength")]
        public float FerrofluidEmissionStrength
        {
            get => _config.FerrofluidEmissionStrength;
            set => Preview(config => config.FerrofluidEmissionStrength = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-home-elasticity-gain")]
        public float FerrofluidHomeElasticityGain
        {
            get => _config.FerrofluidHomeElasticityGain;
            set => Preview(config => config.FerrofluidHomeElasticityGain = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-home-quadratic-power")]
        public float FerrofluidHomeQuadraticPower
        {
            get => _config.FerrofluidHomeQuadraticPower;
            set => Preview(config => config.FerrofluidHomeQuadraticPower = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-home-soft-range")]
        public float FerrofluidHomeSoftRange
        {
            get => _config.FerrofluidHomeSoftRange;
            set => Preview(config => config.FerrofluidHomeSoftRange = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-home-travel-limit")]
        public float FerrofluidHomeTravelLimit
        {
            get => _config.FerrofluidHomeTravelLimit;
            set => Preview(config => config.FerrofluidHomeTravelLimit = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-home-damping-gain")]
        public float FerrofluidHomeDampingGain
        {
            get => _config.FerrofluidHomeDampingGain;
            set => Preview(config => config.FerrofluidHomeDampingGain = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-exposure")]
        public float FerrofluidExposure
        {
            get => _config.FerrofluidExposure;
            set => Preview(config => config.FerrofluidExposure = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-lut-scale")]
        public float FerrofluidLutScale
        {
            get => _config.FerrofluidLutScale;
            set => Preview(config => config.FerrofluidLutScale = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-lut-offset")]
        public float FerrofluidLutOffset
        {
            get => _config.FerrofluidLutOffset;
            set => Preview(config => config.FerrofluidLutOffset = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-reflectivity")]
        public float FerrofluidReflectivity
        {
            get => _config.FerrofluidReflectivity;
            set => Preview(config => config.FerrofluidReflectivity = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-spike-base-size")]
        public float FerrofluidSpikeBaseSize
        {
            get => _config.FerrofluidSpikeBaseSize;
            set => Preview(config => config.FerrofluidSpikeBaseSize = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-spike-height-scale")]
        public float FerrofluidSpikeHeightScale
        {
            get => _config.FerrofluidSpikeHeightScale;
            set => Preview(config => config.FerrofluidSpikeHeightScale = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-spike-mound-curvature")]
        public float FerrofluidSpikeMoundCurvature
        {
            get => _config.FerrofluidSpikeMoundCurvature;
            set => Preview(config => config.FerrofluidSpikeMoundCurvature = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-spike-density")]
        public float FerrofluidSpikeDensity
        {
            get => _config.FerrofluidSpikeDensity;
            set => Preview(config => config.FerrofluidSpikeDensity = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-spike-base-cutoff")]
        public float FerrofluidSpikeBaseCutoff
        {
            get => _config.FerrofluidSpikeBaseCutoff;
            set => Preview(config => config.FerrofluidSpikeBaseCutoff = value);
        }

        [UsedImplicitly]
        [UIValue("particle-mass")]
        public float ParticleMass
        {
            get => _config.ParticleMass;
            set => Preview(config => config.ParticleMass = value);
        }

        [UsedImplicitly]
        [UIValue("rest-density")]
        public float RestDensity
        {
            get => _config.RestDensity;
            set => Preview(config => config.RestDensity = value);
        }

        [UsedImplicitly]
        [UIValue("gas-constant")]
        public float GasConstant
        {
            get => _config.GasConstant;
            set => Preview(config => config.GasConstant = value);
        }

        [UsedImplicitly]
        [UIValue("viscosity")]
        public float Viscosity
        {
            get => _config.Viscosity;
            set => Preview(config => config.Viscosity = value);
        }

        [UsedImplicitly]
        [UIValue("magnet-radius")]
        public float MagnetRadius
        {
            get => _config.MagnetRadius;
            set => Preview(config => config.MagnetRadius = value);
        }

        [UsedImplicitly]
        [UIValue("magnet-maximum-slide")]
        public float MagnetMaximumSlide
        {
            get => _config.MagnetMaximumSlide;
            set => Preview(config => config.MagnetMaximumSlide = value);
        }

        [UsedImplicitly]
        [UIValue("magnet-slide-fraction")]
        public float MagnetSlideFraction
        {
            get => _config.MagnetSlideFraction;
            set => Preview(config => config.MagnetSlideFraction = value);
        }

        [UsedImplicitly]
        [UIValue("magnet-slide-falloff-exponent")]
        public float MagnetSlideFalloffExponent
        {
            get => _config.MagnetSlideFalloffExponent;
            set => Preview(config => config.MagnetSlideFalloffExponent = value);
        }

        [UsedImplicitly]
        [UIValue("magnet-elasticity")]
        public float MagnetElasticity
        {
            get => _config.MagnetElasticity;
            set => Preview(config => config.MagnetElasticity = value);
        }

        [UsedImplicitly]
        [UIValue("magnet-damping")]
        public float MagnetDamping
        {
            get => _config.MagnetDamping;
            set => Preview(config => config.MagnetDamping = value);
        }

        [UsedImplicitly]
        [UIValue("magnet-forward-distance")]
        public float MagnetForwardDistance
        {
            get => _config.MagnetForwardDistance;
            set => Preview(config => config.MagnetForwardDistance = value);
        }

        [UsedImplicitly]
        [UIValue("bulge-radius")]
        public float BulgeRadius
        {
            get => _config.BulgeRadius;
            set => Preview(config => config.BulgeRadius = value);
        }

        [UsedImplicitly]
        [UIValue("bulge-height")]
        public float BulgeHeight
        {
            get => _config.BulgeHeight;
            set => Preview(config => config.BulgeHeight = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-spike-height-radius")]
        public float FerrofluidSpikeHeightRadius
        {
            get => _config.FerrofluidSpikeHeightRadius;
            set => Preview(config => config.FerrofluidSpikeHeightRadius = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-spike-tilt-radius")]
        public float FerrofluidSpikeTiltRadius
        {
            get => _config.FerrofluidSpikeTiltRadius;
            set => Preview(config => config.FerrofluidSpikeTiltRadius = value);
        }

        [UsedImplicitly]
        [UIValue("height-falloff-exponent")]
        public float HeightFalloffExponent
        {
            get => _config.HeightFalloffExponent;
            set => Preview(config => config.HeightFalloffExponent = value);
        }

        [UsedImplicitly]
        [UIValue("peak-width")]
        public float PeakWidth
        {
            get => _config.PeakWidth;
            set => Preview(config => config.PeakWidth = value);
        }

        [UsedImplicitly]
        [UIValue("height-gain")]
        public float HeightGain
        {
            get => _config.HeightGain;
            set => Preview(config => config.HeightGain = value);
        }

        [UsedImplicitly]
        [UIValue("back-height-loss")]
        public float BackHeightLoss
        {
            get => _config.BackHeightLoss;
            set => Preview(config => config.BackHeightLoss = value);
        }

        [UsedImplicitly]
        [UIValue("back-height-falloff-exponent")]
        public float BackHeightFalloffExponent
        {
            get => _config.BackHeightFalloffExponent;
            set => Preview(config => config.BackHeightFalloffExponent = value);
        }

        [UsedImplicitly]
        [UIValue("magnet-height-strength-cap")]
        public float MagnetHeightStrengthCap
        {
            get => _config.MagnetHeightStrengthCap;
            set => Preview(config => config.MagnetHeightStrengthCap = value);
        }

        [UsedImplicitly]
        [UIValue("magnet-height-dead-zone-radius")]
        public float MagnetHeightDeadZoneRadius
        {
            get => _config.MagnetHeightDeadZoneRadius;
            set => Preview(config => config.MagnetHeightDeadZoneRadius = value);
        }

        [UsedImplicitly]
        [UIValue("maximum-height")]
        public float MaximumHeight
        {
            get => _config.MaximumHeight;
            set => Preview(config => config.MaximumHeight = value);
        }

        [UsedImplicitly]
        [UIValue("maximum-tilt-degrees")]
        public float MaximumTiltDegrees
        {
            get => _config.MaximumTiltDegrees;
            set => Preview(config => config.MaximumTiltDegrees = value);
        }

        [UsedImplicitly]
        [UIValue("tilt-peak-radius-multiplier")]
        public float TiltPeakRadiusMultiplier
        {
            get => _config.TiltPeakRadiusMultiplier;
            set => Preview(config => config.TiltPeakRadiusMultiplier = value);
        }

        [UsedImplicitly]
        [UIValue("tilt-return-radius-multiplier")]
        public float TiltReturnRadiusMultiplier
        {
            get => _config.TiltReturnRadiusMultiplier;
            set => Preview(config => config.TiltReturnRadiusMultiplier = value);
        }

        [UsedImplicitly]
        [UIValue("tilt-rise-exponent")]
        public float TiltRiseExponent
        {
            get => _config.TiltRiseExponent;
            set => Preview(config => config.TiltRiseExponent = value);
        }

        [UsedImplicitly]
        [UIValue("tilt-return-exponent")]
        public float TiltReturnExponent
        {
            get => _config.TiltReturnExponent;
            set => Preview(config => config.TiltReturnExponent = value);
        }

        [UsedImplicitly]
        [UIValue("inner-corner-fan")]
        public float InnerCornerFan
        {
            get => _config.InnerCornerFan;
            set => Preview(config => config.InnerCornerFan = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-spike-spring-strength")]
        public float FerrofluidSpikeSpringStrength
        {
            get => _config.FerrofluidSpikeSpringStrength;
            set => Preview(config => config.FerrofluidSpikeSpringStrength = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-spike-spring-damping")]
        public float FerrofluidSpikeSpringDamping
        {
            get => _config.FerrofluidSpikeSpringDamping;
            set => Preview(config => config.FerrofluidSpikeSpringDamping = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-primitive-spike-size")]
        public float FerrofluidPrimitiveSpikeSize
        {
            get => _config.FerrofluidPrimitiveSpikeSize;
            set => Preview(config => config.FerrofluidPrimitiveSpikeSize = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-primitive-tip-roundness")]
        public float FerrofluidPrimitiveTipRoundness
        {
            get => _config.FerrofluidPrimitiveTipRoundness;
            set => Preview(config => config.FerrofluidPrimitiveTipRoundness = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-primitive-tip-cap-rounding")]
        public float FerrofluidPrimitiveTipCapRounding
        {
            get => _config.FerrofluidPrimitiveTipCapRounding;
            set => Preview(config => config.FerrofluidPrimitiveTipCapRounding = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-primitive-flatten-radius")]
        public float FerrofluidPrimitiveFlattenRadius
        {
            get => _config.FerrofluidPrimitiveFlattenRadius;
            set => Preview(config => config.FerrofluidPrimitiveFlattenRadius = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-primitive-flatten-exponent")]
        public float FerrofluidPrimitiveFlattenExponent
        {
            get => _config.FerrofluidPrimitiveFlattenExponent;
            set => Preview(config => config.FerrofluidPrimitiveFlattenExponent = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-primitive-global-flatten")]
        public float FerrofluidPrimitiveGlobalFlatten
        {
            get => _config.FerrofluidPrimitiveGlobalFlatten;
            set => Preview(config => config.FerrofluidPrimitiveGlobalFlatten = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-sphere-bulge-scale")]
        public float FerrofluidSphereBulgeScale
        {
            get => _config.FerrofluidSphereBulgeScale;
            set => Preview(config => config.FerrofluidSphereBulgeScale = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-cube-spike-density")]
        public float FerrofluidCubeSpikeDensity
        {
            get => _config.FerrofluidCubeSpikeDensity;
            set => Preview(config => config.FerrofluidCubeSpikeDensity = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-sphere-spike-density")]
        public float FerrofluidSphereSpikeDensity
        {
            get => _config.FerrofluidSphereSpikeDensity;
            set => Preview(config => config.FerrofluidSphereSpikeDensity = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-cube-roundness")]
        public float FerrofluidCubeRoundness
        {
            get => _config.FerrofluidCubeRoundness;
            set => Preview(config => config.FerrofluidCubeRoundness = value);
        }

        [UsedImplicitly]
        [UIValue("ferrofluid-sphere-chart-yaw")]
        public float FerrofluidSphereChartYaw
        {
            get => _config.FerrofluidSphereChartYaw;
            set => Preview(config => config.FerrofluidSphereChartYaw = value);
        }

        internal void Open(RectTransform anchor)
        {
            if (_markupRoot == null)
            {
                _markupRoot = new GameObject("ChromaGLS Ferrofluid Settings", typeof(RectTransform));
                _markupRoot.transform.SetParent(anchor, false);
                ParseModal();
            }

            if (_parserParams == null)
                return;

            _previewAnchor = anchor;
            OpenSession(() => _parserParams.EmitEvent("show-ferro"));
            ChromaGlsRequirementEffect.CreatePreview(anchor);
        }

        internal void OpenSession(Action showModal)
        {
            // The first native ModalView.Show briefly disables the modal (SetupView),
            // which fires CloseWithoutSaving; the show must precede the editing snapshot.
            showModal();
            _session.Open();
            RefreshControls();
        }

        internal void CloseWithoutSaving()
        {
            if (!_session.IsEditing)
                return;

            _session.Cancel();
            HideModal();
        }

        internal void CloseForGameplay()
        {
            _session.Cancel();
            HideModal();
            TeardownModal();
        }

        public void Dispose()
        {
            CloseWithoutSaving();
            TeardownModal();
        }

        private void TeardownModal()
        {
            if (_modalObject != null)
            {
                if (_modalObject.TryGetComponent(out ModalView modalView))
                    modalView.Hide(false);
                UnityEngine.Object.Destroy(_modalObject);
                _modalObject = null;
            }

            _parserParams = null;
            if (_markupRoot != null)
            {
                UnityEngine.Object.Destroy(_markupRoot);
                _markupRoot = null;
            }
        }

        private void ParseModal()
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ModalResourceName);
            if (stream == null)
            {
                Plugin.Log.Warn("[ChromaGLS Ferro] the tweak view markup is missing; the TWEAK FERRO button will do nothing.");
                return;
            }

            using StreamReader reader = new(stream);
#if PRE_V1_40_8
            _parserParams = BSMLParser.instance.Parse(reader.ReadToEnd(), _markupRoot, this);
#else
            _parserParams = BSMLParser.Instance.Parse(reader.ReadToEnd(), _markupRoot, this);
#endif
            _modalObject!.AddComponent<FerrofluidSettingsAnchorLifecycle>().Initialize(this);
        }

        private void HideModal()
        {
            if (_previewAnchor != null)
                ChromaGlsRequirementEffect.RemovePreview(_previewAnchor);

            _parserParams?.EmitEvent("hide-ferro");
        }

        private void Preview(Action<Config> update)
        {
            if (!_loading)
                _session.Preview(update);
        }

        private void RefreshControls()
        {
            _loading = true;
            try
            {
                _parserParams?.EmitEvent("refresh-ferro");
            }
            finally
            {
                _loading = false;
            }
        }

        [UsedImplicitly]
        [UIAction("ferro-cancel")]
        private void HandleCancel()
        {
            _session.Cancel();
            HideModal();
        }

        [UsedImplicitly]
        [UIAction("ferro-reset")]
        private void HandleReset()
        {
            _session.Reset();
            RefreshControls();
        }

        [UsedImplicitly]
        [UIAction("ferro-save")]
        private void HandleSave()
        {
            _session.Save();
            HideModal();
        }
    }

    internal sealed class FerrofluidSettingsAnchorLifecycle : MonoBehaviour
    {
        private FerrofluidSettingsUI? _controller;

        internal void Initialize(FerrofluidSettingsUI controller) => _controller = controller;

        private void OnDisable() => _controller?.CloseWithoutSaving();
    }
}
