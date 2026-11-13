using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidHighBakedReflectionValidation
    {
        private const string ImportDirectory = "Assets/Generated/RoomReflectionsHigh/";
        private static readonly string[] Faces = { "positiveX", "negativeX", "positiveY", "negativeY", "positiveZ", "negativeZ" };

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidHighBakedReflectionValidation));

        private static void RunRuntime()
        {
            Assembly plugin = FerrofluidValidationRuntime.CreateConfig().GetType().Assembly;
            AssetBundle bundle = FerrofluidValidationRuntime.LoadReflectionPack()
                ?? throw new InvalidOperationException("CHROMAGLS_REFLECTION_PACK must point at a loadable reflection pack.");
            Check(bundle != null, "The reflection pack must load before checking High assets.");
            Material probe = null;
            RenderTexture target = null;
            Texture2D read = null;
            RenderTexture previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            try
            {
                Cubemap low = bundle.LoadAsset<Cubemap>("assets/generated/room-reflections-on.cubemap");
                Check(low != null && low.width == 1024, "The existing Low room map must remain available.");
                Cubemap on = bundle.LoadAsset<Cubemap>("assets/generated/room-reflections-high-on.cubemap");
                Cubemap off = bundle.LoadAsset<Cubemap>("assets/generated/room-reflections-high-off.cubemap");
                Texture2D feet = bundle.LoadAsset<Texture2D>("assets/generated/floor-feet-high.asset");
                TextAsset metadata = bundle.LoadAsset<TextAsset>("assets/generated/room-reflections-high.json");
                Check(on != null && off != null && feet != null && metadata != null, "High must include separate higher-detail room, feet and provenance assets, not reuse Low.");
                Check(on.width == 2048 && off.width == 2048 && on.mipmapCount == 12 && off.mipmapCount == 12, "High room maps must be full-mip 2048 cubemaps for both neon states.");
                Check(feet.width == 4096 && feet.height == 4096 && feet.mipmapCount == 13, "High feet must retain the genuine 4096 capture and full mip chain.");
                Check(on.graphicsFormat == GraphicsFormat.E5B9G9R9_UFloatPack32
                    && off.graphicsFormat == GraphicsFormat.E5B9G9R9_UFloatPack32
                    && feet.graphicsFormat == GraphicsFormat.E5B9G9R9_UFloatPack32, "All High baked maps must retain packed HDR format.");
                Check(!on.isReadable && !off.isReadable && !feet.isReadable, "High must not keep duplicate CPU-readable textures at runtime.");
                Check(on.filterMode == FilterMode.Trilinear && off.filterMode == FilterMode.Trilinear && feet.filterMode == FilterMode.Trilinear, "High maps must retain mip-filtered sampling.");
                JObject manifest = JObject.Parse(metadata.text);
                JObject processing = (JObject)manifest["roomRadianceProcessing"];
                Check(processing != null && processing.Value<int>("sourceCaptureFaceSize") == 4096, "High provenance must describe the fresh native 4096 source, not an upscale.");
                Check(((JObject)processing["sourceFilesSha256"]).Count == 25, "High provenance must identify every composed on/off face, isolated logo face and feet source.");
                Check(manifest.Value<int>("faceSize") == 2048
                    && manifest["floorReflection"].Value<int>("width") == 4096
                    && manifest["floorReflection"].Value<int>("height") == 4096, "The bundled mapping manifest must describe actual High dimensions.");
                probe = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidBakedProbe.shader"));
                target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                Check(target.Create(), "The HDR readback target must be available.");
                read = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
                GL.sRGBWrite = false;
                foreach ((string state, Cubemap cube) pair in new[] { ("on", on), ("off", off) })
                {
                    for (int face = 0; face < 6; face++)
                    {
                        Texture2D source = Source(pair.state + "-" + face + "-" + Faces[face] + ".exr", 2048);
                        foreach ((int x, int y) pixel in new[] { (256, 256), (768, 768), (1024, 1024), (1536, 768), (1792, 1792) })
                        {
                            Vector3 ray = Ray(face, pixel.x, pixel.y);
                            Color expected = source.GetPixel(pixel.x, pixel.y);
                            Color actual = FerrofluidBakedRoomReflectionValidation.ReadCube(probe, target, read, pair.cube, ray, 0f);
                            Match(actual, expected, pair.state + "/" + Faces[face] + " texel " + pixel);
                        }

                        Color mip = FerrofluidBakedRoomReflectionValidation.ReadCube(probe, target, read, pair.cube, Ray(face, 1024, 1024), 5f);
                        Check(mip.maxColorComponent > 0f, "Every High face must retain captured room radiance through its mip chain.");
                    }
                }

                ValidateLogo(probe, target, read, on, off);
                ValidateFeet(target, read, feet);
                Debug.Log("[FerrofluidHighBakedReflectionValidation] PASS: distinct 2048 room pair, 4096 feet, full packed-HDR mips, GPU-only residency, genuine-source provenance, all-six-face orientation, neon switching and planar pixel content.");
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgb;
                if (target != null)
                {
                    target.Release();
                    Object.DestroyImmediate(target);
                }
                if (read != null)
                    Object.DestroyImmediate(read);
                if (probe != null)
                    Object.DestroyImmediate(probe);
                bundle.Unload(true);
            }
        }

        private static void ValidateLogo(Material material, RenderTexture target, Texture2D read, Cubemap on, Cubemap off)
        {
            Color[] onPixels = Source("on-4-positiveZ.exr", 2048).GetPixels();
            Color[] offPixels = Source("off-4-positiveZ.exr", 2048).GetPixels();
            int peak = 0;
            float delta = 0f;
            for (int index = 0; index < onPixels.Length; index++)
            {
                float difference = onPixels[index].r - offPixels[index].r;
                if (difference > delta)
                {
                    delta = difference;
                    peak = index;
                }
            }

            Check(delta > 0.2f, "The captured flickering neon must remain distinct in High on/off sources.");
            Vector3 ray = Ray(4, peak % 2048, peak / 2048);
            Match(FerrofluidBakedRoomReflectionValidation.ReadCube(material, target, read, on, ray, 0f), onPixels[peak], "High neon on");
            Match(FerrofluidBakedRoomReflectionValidation.ReadCube(material, target, read, off, ray, 0f), offPixels[peak], "High neon off");
        }

        private static void ValidateFeet(RenderTexture target, Texture2D read, Texture2D feet)
        {
            Texture2D source = Source("floor-feet.exr", 4096);
            Color[] pixels = source.GetPixels();
            int peak = 0;
            float maximum = 0f;
            for (int index = 0; index < pixels.Length; index++)
            {
                if (pixels[index].maxColorComponent > maximum)
                {
                    maximum = pixels[index].maxColorComponent;
                    peak = index;
                }
            }

            Check(maximum > 0f, "The native High feet source must contain reflected light.");
            Probe(peak % 4096, peak / 4096);
            for (int y = 128; y < 4096; y += 512)
            {
                for (int x = 128; x < 4096; x += 512)
                    Probe(x, y);
            }

            void Probe(int x, int y)
            {
                Graphics.Blit(feet, target, Vector2.zero, new Vector2((x + 0.5f) / 4096f, (y + 0.5f) / 4096f));
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0f, 0f, 1f, 1f), 0, 0);
                read.Apply(false, false);
                Match(read.GetPixel(0, 0), pixels[y * 4096 + x], "High feet texel " + x + "/" + y);
            }
        }

        private static Vector3 Ray(int face, int x, int y)
            => FerrofluidBakedRoomReflectionValidation.FaceDirection(face, (2f * (x + 0.5f) / 2048f) - 1f, (2f * (y + 0.5f) / 2048f) - 1f).normalized;

        private static Texture2D Source(string name, int size)
        {
            Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(ImportDirectory + name);
            Check(source != null && source.width == size && source.height == size, "High source imports must retain their intended dimensions: " + name);
            return source;
        }

        private static void Match(Color actual, Color expected, string description)
        {
            float tolerance = Mathf.Max(expected.r, Mathf.Max(expected.g, expected.b)) / 256f + 0.002f;
            Check(FerrofluidBakedRoomReflectionValidation.Close(actual, expected, tolerance), description + " actual=" + actual.ToString("F6") + " expected=" + expected.ToString("F6"));
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
