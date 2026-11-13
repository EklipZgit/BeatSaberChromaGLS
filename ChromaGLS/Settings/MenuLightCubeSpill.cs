using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChromaGLS.Settings
{
    internal sealed class MenuLightCubeSpill : IDisposable
    {
        private const int MaximumSources = 32;
        private static readonly int CountId = Shader.PropertyToID("_MenuLightSpillCount");
        private static readonly int PositionsId = Shader.PropertyToID("_MenuLightSpillPositions");
        private static readonly int EnergyId = Shader.PropertyToID("_MenuLightSpillEnergy");
        private static readonly int NormalsId = Shader.PropertyToID("_MenuLightSpillNormals");

        private readonly Dictionary<Material, Material> _materials = new();
        private readonly List<(MeshRenderer Renderer, Material Original, Material Spill)> _receivers = new();
        private readonly Dictionary<string, MenuLightSpillSample[]> _saved = new();
        private readonly List<string> _order = new();
        private readonly Vector4[] _positions = new Vector4[MaximumSources];
        private readonly Vector4[] _energy = new Vector4[MaximumSources];
        private readonly Vector4[] _normals = new Vector4[MaximumSources];
        private readonly bool _linearColorSpace;
        private string? _previewId;
        private MenuLightSpillSample[] _preview = Array.Empty<MenuLightSpillSample>();
        private bool _enabled;

        internal MenuLightCubeSpill(Shader shader, Transform environment)
        {
            _linearColorSpace = QualitySettings.activeColorSpace == ColorSpace.Linear;
            foreach (string name in new[] { "Notes", "PileOfNotes" })
            {
                Transform group = environment.Find(name);
                if (group == null)
                    continue;

                foreach (MeshRenderer renderer in group.GetComponentsInChildren<MeshRenderer>(true))
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null || filter.sharedMesh.name != "CubeNoteSmoothHD")
                        continue;

                    foreach (Material original in renderer.sharedMaterials)
                    {
                        if (original == null || (original.name != "NoteMenu" && original.name != "NoteMenu (Instance)"))
                            continue;

                        if (!_materials.TryGetValue(original, out Material? spill))
                        {
                            spill = new Material(shader)
                            {
                                name = "ChromaGLS Menu Cube Spill",
                                hideFlags = HideFlags.DontSave,
                                enableInstancing = true,
                                renderQueue = original.renderQueue + 1,
                                color = original.color,
                            };
                            spill.SetFloat("_MenuLightUseFog", original.IsKeywordEnabled("FOG") ? 1f : 0f);
                            spill.SetFloat("_MenuLightUseHeightFog", original.IsKeywordEnabled("HEIGHT_FOG") ? 1f : 0f);
                            _materials.Add(original, spill);
                        }

                        _receivers.Add((renderer, original, spill));
                    }
                }
            }
        }

        internal void Synchronize(
            IReadOnlyList<MenuLightDefinition> definitions,
            IReadOnlyDictionary<string, MenuLightMeshes> meshes,
            bool enabled)
        {
            _saved.Clear();
            _order.Clear();
            foreach (MenuLightDefinition definition in definitions)
            {
                _order.Add(definition.Id);
                MenuLightSpillSample[] samples = Array.Empty<MenuLightSpillSample>();
                if (definition.Enabled && meshes.TryGetValue(definition.Id, out MenuLightMeshes geometry))
                    samples = MenuLightSpill.Calculate(geometry, definition.Appearance, _linearColorSpace);

                _saved.Add(definition.Id, samples);
            }

            _previewId = null;
            _preview = Array.Empty<MenuLightSpillSample>();
            _enabled = enabled;
            Publish();
        }

        internal void Preview(MenuLightDefinition definition, MenuLightMeshes? meshes)
        {
            _previewId = definition.Id;
            _preview = definition.Enabled && meshes.HasValue
                ? MenuLightSpill.Calculate(meshes.Value, definition.Appearance, _linearColorSpace)
                : Array.Empty<MenuLightSpillSample>();
            Publish();
        }

        internal void RestorePreview()
        {
            if (_previewId == null)
                return;

            _previewId = null;
            _preview = Array.Empty<MenuLightSpillSample>();
            Publish();
        }

        internal void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            Publish();
        }

        public void Dispose()
        {
            SetEnabled(false);
            foreach (Material material in _materials.Values)
                UnityEngine.Object.Destroy(material);

            _materials.Clear();
            _receivers.Clear();
            _saved.Clear();
            _order.Clear();
        }

        private void Publish()
        {
            Array.Clear(_positions, 0, _positions.Length);
            Array.Clear(_energy, 0, _energy.Length);
            Array.Clear(_normals, 0, _normals.Length);
            int count = 0;
            foreach (string id in _order)
                Copy(_previewId == id ? _preview : _saved[id], ref count);

            if (_previewId != null && !_saved.ContainsKey(_previewId))
                Copy(_preview, ref count);

            foreach (Material material in _materials.Values)
            {
                material.SetInt(CountId, _enabled ? count : 0);
                material.SetVectorArray(PositionsId, _positions);
                material.SetVectorArray(EnergyId, _energy);
                material.SetVectorArray(NormalsId, _normals);
            }

            foreach ((MeshRenderer renderer, Material original, Material spill) in _receivers)
                SetAttached(renderer, original, spill, _enabled && count > 0);
        }

        private void Copy(MenuLightSpillSample[] samples, ref int count)
        {
            foreach (MenuLightSpillSample sample in samples)
            {
                _positions[count] = new Vector4(sample.Position.x, sample.Position.y, sample.Position.z, sample.RadiusSquared);
                _energy[count] = sample.Energy;
                _normals[count] = sample.Normal;
                count++;
            }
        }

        private static void SetAttached(MeshRenderer renderer, Material original, Material spill, bool attached)
        {
            if (renderer == null)
                return;

            Material[] current = renderer.sharedMaterials;
            int index = Array.IndexOf(current, spill);
            if (attached)
            {
                if (index >= 0 || Array.IndexOf(current, original) < 0)
                    return;

                Array.Resize(ref current, current.Length + 1);
                current[current.Length - 1] = spill;
                renderer.sharedMaterials = current;
            }
            else if (index >= 0)
            {
                // Remove only our pass so changes another mod made to this renderer survive.
                Material[] remaining = new Material[current.Length - 1];
                Array.Copy(current, 0, remaining, 0, index);
                Array.Copy(current, index + 1, remaining, index, remaining.Length - index);
                renderer.sharedMaterials = remaining;
            }
        }
    }
}
