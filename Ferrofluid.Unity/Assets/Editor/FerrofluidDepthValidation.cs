using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidDepthValidation
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
            internal MethodInfo SetGrabDepthInput = null!;
            internal MethodInfo OnDisable = null!;
            internal MethodInfo StartSpinTest = null!;
            internal PropertyInfo GrabTarget = null!;
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
                context.SetGrabDepthInput = context.Grabbable.GetMethod("SetGrabDepthInput", BindingFlags.Instance | BindingFlags.NonPublic);
                context.OnDisable = context.Grabbable.GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic);
                context.StartSpinTest = context.Grabbable.GetMethod("StartSpinTest", BindingFlags.Instance | BindingFlags.NonPublic);
                context.GrabTarget = context.Grabbable.GetProperty("GrabTarget", BindingFlags.Instance | BindingFlags.NonPublic);
                if (context.SetGrabDepthInput == null)
                    throw new Exception("FerrofluidGrabbable.SetGrabDepthInput is missing; depth movement is not implemented.");

                Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true);
                context.Enclosure = enclosureType
                    .GetConstructor(
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                        null,
                        new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                        null)!
                    .Invoke(new object[] { Vector3.zero, 3f, new Vector3(0f, 0f, -2f), Vector3.forward, 0f });
                context.Material = new PhysicsMaterial("depth validation")
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
                    RunCase($"{label} away moves target along pointer", () => CaseDepthMoves(context, sphere));
                    RunCase($"{label} rotated pointer axis and closer sign", () => CaseRotatedAxis(context, sphere));
                    RunCase($"{label} ramp grows per step", () => CaseRamp(context, sphere));
                    RunCase($"{label} render cadence does not double step", () => CaseRenderCadence(context, sphere));
                    RunCase($"{label} reversal resets ramp", () => CaseReversal(context, sphere));
                    RunCase($"{label} release and reacquire reset", () => CaseReleaseResets(context, sphere));
                    RunCase($"{label} disable clears depth", () => CaseDisableClears(context, sphere));
                    RunCase($"{label} spin test ignores depth", () => CaseSpinTestIsolation(context, sphere));
                    RunCase($"{label} unheld depth input does nothing", () => CaseUnheld(context, sphere));
                    RunCase($"{label} pivot tracks depth without jump", () => CasePivotTracks(context, sphere));
                }

                if (Failures.Count > 0)
                    throw new Exception($"{Failures.Count} depth cases failed: {string.Join(" | ", Failures)}");

                Debug.Log("[FerrofluidDepthValidation] PASS: depth direction, ramp, cadence, clears, and pivot tracking verified.");
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

        private static void CaseDepthMoves(Context context, bool sphere)
        {
            Component body = CreateBody(context, new Vector3(0f, 2.5f, -0.5f), sphere);
            GameObject attach = AcquireTop(context, body, Quaternion.identity);
            Step(context, body, 5);
            Vector3 before = Target(context, body);
            context.SetGrabDepthInput.Invoke(body, new object[] { 1 });
            Step(context, body, 30);
            Vector3 moved = Target(context, body) - before;
            AssertTrue(
                Vector3.Dot(moved, attach.transform.forward) > 0.05f,
                $"away input must move the target along +forward (dot={Vector3.Dot(moved, attach.transform.forward):F4})");
            Vector3 axis = attach.transform.forward * Vector3.Dot(moved, attach.transform.forward);
            AssertTrue(Vector3.Distance(moved, axis) < 0.005f, $"movement must stay on the pointer axis (moved={moved})");
            ReleaseAndCleanup(context, body, attach);
        }

        private static void CaseRotatedAxis(Context context, bool sphere)
        {
            Component body = CreateBody(context, new Vector3(0f, 2.5f, -0.5f), sphere);
            GameObject attach = AcquireTop(context, body, Quaternion.Euler(0f, 90f, 0f));
            Step(context, body, 5);
            Vector3 before = Target(context, body);
            context.SetGrabDepthInput.Invoke(body, new object[] { -1 });
            Step(context, body, 30);
            Vector3 moved = Target(context, body) - before;
            Vector3 expected = -attach.transform.forward * Vector3.Dot(moved, -attach.transform.forward);
            AssertTrue(
                Vector3.Dot(moved, -attach.transform.forward) > 0.05f,
                $"closer input must pull along -forward of a rotated pointer (move={moved})");
            AssertTrue(
                Vector3.Distance(moved, expected) < 0.005f,
                $"movement must follow the rotated pointer axis only (move={moved} axisPart={expected})");
            ReleaseAndCleanup(context, body, attach);
        }

        private static void CaseRamp(Context context, bool sphere)
        {
            Component body = CreateBody(context, new Vector3(0f, 2.5f, -0.5f), sphere);
            GameObject attach = AcquireTop(context, body, Quaternion.identity);
            context.SetGrabDepthInput.Invoke(body, new object[] { 1 });
            float first = StepDistance(context, body, attach, 5);
            float second = StepDistance(context, body, attach, 5);
            float third = StepDistance(context, body, attach, 5);
            AssertTrue(
                second > first * 1.5f && third > second,
                $"displacement per step must ramp up ({first:F4}, {second:F4}, {third:F4})");
            Step(context, body, 20);
            float depthA = Vector3.Dot(Target(context, body) - attach.transform.position, attach.transform.forward);
            float rateA = StepDistance(context, body, attach, 10) / 0.2f;
            float depthB = Vector3.Dot(Target(context, body) - attach.transform.position, attach.transform.forward);
            float rateB = StepDistance(context, body, attach, 10) / 0.2f;
            AssertTrue(
                rateA > depthA * 0.55f && rateA < depthA * 1.45f && rateB > depthB * 0.55f && rateB < depthB * 1.45f,
                $"post-ramp rate must track current depth at 1x (rateA={rateA:F3} depthA={depthA:F3} rateB={rateB:F3} depthB={depthB:F3})");
            ReleaseAndCleanup(context, body, attach);
        }

        private static void CaseRenderCadence(Context context, bool sphere)
        {
            Component fast = CreateBody(context, new Vector3(-0.5f, 2.5f, -0.5f), sphere);
            GameObject fastAttach = AcquireTop(context, fast, Quaternion.identity);
            Component slow = CreateBody(context, new Vector3(0.5f, 2.5f, -0.5f), sphere);
            GameObject slowAttach = AcquireTop(context, slow, Quaternion.identity);
            context.SetGrabDepthInput.Invoke(fast, new object[] { 1 });
            context.SetGrabDepthInput.Invoke(slow, new object[] { 1 });
            for (int tick = 0; tick < 30; tick++)
            {
                context.SampleGrabTarget.Invoke(fast, new object[] { FixedDelta, Time.fixedUnscaledTime });
                if (tick % 3 == 0)
                    context.SampleGrabTarget.Invoke(slow, new object[] { FixedDelta * 3f, Time.fixedUnscaledTime });

                context.FixedUpdate.Invoke(fast, null);
                context.FixedUpdate.Invoke(slow, null);
                Physics.Simulate(FixedDelta);
            }

            float fastDepth = Vector3.Dot(Target(context, fast) - fastAttach.transform.position, Vector3.forward);
            float slowDepth = Vector3.Dot(Target(context, slow) - slowAttach.transform.position, Vector3.forward);
            AssertTrue(
                Mathf.Abs(fastDepth - slowDepth) < 0.02f,
                $"the same fixed tick count must yield the same depth regardless of render cadence (fast={fastDepth:F4} slow={slowDepth:F4})");
            ReleaseAndCleanup(context, fast, fastAttach);
            ReleaseAndCleanup(context, slow, slowAttach);
        }

        private static void CaseReversal(Context context, bool sphere)
        {
            Component body = CreateBody(context, new Vector3(0f, 2.5f, -0.5f), sphere);
            GameObject attach = AcquireTop(context, body, Quaternion.identity);
            context.SetGrabDepthInput.Invoke(body, new object[] { 1 });
            Step(context, body, 30);
            float deep = StepDistance(context, body, attach, 10);
            context.SetGrabDepthInput.Invoke(body, new object[] { -1 });
            float reversed = StepDistance(context, body, attach, 5);
            AssertTrue(
                reversed < deep * 0.7f,
                $"a reversal must restart the ramp instead of keeping full speed (pre={deep:F4} post={reversed:F4})");
            ReleaseAndCleanup(context, body, attach);
        }

        private static void CaseReleaseResets(Context context, bool sphere)
        {
            Component body = CreateBody(context, new Vector3(0f, 2.5f, -0.5f), sphere);
            GameObject attach = AcquireTop(context, body, Quaternion.identity);
            context.SetGrabDepthInput.Invoke(body, new object[] { 1 });
            Step(context, body, 30);
            context.Release.Invoke(body, null);
            GameObject attach2 = AcquireTop(context, body, Quaternion.identity);
            context.SetGrabDepthInput.Invoke(body, new object[] { 1 });
            float fresh = StepDistance(context, body, attach2, 5);
            AssertTrue(fresh < 0.02f, $"reacquire must restart the depth ramp (got {fresh:F4} in 5 ticks)");
            ReleaseAndCleanup(context, body, attach2);
            context.Resources.Add(attach);
        }

        private static void CaseDisableClears(Context context, bool sphere)
        {
            Component body = CreateBody(context, new Vector3(0f, 2.5f, -0.5f), sphere);
            GameObject attach = AcquireTop(context, body, Quaternion.identity);
            context.SetGrabDepthInput.Invoke(body, new object[] { 1 });
            Step(context, body, 10);
            Vector3 before = Target(context, body);
            context.OnDisable.Invoke(body, null);
            Step(context, body, 10);
            AssertTrue(
                Vector3.Distance(Target(context, body), before) < 0.001f,
                $"disable must drop the depth sample (moved {Vector3.Distance(Target(context, body), before):F4})");
            body.gameObject.SetActive(false);
            context.Resources.Add(attach);
        }

        private static void CaseSpinTestIsolation(Context context, bool sphere)
        {
            Component body = CreateBody(context, new Vector3(0f, 0.55f, -0.5f), sphere);
            context.StartSpinTest.Invoke(body, new object[] { 2f });
            context.SetGrabDepthInput.Invoke(body, new object[] { 1 });
            FieldInfo grabOffset = context.Grabbable.GetField("_grabOffset", BindingFlags.Instance | BindingFlags.NonPublic);
            Vector3 before = (Vector3)grabOffset.GetValue(body);
            Step(context, body, 40);
            Vector3 after = (Vector3)grabOffset.GetValue(body);
            AssertTrue(
                Vector3.Distance(before, after) < 0.0001f,
                $"depth input must not move the grab offset during the spin test (moved {Vector3.Distance(before, after):F4})");
            body.gameObject.SetActive(false);
        }

        private static void CaseUnheld(Context context, bool sphere)
        {
            Component body = CreateBody(context, new Vector3(0f, 4f, -0.5f), sphere);
            context.SetGrabDepthInput.Invoke(body, new object[] { 1 });
            Step(context, body, 15);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            AssertTrue(
                Mathf.Abs(rb.linearVelocity.x) < 0.001f && Mathf.Abs(rb.linearVelocity.z) < 0.001f,
                $"unheld depth input must not push the body ({rb.linearVelocity})");
            body.gameObject.SetActive(false);
        }

        private static void CasePivotTracks(Context context, bool sphere)
        {
            Component body = CreateBody(context, new Vector3(0f, 2.5f, -0.5f), sphere);
            GameObject attach = AcquireTop(context, body, Quaternion.identity);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            context.SetGrabDepthInput.Invoke(body, new object[] { 1 });
            for (int tick = 0; tick < 40; tick++)
            {
                Step(context, body, 1);
                Vector3 pivot = rb.position + (rb.rotation * new Vector3(0f, 0.275f, 0f));
                Vector3 desired = (Vector3)context.GrabTarget.GetValue(body)!;
                AssertTrue(
                    Vector3.Distance(pivot, desired) < 0.5f,
                    $"the physical pivot must track the shifted target without a jump (gap={Vector3.Distance(pivot, desired):F3} at tick {tick})");
            }

            ReleaseAndCleanup(context, body, attach);
        }

        private static float StepDistance(Context context, Component body, GameObject attach, int ticks)
        {
            Vector3 before = Target(context, body);
            Step(context, body, ticks);
            return Vector3.Dot(Target(context, body) - before, attach.transform.forward);
        }

        private static Vector3 Target(Context context, Component body)
            => (Vector3)context.GrabTarget.GetValue(body)!;

        private static Component CreateBody(Context context, Vector3 position, bool sphere)
        {
            GameObject body = new($"depth validation {(sphere ? "sphere" : "cube")}");
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

        private static GameObject AcquireTop(Context context, Component body, Quaternion attachRotation)
        {
            GameObject attach = new("depth attach");
            attach.transform.rotation = attachRotation;
            Vector3 pivot = body.GetComponent<Rigidbody>().position + new Vector3(0f, 0.3f, 0f);
            attach.transform.position = pivot - ((attachRotation * Vector3.forward) * 0.5f);
            context.Acquire.Invoke(body, new object[] { attach.transform, pivot });
            return attach;
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
