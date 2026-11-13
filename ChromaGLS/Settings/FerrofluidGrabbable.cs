using UnityEngine;
#if PRE_V1_42_1
using FerroMaterial = UnityEngine.PhysicMaterial;
using FerroMaterialCombine = UnityEngine.PhysicMaterialCombine;
#else
using FerroMaterial = UnityEngine.PhysicsMaterial;
using FerroMaterialCombine = UnityEngine.PhysicsMaterialCombine;
#endif

namespace ChromaGLS.Settings
{
    // Drives one ferrofluid primitive as a dynamic rigidbody: pointer-trigger point-grab that hangs
    // and swings freely under gravity, release with retained motion, and the shared menu enclosure.
    internal sealed partial class FerrofluidGrabbable : MonoBehaviour
    {
        private const float HeldSwingDampingRatio = 0.225f;
        private const float CubeFaceSettlingMaximumSpeed = 1.5f;
        private const float MaximumAngularSpeed = FerrofluidGrabPhysics.MaximumAngularSpeed;
        private const float SphereRollingDamping = 0.125f;

        private const float DefaultAngularDamping = 0f;
        private const float SphereWallRollingDamping = SphereRollingDamping;
        private const float RollingContactGap = 0.01f;

        private Rigidbody _body = null!;
        private FerrofluidGrabEnclosure _enclosure;
        private FerrofluidGrabShape _grabShape;
        private Transform? _attachTransform;
        private Rigidbody _anchorBody = null!;
        private ConfigurableJoint _joint = null!;
        private Vector3 _bodyAnchor;
        private Vector3 _freeCenterOfMass;
        private Vector3 _freeInertiaTensor;
        private Quaternion _freeInertiaRotation;
        private Vector3 _previousPivotTarget;
        private Vector3 _previousPivotTargetVelocity;
        private Vector3 _grabOffset;
        private Vector3 _smoothedGrabPosition;
        private Vector3 _previousGrabTarget;
        private Vector3 _rawGrabTarget;
        private float _grabSampleTime;
        private float _grabSampleDeltaTime;
        private float _magnetHeightAmplification = 1f;
        private Vector2 _forceStick;
        private Vector2 _spinStick;
        private Quaternion _dominantPointerRotation = Quaternion.identity;
        private Vector3 _playerRight = Vector3.right;
        private float _spinRadius;
        private int _grabDepthInput;
        private float _grabDepthHeldTime;
        private float _heldSwingDampingForceScale;
        private FerrofluidGrabOwnership? _ownership;
        private Collider? _floorCollider;
        private MeshCollider _cubeCollider = null!;
        private Mesh? _cubeContactMesh;
        private FerroMaterial? _colliderMaterial;
        private FerroMaterial? _cornerMaterial;
        private FerroMaterial? _floorContactMaterial;
        private bool _cornerMaterialActive;
        private bool _suspendInterpolation;
        private float _configuredRoundness = -1f;
        private float _incomingYVelocity;
        private float _faceDampingStrength = FerrofluidGrabPhysics.DefaultCubeFaceDampingStrength;
        private float _faceDampingExponent = FerrofluidGrabPhysics.DefaultCubeFaceDampingExponent;
        private float _faceDampingStartAngle = FerrofluidGrabPhysics.DefaultCubeFaceDampingStartAngle;
        private bool _floorAssistanceEnabled = true;
        private float _faceLandingStrength = FerrofluidGrabPhysics.DefaultCubeFaceLandingStrength;
        private SpinPhase _spinTestPhase = SpinPhase.Idle;
        private float _spinTestElapsed;
        private float _spinTestOmega;
        private GameObject? _spinAttach;
        private Vector3 _spinLiftStart;
        private Vector3 _spinLiftEnd;
        private GameObject? _physicsRoot;
        private GameObject? _colliderObject;
        private FerrofluidPhysicsWorld? _world;
        private bool _worldRegistered;
        private float _physicsDeltaTime;
        private Vector3 _previousPhysicsPosition;
        private Quaternion _previousPhysicsRotation = Quaternion.identity;
        private Vector3 _currentPhysicsPosition;
        private Quaternion _currentPhysicsRotation = Quaternion.identity;

        private enum SpinPhase : byte
        {
            Idle,
            Active,
        }

        internal bool IsHeld => _attachTransform != null;

        internal bool IsSpinTesting => _spinTestPhase != SpinPhase.Idle;

        // The desired pivot is filtered once per rendered frame, then resampled by physics.
        internal Vector3 GrabTarget => _smoothedGrabPosition;

        internal Vector3 LocalGrabAnchor => _bodyAnchor;

        internal float MagnetHeightAmplification => _magnetHeightAmplification;

        internal Vector3 BodyVelocity => _body.GetPointVelocity(_body.position);

        internal Vector3 BodyAngularVelocity => _body.angularVelocity;

        internal Rigidbody PhysicsBody => _body;

        internal bool OwnsColliderGameObject(GameObject candidate)
            => ReferenceEquals(candidate, _colliderObject);

        internal void ConfigurePhysicsWorld(FerrofluidPhysicsWorld world)
            => _world = world;

        internal int EvaluatePhysicsSubsteps(float deltaTime)
            => _grabShape.IsSphere
                ? 1
                : FerrofluidGrabPhysics.EvaluateCubePhysicsSubsteps(_body.angularVelocity.magnitude, deltaTime);

        internal void CapturePreviousPhysicsPose()
        {
            _previousPhysicsPosition = _currentPhysicsPosition;
            _previousPhysicsRotation = _currentPhysicsRotation;
        }

        internal void CaptureCurrentPhysicsPose()
        {
            _currentPhysicsPosition = _body.position;
            _currentPhysicsRotation = _body.rotation;
            if (_suspendInterpolation)
            {
                // A floor snap teleports the body, so the renderer must jump and not glide.
                _suspendInterpolation = false;
                _previousPhysicsPosition = _currentPhysicsPosition;
                _previousPhysicsRotation = _currentPhysicsRotation;
            }
        }

