using System.Runtime.CompilerServices;
using IPA.Config.Stores;
using IPA.Config.Stores.Attributes;
using JetBrains.Annotations;

[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]

namespace ChromaGLS.Settings
{
    // Only the enable toggles are saved. Tuning values marked [Ignore] reset when Beat Saber restarts.
    [UsedImplicitly(ImplicitUseTargetFlags.Members)]
    internal class Config
    {
        public virtual bool Enabled { get; set; } = true;

        public virtual bool FerrofluidBorderEnabled { get; set; } = true;

        public virtual bool FerrofluidCubeEnabled { get; set; } = false;

        public virtual bool FerrofluidSphereEnabled { get; set; } = false;

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
        public virtual float FerrofluidMatteSheen { get; set; } = 0.57f;

        // Degrees inward from the silhouette, with 90 covering the whole surface.
        [Ignore]
        public virtual float FerrofluidMatteAngle { get; set; } = 10f;

        // Reflection coverage uses the same angle convention as the matte sheen.
        [Ignore]
        public virtual float FerrofluidReflectionAngle { get; set; } = 15f;

        [Ignore]
        public virtual float FerrofluidMatteRotationX { get; set; } = 76f;

        [Ignore]
        public virtual float FerrofluidMatteRotationY { get; set; } = 295f;

        [Ignore]
        public virtual float FerrofluidMatteRotationZ { get; set; } = 174f;

        [Ignore]
        public virtual float FerrofluidReflectionRotationX { get; set; } = 244f;

        [Ignore]
        public virtual float FerrofluidReflectionRotationY { get; set; } = 210f;

        [Ignore]
        public virtual float FerrofluidReflectionRotationZ { get; set; } = 167f;

        [Ignore]
        public virtual float FerrofluidSpikeBaseSize { get; set; } = 3f;

        [Ignore]
        public virtual float FerrofluidSpikeHeightScale { get; set; } = 0.9f;

        [Ignore]
        public virtual float FerrofluidSpikeMoundCurvature { get; set; } = 2f;

        [Ignore]
        public virtual float FerrofluidSpikeDensity { get; set; } = 0.35f;

        // Cut away the thin membrane that bilinear sampling leaves between spike cells.
        [Ignore]
        public virtual float FerrofluidSpikeBaseCutoff { get; set; } = 0.1f;

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
        public virtual float BulgeHeight { get; set; } = 0.12f;

        [Ignore]
        public virtual float FerrofluidSpikeHeightRadius { get; set; } = 0.31f;

        [Ignore]
        public virtual float FerrofluidSpikeTiltRadius { get; set; } = 0.73f;

        [Ignore]
        public virtual float HeightFalloffExponent { get; set; } = 2.4f;

        [Ignore]
        public virtual float PeakWidth { get; set; } = 0.03f;

        [Ignore]
        public virtual float HeightGain { get; set; } = 0.87f;

        [Ignore]
        public virtual float BackHeightLoss { get; set; } = 1.19f;

        [Ignore]
        public virtual float BackHeightFalloffExponent { get; set; } = 2.65f;

        // Cap magnetic height without limiting spike tilt.
        [Ignore]
        public virtual float MagnetHeightStrengthCap { get; set; } = 0.74f;

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
        public virtual float FerrofluidPrimitiveSpikeSize { get; set; } = 0.7f;

        [Ignore]
        public virtual float FerrofluidPrimitiveTipRoundness { get; set; }

        [Ignore]
        public virtual float FerrofluidPrimitiveTipCapRounding { get; set; } = 0.81f;

        // Cube and sphere normals sample the environment map's bright ceiling and floor, so they need less ambient light.
        [Ignore]
        public virtual float FerrofluidPrimitiveAmbientScale { get; set; } = 0.15f;

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

        // Rotate the octahedral spike layout around each shape's up axis, in degrees.
        [Ignore]
        public virtual float FerrofluidCubeChartYaw { get; set; } = 45f;

        [Ignore]
        public virtual float FerrofluidSphereChartYaw { get; set; }
    }
}
