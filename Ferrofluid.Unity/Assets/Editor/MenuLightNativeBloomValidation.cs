using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // The reference pass checks native bloom inputs, not Beat Saber's final post-process image.
    internal static class MenuLightNativeBloomValidation
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static Assembly _plugin;

        public static void Run() => FerrofluidValidationRunner.Run(typeof(MenuLightNativeBloomValidation));

        private static void RunRuntime()
        {
            string managed = Environment.GetEnvironmentVariable("CHROMAGLS_TEST_MANAGED");
            if (string.IsNullOrEmpty(managed))
                throw new InvalidOperationException("Set CHROMAGLS_TEST_MANAGED to the built test-host directory.");
            AppDomain.CurrentDomain.AssemblyResolve += (_, arguments) =>
            {
                string path = Path.Combine(managed, new AssemblyName(arguments.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            _plugin = Assembly.LoadFrom(Path.Combine(managed, "ChromaGLS.dll"));
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/MenuLightCompositionReference.shader");
            Check(shader != null && shader.isSupported, "The native-particle reference pass must be available.");
            Type resources = TypeOf("FerrofluidGpuResources");
            FieldInfo attempted = resources.GetField("_attemptedLoad", Members);
            object previous = attempted.GetValue(null);
            attempted.SetValue(null, true);
            RenderTexture target = new(160, 160, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Check(target.Create(), "The float render target must be available.");
            GameObject cameraRoot = new("Native bloom validation camera");
            Camera camera = cameraRoot.AddComponent<Camera>();
            camera.enabled = false;
            camera.targetTexture = target;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << 30;
            camera.allowHDR = true;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 100f;
            Texture2D readback = new(160, 160, TextureFormat.RGBAFloat, false, true);
            RenderTexture previousTarget = RenderTexture.active;
            try
            {
                foreach (string shape in new[] { "Cube", "Sphere", "Square", "Circle", "Parabola" })
                    ValidateMovable(shape, shader, camera, target, readback);
                ValidateFloorFaceRadiance(shader, camera, target, readback);
                ValidateFloorSwitchAndGameplay(shader);
                ValidateEditorGroups();
                Debug.Log("[MenuLightNativeBloomValidation] PASS: five movable shapes, camera/light rotations, RGB/bloom payload, Floor face radiance and 20mm contact, Floor switching, renderer gameplay gate, editor groups.");
            }
            finally
            {
                RenderTexture.active = previousTarget;
                attempted.SetValue(null, previous);
                Object.DestroyImmediate(cameraRoot);
                Object.DestroyImmediate(readback);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private static void ValidateMovable(string shape, Shader shader, Camera camera, RenderTexture target, Texture2D readback)
        {
            object definition = Definition(shape);
            Set(definition, "Height", 1.2f);
            Call(definition, "RotateAxis", 0, 37f);
            Call(definition, "RotateAxis", 2, -22f);
            object meshes = Geometry(definition);
            object renderer = New("MenuLightRenderer", shader, SceneManager.GetActiveScene(), definition, meshes, true);
            GameObject root = Field<GameObject>(renderer, "_root");
            GameObject halo = Field<GameObject>(renderer, "_haloRoot");
            root.layer = halo.layer = 30;
            try
            {
                Check(!halo.activeSelf, shape + ": a movable light must not draw a planar halo.");
                Check(halo.GetComponent<MeshFilter>().sharedMesh.vertexCount == 0, shape + ": no hidden halo geometry may enter reflections or spill.");
                Material core = Field<Material>(renderer, "_material");
                Check(Mathf.Abs(core.GetFloat("_BloomMultiplier") - .6f) < .0001f, shape + ": native bloom strength must remain intact.");
                Check(Mathf.Abs(core.color.a * core.GetFloat("_BloomMultiplier") - .36f) < .0001f, shape + ": each fully covered fragment must feed .36 bloom alpha.");
                foreach (Vector3 cameraOffset in new[] { new Vector3(0f, 3f, -4f), new Vector3(3f, -1f, -4f) })
                {
                    Vector3 center = root.transform.position;
                    camera.transform.SetPositionAndRotation(center + cameraOffset, Quaternion.LookRotation(-cameraOffset, Vector3.up));
                    camera.Render();
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, 160, 160), 0, 0);
                    readback.Apply();
                    float alpha = 0f;
                    float rgb = 0f;
                    int peakPixel = 0;
                    Color[] pixels = readback.GetPixels();
                    for (int index = 0; index < pixels.Length; index++)
                    {
                        Color pixel = pixels[index];
                        if (pixel.a > alpha)
                        {
                            alpha = pixel.a;
                            peakPixel = index;
                        }
                        rgb = Mathf.Max(rgb, Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)));
                    }
                    float expected = ExpectedAlpha(camera, root, core, peakPixel, 160);
                    Debug.Log($"[MenuLightNativeBloomValidation] {shape} peakPixel={peakPixel} rayExpectedAlpha={expected:F6} actualAlpha={alpha:F6}");
                    Check(expected > 0f && Mathf.Abs(alpha - expected) < .003f, shape + ": rendered bloom must match native additive cull/depth behavior.");
                    Check(rgb > 1f, shape + ": HDR core radiance must survive camera and light rotation.");
                    Debug.Log($"[MenuLightNativeBloomValidation] {shape} camera={cameraOffset} peakRGB={rgb:F4} peakAlpha={alpha:F4} haloActive={halo.activeSelf}");
                }
            }
            finally
            {
                root.SetActive(false);
                ((IDisposable)renderer).Dispose();
            }
        }

        private static void ValidateFloorFaceRadiance(Shader shader, Camera camera, RenderTexture target, Texture2D readback)
        {
            object definition = Definition("Floor");
            object renderer = New("MenuLightRenderer", shader, SceneManager.GetActiveScene(), definition, Geometry(definition), true);
            GameObject root = Field<GameObject>(renderer, "_root");
            GameObject halo = Field<GameObject>(renderer, "_haloRoot");
            root.layer = 30;
            halo.SetActive(false);
            Mesh mesh = root.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.triangles;
            Color[] shaded = mesh.colors;
            Color[] baseline = new Color[shaded.Length];
            for (int index = 0; index < baseline.Length; index++)
                baseline[index] = new Color(1f, 1f, 1f, shaded[index].a);

            try
            {
                float bottom = float.PositiveInfinity;
                float top = float.NegativeInfinity;
                foreach (Vector3 vertex in vertices)
                {
                    float height = root.transform.TransformPoint(vertex).y;
                    bottom = Mathf.Min(bottom, height);
                    top = Mathf.Max(top, height);
                }

                Check(Mathf.Abs(top - .02f) < 1e-5f && Mathf.Abs(bottom) < 1e-5f, "Floor must have a 20mm top and its default underside flush with the floor.");
                camera.orthographic = true;
                camera.orthographicSize = .002f;
                foreach (bool topFace in new[] { true, false })
                {
                    bool sampled = false;
                    for (int index = 0; index < indices.Length; index += 3)
                    {
                        int a = indices[index];
                        int b = indices[index + 1];
                        int c = indices[index + 2];
                        Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                        bool matchingFace = topFace ? normal.y > 0f : normal.y == 0f;
                        if (!matchingFace || normal.sqrMagnitude == 0f || shaded[a].a < .999f || shaded[b].a < .999f || shaded[c].a < .999f)
                            continue;

                        Vector3 center = root.transform.TransformPoint((vertices[a] + vertices[b] + vertices[c]) / 3f);
                        Vector3 direction = root.transform.TransformDirection(normal.normalized);
                        camera.transform.SetPositionAndRotation(
                            center + (direction * .05f),
                            Quaternion.LookRotation(-direction, topFace ? Vector3.forward : Vector3.up));
                        mesh.colors = baseline;
                        Color before = ReadCenter(camera, target, readback);
                        mesh.colors = shaded;
                        Color after = ReadCenter(camera, target, readback);
                        float factor = topFace ? .64f : 1.44f;
                        Check(before.b > .01f && before.a > .01f, "The floor face sample must actually render.");
                        Check(Mathf.Abs((after.b / before.b) - factor) < .005f, "Floor face RGB must apply both radiance factors.");
                        Check(Mathf.Abs(after.a - before.a) < .001f, "Face radiance must preserve native bloom alpha and end coverage.");
                        Debug.Log($"[MenuLightNativeBloomValidation] Floor {(topFace ? "top" : "side")} RGB before={before.b:F6} after={after.b:F6} ratio={after.b / before.b:F6} alpha before={before.a:F6} after={after.a:F6} top={top:F6} bottom={bottom:F6}");
                        sampled = true;
                        break;
                    }

                    Check(sampled, "The floor mesh must expose a fully covered top and side sample.");
                }
            }
            finally
            {
                camera.orthographic = false;
                mesh.colors = shaded;
                root.SetActive(false);
                ((IDisposable)renderer).Dispose();
            }
        }

        private static Color ReadCenter(Camera camera, RenderTexture target, Texture2D readback)
        {
            camera.Render();
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            readback.Apply();
            return readback.GetPixel(target.width / 2, target.height / 2);
        }

        private static float ExpectedAlpha(Camera camera, GameObject root, Material material, int pixel, int size)
        {
            // A concave ribbon can draw several front-facing surfaces before the nearest depth
            // wins. Preserve the native additive payload rather than assuming one fragment.
            Ray ray = camera.ViewportPointToRay(new Vector3(((pixel % size) + .5f) / size, ((pixel / size) + .5f) / size, 0f));
            Mesh mesh = root.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Color[] colors = mesh.colors;
            int[] indices = mesh.triangles;
            float depth = float.PositiveInfinity;
            float alpha = 0f;
            for (int index = 0; index < indices.Length; index += 3)
            {
                int a = indices[index];
                int b = indices[index + 1];
                int c = indices[index + 2];
                Vector3 v0 = root.transform.TransformPoint(vertices[a]);
                Vector3 ab = root.transform.TransformPoint(vertices[b]) - v0;
                Vector3 ac = root.transform.TransformPoint(vertices[c]) - v0;
                if (Vector3.Dot(Vector3.Cross(ab, ac), ray.direction) >= 0f)
                    continue;
                Vector3 p = Vector3.Cross(ray.direction, ac);
                float determinant = Vector3.Dot(ab, p);
                if (Mathf.Abs(determinant) < 1e-9f)
                    continue;
                Vector3 from = ray.origin - v0;
                float u = Vector3.Dot(from, p) / determinant;
                Vector3 q = Vector3.Cross(from, ab);
                float v = Vector3.Dot(ray.direction, q) / determinant;
                float distance = Vector3.Dot(ac, q) / determinant;
                if (u < 0f || v < 0f || u + v > 1f || distance < 0f || distance > depth + 1e-6f)
                    continue;
                depth = distance;
                float fade = ((1f - u - v) * colors[a].a) + (u * colors[b].a) + (v * colors[c].a);
                alpha += fade * material.color.a * material.GetFloat("_BloomMultiplier");
            }
            return alpha;
        }

        private static void ValidateFloorSwitchAndGameplay(Shader shader)
        {
            object floor = Definition("Floor");
            object meshes = Geometry(floor);
            object renderer = New("MenuLightRenderer", shader, SceneManager.GetActiveScene(), floor, meshes, true);
            object config = New("Config");
            Set(config, "ParabolicFloorLightEnabled", true);
            object service = New("ParabolicFloorLight", config);
            try
            {
                GameObject root = Field<GameObject>(renderer, "_root");
                GameObject halo = Field<GameObject>(renderer, "_haloRoot");
                Check(halo.activeInHierarchy, "Floor must retain its projected glow.");
                Check(Mathf.Abs(root.transform.position.y - .02f) < 1e-5f, "Floor strip must stay at20mm.");
                foreach (Vector3 vertex in halo.GetComponent<MeshFilter>().sharedMesh.vertices)
                    Check(Mathf.Abs(halo.transform.TransformPoint(vertex).y - .001f) < 1e-5f, "Floor glow must stay at1mm.");
                object cube = Definition("Cube");
                Call(renderer, "Apply", cube, Geometry(cube), true);
                Check(!halo.activeSelf, "Changing Floor to Cube must remove the planar glow.");
                Call(renderer, "Apply", floor, meshes, true);
                Check(halo.activeSelf, "Changing back to Floor must restore the projected glow.");
                string id = (string)Get(floor, "Id");
                ((IDictionary)Field<object>(service, "_renderers")).Add(id, renderer);
                ((IDictionary)Field<object>(service, "_savedDefinitions")).Add(id, floor);
                Call(service, "SetEnabled", true);
                Call(service, "SetMenuActive", false);
                Call(service, "SetEnabled", true);
                Check(!root.activeSelf && !halo.activeSelf, "Gameplay must hide both roots and reject late enable.");
                Call(service, "SetMenuActive", true);
                Check(root.activeSelf && halo.activeSelf, "Returning to menu must restore saved Floor visibility.");
            }
            finally
            {
                ((IDisposable)service).Dispose();
            }
        }

        private static void ValidateEditorGroups()
        {
            object config = New("Config");
            Set(config, "MenuLightsJson", "[]");
            object service = New("ParabolicFloorLight", config);
            object ui = New("ParabolicFloorLightSettingsUI", config, service);
            string[] fields = { "_parabolaControls", "_sizeControls", "_materialWidthControls", "_floorGlowControls", "_freePoseControls" };
            GameObject[] groups = new GameObject[fields.Length];
            try
            {
                for (int index = 0; index < fields.Length; index++)
                {
                    groups[index] = new GameObject(fields[index]);
                    ui.GetType().GetField(fields[index], Members).SetValue(ui, groups[index]);
                }
                Call(ui, "BeginEditor", null, (Action)(() => { }));
                foreach (string shape in new[] { "Floor", "Cube", "Parabola" })
                {
                    Set(ui, "TypeValue", Enum.Parse(TypeOf("MenuLightType"), shape));
                    bool floor = shape == "Floor";
                    Check(groups[3].activeSelf == floor, "Only Floor may expose floor-glow controls.");
                    Check(groups[4].activeSelf != floor, "Floor may not expose Height/Pitch/Roll.");
                    Check(groups[0].activeSelf == (shape != "Cube"), "Floor and Parabola must expose curve controls.");
                    Check(groups[1].activeSelf == (shape == "Cube"), "Floor must not expose closed-shape Size.");
                }
                Call(ui, "CloseForGameplay");
                Check(!(bool)Get(Field<object>(ui, "_session"), "IsEditing"), "Gameplay must cancel the editor session.");
            }
            finally
            {
                ((IDisposable)ui).Dispose();
                ((IDisposable)service).Dispose();
                foreach (GameObject group in groups)
                    if (group != null)
                        Object.DestroyImmediate(group);
            }
        }

        private static object Definition(string shape)
        {
            object config = New("Config");
            Set(config, "MenuLightsJson", "[]");
            object definition = Call(New("MenuLightStore", config), "CreateDefault", Enum.Parse(TypeOf("MenuLightType"), shape));
            object appearance = Get(definition, "Appearance");
            appearance = Call(appearance, "WithBrightness", .6f);
            appearance = Call(appearance, "WithRed", 2f);
            Set(definition, "Appearance", appearance);
            return definition;
        }

        private static object Geometry(object definition)
        {
            object enclosure = New("FerrofluidGrabEnclosure", Vector3.zero, 5f, new Vector3(0f, 0f, -5f), Vector3.forward, 0f);
            object[] args = { enclosure, Vector3.zero, Vector3.forward, definition, null };
            Check((bool)TypeOf("MenuLightGeometry").GetMethod("TryCreate", Members).Invoke(null, args), "Geometry must be valid.");
            return args[4];
        }

        private static Type TypeOf(string name) => _plugin.GetType("ChromaGLS.Settings." + name, true);
        private static object New(string name, params object[] args) => Activator.CreateInstance(TypeOf(name), Members, null, args, null);
        private static object Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, Members).Invoke(instance, args);
        private static object Get(object instance, string name) => instance.GetType().GetProperty(name, Members).GetValue(instance);
        private static void Set(object instance, string name, object value) => instance.GetType().GetProperty(name, Members).SetValue(instance, value);
        private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Members).GetValue(instance);
        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
