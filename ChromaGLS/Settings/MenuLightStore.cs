using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace ChromaGLS.Settings
{
    internal sealed class MenuLightStore
    {
        internal const int MaximumLights = 8;
        internal const string RaisedParabolaPresetId = "default-raised-parabola";

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

                        definition.ConstrainPose();
                        _lights.Add(definition);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                _lights.Add(CreateRaisedParabolaPreset());
                _config.MenuLightDefaultsVersion = 1;
            }
            else if (_config.MenuLightDefaultsVersion < 1)
            {
                // The persisted marker survives across saves so a deleted preset never
                // respawns and a full or deliberately empty list is left alone.
                bool hasPreset = _lights.Exists(definition => definition.Id == RaisedParabolaPresetId);
                if (_lights.Count > 0 && _lights.Count < MaximumLights && !hasPreset)
                {
                    _lights.Add(CreateRaisedParabolaPreset());
                    Persist();
                }

                _config.MenuLightDefaultsVersion = 1;
            }

            if (_config.MenuLightDefaultsVersion < 2)
            {
                // The default pair swapped colors once. Only entries still carrying the exact
                // shipped RGB are rewritten so saved custom colors and disabled lights survive.
                bool swapped = false;
                foreach (MenuLightDefinition definition in _lights)
                {
                    ParabolicFloorLightSettings appearance = definition.Appearance;
                    if (definition.Type == MenuLightType.Floor
                        && appearance.Red == 10f
                        && appearance.Green == 0f
                        && appearance.Blue == 0f)
                    {
                        definition.Appearance = appearance.WithRed(0f).WithGreen(1f).WithBlue(2.5f);
                        swapped = true;
                    }
                    else if (definition.Id == RaisedParabolaPresetId
                        && definition.Type == MenuLightType.Parabola
                        && appearance.Red == 0f
                        && appearance.Green == 1f
                        && appearance.Blue == 2.5f)
                    {
                        definition.Appearance = appearance.WithRed(10f).WithGreen(0f).WithBlue(0f);
                        swapped = true;
                    }
                }

                if (swapped)
                    Persist();

                _config.MenuLightDefaultsVersion = 2;
            }

            if (_config.MenuLightDefaultsVersion < 3)
            {
                // The default pair also swapped brightness once; only exact shipped values
                // migrate so custom levels survive and no deleted preset is resurrected.
                bool swapped = false;
                foreach (MenuLightDefinition definition in _lights)
                {
                    ParabolicFloorLightSettings appearance = definition.Appearance;
                    if (definition.Id != RaisedParabolaPresetId
                        && definition.Type == MenuLightType.Floor
                        && appearance.Red == 0f
                        && appearance.Green == 1f
                        && appearance.Blue == 2.5f
                        && appearance.Brightness == 0.11f)
                    {
                        definition.Appearance = appearance.WithBrightness(0.43f);
                        swapped = true;
                    }
                    else if (definition.Id == RaisedParabolaPresetId
                        && definition.Type == MenuLightType.Parabola
                        && appearance.Red == 10f
                        && appearance.Green == 0f
                        && appearance.Blue == 0f
                        && appearance.Brightness == 0.43f)
                    {
                        definition.Appearance = appearance.WithBrightness(0.11f);
                        swapped = true;
                    }
                }

                if (swapped)
                    Persist();

                _config.MenuLightDefaultsVersion = 3;
            }
        }

        internal IReadOnlyList<MenuLightDefinition> Lights => _lights;

        private MenuLightDefinition CreateRaisedParabolaPreset()
        {
            MenuLightDefinition preset = CreateDefault(MenuLightType.Parabola);
            preset.Id = RaisedParabolaPresetId;
            return preset;
        }

        internal MenuLightDefinition CreateDefault(MenuLightType type)
        {
            ParabolicFloorLightSettings appearance = type == MenuLightType.Floor
                ? ParabolicFloorLightSettings.FloorDefault
                : type == MenuLightType.Parabola
                ? ParabolicFloorLightSettings.RaisedParabolaDefault
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
                FadeStart = type == MenuLightType.Floor ? 0.13f : type == MenuLightType.Parabola ? 0.15f : 0.45f,
                Height = type == MenuLightType.Floor ? MenuLightDefinition.FloorHeight : type == MenuLightType.Parabola ? 4.3f : ParabolicFloorLightGeometry.TopHeight,
                Depth = type == MenuLightType.Parabola ? 0.2f : 0.02f,
            };
            if (type == MenuLightType.Parabola)
                created.RotateAxis(0, 20f);
            created.ConstrainPose();
            return created;
        }

        internal void Upsert(MenuLightDefinition definition)
        {
            MenuLightDefinition clone = definition.Clone();
            clone.ConstrainPose();
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

        internal static MenuLightType? GetPresetType(MenuLightDefinition definition)
        {
            if (definition.Id == RaisedParabolaPresetId)
                return MenuLightType.Parabola;

            return definition.Type == MenuLightType.Floor
                ? MenuLightType.Floor
                : null;
        }

        internal bool HasPreset(MenuLightType preset)
        {
            foreach (MenuLightDefinition definition in _lights)
            {
                if (GetPresetType(definition) == preset)
                    return true;
            }

            return false;
        }

        internal bool IsDefaultPreset(MenuLightDefinition definition)
        {
            MenuLightType? preset = GetPresetType(definition);
            if (preset == null || definition.Type != preset.Value)
                return false;

            return HasDefaultPresetValues(definition, preset.Value);
        }

        private static bool HasDefaultPresetValues(MenuLightDefinition definition, MenuLightType preset)
        {
            ParabolicFloorLightSettings defaults = preset == MenuLightType.Floor
                ? ParabolicFloorLightSettings.FloorDefault
                : ParabolicFloorLightSettings.RaisedParabolaDefault;
            float height = preset == MenuLightType.Floor
                ? MenuLightDefinition.FloorHeight
                : 4.3f;
            float depth = preset == MenuLightType.Floor
                ? MenuLightDefinition.FloorHeight
                : 0.2f;
            float fadeStart = preset == MenuLightType.Floor
                ? 0.13f
                : 0.15f;
            float pitch = preset == MenuLightType.Floor
                ? 0f
                : 20f;
            return definition.Enabled
                && definition.Appearance.Equals(defaults)
                && definition.PositionX == 0f
                && definition.PositionZ == 0f
                && definition.Height == height
                && definition.Depth == depth
                && definition.Size == 1f
                && definition.EndLength == 0f
                && definition.FadeStart == fadeStart
                && definition.RotationX == pitch
                && definition.RotationY == 0f
                && definition.RotationZ == 0f
                && definition.GetRotation() == Quaternion.AngleAxis(pitch, Vector3.right);
        }

        internal bool RestoreDefaultPreset(MenuLightType preset, string? id)
        {
            if (preset != MenuLightType.Floor && preset != MenuLightType.Parabola)
                return false;

            if (id != null)
            {
                MenuLightDefinition? existing = _lights.Find(definition => definition.Id == id);
                if (existing == null || GetPresetType(existing) != preset)
                    return false;

                MenuLightDefinition defaults = preset == MenuLightType.Floor
                    ? CreateDefault(MenuLightType.Floor)
                    : CreateRaisedParabolaPreset();
                defaults.Id = existing.Id;
                int index = _lights.IndexOf(existing);
                _lights[index] = defaults;
                Persist();
                return true;
            }

            if (HasPreset(preset) || _lights.Count >= MaximumLights)
                return false;

            _lights.Add(preset == MenuLightType.Floor
                ? CreateDefault(MenuLightType.Floor)
                : CreateRaisedParabolaPreset());
            Persist();
            return true;
        }

        internal bool IsDefaultFloor(MenuLightDefinition definition)
            => definition.Type == MenuLightType.Floor && IsDefaultPreset(definition);

        internal bool RestoreDefaultFloor(string? id)
            => RestoreDefaultPreset(MenuLightType.Floor, id);

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
            Working.ConstrainPose();
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
