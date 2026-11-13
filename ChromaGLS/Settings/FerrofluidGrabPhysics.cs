using HMUI;
using UnityEngine;

namespace ChromaGLS.Settings
{
    internal struct FerrofluidGrabEnclosure
    {
        internal readonly Vector3 Center;
        internal readonly float Radius;
        internal readonly Vector3 RearPoint;
        internal readonly Vector3 RearNormal;
        internal readonly float RearExtension;
        internal readonly float FloorY;

        internal FerrofluidGrabEnclosure(
            Vector3 center,
            float radius,
            Vector3 rearPoint,
            Vector3 rearNormal,
            float floorY)
            : this(center, radius, rearPoint, rearNormal, floorY, 0f)
        {
        }

        internal FerrofluidGrabEnclosure(
            Vector3 center,
            float radius,
            Vector3 rearPoint,
            Vector3 rearNormal,
            float floorY,
            float rearExtension)
        {
            Center = center;
            Radius = radius;
            RearPoint = rearPoint;
            RearNormal = rearNormal;
            FloorY = floorY;
            RearExtension = rearExtension;
        }
    }

    internal struct FerrofluidGrabShape
    {
        internal readonly bool IsSphere;
        internal readonly Vector3 HalfExtents;
        internal readonly float Radius;
        internal readonly Vector3[]? LocalContactVertices;

        internal FerrofluidGrabShape(bool isSphere, Vector3 halfExtents, float radius)
        {
            IsSphere = isSphere;
            HalfExtents = halfExtents;
            Radius = radius;
            LocalContactVertices = null;
        }

        private FerrofluidGrabShape(bool isSphere, Vector3 halfExtents, float radius, Vector3[] localContactVertices)
        {
            IsSphere = isSphere;
            HalfExtents = halfExtents;
            Radius = radius;
            LocalContactVertices = localContactVertices;
        }

        internal static FerrofluidGrabShape ForSphere(float worldRadius)
            => new(true, Vector3.one * worldRadius, worldRadius);

        internal static FerrofluidGrabShape ForBox(Vector3 worldHalfExtents)
            => new(false, worldHalfExtents, 0f);

        // Convex vertex hulls replace the sharp-corner support so rounded colliders rest where the
        // rendered shell actually reaches.
        internal static FerrofluidGrabShape ForConvexBox(Vector3 worldHalfExtents, Vector3[] localContactVertices)
            => new(false, worldHalfExtents, 0f, localContactVertices);
    }

    internal struct FluidSpringState
    {
        internal Vector3 Offset;
        internal Vector3 Velocity;

        internal FluidSpringState(Vector3 offset, Vector3 velocity)
        {
            Offset = offset;
            Velocity = velocity;
        }
    }

    internal static class FerrofluidGrabPhysics
    {
        internal const float GrabTriggerThreshold = 0.8f;
        internal const float MagnetHeightBoost = 0.6f;
        internal const float GravityMagnitude = 9.8f * 1.15f;
        internal const float MaximumGrabAcceleration = 200f;
        internal const float EnclosureRestitution = FloorRestitution;
        internal const float DefaultCubeFaceDampingStrength = 1.2f;
        internal const float DefaultCubeFaceDampingExponent = 2f;
        internal const float DefaultCubeFaceDampingStartAngle = 10f;
        internal const float FloorFriction = 0.15f;
        internal const float MaximumCubeFloorFriction = 0.5f;
        internal const float DefaultCubeFloorFriction = 0.25f;
        internal const float DefaultCubeCornerFriction = 0.0125f;
        internal const float DefaultCubeFaceLandingStrength = 0.55f;

        // A corner top moving faster than 0.05 m/s slides, so the shared material's normal floor
        // grip applies; only an effectively stationary top keeps the low corner grip.
        internal const float CubeCornerMotionSpeedSquared = 0.05f * 0.05f;

        // 6 m/s bounds the extra kick spin-generated contacts can add to a real bump without
        // suppressing the translational bounce the incoming speed already justifies.
        private const float CubeImpactMaximumKickSpeed = 6f;
        internal const int EnclosureLayer = 3;

        internal const float FloorRestitution = 0.468f;
        internal const float ControllerMaximumAcceleration = 12f;
        internal const float MaximumAngularSpeed = 20f * 2f * Mathf.PI;
        internal const float FloorSpinAngularDeceleration = MaximumAngularSpeed / 90f;

        private const float ControllerStickDeadzone = 0.1f;
        private const int EnclosureProjectionPasses = 4;
        private const float FluidSpringMaximumStep = 0.008f;
        private const float RearPlaneDistance = 5f;
        internal const float MenuEnclosureRadiusScale = 1.01648736f;
        internal const float SphereWallFriction = 0.6f;
        private const float MinimumEnclosureRadius = 0.5f;

        private const float SphereFloorSpinDamping = 0.1875f;
        private const float SphereSlowSpinThreshold = 2f * Mathf.PI;
        private const float SphereSlowSpinDeceleration = 0.35f;

        internal static float EvaluateMagnetHeightAmplification(float triggerValue)
            => 1f + (MagnetHeightBoost * Mathf.Clamp01(triggerValue / GrabTriggerThreshold));

        internal static bool IsGrabTriggerCrossing(float previousTrigger, float triggerValue)
            => previousTrigger <= GrabTriggerThreshold && triggerValue > GrabTriggerThreshold;

