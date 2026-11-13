using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChromaGLS.Settings
{
    internal sealed class FerrofluidFloorShadow : IDisposable
    {
        private static readonly int Edge0Id = Shader.PropertyToID("_FerroShadowEdge0");
        private static readonly int Edge1Id = Shader.PropertyToID("_FerroShadowEdge1");
        private static readonly int Edge2Id = Shader.PropertyToID("_FerroShadowEdge2");
        private static readonly int CenterId = Shader.PropertyToID("_FerroShadowCenter");
        private static readonly int QInvId = Shader.PropertyToID("_FerroShadowQInv");
        private static readonly int MinRadiusId = Shader.PropertyToID("_FerroShadowMinRadius");
        private static readonly int SoftnessId = Shader.PropertyToID("_FerroShadowSoftness");
        private static readonly int OpacityId = Shader.PropertyToID("_FerroShadowOpacity");
        private static readonly int ShapeId = Shader.PropertyToID("_FerroShadowShape");
        private static readonly int UseMaskId = Shader.PropertyToID("_FerroShadowUseMask");
        private static readonly int MaskId = Shader.PropertyToID("_FerroShadowMask");
        private static readonly int MaskBoundsId = Shader.PropertyToID("_FerroShadowMaskBounds");

        private readonly Transform _target;
        private readonly FerrofluidSurfaceShape _shape;
        private readonly float _floorY;
        private readonly Vector2 _slope;
        private readonly GameObject _shadowObject;
        private readonly MeshRenderer _renderer;
        private readonly Material _material;
        private readonly Mesh _quad;
        private readonly MaterialPropertyBlock _block = new();
        private readonly FerrofluidGpuSimulation? _simulation;
        private readonly FerrofluidSpikeShadowMask? _mask;

        internal FerrofluidFloorShadow(
            Transform target,
            FerrofluidSurfaceShape shape,
            float floorY,
            Vector3 surfaceToLight,
            Shader shader)
            : this(target, shape, floorY, surfaceToLight, shader, null)
        {
        }

        internal FerrofluidFloorShadow(
            Transform target,
            FerrofluidSurfaceShape shape,
            float floorY,
            Vector3 surfaceToLight,
            Shader shader,
            FerrofluidGpuSimulation? simulation)
        {
            _simulation = simulation;
            _target = target;
            _shape = shape;
            _floorY = floorY;
            _slope = new Vector2(surfaceToLight.x, surfaceToLight.z) / surfaceToLight.y;
            _material = new Material(shader);
            _quad = CreateQuad();
            _shadowObject = new GameObject("ChromaGLS Ferrofluid Floor Shadow", typeof(MeshFilter), typeof(MeshRenderer));
            SceneManager.MoveGameObjectToScene(_shadowObject, target.gameObject.scene);
            _shadowObject.GetComponent<MeshFilter>().sharedMesh = _quad;
            _renderer = _shadowObject.GetComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            if (_simulation != null)
                _mask = new FerrofluidSpikeShadowMask(_simulation.SurfaceMaterial, shader);
            Update();
        }

        internal void Update()
        {
            Matrix4x4 localToWorld = _target.localToWorldMatrix;
            Vector3 halfX = localToWorld.MultiplyVector(new Vector3(0.5f, 0f, 0f));
            Vector3 halfY = localToWorld.MultiplyVector(new Vector3(0f, 0.5f, 0f));
            Vector3 halfZ = localToWorld.MultiplyVector(new Vector3(0f, 0f, 0.5f));
            Vector2 ax = Project(halfX);
            Vector2 ay = Project(halfY);
            Vector2 az = Project(halfZ);
            Vector3 position = _target.position;
            Vector2 center = new Vector2(position.x, position.z) - ((position.y - _floorY) * _slope);

            bool elliptical = _shape != FerrofluidSurfaceShape.RoundedCube;
            float supportDown;
            Vector2 extent;
            if (elliptical)
            {
                float qxx = (ax.x * ax.x) + (ay.x * ay.x) + (az.x * az.x);
                float qxy = (ax.x * ax.y) + (ay.x * ay.y) + (az.x * az.y);
                float qyy = (ax.y * ax.y) + (ay.y * ay.y) + (az.y * az.y);
                float determinant = (qxx * qyy) - (qxy * qxy);
                float inverse = determinant > 0.0000001f ? 1f / determinant : 0f;
                _block.SetVector(QInvId, new Vector4(qyy * inverse, -qxy * inverse, -qxy * inverse, qxx * inverse));
                float trace = qxx + qyy;
                float root = Mathf.Sqrt(Mathf.Max(0f, (trace * trace) - (4f * determinant)));
                _block.SetFloat(MinRadiusId, Mathf.Sqrt(Mathf.Max(0.0001f, (trace - root) * 0.5f)));
                supportDown = Mathf.Sqrt((halfX.y * halfX.y) + (halfY.y * halfY.y) + (halfZ.y * halfZ.y));
                extent = new Vector2(Mathf.Sqrt(qxx), Mathf.Sqrt(qyy));
            }
            else
            {
                supportDown = Mathf.Abs(halfX.y) + Mathf.Abs(halfY.y) + Mathf.Abs(halfZ.y);
                extent = new Vector2(
                    Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x),
                    Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y));
            }

            _block.SetVector(Edge0Id, Edge(ax, ay, az));
            _block.SetVector(Edge1Id, Edge(ay, ax, az));
            _block.SetVector(Edge2Id, Edge(az, ax, ay));
            _block.SetVector(CenterId, new Vector4(center.x, center.y, 0f, 0f));
            _block.SetFloat(ShapeId, elliptical ? 1f : 0f);

            float gap = Mathf.Max(0f, position.y - _floorY - supportDown);
            float softness = 0.04f + (0.16f * gap);
            float opacity = 0.32f / (1f + (0.7f * gap));
            _block.SetFloat(SoftnessId, softness);
            _block.SetFloat(OpacityId, opacity);

            Vector2 quadExtent;
            if (_mask != null)
            {
                // The mask and the shadow quad share the exact same world bounds: projected
                // body extents enlarged by the deformation envelope (scaled by the light
                // slope) plus a blur margin so the soft edge never clips.
                float padding = _simulation!.CalculateShadowDeformationPadding();
                float margin = softness * 1.25f;
                Vector2 maskExtent = new(
                    extent.x + (padding * Mathf.Sqrt(1f + (_slope.x * _slope.x))) + margin,
                    extent.y + (padding * Mathf.Sqrt(1f + (_slope.y * _slope.y))) + margin);
                Vector2 maskInvExtent = new(1f / maskExtent.x, 1f / maskExtent.y);
                _mask.Update(_simulation.SurfaceMesh, localToWorld, center, maskInvExtent, _floorY, _slope, softness);
                _block.SetFloat(UseMaskId, 1f);
                _block.SetTexture(MaskId, _mask.Texture);
                _block.SetVector(MaskBoundsId, new Vector4(
                    center.x - maskExtent.x,
                    center.y - maskExtent.y,
                    0.5f / maskExtent.x,
                    0.5f / maskExtent.y));
                quadExtent = maskExtent;
            }
            else
            {
                _block.SetFloat(UseMaskId, 0f);
                quadExtent = extent + new Vector2(softness, softness);
            }

            _renderer.SetPropertyBlock(_block);

            _shadowObject.transform.SetPositionAndRotation(
                new Vector3(center.x, _floorY + 0.002f, center.y),
                Quaternion.identity);
            _shadowObject.transform.localScale = new Vector3(
                2f * quadExtent.x,
                1f,
                2f * quadExtent.y);
        }

        internal void SetVisible(bool visible)
        {
            if (_shadowObject != null)
                _shadowObject.SetActive(visible);
        }

        public void Dispose()
        {
            if (_shadowObject != null)
            {
                _shadowObject.SetActive(false);
                UnityEngine.Object.Destroy(_shadowObject);
            }

            if (_material != null)
                UnityEngine.Object.Destroy(_material);
            if (_quad != null)
                UnityEngine.Object.Destroy(_quad);
            _mask?.Dispose();
        }

        private Vector2 Project(Vector3 worldAxis)
            => new(worldAxis.x - (worldAxis.y * _slope.x), worldAxis.z - (worldAxis.y * _slope.y));

        private static Vector4 Edge(Vector2 projected, Vector2 other1, Vector2 other2)
        {
            float length = projected.magnitude;
            if (length < 0.000001f)
                return new Vector4(0f, 0f, 0f, 1000000f);

            Vector2 normal = new(-projected.y / length, projected.x / length);
            float support = Mathf.Abs(Vector2.Dot(normal, projected))
                + Mathf.Abs(Vector2.Dot(normal, other1))
                + Mathf.Abs(Vector2.Dot(normal, other2));
            return new Vector4(normal.x, normal.y, 0f, support);
        }

        private static Mesh CreateQuad()
        {
            Mesh quad = new()
            {
                vertices = new[]
                {
                    new Vector3(-0.5f, 0f, -0.5f),
                    new Vector3(0.5f, 0f, -0.5f),
                    new Vector3(-0.5f, 0f, 0.5f),
                    new Vector3(0.5f, 0f, 0.5f),
                },
                normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one },
                triangles = new[] { 0, 2, 1, 1, 2, 3 },
            };
            quad.RecalculateBounds();
            return quad;
        }
    }
}
