using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // The horizon band is baked into the full-scene room cubemap; there is no separate
    // horizon texture or cylinder. These rays prove the banded cubemap alone supplies
    // the horizon, that downward rays still reproject onto the opaque floor before
    // sampling, and that feet/parabola patches compose on top of the reprojected ground.
    internal static class FerrofluidBakedHorizonValidation
    {
        private static readonly Vector3 CapturePosition = new(0f, 1.7f, -0.6096f);
        private static readonly Color Ground = new(0.02f, 0.04f, 0.06f, 1f);
        private static readonly Color Teal = new(0f, 0.5f, 1f, 1f);
        private static readonly Color Upper = new(0.1f, 0.2f, 0.3f, 1f);
        private static readonly Vector3[] Forward = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        private static readonly Vector3[] Up = { Vector3.up, Vector3.up, Vector3.back, Vector3.forward, Vector3.up, Vector3.up };
        private static readonly Vector3 CapturedPoint = new(-0.06290107f, 0.10990341f, -0.368469864f);
        private static readonly Vector3 CapturedRay = new(-0.1218364436f, -0.3576117777f, 0.9258885989f);
        private static readonly string[] TextureNames =
        {
            "_FerrofluidRoomEnvironment", "_FerrofluidRoomHorizonTexture", "_FerrofluidFloorReflection",
            "_FerrofluidParabolaReflection",
        };
        private static readonly string[] VectorNames =
        {
            "_FerrofluidRoomCapturePosition", "_FerrofluidRoomHorizon", "_FerrofluidRoomHorizonHeight",
            "_FerrofluidRoomLogoPlane", "_FerrofluidFloorReflectionBounds", "_FerrofluidParabolaReflectionBounds",
        };
        private static readonly string[] FloatNames =
        {
            "_FerrofluidRoomFloorY", "_FerrofluidFloorReflectionY", "_FerrofluidParabolaReflectionY",
            "_FerrofluidMenuLightCount", "_FerrofluidParabolaReflectionEnabled",
        };
        private static readonly string[] Keywords = { "FERROFLUID_ROOM_REFLECTIONS", "FERROFLUID_NO_REFLECTIONS" };

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidBakedHorizonValidation));

        private static void RunRuntime()
        {
            Texture[] oldTextures = Array.ConvertAll(TextureNames, Shader.GetGlobalTexture);
            Vector4[] oldVectors = Array.ConvertAll(VectorNames, Shader.GetGlobalVector);
            float[] oldFloats = Array.ConvertAll(FloatNames, Shader.GetGlobalFloat);
            bool[] oldKeywords = Array.ConvertAll(Keywords, Shader.IsKeywordEnabled);
            List<Object> owned = new();
            List<string> failures = new();
            int samples = 0;
            Material material = null;
            Type roomType = FerrofluidValidationRuntime.CreateConfig().GetType().Assembly.GetType("ChromaGLS.Settings.FerrofluidRoomReflections", true);
            Type qualityType = roomType.Assembly.GetType("ChromaGLS.Settings.ReflectionQuality", true);
            const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            try
            {
                roomType.GetMethod("SetReflectionQuality", Flags).Invoke(null, new[] { Enum.Parse(qualityType, "Low") });
                Shader.DisableKeyword("FERROFLUID_NO_REFLECTIONS");
                Shader.SetGlobalFloat("_FerrofluidMenuLightCount", 0f);
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionEnabled", 0f);
                Shader.SetGlobalFloat("_FerrofluidFloorReflectionY", 0.005f);
                Shader.SetGlobalVector("_FerrofluidFloorReflectionBounds", new Vector4(-2f, -2f, 0.25f, 0.25f));

                Cubemap room = MakeRoom();
                owned.Add(room);
                roomType.GetMethod("ApplyRoomProjection", Flags).Invoke(null, new object[]
                {
                    (Vector4)CapturePosition, 0f, new Vector4(0f, 0f, 1f, -26.1f),
                });
                roomType.GetMethod("ApplyEnvironment", Flags).Invoke(null, new object[] { room, room, null });
                Shader.SetGlobalTexture("_FerrofluidFloorReflection", Texture2D.blackTexture);
                Shader.SetGlobalTexture("_FerrofluidParabolaReflection", Texture2D.blackTexture);

                Texture2D feet = new(2, 2, TextureFormat.RGBAFloat, false, true);
                feet.SetPixels(new[] { new Color(2f, 3f, 4f), new Color(2f, 3f, 4f), new Color(2f, 3f, 4f), new Color(2f, 3f, 4f) });
                feet.Apply();
                owned.Add(feet);

                Texture2D parabola = new(2, 2, TextureFormat.RGBAFloat, false, true);
                parabola.SetPixels(new[] { new Color(0.4f, 0.8f, 1.2f), new Color(0.4f, 0.8f, 1.2f), new Color(0.4f, 0.8f, 1.2f), new Color(0.4f, 0.8f, 1.2f) });
                parabola.Apply();
                owned.Add(parabola);

                material = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader"));
                owned.Add(material);
                material.SetFloat("_ValidationOutput", 4f);
                material.SetVector("_FerrofluidReflectionStrengths", new Vector4(1f, 1f, 0f, 1f));
                foreach (int shape in new[] { 1, 2 })
                {
                    material.SetFloat("_SurfaceShapeMode", shape);
                    Check("side-horizon-baked-only", new Vector3(0f, 0.5f, 0f), Vector3.left, Teal);
                    Check("near-horizon-retains-teal", new Vector3(0f, 0.5f, 0f), new Vector3(-1f, 0.04f, 0f), Teal);
                    Check("upward-room", new Vector3(0f, 0.5f, 0f), Vector3.up, Upper);
                    Check("floor-ray-ground", CapturedPoint, CapturedRay, Ground);

                    Shader.SetGlobalTexture("_FerrofluidFloorReflection", feet);
                    Check("floor-ray-feet", CapturedPoint, CapturedRay, new Color(2.02f, 3.04f, 4.06f));
                    material.SetVector("_FerrofluidReflectionStrengths", new Vector4(0f, 1f, 0f, 1f));
                    Check("room-zero-feet-only", CapturedPoint, CapturedRay, new Color(2f, 3f, 4f));
                    material.SetVector("_FerrofluidReflectionStrengths", new Vector4(1f, 1f, 1f, 1f));
                    Shader.SetGlobalTexture("_FerrofluidFloorReflection", Texture2D.blackTexture);

                    Shader.SetGlobalTexture("_FerrofluidParabolaReflection", parabola);
                    Shader.SetGlobalFloat("_FerrofluidParabolaReflectionEnabled", 1f);
                    Shader.SetGlobalFloat("_FerrofluidParabolaReflectionY", 0.01f);
                    Shader.SetGlobalVector("_FerrofluidParabolaReflectionBounds", new Vector4(-2f, -2f, 0.25f, 0.25f));
                    Check("floor-ray-parabola", CapturedPoint, CapturedRay, new Color(0.42f, 0.84f, 1.26f));
                    Shader.SetGlobalFloat("_FerrofluidParabolaReflectionEnabled", 0f);
                    Shader.SetGlobalTexture("_FerrofluidParabolaReflection", Texture2D.blackTexture);
                    material.SetVector("_FerrofluidReflectionStrengths", new Vector4(1f, 1f, 0f, 1f));
                }

                if (failures.Count > 0)
                    throw new Exception("Baked horizon assertions failed (" + failures.Count + "/" + samples + "): " + string.Join(" | ", failures));
                Debug.Log("[FerrofluidBakedHorizonValidation] PASS " + samples + " rendered samples.");
            }
            finally
            {
                roomType.GetMethod("EndMenuSession", Flags).Invoke(null, null);
                for (int index = 0; index < oldTextures.Length; index++)
                    Shader.SetGlobalTexture(TextureNames[index], oldTextures[index]);
                for (int index = 0; index < oldVectors.Length; index++)
                    Shader.SetGlobalVector(VectorNames[index], oldVectors[index]);
                for (int index = 0; index < oldFloats.Length; index++)
                    Shader.SetGlobalFloat(FloatNames[index], oldFloats[index]);
                for (int index = 0; index < oldKeywords.Length; index++)
                {
                    if (oldKeywords[index])
                        Shader.EnableKeyword(Keywords[index]);
                    else
                        Shader.DisableKeyword(Keywords[index]);
                }

                foreach (Object instance in owned)
                    Object.DestroyImmediate(instance);
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
                Debug.Log("[FerrofluidBakedHorizonValidation] " + name + " shape=" + material.GetFloat("_SurfaceShapeMode")
                    + " actual=" + actual.ToString("F6") + " expected=" + expected.ToString("F6") + " correct=" + correct);
                if (!correct)
                    failures.Add(name + ": " + actual.ToString("F6") + " != " + expected.ToString("F6"));
            }
        }

        private static Cubemap MakeRoom()
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
                        Vector3 direction = (Forward[face]
                            + Vector3.Cross(Up[face], Forward[face]) * (2f * (x + 0.5f) / Size - 1f)
                            - Up[face] * (2f * (y + 0.5f) / Size - 1f)).normalized;
                        pixels[y * Size + x] =
                            direction.y < -0.15f ? Ground
                            : Mathf.Abs(direction.y) < 0.08f ? Teal
                            : Upper;
                    }
                }

                cube.SetPixels(pixels, (CubemapFace)face);
            }

            cube.Apply(false);
            return cube;
        }
    }
}
