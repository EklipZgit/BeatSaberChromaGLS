using System;
using System.Reflection;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidHomeTuningValidation
    {
        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidHomeTuningValidation));

        private static void RunRuntime()
        {
            foreach (string shape in new[] { "RoundedCube", "Sphere" })
            {
                ValidateDefaultMotion(shape);
                Validate(shape);
            }

            Debug.Log("[FerrofluidHomeTuningValidation] PASS: live home controls change surface motion without rebaking homes.");
        }

        private static void ValidateDefaultMotion(string shape)
        {
            using FerrofluidValidationRuntime runtime = new(shape, FerrofluidValidationRuntime.CreateConfig());
            Texture rest = (Texture)runtime.Get("_restPositionTexture");
            Color[] original = FerrofluidValidationRuntime.Read(rest);
            for (int frame = 0; frame < 170; frame++)
                Step(Vector3.down * 6f);
            Vector3[] baseline = runtime.ReadSitePositions();
            Vector3[] homes = runtime.ReadHomes();
            float peak = 0f;
            float peakMean = 0f;
            float bound = 0f;
            for (int frame = 0; frame < 170; frame++)
            {
                Step(Vector3.zero);
                Vector3[] sites = runtime.ReadSitePositions();
                float total = 0f;
                for (int index = 0; index < sites.Length; index++)
                {
                    float distance = Vector3.Distance(sites[index], baseline[index]);
                    if (!float.IsFinite(distance))
                        throw new Exception($"{shape}: default motion produced a non-finite site.");
                    peak = Mathf.Max(peak, distance);
                    total += distance;
                    bound = Mathf.Max(bound, Vector3.Distance(sites[index], homes[index]));
                }

                peakMean = Mathf.Max(peakMean, total / sites.Length);
            }

            for (int frame = 0; frame < 850; frame++)
                Step(Vector3.zero);
            Vector3[] recovered = runtime.ReadSitePositions();
            float remaining = 0f;
            for (int index = 0; index < recovered.Length; index++)
                remaining = Mathf.Max(remaining, Vector3.Distance(recovered[index], baseline[index]));
            Color[] after = FerrofluidValidationRuntime.Read(rest);
            for (int index = 0; index < original.Length; index++)
            {
                if (original[index] != after[index])
                    throw new Exception($"{shape}: default motion changed an immutable home.");
            }

            Debug.Log($"[FerrofluidHomeTuningValidation] {shape} defaultImpact peak={peak:F6} peakMean={peakMean:F6} homeTravel={bound:F6} afterRecovery={remaining:F6}");
            if (peak < 0.08f || peakMean < 0.025f)
                throw new Exception($"{shape}: default home forces still pin the surface under a six-metre-per-second stop.");
            if (bound > 0.5001f || remaining > (peak * 0.25f) + 0.005f)
                throw new Exception($"{shape}: default motion does not stay bounded and elastically recover.");

            void Step(Vector3 velocity)
            {
                runtime.Invoke(
                    "UpdatePrimitiveFrame",
                    1f,
                    new Vector3(0f, 0f, -1000f),
                    Vector3.zero,
                    0f,
                    new Vector3(0f, 0f, -1000f),
                    0f,
                    velocity,
                    Vector3.zero,
                    1f);
                runtime.Invoke("AdvancePrimitiveSimulation", 1f / 170f);
                runtime.Invoke("RenderPrimitiveDeformation");
            }
        }

        private static void Validate(string shape)
        {
            object config = FerrofluidValidationRuntime.CreateConfig();
            foreach (string name in new[]
            {
                "FerrofluidHomeElasticityGain", "FerrofluidHomeQuadraticPower",
                "FerrofluidHomeSoftRange", "FerrofluidHomeTravelLimit",
                "FerrofluidHomeDampingGain",
            })
            {
                PropertyInfo property = config.GetType().GetProperty(name);
                if (property == null)
                    throw new Exception($"The session-only home control {name} is unavailable.");
            }

            using FerrofluidValidationRuntime runtime = new(shape, config);
            Texture rest = (Texture)runtime.Get("_restPositionTexture");
            Color[] original = FerrofluidValidationRuntime.Read(rest);
            Vector3[] homes = runtime.ReadHomes();
            Material material = (Material)runtime.Get("_simulationMaterial");
            float baseline = Probe(1f, 16f, 0.04f, 0.5f, 6.8f);
            float free = Probe(0f, 16f, 0.04f, 0.5f, 0f);
            float gentle = Probe(1f, 0f, 0.04f, 0.5f, 0f);
            float wide = Probe(1f, 16f, 0.9f, 0.5f, 0f);
            float limited = Probe(0f, 16f, 0.04f, 0.18f, 0f);
            float damped = Probe(0f, 16f, 0.04f, 0.5f, 27.2f);
            Debug.Log($"[FerrofluidHomeTuningValidation] {shape} travel baseline={baseline:F6} free={free:F6} gentle={gentle:F6} wide={wide:F6} limited={limited:F6} damped={damped:F6}");
            if (free < 0.2f || free < baseline * 2f)
                throw new Exception($"{shape}: reducing home strength does not allow visible surface travel.");
            if (gentle < baseline * 1.2f || wide < baseline * 1.2f)
                throw new Exception($"{shape}: the quadratic ramp and soft range do not change the restoring response.");
            if (limited > 0.1801f || limited < 0.12f || free > 0.5001f)
                throw new Exception($"{shape}: the configured physical home travel bound is not enforced.");
            if (damped > free * 0.75f)
                throw new Exception($"{shape}: surface damping does not reduce inertial travel.");

            Color[] after = FerrofluidValidationRuntime.Read(rest);
            for (int index = 0; index < original.Length; index++)
            {
                if (original[index] != after[index])
                    throw new Exception($"{shape}: tuning changed an immutable home or its baseline density.");
            }

            float Probe(float gain, float power, float softRange, float limit, float damping)
            {
                FerrofluidValidationRuntime.SetConfig(config, "FerrofluidHomeElasticityGain", gain);
                FerrofluidValidationRuntime.SetConfig(config, "FerrofluidHomeQuadraticPower", power);
                FerrofluidValidationRuntime.SetConfig(config, "FerrofluidHomeSoftRange", softRange);
                FerrofluidValidationRuntime.SetConfig(config, "FerrofluidHomeTravelLimit", limit);
                FerrofluidValidationRuntime.SetConfig(config, "FerrofluidHomeDampingGain", damping / 6.8f);
                runtime.Apply(config);
                if (!ReferenceEquals(rest, runtime.Get("_restPositionTexture")))
                    throw new Exception($"{shape}: a force-only setting rebuilt the home texture.");

                runtime.WriteState(FerrofluidValidationRuntime.CreatePositionState(original));
                material.SetFloat("_GasConstant", 0f);
                material.SetFloat("_Viscosity", 0f);
                material.SetFloat("_PointerStrength", 0f);
                material.SetFloat("_SecondaryMagneticFieldStrength", 0f);
                material.SetMatrix("_PrimitiveFrameRotation", Matrix4x4.identity);
                material.SetVector("_PrimitivePreviousAngularVelocity", Vector4.zero);
                material.SetVector("_PrimitiveAngularVelocity", Vector4.zero);
                float maximum = 0f;
                for (int step = 0; step < 60; step++)
                {
                    material.SetVector("_PrimitiveLinearVelocityDelta", step == 0 ? Vector3.down * 6f : Vector3.zero);
                    runtime.Invoke("Simulate", 8f);
                    Vector3[] sites = runtime.ReadSitePositions();
                    for (int index = 0; index < homes.Length; index++)
                    {
                        float distance = Vector3.Distance(homes[index], sites[index]);
                        if (float.IsNaN(distance) || float.IsInfinity(distance))
                            throw new Exception($"{shape}: tuning produced a non-finite site.");
                        maximum = Mathf.Max(maximum, distance);
                    }
                }

                return maximum;
            }
        }
    }
}
