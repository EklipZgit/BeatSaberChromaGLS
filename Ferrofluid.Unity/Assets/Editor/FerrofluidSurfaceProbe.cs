using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // Probes the actual surface vertex deformation at exact chart UVs: one clip-space quad per query
    // with a single shared UV, rendered through the existing world-position pass so position (0) or
    // normal/film (1) lands at one texel each.
    internal static class FerrofluidSurfaceProbe
    {
        internal static Color[] Sample(Material material, Vector2[] samples, int outputMode)
            => Sample(material, samples, outputMode, Matrix4x4.identity);

        internal static Color[] Sample(Material material, Vector2[] samples, int outputMode, Matrix4x4 objectToWorld)
        {
            int count = samples.Length;
            RenderTexture target = RenderTexture.GetTemporary(count, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.filterMode = FilterMode.Point;
            Mesh quad = new();
            RenderTexture previous = RenderTexture.active;
            material.SetFloat("_ValidationOutput", outputMode);
            try
            {
                Vector3[] vertices = new Vector3[4 * count];
                Vector2[] uvs = new Vector2[4 * count];
                int[] triangles = new int[6 * count];
                for (int index = 0; index < count; index++)
                {
                    float left = -1f + (2f * index / count);
                    float right = -1f + (2f * (index + 1) / count);
                    int baseIndex = index * 4;
                    vertices[baseIndex + 0] = new Vector3(left, -1f, 0f);
                    vertices[baseIndex + 1] = new Vector3(right, -1f, 0f);
                    vertices[baseIndex + 2] = new Vector3(left, 1f, 0f);
                    vertices[baseIndex + 3] = new Vector3(right, 1f, 0f);
                    uvs[baseIndex + 0] = samples[index];
                    uvs[baseIndex + 1] = samples[index];
                    uvs[baseIndex + 2] = samples[index];
                    uvs[baseIndex + 3] = samples[index];
                    int triIndex = index * 6;
                    triangles[triIndex + 0] = baseIndex + 0;
                    triangles[triIndex + 1] = baseIndex + 2;
                    triangles[triIndex + 2] = baseIndex + 1;
                    triangles[triIndex + 3] = baseIndex + 1;
                    triangles[triIndex + 4] = baseIndex + 2;
                    triangles[triIndex + 5] = baseIndex + 3;
                }

                quad.vertices = vertices;
                quad.uv = uvs;
                quad.triangles = triangles;
                RenderTexture.active = target;
                GL.Clear(true, true, new Color(-2f, -2f, -2f, -2f));
                if (!material.SetPass(4))
                    throw new Exception("The surface validation pass is unavailable.");

                Graphics.DrawMeshNow(quad, objectToWorld);
                return FerrofluidValidationRuntime.Read(target);
            }
            finally
            {
                RenderTexture.active = previous;
                material.SetFloat("_ValidationOutput", 0f);
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(quad);
            }
        }
    }
}
