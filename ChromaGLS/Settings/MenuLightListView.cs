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
using UnityEngine.UI;

namespace ChromaGLS.Settings
{
    internal sealed class MenuLightListView : System.IDisposable
    {
        private const string ListResourceName = "ChromaGLS.Settings.menu-lights.bsml";
        private const string RowResourceName = "ChromaGLS.Settings.menu-light-row.bsml";

        private static readonly IList<object> ReflectionQualityOptions = new List<object> { ReflectionQuality.None, ReflectionQuality.Low, ReflectionQuality.High };

        private readonly Config _config;
        private readonly ParabolicFloorLight _light;
        private readonly MenuLightStore _store;
        private readonly Action<string?> _openEditor;
        private readonly FerrofluidReflectionDownloadUI? _packUI;
        private readonly List<MenuLightRow> _rowHosts = new();
        private BSMLParserParams? _parserParams;
        private GameObject? _root;

        [UIObject("menu-lights-modal")]
        private GameObject? _modalObject;

        [UIObject("menu-light-rows")]
        private GameObject? _rowContainer;

        [UIComponent("add-light")]
        private Button? _addButton;

        [UIComponent("reflection-quality-dropdown")]
        private BeatSaberMarkupLanguage.Components.Settings.DropDownListSetting? _qualityDropdown;

        internal MenuLightListView(Config config, ParabolicFloorLight light, MenuLightStore store, Action<string?> openEditor)
            : this(config, light, store, openEditor, null)
        {
        }

        internal MenuLightListView(Config config, ParabolicFloorLight light, MenuLightStore store, Action<string?> openEditor, FerrofluidReflectionDownloadUI? packUI)
        {
            _config = config;
            _light = light;
            _store = store;
            _openEditor = openEditor;
            _packUI = packUI;
            if (_packUI != null)
                _packUI.QualityChanged += HandleQualityChanged;
        }

        [UIValue("reflection-quality-options")]
        private IList<object> ReflectionQualityOptionValues => ReflectionQualityOptions;

        [UIValue("reflection-quality")]
        private ReflectionQuality ReflectionQualityValue
        {
            get => _config.ReflectionsQuality;
            set
            {
                if (_packUI != null && _root != null)
                    _packUI.RequestQuality(value, (RectTransform)_root.transform);
                else
                    _light.SetReflectionQuality(value);

                // A failed or declined attempt stays None with no QualityChanged; always pull the
                // dropdown back to the config value so the label cannot lie.
                RefreshQuality();
            }
        }

        internal bool IsOpen { get; private set; }

        internal bool CanAdd => _store.Lights.Count < MenuLightStore.MaximumLights;

        private void HandleQualityChanged(ReflectionQuality quality) => RefreshQuality();

        private void RefreshQuality()
        {
            if (_qualityDropdown != null)
                _qualityDropdown.ReceiveValue();
        }

        internal void Open(Transform parent)
        {
            if (_root == null)
            {
                _root = new GameObject("Menu Lights List", typeof(RectTransform));
                _root.transform.SetParent(parent, false);
                using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ListResourceName);
                if (stream == null)
                    return;

                using StreamReader reader = new(stream);
#if PRE_V1_40_8
                _parserParams = BSMLParser.instance.Parse(reader.ReadToEnd(), _root, this);
#else
                _parserParams = BSMLParser.Instance.Parse(reader.ReadToEnd(), _root, this);
#endif
            }

            if (_parserParams == null)
                return;

            Rebuild();
            RefreshQuality();
            _parserParams.EmitEvent("show-menu-lights");
            IsOpen = true;
        }

        internal void Hide()
        {
            IsOpen = false;
            if (_modalObject != null)
                _modalObject.GetComponent<ModalView>().Hide(false);
        }

        internal void Rebuild()
        {
            if (_rowContainer == null)
                return;

            string rowMarkup;
            using (Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(RowResourceName))
            {
                if (stream == null)
                    return;

                using StreamReader reader = new(stream);
                rowMarkup = reader.ReadToEnd();
            }

            foreach (MenuLightRow row in _rowHosts)
            {
                if (row.Host != null)
                {
                    row.Host.SetActive(false);
                    UnityEngine.Object.Destroy(row.Host);
                }
            }

            _rowHosts.Clear();
            int count = 0;
            foreach (MenuLightDefinition saved in _store.Lights)
            {
                if (count >= MenuLightStore.MaximumLights)
                    break;

                var row = new MenuLightRow(saved.Clone(), count, OpenEditor, _store, _light, Rebuild);
#if PRE_V1_40_8
                BSMLParser.instance.Parse(rowMarkup, _rowContainer, row);
#else
                BSMLParser.Instance.Parse(rowMarkup, _rowContainer, row);
#endif
                _rowHosts.Add(row);
                count++;
            }

            if (_addButton != null)
                _addButton.interactable = CanAdd;
        }

        internal void CloseForGameplay()
        {
            Hide();
            if (_root != null)
                UnityEngine.Object.Destroy(_root);

            if (_modalObject != null)
                UnityEngine.Object.Destroy(_modalObject);

            _root = null;
            _modalObject = null;
            _rowContainer = null;
            _addButton = null;
            _qualityDropdown = null;
            _parserParams = null;
            _rowHosts.Clear();
        }

        public void Dispose()
        {
            if (_packUI != null)
                _packUI.QualityChanged -= HandleQualityChanged;
            CloseForGameplay();
        }

        [UIAction("light-list-close")]
        [UsedImplicitly]
        private void CloseList() => Hide();

        [UIAction("light-add")]
        [UsedImplicitly]
        private void Add()
        {
            if (!CanAdd)
                return;

            Hide();
            _openEditor(null);
        }

        private void OpenEditor(string? id)
        {
            Hide();
            _openEditor(id);
        }
    }
}
