using UnityEngine;

namespace ChromaGLS.Settings
{
    internal static class MenuLightGlowGeometry
    {
        internal static ParabolicFloorLightGlowMeshData Create(
            ParabolicFloorLightMeshData core,
            ParabolicFloorLightSettings settings,
            float y,
            bool oneSided = false,
            Vector3[]? positiveOffsets = null,
            Vector3[]? negativeOffsets = null,
            float[]? negativeLimits = null,
            float[]? longitudinal = null)
        {
            int stations = core.Vertices.Length / 4;
            int broadCount = oneSided ? 7 : 14;
            int narrowCount = oneSided ? 17 : 34;
            float reach = 1.5f * settings.EvaluateGlowSpread() * Mathf.Max(0f, settings.BloomWidthScale);
            float whiteReach = Mathf.Min(reach * 0.125f, ParabolicFloorLightSettings.WhiteBleedReach);
            float[] broadDistances =
            {
                reach, 0.75f * reach, 0.5f * reach, 0.25f * reach, whiteReach, whiteReach * 0.5f, 0f,
                0f, whiteReach * 0.5f, whiteReach, 0.25f * reach, 0.5f * reach, 0.75f * reach, reach,
            };
            Color[] broadColors = new Color[broadCount];
            for (int lateral = 0; lateral < broadCount; lateral++)
            {
                float distance = broadDistances[oneSided ? lateral + 7 : lateral];
                float progress = reach > 0f ? distance / reach : 0f;
                Color tint = settings.EvaluateGlowTint(distance, whiteReach);
                broadColors[lateral] = new Color(tint.r, tint.g, tint.b, (1f - progress) * (1f - progress));
            }

            float narrowReach = reach * Mathf.Max(0f, settings.HdrGlowWidthScale);
            float hdrAlpha = narrowReach > 0f ? settings.EvaluateHdrGlowAlpha() : 0f;
            float hdrBrightness = Mathf.Max(0f, settings.HdrGlowBrightness);
            Color hdrTint = new(settings.Red * hdrBrightness, settings.Green * hdrBrightness, settings.Blue * hdrBrightness);
            int narrowBase = stations * broadCount;
            Vector3[] vertices = new Vector3[stations * (broadCount + narrowCount)];
            Color[] colors = new Color[vertices.Length];
            Vector2[] uv = new Vector2[vertices.Length];
            for (int station = 0; station < stations; station++)
            {
                int corner = station * 4;
                Vector3 positive = core.Vertices[corner];
                Vector3 negative = core.Vertices[corner + 1];
                Vector3 normal = new Vector3(positive.x - negative.x, 0f, positive.z - negative.z).normalized;
                float fade = longitudinal == null ? core.Colors[corner].a : longitudinal[station];
                for (int lateral = 0; lateral < broadCount; lateral++)
                {
                    bool negativeSide = !oneSided && lateral < 7;
                    float distance = broadDistances[oneSided ? lateral + 7 : lateral];
                    Vector3 point = Offset(positive, negative, normal, station, negativeSide, distance, positiveOffsets, negativeOffsets, negativeLimits);
                    int index = (station * broadCount) + lateral;
                    vertices[index] = new Vector3(point.x, y, point.z);
                    Color color = broadColors[lateral];
                    colors[index] = new Color(color.r, color.g, color.b, color.a * fade);
                    uv[index] = new Vector2(lateral / (float)(broadCount - 1), station / (float)(stations - 1));
                }

                for (int lateral = 0; lateral < narrowCount; lateral++)
                {
                    bool negativeSide = !oneSided && lateral < 17;
                    int sample = oneSided ? lateral : (negativeSide ? 16 - lateral : lateral - 17);
                    float distance = narrowReach * sample / 16f;
                    Vector3 point = Offset(positive, negative, normal, station, negativeSide, distance, positiveOffsets, negativeOffsets, negativeLimits);
                    int index = narrowBase + (station * narrowCount) + lateral;
                    vertices[index] = new Vector3(point.x, y, point.z);
                    float edge = sample / 16f;
                    colors[index] = new Color(hdrTint.r, hdrTint.g, hdrTint.b, (1f - edge) * (1f - edge) * hdrAlpha * fade);
                    uv[index] = new Vector2(lateral / (float)(narrowCount - 1), station / (float)(stations - 1));
                }
            }

            int quads = broadCount + narrowCount - (oneSided ? 2 : 4);
            int[] triangles = new int[(stations - 1) * quads * 6];
            int triangle = 0;
            WriteTriangles(triangles, ref triangle, stations, broadCount, 0, oneSided ? -1 : 6);
            WriteTriangles(triangles, ref triangle, stations, narrowCount, narrowBase, oneSided ? -1 : 16);
            return new ParabolicFloorLightGlowMeshData(vertices, triangles, colors, uv);
        }

        private static Vector3 Offset(
            Vector3 positive,
            Vector3 negative,
            Vector3 normal,
            int station,
            bool negativeSide,
            float distance,
            Vector3[]? positiveOffsets,
            Vector3[]? negativeOffsets,
            float[]? negativeLimits)
        {
            if (negativeSide)
            {
                Vector3 direction = negativeOffsets == null ? -normal : negativeOffsets[station];
                if (negativeLimits != null)
                    distance = Mathf.Min(distance, negativeLimits[station]);
                return negative + (direction * distance);
            }

            return positive + ((positiveOffsets == null ? normal : positiveOffsets[station]) * distance);
        }

        private static void WriteTriangles(int[] triangles, ref int index, int stations, int count, int offset, int gap)
        {
            for (int station = 0; station < stations - 1; station++)
            {
                for (int lateral = 0; lateral < count - 1; lateral++)
                {
                    if (lateral == gap)
                        continue;
                    int a = offset + (station * count) + lateral;
                    int b = a + 1;
                    int c = a + count;
                    int d = c + 1;
                    triangles[index++] = a;
                    triangles[index++] = c;
                    triangles[index++] = b;
                    triangles[index++] = b;
                    triangles[index++] = c;
                    triangles[index++] = d;
                }
            }
        }
    }
}
