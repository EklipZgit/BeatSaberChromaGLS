using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidPivotDampingValidation
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
            internal MethodInfo SetGrabDepthInput = null!;
            internal MethodInfo StartSpinTest = null!;
            internal MethodInfo OnDisable = null!;
            internal PropertyInfo GrabTarget = null!;
            internal object Enclosure = null!;
            internal PhysicsMaterial Material = null!;
            internal object Ownership = null!;
            internal GameObject Floor = null!;
            internal Assembly Plugin = null!;
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
                context.Plugin = plugin;
                context.Grabbable = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true);
                context.Initialize = context.Grabbable.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic);
                context.Acquire = context.Grabbable.GetMethod("Acquire", BindingFlags.Instance | BindingFlags.NonPublic);
                context.Release = context.Grabbable.GetMethod("Release", BindingFlags.Instance | BindingFlags.NonPublic);
                context.FixedUpdate = context.Grabbable.GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                context.SampleGrabTarget = context.Grabbable.GetMethod("SampleGrabTarget", BindingFlags.Instance | BindingFlags.NonPublic);
                context.SetControllerInput = context.Grabbable.GetMethod("SetControllerInput", BindingFlags.Instance | BindingFlags.NonPublic);
                context.SetGrabDepthInput = context.Grabbable.GetMethod("SetGrabDepthInput", BindingFlags.Instance | BindingFlags.NonPublic);
                context.StartSpinTest = context.Grabbable.GetMethod("StartSpinTest", BindingFlags.Instance | BindingFlags.NonPublic);
                context.OnDisable = context.Grabbable.GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic);
                context.GrabTarget = context.Grabbable.GetProperty("GrabTarget", BindingFlags.Instance | BindingFlags.NonPublic);

                Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true);
                context.Enclosure = enclosureType
                    .GetConstructor(
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                        null,
                        new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                        null)!
                    .Invoke(new object[] { Vector3.zero, 3f, new Vector3(0f, 0f, -2f), Vector3.forward, 0f });
                context.Material = new PhysicsMaterial("pivot damping validation")
                {
                    dynamicFriction = 0.4f,
                    staticFriction = 0.4f,
                    bounciness = 0.3f,
                };
                context.Resources.Add(context.Material);
                context.Ownership = Activator.CreateInstance(plugin.GetType("ChromaGLS.Settings.FerrofluidGrabOwnership", true), nonPublic: true);
                context.Floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                context.Floor.transform.position = Vector3.zero;
                context.Floor.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
                context.Floor.GetComponent<Collider>().material = context.Material;
                context.Resources.Add(context.Floor);

                foreach (bool sphere in new[] { false, true })
                {
                    string label = sphere ? "sphere" : "cube";
                    RunCase($"{label} twist grows past old ceiling toward new cap", () => CaseTwistGrows(context, sphere));
                    RunCase($"{label} neutral twist coasts while held", () => CaseNeutralTwistCoasts(context, sphere));
                    RunCase($"{label} release keeps twist and motion", () => CaseReleaseKeepsTwist(context, sphere));
                    RunCase($"{label} pivot translation does not damp twist", () => CasePivotTranslation(context, sphere));
                    RunCase($"{label} center grab has no damping", () => CaseCenterGrab(context, sphere));
                    RunCase($"{label} side grab orbit force is delivered", () => CaseSideGrabOrbit(context, sphere));
                    RunCase($"{label} spin test unaffected", () => CaseSpinTest(context, sphere));
                }

                RunCase("existing pendulum settle and release", () => typeof(FerrofluidGrabValidation)
                    .GetMethod("ValidateHeldPivotFriction", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, new object[] { context.Plugin }));

                if (Failures.Count > 0)
                    throw new Exception($"{Failures.Count} pivot damping cases failed: {string.Join(" | ", Failures)}");

                Debug.Log("[FerrofluidPivotDampingValidation] PASS: twist uncapped growth, coast, release, translation immunity, center grab, orbit, spin test, and pendulum settle all verified.");
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

        private static void CaseTwistGrows(Context context, bool sphere)
        {
            Component body = CreateBody(context, new Vector3(0f, 3f, -0.5f), sphere);
            GameObject attach = AcquireTop(context, body);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            try
            {
                SetSpin(context, body, new Vector2(1f, 0f));
                var samples = new List<float>();
                for (int window = 0; window < 5; window++)
                {
                    Step(context, body, 25);
                    samples.Add(rb.angularVelocity.magnitude);
                }

                Debug.Log($"[FerrofluidPivotDampingValidation] {(sphere ? "sphere" : "cube")} twist samples={string.Join(",", samples.ConvertAll(v => v.ToString("F1")))}");
                float rise12 = samples[1] - samples[0];
                float rise34 = samples[3] - samples[2];
                AssertTrue(
                    rise34 > rise12 * 0.5f,
                    $"twist acceleration must stay near-constant below the cap (0.5-1s={rise12:F1} 1.5-2s={rise34:F1})");
                Step(context, body, 125);
                float terminal = rb.angularVelocity.magnitude;
                AssertTrue(
                    terminal > 100f && terminal <= 20f * Mathf.PI * 2f * 1.02f,
                    $"sustained twist must pass 100 rad/s yet hold at the 20 RPS ceiling plus torque tolerance ({terminal:F1})");
                AssertTrue(
                    Mathf.Abs(rb.maxAngularVelocity - (20f * Mathf.PI * 2f)) < 0.05f,
                    $"maxAngularVelocity must be 20 revolutions/s ({rb.maxAngularVelocity:F2})");
            }
            finally
            {
                ReleaseAndCleanup(context, body, attach);
            }
        }

        private static void CaseNeutralTwistCoasts(Context context, bool sphere)
        {
            Component body = CreateBody(context, new Vector3(0f, 3f, -0.5f), sphere);
            GameObject attach = AcquireTop(context, body);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            try
            {
                SetSpin(context, body, new Vector2(1f, 0f));
                Step(context, body, 40);
                float established = rb.angularVelocity.magnitude;
                AssertTrue(established > 0.5f, $"spin must build first ({established:F2})");
                SetSpin(context, body, Vector2.zero);
                Step(context, body, 60);
                AssertTrue(
                    rb.angularVelocity.magnitude > established * 0.9f,
                    $"neutral twist must coast while held, not bleed through a damper (before={established:F2} after={rb.angularVelocity.magnitude:F2})");
            }
            finally
            {
                ReleaseAndCleanup(context, body, attach);
            }
        }

        private static void CaseReleaseKeepsTwist(Context context, bool sphere)
        {
            Component body = CreateBody(context, new Vector3(0f, 3f, -0.5f), sphere);
            GameObject attach = AcquireTop(context, body);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            try
            {
                SetSpin(context, body, new Vector2(1f, 0f));
                Step(context, body, 40);
                context.Release.Invoke(body, null);
                float released = rb.angularVelocity.magnitude;
                Step(context, body, 30);
                AssertTrue(
                    Mathf.Abs(rb.angularVelocity.magnitude - released) < released * 0.1f + 0.05f,
                    $"released twist must keep its physical angular velocity (released={released:F2} later={rb.angularVelocity.magnitude:F2})");
            }
            finally
            {
                body.gameObject.SetActive(false);
            }
        }

        private static void CasePivotTranslation(Context context, bool sphere)
        {
            Component body = CreateBody(context, new Vector3(0f, 3f, -0.5f), sphere);
            GameObject attach = AcquireTop(context, body);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            try
            {
                SetSpin(context, body, new Vector2(1f, 0f));
                Step(context, body, 40);
                float established = rb.angularVelocity.y;
                SetSpin(context, body, Vector2.zero);
                for (int tick = 0; tick < 30; tick++)
                {
                    attach.transform.position += new Vector3(0.02f, 0f, 0f);
                    Step(context, body, 1);
                }

                AssertTrue(
                    Mathf.Abs(rb.angularVelocity.y) > Mathf.Abs(established) * 0.9f,
                    $"a translating pivot must not damp the parallel twist (before={established:F2} after={rb.angularVelocity.y:F2})");
            }
            finally
            {
                ReleaseAndCleanup(context, body, attach);
            }
        }

        private static void CaseCenterGrab(Context context, bool sphere)
        {
            Component body = CreateBody(context, new Vector3(0f, 3f, -0.5f), sphere);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            GameObject attach = new("center attach");
            context.Resources.Add(attach);
            attach.transform.position = rb.position;
            attach.transform.rotation = Quaternion.identity;
            context.Acquire.Invoke(body, new object[] { attach.transform, rb.position });
            try
            {
                SetSpin(context, body, new Vector2(1f, 0f));
                Step(context, body, 40);
                float rate = rb.angularVelocity.magnitude;
                AssertTrue(rate > 5f, $"a center grab must leave twist fully free, got {rate:F2} rad/s after 0.8s");
            }
            finally
            {
                ReleaseAndCleanup(context, body, attach);
            }
        }

        private static void CaseSideGrabOrbit(Context context, bool sphere)
        {
            Component input = CreateSideGrab(context, new Vector3(-0.6f, 3f, -0.5f), sphere, out GameObject inputAttach);
            Component neutral = CreateSideGrab(context, new Vector3(0.6f, 3f, -0.5f), sphere, out GameObject neutralAttach);
            Rigidbody inputRb = input.GetComponent<Rigidbody>();
            Rigidbody neutralRb = neutral.GetComponent<Rigidbody>();
            try
            {
                context.SetControllerInput.Invoke(input, new object[] { new Vector2(-1f, 0f), Vector2.zero, Quaternion.identity, Vector3.right });
                Vector3 inputStart = inputRb.position;
                Vector3 neutralStart = neutralRb.position;
                StepPair(context, input, neutral, 10);
                float inputZ = inputRb.position.z - inputStart.z;
                float neutralZ = neutralRb.position.z - neutralStart.z;
                AssertTrue(
                    inputZ > 0.05f && inputZ > neutralZ * 4f + 0.02f,
                    $"J-left tangential force must push the side-grabbed COM +Z (input={inputZ:F3} neutral={neutralZ:F3})");
                AssertTrue(
                    inputRb.angularVelocity.y > 0.5f && inputRb.angularVelocity.y > neutralRb.angularVelocity.y * 2f + 0.1f,
                    $"J-left on a +X side pivot must yaw +Y (input={inputRb.angularVelocity.y:F2} neutral={neutralRb.angularVelocity.y:F2})");
            }
            finally
            {
                ReleaseAndCleanup(context, input, inputAttach);
                ReleaseAndCleanup(context, neutral, neutralAttach);
            }
        }

        private static Component CreateSideGrab(Context context, Vector3 position, bool sphere, out GameObject attach)
        {
            Component body = CreateBody(context, position, sphere);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            attach = new GameObject("side attach");
            context.Resources.Add(attach);
            Vector3 pivot = rb.position + new Vector3(0.3f, 0f, 0f);
            attach.transform.position = pivot - new Vector3(0f, 0f, 0.4f);
            attach.transform.rotation = Quaternion.identity;
            context.Acquire.Invoke(body, new object[] { attach.transform, pivot });
            return body;
        }

        private static void CaseSpinTest(Context context, bool sphere)
        {
            Component body = CreateBody(context, new Vector3(0f, 0.55f, -0.5f), sphere);
            context.StartSpinTest.Invoke(body, new object[] { 2f });
            FieldInfo grabOffset = context.Grabbable.GetField("_grabOffset", BindingFlags.Instance | BindingFlags.NonPublic);
            Vector3 before = (Vector3)grabOffset.GetValue(body);
            try
            {
                context.SetControllerInput.Invoke(body, new object[] { Vector2.zero, new Vector2(1f, 0f), Quaternion.identity, Vector3.right });
                Step(context, body, 30);
                Vector3 after = (Vector3)grabOffset.GetValue(body);
                AssertTrue(
                    Vector3.Distance(before, after) < 0.0001f,
                    $"manual input must not move the grab offset during the spin test (moved {Vector3.Distance(before, after):F4})");
            }
            finally
            {
                body.gameObject.SetActive(false);
            }
        }

        private static Component CreateBody(Context context, Vector3 position, bool sphere)
        {
            GameObject body = new($"pivot damping {(sphere ? "sphere" : "cube")}");
            context.Resources.Add(body);
            body.transform.localScale = Vector3.one * 0.55f;
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
            GameObject attach = new("pivot attach");
            context.Resources.Add(attach);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            Vector3 pivot = rb.position + new Vector3(0f, 0.3f, 0f);
            attach.transform.position = pivot + new Vector3(0f, 0.4f, 0f);
            attach.transform.rotation = Quaternion.identity;
            context.Acquire.Invoke(body, new object[] { attach.transform, pivot });
            return attach;
        }

        private static void SetSpin(Context context, Component body, Vector2 spin)
            => context.SetControllerInput.Invoke(body, new object[] { Vector2.zero, spin, Quaternion.identity, Vector3.right });

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
        }

        private static void RunCase(string name, Action test)
        {
            try
            {
                test();
            }
            catch (Exception exception)
            {
                Exception inner = exception is TargetInvocationException { InnerException: not null } tied
                    ? tied.InnerException
                    : exception;
                Failures.Add($"{name}: {inner.GetType().Name} {inner.Message.Split('\n')[0]}");
            }
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }
    }
}
