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
        private const string ThinFilmPath = GeneratedAssetDirectory + "/FerroLUT.png";
        private const string EnvironmentPath = GeneratedAssetDirectory + "/env-map-01.jpg";

        // Beat Saber cannot compile ShaderLab at runtime, so this deterministic editor entry point packages the HLSL and its two authoritative lookup textures into one Windows bundle.
        public static void BuildWindows()
        {
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
            ConfigureTexture(ThinFilmPath, TextureWrapMode.Clamp, false);
            ConfigureTexture(EnvironmentPath, TextureWrapMode.Repeat, true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateShader(SimulationShaderPath);
            ValidateShader(SurfaceShaderPath);

            // The generated bundle is embedded by the .NET project, keeping deployment atomic and avoiding loose files beside the plugin DLL.
            string outputDirectory = Path.Combine(repositoryRoot, "ChromaGLS", "Settings");
            Directory.CreateDirectory(outputDirectory);
            AssetBundleBuild build = new AssetBundleBuild
            {
                assetBundleName = "ferrofluid-windows.bundle",
                assetNames = new[]
                {
                    SimulationShaderPath,
                    SurfaceShaderPath,
                    ThinFilmPath,
                    EnvironmentPath,
                },
            };
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                outputDirectory,
                new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                BuildTarget.StandaloneWindows64);
            string outputPath = Path.Combine(outputDirectory, build.assetBundleName);
            if (manifest == null || !File.Exists(outputPath))
            {
                throw new InvalidOperationException($"Ferrofluid AssetBundle was not produced at {outputPath}.");
            }

            // ChromaGLS embeds one named bundle and does not consume Unity's loose bundle manifests, so remove those generated siblings instead of littering the source Settings directory.
            DeleteIfPresent(outputPath + ".manifest");
            string rootManifestPath = Path.Combine(outputDirectory, new DirectoryInfo(outputDirectory).Name);
            DeleteIfPresent(rootManifestPath);
            DeleteIfPresent(rootManifestPath + ".manifest");
            Debug.Log($"Built ChromaGLS ferrofluid bundle: {outputPath} ({new FileInfo(outputPath).Length} bytes)");
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

        // Uncompressed linear sampling matches WebGL's numeric texture lookup; only the environment map needs mipmaps while the LUT must retain literal rows.
        private static void ConfigureTexture(string assetPath, TextureWrapMode wrapMode, bool mipmaps)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"Could not import texture {assetPath}.");
            }

            importer.sRGBTexture = false;
            importer.mipmapEnabled = mipmaps;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = wrapMode;
            importer.filterMode = FilterMode.Bilinear;
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
