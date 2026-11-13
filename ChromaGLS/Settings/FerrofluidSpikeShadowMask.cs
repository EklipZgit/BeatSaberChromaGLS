using System;
using UnityEngine;

namespace ChromaGLS.Settings
{
    // Renders the actual deformed surface mesh into an offscreen silhouette mask so the
    // floor shadow can show real spikes instead of the analytic body projection. The mask
    // is white-overwrite (union) coverage; two separable Gaussian blits supply softness.
    internal sealed class FerrofluidSpikeShadowMask : IDisposable
    {
        internal const int Size = 512;
        private static readonly int StepId = Shader.PropertyToID("_FerroShadowBlurStep");
        private static readonly int SlopeId = Shader.PropertyToID("_FerroMaskSlope");
        private static readonly int FloorYId = Shader.PropertyToID("_FerroMaskFloorY");
        private static readonly int CenterId = Shader.PropertyToID("_FerroMaskCenter");
        private static readonly int InvExtentId = Shader.PropertyToID("_FerroMaskInvExtent");

        private readonly RenderTexture _mask;
        private readonly RenderTexture _blur;
        private readonly Material _surfaceMaterial;
        private readonly Material _blurMaterial;
        private readonly int _maskPassIndex;
        private readonly int _blurPassIndex;

        internal FerrofluidSpikeShadowMask(Material surfaceMaterial, Shader shadowShader)
        {
            // The simulation's own live material is used so runtime-set deformation
            // uniforms always match the rendered surface; the four mask uniforms are only
            // read by the mask pass, so writing them on the material is safe.
            _surfaceMaterial = surfaceMaterial;
            _maskPassIndex = _surfaceMaterial.FindPass("FERROFLUID_SHADOW_MASK");
            _blurMaterial = new Material(shadowShader);
            _blurPassIndex = _blurMaterial.FindPass("FERROFLUID_SHADOW_BLUR");
            _mask = CreateTarget();
            _blur = CreateTarget();
            _mask.Create();
            _blur.Create();
            if (_maskPassIndex < 0 || _blurPassIndex < 0)
            {
                Dispose();
                throw new InvalidOperationException("Ferrofluid shadow mask passes are missing from the compiled shaders.");
            }
        }

        internal RenderTexture Texture => _mask;

        internal void Update(
            Mesh surfaceMesh,
            Matrix4x4 localToWorld,
            Vector2 center,
            Vector2 invExtent,
            float floorY,
            Vector2 slope,
            float softness)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                // Use the live surface material to retain runtime-set deformation uniforms.
                // These four uniforms are used only by the mask pass.
                _surfaceMaterial.SetVector(SlopeId, slope);
                _surfaceMaterial.SetFloat(FloorYId, floorY);
                _surfaceMaterial.SetVector(CenterId, center);
                _surfaceMaterial.SetVector(InvExtentId, invExtent);
                _surfaceMaterial.SetPass(_maskPassIndex);
                RenderTexture.active = _mask;
                GL.Clear(true, true, Color.black);
                Graphics.DrawMeshNow(surfaceMesh, localToWorld);

                // UV step sized so the outer kernel tap lands `softness` meters out per axis.
                float stepX = softness / (3.230769f * (2f / invExtent.x));
                float stepY = softness / (3.230769f * (2f / invExtent.y));
                _blurMaterial.SetVector(StepId, new Vector4(stepX, 0f, 0f, 0f));
                Graphics.Blit(_mask, _blur, _blurMaterial, _blurPassIndex);
                _blurMaterial.SetVector(StepId, new Vector4(0f, stepY, 0f, 0f));
                Graphics.Blit(_blur, _mask, _blurMaterial, _blurPassIndex);
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        public void Dispose()
        {
            if (_mask != null)
            {
                _mask.Release();
                UnityEngine.Object.Destroy(_mask);
            }

            if (_blur != null)
            {
                _blur.Release();
                UnityEngine.Object.Destroy(_blur);
            }

            if (_blurMaterial != null)
                UnityEngine.Object.Destroy(_blurMaterial);
        }

        private static RenderTexture CreateTarget()
            => new(Size, Size, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                antiAliasing = 1,
            };
    }
}
