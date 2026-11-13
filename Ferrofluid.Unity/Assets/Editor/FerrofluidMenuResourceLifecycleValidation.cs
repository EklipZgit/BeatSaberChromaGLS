using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidMenuResourceLifecycleValidation
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidMenuResourceLifecycleValidation));

        private static IEnumerator RunRuntime()
        {
            object config = FerrofluidValidationRuntime.CreateConfig();
            Set(config, "MenuLightsJson", "[]");
            Set(config, "MenuLightDefaultsVersion", 1);
            Assembly plugin = config.GetType().Assembly;
            Type qualityType = plugin.GetType("ChromaGLS.Settings.ReflectionQuality", true);
            object low = Enum.Parse(qualityType, "Low");
            Set(config, "ReflectionsQuality", low);

            Type definitionType = plugin.GetType("ChromaGLS.Settings.MenuLightDefinition", true);
            Type meshesType = plugin.GetType("ChromaGLS.Settings.MenuLightMeshes", true);
            Type cacheType = plugin.GetType("ChromaGLS.Settings.MenuLightReflectionCache", true);
            Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true);
            Type settingsType = plugin.GetType("ChromaGLS.Settings.ParabolicFloorLightSettings", true);
            object enclosure = Activator.CreateInstance(
                enclosureType,
                Instance,
                null,
                new object[] { Vector3.zero, 3f, new Vector3(0f, 0f, -3f), Vector3.forward, 0f },
                null);
            IList definitions = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(definitionType));
            IDictionary meshes = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), meshesType));

            Action<RenderTexture, RenderTexture> process = (source, destination) => Graphics.Blit(source, destination);
            object cache = Activator.CreateInstance(
                cacheType,
                Instance,
                null,
                new object[] { AssetDatabase.LoadAssetAtPath<Shader>("Assets/ParabolicFloorReflection.shader"), process },
                null);
            object red = Definition("red-light", "Square", new Color(2f, 0f, 0f));
            object green = Definition("green-light", "Sphere", new Color(0f, 3f, 0f));
            definitions.Add(red);
            definitions.Add(green);
            meshes.Add("red-light", BuildMeshes(red));
            meshes.Add("green-light", BuildMeshes(green));
            cacheType.GetMethod("SetQuality", Instance).Invoke(cache, new[] { low });
            cacheType.GetMethod("Synchronize", Instance).Invoke(cache, new object[] { definitions, meshes, true });

            Type ownerType = plugin.GetType("ChromaGLS.Settings.ParabolicFloorLight", true);
            object owner = Activator.CreateInstance(ownerType, Instance, null, new[] { config }, null);
            try
            {
                object store = Get(owner, "Store");
                void Commit() => ownerType.GetMethod("Synchronize", Instance).Invoke(owner, new[] { Get(store, "Lights") });
                void OwnerField(string name, object value) => ownerType.GetField(name, Instance).SetValue(owner, value);
                Type storeType = store.GetType();
                storeType.GetMethod("Remove", Instance).Invoke(store, new object[] { "legacy-parabola" });
                object savedRed = Definition("owner-red", "Square", new Color(2f, 0f, 0f));
                object savedGreen = Definition("owner-green", "Sphere", new Color(0f, 3f, 0f));
                storeType.GetMethod("Upsert", Instance).Invoke(store, new[] { savedRed });
                storeType.GetMethod("Upsert", Instance).Invoke(store, new[] { savedGreen });

                OwnerField("_enclosure", enclosure);
                OwnerField("_playerPosition", Vector3.zero);
                OwnerField("_baseForward", Vector3.forward);
                OwnerField("_anchorScene", UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                Shader rendererShader = Shader.Find("Sprites/Default");
                Check(rendererShader != null, "The owner test needs the built-in vertex-color shader.");
                OwnerField("_shader", rendererShader);
                OwnerField("_enabled", true);
                OwnerField("_canCreate", true);
                OwnerField("_reflectionCache", cache);
                Commit();

                RenderTexture atlas = (RenderTexture)Shader.GetGlobalTexture("_FerrofluidMenuLightReflections");
                Check(atlas != null && atlas.IsCreated(), "The owner must bake the Low atlas before suspend.");
                Check(atlas.width == 1024, "The fixture must stay on the small Low atlas, got " + atlas.width);
                IDictionary renderers = (IDictionary)ownerType.GetField("_renderers", Instance).GetValue(owner);
                IDictionary savedMeshes = (IDictionary)ownerType.GetField("_savedMeshes", Instance).GetValue(owner);
                Check(renderers.Count == 2 && savedMeshes.Count == 2, "The owner must hold two visible lights before suspend.");
                List<Material> rendererMaterials = new();
                List<Mesh> rendererMeshes = new();
                foreach (object renderer in renderers.Values)
                {
                    GameObject root = (GameObject)renderer.GetType().GetField("_root", Instance).GetValue(renderer);
                    rendererMaterials.AddRange(root.GetComponent<MeshRenderer>().sharedMaterials);
                    rendererMeshes.Add(root.GetComponent<MeshFilter>().sharedMesh);
                }

                ownerType.GetMethod("SetMenuActive", Instance).Invoke(owner, new object[] { false });
                Check(!atlas.IsCreated(), "Menu suspend must release the reflection atlas RenderTexture.");
                Check((bool)ownerType.GetField("_canCreate", Instance).GetValue(owner) == false, "Suspend must stop creation.");
                Check(ownerType.GetField("_reflectionCache", Instance).GetValue(owner) == null, "Suspend must drop the reflection cache.");
                Check(renderers.Count == 0, "Suspend must dispose every menu light renderer.");
                Check(savedMeshes.Count == 0, "Suspend must drop saved mesh caches.");
                Check(ownerType.GetField("_shader", Instance).GetValue(owner) == null, "Suspend must drop the renderer shader reference.");
                Check(Shader.GetGlobalFloat("_FerrofluidMenuLightCount") == 0f, "Suspend must clear the published light count.");
                Check(Shader.GetGlobalTexture("_FerrofluidMenuLightReflections") == null, "Suspend must unbind the atlas global.");
                Check(Get(owner, "Store") != null, "Suspend must keep the saved-definition store.");
                ICollection lights = (ICollection)Get(Get(owner, "Store"), "Lights");
                Check(lights.Count == 2, "Suspend must retain both saved light definitions.");
                Check(Get(config, "ReflectionsQuality")!.ToString() == "Low", "Suspend must not touch the saved quality.");

                // A suspended quality change updates config only; it must not rebuild GPU state.
                object ultra = Enum.Parse(qualityType, "Ultra");
                ownerType.GetMethod("SetReflectionQuality", Instance).Invoke(owner, new[] { ultra });
                Check(Get(config, "ReflectionsQuality")!.ToString() == "Ultra", "A suspended Ultra choice must persist to config.");
                Check(ownerType.GetField("_reflectionCache", Instance).GetValue(owner) == null, "A suspended Ultra choice must not allocate the 16K atlas.");
                ownerType.GetMethod("SetReflectionQuality", Instance).Invoke(owner, new[] { low });
                Check(Get(config, "ReflectionsQuality")!.ToString() == "Low", "Restore the saved Low choice.");

                // Deferred destruction only becomes observable after a frame.
                yield return null;
                foreach (Material material in rendererMaterials)
                    Check(material == null, "Suspended renderer materials must be destroyed, not parked.");
                foreach (Mesh mesh in rendererMeshes)
                    Check(mesh == null, "Suspended renderer meshes must be destroyed, not parked.");

                // While suspended a quality change may update config, but must not rebuild GPU state.
                ownerType.GetMethod("SetMenuActive", Instance).Invoke(owner, new object[] { false });
                Debug.Log("[MenuResourceLifecycle] owner suspend released atlas, renderers, meshes and globals.");
            }
            finally
            {
                ((IDisposable)owner).Dispose();
            }

            IEnumerator resume = ValidateTransitionedResume(plugin, config, ownerType);
            while (resume.MoveNext())
                yield return null;

            ValidateCoreSessions();

            string packPath = FerrofluidValidationRuntime.ReflectionPackPath ?? string.Empty;
            if (File.Exists(packPath))
            {
                IEnumerator pending = ValidatePendingPackLoad(packPath);
                while (pending.MoveNext())
                    yield return null;
            }
            else
            {
                Debug.LogWarning("[MenuResourceLifecycle] CHROMAGLS_REFLECTION_PACK unset; in-flight pack section skipped.");
            }

            Debug.Log("[MenuResourceLifecycle] PASS: menu exit releases and return recreates GPU/menu resources.");

            object Definition(string id, string shape, Color color)
            {
                object definition = Activator.CreateInstance(definitionType, true);
                Set(definition, "Id", id);
                Set(definition, "Type", Enum.Parse(plugin.GetType("ChromaGLS.Settings.MenuLightType", true), shape));
                object value = settingsType.GetField("Default", Static).GetValue(null);
                foreach ((string field, float amount) in new[]
                {
                    ("Brightness", 0.6f), ("Red", color.r), ("Green", color.g), ("Blue", color.b),
                    ("BloomWidthScale", 0f), ("BloomBrightness", 0f),
                })
                {
                    value = settingsType.GetMethod("With" + field, Instance).Invoke(value, new object[] { amount });
                }

                Set(definition, "Appearance", value);
                return definition;
            }

            object BuildMeshes(object definition)
            {
                object[] arguments = { enclosure, Vector3.zero, Vector3.forward, definition, null };
                bool valid = (bool)plugin.GetType("ChromaGLS.Settings.MenuLightGeometry", true).GetMethod("TryCreate", Static).Invoke(null, arguments);
                Check(valid, "The test light must fit the real enclosure.");
                return arguments[4];
            }
        }

        private static IEnumerator ValidateTransitionedResume(Assembly plugin, object config, Type ownerType)
        {
            Type lifecycleType = plugin.GetType("ChromaGLS.Settings.FerrofluidReflectionLifecycle", true);
            Type gsmType = lifecycleType.GetConstructors()[0].GetParameters()[0].ParameterType;
            FieldInfo transitionField = gsmType.GetField("_currentSceneTransitionType", Instance);
            Check(transitionField != null, "The fixture needs the private transition field.");
            PropertyInfo inTransition = gsmType.GetProperty("isInTransition", Instance);
            Check(inTransition != null, "The fixture needs isInTransition.");
            object owner = Activator.CreateInstance(ownerType, Instance, null, new[] { config }, null);
            GameObject gsmObject = new("GSM fixture");
            Component gsm = gsmObject.AddComponent(gsmType);
            Scene mainMenuScene = default;
            Scene gameCoreScene = default;
            object lifecycle = null;
            try
            {
                mainMenuScene = UnityEngine.SceneManagement.SceneManager.CreateScene("MainMenu");
                gameCoreScene = UnityEngine.SceneManagement.SceneManager.CreateScene("GameCore");
                lifecycle = Activator.CreateInstance(lifecycleType, new object[] { gsm, owner });
                transitionField.SetValue(gsm, Enum.Parse(transitionField.FieldType, "Activate"));
                Check((bool)inTransition.GetValue(gsm), "The fixture must hold the transition active.");
                lifecycleType.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.Public).Invoke(lifecycle, null);
                FieldInfo suspended = lifecycleType.GetField("_menuSuspended", Instance);
                MethodInfo suspend = lifecycleType.GetMethod("SuspendMenu", Instance);
                MethodInfo activeChanged = lifecycleType.GetMethod("HandleActiveSceneChanged", Instance);
                Check(suspended != null && suspend != null && activeChanged != null, "The orchestrator must expose its suspend/resume state.");

                // Force the suspended state a game session leaves behind, then fire the
                // MainMenu activation while the transition is still running.
                suspend.Invoke(lifecycle, null);
                Check((bool)suspended.GetValue(lifecycle), "The fixture must start from the suspended state.");
                activeChanged.Invoke(lifecycle, new object[] { gameCoreScene, mainMenuScene });
                Check((bool)suspended.GetValue(lifecycle), "Menu entry must stay suspended while the transition runs.");
                Check((bool)ownerType.GetField("_menuActive", Instance).GetValue(owner) == false, "The owner must stay inactive during the transition.");
                MethodInfo tryGet = plugin.GetType("ChromaGLS.Settings.FerrofluidGpuResources", true).GetMethod("TryGet", Static);
                object[] probe = { null };
                Check(!(bool)tryGet.Invoke(null, probe), "Core resources must stay unloaded during the transition.");
                yield return null;
                yield return null;
                Check((bool)suspended.GetValue(lifecycle), "Frames must not resume while the transition runs.");

                // A game entry before the resume completes cancels the pending rebuild.
                activeChanged.Invoke(lifecycle, new object[] { mainMenuScene, gameCoreScene });
                yield return null;
                Check((bool)suspended.GetValue(lifecycle), "A game entry during resume must stay suspended.");
                object[] stillOut = { null };
                Check(!(bool)tryGet.Invoke(null, stillOut), "The cancelled resume must not resurrect core resources.");

                // A later menu entry queues a fresh resume that completes when the transition ends.
                activeChanged.Invoke(lifecycle, new object[] { gameCoreScene, mainMenuScene });
                yield return null;
                Check((bool)suspended.GetValue(lifecycle), "The re-queued resume must still wait for the transition.");
                transitionField.SetValue(gsm, Enum.Parse(transitionField.FieldType, "None"));
                yield return null;
                yield return null;
                yield return null;
                Check(!(bool)suspended.GetValue(lifecycle), "The menu must resume once the transition completes.");
                object[] restored = { null };
                Check((bool)tryGet.Invoke(null, restored) && restored[0] != null, "Core resources must reload after the delayed resume.");
                Check((bool)ownerType.GetField("_menuActive", Instance).GetValue(owner), "The owner must be marked active after resume.");
                Debug.Log("[MenuResourceLifecycle] delayed resume waited for transition end; game entry cancelled it cleanly.");
            }
            finally
            {
                if (lifecycle != null)
                    ((IDisposable)lifecycle).Dispose();
                if (gameCoreScene.IsValid())
                    UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(gameCoreScene);
                if (mainMenuScene.IsValid())
                    UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(mainMenuScene);
                Object.DestroyImmediate(gsmObject);
            }
        }

        private static void ValidateCoreSessions()
        {
            Type resourcesType = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                resourcesType = assembly.GetType("ChromaGLS.Settings.FerrofluidGpuResources");
                if (resourcesType != null)
                    break;
            }

            Check(resourcesType != null, "FerrofluidGpuResources must be resolvable.");
            MethodInfo tryGet = resourcesType.GetMethod("TryGet", Static);
            MethodInfo end = resourcesType.GetMethod("EndMenuSession", Static);
            MethodInfo begin = resourcesType.GetMethod("BeginMenuSession", Static);
            Check(end != null && begin != null, "The core resources must expose menu session endpoints.");
            begin.Invoke(null, null);
            object[] arguments = { null };
            Check((bool)tryGet.Invoke(null, arguments), "Core resources must load for the menu session.");
            object resources = arguments[0];
            Shader simulation = (Shader)Get(resources, "SimulationShader");
            Shader surface = (Shader)Get(resources, "SurfaceShader");
            Texture2D lut = (Texture2D)Get(resources, "ThinFilmTexture");
            Texture2D environment = (Texture2D)Get(resources, "EnvironmentTexture");
            Check(simulation != null && surface != null && lut != null && environment != null, "Core assets must load.");
            object high = Enum.Parse(resourcesType.Assembly.GetType("ChromaGLS.Settings.ReflectionQuality", true), "High");

            end.Invoke(null, null);
            object[] probe = { null };
            Check(!(bool)tryGet.Invoke(null, probe) && probe[0] == null, "TryGet must not recreate resources while suspended.");
            Check(simulation == null && surface == null && lut == null && environment == null,
                "The retained core bundle must unload every loaded shader and texture on menu exit.");
            Task<bool> stale = (Task<bool>)resourcesType.GetMethod("TryLoadReflectionsAsync", Instance).Invoke(resources, new[] { high });
            Check(stale.Result == false, "A released core instance must refuse new pack loads.");
            Check(resourcesType.GetProperty("ReflectionPackPath", Static) != null, "ReflectionPackPath must remain a static property.");

            begin.Invoke(null, null);
            object[] restored = { null };
            Check((bool)tryGet.Invoke(null, restored), "Menu return must reload core resources.");
            Check(!ReferenceEquals(restored[0], resources), "Menu return must construct a fresh resource owner.");
            Check((Shader)Get(restored[0], "SimulationShader") != null, "The fresh owner must expose loaded shaders.");

            for (int cycle = 0; cycle < 3; cycle++)
            {
                end.Invoke(null, null);
                object[] suspended = { null };
                Check(!(bool)tryGet.Invoke(null, suspended), "Suspended cycle " + cycle + " must not recreate resources.");
                begin.Invoke(null, null);
                object[] again = { null };
                Check((bool)tryGet.Invoke(null, again), "Cycle " + cycle + " must reload core resources.");
            }

            Debug.Log("[MenuResourceLifecycle] core session End/Begin unloads shaders+textures and reloads cleanly across 3 cycles.");
        }

        private static IEnumerator ValidatePendingPackLoad(string packPath)
        {
            Type resourcesType = null;
            Type qualityType = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                resourcesType ??= assembly.GetType("ChromaGLS.Settings.FerrofluidGpuResources");
                qualityType ??= assembly.GetType("ChromaGLS.Settings.ReflectionQuality");
            }

            resourcesType.GetMethod("ConfigureReflectionPack", Static).Invoke(null, new object[] { packPath });
            object[] arguments = { null };
            Check((bool)resourcesType.GetMethod("TryGet", Static).Invoke(null, arguments), "Core resources must load before the pending-pack check.");
            object resources = arguments[0];
            MethodInfo loadAsync = resourcesType.GetMethod("TryLoadReflectionsAsync", Instance);
            HashSet<int> baselineBundles = GetBundleIds();
            Task<bool> pending = (Task<bool>)loadAsync.Invoke(resources, new[] { Enum.Parse(qualityType, "High") });
            if (pending.IsCompleted)
                throw new InvalidOperationException("The real pack load must be pending when the menu session ends.");

            resourcesType.GetMethod("EndMenuSession", Static).Invoke(null, null);
            while (!pending.IsCompleted)
                yield return null;
            Check(pending.Result == false, "A pack load superseded by menu exit must not publish textures.");
            Check(Get(resources, "RoomEnvironmentOn") == null && Get(resources, "FloorReflectionTexture") == null,
                "A superseded pack load must leave the released owner empty.");
            HashSet<int> remaining = GetBundleIds();
            remaining.ExceptWith(baselineBundles);
            Check(remaining.Count == 0, "Menu exit during a pack load must not leak a native AssetBundle.");
            Check((string)resourcesType.GetProperty("ReflectionPackPath", Static).GetValue(null) == packPath,
                "Menu exit must preserve the configured pack path.");
            resourcesType.GetMethod("BeginMenuSession", Static).Invoke(null, null);
            Debug.Log("[MenuResourceLifecycle] in-flight pack load superseded by menu exit resolved false with no leaked bundle.");
        }

        private static HashSet<int> GetBundleIds()
        {
            HashSet<int> ids = new();
            foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
                ids.Add(bundle.GetInstanceID());
            return ids;
        }

        private static object Get(object target, string property) => target.GetType().GetProperty(property, Instance).GetValue(target);

        private static void Set(object target, string property, object value) => target.GetType().GetProperty(property, Instance).SetValue(target, value);

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
