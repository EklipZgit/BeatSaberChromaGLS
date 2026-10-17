using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.XR.MockHMD;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace ChromaGLS.StereoTest
{
    // VR eye verification harness: rebuilds the requirement torus from the shipped ferrofluid bundle
    // and renders it under the MockHMD provider so a left-eye-only render shows up as an empty slice.
    public sealed class FerrofluidStereoCapture : MonoBehaviour
    {
        // Serialized so the runner's saved scene pulls these project shaders into the player build;
        // unreferenced shaders are stripped by BuildPlayer. ferrofluidSourceSurface exercises the
        // source shader (all variants compiled) instead of the shipped bundle when
        // -useSourceShader is passed.
        public Shader stereoControlShader;
        public Shader ferrofluidSourceSurface;

        private const int ParticleTextureSize = 32;
        private const int FieldColumns = 48;
        private const int FieldRows = 12;
        private const int HeightMapSize = 256;
        private const int CoarseCandidateMapSize = 16;
        private const float RadialCenter = 0.2f;
        private const float RadialRadius = 1.38f;
        private const float SourceRadialRadius = 0.085f;
        private const float HeightFactorValue = 0.38f;
        private const float HeightMapScaleFactor = 0.9f;
        private const float HeightMapSmoothFactor = 0.02f;

        private readonly StringBuilder _report = new();
        private Camera _xrCamera = null!;
        private Camera _monoCamera = null!;
        private Material _simulationMaterial = null!;
        private Material _surfaceMaterial = null!;
        private MeshRenderer _renderer = null!;
        private MeshRenderer _controlRenderer = null!;
        private RenderTexture _heightMap = null!;
        private RenderTexture _coarseCandidates = null!;
        private RenderTexture _stateA = null!;
        private RenderTexture _stateB = null!;
        private RenderTexture _densityPressure = null!;
        private RenderTexture _force = null!;
        private RenderTexture _springStateA = null!;
        private RenderTexture _springStateB = null!;
        private RenderTexture _currentState = null!;
        private RenderTexture _nextState = null!;
        private RenderTexture _springCurrent = null!;
        private RenderTexture _springNext = null!;
        private Texture2D _restPositionTexture = null!;
        private AssetBundle _bundle = null!;
        private Vector3 _magnetWorldPosition;
        private Vector2 _pointerPosition;
        private float _magnetStrength;
        private string _captureDir = string.Empty;
        private string _bundlePath = string.Empty;
        private int _frame;
        private bool _captured;
        private bool _useSourceShader;
        private bool _simulateMissingRightEye;
        private RenderTexture _stereoTarget = null!;

        private void Awake()
        {
            Application.runInBackground = true;

            // Player builds resolve their paths from command line args; editor play mode keeps the
            // project-relative defaults.
            _bundlePath = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "ChromaGLS", "Settings", "ferrofluid-windows.bundle"));
            _captureDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "StereoTestCaptures"));
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (arguments[i] == "-captureDir")
                {
                    _captureDir = Path.GetFullPath(arguments[i + 1]);
                }
                else if (arguments[i] == "-bundlePath")
                {
                    _bundlePath = Path.GetFullPath(arguments[i + 1]);
                }
            }

            _useSourceShader = Array.Exists(arguments, a => a == "-useSourceShader");
            _simulateMissingRightEye = Array.Exists(arguments, a => a == "-simulateMissingRightEye");
        }

        private void Start()
        {
            Directory.CreateDirectory(_captureDir);
            Report("=== Ferrofluid stereo capture start ===");
            Report($"Unity {Application.unityVersion} batch={Application.isBatchMode} api={SystemInfo.graphicsDeviceType} device={SystemInfo.graphicsDeviceName}");
            Report($"supportsInstancing={SystemInfo.supportsInstancing} rtArrayIndexFromVS={SystemInfo.supportsRenderTargetArrayIndexFromVertexShader}");

            try
            {
                BuildScene();
                WarmUpSimulation();
                Camera.onPreRender += TraceCameraRender;
                Camera.onPostRender += TraceCameraRender;
            }
            catch (Exception exception)
            {
                FailAndExit($"setup threw: {exception}");
            }
        }

        private void TraceCameraRender(Camera camera)
        {
            if (camera == _xrCamera && _frame >= 60)
            {
                Report($"XR camera render callback: stereo={camera.stereoEnabled} rect={camera.pixelRect} activeTarget={RenderTexture.active} display={camera.targetDisplay}");
            }
        }

        private void OnDestroy()
        {
            if (_stereoTarget != null)
            {
                _stereoTarget.Release();
                Destroy(_stereoTarget);
            }

            Camera.onPreRender -= TraceCameraRender;
            Camera.onPostRender -= TraceCameraRender;
        }

        private void Update()
        {
            _frame++;

            // Drive a couple of 120 Hz-equivalent sub-ticks so the spring map tracks the magnet like a live session.
            if (_frame < 600 && _frame % 2 == 0)
            {
                Tick(8f);
            }

            bool xrActive = XRSettings.isDeviceActive;
            if (_frame % 60 == 0)
            {
                Report($"frame={_frame} xrActive={xrActive} stereoMode={XRSettings.stereoRenderingMode}");
            }

            if (xrActive && _frame > 60 && !_captured)
            {
                _captured = true;
                CaptureAndExit();
            }

            if (_frame > 1200)
            {
                FailAndExit("XR device never became active / eye render targets never appeared.");
            }
        }

        // The torus sits in front of an identity-pose mock head at ~0.55 m, matching the game's
        // ~0.02 world scale on a 36x8 requirement rect (the same shape SongCore's BG button produces).
        private void BuildScene()
        {
            GameObject cameraGo = new("Stereo Camera");
            _xrCamera = cameraGo.AddComponent<Camera>();
            _xrCamera.tag = "MainCamera";
            _xrCamera.clearFlags = CameraClearFlags.SolidColor;
            _xrCamera.backgroundColor = new Color(0.012f, 0.018f, 0.028f, 1f);
            _xrCamera.nearClipPlane = 0.05f;
            _xrCamera.farClipPlane = 50f;
            _xrCamera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            GameObject monoGo = new("Mono Reference Camera");
            _monoCamera = monoGo.AddComponent<Camera>();
            _monoCamera.clearFlags = CameraClearFlags.SolidColor;
            _monoCamera.backgroundColor = _xrCamera.backgroundColor;
            _monoCamera.nearClipPlane = 0.05f;
            _monoCamera.farClipPlane = 50f;
            _monoCamera.fieldOfView = 90f;
            _monoCamera.stereoTargetEye = StereoTargetEyeMask.None;
            _monoCamera.enabled = false;
            _monoCamera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            if (!File.Exists(_bundlePath))
            {
                throw new FileNotFoundException("ferrofluid bundle missing", _bundlePath);
            }

            _bundle = AssetBundle.LoadFromFile(_bundlePath);
            Shader simulationShader = _bundle.LoadAsset<Shader>("assets/ferrofluidsimulation.shader");
            Shader surfaceShader = _useSourceShader ? ferrofluidSourceSurface : _bundle.LoadAsset<Shader>("assets/ferrofluidsurface.shader");
            if (_useSourceShader)
            {
                Report("using project-compiled source surface shader (not bundle)");
            }
            Texture2D thinFilm = _bundle.LoadAsset<Texture2D>("assets/generated/ferrolut.png");
            Texture2D environment = _bundle.LoadAsset<Texture2D>("assets/generated/env-map-01.jpg");
            if (simulationShader == null || surfaceShader == null || thinFilm == null || environment == null)
            {
                throw new InvalidOperationException("bundle loaded but an expected asset was missing.");
            }

            Report($"bundle={_bundlePath} size={new FileInfo(_bundlePath).Length}");
            Report($"shaders: sim='{simulationShader.name}' supported={simulationShader.isSupported} surface='{surfaceShader.name}' supported={surfaceShader.isSupported}");

            _simulationMaterial = new Material(simulationShader) { name = "StereoTest Sim", hideFlags = HideFlags.DontSave };
            _surfaceMaterial = new Material(surfaceShader) { name = "StereoTest Surface", hideFlags = HideFlags.DontSave };

            // Replicates the production surface material exactly — enableInstancing is the SPI fix under test.
            _surfaceMaterial.enableInstancing = true;
            _surfaceMaterial.SetTexture("_EnvironmentTex", environment);
            _surfaceMaterial.SetTexture("_ThinFilmTex", thinFilm);
            _surfaceMaterial.SetFloat("_SurfaceShapeMode", 0f);
            _surfaceMaterial.SetInt("_ParticleTextureSize", ParticleTextureSize);
            _surfaceMaterial.SetInt("_ParticleCount", FieldColumns * FieldRows);

            // The shader's source units include its radial conversion as well as the renderer's 0.02 world scale.
            const float sourceWorldScale = (0.02f * RadialRadius) / SourceRadialRadius;
            Rect rect = new(-18f, -4f, 36f, 8f);
            Vector2 halfExtents = new(
                Mathf.Max((rect.width * 0.5f) - 0.1f + (rect.height * 0.1f), 0.2f),
                Mathf.Max((rect.height * 0.5f) - 0.1f + (rect.height * 0.1f), 0.2f));
            float cornerRadius = Mathf.Min(
                halfExtents.y * (0.11f / 0.2625f),
                Mathf.Min(halfExtents.x, halfExtents.y) - 0.001f);
            Vector4 perimeterSegments = new(
                (halfExtents.x - cornerRadius) * 2f,
                cornerRadius * Mathf.PI * 0.5f,
                (halfExtents.y - cornerRadius) * 2f,
                (((halfExtents.x - cornerRadius) * 2f) + ((halfExtents.y - cornerRadius) * 2f) + (cornerRadius * Mathf.PI)) * 2f);

            _surfaceMaterial.SetVector("_RectCenter", rect.center);
            _surfaceMaterial.SetVector("_RectSize", rect.size);
            _surfaceMaterial.SetFloat("_RectMinY", rect.yMin);
            _surfaceMaterial.SetFloat("_PivotY", 0.5f);
            _surfaceMaterial.SetFloat("_Skew", 0f);
            _surfaceMaterial.SetVector("_HalfExtents", halfExtents);
            _surfaceMaterial.SetFloat("_CornerRadius", cornerRadius);
            _surfaceMaterial.SetFloat("_RadialRadius", RadialRadius);
            _surfaceMaterial.SetFloat("_DepthRadius", rect.width / 6f);
            _surfaceMaterial.SetFloat("_ViewerLocalZSign", -1f);
            _surfaceMaterial.SetFloat("_RadialCenter", RadialCenter);
            _surfaceMaterial.SetFloat("_SourceLocalScale", RadialRadius / SourceRadialRadius);
            _surfaceMaterial.SetMatrix("_LocalToCurvedCanvas", Matrix4x4.identity);
            _surfaceMaterial.SetMatrix("_CurvedCanvasToLocal", Matrix4x4.identity);
            _surfaceMaterial.SetFloat("_CurvedCanvasRadius", 140f);
            _surfaceMaterial.SetFloat("_HasCurvedCanvas", 0f);

            _surfaceMaterial.SetFloat("_BulgeRadius", 1.19f * sourceWorldScale);
            _surfaceMaterial.SetFloat("_BulgeHeight", 0.145f * sourceWorldScale);
            _surfaceMaterial.SetFloat("_PeakWidth", 0.5f * sourceWorldScale);
            _surfaceMaterial.SetFloat("_FluidHeightScale", 0.42f * 2.7f * sourceWorldScale);
            _surfaceMaterial.SetFloat("_MagnetHeightDeadZoneRadius", 0.185f * sourceWorldScale);
            _surfaceMaterial.SetFloat("_MagnetHeightStrengthCap", 0.72f);
            _surfaceMaterial.SetFloat("_HeightFalloffExponent", 1.25f);
            _surfaceMaterial.SetFloat("_HeightGain", 1.57f);
            _surfaceMaterial.SetFloat("_BackHeightLoss", 3.46f);
            _surfaceMaterial.SetFloat("_BackHeightFalloffExponent", 2f);
            _surfaceMaterial.SetFloat("_MaximumHeight", 2.32f);
            _surfaceMaterial.SetFloat("_MaximumTiltRadians", 52f * Mathf.Deg2Rad);
            _surfaceMaterial.SetFloat("_TiltPeakRadiusMultiplier", 0.24f);
            _surfaceMaterial.SetFloat("_TiltReturnRadiusMultiplier", 0.37f);
            _surfaceMaterial.SetFloat("_TiltRiseExponent", 1.75f);
            _surfaceMaterial.SetFloat("_TiltReturnExponent", 0.92f);
            _surfaceMaterial.SetFloat("_InnerCornerFan", 0f);
            _surfaceMaterial.SetFloat("_HeightFactor", HeightFactorValue);
            _surfaceMaterial.SetFloat("_SpikesEnabled", 1f);
            _surfaceMaterial.SetFloat("_SpikeSpringStrength", 250f);
            _surfaceMaterial.SetFloat("_SpikeSpringDamping", 12f);
            _surfaceMaterial.SetFloat("_Reflectivity", 0.21f);
            _surfaceMaterial.SetFloat("_MatteSheen", 0.57f);
            _surfaceMaterial.SetFloat("_MatteAngle", 10f);
            _surfaceMaterial.SetFloat("_ReflectionAngle", 15f);
            _surfaceMaterial.SetVector("_MatteLightRotation", new Vector3(76f, 295f, 174f));
            _surfaceMaterial.SetVector("_ReflectionRotation", new Vector3(244f, 210f, 167f));
            _surfaceMaterial.SetFloat("_EmissionStrength", 0f);
            _surfaceMaterial.SetFloat("_Exposure", 1f);
            _surfaceMaterial.SetFloat("_ThinFilmScale", 3f);
            _surfaceMaterial.SetFloat("_ThinFilmOffset", 0.13f);
            _surfaceMaterial.SetFloat("_FadeAlpha", 1f);
            _surfaceMaterial.SetVector("_PrimitiveYawCosSin", new Vector2(1f, 0f));

            // A focused magnet 25 cm in front of the torus face raises a visible spike cluster.
            _magnetWorldPosition = new Vector3(0f, 0.02f, 0.30f);
            _pointerPosition = Vector2.zero;
            _magnetStrength = 0.65f;
            _surfaceMaterial.SetVector("_MagneticFieldPosition", _magnetWorldPosition);
            _surfaceMaterial.SetFloat("_MagneticFieldStrength", _magnetStrength);
            _surfaceMaterial.SetVector("_MagnetOpeningPosition", _magnetWorldPosition);
            _surfaceMaterial.SetVector("_MagnetOpeningNormal", Vector3.forward);
            _surfaceMaterial.SetVector("_MagnetOpeningDirection", Vector3.zero);
            _surfaceMaterial.SetFloat("_MagnetOpeningStrength", 0f);

            _simulationMaterial.SetFloat("_SurfaceShapeMode", 0f);
            _simulationMaterial.SetInt("_ParticleTextureSize", ParticleTextureSize);
            _simulationMaterial.SetInt("_ParticleCount", FieldColumns * FieldRows);
            _simulationMaterial.SetVector("_DomainScale", new Vector2(14.8f, 4f));
            _simulationMaterial.SetFloat("_HeightFactor", HeightFactorValue);
            _simulationMaterial.SetFloat("_HeightMapScale", HeightMapScaleFactor);
            _simulationMaterial.SetFloat("_SmoothFactor", HeightMapSmoothFactor);
            _simulationMaterial.SetFloat("_H", 1f);
            _simulationMaterial.SetFloat("_HSQ", 1f);
            _simulationMaterial.SetFloat("_Mass", 1.01f);
            _simulationMaterial.SetFloat("_RestDensity", 0.1f);
            _simulationMaterial.SetFloat("_GasConstant", 438f);
            _simulationMaterial.SetFloat("_Viscosity", 9.8f);
            _simulationMaterial.SetFloat("_Poly6", 315f / (64f * Mathf.PI));
            _simulationMaterial.SetFloat("_SpikyGradient", -45f / Mathf.PI);
            _simulationMaterial.SetFloat("_ViscosityLaplacian", 45f / Mathf.PI);
            _simulationMaterial.SetFloat("_PointerRadius", 5.1f);
            _simulationMaterial.SetFloat("_MaximumSlide", 4.1f);
            _simulationMaterial.SetFloat("_SlideFraction", 0.5f);
            _simulationMaterial.SetFloat("_SlideFalloffExponent", 0.7f);
            _simulationMaterial.SetFloat("_Elasticity", 80f);
            _simulationMaterial.SetFloat("_Damping", 7f);
            _simulationMaterial.SetFloat("_SpikeFactor", 16.25f / 2f);
            _simulationMaterial.SetFloat("_SpikeBaseCutoff", 0.046f);
            _simulationMaterial.SetVector("_PerimeterSegments", perimeterSegments);
            _simulationMaterial.SetFloat("_CornerRadius", cornerRadius);
            _simulationMaterial.SetFloat("_RadialRadius", RadialRadius);
            _simulationMaterial.SetVector("_PointerPosition", _pointerPosition);
            _simulationMaterial.SetFloat("_PointerStrength", _magnetStrength);

            _stateA = CreateFloatTarget("State A", ParticleTextureSize, FilterMode.Point, TextureWrapMode.Repeat);
            _stateB = CreateFloatTarget("State B", ParticleTextureSize, FilterMode.Point, TextureWrapMode.Repeat);
            _densityPressure = CreateFloatTarget("Density Pressure", ParticleTextureSize, FilterMode.Point, TextureWrapMode.Clamp);
            _force = CreateFloatTarget("Force", ParticleTextureSize, FilterMode.Point, TextureWrapMode.Clamp);
            _springStateA = CreateFloatTarget("Spring A", ParticleTextureSize, FilterMode.Point, TextureWrapMode.Clamp);
            _springStateB = CreateFloatTarget("Spring B", ParticleTextureSize, FilterMode.Point, TextureWrapMode.Clamp);
            _heightMap = CreateFloatTarget("Height Map", HeightMapSize, FilterMode.Bilinear, TextureWrapMode.Repeat);
            _coarseCandidates = CreateFloatTarget("Coarse Candidates", CoarseCandidateMapSize, FilterMode.Point, TextureWrapMode.Clamp);
            _simulationMaterial.SetTexture("_CoarseCandidateTex", _coarseCandidates);
            _simulationMaterial.SetFloat("_CoarseCandidateSize", CoarseCandidateMapSize);

            _restPositionTexture = CreateRestTexture();
            Graphics.Blit(_restPositionTexture, _stateA);
            Graphics.Blit(_restPositionTexture, _stateB);
            _currentState = _stateA;
            _nextState = _stateB;
            _springCurrent = _springStateA;
            _springNext = _springStateB;
            _surfaceMaterial.SetTexture("_SpringStateTex", _springCurrent);

            GameObject surfaceObject = new("Ferrofluid Surface (torus)", typeof(MeshFilter), typeof(MeshRenderer));
            surfaceObject.transform.SetPositionAndRotation(new Vector3(0f, 0f, 0.55f), Quaternion.identity);
            surfaceObject.transform.localScale = Vector3.one * 0.02f;
            MeshFilter meshFilter = surfaceObject.GetComponent<MeshFilter>();
            meshFilter.sharedMesh = CreateSurfaceMesh(191);
            _renderer = surfaceObject.GetComponent<MeshRenderer>();
            _renderer.sharedMaterial = _surfaceMaterial;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            // Control object: a project-compiled (non-bundle) shader that paints green on the left
            // eye and red on the right under SPI. If it renders both colors but the torus is
            // left-only, the shipped bundle's stereo variant is the problem; if the control is
            // also left-only, the draw submission path is.
            if (stereoControlShader != null)
            {
                GameObject controlObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                controlObject.name = "Stereo Control Quad";
                controlObject.transform.SetPositionAndRotation(new Vector3(0.55f, 0.35f, 1.1f), Quaternion.identity);
                controlObject.transform.localScale = Vector3.one * 0.22f;
                Destroy(controlObject.GetComponent<Collider>());
                Material controlMaterial = new(stereoControlShader)
                {
                    name = "Stereo Control",
                    hideFlags = HideFlags.DontSave,
                    enableInstancing = true,
                };
                MeshRenderer controlRenderer = controlObject.GetComponent<MeshRenderer>();
                _controlRenderer = controlRenderer;
                controlRenderer.sharedMaterial = controlMaterial;
                controlRenderer.shadowCastingMode = ShadowCastingMode.Off;
                controlRenderer.receiveShadows = false;
            }
            else
            {
                throw new InvalidOperationException("The stereo control shader is required to verify distinct eye slices.");
            }

            Report($"surface material instancing={_surfaceMaterial.enableInstancing} keywords=[{string.Join(", ", _surfaceMaterial.shaderKeywords)}]");
        }

        private void WarmUpSimulation()
        {
            for (int i = 0; i < 40; i++)
            {
                Tick(8f);
            }
        }

        private void Tick(float deltaTimeMilliseconds)
        {
            if (_surfaceMaterial == null)
            {
                return;
            }

            _surfaceMaterial.SetMatrix("_FerrofluidObjectToWorld", _renderer.transform.localToWorldMatrix);
            _surfaceMaterial.SetTexture("_HeightMapTex", _heightMap);

            for (int step = 0; step < 2; step++)
            {
                _simulationMaterial.SetTexture("_StateTex", _currentState);
                Graphics.Blit(Texture2D.blackTexture, _densityPressure, _simulationMaterial, 0);
                _simulationMaterial.SetTexture("_DensityPressureTex", _densityPressure);
                Graphics.Blit(Texture2D.blackTexture, _force, _simulationMaterial, 1);
                _simulationMaterial.SetTexture("_RestPositionTex", _restPositionTexture);
                _simulationMaterial.SetTexture("_ForceTex", _force);
                _simulationMaterial.SetFloat("_DeltaTimeMilliseconds", deltaTimeMilliseconds);
                Graphics.Blit(Texture2D.blackTexture, _nextState, _simulationMaterial, 2);
                (_currentState, _nextState) = (_nextState, _currentState);
            }

            _simulationMaterial.SetTexture("_StateTex", _currentState);
            _surfaceMaterial.SetTexture("_StateTex", _currentState);
            Graphics.Blit(Texture2D.blackTexture, _coarseCandidates, _simulationMaterial, 5);
            Graphics.Blit(Texture2D.blackTexture, _heightMap, _simulationMaterial, 3);

            _surfaceMaterial.SetFloat("_DeltaTimeMilliseconds", deltaTimeMilliseconds);
            _surfaceMaterial.SetTexture("_SpringStateTex", _springCurrent);
            Graphics.Blit(Texture2D.blackTexture, _springNext, _surfaceMaterial, 1);
            (_springCurrent, _springNext) = (_springNext, _springCurrent);
            _surfaceMaterial.SetTexture("_SpringStateTex", _springCurrent);
        }

        private void CaptureAndExit()
        {
            _captured = true;
            int exitCode = 0;
            try
            {
                Report("=== capture ===");
                Report($"stereoMode={XRSettings.stereoRenderingMode} deviceEyeTexDim={XRSettings.deviceEyeTextureDimension}");
                RenderTextureDescriptor desc = XRSettings.eyeTextureDesc;
                Report($"eyeTextureDesc {desc.width}x{desc.height} depth={desc.volumeDepth} dim={desc.dimension} vrUsage={desc.vrUsage} msaa={desc.msaaSamples} color={desc.colorFormat}");
                Report($"camPos={_xrCamera.transform.position} camFwd={_xrCamera.transform.forward}");
                Report($"viewL={_xrCamera.GetStereoViewMatrix(Camera.StereoscopicEye.Left)}");
                Report($"viewR={_xrCamera.GetStereoViewMatrix(Camera.StereoscopicEye.Right)}");
                Report($"projL={_xrCamera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left)}");
                Report($"projR={_xrCamera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right)}");

                List<(Texture texture, int slice, Rect region)> eyes = CollectEyeTargets();
                if (eyes.Count == 0)
                {
                    eyes = CollectMirrorViews();
                }

                if (eyes.Count == 0)
                {
                    throw new InvalidOperationException("no XR eye render targets found at capture time.");
                }

                RenderEyeTargets(eyes);
                List<(int lit, float widthFraction, float heightFraction, int green, int red)> eyeStats = new();
                for (int eye = 0; eye < eyes.Count; eye++)
                {
                    (Texture texture, int slice, Rect region) = eyes[eye];
                    string fileName = eye == 0 ? "left.png" : $"right{(eyes.Count > 2 ? eye.ToString() : string.Empty)}.png";
                    Report($"eye{eye}: tex={texture.width}x{texture.height} dim={texture.dimension} slice={slice} region={region}");
                    eyeStats.Add(SaveEyeSource(texture, slice, region, fileName));
                }

                int monoLit = RenderMonoReference();
                if (monoLit < 64)
                {
                    exitCode = 4;
                    Report("VERDICT FAIL: the mono reference lacks the torus.");
                }


                // The torus is wide and shallow at its real world scale. Control pixels are excluded so the colored quad cannot satisfy either surface assertion.
                const int MinimumLitPixels = 64;
                const float MinimumWidthCoverage = 0.25f;
                const float MinimumHeightCoverage = 0.1f;
                if (eyeStats.Count < 2)
                {
                    exitCode = 4;
                    Report($"VERDICT FAIL: expected 2 eye captures, got {eyeStats.Count}.");
                }
                else
                {
                    for (int eye = 0; eye < eyeStats.Count; eye++)
                    {
                        (int lit, float widthFraction, float heightFraction, int green, int red) = eyeStats[eye];
                        string eyeName = eye == 0 ? "left" : "right";
                        int expectedControl = eye == 0 ? green : red;
                        int wrongControl = eye == 0 ? red : green;
                        if (expectedControl < MinimumLitPixels || wrongControl >= MinimumLitPixels)
                        {
                            exitCode = 4;
                            Report($"VERDICT FAIL: {eyeName} eye has incorrect stereo control colors (green={green}, red={red}).");
                        }

                        if (lit < MinimumLitPixels || widthFraction < MinimumWidthCoverage || heightFraction < MinimumHeightCoverage)
                        {
                            exitCode = 4;
                            Report($"VERDICT FAIL: {eyeName} eye lacks the torus (lit={lit}, coverage={widthFraction:P0}x{heightFraction:P0}).");
                        }
                    }

                    if (exitCode == 0)
                    {
                        Report($"VERDICT PASS: ferrofluid present in all {eyeStats.Count} eyes.");
                    }
                }

                Report("=== done ===");
                File.WriteAllText(Path.Combine(_captureDir, "report.txt"), _report.ToString());
                Debug.Log(_report.ToString());
            }
            catch (Exception exception)
            {
                exitCode = 2;
                Report($"capture threw: {exception}");
                File.WriteAllText(Path.Combine(_captureDir, "report.txt"), _report.ToString());
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(exitCode);
#else
            Application.Quit(exitCode);
#endif
        }

        // Hidden player windows skip automatic camera rendering, so submit one instanced draw into an owned array using the Mock HMD views.
        private void RenderEyeTargets(List<(Texture texture, int slice, Rect region)> eyes)
        {
            if (eyes.Count != 2 || eyes[0].texture != eyes[1].texture
                || eyes[0].texture.dimension != TextureDimension.Tex2DArray
                || XRSettings.stereoRenderingMode != XRSettings.StereoRenderingMode.SinglePassInstanced)
            {
                throw new InvalidOperationException("The stereo regression requires one two-eye single-pass-instanced texture array.");
            }

            RenderTextureDescriptor descriptor = XRSettings.eyeTextureDesc;
            descriptor.depthBufferBits = 24;
            descriptor.msaaSamples = 1;
            RenderTexture target = new(descriptor) { name = "Stereo Regression Eyes" };
            target.Create();
            _stereoTarget = target;
            for (int eye = 0; eye < eyes.Count; eye++)
            {
                eyes[eye] = (target, eyes[eye].slice, eyes[eye].region);
            }

            Matrix4x4[] views = new Matrix4x4[2];
            Matrix4x4[] projections = new Matrix4x4[2];
            Matrix4x4[] viewProjections = new Matrix4x4[2];
            Matrix4x4[] inverseViews = new Matrix4x4[2];
            Matrix4x4[] inverseProjections = new Matrix4x4[2];
            Vector4[] positions = new Vector4[2];
            for (int eye = 0; eye < 2; eye++)
            {
                Camera.StereoscopicEye stereoEye = (Camera.StereoscopicEye)eye;
                views[eye] = _xrCamera.GetStereoViewMatrix(stereoEye);
                inverseViews[eye] = views[eye].inverse;
                projections[eye] = GL.GetGPUProjectionMatrix(_xrCamera.GetStereoProjectionMatrix(stereoEye), true);
                inverseProjections[eye] = projections[eye].inverse;
                viewProjections[eye] = projections[eye] * views[eye];
                positions[eye] = inverseViews[eye].GetColumn(3);
            }

            // UnityShaderVariables.cginc packs eight pairs of matrices, two camera positions, and two scale/offset vectors in UnityStereoGlobals. Individual shader globals do not populate that buffer outside the camera loop.
            Vector4[] constants = new Vector4[68];
            int constantIndex = 0;
            foreach (Matrix4x4[] matrices in new[] { projections, views, inverseViews, viewProjections, projections, inverseProjections, views, inverseViews })
            {
                foreach (Matrix4x4 matrix in matrices)
                {
                    for (int column = 0; column < 4; column++)
                    {
                        constants[constantIndex++] = matrix.GetColumn(column);
                    }
                }
            }

            constants[64] = positions[0];
            constants[65] = positions[1];
            constants[66] = new Vector4(1f, 1f, 0f, 0f);
            constants[67] = new Vector4(1f, 1f, 0f, 0f);
            using (GraphicsBuffer stereoConstants = new(GraphicsBuffer.Target.Constant, constants.Length, 16))
            using (CommandBuffer commands = new() { name = "Ferrofluid stereo regression" })
            {
                stereoConstants.SetData(constants);
                commands.SetRenderTarget(new RenderTargetIdentifier(eyes[0].texture), 0, CubemapFace.Unknown, -1);
                commands.SetViewport(eyes[0].region);
                commands.ClearRenderTarget(true, true, _xrCamera.backgroundColor);
                commands.SetViewProjectionMatrices(views[0], projections[0]);
                commands.EnableShaderKeyword("STEREO_INSTANCING_ON");
                commands.SetSinglePassStereo(SinglePassStereoMode.Instancing);
                commands.SetGlobalConstantBuffer(stereoConstants, Shader.PropertyToID("UnityStereoGlobals"), 0, constants.Length * 16);
                commands.SetInstanceMultiplier(2);
                if (_simulateMissingRightEye)
                {
                    commands.SetRenderTarget(new RenderTargetIdentifier(target), 0, CubemapFace.Unknown, 0);
                }

                commands.DrawRenderer(_renderer, _surfaceMaterial, 0, 0);
                commands.SetRenderTarget(new RenderTargetIdentifier(target), 0, CubemapFace.Unknown, -1);
                commands.DrawRenderer(_controlRenderer, _controlRenderer.sharedMaterial, 0, 0);
                commands.SetInstanceMultiplier(1);
                commands.SetSinglePassStereo(SinglePassStereoMode.None);
                commands.DisableShaderKeyword("STEREO_INSTANCING_ON");
                Graphics.ExecuteCommandBuffer(commands);
            }

            Report($"Submitted single-pass-instanced renderer draws to owned eye slices: created={target.IsCreated()} format={target.graphicsFormat} missingRightEye={_simulateMissingRightEye}.");
            AsyncGPUReadbackRequest readback = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32);
            readback.WaitForCompletion();
            for (int eye = 0; eye < 2; eye++)
            {
                var pixels = readback.GetData<Color32>(eye);
                int maximum = 0;
                for (int index = 0; index < pixels.Length; index++)
                {
                    maximum = Math.Max(maximum, Math.Max(pixels[index].r, Math.Max(pixels[index].g, pixels[index].b)));
                }

                Report($"Direct GPU eye{eye}: error={readback.hasError} first={pixels[0]} maximum={maximum}");
            }
        }

        // The eye render targets live inside the display subsystem's render passes: Single Pass
        // Instanced exposes them as slices of one texture array, multipass as separate 2D targets
        // or horizontal halves of a shared target.
        private List<(Texture texture, int slice, Rect region)> CollectEyeTargets()
        {
            List<(Texture texture, int slice, Rect region)> eyes = new();
            List<XRDisplaySubsystem> displays = new();
            SubsystemManager.GetSubsystems(displays);
            Report($"displays={displays.Count}");
            foreach (XRDisplaySubsystem display in displays)
            {
                if (display == null)
                {
                    continue;
                }

                Report($"display running={display.running} layout={display.textureLayout} supported={display.supportedTextureLayouts}");
                if (!display.running)
                {
                    continue;
                }

                int passCount = display.GetRenderPassCount();
                Report($"renderPassCount={passCount}");
                for (int passIndex = 0; passIndex < passCount; passIndex++)
                {
                    display.GetRenderPass(passIndex, out XRDisplaySubsystem.XRRenderPass pass);
                    Texture target = display.GetRenderTextureForRenderPass(passIndex);
                    int parameterCount = pass.GetRenderParameterCount();
                    Report($"pass{passIndex}: target={(target == null ? "null" : $"{target.width}x{target.height} {target.dimension}")} params={parameterCount} targetDesc={pass.renderTargetDesc.width}x{pass.renderTargetDesc.height} dim={pass.renderTargetDesc.dimension} depth={pass.renderTargetDesc.volumeDepth}");
                    for (int paramIndex = 0; paramIndex < parameterCount; paramIndex++)
                    {
                        pass.GetRenderParameter(_xrCamera, paramIndex, out XRDisplaySubsystem.XRRenderParameter parameter);
                        Report($"  param{paramIndex}: slice={parameter.textureArraySlice} viewport={parameter.viewport} view.m03={parameter.view.m03:F4}");
                        // XRRenderParameter.view is a normalized 0-1 rect; convert to pixels.
                        Rect pixelViewport = new(
                            parameter.viewport.x * target.width,
                            parameter.viewport.y * target.height,
                            parameter.viewport.width * target.width,
                            parameter.viewport.height * target.height);
                        eyes.Add((target, parameter.textureArraySlice, pixelViewport));
                    }
                }
            }

            return eyes;
        }

        // Fallback: the mirror-view blit description exposes the per-eye source textures and
        // slices directly, without needing a live render pass.
        private List<(Texture texture, int slice, Rect region)> CollectMirrorViews()
        {
            List<(Texture texture, int slice, Rect region)> eyes = new();
            List<XRDisplaySubsystem> displays = new();
            SubsystemManager.GetSubsystems(displays);
            foreach (XRDisplaySubsystem display in displays)
            {
                if (display == null || !display.running)
                {
                    continue;
                }

                RenderTexture mirrorTarget = new(XRSettings.eyeTextureDesc.width * 2, XRSettings.eyeTextureDesc.height, 0, RenderTextureFormat.ARGB32)
                {
                    name = "Mirror Target",
                };
                mirrorTarget.Create();
                bool described = display.GetMirrorViewBlitDesc(mirrorTarget, out XRDisplaySubsystem.XRMirrorViewBlitDesc mirrorDesc, display.GetPreferredMirrorBlitMode());
                Report($"mirror blit described={described} params={mirrorDesc.blitParamsCount} native={mirrorDesc.nativeBlitAvailable}");
                for (int i = 0; i < mirrorDesc.blitParamsCount; i++)
                {
                    mirrorDesc.GetBlitParameter(i, out XRDisplaySubsystem.XRBlitParams parameter);
                    Report($"  mirror param{i}: src={parameter.srcTex?.width}x{parameter.srcTex?.height} slice={parameter.srcTexArraySlice} srcRect={parameter.srcRect}");
                    if (parameter.srcTex != null)
                    {
                        eyes.Add((parameter.srcTex, parameter.srcTexArraySlice, parameter.srcRect));
                    }
                }

                mirrorTarget.Release();
                Destroy(mirrorTarget);
            }

            return eyes;
        }

        private (int lit, float widthFraction, float heightFraction, int green, int red) SaveEyeSource(
            Texture source, int arraySlice, Rect pixelRegion, string fileName)
        {
            int copyWidth = pixelRegion.width > 0 ? (int)pixelRegion.width : source.width;
            int copyHeight = pixelRegion.height > 0 ? (int)pixelRegion.height : source.height;
            RenderTexture slice = new(copyWidth, copyHeight, 0, RenderTextureFormat.ARGB32)
            {
                name = "Eye Source",
            };
            slice.Create();
            Graphics.CopyTexture(
                source,
                Math.Max(arraySlice, 0),
                0,
                (int)pixelRegion.x,
                (int)pixelRegion.y,
                copyWidth,
                copyHeight,
                slice,
                0,
                0,
                0,
                0);
            int litPixels = SaveTexture(slice, fileName, default, out Rect litRect, out int green, out int red);
            slice.Release();
            Destroy(slice);
            return (litPixels, copyWidth > 0 ? litRect.width / copyWidth : 0f, copyHeight > 0 ? litRect.height / copyHeight : 0f, green, red);
        }

        private int RenderMonoReference()
        {
            RenderTexture mono = new(960, 540, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "Mono Reference",
            };
            mono.Create();
            _monoCamera.targetTexture = mono;
            _monoCamera.Render();
            int lit = SaveTexture(mono, "mono.png");
            _monoCamera.targetTexture = null;
            RenderTexture.active = null;
            mono.Release();
            Destroy(mono);
            return lit;
        }

        private int SaveTexture(RenderTexture source, string fileName, Rect readRegion = default)
        {
            return SaveTexture(source, fileName, readRegion, out _, out _, out _);
        }

        private int SaveTexture(RenderTexture source, string fileName, Rect readRegion, out Rect litRect, out int green, out int red)
        {
            if (readRegion.width <= 0 || readRegion.height <= 0)
            {
                readRegion = new Rect(0, 0, source.width, source.height);
            }

            int readWidth = (int)readRegion.width;
            int readHeight = (int)readRegion.height;
            Texture2D read = new(readWidth, readHeight, TextureFormat.RGBA32, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = source;
            read.ReadPixels(readRegion, 0, 0);
            read.Apply();
            RenderTexture.active = previous;

            string path = Path.Combine(_captureDir, fileName);
            File.WriteAllBytes(path, read.EncodeToPNG());

            Color32[] pixels = read.GetPixels32();
            int lit = 0;
            green = 0;
            red = 0;
            int minX = readWidth;
            int minY = readHeight;
            int maxX = -1;
            int maxY = -1;
            long rSum = 0;
            long gSum = 0;
            long bSum = 0;
            for (int y = 0; y < readHeight; y++)
            {
                for (int x = 0; x < readWidth; x++)
                {
                    Color32 c = pixels[(y * readWidth) + x];
                    bool greenControl = c.g > 64 && c.r < 40 && c.b < 40;
                    bool redControl = c.r > 64 && c.g < 40 && c.b < 40;
                    if (greenControl || redControl)
                    {
                        green += greenControl ? 1 : 0;
                        red += redControl ? 1 : 0;
                        continue;
                    }

                    int brightest = Math.Max(c.r, Math.Max(c.g, c.b));
                    // The dark blue background clears to ~(27,38,47) in sRGB; keep the threshold
                    // comfortably above it so only actual ferrofluid highlights count as lit.
                    if (brightest > 64)
                    {
                        lit++;
                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                        rSum += c.r;
                        gSum += c.g;
                        bSum += c.b;
                    }
                }
            }

            litRect = lit == 0
                ? Rect.zero
                : new Rect(minX, minY, (maxX - minX) + 1, (maxY - minY) + 1);
            string verdict = lit == 0
                ? "EMPTY"
                : $"lit={lit} bbox=({minX},{minY})-({maxX},{maxY}) meanRgb=({rSum / lit},{gSum / lit},{bSum / lit})";
            Report($"{fileName}: {verdict} controlGreen={green} controlRed={red}");
            Destroy(read);
            return lit;
        }

        private void FailAndExit(string reason)
        {
            Report($"FAILED: {reason}");
            try
            {
                File.WriteAllText(Path.Combine(_captureDir, "report.txt"), _report.ToString());
            }
            catch
            {
                // Best effort report write.
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(2);
#else
            Application.Quit(2);
#endif
        }

        private void Report(string message)
        {
            _report.AppendLine(message);
            Debug.Log($"[FerrofluidStereo] {message}");
        }

        private static RenderTexture CreateFloatTarget(string name, int size, FilterMode filter, TextureWrapMode wrap)
        {
            RenderTexture texture = new(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                name = name,
                filterMode = filter,
                wrapMode = wrap,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.DontSave,
            };
            texture.Create();
            return texture;
        }

        private Texture2D CreateRestTexture()
        {
            Texture2D texture = new(ParticleTextureSize, ParticleTextureSize, TextureFormat.RGBAFloat, false, true)
            {
                name = "Rest Positions",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.DontSave,
            };
            Color[] pixels = new Color[ParticleTextureSize * ParticleTextureSize];
            for (int row = 0; row < FieldRows; row++)
            {
                for (int column = 0; column < FieldColumns; column++)
                {
                    int index = (row * FieldColumns) + column;
                    float xNoise = Mathf.Sin((index + 1) * 12.9898f) * 43758.5453f;
                    float yNoise = Mathf.Sin((index + 1) * 78.233f) * 43758.5453f;
                    float xJitter = ((xNoise - Mathf.Floor(xNoise)) - 0.5f) * 0.2f;
                    float yJitter = ((yNoise - Mathf.Floor(yNoise)) - 0.5f) * 0.2f;
                    float perimeterFraction = (column + 0.5f + xJitter) / FieldColumns;
                    float crossSectionFraction = Mathf.Repeat(
                        ((row + 0.5f + yJitter) / FieldRows) + (2f * perimeterFraction),
                        1f);
                    pixels[index] = new Color((perimeterFraction * 2f) - 1f, (crossSectionFraction * 2f) - 1f, 0f, 0f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

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
                name = "StereoTest Ferrofluid Surface",
                indexFormat = IndexFormat.UInt32,
                vertices = vertices,
                uv = uvs,
                triangles = triangles,
                bounds = new Bounds(Vector3.zero, Vector3.one * 10000f),
            };
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
