using System.Runtime.CompilerServices;
using IPA.Config.Stores;
using IPA.Config.Stores.Attributes;
using JetBrains.Annotations;

[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]

namespace ChromaGLS.Settings
{
    // The enable toggles and the parabola light settings persist. Ferrofluid tuning values marked [Ignore] reset when Beat Saber restarts.
    [UsedImplicitly(ImplicitUseTargetFlags.Members)]
    internal class Config
    {
        public virtual bool Enabled { get; set; } = true;

        public virtual bool FerrofluidBorderEnabled { get; set; } = true;

        public virtual bool FerrofluidCubeEnabled { get; set; } = false;

        public virtual bool FerrofluidSphereEnabled { get; set; } = false;

        public virtual bool FluidCubeEnabled { get; set; } = false;

        public virtual bool FluidSphereEnabled { get; set; } = false;

        public virtual ReflectionQuality ReflectionsQuality { get; set; } = ReflectionQuality.High;

        public virtual bool ReflectionsQualityInitialized { get; set; }

        public virtual bool FerrofluidReflectionMapsDeclined { get; set; }

        [Ignore]
        public virtual bool FerrofluidSpikesEnabled { get; set; } = true;

        [Ignore]
        public virtual bool FerrofluidBodyEnabled { get; set; } = true;

        [Ignore]
        public virtual float FerrofluidEmissionStrength { get; set; }

        [Ignore]
        public virtual float FerrofluidExposure { get; set; } = 1f;

        [Ignore]
        public virtual float FerrofluidLutScale { get; set; } = 3f;

        [Ignore]
        public virtual float FerrofluidLutOffset { get; set; } = 0.07f;

        [Ignore]
        public virtual float FerrofluidReflectivity { get; set; } = 0.21f;

        [Ignore]
        public virtual float FerrofluidBaseRed { get; set; } = 0.01f;

        [Ignore]
        public virtual float FerrofluidBaseGreen { get; set; } = 0.01f;

        [Ignore]
        public virtual float FerrofluidBaseBlue { get; set; } = 0.01f;

        [Ignore]
        public virtual float FerrofluidRoomReflectionStrength { get; set; } = 1f;

        [Ignore]
        public virtual float FerrofluidFeetReflectionStrength { get; set; } = 1f;

        [Ignore]
        public virtual float FerrofluidParabolaReflectionStrength { get; set; } = 1f;

        [Ignore]
        public virtual float FerrofluidParabolaReflectionSaturation { get; set; } = 1f;

        [Ignore]
        public virtual float FerrofluidThinFilmStrength { get; set; } = 1f;

        [Ignore]
        public virtual float FerrofluidThinFilmAngleRate { get; set; } = 1f;

        [Ignore]
        public virtual float FerrofluidSpikeBaseSize { get; set; } = 5.3f;

        [Ignore]
        public virtual float FerrofluidSpikeHeightScale { get; set; } = 0.9f;

        [Ignore]
        public virtual float FerrofluidSpikeMoundCurvature { get; set; } = 1.4f;

        [Ignore]
        public virtual float FerrofluidSpikeDensity { get; set; } = 0.35f;

        // Cut away the thin membrane that bilinear sampling leaves between spike cells.
        [Ignore]
        public virtual float FerrofluidSpikeBaseCutoff { get; set; } = 0.15f;

        [Ignore]
        public virtual float ParticleMass { get; set; } = 1.86f;

        [Ignore]
        public virtual float RestDensity { get; set; } = 2f;

        [Ignore]
        public virtual float GasConstant { get; set; } = 37f;

        [Ignore]
        public virtual float Viscosity { get; set; } = 8.7f;

        [Ignore]
        public virtual int SimulationSteps { get; set; } = 2;

        [Ignore]
        public virtual float MagnetRadius { get; set; } = 5.6f;

        [Ignore]
        public virtual float MagnetMaximumSlide { get; set; } = 4f;

