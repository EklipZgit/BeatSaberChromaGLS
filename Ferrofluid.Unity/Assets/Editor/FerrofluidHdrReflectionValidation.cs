using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidHdrReflectionValidation
    {
        private const float Tolerance = 0.002f;
        private static readonly string[] TextureNames =
        {
            "_FerrofluidRoomEnvironment",
            "_FerrofluidFloorReflection",
            "_FerrofluidParabolaReflection",
        };
        private static readonly string[] FloatNames =
        {
            "_FerrofluidFloorReflectionY",
            "_FerrofluidParabolaReflectionY",
            "_FerrofluidParabolaReflectionEnabled",
        };
        private static readonly string[] VectorNames =
        {
            "_FerrofluidFloorReflectionBounds",
            "_FerrofluidParabolaReflectionBounds",
        };

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidHdrReflectionValidation));

        private static void RunRuntime()
        {
            string directory = FerrofluidValidationArtifacts.CreateRunDirectory("hdr-reflection");
            Debug.Log($"[FerrofluidHdrReflectionValidation] captureDirectory={directory}");
            bool roomKeyword = Shader.IsKeywordEnabled("FERROFLUID_ROOM_REFLECTIONS");
            Texture[] previousTextures = Array.ConvertAll(TextureNames, Shader.GetGlobalTexture);
            float[] previousFloats = Array.ConvertAll(FloatNames, Shader.GetGlobalFloat);
            Vector4[] previousVectors = Array.ConvertAll(VectorNames, Shader.GetGlobalVector);
            Material material = new(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader"));
            Cubemap room = new(16, TextureFormat.RGBAFloat, false);
            using StreamWriter report = new(Path.Combine(directory, "samples.csv"));
            report.WriteLine("case,pixel,deltaR,deltaG,deltaB,expectedR,expectedG,expectedB");
            try
            {
                Shader.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                Shader.SetGlobalTexture(TextureNames[0], room);
                Shader.SetGlobalTexture(TextureNames[1], Texture2D.blackTexture);
                Shader.SetGlobalTexture(TextureNames[2], Texture2D.blackTexture);
                Shader.SetGlobalFloat(FloatNames[0], -1f);
                Shader.SetGlobalFloat(FloatNames[1], -1f);
                Shader.SetGlobalFloat(FloatNames[2], 0f);
                foreach (string name in VectorNames)
                    Shader.SetGlobalVector(name, new Vector4(-2f, -2f, 0.25f, 0.25f));

                material.SetTexture("_ThinFilmTex", Texture2D.blackTexture);
                material.SetTexture("_EnvironmentTex", Texture2D.blackTexture);
                material.SetFloat("_FadeAlpha", 1f);
                material.SetFloat("_Exposure", 1f);
                material.SetFloat("_EmissionStrength", 0f);
                material.SetVector("_FerrofluidBaseColor", Vector4.zero);
                material.SetVector("_FerrofluidReflectionStrengths", Vector4.one);
                material.SetFloat("_FerrofluidThinFilmStrength", 0f);
                material.SetFloat("_ThinFilmScale", 1f);
                material.SetFloat("_ThinFilmOffset", 0f);
                material.SetFloat("_ValidationOutput", 0f);
                material.SetVector("_ValidationWorldOffset", Vector3.zero);
                material.SetVector("_ValidationCameraPosition", Vector3.forward);

                foreach (float shape in new[] { 1f, 2f })
                {
                    material.SetFloat("_SurfaceShapeMode", shape);
                    foreach (Color source in new[] { Color.white, Color.red, Color.green, Color.blue })
                    {
                        ValidateRoom(
                            material,
                            room,
                            report,
                            $"shape-{shape}-front-{source}",
                            source,
                            Vector3.forward,
                            0.25f,
                            source * 0.25f);
                    }

                    ValidateRoom(
                        material,
                        room,
                        report,
                        $"shape-{shape}-zero-reflectivity",
                        Color.white,
                        Vector3.forward,
                        0f,
                        Color.black);
                    ValidateRoom(
                        material,
                        room,
                        report,
                        $"shape-{shape}-grazing-hdr",
                        Color.white,
                        Vector3.right,
                        1f,
                        Color.white * 2.62f);
                    Vector3 oblique = new(Mathf.Sqrt(0.75f), 0f, 0.5f);
                    ValidateRoom(
                        material,
                        room,
                        report,
                        $"shape-{shape}-fixed-fresnel",
                        Color.white,
                        oblique,
                        0.25f,
                        Color.white * 0.26265625f);

                    Fill(room, Color.black);
                    material.SetFloat("_Reflectivity", 0.25f);
                    Vector3 floorNormal = new(0f, -Mathf.Sqrt(0.5f), Mathf.Sqrt(0.5f));
                    float floorWeight = 0.25f * (1f + (1.62f * Mathf.Pow(1f - floorNormal.z, 5f)));
                    foreach (int source in new[] { 1, 2 })
                    {
                        Shader.SetGlobalFloat(FloatNames[2], source == 2 ? 1f : 0f);
                        Color[] black = Capture(material, floorNormal);
                        Shader.SetGlobalTexture(TextureNames[source], Texture2D.whiteTexture);
                        Color[] white = Capture(material, floorNormal);
                        ValidateDelta(
                            report,
                            $"shape-{shape}-planar-source-{source}",
                            black,
                            white,
                            Color.white * floorWeight);
                        Shader.SetGlobalTexture(TextureNames[source], Texture2D.blackTexture);
                    }
                }
            }
            finally
            {
                for (int index = 0; index < TextureNames.Length; index++)
                    Shader.SetGlobalTexture(TextureNames[index], previousTextures[index]);

                for (int index = 0; index < FloatNames.Length; index++)
                    Shader.SetGlobalFloat(FloatNames[index], previousFloats[index]);

                for (int index = 0; index < VectorNames.Length; index++)
                    Shader.SetGlobalVector(VectorNames[index], previousVectors[index]);

                if (!roomKeyword)
                    Shader.DisableKeyword("FERROFLUID_ROOM_REFLECTIONS");

                Object.DestroyImmediate(material);
                Object.DestroyImmediate(room);
            }

            Debug.Log("[FerrofluidHdrReflectionValidation] PASS: cube/sphere neutral and RGB radiance, reflectivity zero, HDR without clipping, fixed Fresnel, and both planar sources.");
        }

        private static void ValidateRoom(
            Material material,
            Cubemap room,
            StreamWriter report,
            string name,
            Color source,
            Vector3 normal,
            float reflectivity,
            Color expected)
        {
            Shader.SetGlobalFloat(FloatNames[2], 0f);
            material.SetFloat("_Reflectivity", reflectivity);
            Fill(room, Color.black);
            Color[] black = Capture(material, normal);
            Fill(room, source);
            Color[] lit = Capture(material, normal);
            ValidateDelta(report, name, black, lit, expected);
        }

        internal static void Fill(Cubemap cubemap, Color color)
        {
            Color[] pixels = new Color[cubemap.width * cubemap.width];
            Array.Fill(pixels, color);
            for (int face = 0; face < 6; face++)
                cubemap.SetPixels(pixels, (CubemapFace)face);

            cubemap.Apply(false, false);
        }

        internal static Color[] Capture(Material material, Vector3 normal)
        {
            return FerrofluidShadingValidation.Capture(
                material,
                normal,
                normal,
                0.5f,
                0.5f,
                "hdr-reflection",
                8,
                false);
        }

        internal static void ValidateDelta(
            StreamWriter report,
            string name,
            Color[] black,
            Color[] lit,
            Color expected)
        {
            for (int pixel = 0; pixel < lit.Length; pixel++)
            {
                Color delta = lit[pixel] - black[pixel];
                report.WriteLine(FormattableString.Invariant(
                    $"\"{name}\",{pixel},{delta.r:R},{delta.g:R},{delta.b:R},{expected.r:R},{expected.g:R},{expected.b:R}"));
                float error = Mathf.Max(
                    Mathf.Abs(delta.r - expected.r),
                    Mathf.Max(Mathf.Abs(delta.g - expected.g), Mathf.Abs(delta.b - expected.b)));
                if (float.IsNaN(error) || float.IsInfinity(error) || error > Tolerance)
                    throw new Exception($"{name}: linear HDR reflection delta {delta} must match {expected}; maximum error {error:R}.");

                if (lit[pixel].a != 0f)
                    throw new Exception($"{name}: the HDR surface must retain zero bloom alpha.");
            }
        }
    }
}
