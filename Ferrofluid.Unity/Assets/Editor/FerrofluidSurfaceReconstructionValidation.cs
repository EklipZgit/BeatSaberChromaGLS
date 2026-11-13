using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // Check affine slopes and bounded per-tap displacement through the compiled surface shader.
    internal static class FerrofluidSurfaceReconstructionValidation
    {
        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidSurfaceReconstructionValidation));

        private static void RunRuntime()
        {
            var failures = new List<string>();
            string directory = FerrofluidValidationArtifacts.CreateRunDirectory("reconstruction");
            Debug.Log($"[FerrofluidSurfaceReconstructionValidation] captureDirectory={directory}");
            Material material = new(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader"));
            var owned = new List<Object> { material };
            try
            {
                material.EnableKeyword("FERROFLUID_CACHED_DEFORMATION");
                material.SetInt("_ParticleTextureSize", 1);
                material.SetInt("_ParticleCount", 1);
                material.SetFloat("_FadeAlpha", 1f);
                material.SetFloat("_Exposure", 1f);
                material.SetFloat("_SpikesEnabled", 1f);
                material.SetFloat("_FluidHeightScale", 1f);
                material.SetFloat("_MaximumHeight", 1f);
                material.SetFloat("_HeightFactor", 1f);
                material.SetFloat("_PrimitiveGlobalSpikeFlatten", 0f);
                material.SetFloat("_MagneticFieldStrength", 0f);
                material.SetFloat("_SecondaryMagneticFieldStrength", 0f);
                material.SetFloat("_SurfaceRoundness", 0.48f);

                ValidateAffineSlope(material, owned, directory, failures);
                ValidateAlternatingBounds(material, owned, directory, failures);
                ValidateSinusoidalNormals(material, owned, directory, failures);
                ValidateTorusCanvasNormals(material, owned, directory, failures);
                ValidateCapturedShoulder(material, owned, directory, failures);
            }
            finally
            {
                foreach (Object o in owned)
                    Object.DestroyImmediate(o);
            }

            if (failures.Count != 0)
                throw new Exception(string.Join(" | ", failures));

            Debug.Log("[FerrofluidSurfaceReconstructionValidation] PASS: displaced surface reproduces the applied spike field.");
        }

        // Affine field H(u,v) = 0.2 + 0.15u + 0.1v through a 256 texel height map. The
        // reconstructed offset must equal the analytic axis*H*spring and its secant slope must
        // track the true 0.15/0.1 gradient between neighbouring knots.
        private static void ValidateAffineSlope(Material material, List<Object> owned, string directory, List<string> failures)
        {
            const int size = 256;
            Texture2D heightMap = Fill(size, size, (x, y) =>
            {
                float h = 0.2f + 0.15f * ((x + 0.5f) / size) + 0.1f * ((y + 0.5f) / size);
                return new Color(h, 0f, 0f, h);
            });
            owned.Add(heightMap);
            material.SetTexture("_HeightMapTex", heightMap);
            BindConstant(material, owned, "_SiteDeformationTex", new Color(0f, 1f, 0f, 0f));
            BindConstant(material, owned, "_SpringStateTex", new Color(1f, 0f, 0f, 0f));
            BindConstant(material, owned, "_StateTex", Color.clear);
            BindConstant(material, owned, "_SiteSurfaceTex", Color.clear);
            BindConstant(material, owned, "_RestSurfaceTex", Color.clear);
            BindConstant(material, owned, "_RestPositionTex", Color.clear);
            BindConstant(material, owned, "_SiteBulgeTex", Color.clear);
            BindConstant(material, owned, "_DensityPressureTex", Color.clear);
            BindConstant(material, owned, "_EnvironmentTex", Color.black);

            const float delta = 1f / (256f * 32f);
            const float u = 153.5f / 256f;
            const float v = 107.5f / 256f;
            float exponent = Mathf.Lerp(8f, 2f, 0.48f);
            var uvs = new[]
            {
                new Vector2(u, v),
                new Vector2(u - delta, v), new Vector2(u + delta, v),
                new Vector2(u, v - delta), new Vector2(u, v + delta),
                new Vector2(u + 1f / size, v),
                new Vector2(u, v + 1f / size),
            };

            for (int shape = 1; shape <= 2; shape++)
            {
                string shapeName = shape == 1 ? "RoundedCube" : "Sphere";
                material.SetFloat("_SurfaceShapeMode", shape);
                foreach (float yawDeg in new[] { 0f, 45f })
                {
                    float radians = yawDeg * Mathf.Deg2Rad;
                    Vector2 yawCosSin = new(Mathf.Cos(radians), Mathf.Sin(radians));
                    material.SetVector("_PrimitiveYawCosSin", yawCosSin);
                    Color[] positions = FerrofluidSurfaceProbe.Sample(material, uvs, 0);
                    var offsets = new Vector3[uvs.Length];
                    bool valid = true;
                    using (StreamWriter csv = new(Path.Combine(directory, $"affine-{shapeName}-yaw{yawDeg}.csv")))
                    {
                        csv.WriteLine("u,v,offx,offy,offz,expected");
                        for (int i = 0; i < uvs.Length; i++)
                        {
                            Color pc = positions[i];
                            if (!float.IsFinite(pc.r) || !float.IsFinite(pc.g) || !float.IsFinite(pc.b) || !float.IsFinite(pc.a) || pc.a < 0.5f)
                            {
                                failures.Add($"{shapeName} yaw{yawDeg} uv{uvs[i]}: non-finite or undrawn position.");
                                valid = false;
                                break;
                            }

                            offsets[i] = new Vector3(pc.r, pc.g, pc.b) - OraclePosition(uvs[i], yawCosSin, shape, exponent);
                            float expected = 0.2f + 0.15f * uvs[i].x + 0.1f * uvs[i].y;
                            csv.WriteLine($"{uvs[i].x},{uvs[i].y},{offsets[i].x},{offsets[i].y},{offsets[i].z},{expected}");
                            Vector3 error = offsets[i] - new Vector3(0f, expected, 0f);
                            if (error.magnitude > 0.00002f)
                                failures.Add($"{shapeName} yaw{yawDeg} uv{uvs[i]}: offset error {error.magnitude:R} exceeds 2e-5.");
                        }
                    }

                    if (valid)
                    {
                        float slopeU = (offsets[2].y - offsets[1].y) / (2f * delta);
                        float slopeV = (offsets[4].y - offsets[3].y) / (2f * delta);
                        if (Mathf.Abs(slopeU - 0.15f) > 0.005f)
                            failures.Add($"{shapeName} yaw{yawDeg}: reconstructed u slope {slopeU:R} deviates from 0.15 by more than 0.005.");
                        if (Mathf.Abs(slopeV - 0.1f) > 0.005f)
                            failures.Add($"{shapeName} yaw{yawDeg}: reconstructed v slope {slopeV:R} deviates from 0.1 by more than 0.005.");
                    }
                }
            }
        }

        // Alternating texel columns: A carries height 0.38 at spring 0.3, B carries height 0 at
        // spring 1, both along the same upward axis. Every reconstructed displacement must stay
        // within the largest individual contribution (0.38*0.3 = 0.114); the ease blend reaches
        // 0.1235 between texels.
        private static void ValidateAlternatingBounds(Material material, List<Object> owned, string directory, List<string> failures)
        {
            material.SetInt("_ParticleTextureSize", 2);
            material.SetInt("_ParticleCount", 2);
            Texture2D heightMap = Fill(2, 2, (x, _) => x == 0 ? new Color(0.38f, 0f, 0f, 0.38f) : new Color(0f, 1f, 0f, 0f));
            owned.Add(heightMap);
            material.SetTexture("_HeightMapTex", heightMap);
            Texture2D spring = Fill(2, 2, (x, _) => x == 0 ? new Color(0.3f, 0f, 0f, 0f) : new Color(1f, 0f, 0f, 0f));
            owned.Add(spring);
            material.SetTexture("_SpringStateTex", spring);

            foreach (Vector3 axisB in new[] { Vector3.up, new Vector3(0.3f, 0.9f, 0.2f).normalized })
            {
                Texture2D deformation = Fill(2, 2, (x, _) => x == 0 ? new Color(0f, 1f, 0f, 0f) : new Color(axisB.x, axisB.y, axisB.z, 0f));
                owned.Add(deformation);
                material.SetTexture("_SiteDeformationTex", deformation);
                BindConstant(material, owned, "_StateTex", Color.clear);
                BindConstant(material, owned, "_SiteSurfaceTex", Color.clear);
                BindConstant(material, owned, "_RestSurfaceTex", Color.clear);
                BindConstant(material, owned, "_RestPositionTex", Color.clear);
                BindConstant(material, owned, "_SiteBulgeTex", Color.clear);
                BindConstant(material, owned, "_DensityPressureTex", Color.clear);
                BindConstant(material, owned, "_EnvironmentTex", Color.black);

                var uvs = new List<Vector2>();
                for (float t = 0.05f; t < 1f; t += 0.1f)
                    uvs.Add(new Vector2(t, 0.5f));

                material.SetVector("_PrimitiveYawCosSin", new Vector2(1f, 0f));
                float exponent = Mathf.Lerp(8f, 2f, 0.48f);
                material.SetFloat("_SurfaceShapeMode", 1f);
                Color[] positions = FerrofluidSurfaceProbe.Sample(material, uvs.ToArray(), 0);
                for (int i = 0; i < uvs.Count; i++)
                {
                    Color pc = positions[i];
                    if (!float.IsFinite(pc.r) || !float.IsFinite(pc.g) || !float.IsFinite(pc.b) || !float.IsFinite(pc.a) || pc.a < 0.5f)
                    {
                        failures.Add($"alternating axis{axisB} uv{uvs[i]}: non-finite or undrawn position.");
                        continue;
                    }

                    Vector3 offset = new Vector3(pc.r, pc.g, pc.b) - OraclePosition(uvs[i], new Vector2(1f, 0f), 1, exponent);
                    if (offset.magnitude > 0.114f + 0.00001f)
                        failures.Add($"alternating axis{axisB} uv{uvs[i]}: displacement {offset.magnitude:R} exceeds bound 0.114.");
                }

                Object.DestroyImmediate(deformation);
                owned.Remove(deformation);
            }

            material.SetInt("_ParticleTextureSize", 1);
            material.SetInt("_ParticleCount", 1);
        }

        // A smooth sinusoidal profile (period 8 texels, amplitude 0.03 + 0.1 base) must reproduce
        // the geometric slope: the GPU normal is compared against the cross of near actual
        // position probes, oriented by the base winding.
        private static void ValidateSinusoidalNormals(Material material, List<Object> owned, string directory, List<string> failures)
        {
            const int size = 256;
            const float period = 8f;
            Texture2D heightMap = Fill(size, size, (x, _) =>
            {
                float h = 0.1f + 0.03f * Mathf.Sin((2f * Mathf.PI * x) / period);
                return new Color(h, 0f, 0f, h);
            });
            owned.Add(heightMap);
            material.SetTexture("_HeightMapTex", heightMap);
            BindConstant(material, owned, "_SiteDeformationTex", new Color(0f, 1f, 0f, 0f));
            BindConstant(material, owned, "_SpringStateTex", new Color(1f, 0f, 0f, 0f));
            BindConstant(material, owned, "_StateTex", Color.clear);
            BindConstant(material, owned, "_SiteSurfaceTex", Color.clear);
            BindConstant(material, owned, "_RestSurfaceTex", Color.clear);
            BindConstant(material, owned, "_RestPositionTex", Color.clear);
            BindConstant(material, owned, "_SiteBulgeTex", Color.clear);
            BindConstant(material, owned, "_DensityPressureTex", Color.clear);
            BindConstant(material, owned, "_EnvironmentTex", Color.black);

            const float delta = 1f / (256f * 32f);
            float exponent = Mathf.Lerp(8f, 2f, 0.48f);
            var uvs = new List<Vector2>();
            for (float u = 0.15f; u < 0.9f; u += 0.15f)
            {
                for (float v = 0.15f; v < 0.9f; v += 0.15f)
                    uvs.Add(new Vector2(u, v));
            }

            for (int shape = 1; shape <= 2; shape++)
            {
                string shapeName = shape == 1 ? "RoundedCube" : "Sphere";
                material.SetFloat("_SurfaceShapeMode", shape);
                material.SetVector("_PrimitiveYawCosSin", new Vector2(1f, 0f));
                Color[] normals = FerrofluidSurfaceProbe.Sample(material, uvs.ToArray(), 1);
                var probeUvs = new List<Vector2>();
                foreach (Vector2 uv in uvs)
                {
                    probeUvs.Add(uv - new Vector2(delta, 0f));
                    probeUvs.Add(uv + new Vector2(delta, 0f));
                    probeUvs.Add(uv - new Vector2(0f, delta));
                    probeUvs.Add(uv + new Vector2(0f, delta));
                }

                Color[] positions = FerrofluidSurfaceProbe.Sample(material, probeUvs.ToArray(), 0);
                using StreamWriter csv = new(Path.Combine(directory, $"sinusoidal-{shapeName}.csv"));
                csv.WriteLine("u,v,gpuNx,gpuNy,gpuNz,cpuNx,cpuNy,cpuNz,error");
                for (int i = 0; i < uvs.Count; i++)
                {
                    Vector2 uv = uvs[i];
                    Color nc = normals[i];
                    if (!float.IsFinite(nc.r) || !float.IsFinite(nc.g) || !float.IsFinite(nc.b) || !float.IsFinite(nc.a) || nc.a < 0f || nc.a > 1f)
                    {
                        failures.Add($"{shapeName} uv{uv}: non-finite or out-of-range normal.");
                        continue;
                    }

                    Vector3 gpuNormal = new(nc.r, nc.g, nc.b);
                    if (Mathf.Abs(gpuNormal.magnitude - 1f) > 0.0002f)
                    {
                        failures.Add($"{shapeName} uv{uv}: GPU normal length {gpuNormal.magnitude:R} deviates from unit by more than 0.0002.");
                        continue;
                    }

                    bool drawn = true;
                    for (int k = 0; k < 4; k++)
                    {
                        Color pc = positions[(i * 4) + k];
                        if (!float.IsFinite(pc.r) || !float.IsFinite(pc.g) || !float.IsFinite(pc.b) || !float.IsFinite(pc.a) || pc.a < 0.5f)
                            drawn = false;
                    }

                    if (!drawn)
                    {
                        failures.Add($"{shapeName} uv{uv}: non-finite or undrawn position probe.");
                        continue;
                    }

                    Vector3 du = new(positions[(i * 4) + 1].r - positions[i * 4].r, positions[(i * 4) + 1].g - positions[i * 4].g, positions[(i * 4) + 1].b - positions[i * 4].b);
                    Vector3 dv = new(positions[(i * 4) + 3].r - positions[(i * 4) + 2].r, positions[(i * 4) + 3].g - positions[(i * 4) + 2].g, positions[(i * 4) + 3].b - positions[(i * 4) + 2].b);
                    Vector3 cpuNormal = Vector3.Cross(du, dv);
                    if (cpuNormal.magnitude < 0.000000001f)
                    {
                        failures.Add($"{shapeName} uv{uv}: degenerate position cross.");
                        continue;
                    }

                    Vector3 bdu = OraclePosition(uv + new Vector2(delta, 0f), new Vector2(1f, 0f), shape, exponent) - OraclePosition(uv - new Vector2(delta, 0f), new Vector2(1f, 0f), shape, exponent);
                    Vector3 bdv = OraclePosition(uv + new Vector2(0f, delta), new Vector2(1f, 0f), shape, exponent) - OraclePosition(uv - new Vector2(0f, delta), new Vector2(1f, 0f), shape, exponent);
                    Vector3 baseNormal = shape == 2 ? SphereDirection(uv, new Vector2(1f, 0f)) : CubeNormal(OraclePosition(uv, new Vector2(1f, 0f), shape, exponent), exponent);
                    float winding = Vector3.Dot(Vector3.Cross(bdu, bdv), baseNormal) >= 0f ? 1f : -1f;
                    cpuNormal = (cpuNormal / cpuNormal.magnitude) * winding;
                    float error = Vector3.Distance(gpuNormal, cpuNormal);
                    csv.WriteLine(FormattableString.Invariant($"{uv.x},{uv.y},{gpuNormal.x},{gpuNormal.y},{gpuNormal.z},{cpuNormal.x},{cpuNormal.y},{cpuNormal.z},{error}"));
                    if (error > 0.02f)
                        failures.Add($"{shapeName} uv{uv}: normal error {error:R} exceeds 0.02.");
                }
            }
        }

        // Curved-canvas torus: zero displacement isolates the base tangent path, so the GPU normal
        // must still follow the bent chart. Unsigned comparison only isolates the bending Jacobian;
        // the orientation contract is covered by the primitive winding tests.
        private static void ValidateTorusCanvasNormals(Material material, List<Object> owned, string directory, List<string> failures)
        {
            material.SetFloat("_SurfaceShapeMode", 0f);
            material.SetFloat("_SpikesEnabled", 0f);
            material.SetVector("_RectCenter", new Vector2(0f, 0f));
            material.SetVector("_HalfExtents", new Vector2(1f, 0.5f));
            material.SetFloat("_CornerRadius", 0.2f);
            material.SetFloat("_RadialCenter", 0f);
            material.SetFloat("_RadialRadius", 0.08f);
            material.SetFloat("_DepthRadius", 0.08f);
            material.SetFloat("_ViewerLocalZSign", 1f);
            material.SetFloat("_SourceLocalScale", 1f);
            material.SetFloat("_RectMinY", -0.5f);
            material.SetVector("_RectSize", new Vector2(2f, 1f));
            material.SetFloat("_Skew", 0.3f);
            material.SetFloat("_PivotY", 0.5f);
            material.SetFloat("_HasCurvedCanvas", 1f);
            material.SetFloat("_CurvedCanvasRadius", 2f);
            material.SetMatrix("_LocalToCurvedCanvas", Matrix4x4.identity);
            material.SetMatrix("_CurvedCanvasToLocal", Matrix4x4.identity);
            BindConstant(material, owned, "_StateTex", Color.clear);
            BindConstant(material, owned, "_SiteSurfaceTex", Color.clear);
            BindConstant(material, owned, "_RestSurfaceTex", Color.clear);
            BindConstant(material, owned, "_RestPositionTex", Color.clear);
            BindConstant(material, owned, "_SiteBulgeTex", Color.clear);
            BindConstant(material, owned, "_SiteDeformationTex", new Color(0f, 1f, 0f, 0f));
            BindConstant(material, owned, "_DensityPressureTex", Color.clear);
            BindConstant(material, owned, "_SpringStateTex", new Color(1f, 0f, 0f, 0f));
            BindConstant(material, owned, "_EnvironmentTex", Color.black);
            Texture2D heightMap = Fill(256, 256, (_, _) => Color.clear);
            owned.Add(heightMap);
            material.SetTexture("_HeightMapTex", heightMap);

            const float delta = 1f / (256f * 32f);
            var uvs = new[] { new Vector2(0.13f, 0.17f), new Vector2(0.37f, 0.31f), new Vector2(0.61f, 0.57f), new Vector2(0.83f, 0.73f) };
            Color[] normals = FerrofluidSurfaceProbe.Sample(material, uvs, 1);
            var probeUvs = new List<Vector2>();
            foreach (Vector2 uv in uvs)
            {
                probeUvs.Add(uv - new Vector2(delta, 0f));
                probeUvs.Add(uv + new Vector2(delta, 0f));
                probeUvs.Add(uv - new Vector2(0f, delta));
                probeUvs.Add(uv + new Vector2(0f, delta));
            }

            Color[] positions = FerrofluidSurfaceProbe.Sample(material, probeUvs.ToArray(), 0);
            using StreamWriter csv = new(Path.Combine(directory, "torus-canvas.csv"));
            csv.WriteLine("u,v,gpuNx,gpuNy,gpuNz,cpuNx,cpuNy,cpuNz,error");
            for (int i = 0; i < uvs.Length; i++)
            {
                Vector2 uv = uvs[i];
                Color nc = normals[i];
                if (!float.IsFinite(nc.r) || !float.IsFinite(nc.g) || !float.IsFinite(nc.b) || !float.IsFinite(nc.a) || nc.a < 0f || nc.a > 1f)
                {
                    failures.Add($"torus uv{uv}: non-finite or out-of-range normal.");
                    continue;
                }

                Vector3 gpuNormal = new(nc.r, nc.g, nc.b);
                if (Mathf.Abs(gpuNormal.magnitude - 1f) > 0.0002f)
                {
                    failures.Add($"torus uv{uv}: GPU normal length {gpuNormal.magnitude:R} deviates from unit by more than 0.0002.");
                    continue;
                }

                bool drawn = true;
                for (int k = 0; k < 4; k++)
                {
                    Color pc = positions[(i * 4) + k];
                    if (!float.IsFinite(pc.r) || !float.IsFinite(pc.g) || !float.IsFinite(pc.b) || !float.IsFinite(pc.a) || pc.a < 0.5f)
                        drawn = false;
                }

                if (!drawn)
                {
                    failures.Add($"torus uv{uv}: non-finite or undrawn position probe.");
                    continue;
                }

                Vector3 du = new(positions[(i * 4) + 1].r - positions[i * 4].r, positions[(i * 4) + 1].g - positions[i * 4].g, positions[(i * 4) + 1].b - positions[i * 4].b);
                Vector3 dv = new(positions[(i * 4) + 3].r - positions[(i * 4) + 2].r, positions[(i * 4) + 3].g - positions[(i * 4) + 2].g, positions[(i * 4) + 3].b - positions[(i * 4) + 2].b);
                Vector3 cpuNormal = Vector3.Cross(du, dv);
                if (cpuNormal.magnitude < 0.000000001f)
                {
                    failures.Add($"torus uv{uv}: degenerate position cross.");
                    continue;
                }

                cpuNormal /= cpuNormal.magnitude;
                float error = Mathf.Min(Vector3.Distance(gpuNormal, cpuNormal), Vector3.Distance(gpuNormal, -cpuNormal));
                csv.WriteLine(FormattableString.Invariant($"{uv.x},{uv.y},{gpuNormal.x},{gpuNormal.y},{gpuNormal.z},{cpuNormal.x},{cpuNormal.y},{cpuNormal.z},{error}"));
                if (error > 0.02f)
                    failures.Add($"torus uv{uv}: normal error {error:R} exceeds 0.02.");
            }

            material.SetFloat("_SurfaceShapeMode", 1f);
            material.SetFloat("_SpikesEnabled", 1f);
            material.SetFloat("_HasCurvedCanvas", 0f);
            material.SetFloat("_Skew", 0f);
        }

        [Serializable]
        private sealed class CapturedShoulderFixture
        {
            public float[] heightMap;
            public float[] deformation;
            public float[] springs;
            public float[] sampleUv;
            public float[] expectedOffset;
            public float effectiveHeightScale;
            public float expectedDensity;
            public int particleTextureSize;
            public int particleCount;
        }

        // An authentic four-by-four field from the user's captured snapshot at UV .668,.4. The
        // expected offset and density were computed independently from the same packet, so this
        // exercises the real per-tap composition path against captured node data.
        private static void ValidateCapturedShoulder(Material material, List<Object> owned, string directory, List<string> failures)
        {
            string fixturePath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../ChromaGLS.Tests/Fixtures/ferrofluid-captured-shoulder.json"));
            var fixture = JsonUtility.FromJson<CapturedShoulderFixture>(File.ReadAllText(fixturePath));

            material.SetInt("_ParticleTextureSize", fixture.particleTextureSize);
            material.SetInt("_ParticleCount", fixture.particleCount);
            material.SetFloat("_FluidHeightScale", fixture.effectiveHeightScale);
            material.SetFloat("_SpikesEnabled", 1f);
            material.SetFloat("_MagneticFieldStrength", 0f);
            material.SetFloat("_SecondaryMagneticFieldStrength", 0f);
            material.SetFloat("_PrimitiveGlobalSpikeFlatten", 0f);
            material.SetFloat("_SurfaceShapeMode", 1f);
            material.SetFloat("_HasCurvedCanvas", 0f);
            material.SetFloat("_Skew", 0f);
            material.SetFloat("_SurfaceRoundness", 0.48f);

            Texture2D heightMap = FillFlat(fixture.particleTextureSize, fixture.particleTextureSize, fixture.heightMap, FilterMode.Bilinear);
            owned.Add(heightMap);
            material.SetTexture("_HeightMapTex", heightMap);
            Texture2D deformation = FillFlat(fixture.particleTextureSize, fixture.particleTextureSize, fixture.deformation, FilterMode.Point);
            owned.Add(deformation);
            material.SetTexture("_SiteDeformationTex", deformation);
            Texture2D spring = FillFlat(fixture.particleTextureSize, fixture.particleTextureSize, fixture.springs, FilterMode.Point);
            owned.Add(spring);
            material.SetTexture("_SpringStateTex", spring);
            BindConstant(material, owned, "_StateTex", Color.clear);
            BindConstant(material, owned, "_SiteSurfaceTex", Color.clear);
            BindConstant(material, owned, "_RestSurfaceTex", Color.clear);
            BindConstant(material, owned, "_RestPositionTex", Color.clear);
            BindConstant(material, owned, "_SiteBulgeTex", Color.clear);
            BindConstant(material, owned, "_DensityPressureTex", Color.clear);
            BindConstant(material, owned, "_EnvironmentTex", Color.black);

            float exponent = Mathf.Lerp(8f, 2f, 0.48f);
            foreach (float yawDeg in new[] { 0f, 45f })
            {
                float radians = yawDeg * Mathf.Deg2Rad;
                Vector2 yawCosSin = new(Mathf.Cos(radians), Mathf.Sin(radians));
                material.SetVector("_PrimitiveYawCosSin", yawCosSin);
                Vector2 uv = new(fixture.sampleUv[0], fixture.sampleUv[1]);
                Color[] positions = FerrofluidSurfaceProbe.Sample(material, new[] { uv }, 0);
                Color pc = positions[0];
                if (!float.IsFinite(pc.r) || !float.IsFinite(pc.g) || !float.IsFinite(pc.b) || !float.IsFinite(pc.a) || pc.a < 0.5f)
                {
                    failures.Add($"captured-shoulder yaw{yawDeg}: non-finite or undrawn position.");
                    continue;
                }

                Vector3 basePosition = OraclePosition(uv, yawCosSin, 1, exponent);
                Vector3 support = CubeNormal(basePosition, exponent) * fixture.expectedDensity;
                Vector3 expected = basePosition
                    + new Vector3(fixture.expectedOffset[0], fixture.expectedOffset[1], fixture.expectedOffset[2])
                    + support;
                Vector3 actual = new(pc.r, pc.g, pc.b);
                float error = Vector3.Distance(actual, expected);
                if (error > 0.00002f)
                    failures.Add($"captured-shoulder yaw{yawDeg}: position error {error:R} exceeds 2e-5 (actual {actual}, expected {expected}).");
            }

            material.SetInt("_ParticleTextureSize", 1);
            material.SetInt("_ParticleCount", 1);
            material.SetFloat("_FluidHeightScale", 1f);
        }

        private static Texture2D FillFlat(int w, int h, float[] values, FilterMode filter)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true) { filterMode = filter, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color(values[i * 4], values[(i * 4) + 1], values[(i * 4) + 2], values[(i * 4) + 3]);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static void BindConstant(Material material, List<Object> owned, string name, Color color)
        {
            Texture2D texture = Fill(1, 1, (_, _) => color);
            owned.Add(texture);
            material.SetTexture(name, texture);
        }

        private static Texture2D Fill(int w, int h, Func<int, int, Color> channel)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                    pixels[(y * w) + x] = channel(x, y);
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Vector3 SphereDirection(Vector2 uv, Vector2 yawCosSin)
        {
            Vector2 p = new(Mathf.Clamp01(uv.x) * 2f - 1f, Mathf.Clamp01(uv.y) * 2f - 1f);
            Vector3 direction = new(p.x, 1f - Mathf.Abs(p.x) - Mathf.Abs(p.y), p.y);
            float fold = Mathf.Max(0f, -direction.y);
            direction.x += direction.x >= 0f ? -fold : fold;
            direction.z += direction.z >= 0f ? -fold : fold;
            direction.Normalize();
            return new Vector3(
                (direction.x * yawCosSin.x) + (direction.z * yawCosSin.y),
                direction.y,
                (direction.z * yawCosSin.x) - (direction.x * yawCosSin.y));
        }

        private static Vector3 CubeNormal(Vector3 direction, float exponent)
        {
            return new Vector3(
                Mathf.Sign(direction.x) * Mathf.Pow(Mathf.Abs(direction.x), exponent - 1f),
                Mathf.Sign(direction.y) * Mathf.Pow(Mathf.Abs(direction.y), exponent - 1f),
                Mathf.Sign(direction.z) * Mathf.Pow(Mathf.Abs(direction.z), exponent - 1f)).normalized;
        }

        private static Vector3 OraclePosition(Vector2 uv, Vector2 yawCosSin, int shape, float exponent)
        {
            Vector3 direction = SphereDirection(uv, yawCosSin);
            if (shape > 1)
                return direction * 0.5f;
            float radialNorm = Mathf.Pow(
                Mathf.Pow(Mathf.Abs(direction.x), exponent)
                    + Mathf.Pow(Mathf.Abs(direction.y), exponent)
                    + Mathf.Pow(Mathf.Abs(direction.z), exponent),
                1f / exponent);
            return (direction / Mathf.Max(radialNorm, 0.000001f)) * 0.5f;
        }
    }
}
