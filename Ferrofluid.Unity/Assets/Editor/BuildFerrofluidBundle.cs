using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class BuildFerrofluidBundle
    {
        private const string GeneratedAssetDirectory = "Assets/Generated";
        private const string SimulationShaderPath = "Assets/FerrofluidSimulation.shader";
        private const string SurfaceShaderPath = "Assets/FerrofluidSurface.shader";
        private const string FloorShadowShaderPath = "Assets/FerrofluidFloorShadow.shader";
        private const string ParabolicFloorReflectionShaderPath = "Assets/ParabolicFloorReflection.shader";
        private const string MenuLightCoreOcclusionShaderPath = "Assets/MenuLightCoreOcclusion.shader";
        private const string MenuLightCubeSpillShaderPath = "Assets/MenuLightCubeSpill.shader";
        private const string SnapshotReadbackShaderPath = "Assets/FerrofluidSnapshotReadback.shader";
        private const string ThinFilmPath = GeneratedAssetDirectory + "/FerroLUT.png";
        private const string EnvironmentPath = GeneratedAssetDirectory + "/env-map-01.jpg";

        // Beat Saber cannot compile ShaderLab at runtime, so this deterministic editor entry point packages the HLSL and its two authoritative lookup textures into one Windows bundle.
        public static void BuildWindows()
        {
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
                throw new InvalidOperationException("Build the ferrofluid bundle in Linear color space to preserve the captured HDR radiance used by Beat Saber.");

            // Unity strips every stereo shader variant (STEREO_INSTANCING_ON, UNITY_SINGLE_PASS_STEREO,
            // STEREO_MULTIVIEW_ON, STEREO_CUBEMAP_RENDER_ON) from builds — including asset bundles —
            // when no XR loader is configured for the target. A bundle built without an XR provider
            // renders the surface only in the left eye under single-pass instanced rendering, so the
            // MockHMD loader must be assigned before BuildAssetBundles runs.
            EditorShared.MockHmdProjectSetup.EnsureConfigured();

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string repositoryRoot = Directory.GetParent(projectRoot).FullName;
            string sourceAssetRoot = Path.Combine(projectRoot, "SourceTextures");
            Directory.CreateDirectory(GeneratedAssetDirectory);
            CopySourceAsset(Path.Combine(sourceAssetRoot, "FerroLUT.png"), ThinFilmPath);
            CopySourceAsset(Path.Combine(sourceAssetRoot, "env-map-01.jpg"), EnvironmentPath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureTextures(ThinFilmPath, EnvironmentPath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            FerrofluidRoomReflectionAssets.Build(repositoryRoot);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateShader(SimulationShaderPath);
            ValidateShader(SurfaceShaderPath);
            ValidateShader(FloorShadowShaderPath);
            ValidateShader(ParabolicFloorReflectionShaderPath);
            ValidateShader(MenuLightCoreOcclusionShaderPath);
            ValidateShader(MenuLightCubeSpillShaderPath);
            ValidateShader(SnapshotReadbackShaderPath);

            // The core bundle is embedded by the .NET project; the reflection cubemap pack is a
            // separate optional download the runtime opens from disk, never embedded in the DLL.
            string outputDirectory = Path.Combine(repositoryRoot, "ChromaGLS", "Settings");
            Directory.CreateDirectory(outputDirectory);
            AssetBundleBuild coreBuild = new AssetBundleBuild
            {
                assetBundleName = "ferrofluid-windows.bundle",
                assetNames = new[]
                {
                    SimulationShaderPath,
                    SurfaceShaderPath,
                    FloorShadowShaderPath,
                    ParabolicFloorReflectionShaderPath,
                    MenuLightCoreOcclusionShaderPath,
                    MenuLightCubeSpillShaderPath,
                    SnapshotReadbackShaderPath,
                    ThinFilmPath,
                    EnvironmentPath,
                },
            };
            AssetBundleBuild reflectionsBuild = new AssetBundleBuild
            {
                assetBundleName = "ferrofluid-reflections-windows.bundle",
                assetNames = new[]
                {
                    FerrofluidRoomReflectionAssets.OnAssetPath,
                    FerrofluidRoomReflectionAssets.OffAssetPath,
                    FerrofluidRoomReflectionAssets.FloorFeetAssetPath,
                    FerrofluidRoomReflectionAssets.RoomManifestPath,
                    FerrofluidRoomReflectionAssets.HighOnAssetPath,
                    FerrofluidRoomReflectionAssets.HighOffAssetPath,
                    FerrofluidRoomReflectionAssets.HighFloorFeetAssetPath,
                    FerrofluidRoomReflectionAssets.HighRoomManifestPath,
                },
            };
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                outputDirectory,
                new[] { coreBuild, reflectionsBuild },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                BuildTarget.StandaloneWindows64);
            string corePath = Path.Combine(outputDirectory, coreBuild.assetBundleName);
            string reflectionsPath = Path.Combine(outputDirectory, reflectionsBuild.assetBundleName);
            if (manifest == null || !File.Exists(corePath) || !File.Exists(reflectionsPath))
            {
                throw new InvalidOperationException($"Ferrofluid AssetBundles were not produced at {corePath} and {reflectionsPath}.");
            }

            // ChromaGLS does not consume Unity's loose bundle manifests, so remove those generated siblings instead of littering the source Settings directory.
            DeleteIfPresent(corePath + ".manifest");
            DeleteIfPresent(reflectionsPath + ".manifest");
            string rootManifestPath = Path.Combine(outputDirectory, new DirectoryInfo(outputDirectory).Name);
            DeleteIfPresent(rootManifestPath);
            DeleteIfPresent(rootManifestPath + ".manifest");
            Debug.Log($"Built ChromaGLS ferrofluid bundle: {corePath} ({new FileInfo(corePath).Length} bytes)");
            Debug.Log($"Built ChromaGLS ferrofluid reflections bundle: {reflectionsPath} ({new FileInfo(reflectionsPath).Length} bytes)");
            // GameCI validates this exact stdout marker even when Unity exits successfully after a custom build method.
            Console.WriteLine("Build succeeded!");
        }

        // Generated manifests are build metadata rather than runtime dependencies, so cleanup is explicit and restricted to files emitted by this bundle invocation.
        private static void DeleteIfPresent(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static void CopySourceAsset(string sourcePath, string destinationPath)
        {
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Required ferrofluid source asset was not found.", sourcePath);
            }

            File.Copy(sourcePath, destinationPath, true);
        }

        internal static void ConfigureTextures(string thinFilmPath, string environmentPath)
        {
            // Forced anisotropy underfilters LUT bands. Bias only the minified lookup footprint.
            ConfigureTexture(thinFilmPath, TextureWrapMode.Clamp, FilterMode.Trilinear, 0, 0.5f);
            ConfigureTexture(environmentPath, TextureWrapMode.Repeat, FilterMode.Bilinear, 1, 0f);
        }

        private static void ConfigureTexture(
            string assetPath,
            TextureWrapMode wrapMode,
            FilterMode filterMode,
            int anisotropy,
            float mipBias)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"Could not import texture {assetPath}.");
            }

            importer.sRGBTexture = false;
            importer.mipmapEnabled = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = wrapMode;
            // V clamps for both assets so the pole texels cannot bleed across the image edge.
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.filterMode = filterMode;
            importer.anisoLevel = anisotropy;
            importer.mipMapBias = mipBias;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }

        // Shader compiler diagnostics are promoted to build failures so a green .NET build can never embed magenta fallback assets.
        private static void ValidateShader(string assetPath)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
            if (shader == null)
            {
                throw new InvalidOperationException($"Could not import shader {assetPath}.");
            }

            ShaderMessage[] errors = ShaderUtil.GetShaderMessages(shader)
                .Where(message => message.severity == ShaderCompilerMessageSeverity.Error)
                .ToArray();
            if (errors.Length > 0)
            {
                string detail = string.Join(Environment.NewLine, errors.Select(error => $"{error.platform}: {error.message} ({error.line})"));
                throw new InvalidOperationException($"Shader compilation failed for {assetPath}:{Environment.NewLine}{detail}");
            }
        }
    }
}
