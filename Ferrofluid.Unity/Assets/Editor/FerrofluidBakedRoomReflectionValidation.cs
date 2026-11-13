using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidBakedRoomReflectionValidation
    {
        private const string OnAsset = "assets/generated/room-reflections-on.cubemap";
        private const string OffAsset = "assets/generated/room-reflections-off.cubemap";
        private static readonly Vector3[] FaceForward = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        private static readonly string[] FaceNames = { "positiveX", "negativeX", "positiveY", "negativeY", "positiveZ", "negativeZ" };

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidBakedRoomReflectionValidation));

        private static void RunRuntime()
        {
            Assembly plugin = FerrofluidValidationRuntime.CreateConfig().GetType().Assembly;
            AssetBundle bundle = FerrofluidValidationRuntime.LoadReflectionPack()
                ?? throw new Exception("CHROMAGLS_REFLECTION_PACK must point at a loadable reflection pack.");
            try
            {
                Cubemap on = bundle.LoadAsset<Cubemap>(OnAsset);
                Cubemap off = bundle.LoadAsset<Cubemap>(OffAsset);
                if (on == null || off == null)
                    throw new Exception($"Baked room reflection cubemaps missing from bundle: on={on != null} off={off != null}.");

                Check(on.width == 1024 && off.width == 1024, $"Baked maps must be 1024 per face, got on={on.width} off={off.width}.");
                Check(on.mipmapCount == 11 && off.mipmapCount == 11, $"Baked maps need the full 11-level mip chain, got on={on.mipmapCount} off={off.mipmapCount}.");
                Check(on.graphicsFormat == GraphicsFormat.E5B9G9R9_UFloatPack32 && off.graphicsFormat == GraphicsFormat.E5B9G9R9_UFloatPack32,
                    $"Baked maps must be RGB9e5 packed, got on={on.graphicsFormat} off={off.graphicsFormat}.");
                Check(!on.isReadable && !off.isReadable, "Baked maps must be GPU-only after save/reload; isReadable doubles memory.");

                Shader probe = AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidBakedProbe.shader");
                Material material = new(probe);
                RenderTexture rt = new(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                Texture2D read = new(1, 1, TextureFormat.RGBAFloat, false, true);
                RenderTexture previousActive = RenderTexture.active;
                rt.Create();
                float maxSource = 0f;
                try
                {
                    foreach ((string state, Cubemap map) pair in new[] { ("on", on), ("off", off) })
                    {
                        for (int face = 0; face < 6; face++)
                        {
                            Texture2D source = LoadExr(pair.state, face);
                            Color expected = (
                                source.GetPixel(511, 511)
                                + source.GetPixel(512, 511)
                                + source.GetPixel(511, 512)
                                + source.GetPixel(512, 512)) * 0.25f;
                            float tolerance = Mathf.Max(expected.r, expected.g, expected.b) / 256f + 0.002f;
                            Color sampled = ReadCube(material, rt, read, pair.map, FaceForward[face], 0f);
                            Check(Close(sampled, expected, tolerance), $"{pair.state} face {FaceNames[face]} center sampled {sampled:F4} expected {expected:F4} tolerance {tolerance:F4}.");
                            Color sampledMip = ReadCube(material, rt, read, pair.map, FaceForward[face], 5f);
                            Check(sampledMip.maxColorComponent > 0f, $"{pair.state} face {FaceNames[face]} mip5 sampled black; mip data missing.");
                            maxSource = Mathf.Max(maxSource, expected.maxColorComponent);

                            // Off-center rays anchor every face orientation against the
                            // raw camera texels, not only the face centers.
                            foreach ((int x, int y) texel in new[] { (256, 256), (768, 256), (256, 768), (768, 768) })
                            {
                                Color expectedOffCenter = source.GetPixel(texel.x, texel.y);
                                float offU = (2f * (texel.x + 0.5f) / 1024f) - 1f;
                                float offV = (2f * (texel.y + 0.5f) / 1024f) - 1f;
                                Vector3 offCenterRay = FaceDirection(face, offU, offV).normalized;
                                float texelTolerance = Mathf.Max(expectedOffCenter.r, expectedOffCenter.g, expectedOffCenter.b) / 256f + 0.002f;
                                Color offSampled = ReadCube(material, rt, read, pair.map, offCenterRay, 0f);
                                Check(Close(offSampled, expectedOffCenter, texelTolerance), $"{pair.state} face {FaceNames[face]} texel ({texel.x},{texel.y}) ray ({offCenterRay.x:F3},{offCenterRay.y:F3},{offCenterRay.z:F3}) sampled {offSampled:F4} expected {expectedOffCenter:F4} tolerance {texelTolerance:F4}.");
                            }
                        }
                    }

                    // The flickering E is the texel whose red channel changes most
                    // between the two captures, not the brightest red texel — other
                    // neon letters are also saturated 1.0 in the on capture.
                    Texture2D onZ = LoadExr("on", 4);
                    Texture2D offZ = LoadExr("off", 4);
                    Color[] onZPixels = onZ.GetPixels();
                    Color[] offZPixels = offZ.GetPixels();
                    int logoIndex = 0;
                    float logoDelta = 0f;
                    for (int index = 0; index < onZPixels.Length; index++)
                    {
                        float delta = onZPixels[index].r - offZPixels[index].r;
                        if (delta > logoDelta)
                        {
                            logoDelta = delta;
                            logoIndex = index;
                        }
                    }

                    Check(logoDelta > 0.2f, $"The flickering E should change the most between captures, got max delta {logoDelta:F3}.");
                    Color logoOn = onZPixels[logoIndex];
                    Color logoOff = offZPixels[logoIndex];
                    float logoU = ((logoIndex % 1024) + 0.5f) / 512f - 1f;
                    float logoV = ((logoIndex / 1024) + 0.5f) / 512f - 1f;
                    Vector3 logoDirection = new Vector3(logoU, logoV, 1f).normalized;
                    float onTolerance = Mathf.Max(logoOn.r, logoOn.g, logoOn.b) / 256f + 0.002f;
                    float offTolerance = Mathf.Max(logoOff.r, logoOff.g, logoOff.b) / 256f + 0.002f;
                    Color sampledOnLogo = ReadCube(material, rt, read, on, logoDirection, 0f);
                    Color sampledOffLogo = ReadCube(material, rt, read, off, logoDirection, 0f);
                    Check(Close(sampledOnLogo, logoOn, onTolerance), $"On-map E texel {logoOn:F4} sampled {sampledOnLogo:F4} tolerance {onTolerance:F4}.");
                    Check(Close(sampledOffLogo, logoOff, offTolerance), $"Off-map E texel {logoOff:F4} sampled {sampledOffLogo:F4} tolerance {offTolerance:F4}.");
                    Check(sampledOnLogo.r > sampledOffLogo.r + 0.2f, $"E on/off must differ, on={sampledOnLogo.r:F3} off={sampledOffLogo.r:F3}.");

                    // Global HDR proof: find the actual brightest texel across all 12
                    // source faces and verify that exact peak survives the bake.
                    float peakValue = 0f;
                    int peakFace = -1;
                    int peakIndex = 0;
                    string peakState = "on";
                    Color peakColor = Color.black;
                    for (int face = 0; face < 6; face++)
                    {
                        foreach (string state in new[] { "on", "off" })
                        {
                            Color[] pixels = LoadExr(state, face).GetPixels();
                            for (int index = 0; index < pixels.Length; index++)
                                if (pixels[index].maxColorComponent > peakValue)
                                {
                                    peakValue = pixels[index].maxColorComponent;
                                    peakFace = face;
                                    peakIndex = index;
                                    peakState = state;
                                    peakColor = pixels[index];
                                }
                        }
                    }

                    if (peakValue <= 1f)
                        throw new Exception($"No source HDR above one exists: global source peak={peakValue:F3} at face={peakFace} index={peakIndex}.");
                    float peakU = ((peakIndex % 1024) + 0.5f) / 512f - 1f;
                    float peakV = ((peakIndex / 1024) + 0.5f) / 512f - 1f;
                    Vector3 peakDirection = FaceDirection(peakFace, peakU, peakV).normalized;
                    Color peakSampled = ReadCube(material, rt, read, peakState == "on" ? on : off, peakDirection, 0f);
                    float peakTolerance = peakColor.maxColorComponent / 256f + 0.002f;
                    Check(Close(peakSampled, peakColor, peakTolerance), $"Baked map must preserve the source HDR peak {peakColor:F4} at {peakState} face={peakFace}, sampled {peakSampled:F4} tolerance {peakTolerance:F4}.");
                    maxSource = peakValue;
                    Check(peakSampled.maxColorComponent > 1f, $"Baked map must preserve source HDR: source peak={peakValue:F3} face={peakFace} sampled={peakSampled.maxColorComponent:F3}.");
                    Debug.Log($"[FerrofluidBakedRoomReflectionValidation] HDR peak source={peakValue:F3} face={peakFace} state={peakState} sampled={peakSampled.maxColorComponent:F3}");
                }
                finally
                {
                    RenderTexture.active = previousActive;
                    Object.DestroyImmediate(rt);
                    Object.DestroyImmediate(read);
                    Object.DestroyImmediate(material);
                }

                ValidateBakedFloor(bundle);
                Debug.Log($"[FerrofluidBakedRoomReflectionValidation] baked on bytes={Profiler.GetRuntimeMemorySizeLong(on)} off bytes={Profiler.GetRuntimeMemorySizeLong(off)} maxSampledSource={maxSource:F3} HDR above one={(maxSource > 1f ? "confirmed" : "not present")}");
                Debug.Log("[FerrofluidBakedRoomReflectionValidation] PASS: bundle pair, 1024x11mips, RGB9e5, GPU-only after reload, all six faces + mips, logo direction and on/off difference.");
            }
            finally
            {
                bundle.Unload(true);
            }
        }

        private static Texture2D LoadExr(string state, int face)
        {
            string path = $"Assets/Generated/RoomReflections/{state}-{face}-{FaceNames[face]}.exr";
            Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (source == null)
                throw new Exception($"Baked source EXR import missing at {path}.");

            return source;
        }

        internal static Color ReadCube(Material material, RenderTexture rt, Texture2D read, Cubemap cube, Vector3 direction, float mip)
        {
            material.SetTexture("_Cube", cube);
            material.SetVector("_Direction", direction);
            material.SetFloat("_FaceMip", mip);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(null, rt, material);
            try
            {
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0f, 0f, 1f, 1f), 0, 0);
                read.Apply();
                return read.GetPixel(0, 0);
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        [Serializable]
        private sealed class FloorReflectionManifest
        {
            public FloorReflection floorReflection;
        }

        [Serializable]
        private sealed class FloorReflection
        {
            public float minimumX;
            public float minimumZ;
            public float inverseWidth;
            public float inverseDepth;
            public float planeY;
        }

        private static void ValidateBakedFloor(AssetBundle bundle)
        {
            Texture2D floor = bundle.LoadAsset<Texture2D>("assets/generated/floor-feet.asset");
            Check(floor != null, "Baked floor-feet texture missing from bundle.");
            Check(floor.width == 1024 && floor.height == 1024, $"Baked floor must be 1024x1024, got {floor.width}x{floor.height}.");
            Check(floor.mipmapCount == 11, $"Baked floor needs the full 11-level mip chain, got {floor.mipmapCount}.");
            Check(floor.graphicsFormat == GraphicsFormat.E5B9G9R9_UFloatPack32, $"Baked floor must be RGB9e5 packed, got {floor.graphicsFormat}.");
            Check(!floor.isReadable, "Baked floor must be GPU-only after save/reload; isReadable doubles memory.");

            Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Generated/RoomReflections/floor-feet.exr");
            Check(source != null, "Native floor-feet EXR import missing for bake comparison.");
            RenderTexture rt = new(1024, 1024, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            RenderTexture mip = new(4, 4, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(floor, rt);
                RenderTexture.active = rt;
                Texture2D copy = new(1024, 1024, TextureFormat.RGBAFloat, false, true);
                copy.ReadPixels(new Rect(0f, 0f, 1024f, 1024f), 0, 0);
                copy.Apply();
                Color[] copied = copy.GetPixels();
                Object.DestroyImmediate(copy);
                Color[] expected = source.GetPixels();
                int nonzero = 0;
                float brightest = 0f;
                int brightestIndex = 0;
                for (int index = 0; index < expected.Length; index++)
                {
                    float peak = expected[index].maxColorComponent;
                    if (peak > 0f)
                        nonzero++;
                    if (peak > brightest)
                    {
                        brightest = peak;
                        brightestIndex = index;
                    }
                }

                int compared = 0;
                for (int index = 0; index < expected.Length; index += 97)
                {
                    float tolerance = Mathf.Max(expected[index].r, expected[index].g, expected[index].b) / 256f + 0.002f;
                    Check(Close(copied[index], expected[index], tolerance), $"Baked floor texel {index} sampled {copied[index]:F4} expected {expected[index]:F4} tolerance {tolerance:F4}.");
                    compared++;
                }

                Check(nonzero > 0, "Native floor source has no nonzero texels.");
                float brightestTolerance = brightest / 256f + 0.002f;
                Check(Close(copied[brightestIndex], expected[brightestIndex], brightestTolerance), $"Baked floor brightest texel {expected[brightestIndex]:F4} sampled {copied[brightestIndex]:F4}.");

                Graphics.Blit(floor, mip);
                RenderTexture.active = mip;
                Texture2D small = new(4, 4, TextureFormat.RGBAFloat, false, true);
                small.ReadPixels(new Rect(0f, 0f, 4f, 4f), 0, 0);
                small.Apply();
                float mipPeak = 0f;
                foreach (Color pixel in small.GetPixels())
                    mipPeak = Mathf.Max(mipPeak, pixel.maxColorComponent);
                Object.DestroyImmediate(small);
                Check(mipPeak > 0f, "Baked floor minified to 4x4 read black; mip data missing.");
            }
            finally
            {
                RenderTexture.active = previous;
                rt.Release();
                mip.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(mip);
            }

            TextAsset manifest = bundle.LoadAsset<TextAsset>("assets/generated/room-reflections.json");
            Check(manifest != null, "Baked room-reflections.json manifest missing from bundle.");
            FloorReflectionManifest parsed = JsonUtility.FromJson<FloorReflectionManifest>(manifest.text);
            Check(parsed != null && parsed.floorReflection != null, "Bundled manifest is missing the floorReflection object.");
            Check(Mathf.Approximately(parsed.floorReflection.minimumX, -2f)
                && Mathf.Approximately(parsed.floorReflection.minimumZ, -2f)
                && Mathf.Approximately(parsed.floorReflection.inverseWidth, 0.25f)
                && Mathf.Approximately(parsed.floorReflection.inverseDepth, 0.25f)
                && Mathf.Approximately(parsed.floorReflection.planeY, 0.005f),
                $"Bundled floor reflection metadata diverged from the capture: min=({parsed.floorReflection.minimumX},{parsed.floorReflection.minimumZ}) inv=({parsed.floorReflection.inverseWidth},{parsed.floorReflection.inverseDepth}) y={parsed.floorReflection.planeY}.");
            Debug.Log("[FerrofluidBakedRoomReflectionValidation] floor: 1024x11mips RGB9e5 GPU-only, source content + brightest match, minified nonblack, manifest bounds (-2,-2,0.25,0.25) y=0.005 verified.");
        }

        // Maps a source texel to its camera ray: capture cameras face each FaceForward
        // with the manifest faceUp basis (+Y up for side faces, -Z for +Y, +Z for -Y),
        // so source +v maps toward camera up.
        internal static Vector3 FaceDirection(int face, float u, float v)
            => face switch
            {
                0 => new Vector3(1f, v, -u),
                1 => new Vector3(-1f, v, u),
                2 => new Vector3(u, 1f, -v),
                3 => new Vector3(u, -1f, v),
                4 => new Vector3(u, v, 1f),
                _ => new Vector3(-u, v, -1f),
            };

        internal static bool Close(Color a, Color b, float tolerance)
            => Mathf.Abs(a.r - b.r) <= tolerance && Mathf.Abs(a.g - b.g) <= tolerance && Mathf.Abs(a.b - b.b) <= tolerance;

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }
    }
}