        internal void RenderPhysicsPose(float alpha)
            => transform.SetPositionAndRotation(
                Vector3.LerpUnclamped(_previousPhysicsPosition, _currentPhysicsPosition, alpha),
                Quaternion.SlerpUnclamped(_previousPhysicsRotation, _currentPhysicsRotation, alpha));

        private void RegisterPhysicsWorld()
        {
            if (_world == null || _worldRegistered)
                return;

            _world.Register(this);
            _worldRegistered = true;
        }

        private void UnregisterPhysicsWorld()
        {
            if (!_worldRegistered || _world == null)
                return;

            _world.Unregister(this);
            _worldRegistered = false;
        }

        internal void SampleGrabTarget(float deltaTime, float sampleTime)
        {
            if (_attachTransform == null || _spinTestPhase != SpinPhase.Idle)
                return;

            _rawGrabTarget = _attachTransform.TransformPoint(_grabOffset);
            _previousGrabTarget = _smoothedGrabPosition;
            _smoothedGrabPosition = FerrofluidInteraction.TrackPosition(
                _previousGrabTarget,
                _rawGrabTarget,
                _body.transform.lossyScale.x,
                deltaTime);
            _grabSampleTime = sampleTime;
            _grabSampleDeltaTime = deltaTime;
        }

        // Resample the same filtered trajectory at a physics step's clock time: each successive
        // first-order step stays collinear with the remaining gap to the raw pointer, so following
        // the last measured slope can never jump or reverse past a known endpoint.
        internal Vector3 CalculateGrabTarget(float sampleTime)
        {
            Vector3 step = _smoothedGrabPosition - _previousGrabTarget;
            float stepSquared = step.sqrMagnitude;
            if (stepSquared == 0f || _spinTestPhase != SpinPhase.Idle)
                return _smoothedGrabPosition;

            float ahead = Mathf.Max(0f, sampleTime - _grabSampleTime);
            float fraction = ahead / _grabSampleDeltaTime;
            float maximum = Mathf.Max(0f, Vector3.Dot(_rawGrabTarget - _smoothedGrabPosition, step) / stepSquared);
            return _smoothedGrabPosition + (step * Mathf.Min(fraction, maximum));
        }

        internal void Initialize(
            FerrofluidGrabEnclosure enclosure,
            bool sphere,
            FerroMaterial material,
            FerrofluidGrabOwnership? ownership = null)
        {
            _enclosure = enclosure;
            _ownership = ownership;

            // A managed world simulates proxy roots inside its own physics scene; the standalone
            // path keeps the body on this root so unmanaged validations behave as before.
            Transform physicsParent = transform;
            if (_world != null)
            {
                GameObject physicsRoot = new("ChromaGLS Ferrofluid Physics Body");
                physicsRoot.transform.SetPositionAndRotation(transform.position, transform.rotation);
                physicsRoot.transform.localScale = transform.lossyScale;
                _world.MoveRootToScene(physicsRoot);
                physicsRoot.AddComponent<CollisionRelay>().Initialize(this);
                _physicsRoot = physicsRoot;
                physicsParent = physicsRoot.transform;
            }

            _body = physicsParent.gameObject.AddComponent<Rigidbody>();
            _body.useGravity = false;
            _body.interpolation = _world != null
                ? RigidbodyInterpolation.None
                : RigidbodyInterpolation.Interpolate;

            // A 25 m/s throw tunnels through the static floor under ContinuousDynamic in native
            // validation; speculative contacts resolve it on the first contact step.
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _body.maxAngularVelocity = MaximumAngularSpeed;
#pragma warning disable CS0618 // Rigidbody.angularDrag spans every supported Unity version; angularDamping does not exist on the older game installs.
            _body.angularDrag = DefaultAngularDamping;
#pragma warning restore CS0618

            // The collider rides a child so the renderer's layer never has to change.
            GameObject colliderObject = new("ChromaGLS Ferrofluid Body Collider");
            colliderObject.layer = FerrofluidGrabPhysics.EnclosureLayer;
            colliderObject.transform.SetParent(physicsParent, false);
            colliderObject.transform.localPosition = Vector3.zero;
            colliderObject.transform.localRotation = Quaternion.identity;
            colliderObject.transform.localScale = Vector3.one;
            if (sphere)
            {
                SphereCollider collider = colliderObject.AddComponent<SphereCollider>();
                collider.radius = 0.5f;
                collider.sharedMaterial = material;
                _grabShape = FerrofluidGrabShape.ForSphere(0.5f * _body.transform.lossyScale.y);
            }
            else
            {
                _cubeCollider = colliderObject.AddComponent<MeshCollider>();
                _cubeCollider.convex = true;
                _cubeCollider.sharedMaterial = material;
                _cubeContactMesh = FerrofluidContactGeometry.CreateCubeColliderMesh(0f);
                _cubeCollider.sharedMesh = _cubeContactMesh;
                _configuredRoundness = 0f;
                _colliderMaterial = material;
                _cornerMaterial = new FerroMaterial("ferrofluid cube corner")
                {
                    dynamicFriction = FerrofluidGrabPhysics.DefaultCubeCornerFriction,
                    staticFriction = FerrofluidGrabPhysics.DefaultCubeCornerFriction,
                    frictionCombine = FerroMaterialCombine.Minimum,
                    bounciness = FerrofluidGrabPhysics.FloorRestitution,
                    bounceCombine = FerroMaterialCombine.Average,
                    hideFlags = HideFlags.DontSave,
                };

                // Each cube owns its flat floor-contact material; the Multiply pair cancels the
                // floor coefficient so the slider value is the effective grip, letting face
                // slides exceed the shared Minimum cap without retuning shared materials.
                _floorContactMaterial = new FerroMaterial("ferrofluid cube floor")
                {
                    dynamicFriction = FerrofluidGrabPhysics.EvaluateCubeFloorMaterialFriction(FerrofluidGrabPhysics.DefaultCubeFloorFriction),
                    staticFriction = FerrofluidGrabPhysics.EvaluateCubeFloorMaterialFriction(FerrofluidGrabPhysics.DefaultCubeFloorFriction),
                    frictionCombine = FerroMaterialCombine.Multiply,
                    bounciness = material.bounciness,
                    bounceCombine = material.bounceCombine,
                    hideFlags = HideFlags.DontSave,
                };
                _grabShape = FerrofluidGrabShape.ForConvexBox(
                    0.5f * _body.transform.lossyScale,
                    _cubeContactMesh.vertices);
            }

            // A kinematic pivot body plus a free-rotation joint lets the clicked point hang under
            // gravity; the independent anchor never inherits the body's pose.
            GameObject anchorObject = new("ChromaGLS Ferrofluid Grab Anchor")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            // Create the anchor's native body in its final scene so joint teardown and recreation stay safe.
            if (_world != null)
                _world.MoveRootToScene(anchorObject);
            _anchorBody = anchorObject.AddComponent<Rigidbody>();
            _anchorBody.useGravity = false;
            _anchorBody.isKinematic = true;
            _anchorBody.interpolation = RigidbodyInterpolation.None;
            _joint = physicsParent.gameObject.AddComponent<ConfigurableJoint>();
            _joint.autoConfigureConnectedAnchor = false;
            _joint.connectedBody = _anchorBody;
            _joint.connectedAnchor = Vector3.zero;
            _joint.anchor = Vector3.zero;
            _joint.enableCollision = false;
            _joint.xMotion = ConfigurableJointMotion.Free;
            _joint.yMotion = ConfigurableJointMotion.Free;
            _joint.zMotion = ConfigurableJointMotion.Free;
            _joint.angularXMotion = ConfigurableJointMotion.Free;
            _joint.angularYMotion = ConfigurableJointMotion.Free;
            _joint.angularZMotion = ConfigurableJointMotion.Free;

            _freeCenterOfMass = _body.centerOfMass;
            _freeInertiaTensor = _body.inertiaTensor;
            _freeInertiaRotation = _body.inertiaTensorRotation;
            _spinRadius = FerrofluidGrabPhysics.EvaluateSpinRadius(_grabShape);
            _colliderObject = colliderObject;
            _physicsDeltaTime = Time.fixedDeltaTime;
            _currentPhysicsPosition = _body.position;
            _currentPhysicsRotation = _body.rotation;
            _previousPhysicsPosition = _currentPhysicsPosition;
            _previousPhysicsRotation = _currentPhysicsRotation;
#if !V1_29_1
            InitializeContactModification();
#endif

            RegisterPhysicsWorld();
        }

