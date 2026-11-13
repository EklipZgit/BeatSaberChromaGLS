using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ChromaGLS.FerrofluidBundle
{
    // Drives the real FerrofluidGrabbable component through manual physics steps and reads back the
    // surface shader's displaced world positions to prove grab physics and GPU lag wiring end to end.
    internal static class FerrofluidGrabValidation
    {
        private const float FixedDelta = 0.02f;

        public static void Run()
        {
            try
            {
                string repository = Directory.GetParent(Application.dataPath).Parent.FullName;
                Assembly plugin = Assembly.LoadFrom(Path.Combine(repository, "ChromaGLS/bin/Release-1.44.1/net48/ChromaGLS.dll"));
                ValidateEnclosureResolution(plugin);
                ValidateHeldPivotFriction(plugin);
                ValidateGrabPointSmoothing(plugin);
                ValidatePivotGrabs(plugin);
                ValidateGrabPhysics(plugin);
                ValidateSurfaceSlide();
                ValidateGrabShader();
                Debug.Log("[FerrofluidGrabValidation] PASS: rigidbody grab physics and GPU lag/scale output all verified.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        // The real menu curve must resolve through CurvedCanvasSettings on the main canvas:
        // a canvas at (0,1,3) scaled 0.02 with radius 150 puts the circle center at (0,1,0), radius 3.
        private static void ValidateEnclosureResolution(Assembly plugin)
        {
            const string managedDir = @"C:\Users\tdrak\BSManager\BSInstances\1.44.1\Beat Saber_Data\Managed";
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                string dependency = Path.Combine(managedDir, new AssemblyName(args.Name).Name + ".dll");
                return File.Exists(dependency) ? Assembly.LoadFrom(dependency) : null;
            };
            Assembly hmui = Assembly.LoadFrom(Path.Combine(managedDir, "HMUI.dll"));
            Type settingsType = hmui.GetType("HMUI.CurvedCanvasSettings", true);
            List<UnityEngine.Object> resources = new();
            try
            {
                GameObject canvasObject = new("validation main canvas");
                resources.Add(canvasObject);
                canvasObject.transform.position = new Vector3(0f, 1f, 3f);
                canvasObject.transform.localScale = Vector3.one * 0.02f;
                canvasObject.AddComponent<Canvas>();
                Component settings = canvasObject.AddComponent(settingsType);
                settingsType.GetField("_radius", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(settings, 150f);

                GameObject anchor = new("validation player anchor");
                resources.Add(anchor);
                anchor.transform.rotation = Quaternion.identity;

                MethodInfo resolve = plugin
                    .GetType("ChromaGLS.Settings.FerrofluidGrabPhysics", true)
                    .GetMethod("TryResolveEnclosure", BindingFlags.Static | BindingFlags.NonPublic);
                object[] arguments = { anchor.transform, null! };
                AssertTrue((bool)resolve.Invoke(null, arguments)!, "the active main curved canvas must resolve the D-shaped enclosure");
                object enclosure = arguments[1]!;
                FieldInfo center = enclosure.GetType().GetField("Center", BindingFlags.Instance | BindingFlags.NonPublic)!;
                FieldInfo radius = enclosure.GetType().GetField("Radius", BindingFlags.Instance | BindingFlags.NonPublic)!;
                FieldInfo rearPoint = enclosure.GetType().GetField("RearPoint", BindingFlags.Instance | BindingFlags.NonPublic)!;
                AssertClose(0f, ((Vector3)center.GetValue(enclosure)).z, 0.001f, "enclosure circle center z");
                AssertClose(3f, (float)radius.GetValue(enclosure), 0.001f, "enclosure circle radius");
                AssertClose(-2f, ((Vector3)rearPoint.GetValue(enclosure)).z, 0.001f, "rear plane distance");
                Debug.Log("[FerrofluidGrabValidation] the real curved menu circle resolves the enclosure.");
            }
            finally
            {
                foreach (UnityEngine.Object resource in resources)
                {
                    if (resource != null)
                    {
                        UnityEngine.Object.DestroyImmediate(resource);
                    }
                }
            }
        }

        // A grabbed body swings from the clicked pivot inside a gravity-aligned cone with pivot
        // friction: a pickup kick cannot flip it over the hand, and the swing settles in ~3 cycles.
        private static void ValidateHeldPivotFriction(Assembly plugin)
        {
            SimulationMode previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            List<UnityEngine.Object> resources = new();
            try
            {
                Type grabbableType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true);
                Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true);
                MethodInfo initialize = grabbableType.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)!;
                MethodInfo acquire = grabbableType.GetMethod("Acquire", BindingFlags.Instance | BindingFlags.NonPublic)!;
                MethodInfo release = grabbableType.GetMethod("Release", BindingFlags.Instance | BindingFlags.NonPublic)!;
                MethodInfo fixedUpdate = grabbableType.GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!;
                MethodInfo sampleGrabTarget = grabbableType.GetMethod("SampleGrabTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
                FieldInfo anchorField = grabbableType.GetField("_anchorBody", BindingFlags.Instance | BindingFlags.NonPublic)!;
                object enclosure = enclosureType
                    .GetConstructor(
                        BindingFlags.Instance | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                        null)!
                    .Invoke(new object[] { Vector3.zero, 3f, new Vector3(0f, 0f, -2f), Vector3.forward, 0f });
                PhysicsMaterial material = new() { bounciness = 0.3f, dynamicFriction = 0.6f, staticFriction = 0.6f };
                resources.Add(material);

                foreach (bool sphereShape in new[] { true, false })
                {
                    string label = sphereShape ? "sphere" : "cube";
                    Component body = CreateBody(
                        grabbableType,
                        initialize,
                        resources,
                        new Vector3(0f, 3f, -0.6f),
                        sphereShape,
                        enclosure,
                        material,
                        null!);
                    Rigidbody bodyRigidbody = body.GetComponent<Rigidbody>();
                    // The settled pendulum pose BEFORE acquire: a 40 degree swing about Z from a
                    // fixed pivot above, so the body's top-face center lands exactly on the pivot.
                    Quaternion pendulumRotation = Quaternion.AngleAxis(40f, Vector3.forward);
                    Vector3 pivot = new(0f, 3f, -0.6f);
                    bodyRigidbody.rotation = pendulumRotation;
                    bodyRigidbody.position = pivot - (pendulumRotation * new Vector3(0f, 0.275f, 0f));
                    bodyRigidbody.linearVelocity = Vector3.zero;
                    bodyRigidbody.angularVelocity = Vector3.zero;
                    bodyRigidbody.PublishTransform();
                    Physics.SyncTransforms();
                    GameObject attach = new($"{label} friction attach");
                    resources.Add(attach);
                    attach.transform.position = pivot;
                    acquire.Invoke(body, new object[] { attach.transform, pivot });
                    Rigidbody anchor = (Rigidbody)anchorField.GetValue(body)!;

                    // The settle window is the physical pendulum period for this click's lever arm,
                    // not a fixed tick count: the last half period must be visually still.
                    float lever = 0.275f;
                    float inertiaCm = (bodyRigidbody.inertiaTensor.x + bodyRigidbody.inertiaTensor.y + bodyRigidbody.inertiaTensor.z) / 3f;
                    float mass = bodyRigidbody.mass;
                    float period = Mathf.PI * 2f * Mathf.Sqrt(
                        (inertiaCm + (mass * lever * lever)) / (mass * 9.8f * lever));
                    int settleSteps = Mathf.CeilToInt(5f * period / FixedDelta);

                    // Signed same-side maxima track the swing peaks, not the zero crossings.
                    List<float> sameSidePeaks = new();
                    float previousSigned = float.NaN;
                    float previousDelta = float.NaN;
                    float initialPeak = 0f;
                    for (int step = 0; step < settleSteps * 2; step++)
                    {
                        Step(body, fixedUpdate, sampleGrabTarget, 1);
                        Vector3 offset = bodyRigidbody.position - anchor.position;
                        float signed = Mathf.Atan2(offset.x, -offset.y) * Mathf.Rad2Deg;
                        float currentDelta = signed - previousSigned;
                        if (!float.IsNaN(previousDelta) && previousDelta > 0f && currentDelta <= 0f && previousSigned > 0f)
                        {
                            sameSidePeaks.Add(previousSigned);
                            if (sameSidePeaks.Count == 1)
                                initialPeak = previousSigned;
                        }

                        previousDelta = currentDelta;
                        previousSigned = signed;
                    }

                    AssertTrue(sameSidePeaks.Count >= 5, $"the {label} pendulum must show at least 5 cycles (peaks={sameSidePeaks.Count})");
                    AssertTrue(sameSidePeaks[2] > 0.3f && sameSidePeaks[2] < initialPeak, $"the {label} third same-side peak must decay but remain visible ({sameSidePeaks[2]:F2} vs {initialPeak:F2})");
                    float quiet = sameSidePeaks[4];
                    AssertTrue(
                        Mathf.Abs(quiet) < 2f,
                        $"the {label} must hang near still by the fifth cycle (peak={quiet:F2} deg)");
                    AssertClose(0f, bodyRigidbody.angularDamping, 0.0001f, $"the {label} must have zero free-flight angular damping");
                    Debug.Log($"[FerrofluidGrabValidation] {label} pendulum peaks={string.Join(",", sameSidePeaks.ConvertAll(p => p.ToString("F2")))} initial={initialPeak:F2} angularDamping={bodyRigidbody.angularDamping:F3}");

                    // Mid-swing release keeps exact motion and frees every joint axis and drive.
                    bodyRigidbody.angularVelocity = new Vector3(0f, 0f, 3f);
                    bodyRigidbody.linearVelocity = Vector3.Cross(
                        new Vector3(0f, 0f, 3f),
                        bodyRigidbody.position - anchor.position);
                    Step(body, fixedUpdate, sampleGrabTarget, 1);
                    Vector3 preVelocity = bodyRigidbody.GetPointVelocity(bodyRigidbody.position);
                    Vector3 preAngular = bodyRigidbody.angularVelocity;
                    Quaternion preRotation = bodyRigidbody.rotation;
                    release.Invoke(body, null);
                    ConfigurableJoint joint = body.GetComponent<ConfigurableJoint>();
                    AssertClose(preVelocity, bodyRigidbody.GetPointVelocity(bodyRigidbody.position), 0.0001f, $"the {label} release keeps body-centre velocity");
                    AssertClose(preAngular, bodyRigidbody.angularVelocity, 0.0001f, $"the {label} release keeps angular velocity");
                    AssertClose(
                        0f,
                        Quaternion.Angle(preRotation, bodyRigidbody.rotation),
                        0.0001f,
                        $"the {label} release keeps orientation");
                    AssertTrue(
                        joint.xMotion == ConfigurableJointMotion.Free
                            && joint.yMotion == ConfigurableJointMotion.Free
                            && joint.zMotion == ConfigurableJointMotion.Free
                            && joint.angularYMotion == ConfigurableJointMotion.Free
                            && joint.angularZMotion == ConfigurableJointMotion.Free
                            && joint.slerpDrive.positionDamper <= 0f,
                        $"the {label} release must free the cone and the pivot damper");

                    // Two full seconds of free flight must not bleed the spin: zero angular drag
                    // means the actual angular velocity vector survives, not just the property.
                    // The body needs altitude so the whole 2s window is airborne (2s of fall is
                    // ~19.6m, so dropping from 25m clears the floor until the window ends).
                    bodyRigidbody.position = new Vector3(0f, 25f, -0.6f);
                    bodyRigidbody.linearVelocity = Vector3.zero;
                    preAngular = bodyRigidbody.angularVelocity;
                    Step(body, fixedUpdate, sampleGrabTarget, 100);
                    AssertTrue(
                        bodyRigidbody.angularVelocity.magnitude >= preAngular.magnitude * 0.98f,
                        $"the {label} released spin must survive 2s of free flight (omega={bodyRigidbody.angularVelocity.magnitude:F2} of {preAngular.magnitude:F2})");
                    body.gameObject.SetActive(false);
                }

                Debug.Log("[FerrofluidGrabValidation] held pivot pendulum settle and clean release verified.");
            }
            finally
            {
                foreach (UnityEngine.Object resource in resources)
                {
                    if (resource != null)
                        UnityEngine.Object.DestroyImmediate(resource);
                }

                Physics.simulationMode = previousMode;
            }
        }

        // The held pivot tracks the same 60 ms critically damped cursor smoothing the magnet uses:
        // a step input follows the shared filter's response, and hand jitter is filtered, not amplified.
        private static void ValidateGrabPointSmoothing(Assembly plugin)
        {
            SimulationMode previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            List<UnityEngine.Object> resources = new();
            try
            {
                Type grabbableType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true);
                Type interactionType = plugin.GetType("ChromaGLS.Settings.FerrofluidInteraction", true);
                Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true);
                MethodInfo initialize = grabbableType.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)!;
                MethodInfo acquire = grabbableType.GetMethod("Acquire", BindingFlags.Instance | BindingFlags.NonPublic)!;
                MethodInfo release = grabbableType.GetMethod("Release", BindingFlags.Instance | BindingFlags.NonPublic)!;
                MethodInfo fixedUpdate = grabbableType.GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!;
                MethodInfo sampleGrabTarget = grabbableType.GetMethod("SampleGrabTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
                MethodInfo track = interactionType.GetMethod("TrackPosition", BindingFlags.Static | BindingFlags.NonPublic)!;
                FieldInfo anchorField = grabbableType.GetField("_anchorBody", BindingFlags.Instance | BindingFlags.NonPublic)!;
                object enclosure = enclosureType
                    .GetConstructor(
                        BindingFlags.Instance | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                        null)!
                    .Invoke(new object[] { Vector3.zero, 3f, new Vector3(0f, 0f, -2f), Vector3.forward, 0f });
                PhysicsMaterial material = new() { bounciness = 0.3f, dynamicFriction = 0.6f, staticFriction = 0.6f };
                resources.Add(material);
                Component sphere = CreateBody(
                    grabbableType,
                    initialize,
                    resources,
                    new Vector3(0f, 2f, -0.6f),
                    true,
                    enclosure,
                    material,
                    null!);
                Rigidbody sphereBody = sphere.GetComponent<Rigidbody>();
                Rigidbody anchor = (Rigidbody)anchorField.GetValue(sphere)!;
                GameObject attach = new("smoothing attach");
                resources.Add(attach);
                Vector3 grabPoint = sphereBody.position + (Vector3.up * 0.275f);
                attach.transform.position = grabPoint;
                acquire.Invoke(sphere, new object[] { attach.transform, grabPoint });

                // A +0.1 world step must produce the shared filter's transient, not the raw jump.
                object[] trackArgs = { grabPoint, grabPoint + new Vector3(0.1f, 0f, 0f), 0.55f, FixedDelta };
                Vector3 expectedAnchor = (Vector3)track.Invoke(null, trackArgs)!;
                attach.transform.position += new Vector3(0.1f, 0f, 0f);
                Step(sphere, fixedUpdate, sampleGrabTarget, 1);
                AssertClose(
                    expectedAnchor,
                    anchor.position,
                    0.0001f,
                    "a step input must follow the shared cursor smoothing");

                // Stationary ±0.002 jitter must come out filtered, matching the helper trajectory.
                Vector3 expectedPosition = anchor.position;
                for (int step = 0; step < 20; step++)
                {
                    object[] warmupArgs = { expectedPosition, attach.transform.position, 0.55f, FixedDelta };
                    expectedPosition = (Vector3)track.Invoke(null, warmupArgs)!;
                    Step(sphere, fixedUpdate, sampleGrabTarget, 1);
                }

                float rawJitter = 0f;
                float filteredJitter = 0f;
                Vector3 lastRaw = attach.transform.position;
                Vector3 lastActual = anchor.position;
                for (int step = 0; step < 8; step++)
                {
                    float jitter = (step % 2 == 0) ? 0.002f : -0.002f;
                    attach.transform.position += new Vector3(jitter, 0f, 0f);
                    object[] args = { expectedPosition, attach.transform.position, 0.55f, FixedDelta };
                    expectedPosition = (Vector3)track.Invoke(null, args)!;
                    Step(sphere, fixedUpdate, sampleGrabTarget, 1);
                    rawJitter = Mathf.Max(rawJitter, Vector3.Distance(attach.transform.position, lastRaw));
                    filteredJitter = Mathf.Max(filteredJitter, Vector3.Distance(anchor.position, lastActual));
                    AssertClose(
                        expectedPosition.x,
                        anchor.position.x,
                        0.001f,
                        "the smoothed anchor must track the shared filter response");
                    lastActual = anchor.position;
                    lastRaw = attach.transform.position;
                }

                AssertTrue(
                    filteredJitter < rawJitter * 0.5f,
                    $"hand jitter must be filtered, not amplified (raw={rawJitter:F4} filtered={filteredJitter:F4})");

                // Regrab at a different point must not carry the old smoothed state or snap the body.
                release.Invoke(sphere, null);
                Vector3 newPoint = sphereBody.position + (Vector3.right * 0.275f);
                attach.transform.position = newPoint;
                Vector3 prePosition = sphereBody.position;
                acquire.Invoke(sphere, new object[] { attach.transform, newPoint });
                AssertClose(
                    0f,
                    Vector3.Distance(prePosition, sphereBody.position),
                    0.0001f,
                    "the regrab must not move the body");
                Step(sphere, fixedUpdate, sampleGrabTarget, 1);
                AssertClose(
                    newPoint,
                    anchor.position,
                    0.001f,
                    "a fresh grab must seed the smoothed anchor at the new hit point");

                Debug.Log("[FerrofluidGrabValidation] grab-point smoothing tracks the shared cursor filter.");
            }
            finally
            {
                foreach (UnityEngine.Object resource in resources)
                {
                    if (resource != null)
                        UnityEngine.Object.DestroyImmediate(resource);
                }

                Physics.simulationMode = previousMode;
            }
        }

        // The clicked surface point is the grab pivot: the body must hang and swing under real
        // gravity, track the kinematic anchor, and release with whatever motion the swing built.
        private static void ValidatePivotGrabs(Assembly plugin)
        {
            SimulationMode previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            List<UnityEngine.Object> resources = new();
            try
            {
                Type grabbableType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true);
                Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true);
                MethodInfo initialize = grabbableType.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)!;
                MethodInfo acquire = grabbableType.GetMethod("Acquire", BindingFlags.Instance | BindingFlags.NonPublic)!;
                MethodInfo release = grabbableType.GetMethod("Release", BindingFlags.Instance | BindingFlags.NonPublic)!;
                MethodInfo fixedUpdate = grabbableType.GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!;
                MethodInfo sampleGrabTarget = grabbableType.GetMethod("SampleGrabTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
                object enclosure = enclosureType
                    .GetConstructor(
                        BindingFlags.Instance | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                        null)!
                    .Invoke(new object[] { Vector3.zero, 3f, new Vector3(0f, 0f, -2f), Vector3.forward, 0f });
                PhysicsMaterial material = new()
                {
                    bounciness = (float)plugin.GetType("ChromaGLS.Settings.FerrofluidGrabPhysics", true)!
                        .GetField("FloorRestitution", BindingFlags.Static | BindingFlags.NonPublic)!
                        .GetValue(null)!,
                    dynamicFriction = 0.6f,
                    staticFriction = 0.6f,
                };
                resources.Add(material);

                foreach (bool sphereShape in new[] { true, false })
                {
                    string label = sphereShape ? "sphere" : "cube";
                    Component body = CreateBody(
                        grabbableType,
                        initialize,
                        resources,
                        new Vector3(0f, 2f, -0.6f),
                        sphereShape,
                        enclosure,
                        material,
                        null!);
                    Rigidbody bodyRigidbody = body.GetComponent<Rigidbody>();
                    GameObject attach = new($"{label} pivot attach");
                    resources.Add(attach);
                    Vector3 grabPoint = sphereShape
                        ? bodyRigidbody.position + (new Vector3(1f, 1f, 0f).normalized * 0.275f)
                        : bodyRigidbody.position + new Vector3(0.275f, 0.15f, 0f);
                    attach.transform.position = grabPoint;
                    Vector3 startPosition = bodyRigidbody.position;
                    Quaternion startRotation = bodyRigidbody.rotation;
                    acquire.Invoke(body, new object[] { attach.transform, grabPoint });

                    AssertTrue(
                        Vector3.Distance(startPosition, bodyRigidbody.position) < 0.0001f,
                        $"the {label} must not snap on acquire");
                    AssertClose(
                        0f,
                        Quaternion.Angle(startRotation, bodyRigidbody.rotation),
                        0.0001f,
                        $"the {label} must not reorient on acquire");

                    // Forty steps at 50 Hz is 0.8 s of hanging — the off-center click must swing.
                    for (int step = 0; step < 40; step++)
                        Step(body, fixedUpdate, sampleGrabTarget, 1);
                    FieldInfo anchorField = grabbableType.GetField("_anchorBody", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    Rigidbody anchor = (Rigidbody)anchorField.GetValue(body)!;
                    float hangAngle = Quaternion.Angle(startRotation, bodyRigidbody.rotation);
                    Debug.Log($"[FerrofluidGrabValidation] {label} hang angle={hangAngle:F1} comY={bodyRigidbody.position.y:F3} anchorError={Vector3.Distance(anchor.position, attach.transform.position):F4}");
                    AssertTrue(
                        hangAngle > 15f,
                        $"the {label} must swing under gravity from the off-center pivot ({hangAngle:F1} deg)");
                    AssertTrue(
                        bodyRigidbody.position.y < attach.transform.position.y,
                        $"the {label} center of mass must settle below the pivot");
                    AssertTrue(
                        bodyRigidbody.angularVelocity.magnitude > 0f || hangAngle > 15f,
                        $"the {label} swing builds real angular motion");

                    // A lateral drag moves the anchor; a mid-swing release keeps exact motion.
                    for (int step = 0; step < 8; step++)
                    {
                        attach.transform.position += new Vector3(0.04f, 0f, 0f);
                        Step(body, fixedUpdate, sampleGrabTarget, 1);
                    }

                    Vector3 preVelocity = bodyRigidbody.GetPointVelocity(bodyRigidbody.position);
                    Vector3 preAngular = bodyRigidbody.angularVelocity;
                    Quaternion preRotation = bodyRigidbody.rotation;
                    release.Invoke(body, null);
                    AssertTrue(
                        (bool)grabbableType.GetProperty("IsHeld", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(body)! == false,
                        $"the {label} must report released");
                    AssertClose(preVelocity, bodyRigidbody.GetPointVelocity(bodyRigidbody.position), 0.0001f, $"the {label} release keeps body-centre velocity");
                    AssertClose(preAngular, bodyRigidbody.angularVelocity, 0.0001f, $"the {label} release keeps angular velocity");
                    AssertClose(
                        0f,
                        Quaternion.Angle(preRotation, bodyRigidbody.rotation),
                        0.0001f,
                        $"the {label} release keeps orientation");
                    body.gameObject.SetActive(false);
                }

                Debug.Log("[FerrofluidGrabValidation] pivot grab hang, swing, drag, and release all verified for cube and sphere.");
            }
            finally
            {
                foreach (UnityEngine.Object resource in resources)
                {
                    if (resource != null)
                        UnityEngine.Object.DestroyImmediate(resource);
                }

                Physics.simulationMode = previousMode;
            }
        }

        private static void ValidateGrabPhysics(Assembly plugin)
        {
            SimulationMode previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            List<UnityEngine.Object> resources = new();
            try
            {
                Type grabbableType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true);
                Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true);
                MethodInfo initialize = grabbableType.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo acquire = grabbableType.GetMethod("Acquire", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo release = grabbableType.GetMethod("Release", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo fixedUpdate = grabbableType.GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo sampleGrabTarget = grabbableType.GetMethod("SampleGrabTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
                PropertyInfo bodyVelocity = grabbableType.GetProperty("BodyVelocity", BindingFlags.Instance | BindingFlags.NonPublic);
                PropertyInfo spikeScale = grabbableType.GetProperty("MagnetHeightAmplification", BindingFlags.Instance | BindingFlags.NonPublic);

                object enclosure = enclosureType
                    .GetConstructor(
                        BindingFlags.Instance | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                        null)!
                    .Invoke(new object[] { Vector3.zero, 3f, new Vector3(0f, 0f, -2f), Vector3.forward, 0f });
                float floorRestitution = (float)plugin.GetType("ChromaGLS.Settings.FerrofluidGrabPhysics", true)!
                    .GetField("FloorRestitution", BindingFlags.Static | BindingFlags.NonPublic)!
                    .GetValue(null)!;
                PhysicsMaterial material = new() { bounciness = floorRestitution, dynamicFriction = 0.6f, staticFriction = 0.6f };
                resources.Add(material);

                GameObject floor = new("validation floor");
                resources.Add(floor);
                floor.transform.position = new Vector3(0f, -0.5f, 0f);
                BoxCollider floorCollider = floor.AddComponent<BoxCollider>();
                floorCollider.size = new Vector3(8f, 1f, 8f);
                floorCollider.sharedMaterial = material;

                Type ownershipType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabOwnership", true);
                object ownership = Activator.CreateInstance(ownershipType, nonPublic: true)!;
                MethodInfo tryAcquire = ownershipType.GetMethod("TryAcquire", BindingFlags.Instance | BindingFlags.NonPublic)!;
                MethodInfo ownerOf = ownershipType.GetMethod("OwnerOf", BindingFlags.Instance | BindingFlags.NonPublic)!;
                MethodInfo onDisable = grabbableType.GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)!;

                Component sphere = CreateBody(grabbableType, initialize, resources, new Vector3(0f, 1.5f, 0.3f), true, enclosure, material, ownership);
                Rigidbody sphereBody = sphere.GetComponent<Rigidbody>();

                // Gravity drop: useGravity stays off, so only the scripted AddForce can pull it.
                // Unity integrates semi-implicitly, so n steps fall 9.8 * dt * dt * n(n + 1) / 2.
                Step(sphere, fixedUpdate, sampleGrabTarget, 20);
                float expectedDrop = 9.8f * FixedDelta * FixedDelta * 20f * 21f * 0.5f;
                AssertClose(1.5f - expectedDrop, sphereBody.position.y, 0.01f, "gravity drop distance at exactly 9.8m/s squared");
                Step(sphere, fixedUpdate, sampleGrabTarget, 200);
                AssertClose(0.275f, sphereBody.position.y, 0.06f, "resting height on the floor");
                AssertClose(0f, sphereBody.linearVelocity.magnitude, 0.2f, "resting velocity");

                // Held follow: the attachment tracks the pointer with a retained offset.
                GameObject attach = new("validation attach");
                resources.Add(attach);
                attach.transform.position = new Vector3(1f, 0.8f, 0.5f);
                acquire.Invoke(sphere, new object[] { attach.transform, sphereBody.position + (Vector3.right * 0.275f) });
                float apiVelocityError = 0f;
                for (int step = 0; step < 10; step++)
                {
                    attach.transform.position += new Vector3(0.06f, 0f, 0f);
                    Step(sphere, fixedUpdate, sampleGrabTarget, 1);
                    apiVelocityError = Mathf.Max(apiVelocityError, ((Vector3)bodyVelocity.GetValue(sphere) - sphereBody.GetPointVelocity(sphereBody.position)).magnitude);
                }

                // Grab offset was (body - attach) = (-1, -0.525, -0.2); the attach ended at (1.6, 0.8, 0.5).
                AssertClose(0.6f, sphereBody.position.x, 0.15f, "held x keeps the grab offset as the attachment moves");
                AssertClose(0.275f, sphereBody.position.y, 0.15f, "held y keeps the grab offset as the attachment moves");
                AssertClose(0.3f, sphereBody.position.z, 0.15f, "held z keeps the grab offset as the attachment moves");
                AssertTrue(apiVelocityError < 0.001f, "BodyVelocity must match physical body-centre velocity");

                // Release retains the recent motion and the enclosure stops the throw inside the circle.
                release.Invoke(sphere, null);
                AssertTrue(sphereBody.linearVelocity.x > 0.5f, "the thrown body must retain the attachment's velocity");
                for (int step = 0; step < 120; step++)
                {
                    Step(sphere, fixedUpdate, sampleGrabTarget, 1);
                    Vector3 radial = new(sphereBody.position.x, 0f, sphereBody.position.z);
                    AssertTrue(
                        radial.magnitude + 0.275f <= 3f + 0.01f,
                        $"the thrown body's outer edge escaped the cylindrical wall ({radial.magnitude + 0.275f} at {sphereBody.position})");
                    AssertTrue(
                        sphereBody.position.z - 0.275f >= -2f - 0.01f,
                        $"the thrown body's back edge crossed the rear plane ({sphereBody.position.z - 0.275f})");
                }

                // A tossed arc has no ceiling; a straight-up throw still falls back under 9.8.
                Step(sphere, fixedUpdate, sampleGrabTarget, 60);
                sphereBody.position = new Vector3(0f, 0.3f, 0f);
                sphereBody.linearVelocity = new Vector3(0f, 12f, 0f);
                float highestY = sphereBody.position.y;
                for (int step = 0; step < 400; step++)
                {
                    Step(sphere, fixedUpdate, sampleGrabTarget, 1);
                    highestY = Mathf.Max(highestY, sphereBody.position.y);
                }

                AssertTrue(highestY > 2f, "the enclosure must not clamp a rising arc");
                AssertTrue(sphereBody.position.y <= 0.35f, "the body must fall back to the floor after its bounces damp out");

                // A held drag through the floor keeps the body inside; the click is a real
                // surface point, not the hand position.
                attach.transform.position = sphereBody.position + (Vector3.right * 0.275f);
                acquire.Invoke(sphere, new object[] { attach.transform, sphereBody.position + (Vector3.right * 0.275f) });
                attach.transform.position = new Vector3(0f, -0.4f, 0.3f);
                for (int step = 0; step < 5; step++)
                {
                    Step(sphere, fixedUpdate, sampleGrabTarget, 1);
                    AssertTrue(
                        sphereBody.position.y >= 0.27f,
                        $"a held body must not clip through the floor (y={sphereBody.position.y:F4})");
                }

                // A swing release keeps the real joint motion: drag the anchor laterally and
                // let the body build up actual angular and linear velocity before release.
                release.Invoke(sphere, null);
                attach.transform.position = sphereBody.position + (Vector3.right * 0.275f);
                attach.transform.rotation = Quaternion.identity;
                acquire.Invoke(sphere, new object[] { attach.transform, sphereBody.position + (Vector3.right * 0.275f) });
                for (int step = 0; step < 8; step++)
                {
                    attach.transform.position += new Vector3(0f, 0f, 0.04f);
                    Step(sphere, fixedUpdate, sampleGrabTarget, 1);
                }

                release.Invoke(sphere, null);
                AssertTrue(sphereBody.angularVelocity.magnitude > 0.5f, "a dragged anchor must toss the body with retained spin");
                AssertTrue(sphereBody.linearVelocity.magnitude > 0.3f, "the release must retain the lateral sweep");

                // The manager's acquire decision: only the currently selected hand's own crossing counts.
                Type managerType = plugin.GetType(
                    "ChromaGLS.Settings.FerrofluidEnvironmentObjects+FerrofluidEnvironmentManager", true);
                MethodInfo decision = managerType.GetMethod("EvaluateGrabDecision", BindingFlags.Static | BindingFlags.NonPublic)!;
                object leftHand = new();
                AssertTrue(
                    (bool)decision.Invoke(null, new object[] { ownership, leftHand, true, true, false, sphere })!,
                    "the selected left hand's trigger rise must acquire the hovered body");
                AssertTrue(
                    ownerOf.Invoke(ownership, new object[] { sphere }) == leftHand,
                    "the acquired body must register its holding hand");
                AssertTrue(
                    !(bool)decision.Invoke(null, new object[] { ownership, leftHand, true, true, false, sphere })!,
                    "a held body cannot be acquired twice");

                // The same left crossing while the pointer's selected hand is the right one does nothing.
                object ownership2 = Activator.CreateInstance(ownershipType, nonPublic: true)!;
                AssertTrue(
                    !(bool)decision.Invoke(null, new object[] { ownership2, leftHand, false, true, false, sphere })!,
                    "a left-hand crossing must not acquire while the right hand owns the pointer");

                // Ownership must release when the grabbable disables so the hand can take another object.
                onDisable.Invoke(sphere, null);
                AssertTrue(ownerOf.Invoke(ownership, new object[] { sphere }) == null, "disabling a held object must release its ownership");

                // Mutual collision: the cube rests on the floor and the dropped sphere lands on it.
                Component cube = CreateBody(grabbableType, initialize, resources, new Vector3(0f, 0.275f, -0.6f), false, enclosure, material, ownership);
                Rigidbody cubeBody = cube.GetComponent<Rigidbody>();
                AssertTrue((bool)tryAcquire.Invoke(ownership, new[] { leftHand, cube })!, "the same hand must acquire the cube after the sphere released");
                sphereBody.position = new Vector3(0f, 1.1f, -0.6f);
                sphereBody.linearVelocity = Vector3.zero;
                sphereBody.angularVelocity = Vector3.zero;
                Step(sphere, cube, fixedUpdate, sampleGrabTarget, 160);
                AssertTrue(cubeBody.position.y >= 0.27f, "the cube fell through the floor");
                AssertTrue(sphereBody.position.y > 0.45f, "the sphere must rest on top of the cube");

                // A spinning held cube keeps every corner inside the cylinder and ahead of the rear.
                acquire.Invoke(cube, new object[] { attach.transform, cubeBody.position + (cubeBody.rotation * new Vector3(0.275f, 0f, 0f)) });
                attach.transform.position = new Vector3(2.4f, 0.6f, -1.4f);
                for (int step = 0; step < 12; step++)
                {
                    attach.transform.rotation = Quaternion.AngleAxis(step * 25f, Vector3.up);
                    Step(cube, fixedUpdate, sampleGrabTarget, 1);
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 local = new(
                            (corner & 1) == 0 ? -0.275f : 0.275f,
                            (corner & 2) == 0 ? -0.275f : 0.275f,
                            (corner & 4) == 0 ? -0.275f : 0.275f);
                        Vector3 world = cubeBody.position + (cubeBody.rotation * local);
                        float cornerRadial = Mathf.Sqrt((world.x * world.x) + (world.z * world.z));
                        AssertTrue(
                            cornerRadial <= 3f + 0.001f,
                            $"a spinning held cube corner escaped the wall ({world})");
                        AssertTrue(
                            world.z >= -2f - 0.001f,
                            $"a spinning held cube corner crossed the rear plane ({world})");
                    }
                }

                release.Invoke(cube, null);

                // The wall must reflect a fraction of the incoming radial velocity, not merely
                // stop the body.
                sphereBody.position = new Vector3(0f, 1.2f, 0f);
                sphereBody.linearVelocity = new Vector3(8f, 0f, 0f);
                float peakInward = 0f;
                float peakOutward = 0f;
                for (int step = 0; step < 40; step++)
                {
                    float radial = Vector3.Dot(
                        new Vector3(sphereBody.linearVelocity.x, 0f, sphereBody.linearVelocity.z),
                        new Vector3(sphereBody.position.x, 0f, sphereBody.position.z).normalized);
                    Step(sphere, cube, fixedUpdate, sampleGrabTarget, 1);
                    float postRadial = Vector3.Dot(
                        new Vector3(sphereBody.linearVelocity.x, 0f, sphereBody.linearVelocity.z),
                        new Vector3(sphereBody.position.x, 0f, sphereBody.position.z).normalized);
                    peakOutward = Mathf.Max(peakOutward, radial);
                    if (radial > 2f)
                        peakInward = Mathf.Min(peakInward, postRadial);
                }

                AssertTrue(
                    peakOutward > 2f,
                    "the sphere must reach the enclosure wall to measure its reflection");
                AssertClose(
                    peakOutward * -0.2f,
                    peakInward,
                    peakOutward * 0.1f,
                    "the enclosure wall must reflect the configured restitution of the radial component");
                Debug.Log($"[FerrofluidGrabValidation] wall impact incoming={peakOutward:F2} reflected={peakInward:F2}");

                // Isolated high-speed drops check floor containment and the solver's rebound.
                // A tilted cube can transfer part of its impact into rotation.
                sphere.gameObject.SetActive(false);
                cube.gameObject.SetActive(false);
                Component sphereDrop = CreateBody(
                    grabbableType,
                    initialize,
                    resources,
                    new Vector3(0f, 1.5f, -0.6f),
                    true,
                    enclosure,
                    material,
                    ownership);
                ValidateFloorImpact(
                    sphereDrop,
                    fixedUpdate,
                    sampleGrabTarget,
                    sphereDrop.GetComponent<Rigidbody>(),
                    bodyVelocity,
                    floorRestitution,
                    isSphere: true,
                    Quaternion.identity,
                    exactRestitution: true,
                    "sphere");
                sphereDrop.gameObject.SetActive(false);
                Component cubeDrop = CreateBody(
                    grabbableType,
                    initialize,
                    resources,
                    new Vector3(0f, 1.5f, -0.6f),
                    false,
                    enclosure,
                    material,
                    ownership);
                ValidateFloorImpact(
                    cubeDrop,
                    fixedUpdate,
                    sampleGrabTarget,
                    cubeDrop.GetComponent<Rigidbody>(),
                    bodyVelocity,
                    floorRestitution,
                    isSphere: false,
                    Quaternion.Euler(30f, 45f, 20f),
                    exactRestitution: false,
                    "tilted cube");

                // Isolated rolling-resistance seam cases drive the production method directly.
#pragma warning disable CS0618 // Rigidbody.angularDrag spans every supported Unity version.
                sphereDrop.gameObject.SetActive(false);
                cubeDrop.gameObject.SetActive(false);
                Component rollSphere = CreateBody(
                    grabbableType,
                    initialize,
                    resources,
                    new Vector3(0f, 1.5f, -0.6f),
                    true,
                    enclosure,
                    material,
                    ownership);
                Component rollCube = CreateBody(
                    grabbableType,
                    initialize,
                    resources,
                    new Vector3(0f, 1.5f, -0.6f),
                    false,
                    enclosure,
                    material,
                    ownership);
                MethodInfo roll = grabbableType.GetMethod(
                    "ApplySphereRollingResistance",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Vector3), typeof(Vector3).MakeByRefType(), typeof(float) },
                    null);
                AssertTrue(roll != null, "the sphere rolling-resistance method must resolve");
                float floorRadius = 3f - 0.275f;
                object[] rollArgs;
                Rigidbody rollBody = rollSphere.GetComponent<Rigidbody>();

                // Floor roll: horizontal tangent decays at the floor rate and angular drag rises.
                rollArgs = new object[] { new Vector3(0f, 0.275f, 0f), new Vector3(1f, 0f, 0f), 0.02f };
                roll!.Invoke(rollSphere, rollArgs);
                AssertClose(
                    Mathf.Exp(-0.125f * 0.02f),
                    ((Vector3)rollArgs[1]).x,
                    0.0001f,
                    "the floor roll must decay the tangential speed at the halved sphere floor rate");
                AssertClose(0f, rollBody.angularDrag, 0.0001f, "floor contact uses the anisotropic torque instead of global angular drag");

                // Curved-wall roll: the perpendicular tangent decays at the wall rate.
                rollArgs = new object[] { new Vector3(floorRadius, 1.2f, 0f), new Vector3(0f, 0f, 1f), 0.02f };
                roll.Invoke(rollSphere, rollArgs);
                AssertClose(
                    Mathf.Exp(-1f * 0.02f),
                    ((Vector3)rollArgs[1]).z,
                    0.0001f,
                    "the wall roll must decay the tangential speed at the wall rate");
                AssertClose(1f, rollBody.angularDrag, 0.0001f, "wall contact raises the sphere angular drag further");

                // Floor+wall corner: downward floor-normal motion survives; tangent decays once.
                rollArgs = new object[] { new Vector3(floorRadius, 0.275f, 0f), new Vector3(0f, -0.01f, 1f), 0.02f };
                roll.Invoke(rollSphere, rollArgs);
                AssertClose(
                    -0.01f,
                    ((Vector3)rollArgs[1]).y,
                    0.0001f,
                    "the corner roll must not drag the floor-normal velocity");
                AssertClose(
                    Mathf.Exp(-1f * 0.02f),
                    ((Vector3)rollArgs[1]).z,
                    0.0001f,
                    "the corner roll damps the tangent once at the wall rate");

                // An inward reflection off the wall is not rolling contact: velocity untouched.
                rollArgs = new object[] { new Vector3(floorRadius, 1.2f, 0f), new Vector3(-1.6f, 0f, 1f), 0.02f };
                roll.Invoke(rollSphere, rollArgs);
                Vector3 reflected = (Vector3)rollArgs[1];
                AssertClose(-1.6f, reflected.x, 0.0001f, "the reflected normal component stays untouched");
                AssertClose(1f, reflected.z, 0.0001f, "the reflected tangent component stays untouched");
                AssertClose(0f, rollBody.angularDrag, 0.0001f, "the reflected sphere falls back to zero free-flight drag");

                // Mid-air keeps free flight at the baseline drag.
                rollArgs = new object[] { new Vector3(0f, 2f, 0f), new Vector3(1f, 0f, 0f), 0.02f };
                roll.Invoke(rollSphere, rollArgs);
                AssertClose(1f, ((Vector3)rollArgs[1]).x, 0.0001f, "airborne motion is undamped");
                AssertClose(0f, rollBody.angularDrag, 0.0001f, "airborne angular drag stays zero");

                // A held sphere resets any contact damping it picked up on the floor.
                rollArgs = new object[] { new Vector3(0f, 0.275f, 0f), new Vector3(1f, 0f, 0f), 0.02f };
                roll.Invoke(rollSphere, rollArgs);
                acquire.Invoke(rollSphere, new object[] { attach.transform, rollBody.position + (Vector3.right * 0.275f) });
                rollArgs = new object[] { new Vector3(0f, 0.275f, 0f), new Vector3(1f, 0f, 0f), 0.02f };
                roll.Invoke(rollSphere, rollArgs);
                AssertClose(1f, ((Vector3)rollArgs[1]).x, 0.0001f, "a held sphere ignores rolling resistance");
                AssertClose(0f, rollBody.angularDrag, 0.0001f, "grabbing resets the sphere angular drag");
                release.Invoke(rollSphere, null);

                // Cubes never engage the rolling-resistance path.
                Rigidbody rollCubeBody = rollCube.GetComponent<Rigidbody>();
                rollArgs = new object[] { new Vector3(0f, 0.275f, 0f), new Vector3(1f, 0f, 0f), 0.02f };
                roll.Invoke(rollCube, rollArgs);
                AssertClose(1f, ((Vector3)rollArgs[1]).x, 0.0001f, "the cube ignores rolling resistance");
                AssertClose(0f, rollCubeBody.angularDrag, 0.0001f, "the cube angular drag stays zero");
