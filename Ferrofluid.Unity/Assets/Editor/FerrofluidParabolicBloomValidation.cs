using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidParabolicBloomValidation
    {
        private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidParabolicBloomValidation));

        private static void RunRuntime()
        {
            Texture oldTexture = Shader.GetGlobalTexture("_FerrofluidParabolaReflection");
            Vector4 oldBounds = Shader.GetGlobalVector("_FerrofluidParabolaReflectionBounds");
            float oldPlane = Shader.GetGlobalFloat("_FerrofluidParabolaReflectionY");
            float oldEnabled = Shader.GetGlobalFloat("_FerrofluidParabolaReflectionEnabled");
            object owner = null;
            int calls = 0;
            int cameraRenders = 0;
            Camera.CameraCallback cameraHook = _ => cameraRenders++;
            Material effect = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/ParabolicBloomValidation.shader"));
            Camera.onPostRender += cameraHook;
            try
            {
                object config = FerrofluidValidationRuntime.CreateConfig();
                FerrofluidValidationRuntime.SetConfig(config, "ParabolicFloorLightBrightness", 0.6f);
                FerrofluidValidationRuntime.SetConfig(config, "ParabolicFloorLightRed", 4f);
                FerrofluidValidationRuntime.SetConfig(config, "ParabolicFloorLightGreen", 0.3f);
                FerrofluidValidationRuntime.SetConfig(config, "ParabolicFloorLightBlue", 0.1f);
                Assembly plugin = config.GetType().Assembly;
                Type ownerType = plugin.GetType("ChromaGLS.Settings.ParabolicFloorLight", true);
                Type settingsType = plugin.GetType("ChromaGLS.Settings.ParabolicFloorLightSettings", true);
                object saved = settingsType.GetMethod("FromConfig", Static).Invoke(null, new[] { config });
                owner = ownerType.GetConstructor(Instance, null, new[] { config.GetType() }, null).Invoke(new[] { config });
                Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true);
                object enclosure = enclosureType.GetConstructor(
                    Instance, null,
                    new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                    null).Invoke(new object[] { new Vector3(0f, 0f, -0.5f), 2.5f, new Vector3(0f, 0f, -2f), Vector3.forward, 0f });
                Set("_enclosure", enclosure);
                Set("_playerPosition", Vector3.zero);
                Set("_baseForward", Vector3.forward);
                Set("_settings", saved);
                Set("_enabled", false);
                Action<RenderTexture, RenderTexture> bloom = (source, destination) =>
                {
                    calls++;
                    Check(source.format == RenderTextureFormat.ARGBHalf, "Bloom input must retain the native HDR alpha payload.");
                    Color[] input = Read(source);
                    Check(input[0].a == 0f && input[0].maxColorComponent == 0f, "Empty bake pixels must be transparent black, not opaque bloom emitters.");
                    float alphaPeak = 0f;
                    foreach (Color pixel in input)
                        alphaPeak = Mathf.Max(alphaPeak, pixel.a);
                    Check(Mathf.Abs(alphaPeak - 0.36f) < 0.002f, $"Core bloom alpha must be brightness squared, not opacity or halo intensity: {alphaPeak:R}.");
                    Graphics.Blit(source, destination, effect);
                    Color[] output = Read(destination);
                    bool spread = false;
                    float peak = 0f;
                    for (int index = 0; index < output.Length; index++)
                    {
                        spread |= input[index].maxColorComponent == 0f && output[index].maxColorComponent > 0.00001f;
                        peak = Mathf.Max(peak, output[index].maxColorComponent);
                    }
                    Check(spread, "The HDR cache must contain bloom radiance outside the source geometry.");
                    Check(peak > 1f, "Bloom processing must not clamp HDR radiance.");
                };
                // The old implementation has no processor seam and must fail on the call assertion.
                ownerType.GetField("_renderBloom", Instance)?.SetValue(owner, bloom);
                Invoke("InitializeReflectionCache", AssetDatabase.LoadAssetAtPath<Shader>("Assets/ParabolicFloorReflection.shader"));
                Check(calls == 1, $"Menu initialization must bake bloom into the saved HDR cache exactly once; got {calls} calls.");
                RenderTexture texture = Shader.GetGlobalTexture("_FerrofluidParabolaReflection") as RenderTexture;
                Check(texture != null && texture.format == RenderTextureFormat.RGB111110Float, "Published reflection must remain one packed HDR texture.");
                Color[] committed = Read(texture);
                Invoke("ApplySettings", saved);
                Invoke("SetEnabled", true);
                Invoke("SetEnabled", false);
                Check(calls == 1, "Preview and visibility changes must not regenerate bloom.");
                Invoke("CommitReflection", saved);
                Check(calls == 2 && ReferenceEquals(texture, Shader.GetGlobalTexture("_FerrofluidParabolaReflection")), "Save must regenerate bloom while reusing the shared texture.");
                Color[] repeated = Read(texture);
                for (int index = 0; index < repeated.Length; index++)
                    Check(repeated[index] == committed[index], "Repeated saved parameters must produce identical HDR pixels.");
                Check(cameraRenders == 0, "Bloom baking must not render a scene camera.");
                Debug.Log("[FerrofluidParabolicBloomValidation] PASS: transparent HDR source, native bloom alpha, cached spread/colour, load/Save only and zero camera renders.");

                void Set(string name, object value) => ownerType.GetField(name, Instance).SetValue(owner, value);
                void Invoke(string name, params object[] values) => ownerType.GetMethod(name, Instance).Invoke(owner, values);
            }
            finally
            {
                Camera.onPostRender -= cameraHook;
                if (owner != null)
                    owner.GetType().GetMethod("Dispose", Instance).Invoke(owner, null);
                Object.DestroyImmediate(effect);
                Shader.SetGlobalTexture("_FerrofluidParabolaReflection", oldTexture);
                Shader.SetGlobalVector("_FerrofluidParabolaReflectionBounds", oldBounds);
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionY", oldPlane);
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionEnabled", oldEnabled);
            }
        }

        private static Color[] Read(RenderTexture source)
        {
            RenderTexture previous = RenderTexture.active;
            Texture2D read = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = source;
                read.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
                read.Apply();
                return read.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(read);
            }
        }

        private static void Check(bool value, string message)
        {
            if (!value)
                throw new Exception(message);
        }
    }
}
