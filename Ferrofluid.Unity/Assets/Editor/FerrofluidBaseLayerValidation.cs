using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidBaseLayerValidation
    {
        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidBaseLayerValidation));

        private static void RunRuntime()
        {
            string directory = FerrofluidValidationArtifacts.CreateRunDirectory("base-layer");
            Debug.Log($"[FerrofluidBaseLayerValidation] captureDirectory={directory}");
            List<string> failures = new();
            foreach (string shape in new[] { "RoundedCube", "Sphere" })
                Validate(shape, directory, failures);

            if (failures.Count != 0)
                throw new Exception(string.Join(" | ", failures));

            Debug.Log("[FerrofluidBaseLayerValidation] PASS: sparse fluid thins, crowded fluid bulges, and trigger amplification stays separate.");
        }

        private static void Validate(string shapeName, string directory, List<string> failures)
        {
            using FerrofluidValidationRuntime runtime = new(shapeName, FerrofluidValidationRuntime.CreateConfig());
            runtime.Invoke(
                "UpdatePrimitiveFrame",
                1f,
                new Vector3(0f, 0f, 1000f),
                Vector3.zero,
                0f,
                new Vector3(0f, 0f, -1000f),
                0f,
                Vector3.zero,
                Vector3.zero,
                1f);
            runtime.Invoke("CalculateParticleForces");
            runtime.Invoke("RenderHeightMap", 8f);
            Material material = (Material)runtime.Get("_surfaceMaterial");
            material.SetFloat("_SpikesEnabled", 0f);
            Vector3[] homes = runtime.ReadHomes();
            float spacing = FerrofluidSurfaceDistribution.MeanSpacing(homes);
            Type environment = runtime.Simulation.GetType().Assembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true)!;
            MethodInfo encode = environment.GetMethod("EvaluateSurfaceUv", BindingFlags.Static | BindingFlags.NonPublic)!;
            Vector4 yaw = ((Material)runtime.Get("_simulationMaterial")).GetVector("_PrimitiveYawCosSin");
            float degrees = Mathf.Atan2(yaw.y, yaw.x) * Mathf.Rad2Deg;
            Vector2[] uv = new Vector2[homes.Length];
            for (int index = 0; index < homes.Length; index++)
                uv[index] = (Vector2)encode.Invoke(null, new object[] { homes[index], degrees })!;

            Texture immutableHomes = (Texture)runtime.Get("_restPositionTexture");
            Color[] originalHomes = FerrofluidValidationRuntime.Read(immutableHomes);
            RenderTexture density = (RenderTexture)runtime.Get("_densityPressure");
            Color[] reference = FerrofluidValidationRuntime.Read(density);
            SetSpringHeight(0.44f);
            Capture(1f, out Color[] resting, out Color[] restingFilm);
            Capture(0.5f, out Color[] low, out Color[] lowFilm);
            Capture(1.5f, out Color[] high, out Color[] highFilm);
            material.SetFloat("_MagnetHeightAmplification", 1.6f);
            SetSpringHeight(0.96f);
            Capture(1.5f, out Color[] amplified, out Color[] amplifiedFilm);
            Capture(10f, out Color[] saturated, out _);
            double thinning = 0.0;
            double bulging = 0.0;
            double filmGrowth = 0.0;
            float maximumAmplificationError = 0f;
            float maximumFilmError = 0f;
            float maximumHeight = 0f;
            using StreamWriter writer = new(Path.Combine(directory, shapeName + ".csv"));
            writer.WriteLine("site,baselineX,baselineY,baselineZ,lowX,lowY,lowZ,highX,highY,highZ,lowFilm,restFilm,highFilm");
            for (int index = 0; index < homes.Length; index++)
            {
                Vector3 normal = new(restingFilm[index].r, restingFilm[index].g, restingFilm[index].b);
                Vector3 baseline = Point(resting[index]);
                thinning += Vector3.Dot(Point(low[index]) - baseline, normal);
                bulging += Vector3.Dot(Point(high[index]) - baseline, normal);
                filmGrowth += highFilm[index].a - restingFilm[index].a;
                maximumAmplificationError = Mathf.Max(maximumAmplificationError, Vector3.Distance(Point(high[index]), Point(amplified[index])));
                maximumFilmError = Mathf.Max(maximumFilmError, Mathf.Abs(highFilm[index].a - amplifiedFilm[index].a));
                maximumHeight = Mathf.Max(maximumHeight, Vector3.Distance(Point(saturated[index]), baseline));
                writer.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1:R},{2:R},{3:R},{4:R},{5:R},{6:R},{7:R},{8:R},{9:R},{10:R},{11:R},{12:R}",
                    index,
                    baseline.x,
                    baseline.y,
                    baseline.z,
                    low[index].r,
                    low[index].g,
                    low[index].b,
                    high[index].r,
                    high[index].g,
                    high[index].b,
                    lowFilm[index].a,
                    restingFilm[index].a,
                    highFilm[index].a));
            }

            thinning /= homes.Length;
            bulging /= homes.Length;
            filmGrowth /= homes.Length;
            Debug.Log($"[FerrofluidBaseLayerValidation] {shapeName} meanThinning={thinning:F7} meanBulging={bulging:F7} filmGrowth={filmGrowth:F7} maxHeightInSpacings={maximumHeight / spacing:F5} triggerError={maximumAmplificationError:F7}");
            if (thinning >= -spacing * 0.02f || bulging <= spacing * 0.02f)
                failures.Add($"{shapeName}: density changes do not thin and bulge the actual base geometry");
            if (Math.Abs(thinning + bulging) > spacing * 0.005f)
                failures.Add($"{shapeName}: symmetric density changes introduce a systematic layer-height bias");
            if (filmGrowth < 0.0001)
                failures.Add($"{shapeName}: base-layer displacement is missing from film coordinates");
            if (maximumHeight > spacing * 0.2f || float.IsNaN(maximumHeight))
                failures.Add($"{shapeName}: extreme density produces an unbounded base-layer displacement");
            if (maximumAmplificationError > 0.000001f || maximumFilmError > 0.000001f)
                failures.Add($"{shapeName}: primary height amplification stretches the density-driven layer");

            ValidateNonuniformLayer();

            Color[] currentHomes = FerrofluidValidationRuntime.Read(immutableHomes);
            for (int index = 0; index < originalHomes.Length; index++)
            {
                if (!originalHomes[index].Equals(currentHomes[index]))
                    throw new Exception($"{shapeName}: density deformation changed immutable homes");
            }

            void SetSpringHeight(float height)
            {
                RenderTexture spring = (RenderTexture)runtime.Get("_springCurrent");
                Color[] pixels = FerrofluidValidationRuntime.Read(spring);
                for (int index = 0; index < homes.Length; index++)
                    pixels[index].r = height;

                Texture2D upload = new(spring.width, spring.height, TextureFormat.RGBAFloat, false, true);
                try
                {
                    upload.SetPixels(pixels);
                    upload.Apply();
                    Graphics.Blit(upload, spring);
                }
                finally
                {
                    Object.DestroyImmediate(upload);
                }
            }

            void ValidateNonuniformLayer()
            {
                List<Vector2> samples = new();
                for (int y = 0; y < 24; y++)
                {
                    for (int x = 0; x < 24; x++)
                        samples.Add(new Vector2((x + 0.5f) / 24f, (y + 0.5f) / 24f));
                }

                int gridCount = samples.Count;
                for (int edge = 0; edge < 4; edge++)
                {
                    for (int index = 1; index < 5; index++)
                    {
                        float t = index * 0.1f;
                        if (edge < 2)
                        {
                            samples.Add(new Vector2(edge, t));
                            samples.Add(new Vector2(edge, 1f - t));
                        }
                        else
                        {
                            samples.Add(new Vector2(t, edge - 2));
                            samples.Add(new Vector2(1f - t, edge - 2));
                        }
                    }
                }

                uv = samples.ToArray();
                Capture(1f, out _, out Color[] baseline);
                Capture(1f, out Color[] displaced, out Color[] normals, true);
                float normalChange = 0f;
                for (int index = 0; index < gridCount; index++)
                    normalChange = Mathf.Max(normalChange, Vector3.Angle(Point(baseline[index]), Point(normals[index])));

                float seamGap = 0f;
                float seamNormal = 0f;
                float seamFilm = 0f;
                for (int index = gridCount; index < uv.Length; index += 2)
                {
                    seamGap = Mathf.Max(seamGap, Vector3.Distance(Point(displaced[index]), Point(displaced[index + 1])));
                    seamNormal = Mathf.Max(seamNormal, Vector3.Angle(Point(normals[index]), Point(normals[index + 1])));
                    seamFilm = Mathf.Max(seamFilm, Mathf.Abs(normals[index].a - normals[index + 1].a));
                }

                using StreamWriter output = new(Path.Combine(directory, shapeName + "-nonuniform.csv"));
                output.WriteLine("u,v,px,py,pz,nx,ny,nz,film");
                for (int index = 0; index < uv.Length; index++)
                {
                    output.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:R},{1:R},{2:R},{3:R},{4:R},{5:R},{6:R},{7:R},{8:R}",
                        uv[index].x,
                        uv[index].y,
                        displaced[index].r,
                        displaced[index].g,
                        displaced[index].b,
                        normals[index].r,
                        normals[index].g,
                        normals[index].b,
                        normals[index].a));
                }

                Debug.Log($"[FerrofluidBaseLayerValidation] {shapeName} nonuniformNormalChange={normalChange:F5} seamGap={seamGap:F7} seamNormal={seamNormal:F5} seamFilm={seamFilm:F7}");
                if (normalChange < 0.1f)
                    failures.Add($"{shapeName}: nonuniform layer geometry does not affect the rendered normals");
                if (seamGap > 0.0001f || seamNormal > 0.1f || seamFilm > 0.00001f)
                    failures.Add($"{shapeName}: nonuniform layer breaks geometry, normal, or film continuity across the chart edges");
            }

            void Capture(float ratio, out Color[] positions, out Color[] film, bool varied = false)
            {
                Texture2D upload = new(density.width, density.height, TextureFormat.RGBAFloat, false, true);
                try
                {
                    Color[] pixels = (Color[])reference.Clone();
                    for (int index = 0; index < homes.Length; index++)
                        pixels[index].r *= ratio * (varied ? 1f + homes[index].x : 1f);

                    upload.SetPixels(pixels);
                    upload.Apply();
                    Graphics.Blit(upload, density);
                    runtime.Invoke("RenderHeightMap", 0f);
                    runtime.Invoke("RenderPrimitiveDeformation");
                    positions = FerrofluidSurfaceProbe.Sample(material, uv, 0);
                    film = FerrofluidSurfaceProbe.Sample(material, uv, 1);
                    for (int index = 0; index < positions.Length; index++)
                    {
                        if (float.IsNaN(positions[index].r) || float.IsNaN(film[index].a) || positions[index].a < 0.5f)
                            throw new Exception($"{shapeName}: base-layer probe did not produce finite rendered values");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(upload);
                }
            }
        }

        private static Vector3 Point(Color value) => new(value.r, value.g, value.b);
    }
}
