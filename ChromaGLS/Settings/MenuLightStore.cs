using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace ChromaGLS.Settings
{
    internal sealed class MenuLightStore
    {
        internal const int MaximumLights = 8;

        private readonly Config _config;
        private readonly List<MenuLightDefinition> _lights = new();

        internal MenuLightStore(Config config)
        {
            _config = config;
            string json = config.MenuLightsJson;
            if (string.IsNullOrWhiteSpace(json))
            {
                _lights.Add(MigrateLegacy(config));
            }
            else
            {
                MenuLightDefinition[]? loaded = JsonConvert.DeserializeObject<MenuLightDefinition[]>(json);
                if (loaded != null)
                {
                    foreach (MenuLightDefinition definition in loaded)
                    {
                        // Only the default floor parabola migrates to Floor; raised or tilted
                        // saved entries keep their Parabola type and full pose editing.
                        Vector3 normal = definition.GetRotation() * Vector3.up;
                        if (definition.Id == "legacy-parabola"
                            && definition.Type == MenuLightType.Parabola
                            && (Mathf.Approximately(definition.Height, ParabolicFloorLightGeometry.TopHeight)
                                || Mathf.Approximately(definition.Height, MenuLightDefinition.FloorHeight)
                                || Mathf.Approximately(definition.Height, 0.03f))
                            && Mathf.Approximately(normal.y, 1f))
                        {
                            definition.Type = MenuLightType.Floor;
                        }

                        definition.ConstrainToFloor();
                        _lights.Add(definition);
                    }
                }
            }
        }

        internal IReadOnlyList<MenuLightDefinition> Lights => _lights;

        internal MenuLightDefinition CreateDefault(MenuLightType type)
        {
            ParabolicFloorLightSettings appearance = type == MenuLightType.Floor
                ? ParabolicFloorLightSettings.FloorDefault
                : type == MenuLightType.Parabola
                ? ParabolicFloorLightSettings.Default
                : new ParabolicFloorLightSettings(
                    ParabolicFloorLightSettings.Default.Brightness,
                    ParabolicFloorLightSettings.Default.Red,
                    ParabolicFloorLightSettings.Default.Green,
                    ParabolicFloorLightSettings.Default.Blue,
                    1f,
                    0f,
                    0f,
                    0f,
                    ParabolicFloorLightSettings.Default.LightWidthScale,
                    ParabolicFloorLightSettings.Default.BloomWidthScale,
                    ParabolicFloorLightSettings.Default.BloomBrightness,
                    ParabolicFloorLightSettings.Default.HdrGlowThreshold,
                    ParabolicFloorLightSettings.Default.HdrGlowWidthScale,
                    ParabolicFloorLightSettings.Default.HdrGlowBrightness);

            var created = new MenuLightDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = type,
                Enabled = true,
                Appearance = appearance,
                EndLength = 0f,
                FadeStart = type == MenuLightType.Floor ? 0.13f : 0.45f,
                Height = type == MenuLightType.Floor ? 0.02f : ParabolicFloorLightGeometry.TopHeight,
            };
            created.ConstrainToFloor();
            return created;
        }

        internal void Upsert(MenuLightDefinition definition)
        {
            MenuLightDefinition clone = definition.Clone();
            clone.ConstrainToFloor();
            int existing = _lights.FindIndex(light => light.Id == clone.Id);
            if (existing >= 0)
            {
                _lights[existing] = clone;
            }
            else
            {
                if (_lights.Count >= MaximumLights)
                    throw new InvalidOperationException("At most eight menu lights can be saved.");
                _lights.Add(clone);
            }

            Persist();
        }

        internal void Remove(string id)
        {
            _lights.RemoveAll(light => light.Id == id);
            Persist();
        }

        private void Persist()
            => _config.MenuLightsJson = JsonConvert.SerializeObject(_lights);

        private static MenuLightDefinition MigrateLegacy(Config config)
        {
            ParabolicFloorLightSettings appearance = ParabolicFloorLightSettings.FromConfig(config);
            float yaw = appearance.Rotation;
            return new MenuLightDefinition
            {
                Id = "legacy-parabola",
                Type = MenuLightType.Floor,
                Height = MenuLightDefinition.FloorHeight,
                FadeStart = 0.13f,
                Enabled = true,
                Appearance = new ParabolicFloorLightSettings(
                    appearance.Brightness,
                    appearance.Red,
                    appearance.Green,
                    appearance.Blue,
                    appearance.WidthScale,
                    0f,
                    0f,
                    0f,
                    appearance.LightWidthScale,
                    appearance.BloomWidthScale,
                    appearance.BloomBrightness,
                    appearance.HdrGlowThreshold,
                    appearance.HdrGlowWidthScale,
                    appearance.HdrGlowBrightness),
                PositionX = appearance.OffsetX,
                PositionZ = appearance.OffsetZ,
                QuaternionY = Mathf.Sin(Mathf.DeltaAngle(0f, yaw) * Mathf.Deg2Rad * 0.5f),
                QuaternionW = Mathf.Cos(Mathf.DeltaAngle(0f, yaw) * Mathf.Deg2Rad * 0.5f),
                RotationY = Mathf.DeltaAngle(0f, yaw),
            };
        }
    }

    internal sealed class MenuLightEditSession
    {
        private readonly MenuLightStore _store;
        private readonly Action<MenuLightDefinition> _preview;
        private readonly Action<string> _restore;
        private readonly Action<IReadOnlyList<MenuLightDefinition>> _committed;

        internal MenuLightEditSession(
            MenuLightStore store,
            Action<MenuLightDefinition> preview,
            Action<string> restore,
            Action<IReadOnlyList<MenuLightDefinition>> committed)
        {
            _store = store;
            _preview = preview;
            _restore = restore;
            _committed = committed;
        }

        internal bool IsEditing { get; private set; }

        internal bool IsNew { get; private set; }

        internal MenuLightDefinition Working { get; private set; } = new();

        internal void OpenExisting(string id)
        {
            if (IsEditing)
                Cancel();

            MenuLightDefinition? saved = null;
            foreach (MenuLightDefinition light in _store.Lights)
            {
                if (light.Id == id)
                    saved = light;
            }

            if (saved == null)
                return;

            Working = saved.Clone();
            IsNew = false;
            IsEditing = true;
        }

        internal void OpenNew(MenuLightType type)
        {
            if (IsEditing)
                Cancel();

            Working = _store.CreateDefault(type);
            IsNew = true;
            IsEditing = true;
            _preview(Working.Clone());
        }

        internal void Preview(Action<MenuLightDefinition> edit)
        {
            if (!IsEditing)
                return;

            edit(Working);
            Working.ConstrainToFloor();
            _preview(Working.Clone());
        }

        internal void Reset()
        {
            if (!IsEditing)
                return;

            MenuLightDefinition defaults = _store.CreateDefault(Working.Type);
            defaults.Id = Working.Id;
            Working = defaults;
            _preview(Working.Clone());
        }

        internal void Save()
        {
            if (!IsEditing)
                return;

            _store.Upsert(Working);
            IsEditing = false;
            _committed(_store.Lights);
        }

        internal void Cancel()
        {
            if (!IsEditing)
                return;

            IsEditing = false;
            _restore(Working.Id);
        }
    }
}