        internal static bool ShouldReleaseGrab(float triggerValue)
            => triggerValue <= GrabTriggerThreshold;

        internal static Vector2 ApplyControllerDeadzone(Vector2 stick)
        {
            float magnitude = stick.magnitude;
            if (magnitude <= ControllerStickDeadzone)
                return Vector2.zero;

            return stick * (Mathf.Min((magnitude - ControllerStickDeadzone) / (1f - ControllerStickDeadzone), 1f) / magnitude);
        }

#if FERROFLUID_TWEAKS
        internal static Vector2 EvaluateControllerKeyVector(bool up, bool left, bool down, bool right)
            => new(
                (right ? 1f : 0f) - (left ? 1f : 0f),
                (up ? 1f : 0f) - (down ? 1f : 0f));
#endif

        internal static void RouteControllerInput(
            FerrofluidGrabOwnership ownership,
            object? ownerHand,
            object? selectedHand,
            bool hovered,
            object? leftHand,
            object? rightHand,
            out object? dominantHand,
            out object? nonDominantHand)
        {
            dominantHand = ownerHand ?? (hovered ? selectedHand : null);
            object? other = ReferenceEquals(dominantHand, leftHand)
                ? rightHand
                : ReferenceEquals(dominantHand, rightHand)
                    ? leftHand
                    : null;
            nonDominantHand = other != null && ownership.HeldTarget(other) == null
                ? other
                : null;
        }

        internal static Vector3 EvaluatePlayerRight(Quaternion headRotation)
            => Quaternion.Euler(0f, headRotation.eulerAngles.y, 0f) * Vector3.right;

        internal static float EvaluateSpinRadius(FerrofluidGrabShape shape)
            => shape.IsSphere ? shape.Radius : shape.HalfExtents.magnitude;

        internal static Vector3 EvaluateHeldForceAcceleration(
            Vector3 pivotToCenter,
            Vector3 playerRight,
            Vector2 stick)
        {
            Vector3 horizontal = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(pivotToCenter, Vector3.up)).normalized * -stick.x;
            Vector3 vertical = Vector3.Cross(playerRight, Vector3.ProjectOnPlane(pivotToCenter, playerRight)).normalized * stick.y;
            return Vector3.ClampMagnitude(horizontal + vertical, 1f) * ControllerMaximumAcceleration;
        }

        internal static Vector3 EvaluateHoverForceAcceleration(Quaternion pointerRotation, Vector2 stick)
            => (((pointerRotation * Vector3.right) * stick.x) + ((pointerRotation * Vector3.up) * stick.y)) * ControllerMaximumAcceleration;

        internal static Vector3 EvaluateSpinAngularAcceleration(Vector3 playerRight, Vector2 stick, float spinRadius)
        {
            Vector3 axis = (playerRight * stick.y) - (Vector3.up * (stick.x / 3f));
            return axis * (ControllerMaximumAcceleration / Mathf.Max(spinRadius, 0.0001f));
        }

        internal static Vector3 EvaluateInertiaTorque(
            Vector3 angularAcceleration,
            Quaternion bodyRotation,
            Quaternion inertiaRotation,
            Vector3 inertiaTensor)
        {
            Quaternion frame = bodyRotation * inertiaRotation;
            return frame * Vector3.Scale(inertiaTensor, Quaternion.Inverse(frame) * angularAcceleration);
        }

        internal static float EvaluateFloorSpinAngularAcceleration(float angularSpeed, float deltaTime)
            => -Mathf.Clamp(angularSpeed / deltaTime, -FloorSpinAngularDeceleration, FloorSpinAngularDeceleration);

        // Sphere twist about the floor normal is an angular acceleration opposite the spin:
        // the measured contact rate above one revolution plus an additional finish brake below
        // one revolution per second, clamped so the last step lands exactly at rest.
        internal static float EvaluateSphereFloorSpinAngularAcceleration(float angularSpeed, float deltaTime)
        {
            float speed = Mathf.Abs(angularSpeed);
            float lowExtra = SphereSlowSpinDeceleration * (1f - Mathf.Clamp01(speed / SphereSlowSpinThreshold));
            float deceleration = (SphereFloorSpinDamping * speed) + lowExtra;
            return -Mathf.Sign(angularSpeed) * Mathf.Min(speed / deltaTime, deceleration);
        }

        // The alignment derivative keeps this damping curve retreat-only. The final landing
        // step is resolved separately by EvaluateCubeFaceLandingAngularVelocity.
        internal static float EvaluateCubeFaceAngularDamping(
            Quaternion rotation,
            Vector3 angularVelocity,
            float strength,
            float exponent,
            float startAngleDegrees)
        {
            if (strength <= 0f || startAngleDegrees <= 0f)
                return 0f;

            Vector3 faceNormal = EvaluateCubeUpFacingNormal(rotation);
            float alignment = Vector3.Dot(faceNormal, Vector3.up);
            float theta = Mathf.Acos(Mathf.Clamp(alignment, -1f, 1f)) * Mathf.Rad2Deg;
            if (theta > startAngleDegrees)
                return 0f;

            Vector3 normalVelocity = Vector3.Cross(angularVelocity, faceNormal);
            if (normalVelocity.y > 0f)
                return 0f;

            // An exactly flat face can still be tipping away: the second derivative catches the
            // inflection without letting a steady spin about the normal register as retreat.
            if (normalVelocity.y == 0f && Vector3.Cross(angularVelocity, normalVelocity).y >= 0f)
                return 0f;

            return strength * Mathf.Pow(startAngleDegrees / Mathf.Max(theta, 0.1f), exponent);
        }

