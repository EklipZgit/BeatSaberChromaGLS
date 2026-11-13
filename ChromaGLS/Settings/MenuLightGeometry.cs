using System;
using UnityEngine;

namespace ChromaGLS.Settings
{
    internal struct MenuLightReflectionBounds
    {
        internal float MinX;
        internal float MinZ;
        internal float InverseWidth;
        internal float InverseHeight;
    }

    internal readonly struct MenuLightMeshes
    {
        internal readonly ParabolicFloorLightMeshData Core;
        internal readonly ParabolicFloorLightGlowMeshData Glow;
        internal readonly ParabolicFloorLightMeshData ReflectionCore;
        internal readonly Vector3 Position;
        internal readonly Quaternion Rotation;
        internal readonly MenuLightReflectionBounds Bounds;

        internal MenuLightMeshes(
            ParabolicFloorLightMeshData core,
            ParabolicFloorLightGlowMeshData glow,
            ParabolicFloorLightMeshData reflectionCore,
            Vector3 position,
            Quaternion rotation,
            MenuLightReflectionBounds bounds)
        {
            Core = core;
            Glow = glow;
            ReflectionCore = reflectionCore;
            Position = position;
            Rotation = rotation;
            Bounds = bounds;
        }

        internal Matrix4x4 GetLocalToWorld()
            => Matrix4x4.TRS(Position, Rotation, Vector3.one);
    }

    internal static class MenuLightGeometry
    {
        private const int CurveSegments = ParabolicFloorLightGeometry.SegmentCount;

        // Floor core vertices carry per-face radiance factors and bloom alpha; the
        // directional reflection metadata derives from the same shared scales.
        internal const float FloorTopRadianceScale = 0.64f;
        internal const float FloorSideRadianceScale = 1.44f;
        internal const float FloorSideEmissionScale = 1.3f;

        // Faded ribbon ends stay slightly lit: reaching black would read darker than the room.
        private const float MinimumRibbonEmission = 0.1f;

        internal static bool TryCreate(
            FerrofluidGrabEnclosure enclosure,
            Vector3 playerPosition,
            Vector3 forward,
            MenuLightDefinition definition,
            out MenuLightMeshes meshes)
        {
            Vector3 flat = new(forward.x, 0f, forward.z);
            flat = flat.sqrMagnitude > 0.000001f ? flat.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, flat);
            Quaternion yaw = Quaternion.AngleAxis(Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, Vector3.up);
            float renderHeight = definition.GetHeight();
            Vector3 position = new Vector3(playerPosition.x, enclosure.FloorY + renderHeight, playerPosition.z)
                + (right * definition.PositionX) + (flat * definition.PositionZ);
            Quaternion rotation = yaw * definition.GetRotation();
            ParabolicFloorLightMeshData core;
            ParabolicFloorLightMeshData reflection;
            ParabolicFloorLightGlowMeshData glow;
            if (definition.Type == MenuLightType.Parabola || definition.Type == MenuLightType.Floor)
            {
                position -= flat * 0.35f;
                if (!TryCreateParabola(enclosure, playerPosition, forward, definition, flat, yaw, renderHeight, out core, out glow))
                {
                    meshes = default;
                    return false;
                }

                reflection = TopSurface(core);
                if (definition.Type == MenuLightType.Floor)
                {
                    core = ApplyFloorFaceRadiance(core);
                    reflection = ApplyFloorFaceRadiance(reflection);
                }
            }
            else
            {
                CreateClosedShape(definition, out core, out reflection, out glow);
            }

            meshes = new MenuLightMeshes(core, glow, reflection, position, rotation, CalculateBounds(reflection, glow));
            return true;
        }

