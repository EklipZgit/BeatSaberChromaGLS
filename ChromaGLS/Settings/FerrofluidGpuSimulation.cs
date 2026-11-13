using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ChromaGLS.Settings
{
#pragma warning disable SA1202, SA1204
    // Run fluid pressure, forces, integration, and spike deformation on the GPU.
    internal sealed partial class FerrofluidGpuSimulation : IDisposable
    {
#pragma warning disable SA1201, SA1516 // Erased snapshot hook declarations precede the field block.
        // Snapshot bookkeeping declarations live unimplemented in the normal build, so the calls
        // and their argument expressions are erased entirely; the tweak partial supplies them.
        partial void RecordSnapshotFloat(Material material, int propertyId, float value);
        partial void RecordSnapshotFloat(Material material, string propertyName, float value);
        partial void RecordSnapshotInt(Material material, int propertyId, int value);
        partial void RecordSnapshotInt(Material material, string propertyName, int value);
        partial void RecordSnapshotVector(Material material, int propertyId, Vector4 value);
        partial void RecordSnapshotVector(Material material, string propertyName, Vector4 value);
        partial void RecordSnapshotMatrix(Material material, int propertyId, Matrix4x4 value);
        partial void RecordSnapshotTexture(Material material, int propertyId, Texture? texture);
        partial void RecordSnapshotTexture(Material material, string propertyName, Texture? texture);

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
        private const float SimulationDeltaTimeMilliseconds = 8f;

        private static readonly Dictionary<(FerrofluidSurfaceShape Shape, int Count, float Roundness, Vector4 Pressure, float Damping), (Vector3[] Positions, float[] Densities)> PrimitiveHomeCache = new();
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
        private static readonly int BaseColorId = Shader.PropertyToID("_FerrofluidBaseColor");
        private static readonly int ReflectionStrengthsId = Shader.PropertyToID("_FerrofluidReflectionStrengths");
        private static readonly int ThinFilmStrengthId = Shader.PropertyToID("_FerrofluidThinFilmStrength");
        private static readonly int ThinFilmAngleRateId = Shader.PropertyToID("_ThinFilmAngleRate");

        private static readonly int FadeAlphaId = Shader.PropertyToID("_FadeAlpha");
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
        private static readonly int PrimitiveFrameRotationId = Shader.PropertyToID("_PrimitiveFrameRotation");
        private static readonly int PrimitiveLinearVelocityDeltaId = Shader.PropertyToID("_PrimitiveLinearVelocityDelta");
        private static readonly int PrimitivePreviousAngularVelocityId = Shader.PropertyToID("_PrimitivePreviousAngularVelocity");
        private static readonly int PrimitiveAngularVelocityId = Shader.PropertyToID("_PrimitiveAngularVelocity");
        private static readonly int MagnetHeightAmplificationId = Shader.PropertyToID("_MagnetHeightAmplification");

        private static readonly int ParticleTextureSizeId = Shader.PropertyToID("_ParticleTextureSize");
        private static readonly int PrimitiveSpikeSizeId = Shader.PropertyToID("_PrimitiveSpikeSize");
        private static readonly int PrimitiveTipRoundnessId = Shader.PropertyToID("_PrimitiveTipRoundness");
        private static readonly int PrimitiveTipCapRoundingId = Shader.PropertyToID("_PrimitiveTipCapRounding");
        private static readonly int PrimitiveSpikeSpacingId = Shader.PropertyToID("_PrimitiveSpikeSpacing");
        private static readonly int SpikeMoundCurvatureId = Shader.PropertyToID("_SpikeMoundCurvature");
        private static readonly int PrimitiveSpikeHeightRadiusId = Shader.PropertyToID("_PrimitiveSpikeHeightRadius");
        private static readonly int PrimitiveSpikeTiltRadiusId = Shader.PropertyToID("_PrimitiveSpikeTiltRadius");
        private static readonly int PrimitiveFlattenRadiusId = Shader.PropertyToID("_PrimitiveFlattenRadius");
        private static readonly int PrimitiveFlattenExponentId = Shader.PropertyToID("_PrimitiveFlattenExponent");
        private static readonly int PrimitiveGlobalFlattenId = Shader.PropertyToID("_PrimitiveGlobalSpikeFlatten");
        private static readonly int SphereBulgeScaleId = Shader.PropertyToID("_SphereBulgeScale");

        private static readonly int SiteSurfaceTextureId = Shader.PropertyToID("_SiteSurfaceTex");
        private static readonly int SiteDeformationTextureId = Shader.PropertyToID("_SiteDeformationTex");
        private static readonly int SiteBulgeTextureId = Shader.PropertyToID("_SiteBulgeTex");
        private static readonly int RestSurfaceTextureId = Shader.PropertyToID("_RestSurfaceTex");
        private static readonly int PrimitiveYawCosSinId = Shader.PropertyToID("_PrimitiveYawCosSin");
        private static readonly int PrimitiveHomeElasticityGainId = Shader.PropertyToID("_PrimitiveHomeElasticityGain");
        private static readonly int PrimitiveHomeQuadraticGainId = Shader.PropertyToID("_PrimitiveHomeQuadraticGain");
        private static readonly int PrimitiveHomeSoftRangeId = Shader.PropertyToID("_PrimitiveHomeSoftRange");
        private static readonly int PrimitiveHomeTravelLimitId = Shader.PropertyToID("_PrimitiveHomeTravelLimit");
        private static readonly int PrimitiveHomeDampingGainId = Shader.PropertyToID("_PrimitiveHomeDampingGain");
        private static readonly int PrimitiveRestDensityReadyId = Shader.PropertyToID("_PrimitiveRestDensityReady");
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
        private Vector3[]? _bakedPrimitiveHomes;
        private float[]? _bakedPrimitiveDensities;
        private (FerrofluidSurfaceShape Shape, int Count, float Roundness, Vector4 Pressure, float Damping)? _homeBakeKey;
        private float _homeChartYaw;

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
        private Quaternion _previousPrimitiveRotation;
        private Vector3 _previousPrimitiveLinearVelocity;
        private Vector3 _previousPrimitiveAngularVelocity;
        private Vector3 _pendingPrimitiveLinearVelocity;
        private Vector3 _pendingPrimitiveAngularVelocity;
        private bool _primitiveHistoryValid;
        private bool _renderEnabled;
        private bool _spikesAllowed = true;
        private float _absSourceWorldScale = 1f;
        private float _shadowDeformationPerScale = 1f;

        internal Mesh SurfaceMesh => _surfaceMesh;

        internal Material SurfaceMaterial => _surfaceMaterial;

        // Conservative bounds envelope for the GPU shadow mask in world units.
        internal float CalculateShadowDeformationPadding()
            => _shadowDeformationPerScale * _absSourceWorldScale;

        // Spring overshoot beyond the clamped spike target at maximum amplification, maximum
        // magnetic bulge, and a slack term covering the density-blended support region.
        // Capture framing only, not a physics clamp.
        private float EvaluateShadowDeformationPerScale(in Config config)
        {
            const float maxAmplification = 1.6f;
            float maxSpringTarget = Mathf.Max(
                0.44f,
                0.44f + (maxAmplification * (Mathf.Min(Mathf.Clamp(config.MaximumHeight, 0.2f, 3f), 1.37f) - 0.44f)));
            float maxBulge = Mathf.Clamp(config.BulgeHeight, 0f, 0.2f)
                * Mathf.Clamp01(config.MagnetHeightStrengthCap)
                * (maxAmplification + 1f)
                * Mathf.Max(1f, Mathf.Clamp(config.FerrofluidSphereBulgeScale, 0f, 1.5f));
            return
                (2f * Mathf.Abs(SourceFluidHeightScale * _spikeHeightScale) * HeightFactorValue * maxSpringTarget)
                + maxBulge
                + 0.2f;
        }

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
            RecordSnapshotTexture(_surfaceMaterial, EnvironmentTextureId, resources.EnvironmentTexture);
            _surfaceMaterial.SetTexture(ThinFilmTextureId, resources.ThinFilmTexture);
            RecordSnapshotTexture(_surfaceMaterial, ThinFilmTextureId, resources.ThinFilmTexture);
            FerrofluidRoomReflections.RequestEnvironment();

            _simulationMaterial.SetFloat(SurfaceShapeModeId, (float)shape);
            RecordSnapshotFloat(_simulationMaterial, SurfaceShapeModeId, (float)shape);
            _surfaceMaterial.SetFloat(SurfaceShapeModeId, (float)shape);
            RecordSnapshotFloat(_surfaceMaterial, SurfaceShapeModeId, (float)shape);
            _simulationMaterial.SetInt(ParticleTextureSizeId, _particleTextureSize);
            RecordSnapshotInt(_simulationMaterial, ParticleTextureSizeId, _particleTextureSize);
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
            RecordSnapshotFloat(_simulationMaterial, CoarseCandidateSizeId, candidateMapSize);
            _simulationMaterial.SetTexture(CoarseCandidateTextureId, _coarseCandidates);
            RecordSnapshotTexture(_simulationMaterial, CoarseCandidateTextureId, _coarseCandidates);

            // Each particle owns one spring-state texel. Start at zero so spikes settle into shape over the first few ticks.
            _springStateA = CreateRenderTexture("ChromaGLS Ferrofluid Spring State A", _particleTextureSize, FilterMode.Point, TextureWrapMode.Clamp);
            _springStateB = CreateRenderTexture("ChromaGLS Ferrofluid Spring State B", _particleTextureSize, FilterMode.Point, TextureWrapMode.Clamp);
            _surfaceMaterial.SetInt(ParticleTextureSizeId, _particleTextureSize);
            RecordSnapshotInt(_surfaceMaterial, ParticleTextureSizeId, _particleTextureSize);
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
                RecordSnapshotTexture(_simulationMaterial, StateTextureId, _currentState);
                _simulationMaterial.SetTexture(RestPositionTextureId, _restPositionTexture);
                RecordSnapshotTexture(_simulationMaterial, RestPositionTextureId, _restPositionTexture);
                _surfaceMaterial.SetTexture(RestPositionTextureId, _restPositionTexture);
                RecordSnapshotTexture(_surfaceMaterial, RestPositionTextureId, _restPositionTexture);
                _surfaceMaterial.SetTexture(DensityPressureTextureId, _densityPressure);
                RecordSnapshotTexture(_surfaceMaterial, DensityPressureTextureId, _densityPressure);
                Graphics.Blit(Texture2D.blackTexture, _siteSurface, _simulationMaterial, 4);
                _simulationMaterial.SetTexture(SiteSurfaceTextureId, _siteSurface);
                RecordSnapshotTexture(_simulationMaterial, SiteSurfaceTextureId, _siteSurface);
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
                FerrofluidBaseRed = live.FerrofluidBaseRed,
                FerrofluidBaseGreen = live.FerrofluidBaseGreen,
                FerrofluidBaseBlue = live.FerrofluidBaseBlue,
                FerrofluidRoomReflectionStrength = live.FerrofluidRoomReflectionStrength,
                FerrofluidFeetReflectionStrength = live.FerrofluidFeetReflectionStrength,
                FerrofluidParabolaReflectionStrength = live.FerrofluidParabolaReflectionStrength,
                FerrofluidParabolaReflectionSaturation = live.FerrofluidParabolaReflectionSaturation,
                FerrofluidThinFilmStrength = live.FerrofluidThinFilmStrength,
                FerrofluidThinFilmAngleRate = live.FerrofluidThinFilmAngleRate,
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
                16.25f / Mathf.Clamp(baseSize, 0.5f, 8f),
                SourceFluidHeightScale * Mathf.Clamp(heightScale, 0.25f, 4f));
        }

        internal static float EvaluateSpikeBaseCutoff(float cutoff)
        {
            return Mathf.Clamp(cutoff, 0f, 0.3f);
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
            return FerrofluidPrimitiveLayout.EvaluateFibonacciDirection(index, count);
        }

        internal static Vector2 EvaluateFibonacciFieldPosition(int index, int count)
        {
            Vector2 uv = FerrofluidEnvironmentObjects.EvaluateSurfaceUv(EvaluateFibonacciDirection(index, count), 0f);
            return (uv * 2f) - Vector2.one;
        }

        // CPU callers use the canonical chart; the runtime encodes settled homes for its active chart.
        internal static Vector2[] EvaluatePrimitiveRestPositions(
            FerrofluidSurfaceShape shape,
            int count,
            float roundness)
        {
            Vector3[] positions = FerrofluidPrimitiveLayout.Get(shape, count, roundness).Positions;
            Vector2[] restPositions = EncodePrimitivePositions(positions, 0f);
            return restPositions;
        }

        private static Vector2[] EncodePrimitivePositions(Vector3[] positions, float chartYawDegrees)
        {
            Vector2[] encoded = new Vector2[positions.Length];
            for (int index = 0; index < positions.Length; index++)
            {
                Vector2 uv = FerrofluidEnvironmentObjects.EvaluateSurfaceUv(positions[index], chartYawDegrees);
                encoded[index] = (uv * 2f) - Vector2.one;
            }

            return encoded;
        }

        // Keep the primitive under-layer shallow so magnetic growth remains visible in spikes.
        // The torus retains its configured bulge height.
        internal static float EvaluateBulgeHeight(FerrofluidSurfaceShape shape, float configuredHeight, float sourceWorldScale)
        {
            return Mathf.Clamp(configuredHeight, 0f, 0.2f)
                * sourceWorldScale
                * (shape == FerrofluidSurfaceShape.Torus ? 1f : 0.5f);
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
            RecordSnapshotFloat(_simulationMaterial, "_SpikeFactor", spikeShape.x);
            _simulationMaterial.SetFloat("_SpikeBaseCutoff", EvaluateSpikeBaseCutoff(config.FerrofluidSpikeBaseCutoff));
            RecordSnapshotFloat(_simulationMaterial, "_SpikeBaseCutoff", EvaluateSpikeBaseCutoff(config.FerrofluidSpikeBaseCutoff));
            _spikeHeightScale = spikeShape.y / SourceFluidHeightScale;
            _shadowDeformationPerScale = EvaluateShadowDeformationPerScale(config);

            Vector2Int densityLattice = EvaluateDensityLattice(
                config.FerrofluidSpikeDensity,
                _shape,
                EvaluateShapeDensityScale(_shape, config));
            _simulationMaterial.SetFloat(PrimitiveSpikeSizeId, Mathf.Clamp(config.FerrofluidPrimitiveSpikeSize, 0.3f, 2f));
            RecordSnapshotFloat(_simulationMaterial, PrimitiveSpikeSizeId, Mathf.Clamp(config.FerrofluidPrimitiveSpikeSize, 0.3f, 2f));

            _simulationMaterial.SetFloat(PrimitiveTipRoundnessId, Mathf.Clamp(config.FerrofluidPrimitiveTipRoundness, 0f, 1f));
            RecordSnapshotFloat(_simulationMaterial, PrimitiveTipRoundnessId, Mathf.Clamp(config.FerrofluidPrimitiveTipRoundness, 0f, 1f));
            _simulationMaterial.SetFloat(PrimitiveTipCapRoundingId, Mathf.Clamp01(config.FerrofluidPrimitiveTipCapRounding));
            RecordSnapshotFloat(_simulationMaterial, PrimitiveTipCapRoundingId, Mathf.Clamp01(config.FerrofluidPrimitiveTipCapRounding));
            _surfaceMaterial.SetFloat(SpikeMoundCurvatureId, Mathf.Clamp(config.FerrofluidSpikeMoundCurvature, 1f, 4f));
            RecordSnapshotFloat(_surfaceMaterial, SpikeMoundCurvatureId, Mathf.Clamp(config.FerrofluidSpikeMoundCurvature, 1f, 4f));
            _surfaceMaterial.SetFloat(PrimitiveFlattenRadiusId, Mathf.Clamp(config.FerrofluidPrimitiveFlattenRadius, 0.05f, 2f));
            RecordSnapshotFloat(_surfaceMaterial, PrimitiveFlattenRadiusId, Mathf.Clamp(config.FerrofluidPrimitiveFlattenRadius, 0.05f, 2f));

            _surfaceMaterial.SetFloat(PrimitiveFlattenExponentId, Mathf.Clamp(config.FerrofluidPrimitiveFlattenExponent, 0.25f, 6f));
            RecordSnapshotFloat(_surfaceMaterial, PrimitiveFlattenExponentId, Mathf.Clamp(config.FerrofluidPrimitiveFlattenExponent, 0.25f, 6f));
            _surfaceMaterial.SetFloat(PrimitiveGlobalFlattenId, Mathf.Clamp01(config.FerrofluidPrimitiveGlobalFlatten));
            RecordSnapshotFloat(_surfaceMaterial, PrimitiveGlobalFlattenId, Mathf.Clamp01(config.FerrofluidPrimitiveGlobalFlatten));
            _surfaceMaterial.SetFloat(SphereBulgeScaleId, Mathf.Clamp(config.FerrofluidSphereBulgeScale, 0f, 1.5f));
            RecordSnapshotFloat(_surfaceMaterial, SphereBulgeScaleId, Mathf.Clamp(config.FerrofluidSphereBulgeScale, 0f, 1.5f));
            float surfaceRoundness = _shape == FerrofluidSurfaceShape.RoundedCube
                ? Mathf.Clamp01(config.FerrofluidCubeRoundness)
                : 0.15f;
            _surfaceMaterial.SetFloat(SurfaceRoundnessId, surfaceRoundness);
            RecordSnapshotFloat(_surfaceMaterial, SurfaceRoundnessId, surfaceRoundness);
            _simulationMaterial.SetFloat(SurfaceRoundnessId, surfaceRoundness);
            RecordSnapshotFloat(_simulationMaterial, SurfaceRoundnessId, surfaceRoundness);

            float chartYawDegrees = FerrofluidEnvironmentObjects.EvaluateChartYawDegrees(_shape, config);
            float chartYawRadians = chartYawDegrees * Mathf.Deg2Rad;
            Vector2 primitiveYawCosSin = new(Mathf.Cos(chartYawRadians), Mathf.Sin(chartYawRadians));
            _simulationMaterial.SetVector(PrimitiveYawCosSinId, primitiveYawCosSin);
            RecordSnapshotVector(_simulationMaterial, PrimitiveYawCosSinId, primitiveYawCosSin);
            _surfaceMaterial.SetVector(PrimitiveYawCosSinId, primitiveYawCosSin);
            RecordSnapshotVector(_surfaceMaterial, PrimitiveYawCosSinId, primitiveYawCosSin);
            _simulationMaterial.SetFloat(PrimitiveCellMetricId, EvaluatePrimitiveCellMetric(config.FerrofluidPrimitiveSpikeSize));
            RecordSnapshotFloat(_simulationMaterial, PrimitiveCellMetricId, EvaluatePrimitiveCellMetric(config.FerrofluidPrimitiveSpikeSize));
            if (_shape == FerrofluidSurfaceShape.Torus
                && (densityLattice.x != _fieldColumns || densityLattice.y != _fieldRows))
            {
                // Replace particle state and rest positions together before the next tick when density changes.
                ResetParticleField(densityLattice.x, densityLattice.y, surfaceRoundness);
            }

            float kernelRadius = 1f;
            float kernelRadiusSquared = kernelRadius * kernelRadius;
            _simulationMaterial.SetFloat("_H", kernelRadius);
            RecordSnapshotFloat(_simulationMaterial, "_H", kernelRadius);
            _simulationMaterial.SetFloat("_HSQ", kernelRadiusSquared);
            RecordSnapshotFloat(_simulationMaterial, "_HSQ", kernelRadiusSquared);
            _simulationMaterial.SetFloat("_Mass", Mathf.Clamp(config.ParticleMass, 0.01f, 5f));
            RecordSnapshotFloat(_simulationMaterial, "_Mass", Mathf.Clamp(config.ParticleMass, 0.01f, 5f));
            _simulationMaterial.SetFloat("_RestDensity", Mathf.Clamp(config.RestDensity, 0.1f, 5f));
            RecordSnapshotFloat(_simulationMaterial, "_RestDensity", Mathf.Clamp(config.RestDensity, 0.1f, 5f));
            _simulationMaterial.SetFloat("_GasConstant", Mathf.Clamp(config.GasConstant, 10f, 500f));
            RecordSnapshotFloat(_simulationMaterial, "_GasConstant", Mathf.Clamp(config.GasConstant, 10f, 500f));
            _simulationMaterial.SetFloat("_Viscosity", Mathf.Clamp(config.Viscosity, 1f, 20f));
            RecordSnapshotFloat(_simulationMaterial, "_Viscosity", Mathf.Clamp(config.Viscosity, 1f, 20f));
            _simulationMaterial.SetFloat("_Poly6", 315f / (64f * Mathf.PI * Mathf.Pow(kernelRadius, 9f)));
            RecordSnapshotFloat(_simulationMaterial, "_Poly6", 315f / (64f * Mathf.PI * Mathf.Pow(kernelRadius, 9f)));
            _simulationMaterial.SetFloat("_SpikyGradient", -45f / (Mathf.PI * Mathf.Pow(kernelRadius, 6f)));
            RecordSnapshotFloat(_simulationMaterial, "_SpikyGradient", -45f / (Mathf.PI * Mathf.Pow(kernelRadius, 6f)));
            _simulationMaterial.SetFloat("_ViscosityLaplacian", 45f / (Mathf.PI * Mathf.Pow(kernelRadius, 5f)));
            RecordSnapshotFloat(_simulationMaterial, "_ViscosityLaplacian", 45f / (Mathf.PI * Mathf.Pow(kernelRadius, 5f)));
            _simulationMaterial.SetFloat("_PointerRadius", Mathf.Clamp(config.MagnetRadius, 0.1f, 8f));
            RecordSnapshotFloat(_simulationMaterial, "_PointerRadius", Mathf.Clamp(config.MagnetRadius, 0.1f, 8f));
            _simulationMaterial.SetFloat("_MaximumSlide", Mathf.Clamp(config.MagnetMaximumSlide, 0.1f, 8f));
            RecordSnapshotFloat(_simulationMaterial, "_MaximumSlide", Mathf.Clamp(config.MagnetMaximumSlide, 0.1f, 8f));
            _simulationMaterial.SetFloat("_SlideFraction", Mathf.Clamp01(config.MagnetSlideFraction));
            RecordSnapshotFloat(_simulationMaterial, "_SlideFraction", Mathf.Clamp01(config.MagnetSlideFraction));
            _simulationMaterial.SetFloat("_SlideFalloffExponent", Mathf.Clamp(config.MagnetSlideFalloffExponent, 0.25f, 6f));
            RecordSnapshotFloat(_simulationMaterial, "_SlideFalloffExponent", Mathf.Clamp(config.MagnetSlideFalloffExponent, 0.25f, 6f));

            _simulationMaterial.SetFloat("_Elasticity", Mathf.Clamp(config.MagnetElasticity, 4f, 160f));
            RecordSnapshotFloat(_simulationMaterial, "_Elasticity", Mathf.Clamp(config.MagnetElasticity, 4f, 160f));
            _simulationMaterial.SetFloat("_Damping", Mathf.Clamp(config.MagnetDamping, 2f, 30f));
            RecordSnapshotFloat(_simulationMaterial, "_Damping", Mathf.Clamp(config.MagnetDamping, 2f, 30f));
            _simulationMaterial.SetFloat(PrimitiveHomeElasticityGainId, Mathf.Clamp(config.FerrofluidHomeElasticityGain, 0f, 2f));
            RecordSnapshotFloat(_simulationMaterial, PrimitiveHomeElasticityGainId, Mathf.Clamp(config.FerrofluidHomeElasticityGain, 0f, 2f));
            _simulationMaterial.SetFloat(PrimitiveHomeQuadraticGainId, Mathf.Pow(2f, Mathf.Clamp(config.FerrofluidHomeQuadraticPower, 0f, 16f)));
            RecordSnapshotFloat(_simulationMaterial, PrimitiveHomeQuadraticGainId, Mathf.Pow(2f, Mathf.Clamp(config.FerrofluidHomeQuadraticPower, 0f, 16f)));
            _simulationMaterial.SetFloat(PrimitiveHomeSoftRangeId, Mathf.Clamp(config.FerrofluidHomeSoftRange, 0f, 0.9f));
            RecordSnapshotFloat(_simulationMaterial, PrimitiveHomeSoftRangeId, Mathf.Clamp(config.FerrofluidHomeSoftRange, 0f, 0.9f));
            _simulationMaterial.SetFloat(PrimitiveHomeTravelLimitId, Mathf.Clamp(config.FerrofluidHomeTravelLimit, 0.05f, 0.5f));
            RecordSnapshotFloat(_simulationMaterial, PrimitiveHomeTravelLimitId, Mathf.Clamp(config.FerrofluidHomeTravelLimit, 0.05f, 0.5f));
            _simulationMaterial.SetFloat(PrimitiveHomeDampingGainId, Mathf.Clamp(config.FerrofluidHomeDampingGain, 0f, 4f));
            RecordSnapshotFloat(_simulationMaterial, PrimitiveHomeDampingGainId, Mathf.Clamp(config.FerrofluidHomeDampingGain, 0f, 4f));
            _surfaceMaterial.SetFloat("_HeightFalloffExponent", Mathf.Clamp(config.HeightFalloffExponent, 0.25f, 8f));
            RecordSnapshotFloat(_surfaceMaterial, "_HeightFalloffExponent", Mathf.Clamp(config.HeightFalloffExponent, 0.25f, 8f));
            _surfaceMaterial.SetFloat("_HeightGain", Mathf.Clamp(config.HeightGain, 0f, 4f));
            RecordSnapshotFloat(_surfaceMaterial, "_HeightGain", Mathf.Clamp(config.HeightGain, 0f, 4f));
            _surfaceMaterial.SetFloat("_BackHeightLoss", Mathf.Clamp(config.BackHeightLoss, 0f, 4f));
            RecordSnapshotFloat(_surfaceMaterial, "_BackHeightLoss", Mathf.Clamp(config.BackHeightLoss, 0f, 4f));

            _surfaceMaterial.SetFloat(BackHeightFalloffExponentId, Mathf.Clamp(config.BackHeightFalloffExponent, 0.05f, 8f));
            RecordSnapshotFloat(_surfaceMaterial, BackHeightFalloffExponentId, Mathf.Clamp(config.BackHeightFalloffExponent, 0.05f, 8f));

            _surfaceMaterial.SetFloat(MagnetHeightStrengthCapId, Mathf.Clamp01(config.MagnetHeightStrengthCap));
            RecordSnapshotFloat(_surfaceMaterial, MagnetHeightStrengthCapId, Mathf.Clamp01(config.MagnetHeightStrengthCap));
            _surfaceMaterial.SetFloat("_MaximumHeight", Mathf.Clamp(config.MaximumHeight, 0.2f, 3f));
            RecordSnapshotFloat(_surfaceMaterial, "_MaximumHeight", Mathf.Clamp(config.MaximumHeight, 0.2f, 3f));
            _surfaceMaterial.SetFloat("_MaximumTiltRadians", Mathf.Clamp(config.MaximumTiltDegrees, 0f, 85f) * Mathf.Deg2Rad);
            RecordSnapshotFloat(_surfaceMaterial, "_MaximumTiltRadians", Mathf.Clamp(config.MaximumTiltDegrees, 0f, 85f) * Mathf.Deg2Rad);
            _surfaceMaterial.SetFloat("_TiltPeakRadiusMultiplier", Mathf.Clamp(config.TiltPeakRadiusMultiplier, 0.1f, 1.5f));
            RecordSnapshotFloat(_surfaceMaterial, "_TiltPeakRadiusMultiplier", Mathf.Clamp(config.TiltPeakRadiusMultiplier, 0.1f, 1.5f));
            _surfaceMaterial.SetFloat("_TiltReturnRadiusMultiplier", Mathf.Clamp(config.TiltReturnRadiusMultiplier, 0.2f, 3f));
            RecordSnapshotFloat(_surfaceMaterial, "_TiltReturnRadiusMultiplier", Mathf.Clamp(config.TiltReturnRadiusMultiplier, 0.2f, 3f));
            _surfaceMaterial.SetFloat("_TiltRiseExponent", Mathf.Clamp(config.TiltRiseExponent, 0.25f, 6f));
            RecordSnapshotFloat(_surfaceMaterial, "_TiltRiseExponent", Mathf.Clamp(config.TiltRiseExponent, 0.25f, 6f));
            _surfaceMaterial.SetFloat("_TiltReturnExponent", Mathf.Clamp(config.TiltReturnExponent, 0.25f, 6f));
            RecordSnapshotFloat(_surfaceMaterial, "_TiltReturnExponent", Mathf.Clamp(config.TiltReturnExponent, 0.25f, 6f));
            _surfaceMaterial.SetFloat("_InnerCornerFan", Mathf.Clamp01(config.InnerCornerFan));
            RecordSnapshotFloat(_surfaceMaterial, "_InnerCornerFan", Mathf.Clamp01(config.InnerCornerFan));
            _surfaceMaterial.SetFloat("_EmissionStrength", Mathf.Clamp(config.FerrofluidEmissionStrength, 0f, 0.2f));
            RecordSnapshotFloat(_surfaceMaterial, "_EmissionStrength", Mathf.Clamp(config.FerrofluidEmissionStrength, 0f, 0.2f));

            _surfaceMaterial.SetFloat("_Exposure", Mathf.Clamp(config.FerrofluidExposure, 0f, 2f));
            RecordSnapshotFloat(_surfaceMaterial, "_Exposure", Mathf.Clamp(config.FerrofluidExposure, 0f, 2f));
            _surfaceMaterial.SetFloat(ThinFilmScaleId, Mathf.Clamp(config.FerrofluidLutScale, 0f, 4f));
            RecordSnapshotFloat(_surfaceMaterial, ThinFilmScaleId, Mathf.Clamp(config.FerrofluidLutScale, 0f, 4f));
            _surfaceMaterial.SetFloat(ThinFilmOffsetId, Mathf.Clamp(config.FerrofluidLutOffset, -1f, 1f));
            RecordSnapshotFloat(_surfaceMaterial, ThinFilmOffsetId, Mathf.Clamp(config.FerrofluidLutOffset, -1f, 1f));

            if (_shape != FerrofluidSurfaceShape.Torus)
                BakePrimitiveHomes(densityLattice, surfaceRoundness, chartYawDegrees);

            ApplyEnabledState(config);
        }

        internal void DisableSpikes(Config config)
        {
            _spikesAllowed = false;
            ApplyEnabledState(config);
        }

        // Update visibility, spike springs, and lighting live while retaining the torus's other authored settings.
        internal void ApplyEnabledState(Config config)
        {
            _renderEnabled = _shape != FerrofluidSurfaceShape.Torus || ShouldRun(config.FerrofluidBodyEnabled);
            float spikesEnabled = _spikesAllowed && config.FerrofluidSpikesEnabled ? 1f : 0f;
            _surfaceMaterial.SetFloat("_SpikesEnabled", spikesEnabled);
            RecordSnapshotFloat(_surfaceMaterial, "_SpikesEnabled", spikesEnabled);
            _surfaceMaterial.SetFloat(SpikeSpringStrengthId, Mathf.Clamp(config.FerrofluidSpikeSpringStrength, 1f, 800f));
            RecordSnapshotFloat(_surfaceMaterial, SpikeSpringStrengthId, Mathf.Clamp(config.FerrofluidSpikeSpringStrength, 1f, 800f));
            _surfaceMaterial.SetFloat(SpikeSpringDampingId, Mathf.Clamp(config.FerrofluidSpikeSpringDamping, 0f, 80f));
            RecordSnapshotFloat(_surfaceMaterial, SpikeSpringDampingId, Mathf.Clamp(config.FerrofluidSpikeSpringDamping, 0f, 80f));
            _surfaceMaterial.SetFloat(ReflectivityId, Mathf.Clamp01(config.FerrofluidReflectivity));
            RecordSnapshotFloat(_surfaceMaterial, ReflectivityId, Mathf.Clamp01(config.FerrofluidReflectivity));
            Color baseColor = new Color(
                Mathf.Clamp01(config.FerrofluidBaseRed),
                Mathf.Clamp01(config.FerrofluidBaseGreen),
                Mathf.Clamp01(config.FerrofluidBaseBlue),
                1f).linear;
            _surfaceMaterial.SetVector(BaseColorId, baseColor);
            RecordSnapshotVector(_surfaceMaterial, BaseColorId, baseColor);
            Vector4 strengths = new(
                Mathf.Clamp(config.FerrofluidRoomReflectionStrength, 0f, 4f),
                Mathf.Clamp(config.FerrofluidFeetReflectionStrength, 0f, 4f),
                Mathf.Clamp(config.FerrofluidParabolaReflectionStrength, 0f, 8f),
                Mathf.Clamp01(config.FerrofluidParabolaReflectionSaturation));
            _surfaceMaterial.SetVector(ReflectionStrengthsId, strengths);
            RecordSnapshotVector(_surfaceMaterial, ReflectionStrengthsId, strengths);
            _surfaceMaterial.SetFloat(ThinFilmStrengthId, Mathf.Clamp01(config.FerrofluidThinFilmStrength));
            RecordSnapshotFloat(_surfaceMaterial, ThinFilmStrengthId, Mathf.Clamp01(config.FerrofluidThinFilmStrength));
            _surfaceMaterial.SetFloat(ThinFilmAngleRateId, Mathf.Clamp(config.FerrofluidThinFilmAngleRate, 0f, 4f));
            RecordSnapshotFloat(_surfaceMaterial, ThinFilmAngleRateId, Mathf.Clamp(config.FerrofluidThinFilmAngleRate, 0f, 4f));

            _renderer.enabled = _renderEnabled;
            ConfigureSurfaceNormals(_shape != FerrofluidSurfaceShape.Torus && spikesEnabled > 0f);
        }

        internal void SetFadeAlpha(float fadeAlpha)
        {
            if (fadeAlpha == _lastFadeAlpha)
            {
                return;
            }

            _lastFadeAlpha = fadeAlpha;
            _surfaceMaterial.SetFloat(FadeAlphaId, fadeAlpha);
            RecordSnapshotFloat(_surfaceMaterial, FadeAlphaId, fadeAlpha);
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
            float handStrength,
            float heightAmplification = 1f)
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
            RecordSnapshotVector(_simulationMaterial, PointerPositionId, pointerPosition);
            _simulationMaterial.SetFloat(PointerStrengthId, focusedStrength);
            RecordSnapshotFloat(_simulationMaterial, PointerStrengthId, focusedStrength);
            _surfaceMaterial.SetVector(MagneticFieldPositionId, magneticFieldPosition);
            RecordSnapshotVector(_surfaceMaterial, MagneticFieldPositionId, magneticFieldPosition);
            _surfaceMaterial.SetFloat(MagneticFieldStrengthId, focusedStrength);
            RecordSnapshotFloat(_surfaceMaterial, MagneticFieldStrengthId, focusedStrength);
            _surfaceMaterial.SetVector(MagnetOpeningPositionId, openingPointerPosition);
            RecordSnapshotVector(_surfaceMaterial, MagnetOpeningPositionId, openingPointerPosition);
            _surfaceMaterial.SetVector(MagnetOpeningNormalId, openingPlaneNormal);
            RecordSnapshotVector(_surfaceMaterial, MagnetOpeningNormalId, openingPlaneNormal);
            _surfaceMaterial.SetVector(MagnetOpeningDirectionId, openingPointerDirection);
            RecordSnapshotVector(_surfaceMaterial, MagnetOpeningDirectionId, openingPointerDirection);
            _surfaceMaterial.SetFloat(MagnetOpeningStrengthId, openingStrength);
            RecordSnapshotFloat(_surfaceMaterial, MagnetOpeningStrengthId, openingStrength);
            _surfaceMaterial.SetFloat(MagnetHeightAmplificationId, heightAmplification);
            RecordSnapshotFloat(_surfaceMaterial, MagnetHeightAmplificationId, heightAmplification);
            ApplySecondaryMagnet(handPosition, handPointer, handStrength);
            AdvanceSimulation();
        }

        internal void UpdatePrimitiveFrame(
            float sourceWorldScale,
            Vector3 magneticFieldPosition,
            Vector3 pointerSurfacePosition,
            float magneticFieldStrength,
            Vector3 handPosition,
            float handStrength,
            Vector3 bodyVelocity = default,
            Vector3 bodyAngularVelocity = default,
            float heightAmplification = 1f)
        {
            if (!_renderEnabled)
            {
                return;
            }

            SetPrimitiveMotion(bodyVelocity, bodyAngularVelocity);
            _surfaceMaterial.SetFloat(MagnetHeightAmplificationId, heightAmplification);
            RecordSnapshotFloat(_surfaceMaterial, MagnetHeightAmplificationId, heightAmplification);
            ApplyWorldScaleParameters(sourceWorldScale);
            _simulationMaterial.SetFloat(PointerStrengthId, magneticFieldStrength);
            RecordSnapshotFloat(_simulationMaterial, PointerStrengthId, magneticFieldStrength);
            _surfaceMaterial.SetVector(MagneticFieldPositionId, magneticFieldPosition);
            RecordSnapshotVector(_surfaceMaterial, MagneticFieldPositionId, magneticFieldPosition);
            _surfaceMaterial.SetFloat(MagneticFieldStrengthId, magneticFieldStrength);
            RecordSnapshotFloat(_surfaceMaterial, MagneticFieldStrengthId, magneticFieldStrength);
            ApplySecondaryMagnet(handPosition, Vector2.zero, handStrength);

            _simulationMaterial.SetVector(PrimitiveMagnetPositionId, _renderer.transform.InverseTransformPoint(pointerSurfacePosition));
            RecordSnapshotVector(_simulationMaterial, PrimitiveMagnetPositionId, _renderer.transform.InverseTransformPoint(pointerSurfacePosition));
            _simulationMaterial.SetVector(PrimitiveHandPositionId, _renderer.transform.InverseTransformPoint(handPosition));
            RecordSnapshotVector(_simulationMaterial, PrimitiveHandPositionId, _renderer.transform.InverseTransformPoint(handPosition));
            _surfaceMaterial.SetVector(MagnetOpeningPositionId, magneticFieldPosition);
            RecordSnapshotVector(_surfaceMaterial, MagnetOpeningPositionId, magneticFieldPosition);
            _surfaceMaterial.SetVector(MagnetOpeningNormalId, Vector3.up);
            RecordSnapshotVector(_surfaceMaterial, MagnetOpeningNormalId, Vector3.up);
            _surfaceMaterial.SetVector(MagnetOpeningDirectionId, Vector3.zero);
            RecordSnapshotVector(_surfaceMaterial, MagnetOpeningDirectionId, Vector3.zero);
            _surfaceMaterial.SetFloat(MagnetOpeningStrengthId, 0f);
            RecordSnapshotFloat(_surfaceMaterial, MagnetOpeningStrengthId, 0f);
            AdvanceSimulation();
            RenderPrimitiveDeformation();
            RenderSurfaceNormals();
        }

        private void ApplySecondaryMagnet(Vector3 position, Vector2 pointer, float strength)
        {
            _surfaceMaterial.SetVector(SecondaryMagneticFieldPositionId, position);
            RecordSnapshotVector(_surfaceMaterial, SecondaryMagneticFieldPositionId, position);
            _surfaceMaterial.SetFloat(SecondaryMagneticFieldStrengthId, strength);
            RecordSnapshotFloat(_surfaceMaterial, SecondaryMagneticFieldStrengthId, strength);
            _simulationMaterial.SetVector(SecondaryPointerPositionId, pointer);
            RecordSnapshotVector(_simulationMaterial, SecondaryPointerPositionId, pointer);
            _simulationMaterial.SetFloat(SecondaryMagneticFieldStrengthId, strength);
            RecordSnapshotFloat(_simulationMaterial, SecondaryMagneticFieldStrengthId, strength);
        }

        private void AdvanceSimulation()
        {
            float now = Time.unscaledTime;
            float elapsed = Mathf.Max(0f, now - _lastSimulationAt);
            _lastSimulationAt = now;
            AdvancePrimitiveSimulation(elapsed);
        }

        // Every substep uploads its own interpolated momentum slice immediately before Simulate so
        // the impulse spreads across the measured elapsed time instead of one repeated uniform.
        internal void AdvancePrimitiveSimulation(float elapsedSeconds)
        {
            // The spring blit uses a fullscreen quad, so pass the ferrofluid renderer's world matrix explicitly.
            _surfaceMaterial.SetMatrix(EffectObjectToWorldId, _renderer.transform.localToWorldMatrix);
            RecordSnapshotMatrix(_surfaceMaterial, EffectObjectToWorldId, _renderer.transform.localToWorldMatrix);
            _surfaceMaterial.SetTexture(HeightMapTextureId, _heightMap);
            RecordSnapshotTexture(_surfaceMaterial, HeightMapTextureId, _heightMap);
            int stepCount = Mathf.Clamp(_config.SimulationSteps, 1, 7);
            bool primitive = _shape != FerrofluidSurfaceShape.Torus;
            (int tickCount, float tickSeconds, float remainder) = FerrofluidSimulationTiming.CalculateStepSchedule(
                _simulationAccumulator,
                elapsedSeconds,
                stepCount,
                primitive);
            _simulationAccumulator = remainder;
            int totalSteps = tickCount * stepCount;
            if (totalSteps == 0)
            {
                return;
            }

            Transform rendererTransform = _renderer.transform;
            Quaternion rotationNow = rendererTransform.rotation;
            float uniformScale = rendererTransform.lossyScale.x;
            int globalStep = 0;
            for (int tick = 0; tick < tickCount; tick++)
            {
                for (int step = 0; step < stepCount; step++)
                {
                    if (primitive)
                    {
                        Quaternion previous = Quaternion.Slerp(_previousPrimitiveRotation, rotationNow, (float)globalStep / totalSteps);
                        Quaternion next = Quaternion.Slerp(_previousPrimitiveRotation, rotationNow, (globalStep + 1f) / totalSteps);
                        Vector3 previousLinear = Vector3.Lerp(_previousPrimitiveLinearVelocity, _pendingPrimitiveLinearVelocity, (float)globalStep / totalSteps);
                        Vector3 nextLinear = Vector3.Lerp(_previousPrimitiveLinearVelocity, _pendingPrimitiveLinearVelocity, (globalStep + 1f) / totalSteps);
                        Vector3 previousAngular = Vector3.Lerp(_previousPrimitiveAngularVelocity, _pendingPrimitiveAngularVelocity, (float)globalStep / totalSteps);
                        Vector3 nextAngular = Vector3.Lerp(_previousPrimitiveAngularVelocity, _pendingPrimitiveAngularVelocity, (globalStep + 1f) / totalSteps);
                        Quaternion inversePrevious = Quaternion.Inverse(previous);
                        Quaternion inverseNext = Quaternion.Inverse(next);
                        _simulationMaterial.SetMatrix(PrimitiveFrameRotationId, Matrix4x4.Rotate(inverseNext * previous));
                        RecordSnapshotMatrix(_simulationMaterial, PrimitiveFrameRotationId, Matrix4x4.Rotate(inverseNext * previous));
                        _simulationMaterial.SetVector(
                            PrimitiveLinearVelocityDeltaId,
                            inverseNext * ((previousLinear - nextLinear) / uniformScale));
                        RecordSnapshotVector(
                            _simulationMaterial,
                            PrimitiveLinearVelocityDeltaId,
                            inverseNext * ((previousLinear - nextLinear) / uniformScale));
                        _simulationMaterial.SetVector(PrimitivePreviousAngularVelocityId, inversePrevious * previousAngular);
                        RecordSnapshotVector(_simulationMaterial, PrimitivePreviousAngularVelocityId, inversePrevious * previousAngular);
                        _simulationMaterial.SetVector(PrimitiveAngularVelocityId, inverseNext * nextAngular);
                        RecordSnapshotVector(_simulationMaterial, PrimitiveAngularVelocityId, inverseNext * nextAngular);
                        Simulate(1000f * (tickSeconds / stepCount));
                    }
                    else
                    {
                        Simulate(SimulationDeltaTimeMilliseconds);
                    }

                    globalStep++;
                }

                RenderHeightMap(primitive ? 1000f * tickSeconds : SimulationDeltaTimeMilliseconds);
            }

            if (primitive)
            {
                _previousPrimitiveRotation = rotationNow;
                _previousPrimitiveLinearVelocity = _pendingPrimitiveLinearVelocity;
                _previousPrimitiveAngularVelocity = _pendingPrimitiveAngularVelocity;
            }
        }

        // The first sample only seeds the history so a fresh lattice or first body input does not
        // fire a false impulse; every later sample is the pending endpoint for the next advance.
        internal void SetPrimitiveMotion(Vector3 bodyVelocity, Vector3 bodyAngularVelocity)
        {
            if (!_primitiveHistoryValid)
            {
                _previousPrimitiveRotation = _renderer.transform.rotation;
                _previousPrimitiveLinearVelocity = bodyVelocity;
                _previousPrimitiveAngularVelocity = bodyAngularVelocity;
                _primitiveHistoryValid = true;
            }

            _pendingPrimitiveLinearVelocity = bodyVelocity;
            _pendingPrimitiveAngularVelocity = bodyAngularVelocity;
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
            ReleaseSurfaceNormals();
            UnityEngine.Object.Destroy(_surfaceMaterial);
            UnityEngine.Object.Destroy(_surfaceMesh);
        }

        private void ApplyStaticSimulationParameters()
        {
            _simulationMaterial.SetInt(ParticleCountId, _particleCount);
            RecordSnapshotInt(_simulationMaterial, ParticleCountId, _particleCount);

            _surfaceMaterial.SetInt(ParticleCountId, _particleCount);
            RecordSnapshotInt(_surfaceMaterial, ParticleCountId, _particleCount);
            _simulationMaterial.SetVector(DomainScaleId, EvaluateDomainScale(_shape, _fieldColumns));
            RecordSnapshotVector(_simulationMaterial, DomainScaleId, EvaluateDomainScale(_shape, _fieldColumns));
            _simulationMaterial.SetFloat(HeightFactorId, HeightFactorValue);
            RecordSnapshotFloat(_simulationMaterial, HeightFactorId, HeightFactorValue);
            _simulationMaterial.SetFloat("_HeightMapScale", HeightMapScaleFactor);
            RecordSnapshotFloat(_simulationMaterial, "_HeightMapScale", HeightMapScaleFactor);
            _simulationMaterial.SetFloat("_SmoothFactor", HeightMapSmoothFactor);
            RecordSnapshotFloat(_simulationMaterial, "_SmoothFactor", HeightMapSmoothFactor);
            _surfaceMaterial.SetFloat(HeightFactorId, HeightFactorValue);
            RecordSnapshotFloat(_surfaceMaterial, HeightFactorId, HeightFactorValue);
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
            RecordSnapshotVector(_simulationMaterial, PerimeterSegmentsId, perimeterSegments);
            _simulationMaterial.SetFloat(CornerRadiusId, geometry.CornerRadius);
            RecordSnapshotFloat(_simulationMaterial, CornerRadiusId, geometry.CornerRadius);
            _simulationMaterial.SetFloat(RadialRadiusId, RadialRadius);
            RecordSnapshotFloat(_simulationMaterial, RadialRadiusId, RadialRadius);
            _surfaceMaterial.SetVector(RectCenterId, rect.center);
            RecordSnapshotVector(_surfaceMaterial, RectCenterId, rect.center);
            _surfaceMaterial.SetVector(RectSizeId, rect.size);
            RecordSnapshotVector(_surfaceMaterial, RectSizeId, rect.size);
            _surfaceMaterial.SetFloat(RectMinYId, rect.yMin);
            RecordSnapshotFloat(_surfaceMaterial, RectMinYId, rect.yMin);
            _surfaceMaterial.SetFloat(PivotYId, pivot.y);
            RecordSnapshotFloat(_surfaceMaterial, PivotYId, pivot.y);
            _surfaceMaterial.SetFloat(SkewId, skew);
            RecordSnapshotFloat(_surfaceMaterial, SkewId, skew);
            _surfaceMaterial.SetVector(HalfExtentsId, geometry.HalfExtents);
            RecordSnapshotVector(_surfaceMaterial, HalfExtentsId, geometry.HalfExtents);
            _surfaceMaterial.SetFloat(CornerRadiusId, geometry.CornerRadius);
            RecordSnapshotFloat(_surfaceMaterial, CornerRadiusId, geometry.CornerRadius);
            _surfaceMaterial.SetFloat(RadialRadiusId, RadialRadius);
            RecordSnapshotFloat(_surfaceMaterial, RadialRadiusId, RadialRadius);
            _surfaceMaterial.SetFloat(DepthRadiusId, rect.width / 6f);
            RecordSnapshotFloat(_surfaceMaterial, DepthRadiusId, rect.width / 6f);
            _surfaceMaterial.SetFloat(ViewerLocalZSignId, viewerLocalZSign);
            RecordSnapshotFloat(_surfaceMaterial, ViewerLocalZSignId, viewerLocalZSign);
            _surfaceMaterial.SetFloat(RadialCenterId, RadialCenter);
            RecordSnapshotFloat(_surfaceMaterial, RadialCenterId, RadialCenter);
            _surfaceMaterial.SetFloat(SourceLocalScaleId, RadialRadius / SourceRadialRadius);
            RecordSnapshotFloat(_surfaceMaterial, SourceLocalScaleId, RadialRadius / SourceRadialRadius);
            _surfaceMaterial.SetMatrix(LocalToCurvedCanvasId, localToCurvedCanvas);
            RecordSnapshotMatrix(_surfaceMaterial, LocalToCurvedCanvasId, localToCurvedCanvas);
            _surfaceMaterial.SetMatrix(CurvedCanvasToLocalId, curvedCanvasToLocal);
            RecordSnapshotMatrix(_surfaceMaterial, CurvedCanvasToLocalId, curvedCanvasToLocal);
            _surfaceMaterial.SetFloat(CurvedCanvasRadiusId, curvedCanvasRadius);
            RecordSnapshotFloat(_surfaceMaterial, CurvedCanvasRadiusId, curvedCanvasRadius);
            _surfaceMaterial.SetFloat(HasCurvedCanvasId, hasCurvedCanvas ? 1f : 0f);
            RecordSnapshotFloat(_surfaceMaterial, HasCurvedCanvasId, hasCurvedCanvas ? 1f : 0f);
            ApplyWorldScaleParameters(sourceWorldScale);
        }

        private void ApplyWorldScaleParameters(float sourceWorldScale)
        {
            _absSourceWorldScale = Mathf.Abs(sourceWorldScale);
            _surfaceMaterial.SetFloat("_BulgeRadius", Mathf.Clamp(_config.BulgeRadius, 0.1f, 3.2f) * sourceWorldScale);
            RecordSnapshotFloat(_surfaceMaterial, "_BulgeRadius", Mathf.Clamp(_config.BulgeRadius, 0.1f, 3.2f) * sourceWorldScale);
            _surfaceMaterial.SetFloat(PrimitiveSpikeHeightRadiusId, Mathf.Clamp(_config.FerrofluidSpikeHeightRadius, 0.1f, 3.2f) * sourceWorldScale);
            RecordSnapshotFloat(_surfaceMaterial, PrimitiveSpikeHeightRadiusId, Mathf.Clamp(_config.FerrofluidSpikeHeightRadius, 0.1f, 3.2f) * sourceWorldScale);
            _surfaceMaterial.SetFloat(PrimitiveSpikeTiltRadiusId, Mathf.Clamp(_config.FerrofluidSpikeTiltRadius, 0.1f, 3.2f) * sourceWorldScale);
            RecordSnapshotFloat(_surfaceMaterial, PrimitiveSpikeTiltRadiusId, Mathf.Clamp(_config.FerrofluidSpikeTiltRadius, 0.1f, 3.2f) * sourceWorldScale);
            _surfaceMaterial.SetFloat(MagnetHeightDeadZoneRadiusId, Mathf.Clamp(_config.MagnetHeightDeadZoneRadius, 0f, 0.5f) * sourceWorldScale);
            RecordSnapshotFloat(_surfaceMaterial, MagnetHeightDeadZoneRadiusId, Mathf.Clamp(_config.MagnetHeightDeadZoneRadius, 0f, 0.5f) * sourceWorldScale);
            float bulgeHeight = EvaluateBulgeHeight(_shape, _config.BulgeHeight, sourceWorldScale);
            _surfaceMaterial.SetFloat("_BulgeHeight", bulgeHeight);
            RecordSnapshotFloat(_surfaceMaterial, "_BulgeHeight", bulgeHeight);

            _surfaceMaterial.SetFloat("_PeakWidth", Mathf.Clamp(_config.PeakWidth, 0.03f, 1f) * sourceWorldScale);
            RecordSnapshotFloat(_surfaceMaterial, "_PeakWidth", Mathf.Clamp(_config.PeakWidth, 0.03f, 1f) * sourceWorldScale);
            _surfaceMaterial.SetFloat("_FluidHeightScale", SourceFluidHeightScale * _spikeHeightScale * sourceWorldScale);
            RecordSnapshotFloat(_surfaceMaterial, "_FluidHeightScale", SourceFluidHeightScale * _spikeHeightScale * sourceWorldScale);
        }

        private void Simulate(float deltaTimeMilliseconds)
        {
            AdvanceParticles(deltaTimeMilliseconds, 2);
        }

        private void CalculateParticleForces()
        {
            _simulationMaterial.SetTexture(StateTextureId, _currentState);
            RecordSnapshotTexture(_simulationMaterial, StateTextureId, _currentState);
            if (_siteSurface != null)
            {
                // Pressure needs the current physical positions, not the previous presentation frame.
                Graphics.Blit(Texture2D.blackTexture, _siteSurface, _simulationMaterial, 4);
                _simulationMaterial.SetTexture(SiteSurfaceTextureId, _siteSurface);
                RecordSnapshotTexture(_simulationMaterial, SiteSurfaceTextureId, _siteSurface);
            }

            Graphics.Blit(Texture2D.blackTexture, _densityPressure, _simulationMaterial, 0);
            _simulationMaterial.SetTexture(DensityPressureTextureId, _densityPressure);
            RecordSnapshotTexture(_simulationMaterial, DensityPressureTextureId, _densityPressure);
            Graphics.Blit(Texture2D.blackTexture, _force, _simulationMaterial, 1);
        }

        private void AdvanceParticles(float deltaTimeMilliseconds, int integrationPass)
        {
            CalculateParticleForces();
            _simulationMaterial.SetTexture(RestPositionTextureId, _restPositionTexture);
            RecordSnapshotTexture(_simulationMaterial, RestPositionTextureId, _restPositionTexture);
            _simulationMaterial.SetTexture(ForceTextureId, _force);
            RecordSnapshotTexture(_simulationMaterial, ForceTextureId, _force);
            _simulationMaterial.SetFloat(DeltaTimeMillisecondsId, deltaTimeMilliseconds);
            RecordSnapshotFloat(_simulationMaterial, DeltaTimeMillisecondsId, deltaTimeMilliseconds);
            Graphics.Blit(Texture2D.blackTexture, _nextState, _simulationMaterial, integrationPass);
            RenderTexture previous = _currentState;
            _currentState = _nextState;
            _nextState = previous;
        }

        private void RenderHeightMap(float deltaTimeMilliseconds)
        {
            _simulationMaterial.SetTexture(StateTextureId, _currentState);
            RecordSnapshotTexture(_simulationMaterial, StateTextureId, _currentState);

            _surfaceMaterial.SetTexture(StateTextureId, _currentState);
            RecordSnapshotTexture(_surfaceMaterial, StateTextureId, _currentState);
            _surfaceMaterial.SetTexture(SiteSurfaceTextureId, _siteSurface);
            RecordSnapshotTexture(_surfaceMaterial, SiteSurfaceTextureId, _siteSurface);
            if (_siteSurface != null)
            {
                // Only site positions move. Rest spacing was cached when the settings changed.
                Graphics.Blit(Texture2D.blackTexture, _siteSurface, _simulationMaterial, 4);
                _simulationMaterial.SetTexture(SiteSurfaceTextureId, _siteSurface);
                RecordSnapshotTexture(_simulationMaterial, SiteSurfaceTextureId, _siteSurface);
            }

            if (_shape != FerrofluidSurfaceShape.Torus)
                Graphics.Blit(Texture2D.blackTexture, _siteBulge, _surfaceMaterial, 3);

            Graphics.Blit(Texture2D.blackTexture, _coarseCandidates, _simulationMaterial, 5);
            Graphics.Blit(Texture2D.blackTexture, _heightMap, _simulationMaterial, 3);

            _surfaceMaterial.SetFloat(DeltaTimeMillisecondsId, deltaTimeMilliseconds);
            RecordSnapshotFloat(_surfaceMaterial, DeltaTimeMillisecondsId, deltaTimeMilliseconds);
            _surfaceMaterial.SetTexture(SpringStateTextureId, _springCurrent);
            RecordSnapshotTexture(_surfaceMaterial, SpringStateTextureId, _springCurrent);
            Graphics.Blit(Texture2D.blackTexture, _springNext, _surfaceMaterial, 1);
            RenderTexture previousSpring = _springCurrent;
            _springCurrent = _springNext;
            _springNext = previousSpring;
            _surfaceMaterial.SetTexture(SpringStateTextureId, _springCurrent);
            RecordSnapshotTexture(_surfaceMaterial, SpringStateTextureId, _springCurrent);
        }

        private void CreatePrimitiveDeformationTextures()
        {
            _siteDeformation = CreateRenderTexture("ChromaGLS Ferrofluid Site Deformation", _particleTextureSize, FilterMode.Point, TextureWrapMode.Clamp);
            _siteBulge = CreateRenderTexture("ChromaGLS Ferrofluid Site Bulge", _particleTextureSize, FilterMode.Point, TextureWrapMode.Clamp);
            _surfaceMaterial.SetTexture(SiteDeformationTextureId, _siteDeformation);
            RecordSnapshotTexture(_surfaceMaterial, SiteDeformationTextureId, _siteDeformation);
            _surfaceMaterial.SetTexture(SiteBulgeTextureId, _siteBulge);
            RecordSnapshotTexture(_surfaceMaterial, SiteBulgeTextureId, _siteBulge);
            _simulationMaterial.SetTexture(SiteBulgeTextureId, _siteBulge);
            RecordSnapshotTexture(_simulationMaterial, SiteBulgeTextureId, _siteBulge);
        }

        private void RenderPrimitiveDeformation()
        {
            // Magnet positions change between simulation ticks, so refresh the shared site responses every presentation frame.
            Graphics.Blit(Texture2D.blackTexture, _siteDeformation, _surfaceMaterial, 2);
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

        private void BakePrimitiveHomes(Vector2Int lattice, float roundness, float chartYaw)
        {
            Vector4 pressure = new(
                _simulationMaterial.GetFloat("_Mass"),
                _simulationMaterial.GetFloat("_RestDensity"),
                _simulationMaterial.GetFloat("_GasConstant"),
                _simulationMaterial.GetFloat("_Viscosity"));
            var key = (_shape, lattice.x * lattice.y, roundness, pressure, _simulationMaterial.GetFloat("_Damping"));
            bool sameBake = _homeBakeKey.HasValue && _homeBakeKey.Value.Equals(key);
            if (sameBake && _homeChartYaw == chartYaw)
                return;

            if (!sameBake)
            {
                _fieldColumns = lattice.x;
                _fieldRows = lattice.y;
                _particleCount = lattice.x * lattice.y;
                _simulationMaterial.SetVector(DomainScaleId, EvaluateDomainScale(_shape, _fieldColumns));
                RecordSnapshotVector(_simulationMaterial, DomainScaleId, EvaluateDomainScale(_shape, _fieldColumns));
                EnsureParticleTextureCapacity();
                _simulationMaterial.SetInt(ParticleCountId, _particleCount);
                RecordSnapshotInt(_simulationMaterial, ParticleCountId, _particleCount);
                _surfaceMaterial.SetInt(ParticleCountId, _particleCount);
                RecordSnapshotInt(_surfaceMaterial, ParticleCountId, _particleCount);
                if (!PrimitiveHomeCache.TryGetValue(key, out (Vector3[] Positions, float[] Densities) cached))
                {
                    FerrofluidPrimitiveLayout.Layout layout = FerrofluidPrimitiveLayout.Get(_shape, _particleCount, roundness);

                    // Settle in one canonical chart, then encode the frozen physical result for the render chart.
                    _simulationMaterial.SetVector(PrimitiveYawCosSinId, new Vector2(1f, 0f));
                    RecordSnapshotVector(_simulationMaterial, PrimitiveYawCosSinId, new Vector2(1f, 0f));
                    InstallPrimitivePositions(layout.Positions, 0f);
                    RefreshRestSurface(layout.Positions);
                    _simulationMaterial.SetInt("_PrimitiveBakePinnedCount", layout.PinnedCount);
                    RecordSnapshotInt(_simulationMaterial, "_PrimitiveBakePinnedCount", layout.PinnedCount);
                    for (int iteration = 0; iteration < 64; iteration++)
                        AdvanceParticles(SimulationDeltaTimeMilliseconds, 7);

                    Vector3[] homes = ReadSettledPrimitiveHomes(roundness);

                    // Store the settled density with each immutable home so sparse and crowded regions use the same reference.
                    _simulationMaterial.SetTexture(RestPositionTextureId, _currentState);
                    RecordSnapshotTexture(_simulationMaterial, RestPositionTextureId, _currentState);
                    RefreshRestSurface(homes);
                    _simulationMaterial.SetTexture(RestPositionTextureId, _restPositionTexture);
                    RecordSnapshotTexture(_simulationMaterial, RestPositionTextureId, _restPositionTexture);
                    _simulationMaterial.SetTexture(StateTextureId, _currentState);
                    RecordSnapshotTexture(_simulationMaterial, StateTextureId, _currentState);
                    Graphics.Blit(Texture2D.blackTexture, _siteSurface, _simulationMaterial, 4);
                    _simulationMaterial.SetTexture(SiteSurfaceTextureId, _siteSurface);
                    RecordSnapshotTexture(_simulationMaterial, SiteSurfaceTextureId, _siteSurface);
                    Graphics.Blit(Texture2D.blackTexture, _densityPressure, _simulationMaterial, 0);
                    Color[] densityPixels = ReadParticlePixels(_densityPressure);
                    float[] densities = new float[_particleCount];
                    for (int index = 0; index < _particleCount; index++)
                        densities[index] = densityPixels[index].r;

                    cached = (homes, densities);
                    if (PrimitiveHomeCache.Count >= 24)
                        PrimitiveHomeCache.Clear();

                    PrimitiveHomeCache.Add(key, cached);
                }

                _bakedPrimitiveHomes = cached.Positions;
                _bakedPrimitiveDensities = cached.Densities;

                _homeBakeKey = key;
            }

            float radians = chartYaw * Mathf.Deg2Rad;
            _simulationMaterial.SetVector(PrimitiveYawCosSinId, new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)));
            RecordSnapshotVector(_simulationMaterial, PrimitiveYawCosSinId, new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)));
            InstallPrimitivePositions(_bakedPrimitiveHomes!, chartYaw, _bakedPrimitiveDensities);
            RefreshRestSurface(_bakedPrimitiveHomes!);
            _homeChartYaw = chartYaw;
            _lastSimulationAt = Time.unscaledTime;
            _simulationAccumulator = 0f;
            _primitiveHistoryValid = false;
        }

        private void InstallPrimitivePositions(Vector3[] positions, float chartYaw, float[]? densities = null)
        {
            Texture2D previous = _restPositionTexture;
            _primitiveRestPositions = EncodePrimitivePositions(positions, chartYaw);
            _restPositionTexture = new Texture2D(_particleTextureSize, _particleTextureSize, TextureFormat.RGBAFloat, false, true)
            {
                name = "ChromaGLS Ferrofluid Baked Homes",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.DontSave,
            };
            Color[] pixels = new Color[_particleTextureSize * _particleTextureSize];
            for (int index = 0; index < _primitiveRestPositions.Length; index++)
            {
                Vector2 position = _primitiveRestPositions[index];
                float density = densities?[index] ?? 0f;
                pixels[index] = new Color(position.x, position.y, density, 0f);
            }

            _restPositionTexture.SetPixels(pixels);
            _restPositionTexture.Apply(false, true);
            _simulationMaterial.SetTexture(RestPositionTextureId, _restPositionTexture);
            RecordSnapshotTexture(_simulationMaterial, RestPositionTextureId, _restPositionTexture);
            float restDensityReady = densities != null
                ? 1f
                : 0f;
            _simulationMaterial.SetFloat(PrimitiveRestDensityReadyId, restDensityReady);
            RecordSnapshotFloat(_simulationMaterial, PrimitiveRestDensityReadyId, restDensityReady);
            _surfaceMaterial.SetTexture(RestPositionTextureId, _restPositionTexture);
            RecordSnapshotTexture(_surfaceMaterial, RestPositionTextureId, _restPositionTexture);
            Graphics.Blit(Texture2D.blackTexture, _stateA, _simulationMaterial, 9);
            Graphics.Blit(Texture2D.blackTexture, _stateB, _simulationMaterial, 9);
            _currentState = _stateA;
            _nextState = _stateB;
            UnityEngine.Object.Destroy(previous);
        }

        private void RefreshRestSurface(Vector3[] positions)
        {
            if (_shape == FerrofluidSurfaceShape.RoundedCube)
            {
                _simulationMaterial.SetFloat(PrimitiveSpikeSpacingId, FerrofluidEnvironmentObjects.EvaluateCubeSpikeSpacing(positions));
                RecordSnapshotFloat(_simulationMaterial, PrimitiveSpikeSpacingId, FerrofluidEnvironmentObjects.EvaluateCubeSpikeSpacing(positions));
            }

            Graphics.Blit(Texture2D.blackTexture, _restSurface, _simulationMaterial, 6);
            _simulationMaterial.SetTexture(RestSurfaceTextureId, _restSurface);
            RecordSnapshotTexture(_simulationMaterial, RestSurfaceTextureId, _restSurface);
        }

        private Color[] ReadParticlePixels(RenderTexture source)
        {
            RenderTexture previous = RenderTexture.active;
            Texture2D readback = new(_particleTextureSize, _particleTextureSize, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = source;
                readback.ReadPixels(new Rect(0f, 0f, _particleTextureSize, _particleTextureSize), 0, 0);
                readback.Apply();
                return readback.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.Destroy(readback);
            }
        }

        private Vector3[] ReadSettledPrimitiveHomes(float roundness)
        {
            // Readback happens only when a new layout is baked, never in the simulation frame loop.
            Color[] pixels = ReadParticlePixels(_currentState);
            Vector3[] homes = new Vector3[_particleCount];
            for (int index = 0; index < homes.Length; index++)
            {
                homes[index] = FerrofluidEnvironmentObjects.EvaluateSiteSurfacePosition(
                    _shape,
                    new Vector2(pixels[index].r, pixels[index].g),
                    roundness,
                    0f);
            }

            return homes;
        }

        // Density changes replace the torus lattice; primitives also invalidate on geometry and pressure settings.
        private void ResetParticleField(int columns, int rows, float roundness)
        {
            Texture2D previousRest = _restPositionTexture;
            _fieldColumns = columns;
            _fieldRows = rows;
            _particleCount = columns * rows;

            _simulationMaterial.SetVector(DomainScaleId, EvaluateDomainScale(_shape, columns));
            RecordSnapshotVector(_simulationMaterial, DomainScaleId, EvaluateDomainScale(_shape, columns));
            EnsureParticleTextureCapacity();
            _restPositionTexture = CreateInitialPositionTexture(columns, rows, _shape, _particleTextureSize, roundness, out _primitiveRestPositions);
            Graphics.Blit(_restPositionTexture, _stateA);
            Graphics.Blit(_restPositionTexture, _stateB);
            _currentState = _stateA;
            _nextState = _stateB;
            _simulationMaterial.SetInt(ParticleCountId, _particleCount);
            RecordSnapshotInt(_simulationMaterial, ParticleCountId, _particleCount);
            _surfaceMaterial.SetInt(ParticleCountId, _particleCount);
            RecordSnapshotInt(_surfaceMaterial, ParticleCountId, _particleCount);

            // Discard time left over from the old lattice so the replacement starts at its initial state.
            _lastSimulationAt = Time.unscaledTime;
            _simulationAccumulator = 0f;
            _primitiveHistoryValid = false;
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
                _surfaceMaterial.SetTexture(DensityPressureTextureId, _densityPressure);
                RecordSnapshotTexture(_surfaceMaterial, DensityPressureTextureId, _densityPressure);
            }

            _simulationMaterial.SetInt(ParticleTextureSizeId, _particleTextureSize);
            RecordSnapshotInt(_simulationMaterial, ParticleTextureSizeId, _particleTextureSize);
            _surfaceMaterial.SetInt(ParticleTextureSizeId, _particleTextureSize);
            RecordSnapshotInt(_surfaceMaterial, ParticleTextureSizeId, _particleTextureSize);
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