        // Settling brakes tipping, not twist about the landing face.
        internal static Vector3 EvaluateCubeFaceSettlingAngularVelocity(
            Quaternion rotation,
            Vector3 angularVelocity,
            float rate,
            float deltaTime)
        {
            Vector3 normal = EvaluateCubeUpFacingNormal(rotation);
            Vector3 normalSpin = normal * Vector3.Dot(angularVelocity, normal);
            return normalSpin + ((angularVelocity - normalSpin) * Mathf.Exp(-rate * deltaTime));
        }

        private static Vector3 EvaluateCubeUpFacingNormal(Quaternion rotation)
        {
            Vector3 localUp = Quaternion.Inverse(rotation) * Vector3.up;
            Vector3 axis = Mathf.Abs(localUp.x) >= Mathf.Abs(localUp.y)
                ? (Mathf.Abs(localUp.x) >= Mathf.Abs(localUp.z)
                    ? new Vector3(Mathf.Sign(localUp.x), 0f, 0f)
                    : new Vector3(0f, 0f, Mathf.Sign(localUp.z)))
                : (Mathf.Abs(localUp.y) >= Mathf.Abs(localUp.z)
                    ? new Vector3(0f, Mathf.Sign(localUp.y), 0f)
                    : new Vector3(0f, 0f, Mathf.Sign(localUp.z)));
            return rotation * axis;
        }

        // Soften the last approaching step while leaving some momentum for the contact solver.
        // Normal-axis spin is preserved, and the damping curve still only acts on retreat.
        internal static Vector3 EvaluateCubeFaceLandingAngularVelocity(
            Quaternion rotation,
            Vector3 angularVelocity,
            float deltaTime,
            float strength)
        {
            if (strength <= 0f || angularVelocity.sqrMagnitude == 0f || angularVelocity.sqrMagnitude > Mathf.PI * Mathf.PI * 4f)
                return angularVelocity;

            Vector3 normal = EvaluateCubeUpFacingNormal(rotation);
            float alignment = Vector3.Dot(normal, Vector3.up);
            if (alignment < 0.9848078f || alignment >= 1f)
                return angularVelocity;
            if (Vector3.Cross(angularVelocity, normal).y <= 0f)
                return angularVelocity;

            float theta = Mathf.Acos(Mathf.Clamp(alignment, -1f, 1f));
            Vector3 tipAxis = Vector3.Cross(normal, Vector3.up).normalized;
            float closingSpeed = Vector3.Dot(angularVelocity, tipAxis);
            if (closingSpeed * deltaTime < theta)
                return angularVelocity;

            Quaternion destination = Quaternion.AngleAxis(
                    Vector3.Dot(angularVelocity, normal) * deltaTime * Mathf.Rad2Deg,
                    Vector3.up)
                * Quaternion.FromToRotation(normal, Vector3.up)
                * rotation;
            Quaternion delta = destination * Quaternion.Inverse(rotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f)
                angle -= 360f;

            return Vector3.Lerp(angularVelocity, axis * (angle * Mathf.Deg2Rad / deltaTime), strength);
        }

        // The owned flat-face material multiplies against the floor, so the coefficient is the
        // effective friction divided by the floor's own value; a ratio above one intentionally
        // raises cube face grip past the shared Minimum cap without raising floor friction.
        internal static float EvaluateCubeFloorMaterialFriction(float friction)
            => Mathf.Clamp(friction, 0f, MaximumCubeFloorFriction) / FloorFriction;

        // Manual snaps may only cover contact slop plus one step of downward travel so a
        // shrinking support radius cannot drag the center down centimeters in a single step.
        internal static bool ShouldSnapFloorContact(
            float gap,
            float incomingVerticalVelocity,
            float resolvedVerticalVelocity,
            float deltaTime,
            bool isStay)
        {
            if (incomingVerticalVelocity > 0f)
                return false;
            if (isStay && (resolvedVerticalVelocity < 0f || gap <= 0.001f))
                return false;
            if (!isStay && gap <= 0f)
                return false;

            float stepTravel = Mathf.Max(0f, -incomingVerticalVelocity + (GravityMagnitude * deltaTime)) * deltaTime;
            return gap <= 0.002f + stepTravel;
        }

        // Adjacent vertex heights identify edge contact without penalizing a smoothly tilted top.
        // Different enter/exit clearances keep small contact fluctuations from toggling grip.
        internal static bool ShouldUseCubeCornerFriction(
            bool active,
            float supportGap,
            Vector3 cornerClearances,
            Vector3 localAngularVelocity)
        {
            float minClearance = Mathf.Min(
                cornerClearances.x,
                Mathf.Min(cornerClearances.y, cornerClearances.z));
            return supportGap <= 0.01f
                && minClearance > (active ? 0.002f : 0.004f)
                && IsCubeCornerSpin(active, localAngularVelocity);
        }

        internal static bool IsCubeCornerStationary(float horizontalSpeedSquared)
            => horizontalSpeedSquared <= CubeCornerMotionSpeedSquared;

