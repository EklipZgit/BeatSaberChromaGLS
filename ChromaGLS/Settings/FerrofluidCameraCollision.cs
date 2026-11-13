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
        }

        internal void AdvancePhysics()
            => _body.MovePosition(_head.position);

        // Repositioning without MovePosition avoids injecting velocity when the collider returns.
        internal void SnapToHead()
            => _body.position = _head.position;

        private void FixedUpdate()
            => AdvancePhysics();
    }
}
