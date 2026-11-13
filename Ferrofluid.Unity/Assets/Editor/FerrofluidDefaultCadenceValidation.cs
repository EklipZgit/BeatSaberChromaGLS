using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidDefaultCadenceValidation
    {
        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidDefaultCadenceValidation));

        private static void RunRuntime()
        {
            string directory = FerrofluidValidationArtifacts.CreateRunDirectory("default-fluid-cadence");
            foreach (string shape in new[] { "RoundedCube", "Sphere" })
                Validate(shape, directory);

            Debug.Log($"[FerrofluidDefaultCadenceValidation] PASS: default cube/sphere SPH and spike springs stay finite and bounded at 30 rendered frames/s. Artifacts={directory}");
        }

        private static void Validate(string shape, string directory)
        {
            const float frameSeconds = 1f / 30f;
            object config = FerrofluidValidationRuntime.CreateConfig();
            using FerrofluidValidationRuntime runtime = new(shape, config);
            Transform root = ((Component)runtime.Get("_renderer")).transform;
            root.localScale = Vector3.one * 0.55f;
            Type environment = config.GetType().Assembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true)!;
            float sourceScale = (float)environment.GetMethod("EvaluateSourceWorldScale", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { root });
            runtime.Invoke("ApplyWorldScaleParameters", sourceScale);
            for (int frame = 0; frame < 60; frame++)
            {
                runtime.Invoke("SetPrimitiveMotion", Vector3.zero, Vector3.zero);
                runtime.Invoke("AdvancePrimitiveSimulation", 1f / 120f);
            }

            Vector3[] homes = runtime.ReadHomes();
            Material material = (Material)runtime.Get("_simulationMaterial");
            StringBuilder trace = new("frame,seconds,site,x,y,z,homeTravel,integrationMilliseconds\n");
            float maximumTravel = 0f;
            float maximumStep = 0f;
            for (int frame = 0; frame < 105; frame++)
            {
                float seconds = (frame + 1) * frameSeconds;
                float motionSeconds = Mathf.Min(seconds, 0.5f);
                bool moving = seconds < 0.5f;
                root.position = Vector3.down * (3f * motionSeconds);
                root.rotation = Quaternion.AngleAxis(2f * motionSeconds * Mathf.Rad2Deg, Vector3.forward);
                runtime.Invoke("SetPrimitiveMotion", moving ? Vector3.down * 3f : Vector3.zero, moving ? Vector3.forward * 2f : Vector3.zero);
                runtime.Invoke("AdvancePrimitiveSimulation", frameSeconds);
                runtime.Invoke("RenderPrimitiveDeformation");
                float stepMilliseconds = material.GetFloat("_DeltaTimeMilliseconds");
                maximumStep = Mathf.Max(maximumStep, stepMilliseconds);
                Require(stepMilliseconds <= 8.334f, $"{shape}: bounded work must not create an oversized SPH integration step");
                if (frame % 7 != 0 && frame != 104)
                    continue;

                Vector3[] sites = runtime.ReadSitePositions();
                for (int site = 0; site < sites.Length; site++)
                {
                    Vector3 p = sites[site];
                    Require(Finite(p.x) && Finite(p.y) && Finite(p.z), $"{shape}: non-finite particle at frame {frame}, site {site}");
                    float travel = Vector3.Distance(p, homes[site]);
                    maximumTravel = Mathf.Max(maximumTravel, travel);
                    Require(travel <= 0.501f, $"{shape}: particle left its existing half-local-unit home bound");
                    trace.AppendFormat(CultureInfo.InvariantCulture, "{0},{1:F6},{2},{3:F7},{4:F7},{5:F7},{6:F7},{7:F6}\n", frame, seconds, site, p.x, p.y, p.z, travel, stepMilliseconds);
                }

                foreach (Color spring in FerrofluidValidationRuntime.Read((Texture)runtime.Get("_springCurrent")))
                    Require(Finite(spring.r) && Finite(spring.g) && Finite(spring.b) && Finite(spring.a), $"{shape}: non-finite spike spring at frame {frame}");
            }

            File.WriteAllText(Path.Combine(directory, shape + ".csv"), trace.ToString());
            Require(maximumStep > 8f, $"{shape}: the low-cadence test must actually exercise the stretched default integration step");
            Debug.Log($"[FerrofluidDefaultCadenceValidation] {shape}: maximumTravel={maximumTravel:F6} local, maximumIntegration={maximumStep:F6} ms");
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
