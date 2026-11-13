using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidParabolicAntialiasingValidation
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidParabolicAntialiasingValidation));

        private static void RunRuntime()
        {
            RenderTexture previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            Texture previousMap = Shader.GetGlobalTexture("_FerrofluidParabolaReflection");
            Vector4 previousBounds = Shader.GetGlobalVector("_FerrofluidParabolaReflectionBounds");
            float previousPlane = Shader.GetGlobalFloat("_FerrofluidParabolaReflectionY");
            float previousEnabled = Shader.GetGlobalFloat("_FerrofluidParabolaReflectionEnabled");
            object cache = null;
            Mesh core = null;
            Mesh halo = null;
            Material referenceMaterial = null;
            RenderTexture high = null;
            RenderTexture single = null;
            RenderTexture resolved = null;
            try
            {
                object config = FerrofluidValidationRuntime.CreateConfig();
                // These are the thin strip and wide fog settings from the user's 20:27 F12 capture.
                SetConfig("Brightness", 0.336f);
                SetConfig("Red", 0.442f);
                SetConfig("Green", 0f);
                SetConfig("Blue", 3.396304f);
                SetConfig("WidthScale", 3.023f);
                SetConfig("LightWidthScale", 0.3190165f);
                SetConfig("BloomWidthScale", 1.855044f);
                SetConfig("BloomBrightness", 0.914f);
                SetConfig("HdrGlowThreshold", 1.509796f);
                SetConfig("HdrGlowWidthScale", 0.06300099f);
                SetConfig("HdrGlowBrightness", 1.725904f);
                Assembly plugin = config.GetType().Assembly;
                Type settingsType = plugin.GetType("ChromaGLS.Settings.ParabolicFloorLightSettings", true);
                object settings = settingsType.GetMethod("FromConfig", Static).Invoke(null, new[] { config });
                Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true);
                object enclosure = enclosureType.GetConstructor(
                    Instance,
                    null,
                    new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                    null).Invoke(new object[]
                    {
                        new Vector3(0f, 1.2750001f, 0.150000215f),
                        4.2f,
                        new Vector3(0f, 1.2750001f, -4.2f),
                        Vector3.forward,
                        0f,
                    });
                Color[] actual = null;
                int callbacks = 0;
                Action<RenderTexture, RenderTexture> processor = (source, destination) =>
                {
                    callbacks++;
                    actual = FerrofluidValidationRuntime.Read(source);
                    Graphics.Blit(source, destination);
                };
                Type cacheType = plugin.GetType("ChromaGLS.Settings.ParabolicFloorReflectionCache", true);
                cache = cacheType.GetConstructor(Instance, null, new[] { typeof(Shader), processor.GetType() }, null).Invoke(new object[]
                {
                    AssetDatabase.LoadAssetAtPath<Shader>("Assets/ParabolicFloorReflection.shader"),
                    processor,
                });
                cacheType.GetMethod("Regenerate", Instance).Invoke(cache, new[] { enclosure, Vector3.zero, Vector3.forward, settings });
                Check(callbacks == 1 && actual != null, "The cache must pass its antialiased source through bloom exactly once per generation.");
                RenderTexture published = (RenderTexture)Shader.GetGlobalTexture("_FerrofluidParabolaReflection");
                Check(published.width == 1024 && published.height == 1024 && published.format == RenderTextureFormat.RGB111110Float, "Coverage antialiasing must retain the shared 1024 packed-HDR allocation.");
                Vector4 bounds = Shader.GetGlobalVector("_FerrofluidParabolaReflectionBounds");
                Check(Mathf.Abs((1f / bounds.z) - 28.0396f) < 0.05f, "The regression must retain the user's wide fog footprint rather than hiding aliasing by cropping it.");
                Type geometry = plugin.GetType("ChromaGLS.Settings.ParabolicFloorLightGeometry", true);
                object[] arguments = { enclosure, Vector3.zero, Vector3.forward, settings, null };
                Check((bool)geometry.GetMethod("TryCreateConfigured", Static).Invoke(null, arguments), "Captured floor-light geometry must be valid.");
                object data = arguments[4];
                int segments = (int)geometry.GetField("SegmentCount", Static).GetRawConstantValue();
                int[] allTriangles = (int[])Field(data, "Triangles");
                int[] top = new int[segments * 6];
                for (int segment = 0; segment < segments; segment++)
                    Array.Copy(allTriangles, segment * 24, top, segment * 6, 6);

                core = new Mesh
                {
                    vertices = (Vector3[])Field(data, "Vertices"),
                    colors = (Color[])Field(data, "Colors"),
                    triangles = top,
                };
                object glow = geometry.GetMethod("CreateGlow", Static).Invoke(null, new[] { enclosure, data, settings });
                halo = new Mesh
                {
                    vertices = (Vector3[])Field(glow, "Vertices"),
                    colors = (Color[])Field(glow, "Colors"),
                    triangles = (int[])Field(glow, "Triangles"),
                };
                high = Target(4096, true);
                single = Target(1024, false);
                resolved = Target(1024, false);
                Material bake = (Material)cacheType.GetField("_material", Instance).GetValue(cache);
                // The reference draws the same production geometry/material at 4x resolution.
                // Mip 2 averages sixteen source samples without changing hue or strip width.
                bake.SetVector("_BakeSample", new Vector4(0f, 0f, 1f, 0f));
                Draw(single);
                Color[] point = FerrofluidValidationRuntime.Read(single);
                Draw(high);
                high.GenerateMips();
                referenceMaterial = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/ParabolicCoverageReference.shader"));
                Graphics.Blit(high, resolved, referenceMaterial);
                Color[] reference = FerrofluidValidationRuntime.Read(resolved);
                float peak = 0f;
                foreach (Color color in reference)
                    peak = Mathf.Max(peak, color.b);

                double pointError = 0d;
                double actualError = 0d;
                int edgePixels = 0;
                for (int y = 1; y < 1023; y++)
                {
                    for (int x = 1; x < 1023; x++)
                    {
                        int index = y * 1024 + x;
                        float gradient = Mathf.Max(
                            Mathf.Abs(reference[index + 1].b - reference[index - 1].b),
                            Mathf.Abs(reference[index + 1024].b - reference[index - 1024].b));
                        if (gradient < peak * 0.1f)
                            continue;

                        double baselineDelta = point[index].b - reference[index].b;
                        double candidateDelta = actual[index].b - reference[index].b;
                        pointError += baselineDelta * baselineDelta;
                        actualError += candidateDelta * candidateDelta;
                        edgePixels++;
                    }
                }

                string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "Scratch", "ferro-parabola-aliasing", "coverage"));
                Directory.CreateDirectory(output);
                Save(Path.Combine(output, "point.png"), point, peak);
                Save(Path.Combine(output, "cache.png"), actual, peak);
                Save(Path.Combine(output, "reference.png"), reference, peak);
                Debug.Log($"[FerrofluidParabolicAntialiasingValidation] edgePixels={edgePixels} pointSquaredError={pointError:R} cacheSquaredError={actualError:R} ratio={actualError / pointError:R} output={output}");
                Check(edgePixels > 128 && pointError > 0.001d, "The captured thin strip must expose a measurable point-rasterization failure.");
                Check(actualError < pointError * 0.6d, "The saved light source must reduce strip-edge coverage error by at least 40% versus one point sample, without increasing texture memory.");
                Debug.Log("[FerrofluidParabolicAntialiasingValidation] PASS: smoother strip coverage, unchanged 1024 packed HDR, full wide fog and one bloom invocation.");

                void SetConfig(string name, float value)
                    => FerrofluidValidationRuntime.SetConfig(config, "ParabolicFloorLight" + name, value);

                void Draw(RenderTexture target)
                {
                    GL.sRGBWrite = false;
                    RenderTexture.active = target;
                    GL.Clear(false, true, Color.clear);
                    cacheType.GetMethod("Draw", Instance).Invoke(cache, new[]
                    {
                        core,
                        settingsType.GetMethod("EvaluateColor", Instance).Invoke(settings, null),
                        settingsType.GetMethod("EvaluateBloomMultiplier", Instance).Invoke(settings, null),
                    });
                    cacheType.GetMethod("Draw", Instance).Invoke(cache, new[]
                    {
                        halo,
                        settingsType.GetMethod("EvaluateGlowColor", Instance).Invoke(settings, null),
                        (object)0f,
                    });
                }
            }
            finally
            {
                if (cache is IDisposable disposable)
                    disposable.Dispose();

                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgb;
                foreach (RenderTexture target in new[] { high, single, resolved })
                {
                    if (target == null)
                        continue;

                    target.Release();
                    Object.DestroyImmediate(target);
                }

                foreach (Object resource in new Object[] { core, halo, referenceMaterial })
                {
                    if (resource != null)
                        Object.DestroyImmediate(resource);
                }

                Shader.SetGlobalTexture("_FerrofluidParabolaReflection", previousMap);
                Shader.SetGlobalVector("_FerrofluidParabolaReflectionBounds", previousBounds);
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionY", previousPlane);
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionEnabled", previousEnabled);
            }
        }

        private static object Field(object value, string name) => value.GetType().GetField(name, Instance).GetValue(value);

        private static RenderTexture Target(int size, bool mips)
        {
            RenderTexture texture = new RenderTexture(size, size, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                useMipMap = mips,
                autoGenerateMips = false,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.Create();
            return texture;
        }

        private static void Save(string path, Color[] pixels, float peak)
        {
            Texture2D image = new Texture2D(1024, 1024, TextureFormat.RGBA32, false, true);
            Texture2D hdr = new Texture2D(1024, 1024, TextureFormat.RGBAHalf, false, true);
            try
            {
                hdr.SetPixels(pixels);
                hdr.Apply();
                File.WriteAllBytes(Path.ChangeExtension(path, ".exr"), hdr.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));
                Color[] display = new Color[pixels.Length];
                for (int index = 0; index < pixels.Length; index++)
                {
                    display[index] = (pixels[index] / peak).gamma;
                    display[index].a = 1f;
                }

                image.SetPixels(display);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(hdr);
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }
    }
}
