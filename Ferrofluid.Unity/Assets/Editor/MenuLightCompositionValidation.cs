using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // Composition coverage for menu lights: additive halo spill must never stack on top of a lit
    // core, whatever the light's pose. Production MenuLightRenderer/MenuLightGeometry drive real
    // GameObjects, materials, and queues; only the particle material is an editor reference that
    // reproduces the configured native blend/depth/alpha state, so this isolates the renderer's
    // material ordering rather than the game's post-process chain.
    internal static class MenuLightCompositionValidation
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const int TestLayer = 30;
        private const int Size = 192;

        public static void Run() => FerrofluidValidationRunner.Run(typeof(MenuLightCompositionValidation));

        private static void RunRuntime()
        {
            Assembly plugin = FerrofluidValidationRuntime.CreateConfig().GetType().Assembly;
            Type resourcesType = plugin.GetType("ChromaGLS.Settings.FerrofluidGpuResources", true);
            Shader reference = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/MenuLightCompositionReference.shader");
            Check(reference != null, "The composition reference shader must exist.");

            // A hand-built resources object lets the renderer see the same optional shaders the
            // embedded bundle would provide; the statics are restored afterwards.
            FieldInfo instanceField = resourcesType.GetField("_instance", Static);
            FieldInfo attemptedField = resourcesType.GetField("_attemptedLoad", Static);
            FieldInfo bundleField = resourcesType.GetField("_bundle", Static);
            object savedInstance = instanceField.GetValue(null);
            object savedAttempted = attemptedField.GetValue(null);
            object savedBundle = bundleField.GetValue(null);
            object resources = resourcesType.GetConstructor(
                Instance, null, new[] { typeof(Shader), typeof(Shader), typeof(Texture2D), typeof(Texture2D) }, null)!
                .Invoke(new object[]
                {
                    AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSimulation.shader"),
                    AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader"),
                    Texture2D.whiteTexture,
                    Texture2D.whiteTexture,
                });
            PropertyInfo occlusion = resourcesType.GetProperty("MenuLightCoreOcclusionShader", Instance);
            if (occlusion != null)
            {
                // Optional seam: when the coverage shader is absent this stays unset and the
                // renderer must keep its previous single-material core draw.
                occlusion.GetSetMethod(true)?.Invoke(
                    resources,
                    new object[] { AssetDatabase.LoadAssetAtPath<Shader>("Assets/MenuLightCoreOcclusion.shader") });
            }

            instanceField.SetValue(null, resources);
            Fixture fixture = new(plugin, reference);
            try
            {
                fixture.Run();
                Debug.Log("[MenuLightCompositionValidation] PASS: coverage compositor, poses, two lights, fades, occluder, apply/toggle.");
            }
            finally
            {
                fixture.Dispose();
                instanceField.SetValue(null, savedInstance);
                attemptedField.SetValue(null, savedAttempted);
                bundleField.SetValue(null, savedBundle);
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private sealed class Fixture : IDisposable
        {
            private readonly Assembly _plugin;
            private readonly Shader _reference;
            private readonly Type _definitionType;
            private readonly Type _settingsType;
            private readonly Type _rendererType;
            private readonly Type _meshesType;
            private readonly Type _lightType;
            private readonly Type _geometryType;
            private readonly object _enclosure;
            private readonly RenderTexture _target;
            private readonly Camera _camera;
            private readonly List<Object> _owned = new();
            private readonly string _artifacts;

            internal Fixture(Assembly plugin, Shader reference)
            {
                _plugin = plugin;
                _reference = reference;
                _definitionType = plugin.GetType("ChromaGLS.Settings.MenuLightDefinition", true);
                _settingsType = plugin.GetType("ChromaGLS.Settings.ParabolicFloorLightSettings", true);
                _rendererType = plugin.GetType("ChromaGLS.Settings.MenuLightRenderer", true);
                _meshesType = plugin.GetType("ChromaGLS.Settings.MenuLightMeshes", true);
                _lightType = plugin.GetType("ChromaGLS.Settings.MenuLightType", true);
                _geometryType = plugin.GetType("ChromaGLS.Settings.MenuLightGeometry", true);
                _enclosure = Activator.CreateInstance(
                    plugin.GetType("ChromaGLS.Settings.FerrofluidGrabEnclosure", true),
                    Instance, null,
                    new object[] { Vector3.zero, 5f, new Vector3(0f, 0f, -5f), Vector3.forward, 0f }, null);
                _target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                Check(_target.Create(), "The ARGBFloat target with depth must be supported.");
                _owned.Add(_target);
                GameObject host = new("Menu light composition camera");
                _owned.Add(host);
                _camera = host.AddComponent<Camera>();
                _camera.enabled = false;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = Color.clear;
                _camera.cullingMask = 1 << TestLayer;
                _camera.allowHDR = true;
                _camera.fieldOfView = 60f;
                _camera.nearClipPlane = 0.02f;
                _camera.farClipPlane = 60f;
                _camera.targetTexture = _target;
                _artifacts = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "Scratch", "menu-light-composition"));
                Directory.CreateDirectory(_artifacts);
            }

            public void Dispose()
            {
                foreach (Object resource in _owned)
                {
                    if (resource == null)
                        continue;
                    if (resource is RenderTexture texture)
                        texture.Release();
                    Object.DestroyImmediate(resource);
                }
            }

            internal void Run()
            {
                // From below the halo plane sits in front of the strip, so it must be erased
                // under the lit surface instead of adding to it. This is the reported defect.
                foreach (string type in new[] { "Cube", "Sphere", "Parabola" })
                {
                    object light = Spawn(Definition(type + "-below", type, 2f, 0.5f, 0.2f, height: 1f));
                    Aim(new Vector3(0f, 0.7f, -2f), new Vector3(0f, 0.98f, -0.2f));
                    AssertComposition("below " + type, light);
                    Remove(light);
                }

                // Above, the halo is behind the depth-writing core already; this is a control.
                {
                    object light = Spawn(Definition("above-cube", "Cube", 2f, 0.5f, 0.2f, height: 1f));
                    Aim(new Vector3(0f, 2.2f, -0.6f), new Vector3(0f, 1f, 0f));
                    AssertComposition("above cube", light);
                    Remove(light);
                }

                // A 90 degree yaw plus a translated origin keeps the glow plane below the core
                // for a low camera; composition must not depend on the light's pose.
                {
                    object definition = Definition("tilted", "Cube", 2f, 0.5f, 0.2f, height: 1f);
                    Set(definition, "PositionX", 0.4f);
                    Set(definition, "PositionZ", -0.2f);
                    _definitionType.GetMethod("RotateAxis", Instance).Invoke(definition, new object[] { 1, 90f });
                    object light = Spawn(definition);
                    Aim(new Vector3(0.4f, 0.7f, -2f), new Vector3(0.4f, 0.98f, -0.2f));
                    AssertComposition("tilted translated cube", light);
                    Remove(light);
                }

                // The same local below-source viewpoint must compose under arbitrary whole-light
                // rotations; the camera follows the generated pose rather than world axes.
                foreach ((string label, int[] axes, float[] degrees) in new[]
                {
                    ("rx90", new[] { 0 }, new[] { 90f }),
                    ("rx35 rz-25", new[] { 0, 2 }, new[] { 35f, -25f }),
                })
                {
                    object definition = Definition("tilted-" + label, "Cube", 2f, 0.5f, 0.2f, height: 1f);
                    Set(definition, "PositionX", 0.4f);
                    Set(definition, "PositionZ", -0.2f);
                    for (int step = 0; step < axes.Length; step++)
                        _definitionType.GetMethod("RotateAxis", Instance).Invoke(definition, new object[] { axes[step], degrees[step] });
                    object light = Spawn(definition);
                    AimPose(light, new Vector3(0f, -0.3f, -2f), new Vector3(0f, -0.02f, -0.2f));
                    AssertComposition("tilted " + label + " cube", light);
                    Remove(light);
                }

                // One halo crossing another light's core, plus overlapping cores: the nearer lit
                // surface always wins, in either spawn order.
                TwoLights("crossed", true, true);
                TwoLights("crossed", false, true);
                TwoLights("cores", true, false);
                TwoLights("cores", false, false);

                // A hand-edited fade ramp proves partial coverage attenuates a coloured backdrop
                // proportionally and leaves zero-coverage pixels untouched.
                FadeRamp();

                // An opaque foreground must still hide everything behind it; no ZTestAlways.
                ForegroundOccluder();

                // Apply and visibility toggles must keep the coverage/native pair and hide both.
                ApplyAndToggle();
            }

            private object Definition(string id, string shape, float red, float green, float blue, float height)
            {
                object definition = Activator.CreateInstance(_definitionType, true);
                Set(definition, "Id", id);
                Set(definition, "Type", Enum.Parse(_lightType, shape));
                object appearance = _settingsType.GetField("Default", Static).GetValue(null);
                foreach ((string field, float amount) in new[]
                {
                    ("Brightness", 0.6f), ("Red", red), ("Green", green), ("Blue", blue),
                    ("BloomWidthScale", 1f), ("BloomBrightness", 1f),
                })
                {
                    appearance = _settingsType.GetMethod("With" + field, Instance).Invoke(appearance, new object[] { amount });
                }

                Set(definition, "Appearance", appearance);
                Set(definition, "Height", height);
                return definition;
            }

            private object Meshes(object definition)
            {
                object[] arguments = { _enclosure, Vector3.zero, Vector3.forward, definition, null };
                Check((bool)_geometryType.GetMethod("TryCreate", Static).Invoke(null, arguments), "the test light must generate geometry");
                return arguments[4];
            }

            private object Spawn(object definition)
            {
                object renderer = _rendererType.GetConstructor(
                    Instance, null,
                    new[] { typeof(Shader), typeof(Scene), _definitionType, _meshesType, typeof(bool) }, null)!
                    .Invoke(new object[] { _reference, SceneManager.GetActiveScene(), definition, Meshes(definition), true });
                Root(renderer).layer = TestLayer;
                Halo(renderer).layer = TestLayer;
                return renderer;
            }

            private void Remove(object renderer)
            {
                // Dispose destroys at end of frame; hide first so later captures stay clean.
                Root(renderer).SetActive(false);
                ((IDisposable)renderer).Dispose();
            }

            private void Aim(Vector3 position, Vector3 lookAt)
                => _camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up));

            private void AimPose(object renderer, Vector3 localPosition, Vector3 localTarget)
            {
                Transform root = Root(renderer).transform;
                Vector3 position = root.position + (root.rotation * localPosition);
                Vector3 target = root.position + (root.rotation * localTarget);
                _camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, root.rotation * Vector3.up));
            }

            private Color[] Capture()
            {
                _camera.Render();
                return FerrofluidValidationRuntime.Read(_target);
            }

            private void AssertComposition(string label, object renderer, Color? fadeClear = null, bool requirePartial = false)
            {
                GameObject root = Root(renderer);
                GameObject halo = Halo(renderer);
                MeshRenderer core = root.GetComponent<MeshRenderer>();
                Color[] fadeReference = null;
                try
                {
                    if (fadeClear.HasValue)
                        _camera.backgroundColor = fadeClear.Value;
                    root.SetActive(false);
                    Color[] clear = Capture();
                    root.SetActive(true);
                    halo.SetActive(false);
                    Color[] coreOnly = Capture();
                    halo.SetActive(true);
                    core.enabled = false;
                    Color[] haloOnly = Capture();
                    core.enabled = true;
                    Color[] combined = Capture();
                    if (fadeClear.HasValue)
                    {
                        // A black-clear core capture keeps the fade estimate exact: under a
                        // coloured alpha backdrop the coverage pass attenuates clear alpha too,
                        // so it cannot be separated from the core's own .36 payload there.
                        _camera.backgroundColor = Color.clear;
                        halo.SetActive(false);
                        fadeReference = Capture();
                        halo.SetActive(true);
                    }

                    Dump(label + "-combined", combined);
                    Dump(label + "-coreOnly", coreOnly);
                    Dump(label + "-haloOnly", haloOnly);
                    AssertComposite(label, clear, coreOnly, haloOnly, combined, fadeReference, requirePartial);
                }
                finally
                {
                    _camera.backgroundColor = Color.clear;
                }
            }

            private void AssertComposite(string label, Color[] clear, Color[] coreOnly, Color[] haloOnly, Color[] combined, Color[] fadeReference, bool requirePartial)
            {
                int covered = 0;
                int partial = 0;
                int outsideGlow = 0;
                float peakAlpha = 0f;
                float peakRgb = 0f;
                float worst = 0f;
                int worstPixel = -1;
                float worstFade = 0f;
                Color worstExpected = default;
                for (int pixel = 0; pixel < combined.Length; pixel++)
                {
                    Color coreReference = fadeReference != null ? fadeReference[pixel] : default;
                    float fade = Mathf.Clamp01(((fadeReference != null ? coreReference.a : coreOnly[pixel].a) - (fadeReference != null ? 0f : clear[pixel].a)) / 0.36f);
                    float keep = 1f - fade;
                    Color expected = default;
                    for (int channel = 0; channel < 4; channel++)
                        expected[channel] = (haloOnly[pixel][channel] * keep) + coreOnly[pixel][channel] - (clear[pixel][channel] * keep);

                    float error = ChannelError(combined[pixel], expected);
                    if (error > worst)
                    {
                        worst = error;
                        worstPixel = pixel;
                        worstFade = fade;
                        worstExpected = expected;
                    }

                    peakAlpha = Mathf.Max(peakAlpha, fadeReference != null ? coreReference.a : coreOnly[pixel].a - clear[pixel].a);
                    peakRgb = Mathf.Max(peakRgb, (fadeReference != null ? coreReference.maxColorComponent : coreOnly[pixel].maxColorComponent) - (fadeReference != null ? 0f : clear[pixel].maxColorComponent));
                    if (fade > 0.95f && haloOnly[pixel].maxColorComponent > 0.01f)
                        covered++;
                    if (fade > 0.02f && fade < 0.98f && haloOnly[pixel].maxColorComponent > 0.01f)
                        partial++;
                    if (fade < 0.05f && combined[pixel].maxColorComponent > 0.005f)
                        outsideGlow++;
                }

                int inspect = worstPixel < 0 ? 0 : worstPixel;
                Debug.Log($"[MenuLightCompositionValidation] {label}: covered={covered} partial={partial} outsideGlow={outsideGlow} peakAlpha={peakAlpha:F3} peakRgb={peakRgb:F3} maxError={worst:F4} worstPixel={worstPixel} f={worstFade:F2} actual={combined[inspect]} expected={worstExpected} halo={haloOnly[inspect]} core={coreOnly[inspect]} clear={clear[inspect]}");
                Check(covered > 10, label + ": solid core pixels must overlap live halo or the composition is untested.");
                Check(outsideGlow > 10, label + ": halo beyond the core footprint must survive untouched.");
                Check(Mathf.Abs(peakAlpha - 0.36f) <= 0.002f, label + ": a fully covered core pixel must carry the native .36 bloom payload, not opacity.");
                Check(peakRgb > 1f, label + ": the core's HDR radiance must survive above 1.");
                if (requirePartial)
                    Check(partial > 10, label + ": mid-fade pixels must exist for the proportional attenuation oracle.");
                Check(worst <= 0.005f, label + ": additive halo must never stack on a lit core.");
            }

            private void TwoLights(string label, bool nearFirst, bool halos)
            {
                object nearDefinition = Definition(label + "-near", "Cube", 2f, 0.3f, 0.1f, height: 1f);
                Set(nearDefinition, "PositionX", -0.1f);
                Set(nearDefinition, "PositionZ", -0.1f);
                object farDefinition = Definition(label + "-far", "Cube", 0.1f, 0.4f, 2f, height: 1f);
                Set(farDefinition, "PositionX", 0.35f);
                Set(farDefinition, "PositionZ", 1f);
                object first = Spawn(nearFirst ? nearDefinition : farDefinition);
                object second = Spawn(nearFirst ? farDefinition : nearDefinition);
                object near = nearFirst ? first : second;
                object far = nearFirst ? second : first;
                string name = label + (nearFirst ? " near-first" : " far-first");
                try
                {
                    Aim(new Vector3(0f, 0.7f, -2f), new Vector3(0.1f, 0.99f, 0f));
                    GameObject nearRoot = Root(near);
                    GameObject farRoot = Root(far);
                    MeshRenderer nearCore = nearRoot.GetComponent<MeshRenderer>();
                    MeshRenderer farCore = farRoot.GetComponent<MeshRenderer>();
                    // Disjoint z ranges guarantee the near surface is first along every shared
                    // ray, so the oracle can demand the near strip wins every shared pixel.
                    Check(nearCore.bounds.max.z < farCore.bounds.min.z, name + ": fixture must keep a positive z gap between the lights.");
                    Check(_camera.transform.position.z < nearCore.bounds.min.z, name + ": camera must sit in front of both lights.");
                    if (!halos)
                    {
                        Halo(near).SetActive(false);
                        Halo(far).SetActive(false);
                    }

                    nearRoot.SetActive(false);
                    farRoot.SetActive(false);
                    Color[] clear = Capture();
                    nearRoot.SetActive(true);
                    Halo(near).SetActive(false);
                    Color[] nearCoreOnly = Capture();
                    nearRoot.SetActive(false);
                    farRoot.SetActive(true);
                    Halo(far).SetActive(false);
                    Color[] farCoreOnly = Capture();
                    nearCore.enabled = false;
                    farCore.enabled = false;
                    nearRoot.SetActive(true);
                    Halo(near).SetActive(halos);
                    Halo(far).SetActive(halos);
                    Color[] backdrop = Capture();
                    nearCore.enabled = true;
                    farCore.enabled = true;
                    Color[] combined = Capture();
                    Dump(name + "-combined", combined);

                    int covered = 0;
                    int shared = 0;
                    int outsideGlow = 0;
                    float worst = 0f;
                    int worstPixel = -1;
                    float worstNearFade = 0f;
                    float worstFarFade = 0f;
                    Color worstExpected = default;
                    for (int pixel = 0; pixel < combined.Length; pixel++)
                    {
                        float nearFade = Mathf.Clamp01((nearCoreOnly[pixel].a - clear[pixel].a) / 0.36f);
                        float farFade = Mathf.Clamp01((farCoreOnly[pixel].a - clear[pixel].a) / 0.36f);
                        // Far pair draws first, then the nearer pair attenuates whatever is
                        // already under it and adds its own radiance; the near strip wins
                        // every shared pixel because it is strictly closer along the ray.
                        float keepNear = 1f - nearFade;
                        float keepFar = 1f - farFade;
                        Color expected = default;
                        for (int channel = 0; channel < 4; channel++)
                        {
                            float spill = backdrop[pixel][channel] * keepFar * keepNear;
                            float nearPart = nearCoreOnly[pixel][channel] - clear[pixel][channel];
                            float farPart = farCoreOnly[pixel][channel] - clear[pixel][channel];
                            expected[channel] = spill + (farPart * keepNear) + nearPart;
                        }

                        float error = ChannelError(combined[pixel], expected);
                        if (error > worst)
                        {
                            worst = error;
                            worstPixel = pixel;
                            worstNearFade = nearFade;
                            worstFarFade = farFade;
                            worstExpected = expected;
                        }
                        if (nearFade > 0.95f && farFade > 0.95f)
                            shared++;
                        float underneath = Mathf.Max(backdrop[pixel].maxColorComponent, farCoreOnly[pixel].maxColorComponent);
                        if (nearFade > 0.95f && underneath > clear[pixel].maxColorComponent + 0.01f)
                            covered++;
                        if (nearFade < 0.05f && farFade < 0.05f && combined[pixel].maxColorComponent > clear[pixel].maxColorComponent + 0.005f)
                            outsideGlow++;
                    }

                    Dump(name + "-backdrop", backdrop);
                    Dump(name + "-nearCore", nearCoreOnly);
                    Dump(name + "-farCore", farCoreOnly);
                    int inspect = worstPixel < 0 ? 0 : worstPixel;
                    Debug.Log($"[MenuLightCompositionValidation] {name}: covered={covered} shared={shared} outsideGlow={outsideGlow} maxError={worst:F4} worstPixel={worstPixel} fN={worstNearFade:F2} fF={worstFarFade:F2} actual={combined[inspect]} expected={worstExpected} backdrop={backdrop[inspect]} near={nearCoreOnly[inspect]} far={farCoreOnly[inspect]} clear={clear[inspect]}");
                    Check(shared > 10, name + ": the projections must genuinely overlap on more than ten pixels.");
                    Check(covered > 10, name + ": the nearer core must cover live spill to be tested.");
                    Check(halos ? outsideGlow > 10 : true, name + ": halo outside every core must survive.");
                    Check(worst <= 0.005f, name + ": the nearer lit surface must win; no far additive bleed may remain.");
                }
                finally
                {
                    Remove(first);
                    Remove(second);
                }
            }

            private void FadeRamp()
            {
                object definition = Definition("fade-ramp", "Cube", 2f, 0.5f, 0.2f, height: 1f);
                object meshes = Meshes(definition);
                object core = _meshesType.GetField("Core", Instance).GetValue(meshes);
                Color[] colors = (Color[])core.GetType().GetField("Colors", Instance).GetValue(core);
                Vector3[] vertices = (Vector3[])core.GetType().GetField("Vertices", Instance).GetValue(core);
                // Deterministic coverage ramp by position, so every visible face carries f=1/.5/0.
                for (int index = 0; index < colors.Length; index++)
                    colors[index].a = vertices[index].x < 0f ? 1f : (vertices[index].z < 0f ? 0.5f : 0f);
                object light = _rendererType.GetConstructor(
                    Instance, null,
                    new[] { typeof(Shader), typeof(Scene), _definitionType, _meshesType, typeof(bool) }, null)!
                    .Invoke(new object[] { _reference, SceneManager.GetActiveScene(), definition, meshes, true });
                Root(light).layer = TestLayer;
                Halo(light).layer = TestLayer;
                try
                {
                    Aim(new Vector3(0f, 0.35f, -1.6f), new Vector3(0f, 0.99f, -0.05f));
                    AssertComposition("fade ramp", light, new Color(0.05f, 0.02f, 0.08f, 0.4f), requirePartial: true);
                }
                finally
                {
                    Remove(light);
                }
            }

            private void ForegroundOccluder()
            {
                object light = Spawn(Definition("occluder", "Cube", 2f, 0.5f, 0.2f, height: 1f));
                Material occluderMaterial = new(_reference)
                {
                    color = new Color(0.2f, 0.05f, 0.3f, 1f),
                    renderQueue = 2000,
                };
                occluderMaterial.SetFloat("_BlendSrcFactor", 1f);
                occluderMaterial.SetFloat("_BlendDstFactor", 0f);
                occluderMaterial.SetFloat("_BlendSrcFactorA", 1f);
                occluderMaterial.SetFloat("_BlendDstFactorA", 0f);
                occluderMaterial.SetFloat("_CustomZWrite", 1f);
                occluderMaterial.SetFloat("_ZTest", 4f);
                occluderMaterial.SetFloat("_CullMode", 0f);
                occluderMaterial.SetFloat("_BloomMultiplier", 0f);
                occluderMaterial.mainTexture = Texture2D.whiteTexture;
                Mesh occluderMesh = new()
                {
                    vertices = new[] { new Vector3(-0.3f, -0.25f, 0f), new Vector3(0.3f, -0.25f, 0f), new Vector3(0.3f, 0.25f, 0f), new Vector3(-0.3f, 0.25f, 0f) },
                    triangles = new[] { 0, 2, 1, 0, 3, 2 },
                    colors = new[] { Color.white, Color.white, Color.white, Color.white },
                    uv = new Vector2[4],
                };
                GameObject occluder = new("Composition occluder", typeof(MeshFilter), typeof(MeshRenderer));
                occluder.layer = TestLayer;
                occluder.GetComponent<MeshFilter>().sharedMesh = occluderMesh;
                occluder.GetComponent<MeshRenderer>().sharedMaterial = occluderMaterial;
                try
                {
                    Vector3 cameraPosition = new(0f, 0.7f, -2f);
                    Aim(cameraPosition, new Vector3(0f, 1f, 0f));
                    Color[] unblocked = Capture();
                    occluder.transform.position = new Vector3(0f, 0.85f, -1f);
                    occluder.transform.rotation = Quaternion.LookRotation(occluder.transform.position - cameraPosition);
                    Color[] blocked = Capture();
                    Color occluderColor = new Color(0.2f, 0.05f, 0.3f).linear;
                    int occluded = 0;
                    int spilled = 0;
                    for (int pixel = 0; pixel < blocked.Length; pixel++)
                    {
                        float change = Mathf.Max(
                            Mathf.Abs(blocked[pixel].r - unblocked[pixel].r),
                            Mathf.Max(Mathf.Abs(blocked[pixel].g - unblocked[pixel].g), Mathf.Abs(blocked[pixel].b - unblocked[pixel].b)));
                        if (change <= 0.01f)
                            continue;
                        if (Mathf.Abs(blocked[pixel].r - occluderColor.r) <= 0.005f
                            && Mathf.Abs(blocked[pixel].g - occluderColor.g) <= 0.005f
                            && Mathf.Abs(blocked[pixel].b - occluderColor.b) <= 0.005f)
                        {
                            occluded++;
                        }
                        else if (blocked[pixel].maxColorComponent > 1.5f)
                        {
                            // Only the lit core reaches this brightness, and it sits entirely
                            // behind the occluder: any of it showing through is a depth leak.
                            spilled++;
                        }
                    }

                    int center = (Size / 2) + (Size / 2 * Size);
                    int quarter = (Size / 4) + (Size / 2 * Size);
                    Debug.Log($"[MenuLightCompositionValidation] occluder: occluded={occluded} spilled={spilled} blocked[center]={blocked[center]} unblocked[center]={unblocked[center]} blocked[quarter]={blocked[quarter]} unblocked[quarter]={unblocked[quarter]} quadPos={occluder.transform.position} quadBounds={occluder.GetComponent<MeshRenderer>().bounds}");
                    Check(occluded > 50, "the opaque occluder must cover the light from this angle");
                    Check(spilled == 0, "nothing behind the opaque occluder may draw over it; no ZTestAlways shortcuts");
                }
                finally
                {
                    Object.DestroyImmediate(occluderMesh);
                    Object.DestroyImmediate(occluder);
                    Object.DestroyImmediate(occluderMaterial);
                    Remove(light);
                }
            }

            private void ApplyAndToggle()
            {
                object definition = Definition("apply", "Cube", 2f, 0.5f, 0.2f, height: 1f);
                object light = Spawn(definition);
                object next = Definition("apply-next", "Sphere", 0.2f, 1.5f, 0.4f, height: 1f);
                _rendererType.GetMethod("Apply", Instance).Invoke(light, new object[] { next, Meshes(next), true });
                Aim(new Vector3(0f, 0.7f, -2f), new Vector3(0f, 1f, 0f));
                Material[] shared = Root(light).GetComponent<MeshRenderer>().sharedMaterials;
                Debug.Log($"[MenuLightCompositionValidation] apply: sharedMaterials={shared.Length}");
                AssertComposition("apply sphere", light);
                _rendererType.GetMethod("SetEnabled", Instance).Invoke(light, new object[] { false, false });
                Color[] hidden = Capture();
                float leak = 0f;
                foreach (Color pixel in hidden)
                    leak = Mathf.Max(leak, pixel.maxColorComponent, pixel.a);
                Check(leak <= 0.001f, "a disabled light must hide both the coverage and the additive draw");
                _rendererType.GetMethod("SetEnabled", Instance).Invoke(light, new object[] { true, true });
                FieldInfo coverageField = _rendererType.GetField("_coverageMaterial", Instance);
                if (coverageField != null)
                {
                    Material coverage = coverageField.GetValue(light) as Material;
                    Check(shared.Length == (coverage == null ? 1 : 2), "the core renderer must keep exactly the coverage/native pair");
                }

                Remove(light);
                Check(_rendererType.GetField("_root", Instance).GetValue(light) == null, "dispose must drop the root object");
                Check(_rendererType.GetField("_haloMesh", Instance).GetValue(light) == null, "dispose must drop the halo mesh");
                if (coverageField != null)
                    Check(coverageField.GetValue(light) == null, "dispose must drop the coverage material");
            }

            private void Set(object target, string property, object value)
                => target.GetType().GetProperty(property, Instance).SetValue(target, value);

            private GameObject Root(object renderer)
                => (GameObject)_rendererType.GetField("_root", Instance).GetValue(renderer);

            private GameObject Halo(object renderer)
                => (GameObject)_rendererType.GetField("_haloRoot", Instance).GetValue(renderer);

            private void Dump(string name, Color[] pixels)
            {
                Texture2D image = new(Size, Size, TextureFormat.RGBA32, false, true);
                try
                {
                    Color32[] buffer = new Color32[pixels.Length];
                    for (int index = 0; index < pixels.Length; index++)
                        buffer[index] = pixels[index];
                    image.SetPixels32(buffer);
                    File.WriteAllBytes(Path.Combine(_artifacts, name + ".png"), image.EncodeToPNG());
                }
                finally
                {
                    Object.DestroyImmediate(image);
                }
            }
        }

        private static float ChannelError(Color actual, Color expected)
        {
            float worst = 0f;
            for (int channel = 0; channel < 4; channel++)
                worst = Mathf.Max(worst, Mathf.Abs(actual[channel] - expected[channel]) - (0.005f * Mathf.Abs(expected[channel])));
            return worst;
        }
    }
}
