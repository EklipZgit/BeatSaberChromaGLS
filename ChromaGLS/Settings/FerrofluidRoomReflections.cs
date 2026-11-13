using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using Zenject;

namespace ChromaGLS.Settings
{
    internal sealed class FerrofluidReflectionLifecycle : IInitializable, IDisposable
    {
        private readonly GameScenesManager _sceneManager;

        public FerrofluidReflectionLifecycle(GameScenesManager sceneManager)
        {
            _sceneManager = sceneManager;
        }

        public void Initialize()
        {
            FerrofluidRoomReflections.BeginMenuSession(_sceneManager);
        }

        public void Dispose()
        {
            FerrofluidRoomReflections.EndMenuSession();
        }
    }

    internal sealed class FerrofluidRoomReflections : MonoBehaviour
    {
        private const string RoomTextureName = "_FerrofluidRoomEnvironment";
        private const string RoomKeyword = "FERROFLUID_ROOM_REFLECTIONS";
        private const string DisabledKeyword = "FERROFLUID_NO_REFLECTIONS";
        private const string FloorTextureName = "_FerrofluidFloorReflection";
        private const string FloorBoundsName = "_FerrofluidFloorReflectionBounds";
        private const string FloorYName = "_FerrofluidFloorReflectionY";
        private const string RoomCapturePositionName = "_FerrofluidRoomCapturePosition";
        private const string RoomFloorYName = "_FerrofluidRoomFloorY";
        private const string RoomLogoPlaneName = "_FerrofluidRoomLogoPlane";
        private static GameScenesManager? _sceneManager;
        private static FerrofluidRoomReflections? _host;
        private static Cubemap? _environmentOn;
        private static Cubemap? _environmentOff;
        private static Cubemap? _selected;
        private static FlickeringNeonSign? _targetSign;
        private static bool _environmentRequested;
        private static bool _running;
        private static int _requestVersion;
        private static ReflectionQuality _selectedQuality = ReflectionQuality.Low;

        internal static event Action? ReflectionsRequested;

        internal static bool EnvironmentRequested => _environmentRequested;

        internal static void SetReflectionQuality(ReflectionQuality quality)
        {
            bool changed = quality != _selectedQuality;
            _selectedQuality = quality;
            if (quality == ReflectionQuality.None || _environmentOn == null)
                Shader.EnableKeyword(DisabledKeyword);

            if (!changed)
                return;

            ClearBindings();
            FerrofluidGpuResources.ReleaseReflectionAssets();
            _requestVersion++;
            if (quality != ReflectionQuality.None && _environmentRequested)
                ReflectionsRequested?.Invoke();
            if (quality != ReflectionQuality.None && _environmentRequested && !_running)
                Schedule();
        }

        private static void ClearBindings()
        {
            // With nothing bound the shader must not fall back to the legacy environment map.
            Shader.EnableKeyword(DisabledKeyword);
            Shader.DisableKeyword(RoomKeyword);
            Shader.SetGlobalTexture(RoomTextureName, null);
            Shader.SetGlobalTexture(FloorTextureName, null);
            Shader.SetGlobalVector(FloorBoundsName, Vector4.zero);
            Shader.SetGlobalFloat(FloorYName, 0f);
            Shader.SetGlobalVector(RoomCapturePositionName, Vector4.zero);
            Shader.SetGlobalFloat(RoomFloorYName, 0f);
            Shader.SetGlobalVector(RoomLogoPlaneName, Vector4.zero);
            _environmentOn = null;
            _environmentOff = null;
            _selected = null;
            _targetSign = null;
        }

        internal static void BeginMenuSession(GameScenesManager sceneManager)
        {
            _sceneManager = sceneManager;
            if (_environmentRequested)
                Schedule();
        }

        internal static void EndMenuSession()
        {
            ClearBindings();
            FerrofluidGpuResources.ReleaseReflectionAssets();
            if (_host != null)
            {
                _host.gameObject.SetActive(false);
                Destroy(_host.gameObject);
            }

            _host = null;
            _sceneManager = null;
            _environmentRequested = false;
            _running = false;
            _requestVersion++;
        }

        internal static void RequestEnvironment()
        {
            _environmentRequested = true;
            ReflectionsRequested?.Invoke();
            if (_environmentOn == null && _selectedQuality != ReflectionQuality.None)
                Schedule();
        }

        private static void Schedule()
        {
            if (_sceneManager == null || _running || _selectedQuality == ReflectionQuality.None || !FerrofluidGpuResources.ReflectionPackAvailable)
                return;

            try
            {
                if (_host == null)
                {
                    GameObject hostObject = new(nameof(FerrofluidRoomReflections)) { hideFlags = HideFlags.DontSave };
                    _host = hostObject.AddComponent<FerrofluidRoomReflections>();
                }

                _running = true;
                _host.StartCoroutine(ApplyWhenReady());
            }
            catch (Exception ex)
            {
                _running = false;
                Plugin.Log.Warn($"[ChromaGLS Ferrofluid] room reflection binding failed to schedule: {ex.Message}");
            }
        }

