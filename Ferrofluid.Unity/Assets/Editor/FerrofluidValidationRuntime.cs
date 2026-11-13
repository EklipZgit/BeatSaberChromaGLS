using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // Construct the actual runtime simulation without starting IPA or its background log writers.
    internal sealed class FerrofluidValidationRuntime : IDisposable
    {
        private static readonly BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Assembly Plugin;
        private readonly HashSet<Object> _owned = new();
        private readonly GameObject _gameObject;
        private readonly Type _type;

        static FerrofluidValidationRuntime()
        {
            string repository = Directory.GetParent(Application.dataPath)!.Parent!.FullName;
            string managed = Path.Combine(repository, "ChromaGLS.Tests", "bin", "Release-1.44.1", "net48");
            AppDomain.CurrentDomain.AssemblyResolve += (_, arguments) =>
            {
                string path = Path.Combine(managed, new AssemblyName(arguments.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            Plugin = Assembly.LoadFrom(Path.Combine(repository, "ChromaGLS", "bin", "Release-1.44.1", "net48", "ChromaGLS.dll"));
            PropertyInfo loggerProperty = Plugin.GetType("ChromaGLS.Plugin", true)!.GetProperty("Log", Static)!;
            Type loggerType = loggerProperty.PropertyType;
            MethodInfo log = loggerType.GetMethod("Log", new[] { loggerType.GetNestedType("Level")!, typeof(string) })!;
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("FerrofluidValidationLogger"), AssemblyBuilderAccess.Run);
            TypeBuilder logger = assembly.DefineDynamicModule("Logger").DefineType("SilentLogger", TypeAttributes.Public, loggerType);
            logger.DefineDefaultConstructor(MethodAttributes.Public);
            MethodBuilder implementation = logger.DefineMethod(
                "Log",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                typeof(void),
                new[] { loggerType.GetNestedType("Level")!, typeof(string) });
            implementation.GetILGenerator().Emit(OpCodes.Ret);
            logger.DefineMethodOverride(implementation, log);
            loggerProperty.SetValue(null, Activator.CreateInstance(logger.CreateType()!));
        }

        internal FerrofluidValidationRuntime(string shapeName, object config)
        {
            _gameObject = new GameObject("Ferrofluid validation", typeof(MeshFilter), typeof(MeshRenderer));
            _type = Plugin.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", true)!;
            Type resourceType = Plugin.GetType("ChromaGLS.Settings.FerrofluidGpuResources", true)!;
            Type shapeType = Plugin.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", true)!;
            object resources = resourceType.GetConstructor(
                Instance,
                null,
                new[] { typeof(Shader), typeof(Shader), typeof(Texture2D), typeof(Texture2D) },
                null)!.Invoke(new object[]
                {
                    AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSimulation.shader"),
                    AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader"),
                    Texture2D.whiteTexture,
                    Texture2D.whiteTexture,
                });
            Simulation = _type.GetConstructor(
                Instance,
                null,
                new[] { typeof(MeshFilter), typeof(MeshRenderer), resourceType, config.GetType(), shapeType },
                null)!.Invoke(new[]
                {
                    (object)_gameObject.GetComponent<MeshFilter>(),
                    _gameObject.GetComponent<MeshRenderer>(),
                    resources,
                    config,
                    Enum.Parse(shapeType, shapeName),
                });
            CaptureResources();
        }

        internal object Simulation { get; }

        internal int Count => (int)Get("_particleCount");

        internal static object CreateConfig()
        {
            return Activator.CreateInstance(Plugin.GetType("ChromaGLS.Settings.Config", true)!, nonPublic: true)!;
        }

        internal static string? ReflectionPackPath => Environment.GetEnvironmentVariable("CHROMAGLS_REFLECTION_PACK");

        internal static AssetBundle? LoadReflectionPack()
        {
            string? path = ReflectionPackPath;
            return string.IsNullOrEmpty(path) ? null : AssetBundle.LoadFromFile(path);
        }

        internal static void ConfigureReflectionPack()
        {
            Plugin.GetType("ChromaGLS.Settings.FerrofluidRoomReflections", true)!
                .GetMethod("SetReflectionPackPath", Static)!
                .Invoke(null, new object?[] { ReflectionPackPath });
        }

        internal static void SetConfig(object config, string name, float value)
        {
            config.GetType().GetProperty(name)!.SetValue(config, value);
        }

        internal object Get(string name)
        {
            return _type.GetField(name, Instance)!.GetValue(Simulation)!;
        }

        internal object Invoke(string name, params object[] arguments)
        {
            return _type.GetMethod(name, Instance)!.Invoke(Simulation, arguments)!;
        }

        internal void Set(string name, object value)
        {
            CaptureResources();
            _type.GetField(name, Instance)!.SetValue(Simulation, value);
            CaptureResources();
        }

        internal void Apply(object config)
        {
            CaptureResources();
            Invoke("ApplyConfiguration", config);
            CaptureResources();
        }

        internal Vector3[] ReadHomes()
        {
            Color[] pixels = Read((Texture)Get("_restSurface"));
            Vector3[] homes = new Vector3[Count];
            for (int index = 0; index < homes.Length; index++)
                homes[index] = new Vector3(pixels[index].r, pixels[index].g, pixels[index].b);

            return homes;
        }

        internal Vector3[] ReadSitePositions()
        {
            Material simulation = (Material)Get("_simulationMaterial");
            simulation.SetTexture("_StateTex", (Texture)Get("_currentState"));
            RenderTexture surface = (RenderTexture)Get("_siteSurface");
            Graphics.Blit(Texture2D.blackTexture, surface, simulation, 4);
            Color[] pixels = Read(surface);
            Vector3[] positions = new Vector3[Count];
            for (int index = 0; index < positions.Length; index++)
                positions[index] = new Vector3(pixels[index].r, pixels[index].g, pixels[index].b);

            return positions;
        }

        internal static Color[] CreatePositionState(Color[] homes)
        {
            Color[] state = new Color[homes.Length];
            for (int index = 0; index < homes.Length; index++)
                state[index] = new Color(homes[index].r, homes[index].g, 0f, 0f);

            return state;
        }

        internal void WriteState(Color[] pixels)
        {
            Texture source = (Texture)Get("_currentState");
            Texture2D upload = new(source.width, source.height, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
            try
            {
                upload.SetPixels(pixels);
                upload.Apply();
                Graphics.Blit(upload, (RenderTexture)Get("_currentState"));
                Graphics.Blit(upload, (RenderTexture)Get("_nextState"));
            }
            finally
            {
                Object.DestroyImmediate(upload);
            }
        }

        internal static Color[] Read(Texture source)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Texture2D readback = new(source.width, source.height, TextureFormat.RGBAFloat, false, true);
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
                readback.Apply();
                return readback.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(readback);
            }
        }

        public void Dispose()
        {
            CaptureResources();
            foreach (Object resource in _owned)
            {
                if (resource == null)
                    continue;

                if (resource is RenderTexture renderTexture)
                    renderTexture.Release();

                Object.DestroyImmediate(resource);
            }

            Object.DestroyImmediate(_gameObject);
        }

        private void CaptureResources()
        {
            foreach (FieldInfo field in _type.GetFields(Instance))
            {
                object value = field.GetValue(Simulation);
                if (value is Material || value is Texture || value is Mesh)
                    _owned.Add((Object)value);
            }
        }
    }
}
