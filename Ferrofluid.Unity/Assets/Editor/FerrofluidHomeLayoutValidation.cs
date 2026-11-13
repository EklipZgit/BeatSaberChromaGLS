using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidHomeLayoutValidation
    {
        private static readonly List<string> Failures = new();

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidHomeLayoutValidation));

        private static void RunRuntime()
        {
            Failures.Clear();
            object config = FerrofluidValidationRuntime.CreateConfig();
            using (FerrofluidValidationRuntime cube = new("RoundedCube", config))
            {
                Vector3[] original = cube.ReadHomes();
                int symmetric = SymmetricEdgeSites(original);
                Debug.Log($"[FerrofluidHomeLayoutValidation] cube count={original.Length} symmetricEdgeSites={symmetric}");
                Require(symmetric >= 96, $"cube edge homes lack matching rotational zigzags ({symmetric} symmetric sites)");

                FerrofluidValidationRuntime.SetConfig(config, "FerrofluidCubeChartYaw", 0f);
                cube.Apply(config);
                float yawError = MaximumDifference(original, cube.ReadHomes());
                Debug.Log($"[FerrofluidHomeLayoutValidation] chart yaw physical-home error={yawError:F7}");
                Require(yawError < 0.0003f, $"changing chart yaw moves physical homes by {yawError:F6}");

                FerrofluidValidationRuntime.SetConfig(config, "FerrofluidCubeChartYaw", 45f);
                FerrofluidValidationRuntime.SetConfig(config, "FerrofluidCubeRoundness", 0.21f);
                cube.Apply(config);
                using FerrofluidValidationRuntime fresh = new("RoundedCube", config);
                float historyError = MaximumDifference(fresh.ReadHomes(), cube.ReadHomes());
                Debug.Log($"[FerrofluidHomeLayoutValidation] changed/fresh roundness home error={historyError:F7}");
                Require(historyError < 0.0003f, $"roundness changes retain stale homes ({historyError:F6} vs fresh bake)");
            }

            ValidatePressureCoordinates();
            if (Failures.Count != 0)
                throw new Exception(string.Join(" | ", Failures));

            Debug.Log("[FerrofluidHomeLayoutValidation] PASS: physical homes, invalidation, and pressure do not depend on chart coordinates.");
        }

        private static void ValidatePressureCoordinates()
        {
            object config = FerrofluidValidationRuntime.CreateConfig();
            FerrofluidValidationRuntime.SetConfig(config, "FerrofluidSphereChartYaw", 0f);
            using FerrofluidValidationRuntime sphere = new("Sphere", config);
            Vector3[] homes = sphere.ReadHomes();
            sphere.Invoke("Simulate", 8f);
            Color[] baseline = FerrofluidValidationRuntime.Read((Texture)sphere.Get("_densityPressure"));
            Type environment = sphere.Simulation.GetType().Assembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true)!;
            MethodInfo encode = environment.GetMethod("EvaluateSurfaceUv", BindingFlags.Static | BindingFlags.NonPublic)!;
            Texture source = (Texture)sphere.Get("_restPositionTexture");
            Color[] original = FerrofluidValidationRuntime.Read(source);
            Texture2D encoded = new(source.width, source.height, TextureFormat.RGBAFloat, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
            };
            Color[] positions = new Color[source.width * source.height];
            for (int index = 0; index < homes.Length; index++)
            {
                Vector2 uv = (Vector2)encode.Invoke(null, new object[] { homes[index], 45f })!;
                positions[index] = new Color((uv.x * 2f) - 1f, (uv.y * 2f) - 1f, original[index].b, original[index].a);
            }

            encoded.SetPixels(positions);
            encoded.Apply();
            sphere.Set("_restPositionTexture", encoded);
            sphere.WriteState(FerrofluidValidationRuntime.CreatePositionState(positions));
            Vector4 yaw = new(Mathf.Cos(Mathf.PI / 4f), Mathf.Sin(Mathf.PI / 4f), 0f, 0f);
            ((Material)sphere.Get("_simulationMaterial")).SetVector("_PrimitiveYawCosSin", yaw);
            sphere.Invoke("Simulate", 8f);
            Color[] rotated = FerrofluidValidationRuntime.Read((Texture)sphere.Get("_densityPressure"));
            float maximumError = 0f;
            for (int index = 0; index < homes.Length; index++)
                maximumError = Mathf.Max(maximumError, Mathf.Abs(rotated[index].r - baseline[index].r) / baseline[index].r);

            Debug.Log($"[FerrofluidHomeLayoutValidation] identical physical sites chart-density error={maximumError:F6}");
            Require(maximumError < 0.0005f, $"pressure depends on chart yaw instead of physical spacing ({maximumError:F6})");
        }

        private static float MaximumDifference(Vector3[] expected, Vector3[] actual)
        {
            Require(expected.Length == actual.Length, "configuration changed the site count unexpectedly");
            float maximum = 0f;
            for (int index = 0; index < Math.Min(expected.Length, actual.Length); index++)
                maximum = Mathf.Max(maximum, Vector3.Distance(expected[index], actual[index]));

            return maximum;
        }

        private static int SymmetricEdgeSites(Vector3[] homes)
        {
            int[][] permutations =
            {
                new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
                new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 },
            };
            int count = 0;
            foreach (Vector3 home in homes)
            {
                float largest = Mathf.Max(Mathf.Abs(home.x), Mathf.Max(Mathf.Abs(home.y), Mathf.Abs(home.z)));
                float smallest = Mathf.Min(Mathf.Abs(home.x), Mathf.Min(Mathf.Abs(home.y), Mathf.Abs(home.z)));
                float middle = Mathf.Abs(home.x) + Mathf.Abs(home.y) + Mathf.Abs(home.z) - largest - smallest;
                if (largest - middle > 0.08f || smallest > largest * 0.9f)
                    continue;

                bool symmetric = true;
                foreach (int[] permutation in permutations)
                {
                    int inversions = 0;
                    for (int a = 0; a < 3; a++)
                    {
                        for (int b = a + 1; b < 3; b++)
                        {
                            if (permutation[a] > permutation[b])
                                inversions++;
                        }
                    }

                    int parity = inversions % 2 == 0 ? 1 : -1;
                    for (int x = -1; x <= 1; x += 2)
                    {
                        for (int y = -1; y <= 1; y += 2)
                        {
                            for (int z = -1; z <= 1; z += 2)
                            {
                                if (parity * x * y * z != 1)
                                    continue;

                                Vector3 rotated = new(home[permutation[0]] * x, home[permutation[1]] * y, home[permutation[2]] * z);
                                float nearest = float.MaxValue;
                                foreach (Vector3 candidate in homes)
                                    nearest = Mathf.Min(nearest, (candidate - rotated).sqrMagnitude);

                                symmetric &= nearest < 0.00000001f;
                            }
                        }
                    }
                }

                if (symmetric)
                    count++;
            }

            return count;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                Failures.Add(message);
        }
    }
}