        private static bool IsCubeCornerSpin(bool active, Vector3 localAngularVelocity)
        {
            float omegaMagnitudeSquared = localAngularVelocity.sqrMagnitude;
            float axisSum = Mathf.Abs(localAngularVelocity.x)
                + Mathf.Abs(localAngularVelocity.y)
                + Mathf.Abs(localAngularVelocity.z);
            return omegaMagnitudeSquared > 0.0025f
                && (axisSum * axisSum)
                    >= 3f * (active ? 0.90f * 0.90f : 0.95f * 0.95f) * omegaMagnitudeSquared;
        }

        // Angular-state-only predicate: fast spin returns a solver-bias scale.
        // The caller applies it to a cube's own external contact pairs, excluding the
        // configured floor; slow bodies keep the native tensor. Engines older than 1.40.8
        // lack the contact velocities and impulse controls the ghost filter and cap need,
        // so they keep the existing 2x diagonal-only bias unchanged.
        internal static float EvaluateCubeImpactInverseInertiaScale(Vector3 localAngularVelocity)
        {
            if (localAngularVelocity.sqrMagnitude < 4f * Mathf.PI * Mathf.PI)
                return 1f;
#if PRE_V1_40_8
            return IsCubeCornerSpin(active: true, localAngularVelocity)
                ? 0.5f
                : 1f;
#else
            return 0.125f;
#endif
        }

        // Substeps bound each step's rotation to twenty degrees; spheres always contribute one.
        internal static int EvaluateCubePhysicsSubsteps(float angularSpeed, float deltaTime)
            => Mathf.Max(1, Mathf.CeilToInt(angularSpeed * deltaTime / (Mathf.PI / 9f)));

        // Intentionally limits non-floor speculative reach to the translational closing budget so a
        // spinning collider's broadphase envelope cannot act as remote solid contacts. Very fast
        // angular-only grazes between steps are not continuously swept; this is not angular CCD.
        internal static bool ShouldIgnoreCubeSpeculativeContact(
            float separation,
            float linearClosingSpeed,
            float deltaTime,
            float contactOffset)
            => separation > contactOffset + (Mathf.Max(0f, linearClosingSpeed) * deltaTime);

        internal static float EvaluateCubeImpactMaxImpulse(float mass, float linearClosingSpeed, float restitution, int contactCount)
            => mass * (CubeImpactMaximumKickSpeed + ((1f + restitution) * Mathf.Max(0f, linearClosingSpeed))) / contactCount;

        internal static Vector3 EvaluatePivotSwingDampingForce(Vector3 pivotToCenter, Vector3 angularVelocity, float forceScale)
            => -forceScale * Vector3.Cross(angularVelocity, pivotToCenter);

        internal static float EvaluateGrabDepthDelta(float depth, int direction, float heldTime, float deltaTime)
        {
            float speedIntegral = GrabDepthSpeedIntegral(heldTime + deltaTime) - GrabDepthSpeedIntegral(heldTime);
            return Mathf.Max(depth * direction * speedIntegral, -depth);
        }

        private static float GrabDepthSpeedIntegral(float time)
        {
            if (time <= 0.5f)
                return (0.1f * time) + (0.9f * time * time);

            return 0.275f + (time - 0.5f);
        }

        internal static Vector3 EvaluateGrabPointVelocity(Vector3 velocity, Vector3 gap, float deltaTime)
        {
            float distance = gap.magnitude;
            if (distance == 0f)
                return Vector3.zero;

            Vector3 direction = gap / distance;
            float forwardSpeed = Mathf.Max(0f, Vector3.Dot(velocity, direction));
            float speed = Mathf.Min(distance / deltaTime, forwardSpeed + (MaximumGrabAcceleration * deltaTime));
            return direction * speed;
        }

        internal static void CaptureGrabOffset(
            Vector3 attachPosition,
            Quaternion attachRotation,
            Vector3 bodyPosition,
            Quaternion bodyRotation,
            out Vector3 localCenterOffset,
            out Quaternion localRotationOffset)
        {
            Quaternion inverse = Quaternion.Inverse(attachRotation);
            localCenterOffset = inverse * (bodyPosition - attachPosition);
            localRotationOffset = inverse * bodyRotation;
        }

        internal static void EvaluateHeldPose(
            Vector3 attachPosition,
            Quaternion attachRotation,
            Vector3 localCenterOffset,
            Quaternion localRotationOffset,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = attachPosition + (attachRotation * localCenterOffset);
            rotation = attachRotation * localRotationOffset;
        }

        internal static Vector3 EvaluateLinearVelocity(Vector3 from, Vector3 to, float deltaTime)
            => (to - from) / Mathf.Max(deltaTime, 0.000001f);

        internal static Vector3 EvaluateAngularVelocity(Quaternion from, Quaternion to, float deltaTime)
        {
            Quaternion delta = to * Quaternion.Inverse(from);
            delta.ToAngleAxis(out float degrees, out Vector3 axis);
            if (degrees > 180f)
                degrees -= 360f;
            if (Mathf.Abs(degrees) < 0.0001f || axis.sqrMagnitude < 0.0001f)
                return Vector3.zero;
            return axis.normalized * (degrees * Mathf.Deg2Rad / Mathf.Max(deltaTime, 0.000001f));
        }

