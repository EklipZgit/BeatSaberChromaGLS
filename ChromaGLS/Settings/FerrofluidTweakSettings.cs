namespace ChromaGLS.Settings
{
    // Shared explicit copy for every session-only [Ignore] tweak property. Save/cancel/reset
    // all route through this so the modal cannot drift from the shipped property set; the
    // persisted toggles (Enabled/Cube/Sphere/Border/floor light) are deliberately excluded.
    internal static class FerrofluidTweakSettings
    {
        internal static void Copy(Config source, Config destination)
        {
            destination.FerrofluidSpikesEnabled = source.FerrofluidSpikesEnabled;
            destination.FerrofluidBodyEnabled = source.FerrofluidBodyEnabled;
            destination.FerrofluidEmissionStrength = source.FerrofluidEmissionStrength;
            destination.FerrofluidExposure = source.FerrofluidExposure;
            destination.FerrofluidLutScale = source.FerrofluidLutScale;
            destination.FerrofluidLutOffset = source.FerrofluidLutOffset;
            destination.FerrofluidReflectivity = source.FerrofluidReflectivity;
            destination.FerrofluidBaseRed = source.FerrofluidBaseRed;
            destination.FerrofluidBaseGreen = source.FerrofluidBaseGreen;
            destination.FerrofluidBaseBlue = source.FerrofluidBaseBlue;
            destination.FerrofluidRoomReflectionStrength = source.FerrofluidRoomReflectionStrength;
            destination.FerrofluidFeetReflectionStrength = source.FerrofluidFeetReflectionStrength;
            destination.FerrofluidParabolaReflectionStrength = source.FerrofluidParabolaReflectionStrength;
            destination.FerrofluidParabolaReflectionSaturation = source.FerrofluidParabolaReflectionSaturation;
            destination.FerrofluidThinFilmStrength = source.FerrofluidThinFilmStrength;
            destination.FerrofluidThinFilmAngleRate = source.FerrofluidThinFilmAngleRate;
            destination.FerrofluidSpikeBaseSize = source.FerrofluidSpikeBaseSize;
            destination.FerrofluidSpikeHeightScale = source.FerrofluidSpikeHeightScale;
            destination.FerrofluidSpikeMoundCurvature = source.FerrofluidSpikeMoundCurvature;
            destination.FerrofluidSpikeDensity = source.FerrofluidSpikeDensity;
            destination.FerrofluidSpikeBaseCutoff = source.FerrofluidSpikeBaseCutoff;
            destination.ParticleMass = source.ParticleMass;
            destination.RestDensity = source.RestDensity;
            destination.GasConstant = source.GasConstant;
            destination.Viscosity = source.Viscosity;
            destination.SimulationSteps = source.SimulationSteps;
            destination.MagnetRadius = source.MagnetRadius;
            destination.MagnetMaximumSlide = source.MagnetMaximumSlide;
            destination.MagnetSlideFraction = source.MagnetSlideFraction;
            destination.MagnetSlideFalloffExponent = source.MagnetSlideFalloffExponent;
            destination.MagnetElasticity = source.MagnetElasticity;
            destination.MagnetDamping = source.MagnetDamping;
            destination.MagnetForwardDistance = source.MagnetForwardDistance;
            destination.BulgeRadius = source.BulgeRadius;
            destination.BulgeHeight = source.BulgeHeight;
            destination.FerrofluidSpikeHeightRadius = source.FerrofluidSpikeHeightRadius;
            destination.FerrofluidSpikeTiltRadius = source.FerrofluidSpikeTiltRadius;
            destination.HeightFalloffExponent = source.HeightFalloffExponent;
            destination.PeakWidth = source.PeakWidth;
            destination.HeightGain = source.HeightGain;
            destination.BackHeightLoss = source.BackHeightLoss;
            destination.BackHeightFalloffExponent = source.BackHeightFalloffExponent;
            destination.MagnetHeightStrengthCap = source.MagnetHeightStrengthCap;
            destination.MagnetHeightDeadZoneRadius = source.MagnetHeightDeadZoneRadius;
            destination.MaximumHeight = source.MaximumHeight;
            destination.MaximumTiltDegrees = source.MaximumTiltDegrees;
            destination.TiltPeakRadiusMultiplier = source.TiltPeakRadiusMultiplier;
            destination.TiltReturnRadiusMultiplier = source.TiltReturnRadiusMultiplier;
            destination.TiltRiseExponent = source.TiltRiseExponent;
            destination.TiltReturnExponent = source.TiltReturnExponent;
            destination.InnerCornerFan = source.InnerCornerFan;
            destination.FerrofluidSpikeSpringStrength = source.FerrofluidSpikeSpringStrength;
            destination.FerrofluidSpikeSpringDamping = source.FerrofluidSpikeSpringDamping;
            destination.FerrofluidPrimitiveSpikeSize = source.FerrofluidPrimitiveSpikeSize;
            destination.FerrofluidPrimitiveTipRoundness = source.FerrofluidPrimitiveTipRoundness;
            destination.FerrofluidPrimitiveTipCapRounding = source.FerrofluidPrimitiveTipCapRounding;
            destination.FerrofluidPrimitiveFlattenRadius = source.FerrofluidPrimitiveFlattenRadius;
            destination.FerrofluidPrimitiveFlattenExponent = source.FerrofluidPrimitiveFlattenExponent;
            destination.FerrofluidPrimitiveGlobalFlatten = source.FerrofluidPrimitiveGlobalFlatten;
            destination.FerrofluidSphereBulgeScale = source.FerrofluidSphereBulgeScale;
            destination.FerrofluidCubeSpikeDensity = source.FerrofluidCubeSpikeDensity;
            destination.FerrofluidSphereSpikeDensity = source.FerrofluidSphereSpikeDensity;
            destination.FerrofluidCubeRoundness = source.FerrofluidCubeRoundness;
            destination.FerrofluidFloorAssistanceEnabled = source.FerrofluidFloorAssistanceEnabled;
            destination.FerrofluidCubeFloorFriction = source.FerrofluidCubeFloorFriction;
            destination.FerrofluidCubeCornerFriction = source.FerrofluidCubeCornerFriction;
            destination.FerrofluidCubeFaceLandingStrength = source.FerrofluidCubeFaceLandingStrength;
            destination.FerrofluidCubeFaceDampingStrength = source.FerrofluidCubeFaceDampingStrength;
            destination.FerrofluidCubeFaceDampingExponent = source.FerrofluidCubeFaceDampingExponent;
            destination.FerrofluidCubeFaceDampingStartAngle = source.FerrofluidCubeFaceDampingStartAngle;
            destination.FerrofluidSphereChartYaw = source.FerrofluidSphereChartYaw;
            destination.FerrofluidHomeElasticityGain = source.FerrofluidHomeElasticityGain;
            destination.FerrofluidHomeQuadraticPower = source.FerrofluidHomeQuadraticPower;
            destination.FerrofluidHomeSoftRange = source.FerrofluidHomeSoftRange;
            destination.FerrofluidHomeTravelLimit = source.FerrofluidHomeTravelLimit;
            destination.FerrofluidHomeDampingGain = source.FerrofluidHomeDampingGain;
        }
    }
}
