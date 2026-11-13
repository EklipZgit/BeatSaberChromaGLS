using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidPressureCohesionValidation
    {
        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidPressureCohesionValidation));

        private static void RunRuntime()
        {
            List<string> failures = new();
            foreach (string shape in new[] { "RoundedCube", "Sphere" })
            {
                using FerrofluidValidationRuntime runtime = new(shape, FerrofluidValidationRuntime.CreateConfig());
                Material material = (Material)runtime.Get("_simulationMaterial");
                Texture rest = (Texture)runtime.Get("_restPositionTexture");
                Color[] homes = FerrofluidValidationRuntime.Read(rest);
                runtime.WriteState(FerrofluidValidationRuntime.CreatePositionState(homes));
                Color[] restingPressure = Pressure();
                float minimumDensity = float.MaxValue;
                float maximumPressure = 0f;
                for (int index = 0; index < runtime.Count; index++)
                {
                    minimumDensity = Mathf.Min(minimumDensity, restingPressure[index].r);
                    maximumPressure = Mathf.Max(maximumPressure, Mathf.Abs(restingPressure[index].g));
                }

                float selfDensity = material.GetFloat("_Mass") * material.GetFloat("_Poly6")
                    * Mathf.Pow(material.GetFloat("_HSQ"), 3f);
                Debug.Log($"[FerrofluidPressureCohesionValidation] {shape} selfDensity={selfDensity:F6} globalRestDensity={material.GetFloat("_RestDensity"):F6} minBakedDensity={minimumDensity:F6} maxRestPressure={maximumPressure:F6}");
                if (maximumPressure > 0.05f)
                    failures.Add($"{shape}: immutable resting sites retain pressure instead of using their baked density reference.");

                Pair(0.11f, true);
                Pair(0.025f, false);

                Color[] Pressure()
                {
                    runtime.ReadSitePositions();
                    RenderTexture density = (RenderTexture)runtime.Get("_densityPressure");
                    Graphics.Blit(Texture2D.blackTexture, density, material, 0);
                    material.SetTexture("_DensityPressureTex", density);
                    return FerrofluidValidationRuntime.Read(density);
                }

                void Pair(float offset, bool stretched)
                {
                    Color[] state = new Color[homes.Length];
                    state[0] = new Color(-offset, 0f, 0f, 0f);
                    state[1] = new Color(offset, 0f, 0f, 0f);
                    runtime.WriteState(state);
                    material.SetInt("_ParticleCount", 2);
                    Vector3[] positions = runtime.ReadSitePositions();
                    Color[] surface = FerrofluidValidationRuntime.Read((Texture)runtime.Get("_siteSurface"));
                    float spacing = (surface[0].a + surface[1].a) * 0.5f;
                    float kernelRadius = Vector3.Distance(positions[0], positions[1])
                        * material.GetFloat("_PrimitivePressureSpacing") / spacing;
                    if (kernelRadius <= 0f || kernelRadius >= material.GetFloat("_H"))
                        throw new Exception($"{shape}: the pressure fixture's pair is outside its kernel ({kernelRadius:F6}).");
                    float restRadius = material.GetFloat("_PrimitivePressureSpacing");
                    if (stretched
                        ? kernelRadius <= restRadius
                        : kernelRadius >= restRadius)
                        throw new Exception($"{shape}: the pressure fixture does not match its stated spacing.");
                    Color[] pressure = Pressure();
                    RenderTexture force = (RenderTexture)runtime.Get("_force");
                    Graphics.Blit(Texture2D.blackTexture, force, material, 1);
                    Color[] forces = FerrofluidValidationRuntime.Read(force);
                    Debug.Log($"[FerrofluidPressureCohesionValidation] {shape} stretched={stretched} kernelRadius={kernelRadius:F6} spacing={spacing:F6} density={pressure[0].r:F6}/{pressure[1].r:F6} reference={homes[0].b:F6}/{homes[1].b:F6} pressure={pressure[0].g:F6}/{pressure[1].g:F6} forceX={forces[0].r:F6}/{forces[1].r:F6}");
                    if (stretched)
                    {
                        if (pressure[0].r >= homes[0].b - 0.2f || pressure[1].r >= homes[1].b - 0.2f)
                            throw new Exception($"{shape}: the pressure fixture did not produce an under-dense neighborhood.");
                        if (pressure[0].g >= 0f || pressure[1].g >= 0f || forces[0].r <= 0f || forces[1].r >= 0f)
                            failures.Add($"{shape}: under-dense stretched neighbors do not attract each other.");
                    }
                    else if (forces[0].r > 0.00001f || forces[1].r < -0.00001f)
                    {
                        failures.Add($"{shape}: density attraction pulls already compressed neighbors further together.");
                    }
                }
            }

            if (failures.Count > 0)
                throw new Exception(string.Join(" | ", failures));
            Debug.Log("[FerrofluidPressureCohesionValidation] PASS: sparse neighborhoods attract without collapsing compressed pairs.");
        }
    }
}
