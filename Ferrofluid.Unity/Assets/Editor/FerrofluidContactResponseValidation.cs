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
    // Gravity, floor-snap and gentle-landing regression: the quick drop must fall by gravity
    // alone through the first contact step, bodies fall at the configured gravity, and a gentle
    // 44-degree tip must reach the nearest face and stay there without repeated rocking.
    internal static class FerrofluidContactResponseValidation
    {
        private static string CsvDir = string.Empty;
        private static readonly List<string> Failures = new();
        private static readonly List<Object> Resources = new();
        private static Context Ctx = null!;

        private sealed class Context
        {
            internal Type Grabbable = null!;
            internal MethodInfo Initialize = null!;
            internal MethodInfo Advance = null!;
            internal MethodInfo ConfigureCubeRoundness = null!;
            internal MethodInfo ConfigureFloorContact = null!;
            internal MethodInfo ApplySphereRolling = null!;
            internal MethodInfo ApplyCubeSpin = null!;
            internal FieldInfo GrabShape = null!;
            internal FieldInfo FloorContactMaterial = null!;
            internal FieldInfo CornerMaterial = null!;
            internal MethodInfo Extent = null!;
            internal float Gravity;
            internal object Enclosure = null!;
            internal object EnclosureNarrow = null!;
            internal object Ownership = null!;
            internal PhysicsMaterial FloorMaterial = null!;
            internal PhysicsMaterial BodyMaterial = null!;
            internal Collider FloorCollider = null!;
            internal Type Config = null!;
            internal Type Physics = null!;
            internal ConstructorInfo EnclosureCtor = null!;
            internal MethodInfo EnforceMethod = null!;
        }

        public static void RunWallBite()
        {
            Environment.SetEnvironmentVariable("FERROFLUID_CONTACT_CASES", "bite");
            Run();
        }

        public static void Run()
            => FerrofluidValidationRunner.Run(typeof(FerrofluidContactResponseValidation));

        public static void RunQuickLandingRed()
        {
            Environment.SetEnvironmentVariable("FERROFLUID_CONTACT_CASES", "quick");
            Run();
        }

        private static IEnumerator RunRuntime()
        {
            SimulationMode previousMode = Physics.simulationMode;
            float previousDelta = Time.fixedDeltaTime;
            Physics.simulationMode = SimulationMode.Script;
            Failures.Clear();
            Resources.Clear();
            Ctx = new Context();
            try
            {
                string repository = Directory.GetParent(Application.dataPath)!.Parent!.FullName;
                string pluginPath = Environment.GetEnvironmentVariable("FERROFLUID_VALIDATION_PLUGIN")
                    ?? Path.Combine(repository, "ChromaGLS/bin/Release-1.44.1/net48/ChromaGLS.dll");
                CsvDir = Environment.GetEnvironmentVariable("FERROFLUID_VALIDATION_OUTPUT")
                    ?? Path.Combine(Directory.GetParent(repository)!.FullName, "Scratch", "ferro-gravity-wall-landing", "validation");
                Debug.Log($"[FerrofluidContactResponseValidation] dll={pluginPath} csv={CsvDir}");
                Assembly plugin = Assembly.LoadFrom(pluginPath);
                Ctx.Grabbable = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true);
                Ctx.Initialize = Ctx.Grabbable.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.Advance = Ctx.Grabbable.GetMethod("AdvancePhysics", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.ConfigureCubeRoundness = Ctx.Grabbable.GetMethod("ConfigureCubeRoundness", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.ConfigureFloorContact = Ctx.Grabbable.GetMethod("ConfigureFloorContact", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.ApplySphereRolling = Ctx.Grabbable.GetMethod("ApplySphereRollingResistance", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.ApplyCubeSpin = Ctx.Grabbable.GetMethod("ApplyCubeFloorSpinResistance", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.GrabShape = Ctx.Grabbable.GetField("_grabShape", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.FloorContactMaterial = Ctx.Grabbable.GetField("_floorContactMaterial", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.CornerMaterial = Ctx.Grabbable.GetField("_cornerMaterial", BindingFlags.Instance | BindingFlags.NonPublic);
                Type physics = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabPhysics", true);
                Ctx.Physics = physics;
                Ctx.Extent = physics.GetMethod("EvaluateSupportExtent", BindingFlags.Static | BindingFlags.NonPublic);
                Ctx.Gravity = (float)physics.GetField("GravityMagnitude", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(null)!;
                Ctx.Config = plugin.GetType("ChromaGLS.Settings.Config", true);
                Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true);
                ConstructorInfo enclosureCtor = enclosureType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null,
                    new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                    null)!;
                Ctx.EnclosureCtor = enclosureCtor;
                Ctx.EnforceMethod = physics.GetMethod("EnforceEnclosureFromMassFrame", BindingFlags.Static | BindingFlags.NonPublic)!;
                Ctx.Enclosure = enclosureCtor.Invoke(new object[] { Vector3.zero, 100f, new Vector3(0f, 0f, -100f), Vector3.forward, 0f });
                Ctx.EnclosureNarrow = enclosureCtor.Invoke(new object[] { Vector3.zero, 3f, new Vector3(0f, 0f, -3f), Vector3.forward, 0f });
                Ctx.Ownership = Activator.CreateInstance(plugin.GetType("ChromaGLS.Settings.FerrofluidGrabOwnership", true), nonPublic: true);
                Type manager = plugin.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true);
                MethodInfo factory = manager.GetMethod("CreateContactMaterial", BindingFlags.Static | BindingFlags.NonPublic);
                Ctx.FloorMaterial = (PhysicsMaterial)factory!.Invoke(null, new object[] { true });
                Ctx.BodyMaterial = (PhysicsMaterial)factory.Invoke(null, new object[] { false });
                Resources.Add(Ctx.FloorMaterial);
                Resources.Add(Ctx.BodyMaterial);

                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Resources.Add(floor);
                floor.name = "contact response floor";
                floor.layer = 3;
                floor.transform.localScale = new Vector3(40f, 1f, 40f);
                floor.transform.position = new Vector3(0f, -0.5f, 0f);
                Ctx.FloorCollider = floor.GetComponent<Collider>();
                Ctx.FloorCollider.material = Ctx.FloorMaterial;

                foreach (float dt in new[] { 0.02f, 1f / 90f })
                {
                    IEnumerator quick = CaseQuickLanding(dt);
                    while (quick.MoveNext())
                        yield return quick.Current;
                }

                string biteMode = Environment.GetEnvironmentVariable("FERROFLUID_CONTACT_CASES")!;
                if (biteMode != "quick")
                {
                    foreach (float dt in new[] { 0.02f, 1f / 90f })
                    {
                        foreach (float yaw in new[] { 0f, 60f })
                        {
                            foreach (bool rear in new[] { false, true })
                            {
                                IEnumerator bite = CaseWallBite(yaw, rear, dt);
                                while (bite.MoveNext())
                                    yield return bite.Current;
                            }
                        }
                    }
                }

                if (biteMode != "quick" && biteMode != "bite")
                {
                    foreach (float dt in new[] { 0.02f, 1f / 90f })
                    {
                        foreach (bool sphere in new[] { true, false })
                        {
                            foreach (float mass in new[] { 1f, 10f })
                            {
                                IEnumerator drop = CaseGravity(sphere, mass, dt);
                                while (drop.MoveNext())
                                    yield return drop.Current;
                            }
                        }
                    }

                    bool repOnly = Environment.GetEnvironmentVariable("FERROFLUID_GENTLE") != "all";
                    (string face, Vector3 dir)[] faces =
                    {
                        ("up", Vector3.up), ("down", Vector3.down), ("left", Vector3.left),
                        ("right", Vector3.right), ("forward", Vector3.forward), ("back", Vector3.back),
                    };
                    foreach (float dt in new[] { 0.02f, 1f / 90f })
                    {
                        foreach ((string face, Vector3 dir) in faces)
                        {
                            foreach (float yaw in new[] { 0f, 37f })
                            {
                                foreach (float sign in new[] { 1f, -1f })
                                {
                                    if (repOnly && (face != "up" || yaw != 0f))
                                        continue;

                                    IEnumerator gentle = CaseGentle(face, dir, yaw, sign, dt);
                                    while (gentle.MoveNext())
                                        yield return gentle.Current;
                                }
                            }
                        }
                    }

                    IEnumerator assistSphere = CaseAssistOffSphere();
                    while (assistSphere.MoveNext())
                        yield return assistSphere.Current;
                    IEnumerator assistCube = CaseAssistOffCube();
                    while (assistCube.MoveNext())
                        yield return assistCube.Current;
                    IEnumerator assistQuick = CaseAssistOffQuick();
                    while (assistQuick.MoveNext())
                        yield return assistQuick.Current;

                    foreach (float dt in new[] { 0.02f, 1f / 90f })
                    {
                        foreach (bool sphere in new[] { true, false })
                        {
                            foreach (bool curved in new[] { true, false })
                            {
                                foreach (bool spin in new[] { false, true })
                                {
                                    IEnumerator wall = CaseWall(curved, sphere, spin, dt);
                                    while (wall.MoveNext())
                                        yield return wall.Current;
                                }
                            }
                        }
                    }

                    IEnumerator bounce = CaseFloorBounce();
                    while (bounce.MoveNext())
                        yield return bounce.Current;
                    IEnumerator rest = CaseWallRest();
                    while (rest.MoveNext())
                        yield return rest.Current;
                }

                foreach (string failure in Failures)
                    Debug.LogWarning($"[FerrofluidContactResponseValidation] FAIL: {failure}");

                if (Failures.Count > 0)
                    throw new Exception($"{Failures.Count} contact response cases failed: {string.Join(" | ", Failures)}");

                Debug.Log("[FerrofluidContactResponseValidation] PASS: gravity, snap budget, gentle landing verified.");
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

        private static Component MakeBody(string name, bool sphere, Vector3 position, Quaternion rotation, float? roundness, object? enclosure = null)
        {
            GameObject body = new(name);
            Resources.Add(body);
            body.transform.position = position;
            body.transform.rotation = rotation;
            body.transform.localScale = Vector3.one * 0.55f;
            Component component = body.AddComponent(Ctx.Grabbable);
            Ctx.Initialize.Invoke(component, new object[] { enclosure ?? Ctx.Enclosure, sphere, Ctx.BodyMaterial, Ctx.Ownership });
            Ctx.ConfigureFloorContact.Invoke(component, new object[] { Ctx.FloorCollider });
            if (!sphere && roundness.HasValue)
                Ctx.ConfigureCubeRoundness.Invoke(component, new object[] { roundness.Value });

            ((MonoBehaviour)component).enabled = false;
            return component;
        }

        private static float Support(object shape, Quaternion rotation, Vector3 direction)
            => (float)Ctx.Extent.Invoke(null, new object[] { shape, rotation, direction });

        private static void Configure(Component component, bool assistance)
        {
            MethodInfo configure = Ctx.Grabbable.GetMethod("ConfigureContactPhysics", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new Exception("FerrofluidGrabbable must own ConfigureContactPhysics.");
            object config = Activator.CreateInstance(Ctx.Config, nonPublic: true)!;
            Ctx.Config.GetProperty("FerrofluidFloorAssistanceEnabled")!.SetValue(config, assistance);
            configure.Invoke(component, new[] { config });
        }

        private static IEnumerator CaseQuickLanding(float dt)
        {
            Quaternion rotation = Quaternion.Euler(0f, 0f, 6f);
            string label = $"quick6-dt{dt:0.0000}";
            Component component = MakeBody(label, false, Vector3.zero, rotation, 0.48f);
            object shape = Ctx.GrabShape.GetValue(component);
            float support = Support(shape, rotation, Vector3.down);
            Rigidbody rb = component.GetComponent<Rigidbody>();
            component.transform.position = new Vector3(0f, support + 0.002f, 0f);
            rb.position = component.transform.position;
            rb.angularVelocity = new Vector3(0f, 0f, -5f);
            rb.linearVelocity = Vector3.zero;
            Time.fixedDeltaTime = dt;
            float initialY = rb.position.y;
            int steps = Mathf.RoundToInt(0.4f / dt);
            float worstAngle = 0f;
            float worstAdvance = 0f;
            float worstRender = 0f;
            float firstStepY = float.NaN;
            Quaternion previousPose = rb.rotation;
            Quaternion previousRender = component.transform.rotation;
            List<string> rows = LandingHeader(steps);
            for (int step = 0; step <= steps; step++)
            {
                float time = step * dt;
                float preGap = rb.position.y - Support(shape, rb.rotation, Vector3.down);
                Vector3 preAngular = rb.angularVelocity;
                if (step == steps)
                    break;

                rows.Add(PreRow(step, time, rb, preGap));
                Ctx.Advance.Invoke(component, new object[] { dt, time });
                Quaternion advancePose = rb.rotation;
                worstAdvance = Mathf.Max(worstAdvance, Quaternion.Angle(previousPose, advancePose) - 0.001f);
                rows[rows.Count - 1] += Triple(rb);
                Physics.Simulate(dt);
                float postGap = rb.position.y - Support(shape, rb.rotation, Vector3.down);
                rows[rows.Count - 1] += Triple(rb) + Quad(rb) + F(postGap);
                if (step == 0)
                    firstStepY = rb.position.y;

                yield return null;
                Quaternion render = component.transform.rotation;
                rows[rows.Count - 1] += QuadValues(render);

                float stepAngle = Quaternion.Angle(previousPose, rb.rotation);
                float bound = Mathf.Max(preAngular.magnitude, rb.angularVelocity.magnitude) * dt * Mathf.Rad2Deg + 0.25f;
                worstAngle = Mathf.Max(worstAngle, stepAngle - bound);
                worstRender = Mathf.Max(worstRender, Quaternion.Angle(previousRender, render) - bound);
                previousPose = rb.rotation;
                previousRender = render;
            }

            WriteRows($"contact-{label}.csv", rows);
            float expected = initialY - Ctx.Gravity * dt * dt;
            float actual = firstStepY;
            Debug.Log($"[FerrofluidContactResponseValidation] {label}: firstStepY={actual:F5} expected={expected:F5} initialY={initialY:F5} worstAngleOver={worstAngle:F4} worstAdvanceOver={worstAdvance:F4} worstRenderOver={worstRender:F4}");
            AssertTrue(
                Mathf.Abs(actual - expected) < 0.001f,
                $"{label} first step must fall by gravity only (actual={actual:F5} expected={expected:F5})");
            AssertTrue(
                worstAngle <= 0f,
                $"{label} pose step exceeded angular bound by {worstAngle:F4} deg");
            AssertTrue(
                worstAdvance <= 0f,
                $"{label} Advance moved the pose by {worstAdvance:F4} deg");
            AssertTrue(
                worstRender <= 0f,
                $"{label} rendered pose step exceeded angular bound by {worstRender:F4} deg");
            component.gameObject.SetActive(false);
        }

        private static IEnumerator CaseGravity(bool sphere, float mass, float dt)
        {
            string label = $"gravity-{(sphere ? "sphere" : "cube")}-m{mass:0}-dt{dt:0.0000}";
            Component component = MakeBody(label, sphere, new Vector3(0f, 5f, 0f), Quaternion.identity, sphere ? (float?)null : 0.48f);
            Rigidbody rb = component.GetComponent<Rigidbody>();
            rb.position = component.transform.position;
            rb.mass = mass;
            rb.angularVelocity = Vector3.zero;
            rb.linearVelocity = Vector3.zero;
            Time.fixedDeltaTime = dt;
            int steps = Mathf.RoundToInt(0.4f / dt);
            List<string> rows = LandingHeader(steps);
            for (int step = 0; step <= steps; step++)
            {
                float time = step * dt;
                if (step == steps)
                {
                    break;
                }

                rows.Add(PreRow(step, time, rb, rb.position.y - Support(Ctx.GrabShape.GetValue(component), rb.rotation, Vector3.down)));
                Ctx.Advance.Invoke(component, new object[] { dt, time });
                rows[rows.Count - 1] += Triple(rb);
                Physics.Simulate(dt);
                rows[rows.Count - 1] += Triple(rb) + Quad(rb) + F(0f) + QuadValues(component.transform.rotation);
                yield return null;
            }

            WriteRows($"contact-{label}.csv", rows);
            float expectedVy = -Ctx.Gravity * steps * dt;
            float expectedY = 5f - Ctx.Gravity * dt * dt * steps * (steps + 1f) / 2f;
            Debug.Log($"[FerrofluidContactResponseValidation] {label}: vy={rb.linearVelocity.y:F4}/{expectedVy:F4} y={rb.position.y:F5}/{expectedY:F5} drag={rb.linearDamping:F3}");
            AssertTrue(Mathf.Abs(rb.linearVelocity.y - expectedVy) < 0.001f, $"{label} vy={rb.linearVelocity.y:F4} expected {expectedVy:F4}");
            AssertTrue(Mathf.Abs(rb.position.y - expectedY) < 0.002f, $"{label} y={rb.position.y:F5} expected {expectedY:F5}");
            AssertTrue(rb.linearDamping == 0f, $"{label} linear damping must stay zero (drag={rb.linearDamping:F3})");
            component.gameObject.SetActive(false);
        }

        private static IEnumerator CaseGentle(string face, Vector3 dir, float yaw, float sign, float dt)
        {
            Quaternion rotation = Quaternion.AngleAxis(yaw, Vector3.up)
                * Quaternion.AngleAxis(sign * 44f, Vector3.forward)
                * Quaternion.FromToRotation(dir, Vector3.up);
            string label = $"gentle-{face}-y{yaw:0}-s{(sign > 0f ? "p" : "n")}-dt{dt:0.0000}";
            Component component = MakeBody(label, false, Vector3.zero, rotation, 0.48f);
            object shape = Ctx.GrabShape.GetValue(component);
            float support = Support(shape, rotation, Vector3.down);
            Rigidbody rb = component.GetComponent<Rigidbody>();
            component.transform.position = new Vector3(0f, support + 0.002f, 0f);
            rb.position = component.transform.position;
            rb.angularVelocity = Vector3.zero;
            rb.linearVelocity = Vector3.zero;
            Time.fixedDeltaTime = dt;
            int steps = Mathf.RoundToInt(3f / dt);
            float arrival = -1f;
            float maxAfterArrival = 0f;
            int episodes = 0;
            bool armed = false;
            bool outEpisode = false;
            float arrivalQuiet = -1f;
            float lateAngle = float.MaxValue;
            float lateSpeed = float.MaxValue;
            float worstStep = 0f;
            float worstAdvance = 0f;
            float worstRender = 0f;
            Quaternion previousPose = rb.rotation;
            Quaternion previousRender = component.transform.rotation;
            List<string> rows = LandingHeader(steps);
            for (int step = 0; step <= steps; step++)
            {
                float time = step * dt;
                Vector3 preAngular = rb.angularVelocity;
                float preGap = rb.position.y - Support(shape, rb.rotation, Vector3.down);
                float angle = Vector3.Angle(rb.rotation * dir, Vector3.up);
                if (arrival < 0f && angle <= 1f)
                {
                    arrival = time;
                    arrivalQuiet = time + 0.5f;
                }

                if (arrival >= 0f)
                {
                    maxAfterArrival = Mathf.Max(maxAfterArrival, angle);
                    if (!armed && angle <= 0.25f)
                        armed = true;
                    if (armed && !outEpisode && angle >= 0.5f)
                        outEpisode = true;
                    if (outEpisode && angle <= 0.25f)
                    {
                        episodes++;
                        outEpisode = false;
                    }

                    if (time >= arrivalQuiet)
                    {
                        lateAngle = Mathf.Max(lateAngle == float.MaxValue ? 0f : lateAngle, angle);
                        lateSpeed = Mathf.Max(lateSpeed == float.MaxValue ? 0f : lateSpeed, rb.angularVelocity.magnitude);
                    }
                }

                if (step == steps)
                    break;

                rows.Add(PreRow(step, time, rb, preGap));
                Ctx.Advance.Invoke(component, new object[] { dt, time });
                Quaternion advancePose = rb.rotation;
                worstAdvance = Mathf.Max(worstAdvance, Quaternion.Angle(previousPose, advancePose) - 0.001f);
                rows[rows.Count - 1] += Triple(rb);
                Physics.Simulate(dt);
                float postGap = rb.position.y - Support(shape, rb.rotation, Vector3.down);
                rows[rows.Count - 1] += Triple(rb) + Quad(rb) + F(postGap);
                yield return null;
                Quaternion render = component.transform.rotation;
                rows[rows.Count - 1] += QuadValues(render);

                float stepAngle = Quaternion.Angle(previousPose, rb.rotation);
                float bound = Mathf.Max(preAngular.magnitude, rb.angularVelocity.magnitude) * dt * Mathf.Rad2Deg + 0.25f;
                worstStep = Mathf.Max(worstStep, stepAngle - bound);
                worstRender = Mathf.Max(worstRender, Quaternion.Angle(previousRender, render) - bound);
                previousPose = rb.rotation;
                previousRender = render;
            }

            if (outEpisode)
                episodes++;

            WriteRows($"contact-{label}.csv", rows);
            Debug.Log($"[FerrofluidContactResponseValidation] {label}: arrival={arrival:F2} maxAfter={maxAfterArrival:F2} episodes={episodes} lateA={lateAngle:F2} lateW={lateSpeed:F2} worstStep={worstStep:F3}");
            AssertTrue(arrival > 0f && arrival <= 2f, $"{label} must reach the face within 2s (arrival={arrival:F2})");
            AssertTrue(maxAfterArrival <= 1.5f, $"{label} overshoot {maxAfterArrival:F2} exceeds 1.5deg");
            AssertTrue(episodes <= 1, $"{label} rocked {episodes} times past 0.5deg");
            AssertTrue(lateAngle <= 0.25f && lateSpeed <= 0.15f, $"{label} still rocking late (angle={lateAngle:F2} w={lateSpeed:F2})");
            AssertTrue(worstStep <= 0f, $"{label} pose step exceeded angular bound by {worstStep:F4} deg");
            AssertTrue(worstAdvance <= 0f, $"{label} Advance moved the pose by {worstAdvance:F4} deg");
            AssertTrue(worstRender <= 0f, $"{label} rendered pose step exceeded angular bound by {worstRender:F4} deg");
            component.gameObject.SetActive(false);
        }

        private static IEnumerator CaseAssistOffSphere()
        {
            Component component = MakeBody("assistoff-sphere", true, new Vector3(0f, 0.2775f, 0f), Quaternion.identity, null);
            Configure(component, false);
            Rigidbody rb = component.GetComponent<Rigidbody>();
            rb.linearVelocity = new Vector3(1f, 0f, 0f);
            rb.angularVelocity = new Vector3(0f, 3f, 0f);
            Vector3 position = rb.position;
            Vector3 velocity = rb.linearVelocity;
            object[] args = { position, velocity, 0.02f };
            Ctx.ApplySphereRolling.Invoke(component, args);
            Physics.Simulate(0.02f);
            yield return null;
            Debug.Log($"[FerrofluidContactResponseValidation] assistoff-sphere: vx={rb.linearVelocity.x:F4} wy={rb.angularVelocity.y:F4} drag={rb.angularDamping:F3}");
            AssertTrue(Mathf.Abs(rb.linearVelocity.x - 1f) < 0.001f, $"assistance off must not brake sphere slide (vx={rb.linearVelocity.x:F4})");
            AssertTrue(Mathf.Abs(rb.angularVelocity.y - 3f) < 0.01f, $"assistance off must not brake sphere twist (wy={rb.angularVelocity.y:F4})");
            AssertTrue(rb.angularDamping == 0f, $"assistance off must leave angular damping at zero ({rb.angularDamping:F3})");
            component.gameObject.SetActive(false);
        }

        private static IEnumerator CaseAssistOffCube()
        {
            Component component = MakeBody("assistoff-cube", false, new Vector3(0f, 0.3f, 0f), Quaternion.identity, 0.48f);
            Configure(component, false);
            Rigidbody rb = component.GetComponent<Rigidbody>();
            rb.angularVelocity = new Vector3(0f, 3f, 0f);
            object[] args = { rb.position, rb.linearVelocity, 0.02f };
            Ctx.ApplyCubeSpin.Invoke(component, args);
            Physics.Simulate(0.02f);
            yield return null;
            Debug.Log($"[FerrofluidContactResponseValidation] assistoff-cube: wy={rb.angularVelocity.y:F4}");
            AssertTrue(Mathf.Abs(rb.angularVelocity.y - 3f) < 0.001f, $"assistance off must skip cube floor spin brake (wy={rb.angularVelocity.y:F4})");
            float restitution = (float)Ctx.Physics.GetField("FloorRestitution", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(null)!;
            PhysicsMaterial flat = (PhysicsMaterial)Ctx.FloorContactMaterial.GetValue(component);
            PhysicsMaterial corner = (PhysicsMaterial)Ctx.CornerMaterial.GetValue(component);
            AssertTrue(
                Mathf.Abs(Ctx.FloorMaterial.bounciness - restitution) < 0.0005f
                && Mathf.Abs(Ctx.BodyMaterial.bounciness - restitution) < 0.0005f
                && Mathf.Abs(flat.bounciness - restitution) < 0.0005f
                && Mathf.Abs(corner.bounciness - restitution) < 0.0005f,
                $"all contact materials must share the {restitution:F2} restitution (floor={Ctx.FloorMaterial.bounciness} body={Ctx.BodyMaterial.bounciness} flat={flat.bounciness} corner={corner.bounciness})");
            AssertTrue(
                Ctx.FloorMaterial.bounceCombine == PhysicsMaterialCombine.Average
                && Ctx.BodyMaterial.bounceCombine == PhysicsMaterialCombine.Average
                && flat.bounceCombine == PhysicsMaterialCombine.Average
                && corner.bounceCombine == PhysicsMaterialCombine.Average,
                "bounce combine must stay Average on every contact material");
            component.gameObject.SetActive(false);
        }

        private static IEnumerator CaseAssistOffQuick()
        {
            const float dt = 0.02f;
            Quaternion rotation = Quaternion.Euler(0f, 0f, 6f);
            Component component = MakeBody("assistoff-quick6", false, Vector3.zero, rotation, 0.48f);
            Configure(component, false);
            object shape = Ctx.GrabShape.GetValue(component);
            float support = Support(shape, rotation, Vector3.down);
            Rigidbody rb = component.GetComponent<Rigidbody>();
            component.transform.position = new Vector3(0f, support + 0.002f, 0f);
            rb.position = component.transform.position;
            rb.angularVelocity = new Vector3(0f, 0f, -5f);
            Time.fixedDeltaTime = dt;
            float initialY = rb.position.y;
            Ctx.Advance.Invoke(component, new object[] { dt, 0f });
            Physics.Simulate(dt);
            yield return null;
            float expected = initialY - Ctx.Gravity * dt * dt;
            Debug.Log($"[FerrofluidContactResponseValidation] assistoff-quick6: y={rb.position.y:F5} expected={expected:F5}");
            AssertTrue(Mathf.Abs(rb.position.y - expected) < 0.001f, $"assistance off must remove the snap (y={rb.position.y:F5} expected={expected:F5})");
            component.gameObject.SetActive(false);
        }

        private static IEnumerator CaseWall(bool curved, bool sphere, bool spin, float dt)
        {
            Quaternion rotation = sphere ? Quaternion.identity : Quaternion.Euler(17f, 23f, 11f);
            Vector3 angular = spin ? new Vector3(2f, 4f, 1f) : Vector3.zero;
            string label = string.Format(
                CultureInfo.InvariantCulture,
                "wall-{0}-{1}-w{2}-dt{3:0.0000}",
                curved ? "curved" : "rear",
                sphere ? "sphere" : "cube",
                spin ? "241" : "0",
                dt);
            Component component = MakeBody(label, sphere, Vector3.zero, rotation, sphere ? (float?)null : 0.48f, Ctx.EnclosureNarrow);
            object shape = Ctx.GrabShape.GetValue(component);
            Vector3 outward = curved ? Vector3.right : Vector3.back;
            Vector3 tangent = curved ? Vector3.forward : Vector3.right;
            float support = Support(shape, rotation, outward);
            Vector3 center = curved
                ? new Vector3(3f - support - 0.02f, 5f, 0f)
                : new Vector3(0f, 5f, -3f + support + 0.02f);
            Vector3 velocity = outward * 4f + tangent * 0.5f + Vector3.up * 0.2f;
            Rigidbody rb = component.GetComponent<Rigidbody>();
            component.transform.position = center;
            rb.position = center;
            rb.angularVelocity = angular;
            rb.linearVelocity = velocity;
            Time.fixedDeltaTime = dt;
            int steps = Mathf.RoundToInt(0.4f / dt);
            float advNormal = float.NaN;
            float advTangent = float.NaN;
            float advVertical = float.NaN;
            Vector3 expectedVelocity = Vector3.zero;
            Vector3 expectedAngular = Vector3.zero;
            Vector3 actualVelocity = Vector3.zero;
            Vector3 actualAngular = Vector3.zero;
            List<string> rows = StartRows();
            for (int step = 0; step <= steps; step++)
            {
                if (step == steps)
                    break;

                Vector3 preVelocity = rb.linearVelocity;
                Vector3 preAngular = rb.angularVelocity;
                Vector3 prePosition = rb.position;
                Quaternion preRotation = rb.rotation;
                Vector3 preCom = rb.centerOfMass;
                rows.Add(PreRow(step, step * dt, rb, 0f));
                rows[rows.Count - 1] = rows[rows.Count - 1].Substring(0, rows[rows.Count - 1].LastIndexOf(','));

                Ctx.Advance.Invoke(component, new object[] { dt, step * dt });
                rows[rows.Count - 1] += Triple(rb);
                if (step == 0)
                {
                    advNormal = Vector3.Dot(rb.linearVelocity, outward);
                    advTangent = Vector3.Dot(rb.linearVelocity, tangent);
                    advVertical = rb.linearVelocity.y;
                    if (sphere)
                    {
                        object[] enforceArgs =
                        new object[] {
                            Ctx.EnclosureNarrow,
                            shape,
                            prePosition,
                            preRotation,
                            preVelocity,
                            preAngular,
                            preCom,
                            Vector3.down * (Ctx.Gravity * dt),
                            dt,
                            (float)Ctx.Physics.GetField("EnclosureRestitution", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(null)!,
                            Vector3.zero,
                            Vector3.zero,
                        };
                        Ctx.EnforceMethod.Invoke(null, enforceArgs);
                        Vector3 reflected = (Vector3)enforceArgs[11]!;
                        Vector3 normalDelta = reflected - preVelocity;
                        normalDelta.y = 0f;
                        Vector3 outwardNormal = -normalDelta.normalized;
                        Vector3 contactArm = outwardNormal * 0.275f;
                        Vector3 slip = reflected + Vector3.Cross(preAngular, contactArm);
                        slip -= outwardNormal * Vector3.Dot(slip, outwardNormal);
                        Vector3 frictionDelta = Vector3.ClampMagnitude(
                            -slip / 3.5f,
                            0.6f * normalDelta.magnitude);
                        expectedVelocity = reflected + frictionDelta;
                        expectedAngular = preAngular + (Vector3.Cross(contactArm, frictionDelta) / (0.4f * 0.275f * 0.275f));
                        actualVelocity = rb.linearVelocity;
                        actualAngular = rb.angularVelocity;
                    }
                }

                Physics.Simulate(dt);
                rows[rows.Count - 1] += Triple(rb) + Quad(rb);
                yield return null;
            }

            WriteRows($"contact-{label}.csv", rows);
            float finalNormal = Vector3.Dot(rb.linearVelocity, outward);
            float expectedRecoil = -4f * (float)Ctx.Physics.GetField("EnclosureRestitution", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(null)!;
            Debug.Log($"[FerrofluidContactResponseValidation] {label}: advN={advNormal:F4} advT={advTangent:F4} advV={advVertical:F4} finalN={finalNormal:F4}");
            AssertTrue(
                Mathf.Abs(advNormal - expectedRecoil) < (curved ? 0.05f : 0.001f),
                $"{label} first Advance normal recoil {advNormal:F4} expected {expectedRecoil:F2}");
            if (sphere)
            {
                // Wall friction consumes tangent slip and roll; the exact outcome must match the
                // shared contact helper applied to the same reflected state.
                AssertTrue(
                    Vector3.Distance(actualVelocity, expectedVelocity) < 0.005f,
                    $"{label} sphere velocity {actualVelocity} expected {expectedVelocity}");
                AssertTrue(
                    Vector3.Distance(actualAngular, expectedAngular) < 0.005f,
                    $"{label} sphere spin {actualAngular} expected {expectedAngular}");
            }
            else
            {
                AssertTrue(
                    Mathf.Abs(advTangent - 0.5f) < 0.05f,
                    $"{label} cube tangent {advTangent:F4} expected 0.5");
                AssertTrue(
                    Mathf.Abs(advVertical - 0.2f) < 0.001f,
                    $"{label} cube vertical before Simulate {advVertical:F4} must stay unchanged");
            }
            AssertTrue(
                finalNormal < 0f,
                $"{label} recoil must remain inward at 0.4s (n={finalNormal:F4})");
            component.gameObject.SetActive(false);
        }

        private static IEnumerator CaseFloorBounce()
        {
            const float dt = 0.02f;
            Component component = MakeBody("bounce-sphere", true, new Vector3(0f, 0.295f, 0f), Quaternion.identity, null);
            Rigidbody rb = component.GetComponent<Rigidbody>();
            rb.linearVelocity = new Vector3(0f, -4f, 0f);
            Time.fixedDeltaTime = dt;
            float rebound = 0f;
            for (int step = 0; step < 20; step++)
            {
                Ctx.Advance.Invoke(component, new object[] { dt, step * dt });
                Physics.Simulate(dt);
                rebound = Mathf.Max(rebound, rb.linearVelocity.y);
                yield return null;
            }

            float expected = 4f * (float)Ctx.Physics.GetField("FloorRestitution", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(null)!;
            Debug.Log($"[FerrofluidContactResponseValidation] bounce-sphere: rebound={rebound:F4} expected={expected:F3}");
            AssertTrue(
                Mathf.Abs(rebound - expected) < 0.15f,
                $"floor bounce rebound {rebound:F4} expected {expected:F3}");
            component.gameObject.SetActive(false);
        }

        // A forward-rolling sphere meeting the front wall must bite upward and bleed roll:
        // contact friction transfers part of the roll into a vertical kick while the normal
        // response reflects the approach velocity. The cube path stays untouched.
        private static IEnumerator CaseWallBite(float yawDegrees, bool rear, float dt)
        {
            const float radius = 0.275f;
            Quaternion yaw = Quaternion.Euler(0f, yawDegrees, 0f);
            Vector3 forward = yaw * Vector3.forward;
            Vector3 approach = rear ? -forward : forward;
            object enclosure = Ctx.EnclosureNarrow;
            if (yawDegrees != 0f)
            {
                Vector3 rearPoint = Vector3.zero - (forward * 3f);
                enclosure = Ctx.EnclosureCtor.Invoke(new object[] { Vector3.zero, 3f, rearPoint, forward, 0f });
            }

            string label = $"bite-{(rear ? "rear" : "front")}-yaw{yawDegrees:0}-dt{dt:0.0000}";
            Component component = MakeBody(label, true, Vector3.zero, Quaternion.identity, null, enclosure);
            Rigidbody rb = component.GetComponent<Rigidbody>();
            component.transform.position = new Vector3(0f, radius + 0.002f, 0f) + approach * (3f - radius - 0.03f);
            rb.position = component.transform.position;
            rb.angularVelocity = Vector3.Cross(Vector3.up, approach) * (2f / radius);
            rb.linearVelocity = approach * 2f;
            Time.fixedDeltaTime = dt;
            int steps = Mathf.RoundToInt(0.7f / dt);
            float vyBefore = 0f;
            float wxBefore = 0f;
            float vyAfter = 0f;
            float wxAfter = 0f;
            float vzAfter = 2f;
            int impactStep = -1;
            Vector3 impactPos = Vector3.zero;
            Vector3 outward = -approach;
            float peakClearance = 0f;
            float exitDistance = 0f;
            bool secondBounce = false;
            int stopStep = steps;
            List<string> rows = StartRows();
            for (int step = 0; step <= steps; step++)
            {
                if (step == steps)
                    break;

                float preVz = Vector3.Dot(rb.linearVelocity, approach);
                float preVy = rb.linearVelocity.y;
                float preWx = Vector3.Dot(rb.angularVelocity, Vector3.Cross(Vector3.up, approach));
                rows.Add(PreRow(step, step * dt, rb, 0f));
                Ctx.Advance.Invoke(component, new object[] { dt, step * dt });
                float postAdvanceNormal = Vector3.Dot(rb.linearVelocity, approach);
                rows[rows.Count - 1] += Triple(rb);
                Physics.Simulate(dt);
                rows[rows.Count - 1] += Triple(rb);
                if (impactStep >= 0 && preVz > 0f && postAdvanceNormal < 0f)
                    secondBounce = true;
                Vector3 horizontal = rb.position;
                horizontal.y = 0f;
                AssertTrue(
                    horizontal.magnitude <= 3f - radius + 0.01f,
                    $"{label} containment failed at step {step} (radial={horizontal.magnitude:F3})");
                if (impactStep < 0 && postAdvanceNormal < preVz - 0.05f)
                {
                    impactStep = step;
                    vyBefore = preVy;
                    wxBefore = preWx;
                    vyAfter = rb.linearVelocity.y;
                    wxAfter = Vector3.Dot(rb.angularVelocity, Vector3.Cross(Vector3.up, approach));
                    vzAfter = Vector3.Dot(rb.linearVelocity, approach);
                    impactPos = rb.position;
                    stopStep = impactStep + Mathf.CeilToInt(0.5f / dt);
                }
                else if (impactStep >= 0)
                {
                    if ((step - impactStep) * dt <= 0.15f)
                        peakClearance = Mathf.Max(peakClearance, rb.position.y - radius);
                    if ((step - impactStep) * dt <= 0.3f)
                        exitDistance = Mathf.Max(exitDistance, Vector3.Dot(rb.position - impactPos, outward));
                }

                if (step >= stopStep)
                    break;
                yield return null;
            }

            WriteRows($"contact-{label}.csv", rows);
            Debug.Log($"[FerrofluidContactResponseValidation] {label}: step={impactStep} vy={vyBefore:F3}->{vyAfter:F3} wx={wxBefore:F3}->{wxAfter:F3} vz={vzAfter:F3} clear={peakClearance:F3} away={exitDistance:F3}");
            AssertTrue(impactStep >= 0, $"{label} must reach the wall");
            float biteImpulse = vyAfter - vyBefore + Ctx.Gravity * dt;
            AssertTrue(
                biteImpulse >= 0.45f,
                $"{label} wall contact must bite upward >=0.45 impulse (actual={biteImpulse:F3})");
            AssertTrue(
                wxAfter < wxBefore * 0.6f,
                $"{label} rolling contact must consume >=40% of the roll (wx={wxBefore:F2}->{wxAfter:F2})");
            AssertTrue(vzAfter < 0f, $"{label} must reflect off the wall (vz={vzAfter:F3})");
            AssertTrue(
                peakClearance > 0.002f,
                $"{label} must clear the floor after the bite (peak={peakClearance:F4})");
            AssertTrue(
                exitDistance >= 0.03f,
                $"{label} must move away from the wall within 0.3s (away={exitDistance:F3})");
            AssertTrue(
                !secondBounce,
                $"{label} must not strike the wall a second time within 0.5s");
            component.gameObject.SetActive(false);
        }

        private static IEnumerator CaseWallRest()
        {
            const float dt = 0.02f;
            Component component = MakeBody("wallrest-sphere", true, new Vector3(3f - 0.275f, 5f, 0f), Quaternion.identity, null, Ctx.EnclosureNarrow);
            Rigidbody rb = component.GetComponent<Rigidbody>();
            rb.linearVelocity = new Vector3(0f, 2f, 1f);
            object[] args = { rb.position, rb.linearVelocity, dt };
            Ctx.ApplySphereRolling.Invoke(component, args);
            yield return null;
            Vector3 result = (Vector3)args[1];
            float keep = Mathf.Exp(-0.125f * dt);
            Debug.Log($"[FerrofluidContactResponseValidation] wallrest-sphere: v={result} drag={rb.angularDamping:F3} keep={keep:F4}");
            AssertTrue(
                Mathf.Abs(result.y - 2f * keep) < 0.001f && Mathf.Abs(result.z - keep) < 0.001f && Mathf.Abs(result.x) < 0.001f,
                $"wall rest tangent must damp at 0.125 (v={result})");
            AssertTrue(
                Mathf.Abs(rb.angularDamping - 0.125f) < 0.0001f,
                $"wall rest angular damping must be 0.125 not 1 ({rb.angularDamping:F3})");
            component.gameObject.SetActive(false);
        }

        private static List<string> StartRows()
            => new()
            {
                "step,time,prepx,prepy,prepz,prevx,prevy,prevz,prewx,prewy,prewz,preqx,preqy,preqz,preqw,advpx,advpy,advpz,advvx,advvy,advvz,advwx,advwy,advwz,postpx,postpy,postpz,postvx,postvy,postvz,postwx,postwy,postwz,postqx,postqy,postqz,postqw",
            };

        private static List<string> LandingHeader(int steps)
            => new(steps + 2)
            {
                "step,time,prepx,prepy,prepz,prevx,prevy,prevz,prewx,prewy,prewz,preqx,preqy,preqz,preqw,pregap,advpx,advpy,advpz,advvx,advvy,advvz,advwx,advwy,advwz,postpx,postpy,postpz,postvx,postvy,postvz,postwx,postwy,postwz,postqx,postqy,postqz,postqw,postgap,renderqx,renderqy,renderqz,renderqw",
            };

        private static string PreRow(int step, float time, Rigidbody rb, float gap)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            Vector3 p = rb.position;
            Vector3 v = rb.linearVelocity;
            Vector3 w = rb.angularVelocity;
            Quaternion q = rb.rotation;
            return string.Join(",", new[]
            {
                step.ToString(c), time.ToString(c),
                p.x.ToString(c), p.y.ToString(c), p.z.ToString(c),
                v.x.ToString(c), v.y.ToString(c), v.z.ToString(c),
                w.x.ToString(c), w.y.ToString(c), w.z.ToString(c),
                q.x.ToString(c), q.y.ToString(c), q.z.ToString(c), q.w.ToString(c),
                gap.ToString(c),
            });
        }

        private static string Triple(Rigidbody rb)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            Vector3 p = rb.position;
            Vector3 v = rb.linearVelocity;
            Vector3 w = rb.angularVelocity;
            return "," + string.Join(",", new[]
            {
                p.x.ToString(c), p.y.ToString(c), p.z.ToString(c),
                v.x.ToString(c), v.y.ToString(c), v.z.ToString(c),
                w.x.ToString(c), w.y.ToString(c), w.z.ToString(c),
            });
        }

        private static string Quad(Rigidbody rb)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            Quaternion q = rb.rotation;
            return "," + string.Join(",", new[] { q.x.ToString(c), q.y.ToString(c), q.z.ToString(c), q.w.ToString(c) });
        }

        private static string QuadValues(Quaternion q)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            return "," + string.Join(",", new[] { q.x.ToString(c), q.y.ToString(c), q.z.ToString(c), q.w.ToString(c) });
        }

        private static string F(float value)
            => "," + value.ToString(CultureInfo.InvariantCulture);

        private static void WriteRows(string name, List<string> rows)
        {
            Directory.CreateDirectory(CsvDir);
            string path = Path.Combine(CsvDir, name);
            File.WriteAllLines(path, rows);
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                Failures.Add(message);
        }
    }
}
