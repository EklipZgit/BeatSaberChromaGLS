using UnityEngine;

namespace ChromaGLS.Settings
{
    // Analytic reflected-ray -> curved-canvas intersection and tile UV mapping for the
    // captured menu atlas. The canvas is a vertical cylinder matching the native
    // CurvedCanvasSettings radius; a hit inside the captured front arc and vertical span
    // projects through the tile's captured view-projection matrix.
    internal static class FerrofluidMenuReflectionGeometry
    {
        internal const int TileCount = 4;
        internal const float SectorDegrees = 45f;
        internal static readonly float[] SectorYawDegrees = { -67.5f, -22.5f, 22.5f, 67.5f };

        // Composed hit: nearest positive root inside the front arc and vertical span,
        // falling back to the second root when the nearer one is out of range.
        internal static bool TryIntersectCanvas(
            Vector3 origin,
            Vector3 direction,
            Vector2 centerXZ,
            float radius,
            float minY,
            float maxY,
            float halfArcDegrees,
            out Vector3 hit,
            out int tile)
        {
            hit = default;
            tile = -1;
            Vector2 o = new(origin.x - centerXZ.x, origin.z - centerXZ.y);
            Vector2 d = new(direction.x, direction.z);
            float a = (d.x * d.x) + (d.y * d.y);
            if (a < 1e-12f)
                return false;

            float b = 2f * ((o.x * d.x) + (o.y * d.y));
            float c = (o.x * o.x) + (o.y * o.y) - (radius * radius);
            float discriminant = (b * b) - (4f * a * c);
            if (discriminant < 0f)
                return false;

            float sqrt = Mathf.Sqrt(discriminant);
            float inv2a = 0.5f / a;
            float t1 = (-b - sqrt) * inv2a;
            float t2 = (-b + sqrt) * inv2a;

            // Try the nearer root first; an out-of-arc/out-of-span hit retries the far root.
            return TryAccept(origin, direction, t1, centerXZ, minY, maxY, halfArcDegrees, ref hit, ref tile)
                || TryAccept(origin, direction, t2, centerXZ, minY, maxY, halfArcDegrees, ref hit, ref tile);
        }

        private static bool TryAccept(
            Vector3 origin,
            Vector3 direction,
            float t,
            Vector2 centerXZ,
            float minY,
            float maxY,
            float halfArcDegrees,
            ref Vector3 hit,
            ref int tile)
        {
            if (t <= 1e-4f)
                return false;

            Vector3 candidate = origin + (t * direction);
            if (candidate.y < minY || candidate.y > maxY)
                return false;

            float angle = Mathf.Atan2(candidate.x - centerXZ.x, candidate.z - centerXZ.y) * Mathf.Rad2Deg;
            if (angle < -halfArcDegrees || angle > halfArcDegrees)
                return false;

            int index = TileIndexForAngle(angle);
            if (index < 0)
                return false;

            hit = candidate;
            tile = index;
            return true;
        }

        internal static int TileIndexForAngle(float angleDegrees)
        {
            float sector = (angleDegrees + 90f) / SectorDegrees;
            if (sector < 0f || sector >= TileCount)
                return -1;
            return Mathf.Clamp((int)sector, 0, TileCount - 1);
        }

        // Projects the canvas-local hit through the tile's captured view-projection matrix;
        // w must be positive or the point sits behind the capture camera.
        internal static bool TryProjectToAtlas(
            Vector3 canvasLocalHit,
            int tile,
            Matrix4x4[] viewProjections,
            out Vector2 atlasUV)
        {
            atlasUV = default;
            if (tile < 0 || tile >= TileCount || viewProjections == null || viewProjections.Length < TileCount)
                return false;

            Vector4 clip = viewProjections[tile] * new Vector4(canvasLocalHit.x, canvasLocalHit.y, canvasLocalHit.z, 1f);
            if (clip.w <= 1e-6f)
                return false;

            Vector2 ndc = new(clip.x / clip.w, clip.y / clip.w);
            if (ndc.x < -1f || ndc.x > 1f || ndc.y < -1f || ndc.y > 1f)
                return false;

            // 2x2 tile layout, tile index = column + 2 * row, bottom-left atlas origin.
            Vector2 tileUV = new((ndc.x * 0.5f) + 0.5f, (ndc.y * 0.5f) + 0.5f);
            int column = tile % 2;
            int row = tile / 2;
            atlasUV = new Vector2(
                (column + tileUV.x) * 0.5f,
                (row + tileUV.y) * 0.5f);
            return true;
        }
    }
}
