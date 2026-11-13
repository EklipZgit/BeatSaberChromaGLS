using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.Settings
{
    internal sealed class ParabolicFloorReflectionCache : IDisposable
    {
        internal const int Resolution = 1024;
        private static readonly int TextureId = Shader.PropertyToID("_FerrofluidParabolaReflection");
        private static readonly int BoundsId = Shader.PropertyToID("_FerrofluidParabolaReflectionBounds");
        private static readonly int PlaneId = Shader.PropertyToID("_FerrofluidParabolaReflectionY");
        private static readonly int EnabledId = Shader.PropertyToID("_FerrofluidParabolaReflectionEnabled");
        private static readonly int BakeBoundsId = Shader.PropertyToID("_BakeBounds");
        private static readonly int BloomMultiplierId = Shader.PropertyToID("_BloomMultiplier");
        private static readonly int BakeSampleId = Shader.PropertyToID("_BakeSample");
        private static readonly Vector2[] BakeSampleOffsets =
        {
            new(-0.125f, -0.375f),
            new(0.375f, -0.125f),
            new(-0.375f, 0.125f),
            new(0.125f, 0.375f),
        };

        private readonly RenderTexture _texture;
        private readonly Material _material;
        private readonly Action<RenderTexture, RenderTexture> _renderBloom;
        private Vector4 _bounds;
        private float _planeY;
        private bool _enabled;
        private bool _hasContent;

        internal ParabolicFloorReflectionCache(Shader shader, Action<RenderTexture, RenderTexture> renderBloom)
        {
            if (!shader.isSupported || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float) || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
                throw new NotSupportedException("The packed HDR floor reflection format or shader is unsupported.");

            _renderBloom = renderBloom ?? throw new ArgumentNullException(nameof(renderBloom));

            _material = new Material(shader)
            {
                name = "ChromaGLS Parabolic Floor Reflection Bake",
                hideFlags = HideFlags.DontSave,
            };
            _texture = new RenderTexture(Resolution, Resolution, 0, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear)
            {
                name = "ChromaGLS Saved Parabolic Floor Reflection",
                hideFlags = HideFlags.DontSave,
                useMipMap = true,
                autoGenerateMips = false,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };
            try
            {
                if (!_texture.Create() || _texture.format != RenderTextureFormat.RGB111110Float)
                    throw new NotSupportedException("The GPU could not allocate the 32-bit HDR floor reflection.");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void Regenerate(
            FerrofluidGrabEnclosure enclosure,
            Vector3 playerPosition,
            Vector3 forward,
            ParabolicFloorLightSettings settings)
        {
            Mesh? core = null;
            Mesh? halo = null;
            Vector4 bounds = Vector4.zero;
            bool hasContent = false;
            RenderTexture previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            try
            {
                if (settings.Brightness > 0f
                    && Mathf.Max(settings.Red, Mathf.Max(settings.Green, settings.Blue)) > 0f
                    && ParabolicFloorLightGeometry.TryCreateConfigured(enclosure, playerPosition, forward, settings, out ParabolicFloorLightMeshData? data)
                    && data.HasValue)
                {
                    ParabolicFloorLightMeshData source = data.Value;
                    int[] top = new int[ParabolicFloorLightGeometry.SegmentCount * 6];

                    // The bottom and walls belong to the visible strip, not its top-down radiance.
                    for (int segment = 0; segment < ParabolicFloorLightGeometry.SegmentCount; segment++)
                        Array.Copy(source.Triangles, segment * 24, top, segment * 6, 6);

                    core = new Mesh
                    {
                        vertices = source.Vertices,
                        colors = source.Colors,
                        triangles = top,
                    };
                    core.RecalculateBounds();
                    Bounds footprint = core.bounds;
                    if (settings.BloomWidthScale > 0f && settings.BloomBrightness > 0f)
                    {
                        ParabolicFloorLightGlowMeshData glow = ParabolicFloorLightGeometry.CreateGlow(enclosure, source, settings);
                        halo = new Mesh
                        {
                            vertices = glow.Vertices,
                            colors = glow.Colors,
                            triangles = glow.Triangles,
                        };
                        halo.RecalculateBounds();
                        footprint.Encapsulate(halo.bounds);
                    }

                    float side = Mathf.Max(footprint.size.x, footprint.size.z) * 1.5f;
                    Vector3 center = footprint.center;
                    bounds = new Vector4(center.x - (side * 0.5f), center.z - (side * 0.5f), 1f / side, 1f / side);
                    hasContent = true;
                }

                Bake(_material, core, halo, settings, bounds, _texture, _renderBloom);

                _bounds = bounds;
                _planeY = enclosure.FloorY + ParabolicFloorLightGeometry.TopHeight;
                _hasContent = hasContent;

                // Save runs synchronously before rendering, so pixels and mapping become visible together.
                Publish();
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgb;
                if (core != null)
                    Object.Destroy(core);

                if (halo != null)
                    Object.Destroy(halo);
            }
        }

        internal static void Bake(
            Material material,
            Mesh? core,
            Mesh? halo,
            ParabolicFloorLightSettings appearance,
            Vector4 bounds,
            RenderTexture destination,
            Action<RenderTexture, RenderTexture> renderBloom)
        {
            bool hasContent = core != null;
            RenderTexture previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            RenderTexture staging = RenderTexture.GetTemporary(destination.width, destination.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear, 1);
            try
            {
                GL.sRGBWrite = false;
                RenderTexture.active = staging;
                GL.Clear(false, true, new Color(0f, 0f, 0f, 0f));
                if (hasContent)
                {
                    material.SetVector(BakeBoundsId, bounds);

                    // Four rotated-grid subpixel passes accumulate at quarter weight, so strip-edge
                    // coverage is antialiased without another render target.
                    foreach (Vector2 offset in BakeSampleOffsets)
                    {
                        material.SetVector(BakeSampleId, new Vector4(offset.x / destination.width, offset.y / destination.height, 0.25f, 0f));
                        Draw(material, core!, appearance.EvaluateColor(), appearance.EvaluateBloomMultiplier());
                        if (halo != null)
                            Draw(material, halo, appearance.EvaluateGlowColor(), 0f);
                    }
                }

                if (hasContent)
                {
                    renderBloom(staging, destination);
                }
                else
                {
                    RenderTexture.active = destination;
                    GL.Clear(false, true, new Color(0f, 0f, 0f, 0f));
                }

                if (destination.useMipMap)
                    destination.GenerateMips();
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgb;
                RenderTexture.ReleaseTemporary(staging);
            }
        }

        internal void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            Shader.SetGlobalFloat(EnabledId, _enabled && _hasContent ? 1f : 0f);
        }

        public void Dispose()
        {
            Shader.SetGlobalFloat(EnabledId, 0f);
            Shader.SetGlobalTexture(TextureId, null);
            Shader.SetGlobalVector(BoundsId, Vector4.zero);
            Shader.SetGlobalFloat(PlaneId, 0f);
            _texture.Release();
            Object.Destroy(_texture);
            Object.Destroy(_material);
        }

        private static void Draw(Material material, Mesh mesh, Color color, float bloomMultiplier)
        {
            material.color = color;
            material.SetFloat(BloomMultiplierId, bloomMultiplier);
            if (!material.SetPass(0))
                throw new InvalidOperationException("The procedural floor reflection bake pass is unavailable.");

            Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
        }

        private void Publish()
        {
            Shader.SetGlobalTexture(TextureId, _texture);
            Shader.SetGlobalVector(BoundsId, _bounds);
            Shader.SetGlobalFloat(PlaneId, _planeY);
            Shader.SetGlobalFloat(EnabledId, _enabled && _hasContent ? 1f : 0f);
        }
    }
}
