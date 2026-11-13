using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChromaGLS.FerrofluidBundle
{
    // Native baseline for the travelling-top requirement: each case builds a real
    // FerrofluidPhysicsWorld, drives it with World.Step/Render only, and scores the captured
    // rigidbody trace with FerrofluidTopPhysicsAssertions. Every case still runs after a
    // failure so the report shows the whole matrix at once.
    internal static class FerrofluidTopPhysicsValidation
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        private static Type BodyType = null!;
        private static Type WorldType = null!;
        private static object Enclosure = null!;
        private static object Ownership = null!;
        private static object Config = null!;
        private static PhysicsMaterial BodyMaterial = null!;
        private static PhysicsMaterial FloorMaterial = null!;
        private static string OutputDir = null!;
        private static int InitialSceneCount;
        private static bool MetadataWritten;

        private static readonly List<UnityEngine.Object> Shared = new();

        [Serializable]
        private sealed class CheckOutcome
        {
            public string name = string.Empty;
            public bool passed;
            public string failure = string.Empty;
            public Vector3 beforeMomentum;
            public Vector3 afterMomentum;
            public Vector3 beforeVelocity;
            public Vector3 afterVelocity;
        }

        [Serializable]
        private sealed class ColliderMetadata
        {
            public string colliderType = string.Empty;
            public Vector3 boxSize;
            public Vector3 boxCenter;
            public int sourceMeshVertexCount;
            public int sourceMeshTriangleCount;
            public bool convex;
            public Vector3 boundsCenter;
            public Vector3 boundsSize;
            public float mass;
            public Vector3 inertiaTensor;
            public Quaternion inertiaTensorRotation;
            public float cornerDynamicFriction;
            public float cornerStaticFriction;
            public float floorContactDynamicFriction;
            public float floorContactStaticFriction;
            public float floorMaterialDynamicFriction;
            public float floorMaterialStaticFriction;
            public float floorMaterialBounciness;
        }

        [Serializable]
        private sealed class RunReport
        {
            public string pluginPath = string.Empty;
            public string pluginSha256 = string.Empty;
            public string unityVersion = string.Empty;
            public bool developmentBuild;
            public List<FerrofluidTopPhysicsAssertions.Result> grounded = new();
            public List<CheckOutcome> checks = new();
        }

        public static void Run()
            => FerrofluidValidationRunner.Run(typeof(FerrofluidTopPhysicsValidation));

        private static IEnumerator RunRuntime()
        {
            SimulationMode previousMode = Physics.simulationMode;
            float previousTimeScale = Time.timeScale;
            float previousDelta = Time.fixedDeltaTime;
            Physics.simulationMode = SimulationMode.Script;
            Time.timeScale = 0f;
            InitialSceneCount = SceneManager.sceneCount;
            RunReport report = new();

            try
            {
                string pluginPath = Environment.GetEnvironmentVariable("FERROFLUID_VALIDATION_PLUGIN")!;
                Assembly plugin = Assembly.LoadFrom(pluginPath);
                report.pluginPath = pluginPath;
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] digest = sha.ComputeHash(File.ReadAllBytes(pluginPath));
                    var hex = new System.Text.StringBuilder(digest.Length * 2);
                    foreach (byte b in digest)
                        hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                    report.pluginSha256 = hex.ToString();
                }
                report.unityVersion = Application.unityVersion;
                report.developmentBuild = Debug.isDebugBuild;
                Debug.Log($"[TopPhysics] plugin={pluginPath} sha={report.pluginSha256} unity={report.unityVersion} debug={report.developmentBuild}");

                BodyType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true)!;
                WorldType = plugin.GetType("ChromaGLS.Settings.FerrofluidPhysicsWorld", true)!;
                Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true)!;
                Enclosure = enclosureType.GetConstructor(
                    Instance,
                    null,
                    new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                    null)!
                    .Invoke(new object[] { Vector3.zero, 1000f, new Vector3(0f, 0f, -1000f), Vector3.forward, 0f });
                Ownership = Activator.CreateInstance(plugin.GetType("ChromaGLS.Settings.FerrofluidGrabOwnership", true)!, nonPublic: true);
                Config = Activator.CreateInstance(plugin.GetType("ChromaGLS.Settings.Config", true)!, nonPublic: true);
                Type environment = plugin.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true)!;
                MethodInfo contactMaterial = environment.GetMethod("CreateContactMaterial", Static)!;
                FloorMaterial = (PhysicsMaterial)contactMaterial.Invoke(null, new object[] { true })!;
                BodyMaterial = (PhysicsMaterial)contactMaterial.Invoke(null, new object[] { false })!;
                Shared.Add(FloorMaterial);
                Shared.Add(BodyMaterial);

                OutputDir = Environment.GetEnvironmentVariable("FERROFLUID_TOP_VALIDATION_OUTPUT")!;
                Directory.CreateDirectory(OutputDir);
                MetadataWritten = false;
                string caseMode = Environment.GetEnvironmentVariable("FERROFLUID_TOP_VALIDATION_CASES") ?? string.Empty;
                bool releaseOnly = caseMode == "controller-release";
                bool dropOnly = caseMode == "controller-drop";
                bool grabFloorOnly = caseMode == "grab-floor" || caseMode == "grab-floor-tip";
                bool topMatrixOnly = caseMode == "top-matrix";
                bool landingOnly = caseMode == "gentle-landing";
                bool floorGripOnly = caseMode == "floor-grip";

                if (!releaseOnly && !dropOnly && !grabFloorOnly && !landingOnly && !floorGripOnly)
                {
                    foreach (FerrofluidTopPhysicsAssertions.Case test in FerrofluidTopPhysicsAssertions.Cases())
                    {
                        IEnumerator run = GroundedCase(test, report);
                        while (run.MoveNext())
                            yield return run.Current;
                    }
                }

                foreach (float deltaTime in new[] { 0.02f, 1f / 90f })
                {
                    if (!releaseOnly && !dropOnly && !grabFloorOnly && !topMatrixOnly && !landingOnly)
                    {
                        foreach (bool held in new[] { false, true })
                        {
                            foreach (int direction in new[] { -1, 1 })
                            {
                                IEnumerator grip = FloorGripCase(deltaTime, held, direction, report);
                                while (grip.MoveNext())
                                    yield return grip.Current;
                            }
                        }
                    }
                    if (floorGripOnly)
                        continue;

                    if (!releaseOnly && !dropOnly && !grabFloorOnly && !topMatrixOnly)
                    {
                        foreach (FerrofluidGentleLandingAssertions.Case test in FerrofluidGentleLandingAssertions.Cases(deltaTime))
                        {
                            IEnumerator landing = OwnedLandingCase(test, report);
                            while (landing.MoveNext())
                                yield return landing.Current;
                        }
                    }
                    if (landingOnly)
                        continue;

                    if (!releaseOnly && !dropOnly && !topMatrixOnly)
                    {
                        foreach (bool tip in new[] { false, true })
                        {
                            if (caseMode == "grab-floor-tip" && !tip)
                                continue;
                            foreach (int direction in new[] { -1, 1 })
                            {
                                IEnumerator manipulation = GrabFloorCase(deltaTime, tip, direction, report);
                                while (manipulation.MoveNext())
                                    yield return manipulation.Current;
                            }
                        }
                    }
                    if (grabFloorOnly)
                        continue;

                    if (dropOnly)
                    {
                        IEnumerator drop = DropCase(deltaTime, report);
                        while (drop.MoveNext())
                            yield return drop.Current;
                        continue;
                    }

                    if (!releaseOnly)
                    {
                        IEnumerator torqueFree = TorqueFreeCase(deltaTime, report);
                        while (torqueFree.MoveNext())
                            yield return torqueFree.Current;
                    }

                    IEnumerator release = ReleaseCase(deltaTime, report);
                    while (release.MoveNext())
                        yield return release.Current;
                }
            }
            finally
            {
                Physics.simulationMode = previousMode;
                Time.timeScale = previousTimeScale;
                Time.fixedDeltaTime = previousDelta;
                foreach (UnityEngine.Object item in Shared)
                {
                    if (item != null)
                        UnityEngine.Object.Destroy(item);
                }

                Shared.Clear();
                if (!string.IsNullOrEmpty(OutputDir))
                    File.WriteAllText(Path.Combine(OutputDir, "results.json"), JsonUtility.ToJson(report, prettyPrint: true));
            }

            int failed = 0;
            foreach (FerrofluidTopPhysicsAssertions.Result result in report.grounded)
                failed += result.passed ? 0 : 1;
            foreach (CheckOutcome check in report.checks)
                failed += check.passed ? 0 : 1;
            Debug.Log($"[TopPhysics] COMPLETE grounded={report.grounded.Count} checks={report.checks.Count} failed={failed}");
            if (failed > 0)
                throw new InvalidOperationException($"{failed} top-physics cases failed; see results.json.");
        }

        private static Component NewCube(object world, Vector3 position, Quaternion rotation, Collider floor)
        {
            GameObject go = new("top validation cube");
            go.transform.position = position;
            go.transform.localScale = Vector3.one * FerrofluidTopPhysicsAssertions.BodyScale;
            go.transform.rotation = rotation;
            Component component = go.AddComponent(BodyType);
            BodyType.GetMethod("ConfigurePhysicsWorld", Instance)!.Invoke(component, new[] { world });
            BodyType.GetMethod("Initialize", Instance)!.Invoke(component, new[] { Enclosure, (object)false, BodyMaterial, Ownership });
            if (floor != null)
                BodyType.GetMethod("ConfigureFloorContact", Instance)!.Invoke(component, new object[] { floor });
            BodyType.GetMethod("ConfigureCubeRoundness", Instance)!.Invoke(component, new object[] { FerrofluidTopPhysicsAssertions.Roundness });
            BodyType.GetMethod("ConfigureContactPhysics", Instance)!.Invoke(component, new[] { Config });
            return component;
        }

        private static Quaternion InitialTilt(float tiltDegrees)
            => Quaternion.AngleAxis(tiltDegrees, Vector3.forward)
                * Quaternion.FromToRotation(Vector3.one.normalized, Vector3.up);

        private static Rigidbody Body(Component component)
            => (Rigidbody)BodyType.GetField("_body", Instance)!.GetValue(component)!;

        private static FerrofluidTopPhysicsAssertions.Sample Capture(float time, Rigidbody rb, Collider collider)
        {
            Quaternion inertiaFrame = rb.rotation * rb.inertiaTensorRotation;
            FerrofluidTopPhysicsAssertions.Sample sample = new()
            {
                Time = time,
                Position = rb.worldCenterOfMass,
                Rotation = rb.rotation,
                Velocity = rb.linearVelocity,
                AngularVelocity = rb.angularVelocity,
                AngularMomentum = inertiaFrame * Vector3.Scale(
                    rb.inertiaTensor,
                    Quaternion.Inverse(inertiaFrame) * rb.angularVelocity),
                ColliderSupportPoint = collider.ClosestPoint(
                    rb.worldCenterOfMass - Vector3.up * (1000f * FerrofluidTopPhysicsAssertions.BodyScale)),
            };
            return sample;
        }

        private static IEnumerator GroundedCase(FerrofluidTopPhysicsAssertions.Case test, RunReport report)
        {
            Time.fixedDeltaTime = test.DeltaTime;
            object world = Activator.CreateInstance(WorldType, nonPublic: true);
            List<UnityEngine.Object> caseResources = new();

            GameObject floorObject = new("top validation floor");
            caseResources.Add(floorObject);
            floorObject.layer = 3;
            floorObject.transform.position = new Vector3(0f, -0.5f, 0f);
            WorldType.GetMethod("MoveRootToScene", Instance)!.Invoke(world, new object[] { floorObject });
            BoxCollider floor = floorObject.AddComponent<BoxCollider>();
            floor.size = new Vector3(200f, 1f, 200f);
            floor.sharedMaterial = FloorMaterial;

            List<FerrofluidTopPhysicsAssertions.Sample> samples;
            GameObject owner = null;
            try
            {
                Component body = NewCube(world, new Vector3(0f, 10f, 0f), InitialTilt(test.TiltDegrees), floor);
                owner = body.gameObject;
                caseResources.Add(owner);
                Rigidbody rb = Body(body);
                Collider collider = rb.GetComponentInChildren<Collider>()!;
                WriteMetadata(rb, collider, body);

                Physics.SyncTransforms();
                Vector3 support = collider.ClosestPoint(
                    rb.worldCenterOfMass - Vector3.up * (1000f * FerrofluidTopPhysicsAssertions.BodyScale));
                rb.position += Vector3.up * (0.002f - support.y);
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = (test.WorldUpSpin ? Vector3.up : rb.rotation * Vector3.one.normalized)
                    * (2f * Mathf.PI * test.RevolutionsPerSecond);

                samples = new List<FerrofluidTopPhysicsAssertions.Sample> { Capture(0f, rb, collider) };
                MethodInfo step = WorldType.GetMethod("Step", Instance)!;
                MethodInfo render = WorldType.GetMethod("Render", Instance)!;
                int steps = Mathf.CeilToInt(FerrofluidTopPhysicsAssertions.Duration / test.DeltaTime);
                for (int i = 0; i < steps; i++)
                {
                    float time = (i + 1) * test.DeltaTime;
                    step.Invoke(world, new object[] { test.DeltaTime, test.DeltaTime, time });
                    render.Invoke(world, new object[] { 1f });
                    Physics.SyncTransforms();
                    samples.Add(Capture(time, rb, collider));
                    yield return null;
                }
            }
            finally
            {
                owner?.SetActive(false);
                WorldType.GetMethod("Dispose", Instance)!.Invoke(world, null);
                foreach (UnityEngine.Object item in caseResources)
                {
                    if (item != null)
                        UnityEngine.Object.Destroy(item);
                }
            }

            while (SceneManager.sceneCount > InitialSceneCount)
                yield return null;

            report.grounded.Add(FerrofluidTopPhysicsAssertions.EvaluateGrounded(test.Name, samples));
            WriteSamplesCsv(test.Name, samples);
        }

        private static IEnumerator TorqueFreeCase(float deltaTime, RunReport report)
        {
            Time.fixedDeltaTime = deltaTime;
            object world = Activator.CreateInstance(WorldType, nonPublic: true);
            List<UnityEngine.Object> caseResources = new();

            List<FerrofluidTopPhysicsAssertions.Sample> samples;
            GameObject owner = null;
            try
            {
                Component body = NewCube(world, new Vector3(0f, 100f, 0f), InitialTilt(10f), null!);
                owner = body.gameObject;
                caseResources.Add(owner);
                Rigidbody rb = Body(body);
                Collider collider = rb.GetComponentInChildren<Collider>()!;
                rb.angularDamping = 0f;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = new Vector3(10f, 15f, -5f);

                samples = new List<FerrofluidTopPhysicsAssertions.Sample> { Capture(0f, rb, collider) };
                MethodInfo step = WorldType.GetMethod("Step", Instance)!;
                MethodInfo render = WorldType.GetMethod("Render", Instance)!;
                int steps = Mathf.CeilToInt(2f / deltaTime);
                for (int i = 0; i < steps; i++)
                {
                    float time = (i + 1) * deltaTime;
                    step.Invoke(world, new object[] { deltaTime, deltaTime, time });
                    render.Invoke(world, new object[] { 1f });
                    Physics.SyncTransforms();
                    samples.Add(Capture(time, rb, collider));
                    yield return null;
                }
            }
            finally
            {
                owner?.SetActive(false);
                WorldType.GetMethod("Dispose", Instance)!.Invoke(world, null);
                foreach (UnityEngine.Object item in caseResources)
                {
                    if (item != null)
                        UnityEngine.Object.Destroy(item);
                }
            }

            while (SceneManager.sceneCount > InitialSceneCount)
                yield return null;

            CheckOutcome outcome = new() { name = $"torque-free-dt{deltaTime:F5}" };
            try
            {
                FerrofluidTopPhysicsAssertions.AssertTorqueFreeMomentum(samples);
                outcome.passed = true;
            }
            catch (Exception exception)
            {
                outcome.failure = exception.Message;
            }

            report.checks.Add(outcome);
            WriteSamplesCsv(outcome.name, samples);
        }

        private static IEnumerator ReleaseCase(float deltaTime, RunReport report)
        {
            Time.fixedDeltaTime = deltaTime;
            object world = Activator.CreateInstance(WorldType, nonPublic: true);
            List<UnityEngine.Object> caseResources = new();

            List<FerrofluidTopPhysicsAssertions.Sample> samples;
            Vector3 beforeMomentum = Vector3.zero;
            Vector3 afterMomentum = Vector3.zero;
            Vector3 beforeVelocity = Vector3.zero;
            Vector3 afterVelocity = Vector3.zero;
            GameObject owner = null;
            try
            {
                Component body = NewCube(world, new Vector3(0f, 100f, 0f), InitialTilt(10f), null!);
                owner = body.gameObject;
                caseResources.Add(owner);
                Rigidbody rb = Body(body);
                Collider collider = rb.GetComponentInChildren<Collider>()!;
                WriteMetadata(rb, collider, body);
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;

                GameObject attach = new("top validation grab anchor");
                caseResources.Add(attach);
                attach.transform.position = rb.position + rb.rotation * (Vector3.up * 0.5f * FerrofluidTopPhysicsAssertions.BodyScale);
                Physics.SyncTransforms();

                Vector3 freeTensor = (Vector3)BodyType.GetField("_freeInertiaTensor", Instance)!.GetValue(body)!;
                Quaternion freeTensorRotation = (Quaternion)BodyType.GetField("_freeInertiaRotation", Instance)!.GetValue(body)!;
                Vector3 freeCenterOfMass = (Vector3)BodyType.GetField("_freeCenterOfMass", Instance)!.GetValue(body)!;

                BodyType.GetMethod("Acquire", Instance)!.Invoke(body, new object[] { attach.transform, attach.transform.position });
                BodyType.GetMethod("SetControllerInput", Instance)!.Invoke(
                    body,
                    new object[] { Vector2.zero, new Vector2(-1f, 0f), Quaternion.identity, Vector3.right });

                MethodInfo sampleGrab = BodyType.GetMethod("SampleGrabTarget", Instance)!;
                MethodInfo step = WorldType.GetMethod("Step", Instance)!;
                MethodInfo render = WorldType.GetMethod("Render", Instance)!;
                samples = new List<FerrofluidTopPhysicsAssertions.Sample> { Capture(0f, rb, collider) };
                int steps = Mathf.CeilToInt(2f / deltaTime);
                for (int i = 0; i < steps; i++)
                {
                    float time = (i + 1) * deltaTime;
                    sampleGrab.Invoke(body, new object[] { deltaTime, time });
                    step.Invoke(world, new object[] { deltaTime, deltaTime, time });
                    render.Invoke(world, new object[] { 1f });
                    Physics.SyncTransforms();
                    samples.Add(Capture(time, rb, collider));
                    yield return null;
                }

                Quaternion frame = rb.rotation * freeTensorRotation;
                beforeMomentum = frame * Vector3.Scale(freeTensor, Quaternion.Inverse(frame) * rb.angularVelocity);
                Vector3 freeCenter = rb.position + rb.rotation * freeCenterOfMass;
                beforeVelocity = rb.GetPointVelocity(freeCenter);
                BodyType.GetMethod("Release", Instance)!.Invoke(body, null);
                Quaternion afterFrame = rb.rotation * freeTensorRotation;
                afterMomentum = afterFrame * Vector3.Scale(freeTensor, Quaternion.Inverse(afterFrame) * rb.angularVelocity);
                Vector3 afterCenter = rb.position + rb.rotation * freeCenterOfMass;
                afterVelocity = rb.GetPointVelocity(afterCenter);
            }
            finally
            {
                owner?.SetActive(false);
                WorldType.GetMethod("Dispose", Instance)!.Invoke(world, null);
                foreach (UnityEngine.Object item in caseResources)
                {
                    if (item != null)
                        UnityEngine.Object.Destroy(item);
                }
            }

            while (SceneManager.sceneCount > InitialSceneCount)
                yield return null;

            CheckOutcome outcome = new()
            {
                name = $"controller-release-dt{deltaTime:F5}",
                beforeMomentum = beforeMomentum,
                afterMomentum = afterMomentum,
                beforeVelocity = beforeVelocity,
                afterVelocity = afterVelocity,
            };
            try
            {
                FerrofluidTopPhysicsAssertions.AssertRelease(beforeMomentum, afterMomentum, beforeVelocity, afterVelocity);
                outcome.passed = true;
            }
            catch (Exception exception)
            {
                outcome.failure = exception.Message;
            }

            report.checks.Add(outcome);
            WriteSamplesCsv(outcome.name, samples);
        }

        private static IEnumerator OwnedLandingCase(FerrofluidGentleLandingAssertions.Case test, RunReport report)
        {
            Time.fixedDeltaTime = test.DeltaTime;
            object world = Activator.CreateInstance(WorldType, nonPublic: true);
            List<UnityEngine.Object> resources = new();
            List<FerrofluidTopPhysicsAssertions.Sample> samples = new();
            GameObject owner = null;
            try
            {
                GameObject floorObject = new("owned landing validation floor");
                resources.Add(floorObject);
                floorObject.layer = 3;
                floorObject.transform.position = new Vector3(0f, -0.5f, 0f);
                WorldType.GetMethod("MoveRootToScene", Instance)!.Invoke(world, new object[] { floorObject });
                BoxCollider floor = floorObject.AddComponent<BoxCollider>();
                floor.size = new Vector3(200f, 1f, 200f);
                floor.sharedMaterial = FloorMaterial;
                Component body = NewCube(world, Vector3.up * 10f, test.Rotation, floor);
                owner = body.gameObject;
                resources.Add(owner);
                Rigidbody rb = Body(body);
                Collider collider = rb.GetComponentInChildren<Collider>()!;
                Physics.SyncTransforms();
                Vector3 support = collider.ClosestPoint(
                    rb.worldCenterOfMass - Vector3.up * (1000f * FerrofluidTopPhysicsAssertions.BodyScale));
                rb.position += Vector3.up * (0.002f - support.y);
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                WriteMetadata(rb, collider, body);
                samples.Add(Capture(0f, rb, collider));
                MethodInfo step = WorldType.GetMethod("Step", Instance)!;
                MethodInfo render = WorldType.GetMethod("Render", Instance)!;
                int steps = Mathf.RoundToInt(FerrofluidGentleLandingAssertions.Duration / test.DeltaTime);
                for (int i = 0; i < steps; i++)
                {
                    float time = (i + 1) * test.DeltaTime;
                    step.Invoke(world, new object[] { test.DeltaTime, test.DeltaTime, time });
                    render.Invoke(world, new object[] { 1f });
                    Physics.SyncTransforms();
                    samples.Add(Capture(time, rb, collider));
                    yield return null;
                }
            }
            finally
            {
                if (owner != null)
                    owner.SetActive(false);
                WorldType.GetMethod("Dispose", Instance)!.Invoke(world, null);
                foreach (UnityEngine.Object resource in resources)
                {
                    if (resource != null)
                        UnityEngine.Object.Destroy(resource);
                }
            }

            while (SceneManager.sceneCount > InitialSceneCount)
                yield return null;

            FerrofluidGentleLandingAssertions.Result result = FerrofluidGentleLandingAssertions.Evaluate(test, samples);
            report.checks.Add(new CheckOutcome
            {
                name = test.Name,
                passed = result.passed,
                failure = string.Join(" | ", result.failures),
            });
            WriteSamplesCsv(test.Name, samples);
            File.WriteAllText(Path.Combine(OutputDir, $"{test.Name}-landing.json"), JsonUtility.ToJson(result, true));
        }

        private static IEnumerator FloorGripCase(float deltaTime, bool held, int direction, RunReport report)
        {
            Time.fixedDeltaTime = deltaTime;
            object world = Activator.CreateInstance(WorldType, nonPublic: true);
            List<UnityEngine.Object> resources = new();
            List<FerrofluidFloorGripAssertions.Sample> observations = new();
            List<FerrofluidTopPhysicsAssertions.Sample> samples = new();
            List<string> materialRows = new()
            {
                "time,held,bodyDynamic,bodyStatic,bodyCombine,floorDynamic,floorStatic,floorCombine,effectiveDynamic,effectiveStatic",
            };
            string name = $"floor-grip-{(held ? "held" : "free")}-sign{direction}-dt{deltaTime:F5}";
            GameObject owner = null;
            float width = 0f;
            try
            {
                GameObject floorObject = new("floor grip validation floor");
                resources.Add(floorObject);
                floorObject.layer = 3;
                floorObject.transform.position = new Vector3(0f, -0.5f, 0f);
                WorldType.GetMethod("MoveRootToScene", Instance)!.Invoke(world, new object[] { floorObject });
                BoxCollider floor = floorObject.AddComponent<BoxCollider>();
                floor.size = new Vector3(200f, 1f, 200f);
                floor.sharedMaterial = FloorMaterial;
                Component body = NewCube(world, Vector3.up, Quaternion.identity, floor);
                owner = body.gameObject;
                resources.Add(owner);
                object config = Activator.CreateInstance(Config.GetType(), nonPublic: true)!;
                config.GetType().GetProperty("FerrofluidCubeFloorFriction")!.SetValue(config, FerrofluidFloorGripAssertions.ConfiguredFriction);
                BodyType.GetMethod("ConfigureContactPhysics", Instance)!.Invoke(body, new[] { config });
                Rigidbody rb = Body(body);
                Collider collider = rb.GetComponentInChildren<Collider>()!;
                Physics.SyncTransforms();
                width = collider.bounds.size.x;
                rb.position = new Vector3(0f, collider.bounds.extents.y + 0.002f, 0f);
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                Physics.SyncTransforms();
                WriteMetadata(rb, collider, body);
                GameObject attach = new("floor grip validation controller");
                resources.Add(attach);
                MethodInfo acquire = BodyType.GetMethod("Acquire", Instance)!;
                MethodInfo release = BodyType.GetMethod("Release", Instance)!;
                MethodInfo sampleGrab = BodyType.GetMethod("SampleGrabTarget", Instance)!;
                MethodInfo step = WorldType.GetMethod("Step", Instance)!;
                MethodInfo render = WorldType.GetMethod("Render", Instance)!;
                BodyType.GetMethod("SetControllerInput", Instance)!.Invoke(body,
                    new object[] { Vector2.zero, Vector2.zero, Quaternion.identity, Vector3.right });
                bool acquired = false;
                bool released = false;
                bool pushed = false;
                Vector3 startGrab = Vector3.zero;
                Vector3 endGrab = Vector3.zero;
                samples.Add(CaptureOrigin(0f, rb, collider));
                int steps = Mathf.RoundToInt(FerrofluidFloorGripAssertions.Duration / deltaTime);
                for (int i = 0; i < steps; i++)
                {
                    float time = i * deltaTime;
                    if (held && !acquired && time >= FerrofluidFloorGripAssertions.AcquireTime - 0.00001f)
                    {
                        Ray ray = new(new Vector3(rb.position.x - direction * 0.45f * width,
                            rb.position.y + 2f * width, rb.position.z), Vector3.down);
                        if (!collider.Raycast(ray, out RaycastHit hit, 4f * width))
                            throw new InvalidOperationException($"{name}: controller ray missed the trailing upper edge.");
                        startGrab = hit.point;
                        Vector3 groundedEdge = new(rb.position.x + direction * 0.5f * width, 0f, rb.position.z);
                        endGrab = groundedEdge
                            + Quaternion.AngleAxis(-direction * 30f, Vector3.forward) * (startGrab - groundedEdge);
                        attach.transform.position = startGrab;
                        acquire.Invoke(body, new object[] { attach.transform, startGrab });
                        acquired = true;
                    }
                    if (acquired && !released)
                    {
                        float fraction = Mathf.Clamp01((time - FerrofluidFloorGripAssertions.MoveStart)
                            / (FerrofluidFloorGripAssertions.MoveEnd - FerrofluidFloorGripAssertions.MoveStart));
                        // Use a straight hand sweep, with an endpoint high enough to tip 30 degrees.
                        attach.transform.position = Vector3.Lerp(startGrab, endGrab, fraction);
                        sampleGrab.Invoke(body, new object[] { deltaTime, time });
                        if (time >= FerrofluidFloorGripAssertions.ReleaseTime - 0.00001f)
                        {
                            release.Invoke(body, null);
                            released = true;
                        }
                    }
                    if (!held && !pushed && time >= FerrofluidFloorGripAssertions.PushTime - 0.00001f)
                    {
                        rb.AddForce(Vector3.right * (direction * rb.mass), ForceMode.Impulse);
                        pushed = true;
                    }
                    step.Invoke(world, new object[] { deltaTime, deltaTime, time + deltaTime });
                    render.Invoke(world, new object[] { 1f });
                    Physics.SyncTransforms();
                    FerrofluidTopPhysicsAssertions.Sample sample = CaptureOrigin(time + deltaTime, rb, collider);
                    samples.Add(sample);
                    PhysicsMaterial material = collider.sharedMaterial;
                    float dynamicFriction = FerrofluidFloorGripAssertions.CombineFriction(
                        material.dynamicFriction, material.frictionCombine, FloorMaterial.dynamicFriction, FloorMaterial.frictionCombine);
                    float staticFriction = FerrofluidFloorGripAssertions.CombineFriction(
                        material.staticFriction, material.frictionCombine, FloorMaterial.staticFriction, FloorMaterial.frictionCombine);
                    observations.Add(new FerrofluidFloorGripAssertions.Sample
                    {
                        Body = sample,
                        EffectiveDynamicFriction = dynamicFriction,
                        EffectiveStaticFriction = staticFriction,
                    });
                    materialRows.Add(string.Join(",",
                        F(sample.Time), acquired && !released ? "1" : "0",
                        F(material.dynamicFriction), F(material.staticFriction), material.frictionCombine.ToString(),
                        F(FloorMaterial.dynamicFriction), F(FloorMaterial.staticFriction), FloorMaterial.frictionCombine.ToString(),
                        F(dynamicFriction), F(staticFriction)));
                    yield return null;
                }
            }
            finally
            {
                if (owner != null)
                    owner.SetActive(false);
                WorldType.GetMethod("Dispose", Instance)!.Invoke(world, null);
                foreach (UnityEngine.Object resource in resources)
                {
                    if (resource != null)
                        UnityEngine.Object.Destroy(resource);
                }
            }
            while (SceneManager.sceneCount > InitialSceneCount)
                yield return null;
            FerrofluidFloorGripAssertions.Result result =
                FerrofluidFloorGripAssertions.Evaluate(name, held, direction, width, observations);
            report.checks.Add(new CheckOutcome
            {
                name = name,
                passed = result.passed,
                failure = string.Join(" | ", result.failures),
            });
            WriteSamplesCsv(name, samples);
            File.WriteAllLines(Path.Combine(OutputDir, $"{name}-materials.csv"), materialRows);
            File.WriteAllText(Path.Combine(OutputDir, $"{name}-grip.json"), JsonUtility.ToJson(result, true));
        }

        private static IEnumerator GrabFloorCase(float deltaTime, bool tip, int direction, RunReport report)
        {
            Time.fixedDeltaTime = deltaTime;
            object world = Activator.CreateInstance(WorldType, nonPublic: true);
            List<UnityEngine.Object> resources = new();
            List<FerrofluidTopPhysicsAssertions.Sample> samples = new();
            List<string> materials = new()
            {
                "time,held,dynamicFriction,staticFriction,frictionCombine,cornerActive",
            };
            string name = $"grab-{(tip ? "tip" : "slide")}-sign{direction}-dt{deltaTime:F5}";
            GameObject owner = null;
            float width = 0f;
            try
            {
                GameObject floorObject = new("grab floor validation floor");
                resources.Add(floorObject);
                floorObject.transform.position = new Vector3(0f, -0.5f, 0f);
                WorldType.GetMethod("MoveRootToScene", Instance)!.Invoke(world, new object[] { floorObject });
                BoxCollider floor = floorObject.AddComponent<BoxCollider>();
                floor.size = new Vector3(200f, 1f, 200f);
                floor.sharedMaterial = FloorMaterial;
                Component body = NewCube(world, Vector3.up, Quaternion.identity, floor);
                owner = body.gameObject;
                resources.Add(owner);
                Rigidbody rb = Body(body);
                Collider collider = rb.GetComponentInChildren<Collider>()!;
                Physics.SyncTransforms();
                width = collider.bounds.size.x;
                rb.position = new Vector3(0f, collider.bounds.extents.y + 0.002f, 0f);
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                Physics.SyncTransforms();
                WriteMetadata(rb, collider, body);
                GameObject attach = new("grab floor validation controller");
                resources.Add(attach);
                MethodInfo acquire = BodyType.GetMethod("Acquire", Instance)!;
                MethodInfo release = BodyType.GetMethod("Release", Instance)!;
                MethodInfo sampleGrab = BodyType.GetMethod("SampleGrabTarget", Instance)!;
                MethodInfo step = WorldType.GetMethod("Step", Instance)!;
                MethodInfo render = WorldType.GetMethod("Render", Instance)!;
                FieldInfo corner = BodyType.GetField("_cornerMaterialActive", Instance)!;
                BodyType.GetMethod("SetControllerInput", Instance)!.Invoke(
                    body,
                    new object[] { Vector2.zero, Vector2.zero, Quaternion.identity, Vector3.right });
                bool acquired = false;
                bool released = false;
                Vector3 startGrab = Vector3.zero;
                Vector3 endGrab = Vector3.zero;
                Vector3 tipEdge = Vector3.zero;
                samples.Add(CaptureOrigin(0f, rb, collider));
                int count = Mathf.RoundToInt(FerrofluidGrabFloorAssertions.EndTime / deltaTime);
                for (int i = 0; i < count; i++)
                {
                    float time = i * deltaTime;
                    if (!acquired && time >= FerrofluidGrabFloorAssertions.AcquireTime - 0.00001f)
                    {
                        float x = rb.position.x - (tip ? direction * 0.45f * width : 0f);
                        Ray ray = new(new Vector3(x, rb.position.y + (2f * width), rb.position.z), Vector3.down);
                        if (!collider.Raycast(ray, out RaycastHit hit, 4f * width))
                            throw new InvalidOperationException($"{name}: the controller ray missed the requested top surface.");
                        startGrab = hit.point;
                        attach.transform.position = startGrab;
                        if (tip)
                        {
                            // Lift the trailing corner around the opposite grounded edge.
                            // Only the controller moves, leaving body rotation to the joint and floor.
                            tipEdge = new Vector3(rb.position.x + (direction * 0.5f * width), 0f, rb.position.z);
                        }
                        else
                        {
                            endGrab = startGrab + (Vector3.right * (direction * 0.4f * width));
                        }

                        acquire.Invoke(body, new object[] { attach.transform, startGrab });
                        acquired = true;
                    }

                    if (acquired && !released)
                    {
                        float fraction = Mathf.Clamp01(
                            (time - FerrofluidGrabFloorAssertions.MoveStart)
                            / (FerrofluidGrabFloorAssertions.MoveEnd - FerrofluidGrabFloorAssertions.MoveStart));
                        attach.transform.position = tip
                            ? tipEdge + (Quaternion.AngleAxis(-direction * 45f * fraction, Vector3.forward) * (startGrab - tipEdge))
                            : Vector3.Lerp(startGrab, endGrab, fraction);
                        sampleGrab.Invoke(body, new object[] { deltaTime, time });
                        if (time >= FerrofluidGrabFloorAssertions.ReleaseTime - 0.00001f)
                        {
                            release.Invoke(body, null);
                            released = true;
                        }
                    }

                    step.Invoke(world, new object[] { deltaTime, deltaTime, time + deltaTime });
                    render.Invoke(world, new object[] { 1f });
                    Physics.SyncTransforms();
                    samples.Add(CaptureOrigin(time + deltaTime, rb, collider));
                    PhysicsMaterial material = collider.sharedMaterial;
                    materials.Add(string.Join(",",
                        F(time + deltaTime), acquired && !released ? "1" : "0",
                        F(material.dynamicFriction), F(material.staticFriction),
                        material.frictionCombine.ToString(), (bool)corner.GetValue(body)! ? "1" : "0"));
                    yield return null;
                }
            }
            finally
            {
                if (owner != null)
                    owner.SetActive(false);
                WorldType.GetMethod("Dispose", Instance)!.Invoke(world, null);
                foreach (UnityEngine.Object resource in resources)
                {
                    if (resource != null)
                        UnityEngine.Object.Destroy(resource);
                }
            }

            while (SceneManager.sceneCount > InitialSceneCount)
                yield return null;

            List<string> failures = FerrofluidGrabFloorAssertions.Evaluate(samples, tip, direction, width);
            report.checks.Add(new CheckOutcome
            {
                name = name,
                passed = failures.Count == 0,
                failure = string.Join(" | ", failures),
            });
            WriteSamplesCsv(name, samples);
            File.WriteAllLines(Path.Combine(OutputDir, $"{name}-materials.csv"), materials);
        }

        private static FerrofluidTopPhysicsAssertions.Sample CaptureOrigin(float time, Rigidbody rb, Collider collider)
        {
            FerrofluidTopPhysicsAssertions.Sample sample = Capture(time, rb, collider);
            sample.Position = rb.position;
            sample.Velocity = rb.GetPointVelocity(rb.position);
            return sample;
        }

        // Spin-up is produced by the real grab/input path: the body is acquired at the
        // visible corner opposite its eventual supporting corner, driven with the authored
        // spin stick, then released in place so the post-release drop exercises the same
        // grounded scoring as the injected-spin baseline cases.
        private static IEnumerator DropCase(float deltaTime, RunReport report)
        {
            Time.fixedDeltaTime = deltaTime;
            object world = Activator.CreateInstance(WorldType, nonPublic: true);
            List<UnityEngine.Object> caseResources = new();

            GameObject floorObject = new("top validation floor");
            caseResources.Add(floorObject);
            floorObject.layer = 3;
            floorObject.transform.position = new Vector3(0f, -0.5f, 0f);
            WorldType.GetMethod("MoveRootToScene", Instance)!.Invoke(world, new object[] { floorObject });
            BoxCollider floor = floorObject.AddComponent<BoxCollider>();
            floor.size = new Vector3(200f, 1f, 200f);
            floor.sharedMaterial = FloorMaterial;

            string name = $"controller-drop-dt{deltaTime:F5}";
            List<FerrofluidTopPhysicsAssertions.Sample> rampSamples = new();
            List<FerrofluidTopPhysicsAssertions.Sample> samples = new();
            Vector3 beforeMomentum = Vector3.zero;
            Vector3 afterMomentum = Vector3.zero;
            Vector3 beforeVelocity = Vector3.zero;
            Vector3 afterVelocity = Vector3.zero;
            bool preconditionMet = false;
            float rampElapsed = 0f;
            float actualSpinY = 0f;
            GameObject owner = null;
            try
            {
                Component body = NewCube(world, new Vector3(0f, 1f, 0f), InitialTilt(10f), floor);
                owner = body.gameObject;
                caseResources.Add(owner);
                Rigidbody rb = Body(body);
                Collider collider = rb.GetComponentInChildren<Collider>()!;
                WriteMetadata(rb, collider, body);
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;

                float cornerLocal = FerrofluidTopPhysicsAssertions.BodyScale * 0.5f
                    / Mathf.Pow(3f, 1f / (8f - 6f * FerrofluidTopPhysicsAssertions.Roundness));
                Vector3 grabPoint = rb.position + rb.rotation * (Vector3.one * cornerLocal);
                GameObject attach = new("top validation grab anchor");
                caseResources.Add(attach);
                attach.transform.position = grabPoint;
                Physics.SyncTransforms();

                Vector3 freeTensor = (Vector3)BodyType.GetField("_freeInertiaTensor", Instance)!.GetValue(body)!;
                Quaternion freeTensorRotation = (Quaternion)BodyType.GetField("_freeInertiaRotation", Instance)!.GetValue(body)!;
                Vector3 freeCenterOfMass = (Vector3)BodyType.GetField("_freeCenterOfMass", Instance)!.GetValue(body)!;

                BodyType.GetMethod("Acquire", Instance)!.Invoke(body, new object[] { attach.transform, attach.transform.position });
                BodyType.GetMethod("SetControllerInput", Instance)!.Invoke(
                    body,
                    new object[] { Vector2.zero, new Vector2(-1f, 0.03f), Quaternion.identity, Vector3.right });

                MethodInfo sampleGrab = BodyType.GetMethod("SampleGrabTarget", Instance)!;
                MethodInfo step = WorldType.GetMethod("Step", Instance)!;
                MethodInfo render = WorldType.GetMethod("Render", Instance)!;
                float targetSpin = 10f * 2f * Mathf.PI;
                rampSamples.Add(Capture(0f, rb, collider));
                while (rampElapsed < 8f && rb.angularVelocity.y < targetSpin)
                {
                    rampElapsed += deltaTime;
                    sampleGrab.Invoke(body, new object[] { deltaTime, rampElapsed });
                    step.Invoke(world, new object[] { deltaTime, deltaTime, rampElapsed });
                    render.Invoke(world, new object[] { 1f });
                    Physics.SyncTransforms();
                    rampSamples.Add(Capture(rampElapsed, rb, collider));
                    yield return null;
                }

                actualSpinY = rb.angularVelocity.y;
                preconditionMet = actualSpinY >= targetSpin;
                if (!preconditionMet)
                {
                    report.checks.Add(new CheckOutcome
                    {
                        name = $"controller-drop-precondition-dt{deltaTime:F5}",
                        failure = $"spin-up target {targetSpin:F3} rad/s not reached; actual angularVelocity.y={actualSpinY:F3} rad/s after {rampElapsed:F3}s of real input.",
                    });
                }
                else
                {
                    Vector3 cornerAxis = rb.rotation * Vector3.one.normalized;
                    DropReleaseObservation observation = new()
                    {
                        name = name,
                        releaseRotation = rb.rotation,
                        releaseAngularVelocity = rb.angularVelocity,
                        cornerAxisDirection = cornerAxis,
                        cornerAxisLeanDegrees = Vector3.Angle(Vector3.up, cornerAxis),
                        spinAxisDirection = rb.angularVelocity.sqrMagnitude > 0f ? rb.angularVelocity.normalized : Vector3.zero,
                        releaseHeight = (rb.position + rb.rotation * freeCenterOfMass).y,
                        comToGrabPoint = grabPoint - rb.position,
                        grabPointDistance = Vector3.Distance(grabPoint, rb.position),
                        rampElapsed = rampElapsed,
                    };

                    Quaternion frame = rb.rotation * freeTensorRotation;
                    beforeMomentum = frame * Vector3.Scale(freeTensor, Quaternion.Inverse(frame) * rb.angularVelocity);
                    observation.releaseAngularMomentum = beforeMomentum;
                    Vector3 freeCenter = rb.position + rb.rotation * freeCenterOfMass;
                    beforeVelocity = rb.GetPointVelocity(freeCenter);
                    BodyType.GetMethod("Release", Instance)!.Invoke(body, null);
                    Quaternion afterFrame = rb.rotation * freeTensorRotation;
                    afterMomentum = afterFrame * Vector3.Scale(freeTensor, Quaternion.Inverse(afterFrame) * rb.angularVelocity);
                    Vector3 afterCenter = rb.position + rb.rotation * freeCenterOfMass;
                    afterVelocity = rb.GetPointVelocity(afterCenter);
                    observation.beforeMomentum = beforeMomentum;
                    observation.afterMomentum = afterMomentum;
                    observation.beforeVelocity = beforeVelocity;
                    observation.afterVelocity = afterVelocity;
                    File.WriteAllText(
                        Path.Combine(OutputDir, $"controller-drop-release-dt{deltaTime:F5}.json"),
                        JsonUtility.ToJson(observation, prettyPrint: true));

                    samples.Add(Capture(0f, rb, collider));
                    int steps = Mathf.CeilToInt(FerrofluidTopPhysicsAssertions.Duration / deltaTime);
                    for (int i = 0; i < steps; i++)
                    {
                        rampElapsed += deltaTime;
                        step.Invoke(world, new object[] { deltaTime, deltaTime, rampElapsed });
                        render.Invoke(world, new object[] { 1f });
                        Physics.SyncTransforms();
                        samples.Add(Capture((i + 1) * deltaTime, rb, collider));
                        yield return null;
                    }
                }
            }
            finally
            {
                owner?.SetActive(false);
                WorldType.GetMethod("Dispose", Instance)!.Invoke(world, null);
                foreach (UnityEngine.Object item in caseResources)
                {
                    if (item != null)
                        UnityEngine.Object.Destroy(item);
                }
            }

            while (SceneManager.sceneCount > InitialSceneCount)
                yield return null;

            WriteSamplesCsv($"controller-drop-ramp-dt{deltaTime:F5}", rampSamples);
            if (!preconditionMet)
                yield break;

            report.grounded.Add(FerrofluidTopPhysicsAssertions.EvaluateGrounded(name, samples));
            WriteSamplesCsv(name, samples);

            CheckOutcome outcome = new()
            {
                name = $"controller-drop-release-dt{deltaTime:F5}",
                beforeMomentum = beforeMomentum,
                afterMomentum = afterMomentum,
                beforeVelocity = beforeVelocity,
                afterVelocity = afterVelocity,
            };
            try
            {
                FerrofluidTopPhysicsAssertions.AssertRelease(beforeMomentum, afterMomentum, beforeVelocity, afterVelocity);
                outcome.passed = true;
            }
            catch (Exception exception)
            {
                outcome.failure = exception.Message;
            }

            report.checks.Add(outcome);
        }

        [Serializable]
        private sealed class DropReleaseObservation
        {
            public string name = string.Empty;
            public Quaternion releaseRotation;
            public Vector3 releaseAngularVelocity;
            public Vector3 releaseAngularMomentum;
            public Vector3 cornerAxisDirection;
            public float cornerAxisLeanDegrees;
            public Vector3 spinAxisDirection;
            public float releaseHeight;
            public Vector3 comToGrabPoint;
            public float grabPointDistance;
            public float rampElapsed;
            public Vector3 beforeMomentum;
            public Vector3 afterMomentum;
            public Vector3 beforeVelocity;
            public Vector3 afterVelocity;
        }

        private static void WriteMetadata(Rigidbody rb, Collider collider, Component body)
        {
            if (MetadataWritten)
                return;
            MetadataWritten = true;
            PhysicsMaterial corner = (PhysicsMaterial)BodyType.GetField("_cornerMaterial", Instance)!.GetValue(body)!;
            PhysicsMaterial floorContact = (PhysicsMaterial)BodyType.GetField("_floorContactMaterial", Instance)!.GetValue(body)!;
            ColliderMetadata metadata = new()
            {
                colliderType = collider.GetType().Name,
                boundsCenter = collider.bounds.center,
                boundsSize = collider.bounds.size,
                mass = rb.mass,
                inertiaTensor = rb.inertiaTensor,
                inertiaTensorRotation = rb.inertiaTensorRotation,
                cornerDynamicFriction = corner.dynamicFriction,
                cornerStaticFriction = corner.staticFriction,
                floorContactDynamicFriction = floorContact.dynamicFriction,
                floorContactStaticFriction = floorContact.staticFriction,
                floorMaterialDynamicFriction = FloorMaterial.dynamicFriction,
                floorMaterialStaticFriction = FloorMaterial.staticFriction,
                floorMaterialBounciness = FloorMaterial.bounciness,
            };
            if (collider is BoxCollider box)
            {
                metadata.boxSize = box.size;
                metadata.boxCenter = box.center;
            }
            else if (collider is MeshCollider mesh && mesh.sharedMesh != null)
            {
                metadata.sourceMeshVertexCount = mesh.sharedMesh.vertexCount;
                metadata.sourceMeshTriangleCount = mesh.sharedMesh.triangles.Length / 3;
                metadata.convex = mesh.convex;
            }

            File.WriteAllText(Path.Combine(OutputDir, "metadata.json"), JsonUtility.ToJson(metadata, prettyPrint: true));
        }

        private static void WriteSamplesCsv(string name, List<FerrofluidTopPhysicsAssertions.Sample> samples)
        {
            List<string> rows = new()
            {
                "time,px,py,pz,qx,qy,qz,qw,vx,vy,vz,wx,wy,wz,lx,ly,lz,sx,sy,sz",
            };
            foreach (FerrofluidTopPhysicsAssertions.Sample sample in samples)
            {
                rows.Add(string.Join(",",
                    F(sample.Time), F(sample.Position.x), F(sample.Position.y), F(sample.Position.z),
                    F(sample.Rotation.x), F(sample.Rotation.y), F(sample.Rotation.z), F(sample.Rotation.w),
                    F(sample.Velocity.x), F(sample.Velocity.y), F(sample.Velocity.z),
                    F(sample.AngularVelocity.x), F(sample.AngularVelocity.y), F(sample.AngularVelocity.z),
                    F(sample.AngularMomentum.x), F(sample.AngularMomentum.y), F(sample.AngularMomentum.z),
                    F(sample.ColliderSupportPoint.x), F(sample.ColliderSupportPoint.y), F(sample.ColliderSupportPoint.z)));
            }

            File.WriteAllLines(Path.Combine(OutputDir, $"{name}.csv"), rows);
        }

        private static string F(float value)
            => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
