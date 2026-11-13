using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ChromaGLS.Settings
{
    // Load the compiled ferrofluid shaders and textures from the embedded AssetBundle.
    internal sealed class FerrofluidGpuResources
    {
        internal const string BundleResourceName = "ChromaGLS.Settings.ferrofluid-windows.bundle";
        private const string SimulationShaderAsset = "assets/ferrofluidsimulation.shader";
        private const string SurfaceShaderAsset = "assets/ferrofluidsurface.shader";
        private const string ThinFilmAsset = "assets/generated/ferrolut.png";
        private const string EnvironmentAsset = "assets/generated/env-map-01.jpg";
        private const string FloorShadowAsset = "assets/ferrofluidfloorshadow.shader";
        private const string ParabolicFloorReflectionAsset = "assets/parabolicfloorreflection.shader";
        private const string MenuLightCoreOcclusionAsset = "assets/menulightcoreocclusion.shader";
        private const string MenuLightCubeSpillAsset = "assets/menulightcubespill.shader";
        private const string RoomOnAsset = "assets/generated/room-reflections-on.cubemap";
        private const string RoomOffAsset = "assets/generated/room-reflections-off.cubemap";
        private const string FloorFeetAsset = "assets/generated/floor-feet.asset";
        private const string RoomManifestAsset = "assets/generated/room-reflections.json";
        private const string RoomOnHighAsset = "assets/generated/room-reflections-high-on.cubemap";
        private const string RoomOffHighAsset = "assets/generated/room-reflections-high-off.cubemap";
        private const string FloorFeetHighAsset = "assets/generated/floor-feet-high.asset";
        private const string RoomManifestHighAsset = "assets/generated/room-reflections-high.json";
#if FERROFLUID_TWEAKS
        private const string SnapshotReadbackAsset = "assets/ferrofluidsnapshotreadback.shader";
#endif
        private static FerrofluidGpuResources? _instance;
#if FERROFLUID_REFLECTION_PACK_UNAVAILABLE
        internal const bool SupportsReflectionPacks = false;
#else
        internal const bool SupportsReflectionPacks = true;
#endif
        private static AssetBundle? _bundle;
        private static string? _reflectionPackPath;
        private static bool _attemptedLoad;
        private readonly SemaphoreSlim _loadGate = new(1, 1);
        private int _loadGeneration;
        private ReflectionQuality? _loadedTier;
        private AssetBundle? _reflectionBundle;

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

        internal Shader? FloorShadowShader { get; private set; }

        internal Shader? ParabolicFloorReflectionShader { get; private set; }

        internal Shader? MenuLightCoreOcclusionShader { get; private set; }

        internal Shader? MenuLightCubeSpillShader { get; private set; }

#if FERROFLUID_TWEAKS
        internal Shader? SnapshotReadbackShader { get; private set; }
#endif

        internal Cubemap? RoomEnvironmentOn { get; private set; }

        internal Cubemap? RoomEnvironmentOff { get; private set; }

        internal Texture2D? FloorReflectionTexture { get; private set; }

        internal Vector4 RoomCapturePosition { get; private set; }

        internal float RoomFloorY { get; private set; }

        internal Vector4 RoomLogoPlane { get; private set; }

        internal Vector4 FloorReflectionBounds { get; private set; }

        internal float FloorReflectionY { get; private set; }

        internal static string? ReflectionPackPath => _reflectionPackPath;

        internal static bool ReflectionPackAvailable => _reflectionPackPath != null;

        internal async Task<bool> TryLoadReflectionsAsync(ReflectionQuality quality)
        {
            if (_loadedTier == quality)
                return true;
            if (quality == ReflectionQuality.None)
            {
                ReleaseOwned();
                return true;
            }
#if FERROFLUID_REFLECTION_PACK_UNAVAILABLE
            return false;
#else
            if (_reflectionPackPath == null)
                return false;

            // Increment before the gate so a release or path change between requests
            // can invalidate whichever request finishes behind the other.
            int generation = ++_loadGeneration;
            await _loadGate.WaitAsync();
            AssetBundle? pack = null;
            try
            {
                if (generation != _loadGeneration)
                    return false;
                ClearOwnedData();
                pack = await AssetBundleLoadingTools.Utilities.AssetBundleExtensions.LoadFromFileAsync(_reflectionPackPath);
                if (pack == null || generation != _loadGeneration)
                    return false;

                int roomResolution = ReflectionQualitySettings.GetRoomResolution(quality);
                int floorResolution = ReflectionQualitySettings.GetResolution(quality);
                bool high = quality == ReflectionQuality.High;
                Cubemap? on = pack.LoadAsset<Cubemap>(high ? RoomOnHighAsset : RoomOnAsset);
                Cubemap? off = pack.LoadAsset<Cubemap>(high ? RoomOffHighAsset : RoomOffAsset);
                Texture2D? feet = pack.LoadAsset<Texture2D>(high ? FloorFeetHighAsset : FloorFeetAsset);
                TextAsset? manifest = pack.LoadAsset<TextAsset>(high ? RoomManifestHighAsset : RoomManifestAsset);
                Vector4 bounds = Vector4.zero;
                float planeY = 0f;
                Vector4 capturePosition = Vector4.zero;
                float floorY = 0f;
                Vector4 logoPlane = Vector4.zero;
                bool valid = on != null
                    && off != null
                    && feet != null
                    && manifest != null
                    && on.width == roomResolution
                    && off.width == roomResolution
                    && feet.width == floorResolution
                    && feet.height == floorResolution
                    && TryParseFloorReflection(manifest!.text, out bounds, out planeY)
                    && TryParseRoomProjection(quality, manifest!.text, out capturePosition, out floorY, out logoPlane);

                if (!valid)
                {
                    Plugin.Log.Warn($"[ChromaGLS Ferrofluid] {quality} reflection assets are unavailable in the reflection pack; reflections stay unloaded.");
                    return false;
                }

                _reflectionBundle = pack;
                pack = null;
                RoomEnvironmentOn = on;
                RoomEnvironmentOff = off;
                FloorReflectionTexture = feet;
                RoomCapturePosition = capturePosition;
                RoomFloorY = floorY;
                RoomLogoPlane = logoPlane;
                FloorReflectionBounds = bounds;
                FloorReflectionY = planeY;
                _loadedTier = quality;
                return true;
            }
            finally
            {
                if (pack != null)
                    pack.Unload(true);
                _loadGate.Release();
            }
#endif
        }

        internal static void ConfigureReflectionPack(string? path)
        {
            if (path == _reflectionPackPath)
                return;
            _reflectionPackPath = path;
            _instance?.ReleaseOwned();
        }

        internal static void ReleaseReflectionAssets()
        {
            _instance?.ReleaseOwned();
        }

        private void ReleaseOwned()
        {
            _loadGeneration++;
            ClearOwnedData();
        }

        private void ClearOwnedData()
        {
            // The core container was already released with Unload(false); its shader, LUT and
            // fallback objects live on independently. The tier container owns only the loaded
            // reflection textures, so unloading it destroys them without touching core assets.
            if (_reflectionBundle != null)
            {
                _reflectionBundle.Unload(true);
                _reflectionBundle = null;
            }

            RoomEnvironmentOn = null;
            RoomEnvironmentOff = null;
            FloorReflectionTexture = null;
            RoomCapturePosition = Vector4.zero;
            RoomFloorY = 0f;
            RoomLogoPlane = Vector4.zero;
            FloorReflectionBounds = Vector4.zero;
            FloorReflectionY = 0f;
            _loadedTier = null;
        }

        internal static bool TryParseFloorReflection(string manifestJson, out Vector4 bounds, out float planeY)
        {
            bounds = Vector4.zero;
            planeY = 0f;
            try
            {
                JObject? floor = JObject.Parse(manifestJson).SelectToken("floorReflection") as JObject;
                if (floor == null)
                    return false;

                float? minimumX = floor.Value<float?>("minimumX");
                float? minimumZ = floor.Value<float?>("minimumZ");
                float? inverseWidth = floor.Value<float?>("inverseWidth");
                float? inverseDepth = floor.Value<float?>("inverseDepth");
                float? y = floor.Value<float?>("planeY");
                if (!minimumX.HasValue || !minimumZ.HasValue || !inverseWidth.HasValue || !inverseDepth.HasValue || !y.HasValue)
                    return false;
                if (!Finite(minimumX.Value) || !Finite(minimumZ.Value) || !Finite(y.Value)
                    || !Finite(inverseWidth.Value) || !Finite(inverseDepth.Value)
                    || inverseWidth.Value <= 0f || inverseDepth.Value <= 0f)
                    return false;

                planeY = y.Value;
                bounds = new Vector4(minimumX.Value, minimumZ.Value, inverseWidth.Value, inverseDepth.Value);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool TryParseRoomProjection(
            ReflectionQuality quality,
            string manifestJson,
            out Vector4 capturePosition,
            out float floorY,
            out Vector4 logoPlane)
        {
            capturePosition = Vector4.zero;
            floorY = 0f;
            logoPlane = Vector4.zero;
            try
            {
                JObject root = JObject.Parse(manifestJson);
                int roomResolution = ReflectionQualitySettings.GetRoomResolution(quality);
                if (root.Value<int?>("faceSize") != roomResolution)
                    return false;

                JObject? projection = root["roomProjection"] as JObject;
                if (projection == null)
                    return false;

                JArray? capture = projection["capturePosition"] as JArray;
                JArray? plane = projection["logoPlane"] as JArray;
                float? floor = projection.Value<float?>("floorY");
                if (capture == null || capture.Count != 3
                    || plane == null || plane.Count != 4
                    || !floor.HasValue)
                    return false;

                for (int index = 0; index < 3; index++)
                {
                    if (!Finite(capture[index]!.Value<float>()))
                    {
                        return false;
                    }
                }

                for (int index = 0; index < 4; index++)
                {
                    if (!Finite(plane[index]!.Value<float>()))
                    {
                        return false;
                    }
                }

                if (!Finite(floor.Value))
                    return false;
                float planeLength = Mathf.Sqrt(
                    (plane[0]!.Value<float>() * plane[0]!.Value<float>())
                    + (plane[1]!.Value<float>() * plane[1]!.Value<float>())
                    + (plane[2]!.Value<float>() * plane[2]!.Value<float>()));
                if (!Finite(planeLength) || planeLength <= 0f)
                    return false;

                capturePosition = new Vector4(capture[0]!.Value<float>(), capture[1]!.Value<float>(), capture[2]!.Value<float>(), 0f);
                floorY = floor.Value;
                logoPlane = new Vector4(plane[0]!.Value<float>(), plane[1]!.Value<float>(), plane[2]!.Value<float>(), plane[3]!.Value<float>());
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

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
                Shader? floorShadowShader = _bundle.LoadAsset<Shader>(FloorShadowAsset);
                Shader? parabolicFloorReflectionShader = _bundle.LoadAsset<Shader>(ParabolicFloorReflectionAsset);
                if (simulationShader == null
                    || surfaceShader == null
                    || thinFilmTexture == null
                    || environmentTexture == null
                    || floorShadowShader == null
                    || !simulationShader.isSupported
                    || !surfaceShader.isSupported
                    || !floorShadowShader.isSupported)
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
                    environmentTexture)
                {
                    FloorShadowShader = floorShadowShader,
                    ParabolicFloorReflectionShader = parabolicFloorReflectionShader,
                    MenuLightCoreOcclusionShader = _bundle.LoadAsset<Shader>(MenuLightCoreOcclusionAsset),
                    MenuLightCubeSpillShader = _bundle.LoadAsset<Shader>(MenuLightCubeSpillAsset),
#if FERROFLUID_TWEAKS
                    SnapshotReadbackShader = _bundle.LoadAsset<Shader>(SnapshotReadbackAsset),
#endif
                };

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
