using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidPositionalRoomValidation
    {
        private static readonly Vector3 CapturePosition = new(0f, 1.7f, -0.6096f);
        private static readonly string[] TextureNames =
        {
            "_FerrofluidRoomEnvironment", "_FerrofluidFloorReflection",
        };
        private static readonly string[] VectorNames =
        {
            "_FerrofluidRoomCapturePosition", "_FerrofluidRoomLogoPlane", "_FerrofluidFloorReflectionBounds",
        };
        private static readonly string[] FloatNames =
        {
            "_FerrofluidRoomFloorY", "_FerrofluidFloorReflectionY", "_FerrofluidMenuLightCount", "_FerrofluidParabolaReflectionEnabled",
        };
        private static readonly string[] Keywords = { "FERROFLUID_ROOM_REFLECTIONS", "FERROFLUID_NO_REFLECTIONS" };
        private static readonly Vector3[] Forward = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        private static readonly Vector3[] Up = { Vector3.up, Vector3.up, Vector3.back, Vector3.forward, Vector3.up, Vector3.up };

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidPositionalRoomValidation));

        private static IEnumerator RunRuntime()
        {
            var textures = new Texture[TextureNames.Length];
            var vectors = new Vector4[VectorNames.Length];
            var floats = new float[FloatNames.Length];
            var keywords = new bool[Keywords.Length];
            for (int index = 0; index < textures.Length; index++)
                textures[index] = Shader.GetGlobalTexture(TextureNames[index]);
            for (int index = 0; index < vectors.Length; index++)
                vectors[index] = Shader.GetGlobalVector(VectorNames[index]);
            for (int index = 0; index < floats.Length; index++)
                floats[index] = Shader.GetGlobalFloat(FloatNames[index]);
            for (int index = 0; index < keywords.Length; index++)
                keywords[index] = Shader.IsKeywordEnabled(Keywords[index]);

            var owned = new List<Object>();
            var failures = new List<string>();
            int samples = 0;
            Type roomType = FerrofluidValidationRuntime.CreateConfig().GetType().Assembly.GetType("ChromaGLS.Settings.FerrofluidRoomReflections", true);
            Type qualityType = roomType.Assembly.GetType("ChromaGLS.Settings.ReflectionQuality", true);
            const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            MethodInfo quality = roomType.GetMethod("SetReflectionQuality", Flags);
            MethodInfo bind = roomType.GetMethod("ApplyEnvironment", Flags);
            MethodInfo projection = roomType.GetMethod("ApplyRoomProjection", Flags);
            try
            {
                quality.Invoke(null, new[] { Enum.Parse(qualityType, "Low") });
                Shader.DisableKeyword("FERROFLUID_NO_REFLECTIONS");
                Shader.SetGlobalFloat("_FerrofluidMenuLightCount", 0f);
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionEnabled", 0f);
                Shader.SetGlobalFloat("_FerrofluidFloorReflectionY", 0.005f);
                Shader.SetGlobalVector("_FerrofluidFloorReflectionBounds", new Vector4(-2f, -2f, 0.25f, 0.25f));

                Cubemap black = Cube(false);
                Cubemap encoded = Cube(true);
                Texture2D feet = new(2, 2, TextureFormat.RGBAFloat, false, true);
                feet.SetPixels(new[] { new Color(2f, 3f, 4f), new Color(2f, 3f, 4f), new Color(2f, 3f, 4f), new Color(2f, 3f, 4f) });
                feet.Apply();
                Shader.SetGlobalTexture("_FerrofluidFloorReflection", Texture2D.blackTexture);
                owned.Add(black);
                owned.Add(encoded);
                owned.Add(feet);

                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader");
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                {
                    if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                        throw new Exception("Production shader failed compilation: " + message.message);
                }

                Material material = new(shader);
                owned.Add(material);
                material.SetVector("_FerrofluidReflectionStrengths", new Vector4(1f, 1f, 0f, 1f));
                material.SetFloat("_ValidationOutput", 4f);
                foreach (int shape in new[] { 1, 2 })
                {
                    material.SetFloat("_SurfaceShapeMode", shape);
                    // The horizon band lives inside the cubemap now; a horizontal ray must
                    // return exactly the encoded direction color with nothing added.
                    Bind(encoded);
                    Check("near-horizon-cube-only", new Vector3(0f, 0.5f, 0f), Vector3.left, Encode(Vector3.left));
                    Check("vertical-up", new Vector3(0f, 0.5f, 0f), Vector3.up, Encode(Vector3.up));
                    // A forward ray from outside still hits the sign plane before the floor,
                    // so the cubemap is read at the projected direction, not the raw ray.
                    Check("tangent-at-origin", new Vector3(8f, 0.5f, 0f), Vector3.forward,
                        Encode(((new Vector3(8f, 0.5f, 0f) + (26.1f * Vector3.forward)) - CapturePosition).normalized));

                    Vector3 target = new(-1.19f, 18.9f, 26.1f);
                    Vector3 expectedDirection = (target - CapturePosition).normalized;
                    Color expectedLogo = Encode(expectedDirection);
                    Check("neon-source-viewpoint", CapturePosition, target - CapturePosition, expectedLogo);
                    Vector3 moved = new(2f, 0.5f, -2f);
                    Check("neon-moved-viewpoint", moved, target - moved, expectedLogo);
                    Check("parallel-up-fallback", CapturePosition, Vector3.up, Encode(Vector3.up));
                    Check("away-facing-fallback", CapturePosition, Vector3.back, Encode(Vector3.back));

                    // Keep the room black here to isolate floor occlusion.
                    // RoomStateGroundValidation separately covers nonzero ground radiance.
                    Bind(black);
                    Shader.SetGlobalTexture("_FerrofluidFloorReflection", feet);
                    Vector3 floorRay = new(0f, -0.495f, 0.5f);
                    Check("floor-occludes-room", new Vector3(0f, 0.5f, 0f), floorRay, new Color(2f, 3f, 4f));
                    Vector3 capturedPoint = new(-0.06290107f, 0.10990341f, -0.368469864f);
                    Vector3 capturedRay = new(-0.1218364436f, -0.3576117777f, 0.9258885989f);
                    Check("captured-low-site469", capturedPoint, capturedRay, new Color(2f, 3f, 4f));
                    Shader.SetGlobalVector("_FerrofluidFloorReflectionBounds", new Vector4(50f, 50f, 0.25f, 0.25f));
                    Check("floor-occlusion-outside-icon", new Vector3(0f, 0.5f, 0f), floorRay, Color.black);
                    Shader.SetGlobalVector("_FerrofluidFloorReflectionBounds", new Vector4(-2f, -2f, 0.25f, 0.25f));
                    material.SetVector("_FerrofluidReflectionStrengths", new Vector4(0f, 1f, 0f, 1f));
                    Check("room-slider-preserves-feet", capturedPoint, capturedRay, new Color(2f, 3f, 4f));
                    Check("room-slider-scales-both-sources", new Vector3(0f, 0.5f, 0f), Vector3.left, Color.black);
                    material.SetVector("_FerrofluidReflectionStrengths", new Vector4(1f, 1f, 0f, 1f));
                    Shader.SetGlobalTexture("_FerrofluidFloorReflection", Texture2D.blackTexture);
                }

                // Synthetic maps cannot catch a missing packaged tier or an inverted real image.
                FerrofluidValidationRuntime.ConfigureReflectionPack();
                quality.Invoke(null, new[] { Enum.Parse(qualityType, "High") });
                Type resourceType = roomType.Assembly.GetType("ChromaGLS.Settings.FerrofluidGpuResources", true);
                object[] resourceArguments = { null };
                if (!(bool)resourceType.GetMethod("TryGet", Flags).Invoke(null, resourceArguments))
                    throw new Exception("Actual core bundle must load for sign-image checks.");
                object resources = resourceArguments[0];
                const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                Task<bool> highLoad = (Task<bool>)resourceType.GetMethod("TryLoadReflectionsAsync", InstanceFlags).Invoke(resources, new[] { Enum.Parse(qualityType, "High") });
                while (!highLoad.IsCompleted)
                    yield return null;
                if (!highLoad.Result)
                    throw new Exception("Actual High full-scene room must load for sign-image checks.");

                object GetResource(string name) => resourceType.GetProperty(name, InstanceFlags).GetValue(resources);
                projection.Invoke(null, new[]
                {
                    GetResource("RoomCapturePosition"), GetResource("RoomFloorY"), GetResource("RoomLogoPlane"),
                });
                bind.Invoke(null, new[] { GetResource("RoomEnvironmentOn"), GetResource("RoomEnvironmentOff"), null });
                material = new Material((Shader)GetResource("SurfaceShader"));
                owned.Add(material);
                material.SetFloat("_ValidationOutput", 4f);
                material.SetFloat("_SurfaceShapeMode", 2f);
                material.SetVector("_FerrofluidReflectionStrengths", new Vector4(1f, 0f, 0f, 1f));
                // The same rays now read the new full-scene High sources directly at pixel
                // centres (1120,368) and (990,514) top-down. Tolerances include RGB9e5 error.
                Texture2D highSource = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Generated/RoomReflectionsHigh/on-4-positiveZ.exr");
                if (highSource == null || highSource.width != 2048)
                    throw new Exception("Imported High full-scene positiveZ source is required for sign-image checks.");
                Check("actual-high-red-sign", CapturePosition, new Vector3(0.0791204642f, 0.537445226f, 0.839578812f),
                    highSource.GetPixel(1120, 2048 - 1 - 368), 0.02879443f);
                Check("actual-high-blue-sign", CapturePosition, new Vector3(-0.0292770376f, 0.445273154f, 0.894916015f),
                    highSource.GetPixel(990, 2048 - 1 - 514), 0.02894702f);

                quality.Invoke(null, new[] { Enum.Parse(qualityType, "None") });
                if (Shader.GetGlobalVector("_FerrofluidRoomCapturePosition") != Vector4.zero
                    || Shader.GetGlobalFloat("_FerrofluidRoomFloorY") != 0f
                    || Shader.GetGlobalVector("_FerrofluidRoomLogoPlane") != Vector4.zero)
                    failures.Add("None did not clear room projection state.");
                Check("none-disables-reflections", CapturePosition, Vector3.up, Color.black);

                if (failures.Count > 0)
                    throw new Exception("Positional room assertions failed (" + failures.Count + "/" + samples + "): " + string.Join(" | ", failures));
                Debug.Log("[FerrofluidPositionalRoomValidation] PASS " + samples + " rendered samples and None teardown.");

                void Bind(Cubemap far)
                {
                    projection.Invoke(null, new object[]
                    {
                        (Vector4)CapturePosition, 0f, new Vector4(0f, 0f, 1f, -26.1f),
                    });
                    bind.Invoke(null, new object[] { far, far, null });
                }

                void Check(string name, Vector3 origin, Vector3 ray, Color expected, float tolerance = 0.01f)
                {
                    // reflect(-view, normal) equals ray when both view and normal equal ray.
                    ray.Normalize();
                    material.SetVector("_ValidationWorldPosition", origin);
                    material.SetVector("_ValidationCameraPosition", origin + ray);
                    Color[] pixels = FerrofluidShadingValidation.Capture(material, ray, ray, 0f, 0f, name, 8, false);
                    Color actual = pixels[pixels.Length / 2];
                    bool correct = true;
                    for (int channel = 0; channel < 3; channel++)
                        correct &= !float.IsNaN(actual[channel]) && !float.IsInfinity(actual[channel]) && Mathf.Abs(actual[channel] - expected[channel]) <= tolerance;
                    samples++;
                    Debug.Log("[FerrofluidPositionalRoomValidation] " + name + " shape=" + material.GetFloat("_SurfaceShapeMode")
                        + " actual=" + actual.ToString("F6") + " expected=" + expected.ToString("F6") + " correct=" + correct);
                    if (!correct)
                        failures.Add(name + ": " + actual.ToString("F6") + " != " + expected.ToString("F6"));
                }
            }
            finally
            {
                roomType.GetMethod("EndMenuSession", Flags).Invoke(null, null);
                for (int index = 0; index < textures.Length; index++)
                    Shader.SetGlobalTexture(TextureNames[index], textures[index]);
                for (int index = 0; index < vectors.Length; index++)
                    Shader.SetGlobalVector(VectorNames[index], vectors[index]);
                for (int index = 0; index < floats.Length; index++)
                    Shader.SetGlobalFloat(FloatNames[index], floats[index]);
                for (int index = 0; index < keywords.Length; index++)
                {
                    if (keywords[index])
                        Shader.EnableKeyword(Keywords[index]);
                    else
                        Shader.DisableKeyword(Keywords[index]);
                }

                foreach (Object instance in owned)
                    Object.DestroyImmediate(instance);
            }
        }

        private static Cubemap Cube(bool encodeDirection)
        {
            const int Size = 128;
            Cubemap cube = new(Size, TextureFormat.RGBAFloat, false) { filterMode = FilterMode.Bilinear };
            for (int face = 0; face < 6; face++)
            {
                Color[] pixels = new Color[Size * Size];
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        Vector3 direction = Forward[face]
                            + (Vector3.Cross(Up[face], Forward[face]) * ((2f * (x + 0.5f) / Size) - 1f))
                            - (Up[face] * ((2f * (y + 0.5f) / Size) - 1f));
                        pixels[(y * Size) + x] = encodeDirection ? Encode(direction.normalized) : Color.black;
                    }
                }

                cube.SetPixels(pixels, (CubemapFace)face);
            }

            cube.Apply(false);
            return cube;
        }

        private static Color Encode(Vector3 direction) => new((direction.x + 1f) * 0.5f, (direction.y + 1f) * 0.5f, (direction.z + 1f) * 0.5f, 1f);
    }
}
