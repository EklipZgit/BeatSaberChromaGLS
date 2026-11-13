using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Zenject;

namespace ChromaGLS.Settings
{
    internal struct ParabolicFloorLightMeshData
    {
        internal readonly Vector3[] Vertices;
        internal readonly int[] Triangles;
        internal readonly Color[] Colors;
        internal readonly Vector2[] UV;

        internal ParabolicFloorLightMeshData(Vector3[] vertices, int[] triangles, Color[] colors, Vector2[] uv)
        {
            Vertices = vertices;
            Triangles = triangles;
            Colors = colors;
            UV = uv;
        }
    }

    internal struct ParabolicFloorLightGlowMeshData
    {
        internal readonly Vector3[] Vertices;
        internal readonly int[] Triangles;
        internal readonly Color[] Colors;
        internal readonly Vector2[] UV;

        internal ParabolicFloorLightGlowMeshData(Vector3[] vertices, int[] triangles, Color[] colors, Vector2[] uv)
        {
            Vertices = vertices;
            Triangles = triangles;
            Colors = colors;
            UV = uv;
        }
    }

    internal static class ParabolicFloorLightGeometry
    {
        // Sampled from native GlowGradient so the visible halo and saved reflection bake
        // use the same falloff without another texture.
        internal static readonly float[] NativeGlowFalloff =
        {
            255f / 255f, 255f / 255f, 255f / 255f, 240f / 255f, 212f / 255f, 184f / 255f, 159f / 255f,
            134f / 255f, 112f / 255f, 93f / 255f, 73f / 255f, 55f / 255f, 39f / 255f, 25f / 255f,
            13f / 255f, 5f / 255f, 0f,
        };

        internal const int SegmentCount = 96;
        internal const int BisectionIterations = 32;

        private const int BranchSegmentCount = SegmentCount / 2;
        private const float VertexRearOffset = 0.35f;
        internal const float BaseCurveScale = 0.75f;
        private const float HalfWidth = 0.0875f;
        internal const float TopHeight = 0.006f;
        internal const float GlowHeight = 0.001f;
        private const float BottomDepth = 0.015f;

        internal static bool TryCreate(
            FerrofluidGrabEnclosure enclosure,
            Vector3 playerPosition,
            Vector3 forward,
            out ParabolicFloorLightMeshData? mesh)
            => TryCreateConfigured(enclosure, playerPosition, forward, ParabolicFloorLightSettings.Default, out mesh);

