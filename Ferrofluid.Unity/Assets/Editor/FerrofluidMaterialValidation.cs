using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidMaterialValidation
    {
        private static readonly Vector3 FloorNormal = new(0f, -Mathf.Sqrt(0.5f), Mathf.Sqrt(0.5f));

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidMaterialValidation));

        private static void RunRuntime()
        {
            string directory = FerrofluidValidationArtifacts.CreateRunDirectory("neutral-ferrofluid-material");
            Debug.Log($"[FerrofluidMaterialValidation] captureDirectory={directory}");
            string[] textures = { "_FerrofluidRoomEnvironment", "_FerrofluidFloorReflection", "_FerrofluidParabolaReflection" };
            string[] floats = { "_FerrofluidFloorReflectionY", "_FerrofluidParabolaReflectionY", "_FerrofluidParabolaReflectionEnabled" };
            string[] vectors = { "_FerrofluidFloorReflectionBounds", "_FerrofluidParabolaReflectionBounds" };
            Texture[] oldTextures = Array.ConvertAll(textures, Shader.GetGlobalTexture);
            float[] oldFloats = Array.ConvertAll(floats, Shader.GetGlobalFloat);
            Vector4[] oldVectors = Array.ConvertAll(vectors, Shader.GetGlobalVector);
            bool oldKeyword = Shader.IsKeywordEnabled("FERROFLUID_ROOM_REFLECTIONS");
            Material material = new(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader"));
            Cubemap room = new(16, TextureFormat.RGBAFloat, false);
            Texture2D plane = new(8, 8, TextureFormat.RGBAFloat, false, true);
            using StreamWriter report = new(Path.Combine(directory, "samples.csv"));
            report.WriteLine("case,pixel,deltaR,deltaG,deltaB,expectedR,expectedG,expectedB");
            try
            {
                Shader.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                Shader.SetGlobalTexture(textures[0], room);
                Shader.SetGlobalFloat(floats[0], -1f);
                Shader.SetGlobalFloat(floats[1], -1f);
                foreach (string name in vectors)
                    Shader.SetGlobalVector(name, new Vector4(-2f, -2f, 0.25f, 0.25f));

                material.SetTexture("_EnvironmentTex", Texture2D.blackTexture);
                material.SetTexture("_ThinFilmTex", Texture2D.blackTexture);
                material.SetFloat("_FadeAlpha", 1f);
                material.SetFloat("_Exposure", 1f);
                material.SetFloat("_EmissionStrength", 0f);
                material.SetFloat("_MatteSheen", 0f);
                material.SetFloat("_PrimitiveAmbientScale", 0f);
                material.SetFloat("_Reflectivity", 0.25f);
                material.SetFloat("_FerrofluidThinFilmStrength", 0f);
                material.SetFloat("_ValidationOutput", 0f);
                material.SetVector("_ValidationWorldOffset", Vector3.zero);
                material.SetVector("_ValidationCameraPosition", Vector3.forward);
                material.SetVector("_FerrofluidReflectionStrengths", Vector4.one);

                foreach (float shape in new[] { 1f, 2f })
                {
                    material.SetFloat("_SurfaceShapeMode", shape);
                    Color baseColor = new(0.005f, 0.006f, 0.007f, 0f);
                    SetBase(material, baseColor);
                    ConfigureSource(0, Color.black);
                    Color[] dark = Capture(Vector3.forward);
                    Check($"shape-{shape}-base", new Color[dark.Length], dark, baseColor);
                    material.SetFloat("_MatteSheen", 1f);
                    material.SetFloat("_PrimitiveAmbientScale", 5f);
                    Check($"shape-{shape}-no-legacy-wash", dark, Capture(Vector3.forward), Color.black);
                    material.SetFloat("_MatteSheen", 0f);
                    material.SetFloat("_PrimitiveAmbientScale", 0f);

                    for (int source = 0; source < 3; source++)
                    {
                        Vector3 normal = source == 0 ? Vector3.forward : FloorNormal;
                        Color radiance = source == 0 ? new Color(4f, 2f, 1f) : new Color(0.1f, 1f, 4f);
                        foreach (float gain in new[] { 0f, 0.5f, 2f })
                        {
                            Vector4 strengths = Vector4.one;
                            strengths[source] = gain;
                            material.SetVector("_FerrofluidReflectionStrengths", strengths);
                            ConfigureSource(source, Color.black);
                            Color[] black = Capture(normal);
                            ConfigureSource(source, radiance);
                            Check(
                                $"shape-{shape}-source-{source}-gain-{gain}",
                                black,
                                Capture(normal),
                                radiance * (Weight(normal) * gain));
                        }

                        SetBase(material, new Color(0.2f, 0.1f, 0.05f));
                        material.SetVector("_FerrofluidReflectionStrengths", Vector4.one);
                        ConfigureSource(source, Color.black);
                        Color[] tintedBlack = Capture(normal);
                        ConfigureSource(source, radiance);
                        Check($"shape-{shape}-source-{source}-base-independent", tintedBlack, Capture(normal), radiance * Weight(normal));
                        SetBase(material, baseColor);
                    }

                    ConfigureSource(0, Color.white);
                    material.SetVector("_FerrofluidReflectionStrengths", Vector4.one);
                    Color[] outward = Capture(Vector3.forward);
                    Color[] inward = Capture(Vector3.back);
                    Check($"shape-{shape}-two-sided-reflection", outward, inward, Color.black);

                    foreach (float saturation in new[] { 0f, 0.35f, 1f })
                    {
                        Color radiance = new(0.1f, 1f, 4f);
                        material.SetVector("_FerrofluidReflectionStrengths", new Vector4(1f, 1f, 1f, saturation));
                        ConfigureSource(2, Color.black);
                        Color[] black = Capture(FloorNormal);
                        ConfigureSource(2, radiance);
                        float luminance = (0.2126f * radiance.r) + (0.7152f * radiance.g) + (0.0722f * radiance.b);
                        Color adjusted = Color.Lerp(new Color(luminance, luminance, luminance), radiance, saturation);
                        Check($"shape-{shape}-parabola-saturation-{saturation}", black, Capture(FloorNormal), adjusted * Weight(FloorNormal));
                    }

                    ConfigureSource(0, Color.black);
                    FillPlane(Color.green);
                    material.SetTexture("_ThinFilmTex", plane);
                    material.SetFloat("_FerrofluidThinFilmStrength", 0f);
                    Color[] filmOff = Capture(Vector3.forward);
                    Check($"shape-{shape}-film-off", new Color[filmOff.Length], filmOff, baseColor);
                    material.SetFloat("_FerrofluidThinFilmStrength", 1f);
                    Color[] filmOn = Capture(Vector3.forward);
                    if (filmOn[0].g <= filmOff[0].g || Mathf.Abs(filmOn[0].r - filmOff[0].r) > 0.0001f)
                        throw new Exception("Thin-film strength must be optional and preserve the independent base colour.");

                    material.SetFloat("_FerrofluidThinFilmStrength", 0f);
                    material.SetTexture("_ThinFilmTex", Texture2D.blackTexture);
                }

                void SetBase(Material target, Color value)
                    => target.SetVector("_FerrofluidBaseColor", new Vector4(value.r, value.g, value.b, 0f));

                Color[] Capture(Vector3 normal)
                    => FerrofluidHdrReflectionValidation.Capture(material, normal);

                void Check(string name, Color[] black, Color[] lit, Color expected)
                    => FerrofluidHdrReflectionValidation.ValidateDelta(report, name, black, lit, expected);

                void FillPlane(Color color)
                {
                    Color[] pixels = new Color[plane.width * plane.height];
                    Array.Fill(pixels, color);
                    plane.SetPixels(pixels);
                    plane.Apply(false, false);
                }

                void ConfigureSource(int source, Color color)
                {
                    FerrofluidHdrReflectionValidation.Fill(room, source == 0 ? color : Color.black);
                    FillPlane(color);
                    Shader.SetGlobalTexture(textures[1], source == 1 ? plane : Texture2D.blackTexture);
                    Shader.SetGlobalTexture(textures[2], source == 2 ? plane : Texture2D.blackTexture);
                    Shader.SetGlobalFloat(floats[2], source == 2 ? 1f : 0f);
                }
            }
            finally
            {
                for (int index = 0; index < textures.Length; index++)
                    Shader.SetGlobalTexture(textures[index], oldTextures[index]);

                for (int index = 0; index < floats.Length; index++)
                    Shader.SetGlobalFloat(floats[index], oldFloats[index]);

                for (int index = 0; index < vectors.Length; index++)
                    Shader.SetGlobalVector(vectors[index], oldVectors[index]);

                if (!oldKeyword)
                    Shader.DisableKeyword("FERROFLUID_ROOM_REFLECTIONS");

                Object.DestroyImmediate(material);
                Object.DestroyImmediate(room);
                Object.DestroyImmediate(plane);
            }

            Debug.Log("[FerrofluidMaterialValidation] PASS: neutral independent base, linear HDR source gains, parabola saturation, and optional thin film.");
        }

        private static float Weight(Vector3 normal)
        {
            float incidence = 1f - Mathf.Clamp01(Mathf.Abs(Vector3.Dot(normal.normalized, Vector3.forward)));
            return 0.25f * (1f + (1.62f * Mathf.Pow(incidence, 5f)));
        }
    }
}
