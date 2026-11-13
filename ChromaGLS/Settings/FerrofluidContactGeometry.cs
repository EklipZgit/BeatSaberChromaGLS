using System.Collections.Generic;
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

        internal static Mesh CreateCubeColliderMesh(float roundness)
        {
            if (Mathf.Abs(roundness - 0.48f) <= 0.00001f)
                return FerrofluidPrototypeContactGeometry.CreateCubeColliderMesh();

            // Sampling surface normals keeps curvature near the spinning corners with only
            // 98 vertices, below PhysX's convex-mesh limit.
            float[] coordinates = { -1f, -0.65f, 0f, 0.65f, 1f };
            Vector3[] vertices = new Vector3[122];
            int[] indices = new int[125];
            int vertexCount = 0;
            float exponent = Mathf.Lerp(8f, 2f, Mathf.Clamp01(roundness));
            float dual = exponent / (exponent - 1f);
            for (int x = 0; x < 5; x++)
            {
                for (int y = 0; y < 5; y++)
                {
                    for (int z = 0; z < 5; z++)
                    {
                        if (x != 0 && x != 4 && y != 0 && y != 4 && z != 0 && z != 4)
                            continue;

                        indices[(x * 25) + (y * 5) + z] = vertexCount;
                        vertices[vertexCount++] = CalculateCubeSupportPoint(
                            new Vector3(coordinates[x], coordinates[y], coordinates[z]),
                            dual);
                    }
                }
            }

            // 24 additional support samples bisect the normals halfway between each edge
            // normal (variable axis 0) and its adjacent near-corner normal (variable axis
            // +-0.65), refining corner-to-edge transitions without moving the original
            // 98 support samples.
            float midpoint = 0.65f * Mathf.Sqrt(2f)
                / (Mathf.Sqrt(2f) + Mathf.Sqrt(2f + (0.65f * 0.65f)));
            var splitVertices = new Dictionary<long, int>(24);
            for (int axis = 0; axis < 3; axis++)
            {
                int otherA = (axis + 1) % 3;
                int otherB = (axis + 2) % 3;
                for (int signA = -1; signA <= 1; signA += 2)
                {
                    for (int signB = -1; signB <= 1; signB += 2)
                    {
                        for (int signVariable = -1; signVariable <= 1; signVariable += 2)
                        {
                            int[] gridEdge = new int[3];
                            int[] gridNear = new int[3];
                            gridEdge[axis] = 2;
                            gridNear[axis] = signVariable > 0 ? 3 : 1;
                            gridEdge[otherA] = gridNear[otherA] = signA > 0 ? 4 : 0;
                            gridEdge[otherB] = gridNear[otherB] = signB > 0 ? 4 : 0;
                            int edgeId = indices[(gridEdge[0] * 25) + (gridEdge[1] * 5) + gridEdge[2]];
                            int nearId = indices[(gridNear[0] * 25) + (gridNear[1] * 5) + gridNear[2]];
                            Vector3 normal = Vector3.zero;
                            normal[axis] = signVariable * midpoint;
                            normal[otherA] = signA;
                            normal[otherB] = signB;
                            splitVertices[EdgeKey(edgeId, nearId)] = vertexCount;
                            vertices[vertexCount++] = CalculateCubeSupportPoint(normal, dual);
                        }
                    }
                }
            }

            int[] triangles = new int[240 * 3];
            int triangleIndex = 0;
            void Emit(int a, int b, int c)
            {
                triangles[triangleIndex++] = a;
                triangles[triangleIndex++] = b;
                triangles[triangleIndex++] = c;
            }

            void AppendTriangle(int a, int b, int c)
            {
                int s;
                if (splitVertices.TryGetValue(EdgeKey(a, b), out s))
                {
                    Emit(a, s, c);
                    Emit(s, b, c);
                }
                else if (splitVertices.TryGetValue(EdgeKey(b, c), out s))
                {
                    Emit(b, s, a);
                    Emit(s, c, a);
                }
                else if (splitVertices.TryGetValue(EdgeKey(c, a), out s))
                {
                    Emit(c, s, b);
                    Emit(s, a, b);
                }
                else
                {
                    Emit(a, b, c);
                }
            }

            for (int axis = 0; axis < 3; axis++)
            {
                for (int side = 0; side < 2; side++)
                {
                    int plane = side * 4;
                    for (int u = 0; u < 4; u++)
                    {
                        for (int v = 0; v < 4; v++)
                        {
                            int a = GetCubeVertexIndex(indices, axis, plane, u, v);
                            int b = GetCubeVertexIndex(indices, axis, plane, u + 1, v);
                            int c = GetCubeVertexIndex(indices, axis, plane, u + 1, v + 1);
                            int d = GetCubeVertexIndex(indices, axis, plane, u, v + 1);
                            if (side == 0)
                            {
                                int swap = b;
                                b = d;
                                d = swap;
                            }

                            AppendTriangle(a, b, c);
                            AppendTriangle(a, c, d);
                        }
                    }
                }
            }

            Mesh mesh = new()
            {
                name = "Ferrofluid rounded cube contact",
                hideFlags = HideFlags.DontSave,
                vertices = vertices,
                triangles = triangles,
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static long EdgeKey(int a, int b)
            => a < b ? (((long)a << 32) | (uint)b) : (((long)b << 32) | (uint)a);

        private static Vector3 CalculateCubeSupportPoint(Vector3 normal, float dual)
        {
            float denominator = Mathf.Pow(
                Mathf.Pow(Mathf.Abs(normal.x), dual)
                    + Mathf.Pow(Mathf.Abs(normal.y), dual)
                    + Mathf.Pow(Mathf.Abs(normal.z), dual),
                (dual - 1f) / dual);
            float factor = 0.5f / denominator;
            return new Vector3(
                Mathf.Sign(normal.x) * Mathf.Pow(Mathf.Abs(normal.x), dual - 1f),
                Mathf.Sign(normal.y) * Mathf.Pow(Mathf.Abs(normal.y), dual - 1f),
                Mathf.Sign(normal.z) * Mathf.Pow(Mathf.Abs(normal.z), dual - 1f)) * factor;
        }

        private static int GetCubeVertexIndex(int[] indices, int axis, int plane, int u, int v)
        {
            if (axis == 0)
                return indices[(plane * 25) + (u * 5) + v];
            if (axis == 1)
                return indices[(v * 25) + (plane * 5) + u];
            return indices[(u * 25) + (v * 5) + plane];
        }
    }
}
