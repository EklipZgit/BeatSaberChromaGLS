using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.Settings
{
    internal sealed class MenuLightReflectionCache : IDisposable
    {
        internal const int Resolution = 1024;

        private const int MaximumLights = 8;

        private static readonly int TextureId = Shader.PropertyToID("_FerrofluidMenuLightReflections");
        private static readonly int CountId = Shader.PropertyToID("_FerrofluidMenuLightCount");
        private static readonly int WorldToLocalId = Shader.PropertyToID("_FerrofluidMenuLightWorldToLocal");
        private static readonly int BoundsId = Shader.PropertyToID("_FerrofluidMenuLightBounds");
        private static readonly int FlagsId = Shader.PropertyToID("_FerrofluidMenuLightFlags");
        private static readonly int LegacyEnabledId = Shader.PropertyToID("_FerrofluidParabolaReflectionEnabled");

        private readonly Material _material;
        private readonly Action<RenderTexture, RenderTexture> _renderBloom;
        private readonly List<MenuLightDefinition> _saved = new();
        private readonly Dictionary<string, MenuLightMeshes> _meshes = new();
        private int _resolution = Resolution;
        private RenderTexture? _array;
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
                RenderTexture? previous = _array;
                _array = next.Count > 0 ? CreateArray(next.Count) : null;
                if (_array != null)
                {
                    _array.Create();
                    if (previous != null)
                    {
                        // Copy surviving slices and every mip by stable Id before releasing the old array.
                        for (int nextIndex = 0; nextIndex < next.Count; nextIndex++)
                        {
                            int oldIndex = _saved.FindIndex(definition => definition.Id == next[nextIndex].Id);
                            if (oldIndex < 0)
                                continue;

                            for (int mip = 0; mip < previous.mipmapCount; mip++)
                                Graphics.CopyTexture(previous, oldIndex, mip, _array, nextIndex, mip);
                        }
                    }
                }

                if (previous != null)
                {
                    previous.Release();
                    Object.Destroy(previous);
                }
            }

            // Bake only slices whose geometry or appearance changed; compare against the prior
            // snapshots before they are replaced so layout copies never re-run the bake.
            bool anyBaked = false;
            if (_array != null)
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
                            Graphics.CopyTexture(temp, 0, 0, _array, index, 0);
                            anyBaked = true;
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

                if (anyBaked)
                    _array.GenerateMips();
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
            if (_array != null)
            {
                _array.Release();
                Object.Destroy(_array);
                _array = null;
            }

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
            Shader.SetGlobalTexture(TextureId, null);
            Shader.SetGlobalFloat(CountId, 0f);
            Shader.SetGlobalMatrixArray(WorldToLocalId, new Matrix4x4[MaximumLights]);
            Shader.SetGlobalVectorArray(BoundsId, new Vector4[MaximumLights]);
            Shader.SetGlobalVectorArray(FlagsId, new Vector4[MaximumLights]);
            Shader.SetGlobalFloat(LegacyEnabledId, 0f);
            if (_array != null)
            {
                _array.Release();
                Object.Destroy(_array);
                _array = null;
            }

            Object.Destroy(_material);
        }

        private void Publish()
        {
            // The collection owns the slot; the legacy single-map path stays off while it exists.
            Shader.SetGlobalFloat(LegacyEnabledId, 0f);
            Shader.SetGlobalTexture(TextureId, _hasContent && _masterEnabled ? _array : null);
            Shader.SetGlobalFloat(CountId, _hasContent && _masterEnabled ? _saved.Count : 0f);
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
                flags[index] = new Vector4(_masterEnabled && definition.Enabled ? 1f : 0f, 0f, 0f, 0f);
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

        private RenderTexture CreateArray(int count)
            => new(_resolution, _resolution, 0, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear)
            {
                name = "ChromaGLS Menu Light Reflections",
                hideFlags = HideFlags.DontSave,
                useMipMap = true,
                autoGenerateMips = false,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
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