        internal static void EvaluateThrowVelocity(
            Vector3 attachFromPosition,
            Quaternion attachFromRotation,
            Vector3 attachToPosition,
            Quaternion attachToRotation,
            float deltaTime,
            Vector3 bodyPosition,
            out Vector3 velocity,
            out Vector3 angularVelocity)
        {
            velocity = EvaluateLinearVelocity(attachFromPosition, attachToPosition, deltaTime);
            angularVelocity = EvaluateAngularVelocity(attachFromRotation, attachToRotation, deltaTime);
            velocity += Vector3.Cross(angularVelocity, bodyPosition - attachToPosition);
        }

        // Measures a point's horizontal offset from the rear-extruded wall boundary: beyond the
        // circle center the offset is taken from the capsule axis instead of the center point.
        internal static Vector3 EvaluateEnclosureRadial(FerrofluidGrabEnclosure enclosure, Vector3 point)
        {
            Vector3 offset = point - enclosure.Center;
            offset.y = 0f;
            float along = Mathf.Clamp(
                Vector3.Dot(offset, enclosure.RearNormal),
                -enclosure.RearExtension,
                0f);
            return offset - (enclosure.RearNormal * along);
        }

        internal static float EvaluateSupportExtent(FerrofluidGrabShape shape, Quaternion rotation, Vector3 direction)
            => EvaluateSupportExtentWithPoint(shape, rotation, direction, out _);

        internal static float EvaluateSupportExtentWithPoint(
            FerrofluidGrabShape shape,
            Quaternion rotation,
            Vector3 direction,
            out Vector3 localSupportPoint)
        {
            if (shape.IsSphere)
            {
                localSupportPoint = Vector3.zero;
                return shape.Radius;
            }

            Vector3 local = Quaternion.Inverse(rotation) * direction;
            Vector3[]? contactVertices = shape.LocalContactVertices;
            if (contactVertices == null)
            {
                localSupportPoint = new Vector3(
                    Mathf.Abs(local.x) > 0.000001f ? Mathf.Sign(local.x) * 0.5f : 0f,
                    Mathf.Abs(local.y) > 0.000001f ? Mathf.Sign(local.y) * 0.5f : 0f,
                    Mathf.Abs(local.z) > 0.000001f ? Mathf.Sign(local.z) * 0.5f : 0f);
                return (Mathf.Abs(local.x) * shape.HalfExtents.x)
                    + (Mathf.Abs(local.y) * shape.HalfExtents.y)
                    + (Mathf.Abs(local.z) * shape.HalfExtents.z);
            }

            // Convex hull vertices live in unit space: scale the query direction by the full world
            // extents once, then take the deepest vertex projection.
            Vector3 scaledDirection = Vector3.Scale(local, shape.HalfExtents * 2f);
            float extent = float.NegativeInfinity;
            Vector3 supportSum = Vector3.zero;
            int supportCount = 0;
            for (int index = 0; index < contactVertices.Length; index++)
            {
                Vector3 vertex = contactVertices[index];
                float projection = Vector3.Dot(scaledDirection, vertex);
                if (projection > extent + 0.000001f)
                {
                    supportSum = vertex;
                    supportCount = 1;
                }
                else if (Mathf.Abs(projection - extent) <= 0.000001f)
                {
                    supportSum += vertex;
                    supportCount++;
                }

                extent = Mathf.Max(extent, projection);
            }

            // Tied deepest vertices average to the real supporting feature: an edge cancels its
            // third axis and a face cancels two, instead of picking an arbitrary corner.
            localSupportPoint = supportCount > 0 ? supportSum / supportCount : Vector3.zero;
            return extent;
        }

        internal static Vector3 ClampEnclosurePosition(
            FerrofluidGrabEnclosure enclosure,
            FerrofluidGrabShape shape,
            Vector3 position,
            Quaternion rotation)
            => ClampEnclosurePositionCore(enclosure, shape, position, rotation, constrainFloor: true);

        // Held grab anchors clamp the pivot by its worst-case swing reach against the rear-stretched
        // wall capsule and rear plane; the floor stays a real physics contact so resting bodies are not hoisted.
        internal static Vector3 ClampEnclosureWallPosition(
            FerrofluidGrabEnclosure enclosure,
            FerrofluidGrabShape shape,
            Vector3 position,
            Quaternion rotation)
            => ClampEnclosurePositionCore(enclosure, shape, position, rotation, constrainFloor: false);

        // Held targets keep the analytic floor so a controller cannot shove the body through the
        // ground; free bodies leave it to the real physics floor for actual contact and rebound.
        private static Vector3 ClampEnclosurePositionCore(
            FerrofluidGrabEnclosure enclosure,
            FerrofluidGrabShape shape,
            Vector3 position,
            Quaternion rotation,
            bool constrainFloor)
        {
            Vector3 velocity = Vector3.zero;
            for (int pass = 0; pass < EnclosureProjectionPasses; pass++)
                ConstrainWalls(enclosure, shape, ref position, ref velocity, rotation, 0f, constrainFloor);
            return position;
        }

        // The wall capsule and rear wall are analytic so the region can stay unbounded vertically
        // while the floor remains a real Unity collider for the contact solver. The predicted next pose is
        // also constrained so a fast or spinning body never leaves the D for a displayed physics tick.
        internal static void EnforceEnclosure(
            FerrofluidGrabEnclosure enclosure,
            FerrofluidGrabShape shape,
            Vector3 position,
            Quaternion rotation,
            Vector3 velocity,
            Vector3 angularVelocity,
            Vector3 pendingVelocityDelta,
            float deltaTime,
            float restitution,
            out Vector3 constrainedPosition,
            out Vector3 constrainedVelocity)
        {
            EnforceEnclosureFromMassFrame(
                enclosure,
                shape,
                position,
                rotation,
                velocity,
                angularVelocity,
                Vector3.zero,
                pendingVelocityDelta,
                deltaTime,
                restitution,
                out constrainedPosition,
                out constrainedVelocity);
        }

