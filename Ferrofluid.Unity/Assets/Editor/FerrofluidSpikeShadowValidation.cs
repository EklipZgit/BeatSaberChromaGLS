using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // The analytic floor shadow can never show spike silhouette changes; the GPU mask
    // must. Off/on is toggled through the real surface material's _SpikesEnabled while the
    // seeded spring state is held fixed, so identical frames mean the mask path did not
    // render actual deformed geometry.
    public static class FerrofluidSpikeShadowValidation
    {
        private const int Resolution = 512;
        private const float FloorY = 0f;
        private static readonly string Output = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "..", "..", "Scratch", "ferro-spike-shadows"));
        private static readonly Vector2 Slope = new(0.5f, 0.5f);

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidSpikeShadowValidation));

        private static void RunRuntime()
        {
            Directory.CreateDirectory(Output);
            List<Object> owned = new();
            RenderTexture previous = RenderTexture.active;
            try
            {
                Shader shadowShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidFloorShadow.shader");
                Assembly plugin = FerrofluidValidationRuntime.CreateConfig().GetType().Assembly;
                Type shapeType = plugin.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", true)!;
                Type simType = plugin.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", true)!;
                Type shadowType = plugin.GetType("ChromaGLS.Settings.FerrofluidFloorShadow", true)!;
                ConstructorInfo six = shadowType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Transform), shapeType, typeof(float), typeof(Vector3), typeof(Shader), simType },
                    null);
                if (six == null)
                    throw new InvalidOperationException("FerrofluidFloorShadow is missing the simulation-backed constructor.");

                Camera camera = BuildCamera(owned, out GameObject cameraObject, out GameObject floorObject);
                RenderTexture target = new(Resolution, Resolution, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                target.Create();
                owned.Add(target);
                camera.targetTexture = target;

                ValidateOrientationTriangle(owned);
                ValidateShape("RoundedCube", six, shapeType, camera, target, owned);
                ValidateShape("Sphere", six, shapeType, camera, target, owned);

                cameraObject.SetActive(false);
                floorObject.SetActive(false);
                Debug.Log("[FerrofluidSpikeShadowValidation] PASS");
            }
            finally
            {
                RenderTexture.active = previous;
                foreach (Object item in owned)
                    Object.DestroyImmediate(item);
            }
        }

        private static void ValidateShape(
            string shapeName,
            ConstructorInfo ctor,
            Type shapeType,
            Camera camera,
            RenderTexture target,
            List<Object> owned)
        {
            object config = FerrofluidValidationRuntime.CreateConfig();
            using FerrofluidValidationRuntime runtime = new(shapeName, config);
            MeshRenderer body = (MeshRenderer)runtime.Get("_renderer");
            body.transform.position = new Vector3(0f, 0.45f, 0f);
            Material material = (Material)runtime.Get("_surfaceMaterial");
            runtime.Set("_renderEnabled", true);
            runtime.Invoke(
                "UpdatePrimitiveFrame",
                1f,
                new Vector3(0.18f, 0.7f, 0.12f),
                new Vector3(0.18f, 0.44f, 0.12f),
                1f,
                Vector3.zero,
                0f,
                Vector3.zero,
                Vector3.zero,
                1.6f);
            for (int index = 0; index < 240; index++)
                runtime.Invoke("AdvancePrimitiveSimulation", 1f / 120f);

            // Seed the actual spring state with a uniform tall spike so the silhouette
            // change is unambiguous; velocity stays zero.
            RenderTexture spring = (RenderTexture)runtime.Get("_springCurrent");
            if (spring == null)
                spring = (RenderTexture)material.GetTexture("_SpringStateTex");
            Texture2D seed = new(spring.width, spring.height, TextureFormat.RGBAFloat, false, true);
            Color[] uniform = new Color[spring.width * spring.height];
            for (int index = 0; index < uniform.Length; index++)
                uniform[index] = new Color(1.6f, 0f, 0f, 1f);
            seed.SetPixels(uniform);
            seed.Apply();
            Graphics.Blit(seed, spring);
            Object.DestroyImmediate(seed);
            material.SetFloat("_MagneticFieldStrength", 0.9f);
            material.SetVector("_MagneticFieldPosition", new Vector3(0.18f, 0.7f, 0.12f));
            material.SetFloat("_MagnetHeightAmplification", 1.6f);
            runtime.Invoke("RenderPrimitiveDeformation");

            object shadow = ctor.Invoke(new object[]
            {
                body.transform,
                Enum.Parse(shapeType, shapeName),
                FloorY,
                new Vector3(0.5f, 1f, 0.5f),
                AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidFloorShadow.shader"),
                runtime.Simulation,
            });
            using IDisposable shadowLifetime = (IDisposable)shadow;
            MethodInfo update = shadow.GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            MethodInfo setVisible = shadow.GetType().GetMethod("SetVisible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            body.enabled = false;

            // FERROFLUID_ENVIRONMENT_CAPTURE suppresses the floor shadow entirely so room
            // captures never see it; restore the keyword regardless of outcome.
            bool capture = Shader.IsKeywordEnabled("FERROFLUID_ENVIRONMENT_CAPTURE");
            try
            {
                Shader.EnableKeyword("FERROFLUID_ENVIRONMENT_CAPTURE");
                update.Invoke(shadow, null);
                Color[] captured = RenderFrame(camera, target);
                int darkDuringCapture = 0;
                for (int index = 0; index < captured.Length; index++)
                    if (captured[index].r < 0.95f)
                        darkDuringCapture++;
                if (darkDuringCapture > 0)
                    throw new Exception($"{shapeName}: capture keyword must suppress the shadow, {darkDuringCapture} pixels still darkened.");
            }
            finally
            {
                if (capture)
                    Shader.EnableKeyword("FERROFLUID_ENVIRONMENT_CAPTURE");
                else
                    Shader.DisableKeyword("FERROFLUID_ENVIRONMENT_CAPTURE");
            }

            // The approved budget is one 512 R8 no-depth mask plus a matching blur target.
            FieldInfo maskField = shadow.GetType().GetField("_mask", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object maskObject = maskField.GetValue(shadow);
            if (maskObject == null)
                throw new Exception($"{shapeName}: the simulation-backed shadow has no GPU mask.");
            RenderTexture maskTarget = (RenderTexture)maskObject.GetType().GetProperty("Texture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(maskObject);
            if (maskTarget.width != 512 || maskTarget.format != RenderTextureFormat.R8 || maskTarget.depth != 0)
                throw new Exception($"{shapeName}: mask target must be 512 R8 depth 0, got {maskTarget.width} {maskTarget.format} depth {maskTarget.depth}.");

            // The mask update must not drive any scene camera render.
            _sceneRenders = 0;
            Camera.onPostRender += CountRender;
            try
            {
                material.SetFloat("_SpikesEnabled", 1f);
                update.Invoke(shadow, null);
            }
            finally
            {
                Camera.onPostRender -= CountRender;
            }

            if (_sceneRenders != 0)
                throw new Exception($"{shapeName}: mask update triggered {_sceneRenders} scene camera renders.");
            Color[] on = RenderFrame(camera, target);
            Save(on, $"spike-shadow-{shapeName.ToLowerInvariant()}-on.png");
            material.SetFloat("_SpikesEnabled", 0f);
            update.Invoke(shadow, null);
            Color[] off = RenderFrame(camera, target);
            Save(off, $"spike-shadow-{shapeName.ToLowerInvariant()}-off.png");
            material.SetFloat("_SpikesEnabled", 1f);

            int changed = 0;
            float darkest = 1f;
            for (int index = 0; index < on.Length; index++)
            {
                darkest = Mathf.Min(darkest, on[index].r, off[index].r);
                if (Mathf.Abs(on[index].r - off[index].r) > 0.01f)
                    changed++;
            }

            if (changed == 0)
                throw new Exception($"{shapeName}: the rendered spike silhouette did not change the floor shadow (darkest={darkest:F3}).");

            // Repeat render must be stable.
            update.Invoke(shadow, null);
            Color[] again = RenderFrame(camera, target);
            int drift = 0;
            for (int index = 0; index < on.Length; index++)
                if (Mathf.Abs(on[index].r - again[index].r) > 0.01f)
                    drift++;
            if (drift > 40)
                throw new Exception($"{shapeName}: repeated shadow render drifted by {drift} pixels.");

            // The world-space floor image must follow the body (raw mask is recentered).
            Vector3 at = body.transform.position;
            body.transform.position = at + new Vector3(0.7f, 0f, 0.3f);
            update.Invoke(shadow, null);
            Color[] moved = RenderFrame(camera, target);
            body.transform.position = at;
            update.Invoke(shadow, null);
            float centroidOn = CentroidX(on);
            float centroidMoved = CentroidX(moved);
            if (centroidMoved - centroidOn < 30f)
                throw new Exception($"{shapeName}: translated body did not move the floor shadow ({centroidOn:F1}->{centroidMoved:F1}).");

            // Union coverage: overlapping deformed triangles can never darken more than
            // the single-coverage cap 1 - opacity = 0.68.
            if (darkest < 0.67f)
                throw new Exception($"{shapeName}: mask union exceeded single-coverage opacity (darkest={darkest:F3}).");

            // Rotation must carry the silhouette; yaw 90 degrees changes the projected shape.
            body.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            update.Invoke(shadow, null);
            Color[] rotated = RenderFrame(camera, target);
            int rotChanged = 0;
            for (int index = 0; index < on.Length; index++)
                if (Mathf.Abs(on[index].r - rotated[index].r) > 0.01f)
                    rotChanged++;
            body.transform.rotation = Quaternion.identity;
            if (rotChanged == 0)
                throw new Exception($"{shapeName}: rotating the body 90 degrees left the silhouette unchanged.");

            // Raising the body widens the projected gap: softness grows and opacity
            // fades per .32/(1+.7gap), so the darkest pixel must lighten.
            body.transform.position = at + new Vector3(0f, 1.2f, 0f);
            update.Invoke(shadow, null);
            Color[] raised = RenderFrame(camera, target);
            float raisedDarkest = 1f;
            for (int index = 0; index < raised.Length; index++)
                raisedDarkest = Mathf.Min(raisedDarkest, raised[index].r);
            body.transform.position = at;
            update.Invoke(shadow, null);
            if (raisedDarkest <= darkest + 0.02f)
                throw new Exception($"{shapeName}: raising the body did not soften the shadow (low={darkest:F3} raised={raisedDarkest:F3}).");

            setVisible.Invoke(shadow, new object[] { false });
            update.Invoke(shadow, null);
            Color[] hidden = RenderFrame(camera, target);
            int dark = 0;
            for (int index = 0; index < hidden.Length; index++)
                if (hidden[index].r < 0.95f)
                    dark++;
            if (dark > 0)
                throw new Exception($"{shapeName}: hidden shadow still darkened {dark} pixels.");
            setVisible.Invoke(shadow, new object[] { true });
            update.Invoke(shadow, null);
            body.enabled = true;
            Debug.Log($"[FerrofluidSpikeShadowValidation] {shapeName} changed={changed} drift={drift} rotChanged={rotChanged} low={darkest:F2} raised={raisedDarkest:F2} centroid={centroidOn:F1}->{centroidMoved:F1}");
        }

        // Asymmetric world-floor projection check: a 3-UV sphere patch with no height,
        // rotation, or deformation must darken exactly at its CPU-projected centroid and
        // not at the mirrored-Z point. This proves the mask's D3D clip-Y convention.
        private static void ValidateOrientationTriangle(List<Object> owned)
        {
            object config = FerrofluidValidationRuntime.CreateConfig();
            using FerrofluidValidationRuntime runtime = new("Sphere", config);
            Material material = (Material)runtime.Get("_surfaceMaterial");
            material.SetVector("_PrimitiveYawCosSin", new Vector2(1f, 0f));
            material.SetFloat("_SpikesEnabled", 0f);
            material.SetFloat("_MagneticFieldStrength", 0f);
            material.SetFloat("_BulgeHeight", 0f);
            material.SetTexture("_HeightMapTex", Texture2D.blackTexture);
            material.SetTexture("_SiteDeformationTex", Texture2D.blackTexture);
            material.SetFloat("_SurfaceShapeMode", 2f);

            Vector2[] uvs = { new(0.75f, 0.60f), new(0.80f, 0.60f), new(0.75f, 0.65f) };
            Mesh tri = new();
            tri.SetVertices(new List<Vector3> { Vector3.zero, Vector3.zero, Vector3.zero });
            tri.SetUVs(0, new List<Vector2>(uvs));
            tri.SetTriangles(new[] { 0, 1, 2 }, 0);
            owned.Add(tri);

            Vector3 centroid = Vector3.zero;
            foreach (Vector2 uv in uvs)
            {
                float x = (uv.x * 2f) - 1f;
                float z = (uv.y * 2f) - 1f;
                centroid += 0.5f * new Vector3(x, 1f - Mathf.Abs(x) - Mathf.Abs(z), z).normalized;
            }
            centroid /= 3f;
            float worldY = centroid.y + 0.45f;
            Vector2 hit = new(
                centroid.x - ((worldY - 0f) * Slope.x),
                centroid.z - ((worldY - 0f) * Slope.y));

            // Draw the triangle through the same mask path into fixed [-2,2] bounds.
            RenderTexture mask = new(Resolution, Resolution, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            mask.Create();
            owned.Add(mask);
            int pass = material.FindPass("FERROFLUID_SHADOW_MASK");
            material.SetVector("_FerroMaskSlope", Slope);
            material.SetFloat("_FerroMaskFloorY", 0f);
            material.SetVector("_FerroMaskCenter", Vector2.zero);
            material.SetVector("_FerroMaskInvExtent", new Vector2(0.5f, 0.5f));
            RenderTexture previous = RenderTexture.active;
            material.SetPass(pass);
            RenderTexture.active = mask;
            GL.Clear(true, true, Color.black);
            Graphics.DrawMeshNow(tri, Matrix4x4.Translate(new Vector3(0f, 0.45f, 0f)));
            RenderTexture.active = previous;

            RenderTexture.active = mask;
            Texture2D read = new(Resolution, Resolution, TextureFormat.RGBAFloat, false, true);
            read.ReadPixels(new Rect(0f, 0f, Resolution, Resolution), 0, 0);
            read.Apply();
            RenderTexture.active = previous;
            Color[] cover = read.GetPixels();
            Object.DestroyImmediate(read);
            double cx = 0, cz = 0, weight = 0;
            for (int index = 0; index < cover.Length; index++)
            {
                if (cover[index].r <= 0.25f)
                    continue;
                cx += index % Resolution;
                cz += index / Resolution;
                weight += 1;
            }
            if (weight == 0)
                throw new Exception("orientation: the triangle patch rasterized zero mask pixels.");
            Vector2 measured = new((float)(cx / weight), (float)(cz / weight));
            Vector2 expectedPx = new(((hit.x + 2f) / 4f) * Resolution, ((hit.y + 2f) / 4f) * Resolution);
            Vector2 mirroredPx = new(expectedPx.x, Resolution - 1f - expectedPx.y);
            float dExpected = Vector2.Distance(measured, expectedPx);
            float dMirror = Vector2.Distance(measured, mirroredPx);
            Debug.Log($"[FerrofluidSpikeShadowValidation] orientation covered={weight} measured=({measured.x:F0},{measured.y:F0}) expected=({expectedPx.x:F0},{expectedPx.y:F0}) dExp={dExpected:F0} dMir={dMirror:F0}");
            if (dExpected > 3f && dMirror < dExpected)
                throw new Exception($"orientation: covered centroid sits at the mirrored-Z location (dExp={dExpected:F0} dMir={dMirror:F0}) — mask clip-Y is flipped.");
            if (dExpected > 3f)
                throw new Exception($"orientation: covered centroid ({measured.x:F0},{measured.y:F0}) is far from the projected point ({expectedPx.x:F0},{expectedPx.y:F0}).");
        }

        private static int _sceneRenders;

        private static void CountRender(Camera _)
            => _sceneRenders++;

        private static float CentroidX(Color[] pixels)
        {
            double sum = 0, weight = 0;
            for (int index = 0; index < pixels.Length; index++)
            {
                float w = 1f - pixels[index].r;
                sum += (index % Resolution) * w;
                weight += w;
            }
            return weight <= 0 ? 0f : (float)(sum / weight);
        }

        private static Camera BuildCamera(List<Object> owned, out GameObject cameraObject, out GameObject floorObject)
        {
            floorObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floorObject.name = "Validation Floor";
            floorObject.transform.position = new Vector3(0f, -0.01f, 0f);
            floorObject.transform.localScale = new Vector3(4f, 0.02f, 4f);
            floorObject.layer = 27;
            MeshRenderer floorRenderer = floorObject.GetComponent<MeshRenderer>();
            floorRenderer.material = new Material(Shader.Find("Unlit/Color")) { color = Color.white };
            owned.Add(floorObject);
            owned.Add(floorRenderer.material);

            cameraObject = new GameObject("Validation Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 5f, 0f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            camera.orthographic = true;
            camera.orthographicSize = 2f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = (1 << 0) | (1 << 27);
            camera.enabled = false;
            owned.Add(cameraObject);
            return camera;
        }

        private static Color[] RenderFrame(Camera camera, RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                camera.Render();
                Texture2D pixels = new(Resolution, Resolution, TextureFormat.RGBAFloat, false, true);
                try
                {
                    pixels.ReadPixels(new Rect(0f, 0f, Resolution, Resolution), 0, 0);
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

        private static void Save(Color[] pixels, string name)
        {
            Texture2D image = new(Resolution, Resolution, TextureFormat.RGBA32, false, false);
            try
            {
                image.SetPixels(pixels);
                image.Apply();
                File.WriteAllBytes(Path.Combine(Output, name), image.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }
    }
}