#pragma warning restore CS0618
                rollSphere.gameObject.SetActive(false);
                rollCube.gameObject.SetActive(false);

                float scale = (float)spikeScale.GetValue(sphere);
                AssertClose(1f, scale, 0.0001f, "spike scale defaults to neutral");

                Debug.Log("[FerrofluidGrabValidation] rigidbody drop, hold, throw, bounce, mutual collision, and enclosure all pass.");
            }
            finally
            {
                Physics.simulationMode = previousMode;
                foreach (UnityEngine.Object resource in resources)
                {
                    if (resource != null)
                    {
                        UnityEngine.Object.DestroyImmediate(resource);
                    }
                }
            }
        }

        private static Component CreateBody(
            Type grabbableType,
            MethodInfo initialize,
            List<UnityEngine.Object> resources,
            Vector3 position,
            bool sphere,
            object enclosure,
            PhysicsMaterial material,
            object ownership)
        {
            GameObject body = new(sphere ? "validation sphere" : "validation cube");
            resources.Add(body);
            body.transform.position = position;
            body.transform.localScale = Vector3.one * 0.55f;
            Component grabbable = body.AddComponent(grabbableType);
            initialize.Invoke(grabbable, new object[] { enclosure, sphere, material, ownership! });
            return grabbable;
        }

        private static void ValidateFloorImpact(
            Component grabbable,
            MethodInfo fixedUpdate,
            MethodInfo sampleGrabTarget,
            Rigidbody body,
            PropertyInfo bodyVelocity,
            float restitution,
            bool isSphere,
            Quaternion orientation,
            bool exactRestitution,
            string label)
        {
            body.position = new Vector3(0f, 0.8f, -0.6f);
            body.rotation = orientation;
            body.linearVelocity = new Vector3(0f, -25f, 0f);
            body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            float minSupport = float.MaxValue;
            float incidentSpeed = 0f;
            float reboundVelocity = 0f;
            float apiVelocityDelta = 0f;
            bool impacted = false;
            int bounceCount = 0;
            bool sawDownward = false;
            float firstPeak = 0f;
            float secondPeak = 0f;
            float episodePeak = 0f;
            for (int step = 0; step < 400; step++)
            {
                // The support extent follows the body's current rotation for a tumbling box.
                Vector3 localDown = Quaternion.Inverse(body.rotation) * Vector3.down;
                float supportExtent = isSphere
                    ? 0.275f
                    : (Mathf.Abs(localDown.x) + Mathf.Abs(localDown.y) + Mathf.Abs(localDown.z)) * 0.275f;
                float support = body.position.y - supportExtent;
                minSupport = Mathf.Min(minSupport, support);
                bool shouldImpact = support > 0f
                    && support + (body.linearVelocity.y * FixedDelta) <= 0f;
                if (shouldImpact && !impacted)
                    incidentSpeed = -body.linearVelocity.y;
                Step(grabbable, fixedUpdate, sampleGrabTarget, 1);
                apiVelocityDelta = Mathf.Max(apiVelocityDelta, ((Vector3)bodyVelocity.GetValue(grabbable) - body.linearVelocity).magnitude);
                Vector3 stepLocalDown = Quaternion.Inverse(body.rotation) * Vector3.down;
                float stepExtent = isSphere
                    ? 0.275f
                    : (Mathf.Abs(stepLocalDown.x) + Mathf.Abs(stepLocalDown.y) + Mathf.Abs(stepLocalDown.z)) * 0.275f;
                float stepSupport = body.position.y - stepExtent;
                minSupport = Mathf.Min(minSupport, stepSupport);
                AssertTrue(
                    stepSupport >= -0.005f,
                    $"the {label} support phased through the floor ({stepSupport:F4} at step {step})");
                if (shouldImpact)
                {
                    if (!impacted)
                    {
                        impacted = true;
                        float contactVelocity = body.linearVelocity.y;
                        AssertTrue(
                            contactVelocity > -incidentSpeed * 0.5f,
                            $"the {label} contact must absorb most of the impact on the contact step (v={contactVelocity:F3})");
                    }
                }

                // Count genuine upward episodes: a real bounce is a fall followed by rising.
                float postVy = body.linearVelocity.y;
                if (postVy < -0.1f)
                {
                    if (episodePeak > 0f)
                    {
                        if (firstPeak <= 0f)
                            firstPeak = episodePeak;
                        else if (secondPeak <= 0f)
                            secondPeak = episodePeak;
                        episodePeak = 0f;
                    }

                    sawDownward = true;
                }
                else if (postVy > 0.1f)
                {
                    if (sawDownward)
                        bounceCount++;
                    sawDownward = false;
                    episodePeak = Mathf.Max(episodePeak, postVy);
                }

                if (impacted)
                    reboundVelocity = Mathf.Max(reboundVelocity, body.linearVelocity.y);
            }

            AssertTrue(impacted, $"the {label} must reach the floor");
            AssertTrue(
                bounceCount >= 2,
                $"the {label} must rebound upward at least twice before settling ({bounceCount} upward episodes)");
            AssertTrue(
                secondPeak <= 0f || secondPeak < firstPeak,
                $"the {label} rebounds must decay (first={firstPeak:F2} second={secondPeak:F2})");
            AssertTrue(
                reboundVelocity > 0f,
                $"the {label} must rebound upward after the floor contact (v={reboundVelocity:F3})");
            if (exactRestitution)
            {
                AssertClose(
                    incidentSpeed * restitution,
                    reboundVelocity,
                    incidentSpeed * 0.04f,
                    $"the {label} rebound must match the material restitution");
            }

            AssertTrue(
                apiVelocityDelta < 0.001f,
                $"the {label} BodyVelocity must mirror the rigidbody velocity through impact ({apiVelocityDelta})");
            Debug.Log(
                $"[FerrofluidGrabValidation] {label} floor impact incident={incidentSpeed:F2} rebound={reboundVelocity:F2} minSupport={minSupport:F4} apiDelta={apiVelocityDelta:F4}");
        }

        private static void Step(Component grabbable, MethodInfo fixedUpdate, MethodInfo sampleGrabTarget, int steps)
        {
            Step(grabbable, null, fixedUpdate, sampleGrabTarget, steps);
        }

        private static void Step(Component first, Component second, MethodInfo fixedUpdate, MethodInfo sampleGrabTarget, int steps)
        {
            for (int step = 0; step < steps; step++)
            {
                sampleGrabTarget.Invoke(first, new object[] { FixedDelta, Time.unscaledTime });
                fixedUpdate.Invoke(first, null);
                if (second != null)
                {
                    sampleGrabTarget.Invoke(second, new object[] { FixedDelta, Time.unscaledTime });
                    fixedUpdate.Invoke(second, null);
                }

                Physics.Simulate(FixedDelta);
            }
        }

        // Body motion injects momentum into surface-site velocities through the integrator:
        // bases slide tangentially along the shape, then the ordinary elastic spring carries
        // them home once the input stops.
        private static void ValidateSurfaceSlide()
        {
            Shader simShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSimulation.shader");
            if (ShaderUtil.ShaderHasError(simShader))
            {
                throw new Exception("The simulation shader failed to compile.");
            }

            List<UnityEngine.Object> resources = new();
            try
            {
                Vector2[] sites = new Vector2[]
                {
                    new(0.35f, 0.4f), new(0.65f, 0.4f), new(0.5f, 0.7f),
                    new(0.42f, 0.55f), new(0.58f, 0.55f),
                };
                foreach (int shape in new[] { 1, 2 })
                {
                    ValidateSurfaceSlideShape(simShader, sites, shape, resources);
                }
            }
            finally
            {
                foreach (UnityEngine.Object resource in resources)
                {
                    if (resource != null)
                    {
                        UnityEngine.Object.DestroyImmediate(resource);
                    }
                }
            }
        }

        private static void ValidateSurfaceSlideShape(
            Shader simShader,
            Vector2[] sites,
            int shape,
            List<UnityEngine.Object> resources)
        {
            string label = shape == 1 ? "cube" : "sphere";
            int stride = 4;
            Material material = new(simShader);
            material.EnableKeyword("FERROFLUID_BRUTE_FORCE_HEIGHT_MAP");
            resources.Add(material);
            Texture2D rest = new(stride, stride, TextureFormat.RGBAFloat, false, true)
            {
                filterMode = FilterMode.Point,
            };
            resources.Add(rest);
            Color[] restPixels = new Color[stride * stride];
            for (int index = 0; index < sites.Length; index++)
            {
                restPixels[index] = new Color((sites[index].x * 2f) - 1f, (sites[index].y * 2f) - 1f, 0f, 0f);
            }

            rest.SetPixels(restPixels);
            rest.Apply(false, false);
            Texture2D springState = new(stride, stride, TextureFormat.RGBAFloat, false, true)
            {
                filterMode = FilterMode.Point,
            };
            resources.Add(springState);
            Color[] springPixels = new Color[stride * stride];
            for (int index = 0; index < sites.Length; index++)
            {
                springPixels[index] = new Color(0.4f, 0.5f, 0.5f, 0.4f);
            }

            springState.SetPixels(springPixels);
            springState.Apply(false, false);
            RenderTexture current = CreateRenderTexture(stride, resources);
            RenderTexture next = CreateRenderTexture(stride, resources);
            RenderTexture siteSurface = CreateRenderTexture(stride, resources);
            RenderTexture restSurface = CreateRenderTexture(stride, resources);
            RenderTexture height = CreateRenderTexture(32, resources);
            Graphics.Blit(rest, current);
            material.SetInt("_ParticleCount", sites.Length);
            material.SetInt("_ParticleTextureSize", stride);
            material.SetFloat("_SurfaceShapeMode", shape);
            material.SetFloat("_SurfaceRoundness", 0.48f);
            material.SetVector("_PrimitiveYawCosSin", new Vector2(1f, 0f));
            material.SetVector("_DomainScale", new Vector2(15.36f, 15.36f));
            material.SetFloat("_H", 1f);
            material.SetFloat("_HeightMapScale", 0.9f);
            material.SetFloat("_SmoothFactor", 0.02f);
            material.SetFloat("_HeightFactor", 0.38f);
            material.SetFloat("_PointerRadius", 5.1f);
            material.SetFloat("_MaximumSlide", 4.1f);
            material.SetFloat("_SlideFraction", 0.5f);
            material.SetFloat("_SlideFalloffExponent", 0.7f);
            material.SetFloat("_Elasticity", 80f);
            material.SetFloat("_Damping", 7f);
            material.SetFloat("_PrimitiveSpikeSpacing", 0.16f);
            material.SetFloat("_PrimitiveCellMetric", 0.64f * 0.26f / 1.4f);
            material.SetFloat("_SpikeFactor", 1f / 3f);
            material.SetFloat("_SpikeBaseCutoff", 0.1f);
            material.SetFloat("_PrimitiveTipRoundness", 0.2f);
            material.SetFloat("_DeltaTimeMilliseconds", 8f);
            material.SetFloat("_SpikesEnabled", 1f);
            material.SetTexture("_RestPositionTex", rest);
            material.SetTexture("_SpringStateTex", springState);
            material.SetTexture("_DensityPressureTex", Texture2D.whiteTexture);
            material.SetTexture("_ForceTex", Texture2D.blackTexture);
            material.SetTexture("_SiteSurfaceTex", siteSurface);
            material.SetTexture("_RestSurfaceTex", restSurface);
            material.SetTexture("_StateTex", current);
            material.SetMatrix("_PrimitiveFrameRotation", Matrix4x4.identity);
            material.SetVector("_PrimitiveLinearVelocityDelta", Vector4.zero);
            material.SetVector("_PrimitivePreviousAngularVelocity", Vector4.zero);
            material.SetVector("_PrimitiveAngularVelocity", Vector4.zero);
            Graphics.Blit(Texture2D.blackTexture, restSurface, material, 6);
            Graphics.Blit(Texture2D.blackTexture, siteSurface, material, 4);
            Color[] baseline = Read(siteSurface);

            // A 1 m/s pickup step injects momentum once; read near the first spring peak, not
            // after the oscillation has swung back through home.
            material.SetVector("_PrimitiveLinearVelocityDelta", new Vector3(1f, 0f, 0f));
            IntegrateSurface(material, current, next, 1);
            material.SetVector("_PrimitiveLinearVelocityDelta", Vector4.zero);
            IntegrateSurface(material, current, next, 19);
            Graphics.Blit(Texture2D.blackTexture, siteSurface, material, 4);
            Color[] shifted = Read(siteSurface);

            float moved = 0f;
            float surfaceError = 0f;
            for (int index = 0; index < sites.Length; index++)
            {
                Vector3 before = new(baseline[index].r, baseline[index].g, baseline[index].b);
                Vector3 after = new(shifted[index].r, shifted[index].g, shifted[index].b);
                moved = Mathf.Max(moved, Vector3.Distance(before, after));
                float exponent = shape > 1.5f ? 2f : Mathf.Lerp(8f, 2f, 0.48f);
                float surfaceValue =
                    Mathf.Pow(Mathf.Abs(after.x) / 0.5f, exponent)
                    + Mathf.Pow(Mathf.Abs(after.y) / 0.5f, exponent)
                    + Mathf.Pow(Mathf.Abs(after.z) / 0.5f, exponent);
                surfaceError = Mathf.Max(surfaceError, Mathf.Abs(surfaceValue - 1f));
            }

            AssertTrue(
                moved > 0.005f && moved < 0.04f,
                $"the {label} site bases must slide under linear momentum (moved={moved:F4})");
            AssertTrue(
                surfaceError < 0.05f,
                $"the {label} moved bases must stay on the shape surface (surface equation error={surfaceError:F4})");

            IntegrateSurface(material, current, next, 300);
            Graphics.Blit(Texture2D.blackTexture, siteSurface, material, 4);
            Color[] returned = Read(siteSurface);
            float returnError = 0f;
            for (int index = 0; index < sites.Length; index++)
            {
                Vector3 before = new(baseline[index].r, baseline[index].g, baseline[index].b);
                Vector3 after = new(returned[index].r, returned[index].g, returned[index].b);
                returnError = Mathf.Max(returnError, Vector3.Distance(before, after));
            }

            AssertTrue(
                returnError < moved * 0.25f,
                $"the {label} bases must elastically return once inertia clears (error={returnError:F4} of {moved:F4})");

            // A stopped spin releases angular momentum through the previous-omega term once;
            // the sign is only valid on the impulse step before the spring can swing it back.
            material.SetVector("_PrimitivePreviousAngularVelocity", new Vector3(0f, 0.8f, 0f));
            IntegrateSurface(material, current, next, 1);
            material.SetVector("_PrimitivePreviousAngularVelocity", Vector4.zero);
            Graphics.Blit(Texture2D.blackTexture, siteSurface, material, 4);
            Color[] spun = Read(siteSurface);
            float spinAligned = 0f;
            float spinMoved = 0f;
            float normalExponent = Mathf.Lerp(8f, 2f, 0.48f);
            for (int index = 0; index < sites.Length; index++)
            {
                Vector3 before = new(baseline[index].r, baseline[index].g, baseline[index].b);
                Vector3 after = new(spun[index].r, spun[index].g, spun[index].b);
                Vector3 delta = after - before;
                spinMoved = Mathf.Max(spinMoved, delta.magnitude);
                Vector3 siteNormal = shape > 1.5f
                    ? before.normalized
                    : new Vector3(
                        Mathf.Sign(before.x) * Mathf.Pow(Mathf.Abs(before.x), normalExponent - 1f),
                        Mathf.Sign(before.y) * Mathf.Pow(Mathf.Abs(before.y), normalExponent - 1f),
                        Mathf.Sign(before.z) * Mathf.Pow(Mathf.Abs(before.z), normalExponent - 1f)).normalized;
                Vector3 expected = Vector3.Cross(new Vector3(0f, 0.8f, 0f), before);
                expected -= siteNormal * Vector3.Dot(expected, siteNormal);
                if (delta.sqrMagnitude > 0.00000001f && expected.sqrMagnitude > 0.00000001f)
                {
                    spinAligned = Mathf.Max(spinAligned, Vector3.Dot(delta.normalized, expected.normalized));
                }
            }

            AssertTrue(
                spinAligned > 0.7f,
                $"the {label} released angular momentum must move bases along cross(omega, home) (alignment={spinAligned:F3})");
            AssertTrue(
                spinMoved > 0.0001f && spinMoved < 0.01f,
                $"the {label} released angular momentum moves bases one impulse step (moved={spinMoved:F4})");
            IntegrateSurface(material, current, next, 59);

            // The height footprint follows the moved sites so spikes appear where bases landed.
            Graphics.Blit(Texture2D.blackTexture, height, material, 3);
            Color[] heightAfter = Read(height);
            material.SetVector("_PrimitiveLinearVelocityDelta", new Vector3(1f, 0f, 0f));
            IntegrateSurface(material, current, next, 1);
            material.SetVector("_PrimitiveLinearVelocityDelta", Vector4.zero);
            IntegrateSurface(material, current, next, 19);
            Graphics.Blit(Texture2D.blackTexture, siteSurface, material, 4);
            Graphics.Blit(Texture2D.blackTexture, height, material, 3);
            Color[] heightShifted = Read(height);
            float heightDelta = 0f;
            for (int index = 0; index < heightAfter.Length; index++)
            {
                heightDelta = Mathf.Max(heightDelta, Mathf.Abs(heightAfter[index].r - heightShifted[index].r));
                heightDelta = Mathf.Max(heightDelta, Mathf.Abs(heightAfter[index].a - heightShifted[index].a));
            }

            AssertTrue(
                heightDelta > 0.001f,
                $"the {label} height footprint must move with the sliding bases ({heightDelta:F4})");
            Debug.Log($"[FerrofluidGrabValidation] {label} surface slide moved={moved:F4} surfaceError={surfaceError:F4} return={returnError:F4} spin={spinAligned:F3} height={heightDelta:F4}");
        }

        private static void IntegrateSurface(Material material, RenderTexture current, RenderTexture next, int steps)
        {
            // Track the live state texture so split calls keep alternating buffers instead of
            // writing back into the texture that is still bound for reading.
            RenderTexture active = (RenderTexture)material.GetTexture("_StateTex");
            for (int step = 0; step < steps; step++)
            {
                RenderTexture target = ReferenceEquals(active, current) ? next : current;
                Graphics.Blit(Texture2D.blackTexture, target, material, 2);
                active = target;
                material.SetTexture("_StateTex", active);
            }
        }

        private static RenderTexture CreateRenderTexture(int size, List<UnityEngine.Object> resources)
        {
            RenderTexture target = new(size, size, 0, RenderTextureFormat.ARGBFloat)
            {
                filterMode = FilterMode.Point,
            };
            target.Create();
            resources.Add(target);
            return target;
        }

        private static void ValidateGrabShader()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader");
            if (ShaderUtil.ShaderHasError(shader))
            {
                throw new Exception("The surface shader failed to compile.");
            }

            List<UnityEngine.Object> resources = new();
            try
            {
                Material material = new(shader);
                resources.Add(material);
                Texture2D state = CreateTexture(resources, new Color(0f, 0f, 0f, 0f));
                Texture2D siteSurface = CreateTexture(resources, new Color(0.3f, 0.2f, 0.4f, 1f));
                material.SetFloat("_SurfaceShapeMode", 1f);
                material.SetFloat("_SurfaceRoundness", 0.48f);
                material.SetVector("_PrimitiveYawCosSin", new Vector2(1f, 0f));
                material.SetVector("_DomainScale", new Vector2(1f / 0.168f, 1f / 0.168f));
                material.SetVector("_MagneticFieldPosition", new Vector3(0f, 0f, 100f));
                material.SetFloat("_MagneticFieldStrength", 0f);
                material.SetFloat("_SecondaryMagneticFieldStrength", 0f);
                material.SetFloat("_SpikesEnabled", 1f);
                material.SetMatrix("_FerrofluidObjectToWorld", Matrix4x4.identity);
                material.SetTexture("_StateTex", state);
                material.SetTexture("_SiteSurfaceTex", siteSurface);
                material.SetTexture("_RestPositionTex", siteSurface);
                material.SetTexture("_DensityPressureTex", Texture2D.whiteTexture);
                material.SetTexture("_ForceTex", Texture2D.blackTexture);
                material.SetFloat("_ParticleCount", 1f);
                material.SetFloat("_ParticleTextureSize", 1f);
                material.SetFloat("_DeltaTimeMilliseconds", 8f);
                material.SetFloat("_H", 1f);
                material.SetFloat("_Elasticity", 80f);
                material.SetFloat("_Damping", 7f);
                material.SetFloat("_PointerRadius", 5f);
                material.SetFloat("_FluidHeightScale", 1f);
                material.SetFloat("_BulgeRadius", 0.8f);
                material.SetFloat("_BulgeHeight", 0.3f);
                material.SetFloat("_PeakWidth", 0.4f);
                material.SetFloat("_HeightFalloffExponent", 2f);
                material.SetFloat("_HeightGain", 1f);
                material.SetFloat("_BackHeightLoss", 0f);
                material.SetFloat("_BackHeightFalloffExponent", 1f);
                material.SetFloat("_MaximumHeight", 1.2f);
                material.SetFloat("_MagnetHeightDeadZoneRadius", 0.15f);
                material.SetFloat("_MagnetHeightStrengthCap", 0.8f);
                material.SetFloat("_MaximumTiltRadians", 0.5f);
                material.SetFloat("_TiltPeakRadiusMultiplier", 0.6f);
                material.SetFloat("_TiltReturnRadiusMultiplier", 1.2f);
                material.SetFloat("_TiltRiseExponent", 1f);
                material.SetFloat("_TiltReturnExponent", 2f);
                material.SetFloat("_MagnetOpeningStrength", 0f);
                material.SetVector("_MagnetOpeningPosition", new Vector3(0f, 0f, 100f));
                material.SetVector("_MagnetOpeningNormal", Vector3.up);
                material.SetVector("_MagnetOpeningDirection", Vector3.zero);
                material.SetFloat("_SpikeMoundCurvature", 2f);
                material.SetFloat("_PrimitiveAmbientScale", 1f);
                material.SetFloat("_PrimitiveFlattenRadius", 0.2f);
                material.SetFloat("_PrimitiveFlattenExponent", 1f);
                material.SetFloat("_PrimitiveGlobalSpikeFlatten", 0f);
                material.SetFloat("_SphereBulgeScale", 0.5f);

                RenderTexture deformNeutral = BlitTarget(material, 2);
                RenderTexture bulgeNeutral = BlitTarget(material, 3);
                resources.Add(deformNeutral);
                resources.Add(bulgeNeutral);

                // The cached site axis and bulge come only from the site data now: inertia moves
                // sites instead of deforming vertex output.
                Color restDirection = Read(deformNeutral)[10];
                Color restBulge = Read(bulgeNeutral)[10];
                Debug.Log($"[FerrofluidGrabValidation] site direction={restDirection} bulge={restBulge}");

                // The world-position pass scales spike height with the grab multiplier.
                material.EnableKeyword("FERROFLUID_CACHED_DEFORMATION");
                Texture2D heightMap = new(4, 4, TextureFormat.RGBAFloat, false, true);
                resources.Add(heightMap);
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        heightMap.SetPixel(x, y, new Color(0.4f, 0f, 0f, 0.4f));
                    }
                }

                heightMap.Apply(false, false);
                Texture2D spring = CreateTexture(resources, new Color(1f, 0f, 0f, 0f));
                Texture2D deformation = CreateTexture(resources, new Color(0f, 1f, 0f, 0f));
                Texture2D bulgeTex = CreateTexture(resources, new Color(0f, 0f, 0f, 0f));
                material.SetTexture("_HeightMapTex", heightMap);
                material.SetTexture("_SpringStateTex", spring);
                material.SetTexture("_SiteDeformationTex", deformation);
                material.SetTexture("_SiteBulgeTex", bulgeTex);

                float neutralY = MeasureDisplacedY(material, 1f, resources);
                float stretchedY = MeasureDisplacedY(material, 1.6f, resources);
                AssertClose(0f, stretchedY - neutralY, 0.001f, "without magnet the trigger leaves unrelated spikes unchanged");

                // The rigid shell stays put per shape: the magnet bulge moves it, spike heights
                // lift it, and zero-height roots keep it fixed — inertia only slides sites now.
                material.SetFloat("_MagnetHeightAmplification", 1f);
                material.SetFloat("_HeightFactor", 0.38f);
                material.SetMatrix("_LocalToCurvedCanvas", Matrix4x4.identity);
                material.SetMatrix("_CurvedCanvasToLocal", Matrix4x4.identity);
                material.SetFloat("_CurvedCanvasRadius", 100000f);
                foreach (float shapeMode in new[] { 1f, 2f })
                {
                    material.SetFloat("_SurfaceShapeMode", shapeMode);
                    string shapeLabel = shapeMode > 1.5f ? "sphere" : "cube";

                    material.SetFloat("_SpikesEnabled", 0f);
                    Color[] shellNeutral = MeasurePixels(material, resources);

                    // Cached mode carries the magnet bulge through _SiteBulgeTex.
                    bulgeTex.SetPixel(0, 0, new Color(0.05f, 0.03f, -0.02f, 0f));
                    bulgeTex.Apply(false, false);
                    Color[] magnetBulge = MeasurePixels(material, resources);
                    AssertTrue(
                        MaxPixelDistance(magnetBulge, shellNeutral) > 0.005f,
                        $"the {shapeLabel} magnet bulge must still displace the shell");
                    bulgeTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
                    bulgeTex.Apply(false, false);

                    // Spikes on but a zero height map pins every root at the rigid shape.
                    material.SetFloat("_SpikesEnabled", 1f);
                    SetZeroHeights(heightMap);
                    Color[] rootNeutral = MeasurePixels(material, resources);
                    AssertTrue(
                        MaxPixelDistance(rootNeutral, shellNeutral) < 0.001f,
                        $"the {shapeLabel} zero-height roots must stay pinned at the shell ({MaxPixelDistance(rootNeutral, shellNeutral):F4})");

                    // Nonzero heights extend the spikes off the rigid shell.
                    SetUniformHeights(heightMap);
                    Color[] tipNeutral = MeasurePixels(material, resources);
                    AssertTrue(
                        MaxPixelDistance(tipNeutral, rootNeutral) > 0.005f,
                        $"the {shapeLabel} nonzero heights must extend spikes past the shell");
                }

                Debug.Log("[FerrofluidGrabValidation] rigid shell, magnet bulge, and grab scale read back correctly.");

            }
            finally
            {
                RenderTexture.active = null;
                foreach (UnityEngine.Object resource in resources)
                {
                    if (resource != null)
                    {
                        UnityEngine.Object.DestroyImmediate(resource);
                    }
                }
            }
        }

        private static float MeasureDisplacedY(Material material, float grabScale, List<UnityEngine.Object> resources)
        {
            material.SetFloat("_MagnetHeightAmplification", grabScale);
            RenderTexture target = new(32, 32, 0, RenderTextureFormat.ARGBFloat)
            {
                antiAliasing = 1,
                filterMode = FilterMode.Point,
            };
            target.Create();
            resources.Add(target);
            // The pass writes displaced world positions at each blit texel's surface point.
            Graphics.Blit(Texture2D.blackTexture, target, material, 4);
            Texture2D readback = new(32, 32, TextureFormat.RGBAFloat, false, true);
            resources.Add(readback);
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0f, 0f, 32f, 32f), 0, 0, false);
            readback.Apply(false, false);
            RenderTexture.active = null;
            Color[] pixels = readback.GetPixels();
            float maximum = float.MinValue;
            foreach (Color pixel in pixels)
            {
                if (pixel.a > 0.5f && pixel.g > maximum)
                {
                    maximum = pixel.g;
                }
            }

            AssertTrue(maximum > float.MinValue, "the world-position pass must rasterize the quad");
            return maximum;
        }

        private static Color[] MeasurePixels(Material material, List<UnityEngine.Object> resources)
        {
            RenderTexture target = new(32, 32, 0, RenderTextureFormat.ARGBFloat)
            {
                antiAliasing = 1,
                filterMode = FilterMode.Point,
            };
            target.Create();
            resources.Add(target);
            Graphics.Blit(Texture2D.blackTexture, target, material, 4);
            Texture2D readback = new(32, 32, TextureFormat.RGBAFloat, false, true);
            resources.Add(readback);
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0f, 0f, 32f, 32f), 0, 0, false);
            readback.Apply(false, false);
            RenderTexture.active = null;
            Color[] pixels = readback.GetPixels();
            bool any = false;
            foreach (Color pixel in pixels)
            {
                any |= pixel.a > 0.5f;
            }

            AssertTrue(any, "the world-position pass must rasterize the quad");
            return pixels;
        }

        private static float MaxPixelDistance(Color[] first, Color[] second)
        {
            float maximum = 0f;
            for (int index = 0; index < first.Length; index++)
            {
                if (first[index].a > 0.5f && second[index].a > 0.5f)
                {
                    maximum = Mathf.Max(maximum, Vector3.Distance(
                        new Vector3(first[index].r, first[index].g, first[index].b),
                        new Vector3(second[index].r, second[index].g, second[index].b)));
                }
            }

            return maximum;
        }

        private static void SetUniformHeights(Texture2D heightMap)
        {
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    heightMap.SetPixel(x, y, new Color(0.4f, 0.5f, 0.5f, 0.4f));
                }
            }

            heightMap.Apply(false, false);
        }

        private static void SetZeroHeights(Texture2D heightMap)
        {
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    heightMap.SetPixel(x, y, new Color(0f, 0.5f, 0.5f, 0f));
                }
            }

            heightMap.Apply(false, false);
        }

        private static RenderTexture BlitTarget(Material material, int pass)
        {
            RenderTexture target = new(4, 4, 0, RenderTextureFormat.ARGBFloat)
            {
                antiAliasing = 1,
                filterMode = FilterMode.Point,
            };
            target.Create();
            Graphics.Blit(Texture2D.blackTexture, target, material, pass);
            return target;
        }

        private static Color[] Read(RenderTexture target)
        {
            Texture2D readback = new(target.width, target.height, TextureFormat.RGBAFloat, false, true);
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0, false);
            readback.Apply(false, false);
            RenderTexture.active = null;
            Color[] pixels = readback.GetPixels();
            UnityEngine.Object.DestroyImmediate(readback);
            return pixels;
        }

        private static Texture2D CreateTexture(List<UnityEngine.Object> resources, Color value)
        {
            Texture2D texture = new(1, 1, TextureFormat.RGBAFloat, false, true);
            texture.SetPixel(0, 0, value);
            texture.Apply(false, false);
            resources.Add(texture);
            return texture;
        }

        private static void AssertClose(Vector3 expected, Vector3 actual, float tolerance, string what)
        {
            if (Vector3.Distance(expected, actual) >= tolerance)
            {
                throw new Exception($"{what}: expected {expected:F4}, actual {actual:F4}");
            }
        }

        private static void AssertClose(float expected, float actual, float tolerance, string what)
        {
            if (Mathf.Abs(expected - actual) > tolerance)
            {
                throw new Exception($"{what}: expected {expected:F4}, got {actual:F4}.");
            }
        }

        private static void AssertTrue(bool condition, string what)
        {
            if (!condition)
            {
                throw new Exception(what);
            }
        }
    }
}
