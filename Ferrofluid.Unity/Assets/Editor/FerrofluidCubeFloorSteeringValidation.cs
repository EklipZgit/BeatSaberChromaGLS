using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // Cube floor steering and flat-slide regression: the tilted corner top must trade lateral
    // velocity back and forth (restored corner grip) and flat faces must slide measurably longer
    // under the floor-contact material than under the old shared-material friction. Raw rigidbody
    // state is exported per step for offline trajectory review.
    internal static class FerrofluidCubeFloorSteeringValidation
    {
        private const float Dt = 0.02f;
        private static string CsvDir = string.Empty;
        private static readonly List<string> Failures = new();
        private static readonly List<Object> Resources = new();
        private static Context Ctx = null!;

        private sealed class Context
        {
            internal Type Grabbable = null!;
            internal MethodInfo Initialize = null!;
            internal MethodInfo Advance = null!;
            internal MethodInfo Acquire = null!;
            internal MethodInfo ConfigureCubeRoundness = null!;
            internal MethodInfo ConfigureFloorContact = null!;
            internal FieldInfo GrabShape = null!;
            internal FieldInfo CornerActive = null!;
            internal FieldInfo CubeCollider = null!;
            internal MethodInfo Extent = null!;
            internal object Enclosure = null!;
            internal object Ownership = null!;
            internal PhysicsMaterial FloorMaterial = null!;
            internal PhysicsMaterial BodyMaterial = null!;
            internal Type Physics = null!;
            internal Collider FloorCollider = null!;
        }

        public static void Run()
            => FerrofluidValidationRunner.Run(typeof(FerrofluidCubeFloorSteeringValidation));

        public static void RunFaceGrip()
        {
            Environment.SetEnvironmentVariable("FERROFLUID_STEERING_CASES", "grip");
            Run();
        }

        public static void RunSteering()
        {
            Environment.SetEnvironmentVariable("FERROFLUID_STEERING_CASES", "steering");
            Run();
        }

        private static IEnumerator RunRuntime()
        {
            SimulationMode previousMode = Physics.simulationMode;
            float previousDelta = Time.fixedDeltaTime;
            Physics.simulationMode = SimulationMode.Script;
            Time.fixedDeltaTime = Dt;
            Failures.Clear();
            Resources.Clear();
            Ctx = new Context();
            try
            {
                string repository = Directory.GetParent(Application.dataPath)!.Parent!.FullName;
                string pluginPath = Environment.GetEnvironmentVariable("FERROFLUID_VALIDATION_PLUGIN")
                    ?? Path.Combine(repository, "ChromaGLS/bin/Release-1.44.1/net48/ChromaGLS.dll");
                CsvDir = Environment.GetEnvironmentVariable("FERROFLUID_VALIDATION_OUTPUT")
                    ?? Path.Combine(Directory.GetParent(repository)!.FullName, "Scratch", "ferro-cube-floor-steering", "validation");
                Debug.Log($"[FerrofluidCubeFloorSteeringValidation] dll={pluginPath} csv={CsvDir}");
                Assembly plugin = Assembly.LoadFrom(pluginPath);
                Ctx.Grabbable = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true);
                Ctx.Initialize = Ctx.Grabbable.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.Advance = Ctx.Grabbable.GetMethod("AdvancePhysics", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.Acquire = Ctx.Grabbable.GetMethod("Acquire", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.ConfigureCubeRoundness = Ctx.Grabbable.GetMethod("ConfigureCubeRoundness", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.ConfigureFloorContact = Ctx.Grabbable.GetMethod("ConfigureFloorContact", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.GrabShape = Ctx.Grabbable.GetField("_grabShape", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.CornerActive = Ctx.Grabbable.GetField("_cornerMaterialActive", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.CubeCollider = Ctx.Grabbable.GetField("_cubeCollider", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.Physics = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabPhysics", true);
                Ctx.Extent = Ctx.Physics
                    .GetMethod("EvaluateSupportExtent", BindingFlags.Static | BindingFlags.NonPublic);
                Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true);
                Ctx.Enclosure = enclosureType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null,
                    new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                    null)!.Invoke(new object[] { Vector3.zero, 100f, new Vector3(0f, 0f, -100f), Vector3.forward, 0f });
                Ctx.Ownership = Activator.CreateInstance(plugin.GetType("ChromaGLS.Settings.FerrofluidGrabOwnership", true), nonPublic: true);
                Type manager = plugin.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true);
                MethodInfo factory = manager.GetMethod("CreateContactMaterial", BindingFlags.Static | BindingFlags.NonPublic);
                Ctx.FloorMaterial = (PhysicsMaterial)factory!.Invoke(null, new object[] { true });
                Ctx.BodyMaterial = (PhysicsMaterial)factory.Invoke(null, new object[] { false });
                Resources.Add(Ctx.FloorMaterial);
                Resources.Add(Ctx.BodyMaterial);

                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Resources.Add(floor);
                floor.name = "cube floor steering floor";
                floor.layer = 3;
                floor.transform.localScale = new Vector3(40f, 1f, 40f);
                floor.transform.position = new Vector3(0f, -0.5f, 0f);
                Ctx.FloorCollider = floor.GetComponent<Collider>();
                Ctx.FloorCollider.material = Ctx.FloorMaterial;

                foreach (float dt in new[] { 0.02f, 1f / 90f })
                {
                    IEnumerator steering = CaseSteering(dt);
                    while (steering.MoveNext())
                        yield return steering.Current;
                }

                if (Environment.GetEnvironmentVariable("FERROFLUID_STEERING_CASES") == "grip")
                {
                    IEnumerator grip = RunGripCases();
                    while (grip.MoveNext())
                        yield return grip.Current;
                }

                if (Environment.GetEnvironmentVariable("FERROFLUID_STEERING_CASES") != "steering" && Environment.GetEnvironmentVariable("FERROFLUID_STEERING_CASES") != "grip")
                {
                    (string name, Vector3 face)[] faces =
                    {
                        ("up", Vector3.up),
                        ("down", Vector3.down),
                        ("left", Vector3.left),
                        ("right", Vector3.right),
                        ("forward", Vector3.forward),
                        ("back", Vector3.back),
                    };
                    foreach (float dt in new[] { 0.02f, 1f / 90f })
                    {
                        foreach ((string name, Vector3 face) in faces)
                        {
                            float[] legacyDistance = new float[1];
                            IEnumerator current = CaseFlatSlide(name, face, dt, 0.125f, legacyDistance);
                            while (current.MoveNext())
                                yield return current.Current;
                            float[] oldDistance = new float[1];
                            IEnumerator previous = CaseFlatSlide(name, face, dt, 0.15f, oldDistance);
                            while (previous.MoveNext())
                                yield return previous.Current;
                            AssertTrue(
                                legacyDistance[0] > 0.1f && oldDistance[0] > 0.1f,
                                $"slide {name} dt={dt:F4} must travel measurably (f0.125={legacyDistance[0]:F3} f0.15={oldDistance[0]:F3})");
                            AssertTrue(
                                legacyDistance[0] > oldDistance[0] * 1.02f && legacyDistance[0] < oldDistance[0] * 1.3f,
                                $"slide {name} dt={dt:F4} the 0.125 setting must slide slightly longer than 0.15 (f0.125={legacyDistance[0]:F3} f0.15={oldDistance[0]:F3})");
                        }
                    }

                    IEnumerator lifecycle = CaseMaterialLifecycle();
                    while (lifecycle.MoveNext())
                        yield return lifecycle.Current;

                    IEnumerator grip = RunGripCases();
                    while (grip.MoveNext())
                        yield return grip.Current;
                }

                foreach (string failure in Failures)
                    Debug.LogWarning($"[FerrofluidCubeFloorSteeringValidation] FAIL: {failure}");

                if (Failures.Count > 0)
                    throw new Exception($"{Failures.Count} cube floor steering cases failed: {string.Join(" | ", Failures)}");

                Debug.Log("[FerrofluidCubeFloorSteeringValidation] PASS: corner steering, flat slide, material lifetime verified.");
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

        private static Component MakeBody(string name, bool sphere, Vector3 position, Quaternion rotation, float? roundness)
        {
            GameObject body = new(name);
            Resources.Add(body);
            body.transform.position = position;
            body.transform.rotation = rotation;
            body.transform.localScale = Vector3.one * 0.55f;
            Component component = body.AddComponent(Ctx.Grabbable);
            Ctx.Initialize.Invoke(component, new object[] { Ctx.Enclosure, sphere, Ctx.BodyMaterial, Ctx.Ownership });
            Ctx.ConfigureFloorContact.Invoke(component, new object[] { Ctx.FloorCollider });
            if (!sphere && roundness.HasValue)
                Ctx.ConfigureCubeRoundness.Invoke(component, new object[] { roundness.Value });

            ((MonoBehaviour)component).enabled = false;
            return component;
        }

        private static float Support(object shape, Quaternion rotation)
            => (float)Ctx.Extent.Invoke(null, new object[] { shape, rotation, Vector3.down });

        private static object MakeConfig(float floorFriction, float cornerFriction)
        {
            Type configType = Ctx.Grabbable.Assembly.GetType("ChromaGLS.Settings.Config", true)!;
            object config = Activator.CreateInstance(configType, nonPublic: true)!;
            configType.GetProperty("FerrofluidCubeFloorFriction")!.SetValue(config, floorFriction);
            configType.GetProperty("FerrofluidCubeCornerFriction")!.SetValue(config, cornerFriction);
            return config;
        }

        private static void ConfigureFloorPhysics(Component component, object config)
            => (Ctx.Grabbable.GetMethod("ConfigureContactPhysics", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new Exception("FerrofluidGrabbable must own ConfigureContactPhysics for live floor tuning."))
                .Invoke(component, new[] { config });

        private static FieldInfo FloorContactMaterialField()
            => Ctx.Grabbable.GetField("_floorContactMaterial", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new Exception("FerrofluidGrabbable must own _floorContactMaterial.");

        private static float ColliderFriction(Component component)
            => ((Collider)Ctx.CubeCollider.GetValue(component)).sharedMaterial!.dynamicFriction;

        private static float MaterialFriction(float desired)
            => (float)Ctx.Physics.GetMethod("EvaluateCubeFloorMaterialFriction", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { desired })!;

        private static IEnumerator CaseSteering(float dt)
        {
            Quaternion rotation = Quaternion.AngleAxis(5f, Vector3.right)
                * Quaternion.FromToRotation(Vector3.one.normalized, Vector3.up);
            string label = $"steering-tilt5-dt{dt:0.0000}";
            Component component = MakeBody(label, false, Vector3.zero, rotation, 0.48f);
            object shape = Ctx.GrabShape.GetValue(component);
            float support = Support(shape, rotation);
            GameObject body = component.gameObject;
            body.transform.position = new Vector3(0f, support + 0.001f, 0f);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            rb.position = body.transform.position;
            rb.angularVelocity = Vector3.up * (10f * 2f * Mathf.PI);
            rb.linearVelocity = new Vector3(0.08f, 0f, 0f);

            Time.fixedDeltaTime = dt;
            int steps = Mathf.RoundToInt(8f / dt);
            float minVz = float.MaxValue;
            float maxVz = float.MinValue;
            float maxGap = 0f;
            List<string> rows = new(steps + 2)
            {
                "step,time,px,py,pz,vx,vy,vz,wx,wy,wz,qx,qy,qz,qw,cornerActive,gap",
            };
            for (int step = 0; step <= steps; step++)
            {
                float time = step * dt;
                Vector3 velocity = rb.linearVelocity;
                float gap = rb.position.y - Support(shape, rb.rotation);
                maxGap = Mathf.Max(maxGap, gap);
                if (time >= 4f)
                {
                    minVz = Mathf.Min(minVz, velocity.z);
                    maxVz = Mathf.Max(maxVz, velocity.z);
                }

                AppendRow(rows, component, step, time, rb, gap);
                if (step == steps)
                    break;

                Ctx.Advance.Invoke(component, new object[] { dt, time });
                Physics.Simulate(dt);
                yield return null;
            }

            WriteRows($"cube-floor-{label}.csv", rows);
            Debug.Log($"[FerrofluidCubeFloorSteeringValidation] {label}: minVz={minVz:F4} maxVz={maxVz:F4} wy={rb.angularVelocity.y:F2} maxGap={maxGap:F4} rows={rows.Count - 1}");
            AssertTrue(
                minVz < -0.005f && maxVz > 0.005f,
                $"{label} must alternate sideways velocity in 4-8s (minVz={minVz:F4} maxVz={maxVz:F4})");
            AssertTrue(
                rb.angularVelocity.y > 40f,
                $"{label} must keep spinning past 8s (wy={rb.angularVelocity.y:F2})");
            AssertTrue(
                maxGap < 0.02f,
                $"{label} hovered off the rendered shell (maxGap={maxGap:F4})");
            body.SetActive(false);
        }

        private static IEnumerator CaseFlatSlide(string faceName, Vector3 face, float dt, float? floorFriction, float[] distance)
        {
            Quaternion rotation = Quaternion.FromToRotation(face, Vector3.up);
            string frictionLabel = floorFriction.HasValue
                ? string.Format(CultureInfo.InvariantCulture, "f{0:0.###}", floorFriction.Value)
                : "default";
            string label = $"slide-{faceName}-{frictionLabel}-dt{dt:0.0000}";
            Component component = MakeBody(label, false, Vector3.zero, rotation, 0.48f);
            if (floorFriction.HasValue)
                ConfigureFloorPhysics(component, MakeConfig(floorFriction.Value, 0.01f));
            object shape = Ctx.GrabShape.GetValue(component);
            float support = Support(shape, rotation);
            GameObject body = component.gameObject;
            body.transform.position = new Vector3(0f, support + 0.002f, 0f);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            rb.position = body.transform.position;
            rb.angularVelocity = Vector3.zero;
            rb.linearVelocity = new Vector3(1f, 0f, 0f);

            Time.fixedDeltaTime = dt;
            int steps = Mathf.RoundToInt(0.4f / dt);
            List<string> rows = new(steps + 2)
            {
                "step,time,px,py,pz,vx,vy,vz,wx,wy,wz,qx,qy,qz,qw,cornerActive,gap",
            };
            for (int step = 0; step <= steps; step++)
            {
                float time = step * dt;
                float gap = rb.position.y - Support(shape, rb.rotation);
                AppendRow(rows, component, step, time, rb, gap);
                if (step == steps)
                    break;

                Ctx.Advance.Invoke(component, new object[] { dt, time });
                Physics.Simulate(dt);
                yield return null;
            }

            distance[0] = rb.position.x;
            float expected = MaterialFriction(floorFriction ?? 0.25f);
            float actual = ColliderFriction(component);
            WriteRows($"cube-floor-{label}.csv", rows);
            Debug.Log($"[FerrofluidCubeFloorSteeringValidation] {label}: distance={distance[0]:F4} friction={actual:F4} rows={rows.Count - 1}");
            AssertTrue(
                Mathf.Abs(actual - expected) < 0.0005f,
                $"{label} collider must run the floor-contact friction {expected:F3} once resting (actual={actual:F4})");
            body.SetActive(false);
        }

        private static IEnumerator RunGripCases()
        {
            (string name, Vector3 face)[] faces =
            {
                ("up", Vector3.up),
                ("down", Vector3.down),
                ("left", Vector3.left),
                ("right", Vector3.right),
                ("forward", Vector3.forward),
                ("back", Vector3.back),
            };
            foreach (float dt in new[] { 0.02f, 1f / 90f })
            {
                foreach ((string name, Vector3 face) in faces)
                {
                    float[] defaultDecel = new float[1];
                    float[] defaultDistance = new float[1];
                    IEnumerator current = CaseFaceGrip(name, face, dt, null, defaultDecel, defaultDistance);
                    while (current.MoveNext())
                        yield return current.Current;
                    float[] oldDecel = new float[1];
                    float[] oldDistance = new float[1];
                    IEnumerator previous = CaseFaceGrip(name, face, dt, 0.125f, oldDecel, oldDistance);
                    while (previous.MoveNext())
                        yield return previous.Current;
                    AssertTrue(
                        oldDistance[0] > 0.05f && defaultDistance[0] > 0.02f,
                        $"grip {name} dt={dt:F4} must travel measurably (default={defaultDistance[0]:F3} old={oldDistance[0]:F3})");
                    AssertTrue(
                        defaultDecel[0] > oldDecel[0] * 1.8f && defaultDecel[0] < oldDecel[0] * 2.2f,
                        $"grip {name} dt={dt:F4} default must brake ~2x the 0.125 setting (default={defaultDecel[0]:F3} old={oldDecel[0]:F3})");
                    AssertTrue(
                        defaultDistance[0] > oldDistance[0] * 0.4f && defaultDistance[0] < oldDistance[0] * 0.6f,
                        $"grip {name} dt={dt:F4} default must stop in about half the distance (default={defaultDistance[0]:F3} old={oldDistance[0]:F3})");
                }
            }
        }

        // Settles a cube flat for fifty steps, then releases a one metre-per-second slide and
        // measures the first-80ms deceleration plus total travel under the chosen floor friction.
        private static IEnumerator CaseFaceGrip(string faceName, Vector3 face, float dt, float? floorFriction, float[] decel, float[] distance)
        {
            Quaternion rotation = Quaternion.FromToRotation(face, Vector3.up);
            string frictionLabel = floorFriction.HasValue
                ? string.Format(CultureInfo.InvariantCulture, "f{0:0.###}", floorFriction.Value)
                : "default";
            string label = $"grip-{faceName}-{frictionLabel}-dt{dt:0.0000}";
            Component component = MakeBody(label, false, Vector3.zero, rotation, 0.48f);
            if (floorFriction.HasValue)
                ConfigureFloorPhysics(component, MakeConfig(floorFriction.Value, 0.01f));
            object shape = Ctx.GrabShape.GetValue(component);
            float support = Support(shape, rotation);
            GameObject body = component.gameObject;
            body.transform.position = new Vector3(0f, support + 0.002f, 0f);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            rb.position = body.transform.position;
            rb.angularVelocity = Vector3.zero;
            rb.linearVelocity = Vector3.zero;
            Time.fixedDeltaTime = dt;

            for (int step = 0; step < 50; step++)
            {
                Ctx.Advance.Invoke(component, new object[] { dt, step * dt });
                Physics.Simulate(dt);
                yield return null;
            }

            rb.linearVelocity = new Vector3(1f, 0f, 0f);
            rb.angularVelocity = Vector3.zero;
            int steps = Mathf.RoundToInt(1f / dt);
            int measure = Mathf.RoundToInt(0.08f / dt);
            float vxMeasured = 1f;
            List<string> rows = new(steps + 2)
            {
                "step,time,px,py,pz,vx,vy,vz,wx,wy,wz,qx,qy,qz,qw,cornerActive,gap",
            };
            for (int step = 0; step <= steps; step++)
            {
                float time = step * dt;
                float gap = rb.position.y - Support(shape, rb.rotation);
                AppendRow(rows, component, step, time, rb, gap);
                if (step == measure)
                    vxMeasured = rb.linearVelocity.x;
                if (step == steps || (step > measure && Mathf.Abs(rb.linearVelocity.x) < 0.01f))
                    break;

                Ctx.Advance.Invoke(component, new object[] { dt, time });
                Physics.Simulate(dt);
                yield return null;
            }

            decel[0] = (1f - vxMeasured) / (measure * dt);
            distance[0] = rb.position.x;
            WriteRows($"cube-floor-{label}.csv", rows);
            Debug.Log($"[FerrofluidCubeFloorSteeringValidation] {label}: decel={decel[0]:F4} distance={distance[0]:F4} rows={rows.Count - 1}");
            body.SetActive(false);
        }

        private static IEnumerator CaseMaterialLifecycle()
        {
            FieldInfo floorField = FloorContactMaterialField();
            FieldInfo cornerField = Ctx.Grabbable.GetField("_cornerMaterial", BindingFlags.Instance | BindingFlags.NonPublic)!;
            const float dt = 0.02f;
            Time.fixedDeltaTime = dt;

            // A flat resting cube runs the floor-contact material; lifting it restores the shared body material.
            Component flat = MakeBody("lifecycle flat", false, Vector3.zero, Quaternion.identity, 0.48f);
            object flatShape = Ctx.GrabShape.GetValue(flat);
            float flatSupport = Support(flatShape, Quaternion.identity);
            flat.transform.position = new Vector3(0f, flatSupport + 0.002f, 0f);
            Rigidbody flatRb = flat.GetComponent<Rigidbody>();
            flatRb.position = flat.transform.position;
            IEnumerator settle = Step(flat, dt, 50);
            while (settle.MoveNext())
                yield return settle.Current;
            PhysicsMaterial floorContact = (PhysicsMaterial)floorField.GetValue(flat)!;
            Collider flatCollider = (Collider)Ctx.CubeCollider.GetValue(flat);
            AssertTrue(
                ReferenceEquals(flatCollider.sharedMaterial, floorContact),
                "flat resting cube must run the owned floor-contact material");
            AssertTrue(
                Mathf.Abs(flatCollider.sharedMaterial!.dynamicFriction - MaterialFriction(0.25f)) < 0.0005f,
                $"floor-contact coefficient must default to the 0.25 multiply ratio (actual={flatCollider.sharedMaterial.dynamicFriction:F4})");
            AssertTrue(
                flatCollider.sharedMaterial!.frictionCombine == PhysicsMaterialCombine.Multiply,
                $"floor-contact material must combine by multiply (actual={flatCollider.sharedMaterial.frictionCombine})");

            flatRb.position = flatRb.position + Vector3.up;
            IEnumerator airborne = Step(flat, dt, 10);
            while (airborne.MoveNext())
                yield return airborne.Current;
            AssertTrue(
                ReferenceEquals(flatCollider.sharedMaterial, Ctx.BodyMaterial),
                "airborne cube must restore the shared body material");

            // Held cubes keep the shared material even flat on the floor.
            flatRb.position = new Vector3(flatRb.position.x, flatSupport + 0.002f, flatRb.position.z);
            IEnumerator resettle = Step(flat, dt, 30);
            while (resettle.MoveNext())
                yield return resettle.Current;
            GameObject pointer = new("steering held pointer");
            Resources.Add(pointer);
            Ctx.Acquire.Invoke(flat, new object[] { pointer.transform, flatRb.position });
            IEnumerator held = Step(flat, dt, 30);
            while (held.MoveNext())
                yield return held.Current;
            AssertTrue(
                ReferenceEquals(flatCollider.sharedMaterial, Ctx.BodyMaterial),
                "held cube must keep the shared body material on the floor");

            // Live config retunes the owned materials without touching the shared set.
            ConfigureFloorPhysics(flat, MakeConfig(0.1f, 0.02f));
            PhysicsMaterial corner = (PhysicsMaterial)cornerField.GetValue(flat)!;
            AssertTrue(
                Mathf.Abs(floorContact.dynamicFriction - MaterialFriction(0.1f)) < 0.0005f && Mathf.Abs(floorContact.staticFriction - MaterialFriction(0.1f)) < 0.0005f,
                $"live config must retune the floor-contact coefficient (actual={floorContact.dynamicFriction:F4})");
            AssertTrue(
                Mathf.Abs(corner.dynamicFriction - 0.02f) < 0.0005f && Mathf.Abs(corner.staticFriction - 0.02f) < 0.0005f,
                $"live config must retune the corner material (actual={corner.dynamicFriction:F4})");
            AssertTrue(
                Mathf.Abs(Ctx.BodyMaterial.dynamicFriction - 0.6f) < 0.0005f && Mathf.Abs(Ctx.FloorMaterial.dynamicFriction - 0.15f) < 0.0005f,
                $"shared contact materials must stay untouched (body={Ctx.BodyMaterial.dynamicFriction:F4} floor={Ctx.FloorMaterial.dynamicFriction:F4})");
            flat.gameObject.SetActive(false);

            // Spheres never touch the cube material set.
            Component sphere = MakeBody("lifecycle sphere", true, new Vector3(2f, 0f, 0f), Quaternion.identity, null);
            sphere.transform.position = new Vector3(2f, Support(Ctx.GrabShape.GetValue(sphere), Quaternion.identity) + 0.002f, 0f);
            Rigidbody sphereRb = sphere.GetComponent<Rigidbody>();
            sphereRb.position = sphere.transform.position;
            IEnumerator sphereSteps = Step(sphere, dt, 30);
            while (sphereSteps.MoveNext())
                yield return sphereSteps.Current;
            Collider sphereCollider = sphere.GetComponentInChildren<Collider>();
            AssertTrue(
                ReferenceEquals(sphereCollider!.sharedMaterial, Ctx.BodyMaterial),
                "spheres must keep the shared body material");
            sphere.gameObject.SetActive(false);
        }

        private static IEnumerator Step(Component component, float dt, int count)
        {
            for (int index = 0; index < count; index++)
            {
                Ctx.Advance.Invoke(component, new object[] { dt, index * dt });
                Physics.Simulate(dt);
                yield return null;
            }
        }

        private static void AppendRow(List<string> rows, Component component, int step, float time, Rigidbody rb, float gap)
        {
            CultureInfo culture = CultureInfo.InvariantCulture;
            Vector3 position = rb.position;
            Vector3 velocity = rb.linearVelocity;
            Vector3 angular = rb.angularVelocity;
            Quaternion pose = rb.rotation;
            bool cornerActive = (bool)Ctx.CornerActive.GetValue(component);
            rows.Add(string.Join(",", new[]
            {
                step.ToString(culture),
                time.ToString(culture),
                position.x.ToString(culture),
                position.y.ToString(culture),
                position.z.ToString(culture),
                velocity.x.ToString(culture),
                velocity.y.ToString(culture),
                velocity.z.ToString(culture),
                angular.x.ToString(culture),
                angular.y.ToString(culture),
                angular.z.ToString(culture),
                pose.x.ToString(culture),
                pose.y.ToString(culture),
                pose.z.ToString(culture),
                pose.w.ToString(culture),
                cornerActive ? "1" : "0",
                gap.ToString(culture),
            }));
        }

        private static void WriteRows(string name, List<string> rows)
        {
            Directory.CreateDirectory(CsvDir);
            File.WriteAllLines(Path.Combine(CsvDir, name), rows);
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                Failures.Add(message);
        }
    }
}