        internal static bool TryCreateConfigured(
            FerrofluidGrabEnclosure enclosure,
            Vector3 playerPosition,
            Vector3 forward,
            ParabolicFloorLightSettings settings,
            out ParabolicFloorLightMeshData? mesh)
        {
            mesh = null;
            Vector3 baseForward = forward;
            baseForward.y = 0f;
            baseForward = baseForward.sqrMagnitude > 0.000001f
                ? baseForward.normalized
                : Vector3.forward;
            Vector3 baseRight = Vector3.Cross(Vector3.up, baseForward);
            float radians = settings.Rotation * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            Vector3 flatForward = (baseForward * cos) + (baseRight * sin);
            Vector3 right = (baseRight * cos) - (baseForward * sin);
            float curveScale = BaseCurveScale / (settings.WidthScale * settings.WidthScale);
            float halfWidth = HalfWidth * settings.LightWidthScale;
            Vector3 vertex = playerPosition - (baseForward * VertexRearOffset) + (baseRight * settings.OffsetX) + (baseForward * settings.OffsetZ);
            float vertexOffsetX = vertex.x - enclosure.Center.x;
            float vertexOffsetZ = vertex.z - enclosure.Center.z;
            float vertexDistance = Mathf.Sqrt((vertexOffsetX * vertexOffsetX) + (vertexOffsetZ * vertexOffsetZ));
            if (vertexDistance >= enclosure.Radius)
                return false;

            Vector2 circleCenter = new(enclosure.Center.x, enclosure.Center.z);
            float radiusSquared = enclosure.Radius * enclosure.Radius;
            EvaluateStationCorners(vertex, right, flatForward, 0f, curveScale, halfWidth, out Vector3 vertexPositive, out Vector3 vertexNegative);
            if (RadialSquared(vertexPositive, circleCenter) > radiusSquared
                || RadialSquared(vertexNegative, circleCenter) > radiusSquared)
            {
                return false;
            }

            float leftExtent = FindBranchExtent(-1f, vertex, right, flatForward, curveScale, halfWidth, circleCenter, enclosure.Radius, vertexDistance) * Mathf.Sqrt(2f);
            float rightExtent = FindBranchExtent(1f, vertex, right, flatForward, curveScale, halfWidth, circleCenter, enclosure.Radius, vertexDistance) * Mathf.Sqrt(2f);

            float topY = enclosure.FloorY + TopHeight;
            float bottomY = enclosure.FloorY - BottomDepth;
            Vector3[] vertices = new Vector3[(SegmentCount + 1) * 4];
            Color[] colors = new Color[(SegmentCount + 1) * 4];
            Vector2[] uv = new Vector2[(SegmentCount + 1) * 4];
            int[] triangles = new int[((SegmentCount * 8) + 4) * 3];
            for (int station = 0; station <= SegmentCount; station++)
            {
                float x = station <= BranchSegmentCount
                    ? (-leftExtent * (BranchSegmentCount - station)) / BranchSegmentCount
                    : (rightExtent * (station - BranchSegmentCount)) / BranchSegmentCount;
                EvaluateStationCorners(vertex, right, flatForward, x, curveScale, halfWidth, out Vector3 positive, out Vector3 negative);
                int corner = station * 4;
                vertices[corner] = new Vector3(positive.x, topY, positive.z);
                vertices[corner + 1] = new Vector3(negative.x, topY, negative.z);
                vertices[corner + 2] = new Vector3(negative.x, bottomY, negative.z);
                vertices[corner + 3] = new Vector3(positive.x, bottomY, positive.z);
                float forwardProgress = Mathf.Abs(station - BranchSegmentCount) / (float)BranchSegmentCount;
                forwardProgress *= forwardProgress;
                float alpha = EvaluateEndFade(forwardProgress);
                for (int index = 0; index < 4; index++)
                {
                    colors[corner + index] = new Color(1f, 1f, 1f, alpha);
                    uv[corner + index] = new Vector2(station / (float)SegmentCount, index * 0.25f);
                }
            }

            int triangleIndex = 0;
            for (int segment = 0; segment < SegmentCount; segment++)
            {
                int from = segment * 4;
                int to = from + 4;

                triangles[triangleIndex++] = from;
                triangles[triangleIndex++] = from + 1;
                triangles[triangleIndex++] = to;
                triangles[triangleIndex++] = from + 1;
                triangles[triangleIndex++] = to + 1;
                triangles[triangleIndex++] = to;
                triangles[triangleIndex++] = from + 3;
                triangles[triangleIndex++] = to + 3;
                triangles[triangleIndex++] = from + 2;
                triangles[triangleIndex++] = to + 3;
                triangles[triangleIndex++] = to + 2;
                triangles[triangleIndex++] = from + 2;
                triangles[triangleIndex++] = from + 3;
                triangles[triangleIndex++] = from;
                triangles[triangleIndex++] = to + 3;
                triangles[triangleIndex++] = from;
                triangles[triangleIndex++] = to;
                triangles[triangleIndex++] = to + 3;
                triangles[triangleIndex++] = from + 1;
                triangles[triangleIndex++] = from + 2;
                triangles[triangleIndex++] = to + 1;
                triangles[triangleIndex++] = from + 2;
                triangles[triangleIndex++] = to + 2;
                triangles[triangleIndex++] = to + 1;
            }

            int last = SegmentCount * 4;
            triangles[triangleIndex++] = 0;
            triangles[triangleIndex++] = 3;
            triangles[triangleIndex++] = 2;
            triangles[triangleIndex++] = 0;
            triangles[triangleIndex++] = 2;
            triangles[triangleIndex++] = 1;
            triangles[triangleIndex++] = last;
            triangles[triangleIndex++] = last + 1;
            triangles[triangleIndex++] = last + 2;
            triangles[triangleIndex++] = last;
            triangles[triangleIndex++] = last + 2;
            triangles[triangleIndex++] = last + 3;

            mesh = new ParabolicFloorLightMeshData(vertices, triangles, colors, uv);
            return true;
        }

