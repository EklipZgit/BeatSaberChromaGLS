using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // Grazing reflections must not band when a narrow sign letter minifies along one
    // axis: the packaged cubemaps need a footprint filter (aniso) baked in, measured
    // against a supersampled reference of the same production shader and assets.
    internal static class FerrofluidReflectionMinificationValidation
    {
        private static readonly Vector3 CapturePosition = new(0f, 1.7f, -0.6096f);
        private static readonly Vector3 View = new(0f, 0f, 1f);
        private const int TargetWidth = 256;
        private const int TargetHeight = 24;
        private const int ReferenceScale = 8;
        private const double MaxRelativeError = 0.10;

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidReflectionMinificationValidation));

        private static void RunRuntime()
        {
            string[] textureNames = { "_FerrofluidRoomEnvironment", "_FerrofluidFloorReflection", "_FerrofluidParabolaReflection" };
            string[] vectorNames = { "_FerrofluidRoomCapturePosition", "_FerrofluidRoomLogoPlane", "_FerrofluidFloorReflectionBounds", "_FerrofluidParabolaReflectionBounds" };
            string[] floatNames = { "_FerrofluidRoomFloorY", "_FerrofluidFloorReflectionY", "_FerrofluidParabolaReflectionY", "_FerrofluidMenuLightCount", "_FerrofluidParabolaReflectionEnabled" };
            Texture[] oldTextures = Array.ConvertAll(textureNames, Shader.GetGlobalTexture);
            Vector4[] oldVectors = Array.ConvertAll(vectorNames, Shader.GetGlobalVector);
            float[] oldFloats = Array.ConvertAll(floatNames, Shader.GetGlobalFloat);
            bool oldRoom = Shader.IsKeywordEnabled("FERROFLUID_ROOM_REFLECTIONS");
            bool oldNone = Shader.IsKeywordEnabled("FERROFLUID_NO_REFLECTIONS");
            bool oldSrgb = GL.sRGBWrite;
            AnisotropicFiltering oldQuality = QualitySettings.anisotropicFiltering;
            var restored = new List<(Cubemap cube, FilterMode filter, int aniso)>();
            List<Object> owned = new();
            try
            {
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
                GL.sRGBWrite = false;
                Shader.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                Shader.DisableKeyword("FERROFLUID_NO_REFLECTIONS");
                Shader.SetGlobalVector("_FerrofluidRoomCapturePosition", CapturePosition);
                Shader.SetGlobalFloat("_FerrofluidRoomFloorY", 0f);
                Shader.SetGlobalVector("_FerrofluidRoomLogoPlane", new Vector4(0f, 0f, 1f, -26.1f));
                Shader.SetGlobalTexture("_FerrofluidFloorReflection", Texture2D.blackTexture);
                Shader.SetGlobalVector("_FerrofluidFloorReflectionBounds", new Vector4(-2f, -2f, 0.25f, 0.25f));
                Shader.SetGlobalFloat("_FerrofluidFloorReflectionY", 0.005f);
                Shader.SetGlobalTexture("_FerrofluidParabolaReflection", Texture2D.blackTexture);
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionEnabled", 0f);
                Shader.SetGlobalFloat("_FerrofluidMenuLightCount", 0f);

                Material material = new(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader"));
                owned.Add(material);
                material.SetFloat("_ValidationOutput", 4f);
                material.SetFloat("_SurfaceShapeMode", 2f);
                material.SetVector("_FerrofluidReflectionStrengths", new Vector4(1f, 1f, 0f, 1f));
                material.SetVector("_ValidationWorldPosition", CapturePosition);
                material.SetVector("_ValidationWorldOffset", CapturePosition);
                material.SetVector("_ValidationCameraPosition", CapturePosition + View);

                Vector3[] normals =
                {
                    CornerNormal(-0.35f, 0.35f), CornerNormal(0.35f, 0.35f),
                    CornerNormal(-0.35f, 0.9f), CornerNormal(0.35f, 0.9f),
                };
                Vector2[] uvs = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };

                List<string> failures = new();
                foreach ((string tier, string asset) in new[]
                {
                    ("low", "Assets/Generated/room-reflections-on.cubemap"),
                    ("high", "Assets/Generated/room-reflections-high-on.cubemap"),
                })
                {
                    Cubemap cube = AssetDatabase.LoadAssetAtPath<Cubemap>(asset);
                    if (cube == null)
                        throw new Exception("Required generated cubemap missing: " + asset);
                    int packagedAniso = cube.anisoLevel;
                    restored.Add((cube, cube.filterMode, cube.anisoLevel));
                    Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", cube);

                    // Target uses the packaged defaults as baked; the reference renders the
                    // same field at 8x both axes so each target can be area-averaged.
                    Color[] actual = FerrofluidShadingValidation.CaptureQuad(material, normals, uvs, TargetWidth, TargetHeight);
                    cube.filterMode = FilterMode.Trilinear;
                    cube.anisoLevel = 0;
                    Color[] reference = FerrofluidShadingValidation.CaptureQuad(
                        material,
                        normals,
                        uvs,
                        TargetWidth * ReferenceScale,
                        TargetHeight * ReferenceScale);
                    double squaredError = 0.0;
                    double referenceEnergy = 0.0;
                    for (int y = 0; y < TargetHeight; y++)
                    {
                        for (int x = 0; x < TargetWidth; x++)
                        {
                            Color expected = Color.black;
                            for (int dy = 0; dy < ReferenceScale; dy++)
                            {
                                for (int dx = 0; dx < ReferenceScale; dx++)
                                {
                                    Color sample = reference[(y * ReferenceScale + dy) * (TargetWidth * ReferenceScale) + (x * ReferenceScale + dx)];
                                    if (float.IsNaN(sample.r) || float.IsInfinity(sample.r)
                                        || float.IsNaN(sample.g) || float.IsInfinity(sample.g)
                                        || float.IsNaN(sample.b) || float.IsInfinity(sample.b)
                                        || sample.a < -1f)
                                        throw new Exception(tier + " reference produced non-finite or unrendered pixels.");
                                    expected.r += sample.r;
                                    expected.g += sample.g;
                                    expected.b += sample.b;
                                }
                            }

                            expected.r /= ReferenceScale * ReferenceScale;
                            expected.g /= ReferenceScale * ReferenceScale;
                            expected.b /= ReferenceScale * ReferenceScale;
                            Color target = actual[(y * TargetWidth) + x];
                            for (int channel = 0; channel < 3; channel++)
                            {
                                float actualChannel = target[channel];
                                float expectedChannel = expected[channel];
                                if (float.IsNaN(actualChannel) || float.IsInfinity(actualChannel)
                                    || float.IsNaN(expectedChannel) || float.IsInfinity(expectedChannel)
                                    || target.a < -1f)
                                    throw new Exception(tier + " produced non-finite or unrendered pixels.");
                                double difference = actualChannel - expectedChannel;
                                squaredError += difference * difference;
                                referenceEnergy += expectedChannel * expectedChannel;
                            }
                        }
                    }

                    if (referenceEnergy <= 0.0)
                        throw new Exception(tier + " reference contains no energy to compare against.");
                    double relativeError = Math.Sqrt(squaredError / referenceEnergy);
                    bool correct = relativeError < MaxRelativeError;
                    Debug.Log($"[FerrofluidReflectionMinificationValidation] {tier} packagedAniso={packagedAniso} relativeError={relativeError:F4} correct={correct}");
                    if (!correct)
                        failures.Add($"{tier}: relativeError={relativeError:F4} >= {MaxRelativeError}");
                }

                if (failures.Count > 0)
                    throw new Exception("Reflection minification assertions failed: " + string.Join(" | ", failures));
                Debug.Log("[FerrofluidReflectionMinificationValidation] PASS both tiers within " + MaxRelativeError + " of the 8x8 supersampled reference.");
            }
            finally
            {
                foreach ((Cubemap cube, FilterMode filter, int aniso) in restored)
                {
                    cube.filterMode = filter;
                    cube.anisoLevel = aniso;
                }

                QualitySettings.anisotropicFiltering = oldQuality;
                GL.sRGBWrite = oldSrgb;
                for (int index = 0; index < oldTextures.Length; index++)
                    Shader.SetGlobalTexture(textureNames[index], oldTextures[index]);
                for (int index = 0; index < oldVectors.Length; index++)
                    Shader.SetGlobalVector(vectorNames[index], oldVectors[index]);
                for (int index = 0; index < oldFloats.Length; index++)
                    Shader.SetGlobalFloat(floatNames[index], oldFloats[index]);
                if (oldRoom)
                    Shader.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                else
                    Shader.DisableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                if (oldNone)
                    Shader.EnableKeyword("FERROFLUID_NO_REFLECTIONS");
                else
                    Shader.DisableKeyword("FERROFLUID_NO_REFLECTIONS");
                foreach (Object instance in owned)
                    Object.DestroyImmediate(instance);
            }
        }

        private static Vector3 CornerNormal(float u, float v)
        {
            Vector3 desiredRay = new Vector3(u, v, 1f).normalized;
            return (View + desiredRay).normalized;
        }
    }
}
