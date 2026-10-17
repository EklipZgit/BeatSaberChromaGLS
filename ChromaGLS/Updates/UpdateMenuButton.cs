using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UI;
using VRUIControls;
using Zenject;

namespace ChromaGLS.Updates
{
    internal sealed class UpdateMenuButton : IDisposable
    {
        private const string BannerResourceName = "ChromaGLS.Updates.update-banner.bsml";
        private const float BorderThickness = 0.55f;

        private const string NoGlowMaterialName = "UINoGlow";

        private static readonly Vector2 BannerSize = new(76f, 22f);
        private static readonly Color BackgroundColor = new(0.015f, 0.015f, 0.02f, 1f);
        private static readonly Color BorderColor = new(1f, 0.05f, 0.05f, 1f);
        private static readonly Vector2 CenterAnchor = new(0.5f, 0.5f);

        private readonly MainMenuViewController _mainMenu;
        private readonly DiContainer _container;
        private readonly UpdateBannerPresenter _presenter;
        private GameObject? _host;
        private GameObject? _panel;
        private Sprite? _whiteSprite;
        private bool _disposed;

        [UIObject("update-banner-message")]
        private GameObject? _messageObject;

        [UIObject("update-banner-versions")]
        private GameObject? _versionsObject;

        [UIObject("update-banner-link")]
        private GameObject? _linkObject;

        [UsedImplicitly]
        private UpdateMenuButton(StartupUpdateCheck updateCheck, MainMenuViewController mainMenu, DiContainer container)
        {
            _mainMenu = mainMenu;
            _container = container;
            _presenter = new UpdateBannerPresenter(updateCheck, SetPanelVisible, OpenReleasePage);
            _mainMenu.didActivateEvent += HandleMenuActivated;
            if (_mainMenu.isActivated)
            {
                EnsureHost();
            }

            _ = InitializeBannerAsync();
        }

        public void Dispose()
        {
            _disposed = true;
            _mainMenu.didActivateEvent -= HandleMenuActivated;
            if (_host != null)
            {
                UnityEngine.Object.Destroy(_host);
                _host = null;
                _panel = null;
            }

            if (_whiteSprite != null)
            {
                UnityEngine.Object.Destroy(_whiteSprite);
                _whiteSprite = null;
            }

            _presenter.Dispose();
        }

        private async Task InitializeBannerAsync()
        {
            // Keep Unity's synchronization context here. MenuButtons refreshes Unity objects after the background request completes.
            await _presenter.InitializeAsync();
        }

        private void HandleMenuActivated(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            EnsureHost();
        }

        private void EnsureHost()
        {
            if (_host != null || _disposed)
            {
                return;
            }

            HMUI.Screen? mainScreen = _mainMenu.screen;
            foreach (HMUI.ScreenSystem candidate in Resources.FindObjectsOfTypeAll<HMUI.ScreenSystem>())
            {
                if (candidate.mainScreen == mainScreen)
                {
                    _host = CreateHost(candidate);
                    return;
                }
            }

            Plugin.Log?.Warn("[ChromaGLS] No screen system owns the main menu screen; the update banner cannot attach.");
        }

        private GameObject? CreateHost(HMUI.ScreenSystem screenSystem)
        {
            Material? noGlowMaterial = ResolveNoGlowMaterial();
            if (noGlowMaterial == null)
            {
                return null;
            }

            RectTransform leftScreen = (RectTransform)screenSystem.leftScreen.transform;
            GameObject host = new("ChromaGLS Update Banner", typeof(RectTransform));
            host.SetActive(false);
            Canvas canvas = host.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 20;

            // ImageView writes the curve radius through uv2, so the channel must exist before the text and images build their meshes.
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord2;
            HMUI.CurvedCanvasSettings hostCurve = host.AddComponent<HMUI.CurvedCanvasSettings>();
            hostCurve.raycastTarget = false;
            HMUI.CurvedCanvasSettings? sourceCurve = leftScreen.GetComponentInChildren<HMUI.CurvedCanvasSettings>(true);
            if (sourceCurve == null)
            {
                sourceCurve = screenSystem.GetComponentInChildren<HMUI.CurvedCanvasSettings>(true);
            }

            float curveRadius = sourceCurve == null ? 0f : sourceCurve.radius;
            hostCurve.SetRadius(curveRadius);
            _whiteSprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(0.5f, 0.5f));
            PositionBannerRoot((RectTransform)host.transform, leftScreen, screenSystem.transform, sourceCurve == null ? null : sourceCurve.transform);

            _panel = BuildPanel(host.transform, noGlowMaterial);

            // InstantiateComponent injects the native raycaster's PhysicsRaycasterWithCache while the host
            // is still inactive, so its OnEnable canvas caching sees a fully wired component.
            _container.InstantiateComponent<VRGraphicRaycaster>(host);
            UpdateBannerLifecycle lifecycle = host.AddComponent<UpdateBannerLifecycle>();
            lifecycle.Initialize(_presenter);
            SetLayerRecursively(host.transform, leftScreen.gameObject.layer);
            host.SetActive(true);
            return host;
        }

