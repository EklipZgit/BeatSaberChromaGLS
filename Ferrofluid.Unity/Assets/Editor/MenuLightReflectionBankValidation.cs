using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // Bank-split reflection layout: each RenderTexture bank stays under the guaranteed
    // per-resource bound, so the collection publishes up to eight arrays and a per-light
    // bank/slice index instead of one monolithic atlas.
    internal static class MenuLightReflectionBankValidation
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string SplitKeyword = "FERROFLUID_SPLIT_MENU_LIGHT_REFLECTIONS";

        public static void Run() => FerrofluidValidationRunner.Run(typeof(MenuLightReflectionBankValidation));

        // The compiled variant lives in the core bundle embedded into the plugin assembly;
        // prove the shipped shader, not only the project source, carries the split path.
        public static void RunBundled() => FerrofluidValidationRunner.Run(typeof(BundledShaderValidation));

        public static void RunCacheBanks() => FerrofluidValidationRunner.Run(typeof(BankCacheValidation));

        private static void RunRuntime()
        {
            using (BankGlobals saved = new())
            {
                ValidateBankSampling();
            }

            Debug.Log("[MenuLightReflectionBankValidation] PASS: split banks resolve through the shared sampler and per-light indices.");
        }

        private static void ValidateBankSampling(Shader overrideShader = null)
        {
            Shader shader = overrideShader != null ? overrideShader : AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader");
            Material material = new(shader);
            Cubemap blackRoom = new(4, TextureFormat.RGBAHalf, false);
            RenderTexture[] banks = new RenderTexture[8];
            Matrix4x4[] matrices = new Matrix4x4[8];
            Vector4[] bounds = new Vector4[8];
            Vector4[] flags = new Vector4[8];
            try
            {
                for (int face = 0; face < 6; face++)
                    blackRoom.SetPixels(new Color[16], (CubemapFace)face);

                blackRoom.Apply(false);
                GL.sRGBWrite = false;
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
                for (int index = 0; index < 8; index++)
                {
                    matrices[index] = Matrix4x4.identity;
                    bounds[index] = new Vector4(-2f, -2f, 0.25f, 0.25f);
                }

                Vector3 origin = new(0f, 1f, 0f);
                Vector3 view = Vector3.down;

                // Two single-slice banks: light 1 must come from the second resource.
                banks[0] = Marker(1, new Color(2f, 0f, 0f));
                banks[1] = Marker(1, new Color(0f, 3f, 0f));
                BindBanks(banks, 2);
                SetSlicesPerBank(material, 1);
                flags[0].x = 1f;
                flags[1].x = 1f;
                Publish(2);
                Sample("two-bank HDR sum", origin, view, Vector3.up, new Color(2f, 3f, 0f));
                Publish(1);
                Sample("count excludes second bank slot", origin, view, Vector3.up, new Color(2f, 0f, 0f));
                flags[0].x = 0f;
                Publish(2);
                Sample("per-light flag across banks", origin, view, Vector3.up, new Color(0f, 3f, 0f));
                flags[0].x = 1f;
                Publish(2);
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionEnabled", 1f);
                Sample("split collection replaces legacy", origin, view, Vector3.up, new Color(2f, 3f, 0f));
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionEnabled", 0f);
                Publish(0);
                Sample("empty split collection", origin, view, Vector3.up, Color.black);
                Publish(2);

                // Eight single-slice banks: every bank index must resolve independently.
                banks[2] = Marker(1, new Color(0f, 0f, 4f));
                banks[3] = Marker(1, new Color(5f, 0f, 0f));
                banks[4] = Marker(1, new Color(0f, 6f, 0f));
                banks[5] = Marker(1, new Color(0f, 0f, 7f));
                banks[6] = Marker(1, new Color(8f, 0f, 0f));
                banks[7] = Marker(1, new Color(0f, 9f, 0f));
                BindBanks(banks, 8);
                for (int index = 0; index < 8; index++)
                    flags[index].x = 1f;

                Publish(8);
                Sample("eight-bank HDR sum", origin, view, Vector3.up, new Color(15f, 18f, 11f));
                Color[] singles =
                {
                    new Color(2f, 0f, 0f), new Color(0f, 3f, 0f), new Color(0f, 0f, 4f),
                    new Color(5f, 0f, 0f), new Color(0f, 6f, 0f), new Color(0f, 0f, 7f),
                    new Color(8f, 0f, 0f), new Color(0f, 9f, 0f),
                };
                for (int index = 0; index < 8; index++)
                {
                    for (int other = 0; other < 8; other++)
                        flags[other].x = other == index ? 1f : 0f;

                    Publish(8);
                    Sample("bank " + index + " only", origin, view, Vector3.up, singles[index]);
                }

                // Capacity above one: indices map to bank index / slices and slice index % slices.
                for (int index = 0; index < 8; index++)
                {
                    if (banks[index] != null)
                    {
                        banks[index].Release();
                        Object.DestroyImmediate(banks[index]);
                        banks[index] = null;
                    }
                }

                banks[0] = Marker(2, new Color(2f, 0f, 0f), new Color(0f, 3f, 0f));
                banks[1] = Marker(2, new Color(0f, 0f, 4f), new Color(5f, 0f, 0f));
                BindBanks(banks, 2);
                SetSlicesPerBank(material, 2);
                for (int index = 0; index < 8; index++)
                    flags[index].x = 0f;

                flags[0].x = 1f;
                Publish(3);
                Sample("bank0 slice0", origin, view, Vector3.up, new Color(2f, 0f, 0f));
                flags[0].x = 0f;
                flags[1].x = 1f;
                Publish(3);
                Sample("bank0 slice1", origin, view, Vector3.up, new Color(0f, 3f, 0f));
                flags[1].x = 0f;
                flags[2].x = 1f;
                Publish(3);
                Sample("bank1 slice0", origin, view, Vector3.up, new Color(0f, 0f, 4f));
                flags[0].x = 1f;
                flags[1].x = 1f;
                Publish(3);
                Sample("two-bank capacity sum", origin, view, Vector3.up, new Color(2f, 3f, 4f));

                // The collection stays live without the captured room; the missing-pack variant
                // must still sample every bank.
                for (int index = 0; index < 8; index++)
                {
                    if (banks[index] != null)
                    {
                        banks[index].Release();
                        Object.DestroyImmediate(banks[index]);
                        banks[index] = null;
                    }
                }

                banks[0] = Marker(1, new Color(2f, 0f, 0f));
                banks[1] = Marker(1, new Color(0f, 3f, 0f));
                BindBanks(banks, 2);
                SetSlicesPerBank(material, 1);
                flags[2].x = 0f;
                flags[0].x = 1f;
                flags[1].x = 1f;
                Publish(2);
                Shader.DisableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                material.DisableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                Shader.EnableKeyword("FERROFLUID_NO_REFLECTIONS");
                material.EnableKeyword("FERROFLUID_NO_REFLECTIONS");
                Sample("no-room variant keeps split lights", origin, view, Vector3.up, new Color(2f, 3f, 0f));
                Shader.DisableKeyword("FERROFLUID_NO_REFLECTIONS");
                material.DisableKeyword("FERROFLUID_NO_REFLECTIONS");
                Sample("default variant keeps split lights", origin, view, Vector3.up, new Color(2f, 3f, 0f));
                Shader.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                material.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");

                // The unsplit variant must keep sampling the packed single array unchanged.
                for (int index = 0; index < 8; index++)
                {
                    if (banks[index] != null)
                    {
                        banks[index].Release();
                        Object.DestroyImmediate(banks[index]);
                        banks[index] = null;
                    }
                }

                banks[0] = Marker(2, new Color(2f, 0f, 0f), new Color(0f, 3f, 0f));
                BindBanks(banks, 1);
                SetSplit(material, false);
                Publish(2);
                Sample("unsplit two-slice sum", origin, view, Vector3.up, new Color(2f, 3f, 0f));
            }
            finally
            {
                for (int index = 0; index < banks.Length; index++)
                {
                    if (banks[index] != null)
                    {
                        banks[index].Release();
                        Object.DestroyImmediate(banks[index]);
                    }
                }

                RenderTexture.active = null;
                Object.DestroyImmediate(blackRoom);
                Object.DestroyImmediate(material);
            }

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

            void SetSlicesPerBank(Material target, int slices)
            {
                Shader.SetGlobalFloat("_FerrofluidMenuLightSlicesPerArray", slices);
                SetSplit(target, true);
            }

            void Sample(string name, Vector3 sampleOrigin, Vector3 sampleView, Vector3 normal, Color expected)
            {
                material.SetVector("_ValidationWorldPosition", sampleOrigin);
                material.SetVector("_ValidationCameraPosition", sampleOrigin + sampleView);
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

                Debug.Log($"[MenuLightReflectionBankValidation] {name}: actual={pixels[pixels.Length / 2]:F6} expected={expected:F6} maxError={maximumError:R}");
                Check(maximumError <= 0.01f, name + " must match raw linear HDR within 0.01.");
            }
        }

        private static RenderTexture Marker(int slices, params Color[] colors)
        {
            RenderTexture bank = new(16, 16, 0, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = slices,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            Check(bank.Create(), "The marker bank must be supported.");
            for (int slice = 0; slice < slices; slice++)
            {
                Graphics.SetRenderTarget(bank, 0, CubemapFace.Unknown, slice);
                GL.Clear(false, true, colors[slice]);
            }

            RenderTexture.active = null;
            return bank;
        }

        private static void BindBanks(RenderTexture[] banks, int count)
        {
            for (int index = 0; index < 8; index++)
                Shader.SetGlobalTexture(BankName(index), index < count ? banks[index] : null);
        }

        private static string BankName(int index)
            => index == 0 ? "_FerrofluidMenuLightReflections" : "_FerrofluidMenuLightReflections" + index;

        private static void SetSplit(Material material, bool enabled)
        {
            if (enabled)
                Shader.EnableKeyword(SplitKeyword);
            else
                Shader.DisableKeyword(SplitKeyword);

            // The keyword only exists after the shader declares the variant; the pre-fix run must
            // still reach the color assertion instead of failing on an unknown keyword.
            if (Array.IndexOf(material.shader.keywordSpace.keywordNames, SplitKeyword) >= 0)
                material.SetKeyword(new LocalKeyword(material.shader, SplitKeyword), enabled);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static class BundledShaderValidation
        {
            private static void RunRuntime()
            {
                object config = FerrofluidValidationRuntime.CreateConfig();
                Assembly plugin = config.GetType().Assembly;
                using Stream stream = plugin.GetManifestResourceStream("ChromaGLS.Settings.ferrofluid-windows.bundle")!;
                Check(stream != null, "The plugin assembly must embed the ferrofluid bundle.");
                byte[] bundleBytes = new byte[stream.Length];
                int offset = 0;
                while (offset < bundleBytes.Length)
                {
                    int read = stream.Read(bundleBytes, offset, bundleBytes.Length - offset);
                    if (read == 0)
                        throw new EndOfStreamException($"Read {offset} of {bundleBytes.Length} ferrofluid bundle bytes.");

                    offset += read;
                }

                AssetBundle bundle = AssetBundle.LoadFromMemory(bundleBytes);
                try
                {
                    Check(bundle != null, "The embedded bundle must load.");
                    Shader shader = bundle.LoadAsset<Shader>("assets/ferrofluidsurface.shader");
                    Check(shader != null && shader.isSupported, "The bundled surface shader must load and be supported.");
                    using (BankGlobals saved = new())
                    {
                        ValidateBankSampling(shader);
                    }
                }
                finally
                {
                    if (bundle != null)
                        bundle.Unload(true);
                }

                Debug.Log("[MenuLightReflectionBankValidation] PASS: embedded bundle shader carries the split bank path.");
            }
        }

        private static class BankCacheValidation
        {
            private static void RunRuntime()
            {
                using (BankGlobals saved = new())
                {
                    ValidateUltraSplit();
                }

                Debug.Log("[MenuLightReflectionBankValidation] PASS: production Ultra cache keeps each 16384 bank under the bound.");
            }

            private static void ValidateUltraSplit()
            {
                object config = FerrofluidValidationRuntime.CreateConfig();
                Set(config, "MenuLightsJson", "[]");
                Set(config, "MenuLightDefaultsVersion", 1);
                Assembly plugin = config.GetType().Assembly;
                Type definitionType = plugin.GetType("ChromaGLS.Settings.MenuLightDefinition", true)!;
                Type meshesType = plugin.GetType("ChromaGLS.Settings.MenuLightMeshes", true)!;
                Type cacheType = plugin.GetType("ChromaGLS.Settings.MenuLightReflectionCache", true)!;
                Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true)!;
                Type settingsType = plugin.GetType("ChromaGLS.Settings.ParabolicFloorLightSettings", true)!;
                Type qualityType = plugin.GetType("ChromaGLS.Settings.ReflectionQuality", true)!;
                long maximumBytes = (long)cacheType.GetField("MaximumArrayBytes", Static)!.GetValue(null)!;
                object enclosure = Activator.CreateInstance(
                    enclosureType,
                    Instance,
                    null,
                    new object[] { Vector3.zero, 3f, new Vector3(0f, 0f, -3f), Vector3.forward, 0f },
                    null)!;
                IList definitions = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(definitionType))!;
                IDictionary meshes = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), meshesType))!;
                object cache = null;
                int calls = 0;
                try
                {
                    Action<RenderTexture, RenderTexture> process = (source, destination) =>
                    {
                        calls++;
                        Graphics.Blit(source, destination);
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
                    object ultra = Enum.Parse(qualityType, "Ultra");
                    object high = Enum.Parse(qualityType, "High");
                    object none = Enum.Parse(qualityType, "None");
                    cacheType.GetMethod("SetQuality", Instance)!.Invoke(cache, new[] { ultra });
                    Sync();
                    RenderTexture first = GlobalBank(0);
                    RenderTexture second = GlobalBank(1);
                    CheckBank(first, "Ultra bank0");
                    CheckBank(second, "Ultra bank1");
                    Check(BankBytes(first) <= maximumBytes && BankBytes(second) <= maximumBytes, "Each 16384 bank must stay under the guaranteed bound.");
                    for (int index = 2; index < 8; index++)
                        Check(GlobalBank(index) == null, "Unused bank slots must publish null.");

                    Check(Shader.GetGlobalFloat("_FerrofluidMenuLightCount") == 2f, "Both lights stay published.");
                    Check(Shader.IsKeywordEnabled(SplitKeyword), "Two banks must enable the split variant.");
                    Check(Shader.GetGlobalFloat("_FerrofluidMenuLightSlicesPerArray") == 1f, "Ultra must publish one slice per bank.");
                    Check(calls == 2, "Each new saved light must bake once.");
                    Sync();
                    Check(calls == 2, "An unchanged sync must not bake.");
                    Color firstTail = MipTail(first);
                    Color secondTail = MipTail(second);
                    Check(firstTail.r > firstTail.g && secondTail.g > secondTail.r, "Each bank must retain its own baked color.");

                    // Reorder by stable Id: banks reallocate but colors copy without baking.
                    object moved = definitions[0]!;
                    definitions.RemoveAt(0);
                    definitions.Add(moved);
                    Sync();
                    Check(calls == 2, "Reordering saved lights must copy, not bake.");
                    RenderTexture swappedFirst = GlobalBank(0);
                    RenderTexture swappedSecond = GlobalBank(1);
                    CheckBank(swappedFirst, "reordered bank0");
                    CheckBank(swappedSecond, "reordered bank1");
                    Color newFirstTail = MipTail(swappedFirst);
                    Color newSecondTail = MipTail(swappedSecond);
                    Check(newFirstTail.g > newFirstTail.r && newSecondTail.r > newSecondTail.g, "Reordered banks must keep their lights' colors.");

                    // Removing one light leaves a single bank, so the split keyword turns off.
                    // The reorder moved green first, so index zero is the light being removed.
                    definitions.RemoveAt(0);
                    meshes.Remove("green-light");
                    Sync();
                    Check(calls == 2, "Removing a light must copy the survivor without baking.");
                    Check(GlobalBank(0) != null && GlobalBank(1) == null, "One light needs one bank.");
                    Check(MipTail(GlobalBank(0)).r > MipTail(GlobalBank(0)).g, "The surviving bank must keep the red light.");
                    Check(!Shader.IsKeywordEnabled(SplitKeyword), "A single bank must disable the split variant.");

                    definitions.Add(green);
                    meshes.Add("green-light", BuildMeshes(green));
                    Sync();
                    Check(calls == 3, "Only the re-added light must bake.");
                    Check(Shader.IsKeywordEnabled(SplitKeyword), "Two banks must re-enable the split variant.");

                    // Ultra to High releases both huge banks and re-packs into a single array.
                    RenderTexture oldFirst = GlobalBank(0);
                    RenderTexture oldSecond = GlobalBank(1);
                    cacheType.GetMethod("SetQuality", Instance)!.Invoke(cache, new[] { high });
                    Sync();
                    Check(!oldFirst.IsCreated() && !oldSecond.IsCreated(), "The quality switch must release every old bank.");
                    RenderTexture highBank = GlobalBank(0);
                    Check(highBank != null && highBank.IsCreated(), "High must publish a bank.");
                    Check(highBank.width == 8192 && highBank.volumeDepth == 2, "8192 capacity five keeps two lights in one bank.");
                    Check(BankBytes(highBank) <= maximumBytes, "The High bank must stay under the bound.");
                    Check(GlobalBank(1) == null, "High must not publish a second bank.");
                    Check(!Shader.IsKeywordEnabled(SplitKeyword), "A packed High collection must disable the split variant.");
                    Check(Shader.GetGlobalFloat("_FerrofluidMenuLightSlicesPerArray") == 5f, "High must publish five slices per bank.");
                    Check(calls == 5, "The quality switch must rebake both lights once.");

                    // None keeps the procedural Low atlas; the collection stays published.
                    cacheType.GetMethod("SetQuality", Instance)!.Invoke(cache, new[] { none });
                    Sync();
                    RenderTexture lowBank = GlobalBank(0);
                    Check(lowBank != null && lowBank.width == 1024, "None must keep the Low procedural atlas.");
                    Check(Shader.GetGlobalFloat("_FerrofluidMenuLightCount") == 2f, "None must keep both procedural lights published.");
                    Check(calls == 7, "The None rebake must run each light once.");
                    ((IDisposable)cache).Dispose();
                    cache = null;
                    Check(Shader.GetGlobalFloat("_FerrofluidMenuLightCount") == 0f, "Dispose must clear the collection count.");
                    for (int index = 0; index < 8; index++)
                        Check(GlobalBank(index) == null, "Dispose must release bank " + index + ".");

                    Check(!Shader.IsKeywordEnabled(SplitKeyword), "Dispose must disable the split variant.");
                }
                finally
                {
                    if (cache != null)
                        ((IDisposable)cache).Dispose();
                }

                void Sync() => cacheType.GetMethod("Synchronize", Instance)!.Invoke(cache, new object[] { definitions, meshes, true });

                RenderTexture GlobalBank(int index) => (RenderTexture)Shader.GetGlobalTexture(BankName(index))!;

                void CheckBank(RenderTexture bank, string name)
                {
                    Check(bank != null && bank.IsCreated(), name + " must be created.");
                    Check(bank.width == 16384 && bank.height == 16384 && bank.depth == 0, name + " must be 16384 square without depth.");
                    Check(bank.format == RenderTextureFormat.RGB111110Float && bank.dimension == TextureDimension.Tex2DArray, name + " must stay a packed HDR array.");
                    Check(bank.volumeDepth == 1 && bank.useMipMap && bank.mipmapCount == 15, name + " needs one slice and fifteen mip levels.");
                    Check(bank.filterMode == FilterMode.Trilinear && bank.wrapMode == TextureWrapMode.Clamp && bank.anisoLevel == 16, name + " needs trilinear/clamp/aniso16 filtering.");
                }

                long BankBytes(RenderTexture bank)
                {
                    long bytes = 0;
                    for (int size = bank.width; size > 0; size >>= 1)
                        bytes += (long)size * size * 4;

                    return bytes * bank.volumeDepth;
                }

                Color MipTail(RenderTexture bank)
                {
                    RenderTexture single = RenderTexture.GetTemporary(1, 1, 0, bank.format, RenderTextureReadWrite.Linear);
                    try
                    {
                        Graphics.CopyTexture(bank, 0, bank.mipmapCount - 1, single, 0, 0);
                        return FerrofluidValidationRuntime.Read(single)[0];
                    }
                    finally
                    {
                        RenderTexture.ReleaseTemporary(single);
                    }
                }

                object Definition(string id, string shape, Color color)
                {
                    object definition = Activator.CreateInstance(definitionType, true)!;
                    Set(definition, "Id", id);
                    Set(definition, "Type", Enum.Parse(plugin.GetType("ChromaGLS.Settings.MenuLightType", true)!, shape));
                    object value = settingsType.GetField("Default", Static)!.GetValue(null)!;
                    foreach ((string field, float amount) in new[]
                    {
                        ("Brightness", 0.6f), ("Red", color.r), ("Green", color.g), ("Blue", color.b),
                        ("BloomWidthScale", 0f), ("BloomBrightness", 0f),
                    })
                    {
                        value = settingsType.GetMethod("With" + field, Instance)!.Invoke(value, new object[] { amount });
                    }

                    Set(definition, "Appearance", value);
                    return definition;
                }

                object BuildMeshes(object definition)
                {
                    object[] arguments = { enclosure, Vector3.zero, Vector3.forward, definition, null! };
                    bool valid = (bool)plugin.GetType("ChromaGLS.Settings.MenuLightGeometry", true)!.GetMethod("TryCreate", Static)!.Invoke(null, arguments)!;
                    Check(valid, "The test light must fit the real enclosure.");
                    return arguments[4];
                }
            }
        }

        private static object Get(object target, string property) => target.GetType().GetProperty(property, Instance)!.GetValue(target)!;

        private static void Set(object target, string property, object value) => target.GetType().GetProperty(property, Instance)!.SetValue(target, value);

        private sealed class BankGlobals : IDisposable
        {
            private readonly Texture[] _banks = new Texture?[8];
            private readonly Texture _room = Shader.GetGlobalTexture("_FerrofluidRoomEnvironment");
            private readonly Texture _floor = Shader.GetGlobalTexture("_FerrofluidFloorReflection");
            private readonly Texture _parabola = Shader.GetGlobalTexture("_FerrofluidParabolaReflection");
            private readonly float _count = Shader.GetGlobalFloat("_FerrofluidMenuLightCount");
            private readonly float _slices = Shader.GetGlobalFloat("_FerrofluidMenuLightSlicesPerArray");
            private readonly float _legacy = Shader.GetGlobalFloat("_FerrofluidParabolaReflectionEnabled");
            private readonly float _floorY = Shader.GetGlobalFloat("_FerrofluidFloorReflectionY");
            private readonly float _parabolaY = Shader.GetGlobalFloat("_FerrofluidParabolaReflectionY");
            private readonly Vector4 _floorBounds = Shader.GetGlobalVector("_FerrofluidFloorReflectionBounds");
            private readonly Vector4 _parabolaBounds = Shader.GetGlobalVector("_FerrofluidParabolaReflectionBounds");
            private readonly Vector4 _strengths = Shader.GetGlobalVector("_FerrofluidReflectionStrengths");
            private readonly Matrix4x4[] _matrices = Shader.GetGlobalMatrixArray("_FerrofluidMenuLightWorldToLocal") ?? Array.Empty<Matrix4x4>();
            private readonly Vector4[] _bounds = Shader.GetGlobalVectorArray("_FerrofluidMenuLightBounds") ?? Array.Empty<Vector4>();
            private readonly Vector4[] _flags = Shader.GetGlobalVectorArray("_FerrofluidMenuLightFlags") ?? Array.Empty<Vector4>();
            private readonly bool _roomKeyword = Shader.IsKeywordEnabled("FERROFLUID_ROOM_REFLECTIONS");
            private readonly bool _noRoomKeyword = Shader.IsKeywordEnabled("FERROFLUID_NO_REFLECTIONS");
            private readonly bool _splitKeyword = Shader.IsKeywordEnabled(SplitKeyword);
            private readonly RenderTexture _active = RenderTexture.active;
            private readonly bool _srgb = GL.sRGBWrite;

            internal BankGlobals()
            {
                for (int index = 0; index < _banks.Length; index++)
                    _banks[index] = Shader.GetGlobalTexture(BankName(index));
            }

            public void Dispose()
            {
                for (int index = 0; index < _banks.Length; index++)
                    Shader.SetGlobalTexture(BankName(index), _banks[index]);

                Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", _room);
                Shader.SetGlobalTexture("_FerrofluidFloorReflection", _floor);
                Shader.SetGlobalTexture("_FerrofluidParabolaReflection", _parabola);
                Shader.SetGlobalFloat("_FerrofluidMenuLightCount", _count);
                Shader.SetGlobalFloat("_FerrofluidMenuLightSlicesPerArray", _slices);
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionEnabled", _legacy);
                Shader.SetGlobalFloat("_FerrofluidFloorReflectionY", _floorY);
                Shader.SetGlobalFloat("_FerrofluidParabolaReflectionY", _parabolaY);
                Shader.SetGlobalVector("_FerrofluidFloorReflectionBounds", _floorBounds);
                Shader.SetGlobalVector("_FerrofluidParabolaReflectionBounds", _parabolaBounds);
                Shader.SetGlobalVector("_FerrofluidReflectionStrengths", _strengths);
                Shader.SetGlobalMatrixArray("_FerrofluidMenuLightWorldToLocal", _matrices.Length > 0 ? _matrices : new Matrix4x4[8]);
                Shader.SetGlobalVectorArray("_FerrofluidMenuLightBounds", _bounds.Length > 0 ? _bounds : new Vector4[8]);
                Shader.SetGlobalVectorArray("_FerrofluidMenuLightFlags", _flags.Length > 0 ? _flags : new Vector4[8]);
                SetGlobalKeyword("FERROFLUID_ROOM_REFLECTIONS", _roomKeyword);
                SetGlobalKeyword("FERROFLUID_NO_REFLECTIONS", _noRoomKeyword);
                SetGlobalKeyword(SplitKeyword, _splitKeyword);
                RenderTexture.active = _active;
                GL.sRGBWrite = _srgb;
            }

            private static void SetGlobalKeyword(string keyword, bool enabled)
            {
                if (enabled)
                    Shader.EnableKeyword(keyword);
                else
                    Shader.DisableKeyword(keyword);
            }
        }
    }
}
