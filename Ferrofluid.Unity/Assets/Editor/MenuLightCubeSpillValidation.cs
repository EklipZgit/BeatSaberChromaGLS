using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // Validates MenuLightCubeSpill on the exported native menu-cube mesh
    // (CubeNoteSmoothHD_0.asset, GUID d552425fdec20d14e88596fff74f70f4). The base
    // material is a black stand-in — the fixture proves the additive pass lights the
    // real geometry, not the original NoteMenu PBR.
    internal static partial class MenuLightCubeSpillValidation
    {
        private const int Resolution = 256;
        private const int SamplePixel = 128;
        private const int Layer = 30;

        public static void Run() => FerrofluidValidationRunner.Run(typeof(MenuLightCubeSpillValidation));

        private static void RunRuntime()
        {
            string directory = FerrofluidValidationArtifacts.CreateRunDirectory("cube-spill");
            Mesh mesh = LoadFixtureMesh();
            Shader baseShader = LoadShader("Assets/Editor/Fixtures/NativeMenuCubeBase.shader");
            Shader spillShader = LoadShader("Assets/MenuLightCubeSpill.shader");
            Scene previousScene = SceneManager.GetActiveScene();
            Scene scene = SceneManager.CreateScene("MenuLightCubeSpillValidation");
            SceneManager.SetActiveScene(scene);
            RenderTexture previousTarget = RenderTexture.active;
            Color previousAmbient = RenderSettings.ambientLight;
            GameObject root = new("DefaultMenuEnvironment");
            GameObject cameraObject = new("Cube spill camera");
            Material baseMaterial = new(baseShader) { name = "NoteMenu" };
            Material spillMaterial = new(spillShader) { renderQueue = baseMaterial.renderQueue + 1 };
            RenderTexture target = new(Resolution, Resolution, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                antiAliasing = 1,
            };
            Texture2D readback = new(Resolution, Resolution, TextureFormat.RGBAFloat, false, true);
            try
            {
                if (!mesh.isReadable || mesh.vertexCount != 512)
                    throw new InvalidOperationException("The fixture requires the readable exported native 512-vertex cube.");

                GameObject notes = new("Notes");
                notes.transform.SetParent(root.transform, false);
                GameObject note = new("Note") { layer = Layer };
                note.transform.SetParent(notes.transform, false);
                note.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = note.AddComponent<MeshRenderer>();
                Color baseColor = new(1f, 1f, 1f, 0.36f);
                baseMaterial.color = baseColor;
                renderer.sharedMaterials = new[] { baseMaterial, spillMaterial };
                spillMaterial.SetVectorArray("_MenuLightSpillPositions", SourceArray(new Vector4(0, 1, -2, 1), 32));
                spillMaterial.SetVectorArray("_MenuLightSpillEnergy", SourceArray(new Vector4(100, 150, 200, 0), 32));
                spillMaterial.SetVectorArray("_MenuLightSpillNormals", SourceArray(new Vector4(0, 1, 0, 0), 32));
                spillMaterial.SetInt("_MenuLightSpillCount", 1);
                spillMaterial.SetColor("_Color", Color.white);

                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.transform.SetPositionAndRotation(new Vector3(0, 0, -3), Quaternion.identity);
                camera.orthographic = true;
                camera.orthographicSize = 0.5f;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 10f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.allowHDR = true;
                camera.allowMSAA = false;
                camera.cullingMask = 1 << Layer;
                target.Create();
                camera.targetTexture = target;
                RenderSettings.ambientLight = Color.black;
                camera.Render();
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
                readback.Apply();
                Color actual = readback.GetPixel(SamplePixel, SamplePixel);
                float coordinate = (SamplePixel + 0.5f) / Resolution;
                Ray ray = camera.ViewportPointToRay(new Vector3(coordinate, coordinate, 0f));
                (Vector3 point, Vector3 normal) = RayMesh(ray, mesh);
                Vector3 expected = Expected(point, normal, new Vector3(0, 1, -2), 1f, new Vector3(100, 150, 200), Vector3.up);
                string sample = FormattableString.Invariant(
                    $"point={point:F8} normal={normal:F8} expectedRGB={expected:F8} actualRGBA={actual:F8}");
                Debug.Log($"[MenuLightCubeSpill] {sample} capture={directory}");
                File.WriteAllText(Path.Combine(directory, "sample.txt"), sample);
                File.WriteAllBytes(Path.Combine(directory, "native-cube.exr"), readback.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                SavePreviews(readback.GetPixels(), directory);
                if (renderer.sharedMaterials[0] != baseMaterial || baseMaterial.color != baseColor)
                    throw new InvalidOperationException("The spill pass must not replace or modify the base material.");

                if (Mathf.Abs(actual.a - 0.36f) > 0.002f)
                    throw new InvalidOperationException($"The rendered cube must retain base alpha .36; actual {actual.a:R}.");

                if (expected.x <= 0.1f)
                    throw new InvalidOperationException("The fixture must sample a nontrivial facing-source contribution.");

                float error = Mathf.Max(Mathf.Abs(actual.r - expected.x), Mathf.Max(Mathf.Abs(actual.g - expected.y), Mathf.Abs(actual.b - expected.z)));
                if (error > 0.002f)
                    throw new InvalidOperationException($"Facing source must light the native cube: expected {expected:F6}, actual {actual:F6}.");

                ValidateShaderCases(spillMaterial, camera, target, readback, mesh, point, normal);
                ValidatePublisher(root.transform, renderer, baseMaterial, spillShader, camera, target, readback, directory);
            }
            finally
            {
                RenderTexture.active = previousTarget;
                RenderSettings.ambientLight = previousAmbient;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(baseMaterial);
                Object.DestroyImmediate(spillMaterial);
                SceneManager.SetActiveScene(previousScene);
                SceneManager.UnloadSceneAsync(scene);
            }
        }

        private static Vector3 Expected(Vector3 point, Vector3 normal, Vector3 sourcePos, float radiusSq, Vector3 energy, Vector3 sourceNormal)
        {
            Vector3 delta = sourcePos - point;
            float d2 = delta.sqrMagnitude;
            Vector3 direction = delta / Mathf.Sqrt(Mathf.Max(d2, 1e-8f));
            float cos = Mathf.Max(0f, Vector3.Dot(normal, direction));
            float emitter = Mathf.Abs(Vector3.Dot(sourceNormal, direction));
            return energy * cos * emitter / (Mathf.PI * Mathf.Max(d2 + radiusSq, 1e-8f));
        }

        private static (Vector3 point, Vector3 normal) RayMesh(Ray ray, Mesh mesh)
        {
            float best = float.PositiveInfinity;
            Vector3 point = Vector3.zero;
            Vector3 normal = Vector3.up;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] triangles = mesh.triangles;
            for (int index = 0; index < triangles.Length; index += 3)
            {
                int a = triangles[index];
                int b = triangles[index + 1];
                int c = triangles[index + 2];
                if (RayTriangle(ray, vertices[a], vertices[b], vertices[c], out float distance, out Vector3 barycentric)
                    && distance < best)
                {
                    best = distance;
                    point = ray.GetPoint(distance);
                    normal = ((normals[a] * barycentric.x) + (normals[b] * barycentric.y) + (normals[c] * barycentric.z)).normalized;
                }
            }

            if (float.IsPositiveInfinity(best))
                throw new InvalidOperationException("The sample ray missed the native cube.");

            return (point, normal);
        }

        private static bool RayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float t, out Vector3 bary)
        {
            t = 0;
            bary = Vector3.zero;
            Vector3 e1 = b - a;
            Vector3 e2 = c - a;
            Vector3 p = Vector3.Cross(ray.direction, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-8f)
                return false;

            float inv = 1f / det;
            Vector3 s = ray.origin - a;
            float u = Vector3.Dot(s, p) * inv;
            Vector3 q = Vector3.Cross(s, e1);
            float w = Vector3.Dot(ray.direction, q) * inv;
            float v2 = 1f - u - w;
            if (u < 0 || w < 0 || v2 < 0)
                return false;

            t = Vector3.Dot(e2, q) * inv;
            if (t < 0)
                return false;

            bary = new Vector3(v2, u, w);
            return true;
        }

        private static void SavePreviews(Color[] pixels, string directory)
        {
            Texture2D preview = new(Resolution, Resolution, TextureFormat.RGBA32, false, true);
            try
            {
                preview.SetPixels(pixels);
                preview.Apply();
                File.WriteAllBytes(Path.Combine(directory, "native-cube-color.png"), preview.EncodeToPNG());
                for (int index = 0; index < pixels.Length; index++)
                {
                    float alpha = pixels[index].a;
                    pixels[index] = new Color(alpha, alpha, alpha, 1f);
                }

                preview.SetPixels(pixels);
                preview.Apply();
                File.WriteAllBytes(Path.Combine(directory, "native-cube-coverage.png"), preview.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(preview);
            }
        }

        private static Vector4[] SourceArray(Vector4 value, int count)
        {
            var arr = new Vector4[count];
            arr[0] = value;
            return arr;
        }

        private static Mesh LoadFixtureMesh()
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Editor/Fixtures/NativeMenuCube.asset");
            if (mesh == null)
                throw new InvalidOperationException("The exported native menu-cube fixture mesh is missing.");

            return mesh;
        }

        private static Shader LoadShader(string path)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException($"The fixture shader failed to load or compile: {path}");

            return shader;
        }
    }
}
