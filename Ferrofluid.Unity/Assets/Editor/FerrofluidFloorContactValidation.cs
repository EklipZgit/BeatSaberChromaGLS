using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidFloorContactValidation
    {
        private const float TipSettleTime = 0.1f;
        private const float TipLinearSettleTime = 0.2f;
        private const float TipSettledAngle = 0.25f;
        private const float TipSettledAngularSpeed = 0.15f;
        private const float TipMaximumOvershoot = 1.5f;
        private static readonly List<string> Failures = new();
        private static readonly List<Object> Resources = new();
        private static Context Ctx = null!;

        private sealed class FloorContactObserver : MonoBehaviour
        {
            internal Collider Floor = null!;
            internal bool TouchedFloor;
            internal int ContactCount;
            internal float MinimumSeparation;
            internal float MaximumSeparation;
            internal float MinimumNormalY;

            internal void ResetContacts()
            {
                ContactCount = 0;
                MinimumSeparation = float.PositiveInfinity;
                MaximumSeparation = float.NegativeInfinity;
                MinimumNormalY = 1f;
            }

            private void OnCollisionEnter(Collision collision) => Observe(collision);

            private void OnCollisionStay(Collision collision) => Observe(collision);

            private void Observe(Collision collision)
            {
                if (collision.collider != Floor)
                    return;

                ContactCount = collision.contactCount;
                for (int index = 0; index < collision.contactCount; index++)
                {
                    ContactPoint point = collision.GetContact(index);
                    MinimumSeparation = Mathf.Min(MinimumSeparation, point.separation);
                    MaximumSeparation = Mathf.Max(MaximumSeparation, point.separation);
                    MinimumNormalY = Mathf.Min(MinimumNormalY, point.normal.y);
                    if (point.normal.y > 0.9f)
                        TouchedFloor = true;
                }
            }
        }

        private sealed class Context
        {
            internal Type Grabbable = null!;
            internal MethodInfo Initialize = null!;
            internal MethodInfo Acquire = null!;
            internal MethodInfo FixedUpdate = null!;
            internal MethodInfo SampleGrabTarget = null!;
            internal MethodInfo ConfigureFloorContact = null!;
            internal MethodInfo ConfigureCubeRoundness = null!;
            internal MethodInfo Extent = null!;
            internal FieldInfo GrabShape = null!;
            internal object Enclosure = null!;
            internal object Ownership = null!;
            internal PhysicsMaterial FloorMaterial = null!;
            internal PhysicsMaterial BodyMaterial = null!;
            internal GameObject Floor = null!;
            internal Collider FloorCollider = null!;
        }

        public static void Run()
            => FerrofluidValidationRunner.Run(typeof(FerrofluidFloorContactValidation));

        public static void RunFaceSettling()
        {
            Environment.SetEnvironmentVariable("FERROFLUID_FLOOR_CASES", "face-settling");
            Run();
        }

        public static void RunTipAndSlosh()
        {
            Environment.SetEnvironmentVariable("FERROFLUID_FLOOR_CASES", "tip-and-slosh");
            Run();
        }

        public static void RunBoxCoreGeometry()
        {
            Environment.SetEnvironmentVariable("FERROFLUID_FLOOR_CASES", "box-core");
            Run();
        }

        public static void RunCornerAndHeld()
        {
            Environment.SetEnvironmentVariable("FERROFLUID_FLOOR_CASES", "corner-and-held");
            Run();
        }

        public static void RunTiltedCornerDiagnostics()
        {
            Environment.SetEnvironmentVariable("FERROFLUID_FLOOR_CASES", "tilted-corner");
            Run();
        }

        public static void RunDefaultResponse()
        {
            Environment.SetEnvironmentVariable("FERROFLUID_FLOOR_CASES", "default-response");
            Environment.SetEnvironmentVariable("FERROFLUID_TIP_DIAGNOSTICS", "1");
            Run();
        }

        private static IEnumerator RunRuntime()
        {
            SimulationMode previousMode = Physics.simulationMode;
            float previousDelta = Time.fixedDeltaTime;
            Physics.simulationMode = SimulationMode.Script;
            Time.fixedDeltaTime = 0.02f;
            Failures.Clear();
            Ctx = new Context();
            try
            {
                string repository = Directory.GetParent(Application.dataPath).Parent.FullName;
                Assembly plugin = Assembly.LoadFrom(Path.Combine(repository, "ChromaGLS/bin/Release-1.44.1/net48/ChromaGLS.dll"));
                Debug.Log($"[FerrofluidFloorContactValidation] SUT location={plugin.Location}, module={plugin.ManifestModule.ModuleVersionId}");
                Ctx.Grabbable = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true);
                Ctx.Initialize = Ctx.Grabbable.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.Acquire = Ctx.Grabbable.GetMethod("Acquire", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.FixedUpdate = Ctx.Grabbable.GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.SampleGrabTarget = Ctx.Grabbable.GetMethod("SampleGrabTarget", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.ConfigureFloorContact = Ctx.Grabbable.GetMethod("ConfigureFloorContact", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.ConfigureCubeRoundness = Ctx.Grabbable.GetMethod("ConfigureCubeRoundness", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.GrabShape = Ctx.Grabbable.GetField("_grabShape", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.Extent = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabPhysics", true)
                    .GetMethod("EvaluateSupportExtent", BindingFlags.Static | BindingFlags.NonPublic);
                AssertTrue(Ctx.ConfigureFloorContact != null, "grabbable must own ConfigureFloorContact for the rendered contact pose");
                AssertTrue(Ctx.ConfigureCubeRoundness != null, "grabbable must own ConfigureCubeRoundness for the rounded shell collider");

                Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true);
                Ctx.Enclosure = enclosureType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null,
                    new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                    null)!.Invoke(new object[] { Vector3.zero, 5f, new Vector3(0f, 0f, -10f), Vector3.forward, 0f });
                Ctx.Ownership = Activator.CreateInstance(plugin.GetType("ChromaGLS.Settings.FerrofluidGrabOwnership", true), nonPublic: true);
                Type manager = plugin.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true);
                MethodInfo factory = manager.GetMethod("CreateContactMaterial", BindingFlags.Static | BindingFlags.NonPublic);
                Ctx.FloorMaterial = (PhysicsMaterial)factory!.Invoke(null, new object[] { true });
                Ctx.BodyMaterial = (PhysicsMaterial)factory.Invoke(null, new object[] { false });
                Resources.Add(Ctx.FloorMaterial);
                Resources.Add(Ctx.BodyMaterial);

                Ctx.Floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Resources.Add(Ctx.Floor);
                Ctx.Floor.name = "contact validation floor";
                Ctx.Floor.layer = 3;
                Ctx.Floor.transform.localScale = new Vector3(40f, 1f, 40f);
                Ctx.Floor.transform.position = new Vector3(0f, -0.5f, 0f);
                Ctx.FloorCollider = Ctx.Floor.GetComponent<Collider>();
                Ctx.FloorCollider.material = Ctx.FloorMaterial;

                string cases = Environment.GetEnvironmentVariable("FERROFLUID_FLOOR_CASES") ?? string.Empty;
                if (cases == "default-response")
                {
                    IEnumerator fall = CaseDefaultFreeFall();
                    while (fall.MoveNext())
                        yield return fall.Current;

                    CaseFastApproach();
                    IEnumerator tip = CaseTipAndSlosh(plugin);
                    while (tip.MoveNext())
                        yield return tip.Current;
                }
                else if (cases == "box-core")
                {
                    CheckBoxCoreGeometry(plugin);
                }
                else if (cases == "tip-and-slosh")
                {
                    IEnumerator tip = CaseTipAndSlosh(plugin);
                    while (tip.MoveNext())
                        yield return tip.Current;
                }
                else if (cases == "corner-and-held")
                {
                    IEnumerator balanced = CaseCornerTop();
                    while (balanced.MoveNext())
                        yield return balanced.Current;

                    foreach (float tilt in new[] { 20f, 30f })
                    {
                        IEnumerator top = CaseTiltedCornerTop(tilt);
                        while (top.MoveNext())
                            yield return top.Current;
                    }

                    IEnumerator held = CaseHeldImmunity();
                    while (held.MoveNext())
                        yield return held.Current;

                    IEnumerator reconfigure = CaseHeldReconfigure();
                    while (reconfigure.MoveNext())
                        yield return reconfigure.Current;
                }
                else if (cases == "tilted-corner")
                {
                    foreach (float tilt in new[] { 20f, 30f })
                    {
                        IEnumerator top = CaseTiltedCornerTop(tilt);
                        while (top.MoveNext())
                            yield return top.Current;
                    }
                }
                else
                {
                    if (cases != "face-settling")
                    {
                        IEnumerator impacts = CaseImpacts();
                        while (impacts.MoveNext())
                            yield return impacts.Current;
                        IEnumerator held = CaseHeldImmunity();
                        while (held.MoveNext())
                            yield return held.Current;
                        IEnumerator corner = CaseCornerTop();
                        while (corner.MoveNext())
                            yield return corner.Current;
                        IEnumerator geometry = CaseRoundnessGeometry(plugin);
                        while (geometry.MoveNext())
                            yield return geometry.Current;
                        IEnumerator reconfigure = CaseHeldReconfigure();
                        while (reconfigure.MoveNext())
                            yield return reconfigure.Current;
                    }

                    IEnumerator faces = CaseFaceSettling();
                    while (faces.MoveNext())
                        yield return faces.Current;
                    CaseBroadFaceRetreat();
                }

                foreach (string failure in Failures)
                    Debug.LogWarning($"[FerrofluidFloorContactValidation] FAIL: {failure}");

                if (Failures.Count > 0)
                    throw new Exception($"{Failures.Count} contact cases failed: {string.Join(" | ", Failures)}");

                Debug.Log("[FerrofluidFloorContactValidation] PASS: requested floor-contact cases.");
            }
            finally
            {
                foreach (Object resource in Resources)
                {
                    if (resource != null)
                        Object.Destroy(resource);
                }

                Physics.simulationMode = previousMode;
                Time.fixedDeltaTime = previousDelta;
            }
        }

        private static IEnumerator CaseFaceSettling()
        {
            float dt = Time.fixedDeltaTime;
            foreach (Vector3 face in new[] { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                Quaternion rotation = Quaternion.FromToRotation(face, Vector3.up);
                Component cube = MakeFloorFaceBody("face settle", false, rotation, 0.48f);
                Rigidbody rb = cube.GetComponent<Rigidbody>();
                FloorContactObserver contact = cube.GetComponent<FloorContactObserver>();
                rb.angularVelocity = new Vector3(0f, 0.8f, 0f);
                rb.linearVelocity = new Vector3(0.15f, -0.15f, 0f);
                int firstContact = -1;
                bool checkedSettling = false;
                for (int tick = 0; tick < 25; tick++)
                {
                    Ctx.FixedUpdate.Invoke(cube, null);
                    Physics.Simulate(dt);
                    if (firstContact < 0 && contact.TouchedFloor)
                        firstContact = tick;
                    if (firstContact >= 0 && tick - firstContact == 10)
                    {
                        checkedSettling = true;
                        Debug.Log($"[FerrofluidFloorContactValidation] face={face} firstContact={firstContact} age={10f * dt:F2} w={rb.angularVelocity.magnitude:F4} v={rb.linearVelocity.magnitude:F4}");
                        AssertTrue(rb.angularVelocity.magnitude < 0.05f && rb.linearVelocity.magnitude < 0.03f,
                            $"{face} face must settle within .20s of contact (w={rb.angularVelocity.magnitude:F3} v={rb.linearVelocity.magnitude:F3})");
                    }
                    yield return null;
                    yield return null;
                }

                AssertTrue(firstContact >= 0 && checkedSettling, $"{face} face case must contact the floor and reach the settling check");
                cube.gameObject.SetActive(false);
            }

            Component rocking = MakeFloorFaceBody("face rocking", false, Quaternion.Euler(0f, 0f, 0.5f), 0.48f);
            Rigidbody rockRb = rocking.GetComponent<Rigidbody>();
            FloorContactObserver rockObserver = rocking.GetComponent<FloorContactObserver>();
            rockRb.angularVelocity = new Vector3(0f, 0f, 0.75f);
            rockRb.linearVelocity = new Vector3(0.1f, -0.15f, 0f);
            float rockMax = 0f;
            bool rockContact = false;
            for (int tick = 0; tick < 25; tick++)
            {
                Ctx.FixedUpdate.Invoke(rocking, null);
                Physics.Simulate(dt);
                rockContact |= rockObserver.TouchedFloor;
                if (tick > 12)
                    rockMax = Mathf.Max(rockMax, rockRb.angularVelocity.magnitude);
                yield return null;
            }

            AssertTrue(rockContact, "the rocking case must make real floor contact");
            Debug.Log($"[FerrofluidFloorContactValidation] rocking finalW={rockRb.angularVelocity.magnitude:F4} lateMax={rockMax:F4}");
            AssertTrue(rockRb.angularVelocity.magnitude < 0.05f, $"rocking must die within .5s (w={rockRb.angularVelocity.magnitude:F3})");
            AssertTrue(rockMax < 0.1f, $"rocking must not see-saw again (post=.1 max {rockMax:F3})");
            rocking.gameObject.SetActive(false);

            Component air = MakeBody("face air", false, new Vector3(0f, 1f, 0f), Quaternion.identity, 0.48f);
            Rigidbody airRb = air.GetComponent<Rigidbody>();
            airRb.angularVelocity = new Vector3(0f, 0.8f, 0f);
            for (int tick = 0; tick < 10; tick++)
            {
                Ctx.FixedUpdate.Invoke(air, null);
                Physics.Simulate(dt);
                yield return null;
            }

            AssertTrue(Mathf.Abs(airRb.angularVelocity.y) > 0.7f, $"airborne face cube must keep its spin (w={airRb.angularVelocity.y:F3})");
            air.gameObject.SetActive(false);

            Component fast = MakeFloorFaceBody("face fast", false, Quaternion.identity, 0.48f);
            Rigidbody fastRb = fast.GetComponent<Rigidbody>();
            fastRb.linearVelocity = new Vector3(2f, 0f, 0f);
            for (int tick = 0; tick < 5; tick++)
            {
                Ctx.FixedUpdate.Invoke(fast, null);
                Physics.Simulate(dt);
                yield return null;
            }

            AssertTrue(Mathf.Abs(fastRb.linearVelocity.x) > 1.2f, $"a fast slide must not be absorbed (vx={fastRb.linearVelocity.x:F3})");
            fast.gameObject.SetActive(false);

            Component sphere = MakeFloorFaceBody("face sphere", true, Quaternion.identity, null);
            Rigidbody sphereRb = sphere.GetComponent<Rigidbody>();
            sphereRb.angularVelocity = new Vector3(0f, 0.8f, 0f);
            for (int tick = 0; tick < 10; tick++)
            {
                Ctx.FixedUpdate.Invoke(sphere, null);
                Physics.Simulate(dt);
                yield return null;
            }

            AssertTrue(Mathf.Abs(sphereRb.angularVelocity.y) > 0.4f, $"a sphere must keep most twist (w={sphereRb.angularVelocity.y:F3})");
            sphere.gameObject.SetActive(false);

            Component held = MakeFloorFaceBody("face held", false, Quaternion.identity, 0.48f);
            Rigidbody heldRb = held.GetComponent<Rigidbody>();
            heldRb.angularVelocity = new Vector3(0f, 0.8f, 0f);
            Ctx.Acquire.Invoke(held, new object[] { held.transform, held.transform.position });
            for (int tick = 0; tick < 10; tick++)
            {
                Ctx.FixedUpdate.Invoke(held, null);
                Physics.Simulate(dt);
                yield return null;
            }

            AssertTrue(Mathf.Abs(heldRb.angularVelocity.y) > 0.4f, $"a held cube keeps its twist (w={heldRb.angularVelocity.y:F3})");
            held.gameObject.SetActive(false);

            Component impact = MakeFloorFaceBody("face fast impact", false, Quaternion.identity, 0.48f);
            Rigidbody impactRb = impact.GetComponent<Rigidbody>();
            impactRb.linearVelocity = Vector3.down * 5f;
            float rebound = 0f;
            for (int tick = 0; tick < 10; tick++)
            {
                Ctx.FixedUpdate.Invoke(impact, null);
                Physics.Simulate(dt);
                rebound = Mathf.Max(rebound, impactRb.linearVelocity.y);
                yield return null;
            }

            AssertTrue(impact.GetComponent<FloorContactObserver>().TouchedFloor, "fast impact must touch the floor");
            AssertTrue(rebound > 1f, $"fast impact must retain its bounce (rebound={rebound:F3})");
            impact.gameObject.SetActive(false);

            Component driven = MakeFloorFaceBody("face driven spin", false, Quaternion.identity, 0.48f);
            Rigidbody drivenRb = driven.GetComponent<Rigidbody>();
            drivenRb.angularVelocity = Vector3.up * 0.8f;
            Ctx.Grabbable.GetMethod("SetControllerInput", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(driven, new object[] { Vector2.zero, new Vector2(-0.5f, 0f), Quaternion.identity, Vector3.right });
            for (int tick = 0; tick < 10; tick++)
            {
                Ctx.FixedUpdate.Invoke(driven, null);
                Physics.Simulate(dt);
                yield return null;
            }

            AssertTrue(drivenRb.angularVelocity.y > 0.8f, $"controller input must still spin up a face-down cube (w={drivenRb.angularVelocity.y:F3})");
            driven.gameObject.SetActive(false);

            foreach (float speed in new[] { 0.8f, 20f * 2f * Mathf.PI })
            {
                Quaternion cornerUp = Quaternion.FromToRotation(Vector3.one.normalized, Vector3.up);
                Component top = MakeFloorFaceBody("face exclusion corner top", false, cornerUp, 0.48f);
                Rigidbody topRb = top.GetComponent<Rigidbody>();
                topRb.angularVelocity = Vector3.up * speed;
                for (int tick = 0; tick < 5; tick++)
                {
                    Ctx.FixedUpdate.Invoke(top, null);
                    Physics.Simulate(dt);
                    yield return null;
                }

                AssertTrue(topRb.angularVelocity.magnitude > speed * 0.5f,
                    $"corner spin must not receive face settling (start={speed:F3} final={topRb.angularVelocity.magnitude:F3})");
                top.gameObject.SetActive(false);
            }
        }

        private static void CaseBroadFaceRetreat()
        {
            Quaternion rotation = Quaternion.Euler(0f, 0f, 5f);
            Component baseline = MakeFloorFaceBody("retreat without damping", false, rotation, 0.48f);
            Component damped = MakeFloorFaceBody("retreat with damping", false, rotation, 0.48f);
            Rigidbody baselineBody = baseline.GetComponent<Rigidbody>();
            Rigidbody dampedBody = damped.GetComponent<Rigidbody>();
            Type configType = Ctx.Grabbable.Assembly.GetType("ChromaGLS.Settings.Config", true);
            object config = Activator.CreateInstance(configType, nonPublic: true);
            MethodInfo configure = Ctx.Grabbable.GetMethod("ConfigureContactPhysics", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? Ctx.Grabbable.GetMethod("ConfigureCubeFloorPhysics", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? Ctx.Grabbable.GetMethod("ConfigureCubeFaceDamping", BindingFlags.Instance | BindingFlags.NonPublic);
            configure.Invoke(damped, new[] { config });
            configType.GetProperty("FerrofluidCubeFaceDampingStrength").SetValue(config, 0f);
            configure.Invoke(baseline, new[] { config });

            try
            {
                baselineBody.position += Vector3.left;
                dampedBody.position += Vector3.right;
                foreach (Rigidbody body in new[] { baselineBody, dampedBody })
                {
                    body.angularVelocity = Vector3.forward * 2f;
                    body.linearVelocity = new Vector3(0f, -0.15f, 0f);
                }

                Physics.SyncTransforms();
                Ctx.FixedUpdate.Invoke(baseline, null);
                Ctx.FixedUpdate.Invoke(damped, null);
                Physics.Simulate(Time.fixedDeltaTime);

                float baselineAngle = FaceAngle(baselineBody.rotation);
                float dampedAngle = FaceAngle(dampedBody.rotation);
                Debug.Log($"[FerrofluidFloorContactValidation] broad retreat angles={baselineAngle:F4}/{dampedAngle:F4} omegaZ={baselineBody.angularVelocity.z:F4}/{dampedBody.angularVelocity.z:F4}");
                AssertTrue(
                    baseline.GetComponent<FloorContactObserver>().TouchedFloor && damped.GetComponent<FloorContactObserver>().TouchedFloor,
                    "both broad-retreat bodies must make real floor contact");
                AssertTrue(
                    baselineAngle > 3f && baselineAngle < 10f && dampedAngle > 3f && dampedAngle < 10f,
                    "broad retreat must exercise the newly covered 3-to-10 degree band");
                AssertTrue(baselineBody.angularVelocity.z > 0.1f, "the baseline must still be retreating after contact");
                AssertTrue(
                    dampedBody.angularVelocity.z >= 0f && dampedBody.angularVelocity.z < baselineBody.angularVelocity.z - 0.01f,
                    "retreat damping must reduce angular motion through the real contact callback");
            }
            finally
            {
                baseline.gameObject.SetActive(false);
                damped.gameObject.SetActive(false);
            }
        }

        private static void CheckBoxCoreGeometry(Assembly plugin)
        {
            MethodInfo surfacePoint = plugin.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true)!
                .GetMethod("EvaluateSurfacePositionFromDirection", BindingFlags.Static | BindingFlags.NonPublic)!;
            object roundedCube = Enum.Parse(plugin.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", true)!, "RoundedCube");
            foreach (float roundness in new[] { 0f, 0.48f, 1f })
            {
                Component cube = MakeFloorFaceBody($"box-core-{roundness}", false, Quaternion.identity, roundness);
                try
                {
                    int count = 0;
                    BoxCollider box = null;
                    foreach (Collider collider in cube.GetComponentsInChildren<Collider>())
                    {
                        if (!collider.enabled)
                            continue;

                        count++;
                        box = collider as BoxCollider;
                    }

                    AssertTrue(count == 1, $"roundness {roundness}: cube must have one active collision core, got {count}");
                    AssertTrue(box != null, $"roundness {roundness}: active production core must be a BoxCollider");
                    if (box == null)
                        continue;

                    // The 5% core intentionally lets sharp corners cross the rounded base, so only
                    // the six face centres must stay inside the analytic liquid hull, and the box
                    // must be exactly the fixed half-extents times two.
                    Vector3 expectedSize = Vector3.one * (roundness == 0f ? 0.9106934709152459f : roundness < 0.5f ? 0.842994293495772f : 0.6031866937358614f);
                    AssertTrue((box.size - expectedSize).magnitude < 0.000001f, $"roundness {roundness}: box size {box.size} must equal the fixed core size {expectedSize}");
                    foreach (Vector3 direction in new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
                    {
                        Vector3 faceWorld = box.transform.TransformPoint(box.center + direction * (box.size.x * 0.5f));
                        Vector3 faceLocal = cube.transform.InverseTransformPoint(faceWorld);
                        Vector3 faceSurface = (Vector3)surfacePoint.Invoke(null, new[] { roundedCube, faceLocal.normalized, (object)roundness })!;
                        AssertTrue(faceSurface.magnitude - faceLocal.magnitude > 0.0001f, $"roundness {roundness}: box face {direction} at {faceLocal} must stay inside the liquid");
                    }

                    Rigidbody body = cube.GetComponent<Rigidbody>();
                    Debug.Log($"[FerrofluidFloorContactValidation] box geometry roundness={roundness}, physicsPosition={body.position}, displayPosition={cube.transform.position}");
                    Vector3[] offsets = new Vector3[8];
                    for (int index = 0; index < offsets.Length; index++)
                    {
                        Vector3 sign = new(
                            (index & 1) == 0 ? -1f : 1f,
                            (index & 2) == 0 ? -1f : 1f,
                            (index & 4) == 0 ? -1f : 1f);
                        Vector3 worldPoint = box.transform.TransformPoint(box.center + Vector3.Scale(box.size * 0.5f, sign));
                        Vector3 localPoint = cube.transform.InverseTransformPoint(worldPoint);
                        // An interpolated display pose can still lag behind the newly positioned body.
                        offsets[index] = body.rotation * Vector3.Scale(localPoint, cube.transform.lossyScale);
                    }

                    foreach (Vector3 direction in new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
                    {
                        float physicalExtent = float.MinValue;
                        foreach (Vector3 offset in offsets)
                            physicalExtent = Mathf.Max(physicalExtent, Vector3.Dot(offset, direction));

                        float support = (float)Ctx.Extent.Invoke(null, new[] { ActualShape(cube), Quaternion.identity, (object)direction })!;
                        AssertTrue(Mathf.Abs(support - physicalExtent) < 0.00001f, $"roundness {roundness}: {direction} support must match the actual box core");
                    }
                }
                finally
                {
                    cube.gameObject.SetActive(false);
                }
            }
        }

        private static IEnumerator CaseTipAndSlosh(Assembly plugin)
        {
            string repository = Directory.GetParent(Application.dataPath)!.Parent!.FullName;
            string directory = Path.GetFullPath(Path.Combine(
                repository,
                "..",
                "Scratch",
                "ferro-flat-core-slosh",
                DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture)));
            Directory.CreateDirectory(directory);
            Debug.Log($"[FerrofluidFloorContactValidation] tip artifacts={directory}");
            CheckBoxCoreGeometry(plugin);
            foreach (Vector3 face in new[] { Vector3.down, Vector3.up, Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
            {
                if (Environment.GetEnvironmentVariable("FERROFLUID_TIP_DIAGNOSTICS") == "1" && face != Vector3.down)
                    continue;

                Quaternion basis = Quaternion.FromToRotation(face, Vector3.down);
                foreach (float yaw in new[] { 0f, 37f })
                {
                    foreach (float tip in new[] { 46f, -46f })
                    {
                        string label = $"face-{face.x}-{face.y}-{face.z}-yaw{yaw}-tip{tip}";
                        IEnumerator run = TraceTip(plugin, basis, yaw, tip, null, directory, label);
                        while (run.MoveNext())
                            yield return run.Current;
                    }
                }
            }

            foreach (bool spikes in new[] { true, false })
            {
                IEnumerator run = TraceTip(plugin, Quaternion.identity, 0f, 46f, spikes, directory, $"coupled-spikes-{spikes}");
                while (run.MoveNext())
                    yield return run.Current;
            }
        }

        private static IEnumerator TraceTip(
            Assembly plugin,
            Quaternion basis,
            float yawDegrees,
            float tip,
            bool? spikes,
            string directory,
            string label)
        {
            Quaternion yaw = Quaternion.AngleAxis(yawDegrees, Vector3.up);
            Quaternion rotation = yaw * Quaternion.AngleAxis(tip, Vector3.forward) * basis;
            Quaternion target = yaw * Quaternion.AngleAxis(Mathf.Sign(tip) * 90f, Vector3.forward) * basis;
            Vector3 landingFace = Quaternion.Inverse(target) * Vector3.down;
            LandingFluidFixture fluid = null;
            Component cube = null;
            int failuresBefore = Failures.Count;
            StringBuilder csv = new("step,time,roll,error,preOmega,postOmega,stepAngle,allowedAngle,displayStepAngle,qx,qy,qz,qw,displayX,displayY,displayZ,displayW,vx,vy,vz,gap,contacts,fluidTravel,fluidMotion,surfaceMotion,fixedUpdateAngle,nativeAngle,preSimOmega,preSimVy,minSeparation,maxSeparation,minNormalY,sleepBefore,sleepAfterUpdate,sleepAfterSim,modeBeforeUpdate,modeBeforeSim,modeAfterSim\n");
            try
            {
                if (spikes.HasValue)
                {
                    fluid = new LandingFluidFixture(plugin, rotation, target, spikes.Value);
                    cube = fluid.Cube;
                }
                else
                {
                    cube = MakeFloorFaceBody(label, false, rotation, 0.48f);
                }

                Rigidbody rb = cube.GetComponent<Rigidbody>();
                BoxCollider collider = cube.GetComponentInChildren<BoxCollider>();
                Debug.Log($"[FerrofluidFloorContactValidation] {label} setup: type={cube.GetType().Assembly.Location}, collider={collider.GetType().Name}, mass={rb.mass:F5}, inertia={rb.inertiaTensor:F6}");
                FloorContactObserver observer = cube.GetComponent<FloorContactObserver>();
                object shape = ActualShape(cube);
                Quaternion previousDisplay = cube.transform.rotation;
                float previousOmega = 0f;
                float dt = Time.fixedDeltaTime;
                int settleSteps = Mathf.CeilToInt(TipSettleTime / dt);
                int linearSettleSteps = Mathf.CeilToInt(TipLinearSettleTime / dt);
                int arrival = -1;
                int angularRest = -1;
                int fullRest = -1;
                bool angularDeadlineMet = false;
                bool linearDeadlineMet = false;
                float tailOmega = 0f;
                float tailError = 0f;
                float tailSpeed = 0f;
                float overshoot = 0f;
                float worstJump = 0f;
                float worstDisplayJump = 0f;
                float minimumFloorNormalY = 1f;
                for (int step = 0; step < 150; step++)
                {
                    Quaternion before = rb.rotation;
                    float preOmega = rb.angularVelocity.magnitude;
                    CollisionDetectionMode modeBeforeUpdate = rb.collisionDetectionMode;
                    bool sleepBefore = rb.IsSleeping();
                    observer.ResetContacts();
                    Ctx.FixedUpdate.Invoke(cube, null);
                    Quaternion beforeSimulation = rb.rotation;
                    float preSimulationOmega = rb.angularVelocity.magnitude;
                    float preSimulationVy = rb.linearVelocity.y;
                    bool sleepAfterUpdate = rb.IsSleeping();
                    CollisionDetectionMode modeBeforeSimulation = rb.collisionDetectionMode;
                    Physics.Simulate(dt);
                    Quaternion physical = rb.rotation;
                    float postOmega = rb.angularVelocity.magnitude;
                    float error = Vector3.Angle(physical * landingFace, Vector3.down);
                    Quaternion relative = Quaternion.Inverse(yaw) * physical * Quaternion.Inverse(basis);
                    float roll = Mathf.DeltaAngle(0f, relative.eulerAngles.z);
                    overshoot = Mathf.Max(overshoot, Mathf.Sign(tip) * (roll - (Mathf.Sign(tip) * 90f)));
                    if (arrival < 0 && error <= 1f && observer.TouchedFloor)
                        arrival = step;

                    bool angularQuiet = error <= TipSettledAngle && postOmega <= TipSettledAngularSpeed;
                    bool fullyQuiet = error <= 0.1f && postOmega <= 0.05f && rb.linearVelocity.sqrMagnitude <= 0.0025f;
                    if (arrival >= 0 && angularRest < 0 && angularQuiet)
                        angularRest = step;

                    if (arrival >= 0 && fullRest < 0 && fullyQuiet)
                        fullRest = step;

                    if (arrival >= 0 && step == arrival + settleSteps)
                        angularDeadlineMet = angularQuiet;

                    if (arrival >= 0 && step == arrival + linearSettleSteps)
                        linearDeadlineMet = rb.linearVelocity.sqrMagnitude <= 0.0025f;

                    if (arrival >= 0 && step >= arrival + settleSteps)
                    {
                        tailOmega = Mathf.Max(tailOmega, postOmega);
                        tailError = Mathf.Max(tailError, error);
                    }

                    if (arrival >= 0 && step >= arrival + linearSettleSteps)
                        tailSpeed = Mathf.Max(tailSpeed, rb.linearVelocity.magnitude);

                    minimumFloorNormalY = Mathf.Min(minimumFloorNormalY, observer.MinimumNormalY);
                    float stepAngle = Quaternion.Angle(before, physical);
                    float allowedAngle = Mathf.Max(preOmega, postOmega) * dt * Mathf.Rad2Deg + 0.25f;
                    worstJump = Mathf.Max(worstJump, stepAngle - allowedAngle);
                    if (fluid != null)
                        fluid.Advance(step, arrival >= 0 && fullyQuiet, dt);

                    // Sample the displayed pose after Unity has had a presentation frame, not a
                    // nearest-face angle that cannot distinguish the original face from the next.
                    yield return null;
                    Quaternion display = cube.transform.rotation;
                    float displayStep = Quaternion.Angle(previousDisplay, display);
                    float displayAllowed = Mathf.Max(previousOmega, Mathf.Max(preOmega, postOmega)) * dt * Mathf.Rad2Deg + 0.25f;
                    worstDisplayJump = Mathf.Max(worstDisplayJump, displayStep - displayAllowed);
                    previousDisplay = display;
                    previousOmega = Mathf.Max(preOmega, postOmega);
                    Vector3 velocity = rb.linearVelocity;
                    float gap = rb.position.y - Support(shape, physical);
                    csv.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "{0},{1:F4},{2:F5},{3:F5},{4:F5},{5:F5},{6:F5},{7:F5},{8:F5},{9:F7},{10:F7},{11:F7},{12:F7},{13:F7},{14:F7},{15:F7},{16:F7},{17:F5},{18:F5},{19:F5},{20:F6},{21},{22:F6},{23:F6},{24:F6}",
                        step,
                        (step + 1) * dt,
                        roll,
                        error,
                        preOmega,
                        postOmega,
                        stepAngle,
                        allowedAngle,
                        displayStep,
                        physical.x,
                        physical.y,
                        physical.z,
                        physical.w,
                        display.x,
                        display.y,
                        display.z,
                        display.w,
                        velocity.x,
                        velocity.y,
                        velocity.z,
                        gap,
                        observer.ContactCount,
                        fluid == null ? 0f : fluid.MaximumTravel,
                        fluid == null ? 0f : fluid.PostStopMotion,
                        fluid == null ? 0f : fluid.PostStopSurfaceMotion);
                    csv.AppendFormat(
                        CultureInfo.InvariantCulture,
                        ",{0:F6},{1:F6},{2:F6},{3:F6},{4:F6},{5:F6},{6:F6},{7},{8},{9},{10},{11},{12}\n",
                        Quaternion.Angle(before, beforeSimulation),
                        Quaternion.Angle(beforeSimulation, physical),
                        preSimulationOmega,
                        preSimulationVy,
                        observer.MinimumSeparation,
                        observer.MaximumSeparation,
                        observer.MinimumNormalY,
                        sleepBefore,
                        sleepAfterUpdate,
                        rb.IsSleeping(),
                        modeBeforeUpdate,
                        modeBeforeSimulation,
                        rb.collisionDetectionMode);
                }

                File.WriteAllText(Path.Combine(directory, label + ".csv"), csv.ToString());
                float angularDelay = angularRest < 0 ? -1f : (angularRest - arrival) * dt;
                float fullDelay = fullRest < 0 ? -1f : (fullRest - arrival) * dt;
                Debug.Log($"[FerrofluidFloorContactValidation] {label}: arrival={arrival}, angularDeadline={angularDeadlineMet}, linearDeadline={linearDeadlineMet}, firstAngularRestDelay={angularDelay:F4}, firstFullRestDelay={fullDelay:F4}, tailError={tailError:F4}, tailOmega={tailOmega:F4}, tailSpeed={tailSpeed:F4}, overshoot={overshoot:F4}, jumpExcess={worstJump:F4}, displayJumpExcess={worstDisplayJump:F4}, minFloorNormalY={minimumFloorNormalY:F6}");
                AssertTrue(arrival >= 0, $"{label} must reach the NEXT face, not return to the starting face");
                AssertTrue(angularDeadlineMet, $"{label} must settle angularly within {TipSettleTime:F2}s of face arrival");
                AssertTrue(linearDeadlineMet, $"{label} must stop sliding within {TipLinearSettleTime:F2}s of face arrival");
                AssertTrue(arrival >= 0 && arrival + settleSteps + 25 < 150, $"{label} must leave at least .5s to check settled motion");
                AssertTrue(tailError <= TipSettledAngle && tailOmega <= TipSettledAngularSpeed, $"{label} may only rock slightly after settling: error={tailError:F4}, omega={tailOmega:F4}");
                AssertTrue(tailSpeed <= 0.05f, $"{label} must stop translating too, not just stop rotating ({tailSpeed:F4}m/s)");
                AssertTrue(overshoot <= TipMaximumOvershoot, $"{label} must keep landing overshoot within {TipMaximumOvershoot:F2}deg ({overshoot:F4})");
                AssertTrue(worstJump <= 0f && worstDisplayJump <= 0f, $"{label} must not jump physical/displayed angles ({worstJump:F4}/{worstDisplayJump:F4}deg beyond velocity bound)");
                if (fluid != null)
                    fluid.Check(label, directory);
                if (Failures.Count == failuresBefore)
                    Debug.Log($"[FerrofluidFloorContactValidation] CASE {label} PASS");
            }
            finally
            {
                if (cube != null)
                    cube.gameObject.SetActive(false);
                if (fluid != null)
                    fluid.Dispose();
            }
        }

        private sealed class LandingFluidFixture : IDisposable
        {
            private readonly FerrofluidValidationRuntime _fluid;
            private readonly FerrofluidValidationRuntime _stationary;
            private readonly Vector3[] _homes;
            private readonly Vector3[] _normals;
            private readonly bool[] _upperFace;
            private readonly Vector2[] _surfaceUvs;
            private readonly int[] _trackedSites = new int[25];
            private readonly MethodInfo _encodeUv;
            private readonly float _chartYaw;
            private readonly bool _trackTips;
            private readonly Vector3 _tipDirection;
            private readonly StringBuilder _tipTrace = new("step,afterStop,sample,site,siteTangent,tipTangent\n");
            private float _tipAtStop;
            private float _tipForwardTravel;
            private float _tipPeakDeparture;
            private float _tipLateDeparture;
            private float _tipSignedPeak = float.NegativeInfinity;
            private bool _tipReversed;
            private readonly StringBuilder _snapshots = new("step,site,x,y,z,controlX,controlY,controlZ,homeX,homeY,homeZ\n");
            private readonly StringBuilder _surfaceSnapshots = new("step,sample,x,y,z,controlX,controlY,controlZ\n");
            private Vector3[] _stopSites;
            private Vector3[] _stopControlSites;
            private Vector3[] _stopSurface;
            private Vector3[] _stopControlSurface;
            private int _stopStep = -1;
            private bool _bounded = true;

            internal LandingFluidFixture(Assembly plugin, Quaternion initial, Quaternion target, bool spikes)
            {
                object config = FerrofluidValidationRuntime.CreateConfig();
                _fluid = new FerrofluidValidationRuntime("RoundedCube", config);
                _stationary = new FerrofluidValidationRuntime("RoundedCube", config);
                Material simulationMaterial = (Material)_fluid.Get("_simulationMaterial");
                Debug.Log($"[FerrofluidFloorContactValidation] default fluid elasticity={simulationMaterial.GetFloat("_Elasticity"):F4} damping={simulationMaterial.GetFloat("_Damping"):F4} homeElasticity={simulationMaterial.GetFloat("_PrimitiveHomeElasticityGain"):F4} homeDamping={simulationMaterial.GetFloat("_PrimitiveHomeDampingGain"):F4}");
                Transform root = ((Component)_fluid.Get("_renderer")).transform;
                Transform stationaryRoot = ((Component)_stationary.Get("_renderer")).transform;
                root.localScale = stationaryRoot.localScale = Vector3.one * 0.55f;
                Type environment = plugin.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true)!;
                float sourceWorldScale = (float)environment.GetMethod("EvaluateSourceWorldScale", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, new object[] { root });
                root.rotation = initial;
                stationaryRoot.rotation = target;
                ((Renderer)_stationary.Get("_renderer")).enabled = false;
                Cube = root.gameObject.AddComponent(Ctx.Grabbable);
                Ctx.Initialize.Invoke(Cube, new object[] { Ctx.Enclosure, false, Ctx.BodyMaterial, Ctx.Ownership });
                Ctx.ConfigureFloorContact.Invoke(Cube, new object[] { Ctx.FloorCollider });
                Ctx.ConfigureCubeRoundness.Invoke(Cube, new object[] { 0.48f });
                ((MonoBehaviour)Cube).enabled = false;
                Rigidbody rb = Cube.GetComponent<Rigidbody>();
                rb.position = Vector3.up * (Support(ActualShape(Cube), initial) + 0.002f);
                FloorContactObserver observer = root.gameObject.AddComponent<FloorContactObserver>();
                observer.Floor = Ctx.FloorCollider;
                Physics.SyncTransforms();
                foreach (FerrofluidValidationRuntime simulation in new[] { _fluid, _stationary })
                {
                    if (!spikes)
                        simulation.Invoke("DisableSpikes", config);
                    simulation.Invoke("ApplyWorldScaleParameters", sourceWorldScale);
                    for (int tick = 0; tick < 30; tick++)
                    {
                        simulation.Invoke("SetPrimitiveMotion", Vector3.zero, Vector3.zero);
                        simulation.Invoke("AdvancePrimitiveSimulation", 0.02f);
                    }

                    simulation.Invoke("RenderPrimitiveDeformation");
                }

                _homes = _fluid.ReadHomes();
                _normals = new Vector3[_homes.Length];
                _upperFace = new bool[_homes.Length];
                for (int index = 0; index < _homes.Length; index++)
                {
                    Vector3 p = _homes[index];
                    _normals[index] = new Vector3(
                        Mathf.Sign(p.x) * Mathf.Pow(Mathf.Abs(p.x), 4.12f),
                        Mathf.Sign(p.y) * Mathf.Pow(Mathf.Abs(p.y), 4.12f),
                        Mathf.Sign(p.z) * Mathf.Pow(Mathf.Abs(p.z), 4.12f)).normalized;
                    _upperFace[index] = Vector3.Dot(target * _normals[index], Vector3.up) > 0.9f;
                }

                MethodInfo encode = environment.GetMethod("EvaluateSurfaceUv", BindingFlags.Static | BindingFlags.NonPublic)!;
                float chartYaw = (float)_fluid.Get("_homeChartYaw");
                _encodeUv = encode;
                _chartYaw = chartYaw;
                _trackTips = spikes;
                (target * Quaternion.Inverse(initial)).ToAngleAxis(out _, out Vector3 spinAxis);
                _tipDirection = Quaternion.Inverse(target) * Vector3.Cross(spinAxis, Vector3.up).normalized;
                Vector3 upperNormal = Quaternion.Inverse(target) * Vector3.up;
                Vector3 tangent = Vector3.Cross(upperNormal, Vector3.forward).normalized;
                Vector3 bitangent = Vector3.Cross(upperNormal, tangent);
                _surfaceUvs = new Vector2[25];
                for (int y = 0; y < 5; y++)
                {
                    for (int x = 0; x < 5; x++)
                    {
                        Vector3 direction = (upperNormal * 0.5f) + (tangent * ((x - 2) * 0.15f)) + (bitangent * ((y - 2) * 0.15f));
                        int sample = (y * 5) + x;
                        _surfaceUvs[sample] = (Vector2)encode.Invoke(null, new object[] { direction, chartYaw })!;
                        float alignment = float.NegativeInfinity;
                        for (int site = 0; site < _homes.Length; site++)
                        {
                            float candidate = Vector3.Dot(direction.normalized, _homes[site].normalized);
                            if (candidate > alignment)
                            {
                                alignment = candidate;
                                _trackedSites[sample] = site;
                            }
                        }
                    }
                }
            }

            internal Component Cube { get; }

            internal float MaximumTravel { get; private set; }

            internal float PostStopMotion { get; private set; }

            internal float PostStopSurfaceMotion { get; private set; }

            internal float PostStopMeanSurfaceMotion { get; private set; }

            internal void Advance(int step, bool atRest, float dt)
            {
                Rigidbody rb = Cube.GetComponent<Rigidbody>();
                _fluid.Invoke("SetPrimitiveMotion", rb.GetPointVelocity(rb.position), rb.angularVelocity);
                _stationary.Invoke("SetPrimitiveMotion", Vector3.zero, Vector3.zero);
                _fluid.Invoke("AdvancePrimitiveSimulation", dt);
                _stationary.Invoke("AdvancePrimitiveSimulation", dt);
                _fluid.Invoke("RenderPrimitiveDeformation");
                _stationary.Invoke("RenderPrimitiveDeformation");
                Vector3[] sites = _fluid.ReadSitePositions();
                Vector3[] control = _stationary.ReadSitePositions();
                Vector3[] surface = ReadSurface(_fluid);
                Vector3[] controlSurface = ReadSurface(_stationary);
                for (int index = 0; index < sites.Length; index++)
                {
                    float travel = Vector3.Distance(sites[index], _homes[index]);
                    _bounded &= !float.IsNaN(travel) && !float.IsInfinity(travel) && travel <= 0.501f;
                    MaximumTravel = Mathf.Max(MaximumTravel, travel);
                }

                if (_stopStep < 0 && atRest)
                {
                    _stopStep = step;
                    _stopSites = sites;
                    _stopControlSites = control;
                    _stopSurface = surface;
                    _stopControlSurface = controlSurface;
                }

                if (_stopStep < 0)
                    return;

                int sinceStop = step - _stopStep;
                if (_trackTips)
                    TrackTips(step, sinceStop * dt, sites, control);

                if (sinceStop * dt <= 0.25f)
                {
                    float mean = 0f;
                    int count = 0;
                    for (int index = 0; index < sites.Length; index++)
                    {
                        if (!_upperFace[index])
                            continue;

                        Vector3 delta = (sites[index] - _stopSites[index]) - (control[index] - _stopControlSites[index]);
                        delta -= _normals[index] * Vector3.Dot(delta, _normals[index]);
                        mean += delta.magnitude * 0.55f;
                        count++;
                    }

                    PostStopMotion = Mathf.Max(PostStopMotion, mean / Mathf.Max(count, 1));
                    float surfaceMotionSum = 0f;
                    for (int index = 0; index < surface.Length; index++)
                    {
                        Vector3 delta = (surface[index] - _stopSurface[index]) - (controlSurface[index] - _stopControlSurface[index]);
                        float motion = delta.magnitude * 0.55f;
                        surfaceMotionSum += motion;
                        PostStopSurfaceMotion = Mathf.Max(PostStopSurfaceMotion, motion);
                    }

                    PostStopMeanSurfaceMotion = Mathf.Max(PostStopMeanSurfaceMotion, surfaceMotionSum / surface.Length);
                }

                if (sinceStop == 0 || sinceStop == 5 || sinceStop == 10 || sinceStop == 20)
                    SaveSnapshot(step, sites, control, surface, controlSurface);
            }

            private Vector3[] ReadSurface(FerrofluidValidationRuntime simulation)
                => ReadSurface(simulation, _surfaceUvs);

            private Vector3[] ReadSurface(FerrofluidValidationRuntime simulation, Vector2[] uvs)
            {
                Renderer renderer = (Renderer)simulation.Get("_renderer");
                Material material = (Material)simulation.Get("_surfaceMaterial");
                Color[] pixels = FerrofluidSurfaceProbe.Sample(material, uvs, 0, renderer.localToWorldMatrix);
                Vector3[] points = new Vector3[pixels.Length];
                for (int index = 0; index < points.Length; index++)
                    points[index] = renderer.worldToLocalMatrix.MultiplyPoint3x4(new Vector3(pixels[index].r, pixels[index].g, pixels[index].b));
                return points;
            }

            private void TrackTips(int step, float afterStop, Vector3[] sites, Vector3[] control)
            {
                Vector2[] liveUvs = new Vector2[_trackedSites.Length];
                Vector2[] controlUvs = new Vector2[_trackedSites.Length];
                for (int sample = 0; sample < _trackedSites.Length; sample++)
                {
                    int site = _trackedSites[sample];
                    liveUvs[sample] = (Vector2)_encodeUv.Invoke(null, new object[] { sites[site], _chartYaw });
                    controlUvs[sample] = (Vector2)_encodeUv.Invoke(null, new object[] { control[site], _chartYaw });
                }

                Vector3[] tips = ReadSurface(_fluid, liveUvs);
                Vector3[] controlTips = ReadSurface(_stationary, controlUvs);
                float signed = 0f;
                float departure = 0f;
                for (int sample = 0; sample < _trackedSites.Length; sample++)
                {
                    int site = _trackedSites[sample];
                    float tipTangent = Vector3.Dot(tips[sample] - controlTips[sample], _tipDirection) * 0.55f;
                    float siteTangent = Vector3.Dot(sites[site] - control[site], _tipDirection) * 0.55f;
                    signed += tipTangent;
                    departure += Mathf.Abs(tipTangent);
                    _tipTrace.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "{0},{1:F4},{2},{3},{4:F7},{5:F7}\n",
                        step,
                        afterStop,
                        sample,
                        site,
                        siteTangent,
                        tipTangent);
                }

                signed /= _trackedSites.Length;
                departure /= _trackedSites.Length;
                if (afterStop == 0f)
                    _tipAtStop = signed;

                if (afterStop <= 0.25f)
                    _tipForwardTravel = Mathf.Max(_tipForwardTravel, signed - _tipAtStop);

                if (afterStop <= 1.2f)
                {
                    _tipSignedPeak = Mathf.Max(_tipSignedPeak, signed);
                    if (afterStop >= 0.06f && signed < _tipSignedPeak - 0.001f)
                        _tipReversed = true;
                }

                if (afterStop <= 0.6f)
                    _tipPeakDeparture = Mathf.Max(_tipPeakDeparture, departure);

                if (afterStop >= 1.5f && afterStop <= 2f)
                    _tipLateDeparture = Mathf.Max(_tipLateDeparture, departure);
            }

            private void SaveSnapshot(int step, Vector3[] sites, Vector3[] control, Vector3[] surface, Vector3[] controlSurface)
            {
                for (int index = 0; index < sites.Length; index++)
                {
                    Vector3 p = sites[index];
                    Vector3 c = control[index];
                    Vector3 h = _homes[index];
                    _snapshots.AppendFormat(CultureInfo.InvariantCulture, "{0},{1},{2:F7},{3:F7},{4:F7},{5:F7},{6:F7},{7:F7},{8:F7},{9:F7},{10:F7}\n", step, index, p.x, p.y, p.z, c.x, c.y, c.z, h.x, h.y, h.z);
                }

                for (int index = 0; index < surface.Length; index++)
                {
                    Vector3 p = surface[index];
                    Vector3 c = controlSurface[index];
                    _surfaceSnapshots.AppendFormat(CultureInfo.InvariantCulture, "{0},{1},{2:F7},{3:F7},{4:F7},{5:F7},{6:F7},{7:F7}\n", step, index, p.x, p.y, p.z, c.x, c.y, c.z);
                }
            }

            internal void Check(string label, string directory)
            {
                File.WriteAllText(Path.Combine(directory, label + "-sites.csv"), _snapshots.ToString());
                File.WriteAllText(Path.Combine(directory, label + "-surface.csv"), _surfaceSnapshots.ToString());
                if (_trackTips)
                {
                    File.WriteAllText(Path.Combine(directory, label + "-tips.csv"), _tipTrace.ToString());
                    Debug.Log($"[FerrofluidFloorContactValidation] {label}: tipForward={_tipForwardTravel:F6} tipReversed={_tipReversed} tipPeakDeparture={_tipPeakDeparture:F6} tipLateDeparture={_tipLateDeparture:F6}");
                    AssertTrue(_tipForwardTravel >= 0.002f, $"{label}: actual spike tips must continue at least 2mm in the rolling direction after full body rest");
                    AssertTrue(_tipReversed, $"{label}: actual spike tips must reverse toward home within 1.2 seconds of the stop");
                    AssertTrue(_tipLateDeparture < 0.5f * _tipPeakDeparture, $"{label}: spike-tip home departure must halve by 1.5-2 seconds");
                }
                Debug.Log($"[FerrofluidFloorContactValidation] {label}: stop={_stopStep}, particleMotion={PostStopMotion:F6}m, renderedSurfaceMotion={PostStopSurfaceMotion:F6}m, meanSurfaceMotion={PostStopMeanSurfaceMotion:F6}m, travel={MaximumTravel:F6} local");
                AssertTrue(_bounded, $"{label}: fluid must stay finite and within its existing .5-local travel bound");
                AssertTrue(_stopStep >= 0 && _stopStep < 135, $"{label}: body must stop early enough to test continued fluid motion");
                AssertTrue(PostStopMotion >= 0.005f, $"{label}: upper-face fluid must continue moving at least 5mm after the core stops ({PostStopMotion:F6}m)");
                AssertTrue(PostStopSurfaceMotion >= 0.003f, $"{label}: rendered slosh must move at least 3mm after the core stops ({PostStopSurfaceMotion:F6}m)");
                AssertTrue(PostStopMeanSurfaceMotion >= 0.001f, $"{label}: mean rendered motion across the upper face must reach 1mm, not just one moving sample ({PostStopMeanSurfaceMotion:F6}m)");
            }

            public void Dispose()
            {
                _fluid.Dispose();
                _stationary.Dispose();
            }
        }

        private static float FaceAngle(Quaternion rotation)
        {
            Vector3 localUp = Quaternion.Inverse(rotation) * Vector3.up;
            float alignment = Mathf.Max(Mathf.Abs(localUp.x), Mathf.Max(Mathf.Abs(localUp.y), Mathf.Abs(localUp.z)));
            return Mathf.Acos(Mathf.Clamp01(alignment)) * Mathf.Rad2Deg;
        }

        private static Component MakeFloorFaceBody(string name, bool sphere, Quaternion rotation, float? roundness)
        {
            Component component = MakeBody(name, sphere, Vector3.zero, rotation, roundness);
            FloorContactObserver observer = component.gameObject.AddComponent<FloorContactObserver>();
            observer.Floor = Ctx.FloorCollider;
            Rigidbody rb = component.GetComponent<Rigidbody>();
            rb.position = Vector3.up * (Support(ActualShape(component), rotation) + 0.002f);
            Physics.SyncTransforms();
            return component;
        }

        private static IEnumerator CaseImpacts()
        {
            foreach ((string label, bool sphere, Quaternion rotation, float vy, float dt) in new (string, bool, Quaternion, float, float)[]
            {
                ("sphere fast", true, Quaternion.identity, -25f, 0.02f),
                ("sphere fine", true, Quaternion.identity, -25f, 1f / 90f),
                ("sphere slow", true, Quaternion.identity, -2f, 0.02f),
                ("cube fast", false, Quaternion.identity, -25f, 0.02f),
                ("cube tilted", false, Quaternion.Euler(30f, 45f, 20f), -25f, 0.02f),
            })
            {
                IEnumerator run = DropAndCheck(label, sphere, rotation, vy, dt);
                while (run.MoveNext())
                    yield return run.Current;
            }
        }

        private static Component MakeBody(string name, bool sphere, Vector3 position, Quaternion rotation, float? roundness)
        {
            GameObject body = new(name);
            Resources.Add(body);
            body.transform.position = position;
            body.transform.rotation = rotation;
            body.transform.localScale = Vector3.one * 0.55f;
            Component component = body.AddComponent(Ctx.Grabbable);
            Ctx.Initialize.Invoke(component, new object[] { Ctx.Enclosure, sphere, Ctx.BodyMaterial, Ctx.Ownership });
            if (Ctx.ConfigureFloorContact != null)
                Ctx.ConfigureFloorContact.Invoke(component, new object[] { Ctx.FloorCollider });
            if (!sphere && roundness.HasValue && Ctx.ConfigureCubeRoundness != null)
                Ctx.ConfigureCubeRoundness.Invoke(component, new object[] { roundness.Value });

            // Manual stepping already invokes the physics path; Unity's own FixedUpdate would add
            // every force twice. Disabled components still receive collision callbacks.
            ((MonoBehaviour)component).enabled = false;
            return component;
        }

        private static object ActualShape(Component component)
            => Ctx.GrabShape.GetValue(component);

        private static float Support(object shape, Quaternion rotation)
            => (float)Ctx.Extent.Invoke(null, new object[] { shape, rotation, Vector3.down });

        private static IEnumerator DropAndCheck(string label, bool sphere, Quaternion rotation, float vy, float dt)
        {
            Time.fixedDeltaTime = dt;
            Component component = MakeBody($"contact {label}", sphere, new Vector3(0f, 1.5f, 0f), rotation, 0.48f);
            GameObject body = component.gameObject;
            Rigidbody rb = body.GetComponent<Rigidbody>();
            rb.linearVelocity = new Vector3(0f, vy, 0f);
            object shape = ActualShape(component);

            int contactStep = -1;
            float contactGap = float.NaN;
            float reboundVy = float.NaN;
            float incidentVy = float.NaN;
            float worstPenetration = 0f;
            float bestRenderedGap = float.MaxValue;
            bool roseAfter = false;
            for (int step = 0; step < 40; step++)
            {
                float preVy = rb.linearVelocity.y;
                Ctx.SampleGrabTarget.Invoke(component, new object[] { dt, Time.fixedUnscaledTime });
                Ctx.FixedUpdate.Invoke(component, null);
                Physics.Simulate(dt);
                if (contactStep < 0 && rb.linearVelocity.y > preVy + 0.5f)
                {
                    contactStep = step;
                    incidentVy = preVy;
                    contactGap = rb.position.y - Support(shape, rb.rotation);
                    reboundVy = rb.linearVelocity.y;
                }
                else if (contactStep >= 0 && step > contactStep && rb.linearVelocity.y > 0f)
                {
                    roseAfter |= rb.position.y - Support(shape, rb.rotation) > contactGap + 0.01f;
                }

                float rbGap = rb.position.y - Support(shape, rb.rotation);
                worstPenetration = Mathf.Min(worstPenetration, rbGap);
                yield return null;
                if (contactStep >= 0 && step <= contactStep + 3)
                {
                    float renderedGap = body.transform.position.y - Support(shape, body.transform.rotation);
                    bestRenderedGap = Mathf.Min(bestRenderedGap, renderedGap);
                    worstPenetration = Mathf.Min(worstPenetration, renderedGap);
                }
            }

            Debug.Log($"[FerrofluidFloorContactValidation] {label} contactStep={contactStep} contactGap={contactGap:F4} renderedGap={bestRenderedGap:F4} incident={incidentVy:F2} rebound={reboundVy:F2} worstPen={worstPenetration:F4} rose={roseAfter}");
            AssertTrue(contactStep >= 0, $"{label} never contacted the floor");
            if (contactStep >= 0)
            {
                AssertTrue(Mathf.Abs(contactGap) <= 0.002f, $"{label} contact-frame gap {contactGap:F4} — the published pose must land on the floor");
                AssertTrue(Mathf.Abs(bestRenderedGap) <= 0.002f, $"{label} rendered gap {bestRenderedGap:F4} — the first-impact frames must display the touch");
                AssertTrue(worstPenetration > -0.005f, $"{label} penetrated {worstPenetration:F4} deeper than 5mm");
                AssertTrue(reboundVy > 0f && reboundVy < -incidentVy * 0.5f, $"{label} rebound {reboundVy:F2} not near native restitution from {incidentVy:F2}");
                AssertTrue(roseAfter, $"{label} stayed snapped instead of bouncing away");
            }

            body.SetActive(false);
        }

        private static IEnumerator CaseHeldImmunity()
        {
            Component component = MakeFloorFaceBody("contact held cube", false, Quaternion.identity, 0.48f);
            GameObject body = component.gameObject;
            Rigidbody rb = body.GetComponent<Rigidbody>();
            rb.position -= Vector3.up * 0.003f;
            FloorContactObserver observer = body.GetComponent<FloorContactObserver>();
            GameObject pointer = new("held floor-contact pointer");
            Resources.Add(pointer);
            pointer.transform.position = rb.position + new Vector3(0f, -0.275f, 0f);
            Ctx.Acquire?.Invoke(component, new object[] { pointer.transform, pointer.transform.position });
            Vector3 heldPosition = rb.position;
            for (int step = 0; step < 10; step++)
            {
                Ctx.SampleGrabTarget.Invoke(component, new object[] { 0.02f, Time.fixedUnscaledTime });
                Ctx.FixedUpdate.Invoke(component, null);
                Physics.Simulate(0.02f);
                yield return null;
            }

            AssertTrue(observer.TouchedFloor, "held immunity must exercise actual floor contact");
            AssertTrue(Mathf.Abs(rb.position.y - heldPosition.y) < 0.02f, $"held cube pose was corrected by the floor contact ({rb.position.y:F4} vs {heldPosition.y:F4})");
            body.SetActive(false);
        }

        private static IEnumerator CaseDefaultFreeFall()
        {
            const float dt = 0.02f;
            Time.fixedDeltaTime = dt;
            foreach (bool sphere in new[] { false, true })
            {
                Component cube = MakeBody("default free fall", sphere, Vector3.up * 3f, Quaternion.identity, 0.48f);
                Rigidbody body = cube.GetComponent<Rigidbody>();
                float start = body.position.y;
                for (int step = 0; step < 20; step++)
                {
                    Ctx.FixedUpdate.Invoke(cube, null);
                    Physics.Simulate(dt);
                    yield return null;
                }

                float expectedSpeed = -9.8f * dt * 20f;
                float expectedDrop = 9.8f * dt * dt * 20f * 21f / 2f;
                Debug.Log($"[FerrofluidFloorContactValidation] default free fall sphere={sphere} seconds={20f * dt:F3} drop={start - body.position.y:F6} vy={body.linearVelocity.y:F6} damping={body.linearDamping:F6} timeScale={Time.timeScale:F4}");
                AssertTrue(Mathf.Abs(body.linearVelocity.y - expectedSpeed) < 0.03f, "default free fall must accelerate at 9.8 m/s^2 without air braking");
                AssertTrue(Mathf.Abs((start - body.position.y) - expectedDrop) < 0.015f, "default free-fall displacement must match the simulated elapsed time");
                cube.gameObject.SetActive(false);
            }
        }

        private static void CaseFastApproach()
        {
            Component cube = MakeFloorFaceBody("fast face approach", false, Quaternion.Euler(0f, 0f, 1f), 0.48f);
            Rigidbody body = cube.GetComponent<Rigidbody>();
            body.position -= Vector3.up * 0.001f;
            body.linearVelocity = Vector3.left * 3f;
            Vector3 incoming = body.angularVelocity = Vector3.back * 2f;
            Ctx.FixedUpdate.Invoke(cube, null);
            Debug.Log($"[FerrofluidFloorContactValidation] fast face approach incoming={incoming} afterUpdate={body.angularVelocity} speed={body.linearVelocity.magnitude:F4}");
            AssertTrue(Vector3.Distance(body.angularVelocity, incoming) < 0.01f, "a fast sliding cube must not receive the gentle face-landing impulse");
            cube.gameObject.SetActive(false);
        }

        private static IEnumerator CaseCornerTop()
        {
            Quaternion cornerUp = Quaternion.FromToRotation(Vector3.one.normalized, Vector3.up);
            Component component = MakeFloorFaceBody("contact corner cube", false, cornerUp, 0.48f);
            object shape = ActualShape(component);
            GameObject body = component.gameObject;
            Rigidbody rb = body.GetComponent<Rigidbody>();
            rb.angularVelocity = new Vector3(0f, 20f * 2f * Mathf.PI, 0f);
            float startOmega = rb.angularVelocity.y;
            float maxHover = 0f;
            float belowRps = -1f;
            float omegaAt45 = float.NaN;
            for (int step = 0; step <= 3000; step++)
            {
                Ctx.SampleGrabTarget.Invoke(component, new object[] { 0.02f, Time.fixedUnscaledTime });
                Ctx.FixedUpdate.Invoke(component, null);
                Physics.Simulate(0.02f);
                float gap = rb.position.y - Support(shape, rb.rotation);
                maxHover = Mathf.Max(maxHover, Mathf.Max(0f, gap));
                if (belowRps < 0f && Mathf.Abs(rb.angularVelocity.y) < Mathf.PI * 2f)
                    belowRps = step * 0.02f;
                if (belowRps >= 0f)
                    break;
                if (step == 500 || step == 1500 || step == 2250 || step == 3000)
                    Debug.Log($"[FerrofluidFloorContactValidation] corner t={step * 0.02f:F0}s omegaY={rb.angularVelocity.y:F2}");
                if (step == 2250)
                    omegaAt45 = rb.angularVelocity.y;
                yield return null;
            }

            float loss = startOmega - rb.angularVelocity.y;
            Debug.Log($"[FerrofluidFloorContactValidation] corner below1RPS={belowRps:F1}s omegaAt45={omegaAt45:F2} omegaLoss={loss:F2} maxHover={maxHover:F4} finalY={rb.position.y:F4}");
            AssertTrue(
                belowRps > 45f || (belowRps < 0f && omegaAt45 > Mathf.PI * 2f),
                $"corner top must still coast past 45s (below1RPS={belowRps:F1}s, omegaAt45={omegaAt45:F2})");
            AssertTrue(maxHover < 0.02f, $"corner top hovered {maxHover:F4} above floor contact");
            body.SetActive(false);
        }

        private static IEnumerator CaseRoundnessGeometry(Assembly plugin)
        {
            CheckBoxCoreGeometry(plugin);
            yield return null;
        }

        private static IEnumerator CaseTiltedCornerTop(float tiltDegrees)
        {
            StringBuilder trace = new("step,time,preSpeed,afterUpdateSpeed,postSpeed,gap,postGap,clearance,postClearance,diagonalAlignment,materialBefore,materialDuringSim,contacts,minSeparation,minNormalY,faceAngle,vx,vy,vz,wx,wy,wz\n");
            Quaternion tilt = Quaternion.AngleAxis(tiltDegrees, Vector3.right);
            Quaternion rotation = tilt * Quaternion.FromToRotation(Vector3.one.normalized, Vector3.up);
            Component cube = MakeFloorFaceBody($"tilted top {tiltDegrees}", false, rotation, 0.48f);
            Rigidbody body = cube.GetComponent<Rigidbody>();
            FloorContactObserver observer = cube.GetComponent<FloorContactObserver>();
            float deltaTime = Environment.GetEnvironmentVariable("FERROFLUID_TOP_FINE_STEP") == "1"
                ? 0.002f
                : 0.02f;
            Time.fixedDeltaTime = deltaTime;
            int stepCount = Mathf.RoundToInt(10f / deltaTime);
            int stepsPerFrame = Mathf.RoundToInt(0.02f / deltaTime);
            FieldInfo materialState = Ctx.Grabbable.GetField("_cornerMaterialActive", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object shape = ActualShape(cube);
            Vector3 half = (Vector3)shape.GetType().GetField("HalfExtents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shape);
            float initialSpeed = 20f * 2f * Mathf.PI;
            body.angularVelocity = tilt * Vector3.up * initialSpeed;
            float minimumSpeedBeforeEdge = initialSpeed;
            int firstEdge = -1;
            int eligiblePointSteps = 0;
            int wrongGripSteps = 0;
            for (int step = 0; step < stepCount; step++)
            {
                float preSpeed = body.angularVelocity.magnitude;
                bool materialBefore = (bool)materialState.GetValue(cube);
                Quaternion inverse = Quaternion.Inverse(body.rotation);
                Vector3 up = inverse * Vector3.up;
                float clearance = 2f * Mathf.Min(
                    Mathf.Abs(up.x) * half.x,
                    Mathf.Min(Mathf.Abs(up.y) * half.y, Mathf.Abs(up.z) * half.z));
                float preClearance = clearance;
                Vector3 localAngular = inverse * body.angularVelocity;
                float diagonalAlignment = (Mathf.Abs(localAngular.x) + Mathf.Abs(localAngular.y) + Mathf.Abs(localAngular.z))
                    / (Mathf.Sqrt(3f) * localAngular.magnitude);
                float gap = body.position.y - Support(shape, body.rotation);
                Ctx.FixedUpdate.Invoke(cube, null);
                float afterUpdateSpeed = body.angularVelocity.magnitude;
                bool materialDuringSim = (bool)materialState.GetValue(cube);
                if (step >= 2 && gap <= 0.01f && clearance > 0.004f && diagonalAlignment >= 0.95f)
                {
                    eligiblePointSteps++;
                    if (!(bool)materialState.GetValue(cube))
                        wrongGripSteps++;
                }

                observer.ResetContacts();
                Physics.Simulate(deltaTime);
                up = Quaternion.Inverse(body.rotation) * Vector3.up;
                clearance = 2f * Mathf.Min(
                    Mathf.Abs(up.x) * half.x,
                    Mathf.Min(Mathf.Abs(up.y) * half.y, Mathf.Abs(up.z) * half.z));
                if (firstEdge < 0 && observer.TouchedFloor && clearance <= 0.002f)
                    firstEdge = step;

                if (firstEdge < 0)
                    minimumSpeedBeforeEdge = Mathf.Min(minimumSpeedBeforeEdge, body.angularVelocity.magnitude);

                Vector3 velocity = body.linearVelocity;
                Vector3 angular = body.angularVelocity;
                trace.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "{0},{1:F4},{2:F6},{3:F6},{4:F6},{5:F6},{6:F6},{7:F6},{8:F6},{9:F6},{10},{11},{12},{13:F6},{14:F6},{15:F6},{16:F6},{17:F6},{18:F6},{19:F6},{20:F6},{21:F6}\n",
                    step,
                    (step + 1) * deltaTime,
                    preSpeed,
                    afterUpdateSpeed,
                    angular.magnitude,
                    gap,
                    body.position.y - Support(shape, body.rotation),
                    preClearance,
                    clearance,
                    diagonalAlignment,
                    materialBefore,
                    materialDuringSim,
                    observer.ContactCount,
                    observer.MinimumSeparation,
                    observer.MinimumNormalY,
                    FaceAngle(body.rotation),
                    velocity.x,
                    velocity.y,
                    velocity.z,
                    angular.x,
                    angular.y,
                    angular.z);
                if ((step + 1) % stepsPerFrame == 0)
                    yield return null;
            }

            string repository = Directory.GetParent(Application.dataPath)!.Parent!.FullName;
            string tracePath = Path.GetFullPath(Path.Combine(repository, "..", "Scratch", "ferro-flat-core-slosh", $"tilted-point-{tiltDegrees}-{DateTime.UtcNow:yyyyMMddTHHmmssfff}.csv"));
            File.WriteAllText(tracePath, trace.ToString());
            Debug.Log($"[FerrofluidFloorContactValidation] tilted trace={tracePath}");
            Debug.Log($"[FerrofluidFloorContactValidation] tilted top={tiltDegrees} eligiblePointSteps={eligiblePointSteps} wrongGripSteps={wrongGripSteps} firstEdge={firstEdge} minimumSpeedBeforeEdge={minimumSpeedBeforeEdge:F4} initialSpeed={initialSpeed:F4} collisionMode={body.collisionDetectionMode} deltaTime={deltaTime:F4}");
            AssertTrue(eligiblePointSteps * deltaTime >= 1f, $"{tiltDegrees}-degree top must exercise at least one second of point-supported spin");
            AssertTrue(wrongGripSteps == 0, $"{tiltDegrees}-degree smooth top must retain low grip until an edge reaches the floor");
            AssertTrue(minimumSpeedBeforeEdge >= 0.8f * initialSpeed, $"{tiltDegrees}-degree top must not lose more than 20% spin in 10s before edge contact");
            cube.gameObject.SetActive(false);
        }

        private static IEnumerator CaseHeldReconfigure()
        {
            Component component = MakeBody("reconfigure cube", false, new Vector3(0f, 1f, 0f), Quaternion.identity, 0.48f);
            GameObject body = component.gameObject;
            Rigidbody rb = body.GetComponent<Rigidbody>();
            Ctx.Acquire?.Invoke(component, new object[] { body.transform, rb.position + new Vector3(0f, -0.275f, 0f) });
            Vector3 angularBefore = rb.angularVelocity = new Vector3(0.5f, 1.2f, -0.7f);
            rb.linearVelocity = new Vector3(1f, 0.5f, -0.5f);
            Vector3 originVelocityBefore = rb.GetPointVelocity(rb.position);
            if (Ctx.ConfigureCubeRoundness != null)
                Ctx.ConfigureCubeRoundness.Invoke(component, new object[] { 0.2f });
            FieldInfo attach = Ctx.Grabbable.GetField("_attachTransform", BindingFlags.Instance | BindingFlags.NonPublic);
            AssertTrue(attach != null && attach.GetValue(component) != null, "held reconfigure dropped the grab");
            AssertTrue(Vector3.Distance(rb.angularVelocity, angularBefore) < 0.001f, $"held reconfigure changed angular velocity {rb.angularVelocity} vs {angularBefore}");
            AssertTrue(Vector3.Distance(rb.GetPointVelocity(rb.position), originVelocityBefore) < 0.001f, $"held reconfigure changed origin motion {rb.GetPointVelocity(rb.position)} vs {originVelocityBefore}");
            body.SetActive(false);
            yield return null;
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                Failures.Add(message);
        }
    }
}
