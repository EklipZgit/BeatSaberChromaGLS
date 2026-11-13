using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // The held magnet must ride the captured object-local anchor under the rendered pose, not the
    // wanted cursor: grabbing a sphere's X pole or a cube face centre keeps the field outside the
    // surface along the local outward normal even when the cursor is occluded or inside the body.
    internal static class FerrofluidHeldMagnetValidation
    {
        private const float FixedDelta = 0.02f;
        private static ResolveEventHandler _resolver = null!;

        private static void Set(Type type, object instance, string name, object value) =>
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

        public static void Run()
        {
            List<string> failures = new();
            SimulationMode previousMode = Physics.simulationMode;
            float previousDelta = Time.fixedDeltaTime;
            Physics.simulationMode = SimulationMode.Script;
            Time.fixedDeltaTime = FixedDelta;
            List<Object> resources = new();
            List<Component> effects = new();
            Type effectType = null!;
            _resolver = (_, args) =>
            {
                string managed = Path.Combine(
                    Directory.GetParent(Application.dataPath)!.Parent!.FullName,
                    "ChromaGLS.Tests/bin/Release-1.44.1/net48",
                    new System.Reflection.AssemblyName(args.Name).Name + ".dll");
                return File.Exists(managed) ? System.Reflection.Assembly.LoadFrom(managed) : null;
            };
            AppDomain.CurrentDomain.AssemblyResolve += _resolver;
            try
            {
                string repository = Directory.GetParent(Application.dataPath)!.Parent!.FullName;
                Assembly plugin = Assembly.LoadFrom(Path.Combine(repository, "ChromaGLS/bin/Release-1.44.1/net48/ChromaGLS.dll"));
                BindingFlags inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                Type grabbableType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true)!;
                effectType = plugin.GetType("ChromaGLS.Settings.FerrofluidPrimitiveEffect", true)!;
                Type simulationType = plugin.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", true)!;
                Type controllerType = plugin.GetType("ChromaGLS.Settings.FerrofluidMagnetController", true)!;
                Type shapeType = plugin.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", true)!;
                Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true)!;
                Type environmentType = plugin.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true)!;
                MethodInfo acquire = grabbableType.GetMethod("Acquire", inst)!;
                MethodInfo sampleGrab = grabbableType.GetMethod("SampleGrabTarget", inst)!;
                MethodInfo advancePhysics = grabbableType.GetMethod("AdvancePhysics", inst)!;
                FieldInfo localAnchor = grabbableType.GetField("_bodyAnchor", inst)!;
                MethodInfo lateUpdate = effectType.GetMethod("LateUpdate", inst)!;
                object enclosure = enclosureType.GetConstructor(
                        inst,
                        null,
                        new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                        null)!
                    .Invoke(new object[] { Vector3.zero, 3f, new Vector3(0f, 0f, -2f), Vector3.forward, 0f });
                object ownership = Activator.CreateInstance(plugin.GetType("ChromaGLS.Settings.FerrofluidGrabOwnership", true)!, nonPublic: true)!;
                object controller = FormatterServices.GetUninitializedObject(controllerType);
                PhysicsMaterial material = new() { dynamicFriction = 0.4f, staticFriction = 0.4f, bounciness = 0.3f };
                resources.Add(material);
                MethodInfo normalHelper = environmentType.GetMethod("EvaluateSurfaceNormalFromDirection", BindingFlags.Static | BindingFlags.NonPublic)!;
                MethodInfo scaleHelper = environmentType.GetMethod("EvaluateSourceWorldScale", BindingFlags.Static | BindingFlags.NonPublic)!;
                MethodInfo surfaceHelper = environmentType.GetMethod("EvaluateSurfacePositionAndNormal", BindingFlags.Static | BindingFlags.NonPublic)!;
                Type configType = plugin.GetType("ChromaGLS.Settings.Config", true)!;
                object config = Activator.CreateInstance(configType, nonPublic: true)!;

                foreach ((bool sphere, string label) in new[] { (true, "sphere"), (false, "cube") })
                {
                    foreach (Vector3 direction in HeldAnchors(sphere))
                    {
                        object shape = Enum.Parse(shapeType, sphere ? "Sphere" : "RoundedCube");
                        object[] surfaceArguments = { shape, direction, 0.48f, Vector3.zero, Vector3.zero };
                        surfaceHelper.Invoke(null, surfaceArguments);
                        Vector3 anchor = (Vector3)surfaceArguments[3];
                        GameObject body = new($"{label} held body");
                        GameObject attach = new($"{label} attach");
                        resources.Add(body);
                        resources.Add(attach);
                        body.transform.localScale = Vector3.one * 0.55f;
                        Component comp = body.AddComponent(grabbableType);
                        MethodInfo init = grabbableType.GetMethod("Initialize", inst)!;
                        init.Invoke(comp, new object[] { enclosure, sphere, material, ownership });
                        Rigidbody rb = comp.GetComponent<Rigidbody>();
                        rb.position = new Vector3(0f, 1.5f, 0f);
                        rb.PublishTransform();
                        Physics.SyncTransforms();
                        Vector3 worldAnchor = body.transform.TransformPoint(anchor);
                        attach.transform.position = worldAnchor;
                        acquire.Invoke(comp, new object[] { attach.transform, worldAnchor });
                        sampleGrab.Invoke(comp, new object[] { FixedDelta, Time.unscaledTime });
                        advancePhysics.Invoke(comp, new object[] { FixedDelta, Time.unscaledTime });

                        // The effect reads the rendered pose on a sibling whose world matrix matches the body.
                        GameObject effectObject = new($"{label} effect", typeof(MeshFilter), typeof(MeshRenderer));
                        resources.Add(effectObject);
                        effectObject.transform.SetParent(body.transform, false);
                        effectObject.transform.localPosition = Vector3.zero;
                        effectObject.transform.localRotation = Quaternion.identity;
                        effectObject.transform.localScale = Vector3.one;
                        Component effect = effectObject.AddComponent(effectType);
                        effects.Add(effect);
                        effectType.GetProperty("Grabbable", inst)!.SetValue(effect, comp);
                        Set(effectType, effect, "_shape", Enum.Parse(shapeType, sphere ? "Sphere" : "RoundedCube"));
                        Set(effectType, effect, "_roundness", 0.48f);
                        Set(effectType, effect, "_magnetController", controller);
                        Set(effectType, effect, "_magnetSurfaceGap", 0.1f);
                        Set(effectType, effect, "_magneticFieldStrength", 1f);
                        object simulation = FormatterServices.GetUninitializedObject(simulationType);
                        Material simMaterial = new(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader"));
                        Material fluidMaterial = new(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSimulation.shader"));
                        resources.Add(simMaterial);
                        resources.Add(fluidMaterial);
                        Set(simulationType, simulation, "_renderer", effectObject.GetComponent<MeshRenderer>());
                        Set(simulationType, simulation, "_simulationMaterial", fluidMaterial);
                        Set(simulationType, simulation, "_surfaceMaterial", simMaterial);
                        Set(simulationType, simulation, "_shape", shape);
                        Set(simulationType, simulation, "_config", config);
                        Set(simulationType, simulation, "_renderEnabled", true);
                        Set(simulationType, simulation, "_lastSimulationAt", Time.unscaledTime);
                        Set(simulationType, simulation, "_simulationAccumulator", -100f);
                        foreach (string textureField in new[] { "_siteDeformation", "_siteBulge" })
                        {
                            RenderTexture texture = new(4, 4, 0, RenderTextureFormat.ARGBFloat);
                            texture.Create();
                            resources.Add(texture);
                            Set(simulationType, simulation, textureField, texture);
                        }

                        Set(effectType, effect, "_simulation", simulation);
                        Set(effectType, effect, "_lastUpdateAt", Time.unscaledTime);

                        foreach (int pose in new[] { 0, 1, 2 })
                        {
                            if (pose == 1)
                            {
                                rb.position = new Vector3(0.3f, 1.2f, -0.2f);
                                rb.rotation = Quaternion.Euler(25f, 61f, -32f);
                                rb.PublishTransform();
                            }
                            else if (pose == 2)
                            {
                                // Keep the body pose while the desired cursor moves somewhere it cannot follow.
                                attach.transform.position += new Vector3(0.5f, -4f, -2f);
                                sampleGrab.Invoke(comp, new object[] { FixedDelta, Time.unscaledTime });
                            }

                            float neutralHeight = 0f;
                            foreach (float amplification in new[] { 1f, 1.6f })
                            {
                                grabbableType.GetMethod("SetMagnetHeightAmplification", inst)!
                                    .Invoke(comp, new object[] { amplification });
                                lateUpdate.Invoke(effect, null);
                                Vector3 uploaded = simMaterial.GetVector("_MagneticFieldPosition");
                                Vector3 captured = (Vector3)localAnchor.GetValue(comp)!;
                                Vector3 surfaceWorld = body.transform.TransformPoint(captured);
                                Vector3 normalLocal = (Vector3)normalHelper.Invoke(null, new object[] { shape, captured, 0.48f })!;
                                Vector3 worldNormal = body.transform.TransformDirection(normalLocal).normalized;
                                float scale = (float)scaleHelper.Invoke(null, new object[] { body.transform })!;
                                Vector3 expected = surfaceWorld + (worldNormal * (0.1f * scale));
                                string context = $"{label} anchor {anchor} pose{pose} amp{amplification}";
                                if ((uploaded - expected).sqrMagnitude > 0.0000000001f)
                                    failures.Add($"{context}: field must follow captured surface plus outward gap ({expected} != {uploaded})");

                                Vector3 uploadedSurface = fluidMaterial.GetVector("_PrimitiveMagnetPosition");
                                if ((uploadedSurface - captured).sqrMagnitude > 0.0000000001f)
                                    failures.Add($"{context}: sliding field must retain the captured local anchor");

                                float alongNormal = Vector3.Dot(uploaded - surfaceWorld, worldNormal);
                                if (alongNormal < 0.0001f)
                                    failures.Add($"{context}: field must sit outside the surface (along={alongNormal:F4})");

                                if (simMaterial.GetFloat("_MagneticFieldStrength") < 0.9f)
                                    failures.Add($"{context}: held magnet must stay engaged without a ray");

                                float height = ReadFrontHeight(simMaterial, anchor, sphere);
                                if (height <= 0.441f)
                                    failures.Add($"{context}: held front spike must grow above rest, got {height:F5}");

                                if (amplification == 1f)
                                    neutralHeight = height;
                                else if (height < neutralHeight + 0.1f)
                                    failures.Add($"{context}: trigger must further raise the held front spike ({neutralHeight:F5}->{height:F5})");
                            }
                        }

                        Set(effectType, effect, "_simulation", null!);
                        grabbableType.GetMethod("Release", inst)!.Invoke(comp, null);
                        body.SetActive(false);
                        attach.SetActive(false);
                    }
                }
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= _resolver;
                Physics.simulationMode = previousMode;
                Time.fixedDeltaTime = previousDelta;
                foreach (Component effect in effects)
                {
                    if (effect != null)
                        Set(effectType, effect, "_simulation", null!);
                }

                foreach (Object resource in resources)
                {
                    if (resource != null)
                        Object.DestroyImmediate(resource);
                }
            }

            if (failures.Count > 0)
                throw new Exception($"{failures.Count} held-magnet cases failed: {string.Join(" | ", failures)}");
            Debug.Log("[FerrofluidHeldMagnetValidation] PASS: the held field follows the captured local anchor.");
        }

        private static float ReadFrontHeight(Material material, Vector3 anchor, bool sphere)
        {
            Texture2D sites = new(2, 2, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
            Texture2D baseline = new(2, 2, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
            Texture2D read = new(2, 2, TextureFormat.RGBAFloat, false, true);
            RenderTexture state = new(2, 2, 0, RenderTextureFormat.ARGBFloat) { filterMode = FilterMode.Point };
            RenderTexture output = new(2, 2, 0, RenderTextureFormat.ARGBFloat) { filterMode = FilterMode.Point };
            RenderTexture previous = RenderTexture.active;
            try
            {
                Color site = new(anchor.x, anchor.y, anchor.z, 0.1f);
                sites.SetPixels(new[] { site, site, site, site });
                sites.Apply();
                baseline.SetPixels(new[] { new Color(0.44f, 0f, 0f, 0f), new Color(0.44f, 0f, 0f, 0f), new Color(0.44f, 0f, 0f, 0f), new Color(0.44f, 0f, 0f, 0f) });
                baseline.Apply();
                material.SetTexture("_SiteSurfaceTex", sites);
                material.SetTexture("_SpringStateTex", state);
                material.SetFloat("_ParticleTextureSize", 2f);
                material.SetFloat("_ParticleCount", 4f);
                material.SetFloat("_SurfaceShapeMode", sphere ? 2f : 1f);
                material.SetFloat("_SurfaceRoundness", 0.48f);
                material.SetFloat("_SpikeSpringStrength", 100f);
                material.SetFloat("_SpikeSpringDamping", 0f);
                material.SetFloat("_DeltaTimeMilliseconds", 100f);
                material.SetFloat("_PrimitiveSpikeHeightRadius", 2f);
                material.SetFloat("_PrimitiveFlattenRadius", 1f);
                material.SetFloat("_PrimitiveFlattenExponent", 1f);
                material.SetFloat("_BackHeightFalloffExponent", 1f);
                material.SetFloat("_PeakWidth", 1f);
                material.SetFloat("_HeightFalloffExponent", 2f);
                material.SetFloat("_MagnetHeightStrengthCap", 0.8f);
                material.SetFloat("_MagnetHeightDeadZoneRadius", 0f);
                material.SetFloat("_HeightGain", 1f);
                material.SetFloat("_BackHeightLoss", 0.5f);
                material.SetFloat("_MaximumHeight", 1.37f);
                Graphics.Blit(baseline, state);
                Graphics.Blit(state, output, material, 1);
                RenderTexture.active = output;
                read.ReadPixels(new Rect(0, 0, 2, 2), 0, 0);
                read.Apply(false, false);
                return read.GetPixel(0, 0).r;
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(sites);
                Object.DestroyImmediate(baseline);
                Object.DestroyImmediate(read);
                Object.DestroyImmediate(state);
                Object.DestroyImmediate(output);
            }
        }

        private static IEnumerable<Vector3> HeldAnchors(bool sphere)
        {
            // Sphere axes and cube faces/edges/corners are object-local grab anchors.
            if (sphere)
            {
                yield return new Vector3(0.5f, 0f, 0f);
                yield return new Vector3(-0.5f, 0f, 0f);
                yield return new Vector3(0f, 0.5f, 0f);
                yield return new Vector3(0f, -0.5f, 0f);
                yield return new Vector3(0f, 0f, 0.5f);
                yield return new Vector3(0f, 0f, -0.5f);
                yield return new Vector3(0.3f, 0.3f, 0.3f).normalized * 0.5f;
            }
            else
            {
                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int z = -1; z <= 1; z++)
                        {
                            if (x != 0 || y != 0 || z != 0)
                                yield return new Vector3(x, y, z);
                        }
                    }
                }
            }
        }
    }
}
