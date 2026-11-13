using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ChromaGLS.Settings
{
    internal sealed class MenuLightRenderer : System.IDisposable
    {
        private static readonly string[] CoreKeywords = { "MAIN_TEXTURE", "VERTEX_COLOR", "_WHITEBOOSTTYPE_MAINEFFECT" };

        private static readonly string[] HaloKeywords = { "MAIN_TEXTURE", "VERTEX_COLOR" };

        private readonly Shader _shader;
        private readonly Scene _scene;
        private MenuLightDefinition _definition;
        private bool _masterEnabled;
        private GameObject? _root;
        private GameObject? _haloRoot;
        private Mesh? _mesh;
        private Mesh? _haloMesh;
        private Material? _material;
        private Material? _haloMaterial;
        private Material? _coverageMaterial;

        internal MenuLightRenderer(Shader shader, Scene scene, MenuLightDefinition definition, MenuLightMeshes meshes, bool masterEnabled)
        {
            _shader = shader;
            _scene = scene;
            _definition = definition.Clone();
            _masterEnabled = masterEnabled;
            Create(meshes);
        }

        internal void Apply(MenuLightDefinition definition, MenuLightMeshes meshes, bool masterEnabled)
        {
            _definition = definition.Clone();
            _masterEnabled = masterEnabled;
            Update(meshes);
            ApplyVisibility();
        }

        internal void SetEnabled(bool masterEnabled, bool definitionEnabled)
        {
            _masterEnabled = masterEnabled;
            _definition.Enabled = definitionEnabled;
            ApplyVisibility();
        }

        public void Dispose()
        {
            if (_haloRoot != null)
            {
                Object.Destroy(_haloRoot);
                _haloRoot = null;
            }

            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }

            if (_haloMesh != null)
            {
                Object.Destroy(_haloMesh);
                _haloMesh = null;
            }

            if (_mesh != null)
            {
                Object.Destroy(_mesh);
                _mesh = null;
            }

            if (_haloMaterial != null)
            {
                Object.Destroy(_haloMaterial);
                _haloMaterial = null;
            }

            if (_coverageMaterial != null)
            {
                Object.Destroy(_coverageMaterial);
                _coverageMaterial = null;
            }

            if (_material != null)
            {
                Object.Destroy(_material);
                _material = null;
            }
        }

        private void Create(MenuLightMeshes meshes)
        {
            _material = CreateCoreMaterial(_shader, _definition.Appearance);
            _haloMaterial = CreateGlowMaterial(_shader, _definition.Appearance);
            if (FerrofluidGpuResources.TryGet(out FerrofluidGpuResources? resources)
                && resources!.MenuLightCoreOcclusionShader != null
                && resources.MenuLightCoreOcclusionShader.isSupported)
            {
                // Remove earlier glow using mesh coverage before adding native radiance.
                // Bloom alpha is not opacity.
                _coverageMaterial = new Material(resources.MenuLightCoreOcclusionShader)
                {
                    name = "ChromaGLS Menu Light Coverage Material",
                    hideFlags = HideFlags.DontSave,
                    enableInstancing = true,
                    renderQueue = (int)RenderQueue.Transparent - 1,
                };
                _haloMaterial.renderQueue = (int)RenderQueue.Transparent - 2;
            }

            _root = new GameObject("ChromaGLS Menu Light", typeof(MeshFilter), typeof(MeshRenderer));
            if (_root.scene != _scene)
                SceneManager.MoveGameObjectToScene(_root, _scene);

            _root.transform.SetPositionAndRotation(meshes.Position, meshes.Rotation);

            _mesh = BindMesh(_root, meshes.Core, "ChromaGLS Menu Light Core");
            MeshRenderer coreRenderer = _root.GetComponent<MeshRenderer>();
            BindRenderer(coreRenderer, _material);
            if (_coverageMaterial != null)
                coreRenderer.sharedMaterials = new[] { _coverageMaterial, _material };

            _haloRoot = new GameObject("ChromaGLS Menu Light Glow", typeof(MeshFilter), typeof(MeshRenderer));
            _haloRoot.transform.SetParent(_root.transform, false);
            _haloMesh = BindMesh(_haloRoot, meshes.Glow, "ChromaGLS Menu Light Glow");
            BindRenderer(_haloRoot.GetComponent<MeshRenderer>(), _haloMaterial);
            ApplyVisibility();
        }

        private void Update(MenuLightMeshes meshes)
        {
            _root!.transform.SetPositionAndRotation(meshes.Position, meshes.Rotation);
            AssignMesh(_mesh!, meshes.Core);
            AssignMesh(_haloMesh!, meshes.Glow);
            _material!.color = _definition.Appearance.EvaluateColor();
            _material.SetFloat("_BloomMultiplier", _definition.Appearance.EvaluateBloomMultiplier());
            _haloMaterial!.color = _definition.Appearance.EvaluateGlowColor();
        }

        private static void AssignMesh(Mesh mesh, ParabolicFloorLightMeshData data)
        {
            // Clear before reassigning when the vertex count shrinks; stale triangles must not reference removed verts.
            if (mesh.vertexCount != data.Vertices.Length)
                mesh.Clear();
            mesh.vertices = data.Vertices;
            mesh.triangles = data.Triangles;
            mesh.colors = data.Colors;
            mesh.uv = data.UV;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private static void AssignMesh(Mesh mesh, ParabolicFloorLightGlowMeshData data)
        {
            if (mesh.vertexCount != data.Vertices.Length)
                mesh.Clear();
            mesh.vertices = data.Vertices;
            mesh.triangles = data.Triangles;
            mesh.colors = data.Colors;
            mesh.uv = data.UV;
            mesh.RecalculateBounds();
        }

        private static Mesh BindMesh(GameObject root, ParabolicFloorLightMeshData data, string name)
        {
            Mesh mesh = new()
            {
                name = name,
                vertices = data.Vertices,
                triangles = data.Triangles,
                colors = data.Colors,
                uv = data.UV,
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            root.GetComponent<MeshFilter>().sharedMesh = mesh;
            return mesh;
        }

        private static Mesh BindMesh(GameObject root, ParabolicFloorLightGlowMeshData data, string name)
        {
            Mesh mesh = new()
            {
                name = name,
                vertices = data.Vertices,
                triangles = data.Triangles,
                colors = data.Colors,
                uv = data.UV,
            };
            mesh.RecalculateBounds();
            root.GetComponent<MeshFilter>().sharedMesh = mesh;
            return mesh;
        }

        private static void BindRenderer(MeshRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        internal static Material CreateCoreMaterial(Shader shader, ParabolicFloorLightSettings appearance)
        {
            Material material = new(shader)
            {
                name = "ChromaGLS Menu Light Material",
                hideFlags = HideFlags.DontSave,
                enableInstancing = true,
                shaderKeywords = CoreKeywords,
                color = appearance.EvaluateColor(),
            };
            ConfigureParticleMaterial(material);

            // The strip is physically solid, so it must occlude the floor halo below it.
            material.SetFloat("_CustomZWrite", 1f);

            // Vertex alpha is emission/bloom payload, not opacity, so the solid face must
            // replace the background to keep end fades opaque rather than additive.
            material.SetFloat("_BlendDstFactor", 0f);
            material.SetFloat("_BlendDstFactorA", 0f);
            material.renderQueue = (int)RenderQueue.Transparent - 1;
            material.SetFloat("_BloomMultiplier", appearance.EvaluateBloomMultiplier());
            material.SetFloat("_WhiteBoostType", 1f);
            material.SetFloat("_BlendSrcFactorA", 1f);
            material.SetFloat("_CullMode", 2f);
            return material;
        }

        internal static Material CreateGlowMaterial(Shader shader, ParabolicFloorLightSettings appearance)
        {
            Material material = new(shader)
            {
                name = "ChromaGLS Menu Glow Material",
                hideFlags = HideFlags.DontSave,
                enableInstancing = true,
                shaderKeywords = HaloKeywords,
                color = appearance.EvaluateGlowColor(),
            };
            ConfigureParticleMaterial(material);
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetFloat("_WhiteBoostType", 0f);
            material.SetFloat("_BloomMultiplier", 0f);
            material.SetFloat("_BlendSrcFactorA", 0f);
            material.SetFloat("_CullMode", 0f);
            return material;
        }

        internal static void ConfigureParticleMaterial(Material material)
        {
            material.SetFloat("_Intensity", 1f);
            material.SetFloat("_AlphaMultiplier", 1f);
            material.SetFloat("_BlendSrcFactor", 1f);
            material.SetFloat("_BlendDstFactor", 1f);
            material.SetFloat("_BlendDstFactorA", 1f);
            material.SetFloat("_BlendOp", 0f);
            material.SetFloat("_CustomZWrite", 0f);
            material.SetFloat("_ZTest", 4f);
            material.mainTexture = Texture2D.whiteTexture;
        }

        private void ApplyVisibility()
        {
            if (_root == null)
                return;

            ParabolicFloorLightSettings appearance = _definition.Appearance;
            bool visible = _masterEnabled
                && _definition.Enabled
                && appearance.Brightness > 0f
                && (appearance.Red > 0f || appearance.Green > 0f || appearance.Blue > 0f);
            _root.SetActive(visible);
            if (_haloRoot != null)
                _haloRoot.SetActive(visible && _definition.Type == MenuLightType.Floor && appearance.BloomWidthScale > 0f && appearance.BloomBrightness > 0f);
        }
    }
}
