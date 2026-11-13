using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Parser;
using HMUI;
using JetBrains.Annotations;
using UnityEngine;

namespace ChromaGLS.Settings
{
    internal sealed class ParabolicFloorLightSettingsUI : System.IDisposable
    {
        private const string ModalResourceName = "ChromaGLS.Settings.parabolic-floor-light.bsml";
        private static readonly IList<object> LightTypeOptions = new List<object> { MenuLightType.Floor, MenuLightType.Parabola, MenuLightType.Square, MenuLightType.Cube, MenuLightType.Sphere, MenuLightType.Circle };

        private readonly Config _config;
        private readonly ParabolicFloorLight _light;
        private readonly MenuLightEditSession _session;
        private readonly MenuLightListView _list;
        private readonly MenuLightDefinition _fallback;
        private GameObject? _markupRoot;
        private BSMLParserParams? _parserParams;
        private RectTransform? _anchor;
        private bool _loading;

        [UIObject("parabola-controls")]
        private GameObject? _parabolaControls;

        [UIObject("parabola-light-modal")]
        private GameObject? _modalObject;

        [UIObject("size-controls")]
        private GameObject? _sizeControls;

        [UIObject("floor-glow-controls")]
        private GameObject? _floorGlowControls;

        [UIObject("free-pose-controls")]
        private GameObject? _freePoseControls;

        [UIObject("material-width-controls")]
        private GameObject? _materialWidthControls;

        [UIObject("floor-height-controls")]
        private GameObject? _floorHeightControls;

        [UIObject("underside-depth-controls")]
        private GameObject? _undersideDepthControls;

        [UIObject("frame-width-hint")]
        private GameObject? _frameWidthHint;

        internal ParabolicFloorLightSettingsUI(Config config, ParabolicFloorLight light)
            : this(config, light, null)
        {
        }

        internal ParabolicFloorLightSettingsUI(Config config, ParabolicFloorLight light, FerrofluidReflectionDownloadUI? packUI)
        {
            _config = config;
            _light = light;
            _session = new MenuLightEditSession(light.Store, light.Preview, light.Restore, light.Synchronize);
            _list = new MenuLightListView(_config, light, light.Store, OpenEditor, packUI);
            _fallback = light.Store.CreateDefault(MenuLightType.Parabola);
        }

        private MenuLightDefinition Current => _session.IsEditing ? _session.Working : _fallback;

        [UIValue("light-enabled")]
        private bool EnabledValue
        {
            get => Current.Enabled;
            set => PreviewLight(def => def.Enabled = value);
        }

        [UIValue("light-type")]
        private MenuLightType TypeValue
        {
            get => Current.Type;
            set
            {
                if (_loading)
                    return;

                PreviewLight(def => def.Type = value);
                RefreshControls();
            }
        }

        [UIValue("light-types")]
        private IList<object> TypeValues => LightTypeOptions;

        [UIValue("light-height")]
        private float HeightValue
        {
            get => ParabolicFloorLightSliderMapping.EvaluateHeightSlider(Current.Height);
            set => PreviewLight(def => def.Height = ParabolicFloorLightSliderMapping.EvaluateHeight(value));
        }

        [UIValue("light-depth")]
        private float DepthValue
        {
            get => Current.Depth;
            set => PreviewLight(def => def.Depth = value);
        }

        [UIValue("floor-height")]
        private float FloorHeightValue
        {
            get => Current.Height;
            set => PreviewLight(def => def.Height = value);
        }

        [UIValue("light-size")]
        private float SizeValue
        {
            get => Current.Size;
            set => PreviewLight(def => def.Size = value);
        }

        [UIValue("light-end-length")]
        private float EndLengthValue
        {
            get => Current.EndLength;
            set => PreviewLight(def => def.EndLength = value);
        }

        [UIValue("light-fade-start")]
        private float FadeStartValue
        {
            get => Current.FadeStart;
            set => PreviewLight(def => def.FadeStart = value);
        }

        [UIValue("parabola-offset-x")]
        private float PositionXValue
        {
            get => Current.PositionX;
            set => PreviewLight(def => def.PositionX = value);
        }