        internal void SetMagnetHeightAmplification(float amplification) => _magnetHeightAmplification = amplification;

        internal void ConfigureFloorContact(Collider floorCollider)
        {
            _floorCollider = floorCollider;
#if !V1_29_1
            CacheFloorContactId(floorCollider);
#endif
        }

        internal void ConfigureContactPhysics(Config config)
        {
            _floorAssistanceEnabled = config.FerrofluidFloorAssistanceEnabled;
            if (_grabShape.IsSphere)
                return;

            _faceLandingStrength = Mathf.Clamp(config.FerrofluidCubeFaceLandingStrength, 0f, 1f);

            _faceDampingStrength = Mathf.Clamp(config.FerrofluidCubeFaceDampingStrength, 0f, 10f);
            _faceDampingExponent = Mathf.Clamp(config.FerrofluidCubeFaceDampingExponent, 0f, 6f);
            _faceDampingStartAngle = Mathf.Clamp(config.FerrofluidCubeFaceDampingStartAngle, 0f, 30f);

            // The owned materials are retuned in place; the shared contact materials never are.
            float floorFriction = FerrofluidGrabPhysics.EvaluateCubeFloorMaterialFriction(config.FerrofluidCubeFloorFriction);
            _floorContactMaterial!.dynamicFriction = floorFriction;
            _floorContactMaterial.staticFriction = floorFriction;
            float cornerFriction = Mathf.Clamp(config.FerrofluidCubeCornerFriction, 0f, 0.05f);
            _cornerMaterial!.dynamicFriction = cornerFriction;
            _cornerMaterial.staticFriction = cornerFriction;
        }

        // The inset core has flat support faces and sharper corners than the surrounding liquid.
        internal void ConfigureCubeRoundness(float roundness)
        {
            if (_grabShape.IsSphere)
                return;
            if (_configuredRoundness == roundness)
                return;

            bool held = _attachTransform != null;
            Vector3 heldCenterOfMass = _body.centerOfMass;
            Vector3 angularVelocity = _body.angularVelocity;
            Vector3 originVelocity = _body.GetPointVelocity(_body.position);
            if (held)
            {
                _body.centerOfMass = _freeCenterOfMass;
                _body.ResetInertiaTensor();
            }

            Mesh mesh = FerrofluidContactGeometry.CreateCubeColliderMesh(roundness);
            Mesh? previousMesh = _cubeContactMesh;
            _cubeContactMesh = mesh;
            _cubeCollider.sharedMesh = mesh;
            _configuredRoundness = roundness;
            _grabShape = FerrofluidGrabShape.ForConvexBox(
                0.5f * _body.transform.lossyScale,
                mesh.vertices);
            if (previousMesh != null)
                Destroy(previousMesh);
            _body.ResetInertiaTensor();
            _freeCenterOfMass = _body.centerOfMass;
            _freeInertiaTensor = _body.inertiaTensor;
            _freeInertiaRotation = _body.inertiaTensorRotation;

            if (held)
            {
                _body.centerOfMass = heldCenterOfMass;
                _body.ResetInertiaTensor();
                float leverSquared = (_freeCenterOfMass - _body.centerOfMass).sqrMagnitude;
                _heldSwingDampingForceScale = EvaluateHeldSwingForceScale(leverSquared);
            }

            _body.angularVelocity = angularVelocity;
#pragma warning disable CS0618 // Rigidbody.velocity spans every supported Unity version.
            _body.velocity = originVelocity - Vector3.Cross(angularVelocity, _body.position - _body.worldCenterOfMass);
#pragma warning restore CS0618
        }