        private static bool TryCreateParabola(
            FerrofluidGrabEnclosure enclosure,
            Vector3 playerPosition,
            Vector3 forward,
            MenuLightDefinition definition,
            Vector3 flat,
            Quaternion yaw,
            float renderHeight,
            out ParabolicFloorLightMeshData core,
            out ParabolicFloorLightGlowMeshData glow)
        {
            float coefficient = ParabolicFloorLightGeometry.BaseCurveScale
                / (definition.Appearance.WidthScale * definition.Appearance.WidthScale);
            float left;
            float right;
            if (definition.EndLength > 0f)
            {
                left = right = Mathf.Sqrt(definition.EndLength / coefficient);
            }
            else
            {
                ParabolicFloorLightSettings settings = definition.Appearance
                    .WithOffsetX(definition.PositionX).WithOffsetZ(definition.PositionZ).WithRotation(definition.RotationY);
                if (!ParabolicFloorLightGeometry.TryCreateConfigured(enclosure, playerPosition, forward, settings, out ParabolicFloorLightMeshData? legacy)
                    || !legacy.HasValue)
                {
                    core = default;
                    glow = default;
                    return false;
                }

                Vector3 apex = playerPosition - (flat * 0.35f)
                    + (Vector3.Cross(Vector3.up, flat) * definition.PositionX) + (flat * definition.PositionZ);
                Quaternion inverseYaw = Quaternion.Inverse(yaw * Quaternion.AngleAxis(definition.RotationY, Vector3.up));
                Vector3[] source = legacy.Value.Vertices;
                left = Mathf.Abs((inverseYaw * (((source[0] + source[1]) * 0.5f) - apex)).x);
                int end = source.Length - 4;
                right = Mathf.Abs((inverseYaw * (((source[end] + source[end + 1]) * 0.5f) - apex)).x);
            }

            int stations = CurveSegments + 1;
            Vector3[] positive = new Vector3[stations];
            Vector3[] negative = new Vector3[stations];
            float[] alpha = new float[stations];
            float halfWidth = 0.0875f * definition.Appearance.LightWidthScale;
            for (int station = 0; station < stations; station++)
            {
                int branch = CurveSegments / 2;
                float x = station <= branch ? -left * (branch - station) / branch : right * (station - branch) / branch;
                Vector3 center = new(x, 0f, coefficient * x * x);
                Vector3 normal = new Vector3(2f * coefficient * x, 0f, -1f).normalized;
                positive[station] = center + (normal * halfWidth);
                negative[station] = center - (normal * halfWidth);
                float progress = (station - branch) / (float)branch;
                alpha[station] = 1f - Mathf.SmoothStep(0f, 1f, ((progress * progress) - definition.FadeStart) / (1f - definition.FadeStart));
            }

            core = CreateRibbon(positive, negative, definition.Type == MenuLightType.Floor ? renderHeight : definition.Depth, alpha, true);

            // Diffuse the source mask along the static ribbon instead of copying its transparency into the spill.
            float[] spillAlpha = new float[stations];
            float[] weights = { 1f / 16f, 4f / 16f, 6f / 16f, 4f / 16f, 1f / 16f };
            for (int station = 0; station < stations; station++)
            {
                for (int tap = 0; tap < weights.Length; tap++)
                {
                    int source = station + tap - 2;
                    if (source >= 0 && source < stations)
                        spillAlpha[station] += alpha[source] * weights[tap];
                }
            }

            glow = definition.Type == MenuLightType.Floor
                ? MenuLightGlowGeometry.Create(
                    core,
                    definition.Appearance,
                    ParabolicFloorLightGeometry.GlowHeight - renderHeight,
                    longitudinal: spillAlpha)
                : new ParabolicFloorLightGlowMeshData(Array.Empty<Vector3>(), Array.Empty<int>(), Array.Empty<Color>(), Array.Empty<Vector2>());
            return true;
        }

        private static void CreateClosedShape(
            MenuLightDefinition definition,
            out ParabolicFloorLightMeshData core,
            out ParabolicFloorLightMeshData reflection,
            out ParabolicFloorLightGlowMeshData glow)
        {
            bool round = definition.Type == MenuLightType.Circle || definition.Type == MenuLightType.Sphere;
            bool solid = definition.Type == MenuLightType.Cube || definition.Type == MenuLightType.Sphere;
            int segments = round ? CurveSegments : 4;
            int stations = segments + 1;
            float half = definition.Size * 0.5f;
            float width = Mathf.Min(0.175f * definition.Appearance.LightWidthScale, half * 0.99f);
            Vector3[] positive = new Vector3[stations];
            Vector3[] negative = new Vector3[stations];
            for (int station = 0; station < stations; station++)
            {
                int point = station % segments;
                Vector3 direction = round
                    ? new Vector3(Mathf.Cos(2f * Mathf.PI * point / segments), 0f, Mathf.Sin(2f * Mathf.PI * point / segments))
                    : SquareCorner(point);
                positive[station] = direction * half;
                negative[station] = direction * (half - width);
            }

            ParabolicFloorLightMeshData ribbon = CreateRibbon(positive, negative, definition.Depth, null, false);
            core = solid
                ? (round ? CreateSphere(definition.Size, definition.Depth) : CreateBox(definition.Size, definition.Depth))
                : ribbon;
            reflection = solid ? FilledFootprint(positive) : TopSurface(ribbon);

            // Only the floor parabola keeps the planar glow; closed shapes use native bloom instead.
            glow = new ParabolicFloorLightGlowMeshData(Array.Empty<Vector3>(), Array.Empty<int>(), Array.Empty<Color>(), Array.Empty<Vector2>());
        }

