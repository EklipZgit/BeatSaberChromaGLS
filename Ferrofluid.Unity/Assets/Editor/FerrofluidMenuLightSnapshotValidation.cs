using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidMenuLightSnapshotValidation
    {
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string TextureName = "_FerrofluidMenuLightReflections";
        private const string CountName = "_FerrofluidMenuLightCount";
        private const string MatricesName = "_FerrofluidMenuLightWorldToLocal";
        private const string BoundsName = "_FerrofluidMenuLightBounds";
        private const string FlagsName = "_FerrofluidMenuLightFlags";

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidMenuLightSnapshotValidation));

        private static void RunRuntime()
        {
            Texture previousTexture = Shader.GetGlobalTexture(TextureName);
            float previousCount = Shader.GetGlobalFloat(CountName);
            Matrix4x4[] previousMatrices = Shader.GetGlobalMatrixArray(MatricesName);
            Vector4[] previousBounds = Shader.GetGlobalVectorArray(BoundsName);
            Vector4[] previousFlags = Shader.GetGlobalVectorArray(FlagsName);
            RenderTexture previousActive = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            RenderTexture array = new(16, 16, 0, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear)
            {
                name = "Menu light snapshot markers",
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 2,
                useMipMap = true,
                autoGenerateMips = false,
            };
            try
            {
                Check(array.Create(), "The marker array must allocate.");
                GL.sRGBWrite = false;
                Color[] markers = { new(2f, 0.25f, 0.125f, 1f), new(0.125f, 3f, 0.5f, 1f) };
                for (int slice = 0; slice < 2; slice++)
                {
                    Graphics.SetRenderTarget(array, 0, CubemapFace.Unknown, slice);
                    GL.Clear(false, true, markers[slice]);
                }

                array.GenerateMips();
                var matrices = new Matrix4x4[8];
                var bounds = new Vector4[8];
                var flags = new Vector4[8];
                matrices[0] = Matrix4x4.TRS(new Vector3(1f, 2f, 3f), Quaternion.Euler(17f, 37f, 11f), Vector3.one).inverse;
                matrices[1] = Matrix4x4.TRS(new Vector3(-2f, 4f, 1f), Quaternion.Euler(0f, 0f, 90f), Vector3.one).inverse;
                bounds[0] = new Vector4(-2f, -3f, 0.25f, 0.125f);
                bounds[1] = new Vector4(-4f, -1f, 0.1f, 0.5f);
                flags[0] = new Vector4(1f, 0f, 0f, 0f);
                flags[1] = Vector4.zero;
                Shader.SetGlobalTexture(TextureName, array);
                Shader.SetGlobalFloat(CountName, 2f);
                Shader.SetGlobalMatrixArray(MatricesName, matrices);
                Shader.SetGlobalVectorArray(BoundsName, bounds);
                Shader.SetGlobalVectorArray(FlagsName, flags);
                RenderTexture.active = array;

                Assembly plugin = FerrofluidValidationRuntime.CreateConfig().GetType().Assembly;
                Type capture = plugin.GetType("ChromaGLS.Settings.FerrofluidSnapshotCapture", true);
                JObject shared = (JObject)capture.GetMethod("BuildSharedContext", Static).Invoke(null, new object[] { DateTime.UtcNow });
                JObject rendering = (JObject)shared["rendering"];
                Check(rendering["globalFloats"]?[CountName]?.Value<float>() == 2f, "F12 must record the active menu-light collection count.");
                Check(rendering["matrixArrayLayout"]?.Value<string>() == "column-major", "F12 must declare the matrix array element layout.");
                JArray capturedMatrices = rendering["globalMatrixArrays"]?[MatricesName] as JArray;
                JArray capturedBounds = rendering["globalVectorArrays"]?[BoundsName] as JArray;
                JArray capturedFlags = rendering["globalVectorArrays"]?[FlagsName] as JArray;
                Check(capturedMatrices != null && capturedMatrices.Count == 8, "F12 must retain every bound matrix slot.");
                Check(capturedBounds != null && capturedBounds.Count == 8, "F12 must retain every bound bounds slot.");
                Check(capturedFlags != null && capturedFlags.Count == 8, "F12 must retain disabled flags as well as enabled lights.");
                for (int slot = 0; slot < 8; slot++)
                {
                    for (int component = 0; component < 16; component++)
                        Near(capturedMatrices[slot][component].Value<float>(), matrices[slot][component], "A captured local plane matrix must not be transposed.");

                    for (int component = 0; component < 4; component++)
                    {
                        Near(capturedBounds[slot][component].Value<float>(), bounds[slot][component], "Captured UV bounds must match the binding.");
                        Near(capturedFlags[slot][component].Value<float>(), flags[slot][component], "Captured flags must match the binding.");
                    }
                }

                JObject descriptor = rendering["globalTextures"]?[TextureName] as JObject;
                Check(descriptor != null && descriptor["volumeDepth"].Value<int>() == 2, "F12 must describe the actual array, not the empty legacy texture.");
                Type resourcesType = plugin.GetType("ChromaGLS.Settings.FerrofluidGpuResources", true);
                object[] resources = { null };
                Check((bool)resourcesType.GetMethod("TryGet", Static).Invoke(null, resources), "The embedded GPU assets must load.");
                Shader readback = (Shader)resourcesType.GetProperty("SnapshotReadbackShader", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(resources[0]);
                Check(readback != null && readback.isSupported, "The embedded array readback pass must be supported.");
                IDictionary files = (IDictionary)capture.GetMethod("CaptureRenderingAssets", Static).Invoke(null, new object[] { rendering, readback });
                JArray names = (JArray)descriptor["files"];
                Check(names.Count == 2, "Both saved slices must be exported, including the disabled slice.");
                for (int slice = 0; slice < 2; slice++)
                {
                    string name = names[slice].Value<string>();
                    Check(name == "FerrofluidMenuLightReflections-slice" + slice + ".exr", "The file name must identify the array slice.");
                    JToken metadata = descriptor["fileMetadata"][slice];
                    Check(metadata["slice"].Value<int>() == slice && metadata["sha256"].Value<string>().Length == 64, "Each EXR must identify its slice and content hash.");
                    Color pixel = Decode(name, (byte[])files[name]);
                    for (int channel = 0; channel < 3; channel++)
                        Check(Mathf.Abs(pixel[channel] - markers[slice][channel]) < 0.01f, "Array readback must preserve the correct slice's unclamped linear HDR colour.");
                }

                Check(RenderTexture.active == array && !GL.sRGBWrite, "Readback must restore the caller's render state.");
                Check(Shader.GetGlobalTexture(TextureName) == array && Shader.GetGlobalFloat(CountName) == 2f, "Readback must not change the saved collection bindings.");
                Debug.Log("[FerrofluidMenuLightSnapshotValidation] PASS: array slices, HDR pixels, count, matrices, bounds, disabled flags and caller state.");
            }
            finally
            {
                RenderTexture.active = previousActive;
                GL.sRGBWrite = previousSrgb;
                Shader.SetGlobalTexture(TextureName, previousTexture);
                Shader.SetGlobalFloat(CountName, previousCount);
                if (previousMatrices != null && previousMatrices.Length > 0)
                    Shader.SetGlobalMatrixArray(MatricesName, previousMatrices);
                if (previousBounds != null && previousBounds.Length > 0)
                    Shader.SetGlobalVectorArray(BoundsName, previousBounds);
                if (previousFlags != null && previousFlags.Length > 0)
                    Shader.SetGlobalVectorArray(FlagsName, previousFlags);
                array.Release();
                Object.DestroyImmediate(array);
            }
        }

        private static Color Decode(string name, byte[] bytes)
        {
            const string directory = "Assets/Generated/MenuLightSnapshotValidation";
            Directory.CreateDirectory(directory);
            string path = directory + "/" + Path.GetFileName(name);
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = false;
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path).GetPixel(2, 2);
        }

        private static void Near(float actual, float expected, string message)
            => Check(Mathf.Abs(actual - expected) < 0.000001f, message);

        private static void Check(bool value, string message)
        {
            if (!value)
                throw new InvalidOperationException(message);
        }
    }
}
