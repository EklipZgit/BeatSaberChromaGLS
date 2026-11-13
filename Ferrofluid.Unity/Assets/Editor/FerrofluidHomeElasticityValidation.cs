using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidHomeElasticityValidation
    {
        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidHomeElasticityValidation));

        private static void RunRuntime()
        {
            string directory = FerrofluidValidationArtifacts.CreateRunDirectory("home-elasticity");
            Debug.Log($"[FerrofluidHomeElasticityValidation] captureDirectory={directory}");
            List<string> failures = new();
            foreach (string shapeName in new[] { "RoundedCube", "Sphere" })
                Validate(shapeName, directory, failures);

            if (failures.Count != 0)
                throw new Exception(string.Join(" | ", failures));

            Debug.Log("[FerrofluidHomeElasticityValidation] PASS: home pull strengthens with distance, stays stable, and respects half-face travel.");
        }

        private static void Validate(string shapeName, string directory, List<string> failures)
        {
            object config = FerrofluidValidationRuntime.CreateConfig();
            using FerrofluidValidationRuntime runtime = new(shapeName, config);
            Type assemblyType = config.GetType().Assembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true)!;
            object shape = Enum.Parse(config.GetType().Assembly.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", true)!, shapeName);
            BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            MethodInfo project = assemblyType.GetMethod("EvaluateSurfacePositionFromDirection", flags)!;
            MethodInfo encode = assemblyType.GetMethod("EvaluateSurfaceUv", flags)!;
            float roundness = (float)assemblyType.GetMethod("EvaluateRoundness", flags)!.Invoke(null, new[] { shape, config })!;
            Material simulation = (Material)runtime.Get("_simulationMaterial");
            ConfigureSpring(simulation);
            Vector4 yaw = simulation.GetVector("_PrimitiveYawCosSin");
            float degrees = Mathf.Atan2(yaw.y, yaw.x) * Mathf.Rad2Deg;
            Vector3[] homes = runtime.ReadHomes();
            int site = 0;
            for (int index = 1; index < homes.Length; index++)
            {
                if ((homes[index] - (Vector3.forward * 0.5f)).sqrMagnitude < (homes[site] - (Vector3.forward * 0.5f)).sqrMagnitude)
                    site = index;
            }

            Texture immutable = (Texture)runtime.Get("_restPositionTexture");
            Color[] original = FerrofluidValidationRuntime.Read(immutable);
            using StreamWriter writer = new(Path.Combine(directory, shapeName + ".csv"));
            writer.WriteLine("dtMilliseconds,requestedDistance,initialVelocity,beforeDistance,afterDistance,normalizedContraction");
            foreach (float milliseconds in new[] { 4f, 8f, 16f })
            {
                foreach (float requested in new[] { 0.02f, 0.05f, 0.1f, 0.2f, 0.3f, 2f })
                    Measure(milliseconds, requested, 0f);
            }

            Measure(16f, 0.02f, 100f);
            Color[] afterHomes = FerrofluidValidationRuntime.Read(immutable);
            for (int index = 0; index < original.Length; index++)
            {
                if (!original[index].Equals(afterHomes[index]))
                    throw new Exception($"{shapeName}: elasticity changed immutable home {index}");
            }

            ValidateChartCoordinates();

            void ValidateChartCoordinates()
            {
                string setting = shapeName == "RoundedCube" ? "FerrofluidCubeChartYaw" : "FerrofluidSphereChartYaw";
                Vector3[] first = new Vector3[3];
                Vector3[] offsets = { Vector3.right * 0.3f, Vector3.up * 0.3f, new Vector3(0.2f, 0.2f, 0f) };
                using StreamWriter chartWriter = new(Path.Combine(directory, shapeName + "-chart.csv"));
                chartWriter.WriteLine("yaw,sample,velocityX,velocityY,velocityZ,relativeError");
                foreach (float chartYaw in new[] { 0f, 45f })
                {
                    FerrofluidValidationRuntime.SetConfig(config, setting, chartYaw);
                    runtime.Apply(config);
                    ConfigureSpring(simulation);
                    Color[] encodedHomes = FerrofluidValidationRuntime.Read((Texture)runtime.Get("_restPositionTexture"));
                    for (int sample = 0; sample < offsets.Length; sample++)
                    {
                        Color[] state = FerrofluidValidationRuntime.CreatePositionState(encodedHomes);
                        Vector3 target = (Vector3)project.Invoke(null, new object[] { shape, homes[site] + offsets[sample], roundness })!;
                        Vector2 uv = (Vector2)encode.Invoke(null, new object[] { target, chartYaw })!;
                        state[site] = new Color((uv.x * 2f) - 1f, (uv.y * 2f) - 1f, 0f, 0f);
                        runtime.WriteState(state);
                        runtime.Invoke("Simulate", 0.25f);
                        Vector3 velocity = ReadVelocity(runtime, simulation, site);
                        float error = 0f;
                        if (chartYaw == 0f)
                            first[sample] = velocity;
                        else
                            error = (velocity - first[sample]).magnitude / first[sample].magnitude;

                        chartWriter.WriteLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0:R},{1},{2:R},{3:R},{4:R},{5:R}",
                            chartYaw,
                            sample,
                            velocity.x,
                            velocity.y,
                            velocity.z,
                            error));
                        Debug.Log($"[FerrofluidHomeElasticityValidation] {shapeName} chartYaw={chartYaw:F0} sample={sample} velocityRelativeError={error:F7}");
                        if (float.IsNaN(error) || float.IsInfinity(error) || error > 0.005f)
                            failures.Add($"{shapeName}: identical physical displacement produces chart-dependent home force ({error:F7})");
                    }
                }
            }

            void Measure(float milliseconds, float requested, float initialVelocity)
            {
                Color[] state = FerrofluidValidationRuntime.CreatePositionState(original);

                Vector3 target = (Vector3)project.Invoke(null, new object[] { shape, homes[site] + (Vector3.right * requested), roundness })!;
                Vector2 uv = (Vector2)encode.Invoke(null, new object[] { target, degrees })!;
                state[site] = new Color((uv.x * 2f) - 1f, (uv.y * 2f) - 1f, initialVelocity, 0f);
                runtime.WriteState(state);
                Vector3 before = runtime.ReadSitePositions()[site];
                if (Vector3.Distance(before, target) > 0.00001f)
                    throw new Exception($"{shapeName}: injected state does not match its physical target");

                float distance = Vector3.Distance(before, homes[site]);
                runtime.Invoke("Simulate", milliseconds);
                Vector3 after = runtime.ReadSitePositions()[site];
                float nextDistance = Vector3.Distance(after, homes[site]);
                float seconds = milliseconds * 0.001f;
                float contraction = (distance - nextDistance) / (distance * 82f * seconds * seconds);
                writer.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:R},{1:R},{2:R},{3:R},{4:R},{5:R}",
                    milliseconds,
                    requested,
                    initialVelocity,
                    distance,
                    nextDistance,
                    contraction));
                Debug.Log($"[FerrofluidHomeElasticityValidation] {shapeName} dt={milliseconds:F0} requested={requested:F3} velocity={initialVelocity:F0} before={distance:F6} after={nextDistance:F6} response={contraction:F4}");
                if (float.IsNaN(nextDistance) || float.IsInfinity(nextDistance) || nextDistance > 0.5001f)
                    failures.Add($"{shapeName}: home travel is non-finite or exceeds half a cube face");
                if (initialVelocity != 0f)
                    return;
                if (requested <= 0.3f && nextDistance > distance + 0.00001f)
                    failures.Add($"{shapeName}: restoring force moves a resting site farther from home");
                if (requested == 0.02f && (contraction < 0.4f || contraction > 2f))
                    failures.Add($"{shapeName}: small near-home movement no longer has a gentle restoring response");
                if (requested == 0.2f && contraction < 8f)
                    failures.Add($"{shapeName}: far-from-home pull is still linear instead of becoming substantially stronger");
            }
        }

        private static void ConfigureSpring(Material simulation)
        {
            simulation.SetFloat("_GasConstant", 0f);
            simulation.SetFloat("_Viscosity", 0f);
            simulation.SetFloat("_Elasticity", 82f);
            simulation.SetFloat("_Damping", 0f);
            simulation.SetFloat("_PointerStrength", 0f);
            simulation.SetFloat("_SecondaryMagneticFieldStrength", 0f);
            simulation.SetMatrix("_PrimitiveFrameRotation", Matrix4x4.identity);
            simulation.SetVector("_PrimitiveLinearVelocityDelta", Vector3.zero);
            simulation.SetVector("_PrimitivePreviousAngularVelocity", Vector3.zero);
            simulation.SetVector("_PrimitiveAngularVelocity", Vector3.zero);
        }

        private static Vector3 ReadVelocity(FerrofluidValidationRuntime runtime, Material simulation, int site)
        {
            Texture source = (Texture)runtime.Get("_currentState");
            RenderTexture output = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            try
            {
                simulation.SetTexture("_StateTex", source);
                simulation.SetFloat("_ValidationMode", 2f);
                Graphics.Blit(Texture2D.blackTexture, output, simulation, simulation.FindPass("VALIDATION"));
                Color pixel = FerrofluidValidationRuntime.Read(output)[site];
                return new Vector3(pixel.r, pixel.g, pixel.b);
            }
            finally
            {
                simulation.SetFloat("_ValidationMode", 0f);
                RenderTexture.ReleaseTemporary(output);
            }
        }
    }
}
