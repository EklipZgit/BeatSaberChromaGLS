using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;
using VRUIControls;

namespace ChromaGLS.Settings
{
    // The active UI pointer aims one magnet. The other controller supplies a magnet at its hand position.
    internal sealed partial class FerrofluidMagnetController
    {
        private readonly Transform? _leftPointer;
        private readonly Transform? _rightPointer;
        private readonly List<RaycastResult>? _raycastScratch;
        private readonly PointerEventData? _hoverEventData;
        private readonly Transform? _playerAnchor;
        private readonly MenuPlayerController? _menuPlayerController;
        private readonly VRPointer? _vrPointer;
        private readonly EventSystem? _eventSystem;
        private readonly Component? _leftHand;
        private readonly Component? _rightHand;
        private readonly bool _hasControllers;
#if V1_29_1 && FERROFLUID_TWEAKS
        private readonly FirstPersonFlyingController? _fpfc;
#endif

        private FerrofluidMagnetController(
            Transform? leftPointer,
            Transform? rightPointer,
            Transform? playerAnchor,
            MenuPlayerController? menuPlayerController = null,
            VRPointer? vrPointer = null,
            Component? leftHand = null,
            Component? rightHand = null,
#if V1_29_1 && FERROFLUID_TWEAKS
            FirstPersonFlyingController? fpfc = null,
#endif
            bool enableHoverRaycast = false)
        {
            _leftPointer = leftPointer;
            _rightPointer = rightPointer;
            _playerAnchor = playerAnchor;
            _menuPlayerController = menuPlayerController;
            _vrPointer = vrPointer;
            _eventSystem = vrPointer != null ? vrPointer.GetComponent<EventSystem>() : null;
            _leftHand = leftHand;
            _rightHand = rightHand;
#if V1_29_1 && FERROFLUID_TWEAKS
            _fpfc = fpfc;
#endif
            _hasControllers = vrPointer != null && _eventSystem != null && leftHand != null && rightHand != null;
            if (enableHoverRaycast)
            {
                _raycastScratch = new List<RaycastResult>(8);
                _hoverEventData = new PointerEventData(_eventSystem);
            }
        }

        internal Transform? PlayerAnchor => _playerAnchor;

        // Which hand the pointer currently belongs to, so grab crossings match that hand's own samples.
        internal VRController? LeftController => (VRController?)_leftHand;

        internal VRController? RightController => (VRController?)_rightHand;

        internal bool TryGetHandTriggers(out float leftTrigger, out float rightTrigger)
        {
            if (!_hasControllers)
            {
                leftTrigger = 0f;
                rightTrigger = 0f;
                return false;
            }

            leftTrigger = ((VRController)_leftHand!).triggerValue;
            rightTrigger = ((VRController)_rightHand!).triggerValue;
            return true;
        }

        internal Vector2 SampleForceInput(VRController controller)
            => SampleStick(controller, spinKeys: false);

        internal Vector2 SampleSpinInput(VRController controller)
            => SampleStick(controller, spinKeys: true);

        internal int SampleGrabDepthInput(VRController holder)
        {
            if (!holder.active)
                return 0;

            bool closer;
            bool farther;
#if FERROFLUID_TWEAKS
            if (IsFpfc(holder))
            {
                closer = FerrofluidKeyboardInput.GetKey(KeyCode.Z);
                farther = FerrofluidKeyboardInput.GetKey(KeyCode.X);
            }
            else
#endif
            {
                InputDevice device = InputDevices.GetDeviceAtXRNode(holder.node);
                closer = device.TryGetFeatureValue(CommonUsages.primaryButton, out bool primary) && primary;
                farther = device.TryGetFeatureValue(CommonUsages.secondaryButton, out bool secondary) && secondary;
            }

            return closer == farther
                ? 0
                : closer ? -1 : 1;
        }