        private float EvaluateHeldSwingForceScale(float leverSquared)
        {
            float lever = Mathf.Sqrt(leverSquared);
            float inertiaCm = (_freeInertiaTensor.x + _freeInertiaTensor.y + _freeInertiaTensor.z) / 3f;
            float inertiaEffective = inertiaCm + (_body.mass * leverSquared);
            float gravitationalStiffness = _body.mass * FerrofluidGrabPhysics.GravityMagnitude * lever;
            float coefficient = 2f * HeldSwingDampingRatio * Mathf.Sqrt(inertiaEffective * gravitationalStiffness);
            return leverSquared > 0f ? coefficient / leverSquared : 0f;
        }

        // Corner tops keep low grip until an adjacent vertex reaches the floor.
        private void UpdateCornerContactMaterial(float deltaTime)
        {
            if (_grabShape.IsSphere)
                return;

            bool held = _attachTransform != null;
            bool cornerTop = !held && _spinTestPhase == SpinPhase.Idle;
            FerroMaterial? flatContact = null;
            Vector3 bodyVelocity = BodyVelocity;
            if (cornerTop || held)
            {
                Quaternion inverse = Quaternion.Inverse(_body.rotation);
                float gap = _body.position.y - _enclosure.FloorY
                    - FerrofluidGrabPhysics.EvaluateSupportExtent(
                        _grabShape,
                        _body.rotation,
                        Vector3.down);
                if (cornerTop
                    && _floorAssistanceEnabled
                    && gap <= 0.002f
                    && _forceStick.sqrMagnitude == 0f
                    && _spinStick.sqrMagnitude == 0f
                    && Mathf.Abs(_incomingYVelocity) <= 0.75f
                    && bodyVelocity.sqrMagnitude <= CubeFaceSettlingMaximumSpeed * CubeFaceSettlingMaximumSpeed)
                {
                    Vector3 candidate = FerrofluidGrabPhysics.EvaluateCubeFaceLandingAngularVelocity(
                        _body.rotation,
                        _body.angularVelocity,
                        deltaTime,
                        _faceLandingStrength);
                    if (candidate != _body.angularVelocity)
                        _body.angularVelocity = candidate;
                }

                Vector3 localUp = inverse * Vector3.up;
                float absX = Mathf.Abs(localUp.x);
                float absY = Mathf.Abs(localUp.y);
                float absZ = Mathf.Abs(localUp.z);
                Vector3 cornerClearances = 2f * Vector3.Scale(
                    new Vector3(absX, absY, absZ),
                    _grabShape.HalfExtents);
                cornerTop = cornerTop
                    && FerrofluidGrabPhysics.ShouldUseCubeCornerFriction(
                        _cornerMaterialActive,
                        gap,
                        cornerClearances,
                        inverse * _body.angularVelocity);

                bool heldEdge = held
                    && Mathf.Min(
                        cornerClearances.x,
                        Mathf.Min(cornerClearances.y, cornerClearances.z)) <= 0.004f;

                // Held faces and edges use the tuned floor grip. Free cubes keep the existing
                // near-flat condition so spinning corner tops retain their contact behavior.
                if (!cornerTop
                    && gap <= RollingContactGap
                    && (Mathf.Max(absX, Mathf.Max(absY, absZ)) >= 0.9848078f || heldEdge))
                    flatContact = _floorContactMaterial;
            }

            // A corner top traveling faster than 0.05 m/s needs normal floor traction; only an
            // effectively stationary top keeps the low corner grip.
            FerroMaterial? target;
            if (cornerTop)
            {
                float horizontalSpeedSquared = (bodyVelocity.x * bodyVelocity.x) + (bodyVelocity.z * bodyVelocity.z);

                target = FerrofluidGrabPhysics.IsCubeCornerStationary(horizontalSpeedSquared)
                    ? _cornerMaterial
                    : _colliderMaterial;
            }
            else
            {
                target = flatContact != null ? flatContact : _colliderMaterial;
            }

            _cornerMaterialActive = cornerTop;
            if (_cubeCollider.sharedMaterial == target)
                return;

            _cubeCollider.sharedMaterial = target;
        }

        private void OnCollisionEnter(Collision collision) => SnapFloorContact(collision, isStay: false);

        private void OnCollisionStay(Collision collision) => SnapFloorContact(collision, isStay: true);

        // Speculative CCD resolves impacts above the floor, so publish the floor-touching pose.
        private void SnapFloorContact(Collision collision, bool isStay)
        {
            if (!_floorAssistanceEnabled)
                return;
            if (_floorCollider == null || collision.collider != _floorCollider)
                return;
            if (_attachTransform != null || _spinTestPhase != SpinPhase.Idle)
                return;

            bool upward = false;
            for (int index = 0; index < collision.contactCount; index++)
            {
                if (collision.GetContact(index).normal.y > 0.9f)
                {
                    upward = true;
                    break;
                }
            }

            if (!upward)
                return;

            float gap = _body.position.y - _enclosure.FloorY
                - FerrofluidGrabPhysics.EvaluateSupportExtent(_grabShape, _body.rotation, Vector3.down);
#pragma warning disable CS0618 // Rigidbody.velocity spans every supported Unity version.
            bool snap = FerrofluidGrabPhysics.ShouldSnapFloorContact(
                gap,
                _incomingYVelocity,
                _body.velocity.y,
                _physicsDeltaTime,
                isStay);
#pragma warning restore CS0618
            if (snap)
            {
                Vector3 position = _body.position;
                position.y = _enclosure.FloorY + FerrofluidGrabPhysics.EvaluateSupportExtent(_grabShape, _body.rotation, Vector3.down);
                _body.position = position;
                _body.interpolation = RigidbodyInterpolation.None;
                _suspendInterpolation = true;
                gap = 0f;
            }

            ApplyCubeFaceSettling(gap, _physicsDeltaTime);
        }