        [UIValue("parabola-offset-z")]
        private float PositionZValue
        {
            get => Current.PositionZ;
            set => PreviewLight(def => def.PositionZ = value);
        }

        [UIValue("light-rotation-x")]
        private float RotationXValue
        {
            get => Current.RotationX;
            set => PreviewLight(def => def.RotateAxis(0, value));
        }

        [UIValue("parabola-rotation")]
        private float RotationYValue
        {
            get => Current.RotationY;
            set => PreviewLight(def => def.RotateAxis(1, value));
        }

        [UIValue("light-rotation-z")]
        private float RotationZValue
        {
            get => Current.RotationZ;
            set => PreviewLight(def => def.RotateAxis(2, value));
        }

        [UIValue("parabola-brightness")]
        private float ParabolaBrightness
        {
            get => ParabolicFloorLightSliderMapping.EvaluateIntensitySlider(Current.Appearance.Brightness);
            set => PreviewAppearance(s => s.WithBrightness(ParabolicFloorLightSliderMapping.EvaluateIntensity(value)));
        }

        [UIValue("parabola-red")]
        private float ParabolaRed
        {
            get => ParabolicFloorLightSliderMapping.EvaluateColorIntensitySlider(Current.Appearance.Red);
            set => PreviewAppearance(s => s.WithRed(ParabolicFloorLightSliderMapping.EvaluateColorIntensity(value)));
        }

        [UIValue("parabola-green")]
        private float ParabolaGreen
        {
            get => ParabolicFloorLightSliderMapping.EvaluateColorIntensitySlider(Current.Appearance.Green);
            set => PreviewAppearance(s => s.WithGreen(ParabolicFloorLightSliderMapping.EvaluateColorIntensity(value)));
        }

        [UIValue("parabola-blue")]
        private float ParabolaBlue
        {
            get => ParabolicFloorLightSliderMapping.EvaluateColorIntensitySlider(Current.Appearance.Blue);
            set => PreviewAppearance(s => s.WithBlue(ParabolicFloorLightSliderMapping.EvaluateColorIntensity(value)));
        }

        [UIValue("parabola-width")]
        private float ParabolaWidth
        {
            get => ParabolicFloorLightSliderMapping.EvaluateWidthScaleSlider(Current.Appearance.WidthScale);
            set => PreviewAppearance(s => s.WithWidthScale(ParabolicFloorLightSliderMapping.EvaluateWidthScale(value)));
        }

        [UIValue("parabola-light-width")]
        private float ParabolaLightWidth
        {
            get => ParabolicFloorLightSliderMapping.EvaluateLightWidthScaleSlider(Current.Appearance.LightWidthScale);
            set => PreviewAppearance(s => s.WithLightWidthScale(ParabolicFloorLightSliderMapping.EvaluateLightWidthScale(value)));
        }

        [UIValue("parabola-bloom-width")]
        private float ParabolaBloomWidth
        {
            get => ParabolicFloorLightSliderMapping.EvaluateBloomWidthScaleSlider(Current.Appearance.BloomWidthScale);
            set => PreviewAppearance(s => s.WithBloomWidthScale(ParabolicFloorLightSliderMapping.EvaluateBloomWidthScale(value)));
        }

        [UIValue("parabola-bloom-brightness")]
        private float ParabolaBloomBrightness
        {
            get => ParabolicFloorLightSliderMapping.EvaluateIntensitySlider(Current.Appearance.BloomBrightness);
            set => PreviewAppearance(s => s.WithBloomBrightness(ParabolicFloorLightSliderMapping.EvaluateIntensity(value)));
        }

        [UIValue("parabola-hdr-glow-threshold")]
        private float ParabolaHdrGlowThreshold
        {
            get => ParabolicFloorLightSliderMapping.EvaluateIntensitySlider(Current.Appearance.HdrGlowThreshold);
            set => PreviewAppearance(s => s.WithHdrGlowThreshold(ParabolicFloorLightSliderMapping.EvaluateIntensity(value)));
        }

