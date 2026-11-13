#if !V1_29_1
using Unity.Collections;
using UnityEngine;

namespace ChromaGLS.Settings
{
    internal sealed partial class FerrofluidGrabbable
    {
        private int _ownColliderInstanceId;
        private int _floorColliderInstanceId;
        private volatile float _inverseInertiaScale = 1f;
        private bool _contactModifySubscribed;
#if !PRE_V1_40_8
        private volatile float _contactDeltaTime;
        private float _ownMass;
        private float _speculativeContactOffset;
#endif

        private void InitializeContactModification()
        {
            if (!_grabShape.IsSphere)
            {
                _cubeCollider.hasModifiableContacts = true;
                _ownColliderInstanceId = _cubeCollider.GetInstanceID();
#if !PRE_V1_40_8
                _ownMass = _body.mass;
                _speculativeContactOffset = _cubeCollider.contactOffset + Physics.defaultContactOffset;
#endif
            }

            SubscribeContactModification();
        }

        private void CacheFloorContactId(Collider floorCollider)
        {
            _floorColliderInstanceId = floorCollider.GetInstanceID();
        }

        private void UpdateInverseInertiaScale(float deltaTime)
        {
#if !PRE_V1_40_8
            _contactDeltaTime = deltaTime;
#endif
#if PRE_V1_40_8
            _inverseInertiaScale = _grabShape.IsSphere || _attachTransform != null || _spinTestPhase != SpinPhase.Idle
#else
            _inverseInertiaScale = _grabShape.IsSphere || _spinTestPhase != SpinPhase.Idle
#endif
                ? 1f
                : FerrofluidGrabPhysics.EvaluateCubeImpactInverseInertiaScale(Quaternion.Inverse(_body.rotation) * _body.angularVelocity);
        }

        private void SubscribeContactModification()
        {
            if (_contactModifySubscribed || _ownColliderInstanceId == 0 || !isActiveAndEnabled)
                return;
            Physics.ContactModifyEvent += OnContactModify;
            Physics.ContactModifyEventCCD += OnContactModify;
            _contactModifySubscribed = true;
        }

        private void UnsubscribeContactModification()
        {
            if (!_contactModifySubscribed)
                return;
            Physics.ContactModifyEvent -= OnContactModify;
            Physics.ContactModifyEventCCD -= OnContactModify;
            _contactModifySubscribed = false;
        }

        // Physics-thread callback: only cached IDs, mass, offset, and the volatile scale/dt may be read here.
        private void OnContactModify(PhysicsScene scene, NativeArray<ModifiableContactPair> contacts)
        {
            float scale = _inverseInertiaScale;
#if PRE_V1_40_8
            if (scale >= 1f)
                return;
#endif
#if !PRE_V1_40_8
            float deltaTime = _contactDeltaTime;
#endif

            for (int i = 0; i < contacts.Length; i++)
            {
                ModifiableContactPair pair = contacts[i];
                if (pair.colliderInstanceID == _floorColliderInstanceId
                    || pair.otherColliderInstanceID == _floorColliderInstanceId)
                    continue;

                bool first = pair.colliderInstanceID == _ownColliderInstanceId;
                if (!first && pair.otherColliderInstanceID != _ownColliderInstanceId)
                    continue;

#if !PRE_V1_40_8
                int acceptedCount = 0;
                Vector3 relativeVelocity = pair.bodyVelocity - pair.otherBodyVelocity;
                int contactCount = pair.contactCount;
                for (int c = 0; c < contactCount; c++)
                {
                    Vector3 normal = pair.GetNormal(c);
                    float closing = Mathf.Max(0f, -Vector3.Dot(relativeVelocity, normal));
                    if (!FerrofluidGrabPhysics.ShouldIgnoreCubeSpeculativeContact(pair.GetSeparation(c), closing, deltaTime, _speculativeContactOffset))
                        acceptedCount++;
                }

                for (int c = 0; c < contactCount; c++)
                {
                    Vector3 normal = pair.GetNormal(c);
                    float closing = Mathf.Max(0f, -Vector3.Dot(relativeVelocity, normal));
                    if (FerrofluidGrabPhysics.ShouldIgnoreCubeSpeculativeContact(pair.GetSeparation(c), closing, deltaTime, _speculativeContactOffset))
                    {
                        pair.IgnoreContact(c);
                        continue;
                    }

                    if (scale < 1f)
                    {
                        float cap = FerrofluidGrabPhysics.EvaluateCubeImpactMaxImpulse(_ownMass, closing, pair.GetBounciness(c), acceptedCount);
                        pair.SetMaxImpulse(c, Mathf.Min(pair.GetMaxImpulse(c), cap));
                    }
                }

                if (acceptedCount == 0)
                    continue;
#endif

                if (scale < 1f)
                {
                    ModifiableMassProperties mass = pair.massProperties;
                    if (first)
                        mass.inverseInertiaScale *= scale;
                    else
                        mass.otherInverseInertiaScale *= scale;
                    pair.massProperties = mass;
                }
            }
        }
    }
}
#endif
