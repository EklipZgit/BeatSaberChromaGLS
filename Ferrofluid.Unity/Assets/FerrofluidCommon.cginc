#ifndef CHROMAGLS_FERROFLUID_COMMON_INCLUDED
#define CHROMAGLS_FERROFLUID_COMMON_INCLUDED

// frac wraps negative offsets correctly. fmod would leave negative remainders.
float2 WrapFerrofluidDelta(float2 delta)
{
    return frac((delta + 1.0) * 0.5) * 2.0 - 1.0;
}

int _ParticleTextureSize;

float2 FerrofluidParticleUv(int index)
{
    uint textureSize = (uint)_ParticleTextureSize;
    uint particleIndex = (uint)index;
    uint y = particleIndex / textureSize;
    uint x = particleIndex - (y * textureSize);
    return (float2(x, y) + 0.5) / _ParticleTextureSize;
}

int FerrofluidParticleId(float2 uv)
{
    int2 pixel = min((int2)floor(saturate(uv) * _ParticleTextureSize), _ParticleTextureSize - 1);
    return pixel.x + (pixel.y * _ParticleTextureSize);
}

// Inigo Quilez's almost-identity curve rounds only the cone tip.
float FerrofluidAlmostIdentity(float x, float m, float n)
{
    if (x > m)
    {
        return x;
    }

    float a = (2.0 * n) - m;
    float b = (2.0 * m) - (3.0 * n);
    float t = x / m;
    return ((a * t) + b) * t * t + n;
}

float3 FerrofluidPrimitiveSphereDirection(float2 uv, float2 yawCosSin)
{
    // Clamp rather than wrap: primitive UV edges meet at mirrored coordinates, not opposite edges.
    float2 p = saturate(uv) * 2.0 - 1.0;
    float3 direction = float3(p.x, 1.0 - abs(p.x) - abs(p.y), p.y);
    float fold = saturate(-direction.y);
    direction.xz += direction.xz >= 0.0 ? -fold : fold;
    float3 normalized = normalize(direction);
    return float3(
        (normalized.x * yawCosSin.x) + (normalized.z * yawCosSin.y),
        normalized.y,
        (normalized.z * yawCosSin.x) - (normalized.x * yawCosSin.y));
}

float3 FerrofluidPrimitiveBasePosition(float2 uv, float shapeMode, float roundness, float2 yawCosSin)
{
    float3 sphereDirection = FerrofluidPrimitiveSphereDirection(uv, yawCosSin);
    if (shapeMode > 1.5)
    {
        return sphereDirection * 0.5;
    }

    float exponent = lerp(8.0, 2.0, saturate(roundness));
    float3 poweredDirection = pow(abs(sphereDirection), exponent);
    float radialNorm = pow(
        poweredDirection.x + poweredDirection.y + poweredDirection.z,
        1.0 / exponent);
    return (sphereDirection / max(radialNorm, 0.000001)) * 0.5;
}

float2 FerrofluidPrimitiveSurfaceUv(float3 world, float2 yawCosSin)
{
    float3 direction = float3(
        (world.x * yawCosSin.x) - (world.z * yawCosSin.y),
        world.y,
        (world.x * yawCosSin.y) + (world.z * yawCosSin.x));
    float inverseL1 = 1.0 / max(abs(direction.x) + abs(direction.y) + abs(direction.z), 0.000001);
    float2 p = direction.xz * inverseL1;
    if (direction.y < 0.0)
    {
        p = (1.0 - abs(p.yx)) * (p >= 0.0 ? 1.0 : -1.0);
    }

    return (p * 0.5) + 0.5;
}

#endif