        private static ParabolicFloorLightMeshData CreateRibbon(
            Vector3[] positive,
            Vector3[] negative,
            float depth,
            float[]? alpha,
            bool capEnds)
        {
            int stations = positive.Length;
            Vector3[] vertices = new Vector3[stations * 4];
            Color[] colors = new Color[vertices.Length];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[((stations - 1) * 24) + (capEnds ? 12 : 0)];
            int triangle = 0;
            for (int station = 0; station < stations; station++)
            {
                int corner = station * 4;
                vertices[corner] = positive[station];
                vertices[corner + 1] = negative[station];
                vertices[corner + 2] = negative[station] - (Vector3.up * depth);
                vertices[corner + 3] = positive[station] - (Vector3.up * depth);
                float emission = alpha == null ? 1f : MinimumRibbonEmission + ((1f - MinimumRibbonEmission) * alpha[station]);
                for (int side = 0; side < 4; side++)
                {
                    colors[corner + side] = new Color(1f, 1f, 1f, emission);
                    uv[corner + side] = new Vector2(station / (float)(stations - 1), side * 0.25f);
                }

                if (station == stations - 1)
                    continue;
                int next = corner + 4;
                WriteQuad(triangles, ref triangle, corner, corner + 1, next, next + 1);
                WriteQuad(triangles, ref triangle, corner + 3, next + 3, corner + 2, next + 2);
                WriteQuad(triangles, ref triangle, corner + 3, corner, next + 3, next);
                WriteQuad(triangles, ref triangle, corner + 1, corner + 2, next + 1, next + 2);
            }

            if (capEnds)
            {
                WriteQuad(triangles, ref triangle, 3, 2, 0, 1);
                int end = (stations - 1) * 4;
                WriteQuad(triangles, ref triangle, end + 1, end + 2, end, end + 3);
            }

            return new ParabolicFloorLightMeshData(vertices, triangles, colors, uv);
        }

        // Duplicate triangle vertices so top and vertical faces can carry independent
        // radiance and native-bloom input. Top scales .64, sides/endcaps 1.44, bottom 1.
        private static ParabolicFloorLightMeshData ApplyFloorFaceRadiance(ParabolicFloorLightMeshData core)
        {
            int indexCount = core.Triangles.Length;
            Vector3[] vertices = new Vector3[indexCount];
            Color[] colors = new Color[indexCount];
            Vector2[] uv = new Vector2[indexCount];
            int[] triangles = new int[indexCount];
            for (int triangle = 0; triangle < indexCount; triangle += 3)
            {
                int a = core.Triangles[triangle];
                int b = core.Triangles[triangle + 1];
                int c = core.Triangles[triangle + 2];
                Vector3 normal = Vector3.Cross(core.Vertices[b] - core.Vertices[a], core.Vertices[c] - core.Vertices[a]);
                float factor = normal.y > 0f ? FloorTopRadianceScale : normal.y < 0f ? 1f : FloorSideRadianceScale;

                // Vertex alpha scales both emitted RGB and native bloom input, so only sides
                // receive the extra 30%.
                float alphaFactor = normal.y == 0f ? FloorSideEmissionScale : 1f;
                for (int i = 0; i < 3; i++)
                {
                    int sourceIndex = core.Triangles[triangle + i];
                    vertices[triangle + i] = core.Vertices[sourceIndex];
                    uv[triangle + i] = core.UV[sourceIndex];
                    Color color = core.Colors[sourceIndex];
                    colors[triangle + i] = new Color(color.r * factor, color.g * factor, color.b * factor, color.a * alphaFactor);
                    triangles[triangle + i] = triangle + i;
                }
            }

            return new ParabolicFloorLightMeshData(vertices, triangles, colors, uv);
        }

        private static ParabolicFloorLightMeshData TopSurface(ParabolicFloorLightMeshData core)
        {
            int segments = (core.Vertices.Length / 4) - 1;
            int[] triangles = new int[segments * 6];
            for (int segment = 0; segment < segments; segment++)
                System.Array.Copy(core.Triangles, segment * 24, triangles, segment * 6, 6);
            return new ParabolicFloorLightMeshData(core.Vertices, triangles, core.Colors, core.UV);
        }

