using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidHeldCoverageValidation
    {
        private const float Delta = 1f / 120f;
        private static readonly List<string> Failures = new();
        private static string _captureDirectory;

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidHeldCoverageValidation));

        private static void RunRuntime()
        {
            ValidateSpeeds(new[] { 2f, 8f });
        }

        internal static void ValidateSpeeds(float[] speeds)
        {
            Failures.Clear();
            _captureDirectory = FerrofluidValidationArtifacts.CreateRunDirectory("held-coverage");
            Debug.Log($"[FerrofluidHeldCoverageValidation] captureDirectory={_captureDirectory}");
            foreach (string shape in new[] { "RoundedCube", "Sphere" })
            {
                foreach (float speed in speeds)
                {
                    foreach (bool corner in new[] { false, true })
                        RunCase(shape, speed, corner);
                }
            }

            if (Failures.Count != 0)
                throw new Exception(string.Join(" | ", Failures));

            Debug.Log("[FerrofluidHeldCoverageValidation] PASS: held-pivot motion retains bounded sliding and recovers coverage.");
        }

        private static void RunCase(string shapeName, float speed, bool corner)
        {
            string label = $"{shapeName}-{speed.ToString("0.##", CultureInfo.InvariantCulture)}rps-{(corner ? "corner" : "face")}";
            object config = FerrofluidValidationRuntime.CreateConfig();
            using FerrofluidValidationRuntime runtime = new(shapeName, config);
            Type environment = config.GetType().Assembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true)!;
            object shape = Enum.Parse(config.GetType().Assembly.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", true)!, shapeName);
            BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            float roundness = (float)environment.GetMethod("EvaluateRoundness", flags)!.Invoke(null, new[] { shape, config })!;
            float yaw = (float)environment.GetMethod("EvaluateChartYawDegrees", flags)!.Invoke(null, new[] { shape, config })!;
            MethodInfo project = environment.GetMethod("EvaluateSurfacePositionFromDirection", flags)!;
            MethodInfo normal = environment.GetMethod("EvaluateSurfaceNormalFromDirection", flags)!;
            Vector3 grabDirection = corner ? new Vector3(1f, 1f, 1f) : new Vector3(1f, 0.3f, 0.15f);
            Vector3 grab = (Vector3)project.Invoke(null, new object[] { shape, grabDirection, roundness })!;
            Vector3 outward = (Vector3)normal.Invoke(null, new object[] { shape, grabDirection.normalized, roundness })!;
            Transform body = ((MeshRenderer)runtime.Get("_renderer")).transform;
            Vector3 anchor = new(0f, 1f, 0f);
            body.SetPositionAndRotation(anchor - grab, Quaternion.identity);
            SetFrame(runtime, body, grab, outward, anchor, Vector3.zero, 0f, 0f);
            runtime.Invoke("AdvancePrimitiveSimulation", Delta);
            for (int tick = 0; tick < 375; tick++)
                runtime.Invoke("Simulate", 8f);

            Vector3[] homes = runtime.ReadHomes();
            Vector3[] resting = runtime.ReadSitePositions();
            float spacing = FerrofluidSurfaceDistribution.MeanSpacing(homes);
            int[] referenceBins = CountBins(resting, shapeName == "Sphere");
            Type layout = config.GetType().Assembly.GetType("ChromaGLS.Settings.FerrofluidPrimitiveLayout", true)!;
            object[] sampleArguments = { shape, Math.Max(1, homes.Length / 3), roundness, null!, null!, 0f };
            layout.GetMethod("CreateSamples", flags)!.Invoke(null, sampleArguments);
            Vector3[] probes = (Vector3[])sampleArguments[3];
            float restHole = FerrofluidSurfaceDistribution.MaximumHole(resting, probes) / spacing;
            float maximumHole = restHole;
            float minimumSpacing = float.MaxValue;
            float minimumP10Spacing = float.MaxValue;
            float minimumBinRatio = 1f;
            float maximumTravel = 0f;
            float maximumHomeDistance = 0f;
            float[] nearestSpacing = new float[homes.Length];
            float angle = 0f;
            float previousOmega = 0f;
            Vector3[] worstSites = resting;
            int worstFrame = 0;
            for (int frame = 1; frame <= 360; frame++)
            {
                float time = frame * Delta;
                float envelope = time < 0.2f ? time / 0.2f : time < 2.2f ? 1f : Mathf.Clamp01((2.4f - time) / 0.2f);
                float omega = speed * 2f * Mathf.PI * envelope;
                angle += (previousOmega + omega) * 0.5f * Delta;
                Quaternion rotation = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.up);
                body.SetPositionAndRotation(anchor - (rotation * grab), rotation);
                Vector3 angular = Vector3.up * omega;
                Vector3 linear = -Vector3.Cross(angular, rotation * grab);
                SetFrame(runtime, body, grab, outward, anchor, linear, omega, 1f);
                runtime.Invoke("AdvancePrimitiveSimulation", Delta);
                previousOmega = omega;
                if (frame % 10 != 0)
                    continue;

                Vector3[] sites = runtime.ReadSitePositions();
                float hole = FerrofluidSurfaceDistribution.MaximumHole(sites, probes) / spacing;
                if (hole > maximumHole)
                {
                    maximumHole = hole;
                    worstSites = sites;
                    worstFrame = frame;
                }

                int[] bins = CountBins(sites, shapeName == "Sphere");
                minimumBinRatio = Mathf.Min(minimumBinRatio, CalculateMinimumBinRatio(referenceBins, bins));
                minimumP10Spacing = Mathf.Min(
                    minimumP10Spacing,
                    CalculateSpacingPercentile(sites, spacing, nearestSpacing, out float closestSpacing));
                minimumSpacing = Mathf.Min(minimumSpacing, closestSpacing);

                for (int index = 0; index < sites.Length; index++)
                {
                    if (!float.IsFinite(sites[index].x) || !float.IsFinite(sites[index].y) || !float.IsFinite(sites[index].z))
                        throw new Exception($"{label}: motion produced a non-finite site.");
                    float homeDistance = Vector3.Distance(homes[index], sites[index]);
                    maximumHomeDistance = Mathf.Max(maximumHomeDistance, homeDistance);
                    maximumTravel = Mathf.Max(maximumTravel, homeDistance / spacing);
                }
            }

            SaveSnapshot(label, homes, worstSites, worstFrame, spacing);
            for (int tick = 0; tick < 600; tick++)
            {
                SetFrame(runtime, body, grab, outward, anchor, Vector3.zero, 0f, 0f);
                runtime.Invoke("AdvancePrimitiveSimulation", Delta);
            }

            Vector3[] recovered = runtime.ReadSitePositions();
            float recoveredHole = FerrofluidSurfaceDistribution.MaximumHole(recovered, probes) / spacing;
            float recoveredP10 = CalculateSpacingPercentile(recovered, spacing, nearestSpacing, out _);
            float recoveredBinRatio = CalculateMinimumBinRatio(referenceBins, CountBins(recovered, shapeName == "Sphere"));
            SaveSnapshot(label + "-recovered", homes, recovered, 960, spacing);
            Debug.Log($"[FerrofluidHeldCoverageValidation] {label} restHole={restHole:F4} maxHole={maximumHole:F4} minSpacing={minimumSpacing:F4} minP10Spacing={minimumP10Spacing:F4} minBinRatio={minimumBinRatio:F4} maxTravel={maximumTravel:F4} maxHomeDistance={maximumHomeDistance:F4} recoveredHole={recoveredHole:F4} recoveredP10={recoveredP10:F4} recoveredBinRatio={recoveredBinRatio:F4} worstFrame={worstFrame}");
            if (speed <= 0.5f)
            {
                // Individual sites may pass close together. Normal motion must preserve bulk spacing and coverage.
                Require(maximumHole <= 1.5f, $"{label}: normal motion leaves a large empty region ({maximumHole:F4} spacings)");
                Require(minimumP10Spacing >= 0.5f, $"{label}: normal motion compresses more than a tenth of the sites below half spacing");
                Require(minimumBinRatio >= 0.45f, $"{label}: normal motion drains a face quadrant/equal-area sphere region");
            }

            // Sustained multi-revolution spins may rearrange the lattice, but coverage must recover after their load is removed.
            Require(recoveredHole <= 1.5f, $"{label}: coverage does not recover after removing the magnet and spin");
            Require(recoveredP10 >= 0.5f, $"{label}: bulk compression persists after removing the magnet and spin");
            Require(recoveredBinRatio >= 0.45f, $"{label}: a region remains drained after removing the magnet and spin");
            Require(maximumHomeDistance <= 0.5001f, $"{label}: motion exceeds the half-face home bound");
            Require(maximumTravel >= 0.15f, $"{label}: coverage was preserved by suppressing visible sliding");
        }

        private static float CalculateSpacingPercentile(Vector3[] sites, float spacing, float[] nearest, out float minimum)
        {
            for (int index = 0; index < sites.Length; index++)
                nearest[index] = Mathf.Sqrt(FerrofluidSurfaceDistribution.NearestSquared(sites[index], sites, index)) / spacing;
            Array.Sort(nearest);
            minimum = nearest[0];
            return nearest[Mathf.CeilToInt(nearest.Length * 0.1f) - 1];
        }

        private static float CalculateMinimumBinRatio(int[] reference, int[] counts)
        {
            float minimum = 1f;
            for (int bin = 0; bin < counts.Length; bin++)
                minimum = Mathf.Min(minimum, (float)counts[bin] / reference[bin]);
            return minimum;
        }

        private static void SetFrame(
            FerrofluidValidationRuntime runtime,
            Transform body,
            Vector3 grab,
            Vector3 normal,
            Vector3 anchor,
            Vector3 linear,
            float omega,
            float strength)
        {
            runtime.Invoke(
                "UpdatePrimitiveFrame",
                1f,
                body.TransformPoint(grab + (normal * 0.14f)),
                anchor,
                strength,
                new Vector3(0f, -1000f, 0f),
                0f,
                linear,
                Vector3.up * omega,
                1.6f);
        }

        private static int[] CountBins(Vector3[] sites, bool sphere)
        {
            int[] bins = new int[24];
            foreach (Vector3 site in sites)
            {
                int bin;
                if (sphere)
                {
                    Vector3 direction = site.normalized;
                    int band = Mathf.Clamp(Mathf.FloorToInt((direction.y + 1f) * 2f), 0, 3);
                    float phase = Mathf.Repeat(Mathf.Atan2(direction.z, direction.x) / (2f * Mathf.PI), 1f);
                    bin = (band * 6) + Mathf.Min(5, Mathf.FloorToInt(phase * 6f));
                }
                else
                {
                    float x = Mathf.Abs(site.x);
                    float y = Mathf.Abs(site.y);
                    float z = Mathf.Abs(site.z);
                    int axis = x >= y && x >= z ? 0 : y >= z ? 1 : 2;
                    int sign = site[axis] >= 0f ? 1 : 0;
                    int quadrant = (site[(axis + 1) % 3] >= 0f ? 1 : 0) + (site[(axis + 2) % 3] >= 0f ? 2 : 0);
                    bin = ((axis * 2 + sign) * 4) + quadrant;
                }

                bins[bin]++;
            }

            return bins;
        }

        private static void SaveSnapshot(string label, Vector3[] homes, Vector3[] sites, int frame, float spacing)
        {
            string path = Path.Combine(_captureDirectory, label + ".csv");
            using StreamWriter writer = new(path);
            writer.WriteLine("site,frame,spacing,homeX,homeY,homeZ,liveX,liveY,liveZ");
            for (int index = 0; index < sites.Length; index++)
            {
                writer.Write(index.ToString(CultureInfo.InvariantCulture));
                writer.Write(',' + frame.ToString(CultureInfo.InvariantCulture));
                writer.Write(',' + spacing.ToString("R", CultureInfo.InvariantCulture));
                foreach (Vector3 point in new[] { homes[index], sites[index] })
                {
                    for (int axis = 0; axis < 3; axis++)
                    {
                        writer.Write(',');
                        writer.Write(point[axis].ToString("R", CultureInfo.InvariantCulture));
                    }
                }

                writer.WriteLine();
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                Failures.Add(message);
        }
    }

    internal static class FerrofluidReasonableCoverageValidation
    {
        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidReasonableCoverageValidation));

        private static void RunRuntime()
        {
            FerrofluidHeldCoverageValidation.ValidateSpeeds(new[] { 0f, 0.5f });
        }
    }
}
