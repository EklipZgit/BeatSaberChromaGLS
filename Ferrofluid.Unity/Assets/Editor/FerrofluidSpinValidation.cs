namespace ChromaGLS.FerrofluidBundle
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using UnityEditor;
    using UnityEngine;

    // Exercises the session spin test through the production Grabbable on a real floor: top-face
    // acquire, timed lift/settle/spin/hover, actual BodyAngularVelocity, and cancel/stop/restart.
    internal static class FerrofluidSpinValidation
    {
        private const float FixedDelta = 0.02f;

        public static void Run()
        {
            try
            {
                Execute();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[FerrofluidSpinValidation] {exception.Message}");
                EditorApplication.Exit(1);
            }
        }

        private static void Execute()
        {
            List<string> failures = new();
            List<UnityEngine.Object> resources = new();
            SimulationMode previousMode = Physics.simulationMode;
            float previousDelta = Time.fixedDeltaTime;
            Physics.simulationMode = SimulationMode.Script;
            Time.fixedDeltaTime = FixedDelta;
            string repository = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            Assembly plugin = Assembly.LoadFrom(Path.Combine(repository, "ChromaGLS/bin/Release-1.44.1/net48/ChromaGLS.dll"));
            Type grabbable = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true);
            Type enclosure = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true);
            ConstructorInfo enclosureCtor = enclosure.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                null);
            object enclosureValue = enclosureCtor.Invoke(new object[] { Vector3.zero, 3f, new Vector3(0f, 0f, -2f), Vector3.forward, 0f });
            MethodInfo initialize = grabbable.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo acquire = grabbable.GetMethod("Acquire", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo release = grabbable.GetMethod("Release", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo startSpin = grabbable.GetMethod("StartSpinTest", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo stopSpin = grabbable.GetMethod("StopSpinTest", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            PropertyInfo isSpinTesting = grabbable.GetProperty("IsSpinTesting", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            PropertyInfo isHeld = grabbable.GetProperty("IsHeld", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo fixedUpdate = grabbable.GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            PhysicsMaterial material = new() { dynamicFriction = 0.6f, staticFriction = 0.6f, bounciness = 0.15f };
            object ownership = Activator.CreateInstance(plugin.GetType("ChromaGLS.Settings.FerrofluidGrabOwnership", true), nonPublic: true);
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(4f, 1f, 4f);
            floor.GetComponent<Collider>().material = material;
            resources.Add(floor);
            try
            {
                foreach (bool sphere in new[] { false, true })
                {
                    foreach (float rps in new[] { 2f, 8f })
                    {
                        string label = $"{(sphere ? "sphere" : "cube")} {rps}rps";
                        GameObject body = new("spin body");
                        body.transform.localScale = Vector3.one * 0.55f;
                        body.transform.position = new Vector3(0f, 0.275f, 0f);
                        Component comp = body.AddComponent(grabbable);
                        initialize.Invoke(comp, new object[] { enclosureValue, sphere, material, ownership });
                        Rigidbody rb = body.GetComponent<Rigidbody>();
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                        try
                        {
                            bool started = (bool)startSpin.Invoke(comp, new object[] { rps });
                            if (!started || !(bool)isSpinTesting.GetValue(comp))
                                failures.Add($"{label}: StartSpinTest must leave the spin test active");

                            for (int tick = 0; tick < 40; tick++)
                                Tick(comp, fixedUpdate);

                            if (rb.position.y < 0.55f || rb.position.y > 0.62f)
                                failures.Add($"{label}: lift must clear the floor to ~0.58m, got {rb.position.y:F3}");

                            Tick(comp, fixedUpdate);
                            float expectedOmega = Mathf.PI * 2f * rps;
                            if (Mathf.Abs(rb.angularVelocity.y - expectedOmega) > expectedOmega * 0.05f)
                                failures.Add($"{label}: spin start must set angularVelocity={expectedOmega:F2}, got {rb.angularVelocity.y:F2}");

                            for (int tick = 0; tick < 107; tick++)
                                Tick(comp, fixedUpdate);

                            if (rb.angularVelocity.magnitude > 0.1f)
                                failures.Add($"{label}: hover must zero angularVelocity, got {rb.angularVelocity.magnitude:F3}");

                            if (rb.position.y < 0.5f)
                                failures.Add($"{label}: hover must keep clearance, got y={rb.position.y:F3}");

                            if (!(bool)isSpinTesting.GetValue(comp))
                                failures.Add($"{label}: hover must keep the spin test active until stopped");

                            // Restart while active must cleanly re-run.
                            bool restarted = (bool)startSpin.Invoke(comp, new object[] { rps * 0.5f });
                            if (!restarted || !(bool)isSpinTesting.GetValue(comp))
                                failures.Add($"{label}: StartSpinTest while active must restart");
                            stopSpin.Invoke(comp, null);
                            if ((bool)isSpinTesting.GetValue(comp))
                                failures.Add($"{label}: StopSpinTest must clear the active state");

                            // A real grab on a held body must reject a new spin test, and a grab
                            // during an active spin test must cancel it.
                            GameObject attach = new("attach");
                            attach.transform.position = new Vector3(0.2f, 0.5f, 0f);
                            acquire.Invoke(comp, new object[] { attach.transform, attach.transform.position });
                            if ((bool)startSpin.Invoke(comp, new object[] { rps }))
                                failures.Add($"{label}: StartSpinTest must reject while held");
                            release.Invoke(comp, null);

                            bool spun = (bool)startSpin.Invoke(comp, new object[] { rps });
                            if (spun && (bool)isSpinTesting.GetValue(comp))
                            {
                                acquire.Invoke(comp, new object[] { attach.transform, attach.transform.position });
                                if ((bool)isSpinTesting.GetValue(comp))
                                    failures.Add($"{label}: a real grab during an active spin must cancel it");
                            }

                            release.Invoke(comp, null);
                            UnityEngine.Object.DestroyImmediate(attach);
                        }
                        catch (Exception exception)
                        {
                            failures.Add($"{label}: {exception.GetType().Name} {exception.Message.Split('\n')[0]}");
                        }
                        finally
                        {
                            body.SetActive(false);
                            resources.Add(body);
                        }
                    }
                }
            }
            finally
            {
                Physics.simulationMode = previousMode;
                Time.fixedDeltaTime = previousDelta;
                foreach (UnityEngine.Object resource in resources)
                {
                    if (resource != null)
                        UnityEngine.Object.DestroyImmediate(resource);
                }
            }

            if (failures.Count > 0)
                throw new Exception($"{failures.Count} spin cases failed: {string.Join(" | ", failures)}");
            Debug.Log("[FerrofluidSpinValidation] PASS: spin test phases, rates, and cancel paths verified.");
        }

        private static void Tick(Component comp, MethodInfo fixedUpdate)
        {
            fixedUpdate.Invoke(comp, null);
            Physics.Simulate(FixedDelta);
        }
    }
}
