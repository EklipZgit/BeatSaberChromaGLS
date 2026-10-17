using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ChromaGLS.Settings
{
    // Load the compiled ferrofluid shaders and textures from the embedded AssetBundle.
    internal sealed class FerrofluidGpuResources
    {
        private const string BundleResourceName = "ChromaGLS.Settings.ferrofluid-windows.bundle";
        private const string SimulationShaderAsset = "assets/ferrofluidsimulation.shader";
        private const string SurfaceShaderAsset = "assets/ferrofluidsurface.shader";
        private const string ThinFilmAsset = "assets/generated/ferrolut.png";
        private const string EnvironmentAsset = "assets/generated/env-map-01.jpg";
        private static FerrofluidGpuResources? _instance;
        private static AssetBundle? _bundle;
        private static bool _attemptedLoad;

        private FerrofluidGpuResources(
            Shader simulationShader,
            Shader surfaceShader,
            Texture2D thinFilmTexture,
            Texture2D environmentTexture)
        {
            SimulationShader = simulationShader;
            SurfaceShader = surfaceShader;
            ThinFilmTexture = thinFilmTexture;
            EnvironmentTexture = environmentTexture;
        }

        internal Shader SimulationShader { get; }

        internal Shader SurfaceShader { get; }

        internal Texture2D ThinFilmTexture { get; }

        internal Texture2D EnvironmentTexture { get; }

        // Cache load failure until restart so song selection cannot repeatedly allocate resources or log the same error.
        internal static bool TryGet(out FerrofluidGpuResources? resources)
        {
            if (_instance != null)
            {
                resources = _instance;
                return true;
            }

            if (_attemptedLoad)
            {
                resources = null;
                return false;
            }

            _attemptedLoad = true;
            try
            {
                Assembly assembly = typeof(FerrofluidGpuResources).Assembly;
                using Stream? stream = assembly.GetManifestResourceStream(BundleResourceName);
                if (stream == null)
                {
                    Plugin.Log.Error($"[ChromaGLS Ferrofluid] embedded shader bundle {BundleResourceName} was not found; visual disabled.");
                    resources = null;
                    return false;
                }

                byte[] bundleBytes = new byte[stream.Length];
                int offset = 0;
                while (offset < bundleBytes.Length)
                {
                    int read = stream.Read(bundleBytes, offset, bundleBytes.Length - offset);
                    if (read == 0)
                    {
                        throw new EndOfStreamException($"Read {offset} of {bundleBytes.Length} ferrofluid bundle bytes.");
                    }

                    offset += read;
                }

                _bundle = AssetBundle.LoadFromMemory(bundleBytes);
                if (_bundle == null)
                {
                    Plugin.Log.Error($"[ChromaGLS Ferrofluid] Unity {Application.unityVersion} rejected the compiled shader bundle; visual disabled on this game version.");
                    resources = null;
                    return false;
                }

                Shader? simulationShader = _bundle.LoadAsset<Shader>(SimulationShaderAsset);
                Shader? surfaceShader = _bundle.LoadAsset<Shader>(SurfaceShaderAsset);
                Texture2D? thinFilmTexture = _bundle.LoadAsset<Texture2D>(ThinFilmAsset);
                Texture2D? environmentTexture = _bundle.LoadAsset<Texture2D>(EnvironmentAsset);
                if (simulationShader == null
                    || surfaceShader == null
                    || thinFilmTexture == null
                    || environmentTexture == null
                    || !simulationShader.isSupported
                    || !surfaceShader.isSupported)
                {
                    Plugin.Log.Error($"[ChromaGLS Ferrofluid] bundle loaded in Unity {Application.unityVersion}, but a required shader/texture was missing or unsupported; visual disabled.");
                    _bundle.Unload(true);
                    _bundle = null;
                    resources = null;
                    return false;
                }

                _instance = new FerrofluidGpuResources(
                    simulationShader,
                    surfaceShader,
                    thinFilmTexture,
                    environmentTexture);

                // Unload(false) keeps the loaded shaders and textures while releasing the bundle container.
                _bundle.Unload(false);
                _bundle = null;
                Plugin.Log.Info($"[ChromaGLS Ferrofluid] GPU assets loaded in Unity {Application.unityVersion}; renderer={SystemInfo.graphicsDeviceName} api={SystemInfo.graphicsDeviceType}.");
                resources = _instance;
                return true;
            }
            catch (Exception exception)
            {
                // Release a partially loaded bundle before caching the failure.
                if (_bundle != null)
                {
                    _bundle.Unload(true);
                    _bundle = null;
                }

                Plugin.Log.Error($"[ChromaGLS Ferrofluid] shader bundle initialization failed; visual disabled. {exception}");
                resources = null;
                return false;
            }
        }
    }
}
