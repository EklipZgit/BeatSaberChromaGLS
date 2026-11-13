using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ChromaGLS.FerrofluidBundle
{
    // Regression for the pickup hover: the UI canvas physics raycaster (sorting order 25) lands on
    // the inner body collider and outranks the analytic outer-hull hit (sorting order 0), so grab
    // arbitration used to anchor inside the shape. ResolveSurfaceHit must promote the outer hit.
    internal static class FerrofluidPickupSurfaceValidation
    {
        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidPickupSurfaceValidation));

        private static void RunRuntime()
        {
            string repository = Directory.GetParent(Application.dataPath)!.Parent!.FullName;
            Assembly plugin = Assembly.LoadFrom(Path.Combine(repository, "ChromaGLS/bin/Release-1.44.1/net48/ChromaGLS.dll"));
            Assembly vrui = Assembly.LoadFrom(Path.Combine(repository, "ChromaGLS.Tests/bin/Release-1.44.1/net48/VRUI.dll"));
            BindingFlags inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            List<string> failures = new();
            List<GameObject> owned = new();
            PhysicsMaterial physicsMaterial = null!;
            try
            {
                Type shapeType = plugin.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", true)!;
                Type effectType = plugin.GetType("ChromaGLS.Settings.FerrofluidPrimitiveEffect", true)!;
                Type raycasterType = plugin.GetType("ChromaGLS.Settings.FerrofluidPrimitiveRaycaster", true)!;
                Type grabbableType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true)!;
                Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true)!;
                object enclosure = enclosureType.GetConstructor(
                        inst, null, new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) }, null)!
                    .Invoke(new object[] { Vector3.zero, 3f, new Vector3(0f, 0f, -2f), Vector3.forward, 0f });
                object ownership = Activator.CreateInstance(plugin.GetType("ChromaGLS.Settings.FerrofluidGrabOwnership", true)!, nonPublic: true)!;
                physicsMaterial = new() { dynamicFriction = 0.4f, staticFriction = 0.4f, bounciness = 0.3f };
                MethodInfo resolve = raycasterType.GetMethod("ResolveSurfaceHit", BindingFlags.Static | BindingFlags.NonPublic)
                    ?? throw new Exception("ResolveSurfaceHit is missing; the production resolver seam is absent.");
                MethodInfo acquire = grabbableType.GetMethod("Acquire", inst)!;
                FieldInfo bodyAnchor = grabbableType.GetField("_bodyAnchor", inst)!;

                Type graphicRaycasterType = vrui.GetType("VRUIControls.VRGraphicRaycaster", true)!;
                Type physicsRaycasterType = vrui.GetType("VRUIControls.PhysicsRaycasterWithCache", true)!;
                FieldInfo physicsField = graphicRaycasterType.GetField("_physicsRaycaster", BindingFlags.Instance | BindingFlags.NonPublic)!;
                FieldInfo frameField = physicsRaycasterType.GetField("_lastFrameCount", BindingFlags.Instance | BindingFlags.NonPublic)!;

                GameObject eventSystemGo = new("event system");
                owned.Add(eventSystemGo);
                EventSystem eventSystem = eventSystemGo.AddComponent<EventSystem>();
                GameObject canvasGo = new("ui canvas");
                owned.Add(canvasGo);
                canvasGo.transform.position = new Vector3(0f, 10f, 0f);
                Canvas canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.sortingOrder = 25;
                canvasGo.AddComponent<RectTransform>();
                Component graphicRaycaster = canvasGo.AddComponent(graphicRaycasterType);
                object physicsRaycaster = Activator.CreateInstance(physicsRaycasterType);
                physicsField.SetValue(graphicRaycaster, physicsRaycaster);

                GameObject blocker = new("foreign blocker");
                owned.Add(blocker);

                foreach ((Quaternion rotation, string label) in new[] { (Quaternion.identity, "upright"), (Quaternion.Euler(30f, 45f, 20f), "rotated") })
                {
                    // The real grabbable supplies the production body collider; the effect and the
                    // custom raycaster sit on the same root just like the menu does.
                    GameObject primitive = new($"pickup cube {label}");
                    owned.Add(primitive);
                    primitive.transform.position = new Vector3(0f, 1f, 0f);
                    primitive.transform.localScale = Vector3.one * 0.55f;
                    primitive.transform.rotation = rotation;
                    Component grabbable = primitive.AddComponent(grabbableType);
                    grabbableType.GetMethod("Initialize", inst)!.Invoke(grabbable, new object[] { enclosure, false, physicsMaterial, ownership });
                    grabbableType.GetMethod("ConfigureCubeRoundness", inst)!.Invoke(grabbable, new object[] { 0.48f });
                    Component effect = primitive.AddComponent(effectType);
                    ((Behaviour)effect).enabled = false;
                    effectType.GetField("_shape", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(effect, Enum.Parse(shapeType, "RoundedCube"));
                    effectType.GetField("_roundness", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(effect, 0.48f);
                    BaseRaycaster raycaster = (BaseRaycaster)primitive.AddComponent(raycasterType);
                    raycasterType.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(raycaster, new object[] { effect });
                    GameObject attach = new($"attach {label}");
                    owned.Add(attach);
                    Collider bodyCollider = null!;
                    foreach (Collider candidate in primitive.GetComponentsInChildren<Collider>())
                        if (candidate.enabled && candidate.gameObject != primitive)
                            bodyCollider = candidate;
                    Physics.SyncTransforms();
                    if (bodyCollider == null)
                    {
                        failures.Add($"{label}: the production grabbable created no child collider.");
                        continue;
                    }

                    foreach ((Vector3 origin, string ray) in new[] { (new Vector3(0f, 1f, 2f), "centre"), (primitive.transform.TransformPoint(new Vector3(0.15f, 0.1f, 4f)), "off-axis") })
                    {
                        Vector3 direction = (primitive.transform.TransformPoint(Vector3.zero) - origin).normalized;
                        PointerEventData data = new(eventSystem)
                        {
                            pointerCurrentRaycast = new RaycastResult { worldPosition = origin, worldNormal = direction },
                        };
                        List<RaycastResult> hits = new();
                        eventSystem.RaycastAll(data, hits);
                        RaycastResult winner = default;
                        RaycastResult outer = default;
                        bool found = false;
                        foreach (RaycastResult hit in hits)
                        {
                            if (hit.gameObject == null)
                                continue;
                            if (!found)
                            {
                                winner = hit;
                                found = true;
                            }
                            if (ReferenceEquals(hit.module, raycaster))
                                outer = hit;
                        }

                        if (!found)
                        {
                            failures.Add($"{label}/{ray}: no raycast hit at all.");
                            continue;
                        }
                        if (outer.gameObject != primitive)
                        {
                            failures.Add($"{label}/{ray}: the primitive raycaster produced no outer candidate.");
                            continue;
                        }

                        // Without promotion a child-collider hit wins on canvas sorting order.
                        bool innerWins = winner.gameObject.transform.IsChildOf(primitive.transform) && winner.gameObject != primitive;
                        if (!innerWins && winner.gameObject != primitive)
                            failures.Add($"{label}/{ray}: raw winner {winner.gameObject?.name} is outside the primitive hierarchy.");
                        if (innerWins && outer.distance >= winner.distance)
                            failures.Add($"{label}/{ray}: the outer hull hit is not closer than the inner collider hit.");

                        RaycastResult resolved = (RaycastResult)resolve.Invoke(null, new object[] { winner, hits })!;
                        if (resolved.gameObject != primitive || resolved.worldPosition != outer.worldPosition)
                        {
                            failures.Add($"{label}/{ray}: resolved hit {resolved.gameObject?.name} at {resolved.worldPosition} is not the outer hull surface {outer.worldPosition}.");
                            continue;
                        }

                        attach.transform.position = resolved.worldPosition;
                        acquire.Invoke(grabbable, new object[] { attach.transform, resolved.worldPosition });
                        Vector3 anchor = (Vector3)bodyAnchor.GetValue(grabbable)!;
                        Vector3 expectedAnchor = primitive.transform.InverseTransformPoint(outer.worldPosition);
                        if (Vector3.Distance(anchor, expectedAnchor) > 0.00001f)
                            failures.Add($"{label}/{ray}: grab anchor {anchor} does not match the outer surface anchor {expectedAnchor}.");
                    }

                    // A foreign physical blocker in front of the body keeps its win untouched.
                    blocker.transform.position = primitive.transform.TransformPoint(new Vector3(0f, 0f, 0.9f));
                    if (blocker.GetComponent<BoxCollider>() == null)
                        blocker.AddComponent<BoxCollider>().size = Vector3.one * 0.5f;
                    Physics.SyncTransforms();
                    frameField.SetValue(physicsRaycaster, Time.frameCount - 1);
                    PointerEventData blocked = new(eventSystem)
                    {
                        pointerCurrentRaycast = new RaycastResult { worldPosition = primitive.transform.TransformPoint(new Vector3(0f, 0f, 4f)), worldNormal = -primitive.transform.forward },
                    };
                    List<RaycastResult> blockedHits = new();
                    eventSystem.RaycastAll(blocked, blockedHits);
                    RaycastResult foreign = default;
                    foreach (RaycastResult hit in blockedHits)
                    {
                        if (hit.gameObject != null)
                        {
                            foreign = hit;
                            break;
                        }
                    }

                    if (foreign.gameObject != blocker)
                        failures.Add($"{label}: foreign winner was {foreign.gameObject?.name}, expected the blocker.");
                    RaycastResult resolvedForeign = (RaycastResult)resolve.Invoke(null, new object[] { foreign, blockedHits })!;
                    if (resolvedForeign.gameObject != foreign.gameObject || resolvedForeign.worldPosition != foreign.worldPosition)
                        failures.Add($"{label}: ResolveSurfaceHit changed a foreign winner.");
                    blocker.transform.position = new Vector3(0f, -100f, 0f);
                    frameField.SetValue(physicsRaycaster, Time.frameCount - 1);
                    Physics.SyncTransforms();
                    UnityEngine.Object.DestroyImmediate(primitive);
                    UnityEngine.Object.DestroyImmediate(attach);
                }
            }
            finally
            {
                foreach (GameObject go in owned)
                    if (go != null)
                        UnityEngine.Object.DestroyImmediate(go);
                if (physicsMaterial != null)
                    UnityEngine.Object.DestroyImmediate(physicsMaterial);
            }

            if (failures.Count != 0)
                throw new Exception(string.Join(" | ", failures));
            Debug.Log("[FerrofluidPickupSurfaceValidation] PASS: the inner collider winner resolves to the outer hull, grab anchor matches, foreign hits untouched.");
        }
    }
}
