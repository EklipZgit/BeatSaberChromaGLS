using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidParabolicCacheValidation
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string TextureName = "_FerrofluidParabolaReflection";
        private const string BoundsName = "_FerrofluidParabolaReflectionBounds";
        private const string PlaneName = "_FerrofluidParabolaReflectionY";
        private const string EnabledName = "_FerrofluidParabolaReflectionEnabled";
        private static readonly string[] Fields =
        {
            "Brightness", "Red", "Green", "Blue", "WidthScale", "OffsetX", "OffsetZ", "Rotation",
            "LightWidthScale", "BloomWidthScale", "BloomBrightness", "HdrGlowThreshold", "HdrGlowWidthScale", "HdrGlowBrightness",
        };
        private static readonly float[] ChangedValues = { 0.9f, 1f, 2f, 5f, 1.6f, 0.8f, 0.5f, 36f, 2f, 2f, 3f, 5f, 0.4f, 3f };
        private static readonly string OutputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "Scratch", "ferro-parabola-reflection"));

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidParabolicCacheValidation));

        private static void RunRuntime()
        {
            Directory.CreateDirectory(OutputDirectory);
            Texture previousTexture = Shader.GetGlobalTexture(TextureName);
            Vector4 previousBounds = Shader.GetGlobalVector(BoundsName);
            float previousPlane = Shader.GetGlobalFloat(PlaneName);
            float previousEnabled = Shader.GetGlobalFloat(EnabledName);
            object owner = null;
            object ui = null;
            int cameraRenders = 0;
            Camera.CameraCallback cameraHook = _ => cameraRenders++;
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
                    Instance,
                    null,
                    new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                    null).Invoke(new object[] { new Vector3(0f, 0f, -0.5f), 2.5f, new Vector3(0f, 0f, -2f), Vector3.forward, 0f });
                Set(owner, "_enclosure", enclosure);
                Set(owner, "_playerPosition", Vector3.zero);
                Set(owner, "_baseForward", Vector3.forward);
                Set(owner, "_settings", saved);
                Set(owner, "_enabled", false);
                Set(owner, "_renderBloom", new Action<RenderTexture, RenderTexture>((src, dst) => Graphics.Blit(src, dst)));
                Invoke(owner, "InitializeReflectionCache", AssetDatabase.LoadAssetAtPath<Shader>("Assets/ParabolicFloorReflection.shader"));
                RenderTexture texture = Shader.GetGlobalTexture(TextureName) as RenderTexture;
                Check(texture != null, "Menu initialization must publish a procedural texture even while the feature is disabled.");
                Check(texture.width == 1024 && texture.height == 1024 && texture.depth == 0, "Cache must be 1024x1024 with no depth buffer.");
                Check(texture.format == RenderTextureFormat.RGB111110Float && texture.mipmapCount == 11, $"Cache must use packed HDR and 11 mip levels, got {texture.graphicsFormat}/{texture.mipmapCount}.");
                Check(texture.useMipMap && !texture.autoGenerateMips && texture.filterMode == FilterMode.Trilinear && texture.wrapMode == TextureWrapMode.Clamp, "Cache must have explicit mips and trilinear/clamp filtering.");
                Check(Shader.GetGlobalFloat(EnabledName) == 0f, "Initial disabled state must not contribute.");
                Color[] initial = Read();
                Vector4 bounds = Shader.GetGlobalVector(BoundsName);
                Check(Peak(initial) > 1f, "Packed HDR cache must retain radiance above one.");
                SaveImage("parabola-cache-initial.png", initial);
                Color coreTint = (Color)settingsType.GetMethod("EvaluateColor", Instance).Invoke(saved, null);
                Color glowTint = (Color)settingsType.GetMethod("EvaluateGlowColor", Instance).Invoke(saved, null);
                // The shader's Color property receives Unity's linearized material tint; vertex colours stay unconverted.
                Color coreRadiance = QualitySettings.activeColorSpace == ColorSpace.Linear ? coreTint.linear : coreTint;
                Color expectedApex = coreRadiance * coreTint.a + new Color(4f, 0.3f, 0.1f) * glowTint.a;
                Color apex = At(initial, bounds, new Vector3(0f, 0.006f, -0.35f));
                Check(Mathf.Abs(apex.r - expectedApex.r) < expectedApex.r * 0.04f, $"Core and broad spill must match source radiance, not include the bottom: {apex:F4} vs {expectedApex:F4}.");
                Check(apex.r > apex.g * 2f, "The saved red source must keep its hue.");
                Check(Mathf.Abs(Shader.GetGlobalFloat(PlaneName) - 0.006f) < 0.000001f, "Cache must use the core plane.");
                Invoke(owner, "SetEnabled", true);
                Check(Shader.GetGlobalFloat(EnabledName) == 1f, "Enabling must expose the already-generated cache.");
                Same(initial, Read(), "Enabling");
                Check(Shader.GetGlobalVector(BoundsName) == bounds, "Enabling must preserve mapping.");
                Type uiType = plugin.GetType("ChromaGLS.Settings.ParabolicFloorLightSettingsUI", true);
                ui = uiType.GetConstructor(Instance, null, new[] { config.GetType(), ownerType }, null).Invoke(new[] { config, owner });
                object session = uiType.GetField("_session", Instance).GetValue(ui);
                Invoke(session, "Open");
                foreach (string field in Fields)
                {
                    int index = Array.IndexOf(Fields, field);
                    Invoke(session, "Preview", With(saved, field, ChangedValues[index]));
                    Same(initial, Read(), "Preview " + field);
                    Check(Shader.GetGlobalVector(BoundsName) == bounds, "Preview must not move the cached coordinates.");
                }

                Invoke(ui, "HandleReset");
                Same(initial, Read(), "Reset");
                Invoke(ui, "HandleCancel");
                Same(initial, Read(), "Cancel");
                Check(Shader.GetGlobalVector(BoundsName) == bounds, "Reset/Cancel must preserve mapping.");
                foreach (string field in Fields)
                {
                    int index = Array.IndexOf(Fields, field);
                    object changed = With(saved, field, ChangedValues[index]);
                    Invoke(session, "Open");
                    Invoke(session, "Preview", changed);
                    Invoke(ui, "HandleSave");
                    Color[] updated = Read();
                    Vector4 updatedBounds = Shader.GetGlobalVector(BoundsName);
                    Check(Different(initial, updated) || updatedBounds != bounds, $"Saved {field} must affect pixels or world mapping.");
                    Check(ReferenceEquals(Shader.GetGlobalTexture(TextureName), texture), "Every Save and both shapes must share the same texture.");
                    Debug.Log($"[FerrofluidParabolicCacheValidation] saved-control={field} pixelsChanged={Different(initial, updated)} mappingChanged={updatedBounds != bounds}");
                }

                Invoke(session, "Open");
                Invoke(session, "Preview", With(With(saved, "OffsetX", 0.4f), "Rotation", 25f));
                Invoke(ui, "HandleSave");
                Color[] committed = Read();
                Vector4 committedBounds = Shader.GetGlobalVector(BoundsName);
                Check(committedBounds != bounds, "A saved offset/rotation must update the map.");
                Invoke(owner, "SetEnabled", false);
                Invoke(session, "Open");
                Invoke(session, "Preview", With(saved, "Blue", 6f));
                Invoke(ui, "HandleSave");
                Check(Shader.GetGlobalFloat(EnabledName) == 0f, "Save while off must not enable the light.");
                Color[] savedWhileOff = Read();
                Check(Different(committed, savedWhileOff), "Save while off must prepare the new saved source.");
                Invoke(owner, "SetEnabled", true);
                Same(savedWhileOff, Read(), "Re-enable after Save");
                foreach ((string field, float value) in new[] { ("Brightness", 0f), ("OffsetX", 6f) })
                {
                    Invoke(session, "Open");
                    Invoke(session, "Preview", With(saved, field, value));
                    Check(Shader.GetGlobalFloat(EnabledName) == 1f, "A dark/invalid preview must leave saved reflection active.");
                    Invoke(ui, "HandleSave");
                    Check(Shader.GetGlobalFloat(EnabledName) == 0f && Peak(Read()) == 0f, $"Saved {field}={value} must clear stale radiance.");
                    Invoke(session, "Open");
                    Invoke(session, "Preview", saved);
                    Invoke(ui, "HandleSave");
                }

                Invoke(session, "Open");
                Invoke(session, "Preview", With(With(With(saved, "Red", 0f), "Green", 0f), "Blue", 0f));
                Invoke(ui, "HandleSave");
                Check(Shader.GetGlobalFloat(EnabledName) == 0f && Peak(Read()) == 0f, "Saved black colour must remove the reflection.");
                Check(cameraRenders == 0, $"Initialization/Save/Preview/Reset/Cancel must render no scene cameras, got {cameraRenders}.");
                Debug.Log("[FerrofluidParabolicCacheValidation] PASS: 1024 packed HDR, 11 mips (~5.33 MiB), 14 controls, saved-only pixels/mapping, toggles and 0 scene-camera renders.");
                Camera.onPostRender -= cameraHook;
                Invoke(owner, "CommitReflection", saved);
                ValidateSurfaceShapes();
            }
            finally
            {
                Camera.onPostRender -= cameraHook;
                if (ui != null)
                    Invoke(ui, "Dispose");

                if (owner != null)
                    Invoke(owner, "Dispose");

                Shader.SetGlobalTexture(TextureName, previousTexture);
                Shader.SetGlobalVector(BoundsName, previousBounds);
                Shader.SetGlobalFloat(PlaneName, previousPlane);
                Shader.SetGlobalFloat(EnabledName, previousEnabled);
            }
        }

        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
        private static object Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Instance).Invoke(target, args);
        private static object With(object settings, string name, float value) => Invoke(settings, "With" + name, value);
        private static Color[] Read() => FerrofluidValidationRuntime.Read(Shader.GetGlobalTexture(TextureName));

        private static void ValidateSurfaceShapes()
        {
            Texture previousRoom = Shader.GetGlobalTexture("_FerrofluidRoomEnvironment");
            Texture previousFeet = Shader.GetGlobalTexture("_FerrofluidFloorReflection");
            bool previousKeyword = Shader.IsKeywordEnabled("FERROFLUID_ROOM_REFLECTIONS");
            float previousEnabled = Shader.GetGlobalFloat(EnabledName);
            Cubemap black = new(1, TextureFormat.RGBAHalf, false);
            GameObject cameraObject = new("Procedural reflection validation camera", typeof(Camera));
            RenderTexture frame = new(256, 256, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            try
            {
                for (int face = 0; face < 6; face++)
                    black.SetPixels(new[] { Color.black }, (CubemapFace)face);

                black.Apply();
                Shader.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", black);
                Shader.SetGlobalTexture("_FerrofluidFloorReflection", Texture2D.blackTexture);
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.enabled = false;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.fieldOfView = 30f;
                camera.cullingMask = 1 << 31;
                camera.transform.position = new Vector3(0f, 1.2f, -2.2f);
                camera.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0.8f, -0.1f) - camera.transform.position);
                camera.targetTexture = frame;
                frame.Create();
                foreach (string shape in new[] { "RoundedCube", "Sphere" })
                {
                    object config = FerrofluidValidationRuntime.CreateConfig();
                    config.GetType().GetProperty("FerrofluidSpikesEnabled").SetValue(config, false);
                    using FerrofluidValidationRuntime runtime = new(shape, config);
                    MeshRenderer body = (MeshRenderer)runtime.Get("_renderer");
                    body.transform.position = new Vector3(0f, 0.8f, -0.1f);
                    body.transform.localScale = Vector3.one * 0.55f;
                    body.gameObject.layer = 31;
                    body.enabled = true;
                    runtime.Set("_renderEnabled", true);
                    runtime.Set("_lastSimulationAt", Time.unscaledTime);
                    runtime.Set("_simulationAccumulator", 0f);
                    runtime.Invoke("UpdatePrimitiveFrame", 0.55f, Vector3.zero, Vector3.zero, 0f, new Vector3(0f, 0f, -1000f), 0f, Vector3.zero, Vector3.zero, 1f);
                    Shader.SetGlobalFloat(EnabledName, 0f);
                    camera.Render();
                    Color[] baseline = FerrofluidValidationRuntime.Read(frame);
                    Shader.SetGlobalFloat(EnabledName, 1f);
                    camera.Render();
                    Color[] reflected = FerrofluidValidationRuntime.Read(frame);
                    int changed = 0;
                    float redDelta = 0f;
                    float greenDelta = 0f;
                    float blueDelta = 0f;
                    for (int index = 0; index < baseline.Length; index++)
                    {
                        Color delta = reflected[index] - baseline[index];
                        Check(!float.IsNaN(delta.r) && !float.IsNaN(delta.g) && !float.IsNaN(delta.b), shape + " produced a non-finite reflection.");
                        if (delta.r > 0.000001f)
                            changed++;

                        redDelta += delta.r;
                        greenDelta += delta.g;
                        blueDelta += delta.b;
                    }

                    Check(changed > 16 && redDelta > (greenDelta + blueDelta) * 1.5f, $"{shape} must reflect the saved red strip/glow through FERROFLUID_SURFACE: {changed} pixels; RGB delta {redDelta:F5}/{greenDelta:F5}/{blueDelta:F5}.");
                    SaveImage("parabola-" + shape + "-off.png", baseline, 256);
                    SaveImage("parabola-" + shape + "-on.png", reflected, 256);
                    Debug.Log($"[FerrofluidParabolicCacheValidation] actual {shape} surface PASS: {changed} red reflection pixels, RGB delta {redDelta:F5}/{greenDelta:F5}/{blueDelta:F5}.");
                    body.enabled = false;
                }
            }
            finally
            {
                Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", previousRoom);
                Shader.SetGlobalTexture("_FerrofluidFloorReflection", previousFeet);
                Shader.SetGlobalFloat(EnabledName, previousEnabled);
                if (!previousKeyword)
                    Shader.DisableKeyword("FERROFLUID_ROOM_REFLECTIONS");

                Object.DestroyImmediate(cameraObject);
                frame.Release();
                Object.DestroyImmediate(frame);
                Object.DestroyImmediate(black);
            }
        }

        private static bool Different(Color[] left, Color[] right)
        {
            for (int index = 0; index < left.Length; index++)
            {
                if (!left[index].Equals(right[index]))
                    return true;
            }

            return false;
        }

        private static void Same(Color[] left, Color[] right, string label)
            => Check(!Different(left, right), label + " must not regenerate the saved reflection.");

        private static float Peak(Color[] pixels)
        {
            float peak = 0f;
            foreach (Color pixel in pixels)
                peak = Mathf.Max(peak, Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)));

            return peak;
        }

        private static Color At(Color[] pixels, Vector4 bounds, Vector3 world)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((world.x - bounds.x) * bounds.z * 1024), 0, 1023);
            int y = Mathf.Clamp(Mathf.FloorToInt((world.z - bounds.y) * bounds.w * 1024), 0, 1023);
            return pixels[y * 1024 + x];
        }

        private static void SaveImage(string name, Color[] pixels, int size = 1024)
        {
            Texture2D image = new(size, size, TextureFormat.RGBA32, false, true);
            try
            {
                Color[] display = new Color[pixels.Length];
                for (int index = 0; index < pixels.Length; index++)
                {
                    Color color = pixels[index];
                    color /= 1f + Mathf.Max(color.r, Mathf.Max(color.g, color.b));
                    display[index] = color.gamma;
                    display[index].a = 1f;
                }

                image.SetPixels(display);
                image.Apply();
                File.WriteAllBytes(Path.Combine(OutputDirectory, name), image.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }
    }
}
