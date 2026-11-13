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
        [HideInInspector] _FerrofluidBaseColor ("Base Color Linear", Vector) = (0.0007739938, 0.0007739938, 0.0007739938, 1)
        [HideInInspector] _FerrofluidReflectionStrengths ("Reflection Strengths", Vector) = (1, 1, 1, 1)
        [HideInInspector] _FerrofluidThinFilmStrength ("Thin Film Strength", Float) = 1
        [HideInInspector] _ThinFilmAngleRate ("Thin Film LUT Angle Rate", Float) = 1
        [HideInInspector] _BackHeightFalloffExponent ("Back Height Falloff Exponent", Float) = 0.8
        [HideInInspector] _MagnetHeightStrengthCap ("Magnet Height Strength Cap", Float) = 0.8
        [HideInInspector] _MagnetHeightDeadZoneRadius ("Magnet Height Dead Zone Radius", Float) = 0.15
        [HideInInspector] _SpikesEnabled ("Spikes Enabled", Float) = 1
        [HideInInspector] _FerroMaskSlope ("Mask Light Slope", Vector) = (0, 0, 0, 0)
        [HideInInspector] _FerroMaskFloorY ("Mask Floor Y", Float) = 0
        [HideInInspector] _FerroMaskCenter ("Mask Center", Vector) = (0, 0, 0, 0)
        [HideInInspector] _FerroMaskInvExtent ("Mask Inverse Extent", Vector) = (1, 1, 0, 0)
        [HideInInspector] _SpikeSpringStrength ("Spike Spring Strength", Float) = 120
        [HideInInspector] _SpikeSpringDamping ("Spike Spring Damping", Float) = 14
        [HideInInspector] _SurfaceShapeMode ("Surface Shape Mode", Float) = 0
        [HideInInspector] _SurfaceRoundness ("Surface Roundness", Float) = 0.15
        [HideInInspector] _SpikeMoundCurvature ("Spike Mound Curvature", Float) = 2
        [HideInInspector] _PrimitiveSpikeHeightRadius ("Primitive Spike Height Radius", Float) = 0.31
        [HideInInspector] _PrimitiveSpikeTiltRadius ("Primitive Spike Tilt Radius", Float) = 0.73
        [HideInInspector] _MagnetHeightAmplification ("Magnet Height Amplification", Float) = 1
        [HideInInspector] _RestPositionTex ("Rest Position", 2D) = "white" {}
        [HideInInspector] _DensityPressureTex ("Density Pressure", 2D) = "white" {}
        [HideInInspector] _SurfaceNormalTex ("Surface Normal Cache", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "Queue"="Geometry+10" "RenderType"="Opaque" }

        CGINCLUDE
        #pragma target 5.0
        #include "UnityCG.cginc"
        #include "FerrofluidCommon.cginc"

        sampler2D _HeightMapTex;
        sampler2D _SurfaceNormalTex;
        float _SurfaceNormalLogicalSize;
        float4 _HeightMapTex_TexelSize;
        sampler2D _SpringStateTex;
        sampler2D _RestPositionTex;
        sampler2D _DensityPressureTex;
        sampler2D _StateTex;
        sampler2D _SiteSurfaceTex;
        sampler2D _SiteDeformationTex;
        sampler2D _SiteBulgeTex;
        int _ParticleCount;
        sampler2D _EnvironmentTex;
        samplerCUBE _FerrofluidRoomEnvironment;
        float4 _FerrofluidRoomCapturePosition;
        float _FerrofluidRoomFloorY;
        float4 _FerrofluidRoomLogoPlane;
        sampler2D _FerrofluidFloorReflection;
        float4 _FerrofluidFloorReflectionBounds;
        float _FerrofluidFloorReflectionY;
        sampler2D _FerrofluidParabolaReflection;
        float4 _FerrofluidParabolaReflectionBounds;
        float _FerrofluidParabolaReflectionY;
        float _FerrofluidParabolaReflectionEnabled;
        UNITY_DECLARE_TEX2DARRAY(_FerrofluidMenuLightReflections);
        int _FerrofluidMenuLightCount;
        float4x4 _FerrofluidMenuLightWorldToLocal[8];
        float4 _FerrofluidMenuLightBounds[8];
        float4 _FerrofluidMenuLightFlags[8];
        sampler2D _ThinFilmTex;
        float4 _ThinFilmTex_TexelSize;
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
        float _ValidationOutput;
        float3 _ValidationCameraPosition;
        float3 _ValidationWorldPosition;
        float3 _ValidationWorldOffset;
        float2 _FerroMaskSlope;
        float _FerroMaskFloorY;
        float2 _FerroMaskCenter;
        float2 _FerroMaskInvExtent;

        float _ThinFilmScale;
        float _ThinFilmOffset;
        float _Reflectivity;
        float4 _FerrofluidBaseColor;
        float4 _FerrofluidReflectionStrengths;
        float _FerrofluidThinFilmStrength;
        float _ThinFilmAngleRate;
        float _SpikesEnabled;
        float _SpikeSpringStrength;
        float _SpikeSpringDamping;
        float _DeltaTimeMilliseconds;

        float3 FerrofluidRoomColor(float3 color)
        {
        #if defined(UNITY_COLORSPACE_GAMMA)
            return color;
        #else
            return LinearToGammaSpace(color);
        #endif
        }

        // Differentiate the reflected direction so wrapping U does not select an unrelated coarse mip.
        float3 FerrofluidSampleEnvironment(float3 direction)
        {
            float phi = atan2(direction.z, direction.x);
            float theta = acos(clamp(direction.y, -1.0, 1.0));
            float2 uv = float2(frac((-phi / 6.28318530718) + 1.0), theta / 3.14159265359);
            float3 dx = ddx(direction);
            float3 dy = ddy(direction);
            float h2 = max(dot(direction.xz, direction.xz), 0.00000001);
            float invPolar = rsqrt(max(1.0 - (direction.y * direction.y), 0.00000001));
            float2 gradX = float2(
                -(direction.x * dx.z - direction.z * dx.x) / (6.28318530718 * h2),
                -dx.y * invPolar / 3.14159265359);
            float2 gradY = float2(
                -(direction.x * dy.z - direction.z * dy.x) / (6.28318530718 * h2),
                -dy.y * invPolar / 3.14159265359);
            return tex2Dgrad(_EnvironmentTex, uv, gradX, gradY).rgb;
        }

        // Graphics.Blit supplies a quad transform, so the spring pass needs the surface's world matrix explicitly.
        float4x4 _FerrofluidObjectToWorld;
        float _SurfaceShapeMode;
        float _SurfaceRoundness;
        float _PrimitiveFlattenRadius;
        float _PrimitiveFlattenExponent;
        float _PrimitiveGlobalSpikeFlatten;
        float _SphereBulgeScale;
        float _PrimitiveSpikeHeightRadius;
        float _PrimitiveSpikeTiltRadius;
        float _MagnetHeightAmplification;
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
            float2 surfaceUv : TEXCOORD3;
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

        float2 SurfaceHeightUv(float2 uv);

        void BaseSurface(float2 uv, out float3 basePosition, out float3 surfaceNormal)
        {
            if (_SurfaceShapeMode < 0.5)
            {
                TorusSurface(uv, basePosition, surfaceNormal);
            }
            else
            {
                PrimitiveSurface(SurfaceHeightUv(uv), basePosition, surfaceNormal);
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

        void FerrofluidApplyMagnet(inout FerrofluidSiteField field, float3 position, float strength, float openingStrength, float radiusScale)
        {
            field.strength = strength;
            field.openingStrength = openingStrength;
            float3 siteFieldVector = position - field.basePosition;
            field.fieldDistance = length(siteFieldVector);
            field.fieldDirection = siteFieldVector / max(field.fieldDistance, 0.000001);
            field.fieldDistance /= radiusScale;
            float normalizedSiteFieldDistance = field.fieldDistance / (_SurfaceShapeMode > 0.5 ? _PrimitiveSpikeTiltRadius : _BulgeRadius);
            field.fieldFacing = dot(field.surfaceNormal, field.fieldDirection);
            float3 siteTangentField = siteFieldVector
                - (field.surfaceNormal * dot(siteFieldVector, field.surfaceNormal));
            field.tangentFieldDistance = length(siteTangentField) / radiusScale;
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
            // Trigger pressure expands the whole spike response, including its dead zone, peak and tilt distances.
            float radiusScale = _SurfaceShapeMode > 0.5 ? 1.0 + saturate((_MagnetHeightAmplification - 1.0) / 0.6) : 1.0;
            FerrofluidApplyMagnet(field, _MagneticFieldPosition, _MagneticFieldStrength, _MagnetOpeningStrength, radiusScale);
        }

        float FerrofluidSingleMagneticHeightTarget(FerrofluidSiteField field, float heightAmplification, out float frontFieldResponse)
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
            float frontHeightGain = _HeightGain * heightAmplification * (_SurfaceShapeMode > 0.5
                ? heightFieldStrength * (1.0 - pow(1.0 - saturate(frontUnitResponse), _SpikeMoundCurvature))
                : frontFieldResponse);

            float primitiveBaseHeightScale = _SurfaceShapeMode > 0.5 ? 0.44 : 0.18;
            float primitiveBackHeightRadius = heightRadius * (_SurfaceShapeMode > 0.5 ? _PrimitiveFlattenRadius : 1.0);
            float primitiveMaximumHeight = primitiveBaseHeightScale
                + (heightAmplification * ((_SurfaceShapeMode > 0.5 ? min(_MaximumHeight, 1.37) : _MaximumHeight) - primitiveBaseHeightScale));
            float normalizedBackHeightDistance = heightFieldDistance / max(primitiveBackHeightRadius, 0.000001);
            // Normalize against the shared default so primitive tuning is unchanged at that value.
            float backFieldResponse = heightFieldStrength
                * pow(max(0.0, 1.0 - normalizedBackHeightDistance),
                    _SurfaceShapeMode > 0.5
                        ? _PrimitiveFlattenExponent * (_BackHeightFalloffExponent / 2.65)
                        : _BackHeightFalloffExponent);
            float backHeightLoss = _BackHeightLoss * heightAmplification * max(-field.fieldFacing, 0.0) * backFieldResponse;
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
                + (_HeightGain * heightAmplification * openingOuterResponse)
                - (_BackHeightLoss * heightAmplification * openingInnerResponse),
                0.03,
                primitiveMaximumHeight);
        }

        // Sum each magnet's departure from the resting height, keeping both fields continuous when their influence overlaps.
        float FerrofluidMagneticHeightTarget(FerrofluidSiteField field, out float frontFieldResponse)
        {
            float height = FerrofluidSingleMagneticHeightTarget(field, _MagnetHeightAmplification, frontFieldResponse);
            if (_SecondaryMagneticFieldStrength > 0.0)
            {
                FerrofluidSiteField second = field;
                FerrofluidApplyMagnet(second, _SecondaryMagneticFieldPosition, _SecondaryMagneticFieldStrength, 0.0, 1.0);
                float secondFrontResponse;
                height += FerrofluidSingleMagneticHeightTarget(second, 1.0, secondFrontResponse) - (_SurfaceShapeMode > 0.5 ? 0.44 : 0.18);
                frontFieldResponse += secondFrontResponse;
                float combinedMax = (_SurfaceShapeMode > 0.5 ? 0.44 : 0.18)
                    + (_MagnetHeightAmplification * ((_SurfaceShapeMode > 0.5 ? min(_MaximumHeight, 1.37) : _MaximumHeight) - (_SurfaceShapeMode > 0.5 ? 0.44 : 0.18)));
                height = clamp(height, 0.03, combinedMax);
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

        float FerrofluidDomeEase(float unit)
        {
            return unit * unit * unit * ((unit * ((6.0 * unit) - 15.0)) + 10.0);
        }

        float FerrofluidMagnetBulge(float3 worldBasePosition, float3 worldSurfaceNormal, float3 position, float strength, float heightAmplification)
        {
            float3 delta = position - worldBasePosition;
            float distanceSquared = dot(delta, delta);
            float distance = sqrt(distanceSquared);
            float facing = dot(worldSurfaceNormal, delta / max(distance, 0.000001));
            if (_SurfaceShapeMode <= 0.5)
            {
                float heightDistance = max(distance - _MagnetHeightDeadZoneRadius, 0.0);
                return _BulgeHeight
                    * max(facing, 0.0)
                    * FerrofluidBulgeEnvelope(heightDistance / _BulgeRadius, _HeightFalloffExponent)
                    * min(strength, _MagnetHeightStrengthCap)
                    * heightAmplification;
            }

            // A single radial bell avoids a narrow facing-driven crest on a wider mound.
            float radialDome = exp(
                -0.5 * max(_HeightFalloffExponent, 0.25)
                * distanceSquared / (_BulgeRadius * _BulgeRadius));
            float outerRadius = 0.9 * ((_BulgeRadius * 1.5) + _MagnetHeightDeadZoneRadius);
            float outerBlend = FerrofluidDomeEase(
                saturate((outerRadius - distance) / (outerRadius * 0.25)));
            // Facing gates only the back-side skirt; it is constant across the front-facing dome.
            float backBlend = FerrofluidDomeEase(saturate(1.0 + facing));
            return _BulgeHeight
                * radialDome * outerBlend * backBlend
                * min(strength, _MagnetHeightStrengthCap)
                * heightAmplification
                * (_SurfaceShapeMode > 1.5 ? _SphereBulgeScale : 1.0);
        }

        void CalculateSpikeDeformation(float2 siteParticleUv, float4x4 objectToWorld, out float3 spikeDirection, out float magneticHeightScale, out float apexSharpening)
        {
            FerrofluidSiteField field;
            FerrofluidSiteFrame(siteParticleUv, objectToWorld, field);
            float frontFieldResponse;
            magneticHeightScale = FerrofluidMagneticHeightTarget(field, frontFieldResponse);
            apexSharpening = smoothstep(0.25, 0.6, frontFieldResponse);
            spikeDirection = FerrofluidSingleSpikeDirection(field);
            if (_SecondaryMagneticFieldStrength > 0.0)
            {
                FerrofluidSiteField second = field;
                FerrofluidApplyMagnet(second, _SecondaryMagneticFieldPosition, _SecondaryMagneticFieldStrength, 0.0, 1.0);
                float3 secondDirection = FerrofluidSingleSpikeDirection(second);
                float firstWeight = field.coherentFieldFalloff;
                float secondWeight = second.coherentFieldFalloff;
                float totalWeight = firstWeight + secondWeight;
                spikeDirection = normalize(lerp(spikeDirection, secondDirection, secondWeight / max(totalWeight, 0.000001)));
            }
        }

        void SpikeDeformation(float2 siteParticleUv, out float3 spikeDirection, out float magneticHeightScale, out float apexSharpening)
        {
        #if defined(FERROFLUID_CACHED_DEFORMATION)
            float4 deformation = tex2Dlod(_SiteDeformationTex, float4(siteParticleUv, 0.0, 0.0));
            spikeDirection = deformation.xyz;
            apexSharpening = deformation.w;
            // Vertex height comes from the spring texture, so the instantaneous target is not cached.
            magneticHeightScale = 0.0;
        #else
            CalculateSpikeDeformation(siteParticleUv, unity_ObjectToWorld, spikeDirection, magneticHeightScale, apexSharpening);
        #endif
        }

        // Each site has one magnet response shared by all vertices and normal samples that refer to it.
        float4 FerrofluidSiteDeformationFragment(FerrofluidSpringVaryings input) : SV_Target
        {
            float3 direction;
            float height;
            float sharpening;
            CalculateSpikeDeformation(input.uv, _FerrofluidObjectToWorld, direction, height, sharpening);
            return float4(direction, sharpening);
        }

        float4 FerrofluidSiteBulgeFragment(FerrofluidSpringVaryings input) : SV_Target
        {
            float densityHeight = 0.0;
            if (_SurfaceShapeMode > 0.5)
            {
                int particleId = FerrofluidParticleId(input.uv);
                if (particleId >= _ParticleCount)
                {
                    return 0.0;
                }

                float localSpacing = LoadFerrofluidTexel(_SiteSurfaceTex, particleId).w;
                float rho = LoadFerrofluidTexel(_DensityPressureTex, particleId).r;
                float rho0 = LoadFerrofluidTexel(_RestPositionTex, particleId).z;
                float scale = length(mul((float3x3)_FerrofluidObjectToWorld, float3(1.0, 0.0, 0.0)));
                densityHeight = 0.12 * localSpacing * scale * clamp((rho / rho0) - 1.0, -0.75, 0.75);
            }

            return float4(0.0, 0.0, 0.0, densityHeight);
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

        // One positive cubic B-spline per channel over the complete per-tap spike displacement.
        // Each tap composes its own axis, height, spring and flatten term before blending, so no
        // normalized-axis average or height-times-spring cross product can overshoot the nodes.
        struct FerrofluidSurfaceField
        {
            float3 offset;
            float3 offsetDu;
            float3 offsetDv;
            float density;
            float densityDu;
            float densityDv;
            float film;
        };

        float4 FerrofluidBsplineWeights(float t)
        {
            float oneMinusT = 1.0 - t;
            return float4(
                oneMinusT * oneMinusT * oneMinusT,
                (3.0 * t * t * t) - (6.0 * t * t) + 4.0,
                (-3.0 * t * t * t) + (3.0 * t * t) + (3.0 * t) + 1.0,
                t * t * t) / 6.0;
        }

        float4 FerrofluidBsplineDerivatives(float t)
        {
            float oneMinusT = 1.0 - t;
            return float4(
                -0.5 * oneMinusT * oneMinusT,
                (1.5 * t * t) - (2.0 * t),
                (-1.5 * t * t) + t + 0.5,
                0.5 * t * t);
        }

        FerrofluidSurfaceField EvaluateSurfaceField(float2 uv)
        {
            float2 texelCoord = (uv * _HeightMapTex_TexelSize.zw) - 0.5;
            float2 texelBase = floor(texelCoord);
            float2 texelFrac = texelCoord - texelBase;
            float4 weightsX = FerrofluidBsplineWeights(texelFrac.x);
            float4 weightsY = FerrofluidBsplineWeights(texelFrac.y);
            // Derivative weights carry the texture dimensions so they express d/du and d/dv.
            float4 derivativesX = FerrofluidBsplineDerivatives(texelFrac.x) * _HeightMapTex_TexelSize.z;
            float4 derivativesY = FerrofluidBsplineDerivatives(texelFrac.y) * _HeightMapTex_TexelSize.w;

            float bulgeFieldStrength = min(_MagneticFieldStrength, _MagnetHeightStrengthCap);
            float globalSpikeFlatten = _SurfaceShapeMode > 0.5
                ? saturate(_PrimitiveGlobalSpikeFlatten * bulgeFieldStrength)
                : 0.0;

            FerrofluidSurfaceField field;
            field.offset = 0.0;
            field.offsetDu = 0.0;
            field.offsetDv = 0.0;
            field.density = 0.0;
            field.densityDu = 0.0;
            field.densityDv = 0.0;
            field.film = 0.0;

            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    // Fold each tap, not just the requested UV, so both copies of an edge share one neighborhood.
                    float2 tapUv = SurfaceHeightUv((texelBase + float2(column - 1, row - 1) + 0.5) * _HeightMapTex_TexelSize.xy);
                    float4 height = tex2Dlod(_HeightMapTex, float4(tapUv, 0.0, 0.0));
                    float2 particleUv = FerrofluidParticleUv((int)round(height.g));
                    float3 direction;
                    float instantHeightScale;
                    float sharpening;
                    SpikeDeformation(particleUv, direction, instantHeightScale, sharpening);
                    float spring = tex2Dlod(_SpringStateTex, float4(particleUv, 0.0, 0.0)).x;
                    float selectedHeight = lerp(height.r, height.a, sharpening) * _SpikesEnabled;
                    float3 tapOffset = direction * selectedHeight * spring * _FluidHeightScale * (1.0 - globalSpikeFlatten);
                    float tapDensity = height.b;
                    float lutHeightScale = _SurfaceShapeMode > 0.5 ? max(spring - 0.26, 0.0) : spring;
                    float tapFilm = selectedHeight * lutHeightScale * (1.0 - globalSpikeFlatten);

                    float weightXY = weightsY[row] * weightsX[column];
                    field.offset += tapOffset * weightXY;
                    field.offsetDu += tapOffset * (weightsY[row] * derivativesX[column]);
                    field.offsetDv += tapOffset * (derivativesY[row] * weightsX[column]);
                    field.density += tapDensity * weightXY;
                    field.densityDu += tapDensity * (weightsY[row] * derivativesX[column]);
                    field.densityDv += tapDensity * (derivativesY[row] * weightsX[column]);
                    field.film += tapFilm * weightXY;
                }
            }

            field.film /= max(_HeightFactor * _MaximumHeight, 0.000001);
            return field;
        }

        // Support follows the queried surface point, not the spike owning a height-map texel.
        float FerrofluidSupportBulge(float3 worldBasePosition, float3 worldSurfaceNormal)
        {
            float bulgeHeight = 0.0;
            if (_MagneticFieldStrength > 0.0)
            {
                bulgeHeight = FerrofluidMagnetBulge(worldBasePosition, worldSurfaceNormal, _MagneticFieldPosition, _MagneticFieldStrength, _MagnetHeightAmplification);
            }
            if (_SecondaryMagneticFieldStrength > 0.0)
            {
                bulgeHeight += FerrofluidMagnetBulge(worldBasePosition, worldSurfaceNormal, _SecondaryMagneticFieldPosition, _SecondaryMagneticFieldStrength, 1.0);
            }
            return bulgeHeight;
        }

        float3 FerrofluidDisplacedWorld(float3 basePosition, float3 baseNormal, FerrofluidSurfaceField field)
        {
            float3 worldBasePosition = RenderedWorldPosition(basePosition, unity_ObjectToWorld);
            float3 worldSurfaceNormal = RenderedWorldDirection(basePosition, baseNormal, unity_ObjectToWorld);
            float bulgeHeight = FerrofluidSupportBulge(worldBasePosition, worldSurfaceNormal);
            return worldBasePosition
                + field.offset
                + (worldSurfaceNormal * (bulgeHeight + field.density));
        }

        float3 FerrofluidDisplacedSample(float2 uv)
        {
            FerrofluidSurfaceField field = EvaluateSurfaceField(SurfaceHeightUv(uv));
            float3 basePosition;
            float3 baseNormal;
            BaseSurface(uv, basePosition, baseNormal);
            float3 worldPosition = FerrofluidDisplacedWorld(basePosition, baseNormal, field);
            return mul(unity_WorldToObject, float4(worldPosition, 1.0)).xyz;
        }

        FerrofluidSurfaceVaryings FerrofluidSurfaceVertex(FerrofluidSurfaceInput input)
        {
            UNITY_SETUP_INSTANCE_ID(input);
            FerrofluidSurfaceVaryings output;
            UNITY_INITIALIZE_OUTPUT(FerrofluidSurfaceVaryings, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            float2 uv = input.uv;
            output.surfaceUv = input.uv;
            FerrofluidSurfaceField field = EvaluateSurfaceField(SurfaceHeightUv(uv));

            // Film follows the same node weights as the geometry: density adds on top before saturating.
            output.spikeHeight = saturate(field.film
                + (field.density / (_FluidHeightScale * max(_HeightFactor * _MaximumHeight, 0.000001))));

            float3 analyticBasePosition;
            float3 analyticNormal;
            BaseSurface(uv, analyticBasePosition, analyticNormal);
            float3 worldBasePosition = RenderedWorldPosition(analyticBasePosition, unity_ObjectToWorld);
            float3 worldAnalyticNormal = RenderedWorldDirection(analyticBasePosition, analyticNormal, unity_ObjectToWorld);
            float bulgeHeight = FerrofluidSupportBulge(worldBasePosition, worldAnalyticNormal);
            float3 worldPosition = worldBasePosition
                + field.offset
                + (worldAnalyticNormal * (bulgeHeight + field.density));

            #if defined(FERROFLUID_CACHED_NORMALS)
            // The beauty pixel replaces this with the baked continuous normal; skip reconstruction.
            float3 worldNormal = worldAnalyticNormal;
            #else
            // Analytic UV derivatives of the reconstructed field replace the four displaced normal samples.
            float2 epsilon = 2.0 * _HeightMapTex_TexelSize.xy;
            float2 previousPerimeterUv = uv - float2(epsilon.x, 0.0);
            float2 nextPerimeterUv = uv + float2(epsilon.x, 0.0);
            float2 previousCrossSectionUv = uv - float2(0.0, epsilon.y);
            float2 nextCrossSectionUv = uv + float2(0.0, epsilon.y);
            // Use the undeformed surface's winding to orient normals.
            // Comparing against the base normal would incorrectly flip the underside of an overhanging spike.
            float3 basePreviousPerimeter;
            float3 baseNextPerimeter;
            float3 basePreviousCross;
            float3 baseNextCross;
            float3 normalPreviousPerimeter;
            float3 normalNextPerimeter;
            float3 normalPreviousCross;
            float3 normalNextCross;
            BaseSurface(previousPerimeterUv, basePreviousPerimeter, normalPreviousPerimeter);
            BaseSurface(nextPerimeterUv, baseNextPerimeter, normalNextPerimeter);
            BaseSurface(previousCrossSectionUv, basePreviousCross, normalPreviousCross);
            BaseSurface(nextCrossSectionUv, baseNextCross, normalNextCross);
            float3 baseFiniteNormal = cross(
                baseNextPerimeter - basePreviousPerimeter,
                baseNextCross - basePreviousCross);
            float chartWinding = dot(baseFiniteNormal, baseFiniteNormal) > 0.000000001
                ? (dot(baseFiniteNormal, analyticNormal) >= 0.0 ? 1.0 : -1.0)
                : 0.0;

            // The base tangent comes from the analytic base secants alone. For primitives the
            // octahedral chart folds bias those secants, so they are projected onto the analytic
            // tangent plane like the seam fix; displacement slopes still come from the field.
            float3 baseTangentU = baseNextPerimeter - basePreviousPerimeter;
            float3 baseTangentV = baseNextCross - basePreviousCross;
            if (_SurfaceShapeMode > 0.5)
            {
                baseTangentU -= analyticNormal * dot(baseTangentU, analyticNormal);
                baseTangentV -= analyticNormal * dot(baseTangentV, analyticNormal);
            }
            float2 derivativeSpan = 2.0 * epsilon;
            // Rendered base positions carry the torus's skew and curved-canvas warp, so the
            // rendered secants are evaluated once and reused for the support queries below.
            float3 worldBasePreviousPerimeter = RenderedWorldPosition(basePreviousPerimeter, unity_ObjectToWorld);
            float3 worldBaseNextPerimeter = RenderedWorldPosition(baseNextPerimeter, unity_ObjectToWorld);
            float3 worldBasePreviousCross = RenderedWorldPosition(basePreviousCross, unity_ObjectToWorld);
            float3 worldBaseNextCross = RenderedWorldPosition(baseNextCross, unity_ObjectToWorld);
            float3 worldTangentU;
            float3 worldTangentV;
            if (_SurfaceShapeMode > 0.5)
            {
                // Primitives have no canvas warp; the projected local secant transforms directly.
                worldTangentU = mul((float3x3)unity_ObjectToWorld, baseTangentU / derivativeSpan.x);
                worldTangentV = mul((float3x3)unity_ObjectToWorld, baseTangentV / derivativeSpan.y);
            }
            else
            {
                worldTangentU = (worldBaseNextPerimeter - worldBasePreviousPerimeter) / derivativeSpan.x;
                worldTangentV = (worldBaseNextCross - worldBasePreviousCross) / derivativeSpan.y;
            }

            // Support derivatives reuse the four analytic base evaluations: normal change times the
            // support height plus the normal times the bulge and density change along the surface.
            float3 worldNormalPreviousPerimeter = RenderedWorldDirection(basePreviousPerimeter, normalPreviousPerimeter, unity_ObjectToWorld);
            float3 worldNormalNextPerimeter = RenderedWorldDirection(baseNextPerimeter, normalNextPerimeter, unity_ObjectToWorld);
            float3 worldNormalPreviousCross = RenderedWorldDirection(basePreviousCross, normalPreviousCross, unity_ObjectToWorld);
            float3 worldNormalNextCross = RenderedWorldDirection(baseNextCross, normalNextCross, unity_ObjectToWorld);
            float3 worldNormalDu = (worldNormalNextPerimeter - worldNormalPreviousPerimeter) / derivativeSpan.x;
            float3 worldNormalDv = (worldNormalNextCross - worldNormalPreviousCross) / derivativeSpan.y;
            float bulgeNextPerimeter = FerrofluidSupportBulge(worldBaseNextPerimeter, worldNormalNextPerimeter);
            float bulgePreviousPerimeter = FerrofluidSupportBulge(worldBasePreviousPerimeter, worldNormalPreviousPerimeter);
            float bulgeNextCross = FerrofluidSupportBulge(worldBaseNextCross, worldNormalNextCross);
            float bulgePreviousCross = FerrofluidSupportBulge(worldBasePreviousCross, worldNormalPreviousCross);
            float bulgeDu = (bulgeNextPerimeter - bulgePreviousPerimeter) / derivativeSpan.x;
            float bulgeDv = (bulgeNextCross - bulgePreviousCross) / derivativeSpan.y;
            float3 supportDu = (worldNormalDu * (bulgeHeight + field.density)) + (worldAnalyticNormal * (bulgeDu + field.densityDu));
            float3 supportDv = (worldNormalDv * (bulgeHeight + field.density)) + (worldAnalyticNormal * (bulgeDv + field.densityDv));

            float3 finiteNormal = cross(
                worldTangentU + field.offsetDu + supportDu,
                worldTangentV + field.offsetDv + supportDv);
            // At chart singularities the base secants cannot establish winding. Orient the resulting normal outward there.
            chartWinding = chartWinding == 0.0
                ? (dot(finiteNormal, worldAnalyticNormal) >= 0.0 ? 1.0 : -1.0)
                : chartWinding;
            finiteNormal *= chartWinding;

            float3 worldNormal;
            if (dot(finiteNormal, finiteNormal) > 0.000000001)
            {
                worldNormal = normalize(finiteNormal);
            }
            else if (_SurfaceShapeMode > 0.5)
            {
                // UV derivatives collapse at chart tips and the bottom pole, but physical tangent samples still capture the displaced slope.
                float3 reference = abs(analyticNormal.y) < 0.9 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
                float3 tangent = normalize(cross(analyticNormal, reference));
                float3 bitangent = cross(analyticNormal, tangent);
                float step = _HeightMapTex_TexelSize.x;
                float2 minusT = FerrofluidPrimitiveSurfaceUv(analyticBasePosition - (tangent * step), _PrimitiveYawCosSin);
                float2 plusT = FerrofluidPrimitiveSurfaceUv(analyticBasePosition + (tangent * step), _PrimitiveYawCosSin);
                float2 minusB = FerrofluidPrimitiveSurfaceUv(analyticBasePosition - (bitangent * step), _PrimitiveYawCosSin);
                float2 plusB = FerrofluidPrimitiveSurfaceUv(analyticBasePosition + (bitangent * step), _PrimitiveYawCosSin);
                float3 physicalNormal = cross(
                    FerrofluidDisplacedSample(plusT) - FerrofluidDisplacedSample(minusT),
                    FerrofluidDisplacedSample(plusB) - FerrofluidDisplacedSample(minusB));
                worldNormal = normalize(mul((float3x3)unity_ObjectToWorld, physicalNormal));
            }
            else
            {
                worldNormal = worldAnalyticNormal;
            }
            #endif

            output.worldPosition = worldPosition;
            output.worldNormal = worldNormal;
            output.vertex = UnityWorldToClipPos(worldPosition);
            return output;
        }

        // Validation renders this pass to read back the deformed surface's world positions.
        // It keeps the draw's own clip positions so a fullscreen blit covers the render target.
        FerrofluidSurfaceVaryings FerrofluidWorldPositionVertex(FerrofluidSurfaceInput input)
        {
            FerrofluidSurfaceVaryings output = FerrofluidSurfaceVertex(input);
            output.vertex = input.vertex;
            return output;
        }

        float4 FerrofluidWorldPositionFragment(FerrofluidSurfaceVaryings input) : SV_Target
        {
            // Validation mode 1 reads the deformed normal and spike film; 0 reads the world position.
            [branch]
            if (_ValidationOutput > 0.5)
            {
                return float4(normalize(input.worldNormal), input.spikeHeight);
            }

            return float4(input.worldPosition, 1.0);
        }

        // Floor shadow mask: the real deformed silhouette projected along the light onto the
        // floor. Reuses the production displacement path so the mask matches the rendered
        // mesh, then remaps clip space into the mask's world bounds. No normal/lighting work.
        float4 FerrofluidShadowMaskVertex(FerrofluidSurfaceInput input) : SV_POSITION
        {
            UNITY_SETUP_INSTANCE_ID(input);
            float3 localPosition = FerrofluidDisplacedSample(input.uv);
            float3 worldPosition = mul(unity_ObjectToWorld, float4(localPosition, 1.0)).xyz;
            float2 hit = worldPosition.xz
                - ((worldPosition.y - _FerroMaskFloorY) * _FerroMaskSlope);
            float2 maskClip = (hit - _FerroMaskCenter) * _FerroMaskInvExtent;
#if UNITY_UV_STARTS_AT_TOP
            // ReadPixels and tex2D share bottom-left UV origin: negate clip-Y on APIs whose
            // clip space runs top-down so positive world Z maps to positive texture V.
            maskClip.y = -maskClip.y;
#endif
            return float4(maskClip.x, maskClip.y, 0.0, 1.0);
        }

        fixed4 FerrofluidShadowMaskFragment() : SV_Target
        {
            return fixed4(1.0, 1.0, 1.0, 1.0);
        }

        float3 FerrofluidSampleViewReflection(float3 worldPosition, float3 viewDirection, float3 lightingNormal)
        {
            // physical; artistic knobs were removed, so the room cube, feet patch, and the
            // procedural light atlas each arrive with their own slider strength.
            float3 reflectionRay = reflect(-viewDirection, lightingNormal);
            float inverseDownwardRay = rcp(max(-reflectionRay.y, 0.0001));
            float3 radiance = float3(0.0, 0.0, 0.0);
        #if defined(FERROFLUID_ROOM_REFLECTIONS)
            // Project the world-space reflection ray onto the feet plane for the
            // position-correct floor patch.
            float floorT = (worldPosition.y - _FerrofluidFloorReflectionY) * inverseDownwardRay;
            float2 floorHit = worldPosition.xz + floorT * reflectionRay.xz;
            float2 floorUV = (floorHit - _FerrofluidFloorReflectionBounds.xy) * _FerrofluidFloorReflectionBounds.zw;
            float floorValid =
                worldPosition.y > _FerrofluidFloorReflectionY
                && reflectionRay.y < -0.0001
                && floorUV.x >= 0.0 && floorUV.x <= 1.0
                && floorUV.y >= 0.0 && floorUV.y <= 1.0
                ? 1.0 : 0.0;
            float3 floorPatch = tex2Dgrad(_FerrofluidFloorReflection, floorUV, ddx(floorUV), ddy(floorUV)).rgb * floorValid * _FerrofluidReflectionStrengths.y;

            // The horizon band is baked into the room cubemap; the opaque floor at
            // _FerrofluidRoomFloorY reprojects downward rays to the captured ground.
            float roomFloorY = _FerrofluidRoomFloorY;
            bool roomFloorHit = worldPosition.y >= roomFloorY && reflectionRay.y < -0.0001;
            float floorDistance = roomFloorHit ? (worldPosition.y - roomFloorY) / (-reflectionRay.y) : 1e9;
            float3 groundCaptureDirection = float3(0.0, -1.0, 0.0);
            if (roomFloorHit)
                groundCaptureDirection = normalize(worldPosition + floorDistance * reflectionRay - _FerrofluidRoomCapturePosition.xyz);

            // Far room: sign plane first, ground reprojection when floor-blocked, else ray.
            float planeDenominator = dot(reflectionRay, _FerrofluidRoomLogoPlane.xyz);
            float planeT = abs(planeDenominator) > 1e-6
                ? -(dot(worldPosition, _FerrofluidRoomLogoPlane.xyz) + _FerrofluidRoomLogoPlane.w) / planeDenominator
                : -1.0;
            bool planeHit = planeT > 0.0001 && planeT < floorDistance;
            float3 farDirection;
            if (planeHit)
                farDirection = normalize(worldPosition + planeT * reflectionRay - _FerrofluidRoomCapturePosition.xyz);
            else if (roomFloorHit)
                farDirection = groundCaptureDirection;
            else
                farDirection = reflectionRay;
            float3 farRoom = texCUBE(_FerrofluidRoomEnvironment, farDirection).rgb;

            radiance = farRoom * _FerrofluidReflectionStrengths.x + floorPatch;
        #else
            // The legacy JPG import ships gamma-encoded; convert so the same linear HDR add works.
            // NOREF keeps procedural lights live but must not fall back to the legacy JPG.
        #if !defined(FERROFLUID_NO_REFLECTIONS)
            radiance = GammaToLinearSpace(FerrofluidSampleEnvironment(reflectionRay)) * _FerrofluidReflectionStrengths.x;
        #endif
        #endif
            // Procedural light atlases stay live without the optional room pack; FERROFLUID_NO_REFLECTIONS
            // only suppresses the captured room and feet patch above.
            [branch]
            if (_FerrofluidMenuLightCount > 0)
            {
                // Each saved light contributes its local-space reference plane; the collection
                // replaces the single-map legacy path while active.
                float3 collection = float3(0.0, 0.0, 0.0);
                [unroll]
                for (int lightIndex = 0; lightIndex < 8; lightIndex++)
                {
                    if (lightIndex >= _FerrofluidMenuLightCount)
                        break;
                    if (_FerrofluidMenuLightFlags[lightIndex].x < 0.5)
                        continue;
                    float3 localOrigin = mul(_FerrofluidMenuLightWorldToLocal[lightIndex], float4(worldPosition, 1.0)).xyz;
                    float3 localRay = mul(_FerrofluidMenuLightWorldToLocal[lightIndex], float4(reflectionRay, 0.0)).xyz;
                    float denominator = localRay.y;
                    denominator = abs(denominator) < 0.0001 ? (denominator >= 0.0 ? 0.0001 : -0.0001) : denominator;
                    float hitT = -localOrigin.y / denominator;
                    float3 localHit = localOrigin + hitT * localRay;
                    float4 lightBounds = _FerrofluidMenuLightBounds[lightIndex];
                    float2 lightUV = (localHit.xz - lightBounds.xy) * lightBounds.zw;
                    float2 lightUVx = ddx(lightUV);
                    float2 lightUVy = ddy(lightUV);
                    // Sampling inside the hit-test branch returns black in the room-disabled D3D11 variants.
                    float3 lightSample = _FerrofluidMenuLightReflections.SampleGrad(sampler_FerrofluidMenuLightReflections, float3(lightUV, lightIndex), lightUVx, lightUVy).rgb;
                    [branch]
                    if (abs(localRay.y) > 0.0001 && hitT > 0.0 && all(lightUV >= 0.0) && all(lightUV <= 1.0))
                        collection += lightSample;
                }
                float3 collectionLuma = dot(collection, float3(0.2126, 0.7152, 0.0722)).xxx;
                radiance += lerp(collectionLuma, collection, _FerrofluidReflectionStrengths.w) * _FerrofluidReflectionStrengths.z;
            }
            else if (_FerrofluidParabolaReflectionEnabled > 0.5)
            {
                float lightT = (worldPosition.y - _FerrofluidParabolaReflectionY) * inverseDownwardRay;
                float2 lightHit = worldPosition.xz + lightT * reflectionRay.xz;
                float2 lightUV = (lightHit - _FerrofluidParabolaReflectionBounds.xy) * _FerrofluidParabolaReflectionBounds.zw;
                float lightValid =
                    worldPosition.y > _FerrofluidParabolaReflectionY
                    && reflectionRay.y < -0.0001
                    && lightUV.x >= 0.0 && lightUV.x <= 1.0
                    && lightUV.y >= 0.0 && lightUV.y <= 1.0
                    ? 1.0 : 0.0;
                float3 parabola = tex2Dgrad(_FerrofluidParabolaReflection, lightUV, ddx(lightUV), ddy(lightUV)).rgb;
                float3 parabolaLuma = dot(parabola, float3(0.2126, 0.7152, 0.0722)).xxx;
                radiance += lerp(parabolaLuma, parabola, _FerrofluidReflectionStrengths.w) * lightValid * _FerrofluidReflectionStrengths.z;
            }

            return radiance;
        }

        float4 FerrofluidShadeSurface(FerrofluidSurfaceVaryings input, float facing, float3 cameraPosition)
        {
        #if defined(FERROFLUID_ENVIRONMENT_CAPTURE)
            clip(-1.0);
        #endif
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

            float3 viewDirection = normalize(cameraPosition - input.worldPosition);
            float3 surfaceDirection = normalize(input.worldNormal);
            float3 lightingNormal = facing < 0.0 ? -surfaceDirection : surfaceDirection;
            float3 reflectedEnvironment = FerrofluidSampleViewReflection(input.worldPosition, viewDirection, lightingNormal);
            float3 color = _FerrofluidBaseColor.rgb * _Exposure;

            [branch]
            if (_FerrofluidThinFilmStrength > 0.0)
            {
                float surfaceIncidence = 1.0 - saturate(abs(dot(surfaceDirection, viewDirection)));
                float thinFilmHeight = saturate((input.spikeHeight * _ThinFilmScale) + _ThinFilmOffset);
                float2 filmUv = float2(saturate(surfaceIncidence * _ThinFilmAngleRate), thinFilmHeight);
                float3 filmColor = tex2D(_ThinFilmTex, filmUv).rgb;
                float filmAlpha = lerp(0.18, 0.65, smoothstep(0.05, 0.85, surfaceIncidence));
                // Minified lookup mips mix brighter angles into the silhouette. Fade the film using the actual viewing angle.
                filmAlpha *= 1.0 - smoothstep(0.7, 1.0, surfaceIncidence);
                if (_SurfaceShapeMode > 0.5)
                {
                    // Fade the filtered tint when the lookup footprint spans a large part of a spike's color band.
                    float2 filmDx = ddx(filmUv) * _ThinFilmTex_TexelSize.zw;
                    float2 filmDy = ddy(filmUv) * _ThinFilmTex_TexelSize.zw;
                    float footprintSquared = max(dot(filmDx, filmDx), dot(filmDy, filmDy));
                    filmAlpha *= lerp(1.0, 0.55, smoothstep(256.0, 4096.0, footprintSquared));
                }

                color += GammaToLinearSpace(filmColor * filmAlpha) * _FerrofluidThinFilmStrength * _Exposure;
            }

            // Fixed two-sided Fresnel: Cull Off means wound normals can flip, so use the
            // absolute incidence. Front reflections weight the reflectivity master;
            // grazing peaks at 1 + 1.62 like the approved prototype.
            float incidence = 1.0 - saturate(abs(dot(lightingNormal, viewDirection)));
            float incidence2 = incidence * incidence;
            float reflectionGain = 1.0 + (1.62 * incidence2 * incidence2 * incidence);
            color += reflectedEnvironment * _Reflectivity * reflectionGain * _Exposure;
            color += _EmissionStrength;

            // Beat Saber's bloom prefilter multiplies RGB by alpha. Zero alpha keeps the surface out of bloom.
            return float4(color, 0.0);
        }

        // Filtered bicubic sample of the baked continuous normal/film cache (beauty path only).
        float4 FerrofluidSampleNormalCache(float2 uv)
        {
            float size = _SurfaceNormalLogicalSize;
            float2 q = uv * size - 0.5;
            float2 b = floor(q);
            float4 wx = FerrofluidBsplineWeights(frac(q.x));
            float4 wy = FerrofluidBsplineWeights(frac(q.y));
            float2 gx = float2(wx.x + wx.y, wx.z + wx.w);
            float2 gy = float2(wy.x + wy.y, wy.z + wy.w);
            float2 ox = float2(-1 + wx.y / gx.x, 1 + wx.w / gx.y);
            float2 oy = float2(-1 + wy.y / gy.x, 1 + wy.w / gy.y);
            float4 sum = 0;
            float inv = 1.0 / (size + 4);
            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    float2 pos = float2(b.x + ox[i] + 2.5, b.y + oy[j] + 2.5) * inv;
                    sum += tex2D(_SurfaceNormalTex, pos) * (gx[i] * gy[j]);
                }
            }
            return sum;
        }

        struct FerrofluidNormalCacheVaryings
        {
            float4 vertex : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        FerrofluidNormalCacheVaryings FerrofluidNormalCacheVertex(uint vertexId : SV_VertexID)
        {
            FerrofluidNormalCacheVaryings output;
            float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
            output.uv = uv;
            float2 clip = uv * 2 - 1;
            #if UNITY_UV_STARTS_AT_TOP
            clip.y = -clip.y;
            #endif
            output.vertex = float4(clip, 0, 1);
            return output;
        }

        float4 FerrofluidNormalCacheFragment(FerrofluidNormalCacheVaryings input) : SV_Target
        {
            float size = _SurfaceNormalLogicalSize;
            float2 queryUv = (input.uv * (size + 4) - 2) / size;
            FerrofluidSurfaceInput surfaceInput = (FerrofluidSurfaceInput)0;
            surfaceInput.uv = SurfaceHeightUv(queryUv);
            FerrofluidSurfaceVaryings exact = FerrofluidSurfaceVertex(surfaceInput);
            return float4(exact.worldNormal, exact.spikeHeight);
        }

        float4 FerrofluidSurfaceFragment(FerrofluidSurfaceVaryings input, fixed facing : VFACE) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            #if defined(FERROFLUID_CACHED_NORMALS)
            float4 cachedNormal = FerrofluidSampleNormalCache(input.surfaceUv);
            input.worldNormal = cachedNormal.rgb;
            input.spikeHeight = cachedNormal.a;
            #endif
            // Each eye needs its own view direction for reflections and thin-film highlights.
            #if defined(USING_STEREO_MATRICES)
            float3 cameraPosition = unity_StereoWorldSpaceCameraPos[unity_StereoEyeIndex];
            #else
            float3 cameraPosition = _WorldSpaceCameraPos.xyz;
            #endif
            return FerrofluidShadeSurface(input, facing, cameraPosition);
        }

        struct FerrofluidShadingInput
        {
            float4 vertex : POSITION;
            float3 normal : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        FerrofluidSurfaceVaryings FerrofluidShadingValidationVertex(FerrofluidShadingInput input)
        {
            UNITY_SETUP_INSTANCE_ID(input);
            FerrofluidSurfaceVaryings output;
            UNITY_INITIALIZE_OUTPUT(FerrofluidSurfaceVaryings, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.vertex = input.vertex;
            output.worldPosition = _ValidationWorldOffset;
            output.worldNormal = input.normal;
            output.spikeHeight = input.uv.y;
            return output;
        }

        float4 FerrofluidShadingValidationFragment(FerrofluidSurfaceVaryings input) : SV_Target
        {
            // Raw linear reflection for the marker-value oracle; display gamma would corrupt the
            // constant slice colors the GPU fixtures compare.
            if (_ValidationOutput > 3.5)
                return float4(FerrofluidSampleViewReflection(_ValidationWorldPosition, normalize(_ValidationCameraPosition - _ValidationWorldPosition), normalize(input.worldNormal)), 1.0);

            if (_ValidationOutput > 1.5)
            {
                // The raw reflection seam reports display gamma like the legacy callers.
                return float4(FerrofluidRoomColor(FerrofluidSampleViewReflection(_ValidationWorldPosition, normalize(_ValidationCameraPosition - input.worldPosition), normalize(input.worldNormal))), 1.0);
            }

            return FerrofluidShadeSurface(input, 1.0, _ValidationCameraPosition);
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
            #pragma multi_compile_local _ FERROFLUID_CACHED_NORMALS
            #pragma multi_compile _ FERROFLUID_ROOM_REFLECTIONS FERROFLUID_NO_REFLECTIONS
            #pragma multi_compile _ FERROFLUID_ENVIRONMENT_CAPTURE
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

        // Only the native validation harness draws this pass via an explicit pass index. A custom
        // LightMode keeps it out of the game's normal rendering.
        Pass
        {
            Name "FERROFLUID_WORLD_POSITION"
            Tags { "LightMode"="FerrofluidWorldPosition" }
            Cull Off
            ZWrite Off
            ZTest Always

            CGPROGRAM
            #pragma vertex FerrofluidWorldPositionVertex
            #pragma fragment FerrofluidWorldPositionFragment
            #pragma multi_compile_local _ FERROFLUID_CACHED_DEFORMATION
            ENDCG
        }

        Pass
        {
            Name "FERROFLUID_SHADING_VALIDATION"
            Tags { "LightMode"="FerrofluidShadingValidation" }
            Cull Off
            ZWrite Off
            ZTest Always

            CGPROGRAM
            #pragma vertex FerrofluidShadingValidationVertex
            #pragma fragment FerrofluidShadingValidationFragment
            #pragma multi_compile _ FERROFLUID_ROOM_REFLECTIONS FERROFLUID_NO_REFLECTIONS
            #pragma multi_compile _ FERROFLUID_ENVIRONMENT_CAPTURE
            ENDCG
        }

        // Only the floor shadow mask command buffer draws this pass via an explicit pass
        // index. A custom LightMode keeps it out of the game's normal rendering, and the
        // offscreen mask target stays mono with no instancing variants.
        Pass
        {
            Name "FERROFLUID_SHADOW_MASK"
            Tags { "LightMode" = "FerrofluidShadowMask" }
            Cull Off
            ZWrite Off
            ZTest Always
            ColorMask R
            Blend Off

            CGPROGRAM
            #pragma vertex FerrofluidShadowMaskVertex
            #pragma fragment FerrofluidShadowMaskFragment
            #pragma multi_compile_local _ FERROFLUID_CACHED_DEFORMATION
            ENDCG
        }

        // Bakes the continuous field normal + film into a padded cache texture. Driven by an
        // explicit DrawProcedural call only; never part of the camera's normal rendering.
        Pass
        {
            Name "FERROFLUID_SURFACE_NORMAL_CACHE"
            Tags { "LightMode"="FerrofluidSurfaceNormalCache" }
            Cull Off
            ZWrite Off
            ZTest Always

            CGPROGRAM
            #pragma vertex FerrofluidNormalCacheVertex
            #pragma fragment FerrofluidNormalCacheFragment
            #pragma multi_compile_local _ FERROFLUID_CACHED_DEFORMATION
            ENDCG
        }
    }

    Fallback Off
}
