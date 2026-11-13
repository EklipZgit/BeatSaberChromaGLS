using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidRenderingSnapshotValidation
    {
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string[] TextureNames =
        {
            "_FerrofluidRoomEnvironment", "_FerrofluidFloorReflection",
            "_FerrofluidParabolaReflection", "_BloomPrePassTexture",
        };

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidRenderingSnapshotValidation));

        private static void RunRuntime()
        {
            Assembly plugin = FerrofluidValidationRuntime.CreateConfig().GetType().Assembly;
            Type capture = plugin.GetType("ChromaGLS.Settings.FerrofluidSnapshotCapture", true);
            Dictionary<string, Texture> oldTextures = new Dictionary<string, Texture>();
            foreach (string name in TextureNames)
                oldTextures[name] = Shader.GetGlobalTexture(name);
            Vector4 oldBounds = Shader.GetGlobalVector("_FerrofluidParabolaReflectionBounds");
            float oldPlane = Shader.GetGlobalFloat("_FerrofluidParabolaReflectionY");
            float oldEnabled = Shader.GetGlobalFloat("_FerrofluidParabolaReflectionEnabled");
            bool oldKeyword = Shader.IsKeywordEnabled("FERROFLUID_ROOM_REFLECTIONS");
            bool oldSrgb = GL.sRGBWrite;
            RenderTexture oldActive = RenderTexture.active;
            Cubemap room = new Cubemap(8, TextureFormat.RGBAHalf, false) { name = "Snapshot room fixture" };
            RenderTexture floor = new RenderTexture(16, 16, 0, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear);
            try
            {
                for (int face = 0; face < 6; face++)
                {
                    Color[] pixels = new Color[64];
                    for (int index = 0; index < pixels.Length; index++)
                        pixels[index] = new Color(face + 1f, 0.25f, 0.125f, 1f);
                    room.SetPixels(pixels, (CubemapFace)face);
                }
                room.Apply(false, true);
                floor.Create();
                GL.sRGBWrite = false;
                RenderTexture.active = floor;
                GL.Clear(false, true, new Color(4f, 1f, 0.5f, 0f));
                Shader.SetGlobalTexture(TextureNames[0], room);
                Shader.SetGlobalTexture(TextureNames[1], floor);
                Shader.SetGlobalTexture(TextureNames[2], floor);
                Shader.SetGlobalTexture(TextureNames[3], null);
                Shader.SetGlobalVector("_FerrofluidParabolaReflectionBounds", new Vector4(-3f, -2f, 0.125f, 0.25f));
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionY", 0.006f);
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionEnabled", 1f);
                Shader.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                JObject shared = (JObject)capture.GetMethod("BuildSharedContext", Static).Invoke(null, new object[] { DateTime.UtcNow });
                JObject rendering = shared["rendering"] as JObject;
                Check(rendering != null, "F12 must record the live global reflection bindings, not only per-material uniforms.");
                Check(rendering["globalKeywords"].ToObject<string[]>().Length > 0, "Global shader variants must be captured.");
                Check(Array.IndexOf(rendering["globalKeywords"].ToObject<string[]>(), "FERROFLUID_ROOM_REFLECTIONS") >= 0, "The enabled room-reflection keyword must appear in the live snapshot.");
                Shader.DisableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                JObject disabledShared = (JObject)capture.GetMethod("BuildSharedContext", Static).Invoke(null, new object[] { DateTime.UtcNow });
                Check(Array.IndexOf(((JArray)disabledShared["rendering"]["globalKeywords"]).ToObject<string[]>(), "FERROFLUID_ROOM_REFLECTIONS") < 0, "globalKeywords must reflect enabled state, not every registered keyword.");
                Shader.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                Check(rendering["globalTextures"][TextureNames[0]]["name"].Value<string>() == room.name, "The actual selected cubemap identity must be recorded.");
                Check(Mathf.Abs(rendering["globalFloats"]["_FerrofluidParabolaReflectionY"].Value<float>() - 0.006f) < 0.000001f, "The saved reflection plane must be recorded.");
                Check(rendering["globalVectors"]["_FerrofluidParabolaReflectionBounds"][0].Value<float>() == -3f, "The saved cache mapping must be recorded.");
                Check(shared["ferrofluidBundle"]["sha256"].Value<string>().Length == 64, "F12 must identify the exact embedded bundle bytes.");
                MethodInfo export = capture.GetMethod("CaptureRenderingAssets", Static);
                Check(export != null, "F12 must export HDR reflection pixels, including non-readable cubemap faces.");
                Type resourceType = plugin.GetType("ChromaGLS.Settings.FerrofluidGpuResources", true);
                object[] resources = { null };
                Check((bool)resourceType.GetMethod("TryGet", Static).Invoke(null, resources), "The actual embedded ferrofluid bundle must load.");
                Shader readbackShader = (Shader)resourceType.GetProperty(
                    "SnapshotReadbackShader",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(resources[0]);
                Check(readbackShader != null && readbackShader.isSupported, "The trial DLL must embed the supported F12 HDR readback shader.");
                IDictionary files = (IDictionary)export.Invoke(null, new object[]
                {
                    rendering,
                    readbackShader,
                });
                JArray faces = rendering["globalTextures"][TextureNames[0]]["files"] as JArray;
                Check(faces != null && faces.Count == 6, "A room dump must retain every cubemap face.");
                for (int face = 0; face < 6; face++)
                {
                    Color pixel = Decode((string)faces[face], (byte[])files[(string)faces[face]]);
                    Check(Mathf.Abs(pixel.r - (face + 1f)) < 0.01f && Mathf.Abs(pixel.g - 0.25f) < 0.001f, $"Cubemap face {face} must preserve unclamped HDR pixels: {pixel}.");
                }
                foreach (string name in new[] { TextureNames[1], TextureNames[2] })
                {
                    string file = (string)rendering["globalTextures"][name]["files"][0];
                    Color pixel = Decode(file, (byte[])files[file]);
                    Check(Mathf.Abs(pixel.r - 4f) < 0.01f && Mathf.Abs(pixel.b - 0.5f) < 0.001f, "Planar reflection pixels must preserve HDR and colour.");
                }
                Check(RenderTexture.active == floor && GL.sRGBWrite == false, "F12 GPU readback must restore the caller's render state.");
                Check(Shader.GetGlobalTexture(TextureNames[0]) == room && Shader.IsKeywordEnabled("FERROFLUID_ROOM_REFLECTIONS"), "F12 must not replace the game's reflection bindings.");
                Debug.Log("[FerrofluidRenderingSnapshotValidation] PASS: live globals, exact bundle identity, six non-readable HDR cube faces and both planar HDR images.");
            }
            finally
            {
                RenderTexture.active = oldActive;
                GL.sRGBWrite = oldSrgb;
                foreach (KeyValuePair<string, Texture> entry in oldTextures)
                    Shader.SetGlobalTexture(entry.Key, entry.Value);
                Shader.SetGlobalVector("_FerrofluidParabolaReflectionBounds", oldBounds);
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionY", oldPlane);
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionEnabled", oldEnabled);
                if (oldKeyword)
                    Shader.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                else
                    Shader.DisableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                floor.Release();
                Object.DestroyImmediate(floor);
                Object.DestroyImmediate(room);
            }
        }

        private static Color Decode(string name, byte[] bytes)
        {
            string asset = "Assets/Generated/SnapshotValidation/" + Path.GetFileName(name);
            Directory.CreateDirectory("Assets/Generated/SnapshotValidation");
            File.WriteAllBytes(asset, bytes);
            AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(asset);
            importer.sRGBTexture = false;
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(asset).GetPixel(2, 2);
        }

        private static void Check(bool value, string message)
        {
            if (!value)
                throw new Exception(message);
        }
    }
}