        [UIValue("parabola-hdr-glow-width")]
        private float ParabolaHdrGlowWidth
        {
            get => ParabolicFloorLightSliderMapping.EvaluateHdrGlowWidthScaleSlider(Current.Appearance.HdrGlowWidthScale);
            set => PreviewAppearance(s => s.WithHdrGlowWidthScale(ParabolicFloorLightSliderMapping.EvaluateHdrGlowWidthScale(value)));
        }

        [UIValue("parabola-hdr-glow-brightness")]
        private float ParabolaHdrGlowBrightness
        {
            get => ParabolicFloorLightSliderMapping.EvaluateIntensitySlider(Current.Appearance.HdrGlowBrightness);
            set => PreviewAppearance(s => s.WithHdrGlowBrightness(ParabolicFloorLightSliderMapping.EvaluateIntensity(value)));
        }

        internal void SetEnabled(bool enabled)
        {
            _config.ParabolicFloorLightEnabled = enabled;
            _light.SetEnabled(enabled);
        }

        // Anchors the nested Tweak button's right edge two units left of the switch's left edge;
        // the SwitchView rect is the row's real switch, so this holds regardless of row width.
        internal static void PositionTweakButtonLeftOfSwitch(RectTransform buttonRect, RectTransform switchRect)
        {
            buttonRect.SetParent(switchRect, false);
            buttonRect.anchorMin = new Vector2(0f, 0.5f);
            buttonRect.anchorMax = new Vector2(0f, 0.5f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(-2f, 0f);
            buttonRect.sizeDelta = new Vector2(18f, 6f);
            if (buttonRect.TryGetComponent(out UnityEngine.UI.LayoutElement layout))
                layout.ignoreLayout = true;
            if (buttonRect.TryGetComponent(out UnityEngine.UI.ContentSizeFitter fitter))
            {
                fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
            }
        }

        internal void Open(RectTransform anchor)
        {
            _anchor = anchor;
            _session.Cancel();
            HideEditorNative();
            _list.Open(anchor.transform);
        }

        private void OpenEditor(string? id) => BeginEditor(id, () => _parserParams?.EmitEvent("show-parabola"));

        internal void BeginEditor(string? id, Action showModal)
        {
            if (id == null && !_list.CanAdd)
                return;

            _session.Cancel();
            _list.Hide();
            if (_markupRoot == null && _anchor != null)
            {
                _markupRoot = new GameObject("ChromaGLS Menu Lights Editor", typeof(RectTransform));
                _markupRoot.transform.SetParent(_anchor, false);
                ParseModal();
            }

            showModal();
            if (id == null)
                _session.OpenNew(MenuLightType.Parabola);
            else
                _session.OpenExisting(id);

            RefreshControls();
        }

        internal void CloseWithoutSaving()
        {
            if (!_session.IsEditing && !_list.IsOpen)
                return;

            _session.Cancel();
            HideEditorNative();
            _list.Hide();
        }

        internal void CloseForGameplay()
        {
            _session.Cancel();
            _list.CloseForGameplay();
            TeardownEditor();
            _anchor = null;
        }

        public void Dispose() => CloseForGameplay();

        private void TeardownEditor()
        {
            if (_modalObject != null)
            {
                if (_modalObject.TryGetComponent(out ModalView modalView))
                    modalView.Hide(false);
                UnityEngine.Object.Destroy(_modalObject);
                _modalObject = null;
            }

            _parserParams = null;
            if (_markupRoot != null)
            {
                UnityEngine.Object.Destroy(_markupRoot);
                _markupRoot = null;
            }
        }

        private void ParseModal()
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ModalResourceName);
            if (stream == null)
            {
                Plugin.Log.Warn("[ChromaGLS Menu Lights] the editor markup is missing; [Tweak] will do nothing.");
                return;
            }

            using StreamReader reader = new(stream);
#if PRE_V1_40_8
            _parserParams = BSMLParser.instance.Parse(reader.ReadToEnd(), _markupRoot, this);
#else
            _parserParams = BSMLParser.Instance.Parse(reader.ReadToEnd(), _markupRoot, this);
#endif
            if (_modalObject != null && _modalObject.GetComponent<ParabolicFloorLightAnchorLifecycle>() == null)
                _modalObject.AddComponent<ParabolicFloorLightAnchorLifecycle>().Initialize(this);
        }