        [Ignore]
        public virtual float MagnetSlideFraction { get; set; } = 0.23f;

        [Ignore]
        public virtual float MagnetSlideFalloffExponent { get; set; } = 0.85f;

        [Ignore]
        public virtual float MagnetElasticity { get; set; } = 82f;

        [Ignore]
        public virtual float MagnetDamping { get; set; } = 6.8f;

        // Measured in simulation units and scaled to the rendered surface.
        [Ignore]
        public virtual float MagnetForwardDistance { get; set; } = 0.07f;

        [Ignore]
        public virtual float BulgeRadius { get; set; } = 0.31f;

        [Ignore]
        public virtual float BulgeHeight { get; set; } = 0.156f;

        [Ignore]
        public virtual float FerrofluidSpikeHeightRadius { get; set; } = 0.31f;

        [Ignore]
        public virtual float FerrofluidSpikeTiltRadius { get; set; } = 0.73f;

        [Ignore]
        public virtual float HeightFalloffExponent { get; set; } = 3f;

        [Ignore]
        public virtual float PeakWidth { get; set; } = 0.5f;

        [Ignore]
        public virtual float HeightGain { get; set; } = 1.43f;

        [Ignore]
        public virtual float BackHeightLoss { get; set; } = 1.19f;

        [Ignore]
        public virtual float BackHeightFalloffExponent { get; set; } = 2.65f;

        // Cap magnetic height without limiting spike tilt.
        [Ignore]
        public virtual float MagnetHeightStrengthCap { get; set; } = 0.66f;

        // Radius around the magnet that uses the full height cap, without changing tilt.
        [Ignore]
        public virtual float MagnetHeightDeadZoneRadius { get; set; } = 0.13f;

        // Height used to normalize the cube and sphere lookup-table coordinates.
        [Ignore]
        public virtual float MaximumHeight { get; set; } = 1.6f;

        [Ignore]
        public virtual float MaximumTiltDegrees { get; set; } = 50f;

        // Measured as a multiple of BulgeRadius.
        [Ignore]
        public virtual float TiltPeakRadiusMultiplier { get; set; } = 0.22f;

        // Measured as a multiple of BulgeRadius.
        [Ignore]
        public virtual float TiltReturnRadiusMultiplier { get; set; } = 0.49f;

        [Ignore]
        public virtual float TiltRiseExponent { get; set; } = 1.81f;

        [Ignore]
        public virtual float TiltReturnExponent { get; set; } = 0.92f;

        [Ignore]
        public virtual float InnerCornerFan { get; set; }

        [Ignore]
        public virtual float FerrofluidSpikeSpringStrength { get; set; } = 250f;

        [Ignore]
        public virtual float FerrofluidSpikeSpringDamping { get; set; } = 12f;

        [Ignore]
        public virtual float FerrofluidPrimitiveSpikeSize { get; set; } = 0.35f;

        [Ignore]
        public virtual float FerrofluidPrimitiveTipRoundness { get; set; }

        [Ignore]
        public virtual float FerrofluidPrimitiveTipCapRounding { get; set; } = 0.81f;

        // Flattening radius on the side facing away from the magnet, as a fraction of BulgeRadius.
        [Ignore]
        public virtual float FerrofluidPrimitiveFlattenRadius { get; set; } = 1.45f;

        [Ignore]
        public virtual float FerrofluidPrimitiveFlattenExponent { get; set; } = 2.7f;

        [Ignore]
        public virtual float FerrofluidPrimitiveGlobalFlatten { get; set; } = 0.37f;

        [Ignore]
        public virtual float FerrofluidSphereBulgeScale { get; set; } = 0.6f;

        [Ignore]
        public virtual float FerrofluidCubeSpikeDensity { get; set; } = 1.2f;

        [Ignore]
        public virtual float FerrofluidSphereSpikeDensity { get; set; } = 0.95f;

        [Ignore]
        public virtual float FerrofluidCubeRoundness { get; set; } = 0.48f;

