#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidRoomReflectionExportValidation
    {
        private const int FaceSize = 64;
        private static readonly string[] FaceNames = { "positiveX", "negativeX", "positiveY", "negativeY", "positiveZ", "negativeZ" };
        private static readonly string ExportDir = Path.Combine(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..")),
            "Scratch",
            "ferro-soft-shadows",
            "export-validation");

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidRoomReflectionExportValidation));

        private static void RunRuntime()
        {
            RenderTexture on = null!;
            RenderTexture off = null!;
            RenderTexture temp = null!;
            try
            {
                Directory.CreateDirectory(ExportDir);
                foreach (string stale in Directory.GetFiles(ExportDir))
                    File.Delete(stale);

                on = SeedCube(new[] { new Color(4f, 0f, 0f), new Color(0f, 4f, 0f), new Color(0f, 0f, 4f), new Color(4f, 4f, 0f), new Color(0f, 4f, 4f), new Color(4f, 0f, 4f) });
                off = SeedCube(new[] { new Color(0.01f, 0.01f, 0.01f), new Color(0.01f, 0.01f, 0.01f), new Color(0.01f, 0.01f, 0.01f), new Color(0.01f, 0.01f, 0.01f), new Color(0.01f, 0.01f, 0.01f), new Color(0.01f, 0.01f, 0.01f) });

                Assembly plugin = FerrofluidValidationRuntime.CreateConfig().GetType().Assembly;
                Type exportType = plugin.GetType("ChromaGLS.Settings.FerrofluidRoomReflectionExport", true);
                MethodInfo export = exportType.GetMethod("ExportMaps", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                    ?? throw new Exception("ExportMaps seam missing; build the tweaks DLL before running this validation.");
                export.Invoke(null, new object[] { ExportDir, on, off });

                string directory = Path.Combine(ExportDir, "room-reflections");
                Check(File.Exists(Path.Combine(directory, "manifest.json")), "manifest.json must be written.");
                JObject manifest = JObject.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
                JArray states = (JArray)manifest["states"]!;
                Check(states.Count == 2, $"Manifest must list on and off states, got {states.Count}.");

                for (int face = 0; face < 6; face++)
                {
                    foreach (string state in new[] { "on", "off" })
                    {
                        string exr = Path.Combine(directory, $"{state}-{face}-{FaceNames[face]}.exr");
                        string png = Path.Combine(directory, $"{state}-{face}-{FaceNames[face]}.png");
                        Check(File.Exists(exr), $"Missing {exr}.");
                        Check(File.Exists(png), $"Missing {png}.");
                    }
                }

                byte[] onPositiveX = File.ReadAllBytes(Path.Combine(directory, "on-0-positiveX.exr"));
                Check(onPositiveX[0] == 0x76 && onPositiveX[1] == 0x2f && onPositiveX[2] == 0x31 && onPositiveX[3] == 0x01, "EXR magic bytes must identify a real EXR file.");
                Check(!File.ReadAllBytes(Path.Combine(directory, "on-0-positiveX.exr")).SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "on-1-negativeX.exr"))), "Distinct face colors must produce distinct EXR payloads.");
                Check(!File.ReadAllBytes(Path.Combine(directory, "on-0-positiveX.exr")).SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "off-0-positiveX.exr"))), "On and off faces must produce distinct EXR payloads.");

                Texture2D decoded = new(2, 2, TextureFormat.RGBA32, false, true);
                try
                {
                    Check(decoded.LoadImage(File.ReadAllBytes(Path.Combine(directory, "on-0-positiveX.png"))), "tone-mapped PNG must decode.");
                    Color sample = decoded.GetPixel(8, 8);
                    Check(sample.r > 0.6f && sample.r < 1.0f && sample.g < 0.05f, $"HDR red must tone-map into a red PNG, got {sample}.");
                }
                finally
                {
                    Object.DestroyImmediate(decoded);
                }

                Check(File.Exists(Path.Combine(directory, "on-contactsheet.png")), "Contact sheet must be written.");
                Check(File.Exists(Path.Combine(directory, "off-contactsheet.png")), "Off contact sheet must be written.");
                Debug.Log($"[FerrofluidRoomReflectionExportValidation] PASS: six named faces x2 states, lossless EXR, tone-mapped PNGs, contact sheets, manifest, on/off separation in {directory}.");
            }
            finally
            {
                if (temp != null)
                    Object.DestroyImmediate(temp);
                if (on != null)
                    Object.DestroyImmediate(on);
                if (off != null)
                    Object.DestroyImmediate(off);
            }
        }

        private static RenderTexture SeedCube(Color[] faceColors)
        {
            RenderTexture cube = new(FaceSize, FaceSize, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                dimension = TextureDimension.Cube,
                useMipMap = false,
            };
            cube.Create();
            RenderTexture temp = new(FaceSize, FaceSize, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            temp.Create();
            try
            {
                for (int face = 0; face < 6; face++)
                {
                    RenderTexture.active = temp;
                    GL.Clear(true, true, faceColors[face]);
                    RenderTexture.active = null;
                    Graphics.CopyTexture(temp, 0, 0, cube, face, 0);
                }
            }
            finally
            {
                temp.Release();
                Object.DestroyImmediate(temp);
            }

            return cube;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }
    }
}
#endif
