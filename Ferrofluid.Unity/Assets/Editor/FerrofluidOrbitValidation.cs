using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // Distinguish an actual joint-anchor orbit from the COM swinging around a stable grabbed point.
    internal static class FerrofluidOrbitValidation
    {
        public static void Run()
        {
            string repository = Directory.GetParent(Application.dataPath)!.Parent!.FullName;
            Assembly plugin = Assembly.LoadFrom(Path.Combine(repository, "ChromaGLS/bin/Release-1.44.1/net48/ChromaGLS.dll"));
            Type grabbable = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true)!;
            Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true)!;
            BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            object enclosure = enclosureType.GetConstructor(
                    instance,
                    null,
                    new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                    null)!
                .Invoke(new object[] { Vector3.zero, 100f, new Vector3(0f, 0f, -100f), Vector3.forward, -100f });
            object ownership = Activator.CreateInstance(plugin.GetType("ChromaGLS.Settings.FerrofluidGrabOwnership", true)!, nonPublic: true)!;
            MethodInfo initialize = grabbable.GetMethod("Initialize", instance)!;
            MethodInfo acquire = grabbable.GetMethod("Acquire", instance)!;
            MethodInfo sample = grabbable.GetMethod("SampleGrabTarget", instance)!;
            MethodInfo advance = grabbable.GetMethod("AdvancePhysics", instance)!;
            MethodInfo release = grabbable.GetMethod("Release", instance)!;
            PropertyInfo target = grabbable.GetProperty("GrabTarget", instance)!;
            PhysicsMaterial material = new() { dynamicFriction = 0.4f, staticFriction = 0.4f, bounciness = 0.3f };
            SimulationMode previousMode = Physics.simulationMode;
            float previousDelta = Time.fixedDeltaTime;
            List<Object> resources = new() { material };
            List<string> failures = new();
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                foreach (bool sphere in new[] { false, true })
                {
                    Vector3[] anchors = { Vector3.up * 0.5f, Vector3.right * 0.5f, sphere ? Vector3.one.normalized * 0.5f : Vector3.one * 0.4f };
                    foreach (Vector3 localAnchor in anchors)
                    {
                        foreach (float radius in new[] { 0.275f, 0.55f, 0.825f })
                        {
                            foreach (float period in new[] { 0.35f, 0.6f })
                            {
                                foreach (float physicsHz in new[] { 50f, 100f })
                                {
                                    float renderHz = physicsHz == 50f ? 90f : 120f;
                                    float renderDelta = 1f / renderHz;
                                    float physicsDelta = 1f / physicsHz;
                                    Time.fixedDeltaTime = physicsDelta;
                                    string label = $"{(sphere ? "sphere" : "cube")} anchor{localAnchor} r{radius:F3} period{period:F2} render{renderHz}/physics{physicsHz}";
                                    GameObject bodyObject = new(label);
                                    GameObject controller = new("orbit controller");
                                    resources.Add(bodyObject);
                                    resources.Add(controller);
                                    Component component = null!;
                                    try
                                    {
                                        Vector3 pivot = new(0f, 3f, 0f);
                                        Vector3 scaledAnchor = localAnchor * 0.55f;
                                        bodyObject.transform.localScale = Vector3.one * 0.55f;
                                        bodyObject.transform.position = pivot - scaledAnchor;
                                        component = bodyObject.AddComponent(grabbable);
                                        initialize.Invoke(component, new object[] { enclosure, sphere, material, ownership });
                                        Rigidbody body = bodyObject.GetComponent<Rigidbody>();
                                        body.PublishTransform();
                                        Physics.SyncTransforms();
                                        ConfigurableJoint joint = bodyObject.GetComponent<ConfigurableJoint>();
                                        controller.transform.position = new Vector3(0f, 1f, -2f);
                                        Quaternion initialDirection = Quaternion.LookRotation(pivot - controller.transform.position);
                                        controller.transform.rotation = initialDirection;
                                        acquire.Invoke(component, new object[] { controller.transform, pivot });
                                        sample.Invoke(component, new object[] { renderDelta, 0f });

                                        const float Warmup = 2f;
                                        float stopTime = Warmup + (2f * period);
                                        double lastPhysics = 0;
                                        float maxLateError = 0f;
                                        float maxLateTangentSpeed = 0f;
                                        float maxModeledRenderError = 0f;
                                        float peakForce = 0f;
                                        float winding = 0f;
                                        Vector3 previousError = Vector3.zero;
                                        Vector3 previousTarget = pivot;
                                        int frames = Mathf.CeilToInt((stopTime + 1.2f) * renderHz);
                                        for (int frame = 1; frame <= frames; frame++)
                                        {
                                            double time = frame / (double)renderHz;
                                            while (lastPhysics + physicsDelta <= time + 0.0000001)
                                            {
                                                lastPhysics += physicsDelta;
                                                body.PublishTransform();
                                                advance.Invoke(component, new object[] { physicsDelta, (float)lastPhysics });
                                                Physics.Simulate(physicsDelta);
                                                peakForce = Mathf.Max(peakForce, joint.currentForce.magnitude);
                                            }

                                            Vector3 circleTarget = pivot;
                                            if (time >= Warmup && time < stopTime)
                                            {
                                                float angle = (float)((time - Warmup) * 2.0 * Math.PI / period);
                                                circleTarget += new Vector3(radius * (Mathf.Cos(angle) - 1f), radius * Mathf.Sin(angle), 0f);
                                            }

                                            controller.transform.rotation = Quaternion.LookRotation(circleTarget - controller.transform.position);
                                            sample.Invoke(component, new object[] { renderDelta, (float)time });
                                            Vector3 desired = (Vector3)target.GetValue(component)!;
                                            Vector3 clicked = body.position + (body.rotation * scaledAnchor);
                                            Vector3 error = clicked - desired;
                                            Vector3 pointVelocity = body.GetPointVelocity(clicked);
                                            Vector3 desiredVelocity = (desired - previousTarget) / renderDelta;
                                            previousTarget = desired;
                                            if (time > stopTime + 0.25f)
                                            {
                                                maxLateError = Mathf.Max(maxLateError, error.magnitude);
                                                Vector3 relativeVelocity = pointVelocity - desiredVelocity;
                                                Vector3 tangent = relativeVelocity - (error.normalized * Vector3.Dot(relativeVelocity, error.normalized));
                                                maxLateTangentSpeed = Mathf.Max(maxLateTangentSpeed, tangent.magnitude);
                                                // This is a mathematical extrapolation diagnostic, not an engine-rendered pose.
                                                float remainder = (float)(time - lastPhysics);
                                                Vector3 omega = body.angularVelocity;
                                                Quaternion predictedRotation = omega.sqrMagnitude > 0.000000001f
                                                    ? Quaternion.AngleAxis(omega.magnitude * Mathf.Rad2Deg * remainder, omega.normalized) * body.rotation
                                                    : body.rotation;
                                                Vector3 predictedPoint = body.worldCenterOfMass + (body.linearVelocity * remainder) + (predictedRotation * (scaledAnchor - body.centerOfMass));
                                                maxModeledRenderError = Mathf.Max(maxModeledRenderError, Vector3.Distance(predictedPoint, desired));
                                            }

                                            if (time > stopTime && error.magnitude > 0.01f && previousError.magnitude > 0.01f)
                                                winding += Vector3.Angle(previousError, error);

                                            previousError = error;
                                        }

                                        Debug.Log($"[FerrofluidOrbitValidation] {label}: lateGap={maxLateError:F5} lateTangent={maxLateTangentSpeed:F4} modeledRenderGap={maxModeledRenderError:F5} winding={winding:F1} force={peakForce:F1}");
                                        if (maxLateError > 0.005f || maxLateTangentSpeed > 0.05f)
                                            failures.Add($"{label}: grabbed point continues orbiting/lagging after stop (gap={maxLateError:F5}, tangent={maxLateTangentSpeed:F4})");
                                    }
                                    finally
                                    {
                                        if (component != null)
                                            release.Invoke(component, null);

                                        bodyObject.SetActive(false);
                                        controller.SetActive(false);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                Physics.simulationMode = previousMode;
                Time.fixedDeltaTime = previousDelta;
                foreach (Object resource in resources)
                {
                    if (resource != null)
                        Object.DestroyImmediate(resource);
                }
            }

            if (failures.Count > 0)
                throw new Exception($"{failures.Count} orbit cases failed: {string.Join(" | ", failures)}");

            Debug.Log("[FerrofluidOrbitValidation] PASS: the grabbed surface point settles directly after circular input.");
        }
    }
}
