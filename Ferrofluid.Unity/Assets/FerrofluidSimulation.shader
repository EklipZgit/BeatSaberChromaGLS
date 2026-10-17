Shader "Hidden/ChromaGLS/FerrofluidSimulation"
{
    Properties
    {
        [HideInInspector] _StateTex ("State", 2D) = "black" {}
        [HideInInspector] _RestPositionTex ("Rest Position", 2D) = "black" {}
        [HideInInspector] _DensityPressureTex ("Density Pressure", 2D) = "black" {}
        [HideInInspector] _ForceTex ("Force", 2D) = "black" {}
        [HideInInspector] _CoarseCandidateTex ("Coarse Height Candidates", 2D) = "black" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "FerrofluidCommon.cginc"
    #pragma target 5.0

    #ifndef FERROFLUID_BRUTE_FORCE_HEIGHT_MAP
    #define FERROFLUID_BRUTE_FORCE_HEIGHT_MAP 0
    #endif

    sampler2D _StateTex;
    sampler2D _RestPositionTex;
    sampler2D _DensityPressureTex;
    sampler2D _ForceTex;
    sampler2D _CoarseCandidateTex;
    float4 _CoarseCandidateTex_TexelSize;
    float _CoarseCandidateSize;
    int _ParticleCount;
    float _H;
    float _HSQ;
    float _Mass;
    float _RestDensity;
    float _GasConstant;
    float _Viscosity;
    float _Poly6;
    float _SpikyGradient;
    float _ViscosityLaplacian;
    float2 _DomainScale;
    float2 _PointerPosition;
    float _PointerStrength;
    float3 _PrimitiveMagnetPosition;
    float3 _PrimitiveHandPosition;
    float _SecondaryMagneticFieldStrength;
    float2 _SecondaryPointerPosition;
    float _PointerRadius;
    float _MaximumSlide;
    float _SlideFraction;
    float _SlideFalloffExponent;
    float _Elasticity;
    float _Damping;
    float _DeltaTimeMilliseconds;
    float _HeightFactor;
    float _HeightMapScale;
    float _SmoothFactor;
    float _SpikeFactor;
    float _SpikeBaseCutoff;
    float4 _PerimeterSegments;
    float _CornerRadius;
    float _RadialRadius;
    float _SurfaceShapeMode;
    float _PrimitiveSpikeSize;
    float _PrimitiveTipRoundness;
    float _PrimitiveTipCapRounding;
    float _PrimitiveSpikeSpacing;
    // Surface distances keep spike footprints round where the primitive UV map stretches.
    sampler2D _SiteSurfaceTex;
    sampler2D _RestSurfaceTex;
    float2 _PrimitiveYawCosSin;
    float _SurfaceRoundness;
    float _PrimitiveCellMetric;

    // Keep particle UVs independent of Graphics.Blit's source-texture flipping.
    struct FerrofluidBlitInput
    {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
    };

    struct FerrofluidBlitVaryings
    {
        float4 vertex : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    FerrofluidBlitVaryings FerrofluidBlitVertex(FerrofluidBlitInput input)
    {
        FerrofluidBlitVaryings output;
        output.vertex = UnityObjectToClipPos(input.vertex);
        output.uv = input.uv;
        return output;
    }

    // Sample individual particles without filtering or mipmaps.
    float4 LoadFerrofluidTexel(sampler2D textureSampler, int index)
    {
        return tex2Dlod(textureSampler, float4(FerrofluidParticleUv(index), 0.0, 0.0));
    }



    // Springs need the nearest folded copy even outside the pressure kernel, or their pull jumps at its boundary.
    float2 FerrofluidPairDelta(float2 from, float2 to, out float2 velocityMirror)
    {
        velocityMirror = float2(1.0, 1.0);
        if (_SurfaceShapeMode < 0.5)
        {
            return WrapFerrofluidDelta(to - from);
        }

        float2 best = to - from;
        float bestLength = dot(best, best);
        float edgeX = from.x + to.x >= 0.0 ? 1.0 : -1.0;
        float2 candidate = float2((2.0 * edgeX) - to.x, -to.y) - from;
        float candidateLength = dot(candidate, candidate);
        if (candidateLength < bestLength)
        {
            best = candidate;
            bestLength = candidateLength;
            velocityMirror = float2(-1.0, -1.0);
        }

        float edgeY = from.y + to.y >= 0.0 ? 1.0 : -1.0;
        candidate = float2(-to.x, (2.0 * edgeY) - to.y) - from;
        candidateLength = dot(candidate, candidate);
        if (candidateLength < bestLength)
        {
            best = candidate;
            bestLength = candidateLength;
            velocityMirror = float2(-1.0, -1.0);
        }

        float2 corner = float2(from.x - to.x >= 0.0 ? 1.0 : -1.0, from.y - to.y >= 0.0 ? 1.0 : -1.0);
        candidate = (2.0 * corner) + to - from;
        candidateLength = dot(candidate, candidate);
        if (candidateLength < bestLength)
        {
            best = candidate;
            velocityMirror = float2(1.0, 1.0);
        }

        return best;
    }

    float2 FerrofluidPairDelta(float2 from, float2 to)
    {
        float2 velocityMirror;
        return FerrofluidPairDelta(from, to, velocityMirror);
    }

    // Mirror position and velocity across primitive UV edges so particles can cross the seam.
    // Two passes handle crossings through a corner.
    float2 FerrofluidChartFold(float2 position, inout float2 velocity)
    {
        [unroll]
        for (int foldPass = 0; foldPass < 2; foldPass++)
        {
            if (position.x > 1.0)
            {
                position = float2(2.0 - position.x, -position.y);
                velocity = -velocity;
            }
            else if (position.x < -1.0)
            {
                position = float2(-2.0 - position.x, -position.y);
                velocity = -velocity;
            }

            if (position.y > 1.0)
            {
                position = float2(-position.x, 2.0 - position.y);
                velocity = -velocity;
            }
            else if (position.y < -1.0)
            {
                position = float2(-position.x, -2.0 - position.y);
                velocity = -velocity;
            }
        }

        return position;
    }

    float4 FerrofluidPressureFragment(FerrofluidBlitVaryings input) : SV_Target
    {
        int particleId = FerrofluidParticleId(input.uv);
        if (particleId >= _ParticleCount)
        {
            return float4(1.0, 0.0, 0.0, 0.0);
        }

        float2 position = LoadFerrofluidTexel(_StateTex, particleId).xy;
        float selfKernel = _Poly6 * _HSQ * _HSQ * _HSQ;
        float density = _Mass * selfKernel;
        [loop]
        for (int neighborId = 0; neighborId < _ParticleCount; neighborId++)
        {
            if (neighborId == particleId)
            {
                continue;
            }

            float2 neighborPosition = LoadFerrofluidTexel(_StateTex, neighborId).xy;
            float2 physicalDelta = FerrofluidPairDelta(position, neighborPosition) * _DomainScale;
            float radiusSquared = dot(physicalDelta, physicalDelta);
            if (radiusSquared < _HSQ)
            {
                float remainder = max(0.0, _HSQ - radiusSquared);
                density += _Mass * _Poly6 * remainder * remainder * remainder;
            }
        }

        float pressure = max(_GasConstant * (density - _RestDensity), 0.0);
        return float4(density, pressure, 0.0, 0.0);
    }

    float4 FerrofluidForceFragment(FerrofluidBlitVaryings input) : SV_Target
    {
        int particleId = FerrofluidParticleId(input.uv);
        if (particleId >= _ParticleCount)
        {
            return 0.0;
        }

        float4 state = LoadFerrofluidTexel(_StateTex, particleId);
        float2 densityPressure = LoadFerrofluidTexel(_DensityPressureTex, particleId).xy;
        float2 physicalForce = 0.0;
        [loop]
        for (int neighborId = 0; neighborId < _ParticleCount; neighborId++)
        {
            if (neighborId == particleId)
            {
                continue;
            }

            float4 neighborState = LoadFerrofluidTexel(_StateTex, neighborId);
            float2 velocityMirror;
            float2 physicalDelta = FerrofluidPairDelta(state.xy, neighborState.xy, velocityMirror) * _DomainScale;
            float radiusSquared = dot(physicalDelta, physicalDelta);
            if (radiusSquared >= _HSQ || radiusSquared < 0.0000000001)
            {
                continue;
            }

            float radius = sqrt(radiusSquared);
            float2 neighborDensityPressure = LoadFerrofluidTexel(_DensityPressureTex, neighborId).xy;
            float neighborDensity = max(neighborDensityPressure.x, 0.000000001);
            float kernelRemainder = max(0.0, _H - radius);
            float pressureWeight = (_SpikyGradient * kernelRemainder * kernelRemainder) / radius;
            float pressureScale = _Mass * ((densityPressure.y + neighborDensityPressure.y) / (2.0 * neighborDensity)) * pressureWeight;
            float2 pressureForce = physicalDelta * pressureScale;
            float viscosityWeight = _ViscosityLaplacian * (1.0 - (radius / _H));
            // Mirror the neighbor's velocity too, or viscosity treats the seam as a sudden change in velocity.
            float2 physicalVelocityDelta = ((neighborState.zw * velocityMirror) - state.zw) * _DomainScale;
            float2 viscosityForce = physicalVelocityDelta * (_Viscosity * _Mass * viscosityWeight / neighborDensity);
            physicalForce += pressureForce + viscosityForce;
        }

        return float4(physicalForce / _DomainScale, 0.0, 0.0);
    }

    float2 FerrofluidChartSlide(float2 restPosition, float2 pointerPosition, float strength)
    {
        float2 delta = FerrofluidPairDelta(restPosition, pointerPosition) * _DomainScale;
        float distance = length(delta);
        float profile = strength * _SlideFraction
            * pow(max(0.0, 1.0 - (distance / _PointerRadius)), _SlideFalloffExponent);
        return delta / max(distance, 0.000001) * min(distance, _MaximumSlide * profile);
    }

    // Measure attraction on the surface so the pointer cannot switch between folded UV copies.
    float3 FerrofluidPrimitiveSlide(float3 home, float3 normal, float spacing, float3 magnet, float strength)
    {
        float3 delta = magnet - home;
        float distance = length(delta);
        float physicalScale = max(spacing / 0.64, 0.0001);
        float fieldDistance = distance / physicalScale;
        float slideProfile = strength * _SlideFraction
            * pow(max(0.0, 1.0 - (fieldDistance / _PointerRadius)), _SlideFalloffExponent);
        float3 tangent = delta - (normal * dot(delta, normal));
        float tangentLength = length(tangent);
        return tangent / max(tangentLength, 0.000001)
            * min(tangentLength, _MaximumSlide * slideProfile * physicalScale);
    }

    float4 FerrofluidIntegrateFragment(FerrofluidBlitVaryings input) : SV_Target
    {
        int particleId = FerrofluidParticleId(input.uv);
        float4 state = LoadFerrofluidTexel(_StateTex, particleId);
        if (particleId >= _ParticleCount)
        {
            return float4(state.xy, 0.0, 0.0);
        }

        float deltaTime = _DeltaTimeMilliseconds * 0.001;
        float density = LoadFerrofluidTexel(_DensityPressureTex, particleId).x + 0.000000001;
        float2 acceleration = LoadFerrofluidTexel(_ForceTex, particleId).xy / density;
        float2 velocity = state.zw + (acceleration * deltaTime);
        float2 restPosition = LoadFerrofluidTexel(_RestPositionTex, particleId).xy;
        float2 targetPosition;
        if (_SurfaceShapeMode < 0.5)
        {
            float2 physicalTargetOffset = FerrofluidChartSlide(restPosition, _PointerPosition, _PointerStrength);
            physicalTargetOffset += FerrofluidChartSlide(restPosition, _SecondaryPointerPosition, _SecondaryMagneticFieldStrength);
            targetPosition = restPosition + (physicalTargetOffset / _DomainScale);
        }
        else
        {
            float3 home = FerrofluidPrimitiveBasePosition((restPosition * 0.5) + 0.5, _SurfaceShapeMode, _SurfaceRoundness, _PrimitiveYawCosSin);
            float3 normal = _SurfaceShapeMode > 1.5
                ? normalize(home)
                : normalize(sign(home) * pow(abs(home), lerp(8.0, 2.0, saturate(_SurfaceRoundness)) - 1.0));
            float spacing = LoadFerrofluidTexel(_SiteSurfaceTex, particleId).w;
            float3 slide = FerrofluidPrimitiveSlide(home, normal, spacing, _PrimitiveMagnetPosition, _PointerStrength)
                + FerrofluidPrimitiveSlide(home, normal, spacing, _PrimitiveHandPosition, _SecondaryMagneticFieldStrength);
            targetPosition = (FerrofluidPrimitiveSurfaceUv(home + slide, _PrimitiveYawCosSin) * 2.0) - 1.0;
        }

        float2 targetDelta = FerrofluidPairDelta(state.xy, targetPosition);
        velocity += targetDelta * _Elasticity * deltaTime;
        velocity *= exp(-_Damping * deltaTime);
        float2 position = state.xy + ((velocity + (0.5 * acceleration * deltaTime)) * deltaTime);
        position = _SurfaceShapeMode > 0.5 ? FerrofluidChartFold(position, velocity) : WrapFerrofluidDelta(position);
        return float4(position, velocity);
    }

    // Store live surface positions with rest-neighbor spacing so spike widths stay consistent across UV folds.
    float3 FerrofluidSiteRestSurfacePosition(int particleId)
    {
        float2 restPosition = LoadFerrofluidTexel(_RestPositionTex, particleId).xy;
        return FerrofluidPrimitiveBasePosition(
            (restPosition * 0.5) + 0.5,
            _SurfaceShapeMode,
            _SurfaceRoundness,
            _PrimitiveYawCosSin);
    }

    float4 FerrofluidRestSurfaceFragment(FerrofluidBlitVaryings input) : SV_Target
    {
        int particleId = FerrofluidParticleId(input.uv);
        if (particleId >= _ParticleCount)
        {
            return float4(0.0, 0.0, 0.0, 1.0);
        }

        // Rest spacing changes only with topology or shape settings, so this full neighbor search need not run every tick.
        float3 restSurfacePosition = FerrofluidSiteRestSurfacePosition(particleId);
        if (_SurfaceShapeMode > 0.5 && _SurfaceShapeMode < 1.5)
        {
            return float4(restSurfacePosition, _PrimitiveSpikeSpacing);
        }
        float spacing = 10000000000.0;
        [loop]
        for (int neighborId = 0; neighborId < _ParticleCount; neighborId++)
        {
            if (neighborId == particleId)
            {
                continue;
            }

            spacing = min(spacing, length(FerrofluidSiteRestSurfacePosition(neighborId) - restSurfacePosition));
        }

        return float4(restSurfacePosition, max(spacing, 0.0001));
    }

    float4 FerrofluidSiteSurfaceFragment(FerrofluidBlitVaryings input) : SV_Target
    {
        int particleId = FerrofluidParticleId(input.uv);
        if (particleId >= _ParticleCount)
        {
            return float4(0.0, 0.0, 0.0, 1.0);
        }

        float2 particlePosition = LoadFerrofluidTexel(_StateTex, particleId).xy;
        float3 sitePosition = FerrofluidPrimitiveBasePosition(
            (particlePosition * 0.5) + 0.5,
            _SurfaceShapeMode,
            _SurfaceRoundness,
            _PrimitiveYawCosSin);
        return float4(sitePosition, LoadFerrofluidTexel(_RestSurfaceTex, particleId).w);
    }

    // Candidate selection and the final height pass must use the same distance metric.
    void FerrofluidPrepareHeightMetric(
        float2 uv,
        out float2 position,
        out float2 surfaceMetric,
        out float3 texelSurfacePosition)
    {
        position = uv * 2.0 - 1.0;
        float perimeterDistance = frac(uv.x) * _PerimeterSegments.w;
        float secondCornerStart = _PerimeterSegments.x + _PerimeterSegments.y + _PerimeterSegments.z;
        float thirdCornerStart = secondCornerStart + _PerimeterSegments.y + _PerimeterSegments.x;
        float fourthCornerStart = thirdCornerStart + _PerimeterSegments.y + _PerimeterSegments.z;
        bool isCorner = (perimeterDistance >= _PerimeterSegments.x && perimeterDistance < (_PerimeterSegments.x + _PerimeterSegments.y))
            || (perimeterDistance >= secondCornerStart && perimeterDistance < (secondCornerStart + _PerimeterSegments.y))
            || (perimeterDistance >= thirdCornerStart && perimeterDistance < (thirdCornerStart + _PerimeterSegments.y))
            || perimeterDistance >= fourthCornerStart;
        float radialOffset = _RadialRadius * cos(uv.y * 6.28318530718);
        float cornerStretch = (_CornerRadius + radialOffset) / _CornerRadius;

        surfaceMetric = float2(3.7 * (isCorner ? cornerStretch : 1.0), 1.0);
        texelSurfacePosition = _SurfaceShapeMode > 0.5
            ? FerrofluidPrimitiveBasePosition(uv, _SurfaceShapeMode, _SurfaceRoundness, _PrimitiveYawCosSin)
            : float3(0.0, 0.0, 0.0);
    }

    float FerrofluidHeightDistance(
        int particleId,
        float2 position,
        float2 surfaceMetric,
        float3 texelSurfacePosition)
    {
        float2 particlePosition = LoadFerrofluidTexel(_StateTex, particleId).xy;
        if (_SurfaceShapeMode < 0.5)
        {
            float2 wrappedDelta = WrapFerrofluidDelta(particlePosition - position);
            return length(wrappedDelta * surfaceMetric) * _HeightMapScale;
        }

        float4 siteSurface = LoadFerrofluidTexel(_SiteSurfaceTex, particleId);
        return (length(texelSurfacePosition - siteSurface.xyz) / max(siteSurface.w, 0.0001))
            * _PrimitiveCellMetric
            * _HeightMapScale;
    }

    // Store sixteen candidates and distance bounds for omitted sites so crowded cells can fall back to a full scan.
    float4 FerrofluidPrimitiveCandidates(float2 uv)
    {
        int2 pixel = (int2)floor(uv * _CoarseCandidateTex_TexelSize.zw);
        int2 cell = pixel / int2(3, 2);
        int2 block = pixel - (cell * int2(3, 2));
        int blockId = block.x + (block.y * 3);
        float2 centerUv = (cell + 0.5) / _CoarseCandidateSize;
        float2 position;
        float2 metric;
        float3 surface;
        FerrofluidPrepareHeightMetric(centerUv, position, metric, surface);
        float distances[17];
        int ids[17];
        [unroll]
        for (int rank = 0; rank < 17; rank++)
        {
            distances[rank] = 10000000000.0;
            ids[rank] = -1;
        }

        float minimumSpacing = 10000000000.0;
        [loop]
        for (int particleId = 0; particleId < _ParticleCount; particleId++)
        {
            float4 site = LoadFerrofluidTexel(_SiteSurfaceTex, particleId);
            minimumSpacing = min(minimumSpacing, max(site.w, 0.0001));
            float distance = length(surface - site.xyz) / max(site.w, 0.0001) * _PrimitiveCellMetric * _HeightMapScale;
            if (distance >= distances[16])
            {
                continue;
            }

            int destination = 16;
            [loop]
            while (destination > 0 && distance < distances[destination - 1])
            {
                distances[destination] = distances[destination - 1];
                ids[destination] = ids[destination - 1];
                destination--;
            }

            distances[destination] = distance;
            ids[destination] = particleId;
        }

        if (blockId >= 4)
        {
            return float4(distances[16], minimumSpacing, 0.0, 0.0);
        }

        int first = blockId * 4;
        return float4(ids[first], ids[first + 1], ids[first + 2], ids[first + 3]);
    }

    // The torus height pass gathers these four candidates from this cell and its eight neighbors.
    float4 FerrofluidCoarseCandidateFragment(FerrofluidBlitVaryings input) : SV_Target
    {
        if (_SurfaceShapeMode > 0.5)
        {
            return FerrofluidPrimitiveCandidates(input.uv);
        }

        float2 position;
        float2 surfaceMetric;
        float3 texelSurfacePosition;
        FerrofluidPrepareHeightMetric(input.uv, position, surfaceMetric, texelSurfacePosition);

        float4 candidateIds = -1.0;
        float4 candidateDistances = 10000000000.0;
        [loop]
        for (int particleId = 0; particleId < _ParticleCount; particleId++)
        {
            float distanceToParticle = FerrofluidHeightDistance(
                particleId,
                position,
                surfaceMetric,
                texelSurfacePosition);

            // Break distance ties by particle ID to match the full scan.
            if (distanceToParticle < candidateDistances.x)
            {
                candidateDistances.w = candidateDistances.z;
                candidateDistances.z = candidateDistances.y;
                candidateDistances.y = candidateDistances.x;
                candidateDistances.x = distanceToParticle;
                candidateIds.w = candidateIds.z;
                candidateIds.z = candidateIds.y;
                candidateIds.y = candidateIds.x;
                candidateIds.x = particleId;
            }
            else if (distanceToParticle < candidateDistances.y)
            {
                candidateDistances.w = candidateDistances.z;
                candidateDistances.z = candidateDistances.y;
                candidateDistances.y = distanceToParticle;
                candidateIds.w = candidateIds.z;
                candidateIds.z = candidateIds.y;
                candidateIds.y = particleId;
            }
            else if (distanceToParticle < candidateDistances.z)
            {
                candidateDistances.w = candidateDistances.z;
                candidateDistances.z = distanceToParticle;
                candidateIds.w = candidateIds.z;
                candidateIds.z = particleId;
            }
            else if (distanceToParticle < candidateDistances.w)
            {
                candidateDistances.w = distanceToParticle;
                candidateIds.w = particleId;
            }
        }

        return candidateIds;
    }

    void FerrofluidAccumulateHeightParticle(
        int particleId,
        float2 position,
        float2 surfaceMetric,
        float3 texelSurfacePosition,
        inout float resultDistance,
        inout float nearestDistance,
        inout float secondNearestDistance,
        inout float2 nearestSiteParticleUv)
    {
        float distanceToParticle = FerrofluidHeightDistance(
            particleId,
            position,
            surfaceMetric,
            texelSurfacePosition);
        if (distanceToParticle < nearestDistance)
        {
            secondNearestDistance = nearestDistance;
            nearestDistance = distanceToParticle;
            // Store particle UVs, not surface UVs, so spring history follows a particle through folds and cell boundaries.
            nearestSiteParticleUv = FerrofluidParticleUv(particleId);
        }
        else if (distanceToParticle < secondNearestDistance)
        {
            secondNearestDistance = distanceToParticle;
        }

        float blend = smoothstep(-1.0, 1.0, (resultDistance - distanceToParticle) / _SmoothFactor);
        resultDistance = lerp(resultDistance, distanceToParticle, blend)
            - (blend * (1.0 - blend) * (_SmoothFactor / (1.0 + (3.0 * _SmoothFactor))));
    }

    float4 FerrofluidHeightMapFragment(FerrofluidBlitVaryings input) : SV_Target
    {
        float2 position;
        float2 surfaceMetric;
        float3 texelSurfacePosition;
        FerrofluidPrepareHeightMetric(input.uv, position, surfaceMetric, texelSurfacePosition);

        float resultDistance = 1.0;
        float nearestDistance = 10000000000.0;
        float secondNearestDistance = 10000000000.0;
        float2 nearestSiteParticleUv = 0.0;

    #if FERROFLUID_BRUTE_FORCE_HEIGHT_MAP
        [loop]
        for (int particleId = 0; particleId < _ParticleCount; particleId++)
        {
            FerrofluidAccumulateHeightParticle(
                particleId,
                position,
                surfaceMetric,
                texelSurfacePosition,
                resultDistance,
                nearestDistance,
                secondNearestDistance,
                nearestSiteParticleUv);
        }
    #else
        if (_SurfaceShapeMode > 0.5)
        {
            int2 cell = min((int2)floor(input.uv * _CoarseCandidateSize), (int)_CoarseCandidateSize - 1);
            int primitiveIds[16];
            [unroll]
            for (int block = 0; block < 4; block++)
            {
                int2 blockPixel = (cell * int2(3, 2)) + int2(block % 3, block / 3);
                float4 ids = tex2Dlod(_CoarseCandidateTex, float4((blockPixel + 0.5) * _CoarseCandidateTex_TexelSize.xy, 0.0, 0.0));
                [unroll]
                for (int channel = 0; channel < 4; channel++)
                {
                    primitiveIds[(block * 4) + channel] = (int)round(ids[channel]);
                }
            }

            [loop]
            for (int rank = 1; rank < 16; rank++)
            {
                int id = primitiveIds[rank];
                int destination = rank;
                [loop]
                while (destination > 0 && id < primitiveIds[destination - 1])
                {
                    primitiveIds[destination] = primitiveIds[destination - 1];
                    destination--;
                }

                primitiveIds[destination] = id;
            }

            [unroll]
            for (int rank = 0; rank < 16; rank++)
            {
                int id = primitiveIds[rank];
                if (id >= 0)
                {
                    FerrofluidAccumulateHeightParticle(id, position, surfaceMetric, texelSurfacePosition, resultDistance, nearestDistance, secondNearestDistance, nearestSiteParticleUv);
                }
            }

            int2 boundPixel = (cell * int2(3, 2)) + int2(1, 1);
            float2 bounds = tex2Dlod(_CoarseCandidateTex, float4((boundPixel + 0.5) * _CoarseCandidateTex_TexelSize.xy, 0.0, 0.0)).xy;
            float3 center = FerrofluidPrimitiveBasePosition((cell + 0.5) / _CoarseCandidateSize, _SurfaceShapeMode, _SurfaceRoundness, _PrimitiveYawCosSin);
            float omittedLowerBound = bounds.x - (length(texelSurfacePosition - center) / bounds.y * _PrimitiveCellMetric * _HeightMapScale);
            // The pairwise smooth minimum stays within one smoothing width of its nearest input.
            // Two widths ensure that omitted sites cannot alter an earlier accumulation before the nearest site replaces it.
            if (omittedLowerBound <= secondNearestDistance + (2.0 * _SmoothFactor))
            {
                resultDistance = 1.0;
                nearestDistance = 10000000000.0;
                secondNearestDistance = 10000000000.0;
                [loop]
                for (int particleId = 0; particleId < _ParticleCount; particleId++)
                {
                    FerrofluidAccumulateHeightParticle(particleId, position, surfaceMetric, texelSurfacePosition, resultDistance, nearestDistance, secondNearestDistance, nearestSiteParticleUv);
                }
            }
        }
        else
        {
            int candidateIds[36];
            int2 coarseCell = (int2)floor(input.uv * _CoarseCandidateSize);
            int coarseSize = (int)_CoarseCandidateSize;
            [unroll]
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                [unroll]
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    int2 sampleCell = coarseCell + int2(offsetX, offsetY);
                    if (_SurfaceShapeMode < 0.5)
                    {
                        sampleCell.x = sampleCell.x < 0
                            ? sampleCell.x + coarseSize
                            : sampleCell.x >= coarseSize
                                ? sampleCell.x - coarseSize
                                : sampleCell.x;
                        sampleCell.y = sampleCell.y < 0
                            ? sampleCell.y + coarseSize
                            : sampleCell.y >= coarseSize
                                ? sampleCell.y - coarseSize
                                : sampleCell.y;
                    }
                    else
                    {
                        // Mirror neighbor cells when crossing a primitive UV edge.
                        if (sampleCell.x < 0 || sampleCell.x >= coarseSize)
                        {
                            sampleCell.x = sampleCell.x < 0 ? -sampleCell.x - 1 : (2 * coarseSize) - sampleCell.x - 1;
                            sampleCell.y = coarseSize - sampleCell.y - 1;
                        }

                        if (sampleCell.y < 0 || sampleCell.y >= coarseSize)
                        {
                            sampleCell.y = sampleCell.y < 0 ? -sampleCell.y - 1 : (2 * coarseSize) - sampleCell.y - 1;
                            sampleCell.x = coarseSize - sampleCell.x - 1;
                        }
                    }

                    float2 candidateUv = (sampleCell + 0.5) / _CoarseCandidateSize;
                    float4 cellCandidates = tex2Dlod(_CoarseCandidateTex, float4(candidateUv, 0.0, 0.0));
                    [unroll]
                    for (int channel = 0; channel < 4; channel++)
                    {
                        int candidateIndex = ((((offsetY + 1) * 3) + (offsetX + 1)) * 4) + channel;
                        int particleId = (int)round(cellCandidates[channel]);
                        candidateIds[candidateIndex] = particleId >= 0 && particleId < _ParticleCount
                            ? particleId
                            : 2147483647;
                    }
                }
            }

            // Match the full scan's accumulation order. Skip duplicate IDs below.
            [loop]
            for (int sortIndex = 1; sortIndex < 36; sortIndex++)
            {
                int candidate = candidateIds[sortIndex];
                int destination = sortIndex;
                [loop]
                while (destination > 0 && candidateIds[destination - 1] > candidate)
                {
                    candidateIds[destination] = candidateIds[destination - 1];
                    destination--;
                }

                candidateIds[destination] = candidate;
            }

            int previousParticleId = -1;
            [loop]
            for (int candidateIndex = 0; candidateIndex < 36; candidateIndex++)
            {
                int particleId = candidateIds[candidateIndex];
                if (particleId >= _ParticleCount)
                {
                    break;
                }

                if (particleId == previousParticleId)
                {
                    continue;
                }

                previousParticleId = particleId;
                FerrofluidAccumulateHeightParticle(
                    particleId,
                    position,
                    surfaceMetric,
                    texelSurfacePosition,
                    resultDistance,
                    nearestDistance,
                    secondNearestDistance,
                    nearestSiteParticleUv);
            }
        }
    #endif

        resultDistance = saturate(resultDistance * _SpikeFactor);

        // A wide cap makes tall primitive spikes look bulbous.
        float tipCapRounding = _SurfaceShapeMode < 0.5 ? 1.0 : _PrimitiveTipCapRounding;
        if (tipCapRounding > 0.0)
        {
            resultDistance = FerrofluidAlmostIdentity(
                resultDistance,
                _SurfaceShapeMode < 0.5 ? 0.3 : 0.12 * tipCapRounding,
                _SurfaceShapeMode < 0.5 ? 0.05 : 0.02 * tipCapRounding);
        }

        float coneHeight = 1.0 - resultDistance;
        coneHeight = coneHeight * coneHeight * (3.0 - (2.0 * coneHeight));
        float roundedHeight = sqrt(max(0.0, 1.0 - (resultDistance * resultDistance)));
        float spikeHeight = lerp(coneHeight, roundedHeight, _SurfaceShapeMode < 0.5 ? 0.2 : _PrimitiveTipRoundness);
        float primitiveTipRoundness = _SurfaceShapeMode < 0.5 ? 0.0 : _PrimitiveTipRoundness;
        float sharpConeHeight = lerp(saturate((1.0 - resultDistance) / lerp(1.0, 0.95, tipCapRounding)), roundedHeight, primitiveTipRoundness);

        // Zero the cell boundary so bilinear filtering cannot bridge neighboring spikes.
        float boundaryVisibility = _SpikeBaseCutoff > 0.0
            ? smoothstep(0.0, _SpikeBaseCutoff, secondNearestDistance - nearestDistance)
            : 1.0;
        spikeHeight *= boundaryVisibility;
        sharpConeHeight *= boundaryVisibility;
        return float4(
            spikeHeight * _HeightFactor,
            nearestSiteParticleUv,
            sharpConeHeight * _HeightFactor);
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "PRESSURE"
            CGPROGRAM
            #pragma vertex FerrofluidBlitVertex
            #pragma fragment FerrofluidPressureFragment
            ENDCG
        }

        Pass
        {
            Name "FORCE"
            CGPROGRAM
            #pragma vertex FerrofluidBlitVertex
            #pragma fragment FerrofluidForceFragment
            ENDCG
        }

        Pass
        {
            Name "INTEGRATE"
            CGPROGRAM
            #pragma vertex FerrofluidBlitVertex
            #pragma fragment FerrofluidIntegrateFragment
            ENDCG
        }

        Pass
        {
            Name "HEIGHT_MAP"
            CGPROGRAM
            #pragma vertex FerrofluidBlitVertex
            #pragma fragment FerrofluidHeightMapFragment
            #pragma multi_compile_local _ FERROFLUID_BRUTE_FORCE_HEIGHT_MAP
            ENDCG
        }

        Pass
        {
            Name "SITE_SURFACE"
            CGPROGRAM
            #pragma vertex FerrofluidBlitVertex
            #pragma fragment FerrofluidSiteSurfaceFragment
            ENDCG
        }

        Pass
        {
            Name "COARSE_HEIGHT_CANDIDATES"
            CGPROGRAM
            #pragma vertex FerrofluidBlitVertex
            #pragma fragment FerrofluidCoarseCandidateFragment
            ENDCG
        }

        Pass
        {
            Name "REST_SURFACE"
            CGPROGRAM
            #pragma vertex FerrofluidBlitVertex
            #pragma fragment FerrofluidRestSurfaceFragment
            ENDCG
        }
    }

    Fallback Off
}
