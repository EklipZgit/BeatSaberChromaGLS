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
                ValidateSolidCoreFade(shader, camera, target, readback);
                ValidateFloorSwitchAndGameplay(shader);
                ValidateEditorGroups();
                Debug.Log("[MenuLightNativeBloomValidation] PASS: five movable shapes, camera/light rotations, RGB/bloom payload, Floor face radiance and 20mm contact, solid Floor/Parabola end fades, Floor switching, renderer gameplay gate, editor groups.");
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
            object appearance = Definition("Floor");
            appearance = Call(Get(appearance, "Appearance"), "WithBrightness", .6f);
            appearance = Call(appearance, "WithRed", 2f);
            appearance = Call(appearance, "WithGreen", .5f);
            appearance = Call(appearance, "WithBlue", 1f);
            object definition = Definition("Floor");
            Set(definition, "Height", .02f);
            Set(definition, "Appearance", appearance);

            // A controlled non-zero color keeps every channel measurable through the native
            // bloom input; the reference shader reports pre-white-boost PP input only.
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
            for (int index = 0; index < indices.Length; index += 3)
            {
                int a = indices[index];
                int b = indices[index + 1];
                int c = indices[index + 2];
                Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                float alphaFactor = normal.y == 0f ? 1.3f : 1f;
                baseline[a] = new Color(1f, 1f, 1f, shaded[a].a / alphaFactor);
                baseline[b] = new Color(1f, 1f, 1f, shaded[b].a / alphaFactor);
                baseline[c] = new Color(1f, 1f, 1f, shaded[c].a / alphaFactor);
            }

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
                        float rawAlpha = shaded[a].a / (topFace ? 1f : 1.3f);
                        if (!matchingFace || normal.sqrMagnitude == 0f || rawAlpha < .999f)
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
                        float rgbFactor = topFace ? .64f : 1.872f;
                        float alphaFactor = topFace ? 1f : 1.3f;
                        Check(before.b > .01f && before.a > .01f, "The floor face sample must actually render.");
                        Check(Mathf.Abs((after.b / before.b) - rgbFactor) < .005f, "Floor face RGB must apply both radiance factors.");
                        Check(Mathf.Abs((after.a / before.a) - alphaFactor) < .005f, "Floor face alpha must carry the side bloom boost only.");
                        Debug.Log($"[MenuLightNativeBloomValidation] Floor {(topFace ? "top" : "side")} RGB before={before.b:F6} after={after.b:F6} rgbRatio={after.b / before.b:F6} alphaRatio={after.a / before.a:F6} top={top:F6} bottom={bottom:F6}");
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

            ValidateFloorCustomHeight(shader);
        }

        private static void ValidateFloorCustomHeight(Shader shader)
        {
            object definition = Definition("Floor");
            Set(definition, "Height", .125f);
            Set(definition, "Depth", .02f);
            object renderer = New("MenuLightRenderer", shader, SceneManager.GetActiveScene(), definition, Geometry(definition), true);
            GameObject root = Field<GameObject>(renderer, "_root");
            Mesh mesh = root.GetComponent<MeshFilter>().sharedMesh;
            try
            {
                float bottom = float.PositiveInfinity;
                float top = float.NegativeInfinity;
                foreach (Vector3 vertex in mesh.vertices)
                {
                    float height = root.transform.TransformPoint(vertex).y;
                    bottom = Mathf.Min(bottom, height);
                    top = Mathf.Max(top, height);
                }

                Check(Mathf.Abs(top - .125f) < 1e-5f, "A raised Floor must put its top at the saved height.");
                Check(Mathf.Abs(bottom) < 1e-5f, "A raised Floor must keep its underside flush with the floor, ignoring stale Depth.");
                GameObject halo = Field<GameObject>(renderer, "_haloRoot");
                float glow = float.PositiveInfinity;
                foreach (Vector3 vertex in halo.GetComponent<MeshFilter>().sharedMesh.vertices)
                    glow = Mathf.Min(glow, halo.transform.TransformPoint(vertex).y);
                Check(Mathf.Abs(glow - .001f) < 1e-5f, "The floor glow must hover one millimetre above the floor at any saved height.");
            }
            finally
            {
                root.SetActive(false);
                ((IDisposable)renderer).Dispose();
            }
        }

        private static void ValidateSolidCoreFade(Shader shader, Camera camera, RenderTexture target, Texture2D readback)
        {
            foreach (string shape in new[] { "Floor", "Parabola" })
                ValidateOneSolidCore(shape, shader, camera, target, readback);
        }

        // A faded solid face must overwrite the background, not add to it: vertex alpha is
        // emission/bloom payload, not opacity, so the core emits a self-contained value.
        private static void ValidateOneSolidCore(string shape, Shader shader, Camera camera, RenderTexture target, Texture2D readback)
        {
            object definition = Definition(shape);
            object appearance = Get(definition, "Appearance");
            appearance = Call(appearance, "WithBrightness", .6f);
            appearance = Call(appearance, "WithRed", 2f);
            appearance = Call(appearance, "WithGreen", .5f);
            appearance = Call(appearance, "WithBlue", 1f);
            Set(definition, "Appearance", appearance);
            object renderer = New("MenuLightRenderer", shader, SceneManager.GetActiveScene(), definition, Geometry(definition), true);
            GameObject root = Field<GameObject>(renderer, "_root");
            GameObject halo = Field<GameObject>(renderer, "_haloRoot");
            root.layer = 30;
            halo.SetActive(false);
            bool oldOrtho = camera.orthographic;
            float oldSize = camera.orthographicSize;
            Vector3 oldPos = camera.transform.position;
            Quaternion oldRot = camera.transform.rotation;
            Color oldClear = camera.backgroundColor;
            Matrix4x4 oldProjection = camera.projectionMatrix;
            Mesh mesh = root.GetComponent<MeshFilter>().sharedMesh;
            Color[] origColors = mesh.colors;
            try
            {
                // Pick a genuine end-fade top triangle: all three vertex alphas in .05..0.95.
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                int found = -1;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    float minAlpha = Mathf.Min(origColors[triangles[i]].a, Mathf.Min(origColors[triangles[i + 1]].a, origColors[triangles[i + 2]].a));
                    float maxAlpha = Mathf.Max(origColors[triangles[i]].a, Mathf.Max(origColors[triangles[i + 1]].a, origColors[triangles[i + 2]].a));
                    Vector3 normal = Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]], vertices[triangles[i + 2]] - vertices[triangles[i]]);
                    if (normal.sqrMagnitude < 1e-10f)
                        continue;
                    if (minAlpha > .05f && maxAlpha < .95f && normal.normalized.y > .4f)
                    {
                        found = i;
                        break;
                    }
                }
                Check(found >= 0, shape + ": a faded top triangle must exist for the fade test.");
                Vector3 center = (vertices[triangles[found]] + vertices[triangles[found + 1]] + vertices[triangles[found + 2]]) / 3f;
                Vector3 localNormal = Vector3.Cross(vertices[triangles[found + 1]] - vertices[triangles[found]], vertices[triangles[found + 2]] - vertices[triangles[found]]).normalized;
                Vector3 worldCenter = root.transform.TransformPoint(center);
                Vector3 worldNormal = root.transform.TransformDirection(localNormal).normalized;
                Vector3 worldForward = root.transform.TransformDirection(Vector3.forward);
                camera.orthographic = true;
                camera.orthographicSize = .05f;
                camera.transform.SetPositionAndRotation(worldCenter + worldNormal * .4f, Quaternion.LookRotation(-worldNormal, worldForward));

                // Center must be nonblank and identical over clear vs colored backgrounds.
                camera.backgroundColor = Color.clear;
                Color overClear = ReadCenter(camera, target, readback);
                camera.backgroundColor = new Color(.25f, .07f, .4f, .5f);
                Color overColored = ReadCenter(camera, target, readback);
                Check(overClear.r > .001f || overClear.g > .001f || overClear.b > .001f, shape + ": faded core must emit a nonblank center.");
                float err = Mathf.Max(Mathf.Abs(overClear.r - overColored.r), Mathf.Max(Mathf.Abs(overClear.g - overColored.g), Mathf.Max(Mathf.Abs(overClear.b - overColored.b), Mathf.Abs(overClear.a - overColored.a))));
                Check(err < .005f, $"{shape}: solid core output must not depend on clear color (err {err:F5}); additive destination leaks background through the fade.");

                // Fully-lit baseline (alpha reset, floor sides keep 1.3) exceeds the faded value.
                Color[] lit = (Color[])origColors.Clone();
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 normal = Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]], vertices[triangles[i + 2]] - vertices[triangles[i]]);
                    float side = shape == "Floor" && normal.y == 0f ? 1.3f : 1f;
                    foreach (int index in new[] { triangles[i], triangles[i + 1], triangles[i + 2] })
                        lit[index] = new Color(lit[index].r, lit[index].g, lit[index].b, side);
                }
                mesh.colors = lit;
                camera.backgroundColor = Color.black;
                Color fullyLit = ReadCenter(camera, target, readback);
                mesh.colors = origColors;
                Check(overClear.a < fullyLit.a && overClear.a > 0f, shape + ": faded alpha must be positive but below the fully-lit payload.");
                Check(Mathf.Abs(fullyLit.a - .36f) < .005f, $"{shape}: fully-lit top bloom alpha should stay .36, got {fullyLit.a:F4}.");
                float rgbRatio = fullyLit.r > 0f ? overClear.r / fullyLit.r : 0f;
                float alphaRatio = fullyLit.a > 0f ? overClear.a / fullyLit.a : 0f;
                Check(Mathf.Abs(rgbRatio - alphaRatio) < .005f, $"{shape}: fade should dim RGB and bloom alpha together (rgb {rgbRatio:F3} vs alpha {alphaRatio:F3}).");

                // An artificially zeroed material must still be dim everywhere: it is an
                // opaque-material contract, not the generated end, which keeps residual emission.
                Color[] zeroed = (Color[])origColors.Clone();
                for (int i = 0; i < zeroed.Length; i++)
                    zeroed[i] = new Color(zeroed[i].r, zeroed[i].g, zeroed[i].b, 0f);
                mesh.colors = zeroed;
                camera.backgroundColor = new Color(.25f, .07f, .4f, .5f);
                Color dark = ReadCenter(camera, target, readback);
                float darkMax = Mathf.Max(dark.r, Mathf.Max(dark.g, Mathf.Max(dark.b, dark.a)));
                Check(darkMax < .005f, $"{shape}: a fully-faded face must go dark in every channel (got {darkMax:F4}), not reveal the clear color.");

                // The generated end bottoms out at 20% emission, not black: the most-faded
                // top triangle must emit about a fifth of the same face fully lit.
                int endTri = -1;
                float endAvg = 1f;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 normal = Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]], vertices[triangles[i + 2]] - vertices[triangles[i]]);
                    if (normal.sqrMagnitude < 1e-10f)
                        continue;
                    if (normal.normalized.y <= .4f)
                        continue;
                    float avg = (origColors[triangles[i]].a + origColors[triangles[i + 1]].a + origColors[triangles[i + 2]].a) / 3f;
                    if (avg < endAvg)
                    {
                        endAvg = avg;
                        endTri = i;
                    }
                }
                Check(endTri >= 0 && endAvg < .25f, shape + ": an end top triangle at the emission floor must exist.");
                float endMin = Mathf.Min(origColors[triangles[endTri]].a, Mathf.Min(origColors[triangles[endTri + 1]].a, origColors[triangles[endTri + 2]].a));
                Check(Mathf.Abs(endMin - .2f) < .01f, $"{shape}: the end triangle must reach the 20% emission floor, got {endMin:F4}.");
                Vector3 endCenter = (vertices[triangles[endTri]] + vertices[triangles[endTri + 1]] + vertices[triangles[endTri + 2]]) / 3f;
                Vector3 endNormal = Vector3.Cross(vertices[triangles[endTri + 1]] - vertices[triangles[endTri]], vertices[triangles[endTri + 2]] - vertices[triangles[endTri]]).normalized;
                Vector3 endWorldCenter = root.transform.TransformPoint(endCenter);
                Vector3 endWorldNormal = root.transform.TransformDirection(endNormal).normalized;
                camera.transform.SetPositionAndRotation(endWorldCenter + endWorldNormal * .4f, Quaternion.LookRotation(-endWorldNormal, worldForward));
                mesh.colors = origColors;
                camera.backgroundColor = Color.black;
                Color endActual = ReadCenter(camera, target, readback);
                mesh.colors = lit;
                Color endFull = ReadCenter(camera, target, readback);
                mesh.colors = origColors;
                Check(endActual.r > .001f || endActual.g > .001f || endActual.b > .001f, shape + ": the 20% endpoint must still emit light.");
                float endRgbErr = Mathf.Max(Mathf.Abs(endActual.r / endFull.r - endAvg), Mathf.Max(Mathf.Abs(endActual.g / endFull.g - endAvg), Mathf.Abs(endActual.b / endFull.b - endAvg)));
                float endAlphaErr = Mathf.Abs(endActual.a / endFull.a - endAvg);
                Check(endRgbErr < .005f && endAlphaErr < .005f, $"{shape}: the 20% endpoint must emit {endAvg:F3} of the same face fully lit (rgbErr {endRgbErr:F4} alphaErr {endAlphaErr:F4}).");
                Debug.Log($"[MenuLightNativeBloomValidation] {shape}: clearErr={err:F5} fullyLitA={fullyLit.a:F4} rgbRatio={rgbRatio:F3} alphaRatio={alphaRatio:F3} darkMax={darkMax:F4} endRatio={endActual.a / endFull.a:F3} endExpected={endAvg:F3}");
            }
            finally
            {
                mesh.colors = origColors;
                camera.orthographic = oldOrtho;
                camera.orthographicSize = oldSize;
                camera.transform.SetPositionAndRotation(oldPos, oldRot);
                camera.projectionMatrix = oldProjection;
                camera.backgroundColor = oldClear;
                halo.SetActive(false);
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
            // The solid core writes depth, so only the nearest front-facing surface supplies
            // bloom even when it fades; a dim near face occludes any brighter face behind it.
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
                alpha = fade * material.color.a * material.GetFloat("_BloomMultiplier");
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
            string[] fields = { "_parabolaControls", "_sizeControls", "_materialWidthControls", "_floorGlowControls", "_freePoseControls", "_floorHeightControls", "_undersideDepthControls", "_frameWidthHint" };
            GameObject[] groups = new GameObject[fields.Length];
            try
            {
                for (int index = 0; index < fields.Length; index++)
                {
                    groups[index] = new GameObject(fields[index]);
                    ui.GetType().GetField(fields[index], Members).SetValue(ui, groups[index]);
                }
                Call(ui, "BeginEditor", null, (Action)(() => { }));
                foreach (string shape in new[] { "Floor", "Cube", "Parabola", "Square", "Circle" })
                {
                    Set(ui, "TypeValue", Enum.Parse(TypeOf("MenuLightType"), shape));
                    bool floor = shape == "Floor";
                    Check(groups[3].activeSelf == floor, "Only Floor may expose floor-glow controls.");
                    Check(groups[4].activeSelf != floor, "Floor may not expose Height/Pitch/Roll.");
                    Check(groups[5].activeSelf == floor, "Only Floor may expose the floor-height slider.");
                    Check(groups[6].activeSelf != floor, "Only non-floor shapes may expose underside depth.");
                    Check(groups[7].activeSelf == (shape == "Square" || shape == "Circle"), "Only Square and Circle show the capped light-width hint.");
                    Check(groups[0].activeSelf == (shape == "Parabola" || shape == "Floor"), "Only Parabola and Floor must expose curve controls.");
                    Check(groups[1].activeSelf == (shape != "Parabola" && shape != "Floor"), "Only closed shapes may expose Size.");
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