        internal static void EnforceEnclosureFromMassFrame(
            FerrofluidGrabEnclosure enclosure,
            FerrofluidGrabShape shape,
            Vector3 position,
            Quaternion rotation,
            Vector3 velocity,
            Vector3 angularVelocity,
            Vector3 centerOfMass,
            Vector3 pendingVelocityDelta,
            float deltaTime,
            float restitution,
            out Vector3 constrainedPosition,
            out Vector3 constrainedVelocity)
        {
            // Only the side wall capsule and rear plane are analytic for free bodies: the floor stays a
            // real physics collider so impacts keep their CCD rebound instead of being cancelled.
            for (int pass = 0; pass < EnclosureProjectionPasses; pass++)
                ConstrainWalls(enclosure, shape, ref position, ref velocity, rotation, restitution, constrainFloor: false);

            // The solver applies pending forces (gravity) after this callback, so the predicted
            // landing velocity includes them or a fast body slips a little outside every tick.
            Vector3 incoming = velocity + pendingVelocityDelta;
            Quaternion predictedRotation = PredictRotation(rotation, angularVelocity, deltaTime);

            // The origin follows an arc around an offset mass centre, not its instantaneous tangent.
            Vector3 rotationalCorrection = Vector3.zero;
            if (centerOfMass.sqrMagnitude > 0f)
            {
                Vector3 centerOffset = rotation * centerOfMass;
                Vector3 predictedCenterOffset = predictedRotation * centerOfMass;
                rotationalCorrection = ((centerOffset - predictedCenterOffset) / deltaTime)
                    + Vector3.Cross(angularVelocity, centerOffset);
            }

            Vector3 predictedVelocity = incoming + rotationalCorrection;
            Vector3 predicted = position + (predictedVelocity * deltaTime);
            Vector3 incomingStepVelocity = predictedVelocity;

            // Predicted penetration resolves the wall contact on this timestep's frame: the
            // outward component reflects at the shared restitution instead of the position clamp
            // silently stopping the body, while tangential and upward motion stay untouched.
            ConstrainWalls(enclosure, shape, ref predicted, ref predictedVelocity, predictedRotation, restitution, constrainFloor: false);
            ConstrainWalls(enclosure, shape, ref predicted, ref predictedVelocity, rotation, restitution, constrainFloor: false);
            if (predictedVelocity != incomingStepVelocity)
            {
                velocity = predictedVelocity - rotationalCorrection - pendingVelocityDelta;
                predicted = position + (predictedVelocity * deltaTime);
            }

            // Corner-level projections keep the final containment guarantee; the residual folds
            // back into velocity so no displayed tick lands outside.
            Vector3 clampedPredicted = ClampEnclosurePositionCore(enclosure, shape, predicted, predictedRotation, constrainFloor: false);
            clampedPredicted = ClampEnclosurePositionCore(enclosure, shape, clampedPredicted, rotation, constrainFloor: false);
            if (clampedPredicted != predicted)
                velocity += (clampedPredicted - predicted) / Mathf.Max(deltaTime, 0.000001f);

            constrainedPosition = position;
            constrainedVelocity = velocity;
        }

        private static Quaternion PredictRotation(Quaternion rotation, Vector3 angularVelocity, float deltaTime)
        {
            float radians = angularVelocity.magnitude;
            if (radians < 0.000001f)
                return rotation;
            return Quaternion.AngleAxis(radians * Mathf.Rad2Deg * deltaTime, angularVelocity / radians) * rotation;
        }

        internal static FluidSpringState ApplyFluidImpulse(FluidSpringState state, Vector3 deltaVelocity)
        {
            state.Velocity -= deltaVelocity;
            return state;
        }

        internal static FluidSpringState IntegrateFluidSpring(FluidSpringState state, float strength, float damping, float deltaTime)
        {
            float remaining = Mathf.Max(deltaTime, 0f);
            while (remaining > 0f)
            {
                float step = Mathf.Min(remaining, FluidSpringMaximumStep);
                state.Velocity += ((-state.Offset * strength) - (state.Velocity * damping)) * step;
                state.Offset += state.Velocity * step;
                remaining -= step;
            }

            return state;
        }

