using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ChromaGLS.Settings
{
#pragma warning disable SA1202, SA1204
    // Run fluid pressure, forces, integration, and spike deformation on the GPU.
    internal sealed class FerrofluidGpuSimulation : IDisposable
    {
        private const int ParticleCount = 576;
        private const int ParticleTextureSize = 32;
        private const int FieldColumns = 48;
        private const int FieldRows = 12;
        private const int FieldWindings = 2;

        private const int PrimitiveLatticeAxis = 24;
        private const int PrimitiveMinLatticeAxis = 8;
        private const int PrimitiveMaxLatticeAxis = 48;
        private const float PrimitiveDomainSpacing = 0.64f;

        // Slightly overlap neighboring pressure radii so spikes can move past each other under the magnet.
        private const float PrimitiveOverpackFactor = 0.98f;
        private const float PrimitiveBaselineDensity = 0.45f;

        private const int PrimitiveRelaxationIterations = 400;
        private const int PrimitiveRelaxationNeighbors = 8;
        private const float PrimitiveRelaxationStep = 1.0f;
        internal const int HeightMapSize = 256;
        internal const int PrimitiveHeightMapSize = 256;

        internal const int CoarseCandidateMapSize = 16;
        private const int PrimitiveCandidateMapSize = 64;

        // The cube and sphere need more vertices than the info-button ring to keep their curved silhouettes smooth.
        private const int TorusSurfaceResolution = 191;
        private const int PrimitiveSurfaceResolution = 448;
        private const float SourceTorusWidth = 0.9f;
        private const float SourceCornerRadius = 0.11f;
        private const float SourceHalfHeight = 0.2625f;
        private const float SourceRadialRadius = 0.085f;
        private const float SourceFluidHeightScale = 0.42f;

        private const float RequirementBorderPaddingHeightFraction = 0.1f;
        internal const float RadialCenter = 0.2f;
        internal const float RadialRadius = 1.38f;

        internal const float HeightMapScaleFactor = 0.9f;
        internal const float HeightMapSmoothFactor = 0.02f;
        internal const float HeightFactorValue = 0.38f;

        internal const float SourceMagnetSurfaceGap = 0.14f;

        // Run at 120 Hz with an 8 ms integration step and at most two catch-up ticks per rendered frame.
        private const float SimulationInterval = 1f / 120f;
        private const float SimulationDeltaTimeMilliseconds = 8f;
        private const int MaximumSimulationTicksPerFrame = 2;

        // Reuse relaxed spike layouts when a shape is recreated with the same settings.
        private static readonly Dictionary<long, Vector2[]> RestPositionCache = new();
        private static readonly int StateTextureId = Shader.PropertyToID("_StateTex");
        private static readonly int RestPositionTextureId = Shader.PropertyToID("_RestPositionTex");
        private static readonly int DensityPressureTextureId = Shader.PropertyToID("_DensityPressureTex");
        private static readonly int ForceTextureId = Shader.PropertyToID("_ForceTex");
        private static readonly int HeightMapTextureId = Shader.PropertyToID("_HeightMapTex");

        private static readonly int CoarseCandidateTextureId = Shader.PropertyToID("_CoarseCandidateTex");
        private static readonly int CoarseCandidateSizeId = Shader.PropertyToID("_CoarseCandidateSize");

        private static readonly int SpringStateTextureId = Shader.PropertyToID("_SpringStateTex");
        private static readonly int SpikeSpringStrengthId = Shader.PropertyToID("_SpikeSpringStrength");
        private static readonly int SpikeSpringDampingId = Shader.PropertyToID("_SpikeSpringDamping");
        private static readonly int ReflectivityId = Shader.PropertyToID("_Reflectivity");

        private static readonly int FadeAlphaId = Shader.PropertyToID("_FadeAlpha");
        private static readonly int MatteSheenId = Shader.PropertyToID("_MatteSheen");
        private static readonly int MatteAngleId = Shader.PropertyToID("_MatteAngle");
        private static readonly int ReflectionAngleId = Shader.PropertyToID("_ReflectionAngle");
        private static readonly int MatteLightRotationId = Shader.PropertyToID("_MatteLightRotation");
        private static readonly int ReflectionRotationId = Shader.PropertyToID("_ReflectionRotation");
        private static readonly int EffectObjectToWorldId = Shader.PropertyToID("_FerrofluidObjectToWorld");
        private static readonly int EnvironmentTextureId = Shader.PropertyToID("_EnvironmentTex");
        private static readonly int ThinFilmTextureId = Shader.PropertyToID("_ThinFilmTex");

        private static readonly int ThinFilmScaleId = Shader.PropertyToID("_ThinFilmScale");
        private static readonly int ThinFilmOffsetId = Shader.PropertyToID("_ThinFilmOffset");
        private static readonly int ParticleCountId = Shader.PropertyToID("_ParticleCount");
        private static readonly int DomainScaleId = Shader.PropertyToID("_DomainScale");
        private static readonly int PointerPositionId = Shader.PropertyToID("_PointerPosition");
        private static readonly int PointerStrengthId = Shader.PropertyToID("_PointerStrength");
        private static readonly int DeltaTimeMillisecondsId = Shader.PropertyToID("_DeltaTimeMilliseconds");
        private static readonly int PerimeterSegmentsId = Shader.PropertyToID("_PerimeterSegments");
        private static readonly int CornerRadiusId = Shader.PropertyToID("_CornerRadius");
        private static readonly int RadialRadiusId = Shader.PropertyToID("_RadialRadius");
        private static readonly int HeightFactorId = Shader.PropertyToID("_HeightFactor");
        private static readonly int RectCenterId = Shader.PropertyToID("_RectCenter");
        private static readonly int RectSizeId = Shader.PropertyToID("_RectSize");
        private static readonly int RectMinYId = Shader.PropertyToID("_RectMinY");
        private static readonly int PivotYId = Shader.PropertyToID("_PivotY");
        private static readonly int SkewId = Shader.PropertyToID("_Skew");
        private static readonly int HalfExtentsId = Shader.PropertyToID("_HalfExtents");
        private static readonly int DepthRadiusId = Shader.PropertyToID("_DepthRadius");
        private static readonly int ViewerLocalZSignId = Shader.PropertyToID("_ViewerLocalZSign");
        private static readonly int RadialCenterId = Shader.PropertyToID("_RadialCenter");
        private static readonly int SourceLocalScaleId = Shader.PropertyToID("_SourceLocalScale");
        private static readonly int LocalToCurvedCanvasId = Shader.PropertyToID("_LocalToCurvedCanvas");
        private static readonly int CurvedCanvasToLocalId = Shader.PropertyToID("_CurvedCanvasToLocal");
        private static readonly int CurvedCanvasRadiusId = Shader.PropertyToID("_CurvedCanvasRadius");
        private static readonly int HasCurvedCanvasId = Shader.PropertyToID("_HasCurvedCanvas");
        private static readonly int MagneticFieldPositionId = Shader.PropertyToID("_MagneticFieldPosition");
        private static readonly int MagneticFieldStrengthId = Shader.PropertyToID("_MagneticFieldStrength");
        private static readonly int SecondaryMagneticFieldPositionId = Shader.PropertyToID("_SecondaryMagneticFieldPosition");
        private static readonly int SecondaryMagneticFieldStrengthId = Shader.PropertyToID("_SecondaryMagneticFieldStrength");
        private static readonly int PrimitiveMagnetPositionId = Shader.PropertyToID("_PrimitiveMagnetPosition");
        private static readonly int PrimitiveHandPositionId = Shader.PropertyToID("_PrimitiveHandPosition");
        private static readonly int SecondaryPointerPositionId = Shader.PropertyToID("_SecondaryPointerPosition");

        private static readonly int MagnetOpeningPositionId = Shader.PropertyToID("_MagnetOpeningPosition");
        private static readonly int MagnetOpeningNormalId = Shader.PropertyToID("_MagnetOpeningNormal");
        private static readonly int MagnetOpeningDirectionId = Shader.PropertyToID("_MagnetOpeningDirection");
        private static readonly int MagnetOpeningStrengthId = Shader.PropertyToID("_MagnetOpeningStrength");

        private static readonly int SurfaceShapeModeId = Shader.PropertyToID("_SurfaceShapeMode");
        private static readonly int SurfaceRoundnessId = Shader.PropertyToID("_SurfaceRoundness");

        private static readonly int ParticleTextureSizeId = Shader.PropertyToID("_ParticleTextureSize");
        private static readonly int PrimitiveSpikeSizeId = Shader.PropertyToID("_PrimitiveSpikeSize");
        private static readonly int PrimitiveTipRoundnessId = Shader.PropertyToID("_PrimitiveTipRoundness");
        private static readonly int PrimitiveTipCapRoundingId = Shader.PropertyToID("_PrimitiveTipCapRounding");
        private static readonly int PrimitiveSpikeSpacingId = Shader.PropertyToID("_PrimitiveSpikeSpacing");
        private static readonly int SpikeMoundCurvatureId = Shader.PropertyToID("_SpikeMoundCurvature");
        private static readonly int PrimitiveSpikeHeightRadiusId = Shader.PropertyToID("_PrimitiveSpikeHeightRadius");
        private static readonly int PrimitiveSpikeTiltRadiusId = Shader.PropertyToID("_PrimitiveSpikeTiltRadius");
        private static readonly int PrimitiveAmbientScaleId = Shader.PropertyToID("_PrimitiveAmbientScale");
        private static readonly int PrimitiveFlattenRadiusId = Shader.PropertyToID("_PrimitiveFlattenRadius");
        private static readonly int PrimitiveFlattenExponentId = Shader.PropertyToID("_PrimitiveFlattenExponent");
        private static readonly int PrimitiveGlobalFlattenId = Shader.PropertyToID("_PrimitiveGlobalSpikeFlatten");
        private static readonly int SphereBulgeScaleId = Shader.PropertyToID("_SphereBulgeScale");

        private static readonly int SiteSurfaceTextureId = Shader.PropertyToID("_SiteSurfaceTex");
        private static readonly int SiteDeformationTextureId = Shader.PropertyToID("_SiteDeformationTex");
        private static readonly int SiteBulgeTextureId = Shader.PropertyToID("_SiteBulgeTex");
        private static readonly int RestSurfaceTextureId = Shader.PropertyToID("_RestSurfaceTex");
        private static readonly int PrimitiveYawCosSinId = Shader.PropertyToID("_PrimitiveYawCosSin");
        private static readonly int PrimitiveCellMetricId = Shader.PropertyToID("_PrimitiveCellMetric");

        private static readonly int BackHeightFalloffExponentId = Shader.PropertyToID("_BackHeightFalloffExponent");
        private static readonly int MagnetHeightStrengthCapId = Shader.PropertyToID("_MagnetHeightStrengthCap");
        private static readonly int MagnetHeightDeadZoneRadiusId = Shader.PropertyToID("_MagnetHeightDeadZoneRadius");
        private readonly MeshRenderer _renderer;
        private readonly Material _simulationMaterial;
        private readonly Material _surfaceMaterial;
        private readonly RenderTexture _heightMap;

        private readonly RenderTexture _coarseCandidates;

        private readonly Mesh _surfaceMesh;
        private readonly FerrofluidSurfaceShape _shape;
        private Texture2D _restPositionTexture;
        private Vector2[]? _primitiveRestPositions;

        private float _lastFadeAlpha = 1f;

        // Store height and velocity per particle so each spike retains its spring state while moving across UV seams.
        private RenderTexture _springStateA;
        private RenderTexture _springStateB;
        private RenderTexture _springCurrent;
        private RenderTexture _springNext;

        private RenderTexture _stateA;
        private RenderTexture _stateB;
        private RenderTexture _densityPressure;
        private RenderTexture _force;
        private RenderTexture _currentState;
        private RenderTexture _nextState;

        private RenderTexture? _siteSurface;
        private RenderTexture? _restSurface;
        private RenderTexture? _siteDeformation;
        private RenderTexture? _siteBulge;
        private Config _config;
        private int _particleCount;
        private int _particleTextureSize;
        private int _fieldColumns;
        private int _fieldRows;
        private float _spikeHeightScale = 1f;

        // Carry fractional frame time forward rather than changing the fixed integration step.
        private float _simulationAccumulator;
        private float _lastSimulationAt;
        private bool _renderEnabled;

        private FerrofluidGpuSimulation(
            MeshFilter meshFilter,
            MeshRenderer renderer,
            FerrofluidGpuResources resources,
            Config config,
            FerrofluidSurfaceShape shape)
        {
            _renderer = renderer;
            _shape = shape;

            Config initialConfig = shape == FerrofluidSurfaceShape.Torus
                ? EvaluateTorusBaseline(config)
                : config;
            _config = initialConfig;

            Vector2Int initialLattice = EvaluateDensityLattice(
                initialConfig.FerrofluidSpikeDensity,
                shape,
                EvaluateShapeDensityScale(shape, initialConfig));
            _fieldColumns = initialLattice.x;
            _fieldRows = initialLattice.y;
            _particleCount = _fieldColumns * _fieldRows;
            _particleTextureSize = EvaluateParticleTextureSize(_particleCount);
            _simulationMaterial = new Material(resources.SimulationShader)
            {
                name = "ChromaGLS Ferrofluid Simulation Material",
                hideFlags = HideFlags.DontSave,
            };
            _surfaceMaterial = new Material(resources.SurfaceShader)
            {
                name = "ChromaGLS Ferrofluid Surface Material",
                hideFlags = HideFlags.DontSave,

                // Beat Saber uses single-pass instanced stereo. Without material instancing, the ferrofluid renders only in the left eye.
                enableInstancing = true,
            };
            _surfaceMaterial.SetTexture(EnvironmentTextureId, resources.EnvironmentTexture);
            _surfaceMaterial.SetTexture(ThinFilmTextureId, resources.ThinFilmTexture);

            _simulationMaterial.SetFloat(SurfaceShapeModeId, (float)shape);
            _surfaceMaterial.SetFloat(SurfaceShapeModeId, (float)shape);
            _simulationMaterial.SetInt(ParticleTextureSizeId, _particleTextureSize);
            _stateA = CreateRenderTexture("ChromaGLS Ferrofluid State A", _particleTextureSize, FilterMode.Point, TextureWrapMode.Repeat);
            _stateB = CreateRenderTexture("ChromaGLS Ferrofluid State B", _particleTextureSize, FilterMode.Point, TextureWrapMode.Repeat);
            _densityPressure = CreateRenderTexture("ChromaGLS Ferrofluid Density Pressure", _particleTextureSize, FilterMode.Point, TextureWrapMode.Clamp);
            _force = CreateRenderTexture("ChromaGLS Ferrofluid Force", _particleTextureSize, FilterMode.Point, TextureWrapMode.Clamp);

            // The primitive chart does not wrap. Repeat would sample the opposite edge's spikes at the seam.
            TextureWrapMode heightWrap = _shape == FerrofluidSurfaceShape.Torus ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            int heightMapSize = shape == FerrofluidSurfaceShape.Torus ? HeightMapSize : PrimitiveHeightMapSize;
            _heightMap = CreateRenderTexture("ChromaGLS Ferrofluid Height Map", heightMapSize, FilterMode.Bilinear, heightWrap);

            // Point filtering preserves candidate IDs. The shader handles torus wrapping explicitly.
            int candidateMapSize = shape == FerrofluidSurfaceShape.Torus ? CoarseCandidateMapSize : PrimitiveCandidateMapSize;
            int candidateWidth = shape == FerrofluidSurfaceShape.Torus ? candidateMapSize : candidateMapSize * 3;
            int candidateHeight = shape == FerrofluidSurfaceShape.Torus ? candidateMapSize : candidateMapSize * 2;
            _coarseCandidates = CreateRenderTexture("ChromaGLS Ferrofluid Coarse Height Candidates", candidateWidth, FilterMode.Point, TextureWrapMode.Clamp, candidateHeight);
            _simulationMaterial.SetFloat(CoarseCandidateSizeId, candidateMapSize);
            _simulationMaterial.SetTexture(CoarseCandidateTextureId, _coarseCandidates);

            // Each particle owns one spring-state texel. Start at zero so spikes settle into shape over the first few ticks.
            _springStateA = CreateRenderTexture("ChromaGLS Ferrofluid Spring State A", _particleTextureSize, FilterMode.Point, TextureWrapMode.Clamp);
            _springStateB = CreateRenderTexture("ChromaGLS Ferrofluid Spring State B", _particleTextureSize, FilterMode.Point, TextureWrapMode.Clamp);
            _surfaceMaterial.SetInt(ParticleTextureSizeId, _particleTextureSize);
            _springCurrent = _springStateA;
            _springNext = _springStateB;
            if (shape != FerrofluidSurfaceShape.Torus)
            {
                _siteSurface = CreateRenderTexture("ChromaGLS Ferrofluid Site Surface", _particleTextureSize, FilterMode.Point, TextureWrapMode.Clamp);
                _restSurface = CreateRenderTexture("ChromaGLS Ferrofluid Rest Surface", _particleTextureSize, FilterMode.Point, TextureWrapMode.Clamp);
                CreatePrimitiveDeformationTextures();
                _surfaceMaterial.EnableKeyword("FERROFLUID_CACHED_DEFORMATION");
            }

            // Relax cube sites against the current roundness to keep spacing even across rounded edges.
            float initialRoundness = shape == FerrofluidSurfaceShape.RoundedCube
                ? Mathf.Clamp01(initialConfig.FerrofluidCubeRoundness)
                : 0.15f;
            _restPositionTexture = CreateInitialPositionTexture(_fieldColumns, _fieldRows, _shape, _particleTextureSize, initialRoundness, out _primitiveRestPositions);
            Graphics.Blit(_restPositionTexture, _stateA);
            Graphics.Blit(_restPositionTexture, _stateB);
            _currentState = _stateA;
            _nextState = _stateB;

            _surfaceMesh = CreateSurfaceMesh(shape == FerrofluidSurfaceShape.Torus
                ? TorusSurfaceResolution
                : PrimitiveSurfaceResolution);
            meshFilter.sharedMesh = _surfaceMesh;
            renderer.sharedMaterial = _surfaceMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ApplyStaticSimulationParameters();
            ApplyConfiguration(initialConfig);
            if (_siteSurface != null)
            {
                _simulationMaterial.SetTexture(StateTextureId, _currentState);
                _simulationMaterial.SetTexture(RestPositionTextureId, _restPositionTexture);
                Graphics.Blit(Texture2D.blackTexture, _siteSurface, _simulationMaterial, 4);
                _simulationMaterial.SetTexture(SiteSurfaceTextureId, _siteSurface);
            }

            object? stereoMode;
            object? xrActive;
            try
            {
                Type? xrSettings = Type.GetType("UnityEngine.XR.XRSettings, UnityEngine.VRModule");
                stereoMode = xrSettings?.GetProperty("stereoRenderingMode")?.GetValue(null);
                xrActive = xrSettings?.GetProperty("isDeviceActive")?.GetValue(null);
            }
            catch
            {
                stereoMode = null;
                xrActive = null;
            }

            Plugin.Log.Info($"[ChromaGLS Ferrofluid] surface material shape={shape} instancing={_surfaceMaterial.enableInstancing} keywords=[{string.Join(", ", _surfaceMaterial.shaderKeywords)}] stereoMode={stereoMode?.ToString() ?? "unknown"} xrActive={xrActive?.ToString() ?? "unknown"}.");

            _lastSimulationAt = Time.unscaledTime;
            _simulationAccumulator = 0f;
        }

        // If the GPU effect is unsupported, keep GLS lighting and SongCore's native outline available.
        internal static FerrofluidGpuSimulation? TryCreate(
            MeshFilter meshFilter,
            MeshRenderer renderer,
            Config config,
            FerrofluidSurfaceShape shape = FerrofluidSurfaceShape.Torus)
        {
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat))
            {
                Plugin.Log.Error("[ChromaGLS Ferrofluid] ARGBFloat render targets are unsupported; visual disabled.");
                return null;
            }

            if (!FerrofluidGpuResources.TryGet(out FerrofluidGpuResources? resources) || resources == null)
            {
                return null;
            }

            try
            {
                return new FerrofluidGpuSimulation(meshFilter, renderer, resources, config, shape);
            }
            catch (Exception exception)
            {
                Plugin.Log.Error($"[ChromaGLS Ferrofluid] GPU simulation initialization failed; visual disabled. {exception}");
                return null;
            }
        }

        internal static bool ShouldRun(bool torusEnabled)
        {
            return torusEnabled;
        }

        internal static Vector3 EvaluateMagnetSurfacePosition(
            Vector3 worldIntersection,
            Vector3 surfaceWorld,
            Vector3 planeNormal,
            float sourceWorldScale)
        {
            return EvaluateMagnetSurfacePosition(
                worldIntersection,
                surfaceWorld,
                planeNormal,
                sourceWorldScale,
                SourceMagnetSurfaceGap);
        }

        internal static Vector3 EvaluateMagnetSurfacePosition(
            Vector3 worldIntersection,
            Vector3 surfaceWorld,
            Vector3 planeNormal,
            float sourceWorldScale,
            float sourceSurfaceGap)
        {
            Vector3 separation = worldIntersection - surfaceWorld;
            Vector3 normalizedNormal = planeNormal.normalized;
            Vector3 tangentOffset = separation - (normalizedNormal * Vector3.Dot(separation, normalizedNormal));
            return surfaceWorld
                + tangentOffset
                + (normalizedNormal * (sourceSurfaceGap * Mathf.Max(sourceWorldScale, 0f)));
        }

        internal static Vector2Int EvaluateDensityLattice(float density)
        {
            return EvaluateDensityLattice(density, FerrofluidSurfaceShape.Torus, 1f);
        }

        internal static Vector2Int EvaluateDensityLattice(
            float density,
            FerrofluidSurfaceShape shape,
            float shapeDensityScale)
        {
            if (shape != FerrofluidSurfaceShape.Torus)
            {
                float scaledAxis = PrimitiveLatticeAxis
                    * Mathf.Max(shapeDensityScale, 0.01f)
                    * (density / PrimitiveBaselineDensity);
                int axis = Mathf.Clamp(
                    Mathf.RoundToInt(scaledAxis),
                    PrimitiveMinLatticeAxis,
                    PrimitiveMaxLatticeAxis);
                return new Vector2Int(axis, axis);
            }

            float axisScale = Mathf.Sqrt(Mathf.Clamp(density, 0.25f, 1.5f));
            return new Vector2Int(
                Mathf.Max(1, Mathf.RoundToInt(FieldColumns * axisScale)),
                Mathf.Max(1, Mathf.RoundToInt(FieldRows * axisScale)));
        }

        internal static float EvaluateShapeDensityScale(FerrofluidSurfaceShape shape, Config config)
        {
            if (shape == FerrofluidSurfaceShape.RoundedCube)
            {
                return Mathf.Clamp(config.FerrofluidCubeSpikeDensity, 0.5f, 3f);
            }

            return shape == FerrofluidSurfaceShape.Sphere
                ? Mathf.Clamp(config.FerrofluidSphereSpikeDensity, 0.5f, 3f)
                : 1f;
        }

        internal static int EvaluateParticleTextureSize(int particleCount)
        {
            return Mathf.Max(ParticleTextureSize, Mathf.CeilToInt(Mathf.Sqrt(particleCount)));
        }

        internal static float EvaluateFieldWindings(int columns, int rows, FerrofluidSurfaceShape shape)
        {
            return shape == FerrofluidSurfaceShape.Torus ? FieldWindings : 0f;
        }

        internal static Vector2 EvaluateDomainScale(FerrofluidSurfaceShape shape, int columns)
        {
            return shape == FerrofluidSurfaceShape.Torus
                ? new Vector2(14.8f, 4f)
                : new Vector2(columns * PrimitiveDomainSpacing * PrimitiveOverpackFactor * 0.5f, columns * PrimitiveDomainSpacing * PrimitiveOverpackFactor * 0.5f);
        }

        // Mirror the shader's octahedral edge folds. Two passes handle a diagonal corner crossing.
        internal static Vector2 FoldPrimitiveChartPosition(Vector2 position)
        {
            for (int foldPass = 0; foldPass < 2; foldPass++)
            {
                if (position.x > 1f)
                {
                    position = new Vector2(2f - position.x, -position.y);
                }
                else if (position.x < -1f)
                {
                    position = new Vector2(-2f - position.x, -position.y);
                }

                if (position.y > 1f)
                {
                    position = new Vector2(-position.x, 2f - position.y);
                }
                else if (position.y < -1f)
                {
                    position = new Vector2(-position.x, -2f - position.y);
                }
            }

            return position;
        }

        internal static Vector2 EvaluatePrimitiveFoldDelta(Vector2 from, Vector2 to)
        {
            Vector2 best = to - from;
            float bestLength = best.sqrMagnitude;
            float edgeX = from.x + to.x >= 0f ? 1f : -1f;
            Vector2 candidate = new Vector2((2f * edgeX) - to.x, -to.y) - from;
            float candidateLength = candidate.sqrMagnitude;
            if (candidateLength < bestLength)
            {
                best = candidate;
                bestLength = candidateLength;
            }

            float edgeY = from.y + to.y >= 0f ? 1f : -1f;
            candidate = new Vector2(-to.x, (2f * edgeY) - to.y) - from;
            candidateLength = candidate.sqrMagnitude;
            if (candidateLength < bestLength)
            {
                best = candidate;
                bestLength = candidateLength;
            }

            Vector2 corner = new Vector2(from.x - to.x >= 0f ? 1f : -1f, from.y - to.y >= 0f ? 1f : -1f);
            candidate = (2f * corner) + to - from;
            if (candidate.sqrMagnitude < bestLength)
            {
                best = candidate;
            }

            return best;
        }

        // Keep the info-button torus's authored shape and simulation settings independent of primitive tweaks.
        // Visibility, spike springs, and lighting controls still update live.
        internal static Config EvaluateTorusBaseline(Config live)
        {
            return new Config
            {
                FerrofluidBodyEnabled = live.FerrofluidBodyEnabled,
                FerrofluidSpikesEnabled = live.FerrofluidSpikesEnabled,
                FerrofluidSpikeSpringStrength = live.FerrofluidSpikeSpringStrength,
                FerrofluidSpikeSpringDamping = live.FerrofluidSpikeSpringDamping,

                FerrofluidReflectivity = live.FerrofluidReflectivity,
                FerrofluidMatteSheen = live.FerrofluidMatteSheen,
                FerrofluidMatteAngle = live.FerrofluidMatteAngle,
                FerrofluidReflectionAngle = live.FerrofluidReflectionAngle,
                FerrofluidMatteRotationX = live.FerrofluidMatteRotationX,
                FerrofluidMatteRotationY = live.FerrofluidMatteRotationY,
                FerrofluidMatteRotationZ = live.FerrofluidMatteRotationZ,
                FerrofluidReflectionRotationX = live.FerrofluidReflectionRotationX,
                FerrofluidReflectionRotationY = live.FerrofluidReflectionRotationY,
                FerrofluidReflectionRotationZ = live.FerrofluidReflectionRotationZ,
                FerrofluidEmissionStrength = 0f,
                FerrofluidExposure = 1f,
                FerrofluidLutScale = 3f,
                FerrofluidLutOffset = 0.13f,
                FerrofluidSpikeBaseSize = 2f,
                FerrofluidSpikeHeightScale = 2.7f,
                FerrofluidSpikeDensity = 0.45f,
                FerrofluidSpikeBaseCutoff = 0.046f,
                ParticleMass = 1.01f,
                RestDensity = 0.1f,
                GasConstant = 438f,
                Viscosity = 9.8f,

                SimulationSteps = 2,
                MagnetRadius = 5.1f,
                MagnetMaximumSlide = 4.1f,
                MagnetSlideFraction = 0.5f,
                MagnetSlideFalloffExponent = 0.7f,
                MagnetElasticity = 80f,
                MagnetDamping = 7f,
                MagnetForwardDistance = 0.47f,
                BulgeRadius = 1.19f,
                BulgeHeight = 0.145f,
                HeightFalloffExponent = 1.25f,
                PeakWidth = 0.5f,
                HeightGain = 1.57f,
                BackHeightLoss = 3.46f,
                BackHeightFalloffExponent = 2f,
                MagnetHeightStrengthCap = 0.72f,
                MagnetHeightDeadZoneRadius = 0.185f,
                MaximumHeight = 2.32f,
                MaximumTiltDegrees = 52f,
                TiltPeakRadiusMultiplier = 0.24f,
                TiltReturnRadiusMultiplier = 0.37f,
                TiltRiseExponent = 1.75f,
                TiltReturnExponent = 0.92f,
                InnerCornerFan = 0f,
            };
        }

        internal static Vector2 EvaluateSpikeShape(float baseSize, float heightScale)
        {
            return new Vector2(
                16.25f / Mathf.Clamp(baseSize, 0.5f, 3f),
                SourceFluidHeightScale * Mathf.Clamp(heightScale, 0.25f, 4f));
        }

        internal static float EvaluateSpikeBaseCutoff(float cutoff)
        {
            return Mathf.Clamp(cutoff, 0f, 0.1f);
        }

        internal static Vector2 EvaluateRestFieldPosition(
            int column,
            int row,
            int columns,
            int rows,
            FerrofluidSurfaceShape shape)
        {
            int index = (row * columns) + column;
            if (shape != FerrofluidSurfaceShape.Torus)
            {
                return EvaluateFibonacciFieldPosition(index, columns * rows);
            }

            float fieldWindings = EvaluateFieldWindings(columns, rows, shape);
            float xNoise = Mathf.Sin((index + 1) * 12.9898f) * 43758.5453f;
            float yNoise = Mathf.Sin((index + 1) * 78.233f) * 43758.5453f;
            float xJitter = ((xNoise - Mathf.Floor(xNoise)) - 0.5f) * 0.2f;
            float yJitter = ((yNoise - Mathf.Floor(yNoise)) - 0.5f) * 0.2f;
            float perimeterFraction = (column + 0.5f + xJitter) / columns;
            float crossSectionFraction = Mathf.Repeat(
                ((row + 0.5f + yJitter) / rows) + (fieldWindings * perimeterFraction),
                1f);
            return new Vector2(
                (perimeterFraction * 2f) - 1f,
                (crossSectionFraction * 2f) - 1f);
        }

        internal static Vector3 EvaluateFibonacciDirection(int index, int count)
        {
            float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));
            float y = 1f - (2f * (index + 0.5f) / count);
            float ringRadius = Mathf.Sqrt(Mathf.Max(0f, 1f - (y * y)));
            float theta = index * goldenAngle;
            return new Vector3(
                ringRadius * Mathf.Cos(theta),
                y,
                ringRadius * Mathf.Sin(theta));
        }

        internal static Vector2 EvaluateFibonacciFieldPosition(int index, int count)
        {
            Vector2 uv = FerrofluidEnvironmentObjects.EvaluateSurfaceUv(EvaluateFibonacciDirection(index, count), 0f);
            return (uv * 2f) - Vector2.one;
        }

        // Use a Fibonacci spiral for even sphere coverage and staggered face grids for the cube.
        // A uniform direction seed leaves gaps near rounded cube corners.
        internal static Vector2[] EvaluatePrimitiveRestPositions(
            FerrofluidSurfaceShape shape,
            int count,
            float roundness)
        {
            long cacheKey = ((long)(int)shape << 40)
                | ((long)count << 12)
                | (long)Mathf.Clamp(Mathf.RoundToInt(roundness * 4096f), 0, 4095);
            if (RestPositionCache.TryGetValue(cacheKey, out Vector2[]? cached) && cached.Length == count)
            {
                return cached;
            }

            Vector3[] directions = shape == FerrofluidSurfaceShape.Sphere
                ? EvaluateFibonacciSeed(count)
                : EvaluateCubeFaceSeed(count);

            Vector3[] positions = new Vector3[count];
            Vector3[] normals = new Vector3[count];
            int[] nearestIndices = new int[count * PrimitiveRelaxationNeighbors];
            float[] nearestDistances = new float[count * PrimitiveRelaxationNeighbors];

            // Search a spatial grid instead of scanning every site for each neighbor.
            float expectedSpacing = Mathf.Sqrt(12.6f / count);
            float cellSize = Mathf.Max(expectedSpacing * 2.5f, 0.0001f);
            int gridAxis = Mathf.Clamp(Mathf.CeilToInt(3.4f / cellSize), 4, 64);
            int cellCount = gridAxis * gridAxis * gridAxis;
            int[] cellStart = new int[cellCount + 1];
            int[] cellCursor = new int[cellCount];
            int[] siteCells = new int[count];
            int[] cellEntries = new int[count];
            int settledIterations = 0;
            for (int iteration = 0; iteration < PrimitiveRelaxationIterations; iteration++)
            {
                // Reduce the step as relaxation progresses so the lattice settles instead of oscillating.
                float anneal = Mathf.Lerp(1f, 0.15f, iteration / (float)PrimitiveRelaxationIterations);
                for (int index = 0; index < count; index++)
                {
                    FerrofluidEnvironmentObjects.EvaluateSurfacePositionAndNormal(
                        shape,
                        directions[index],
                        roundness,
                        out positions[index],
                        out normals[index]);
                }

                Array.Clear(cellStart, 0, cellStart.Length);
                for (int index = 0; index < count; index++)
                {
                    int cell = EvaluateRelaxationCell(positions[index], cellSize, gridAxis);
                    siteCells[index] = cell;
                    cellStart[cell + 1]++;
                }

                for (int cell = 0; cell < cellCount; cell++)
                {
                    cellStart[cell + 1] += cellStart[cell];
                }

                Array.Copy(cellStart, cellCursor, cellCount);
                for (int index = 0; index < count; index++)
                {
                    cellEntries[cellCursor[siteCells[index]]++] = index;
                }

                for (int index = 0; index < count; index++)
                {
                    int baseOffset = index * PrimitiveRelaxationNeighbors;
                    for (int slot = 0; slot < PrimitiveRelaxationNeighbors; slot++)
                    {
                        nearestDistances[baseOffset + slot] = float.MaxValue;
                    }

                    int centerCell = siteCells[index];
                    int centerX = centerCell % gridAxis;
                    int centerY = (centerCell / gridAxis) % gridAxis;
                    int centerZ = centerCell / (gridAxis * gridAxis);
                    for (int shell = 0; shell < gridAxis; shell++)
                    {
                        int minX = Mathf.Max(0, centerX - shell);
                        int maxX = Mathf.Min(gridAxis - 1, centerX + shell);
                        int minY = Mathf.Max(0, centerY - shell);
                        int maxY = Mathf.Min(gridAxis - 1, centerY + shell);
                        int minZ = Mathf.Max(0, centerZ - shell);
                        int maxZ = Mathf.Min(gridAxis - 1, centerZ + shell);
                        for (int cellZ = minZ; cellZ <= maxZ; cellZ++)
                        {
                            for (int cellY = minY; cellY <= maxY; cellY++)
                            {
                                for (int cellX = minX; cellX <= maxX; cellX++)
                                {
                                    int ring = Mathf.Max(
                                        Mathf.Max(Mathf.Abs(cellX - centerX), Mathf.Abs(cellY - centerY)),
                                        Mathf.Abs(cellZ - centerZ));
                                    if (ring != shell)
                                    {
                                        continue;
                                    }

                                    int cell = (((cellZ * gridAxis) + cellY) * gridAxis) + cellX;
                                    for (int entry = cellStart[cell]; entry < cellStart[cell + 1]; entry++)
                                    {
                                        int other = cellEntries[entry];
                                        if (other == index)
                                        {
                                            continue;
                                        }

                                        float distanceSquared = (positions[other] - positions[index]).sqrMagnitude;
                                        for (int slot = 0; slot < PrimitiveRelaxationNeighbors; slot++)
                                        {
                                            if (distanceSquared >= nearestDistances[baseOffset + slot])
                                            {
                                                continue;
                                            }

                                            for (int shift = PrimitiveRelaxationNeighbors - 1; shift > slot; shift--)
                                            {
                                                nearestDistances[baseOffset + shift] = nearestDistances[baseOffset + shift - 1];
                                                nearestIndices[baseOffset + shift] = nearestIndices[baseOffset + shift - 1];
                                            }

                                            nearestDistances[baseOffset + slot] = distanceSquared;
                                            nearestIndices[baseOffset + slot] = other;
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        // Stop when all eight neighbors are closer than the nearest possible site in the next shell.
                        float eighthDistance = nearestDistances[baseOffset + PrimitiveRelaxationNeighbors - 1];
                        if (eighthDistance != float.MaxValue
                            && (shell * cellSize * shell * cellSize) >= eighthDistance)
                        {
                            break;
                        }
                    }
                }

                float maxStep = 0f;
                for (int index = 0; index < count; index++)
                {
                    int baseOffset = index * PrimitiveRelaxationNeighbors;
                    float localSpacingSum = 0f;
                    for (int slot = 0; slot < PrimitiveRelaxationNeighbors; slot++)
                    {
                        localSpacingSum += Mathf.Sqrt(nearestDistances[baseOffset + slot]);
                    }

                    // Use repulsion only. Attraction can squeeze close pairs together or move them past neighboring sites.
                    float localMean = localSpacingSum / PrimitiveRelaxationNeighbors;
                    Vector3 displacement = Vector3.zero;
                    for (int slot = 0; slot < PrimitiveRelaxationNeighbors; slot++)
                    {
                        float distance = Mathf.Sqrt(nearestDistances[baseOffset + slot]);
                        Vector3 away;
                        if (distance < 0.000001f)
                        {
                            // Rounded cube edges can put distinct directions at nearly the same position.
                            // Use their direction difference to separate them when the position difference vanishes.
                            Vector3 directionDelta = directions[index] - directions[nearestIndices[baseOffset + slot]];
                            away = directionDelta.sqrMagnitude > 0.000001f ? directionDelta.normalized : Vector3.up;
                            distance = 0f;
                        }
                        else
                        {
                            away = (positions[index] - positions[nearestIndices[baseOffset + slot]]) / distance;
                        }

                        displacement += away * Mathf.Clamp01((localMean - distance) / localMean);
                    }

                    displacement /= PrimitiveRelaxationNeighbors;

                    // Move along the surface tangent, then project back. Direction-only steps cannot separate overlapping cube-edge sites.
                    Vector3 normal = normals[index];
                    Vector3 tangent = displacement - (normal * Vector3.Dot(displacement, normal));
                    float step = PrimitiveRelaxationStep * anneal * localMean;
                    maxStep = Mathf.Max(maxStep, tangent.magnitude * step);
                    directions[index] = (positions[index] + (tangent * step)).normalized;
                }

                if (maxStep < 0.0008f)
                {
                    settledIterations++;
                    if (settledIterations >= 4)
                    {
                        break;
                    }
                }
                else
                {
                    settledIterations = 0;
                }
            }

            Vector2[] restPositions = new Vector2[count];
            for (int index = 0; index < count; index++)
            {
                Vector2 uv = FerrofluidEnvironmentObjects.EvaluateSurfaceUv(directions[index], 0f);
                restPositions[index] = (uv * 2f) - Vector2.one;
            }

            if (RestPositionCache.Count >= 24)
            {
                RestPositionCache.Clear();
            }

            RestPositionCache[cacheKey] = restPositions;
            return restPositions;
        }

        private static int EvaluateRelaxationCell(Vector3 position, float cellSize, int gridAxis)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((position.x + 1.7f) / cellSize), 0, gridAxis - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt((position.y + 1.7f) / cellSize), 0, gridAxis - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt((position.z + 1.7f) / cellSize), 0, gridAxis - 1);
            return (((z * gridAxis) + y) * gridAxis) + x;
        }

        private static Vector3[] EvaluateFibonacciSeed(int count)
        {
            Vector3[] directions = new Vector3[count];
            for (int index = 0; index < count; index++)
            {
                directions[index] = EvaluateFibonacciDirection(index, count);
            }

            return directions;
        }

        // Seed each face's negative edges and let adjacent faces supply the positive edges to avoid duplicates and gaps.
        // Deduplicate shared corners before reducing the seed to the requested particle count.
        private static Vector3[] EvaluateCubeFaceSeed(int count)
        {
            int perFace = (count + 5) / 6;
            int axis = (int)Mathf.Ceil(Mathf.Sqrt(perFace));
            int candidateTotal = (axis * axis * 6) + 8;
            Vector3[] candidates = new Vector3[candidateTotal];
            int slot = 0;
            for (int face = 0; face < 6; face++)
            {
                for (int row = 0; row < axis; row++)
                {
                    for (int column = 0; column < axis; column++)
                    {
                        float u = -1f + (((column + ((row % 2) * 0.5f)) / axis) * 2f);
                        float v = -1f + ((row / (float)axis) * 2f);
                        candidates[slot++] = face switch
                        {
                            0 => new Vector3(u, v, 1f),
                            1 => new Vector3(u, v, -1f),
                            2 => new Vector3(1f, u, v),
                            3 => new Vector3(-1f, u, v),
                            4 => new Vector3(u, 1f, v),
                            _ => new Vector3(u, -1f, v),
                        };
                    }
                }
            }

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        candidates[slot++] = new Vector3(x, y, z);
                    }
                }
            }

            Vector3[] kept = new Vector3[candidateTotal];
            int keptCount = 0;
            for (int candidate = 0; candidate < candidateTotal; candidate++)
            {
                Vector3 direction = candidates[candidate].normalized;
                bool duplicate = false;
                for (int existing = 0; existing < keptCount; existing++)
                {
                    if ((kept[existing] - direction).sqrMagnitude < 0.000001f)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    kept[keptCount++] = direction;
                }
            }

            Vector3[] directions = new Vector3[count];
            int emitted = 0;
            for (int index = 0; index < keptCount && emitted < count; index++)
            {
                if ((int)((long)(index + 1) * count / keptCount) > emitted)
                {
                    directions[emitted++] = kept[index];
                }
            }

            while (emitted < count)
            {
                directions[emitted] = EvaluateFibonacciDirection(emitted, count);
                emitted++;
            }

            return directions;
        }

        internal static float EvaluatePrimitiveCellMetric(float spikeSize)
        {
            return PrimitiveDomainSpacing * 0.26f / Mathf.Clamp(spikeSize, 0.3f, 2f);
        }

        internal void ApplyConfiguration(Config config)
        {
            _config = config;
            Vector2 spikeShape = EvaluateSpikeShape(config.FerrofluidSpikeBaseSize, config.FerrofluidSpikeHeightScale);
            _simulationMaterial.SetFloat("_SpikeFactor", spikeShape.x);
            _simulationMaterial.SetFloat("_SpikeBaseCutoff", EvaluateSpikeBaseCutoff(config.FerrofluidSpikeBaseCutoff));
            _spikeHeightScale = spikeShape.y / SourceFluidHeightScale;

            Vector2Int densityLattice = EvaluateDensityLattice(
                config.FerrofluidSpikeDensity,
                _shape,
                EvaluateShapeDensityScale(_shape, config));
            _simulationMaterial.SetFloat(PrimitiveSpikeSizeId, Mathf.Clamp(config.FerrofluidPrimitiveSpikeSize, 0.3f, 2f));

            _simulationMaterial.SetFloat(PrimitiveTipRoundnessId, Mathf.Clamp(config.FerrofluidPrimitiveTipRoundness, 0f, 1f));
            _simulationMaterial.SetFloat(PrimitiveTipCapRoundingId, Mathf.Clamp01(config.FerrofluidPrimitiveTipCapRounding));
            _surfaceMaterial.SetFloat(SpikeMoundCurvatureId, Mathf.Clamp(config.FerrofluidSpikeMoundCurvature, 1f, 4f));
            _surfaceMaterial.SetFloat(PrimitiveAmbientScaleId, Mathf.Clamp(config.FerrofluidPrimitiveAmbientScale, 0f, 5f));
            _surfaceMaterial.SetFloat(PrimitiveFlattenRadiusId, Mathf.Clamp(config.FerrofluidPrimitiveFlattenRadius, 0.05f, 2f));

            _surfaceMaterial.SetFloat(PrimitiveFlattenExponentId, Mathf.Clamp(config.FerrofluidPrimitiveFlattenExponent, 0.25f, 6f));
            _surfaceMaterial.SetFloat(PrimitiveGlobalFlattenId, Mathf.Clamp01(config.FerrofluidPrimitiveGlobalFlatten));
            _surfaceMaterial.SetFloat(SphereBulgeScaleId, Mathf.Clamp(config.FerrofluidSphereBulgeScale, 0f, 1.5f));
            float surfaceRoundness = _shape == FerrofluidSurfaceShape.RoundedCube
                ? Mathf.Clamp01(config.FerrofluidCubeRoundness)
                : 0.15f;
            _surfaceMaterial.SetFloat(SurfaceRoundnessId, surfaceRoundness);
            _simulationMaterial.SetFloat(SurfaceRoundnessId, surfaceRoundness);

            float chartYawDegrees = FerrofluidEnvironmentObjects.EvaluateChartYawDegrees(_shape, config);
            float chartYawRadians = chartYawDegrees * Mathf.Deg2Rad;
            Vector2 primitiveYawCosSin = new(Mathf.Cos(chartYawRadians), Mathf.Sin(chartYawRadians));
            _simulationMaterial.SetVector(PrimitiveYawCosSinId, primitiveYawCosSin);
            _surfaceMaterial.SetVector(PrimitiveYawCosSinId, primitiveYawCosSin);
            _simulationMaterial.SetFloat(PrimitiveCellMetricId, EvaluatePrimitiveCellMetric(config.FerrofluidPrimitiveSpikeSize));
            if (densityLattice.x != _fieldColumns || densityLattice.y != _fieldRows)
            {
                // Replace particle state and rest positions together before the next tick when density changes.
                ResetParticleField(densityLattice.x, densityLattice.y, surfaceRoundness);
            }

            float kernelRadius = 1f;
            float kernelRadiusSquared = kernelRadius * kernelRadius;
            _simulationMaterial.SetFloat("_H", kernelRadius);
            _simulationMaterial.SetFloat("_HSQ", kernelRadiusSquared);
            _simulationMaterial.SetFloat("_Mass", Mathf.Clamp(config.ParticleMass, 0.01f, 5f));
            _simulationMaterial.SetFloat("_RestDensity", Mathf.Clamp(config.RestDensity, 0.1f, 5f));
            _simulationMaterial.SetFloat("_GasConstant", Mathf.Clamp(config.GasConstant, 10f, 500f));
            _simulationMaterial.SetFloat("_Viscosity", Mathf.Clamp(config.Viscosity, 1f, 20f));
            _simulationMaterial.SetFloat("_Poly6", 315f / (64f * Mathf.PI * Mathf.Pow(kernelRadius, 9f)));
            _simulationMaterial.SetFloat("_SpikyGradient", -45f / (Mathf.PI * Mathf.Pow(kernelRadius, 6f)));
            _simulationMaterial.SetFloat("_ViscosityLaplacian", 45f / (Mathf.PI * Mathf.Pow(kernelRadius, 5f)));
            _simulationMaterial.SetFloat("_PointerRadius", Mathf.Clamp(config.MagnetRadius, 0.1f, 8f));
            _simulationMaterial.SetFloat("_MaximumSlide", Mathf.Clamp(config.MagnetMaximumSlide, 0.1f, 8f));
            _simulationMaterial.SetFloat("_SlideFraction", Mathf.Clamp01(config.MagnetSlideFraction));
            _simulationMaterial.SetFloat("_SlideFalloffExponent", Mathf.Clamp(config.MagnetSlideFalloffExponent, 0.25f, 6f));

            _simulationMaterial.SetFloat("_Elasticity", Mathf.Clamp(config.MagnetElasticity, 4f, 160f));
            _simulationMaterial.SetFloat("_Damping", Mathf.Clamp(config.MagnetDamping, 2f, 30f));
            _surfaceMaterial.SetFloat("_HeightFalloffExponent", Mathf.Clamp(config.HeightFalloffExponent, 0.25f, 8f));
            _surfaceMaterial.SetFloat("_HeightGain", Mathf.Clamp(config.HeightGain, 0f, 4f));
            _surfaceMaterial.SetFloat("_BackHeightLoss", Mathf.Clamp(config.BackHeightLoss, 0f, 4f));

            _surfaceMaterial.SetFloat(BackHeightFalloffExponentId, Mathf.Clamp(config.BackHeightFalloffExponent, 0.05f, 8f));

            _surfaceMaterial.SetFloat(MagnetHeightStrengthCapId, Mathf.Clamp01(config.MagnetHeightStrengthCap));
            _surfaceMaterial.SetFloat("_MaximumHeight", Mathf.Clamp(config.MaximumHeight, 0.2f, 3f));
            _surfaceMaterial.SetFloat("_MaximumTiltRadians", Mathf.Clamp(config.MaximumTiltDegrees, 0f, 85f) * Mathf.Deg2Rad);
            _surfaceMaterial.SetFloat("_TiltPeakRadiusMultiplier", Mathf.Clamp(config.TiltPeakRadiusMultiplier, 0.1f, 1.5f));
            _surfaceMaterial.SetFloat("_TiltReturnRadiusMultiplier", Mathf.Clamp(config.TiltReturnRadiusMultiplier, 0.2f, 3f));
            _surfaceMaterial.SetFloat("_TiltRiseExponent", Mathf.Clamp(config.TiltRiseExponent, 0.25f, 6f));
            _surfaceMaterial.SetFloat("_TiltReturnExponent", Mathf.Clamp(config.TiltReturnExponent, 0.25f, 6f));
            _surfaceMaterial.SetFloat("_InnerCornerFan", Mathf.Clamp01(config.InnerCornerFan));
            _surfaceMaterial.SetFloat("_EmissionStrength", Mathf.Clamp(config.FerrofluidEmissionStrength, 0f, 0.2f));

            _surfaceMaterial.SetFloat("_Exposure", Mathf.Clamp(config.FerrofluidExposure, 0f, 2f));
            _surfaceMaterial.SetFloat(ThinFilmScaleId, Mathf.Clamp(config.FerrofluidLutScale, 0f, 4f));
            _surfaceMaterial.SetFloat(ThinFilmOffsetId, Mathf.Clamp(config.FerrofluidLutOffset, -1f, 1f));

            if (_shape == FerrofluidSurfaceShape.RoundedCube && _primitiveRestPositions != null)
            {
                Vector3[] sitePositions = new Vector3[_primitiveRestPositions.Length];
                for (int index = 0; index < sitePositions.Length; index++)
                {
                    sitePositions[index] = FerrofluidEnvironmentObjects.EvaluateSiteSurfacePosition(
                        _shape,
                        _primitiveRestPositions[index],
                        surfaceRoundness,
                        chartYawDegrees);
                }

                _simulationMaterial.SetFloat(PrimitiveSpikeSpacingId, FerrofluidEnvironmentObjects.EvaluateCubeSpikeSpacing(sitePositions));
            }

            if (_restSurface != null)
            {
                _simulationMaterial.SetTexture(RestPositionTextureId, _restPositionTexture);
                Graphics.Blit(Texture2D.blackTexture, _restSurface, _simulationMaterial, 6);
                _simulationMaterial.SetTexture(RestSurfaceTextureId, _restSurface);
            }

            ApplyEnabledState(config);
        }

        // Update visibility, spike springs, and lighting live while retaining the torus's other authored settings.
        internal void ApplyEnabledState(Config config)
        {
            _renderEnabled = _shape != FerrofluidSurfaceShape.Torus || ShouldRun(config.FerrofluidBodyEnabled);
            _surfaceMaterial.SetFloat("_SpikesEnabled", config.FerrofluidSpikesEnabled ? 1f : 0f);
            _surfaceMaterial.SetFloat(SpikeSpringStrengthId, Mathf.Clamp(config.FerrofluidSpikeSpringStrength, 1f, 800f));
            _surfaceMaterial.SetFloat(SpikeSpringDampingId, Mathf.Clamp(config.FerrofluidSpikeSpringDamping, 0f, 80f));
            _surfaceMaterial.SetFloat(ReflectivityId, Mathf.Clamp01(config.FerrofluidReflectivity));

            _surfaceMaterial.SetFloat(MatteSheenId, Mathf.Clamp01(config.FerrofluidMatteSheen));
            _surfaceMaterial.SetFloat(MatteAngleId, Mathf.Clamp(config.FerrofluidMatteAngle, 0f, 90f));
            _surfaceMaterial.SetFloat(ReflectionAngleId, Mathf.Clamp(config.FerrofluidReflectionAngle, 0f, 90f));

            _surfaceMaterial.SetVector(MatteLightRotationId, new Vector3(
                Mathf.Clamp(config.FerrofluidMatteRotationX, 0f, 360f),
                Mathf.Clamp(config.FerrofluidMatteRotationY, 0f, 360f),
                Mathf.Clamp(config.FerrofluidMatteRotationZ, 0f, 360f)));
            _surfaceMaterial.SetVector(ReflectionRotationId, new Vector3(
                Mathf.Clamp(config.FerrofluidReflectionRotationX, 0f, 360f),
                Mathf.Clamp(config.FerrofluidReflectionRotationY, 0f, 360f),
                Mathf.Clamp(config.FerrofluidReflectionRotationZ, 0f, 360f)));
            _renderer.enabled = _renderEnabled;
        }

        internal void SetFadeAlpha(float fadeAlpha)
        {
            if (fadeAlpha == _lastFadeAlpha)
            {
                return;
            }

            _lastFadeAlpha = fadeAlpha;
            _surfaceMaterial.SetFloat(FadeAlphaId, fadeAlpha);
        }

        internal void UpdateFrame(
            Rect rect,
            Vector2 pivot,
            float skew,
            bool hasCurvedCanvas,
            Matrix4x4 localToCurvedCanvas,
            Matrix4x4 curvedCanvasToLocal,
            float curvedCanvasRadius,
            float viewerLocalZSign,
            float sourceWorldScale,
            Vector3 magneticFieldPosition,
            Vector2 pointerPosition,
            float magneticFieldStrength,
            Vector3 openingPointerPosition,
            Vector3 openingPlaneNormal,
            Vector3 openingPointerDirection,
            float openingWeight,
            Vector3 handPosition,
            Vector2 handPointer,
            float handStrength)
        {
            if (!_renderEnabled)
            {
                return;
            }

            Geometry geometry = EvaluateGeometry(rect);
            ApplyGeometry(
                rect,
                pivot,
                skew,
                hasCurvedCanvas,
                localToCurvedCanvas,
                curvedCanvasToLocal,
                curvedCanvasRadius,
                viewerLocalZSign,
                sourceWorldScale,
                geometry);

            float focusedStrength = magneticFieldStrength * (1f - openingWeight);
            float openingStrength = magneticFieldStrength * openingWeight;
            _simulationMaterial.SetVector(PointerPositionId, pointerPosition);
            _simulationMaterial.SetFloat(PointerStrengthId, focusedStrength);
            _surfaceMaterial.SetVector(MagneticFieldPositionId, magneticFieldPosition);
            _surfaceMaterial.SetFloat(MagneticFieldStrengthId, focusedStrength);
            _surfaceMaterial.SetVector(MagnetOpeningPositionId, openingPointerPosition);
            _surfaceMaterial.SetVector(MagnetOpeningNormalId, openingPlaneNormal);
            _surfaceMaterial.SetVector(MagnetOpeningDirectionId, openingPointerDirection);
            _surfaceMaterial.SetFloat(MagnetOpeningStrengthId, openingStrength);
            ApplySecondaryMagnet(handPosition, handPointer, handStrength);
            AdvanceSimulation();
        }

        internal void UpdatePrimitiveFrame(
            float sourceWorldScale,
            Vector3 magneticFieldPosition,
            Vector3 pointerSurfacePosition,
            float magneticFieldStrength,
            Vector3 handPosition,
            float handStrength)
        {
            if (!_renderEnabled)
            {
                return;
            }

            ApplyWorldScaleParameters(sourceWorldScale);
            _simulationMaterial.SetFloat(PointerStrengthId, magneticFieldStrength);
            _surfaceMaterial.SetVector(MagneticFieldPositionId, magneticFieldPosition);
            _surfaceMaterial.SetFloat(MagneticFieldStrengthId, magneticFieldStrength);
            ApplySecondaryMagnet(handPosition, Vector2.zero, handStrength);

            _simulationMaterial.SetVector(PrimitiveMagnetPositionId, _renderer.transform.InverseTransformPoint(pointerSurfacePosition));
            _simulationMaterial.SetVector(PrimitiveHandPositionId, _renderer.transform.InverseTransformPoint(handPosition));
            _surfaceMaterial.SetVector(MagnetOpeningPositionId, magneticFieldPosition);
            _surfaceMaterial.SetVector(MagnetOpeningNormalId, Vector3.up);
            _surfaceMaterial.SetVector(MagnetOpeningDirectionId, Vector3.zero);
            _surfaceMaterial.SetFloat(MagnetOpeningStrengthId, 0f);
            AdvanceSimulation();
            RenderPrimitiveDeformation();
        }

        private void ApplySecondaryMagnet(Vector3 position, Vector2 pointer, float strength)
        {
            _surfaceMaterial.SetVector(SecondaryMagneticFieldPositionId, position);
            _surfaceMaterial.SetFloat(SecondaryMagneticFieldStrengthId, strength);
            _simulationMaterial.SetVector(SecondaryPointerPositionId, pointer);
            _simulationMaterial.SetFloat(SecondaryMagneticFieldStrengthId, strength);
        }

        private void AdvanceSimulation()
        {
            // The spring blit uses a fullscreen quad, so pass the ferrofluid renderer's world matrix explicitly.
            _surfaceMaterial.SetMatrix(EffectObjectToWorldId, _renderer.transform.localToWorldMatrix);
            _surfaceMaterial.SetTexture(HeightMapTextureId, _heightMap);
            float now = Time.unscaledTime;
            float elapsed = Mathf.Max(0f, now - _lastSimulationAt);
            _lastSimulationAt = now;
            _simulationAccumulator = Mathf.Min(
                _simulationAccumulator + elapsed,
                SimulationInterval * MaximumSimulationTicksPerFrame);
            while (_simulationAccumulator >= SimulationInterval)
            {
                _simulationAccumulator -= SimulationInterval;

                int stepCount = Mathf.Clamp(_config.SimulationSteps, 1, 7);
                for (int step = 0; step < stepCount; step++)
                {
                    Simulate(SimulationDeltaTimeMilliseconds);
                }

                RenderHeightMap(SimulationDeltaTimeMilliseconds);
            }
        }

        // Keep arc-length order identical to the shader so saber interaction stays continuous around corners.
        internal static Vector2 EvaluateRoundedRectangleCenterline(
            float fraction,
            Vector2 center,
            Vector2 halfExtents,
            float cornerRadius)
        {
            Vector2 innerExtents = halfExtents - (Vector2.one * cornerRadius);
            float horizontalLength = innerExtents.x * 2f;
            float verticalLength = innerExtents.y * 2f;
            float cornerLength = cornerRadius * Mathf.PI * 0.5f;
            float distance = Mathf.Repeat(fraction, 1f) * ((horizontalLength + verticalLength + (cornerLength * 2f)) * 2f);
            if (distance < horizontalLength)
            {
                return center + new Vector2(-innerExtents.x + distance, halfExtents.y);
            }

            distance -= horizontalLength;
            if (distance < cornerLength)
            {
                float angle = (Mathf.PI * 0.5f) - (distance / cornerRadius);
                return center + innerExtents + (new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * cornerRadius);
            }

            distance -= cornerLength;
            if (distance < verticalLength)
            {
                return center + new Vector2(halfExtents.x, innerExtents.y - distance);
            }

            distance -= verticalLength;
            if (distance < cornerLength)
            {
                float angle = -(distance / cornerRadius);
                return center + new Vector2(innerExtents.x, -innerExtents.y) + (new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * cornerRadius);
            }

            distance -= cornerLength;
            if (distance < horizontalLength)
            {
                return center + new Vector2(innerExtents.x - distance, -halfExtents.y);
            }

            distance -= horizontalLength;
            if (distance < cornerLength)
            {
                float angle = (-Mathf.PI * 0.5f) - (distance / cornerRadius);
                return center + new Vector2(-innerExtents.x, -innerExtents.y) + (new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * cornerRadius);
            }

            distance -= cornerLength;
            if (distance < verticalLength)
            {
                return center + new Vector2(-halfExtents.x, -innerExtents.y + distance);
            }

            distance -= verticalLength;
            float finalAngle = Mathf.PI - (distance / cornerRadius);
            return center + new Vector2(-innerExtents.x, innerExtents.y) + (new Vector2(Mathf.Cos(finalAngle), Mathf.Sin(finalAngle)) * cornerRadius);
        }

        internal static Vector2 EvaluateRoundedRectangleOutward(
            Vector2 position,
            Vector2 center,
            Vector2 halfExtents,
            float cornerRadius)
        {
            Vector2 innerExtents = halfExtents - (Vector2.one * cornerRadius);
            Vector2 relative = position - center;
            Vector2 offset = new(
                Mathf.Max(Mathf.Abs(relative.x) - innerExtents.x, 0f) * Mathf.Sign(relative.x),
                Mathf.Max(Mathf.Abs(relative.y) - innerExtents.y, 0f) * Mathf.Sign(relative.y));
            return offset.normalized;
        }

        internal static float EvaluateMagnetOpeningWeight(float crossSectionAngle)
        {
            float openingWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                Mathf.PI * 70f / 180f,
                Mathf.PI,
                crossSectionAngle));
            return openingWeight * openingWeight;
        }

        // Remove the tube center offset before mapping cursor distance across its rendered diameter.
        internal static float EvaluateCrossSectionAngle(
            float radialDistance,
            float radialCenter,
            float radialRadius)
        {
            return Mathf.Acos(Mathf.Clamp(
                (radialDistance - radialCenter) / (radialRadius * 2f),
                -1f,
                1f));
        }

        public void Dispose()
        {
            _renderer.enabled = false;
            _stateA.Release();
            _stateB.Release();
            _densityPressure.Release();
            _force.Release();
            _heightMap.Release();

            _coarseCandidates.Release();
            _springStateA.Release();
            _springStateB.Release();
            UnityEngine.Object.Destroy(_stateA);
            UnityEngine.Object.Destroy(_stateB);
            UnityEngine.Object.Destroy(_densityPressure);
            UnityEngine.Object.Destroy(_force);
            UnityEngine.Object.Destroy(_heightMap);
            UnityEngine.Object.Destroy(_coarseCandidates);
            UnityEngine.Object.Destroy(_springStateA);
            UnityEngine.Object.Destroy(_springStateB);
            if (_siteSurface != null)
            {
                _siteSurface.Release();
                UnityEngine.Object.Destroy(_siteSurface);
                ReleasePrimitiveDeformationTextures();
            }

            if (_restSurface != null)
            {
                _restSurface.Release();
                UnityEngine.Object.Destroy(_restSurface);
            }

            UnityEngine.Object.Destroy(_restPositionTexture);
            UnityEngine.Object.Destroy(_simulationMaterial);
            UnityEngine.Object.Destroy(_surfaceMaterial);
            UnityEngine.Object.Destroy(_surfaceMesh);
        }

        private void ApplyStaticSimulationParameters()
        {
            _simulationMaterial.SetInt(ParticleCountId, _particleCount);

            _surfaceMaterial.SetInt(ParticleCountId, _particleCount);
            _simulationMaterial.SetVector(DomainScaleId, EvaluateDomainScale(_shape, _fieldColumns));
            _simulationMaterial.SetFloat(HeightFactorId, HeightFactorValue);
            _simulationMaterial.SetFloat("_HeightMapScale", HeightMapScaleFactor);
            _simulationMaterial.SetFloat("_SmoothFactor", HeightMapSmoothFactor);
            _surfaceMaterial.SetFloat(HeightFactorId, HeightFactorValue);
        }

        private void ApplyGeometry(
            Rect rect,
            Vector2 pivot,
            float skew,
            bool hasCurvedCanvas,
            Matrix4x4 localToCurvedCanvas,
            Matrix4x4 curvedCanvasToLocal,
            float curvedCanvasRadius,
            float viewerLocalZSign,
            float sourceWorldScale,
            Geometry geometry)
        {
            Vector4 perimeterSegments = new(
                geometry.HorizontalLength,
                geometry.CornerLength,
                geometry.VerticalLength,
                geometry.PerimeterLength);
            _simulationMaterial.SetVector(PerimeterSegmentsId, perimeterSegments);
            _simulationMaterial.SetFloat(CornerRadiusId, geometry.CornerRadius);
            _simulationMaterial.SetFloat(RadialRadiusId, RadialRadius);
            _surfaceMaterial.SetVector(RectCenterId, rect.center);
            _surfaceMaterial.SetVector(RectSizeId, rect.size);
            _surfaceMaterial.SetFloat(RectMinYId, rect.yMin);
            _surfaceMaterial.SetFloat(PivotYId, pivot.y);
            _surfaceMaterial.SetFloat(SkewId, skew);
            _surfaceMaterial.SetVector(HalfExtentsId, geometry.HalfExtents);
            _surfaceMaterial.SetFloat(CornerRadiusId, geometry.CornerRadius);
            _surfaceMaterial.SetFloat(RadialRadiusId, RadialRadius);
            _surfaceMaterial.SetFloat(DepthRadiusId, rect.width / 6f);
            _surfaceMaterial.SetFloat(ViewerLocalZSignId, viewerLocalZSign);
            _surfaceMaterial.SetFloat(RadialCenterId, RadialCenter);
            _surfaceMaterial.SetFloat(SourceLocalScaleId, RadialRadius / SourceRadialRadius);
            _surfaceMaterial.SetMatrix(LocalToCurvedCanvasId, localToCurvedCanvas);
            _surfaceMaterial.SetMatrix(CurvedCanvasToLocalId, curvedCanvasToLocal);
            _surfaceMaterial.SetFloat(CurvedCanvasRadiusId, curvedCanvasRadius);
            _surfaceMaterial.SetFloat(HasCurvedCanvasId, hasCurvedCanvas ? 1f : 0f);
            ApplyWorldScaleParameters(sourceWorldScale);
        }

        private void ApplyWorldScaleParameters(float sourceWorldScale)
        {
            _surfaceMaterial.SetFloat("_BulgeRadius", Mathf.Clamp(_config.BulgeRadius, 0.1f, 3.2f) * sourceWorldScale);
            _surfaceMaterial.SetFloat(PrimitiveSpikeHeightRadiusId, Mathf.Clamp(_config.FerrofluidSpikeHeightRadius, 0.1f, 3.2f) * sourceWorldScale);
            _surfaceMaterial.SetFloat(PrimitiveSpikeTiltRadiusId, Mathf.Clamp(_config.FerrofluidSpikeTiltRadius, 0.1f, 3.2f) * sourceWorldScale);
            _surfaceMaterial.SetFloat(MagnetHeightDeadZoneRadiusId, Mathf.Clamp(_config.MagnetHeightDeadZoneRadius, 0f, 0.5f) * sourceWorldScale);
            _surfaceMaterial.SetFloat("_BulgeHeight", Mathf.Clamp(_config.BulgeHeight, 0f, 0.2f) * sourceWorldScale);

            _surfaceMaterial.SetFloat("_PeakWidth", Mathf.Clamp(_config.PeakWidth, 0.03f, 1f) * sourceWorldScale);
            _surfaceMaterial.SetFloat("_FluidHeightScale", SourceFluidHeightScale * _spikeHeightScale * sourceWorldScale);
        }

        private void Simulate(float deltaTimeMilliseconds)
        {
            _simulationMaterial.SetTexture(StateTextureId, _currentState);
            Graphics.Blit(Texture2D.blackTexture, _densityPressure, _simulationMaterial, 0);
            _simulationMaterial.SetTexture(DensityPressureTextureId, _densityPressure);
            Graphics.Blit(Texture2D.blackTexture, _force, _simulationMaterial, 1);
            _simulationMaterial.SetTexture(RestPositionTextureId, _restPositionTexture);
            _simulationMaterial.SetTexture(ForceTextureId, _force);
            _simulationMaterial.SetFloat(DeltaTimeMillisecondsId, deltaTimeMilliseconds);
            Graphics.Blit(Texture2D.blackTexture, _nextState, _simulationMaterial, 2);
            RenderTexture previous = _currentState;
            _currentState = _nextState;
            _nextState = previous;
        }

        private void RenderHeightMap(float deltaTimeMilliseconds)
        {
            _simulationMaterial.SetTexture(StateTextureId, _currentState);

            _surfaceMaterial.SetTexture(StateTextureId, _currentState);
            _surfaceMaterial.SetTexture(SiteSurfaceTextureId, _siteSurface);
            if (_siteSurface != null)
            {
                // Only site positions move. Rest spacing was cached when the settings changed.
                Graphics.Blit(Texture2D.blackTexture, _siteSurface, _simulationMaterial, 4);
                _simulationMaterial.SetTexture(SiteSurfaceTextureId, _siteSurface);
            }

            Graphics.Blit(Texture2D.blackTexture, _coarseCandidates, _simulationMaterial, 5);
            Graphics.Blit(Texture2D.blackTexture, _heightMap, _simulationMaterial, 3);

            _surfaceMaterial.SetFloat(DeltaTimeMillisecondsId, deltaTimeMilliseconds);
            _surfaceMaterial.SetTexture(SpringStateTextureId, _springCurrent);
            Graphics.Blit(Texture2D.blackTexture, _springNext, _surfaceMaterial, 1);
            RenderTexture previousSpring = _springCurrent;
            _springCurrent = _springNext;
            _springNext = previousSpring;
            _surfaceMaterial.SetTexture(SpringStateTextureId, _springCurrent);
        }

        private void CreatePrimitiveDeformationTextures()
        {
            _siteDeformation = CreateRenderTexture("ChromaGLS Ferrofluid Site Deformation", _particleTextureSize, FilterMode.Point, TextureWrapMode.Clamp);
            _siteBulge = CreateRenderTexture("ChromaGLS Ferrofluid Site Bulge", _particleTextureSize, FilterMode.Point, TextureWrapMode.Clamp);
            _surfaceMaterial.SetTexture(SiteDeformationTextureId, _siteDeformation);
            _surfaceMaterial.SetTexture(SiteBulgeTextureId, _siteBulge);
        }

        private void RenderPrimitiveDeformation()
        {
            // Magnet positions change between simulation ticks, so refresh the shared site responses every presentation frame.
            Graphics.Blit(Texture2D.blackTexture, _siteDeformation, _surfaceMaterial, 2);
            Graphics.Blit(Texture2D.blackTexture, _siteBulge, _surfaceMaterial, 3);
        }

        private void ReleasePrimitiveDeformationTextures()
        {
            _siteDeformation!.Release();
            _siteBulge!.Release();
            UnityEngine.Object.Destroy(_siteDeformation);
            UnityEngine.Object.Destroy(_siteBulge);
        }

        // Keep this CPU spring calculation in sync with the height-spring shader.
        internal static Vector2 IntegrateSpikeSpringScale(
            float height,
            float velocity,
            float target,
            float strength,
            float damping,
            float deltaTimeSeconds)
        {
            float nextVelocity = velocity + ((((target - height) * strength) - (velocity * damping)) * deltaTimeSeconds);
            return new Vector2(height + (nextVelocity * deltaTimeSeconds), nextVelocity);
        }

        // Density changes replace the lattice, so reset both particle buffers and enlarge them if needed.
        private void ResetParticleField(int columns, int rows, float roundness)
        {
            Texture2D previousRest = _restPositionTexture;
            _fieldColumns = columns;
            _fieldRows = rows;
            _particleCount = columns * rows;

            _simulationMaterial.SetVector(DomainScaleId, EvaluateDomainScale(_shape, columns));
            EnsureParticleTextureCapacity();
            _restPositionTexture = CreateInitialPositionTexture(columns, rows, _shape, _particleTextureSize, roundness, out _primitiveRestPositions);
            Graphics.Blit(_restPositionTexture, _stateA);
            Graphics.Blit(_restPositionTexture, _stateB);
            _currentState = _stateA;
            _nextState = _stateB;
            _simulationMaterial.SetInt(ParticleCountId, _particleCount);
            _surfaceMaterial.SetInt(ParticleCountId, _particleCount);

            // Discard time left over from the old lattice so the replacement starts at its initial state.
            _lastSimulationAt = Time.unscaledTime;
            _simulationAccumulator = 0f;
            UnityEngine.Object.Destroy(previousRest);
        }

        private void EnsureParticleTextureCapacity()
        {
            int required = EvaluateParticleTextureSize(_particleCount);
            if (required == _particleTextureSize)
            {
                return;
            }

            _particleTextureSize = required;
            ReleaseAndRecreate(ref _stateA, "ChromaGLS Ferrofluid State A", FilterMode.Point, TextureWrapMode.Repeat);
            ReleaseAndRecreate(ref _stateB, "ChromaGLS Ferrofluid State B", FilterMode.Point, TextureWrapMode.Repeat);
            ReleaseAndRecreate(ref _densityPressure, "ChromaGLS Ferrofluid Density Pressure", FilterMode.Point, TextureWrapMode.Clamp);
            ReleaseAndRecreate(ref _force, "ChromaGLS Ferrofluid Force", FilterMode.Point, TextureWrapMode.Clamp);

            // Resize spring state with the particle texture so each site still maps to its own texel.
            ReleaseAndRecreate(ref _springStateA, "ChromaGLS Ferrofluid Spring State A", FilterMode.Point, TextureWrapMode.Clamp);
            ReleaseAndRecreate(ref _springStateB, "ChromaGLS Ferrofluid Spring State B", FilterMode.Point, TextureWrapMode.Clamp);
            _springCurrent = _springStateA;
            _springNext = _springStateB;
            if (_siteSurface != null)
            {
                _siteSurface.Release();
                UnityEngine.Object.Destroy(_siteSurface);
                _siteSurface = CreateRenderTexture("ChromaGLS Ferrofluid Site Surface", _particleTextureSize, FilterMode.Point, TextureWrapMode.Clamp);
                _restSurface!.Release();
                UnityEngine.Object.Destroy(_restSurface);
                _restSurface = CreateRenderTexture("ChromaGLS Ferrofluid Rest Surface", _particleTextureSize, FilterMode.Point, TextureWrapMode.Clamp);
                ReleasePrimitiveDeformationTextures();
                CreatePrimitiveDeformationTextures();
            }

            _simulationMaterial.SetInt(ParticleTextureSizeId, _particleTextureSize);
            _surfaceMaterial.SetInt(ParticleTextureSizeId, _particleTextureSize);
        }

        private void ReleaseAndRecreate(
            ref RenderTexture texture,
            string name,
            FilterMode filterMode,
            TextureWrapMode wrapMode)
        {
            texture.Release();
            UnityEngine.Object.Destroy(texture);
            texture = CreateRenderTexture(name, _particleTextureSize, filterMode, wrapMode);
        }

        private static RenderTexture CreateRenderTexture(
            string name,
            int size,
            FilterMode filterMode,
            TextureWrapMode wrapMode,
            int height = 0)
        {
            RenderTexture texture = new(size, height == 0 ? size : height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                name = name,
                filterMode = filterMode,
                wrapMode = wrapMode,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.DontSave,
            };
            texture.Create();
            return texture;
        }

        private static Texture2D CreateInitialPositionTexture(int fieldColumns, int fieldRows, FerrofluidSurfaceShape shape, int textureSize, float roundness, out Vector2[]? primitiveRestPositions)
        {
            Texture2D texture = new(textureSize, textureSize, TextureFormat.RGBAFloat, false, true)
            {
                name = "ChromaGLS Ferrofluid Rest Positions",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.DontSave,
            };
            Color[] pixels = new Color[textureSize * textureSize];
            Vector2[]? relaxedRest = shape == FerrofluidSurfaceShape.Torus
                ? null
                : EvaluatePrimitiveRestPositions(shape, fieldColumns * fieldRows, roundness);
            for (int row = 0; row < fieldRows; row++)
            {
                for (int column = 0; column < fieldColumns; column++)
                {
                    int index = (row * fieldColumns) + column;
                    Vector2 restPosition = relaxedRest?[index]
                        ?? EvaluateRestFieldPosition(column, row, fieldColumns, fieldRows, shape);
                    pixels[index] = new Color(restPosition.x, restPosition.y, 0f, 0f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            primitiveRestPositions = relaxedRest;
            return texture;
        }

        // Use UInt32 indices because the primitive mesh has more than 65,536 vertices.
        private static Mesh CreateSurfaceMesh(int surfaceResolution)
        {
            int sideVertexCount = surfaceResolution + 1;
            Vector3[] vertices = new Vector3[sideVertexCount * sideVertexCount];
            Vector2[] uvs = new Vector2[vertices.Length];
            for (int y = 0; y < sideVertexCount; y++)
            {
                float v = (float)y / surfaceResolution;
                for (int x = 0; x < sideVertexCount; x++)
                {
                    float u = (float)x / surfaceResolution;
                    int index = (y * sideVertexCount) + x;
                    vertices[index] = new Vector3(u, v, 0f);
                    uvs[index] = new Vector2(u, v);
                }
            }

            int[] triangles = new int[surfaceResolution * surfaceResolution * 6];
            int triangleIndex = 0;
            for (int y = 0; y < surfaceResolution; y++)
            {
                for (int x = 0; x < surfaceResolution; x++)
                {
                    int lowerLeft = (y * sideVertexCount) + x;
                    int upperLeft = lowerLeft + sideVertexCount;
                    triangles[triangleIndex++] = lowerLeft;
                    triangles[triangleIndex++] = lowerLeft + 1;
                    triangles[triangleIndex++] = upperLeft;
                    triangles[triangleIndex++] = lowerLeft + 1;
                    triangles[triangleIndex++] = upperLeft + 1;
                    triangles[triangleIndex++] = upperLeft;
                }
            }

            Mesh mesh = new()
            {
                name = "ChromaGLS Ferrofluid GPU Surface",
                indexFormat = IndexFormat.UInt32,
                vertices = vertices,
                uv = uvs,
                triangles = triangles,
                bounds = new Bounds(Vector3.zero, Vector3.one * 10000f),
            };
            mesh.UploadMeshData(true);
            return mesh;
        }

        internal static Geometry EvaluateGeometry(Rect rect)
        {
            float borderPadding = rect.height * RequirementBorderPaddingHeightFraction;
            Vector2 halfExtents = new(
                Mathf.Max((rect.width * 0.5f) - 0.1f + borderPadding, 0.2f),
                Mathf.Max((rect.height * 0.5f) - 0.1f + borderPadding, 0.2f));
            float cornerRadius = Mathf.Min(
                halfExtents.y * (SourceCornerRadius / SourceHalfHeight),
                Mathf.Min(halfExtents.x, halfExtents.y) - 0.001f);
            return new Geometry(halfExtents, cornerRadius);
        }

        internal readonly struct Geometry
        {
            internal Geometry(Vector2 halfExtents, float cornerRadius)
            {
                HalfExtents = halfExtents;
                CornerRadius = cornerRadius;
                HorizontalLength = (halfExtents.x - cornerRadius) * 2f;
                VerticalLength = (halfExtents.y - cornerRadius) * 2f;
                CornerLength = cornerRadius * Mathf.PI * 0.5f;
                PerimeterLength = (HorizontalLength + VerticalLength + (CornerLength * 2f)) * 2f;
            }

            internal Vector2 HalfExtents { get; }

            internal float CornerRadius { get; }

            internal float HorizontalLength { get; }

            internal float VerticalLength { get; }

            internal float CornerLength { get; }

            internal float PerimeterLength { get; }
        }
    }
#pragma warning restore SA1202, SA1204
}
