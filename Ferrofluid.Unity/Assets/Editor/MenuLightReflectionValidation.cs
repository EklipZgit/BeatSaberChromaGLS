using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class MenuLightReflectionValidation
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static void Run() => FerrofluidValidationRunner.Run(typeof(MenuLightReflectionValidation));

        public static void RunProjectionAndCache() => FerrofluidValidationRunner.Run(typeof(ProjectionAndCacheValidation));

        private static void RunRuntime()
        {
            using (SavedGlobals saved = new())
            {
                ValidateProjection();
                ValidateCache();
            }

            Debug.Log("[MenuLightReflectionValidation] PASS: production projection and saved array cache.");
        }

        private static void ValidateProjection(Shader overrideShader = null)
        {
            RenderTexture array = new(16, 16, 0, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 2,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            Cubemap blackRoom = new(4, TextureFormat.RGBAHalf, false);
            Material material = new(overrideShader != null ? overrideShader : AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader"));
            try
            {
                Check(array.Create(), "The marker texture array must be supported.");
                GL.sRGBWrite = false;
                Graphics.SetRenderTarget(array, 0, CubemapFace.Unknown, 0);
                GL.Clear(false, true, new Color(2f, 0f, 0f));
                Graphics.SetRenderTarget(array, 0, CubemapFace.Unknown, 1);
                GL.Clear(false, true, new Color(0f, 3f, 0f));
                for (int face = 0; face < 6; face++)
                    blackRoom.SetPixels(new Color[16], (CubemapFace)face);

                blackRoom.Apply(false);
                Shader.SetGlobalTexture("_FerrofluidMenuLightReflections", array);
                Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", blackRoom);
                Shader.SetGlobalTexture("_FerrofluidFloorReflection", Texture2D.blackTexture);
                Shader.SetGlobalVector("_FerrofluidFloorReflectionBounds", new Vector4(-2f, -2f, 0.25f, 0.25f));
                Shader.SetGlobalFloat("_FerrofluidFloorReflectionY", 0f);
                Shader.SetGlobalTexture("_FerrofluidParabolaReflection", Texture2D.whiteTexture);
                Shader.SetGlobalVector("_FerrofluidParabolaReflectionBounds", new Vector4(-2f, -2f, 0.25f, 0.25f));
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionY", 0f);
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionEnabled", 0f);
                Shader.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                material.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                material.SetFloat("_ValidationOutput", 4f);
                SetStrengths(new Vector4(0f, 0f, 1f, 1f));

                Matrix4x4[] matrices = new Matrix4x4[8];
                Vector4[] bounds = new Vector4[8];
                Vector4[] flags = new Vector4[8];
                for (int index = 0; index < 8; index++)
                {
                    matrices[index] = Matrix4x4.identity;
                    bounds[index] = new Vector4(-2f, -2f, 0.25f, 0.25f);
                }

                flags[0].x = 1f;
                flags[1].x = 1f;
                Publish(2);
                Sample("two-light HDR sum", new Vector3(0f, 1f, 0f), Vector3.down, Vector3.up, new Color(2f, 3f, 0f));
                Publish(1);
                Sample("count excludes second enabled slot", new Vector3(0f, 1f, 0f), Vector3.down, Vector3.up, new Color(2f, 0f, 0f));
                flags[0].x = 0f;
                Publish(2);
                Sample("per-light flag", new Vector3(0f, 1f, 0f), Vector3.down, Vector3.up, new Color(0f, 3f, 0f));
                flags[0].x = 1f;
                Publish(2);
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionEnabled", 1f);
                Sample("collection replaces legacy", new Vector3(0f, 1f, 0f), Vector3.down, Vector3.up, new Color(2f, 3f, 0f));
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionEnabled", 0f);
                SetStrengths(new Vector4(0f, 0f, 0.5f, 0f));
                Sample("luma and strength", new Vector3(0f, 1f, 0f), Vector3.down, Vector3.up, new Color(1.2854f, 1.2854f, 1.2854f));
                SetStrengths(new Vector4(0f, 0f, 1f, 1f));

                flags[1].x = 0f;
                Publish(2);
                Sample("horizontal underside", new Vector3(0f, -1f, 0f), Vector3.up, Vector3.up, new Color(2f, 0f, 0f));
                Sample("horizontal negative t", new Vector3(0f, 1f, 0f), Vector3.up, Vector3.up, Color.black);
                Sample("horizontal parallel", new Vector3(0f, 1f, 0f), Vector3.left, Vector3.up, Color.black);
                Sample("horizontal outside bounds", new Vector3(4f, 1f, 0f), Vector3.down, Vector3.up, Color.black);

                matrices[1] = Matrix4x4.Rotate(Quaternion.AngleAxis(90f, Vector3.forward)).inverse;
                flags[0].x = 0f;
                flags[1].x = 1f;
                Publish(2);
                Sample("vertical plane", new Vector3(1f, 1f, 0f), Vector3.left, Vector3.right, new Color(0f, 3f, 0f));
                Sample("vertical underside", new Vector3(-1f, 1f, 0f), Vector3.right, Vector3.right, new Color(0f, 3f, 0f));
                Sample("vertical negative t", new Vector3(1f, 1f, 0f), Vector3.right, Vector3.right, Color.black);
                Sample("vertical parallel", new Vector3(1f, 1f, 0f), Vector3.down, Vector3.up, Color.black);
                Sample("vertical outside bounds", new Vector3(1f, 4f, 0f), Vector3.left, Vector3.right, Color.black);
                flags[1].x = 0f;
                Publish(2);
                Sample("master flags off", new Vector3(1f, 1f, 0f), Vector3.left, Vector3.right, Color.black);
                Publish(0);
                Sample("empty collection", new Vector3(0f, 1f, 0f), Vector3.down, Vector3.up, Color.black);

                // Directional floor response: flags.y is the side/top gain, flags.z is the
                // parabola slope scale 2*BaseCurveScale/WidthScale^2. Red slice red(2,0,0).
                float sideGain = 1.44f * 1.3f / .64f - 1f;
                float slopeScale = 2f * .75f / (3.2f * 3.2f);
                flags[0] = new Vector4(1f, sideGain, slopeScale, 0f);
                flags[1].x = 0f;
                matrices[0] = Matrix4x4.identity;
                bounds[0] = new Vector4(-2f, -2f, .25f, .25f);
                Publish(1);
                float r75 = Mathf.Sqrt(.75f);
                float r5 = Mathf.Sqrt(.5f);
                Color lit = new(2f, 0f, 0f);
                Color boosted = new(2f * (1f + sideGain * .75f), 0f, 0f);
                Color mid = new(2f * (1f + sideGain * .5f), 0f, 0f);
                SampleRay("floor overhead", Vector3.zero, Vector3.down, lit);
                SampleRay("floor apex front", Vector3.zero, new Vector3(0f, -.5f, r75), boosted);
                SampleRay("floor apex back", Vector3.zero, new Vector3(0f, -.5f, -r75), boosted);
                SampleRay("floor apex 45", Vector3.zero, new Vector3(0f, -r5, r5), mid);
                SampleRay("floor apex tangent", Vector3.zero, new Vector3(r75, -.5f, 0f), lit);
                float sx = slopeScale; // 2a at localHit.x=1
                float nz = Mathf.Sqrt(1f + sx * sx);
                Vector3 offApexHit = new(1f, 0f, .75f / (3.2f * 3.2f));
                SampleRay("floor off-apex side normal", offApexHit, new Vector3(r75 * sx / nz, -.5f, -r75 / nz), boosted);
                SampleRay("floor off-apex tangent", offApexHit, new Vector3(r75 / nz, -.5f, r75 * sx / nz), lit);

                // Whole-pose rotation+translation must preserve the local response.
                Quaternion yaw60 = Quaternion.AngleAxis(60f, Vector3.up);
                Matrix4x4 pose = Matrix4x4.TRS(new Vector3(1f, .02f, -.35f), yaw60, Vector3.one);
                matrices[0] = pose.inverse;
                Publish(1);
                Vector3 worldHit = pose.MultiplyPoint3x4(Vector3.zero);
                Vector3 worldRay = pose.MultiplyVector(new Vector3(0f, -.5f, r75));
                SampleRay("floor rotated apex front", worldHit, worldRay, boosted);
                matrices[0] = Matrix4x4.identity;
                Publish(1);
                SampleRay("floor outside bounds", new Vector3(0f, 0f, 4f), Vector3.down, Color.black);
                SampleRay("floor parallel rejected", Vector3.zero, Vector3.forward, Color.black);
                flags[0] = new Vector4(1f, 0f, 0f, 0f);
                Publish(1);
                SampleRay("non-floor unboosted", Vector3.zero, new Vector3(0f, -.5f, r75), lit);
                flags[0] = new Vector4(0f, sideGain, slopeScale, 0f);
                Publish(1);
                SampleRay("disabled floor black", Vector3.zero, new Vector3(0f, -.5f, r75), Color.black);
                flags[0] = new Vector4(1f, sideGain, slopeScale, 0f);
                Publish(0);

                // A missing optional pack enables FERROFLUID_NO_REFLECTIONS; procedural floor
                // lights must still contribute because they are independent of the room pack.
                flags[0].x = 1f;
                flags[1].x = 1f;
                matrices[0] = Matrix4x4.identity;
                matrices[1] = Matrix4x4.identity;
                Publish(2);
                Shader.DisableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                material.DisableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                Sample("default variant keeps procedural lights", new Vector3(0f, 1f, 0f), Vector3.down, Vector3.up, new Color(2f, 3f, 0f));
                flags[1].x = 0f;
                Publish(1);
                SampleRay("default floor apex front", Vector3.zero, new Vector3(0f, -.5f, r75), boosted);
                SampleRay("default floor apex back", Vector3.zero, new Vector3(0f, -.5f, -r75), boosted);
                SampleRay("default floor overhead", Vector3.zero, Vector3.down, lit);
                flags[1].x = 1f;
                Publish(2);
                Shader.EnableKeyword("FERROFLUID_NO_REFLECTIONS");
                material.EnableKeyword("FERROFLUID_NO_REFLECTIONS");
                Sample("missing room keeps procedural lights", new Vector3(0f, 1f, 0f), Vector3.down, Vector3.up, new Color(2f, 3f, 0f));
                flags[1].x = 0f;
                Publish(1);
                SampleRay("no-room floor apex front", Vector3.zero, new Vector3(0f, -.5f, r75), boosted);
                SampleRay("no-room floor apex back", Vector3.zero, new Vector3(0f, -.5f, -r75), boosted);
                SampleRay("no-room floor overhead", Vector3.zero, Vector3.down, lit);
                flags[1].x = 1f;
                Publish(2);
                Publish(0);
                Sample("missing room with empty collection", new Vector3(0f, 1f, 0f), Vector3.down, Vector3.up, Color.black);
                Shader.DisableKeyword("FERROFLUID_NO_REFLECTIONS");
                Shader.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                material.DisableKeyword("FERROFLUID_NO_REFLECTIONS");
                material.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");

                void Publish(int count)
                {
                    Shader.SetGlobalFloat("_FerrofluidMenuLightCount", count);
                    Shader.SetGlobalMatrixArray("_FerrofluidMenuLightWorldToLocal", matrices);
                    Shader.SetGlobalVectorArray("_FerrofluidMenuLightBounds", bounds);
                    Shader.SetGlobalVectorArray("_FerrofluidMenuLightFlags", flags);
                }

                void SetStrengths(Vector4 strengths)
                {
                    Shader.SetGlobalVector("_FerrofluidReflectionStrengths", strengths);
                    material.SetVector("_FerrofluidReflectionStrengths", strengths);
                }

                void SampleRay(string name, Vector3 worldHit, Vector3 worldRay, Color expected)
                {
                    Vector3 normal = Vector3.up;
                    Vector3 origin = worldHit - worldRay;
                    Vector3 view = -Vector3.Reflect(worldRay, normal);
                    Sample(name, origin, view, normal, expected);
                }

                void Sample(string name, Vector3 origin, Vector3 view, Vector3 normal, Color expected)
                {
                    // The shader uses reflect(-view, normal), not reflect(view, normal).
                    material.SetVector("_ValidationWorldPosition", origin);
                    material.SetVector("_ValidationCameraPosition", origin + view);
                    Color[] pixels = FerrofluidShadingValidation.Capture(material, normal, normal, 0f, 0f, name, 16, false);
                    float maximumError = 0f;
                    foreach (Color pixel in pixels)
                    {
                        for (int channel = 0; channel < 3; channel++)
                        {
                            Check(!float.IsNaN(pixel[channel]) && !float.IsInfinity(pixel[channel]), name + " must remain finite.");
                            maximumError = Mathf.Max(maximumError, Mathf.Abs(pixel[channel] - expected[channel]));
                        }
                    }

                    Debug.Log($"[MenuLightReflectionValidation] {name}: actual={pixels[pixels.Length / 2]:F6} expected={expected:F6} maxError={maximumError:R}");
                    Check(maximumError <= 0.01f, name + " must match raw linear HDR within 0.01.");
                }
            }
            finally
            {
                RenderTexture.active = null;
                array.Release();
                Object.DestroyImmediate(array);
                Object.DestroyImmediate(blackRoom);
                Object.DestroyImmediate(material);
            }
        }

        private static void ValidateCache(bool includeOwner = true)
        {
            object config = FerrofluidValidationRuntime.CreateConfig();
            Set(config, "MenuLightsJson", "[]");
            Set(config, "MenuLightDefaultsVersion", 1);
            Assembly plugin = config.GetType().Assembly;
            Type definitionType = plugin.GetType("ChromaGLS.Settings.MenuLightDefinition", true);
            Type meshesType = plugin.GetType("ChromaGLS.Settings.MenuLightMeshes", true);
            Type cacheType = plugin.GetType("ChromaGLS.Settings.MenuLightReflectionCache", true);
            Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true);
            Type settingsType = plugin.GetType("ChromaGLS.Settings.ParabolicFloorLightSettings", true);
            object enclosure = Activator.CreateInstance(
                enclosureType,
                Instance,
                null,
                new object[] { Vector3.zero, 3f, new Vector3(0f, 0f, -3f), Vector3.forward, 0f },
                null);
            IList definitions = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(definitionType));
            IDictionary meshes = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), meshesType));
            Material post = new(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/ParabolicBloomValidation.shader"));
            object cache = null;
            int calls = 0;
            int cameraRenders = 0;
            Camera.CameraCallback cameraHook = _ => cameraRenders++;
            Camera.onPostRender += cameraHook;
            try
            {
                Action<RenderTexture, RenderTexture> process = (source, destination) =>
                {
                    calls++;
                    Check(source.format == RenderTextureFormat.ARGBHalf, "The post processor needs the native HDR alpha payload.");
                    Color[] input = FerrofluidValidationRuntime.Read(source);
                    float alphaPeak = 0f;
                    foreach (Color pixel in input)
                        alphaPeak = Mathf.Max(alphaPeak, pixel.a);

                    Check(Mathf.Abs(alphaPeak - 0.36f) < 0.002f, "Brightness 0.6 must preserve the 0.36 source bloom payload.");
                    Check(input[0].a == 0f && input[0].maxColorComponent == 0f, "Empty bake pixels must be transparent black.");

                    // This existing test processor checks the callback and alpha contract, not
                    // Beat Saber's serialized main-effect assets or their in-headset appearance.
                    Graphics.Blit(source, destination, post);
                };
                cache = Activator.CreateInstance(
                    cacheType,
                    Instance,
                    null,
                    new object[] { AssetDatabase.LoadAssetAtPath<Shader>("Assets/ParabolicFloorReflection.shader"), process },
                    null);
                object red = Definition("red-light", "Square", new Color(2f, 0f, 0f));
                object green = Definition("green-light", "Sphere", new Color(0f, 3f, 0f));
                definitions.Add(red);
                definitions.Add(green);
                meshes.Add("red-light", BuildMeshes(red));
                meshes.Add("green-light", BuildMeshes(green));
                Sync();
                RenderTexture initial = Current();
                Check(initial.width == 1024 && initial.height == 1024 && initial.depth == 0, "The cache must be 1024 square without depth.");
                Check(initial.format == RenderTextureFormat.RGB111110Float && initial.dimension == TextureDimension.Tex2DArray, "The saved cache must be one packed HDR array.");
                Check(initial.volumeDepth == 2 && initial.useMipMap && initial.mipmapCount == 11, "Two lights need two slices and eleven mip levels.");
                Check(initial.filterMode == FilterMode.Trilinear && initial.wrapMode == TextureWrapMode.Clamp, "Saved maps need trilinear/clamp filtering.");
                Check(calls == 2, "Each new saved light must bake once.");
                Type qualityType = plugin.GetType("ChromaGLS.Settings.ReflectionQuality", true);
                object none = Enum.Parse(qualityType, "None");
                cacheType.GetMethod("SetQuality", Instance).Invoke(cache, new[] { none });
                Sync();
                Check(ReferenceEquals(initial, Current()) && calls == 2, "None must retain the Low procedural atlas without reallocating or rebaking.");
                Check(Shader.GetGlobalFloat("_FerrofluidMenuLightCount") == 2f, "None must keep both procedural lights published without a room pack.");
                Color[] redPixels = ReadSlice(initial, 0, 0);
                Color[] greenPixels = ReadSlice(initial, 1, 0);
                Color redPeak = Peak(redPixels);
                Color greenPeak = Peak(greenPixels);
                Check(redPeak.r > redPeak.g && greenPeak.g > greenPeak.r, "Each slice must retain its own source color after post processing.");
                Check(redPeak.maxColorComponent > 1f && greenPeak.maxColorComponent > 1f, "Packed maps must retain HDR radiance.");

                cacheType.GetMethod("SetEnabled", Instance).Invoke(cache, new object[] { false });
                Vector4[] flags = Shader.GetGlobalVectorArray("_FerrofluidMenuLightFlags");
                Check(flags[0].x == 0f && flags[1].x == 0f, "Master off must disable every slice.");
                cacheType.GetMethod("SetEnabled", Instance).Invoke(cache, new object[] { true });
                Set(red, "Enabled", false);
                Sync();
                flags = Shader.GetGlobalVectorArray("_FerrofluidMenuLightFlags");
                Check(flags[0].x == 0f && flags[1].x == 1f && calls == 2, "Per-light and master visibility must not bake.");
                Check(flags[0].y == 0f && flags[0].z == 0f && flags[1].y == 0f && flags[1].z == 0f, "Non-floor lights must publish zero directional metadata.");

                object appearance = Get(green, "Appearance");
                Set(green, "Appearance", appearance.GetType().GetMethod("WithGreen", Instance).Invoke(appearance, new object[] { 5f }));
                Sync();
                Check(calls == 3 && ReferenceEquals(initial, Current()), "One color edit must bake exactly one slice without reallocating.");
                Same(redPixels, ReadSlice(Current(), 0, 0), "Editing green must leave red pixels unchanged.");
                Color[] retained = ReadSlice(Current(), 1, 0);
                Color[] retainedMip = ReadSlice(Current(), 1, 3);
                definitions.RemoveAt(0);
                meshes.Remove("red-light");
                Sync();
                Check(Current().volumeDepth == 1 && calls == 3, "Removing red must copy green by stable ID without baking.");
                Same(retained, ReadSlice(Current(), 0, 0), "Retained base pixels must survive resize exactly.");
                Same(retainedMip, ReadSlice(Current(), 0, 3), "Retained mip pixels must survive resize exactly.");
                definitions.Clear();
                meshes.Clear();
                Sync();
                Check(Shader.GetGlobalFloat("_FerrofluidMenuLightCount") == 0f, "Removing the last light must clear collection count.");
                Check(Shader.GetGlobalTexture("_FerrofluidMenuLightReflections") == null, "Removing the last light must release the array.");
                Check(Shader.GetGlobalFloat("_FerrofluidParabolaReflectionEnabled") == 0f, "An empty collection must not resurrect legacy reflections.");

                object floor = Definition("floor-light", "Floor", new Color(2f, 0f, 0f));
                definitions.Add(floor);
                meshes.Add("floor-light", BuildMeshes(floor));
                Sync();
                flags = Shader.GetGlobalVectorArray("_FerrofluidMenuLightFlags");
                float topScale = Constant("MenuLightGeometry", "FloorTopRadianceScale");
                float expectedGain = ((Constant("MenuLightGeometry", "FloorSideRadianceScale") * Constant("MenuLightGeometry", "FloorSideEmissionScale")) / topScale) - 1f;
                object floorAppearance = Get(floor, "Appearance");
                float width = (float)floorAppearance.GetType().GetField("WidthScale", Instance).GetValue(floorAppearance);
                float expectedSlope = 2f * Constant("ParabolicFloorLightGeometry", "BaseCurveScale") / (width * width);
                Check(Mathf.Abs(flags[0].y - expectedGain) < 0.0001f, $"Floor must publish side/top gain {expectedGain}.");
                Check(Mathf.Abs(flags[0].z - expectedSlope) < 0.0001f, $"Floor must publish doubled curve slope {expectedSlope}.");
                Check(flags[0].x == 1f && flags[0].w == 0f, "An enabled floor must keep the enable flag and a zero tail.");
                definitions.Clear();
                meshes.Clear();
                Sync();
                Check(cameraRenders == 0, "Baking and collection edits must render no scene cameras.");
                Debug.Log($"[MenuLightReflectionValidation] cache: calls={calls} sceneCameras={cameraRenders} redPeak={redPeak:F6} greenPeak={greenPeak:F6}; retained base/mip3 identical.");
                if (includeOwner)
                {
                    ValidateOwner();
                    Check(cameraRenders == 0, "Owner previews and commits must render no scene cameras.");
                }

                void ValidateOwner()
                {
                    Type ownerType = plugin.GetType("ChromaGLS.Settings.ParabolicFloorLight", true);
                    object owner = Activator.CreateInstance(ownerType, Instance, null, new[] { config }, null);
                    try
                    {
                        object store = Get(owner, "Store");
                        Type storeType = store.GetType();
                        storeType.GetMethod("Remove", Instance).Invoke(store, new object[] { "legacy-parabola" });
                        object savedRed = Definition("owner-red", "Square", new Color(2f, 0f, 0f));
                        object savedGreen = Definition("owner-green", "Sphere", new Color(0f, 3f, 0f));
                        storeType.GetMethod("Upsert", Instance).Invoke(store, new[] { savedRed });
                        storeType.GetMethod("Upsert", Instance).Invoke(store, new[] { savedGreen });

                        // The menu rig/main-effect discovery belongs to game initialization.
                        // Inject those resolved dependencies here, then exercise the real owner.
                        OwnerField("_enclosure", enclosure);
                        OwnerField("_playerPosition", Vector3.zero);
                        OwnerField("_baseForward", Vector3.forward);
                        OwnerField("_anchorScene", UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                        Shader rendererShader = Shader.Find("Sprites/Default");
                        Check(rendererShader != null, "The owner test needs the built-in vertex-color shader.");
                        OwnerField("_shader", rendererShader);
                        OwnerField("_enabled", true);
                        OwnerField("_canCreate", true);
                        OwnerField("_reflectionCache", cache);
                        Commit();
                        int initialCalls = calls;
                        RenderTexture committedArray = Current();
                        Color[] committedRed = ReadSlice(committedArray, 0, 0);
                        Color[] committedGreen = ReadSlice(committedArray, 1, 0);
                        Matrix4x4[] committedMatrices = Shader.GetGlobalMatrixArray("_FerrofluidMenuLightWorldToLocal");
                        Vector4[] committedBounds = Shader.GetGlobalVectorArray("_FerrofluidMenuLightBounds");
                        IDictionary renderers = (IDictionary)ownerType.GetField("_renderers", Instance).GetValue(owner);
                        object renderer = renderers["owner-red"];
                        GameObject root = (GameObject)renderer.GetType().GetField("_root", Instance).GetValue(renderer);
                        Vector3 committedPosition = root.transform.position;
                        Color committedColor = EmitterColor(root);
                        object working = savedRed.GetType().GetMethod("Clone", Instance).Invoke(savedRed, null);
                        Set(working, "Height", 1f);
                        Set(working, "PositionX", 0.5f);
                        working.GetType().GetMethod("RotateAxis", Instance).Invoke(working, new object[] { 0, 60f });
                        object color = Get(working, "Appearance");
                        Set(working, "Appearance", color.GetType().GetMethod("WithBlue", Instance).Invoke(color, new object[] { 4f }));
                        ownerType.GetMethod("Preview", Instance).Invoke(owner, new[] { working });
                        Check(root.transform.position == new Vector3(0.5f, 1f, 0f), "An unsaved pose must update the visible renderer.");
                        Color previewColor = EmitterColor(root);
                        Check(previewColor.b > previewColor.r, "Unsaved blue must reach the visible material.");
                        Check(calls == initialCalls && ReferenceEquals(committedArray, Current()), "Preview must not bake or replace saved reflection storage.");
                        SameMatrices(committedMatrices, Shader.GetGlobalMatrixArray("_FerrofluidMenuLightWorldToLocal"), 0, 2);
                        Vector4[] previewBounds = Shader.GetGlobalVectorArray("_FerrofluidMenuLightBounds");
                        Check(committedBounds[0] == previewBounds[0] && committedBounds[1] == previewBounds[1], "Preview must not publish unsaved bounds.");
                        Same(committedRed, ReadSlice(Current(), 0, 0), "Preview must not mutate saved red pixels.");
                        Same(committedGreen, ReadSlice(Current(), 1, 0), "Preview must not mutate saved green pixels.");
                        ownerType.GetMethod("Restore", Instance).Invoke(owner, new object[] { "owner-red" });
                        Check(root.transform.position == committedPosition && EmitterColor(root) == committedColor, "Cancel must restore the saved renderer.");
                        Check(calls == initialCalls, "Cancel must not bake.");

                        ownerType.GetMethod("Preview", Instance).Invoke(owner, new[] { working });
                        storeType.GetMethod("Upsert", Instance).Invoke(store, new[] { working });
                        Commit();
                        Check(calls == initialCalls + 1 && ReferenceEquals(committedArray, Current()), "Saving one preview must bake only that slice.");
                        Same(committedGreen, ReadSlice(Current(), 1, 0), "Saving red must preserve green pixels exactly.");
                        Matrix4x4[] savedMatrices = Shader.GetGlobalMatrixArray("_FerrofluidMenuLightWorldToLocal");
                        SameMatrices(committedMatrices, savedMatrices, 1, 1);
                        Check(savedMatrices[0] != committedMatrices[0], "Save must publish the edited plane pose.");
                        Color[] editedRed = ReadSlice(Current(), 0, 0);
                        bool changed = false;
                        for (int pixel = 0; pixel < editedRed.Length && !changed; pixel++)
                            changed = editedRed[pixel].r != committedRed[pixel].r || editedRed[pixel].b != committedRed[pixel].b;

                        Check(changed, "Save must publish the edited source color.");
                        storeType.GetMethod("Remove", Instance).Invoke(store, new object[] { "owner-red" });
                        storeType.GetMethod("Remove", Instance).Invoke(store, new object[] { "owner-green" });
                        Commit();
                        Check(Shader.GetGlobalFloat("_FerrofluidMenuLightCount") == 0f && Current() == null, "Owner remove-to-zero must clear storage and count.");
                        Check(Shader.GetGlobalFloat("_FerrofluidParabolaReflectionEnabled") == 0f, "Owner remove-to-zero must leave legacy disabled.");
                        Debug.Log("[MenuLightReflectionValidation] owner: preview/cancel preserved saved pixels and metadata; save baked only red; green unchanged; remove-to-zero cleared.");

                        void OwnerField(string name, object value) => ownerType.GetField(name, Instance).SetValue(owner, value);

                        Color EmitterColor(GameObject target)
                        {
                            // The optional coverage pass occupies slot zero; the emitter is always last.
                            Material[] materials = target.GetComponent<MeshRenderer>().sharedMaterials;
                            return materials[materials.Length - 1].color;
                        }

                        void Commit() => ownerType.GetMethod("Synchronize", Instance).Invoke(owner, new[] { Get(store, "Lights") });
                    }
                    finally
                    {
                        ((IDisposable)owner).Dispose();
                        cache = null;
                    }
                }

                object Definition(string id, string shape, Color color)
                {
                    object definition = Activator.CreateInstance(definitionType, true);
                    Set(definition, "Id", id);
                    Set(definition, "Type", Enum.Parse(plugin.GetType("ChromaGLS.Settings.MenuLightType", true), shape));
                    object value = settingsType.GetField("Default", Static).GetValue(null);
                    foreach ((string field, float amount) in new[]
                    {
                        ("Brightness", 0.6f), ("Red", color.r), ("Green", color.g), ("Blue", color.b),
                        ("BloomWidthScale", 0f), ("BloomBrightness", 0f),
                    })
                    {
                        value = settingsType.GetMethod("With" + field, Instance).Invoke(value, new object[] { amount });
                    }

                    Set(definition, "Appearance", value);
                    return definition;
                }

                object BuildMeshes(object definition)
                {
                    object[] arguments = { enclosure, Vector3.zero, Vector3.forward, definition, null };
                    bool valid = (bool)plugin.GetType("ChromaGLS.Settings.MenuLightGeometry", true).GetMethod("TryCreate", Static).Invoke(null, arguments);
                    Check(valid, "The test light must fit the real enclosure.");
                    return arguments[4];
                }

                void Sync() => cacheType.GetMethod("Synchronize", Instance).Invoke(cache, new object[] { definitions, meshes, true });

                float Constant(string type, string name) => (float)plugin.GetType("ChromaGLS.Settings." + type, true).GetField(name, Static).GetValue(null);

                RenderTexture Current() => (RenderTexture)Shader.GetGlobalTexture("_FerrofluidMenuLightReflections");
            }
            finally
            {
                Camera.onPostRender -= cameraHook;
                if (cache != null)
                    ((IDisposable)cache).Dispose();

                Object.DestroyImmediate(post);
            }
        }

        private static object Get(object target, string property) => target.GetType().GetProperty(property, Instance).GetValue(target);

        private static void Set(object target, string property, object value) => target.GetType().GetProperty(property, Instance).SetValue(target, value);

        private static Color[] ReadSlice(RenderTexture array, int slice, int mip)
        {
            int side = Mathf.Max(1, array.width >> mip);
            RenderTexture copy = RenderTexture.GetTemporary(side, side, 0, array.format, RenderTextureReadWrite.Linear);
            try
            {
                Graphics.CopyTexture(array, slice, mip, copy, 0, 0);
                return FerrofluidValidationRuntime.Read(copy);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(copy);
            }
        }

        private static Color Peak(Color[] pixels)
        {
            Color peak = Color.black;
            foreach (Color pixel in pixels)
            {
                peak.r = Mathf.Max(peak.r, pixel.r);
                peak.g = Mathf.Max(peak.g, pixel.g);
                peak.b = Mathf.Max(peak.b, pixel.b);
            }

            return peak;
        }

        private static void Same(Color[] expected, Color[] actual, string message)
        {
            Check(expected.Length == actual.Length, message);
            for (int index = 0; index < expected.Length; index++)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    if (expected[index][channel] != actual[index][channel])
                        throw new InvalidOperationException(message + " Pixel " + index + ", channel " + channel);
                }
            }
        }

        private static void SameMatrices(Matrix4x4[] expected, Matrix4x4[] actual, int start, int count)
        {
            for (int matrix = start; matrix < start + count; matrix++)
            {
                for (int component = 0; component < 16; component++)
                    Check(expected[matrix][component] == actual[matrix][component], "Saved plane matrix must remain unchanged.");
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static class ProjectionAndCacheValidation
        {
            private static void RunRuntime()
            {
                using (SavedGlobals saved = new())
                {
                    ValidateProjection();
                    ValidateCache(false);
                }

                Debug.Log("[MenuLightReflectionValidation] PASS: projection variants and saved cache without owner previews.");
            }
        }

        private sealed class SavedGlobals : IDisposable
        {
            private readonly string[] _textureNames =
            {
                "_FerrofluidMenuLightReflections", "_FerrofluidRoomEnvironment",
                "_FerrofluidFloorReflection", "_FerrofluidParabolaReflection",
            };
            private readonly string[] _floatNames =
            {
                "_FerrofluidMenuLightCount", "_FerrofluidParabolaReflectionEnabled",
                "_FerrofluidParabolaReflectionY", "_FerrofluidFloorReflectionY",
            };
            private readonly string[] _vectorNames =
            {
                "_FerrofluidParabolaReflectionBounds", "_FerrofluidFloorReflectionBounds", "_FerrofluidReflectionStrengths",
            };
            private readonly Texture[] _textures;
            private readonly float[] _floats;
            private readonly Vector4[] _vectors;
            private readonly Matrix4x4[] _matrices = Shader.GetGlobalMatrixArray("_FerrofluidMenuLightWorldToLocal") ?? Array.Empty<Matrix4x4>();
            private readonly Vector4[] _bounds = Shader.GetGlobalVectorArray("_FerrofluidMenuLightBounds") ?? Array.Empty<Vector4>();
            private readonly Vector4[] _flags = Shader.GetGlobalVectorArray("_FerrofluidMenuLightFlags") ?? Array.Empty<Vector4>();
            private readonly bool _roomKeyword = Shader.IsKeywordEnabled("FERROFLUID_ROOM_REFLECTIONS");
            private readonly bool _noRoomKeyword = Shader.IsKeywordEnabled("FERROFLUID_NO_REFLECTIONS");
            private readonly RenderTexture _active = RenderTexture.active;
            private readonly bool _srgb = GL.sRGBWrite;

            internal SavedGlobals()
            {
                _textures = new Texture[_textureNames.Length];
                _floats = new float[_floatNames.Length];
                _vectors = new Vector4[_vectorNames.Length];
                for (int index = 0; index < _textures.Length; index++)
                    _textures[index] = Shader.GetGlobalTexture(_textureNames[index]);

                for (int index = 0; index < _floats.Length; index++)
                    _floats[index] = Shader.GetGlobalFloat(_floatNames[index]);

                for (int index = 0; index < _vectors.Length; index++)
                    _vectors[index] = Shader.GetGlobalVector(_vectorNames[index]);
            }

            public void Dispose()
            {
                for (int index = 0; index < _textures.Length; index++)
                    Shader.SetGlobalTexture(_textureNames[index], _textures[index]);

                for (int index = 0; index < _floats.Length; index++)
                    Shader.SetGlobalFloat(_floatNames[index], _floats[index]);

                for (int index = 0; index < _vectors.Length; index++)
                    Shader.SetGlobalVector(_vectorNames[index], _vectors[index]);

                Shader.SetGlobalMatrixArray("_FerrofluidMenuLightWorldToLocal", _matrices.Length > 0 ? _matrices : new Matrix4x4[8]);
                Shader.SetGlobalVectorArray("_FerrofluidMenuLightBounds", _bounds.Length > 0 ? _bounds : new Vector4[8]);
                Shader.SetGlobalVectorArray("_FerrofluidMenuLightFlags", _flags.Length > 0 ? _flags : new Vector4[8]);
                if (_roomKeyword)
                    Shader.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                else
                    Shader.DisableKeyword("FERROFLUID_ROOM_REFLECTIONS");

                if (_noRoomKeyword)
                    Shader.EnableKeyword("FERROFLUID_NO_REFLECTIONS");
                else
                    Shader.DisableKeyword("FERROFLUID_NO_REFLECTIONS");

                RenderTexture.active = _active;
                GL.sRGBWrite = _srgb;
            }
        }
    }
}
