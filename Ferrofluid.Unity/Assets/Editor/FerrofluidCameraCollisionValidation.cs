#nullable enable annotations

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidCameraCollisionValidation
    {
        private const float HeadRadius = 0.23375f;
        private const float BodyRadius = 0.275f;
        private const float CombinedRadius = HeadRadius + BodyRadius;
        private static readonly List<string> Failures = new();
        private static readonly List<Object> Resources = new();
        private static Context Ctx = null!;

        private sealed class Context
        {
            internal Type Grabbable = null!;
            internal Type CameraType = null!;
            internal MethodInfo Initialize = null!;
            internal MethodInfo Acquire = null!;
            internal MethodInfo FixedUpdate = null!;
            internal MethodInfo SampleGrabTarget = null!;
            internal MethodInfo ConfigureCubeRoundness = null!;
            internal MethodInfo CameraInitialize = null!;
            internal MethodInfo CameraAdvance = null!;
            internal MethodInfo CameraSnap = null!;
            internal PropertyInfo IsHeld = null!;
            internal object Enclosure = null!;
            internal object Ownership = null!;
            internal PhysicsMaterial FloorMaterial = null!;
            internal PhysicsMaterial BodyMaterial = null!;
            internal Collider FloorCollider = null!;
            internal GameObject Head = null!;
            internal GameObject? CameraParent;
            internal GameObject? CameraObject;
            internal Component? Camera;
        }

        public static void Run()
            => FerrofluidValidationRunner.Run(typeof(FerrofluidCameraCollisionValidation));

        private static IEnumerator RunRuntime()
        {
            SimulationMode previousMode = Physics.simulationMode;
            float previousDelta = Time.fixedDeltaTime;
            Physics.simulationMode = SimulationMode.Script;
            Time.fixedDeltaTime = 0.02f;
            Failures.Clear();
            Resources.Clear();
            Ctx = new Context();
            try
            {
                string repository = Directory.GetParent(Application.dataPath).Parent.FullName;
                Assembly plugin = Assembly.LoadFrom(Path.Combine(repository, "ChromaGLS/bin/Release-1.44.1/net48/ChromaGLS.dll"));
                Ctx.Grabbable = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true);
                Ctx.Initialize = Ctx.Grabbable.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.Acquire = Ctx.Grabbable.GetMethod("Acquire", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.FixedUpdate = Ctx.Grabbable.GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.SampleGrabTarget = Ctx.Grabbable.GetMethod("SampleGrabTarget", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.ConfigureCubeRoundness = Ctx.Grabbable.GetMethod("ConfigureCubeRoundness", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.IsHeld = Ctx.Grabbable.GetProperty("IsHeld", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                Ctx.CameraType = plugin.GetType("ChromaGLS.Settings.FerrofluidCameraCollision", true);
                Ctx.CameraInitialize = Ctx.CameraType.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.CameraAdvance = Ctx.CameraType.GetMethod("AdvancePhysics", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.CameraSnap = Ctx.CameraType.GetMethod("SnapToHead", BindingFlags.Instance | BindingFlags.NonPublic);
                AssertTrue(Ctx.CameraInitialize != null, "camera collision must expose Initialize(head, radius, material)");
                AssertTrue(Ctx.CameraAdvance != null, "camera collision must expose AdvancePhysics()");
                AssertTrue(Ctx.CameraSnap != null, "camera collision must expose SnapToHead()");

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

                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Resources.Add(floor);
                floor.name = "camera validation floor";
                floor.layer = 3;
                floor.transform.localScale = new Vector3(40f, 1f, 40f);
                floor.transform.position = new Vector3(0f, -0.5f, 0f);
                Ctx.FloorCollider = floor.GetComponent<Collider>();
                Ctx.FloorCollider.material = Ctx.FloorMaterial;

                Ctx.Head = new GameObject("tracked head");
                Resources.Add(Ctx.Head);
                Ctx.Head.transform.position = new Vector3(0f, 1.6f, 0f);
                CreateCamera();
                CheckProductionCameraRadius(plugin);

                IEnumerator impacts = CaseImpacts();
                while (impacts.MoveNext())
                    yield return impacts.Current;
                IEnumerator cube = CaseCubeImpact();
                while (cube.MoveNext())
                    yield return cube.Current;
                IEnumerator glancing = CaseGlancing();
                while (glancing.MoveNext())
                    yield return glancing.Current;
                IEnumerator held = CaseHeldBodies();
                while (held.MoveNext())
                    yield return held.Current;
                IEnumerator negative = CaseNegative();
                while (negative.MoveNext())
                    yield return negative.Current;
                IEnumerator moving = CaseMovingHead();
                while (moving.MoveNext())
                    yield return moving.Current;
                IEnumerator disabled = CaseDisabled();
                while (disabled.MoveNext())
                    yield return disabled.Current;
                IEnumerator lifecycle = CaseLifecycle();
                while (lifecycle.MoveNext())
                    yield return lifecycle.Current;

                foreach (string failure in Failures)
                    Debug.LogWarning($"[FerrofluidCameraCollisionValidation] FAIL: {failure}");

                if (Failures.Count > 0)
                    throw new Exception($"{Failures.Count} camera cases failed: {string.Join(" | ", Failures)}");

                Debug.Log("[FerrofluidCameraCollisionValidation] PASS: camera rebounds, tracking, held immunity, disable, lifecycle verified.");
            }
            finally
            {
                foreach (Object resource in Resources)
                {
                    if (resource != null)
                        Object.Destroy(resource);
                }

                Resources.Clear();
                Physics.simulationMode = previousMode;
                Time.fixedDeltaTime = previousDelta;
            }
        }

        private static void CreateCamera()
        {
            Ctx.CameraParent = new GameObject("camera collision parent");
            Resources.Add(Ctx.CameraParent);
            Ctx.CameraObject = new GameObject("camera collision") { layer = 3 };
            Ctx.CameraObject.transform.SetParent(Ctx.CameraParent.transform, true);
            Ctx.Camera = Ctx.CameraObject.AddComponent(Ctx.CameraType);
            Ctx.CameraInitialize!.Invoke(Ctx.Camera, new object[] { Ctx.Head.transform, HeadRadius, Ctx.BodyMaterial });
            ((MonoBehaviour)Ctx.Camera).enabled = false;
        }

        // The component is disabled so only this manual step advances the kinematic body.
        private static void Simulate()
        {
            Ctx.CameraAdvance!.Invoke(Ctx.Camera, null);
            Physics.Simulate(0.02f);
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
            if (!sphere && roundness.HasValue)
                Ctx.ConfigureCubeRoundness.Invoke(component, new object[] { roundness.Value });

            ((MonoBehaviour)component).enabled = false;
            return component;
        }

        private static IEnumerator CaseImpacts()
        {
            foreach ((string label, float speed) in new (string, float)[] { ("sphere 5", 5f), ("sphere 25", 25f) })
            {
                IEnumerator run = LaunchSphere(label, speed);
                while (run.MoveNext())
                    yield return run.Current;
            }
        }

        private static IEnumerator LaunchSphere(string label, float speed)
        {
            Vector3 head = Ctx.Head.transform.position;
            Component component = MakeBody($"camera {label}", true, head - new Vector3(0f, 0f, 1.2f), Quaternion.identity, null);
            Rigidbody rb = component.GetComponent<Rigidbody>();
            rb.linearVelocity = new Vector3(0f, 0f, speed);
            float minDistance = float.MaxValue;
            float minVz = float.MaxValue;
            for (int step = 0; step < 80; step++)
            {
                Ctx.SampleGrabTarget.Invoke(component, new object[] { 0.02f, Time.fixedUnscaledTime });
                Ctx.FixedUpdate.Invoke(component, null);
                Simulate();
                minDistance = Mathf.Min(minDistance, Vector3.Distance(rb.position, head));
                minVz = Mathf.Min(minVz, rb.linearVelocity.z);
                yield return null;
            }

            Debug.Log($"[FerrofluidCameraCollisionValidation] {label}: minDist={minDistance:F4} minVz={minVz:F2} outVz={rb.linearVelocity.z:F2}");
            AssertTrue(
                minDistance >= CombinedRadius - 0.02f,
                $"{label} entered the camera volume (minDist={minDistance:F4} < {CombinedRadius - 0.02f:F4})");
            AssertTrue(
                minVz < -0.5f,
                $"{label} failed to rebound off the camera (minVz={minVz:F2})");
            component.gameObject.SetActive(false);
        }

        private static IEnumerator CaseCubeImpact()
        {
            foreach ((string label, Quaternion rotation) in new (string, Quaternion)[]
            {
                ("cube face", Quaternion.identity),
                ("cube corner", Quaternion.Euler(30f, 45f, 20f)),
            })
            {
                IEnumerator run = LaunchCube(label, rotation);
                while (run.MoveNext())
                    yield return run.Current;
            }
        }

        private static IEnumerator LaunchCube(string label, Quaternion rotation)
        {
            Vector3 head = Ctx.Head.transform.position;
            Component component = MakeBody($"camera {label}", false, head - new Vector3(0f, 0f, 1.2f), rotation, 0.48f);
            Rigidbody rb = component.GetComponent<Rigidbody>();
            rb.linearVelocity = new Vector3(0f, 0f, 8f);
            Collider cubeCollider = component.GetComponentInChildren<BoxCollider>();
            Collider cameraCollider = Ctx.CameraObject!.GetComponent<Collider>();
            float maxPenetration = 0f;
            float minVz = float.MaxValue;
            for (int step = 0; step < 60; step++)
            {
                Ctx.SampleGrabTarget.Invoke(component, new object[] { 0.02f, Time.fixedUnscaledTime });
                Ctx.FixedUpdate.Invoke(component, null);
                Simulate();
                if (Physics.ComputePenetration(
                        cubeCollider,
                        cubeCollider.transform.position,
                        cubeCollider.transform.rotation,
                        cameraCollider,
                        cameraCollider.transform.position,
                        cameraCollider.transform.rotation,
                        out Vector3 _,
                        out float distance))
                {
                    maxPenetration = Mathf.Max(maxPenetration, distance);
                }

                minVz = Mathf.Min(minVz, rb.linearVelocity.z);
                yield return null;
            }

            Debug.Log($"[FerrofluidCameraCollisionValidation] {label}: maxPen={maxPenetration:F4} endZ={rb.position.z:F2} minVz={minVz:F2}");
            AssertTrue(
                maxPenetration <= 0.01f,
                $"{label} interpenetrated the camera collider ({maxPenetration:F4})");
            AssertTrue(
                minVz < 0f,
                $"{label} failed to rebound off the camera (minVz={minVz:F2})");
            component.gameObject.SetActive(false);
        }

        private static IEnumerator CaseGlancing()
        {
            Vector3 head = Ctx.Head.transform.position;
            Component component = MakeBody("camera glancing", true, head + new Vector3(0.48f, 0.05f, -1.5f), Quaternion.identity, null);
            Rigidbody rb = component.GetComponent<Rigidbody>();
            rb.linearVelocity = new Vector3(0f, 0f, 8f);
            float minDistance = float.MaxValue;
            for (int step = 0; step < 60; step++)
            {
                Ctx.SampleGrabTarget.Invoke(component, new object[] { 0.02f, Time.fixedUnscaledTime });
                Ctx.FixedUpdate.Invoke(component, null);
                Simulate();
                minDistance = Mathf.Min(minDistance, Vector3.Distance(rb.position, head));
                yield return null;
            }

            Debug.Log($"[FerrofluidCameraCollisionValidation] glancing: minDist={minDistance:F4} outV={rb.linearVelocity} head={Ctx.Head.transform.position}");
            AssertTrue(minDistance >= CombinedRadius - 0.03f, $"glancing contact entered the camera volume ({minDistance:F4})");
            AssertTrue(minDistance <= CombinedRadius + 0.08f, $"glancing aim missed the camera ({minDistance:F4})");
            AssertTrue(
                rb.linearVelocity.magnitude > 0.3f,
                $"glancing contact killed all tangential motion ({rb.linearVelocity})");
            AssertTrue(
                Mathf.Abs(rb.linearVelocity.x) + Mathf.Abs(rb.linearVelocity.y) > 0.1f,
                $"glancing contact failed to deflect ({rb.linearVelocity})");

            AssertTrue(
                Vector3.Distance(Ctx.Head.transform.position, head) < 0.0001f,
                "the camera collider must never move the tracked head");
            component.gameObject.SetActive(false);
        }

        private static IEnumerator CaseMovingHead()
        {
            Ctx.Head.transform.position = new Vector3(0f, 1.6f, -1.5f);
            Ctx.CameraSnap!.Invoke(Ctx.Camera, null);
            Component component = MakeBody("camera pushed", true, new Vector3(0f, 1.6f, -0.8f), Quaternion.identity, null);
            Rigidbody rb = component.GetComponent<Rigidbody>();
            Rigidbody cameraBody = Ctx.CameraObject!.GetComponent<Rigidbody>();
            float minDistance = float.MaxValue;
            float maxLag = 0f;
            for (int step = 0; step < 80; step++)
            {
                Ctx.Head.transform.position += new Vector3(0f, 0f, 0.04f);
                Ctx.SampleGrabTarget.Invoke(component, new object[] { 0.02f, Time.fixedUnscaledTime });
                Ctx.FixedUpdate.Invoke(component, null);
                Simulate();
                minDistance = Mathf.Min(minDistance, Vector3.Distance(rb.position, Ctx.Head.transform.position));
                maxLag = Mathf.Max(maxLag, Vector3.Distance(cameraBody.position, Ctx.Head.transform.position));
                yield return null;
            }

            Debug.Log($"[FerrofluidCameraCollisionValidation] moving head: body z={rb.position.z:F2} minDist={minDistance:F4} maxLag={maxLag:F4}");
            AssertTrue(minDistance >= CombinedRadius - 0.03f, $"moving camera tunneled through the sphere ({minDistance:F4})");
            AssertTrue(rb.position.z > -0.4f, $"moving camera failed to push the sphere (z={rb.position.z:F2})");
            AssertTrue(maxLag < 0.001f, $"the camera rigidbody lagged the tracked head by {maxLag:F4}");
            Ctx.Head.transform.position = new Vector3(0f, 1.6f, 0f);
            component.gameObject.SetActive(false);
        }

        private static IEnumerator CaseHeldBodies()
        {
            foreach (bool sphere in new[] { true, false })
            {
                IEnumerator run = DriveHeld(sphere);
                while (run.MoveNext())
                    yield return run.Current;
            }
        }

        private static IEnumerator DriveHeld(bool sphere)
        {
            string label = sphere ? "held sphere" : "held cube";
            Vector3 head = Ctx.Head.transform.position;
            GameObject anchor = new($"{label} anchor");
            Resources.Add(anchor);
            anchor.transform.position = head - new Vector3(0f, 0f, 1.2f);
            Component component = MakeBody($"camera {label}", sphere, anchor.transform.position, Quaternion.identity, sphere ? (float?)null : 0.48f);
            Rigidbody rb = component.GetComponent<Rigidbody>();
            Ctx.Acquire.Invoke(component, new object[] { anchor.transform, rb.position });
            Collider bodyCollider = sphere
                ? (Collider)component.GetComponentInChildren<SphereCollider>()
                : component.GetComponentInChildren<BoxCollider>();
            Collider cameraCollider = Ctx.CameraObject!.GetComponent<Collider>();
            float minDistance = float.MaxValue;
            float maxPenetration = 0f;
            for (int step = 0; step < 100; step++)
            {
                anchor.transform.position += new Vector3(0f, 0f, 0.02f);
                Ctx.SampleGrabTarget.Invoke(component, new object[] { 0.02f, Time.fixedUnscaledTime });
                Ctx.FixedUpdate.Invoke(component, null);
                Simulate();
                minDistance = Mathf.Min(minDistance, Vector3.Distance(rb.position, head));
                if (Physics.ComputePenetration(
                        bodyCollider,
                        bodyCollider.transform.position,
                        bodyCollider.transform.rotation,
                        cameraCollider,
                        cameraCollider.transform.position,
                        cameraCollider.transform.rotation,
                        out Vector3 _,
                        out float distance))
                {
                    maxPenetration = Mathf.Max(maxPenetration, distance);
                }

                yield return null;
            }

            Debug.Log($"[FerrofluidCameraCollisionValidation] {label}: minDist={minDistance:F4} maxPen={maxPenetration:F4} held={Ctx.IsHeld.GetValue(component)}");
            AssertTrue(
                maxPenetration <= 0.01f,
                $"{label} interpenetrated the camera collider ({maxPenetration:F4})");
            AssertTrue(
                minDistance >= (sphere ? CombinedRadius - 0.01f : 0.4f),
                $"{label} driven through the camera reached {minDistance:F4}");
            AssertTrue(
                (bool)Ctx.IsHeld.GetValue(component)!,
                $"{label} lost held ownership against the camera");
            component.gameObject.SetActive(false);
        }

        private static IEnumerator CaseNegative()
        {
            // The far-field control keeps its native motion; the camera must not reach it.
            Component flying = MakeBody("camera negative flying", true, new Vector3(5f, 1.6f, -6f), Quaternion.identity, null);
            Rigidbody flyingRb = flying.GetComponent<Rigidbody>();
            flyingRb.linearVelocity = new Vector3(0f, 0f, 5f);
            for (int step = 0; step < 20; step++)
            {
                Ctx.SampleGrabTarget.Invoke(flying, new object[] { 0.02f, Time.fixedUnscaledTime });
                Ctx.FixedUpdate.Invoke(flying, null);
                Simulate();
                yield return null;
            }

            AssertTrue(
                Mathf.Abs(flyingRb.linearVelocity.z - 5f) < 0.01f,
                $"a body clear of the camera lost forward speed ({flyingRb.linearVelocity.z:F2})");
            flying.gameObject.SetActive(false);
        }

        private static IEnumerator CaseDisabled()
        {
            Ctx.CameraObject!.SetActive(false);
            Vector3 head = Ctx.Head.transform.position;
            Component component = MakeBody("camera disabled", true, head - new Vector3(0f, 0f, 1.2f), Quaternion.identity, null);
            Rigidbody rb = component.GetComponent<Rigidbody>();
            rb.linearVelocity = new Vector3(0f, 0f, 5f);
            for (int step = 0; step < 60; step++)
            {
                Ctx.SampleGrabTarget.Invoke(component, new object[] { 0.02f, Time.fixedUnscaledTime });
                Ctx.FixedUpdate.Invoke(component, null);
                Simulate();
                yield return null;
            }

            AssertTrue(
                rb.position.z > head.z + 0.4f,
                $"a disabled camera still blocked the sphere (z={rb.position.z:F2})");
            component.gameObject.SetActive(false);
            Ctx.CameraObject.SetActive(true);
        }

        private static IEnumerator CaseLifecycle()
        {
            SphereCollider collider = Ctx.CameraObject!.GetComponent<SphereCollider>();
            AssertTrue(collider != null, "camera collision must carry a native sphere collider");
            if (collider != null)
            {
                float worldRadius = collider.radius * collider.transform.lossyScale.x;
                AssertTrue(
                    Mathf.Abs(worldRadius - HeadRadius) < 0.001f,
                    $"camera radius {worldRadius:F4} must match the shrunken {HeadRadius:F4}");
            }

            // Re-enable must snap the collider to wherever the head moved while it was off.
            Ctx.CameraObject.SetActive(false);
            Ctx.Head.transform.position += new Vector3(0.5f, 0f, 0.3f);
            Ctx.CameraSnap!.Invoke(Ctx.Camera, null);
            Ctx.CameraObject.SetActive(true);
            Rigidbody cameraBody = Ctx.CameraObject.GetComponent<Rigidbody>();
            Simulate();
            AssertTrue(
                Vector3.Distance(cameraBody.position, Ctx.Head.transform.position) < 0.001f,
                $"re-enabled camera did not snap to the moved head ({cameraBody.position} vs {Ctx.Head.transform.position})");
            Ctx.Head.transform.position = new Vector3(0f, 1.6f, 0f);

            // Destroying the owner destroys the collider child but not the shared material.
            Object.Destroy(Ctx.CameraParent);
            yield return null;
            AssertTrue(Ctx.CameraObject == null, "destroying the owner must clean the camera object");
            AssertTrue(Ctx.Camera == null, "destroying the owner must clean the camera component");
            AssertTrue(Ctx.BodyMaterial != null, "destroying the camera object must not destroy the shared enclosure material");
            Ctx.CameraObject = null;
            Ctx.CameraParent = null;
        }

        // The manual CreateCamera above injects the radius, so it cannot catch the production
        // factory passing the wrong value. Instantiate the real manager path instead.
        private static void CheckProductionCameraRadius(Assembly plugin)
        {
            GameObject owner = new("camera factory check");
            try
            {
                Type managerType = plugin.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects+FerrofluidEnvironmentManager", true)!;
                Component manager = owner.AddComponent(managerType);
                ((Behaviour)manager).enabled = false;
                BindingFlags inst = BindingFlags.Instance | BindingFlags.NonPublic;
                managerType.GetField("_headTransform", inst)!.SetValue(manager, Ctx.Head.transform);
                PhysicsMaterial ownedMaterial = new();
                managerType.GetField("_enclosureMaterial", inst)!.SetValue(manager, ownedMaterial);
                MethodInfo create = managerType.GetMethod("CreateCameraCollision", inst)!;
                create.Invoke(manager, null);
                Component cameraCollision = (Component)managerType.GetField("_cameraCollision", inst)!.GetValue(manager)!;
                SphereCollider collider = cameraCollision.GetComponent<SphereCollider>();
                float radius = collider.radius * collider.transform.lossyScale.x;
                AssertTrue(
                    Mathf.Abs(radius - HeadRadius) < 0.000001f,
                    $"production camera radius {radius:R} must equal the shrunken {HeadRadius:R}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                Failures.Add(message);
        }
    }
}
