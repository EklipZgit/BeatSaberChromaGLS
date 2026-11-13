using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ChromaGLS.Settings
{
    internal sealed class FerrofluidPrimitiveRaycaster : BaseRaycaster
    {
        private FerrofluidPrimitiveEffect _effect = null!;

        public override Camera? eventCamera => null;

        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            Vector3 origin = eventData.pointerCurrentRaycast.worldPosition;
            Vector3 direction = eventData.pointerCurrentRaycast.worldNormal;

            if (direction.sqrMagnitude < 0.00000001f)
            {
                return;
            }

            if (!_effect.TryRaycast(origin, direction, out Vector3 position, out Vector3 normal))
            {
                return;
            }

            resultAppendList.Add(new RaycastResult
            {
                gameObject = gameObject,
                module = this,
                distance = Vector3.Distance(origin, position),
                worldPosition = position,
                worldNormal = normal,
                screenPosition = eventData.position,
                index = resultAppendList.Count,
            });
        }

        // A VR UI raycaster's own physics hit can outrank our analytic surface hit when its canvas
        // sorting order is higher, even though the physical child collider sits inside the hull.
        // Promote the matching outer hit only when the winner belongs to the same primitive.
        internal static RaycastResult ResolveSurfaceHit(RaycastResult winner, List<RaycastResult> hits)
        {
            foreach (RaycastResult candidate in hits)
            {
                if (candidate.module is FerrofluidPrimitiveRaycaster primitive
                    && (winner.gameObject.transform.IsChildOf(primitive.transform)
                        || (primitive._effect.Grabbable != null
                            && primitive._effect.Grabbable.OwnsColliderGameObject(winner.gameObject))))
                {
                    return candidate;
                }
            }

            return winner;
        }

        internal void Initialize(FerrofluidPrimitiveEffect effect)
        {
            _effect = effect;
        }
    }
}
