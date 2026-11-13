using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidRoomReflectionAssets
    {
        internal const string OnAssetPath = "Assets/Generated/room-reflections-on.cubemap";
        internal const string OffAssetPath = "Assets/Generated/room-reflections-off.cubemap";
        internal const string FloorFeetAssetPath = "Assets/Generated/floor-feet.asset";
        internal const string RoomManifestPath = "Assets/Generated/room-reflections.json";
        internal const string HighOnAssetPath = "Assets/Generated/room-reflections-high-on.cubemap";
        internal const string HighOffAssetPath = "Assets/Generated/room-reflections-high-off.cubemap";
        internal const string HighFloorFeetAssetPath = "Assets/Generated/floor-feet-high.asset";
        internal const string HighRoomManifestPath = "Assets/Generated/room-reflections-high.json";
        private static readonly string[] FaceNames = { "positiveX", "negativeX", "positiveY", "negativeY", "positiveZ", "negativeZ" };

        internal static void Build(string repositoryRoot)
        {
            string sourceRoot = Path.Combine(repositoryRoot, "Ferrofluid.Unity", "SourceTextures");
            BakeTier(
                Path.Combine(sourceRoot, "RoomReflections"), "Assets/Generated/RoomReflections",
                1024, 1024, OnAssetPath, OffAssetPath, FloorFeetAssetPath, RoomManifestPath);
            BakeTier(
                Path.Combine(sourceRoot, "RoomReflectionsHigh"), "Assets/Generated/RoomReflectionsHigh",
                2048, 4096, HighOnAssetPath, HighOffAssetPath, HighFloorFeetAssetPath, HighRoomManifestPath);
        }

        private static void BakeTier(
            string sourceDirectory,
            string importDirectory,
            int roomSize,
            int floorSize,
            string onAssetPath,
            string offAssetPath,
            string floorAssetPath,
            string manifestPath)
        {
            Directory.CreateDirectory(importDirectory);
            Bake(sourceDirectory, importDirectory, "on", onAssetPath, roomSize);
            Bake(sourceDirectory, importDirectory, "off", offAssetPath, roomSize);
            BakeFloor(sourceDirectory, importDirectory, floorAssetPath, floorSize);
            File.Copy(
                Path.Combine(sourceDirectory, "manifest.json"),
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", manifestPath)),
                overwrite: true);
            AssetDatabase.ImportAsset(manifestPath, ImportAssetOptions.ForceUpdate);
        }

        private static Texture2D ImportFace(string sourceDirectory, string importDirectory, string fileName, int size)
        {
            string sourcePath = Path.Combine(sourceDirectory, fileName);
            if (!File.Exists(sourcePath))
                throw new InvalidOperationException($"Required capture source missing at {sourcePath}; run the NativeMenuReflectionCapture helper first.");

            string importPath = $"{importDirectory}/{fileName}";
            File.Copy(
                sourcePath,
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", importPath)),
                overwrite: true);
            AssetDatabase.ImportAsset(importPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(importPath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.isReadable = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = size;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
            standalone.format = TextureImporterFormat.RGBAHalf;
            standalone.overridden = true;
            standalone.maxTextureSize = size;
            importer.SetPlatformTextureSettings(standalone);
            importer.SaveAndReimport();

            Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(importPath);
            if (source == null || source.width != size || source.height != size)
                throw new InvalidOperationException($"Capture source {importPath} must import as {size}x{size}, got {(source == null ? "missing" : source.width + "x" + source.height)}.");

            return source;
        }

        private static void BakeFloor(string sourceDirectory, string importDirectory, string assetPath, int size)
        {
            Texture2D source = ImportFace(sourceDirectory, importDirectory, "floor-feet.exr", size);
            Texture2D baked = new(size, size, TextureFormat.RGB9e5Float, true, true)
            {
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                mipMapBias = 0f,
            };
            baked.SetPixels(source.GetPixels());
            baked.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(baked, existing);
                Object.DestroyImmediate(baked);
            }
            else
            {
                AssetDatabase.CreateAsset(baked, assetPath);
            }
        }

        private static void Bake(string sourceDirectory, string importDirectory, string state, string assetPath, int size)
        {
            Cubemap cube = new(size, TextureFormat.RGB9e5Float, true)
            {
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                // Narrow sign lettering minifies along one axis at grazing angles, so the
                // footprint needs anisotropic filtering independent of the trilinear base.
                anisoLevel = 16,
                mipMapBias = 0f,
            };
            for (int face = 0; face < 6; face++)
            {
                Texture2D source = ImportFace(sourceDirectory, importDirectory, $"{state}-{face}-{FaceNames[face]}.exr", size);
                // Raw camera ReadPixels rows are bottom-up; Unity cubemap faces need them
                // flipped, matching the capture tool's -cubemap.exr variants.
                Color[] pixels = source.GetPixels();
                for (int y = 0; y < size / 2; y++)
                {
                    int rowStart = y * size;
                    int mirroredRowStart = (size - 1 - y) * size;
                    for (int x = 0; x < size; x++)
                    {
                        Color top = pixels[rowStart + x];
                        pixels[rowStart + x] = pixels[mirroredRowStart + x];
                        pixels[mirroredRowStart + x] = top;
                    }
                }

                cube.SetPixels(pixels, (CubemapFace)face, 0);
            }

            cube.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            Cubemap existing = AssetDatabase.LoadAssetAtPath<Cubemap>(assetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(cube, existing);
                Object.DestroyImmediate(cube);
            }
            else
            {
                AssetDatabase.CreateAsset(cube, assetPath);
            }
        }
    }
}
