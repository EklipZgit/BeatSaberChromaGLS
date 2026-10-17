using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace ChromaGLS.FerrofluidBundle
{
    // Read back real shader output while sweeping both magnets, including the cube's top-right edge and chart folds.
    internal static class FerrofluidInteractionValidation
    {
        public static void Run()
        {
            try
            {
                string repository = Directory.GetParent(Application.dataPath).Parent.FullName;
                Assembly plugin = Assembly.LoadFrom(Path.Combine(repository, "ChromaGLS/bin/Release-1.44.1/net48/ChromaGLS.dll"));
                Type simulation = plugin.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", true);
                Type shapeType = plugin.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", true);
                ValidateRaycaster(plugin, shapeType);
                MethodInfo rests = simulation.GetMethod("EvaluatePrimitiveRestPositions", BindingFlags.Static | BindingFlags.NonPublic);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSimulation.shader");
                if (ShaderUtil.ShaderHasError(shader))
                {
                    throw new Exception("The simulation shader failed to compile.");
                }

                ValidateSpringContinuity(shader);
                foreach (string shapeName in new[] { "RoundedCube", "Sphere" })
                {
                    object shape = Enum.Parse(shapeType, shapeName);
                    Vector2[] positions = (Vector2[])rests.Invoke(null, new object[] { shape, 576, 0.48f });
                    int heightSize = (int)simulation.GetField("PrimitiveHeightMapSize", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
                    int candidateSize = (int)simulation.GetField("PrimitiveCandidateMapSize", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
                    Validate(shader, positions, Convert.ToInt32(shape), 48, heightSize, candidateSize);
                    Vector2[] densePositions = (Vector2[])rests.Invoke(null, new object[] { shape, 2304, 0.48f });
                    Validate(shader, densePositions, Convert.ToInt32(shape), 4, heightSize, candidateSize);
                }

                Debug.Log("[FerrofluidInteractionValidation] PASS: shader fields remain finite and accelerated height output matches the exhaustive shader during both magnet sweeps.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void Profile()
        {
            string repository = Directory.GetParent(Application.dataPath).Parent.FullName;
            Assembly plugin = Assembly.LoadFrom(Path.Combine(repository, "ChromaGLS/bin/Release-1.44.1/net48/ChromaGLS.dll"));
            Type simulation = plugin.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", true);
            Type shapeType = plugin.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", true);
            object shape = Enum.Parse(shapeType, "RoundedCube");
            Vector2[] positions = (Vector2[])simulation.GetMethod("EvaluatePrimitiveRestPositions", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { shape, 1444, 0.48f });
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSimulation.shader");
            Validate(shader, positions, Convert.ToInt32(shape), 0, 384, 64, true);
        }

        private static void ValidateSpringContinuity(Shader shader)
        {
            Material material = new Material(shader);
            Texture2D rest = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            Texture2D state = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            RenderTexture result = CreateTarget(1);
            rest.SetPixel(0, 0, new Color(0.99f, -0.3f, 0f, 0f));
            rest.Apply(false, false);
            material.SetInt("_ParticleCount", 1);
            material.SetInt("_ParticleTextureSize", 1);
            material.SetFloat("_SurfaceShapeMode", 1f);
            material.SetFloat("_SurfaceRoundness", 0.48f);
            material.SetVector("_PrimitiveYawCosSin", new Vector2(1f, 0f));
            material.SetVector("_DomainScale", new Vector2(1f / 0.168f, 1f / 0.168f));
            material.SetFloat("_H", 1f);
            material.SetFloat("_PointerRadius", 5f);
            material.SetFloat("_Elasticity", 80f);
            material.SetFloat("_Damping", 7f);
            material.SetFloat("_DeltaTimeMilliseconds", 8f);
            material.SetTexture("_RestPositionTex", rest);
            material.SetTexture("_StateTex", state);
            material.SetTexture("_SiteSurfaceTex", Texture2D.whiteTexture);
            material.SetTexture("_DensityPressureTex", Texture2D.whiteTexture);
            material.SetTexture("_ForceTex", Texture2D.blackTexture);
            state.SetPixel(0, 0, new Color(0.832f - 0.000001f, 0.3f, 0f, 0f));
            state.Apply(false, false);
            Graphics.Blit(Texture2D.blackTexture, result, material, 2);
            Color before = Read(result)[0];
            state.SetPixel(0, 0, new Color(0.832f + 0.000001f, 0.3f, 0f, 0f));
            state.Apply(false, false);
            Graphics.Blit(Texture2D.blackTexture, result, material, 2);
            Color after = Read(result)[0];
            if (Mathf.Abs(before.r - after.r) > 0.0001f || Mathf.Abs(before.g - after.g) > 0.0001f
                || Mathf.Abs(before.b - after.b) > 0.0001f || Mathf.Abs(before.a - after.a) > 0.0001f)
            {
                throw new Exception("The shader spring changes abruptly across the pressure kernel boundary.");
            }

            RenderTexture.active = null;
            foreach (UnityEngine.Object resource in new UnityEngine.Object[] { material, rest, state, result })
            {
                UnityEngine.Object.DestroyImmediate(resource);
            }

            Debug.Log("[FerrofluidInteractionValidation] native shader spring stays continuous across the pressure kernel boundary.");
        }

        private static void ValidateRaycaster(Assembly plugin, Type shapeType)
        {
            GameObject primitive = new GameObject("Ferrofluid raycast validation");
            primitive.transform.position = new Vector3(-1f, 0.275f, 1f);
            primitive.transform.localScale = Vector3.one * 0.55f;
            Type effectType = plugin.GetType("ChromaGLS.Settings.FerrofluidPrimitiveEffect", true);
            Component effect = primitive.AddComponent(effectType);
            effectType.GetField("_shape", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(effect, Enum.Parse(shapeType, "RoundedCube"));
            effectType.GetField("_roundness", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(effect, 0.48f);
            Type raycasterType = plugin.GetType("ChromaGLS.Settings.FerrofluidPrimitiveRaycaster", true);
            BaseRaycaster raycaster = (BaseRaycaster)primitive.AddComponent(raycasterType);
            raycasterType.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(raycaster, new object[] { effect });
            PointerEventData pointer = new PointerEventData(null);
            pointer.pointerCurrentRaycast = new RaycastResult
            {
                worldPosition = new Vector3(-1f, 0.275f, 0f),
                worldNormal = Vector3.forward,
            };
            List<RaycastResult> results = new List<RaycastResult>();
            raycaster.Raycast(pointer, results);
            if (results.Count != 1 || Mathf.Abs(results[0].worldPosition.z - 0.725f) > 0.00001f
                || Mathf.Abs(results[0].distance - 0.725f) > 0.00001f || results[0].gameObject != primitive)
            {
                throw new Exception("The native menu raycaster did not return the cube's front surface.");
            }

            UnityEngine.Object.DestroyImmediate(primitive);
            Debug.Log("[FerrofluidInteractionValidation] native cube raycast ends at the front surface.");
        }

        private static void Validate(Shader shader, Vector2[] positions, int shape, int frameCount, int heightSize, int candidateSize, bool profile = false)
        {
            int stride = Mathf.NextPowerOfTwo(Mathf.CeilToInt(Mathf.Sqrt(positions.Length)));
            Material accelerated = new Material(shader);
            Material exhaustive = new Material(shader);
            exhaustive.EnableKeyword("FERROFLUID_BRUTE_FORCE_HEIGHT_MAP");
            Texture2D rest = new Texture2D(stride, stride, TextureFormat.RGBAFloat, false, true);
            rest.filterMode = FilterMode.Point;
            Color[] pixels = new Color[stride * stride];
            for (int index = 0; index < positions.Length; index++)
            {
                pixels[index] = new Color(positions[index].x, positions[index].y, 0f, 0f);
            }

            rest.SetPixels(pixels);
            rest.Apply(false, false);
            RenderTexture current = CreateTarget(stride);
            RenderTexture next = CreateTarget(stride);
            RenderTexture sites = CreateTarget(stride);
            RenderTexture restSurface = CreateTarget(stride);
            RenderTexture candidates = CreateTarget(candidateSize * 3, candidateSize * 2);
            RenderTexture height = CreateTarget(heightSize);
            RenderTexture reference = CreateTarget(heightSize);
            Graphics.Blit(rest, current);
            foreach (Material material in new[] { accelerated, exhaustive })
            {
                material.SetInt("_ParticleCount", positions.Length);
                material.SetInt("_ParticleTextureSize", stride);
                material.SetFloat("_SurfaceShapeMode", shape);
                material.SetFloat("_SurfaceRoundness", 0.48f);
                material.SetVector("_PrimitiveYawCosSin", new Vector2(Mathf.Cos(Mathf.PI / 4f), Mathf.Sin(Mathf.PI / 4f)));
                material.SetVector("_DomainScale", new Vector2(15.36f, 15.36f));
                material.SetFloat("_H", 1f);
                material.SetFloat("_PrimitiveCellMetric", 0.64f * 0.26f / 1.4f);
                material.SetFloat("_HeightMapScale", 0.9f);
                material.SetFloat("_SmoothFactor", 0.02f);
                material.SetFloat("_HeightFactor", 0.38f);
                material.SetFloat("_SpikeFactor", 1f / 3f);
                material.SetFloat("_SpikeBaseCutoff", 0.1f);
                material.SetFloat("_PrimitiveTipRoundness", 0.2f);
                material.SetFloat("_CoarseCandidateSize", candidateSize);
                material.SetFloat("_PointerRadius", 5.1f);
                material.SetFloat("_MaximumSlide", 4.1f);
                material.SetFloat("_SlideFraction", 0.5f);
                material.SetFloat("_SlideFalloffExponent", 0.7f);
                material.SetFloat("_Elasticity", 80f);
                material.SetFloat("_Damping", 7f);
                material.SetFloat("_DeltaTimeMilliseconds", 8f);
                material.SetTexture("_RestPositionTex", rest);
                material.SetTexture("_DensityPressureTex", Texture2D.whiteTexture);
                material.SetTexture("_ForceTex", Texture2D.blackTexture);
                material.SetTexture("_SiteSurfaceTex", sites);
                material.SetTexture("_RestSurfaceTex", restSurface);
                material.SetTexture("_CoarseCandidateTex", candidates);
            }

            accelerated.SetTexture("_StateTex", current);
            exhaustive.SetTexture("_StateTex", current);
            Graphics.Blit(Texture2D.blackTexture, restSurface, accelerated, 6);
            Graphics.Blit(Texture2D.blackTexture, sites, accelerated, 4);
            if (profile)
            {
                ProfileHeightPipeline(accelerated, exhaustive, sites, candidates, height, reference,
                    new UnityEngine.Object[] { accelerated, exhaustive, rest, current, next, sites, restSurface, candidates, height, reference });
                return;
            }

            float largestError = 0f;
            for (int frame = 0; frame < frameCount; frame++)
            {
                float phase = frame / (float)(frameCount - 1);
                accelerated.SetVector("_PrimitiveMagnetPosition", new Vector3(0.53f, 0.53f, Mathf.Lerp(-0.5f, 0.5f, phase)));
                accelerated.SetVector("_PrimitiveHandPosition", new Vector3(-0.6f, 0.1f, Mathf.Lerp(0.4f, -0.4f, phase)));
                accelerated.SetFloat("_PointerStrength", 0.7f);
                accelerated.SetFloat("_SecondaryMagneticFieldStrength", 1f);
                Graphics.Blit(Texture2D.blackTexture, next, accelerated, 2);
                RenderTexture previous = current;
                current = next;
                next = previous;
                accelerated.SetTexture("_StateTex", current);
                exhaustive.SetTexture("_StateTex", current);
                Graphics.Blit(Texture2D.blackTexture, sites, accelerated, 4);
                Graphics.Blit(Texture2D.blackTexture, candidates, accelerated, 5);
                Graphics.Blit(Texture2D.blackTexture, height, accelerated, 3);
                Graphics.Blit(Texture2D.blackTexture, reference, exhaustive, 3);
                Color[] actual = Read(height);
                Color[] expected = Read(reference);
                for (int index = 0; index < actual.Length; index++)
                {
                    float error = Mathf.Max(Mathf.Abs(actual[index].r - expected[index].r), Mathf.Abs(actual[index].a - expected[index].a));
                    if (float.IsNaN(error) || float.IsInfinity(error))
                    {
                        throw new Exception($"Non-finite height: shape={shape} frame={frame} texel={index}");
                    }

                    largestError = Mathf.Max(largestError, error);
                }
            }

            Debug.Log($"[FerrofluidInteractionValidation] shape={shape} sites={positions.Length} maximum height error={largestError}");
            if (largestError > 0.00001f)
            {
                throw new Exception($"Candidate search omitted contributing spikes: shape={shape}, maximum error={largestError}");
            }

            RenderTexture.active = null;
            foreach (UnityEngine.Object resource in new UnityEngine.Object[] { accelerated, exhaustive, rest, current, next, sites, restSurface, candidates, height, reference })
            {
                UnityEngine.Object.DestroyImmediate(resource);
            }
        }

        private static void ProfileHeightPipeline(Material material, Material exhaustive, RenderTexture sites, RenderTexture candidates, RenderTexture height, RenderTexture reference, UnityEngine.Object[] resources)
        {
            Profiler.enabled = true;
            Profiler.SetAreaEnabled(ProfilerArea.GPU, true);
            string[] names = { "Ferrofluid Sites", "Ferrofluid Candidates", "Ferrofluid Height" };
            CustomSampler[] samplers = new CustomSampler[names.Length];
            Recorder[] recorders = new Recorder[names.Length];
            for (int index = 0; index < names.Length; index++)
            {
                samplers[index] = CustomSampler.Create(names[index], true);
                recorders[index] = samplers[index].GetRecorder();
                recorders[index].enabled = true;
            }

            Vector2Int[] sizes = { new Vector2Int(384, 64), new Vector2Int(256, 64), new Vector2Int(256, 32), new Vector2Int(256, 16) };
            int configuration = 0;
            int frame = 0;
            int samples = 0;
            double[] milliseconds = new double[names.Length];
            CommandBuffer commands = new CommandBuffer { name = "Ferrofluid height pipeline GPU profile" };
            EditorApplication.CallbackFunction update = null;
            update = () =>
            {
                try
                {
                    if (frame == 0)
                    {
                        Vector2Int size = sizes[configuration];
                        height.Release();
                        height.width = size.x;
                        height.height = size.x;
                        height.Create();
                        reference.Release();
                        reference.width = size.x;
                        reference.height = size.x;
                        reference.Create();
                        candidates.Release();
                        candidates.width = size.y * 3;
                        candidates.height = size.y * 2;
                        candidates.Create();
                        material.SetTexture("_CoarseCandidateTex", candidates);
                        material.SetVector("_CoarseCandidateTex_TexelSize", new Vector4(1f / candidates.width, 1f / candidates.height, candidates.width, candidates.height));
                        material.SetFloat("_CoarseCandidateSize", size.y);
                        commands.Clear();
                        RenderTexture[] targets = { sites, candidates, height };
                        int[] passes = { 4, 5, 3 };
                        for (int index = 0; index < names.Length; index++)
                        {
                            commands.BeginSample(samplers[index]);
                            commands.Blit(Texture2D.blackTexture, targets[index], material, passes[index]);
                            commands.EndSample(samplers[index]);
                        }
                    }

                    if (frame == 15)
                    {
                        Graphics.Blit(Texture2D.blackTexture, reference, exhaustive, 3);
                        Color[] actual = Read(height);
                        Color[] expected = Read(reference);
                        float maximumError = 0f;
                        for (int index = 0; index < actual.Length; index++)
                        {
                            maximumError = Mathf.Max(maximumError, Mathf.Abs(actual[index].r - expected[index].r), Mathf.Abs(actual[index].a - expected[index].a));
                        }

                        if (maximumError > 0.00001f || float.IsNaN(maximumError))
                        {
                            throw new Exception($"GPU profile height disagrees with exhaustive output: {maximumError}.");
                        }
                    }

                    // GPU recorders arrive three frames late, so discard warm-up after every target-size change.
                    if (frame >= 24 && recorders[0].gpuSampleBlockCount == 1 && recorders[1].gpuSampleBlockCount == 1 && recorders[2].gpuSampleBlockCount == 1)
                    {
                        samples++;
                        for (int index = 0; index < names.Length; index++)
                        {
                            milliseconds[index] += recorders[index].gpuElapsedNanoseconds / 1000000.0;
                        }
                    }

                    Graphics.ExecuteCommandBuffer(commands);
                    EditorApplication.QueuePlayerLoopUpdate();
                    frame++;
                    if (frame < 80)
                    {
                        return;
                    }

                    if (samples < 32)
                    {
                        throw new Exception($"GPU profiling did not produce enough timestamp samples: {samples}.");
                    }

                    Debug.Log($"[FerrofluidGpuProfile] device={SystemInfo.graphicsDeviceName} height={sizes[configuration].x} candidates={sizes[configuration].y} samples={samples} sitesMs={milliseconds[0] / samples:F4} candidatesMs={milliseconds[1] / samples:F4} heightMs={milliseconds[2] / samples:F4}");
                    configuration++;
                    frame = 0;
                    samples = 0;
                    Array.Clear(milliseconds, 0, milliseconds.Length);
                    if (configuration < sizes.Length)
                    {
                        return;
                    }

                    EditorApplication.update -= update;
                    commands.Dispose();
                    Profiler.enabled = false;
                    foreach (UnityEngine.Object resource in resources)
                    {
                        UnityEngine.Object.DestroyImmediate(resource);
                    }

                    EditorApplication.Exit(0);
                }
                catch (Exception exception)
                {
                    EditorApplication.update -= update;
                    commands.Dispose();
                    Profiler.enabled = false;
                    Debug.LogException(exception);
                    EditorApplication.Exit(1);
                }
            };
            EditorApplication.update += update;
        }

        private static RenderTexture CreateTarget(int size, int height = 0)
        {
            RenderTexture target = new RenderTexture(size, height == 0 ? size : height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.filterMode = FilterMode.Point;
            target.wrapMode = TextureWrapMode.Clamp;
            target.Create();
            return target;
        }

        private static Color[] Read(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D texture = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
            texture.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
            Color[] pixels = texture.GetPixels();
            UnityEngine.Object.DestroyImmediate(texture);
            RenderTexture.active = previous;
            return pixels;
        }
    }
}
