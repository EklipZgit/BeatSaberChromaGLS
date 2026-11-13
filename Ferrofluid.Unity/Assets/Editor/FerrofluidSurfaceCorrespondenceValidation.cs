#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // GPU regression for the shared FerrofluidCorrectSurfaceUv helper: the probe shader
    // includes the production cginc and writes the corrected UV (RG) next to the
    // original (BA); the CPU closed-form inverse of the synthetic field provides ground truth.
    internal static class FerrofluidSurfaceCorrespondenceValidation
    {
        private const int Width = 128;
        private const int Height = 64;

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidSurfaceCorrespondenceValidation));

        private static void RunRuntime()
        {
            var failures = new List<string>();
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/FerrofluidCorrespondenceProbe.shader");
            if (shader == null)
            {
                throw new Exception("[FerrofluidSurfaceCorrespondenceValidation] probe shader missing.");
            }

            var material = new Material(shader);
            var target = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create();
            var readback = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
            try
            {
                foreach (Vector3 camera in new[] { new Vector3(0.468f, 0.5f, -2f), new Vector3(0.532f, 0.5f, -2f), new Vector3(0.7f, 0.62f, -1.5f) })
                {
                    float maxError = 0f;
                    float maxUncorrected = 0f;
                    Color[] pixels = Render(material, target, readback, camera, 0.01f, false);
                    for (int y = 0; y < Height; y++)
                    {
                        for (int x = 0; x < Width; x++)
                        {
                            Color pixel = pixels[(y * Width) + x];
                            float u = (x + 0.5f) / Width;
                            // The clip.y flip pairs with the RT origin: v=0 lands on readback row 0.
                            float v = (y + 0.5f) / Height;
                            Vector2 truth = Inverse(u, v, 0.01f);
                            maxError = Math.Max(maxError, Vector2.Distance(new Vector2(pixel.r, pixel.g), truth));
                            maxUncorrected = Math.Max(maxUncorrected, Vector2.Distance(new Vector2(pixel.b, pixel.a), truth));
                        }
                    }
                    if (!float.IsFinite(maxError) || maxError > 0.000002f)
                        failures.Add($"camera {camera}: corrected UV max error {maxError:F8} exceeds 2e-6.");
                    if (!float.IsFinite(maxUncorrected) || maxUncorrected <= 0.002f)
                        failures.Add($"camera {camera}: uncorrected control error {maxUncorrected:F6} should exceed 0.002 to prove the reference differs.");
                }

                Color[] constant = Render(material, target, readback, new Vector3(0.468f, 0.5f, -2f), 0.01f, true);
                foreach (Color pixel in constant)
                {
                    if (!float.IsFinite(pixel.r + pixel.g) || Vector2.Distance(new Vector2(pixel.r, pixel.g), new Vector2(pixel.b, pixel.a)) > 0.000001f)
                    {
                        failures.Add($"constant field: corrected UV {pixel.r:F6},{pixel.g:F6} drifted from original {pixel.b:F6},{pixel.a:F6}.");
                        break;
                    }
                }

                Color[] grazing = Render(material, target, readback, new Vector3(2f, 0.5f, 0f), 0.2f, false);
                foreach (Color pixel in grazing)
                {
                    // The helper clamps each correction component to +/-2 cache texels.
                    float stepU = Mathf.Abs(pixel.r - pixel.b);
                    float stepV = Mathf.Abs(pixel.g - pixel.a);
                    if (!float.IsFinite(stepU + stepV) || stepU > (2f / 512f) + 0.000001f || stepV > (2f / 512f) + 0.000001f)
                    {
                        failures.Add($"grazing strong curvature: correction step ({stepU:F6},{stepV:F6}) exceeds the per-component 2-texel bound or is non-finite.");
                        break;
                    }
                }
            }
            finally
            {
                target.Release();
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(material);
            }

            if (failures.Count != 0)
                throw new Exception($"[FerrofluidSurfaceCorrespondenceValidation] {string.Join("; ", failures)}");
            Debug.Log("[FerrofluidSurfaceCorrespondenceValidation] PASS: corrected UV matches closed-form inverse; controls and limits hold.");
        }

        private static Color[] Render(Material material, RenderTexture target, Texture2D readback, Vector3 camera, float fieldCurvature, bool constantField)
        {
            material.SetVector("_CameraPosition", camera);
            material.SetFloat("_FieldCurvature", fieldCurvature);
            material.SetFloat("_ConstantField", constantField ? 1f : 0f);
            var commands = new CommandBuffer();
            RenderTexture previous = RenderTexture.active;
            try
            {
                commands.SetRenderTarget(target);
                commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(commands);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                readback.Apply();
                return readback.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                commands.Release();
            }
        }

        // Closed-form inverse of x = u + a*u*(1-u): the corrected u that maps the
        // rasterized mesh position back onto the field point.
        private static Vector2 Inverse(float u, float v, float a)
        {
            float discriminant = (1f + a) * (1f + a) - 4f * a * u;
            double root = Math.Sqrt(Math.Max(discriminant, 0f));
            double uCorrected = 2.0 * u / (1.0 + a + root);
            return new Vector2((float)uCorrected, v);
        }
    }
}
