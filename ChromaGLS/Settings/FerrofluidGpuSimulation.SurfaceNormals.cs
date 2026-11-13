#nullable enable

using UnityEngine;
using UnityEngine.Rendering;

namespace ChromaGLS.Settings
{
    internal sealed partial class FerrofluidGpuSimulation
    {
        private const int SurfaceNormalLogicalSize = 512;
        private const int SurfaceNormalPadding = 2;
        private static readonly int SurfaceNormalTextureId = Shader.PropertyToID("_SurfaceNormalTex");
        private static readonly int SurfaceNormalLogicalSizeId = Shader.PropertyToID("_SurfaceNormalLogicalSize");

        private RenderTexture? _surfaceNormalCache;
        private CommandBuffer? _surfaceNormalCommands;
        private int _surfaceNormalPass = -1;
        private bool _useSurfaceNormals;
        private bool _surfaceNormalsReady;

        // Allocates or frees the baked continuous-normal cache on spikes/lifecycle transitions only.
        private void ConfigureSurfaceNormals(bool wanted)
        {
            if (wanted == _useSurfaceNormals)
            {
                return;
            }

            if (!wanted)
            {
                ReleaseSurfaceNormals();
                return;
            }

            _surfaceNormalPass = _surfaceMaterial.FindPass("FERROFLUID_SURFACE_NORMAL_CACHE");
            if (_surfaceNormalPass < 0)
            {
                throw new System.InvalidOperationException("The ferrofluid surface shader lacks the normal cache pass; rebuild the shader bundle.");
            }

            _useSurfaceNormals = true;
            _surfaceNormalCache = new RenderTexture(
                SurfaceNormalLogicalSize + (2 * SurfaceNormalPadding),
                SurfaceNormalLogicalSize + (2 * SurfaceNormalPadding),
                0,
                RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear)
            {
                name = "ChromaGLS Ferrofluid Surface Normals",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
            };
            _surfaceNormalCache.Create();
            _surfaceNormalCommands = new CommandBuffer { name = "ChromaGLS Ferrofluid Surface Normals" };
            _surfaceMaterial.SetFloat(SurfaceNormalLogicalSizeId, SurfaceNormalLogicalSize);
            RecordSnapshotFloat(_surfaceMaterial, SurfaceNormalLogicalSizeId, SurfaceNormalLogicalSize);
            _surfaceMaterial.SetTexture(SurfaceNormalTextureId, _surfaceNormalCache);
            RecordSnapshotTexture(_surfaceMaterial, SurfaceNormalTextureId, _surfaceNormalCache);
            _surfaceNormalsReady = false;
        }

        // Rebakes the continuous field normal + film each presentation; the beauty keyword stays
        // off until the first full bake is submitted so startup frames keep exact vertex normals.
        private void RenderSurfaceNormals()
        {
            if (!_useSurfaceNormals)
            {
                return;
            }

            RenderTexture previous = RenderTexture.active;
            try
            {
                _surfaceNormalCommands!.Clear();
                _surfaceNormalCommands.SetRenderTarget(_surfaceNormalCache!);
                _surfaceNormalCommands.DrawProcedural(
                    _renderer.localToWorldMatrix,
                    _surfaceMaterial,
                    _surfaceNormalPass,
                    MeshTopology.Triangles,
                    3);
                Graphics.ExecuteCommandBuffer(_surfaceNormalCommands);
            }
            finally
            {
                RenderTexture.active = previous;
            }

            if (!_surfaceNormalsReady)
            {
                _surfaceMaterial.EnableKeyword("FERROFLUID_CACHED_NORMALS");
                _surfaceNormalsReady = true;
            }
        }

        private void ReleaseSurfaceNormals()
        {
            _surfaceMaterial.DisableKeyword("FERROFLUID_CACHED_NORMALS");
            _surfaceMaterial.SetTexture(SurfaceNormalTextureId, (Texture?)null);
            RecordSnapshotTexture(_surfaceMaterial, SurfaceNormalTextureId, null);
            if (_surfaceNormalCommands != null)
            {
                _surfaceNormalCommands.Release();
                _surfaceNormalCommands = null;
            }

            if (_surfaceNormalCache != null)
            {
                _surfaceNormalCache.Release();
                UnityEngine.Object.Destroy(_surfaceNormalCache);
                _surfaceNormalCache = null;
            }

            _surfaceNormalPass = -1;
            _useSurfaceNormals = false;
            _surfaceNormalsReady = false;
        }
    }
}
