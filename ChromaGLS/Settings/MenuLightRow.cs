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
        private readonly string _label;

        [UIObject("light-row")]
        private GameObject? _host;

        internal MenuLightRow(MenuLightDefinition saved, int index, Action<string?> openEditor, MenuLightStore store, ParabolicFloorLight light, Action removed)
        {
            _saved = saved;
            _openEditor = openEditor;
            _store = store;
            _light = light;
            _removed = removed;
            _label = $"{index + 1}. {_saved.Type}";
        }

        internal GameObject? Host => _host;

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
            }
        }

        [UIAction("light-tweak")]
        [UsedImplicitly]
        private void Tweak() => _openEditor(_saved.Id);

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
