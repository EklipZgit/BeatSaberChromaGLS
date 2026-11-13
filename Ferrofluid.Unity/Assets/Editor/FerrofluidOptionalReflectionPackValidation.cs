using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // The embedded core bundle must stay small: room/feet reflection assets live in a
    // separate optional pack file loaded from disk. Missing pack must yield black
    // reflections (FERROFLUID_NO_REFLECTIONS), never the legacy environment fallback.
    internal static class FerrofluidOptionalReflectionPackValidation
    {
        private const string BundleName = "ChromaGLS.Settings.ferrofluid-windows.bundle";
        private const string RoomKeyword = "FERROFLUID_ROOM_REFLECTIONS";
        private const string DisabledKeyword = "FERROFLUID_NO_REFLECTIONS";
        private static readonly string[] ReflectionAssetNames =
        {
            "assets/generated/room-reflections-on.cubemap",
            "assets/generated/room-reflections-off.cubemap",
            "assets/generated/floor-feet.asset",
            "assets/generated/room-reflections.json",
            "assets/generated/room-reflections-high-on.cubemap",
            "assets/generated/room-reflections-high-off.cubemap",
            "assets/generated/floor-feet-high.asset",
            "assets/generated/room-reflections-high.json",
        };
        private static readonly string[] TextureNames =
        {
            "_FerrofluidRoomEnvironment", "_EnvironmentTex", "_FerrofluidFloorReflection", "_FerrofluidParabolaReflection",
        };
        private static readonly string[] VectorNames =
        {
            "_FerrofluidRoomCapturePosition", "_FerrofluidRoomLogoPlane", "_FerrofluidFloorReflectionBounds", "_FerrofluidParabolaReflectionBounds",
        };
        private static readonly string[] FloatNames =
        {
            "_FerrofluidRoomFloorY", "_FerrofluidFloorReflectionY", "_FerrofluidParabolaReflectionY",
            "_FerrofluidMenuLightCount", "_FerrofluidParabolaReflectionEnabled",
        };

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidOptionalReflectionPackValidation));

        private static IEnumerator RunRuntime()
        {
            Texture[] oldTextures = Array.ConvertAll(TextureNames, Shader.GetGlobalTexture);
            Vector4[] oldVectors = Array.ConvertAll(VectorNames, Shader.GetGlobalVector);
            float[] oldFloats = Array.ConvertAll(FloatNames, Shader.GetGlobalFloat);
            bool[] oldKeywords = Array.ConvertAll(new[] { RoomKeyword, DisabledKeyword }, Shader.IsKeywordEnabled);
            List<Object> owned = new();
            Assembly plugin = FerrofluidValidationRuntime.CreateConfig().GetType().Assembly;
            const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            Type roomType = plugin.GetType("ChromaGLS.Settings.FerrofluidRoomReflections", true);
            Type resourceType = plugin.GetType("ChromaGLS.Settings.FerrofluidGpuResources", true);
            Type qualityType = plugin.GetType("ChromaGLS.Settings.ReflectionQuality", true);
            try
            {
                // The pack descriptor must travel inside the small core DLL so the app can
                // locate and verify the content-addressed optional pack on disk.
                using Stream? descriptorStream = plugin.GetManifestResourceStream("ChromaGLS.Settings.ferrofluid-reflections-pack.json");
                if (descriptorStream == null)
                    throw new Exception("optional reflection pack descriptor missing");

                // The small core DLL must not carry the optional reflection assets.
                using Stream stream = plugin.GetManifestResourceStream(BundleName)
                    ?? throw new InvalidOperationException("The embedded core bundle must be present.");
                using MemoryStream bytes = new();
                stream.CopyTo(bytes);
                AssetBundle core = AssetBundle.LoadFromMemory(bytes.ToArray());
                if (core == null)
                    throw new Exception("Embedded core bundle failed to load.");
                try
                {
                    foreach (string name in ReflectionAssetNames)
                    {
                        if (core.Contains(name))
                            throw new Exception("core contains optional reflection maps: " + name);
                    }
                }
                finally
                {
                    core.Unload(true);
                }

                Debug.Log("[FerrofluidOptionalReflectionPackValidation] core bundle contains none of the 8 reflection assets");

                // The optional pack supplies every reflection asset from disk.
                AssetBundle? pack = FerrofluidValidationRuntime.LoadReflectionPack()
                    ?? throw new Exception("CHROMAGLS_REFLECTION_PACK must point at a loadable reflection pack.");
                try
                {
                    foreach (string name in ReflectionAssetNames)
                    {
                        if (!pack.Contains(name))
                            throw new Exception("reflection pack missing expected asset " + name);
                    }
                }
                finally
                {
                    pack.Unload(true);
                }

                Debug.Log("[FerrofluidOptionalReflectionPackValidation] pack contains all 8 reflection assets");

                MethodInfo quality = roomType.GetMethod("SetReflectionQuality", Flags);
                MethodInfo setPath = roomType.GetMethod("SetReflectionPackPath", Flags);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader");
                Material material = new(shader);
                owned.Add(material);
                material.SetFloat("_ValidationOutput", 4f);
                material.SetFloat("_SurfaceShapeMode", 2f);
                material.SetVector("_FerrofluidReflectionStrengths", new Vector4(1f, 1f, 0f, 1f));

                Texture2D white = new(2, 2, TextureFormat.RGBAFloat, false, true);
                white.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                white.Apply();
                owned.Add(white);
                material.SetTexture("_EnvironmentTex", white);

                // A fresh Low selection with no pack must disable reflections even though
                // it matches the default quality and skips the change branch.
                quality.Invoke(null, new[] { Enum.Parse(qualityType, "Low") });
                if (!Shader.IsKeywordEnabled(DisabledKeyword))
                    throw new Exception("initial Low missing pack must disable FERROFLUID_NO_REFLECTIONS.");
                Color initial = Sample("initial-low-black", material);
                CheckColor("initial-low-black", initial, Color.black);

                // No pack: the shader returns black under FERROFLUID_NO_REFLECTIONS even
                // though a bright legacy environment is bound (which would be visible if
                // the fallback leaked through).
                setPath.Invoke(null, new object?[] { null });
                quality.Invoke(null, new[] { Enum.Parse(qualityType, "High") });
                if (!Shader.IsKeywordEnabled(DisabledKeyword))
                    throw new Exception("missing pack with quality High must enable FERROFLUID_NO_REFLECTIONS.");
                Color actual = Sample("missing-pack-black", material);
                CheckColor("missing-pack-black", actual, Color.black);

                // Core material still renders: disable the keyword and the legacy env shows.
                Shader.DisableKeyword(DisabledKeyword);
                actual = Sample("core-env-render", material);
                CheckColor("core-env-render", actual, Color.white, 0.05f);
                Shader.EnableKeyword(DisabledKeyword);

                // A valid pack path restores on/off tier loading for the configured quality.
                FerrofluidValidationRuntime.ConfigureReflectionPack();
                object[] resourceArguments = { null };
                if (!(bool)resourceType.GetMethod("TryGet", Flags).Invoke(null, resourceArguments))
                    throw new Exception("Core bundle must load for pack-tier checks.");
                object resources = resourceArguments[0];
                const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                Task<bool> highLoad = (Task<bool>)resourceType.GetMethod("TryLoadReflectionsAsync", InstanceFlags).Invoke(resources, new[] { Enum.Parse(qualityType, "High") });
                while (!highLoad.IsCompleted)
                    yield return null;
                if (!highLoad.Result)
                    throw new Exception("Configured pack path must load High reflection assets.");
                object roomOn = resourceType.GetProperty("RoomEnvironmentOn", InstanceFlags).GetValue(resources);
                object roomOff = resourceType.GetProperty("RoomEnvironmentOff", InstanceFlags).GetValue(resources);
                if (roomOn == null || roomOff == null)
                    throw new Exception("Pack High load must provide both on and off cubemaps.");
                roomType.GetMethod("ApplyEnvironment", Flags).Invoke(null, new object[] { roomOn, roomOff, null });
                if (Shader.IsKeywordEnabled(DisabledKeyword) || !Shader.IsKeywordEnabled(RoomKeyword))
                    throw new Exception("Bound pack room must disable FERROFLUID_NO_REFLECTIONS and enable the room keyword.");
                actual = Sample("pack-bound-room", material);
                if (actual.maxColorComponent <= 0f)
                    throw new Exception("Bound pack room produced black radiance.");

                // A pending SDK High load superseded by None must not retain the pack bundle or textures.
                resourceType.GetMethod("ReleaseReflectionAssets", Flags).Invoke(null, null);
                HashSet<int> baselineBundles = GetBundleIds();
                MethodInfo loadAsync = resourceType.GetMethod("TryLoadReflectionsAsync", InstanceFlags);
                Task<bool> pendingHigh = (Task<bool>)loadAsync.Invoke(resources, new[] { Enum.Parse(qualityType, "High") });
                if (pendingHigh.IsCompleted)
                    throw new Exception("The SDK High load must be pending before changing quality.");
                quality.Invoke(null, new[] { Enum.Parse(qualityType, "None") });
                while (!pendingHigh.IsCompleted)
                    yield return null;
                if (pendingHigh.Result)
                    throw new Exception("Superseded pending High load must not publish textures.");
                foreach (string property in new[] { "RoomEnvironmentOn", "RoomEnvironmentOff", "FloorReflectionTexture" })
                {
                    if (resourceType.GetProperty(property, InstanceFlags).GetValue(resources) != null)
                        throw new Exception("None selection during pending load must clear " + property);
                }

                if (!baselineBundles.SetEquals(GetBundleIds()))
                    throw new Exception("Superseded pending load leaked a native AssetBundle.");
                if (!Shader.IsKeywordEnabled(DisabledKeyword))
                    throw new Exception("Superseded pending load must leave FERROFLUID_NO_REFLECTIONS enabled.");

                // A pending High load superseded by Low must resolve to the latest request only.
                quality.Invoke(null, new[] { Enum.Parse(qualityType, "High") });
                Task<bool> highPending = (Task<bool>)loadAsync.Invoke(resources, new[] { Enum.Parse(qualityType, "High") });
                if (highPending.IsCompleted)
                    throw new Exception("High must still be pending when the Low request supersedes it.");
                Task<bool> lowPending = (Task<bool>)loadAsync.Invoke(resources, new[] { Enum.Parse(qualityType, "Low") });
                while (!highPending.IsCompleted || !lowPending.IsCompleted)
                    yield return null;
                if (highPending.Result || !lowPending.Result)
                    throw new Exception("The latest Low request must complete after superseding High.");
                Cubemap surviving = resourceType.GetProperty("RoomEnvironmentOn", InstanceFlags).GetValue(resources) as Cubemap;
                if (surviving == null || surviving.width != 1024)
                    throw new Exception("Only the latest Low tier must survive a superseded High load.");
                HashSet<int> lowBundles = GetBundleIds();
                lowBundles.ExceptWith(baselineBundles);
                if (lowBundles.Count != 1)
                    throw new Exception("Low must retain exactly one native pack bundle after superseding High.");

                // Restore a bound High room for the remaining black-state checks.
                resourceType.GetMethod("ReleaseReflectionAssets", Flags).Invoke(null, null);
                Task<bool> rebind = (Task<bool>)loadAsync.Invoke(resources, new[] { Enum.Parse(qualityType, "High") });
                while (!rebind.IsCompleted)
                    yield return null;
                if (!rebind.Result)
                    throw new Exception("High reload after lifecycle checks must succeed.");
                roomType.GetMethod("ApplyEnvironment", Flags).Invoke(null, new object[]
                {
                    resourceType.GetProperty("RoomEnvironmentOn", InstanceFlags).GetValue(resources),
                    resourceType.GetProperty("RoomEnvironmentOff", InstanceFlags).GetValue(resources),
                    null,
                });

                // None and a cleared path both return to black with the keyword enabled.
                quality.Invoke(null, new[] { Enum.Parse(qualityType, "None") });
                actual = Sample("quality-none-black", material);
                CheckColor("quality-none-black", actual, Color.black);
                setPath.Invoke(null, new object?[] { null });
                quality.Invoke(null, new[] { Enum.Parse(qualityType, "High") });
                if (!Shader.IsKeywordEnabled(DisabledKeyword))
                    throw new Exception("Cleared pack path must restore FERROFLUID_NO_REFLECTIONS.");
                actual = Sample("cleared-path-black", material);
                CheckColor("cleared-path-black", actual, Color.black);

                // The verified file can disappear before Unity opens it. SDK failure must not
                // revive the white core environment or leave a partial reflection binding.
                string missingPackPath = FerrofluidValidationRuntime.ReflectionPackPath + ".missing-" + Guid.NewGuid().ToString("N");
                setPath.Invoke(null, new object[] { missingPackPath });
                Task<bool> missingLoad = (Task<bool>)loadAsync.Invoke(resources, new[] { Enum.Parse(qualityType, "High") });
                while (!missingLoad.IsCompleted)
                    yield return null;
                if (missingLoad.Result || !Shader.IsKeywordEnabled(DisabledKeyword) || Shader.IsKeywordEnabled(RoomKeyword))
                    throw new Exception("SDK file-load failure must keep reflections disabled without the old fallback.");
                CheckColor("sdk-load-failure-black", Sample("sdk-load-failure-black", material), Color.black);
                if (!baselineBundles.SetEquals(GetBundleIds()))
                    throw new Exception("SDK file-load failure must not retain a native pack bundle.");

                Debug.Log("[FerrofluidOptionalReflectionPackValidation] PASS core has no reflection assets, pack file loads all tiers, missing/cleared pack is black and safe.");
            }
            finally
            {
                roomType.GetMethod("EndMenuSession", Flags).Invoke(null, null);
                for (int index = 0; index < oldTextures.Length; index++)
                    Shader.SetGlobalTexture(TextureNames[index], oldTextures[index]);
                for (int index = 0; index < oldVectors.Length; index++)
                    Shader.SetGlobalVector(VectorNames[index], oldVectors[index]);
                for (int index = 0; index < oldFloats.Length; index++)
                    Shader.SetGlobalFloat(FloatNames[index], oldFloats[index]);
                string[] keywords = { RoomKeyword, DisabledKeyword };
                for (int index = 0; index < oldKeywords.Length; index++)
                {
                    if (oldKeywords[index])
                        Shader.EnableKeyword(keywords[index]);
                    else
                        Shader.DisableKeyword(keywords[index]);
                }

                foreach (Object instance in owned)
                    Object.DestroyImmediate(instance);
            }
        }

        private static HashSet<int> GetBundleIds()
        {
            HashSet<int> ids = new();
            foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
                ids.Add(bundle.GetInstanceID());
            return ids;
        }

        private static Color Sample(string name, Material material)
        {
            Vector3 origin = new(0f, 0.5f, 0f);
            Vector3 ray = new(0f, 0f, 1f);
            material.SetVector("_ValidationWorldPosition", origin);
            material.SetVector("_ValidationCameraPosition", origin + ray);
            Color[] pixels = FerrofluidShadingValidation.Capture(material, ray, ray, 0f, 0f, name, 8, false);
            Color actual = pixels[pixels.Length / 2];
            for (int channel = 0; channel < 3; channel++)
            {
                if (float.IsNaN(actual[channel]) || float.IsInfinity(actual[channel]))
                    throw new Exception(name + " produced non-finite radiance.");
            }

            Debug.Log("[FerrofluidOptionalReflectionPackValidation] " + name + " actual=" + actual.ToString("F6"));
            return actual;
        }

        private static void CheckColor(string name, Color actual, Color expected, float tolerance = 0.01f)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                if (Mathf.Abs(actual[channel] - expected[channel]) > tolerance)
                    throw new Exception($"{name}: {actual} != {expected}");
            }
        }
    }
}