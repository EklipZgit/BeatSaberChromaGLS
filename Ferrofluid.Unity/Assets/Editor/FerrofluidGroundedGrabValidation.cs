using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // Grounded corner grabs: stationary grabs at each collider corner must not excite the body,
    // and slow attach drags must keep the body bounded against the floor. Diagnostics only.
    internal static class FerrofluidGroundedGrabValidation
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
                Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true);
                context.Enclosure = enclosureType
                    .GetConstructor(
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                        null,
                        new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                        null)!
                    .Invoke(new object[] { Vector3.zero, 3f, new Vector3(0f, 0f, -2f), Vector3.forward, 0f });
                context.Material = new PhysicsMaterial("grounded validation")
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

                GameObject attach = new("grounded attach");
                context.Resources.Add(attach);
                Component cube = CreateBody(context, new Vector3(0f, 0.275f, 0f));

                foreach (int order in new[] { 0, 1 })
                {
                    for (int index = 0; index < 8; index++)
                    {
                        int corner = order == 0 ? index : 7 - index;
                        RunCase($"corner {corner} order {order} stationary", () => CaseStationary(context, cube, attach, corner));
                        RunCase($"corner {corner} order {order} drag up-side", () => CaseDrag(context, cube, attach, corner, new Vector3(0.05f, 0.05f, 0f)));
                        RunCase($"corner {corner} order {order} drag varied", () => CaseDrag(context, cube, attach, corner, new Vector3(-0.05f, 0.05f, 0.05f)));
                        RunCase($"corner {corner} order {order} drag down-side", () => CaseDrag(context, cube, attach, corner, new Vector3(0.05f, -0.05f, 0f)));
                    }
                }

                RunCase("top-edge lift opposite pivot", () => CaseTopEdgeLift(context, cube, attach));
                RunCase("acquire interpolation anchor", () => CaseAcquireInterpolation(context, cube, attach));
                RunCase("top-corner supported lift", () => CaseSupportedLift(context, cube, attach, new Vector3(-0.5f, 0.5f, -0.5f), "top corner"));
                RunCase("below-top-left-edge supported lift", () => CaseSupportedLift(context, cube, attach, new Vector3(-0.5f, 0.4f, -0.5f), "below-top edge"));
                RunCase("blocker press bounded", () => CaseBlocker(context, cube, attach));

                if (Failures.Count > 0)
                    throw new Exception($"{Failures.Count} grounded grab cases failed: {string.Join(" | ", Failures)}");

                Debug.Log("[FerrofluidGroundedGrabValidation] PASS: grounded corner grabs stay quiescent.");
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

        private static void RunCase(string name, Action test)
        {
            try
            {
                test();
                Debug.Log($"[FerrofluidGroundedGrabValidation] {name}: PASS");
            }
            catch (Exception exception)
            {
                Failures.Add($"{name}: {exception.Message}");
                Debug.Log($"[FerrofluidGroundedGrabValidation] {name}: FAIL {exception.Message}");
            }
        }

        private static Component CreateBody(Context context, Vector3 position, bool sphere = false)
        {
            GameObject body = new(sphere ? "grounded sphere" : "grounded cube");
            context.Resources.Add(body);
            body.transform.position = position;
            body.transform.localScale = Vector3.one * 0.55f;
            Component grabbable = body.AddComponent(context.Grabbable);
            context.Initialize.Invoke(grabbable, new object[] { context.Enclosure, sphere, context.Material, context.Ownership });
            return grabbable;
        }

        private static Vector3 SettlePose(Component cube)
        {
            Rigidbody body = cube.GetComponent<Rigidbody>();
            return body.position;
        }

        private static Vector3 CornerLocal(int corner)
        {
            return new Vector3(
                (corner & 1) == 0 ? -0.5f : 0.5f,
                (corner & 2) == 0 ? -0.5f : 0.5f,
                (corner & 4) == 0 ? -0.5f : 0.5f);
        }

        private static void ResetCube(Context context, Component cube, GameObject attach)
        {
            context.Release.Invoke(cube, null);
            Rigidbody body = cube.GetComponent<Rigidbody>();
            body.position = new Vector3(0f, 0.275f, 0f);
            body.rotation = Quaternion.identity;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            for (int step = 0; step < 30; step++)
            {
                context.SampleGrabTarget.Invoke(cube, new object[] { FixedDelta, Time.unscaledTime });
                context.FixedUpdate.Invoke(cube, null);
                Physics.Simulate(FixedDelta);
            }

            // The fixture is only meaningful if the body is genuinely at rest before Acquire;
            // spheres keep a residual slow roll so the bound is speed, not a fixed pose.
            AssertTrue(
                body.linearVelocity.magnitude < 0.1f && body.angularVelocity.magnitude < 0.15f,
                $"the reset must leave the body at rest (pos={body.position} v={body.linearVelocity.magnitude:F3} omega={body.angularVelocity.magnitude:F3})");
        }

        private static Vector3 CornerWorld(Component cube, int corner)
        {
            Rigidbody body = cube.GetComponent<Rigidbody>();
            return body.position + (body.rotation * Vector3.Scale(CornerLocal(corner), cube.transform.localScale));
        }

        private sealed class Measurement
        {
            internal float MaxAngular;
            internal float MaxLinear;
            internal float MaxPoseJump;
            internal float MinCornerY;
            internal Vector3 FinalPosition;
            internal Quaternion FinalRotation;
        }

        private static Measurement StepAndMeasure(
            Context context,
            Component cube,
            int steps,
            int frameOffset)
        {
            Rigidbody body = cube.GetComponent<Rigidbody>();
            Measurement measurement = new() { MinCornerY = float.MaxValue };
            Vector3 previous = body.position;
            for (int step = 0; step < steps; step++)
            {
                context.SampleGrabTarget.Invoke(cube, new object[] { FixedDelta, Time.unscaledTime });
                context.FixedUpdate.Invoke(cube, null);
                Physics.Simulate(FixedDelta);
                if (step >= frameOffset)
                {
                    measurement.MaxAngular = Mathf.Max(measurement.MaxAngular, body.angularVelocity.magnitude);
                    measurement.MaxLinear = Mathf.Max(measurement.MaxLinear, body.linearVelocity.magnitude);
                    measurement.MaxPoseJump = Mathf.Max(measurement.MaxPoseJump, Vector3.Distance(previous, body.position));
                    for (int corner = 0; corner < 8; corner++)
                        measurement.MinCornerY = Mathf.Min(measurement.MinCornerY, CornerWorld(cube, corner).y);
                }

                previous = body.position;
            }

            measurement.FinalPosition = body.position;
            measurement.FinalRotation = body.rotation;
            return measurement;
        }

        private static void CaseStationary(Context context, Component cube, GameObject attach, int corner)
        {
            ResetCube(context, cube, attach);
            Rigidbody body = cube.GetComponent<Rigidbody>();
            Vector3 settled = body.position;
            Quaternion settledRotation = body.rotation;
            Vector3 grabPoint = CornerWorld(cube, corner);
            attach.transform.position = grabPoint;
            float initialCone = Mathf.Acos(Mathf.Clamp(Vector3.Dot((grabPoint - body.position).normalized, Vector3.up), -1f, 1f)) * Mathf.Rad2Deg;
            context.Acquire.Invoke(cube, new object[] { attach.transform, grabPoint });
            Measurement measurement = StepAndMeasure(context, cube, 10, 0);
            float comDrift = Vector3.Distance(measurement.FinalPosition, settled);
            float rotationDrift = Quaternion.Angle(measurement.FinalRotation, settledRotation);
            Debug.Log(
                $"[FerrofluidGroundedGrabValidation] corner {corner} stationary cone={initialCone:F1} drift={comDrift:F4} rot={rotationDrift:F2} maxOmega={measurement.MaxAngular:F3} maxV={measurement.MaxLinear:F3} minCornerY={measurement.MinCornerY:F4}");
            if (comDrift > 0.003f || rotationDrift > 2f || measurement.MaxAngular > 0.5f || measurement.MaxLinear > 0.2f)
            {
                throw new Exception(
                    $"stationary corner grab excited the body (cone={initialCone:F1} drift={comDrift:F4} rot={rotationDrift:F2} omega={measurement.MaxAngular:F3} v={measurement.MaxLinear:F3})");
            }
        }

        private static void CaseDrag(Context context, Component cube, GameObject attach, int corner, Vector3 drag)
        {
            ResetCube(context, cube, attach);
            Vector3 grabPoint = CornerWorld(cube, corner);
            attach.transform.position = grabPoint;
            context.Acquire.Invoke(cube, new object[] { attach.transform, grabPoint });
            Rigidbody body = cube.GetComponent<Rigidbody>();
            Vector3 anchor = attach.transform.position;
            Vector3 previous = body.position;
            float maxAngular = 0f;
            float maxLinear = 0f;
            float maxJump = 0f;
            float minCornerY = float.MaxValue;
            for (int step = 0; step < 20; step++)
            {
                float fraction = Mathf.Clamp01(step * FixedDelta / 0.15f);
                attach.transform.position = anchor + (drag * fraction);
                context.SampleGrabTarget.Invoke(cube, new object[] { FixedDelta, Time.unscaledTime });
                context.FixedUpdate.Invoke(cube, null);
                Physics.Simulate(FixedDelta);
                maxAngular = Mathf.Max(maxAngular, body.angularVelocity.magnitude);
                maxLinear = Mathf.Max(maxLinear, body.linearVelocity.magnitude);
                maxJump = Mathf.Max(maxJump, Vector3.Distance(previous, body.position));
                for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
                    minCornerY = Mathf.Min(minCornerY, CornerWorld(cube, cornerIndex).y);
                previous = body.position;
            }

            Debug.Log(
                $"[FerrofluidGroundedGrabValidation] corner {corner} drag {drag} maxOmega={maxAngular:F3} maxV={maxLinear:F3} jump={maxJump:F4} minCornerY={minCornerY:F4}");
            if (minCornerY < -0.005f || maxLinear > 2f || maxAngular > 10f || maxJump > 0.04f)
            {
                throw new Exception(
                    $"slow drag destabilized the grounded cube (omega={maxAngular:F3} v={maxLinear:F3} jump={maxJump:F4} minCornerY={minCornerY:F4})");
            }
        }

        private static void CaseTopEdgeLift(Context context, Component cube, GameObject attach)
        {
            ResetCube(context, cube, attach);
            Rigidbody body = cube.GetComponent<Rigidbody>();
            Vector3 local = new(-0.5f, 0.4f, -0.5f);
            Vector3 grabPoint = body.position + Vector3.Scale(local, cube.transform.localScale);
            attach.transform.position = grabPoint;
            context.Acquire.Invoke(cube, new object[] { attach.transform, grabPoint });
            Vector3 oppositeCorner = body.position + (body.rotation * new Vector3(0.275f, -0.275f, 0.275f));
            float lever = Vector3.Distance(oppositeCorner, grabPoint);
            float targetY = (lever / Mathf.Sqrt(2f)) + 0.03f;
            Vector3 anchor = attach.transform.position;
            float maxAngular = 0f;
            float maxLinear = 0f;
            float maxJump = 0f;
            float minCornerY = float.MaxValue;
            float peakSwing = 0f;
            Vector3 previous = body.position;
            for (int step = 0; step < 75; step++)
            {
                float phase = step * FixedDelta;
                float lift = Mathf.Clamp01(phase / 0.5f);
                float side = Mathf.Clamp01(Mathf.Max(0f, phase - 0.5f) / 0.4f);
                attach.transform.position = anchor + new Vector3(0.05f * side, (targetY + 0.15f - anchor.y) * lift, 0f);
                context.SampleGrabTarget.Invoke(cube, new object[] { FixedDelta, Time.unscaledTime });
                context.FixedUpdate.Invoke(cube, null);
                Physics.Simulate(FixedDelta);
                maxAngular = Mathf.Max(maxAngular, body.angularVelocity.magnitude);
                maxLinear = Mathf.Max(maxLinear, body.linearVelocity.magnitude);
                maxJump = Mathf.Max(maxJump, Vector3.Distance(previous, body.position));
                peakSwing = Mathf.Max(
                    peakSwing,
                    Vector3.Angle(Vector3.down, body.position - attach.transform.position));
                for (int corner = 0; corner < 8; corner++)
                    minCornerY = Mathf.Min(minCornerY, CornerWorld(cube, corner).y);
                previous = body.position;
            }

            Debug.Log(
                $"[FerrofluidGroundedGrabValidation] top-edge lift lever={lever:F3} targetY={targetY:F3} peakSwing={peakSwing:F1} maxOmega={maxAngular:F3} maxV={maxLinear:F3} jump={maxJump:F4} minCornerY={minCornerY:F4}");
            float finalSwing = Vector3.Angle(Vector3.down, body.position - attach.transform.position);
            if (minCornerY < -0.005f || maxLinear > 3f || maxAngular > 15f || maxJump > 0.06f || finalSwing > 30f)
            {
                throw new Exception(
                    $"top-edge lift destabilized the cube (omega={maxAngular:F3} v={maxLinear:F3} jump={maxJump:F4} minCornerY={minCornerY:F4} finalSwing={finalSwing:F1})");
            }
        }

        private static void CaseAcquireInterpolation(Context context, Component cube, GameObject attach)
        {
            // The rendered transform lags the rigidbody under interpolation; the anchor must seed
            // from the physical pose so an acquire never snaps the body toward the stale transform.
            ResetCube(context, cube, attach);
            Rigidbody body = cube.GetComponent<Rigidbody>();
            cube.transform.position = body.position + new Vector3(0.04f, 0.03f, -0.02f);
            cube.transform.rotation = Quaternion.Euler(8f, 10f, 6f);
            Vector3 localClick = new(-0.5f, 0.4f, -0.5f);
            Vector3 grabPoint = cube.transform.TransformPoint(localClick);
            attach.transform.position = grabPoint;
            context.Acquire.Invoke(cube, new object[] { attach.transform, grabPoint });
            Rigidbody anchor = (Rigidbody)context.Grabbable
                .GetField("_anchorBody", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(cube);
            Vector3 expected = body.position + (body.rotation * Vector3.Scale(
                cube.transform.InverseTransformPoint(grabPoint),
                cube.transform.lossyScale));
            AssertTrue(
                Vector3.Distance(anchor.position, expected) < 0.001f,
                $"the anchor must seed at the physical pose (anchor={anchor.position} expected={expected})");
            // The fixture moved the rendered transform manually; publish so the pending teleport
            // does not land on the first simulated step and fake an acquire snap.
            body.PublishTransform();
            Vector3 before = body.position;
            Quaternion beforeRot = body.rotation;
            context.SampleGrabTarget.Invoke(cube, new object[] { FixedDelta, Time.unscaledTime });
            context.FixedUpdate.Invoke(cube, null);
            Physics.Simulate(FixedDelta);
            AssertTrue(
                Vector3.Distance(before, body.position) < 0.01f
                    && Quaternion.Angle(beforeRot, body.rotation) < 2f,
                $"acquire must not snap the body toward the rendered pose (jump={Vector3.Distance(before, body.position):F4} rot={Quaternion.Angle(beforeRot, body.rotation):F2})");
        }

        private static void CaseBlocker(Context context, Component cube, GameObject attach)
        {
            // Holding the grab press against a solid blocker for a full second must not explode;
            // the bounded pivot drive must let the contact win and release with no stored energy.
            CaseBlockerShape(context, cube, attach, false);
            cube.gameObject.SetActive(false);
            Component sphere = CreateBody(context, new Vector3(0f, 0.275f, 0f), true);
            CaseBlockerShape(context, sphere, attach, true);
        }

        private static void CaseBlockerShape(Context context, Component cube, GameObject attach, bool sphereShape)
        {
            ResetCube(context, cube, attach);
            string label = sphereShape ? "sphere" : "cube";
            GameObject blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.transform.position = new Vector3(0.6f, 0.5f, 0f);
            blocker.transform.localScale = new Vector3(0.2f, 1f, 3f);
            blocker.GetComponent<Collider>().material = context.Material;
            context.Resources.Add(blocker);
            Rigidbody body = cube.GetComponent<Rigidbody>();
            Collider bodyCollider = body.GetComponentInChildren<Collider>();
            Collider blockerCollider = blocker.GetComponent<Collider>();
            Collider floorCollider = context.Floor.GetComponent<Collider>();
            // The sphere's grab point must be a real point on its own surface, not a point outside
            // the collider the joint could never reach.
            Vector3 grabPoint = sphereShape
                ? body.position + (new Vector3(0.8f, 0.6f, 0f).normalized * 0.275f)
                : body.position + new Vector3(0.275f, 0.1f, 0f);
            attach.transform.position = grabPoint;
            context.Acquire.Invoke(cube, new object[] { attach.transform, grabPoint });
            float maxAngular = 0f;
            float maxLinear = 0f;
            float maxJump = 0f;
            float maxDepth = 0f;
            float minBodyY = float.MaxValue;
            Vector3 previous = body.position;

            void MeasureStep()
            {
                context.SampleGrabTarget.Invoke(cube, new object[] { FixedDelta, Time.unscaledTime });
                context.FixedUpdate.Invoke(cube, null);
                Physics.Simulate(FixedDelta);
                maxAngular = Mathf.Max(maxAngular, body.angularVelocity.magnitude);
                maxLinear = Mathf.Max(maxLinear, body.linearVelocity.magnitude);
                maxJump = Mathf.Max(maxJump, Vector3.Distance(previous, body.position));
                minBodyY = Mathf.Min(minBodyY, body.position.y);
                // Actual penetration on the rigidbody pose plus each collider's offset, never the
                // interpolated transform.
                if (Physics.ComputePenetration(
                        bodyCollider,
                        body.position,
                        body.rotation,
                        blockerCollider,
                        blocker.transform.position,
                        blocker.transform.rotation,
                        out _,
                        out float blockerDepth))
                    maxDepth = Mathf.Max(maxDepth, blockerDepth);
                if (Physics.ComputePenetration(
                        bodyCollider,
                        body.position,
                        body.rotation,
                        floorCollider,
                        context.Floor.transform.position,
                        context.Floor.transform.rotation,
                        out _,
                        out float floorDepth))
                    maxDepth = Mathf.Max(maxDepth, floorDepth);
                previous = body.position;
            }

            for (int step = 0; step < 15; step++)
            {
                attach.transform.position += new Vector3(0.4f / 15f, 0f, 0f);
                MeasureStep();
            }

            // Hold the press for a full second: the bounded drive must stay bounded while the
            // contact keeps winning.
            for (int step = 0; step < 50; step++)
            {
                MeasureStep();
            }

            // Reverse the cursor out and measure the release through the same metrics.
            attach.transform.position = body.position + new Vector3(-0.3f, 0.3f, 0f);
            MeasureStep();
            Vector3 preReleaseVelocity = body.GetPointVelocity(body.position);
            Vector3 preReleaseAngular = body.angularVelocity;
            context.Release.Invoke(cube, null);
            AssertTrue(
                Vector3.Distance(preReleaseVelocity, body.GetPointVelocity(body.position)) < 0.001f
                    && Vector3.Distance(preReleaseAngular, body.angularVelocity) < 0.001f,
                $"the {label} release must preserve the blocked velocity, not kick stored energy");
            // Post-release ticks still run through the same measurement so the settled state is
            // covered by the same bounds.
            MeasureStep();
            MeasureStep();
            context.Resources.Remove(blocker);
            Object.DestroyImmediate(blocker);
            Debug.Log(
                $"[FerrofluidGroundedGrabValidation] {label} blocker maxOmega={maxAngular:F3} maxV={maxLinear:F3} jump={maxJump:F4} depth={maxDepth:F4} minY={minBodyY:F4}");
            AssertTrue(
                maxLinear < 5f && maxAngular < 15f && maxJump < 0.15f && minBodyY > -0.005f && maxDepth <= 0.005f,
                $"pressing a held {label} into a blocker must stay bounded (omega={maxAngular:F3} v={maxLinear:F3} jump={maxJump:F4} depth={maxDepth:F4} minY={minBodyY:F4})");
        }

        // A supported corner stays on the floor while the cube rotates around the click lever:
        // the floor keeps bearing weight, the corner never sinks, and the COM swings toward
        // directly under the anchor.
        private static void CaseSupportedLift(Context context, Component cube, GameObject attach, Vector3 localClick, string label)
        {
            ResetCube(context, cube, attach);
            Rigidbody body = cube.GetComponent<Rigidbody>();
            Vector3 grabPoint = body.position + (body.rotation * Vector3.Scale(localClick, Vector3.one * 0.55f));
            attach.transform.position = grabPoint;
            context.Acquire.Invoke(cube, new object[] { attach.transform, grabPoint });
            Rigidbody anchor = (Rigidbody)context.Grabbable
                .GetField("_anchorBody", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(cube);
            float initialAngle = Vector3.Angle(Vector3.down, body.position - anchor.position);
            float minCornerY = float.MaxValue;
            Quaternion initialRotation = body.rotation;
            Vector3 target = new(grabPoint.x + 0.05f, 0.75f, grabPoint.z);
            Vector3 start = attach.transform.position;
            for (int step = 0; step < 20; step++)
            {
                attach.transform.position = Vector3.Lerp(start, target, (step + 1f) / 20f);
                context.SampleGrabTarget.Invoke(cube, new object[] { FixedDelta, Time.unscaledTime });
                context.FixedUpdate.Invoke(cube, null);
                Physics.Simulate(FixedDelta);
                minCornerY = Mathf.Min(minCornerY, MinCornerY(body));
            }

            for (int step = 0; step < 150; step++)
            {
                context.SampleGrabTarget.Invoke(cube, new object[] { FixedDelta, Time.unscaledTime });
                context.FixedUpdate.Invoke(cube, null);
                Physics.Simulate(FixedDelta);
                minCornerY = Mathf.Min(minCornerY, MinCornerY(body));
            }

            float finalMinY = MinCornerY(body);
            float rotationDegrees = Quaternion.Angle(initialRotation, body.rotation);
            float finalAngle = Vector3.Angle(Vector3.down, body.position - anchor.position);
            Debug.Log(
                $"[FerrofluidGroundedGrabValidation] {label} supported lift minY={minCornerY:F4} finalY={finalMinY:F4} rot={rotationDegrees:F2} angle {initialAngle:F2}->{finalAngle:F2}");
            AssertTrue(
                minCornerY >= -0.005f && finalMinY <= 0.015f && rotationDegrees > 5f && finalAngle < initialAngle,
                $"the {label} supported lift must keep floor contact, rotate, and settle toward the anchor ({minCornerY:F4} {finalMinY:F4} {rotationDegrees:F2} {finalAngle:F2})");
        }

        private static float MinCornerY(Rigidbody body)
        {
            float minimum = float.MaxValue;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = body.position + (body.rotation * (new Vector3(x, y, z) * 0.275f));
                        minimum = Mathf.Min(minimum, corner.y);
                    }
                }
            }

            return minimum;
        }

        private static void AssertTrue(bool condition, string what)
        {
            if (!condition)
                throw new Exception(what);
        }
    }
}
