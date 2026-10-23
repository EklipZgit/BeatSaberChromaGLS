Shader "ChromaGLS/FerrofluidSurface"
{
    Properties
    {
        [HideInInspector] _HeightMapTex ("Height Map", 2D) = "black" {}
        [HideInInspector] _SpringStateTex ("Spike Spring State", 2D) = "black" {}
        [HideInInspector] _EnvironmentTex ("Environment", 2D) = "black" {}
        [HideInInspector] _ThinFilmTex ("Thin Film", 2D) = "black" {}
        [HideInInspector] _EmissionStrength ("Emission Strength", Float) = 0
        [HideInInspector] _Exposure ("Beat Saber Exposure", Float) = 1
        [HideInInspector] _FadeAlpha ("Fade Alpha", Range(0, 1)) = 1
        [HideInInspector] _ThinFilmScale ("Thin Film LUT Scale", Float) = 1
        [HideInInspector] _ThinFilmOffset ("Thin Film LUT Offset", Float) = 0
        [HideInInspector] _Reflectivity ("Surface Reflectivity", Range(0, 1)) = 0.75
        [HideInInspector] _MatteSheen ("Matte Sheen", Range(0, 1)) = 1
        [HideInInspector] _MatteAngle ("Matte Rim Angle", Float) = 2
        [HideInInspector] _ReflectionAngle ("Reflection Angle", Float) = 5
        [HideInInspector] _MatteLightRotation ("Matte Light Rotation", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ReflectionRotation ("Reflection Rotation", Vector) = (0, 0, 0, 0)
        [HideInInspector] _BackHeightFalloffExponent ("Back Height Falloff Exponent", Float) = 0.8
        [HideInInspector] _MagnetHeightStrengthCap ("Magnet Height Strength Cap", Float) = 0.8
        [HideInInspector] _MagnetHeightDeadZoneRadius ("Magnet Height Dead Zone Radius", Float) = 0.15
        [HideInInspector] _SpikesEnabled ("Spikes Enabled", Float) = 1
        [HideInInspector] _SpikeSpringStrength ("Spike Spring Strength", Float) = 120
        [HideInInspector] _SpikeSpringDamping ("Spike Spring Damping", Float) = 14
        [HideInInspector] _SurfaceShapeMode ("Surface Shape Mode", Float) = 0
        [HideInInspector] _SurfaceRoundness ("Surface Roundness", Float) = 0.15
        [HideInInspector] _SpikeMoundCurvature ("Spike Mound Curvature", Float) = 2
        [HideInInspector] _PrimitiveSpikeHeightRadius ("Primitive Spike Height Radius", Float) = 0.31
        [HideInInspector] _PrimitiveSpikeTiltRadius ("Primitive Spike Tilt Radius", Float) = 0.73
    }

    SubShader
    {
        Tags { "Queue"="Geometry+10" "RenderType"="Opaque" }

        CGINCLUDE
        #pragma target 5.0
        #include "UnityCG.cginc"
        #include "FerrofluidCommon.cginc"

        sampler2D _HeightMapTex;
        float4 _HeightMapTex_TexelSize;
        sampler2D _SpringStateTex;
        sampler2D _StateTex;
        sampler2D _SiteSurfaceTex;
        sampler2D _SiteDeformationTex;
        sampler2D _SiteBulgeTex;
        int _ParticleCount;
        sampler2D _EnvironmentTex;
        sampler2D _ThinFilmTex;
        float2 _RectCenter;
        float2 _RectSize;
        float _RectMinY;
        float _PivotY;
        float _Skew;
        float2 _HalfExtents;
        float _CornerRadius;
        float _RadialRadius;
        float _DepthRadius;
        float _ViewerLocalZSign;
        float _RadialCenter;
        float _SourceLocalScale;
        float4x4 _LocalToCurvedCanvas;
        float4x4 _CurvedCanvasToLocal;
        float _CurvedCanvasRadius;
        float _HasCurvedCanvas;
        float3 _MagneticFieldPosition;
        float _MagneticFieldStrength;
        float3 _SecondaryMagneticFieldPosition;
        float _SecondaryMagneticFieldStrength;
        float3 _MagnetOpeningPosition;
        float3 _MagnetOpeningNormal;
        float3 _MagnetOpeningDirection;
        float _MagnetOpeningStrength;
        float _BulgeRadius;
        float _BulgeHeight;
        float _SpikeMoundCurvature;
        float _HeightFalloffExponent;
        float _PeakWidth;
        float _HeightGain;
        float _BackHeightLoss;
        float _BackHeightFalloffExponent;
        float _MagnetHeightStrengthCap;
        float _MagnetHeightDeadZoneRadius;
        float _MaximumHeight;
        float _MaximumTiltRadians;
        float _TiltPeakRadiusMultiplier;
        float _TiltReturnRadiusMultiplier;
        float _TiltRiseExponent;
        float _TiltReturnExponent;
        float _InnerCornerFan;
        float _HeightFactor;
        float _FluidHeightScale;
        float _EmissionStrength;
        float _Exposure;
        float _FadeAlpha;

        float _ThinFilmScale;
        float _ThinFilmOffset;
        float _Reflectivity;
        float _MatteSheen;
        float _MatteAngle;
        float _ReflectionAngle;
        float3 _MatteLightRotation;
        float3 _ReflectionRotation;
        float _SpikesEnabled;
        float _SpikeSpringStrength;
        float _SpikeSpringDamping;
        float _DeltaTimeMilliseconds;
        // Graphics.Blit supplies a quad transform, so the spring pass needs the surface's world matrix explicitly.
        float4x4 _FerrofluidObjectToWorld;
        float _SurfaceShapeMode;
        float _SurfaceRoundness;
        float _PrimitiveAmbientScale;
        float _PrimitiveFlattenRadius;
        float _PrimitiveFlattenExponent;
        float _PrimitiveGlobalSpikeFlatten;
        float _SphereBulgeScale;
        float _PrimitiveSpikeHeightRadius;
        float _PrimitiveSpikeTiltRadius;
        // Rotate primitive UV folds away from the player-facing sides.
        float2 _PrimitiveYawCosSin;

        // Mesh positions are unused. The vertex shader builds the surface from UVs.
        struct FerrofluidSurfaceInput
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct FerrofluidSurfaceVaryings
        {
            float4 vertex : SV_POSITION;
            float3 worldPosition : TEXCOORD0;
            float3 worldNormal : TEXCOORD1;
            float spikeHeight : TEXCOORD2;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        struct FerrofluidSpringInput
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct FerrofluidSpringVaryings
        {
            float4 vertex : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        FerrofluidSpringVaryings FerrofluidSpringVertex(FerrofluidSpringInput input)
        {
            FerrofluidSpringVaryings output;
            output.vertex = UnityObjectToClipPos(input.vertex);
            output.uv = input.uv;
            return output;
        }

        // Every tube row must follow the same perimeter to keep the straight edges aligned.
        float2 RoundedRectangleCenterline(float phase)
        {
            float2 innerExtents = _HalfExtents - _CornerRadius;
            float horizontalLength = innerExtents.x * 2.0;
            float verticalLength = innerExtents.y * 2.0;
            float cornerLength = _CornerRadius * 1.57079632679;
            float distanceAlongPerimeter = frac(phase) * ((horizontalLength + verticalLength + (cornerLength * 2.0)) * 2.0);
            if (distanceAlongPerimeter < horizontalLength)
            {
                return _RectCenter + float2(-innerExtents.x + distanceAlongPerimeter, _HalfExtents.y);
            }
            distanceAlongPerimeter -= horizontalLength;

            if (distanceAlongPerimeter < cornerLength)
            {
                float angle = 1.57079632679 - (distanceAlongPerimeter / _CornerRadius);
                return _RectCenter + innerExtents + (float2(cos(angle), sin(angle)) * _CornerRadius);
            }
            distanceAlongPerimeter -= cornerLength;

            if (distanceAlongPerimeter < verticalLength)
            {
                return _RectCenter + float2(_HalfExtents.x, innerExtents.y - distanceAlongPerimeter);
            }
            distanceAlongPerimeter -= verticalLength;

            if (distanceAlongPerimeter < cornerLength)
            {
                float angle = -(distanceAlongPerimeter / _CornerRadius);
                return _RectCenter + float2(innerExtents.x, -innerExtents.y) + (float2(cos(angle), sin(angle)) * _CornerRadius);
            }
            distanceAlongPerimeter -= cornerLength;

            if (distanceAlongPerimeter < horizontalLength)
            {
                return _RectCenter + float2(innerExtents.x - distanceAlongPerimeter, -_HalfExtents.y);
            }
            distanceAlongPerimeter -= horizontalLength;

            if (distanceAlongPerimeter < cornerLength)
            {
                float angle = -1.57079632679 - (distanceAlongPerimeter / _CornerRadius);
                return _RectCenter + float2(-innerExtents.x, -innerExtents.y) + (float2(cos(angle), sin(angle)) * _CornerRadius);
            }
            distanceAlongPerimeter -= cornerLength;

            if (distanceAlongPerimeter < verticalLength)
            {
                return _RectCenter + float2(-_HalfExtents.x, -innerExtents.y + distanceAlongPerimeter);
            }
            distanceAlongPerimeter -= verticalLength;

            float finalAngle = 3.14159265359 - (distanceAlongPerimeter / _CornerRadius);
            return _RectCenter + float2(-innerExtents.x, innerExtents.y) + (float2(cos(finalAngle), sin(finalAngle)) * _CornerRadius);
        }

        // Match the C# ray mapping so controller hits and magnetic deformation line up.
        float2 RoundedRectangleOutward(float2 position)
        {
            float2 innerExtents = _HalfExtents - _CornerRadius;
            float2 relativePosition = position - _RectCenter;
            float2 cornerOffset = max(abs(relativePosition) - innerExtents, 0.0) * sign(relativePosition);
            return normalize(cornerOffset);
        }

        void TorusSurface(float2 uv, out float3 basePosition, out float3 ellipseNormal)
        {
            float2 wrappedUv = frac(uv);
            float crossSectionAngle = wrappedUv.y * 6.28318530718;
            float radialWeight = cos(crossSectionAngle);
            float depthWeight = sin(crossSectionAngle);
            float2 centerCurve = RoundedRectangleCenterline(wrappedUv.x);
            float2 outward = RoundedRectangleOutward(centerCurve);
            basePosition = float3(
                centerCurve + (outward * (_RadialCenter + (_RadialRadius * radialWeight))),
                _ViewerLocalZSign * _DepthRadius * depthWeight);
            ellipseNormal = normalize(float3(
                outward * (radialWeight / _RadialRadius),
                _ViewerLocalZSign * depthWeight / _DepthRadius));
        }

        float3 SphereDirection(float2 uv)
        {
            return FerrofluidPrimitiveSphereDirection(uv, _PrimitiveYawCosSin);
        }

        float RoundedCubeExponent(float roundness)
        {
            return lerp(8.0, 2.0, saturate(roundness));
        }

        float3 RoundedCubePosition(float2 uv, float roundness)
        {
            return FerrofluidPrimitiveBasePosition(uv, _SurfaceShapeMode, roundness, _PrimitiveYawCosSin);
        }

        // Analytic normals stay smooth across UV folds, where finite differences can sample the wrong side.
        void PrimitiveSurface(float2 uv, out float3 basePosition, out float3 surfaceNormal)
        {
            float3 direction = FerrofluidPrimitiveSphereDirection(uv, _PrimitiveYawCosSin);
            if (_SurfaceShapeMode > 1.5)
            {
                surfaceNormal = direction;
                basePosition = direction * 0.5;
                return;
            }

            float exponent = RoundedCubeExponent(_SurfaceRoundness);
            surfaceNormal = normalize(sign(direction) * pow(abs(direction), exponent - 1.0));
            basePosition = FerrofluidPrimitiveBasePosition(uv, _SurfaceShapeMode, _SurfaceRoundness, _PrimitiveYawCosSin);
        }

        void BaseSurface(float2 uv, out float3 basePosition, out float3 surfaceNormal)
        {
            if (_SurfaceShapeMode < 0.5)
            {
                TorusSurface(uv, basePosition, surfaceNormal);
            }
            else
            {
                PrimitiveSurface(uv, basePosition, surfaceNormal);
            }
        }

        // Primitive UV edges meet at mirrored coordinates, not opposite edges as on the torus.
        // Fold normal samples across them rather than clamping to the edge.
        float2 SurfaceHeightUv(float2 uv)
        {
            if (_SurfaceShapeMode > 0.5)
            {
                if (uv.x < 0.0)
                {
                    uv = float2(-uv.x, 1.0 - uv.y);
                }
                else if (uv.x > 1.0)
                {
                    uv = float2(2.0 - uv.x, 1.0 - uv.y);
                }

                if (uv.y < 0.0)
                {
                    uv = float2(1.0 - uv.x, -uv.y);
                }
                else if (uv.y > 1.0)
                {
                    uv = float2(1.0 - uv.x, 2.0 - uv.y);
                }

                return uv;
            }

            return frac(uv);
        }

        // This mesh bypasses HMUI rendering, so apply the canvas skew and curvature here.
        float3 ApplyRenderedPoint(float3 localPosition)
        {
            if (_SurfaceShapeMode > 0.5)
            {
                return localPosition;
            }

            float normalizedY = (localPosition.y - _RectMinY) / _RectSize.y;
            localPosition.x += _Skew * _RectSize.y * (normalizedY - _PivotY);
            if (_HasCurvedCanvas > 0.5)
            {
                float3 canvasPosition = mul(_LocalToCurvedCanvas, float4(localPosition, 1.0)).xyz;
                float angle = canvasPosition.x / _CurvedCanvasRadius;
                float depthRadius = _CurvedCanvasRadius + canvasPosition.z;
                canvasPosition = float3(
                    sin(angle) * depthRadius,
                    canvasPosition.y,
                    (cos(angle) * depthRadius) - _CurvedCanvasRadius);
                localPosition = mul(_CurvedCanvasToLocal, float4(canvasPosition, 1.0)).xyz;
            }

            return localPosition;
        }

        // Transform a short normal offset to account for canvas skew and curvature.
        float3 RenderedWorldDirection(float3 localPosition, float3 localDirection, float4x4 objectToWorld)
        {
            float derivativeStep = 0.01;
            float3 renderedOrigin = ApplyRenderedPoint(localPosition);
            float3 renderedOffset = ApplyRenderedPoint(localPosition + (localDirection * derivativeStep));
            return normalize(mul((float3x3)objectToWorld, renderedOffset - renderedOrigin));
        }

        float3 RenderedWorldPosition(float3 localPosition, float4x4 objectToWorld)
        {
            return mul(objectToWorld, float4(ApplyRenderedPoint(localPosition), 1.0)).xyz;
        }

        // The fourth-power falloff keeps the center flat and concentrates the drop near the rim.
        float FerrofluidBulgeEnvelope(float normalizedDistance, float exponent)
        {
            float distanceSquared = normalizedDistance * normalizedDistance;
            return pow(max(0.0, 1.0 - (distanceSquared * distanceSquared)), exponent);
        }

        struct FerrofluidSiteField
        {
            float3 basePosition;
            float3 surfaceNormal;
            float3 ringOutward;
            float3 fieldDirection;
            float fieldDistance;
            float tangentFieldDistance;
            float fieldFacing;
            float coherentFieldFalloff;
            float openingProximity;
            float radialWeight;
            float innerWeight;
            float strength;
            float openingStrength;
        };

        void FerrofluidApplyMagnet(inout FerrofluidSiteField field, float3 position, float strength, float openingStrength)
        {
            field.strength = strength;
            field.openingStrength = openingStrength;
            float3 siteFieldVector = position - field.basePosition;
            field.fieldDistance = length(siteFieldVector);
            field.fieldDirection = siteFieldVector / max(field.fieldDistance, 0.000001);
            float normalizedSiteFieldDistance = field.fieldDistance / (_SurfaceShapeMode > 0.5 ? _PrimitiveSpikeTiltRadius : _BulgeRadius);
            field.fieldFacing = dot(field.surfaceNormal, field.fieldDirection);
            float3 siteTangentField = siteFieldVector
                - (field.surfaceNormal * dot(siteFieldVector, field.surfaceNormal));
            field.tangentFieldDistance = length(siteTangentField);
            float compactFieldEnvelope = FerrofluidBulgeEnvelope(normalizedSiteFieldDistance, _HeightFalloffExponent);
            float exponentialPeak = exp(-pow(field.fieldDistance / _PeakWidth, _HeightFalloffExponent));
            field.coherentFieldFalloff = strength * compactFieldEnvelope * exponentialPeak;

            float3 openingPlanarDelta = _MagnetOpeningPosition - field.basePosition;
            openingPlanarDelta -= _MagnetOpeningNormal * dot(openingPlanarDelta, _MagnetOpeningNormal);
            float openingDistance = length(openingPlanarDelta);
            float openingDirectionLength = length(_MagnetOpeningDirection);
            float openingDirectionDot = dot(
                field.ringOutward,
                _MagnetOpeningDirection / max(openingDirectionLength, 0.000001));
            float openingDirectionWeight = lerp(
                1.0,
                lerp(0.15, 1.0, smoothstep(-0.6, 1.0, openingDirectionDot)),
                saturate(openingDirectionLength * 4.0));
            field.openingProximity = exp(-pow(
                openingDistance / max(_BulgeRadius * 0.75, 0.000001),
                _HeightFalloffExponent)) * openingDirectionWeight;
        }

        // Look up by particle UV so each spike keeps its own position and field response as it moves.
        void FerrofluidSiteFrame(float2 siteParticleUv, float4x4 objectToWorld, out FerrofluidSiteField field)
        {
            float3 siteBasePosition;
            float3 siteSurfaceNormal;
            float3 localRingDirection;

            if (_SurfaceShapeMode < 0.5)
            {
                float2 siteUv = (tex2Dlod(_StateTex, float4(siteParticleUv, 0.0, 0.0)).xy * 0.5) + 0.5;
                float3 siteEllipseNormal;
                TorusSurface(siteUv, siteBasePosition, siteEllipseNormal);
                field.radialWeight = cos(frac(siteUv.y) * 6.28318530718);
                field.innerWeight = smoothstep(0.15, 0.95, -field.radialWeight);
                float2 siteOutward = RoundedRectangleOutward(siteBasePosition.xy);
                float2 innerExtents = _HalfExtents - _CornerRadius;
                float2 cornerOffset = max(abs(siteBasePosition.xy - _RectCenter) - innerExtents, 0.0);
                float cornerWeight = smoothstep(0.002 * _SourceLocalScale, 0.012 * _SourceLocalScale, min(cornerOffset.x, cornerOffset.y));
                float horizontalDominance = smoothstep(-0.08, 0.08, abs(siteOutward.y) - abs(siteOutward.x));
                float2 verticalSideInward = float2(-sign(siteOutward.x), 0.0);
                float2 horizontalSideInward = float2(0.0, -sign(siteOutward.y));
                float2 separatedCornerAxis = normalize(lerp(verticalSideInward, horizontalSideInward, horizontalDominance));
                float cornerFanWeight = field.innerWeight * cornerWeight * _InnerCornerFan;
                float siteNormalXyLength = length(siteEllipseNormal.xy);
                siteSurfaceNormal = normalize(float3(
                    lerp(siteEllipseNormal.xy, separatedCornerAxis * siteNormalXyLength, cornerFanWeight),
                    siteEllipseNormal.z));
                localRingDirection = float3(siteOutward, 0.0);
            }
            else
            {
                // The primitive buffer stores surface positions, whose normalized direction also gives the sphere normal.
                siteBasePosition = tex2Dlod(_SiteSurfaceTex, float4(siteParticleUv, 0.0, 0.0)).xyz;
                float3 siteDirection = normalize(siteBasePosition);
                siteSurfaceNormal = _SurfaceShapeMode > 1.5
                    ? siteDirection
                    : normalize(sign(siteDirection) * pow(abs(siteDirection), RoundedCubeExponent(_SurfaceRoundness) - 1.0));
                field.radialWeight = 0.0;
                field.innerWeight = 0.0;
                localRingDirection = siteSurfaceNormal;
            }

            field.basePosition = RenderedWorldPosition(siteBasePosition, objectToWorld);
            field.surfaceNormal = RenderedWorldDirection(siteBasePosition, siteSurfaceNormal, objectToWorld);
            field.ringOutward = RenderedWorldDirection(siteBasePosition, localRingDirection, objectToWorld);
            FerrofluidApplyMagnet(field, _MagneticFieldPosition, _MagneticFieldStrength, _MagnetOpeningStrength);
        }

        float FerrofluidSingleMagneticHeightTarget(FerrofluidSiteField field, out float frontFieldResponse)
        {
            // Height uses the dead zone and strength cap, but tilt still needs the unmodified field.
            float heightFieldDistance = max(field.fieldDistance - _MagnetHeightDeadZoneRadius, 0.0);
            float heightRadius = _SurfaceShapeMode > 0.5 ? _PrimitiveSpikeHeightRadius : _BulgeRadius;
            float normalizedHeightFieldDistance = heightFieldDistance / heightRadius;
            float heightFieldStrength = min(field.strength, _MagnetHeightStrengthCap);
            float heightCompactEnvelope = FerrofluidBulgeEnvelope(normalizedHeightFieldDistance, _HeightFalloffExponent);
            float heightExponentialPeak = exp(-pow(heightFieldDistance / _PeakWidth, _HeightFalloffExponent));
            float frontUnitResponse = max(field.fieldFacing, 0.0) * heightCompactEnvelope * heightExponentialPeak;
            frontFieldResponse = heightFieldStrength * frontUnitResponse;
            float frontHeightGain = _HeightGain * (_SurfaceShapeMode > 0.5
                ? heightFieldStrength * (1.0 - pow(1.0 - saturate(frontUnitResponse), _SpikeMoundCurvature))
                : frontFieldResponse);

            float primitiveBaseHeightScale = _SurfaceShapeMode > 0.5 ? 0.44 : 0.18;
            float primitiveBackHeightRadius = heightRadius * (_SurfaceShapeMode > 0.5 ? _PrimitiveFlattenRadius : 1.0);
            float primitiveMaximumHeight = _SurfaceShapeMode > 0.5 ? min(_MaximumHeight, 1.37) : _MaximumHeight;
            float normalizedBackHeightDistance = heightFieldDistance / max(primitiveBackHeightRadius, 0.000001);
            // Normalize against the shared default so primitive tuning is unchanged at that value.
            float backFieldResponse = heightFieldStrength
                * pow(max(0.0, 1.0 - normalizedBackHeightDistance),
                    _SurfaceShapeMode > 0.5
                        ? _PrimitiveFlattenExponent * (_BackHeightFalloffExponent / 2.65)
                        : _BackHeightFalloffExponent);
            float backHeightLoss = _BackHeightLoss * max(-field.fieldFacing, 0.0) * backFieldResponse;
            float magneticHeightScale = clamp(
                primitiveBaseHeightScale + frontHeightGain - backHeightLoss,
                0.03,
                primitiveMaximumHeight);

            // A magnet in the opening raises the outer ring slightly and flattens the inner tube.
            float outerOpeningWeight = smoothstep(0.05, 0.85, field.radialWeight);
            float innerOpeningWeight = field.innerWeight;
            float openingHeightStrength = min(field.openingStrength, _MagnetHeightStrengthCap);
            float openingOuterResponse = openingHeightStrength
                * field.openingProximity
                * outerOpeningWeight
                * 0.25;
            float openingInnerResponse = openingHeightStrength * innerOpeningWeight;
            return clamp(
                magneticHeightScale
                + (_HeightGain * openingOuterResponse)
                - (_BackHeightLoss * openingInnerResponse),
                0.03,
                primitiveMaximumHeight);
        }

        // Sum each magnet's departure from the resting height, keeping both fields continuous when their influence overlaps.
        float FerrofluidMagneticHeightTarget(FerrofluidSiteField field, out float frontFieldResponse)
        {
            float height = FerrofluidSingleMagneticHeightTarget(field, frontFieldResponse);
            if (_SecondaryMagneticFieldStrength > 0.0)
            {
                FerrofluidSiteField second = field;
                FerrofluidApplyMagnet(second, _SecondaryMagneticFieldPosition, _SecondaryMagneticFieldStrength, 0.0);
                float secondFrontResponse;
                height += FerrofluidSingleMagneticHeightTarget(second, secondFrontResponse) - (_SurfaceShapeMode > 0.5 ? 0.44 : 0.18);
                frontFieldResponse += secondFrontResponse;
                height = clamp(height, 0.03, _SurfaceShapeMode > 0.5 ? min(_MaximumHeight, 1.37) : _MaximumHeight);
            }

            return height;
        }

        float3 FerrofluidSingleSpikeDirection(FerrofluidSiteField field)
        {
            float siteSplayFacing = dot(field.surfaceNormal, field.fieldDirection);
            float3 tangentTowardField = field.fieldDirection - (field.surfaceNormal * siteSplayFacing);
            float tangentLength = length(tangentTowardField);
            float3 tangentAwayFromField = tangentLength > 0.000001
                ? -tangentTowardField / tangentLength
                : 0.0;
            float tiltRadius = _SurfaceShapeMode > 0.5 ? _PrimitiveSpikeTiltRadius : _BulgeRadius;
            float tiltPeakDistance = max(tiltRadius * _TiltPeakRadiusMultiplier, 0.000001);
            float tiltReturnDistance = max(tiltRadius * _TiltReturnRadiusMultiplier, tiltPeakDistance + 0.000001);
            float normalizedTiltRise = saturate(field.tangentFieldDistance / tiltPeakDistance);
            float tiltAngle = _MaximumTiltRadians * pow(normalizedTiltRise, _TiltRiseExponent);
            float3 outwardSplayedDirection = normalize(
                (field.surfaceNormal * cos(tiltAngle))
                + (tangentAwayFromField * sin(tiltAngle)));
            float tiltReturnBase = 1.0 - smoothstep(tiltPeakDistance, tiltReturnDistance, field.tangentFieldDistance);
            float tiltReturn = pow(tiltReturnBase, _TiltReturnExponent);
            float magnetEngagement = smoothstep(0.0, 0.02, field.strength);
            float fieldAxisWeight = tiltReturn
                * magnetEngagement
                * smoothstep(-0.2, 0.45, siteSplayFacing);
            float3 spikeDirection = normalize(lerp(field.surfaceNormal, outwardSplayedDirection, fieldAxisWeight));

            float openingAxisWeight = saturate(
                field.openingStrength * field.openingProximity * smoothstep(0.05, 0.85, field.radialWeight));
            spikeDirection = normalize(lerp(spikeDirection, field.ringOutward, openingAxisWeight));
            return spikeDirection;
        }

        float FerrofluidMagnetBulge(float3 worldBasePosition, float3 worldSurfaceNormal, float3 position, float strength)
        {
            float3 delta = position - worldBasePosition;
            float distance = length(delta);
            float facing = dot(worldSurfaceNormal, delta / max(distance, 0.000001));
            float heightDistance = max(distance - _MagnetHeightDeadZoneRadius, 0.0);
            float envelope = min(strength, _MagnetHeightStrengthCap)
                * FerrofluidBulgeEnvelope(heightDistance / _BulgeRadius, _HeightFalloffExponent);
            return _BulgeHeight * max(facing, 0.0) * envelope
                * (_SurfaceShapeMode > 1.5 ? _SphereBulgeScale : 1.0);
        }

        void CalculateSpikeDeformation(float2 siteParticleUv, float4x4 objectToWorld, out float3 spikeDirection, out float magneticHeightScale, out float apexSharpening, out float3 primitiveBulgeOffset)
        {
            FerrofluidSiteField field;
            FerrofluidSiteFrame(siteParticleUv, objectToWorld, field);
            float frontFieldResponse;
            magneticHeightScale = FerrofluidMagneticHeightTarget(field, frontFieldResponse);
            apexSharpening = smoothstep(0.25, 0.6, frontFieldResponse);
            spikeDirection = FerrofluidSingleSpikeDirection(field);
            primitiveBulgeOffset = float3(0.0, 0.0, 0.0);
            if (_SecondaryMagneticFieldStrength > 0.0)
            {
                FerrofluidSiteField second = field;
                FerrofluidApplyMagnet(second, _SecondaryMagneticFieldPosition, _SecondaryMagneticFieldStrength, 0.0);
                float3 secondDirection = FerrofluidSingleSpikeDirection(second);
                float firstWeight = field.coherentFieldFalloff;
                float secondWeight = second.coherentFieldFalloff;
                float totalWeight = firstWeight + secondWeight;
                spikeDirection = normalize(lerp(spikeDirection, secondDirection, secondWeight / max(totalWeight, 0.000001)));
            }

            if (_SurfaceShapeMode > 0.5)
            {
                float primitiveSupportHeight = FerrofluidMagnetBulge(field.basePosition, field.surfaceNormal, _MagneticFieldPosition, _MagneticFieldStrength);
                if (_SecondaryMagneticFieldStrength > 0.0)
                {
                    primitiveSupportHeight += FerrofluidMagnetBulge(field.basePosition, field.surfaceNormal, _SecondaryMagneticFieldPosition, _SecondaryMagneticFieldStrength);
                }

                primitiveBulgeOffset = field.surfaceNormal * primitiveSupportHeight;
            }
        }

        void SpikeDeformation(float2 siteParticleUv, out float3 spikeDirection, out float magneticHeightScale, out float apexSharpening, out float3 primitiveBulgeOffset)
        {
        #if defined(FERROFLUID_CACHED_DEFORMATION)
            float4 deformation = tex2Dlod(_SiteDeformationTex, float4(siteParticleUv, 0.0, 0.0));
            spikeDirection = deformation.xyz;
            apexSharpening = deformation.w;
            primitiveBulgeOffset = tex2Dlod(_SiteBulgeTex, float4(siteParticleUv, 0.0, 0.0)).xyz;
            // Vertex height comes from the spring texture, so the instantaneous target is not cached.
            magneticHeightScale = 0.0;
        #else
            CalculateSpikeDeformation(siteParticleUv, unity_ObjectToWorld, spikeDirection, magneticHeightScale, apexSharpening, primitiveBulgeOffset);
        #endif
        }

        // Each site has one magnet response shared by all vertices and normal samples that refer to it.
        float4 FerrofluidSiteDeformationFragment(FerrofluidSpringVaryings input) : SV_Target
        {
            float3 direction;
            float height;
            float sharpening;
            float3 bulge;
            CalculateSpikeDeformation(input.uv, _FerrofluidObjectToWorld, direction, height, sharpening, bulge);
            return float4(direction, sharpening);
        }

        float4 FerrofluidSiteBulgeFragment(FerrofluidSpringVaryings input) : SV_Target
        {
            float3 direction;
            float height;
            float sharpening;
            float3 bulge;
            CalculateSpikeDeformation(input.uv, _FerrofluidObjectToWorld, direction, height, sharpening, bulge);
            return float4(bulge, 0.0);
        }

        // Keep spring state per particle so moving spikes cannot inherit a neighbor's height and velocity.
        float4 FerrofluidHeightSpringFragment(FerrofluidSpringVaryings input) : SV_Target
        {
            int particleId = FerrofluidParticleId(input.uv);
            if (particleId >= _ParticleCount)
            {
                return float4(0.0, 0.0, 0.0, 0.0);
            }

            FerrofluidSiteField field;
            FerrofluidSiteFrame(input.uv, _FerrofluidObjectToWorld, field);
            float frontFieldResponse;
            float target = FerrofluidMagneticHeightTarget(field, frontFieldResponse);
            float2 state = tex2Dlod(_SpringStateTex, float4(input.uv, 0.0, 0.0)).xy;
            float deltaTime = _DeltaTimeMilliseconds * 0.001;
            float velocity = state.y + ((((target - state.x) * _SpikeSpringStrength) - (state.y * _SpikeSpringDamping)) * deltaTime);
            float height = state.x + (velocity * deltaTime);
            return float4(height, velocity, 0.0, 0.0);
        }

        // Blend each particle's deformation, not its UVs, to keep the surface continuous between spikes.
        // Blending UVs would look up unrelated particles between the four samples.
        void FerrofluidVertexFrame(
            float2 heightUv,
            out float3 spikeDirection,
            out float magneticHeightScale,
            out float apexSharpening,
            out float3 primitiveBulgeOffset)
        {
            float2 texelCoord = (heightUv * _HeightMapTex_TexelSize.zw) - 0.5;
            float2 texelBase = floor(texelCoord);
            float2 texelFrac = saturate(texelCoord - texelBase);
            float2 texelUv00 = (texelBase + 0.5) * _HeightMapTex_TexelSize.xy;
            float2 texelUv10 = texelUv00 + float2(_HeightMapTex_TexelSize.x, 0.0);
            float2 texelUv01 = texelUv00 + float2(0.0, _HeightMapTex_TexelSize.y);
            float2 texelUv11 = texelUv00 + _HeightMapTex_TexelSize.xy;
            float2 particleUv00 = tex2Dlod(_HeightMapTex, float4(texelUv00, 0.0, 0.0)).gb;
            float2 particleUv10 = tex2Dlod(_HeightMapTex, float4(texelUv10, 0.0, 0.0)).gb;
            float2 particleUv01 = tex2Dlod(_HeightMapTex, float4(texelUv01, 0.0, 0.0)).gb;
            float2 particleUv11 = tex2Dlod(_HeightMapTex, float4(texelUv11, 0.0, 0.0)).gb;
            float3 direction00;
            float3 direction10;
            float3 direction01;
            float3 direction11;
            float sharp00;
            float sharp10;
            float sharp01;
            float sharp11;
            float3 bulgeOffset00;
            float3 bulgeOffset10;
            float3 bulgeOffset01;
            float3 bulgeOffset11;
            float instantHeightScale;
            SpikeDeformation(particleUv00, direction00, instantHeightScale, sharp00, bulgeOffset00);
            SpikeDeformation(particleUv10, direction10, instantHeightScale, sharp10, bulgeOffset10);
            SpikeDeformation(particleUv01, direction01, instantHeightScale, sharp01, bulgeOffset01);
            SpikeDeformation(particleUv11, direction11, instantHeightScale, sharp11, bulgeOffset11);
            float3 blendedAxis = lerp(
                lerp(direction00, direction10, texelFrac.x),
                lerp(direction01, direction11, texelFrac.x),
                texelFrac.y);
            spikeDirection = blendedAxis / max(length(blendedAxis), 0.0001);
            apexSharpening = lerp(
                lerp(sharp00, sharp10, texelFrac.x),
                lerp(sharp01, sharp11, texelFrac.x),
                texelFrac.y);

            float spring00 = tex2Dlod(_SpringStateTex, float4(particleUv00, 0.0, 0.0)).x;
            float spring10 = tex2Dlod(_SpringStateTex, float4(particleUv10, 0.0, 0.0)).x;
            float spring01 = tex2Dlod(_SpringStateTex, float4(particleUv01, 0.0, 0.0)).x;
            float spring11 = tex2Dlod(_SpringStateTex, float4(particleUv11, 0.0, 0.0)).x;
            magneticHeightScale = lerp(
                lerp(spring00, spring10, texelFrac.x),
                lerp(spring01, spring11, texelFrac.x),
                texelFrac.y);
            primitiveBulgeOffset = lerp(
                lerp(bulgeOffset00, bulgeOffset10, texelFrac.x),
                lerp(bulgeOffset01, bulgeOffset11, texelFrac.x),
                texelFrac.y);
        }

        float3 DistortSurface(
            float2 uv,
            float4 heightSample,
            float3 spikeDirection,
            float magneticHeightScale,
            float spikeSharpness,
            float3 primitiveBulgeOffset)
        {
            float3 basePosition;
            float3 ellipseNormal;
            BaseSurface(uv, basePosition, ellipseNormal);
            float selectedHeight = lerp(heightSample.r, heightSample.a, spikeSharpness) * _SpikesEnabled;
            float3 worldBasePosition = RenderedWorldPosition(basePosition, unity_ObjectToWorld);
            float bulgeFieldStrength = min(_MagneticFieldStrength, _MagnetHeightStrengthCap);
            float globalSpikeFlatten = _SurfaceShapeMode > 0.5
                ? saturate(_PrimitiveGlobalSpikeFlatten * bulgeFieldStrength)
                : 0.0;
            float3 supportOffset = primitiveBulgeOffset;
            if (_SurfaceShapeMode < 0.5)
            {
                float3 worldSurfaceNormal = RenderedWorldDirection(basePosition, ellipseNormal, unity_ObjectToWorld);
                float bulgeHeight = FerrofluidMagnetBulge(worldBasePosition, worldSurfaceNormal, _MagneticFieldPosition, _MagneticFieldStrength);
                if (_SecondaryMagneticFieldStrength > 0.0)
                {
                    bulgeHeight += FerrofluidMagnetBulge(worldBasePosition, worldSurfaceNormal, _SecondaryMagneticFieldPosition, _SecondaryMagneticFieldStrength);
                }

                supportOffset = worldSurfaceNormal * bulgeHeight;
            }
            float3 worldPosition = worldBasePosition
                + supportOffset
                + (spikeDirection * selectedHeight * _FluidHeightScale * magneticHeightScale * (1.0 - globalSpikeFlatten));
            return mul(unity_WorldToObject, float4(worldPosition, 1.0)).xyz;
        }

        FerrofluidSurfaceVaryings FerrofluidSurfaceVertex(FerrofluidSurfaceInput input)
        {
            UNITY_SETUP_INSTANCE_ID(input);
            FerrofluidSurfaceVaryings output;
            UNITY_INITIALIZE_OUTPUT(FerrofluidSurfaceVaryings, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            float2 uv = input.uv;
            float4 heightSample = tex2Dlod(_HeightMapTex, float4(SurfaceHeightUv(uv), 0.0, 0.0));

            float3 spikeDirection;
            float magneticHeightScale;
            float apexSharpening;
            float3 primitiveBulgeOffset;
            FerrofluidVertexFrame(SurfaceHeightUv(uv), spikeDirection, magneticHeightScale, apexSharpening, primitiveBulgeOffset);

            float selectedProfileHeight = lerp(heightSample.r, heightSample.a, apexSharpening) * _SpikesEnabled;
            // Apply the same flattening as the geometry so thin-film colors follow the visible spike height.
            float vertexGlobalSpikeFlatten = _SurfaceShapeMode > 0.5
                ? saturate(_PrimitiveGlobalSpikeFlatten * min(_MagneticFieldStrength, _MagnetHeightStrengthCap))
                : 0.0;
            // Offset the primitive rest height from 0.44 to the torus's 0.18 so both use the same resting film colors.
            float lutHeightScale = _SurfaceShapeMode > 0.5 ? max(magneticHeightScale - 0.26, 0.0) : magneticHeightScale;
            output.spikeHeight = saturate(
                (selectedProfileHeight * lutHeightScale * (1.0 - vertexGlobalSpikeFlatten))
                / max(_HeightFactor * _MaximumHeight, 0.000001));

            // Recompute deformation at each normal sample. Reusing the center's values gives the wrong slope between spikes.
            float2 epsilon = 2.0 * _HeightMapTex_TexelSize.xy;
            float2 previousPerimeterUv = uv - float2(epsilon.x, 0.0);
            float2 nextPerimeterUv = uv + float2(epsilon.x, 0.0);
            float2 previousCrossSectionUv = uv - float2(0.0, epsilon.y);
            float2 nextCrossSectionUv = uv + float2(0.0, epsilon.y);
            float3 tapDirection;
            float tapScale;
            float tapSharpening;
            float3 tapBulgeOffset;
            FerrofluidVertexFrame(SurfaceHeightUv(previousPerimeterUv), tapDirection, tapScale, tapSharpening, tapBulgeOffset);
            float3 previousPerimeter = DistortSurface(
                previousPerimeterUv,
                tex2Dlod(_HeightMapTex, float4(SurfaceHeightUv(previousPerimeterUv), 0.0, 0.0)),
                tapDirection,
                tapScale,
                tapSharpening,
                tapBulgeOffset);
            FerrofluidVertexFrame(SurfaceHeightUv(nextPerimeterUv), tapDirection, tapScale, tapSharpening, tapBulgeOffset);
            float3 nextPerimeter = DistortSurface(
                nextPerimeterUv,
                tex2Dlod(_HeightMapTex, float4(SurfaceHeightUv(nextPerimeterUv), 0.0, 0.0)),
                tapDirection,
                tapScale,
                tapSharpening,
                tapBulgeOffset);
            FerrofluidVertexFrame(SurfaceHeightUv(previousCrossSectionUv), tapDirection, tapScale, tapSharpening, tapBulgeOffset);
            float3 previousCrossSection = DistortSurface(
                previousCrossSectionUv,
                tex2Dlod(_HeightMapTex, float4(SurfaceHeightUv(previousCrossSectionUv), 0.0, 0.0)),
                tapDirection,
                tapScale,
                tapSharpening,
                tapBulgeOffset);
            FerrofluidVertexFrame(SurfaceHeightUv(nextCrossSectionUv), tapDirection, tapScale, tapSharpening, tapBulgeOffset);
            float3 nextCrossSection = DistortSurface(
                nextCrossSectionUv,
                tex2Dlod(_HeightMapTex, float4(SurfaceHeightUv(nextCrossSectionUv), 0.0, 0.0)),
                tapDirection,
                tapScale,
                tapSharpening,
                tapBulgeOffset);
            float3 localPosition = DistortSurface(
                uv,
                heightSample,
                spikeDirection,
                magneticHeightScale,
                apexSharpening,
                primitiveBulgeOffset);
            float3 worldPosition = mul(unity_ObjectToWorld, float4(localPosition, 1.0)).xyz;
            float3 finiteNormal = cross(
                nextPerimeter - previousPerimeter,
                nextCrossSection - previousCrossSection);
            float3 analyticBasePosition;
            float3 analyticNormal;
            BaseSurface(uv, analyticBasePosition, analyticNormal);
            float3 worldAnalyticNormal = RenderedWorldDirection(analyticBasePosition, analyticNormal, unity_ObjectToWorld);

            // Use the undeformed surface's winding to orient normals.
            // Comparing against the base normal would incorrectly flip the underside of an overhanging spike.
            float3 basePreviousPerimeter;
            float3 baseNextPerimeter;
            float3 basePreviousCross;
            float3 baseNextCross;
            float3 unusedNormal;
            BaseSurface(previousPerimeterUv, basePreviousPerimeter, unusedNormal);
            BaseSurface(nextPerimeterUv, baseNextPerimeter, unusedNormal);
            BaseSurface(previousCrossSectionUv, basePreviousCross, unusedNormal);
            BaseSurface(nextCrossSectionUv, baseNextCross, unusedNormal);
            float3 baseFiniteNormal = cross(
                baseNextPerimeter - basePreviousPerimeter,
                baseNextCross - basePreviousCross);
            float chartWinding = dot(baseFiniteNormal, baseFiniteNormal) > 0.000000001
                ? (dot(baseFiniteNormal, analyticNormal) >= 0.0 ? 1.0 : -1.0)
                : (dot(finiteNormal, analyticNormal) >= 0.0 ? 1.0 : -1.0);

            // Use the analytic normal where the UV corners meet at the bottom pole and derivatives collapse.
            float3 worldNormal = dot(finiteNormal, finiteNormal) > 0.000000001
                ? normalize(mul((float3x3)unity_ObjectToWorld, finiteNormal)) * chartWinding
                : worldAnalyticNormal;

            output.worldPosition = worldPosition;
            output.worldNormal = worldNormal;
            output.vertex = UnityWorldToClipPos(worldPosition);
            return output;
        }

        float3 FerrofluidRotateEuler(float3 dir, float3 eulerDegrees)
        {
            float3 r = radians(eulerDegrees);
            float sx = sin(r.x);
            float cx = cos(r.x);
            float sy = sin(r.y);
            float cy = cos(r.y);
            float sz = sin(r.z);
            float cz = cos(r.z);
            dir = float3(dir.x, (dir.y * cx) - (dir.z * sx), (dir.y * sx) + (dir.z * cx));
            dir = float3((dir.x * cy) + (dir.z * sy), dir.y, (dir.z * cy) - (dir.x * sy));
            return float3((dir.x * cz) - (dir.y * sz), (dir.x * sz) + (dir.y * cz), dir.z);
        }

        // This rational approximation gives a narrow highlight without a general pow.
        float FerrofluidPowFast(float value, float exponent)
        {
            return value / (((1.0 - exponent) * value) + exponent);
        }

        float4 FerrofluidSurfaceFragment(FerrofluidSurfaceVaryings input, fixed facing : VFACE) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            // Dither the fade because framebuffer alpha must stay zero to avoid bloom.
            [branch]
            if (_FadeAlpha < 0.999)
            {
                float fadeDither = frac(52.9829189 * frac(input.vertex.x * 0.06711056 + input.vertex.y * 0.00583715));
                clip(_FadeAlpha - fadeDither);
            }

            [branch]
            if (_Exposure <= 0.000001)
            {
                return float4(_EmissionStrength.xxx, 0.0);
            }

            // Each eye needs its own view direction for reflections and thin-film highlights.
#if defined(USING_STEREO_MATRICES)
            float3 cameraPosition = unity_StereoWorldSpaceCameraPos[unity_StereoEyeIndex];
#else
            float3 cameraPosition = _WorldSpaceCameraPos.xyz;
#endif
            float3 viewDirection = normalize(cameraPosition - input.worldPosition);
            float3 surfaceDirection = normalize(input.worldNormal);
            float3 lightingNormal = facing < 0.0 ? -surfaceDirection : surfaceDirection;
            float3 lightDirection = normalize(FerrofluidRotateEuler(float3(2.0, 1.0, 1.0), _MatteLightRotation));
            float3 reflectionDirection = reflect(lightDirection, lightingNormal);
            float specularValue = FerrofluidPowFast(max(0.0, dot(reflectionDirection, -viewDirection)), lerp(50.0, 160.0, _Reflectivity));
            float phi = atan2(reflectionDirection.z, reflectionDirection.x);
            float theta = acos(clamp(reflectionDirection.y, -1.0, 1.0));
            float2 environmentUv = float2(frac((-phi / 6.28318530718) + 1.0), theta / 3.14159265359);
            // Darken resting primitive surfaces, whose normals sample brighter parts of the environment.
            // Raised spikes regain the full rim highlight.
            float primitiveAmbientScale = _SurfaceShapeMode > 0.5 ? _PrimitiveAmbientScale : 1.0;
            float primitiveLift = _SurfaceShapeMode > 0.5
                ? lerp(_PrimitiveAmbientScale, 1.0, smoothstep(0.2, 0.6, input.spikeHeight))
                : 1.0;
            float3 ambient = tex2D(_EnvironmentTex, environmentUv).rgb * primitiveAmbientScale;
            float facingView = max(0.0, dot(lightingNormal, viewDirection));
            // Angles measure coverage inward from the silhouette: 0 is off, 90 covers the whole surface.
            float rimEdge = cos(radians(_MatteAngle));
            float fresnelValue = smoothstep(rimEdge, 1.0, 1.0 - facingView);
            fresnelValue *= (smoothstep(0.6, 2.0, 1.0 - input.worldPosition.z) * 0.7) + 0.3;
            float3 viewReflection = FerrofluidRotateEuler(reflect(-viewDirection, lightingNormal), _ReflectionRotation);
            float reflectionPhi = atan2(viewReflection.z, viewReflection.x);
            float reflectionTheta = acos(clamp(viewReflection.y, -1.0, 1.0));
            float2 reflectionUv = float2(frac((-reflectionPhi / 6.28318530718) + 1.0), reflectionTheta / 3.14159265359);
            float3 reflectedEnvironment = tex2D(_EnvironmentTex, reflectionUv).rgb;
            // Suppress environment mid-tones while retaining the bright panels, then tint them blue.
            float3 oilReflection = (reflectedEnvironment * reflectedEnvironment) * float3(0.25, 0.62, 0.9);
            float reflectEdge = cos(radians(_ReflectionAngle));
            float schlick = 0.035 + (0.965 * smoothstep(reflectEdge, 1.0, 1.0 - facingView));
            float3 matteSheenColor = float3(0.1, 0.33, 0.55);
            float3 color = (ambient * 0.2)
                + (fresnelValue * matteSheenColor * _MatteSheen * primitiveLift)
                + (specularValue * matteSheenColor * lerp(1.2, 0.6, _Reflectivity) * _MatteSheen)
                + (oilReflection * ((schlick * 2.5) + 0.12) * _Reflectivity)
                // Keep a little sheen where reflections land on the environment's dark rear wall.
                + (matteSheenColor * 0.4 * _Reflectivity);

            float surfaceIncidence = 1.0 - saturate(abs(dot(surfaceDirection, viewDirection)));

            float thinFilmHeight = saturate((input.spikeHeight * _ThinFilmScale) + _ThinFilmOffset);
            float3 filmColor = tex2D(_ThinFilmTex, float2(surfaceIncidence, thinFilmHeight)).rgb;
            float filmAlpha = lerp(0.18, 0.65, smoothstep(0.05, 0.85, surfaceIncidence));
            color += filmColor * filmAlpha;

            // The shading and LUT colors are in sRGB. Convert them before writing to the HDR target.
            color = GammaToLinearSpace(saturate(color) * _Exposure);
            color += _EmissionStrength;

            // Beat Saber's bloom prefilter multiplies RGB by alpha. Zero alpha keeps the surface out of bloom.
            return float4(color, 0.0);
        }
        ENDCG

        // Deformation can reverse local winding, so render both sides to avoid holes.
        Pass
        {
            Name "FERROFLUID_SURFACE"
            Cull Off
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex FerrofluidSurfaceVertex
            #pragma fragment FerrofluidSurfaceFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ FERROFLUID_CACHED_DEFORMATION
            ENDCG
        }

        // Only Graphics.Blit should run this pass. A custom LightMode prevents a second draw during normal rendering.
        Pass
        {
            Name "FERROFLUID_HEIGHT_SPRING"
            Tags { "LightMode"="FerrofluidHeightSpring" }
            Cull Off
            ZWrite Off
            ZTest Always

            CGPROGRAM
            #pragma vertex FerrofluidSpringVertex
            #pragma fragment FerrofluidHeightSpringFragment
            ENDCG
        }

        Pass
        {
            Name "FERROFLUID_SITE_DEFORMATION"
            Tags { "LightMode"="FerrofluidSiteDeformation" }
            Cull Off
            ZWrite Off
            ZTest Always

            CGPROGRAM
            #pragma vertex FerrofluidSpringVertex
            #pragma fragment FerrofluidSiteDeformationFragment
            ENDCG
        }

        Pass
        {
            Name "FERROFLUID_SITE_BULGE"
            Tags { "LightMode"="FerrofluidSiteBulge" }
            Cull Off
            ZWrite Off
            ZTest Always

            CGPROGRAM
            #pragma vertex FerrofluidSpringVertex
            #pragma fragment FerrofluidSiteBulgeFragment
            ENDCG
        }
    }

    Fallback Off
}