        internal static bool TryResolveEnclosure(Transform playerAnchor, out FerrofluidGrabEnclosure enclosure)
        {
            Vector3 playerPosition = playerAnchor.position;
            Vector3 forward = playerAnchor.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.000001f ? forward.normalized : Vector3.forward;
            FerrofluidEnvironmentObjects.EvaluatePlacement(
                FerrofluidSurfaceShape.RoundedCube,
                out Vector3 cubeCenter,
                out _);
            FerrofluidEnvironmentObjects.EvaluatePlacement(
                FerrofluidSurfaceShape.Sphere,
                out Vector3 sphereCenter,
                out _);
            Vector3 bestCenter = default;
            float bestRadius = float.MaxValue;
            bool found = false;
            foreach (CurvedCanvasSettings settings in Resources.FindObjectsOfTypeAll<CurvedCanvasSettings>())
            {
                if (!settings.isActiveAndEnabled)
                    continue;
                Canvas canvas = settings.canvas;
                if (canvas == null)
                    continue;
                Transform root = canvas.rootCanvas.transform;
                float sourceRadius = settings.radius;
                float worldRadius = sourceRadius * root.lossyScale.z;
                if (worldRadius < MinimumEnclosureRadius)
                    continue;

                // The curve's circle center sits behind the canvas, so the player inside it is on
                // the canvas's negative-forward side. The menu itself must sit in front of the rig.
                if (Vector3.Dot(root.forward, playerPosition - root.position) >= 0f
                    || Vector3.Dot(forward, root.position - playerPosition) <= 0f)
                    continue;

                Vector3 center = root.TransformPoint(0f, 0f, -sourceRadius);
                if (!CircleContains(center, worldRadius, playerPosition)
                    || !CircleContains(center, worldRadius, cubeCenter)
                    || !CircleContains(center, worldRadius, sphereCenter))
                    continue;

                if (worldRadius < bestRadius)
                {
                    bestRadius = worldRadius;
                    bestCenter = center;
                    found = true;
                }
            }

            if (!found)
            {
                enclosure = default;
                return false;
            }

            enclosure = CreateMenuEnclosure(bestCenter, bestRadius, playerPosition, forward, 0f);
            return true;
        }

        // Keep the front contact point fixed while flattening the menu arc and widening the sides.
        internal static FerrofluidGrabEnclosure CreateMenuEnclosure(
            Vector3 center,
            float radius,
            Vector3 playerPosition,
            Vector3 forward,
            float floorY)
        {
            float expanded = radius * MenuEnclosureRadiusScale;
            Vector3 shifted = center - (forward * (expanded - radius));
            return CreateEnclosure(shifted, expanded, playerPosition, forward, floorY);
        }

        // The rear boundary is a plane tangent to the capsule's round rear cap rather than a
        // blade chopping the corridor through its widest diameter, so the cap pole sits at the
        // requested distance behind the player. Subtracting the radius makes the plane tangent
        // to the cap; a circle already reaching farther back keeps its natural rear cap instead
        // of being shrunk or flattened.
        internal static FerrofluidGrabEnclosure CreateEnclosure(
            Vector3 center,
            float radius,
            Vector3 playerPosition,
            Vector3 forward,
            float floorY)
        {
            Vector3 desiredRear = playerPosition - (forward * RearPlaneDistance);
            float rearExtension = Mathf.Max(
                0f,
                Vector3.Dot(center - desiredRear, forward) - radius);
            Vector3 rearPoint = center - (forward * (rearExtension + radius));
            return new FerrofluidGrabEnclosure(
                center,
                radius,
                rearPoint,
                forward,
                floorY,
                rearExtension);
        }

        // Resolve wall contact slip using the sphere's inertia, so rolling into the wall
        // transfers spin into lift.
        internal static void ApplySphereWallFriction(
            Vector3 incomingVelocity,
            Vector3 reflectedVelocity,
            Vector3 angularVelocity,
            float radius,
            float inertiaPerMass,
            float friction,
            out Vector3 velocity,
            out Vector3 spin)
        {
            velocity = reflectedVelocity;
            spin = angularVelocity;
            Vector3 normalDelta = reflectedVelocity - incomingVelocity;
            normalDelta.y = 0f;
            if (normalDelta.sqrMagnitude <= 0.0000000001f)
                return;

            Vector3 outwardNormal = -normalDelta.normalized;
            Vector3 contactArm = outwardNormal * radius;
            Vector3 slip = reflectedVelocity + Vector3.Cross(angularVelocity, contactArm);
            slip -= outwardNormal * Vector3.Dot(slip, outwardNormal);
            Vector3 requiredDelta = -slip / (1f + ((radius * radius) / inertiaPerMass));
            Vector3 deltaVelocity = Vector3.ClampMagnitude(requiredDelta, friction * normalDelta.magnitude);
            velocity += deltaVelocity;
            spin += Vector3.Cross(contactArm, deltaVelocity) / inertiaPerMass;
        }

        private static bool CircleContains(Vector3 center, float radius, Vector3 point)
        {
            float distanceX = point.x - center.x;
            float distanceZ = point.z - center.z;
            return ((distanceX * distanceX) + (distanceZ * distanceZ)) < radius * radius;
        }

