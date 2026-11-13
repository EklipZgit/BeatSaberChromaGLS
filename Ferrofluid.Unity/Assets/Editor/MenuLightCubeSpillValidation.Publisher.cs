using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    internal static partial class MenuLightCubeSpillValidation
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static void ValidatePublisher(
            Transform environment,
            MeshRenderer renderer,
            Material baseMaterial,
            Shader shader,
            Camera camera,
            RenderTexture target,
            Texture2D readback,
            string directory)
        {
            Assembly plugin = LoadPlugin();
            renderer.sharedMaterials = new[] { baseMaterial };
            object publisher = NewProduct(plugin, "MenuLightCubeSpill", shader, environment);
            try
            {
                Type definitionType = ProductType(plugin, "MenuLightDefinition");
                Type meshesType = ProductType(plugin, "MenuLightMeshes");
                object definition = NewProduct(plugin, "MenuLightDefinition");
                SetProductProperty(definition, "Id", "native-cube-light");
                object appearance = ProductType(plugin, "ParabolicFloorLightSettings").GetField("Default", Static).GetValue(null);
                foreach (string channel in new[] { "Red", "Green", "Blue" })
                    appearance = InvokeProduct(appearance, "With" + channel, 1f);

                appearance = InvokeProduct(appearance, "WithBrightness", 3f);
                SetProductProperty(definition, "Appearance", appearance);
                Vector3[] vertices = { new(.25f, 0, .25f), new(1.25f, 0, .25f), new(.25f, 0, 1.25f) };
                object core = NewProduct(plugin, "ParabolicFloorLightMeshData", vertices, new[] { 0, 1, 2 }, new[] { Color.white, Color.white, Color.white }, new Vector2[3]);
                object glow = NewProduct(plugin, "ParabolicFloorLightGlowMeshData", Array.Empty<Vector3>(), Array.Empty<int>(), Array.Empty<Color>(), Array.Empty<Vector2>());
                object bounds = NewProduct(plugin, "MenuLightReflectionBounds");
                bounds.GetType().GetField("InverseWidth", Instance).SetValue(bounds, .5f);
                bounds.GetType().GetField("InverseHeight", Instance).SetValue(bounds, .5f);
                object meshes = NewProduct(
                    plugin,
                    "MenuLightMeshes",
                    core,
                    glow,
                    core,
                    new Vector3(-7f / 12f, 1f, -2f - (7f / 12f)),
                    Quaternion.identity,
                    bounds);
                IList definitions = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(definitionType));
                definitions.Add(definition);
                IDictionary meshDictionary = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), meshesType));
                meshDictionary.Add("native-cube-light", meshes);
                InvokeProduct(publisher, "Synchronize", definitions, meshDictionary, true);

                Color tint = new(6f, 6f, 6f, 3f);
                if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                    tint = tint.linear;

                float coordinate = (SamplePixel + .5f) / Resolution;
                (Vector3 point, Vector3 normal) = RayMesh(camera.ViewportPointToRay(new Vector3(coordinate, coordinate, 0)), renderer.GetComponent<MeshFilter>().sharedMesh);
                Vector3 expected = Expected(point, normal, new Vector3(0, 1, -2), .5f / Mathf.PI, new Vector3(tint.r, tint.g, tint.b) * 1.5f, Vector3.up);
                Color actual = ReadCenter(camera, target, readback);
                string capture = Path.Combine(directory, "publisher");
                Directory.CreateDirectory(capture);
                SavePreviews(readback.GetPixels(), capture);
                File.WriteAllBytes(Path.Combine(capture, "native-cube.exr"), readback.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                Debug.Log($"[MenuLightCubeSpill] production publisher expectedRGB={expected:F8} actualRGBA={actual:F8} capture={capture}");
                AssertColor(actual, expected, "Production publisher must illuminate the native cube.");
            }
            finally
            {
                ((IDisposable)publisher).Dispose();
            }
        }

        private static Color ReadCenter(Camera camera, RenderTexture target, Texture2D readback)
        {
            camera.Render();
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
            readback.Apply();
            return readback.GetPixel(SamplePixel, SamplePixel);
        }

        private static void AssertColor(Color actual, Vector3 expected, string message)
        {
            float error = Mathf.Max(Mathf.Abs(actual.r - expected.x), Mathf.Max(Mathf.Abs(actual.g - expected.y), Mathf.Abs(actual.b - expected.z)));
            if (error > .002f || Mathf.Abs(actual.a - .36f) > .002f)
                throw new InvalidOperationException($"{message} Expected RGB={expected:F8}, alpha=.36; actual={actual:F8}.");
        }

        private static Assembly LoadPlugin()
        {
            string repository = Directory.GetParent(Application.dataPath).Parent.FullName;
            string managed = Environment.GetEnvironmentVariable("CHROMAGLS_TEST_MANAGED")
                ?? Path.Combine(repository, "ChromaGLS.Tests", "bin", "Release-1.44.1", "net48");
            AppDomain.CurrentDomain.AssemblyResolve += (_, arguments) =>
            {
                string path = Path.Combine(managed, new AssemblyName(arguments.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            return Assembly.LoadFrom(Path.Combine(managed, "ChromaGLS.dll"));
        }

        private static Type ProductType(Assembly plugin, string name) => plugin.GetType("ChromaGLS.Settings." + name, true);

        private static object NewProduct(Assembly plugin, string name, params object[] arguments)
            => Activator.CreateInstance(ProductType(plugin, name), Instance, null, arguments, null);

        private static object InvokeProduct(object value, string name, params object[] arguments)
            => value.GetType().GetMethod(name, Instance).Invoke(value, arguments);

        private static void SetProductProperty(object value, string name, object setting)
            => value.GetType().GetProperty(name, Instance).SetValue(value, setting);
    }
}