        private static ParabolicFloorLightMeshData FilledFootprint(Vector3[] outline)
        {
            Vector3[] vertices = new Vector3[outline.Length + 1];
            Color[] colors = new Color[vertices.Length];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[(outline.Length - 1) * 3];
            colors[0] = Color.white;
            for (int point = 0; point < outline.Length; point++)
            {
                vertices[point + 1] = outline[point];
                colors[point + 1] = Color.white;
                if (point == outline.Length - 1)
                    continue;
                triangles[point * 3] = 0;
                triangles[(point * 3) + 1] = point + 2;
                triangles[(point * 3) + 2] = point + 1;
            }

            return new ParabolicFloorLightMeshData(vertices, triangles, colors, uv);
        }

        private static ParabolicFloorLightMeshData CreateBox(float size, float depth)
        {
            float half = size * 0.5f;
            Vector3[] vertices =
            {
                new(-half, -depth, -half), new(half, -depth, -half), new(half, -depth, half), new(-half, -depth, half),
                new(-half, size, -half), new(half, size, -half), new(half, size, half), new(-half, size, half),
            };
            int[] triangles =
            {
                0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6,
                0, 5, 1, 0, 4, 5, 1, 6, 2, 1, 5, 6,
                2, 7, 3, 2, 6, 7, 3, 4, 0, 3, 7, 4,
            };
            Color[] colors = new Color[vertices.Length];
            for (int index = 0; index < colors.Length; index++)
                colors[index] = Color.white;
            return new ParabolicFloorLightMeshData(vertices, triangles, colors, new Vector2[vertices.Length]);
        }

        private static ParabolicFloorLightMeshData CreateSphere(float size, float depth)
        {
            const int latitudeCount = 16;
            const int longitudeCount = 24;
            int stride = longitudeCount + 1;
            Vector3[] vertices = new Vector3[(latitudeCount + 1) * stride];
            Color[] colors = new Color[vertices.Length];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[latitudeCount * longitudeCount * 6];
            float radius = size * 0.5f;
            int triangle = 0;
            for (int latitude = 0; latitude <= latitudeCount; latitude++)
            {
                float theta = latitude * Mathf.PI / latitudeCount;
                float ringRadius = latitude == 0 || latitude == latitudeCount ? 0f : radius * Mathf.Sin(theta);
                float cosine = Mathf.Cos(theta);
                float y = radius + ((radius + (cosine < 0f ? depth : 0f)) * cosine);
                for (int longitude = 0; longitude <= longitudeCount; longitude++)
                {
                    float phi = (longitude % longitudeCount) * Mathf.PI * 2f / longitudeCount;
                    int index = (latitude * stride) + longitude;
                    vertices[index] = new Vector3(ringRadius * Mathf.Cos(phi), y, ringRadius * Mathf.Sin(phi));
                    colors[index] = Color.white;
                    uv[index] = new Vector2(longitude / (float)longitudeCount, latitude / (float)latitudeCount);
                    if (latitude < latitudeCount && longitude < longitudeCount)
                        WriteQuad(triangles, ref triangle, index, index + 1, index + stride, index + stride + 1);
                }
            }

            return new ParabolicFloorLightMeshData(vertices, triangles, colors, uv);
        }

        private static MenuLightReflectionBounds CalculateBounds(
            ParabolicFloorLightMeshData core,
            ParabolicFloorLightGlowMeshData glow)
        {
            Vector3 min = core.Vertices[0];
            Vector3 max = min;
            foreach (Vector3 vertex in core.Vertices)
            {
                min = Vector3.Min(min, vertex);
                max = Vector3.Max(max, vertex);
            }

            foreach (Vector3 vertex in glow.Vertices)
            {
                min = Vector3.Min(min, vertex);
                max = Vector3.Max(max, vertex);
            }

            float size = Mathf.Max(max.x - min.x, max.z - min.z) * 1.5f;
            return new MenuLightReflectionBounds
            {
                MinX = ((min.x + max.x) - size) * 0.5f,
                MinZ = ((min.z + max.z) - size) * 0.5f,
                InverseWidth = 1f / size,
                InverseHeight = 1f / size,
            };
        }

        private static void WriteQuad(int[] triangles, ref int index, int a, int b, int c, int d)
        {
            triangles[index++] = a;
            triangles[index++] = b;
            triangles[index++] = c;
            triangles[index++] = c;
            triangles[index++] = b;
            triangles[index++] = d;
        }

        private static Vector3 SquareCorner(int point)
        {
            switch (point)
            {
                case 0:
                    return new Vector3(-1f, 0f, -1f);
                case 1:
                    return new Vector3(1f, 0f, -1f);
                case 2:
                    return new Vector3(1f, 0f, 1f);
                default:
                    return new Vector3(-1f, 0f, 1f);
            }
        }
    }
}
