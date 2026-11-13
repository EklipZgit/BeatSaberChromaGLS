using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidFloorShadowValidation
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string CaptureDir = Path.Combine(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..")),
            "Scratch",
            "ferro-soft-shadows");

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidFloorShadowValidation));

        private static void RunRuntime()
        {
            List<Object> owned = new();
            List<object> helpers = new();
            bool previousKeyword = Shader.IsKeywordEnabled("FERROFLUID_ENVIRONMENT_CAPTURE");
            try
            {
                Directory.CreateDirectory(CaptureDir);
                GameObject cameraObject = Own(new GameObject("Shadow validation camera", typeof(Camera)));
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = 2f;
                camera.aspect = 1f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 20f;
                camera.cullingMask = (1 << 27) | 1;
                camera.transform.SetPositionAndRotation(new Vector3(0f, 5f, 0f), Quaternion.LookRotation(Vector3.down, Vector3.back));
                RenderTexture target = Own(new RenderTexture(256, 256, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
                target.Create();
                camera.targetTexture = target;

                Material white = Own(new Material(Shader.Find("Unlit/Color")) { color = Color.white });
                GameObject floor = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
                floor.layer = 27;
                floor.transform.localScale = new Vector3(30f, 0.01f, 30f);
                floor.transform.position = new Vector3(0f, -0.01f, 0f);
                floor.GetComponent<Renderer>().sharedMaterial = white;

                GameObject raisedFloor = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
                raisedFloor.layer = 27;
                raisedFloor.transform.localScale = new Vector3(3f, 0.01f, 3f);
                raisedFloor.transform.position = new Vector3(1f, 0.29f, -1.4f);
                raisedFloor.GetComponent<Renderer>().sharedMaterial = white;

                Assembly plugin = FerrofluidValidationRuntime.CreateConfig().GetType().Assembly;
                Type helperType = plugin.GetType("ChromaGLS.Settings.FerrofluidFloorShadow", throwOnError: false);
                Type shapeType = plugin.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", true);
                object sphereShape = Enum.Parse(shapeType, "Sphere");
                object cubeShape = Enum.Parse(shapeType, "RoundedCube");
                Shader shadowShader = ResolveShadowShader(plugin, helperType != null);
                Vector3 light = new Vector3(0f, 1f, 1f).normalized;

                GameObject sphere = Own(GameObject.CreatePrimitive(PrimitiveType.Sphere));
                sphere.layer = 27;
                sphere.transform.SetPositionAndRotation(new Vector3(-1f, 0.275f, 1f), Quaternion.identity);
                sphere.transform.localScale = Vector3.one * 0.55f;

                GameObject cube = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
                cube.layer = 27;
                cube.transform.SetPositionAndRotation(new Vector3(1f, 0.35f, 1f), Quaternion.identity);
                cube.transform.localScale = Vector3.one * 0.55f;

                GameObject elevated = Own(GameObject.CreatePrimitive(PrimitiveType.Sphere));
                elevated.layer = 27;
                elevated.transform.SetPositionAndRotation(new Vector3(1f, 0.55f, -1.4f), Quaternion.identity);
                elevated.transform.localScale = Vector3.one * 0.55f;

                int renders = 0;
                Camera.CameraCallback afterRender = _ => renders++;
                Camera.onPostRender += afterRender;
                try
                {
                    object helper = Track(CreateHelper(helperType, sphere.transform, sphereShape, 0f, light, shadowShader), helpers);
                    Update(helper);
                    Color[] pixels = Render(camera, target);
                    float shadowCenter = Sample(camera, pixels, -1f, 0.6f);
                    Check(shadowCenter < 0.85f, $"Grounded sphere must darken the floor behind it, got {shadowCenter:F3} at z=0.6.");
                    Check(shadowCenter > 0.05f, $"Shadow must not be a hard black blob, got {shadowCenter:F3}.");
                    Check(Sample(camera, pixels, -1f, 1.6f) > 0.9f, "Floor in front of the sphere must stay lit.");
                    float inner = Sample(camera, pixels, -1f, 0.55f);
                    float penumbra = Sample(camera, pixels, -1f, 0.33f);
                    float outer = Sample(camera, pixels, -1f, 0.15f);
                    Check(inner < penumbra && penumbra < outer, $"Penumbra must interpolate, inner={inner:F3} mid={penumbra:F3} outer={outer:F3}.");
                    Check(outer > 0.9f, "Background outside the patch must stay untouched.");
                    SavePng("shadow-topdown-grounded.png", target);

                    int before = renders;
                    Update(helper);
                    Update(helper);
                    Check(renders == before, "Shadow Update must not render the scene.");

                    sphere.transform.position = new Vector3(-1f, 1.275f, 1f);
                    Update(helper);
                    Color[] raised = Render(camera, target);
                    float raisedCenter = Sample(camera, raised, -1f, -0.275f);
                    Check(raisedCenter < 0.9f && raisedCenter > shadowCenter, $"Raised sphere must move a lighter shadow toward -Z, got {raisedCenter:F3} vs grounded {shadowCenter:F3}.");
                    SavePng("shadow-topdown-raised.png", target);

                    sphere.transform.position = new Vector3(-1f, 0.275f, 1f);
                    Renderer sphereBody = sphere.GetComponent<Renderer>();
                    sphereBody.enabled = false;
                    try
                    {
                        sphere.transform.rotation = Quaternion.identity;
                        Update(helper);
                        Color[] shadowBaseline = Render(camera, target);
                        SavePng("shadow-sphere-shadow-only.png", target);
                        sphere.transform.rotation = Quaternion.Euler(33f, 47f, 61f);
                        Update(helper);
                        Color[] rotated = Render(camera, target);
                        SavePng("shadow-sphere-rotated-shadow-only.png", target);
                        float maxDiff = 0f;
                        int diffCount = 0;
                        int maxIndex = 0;
                        for (int index = 0; index < rotated.Length; index++)
                        {
                            float diff = Mathf.Abs(rotated[index].r - shadowBaseline[index].r);
                            if (diff > 0.002f)
                                diffCount++;
                            if (diff > maxDiff)
                            {
                                maxDiff = diff;
                                maxIndex = index;
                            }
                        }

                        Debug.Log($"[FerrofluidFloorShadowValidation] sphere rotation diff: max={maxDiff:F4} at {maxIndex % 256},{maxIndex / 256} count={diffCount}");
                        Check(diffCount == 0, $"Sphere rotation must not change the shadow, max={maxDiff:F4} count={diffCount}.");
                    }
                    finally
                    {
                        sphereBody.enabled = true;
                    }

                    Dispose(helper);
                    helpers.Remove(helper);
                    Color[] cleared = Render(camera, target);
                    Check(Sample(camera, cleared, -1f, 0.6f) > 0.9f, "Disposed shadow must leave the floor lit.");

                    object sphereShadow = Track(CreateHelper(helperType, sphere.transform, sphereShape, 0f, light, shadowShader), helpers);
                    SetVisible(sphereShadow, false);
                    Color[] hidden = Render(camera, target);
                    Check(Sample(camera, hidden, -1f, 0.6f) > 0.9f, "Hidden shadow must leave the floor lit.");
                    SetVisible(sphereShadow, true);

                    object cubeShadow = Track(CreateHelper(helperType, cube.transform, cubeShape, 0f, light, shadowShader), helpers);
                    Update(sphereShadow);
                    Update(cubeShadow);
                    Color[] pair = Render(camera, target);
                    Check(Sample(camera, pair, -1f, 0.6f) < 0.9f, "Sphere shadow must render beside the cube shadow.");
                    Check(Sample(camera, pair, 1f, 0.75f) < 0.95f, "Cube shadow must render behind the cube.");

                    Renderer cubeBody = cube.GetComponent<Renderer>();
                    cubeBody.enabled = false;
                    try
                    {
                        Update(cubeShadow);
                        Color[] cubeBaseline = Render(camera, target);
                        cube.transform.rotation = Quaternion.Euler(25f, 35f, 15f);
                        Update(cubeShadow);
                        Color[] cubeTurned = Render(camera, target);
                        bool differs = false;
                        for (int index = 0; index < cubeBaseline.Length; index++)
                            differs |= Mathf.Abs(cubeTurned[index].r - cubeBaseline[index].r) > 0.002f;
                        Check(differs, "Rotating the cube must change its projected silhouette.");
                    }
                    finally
                    {
                        cubeBody.enabled = true;
                    }

                    cube.transform.position = new Vector3(0.6f, 0.35f, 0.4f);
                    cube.transform.rotation = Quaternion.identity;
                    Update(cubeShadow);
                    Color[] moved = Render(camera, target);
                    Check(Sample(camera, moved, 0.6f, 0.15f) < 0.95f, "Translating the cube must move its shadow.");

                    object elevatedShadow = Track(CreateHelper(helperType, elevated.transform, sphereShape, 0.3f, light, shadowShader), helpers);
                    Update(elevatedShadow);
                    Color[] elevatedPixels = Render(camera, target);
                    Check(Sample(camera, elevatedPixels, 1f, -1.7f) < 0.95f, "A nonzero floorY must still land the shadow on its plane.");

                    bool previousCapture = Shader.IsKeywordEnabled("FERROFLUID_ENVIRONMENT_CAPTURE");
                    Shader.EnableKeyword("FERROFLUID_ENVIRONMENT_CAPTURE");
                    cubeBody.enabled = false;
                    Update(cubeShadow);
                    Color[] suppressed = Render(camera, target);
                    Check(Sample(camera, suppressed, 0.6f, 0.15f) > 0.9f, "The capture keyword must suppress floor shadows.");
                    cubeBody.enabled = true;
                    if (previousCapture)
                        Shader.EnableKeyword("FERROFLUID_ENVIRONMENT_CAPTURE");
                    else
                        Shader.DisableKeyword("FERROFLUID_ENVIRONMENT_CAPTURE");
                    Update(cubeShadow);

                    camera.transform.SetPositionAndRotation(new Vector3(0f, 3f, -3f), Quaternion.LookRotation(new Vector3(0f, -0.4f, 0.9f), Vector3.up));
                    camera.orthographic = false;
                    camera.fieldOfView = 60f;
                    Render(camera, target);
                    SavePng("shadow-oblique.png", target);
                }
                finally
                {
                    Camera.onPostRender -= afterRender;
                    for (int index = helpers.Count - 1; index >= 0; index--)
                        Dispose(helpers[index]);
                }

                Debug.Log("[FerrofluidFloorShadowValidation] PASS: grounded/raised/rotated/translated silhouettes, penumbra, nonzero floorY, two shadows, visibility, disposal, capture suppression, and zero scene renders.");
            }
            finally
            {
                if (previousKeyword)
                    Shader.EnableKeyword("FERROFLUID_ENVIRONMENT_CAPTURE");
                else
                    Shader.DisableKeyword("FERROFLUID_ENVIRONMENT_CAPTURE");
                for (int index = owned.Count - 1; index >= 0; index--)
                    Object.DestroyImmediate(owned[index]);
            }

            T Own<T>(T item) where T : Object
            {
                owned.Add(item);
                return item;
            }
        }

        private static Shader ResolveShadowShader(Assembly plugin, bool requireBundleShader)
        {
            if (!requireBundleShader)
                return null;

            Type resourcesType = plugin.GetType("ChromaGLS.Settings.FerrofluidGpuResources", true);
            object[] arguments = { null };
            object loaded = resourcesType.GetMethod("TryGet", Static).Invoke(null, arguments);
            if (!(bool)loaded || arguments[0] == null)
                throw new Exception("FerrofluidGpuResources.TryGet failed; the bundle could not supply the shadow shader.");

            Shader shader = arguments[0].GetType().GetProperty("FloorShadowShader", Instance).GetValue(arguments[0]) as Shader;
            if (shader == null || !shader.isSupported)
                throw new Exception("The built bundle must provide a supported FerrofluidFloorShadow shader; AssetDatabase fallback is not acceptable for a green run.");

            return shader;
        }

        private static object CreateHelper(Type helperType, Transform target, object shape, float floorY, Vector3 light, Shader shader)
        {
            if (helperType == null || shader == null)
                return null;

            return Activator.CreateInstance(
                helperType,
                Instance | BindingFlags.CreateInstance,
                null,
                new object[] { target, shape, floorY, light, shader },
                null);
        }

        private static T Track<T>(T helper, List<object> helpers)
            where T : class
        {
            if (helper != null)
                helpers.Add(helper);

            return helper;
        }

        private static void Update(object helper)
            => helper?.GetType().GetMethod("Update", Instance)?.Invoke(helper, null);

        private static void SetVisible(object helper, bool visible)
            => helper?.GetType().GetMethod("SetVisible", Instance)?.Invoke(helper, new object[] { visible });

        private static void Dispose(object helper)
            => (helper as IDisposable)?.Dispose();

        private static Color[] Render(Camera camera, RenderTexture target)
        {
            camera.Render();
            return FerrofluidValidationRuntime.Read(target);
        }

        private static float Sample(Camera camera, Color[] pixels, float worldX, float worldZ)
        {
            Vector3 viewport = camera.WorldToViewportPoint(new Vector3(worldX, 0f, worldZ));
            int u = Mathf.Clamp(Mathf.RoundToInt(viewport.x * 255f), 0, 255);
            int v = Mathf.Clamp(Mathf.RoundToInt(viewport.y * 255f), 0, 255);
            return pixels[(v * 256) + u].r;
        }

        private static void SavePng(string name, RenderTexture target)
        {
            Color[] pixels = FerrofluidValidationRuntime.Read(target);
            Texture2D image = new(256, 256, TextureFormat.RGBA32, false, true);
            try
            {
                image.SetPixels(pixels);
                image.Apply();
                File.WriteAllBytes(Path.Combine(CaptureDir, name), image.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }
    }
}