        // Speculative stays can occur above the floor. Ignore those and active input so settling
        // neither brakes an outbound bounce nor prevents an intentional push or spin-up.
        private void ApplyCubeFaceSettling(float gap, float deltaTime)
        {
            if (_grabShape.IsSphere
                || gap > 0.002f
                || _forceStick.sqrMagnitude > 0f
                || _spinStick.sqrMagnitude > 0f
                || Mathf.Abs(_incomingYVelocity) > 0.75f
                || BodyVelocity.sqrMagnitude > CubeFaceSettlingMaximumSpeed * CubeFaceSettlingMaximumSpeed)
            {
                return;
            }

            float rate = FerrofluidGrabPhysics.EvaluateCubeFaceAngularDamping(
                _body.rotation,
                _body.angularVelocity,
                _faceDampingStrength,
                _faceDampingExponent,
                _faceDampingStartAngle);
            if (rate <= 0f)
                return;

            _body.angularVelocity = FerrofluidGrabPhysics.EvaluateCubeFaceSettlingAngularVelocity(
                _body.rotation,
                _body.angularVelocity,
                rate,
                deltaTime);
        }

        internal void SetControllerInput(
            Vector2 forceStick,
            Vector2 spinStick,
            Quaternion dominantPointerRotation,
            Vector3 playerRight)
        {
            _forceStick = forceStick;
            _spinStick = spinStick;
            _dominantPointerRotation = dominantPointerRotation;
            _playerRight = playerRight;
        }

        internal void SetGrabDepthInput(int direction)
        {
            direction = Mathf.Clamp(direction, -1, 1);
            if (direction != _grabDepthInput)
            {
                _grabDepthInput = direction;
                _grabDepthHeldTime = 0f;
            }
        }

        // The clicked surface point is the grab anchor; gravity swings the body around it.
        internal void Acquire(Transform attachTransform, Vector3 worldGrabPoint)
        {
            StopSpinTest();
            AcquirePivot(attachTransform, transform.InverseTransformPoint(worldGrabPoint));
        }

#pragma warning disable CS0618 // Rigidbody.velocity spans every supported Unity version.
        private void AcquirePivot(Transform attachTransform, Vector3 localAnchor)
        {
            _attachTransform = attachTransform;
            _bodyAnchor = localAnchor;
            Vector3 worldBodyAnchor = Vector3.Scale(_bodyAnchor, _body.transform.lossyScale);

            // The kinematic anchor starts at the body's actual physical anchor so acquiring a
            // rendered-pose grab point never snaps the body toward a stale transform.
            Vector3 actualPivot = _body.position + (_body.rotation * worldBodyAnchor);
            Vector3 pivotVelocity = _body.GetPointVelocity(actualPivot);
            Vector3 angularVelocity = _body.angularVelocity;
            _freeCenterOfMass = _body.centerOfMass;
            _freeInertiaTensor = _body.inertiaTensor;
            _freeInertiaRotation = _body.inertiaTensorRotation;

            // Integrating about the grabbed point prevents body rotation from moving that point.
            // Unity recomputes the parallel-axis inertia for the temporary mass frame.
            _body.centerOfMass = worldBodyAnchor;
            _body.ResetInertiaTensor();
            _body.velocity = pivotVelocity;
            _body.angularVelocity = angularVelocity;
            _previousPivotTarget = actualPivot;
            _previousPivotTargetVelocity = Vector3.zero;
            _grabOffset = attachTransform.InverseTransformPoint(actualPivot);
            _smoothedGrabPosition = actualPivot;
            _previousGrabTarget = actualPivot;
            _rawGrabTarget = actualPivot;
            _grabSampleTime = Time.unscaledTime;
            _grabSampleDeltaTime = 0f;
            _forceStick = Vector2.zero;
            _spinStick = Vector2.zero;
            _grabDepthInput = 0;
            _grabDepthHeldTime = 0f;
            _anchorBody.position = actualPivot;
            _anchorBody.rotation = _body.rotation;
            _joint.anchor = _bodyAnchor;
            ClearPivotDrives();

            // Free angular axes leave twist about the pivot-to-center line undamped.
            // The swing damper acts only on the tangential motion of the real center of mass.
            float leverSquared = (_freeCenterOfMass - _body.centerOfMass).sqrMagnitude;
            _heldSwingDampingForceScale = EvaluateHeldSwingForceScale(leverSquared);
            _joint.rotationDriveMode = RotationDriveMode.Slerp;
            _joint.slerpDrive = default;
            if (_world == null)
                _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.WakeUp();
        }

