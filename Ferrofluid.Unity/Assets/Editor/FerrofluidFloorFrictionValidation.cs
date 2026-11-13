using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidFloorFrictionValidation
    {
        private const float FixedDelta = 0.02f;
        private static readonly List<string> Failures = new();
        private static readonly List<string> NotVerified = new();

        private sealed class Context
        {
            internal Type Grabbable = null!;
            internal MethodInfo Initialize = null!;
            internal MethodInfo Acquire = null!;
            internal MethodInfo Release = null!;
            internal MethodInfo FixedUpdate = null!;
            internal MethodInfo SampleGrabTarget = null!;
            internal MethodInfo SetControllerInput = null!;
            internal MethodInfo ApplyFloorSpin = null!;
            internal MethodInfo ApplyRolling = null!;
            internal MethodInfo SphereTwistAccel = null!;
            internal object Enclosure = null!;
            internal object WideEnclosure = null!;
            internal PhysicsMaterial Material = null!;
            internal object Ownership = null!;
            internal GameObject Floor = null!;
            internal Assembly Plugin = null!;
            internal List<Object> Resources = new();
        }

        public static void Run()
        {
            Failures.Clear();
            NotVerified.Clear();
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
                context.ApplyFloorSpin = context.Grabbable.GetMethod("ApplyCubeFloorSpinResistance", BindingFlags.Instance | BindingFlags.NonPublic);
                context.ApplyRolling = context.Grabbable.GetMethod("ApplySphereRollingResistance", BindingFlags.Instance | BindingFlags.NonPublic);
                context.SphereTwistAccel = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabPhysics", true)
                    .GetMethod("EvaluateSphereFloorSpinAngularAcceleration", BindingFlags.Static | BindingFlags.NonPublic);

                Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true);
                ConstructorInfo enclosureCtor = enclosureType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null,
                    new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                    null)!;
                context.Enclosure = enclosureCtor.Invoke(new object[] { Vector3.zero, 3f, new Vector3(0f, 0f, -2f), Vector3.forward, 0f });
                context.WideEnclosure = enclosureCtor.Invoke(new object[] { Vector3.zero, 100f, new Vector3(0f, 0f, -100f), Vector3.forward, 0f });
                context.Material = new PhysicsMaterial("floor friction validation")
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

                AssertTrue(context.ApplyFloorSpin != null, "FerrofluidGrabbable must own ApplyCubeFloorSpinResistance for the grounded-cube torque.");

                RunCase("material factory body vs floor values", () => CaseMaterialFactory(context));
                RunCase("isolated grounded torque delivery", () => CaseIsolatedTorque(context));
                RunCase("airborne cube keeps spin", () => CaseAirborneKeepsSpin(context));
                RunCase("held grounded cube skips floor torque", () => CaseHeldSkipsFloor(context));
                RunCase("cap is twenty revolutions", () => CaseCap(context));
                RunCase("sphere backspin and cube slide halved grip", () => CaseSlidingPair(context));
                RunCase("floor sphere twist half-damps", () => CaseSphereFloorTwist(context));
                RunCase("existing grab physics floor and wall damping", () => typeof(FerrofluidGrabValidation)
                    .GetMethod("ValidateGrabPhysics", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, new object[] { context.Plugin }));
                RunDiagnostic("free corner-top lifetime", () => DiagnoseFreeCorner(context));

                foreach (string note in NotVerified)
                    Debug.Log($"[FerrofluidFloorFrictionValidation] NOT VERIFIED: {note}");

                if (Failures.Count > 0)
                    throw new Exception($"{Failures.Count} floor friction cases failed: {string.Join(" | ", Failures)}");

                Debug.Log($"[FerrofluidFloorFrictionValidation] PASS: isolated torque, airborne/held immunity, cap, material split, sliding pair verified. {NotVerified.Count} diagnostics not verified.");
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

        private static void CaseMaterialFactory(Context context)
        {
            Type manager = context.Plugin.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true);
            MethodInfo factory = manager.GetMethod("CreateContactMaterial", BindingFlags.Static | BindingFlags.NonPublic);
            AssertTrue(factory != null, "CreateContactMaterial factory must exist to split body and floor grip.");
            PhysicsMaterial body = (PhysicsMaterial)factory!.Invoke(null, new object[] { false })!;
            PhysicsMaterial floor = (PhysicsMaterial)factory.Invoke(null, new object[] { true })!;
            try
            {
                AssertTrue(Mathf.Abs(body.dynamicFriction - 0.6f) < 0.001f && Mathf.Abs(body.staticFriction - 0.6f) < 0.001f && body.frictionCombine == PhysicsMaterialCombine.Average, $"body material must keep 0.6 Average grip (got {body.dynamicFriction}/{body.frictionCombine})");
                AssertTrue(Mathf.Abs(floor.dynamicFriction - 0.15f) < 0.001f && Mathf.Abs(floor.staticFriction - 0.15f) < 0.001f && floor.frictionCombine == PhysicsMaterialCombine.Minimum, $"floor material must cut grip via 0.15 Minimum (got {floor.dynamicFriction}/{floor.frictionCombine})");
                AssertTrue(Mathf.Abs(floor.bounciness - 0.3f) < 0.001f && Mathf.Abs(body.bounciness - 0.3f) < 0.001f && floor.bounceCombine == PhysicsMaterialCombine.Average, "both materials must keep the shared bounce restitution");
            }
            finally
            {
                Object.DestroyImmediate(body);
                Object.DestroyImmediate(floor);
            }
        }

        private static void CaseIsolatedTorque(Context context)
        {
            float expectedStep = 20f * 2f * Mathf.PI / 90f * FixedDelta;
            RunIsolated(context, new Vector3(0f, 2f, 0f), new Vector3(0f, 2f - expectedStep, 0f), "positive spin");
            RunIsolated(context, new Vector3(0f, -2f, 0f), new Vector3(0f, -2f + expectedStep, 0f), "negative spin");
            RunIsolated(context, new Vector3(0f, 0.005f, 0f), new Vector3(0f, 0f, 0f), "last step stops without overshoot");
            RunIsolated(context, new Vector3(1f, 2f, 0.5f), new Vector3(1f, 2f - expectedStep, 0.5f), "mixed spin keeps X/Z");

            Component held = CreateCube(context, new Vector3(8f, 0.28f, -8f), Quaternion.identity, wide: true);
            Rigidbody heldRb = held.GetComponent<Rigidbody>();
            GameObject attach = new("held gate attach");
            context.Resources.Add(attach);
            attach.transform.position = heldRb.position + new Vector3(0f, 0.4f, 0f);
            context.Acquire.Invoke(held, new object[] { attach.transform, heldRb.position + new Vector3(0f, 0.3f, 0f) });
            try
            {
                heldRb.angularVelocity = new Vector3(0f, 2f, 0f);
                context.ApplyFloorSpin.Invoke(held, new object[] { heldRb.position, Vector3.zero, FixedDelta });
                Physics.Simulate(FixedDelta);
                AssertTrue(Mathf.Abs(heldRb.angularVelocity.y - 2f) < 0.0001f, $"held body must skip the floor torque ({heldRb.angularVelocity.y:F4})");
            }
            finally
            {
                context.Release.Invoke(held, null);
                held.gameObject.SetActive(false);
            }

            Component lifted = CreateCube(context, new Vector3(8f, 0.30f, -8f), Quaternion.identity, wide: true);
            try
            {
                lifted.GetComponent<Rigidbody>().angularVelocity = new Vector3(0f, 2f, 0f);
                context.ApplyFloorSpin.Invoke(lifted, new object[] { lifted.GetComponent<Rigidbody>().position, Vector3.zero, FixedDelta });
                Physics.Simulate(FixedDelta);
                AssertTrue(Mathf.Abs(lifted.GetComponent<Rigidbody>().angularVelocity.y - 2f) < 0.0001f, $"a body above the contact gap must skip the floor torque ({lifted.GetComponent<Rigidbody>().angularVelocity.y:F4})");
            }
            finally
            {
                lifted.gameObject.SetActive(false);
            }

            Component rising = CreateCube(context, new Vector3(8f, 0.28f, -8f), Quaternion.identity, wide: true);
            try
            {
                rising.GetComponent<Rigidbody>().linearVelocity = new Vector3(0f, 1f, 0f);
                rising.GetComponent<Rigidbody>().angularVelocity = new Vector3(0f, 2f, 0f);
                context.ApplyFloorSpin.Invoke(rising, new object[] { rising.GetComponent<Rigidbody>().position, rising.GetComponent<Rigidbody>().linearVelocity, FixedDelta });
                Physics.Simulate(FixedDelta);
                AssertTrue(Mathf.Abs(rising.GetComponent<Rigidbody>().angularVelocity.y - 2f) < 0.0001f, $"a body leaving the floor must skip the brake ({rising.GetComponent<Rigidbody>().angularVelocity.y:F4})");
            }
            finally
            {
                rising.gameObject.SetActive(false);
            }

            Component sphere = CreateCube(context, new Vector3(8f, 0.28f, -8f), Quaternion.identity, wide: true, sphere: true);
            try
            {
                sphere.GetComponent<Rigidbody>().angularVelocity = new Vector3(0f, 2f, 0f);
                context.ApplyFloorSpin.Invoke(sphere, new object[] { sphere.GetComponent<Rigidbody>().position, Vector3.zero, FixedDelta });
                Physics.Simulate(FixedDelta);
                AssertTrue(Mathf.Abs(sphere.GetComponent<Rigidbody>().angularVelocity.y - 2f) < 0.0001f, $"spheres keep their own rolling loss ({sphere.GetComponent<Rigidbody>().angularVelocity.y:F4})");
            }
            finally
            {
                sphere.gameObject.SetActive(false);
            }
        }

        private static void RunIsolated(Context context, Vector3 spin, Vector3 expected, string label)
        {
            Component body = CreateCube(context, new Vector3(8f, 0.28f, -8f), Quaternion.identity, wide: true);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            try
            {
                rb.angularVelocity = spin;
                context.ApplyFloorSpin.Invoke(body, new object[] { rb.position, Vector3.zero, FixedDelta });
                Physics.Simulate(FixedDelta);
                float error = Vector3.Distance(rb.angularVelocity, expected);
                AssertTrue(error < 0.005f, $"isolated floor torque {label}: expected {expected}, got {rb.angularVelocity}");
            }
            finally
            {
                body.gameObject.SetActive(false);
            }
        }

        private static void CaseAirborneKeepsSpin(Context context)
        {
            Component body = CreateCube(context, new Vector3(0f, 25f, -0.5f), Quaternion.identity);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            try
            {
                rb.angularVelocity = new Vector3(0f, 5f, 2f);
                Step(context, body, 50);
                AssertTrue(
                    Mathf.Abs(rb.angularVelocity.y - 5f) < 0.05f && Mathf.Abs(rb.angularVelocity.z - 2f) < 0.05f,
                    $"airborne spin must be untouched ({rb.angularVelocity})");
            }
            finally
            {
                body.gameObject.SetActive(false);
            }
        }

        private static void CaseHeldSkipsFloor(Context context)
        {
            Component body = CreateCube(context, new Vector3(0f, 0.28f, -0.5f), Quaternion.identity);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            GameObject attach = new("held attach");
            context.Resources.Add(attach);
            attach.transform.position = rb.position + new Vector3(0f, 0.4f, 0f);
            context.Acquire.Invoke(body, new object[] { attach.transform, rb.position + new Vector3(0f, 0.3f, 0f) });
            try
            {
                rb.angularVelocity = new Vector3(0f, 6f, 0f);
                float before = rb.angularVelocity.y;
                Step(context, body, 50);
                AssertTrue(
                    Mathf.Abs(rb.angularVelocity.y) > before * 0.9f,
                    $"held twist must not bleed through the floor torque (before={before:F2} after={rb.angularVelocity.y:F2})");
            }
            finally
            {
                context.Release.Invoke(body, null);
                body.gameObject.SetActive(false);
            }
        }

        private static void CaseCap(Context context)
        {
            Component body = CreateCube(context, Vector3.up * 5f, Quaternion.identity);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            try
            {
                AssertTrue(
                    Mathf.Abs(rb.maxAngularVelocity - (20f * 2f * Mathf.PI)) < 0.05f,
                    $"maxAngularVelocity must be 20 RPS, got {rb.maxAngularVelocity}");
            }
            finally
            {
                body.gameObject.SetActive(false);
            }
        }

        private static void CaseSlidingPair(Context context)
        {
            Type manager = context.Plugin.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true);
            MethodInfo factory = manager.GetMethod("CreateContactMaterial", BindingFlags.Static | BindingFlags.NonPublic);
            AssertTrue(factory != null, "CreateContactMaterial factory is required for the sliding pair.");

            bool hadFloor = context.Floor.activeSelf;
            context.Floor.SetActive(false);
            try
            {
                var baseline = SlideRun(factory!, context, new Vector3(-10f, 0f, -8f), floorMaterialNew: false);
                var halved = SlideRun(factory, context, new Vector3(10f, 0f, -8f), floorMaterialNew: true);
                AssertTrue(
                    halved.sphereBackspin < baseline.sphereBackspin - 0.5f,
                    $"halved floor grip must strip sphere backspin more slowly (baseline={baseline.sphereBackspin:F3} halved={halved.sphereBackspin:F3})");
                AssertTrue(
                    halved.sphereSlide > baseline.sphereSlide * 1.1f,
                    $"halved floor grip must carry the sliding sphere farther (baseline={baseline.sphereSlide:F3} halved={halved.sphereSlide:F3})");
                AssertTrue(
                    halved.cubeSlide > baseline.cubeSlide * 1.1f,
                    $"halved floor grip must carry the flat cube farther (baseline={baseline.cubeSlide:F3} halved={halved.cubeSlide:F3})");
            }
            finally
            {
                context.Floor.SetActive(hadFloor);
            }
        }

        private static (float sphereBackspin, float sphereSlide, float cubeSlide) SlideRun(
            MethodInfo factory,
            Context context,
            Vector3 origin,
            bool floorMaterialNew)
        {
            PhysicsMaterial plateMaterial = (PhysicsMaterial)factory.Invoke(null, new object[] { floorMaterialNew })!;
            PhysicsMaterial bodyMaterial = (PhysicsMaterial)factory.Invoke(null, new object[] { false })!;
            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            context.Resources.Add(plate);
            plate.transform.localScale = new Vector3(20f, 0.1f, 20f);
            plate.transform.position = origin + new Vector3(0f, -0.05f, 0f);
            plate.GetComponent<Collider>().material = plateMaterial;
            try
            {
                Component sphere = CreateBodyWithMaterial(context, origin + new Vector3(0f, 0.276f, -1f), Quaternion.identity, bodyMaterial, sphere: true);
                Rigidbody sphereRb = sphere.GetComponent<Rigidbody>();
                Component cube = CreateBodyWithMaterial(context, origin + new Vector3(0f, 0.276f, 1f), Quaternion.identity, bodyMaterial, sphere: false);
                Rigidbody cubeRb = cube.GetComponent<Rigidbody>();
                sphereRb.linearVelocity = new Vector3(0f, 0f, 3f);
                sphereRb.angularVelocity = new Vector3(-25f, 0f, 0f);
                cubeRb.linearVelocity = new Vector3(0f, 0f, 3f);
                Vector3 sphereStart = sphereRb.position;
                Vector3 cubeStart = cubeRb.position;
                try
                {
                    StepPair(context, sphere, cube, 15);
                }
                finally
                {
                    sphere.gameObject.SetActive(false);
                    cube.gameObject.SetActive(false);
                }

                return (sphereRb.angularVelocity.x, sphereRb.position.z - sphereStart.z, cubeRb.position.z - cubeStart.z);
            }
            finally
            {
                plate.SetActive(false);
                Object.DestroyImmediate(plateMaterial);
                Object.DestroyImmediate(bodyMaterial);
            }
        }

        private static float ExpectedTwist(Context context, float omega, int steps)
        {
            for (int tick = 0; tick < steps; tick++)
                omega += (float)context.SphereTwistAccel.Invoke(null, new object[] { omega, FixedDelta })! * FixedDelta;
            return omega;
        }

        private static void CaseSphereFloorTwist(Context context)
        {
            Component sphere = CreateCube(context, new Vector3(8f, 0.276f, -8f), Quaternion.identity, wide: true, sphere: true);
            Rigidbody rb = sphere.GetComponent<Rigidbody>();
            try
            {
                rb.angularVelocity = new Vector3(0f, 2f, 0f);
                InvokeRolling(context, sphere, FixedDelta);
                Physics.Simulate(FixedDelta);
                AssertTrue(
                    Mathf.Abs(rb.angularVelocity.y - ExpectedTwist(context, 2f, 1)) < 0.002f,
                    $"one grounded step must damp twist at the floor-spin rate ({rb.angularVelocity.y:F4})");

                for (int tick = 0; tick < 49; tick++)
                {
                    InvokeRolling(context, sphere, FixedDelta);
                    Physics.Simulate(FixedDelta);
                }

                AssertTrue(
                    Mathf.Abs(rb.angularVelocity.y - ExpectedTwist(context, 2f, 50)) < 0.05f,
                    $"50 grounded steps must track the floor-spin brake, now including its sub-RPS finish ({rb.angularVelocity.y:F4})");
                AssertTrue(Mathf.Abs(rb.angularDamping) < 0.0001f, $"floor contact must not raise angularDamping while the torque path handles it ({rb.angularDamping:F3})");
            }
            finally
            {
                sphere.gameObject.SetActive(false);
            }

            Component mixed = CreateCube(context, new Vector3(8f, 0.276f, -8f), Quaternion.identity, wide: true, sphere: true);
            Rigidbody mixedRb = mixed.GetComponent<Rigidbody>();
            try
            {
                mixedRb.angularVelocity = new Vector3(1f, 2f, 0.5f);
                Vector3 velocity = new(1f, 0f, 0.5f);
                Vector3 damped = InvokeRolling(context, mixed, FixedDelta, velocity);
                AssertTrue(
                    Mathf.Abs(damped.x - (1f * Mathf.Exp(-0.125f * FixedDelta))) < 0.001f
                        && Mathf.Abs(damped.z - (0.5f * Mathf.Exp(-0.125f * FixedDelta))) < 0.001f,
                    $"floor translation damping must stay at the 0.125 rolling rate ({damped})");
                for (int tick = 0; tick < 50; tick++)
                {
                    InvokeRolling(context, mixed, FixedDelta);
                    Physics.Simulate(FixedDelta);
                }

                AssertTrue(
                    Mathf.Abs(mixedRb.angularVelocity.x - (1f * Mathf.Exp(-0.125f))) < 0.05f
                        && Mathf.Abs(mixedRb.angularVelocity.z - (0.5f * Mathf.Exp(-0.125f))) < 0.03f,
                    $"roll axes must keep the 0.125 angular rate ({mixedRb.angularVelocity})");
                AssertTrue(
                    Mathf.Abs(mixedRb.angularVelocity.y - ExpectedTwist(context, 2f, 51)) < 0.04f,
                    $"twist must track the floor-spin brake ({mixedRb.angularVelocity.y:F4})");
            }
            finally
            {
                mixed.gameObject.SetActive(false);
            }

            Component wall = CreateCube(context, new Vector3(99.73f, 1f, 0f), Quaternion.identity, wide: true, sphere: true);
            Rigidbody wallRb = wall.GetComponent<Rigidbody>();
            try
            {
                wallRb.angularVelocity = new Vector3(0f, 2f, 0f);
                InvokeRolling(context, wall, FixedDelta);
                AssertTrue(Mathf.Abs(wallRb.angularDamping - 1f) < 0.0001f, $"wall contact must keep the full wall rolling damping ({wallRb.angularDamping:F3})");
            }
            finally
            {
                wall.gameObject.SetActive(false);
            }

            Component cube = CreateCube(context, new Vector3(8f, 0.276f, -8f), Quaternion.identity, wide: true);
            Rigidbody cubeRb = cube.GetComponent<Rigidbody>();
            try
            {
                cubeRb.angularVelocity = new Vector3(1f, 2f, 0.5f);
                InvokeRolling(context, cube, FixedDelta);
                Physics.Simulate(FixedDelta);
                AssertTrue(
                    Vector3.Distance(cubeRb.angularVelocity, new Vector3(1f, 2f, 0.5f)) < 0.0001f,
                    $"the sphere rolling path must leave cubes alone ({cubeRb.angularVelocity})");
            }
            finally
            {
                cube.gameObject.SetActive(false);
            }
        }

        private static Vector3 InvokeRolling(Context context, Component body, float deltaTime, Vector3 velocity = default)
        {
            object[] args = { body.GetComponent<Rigidbody>().position, velocity, deltaTime };
            context.ApplyRolling.Invoke(body, args);
            return (Vector3)args[1];
        }

        private static void DiagnoseFreeCorner(Context context)
        {
            Quaternion cornerUp = Quaternion.FromToRotation(Vector3.one.normalized, Vector3.up);
            Component body = CreateCube(context, new Vector3(0f, 0.477f, -0.5f), cornerUp);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            try
            {
                rb.angularVelocity = new Vector3(0f, 20f * 2f * Mathf.PI, 0f);
                var samples = new List<float>();
                for (int window = 0; window < 6; window++)
                {
                    Step(context, body, 250);
                    samples.Add(rb.angularVelocity.y);
                }

                NotVerified.Add($"free corner at 20 RPS omegaY(5s windows)={string.Join(",", samples.ConvertAll(v => v.ToString("F1")))} pos={rb.position} rot={rb.rotation.eulerAngles} — batch fixture cannot keep the corner pose long enough to measure the 90s curve; verify in game");
            }
            finally
            {
                body.gameObject.SetActive(false);
            }
        }

        private static Component CreateCube(Context context, Vector3 position, Quaternion rotation, bool wide = false, bool sphere = false)
            => CreateBody(context, position, rotation, context.Material, wide, sphere);

        private static Component CreateBodyWithMaterial(Context context, Vector3 position, Quaternion rotation, PhysicsMaterial material, bool sphere)
            => CreateBody(context, position, rotation, material, wide: true, sphere);

        private static Component CreateBody(Context context, Vector3 position, Quaternion rotation, PhysicsMaterial material, bool wide, bool sphere)
        {
            GameObject body = new($"floor friction {(sphere ? "sphere" : "cube")}");
            context.Resources.Add(body);
            body.transform.localScale = Vector3.one * 0.55f;
            body.transform.position = position;
            body.transform.rotation = rotation;
            Component component = body.AddComponent(context.Grabbable);
            context.Initialize.Invoke(component, new object[] { wide ? context.WideEnclosure : context.Enclosure, sphere, material, context.Ownership });
            Rigidbody rb = body.GetComponent<Rigidbody>();
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            return component;
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

        private static void RunDiagnostic(string name, Action test)
        {
            try
            {
                test();
            }
            catch (Exception exception)
            {
                NotVerified.Add($"{name} threw {exception.GetType().Name}: {exception.Message.Split('\n')[0]}");
            }
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }
    }
}
