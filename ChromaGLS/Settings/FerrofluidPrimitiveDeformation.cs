using UnityEngine;

namespace ChromaGLS.Settings
{
    // CPU copy of the cube and sphere deformation math. Keep it in sync with the surface shader.
    internal static class FerrofluidPrimitiveDeformation
    {
        internal static DeformationSettings EvaluateSettings(
            FerrofluidSurfaceShape shape,
            Config config,
            float sourceWorldScale)
        {
            Vector2 spikeShape = FerrofluidGpuSimulation.EvaluateSpikeShape(
                config.FerrofluidSpikeBaseSize,
                config.FerrofluidSpikeHeightScale);
            return new DeformationSettings
            {
                Roundness = FerrofluidEnvironmentObjects.EvaluateRoundness(shape, config),
                YawDegrees = FerrofluidEnvironmentObjects.EvaluateChartYawDegrees(shape, config),
                BulgeRadius = Mathf.Clamp(config.BulgeRadius, 0.1f, 3.2f) * sourceWorldScale,
                BulgeHeight = Mathf.Clamp(config.BulgeHeight, 0f, 0.2f) * sourceWorldScale,
                SpikeMoundCurvature = Mathf.Clamp(config.FerrofluidSpikeMoundCurvature, 1f, 4f),
                SpikeHeightRadius = Mathf.Clamp(config.FerrofluidSpikeHeightRadius, 0.1f, 3.2f) * sourceWorldScale,
                SpikeTiltRadius = Mathf.Clamp(config.FerrofluidSpikeTiltRadius, 0.1f, 3.2f) * sourceWorldScale,
                PeakWidth = Mathf.Clamp(config.PeakWidth, 0.03f, 1f) * sourceWorldScale,
                DeadZoneRadius = Mathf.Clamp(config.MagnetHeightDeadZoneRadius, 0f, 0.5f) * sourceWorldScale,
                FluidHeightScale = spikeShape.y * sourceWorldScale,
                SpikeFactor = spikeShape.x,
                HeightFalloffExponent = Mathf.Clamp(config.HeightFalloffExponent, 0.25f, 8f),
                HeightGain = Mathf.Clamp(config.HeightGain, 0f, 4f),
                BackHeightLoss = Mathf.Clamp(config.BackHeightLoss, 0f, 4f),
                BackHeightFalloffExponent = Mathf.Clamp(config.BackHeightFalloffExponent, 0.05f, 8f),
                HeightStrengthCap = Mathf.Clamp01(config.MagnetHeightStrengthCap),
                MaximumHeight = Mathf.Clamp(config.MaximumHeight, 0.2f, 3f),
                MaximumTiltRadians = Mathf.Clamp(config.MaximumTiltDegrees, 0f, 85f) * Mathf.Deg2Rad,
                TiltPeakRadiusMultiplier = Mathf.Clamp(config.TiltPeakRadiusMultiplier, 0.1f, 1.5f),
                TiltReturnRadiusMultiplier = Mathf.Clamp(config.TiltReturnRadiusMultiplier, 0.2f, 3f),
                TiltRiseExponent = Mathf.Clamp(config.TiltRiseExponent, 0.25f, 6f),
                TiltReturnExponent = Mathf.Clamp(config.TiltReturnExponent, 0.25f, 6f),
                FlattenRadius = Mathf.Clamp(config.FerrofluidPrimitiveFlattenRadius, 0.05f, 2f),
                FlattenExponent = Mathf.Clamp(config.FerrofluidPrimitiveFlattenExponent, 0.25f, 6f),
                GlobalSpikeFlatten = Mathf.Clamp01(config.FerrofluidPrimitiveGlobalFlatten),
                SphereBulgeScale = Mathf.Clamp(config.FerrofluidSphereBulgeScale, 0f, 1.5f),
                CellMetric = FerrofluidGpuSimulation.EvaluatePrimitiveCellMetric(config.FerrofluidPrimitiveSpikeSize),
                TipRoundness = Mathf.Clamp01(config.FerrofluidPrimitiveTipRoundness),
                SpikeBaseCutoff = FerrofluidGpuSimulation.EvaluateSpikeBaseCutoff(config.FerrofluidSpikeBaseCutoff),
                SpikesEnabled = config.FerrofluidSpikesEnabled ? 1f : 0f,
                SpringStrength = Mathf.Clamp(config.FerrofluidSpikeSpringStrength, 1f, 800f),
                SpringDamping = Mathf.Clamp(config.FerrofluidSpikeSpringDamping, 0f, 80f),
            };
        }