        // Debug spin test: grab the physical top-face point, lift to clear the floor, spin about
        // world-up for two seconds, then hover so the fluid recovery is observable. Session-only.
        internal bool StartSpinTest(float rotationsPerSecond)
        {
            if (_attachTransform != null && _spinTestPhase == SpinPhase.Idle)
                return false;

            StopSpinTest();
            _body.rotation = Quaternion.Euler(0f, _body.rotation.eulerAngles.y, 0f);
            _body.rotation = Quaternion.Euler(0f, _body.rotation.eulerAngles.y, 0f);
            Vector3 topPoint = _body.position + (_body.rotation * new Vector3(0f, 0.5f * _body.transform.lossyScale.y, 0f));
            _spinAttach = new GameObject("ChromaGLS Spin Attach")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            _spinAttach.transform.position = topPoint;
            _spinTestOmega = Mathf.PI * 2f * rotationsPerSecond;
            _spinTestElapsed = 0f;
            _spinLiftStart = topPoint;
            _spinLiftEnd = new Vector3(topPoint.x, _enclosure.FloorY + 0.305f + _body.transform.lossyScale.y, topPoint.z);
            AcquirePivot(_spinAttach.transform, new Vector3(0f, 0.5f, 0f));
            _joint.slerpDrive = default;
            _spinTestPhase = SpinPhase.Active;
            return true;
        }

        internal void StopSpinTest()
        {
            _spinTestPhase = SpinPhase.Idle;
            if (_spinAttach == null)
                return;

            Release();
            Destroy(_spinAttach);
            _spinAttach = null;
        }

        private void UpdateSpinTest(float deltaTime)
        {
            _spinTestElapsed += deltaTime;
            Vector3 target = _spinTestElapsed < 0.3f
                ? Vector3.Lerp(_spinLiftStart, _spinLiftEnd, _spinTestElapsed / 0.3f)
                : _spinLiftEnd;
            _spinAttach!.transform.position = target;
            _smoothedGrabPosition = target;
            if (_spinTestElapsed >= 0.8f && _spinTestElapsed < 2.8f)
                _body.angularVelocity = new Vector3(0f, _spinTestOmega, 0f);
            else if (_spinTestElapsed >= 2.8f)
                _body.angularVelocity = Vector3.zero;
        }

