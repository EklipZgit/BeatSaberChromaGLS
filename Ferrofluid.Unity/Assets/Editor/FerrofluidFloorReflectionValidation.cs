using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidFloorReflectionValidation
    {
        private const string FloorTexture = "_FerrofluidFloorReflection";
        private const string FloorBounds = "_FerrofluidFloorReflectionBounds";
        private const string FloorY = "_FerrofluidFloorReflectionY";
        private const string RoomTexture = "_FerrofluidRoomEnvironment";
        private const string RoomKeyword = "FERROFLUID_ROOM_REFLECTIONS";
        private const string WorldPosition = "_ValidationWorldPosition";
        private const string WorldOffset = "_ValidationWorldOffset";
        private const float PlaneY = 0.005f;
        private static readonly Vector4 Bounds = new(-2f, -2f, 0.25f, 0.25f);
        private static readonly string OutputDirectory = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            "..",
            "..",
            "Scratch",
            "ferro-planar-floor");

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidFloorReflectionValidation));

        private static void RunRuntime()
        {
            Directory.CreateDirectory(OutputDirectory);
            Texture previousRoom = Shader.GetGlobalTexture(RoomTexture);
            Texture previousFloor = Shader.GetGlobalTexture(FloorTexture);
            Vector4 previousBounds = Shader.GetGlobalVector(FloorBounds);
            float previousY = Shader.GetGlobalFloat(FloorY);
            Vector4 previousPosition = Shader.GetGlobalVector(WorldPosition);
            Vector4 previousOffset = Shader.GetGlobalVector(WorldOffset);
            bool previousKeyword = Shader.IsKeywordEnabled(RoomKeyword);
            Texture2D floor = null;
            Cubemap room = null;
            Material material = null;
            try
            {
                // Asymmetric 4x4-cell HDR patch: each cell a distinct pure hue at a distinct brightness.
                floor = new Texture2D(64, 64, TextureFormat.RGBAHalf, true, true) { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
                Color[] cellColors =
                {
                    new(2.5f, 0f, 0f), new(0f, 2.5f, 0f), new(0f, 0f, 2.5f), new(2.5f, 2.5f, 0f),
                    new(1.0f, 0f, 0f), new(0f, 1.0f, 0f), new(0f, 0f, 1.0f), new(0f, 1.0f, 1.0f),
                    new(0.5f, 0f, 0f), new(0f, 0.5f, 0f), new(0f, 0f, 0.5f), new(0.5f, 0f, 0.5f),
                    new(0.1f, 0f, 0f), new(0f, 0.1f, 0f), new(0f, 0f, 0.1f), new(0.1f, 0.1f, 0.1f),
                };
                for (int cell = 0; cell < 16; cell++)
                {
                    int cx = cell % 4;
                    int cy = cell / 4;
                    for (int v = cy * 16; v < (cy + 1) * 16; v++)
                        for (int u = cx * 16; u < (cx + 1) * 16; u++)
                            floor.SetPixel(u, v, cellColors[cell]);
                }

                floor.Apply();
                room = new Cubemap(64, TextureFormat.RGBAHalf, false);
                Color[] roomPixels = new Color[64 * 64];
                Array.Fill(roomPixels, new Color(0.3f, 0.3f, 0.3f, 1f));
                for (int face = 0; face < 6; face++)
                    room.SetPixels(roomPixels, (CubemapFace)face);
                room.Apply();

                material = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader"));
                material.SetFloat("_FadeAlpha", 1f);
                material.SetFloat("_Exposure", 1f);
                Shader.SetGlobalTexture(RoomTexture, room);
                Shader.SetGlobalTexture(FloorTexture, floor);
                Shader.SetGlobalVector(FloorBounds, Bounds);
                Shader.SetGlobalFloat(FloorY, PlaneY);
                Shader.EnableKeyword(RoomKeyword);

                Vector3 origin = new(0.2f, 1.5f, 0.1f);
                Color red = ReadAt(material, origin, RayTo(origin, new Vector3(-1.5f, PlaneY, -1.5f)));
                Check(red.r > 1.5f && red.g < 0.6f && red.b < 0.6f, $"Parallax ray must return the red cell (HDR above one), got {red:F4}.");

                // Four surface positions behind/front/left/right aimed at the same marker hit the same cell.
                // Cell (1,0) holds green 2.5; its footprint is x in [-1, 0], z in [-2, -1].
                Vector3 marker = new(-0.5f, PlaneY, -1.5f);
                foreach (Vector3 position in new[] { new Vector3(0.2f, 1.5f, -1.5f), new Vector3(0.2f, 1.5f, 2.5f), new Vector3(-1.5f, 1.5f, 0.1f), new Vector3(2.5f, 1.5f, 0.1f), new Vector3(0.2f, 3.5f, 0.1f) })
                {
                    Color cell = ReadAt(material, position, RayTo(position, marker));
                    Check(cell.g > 1.5f && cell.r < 0.6f && cell.b < 0.6f, $"Aim from {position} must reach the green marker cell, got {cell:F4}.");
                }

                // Same ray direction at translated origins hits different cells.
                Vector3 fixedRay = Vector3.down;
                Color cellA = ReadAt(material, new Vector3(-1.5f, 1.5f, -1.5f), fixedRay);
                Color cellB = ReadAt(material, new Vector3(-0.5f, 1.5f, -1.5f), fixedRay);
                Check(cellA.r > 1.5f && cellB.g > 1.5f && cellB.r < 0.6f, $"Translated origins must land on different cells, A={cellA:F4} B={cellB:F4}.");

                // Miss, upward, horizontal, and below-plane rays return room only and stay finite.
                foreach ((Vector3 position, Vector3 direction, string label) in new[]
                {
                    (new Vector3(9f, 1.5f, 9f), Vector3.down, "miss outside patch"),
                    (origin, Vector3.up, "upward ray"),
                    (origin, new Vector3(1f, 0.0f, 0f), "horizontal ray"),
                    (new Vector3(0f, -1f, 0f), Vector3.down, "below-plane origin"),
                })
                {
                    Color missed = ReadAt(material, position, direction);
                    Check(Finite(missed), $"{label} produced a non-finite sample: {missed}.");
                    Check(Mathf.Abs(missed.r - 0.3f) < 0.05f && Mathf.Abs(missed.g - 0.3f) < 0.05f && Mathf.Abs(missed.b - 0.3f) < 0.05f, $"{label} must return room only, got {missed:F4}.");
                }

                // Linear add: feet + room agree with a direct cubemap probe sum.
                Vector3 probeDir = Vector3.down;
                Color combined = ReadAt(material, new Vector3(-1.5f, 1.5f, -1.5f), probeDir);
                Check(Mathf.Abs(combined.r - 2.8f) < 0.15f, $"Feet must add in linear space before gamma, got {combined:F4} expected 0.3+2.5.");

                // Shape modes keep identical sampling (parallax is shape-independent).
                foreach (float shape in new[] { 0f, 1f, 2f })
                {
                    material.SetFloat("_SurfaceShapeMode", shape);
                    Color shaped = ReadAt(material, origin, RayTo(origin, marker));
                    Check(shaped.g > 1.5f, $"shape={shape} must hit the marker cell, got {shaped:F4}.");
                }

                material.SetFloat("_SurfaceShapeMode", 0f);

                // Boundary filtering: a ray on the red/green cell edge blends instead of
                // snapping to one texel (bilinear magnification, not minification).
                Vector3 edgeHit = new(-1f, PlaneY, -1.5f);
                Color blended = ReadAt(material, origin, RayTo(origin, edgeHit));
                Check(blended.r > 0.5f && blended.g > 0.5f, $"Cell-boundary ray must filter across texels, got {blended:F4}.");

                // Actual FERROFLUID_SURFACE pass: a translated runtime sphere reflects the
                // floor through the real vertex/mesh path with a high-frequency checker
                // patch, whose mip chain must yield the filtered mid brightness.
                object simConfig = FerrofluidValidationRuntime.CreateConfig();
                using FerrofluidValidationRuntime runtime = new("Sphere", simConfig);
                MeshRenderer primitive = (MeshRenderer)runtime.Get("_renderer");
                primitive.transform.position = new Vector3(-1.5f, 1.5f, -1.5f);
                Texture2D checker = new(64, 64, TextureFormat.RGBAHalf, true, true) { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
                for (int v = 0; v < 64; v++)
                    for (int u = 0; u < 64; u++)
                        checker.SetPixel(u, v, (u + v) % 2 == 0 ? Color.black : new Color(1f, 1f, 1f, 1f));
                checker.Apply();
                GameObject cameraObject = new("Floor parallax camera", typeof(Camera));
                Camera camera = cameraObject.GetComponent<Camera>();
                RenderTexture frame = new(256, 256, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                try
                {
                    camera.enabled = false;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = Color.black;
                    camera.fieldOfView = 40f;
                    camera.transform.SetPositionAndRotation(
                        new Vector3(-1.5f, 0.4f, -4.5f),
                        Quaternion.LookRotation(new Vector3(-1.5f, 1.5f, -1.5f) - new Vector3(-1.5f, 0.4f, -4.5f)));
                    camera.targetTexture = frame;
                    frame.Create();
                    Shader.SetGlobalTexture(FloorTexture, Texture2D.blackTexture);
                    Color[] baseline = RenderFrame(camera, frame);
                    Shader.SetGlobalTexture(FloorTexture, checker);
                    Color[] patched = RenderFrame(camera, frame);
                    int visible = 0;
                    for (int index = 0; index < baseline.Length; index++)
                    {
                        if (baseline[index].maxColorComponent > 0.02f)
                            visible++;
                    }

                    Debug.Log($"[FerrofluidFloorReflectionValidation] real-pass baseline visible pixels={visible}");
                    SavePng(baseline, 256, Path.Combine(OutputDirectory, "floor-real-baseline.png"));
                    float changedSum = 0f;
                    int changedCount = 0;
                    for (int index = 0; index < patched.Length; index++)
                    {
                        float diff = patched[index].maxColorComponent - baseline[index].maxColorComponent;
                        if (diff <= 0.05f)
                            continue;
                        changedSum += diff;
                        changedCount++;
                    }

                    Check(changedCount > 20, $"The translated primitive must reflect the floor patch, {changedCount} changed pixels.");
                    float mean = changedSum / changedCount;
                    Check(mean > 0.25f && mean < 0.85f, $"Minified checker reflection must average near mid brightness, mean diff={mean:F3} over {changedCount} pixels.");
                    SavePng(patched, 256, Path.Combine(OutputDirectory, "floor-real-render.png"));
                }
                finally
                {
                    Object.DestroyImmediate(checker);
                    Object.DestroyImmediate(frame);
                    Object.DestroyImmediate(cameraObject);
                }

                Debug.Log($"[FerrofluidFloorReflectionValidation] floor patch {Profiler.GetRuntimeMemorySizeLong(floor)} bytes; parallax, translation, miss/up/horizontal/below-plane, HDR, linear add, and shapes verified.");
                Debug.Log("[FerrofluidFloorReflectionValidation] PASS: planar floor parallax verified.");
            }
            finally
            {
                Shader.SetGlobalTexture(RoomTexture, previousRoom);
                Shader.SetGlobalTexture(FloorTexture, previousFloor);
                Shader.SetGlobalVector(FloorBounds, previousBounds);
                Shader.SetGlobalFloat(FloorY, previousY);
                Shader.SetGlobalVector(WorldPosition, previousPosition);
                Shader.SetGlobalVector(WorldOffset, previousOffset);
                if (previousKeyword)
                    Shader.EnableKeyword(RoomKeyword);
                else
                    Shader.DisableKeyword(RoomKeyword);
                if (material != null)
                    Object.DestroyImmediate(material);
                if (floor != null)
                    Object.DestroyImmediate(floor);
                if (room != null)
                    Object.DestroyImmediate(room);
            }
        }

        private static Vector3 RayTo(Vector3 from, Vector3 target) => (target - from).normalized;

        private static Color[] RenderFrame(Camera camera, RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.Render();
                RenderTexture.active = target;
                Texture2D pixels = new(target.width, target.height, TextureFormat.RGBAFloat, false, true);
                try
                {
                    pixels.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
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

        private static void SavePng(Color[] pixels, int size, string path)
        {
            Texture2D image = new(size, size, TextureFormat.RGBAFloat, false, true);
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

        private static Color ReadAt(Material material, Vector3 worldPosition, Vector3 direction)
        {
            Shader.SetGlobalVector(WorldPosition, worldPosition);
            return FerrofluidEnvironmentReflectionValidation.Sample(material, Vector3.forward, direction);
        }

        private static bool Finite(Color color)
            => !float.IsNaN(color.r) && !float.IsInfinity(color.r)
                && !float.IsNaN(color.g) && !float.IsInfinity(color.g)
                && !float.IsNaN(color.b) && !float.IsInfinity(color.b);

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }
    }
}
