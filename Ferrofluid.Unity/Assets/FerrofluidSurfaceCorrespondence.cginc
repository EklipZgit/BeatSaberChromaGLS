#ifndef FERROFLUID_SURFACE_CORRESPONDENCE_INCLUDED
#define FERROFLUID_SURFACE_CORRESPONDENCE_INCLUDED

// Projects the cached field position onto the pixel's view ray and converts the
// residual to a chart UV correction. One Newton-style correspondence step:
// ddx/ddy field gradients and the UV gradients eliminate the triangle
// parametrization error that otherwise makes the normal cache sample lag the
// rasterized surface.
float2 FerrofluidCorrectSurfaceUv(float2 uv, float3 meshPosition, float3 fieldPosition, float3 cameraPosition, float logicalSize)
{
    float3 ray = normalize(meshPosition - cameraPosition);
    float3 gradientX = ddx(fieldPosition);
    float3 gradientY = ddy(fieldPosition);
    gradientX -= ray * dot(ray, gradientX);
    gradientY -= ray * dot(ray, gradientY);
    float3 residual = fieldPosition - meshPosition;
    residual -= ray * dot(ray, residual);
    float3 areaNormal = cross(gradientX, gradientY);
    float inverseAreaSquared = rcp(max(dot(areaNormal, areaNormal), 1e-24));
    float deltaX = dot(cross(residual, gradientY), areaNormal) * inverseAreaSquared;
    float deltaY = dot(cross(gradientX, residual), areaNormal) * inverseAreaSquared;
    float2 correction = ddx(uv) * deltaX + ddy(uv) * deltaY;
    float maxCorrection = 2.0 / logicalSize;
    return uv - clamp(correction, -maxCorrection, maxCorrection);
}

#endif
