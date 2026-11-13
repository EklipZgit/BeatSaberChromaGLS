#if FERROFLUID_TWEAKS
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ChromaGLS.Settings
{
    internal sealed partial class ChromaGlsRequirementEffect
    {
        internal FerrofluidMagnetController MagnetController => _magnetController;

        // The preview uses its own anchor; the requirement border reports the real menu fade.
        internal JObject? BuildObjectSnapshot()
        {
            if (_simulation == null || _sceneRenderer == null)
                return null;
            float fade = _isPreview ? 1f : _fadeAlpha;
            if (!FerrofluidSnapshotCapture.IsRendered(gameObject.activeInHierarchy, _sceneRenderer.enabled, fade))
                return null;

            Rect rect = rectTransform.rect;
            FerrofluidGpuSimulation.Geometry geometry = FerrofluidGpuSimulation.EvaluateGeometry(rect);
            var torusChart = new FerrofluidTorusChartGeometry(
                rect.center,
                geometry.HalfExtents,
                geometry.CornerRadius,
                _viewerLocalZSign,
                rect.width / 6f);

            var subject = new JObject
            {
                ["name"] = gameObject.name,
                ["instanceId"] = gameObject.GetInstanceID(),
                ["scene"] = gameObject.scene.name,
                ["objectKind"] = _isPreview ? "settingsPreviewTorus" : "requirementBorderTorus",
                ["shape"] = "Torus",
                ["isPreview"] = _isPreview,
                ["fadeAlpha"] = _fadeAlpha,
                ["rendererEnabled"] = _sceneRenderer.enabled,
                ["rect"] = new JObject
                {
                    ["x"] = rect.x,
                    ["y"] = rect.y,
                    ["width"] = rect.width,
                    ["height"] = rect.height,
                    ["pivot"] = FerrofluidSnapshotData.ToToken(rectTransform.pivot),
                },
                ["skew"] = _skew,
                ["curvedCanvas"] = new JObject
                {
                    ["hasCurvedCanvas"] = _hasCurvedCanvas,
                    ["radius"] = _curvedUiRadius,
                    ["localToCurvedCanvas"] = FerrofluidSnapshotMatrix4x4.From(_localToCurvedCanvasMatrix).ToToken(),
                    ["curvedCanvasToLocal"] = FerrofluidSnapshotMatrix4x4.From(_curvedCanvasToLocalMatrix).ToToken(),
                    ["viewerLocalZSign"] = _viewerLocalZSign,
                },
                ["torusGeometry"] = new JObject
                {
                    ["center"] = FerrofluidSnapshotData.ToToken(rect.center),
                    ["halfExtents"] = FerrofluidSnapshotData.ToToken(geometry.HalfExtents),
                    ["cornerRadius"] = geometry.CornerRadius,
                    ["depthRadius"] = rect.width / 6f,
                },
                ["transform"] = new JObject
                {
                    ["pose"] = FerrofluidSnapshotPose.From(transform.position, transform.rotation).ToObject(),
                    ["localScale"] = FerrofluidSnapshotData.ToToken(transform.localScale),
                    ["localToWorld"] = FerrofluidSnapshotMatrix4x4.From(transform.localToWorldMatrix).ToToken(),
                    ["worldToLocal"] = FerrofluidSnapshotMatrix4x4.From(transform.worldToLocalMatrix).ToToken(),
                },
                ["magnet"] = new JObject
                {
                    ["pointerTarget"] = FerrofluidSnapshotData.ToToken(_pointerTarget),
                    ["pointerSmoothed"] = FerrofluidSnapshotData.ToToken(_pointerSmoothed),
                    ["magneticFieldPosition"] = FerrofluidSnapshotData.ToToken(_magneticFieldPosition),
                    ["magneticFieldTarget"] = FerrofluidSnapshotData.ToToken(_magneticFieldTarget),
                    ["magneticFieldStrength"] = _magneticFieldStrength,
                    ["magneticFieldTargetStrength"] = _magneticFieldTargetStrength,
                    ["hasPointer"] = _hasPointer,
                    ["openingPointerPosition"] = FerrofluidSnapshotData.ToToken(_openingPointerPosition),
                    ["openingPlaneNormal"] = FerrofluidSnapshotData.ToToken(_openingPlaneNormal),
                    ["openingPointerDirection"] = FerrofluidSnapshotData.ToToken(_openingPointerDirection),
                    ["openingWeight"] = _openingWeight,
                    ["handPosition"] = FerrofluidSnapshotData.ToToken(_handPosition),
                    ["handStrength"] = _handStrength,
                    ["hasHand"] = _hasHand,
                },
            };

            subject["gpuSimulation"] = _simulation.ExportSnapshot(
                torusChart,
                localPoint => TransformRenderedPoint(
                    localPoint,
                    rect,
                    rectTransform.pivot,
                    _skew,
                    _hasCurvedCanvas,
                    _localToCurvedCanvasMatrix,
                    _curvedCanvasToLocalMatrix,
                    _curvedUiRadius));

            var raycasts = new JObject();
            foreach ((string hand, bool left) in new[] { ("left", true), ("right", false) })
            {
                raycasts[hand] = _magnetController.TryGetPointerRay(left, out Vector3 origin, out Vector3 direction)
                    ? (JToken)MapHandRaySnapshot(rect, origin, direction)
                    : JValue.CreateNull();
            }

            subject["raycasts"] = raycasts;
            return subject;
        }

        // Map the hand ray against the same rendered magnet plane LateUpdate uses; the hit is a
        // projected magnet-plane hit, not the true deformed spike surface.
        private JObject MapHandRaySnapshot(Rect rect, Vector3 rayOrigin, Vector3 rayDirection)
        {
            var raycast = new JObject
            {
                ["kind"] = "projectedMagnetPlane",
                ["rayOrigin"] = FerrofluidSnapshotData.ToToken(rayOrigin),
                ["rayDirection"] = FerrofluidSnapshotData.ToToken(rayDirection),
            };
            float sourceWorldScale = (rect.width * transform.TransformVector(Vector3.right).magnitude) / 0.9f;
            float forwardDistance = EvaluateMagnetForwardWorldDistance(_configuration!.MagnetForwardDistance, sourceWorldScale);
            EvaluateRenderedMagnetPlane(
                rect,
                rectTransform.pivot,
                _skew,
                _hasCurvedCanvas,
                _localToCurvedCanvasMatrix,
                _curvedCanvasToLocalMatrix,
                _curvedUiRadius,
                transform.localToWorldMatrix,
                _viewerLocalZSign,
                forwardDistance,
                out Vector3 planePoint,
                out Vector3 planeNormal);
            raycast["planePoint"] = FerrofluidSnapshotData.ToToken(planePoint);
            raycast["planeNormal"] = FerrofluidSnapshotData.ToToken(planeNormal);
            float distance = FerrofluidMagnetController.EvaluateForwardPlaneIntersectionDistance(rayOrigin, rayDirection, planePoint, planeNormal);
            if (float.IsPositiveInfinity(distance))
            {
                raycast["hit"] = JValue.CreateNull();
                return raycast;
            }

            Vector3 intersection = rayOrigin + (rayDirection * distance);
            if (!TryMapMagnet(
                    intersection,
                    planeNormal,
                    out Vector2 pointer,
                    out float targetStrength,
                    out Vector3 magnetPosition,
                    out float openingWeight,
                    out Vector3 openingDirection))
            {
                raycast["hit"] = new JObject
                {
                    ["worldIntersection"] = FerrofluidSnapshotData.ToToken(intersection),
                    ["magnetMapping"] = JValue.CreateNull(),
                };
                return raycast;
            }

            raycast["hit"] = new JObject
            {
                ["worldIntersection"] = FerrofluidSnapshotData.ToToken(intersection),
                ["magnetMapping"] = new JObject
                {
                    ["chartPosition"] = FerrofluidSnapshotData.ToToken(pointer),
                    ["targetStrength"] = targetStrength,
                    ["magnetPosition"] = FerrofluidSnapshotData.ToToken(magnetPosition),
                    ["openingWeight"] = openingWeight,
                    ["openingDirection"] = FerrofluidSnapshotData.ToToken(openingDirection),
                },
            };
            return raycast;
        }
    }
}
#endif
