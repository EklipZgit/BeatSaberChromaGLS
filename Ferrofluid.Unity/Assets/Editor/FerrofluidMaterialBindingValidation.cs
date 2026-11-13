using System;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidMaterialBindingValidation
    {
        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidMaterialBindingValidation));

        private static void RunRuntime()
        {
            foreach (string shape in new[] { "RoundedCube", "Sphere" })
            {
                object config = FerrofluidValidationRuntime.CreateConfig();
                Set("FerrofluidBaseRed", 0.2f);
                Set("FerrofluidBaseGreen", 0.3f);
                Set("FerrofluidBaseBlue", 0.4f);
                Set("FerrofluidRoomReflectionStrength", 0.5f);
                Set("FerrofluidFeetReflectionStrength", 1.5f);
                Set("FerrofluidParabolaReflectionStrength", 2.5f);
                Set("FerrofluidParabolaReflectionSaturation", 0.6f);
                Set("FerrofluidThinFilmStrength", 0.35f);
                using FerrofluidValidationRuntime runtime = new(shape, config);
                Material material = (Material)runtime.Get("_surfaceMaterial");
                Color expected = new Color(0.2f, 0.3f, 0.4f).linear;
                CheckVector(
                    material.GetVector("_FerrofluidBaseColor"),
                    new Vector4(expected.r, expected.g, expected.b, 0f),
                    shape + " base RGB must reach the material in linear space",
                    3);
                CheckVector(
                    material.GetVector("_FerrofluidReflectionStrengths"),
                    new Vector4(0.5f, 1.5f, 2.5f, 0.6f),
                    shape + " must bind all source strengths and saturation independently");
                if (Mathf.Abs(material.GetFloat("_FerrofluidThinFilmStrength") - 0.35f) > 0.00001f)
                    throw new Exception(shape + " must bind optional thin-film strength.");

                Texture homes = (Texture)runtime.Get("_restPositionTexture");
                Set("FerrofluidBaseRed", 0.8f);
                Set("FerrofluidParabolaReflectionStrength", 3.5f);
                runtime.Apply(config);
                if (!ReferenceEquals(homes, runtime.Get("_restPositionTexture")))
                    throw new Exception(shape + " must not rebake or reinstall homes for colour-only edits.");

                expected = new Color(0.8f, 0.3f, 0.4f).linear;
                CheckVector(
                    material.GetVector("_FerrofluidBaseColor"),
                    new Vector4(expected.r, expected.g, expected.b, 0f),
                    shape + " colour previews must update the live material",
                    3);
                CheckVector(
                    material.GetVector("_FerrofluidReflectionStrengths"),
                    new Vector4(0.5f, 1.5f, 3.5f, 0.6f),
                    shape + " reflection previews must update without touching the other sources");

                void Set(string name, float value)
                    => FerrofluidValidationRuntime.SetConfig(config, name, value);
            }

            Debug.Log("[FerrofluidMaterialBindingValidation] PASS: both runtime shapes bind linear base RGB, independent reflection controls and optional film without rebaking homes.");
        }

        private static void CheckVector(Vector4 actual, Vector4 expected, string message, int components = 4)
        {
            for (int component = 0; component < components; component++)
            {
                if (float.IsNaN(actual[component]) || Mathf.Abs(actual[component] - expected[component]) > 0.00001f)
                    throw new Exception($"{message}: expected {expected}, actual {actual}.");
            }
        }
    }
}
