using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidValleyValidation
    {
        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidValleyValidation));

        private static void RunRuntime()
        {
            List<string> failures = new();
            foreach (string shape in new[] { "RoundedCube", "Sphere" })
                Validate(shape, failures);

            if (failures.Count != 0)
                throw new Exception(string.Join(" | ", failures));

            Debug.Log("[FerrofluidValleyValidation] PASS: valley slopes converge smoothly across height-map cells.");
        }

        private static void Validate(string shapeName, List<string> failures)
        {
            object config = FerrofluidValidationRuntime.CreateConfig();
            FerrofluidValidationRuntime.SetConfig(config, $"Ferrofluid{(shapeName == "RoundedCube" ? "Cube" : "Sphere")}ChartYaw", 0f);
            using FerrofluidValidationRuntime runtime = new(shapeName, config);
            Assembly plugin = runtime.Simulation.GetType().Assembly;
            Type environment = plugin.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true);
            Type shapeType = plugin.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", true);
            object shape = Enum.Parse(shapeType, shapeName);
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            float roundness = (float)environment.GetMethod("EvaluateRoundness", flags).Invoke(null, new[] { shape, config });
            Vector2 center = new(0.6f, 0.6f);
            Vector3 point = (Vector3)environment.GetMethod("EvaluateSurfacePosition", flags)
                .Invoke(null, new object[] { shape, center, roundness, 0f });
            Vector3 normal = (Vector3)environment.GetMethod("EvaluateSurfaceNormal", flags)
                .Invoke(null, new object[] { shape, center, roundness, 0f });
            runtime.Invoke(
                "UpdatePrimitiveFrame",
                1f, point + normal * 0.14f, point, 1f,
                new Vector3(0f, 0f, -1000f), 0f, Vector3.zero, Vector3.zero, 1f);
            for (int step = 0; step < 120; step++)
                runtime.Invoke("RenderHeightMap", 8f);

            runtime.Invoke("RenderPrimitiveDeformation");
            Material material = (Material)runtime.Get("_surfaceMaterial");
            Texture heightMap = (Texture)runtime.Get("_heightMap");
            float epsilon = 0.08f / heightMap.width;
            List<Vector2> centers = new();
            List<Vector2> samples = new();
            int first = Mathf.FloorToInt(center.x * heightMap.width) - 20;
            for (int y = first; y < first + 40; y++)
            {
                for (int x = first; x < first + 40; x++)
                {
                    Vector2 uv = new((x + 0.5f) / heightMap.width, (y + 0.5f) / heightMap.height);
                    centers.Add(uv);
                    samples.Add(uv);
                    samples.Add(uv - Vector2.right * epsilon);
                    samples.Add(uv + Vector2.right * epsilon);
                    samples.Add(uv - Vector2.right * (epsilon * 0.5f));
                    samples.Add(uv + Vector2.right * (epsilon * 0.5f));
                }
            }

            Color[] positions = FerrofluidSurfaceProbe.Sample(material, samples.ToArray(), 0);
            Color[] film = FerrofluidSurfaceProbe.Sample(material, centers.ToArray(), 1);
            int selected = 0;
            double coarseSum = 0;
            double fineSum = 0;
            for (int index = 0; index < centers.Count; index++)
            {
                int offset = index * 5;
                Vector3 p = Point(positions[offset]);
                float coarse = (Point(positions[offset + 1]) + Point(positions[offset + 2]) - p * 2f).magnitude / epsilon;
                float fine = (Point(positions[offset + 3]) + Point(positions[offset + 4]) - p * 2f).magnitude / (epsilon * 0.5f);
                if (film[index].a > 0.06f || coarse < 0.03f || coarse > 5f)
                    continue;

                selected++;
                coarseSum += coarse;
                fineSum += fine;
            }

            double ratio = fineSum / Math.Max(coarseSum, 0.000001);
            Debug.Log($"[FerrofluidValleyValidation] {shapeName} valleySamples={selected} coarseSlopeJump={coarseSum / Math.Max(selected, 1):F6} fineSlopeJump={fineSum / Math.Max(selected, 1):F6} convergenceRatio={ratio:F6}");
            if (selected < 4)
                throw new Exception($"{shapeName}: the probe did not cover enough curved valley samples.");
            if (ratio > 0.7)
                failures.Add($"{shapeName}: valley slopes stay discontinuous as the sampling interval shrinks ({ratio:F6}).");

            CaptureMagnetSweep(runtime, material, point, normal, centers, shapeName, failures);
        }

        private static Vector3 Point(Color pixel) => new(pixel.r, pixel.g, pixel.b);

        private static void CaptureMagnetSweep(
            FerrofluidValidationRuntime runtime,
            Material material,
            Vector3 point,
            Vector3 normal,
            List<Vector2> centers,
            string shape,
            List<string> failures)
        {
            string directory = FerrofluidValidationArtifacts.CreateRunDirectory("valley-render-" + shape);
            material.SetTexture("_ThinFilmTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Generated/FerroLUT.png"));
            material.SetTexture("_EnvironmentTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Generated/env-map-01.jpg"));
            Renderer renderer = (Renderer)runtime.Get("_renderer");
            int previousLayer = renderer.gameObject.layer;
            renderer.gameObject.layer = 31;
            GameObject cameraObject = new("Valley render validation", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>();
            RenderTexture target = RenderTexture.GetTemporary(512, 512, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear, 4);
            try
            {
                camera.enabled = false;
                camera.cullingMask = 1 << 31;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.nearClipPlane = 0.001f;
                camera.farClipPlane = 10f;
                camera.fieldOfView = 40f;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.targetTexture = target;
                camera.allowHDR = true;
                camera.allowMSAA = true;
                camera.transform.position = point + (normal * 0.55f) + (Vector3.right * 0.08f);
                camera.transform.LookAt(point, Vector3.forward);
                Vector2[] probes = centers.ToArray();
                for (int warm = 0; warm < 340; warm++)
                    Step(point);

                Color[] previousNormals = null;
                Color[] previousPixels = null;
                float maximumNormalStep = 0f;
                float maximumPixelStep = 0f;
                float maximumP99PixelStep = 0f;
                float maximumLuminance = 0f;
                float[] pixelSteps = new float[target.width * target.height];
                for (int frame = 0; frame < 60; frame++)
                {
                    Step(point + (Vector3.right * (frame * 0.00005f)));
                    Color[] normals = FerrofluidSurfaceProbe.Sample(material, probes, 1);
                    camera.Render();
                    Color[] pixels = FerrofluidValidationRuntime.Read(target);
                    for (int index = 0; index < pixels.Length; index++)
                    {
                        Color pixel = pixels[index];
                        if (!float.IsFinite(pixel.r) || !float.IsFinite(pixel.g) || !float.IsFinite(pixel.b))
                            throw new Exception($"{shape}: the magnet sweep rendered a non-finite color.");
                        maximumLuminance = Mathf.Max(maximumLuminance, pixel.maxColorComponent);
                        if (previousPixels != null)
                        {
                            Color delta = pixel - previousPixels[index];
                            float pixelStep = Mathf.Max(Mathf.Abs(delta.r), Mathf.Max(Mathf.Abs(delta.g), Mathf.Abs(delta.b)));
                            maximumPixelStep = Mathf.Max(maximumPixelStep, pixelStep);
                            pixelSteps[index] = pixelStep;
                        }
                    }

                    if (previousPixels != null)
                    {
                        Array.Sort(pixelSteps);
                        maximumP99PixelStep = Mathf.Max(
                            maximumP99PixelStep,
                            pixelSteps[Mathf.CeilToInt(pixelSteps.Length * 0.99f) - 1]);
                    }

                    if (previousNormals != null)
                    {
                        for (int index = 0; index < normals.Length; index++)
                        {
                            if (normals[index].a < 0.06f && previousNormals[index].a < 0.06f)
                            {
                                float dot = Mathf.Clamp(Vector3.Dot(Point(normals[index]), Point(previousNormals[index])), -1f, 1f);
                                maximumNormalStep = Mathf.Max(maximumNormalStep, Mathf.Acos(dot) * Mathf.Rad2Deg);
                            }
                        }
                    }

                    SaveFrame(pixels, frame);
                    previousNormals = normals;
                    previousPixels = pixels;
                }

                Debug.Log($"[FerrofluidValleyValidation] {shape} gentleSweep maxValleyNormalStep={maximumNormalStep:F6} maxLinearPixelStep={maximumPixelStep:F6} p99LinearPixelStep={maximumP99PixelStep:F6} maxColor={maximumLuminance:F6} captures={directory}");
                if (maximumLuminance < 0.00001f)
                    failures.Add($"{shape}: the full surface did not render during the gentle magnet sweep.");
                // Reflection edges can cross individual pixels. Track valley normals and broad color changes separately.
                if (maximumNormalStep > 5f || maximumP99PixelStep > 0.02f)
                    failures.Add($"{shape}: gentle magnet motion still produces abrupt valley normals or reflected colors.");

                camera.transform.position = point + (normal * 2.2f) + (Vector3.right * 0.32f);
                camera.transform.LookAt(point, Vector3.forward);
                camera.Render();
                SaveFrame(FerrofluidValidationRuntime.Read(target), 60);
            }
            finally
            {
                renderer.gameObject.layer = previousLayer;
                camera.targetTexture = null;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(cameraObject);
            }

            void Step(Vector3 desired)
            {
                runtime.Invoke(
                    "UpdatePrimitiveFrame",
                    1f, desired + (normal * 0.14f), desired, 1f,
                    new Vector3(0f, 0f, -1000f), 0f, Vector3.zero, Vector3.zero, 1f);
                runtime.Invoke("AdvancePrimitiveSimulation", 1f / 170f);
                runtime.Invoke("RenderPrimitiveDeformation");
            }

            void SaveFrame(Color[] pixels, int frame)
            {
                Texture2D image = new(512, 512, TextureFormat.RGBA32, false, true);
                try
                {
                    Color[] display = new Color[pixels.Length];
                    for (int index = 0; index < pixels.Length; index++)
                    {
                        display[index] = pixels[index].gamma;
                        display[index].a = 1f;
                    }

                    image.SetPixels(display);
                    image.Apply();
                    File.WriteAllBytes(Path.Combine(directory, $"frame-{frame:D2}.png"), image.EncodeToPNG());
                }
                finally
                {
                    Object.DestroyImmediate(image);
                }
            }
        }
    }
}
