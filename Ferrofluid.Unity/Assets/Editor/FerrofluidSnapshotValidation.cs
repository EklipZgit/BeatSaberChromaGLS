using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // Native round-trip check for the F12 snapshot export: seeds known nonzero state into the real
    // GPU textures, runs the tweak-only ExportSnapshot, and proves the raw float payload, decoded
    // spike fields, exact applied uniform values, and rendered-site probe all read back correctly.
    internal static class FerrofluidSnapshotValidation
    {
        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidSnapshotValidation));

        private static IEnumerator RunRuntime()
        {
            string repository = Directory.GetParent(Application.dataPath)!.Parent!.FullName;
            string directory = Path.GetFullPath(Path.Combine(
                repository,
                "..",
                "Scratch",
                "ferro-snapshots",
                "native-validation-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture)));
            Directory.CreateDirectory(directory);
            Debug.Log($"[FerrofluidSnapshotValidation] captureDirectory={directory}");
            List<string> failures = new();
            foreach (string shape in new[] { "Sphere", "RoundedCube", "Torus" })
                ValidateShape(shape, directory, failures);

            // The screenshot leg needs one rendered frame so the capture sees real content.
            yield return new WaitForEndOfFrame();
            ValidateScreenshot(failures);
            ValidateScreenshotEncoding(failures);

            if (failures.Count != 0)
                throw new Exception(string.Join(" | ", failures));

            Debug.Log("[FerrofluidSnapshotValidation] PASS: seeded textures, decoded spikes, exact applied uniforms, and rendered sites verified. Screenshot capture SKIPPED (batchmode has no frame buffer).");
        }

        private static void ValidateShape(string shapeName, string directory, List<string> failures)
        {
            object config = FerrofluidValidationRuntime.CreateConfig();
            FerrofluidValidationRuntime.SetConfig(config, "GasConstant", 99f);
            FerrofluidValidationRuntime.SetConfig(config, "ParticleMass", 4.7f);
            using FerrofluidValidationRuntime runtime = new(shapeName, config);
            runtime.Apply(config);

            Transform rendererTransform = ((Renderer)runtime.Get("_renderer")).transform;
            rendererTransform.SetPositionAndRotation(new Vector3(-3f, -4f, -5f), Quaternion.Euler(15f, 30f, -20f));
            rendererTransform.localScale = Vector3.one * 0.55f;

            int count = runtime.Count;
            var homes = FerrofluidValidationRuntime.Read((Texture)runtime.Get("_restPositionTexture"));
            // _currentState is particleTextureSize², which can exceed particleCount.
            Texture stateTexture = (Texture)runtime.Get("_currentState");
            var seeded = new Color[stateTexture.width * stateTexture.height];
            for (int index = 0; index < seeded.Length; index++)
            {
                seeded[index] = index < count
                    ? new Color(
                        Mathf.Clamp(homes[index].r + 0.05f, -1f, 1f),
                        Mathf.Clamp(homes[index].g - 0.03f, -1f, 1f),
                        0.3f,
                        -0.4f)
                    : new Color(0f, 0f, 0.3f, -0.4f);
            }

            Rect torusRect = new(-4.5f, -2f, 9f, 4f);
            Matrix4x4 curved = Matrix4x4.TRS(new Vector3(0.1f, 0.2f, 0f), Quaternion.identity, Vector3.one);
            if (shapeName == "Torus")
            {
                // The rendered-site probe shades through the surface material, so the torus needs
                // its geometry/texture uniforms applied exactly as a live UpdateFrame applies them.
                // UpdateFrame also ticks the simulation, so the seeded textures land afterwards.
                runtime.Invoke(
                    "UpdateFrame",
                    torusRect,
                    new Vector2(0.5f, 0.5f),
                    0f,
                    true,
                    curved,
                    curved.inverse,
                    2.5f,
                    -1f,
                    1f,
                    Vector3.zero,
                    Vector2.zero,
                    0f,
                    Vector3.zero,
                    new Vector3(0f, 0f, -1f),
                    Vector3.zero,
                    0f,
                    new Vector3(0f, 0f, -1000f),
                    Vector2.zero,
                    0f,
                    1f);
                runtime.Invoke("RenderHeightMap", 8f);
            }
            else
            {
                runtime.Invoke(
                    "UpdatePrimitiveFrame",
                    0.55f,
                    new Vector3(0.3f, 0.1f, 0.2f),
                    new Vector3(0.2f, 0.5f, 0f),
                    0.5f,
                    new Vector3(0f, 0f, -1000f),
                    0f,
                    Vector3.zero,
                    Vector3.zero,
                    1f);
            }

            runtime.WriteState(seeded);
            SeedSpring(runtime, count);

            Material simulationMaterial = (Material)runtime.Get("_simulationMaterial");
            simulationMaterial.SetFloat("_PrimitiveHomeSoftRange", 1.234f);
            simulationMaterial.SetVector("_PrimitiveYawCosSin", new Vector4(1f, 0f, 0.25f, -0.75f));
            Material surfaceMaterial = (Material)runtime.Get("_surfaceMaterial");
            surfaceMaterial.SetFloat("_Exposure", 0.777f);

            RenderTexture previousActive = RenderTexture.active;
            object torusChart = shapeName == "Torus" ? CreateTorusChart(runtime) : null!;
            object snapshot = runtime.Invoke("ExportSnapshot", torusChart, null);
            if (!ReferenceEquals(RenderTexture.active, previousActive))
                failures.Add($"{shapeName}: RenderTexture.active was not restored after ExportSnapshot.");

            dynamic document = snapshot;
            int particleCount = (int)document["particleCount"];
            if (particleCount != count)
                failures.Add($"{shapeName}: particleCount exported {particleCount}, expected {count}.");

            dynamic textures = document["textures"];
            if ((string)textures["_currentState"]["materialProperty"] != "_StateTex")
                failures.Add($"{shapeName}: _currentState must record its _StateTex binding.");
            if (textures["_springCurrent"]["materialProperty"] == null)
                failures.Add($"{shapeName}: _springCurrent must record its material binding.");

            dynamic data = textures["_currentState"]["data"];
            for (int index = 0; index < 3; index++)
            {
                float[] expected = { seeded[index].r, seeded[index].g, 0.3f, -0.4f };
                for (int channel = 0; channel < 4; channel++)
                {
                    if (Mathf.Abs((float)data[(index * 4) + channel] - expected[channel]) > 0.001f)
                        failures.Add($"{shapeName}: _currentState[{index}].{channel} exported {(float)data[(index * 4) + channel]}, expected {expected[channel]}.");
                }
            }

            dynamic spikes = document["spikes"];
            if ((int)spikes.Count != count)
                failures.Add($"{shapeName}: exported {spikes.Count} spikes, expected {count}.");
            float springScale = (float)spikes[0]["springHeightScale"];
            if (Mathf.Abs(springScale - 0.25f) > 0.001f)
                failures.Add($"{shapeName}: springHeightScale exported {springScale}, expected seeded 0.25.");
            if (Mathf.Abs((float)spikes[0]["springHeightScaleVelocityPerSecond"] - (-0.5f)) > 0.001f)
                failures.Add($"{shapeName}: spring scale velocity did not round-trip.");

            float homeSoftRange = (float)document["simulationMaterial"]["floats"]["_PrimitiveHomeSoftRange"];
            if (Mathf.Abs(homeSoftRange - 1.234f) > 0.0001f)
                failures.Add($"{shapeName}: declared _PrimitiveHomeSoftRange exported {homeSoftRange}, expected the applied 1.234.");
            float exposure = (float)document["surfaceMaterial"]["floats"]["_Exposure"];
            if (Mathf.Abs(exposure - 0.777f) > 0.0001f)
                failures.Add($"{shapeName}: declared _Exposure exported {exposure}, expected the applied 0.777.");
            float gas = (float)document["config"]["GasConstant"];
            if (Mathf.Abs(gas - 99f) > 0.001f)
                failures.Add($"{shapeName}: config GasConstant exported {gas}, expected configured 99.");

            // Exact applied uniform values: uniform-only Set arguments recorded at their call sites.
            dynamic applied = document["appliedUniforms"];
            if (applied == null)
            {
                failures.Add($"{shapeName}: appliedUniforms missing; exact Set arguments were not recorded.");
            }
            else
            {
                float mass = (float)applied["simulationMaterial"]["floats"]["_Mass"];
                if (Mathf.Abs(mass - 4.7f) > 0.0001f)
                    failures.Add($"{shapeName}: applied _Mass exported {mass}, expected the clamped ParticleMass 4.7.");
                float kernelRadius = (float)applied["simulationMaterial"]["floats"]["_H"];
                if (Mathf.Abs(kernelRadius - 1f) > 0.0001f)
                    failures.Add($"{shapeName}: applied _H exported {kernelRadius}, expected the fixed kernel radius 1.");
                float fluidScale = (float)applied["surfaceMaterial"]["floats"]["_FluidHeightScale"];
                if (fluidScale <= 0f)
                    failures.Add($"{shapeName}: applied _FluidHeightScale exported {fluidScale}, expected the actual scale-composed value at 0.55 world scale.");
                if (shapeName == "Torus")
                {
                    var canvasMatrix = applied["surfaceMaterial"]["matrices"]["_LocalToCurvedCanvas"];
                    if (Mathf.Abs((float)canvasMatrix[3] - curved.m03) > 0.001f
                        || Mathf.Abs((float)canvasMatrix[7] - curved.m13) > 0.001f)
                        failures.Add($"{shapeName}: applied _LocalToCurvedCanvas did not preserve the UpdateFrame matrix.");
                }
                else
                {
                    var localMagnet = applied["simulationMaterial"]["vectors"]["_PrimitiveMagnetPosition"];
                    if (localMagnet == null || Mathf.Abs((float)localMagnet[0] - 0.3f) < 0.0001f)
                        failures.Add($"{shapeName}: applied _PrimitiveMagnetPosition must be the transformed local point, not the world argument.");
                }
            }

            var rendered = spikes[0]["renderedSiteWorldPosition"];
            if (rendered == null || rendered.Type.ToString() == "Null")
            {
                failures.Add($"{shapeName}: renderedSiteWorldPosition missing; the world-position probe did not run.");
            }
            else
            {
                Vector3 world = new((float)rendered[0], (float)rendered[1], (float)rendered[2]);
                if (float.IsNaN(world.x) || float.IsNaN(world.y) || float.IsNaN(world.z))
                    failures.Add($"{shapeName}: renderedSiteWorldPosition is NaN ({world}).");
                var localToken = spikes[0]["renderedSiteLocalPosition"];
                if (localToken == null || localToken.Type.ToString() == "Null")
                {
                    failures.Add($"{shapeName}: renderedSiteLocalPosition missing.");
                }
                else
                {
                    Vector3 local = new((float)localToken[0], (float)localToken[1], (float)localToken[2]);
                    Vector3 expected = rendererTransform.InverseTransformPoint(world);
                    if (Vector3.Distance(local, expected) > 0.001f)
                        failures.Add($"{shapeName}: local/world transform mismatch ({local} vs {expected}).");
                }
            }

            var normal = spikes[0]["renderedSiteWorldNormal"];
            if (normal == null || normal.Type.ToString() == "Null")
            {
                failures.Add($"{shapeName}: renderedSiteWorldNormal missing.");
            }
            else
            {
                Vector3 n = new((float)normal[0], (float)normal[1], (float)normal[2]);
                if (Mathf.Abs(n.magnitude - 1f) > 0.01f)
                    failures.Add($"{shapeName}: renderedSiteWorldNormal is not unit length ({n.magnitude}).");
            }

            if (spikes[0]["renderedBasisBaseWorldPosition"] == null || spikes[0]["renderedBasisBaseWorldPosition"].Type.ToString() == "Null")
                failures.Add($"{shapeName}: renderedBasisBaseWorldPosition missing.");
            if (spikes[0]["surfaceDisplacementHeightWorld"] == null || spikes[0]["surfaceDisplacementHeightWorld"].Type.ToString() == "Null")
                failures.Add($"{shapeName}: surfaceDisplacementHeightWorld missing.");

            string json = snapshot.ToString()!;
            if (json.Length < 1000 || !json.Contains("springHeightScale"))
                failures.Add($"{shapeName}: serialized snapshot JSON looks incomplete.");

            File.WriteAllText(Path.Combine(directory, $"native-{shapeName.ToLowerInvariant()}-snapshot.json"), json);
        }

        private static void SeedSpring(FerrofluidValidationRuntime runtime, int count)
        {
            RenderTexture spring = (RenderTexture)runtime.Get("_springCurrent");
            var pixels = new Color[spring.width * spring.height];
            for (int index = 0; index < count; index++)
                pixels[index] = new Color(0.25f + (index * 0.0001f), -0.5f, 0f, 0f);

            var texture = new Texture2D(spring.width, spring.height, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
            try
            {
                texture.SetPixels(pixels);
                texture.Apply();
                Graphics.Blit(texture, spring);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        private static object CreateTorusChart(FerrofluidValidationRuntime runtime)
        {
            Type geometry = runtime.Simulation.GetType().Assembly.GetType("ChromaGLS.Settings.FerrofluidTorusChartGeometry", true)!;
            return Activator.CreateInstance(
                geometry,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new object[] { new Vector2(0f, 0f), new Vector2(4.5f, 2f), 1f, -1f, 1.5f },
                null)!;
        }

        // CaptureScreenshotAsTexture returns a transparent-alpha frame buffer; the production
        // encoder must force opaque alpha or viewers that honor alpha show a blank image.
        private static void ValidateScreenshotEncoding(List<string> failures)
        {
            Texture2D source = null!;
            Texture2D decoded = null!;
            try
            {
                Type captureType = Assembly.Load("ChromaGLS")
                    .GetType("ChromaGLS.Settings.FerrofluidSnapshotCapture", true)!;
                byte[] alphas = { 0, 1, 69, 255, 0, 200, 77, 33 };
                source = new Texture2D(4, 2, TextureFormat.RGBA32, false);
                var pixels = new Color32[8];
                for (int index = 0; index < 8; index++)
                {
                    pixels[index] = new Color32(
                        (byte)(10 + (index * 31)),
                        (byte)(20 + (index * 17)),
                        (byte)(30 + (index * 11)),
                        alphas[index]);
                }

                source.SetPixels32(pixels);
                source.Apply();
                MethodInfo encoder = captureType
                    .GetMethod("EncodeScreenshotPng", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
                byte[] png = (byte[])encoder.Invoke(null, new object[] { source })!;
                decoded = new Texture2D(2, 2);
                if (!ImageConversion.LoadImage(decoded, png))
                {
                    failures.Add("EncodeScreenshotPng output failed to decode.");
                    return;
                }

                if (decoded.width != 4 || decoded.height != 2)
                    failures.Add($"EncodeScreenshotPng decoded {decoded.width}x{decoded.height}, expected 4x2.");
                Color32[] result = decoded.GetPixels32();
                for (int index = 0; index < 8 && index < result.Length; index++)
                {
                    if (result[index].a != 255)
                        failures.Add($"EncodeScreenshotPng preserved alpha {result[index].a} at pixel {index}; every PNG pixel must be opaque (source alpha {alphas[index]}).");
                    if (result[index].r != pixels[index].r || result[index].g != pixels[index].g || result[index].b != pixels[index].b)
                        failures.Add($"EncodeScreenshotPng changed RGB at pixel {index}: ({result[index].r},{result[index].g},{result[index].b}) vs ({pixels[index].r},{pixels[index].g},{pixels[index].b}).");
                }
            }
            finally
            {
                if (source != null)
                    Object.DestroyImmediate(source);
                if (decoded != null)
                    Object.DestroyImmediate(decoded);
            }
        }

        private static void ValidateScreenshot(List<string> failures)
        {
            Texture2D shot = null!;
            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                byte[] png = ImageConversion.EncodeToPNG(shot);
                var decoded = new Texture2D(2, 2);
                if (!ImageConversion.LoadImage(decoded, png) || decoded.width < 4 || decoded.height < 4)
                    failures.Add($"screenshot PNG decoded to {decoded.width}x{decoded.height}.");
                Object.DestroyImmediate(decoded);
            }
            catch (Exception error)
            {
                // Batchmode has no window swap chain, so CaptureScreenshotAsTexture legitimately
                // fails here; the in-game F12 path runs against a real frame buffer.
                Debug.LogWarning($"[FerrofluidSnapshotValidation] screenshot capture unavailable in batchmode: {error.Message}");
            }
            finally
            {
                if (shot != null)
                    Object.DestroyImmediate(shot);
            }
        }
    }
}