        internal Vector3 EvaluatePlayerRight()
        {
            Quaternion headRotation = _menuPlayerController != null ? _menuPlayerController.headRot : Quaternion.identity;
            return FerrofluidGrabPhysics.EvaluatePlayerRight(headRotation);
        }

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
#if V1_29_1 && FERROFLUID_TWEAKS
                        return new FerrofluidMagnetController(left, right, menuPlayerController.transform, menuPlayerController, pointer, leftController, rightController, ResolveFpfc(leftController, rightController));
#else
                        return new FerrofluidMagnetController(left, right, menuPlayerController.transform, menuPlayerController, pointer, leftController, rightController);
#endif
                    }
                }
            }

            Plugin.Log.Error("[ChromaGLS Ferrofluid] no active menu controllers were available at effect initialization; virtual-saber magnet disabled for this effect.");
            return new FerrofluidMagnetController(null, null, null);
        }

        // Grab arbitration raycasts every frame; magnet-only controllers skip the hover scratch state.
        internal static FerrofluidMagnetController CreateForGrab()
        {
            FerrofluidMagnetController controller = Create();
            return new FerrofluidMagnetController(
                controller._leftPointer,
                controller._rightPointer,
                controller._playerAnchor,
                controller._menuPlayerController,
                controller._vrPointer,
                controller._leftHand,
                controller._rightHand,
#if V1_29_1 && FERROFLUID_TWEAKS
                controller._fpfc,
#endif
                enableHoverRaycast: true);
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

        // The trigger value of whichever controller the pointer currently selects.
        internal float GetActiveTriggerValue()
        {
            if (!_hasControllers || !_vrPointer!.isActiveAndEnabled || !GetSelectedController(_vrPointer).active)
                return 0f;

            return GetSelectedController(_vrPointer).triggerValue;
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

        // The event system's winning raycast is the only object the pointer is actually over,
        // so nearer UI or physics blockers already exclude the primitives.
        internal bool TryGetPointerState(
            out VRController controller,
            out float triggerValue,
            out RaycastResult hovering)
        {
            if (!_hasControllers || !_vrPointer!.isActiveAndEnabled || !_eventSystem!.isActiveAndEnabled || !GetSelectedController(_vrPointer).active)
            {
                controller = null!;
                triggerValue = 0f;
                hovering = default;
                return false;
            }

            controller = GetSelectedController(_vrPointer);
            triggerValue = controller.triggerValue;

            // pointingOver can describe the previous EventSystem.Update ray from the old hand's aim,
            // so grab arbitration raycasts itself at the currently selected pointer's pose.
            if (_hoverEventData == null || _raycastScratch == null)
            {
                hovering = default;
                return true;
            }

            Transform pointer = ResolveVirtualPointerTransform(controller);
            _hoverEventData.pointerCurrentRaycast = new RaycastResult
            {
                worldPosition = pointer.position,
                worldNormal = pointer.forward.normalized,
            };
            _raycastScratch.Clear();
            _eventSystem!.RaycastAll(_hoverEventData, _raycastScratch);
            hovering = default;
            foreach (RaycastResult hit in _raycastScratch)
            {
                if (hit.gameObject != null)
                {
                    hovering = FerrofluidPrimitiveRaycaster.ResolveSurfaceHit(hit, _raycastScratch);
                    break;
                }
            }

            return true;
        }

        internal void SuppressGrabCursor(bool overGrabbable)
        {
            // VRPointer refreshes the dot each input frame. Suppression must not take over its normal visibility.
            if (overGrabbable)
                _vrPointer!._cursorTransform.gameObject.SetActive(false);
        }

        // Held objects track the same aim anchor the laser pointer uses.
        internal static Transform GetAttachTransform(VRController controller)
        {
            return ResolveVirtualPointerTransform(controller);
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

        private Vector2 SampleStick(VRController controller, bool spinKeys)
        {
            if (!controller.active)
                return Vector2.zero;

            Vector2 raw;
#if FERROFLUID_TWEAKS
            if (IsFpfc(controller))
            {
                raw = SampleRoleKeys(spinKeys);
            }
            else
#endif
            {
#if V1_29_1
                raw = new Vector2(controller.horizontalAxisValue, controller.verticalAxisValue);
#else
                raw = controller.thumbstick;
#endif
            }

            return FerrofluidGrabPhysics.ApplyControllerDeadzone(raw);
        }

#if FERROFLUID_TWEAKS
        private bool IsFpfc(VRController controller)
        {
#if V1_29_1
            return _fpfc != null && _fpfc.isActiveAndEnabled;
#else
            return controller.mouseMode;
#endif
        }

        private static Vector2 SampleRoleKeys(bool spinKeys)
        {
            return spinKeys
                ? FerrofluidGrabPhysics.EvaluateControllerKeyVector(
                    FerrofluidKeyboardInput.GetKey(KeyCode.T),
                    FerrofluidKeyboardInput.GetKey(KeyCode.F),
                    FerrofluidKeyboardInput.GetKey(KeyCode.G),
                    FerrofluidKeyboardInput.GetKey(KeyCode.H))
                : FerrofluidGrabPhysics.EvaluateControllerKeyVector(
                    FerrofluidKeyboardInput.GetKey(KeyCode.I),
                    FerrofluidKeyboardInput.GetKey(KeyCode.J),
                    FerrofluidKeyboardInput.GetKey(KeyCode.K),
                    FerrofluidKeyboardInput.GetKey(KeyCode.L));
        }

#if V1_29_1
        private static FirstPersonFlyingController? ResolveFpfc(VRController? leftController, VRController? rightController)
        {
            foreach (FirstPersonFlyingController fpfc in Resources.FindObjectsOfTypeAll<FirstPersonFlyingController>())
            {
                if (fpfc._controller0 == leftController || fpfc._controller1 == leftController
                    || fpfc._controller0 == rightController || fpfc._controller1 == rightController)
                {
                    return fpfc;
                }
            }

            return null;
        }
#endif
#endif

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

        internal static float EvaluateForwardPlaneIntersectionDistance(
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
