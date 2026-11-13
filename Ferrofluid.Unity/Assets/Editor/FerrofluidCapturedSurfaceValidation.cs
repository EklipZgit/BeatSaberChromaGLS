using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // Parity regression against the user's actual click and hover captures. Each frozen record
    // carries a real four-by-four height/deformation/spring packet and the independently computed
    // expected displacement, so the selected B-spline reconstruction must reproduce the captured growth.
    internal static class FerrofluidCapturedSurfaceValidation
    {
        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidCapturedSurfaceValidation));

        private static void RunRuntime()
        {
            var failures = new List<string>();
            string directory = FerrofluidValidationArtifacts.CreateRunDirectory("captured-growth");
            Debug.Log($"[FerrofluidCapturedSurfaceValidation] captureDirectory={directory}");
            Material material = new(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader"));
            var owned = new List<Object> { material };
            try
            {
                material.EnableKeyword("FERROFLUID_CACHED_DEFORMATION");
                material.SetFloat("_FadeAlpha", 1f);
                material.SetFloat("_Exposure", 1f);
                material.SetFloat("_SpikesEnabled", 1f);
                material.SetFloat("_HeightFactor", 1f);
                material.SetFloat("_MaximumHeight", 1f);
                material.SetFloat("_PrimitiveGlobalSpikeFlatten", 0f);
                material.SetFloat("_MagneticFieldStrength", 0f);
                material.SetFloat("_SecondaryMagneticFieldStrength", 0f);
                material.SetFloat("_SurfaceRoundness", 0.48f);
                material.SetFloat("_HasCurvedCanvas", 0f);
                material.SetFloat("_Skew", 0f);

                ValidateCapturedGrowth(material, owned, directory, failures);
            }
            finally
            {
                foreach (Object o in owned)
                    Object.DestroyImmediate(o);
            }

            if (failures.Count != 0)
                throw new Exception(string.Join(" | ", failures));

            Debug.Log("[FerrofluidCapturedSurfaceValidation] PASS: captured growth packets reproduce the expected surface.");
        }

        [Serializable]
        private sealed class CapturedSurfaceFixtures
        {
            public CapturedSurfaceSample[] samples;
        }

        [Serializable]
        private sealed class CapturedSurfaceSample
        {
            public string capture;
            public float[] sourceUv;
            public float[] sourceSiteIds;
            public float[] sampleUv;
            public float[] heightMap;
            public float[] deformation;
            public float[] springs;
            public float effectiveHeightScale;
            public float[] expectedOffset;
            public float expectedDensity;
        }

        private static void ValidateCapturedGrowth(Material material, List<Object> owned, string directory, List<string> failures)
        {
            string fixturePath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../ChromaGLS.Tests/Fixtures/ferrofluid-captured-growth.json"));
            var fixtures = JsonUtility.FromJson<CapturedSurfaceFixtures>(File.ReadAllText(fixturePath));
            const int size = 4;
            float exponent = Mathf.Lerp(8f, 2f, 0.48f);
            material.SetInt("_ParticleTextureSize", size);
            material.SetInt("_ParticleCount", 16);
            material.SetFloat("_SurfaceShapeMode", 1f);

            BindConstant(material, owned, "_StateTex", Color.clear);
            BindConstant(material, owned, "_SiteSurfaceTex", Color.clear);
            BindConstant(material, owned, "_RestSurfaceTex", Color.clear);
            BindConstant(material, owned, "_RestPositionTex", Color.clear);
            BindConstant(material, owned, "_SiteBulgeTex", Color.clear);
            BindConstant(material, owned, "_DensityPressureTex", Color.clear);
            BindConstant(material, owned, "_EnvironmentTex", Color.black);

            using StreamWriter csv = new(Path.Combine(directory, "captured-growth.csv"));
            csv.WriteLine("capture,yaw,u,v,ax,ay,az,ex,ey,ez,error");
            for (int index = 0; index < fixtures.samples.Length; index++)
            {
                CapturedSurfaceSample record = fixtures.samples[index];
                Texture2D heightMap = FillFlat(size, size, record.heightMap, FilterMode.Bilinear);
                Texture2D deformation = FillFlat(size, size, record.deformation, FilterMode.Point);
                Texture2D spring = FillFlat(size, size, record.springs, FilterMode.Point);
                material.SetTexture("_HeightMapTex", heightMap);
                material.SetTexture("_SiteDeformationTex", deformation);
                material.SetTexture("_SpringStateTex", spring);
                material.SetFloat("_FluidHeightScale", record.effectiveHeightScale);

                foreach (float yawDeg in new[] { 0f, 45f })
                {
                    float radians = yawDeg * Mathf.Deg2Rad;
                    Vector2 yawCosSin = new(Mathf.Cos(radians), Mathf.Sin(radians));
                    material.SetVector("_PrimitiveYawCosSin", yawCosSin);
                    Vector2 uv = new(record.sampleUv[0], record.sampleUv[1]);
                    Color[] positions = FerrofluidSurfaceProbe.Sample(material, new[] { uv }, 0);
                    Color pc = positions[0];
                    if (!float.IsFinite(pc.r) || !float.IsFinite(pc.g) || !float.IsFinite(pc.b) || !float.IsFinite(pc.a) || pc.a < 0.5f)
                    {
                        failures.Add($"{record.capture}[{index}] yaw{yawDeg}: non-finite or undrawn position.");
                        continue;
                    }

                    Vector3 basePosition = OraclePosition(uv, yawCosSin, 1, exponent);
                    Vector3 support = CubeNormal(basePosition, exponent) * record.expectedDensity;
                    Vector3 expected = basePosition
                        + new Vector3(record.expectedOffset[0], record.expectedOffset[1], record.expectedOffset[2])
                        + support;
                    Vector3 actual = new(pc.r, pc.g, pc.b);
                    float error = Vector3.Distance(actual, expected);
                    csv.WriteLine(FormattableString.Invariant($"{record.capture},{yawDeg},{uv.x},{uv.y},{actual.x},{actual.y},{actual.z},{expected.x},{expected.y},{expected.z},{error}"));
                    if (error > 0.00002f)
                        failures.Add($"{record.capture}[{index}] yaw{yawDeg}: position error {error:R} exceeds 2e-5.");
                }

                Object.DestroyImmediate(heightMap);
                Object.DestroyImmediate(deformation);
                Object.DestroyImmediate(spring);
            }
        }

        private static void BindConstant(Material material, List<Object> owned, string name, Color color)
        {
            Texture2D texture = FillFlat(1, 1, new[] { color.r, color.g, color.b, color.a }, FilterMode.Bilinear);
            owned.Add(texture);
            material.SetTexture(name, texture);
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