        internal static float EvaluateEndFade(float forwardProgress)
        {
            // The menu boundary is halfway along the doubled forward reach, so start fading slightly before it.
            float t = Mathf.Clamp01((forwardProgress - 0.45f) / 0.55f);
            return 1f - (t * t * (3f - (2f * t)));
        }

        private static float FindBranchExtent(
            float direction,
            Vector3 vertex,
            Vector3 right,
            Vector3 forward,
            float curveScale,
            float halfWidth,
            Vector2 circleCenter,
            float radius,
            float vertexDistance)
        {
            float high = radius + vertexDistance + halfWidth + 0.5f;
            float low = 0f;
            for (int iteration = 0; iteration < BisectionIterations; iteration++)
            {
                float mid = (low + high) * 0.5f;
                if (BranchStaysInside(direction, mid, vertex, right, forward, curveScale, halfWidth, circleCenter, radius))
                    low = mid;
                else
                    high = mid;
            }

            return low;
        }

        private static bool BranchStaysInside(
            float direction,
            float extent,
            Vector3 vertex,
            Vector3 right,
            Vector3 forward,
            float curveScale,
            float halfWidth,
            Vector2 circleCenter,
            float radius)
        {
            float radiusSquared = radius * radius;
            for (int step = 0; step <= BranchSegmentCount; step++)
            {
                float x = (direction * extent * step) / BranchSegmentCount;
                EvaluateStationCorners(vertex, right, forward, x, curveScale, halfWidth, out Vector3 positive, out Vector3 negative);
                if (RadialSquared(positive, circleCenter) > radiusSquared
                    || RadialSquared(negative, circleCenter) > radiusSquared)
                {
                    return false;
                }
            }

            return true;
        }

        private static void EvaluateStationCorners(
            Vector3 vertex,
            Vector3 right,
            Vector3 forward,
            float x,
            float curveScale,
            float halfWidth,
            out Vector3 positiveCorner,
            out Vector3 negativeCorner)
        {
            Vector3 center = vertex + (right * x) + (forward * (curveScale * x * x));
            Vector3 tangent = right + (forward * (2f * curveScale * x));
            Vector3 normal = Vector3.Cross(Vector3.up, tangent).normalized;
            positiveCorner = center + (normal * halfWidth);
            negativeCorner = center - (normal * halfWidth);
        }

        private static float RadialSquared(Vector3 position, Vector2 circleCenter)
        {
            float dx = position.x - circleCenter.x;
            float dz = position.z - circleCenter.y;
            return (dx * dx) + (dz * dz);
        }

        internal static ParabolicFloorLightGlowMeshData CreateGlow(
            FerrofluidGrabEnclosure enclosure,
            ParabolicFloorLightMeshData core,
            ParabolicFloorLightSettings settings)
            => MenuLightGlowGeometry.Create(core, settings, enclosure.FloorY + GlowHeight);
    }

    internal sealed class ParabolicFloorLight : IInitializable, System.IDisposable
    {
        private const string ParticleShaderName = "Custom/CustomParticles";

        private readonly Config _config;
        private readonly Dictionary<string, MenuLightRenderer> _renderers = new();
        private readonly Dictionary<string, MenuLightDefinition> _savedDefinitions = new();
        private readonly Dictionary<string, MenuLightMeshes> _savedMeshes = new();
        private MenuLightReflectionCache? _reflectionCache;
        private MenuLightCubeSpill? _cubeSpill;
        private Action<RenderTexture, RenderTexture>? _renderBloom;
        private MainEffectContainerSO? _mainEffectContainer;
        private Shader? _shader;
        private FerrofluidGrabEnclosure _enclosure;
        private Vector3 _playerPosition;
        private Vector3 _baseForward;
        private Scene _anchorScene;
        private bool _canCreate;
        private bool _enabled;
        private bool _menuActive = true;
        private ReflectionQuality _appliedQuality;
        private FerrofluidReflectionPackService? _packService;
        private string? _previewId;
        private MenuLightDefinition? _preview;

