using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // Stereo regression for the thin-film view-angle rate: each eye keeps its own film direction,
    // the rate scales only the angle LUT coordinate, and rate 0 pins every pixel to one LUT column.
    internal static class FerrofluidStereoFilmValidation
    {
        private static string _directory = null!;

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidStereoFilmValidation));

        private static void RunRuntime()
        {
            var failures = new List<string>();
            _directory = FerrofluidValidationArtifacts.CreateRunDirectory("stereo-film");
            Debug.Log($"[FerrofluidStereoFilmValidation] captureDirectory={_directory}");
            Material material = new(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader"));
            Texture2D lut = null!;
            try
            {
                lut = CreateGradientLut();
                material.SetTexture("_ThinFilmTex", lut);
                material.SetTexture("_EnvironmentTex", Texture2D.blackTexture);
                material.SetFloat("_FadeAlpha", 1f);
                material.SetFloat("_Exposure", 1f);
                material.SetFloat("_EmissionStrength", 0f);
                material.SetFloat("_SurfaceShapeMode", 2f);
                material.SetFloat("_Reflectivity", 0f);
                material.SetFloat("_ThinFilmScale", 1f);
                material.SetFloat("_ThinFilmOffset", 0f);
                material.SetFloat("_FerrofluidThinFilmStrength", 4f);
                material.SetVector("_ValidationWorldOffset", Vector3.zero);
                material.SetFloat("_ValidationOutput", 0f);

                Vector3 normal = new Vector3(0.6f, 0f, 0.8f).normalized;
                Vector3 eyeLeft = new(-0.032f, 0f, 0.25f);
                Vector3 eyeRight = new(0.032f, 0f, 0.25f);

                // The shader default already runs the original rate; an explicit 1 must match it.
                material.SetVector("_ValidationCameraPosition", eyeLeft);
                Color[] defaultRate = FerrofluidShadingValidation.Capture(material, normal, normal, 0.4f, 0.4f, "film-default-rate", savePixels: false);
                material.SetFloat("_ThinFilmAngleRate", 1f);
                Color[] rateOneLeft = FerrofluidShadingValidation.Capture(material, normal, normal, 0.4f, 0.4f, "film-rate-one-left", savePixels: false);
                float defaultDelta = MaxChannelDelta(defaultRate, rateOneLeft);
                if (defaultDelta > 0.00001f)
                    failures.Add($"explicit angle rate 1 changed the default render by {defaultDelta:R}.");

                // The rate scales the LUT angle coordinate; half and double must move the bands.
                material.SetFloat("_ThinFilmAngleRate", 0.5f);
                Color[] rateHalf = FerrofluidShadingValidation.Capture(material, normal, normal, 0.4f, 0.4f, "film-rate-half", savePixels: false);
                material.SetFloat("_ThinFilmAngleRate", 2f);
                Color[] rateTwo = FerrofluidShadingValidation.Capture(material, normal, normal, 0.4f, 0.4f, "film-rate-two", savePixels: false);
                if (MaxChannelDelta(rateHalf, rateOneLeft) <= 0.005f)
                    failures.Add("angle rate 0.5 left the bands in place; the rate must scale the LUT angle coordinate.");
                if (MaxChannelDelta(rateTwo, rateOneLeft) <= 0.005f)
                    failures.Add("angle rate 2 left the bands in place; the rate must scale the LUT angle coordinate.");

                // Rate 0 pins LUT X at its leftmost column, where the gradient keeps red at zero.
                // The eyes must produce identical red there; the unscaled alpha weight may still
                // tint the other channels because it intentionally follows the real view angle.
                material.SetFloat("_ThinFilmAngleRate", 0f);
                Color[] rateZeroLeft = FerrofluidShadingValidation.Capture(material, normal, normal, 0.4f, 0.4f, "film-rate-zero-left", savePixels: false);
                material.SetVector("_ValidationCameraPosition", eyeRight);
                Color[] rateZeroRight = FerrofluidShadingValidation.Capture(material, normal, normal, 0.4f, 0.4f, "film-rate-zero-right", savePixels: false);
                float rateZeroRedDelta = MaxChannelDelta(rateZeroLeft, rateZeroRight, 0);
                if (rateZeroRedDelta > 0.00001f)
                    failures.Add($"angle rate 0 left {rateZeroRedDelta:R} of angle-dependent LUT color; the view-angle coordinate must collapse to one column.");
                if (MeanLuminance(rateZeroLeft) <= 0.001f)
                    failures.Add("angle rate 0 must still sample the LUT column instead of producing a blank film.");

                material.SetVector("_ValidationCameraPosition", eyeLeft);
                Color[] rateZeroTall = FerrofluidShadingValidation.Capture(material, normal, normal, 0.7f, 0.7f, "film-rate-zero-tall", savePixels: false);
                if (MaxChannelDelta(rateZeroLeft, rateZeroTall) <= 0.005f)
                    failures.Add("angle rate 0 must keep the LUT height coordinate; taller spikes stopped moving the film.");

                // At the original rate each eye keeps its own incidence, so the film must differ.
                material.SetFloat("_ThinFilmAngleRate", 1f);
                material.SetVector("_ValidationCameraPosition", eyeRight);
                Color[] rateOneRight = FerrofluidShadingValidation.Capture(material, normal, normal, 0.4f, 0.4f, "film-rate-one-right", savePixels: false);
                if (MaxChannelDelta(rateOneLeft, rateOneRight) <= 0.005f)
                    failures.Add("rate 1 eyes agree; each eye must keep its own film view direction.");

                // With the film disabled the rate cannot alter the reflection; a nonconstant
                // environment is required for the per-eye difference to be observable.
                material.SetTexture("_EnvironmentTex", lut);
                material.SetFloat("_FerrofluidThinFilmStrength", 0f);
                material.SetFloat("_Reflectivity", 0.75f);
                material.SetVector("_ValidationCameraPosition", eyeLeft);
                Color[] reflectionLeft = FerrofluidShadingValidation.Capture(material, normal, normal, 0.4f, 0.4f, "reflection-left", savePixels: false);
                material.SetVector("_ValidationCameraPosition", eyeRight);
                Color[] reflectionRight = FerrofluidShadingValidation.Capture(material, normal, normal, 0.4f, 0.4f, "reflection-right", savePixels: false);
                if (MaxChannelDelta(reflectionLeft, reflectionRight) <= 0.00001f)
                    failures.Add("the environment reflection must keep per-eye view directions.");
                foreach (float rate in new[] { 0f, 1f, 2f, 4f })
                {
                    material.SetFloat("_ThinFilmAngleRate", rate);
                    Color[] swept = FerrofluidShadingValidation.Capture(material, normal, normal, 0.4f, 0.4f, $"reflection-rate-{rate}", savePixels: false);
                    if (MaxChannelDelta(reflectionRight, swept) > 0.00001f)
                        failures.Add($"disabling the film must leave the reflection identical at angle rate {rate}.");
                }

                if (failures.Count != 0)
                    throw new Exception(string.Join(" | ", failures));

                Debug.Log("[FerrofluidStereoFilmValidation] PASS: per-eye film responds to the angle rate.");
            }
            finally
            {
                Object.DestroyImmediate(material);
                if (lut != null)
                    Object.DestroyImmediate(lut);
            }
        }

        private static float MaxChannelDelta(Color[] first, Color[] second, int channel = -1)
        {
            if (first.Length == 0 || first.Length != second.Length)
                throw new ArgumentException($"Captures must have equal nonzero lengths ({first.Length} versus {second.Length}).");

            float maximum = 0f;
            for (int index = 0; index < first.Length; index++)
            {
                Color a = first[index];
                Color b = second[index];
                if (float.IsNaN(a.r) || float.IsInfinity(a.r)
                    || float.IsNaN(a.g) || float.IsInfinity(a.g)
                    || float.IsNaN(a.b) || float.IsInfinity(a.b)
                    || float.IsNaN(b.r) || float.IsInfinity(b.r)
                    || float.IsNaN(b.g) || float.IsInfinity(b.g)
                    || float.IsNaN(b.b) || float.IsInfinity(b.b))
                {
                    throw new Exception($"Capture returned a non-finite pixel at index {index}.");
                }

                if (channel < 0 || channel == 0)
                    maximum = Mathf.Max(maximum, Mathf.Abs(a.r - b.r));
                if (channel < 0 || channel == 1)
                    maximum = Mathf.Max(maximum, Mathf.Abs(a.g - b.g));
                if (channel < 0 || channel == 2)
                    maximum = Mathf.Max(maximum, Mathf.Abs(a.b - b.b));
            }

            return maximum;
        }

        private static float MeanLuminance(Color[] pixels)
        {
            if (pixels.Length == 0)
                throw new ArgumentException("The capture must contain at least one pixel.");

            float total = 0f;
            foreach (Color pixel in pixels)
            {
                if (float.IsNaN(pixel.r) || float.IsInfinity(pixel.r)
                    || float.IsNaN(pixel.g) || float.IsInfinity(pixel.g)
                    || float.IsNaN(pixel.b) || float.IsInfinity(pixel.b))
                {
                    throw new Exception("Capture returned a non-finite pixel.");
                }

                total += (pixel.r * 0.2126f) + (pixel.g * 0.7152f) + (pixel.b * 0.0722f);
            }

            return total / pixels.Length;
        }

        private static Texture2D CreateGradientLut()
        {
            var texture = new Texture2D(512, 512, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[512 * 512];
            for (int y = 0; y < 512; y++)
            {
                for (int x = 0; x < 512; x++)
                {
                    float steep = Mathf.Sqrt(x / 511f);
                    pixels[(y * 512) + x] = new Color(steep, y / 511f, 1f - steep, 1f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
