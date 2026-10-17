using System;
using System.Reflection;
using HarmonyLib;
using SongCore.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ChromaGLS.Settings
{
    // SongCore finishes assigning requirement arrays here, so read them in the postfix.
    [HarmonyPatch]
    internal static class ChromaGlsRequirementSelectionPatch
    {
        // Supported SongCore versions use three different metadata writers.
        private static readonly MethodBase? SelectionWriter = ResolveSelectionWriter(typeof(RequirementsUI).Assembly);
        private static readonly PropertyInfo? RequirementsUiInstance = AccessTools.Property(typeof(RequirementsUI), "instance");

        // A missing menu hook must not prevent gameplay lighting patches from installing.
        private static bool Prepare()
        {
            bool supported = SelectionWriter != null && RequirementsUiInstance != null;
            if (!supported)
            {
                Plugin.Log?.Warn("[ChromaGLS Ferrofluid] SongCore selection writer unavailable; skipping only the requirement marker patch, not gameplay lighting.");
            }
            else
            {
                Plugin.Log?.Info($"[ChromaGLS Ferrofluid] SongCore writer={SelectionWriter!.DeclaringType!.FullName}.{SelectionWriter.Name} static={SelectionWriter.IsStatic}.");
            }

            return supported;
        }

        private static MethodBase? TargetMethod()
        {
            return SelectionWriter;
        }

        // Search only SongCore so type-load failures in other mods cannot hide its writer.
        private static MethodBase? ResolveSelectionWriter(Assembly songCoreAssembly)
        {
            Type? writerType = songCoreAssembly.GetType("SongCore.Hooks.SongDataMenuHooks");
            if (writerType != null)
            {
                return AccessTools.Method(writerType, "ProcessBeatmapRequirements");
            }

            // SongCore 3.15 uses Affinity between the older Harmony and newer MonoMod implementations.
            writerType = songCoreAssembly.GetType("SongCore.Patches.SongDataMenuPatches");
            if (writerType != null)
            {
                return AccessTools.Method(writerType, "HandleBeatmapRequirements");
            }

            writerType = songCoreAssembly.GetType("SongCore.HarmonyPatches.StandardLevelDetailViewRefreshContentPatch")
                ?? songCoreAssembly.GetType("SongCore.HarmonyPatches.StandardLevelDetailViewRefreshContent");
            return writerType != null ? AccessTools.Method(writerType, "Postfix") : null;
        }

        // Legacy writers are static. Use the RequirementsUI singleton instead of Harmony __instance.
        private static void Postfix()
        {
            if (RequirementsUiInstance?.GetValue(null) is RequirementsUI requirementsUi)
            {
                ChromaGlsRequirementEffect.RefreshActiveState(requirementsUi);
            }
        }
    }

#pragma warning disable SA1204

    internal sealed class ChromaGlsRequirementEffect : MaskableGraphic
    {
        private const string CapabilityName = "ChromaGLS";
        private const int MagnetPerimeterSamples = 256;
        private const float RenderDepthBias = 0.04f;

        // Scale the whole ring so the tube and magnet mapping still align with the info button.
        private const float RequirementTorusRadiusScale = 1.05f;

        private const float WorldRecessionDepth = 0.031f;

        private const float FadeDurationSeconds = 0.3f;

        private static readonly Vector2 PreviewFallbackRectSize = new(12f, 7f);
        private static readonly FieldInfo InfoButtonTransformField = AccessTools.Field(typeof(RequirementsUI), "infoButtonTransform");
        private static readonly FieldInfo DifficultyDataField = AccessTools.Field(typeof(RequirementsUI), "diffData");
        private static readonly Type? CurvedCanvasSettingsType = AccessTools.TypeByName("HMUI.CurvedCanvasSettings");
        private static readonly PropertyInfo? CurvedCanvasRadiusProperty = CurvedCanvasSettingsType != null
            ? AccessTools.Property(CurvedCanvasSettingsType, "radius")
            : null;

        private static ChromaGlsRequirementEffect? _instance;
        private static ChromaGlsRequirementEffect? _previewInstance;
        private static RectTransform? _previewAnchor;
        private static RequirementsUI? _lastRequirementsUi;
        private static Config? _configuration;
        private static Vector2 _lastRequirementRectSize = PreviewFallbackRectSize;

        // Non-uniform canvas transforms make BG.lossyScale unsuitable for sizing the settings preview.
        private static Vector3 _lastRequirementRenderedWorldUnitScale = new(0.03f, 0.03f, 0.03f);

        private static Vector3 _lastRequirementWorldSize;
        private static float _lastRequirementViewerDistance = 2.5f;
        private static bool _hasRequirementWorldSize;
        private static bool _ferrofluidEnabled = true;

        private static bool _hasAttachedUnityState;
        private RequirementsUI? _requirementsUi;
        private FieldInfo? _additionalDifficultyDataField;
        private FieldInfo? _requirementsField;
        private FieldInfo? _suggestionsField;
        private Transform? _buttonBorderTransform;
        private Transform? _outlineWrapperTransform;
        private Graphic? _buttonBackgroundGraphic;
        private MeshRenderer? _sceneRenderer;

        // CanvasRenderer.GetAlpha excludes the ancestor CanvasGroup fades used during solo and practice transitions.
        private CanvasGroup[]? _ancestorCanvasGroups;
        private Transform _curvedCanvasTransform = null!;
        private Transform _viewerTransform = null!;
        private Matrix4x4 _localToCurvedCanvasMatrix;
        private Matrix4x4 _curvedCanvasToLocalMatrix;
        private FerrofluidGpuSimulation? _simulation;
        private FerrofluidMagnetController _magnetController = null!;
        private Vector2 _pointerTarget;
        private Vector2 _pointerSmoothed;
        private Vector3 _magneticFieldPosition;
        private Vector3 _magneticFieldTarget;
        private Vector3 _magneticFieldVelocity;
        private Vector3 _handPosition;
        private Vector3 _handVelocity;
        private float _handStrength;
        private bool _hasHand;

        private Vector3 _openingPointerPosition;
        private Vector3 _openingPlaneNormal;
        private Vector3 _openingPointerDirection;
        private float _openingWeight;
        private float _openingWeightTarget;
        private float _magneticFieldTargetStrength;
        private float _magneticFieldStrength;
        private float _curvedUiRadius;
        private float _viewerLocalZSign = -1f;
        private float _skew;
        private float _lastUpdateAt;

        private bool _hasCurvedCanvas;
        private bool _hasPointer;
        private bool _hasChromaGlsMetadata;
        private bool _buttonBorderWasActive;
        private bool _outlineWrapperWasActive;
        private bool _isPreview;

        private float _fadeAlpha;

        internal static void SetFerrofluidEnabled(bool enabled)
        {
            _ferrofluidEnabled = enabled;
            if (_hasAttachedUnityState)
            {
                ReconcileEffectLifetime();
            }
        }

        internal static void ApplyConfiguration(Config config)
        {
            _configuration = config;
            _instance?._simulation?.ApplyEnabledState(config);
            _previewInstance?._simulation?.ApplyEnabledState(config);
        }

        // Move the existing torus into the settings preview so only one simulation runs.
        internal static void CreatePreview(RectTransform previewAnchor)
        {
            _previewAnchor = previewAnchor;
            _hasAttachedUnityState = true;
            ReconcileEffectLifetime();
        }

        internal static void RemovePreview(RectTransform previewAnchor)
        {
            if (!ReferenceEquals(_previewAnchor, previewAnchor))
            {
                return;
            }

            _previewAnchor = null;
            ReconcileEffectLifetime();
        }

        internal static void RefreshActiveState(RequirementsUI requirementsUi)
        {
            _lastRequirementsUi = requirementsUi;
            _hasAttachedUnityState = true;
            RefreshInfoButtonVisibility(requirementsUi);
            Transform? measuredInfoButton = InfoButtonTransformField.GetValue(requirementsUi) as Transform;
            Transform? measuredButtonBackground = measuredInfoButton?.Find("BG");
            if (measuredButtonBackground is RectTransform measuredRectTransform)
            {
                _lastRequirementRectSize = measuredRectTransform.rect.size;
                if (_instance == null && _previewInstance == null)
                {
                    // Use BG scale until the first rendered measurement is available.
                    _lastRequirementRenderedWorldUnitScale = Vector3.one * Mathf.Abs(measuredRectTransform.lossyScale.x);
                }

                Camera? viewerCamera = Camera.main;
                if (viewerCamera != null)
                {
                    _lastRequirementViewerDistance = Vector3.Distance(viewerCamera.transform.position, measuredRectTransform.position);
                }
            }

            // The settings tab keeps the renderer while difficulty changes update the saved SongCore attachment.
            if (!ShouldCreateEffect(_ferrofluidEnabled) || _configuration == null)
            {
                DestroyExistingEffect();
                return;
            }

            ChromaGlsRequirementEffect? effect = _instance;
            if (effect == null)
            {
                Transform? infoButtonTransform = InfoButtonTransformField.GetValue(requirementsUi) as Transform;
                Transform? buttonBackgroundTransform = infoButtonTransform?.Find("BG");
                if (buttonBackgroundTransform is not RectTransform rectTransform)
                {
                    Plugin.Log.Error("[ChromaGLS Ferrofluid] SongCore button BG was not initialized; native outline retained.");
                    return;
                }

                effect = Create(requirementsUi, rectTransform, false, _configuration);
                if (effect == null)
                {
                    return;
                }
            }

            effect._hasChromaGlsMetadata = effect.SelectedDifficultyHasChromaGls();
            effect.ApplyVisibility();
        }

        // The Graphic receives Canvas callbacks, but the MeshRenderer supplies all visible geometry.
        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
        }

        protected override void OnDestroy()
        {
            if (ReferenceEquals(_instance, this))
            {
                RestoreSongCoreOutline();
                _instance = null;
                if (ReferenceEquals(_lastRequirementsUi, _requirementsUi))
                {
                    _lastRequirementsUi = null;
                }
            }

            if (ReferenceEquals(_previewInstance, this))
            {
                _previewInstance = null;
            }

            _simulation?.Dispose();
            _simulation = null;
            base.OnDestroy();
        }

        // Deactivation pauses Update, so resume the dissolve from the current menu alpha.
        protected override void OnEnable()
        {
            base.OnEnable();
            _lastUpdateAt = Time.unscaledTime;
            if (_isPreview || _sceneRenderer == null)
            {
                return;
            }

            bool buttonFlaggedVisible = _requirementsUi == null || _requirementsUi.ButtonGlowColor;
            float backgroundCanvasAlpha = (_buttonBackgroundGraphic != null ? _buttonBackgroundGraphic.canvasRenderer.GetAlpha() : 1f) * EvaluateAncestorAlpha();
            _fadeAlpha = EvaluateFadeTargetAlpha(buttonFlaggedVisible, backgroundCanvasAlpha, IsTuningMenuActive());
            _sceneRenderer.enabled = _fadeAlpha > 0.001f;
        }

        private void Update()
        {
            if (_simulation == null || _configuration == null)
            {
                return;
            }

            float now = Time.unscaledTime;
            float deltaTime = Mathf.Clamp(now - _lastUpdateAt, 0f, 0.1f);
            _lastUpdateAt = now;

            // MeshRenderer does not inherit CanvasGroup alpha. Apply the menu fade as a dissolve to preserve bloom.
            // Disable only the renderer so Update can detect when the menu becomes visible again.
            if (!_isPreview && _sceneRenderer != null)
            {
                float ancestorAlpha = EvaluateAncestorAlpha();
                bool buttonFlaggedVisible = _requirementsUi == null || _requirementsUi.ButtonGlowColor;
                float backgroundCanvasAlpha = (_buttonBackgroundGraphic != null ? _buttonBackgroundGraphic.canvasRenderer.GetAlpha() : 1f) * ancestorAlpha;
                float fadeTargetAlpha = EvaluateFadeTargetAlpha(buttonFlaggedVisible, backgroundCanvasAlpha, IsTuningMenuActive());
                _fadeAlpha = Mathf.MoveTowards(_fadeAlpha, fadeTargetAlpha, deltaTime / FadeDurationSeconds);
                bool rendered = _fadeAlpha > 0.001f;
                _sceneRenderer.enabled = rendered;
                float shaderFadeAlpha = EvaluateShaderFadeAlpha(_fadeAlpha, fadeTargetAlpha);
                _simulation.SetFadeAlpha(shaderFadeAlpha);

                if (!rendered)
                {
                    // Skip simulation and material updates while the renderer is hidden.
                    return;
                }
            }

            UpdateCurvedCanvasMatrices();

            Rect rect = rectTransform.rect;
            float sourceWorldScale = (rect.width * transform.TransformVector(Vector3.right).magnitude) / 0.9f;
            if (!_isPreview)
            {
                float renderedWorldUnitScale = EvaluateRenderedWorldUnitScale(sourceWorldScale, rect.width);
                _lastRequirementRenderedWorldUnitScale = Vector3.one * renderedWorldUnitScale;
                _lastRequirementViewerDistance = Vector3.Distance(_viewerTransform.position, transform.position);
            }

            // The magnet gap is in simulation units, so scale it with the rendered surface.
            float magnetForwardWorldDistance = EvaluateMagnetForwardWorldDistance(
                _configuration.MagnetForwardDistance,
                sourceWorldScale);

            // Use the curved button surface for the cursor plane, rather than the flat canvas behind it.
            EvaluateRenderedMagnetPlane(
                rect,
                rectTransform.pivot,
                _skew,
                _hasCurvedCanvas,
                _localToCurvedCanvasMatrix,
                _curvedCanvasToLocalMatrix,
                _curvedUiRadius,
                transform.localToWorldMatrix,
                _viewerLocalZSign,
                magnetForwardWorldDistance,
                out Vector3 magnetPlanePoint,
                out Vector3 magnetPlaneNormal);
            bool hasIntersection = _magnetController.TryGetIntersection(
                magnetPlanePoint,
                magnetPlaneNormal,
                out Vector3 worldIntersection);

            if (hasIntersection && TryMapMagnet(worldIntersection, magnetPlaneNormal, out Vector2 pointer, out float targetStrength, out Vector3 magnetPosition, out float openingWeight, out Vector3 openingDirection))
            {
                _magneticFieldTarget = magnetPosition;
                _pointerTarget = pointer;
                _magneticFieldTargetStrength = targetStrength;
                _openingPointerPosition = worldIntersection;
                _openingPlaneNormal = magnetPlaneNormal;
                _openingPointerDirection = openingDirection;
                _openingWeightTarget = openingWeight;
                if (!_hasPointer)
                {
                    _pointerSmoothed = pointer;
                    _magneticFieldPosition = magnetPosition;
                }

                _hasPointer = true;
            }
            else
            {
                _magneticFieldTargetStrength = 0f;
                _openingWeightTarget = 0f;
            }

            // Match the original 60 Hz smoothing while keeping controller response consistent at other headset refresh rates.
            float pointerBlend = 1f - Mathf.Pow(0.6f, deltaTime * 60f);
            _magneticFieldPosition = FerrofluidInteraction.SmoothPosition(_magneticFieldPosition, _magneticFieldTarget, ref _magneticFieldVelocity, deltaTime);
            float perimeterDelta = Mathf.Repeat((_pointerTarget.x - _pointerSmoothed.x) + 1f, 2f) - 1f;
            _pointerSmoothed.x = Mathf.Repeat((_pointerSmoothed.x + (perimeterDelta * pointerBlend)) + 1f, 2f) - 1f;
            _pointerSmoothed.y = Mathf.Lerp(_pointerSmoothed.y, _pointerTarget.y, pointerBlend);
            _openingWeight = Mathf.Lerp(_openingWeight, _openingWeightTarget, pointerBlend);
            float strengthBlend = 1f - Mathf.Pow(2f / 3f, deltaTime * 60f);
            _magneticFieldStrength = Mathf.Lerp(_magneticFieldStrength, _magneticFieldTargetStrength, strengthBlend);

            bool hasHand = _magnetController.TryGetInactiveHandPosition(out Vector3 handTarget);
            Vector2 handPointer = Vector2.zero;
            float handStrength = 0f;
            if (hasHand)
            {
                if (!_hasHand)
                {
                    _handPosition = handTarget;
                }

                _hasHand = true;
                _handPosition = FerrofluidInteraction.SmoothPosition(_handPosition, handTarget, ref _handVelocity, deltaTime);
                if (TryMapMagnet(_handPosition, magnetPlaneNormal, out handPointer, out _, out Vector3 mappedHand, out _, out _))
                {
                    float normalizedDistance = Vector3.Distance(_handPosition, mappedHand) / Mathf.Max(0.3f * sourceWorldScale, 0.0001f);
                    handStrength = 1f / (1f + (normalizedDistance * normalizedDistance));
                }
            }

            _handStrength = Mathf.Lerp(_handStrength, handStrength, strengthBlend);

            _simulation.UpdateFrame(
                rect,
                rectTransform.pivot,
                _skew,
                _hasCurvedCanvas,
                _localToCurvedCanvasMatrix,
                _curvedCanvasToLocalMatrix,
                _curvedUiRadius,
                _viewerLocalZSign,
                Mathf.Max(sourceWorldScale, 0.0001f),
                _magneticFieldPosition,
                _pointerSmoothed,
                _magneticFieldStrength,
                _openingPointerPosition,
                _openingPlaneNormal,
                _openingPointerDirection,
                _openingWeight,
                _handPosition,
                handPointer,
                _handStrength);
        }

        private static bool ShouldCreateEffect(bool ferrofluidEnabled)
        {
            return ferrofluidEnabled;
        }

        private static bool IsTuningMenuActive()
        {
#if FERROFLUID_TWEAKS
            return _previewAnchor != null && _previewAnchor.gameObject.activeInHierarchy;
#else
            return false;
#endif
        }

        private static bool ShouldShowEffect(bool ferrofluidEnabled, bool hasMetadata, bool tuningMenuActive)
        {
            return ferrofluidEnabled && (hasMetadata || tuningMenuActive);
        }

        private static bool ShouldShowInfoButton(bool nativeVisible, bool ferrofluidEnabled, bool tuningMenuActive)
        {
            return nativeVisible || (ferrofluidEnabled && tuningMenuActive);
        }

        private static void RefreshInfoButtonVisibility(RequirementsUI requirementsUi)
        {
            if (InfoButtonTransformField.GetValue(requirementsUi) is Transform infoButtonTransform)
            {
                infoButtonTransform.gameObject.SetActive(ShouldShowInfoButton(requirementsUi.ButtonGlowColor, _ferrofluidEnabled, IsTuningMenuActive()));
            }
        }

        // The scale includes the prototype's 0.9-unit width. Divide it out to recover the rendered scale per local unit.
        private static float EvaluateRenderedWorldUnitScale(float sourceWorldScale, float rectWidth)
        {
            return (Mathf.Max(sourceWorldScale, 0f) * 0.9f) / Mathf.Max(rectWidth, 0.0001f);
        }

        private static float EvaluateMagnetForwardWorldDistance(float sourceDistance, float sourceWorldScale)
        {
            return Mathf.Clamp(sourceDistance, 0f, 0.6f) * Mathf.Max(sourceWorldScale, 0f);
        }

        private static float EvaluateMagnetSurfaceGapWorldDistance(float sourceWorldScale)
        {
            return FerrofluidGpuSimulation.SourceMagnetSurfaceGap * Mathf.Max(sourceWorldScale, 0f);
        }

        private static float EvaluateFadeTargetAlpha(bool buttonFlaggedVisible, float backgroundCanvasAlpha)
        {
            return EvaluateFadeTargetAlpha(buttonFlaggedVisible, backgroundCanvasAlpha, false);
        }

        private static float EvaluateFadeTargetAlpha(bool buttonFlaggedVisible, float backgroundCanvasAlpha, bool tuningMenuActive)
        {
            return (buttonFlaggedVisible || tuningMenuActive) ? Mathf.Clamp01(backgroundCanvasAlpha) : 0f;
        }

        // A linear dissolve looks solid until roughly half the pixels disappear. A cubic fade makes it hide sooner.
        private static float EvaluateShaderFadeAlpha(float fadeAlpha, float fadeTargetAlpha)
        {
            return fadeTargetAlpha < fadeAlpha ? fadeAlpha * fadeAlpha * fadeAlpha : fadeAlpha;
        }

        private float EvaluateAncestorAlpha()
        {
            float ancestorAlpha = 1f;
            if (_ancestorCanvasGroups != null)
            {
                foreach (CanvasGroup canvasGroup in _ancestorCanvasGroups)
                {
                    if (canvasGroup == null)
                    {
                        continue;
                    }

                    ancestorAlpha *= canvasGroup.alpha;
                    if (ancestorAlpha <= 0.001f)
                    {
                        break;
                    }
                }
            }

            return ancestorAlpha;
        }

        private static float EvaluateRecessionLocalOffset(float parentForwardWorldScale)
        {
            return WorldRecessionDepth / Mathf.Max(parentForwardWorldScale, 0.0001f);
        }

        private static Vector3 EvaluateMagnetSurfacePosition(
            Vector3 worldIntersection,
            Vector3 surfaceWorld,
            Vector3 planeNormal,
            float sourceWorldScale)
        {
            return FerrofluidGpuSimulation.EvaluateMagnetSurfacePosition(
                worldIntersection,
                surfaceWorld,
                planeNormal,
                sourceWorldScale);
        }

        // Deactivate before deferred destruction so the effect stops simulating and drawing this frame.
        private static void DestroyExistingEffect()
        {
            ChromaGlsRequirementEffect? effect = _instance;
            if (effect == null)
            {
                return;
            }

            _instance = null;
            effect.RestoreSongCoreOutline();
            effect.gameObject.SetActive(false);
            Destroy(effect.gameObject);
        }

        private static void DestroyPreviewEffect()
        {
            ChromaGlsRequirementEffect? effect = _previewInstance;
            if (effect == null)
            {
                return;
            }

            _previewInstance = null;
            effect.gameObject.SetActive(false);
            Destroy(effect.gameObject);
        }

        private static void ReconcileEffectLifetime()
        {
            if (!_hasAttachedUnityState)
            {
                return;
            }

            if (!ShouldCreateEffect(_ferrofluidEnabled))
            {
                if (_lastRequirementsUi != null)
                {
                    RefreshInfoButtonVisibility(_lastRequirementsUi);
                }

                DestroyExistingEffect();
                DestroyPreviewEffect();
                return;
            }

            if (_lastRequirementsUi != null)
            {
                RefreshActiveState(_lastRequirementsUi);
            }
        }

        private static ChromaGlsRequirementEffect? Create(
            RequirementsUI? requirementsUi,
            RectTransform buttonBackgroundTransform,
            bool isPreview,
            Config config)
        {
            Graphic? buttonBackground = buttonBackgroundTransform.GetComponent<Graphic>();
            Canvas? buttonCanvas = buttonBackground != null
                ? buttonBackground.canvas
                : buttonBackgroundTransform.GetComponentInParent<Canvas>();
            PropertyInfo? skewProperty = buttonBackground != null
                ? AccessTools.Property(buttonBackground.GetType(), "skew")
                : null;
            float skew = buttonBackground != null && skewProperty?.GetValue(buttonBackground) is float value
                ? value
                : 0f;
            string roleName = isPreview
                ? "Settings Preview"
                : "Requirement Border";
            GameObject effectObject = new($"ChromaGLS Ferrofluid {roleName}", typeof(RectTransform));
            RectTransform effectTransform = (RectTransform)effectObject.transform;
            Transform effectParent = isPreview && buttonCanvas != null
                ? buttonCanvas.rootCanvas.transform
                : buttonBackgroundTransform;
            effectTransform.SetParent(effectParent, false);
            effectTransform.SetAsLastSibling();
            Vector2 effectSize = EvaluateEffectSize(isPreview, buttonBackgroundTransform.rect.size, _lastRequirementRectSize);
            effectTransform.anchorMin = isPreview ? new Vector2(0.5f, 0.5f) : Vector2.zero;
            effectTransform.anchorMax = isPreview ? new Vector2(0.5f, 0.5f) : Vector2.one;
            effectTransform.pivot = isPreview ? new Vector2(0.5f, 0.5f) : buttonBackgroundTransform.pivot;
            effectTransform.anchoredPosition3D = Vector3.zero;
            effectTransform.sizeDelta = isPreview ? effectSize : Vector2.zero;
            effectTransform.localRotation = Quaternion.identity;

            // The preview measurement already includes the ring scale.
            effectTransform.localScale = isPreview ? Vector3.one : Vector3.one * RequirementTorusRadiusScale;

            MeshFilter meshFilter = effectObject.AddComponent<MeshFilter>();
            MeshRenderer sceneRenderer = effectObject.AddComponent<MeshRenderer>();
            ChromaGlsRequirementEffect effect = effectObject.AddComponent<ChromaGlsRequirementEffect>();
            effect._requirementsUi = requirementsUi;
            effect._sceneRenderer = sceneRenderer;
            effect._buttonBackgroundGraphic = buttonBackground;
            effect._ancestorCanvasGroups = buttonBackgroundTransform.GetComponentsInParent<CanvasGroup>(true);
            effect._isPreview = isPreview;
            effect._skew = skew;
            effect.raycastTarget = false;
            effect.color = Color.white;
            effect.material = null;

            // Hide SongCore's cyan decorations while retaining their original state for teardown.
            if (!isPreview)
            {
                Transform buttonRoot = buttonBackgroundTransform.parent;
                effect._buttonBorderTransform = buttonRoot.Find("Border");
                effect._outlineWrapperTransform = buttonRoot.Find("OutlineWrapper");
                effect._buttonBorderWasActive = effect._buttonBorderTransform != null && effect._buttonBorderTransform.gameObject.activeSelf;
                effect._outlineWrapperWasActive = effect._outlineWrapperTransform != null && effect._outlineWrapperTransform.gameObject.activeSelf;
            }

            // Resolve HMUI curvature at creation. Only the matrices change while the effect is active.
            if (!isPreview
                && buttonCanvas != null
                && CurvedCanvasSettingsType != null
                && CurvedCanvasRadiusProperty != null)
            {
                Component? curvedCanvasSettings = ResolveCurvedCanvasSettings(buttonBackgroundTransform, buttonCanvas);
                if (curvedCanvasSettings != null && CurvedCanvasRadiusProperty.GetValue(curvedCanvasSettings) is float curvedUiRadius)
                {
                    effect._curvedUiRadius = curvedUiRadius;
                    effect._curvedCanvasTransform = curvedCanvasSettings.transform;
                    effect._hasCurvedCanvas = true;
                }
            }

            Camera? mainCamera = Camera.main;
            effect._viewerTransform = mainCamera != null ? mainCamera.transform : effectTransform;
            if (mainCamera != null)
            {
                if (isPreview)
                {
                    PositionTuningPreview(effectTransform, mainCamera.transform);
                }

                Vector3 viewerDirection = effectTransform.InverseTransformDirection(mainCamera.transform.position - effectTransform.position);
                effect._viewerLocalZSign = viewerDirection.z >= 0f ? 1f : -1f;
            }

            effectTransform.localPosition += new Vector3(0f, 0f, effect._viewerLocalZSign * RenderDepthBias);

            // Recess the torus behind the info button in world units, converting the offset through the parent scale.
            // The magnet follows the recessed transform.
            float recessionLocalZ = EvaluateRecessionLocalOffset(effectParent.TransformVector(Vector3.forward).magnitude);
            effectTransform.localPosition -= new Vector3(0f, 0f, effect._viewerLocalZSign * recessionLocalZ);
            if (isPreview && mainCamera != null)
            {
                // Correct the initial transform estimate with measured bounds so the preview matches the real button.
                effect.MatchPreviewWorldSize();
            }

            if (!isPreview)
            {
                // Preserve the measured scale on each axis, including the tube depth.
                _lastRequirementRenderedWorldUnitScale = new Vector3(
                    effectTransform.TransformVector(Vector3.right).magnitude,
                    effectTransform.TransformVector(Vector3.up).magnitude,
                    effectTransform.TransformVector(Vector3.forward).magnitude);
            }

            effect._magneticFieldPosition = effectTransform.position;
            effect._magnetController = FerrofluidMagnetController.Create();
            effect._simulation = FerrofluidGpuSimulation.TryCreate(meshFilter, sceneRenderer, config);
            if (effect._simulation == null)
            {
                effectObject.SetActive(false);
                Destroy(effectObject);
                return null;
            }

            effect._lastUpdateAt = Time.unscaledTime;
            if (isPreview)
            {
                _previewInstance = effect;
            }
            else
            {
                _instance = effect;
            }

            _hasAttachedUnityState = true;

            Plugin.Log.Info($"[ChromaGLS Ferrofluid] attached role={roleName} parent={effectParent.name} size={effectTransform.rect.size} worldScale={effectTransform.lossyScale} curveRadius={effect._curvedUiRadius:F2} viewerZ={effect._viewerLocalZSign:F0} sourceViewerDistance={_lastRequirementViewerDistance:F3}.");
            return effect;
        }

        // Size the preview from the SongCore button, since the BSML content rect stretches with the tab.
        private static Vector2 EvaluateEffectSize(bool isPreview, Vector2 parentSize, Vector2 measuredRequirementSize)
        {
            return isPreview ? measuredRequirementSize : parentSize;
        }

        private static Vector3 EvaluateTuningPreviewCameraPosition()
        {
            return new Vector3(-0.35f, 0.18f, 0.72f);
        }

        private static Vector3 EvaluateTuningPreviewWorldScale(Vector3 measuredRequirementWorldScale)
        {
            return measuredRequirementWorldScale;
        }

        private static bool IsUsableWorldSize(Vector3 worldSize)
        {
            return !float.IsNaN(worldSize.x)
                && !float.IsInfinity(worldSize.x)
                && !float.IsNaN(worldSize.y)
                && !float.IsInfinity(worldSize.y)
                && !float.IsNaN(worldSize.z)
                && !float.IsInfinity(worldSize.z)
                && worldSize.x > 0.00001f
                && worldSize.y > 0.00001f
                && worldSize.z > 0.00001f
                && worldSize.x < 10f
                && worldSize.y < 10f
                && worldSize.z < 10f;
        }

        // Correct width and height only. Canvas shear inflates the depth bounds, so retain the measured Z scale.
        private static Vector3 EvaluateWorldSizeScaleCorrection(Vector3 targetWorldSize, Vector3 previewWorldSize)
        {
            return new Vector3(
                targetWorldSize.x / Mathf.Max(previewWorldSize.x, 0.000001f),
                targetWorldSize.y / Mathf.Max(previewWorldSize.y, 0.000001f),
                1f);
        }

        private static Vector3 EvaluateWorldAxisScaleCorrection(Vector3 targetWorldScale, Vector3 renderedWorldAxisScale)
        {
            return new Vector3(
                targetWorldScale.x / Mathf.Max(renderedWorldAxisScale.x, 0.0001f),
                targetWorldScale.y / Mathf.Max(renderedWorldAxisScale.y, 0.0001f),
                targetWorldScale.z / Mathf.Max(renderedWorldAxisScale.z, 0.0001f));
        }

        private static void PositionTuningPreview(RectTransform effectTransform, Transform cameraTransform)
        {
            Vector3 previewCameraPosition = EvaluateTuningPreviewCameraPosition();

            Vector3 worldScale = EvaluateTuningPreviewWorldScale(_lastRequirementRenderedWorldUnitScale);
            Vector3 parentScale = effectTransform.parent.lossyScale;
            effectTransform.localScale = new Vector3(
                worldScale.x / Mathf.Max(Mathf.Abs(parentScale.x), 0.0001f),
                worldScale.y / Mathf.Max(Mathf.Abs(parentScale.y), 0.0001f),
                worldScale.z / Mathf.Max(Mathf.Abs(parentScale.z), 0.0001f));
            effectTransform.SetPositionAndRotation(
                cameraTransform.TransformPoint(previewCameraPosition),
                cameraTransform.rotation);
            Vector3 renderedWorldAxisScale = new(
                effectTransform.TransformVector(Vector3.right).magnitude,
                effectTransform.TransformVector(Vector3.up).magnitude,
                effectTransform.TransformVector(Vector3.forward).magnitude);
            Vector3 axisCorrection = EvaluateWorldAxisScaleCorrection(worldScale, renderedWorldAxisScale);
            effectTransform.localScale = Vector3.Scale(effectTransform.localScale, axisCorrection);
        }

        private static Component? ResolveCurvedCanvasSettings(Transform sourceTransform, Canvas canvas)
        {
            if (CurvedCanvasSettingsType == null || CurvedCanvasRadiusProperty == null)
            {
                return null;
            }

            Component? curvedCanvasSettings = sourceTransform.GetComponentInParent(CurvedCanvasSettingsType);
            if (curvedCanvasSettings != null)
            {
                return curvedCanvasSettings;
            }

            curvedCanvasSettings = canvas.GetComponent(CurvedCanvasSettingsType);
            Canvas rootCanvas = canvas.rootCanvas;
            if (curvedCanvasSettings == null && rootCanvas != null)
            {
                curvedCanvasSettings = rootCanvas.GetComponent(CurvedCanvasSettingsType);
            }

            if (curvedCanvasSettings != null)
            {
                return curvedCanvasSettings;
            }

            foreach (UnityEngine.Object loadedSetting in Resources.FindObjectsOfTypeAll(CurvedCanvasSettingsType))
            {
                if (loadedSetting is Component loadedComponent
                    && loadedComponent.gameObject.activeInHierarchy
                    && CurvedCanvasRadiusProperty.GetValue(loadedComponent) is float radius
                    && radius > 0f)
                {
                    return loadedComponent;
                }
            }

            return null;
        }

        // Measurements can run while Update is inactive, so initialize the curvature matrices here too.
        private void UpdateCurvedCanvasMatrices()
        {
            if (_hasCurvedCanvas)
            {
                _localToCurvedCanvasMatrix = _curvedCanvasTransform.worldToLocalMatrix * transform.localToWorldMatrix;
                _curvedCanvasToLocalMatrix = transform.worldToLocalMatrix * _curvedCanvasTransform.localToWorldMatrix;
            }
        }

        // Match the surface shader for magnet picking. The visible mesh is still generated on the GPU.
        private static Vector3 TransformRenderedPoint(
            Vector3 position,
            Rect rect,
            Vector2 pivot,
            float skew,
            bool hasCurvedCanvas,
            Matrix4x4 localToCurvedCanvasMatrix,
            Matrix4x4 curvedCanvasToLocalMatrix,
            float curvedUiRadius)
        {
            float normalizedY = (position.y - rect.yMin) / rect.height;
            position.x += skew * rect.height * (normalizedY - pivot.y);
            if (hasCurvedCanvas)
            {
                Vector3 canvasPosition = localToCurvedCanvasMatrix.MultiplyPoint3x4(position);
                float angle = canvasPosition.x / curvedUiRadius;
                float depthRadius = curvedUiRadius + canvasPosition.z;
                canvasPosition = new Vector3(
                    Mathf.Sin(angle) * depthRadius,
                    canvasPosition.y,
                    (Mathf.Cos(angle) * depthRadius) - curvedUiRadius);
                position = curvedCanvasToLocalMatrix.MultiplyPoint3x4(canvasPosition);
            }

            return position;
        }

        // Use the curved surface tangents to keep the cursor plane aligned under non-uniform canvas scaling.
        private static void EvaluateRenderedMagnetPlane(
            Rect rect,
            Vector2 pivot,
            float skew,
            bool hasCurvedCanvas,
            Matrix4x4 localToCanvas,
            Matrix4x4 canvasToLocal,
            float radius,
            Matrix4x4 localToWorld,
            float viewerSign,
            float forwardDistance,
            out Vector3 planePoint,
            out Vector3 planeNormal)
        {
            Vector3 center = new(rect.center.x, rect.center.y, 0f);
            const float step = 0.1f;
            Vector3 renderedCenter = TransformRenderedPoint(center, rect, pivot, skew, hasCurvedCanvas, localToCanvas, canvasToLocal, radius);
            Vector3 left = TransformRenderedPoint(center - (Vector3.right * step), rect, pivot, skew, hasCurvedCanvas, localToCanvas, canvasToLocal, radius);
            Vector3 right = TransformRenderedPoint(center + (Vector3.right * step), rect, pivot, skew, hasCurvedCanvas, localToCanvas, canvasToLocal, radius);
            Vector3 below = TransformRenderedPoint(center - (Vector3.up * step), rect, pivot, skew, hasCurvedCanvas, localToCanvas, canvasToLocal, radius);
            Vector3 above = TransformRenderedPoint(center + (Vector3.up * step), rect, pivot, skew, hasCurvedCanvas, localToCanvas, canvasToLocal, radius);
            Vector3 front = TransformRenderedPoint(center + (Vector3.forward * viewerSign * step), rect, pivot, skew, hasCurvedCanvas, localToCanvas, canvasToLocal, radius);
            Vector3 renderedRight = localToWorld.MultiplyVector(right - left);
            Vector3 renderedUp = localToWorld.MultiplyVector(above - below);
            Vector3 renderedNormal = Vector3.Cross(renderedRight, renderedUp);

            // Use the viewer plane when canvas shear makes the transformed X and Y tangents parallel.
            if (renderedNormal.sqrMagnitude > 0.0000000001f)
            {
                planeNormal = renderedNormal.normalized;
                if (Vector3.Dot(planeNormal, localToWorld.MultiplyVector(front - renderedCenter)) < 0f)
                {
                    planeNormal = -planeNormal;
                }
            }
            else
            {
                planeNormal = localToWorld.MultiplyVector(Vector3.forward * viewerSign).normalized;
            }

            planePoint = localToWorld.MultiplyPoint3x4(renderedCenter) + (planeNormal * forwardDistance);
        }

        // Measure in the torus's own rendered axes so headset rotation does not change its reported size.
        private bool TryMeasureBaseTorusWorldSize(out Vector3 worldSize)
        {
            UpdateCurvedCanvasMatrices();
            Rect rect = rectTransform.rect;
            FerrofluidGpuSimulation.Geometry geometry = FerrofluidGpuSimulation.EvaluateGeometry(rect);
            Vector3 right = transform.localToWorldMatrix.MultiplyVector(Vector3.right).normalized;
            Vector3 upCandidate = transform.localToWorldMatrix.MultiplyVector(Vector3.up).normalized;
            Vector3 up = (upCandidate - (right * Vector3.Dot(upCandidate, right))).normalized;
            Vector3 forward = Vector3.Cross(right, up).normalized;
            Vector3 transformedForward = transform.localToWorldMatrix.MultiplyVector(Vector3.forward);
            if (Vector3.Dot(forward, transformedForward) < 0f)
            {
                forward = -forward;
            }

            Vector3 minimum = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 maximum = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            const int crossSectionSamples = 16;
            for (int perimeterSample = 0; perimeterSample < MagnetPerimeterSamples; perimeterSample++)
            {
                float fraction = (float)perimeterSample / MagnetPerimeterSamples;
                Vector2 center = FerrofluidGpuSimulation.EvaluateRoundedRectangleCenterline(
                    fraction,
                    rect.center,
                    geometry.HalfExtents,
                    geometry.CornerRadius);
                Vector2 outward = FerrofluidGpuSimulation.EvaluateRoundedRectangleOutward(
                    center,
                    rect.center,
                    geometry.HalfExtents,
                    geometry.CornerRadius);
                for (int crossSection = 0; crossSection < crossSectionSamples; crossSection++)
                {
                    float angle = ((float)crossSection / crossSectionSamples) * Mathf.PI * 2f;
                    float radialOffset = FerrofluidGpuSimulation.RadialCenter
                        + (FerrofluidGpuSimulation.RadialRadius * Mathf.Cos(angle));
                    Vector3 localPosition = new(
                        center.x + (outward.x * radialOffset),
                        center.y + (outward.y * radialOffset),
                        _viewerLocalZSign * (rect.width / 6f) * Mathf.Sin(angle));
                    localPosition = TransformRenderedPoint(
                        localPosition,
                        rect,
                        rectTransform.pivot,
                        _skew,
                        _hasCurvedCanvas,
                        _localToCurvedCanvasMatrix,
                        _curvedCanvasToLocalMatrix,
                        _curvedUiRadius);
                    Vector3 worldOffset = transform.TransformPoint(localPosition) - transform.position;
                    Vector3 projected = new(
                        Vector3.Dot(worldOffset, right),
                        Vector3.Dot(worldOffset, up),
                        Vector3.Dot(worldOffset, forward));
                    minimum = Vector3.Min(minimum, projected);
                    maximum = Vector3.Max(maximum, projected);
                }
            }

            worldSize = maximum - minimum;
            return IsUsableWorldSize(worldSize);
        }

        private static void CaptureRequirementRectSize(Vector2 rectSize)
        {
            _lastRequirementRectSize = rectSize;
        }

        private void CaptureRequirementWorldSize()
        {
            CaptureRequirementRectSize(rectTransform.rect.size);

            // Capture after layout settles, immediately before moving the renderer to the settings preview.
            _lastRequirementRenderedWorldUnitScale = new Vector3(
                transform.TransformVector(Vector3.right).magnitude,
                transform.TransformVector(Vector3.up).magnitude,
                transform.TransformVector(Vector3.forward).magnitude);
            if (!TryMeasureBaseTorusWorldSize(out Vector3 worldSize))
            {
                Plugin.Log.Error($"[ChromaGLS Ferrofluid Size] rejected requirement worldSize={worldSize}; last valid target retained.");
                return;
            }

            _lastRequirementWorldSize = worldSize;
            _hasRequirementWorldSize = true;
        }

        // Limit size correction to three passes so repeated menu transitions cannot amplify a bad measurement.
        private void MatchPreviewWorldSize()
        {
            if (!_hasRequirementWorldSize
                || !TryMeasureBaseTorusWorldSize(out Vector3 beforeSize))
            {
                Plugin.Log.Error("[ChromaGLS Ferrofluid Size] no valid measured requirement world size was available; preview retained fallback placement.");
                return;
            }

            Vector3 currentSize = beforeSize;
            for (int iteration = 0; iteration < 3; iteration++)
            {
                if (!IsUsableWorldSize(currentSize))
                {
                    Plugin.Log.Error($"[ChromaGLS Ferrofluid Size] iteration {iteration} produced invalid worldSize={currentSize}; further correction stopped.");
                    break;
                }

                Vector3 correction = EvaluateWorldSizeScaleCorrection(_lastRequirementWorldSize, currentSize);
                correction.x = Mathf.Clamp(correction.x, 0.25f, 4f);
                correction.y = Mathf.Clamp(correction.y, 0.25f, 4f);
                correction.z = Mathf.Clamp(correction.z, 0.25f, 4f);
                transform.localScale = Vector3.Scale(transform.localScale, correction);
                if (!TryMeasureBaseTorusWorldSize(out currentSize))
                {
                    break;
                }
            }
        }

        // Match the shader's rounded-corner coordinates with 256 centerline samples.
        private bool TryMapMagnet(
            Vector3 worldIntersection,
            Vector3 planeNormal,
            out Vector2 pointer,
            out float targetStrength,
            out Vector3 magnetPosition,
            out float openingWeight,
            out Vector3 openingDirection)
        {
            magnetPosition = default;
            openingWeight = 0f;
            openingDirection = default;
            Rect rect = rectTransform.rect;
            FerrofluidGpuSimulation.Geometry geometry = FerrofluidGpuSimulation.EvaluateGeometry(rect);
            float closestDistanceSquared = float.PositiveInfinity;
            float closestFraction = 0f;
            Vector2 closestCenter = default;
            for (int sample = 0; sample < MagnetPerimeterSamples; sample++)
            {
                float fraction = (float)sample / MagnetPerimeterSamples;
                Vector2 center = FerrofluidGpuSimulation.EvaluateRoundedRectangleCenterline(
                    fraction,
                    rect.center,
                    geometry.HalfExtents,
                    geometry.CornerRadius);
                Vector3 renderedCenter = TransformRenderedPoint(
                    new Vector3(center.x, center.y, 0f),
                    rect,
                    rectTransform.pivot,
                    _skew,
                    _hasCurvedCanvas,
                    _localToCurvedCanvasMatrix,
                    _curvedCanvasToLocalMatrix,
                    _curvedUiRadius);
                float distanceSquared = (transform.TransformPoint(renderedCenter) - worldIntersection).sqrMagnitude;
                if (distanceSquared < closestDistanceSquared)
                {
                    closestDistanceSquared = distanceSquared;
                    closestFraction = fraction;
                    closestCenter = center;
                }
            }

            Vector2 outward = FerrofluidGpuSimulation.EvaluateRoundedRectangleOutward(
                closestCenter,
                rect.center,
                geometry.HalfExtents,
                geometry.CornerRadius);
            Vector3 centerLocal = TransformRenderedPoint(
                new Vector3(closestCenter.x, closestCenter.y, 0f),
                rect,
                rectTransform.pivot,
                _skew,
                _hasCurvedCanvas,
                _localToCurvedCanvasMatrix,
                _curvedCanvasToLocalMatrix,
                _curvedUiRadius);
            Vector3 outwardLocal = TransformRenderedPoint(
                new Vector3(closestCenter.x + outward.x, closestCenter.y + outward.y, 0f),
                rect,
                rectTransform.pivot,
                _skew,
                _hasCurvedCanvas,
                _localToCurvedCanvasMatrix,
                _curvedCanvasToLocalMatrix,
                _curvedUiRadius);
            Vector3 centerWorld = transform.TransformPoint(centerLocal);
            Vector3 outwardWorld = transform.TransformPoint(outwardLocal) - centerWorld;
            float radialUnitWorld = outwardWorld.magnitude;
            if (radialUnitWorld < 0.000001f)
            {
                pointer = default;
                targetStrength = 0f;
                return false;
            }

            outwardWorld /= radialUnitWorld;
            float radialDistance = Vector3.Dot(worldIntersection - centerWorld, outwardWorld);
            float radialCenterWorld = 0.2f * radialUnitWorld;
            float radialRadiusWorld = 1.38f * radialUnitWorld;

            // Remove the tube's local center offset before mapping the pointer across its diameter.
            float crossSectionAngle = FerrofluidGpuSimulation.EvaluateCrossSectionAngle(
                radialDistance,
                radialCenterWorld,
                radialRadiusWorld);

            openingWeight = FerrofluidGpuSimulation.EvaluateMagnetOpeningWeight(crossSectionAngle);

            Vector3 renderedRectCenter = TransformRenderedPoint(
                new Vector3(rect.center.x, rect.center.y, 0f),
                rect,
                rectTransform.pivot,
                _skew,
                _hasCurvedCanvas,
                _localToCurvedCanvasMatrix,
                _curvedCanvasToLocalMatrix,
                _curvedUiRadius);
            Vector3 rectCenterWorld = transform.TransformPoint(renderedRectCenter);
            Vector3 planarOpeningDirection = worldIntersection - rectCenterWorld;
            planarOpeningDirection -= planeNormal * Vector3.Dot(planarOpeningDirection, planeNormal);
            openingDirection = planarOpeningDirection.sqrMagnitude > 0.00000001f
                ? planarOpeningDirection.normalized
                : Vector3.zero;
            float radialWeight = Mathf.Cos(crossSectionAngle);
            float depthWeight = Mathf.Sin(crossSectionAngle);
            Vector3 surfacePosition = new(
                closestCenter.x + (outward.x * (0.2f + (1.38f * radialWeight))),
                closestCenter.y + (outward.y * (0.2f + (1.38f * radialWeight))),
                _viewerLocalZSign * (rect.width / 6f) * depthWeight);
            surfacePosition = TransformRenderedPoint(
                surfacePosition,
                rect,
                rectTransform.pivot,
                _skew,
                _hasCurvedCanvas,
                _localToCurvedCanvasMatrix,
                _curvedCanvasToLocalMatrix,
                _curvedUiRadius);

            float sourceWorldScale = (rect.width * transform.TransformVector(Vector3.right).magnitude) / 0.9f;
            Vector3 surfaceWorld = transform.TransformPoint(surfacePosition);

            magnetPosition = EvaluateMagnetSurfacePosition(
                worldIntersection,
                surfaceWorld,
                planeNormal,
                sourceWorldScale);
            float magnetSurfaceDistance = Vector3.Distance(magnetPosition, surfaceWorld);
            float normalizedMagnetDistance = magnetSurfaceDistance
                / Mathf.Max(0.3f * sourceWorldScale, 0.0001f);
            targetStrength = 1f / (1f + (normalizedMagnetDistance * normalizedMagnetDistance));
            pointer = new Vector2(
                (closestFraction * 2f) - 1f,
                ((crossSectionAngle / (Mathf.PI * 2f)) * 2f) - 1f);
            return true;
        }

        private static bool ContainsCapability(string[]? values)
        {
            if (values == null)
            {
                return false;
            }

            foreach (string value in values)
            {
                if (string.Equals(value, CapabilityName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // Cache these fields per effect because SongCore moved their containing types between versions.
        private bool SelectedDifficultyHasChromaGls()
        {
            if (_requirementsUi == null)
            {
                return false;
            }

            object? difficultyData = DifficultyDataField.GetValue(_requirementsUi);
            if (difficultyData == null)
            {
                return false;
            }

            _additionalDifficultyDataField ??= AccessTools.Field(difficultyData.GetType(), "additionalDifficultyData");
            object? additionalDifficultyData = _additionalDifficultyDataField.GetValue(difficultyData);
            if (additionalDifficultyData == null)
            {
                return false;
            }

            _requirementsField ??= AccessTools.Field(additionalDifficultyData.GetType(), "_requirements");
            _suggestionsField ??= AccessTools.Field(additionalDifficultyData.GetType(), "_suggestions");
            return ContainsCapability(_requirementsField.GetValue(additionalDifficultyData) as string[])
                || ContainsCapability(_suggestionsField.GetValue(additionalDifficultyData) as string[]);
        }

        // Hide SongCore's outline only after the ChromaGLS effect initializes successfully.
        private void ApplyVisibility()
        {
            bool effectActive = ShouldShowEffect(_ferrofluidEnabled, _hasChromaGlsMetadata, IsTuningMenuActive());
            SetSongCoreOutlineActive(!effectActive);
            gameObject.SetActive(effectActive);
        }

        private void SetSongCoreOutlineActive(bool active)
        {
            if (_buttonBorderTransform != null)
            {
                _buttonBorderTransform.gameObject.SetActive(active && _buttonBorderWasActive);
            }

            if (_outlineWrapperTransform != null)
            {
                _outlineWrapperTransform.gameObject.SetActive(active && _outlineWrapperWasActive);
            }
        }

        private void RestoreSongCoreOutline()
        {
            if (_buttonBorderTransform != null)
            {
                _buttonBorderTransform.gameObject.SetActive(_buttonBorderWasActive);
            }

            if (_outlineWrapperTransform != null)
            {
                _outlineWrapperTransform.gameObject.SetActive(_outlineWrapperWasActive);
            }
        }
    }
#pragma warning restore SA1204
}
