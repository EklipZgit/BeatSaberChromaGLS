using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidShadingValidation
    {
        private const int Width = 64;
        private const int Height = 8;
        private static readonly List<string> Failures = new();
        private static readonly List<string> OwnedAssets = new();
        private static string _directory;

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidShadingValidation));

        private static void RunRuntime()
        {
            Failures.Clear();
            OwnedAssets.Clear();
            _directory = FerrofluidValidationArtifacts.CreateRunDirectory("shading");
            Debug.Log($"[FerrofluidShadingValidation] captureDirectory={_directory}");
            Material material = new(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader"));
            try
            {
                string filmPath = CreateTexture("film", 512, 512, (_, y) => y % 2 == 0 ? Color.white : Color.black);
                string environmentPath = CreateTexture("environment", 512, 128, (_, y) => y >= 62 && y <= 65 ? Color.white : Color.black);
                string polePath = CreateTexture("poles", 512, 128, (_, y) => y < 64 ? Color.red : Color.blue);
                BuildFerrofluidBundle.ConfigureTextures(filmPath, environmentPath);
                BuildFerrofluidBundle.ConfigureTextures(filmPath, polePath);
                material.SetFloat("_FadeAlpha", 1f);
                material.SetFloat("_Exposure", 1f);
                material.SetFloat("_EmissionStrength", 0f);
                material.SetFloat("_SurfaceShapeMode", 2f);
                material.SetFloat("_PrimitiveAmbientScale", 0f);
                material.SetFloat("_MatteSheen", 0f);
                material.SetFloat("_MatteAngle", 5f);
                material.SetFloat("_ReflectionAngle", 5f);
                material.SetFloat("_ThinFilmScale", 1f);
                material.SetFloat("_ThinFilmOffset", 0f);
                material.SetVector("_MatteLightRotation", Vector3.zero);
                material.SetVector("_ReflectionRotation", Vector3.zero);
                material.SetVector("_ValidationCameraPosition", Vector3.forward);
                ValidateFilm(material, AssetDatabase.LoadAssetAtPath<Texture2D>(filmPath));
                ValidateReflection(material, AssetDatabase.LoadAssetAtPath<Texture2D>(environmentPath));
                ValidateMipTransition(material, AssetDatabase.LoadAssetAtPath<Texture2D>(environmentPath));
                ValidatePoles(material, AssetDatabase.LoadAssetAtPath<Texture2D>(polePath));
                ValidateRimMinification(material);
                ValidateDistantGreenTint(material);
            }
            finally
            {
                Object.DestroyImmediate(material);
                foreach (string path in OwnedAssets)
                    AssetDatabase.DeleteAsset(path);
            }

            if (Failures.Count != 0)
                throw new Exception(string.Join(" | ", Failures));

            Debug.Log("[FerrofluidShadingValidation] PASS: minified film, tiny-motion reflections, and environment poles stay continuous.");
        }

        private static void ValidateFilm(Material material, Texture2D film)
        {
            Debug.Log($"[FerrofluidShadingValidation] film mips={film.mipmapCount} filter={film.filterMode} aniso={film.anisoLevel} qualityAniso={QualitySettings.anisotropicFiltering}");
            material.SetTexture("_ThinFilmTex", film);
            material.SetTexture("_EnvironmentTex", Texture2D.blackTexture);
            material.SetFloat("_Reflectivity", 0f);
            float maximumTemporalChange = 0f;
            float maximumDisplayChange = 0f;
            Color[] previous = null;
            for (int frame = 0; frame < 9; frame++)
            {
                float offset = frame / (8f * 512f);
                Color[] pixels = Capture(material, Vector3.forward, Vector3.forward, 0.25f + offset, 0.4375f + offset, $"film-{frame:D2}");
                if (previous != null)
                {
                    for (int index = 0; index < pixels.Length; index++)
                    {
                        maximumTemporalChange = Mathf.Max(maximumTemporalChange, Difference(previous[index], pixels[index]));
                        maximumDisplayChange = Mathf.Max(maximumDisplayChange, Difference(previous[index].gamma, pixels[index].gamma));
                    }
                }

                previous = pixels;
            }

            // A magnified row must retain its contrast: minification filtering must not globally blur the lookup.
            Color[] sharp = Capture(material, Vector3.forward, Vector3.forward, 0f, 8f / 512f, "film-magnified");
            float minimum = float.MaxValue;
            float maximum = 0f;
            foreach (Color pixel in sharp)
            {
                minimum = Mathf.Min(minimum, pixel.r);
                maximum = Mathf.Max(maximum, pixel.r);
            }

            Debug.Log($"[FerrofluidShadingValidation] film maxTinyMotionChange={maximumTemporalChange:F7} maxDisplayChange={maximumDisplayChange:F7} magnifiedContrast={maximum - minimum:F7}");
            if (maximumTemporalChange > 0.005f)
                Failures.Add($"minified film stripes flicker under a sub-texel shift ({maximumTemporalChange:F7})");
            if (maximum - minimum < 0.015f)
                Failures.Add("film minification was hidden by blurring magnified lookup rows");
            if (maximumDisplayChange > 2f / 255f)
                Failures.Add($"minified film flickers by more than two display levels under a sub-texel shift ({maximumDisplayChange:F7})");
        }

        private static void ValidateRimMinification(Material material)
        {
            Texture2D film = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Generated/FerroLUT.png");
            Texture2D environment = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Generated/env-map-01.jpg");
            material.SetFloat("_PrimitiveAmbientScale", 0.15f);
            material.SetFloat("_ThinFilmScale", 3f);
            material.SetFloat("_ThinFilmOffset", 0.07f);
            material.SetFloat("_MatteAngle", 10f);
            material.SetFloat("_ReflectionAngle", 15f);
            material.SetVector("_MatteLightRotation", new Vector3(76f, 295f, 174f));
            material.SetVector("_ReflectionRotation", new Vector3(244f, 210f, 167f));
            foreach (string component in new[] { "film", "matte", "reflection", "combined" })
            {
                material.SetTexture("_ThinFilmTex", component == "matte" || component == "reflection" ? Texture2D.blackTexture : film);
                material.SetTexture("_EnvironmentTex", component == "film" || component == "matte" ? Texture2D.blackTexture : environment);
                material.SetFloat("_MatteSheen", component == "matte" || component == "combined" ? 0.57f : 0f);
                material.SetFloat("_Reflectivity", component == "reflection" || component == "combined" ? 0.21f : 0f);
                Vector3 normal = new Vector3(1f, 0f, 0.005f).normalized;
                float near = MeanLuminance(Capture(material, normal, normal, 0f, 0.25f, $"rim-{component}-near", 512));
                float far = MeanLuminance(Capture(material, normal, normal, 0f, 0.25f, $"rim-{component}-far", 8));
                Debug.Log($"[FerrofluidShadingValidation] rim component={component} near={near:R} far={far:R} farRatio={far / Mathf.Max(near, 0.0000001f):R}");
                if (component == "film" && far > near * 1.25f + 0.00001f)
                    Failures.Add("Minifying the real film lookup makes grazing spike edges brighter.");
            }
        }

        private static float MeanLuminance(Color[] pixels)
        {
            double total = 0;
            foreach (Color pixel in pixels)
                total += (pixel.r * 0.2126) + (pixel.g * 0.7152) + (pixel.b * 0.0722);

            return (float)(total / pixels.Length);
        }

        private static void ValidateDistantGreenTint(Material material)
        {
            material.SetTexture("_ThinFilmTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Generated/FerroLUT.png"));
            material.SetTexture("_EnvironmentTex", Texture2D.blackTexture);
            material.SetFloat("_MatteSheen", 0f);
            material.SetFloat("_Reflectivity", 0f);
            foreach (float incidence in new[] { 0.3f, 0.5f, 0.65f, 0.8f, 0.9f, 0.95f })
            {
                float facing = 1f - incidence;
                Vector3 normal = new(Mathf.Sqrt(1f - (facing * facing)), 0f, facing);
                Color[] nearPixels = Capture(material, normal, normal, 0f, 0.25f, $"green-{incidence:F2}-near", 512);
                Color[] farPixels = Capture(material, normal, normal, 0f, 0.25f, $"green-{incidence:F2}-far", 8);
                float near = GreenTint(nearPixels);
                float far = GreenTint(farPixels);
                float nearCoverage = GreenCoverage(nearPixels);
                float farCoverage = GreenCoverage(farPixels);
                Debug.Log($"[FerrofluidShadingValidation] green incidence={incidence:F2} near={near:R} far={far:R} nearCoverage={nearCoverage:F6} farCoverage={farCoverage:F6}");
                if (far > (near * 0.75f) + 0.000001f)
                    Failures.Add($"Distant thin-film green remains too prominent at incidence {incidence:F2}.");
                if (farCoverage >= 0.5f)
                    Failures.Add($"Minification spreads prominent green across the thin-film band at incidence {incidence:F2}.");
            }

            static float GreenTint(Color[] pixels)
            {
                double total = 0;
                foreach (Color pixel in pixels)
                    total += Mathf.Max(pixel.g - Mathf.Max(pixel.r, pixel.b), 0f);
                return (float)(total / pixels.Length);
            }

            static float GreenCoverage(Color[] pixels)
            {
                int count = 0;
                foreach (Color pixel in pixels)
                {
                    if (pixel.g - Mathf.Max(pixel.r, pixel.b) > 0.003f)
                        count++;
                }
                return count / (float)pixels.Length;
            }
        }

        private static void ValidateReflection(Material material, Texture2D environment)
        {
            material.SetTexture("_ThinFilmTex", Texture2D.blackTexture);
            material.SetTexture("_EnvironmentTex", environment);
            material.SetFloat("_Reflectivity", 1f);
            float maximumRange = 0f;
            float maximumTemporal = 0f;
            Color[] previous = null;
            const float Span = 0.04f;
            for (int frame = 0; frame < 9; frame++)
            {
                float phase = -frame * 0.2f * Span / Width;
                float left = (Mathf.PI * 0.25f) - (Span * 0.5f) + phase;
                float right = left + Span;
                Vector3 leftNormal = new(Mathf.Sin(left), 0f, Mathf.Cos(left));
                Vector3 rightNormal = new(Mathf.Sin(right), 0f, Mathf.Cos(right));
                Color[] pixels = Capture(material, leftNormal, rightNormal, 0.5f, 0.5f, $"reflection-{frame:D2}");
                Color minimum = Color.white;
                Color maximum = Color.black;
                for (int index = 0; index < pixels.Length; index++)
                {
                    minimum.r = Mathf.Min(minimum.r, pixels[index].r);
                    minimum.g = Mathf.Min(minimum.g, pixels[index].g);
                    minimum.b = Mathf.Min(minimum.b, pixels[index].b);
                    maximum.r = Mathf.Max(maximum.r, pixels[index].r);
                    maximum.g = Mathf.Max(maximum.g, pixels[index].g);
                    maximum.b = Mathf.Max(maximum.b, pixels[index].b);
                    if (previous != null)
                        maximumTemporal = Mathf.Max(maximumTemporal, Difference(previous[index], pixels[index]));
                }

                maximumRange = Mathf.Max(maximumRange, Difference(minimum, maximum));
                previous = pixels;
            }

            Debug.Log($"[FerrofluidShadingValidation] reflection constantLongitudeRange={maximumRange:F7} maxTinyMotionChange={maximumTemporal:F7}");
            if (maximumRange > 0.002f || maximumTemporal > 0.002f)
                Failures.Add($"a longitude-constant environment develops a moving mip seam ({maximumRange:F7}/{maximumTemporal:F7})");
        }

        private static void ValidatePoles(Material material, Texture2D environment)
        {
            material.SetTexture("_EnvironmentTex", environment);
            float component = Mathf.Sqrt(0.5f);
            Vector3 north = new(0f, component, component);
            Vector3 south = new(0f, -component, component);
            Color[] first = Capture(material, north, north, 0.5f, 0.5f, "north");
            Color[] second = Capture(material, south, south, 0.5f, 0.5f, "south");
            float redSeparation = first[0].r - second[0].r;
            float blueSeparation = second[0].b - first[0].b;
            Debug.Log($"[FerrofluidShadingValidation] poles redSeparation={redSeparation:F7} blueSeparation={blueSeparation:F7}");
            if (redSeparation < 0.004f || blueSeparation < 0.02f)
                Failures.Add("environment sampling blends the distinct north and south poles");
        }

        private static void ValidateMipTransition(Material material, Texture2D environment)
        {
            material.SetTexture("_ThinFilmTex", Texture2D.blackTexture);
            material.SetTexture("_EnvironmentTex", environment);
            material.SetFloat("_Reflectivity", 1f);
            Color previous = Color.black;
            float maximumStep = 0f;
            const int OddWidth = 65;
            for (int frame = 0; frame <= 50; frame++)
            {
                float depth = 0.42f + (frame * 0.001f);
                Vector3 left = new(0f, -1f, depth);
                Vector3 right = new(0f, 1f, depth);
                Color[] pixels = Capture(material, left, right, 0.5f, 0.5f, $"mip-{frame:D2}", OddWidth);
                // The centre pixel has a fixed reflection direction while only its footprint changes.
                Color centre = pixels[OddWidth / 2];
                if (frame != 0)
                    maximumStep = Mathf.Max(maximumStep, Difference(previous, centre));

                previous = centre;
            }

            Debug.Log($"[FerrofluidShadingValidation] reflection mipTransitionStep={maximumStep:F7}");
            if (maximumStep > 0.002f)
                Failures.Add($"tiny footprint changes abruptly switch reflection mip levels ({maximumStep:F7})");
        }

        private static float Difference(Color first, Color second)
        {
            return Mathf.Max(Mathf.Abs(first.r - second.r), Mathf.Max(Mathf.Abs(first.g - second.g), Mathf.Abs(first.b - second.b)));
        }

        private static string CreateTexture(string name, int width, int height, Func<int, int, Color> color)
        {
            string fileName = "FerrofluidValidation-" + Path.GetFileName(_directory) + "-" + name + ".png";
            string assetPath = "Assets/Generated/" + fileName;
            Texture2D texture = new(width, height, TextureFormat.RGBA32, false, true);
            try
            {
                Color[] pixels = new Color[width * height];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                        pixels[(y * width) + x] = color(x, y);
                }

                texture.SetPixels(pixels);
                texture.Apply();
                byte[] png = texture.EncodeToPNG();
                File.WriteAllBytes(Path.Combine(_directory, name + "-source.png"), png);
                string project = Directory.GetParent(Application.dataPath)!.FullName;
                File.WriteAllBytes(Path.Combine(project, "Assets", "Generated", fileName), png);
                OwnedAssets.Add(assetPath);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                return assetPath;
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        internal static Color[] Capture(Material material, Vector3 leftNormal, Vector3 rightNormal, float leftHeight, float rightHeight, string name, int width = Width, bool savePixels = true)
        {
            Vector3[] normals = { leftNormal, rightNormal, leftNormal, rightNormal };
            Vector2[] uvs = { new Vector2(0f, leftHeight), new Vector2(1f, rightHeight), new Vector2(0f, leftHeight), new Vector2(1f, rightHeight) };
            Color[] pixels = CaptureQuad(material, normals, uvs, width, Height);
            if (savePixels)
                SavePixels(name, pixels);
            return pixels;
        }

        internal static Color[] CaptureQuad(Material material, Vector3[] normals, Vector2[] uvs, int width, int height)
        {
            Mesh mesh = new();
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            try
            {
                mesh.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f) };
                mesh.normals = normals;
                mesh.uv = uvs;
                mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                RenderTexture.active = target;
                GL.Clear(true, true, new Color(-2f, -2f, -2f, -2f));
                int pass = material.FindPass("FERROFLUID_SHADING_VALIDATION");
                if (pass < 0 || !material.SetPass(pass))
                    throw new Exception("The shared surface shading pass is unavailable.");

                Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
                return FerrofluidValidationRuntime.Read(target);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(mesh);
            }
        }

        private static void SavePixels(string name, Color[] pixels)
        {
            using StreamWriter writer = new(Path.Combine(_directory, name + ".csv"));
            writer.WriteLine("pixel,r,g,b");
            for (int index = 0; index < pixels.Length; index++)
            {
                Color pixel = pixels[index];
                if (float.IsNaN(pixel.r) || float.IsInfinity(pixel.r)
                    || float.IsNaN(pixel.g) || float.IsInfinity(pixel.g)
                    || float.IsNaN(pixel.b) || float.IsInfinity(pixel.b) || pixel.a < -1f)
                {
                    throw new Exception($"Shading probe {name} returned non-finite or unrendered pixels.");
                }

                writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2:R},{3:R}", index, pixel.r, pixel.g, pixel.b));
            }

            Texture2D image = new(pixels.Length / Height, Height, TextureFormat.RGBA32, false, true);
            try
            {
                Color[] display = new Color[pixels.Length];
                for (int index = 0; index < pixels.Length; index++)
                {
                    display[index] = pixels[index].gamma;
                    display[index].a = 1f;
                }

                image.SetPixels(display);
                image.Apply();
                File.WriteAllBytes(Path.Combine(_directory, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }
    }
}
