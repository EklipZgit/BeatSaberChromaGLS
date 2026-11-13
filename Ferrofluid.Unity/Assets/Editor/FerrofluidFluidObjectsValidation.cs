#nullable enable annotations

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidFluidObjectsValidation
    {
        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        public static void Run()
            => FerrofluidValidationRunner.Run(typeof(FerrofluidFluidObjectsValidation));

        private static IEnumerator RunRuntime()
        {
            object config = FerrofluidValidationRuntime.CreateConfig();
            Assembly plugin = config.GetType().Assembly;
            Type envType = plugin.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true)!;
            Type managerType = envType.GetNestedType("FerrofluidEnvironmentManager", BindingFlags.NonPublic)!;
            Type pluginType = plugin.GetType("ChromaGLS.Plugin", true)!;
            Type effectType = plugin.GetType("ChromaGLS.Settings.FerrofluidPrimitiveEffect", true)!;
            Type grabbableType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true)!;
            Type ownershipType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabOwnership", true)!;
            Type controllerType = plugin.GetType("ChromaGLS.Settings.FerrofluidMagnetController", true)!;
            MethodInfo setCube = pluginType.GetMethod("SetFerrofluidCubeEnabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
            MethodInfo setSphere = pluginType.GetMethod("SetFerrofluidSphereEnabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
            MethodInfo setFluidCube = pluginType.GetMethod("SetFluidCubeEnabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
            MethodInfo setFluidSphere = pluginType.GetMethod("SetFluidSphereEnabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

            SimulationMode previousMode = Physics.simulationMode;
            float previousDelta = Time.fixedDeltaTime;
            RenderTexture previousTarget = RenderTexture.active;
            Physics.simulationMode = SimulationMode.Script;
            Time.fixedDeltaTime = 0.02f;
            GameObject anchor = new("fluid fixture anchor");
            GameObject managerObject = new("fluid fixture manager");
            object[] captured = new object[7];
            string[] staticNames = { "_config", "_manager", "_menuActive", "_cubeEnabled", "_sphereEnabled", "_fluidCubeEnabled", "_fluidSphereEnabled" };
            List<GameObject> ownedRoots = new();
            List<Material> ownedMaterials = new();
            Component manager = null!;
            try
            {
                for (int index = 0; index < staticNames.Length; index++)
                    captured[index] = envType.GetField(staticNames[index], BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

                envType.GetField("_config", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, config);
                managerObject.SetActive(false);
                manager = managerObject.AddComponent(managerType);
                managerObject.transform.SetParent(anchor.transform, false);
                managerType.GetField("_playerAnchor", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, anchor.transform);
                managerType.GetField("_enclosureResolved", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, true);
                Type enclosureType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true)!;
                object enclosure = Activator.CreateInstance(
                    enclosureType,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null,
                    new object[] { Vector3.zero, 4.2f, new Vector3(0f, 0f, -4.2f), Vector3.forward, 0f },
                    null)!;
                managerType.GetField("_enclosure", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, enclosure);
                MethodInfo contactMaterial = envType.GetMethod("CreateContactMaterial", BindingFlags.Static | BindingFlags.NonPublic)!;
                managerType.GetField("_enclosureMaterial", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, contactMaterial.Invoke(null, new object[] { false }));
                managerType.GetField("_floorMaterial", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, contactMaterial.Invoke(null, new object[] { true }));
                managerType.GetField("_grabOwnership", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, Activator.CreateInstance(ownershipType, true));
                managerType.GetField("_grabInput", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(
                    manager, controllerType.GetMethod("CreateForGrab", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, null));
                managerType.GetMethod("CreateEnclosureFloor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(manager, null);

                foreach (string flag in new[] { "_cubeEnabled", "_sphereEnabled", "_fluidCubeEnabled", "_fluidSphereEnabled" })
                    envType.GetField(flag, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, false);
                envType.GetField("_manager", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, manager);
                envType.GetField("_menuActive", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
                managerObject.SetActive(true);

                FieldInfo Effect(string name) => managerType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
                object? GrabbableOf(object? effect) => effect == null ? null : effectType.GetProperty("Grabbable", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(effect);

                // CASE A: placement and independence.
                setCube.Invoke(null, new object[] { true });
                setSphere.Invoke(null, new object[] { true });
                object? cube = Effect("_cube").GetValue(manager);
                object? sphere = Effect("_sphere").GetValue(manager);
                AssertTrue(cube != null && sphere != null, "originals must spawn");
                AssertTrue(Effect("_fluidCube").GetValue(manager) == null && Effect("_fluidSphere").GetValue(manager) == null, "fluids must stay off");
                setFluidCube.Invoke(null, new object[] { true });
                setFluidSphere.Invoke(null, new object[] { true });
                object? fluidCube = Effect("_fluidCube").GetValue(manager);
                object? fluidSphere = Effect("_fluidSphere").GetValue(manager);
                AssertTrue(fluidCube != null && fluidSphere != null, "fluid extras must spawn");
                AssertTrue(Effect("_cube").GetValue(manager) == cube && Effect("_sphere").GetValue(manager) == sphere, "original references must stay");
                object?[] effects = { cube, sphere, fluidCube, fluidSphere };
                Vector3[] expected =
                {
                    new(-1f, 0.275f, 1f), new(1f, 0.275f, 1f), new(-1f, 0.275f, 0f), new(1f, 0.275f, 0f),
                };
                for (int index = 0; index < 4; index++)
                {
                    for (int other = index + 1; other < 4; other++)
                    {
                        AssertTrue(effects[index] != effects[other], "effects are distinct");
                        AssertTrue(GrabbableOf(effects[index]) != GrabbableOf(effects[other]), "grabbables are distinct");
                        AssertTrue(((Component)effects[index]!).GetComponent<Renderer>().sharedMaterial
                            != ((Component)effects[other]!).GetComponent<Renderer>().sharedMaterial, "materials are distinct");
                    }

                    AssertTrue(GrabbableOf(effects[index]) != null, $"slot {index} grabbable attached");
                    AssertTrue(((Component)effects[index]!).GetComponent<Rigidbody>() != null, $"slot {index} rigidbody exists");
                    AssertTrue(((Component)effects[index]!).GetComponentInChildren<Collider>() != null, $"slot {index} collider exists");
                    Component effect = (Component)effects[index]!;
                    AssertTrue(Vector3.Distance(effect.transform.position, expected[index]) < 0.001f, $"slot {index} placement");
                    AssertTrue(Vector3.Distance(effect.transform.localScale, Vector3.one * 0.55f) < 0.001f, $"slot {index} scale");
                    ownedRoots.Add(effect.gameObject);
                    ownedMaterials.Add(effect.GetComponent<Renderer>().sharedMaterial);
                }

                foreach ((object? original, object? fluid) in new[] { (cube, fluidCube), (sphere, fluidSphere) })
                {
                    Rigidbody originalBody = ((Component)original!).GetComponent<Rigidbody>();
                    Rigidbody fluidBody = ((Component)fluid!).GetComponent<Rigidbody>();
                    AssertTrue(Mathf.Approximately(originalBody.mass, fluidBody.mass), "matching shape mass");
                    AssertTrue(Mathf.Approximately(originalBody.maxAngularVelocity, fluidBody.maxAngularVelocity), "matching angular cap");
                    AssertTrue(originalBody.interpolation == fluidBody.interpolation, "matching interpolation");
                    AssertTrue(originalBody.collisionDetectionMode == fluidBody.collisionDetectionMode, "matching collision mode");
                    AssertTrue(Mathf.Approximately(originalBody.linearDamping, fluidBody.linearDamping), "matching linear damping");
                    AssertTrue(Mathf.Approximately(originalBody.angularDamping, fluidBody.angularDamping), "matching angular damping");
                    AssertTrue(originalBody.useGravity == fluidBody.useGravity, "matching gravity");
                    AssertTrue(((Component)original).GetComponentInChildren<Collider>().GetType()
                        == ((Component)fluid).GetComponentInChildren<Collider>().GetType(), "matching collider type");
                    AssertTrue(((Component)original).GetComponentInChildren<Collider>().sharedMaterial
                        == ((Component)fluid).GetComponentInChildren<Collider>().sharedMaterial, "matching contact material");
                }

                Debug.Log("[FerrofluidFluidObjectsValidation] CASE placement/shape equivalence PASS");

                // CASE B: hover resolves each root AND its child to the same grabbable.
                MethodInfo resolve = managerType.GetMethod("ResolveHovered", BindingFlags.Instance | BindingFlags.NonPublic)!;
                foreach (object? effect in effects)
                {
                    Transform root = ((Component)effect!).transform;
                    AssertTrue(resolve.Invoke(manager, new object[] { root.gameObject }) == GrabbableOf(effect), "root hover resolves");
                    GameObject child = new("hover child");
                    child.transform.SetParent(root, false);
                    AssertTrue(resolve.Invoke(manager, new object[] { child }) == GrabbableOf(effect), "child hover resolves");
                    Object.Destroy(child);
                }

                Debug.Log("[FerrofluidFluidObjectsValidation] CASE hover PASS");

                // CASE C: per-instance spike suppression across the shared global toggle.
                PropertyInfo spikes = config.GetType().GetProperty("FerrofluidSpikesEnabled")!;
                MethodInfo apply = managerType.GetMethod("ApplyConfiguration", BindingFlags.Instance | BindingFlags.NonPublic)!;
                float SpikeOf(object? effect) => ((Component)effect!).GetComponent<Renderer>().sharedMaterial.GetFloat("_SpikesEnabled");
                spikes.SetValue(config, true);
                apply.Invoke(manager, new object[] { config });
                float[] on = { SpikeOf(cube), SpikeOf(sphere), SpikeOf(fluidCube), SpikeOf(fluidSphere) };
                AssertTrue(on[0] == 1f && on[1] == 1f && on[2] == 0f && on[3] == 0f, $"global on {string.Join(",", on)}");
                spikes.SetValue(config, false);
                apply.Invoke(manager, new object[] { config });
                AssertTrue(SpikeOf(cube) == 0f && SpikeOf(sphere) == 0f && SpikeOf(fluidCube) == 0f && SpikeOf(fluidSphere) == 0f, "global off clears all");
                spikes.SetValue(config, true);
                apply.Invoke(manager, new object[] { config });
                AssertTrue(SpikeOf(cube) == 1f && SpikeOf(sphere) == 1f && SpikeOf(fluidCube) == 0f && SpikeOf(fluidSphere) == 0f, "global on restores originals only");
                AssertTrue((bool)spikes.GetValue(config)!, "shared config stays true");
                Debug.Log("[FerrofluidFluidObjectsValidation] CASE spike suppression PASS");

                // CASE D: shared controller input drives each extra like its shape counterpart.
                MethodInfo setInput = grabbableType.GetMethod("SetControllerInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
                MethodInfo advance = grabbableType.GetMethod("AdvancePhysics", BindingFlags.Instance | BindingFlags.NonPublic)!;
                foreach (object? effect in effects)
                {
                    object? grabbable = GrabbableOf(effect);
                    if (grabbable == null)
                        continue;
                    setInput.Invoke(grabbable, new object[] { Vector2.right, Vector2.up, Quaternion.identity, Vector3.right });
                    advance.Invoke(grabbable, new object[] { Time.fixedDeltaTime, Time.unscaledTime });
                }

                Physics.Simulate(Time.fixedDeltaTime);
                foreach ((object? original, object? fluid) in new[] { (cube, fluidCube), (sphere, fluidSphere) })
                {
                    Rigidbody originalBody = ((Component)original!).GetComponent<Rigidbody>();
                    Rigidbody fluidBody = ((Component)fluid!).GetComponent<Rigidbody>();
                    float originalV = originalBody.linearVelocity.magnitude;
                    float fluidV = fluidBody.linearVelocity.magnitude;
                    float originalW = originalBody.angularVelocity.magnitude;
                    float fluidW = fluidBody.angularVelocity.magnitude;
                    AssertTrue(!float.IsNaN(fluidV) && !float.IsInfinity(fluidV) && fluidV > 0.001f, $"fluid velocity {fluidV}");
                    AssertTrue(!float.IsNaN(fluidW) && !float.IsInfinity(fluidW) && fluidW > 1e-6f, $"fluid angular {fluidW}");
                    AssertTrue(fluidBody.linearVelocity.x > 0.001f, "force must move the extra in the input direction");
                    AssertTrue(Vector3.Distance(fluidBody.linearVelocity, originalBody.linearVelocity) < 0.005f, "matching shape linear velocity");
                    AssertTrue(Vector3.Distance(fluidBody.angularVelocity, originalBody.angularVelocity) < 0.005f, "matching shape angular velocity");
                    Debug.Log($"[FerrofluidFluidObjectsValidation] input pair v={originalV:F4}/{fluidV:F4} w={originalW:F4}/{fluidW:F4}");
                }

                Debug.Log("[FerrofluidFluidObjectsValidation] CASE input delivery PASS");

                ValidateCubeFaceDamping(plugin, config, manager, effects);

                // CASE E: independent toggles and lifecycle cleanup.
                setFluidCube.Invoke(null, new object[] { false });
                AssertTrue(Effect("_fluidCube").GetValue(manager) == null, "fluid cube disables alone");
                AssertTrue(Effect("_sphere").GetValue(manager) == sphere && Effect("_fluidSphere").GetValue(manager) == fluidSphere, "others survive fluid cube off");
                setFluidSphere.Invoke(null, new object[] { false });
                AssertTrue(Effect("_fluidSphere").GetValue(manager) == null, "fluid sphere disables alone");
                AssertTrue(Effect("_cube").GetValue(manager) == cube && Effect("_sphere").GetValue(manager) == sphere, "originals survive both extras off");
                setFluidCube.Invoke(null, new object[] { true });
                setFluidSphere.Invoke(null, new object[] { true });
                object? newFluidCube = Effect("_fluidCube").GetValue(manager);
                AssertTrue(newFluidCube != null && newFluidCube != fluidCube, "replacement is a new instance");
                setFluidCube.Invoke(null, new object[] { true });
                AssertTrue(Effect("_fluidCube").GetValue(manager) == newFluidCube, "re-enable must not duplicate");
                setCube.Invoke(null, new object[] { false });
                setSphere.Invoke(null, new object[] { false });
                AssertTrue(Effect("_cube").GetValue(manager) == null && Effect("_sphere").GetValue(manager) == null, "originals off");
                AssertTrue(Effect("_fluidCube").GetValue(manager) != null && Effect("_fluidSphere").GetValue(manager) != null, "fluids survive without originals");
                Debug.Log("[FerrofluidFluidObjectsValidation] CASE toggle independence PASS");

                setCube.Invoke(null, new object[] { true });
                setSphere.Invoke(null, new object[] { true });
                foreach (string field in new[] { "_cube", "_sphere", "_fluidCube", "_fluidSphere" })
                {
                    Component effect = (Component)Effect(field).GetValue(manager)!;
                    ownedRoots.Add(effect.gameObject);
                    ownedMaterials.Add(effect.GetComponent<Renderer>().sharedMaterial);
                }

                RenderTexture.active = previousTarget;
                managerObject.SetActive(false);
                foreach (string field in new[] { "_cube", "_sphere", "_fluidCube", "_fluidSphere" })
                    AssertTrue(Effect(field).GetValue(manager) == null, $"{field} must clear on disable");

                foreach (GameObject root in ownedRoots)
                    AssertTrue(root == null || !root.activeInHierarchy, "disabled roots must stop immediately");

                yield return null;
                yield return null;
                foreach (GameObject root in ownedRoots)
                    AssertTrue(root == null, "owned roots must be destroyed with the manager");

                foreach (Material material in ownedMaterials)
                    AssertTrue(material == null, "owned surface materials must be destroyed with their roots");

                Debug.Log("[FerrofluidFluidObjectsValidation] CASE manager cleanup PASS");
            }
            finally
            {
                RenderTexture.active = previousTarget;
                managerObject.SetActive(false);
                Object.Destroy(managerObject);
                Object.Destroy(anchor);
                Physics.simulationMode = previousMode;
                Time.fixedDeltaTime = previousDelta;
                for (int index = 0; index < staticNames.Length; index++)
                    envType.GetField(staticNames[index], BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, captured[index]);
            }

            yield return null;
            Debug.Log("[FerrofluidFluidObjectsValidation] PASS: placement, hover, spikes, input, toggles, cleanup verified.");
        }

        private static void ValidateCubeFaceDamping(Assembly plugin, object config, Component manager, object?[] effects)
        {
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type effectType = effects[0]!.GetType();
            PropertyInfo grabbableProperty = effectType.GetProperty("Grabbable", instance)!;
            Type grabbableType = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true)!;
            MethodInfo applyFace = grabbableType.GetMethod("ApplyCubeFaceSettling", instance)!;
            MethodInfo setInput = grabbableType.GetMethod("SetControllerInput", instance)!;
            FieldInfo incoming = grabbableType.GetField("_incomingYVelocity", instance)!;
            FieldInfo shape = grabbableType.GetField("_grabShape", instance)!;
            MethodInfo support = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabPhysics", true)!
                .GetMethod("EvaluateSupportExtent", BindingFlags.Static | BindingFlags.NonPublic)!;
            Collider floor = (Collider)manager.GetType().GetField("_floorCollider", instance)!.GetValue(manager)!;
            Component[] grabbables = new Component[4];
            Rigidbody[] bodies = new Rigidbody[4];
            for (int index = 0; index < effects.Length; index++)
            {
                grabbables[index] = (Component)grabbableProperty.GetValue(effects[index])!;
                bodies[index] = grabbables[index].GetComponent<Rigidbody>();
            }

            Type uiType = plugin.GetType("ChromaGLS.Settings.FerrofluidSettingsUI", true)!;
            Type actionType = typeof(Action<>).MakeGenericType(config.GetType());
            Delegate applyConfiguration = Delegate.CreateDelegate(
                actionType,
                manager,
                manager.GetType().GetMethod("ApplyConfiguration", instance)!);
            object ui = Activator.CreateInstance(
                uiType,
                instance,
                null,
                new object[] { config, applyConfiguration },
                null)!;
            object session = uiType.GetField("_session", instance)!.GetValue(ui)!;
            MethodInfo open = session.GetType().GetMethod("Open", instance)!;
            MethodInfo cancel = session.GetType().GetMethod("Cancel", instance)!;

            void SetSlider(string name, float value) => uiType.GetProperty(name)!.SetValue(ui, value);

            void Prepare(int index, float omega)
            {
                Rigidbody body = bodies[index];
                body.rotation = Quaternion.Euler(0f, 0f, 5f);
                float extent = (float)support.Invoke(
                    null,
                    new[] { shape.GetValue(grabbables[index])!, body.rotation, (object)Vector3.down })!;
                Vector3 position = body.position;
                position.y = floor.bounds.max.y + extent;
                body.position = position;
                body.angularVelocity = Vector3.forward * omega;
                body.linearVelocity = Vector3.right * 0.25f;
                incoming.SetValue(grabbables[index], 0f);
                setInput.Invoke(grabbables[index], new object[] { Vector2.zero, Vector2.zero, Quaternion.identity, Vector3.right });
            }

            Vector3 Apply(int index, float deltaTime = 0.02f, float gap = 0f)
            {
                Vector3 velocity = bodies[index].linearVelocity;
                applyFace.Invoke(grabbables[index], new object[] { gap, deltaTime });
                AssertTrue(
                    Vector3.Distance(velocity, bodies[index].linearVelocity) < 0.000001f,
                    "face damping must not change linear velocity");
                return bodies[index].angularVelocity;
            }

            void CheckCubes(float omega, float expected)
            {
                foreach (int index in new[] { 0, 2 })
                {
                    Prepare(index, omega);
                    Vector3 actual = Apply(index);
                    AssertTrue(
                        Vector3.Distance(actual, Vector3.forward * expected) < 0.0001f,
                        $"cube slot {index} post-contact omega {actual.z:F6}, expected {expected:F6}");
                    Debug.Log($"[FerrofluidFluidObjectsValidation] cube damping slot={index} input={omega:F3} output={actual.z:F6} expected={expected:F6}");
                }
            }

            open.Invoke(session, null);
            try
            {
                SetSlider("CubeFaceDampingStrength", 0f);
                CheckCubes(0.5f, 0.5f);
                SetSlider("CubeFaceDampingStrength", 2f);
                CheckCubes(0.5f, 0.5f * Mathf.Exp(-8f * 0.02f));
                CheckCubes(-0.5f, -0.5f);
                SetSlider("CubeFaceDampingExponent", 3f);
                CheckCubes(0.5f, 0.5f * Mathf.Exp(-16f * 0.02f));
                SetSlider("CubeFaceDampingStartAngle", 4f);
                CheckCubes(0.5f, 0.5f);
                cancel.Invoke(session, null);
                CheckCubes(0.5f, 0.5f * Mathf.Exp(-8f * 0.02f));
                Debug.Log("[FerrofluidFluidObjectsValidation] CASE live damping sliders and Cancel PASS");

                foreach (int index in new[] { 0, 2 })
                {
                    Prepare(index, 0.5f);
                    Vector3 whole = Apply(index, 0.04f);
                    Prepare(index, 0.5f);
                    Apply(index);
                    Vector3 halves = Apply(index);
                    AssertTrue(Vector3.Distance(whole, halves) < 0.00001f, "production applier must be dt invariant");
                    AssertTrue(
                        Vector3.Distance(whole, Vector3.forward * (0.5f * Mathf.Exp(-8f * 0.04f))) < 0.0001f,
                        "production applier must use the exponential retention");

                    Prepare(index, 0.5f);
                    AssertTrue(Vector3.Distance(Apply(index, gap: 0.02f), Vector3.forward * 0.5f) < 0.00001f, "clearance must exclude damping");
                    Prepare(index, 0.5f);
                    incoming.SetValue(grabbables[index], -2f);
                    AssertTrue(Vector3.Distance(Apply(index), Vector3.forward * 0.5f) < 0.00001f, "fast impact must exclude damping");
                    foreach ((Vector2 force, Vector2 spin) in new[] { (Vector2.right, Vector2.zero), (Vector2.zero, Vector2.up) })
                    {
                        Prepare(index, 0.5f);
                        setInput.Invoke(grabbables[index], new object[] { force, spin, Quaternion.identity, Vector3.right });
                        AssertTrue(Vector3.Distance(Apply(index), Vector3.forward * 0.5f) < 0.00001f, "controller input must exclude damping");
                    }
                }

                foreach (int index in new[] { 1, 3 })
                {
                    Prepare(index, 0.5f);
                    AssertTrue(Vector3.Distance(Apply(index), Vector3.forward * 0.5f) < 0.00001f, "spheres must not receive cube face damping");
                }

                Debug.Log("[FerrofluidFluidObjectsValidation] CASE damping dt invariance and exclusions PASS");
                object originalGrabbable = grabbableProperty.GetValue(effects[0])!;
                try
                {
                    grabbableProperty.SetValue(effects[0], null);
                    effectType.GetMethod("ApplyConfiguration", instance)!.Invoke(effects[0], new[] { config });
                }
                finally
                {
                    grabbableProperty.SetValue(effects[0], originalGrabbable);
                }

                Debug.Log("[FerrofluidFluidObjectsValidation] CASE deferred grabbable configuration PASS");
            }
            finally
            {
                cancel.Invoke(session, null);
                ((IDisposable)ui).Dispose();
                foreach (Component grabbable in grabbables)
                    setInput.Invoke(grabbable, new object[] { Vector2.zero, Vector2.zero, Quaternion.identity, Vector3.right });
            }
        }
    }
}
