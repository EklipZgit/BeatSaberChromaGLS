#if FERROFLUID_TWEAKS
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ChromaGLS.Settings
{
    internal sealed partial class FerrofluidPrimitiveEffect
    {
        internal FerrofluidMagnetController MagnetController => _magnetController;

        internal bool IsRendered()
        {
            MeshRenderer renderer = GetComponent<MeshRenderer>();
            return _simulation != null
                && FerrofluidSnapshotCapture.IsRendered(gameObject.activeInHierarchy, renderer != null && renderer.enabled, 1f);
        }

        internal JObject BuildObjectSnapshot()
        {
            var subject = new JObject
            {
                ["name"] = gameObject.name,
                ["instanceId"] = gameObject.GetInstanceID(),
                ["scene"] = gameObject.scene.name,
                ["objectKind"] = "primitive",
                ["shape"] = _shape.ToString(),
                ["roundness"] = _roundness,
                ["chartYawDegrees"] = _chartYawDegrees,
                ["magnetSurfaceGap"] = _magnetSurfaceGap,
                ["sourceWorldScale"] = _sourceWorldScale,
                ["transform"] = new JObject
                {
                    ["pose"] = FerrofluidSnapshotPose.From(transform.position, transform.rotation).ToObject(),
                    ["localScale"] = FerrofluidSnapshotData.ToToken(transform.localScale),
                    ["localToWorld"] = FerrofluidSnapshotMatrix4x4.From(transform.localToWorldMatrix).ToToken(),
                    ["worldToLocal"] = FerrofluidSnapshotMatrix4x4.From(transform.worldToLocalMatrix).ToToken(),
                },
                ["magnet"] = new JObject
                {
                    ["magneticFieldPosition"] = FerrofluidSnapshotData.ToToken(_magneticFieldPosition),
                    ["magneticFieldStrength"] = _magneticFieldStrength,
                    ["magnetTarget"] = FerrofluidSnapshotData.ToToken(_magnetTarget),
                    ["hasPointer"] = _hasPointer,
                    ["handPosition"] = FerrofluidSnapshotData.ToToken(_handPosition),
                    ["handStrength"] = _handStrength,
                    ["hasHand"] = _hasHand,
                },
            };

            subject["gpuSimulation"] = _simulation!.ExportSnapshot(null);
            if (Grabbable != null)
            {
                subject["grabbable"] = Grabbable.ExportGrabState();
                subject["holdPoint"] = Grabbable.IsHeld ? (JToken)BuildHoldPoint(Grabbable.LocalGrabAnchor) : JValue.CreateNull();
            }
            else
            {
                subject["grabbable"] = JValue.CreateNull();
                subject["holdPoint"] = JValue.CreateNull();
            }

            var raycasts = new JObject();
            foreach ((string hand, bool left) in new[] { ("left", true), ("right", false) })
            {
                if (!_magnetController.TryGetPointerRay(left, out Vector3 origin, out Vector3 direction))
                {
                    raycasts[hand] = JValue.CreateNull();
                    continue;
                }

                raycasts[hand] = BuildRaycastSnapshot(origin, direction);
            }

            subject["raycasts"] = raycasts;
            return subject;
        }

        // The live hold anchor maps through the same shape/yaw helpers as raycast hits, so its
        // chart coordinates line up with the exported spike list.
        private JObject BuildHoldPoint(Vector3 localAnchor)
        {
            Vector3 localNormal = FerrofluidEnvironmentObjects.EvaluateSurfaceNormalFromDirection(_shape, localAnchor, _roundness);
            Vector2 uv = FerrofluidEnvironmentObjects.EvaluateSurfaceUv(localAnchor, _chartYawDegrees);
            return new JObject
            {
                ["localPoint"] = FerrofluidSnapshotData.ToToken(localAnchor),
                ["worldPoint"] = FerrofluidSnapshotData.ToToken(transform.TransformPoint(localAnchor)),
                ["chartPosition"] = new JArray((uv.x * 2f) - 1f, (uv.y * 2f) - 1f),
                ["localNormal"] = FerrofluidSnapshotData.ToToken(localNormal),
                ["worldNormal"] = FerrofluidSnapshotData.ToToken(transform.TransformDirection(localNormal).normalized),
                ["localFrameQuaternion"] = FerrofluidSnapshotData.ToToken(Quaternion.LookRotation(localNormal)),
            };
        }

        // TryRaycast intersects the analytic base shape, not the deformed spike surface.
        private JObject BuildRaycastSnapshot(Vector3 origin, Vector3 direction)
        {
            var raycast = new JObject
            {
                ["kind"] = "analyticBaseShape",
                ["rayOrigin"] = FerrofluidSnapshotData.ToToken(origin),
                ["rayDirection"] = FerrofluidSnapshotData.ToToken(direction),
            };
            if (!TryRaycast(origin, direction, out Vector3 position, out Vector3 normal))
            {
                raycast["hit"] = JValue.CreateNull();
                return raycast;
            }

            Vector3 localPoint = transform.InverseTransformPoint(position);
            Vector3 localNormal = transform.InverseTransformDirection(normal).normalized;
            Vector2 uv = FerrofluidEnvironmentObjects.EvaluateSurfaceUv(localPoint, _chartYawDegrees);
            raycast["hit"] = new JObject
            {
                ["worldPosition"] = FerrofluidSnapshotData.ToToken(position),
                ["worldNormal"] = FerrofluidSnapshotData.ToToken(normal),
                ["localPoint"] = FerrofluidSnapshotData.ToToken(localPoint),
                ["localNormal"] = FerrofluidSnapshotData.ToToken(localNormal),
                ["localFrameQuaternion"] = FerrofluidSnapshotData.ToToken(Quaternion.LookRotation(localNormal)),
                ["chartPosition"] = new JArray((uv.x * 2f) - 1f, (uv.y * 2f) - 1f),
            };
            return raycast;
        }
    }
}
#endif
