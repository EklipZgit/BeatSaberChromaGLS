using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // Flat relaxed-surface regression: with zero displacement everywhere the produced normals must
    // equal the analytic base tangent frame — wide UV central differences fold on the octahedral
    // chart and tilt relaxed normals enough to crease reflections.
    internal static class FerrofluidRestSurfaceNormalValidation
    {
        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidRestSurfaceNormalValidation));

        private static void RunRuntime()
        {
            var failures = new List<string>();
            string directory = FerrofluidValidationArtifacts.CreateRunDirectory("rest-seam");
            Debug.Log($"[FerrofluidRestSurfaceNormalValidation] captureDirectory={directory}");
            Material material = new(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader"));
            Texture2D heightMap = null;
            Texture2D deformation = null;
            Texture2D zero = null;
            try
            {
                heightMap = Fill(256, 256, new Color(0f, 0f, 0f, 0f));
                deformation = Fill(1, 1, new Color(0f, 1f, 0f, 0f));
                zero = Fill(1, 1, new Color(0f, 0f, 0f, 0f));
                material.SetTexture("_HeightMapTex", heightMap);
                material.SetTexture("_SiteDeformationTex", deformation);
                material.SetTexture("_SpringStateTex", zero);
                material.SetTexture("_StateTex", zero);
                material.SetTexture("_SiteSurfaceTex", zero);
                material.SetTexture("_RestSurfaceTex", zero);
                material.SetTexture("_RestPositionTex", zero);
                material.SetTexture("_DensityPressureTex", zero);
                material.SetTexture("_EnvironmentTex", Texture2D.blackTexture);
                material.SetInt("_ParticleTextureSize", 1);
                material.SetInt("_ParticleCount", 1);
                material.SetFloat("_SpikesEnabled", 0f);
                material.SetFloat("_FadeAlpha", 1f);
                material.SetFloat("_Exposure", 1f);
                material.SetFloat("_FluidHeightScale", 1f);
                material.SetFloat("_MaximumHeight", 1f);
                material.SetFloat("_HeightFactor", 1f);
                material.SetFloat("_PrimitiveGlobalSpikeFlatten", 0f);
                material.SetFloat("_MagneticFieldStrength", 0f);
                material.SetFloat("_SecondaryMagneticFieldStrength", 0f);
                material.SetFloat("_SurfaceRoundness", 0.48f);
                material.EnableKeyword("FERROFLUID_CACHED_DEFORMATION");

                var uvs = new List<Vector2>();
                float e = 1f / 448f;
                // Mirrored near-edge bands on all four chart edges plus mirrored pairs.
                for (float t = 0.1f; t < 0.95f; t += 0.2f)
                {
                    uvs.Add(new Vector2(1f - e, t));
                    uvs.Add(new Vector2(e, t));
                    uvs.Add(new Vector2(t, 1f - e));
                    uvs.Add(new Vector2(t, e));
                    uvs.Add(new Vector2(1f - 2f * e, t));
                    uvs.Add(new Vector2(t, 1f - 2f * e));
                }

                // Internal equatorial diamond fold |2u-1| + |2v-1| = 1, both branches inside and out.
                foreach (float u in new[] { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f })
                {
                    float vEdge = 0.5f + (1f - Mathf.Abs(2f * u - 1f)) * 0.5f;
                    foreach (float v in new[] { vEdge - e, vEdge + e, 1f - vEdge - e, 1f - vEdge + e })
                    {
                        if (v >= 0f && v <= 1f)
                            uvs.Add(new Vector2(u, v));
                    }
                }

                // Exact corners, edge midpoints and mirrored pairs.
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(0f, 0.5f));
                uvs.Add(new Vector2(1f, 0.5f));
                uvs.Add(new Vector2(0.5f, 0f));
                uvs.Add(new Vector2(0.5f, 1f));
                foreach (float t in new[] { 0.1f, 0.3f, 0.7f, 0.9f })
                {
                    uvs.Add(new Vector2(0f, t));
                    uvs.Add(new Vector2(0f, 1f - t));
                    uvs.Add(new Vector2(1f, t));
                    uvs.Add(new Vector2(1f, 1f - t));
                    uvs.Add(new Vector2(t, 0f));
                    uvs.Add(new Vector2(1f - t, 0f));
                    uvs.Add(new Vector2(t, 1f));
                    uvs.Add(new Vector2(1f - t, 1f));
                }

                // Interior controls.
                uvs.Add(new Vector2(0.3f, 0.3f));
                uvs.Add(new Vector2(0.7f, 0.35f));
                uvs.Add(new Vector2(0.6f, 0.62f));

                for (int shape = 1; shape <= 2; shape++)
                {
                    string shapeName = shape == 1 ? "RoundedCube" : "Sphere";
                    material.SetFloat("_SurfaceShapeMode", shape);
                    float exponent = Mathf.Lerp(8f, 2f, 0.48f);
                    foreach (float yawDeg in new[] { 0f, 45f })
                    {
                        float radians = yawDeg * Mathf.Deg2Rad;
                        Vector2 yawCosSin = new(Mathf.Cos(radians), Mathf.Sin(radians));
                        material.SetVector("_PrimitiveYawCosSin", yawCosSin);
                        Color[] positions = FerrofluidSurfaceProbe.Sample(material, uvs.ToArray(), 0);
                        Color[] normals = FerrofluidSurfaceProbe.Sample(material, uvs.ToArray(), 1);
                        using StreamWriter csv = new(Path.Combine(directory, $"{shapeName}-yaw{yawDeg}.csv"));
                        csv.WriteLine("u,v,px,py,pz,nx,ny,nz,ox,oy,oz,posError,normalError");
                        for (int index = 0; index < uvs.Count; index++)
                        {
                            Vector2 uv = uvs[index];
                            Color pc = positions[index];
                            Color nc = normals[index];
                            Vector3 pos = new(pc.r, pc.g, pc.b);
                            Vector3 normal = new(nc.r, nc.g, nc.b);
                            bool finite = float.IsFinite(pc.r) && float.IsFinite(pc.g) && float.IsFinite(pc.b)
                                && float.IsFinite(pc.a) && pc.a > 0.5f
                                && float.IsFinite(nc.r) && float.IsFinite(nc.g) && float.IsFinite(nc.b) && float.IsFinite(nc.a);
                            if (!finite)
                            {
                                failures.Add($"{shapeName} yaw{yawDeg} uv{uv}: non-finite or undrawn sample.");
                                continue;
                            }

                            Vector3 oraclePos = OraclePosition(uv, yawCosSin, shape, exponent);
                            Vector3 oracleNormal = shape == 2 ? oraclePos.normalized : CubeNormal(oraclePos, exponent);
                            float posError = Vector3.Distance(pos, oraclePos);
                            float normalError = Vector3.Distance(normal.normalized, oracleNormal);
                            csv.WriteLine(string.Format(
                                CultureInfo.InvariantCulture,
                                "{0:R},{1:R},{2:R},{3:R},{4:R},{5:R},{6:R},{7:R},{8:R},{9:R},{10:R},{11:R},{12:R}",
                                uv.x, uv.y, pos.x, pos.y, pos.z, normal.x, normal.y, normal.z,
                                oracleNormal.x, oracleNormal.y, oracleNormal.z, posError, normalError));
                            if (Mathf.Abs(normal.magnitude - 1f) > 0.001f)
                                failures.Add($"{shapeName} yaw{yawDeg} uv{uv}: normal not unit ({normal.magnitude:R}).");
                            if (posError > 0.00001f)
                                failures.Add($"{shapeName} yaw{yawDeg} uv{uv}: position error {posError:R} exceeds 1e-5.");
                            if (normalError > 0.0005f)
                                failures.Add($"{shapeName} yaw{yawDeg} uv{uv}: normal error {normalError:R} exceeds 0.0005 (about {Mathf.Rad2Deg * 2f * Mathf.Asin(normalError / 2f):F3}deg).");
                        }
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(heightMap);
                Object.DestroyImmediate(deformation);
                Object.DestroyImmediate(zero);
            }

            if (failures.Count != 0)
                throw new Exception(string.Join(" | ", failures));

            Debug.Log("[FerrofluidRestSurfaceNormalValidation] PASS: relaxed positions and normals match the analytic tangent frame.");
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

        private static Vector3 CubeNormal(Vector3 pos, float exponent)
        {
            Vector3 direction = pos.normalized;
            return new Vector3(
                Mathf.Sign(direction.x) * Mathf.Pow(Mathf.Abs(direction.x), exponent - 1f),
                Mathf.Sign(direction.y) * Mathf.Pow(Mathf.Abs(direction.y), exponent - 1f),
                Mathf.Sign(direction.z) * Mathf.Pow(Mathf.Abs(direction.z), exponent - 1f)).normalized;
        }

        private static Texture2D Fill(int width, int height, Color color)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
            var pixels = new Color[width * height];
            for (int index = 0; index < pixels.Length; index++)
                pixels[index] = color;
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
