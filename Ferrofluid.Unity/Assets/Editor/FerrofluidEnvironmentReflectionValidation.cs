using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidEnvironmentReflectionValidation
    {
        private const string RoomKeyword = "FERROFLUID_ROOM_REFLECTIONS";
        private const string RoomTexture = "_FerrofluidRoomEnvironment";
        private static readonly Vector3[] Directions =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back,
        };

        private static readonly Color[] Colors =
        {
            Color.red, Color.cyan, Color.green, Color.magenta, Color.blue, new Color(1f, 1f, 0f),
        };

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidEnvironmentReflectionValidation));

        private static IEnumerator RunRuntime()
        {
            Texture previous = Shader.GetGlobalTexture(RoomTexture);
            Texture previousFloor = Shader.GetGlobalTexture("_FerrofluidFloorReflection");
            Vector4 previousBounds = Shader.GetGlobalVector("_FerrofluidFloorReflectionBounds");
            float previousPlaneY = Shader.GetGlobalFloat("_FerrofluidFloorReflectionY");
            bool enabled = Shader.IsKeywordEnabled(RoomKeyword);
            Cubemap room = new(16, TextureFormat.RGBA32, true);
            Material material = new(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader"));
            try
            {
                // Bind a black floor so all room-direction assertions stay floor-free.
                Shader.SetGlobalTexture("_FerrofluidFloorReflection", Texture2D.blackTexture);
                Shader.SetGlobalVector("_FerrofluidFloorReflectionBounds", Vector4.zero);
                Shader.SetGlobalFloat("_FerrofluidFloorReflectionY", 0f);

                for (int face = 0; face < Colors.Length; face++)
                {
                    Color[] pixels = new Color[16 * 16];
                    Array.Fill(pixels, Colors[face]);
                    room.SetPixels(pixels, (CubemapFace)face);
                }

                room.Apply(true);
                Shader.SetGlobalTexture(RoomTexture, room);
                Shader.EnableKeyword(RoomKeyword);
                material.SetTexture("_EnvironmentTex", Texture2D.blackTexture);
                material.SetTexture("_ThinFilmTex", Texture2D.blackTexture);
                material.SetFloat("_FadeAlpha", 1f);
                material.SetFloat("_Exposure", 1f);
                material.SetFloat("_EmissionStrength", 0f);
                material.SetFloat("_PrimitiveAmbientScale", 0f);
                material.SetFloat("_MatteSheen", 0f);
                material.SetFloat("_Reflectivity", 1f);
                material.SetFloat("_ReflectionAngle", 90f);
                object config = FerrofluidValidationRuntime.CreateConfig();
                Type configType = config.GetType();
                material.SetVector(
                    "_ReflectionRotation",
                    new Vector3(
                        (float)configType.GetProperty("FerrofluidReflectionRotationX").GetValue(config),
                        (float)configType.GetProperty("FerrofluidReflectionRotationY").GetValue(config),
                        (float)configType.GetProperty("FerrofluidReflectionRotationZ").GetValue(config)));

                int samples = 0;
                foreach (int shape in new[] { 0, 1, 2 })
                {
                    material.SetFloat("_SurfaceShapeMode", shape);
                    foreach (Vector3 eye in new[] { Vector3.forward, Vector3.right, Vector3.up })
                    {
                        for (int face = 0; face < Directions.Length; face++)
                        {
                            Color pixel = SampleReflection(material, room, eye, Directions[face]);
                            AssertChannels(pixel, Colors[face], $"shape={shape} eye={eye} direction={Directions[face]}");
                            samples++;
                        }
                    }
                }

                ValidateMinification(material);
                IEnumerator baked = ValidateBakedEnvironment(material);
                while (baked.MoveNext())
                    yield return baked.Current;
                Debug.Log($"[FerrofluidEnvironmentReflectionValidation] PASS: {samples} world-direction reflections agree across torus, cube, sphere and camera directions.");
            }
            finally
            {
                Shader.SetGlobalTexture(RoomTexture, previous);
                Shader.SetGlobalTexture("_FerrofluidFloorReflection", previousFloor);
                Shader.SetGlobalVector("_FerrofluidFloorReflectionBounds", previousBounds);
                Shader.SetGlobalFloat("_FerrofluidFloorReflectionY", previousPlaneY);
                if (!enabled)
                    Shader.DisableKeyword(RoomKeyword);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(room);
            }
        }

        private static void ValidateMinification(Material material)
        {
            Cubemap stripes = new(256, TextureFormat.RGBA32, true);
            try
            {
                Color[] source = new Color[256 * 256];
                for (int y = 0; y < 256; y++)
                {
                    for (int x = 0; x < 256; x++)
                        source[(y * 256) + x] = x % 2 == 0 ? Color.white : Color.black;
                }

                for (int face = 0; face < 6; face++)
                    stripes.SetPixels(source, (CubemapFace)face);
                stripes.Apply(true);
                stripes.filterMode = FilterMode.Trilinear;
                Shader.SetGlobalTexture(RoomTexture, stripes);
                Vector3 left = (Vector3.forward + new Vector3(-0.8f, 0f, 1f).normalized).normalized;
                Vector3 right = (Vector3.forward + new Vector3(0.8f, 0f, 1f).normalized).normalized;
                Color[] previous = null;
                float minimum = float.MaxValue;
                float maximum = 0f;
                float temporalStep = 0f;
                for (int frame = 0; frame < 9; frame++)
                {
                    material.SetVector("_ValidationCameraPosition", (Vector3.forward + (Vector3.right * (frame * 0.00002f))).normalized);
                    material.SetFloat("_ValidationOutput", 2f);
                    Color[] pixels = FerrofluidShadingValidation.Capture(
                        material,
                        left,
                        right,
                        0f,
                        0f,
                        "room-minification",
                        8,
                        false);
                    for (int index = 0; index < pixels.Length; index++)
                    {
                        minimum = Mathf.Min(minimum, pixels[index].r);
                        maximum = Mathf.Max(maximum, pixels[index].r);
                        if (previous != null)
                            temporalStep = Mathf.Max(temporalStep, Mathf.Abs(pixels[index].r - previous[index].r));
                    }

                    previous = pixels;
                }

                Debug.Log($"[FerrofluidEnvironmentReflectionValidation] room minification range={minimum:F6}/{maximum:F6} temporalStep={temporalStep:F6}");
                if (!(minimum > 0.2f && maximum < 0.9f && maximum - minimum < 0.05f && temporalStep < 0.01f))
                    throw new Exception("Minified cubemap stripes are not filtered or remain unstable under tiny camera movement.");
            }
            finally
            {
                Object.DestroyImmediate(stripes);
            }
        }

        private static Color SampleReflection(Material material, Texture room, Vector3 eye, Vector3 direction)
        {
            Shader.SetGlobalTexture(RoomTexture, room);
            return Sample(material, eye, direction);
        }

        private static IEnumerator ValidateBakedEnvironment(Material material)
        {
            Cubemap bakedRoom = null;
            Material probeMaterial = null;
            RenderTexture probeTarget = null;
            Texture2D probeRead = null;
            try
            {
                Type resourcesType = FerrofluidValidationRuntime.CreateConfig().GetType().Assembly.GetType(
                    "ChromaGLS.Settings.FerrofluidGpuResources",
                    true);
                object[] arguments = { null };
                FerrofluidValidationRuntime.ConfigureReflectionPack();
                bool loaded = (bool)resourcesType.GetMethod("TryGet", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Invoke(null, arguments);
                if (!loaded || arguments[0] == null)
                    throw new Exception("FerrofluidGpuResources.TryGet failed; baked reflection bundle unavailable.");

                Type qualityEnum = resourcesType.Assembly.GetType("ChromaGLS.Settings.ReflectionQuality", true);
                MethodInfo tryLoad = resourcesType.GetMethod("TryLoadReflectionsAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Task<bool> loadTask = (Task<bool>)tryLoad.Invoke(arguments[0], new object[] { Enum.Parse(qualityEnum, "Low") });
                while (!loadTask.IsCompleted)
                    yield return null;
                if (!loadTask.Result)
                    throw new Exception("FerrofluidGpuResources.TryLoadReflectionsAsync(Low) failed.");

                bakedRoom = arguments[0].GetType().GetProperty("RoomEnvironmentOn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(arguments[0]) as Cubemap;
                if (bakedRoom == null)
                    throw new Exception("The bundle did not provide the baked room reflections cubemap.");

                Shader probeShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidBakedProbe.shader");
                probeMaterial = new Material(probeShader);
                probeTarget = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                probeTarget.Create();
                probeRead = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
                Debug.Log($"[FerrofluidEnvironmentReflectionValidation] baked room {bakedRoom.width}x{bakedRoom.height} graphicsFormat={bakedRoom.graphicsFormat} bytes={Profiler.GetRuntimeMemorySizeLong(bakedRoom)}");

                foreach (int shape in new[] { 0, 1, 2 })
                {
                    material.SetFloat("_SurfaceShapeMode", shape);
                    for (int face = 0; face < Directions.Length; face++)
                    {
                        Color expected = Probe(Directions[face], 0f);
                        AssertNear(
                            SampleReflection(material, bakedRoom, Vector3.forward, Directions[face]),
                            expected,
                            $"bakedRoom shape={shape} direction={Directions[face]}");
                    }

                    CheckMarker(new Vector3(0f, 2f, 4.9f), "upper marker");
                    CheckMarker(new Vector3(0f, 1.2f, 4.9f), "lower marker");
                    CheckMarker(new Vector3(0f, -2f, 4.9f), "below-horizon wall");
                    for (int face = 0; face < Directions.Length; face++)
                    {
                        Vector3 d = Directions[face];
                        Vector3 up = Mathf.Abs(d.y) > 0.5f ? (d.y > 0f ? Vector3.back : Vector3.forward) : Vector3.up;
                        Vector3 right = Vector3.Cross(up, d);
                        CheckMarker((d * 4.9f) + (up * 1.2f) + right, $"off-axis marker {d}");
                    }
                }

                Debug.Log("[FerrofluidEnvironmentReflectionValidation] baked room: all six axes and upright upper-front marker agree for every shape against direct cubemap probes.");

                Color Probe(Vector3 direction, float mip)
                {
                    probeMaterial.SetTexture("_Cube", bakedRoom);
                    probeMaterial.SetVector("_Direction", direction.normalized);
                    probeMaterial.SetFloat("_FaceMip", mip);
                    Graphics.Blit(null, probeTarget, probeMaterial);
                    RenderTexture.active = probeTarget;
                    probeRead.ReadPixels(new Rect(0f, 0f, 1f, 1f), 0, 0);
                    probeRead.Apply();
                    RenderTexture.active = null;
                    return probeRead.GetPixel(0, 0);
                }

                void AssertNear(Color actual, Color expected, string label)
                {
                    for (int channel = 0; channel < 3; channel++)
                    {
                        float bound = Mathf.Max(expected[channel] * 0.05f, 0.01f);
                        if (Mathf.Abs(actual[channel] - expected[channel]) > bound)
                            throw new Exception($"{label}: surface reflection {actual:F4} disagrees with cubemap probe {expected:F4}.");
                    }
                }

                void CheckMarker(Vector3 direction, string label)
                {
                    AssertNear(
                        SampleReflection(material, bakedRoom, Vector3.forward, direction.normalized),
                        Probe(direction, 0f),
                        label);
                }

            }
            finally
            {
                if (probeTarget != null)
                {
                    probeTarget.Release();
                    Object.DestroyImmediate(probeTarget);
                }

                if (probeRead != null)
                    Object.DestroyImmediate(probeRead);
                if (probeMaterial != null)
                    Object.DestroyImmediate(probeMaterial);
            }
        }

        internal static Color Sample(Material material, Vector3 eye, Vector3 reflectedDirection)
        {
            Vector3 normal = eye + reflectedDirection;
            if (normal.sqrMagnitude < 0.000001f)
            {
                Vector3 axis = Mathf.Abs(eye.y) < 0.9f
                    ? Vector3.up
                    : Vector3.right;
                normal = Vector3.Cross(eye, axis);
            }

            normal.Normalize();
            material.SetVector("_ValidationCameraPosition", eye);
            material.SetFloat("_ValidationOutput", 2f);
            Color[] pixels = FerrofluidShadingValidation.Capture(
                material,
                normal,
                normal,
                0f,
                0f,
                "world-reflection",
                16,
                false);
            return pixels[pixels.Length / 2];
        }

        internal static void AssertChannels(Color actual, Color expected, string label)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                bool correct = expected[channel] > 0.5f
                    ? actual[channel] > 0.001f
                    : Mathf.Abs(actual[channel]) < 0.00001f;
                if (!correct)
                    throw new Exception($"{label}: wrong reflected room region, pixel={actual}, expected channels={expected}.");
            }
        }
    }
}