        internal static void PositionBannerRoot(RectTransform banner, RectTransform leftScreen, Transform screenSystem, Transform? curveSource)
        {
            banner.SetParent(leftScreen, false);
            banner.anchorMin = new Vector2(0.5f, 1f);
            banner.anchorMax = new Vector2(0.5f, 1f);
            banner.pivot = new Vector2(0.5f, 0f);
            banner.anchoredPosition = new Vector2(0f, 3f);
            banner.sizeDelta = BannerSize;
            banner.SetParent(screenSystem, true);
            if (curveSource == null)
            {
                return;
            }

            // The bend shader wraps vertices around the vertical line through the curved canvas origin,
            // so the host must keep the source rotation/scale and sit on that same axis to share its cylinder.
            banner.rotation = curveSource.rotation;
            Vector3 axisUp = curveSource.up;
            Vector3 desiredPosition = banner.position;
            banner.position = curveSource.position + (axisUp * Vector3.Dot(desiredPosition - curveSource.position, axisUp));

            // The pivot carries the authored left-screen x offset so the unwrapped rect center keeps it
            // while the root transform itself stays on the shared cylinder axis.
            float desiredUnwrappedX = curveSource.InverseTransformPoint(desiredPosition).x;
            Vector2 pivot = banner.pivot;
            pivot.x = 0.5f - (desiredUnwrappedX / BannerSize.x);
            banner.pivot = pivot;
            Vector3 parentScale = screenSystem.lossyScale;
            Vector3 sourceScale = curveSource.lossyScale;
            banner.localScale = new Vector3(
                parentScale.x == 0f ? sourceScale.x : sourceScale.x / parentScale.x,
                parentScale.y == 0f ? sourceScale.y : sourceScale.y / parentScale.y,
                parentScale.z == 0f ? sourceScale.z : sourceScale.z / parentScale.z);
        }

        private static Material? ResolveNoGlowMaterial()
        {
            foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (material.name == NoGlowMaterialName)
                {
                    return material;
                }
            }

            Plugin.Log?.Warn("[ChromaGLS] The UINoGlow material is not loaded; the update banner cannot suppress its bloom output.");
            return null;
        }

        private GameObject BuildPanel(Transform host, Material noGlowMaterial)
        {
            GameObject panel = new("Panel", typeof(RectTransform));
            panel.SetActive(false);
            RectTransform panelRect = (RectTransform)panel.transform;
            panelRect.SetParent(host, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            HMUI.ImageView background = panel.AddComponent<HMUI.ImageView>();
            ConfigureBannerImage(background, BackgroundColor, noGlowMaterial);
            background.raycastTarget = true;

            HMUI.ImageView[] borders =
            {
                CreateBorder(panelRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, BorderThickness), noGlowMaterial),
                CreateBorder(panelRect, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, BorderThickness), noGlowMaterial),
                CreateBorder(panelRect, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(BorderThickness, 0f), noGlowMaterial),
                CreateBorder(panelRect, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(BorderThickness, 0f), noGlowMaterial),
            };
            panel.AddComponent<UpdateBannerPulse>().Initialize(borders);

            LayoutBannerText(panelRect);
            return panel;
        }

        private void ConfigureBannerImage(HMUI.ImageView image, Color color, Material material)
        {
            // ImageView only produces the tessellated curved mesh while a sprite is assigned.
            image.sprite = _whiteSprite;
            image.color = color;
            image.raycastTarget = false;
            image.material = material;
        }

        private HMUI.ImageView CreateBorder(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta, Material material)
        {
            GameObject strip = new("Border", typeof(RectTransform));
            RectTransform rect = (RectTransform)strip.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = sizeDelta;
            HMUI.ImageView image = strip.AddComponent<HMUI.ImageView>();
            ConfigureBannerImage(image, BorderColor, material);
            return image;
        }

        private void LayoutBannerText(RectTransform panel)
        {
            GameObject markup = new("Markup", typeof(RectTransform));
            ParseBanner(markup);
            if (_messageObject == null || _versionsObject == null || _linkObject == null)
            {
                Plugin.Log?.Warn("[ChromaGLS] The update banner markup did not produce its expected text elements.");
                UnityEngine.Object.Destroy(markup);
                return;
            }

            PlaceText((RectTransform)_messageObject.transform, panel, new Vector2(0f, 6f), new Vector2(72f, 6f));
            PlaceText((RectTransform)_versionsObject.transform, panel, new Vector2(0f, 0f), new Vector2(72f, 6f));
            PlaceText((RectTransform)_linkObject.transform, panel, new Vector2(0f, -6f), new Vector2(72f, 6f));
            _messageObject.GetComponent<Graphic>().raycastTarget = false;
            _versionsObject.GetComponent<Graphic>().raycastTarget = false;
            _linkObject.GetComponent<Graphic>().raycastTarget = true;
            SeedVersionText();
            UnityEngine.Object.Destroy(markup);
        }

        private static void PlaceText(RectTransform text, RectTransform panel, Vector2 position, Vector2 size)
        {
            text.SetParent(panel, false);
            text.anchorMin = CenterAnchor;
            text.anchorMax = CenterAnchor;
            text.pivot = CenterAnchor;
            text.anchoredPosition = position;
            text.sizeDelta = size;
        }

        private void ParseBanner(GameObject markupRoot)
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BannerResourceName);
            if (stream == null)
            {
                return;
            }

            using StreamReader reader = new(stream);
#if PRE_V1_40_8
            BSMLParser.instance.Parse(reader.ReadToEnd(), markupRoot, this);
#else
            BSMLParser.Instance.Parse(reader.ReadToEnd(), markupRoot, this);
#endif
        }

        private void SetPanelVisible(bool visible)
        {
            if (_panel == null)
            {
                return;
            }

            if (visible)
            {
                SeedVersionText();
            }

            _panel.SetActive(visible);
        }

        private void SeedVersionText()
        {
            if (_versionsObject != null)
            {
                _versionsObject.GetComponent<TMPro.TMP_Text>().text = _presenter.VersionText;
            }
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root)
            {
                SetLayerRecursively(child, layer);
            }
        }

        [UIAction("open-releases")]
        [UsedImplicitly]
        private void OpenReleasesClicked()
        {
            _presenter.OpenReleases();
        }

        private void OpenReleasePage(string url)
        {
            Application.OpenURL(url);
        }
    }
}
