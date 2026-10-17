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

        internal void Initialize(FerrofluidPrimitiveEffect effect)
        {
            _effect = effect;
        }
    }
}
