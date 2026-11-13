using UnityEngine;
#if PRE_V1_42_1
using FerroMaterial = UnityEngine.PhysicMaterial;
#else
using FerroMaterial = UnityEngine.PhysicsMaterial;
#endif

namespace ChromaGLS.Settings
{
    // A kinematic sphere that tracks the actual view transform so ferrofluid bodies rebound off
    // the player's head through native PhysX instead of passing through the camera.
    internal sealed class FerrofluidCameraCollision : MonoBehaviour
    {
        private Transform _head = null!;
        private Rigidbody _body = null!;
        private FerrofluidPhysicsWorld? _world;
        private Vector3 _stepStartPosition;
        private Vector3 _stepEndPosition;

        internal void Initialize(Transform head, float radius, FerroMaterial material)
        {
            _head = head;
            Rigidbody body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.None;
            SphereCollider collider = gameObject.AddComponent<SphereCollider>();
            collider.radius = radius;
            collider.sharedMaterial = material;
            _body = body;
            _body.position = head.position;
            _stepStartPosition = head.position;
            _stepEndPosition = head.position;
        }

        internal void ConfigurePhysicsWorld(FerrofluidPhysicsWorld world)
            => _world = world;

        internal void AdvancePhysics()
            => _body.MovePosition(_head.position);

        // Repositioning without MovePosition avoids injecting velocity when the collider returns.
        internal void SnapToHead()
        {
            _body.position = _head.position;
            _stepStartPosition = _head.position;
            _stepEndPosition = _head.position;
        }

        // Head motion is sampled once per parent step, then spread across the substeps so the
        // kinematic body sweeps the same path at each finer step.
        internal void SampleMotionEndpoints()
        {
            _stepStartPosition = _body.position;
            _stepEndPosition = _head.position;
        }

        internal void AdvanceSubstepPosition(float fraction)
            => _body.MovePosition(Vector3.LerpUnclamped(_stepStartPosition, _stepEndPosition, fraction));

        private void FixedUpdate()
        {
            if (_world == null)
                AdvancePhysics();
        }
    }
}
