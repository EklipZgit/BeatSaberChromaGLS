using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // Anchors the baked room orientation to the actual scene: the native camera writes
    // raw ReadPixels rows bottom-up, so the +Z camera ray for a source texel is
    // (u, +v, 1), not the cubemap-upload (u, -v, 1) mapping the old tests assumed.
    internal static class FerrofluidNativeRoomOrientationValidation
    {
        private const string OnAsset = "assets/generated/room-reflections-on.cubemap";
        private const string OffAsset = "assets/generated/room-reflections-off.cubemap";
        private const string ManifestAsset = "assets/generated/room-reflections.json";

        [Serializable]
        private sealed class Manifest
        {
            public string logoCameraRelativePosition;
        }

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidNativeRoomOrientationValidation));

        private static void RunRuntime()
        {
            Assembly plugin = FerrofluidValidationRuntime.CreateConfig().GetType().Assembly;
            AssetBundle bundle = FerrofluidValidationRuntime.LoadReflectionPack()
                ?? throw new Exception("CHROMAGLS_REFLECTION_PACK must point at a loadable reflection pack.");
            List<Object> owned = new();
            bool roomKeyword = Shader.IsKeywordEnabled("FERROFLUID_ROOM_REFLECTIONS");
            Texture savedRoom = Shader.GetGlobalTexture("_FerrofluidRoomEnvironment");
            Texture savedFloor = Shader.GetGlobalTexture("_FerrofluidFloorReflection");
            Vector4 savedFloorBounds = Shader.GetGlobalVector("_FerrofluidFloorReflectionBounds");
            float savedFloorY = Shader.GetGlobalFloat("_FerrofluidFloorReflectionY");
            RenderTexture savedActive = RenderTexture.active;
            try
            {
                Cubemap on = bundle.LoadAsset<Cubemap>(OnAsset);
                Cubemap off = bundle.LoadAsset<Cubemap>(OffAsset);
                TextAsset manifestAsset = bundle.LoadAsset<TextAsset>(ManifestAsset);
                if (on == null || off == null || manifestAsset == null)
                    throw new Exception("Bundled room cubemaps or manifest missing for orientation validation.");
                Manifest manifest = JsonUtility.FromJson<Manifest>(manifestAsset.text);
                Vector3 logoRelative = ParseTuple(manifest.logoCameraRelativePosition).normalized;

                Texture2D onZ = LoadSource("on-4-positiveZ.exr");
                Texture2D offZ = LoadSource("off-4-positiveZ.exr");
                Color[] onPixels = onZ.GetPixels();
                Color[] offPixels = offZ.GetPixels();
                int logoIndex = 0;
                float delta = 0f;
                for (int index = 0; index < onPixels.Length; index++)
                {
                    float change = onPixels[index].r - offPixels[index].r;
                    if (change > delta)
                    {
                        delta = change;
                        logoIndex = index;
                    }
                }

                int texelX = logoIndex % 1024;
                int texelY = logoIndex / 1024;
                // +Z camera basis right=+X up=+Y forward=+Z: the world ray keeps +v.
                float u = (2f * (texelX + 0.5f) / 1024f) - 1f;
                float v = (2f * (texelY + 0.5f) / 1024f) - 1f;
                Vector3 worldRay = new Vector3(u, v, 1f).normalized;
                float manifestDot = Vector3.Dot(worldRay, logoRelative);
                Debug.Log($"[FerrofluidNativeRoomOrientationValidation] E texel=({texelX},{texelY}) delta={delta:F3} worldRay=({worldRay.x:F3},{worldRay.y:F3},{worldRay.z:F3}) manifestRay=({logoRelative.x:F3},{logoRelative.y:F3},{logoRelative.z:F3}) dot={manifestDot:F4}");
                if (delta <= 0.2f)
                    throw new Exception($"Native E delta too small to anchor orientation: {delta:F3}.");
                if (worldRay.y <= 0f)
                    throw new Exception($"Camera-ray oracle maps the E texel below the floor (worldRay.y={worldRay.y:F3}); the source rows must be inverted.");
                if (manifestDot <= 0.99f)
                    throw new Exception($"Camera-ray oracle disagrees with the captured logo direction (dot={manifestDot:F4}).");

                Shader probe = AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidBakedProbe.shader");
                Material probeMaterial = new(probe);
                RenderTexture rt = new(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                Texture2D read = new(1, 1, TextureFormat.RGBAFloat, false, true);
                rt.Create();
                owned.Add(probeMaterial);
                owned.Add(rt);
                owned.Add(read);

                Vector3 mirroredRay = new(worldRay.x, -worldRay.y, worldRay.z);
                Color expectedOn = onPixels[logoIndex];
                Color expectedOff = offPixels[logoIndex];
                Color onAbove = ReadCube(probeMaterial, rt, read, on, worldRay);
                Color offAbove = ReadCube(probeMaterial, rt, read, off, worldRay);
                Color onBelow = ReadCube(probeMaterial, rt, read, on, mirroredRay);
                Color offBelow = ReadCube(probeMaterial, rt, read, off, mirroredRay);
                float onTol = expectedOn.maxColorComponent / 256f + 0.002f;
                float offTol = expectedOff.maxColorComponent / 256f + 0.002f;
                Debug.Log($"[FerrofluidNativeRoomOrientationValidation] aboveFloor on={onAbove:F4} off={offAbove:F4} expectedOn={expectedOn:F4} expectedOff={expectedOff:F4} | belowFloor on={onBelow:F4} off={offBelow:F4}");

                if (!Close(onAbove, expectedOn, onTol))
                    throw new Exception($"On-map above-floor ray sampled {onAbove:F4}, expected source E color {expectedOn:F4}; the captured direction must preserve the source radiance.");
                if (!Close(offAbove, expectedOff, offTol))
                    throw new Exception($"Off-map above-floor ray sampled {offAbove:F4}, expected source color {expectedOff:F4}.");
                if (onAbove.r <= offAbove.r + 0.2f)
                    throw new Exception($"E on/off delta must exceed 0.2 above the floor, got {onAbove.r - offAbove.r:F3}.");
                if (Mathf.Abs(onBelow.r - offBelow.r) >= 0.05f)
                    throw new Exception($"Mirrored below-floor delta {onBelow.r - offBelow.r:F3} must stay under 0.05; the E lit the underside.");

                // The production surface seam must reach the same above-floor texel in
                // both states, and the mirrored ray must not return the E.
                Material surface = new(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader"));
                owned.Add(surface);
                surface.SetTexture("_EnvironmentTex", Texture2D.blackTexture);
                surface.SetTexture("_ThinFilmTex", Texture2D.blackTexture);
                surface.SetFloat("_FadeAlpha", 1f);
                surface.SetFloat("_Exposure", 1f);
                surface.SetFloat("_EmissionStrength", 0f);
                surface.SetFloat("_PrimitiveAmbientScale", 0f);
                surface.SetFloat("_MatteSheen", 0f);
                surface.SetFloat("_Reflectivity", 1f);
                surface.SetFloat("_ReflectionAngle", 90f);
                surface.SetVector("_ReflectionRotation", Vector3.zero);
                Shader.SetGlobalTexture("_FerrofluidFloorReflection", Texture2D.blackTexture);
                Shader.SetGlobalVector("_FerrofluidFloorReflectionBounds", Vector4.zero);
                Shader.SetGlobalFloat("_FerrofluidFloorReflectionY", 0f);
                Shader.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");

                Color shadedExpectedOn = RoomDisplayColor(expectedOn);
                Color shadedExpectedOff = RoomDisplayColor(expectedOff);
                float seamOnTol = shadedExpectedOn.maxColorComponent / 256f + 0.012f;
                float seamOffTol = shadedExpectedOff.maxColorComponent / 256f + 0.012f;
                foreach (int shape in new[] { 1, 2 })
                {
                    surface.SetFloat("_SurfaceShapeMode", shape);
                    Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", on);
                    Color shadedOn = FerrofluidEnvironmentReflectionValidation.Sample(surface, Vector3.forward, worldRay);
                    Color mirroredOn = FerrofluidEnvironmentReflectionValidation.Sample(surface, Vector3.forward, mirroredRay);
                    Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", off);
                    Color shadedOff = FerrofluidEnvironmentReflectionValidation.Sample(surface, Vector3.forward, worldRay);
                    Debug.Log($"[FerrofluidNativeRoomOrientationValidation] seam shape={shape} on={shadedOn:F4} expectedOn={shadedExpectedOn:F4} off={shadedOff:F4} expectedOff={shadedExpectedOff:F4} mirroredOn={mirroredOn:F4}");
                    if (!Close(shadedOn, shadedExpectedOn, seamOnTol))
                        throw new Exception($"Production seam shape={shape} on-state sampled {shadedOn:F4} at the above-floor E ray, expected {shadedExpectedOn:F4}.");
                    if (!Close(shadedOff, shadedExpectedOff, seamOffTol))
                        throw new Exception($"Production seam shape={shape} off-state sampled {shadedOff:F4} at the above-floor E ray, expected {shadedExpectedOff:F4}.");
                    if (shadedOn.r <= shadedOff.r + 0.1f)
                        throw new Exception($"Production seam shape={shape} on/off must differ above the floor ({shadedOn.r:F3} vs {shadedOff.r:F3}).");
                }

                ValidateVisibleSphere(on, off, worldRay, owned);
                Debug.Log("[FerrofluidNativeRoomOrientationValidation] PASS: native E above floor in baked maps, production seam, and rendered sphere.");
            }
            finally
            {
                if (roomKeyword)
                    Shader.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                else
                    Shader.DisableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", savedRoom);
                Shader.SetGlobalTexture("_FerrofluidFloorReflection", savedFloor);
                Shader.SetGlobalVector("_FerrofluidFloorReflectionBounds", savedFloorBounds);
                Shader.SetGlobalFloat("_FerrofluidFloorReflectionY", savedFloorY);
                RenderTexture.active = savedActive;
                foreach (Object item in owned)
                    Object.DestroyImmediate(item);
                bundle.Unload(true);
            }
        }

        // Physical proof: a live sphere fixture renders the baked room through the real
        // FERROFLUID_SURFACE pass. The above-floor E must light the upper hemisphere, so
        // the on/off red difference centroid lands above the image center — the inverted
        // bake would put it below.
        private static void ValidateVisibleSphere(Cubemap on, Cubemap off, Vector3 worldRay, List<Object> owned)
        {
            object config = FerrofluidValidationRuntime.CreateConfig();
            using FerrofluidValidationRuntime runtime = new("Sphere", config);
            MeshRenderer body = (MeshRenderer)runtime.Get("_renderer");
            Material material = (Material)runtime.Get("_surfaceMaterial");
            body.transform.position = new Vector3(0f, 1.7f, -1.5f);
            body.transform.rotation = Quaternion.identity;
            runtime.Set("_renderEnabled", true);
            material.SetFloat("_SpikesEnabled", 0f);
            material.SetFloat("_MagneticFieldStrength", 0f);
            material.SetTexture("_EnvironmentTex", Texture2D.blackTexture);
            material.SetTexture("_ThinFilmTex", Texture2D.blackTexture);
            material.SetFloat("_FadeAlpha", 1f);
            material.SetFloat("_Exposure", 1f);
            material.SetFloat("_EmissionStrength", 0f);
            material.SetFloat("_PrimitiveAmbientScale", 0f);
            material.SetFloat("_MatteSheen", 0f);
            material.SetFloat("_Reflectivity", 1f);
            material.SetFloat("_ReflectionAngle", 90f);
            material.SetVector("_ReflectionRotation", Vector3.zero);
            runtime.Invoke(
                "UpdatePrimitiveFrame",
                1f,
                Vector3.one * 100f,
                new Vector3(0f, 1.7f, -1.5f),
                0f,
                Vector3.zero,
                0f,
                Vector3.zero,
                Vector3.zero,
                1.6f);
            runtime.Invoke("RenderPrimitiveDeformation");

            GameObject cameraObject = new("Orientation Camera");
            owned.Add(cameraObject);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 1.7f, 0f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
            camera.fieldOfView = 60f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << 0;
            camera.enabled = false;
            const int size = 512;
            RenderTexture target = new(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create();
            owned.Add(target);
            camera.targetTexture = target;

            Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", on);
            Color[] onPixels = Render(camera, target, size);
            Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", off);
            Color[] offPixels = Render(camera, target, size);

            double deltaSum = 0;
            double weightedY = 0;
            Color[] diff = new Color[size * size];
            for (int index = 0; index < onPixels.Length; index++)
            {
                float redDelta = onPixels[index].r - offPixels[index].r;
                diff[index] = new Color(Mathf.Max(0f, redDelta), Mathf.Max(0f, redDelta), Mathf.Max(0f, redDelta), 1f);
                if (redDelta > 0.001f)
                {
                    deltaSum += redDelta;
                    weightedY += (index / size) * redDelta;
                }
            }

            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "Scratch", "ferro-room-orientation"));
            Directory.CreateDirectory(output);
            Save(onPixels, size, Path.Combine(output, "room-sphere-on.png"));
            Save(offPixels, size, Path.Combine(output, "room-sphere-off.png"));
            Save(diff, size, Path.Combine(output, "room-sphere-difference.png"));
            float centroidY = (float)(weightedY / deltaSum);
            Debug.Log($"[FerrofluidNativeRoomOrientationValidation] sphere redDelta={deltaSum:F1} centroidY={centroidY:F1} center={size / 2f:F1} (E ray y={worldRay.y:F2})");
            if (deltaSum <= 0.00001f)
                throw new Exception($"Rendered sphere shows no on/off E response: summed red delta {deltaSum:F6}.");
            if (centroidY <= size / 2f)
                throw new Exception($"Rendered E response centroid sits below image center ({centroidY:F1} <= {size / 2f:F1}); the bake is still inverted.");
        }

        private static Color[] Render(Camera camera, RenderTexture target, int size)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                camera.Render();
                Texture2D pixels = new(size, size, TextureFormat.RGBAFloat, false, true);
                try
                {
                    pixels.ReadPixels(new Rect(0f, 0f, size, size), 0, 0);
                    pixels.Apply();
                    return pixels.GetPixels();
                }
                finally
                {
                    Object.DestroyImmediate(pixels);
                }
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private static void Save(Color[] pixels, int size, string path)
        {
            Texture2D image = new(size, size, TextureFormat.RGBA32, false, false);
            try
            {
                image.SetPixels(pixels);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }

        private static Texture2D LoadSource(string name)
        {
            Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Generated/RoomReflections/{name}");
            if (source == null)
                throw new Exception($"Native source EXR missing at Assets/Generated/RoomReflections/{name}.");
            return source;
        }

        private static Vector3 ParseTuple(string text)
        {
            string[] parts = text.Trim('(', ')').Split(',');
            return new Vector3(
                float.Parse(parts[0], CultureInfo.InvariantCulture),
                float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[2], CultureInfo.InvariantCulture));
        }

        private static Color ReadCube(Material material, RenderTexture rt, Texture2D read, Cubemap cube, Vector3 direction)
        {
            material.SetTexture("_Cube", cube);
            material.SetVector("_Direction", direction);
            material.SetFloat("_FaceMip", 0f);
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

        private static Color RoomDisplayColor(Color linear)
        {
            if (QualitySettings.activeColorSpace == ColorSpace.Gamma)
                return linear;

            // UnityCG continues the sRGB curve above one; Color.gamma switches to a different HDR exponent.
            return new Color(Convert(linear.r), Convert(linear.g), Convert(linear.b), linear.a);

            static float Convert(float value)
                => Mathf.Max(0f, (1.055f * Mathf.Pow(Mathf.Max(0f, value), 1f / 2.4f)) - 0.055f);
        }

        private static bool Close(Color a, Color b, float tolerance)
            => Mathf.Abs(a.r - b.r) <= tolerance && Mathf.Abs(a.g - b.g) <= tolerance && Mathf.Abs(a.b - b.b) <= tolerance;
    }
}
