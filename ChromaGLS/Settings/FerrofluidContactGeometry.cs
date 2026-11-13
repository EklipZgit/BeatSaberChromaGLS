using UnityEngine;

namespace ChromaGLS.Settings
{
    internal static class FerrofluidContactGeometry
    {
        // Enlarge the sharp collision core without changing the outer pickup surface.
        internal static float CalculateCubeCoreHalfExtent(float roundness)
            => 0.5f
                * Mathf.Pow(3f, -1f / Mathf.Lerp(8f, 2f, Mathf.Clamp01(roundness)))
                * (0.995f * 1.05f);
    }
}
