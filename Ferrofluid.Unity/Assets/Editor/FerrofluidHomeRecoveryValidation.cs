using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidHomeRecoveryValidation
    {
        private static string _captureDirectory;

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidHomeRecoveryValidation));

        private static void RunRuntime()
        {
            _captureDirectory = FerrofluidValidationArtifacts.CreateRunDirectory("home-recovery");
            Debug.Log($"[FerrofluidHomeRecoveryValidation] captureDirectory={_captureDirectory}");
            foreach (string shapeName in new[] { "RoundedCube", "Sphere" })
                Validate(shapeName);

            Debug.Log("[FerrofluidHomeRecoveryValidation] PASS: immutable identities recover, with pressure and viscosity retained.");
        }

        private static void Validate(string shapeName)
        {
            object config = FerrofluidValidationRuntime.CreateConfig();
            using FerrofluidValidationRuntime runtime = new(shapeName, config);
            Type environment = config.GetType().Assembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true)!;
            object shape = Enum.Parse(config.GetType().Assembly.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", true)!, shapeName);
            BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            MethodInfo project = environment.GetMethod("EvaluateSurfacePositionFromDirection", flags)!;
            MethodInfo encode = environment.GetMethod("EvaluateSurfaceUv", flags)!;
            float roundness = (float)environment.GetMethod("EvaluateRoundness", flags)!.Invoke(null, new[] { shape, config })!;
            Material simulation = (Material)runtime.Get("_simulationMaterial");
            Vector4 yawVector = simulation.GetVector("_PrimitiveYawCosSin");
            float yaw = Mathf.Atan2(yawVector.y, yawVector.x) * Mathf.Rad2Deg;
            simulation.SetMatrix("_PrimitiveFrameRotation", Matrix4x4.identity);
            Vector3[] homes = runtime.ReadHomes();
            Texture immutableHomes = (Texture)runtime.Get("_restPositionTexture");
            Color[] original = FerrofluidValidationRuntime.Read(immutableHomes);
            float spacing = ValidateSpacing(shapeName, homes, shape, roundness, project);

            // Swap adjacent identities without changing the set of physical positions.
            Color[] exchanged = FerrofluidValidationRuntime.CreatePositionState(original);
            bool[] used = new bool[homes.Length];
            for (int pair = 0; pair < 16; pair++)
            {
                int first = (pair * 31) % homes.Length;
                while (used[first])
                    first = (first + 1) % homes.Length;

                int second = -1;
                float nearest = float.MaxValue;
                for (int index = 0; index < homes.Length; index++)
                {
                    float distance = (homes[index] - homes[first]).sqrMagnitude;
                    if (index != first && !used[index] && distance < nearest)
                    {
                        second = index;
                        nearest = distance;
                    }
                }

                used[first] = used[second] = true;
                Color firstPosition = exchanged[first];
                exchanged[first] = exchanged[second];
                exchanged[second] = firstPosition;
            }

            simulation.SetFloat("_GasConstant", 0f);
            simulation.SetFloat("_Viscosity", 0f);
            runtime.WriteState(exchanged);
            double identityBefore = Potential(homes, ReadPositions(runtime));
            Tick(runtime, 250);
            double identityAfter = Potential(homes, ReadPositions(runtime));
            Debug.Log($"[FerrofluidHomeRecoveryValidation] {shapeName} swapped-identity potential ratio={identityAfter / identityBefore:F7}");
            Require(identityAfter < identityBefore * 0.02, $"{shapeName}: displaced identities do not return to their own homes");
            CheckHomes(runtime, immutableHomes, original);

            runtime.Apply(config);
            Require(simulation.GetFloat("_GasConstant") > 0f && simulation.GetFloat("_Viscosity") > 0f, "hybrid recovery must retain pressure and viscosity");
            Tick(runtime, 375);
            Vector3[] baseline = ReadPositions(runtime);
            double baselinePotential = Potential(homes, baseline);
            Vector3[] disturbed = new Vector3[homes.Length];
            Quaternion rotation = Quaternion.AngleAxis(5f, Vector3.up);
            for (int index = 0; index < homes.Length; index++)
                disturbed[index] = (Vector3)project.Invoke(null, new object[] { shape, rotation * baseline[index], roundness })!;

            Color[] shifted = new Color[original.Length];
            for (int index = 0; index < homes.Length; index++)
            {
                Vector2 uv = (Vector2)encode.Invoke(null, new object[] { disturbed[index], yaw })!;
                shifted[index] = new Color((uv.x * 2f) - 1f, (uv.y * 2f) - 1f, 0f, 0f);
            }

            runtime.WriteState(shifted);
            double disturbedPotential = Potential(homes, ReadPositions(runtime));
            for (int tick = 0; tick < 120; tick++)
            {
                float angle = tick / 119f * Mathf.PI;
                Vector3 direction = new(Mathf.Cos(angle), 0.25f, Mathf.Sin(angle));
                Vector3 pointer = (Vector3)project.Invoke(null, new object[] { shape, direction, roundness })!;
                simulation.SetVector("_PrimitiveMagnetPosition", pointer);
                simulation.SetFloat("_PointerStrength", 1f);
                runtime.Invoke("Simulate", 8f);
            }

            simulation.SetFloat("_PointerStrength", 0f);
            Tick(runtime, 375);
            Vector3[] recovered = ReadPositions(runtime);
            double recoveredPotential = Potential(homes, recovered);
            Debug.Log($"[FerrofluidHomeRecoveryValidation] {shapeName} hybrid potentials baseline={baselinePotential:G7} disturbed={disturbedPotential:G7} recovered={recoveredPotential:G7}");
            Require(disturbedPotential > baselinePotential + (spacing * spacing * 0.01), $"{shapeName}: rotation did not disturb home identities");
            Require(
                recoveredPotential <= baselinePotential + ((disturbedPotential - baselinePotential) * 0.25),
                $"{shapeName}: magnet-disturbed hybrid field does not recover home potential");
            CheckHomes(runtime, immutableHomes, original);
            SaveSnapshot(shapeName, homes, baseline, disturbed, recovered);
        }

        private static void Tick(FerrofluidValidationRuntime runtime, int count)
        {
            for (int tick = 0; tick < count; tick++)
                runtime.Invoke("Simulate", 8f);
        }

        private static float ValidateSpacing(string label, Vector3[] homes, object shape, float roundness, MethodInfo project)
        {
            double sum = 0.0;
            double squaredSum = 0.0;
            float minimum = float.MaxValue;
            float maximum = 0f;
            for (int index = 0; index < homes.Length; index++)
            {
                float distance = Mathf.Sqrt(FerrofluidSurfaceDistribution.NearestSquared(homes[index], homes, index));
                sum += distance;
                squaredSum += distance * distance;
                minimum = Mathf.Min(minimum, distance);
                maximum = Mathf.Max(maximum, distance);
            }

            float mean = (float)(sum / homes.Length);
            double cv = Math.Sqrt(Math.Max(0.0, (squaredSum / homes.Length) - (mean * mean))) / mean;
            float holeSquared = 0f;
            if (label == "Sphere")
            {
                int count = homes.Length * 24;
                for (int index = 0; index < count; index++)
                {
                    float y = 1f - ((index + 0.5f) * 2f / count);
                    float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - (y * y)));
                    float angle = index * Mathf.PI * (3f - Mathf.Sqrt(5f));
                    Vector3 sample = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius) * 0.5f;
                    holeSquared = Mathf.Max(holeSquared, FerrofluidSurfaceDistribution.NearestSquared(sample, homes));
                }
            }
            else
            {
                int side = Mathf.CeilToInt(Mathf.Sqrt(homes.Length * 4f));
                for (int row = 0; row < side; row++)
                {
                    for (int column = 0; column < side; column++)
                    {
                        float u = -1f + ((column + 0.5f) * 2f / side);
                        float v = -1f + ((row + 0.5f) * 2f / side);
                        for (int axis = 0; axis < 3; axis++)
                        {
                            for (int sign = -1; sign <= 1; sign += 2)
                            {
                                Vector3 direction = new(u, v, sign);
                                if (axis == 1)
                                    direction = new Vector3(sign, u, v);
                                else if (axis == 2)
                                    direction = new Vector3(v, sign, u);

                                Vector3 sample = (Vector3)project.Invoke(null, new object[] { shape, direction, roundness })!;
                                holeSquared = Mathf.Max(holeSquared, FerrofluidSurfaceDistribution.NearestSquared(sample, homes));
                            }
                        }
                    }
                }
            }

            float holeRatio = Mathf.Sqrt(holeSquared) / mean;
            Debug.Log($"[FerrofluidHomeRecoveryValidation] {label} spacing mean={mean:F7} cv={cv:F5} minRatio={minimum / mean:F5} maxRatio={maximum / mean:F5} holeRatio={holeRatio:F5}");
            Require(cv < 0.18 && minimum / mean >= 0.65f && maximum / mean <= 1.35f && holeRatio <= 0.9f, $"{label}: baked physical spacing has excessive outliers or gaps");
            return mean;
        }

        private static Vector3[] ReadPositions(FerrofluidValidationRuntime runtime)
        {
            return runtime.ReadSitePositions();
        }

        private static double Potential(Vector3[] homes, Vector3[] positions)
        {
            double sum = 0.0;
            for (int index = 0; index < homes.Length; index++)
                sum += (homes[index] - positions[index]).sqrMagnitude;

            return sum / homes.Length;
        }

        private static void CheckHomes(FerrofluidValidationRuntime runtime, Texture expectedTexture, Color[] original)
        {
            Require(ReferenceEquals(expectedTexture, runtime.Get("_restPositionTexture")), "simulation replaced immutable homes");
            Color[] current = FerrofluidValidationRuntime.Read(expectedTexture);
            for (int index = 0; index < original.Length; index++)
                Require(current[index].Equals(original[index]), $"home identity {index} was rebased during physics");
        }

        private static void SaveSnapshot(string shape, Vector3[] homes, Vector3[] baseline, Vector3[] disturbed, Vector3[] recovered)
        {
            string path = Path.Combine(_captureDirectory, shape + ".csv");
            using StreamWriter writer = new(path);
            writer.WriteLine("site,homeX,homeY,homeZ,baselineX,baselineY,baselineZ,disturbedX,disturbedY,disturbedZ,recoveredX,recoveredY,recoveredZ");
            for (int index = 0; index < homes.Length; index++)
            {
                writer.Write(index.ToString(CultureInfo.InvariantCulture));
                foreach (Vector3 point in new[] { homes[index], baseline[index], disturbed[index], recovered[index] })
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
                throw new Exception(message);
        }
    }
}