        private static void ConstrainWalls(
            FerrofluidGrabEnclosure enclosure,
            FerrofluidGrabShape shape,
            ref Vector3 position,
            ref Vector3 velocity,
            Quaternion rotation,
            float restitution,
            bool constrainFloor)
        {
            if (constrainFloor)
            {
                ConstrainPlane(
                    Vector3.down,
                    new Vector3(position.x, enclosure.FloorY, position.z),
                    EvaluateSupportExtent(shape, rotation, Vector3.down),
                    ref position,
                    ref velocity,
                    restitution);
            }

            ConstrainPlane(
                -enclosure.RearNormal,
                enclosure.RearPoint,
                EvaluateSupportExtent(shape, rotation, enclosure.RearNormal),
                ref position,
                ref velocity,
                restitution);
            if (shape.IsSphere)
            {
                Vector3 radial = EvaluateEnclosureRadial(enclosure, position);
                float distance = radial.magnitude;
                Vector3 radialDirection = distance > 0.000001f ? radial / distance : enclosure.RearNormal;
                float overflow = (distance + shape.Radius) - enclosure.Radius;
                if (overflow > 0f)
                {
                    position -= radialDirection * overflow;
                    float outward = Vector3.Dot(velocity, radialDirection);
                    if (outward > 0f)
                        velocity -= radialDirection * (outward * (1f + restitution));
                }

                return;
            }

            // A rotated box's transverse corners matter: the capsule test keeps the worst corner's
            // radial distance inside the swept circle. Shrinking the center's radial offset is convex
            // in the scale factor, so bisection finds the exact boundary without oscillating.
            if (MaxBoxCornerRadial(enclosure, shape, position, rotation, out _) > enclosure.Radius)
            {
                float low = 0f;
                float high = 1f;
                Vector3 centerOffset = EvaluateEnclosureRadial(enclosure, position);
                for (int iteration = 0; iteration < 16; iteration++)
                {
                    float mid = (low + high) * 0.5f;
                    Vector3 probe = position - (centerOffset * (1f - mid));
                    if (MaxBoxCornerRadial(enclosure, shape, probe, rotation, out _) > enclosure.Radius)
                        high = mid;
                    else
                        low = mid;
                }

                position -= centerOffset * (1f - low);
                Vector3 radial = EvaluateEnclosureRadial(enclosure, position);
                float radialDistance = radial.magnitude;
                Vector3 radialDirection = radialDistance > 0.000001f ? radial / radialDistance : enclosure.RearNormal;
                float outward = Vector3.Dot(velocity, radialDirection);
                if (outward > 0f)
                    velocity -= radialDirection * (outward * (1f + restitution));
            }
        }

        private static float MaxBoxCornerRadial(
            FerrofluidGrabEnclosure enclosure,
            FerrofluidGrabShape shape,
            Vector3 position,
            Quaternion rotation,
            out int worstCorner)
        {
            float worst = 0f;
            worstCorner = 0;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = new(
                    (corner & 1) == 0 ? -shape.HalfExtents.x : shape.HalfExtents.x,
                    (corner & 2) == 0 ? -shape.HalfExtents.y : shape.HalfExtents.y,
                    (corner & 4) == 0 ? -shape.HalfExtents.z : shape.HalfExtents.z);
                float radius = EvaluateEnclosureRadial(enclosure, position + (rotation * local)).magnitude;
                if (radius > worst)
                {
                    worst = radius;
                    worstCorner = corner;
                }
            }

            return worst;
        }

        private static void ConstrainPlane(
            Vector3 outwardNormal,
            Vector3 planePoint,
            float inwardExtent,
            ref Vector3 position,
            ref Vector3 velocity,
            float restitution)
        {
            float depth = Vector3.Dot(position - planePoint, -outwardNormal);
            float penetration = inwardExtent - depth;
            if (penetration <= 0f)
                return;
            position -= outwardNormal * penetration;
            float outward = Vector3.Dot(velocity, outwardNormal);
            if (outward > 0f)
                velocity -= outwardNormal * (outward * (1f + restitution));
        }
    }

    // Both hands are sampled every input frame so a trigger rise is never lost when the pointer
    // switches hands on the same frame the trigger crosses the threshold.
    internal sealed class FerrofluidTriggerTracker
    {
        private float _leftPrevious;
        private float _rightPrevious;
        private bool _initialized;

        internal void Update(float leftTrigger, float rightTrigger, out bool leftCrossing, out bool rightCrossing)
        {
            if (!_initialized)
            {
                _initialized = true;
                _leftPrevious = leftTrigger;
                _rightPrevious = rightTrigger;
            }

            leftCrossing = FerrofluidGrabPhysics.IsGrabTriggerCrossing(_leftPrevious, leftTrigger);
            rightCrossing = FerrofluidGrabPhysics.IsGrabTriggerCrossing(_rightPrevious, rightTrigger);
            _leftPrevious = leftTrigger;
            _rightPrevious = rightTrigger;
        }
    }

    // At most two hands and two targets: a hand holds at most one object, an object has at most one owner.
    internal sealed class FerrofluidGrabOwnership
    {
        private object? _firstTarget;
        private object? _firstHand;
        private object? _secondTarget;
        private object? _secondHand;

        internal bool TryAcquire(object hand, object target)
        {
            if (OwnerOf(target) != null || HeldTarget(hand) != null)
                return false;
            if (_firstTarget == null)
            {
                _firstTarget = target;
                _firstHand = hand;
                return true;
            }

            if (_secondTarget == null)
            {
                _secondTarget = target;
                _secondHand = hand;
                return true;
            }

            return false;
        }

        internal object? OwnerOf(object target)
        {
            if (_firstTarget == target)
                return _firstHand;
            if (_secondTarget != target)
                return null;
            return _secondHand;
        }

        internal object? HeldTarget(object hand)
        {
            if (_firstHand == hand)
                return _firstTarget;
            if (_secondHand != hand)
                return null;
            return _secondTarget;
        }

        internal object? ReleaseTarget(object target)
        {
            object? hand = OwnerOf(target);
            if (_firstTarget == target)
            {
                _firstTarget = null;
                _firstHand = null;
            }

            if (_secondTarget == target)
            {
                _secondTarget = null;
                _secondHand = null;
            }

            return hand;
        }

        internal object? ReleaseHand(object hand)
        {
            object? target = HeldTarget(hand);
            if (target != null)
                ReleaseTarget(target);
            return target;
        }
    }
}
