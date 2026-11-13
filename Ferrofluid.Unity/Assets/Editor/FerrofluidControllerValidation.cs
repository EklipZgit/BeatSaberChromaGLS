using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidControllerValidation
    {
        private const float FixedDelta = 0.02f;
        private static readonly List<string> Failures = new();

        private sealed class Context
        {
            internal Type Grabbable = null!;
            internal MethodInfo Initialize = null!;
            internal MethodInfo Acquire = null!;
            internal MethodInfo Release = null!;
            internal MethodInfo FixedUpdate = null!;
            internal MethodInfo SampleGrabTarget = null!;
            internal MethodInfo SetControllerInput = null!;
            internal MethodInfo StartSpinTest = null!;
            internal MethodInfo OnDisable = null!;
            internal PropertyInfo IsSpinTesting = null!;
            internal FieldInfo FreeCenterOfMass = null!;
            internal object Enclosure = null!;
            internal PhysicsMaterial Material = null!;
            internal object Ownership = null!;
            internal GameObject Floor = null!;
            internal List<Object> Resources = new();
        }

        public static void Run()
        {
            Failures.Clear();
            SimulationMode previousMode = Physics.simulationMode;
            float previousDelta = Time.fixedDeltaTime;
            Physics.simulationMode = SimulationMode.Script;
            Time.fixedDeltaTime = FixedDelta;
            Context context = new();
            try
            {
                string repository = Directory.GetParent(Application.dataPath).Parent.FullName;
                Assembly plugin = Assembly.LoadFrom(Path.Combine(repository, "ChromaGLS/bin/Release-1.44.1/net48/ChromaGLS.dll"));
                context.Grabbable = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true);
                context.Initialize = context.Grabbable.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic);
                context.Acquire = context.Grabbable.GetMethod("Acquire", BindingFlags.Instance | BindingFlags.NonPublic);
                context.Release = context.Grabbable.GetMethod("Release", BindingFlags.Instance | BindingFlags.NonPublic);
                context.FixedUpdate = context.Grabbable.GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                context.SampleGrabTarget = context.Grabbable.GetMethod("SampleGrabTarget", BindingFlags.Instance | BindingFlags.NonPublic);
                context.SetControllerInput = context.Grabbable.GetMethod("SetControllerInput", BindingFlags.Instance | BindingFlags.NonPublic);
                context.StartSpinTest = context.Grabbable.GetMethod("StartSpinTest", BindingFlags.Instance | BindingFlags.NonPublic);
                context.OnDisable = context.Grabbable.GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic);
                context.IsSpinTesting = context.Grabbable.GetProperty("IsSpinTesting", BindingFlags.Instance | BindingFlags.NonPublic);
                context.FreeCenterOfMass = context.Grabbable.GetField("_freeCenterOfMass", BindingFlags.Instance | BindingFlags.NonPublic);
                if (context.SetControllerInput == null)
                    throw new Exception("FerrofluidGrabbable.SetControllerInput is missing; controller force/spin is not implemented.");

                Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true);
                context.Enclosure = enclosureType
                    .GetConstructor(
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                        null,
                        new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                        null)!
                    .Invoke(new object[] { Vector3.zero, 3f, new Vector3(0f, 0f, -2f), Vector3.forward, 0f });
                context.Material = new PhysicsMaterial("controller validation")
                {
                    dynamicFriction = 0.4f,
                    staticFriction = 0.4f,
                    bounciness = 0.3f,
                    bounceCombine = PhysicsMaterialCombine.Multiply,
                    frictionCombine = PhysicsMaterialCombine.Average,
                };
                context.Resources.Add(context.Material);
                Type ownershipType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabOwnership", true);
                context.Ownership = Activator.CreateInstance(ownershipType, nonPublic: true);
                context.Floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                context.Floor.transform.position = Vector3.zero;
                context.Floor.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
                context.Floor.GetComponent<Collider>().material = context.Material;
                context.Resources.Add(context.Floor);

                foreach (bool sphere in new[] { false, true })
                {
                    string label = sphere ? "sphere" : "cube";
                    RunCase($"{label} held spin grows under torque", () => CaseHeldSpinGrows(context, sphere));
                    RunCase($"{label} opposite stick brakes existing spin", () => CaseOppositeBrakes(context, sphere));
                    RunCase($"{label} pivot inertia slows pitch spin", () => CaseHeldLeverInhibits(context, sphere));
                    RunCase($"{label} held tangential force orbits the pivot", () => CaseHeldForceOrbits(context, sphere));
                    RunCase($"{label} hover force accelerates in pointer plane", () => CaseHoverForce(context, sphere, 1f));
                    RunCase($"{label} hover force scale independent", () => CaseHoverForce(context, sphere, 0.5f));
                    RunCase($"{label} neutral keeps existing damping", () => CaseNeutralRetains(context, sphere));
                    RunCase($"{label} release clears stored input", () => CaseReleaseClears(context, sphere));
                    RunCase($"{label} disable clears stored input", () => CaseDisableClearsInput(context, sphere));
                    RunCase($"{label} spin test ignores manual input", () => CaseSpinTestIsolation(context, sphere));
                }

                RunCase("unrelated body receives no input", () => CaseUnrelatedBody(context));

                if (Failures.Count > 0)
                    throw new Exception($"{Failures.Count} controller cases failed: {string.Join(" | ", Failures)}");

                Debug.Log("[FerrofluidControllerValidation] PASS: held spin torque, orbit force, hover force, and input clears verified.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
            finally
            {
                foreach (Object resource in context.Resources)
                {
                    if (resource != null)
                        Object.DestroyImmediate(resource);
                }

                Physics.simulationMode = previousMode;
                Time.fixedDeltaTime = previousDelta;
            }
        }

        private static void CaseHeldSpinGrows(Context context, bool sphere)
        {
            Component body = CreateBody(context, new Vector3(0f, 2.5f, -0.5f), sphere, 0.55f);
            GameObject attach = AcquireTop(context, body);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            SetInput(context, body, Vector2.zero, new Vector2(1f, 0f), Quaternion.identity);

            Step(context, body, 15);
            float first = rb.angularVelocity.y;
            Step(context, body, 20);
            float second = rb.angularVelocity.y;
            Step(context, body, 20);
            float third = rb.angularVelocity.y;
            AssertTrue(first < -0.1f, $"held spin must accelerate about -up, got {first:F3} rad/s");
            AssertTrue(
                second < first && third < second,
                $"held spin must keep growing under torque ({first:F3} -> {second:F3} -> {third:F3}), not hold a constant speed");

            ReleaseAndCleanup(context, body, attach);
        }

        private static void CaseOppositeBrakes(Context context, bool sphere)
        {
            Component body = CreateBody(context, new Vector3(0f, 2.5f, -0.5f), sphere, 0.55f);
            GameObject attach = AcquireTop(context, body);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            SetInput(context, body, Vector2.zero, new Vector2(1f, 0f), Quaternion.identity);
            Step(context, body, 40);
            float established = rb.angularVelocity.y;
            AssertTrue(established < -0.5f, $"spin-up precondition needs |omega|>0.5, got {established:F3}");

            SetInput(context, body, Vector2.zero, new Vector2(-1f, 0f), Quaternion.identity);
            Step(context, body, 30);
            AssertTrue(
                rb.angularVelocity.y > established + 0.5f,
                $"opposite stick must brake the spin (before={established:F3} after={rb.angularVelocity.y:F3})");

            ReleaseAndCleanup(context, body, attach);
        }

        private static void CaseHeldLeverInhibits(Context context, bool sphere)
        {
            Component held = CreateBody(context, new Vector3(-0.5f, 2.5f, -0.5f), sphere, 0.55f);
            GameObject attach = AcquireTop(context, held);
            Component free = CreateBody(context, new Vector3(0.5f, 2.5f, -0.5f), sphere, 0.55f);
            SetInput(context, held, Vector2.zero, new Vector2(0f, 1f), Quaternion.identity);
            SetInput(context, free, Vector2.zero, new Vector2(0f, 1f), Quaternion.identity);
            StepPair(context, held, free, 5);
            float heldRate = held.GetComponent<Rigidbody>().angularVelocity.x;
            float freeRate = free.GetComponent<Rigidbody>().angularVelocity.x;
            AssertTrue(heldRate > 0.1f, $"the pitch impulse must still turn the held body, got {heldRate:F3} rad/s");
            AssertTrue(freeRate > 1f, $"free body must spin up under the torque, got {freeRate:F3} rad/s");
            AssertTrue(
                heldRate < freeRate * 0.6f,
                $"the offset pivot's larger inertia tensor must slow the same torque (held={heldRate:F3} free={freeRate:F3})");

            ReleaseAndCleanup(context, held, attach);
            free.gameObject.SetActive(false);
        }

        private static void CaseHeldForceOrbits(Context context, bool sphere)
        {
            Component body = CreateBody(context, new Vector3(0f, 2.5f, -0.5f), sphere, 0.55f);
            GameObject attach = AcquireTop(context, body);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            Vector3 pivot = attach.transform.position;
            SetInput(context, body, new Vector2(0f, 1f), Vector2.zero, Quaternion.identity);
            Step(context, body, 30);

            AssertTrue(
                rb.angularVelocity.x > 0.3f,
                $"stick up with COM below the pivot must torque the body about +X, got omega={rb.angularVelocity}");
            Vector3 freeCenter = (Vector3)context.FreeCenterOfMass.GetValue(body);
            Vector3 physicalCenter = rb.position + (rb.rotation * freeCenter);
            AssertTrue(
                physicalCenter.z < pivot.z - 0.005f,
                $"the orbit force must move the mass center toward the player (centerZ={physicalCenter.z:F4} pivotZ={pivot.z:F4})");
            AssertTrue(
                Vector3.Distance(rb.position + (rb.rotation * new Vector3(0f, 0.275f, 0f)), pivot) < 0.05f,
                "the tangential force must orbit around the pivot, not translate the whole body off the hand");

            ReleaseAndCleanup(context, body, attach);
        }

        private static void CaseHoverForce(Context context, bool sphere, float scale)
        {
            Component body = CreateBody(context, new Vector3(0f, 5f, -0.5f), sphere, scale);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            SetInput(context, body, new Vector2(0f, 1f), Vector2.zero, Quaternion.Euler(90f, 0f, 0f));
            Step(context, body, 20);

            float expected = 12f * 20f * FixedDelta;
            AssertTrue(
                rb.linearVelocity.z > expected * 0.8f && rb.linearVelocity.z < expected * 1.2f,
                $"hover stick-up on a down pointer must accelerate ~12 m/s^2 forward (scale={scale}, vz={rb.linearVelocity.z:F3} of {expected:F3})");
            AssertTrue(Mathf.Abs(rb.linearVelocity.x) < expected * 0.1f, $"hover force must stay inside the pointer plane (vx={rb.linearVelocity.x:F3})");
            AssertTrue(rb.linearVelocity.y < -1f, $"gravity must still pull the free body down (vy={rb.linearVelocity.y:F3})");

            body.gameObject.SetActive(false);
        }

        private static void CaseNeutralRetains(Context context, bool sphere)
        {
            Component held = CreateBody(context, new Vector3(-0.6f, 2.5f, -0.5f), sphere, 0.55f);
            GameObject attach = AcquireTop(context, held);
            Rigidbody rb = held.GetComponent<Rigidbody>();
            SetInput(context, held, Vector2.zero, new Vector2(1f, 0f), Quaternion.identity);
            Step(context, held, 40);

            Component coasting = CreateBody(context, new Vector3(0.6f, 2.5f, -0.5f), sphere, 0.55f);
            GameObject coastingAttach = AcquireTop(context, coasting);
            Rigidbody coastingRb = coasting.GetComponent<Rigidbody>();
            coastingRb.angularVelocity = rb.angularVelocity;

            SetInput(context, held, Vector2.zero, Vector2.zero, Quaternion.identity);
            StepPair(context, held, coasting, 20);

            AssertTrue(
                rb.angularVelocity.magnitude >= coastingRb.angularVelocity.magnitude * 0.9f,
                $"neutral must not brake the spin beyond the held baseline (neutral={rb.angularVelocity.magnitude:F3} coasting={coastingRb.angularVelocity.magnitude:F3})");

            ReleaseAndCleanup(context, held, attach);
            ReleaseAndCleanup(context, coasting, coastingAttach);

            Component free = CreateBody(context, new Vector3(0f, 8f, -0.5f), sphere, 0.55f);
            Rigidbody freeRb = free.GetComponent<Rigidbody>();
            SetInput(context, free, Vector2.zero, new Vector2(1f, 0f), Quaternion.identity);
            Step(context, free, 25);
            float freeEstablished = freeRb.angularVelocity.magnitude;
            SetInput(context, free, Vector2.zero, Vector2.zero, Quaternion.identity);
            Step(context, free, 30);
            AssertTrue(
                freeRb.angularVelocity.magnitude >= freeEstablished * 0.95f,
                $"a free body on neutral must retain its spin except baseline damping (before={freeEstablished:F3} after={freeRb.angularVelocity.magnitude:F3})");

            free.gameObject.SetActive(false);
        }

        private static void CaseReleaseClears(Context context, bool sphere)
        {
            Component body = CreateBody(context, new Vector3(0f, 6f, -0.5f), sphere, 0.55f);
            GameObject attach = AcquireTop(context, body);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            SetInput(context, body, Vector2.zero, new Vector2(1f, 0f), Quaternion.identity);
            Step(context, body, 30);
            float established = rb.angularVelocity.magnitude;

            context.Release.Invoke(body, null);
            float released = rb.angularVelocity.magnitude;
            Step(context, body, 30);
            AssertTrue(
                Mathf.Abs(rb.angularVelocity.magnitude - released) < released * 0.3f + 0.05f,
                $"a released body must coast on its physical spin, not keep accelerating (released={released:F3} later={rb.angularVelocity.magnitude:F3}, established={established:F3})");

            body.gameObject.SetActive(false);
            context.Resources.Add(attach);
        }

        private static void CaseDisableClearsInput(Context context, bool sphere)
        {
            Component body = CreateBody(context, new Vector3(0f, 5f, -0.5f), sphere, 0.55f);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            SetInput(context, body, new Vector2(0f, 1f), new Vector2(1f, 0f), Quaternion.Euler(90f, 0f, 0f));
            Step(context, body, 10);
            AssertTrue(rb.linearVelocity.z > 1f, $"hover force must accelerate the body before disable (vz={rb.linearVelocity.z:F3})");
            float preDisableVelocity = rb.linearVelocity.z;
            float preDisableOmega = rb.angularVelocity.magnitude;
            AssertTrue(preDisableOmega > 0.5f, $"spin must build before disable (omega={preDisableOmega:F3})");

            context.OnDisable.Invoke(body, null);
            Step(context, body, 15);

            AssertTrue(
                Mathf.Abs(rb.linearVelocity.z - preDisableVelocity) < 0.5f,
                $"disable must drop the stored force sample (before={preDisableVelocity:F3} after={rb.linearVelocity.z:F3})");
            AssertTrue(
                Mathf.Abs(rb.angularVelocity.magnitude - preDisableOmega) < (preDisableOmega * 0.3f) + 0.05f,
                $"disable must drop the stored spin sample (before={preDisableOmega:F3} after={rb.angularVelocity.magnitude:F3})");

            body.gameObject.SetActive(false);
        }

        private static void CaseSpinTestIsolation(Context context, bool sphere)
        {
            Component body = CreateBody(context, new Vector3(0f, 0.55f, -0.5f), sphere, 0.55f);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            context.StartSpinTest.Invoke(body, new object[] { 2f });
            AssertTrue((bool)context.IsSpinTesting.GetValue(body), "the spin test must be active for this case");
            SetInput(context, body, Vector2.zero, new Vector2(1f, 0f), Quaternion.identity);
            Step(context, body, 160);
            AssertTrue(
                rb.angularVelocity.magnitude < 1f,
                $"manual input must not stack onto the spin test (omega={rb.angularVelocity})");

            body.gameObject.SetActive(false);
        }

        private static void CaseUnrelatedBody(Context context)
        {
            Component body = CreateBody(context, new Vector3(0f, 6f, -0.5f), true, 0.55f);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            Step(context, body, 20);
            AssertTrue(Mathf.Abs(rb.linearVelocity.x) < 0.001f && Mathf.Abs(rb.linearVelocity.z) < 0.001f, $"no-input body must not drift sideways ({rb.linearVelocity})");
            AssertTrue(rb.angularVelocity.magnitude < 0.001f, $"no-input body must not spin ({rb.angularVelocity})");
            AssertTrue(rb.linearVelocity.y < -1f, $"no-input body must still fall ({rb.linearVelocity.y:F3})");

            body.gameObject.SetActive(false);
        }

        private static Component CreateBody(Context context, Vector3 position, bool sphere, float scale)
        {
            GameObject body = new($"controller validation {(sphere ? "sphere" : "cube")} {scale}");
            context.Resources.Add(body);
            body.transform.localScale = Vector3.one * scale;
            body.transform.position = position;
            Component component = body.AddComponent(context.Grabbable);
            context.Initialize.Invoke(component, new object[] { context.Enclosure, sphere, context.Material, context.Ownership });
            Rigidbody rb = body.GetComponent<Rigidbody>();
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            return component;
        }

        private static GameObject AcquireTop(Context context, Component body)
        {
            GameObject attach = new("controller top attach");
            attach.transform.position = body.GetComponent<Rigidbody>().position + new Vector3(0f, 0.275f, 0f);
            context.Acquire.Invoke(body, new object[] { attach.transform, attach.transform.position });
            return attach;
        }

        private static void SetInput(Context context, Component body, Vector2 force, Vector2 spin, Quaternion pointerRotation)
        {
            context.SetControllerInput.Invoke(body, new object[] { force, spin, pointerRotation, Vector3.right });
        }

        private static void Step(Context context, Component body, int ticks)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                context.SampleGrabTarget.Invoke(body, new object[] { FixedDelta, Time.fixedUnscaledTime });
                context.FixedUpdate.Invoke(body, null);
                Physics.Simulate(FixedDelta);
            }
        }

        private static void StepPair(Context context, Component first, Component second, int ticks)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                context.SampleGrabTarget.Invoke(first, new object[] { FixedDelta, Time.fixedUnscaledTime });
                context.SampleGrabTarget.Invoke(second, new object[] { FixedDelta, Time.fixedUnscaledTime });
                context.FixedUpdate.Invoke(first, null);
                context.FixedUpdate.Invoke(second, null);
                Physics.Simulate(FixedDelta);
            }
        }

        private static void ReleaseAndCleanup(Context context, Component body, GameObject attach)
        {
            context.Release.Invoke(body, null);
            body.gameObject.SetActive(false);
            context.Resources.Add(attach);
        }

        private static void RunCase(string name, Action test)
        {
            try
            {
                test();
            }
            catch (Exception exception)
            {
                Failures.Add($"{name}: {exception.GetType().Name} {exception.Message.Split('\n')[0]}");
            }
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }
    }
}
