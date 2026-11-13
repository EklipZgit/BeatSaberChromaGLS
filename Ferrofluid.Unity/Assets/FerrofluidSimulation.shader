Shader "Hidden/ChromaGLS/FerrofluidSimulation"
{
    Properties
    {
        [HideInInspector] _StateTex ("State", 2D) = "black" {}
        [HideInInspector] _RestPositionTex ("Rest Position", 2D) = "black" {}
        [HideInInspector] _DensityPressureTex ("Density Pressure", 2D) = "black" {}
        [HideInInspector] _ForceTex ("Force", 2D) = "black" {}
        [HideInInspector] _CoarseCandidateTex ("Coarse Height Candidates", 2D) = "black" {}
        [HideInInspector] _SiteBulgeTex ("Site Density Support", 2D) = "black" {}
        [HideInInspector] _PrimitivePressureSpacing ("Primitive Pressure Spacing", Float) = 0.6272
        [HideInInspector] _PrimitiveHomeElasticityGain ("Primitive Home Elasticity Gain", Float) = 1
        [HideInInspector] _PrimitiveHomeQuadraticGain ("Primitive Home Quadratic Gain", Float) = 65536
        [HideInInspector] _PrimitiveHomeSoftRange ("Primitive Home Soft Range", Float) = 0.04
        [HideInInspector] _PrimitiveHomeTravelLimit ("Primitive Home Travel Limit", Float) = 0.5
        [HideInInspector] _PrimitiveHomeDampingGain ("Primitive Home Damping Gain", Float) = 1
        [HideInInspector] _PrimitiveRestDensityReady ("Primitive Rest Density Ready", Float) = 0
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
    sampler2D _SiteBulgeTex;
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
    // Grabbed bodies slide site bases across the shape surface in local space.
    float4x4 _PrimitiveFrameRotation;
    float3 _PrimitiveLinearVelocityDelta;
    float3 _PrimitivePreviousAngularVelocity;
    float3 _PrimitiveAngularVelocity;
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
    float _PrimitivePressureSpacing;
    float _PrimitiveHomeElasticityGain;
    float _PrimitiveHomeQuadraticGain;
    float _PrimitiveHomeSoftRange;
    float _PrimitiveHomeTravelLimit;
    float _PrimitiveHomeDampingGain;
    float _PrimitiveRestDensityReady;
    int _PrimitiveBakePinnedCount;
    float _ValidationMode;
    float3 _ValidationLinearVelocity;
    float3 _ValidationAngularVelocity;

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

    void FerrofluidPrimitiveFrame(float2 chart, out float3 position, out float3 normal, out float3 jx, out float3 jy);
    float2 FerrofluidTangentToChart(float3 physical, float3 jx, float3 jy);

    float4 FerrofluidPressureFragment(FerrofluidBlitVaryings input) : SV_Target
    {
        int particleId = FerrofluidParticleId(input.uv);
        if (particleId >= _ParticleCount)
        {
            return float4(1.0, 0.0, 0.0, 0.0);
        }

        float2 position = LoadFerrofluidTexel(_StateTex, particleId).xy;
        float4 surface = LoadFerrofluidTexel(_SiteSurfaceTex, particleId);
        float selfKernel = _Poly6 * _HSQ * _HSQ * _HSQ;
        float density = _Mass * selfKernel;
        [loop]
        for (int neighborId = 0; neighborId < _ParticleCount; neighborId++)
        {
            if (neighborId == particleId)
            {
                continue;
            }

            float radiusSquared;
            if (_SurfaceShapeMode > 0.5)
            {
                float4 neighborSurface = LoadFerrofluidTexel(_SiteSurfaceTex, neighborId);
                float scale = _PrimitivePressureSpacing / max((surface.w + neighborSurface.w) * 0.5, 0.0001);
                float3 delta = (neighborSurface.xyz - surface.xyz) * scale;
                radiusSquared = dot(delta, delta);
            }
            else
            {
                float2 neighborPosition = LoadFerrofluidTexel(_StateTex, neighborId).xy;
                float2 physicalDelta = FerrofluidPairDelta(position, neighborPosition) * _DomainScale;
                radiusSquared = dot(physicalDelta, physicalDelta);
            }
            if (radiusSquared < _HSQ)
            {
                float remainder = max(0.0, _HSQ - radiusSquared);
                density += _Mass * _Poly6 * remainder * remainder * remainder;
            }
        }

        float pressure;
        if (_SurfaceShapeMode > 0.5 && _PrimitiveRestDensityReady > 0.5)
        {
            // The baked density is the site's resting reference. A global threshold below self density cannot produce attraction.
            float restDensity = LoadFerrofluidTexel(_RestPositionTex, particleId).z;
            pressure = _GasConstant * (density - restDensity);
        }
        else
        {
            pressure = max(_GasConstant * (density - _RestDensity), 0.0);
        }
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
        float3 surfaceForce = 0.0;
        float4 surface = LoadFerrofluidTexel(_SiteSurfaceTex, particleId);
        float3 position, normal, jx, jy;
        float scale = _PrimitivePressureSpacing / max(surface.w, 0.0001);
        if (_SurfaceShapeMode > 0.5)
            FerrofluidPrimitiveFrame(state.xy, position, normal, jx, jy);

        [loop]
        for (int neighborId = 0; neighborId < _ParticleCount; neighborId++)
        {
            if (neighborId == particleId)
            {
                continue;
            }

            float3 surfaceDelta = 0.0;
            float2 physicalDelta = 0.0;
            float2 velocityMirror = 1.0;
            float4 neighborState = 0.0;
            float radiusSquared;
            if (_SurfaceShapeMode > 0.5)
            {
                float4 neighborSurface = LoadFerrofluidTexel(_SiteSurfaceTex, neighborId);
                float pairScale = _PrimitivePressureSpacing / max((surface.w + neighborSurface.w) * 0.5, 0.0001);
                surfaceDelta = (neighborSurface.xyz - surface.xyz) * pairScale;
                radiusSquared = dot(surfaceDelta, surfaceDelta);
            }
            else
            {
                neighborState = LoadFerrofluidTexel(_StateTex, neighborId);
                physicalDelta = FerrofluidPairDelta(state.xy, neighborState.xy, velocityMirror) * _DomainScale;
                radiusSquared = dot(physicalDelta, physicalDelta);
            }
            if (radiusSquared >= _HSQ || radiusSquared < 0.0000000001)
            {
                continue;
            }

            float radius = sqrt(radiusSquared);
            float2 neighborDensityPressure = LoadFerrofluidTexel(_DensityPressureTex, neighborId).xy;
            float neighborDensity = max(neighborDensityPressure.x, 0.000000001);
            float kernelRemainder = max(0.0, _H - radius);
            float pressureWeight = (_SpikyGradient * kernelRemainder * kernelRemainder) / radius;
            float pairPressure = (densityPressure.y + neighborDensityPressure.y) * 0.5;
            if (_SurfaceShapeMode > 0.5 && _PrimitiveRestDensityReady > 0.5)
            {
                // Sparse neighborhoods attract stretched pairs, but compressed pairs must still separate rather than form tensile clumps.
                float relativeStretch = (radius / _PrimitivePressureSpacing) - 1.0;
                pairPressure = max(pairPressure, 0.0) + (min(pairPressure, 0.0) * relativeStretch);
            }

            float pressureScale = _Mass * (pairPressure / neighborDensity) * pressureWeight;
            float viscosityWeight = _ViscosityLaplacian * (1.0 - (radius / _H));
            float viscosityScale = _Viscosity * _Mass * viscosityWeight / neighborDensity;
            if (_SurfaceShapeMode > 0.5)
            {
                neighborState = LoadFerrofluidTexel(_StateTex, neighborId);
                float3 neighborPosition, neighborNormal, neighborJx, neighborJy;
                FerrofluidPrimitiveFrame(neighborState.xy, neighborPosition, neighborNormal, neighborJx, neighborJy);
                // Each chart velocity belongs to its own Jacobian, including within a face and across folds.
                float3 surfaceVelocityDelta = (
                    (neighborJx * neighborState.z) + (neighborJy * neighborState.w)
                    - (jx * state.z) - (jy * state.w)) * scale;
                surfaceForce += (surfaceDelta * pressureScale) + (surfaceVelocityDelta * viscosityScale);
            }
            else
            {
                float2 physicalVelocityDelta = ((neighborState.zw * velocityMirror) - state.zw) * _DomainScale;
                physicalForce += (physicalDelta * pressureScale) + (physicalVelocityDelta * viscosityScale);
            }
        }

        if (_SurfaceShapeMode > 0.5)
            return float4(FerrofluidTangentToChart(surfaceForce / scale, jx, jy), 0.0, 0.0);

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

    // Analytic octahedral chart frame: position, surface normal, and the Jacobian columns that map
    // chart velocity to physical tangent velocity (and back through the Gram inverse below).
    void FerrofluidPrimitiveFrame(float2 chart, out float3 position, out float3 normal, out float3 jx, out float3 jy)
    {
        float2 p = saturate((chart * 0.5) + 0.5) * 2.0 - 1.0;
        float sx = p.x >= 0.0 ? 1.0 : -1.0;
        float sy = p.y >= 0.0 ? 1.0 : -1.0;
        float ax = abs(p.x);
        float ay = abs(p.y);
        float3 q;
        float3 qx;
        float3 qy;
        if (1.0 - ax - ay >= 0.0)
        {
            q = float3(p.x, 1.0 - ax - ay, p.y);
            qx = float3(1.0, -sx, 0.0);
            qy = float3(0.0, -sy, 1.0);
        }
        else
        {
            q = float3((1.0 - ay) * sx, 1.0 - ax - ay, (1.0 - ax) * sy);
            qx = float3(0.0, -sx, -sx * sy);
            qy = float3(-sx * sy, -sy, 0.0);
        }

        float2 yaw = _PrimitiveYawCosSin;
        q = float3((q.x * yaw.x) + (q.z * yaw.y), q.y, (q.z * yaw.x) - (q.x * yaw.y));
        qx = float3((qx.x * yaw.x) + (qx.z * yaw.y), qx.y, (qx.z * yaw.x) - (qx.x * yaw.y));
        qy = float3((qy.x * yaw.x) + (qy.z * yaw.y), qy.y, (qy.z * yaw.x) - (qy.x * yaw.y));
        position = FerrofluidPrimitiveBasePosition((chart * 0.5) + 0.5, _SurfaceShapeMode, _SurfaceRoundness, yaw);
        normal = _SurfaceShapeMode > 1.5
            ? normalize(position)
            : normalize(sign(position) * pow(abs(position), lerp(8.0, 2.0, saturate(_SurfaceRoundness)) - 1.0));
        float scale = dot(position, q) / max(dot(q, q), 0.000001);
        float normalQ = max(dot(normal, q), 0.000001);
        jx = scale * (qx - (q * (dot(normal, qx) / normalQ)));
        jy = scale * (qy - (q * (dot(normal, qy) / normalQ)));
    }

    float2 FerrofluidTangentToChart(float3 physical, float3 jx, float3 jy)
    {
        float jxx = dot(jx, jx);
        float jxy = dot(jx, jy);
        float jyy = dot(jy, jy);
        float inverse = 1.0 / max((jxx * jyy) - (jxy * jxy), 0.0000001);
        return float2(
            ((jyy * dot(physical, jx)) - (jxy * dot(physical, jy))) * inverse,
            ((jxx * dot(physical, jy)) - (jxy * dot(physical, jx))) * inverse);
    }

    float4 FerrofluidInitialStateFragment(FerrofluidBlitVaryings input) : SV_Target
    {
        float4 rest = tex2D(_RestPositionTex, input.uv);
        return float4(rest.x, rest.y, 0.0, 0.0);
    }

    // The editor harness writes a physical velocity field and reads force/velocity back without chart units.
    float4 FerrofluidValidationFragment(FerrofluidBlitVaryings input) : SV_Target
    {
        int id = FerrofluidParticleId(input.uv);
        if (id >= _ParticleCount)
            return 0.0;

        float4 state = LoadFerrofluidTexel(_StateTex, id);
        float3 position, normal, jx, jy;
        FerrofluidPrimitiveFrame(state.xy, position, normal, jx, jy);
        if (_ValidationMode < 0.5)
        {
            float3 physical = _ValidationLinearVelocity + cross(_ValidationAngularVelocity, position);
            physical -= normal * dot(physical, normal);
            return float4(state.xy, FerrofluidTangentToChart(physical, jx, jy));
        }

        float2 chart = _ValidationMode < 1.5 ? LoadFerrofluidTexel(_ForceTex, id).xy : state.zw;
        return float4((jx * chart.x) + (jy * chart.y), 1.0);
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
            float3 live;
            float3 liveNormal;
            float3 jx;
            float3 jy;
            FerrofluidPrimitiveFrame(state.xy, live, liveNormal, jx, jy);
            float3 home;
            float3 homeNormal;
            float3 homeJx;
            float3 homeJy;
            FerrofluidPrimitiveFrame(restPosition, home, homeNormal, homeJx, homeJy);
            float spacing = LoadFerrofluidTexel(_SiteSurfaceTex, particleId).w;
            float3 slide = FerrofluidPrimitiveSlide(home, homeNormal, spacing, _PrimitiveMagnetPosition, _PointerStrength)
                + FerrofluidPrimitiveSlide(home, homeNormal, spacing, _PrimitiveHandPosition, _SecondaryMagneticFieldStrength);
            // Body momentum transports the physical tangent velocity: relative-frame rotation,
            // the 0.28 linear delta, and the released angular momentum each carry forward.
            // One body-surface velocity delta couples linear and angular motion at the same gain.
            float3 bodyDelta = _PrimitiveLinearVelocityDelta
                + mul((float3x3)_PrimitiveFrameRotation, cross(_PrimitivePreviousAngularVelocity, live))
                - cross(_PrimitiveAngularVelocity, live);
            float3 u = mul((float3x3)_PrimitiveFrameRotation, (jx * state.z) + (jy * state.w))
                + (0.28 * bodyDelta);
            u -= liveNormal * dot(liveNormal, u);
            velocity = FerrofluidTangentToChart(u, jx, jy) + (acceleration * deltaTime);
            targetPosition = (FerrofluidPrimitiveSurfaceUv(home + slide, _PrimitiveYawCosSin) * 2.0) - 1.0;
            float3 targetSurface = FerrofluidPrimitiveBasePosition((targetPosition * 0.5) + 0.5, _SurfaceShapeMode, _SurfaceRoundness, _PrimitiveYawCosSin);
            // The pull direction is the physical tangent, not a chart delta that deforms under folds.
            float3 homePull = home - live;
            float3 targetOffset = (targetSurface - home) + (homePull * _PrimitiveHomeElasticityGain);
            velocity += FerrofluidTangentToChart(targetOffset, jx, jy) * _Elasticity * deltaTime;
            float3 homeOffset = live - home;
            float homeLimit = _PrimitiveHomeTravelLimit;
            // Keep small shifts soft, then harden quadratically before a face can drain.
            float stretch = max(length(homeOffset) - (homeLimit * _PrimitiveHomeSoftRange), 0.0);
            float extraElasticity = _Elasticity * _PrimitiveHomeElasticityGain * _PrimitiveHomeQuadraticGain * stretch * stretch / (homeLimit * homeLimit);
            float homeImpulse = extraElasticity * deltaTime;
            velocity = (velocity + (FerrofluidTangentToChart(homePull, jx, jy) * homeImpulse)) / (1.0 + (homeImpulse * deltaTime));
            velocity *= exp(-_Damping * _PrimitiveHomeDampingGain * deltaTime);
            float3 physical = (jx * velocity.x) + (jy * velocity.y);
            // A large chart Euler step can overshoot a nearby home. Retract the physical step to the surface.
            float2 position = (FerrofluidPrimitiveSurfaceUv(live + (physical * deltaTime), _PrimitiveYawCosSin) * 2.0) - 1.0;
            float3 nextSurface = FerrofluidPrimitiveBasePosition((position * 0.5) + 0.5, _SurfaceShapeMode, _SurfaceRoundness, _PrimitiveYawCosSin);
            // The magnet target never expands the configured home limit.
            float bound = homeLimit;
            bool capped = length(nextSurface - home) > bound;
            if (capped)
            {
                float low = 0.0;
                float high = 1.0;
                [unroll]
                for (int iteration = 0; iteration < 6; iteration++)
                {
                    float middle = (low + high) * 0.5;
                    float2 candidate = restPosition + (FerrofluidPairDelta(restPosition, position) * middle);
                    float2 foldVelocity = velocity;
                    candidate = FerrofluidChartFold(candidate, foldVelocity);
                    float3 candidateSurface = FerrofluidPrimitiveBasePosition((candidate * 0.5) + 0.5, _SurfaceShapeMode, _SurfaceRoundness, _PrimitiveYawCosSin);
                    if (length(candidateSurface - home) <= bound)
                    {
                        low = middle;
                    }
                    else
                    {
                        high = middle;
                    }
                }

                float2 foldVelocity = velocity;
                position = FerrofluidChartFold(restPosition + (FerrofluidPairDelta(restPosition, position) * low), foldVelocity);
            }

            float3 newSurface;
            float3 newNormal;
            float3 newJx;
            float3 newJy;
            FerrofluidPrimitiveFrame(position, newSurface, newNormal, newJx, newJy);
            // A site sliding across the rotating surface leaves the body's velocity field: the
            // Coriolis term removes the body-surface speed gained by that relative motion.
            physical -= 0.28 * cross(_PrimitiveAngularVelocity, newSurface - live);
            if (capped)
            {
                float3 outward = newSurface - home;
                outward -= newNormal * dot(newNormal, outward);
                float outwardLength = length(outward);
                if (outwardLength > 0.000001)
                {
                    float3 outwardDirection = outward / outwardLength;
                    physical -= outwardDirection * max(0.0, dot(physical, outwardDirection));
                }
            }

            velocity = FerrofluidTangentToChart(physical, newJx, newJy);
            return float4(position, velocity);
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

    float4 FerrofluidBakeIntegrateFragment(FerrofluidBlitVaryings input) : SV_Target
    {
        int particleId = FerrofluidParticleId(input.uv);
        float2 rest = LoadFerrofluidTexel(_RestPositionTex, particleId).xy;
        if (particleId >= _ParticleCount || particleId < _PrimitiveBakePinnedCount)
            return float4(rest, 0.0, 0.0);

        float4 state = LoadFerrofluidTexel(_StateTex, particleId);
        float deltaTime = _DeltaTimeMilliseconds * 0.001;
        float density = LoadFerrofluidTexel(_DensityPressureTex, particleId).x;
        float2 velocity = state.zw + (LoadFerrofluidTexel(_ForceTex, particleId).xy * deltaTime / density);
        velocity *= exp(-_Damping * deltaTime);
        float3 position, normal, jx, jy;
        FerrofluidPrimitiveFrame(state.xy, position, normal, jx, jy);
        float3 physical = (jx * velocity.x) + (jy * velocity.y);
        float2 next = FerrofluidChartFold(state.xy + (velocity * deltaTime), velocity);
        FerrofluidPrimitiveFrame(next, position, normal, jx, jy);
        physical -= normal * dot(physical, normal);
        return float4(next, FerrofluidTangentToChart(physical, jx, jy));
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
        int2 cell = (int2)((uint2)pixel / uint2(3u, 2u));
        int2 block = pixel - (cell * int2(3, 2));
        int blockId = block.x + (block.y * 3);
        // The sixth atlas texel is padding. The height pass reads four ID blocks and one bounds block.
        if (blockId == 5)
            return 0.0;

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
        inout float2 nearestSiteParticleUv,
        inout float2 densitySupport)
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
        if (_SurfaceShapeMode > 0.5)
        {
            // Blend density support in physical space, independently of the nearest spike's identity.
            float radius = distanceToParticle / (1.5 * _PrimitiveCellMetric * _HeightMapScale);
            float remainder = max(1.0 - (radius * radius), 0.0);
            float weight = remainder * remainder * remainder;
            densitySupport += float2(LoadFerrofluidTexel(_SiteBulgeTex, particleId).w * weight, weight);
        }
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
        float2 densitySupport = 0.0;

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
                nearestSiteParticleUv,
                densitySupport);
        }
    #else
        if (_SurfaceShapeMode > 0.5)
        {
            int2 cell = min((int2)floor(input.uv * _CoarseCandidateSize), (int)_CoarseCandidateSize - 1);
            int primitiveIds[16];
            [unroll]
            for (uint block = 0u; block < 4u; block++)
            {
                int2 blockPixel = (cell * int2(3, 2)) + int2(block % 3u, block / 3u);
                float4 ids = tex2Dlod(_CoarseCandidateTex, float4((blockPixel + 0.5) * _CoarseCandidateTex_TexelSize.xy, 0.0, 0.0));
                [unroll]
                for (int channel = 0; channel < 4; channel++)
                {
                    primitiveIds[(block * 4) + channel] = (int)round(ids[channel]);
                }
            }

            [loop]
            for (int sortRank = 1; sortRank < 16; sortRank++)
            {
                int id = primitiveIds[sortRank];
                int destination = sortRank;
                [loop]
                while (destination > 0 && id < primitiveIds[destination - 1])
                {
                    primitiveIds[destination] = primitiveIds[destination - 1];
                    destination--;
                }

                primitiveIds[destination] = id;
            }

            [unroll]
            for (int candidateRank = 0; candidateRank < 16; candidateRank++)
            {
                int id = primitiveIds[candidateRank];
                if (id >= 0)
                {
                    FerrofluidAccumulateHeightParticle(id, position, surfaceMetric, texelSurfacePosition, resultDistance, nearestDistance, secondNearestDistance, nearestSiteParticleUv, densitySupport);
                }
            }

            int2 boundPixel = (cell * int2(3, 2)) + int2(1, 1);
            float2 bounds = tex2Dlod(_CoarseCandidateTex, float4((boundPixel + 0.5) * _CoarseCandidateTex_TexelSize.xy, 0.0, 0.0)).xy;
            float3 center = FerrofluidPrimitiveBasePosition((cell + 0.5) / _CoarseCandidateSize, _SurfaceShapeMode, _SurfaceRoundness, _PrimitiveYawCosSin);
            float omittedLowerBound = bounds.x - (length(texelSurfacePosition - center) / bounds.y * _PrimitiveCellMetric * _HeightMapScale);
            // The pairwise smooth minimum stays within one smoothing width of its nearest input.
            // Two widths ensure that omitted sites cannot alter an earlier accumulation before the nearest site replaces it.
            float supportRadius = 1.5 * _PrimitiveCellMetric * _HeightMapScale;
            if (omittedLowerBound <= max(secondNearestDistance + (2.0 * _SmoothFactor), supportRadius))
            {
                resultDistance = 1.0;
                nearestDistance = 10000000000.0;
                secondNearestDistance = 10000000000.0;
                densitySupport = 0.0;
                [loop]
                for (int particleId = 0; particleId < _ParticleCount; particleId++)
                {
                    FerrofluidAccumulateHeightParticle(particleId, position, surfaceMetric, texelSurfacePosition, resultDistance, nearestDistance, secondNearestDistance, nearestSiteParticleUv, densitySupport);
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
                    nearestSiteParticleUv,
                    densitySupport);
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
        // One scalar ID leaves a channel for continuous support. Regularization fades empty neighborhoods to the base surface.
        return float4(
            spikeHeight * _HeightFactor,
            FerrofluidParticleId(nearestSiteParticleUv),
            densitySupport.x / (densitySupport.y + 0.0001),
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

        Pass
        {
            Name "BAKE_INTEGRATE"
            CGPROGRAM
            #pragma vertex FerrofluidBlitVertex
            #pragma fragment FerrofluidBakeIntegrateFragment
            ENDCG
        }

        Pass
        {
            Name "VALIDATION"
            CGPROGRAM
            #pragma vertex FerrofluidBlitVertex
            #pragma fragment FerrofluidValidationFragment
            ENDCG
        }

        Pass
        {
            // Rest z is baseline density, not velocity: only the home position seeds the state.
            Name "INITIAL_STATE"
            CGPROGRAM
            #pragma vertex FerrofluidBlitVertex
            #pragma fragment FerrofluidInitialStateFragment
            ENDCG
        }

    }

    Fallback Off
}
