using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    // Surface-momentum inertia: per-frame rotation plus linear/angular velocity deltas must carry
    // tangential spike-base momentum through the integration passes. Every case runs even when a
    // sibling fails so the unchanged shader reports all expected failures at once.
    internal static class FerrofluidMomentumValidation
    {
        private const float DeltaTimeSeconds = 1f / 120f;
        private static readonly List<string> Failures = new();
        private static readonly List<FerrofluidValidationRuntime> RuntimeFixtures = new();

        public static void Run()
        {
            Failures.Clear();
            List<UnityEngine.Object> resources = new();
            try
            {
                Shader simShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSimulation.shader");
                if (ShaderUtil.ShaderHasError(simShader))
                {
                    throw new Exception("The simulation shader failed to compile.");
                }

                foreach (int shape in new[] { 1, 2 })
                {
                    string label = shape == 1 ? "cube" : "sphere";
                    RunCase($"{label} linear impulse direction", () => CaseLinearImpulse(simShader, shape, resources));
                    RunCase($"{label} momentum persists zero input", () => CasePersistence(simShader, shape, resources));
                    RunCase($"{label} top/bottom normal response", () => CaseTopBottom(simShader, shape, resources));
                    RunCase($"{label} roll transport + stop momentum", () => CaseRoll(simShader, shape, resources));
                    RunCase($"{label} freefall vs impact response", () => CaseFreefallImpact(simShader, shape, resources));
                    RunCase($"{label} cadence subdivision", () => CaseCadence(simShader, shape, resources));
                    RunCase($"{label} spin start and stop direction", () => CaseSpinImpulse(simShader, shape, resources));
                    RunCase($"{label} surface containment and travel bound", () => CaseTravelBound(simShader, shape, resources));
                }

                RunCase("cube edge flow and pressure coupling", () => CaseEdgeFlow(simShader, resources));
                RunCase("angular/linear impulse equivalence", () => CaseAngularLinearEquivalence(simShader, resources));
                foreach (int shape in new[] { 1, 2 })
                {
                    RunCase($"shape {shape} moving-site frame velocity", () => CaseRotatingFrameCoriolis(simShader, shape, resources));
                    foreach (float speed in new[] { 2f, 8f })
                        RunCase($"shape {shape} {speed}rps coverage", () => CasePolarCoverage(simShader, shape, speed, resources));
                }
                RunCase("production API cadence", () => FerrofluidMomentumCadenceValidation.Validate(resources));
                if (Failures.Count > 0)
                {
                    throw new Exception(
                        $"{Failures.Count} momentum cases failed: {string.Join(" | ", Failures)}");
                }

                Debug.Log("[FerrofluidMomentumValidation] PASS: surface momentum persists through the integrator.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
            finally
            {
                RenderTexture.active = null;
                foreach (FerrofluidValidationRuntime fixture in RuntimeFixtures)
                    fixture.Dispose();
                RuntimeFixtures.Clear();
                foreach (UnityEngine.Object resource in resources)
                {
                    if (resource != null)
                        UnityEngine.Object.DestroyImmediate(resource);
                }
            }
        }

        private static void RunCase(string name, Action test)
        {
            try
            {
                test();
                Debug.Log($"[FerrofluidMomentumValidation] {name}: PASS");
            }
            catch (Exception exception)
            {
                Exception failure = exception.GetBaseException();
                Failures.Add($"{name}: {failure.GetType().Name}: {failure.Message}");
                Debug.LogException(exception);
            }
        }

        private static void CaseLinearImpulse(Shader shader, int shape, List<UnityEngine.Object> resources)
        {
            Fixture fixture = new(shader, shape, resources, zeroForce: true);
            Color[] baseline = fixture.ReadSites();
            int sideIndex = fixture.NearestSite(new Vector3(0.5f, 0f, 0f), baseline);
            fixture.SetMomentum(Matrix4x4.identity, new Vector3(0f, -0.05f, 0f), Vector3.zero, Vector3.zero);
            fixture.Integrate(1);
            Color[] after = fixture.ReadSites();
            Vector3 displacement = new(after[sideIndex].r - baseline[sideIndex].r, after[sideIndex].g - baseline[sideIndex].g, after[sideIndex].b - baseline[sideIndex].b);
            float expectedY = -0.05f * 0.28f * DeltaTimeSeconds;
            AssertClose(
                expectedY,
                displacement.y,
                (Mathf.Abs(expectedY) * 0.05f) + 0.000002f,
                $"side displacement should track the impulse gain ({displacement.y:F6} vs {expectedY:F6})");
        }

        private static void CaseAngularLinearEquivalence(Shader shader, List<UnityEngine.Object> resources)
        {
            // An angular stop releasing cross(omega, p) must move a site identically to a linear
            // impulse of the same velocity: one body-surface delta, one 0.28 gain.
            Vector3 omega = new(0f, Mathf.PI * 4f, 0f);
            foreach (int shape in new[] { 1, 2 })
            {
                string label = shape == 1 ? "cube" : "sphere";
                foreach (Vector3 target in new[] { new Vector3(0.5f, 0f, 0.05f), new Vector3(0.45f, 0.2f, 0.05f), new Vector3(0f, 0.48f, 0.15f) })
                {
                    Fixture angular = new(shader, shape, resources, zeroForce: true);
                    Fixture linear = new(shader, shape, resources, zeroForce: true);
                    Color[] baseline = angular.ReadSites();
                    int sideIndex = angular.NearestSite(target, baseline);
                    Vector3 site = new(baseline[sideIndex].r, baseline[sideIndex].g, baseline[sideIndex].b);
                    Vector3 equivalentLinear = Vector3.Cross(omega, site);
                    angular.SetMomentum(Matrix4x4.identity, Vector3.zero, omega, Vector3.zero);
                    angular.Integrate(1);
                    Color[] angularAfter = angular.ReadSites();
                    linear.SetMomentum(Matrix4x4.identity, equivalentLinear, Vector3.zero, Vector3.zero);
                    linear.Integrate(1);
                    Color[] linearAfter = linear.ReadSites();
                    Vector3 angularDelta = new(
                        angularAfter[sideIndex].r - baseline[sideIndex].r,
                        angularAfter[sideIndex].g - baseline[sideIndex].g,
                        angularAfter[sideIndex].b - baseline[sideIndex].b);
                    Vector3 linearDelta = new(
                        linearAfter[sideIndex].r - baseline[sideIndex].r,
                        linearAfter[sideIndex].g - baseline[sideIndex].g,
                        linearAfter[sideIndex].b - baseline[sideIndex].b);
                    float error = Vector3.Distance(angularDelta, linearDelta)
                        / Mathf.Max(linearDelta.magnitude, 0.000001f);
                    AssertTrue(
                        linearDelta.sqrMagnitude > 0.000000001f && error < 0.05f,
                        $"the {label} angular stop must vector-match the equivalent linear impulse (angular={angularDelta} linear={linearDelta} error={error:F3})");
                }
            }
        }

        private static void CasePolarCoverage(Shader shader, int shape, float speed, List<UnityEngine.Object> resources)
        {
            Fixture coupled = new(shader, shape, resources, false, production: true);
            Fixture noPressure = new(shader, shape, resources, false, production: true);
            noPressure.SetPressure(0f);
            coupled.Integrate(300);
            noPressure.Integrate(300);
            Color[] rest = coupled.ReadSites();
            CountRegions(rest, out int restTop, out int restBottom, out int restEquator);
            int minTop = restTop;
            int minBottom = restBottom;
            int maxEquator = restEquator;
            float maxPressureEffect = 0f;
            float maxTravel = 0f;
            float peakDeparture = 0f;
            float omega = speed * 2f * Mathf.PI;
            Matrix4x4 frame = Matrix4x4.Rotate(Quaternion.AngleAxis(-omega * DeltaTimeSeconds * Mathf.Rad2Deg, Vector3.up));
            for (int step = 0; step < 240; step++)
            {
                Vector3 previousOmega = step == 0 ? Vector3.zero : Vector3.up * omega;
                coupled.SetMomentum(frame, Vector3.zero, previousOmega, Vector3.up * omega);
                noPressure.SetMomentum(frame, Vector3.zero, previousOmega, Vector3.up * omega);
                coupled.Integrate(1);
                noPressure.Integrate(1);
                Color[] sites = coupled.ReadSites();
                Color[] control = noPressure.ReadSites();
                CountRegions(sites, out int top, out int bottom, out int equator);
                minTop = Mathf.Min(minTop, top);
                minBottom = Mathf.Min(minBottom, bottom);
                maxEquator = Mathf.Max(maxEquator, equator);
                AssertTrue(top > 0 && bottom > 0, $"spin cleared a pole at tick {step}: top={top} bottom={bottom}");
                float departure = 0f;
                for (int index = 0; index < sites.Length; index++)
                {
                    Vector3 at = Position(sites[index]);
                    maxTravel = Mathf.Max(maxTravel, Vector3.Distance(at, coupled.HomePositions[index]));
                    maxPressureEffect = Mathf.Max(maxPressureEffect, Vector3.Distance(at, Position(control[index])));
                    departure += Vector3.Distance(at, Position(rest[index]));
                }

                peakDeparture = Mathf.Max(peakDeparture, departure / sites.Length);
            }

            coupled.SetMomentum(Matrix4x4.identity, Vector3.zero, Vector3.up * omega, Vector3.zero);
            coupled.Integrate(1);
            coupled.SetMomentum(Matrix4x4.identity, Vector3.zero, Vector3.zero, Vector3.zero);
            coupled.Integrate(600);
            Color[] recovered = coupled.ReadSites();
            float recoveryDeparture = 0f;
            for (int index = 0; index < recovered.Length; index++)
                recoveryDeparture += Vector3.Distance(Position(recovered[index]), Position(rest[index])) / recovered.Length;

            Debug.Log(
                $"[FerrofluidMomentumValidation] shape={shape} speed={speed} count={rest.Length} poles={restTop}/{restBottom}->{minTop}/{minBottom} equator={restEquator}->{maxEquator} pressureDelta={maxPressureEffect:F6} travel={maxTravel:F6}/{coupled.MaximumTravel:F6} departure={peakDeparture:F6}->{recoveryDeparture:F6}");
            AssertTrue(maxTravel <= coupled.MaximumTravel + 0.001f, "spinning must obey the same cumulative travel limit as linear motion");
            AssertTrue(minTop < restTop && minBottom < restBottom, "spinning should reduce, but not empty, both polar populations");
            AssertTrue(maxEquator > restEquator, "outward flow must compress the equatorial band");
            AssertTrue(maxPressureEffect > 0.0001f, "real neighbor pressure must affect the spinning lattice");
            AssertTrue(recoveryDeparture < peakDeparture * 0.5f, "elasticity must recover the lattice after the instantaneous stop");
        }

        private static void CountRegions(Color[] sites, out int top, out int bottom, out int equator)
        {
            top = 0;
            bottom = 0;
            equator = 0;
            foreach (Color site in sites)
            {
                if (site.g > 0.35f)
                    top++;
                if (site.g < -0.35f)
                    bottom++;
                if (Mathf.Abs(site.g) < 0.15f)
                    equator++;
            }
        }

        private static void CaseRotatingFrameCoriolis(Shader shader, int shape, List<UnityEngine.Object> resources)
        {
            Fixture fixture = new(shader, shape, resources, true, new[] { new Vector3(0.5f, 0.2f, 0.15f) });
            fixture.SetDeltaTime(0.001f);
            fixture.SetMomentum(Matrix4x4.identity, new Vector3(0.2f, 0.4f, -0.3f), Vector3.zero, Vector3.zero);
            fixture.Integrate(1);
            Vector3 before = Position(fixture.ReadSites()[0]);
            Vector3 oldVelocity = fixture.ReadVelocities()[0];
            Vector3 omega = new(0f, 8f, 0f);
            Matrix4x4 frame = Matrix4x4.Rotate(Quaternion.AngleAxis(-8f * 0.001f * Mathf.Rad2Deg, Vector3.up));
            fixture.SetMomentum(frame, Vector3.zero, omega, omega);
            fixture.Integrate(1);
            Vector3 after = Position(fixture.ReadSites()[0]);
            Vector3 normal = fixture.SurfaceNormal(after);
            Vector3 expected = frame.MultiplyVector(oldVelocity)
                + (0.28f * (frame.MultiplyVector(Vector3.Cross(omega, before)) - Vector3.Cross(omega, after)));
            expected -= normal * Vector3.Dot(expected, normal);
            Vector3 actual = fixture.ReadVelocities()[0];
            AssertTrue(Vector3.Distance(before, after) > 0.00001f, "the rotating-frame test requires a moving site");
            AssertTrue(Vector3.Distance(actual, expected) < 0.00008f, $"relative velocity must include the new surface location: actual={actual:F6} expected={expected:F6}");
        }

        private static void CasePersistence(Shader shader, int shape, List<UnityEngine.Object> resources)
        {
            Fixture fixture = new(shader, shape, resources, zeroForce: true);
            int sideIndex = fixture.NearestSite(new Vector3(0.5f, 0f, 0f), fixture.ReadSites());
            fixture.SetMomentum(Matrix4x4.identity, new Vector3(0f, -0.05f, 0f), Vector3.zero, Vector3.zero);
            fixture.Integrate(1);
            Color[] moved = fixture.ReadSites();
            fixture.SetMomentum(Matrix4x4.identity, Vector3.zero, Vector3.zero, Vector3.zero);
            fixture.Integrate(1);
            Color[] persisted = fixture.ReadSites();
            float secondStep = persisted[sideIndex].g - moved[sideIndex].g;
            AssertTrue(
                secondStep < 0f,
                $"momentum must keep the same sign after inputs clear (step={secondStep:F6})");
        }

        private static void CaseTopBottom(Shader shader, int shape, List<UnityEngine.Object> resources)
        {
            Fixture fixture = new(shader, shape, resources, zeroForce: true);
            Color[] baseline = fixture.ReadSites();
            int topIndex = fixture.NearestSite(new Vector3(0f, 0.5f, 0f), baseline);
            int bottomIndex = fixture.NearestSite(new Vector3(0f, -0.5f, 0f), baseline);
            fixture.SetMomentum(Matrix4x4.identity, new Vector3(0f, -0.05f, 0f), Vector3.zero, Vector3.zero);
            fixture.Integrate(1);
            Color[] after = fixture.ReadSites();
            float topDrift = Mathf.Abs(after[topIndex].g - baseline[topIndex].g);
            float bottomDrift = Mathf.Abs(after[bottomIndex].g - baseline[bottomIndex].g);
            AssertTrue(
                topDrift < 0.0001f && bottomDrift < 0.0001f,
                $"a vertical impulse must not push bases off the surface at the poles (top={topDrift:F6} bottom={bottomDrift:F6})");
        }

        private static void CaseRoll(Shader shader, int shape, List<UnityEngine.Object> resources)
        {
            Fixture fixture = new(shader, shape, resources, zeroForce: false);
            Color[] baseline = fixture.ReadSites();
            int sideIndex = fixture.NearestSite(new Vector3(0.5f, 0f, 0f), baseline);
            Quaternion rotation = Quaternion.identity;
            float omega = 3f * Mathf.Deg2Rad / DeltaTimeSeconds;

            // A real roll: each step carries its actual delta rotation and steady angular velocity.
            for (int step = 0; step < 30; step++)
            {
                Quaternion nextRotation = Quaternion.Euler(0f, 0f, 3f) * rotation;
                Matrix4x4 frame = Matrix4x4.Rotate(Quaternion.Inverse(nextRotation) * rotation);
                fixture.SetMomentum(
                    frame,
                    Vector3.zero,
                    new Vector3(0f, 0f, step == 0 ? 0f : omega),
                    new Vector3(0f, 0f, omega));
                fixture.Integrate(1);
                rotation = nextRotation;
            }

            Color[] rolled = fixture.ReadSites();
            float rollMoved = Vector3.Distance(
                new Vector3(baseline[sideIndex].r, baseline[sideIndex].g, baseline[sideIndex].b),
                new Vector3(rolled[sideIndex].r, rolled[sideIndex].g, rolled[sideIndex].b));
            AssertTrue(
                rollMoved > 0.002f,
                $"a real 90 degree roll must transport the side base (moved={rollMoved:F4})");

            // Stopping the body: Rrel*cross(omegaPrev, p) keeps the rotational momentum signed.
            fixture.SetMomentum(
                Matrix4x4.identity,
                Vector3.zero,
                new Vector3(0f, 0f, omega),
                Vector3.zero);
            fixture.Integrate(1);
            Color[] stopped = fixture.ReadSites();
            float stopDelta = stopped[sideIndex].g - rolled[sideIndex].g;
            AssertTrue(
                Mathf.Abs(stopDelta) > 0.00005f,
                $"stopping the rotation must release retained momentum (delta={stopDelta:F6})");
        }

        private static void CaseFreefallImpact(Shader shader, int shape, List<UnityEngine.Object> resources)
        {
            Fixture fixture = new(shader, shape, resources, zeroForce: false);
            Color[] baseline = fixture.ReadSites();
            int sideIndex = fixture.NearestSite(new Vector3(0.5f, 0f, 0f), baseline);

            // Freefall adds +9.8dt upward relative response; the impact reversal is stronger.
            fixture.SetMomentum(Matrix4x4.identity, new Vector3(0f, 9.8f * DeltaTimeSeconds, 0f), Vector3.zero, Vector3.zero);
            fixture.Integrate(1);
            Color[] falling = fixture.ReadSites();
            float fallDelta = falling[sideIndex].g - baseline[sideIndex].g;
            fixture.SetMomentum(Matrix4x4.identity, new Vector3(0f, -0.3f, 0f), Vector3.zero, Vector3.zero);
            fixture.Integrate(1);
            Color[] impacted = fixture.ReadSites();
            float impactDelta = impacted[sideIndex].g - falling[sideIndex].g;
            AssertTrue(
                fallDelta > 0f && impactDelta < 0f,
                $"freefall lifts and impact drops the side base (fall={fallDelta:F6} impact={impactDelta:F6})");

            // The production spring restores bases toward home once inputs stop.
            fixture.SetMomentum(Matrix4x4.identity, Vector3.zero, Vector3.zero, Vector3.zero);
            fixture.Integrate(400);
            Color[] recovered = fixture.ReadSites();
            float residual = Vector3.Distance(
                new Vector3(baseline[sideIndex].r, baseline[sideIndex].g, baseline[sideIndex].b),
                new Vector3(recovered[sideIndex].r, recovered[sideIndex].g, recovered[sideIndex].b));
            float peak = Mathf.Abs(impactDelta) + Mathf.Abs(fallDelta);
            AssertTrue(
                residual < peak * 0.5f,
                $"the elastic spring must restore the displaced base (residual={residual:F4} peak={peak:F4})");
        }

        private static void CaseCadence(Shader shader, int shape, List<UnityEngine.Object> resources)
        {
            float[] displacements = new float[3];
            int[] steps = { 1, 2, 4 };
            for (int run = 0; run < steps.Length; run++)
            {
                Fixture fixture = new(shader, shape, resources, zeroForce: true);
                Color[] baseline = fixture.ReadSites();
                int sideIndex = fixture.NearestSite(new Vector3(0.5f, 0f, 0f), baseline);
                fixture.SetDeltaTime(DeltaTimeSeconds / steps[run]);
                for (int step = 0; step < steps[run]; step++)
                {
                    Vector3 impulse = step == 0 ? new Vector3(0f, -0.05f, 0f) : Vector3.zero;
                    fixture.SetMomentum(Matrix4x4.identity, impulse, Vector3.zero, Vector3.zero);
                    fixture.Integrate(1);
                }

                Color[] after = fixture.ReadSites();
                displacements[run] = after[sideIndex].g - baseline[sideIndex].g;
            }

            float largest = Mathf.Max(Mathf.Abs(displacements[0] - displacements[1]), Mathf.Abs(displacements[0] - displacements[2]));
            AssertTrue(
                displacements[0] < -0.00005f && largest < 0.000005f,
                $"the same integrated impulse must not depend on the step count ({displacements[0]:F6}/{displacements[1]:F6}/{displacements[2]:F6})");
        }

        private static void CaseSpinImpulse(Shader shader, int shape, List<UnityEngine.Object> resources)
        {
            Vector3[] directions = { new(1f, 0.03f, 0.04f), new(0.03f, 1f, 0.04f) };
            Fixture start = new(shader, shape, resources, true, directions);
            Color[] before = start.ReadSites();
            start.SetMomentum(Matrix4x4.identity, Vector3.zero, Vector3.zero, Vector3.forward);
            start.Integrate(1);
            Color[] after = start.ReadSites();
            AssertTrue(after[0].g < before[0].g, "starting positive Z rotation must leave +X spikes behind toward -Y");
            AssertTrue(after[1].r > before[1].r, "the same rotation must leave +Y spikes behind toward +X");

            Fixture stop = new(shader, shape, resources, true, directions);
            before = stop.ReadSites();
            stop.SetMomentum(Matrix4x4.identity, Vector3.zero, Vector3.forward, Vector3.zero);
            stop.Integrate(1);
            after = stop.ReadSites();
            AssertTrue(after[0].g > before[0].g, "stopping positive Z rotation must preserve +Y momentum on +X");
            AssertTrue(after[1].r < before[1].r, "stopping positive Z rotation must preserve -X momentum on +Y");

            // After a clockwise quarter-turn the local +Y face is the world's right face.
            Fixture landed = new(shader, shape, resources, true, directions);
            before = landed.ReadSites();
            landed.SetMomentum(Matrix4x4.identity, Vector3.zero, -Vector3.forward, Vector3.zero);
            landed.Integrate(1);
            after = landed.ReadSites();
            Quaternion orientation = Quaternion.Euler(0f, 0f, -90f);
            Vector3 worldStep = orientation * (Position(after[1]) - Position(before[1]));
            AssertTrue(worldStep.y < -0.0001f, "the newly vertical right face must keep downward world momentum after landing");
        }

        private static void CaseTravelBound(Shader shader, int shape, List<UnityEngine.Object> resources)
        {
            Fixture fixture = new(shader, shape, resources, false);
            Color[] before = fixture.ReadSites();
            fixture.SetMomentum(Matrix4x4.identity, new Vector3(0f, -100f, 0f), Vector3.zero, Vector3.zero);
            fixture.Integrate(1);
            fixture.SetMomentum(Matrix4x4.identity, Vector3.zero, Vector3.zero, Vector3.zero);
            for (int step = 0; step < 60; step++)
            {
                fixture.Integrate(1);
                Color[] after = fixture.ReadSites();
                for (int index = 0; index < before.Length; index++)
                {
                    Vector3 position = Position(after[index]);
                    float exponent = shape == 2 ? 2f : Mathf.Lerp(8f, 2f, 0.48f);
                    float radius = Mathf.Pow(
                        Mathf.Pow(Mathf.Abs(position.x), exponent)
                        + Mathf.Pow(Mathf.Abs(position.y), exponent)
                        + Mathf.Pow(Mathf.Abs(position.z), exponent),
                        1f / exponent);
                    AssertClose(0.5f, radius, 0.0001f, "bases must remain on the rigid primitive");
                    AssertTrue(
                        Vector3.Distance(position, Position(before[index])) <= fixture.MaximumTravel + 0.0001f,
                        "hard impacts must respect the magnetic travel bound");
                }
            }
        }

        private static void CaseEdgeFlow(Shader shader, List<UnityEngine.Object> resources)
        {
            Vector3[] directions =
            {
                new(0.5f, -0.48f, 0.025f), new(0.48f, -0.5f, 0.025f),
                new(0.48f, 0.5f, 0.025f), new(0.5f, 0.48f, 0.025f),
                new(-0.5f, -0.48f, 0.025f), new(-0.48f, -0.5f, 0.025f),
                new(-0.48f, 0.5f, 0.025f), new(-0.5f, 0.48f, 0.025f),
            };
            Fixture coupled = new(shader, 1, resources, false, directions);
            Fixture uncoupled = new(shader, 1, resources, false, directions);
            coupled.EnableNeighbors(resources);
            // Measure impact compression from pressure equilibrium, not an initially overlapping pair.
            coupled.Integrate(300);
            uncoupled.Integrate(300);
            Color[] before = coupled.ReadSites();
            float initialGap = Vector3.Distance(Position(before[0]), Position(before[1]));
            bool bottomCrossed = false;
            bool topCrossed = false;
            float minimumGap = initialGap;
            float maximumNeighborEffect = 0f;
            coupled.SetMomentum(Matrix4x4.identity, Vector3.down * 4f, Vector3.zero, Vector3.zero);
            uncoupled.SetMomentum(Matrix4x4.identity, Vector3.down * 4f, Vector3.zero, Vector3.zero);
            for (int step = 0; step < 24; step++)
            {
                coupled.Integrate(1);
                uncoupled.Integrate(1);
                Color[] after = coupled.ReadSites();
                Color[] control = uncoupled.ReadSites();
                bottomCrossed |= -after[0].g > Mathf.Abs(after[0].r);
                topCrossed |= after[2].r > Mathf.Abs(after[2].g);
                minimumGap = Mathf.Min(minimumGap, Vector3.Distance(Position(after[0]), Position(after[1])));
                for (int index = 0; index < directions.Length; index++)
                    maximumNeighborEffect = Mathf.Max(maximumNeighborEffect, Vector3.Distance(Position(after[index]), Position(control[index])));

                coupled.SetMomentum(Matrix4x4.identity, Vector3.zero, Vector3.zero, Vector3.zero);
                uncoupled.SetMomentum(Matrix4x4.identity, Vector3.zero, Vector3.zero, Vector3.zero);
            }

            Debug.Log($"[FerrofluidMomentumValidation] edge gap={initialGap:F6}->{minimumGap:F6} neighborEffect={maximumNeighborEffect:F6} bottomCrossed={bottomCrossed} topCrossed={topCrossed}");
            AssertTrue(bottomCrossed, "side sites must flow across the lower edge onto the bottom face");
            AssertTrue(topCrossed, "top-edge sites must flow onto the newly vacated side face");
            AssertTrue(minimumGap < initialGap * 0.98f, "incoming side sites must compress the bottom neighborhood");
            AssertTrue(maximumNeighborEffect > 0.00001f, "pressure and viscosity must affect actual site motion, not just render footprints");
        }

        private static Vector3 Position(Color value)
        {
            return new Vector3(value.r, value.g, value.b);
        }

        private static void AssertTrue(bool condition, string what)
        {
            if (!condition)
            {
                throw new Exception(what);
            }
        }

        private static void AssertClose(float expected, float actual, float tolerance, string what)
        {
            if (Mathf.Abs(expected - actual) >= tolerance)
            {
                throw new Exception($"{what}: expected {expected:F6}, actual {actual:F6}");
            }
        }

        private sealed class Fixture
        {
            private readonly int Stride;
            private readonly int _shape;
            private readonly float _roundness;
            private readonly float _yaw;

            private readonly Material _material;
            private readonly RenderTexture _siteSurface;
            private RenderTexture _current;
            private RenderTexture _next;
            private RenderTexture _densityPressure;
            private RenderTexture _force;
            private readonly int _count;

            internal float MaximumTravel { get; private set; } = 0.5f;

            internal Vector3[] HomePositions { get; private set; }

            internal Fixture(
                Shader shader,
                int shape,
                List<UnityEngine.Object> resources,
                bool zeroForce,
                Vector3[] directions = null,
                bool production = false)
            {
                _shape = shape;
                _roundness = 0.48f;
                if (production)
                {
                    // The bake owns several coupled textures. Use its constructor instead of a partially initialized object.
                    FerrofluidValidationRuntime runtime = new(
                        shape == 1 ? "RoundedCube" : "Sphere",
                        FerrofluidValidationRuntime.CreateConfig());
                    RuntimeFixtures.Add(runtime);
                    _count = runtime.Count;
                    Stride = (int)runtime.Get("_particleTextureSize");
                    _material = (Material)runtime.Get("_simulationMaterial");
                    _siteSurface = (RenderTexture)runtime.Get("_siteSurface");
                    _current = (RenderTexture)runtime.Get("_currentState");
                    _next = (RenderTexture)runtime.Get("_nextState");
                    _densityPressure = (RenderTexture)runtime.Get("_densityPressure");
                    _force = (RenderTexture)runtime.Get("_force");
                    _roundness = _material.GetFloat("_SurfaceRoundness");
                    Vector4 yaw = _material.GetVector("_PrimitiveYawCosSin");
                    _yaw = Mathf.Atan2(yaw.y, yaw.x) * Mathf.Rad2Deg;
                    HomePositions = runtime.ReadHomes();
                    _material.SetFloat("_DeltaTimeMilliseconds", DeltaTimeSeconds * 1000f);
                    SetMomentum(Matrix4x4.identity, Vector3.zero, Vector3.zero, Vector3.zero);
                    return;
                }

                Stride = 8;
                _count = directions == null ? Stride * Stride : directions.Length;
                _material = new Material(shader);
                _material.EnableKeyword("FERROFLUID_BRUTE_FORCE_HEIGHT_MAP");
                resources.Add(_material);
                Texture2D rest = new(Stride, Stride, TextureFormat.RGBAFloat, false, true)
                {
                    filterMode = FilterMode.Point,
                };
                resources.Add(rest);
                Color[] restPixels = new Color[Stride * Stride];
                for (int index = 0; index < _count; index++)
                {
                    int x = index % Stride;
                    int y = index / Stride;
                    Vector2 chart = directions == null
                        ? new Vector2((((x + 0.5f) / Stride) * 2f) - 1f, (((y + 0.5f) / Stride) * 2f) - 1f)
                        : Encode(directions[index]);
                    restPixels[index] = new Color(chart.x, chart.y, 0f, 0f);
                }

                rest.SetPixels(restPixels);
                rest.Apply(false, false);
                Texture2D springState = new(Stride, Stride, TextureFormat.RGBAFloat, false, true)
                {
                    filterMode = FilterMode.Point,
                };
                resources.Add(springState);
                Color[] springPixels = new Color[Stride * Stride];
                for (int index = 0; index < springPixels.Length; index++)
                {
                    springPixels[index] = new Color(0.4f, 0.5f, 0.5f, 0.4f);
                }

                springState.SetPixels(springPixels);
                springState.Apply(false, false);
                _current = CreateRenderTexture(Stride, resources);
                _next = CreateRenderTexture(Stride, resources);
                _siteSurface = CreateRenderTexture(Stride, resources);
                RenderTexture restSurface = CreateRenderTexture(Stride, resources);
                Graphics.Blit(rest, _current);
                _material.SetInt("_ParticleCount", _count);
                _material.SetInt("_ParticleTextureSize", Stride);
                _material.SetFloat("_SurfaceShapeMode", shape);
                _material.SetFloat("_SurfaceRoundness", 0.48f);
                _material.SetVector("_PrimitiveYawCosSin", new Vector2(1f, 0f));
                _material.SetVector("_DomainScale", new Vector2(15.36f, 15.36f));
                _material.SetFloat("_H", 1f);
                _material.SetFloat("_PointerRadius", 5.1f);
                _material.SetFloat("_MaximumSlide", 4.1f);
                _material.SetFloat("_SlideFraction", 0.5f);
                _material.SetFloat("_SlideFalloffExponent", 0.7f);
                _material.SetFloat("_Elasticity", zeroForce ? 0f : 80f);
                _material.SetFloat("_Damping", zeroForce ? 0f : 7f);
                _material.SetFloat("_PrimitiveSpikeSpacing", 0.16f);
                _material.SetFloat("_PrimitiveCellMetric", 0.64f * 0.26f / 1.4f);
                _material.SetFloat("_DeltaTimeMilliseconds", DeltaTimeSeconds * 1000f);
                _material.SetFloat("_SpikesEnabled", 1f);
                _material.SetFloat("_PointerStrength", 0f);
                _material.SetFloat("_SecondaryMagneticFieldStrength", 0f);
                _material.SetTexture("_RestPositionTex", rest);
                _material.SetTexture("_SpringStateTex", springState);
                _material.SetTexture("_DensityPressureTex", Texture2D.whiteTexture);
                _material.SetTexture("_ForceTex", Texture2D.blackTexture);
                _material.SetTexture("_SiteSurfaceTex", _siteSurface);
                _material.SetTexture("_RestSurfaceTex", restSurface);
                _material.SetTexture("_StateTex", _current);
                Graphics.Blit(Texture2D.blackTexture, restSurface, _material, 6);
                Graphics.Blit(Texture2D.blackTexture, _siteSurface, _material, 4);
                Color[] homes = ReadSites();
                HomePositions = new Vector3[homes.Length];
                for (int index = 0; index < homes.Length; index++)
                    HomePositions[index] = Position(homes[index]);
                if (directions != null && directions.Length > 1)
                {
                    // Explicit samples need their actual rest spacing, not the coarser eight-by-eight grid spacing.
                    _material.SetFloat("_PrimitiveSpikeSpacing", FerrofluidSurfaceDistribution.MeanSpacing(HomePositions));
                    Graphics.Blit(Texture2D.blackTexture, restSurface, _material, 6);
                    Graphics.Blit(Texture2D.blackTexture, _siteSurface, _material, 4);
                }

                SetMomentum(Matrix4x4.identity, Vector3.zero, Vector3.zero, Vector3.zero);
            }

            internal void SetPressure(float pressure) => _material.SetFloat("_GasConstant", pressure);

            internal Vector3 SurfaceNormal(Vector3 point)
            {
                float exponent = _shape == 2 ? 2f : Mathf.Lerp(8f, 2f, _roundness);
                return new Vector3(
                    Mathf.Sign(point.x) * Mathf.Pow(Mathf.Abs(point.x), exponent - 1f),
                    Mathf.Sign(point.y) * Mathf.Pow(Mathf.Abs(point.y), exponent - 1f),
                    Mathf.Sign(point.z) * Mathf.Pow(Mathf.Abs(point.z), exponent - 1f)).normalized;
            }

            internal Vector3[] ReadVelocities()
            {
                Color[] states = ReadTexture(_current);
                Vector3[] velocities = new Vector3[_count];
                const float epsilon = 0.0002f;
                for (int index = 0; index < _count; index++)
                {
                    Vector2 chart = new(states[index].r, states[index].g);
                    Vector3 jx = (SurfaceAt(chart + Vector2.right * epsilon) - SurfaceAt(chart - Vector2.right * epsilon)) / (2f * epsilon);
                    Vector3 jy = (SurfaceAt(chart + Vector2.up * epsilon) - SurfaceAt(chart - Vector2.up * epsilon)) / (2f * epsilon);
                    velocities[index] = jx * states[index].b + jy * states[index].a;
                }

                return velocities;
            }

            private Vector3 SurfaceAt(Vector2 chart)
            {
                Vector3 direction = new(chart.x, 1f - Mathf.Abs(chart.x) - Mathf.Abs(chart.y), chart.y);
                float fold = Mathf.Clamp01(-direction.y);
                direction.x += direction.x >= 0f ? -fold : fold;
                direction.z += direction.z >= 0f ? -fold : fold;
                direction = Quaternion.AngleAxis(_yaw, Vector3.up) * direction;
                float exponent = _shape == 2 ? 2f : Mathf.Lerp(8f, 2f, _roundness);
                float norm = Mathf.Pow(
                    Mathf.Pow(Mathf.Abs(direction.x), exponent)
                    + Mathf.Pow(Mathf.Abs(direction.y), exponent)
                    + Mathf.Pow(Mathf.Abs(direction.z), exponent),
                    1f / exponent);
                return direction * (0.5f / norm);
            }

            internal void SetDeltaTime(float seconds)
            {
                _material.SetFloat("_DeltaTimeMilliseconds", seconds * 1000f);
            }

            internal void EnableNeighbors(List<UnityEngine.Object> resources)
            {
                _densityPressure = CreateRenderTexture(Stride, resources);
                _force = CreateRenderTexture(Stride, resources);
                _material.SetFloat("_HSQ", 1f);
                _material.SetFloat("_Mass", 1.86f);
                _material.SetFloat("_RestDensity", 2f);
                _material.SetFloat("_GasConstant", 37f);
                _material.SetFloat("_Viscosity", 8.7f);
                _material.SetFloat("_Poly6", 315f / (64f * Mathf.PI));
                _material.SetFloat("_SpikyGradient", -45f / Mathf.PI);
                _material.SetFloat("_ViscosityLaplacian", 45f / Mathf.PI);
            }

            internal void SetMomentum(
                Matrix4x4 frameRotation,
                Vector3 linearVelocityDelta,
                Vector3 previousAngularVelocity,
                Vector3 angularVelocity)
            {
                _material.SetMatrix("_PrimitiveFrameRotation", frameRotation);
                _material.SetVector("_PrimitiveLinearVelocityDelta", linearVelocityDelta);
                _material.SetVector("_PrimitivePreviousAngularVelocity", previousAngularVelocity);
                _material.SetVector("_PrimitiveAngularVelocity", angularVelocity);
            }

            internal void Integrate(int steps)
            {
                for (int step = 0; step < steps; step++)
                {
                    if (_densityPressure != null)
                    {
                        Graphics.Blit(Texture2D.blackTexture, _densityPressure, _material, 0);
                        _material.SetTexture("_DensityPressureTex", _densityPressure);
                        Graphics.Blit(Texture2D.blackTexture, _force, _material, 1);
                        _material.SetTexture("_ForceTex", _force);
                    }

                    Graphics.Blit(Texture2D.blackTexture, _next, _material, 2);
                    ( _current, _next ) = ( _next, _current );
                    _material.SetTexture("_StateTex", _current);
                    Graphics.Blit(Texture2D.blackTexture, _siteSurface, _material, 4);
                }
            }

            internal Color[] ReadSites()
            {
                return ReadTexture(_siteSurface);
            }

            private Color[] ReadTexture(RenderTexture texture)
            {
                RenderTexture active = RenderTexture.active;
                RenderTexture.active = texture;
                Texture2D output = new(Stride, Stride, TextureFormat.RGBAFloat, false, true)
                {
                    filterMode = FilterMode.Point,
                };
                output.ReadPixels(new Rect(0f, 0f, Stride, Stride), 0, 0);
                output.Apply();
                RenderTexture.active = active;
                Color[] allPixels = output.GetPixels();
                Color[] pixels = new Color[_count];
                Array.Copy(allPixels, pixels, _count);
                UnityEngine.Object.DestroyImmediate(output);
                return pixels;
            }

            private static Vector2 Encode(Vector3 direction)
            {
                direction /= Mathf.Abs(direction.x) + Mathf.Abs(direction.y) + Mathf.Abs(direction.z);
                Vector2 chart = new(direction.x, direction.z);
                if (direction.y < 0f)
                    chart = new Vector2((1f - Mathf.Abs(chart.y)) * Mathf.Sign(chart.x), (1f - Mathf.Abs(chart.x)) * Mathf.Sign(chart.y));

                return chart;
            }

            internal int NearestSite(Vector3 target, Color[] positions)
            {
                int best = 0;
                float bestDistance = float.MaxValue;
                for (int index = 0; index < positions.Length; index++)
                {
                    float distance = Vector3.Distance(
                        new Vector3(positions[index].r, positions[index].g, positions[index].b),
                        target);
                    if (distance < bestDistance)
                    {
                        best = index;
                        bestDistance = distance;
                    }
                }

                return best;
            }

            private static RenderTexture CreateRenderTexture(int size, List<UnityEngine.Object> resources)
            {
                RenderTexture texture = new(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
                {
                    filterMode = FilterMode.Point,
                };
                texture.Create();
                resources.Add(texture);
                return texture;
            }
        }
    }
}
