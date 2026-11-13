#if FERROFLUID_TWEAKS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HMUI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ChromaGLS.Settings
{
    internal static partial class FerrofluidSnapshotCapture
    {
        private static readonly string[] GlobalFloatNames =
        {
            "_FerrofluidFloorReflectionY",
            "_FerrofluidRoomFloorY",
            "_FerrofluidParabolaReflectionY",
            "_FerrofluidParabolaReflectionEnabled",
            "_BaseColorBoost",
            "_BaseColorBoostThreshold",
            "_StereoCameraEyeOffset",
            "_FerrofluidMenuLightCount",
            "_FerrofluidMenuLightSlicesPerArray",
        };

        private static readonly string[] GlobalVectorNames =
        {
            "_FerrofluidFloorReflectionBounds",
            "_FerrofluidParabolaReflectionBounds",
            "_FerrofluidRoomCapturePosition",
            "_FerrofluidRoomLogoPlane",
            "_CustomFogTextureToScreenRatio",
        };

        private static readonly string[] GlobalMatrixArrayNames =
        {
            "_FerrofluidMenuLightWorldToLocal",
        };

        private static readonly string[] GlobalVectorArrayNames =
        {
            "_FerrofluidMenuLightBounds",
            "_FerrofluidMenuLightFlags",
        };

        private static readonly string[] GlobalTextureNames =
        {
            "_FerrofluidRoomEnvironment",
            "_FerrofluidFloorReflection",
            "_FerrofluidParabolaReflection",
            "_BloomPrePassTexture",
            "_FerrofluidMenuLightReflections",
            "_FerrofluidMenuLightReflections1",
            "_FerrofluidMenuLightReflections2",
            "_FerrofluidMenuLightReflections3",
            "_FerrofluidMenuLightReflections4",
            "_FerrofluidMenuLightReflections5",
            "_FerrofluidMenuLightReflections6",
            "_FerrofluidMenuLightReflections7",
        };

#if V1_29_1
        private static readonly string[] LegacyGlobalKeywords =
        {
            "FERROFLUID_ROOM_REFLECTIONS",
            "FERROFLUID_ENVIRONMENT_CAPTURE",
            "MAIN_EFFECT_ENABLED",
            "BLOOM_FOG_ENABLED",
            "LEGACY_AUTOEXPOSURE",
            "UNITY_SINGLE_PASS_STEREO",
            "STEREO_INSTANCING_ON",
            "STEREO_MULTIVIEW_ON",
        };
#endif

        private static readonly Vector3[] CubeFaceForward = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        private static readonly Vector3[] CubeFaceUp = { Vector3.up, Vector3.up, Vector3.back, Vector3.forward, Vector3.up, Vector3.up };
        private static readonly string[] CubeFaceNames = { "positiveX", "negativeX", "positiveY", "negativeY", "positiveZ", "negativeZ" };

        private static JObject BuildRenderingContext()
        {
            var keywords = new JArray();
#if V1_29_1
            const bool hasCompleteGlobalKeywords = false;

            // Unity 2019 only supports checking known global keyword names.
            foreach (string keyword in LegacyGlobalKeywords)
            {
                if (Shader.IsKeywordEnabled(keyword))
                    keywords.Add(keyword);
            }
#else
            const bool hasCompleteGlobalKeywords = true;
            foreach (UnityEngine.Rendering.GlobalKeyword keyword in Shader.globalKeywords)
            {
                if (Shader.IsKeywordEnabled(keyword.name))
                    keywords.Add(keyword.name);
            }
#endif

            var floats = new JObject();
            foreach (string name in GlobalFloatNames)
                floats[name] = Shader.GetGlobalFloat(name);

            var vectors = new JObject();
            foreach (string name in GlobalVectorNames)
                vectors[name] = ToArray(Shader.GetGlobalVector(name));

            var matrixArrays = new JObject();
            foreach (string name in GlobalMatrixArrayNames)
            {
                Matrix4x4[]? slots = Shader.GetGlobalMatrixArray(name);
                matrixArrays[name] = slots == null ? new JArray() : new JArray(slots.Select(matrix => ToArray(matrix)));
            }

            var vectorArrays = new JObject();
            foreach (string name in GlobalVectorArrayNames)
            {
                Vector4[]? slots = Shader.GetGlobalVectorArray(name);
                vectorArrays[name] = slots == null ? new JArray() : new JArray(slots.Select(vector => ToArray(vector)));
            }

            var textures = new JObject();
            foreach (string name in GlobalTextureNames)
            {
                Texture? texture = Shader.GetGlobalTexture(name);
                textures[name] = texture != null ? DescribeTexture(texture) : JValue.CreateNull();
            }

            var context = new JObject
            {
                ["globalKeywords"] = keywords,
                ["globalKeywordsComplete"] = hasCompleteGlobalKeywords,
                ["globalFloats"] = floats,
                ["globalVectors"] = vectors,
                ["globalMatrixArrays"] = matrixArrays,
                ["globalVectorArrays"] = vectorArrays,
                ["matrixArrayLayout"] = "column-major",
                ["globalTextures"] = textures,
            };

            foreach (MainEffectController controller in Resources.FindObjectsOfTypeAll<MainEffectController>())
            {
                if (!controller.enabled || !controller.gameObject.activeInHierarchy || controller.gameObject.name != "MenuMainCamera")
                    continue;
#if PRE_V1_42_1
                MainEffectContainerSO container = controller._mainEffectContainer;
#else
                MainEffectContainerSO container = controller.container;
#endif
                if (container == null || container.mainEffect == null)
                    continue;
                context["nativeMainEffect"] = DescribeMainEffect(controller, container.mainEffect);
                break;
            }

            if (context["nativeMainEffect"] == null)
                context["nativeMainEffect"] = JValue.CreateNull();

            try
            {
                context["menuGeometry"] = BuildMenuGeometry();
            }
            catch (Exception error)
            {
                context["menuGeometry"] = new JObject { ["error"] = error.Message };
            }

            return context;
        }

        private static JObject BuildMenuGeometry()
        {
            var geometry = new JObject();
            var screens = new JObject();
            CurvedCanvasSettings? curve = null;
            foreach (ScreenSystem system in Resources.FindObjectsOfTypeAll<ScreenSystem>())
            {
                if (system == null || !system.gameObject.activeInHierarchy || !system.gameObject.scene.IsValid())
                    continue;

                screens["main"] = DescribeScreen(system.mainScreen);
                screens["left"] = DescribeScreen(system.leftScreen);
                screens["right"] = DescribeScreen(system.rightScreen);
                screens["bottom"] = DescribeScreen(system.bottomScreen);
                screens["top"] = DescribeScreen(system.topScreen);
                if (system.mainScreen != null)
                    curve = system.mainScreen.GetComponentInParent<CurvedCanvasSettings>();
                break;
            }

            geometry["screens"] = screens;
            geometry["curvedCanvas"] = curve == null
                ? JValue.CreateNull()
                : new JObject
                {
                    ["localToWorld"] = ToArray(curve.transform.localToWorldMatrix),
                    ["worldToLocal"] = ToArray(curve.transform.worldToLocalMatrix),
                    ["radius"] = curve.radius,
                };
            geometry["fogRing"] = DescribeFogRing();
            return geometry;
        }

        private static JObject DescribeScreen(HMUI.Screen? screen)
        {
            if (screen == null)
                return new JObject { ["available"] = false };
            var entry = new JObject
            {
                ["available"] = true,
                ["localToWorld"] = ToArray(screen.transform.localToWorldMatrix),
                ["worldToLocal"] = ToArray(screen.transform.worldToLocalMatrix),
            };
            if (screen.transform is RectTransform rect)
                entry["rect"] = new JArray(rect.rect.x, rect.rect.y, rect.rect.width, rect.rect.height);
            return entry;
        }

        private static JObject DescribeFogRing()
        {
            foreach (BloomPrePassNonLightPass pass in BloomPrePassNonLightPass._bloomPrePassBeforeBlurList
                .Concat(BloomPrePassNonLightPass._bloomPrePassAfterBlurList))
            {
                if (pass is not BloomPrePassBackgroundNonLightRenderer background)
                    continue;
                Renderer? renderer = background.renderer;
                if (renderer == null || renderer.gameObject.name != "MenuFogRing")
                    continue;

                var entry = new JObject
                {
                    ["available"] = true,
                    ["rendererName"] = renderer.name,
                    ["enabled"] = renderer.enabled,
                    ["active"] = renderer.gameObject.activeInHierarchy,
                    ["localToWorld"] = ToArray(renderer.transform.localToWorldMatrix),
                    ["worldToLocal"] = ToArray(renderer.transform.worldToLocalMatrix),
                    ["bounds"] = ToArray(renderer.bounds),
                };
                if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter))
                {
                    Mesh? mesh = filter.sharedMesh;
                    entry["meshName"] = mesh != null ? mesh.name : null;
                    if (mesh != null)
                    {
                        entry["localBounds"] = ToArray(mesh.bounds);
                        if (mesh.isReadable)
                        {
                            entry["vertices"] = new JArray(mesh.vertices.Select(v => new JArray(v.x, v.y, v.z)));
                            entry["uv"] = new JArray(mesh.uv.Select(v => new JArray(v.x, v.y)));
                            entry["triangles"] = new JArray(mesh.triangles);
                        }
                        else
                        {
                            entry["meshReadable"] = false;
                        }
                    }
                }

                if (renderer.sharedMaterial != null)
                {
                    entry["material"] = new JObject
                    {
                        ["name"] = renderer.sharedMaterial.name,
                        ["shader"] = renderer.sharedMaterial.shader != null ? renderer.sharedMaterial.shader.name : null,
                        ["color"] = renderer.sharedMaterial.HasProperty("_Color")
                            ? ToArray(renderer.sharedMaterial.GetColor("_Color"))
                            : null,
                    };
                }

                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                entry["propertyBlock"] = new JObject
                {
                    ["present"] = !block.isEmpty,
#if !V1_29_1
                    ["color"] = block.HasColor("_Color") ? ToArray(block.GetColor("_Color")) : null,
#else
                    ["color"] = null,
#endif
                };

                return entry;
            }

            return new JObject { ["available"] = false };
        }

        private static JObject DescribeMainEffect(MainEffectController controller, MainEffectSO effect)
        {
            var entry = new JObject
            {
                ["controllerName"] = controller.name,
                ["enabled"] = controller.enabled,
                ["effectType"] = effect.GetType().Name,
                ["hasPostProcessEffect"] = effect.hasPostProcessEffect,
            };
            if (effect is PyramidBloomMainEffectSO pyramid)
            {
                entry["bloomBlendFactor"] = pyramid._bloomBlendFactor;
                entry["bloomRadius"] = pyramid._bloomRadius;
                entry["bloomIntensity"] = pyramid._bloomIntensity;
                entry["baseColorBoost"] = pyramid._baseColorBoost;
                entry["baseColorBoostThreshold"] = pyramid._baseColorBoostThreshold;
            }

            if (effect is KawaseBloomMainEffectSO kawase)
            {
                entry["bloomIntensity"] = kawase._bloomIntensity;
                entry["bloomBoost"] = kawase._bloomBoost;
                entry["baseColorBoost"] = kawase._baseColorBoost;
                entry["baseColorBoostThreshold"] = kawase._baseColorBoostThreshold;
            }

            return entry;
        }

        private static JObject DescribeTexture(Texture texture)
        {
            var descriptor = new JObject
            {
                ["name"] = texture.name,
                ["instanceId"] = texture.GetInstanceID(),
                ["type"] = texture.GetType().Name,
                ["dimension"] = texture.dimension.ToString(),
                ["width"] = texture.width,
                ["height"] = texture.height,
                ["filterMode"] = texture.filterMode.ToString(),
                ["wrapModeU"] = texture.wrapModeU.ToString(),
                ["wrapModeV"] = texture.wrapModeV.ToString(),
                ["wrapModeW"] = texture.wrapModeW.ToString(),
                ["anisoLevel"] = texture.anisoLevel,
                ["mipMapBias"] = texture.mipMapBias,
                ["files"] = new JArray(),
            };
            if (texture is RenderTexture renderTexture)
            {
                descriptor["graphicsFormat"] = renderTexture.graphicsFormat.ToString();
                descriptor["mipcount"] = renderTexture.mipmapCount;
                descriptor["isCreated"] = renderTexture.IsCreated();
                descriptor["volumeDepth"] = renderTexture.volumeDepth;
                descriptor["vrUsage"] = renderTexture.vrUsage.ToString();
            }
            else
            {
                descriptor["graphicsFormat"] = texture.graphicsFormat.ToString();
                descriptor["mipcount"] = texture.mipmapCount;
            }

            return descriptor;
        }

        private static JArray ToArray(Vector4 vector) => new(vector.x, vector.y, vector.z, vector.w);

        private static JArray ToArray(Color color) => new(color.r, color.g, color.b, color.a);

        private static JArray ToArray(Matrix4x4 matrix)
        {
            var array = new JArray();
            for (int index = 0; index < 16; index++)
                array.Add(matrix[index]);
            return array;
        }

        private static JArray ToArray(Bounds bounds)
            => new(ToArray((Vector4)bounds.center), ToArray((Vector4)bounds.size));

        private static string EmbeddedBundleSha256()
        {
            using Stream? stream = typeof(FerrofluidGpuResources).Assembly.GetManifestResourceStream(FerrofluidGpuResources.BundleResourceName);
            if (stream == null)
                return "<missing>";
            using System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create();
            byte[] digest = hash.ComputeHash(stream);
            var builder = new System.Text.StringBuilder(digest.Length * 2);
            foreach (byte value in digest)
                builder.Append(value.ToString("x2"));
            return builder.ToString();
        }

        // Returns filename -> EXR bytes; the same names appear in each descriptor's files array.
        internal static Dictionary<string, byte[]> CaptureRenderingAssets(JObject rendering, Shader copyShader)
        {
            var files = new Dictionary<string, byte[]>();
            JObject? textures = rendering["globalTextures"] as JObject;
            if (textures == null)
                return files;

            var material = new Material(copyShader) { hideFlags = HideFlags.DontSave };
            RenderTexture? temp = null;
            RenderTexture previousActive = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            GL.sRGBWrite = false;
            try
            {
                foreach (JProperty property in textures.Properties())
                {
                    if (property.Value is not JObject descriptor)
                        continue;
                    JToken? filesToken = descriptor["files"];
                    if (filesToken is not JArray fileList)
                        continue;

                    string name = property.Name.TrimStart('_');
                    descriptor["rowConvention"] = "Unity ReadPixels bottom-left camera-projection rows";
                    descriptor["mipLevel"] = 0;
                    descriptor["readbackFormat"] = "RGBAHalf";
                    descriptor["colourSpace"] = "Linear";
                    var fileMetadata = new JArray();

                    Texture? source = Shader.GetGlobalTexture(property.Name);
                    if (source == null)
                    {
                        descriptor["error"] = "texture became unbound between metadata and readback";
                        continue;
                    }

                    if (source is RenderTexture rt && !rt.IsCreated())
                    {
                        descriptor["error"] = "render texture not created";
                        continue;
                    }

                    int dimension = source.dimension switch
                    {
                        TextureDimension.Tex2D => 0,
                        TextureDimension.Cube => 1,
                        TextureDimension.Tex2DArray => 2,
                        _ => -1,
                    };
                    if (dimension < 0)
                    {
                        descriptor["error"] = "unsupported texture dimension " + source.dimension;
                        continue;
                    }

                    if (dimension == 1)
                    {
                        Cubemap cube = (Cubemap)source;
                        if (cube.mipmapCount < 1)
                        {
                            descriptor["error"] = "cubemap has no mip 0";
                            continue;
                        }

                        material.SetTexture("_Cube", cube);
                        for (int face = 0; face < 6; face++)
                        {
                            temp = GetTemp(source.width, source.height, temp);
                            material.SetVector("_Forward", CubeFaceForward[face]);
                            material.SetVector("_Up", CubeFaceUp[face]);
                            Graphics.Blit(source, temp!, material, 1);
                            byte[] encoded = EncodeReadback(temp!);
                            AddReadbackFile(files, fileList, fileMetadata, name + "-" + CubeFaceNames[face] + ".exr", encoded, face, CubeFaceForward[face], CubeFaceUp[face]);
                        }
                    }
                    else if (dimension == 2)
                    {
                        RenderTexture array = (RenderTexture)source;
                        material.SetTexture("_Array", array);
                        int slices = array.volumeDepth;
                        for (int slice = 0; slice < slices; slice++)
                        {
                            temp = GetTemp(source.width, source.height, temp);
                            material.SetFloat("_Layer", slice);
                            Graphics.Blit(source, temp!, material, 2);
                            byte[] encoded = EncodeReadback(temp!);
                            AddReadbackFile(files, fileList, fileMetadata, name + "-slice" + slice + ".exr", encoded, slice: slice);
                        }
                    }
                    else
                    {
                        temp = GetTemp(source.width, source.height, temp);
                        Graphics.Blit(source, temp!, material, 0);
                        byte[] encoded = EncodeReadback(temp!);
                        AddReadbackFile(files, fileList, fileMetadata, name + ".exr", encoded);
                    }

                    descriptor["fileMetadata"] = fileMetadata;
                }
            }
            finally
            {
                if (temp != null)
                    RenderTexture.ReleaseTemporary(temp);
                RenderTexture.active = previousActive;
                GL.sRGBWrite = previousSrgb;
                Object.Destroy(material);
            }

            return files;
        }

        private static void AddReadbackFile(
            Dictionary<string, byte[]> files,
            JArray fileList,
            JArray fileMetadata,
            string file,
            byte[] encoded,
            int face = -1,
            Vector3? forward = null,
            Vector3? up = null,
            int slice = -1)
        {
            files[file] = encoded;
            fileList.Add(file);
            using System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create();
            var entry = new JObject
            {
                ["file"] = file,
                ["sha256"] = BitConverter.ToString(hash.ComputeHash(encoded)).Replace("-", string.Empty).ToLowerInvariant(),
            };
            if (face >= 0)
            {
                entry["face"] = face;
                entry["forward"] = ToArray(forward!.Value);
                entry["up"] = ToArray(up!.Value);
            }

            if (slice >= 0)
                entry["slice"] = slice;

            fileMetadata.Add(entry);
        }

        private static RenderTexture GetTemp(int width, int height, RenderTexture? reuse)
        {
            if (reuse != null && (reuse.width != width || reuse.height != height))
            {
                RenderTexture.ReleaseTemporary(reuse);
                reuse = null;
            }

            reuse ??= RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            return reuse;
        }

        private static byte[] EncodeReadback(RenderTexture source)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = source;
            Texture2D read = new(source.width, source.height, TextureFormat.RGBAHalf, false, true);
            try
            {
                read.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
                read.Apply();
                byte[] bytes = ImageConversion.EncodeToEXR(read, Texture2D.EXRFlags.CompressZIP);
                return bytes;
            }
            finally
            {
                Object.Destroy(read);
                RenderTexture.active = previous;
            }
        }
    }
}
#endif