        private static IEnumerator ApplyWhenReady()
        {
            while (_sceneManager!.isInTransition)
                yield return null;

            yield return new WaitForEndOfFrame();

            // Optional reflection pack not installed means reflections stay disabled quietly.
            if (_selectedQuality != ReflectionQuality.None && FerrofluidGpuResources.ReflectionPackAvailable)
            {
                int version = _requestVersion;
                FerrofluidRoomReflections? host = _host;
                Task<bool> bind = BindRoomReflectionsAsync(version, host);
                while (!bind.IsCompleted)
                    yield return null;

                // A superseded request (quality/path/menu change) must not clear newer bindings.
                if (version != _requestVersion || !ReferenceEquals(host, _host))
                {
                    _running = false;
                    Schedule();
                    yield break;
                }
            }

            _running = false;
        }

        private static async Task<bool> BindRoomReflectionsAsync(int version, FerrofluidRoomReflections? host)
        {
            try
            {
                if (!FerrofluidGpuResources.TryGet(out FerrofluidGpuResources? resources)
                    || !await resources!.TryLoadReflectionsAsync(_selectedQuality)
                    || resources.RoomEnvironmentOn == null
                    || resources.RoomEnvironmentOff == null)
                {
                    if (version == _requestVersion && ReferenceEquals(host, _host))
                    {
                        ClearBindings();
                        Plugin.Log.Error("[ChromaGLS Ferrofluid] optional reflection pack could not be loaded; reflections disabled.");
                    }

                    return false;
                }

                if (version != _requestVersion || !ReferenceEquals(host, _host))
                    return false;

                ApplyFloorEnvironment(resources.FloorReflectionTexture!, resources.FloorReflectionBounds, resources.FloorReflectionY);

                // Projection data must be bound before the room keyword selects it.
                ApplyRoomProjection(
                    resources.RoomCapturePosition,
                    resources.RoomFloorY,
                    resources.RoomLogoPlane);
                ApplyEnvironment(resources.RoomEnvironmentOn, resources.RoomEnvironmentOff, FindMenuSign());
                Plugin.Log.Info($"[ChromaGLS Ferrofluid] baked room reflections bound ({resources.RoomEnvironmentOn.graphicsFormat} {_environmentOn!.width}x{_environmentOn.width} per face, {(_targetSign != null ? "neon on/off pair" : "static room")}).");
                return true;
            }
            catch (Exception ex)
            {
                if (version == _requestVersion && ReferenceEquals(host, _host))
                {
                    ClearBindings();
                    Plugin.Log.Warn($"[ChromaGLS Ferrofluid] room reflection binding failed: {ex.Message}");
                }

                return false;
            }
        }

        internal static void ApplyFloorEnvironment(Texture2D feet, Vector4 bounds, float planeY)
        {
            if (_selectedQuality == ReflectionQuality.None)
                return;
            Shader.SetGlobalTexture(FloorTextureName, feet);
            Shader.SetGlobalVector(FloorBoundsName, bounds);
            Shader.SetGlobalFloat(FloorYName, planeY);
        }

        internal static void ApplyEnvironment(Cubemap on, Cubemap off, FlickeringNeonSign? sign)
        {
            if (_selectedQuality == ReflectionQuality.None)
                return;
            bool liveOn = sign == null || sign._flickeringSprite.color.maxColorComponent > 0f;
            _environmentOn = on;
            _environmentOff = off;
            _targetSign = sign;
            _selected = liveOn ? on : off;
            Shader.SetGlobalTexture(RoomTextureName, _selected);
            Shader.EnableKeyword(RoomKeyword);
            Shader.DisableKeyword(DisabledKeyword);
        }

        internal static void SetReflectionPackPath(string? path)
        {
            if (path == FerrofluidGpuResources.ReflectionPackPath)
                return;
            FerrofluidGpuResources.ConfigureReflectionPack(path);
            ClearBindings();
            FerrofluidGpuResources.ReleaseReflectionAssets();
            _requestVersion++;
            if (path != null && _environmentRequested && _selectedQuality != ReflectionQuality.None && !_running)
                Schedule();
        }

        internal static void ApplyRoomProjection(
            Vector4 capturePosition,
            float floorY,
            Vector4 logoPlane)
        {
            if (_selectedQuality == ReflectionQuality.None)
                return;
            Shader.SetGlobalVector(RoomCapturePositionName, capturePosition);
            Shader.SetGlobalFloat(RoomFloorYName, floorY);
            Shader.SetGlobalVector(RoomLogoPlaneName, logoPlane);
        }

        internal static void NotifyNeonState(FlickeringNeonSign sign, bool on)
        {
            if (!ReferenceEquals(sign, _targetSign))
                return;

            Cubemap? selected = on ? _environmentOn : _environmentOff;
            if (ReferenceEquals(selected, _selected))
                return;

            _selected = selected;
            Shader.SetGlobalTexture(RoomTextureName, selected);
        }

        private static FlickeringNeonSign? FindMenuSign()
        {
            foreach (FlickeringNeonSign sign in Resources.FindObjectsOfTypeAll<FlickeringNeonSign>())
            {
                if (sign.gameObject.activeInHierarchy && sign.gameObject.scene.IsValid())
                    return sign;
            }

            return null;
        }
    }
}