        private void HideEditorNative()
        {
            if (_modalObject != null && _modalObject.TryGetComponent(out ModalView modalView))
                modalView.Hide(false);
        }

        private void PreviewAppearance(Func<ParabolicFloorLightSettings, ParabolicFloorLightSettings> update)
        {
            if (_loading || !_session.IsEditing)
                return;

            _session.Preview(def => def.Appearance = update(def.Appearance));
        }

        private void PreviewLight(Action<MenuLightDefinition> update)
        {
            if (_loading || !_session.IsEditing)
                return;

            _session.Preview(update);
        }

        private void RefreshControls()
        {
            _loading = true;
            _parserParams?.EmitEvent("refresh-parabola");
            MenuLightDefinition def = Current;
            if (_parabolaControls != null)
                _parabolaControls.SetActive(def.Type is MenuLightType.Parabola or MenuLightType.Floor);
            if (_sizeControls != null)
                _sizeControls.SetActive(def.Type is not MenuLightType.Parabola and not MenuLightType.Floor);
            if (_materialWidthControls != null)
                _materialWidthControls.SetActive(def.Type is MenuLightType.Parabola or MenuLightType.Floor or MenuLightType.Square or MenuLightType.Circle);
            if (_floorGlowControls != null)
                _floorGlowControls.SetActive(def.Type == MenuLightType.Floor);
            if (_freePoseControls != null)
                _freePoseControls.SetActive(def.Type != MenuLightType.Floor);
            if (_floorHeightControls != null)
                _floorHeightControls.SetActive(def.Type == MenuLightType.Floor);
            if (_undersideDepthControls != null)
                _undersideDepthControls.SetActive(def.Type != MenuLightType.Floor);
            if (_frameWidthHint != null)
                _frameWidthHint.SetActive(def.Type is MenuLightType.Square or MenuLightType.Circle);
            _loading = false;
        }

        private void ReturnToList()
        {
            HideEditorNative();
            if (_anchor != null)
                _list.Open(_anchor.transform);
        }

        [UIAction("parabola-cancel")]
        [UsedImplicitly]
        private void HandleCancel()
        {
            _session.Cancel();
            ReturnToList();
        }

        [UIAction("parabola-reset")]
        [UsedImplicitly]
        private void HandleReset()
        {
            _session.Reset();
            RefreshControls();
        }

        [UIAction("parabola-save")]
        [UsedImplicitly]
        private void HandleSave()
        {
            _session.Save();
            ReturnToList();
        }

        private string FormatIntensity(float value) => ParabolicFloorLightSliderMapping.EvaluateIntensity(value).ToString("F2");

        private string FormatColorIntensity(float value) => ParabolicFloorLightSliderMapping.EvaluateColorIntensity(value).ToString("F2");

        private string FormatWidth(float value) => ParabolicFloorLightSliderMapping.EvaluateWidthScale(value).ToString("F2");

        private string FormatLightWidth(float value) => ParabolicFloorLightSliderMapping.EvaluateLightWidthScale(value).ToString("0.00#");

        private string FormatBloomWidth(float value) => ParabolicFloorLightSliderMapping.EvaluateBloomWidthScale(value).ToString("F2") + "x";

        private string FormatHdrGlowWidth(float value) => (ParabolicFloorLightSliderMapping.EvaluateHdrGlowWidthScale(value) * 100f).ToString("F0") + "%";

        private string FormatOffset(float value) => value.ToString("F2");

        private string FormatRotation(float value) => value.ToString("F0");

        private string FormatDepth(float value) => value.ToString("0.000");

        private string FormatHeight(float value) => ParabolicFloorLightSliderMapping.EvaluateHeight(value).ToString("0.000");

        private string FormatEndLength(float value) => value <= 0f ? "Automatic" : value.ToString("F2");
    }

    internal sealed class ParabolicFloorLightAnchorLifecycle : MonoBehaviour
    {
        private ParabolicFloorLightSettingsUI? _controller;

        internal void Initialize(ParabolicFloorLightSettingsUI controller) => _controller = controller;

        private void OnDisable() => _controller?.CloseWithoutSaving();
    }
}