        // Match FerrofluidSiteFrame, FerrofluidMagneticHeightTarget, and SpikeDeformation for closed shapes.
        // Return spike direction and target height, with apex sharpening in the out parameter.
        internal static Vector4 EvaluateSiteDeformation(
            DeformationSettings settings,
            FerrofluidSurfaceShape shape,
            Vector4 site,
            Vector3 magnetPosition,
            float magneticFieldStrength,
            out float apexSharpening)
        {
            Vector3 sitePosition = new(site.x, site.y, site.z);
            Vector3 siteDirection = sitePosition.normalized;
            Vector3 surfaceNormal = shape == FerrofluidSurfaceShape.Sphere
                ? siteDirection
                : FerrofluidEnvironmentObjects.EvaluateSurfaceNormalFromDirection(shape, siteDirection, settings.Roundness);
            Vector3 fieldVector = magnetPosition - sitePosition;
            float fieldDistance = fieldVector.magnitude;
            Vector3 fieldDirection = fieldVector / Mathf.Max(fieldDistance, 0.000001f);
            float fieldFacing = Vector3.Dot(surfaceNormal, fieldDirection);
            Vector3 tangentField = fieldVector - (surfaceNormal * Vector3.Dot(fieldVector, surfaceNormal));
            float tangentFieldDistance = tangentField.magnitude;

            float heightFieldDistance = Mathf.Max(fieldDistance - settings.DeadZoneRadius, 0f);
            float normalizedHeightFieldDistance = heightFieldDistance / settings.SpikeHeightRadius;
            float heightFieldStrength = Mathf.Min(magneticFieldStrength, settings.HeightStrengthCap);
            float compactEnvelope = BulgeEnvelope(normalizedHeightFieldDistance, settings.HeightFalloffExponent);
            float exponentialPeak = Mathf.Exp(-Mathf.Pow(
                heightFieldDistance / settings.PeakWidth,
                settings.HeightFalloffExponent));
            float frontUnitResponse = Mathf.Max(fieldFacing, 0f) * compactEnvelope * exponentialPeak;
            float frontFieldResponse = heightFieldStrength * frontUnitResponse;
            float frontHeightGain = settings.HeightGain
                * heightFieldStrength
                * EvaluateSpikeMoundResponse(frontUnitResponse, settings.SpikeMoundCurvature);
            float primitiveBackHeightRadius = settings.SpikeHeightRadius * settings.FlattenRadius;
            float normalizedBackHeightDistance = heightFieldDistance / Mathf.Max(primitiveBackHeightRadius, 0.000001f);
            float backFieldResponse = heightFieldStrength
                * Mathf.Pow(
                    Mathf.Max(0f, 1f - normalizedBackHeightDistance),
                    settings.FlattenExponent * (settings.BackHeightFalloffExponent / 2.65f));
            float backHeightLoss = settings.BackHeightLoss * Mathf.Max(-fieldFacing, 0f) * backFieldResponse;
            float primitiveMaximumHeight = Mathf.Min(settings.MaximumHeight, 1.37f);
            float heightTarget = Mathf.Clamp(
                0.44f + frontHeightGain - backHeightLoss,
                0.03f,
                primitiveMaximumHeight);
            apexSharpening = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((frontFieldResponse - 0.25f) / 0.35f));

