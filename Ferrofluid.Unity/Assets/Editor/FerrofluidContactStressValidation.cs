using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // Sustained held-contact coverage: air, floor, curved wall, static blocker, and a synthetic
    // collider pile (not an in-game scene or headset measurement). The stress gate allows up to
    // 0.03m observed obstacle overlap under sustained press; the floor stays strict at 0.005m.
    internal static class FerrofluidContactStressValidation
    {
        private const float FixedDelta = 0.02f;
        private static readonly List<string> Failures = new();

        private sealed class Context
        {
            internal Type Grabbable = null!;
            internal MethodInfo Initialize = null!;
            internal MethodInfo Acquire = null!;
            internal MethodInfo Release = null!;
            internal MethodInfo AdvancePhysics = null!;
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
                string repository = Directory.GetParent(Application.dataPath)!.Parent!.FullName;
                Assembly plugin = Assembly.LoadFrom(Path.Combine(repository, "ChromaGLS/bin/Release-1.44.1/net48/ChromaGLS.dll"));
                context.Grabbable = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true);
                BindingFlags inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                context.Initialize = context.Grabbable.GetMethod("Initialize", inst)!;
                context.Acquire = context.Grabbable.GetMethod("Acquire", inst)!;
                context.Release = context.Grabbable.GetMethod("Release", inst)!;
                context.AdvancePhysics = context.Grabbable.GetMethod("AdvancePhysics", inst)!;
                context.SampleGrabTarget = context.Grabbable.GetMethod("SampleGrabTarget", inst)!;
                Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true);
                context.Enclosure = enclosureType
                    .GetConstructor(
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                        null,
                        new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                        null)!
                    .Invoke(new object[] { Vector3.zero, 3f, new Vector3(0f, 0f, -2f), Vector3.forward, 0f });
                context.Material = new PhysicsMaterial("contact stress")
                {
                    dynamicFriction = 0.4f,
                    staticFriction = 0.4f,
                    bounciness = 0.3f,
                    bounceCombine = PhysicsMaterialCombine.Multiply,
                    frictionCombine = PhysicsMaterialCombine.Average,
                };
                context.Resources.Add(context.Material);
                Type ownershipType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabOwnership", true);
                context.Ownership = Activator.CreateInstance(ownershipType, nonPublic: true)!;
                context.Floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                context.Floor.transform.position = Vector3.zero;
                context.Floor.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
                context.Floor.GetComponent<Collider>().material = context.Material;
                context.Resources.Add(context.Floor);

                foreach (bool sphere in new[] { false, true })
                {
                    RunPhase(context, "air", sphere, PhaseAir);
                    RunPhase(context, "floor", sphere, PhaseFloor);
                    RunPhase(context, "wall", sphere, PhaseWall);
                    RunPhase(context, "blocker", sphere, PhaseBlocker);
                    RunPhase(context, "pile", sphere, PhasePile);
                }
            }
            finally
            {
                Physics.simulationMode = previousMode;
                Time.fixedDeltaTime = previousDelta;
                foreach (Object resource in context.Resources)
                {
                    if (resource != null)
                        Object.DestroyImmediate(resource);
                }
            }

            if (Failures.Count > 0)
                throw new Exception($"{Failures.Count} contact stress cases failed: {string.Join(" | ", Failures)}");
            Debug.Log("[FerrofluidContactStressValidation] PASS: sustained held contacts stay bounded under the finite pivot drive.");
        }

        private sealed class PhaseMetrics
        {
            internal float MaxLinear;
            internal float MaxAngular;
            internal float MaxJump;
            internal float MaxObstacleDepth;
            internal float MaxFloorDepth;
            internal float MinCornerY = float.MaxValue;
            internal float MaxCornerRadial;
            internal float MinCornerRearZ = float.MaxValue;
            internal float MaxComX;
            internal float FarFaceX = float.MaxValue;
            internal Vector3 Previous;
        }

        private static void Step(Context context, Component comp, PhaseMetrics metrics, List<Collider> obstacles, float time)
        {
            context.SampleGrabTarget.Invoke(comp, new object[] { FixedDelta, time });
            context.AdvancePhysics.Invoke(comp, new object[] { FixedDelta, time });
            Physics.Simulate(FixedDelta);
            Rigidbody body = comp.GetComponent<Rigidbody>();
            Collider bodyCollider = body.GetComponentInChildren<Collider>();
            metrics.MaxLinear = Mathf.Max(metrics.MaxLinear, body.linearVelocity.magnitude);
            metrics.MaxAngular = Mathf.Max(metrics.MaxAngular, body.angularVelocity.magnitude);
            metrics.MaxJump = Mathf.Max(metrics.MaxJump, Vector3.Distance(metrics.Previous, body.position));
            metrics.Previous = body.position;
            metrics.MaxComX = Mathf.Max(metrics.MaxComX, body.position.x);

            // The cube's real rotated corners carry the containment and floor-contact evidence;
            // the sphere's lowest extent is its radius under the COM.
            if (bodyCollider is SphereCollider sphere)
            {
                float extent = sphere.radius * body.transform.lossyScale.x;
                metrics.MinCornerY = Mathf.Min(metrics.MinCornerY, body.position.y - extent);
                Vector3 radial = body.position;
                radial.y = 0f;
                metrics.MaxCornerRadial = Mathf.Max(metrics.MaxCornerRadial, radial.magnitude + extent);
                metrics.MinCornerRearZ = Mathf.Min(metrics.MinCornerRearZ, body.position.z - extent);
            }
            else
            {
                float half = ((BoxCollider)bodyCollider).size.x * 0.5f * body.transform.lossyScale.x;
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 corner = body.position + (body.rotation * new Vector3(x * half, y * half, z * half));
                            metrics.MinCornerY = Mathf.Min(metrics.MinCornerY, corner.y);
                            Vector3 radial = corner;
                            radial.y = 0f;
                            metrics.MaxCornerRadial = Mathf.Max(metrics.MaxCornerRadial, radial.magnitude);
                            metrics.MinCornerRearZ = Mathf.Min(metrics.MinCornerRearZ, corner.z);
                        }
                    }
                }
            }

            if (Physics.ComputePenetration(
                    bodyCollider,
                    body.position,
                    body.rotation,
                    context.Floor.GetComponent<Collider>(),
                    context.Floor.transform.position,
                    context.Floor.transform.rotation,
                    out _,
                    out float floorDepth))
                metrics.MaxFloorDepth = Mathf.Max(metrics.MaxFloorDepth, floorDepth);
            foreach (Collider obstacle in obstacles)
            {
                if (obstacle == null)
                    continue;
                if (Physics.ComputePenetration(
                        bodyCollider,
                        body.position,
                        body.rotation,
                        obstacle,
                        obstacle.transform.position,
                        obstacle.transform.rotation,
                        out _,
                        out float distance))
                    metrics.MaxObstacleDepth = Mathf.Max(metrics.MaxObstacleDepth, distance);
            }
        }

        private static void RunPhase(Context context, string label, bool sphere, Action<Context, Component, GameObject, PhaseMetrics, List<Collider>> phase)
        {
            string name = $"{label} {(sphere ? "sphere" : "cube")}";
            GameObject bodyObject = new($"{name} body");
            GameObject attach = new($"{name} attach");
            List<Collider> obstacles = new();
            context.Resources.Add(bodyObject);
            context.Resources.Add(attach);
            try
            {
                bodyObject.transform.position = new Vector3(0f, 0.275f, 0f);
                bodyObject.transform.localScale = Vector3.one * 0.55f;
                Component comp = bodyObject.AddComponent(context.Grabbable);
                context.Initialize.Invoke(comp, new object[] { context.Enclosure, sphere, context.Material, context.Ownership });
                PhaseMetrics metrics = new() { Previous = comp.GetComponent<Rigidbody>().position };
                phase(context, comp, attach, metrics, obstacles);
                Rigidbody body = comp.GetComponent<Rigidbody>();
                Vector3 releaseLinear = body.GetPointVelocity(body.position);
                Vector3 releaseAngular = body.angularVelocity;
                context.Release.Invoke(comp, null);
                bool releaseClean =
                    Vector3.Distance(releaseLinear, body.GetPointVelocity(body.position)) < 0.000001f
                    && Vector3.Distance(releaseAngular, body.angularVelocity) < 0.000001f;
                context.AdvancePhysics.Invoke(comp, new object[] { FixedDelta, Time.unscaledTime });
                Physics.Simulate(FixedDelta);
                float afterKick = Mathf.Abs(body.linearVelocity.magnitude - releaseLinear.magnitude);
                Debug.Log(
                    $"[FerrofluidContactStressValidation] {name} v={metrics.MaxLinear:F3} omega={metrics.MaxAngular:F3} jump={metrics.MaxJump:F4} obstacle={metrics.MaxObstacleDepth:F4} floor={metrics.MaxFloorDepth:F4} minY={metrics.MinCornerY:F4} radial={metrics.MaxCornerRadial:F3} rearZ={metrics.MinCornerRearZ:F3} comX={metrics.MaxComX:F3} clean={releaseClean} kick={afterKick:F4}");
                if (metrics.MaxLinear > 5f || metrics.MaxAngular > 15f || metrics.MaxJump > 0.15f
                    || metrics.MaxFloorDepth > 0.005f || metrics.MaxObstacleDepth > 0.03f
                    || metrics.MinCornerY < -0.005f || metrics.MaxCornerRadial > 3.005f
                    || metrics.MinCornerRearZ < -2.005f || metrics.MaxComX > metrics.FarFaceX
                    || !releaseClean || afterKick > 0.5f)
                {
                    Failures.Add(
                        $"{name}: held contact escaped bounds (v={metrics.MaxLinear:F3} omega={metrics.MaxAngular:F3} jump={metrics.MaxJump:F4} obstacle={metrics.MaxObstacleDepth:F4} floor={metrics.MaxFloorDepth:F4} minY={metrics.MinCornerY:F4} radial={metrics.MaxCornerRadial:F3} rearZ={metrics.MinCornerRearZ:F3} comX={metrics.MaxComX:F3} clean={releaseClean} kick={afterKick:F4})");
                }
            }
            catch (Exception exception)
            {
                Failures.Add($"{name}: {exception.Message}");
            }
            finally
            {
                bodyObject.SetActive(false);
                attach.SetActive(false);
                foreach (Collider obstacle in obstacles)
                {
                    if (obstacle != null)
                        obstacle.gameObject.SetActive(false);
                }
            }
        }

        private static void AcquireTop(Context context, Component comp, GameObject attach, Vector3 worldGrabPoint)
        {
            attach.transform.position = worldGrabPoint;
            context.Acquire.Invoke(comp, new object[] { attach.transform, worldGrabPoint });
        }

        private static void PhaseAir(Context context, Component comp, GameObject attach, PhaseMetrics metrics, List<Collider> obstacles)
        {
            Rigidbody body = comp.GetComponent<Rigidbody>();
            body.position = new Vector3(0f, 2f, 0f);
            body.PublishTransform();
            Physics.SyncTransforms();
            metrics.Previous = body.position;
            AcquireTop(context, comp, attach, body.position + new Vector3(0f, 0.275f, 0f));
            for (int step = 0; step < 250; step++)
                Step(context, comp, metrics, obstacles, Time.unscaledTime);
        }

        private static void PhaseFloor(Context context, Component comp, GameObject attach, PhaseMetrics metrics, List<Collider> obstacles)
        {
            Rigidbody body = comp.GetComponent<Rigidbody>();
            AcquireTop(context, comp, attach, body.position + new Vector3(0f, 0.275f, 0f));
            Vector3 start = attach.transform.position;
            for (int step = 0; step < 250; step++)
            {
                attach.transform.position = start + new Vector3(0f, -Mathf.Min(step / 50f, 1f) * 0.4f, 0f);
                Step(context, comp, metrics, obstacles, Time.unscaledTime);
            }
        }

        private static void PhaseWall(Context context, Component comp, GameObject attach, PhaseMetrics metrics, List<Collider> obstacles)
        {
            Rigidbody body = comp.GetComponent<Rigidbody>();
            body.position = new Vector3(0f, 0.275f, 2.65f);
            body.PublishTransform();
            Physics.SyncTransforms();
            metrics.Previous = body.position;
            AcquireTop(context, comp, attach, body.position + new Vector3(0f, 0.275f, 0f));
            Vector3 start = attach.transform.position;
            for (int step = 0; step < 250; step++)
            {
                attach.transform.position = start + new Vector3(0f, 0f, Mathf.Min(step / 75f, 1f) * 0.75f);
                Step(context, comp, metrics, obstacles, Time.unscaledTime);
            }
        }

        private static void PhaseBlocker(Context context, Component comp, GameObject attach, PhaseMetrics metrics, List<Collider> obstacles)
        {
            Rigidbody body = comp.GetComponent<Rigidbody>();
            GameObject blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.transform.position = new Vector3(0.6f, 0.5f, 0f);
            blocker.transform.localScale = new Vector3(0.2f, 1f, 3f);
            blocker.GetComponent<Collider>().material = context.Material;
            context.Resources.Add(blocker);
            obstacles.Add(blocker.GetComponent<Collider>());
            metrics.FarFaceX = 0.72f;
            AcquireTop(context, comp, attach, body.position + new Vector3(0f, 0.275f, 0f));
            Vector3 start = attach.transform.position;
            for (int step = 0; step < 250; step++)
            {
                attach.transform.position = start + new Vector3(Mathf.Min(step / 50f, 1f) * 0.5f, 0f, 0f);
                Step(context, comp, metrics, obstacles, Time.unscaledTime);
            }
        }

        private static void PhasePile(Context context, Component comp, GameObject attach, PhaseMetrics metrics, List<Collider> obstacles)
        {
            Rigidbody body = comp.GetComponent<Rigidbody>();
            GameObject pile = new("synthetic pile");
            context.Resources.Add(pile);
            for (int x = 0; x < 5; x++)
            {
                for (int y = 0; y < 5; y++)
                {
                    for (int z = 0; z < 5; z++)
                    {
                        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        cube.transform.SetParent(pile.transform, false);
                        cube.transform.position = new Vector3(0.6f + (x * 0.121f), 0.06f + (y * 0.121f), -0.3f + (z * 0.121f));
                        cube.transform.localScale = Vector3.one * 0.12f;
                        cube.GetComponent<Collider>().material = context.Material;
                        obstacles.Add(cube.GetComponent<Collider>());
                        context.Resources.Add(cube);
                    }
                }
            }

            metrics.FarFaceX = 1.16f;
            AcquireTop(context, comp, attach, body.position + new Vector3(0f, 0.275f, 0f));
            Vector3 start = attach.transform.position;
            for (int step = 0; step < 250; step++)
            {
                attach.transform.position = start + new Vector3(Mathf.Min(step / 50f, 1f) * 0.55f, 0f, 0f);
                Step(context, comp, metrics, obstacles, Time.unscaledTime);
            }
        }
    }
}
