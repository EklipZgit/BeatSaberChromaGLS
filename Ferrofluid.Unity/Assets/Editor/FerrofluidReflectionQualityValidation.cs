using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidReflectionQualityValidation
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string DisabledKeyword = "FERROFLUID_NO_REFLECTIONS";
        private const string RoomKeyword = "FERROFLUID_ROOM_REFLECTIONS";

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidReflectionQualityValidation));

        private static void RunRuntime()
        {
            ValidateNoneShader();
            ValidateCacheTiers();
            Debug.Log("[FerrofluidReflectionQualityValidation] PASS: None suppresses room and fallback reflection; Low/High/None cache transitions preserve saved planes, dimensions, AA and lifecycle.");
        }

        private static void ValidateNoneShader()
        {
            bool oldDisabled = Shader.IsKeywordEnabled(DisabledKeyword);
            bool oldRoom = Shader.IsKeywordEnabled(RoomKeyword);
            Texture oldCube = Shader.GetGlobalTexture("_FerrofluidRoomEnvironment");
            Texture oldFeet = Shader.GetGlobalTexture("_FerrofluidFloorReflection");
            Cubemap cube = new(4, TextureFormat.RGBAHalf, false);
            Material material = new(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader"));
            try
            {
                for (int face = 0; face < 6; face++)
                {
                    Color[] pixels = new Color[16];
                    for (int index = 0; index < pixels.Length; index++)
                        pixels[index] = new Color(2f, 1f, 0.5f, 1f);

                    cube.SetPixels(pixels, (CubemapFace)face);
                }

                cube.Apply(false);
                Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", cube);
                Shader.SetGlobalTexture("_FerrofluidFloorReflection", Texture2D.blackTexture);
                material.SetTexture("_EnvironmentTex", Texture2D.whiteTexture);
                material.SetVector("_FerrofluidReflectionStrengths", Vector4.one);
                material.SetFloat("_ValidationOutput", 4f);
                material.SetVector("_ValidationWorldPosition", new Vector3(0f, 1f, 0f));
                material.SetVector("_ValidationCameraPosition", new Vector3(0f, 2f, 0f));
                Shader.DisableKeyword(DisabledKeyword);
                Shader.EnableKeyword(RoomKeyword);
                Check(Peak(Sample("quality-room-control")) > 1f, "The room control must render nonzero HDR before testing None.");
                Shader.DisableKeyword(RoomKeyword);
                Shader.EnableKeyword(DisabledKeyword);
                Check(Peak(Sample("quality-none")) < 0.000001f, "None must suppress reflections instead of falling back to the old environment image.");
                Shader.DisableKeyword(DisabledKeyword);
                Check(Peak(Sample("quality-legacy-control")) > 0.1f, "The legacy fallback control must be nonzero so a black None render is meaningful.");
                Shader.EnableKeyword(DisabledKeyword);
                Check(Peak(Sample("quality-none-fallback")) < 0.000001f, "None must remain black with a white fallback texture bound.");

                Color[] Sample(string name)
                    => FerrofluidShadingValidation.Capture(material, Vector3.up, Vector3.up, 0f, 0f, name, 16, false);
            }
            finally
            {
                SetKeyword(DisabledKeyword, oldDisabled);
                SetKeyword(RoomKeyword, oldRoom);
                Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", oldCube);
                Shader.SetGlobalTexture("_FerrofluidFloorReflection", oldFeet);
                Object.DestroyImmediate(cube);
                Object.DestroyImmediate(material);
            }
        }

        private static void ValidateCacheTiers()
        {
            Assembly plugin = FerrofluidValidationRuntime.CreateConfig().GetType().Assembly;
            Type cacheType = plugin.GetType("ChromaGLS.Settings.MenuLightReflectionCache", true);
            Type qualityType = plugin.GetType("ChromaGLS.Settings.ReflectionQuality", true);
            Type definitionType = plugin.GetType("ChromaGLS.Settings.MenuLightDefinition", true);
            Type meshesType = plugin.GetType("ChromaGLS.Settings.MenuLightMeshes", true);
            Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true);
            MethodInfo qualityMethod = cacheType.GetMethod("SetQuality", Instance);
            Check(qualityMethod != null, "The cache must expose an event-driven quality transition.");
            object config = FerrofluidValidationRuntime.CreateConfig();
            Type storeType = plugin.GetType("ChromaGLS.Settings.MenuLightStore", true);
            object store = Activator.CreateInstance(storeType, Instance, null, new[] { config }, null);
            object definition = storeType.GetMethod("CreateDefault", Instance).Invoke(store, new[] { Enum.Parse(plugin.GetType("ChromaGLS.Settings.MenuLightType", true), "Square") });
            definitionType.GetProperty("Id").SetValue(definition, "quality-marker");
            object enclosure = Activator.CreateInstance(enclosureType, Instance, null, new object[] { Vector3.zero, 3f, new Vector3(0f, 0f, -3f), Vector3.forward, 0f }, null);
            object[] createArguments = { enclosure, Vector3.zero, Vector3.forward, definition, null };
            Check((bool)plugin.GetType("ChromaGLS.Settings.MenuLightGeometry", true).GetMethod("TryCreate", Static).Invoke(null, createArguments), "The saved square must create real bake geometry.");
            IList definitions = (IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(definitionType));
            definitions.Add(definition);
            IDictionary meshes = (IDictionary)Activator.CreateInstance(typeof(System.Collections.Generic.Dictionary<,>).MakeGenericType(typeof(string), meshesType));
            meshes.Add("quality-marker", createArguments[4]);
            int calls = 0;
            int expectedResolution = 1024;
            int cameras = 0;
            Camera.CameraCallback hook = _ => cameras++;
            object cache = null;
            RenderTexture oldActive = RenderTexture.active;
            bool oldSrgb = GL.sRGBWrite;
            Texture oldArray = Shader.GetGlobalTexture("_FerrofluidMenuLightReflections");
            float oldCount = Shader.GetGlobalFloat("_FerrofluidMenuLightCount");
            float oldLegacyEnabled = Shader.GetGlobalFloat("_FerrofluidParabolaReflectionEnabled");
            Matrix4x4[] oldMatrices = Shader.GetGlobalMatrixArray("_FerrofluidMenuLightWorldToLocal");
            Vector4[] oldBounds = Shader.GetGlobalVectorArray("_FerrofluidMenuLightBounds");
            Vector4[] oldFlags = Shader.GetGlobalVectorArray("_FerrofluidMenuLightFlags");
            Camera.onPostRender += hook;
            try
            {
                Action<RenderTexture, RenderTexture> post = (source, destination) =>
                {
                    calls++;
                    Check(source.width == expectedResolution && source.height == expectedResolution, "The HDR staging image must use the selected resolution.");
                    Check(destination.width == expectedResolution && destination.height == expectedResolution, "The postprocessed target must match the selected resolution.");
                    Check(source.format == RenderTextureFormat.ARGBHalf && destination.format == RenderTextureFormat.RGB111110Float, "Quality must preserve the native HDR payload and packed cache format.");
                    Material bake = (Material)cacheType.GetField("_material", Instance).GetValue(cache);
                    Vector4 sample = bake.GetVector("_BakeSample");
                    Check(Mathf.Abs(sample.x - 0.125f / expectedResolution) < 0.0000001f && Mathf.Abs(sample.y - 0.375f / expectedResolution) < 0.0000001f && sample.z == 0.25f, "Bake jitter must remain subpixel at both resolutions, with quarter radiance weight.");
                    RenderTexture.active = destination;
                    GL.Clear(false, true, new Color(2f, 0.25f, 0.125f, 1f));
                };
                cache = Activator.CreateInstance(cacheType, Instance, null, new object[] { AssetDatabase.LoadAssetAtPath<Shader>("Assets/ParabolicFloorReflection.shader"), post }, null);
                Synchronize();
                Check(calls == 1, "The initial Low cache must bake its saved light once.");
                RenderTexture low = Current();
                Check(low != null && low.width == 1024 && low.mipmapCount == 11 && low.volumeDepth == 1, "Low must preserve the current 1024 packed-HDR array with full mips.");
                Matrix4x4[] planes = Shader.GetGlobalMatrixArray("_FerrofluidMenuLightWorldToLocal");
                Vector4[] bounds = Shader.GetGlobalVectorArray("_FerrofluidMenuLightBounds");
                expectedResolution = 8192;
                Change("High");
                Synchronize();
                RenderTexture high = Current();
                Check(high != null && high != low && high.width == 8192 && high.mipmapCount == 14 && high.volumeDepth == 1 && high.format == RenderTextureFormat.RGB111110Float, "High must allocate a real 8192 packed-HDR array, not upscale the old cache.");
                Check(low == null || !low.IsCreated(), "A tier change must release the old array instead of retaining both resolutions.");
                Check(calls == 2, "High must regenerate exactly the saved slice once.");
                Same(planes, Shader.GetGlobalMatrixArray("_FerrofluidMenuLightWorldToLocal"));
                Vector4[] highBounds = Shader.GetGlobalVectorArray("_FerrofluidMenuLightBounds");
                Check(bounds[0] == highBounds[0], "Changing quality must not change the world footprint.");
                Change("High");
                Synchronize();
                Check(ReferenceEquals(high, Current()) && calls == 2, "Selecting the same tier must not allocate or rebake.");
                Change("None");
                Synchronize();
                Check(Current() == null && Shader.GetGlobalFloat("_FerrofluidMenuLightCount") == 0f, "None must clear storage and count.");
                Check(high == null || !high.IsCreated(), "None must release the High array.");
                Check(calls == 2, "None must perform no postprocessing.");
                expectedResolution = 1024;
                Change("Low");
                Synchronize();
                Check(Current() != null && Current().width == 1024 && calls == 3, "Re-enabling Low must rebuild the saved source exactly once.");
                Check(cameras == 0, "Tier changes must never render scene cameras.");

                void Change(string quality) => qualityMethod.Invoke(cache, new[] { Enum.Parse(qualityType, quality) });
                void Synchronize() => cacheType.GetMethod("Synchronize", Instance).Invoke(cache, new object[] { definitions, meshes, true });
            }
            finally
            {
                if (cache != null)
                    ((IDisposable)cache).Dispose();
                Camera.onPostRender -= hook;
                RenderTexture.active = oldActive;
                GL.sRGBWrite = oldSrgb;
                Shader.SetGlobalTexture("_FerrofluidMenuLightReflections", oldArray);
                Shader.SetGlobalFloat("_FerrofluidMenuLightCount", oldCount);
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionEnabled", oldLegacyEnabled);
                if (oldMatrices != null && oldMatrices.Length > 0)
                    Shader.SetGlobalMatrixArray("_FerrofluidMenuLightWorldToLocal", oldMatrices);
                if (oldBounds != null && oldBounds.Length > 0)
                    Shader.SetGlobalVectorArray("_FerrofluidMenuLightBounds", oldBounds);
                if (oldFlags != null && oldFlags.Length > 0)
                    Shader.SetGlobalVectorArray("_FerrofluidMenuLightFlags", oldFlags);
            }
        }

        private static RenderTexture Current()
            => Shader.GetGlobalTexture("_FerrofluidMenuLightReflections") as RenderTexture;

        private static void Same(Matrix4x4[] expected, Matrix4x4[] actual)
        {
            for (int slot = 0; slot < expected.Length; slot++)
            {
                for (int component = 0; component < 16; component++)
                    Check(Mathf.Abs(expected[slot][component] - actual[slot][component]) < 0.000001f, "Quality must preserve every saved projection plane.");
            }
        }

        private static float Peak(Color[] pixels)
        {
            Check(pixels.Length > 0, "The shader must draw pixels.");
            float peak = 0f;
            foreach (Color pixel in pixels)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    Check(!float.IsNaN(pixel[channel]) && !float.IsInfinity(pixel[channel]), "Reflection output must remain finite.");
                    peak = Mathf.Max(peak, pixel[channel]);
                }
            }

            return peak;
        }

        private static void SetKeyword(string keyword, bool enabled)
        {
            if (enabled)
                Shader.EnableKeyword(keyword);
            else
                Shader.DisableKeyword(keyword);
        }

        private static void Check(bool value, string message)
        {
            if (!value)
                throw new InvalidOperationException(message);
        }
    }
}
