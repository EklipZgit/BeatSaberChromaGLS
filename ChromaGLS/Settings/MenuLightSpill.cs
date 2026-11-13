using System;
using UnityEngine;

namespace ChromaGLS.Settings
{
    internal readonly struct MenuLightSpillSample
    {
        internal readonly Vector3 Position;
        internal readonly Vector3 Normal;
        internal readonly float RadiusSquared;
        internal readonly Vector3 Energy;

        internal MenuLightSpillSample(Vector3 position, Vector3 normal, float radiusSquared, Vector3 energy)
        {
            Position = position;
            Normal = normal;
            RadiusSquared = radiusSquared;
            Energy = energy;
        }
    }

    internal static class MenuLightSpill
    {
        internal static MenuLightSpillSample[] Calculate(MenuLightMeshes meshes, ParabolicFloorLightSettings appearance, bool linearColorSpace)
        {
            Accumulator[] groups = new Accumulator[4];
            MenuLightReflectionBounds bounds = meshes.Bounds;
            Vector2 center = new(bounds.MinX + (0.5f / bounds.InverseWidth), bounds.MinZ + (0.5f / bounds.InverseHeight));
            Color coreTint = appearance.EvaluateColor();
            Color glowTint = appearance.EvaluateGlowColor();
            if (linearColorSpace)
            {
                coreTint = coreTint.linear;
                glowTint = glowTint.linear;
            }

            ParabolicFloorLightMeshData core = meshes.ReflectionCore;
            Accumulate(core.Vertices, core.Triangles, core.Colors, coreTint, center, groups);
            if (appearance.BloomWidthScale > 0f && appearance.BloomBrightness > 0f)
            {
                ParabolicFloorLightGlowMeshData glow = meshes.Glow;
                Accumulate(glow.Vertices, glow.Triangles, glow.Colors, glowTint, center, groups);
            }

            MenuLightSpillSample[] samples = new MenuLightSpillSample[4];
            int count = 0;
            Vector3 normal = meshes.Rotation * Vector3.up;
            foreach (Accumulator group in groups)
            {
                if (group.Weight <= 0f)
                    continue;

                Vector3 position = meshes.Position + (meshes.Rotation * (group.WeightedPosition / group.Weight));
                samples[count++] = new MenuLightSpillSample(position, normal, group.Area / Mathf.PI, group.Energy);
            }

            Array.Resize(ref samples, count);
            return samples;
        }

        private static void Accumulate(
            Vector3[] vertices,
            int[] triangles,
            Color[] colors,
            Color tint,
            Vector2 center,
            Accumulator[] groups)
        {
            Vector3 tintEnergy = Rgb(tint) * tint.a;
            for (int index = 0; index < triangles.Length; index += 3)
            {
                int a = triangles[index];
                int b = triangles[index + 1];
                int c = triangles[index + 2];
                Vector3 ab = vertices[b] - vertices[a];
                Vector3 ac = vertices[c] - vertices[a];
                float area = Mathf.Abs((ab.x * ac.z) - (ab.z * ac.x)) * 0.5f;
                if (area <= 0f)
                    continue;

                Color ca = colors[a];
                Color cb = colors[b];
                Color cc = colors[c];

                // Both color and alpha vary across the halo. Integrate their product,
                // not the product of their averages, to preserve the end and edge fades.
                Vector3 mean = (((Rgb(ca) + Rgb(cb) + Rgb(cc)) * (ca.a + cb.a + cc.a))
                    + (Rgb(ca) * ca.a) + (Rgb(cb) * cb.a) + (Rgb(cc) * cc.a)) / 12f;
                Vector3 energy = Vector3.Scale(mean, tintEnergy) * area;
                float weight = Vector3.Dot(energy, new Vector3(0.2126f, 0.7152f, 0.0722f));
                if (weight <= 0f)
                    continue;

                Vector3 centroid = (vertices[a] + vertices[b] + vertices[c]) / 3f;
                int quadrant = (centroid.x >= center.x ? 1 : 0) + (centroid.z >= center.y ? 2 : 0);
                groups[quadrant].Energy += energy;
                groups[quadrant].WeightedPosition += centroid * weight;
                groups[quadrant].Weight += weight;
                groups[quadrant].Area += area;
            }
        }

        private static Vector3 Rgb(Color color) => new(color.r, color.g, color.b);

        private struct Accumulator
        {
            internal Vector3 Energy;
            internal Vector3 WeightedPosition;
            internal float Weight;
            internal float Area;
        }
    }
}
