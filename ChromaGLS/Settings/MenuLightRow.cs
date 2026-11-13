using System;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using JetBrains.Annotations;
using UnityEngine;

namespace ChromaGLS.Settings
{
    internal sealed class MenuLightRow
    {
        private readonly MenuLightDefinition _saved;
        private readonly Action<string?> _openEditor;
        private readonly MenuLightStore _store;
        private readonly ParabolicFloorLight _light;
        private readonly Action _removed;
        private readonly MenuLightType? _preset;
        private readonly string _label;

        [UIObject("light-row")]
        private GameObject? _host;

        [UIObject("light-default-button")]
        [UsedImplicitly]
        private GameObject? _defaultButton;

        internal MenuLightRow(MenuLightDefinition saved, int index, Action<string?> openEditor, MenuLightStore store, ParabolicFloorLight light, Action removed)
        {
            _saved = saved;
            _openEditor = openEditor;
            _store = store;
            _light = light;
            _removed = removed;
            _preset = MenuLightStore.GetPresetType(_saved);
            _label = _saved.Id == MenuLightStore.RaisedParabolaPresetId
                ? $"{index + 1}. Upper ({_saved.Type})"
                : $"{index + 1}. {_saved.Type}";
            ShowDefault = _preset.HasValue && !_store.IsDefaultPreset(_saved);
        }

        internal GameObject? Host => _host;

        [UIValue("show-default")]
        [UsedImplicitly]
        internal bool ShowDefault { get; }

        [UIValue("light-label")]
        [UsedImplicitly]
        internal string Label => _label;

        [UIValue("light-enabled")]
        [UsedImplicitly]
        internal bool Enabled
        {
            get => _saved.Enabled;
            set
            {
                if (_saved.Enabled == value)
                    return;

                _saved.Enabled = value;
                _store.Upsert(_saved);
                _light.Synchronize(_store.Lights);
                _removed();
            }
        }

        [UIAction("light-tweak")]
        [UsedImplicitly]
        private void Tweak() => _openEditor(_saved.Id);

        internal void RefreshControls()
        {
            if (_defaultButton != null)
                _defaultButton.SetActive(ShowDefault);
        }

        [UIAction("light-default")]
        [UsedImplicitly]
        private void RestoreDefault()
        {
            if (_preset.HasValue && _store.RestoreDefaultPreset(_preset.Value, _saved.Id))
            {
                _light.Synchronize(_store.Lights);
                _removed();
            }
        }

        [UIAction("light-remove")]
        [UsedImplicitly]
        private void Remove()
        {
            _store.Remove(_saved.Id);
            _light.Synchronize(_store.Lights);
            _removed();
        }
    }
}
