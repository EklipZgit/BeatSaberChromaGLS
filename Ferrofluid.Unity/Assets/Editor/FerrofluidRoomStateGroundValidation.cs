using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidRoomStateGroundValidation
    {
        private static readonly Vector3 CapturePosition = new(0f, 1.7f, -0.6096f);
        private static readonly Color Ground = new(0.02f, 0.04f, 0.06f, 1f);
        private static readonly Color Horizon = new(0.02f, 0.04f, 0.46f, 1f);
        private static readonly Vector3[] Forward = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        private static readonly Vector3[] Up = { Vector3.up, Vector3.up, Vector3.back, Vector3.forward, Vector3.up, Vector3.up };

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidRoomStateGroundValidation));

        private static void RunRuntime()
        {
            string[] textureNames = { "_FerrofluidRoomEnvironment", "_FerrofluidFloorReflection" };
            string[] vectorNames =
            {
                "_FerrofluidRoomCapturePosition", "_FerrofluidRoomLogoPlane", "_FerrofluidFloorReflectionBounds",
            };
            string[] floatNames = { "_FerrofluidRoomFloorY", "_FerrofluidFloorReflectionY", "_FerrofluidMenuLightCount", "_FerrofluidParabolaReflectionEnabled" };
            Texture[] oldTextures = Array.ConvertAll(textureNames, Shader.GetGlobalTexture);
            Vector4[] oldVectors = Array.ConvertAll(vectorNames, Shader.GetGlobalVector);
            float[] oldFloats = Array.ConvertAll(floatNames, Shader.GetGlobalFloat);
            bool oldRoom = Shader.IsKeywordEnabled("FERROFLUID_ROOM_REFLECTIONS");
            bool oldNone = Shader.IsKeywordEnabled("FERROFLUID_NO_REFLECTIONS");
            List<Object> owned = new();
            List<string> failures = new();
            try
            {
                Shader.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                Shader.DisableKeyword("FERROFLUID_NO_REFLECTIONS");
                Shader.SetGlobalFloat("_FerrofluidMenuLightCount", 0f);
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionEnabled", 0f);
                Shader.SetGlobalFloat("_FerrofluidFloorReflectionY", 0.005f);
                Shader.SetGlobalVector("_FerrofluidFloorReflectionBounds", new Vector4(-2f, -2f, 0.25f, 0.25f));
                Shader.SetGlobalVector("_FerrofluidRoomCapturePosition", CapturePosition);
                Shader.SetGlobalFloat("_FerrofluidRoomFloorY", 0f);
                Shader.SetGlobalVector("_FerrofluidRoomLogoPlane", new Vector4(0f, 0f, 1f, -26.1f));
                Shader.SetGlobalTexture("_FerrofluidFloorReflection", Texture2D.blackTexture);

                Material material = new(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader"));
                owned.Add(material);
                material.SetFloat("_ValidationOutput", 4f);
                material.SetVector("_FerrofluidReflectionStrengths", new Vector4(1f, 1f, 0f, 1f));
                Cubemap environment = MakeEnvironment();
                owned.Add(environment);
                Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", environment);

                foreach (int shape in new[] { 1, 2 })
                {
                    material.SetFloat("_SurfaceShapeMode", shape);
                    // The horizon band lives in the cubemap: an unobstructed near-horizontal
                    // ray reads it directly, while a ray grazing the floor far away still
                    // reprojects onto the band at the horizon line.
                    CheckColor("baked-horizon-above-floor",
                        Sample("baked-horizon-above-floor", new Vector3(0f, 0.5f, 0f), new Vector3(0f, 0.04f, 8f)),
                        Horizon);
                    CheckColor("far-floor-keeps-horizon-line",
                        Sample("far-floor-keeps-horizon-line", new Vector3(0f, 0.5f, 0f), new Vector3(0f, -0.005f, 8f)),
                        Horizon);
                    CheckColor("low-viewpoint-ground-only",
                        Sample("low-viewpoint-ground-only", new Vector3(0f, 0.25f, 0f), new Vector3(0f, -0.5f, 8f)),
                        Ground);
                    Color nearFloor = Sample("near-ground-without-far-neon", new Vector3(0f, 0.5f, 0f), new Vector3(0f, -0.5f, 1f));
                    CheckColor("near-ground-retains-dim-room", nearFloor, Ground);

                    Texture2D feet = new(2, 2, TextureFormat.RGBAFloat, false, true);
                    feet.SetPixels(new[] { new Color(2f, 3f, 4f), new Color(2f, 3f, 4f), new Color(2f, 3f, 4f), new Color(2f, 3f, 4f) });
                    feet.Apply();
                    owned.Add(feet);
                    Shader.SetGlobalTexture("_FerrofluidFloorReflection", feet);
                    Vector3 capturedPoint = new(-0.06290107f, 0.10990341f, -0.368469864f);
                    Vector3 capturedRay = new(-0.1218364436f, -0.3576117777f, 0.9258885989f);
                    CheckColor("captured-feet-without-distant-light", Sample("captured-feet", capturedPoint, capturedRay),
                        new Color(2f + Ground.r, 3f + Ground.g, 4f + Ground.b));
                    material.SetVector("_FerrofluidReflectionStrengths", new Vector4(0f, 1f, 0f, 1f));
                    CheckColor("room-slider-preserves-feet", Sample("room-zero", capturedPoint, capturedRay), new Color(2f, 3f, 4f));
                    material.SetVector("_FerrofluidReflectionStrengths", new Vector4(1f, 1f, 0f, 1f));
                    Shader.SetGlobalTexture("_FerrofluidFloorReflection", Texture2D.blackTexture);
                    CheckColor("elevated-sign-still-visible-before-floor",
                        Sample("elevated-sign", new Vector3(0f, 20f, 0f), new Vector3(0f, -4f, 26.1f)),
                        new Color(10f + Ground.r, Ground.g, Ground.b));
                }

                foreach (string tier in new[] { "Low", "High" })
                {
                    string prefix = tier == "High" ? "room-reflections-high-" : "room-reflections-";
                    Cubemap on = AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/Generated/" + prefix + "on.cubemap");
                    Cubemap off = AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/Generated/" + prefix + "off.cubemap");
                    if (on == null || off == null)
                        throw new Exception("Both actual " + tier + " full-scene maps are required.");
                    Vector3 blueTarget = new(5f, 14.82f, 26.09f);
                    Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", on);
                    Color blueOn = Sample(tier + "-saber-on", CapturePosition, blueTarget - CapturePosition);
                    Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", off);
                    Color blueOff = Sample(tier + "-saber-off", CapturePosition, blueTarget - CapturePosition);
                    Require(blueOn.b > 0.5f && blueOff.b > 0.5f && blueOff.b >= 0.85f * blueOn.b,
                        tier + " must preserve the non-flickering SABER light/bloom when E turns off: " + blueOn + " -> " + blueOff);
                }

                if (failures.Count > 0)
                    throw new Exception("Room state/ground assertions: " + string.Join(" | ", failures));
                Debug.Log("[FerrofluidRoomStateGroundValidation] PASS: both E states, baked horizon above and at the floor line, near-floor occlusion and room strength.");

                Color Sample(string name, Vector3 origin, Vector3 ray)
                {
                    ray.Normalize();
                    material.SetVector("_ValidationWorldPosition", origin);
                    material.SetVector("_ValidationCameraPosition", origin + ray);
                    Color[] pixels = FerrofluidShadingValidation.Capture(material, ray, ray, 0f, 0f, name, 8, false);
                    Color actual = pixels[pixels.Length / 2];
                    for (int channel = 0; channel < 3; channel++)
                        Require(!float.IsNaN(actual[channel]) && !float.IsInfinity(actual[channel]), name + " produced non-finite radiance.");
                    Debug.Log("[FerrofluidRoomStateGroundValidation] " + name + " shape=" + material.GetFloat("_SurfaceShapeMode") + " actual=" + actual.ToString("F6"));
                    return actual;
                }

                void Require(bool condition, string message)
                {
                    if (!condition)
                        failures.Add(message);
                }

                void CheckColor(string name, Color actual, Color expected)
                {
                    for (int channel = 0; channel < 3; channel++)
                        Require(Mathf.Abs(actual[channel] - expected[channel]) < 0.01f, name + ": " + actual + " != " + expected);
                }
            }
            finally
            {
                for (int index = 0; index < textureNames.Length; index++)
                    Shader.SetGlobalTexture(textureNames[index], oldTextures[index]);
                for (int index = 0; index < vectorNames.Length; index++)
                    Shader.SetGlobalVector(vectorNames[index], oldVectors[index]);
                for (int index = 0; index < floatNames.Length; index++)
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

        private static Cubemap MakeEnvironment()
        {
            const int Size = 64;
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
                        // The horizon band is baked into the map; the sign hotspot stays above it.
                        pixels[y * Size + x] = Ground
                            + (Mathf.Abs(direction.y) < 0.08f ? new Color(0f, 0f, 0.4f, 0f) : Color.black)
                            + (direction.y > 0.15f ? new Color(10f, 0f, 0f, 0f) : Color.black);
                    }
                }
                cube.SetPixels(pixels, (CubemapFace)face);
            }
            cube.Apply(false);
            return cube;
        }
    }
}