        private ParabolicFloorLight(Config config)
        {
            _config = config;
            _appliedQuality = config.ReflectionsQuality;
            Store = new MenuLightStore(config);
        }

        internal MenuLightStore Store { get; }

        [Inject]
        private void InitializeReflectionPackService(FerrofluidReflectionPackService service)
        {
            _packService = service;
            _packService.QualityChanged += SetReflectionQuality;
        }

        public void Initialize()
        {
            try
            {
                InitializeSceneLifecycle();
                InitializeCore();
            }
            catch (Exception exception)
            {
                Dispose();
                Plugin.Log.Error($"[ChromaGLS Menu Lights] initialization failed; lights disabled for this menu session. {exception}");
            }
        }

        internal void SetEnabled(bool enabled)
        {
            _enabled = enabled && _menuActive;
            foreach (KeyValuePair<string, MenuLightRenderer> pair in _renderers)
            {
                MenuLightDefinition definition = pair.Key == _previewId && _preview != null ? _preview : _savedDefinitions[pair.Key];
                pair.Value.SetEnabled(_enabled, definition.Enabled);
            }

            _reflectionCache?.SetEnabled(_enabled);
            _cubeSpill?.SetEnabled(_enabled);
        }

        internal void SetMenuActive(bool active)
        {
            if (_menuActive == active)
                return;

            _menuActive = active;
            SetEnabled(_config.ParabolicFloorLightEnabled);
            if (!active && _previewId != null)
                Restore(_previewId);
        }

