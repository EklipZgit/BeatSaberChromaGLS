using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    // Each octahedral edge identifies mirrored positions on that same edge, not the opposite edge.
    internal static class FerrofluidSeamValidation
    {
        private static readonly List<string> Failures = new();

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidSeamValidation));

        private static void RunRuntime()
        {
            Failures.Clear();
            foreach (string shape in new[] { "RoundedCube", "Sphere" })
            {
                // The cube yaw is a fixed 45 degrees; only the sphere yaw is configurable.
                foreach (float yaw in shape == "RoundedCube" ? new[] { 45f } : new[] { 0f, 45f })
                {
                    foreach (bool magnet in new[] { false, true })
                    {
                        RunCase(shape, yaw, magnet);
                    }
                }
            }

            if (Failures.Count != 0)
                throw new Exception(string.Join(" | ", Failures));

            Debug.Log("[FerrofluidSeamValidation] PASS: chart edges and corners stay physically seamless.");
        }

        private static void RunCase(string shape, float yaw, bool withMagnet)
        {
            object config = FerrofluidValidationRuntime.CreateConfig();
            if (shape == "Sphere")
                FerrofluidValidationRuntime.SetConfig(config, "FerrofluidSphereChartYaw", yaw);
            using FerrofluidValidationRuntime primitive = new(shape, config);
            Type environment = primitive.Simulation.GetType().Assembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true)!;
            Type simulation = primitive.Simulation.GetType().Assembly.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", true)!;
            Type shapeType = primitive.Simulation.GetType().Assembly.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", true)!;
            object shapeValue = Enum.Parse(shapeType, shape);
            float roundness = (float)environment
                .GetMethod("EvaluateRoundness", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new[] { shapeValue, config })!;

            // The magnet presses just outside a chart edge so its spike field spans the seam.
            Vector2 edgeUv = new(1f, 0.23f);
            Vector3 point = (Vector3)environment
                .GetMethod("EvaluateSurfacePosition", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { shapeValue, edgeUv, roundness, yaw })!;
            Vector3 normal = (Vector3)environment
                .GetMethod("EvaluateSurfaceNormal", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { shapeValue, edgeUv, roundness, yaw })!;
            Vector3 magnetPosition = point + (normal * 0.14f);
            primitive.Invoke(
                "UpdatePrimitiveFrame",
                1f,
                withMagnet ? magnetPosition : new Vector3(0f, 0f, 1000f),
                point,
                withMagnet ? 1f : 0f,
                new Vector3(0f, 0f, -1000f),
                0f,
                Vector3.zero,
                Vector3.zero,
                1.6f);
            // Warm the spring history and fill the deformation caches before probing.
            for (int step = 0; step < 120; step++)
            {
                simulation
                    .GetMethod("RenderHeightMap", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(primitive.Simulation, new object[] { 8f });
            }

            primitive.Invoke("RenderPrimitiveDeformation");

            Material material = (Material)primitive.Get("_surfaceMaterial");
            List<Vector2> pairA = new();
            List<Vector2> pairB = new();
            for (int index = 0; index < 65; index++)
            {
                float t = 0.05f + (0.9f * index / 64f);
                pairA.Add(new Vector2(0f, t));
                pairB.Add(new Vector2(0f, 1f - t));
                pairA.Add(new Vector2(1f, t));
                pairB.Add(new Vector2(1f, 1f - t));
                pairA.Add(new Vector2(t, 0f));
                pairB.Add(new Vector2(1f - t, 0f));
                pairA.Add(new Vector2(t, 1f));
                pairB.Add(new Vector2(1f - t, 1f));
            }

            pairA.Add(new Vector2(0f, 0f));
            pairB.Add(new Vector2(1f, 1f));
            pairA.Add(new Vector2(0f, 1f));
            pairB.Add(new Vector2(1f, 0f));
            pairA.Add(new Vector2(0f, 0f));
            pairB.Add(new Vector2(0f, 1f));
            pairA.Add(new Vector2(1f, 0f));
            pairB.Add(new Vector2(1f, 1f));

            Color[] positionsA = FerrofluidSurfaceProbe.Sample(material, pairA.ToArray(), 0);
            Color[] positionsB = FerrofluidSurfaceProbe.Sample(material, pairB.ToArray(), 0);
            Color[] filmsA = FerrofluidSurfaceProbe.Sample(material, pairA.ToArray(), 1);
            Color[] filmsB = FerrofluidSurfaceProbe.Sample(material, pairB.ToArray(), 1);
            float maximumPosition = 0f;
            float maximumNormal = 0f;
            float maximumFilm = 0f;
            for (int index = 0; index < pairA.Count; index++)
            {
                Vector3 posA = new(positionsA[index].r, positionsA[index].g, positionsA[index].b);
                Vector3 posB = new(positionsB[index].r, positionsB[index].g, positionsB[index].b);
                Vector3 nrmA = new(filmsA[index].r, filmsA[index].g, filmsA[index].b);
                Vector3 nrmB = new(filmsB[index].r, filmsB[index].g, filmsB[index].b);
                maximumPosition = Mathf.Max(maximumPosition, Vector3.Distance(posA, posB));
                maximumNormal = Mathf.Max(maximumNormal, Vector3.Distance(nrmA, nrmB));
                maximumFilm = Mathf.Max(maximumFilm, Mathf.Abs(filmsA[index].a - filmsB[index].a));
                if (!Finite(positionsA[index]) || !Finite(positionsB[index]) || !Finite(filmsA[index]) || !Finite(filmsB[index])
                    || nrmA.sqrMagnitude < 0.9f || nrmB.sqrMagnitude < 0.9f
                    || positionsA[index].a < 0.9f || positionsB[index].a < 0.9f)
                    Failures.Add($"{shape} yaw={yaw} magnet={withMagnet} sample {index} uv {pairA[index]}/{pairB[index]} returned a non-finite or unrendered probe");
            }

            Debug.Log($"[FerrofluidSeamValidation] {shape} yaw={yaw} magnet={withMagnet} posMax={maximumPosition:F6} nrmMax={maximumNormal:F6} filmMax={maximumFilm:F6}");
            if (maximumPosition > 0.0001f)
                Failures.Add($"{shape} yaw={yaw} magnet={withMagnet}: chart-edge world positions differ by {maximumPosition:F6}m");
            if (maximumNormal > 0.0001f)
                Failures.Add($"{shape} yaw={yaw} magnet={withMagnet}: chart-edge normals differ by {maximumNormal:F6}");
            if (maximumFilm > 0.00001f)
                Failures.Add($"{shape} yaw={yaw} magnet={withMagnet}: chart-edge film heights differ by {maximumFilm:F6}");

            ValidateSingularNormals(material, environment, shapeValue, roundness, yaw, $"{shape} yaw={yaw} magnet={withMagnet}");
        }

        private static void ValidateSingularNormals(
            Material material,
            Type environment,
            object shape,
            float roundness,
            float yaw,
            string label)
        {
            Vector2[] centers =
            {
                new(0f, 0.5f), new(1f, 0.5f), new(0.5f, 0f), new(0.5f, 1f),
                new(0f, 0f), new(1f, 0f), new(0f, 1f), new(1f, 1f),
            };
            BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            MethodInfo position = environment.GetMethod("EvaluateSurfacePosition", flags)!;
            MethodInfo normal = environment.GetMethod("EvaluateSurfaceNormal", flags)!;
            MethodInfo encode = environment.GetMethod("EvaluateSurfaceUv", flags)!;
            Vector2[] neighbors = new Vector2[centers.Length * 4];
            const float Step = 1f / 256f;
            for (int index = 0; index < centers.Length; index++)
            {
                object[] arguments = { shape, centers[index], roundness, yaw };
                Vector3 point = (Vector3)position.Invoke(null, arguments)!;
                Vector3 outward = (Vector3)normal.Invoke(null, arguments)!;
                Vector3 reference = Mathf.Abs(outward.y) < 0.9f ? Vector3.up : Vector3.right;
                Vector3 tangent = Vector3.Cross(outward, reference).normalized;
                Vector3 bitangent = Vector3.Cross(outward, tangent);
                Vector3[] offsets = { -tangent, tangent, -bitangent, bitangent };
                for (int tap = 0; tap < offsets.Length; tap++)
                    neighbors[(index * 4) + tap] = (Vector2)encode.Invoke(null, new object[] { point + (offsets[tap] * Step), yaw })!;
            }

            Color[] displaced = FerrofluidSurfaceProbe.Sample(material, neighbors, 0);
            Color[] rendered = FerrofluidSurfaceProbe.Sample(material, centers, 1);
            float minimumDot = 1f;
            for (int index = 0; index < centers.Length; index++)
            {
                int first = index * 4;
                Vector3 tangent = Point(displaced[first + 1]) - Point(displaced[first]);
                Vector3 bitangent = Point(displaced[first + 3]) - Point(displaced[first + 2]);
                Vector3 cross = Vector3.Cross(tangent, bitangent);
                if (cross.sqrMagnitude < 0.00000000000001f || !Finite(displaced[first]) || !Finite(rendered[index]))
                {
                    Failures.Add($"{label}: physical normal probe is degenerate at {centers[index]}");
                    continue;
                }

                float agreement = Vector3.Dot(cross.normalized, Point(rendered[index]));
                minimumDot = Mathf.Min(minimumDot, agreement);
            }

            Debug.Log($"[FerrofluidSeamValidation] {label} physical-normal minimum dot={minimumDot:F6}");
            if (minimumDot < 0.98f)
                Failures.Add($"{label}: normals at chart singularities disagree with the displaced surface ({minimumDot:F6})");
        }

        private static Vector3 Point(Color value)
        {
            return new Vector3(value.r, value.g, value.b);
        }

        private static bool Finite(Color value)
        {
            return float.IsFinite(value.r) && float.IsFinite(value.g) && float.IsFinite(value.b) && float.IsFinite(value.a);
        }
    }
}
