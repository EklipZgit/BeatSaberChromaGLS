#if FERROFLUID_TWEAKS
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ChromaGLS.Settings
{
    internal sealed partial class FerrofluidGrabbable
    {
        internal JObject ExportGrabState()
        {
            var grab = new JObject
            {
                ["isHeld"] = IsHeld,
                ["isSpinTesting"] = IsSpinTesting,
                ["magnetHeightAmplification"] = _magnetHeightAmplification,
                ["localGrabAnchor"] = FerrofluidSnapshotData.ToToken(_bodyAnchor),
                ["grabOffset"] = FerrofluidSnapshotData.ToToken(_grabOffset),
                ["rawGrabTarget"] = FerrofluidSnapshotData.ToToken(_rawGrabTarget),
                ["smoothedGrabTarget"] = FerrofluidSnapshotData.ToToken(_smoothedGrabPosition),
                ["previousGrabTarget"] = FerrofluidSnapshotData.ToToken(_previousGrabTarget),
                ["grabSampleTime"] = _grabSampleTime,
                ["grabSampleDeltaTime"] = _grabSampleDeltaTime,
                ["grabDepthInput"] = _grabDepthInput,
                ["grabDepthHeldTime"] = _grabDepthHeldTime,
                ["forceStick"] = FerrofluidSnapshotData.ToToken(_forceStick),
                ["spinStick"] = FerrofluidSnapshotData.ToToken(_spinStick),
                ["dominantPointerRotation"] = FerrofluidSnapshotData.ToToken(_dominantPointerRotation),
                ["playerRight"] = FerrofluidSnapshotData.ToToken(_playerRight),
                ["previousPivotTarget"] = FerrofluidSnapshotData.ToToken(_previousPivotTarget),
                ["previousPivotTargetVelocity"] = FerrofluidSnapshotData.ToToken(_previousPivotTargetVelocity),
                ["heldSwingDampingForceScale"] = _heldSwingDampingForceScale,
                ["spinRadius"] = _spinRadius,
            };

            grab["worldGrabAnchor"] = FerrofluidSnapshotData.ToToken(transform.TransformPoint(_bodyAnchor));
            grab["attachPose"] = _attachTransform != null
                ? (JToken)FerrofluidSnapshotPose.From(_attachTransform.position, _attachTransform.rotation).ToObject()
                : JValue.CreateNull();
            grab["anchorBodyPosition"] = _anchorBody != null
                ? FerrofluidSnapshotData.ToToken(_anchorBody.position)
                : JValue.CreateNull();
            grab["anchorBodyRotation"] = _anchorBody != null
                ? FerrofluidSnapshotData.ToToken(_anchorBody.rotation)
                : JValue.CreateNull();
            grab["jointAnchor"] = FerrofluidSnapshotData.ToToken(_joint.anchor);

            // The rigidbody pose is the physics truth; the transform pose is the interpolated render pose.
            grab["bodyPose"] = FerrofluidSnapshotPose.From(_body.position, _body.rotation).ToObject();
            grab["renderPose"] = FerrofluidSnapshotPose.From(transform.position, transform.rotation).ToObject();

            // Rigidbody.velocity is the center-of-mass velocity; GetPointVelocity at the physics
            // origin (_body.position, matching BodyVelocity) is the origin point velocity, which
            // differs from COM whenever the body spins and from render pose under interpolation.
            Vector3 angular = _body.angularVelocity;
            float angularMagnitude = angular.magnitude;
#pragma warning disable CS0618 // Rigidbody.velocity/angularDrag span every supported Unity version.
            grab["comVelocity"] = FerrofluidSnapshotData.ToToken(_body.velocity);
            grab["originPointVelocity"] = FerrofluidSnapshotData.ToToken(_body.GetPointVelocity(_body.position));
            grab["linearDamping"] = _body.drag;
            grab["angularDamping"] = _body.angularDrag;
#pragma warning restore CS0618
            grab["originPointVelocityInterpolated"] = FerrofluidSnapshotData.ToToken(_body.GetPointVelocity(transform.position));
            grab["angularVelocityRadiansPerSecond"] = FerrofluidSnapshotData.ToToken(angular);
            grab["angularAxisNormalized"] = angularMagnitude > 0.000001f
                ? FerrofluidSnapshotData.ToToken(angular / angularMagnitude)
                : JValue.CreateNull();
            grab["angularMagnitudeRadiansPerSecond"] = angularMagnitude;
            grab["angularMagnitudeRotationsPerSecond"] = angularMagnitude / (Mathf.PI * 2f);
            grab["localCenterOfMass"] = FerrofluidSnapshotData.ToToken(_body.centerOfMass);
            grab["worldCenterOfMass"] = FerrofluidSnapshotData.ToToken(_body.worldCenterOfMass);
            grab["mass"] = _body.mass;
            grab["bodyInertiaTensor"] = FerrofluidSnapshotData.ToToken(_body.inertiaTensor);
            grab["bodyInertiaTensorRotation"] = FerrofluidSnapshotData.ToToken(_body.inertiaTensorRotation);
            grab["interpolation"] = _body.interpolation.ToString();
            grab["collisionDetectionMode"] = _body.collisionDetectionMode.ToString();
            grab["freeCenterOfMass"] = FerrofluidSnapshotData.ToToken(_freeCenterOfMass);
            grab["freeInertiaTensor"] = FerrofluidSnapshotData.ToToken(_freeInertiaTensor);
            grab["freeInertiaRotation"] = FerrofluidSnapshotData.ToToken(_freeInertiaRotation);
            grab["enclosure"] = new JObject
            {
                ["center"] = FerrofluidSnapshotData.ToToken(_enclosure.Center),
                ["radius"] = _enclosure.Radius,
                ["rearPoint"] = FerrofluidSnapshotData.ToToken(_enclosure.RearPoint),
                ["rearNormal"] = FerrofluidSnapshotData.ToToken(_enclosure.RearNormal),
                ["rearExtension"] = _enclosure.RearExtension,
                ["floorY"] = _enclosure.FloorY,
            };
            return grab;
        }
    }
}
#endif
