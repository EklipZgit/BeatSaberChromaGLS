using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidRenderCadenceValidation
    {
        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidRenderCadenceValidation));

        private static IEnumerator RunRuntime()
        {
            string directory = FerrofluidValidationArtifacts.CreateRunDirectory("render-cadence");
            Debug.Log($"[FerrofluidRenderCadenceValidation] captureDirectory={directory}");
            float oldCapture = Time.captureDeltaTime;
            float oldFixed = Time.fixedDeltaTime;
            SimulationMode oldMode = Physics.simulationMode;
            int oldVsync = QualitySettings.vSyncCount;
            List<string> failures = new();
            // Resolve the same compiled plugin and dependency assemblies as the other native checks.
            Assembly plugin = FerrofluidValidationRuntime.CreateConfig().GetType().Assembly;
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            Type type = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true)!;
            Type enclosure = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true)!;
            object bounds = enclosure.GetConstructor(
                flags,
                null,
                new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                null)!.Invoke(new object[] { Vector3.zero, 100f, new Vector3(0f, 0f, -100f), Vector3.forward, -100f });
            try
            {
                Time.captureDeltaTime = 1f / 170f;
                QualitySettings.vSyncCount = 0;
                Physics.simulationMode = SimulationMode.FixedUpdate;
                foreach (bool sphere in new[] { false, true })
                {
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-ferroContactsOnly") >= 0)
                        break;

                    foreach (int physicsHz in new[] { 50, 90, 120 })
                    {
                        Time.fixedDeltaTime = 1f / physicsHz;
                        GameObject bodyObject = new("Render cadence body", typeof(MeshRenderer));
                        GameObject attach = new("Render cadence cursor");
                        try
                        {
                            bodyObject.transform.SetPositionAndRotation(
                                new Vector3(0f, 3f, 0f),
                                Quaternion.Euler(0f, 0f, 30f));
                            bodyObject.transform.localScale = Vector3.one * 0.7f;
                            Component component = bodyObject.AddComponent(type);
                            type.GetMethod("Initialize", flags)!.Invoke(component, new[] { bounds, sphere, null, null });
                            Rigidbody body = bodyObject.GetComponent<Rigidbody>();
                            MeshRenderer renderer = bodyObject.GetComponent<MeshRenderer>();
                            Vector3 localAnchor = new(0.5f, 0.5f, 0.2f);
                            attach.transform.position = bodyObject.transform.TransformPoint(localAnchor);
                            Vector3 start = attach.transform.position;
                            type.GetMethod("Acquire", flags)!.Invoke(component, new object[] { attach.transform, start });
                            body.angularVelocity = new Vector3(0f, 0f, 2f);
                            MethodInfo sample = type.GetMethod("SampleGrabTarget", flags)!;
                            string name = $"{(sphere ? "Sphere" : "Cube")}-170render-{physicsHz}physics";
                            using StreamWriter writer = new(Path.Combine(directory, name + ".csv"));
                            writer.WriteLine("frame,renderTime,physicsTime,px,py,pz,qx,qy,qz,qw,omega,noPhysicsStep");
                            Vector3 previousPosition = bodyObject.transform.position;
                            Quaternion previousRotation = bodyObject.transform.rotation;
                            float previousFixed = Time.fixedUnscaledTime;
                            int eligible = 0;
                            int frozenPosition = 0;
                            int frozenRotation = 0;
                            float maximumPivotError = 0f;
                            for (int frame = 0; frame < 360; frame++)
                            {
                                yield return null;
                                float motion = Mathf.Max(0, frame - 170) / 170f;
                                attach.transform.position = start + (Vector3.right * motion * 0.25f);
                                sample.Invoke(component, new object[] { Time.unscaledDeltaTime, Time.unscaledTime });
                                Matrix4x4 displayed = renderer.localToWorldMatrix;
                                Vector3 position = displayed.GetColumn(3);
                                Quaternion rotation = displayed.rotation;
                                bool noPhysicsStep = Time.fixedUnscaledTime == previousFixed;
                                if (frame > 30 && noPhysicsStep && body.angularVelocity.magnitude > 0.5f)
                                {
                                    eligible++;
                                    if (Vector3.Distance(position, previousPosition) < 0.000001f)
                                        frozenPosition++;
                                    if (Quaternion.Angle(rotation, previousRotation) < 0.001f)
                                        frozenRotation++;
                                }

                                // A fixed cursor must remain the rendered swing pivot, not orbit it.
                                if (frame > 30 && frame < 160)
                                    maximumPivotError = Mathf.Max(maximumPivotError, Vector3.Distance(displayed.MultiplyPoint3x4(localAnchor), start));

                                writer.WriteLine(string.Format(
                                    CultureInfo.InvariantCulture,
                                    "{0},{1:R},{2:R},{3:R},{4:R},{5:R},{6:R},{7:R},{8:R},{9:R},{10:R},{11}",
                                    frame,
                                    Time.unscaledTime,
                                    Time.fixedUnscaledTime,
                                    position.x,
                                    position.y,
                                    position.z,
                                    rotation.x,
                                    rotation.y,
                                    rotation.z,
                                    rotation.w,
                                    body.angularVelocity.magnitude,
                                    noPhysicsStep));
                                previousPosition = position;
                                previousRotation = rotation;
                                previousFixed = Time.fixedUnscaledTime;
                            }

                            Debug.Log($"[FerrofluidRenderCadenceValidation] {name} eligible={eligible} frozenPosition={frozenPosition} frozenRotation={frozenRotation} pivotError={maximumPivotError:F7} mode={body.interpolation}");
                            if (eligible < 20)
                                failures.Add($"{name}: fixture did not sample enough moving render frames between physics steps");
                            if (frozenPosition > 1 || frozenRotation > 1)
                                failures.Add($"{name}: displayed pose freezes between physics updates ({frozenPosition}/{frozenRotation})");
                            if (maximumPivotError > 0.015f)
                                failures.Add($"{name}: displayed swing no longer pivots around the grabbed point");

                            type.GetMethod("Release", flags)!.Invoke(component, null);
                        }
                        finally
                        {
                            Object.Destroy(bodyObject);
                            Object.Destroy(attach);
                        }

                        yield return null;
                    }
                }

                IEnumerator contacts = ValidateContacts(plugin, failures, directory);
                using (contacts as IDisposable)
                {
                    while (contacts.MoveNext())
                        yield return contacts.Current;
                }

                if (failures.Count != 0)
                    throw new Exception(string.Join(" | ", failures));

                Debug.Log("[FerrofluidRenderCadenceValidation] PASS: held translation and swing update on 170 Hz render frames between physics ticks.");
            }
            finally
            {
                Time.captureDeltaTime = oldCapture;
                Time.fixedDeltaTime = oldFixed;
                Physics.simulationMode = oldMode;
                QualitySettings.vSyncCount = oldVsync;
            }
        }

        private static IEnumerator ValidateContacts(Assembly plugin, List<string> failures, string directory)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            Type type = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true)!;
            Type enclosure = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true)!;
            Type physics = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabPhysics", true)!;
            int layer = (int)physics.GetField("EnclosureLayer", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
            object bounds = enclosure.GetConstructor(
                flags,
                null,
                new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                null)!.Invoke(new object[] { Vector3.zero, 3f, new Vector3(0f, 0f, -2f), Vector3.forward, 0f });
            PhysicsMaterial material = new("Ferrofluid contact render validation")
            {
                dynamicFriction = 0.6f,
                staticFriction = 0.6f,
                bounciness = (float)physics.GetField("FloorRestitution", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!,
            };
            GameObject floor = CreateBlock(new Vector3(0f, -0.1f, 0f), new Vector3(8f, 0.2f, 8f), layer, material);
            GameObject wall = CreateBlock(new Vector3(0.95f, 1.2f, 0f), new Vector3(0.2f, 2.4f, 3f), layer, material);
            try
            {
                foreach (bool sphere in new[] { false, true })
                {
                    foreach (int physicsHz in new[] { 50, 90 })
                    {
                        Time.fixedDeltaTime = 1f / physicsHz;
                        GameObject bodyObject = new("Contact render body", typeof(MeshRenderer));
                        GameObject attach = new("Contact render cursor");
                        try
                        {
                            bodyObject.transform.position = new Vector3(0f, 0.35f, 0f);
                            bodyObject.transform.localScale = Vector3.one * 0.7f;
                            Component component = bodyObject.AddComponent(type);
                            type.GetMethod("Initialize", flags)!.Invoke(component, new object[] { bounds, sphere, material, null! });
                            Rigidbody body = bodyObject.GetComponent<Rigidbody>();
                            FerrofluidValidationContacts observations = bodyObject.AddComponent<FerrofluidValidationContacts>();
                            observations.Floor = floor.GetComponent<Collider>();
                            observations.Wall = wall.GetComponent<Collider>();
                            MeshRenderer renderer = bodyObject.GetComponent<MeshRenderer>();
                            MethodInfo acquire = type.GetMethod("Acquire", flags)!;
                            MethodInfo release = type.GetMethod("Release", flags)!;
                            MethodInfo sample = type.GetMethod("SampleGrabTarget", flags)!;
                            string label = $"{(sphere ? "Sphere" : "Cube")}-contacts-{physicsHz}physics";
                            using StreamWriter writer = new(Path.Combine(directory, label + ".csv"));
                            writer.WriteLine("frame,held,displayX,displayY,displayZ,displayQx,displayQy,displayQz,displayQw,physicalX,physicalY,physicalZ,physicalQx,physicalQy,physicalQz,physicalQw,displayStep,displayAngle,poseError,angleError,contacts");
                            Vector3 previousPosition = bodyObject.transform.position;
                            Quaternion previousRotation = bodyObject.transform.rotation;
                            Vector3 start = Vector3.zero;
                            int floorContacts = 0;
                            int wallContacts = 0;
                            int warps = 0;
                            float maximumStep = 0f;
                            float maximumAngle = 0f;
                            float maximumPoseError = 0f;
                            for (int frame = 0; frame < 720; frame++)
                            {
                                yield return null;
                                bool held = frame >= 25 && frame < 650;
                                if (frame == 25)
                                {
                                    Vector3 localGrab = sphere
                                        ? new Vector3(0.3f, 0.4f, 0f)
                                        : new Vector3(0.5f, 0.5f, 0.5f);
                                    start = bodyObject.transform.TransformPoint(localGrab);
                                    attach.transform.position = start;
                                    acquire.Invoke(component, new object[] { attach.transform, start });
                                }

                                if (held)
                                {
                                    float time = (frame - 25) / 170f;
                                    float liftPhase = Mathf.Clamp01((time - 0.35f) / 0.9f);
                                    float lift = 0.32f * (1f - Mathf.Cos(liftPhase * Mathf.PI));
                                    // Peak pointer speed stays below 2 m/s. The early drag presses the corner into the floor.
                                    Vector3 offset = new(
                                        0.65f * Mathf.Sin(time * 2f),
                                        lift - (0.08f * Mathf.Sin(time * 3f)),
                                        0.18f * Mathf.Sin(time * 2.6f));
                                    attach.transform.position = start + offset;
                                    sample.Invoke(component, new object[] { Time.unscaledDeltaTime, Time.unscaledTime });
                                }
                                else if (frame == 650)
                                {
                                    release.Invoke(component, null);
                                }

                                Matrix4x4 display = renderer.localToWorldMatrix;
                                Vector3 position = display.GetColumn(3);
                                Quaternion rotation = display.rotation;
                                float step = Vector3.Distance(position, previousPosition);
                                float angle = Quaternion.Angle(rotation, previousRotation);
                                float poseError = Vector3.Distance(position, body.position);
                                float angleError = Quaternion.Angle(rotation, body.rotation);
                                Vector3 originVelocity = body.GetPointVelocity(body.position);
                                float positionEnvelope = 0.025f + (originVelocity.magnitude * Time.fixedDeltaTime * 1.5f);
                                float rotationEnvelope = 3f + (body.angularVelocity.magnitude * Time.fixedDeltaTime * Mathf.Rad2Deg * 1.5f);
                                if (frame >= 25)
                                {
                                    maximumStep = Mathf.Max(maximumStep, step);
                                    maximumAngle = Mathf.Max(maximumAngle, angle);
                                    maximumPoseError = Mathf.Max(maximumPoseError, poseError);
                                    if (step > 0.12f || angle > 25f || poseError > positionEnvelope || angleError > rotationEnvelope)
                                        warps++;
                                }

                                floorContacts = observations.FloorContacts;
                                wallContacts = observations.WallContacts;
                                int count = floorContacts + wallContacts;

                                writer.WriteLine(string.Format(
                                    CultureInfo.InvariantCulture,
                                    "{0},{1},{2:R},{3:R},{4:R},{5:R},{6:R},{7:R},{8:R},{9:R},{10:R},{11:R},{12:R},{13:R},{14:R},{15:R},{16:R},{17:R},{18:R},{19:R},{20}",
                                    frame,
                                    held,
                                    position.x,
                                    position.y,
                                    position.z,
                                    rotation.x,
                                    rotation.y,
                                    rotation.z,
                                    rotation.w,
                                    body.position.x,
                                    body.position.y,
                                    body.position.z,
                                    body.rotation.x,
                                    body.rotation.y,
                                    body.rotation.z,
                                    body.rotation.w,
                                    step,
                                    angle,
                                    poseError,
                                    angleError,
                                    count));
                                previousPosition = position;
                                previousRotation = rotation;
                            }

                            Debug.Log($"[FerrofluidRenderCadenceValidation] {label} warps={warps} maxStep={maximumStep:F6} maxAngle={maximumAngle:F4} poseError={maximumPoseError:F6} floorContacts={floorContacts} wallContacts={wallContacts}");
                            if (warps != 0)
                                failures.Add($"{label}: ordinary contact dragging produces single-frame pose jumps ({warps})");
                            if (floorContacts < 5 || wallContacts < 5)
                                failures.Add($"{label}: fixture did not exercise both floor and wall contacts");
                        }
                        finally
                        {
                            Object.Destroy(bodyObject);
                            Object.Destroy(attach);
                        }

                        yield return null;
                    }
                }
            }
            finally
            {
                Object.Destroy(floor);
                Object.Destroy(wall);
                Object.Destroy(material);
            }
        }

        private static GameObject CreateBlock(Vector3 position, Vector3 size, int layer, PhysicsMaterial material)
        {
            GameObject block = new("Render validation contact", typeof(BoxCollider));
            block.layer = layer;
            block.transform.position = position;
            BoxCollider collider = block.GetComponent<BoxCollider>();
            collider.size = size;
            collider.sharedMaterial = material;
            return block;
        }
    }
}