        // Preserve physical release motion, following SteamVR's Throwable interaction pattern:
        // https://github.com/ValveSoftware/steamvr_unity_plugin/blob/056c8236/Assets/SteamVR/InteractionSystem/Core/Scripts/Throwable.cs
        internal void Release()
        {
            _forceStick = Vector2.zero;
            _spinStick = Vector2.zero;
            _grabDepthInput = 0;
            _grabDepthHeldTime = 0f;
            _heldSwingDampingForceScale = 0f;
            if (_attachTransform == null)
                return;

            Vector3 freeCenter = _body.position + (_body.rotation * _freeCenterOfMass);
            Vector3 freeVelocity = _body.GetPointVelocity(freeCenter);
            Vector3 angularVelocity = _body.angularVelocity;
            _body.centerOfMass = _freeCenterOfMass;
            _body.inertiaTensor = _freeInertiaTensor;
            _body.inertiaTensorRotation = _freeInertiaRotation;
            _body.velocity = freeVelocity;
            _body.angularVelocity = angularVelocity;
            ClearPivotDrives();
            _joint.slerpDrive = default;
            _attachTransform = null;
            if (_world == null)
                _body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        private void ClearPivotDrives()
        {
            _joint.xMotion = ConfigurableJointMotion.Free;
            _joint.yMotion = ConfigurableJointMotion.Free;
            _joint.zMotion = ConfigurableJointMotion.Free;
            _joint.angularXMotion = ConfigurableJointMotion.Free;
            _joint.angularYMotion = ConfigurableJointMotion.Free;
            _joint.angularZMotion = ConfigurableJointMotion.Free;
            _joint.xDrive = default;
            _joint.yDrive = default;
            _joint.zDrive = default;
        }

        // The canonical grab target is filtered once per rendered frame by SampleGrabTarget; the
        // joint anchor resamples that same trajectory at this step's clock, then clamps.
        private void UpdateGrabAnchor(float sampleTime, float deltaTime)
        {
            Vector3 desired = CalculateGrabTarget(sampleTime);
            Vector3 grabOffset = _body.rotation * Vector3.Scale(_bodyAnchor, _body.transform.lossyScale);
            Vector3 clampedCenter = FerrofluidGrabPhysics.ClampEnclosureWallPosition(
                _enclosure,
                _grabShape,
                desired - grabOffset,
                _body.rotation);
            Vector3 targetPivot = clampedCenter + grabOffset;
            float minPivotY = _enclosure.FloorY
                + FerrofluidGrabPhysics.EvaluateSupportExtent(_grabShape, _body.rotation, Vector3.down)
                + grabOffset.y;
            targetPivot.y = Mathf.Max(targetPivot.y, minPivotY);
            Vector3 pivotVelocity = _body.velocity;
            Vector3 targetVelocity = (desired - _previousPivotTarget) / deltaTime;
            Vector3 pivotAcceleration = Vector3.ClampMagnitude(
                (targetVelocity - _previousPivotTargetVelocity) / deltaTime,
                FerrofluidGrabPhysics.MaximumGrabAcceleration);
            _previousPivotTarget = desired;
            _previousPivotTargetVelocity = targetVelocity;
            Vector3 centerOffset = _body.rotation * (_freeCenterOfMass - _body.centerOfMass);
            _body.AddTorque(Vector3.Cross(centerOffset, -pivotAcceleration * _body.mass), ForceMode.Force);

            // The collision solver can block this velocity, unlike a position-locked joint.
            _body.velocity = FerrofluidGrabPhysics.EvaluateGrabPointVelocity(
                pivotVelocity,
                targetPivot - (_body.position + grabOffset),
                deltaTime);
            _anchorBody.MovePosition(targetPivot);
        }

        private void FixedUpdate()
        {
            if (_world == null)
                AdvancePhysics(Time.fixedDeltaTime, Time.fixedUnscaledTime);
        }

        // Physics steps resample the filtered trajectory at the step's own clock so the joint
        // anchor matches the rendered sample's age instead of holding the last render value.
        internal void AdvancePhysics(float deltaTime, float sampleTime)
        {
            _physicsDeltaTime = deltaTime;
            if (_world == null && _suspendInterpolation)
            {
                _suspendInterpolation = false;
                _body.interpolation = RigidbodyInterpolation.Interpolate;
            }

            if (_spinTestPhase != SpinPhase.Idle)
                UpdateSpinTest(deltaTime);
            if (_attachTransform != null && _spinTestPhase == SpinPhase.Idle)
                AdvanceGrabDepth(deltaTime);

            if (_attachTransform != null)
                UpdateGrabAnchor(sampleTime, deltaTime);

            if (_attachTransform == null)
            {
                _body.AddForce(0f, -FerrofluidGrabPhysics.GravityMagnitude, 0f, ForceMode.Acceleration);
            }
            else
            {
                Vector3 physicalCenter = _body.position + (_body.rotation * _freeCenterOfMass);
                _body.AddForceAtPosition(
                    Vector3.down * (FerrofluidGrabPhysics.GravityMagnitude * _body.mass),
                    physicalCenter,
                    ForceMode.Force);
                if (_spinTestPhase == SpinPhase.Idle)
                {
                    Vector3 swingDamping = FerrofluidGrabPhysics.EvaluatePivotSwingDampingForce(
                        _body.rotation * (_freeCenterOfMass - _body.centerOfMass),
                        _body.angularVelocity,
                        _heldSwingDampingForceScale);
                    if (swingDamping.sqrMagnitude > 0f)
                        _body.AddForceAtPosition(swingDamping, physicalCenter, ForceMode.Force);
                }
            }

            if (_spinTestPhase == SpinPhase.Idle)
                ApplyControllerInput();

            Vector3 pendingVelocityDelta = Vector3.down * (FerrofluidGrabPhysics.GravityMagnitude * deltaTime);
            Vector3 originVelocity = BodyVelocity;
            FerrofluidGrabPhysics.EnforceEnclosureFromMassFrame(
                _enclosure,
                _grabShape,
                _body.position,
                _body.rotation,
                originVelocity,
                _body.angularVelocity,
                _body.centerOfMass,
                pendingVelocityDelta,
                deltaTime,
                FerrofluidGrabPhysics.EnclosureRestitution,
                out Vector3 position,
                out Vector3 velocity);
            if (position != _body.position)
                _body.position = position;

            if (_grabShape.IsSphere && _attachTransform == null)
            {
                // Free spheres resolve wall contact slip here; cubes keep the current wall response.
                FerrofluidGrabPhysics.ApplySphereWallFriction(
                    originVelocity,
                    velocity,
                    _body.angularVelocity,
                    _grabShape.Radius,
                    _freeInertiaTensor.x / _body.mass,
                    FerrofluidGrabPhysics.SphereWallFriction,
                    out Vector3 wallVelocity,
                    out Vector3 wallSpin);
                velocity = wallVelocity;
                if (wallSpin != _body.angularVelocity)
                    _body.angularVelocity = wallSpin;
            }

            ApplySphereRollingResistance(position, ref velocity, deltaTime);
            ApplyCubeFloorSpinResistance(position, velocity, deltaTime);

            if (velocity != originVelocity)
                _body.velocity += velocity - originVelocity;

            // The contact callback sees the post-impulse velocity; the floor snap needs the speed
            // the body carried into the step to tell arrivals from outbound bounces.
            _incomingYVelocity = _body.velocity.y;

            UpdateCornerContactMaterial(deltaTime);
#if !V1_29_1
            UpdateInverseInertiaScale(deltaTime);
#endif
        }
#pragma warning restore CS0618

        private void AdvanceGrabDepth(float deltaTime)
        {
            if (_attachTransform == null || _grabDepthInput == 0)
                return;

            Vector3 forward = _attachTransform.forward;
            float depth = Mathf.Max(
                0f,
                Vector3.Dot(_attachTransform.TransformPoint(_grabOffset) - _attachTransform.position, forward));
            float delta = FerrofluidGrabPhysics.EvaluateGrabDepthDelta(
                depth,
                _grabDepthInput,
                _grabDepthHeldTime,
                deltaTime);
            _grabDepthHeldTime = Mathf.Min(_grabDepthHeldTime + deltaTime, 0.5f);
            if (delta == 0f)
                return;

            Vector3 worldShift = forward * delta;
            _grabOffset += _attachTransform.InverseTransformVector(worldShift);
            _smoothedGrabPosition += worldShift;
            _previousGrabTarget += worldShift;
            _rawGrabTarget += worldShift;
        }

        private void ApplyControllerInput()
        {
            if (_forceStick.sqrMagnitude > 0f)
            {
                if (_attachTransform != null)
                {
                    Vector3 pivot = _body.position + (_body.rotation * Vector3.Scale(_bodyAnchor, _body.transform.lossyScale));
                    Vector3 physicalCenter = _body.position + (_body.rotation * _freeCenterOfMass);
                    Vector3 acceleration = FerrofluidGrabPhysics.EvaluateHeldForceAcceleration(
                        physicalCenter - pivot,
                        _playerRight,
                        _forceStick);
                    if (acceleration.sqrMagnitude > 0f)
                        _body.AddForceAtPosition(acceleration * _body.mass, physicalCenter, ForceMode.Force);
                }
                else
                {
                    _body.AddForce(
                        FerrofluidGrabPhysics.EvaluateHoverForceAcceleration(_dominantPointerRotation, _forceStick),
                        ForceMode.Acceleration);
                }
            }

            if (_spinStick.sqrMagnitude > 0f)
            {
                Vector3 angularAcceleration = FerrofluidGrabPhysics.EvaluateSpinAngularAcceleration(
                    _playerRight,
                    _spinStick,
                    _spinRadius);
                _body.AddTorque(
                    FerrofluidGrabPhysics.EvaluateInertiaTorque(
                        angularAcceleration,
                        _body.rotation,
                        _freeInertiaRotation,
                        _freeInertiaTensor),
                    ForceMode.Force);
            }
        }

        // A free sphere rolling on the floor or the curved wall bleeds tangential speed at the
        // same rate. Cubes keep the baseline damping.
        private void ApplySphereRollingResistance(Vector3 position, ref Vector3 velocity, float deltaTime)
        {
            if (!_grabShape.IsSphere)
            {
                return;
            }

            if (_attachTransform != null)
            {
#pragma warning disable CS0618 // Rigidbody.angularDrag spans every supported Unity version.
                _body.angularDrag = DefaultAngularDamping;
#pragma warning restore CS0618
                return;
            }

            float gap = position.y - _grabShape.Radius - _enclosure.FloorY;
            bool floorContact = gap <= RollingContactGap && velocity.y <= 0f && _floorAssistanceEnabled;
            Vector3 radial = FerrofluidGrabPhysics.EvaluateEnclosureRadial(_enclosure, position);
            float radialLength = radial.magnitude;
            Vector3 outwardNormal = radialLength > 0.000001f ? radial / radialLength : _enclosure.RearNormal;
            bool wallContact = radialLength + _grabShape.Radius >= _enclosure.Radius - RollingContactGap
                && Vector3.Dot(velocity, outwardNormal) >= -0.0001f;
            float damping = wallContact ? SphereWallRollingDamping : floorContact ? SphereRollingDamping : 0f;
            if (damping > 0f)
            {
                float keep = Mathf.Exp(-damping * deltaTime);
                Vector3 tangential = wallContact
                    ? velocity - (outwardNormal * Vector3.Dot(velocity, outwardNormal))
                    : new Vector3(velocity.x, 0f, velocity.z);
                if (floorContact)
                    tangential.y = 0f;
                velocity -= tangential * (1f - keep);
            }

            if (floorContact && !wallContact)
            {
                // Twist about the floor normal gets its own damping rate independent of the
                // rolling tangent bleed; the torque path keeps the anisotropy exact.
                Vector3 angularVelocity = _body.angularVelocity;
                _body.AddTorque(
                    FerrofluidGrabPhysics.EvaluateInertiaTorque(
                        new Vector3(
                            -angularVelocity.x * SphereRollingDamping,
                            FerrofluidGrabPhysics.EvaluateSphereFloorSpinAngularAcceleration(angularVelocity.y, deltaTime),
                            -angularVelocity.z * SphereRollingDamping),
                        _body.rotation,
                        _freeInertiaRotation,
                        _freeInertiaTensor),
                    ForceMode.Force);
            }

#pragma warning disable CS0618 // Rigidbody.angularDrag spans every supported Unity version.
            _body.angularDrag = floorContact && !wallContact
                ? DefaultAngularDamping
                : damping > 0f ? damping : DefaultAngularDamping;
#pragma warning restore CS0618
        }

        // Point contacts have no lever arm for sliding friction to oppose twist about world up.
        // This floor-only torque targets a ninety-second stop from 20 RPS without adding air drag.
        // Ordinary contact friction can stop other poses sooner.
        private void ApplyCubeFloorSpinResistance(Vector3 position, Vector3 velocity, float deltaTime)
        {
            if (_grabShape.IsSphere || _attachTransform != null || _spinTestPhase != SpinPhase.Idle || !_floorAssistanceEnabled)
                return;

            float gap = position.y
                - FerrofluidGrabPhysics.EvaluateSupportExtent(_grabShape, _body.rotation, Vector3.down)
                - _enclosure.FloorY;
            if (gap > RollingContactGap || velocity.y > 0f)
                return;

            float acceleration = FerrofluidGrabPhysics.EvaluateFloorSpinAngularAcceleration(
                _body.angularVelocity.y,
                deltaTime);
            if (acceleration == 0f)
                return;

            _body.AddTorque(
                FerrofluidGrabPhysics.EvaluateInertiaTorque(
                    Vector3.up * acceleration,
                    _body.rotation,
                    _freeInertiaRotation,
                    _freeInertiaTensor),
                ForceMode.Force);
        }

        private void OnEnable()
        {
#if !V1_29_1
            SubscribeContactModification();
#endif

            if (_physicsRoot != null)
            {
                _physicsRoot.SetActive(true);
                _anchorBody.gameObject.SetActive(true);
            }

            RegisterPhysicsWorld();
        }

        private void OnDisable()
        {
#if !V1_29_1
            UnsubscribeContactModification();
#endif

            UnregisterPhysicsWorld();

            // A disabled actor must not keep simulating or blocking UI rays in the owned scene.
            if (_physicsRoot != null)
            {
                _physicsRoot.SetActive(false);
                _anchorBody.gameObject.SetActive(false);
            }

            StopSpinTest();
            Release();
            _ownership?.ReleaseTarget(this);
        }

        private void OnDestroy()
        {
#if !V1_29_1
            UnsubscribeContactModification();
#endif

            if (_spinAttach != null)
                Destroy(_spinAttach);
            if (_physicsRoot != null)
                Destroy(_physicsRoot);
            if (_anchorBody != null)
                Destroy(_anchorBody.gameObject);
            if (_cornerMaterial != null)
                Destroy(_cornerMaterial);
            if (_floorContactMaterial != null)
                Destroy(_floorContactMaterial);
            if (_cubeContactMesh != null)
                Destroy(_cubeContactMesh);
        }

        // Collision messages fire on the rigidbody's GameObject, which lives in the owned physics
        // scene, so this relay forwards them back to the component on the visual root.
        private sealed class CollisionRelay : MonoBehaviour
        {
            private FerrofluidGrabbable _owner = null!;

            internal void Initialize(FerrofluidGrabbable owner)
                => _owner = owner;

            private void OnCollisionEnter(Collision collision)
                => _owner.SnapFloorContact(collision, isStay: false);

            private void OnCollisionStay(Collision collision)
                => _owner.SnapFloorContact(collision, isStay: true);
        }
    }
}
