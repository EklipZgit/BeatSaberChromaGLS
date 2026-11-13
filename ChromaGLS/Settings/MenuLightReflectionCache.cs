using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.Settings
{
    internal sealed class MenuLightReflectionCache : IDisposable
    {
        internal const int Resolution = 1024;

        // Each array must stay under the guaranteed D3D11 per-resource bound.
        internal const long MaximumArrayBytes = (2L * 1024 * 1024 * 1024) - (64 * 1024); // 2 GiB bound with 64 KiB alignment headroom

        private const int MaximumLights = 8;
        private const string SplitKeyword = "FERROFLUID_SPLIT_MENU_LIGHT_REFLECTIONS";

        private static readonly int[] BankIds =
        {
            Shader.PropertyToID("_FerrofluidMenuLightReflections"),
            Shader.PropertyToID("_FerrofluidMenuLightReflections1"),
            Shader.PropertyToID("_FerrofluidMenuLightReflections2"),
            Shader.PropertyToID("_FerrofluidMenuLightReflections3"),
            Shader.PropertyToID("_FerrofluidMenuLightReflections4"),
            Shader.PropertyToID("_FerrofluidMenuLightReflections5"),
            Shader.PropertyToID("_FerrofluidMenuLightReflections6"),
            Shader.PropertyToID("_FerrofluidMenuLightReflections7"),
        };

        private static readonly int CountId = Shader.PropertyToID("_FerrofluidMenuLightCount");
        private static readonly int WorldToLocalId = Shader.PropertyToID("_FerrofluidMenuLightWorldToLocal");
        private static readonly int BoundsId = Shader.PropertyToID("_FerrofluidMenuLightBounds");
        private static readonly int FlagsId = Shader.PropertyToID("_FerrofluidMenuLightFlags");
        private static readonly int SlicesPerArrayId = Shader.PropertyToID("_FerrofluidMenuLightSlicesPerArray");
        private static readonly int LegacyEnabledId = Shader.PropertyToID("_FerrofluidParabolaReflectionEnabled");

        private readonly Material _material;
        private readonly Action<RenderTexture, RenderTexture> _renderBloom;
        private readonly List<MenuLightDefinition> _saved = new();
        private readonly Dictionary<string, MenuLightMeshes> _meshes = new();
        private readonly List<RenderTexture> _banks = new();
        private int _resolution = Resolution;
        private int _slicesPerArray = MaximumLights;
        private bool _masterEnabled;
        private bool _hasContent;

        internal MenuLightReflectionCache(Shader shader, Action<RenderTexture, RenderTexture> renderBloom)
        {
            if (!shader.isSupported || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float) || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
                throw new NotSupportedException("The packed HDR menu light reflection format or shader is unsupported.");

            _renderBloom = renderBloom ?? throw new ArgumentNullException(nameof(renderBloom));
            _material = new Material(shader)
            {
                name = "ChromaGLS Menu Light Reflection Bake",
                hideFlags = HideFlags.DontSave,
            };
        }

        internal int Count => _saved.Count;

        internal void Synchronize(
            IReadOnlyList<MenuLightDefinition> definitions,
            IReadOnlyDictionary<string, MenuLightMeshes> meshes,
            bool masterEnabled)
        {
            if (definitions.Count > MaximumLights)
                throw new InvalidOperationException("The menu light collection cannot exceed eight lights.");

            _masterEnabled = masterEnabled;
            if (_resolution == 0)
            {
                _saved.Clear();
                _meshes.Clear();
                _hasContent = false;
                Publish();
                return;
            }

            List<MenuLightDefinition> next = new(definitions.Count);
            foreach (MenuLightDefinition definition in definitions)
                next.Add(definition.Clone());

            bool layoutChanged = next.Count != _saved.Count;
            if (!layoutChanged)
            {
                for (int index = 0; index < next.Count; index++)
                {
                    if (next[index].Id != _saved[index].Id)
                    {
                        layoutChanged = true;
                        break;
                    }
                }
            }

            if (layoutChanged)
            {
                List<RenderTexture> previous = new(_banks);
                _banks.Clear();
                int bankCount = (next.Count + _slicesPerArray - 1) / _slicesPerArray;
                for (int bank = 0; bank < bankCount; bank++)
                {
                    int slices = Math.Min(_slicesPerArray, next.Count - (bank * _slicesPerArray));
                    RenderTexture created = CreateArray(slices);
                    created.name = bank == 0 ? "ChromaGLS Menu Light Reflections" : $"ChromaGLS Menu Light Reflections {bank}";
                    _banks.Add(created);
                    bool made = created.Create();
                    Plugin.Log.Info($"[ChromaGLS Menu Lights] reflection atlas requested={_resolution} actual={created.width}x{created.height} slices={created.volumeDepth} bank={bank} created={made} filter={created.filterMode} aniso={created.anisoLevel} mips={created.mipmapCount} globalAniso={QualitySettings.anisotropicFiltering}");
                }

                if (previous.Count > 0 && _banks.Count > 0)
                {
                    // Copy surviving slices and every mip by stable Id before releasing the old
                    // banks; both layouts share the current slices-per-array capacity.
                    for (int nextIndex = 0; nextIndex < next.Count; nextIndex++)
                    {
                        int oldIndex = _saved.FindIndex(definition => definition.Id == next[nextIndex].Id);
                        if (oldIndex < 0)
                            continue;

                        RenderTexture source = previous[oldIndex / _slicesPerArray];
                        RenderTexture destination = _banks[nextIndex / _slicesPerArray];
                        for (int mip = 0; mip < source.mipmapCount; mip++)
                            Graphics.CopyTexture(source, oldIndex % _slicesPerArray, mip, destination, nextIndex % _slicesPerArray, mip);
                    }
                }

                foreach (RenderTexture bank in previous)
                {
                    bank.Release();
                    Object.Destroy(bank);
                }
            }

            // Bake only slices whose geometry or appearance changed; compare against the prior
            // snapshots before they are replaced so layout copies never re-run the bake.
            bool[] bakedBanks = new bool[_banks.Count];
            if (_banks.Count > 0)
            {
                RenderTexture? temp = null;
                try
                {
                    for (int index = 0; index < next.Count; index++)
                    {
                        MenuLightDefinition definition = next[index];
                        bool hasMeshes = meshes.TryGetValue(definition.Id, out MenuLightMeshes current);
                        bool bake = !hasMeshes;
                        MenuLightDefinition? previous = _saved.Find(saved => saved.Id == definition.Id);
                        if (hasMeshes && previous != null && _meshes.TryGetValue(definition.Id, out MenuLightMeshes old))
                        {
                            // An unchanged mesh or appearance keeps the copied slice; a changed
                            // footprint, dimensions, or appearance re-runs the bake.
                            if (!ReferenceEquals(current.Core.Vertices, old.Core.Vertices)
                                || !ReferenceEquals(current.Glow.Vertices, old.Glow.Vertices)
                                || !previous.Appearance.Equals(definition.Appearance))
                            {
                                bake = true;
                            }
                        }
                        else if (hasMeshes)
                        {
                            bake = true;
                        }

                        if (!bake)
                            continue;

                        temp ??= RenderTexture.GetTemporary(_resolution, _resolution, 0, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear);
                        temp.Create();
                        (Mesh? core, Mesh? halo, Vector4 bounds) = hasMeshes && IsVisible(definition)
                            ? BuildBakeMeshes(definition, current)
                            : ((Mesh?)null, (Mesh?)null, Vector4.zero);
                        try
                        {
                            ParabolicFloorReflectionCache.Bake(_material, core, halo, definition.Appearance, bounds, temp, _renderBloom);
                            Graphics.CopyTexture(temp, 0, 0, _banks[index / _slicesPerArray], index % _slicesPerArray, 0);
                            bakedBanks[index / _slicesPerArray] = true;
                        }
                        finally
                        {
                            if (core != null)
                                Object.Destroy(core);

                            if (halo != null)
                                Object.Destroy(halo);
                        }
                    }
                }
                finally
                {
                    if (temp != null)
                        RenderTexture.ReleaseTemporary(temp);
                }

                for (int bank = 0; bank < _banks.Count; bank++)
                {
                    // Only baked banks regenerate mips; copied and unchanged banks keep theirs.
                    if (bakedBanks[bank])
                        _banks[bank].GenerateMips();
                }
            }

            _saved.Clear();
            _saved.AddRange(next);
            _meshes.Clear();
            foreach (KeyValuePair<string, MenuLightMeshes> pair in meshes)
            {
                if (_saved.Exists(definition => definition.Id == pair.Key))
                    _meshes[pair.Key] = pair.Value;
            }

            _hasContent = _saved.Count > 0;
            Publish();
        }

        internal void SetEnabled(bool masterEnabled)
        {
            _masterEnabled = masterEnabled;
            Publish();
        }

        internal bool SetQuality(ReflectionQuality quality)
        {
            int resolution = ReflectionQualitySettings.GetMenuLightResolution(quality);
            if (resolution == _resolution)
                return false;

            _resolution = resolution;
            _slicesPerArray = GetSlicesPerArray(resolution);
            ReleaseBanks();
            _saved.Clear();
            _meshes.Clear();
            _hasContent = false;
            Publish();
            return true;
        }

        public void Dispose()
        {
            _saved.Clear();
            _meshes.Clear();
            _hasContent = false;
            for (int index = 0; index < BankIds.Length; index++)
                Shader.SetGlobalTexture(BankIds[index], null);

            Shader.SetGlobalFloat(CountId, 0f);
            Shader.SetGlobalFloat(SlicesPerArrayId, 0f);
            Shader.SetGlobalMatrixArray(WorldToLocalId, new Matrix4x4[MaximumLights]);
            Shader.SetGlobalVectorArray(BoundsId, new Vector4[MaximumLights]);
            Shader.SetGlobalVectorArray(FlagsId, new Vector4[MaximumLights]);
            Shader.SetGlobalFloat(LegacyEnabledId, 0f);
            Shader.DisableKeyword(SplitKeyword);
            ReleaseBanks();
            Object.Destroy(_material);
        }

        private void ReleaseBanks()
        {
            foreach (RenderTexture bank in _banks)
            {
                bank.Release();
                Object.Destroy(bank);
            }

            _banks.Clear();
        }

        private void Publish()
        {
            // The collection owns the slot; the legacy single-map path stays off while it exists.
            Shader.SetGlobalFloat(LegacyEnabledId, 0f);
            bool active = _hasContent && _masterEnabled;
            for (int index = 0; index < BankIds.Length; index++)
                Shader.SetGlobalTexture(BankIds[index], active && index < _banks.Count ? _banks[index] : null);

            Shader.SetGlobalFloat(CountId, active ? _saved.Count : 0f);
            Shader.SetGlobalFloat(SlicesPerArrayId, _slicesPerArray);

            // Only a multi-bank collection compiles the bank-select sampling; a packed
            // collection keeps the original single-array path.
            if (active && _banks.Count > 1)
                Shader.EnableKeyword(SplitKeyword);
            else
                Shader.DisableKeyword(SplitKeyword);
            Matrix4x4[] matrices = new Matrix4x4[MaximumLights];
            Vector4[] bounds = new Vector4[MaximumLights];
            Vector4[] flags = new Vector4[MaximumLights];
            for (int index = 0; index < _saved.Count && index < MaximumLights; index++)
            {
                MenuLightDefinition definition = _saved[index];
                if (!_meshes.TryGetValue(definition.Id, out MenuLightMeshes meshes) || !IsVisible(definition))
                {
                    // A light with no geometry or no pixels stays fully inactive; its slice and
                    // metadata must never be published as an active reflection.
                    flags[index] = Vector4.zero;
                    bounds[index] = Vector4.zero;
                    matrices[index] = Matrix4x4.identity;
                    continue;
                }

                matrices[index] = meshes.GetLocalToWorld().inverse;
                MenuLightReflectionBounds local = meshes.Bounds;
                bounds[index] = new Vector4(local.MinX, local.MinZ, local.InverseWidth, local.InverseHeight);
                bool floor = definition.Type == MenuLightType.Floor;
                flags[index] = new Vector4(
                    _masterEnabled && definition.Enabled ? 1f : 0f,
                    floor ? (MenuLightGeometry.FloorSideRadianceScale * MenuLightGeometry.FloorSideEmissionScale / MenuLightGeometry.FloorTopRadianceScale) - 1f : 0f,
                    floor ? 2f * ParabolicFloorLightGeometry.BaseCurveScale / (definition.Appearance.WidthScale * definition.Appearance.WidthScale) : 0f,
                    0f);
            }

            Shader.SetGlobalMatrixArray(WorldToLocalId, matrices);
            Shader.SetGlobalVectorArray(BoundsId, bounds);
            Shader.SetGlobalVectorArray(FlagsId, flags);
        }

        private static bool IsVisible(MenuLightDefinition definition)
        {
            ParabolicFloorLightSettings appearance = definition.Appearance;
            return appearance.Brightness > 0f
                && Mathf.Max(appearance.Red, Mathf.Max(appearance.Green, appearance.Blue)) > 0f;
        }

        internal static int GetSlicesPerArray(int resolution)
        {
            if (resolution == 0)
                return MaximumLights;

            long sliceBytes = 0;
            for (int size = resolution; size > 0; size >>= 1)
                sliceBytes += (long)size * size * 4;

            return (int)Math.Min(MaximumLights, Math.Max(1L, MaximumArrayBytes / sliceBytes));
        }

        private RenderTexture CreateArray(int count)
            => new(_resolution, _resolution, 0, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear)
            {
                name = "ChromaGLS Menu Light Reflections",
                hideFlags = HideFlags.DontSave,
                useMipMap = true,
                autoGenerateMips = false,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,

                // Each compressed reflection footprint samples across many texels in one
                // direction, so it needs anisotropic filtering, not coarse mips.
                anisoLevel = 16,
                dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                volumeDepth = count,
            };

        private static (Mesh? Core, Mesh? Halo, Vector4 Bounds) BuildBakeMeshes(MenuLightDefinition definition, MenuLightMeshes meshes)
        {
            ParabolicFloorLightMeshData reflection = meshes.ReflectionCore;
            Mesh core = new()
            {
                vertices = reflection.Vertices,
                colors = reflection.Colors,
                triangles = reflection.Triangles,
            };
            Mesh? halo = null;
            ParabolicFloorLightSettings appearance = definition.Appearance;
            if (appearance.BloomWidthScale > 0f && appearance.BloomBrightness > 0f)
            {
                halo = new Mesh
                {
                    vertices = meshes.Glow.Vertices,
                    colors = meshes.Glow.Colors,
                    triangles = meshes.Glow.Triangles,
                };
            }

            MenuLightReflectionBounds local = meshes.Bounds;
            return (core, halo, new Vector4(local.MinX, local.MinZ, local.InverseWidth, local.InverseHeight));
        }
    }
}