        internal void InitializeSceneLifecycle()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "GameCore")
                SetMenuActive(false);
        }

        private void HandleActiveSceneChanged(Scene previous, Scene next)
        {
            if (next.name == "GameCore")
                SetMenuActive(false);
            else if (next.name == "MainMenu")
                SetMenuActive(true);
        }

        internal void SetReflectionQuality(ReflectionQuality quality)
        {
            // The pack service writes the config before raising QualityChanged, so compare the
            // value actually applied to the renderers, not the already-updated preference.
            bool changed = quality != _appliedQuality;
            _appliedQuality = quality;
            _config.ReflectionsQuality = quality;
            _config.ReflectionsQualityInitialized = true;
            if (!changed)
                return;

            // Release the old-resolution array before any new reflection asset loads.
            if (_reflectionCache != null)
                _reflectionCache.SetQuality(quality);

            FerrofluidRoomReflections.SetReflectionQuality(quality);
            if (_reflectionCache != null)
                _reflectionCache.Synchronize(Store.Lights, _savedMeshes, _enabled);
        }

        internal void Preview(MenuLightDefinition definition)
        {
            if (!_canCreate || !_menuActive)
                return;

            _previewId = definition.Id;
            _preview = definition.Clone();
            bool valid = MenuLightGeometry.TryCreate(_enclosure, _playerPosition, _baseForward, _preview, out MenuLightMeshes meshes);
            if (valid)
            {
                if (_renderers.TryGetValue(_previewId, out MenuLightRenderer? renderer))
                    renderer.Apply(_preview, meshes, _enabled);
                else
                    _renderers[_previewId] = new MenuLightRenderer(_shader!, _anchorScene, _preview, meshes, _enabled);
            }
            else if (_renderers.TryGetValue(_previewId, out MenuLightRenderer? renderer))
            {
                renderer.SetEnabled(false, false);
            }

            _cubeSpill?.Preview(_preview, valid ? meshes : (MenuLightMeshes?)null);
        }

        internal void Restore(string id)
        {
            if (_previewId != id)
                return;

            _previewId = null;
            _preview = null;
            _cubeSpill?.RestorePreview();
            if (_savedMeshes.TryGetValue(id, out MenuLightMeshes meshes) && _savedDefinitions.TryGetValue(id, out MenuLightDefinition? saved))
            {
                if (_renderers.TryGetValue(id, out MenuLightRenderer? renderer))
                    renderer.Apply(saved, meshes, _enabled);
            }
            else if (_renderers.TryGetValue(id, out MenuLightRenderer? renderer))
            {
                renderer.Dispose();
                _renderers.Remove(id);
            }
        }

        internal void Synchronize(IReadOnlyList<MenuLightDefinition> definitions)
        {
            if (!_canCreate)
                return;

            HashSet<string> next = new();
            foreach (MenuLightDefinition definition in definitions)
                next.Add(definition.Id);

            List<string> removed = new();
            foreach (string id in _savedDefinitions.Keys)
            {
                if (!next.Contains(id))
                    removed.Add(id);
            }

            foreach (string id in removed)
            {
                if (_renderers.TryGetValue(id, out MenuLightRenderer? renderer))
                {
                    renderer.Dispose();
                    _renderers.Remove(id);
                }

                _savedDefinitions.Remove(id);
                _savedMeshes.Remove(id);
            }

            foreach (MenuLightDefinition definition in definitions)
            {
                MenuLightDefinition clone = definition.Clone();
                bool exists = _savedDefinitions.TryGetValue(definition.Id, out MenuLightDefinition? previous)
                    && _savedMeshes.ContainsKey(definition.Id);
                bool changed = !exists
                    || !previous!.Appearance.Equals(clone.Appearance)
                    || previous.Type != clone.Type
                    || previous.PositionX != clone.PositionX
                    || previous.PositionZ != clone.PositionZ
                    || previous.Height != clone.Height
                    || previous.Depth != clone.Depth
                    || previous.Size != clone.Size
                    || previous.EndLength != clone.EndLength
                    || previous.FadeStart != clone.FadeStart
                    || previous.QuaternionX != clone.QuaternionX
                    || previous.QuaternionY != clone.QuaternionY
                    || previous.QuaternionZ != clone.QuaternionZ
                    || previous.QuaternionW != clone.QuaternionW
                    || previous.RotationX != clone.RotationX
                    || previous.RotationY != clone.RotationY
                    || previous.RotationZ != clone.RotationZ;

                if (exists && !changed && _renderers.TryGetValue(definition.Id, out MenuLightRenderer? unchanged))
                {
                    // An unchanged save over an active preview restores the committed geometry.
                    if (definition.Id == _previewId)
                        unchanged.Apply(clone, _savedMeshes[definition.Id], _enabled);
                    else
                        unchanged.SetEnabled(_enabled, clone.Enabled);
                }
                else if (MenuLightGeometry.TryCreate(_enclosure, _playerPosition, _baseForward, clone, out MenuLightMeshes meshes))
                {
                    _savedMeshes[definition.Id] = meshes;
                    if (_renderers.TryGetValue(definition.Id, out MenuLightRenderer? renderer))
                        renderer.Apply(clone, meshes, _enabled);
                    else
                        _renderers[definition.Id] = new MenuLightRenderer(_shader!, _anchorScene, clone, meshes, _enabled);
                }
                else if (_renderers.TryGetValue(definition.Id, out MenuLightRenderer? renderer))
                {
                    renderer.SetEnabled(false, false);
                    _savedMeshes.Remove(definition.Id);
                }

                _savedDefinitions[definition.Id] = clone;
            }

            _previewId = null;
            _preview = null;
            _reflectionCache?.Synchronize(definitions, _savedMeshes, _enabled);
            _cubeSpill?.Synchronize(definitions, _savedMeshes, _enabled);
        }

        public void Dispose()
        {
            if (_packService != null)
            {
                _packService.QualityChanged -= SetReflectionQuality;
                _packService = null;
            }

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
            _canCreate = false;
            _enabled = false;
            foreach (MenuLightRenderer renderer in _renderers.Values)
                renderer.Dispose();

            _renderers.Clear();
            _savedDefinitions.Clear();
            _savedMeshes.Clear();
            _preview = null;
            _previewId = null;
            _cubeSpill?.Dispose();
            _cubeSpill = null;
            if (_reflectionCache != null)
            {
                _reflectionCache.Dispose();
                _reflectionCache = null;
            }
        }

        private void InitializeCore()
        {
            Transform? playerAnchor = FerrofluidMagnetController.ResolvePlayerAnchor();
            if (playerAnchor == null)
            {
                Plugin.Log.Error("[ChromaGLS Menu Lights] no menu player rig was found; lights disabled for this menu session.");
                return;
            }

            if (!FerrofluidGrabPhysics.TryResolveEnclosure(playerAnchor, out _enclosure))
            {
                Plugin.Log.Error("[ChromaGLS Menu Lights] no active curved menu canvas contains the player; lights disabled for this menu session.");
                return;
            }

            _shader = ResolveShader(ParticleShaderName);
            if (_shader == null || !_shader.isSupported)
            {
                Plugin.Log.Error($"[ChromaGLS Menu Lights] {ParticleShaderName} was not found or is unsupported; lights disabled for this menu session.");
                return;
            }

            _playerPosition = playerAnchor.position;
            _baseForward = playerAnchor.forward;
            _anchorScene = playerAnchor.gameObject.scene;
            _enabled = _config.ParabolicFloorLightEnabled && _menuActive;
            _canCreate = true;
            Synchronize(Store.Lights);

            foreach (MainEffectController controller in Resources.FindObjectsOfTypeAll<MainEffectController>())
            {
                if (!controller.enabled || !controller.gameObject.activeInHierarchy || controller.gameObject.name != "MenuMainCamera")
                    continue;
#if PRE_V1_42_1
                _mainEffectContainer = controller._mainEffectContainer;
#else
                _mainEffectContainer = controller.container;
#endif
                _renderBloom = RenderReflectionBloom;
                break;
            }

            if (_renderBloom == null)
            {
                Plugin.Log.Warn("[ChromaGLS Menu Lights] the menu MainEffectController was not found; the shared reflection cache is unavailable.");
            }

            if (FerrofluidGpuResources.TryGet(out FerrofluidGpuResources? resources))
            {
                if (_renderBloom != null && resources!.ParabolicFloorReflectionShader != null)
                    InitializeReflectionCache(resources.ParabolicFloorReflectionShader);

                if (resources!.MenuLightCubeSpillShader != null)
                    InitializeCubeSpill(resources.MenuLightCubeSpillShader);
            }
        }

        private void InitializeCubeSpill(Shader shader)
        {
            Scene scene = SceneManager.GetSceneByName("MainMenu");
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            Transform? environment = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(transform => transform.name == "DefaultMenuEnvironment");
            if (environment == null)
                return;

            _cubeSpill = new MenuLightCubeSpill(shader, environment);
            _cubeSpill.Synchronize(Store.Lights, _savedMeshes, _enabled);
        }

        internal void InitializeReflectionCache(Shader shader)
        {
            try
            {
                _reflectionCache = new MenuLightReflectionCache(shader, _renderBloom!);
                _reflectionCache.SetQuality(_config.ReflectionsQuality);
                _reflectionCache.Synchronize(Store.Lights, _savedMeshes, _enabled);
            }
            catch (Exception exception)
            {
                if (_reflectionCache != null)
                {
                    _reflectionCache.Dispose();
                    _reflectionCache = null;
                }

                Plugin.Log.Warn($"[ChromaGLS Menu Lights] saved reflection cache unavailable: {exception.Message}");
            }
        }

        private void RenderReflectionBloom(RenderTexture source, RenderTexture destination)
        {
            MainEffectSO currentEffect = _mainEffectContainer!.mainEffect;
            float boost = Shader.GetGlobalFloat("_BaseColorBoost");
            float boostThreshold = Shader.GetGlobalFloat("_BaseColorBoostThreshold");
            bool legacyAutoExposure = Shader.IsKeywordEnabled("LEGACY_AUTOEXPOSURE");
            try
            {
                currentEffect.PreRender();
                if (currentEffect.hasPostProcessEffect)
                    currentEffect.Render(source, destination, 1f);
                else
                    Graphics.Blit(source, destination);
            }
            finally
            {
                Shader.SetGlobalFloat("_BaseColorBoost", boost);
                Shader.SetGlobalFloat("_BaseColorBoostThreshold", boostThreshold);
                if (legacyAutoExposure)
                    Shader.EnableKeyword("LEGACY_AUTOEXPOSURE");
                else
                    Shader.DisableKeyword("LEGACY_AUTOEXPOSURE");
            }
        }

        private static Shader? ResolveShader(string name)
        {
#if PRE_V1_37_1
            return Shader.Find(name);
#else
            return Resources.FindObjectsOfTypeAll<Shader>().FirstOrDefault(shader => shader.name == name);
#endif
        }
    }
}
