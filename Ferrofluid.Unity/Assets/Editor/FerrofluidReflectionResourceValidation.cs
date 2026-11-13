using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidReflectionResourceValidation
    {
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidReflectionResourceValidation));

        private static IEnumerator RunRuntime() => Validate(false);

        internal static IEnumerator Validate(bool includeHigh)
        {
            Assembly plugin = FerrofluidValidationRuntime.CreateConfig().GetType().Assembly;
            Type type = plugin.GetType("ChromaGLS.Settings.FerrofluidGpuResources", true);
            object[] arguments = { null };
            FerrofluidValidationRuntime.ConfigureReflectionPack();
            Check((bool)type.GetMethod("TryGet", Static).Invoke(null, arguments), "The core shaders and lookup textures must load.");
            object resources = arguments[0];
            Check(Get("RoomEnvironmentOn") == null && Get("RoomEnvironmentOff") == null && Get("FloorReflectionTexture") == null, "Loading core shaders must not eagerly allocate room or feet maps.");
            Type qualityType = plugin.GetType("ChromaGLS.Settings.ReflectionQuality", true);
            MethodInfo load = type.GetMethod("TryLoadReflectionsAsync", Instance);
            MethodInfo release = type.GetMethod("ReleaseReflectionAssets", Static);
            Check(load != null && release != null, "The menu lifecycle must explicitly own reflection resource loading and release.");
            Task<bool> loadTask;
            int cameras = 0;
            Camera.CameraCallback hook = _ => cameras++;
            Camera.onPostRender += hook;
            try
            {
                loadTask = Load("None");
                while (!loadTask.IsCompleted)
                    yield return null;
                Check(loadTask.Result, "None must succeed without loading reflection maps.");
                Check(Get("RoomEnvironmentOn") == null && Get("RoomEnvironmentOff") == null && Get("FloorReflectionTexture") == null, "None must keep all HDR reflection assets unallocated.");
                loadTask = Load("Low");
                while (!loadTask.IsCompleted)
                    yield return null;
                Check(loadTask.Result, "The existing Low maps must load.");
                Cubemap on = (Cubemap)Get("RoomEnvironmentOn");
                Cubemap off = (Cubemap)Get("RoomEnvironmentOff");
                Texture2D feet = (Texture2D)Get("FloorReflectionTexture");
                Check(on != null && off != null && feet != null && on.width == 1024 && off.width == 1024 && feet.width == 1024, "Low must preserve all existing 1024 reflection resources.");
                Check(!on.isReadable && !off.isReadable && !feet.isReadable, "Runtime reflection textures must not retain CPU-readable copies.");
                loadTask = Load("Low");
                while (!loadTask.IsCompleted)
                    yield return null;
                Check(loadTask.Result && ReferenceEquals(on, Get("RoomEnvironmentOn")) && ReferenceEquals(feet, Get("FloorReflectionTexture")), "Loading the same tier must reuse its assets.");
                Vector4 bounds = (Vector4)type.GetProperty("FloorReflectionBounds", Instance).GetValue(resources);
                Check(bounds == new Vector4(-2f, -2f, 0.25f, 0.25f), "The feet map must retain its position-correct four-metre footprint.");
                release.Invoke(null, null);
                Check(Get("RoomEnvironmentOn") == null && Get("RoomEnvironmentOff") == null && Get("FloorReflectionTexture") == null, "Releasing reflections must clear owned references.");
                Check(on == null && off == null && feet == null, "Releasing reflections must unload the native textures, not just clear shader globals.");
                ValidateCoreSurvivesRelease();
                loadTask = Load("Low");
                while (!loadTask.IsCompleted)
                    yield return null;
                Check(loadTask.Result && Get("RoomEnvironmentOn") != null, "The retained bundle must support reloading reflections after a tier or menu lifecycle change.");
                loadTask = Load("None");
                while (!loadTask.IsCompleted)
                    yield return null;
                Check(loadTask.Result, "Switching back to None must release the selected maps.");
                Check(Get("RoomEnvironmentOn") == null && Get("FloorReflectionTexture") == null && cameras == 0, "Resource selection must neither retain None's maps nor capture the scene.");
                ValidateCoreSurvivesRelease();
                if (includeHigh)
                {
                    loadTask = Load("High");
                    while (!loadTask.IsCompleted)
                        yield return null;
                    Check(loadTask.Result, "High must load genuine separately baked assets.");
                    Cubemap highOn = Get("RoomEnvironmentOn") as Cubemap;
                    Cubemap highOff = Get("RoomEnvironmentOff") as Cubemap;
                    Texture2D highFeet = Get("FloorReflectionTexture") as Texture2D;
                    Check(highOn != null && highOn.width == 2048 && highOff != null && highOff.width == 2048
                        && highFeet != null && highFeet.width == 4096 && highFeet.height == 4096, "High must select the 2048 room pair and native 4096 feet, not Low's maps.");
                    loadTask = Load("High");
                    while (!loadTask.IsCompleted)
                        yield return null;
                    Check(loadTask.Result && ReferenceEquals(highOn, Get("RoomEnvironmentOn")) && ReferenceEquals(highFeet, Get("FloorReflectionTexture")), "Repeated High selection must retain the existing assets.");
                    loadTask = Load("Low");
                    while (!loadTask.IsCompleted)
                        yield return null;
                    Check(loadTask.Result, "Changing High to Low must reload the existing Low assets.");
                    Check(highOn == null && highOff == null && highFeet == null && Get("RoomEnvironmentOn").width == 1024, "Low must unload High's native textures before selecting its own maps.");
                    Cubemap lowOn = Get("RoomEnvironmentOn") as Cubemap;
                    loadTask = Load("High");
                    while (!loadTask.IsCompleted)
                        yield return null;
                    Check(loadTask.Result && lowOn == null, "Returning to High must unload the Low map rather than retaining both tiers.");
                    loadTask = Load("None");
                    while (!loadTask.IsCompleted)
                        yield return null;
                    Check(loadTask.Result && Get("RoomEnvironmentOn") == null && Get("FloorReflectionTexture") == null && cameras == 0, "None must unload High completely without rendering a scene.");
                    ValidateCoreSurvivesRelease();
                    Debug.Log("[FerrofluidHighReflectionResourceValidation] PASS: High reuse, High/Low/High/None ownership transitions and core survival.");
                }
                Debug.Log("[FerrofluidReflectionResourceValidation] PASS: lazy core loading, None residency, unchanged Low assets, same-tier reuse, explicit unload/reload and zero scene renders.");
            }
            finally
            {
                release.Invoke(null, null);
                Camera.onPostRender -= hook;
            }

            Texture Get(string name) => type.GetProperty(name, Instance).GetValue(resources) as Texture;
            Task<bool> Load(string quality) => (Task<bool>)load.Invoke(resources, new[] { Enum.Parse(qualityType, quality) });

            void ValidateCoreSurvivesRelease()
            {
                Shader surface = type.GetProperty("SurfaceShader", Instance).GetValue(resources) as Shader;
                Shader simulation = type.GetProperty("SimulationShader", Instance).GetValue(resources) as Shader;
                Check(surface != null && surface.isSupported && simulation != null && simulation.isSupported && Get("ThinFilmTexture") != null && Get("EnvironmentTexture") != null, "Releasing a reflection bundle must not destroy the core shaders or lookup textures.");
                Material material = new(surface);
                bool room = Shader.IsKeywordEnabled("FERROFLUID_ROOM_REFLECTIONS");
                bool none = Shader.IsKeywordEnabled("FERROFLUID_NO_REFLECTIONS");
                try
                {
                    Shader.DisableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                    Shader.DisableKeyword("FERROFLUID_NO_REFLECTIONS");
                    material.SetTexture("_EnvironmentTex", Texture2D.whiteTexture);
                    material.SetTexture("_ThinFilmTex", Texture2D.blackTexture);
                    material.SetFloat("_ValidationOutput", 4f);
                    material.SetFloat("_Reflectivity", 1f);
                    material.SetVector("_FerrofluidReflectionStrengths", Vector4.one);
                    material.SetVector("_ValidationWorldPosition", new Vector3(0f, 1f, 0f));
                    material.SetVector("_ValidationCameraPosition", new Vector3(0f, 2f, 0f));
                    Color[] pixels = FerrofluidShadingValidation.Capture(material, Vector3.up, Vector3.up, 0f, 0f, "core-after-reflection-release", 16, false);
                    Check(pixels[pixels.Length / 2].maxColorComponent > 0.1f, "The embedded core shader must still draw after the reflection bundle is destroyed.");
                }
                finally
                {
                    Object.DestroyImmediate(material);
                    if (room)
                        Shader.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                    if (none)
                        Shader.EnableKeyword("FERROFLUID_NO_REFLECTIONS");
                }
            }
        }

        private static void Check(bool value, string message)
        {
            if (!value)
                throw new InvalidOperationException(message);
        }
    }
}
