namespace ChromaGLS.FerrofluidBundle
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Runtime.Serialization;
    using UnityEditor;
    using UnityEngine;

    // Held-grab target ownership: the manager-sampled canonical target feeds the joint anchor and
    // the GPU magnet uniform through the same value, so render cadence and physics cadence agree.
    internal static class FerrofluidTrackingValidation
    {
        private static ResolveEventHandler _resolver = null!;

        public static void Run()
        {
            try
            {
                Execute();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Exception inner = exception is TargetInvocationException && exception.InnerException != null
                    ? exception.InnerException
                    : exception;
                Debug.LogError($"[FerrofluidTrackingValidation] {inner.GetType().Name}: {inner.Message}\n{inner.StackTrace}");
                EditorApplication.Exit(1);
            }
        }

        private static void Execute()
        {
            List<string> failures = new();
            List<UnityEngine.Object> resources = new();
            SimulationMode previousMode = Physics.simulationMode;
            float previousDelta = Time.fixedDeltaTime;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                string repository = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
                _resolver = (_, args) =>
                {
                    string managed = Path.Combine(repository, "ChromaGLS.Tests/bin/Release-1.44.1/net48", new AssemblyName(args.Name).Name + ".dll");
                    return File.Exists(managed) ? Assembly.LoadFrom(managed) : null;
                };
                AppDomain.CurrentDomain.AssemblyResolve += _resolver;
                Assembly plugin = Assembly.LoadFrom(Path.Combine(repository, "ChromaGLS/bin/Release-1.44.1/net48/ChromaGLS.dll"));
                BindingFlags inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                Type grabbable = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true);
                Type enclosure = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true);
                Type effectType = plugin.GetType("ChromaGLS.Settings.FerrofluidPrimitiveEffect", true);
                Type simulationType = plugin.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", true);
                Type configType = plugin.GetType("ChromaGLS.Settings.Config", true);
                Type controllerType = plugin.GetType("ChromaGLS.Settings.FerrofluidMagnetController", true);
                Type shapeType = plugin.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", true);
                object enclosureValue = enclosure.GetConstructor(
                        BindingFlags.Instance | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float) },
                        null)!
                    .Invoke(new object[] { Vector3.zero, 100f, new Vector3(0f, 0f, -100f), Vector3.forward, 0f });
                MethodInfo initialize = grabbable.GetMethod("Initialize", inst)!;
                MethodInfo acquire = grabbable.GetMethod("Acquire", inst)!;
                MethodInfo release = grabbable.GetMethod("Release", inst)!;
                MethodInfo advancePhysics = grabbable.GetMethod("AdvancePhysics", inst)!;
                MethodInfo sampleGrabTarget = grabbable.GetMethod("SampleGrabTarget", inst)!;
                PropertyInfo grabTarget = grabbable.GetProperty("GrabTarget", inst)!;
                PropertyInfo isHeld = grabbable.GetProperty("IsHeld", inst)!;
                MethodInfo lateUpdate = effectType.GetMethod("LateUpdate", inst)!;
                FieldInfo effectLastUpdate = effectType.GetField("_lastUpdateAt", inst)!;
                FieldInfo effectMagneticField = effectType.GetField("_magneticFieldPosition", inst)!;
                FieldInfo simSurface = simulationType.GetField("_surfaceMaterial", inst)!;
                PropertyInfo effectGrabbable = effectType.GetProperty("Grabbable", inst)!;
                object ownership = Activator.CreateInstance(plugin.GetType("ChromaGLS.Settings.FerrofluidGrabOwnership", true), nonPublic: true);
                object config = Activator.CreateInstance(configType, nonPublic: true);
                // The controller's VRUI fields cannot load in the editor, so allocate it raw: every
                // query short-circuits on _hasControllers == false before touching those fields.
                object magnetController = FormatterServices.GetUninitializedObject(controllerType);
                PhysicsMaterial material = new() { dynamicFriction = 0.6f, staticFriction = 0.6f, bounciness = 0.3f };
                resources.Add(material);

                foreach (bool sphere in new[] { false, true })
                {
                    foreach (float renderHz in new[] { 90f, 120f, 144f, 180f })
                    {
                        foreach (float physicsHz in new[] { 50f, 100f })
                        {
                            foreach (float phase in new[] { 0f, 0.37f })
                            {
                                foreach (float speed in new[] { 0.2f, 1f, 3f })
                                {
                                    RunCase(
                                        failures,
                                        resources,
                                        sphere,
                                        renderHz,
                                        physicsHz,
                                        phase,
                                        speed,
                                        grabbable,
                                        effectType,
                                        simulationType,
                                        shapeType,
                                        enclosureValue,
                                        initialize,
                                        acquire,
                                        release,
                                        advancePhysics,
                                        sampleGrabTarget,
                                        grabTarget,
                                        isHeld,
                                        lateUpdate,
                                        effectLastUpdate,
                                        effectMagneticField,
                                        simSurface,
                                        effectGrabbable,
                                        ownership,
                                        config,
                                        magnetController,
                                        material);
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= _resolver;
                Physics.simulationMode = previousMode;
                Time.fixedDeltaTime = previousDelta;
                foreach (UnityEngine.Object resource in resources)
                {
                    if (resource != null)
                        UnityEngine.Object.DestroyImmediate(resource);
                }
            }

            if (failures.Count > 0)
                throw new Exception($"{failures.Count} tracking cases failed: {string.Join(" | ", failures)}");
                Debug.Log("[FerrofluidTrackingValidation] PASS: shared held target, GPU uniform ownership, and resampled target lag verified.");
        }

        private static void RunCase(
            List<string> failures,
            List<UnityEngine.Object> resources,
            bool sphere,
            float renderHz,
            float physicsHz,
            float phase,
            float speed,
            Type grabbable,
            Type effectType,
            Type simulationType,
            Type shapeType,
            object enclosureValue,
            MethodInfo initialize,
            MethodInfo acquire,
            MethodInfo release,
            MethodInfo advancePhysics,
            MethodInfo sampleGrabTarget,
            PropertyInfo grabTarget,
            PropertyInfo isHeld,
            MethodInfo lateUpdate,
            FieldInfo effectLastUpdate,
            FieldInfo effectMagneticField,
            FieldInfo simSurface,
            PropertyInfo effectGrabbable,
            object ownership,
            object config,
            object magnetController,
            PhysicsMaterial material)
        {
            Component effect = null;
            string label = $"{(sphere ? "sphere" : "cube")} render{renderHz:F0}/physics{physicsHz:F0} phase{phase:F2} {speed:F1}m/s";
            float renderDt = 1f / renderHz;
            float fixedDt = 1f / physicsHz;
            Time.fixedDeltaTime = fixedDt;
            MethodInfo evaluateSourceScale = grabbable.Assembly
                .GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true)!
                .GetMethod("EvaluateSourceWorldScale", BindingFlags.Static | BindingFlags.NonPublic)!;

            GameObject body = new($"{label} body");
            resources.Add(body);
            GameObject effectObject = new($"{label} effect", typeof(MeshFilter), typeof(MeshRenderer));
            resources.Add(effectObject);
            GameObject attach = new($"{label} attach");
            resources.Add(attach);
            try
            {
                body.transform.localScale = Vector3.one * 0.55f;
                body.transform.position = new Vector3(0f, 3f, 0f);
                effectObject.transform.SetParent(body.transform, false);
                Component comp = body.AddComponent(grabbable);
                initialize.Invoke(comp, new object[] { enclosureValue, sphere, material, ownership });
                Rigidbody rb = body.GetComponent<Rigidbody>();
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.PublishTransform();
                Physics.SyncTransforms();

                // Held top-face click 3m in the air, far from the far enclosure walls.
                Vector3 topPoint = body.transform.position + new Vector3(0f, 0.275f, 0f);
                attach.transform.position = topPoint;
                acquire.Invoke(comp, new object[] { attach.transform, topPoint });
                if (!(bool)isHeld.GetValue(comp))
                {
                    failures.Add($"{label}: acquire must leave the body held");
                    return;
                }

                if (rb.interpolation != RigidbodyInterpolation.Interpolate)
                    failures.Add($"{label}: held body must interpolate, got {rb.interpolation}");

                effect = effectObject.AddComponent(effectType);
                effectGrabbable.SetValue(effect, comp);
                Set(effectType, effect, "_shape", Enum.Parse(shapeType, sphere ? "Sphere" : "RoundedCube"));
                Set(effectType, effect, "_roundness", 0.48f);
                Set(effectType, effect, "_chartYawDegrees", 0f);
                Set(effectType, effect, "_magnetSurfaceGap", 0.1f);
                Set(effectType, effect, "_sourceWorldScale", 0.55f);
                Set(effectType, effect, "_magnetController", magnetController);
                Set(effectType, effect, "_magneticFieldPosition", topPoint);
                Set(effectType, effect, "_magnetTarget", topPoint);
                Set(effectType, effect, "_lastUpdateAt", Time.unscaledTime);

                object simulation = FormatterServices.GetUninitializedObject(simulationType);
                Material simMaterial = new(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSimulation.shader"));
                Material surfaceMaterial = new(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader"));
                resources.Add(simMaterial);
                resources.Add(surfaceMaterial);
                RenderTexture siteDeformation = new(4, 4, 0, RenderTextureFormat.ARGBFloat);
                RenderTexture siteBulge = new(4, 4, 0, RenderTextureFormat.ARGBFloat);
                siteDeformation.Create();
                siteBulge.Create();
                resources.Add(siteDeformation);
                resources.Add(siteBulge);
                Set(simulationType, simulation, "_renderer", effectObject.GetComponent<MeshRenderer>());
                Set(simulationType, simulation, "_shape", Enum.Parse(shapeType, sphere ? "Sphere" : "RoundedCube"));
                Set(simulationType, simulation, "_simulationMaterial", simMaterial);
                Set(simulationType, simulation, "_surfaceMaterial", surfaceMaterial);
                Set(simulationType, simulation, "_config", config);
                Set(simulationType, simulation, "_renderEnabled", true);
                Set(simulationType, simulation, "_lastSimulationAt", Time.unscaledTime);
                Set(simulationType, simulation, "_simulationAccumulator", -100f);
                Set(simulationType, simulation, "_siteDeformation", siteDeformation);
                Set(simulationType, simulation, "_siteBulge", siteBulge);
                Set(effectType, effect, "_simulation", simulation);

                float startX = attach.transform.position.x;
                double warmup = 3.0;
                double measure = 2.0;
                double nextFixed = fixedDt * (1.0 - phase);
                double lastFixed = -phase * (double)fixedDt;
                Vector3 lastCanonical = (Vector3)grabTarget.GetValue(comp);
                float lagMin = float.MaxValue;
                float lagMax = float.MinValue;
                float lagSum = 0f;
                int lagSamples = 0;
                int frames = Mathf.RoundToInt((float)(warmup + measure) * renderHz);
                for (int frame = 0; frame < frames; frame++)
                {
                    double renderTime = (frame + 1) * (double)renderDt;
                    while (nextFixed <= renderTime + 0.000000001)
                    {
                        advancePhysics.Invoke(comp, new object[] { fixedDt, (float)nextFixed });
                        Physics.Simulate(fixedDt);
                        Vector3 afterTick = (Vector3)grabTarget.GetValue(comp);
                        if ((afterTick - lastCanonical).sqrMagnitude > 0.0000000001f)
                            failures.Add($"{label} frame{frame}: GrabTarget moved across a FixedUpdate ({lastCanonical.x:F4} -> {afterTick.x:F4})");
                        lastFixed = nextFixed;
                        nextFixed += fixedDt;
                    }

                    attach.transform.position = new Vector3(startX + (speed * (float)renderTime), attach.transform.position.y, attach.transform.position.z);
                    sampleGrabTarget.Invoke(comp, new object[] { renderDt, (float)renderTime });
                    lastCanonical = (Vector3)grabTarget.GetValue(comp);
                    effectLastUpdate.SetValue(effect, Time.unscaledTime - renderDt);
                    lateUpdate.Invoke(effect, null);
                    Vector3 uploaded = ((Material)simSurface.GetValue(simulation)).GetVector("_MagneticFieldPosition");
                    Vector3 capturedPoint = body.transform.TransformPoint(new Vector3(0f, 0.5f, 0f));
                    float sourceScale = (float)evaluateSourceScale.Invoke(null, new object[] { body.transform })!;
                    Vector3 expectedField = capturedPoint + (body.transform.up * (0.1f * sourceScale));
                    if ((uploaded - expectedField).sqrMagnitude > 0.0000000001f)
                        failures.Add($"{label} frame{frame}: field must follow captured rendered point plus outward gap ({expectedField} != {uploaded})");
                    float uploadedStrength = ((Material)simSurface.GetValue(simulation)).GetFloat("_MagneticFieldStrength");
                    if (frame > 5 && uploadedStrength < 0.2f)
                        failures.Add($"{label} frame{frame}: held magnet strength must stay engaged without a ray ({uploadedStrength:F3})");

                    if (renderTime > warmup)
                    {
                        // Mathematically extrapolated clicked point: the rigidbody pose advanced
                        // by its own velocities over the un-simulated remainder — not an
                        // engine-rendered transform.
                        float remainder = (float)(renderTime - lastFixed);
                        if (remainder < -0.000001f || remainder > fixedDt + 0.000001f)
                            failures.Add($"{label} frame{frame}: remainder {remainder:F6} outside [0,{fixedDt:F4}]");
                        Vector3 omega = rb.angularVelocity;
                        Quaternion predictedRotation = omega.sqrMagnitude > 0.0000000001f
                            ? Quaternion.AngleAxis(omega.magnitude * Mathf.Rad2Deg * remainder, omega.normalized) * rb.rotation
                            : rb.rotation;
                        Vector3 predictedClicked = rb.worldCenterOfMass
                            + (rb.linearVelocity * remainder)
                            + (predictedRotation * (new Vector3(0f, 0.275f, 0f) - rb.centerOfMass));
                        float lag = attach.transform.position.x - predictedClicked.x;
                        lagMin = Mathf.Min(lagMin, lag);
                        lagMax = Mathf.Max(lagMax, lag);
                        lagSum += lag;
                        lagSamples++;
                    }
                }

                float lagMean = lagSamples > 0 ? lagSum / lagSamples : 0f;
                Debug.Log($"[FerrofluidTrackingValidation] {label} lag min={lagMin:F4} max={lagMax:F4} mean={lagMean:F4}");
                if (lagSamples > 0 && (lagMax - lagMin) > (0.1f * Mathf.Abs(lagMean)) + 0.002f)
                    failures.Add($"{label}: extrapolated clicked-point lag varies {lagMin:F4}..{lagMax:F4} (mean {lagMean:F4})");
                release.Invoke(comp, null);
                if (rb.interpolation != RigidbodyInterpolation.Interpolate)
                    failures.Add($"{label}: released body must return to Interpolate, got {rb.interpolation}");
            }
            catch (Exception exception)
            {
                Exception inner = exception is TargetInvocationException && exception.InnerException != null
                    ? exception.InnerException
                    : exception;
                failures.Add($"{label}: {inner.GetType().Name} {inner.Message.Split('\n')[0]} {inner.StackTrace?.Split('\n')[0]}");
            }
            finally
            {
                if (effect != null)
                    effectType.GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(effect, null);
                // Earlier cases must not collide with later sweeps at the same track.
                body.SetActive(false);
                effectObject.SetActive(false);
                attach.SetActive(false);
            }
        }

        private static void Set(Type type, object instance, string name, object value)
        {
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
        }
    }
}