        [Ignore]
        public virtual bool FerrofluidFloorAssistanceEnabled { get; set; } = true;

        [Ignore]
        public virtual float FerrofluidCubeFloorFriction { get; set; } = FerrofluidGrabPhysics.DefaultCubeFloorFriction;

        [Ignore]
        public virtual float FerrofluidCubeCornerFriction { get; set; } = FerrofluidGrabPhysics.DefaultCubeCornerFriction;

        [Ignore]
        public virtual float FerrofluidCubeFaceLandingStrength { get; set; } = FerrofluidGrabPhysics.DefaultCubeFaceLandingStrength;

        [Ignore]
        public virtual float FerrofluidCubeFaceDampingStrength { get; set; } = FerrofluidGrabPhysics.DefaultCubeFaceDampingStrength;

        [Ignore]
        public virtual float FerrofluidCubeFaceDampingExponent { get; set; } = FerrofluidGrabPhysics.DefaultCubeFaceDampingExponent;

        [Ignore]
        public virtual float FerrofluidCubeFaceDampingStartAngle { get; set; } = FerrofluidGrabPhysics.DefaultCubeFaceDampingStartAngle;

        [Ignore]
        public virtual float FerrofluidSphereChartYaw { get; set; }

        [Ignore]
        public virtual float FerrofluidHomeElasticityGain { get; set; } = 0.1f;

        [Ignore]
        public virtual float FerrofluidHomeQuadraticPower { get; set; } = 8f;

        // Fraction of the travel bound where the quadratic ramp starts. 0.04 is 2% face width.
        [Ignore]
        public virtual float FerrofluidHomeSoftRange { get; set; } = 0.04f;

        [Ignore]
        public virtual float FerrofluidHomeTravelLimit { get; set; } = 0.5f;

        [Ignore]
        public virtual float FerrofluidHomeDampingGain { get; set; } = 1f;

        public virtual bool ParabolicFloorLightEnabled { get; set; } = true;

        public virtual float ParabolicFloorLightBrightness { get; set; } = ParabolicFloorLightSettings.FloorDefault.Brightness;

        public virtual float ParabolicFloorLightRed { get; set; } = ParabolicFloorLightSettings.FloorDefault.Red;

        public virtual float ParabolicFloorLightGreen { get; set; } = ParabolicFloorLightSettings.FloorDefault.Green;

        public virtual float ParabolicFloorLightBlue { get; set; } = ParabolicFloorLightSettings.FloorDefault.Blue;

        public virtual float ParabolicFloorLightWidthScale { get; set; } = 3.2f;

        public virtual float ParabolicFloorLightOffsetX { get; set; }

        public virtual float ParabolicFloorLightOffsetZ { get; set; }

        public virtual float ParabolicFloorLightRotation { get; set; }

        public virtual float ParabolicFloorLightLightWidthScale { get; set; } = ParabolicFloorLightSettings.FloorDefault.LightWidthScale;

        public virtual float ParabolicFloorLightBloomWidthScale { get; set; } = ParabolicFloorLightSettings.FloorDefault.BloomWidthScale;

        public virtual float ParabolicFloorLightBloomBrightness { get; set; } = ParabolicFloorLightSettings.FloorDefault.BloomBrightness;

        public virtual float ParabolicFloorLightHdrGlowThreshold { get; set; } = ParabolicFloorLightSettings.FloorDefault.HdrGlowThreshold;

        public virtual float ParabolicFloorLightHdrGlowWidthScale { get; set; } = ParabolicFloorLightSettings.FloorDefault.HdrGlowWidthScale;

        public virtual string MenuLightsJson { get; set; } = string.Empty;

        public virtual int MenuLightDefaultsVersion { get; set; }

        public virtual float ParabolicFloorLightHdrGlowBrightness { get; set; } = ParabolicFloorLightSettings.FloorDefault.HdrGlowBrightness;
    }
}
