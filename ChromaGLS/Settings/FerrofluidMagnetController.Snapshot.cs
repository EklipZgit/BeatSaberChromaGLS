#if FERROFLUID_TWEAKS
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ChromaGLS.Settings
{
    internal sealed partial class FerrofluidMagnetController
    {
        // The pointer anchor name records which transform feeds the virtual ray: 1.29.1 uses the
        // controller transform itself, newer versions use the separate viewAnchorTransform.
        private const string PointerAnchorName =
#if V1_29_1
            "controllerTransform";
#else
            "viewAnchorTransform";
#endif

        // FPFC has no analog channel, so a held mouse button reports a full trigger rather than the
        // platform's sampled value; VR keeps its analog pull.
        internal static float EvaluateEffectiveTrigger(bool fpfc, bool mouseHeld, float sampledTrigger)
            => fpfc ? (mouseHeld ? 1f : 0f) : sampledTrigger;

        internal bool TryGetPointerRay(bool left, out Vector3 origin, out Vector3 direction)
        {
            Transform? pointer = left ? _leftPointer : _rightPointer;
            if (pointer == null)
            {
                origin = default;
                direction = default;
                return false;
            }

            origin = pointer.position;
            direction = pointer.forward.normalized;
            return true;
        }

        internal JArray ExportHands()
        {
            VRController? selected = _vrPointer != null && _vrPointer.isActiveAndEnabled
                ? GetSelectedController(_vrPointer)
                : null;
            var hands = new JArray
            {
                ExportHand("left", _leftHand as VRController, _leftPointer, selected),
                ExportHand("right", _rightHand as VRController, _rightPointer, selected),
            };
            return hands;
        }

        private JObject ExportHand(string name, VRController? controller, Transform? pointer, VRController? selected)
        {
            var hand = new JObject { ["name"] = name };
            if (controller == null)
            {
                hand["available"] = false;
                return hand;
            }

            hand["available"] = true;
            hand["active"] = controller.active;
            hand["node"] = controller.node.ToString();
            bool fpfc = IsFpfc(controller);
            hand["fpfc"] = fpfc;
            float sampledTrigger = controller.triggerValue;
            bool mouseHeld = Input.GetMouseButton(0);
            hand["sampledTrigger"] = sampledTrigger;
            hand["fpfcMouse0Held"] = fpfc && mouseHeld;

            // FPFC has no analog trigger: the user requires 1 (100%) while the mouse button is held.
            float trigger = EvaluateEffectiveTrigger(fpfc, mouseHeld, sampledTrigger);
            hand["trigger"] = trigger;
            hand["triggerPercent"] = trigger * 100f;
            hand["handPose"] = FerrofluidSnapshotPose.From(controller.transform.position, controller.transform.rotation).ToObject();
            if (pointer != null)
            {
                hand["virtualPointer"] = new JObject
                {
                    ["anchor"] = PointerAnchorName,
                    ["selected"] = ReferenceEquals(controller, selected),
                    ["origin"] = FerrofluidSnapshotData.ToToken(pointer.position),
                    ["rotation"] = FerrofluidSnapshotData.ToToken(pointer.rotation),
                    ["euler"] = FerrofluidSnapshotData.ToToken(pointer.rotation.eulerAngles),
                    ["direction"] = FerrofluidSnapshotData.ToToken(pointer.forward.normalized),
                };
            }
            else
            {
                hand["virtualPointer"] = JValue.CreateNull();
            }

            return hand;
        }
    }
}
#endif
