using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidSpikeReflectionValidation
    {
        private const int Segments = 448;
        private const int Side = Segments + 1;
        private static readonly string OutputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "Scratch", "ferro-reflection-incidence"));

        [Serializable]
        private sealed class NormalReport
        {
            public string shape;
            public int vertices;
            public int tipVertices;
            public float maximumFilmHeight;
            public float medianDegrees;
            public float p95Degrees;
            public float tipMedianDegrees;
            public float tipP95Degrees;
        }

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidSpikeReflectionValidation));

        private static void RunRuntime()
        {
            Directory.CreateDirectory(OutputDirectory);
            string label = GetLabel();
            bool normalsAgree = true;
            foreach (string shape in new[] { "RoundedCube", "Sphere" })
            {
                object config = FerrofluidValidationRuntime.CreateConfig();
                Configure(config);
                using FerrofluidValidationRuntime runtime = new(shape, config);
                Material surface = (Material)runtime.Get("_surfaceMaterial");
                surface.SetTexture("_ThinFilmTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Generated/FerroLUT.png"));
                surface.SetTexture("_EnvironmentTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Generated/env-map-01.jpg"));
                MeshRenderer renderer = (MeshRenderer)runtime.Get("_renderer");
                renderer.transform.position = new Vector3(0f, 0.8f, 0.45f);
                renderer.transform.localScale = Vector3.one * 0.55f;
                runtime.Set("_renderEnabled", true);
                runtime.Set("_lastSimulationAt", Time.unscaledTime);
                runtime.Set("_simulationAccumulator", 0f);
                runtime.Invoke("UpdatePrimitiveFrame", 0.55f, Vector3.zero, Vector3.zero, 0f, new Vector3(0f, 0f, -1000f), 0f, Vector3.zero, Vector3.zero, 1f);
                runtime.Invoke("RenderHeightMap", 0f);
                SeedSpring(runtime);
                runtime.Invoke("RenderPrimitiveDeformation");
                Color[] positions = Probe(runtime, 0f);
                Color[] normals = Probe(runtime, 1f);
                WriteField(Path.Combine(OutputDirectory, label + "-" + shape + "-positions.rgba32f"), positions);
                WriteField(Path.Combine(OutputDirectory, label + "-" + shape + "-normals.rgba32f"), normals);
                NormalReport report = Measure(shape, positions, normals);
                normalsAgree &= report.tipP95Degrees <= 5f;
                File.WriteAllText(Path.Combine(OutputDirectory, label + "-" + shape + "-normal-report.json"), JsonUtility.ToJson(report, true));
                Debug.Log($"[FerrofluidSpikeReflectionValidation] {label}/{shape}: geometry-normal median={report.medianDegrees:F2} p95={report.p95Degrees:F2}; tip median={report.tipMedianDegrees:F2} p95={report.tipP95Degrees:F2} ({report.tipVertices} vertices, peak film={report.maximumFilmHeight:F3})");
                CaptureCases(runtime, shape, label);
            }

            if (!normalsAgree)
                throw new Exception("Spike-cap normals must agree with the displaced mesh within 5 degrees at 95% of the upper-cap vertices. Larger errors can move a reflected ray across the entire sign.");

            Debug.Log("[FerrofluidSpikeReflectionValidation] PASS: geometry-normal agreement and cube/sphere front/left/right native-source render checks.");
        }

        private static string GetLabel()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, "-ferroIncidenceLabel");
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : "baseline";
        }

        private static void Configure(object config)
        {
            // These are the user's spike-profile values, held still to isolate lighting from dynamics.
            FerrofluidValidationRuntime.SetConfig(config, "FerrofluidPrimitiveSpikeSize", 0.7f);
            FerrofluidValidationRuntime.SetConfig(config, "FerrofluidSpikeHeightScale", 0.9f);
            FerrofluidValidationRuntime.SetConfig(config, "FerrofluidPrimitiveTipRoundness", 0f);
            FerrofluidValidationRuntime.SetConfig(config, "FerrofluidPrimitiveTipCapRounding", 0.81f);
            FerrofluidValidationRuntime.SetConfig(config, "FerrofluidCubeSpikeDensity", 1.2f);
            FerrofluidValidationRuntime.SetConfig(config, "FerrofluidSphereSpikeDensity", 0.95f);
            FerrofluidValidationRuntime.SetConfig(config, "FerrofluidCubeRoundness", 0.48f);
            FerrofluidValidationRuntime.SetConfig(config, "FerrofluidReflectivity", 0.21f);
            FerrofluidValidationRuntime.SetConfig(config, "FerrofluidMatteSheen", 0.57f);
            FerrofluidValidationRuntime.SetConfig(config, "FerrofluidMatteAngle", 10f);
            FerrofluidValidationRuntime.SetConfig(config, "FerrofluidReflectionAngle", 15f);
            FerrofluidValidationRuntime.SetConfig(config, "FerrofluidMatteRotationX", 76f);
            FerrofluidValidationRuntime.SetConfig(config, "FerrofluidMatteRotationY", 295f);
            FerrofluidValidationRuntime.SetConfig(config, "FerrofluidMatteRotationZ", 174f);
            FerrofluidValidationRuntime.SetConfig(config, "FerrofluidLutScale", 3f);
            FerrofluidValidationRuntime.SetConfig(config, "FerrofluidLutOffset", 0.07f);
            FerrofluidValidationRuntime.SetConfig(config, "FerrofluidEmissionStrength", 0f);
            FerrofluidValidationRuntime.SetConfig(config, "FerrofluidExposure", 1f);
            config.GetType().GetProperty("FerrofluidSpikesEnabled").SetValue(config, true);
        }

        private static void SeedSpring(FerrofluidValidationRuntime runtime)
        {
            RenderTexture target = (RenderTexture)runtime.Get("_springCurrent");
            Texture2D upload = new(target.width, target.height, TextureFormat.RGBAFloat, false, true);
            try
            {
                Color[] values = new Color[target.width * target.height];
                for (int index = 0; index < runtime.Count; index++)
                    values[index] = new Color(0.44f, 0f, 0f, 0f);

                upload.SetPixels(values);
                upload.Apply();
                Graphics.Blit(upload, target);
            }
            finally
            {
                Object.DestroyImmediate(upload);
            }
        }

        private static Color[] Probe(FerrofluidValidationRuntime runtime, float mode)
        {
            Material material = (Material)runtime.Get("_surfaceMaterial");
            int pass = material.FindPass("FERROFLUID_WORLD_POSITION");
            Color[] result = new Color[Side * Side];
            const int batchSize = 4096;
            for (int start = 0; start < result.Length; start += batchSize)
            {
                int count = Mathf.Min(batchSize, result.Length - start);
                Vector2[] samples = new Vector2[count];
                for (int index = 0; index < count; index++)
                {
                    int vertex = start + index;
                    samples[index] = new Vector2((vertex % Side) / (float)Segments, (vertex / Side) / (float)Segments);
                }

                Color[] values = (Color[])runtime.Invoke("SampleWorldPositionPass", samples, pass, mode);
                Array.Copy(values, 0, result, start, count);
            }

            return result;
        }

        private static NormalReport Measure(string shape, Color[] positions, Color[] normals)
        {
            Vector3[] geometric = new Vector3[positions.Length];
            for (int y = 0; y < Segments; y++)
            {
                for (int x = 0; x < Segments; x++)
                {
                    int a = y * Side + x;
                    Accumulate(positions, geometric, a, a + 1, a + Side);
                    Accumulate(positions, geometric, a + 1, a + Side + 1, a + Side);
                }
            }

            float maximum = 0f;
            foreach (Color normal in normals)
                maximum = Mathf.Max(maximum, normal.a);

            var all = new System.Collections.Generic.List<float>();
            var tips = new System.Collections.Generic.List<float>();
            for (int y = 2; y < Side - 2; y++)
            {
                for (int x = 2; x < Side - 2; x++)
                {
                    int index = y * Side + x;
                    Vector3 normal = new(normals[index].r, normals[index].g, normals[index].b);
                    if (geometric[index].sqrMagnitude <= 1e-14f || normal.sqrMagnitude <= 1e-10f)
                        continue;

                    float cosine = Mathf.Abs(Vector3.Dot(geometric[index].normalized, normal.normalized));
                    float angle = Mathf.Acos(Mathf.Clamp01(cosine)) * Mathf.Rad2Deg;
                    if (float.IsNaN(angle) || float.IsInfinity(angle))
                        throw new Exception("The displaced surface produced a non-finite normal.");

                    all.Add(angle);
                    if (normals[index].a >= maximum * 0.8f)
                        tips.Add(angle);
                }
            }

            all.Sort();
            tips.Sort();
            if (tips.Count == 0 || maximum <= 0f)
                throw new Exception("The incidence fixture has no actual raised spike tips.");

            return new NormalReport
            {
                shape = shape,
                vertices = all.Count,
                tipVertices = tips.Count,
                maximumFilmHeight = maximum,
                medianDegrees = all[all.Count / 2],
                p95Degrees = all[(int)((all.Count - 1) * 0.95f)],
                tipMedianDegrees = tips[tips.Count / 2],
                tipP95Degrees = tips[(int)((tips.Count - 1) * 0.95f)],
            };
        }

        private static void Accumulate(Color[] points, Vector3[] normals, int a, int b, int c)
        {
            Vector3 p = new(points[a].r, points[a].g, points[a].b);
            Vector3 q = new(points[b].r, points[b].g, points[b].b);
            Vector3 r = new(points[c].r, points[c].g, points[c].b);
            Vector3 cross = Vector3.Cross(q - p, r - p);
            normals[a] += cross;
            normals[b] += cross;
            normals[c] += cross;
        }

        private static void WriteField(string path, Color[] values)
        {
            using BinaryWriter writer = new(File.Create(path));
            foreach (Color value in values)
            {
                writer.Write(value.r);
                writer.Write(value.g);
                writer.Write(value.b);
                writer.Write(value.a);
            }
        }

        private static void CaptureCases(FerrofluidValidationRuntime runtime, string shape, string label)
        {
            Texture savedRoom = Shader.GetGlobalTexture("_FerrofluidRoomEnvironment");
            Texture savedFeet = Shader.GetGlobalTexture("_FerrofluidFloorReflection");
            Vector4 savedBounds = Shader.GetGlobalVector("_FerrofluidFloorReflectionBounds");
            float savedY = Shader.GetGlobalFloat("_FerrofluidFloorReflectionY");
            float savedParabola = Shader.GetGlobalFloat("_FerrofluidParabolaReflectionEnabled");
            bool savedKeyword = Shader.IsKeywordEnabled("FERROFLUID_ROOM_REFLECTIONS");
            GameObject cameraObject = new("Incidence validation camera", typeof(Camera));
            GameObject floorObject = new("Native feet reference", typeof(MeshFilter), typeof(MeshRenderer));
            Mesh floor = new();
            Material floorMaterial = new(Shader.Find("Unlit/Texture"));
            RenderTexture frame = new(1024, 768, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            MeshRenderer body = (MeshRenderer)runtime.Get("_renderer");
            try
            {
                Texture2D feet = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Generated/floor-feet.asset");
                Cubemap roomOn = AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/Generated/room-reflections-on.cubemap");
                Cubemap roomOff = AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/Generated/room-reflections-off.cubemap");
                if (feet == null || roomOn == null || roomOff == null)
                    throw new Exception("Native baked feet and room pair are required for incidence render tests.");

                floor.vertices = new[] { new Vector3(-2f, 0.005f, -2f), new Vector3(-2f, 0.005f, 2f), new Vector3(2f, 0.005f, -2f), new Vector3(2f, 0.005f, 2f) };
                floor.uv = new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.one };
                floor.triangles = new[] { 0, 1, 2, 2, 1, 3 };
                floor.RecalculateBounds();
                floorObject.GetComponent<MeshFilter>().sharedMesh = floor;
                floorMaterial.mainTexture = feet;
                floorObject.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
                floorObject.layer = 31;
                body.gameObject.layer = 31;
                body.enabled = true;
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.enabled = false;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.cullingMask = 1 << 31;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.003f, 0.008f, 0.014f);
                camera.fieldOfView = 100f;
                camera.transform.position = new Vector3(0f, 1.65f, -0.55f);
                camera.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0.6f, 0.1f) - camera.transform.position);
                camera.targetTexture = frame;
                frame.Create();
                Shader.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                Shader.SetGlobalVector("_FerrofluidFloorReflectionBounds", new Vector4(-2f, -2f, 0.25f, 0.25f));
                Shader.SetGlobalFloat("_FerrofluidFloorReflectionY", 0.005f);
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionEnabled", 0f);
                foreach ((string name, float x) in new[] { ("front", 0f), ("left", -0.4f), ("right", 0.4f) })
                {
                    body.transform.position = new Vector3(x, 0.8f, 0.45f);
                    runtime.Set("_lastSimulationAt", Time.unscaledTime);
                    runtime.Set("_simulationAccumulator", 0f);
                    runtime.Invoke("UpdatePrimitiveFrame", 0.55f, Vector3.zero, Vector3.zero, 0f, new Vector3(0f, 0f, -1000f), 0f, Vector3.zero, Vector3.zero, 1f);
                    Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", roomOn);
                    Shader.SetGlobalTexture("_FerrofluidFloorReflection", Texture2D.blackTexture);
                    Color[] withoutFeet = Render(camera, frame);
                    Shader.SetGlobalTexture("_FerrofluidFloorReflection", feet);
                    Color[] withFeet = Render(camera, frame);
                    Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", roomOff);
                    Color[] withoutE = Render(camera, frame);
                    int feetChanged = Changed(withoutFeet, withFeet);
                    int signChanged = Changed(withoutE, withFeet);
                    SaveFrame(label + "-" + shape + "-" + name + "-feet-off.png", withoutFeet);
                    SaveFrame(label + "-" + shape + "-" + name + "-feet-on.png", withFeet);
                    SaveFrame(label + "-" + shape + "-" + name + "-E-off.png", withoutE);
                    Debug.Log($"[FerrofluidSpikeReflectionValidation] {shape}/{name} feet pixels={feetChanged}; E-state pixels={signChanged} (linear RGB delta >1e-7).");
                    if (feetChanged == 0 || signChanged == 0)
                        throw new Exception($"{shape}/{name}: native feet or E reflection has no response.");
                }
            }
            finally
            {
                Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", savedRoom);
                Shader.SetGlobalTexture("_FerrofluidFloorReflection", savedFeet);
                Shader.SetGlobalVector("_FerrofluidFloorReflectionBounds", savedBounds);
                Shader.SetGlobalFloat("_FerrofluidFloorReflectionY", savedY);
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionEnabled", savedParabola);
                if (!savedKeyword)
                    Shader.DisableKeyword("FERROFLUID_ROOM_REFLECTIONS");

                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(floorObject);
                Object.DestroyImmediate(floor);
                Object.DestroyImmediate(floorMaterial);
                frame.Release();
                Object.DestroyImmediate(frame);
            }
        }

        private static Color[] Render(Camera camera, RenderTexture target)
        {
            camera.Render();
            return FerrofluidValidationRuntime.Read(target);
        }

        private static int Changed(Color[] before, Color[] after)
        {
            int changed = 0;
            for (int index = 0; index < before.Length; index++)
            {
                Color delta = after[index] - before[index];
                if (float.IsNaN(delta.r) || float.IsNaN(delta.g) || float.IsNaN(delta.b))
                    throw new Exception("The reflection produced a non-finite pixel.");

                if (Mathf.Max(Mathf.Abs(delta.r), Mathf.Max(Mathf.Abs(delta.g), Mathf.Abs(delta.b))) > 0.0000001f)
                    changed++;
            }

            return changed;
        }

        private static void SaveFrame(string name, Color[] pixels)
        {
            Texture2D image = new(1024, 768, TextureFormat.RGBA32, false, true);
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
                File.WriteAllBytes(Path.Combine(OutputDirectory, name), image.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }
    }
}
