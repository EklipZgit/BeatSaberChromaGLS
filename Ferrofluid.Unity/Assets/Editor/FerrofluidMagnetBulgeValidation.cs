using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidMagnetBulgeValidation
    {
        public static void Run()
        {
            string directory = Environment.GetEnvironmentVariable("FERROFLUID_BULGE_OUTPUT")
                ?? FerrofluidValidationArtifacts.CreateRunDirectory("magnet-dome");
            Directory.CreateDirectory(directory);
            string baselinePath = Environment.GetEnvironmentVariable("FERROFLUID_BULGE_BASELINE");
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader");
            bool baseline = !string.IsNullOrEmpty(baselinePath);
            if (baseline)
            {
                string source = File.ReadAllText(baselinePath).Replace(
                    "#include \"FerrofluidCommon.cginc\"",
                    File.ReadAllText(Path.Combine(Application.dataPath, "FerrofluidCommon.cginc")));
                shader = ShaderUtil.CreateShaderAsset(source);
            }

            List<string> failures = new();
            try
            {
                if (ShaderUtil.ShaderHasError(shader))
                    throw new Exception("Bulge validation shader did not compile.");

                foreach (int shape in new[] { 1, 2 })
                    Validate(shader, shape, directory, failures);
                foreach (int shape in new[] { 1, 2 })
                    ValidateAmplificationResponse(shader, shape, 0.06f, failures);
                ValidateRuntimeBinding("RoundedCube", failures);
                ValidateRuntimeBinding("Sphere", failures);

                if (failures.Count != 0)
                    throw new Exception(string.Join(" | ", failures));

                Debug.Log($"[FerrofluidMagnetBulgeValidation] PASS: GPU domed crown and finite normals. Artifacts: {directory}");
            }
            finally
            {
                if (baseline)
                    Object.DestroyImmediate(shader);
            }
        }

        private static void Validate(Shader shader, int shape, string directory, List<string> failures)
        {
            Material material = new(shader);
            Texture2D direction = new(1, 1, TextureFormat.RGBAFloat, false, true);
            Texture2D neutral = new(1, 1, TextureFormat.RGBAFloat, false, true);
            direction.SetPixel(0, 0, new Color(0f, 1f, 0f, 1f));
            direction.Apply();
            neutral.SetPixel(0, 0, Color.clear);
            neutral.Apply();
            try
            {
                float gap = GetGap();
                object config = FerrofluidValidationRuntime.CreateConfig();
                foreach ((string name, float value) in new (string, float)[]
                {
                    ("BulgeRadius", 0.31f),
                    ("BulgeHeight", 0.12f),
                    ("HeightFalloffExponent", 2.4f),
                    ("MagnetHeightDeadZoneRadius", 0.13f),
                    ("MagnetHeightStrengthCap", 0.74f),
                    ("FerrofluidSphereBulgeScale", 0.6f),
                    ("FerrofluidCubeRoundness", 0.48f),
                })
                    FerrofluidValidationRuntime.SetConfig(config, name, value);
                Type envType = config.GetType().Assembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true)!;
                MethodInfo surfaceAt = envType.GetMethod("EvaluateSurfacePositionFromDirection", BindingFlags.Static | BindingFlags.NonPublic)!;
                MethodInfo normalAt = envType.GetMethod("EvaluateSurfaceNormalFromDirection", BindingFlags.Static | BindingFlags.NonPublic)!;
                object shapeEnum = Enum.ToObject(surfaceAt.GetParameters()[0].ParameterType, shape);
                MethodInfo bulgeOffset = config.GetType().Assembly
                    .GetType("ChromaGLS.Settings.FerrofluidPrimitiveDeformation", true)!
                    .GetMethod("EvaluateSiteBulgeOffset", BindingFlags.Static | BindingFlags.NonPublic)!;
                object settings = config.GetType().Assembly
                    .GetType("ChromaGLS.Settings.FerrofluidPrimitiveDeformation", true)!
                    .GetMethod("EvaluateSettings", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, new object[] { shapeEnum, config, 1f })!;
                float settingsBulgeHeight = (float)settings.GetType()
                    .GetField("BulgeHeight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                    .GetValue(settings)!;

                material.SetFloat("_SurfaceShapeMode", shape);
                material.SetFloat("_SurfaceRoundness", 0.48f);
                material.SetVector("_PrimitiveYawCosSin", new Vector4(1f, 0f, 0f, 0f));
                material.SetFloat("_ParticleTextureSize", 1f);
                material.SetFloat("_ParticleCount", 1f);
                material.SetTexture("_HeightMapTex", neutral);
                material.SetVector("_HeightMapTex_TexelSize", new Vector4(1f / 256f, 1f / 256f, 256f, 256f));
                material.SetTexture("_SiteDeformationTex", direction);
                material.SetTexture("_SiteBulgeTex", neutral);
                material.SetTexture("_SiteSurfaceTex", direction);
                material.EnableKeyword("FERROFLUID_CACHED_DEFORMATION");
                material.SetFloat("_SpikesEnabled", 0f);
                material.SetFloat("_FluidHeightScale", 0.42f);
                material.SetFloat("_HeightFactor", 0.38f);
                material.SetFloat("_MaximumHeight", 1.6f);
                material.SetFloat("_BulgeHeight", settingsBulgeHeight);
                material.SetFloat("_BulgeRadius", 0.31f);
                material.SetFloat("_HeightFalloffExponent", 2.4f);
                material.SetFloat("_MagnetHeightDeadZoneRadius", 0.13f);
                material.SetFloat("_MagnetHeightStrengthCap", 0.74f);
                material.SetFloat("_SphereBulgeScale", 0.6f);
                material.SetFloat("_MagnetHeightAmplification", 1f);
                material.SetVector("_MagneticFieldPosition", new Vector4(0f, 0.5f + gap, 0f, 0f));
                material.SetFloat("_SecondaryMagneticFieldStrength", 0f);
                material.SetMatrix("_FerrofluidObjectToWorld", Matrix4x4.identity);
                material.SetMatrix("_FerrofluidWorldToObject", Matrix4x4.identity);
                material.SetVector("_SourceLocalScale", Vector4.one);
                Vector2[] uv = new Vector2[260];
                for (int index = 0; index <= 256; index++)
                {
                    float angle = index * Mathf.PI / 256f;
                    uv[index] = Encode(new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f));
                }

                uv[257] = Encode(new Vector3(Mathf.Sin(0.02f), Mathf.Cos(0.02f), 0f));
                uv[258] = Encode(new Vector3(Mathf.Sin(0.04f), Mathf.Cos(0.04f), 0f));
                // Probe the skirt at a fixed physical field distance (.46) rather than a fixed
                // angle so the feather check stays valid at the real .07 magnet gap on both hulls.
                uv[259] = Encode(SurfaceAtDistance(surfaceAt, shapeEnum, 0.46f));
                material.SetFloat("_MagneticFieldStrength", 0f);
                Color[] rest = FerrofluidSurfaceProbe.Sample(material, uv, 0);
                Color[] baseNormals = FerrofluidSurfaceProbe.Sample(material, uv, 1);
                material.SetFloat("_MagneticFieldStrength", 0.74f);
                Color[] positions = FerrofluidSurfaceProbe.Sample(material, uv, 0);
                Color[] normals = FerrofluidSurfaceProbe.Sample(material, uv, 1);
                float centre = Height(positions[0], rest[0], baseNormals[0]);
                float dSmall = centre - Height(positions[257], rest[257], baseNormals[257]);
                float dLarge = centre - Height(positions[258], rest[258], baseNormals[258]);
                float skirt = Height(positions[259], rest[259], baseNormals[259]);
                float skirtDistance = ((Vector3)material.GetVector("_MagneticFieldPosition") - Point(rest[259])).magnitude;
                float maxGradient = 0f;
                for (int index = 1; index <= 127; index++)
                {
                    float segment = (Point(rest[index]) - Point(rest[index - 1])).magnitude;
                    if (segment <= 0f)
                        continue;
                    float gradient = Mathf.Abs(
                        Height(positions[index], rest[index], baseNormals[index])
                        - Height(positions[index - 1], rest[index - 1], baseNormals[index - 1])) / segment;
                    maxGradient = Mathf.Max(maxGradient, gradient);
                }

                int parityCount = 0;
                float parityWorst = 0f;
                foreach (int index in new[] { 0, 8, 16, 24, 32, 48, 64, 80, 96, 112, 120 })
                {
                    Vector3 basePoint = Point(rest[index]);
                    Vector3 expected = basePoint + (Vector3)bulgeOffset.Invoke(
                        null,
                        new object[] { settings, shapeEnum, (Vector4)basePoint, new Vector3(0f, 0.5f + gap, 0f), 0.74f, 1f })!;
                    float error = Vector3.Distance(Point(positions[index]), expected);
                    parityWorst = Mathf.Max(parityWorst, error);
                    parityCount++;
                    if (float.IsNaN(error) || float.IsInfinity(error) || error > 0.0001f)
                        failures.Add($"shape={shape} CPU/GPU parity miss at sample {index} ({error:F6})");
                }

                Debug.Log($"[FerrofluidMagnetBulgeValidation] shape={shape} parity checked={parityCount} worst={parityWorst:F6}");
                float normalizedGradient = maxGradient * 0.31f / centre;
                Debug.Log($"[FerrofluidMagnetBulgeValidation] shape={shape} centre={centre:R} dSmall={dSmall:R} dRatio={(dLarge / dSmall):R} skirt={skirt:R} skirtDist={skirtDistance:R} gradient={normalizedGradient:R}");
                if (!(centre > 0f && dSmall > 0.0005f * centre))
                    failures.Add($"shape={shape} crown must curve (dSmall={dSmall:R})");
                if (!(dLarge / dSmall > 3f && dLarge / dSmall < 5f))
                    failures.Add($"shape={shape} crown deficit must be quadratic (ratio={dLarge / dSmall:R})");
                if (!(skirt > 0.005f * centre && skirtDistance > 0.44f))
                    failures.Add($"shape={shape} skirt must extend past the original footprint ({skirt:R} at {skirtDistance:R})");
                if (!(normalizedGradient <= 1.05f))
                    failures.Add($"shape={shape} normalized flank gradient {normalizedGradient:R} must stay <= 1.05");

                using (StreamWriter writer = new(Path.Combine(directory, $"shape-{shape}.csv")))
                {
                    writer.WriteLine("sample,baseX,baseY,baseZ,x,y,z,nx,ny,nz,height");
                    for (int index = 0; index < uv.Length; index++)
                    {
                        Vector3 normal = Point(normals[index]);
                        if (float.IsNaN(normal.sqrMagnitude) || Mathf.Abs(normal.magnitude - 1f) > 0.01f)
                            failures.Add($"shape={shape} non-unit normal at sample {index}");

                        writer.WriteLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0},{1:R},{2:R},{3:R},{4:R},{5:R},{6:R},{7:R},{8:R},{9:R},{10:R}",
                            index, rest[index].r, rest[index].g, rest[index].b,
                            positions[index].r, positions[index].g, positions[index].b,
                            normal.x, normal.y, normal.z,
                            Height(positions[index], rest[index], baseNormals[index])));
                    }
                }

                Capture(material, Path.Combine(directory, $"shape-{shape}.png"), new Vector3(0f, 0.9f, -1.6f));
                Capture(material, Path.Combine(directory, $"shape-{shape}-silhouette.png"), new Vector3(0f, 0.35f, -1.6f));
                if (shape == 1)
                {
                    // Anchor the off-axis poses on the production surface helpers, not a sphere
                    // guess: they return the real base point and normal for this hull.
                    foreach (Vector3 cornerDirection in new[] { new Vector3(1f, 1f, 0f), new Vector3(1f, 1f, -1f) })
                    {
                        Vector3 anchor = (Vector3)surfaceAt.Invoke(null, new object[] { shapeEnum, cornerDirection, 0.48f })!;
                        Vector3 normal = ((Vector3)normalAt.Invoke(null, new object[] { shapeEnum, cornerDirection, 0.48f })!).normalized;
                        Vector3 cornerMagnet = anchor + (normal * gap);
                        material.SetVector("_MagneticFieldPosition", cornerMagnet);
                        material.SetFloat("_MagneticFieldStrength", 0.74f);
                        string tag = cornerDirection.z < 0f ? "edge-corner" : "edge";
                        Debug.Log($"[FerrofluidMagnetBulgeValidation] {tag} anchor={anchor} normal={normal} magnet={cornerMagnet} gap={gap:R}");
                        Capture(material, Path.Combine(directory, $"shape-1-{tag}.png"), new Vector3(0f, 0.9f, -1.6f));
                        Capture(material, Path.Combine(directory, $"shape-1-{tag}-silhouette.png"), new Vector3(0f, 0.35f, -1.6f));
                    }

                    material.SetVector("_MagneticFieldPosition", new Vector4(0f, 0.5f + gap, 0f, 0f));
                }
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(direction);
                Object.DestroyImmediate(neutral);
            }
        }

        private static Vector3 SurfaceAtDistance(MethodInfo surfaceAt, object shapeEnum, float distance)
        {
            float low = 0f;
            float high = Mathf.PI / 2f;
            Vector3 result = Vector3.up;
            Vector3 magnet = new(0f, 0.5f + GetGap(), 0f);
            for (int iteration = 0; iteration < 32; iteration++)
            {
                float theta = (low + high) / 2f;
                Vector3 point = (Vector3)surfaceAt.Invoke(
                    null,
                    new object[] { shapeEnum, new Vector3(Mathf.Sin(theta), Mathf.Cos(theta), 0f), 0.48f })!;
                if (Vector3.Distance(magnet, point) < distance)
                    low = theta;
                else
                    high = theta;
                result = point;
            }

            return result;
        }

        private static float GetGap()
            => float.TryParse(
                Environment.GetEnvironmentVariable("FERROFLUID_BULGE_GAP"),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float gap)
                ? gap
                : 0.07f;

        private static Vector3 Point(Color value) => new(value.r, value.g, value.b);

        private static float Height(Color position, Color rest, Color normal)
            => Vector3.Dot(Point(position) - Point(rest), Point(normal));

        private static Vector2 Encode(Vector3 direction)
        {
            Vector3 p = direction / (Mathf.Abs(direction.x) + Mathf.Abs(direction.y) + Mathf.Abs(direction.z));
            Vector2 chart = new(p.x, p.z);
            if (p.y < 0f)
                chart = new Vector2((1f - Mathf.Abs(chart.y)) * (chart.x >= 0f ? 1f : -1f), (1f - Mathf.Abs(chart.x)) * (chart.y >= 0f ? 1f : -1f));

            return chart * 0.5f + Vector2.one * 0.5f;
        }

        private static void Capture(Material material, string path, Vector3 cameraPosition)
        {
            GameObject body = new("Bulge profile", typeof(MeshFilter), typeof(MeshRenderer));
            GameObject eye = new("Bulge camera", typeof(Camera));
            Mesh mesh = new() { indexFormat = IndexFormat.UInt32 };
            Cubemap stripes = new(64, TextureFormat.RGBAFloat, false);
            RenderTexture target = RenderTexture.GetTemporary(640, 480, 24, RenderTextureFormat.ARGBFloat);
            Texture2D image = new(640, 480, TextureFormat.RGB24, false, true);
            Texture previousRoom = Shader.GetGlobalTexture("_FerrofluidRoomEnvironment");
            float previousParabola = Shader.GetGlobalFloat("_FerrofluidParabolaReflectionEnabled");
            bool previousKeyword = Shader.IsKeywordEnabled("FERROFLUID_ROOM_REFLECTIONS");
            RenderTexture previousTarget = RenderTexture.active;
            try
            {
                const int resolution = 160;
                Vector3[] vertices = new Vector3[(resolution + 1) * (resolution + 1)];
                Vector2[] uv = new Vector2[vertices.Length];
                int[] indices = new int[resolution * resolution * 6];
                for (int y = 0; y <= resolution; y++)
                {
                    for (int x = 0; x <= resolution; x++)
                        uv[y * (resolution + 1) + x] = new Vector2(x / (float)resolution, y / (float)resolution);
                }

                int offset = 0;
                for (int y = 0; y < resolution; y++)
                {
                    for (int x = 0; x < resolution; x++)
                    {
                        int a = y * (resolution + 1) + x;
                        int c = a + resolution + 1;
                        indices[offset++] = a;
                        indices[offset++] = a + 1;
                        indices[offset++] = c;
                        indices[offset++] = a + 1;
                        indices[offset++] = c + 1;
                        indices[offset++] = c;
                    }
                }

                mesh.vertices = vertices;
                mesh.uv = uv;
                mesh.triangles = indices;
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
                body.GetComponent<MeshFilter>().sharedMesh = mesh;
                body.GetComponent<MeshRenderer>().sharedMaterial = material;
                Color[] pixels = new Color[64 * 64];
                for (int index = 0; index < pixels.Length; index++)
                    pixels[index] = (index / 64) % 16 < 3 ? new Color(1f, 1.5f, 2f) : new Color(0.03f, 0.04f, 0.05f);

                for (int face = 0; face < 6; face++)
                    stripes.SetPixels(pixels, (CubemapFace)face);

                stripes.Apply();
                Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", stripes);
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionEnabled", 0f);
                Shader.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                material.SetVector("_FerrofluidBaseColor", new Vector4(0.004f, 0.004f, 0.004f, 1f));
                material.SetVector("_FerrofluidReflectionStrengths", new Vector4(1f, 0f, 0f, 1f));
                material.SetFloat("_Reflectivity", 0.21f);
                material.SetFloat("_Exposure", 1f);
                material.SetFloat("_FadeAlpha", 1f);
                Camera camera = eye.GetComponent<Camera>();
                camera.enabled = false;
                camera.transform.position = cameraPosition;
                Debug.Log($"[FerrofluidMagnetBulgeValidation] capture {path} camera={cameraPosition} magnet={material.GetVector("_MagneticFieldPosition")}");
                camera.transform.LookAt(new Vector3(0f, 0.2f, 0f));
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 640, 480), 0, 0);
                Color[] frame = image.GetPixels();
                for (int index = 0; index < frame.Length; index++)
                {
                    Color c = frame[index];
                    frame[index] = new Color(c.r / (1f + c.r), c.g / (1f + c.g), c.b / (1f + c.b)).gamma;
                }

                image.SetPixels(frame);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", previousRoom);
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionEnabled", previousParabola);
                if (!previousKeyword)
                    Shader.DisableKeyword("FERROFLUID_ROOM_REFLECTIONS");

                RenderTexture.active = previousTarget;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(stripes);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(body);
                Object.DestroyImmediate(eye);
            }
        }

        // Trigger boost scales the whole support response linearly: at amp k the displaced
        // surface sits at rest + k * (amp1 - rest), measured against a no-magnet baseline.
        private static void ValidateAmplificationResponse(Shader shader, int shape, float bulgeHeight, List<string> failures)
        {
            Texture2D neutral = new(1, 1, TextureFormat.RGBAFloat, false, true);
            neutral.SetPixel(0, 0, Color.clear);
            neutral.Apply();
            Texture2D direction = new(1, 1, TextureFormat.RGBAFloat, false, true);
            direction.SetPixel(0, 0, new Color(0f, 1f, 0f, 1f));
            direction.Apply();
            Material material = new(shader);
            try
            {
                material.SetFloat("_SurfaceShapeMode", shape);
                material.SetFloat("_SurfaceRoundness", 0.48f);
                material.SetVector("_PrimitiveYawCosSin", new Vector4(1f, 0f, 0f, 0f));
                material.SetFloat("_ParticleTextureSize", 1f);
                material.SetFloat("_ParticleCount", 1f);
                material.SetTexture("_HeightMapTex", neutral);
                material.SetVector("_HeightMapTex_TexelSize", new Vector4(1f / 256f, 1f / 256f, 256f, 256f));
                material.SetTexture("_SiteDeformationTex", direction);
                material.SetTexture("_SiteBulgeTex", neutral);
                material.SetTexture("_SiteSurfaceTex", direction);
                material.EnableKeyword("FERROFLUID_CACHED_DEFORMATION");
                material.SetFloat("_SpikesEnabled", 0f);
                material.SetFloat("_FluidHeightScale", 1f);
                material.SetFloat("_HeightFactor", 1f);
                material.SetFloat("_MaximumHeight", 1f);
                material.SetFloat("_BulgeHeight", bulgeHeight);
                material.SetFloat("_BulgeRadius", 0.31f);
                material.SetFloat("_HeightFalloffExponent", 2.4f);
                material.SetFloat("_MagnetHeightDeadZoneRadius", 0.13f);
                material.SetFloat("_MagnetHeightStrengthCap", 0.74f);
                material.SetFloat("_SphereBulgeScale", 0.6f);
                material.SetFloat("_MagneticFieldStrength", 0.74f);
                material.SetFloat("_SecondaryMagneticFieldStrength", 0f);
                material.SetVector("_MagneticFieldPosition", new Vector4(0f, 0.5f + GetGap(), 0f, 0f));
                material.SetMatrix("_FerrofluidObjectToWorld", Matrix4x4.identity);
                material.SetMatrix("_FerrofluidWorldToObject", Matrix4x4.identity);
                material.SetVector("_SourceLocalScale", Vector4.one);
                var uvs = new[] { Encode(new Vector3(0f, 1f, 0f)), Encode(new Vector3(Mathf.Sin(0.08f), Mathf.Cos(0.08f), 0f)), Encode(new Vector3(-Mathf.Sin(0.08f), Mathf.Cos(0.08f), 0f)), Encode(new Vector3(Mathf.Sin(0.16f), Mathf.Cos(0.16f), 0f)), Encode(new Vector3(-Mathf.Sin(0.16f), Mathf.Cos(0.16f), 0f)) };
                material.SetFloat("_MagneticFieldStrength", 0f);
                material.SetFloat("_MagnetHeightAmplification", 1f);
                Color[] rest = FerrofluidSurfaceProbe.Sample(material, uvs, 0);
                Color[] restNormals = FerrofluidSurfaceProbe.Sample(material, uvs, 1);
                CheckDrawn(shape, 0f, rest, restNormals, failures);
                material.SetFloat("_MagneticFieldStrength", 0.74f);
                Color[] amp1 = FerrofluidSurfaceProbe.Sample(material, uvs, 0);
                Color[] amp1Normals = FerrofluidSurfaceProbe.Sample(material, uvs, 1);
                CheckDrawn(shape, 1f, amp1, amp1Normals, failures);
                foreach (float amp in new[] { 1.3f, 1.6f })
                {
                    material.SetFloat("_MagnetHeightAmplification", amp);
                    Color[] positions = FerrofluidSurfaceProbe.Sample(material, uvs, 0);
                    Color[] normals = FerrofluidSurfaceProbe.Sample(material, uvs, 1);
                    CheckDrawn(shape, amp, positions, normals, failures);
                    float maxPosition = 0f;
                    for (int i = 0; i < uvs.Length; i++)
                    {
                        Vector3 expected = new Vector3(rest[i].r, rest[i].g, rest[i].b)
                            + amp * (new Vector3(amp1[i].r, amp1[i].g, amp1[i].b) - new Vector3(rest[i].r, rest[i].g, rest[i].b));
                        maxPosition = Mathf.Max(maxPosition, Vector3.Distance(expected, new Vector3(positions[i].r, positions[i].g, positions[i].b)));
                    }
                    string shapeName = shape == 1 ? "RoundedCube" : "Sphere";
                    if (maxPosition > 0.000002f)
                        failures.Add($"{shapeName} amp{amp}: linear response error {maxPosition:R} exceeds 2e-6.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
                Object.DestroyImmediate(neutral);
                Object.DestroyImmediate(direction);
            }
        }

        // Probe results must be finite and drawn before comparing displacement.
        private static void CheckDrawn(int shape, float amp, Color[] positions, Color[] normals, List<string> failures)
        {
            string shapeName = shape == 1 ? "RoundedCube" : "Sphere";
            for (int i = 0; i < positions.Length; i++)
            {
                Color p = positions[i];
                if (!float.IsFinite(p.r) || !float.IsFinite(p.g) || !float.IsFinite(p.b) || !float.IsFinite(p.a) || p.a <= 0.5f)
                {
                    failures.Add($"{shapeName} amp{amp}: position probe {i} is non-finite or undrawn.");
                    continue;
                }
                Color n = normals[i];
                Vector3 normal = new(n.r, n.g, n.b);
                if (!float.IsFinite(n.r) || !float.IsFinite(n.g) || !float.IsFinite(n.b) || Mathf.Abs(normal.magnitude - 1f) > 0.01f)
                    failures.Add($"{shapeName} amp{amp}: normal probe {i} is non-finite or non-unit.");
                if (!float.IsFinite(n.a) || n.a < 0f || n.a > 1f)
                    failures.Add($"{shapeName} amp{amp}: film alpha probe {i} out of range.");
            }
        }

        private static void ValidateRuntimeBinding(string shapeName, List<string> failures)
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
            Material surface = (Material)runtime.Get("_surfaceMaterial");
            float bound = surface.GetFloat("_BulgeHeight");
            if (!float.IsFinite(bound) || Mathf.Abs(bound - 0.06f) > 0.000001f)
                failures.Add($"{shapeName} surface _BulgeHeight {bound:R} is not the primitive half height 0.06.");
        }
    }
}
