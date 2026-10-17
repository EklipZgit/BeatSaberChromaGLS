using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using VRUIControls;

namespace ChromaGLS.Settings
{
    // The active UI pointer aims one magnet. The other controller supplies a magnet at its hand position.
    internal sealed class FerrofluidMagnetController
    {
        private readonly Transform? _leftPointer;
        private readonly Transform? _rightPointer;
        private readonly Transform? _playerAnchor;
        private readonly VRPointer? _vrPointer;
        private readonly EventSystem? _eventSystem;
        private readonly Component? _leftHand;
        private readonly Component? _rightHand;
        private readonly bool _hasControllers;

        private FerrofluidMagnetController(
            Transform? leftPointer,
            Transform? rightPointer,
            Transform? playerAnchor,
            VRPointer? vrPointer = null,
            Component? leftHand = null,
            Component? rightHand = null)
        {
            _leftPointer = leftPointer;
            _rightPointer = rightPointer;
            _playerAnchor = playerAnchor;
            _vrPointer = vrPointer;
            _eventSystem = vrPointer != null ? vrPointer.GetComponent<EventSystem>() : null;
            _leftHand = leftHand;
            _rightHand = rightHand;
            _hasControllers = vrPointer != null && _eventSystem != null && leftHand != null && rightHand != null;
        }

        internal Transform? PlayerAnchor => _playerAnchor;

        internal static FerrofluidMagnetController Create()
        {
            foreach (MenuPlayerController menuPlayerController in FindActiveMenuPlayerControllers())
            {
                VRController leftController = menuPlayerController.leftController;
                VRController rightController = menuPlayerController.rightController;

                // Use Beat Saber's UI ray anchors so the SongCore button and settings preview follow the same pointer.
                Transform? left = leftController != null ? ResolveVirtualPointerTransform(leftController) : null;
                Transform? right = rightController != null ? ResolveVirtualPointerTransform(rightController) : null;
                if (left == null && right == null)
                {
                    continue;
                }

                foreach (VRPointer pointer in Resources.FindObjectsOfTypeAll<VRPointer>())
                {
                    if (pointer.gameObject.activeInHierarchy
                        && (GetSelectedController(pointer) == leftController || GetSelectedController(pointer) == rightController))
                    {
                        Plugin.Log.Info("[ChromaGLS Ferrofluid] magnets use the selected UI pointer and the other controller's hand position, with 0.06s position smoothing.");
                        return new FerrofluidMagnetController(left, right, menuPlayerController.transform, pointer, leftController, rightController);
                    }
                }
            }

            Plugin.Log.Error("[ChromaGLS Ferrofluid] no active menu controllers were available at effect initialization; virtual-saber magnet disabled for this effect.");
            return new FerrofluidMagnetController(null, null, null);
        }

        internal static Transform? ResolvePlayerAnchor()
        {
            return FindMenuPlayerController()?.transform;
        }

        // Let the game choose the active hand, including trigger-driven pointer switches.
        internal bool TryGetActiveControllerRay(
            out Vector3 rayOrigin,
            out Vector3 rayDirection)
        {
            if (!_hasControllers || !_vrPointer!.isActiveAndEnabled || !_eventSystem!.isActiveAndEnabled || !GetSelectedController(_vrPointer).active)
            {
                rayOrigin = default;
                rayDirection = default;
                return false;
            }

            Transform controller = GetSelectedController(_vrPointer) == _leftHand ? _leftPointer! : _rightPointer!;
            rayOrigin = controller.position;
            rayDirection = controller.forward.normalized;
            return true;
        }

        internal bool TryGetInactiveHandPosition(out Vector3 position)
        {
            if (!_hasControllers || !_vrPointer!.isActiveAndEnabled)
            {
                position = default;
                return false;
            }

            VRController hand = (VRController)(GetSelectedController(_vrPointer) == _leftHand ? _rightHand! : _leftHand!);
            position = hand.transform.position;
            return hand.active;
        }

        // Use the rendered world plane because the Transform alone does not include SongCore's menu curvature.
        internal bool TryGetIntersection(
            Vector3 planePoint,
            Vector3 planeNormal,
            out Vector3 worldIntersection)
        {
            if (!TryGetActiveControllerRay(out Vector3 origin, out Vector3 direction))
            {
                worldIntersection = default;
                return false;
            }

            float distance = EvaluateForwardPlaneIntersectionDistance(origin, direction, planePoint, planeNormal);
            if (float.IsPositiveInfinity(distance))
            {
                worldIntersection = default;
                return false;
            }

            worldIntersection = origin + (direction * distance);
            return true;
        }

        private static VRController GetSelectedController(VRPointer pointer)
        {
#if V1_29_1
            return pointer.vrController;
#else
            return pointer.lastSelectedVrController;
#endif
        }

        private static MenuPlayerController? FindMenuPlayerController()
        {
            foreach (MenuPlayerController component in FindActiveMenuPlayerControllers())
            {
                return component;
            }

            return null;
        }

        // Skip inactive or controller-less menu players so they cannot hide a later usable rig.
        private static IEnumerable<MenuPlayerController> FindActiveMenuPlayerControllers()
        {
            foreach (MenuPlayerController controller in Resources.FindObjectsOfTypeAll<MenuPlayerController>())
            {
                if (controller.gameObject.activeInHierarchy)
                {
                    yield return controller;
                }
            }
        }

        private static Transform ResolveVirtualPointerTransform(VRController controller)
        {
#if V1_29_1
            return controller.transform;
#else
            return controller.viewAnchorTransform;
#endif
        }

        private static float EvaluateForwardPlaneIntersectionDistance(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            Vector3 planePoint,
            Vector3 planeNormal)
        {
            Vector3 normalizedDirection = rayDirection.normalized;
            float denominator = Vector3.Dot(normalizedDirection, planeNormal);
            if (Mathf.Abs(denominator) < 0.000001f)
            {
                return float.PositiveInfinity;
            }

            float distance = Vector3.Dot(planePoint - rayOrigin, planeNormal) / denominator;
            return distance > 0f
                ? distance
                : float.PositiveInfinity;
        }
    }
}
