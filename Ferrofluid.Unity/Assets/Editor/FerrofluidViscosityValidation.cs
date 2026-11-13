using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidViscosityValidation
    {
        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidViscosityValidation));

        private static void RunRuntime()
        {
            List<string> failures = new();
            foreach (string shape in new[] { "RoundedCube", "Sphere" })
            {
                object config = FerrofluidValidationRuntime.CreateConfig();
                string yawSetting = shape == "RoundedCube" ? "FerrofluidCubeChartYaw" : "FerrofluidSphereChartYaw";
                FerrofluidValidationRuntime.SetConfig(config, yawSetting, 0f);
                using FerrofluidValidationRuntime runtime = new(shape, config);
                Vector3[] homes = runtime.ReadHomes();
                Vector3[] firstForce = Measure(runtime, out Vector3[] firstVelocity);
                FerrofluidValidationRuntime.SetConfig(config, yawSetting, 45f);
                runtime.Apply(config);
                Vector3[] secondForce = Measure(runtime, out Vector3[] secondVelocity);
                Vector3[] changedHomes = runtime.ReadHomes();
                float homeError = 0f;
                for (int index = 0; index < homes.Length; index++)
                    homeError = Mathf.Max(homeError, Vector3.Distance(homes[index], changedHomes[index]));

                double velocityError = RelativeRms(firstVelocity, secondVelocity);
                double forceError = RelativeRms(firstForce, secondForce);
                Debug.Log($"[FerrofluidViscosityValidation] {shape} homeError={homeError:F7} velocityRelativeRms={velocityError:F7} forceRelativeRms={forceError:F7}");
                if (homeError > 0.0003f || velocityError > 0.0001)
                    failures.Add($"{shape}: chart-invariant viscosity fixture changed its physical inputs");
                else if (forceError > 0.005)
                    failures.Add($"{shape}: identical physical sites and velocities produce chart-dependent viscosity ({forceError:F6})");
            }

            if (failures.Count != 0)
                throw new Exception(string.Join(" | ", failures));

            Debug.Log("[FerrofluidViscosityValidation] PASS: viscosity uses physical velocities, not chart components.");
        }

        private static Vector3[] Measure(FerrofluidValidationRuntime runtime, out Vector3[] velocity)
        {
            Material simulation = (Material)runtime.Get("_simulationMaterial");
            int pass = simulation.FindPass("VALIDATION");
            if (pass < 0)
                throw new Exception("The simulation validation pass is unavailable.");

            simulation.SetFloat("_GasConstant", 0f);
            simulation.SetVector("_ValidationLinearVelocity", new Vector3(0.3f, -0.2f, 0.4f));
            simulation.SetVector("_ValidationAngularVelocity", new Vector3(0.4f, 0.7f, -0.25f));
            simulation.SetFloat("_ValidationMode", 0f);
            RenderTexture current = (RenderTexture)runtime.Get("_currentState");
            RenderTexture next = (RenderTexture)runtime.Get("_nextState");
            simulation.SetTexture("_StateTex", current);
            Graphics.Blit(Texture2D.blackTexture, next, simulation, pass);
            Graphics.Blit(next, current);
            velocity = ReadPhysical(runtime, simulation, pass, 2f);
            runtime.Invoke("CalculateParticleForces");
            return ReadPhysical(runtime, simulation, pass, 1f);
        }

        private static Vector3[] ReadPhysical(FerrofluidValidationRuntime runtime, Material simulation, int pass, float mode)
        {
            Texture current = (Texture)runtime.Get("_currentState");
            RenderTexture output = RenderTexture.GetTemporary(current.width, current.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            try
            {
                simulation.SetFloat("_ValidationMode", mode);
                simulation.SetTexture("_StateTex", current);
                simulation.SetTexture("_ForceTex", (Texture)runtime.Get("_force"));
                Graphics.Blit(Texture2D.blackTexture, output, simulation, pass);
                Color[] pixels = FerrofluidValidationRuntime.Read(output);
                Vector3[] physical = new Vector3[runtime.Count];
                for (int index = 0; index < physical.Length; index++)
                    physical[index] = new Vector3(pixels[index].r, pixels[index].g, pixels[index].b);

                return physical;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(output);
            }
        }

        private static double RelativeRms(Vector3[] baseline, Vector3[] current)
        {
            double difference = 0.0;
            double magnitude = 0.0;
            for (int index = 0; index < baseline.Length; index++)
            {
                difference += (baseline[index] - current[index]).sqrMagnitude;
                magnitude += baseline[index].sqrMagnitude;
            }

            if (magnitude < 0.0000000001 || double.IsNaN(difference) || double.IsInfinity(difference))
                throw new Exception("The viscosity probe returned a zero or non-finite physical field.");

            return Math.Sqrt(difference / magnitude);
        }
    }
}
