using System;
using System.IO;
using ChromaGLS.EditorShared;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ChromaGLS.StereoTest.Editor
{
    // Enables the Mock HMD XR provider in Single Pass Instanced mode for this project, then builds a
    // standalone player that runs FerrofluidStereoCapture. A built player is required because editor
    // play mode in batchmode never runs the render pipeline (no Game View repaint), so no XR render
    // passes ever populate. Invoked via -executeMethod FerrofluidStereoRunner.Run in batch mode; the
    // PowerShell driver then launches the built player.
    public static class FerrofluidStereoRunner
    {
        private const string ScenePath = "Assets/StereoTest/StereoTest.unity";
        private const string BuildOutput = "Build/StereoTest/StereoTest.exe";

        public static void Run()
        {
            try
            {
                MockHmdProjectSetup.EnsureConfigured();
                DumpBundleShaderInfo();
                BuildPlayer();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[FerrofluidStereo] runner failed: {exception}");
                EditorApplication.Exit(3);
            }
        }

        private static void BuildPlayer()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var harness = new GameObject("Ferrofluid Stereo Harness");
            FerrofluidStereoCapture capture = harness.AddComponent<FerrofluidStereoCapture>();
            capture.stereoControlShader = Shader.Find("ChromaGLS/StereoControl");
            capture.ferrofluidSourceSurface = Shader.Find("ChromaGLS/FerrofluidSurface");
            if (capture.stereoControlShader == null || capture.ferrofluidSourceSurface == null)
            {
                Debug.LogError("[FerrofluidStereo] control/source shader not found — build will lack the SPI reference objects");
            }

            EditorSceneManager.SaveScene(harness.scene, ScenePath);

            string outputPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", BuildOutput));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[FerrofluidStereo] player build {report.summary.result} -> {outputPath} ({report.summary.totalErrors} errors)");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 5);
        }

        // Variant diagnostics for the shipped bundle: if the stereo-instanced variant is missing from
        // the compiled shader, the count/keyword dump here explains the single-eye render before the
        // capture even runs.
        private static void DumpBundleShaderInfo()
        {
            string bundlePath = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "ChromaGLS", "Settings", "ferrofluid-windows.bundle"));
            if (!File.Exists(bundlePath))
            {
                Debug.LogError($"[FerrofluidStereo] bundle missing at {bundlePath}");
                return;
            }

            AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
            Shader surface = bundle.LoadAsset<Shader>("assets/ferrofluidsurface.shader");
            Shader simulation = bundle.LoadAsset<Shader>("assets/ferrofluidsimulation.shader");
            foreach (Shader shader in new[] { surface, simulation })
            {
                if (shader == null)
                {
                    continue;
                }

                string keywordNames = string.Join(", ", shader.keywordSpace.keywordNames);
                Debug.Log($"[FerrofluidStereo] bundle shader '{shader.name}' keywords=[{keywordNames}] passes={shader.passCount}");
            }

            bundle.Unload(false);
        }
    }
}