            float tangentLength = tangentField.magnitude;
            Vector3 tangentAwayFromField = tangentLength > 0.000001f
                ? -tangentField / tangentLength
                : Vector3.zero;
            float tiltPeakDistance = Mathf.Max(settings.SpikeTiltRadius * settings.TiltPeakRadiusMultiplier, 0.000001f);
            float tiltReturnDistance = Mathf.Max(
                settings.SpikeTiltRadius * settings.TiltReturnRadiusMultiplier,
                tiltPeakDistance + 0.000001f);
            float normalizedTiltRise = Mathf.Clamp01(tangentFieldDistance / tiltPeakDistance);
            float tiltAngle = settings.MaximumTiltRadians * Mathf.Pow(normalizedTiltRise, settings.TiltRiseExponent);
            Vector3 outwardSplayedDirection = (
                (surfaceNormal * Mathf.Cos(tiltAngle))
                + (tangentAwayFromField * Mathf.Sin(tiltAngle))).normalized;
            float tiltReturnBase = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(
                (tangentFieldDistance - tiltPeakDistance) / (tiltReturnDistance - tiltPeakDistance)));
            float tiltReturn = Mathf.Pow(tiltReturnBase, settings.TiltReturnExponent);
            float magnetEngagement = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(magneticFieldStrength / 0.02f));
            float fieldAxisWeight = tiltReturn
                * magnetEngagement
                * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((fieldFacing + 0.2f) / 0.65f));
            Vector3 spikeDirection = Vector3.Lerp(surfaceNormal, outwardSplayedDirection, fieldAxisWeight).normalized;
            return new Vector4(spikeDirection.x, spikeDirection.y, spikeDirection.z, heightTarget);
        }

        internal static float EvaluateSpikeMoundResponse(float response, float curvature)
        {
            return 1f - Mathf.Pow(1f - Mathf.Clamp01(response), curvature);
        }

        // Match the height map's .gb channels: choose the nearest site after normalizing distance by its spacing.
        internal static int EvaluateTexelSiteOwner(
            DeformationSettings settings,
            FerrofluidSurfaceShape shape,
            int texelX,
            int texelY,
            Vector4[] sites)
        {
            int mapSize = FerrofluidGpuSimulation.PrimitiveHeightMapSize;
            int clampedX = Mathf.Clamp(texelX, 0, mapSize - 1);
            int clampedY = Mathf.Clamp(texelY, 0, mapSize - 1);
            Vector2 texelUv = new((clampedX + 0.5f) / mapSize, (clampedY + 0.5f) / mapSize);
            Vector3 surfacePoint = FerrofluidEnvironmentObjects.EvaluateSurfacePosition(
                shape,
                texelUv,
                settings.Roundness,
                settings.YawDegrees);
            int owner = 0;
            float nearestDistance = float.MaxValue;
            for (int index = 0; index < sites.Length; index++)
            {
                Vector4 site = sites[index];
                float distance = (Vector3.Distance(surfacePoint, new Vector3(site.x, site.y, site.z)) / Mathf.Max(site.w, 0.0001f))
                    * settings.CellMetric
                    * FerrofluidGpuSimulation.HeightMapScaleFactor;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    owner = index;
                }
            }

            return owner;
        }

        internal static Vector2 StepSpring(
            Vector2 state,
            float target,
            float springStrength,
            float springDamping,
            float deltaTimeMilliseconds)
        {
            float deltaTime = deltaTimeMilliseconds * 0.001f;
            float velocity = state.y + ((((target - state.x) * springStrength) - (state.y * springDamping)) * deltaTime);
            return new Vector2(state.x + (velocity * deltaTime), velocity);
        }

        // Blend the four height-map texels' spike frames so adjacent cells remain continuous at different heights.
        internal static void EvaluateDeformationFrame(
            DeformationSettings settings,
            FerrofluidSurfaceShape shape,
            Vector2 heightUv,
            Vector4[] deformations,
            float[] apexSharpenings,
            float[] springScales,
            int[] texelOwners,
            out Vector3 spikeDirection,
            out float magneticHeightScale,
            out float apexSharpening)
        {
            EvaluateFrameTexels(
                heightUv,
                texelOwners,
                out int owner00,
                out int owner10,
                out int owner01,
                out int owner11,
                out float fracX,
                out float fracY);
            Vector3 direction00 = new(deformations[owner00].x, deformations[owner00].y, deformations[owner00].z);
            Vector3 direction10 = new(deformations[owner10].x, deformations[owner10].y, deformations[owner10].z);
            Vector3 direction01 = new(deformations[owner01].x, deformations[owner01].y, deformations[owner01].z);
            Vector3 direction11 = new(deformations[owner11].x, deformations[owner11].y, deformations[owner11].z);
            Vector3 blendedAxis = Vector3.Lerp(
                Vector3.Lerp(direction00, direction10, fracX),
                Vector3.Lerp(direction01, direction11, fracX),
                fracY);
            spikeDirection = blendedAxis / Mathf.Max(blendedAxis.magnitude, 0.0001f);
            apexSharpening = Mathf.Lerp(
                Mathf.Lerp(apexSharpenings[owner00], apexSharpenings[owner10], fracX),
                Mathf.Lerp(apexSharpenings[owner01], apexSharpenings[owner11], fracX),
                fracY);
            magneticHeightScale = Mathf.Lerp(
                Mathf.Lerp(springScales[owner00], springScales[owner10], fracX),
                Mathf.Lerp(springScales[owner01], springScales[owner11], fracX),
                fracY);
        }

        private static void EvaluateFrameTexels(
            Vector2 heightUv,
            int[] texelOwners,
            out int owner00,
            out int owner10,
            out int owner01,
            out int owner11,
            out float fracX,
            out float fracY)
        {
            int mapSize = FerrofluidGpuSimulation.PrimitiveHeightMapSize;
            Vector2 texelCoord = (heightUv * mapSize) - new Vector2(0.5f, 0.5f);
            int baseX = Mathf.FloorToInt(texelCoord.x);
            int baseY = Mathf.FloorToInt(texelCoord.y);
            fracX = Mathf.Clamp01(texelCoord.x - baseX);
            fracY = Mathf.Clamp01(texelCoord.y - baseY);
            owner00 = texelOwners[CornerIndex(baseX, baseY, mapSize)];
            owner10 = texelOwners[CornerIndex(baseX + 1, baseY, mapSize)];
            owner01 = texelOwners[CornerIndex(baseX, baseY + 1, mapSize)];
            owner11 = texelOwners[CornerIndex(baseX + 1, baseY + 1, mapSize)];
        }

        internal static Vector3 EvaluateSiteBulgeOffset(
            DeformationSettings settings,
            FerrofluidSurfaceShape shape,
            Vector4 site,
            Vector3 magnetPosition,
            float magneticFieldStrength)
        {
            Vector3 sitePosition = new(site.x, site.y, site.z);
            Vector3 siteDirection = sitePosition.normalized;
            Vector3 surfaceNormal = shape == FerrofluidSurfaceShape.Sphere
                ? siteDirection
                : FerrofluidEnvironmentObjects.EvaluateSurfaceNormalFromDirection(shape, siteDirection, settings.Roundness);
            Vector3 fieldVector = magnetPosition - sitePosition;
            float fieldDistance = fieldVector.magnitude;
            float fieldFacing = Vector3.Dot(surfaceNormal, fieldVector / Mathf.Max(fieldDistance, 0.000001f));
            float bulgeFieldDistance = Mathf.Max(fieldDistance - settings.DeadZoneRadius, 0f);
            float bulgeFieldStrength = Mathf.Min(magneticFieldStrength, settings.HeightStrengthCap);
            float bulgeFieldEnvelope = bulgeFieldStrength
                * BulgeEnvelope(bulgeFieldDistance / settings.BulgeRadius, settings.HeightFalloffExponent);
            float bulgeHeight = settings.BulgeHeight
                * Mathf.Max(fieldFacing, 0f)
                * bulgeFieldEnvelope
                * (shape == FerrofluidSurfaceShape.Sphere ? settings.SphereBulgeScale : 1f);
            return surfaceNormal * bulgeHeight;
        }

        internal static Vector3 EvaluatePrimitiveBulgeFrame(
            DeformationSettings settings,
            FerrofluidSurfaceShape shape,
            Vector2 heightUv,
            Vector4[] sites,
            int[] texelOwners,
            Vector3 magnetPosition,
            float magneticFieldStrength)
        {
            EvaluateFrameTexels(
                heightUv,
                texelOwners,
                out int owner00,
                out int owner10,
                out int owner01,
                out int owner11,
                out float fracX,
                out float fracY);
            Vector3 offset00 = EvaluateSiteBulgeOffset(settings, shape, sites[owner00], magnetPosition, magneticFieldStrength);
            Vector3 offset10 = EvaluateSiteBulgeOffset(settings, shape, sites[owner10], magnetPosition, magneticFieldStrength);
            Vector3 offset01 = EvaluateSiteBulgeOffset(settings, shape, sites[owner01], magnetPosition, magneticFieldStrength);
            Vector3 offset11 = EvaluateSiteBulgeOffset(settings, shape, sites[owner11], magnetPosition, magneticFieldStrength);
            return Vector3.Lerp(
                Vector3.Lerp(offset00, offset10, fracX),
                Vector3.Lerp(offset01, offset11, fracX),
                fracY);
        }

        internal static Vector3 EvaluateDisplacedPositionWithFrame(
            DeformationSettings settings,
            FerrofluidSurfaceShape shape,
            Vector2 chartUv,
            Vector4[] sites,
            Vector3 spikeDirection,
            float magneticHeightScale,
            float apexSharpening,
            Vector3 magnetPosition,
            float magneticFieldStrength,
            Config config,
            Vector3 primitiveBulgeOffset)
        {
            Vector2 heightUv = FoldChartUv(chartUv);
            Vector3 basePosition = FerrofluidEnvironmentObjects.EvaluateSurfacePosition(
                shape,
                chartUv,
                settings.Roundness,
                settings.YawDegrees);
            Vector3 heightSurfacePoint = FerrofluidEnvironmentObjects.EvaluateSurfacePosition(
                shape,
                heightUv,
                settings.Roundness,
                settings.YawDegrees);
            FerrofluidEnvironmentObjects.EvaluatePrimitiveConeProfiles(
                shape,
                heightSurfacePoint,
                sites,
                config,
                out float smoothHeight,
                out float sharpHeight);
            float selectedHeight = Mathf.Lerp(
                    smoothHeight * FerrofluidGpuSimulation.HeightFactorValue,
                    sharpHeight * FerrofluidGpuSimulation.HeightFactorValue,
                    apexSharpening)
                * settings.SpikesEnabled;

            float bulgeFieldStrength = Mathf.Min(magneticFieldStrength, settings.HeightStrengthCap);
            float globalSpikeFlatten = Mathf.Clamp01(settings.GlobalSpikeFlatten * bulgeFieldStrength);
            return basePosition
                + primitiveBulgeOffset
                + (spikeDirection * selectedHeight * settings.FluidHeightScale * magneticHeightScale * (1f - globalSpikeFlatten));
        }

        internal static Vector3 EvaluateDisplacedPosition(
            DeformationSettings settings,
            FerrofluidSurfaceShape shape,
            Vector2 chartUv,
            Vector4[] sites,
            Vector4[] deformations,
            float[] apexSharpenings,
            float[] springScales,
            int[] texelOwners,
            Vector3 magnetPosition,
            float magneticFieldStrength,
            Config config)
        {
            EvaluateDeformationFrame(
                settings,
                shape,
                FoldChartUv(chartUv),
                deformations,
                apexSharpenings,
                springScales,
                texelOwners,
                out Vector3 spikeDirection,
                out float magneticHeightScale,
                out float apexSharpening);
            Vector3 primitiveBulgeOffset = EvaluatePrimitiveBulgeFrame(
                settings,
                shape,
                FoldChartUv(chartUv),
                sites,
                texelOwners,
                magnetPosition,
                magneticFieldStrength);
            return EvaluateDisplacedPositionWithFrame(
                settings,
                shape,
                chartUv,
                sites,
                spikeDirection,
                magneticHeightScale,
                apexSharpening,
                magnetPosition,
                magneticFieldStrength,
                config,
                primitiveBulgeOffset);
        }

        // This gradient is unoriented. Multiply by chart winding to get the outward normal.
        internal static Vector3 EvaluateRawFiniteNormal(
            DeformationSettings settings,
            FerrofluidSurfaceShape shape,
            Vector2 chartUv,
            Vector4[] sites,
            Vector4[] deformations,
            float[] apexSharpenings,
            float[] springScales,
            int[] texelOwners,
            Vector3 magnetPosition,
            float magneticFieldStrength,
            Config config)
        {
            float epsilon = 2f / FerrofluidGpuSimulation.PrimitiveHeightMapSize;
            Vector3 previousPerimeter = EvaluateDisplacedPosition(
                settings, shape, chartUv - new Vector2(epsilon, 0f), sites, deformations, apexSharpenings, springScales, texelOwners, magnetPosition, magneticFieldStrength, config);
            Vector3 nextPerimeter = EvaluateDisplacedPosition(
                settings, shape, chartUv + new Vector2(epsilon, 0f), sites, deformations, apexSharpenings, springScales, texelOwners, magnetPosition, magneticFieldStrength, config);
            Vector3 previousCross = EvaluateDisplacedPosition(
                settings, shape, chartUv - new Vector2(0f, epsilon), sites, deformations, apexSharpenings, springScales, texelOwners, magnetPosition, magneticFieldStrength, config);
            Vector3 nextCross = EvaluateDisplacedPosition(
                settings, shape, chartUv + new Vector2(0f, epsilon), sites, deformations, apexSharpenings, springScales, texelOwners, magnetPosition, magneticFieldStrength, config);
            return Vector3.Cross(nextPerimeter - previousPerimeter, nextCross - previousCross);
        }

        // At chart poles, the base tangents collapse. Use the displaced normal's agreement with the analytic normal to choose winding.
        internal static float EvaluateRenderedWinding(
            DeformationSettings settings,
            FerrofluidSurfaceShape shape,
            Vector2 chartUv,
            Vector4[] sites,
            Vector4[] deformations,
            float[] apexSharpenings,
            float[] springScales,
            int[] texelOwners,
            Vector3 magnetPosition,
            float magneticFieldStrength,
            Config config)
        {
            float epsilon = 2f / FerrofluidGpuSimulation.PrimitiveHeightMapSize;
            Vector3 previousPerimeter = FerrofluidEnvironmentObjects.EvaluateSurfacePosition(
                shape, chartUv - new Vector2(epsilon, 0f), settings.Roundness, settings.YawDegrees);
            Vector3 nextPerimeter = FerrofluidEnvironmentObjects.EvaluateSurfacePosition(
                shape, chartUv + new Vector2(epsilon, 0f), settings.Roundness, settings.YawDegrees);
            Vector3 previousCross = FerrofluidEnvironmentObjects.EvaluateSurfacePosition(
                shape, chartUv - new Vector2(0f, epsilon), settings.Roundness, settings.YawDegrees);
            Vector3 nextCross = FerrofluidEnvironmentObjects.EvaluateSurfacePosition(
                shape, chartUv + new Vector2(0f, epsilon), settings.Roundness, settings.YawDegrees);
            Vector3 baseFinite = Vector3.Cross(nextPerimeter - previousPerimeter, nextCross - previousCross);
            Vector3 analyticNormal = FerrofluidEnvironmentObjects.EvaluateSurfaceNormal(
                shape,
                chartUv,
                settings.Roundness,
                settings.YawDegrees);
            if (baseFinite.sqrMagnitude > 0.000000001f)
            {
                return Vector3.Dot(baseFinite, analyticNormal) >= 0f ? 1f : -1f;
            }

            Vector3 displacedFinite = EvaluateRawFiniteNormal(
                settings, shape, chartUv, sites, deformations, apexSharpenings, springScales, texelOwners, magnetPosition, magneticFieldStrength, config);
            return Vector3.Dot(displacedFinite, analyticNormal) >= 0f ? 1f : -1f;
        }

        // Use the chart's fixed winding to orient normals. Comparing against the analytic normal would flip spike undersides.
        internal static Vector3 EvaluateRenderedNormal(
            DeformationSettings settings,
            FerrofluidSurfaceShape shape,
            Vector2 chartUv,
            Vector4[] sites,
            Vector4[] deformations,
            float[] apexSharpenings,
            float[] springScales,
            int[] texelOwners,
            Vector3 magnetPosition,
            float magneticFieldStrength,
            Config config)
        {
            Vector3 finiteNormal = EvaluateRawFiniteNormal(
                settings,
                shape,
                chartUv,
                sites,
                deformations,
                apexSharpenings,
                springScales,
                texelOwners,
                magnetPosition,
                magneticFieldStrength,
                config);
            Vector3 analyticNormal = FerrofluidEnvironmentObjects.EvaluateSurfaceNormal(
                shape,
                chartUv,
                settings.Roundness,
                settings.YawDegrees);
            float winding = EvaluateRenderedWinding(
                settings,
                shape,
                chartUv,
                sites,
                deformations,
                apexSharpenings,
                springScales,
                texelOwners,
                magnetPosition,
                magneticFieldStrength,
                config);
            return finiteNormal.sqrMagnitude > 0.000000001f
                ? finiteNormal.normalized * winding
                : analyticNormal;
        }

        // Match SurfaceHeightUv by folding samples across octahedral edges rather than clamping them.
        private static Vector2 FoldChartUv(Vector2 uv)
        {
            if (uv.x < 0f)
            {
                uv = new Vector2(-uv.x, 1f - uv.y);
            }
            else if (uv.x > 1f)
            {
                uv = new Vector2(2f - uv.x, 1f - uv.y);
            }

            if (uv.y < 0f)
            {
                uv = new Vector2(1f - uv.x, -uv.y);
            }
            else if (uv.y > 1f)
            {
                uv = new Vector2(1f - uv.x, 2f - uv.y);
            }

            return uv;
        }

        private static float BulgeEnvelope(float normalizedDistance, float exponent)
        {
            float distanceSquared = normalizedDistance * normalizedDistance;
            return Mathf.Pow(Mathf.Max(0f, 1f - (distanceSquared * distanceSquared)), exponent);
        }

        private static int CornerIndex(int texelX, int texelY, int mapSize)
        {
            int clampedX = Mathf.Clamp(texelX, 0, mapSize - 1);
            int clampedY = Mathf.Clamp(texelY, 0, mapSize - 1);
            return (clampedY * mapSize) + clampedX;
        }

        // Use the same clamps as ApplyConfiguration and ApplyWorldScaleParameters so the CPU copy matches the shader.
        internal struct DeformationSettings
        {
            internal float Roundness;
            internal float YawDegrees;
            internal float BulgeRadius;
            internal float BulgeHeight;
            internal float SpikeMoundCurvature;
            internal float SpikeHeightRadius;
            internal float SpikeTiltRadius;
            internal float PeakWidth;
            internal float DeadZoneRadius;
            internal float FluidHeightScale;
            internal float SpikeFactor;
            internal float HeightFalloffExponent;
            internal float HeightGain;
            internal float BackHeightLoss;
            internal float BackHeightFalloffExponent;
            internal float HeightStrengthCap;
            internal float MaximumHeight;
            internal float MaximumTiltRadians;
            internal float TiltPeakRadiusMultiplier;
            internal float TiltReturnRadiusMultiplier;
            internal float TiltRiseExponent;
            internal float TiltReturnExponent;
            internal float FlattenRadius;
            internal float FlattenExponent;
            internal float GlobalSpikeFlatten;
            internal float SphereBulgeScale;
            internal float CellMetric;
            internal float TipRoundness;
            internal float SpikeBaseCutoff;
            internal float SpikesEnabled;
            internal float SpringStrength;
            internal float SpringDamping;
        }
    }
}
