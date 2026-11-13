using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    // One FERROFLUID_HEIGHT_SPRING blit with strength*dt^2=1 writes the exact magnetic height
    // target: the trigger amplifies front/back response while untouched sites stay at baseline.
    internal static class FerrofluidHeightAmplificationValidation
    {
        private const int SpringPass = 1;

        public static void Run()
        {
            List<string> failures = new();
            List<UnityEngine.Object> resources = new();
            try
            {
                Shader surface = AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader");
                Material material = new(surface);
                resources.Add(material);
                Texture2D sites = new(2, 2, TextureFormat.RGBAFloat, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                sites.SetPixels(new[]
                {
                    new Color(0f, 0.5f, 0f, 0.1f),
                    new Color(0f, -0.5f, 0f, 0.1f),
                    new Color(0.5f, 0f, 0f, 0.1f),
                    new Color(0f, 0f, 0f, 0.1f),
                });
                sites.Apply();
                resources.Add(sites);
                Texture2D baseline = new(2, 2, TextureFormat.RGBAFloat, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                baseline.SetPixels(new[]
                {
                    new Color(0.44f, 0f, 0f, 0f),
                    new Color(0.44f, 0f, 0f, 0f),
                    new Color(0.44f, 0f, 0f, 0f),
                    new Color(0.44f, 0f, 0f, 0f),
                });
                baseline.Apply();
                resources.Add(baseline);
                RenderTexture springState = new(2, 2, 0, RenderTextureFormat.ARGBFloat) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                RenderTexture output = new(2, 2, 0, RenderTextureFormat.ARGBFloat) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                resources.Add(springState);
                resources.Add(output);
                material.SetTexture("_SiteSurfaceTex", sites);
                material.SetTexture("_SpringStateTex", springState);
                material.SetFloat("_SpikeSpringStrength", 100f);
                material.SetFloat("_SpikeSpringDamping", 0f);
                material.SetFloat("_DeltaTimeMilliseconds", 100f);
                material.SetFloat("_ParticleCount", 3f);
                material.SetFloat("_ParticleTextureSize", 2f);
                material.SetFloat("_SurfaceShapeMode", 1f);
                material.SetFloat("_SurfaceRoundness", 0.48f);
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
                material.SetFloat("_SpikeMoundCurvature", 1f);
                material.SetFloat("_MaximumHeight", 1.37f);
                material.SetFloat("_BulgeHeight", 0.1f);
                material.SetFloat("_BulgeRadius", 2f);
                material.SetFloat("_SphereBulgeScale", 0.5f);
                material.SetFloat("_PrimitiveSpikeTiltRadius", 2f);
                material.SetFloat("_MaximumTiltRadians", 0.5f);
                material.SetFloat("_TiltPeakRadiusMultiplier", 0.6f);
                material.SetFloat("_TiltReturnRadiusMultiplier", 1.2f);
                material.SetFloat("_TiltRiseExponent", 1f);
                material.SetFloat("_TiltReturnExponent", 2f);
                material.SetMatrix("_FerrofluidObjectToWorld", Matrix4x4.identity);
                material.SetMatrix("_FerrofluidWorldToObject", Matrix4x4.identity);

                Color[] RenderPass(int pass)
                {
                    Graphics.Blit(baseline, springState);
                    Graphics.Blit(springState, output, material, pass);
                    RenderTexture previous = RenderTexture.active;
                    Texture2D read = new(2, 2, TextureFormat.RGBAFloat, false, true);
                    try
                    {
                        RenderTexture.active = output;
                        read.ReadPixels(new Rect(0, 0, 2, 2), 0, 0);
                        read.Apply(false, false);
                        return read.GetPixels();
                    }
                    finally
                    {
                        RenderTexture.active = previous;
                        UnityEngine.Object.DestroyImmediate(read);
                    }
                }

                float[] Heights(float amplification, float strength, Vector4 magnet, float secondaryStrength = 0f, float gain = 1f)
                {
                    material.SetFloat("_MagnetHeightAmplification", amplification);
                    material.SetFloat("_MagneticFieldStrength", strength);
                    material.SetVector("_MagneticFieldPosition", magnet);
                    material.SetFloat("_SecondaryMagneticFieldStrength", secondaryStrength);
                    material.SetVector("_SecondaryMagneticFieldPosition", magnet);
                    material.SetFloat("_HeightGain", gain);
                    Color[] pixels = RenderPass(SpringPass);
                    float[] heights = { pixels[0].r, pixels[1].r, pixels[2].r };
                    foreach (float height in heights)
                    {
                        if (float.IsNaN(height) || float.IsInfinity(height))
                            failures.Add("height output must be finite");
                    }

                    return heights;
                }

                foreach (int shape in new[] { 1, 2 })
                {
                    material.SetFloat("_SurfaceShapeMode", shape);
                    Vector4 magnet = new(0.5f, 0.7f, 0f, 0f);
                    float[] rest = Heights(1f, 0.5f, magnet);
                    float[] amplified = Heights(1.6f, 0.5f, magnet);
                    Debug.Log($"[FerrofluidHeightAmplificationValidation] primary front {rest[0]:F4}->{amplified[0]:F4} back {rest[1]:F4}->{amplified[1]:F4} distant {rest[2]:F4}->{amplified[2]:F4}");
                    if (amplified[0] <= rest[0] + 0.001f)
                        failures.Add($"amplified front must grow ({rest[0]:F4} -> {amplified[0]:F4})");
                    if (amplified[1] >= rest[1] - 0.001f)
                        failures.Add($"amplified back must flatten ({rest[1]:F4} -> {amplified[1]:F4})");
                    if (Mathf.Abs(amplified[2] - rest[2]) > 0.00001f)
                        failures.Add($"distant site must stay unchanged ({rest[2]:F4} -> {amplified[2]:F4})");

                    float[] off = Heights(1.6f, 0f, magnet);
                    if (Mathf.Abs(off[0] - 0.44f) > 0.001f || Mathf.Abs(off[1] - 0.44f) > 0.001f)
                        failures.Add($"zero magnet strength must leave heights unchanged (front={off[0]:F4} back={off[1]:F4})");

                    float[] secondaryRest = Heights(1f, 0f, magnet, 0.5f);
                    float[] secondaryAmp = Heights(1.6f, 0f, magnet, 0.5f);
                    if (Mathf.Abs(secondaryAmp[0] - secondaryRest[0]) > 0.00001f || Mathf.Abs(secondaryAmp[1] - secondaryRest[1]) > 0.00001f)
                        failures.Add($"secondary magnet must not inherit the primary amplification ({secondaryRest[0]:F4}->{secondaryAmp[0]:F4})");

                    float[] cappedRest = Heights(1f, 1f, new Vector4(0f, 0.6f, 0f, 0f), 0f, 4f);
                    float[] cappedAmp = Heights(1.6f, 1f, new Vector4(0f, 0.6f, 0f, 0f), 0f, 4f);
                    Debug.Log($"[FerrofluidHeightAmplificationValidation] saturated front {cappedRest[0]:F4}->{cappedAmp[0]:F4}");
                    if (cappedAmp[0] <= cappedRest[0] + 0.001f)
                        failures.Add($"saturated amplified front must exceed the old cap ({cappedRest[0]:F4} -> {cappedAmp[0]:F4})");

                    ValidateCpuParity(material, sites.GetPixels(), shape, Heights, RenderPass, failures);
                    ValidateTriggerRadius(material, shape, Heights, RenderPass, failures);
                }

                // Two opposite outer sites and one inner site exercise the central all-sides effect.
                Texture2D torusState = new(2, 2, TextureFormat.RGBAFloat, false, true)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                resources.Add(torusState);
                torusState.SetPixels(new[]
                {
                    new Color(-1f, -1f, 0f, 0f),
                    new Color(-1f, 0f, 0f, 0f),
                    new Color(0f, -1f, 0f, 0f),
                    new Color(0f, 0f, 0f, 0f),
                });
                torusState.Apply(false, false);
                baseline.SetPixels(new[]
                {
                    new Color(0.18f, 0f, 0f, 0f),
                    new Color(0.18f, 0f, 0f, 0f),
                    new Color(0.18f, 0f, 0f, 0f),
                    new Color(0.18f, 0f, 0f, 0f),
                });
                baseline.Apply(false, false);
                material.SetFloat("_SurfaceShapeMode", 0f);
                material.SetTexture("_StateTex", torusState);
                material.SetVector("_HalfExtents", new Vector2(0.25f, 0.25f));
                material.SetVector("_RectSize", new Vector2(0.5f, 0.5f));
                material.SetFloat("_RectMinY", -0.25f);
                material.SetFloat("_SourceLocalScale", 1f);
                material.SetFloat("_CornerRadius", 0.05f);
                material.SetFloat("_RadialCenter", 0.2f);
                material.SetFloat("_RadialRadius", 0.05f);
                material.SetFloat("_DepthRadius", 0.03f);
                material.SetFloat("_ViewerLocalZSign", 1f);
                material.SetFloat("_MagnetOpeningStrength", 0.15f);
                material.SetVector("_MagnetOpeningNormal", Vector3.forward);
                material.SetVector("_MagnetOpeningPosition", Vector3.zero);
                float[] openingRest = Heights(1f, 0f, Vector4.zero);
                Color[] openingDirections = RenderPass(2);
                float[] openingAmplified = Heights(1.6f, 0f, Vector4.zero);
                Color[] amplifiedDirections = RenderPass(2);
                if (!(openingAmplified[0] > openingRest[0] + 0.001f
                    && openingAmplified[2] > openingRest[2] + 0.001f
                    && openingAmplified[1] < openingRest[1] - 0.001f))
                {
                    failures.Add($"central torus opening must amplify both opposite outer sites and inner flatten: {string.Join(",", openingRest)} -> {string.Join(",", openingAmplified)}");
                }

                for (int index = 0; index < 3; index++)
                {
                    if (!(Vector4.Distance(openingDirections[index], amplifiedDirections[index]) < 0.00001f))
                        failures.Add($"trigger must not change torus direction/apex at site {index}: {(Vector4)openingDirections[index]:F7} -> {(Vector4)amplifiedDirections[index]:F7}");
                }

                Debug.Log($"[FerrofluidHeightAmplificationValidation] torus opening {string.Join(",", openingRest)} -> {string.Join(",", openingAmplified)}");
            }
            catch (Exception exception)
            {
                failures.Add($"fixture error: {exception.GetType().Name} {exception.Message.Split('\n')[0]}");
            }
            finally
            {
                foreach (UnityEngine.Object resource in resources)
                {
                    if (resource != null)
                        UnityEngine.Object.DestroyImmediate(resource);
                }
            }

            if (failures.Count > 0)
                throw new Exception($"{failures.Count} height cases failed: {string.Join(" | ", failures)}");
            Debug.Log("[FerrofluidHeightAmplificationValidation] PASS: trigger height amplification verified.");
        }

        private static void ValidateCpuParity(
            Material material,
            Color[] sites,
            int shape,
            Func<float, float, Vector4, float, float, float[]> heights,
            Func<int, Color[]> renderPass,
            List<string> failures)
        {
            string repository = Directory.GetParent(Application.dataPath).Parent.FullName;
            Assembly plugin = Assembly.LoadFrom(Path.Combine(repository, "ChromaGLS/bin/Release-1.44.1/net48/ChromaGLS.dll"));
            Type deformation = plugin.GetType("ChromaGLS.Settings.FerrofluidPrimitiveDeformation", true);
            Type shapeType = plugin.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", true);
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            Type settingsType = deformation.GetNestedType("DeformationSettings", BindingFlags.NonPublic);
            object settings = Activator.CreateInstance(settingsType);
            foreach ((string field, string uniform) in new[]
            {
                ("Roundness", "_SurfaceRoundness"),
                ("BulgeRadius", "_BulgeRadius"),
                ("BulgeHeight", "_BulgeHeight"),
                ("SpikeMoundCurvature", "_SpikeMoundCurvature"),
                ("SpikeHeightRadius", "_PrimitiveSpikeHeightRadius"),
                ("SpikeTiltRadius", "_PrimitiveSpikeTiltRadius"),
                ("PeakWidth", "_PeakWidth"),
                ("DeadZoneRadius", "_MagnetHeightDeadZoneRadius"),
                ("HeightFalloffExponent", "_HeightFalloffExponent"),
                ("BackHeightLoss", "_BackHeightLoss"),
                ("BackHeightFalloffExponent", "_BackHeightFalloffExponent"),
                ("HeightStrengthCap", "_MagnetHeightStrengthCap"),
                ("MaximumHeight", "_MaximumHeight"),
                ("MaximumTiltRadians", "_MaximumTiltRadians"),
                ("TiltPeakRadiusMultiplier", "_TiltPeakRadiusMultiplier"),
                ("TiltReturnRadiusMultiplier", "_TiltReturnRadiusMultiplier"),
                ("TiltRiseExponent", "_TiltRiseExponent"),
                ("TiltReturnExponent", "_TiltReturnExponent"),
                ("SphereBulgeScale", "_SphereBulgeScale"),
                ("FlattenRadius", "_PrimitiveFlattenRadius"),
                ("FlattenExponent", "_PrimitiveFlattenExponent"),
            })
            {
                settingsType.GetField(field, flags).SetValue(settings, material.GetFloat(uniform));
            }

            object shapeValue = Enum.ToObject(shapeType, shape);
            MethodInfo evaluate = deformation.GetMethod("EvaluateSiteDeformation", flags);
            MethodInfo bulge = deformation.GetMethod("EvaluateSiteBulgeOffset", flags);
            foreach (bool saturated in new[] { false, true })
            {
                float gain = saturated ? 4f : 1f;
                float strength = saturated ? 1f : 0.5f;
                Vector4 magnet = saturated ? new Vector4(0f, 0.6f, 0f, 0f) : new Vector4(0.5f, 0.7f, 0f, 0f);
                settingsType.GetField("HeightGain", flags).SetValue(settings, gain);
                foreach (float amplification in new[] { 1f, 1.6f })
                {
                    float[] gpuHeights = heights(amplification, strength, magnet, 0f, gain);
                    Color[] directions = renderPass(2);
                    Vector3[] bulges = CaptureSurfaceBulges(material, sites);
                    for (int index = 0; index < 3; index++)
                    {
                        Vector4 site = sites[index];
                        object[] arguments = { settings, shapeValue, site, (Vector3)magnet, strength, 0f, amplification };
                        Vector4 cpu = (Vector4)evaluate.Invoke(null, arguments);
                        Vector3 cpuBulge = (Vector3)bulge.Invoke(
                            null,
                            new object[] { settings, shapeValue, site, (Vector3)magnet, strength, amplification });
                        if (!(Mathf.Abs(cpu.w - gpuHeights[index]) <= 0.0001f))
                            failures.Add($"shape={shape} site={index} amp={amplification} saturated={saturated} CPU/GPU height mismatch {cpu.w}/{gpuHeights[index]}");
                        if (!(Vector3.Distance((Vector3)cpu, (Vector3)(Vector4)directions[index]) <= 0.0001f
                            && Mathf.Abs((float)arguments[5] - directions[index].a) <= 0.0001f))
                        {
                            failures.Add($"shape={shape} site={index} CPU/GPU direction/apex mismatch");
                        }

                        if (!(Vector3.Distance(cpuBulge, bulges[index]) <= 0.0001f))
                            failures.Add($"shape={shape} site={index} amp={amplification} CPU/GPU bulge mismatch {cpuBulge:F6}/{bulges[index]:F6}");
                    }
                }
            }

            Debug.Log($"[FerrofluidHeightAmplificationValidation] shape={shape} CPU/GPU heights, tilt, apex and bulge compared at amplification 1/1.6");
        }

        private static void ValidateTriggerRadius(
            Material material,
            int shape,
            Func<float, float, Vector4, float, float, float[]> heights,
            Func<int, Color[]> renderPass,
            List<string> failures)
        {
            string[] names = { "_PrimitiveSpikeHeightRadius", "_PrimitiveSpikeTiltRadius", "_PeakWidth", "_MagnetHeightDeadZoneRadius" };
            float[] previous = new float[names.Length];
            float[] values = { 0.31f, 0.73f, 0.4f, 0.03f };
            for (int index = 0; index < names.Length; index++)
            {
                previous[index] = material.GetFloat(names[index]);
                material.SetFloat(names[index], values[index]);
            }

            try
            {
                Vector3 site = new(0f, 0.5f, 0f);
                Vector3 delta = new(0.24f, 0.18f, 0f);
                float baseline = heights(1f, 0.5f, site + delta, 0f, 1f)[0];
                Color direction = renderPass(2)[0];
                foreach (float amplification in new[] { 1f, 1.3f, 1.6f })
                {
                    float radius = 1f + ((amplification - 1f) / 0.6f);
                    Vector3 expandedMagnet = site + (delta * radius);
                    float expanded = heights(amplification, 0.5f, expandedMagnet, 0f, 1f)[0];
                    Color expandedDirection = renderPass(2)[0];
                    float expected = 0.44f + (amplification * (baseline - 0.44f));
                    float directionError = Vector4.Distance(direction, expandedDirection);
                    Debug.Log($"[FerrofluidHeightAmplificationValidation] shape={shape} radiusScale={radius:F2} height={expanded:F6} expected={expected:F6} directionError={directionError:F6}");
                    if (Mathf.Abs(expanded - expected) > 0.0001f || directionError > 0.0001f)
                        failures.Add($"shape={shape}: trigger does not expand the height and tilt footprint by {radius:F2} while preserving its profile");
                }

                heights(1f, 0f, site + delta, 0.5f, 1f);
                Color[] secondary = renderPass(2);
                heights(1.6f, 0f, site + delta, 0.5f, 1f);
                Color[] secondaryHeld = renderPass(2);
                for (int index = 0; index < 3; index++)
                {
                    if (Vector4.Distance(secondary[index], secondaryHeld[index]) > 0.00001f)
                        failures.Add($"shape={shape}: trigger expands the secondary magnet's tilt footprint");
                }
            }
            finally
            {
                for (int index = 0; index < names.Length; index++)
                    material.SetFloat(names[index], previous[index]);
            }
        }

        private static Vector3[] CaptureSurfaceBulges(Material material, Color[] sites)
        {
            Texture previousHeight = material.GetTexture("_HeightMapTex");
            float previousSpikes = material.GetFloat("_SpikesEnabled");
            Vector4 previousYaw = material.GetVector("_PrimitiveYawCosSin");
            try
            {
                // With spikes and density disabled, the production position pass isolates magnetic support.
                material.SetTexture("_HeightMapTex", Texture2D.blackTexture);
                material.SetFloat("_SpikesEnabled", 0f);
                material.SetVector("_PrimitiveYawCosSin", new Vector2(1f, 0f));
                Vector2[] uv = { new(0.5f, 0.5f), Vector2.zero, new(1f, 0.5f) };
                Color[] positions = FerrofluidSurfaceProbe.Sample(material, uv, 0);
                Vector3[] bulges = new Vector3[uv.Length];
                for (int index = 0; index < uv.Length; index++)
                    bulges[index] = (Vector3)(Vector4)positions[index] - (Vector3)(Vector4)sites[index];

                return bulges;
            }
            finally
            {
                material.SetTexture("_HeightMapTex", previousHeight);
                material.SetFloat("_SpikesEnabled", previousSpikes);
                material.SetVector("_PrimitiveYawCosSin", previousYaw);
            }
        }
    }
}
