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
    // Corner-top and sphere floor-twist decay validation: cubes must coast past 60s under the
    // corner patch material and sphere twist must finish under the low-speed brake.
    internal static class FerrofluidFloorSpinValidation
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
            internal MethodInfo ConfigureCubeRoundness = null!;
            internal MethodInfo ConfigureFloorContact = null!;
            internal FieldInfo GrabShape = null!;
            internal MethodInfo Extent = null!;
            internal object Enclosure = null!;
            internal object Ownership = null!;
            internal PhysicsMaterial FloorMaterial = null!;
            internal PhysicsMaterial BodyMaterial = null!;
            internal Collider FloorCollider = null!;
        }

        public static void Run()
            => FerrofluidValidationRunner.Run(typeof(FerrofluidFloorSpinValidation));

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
                string repository = Directory.GetParent(Application.dataPath).Parent.FullName;
                string pluginPath = Environment.GetEnvironmentVariable("FERROFLUID_VALIDATION_PLUGIN")
                    ?? Path.Combine(repository, "ChromaGLS/bin/Release-1.44.1/net48/ChromaGLS.dll");
                CsvDir = Environment.GetEnvironmentVariable("FERROFLUID_VALIDATION_OUTPUT")
                    ?? Path.Combine(Directory.GetParent(repository).FullName, "Scratch", "ferro-floor-spin-validation");
                Debug.Log($"[FerrofluidFloorSpinValidation] dll={pluginPath} csv={CsvDir}");
                Assembly plugin = Assembly.LoadFrom(pluginPath);
                Ctx.Grabbable = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true);
                Ctx.Initialize = Ctx.Grabbable.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.Advance = Ctx.Grabbable.GetMethod("AdvancePhysics", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.ConfigureCubeRoundness = Ctx.Grabbable.GetMethod("ConfigureCubeRoundness", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.ConfigureFloorContact = Ctx.Grabbable.GetMethod("ConfigureFloorContact", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.GrabShape = Ctx.Grabbable.GetField("_grabShape", BindingFlags.Instance | BindingFlags.NonPublic);
                Ctx.Extent = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabPhysics", true)
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
                floor.name = "floor spin floor";
                floor.layer = 3;
                floor.transform.localScale = new Vector3(40f, 1f, 40f);
                floor.transform.position = new Vector3(0f, -0.5f, 0f);
                Ctx.FloorCollider = floor.GetComponent<Collider>();
                Ctx.FloorCollider.material = Ctx.FloorMaterial;

                IEnumerator balanced = CaseCube("balanced", Quaternion.identity, Vector3.zero);
                while (balanced.MoveNext())
                    yield return balanced.Current;
                IEnumerator drift = CaseCube("drift", Quaternion.identity, new Vector3(0.08f, 0f, 0f));
                while (drift.MoveNext())
                    yield return drift.Current;
                IEnumerator wobble = CaseCube("wobble", Quaternion.AngleAxis(5f, Vector3.right), Vector3.zero);
                while (wobble.MoveNext())
                    yield return wobble.Current;
                IEnumerator sphereLow = CaseSphereLowSpeed();
                while (sphereLow.MoveNext())
                    yield return sphereLow.Current;
                IEnumerator sphereHigh = CaseSphereCoast();
                while (sphereHigh.MoveNext())
                    yield return sphereHigh.Current;

                foreach (string failure in Failures)
                    Debug.LogWarning($"[FerrofluidFloorSpinValidation] FAIL: {failure}");

                if (Failures.Count > 0)
                    throw new Exception($"{Failures.Count} floor spin cases failed: {string.Join(" | ", Failures)}");

                Debug.Log("[FerrofluidFloorSpinValidation] PASS: cube coast and sphere finish brake verified.");
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

        private static IEnumerator CaseCube(string label, Quaternion extraRotation, Vector3 extraVelocity)
        {
            Quaternion cornerUp = Quaternion.FromToRotation(Vector3.one.normalized, Vector3.up);
            Quaternion rotation = extraRotation * cornerUp;
            Component component = MakeBody($"spin {label}", false, Vector3.zero, rotation, 0.48f);
            object shape = Ctx.GrabShape.GetValue(component);
            float support = Support(shape, rotation);
            GameObject body = component.gameObject;
            body.transform.position = new Vector3(0f, support + 0.001f, 0f);
            Rigidbody rb = body.GetComponent<Rigidbody>();
            rb.position = body.transform.position;
            rb.angularVelocity = new Vector3(0f, 20f * 2f * Mathf.PI, 0f);
            rb.linearVelocity = extraVelocity;

            List<string> rows = new(4500)
            {
                "case,step,time,wy,wmag,vx,vy,vz,gap,minAxis,axialRatio",
            };
            float startWy = rb.angularVelocity.y;
            float wy60 = float.NaN;
            float belowRps = -1f;
            float maxGap = 0f;
            for (int step = 0; step <= 4500; step++)
            {
                float time = step * Dt;
                if (step == 3000)
                    wy60 = rb.angularVelocity.y;
                if (step > 3000 && Mathf.Abs(rb.angularVelocity.y) < 2f * Mathf.PI)
                {
                    belowRps = time;
                    break;
                }

                Vector3 angular = rb.angularVelocity;
                Vector3 velocity = rb.linearVelocity;
                Vector3 localDown = Quaternion.Inverse(rb.rotation) * Vector3.down;
                float gap = rb.position.y - Support(shape, rb.rotation);
                maxGap = Mathf.Max(maxGap, gap);
                rows.Add(string.Join(",", new[]
                {
                    label,
                    step.ToString(CultureInfo.InvariantCulture),
                    time.ToString("F2", CultureInfo.InvariantCulture),
                    angular.y.ToString("F5", CultureInfo.InvariantCulture),
                    angular.magnitude.ToString("F5", CultureInfo.InvariantCulture),
                    velocity.x.ToString("F5", CultureInfo.InvariantCulture),
                    velocity.y.ToString("F5", CultureInfo.InvariantCulture),
                    velocity.z.ToString("F5", CultureInfo.InvariantCulture),
                    gap.ToString("F5", CultureInfo.InvariantCulture),
                    Mathf.Min(Mathf.Abs(localDown.x), Mathf.Min(Mathf.Abs(localDown.y), Mathf.Abs(localDown.z))).ToString("F5", CultureInfo.InvariantCulture),
                    (angular.magnitude > 0f ? Mathf.Abs(angular.y) / angular.magnitude : 0f).ToString("F5", CultureInfo.InvariantCulture),
                }));
                Ctx.Advance.Invoke(component, new object[] { Dt, time });
                Physics.Simulate(Dt);
                yield return null;
            }

            Directory.CreateDirectory(CsvDir);
            string path = Path.Combine(CsvDir, $"impl-{label}.csv");
            File.WriteAllLines(path, rows);
            Debug.Log($"[FerrofluidFloorSpinValidation] {label}: wy60={wy60:F2} loss60={(startWy - wy60):F2} below1RPS={belowRps:F1}s maxGap={maxGap:F4} rows={rows.Count - 1}");
            AssertTrue(
                Mathf.Abs(wy60) > 2f * Mathf.PI,
                $"{label} must still spin past 60s (wy60={wy60:F2}, baseline died at 48.7/42.5/56.2)");
            AssertTrue(
                startWy - wy60 > 10f,
                $"{label} coasted suspiciously untouched (loss60={startWy - wy60:F2})");
            AssertTrue(
                maxGap < 0.02f,
                $"{label} hovered off the rendered shell (maxGap={maxGap:F4})");
            body.SetActive(false);
        }

        private static IEnumerator CaseSphereLowSpeed()
        {
            foreach (float sign in new[] { 1f, -1f })
            {
                Component component = MakeBody(
                    $"spin sphere {(sign > 0f ? "pos" : "neg")}",
                    true,
                    new Vector3(0f, 0.276f, 0f),
                    Quaternion.identity,
                    null);
                Rigidbody rb = component.GetComponent<Rigidbody>();
                rb.angularVelocity = new Vector3(0f, sign * 2f * Mathf.PI, 0f);
                List<string> rows = new(700) { "case,step,time,wy" };
                float finish = -1f;
                for (int step = 0; step <= 600; step++)
                {
                    float time = step * Dt;
                    rows.Add(string.Join(",", new[]
                    {
                        $"sphere{(sign > 0f ? "pos" : "neg")}",
                        step.ToString(CultureInfo.InvariantCulture),
                        time.ToString("F2", CultureInfo.InvariantCulture),
                        rb.angularVelocity.y.ToString("F5", CultureInfo.InvariantCulture),
                    }));
                    if (Mathf.Abs(rb.angularVelocity.y) < 0.01f)
                    {
                        finish = time;
                        break;
                    }

                    Ctx.Advance.Invoke(component, new object[] { Dt, time });
                    Physics.Simulate(Dt);
                    yield return null;
                }

                Directory.CreateDirectory(CsvDir);
                File.WriteAllLines(Path.Combine(CsvDir, $"impl-sphere{(sign > 0f ? "pos" : "neg")}.csv"), rows);
                Debug.Log($"[FerrofluidFloorSpinValidation] sphere{(sign > 0f ? "+" : "-")}1RPS: finish={finish:F2}s wy={rb.angularVelocity.y:F4}");
                AssertTrue(finish > 0f && finish <= 12f, $"sphere {(sign > 0f ? "+" : "-")}1RPS must reach rest in <=12s (finish={finish:F2})");
                component.gameObject.SetActive(false);
            }
        }

        private static IEnumerator CaseSphereCoast()
        {
            foreach (float rps in new[] { 2f, 20f })
            {
                float omega = rps * 2f * Mathf.PI;
                Component component = MakeBody(
                    $"spin sphere {rps}",
                    true,
                    new Vector3(0f, 0.276f, 0f),
                    Quaternion.identity,
                    null);
                Rigidbody rb = component.GetComponent<Rigidbody>();
                rb.angularVelocity = new Vector3(0f, omega, 0f);
                for (int step = 0; step < 50; step++)
                {
                    Ctx.Advance.Invoke(component, new object[] { Dt, step * Dt });
                    Physics.Simulate(Dt);
                    yield return null;
                }

                float expected = omega * Mathf.Exp(-0.1875f * 1f);
                Debug.Log($"[FerrofluidFloorSpinValidation] sphere {rps}RPS after 1s: wy={rb.angularVelocity.y:F3} expected={expected:F3}");
                AssertTrue(
                    Mathf.Abs(rb.angularVelocity.y - expected) < expected * 0.05f,
                    $"sphere {rps} RPS must keep the 0.1875 coast rate (wy={rb.angularVelocity.y:F3} expected={expected:F3})");
                component.gameObject.SetActive(false);
            }
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                Failures.Add(message);
        }
    }
}
