using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEditor;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    // Production-API cadence: SetPrimitiveMotion + AdvancePrimitiveSimulation must consume a
    // pending impulse across substeps without latching, repeating, or losing the delta.
    internal static class FerrofluidMomentumCadenceValidation
    {
        internal static void Validate(List<UnityEngine.Object> resources)
        {
            string repository = Directory.GetParent(Application.dataPath).Parent.FullName;
            Assembly plugin = Assembly.LoadFrom(Path.Combine(repository, "ChromaGLS/bin/Release-1.44.1/net48/ChromaGLS.dll"));
            Type gpuSimType = plugin.GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", true);
            Type configType = plugin.GetType("ChromaGLS.Settings.Config", true);
            Type shapeType = plugin.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", true);
            object config = Activator.CreateInstance(configType);
            foreach (int steps in new[] { 1, 2, 4 })
            {
                configType.GetProperty("SimulationSteps").SetValue(config, steps);
                float terminal = RunImpulse(gpuSimType, config, shapeType, resources, Vector3.up * 0.05f, Quaternion.identity, 1f);
                Assert(
                    Mathf.Abs(terminal - (-0.014f)) < 0.001f,
                    $"SimulationSteps={steps} must land the same terminal velocity ({terminal:F4})");
            }

            Quaternion bodyRotation = Quaternion.Euler(20f, 37f, 11f);
            float scaled = RunImpulse(gpuSimType, config, shapeType, resources, bodyRotation * new Vector3(0f, 0.05f, 0f), bodyRotation, 0.35f);
            Assert(
                Mathf.Abs(scaled - (-0.014f)) < 0.001f,
                $"world impulse under rotation+scale must land the same velocity ({scaled:F4})");
        }

        private static float RunImpulse(
            Type gpuSimType,
            object config,
            Type shapeType,
            List<UnityEngine.Object> resources,
            Vector3 pendingVelocityDelta,
            Quaternion rotation,
            float uniformScale)
        {
            object simulation = FormatterServices.GetUninitializedObject(gpuSimType);
            FieldInfo field(string name) => gpuSimType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            GameObject holder = new("cadence renderer");
            resources.Add(holder);
            MeshRenderer renderer = holder.AddComponent<MeshRenderer>();
            holder.transform.rotation = rotation;
            holder.transform.localScale = Vector3.one * uniformScale;
            field("_renderer").SetValue(simulation, renderer);
            field("_shape").SetValue(simulation, Enum.Parse(shapeType, "RoundedCube"));
            field("_config").SetValue(simulation, config);
            field("_primitiveHistoryValid").SetValue(simulation, false);
            field("_simulationAccumulator").SetValue(simulation, 0f);
            field("_lastSimulationAt").SetValue(simulation, 0f);
            field("_renderEnabled").SetValue(simulation, true);

            Material simMaterial = new(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSimulation.shader"));
            Material surfaceMaterial = new(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader"));
            resources.Add(simMaterial);
            resources.Add(surfaceMaterial);
            simMaterial.SetInt("_ParticleCount", 1);
            simMaterial.SetInt("_ParticleTextureSize", 1);
            simMaterial.SetFloat("_SurfaceShapeMode", 1f);
            simMaterial.SetFloat("_SurfaceRoundness", 0.48f);
            simMaterial.SetVector("_PrimitiveYawCosSin", new Vector2(0.798636f, 0.601815f));
            simMaterial.SetVector("_DomainScale", new Vector2(15.36f, 15.36f));
            simMaterial.SetFloat("_H", 1f);
            simMaterial.SetFloat("_HSQ", 1f);
            simMaterial.SetFloat("_Poly6", 315f / (64f * Mathf.PI));
            simMaterial.SetFloat("_MaximumSlide", 4.1f);
            simMaterial.SetFloat("_PrimitiveSpikeSpacing", 0.16f);
            simMaterial.SetFloat("_Elasticity", 0f);
            simMaterial.SetFloat("_Damping", 0f);
            simMaterial.SetFloat("_GasConstant", 0f);
            simMaterial.SetFloat("_Viscosity", 0f);
            simMaterial.SetFloat("_Mass", 1f);
            simMaterial.SetMatrix("_PrimitiveFrameRotation", Matrix4x4.identity);
            simMaterial.SetVector("_PrimitiveLinearVelocityDelta", Vector4.zero);
            simMaterial.SetVector("_PrimitivePreviousAngularVelocity", Vector4.zero);
            simMaterial.SetVector("_PrimitiveAngularVelocity", Vector4.zero);
            field("_simulationMaterial").SetValue(simulation, simMaterial);
            field("_surfaceMaterial").SetValue(simulation, surfaceMaterial);

            Texture2D rest = new(1, 1, TextureFormat.RGBAFloat, false, true);
            // A side site away from chart folds makes the vertical impulse tangential.
            rest.SetPixel(0, 0, new Color(0.5f / 0.55f, 0.03f / 0.55f, 0f, 0f));
            rest.Apply(false, false);
            resources.Add(rest);
            field("_restPositionTexture").SetValue(simulation, rest);
            field("_particleCount").SetValue(simulation, 1);
            field("_particleTextureSize").SetValue(simulation, 1);
            RenderTexture stateA = MakeRt(1, resources);
            RenderTexture stateB = MakeRt(1, resources);
            Graphics.Blit(rest, stateA);
            field("_stateA").SetValue(simulation, stateA);
            field("_stateB").SetValue(simulation, stateB);
            field("_currentState").SetValue(simulation, stateA);
            field("_nextState").SetValue(simulation, stateB);
            field("_densityPressure").SetValue(simulation, MakeRt(1, resources));
            field("_force").SetValue(simulation, MakeRt(1, resources));
            RenderTexture restSurface = MakeRt(1, resources);
            RenderTexture siteSurface = MakeRt(1, resources);
            field("_restSurface").SetValue(simulation, restSurface);
            field("_siteSurface").SetValue(simulation, siteSurface);
            field("_heightMap").SetValue(simulation, MakeRt(32, resources));
            field("_coarseCandidates").SetValue(simulation, MakeRt(4, resources));
            field("_springCurrent").SetValue(simulation, MakeRt(1, resources));
            field("_springNext").SetValue(simulation, MakeRt(1, resources));
            simMaterial.SetTexture("_StateTex", stateA);
            simMaterial.SetTexture("_DensityPressureTex", Texture2D.whiteTexture);
            simMaterial.SetTexture("_ForceTex", Texture2D.blackTexture);
            simMaterial.SetTexture("_RestPositionTex", rest);
            Graphics.Blit(Texture2D.blackTexture, restSurface, simMaterial, 6);
            simMaterial.SetTexture("_RestSurfaceTex", restSurface);
            Graphics.Blit(Texture2D.blackTexture, siteSurface, simMaterial, 4);
            simMaterial.SetTexture("_SiteSurfaceTex", siteSurface);

            MethodInfo setMotion = gpuSimType.GetMethod("SetPrimitiveMotion", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo advance = gpuSimType.GetMethod("AdvancePrimitiveSimulation", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            setMotion.Invoke(simulation, new object[] { Vector3.zero, Vector3.zero });
            setMotion.Invoke(simulation, new object[] { pendingVelocityDelta, Vector3.zero });
            advance.Invoke(simulation, new object[] { 0f });
            Assert(
                ((Vector3)field("_previousPrimitiveLinearVelocity").GetValue(simulation)).sqrMagnitude == 0f,
                "a zero-second advance must not consume the pending impulse");
            Assert(ReferenceEquals(field("_currentState").GetValue(simulation), stateA), "zero elapsed time must not integrate");

            advance.Invoke(simulation, new object[] { 0.5f / 120f });
            Assert(ReferenceEquals(field("_currentState").GetValue(simulation), stateA), "a fractional tick must retain pending momentum");
            advance.Invoke(simulation, new object[] { 0.5f / 120f });
            Vector3 firstVelocity = DecodeVelocity(gpuSimType, simulation, shapeType);
            Assert(
                Mathf.Abs((firstVelocity.y * uniformScale) + 0.014f) < 0.001f,
                $"one tick must consume the full body impulse ({firstVelocity * uniformScale})");
            advance.Invoke(simulation, new object[] { 1f / 120f });
            Vector3 secondVelocity = DecodeVelocity(gpuSimType, simulation, shapeType);
            Assert(
                Vector3.Distance(firstVelocity, secondVelocity) * uniformScale < 0.001f,
                "the next tick must preserve momentum without applying the body impulse again");
            Debug.Log($"[FerrofluidMomentumCadenceValidation] steps={config.GetType().GetProperty("SimulationSteps").GetValue(config)} scale={uniformScale} localVelocity={secondVelocity:F6}");
            return secondVelocity.y * uniformScale;
        }

        private static Vector3 DecodeVelocity(Type simulationType, object simulation, Type shapeType)
        {
            RenderTexture current = (RenderTexture)simulationType
                .GetField("_currentState", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(simulation);
            Color state = Read(current)[0];
            Type environment = simulationType.Assembly.GetType("ChromaGLS.Settings.FerrofluidEnvironmentObjects", true);
            MethodInfo evaluate = environment.GetMethod("EvaluateSurfacePosition", BindingFlags.Static | BindingFlags.NonPublic);
            object shape = Enum.Parse(shapeType, "RoundedCube");
            Vector2 uv = new((state.r * 0.5f) + 0.5f, (state.g * 0.5f) + 0.5f);
            const float epsilon = 0.0001f;
            Vector3 at(Vector2 sample) => (Vector3)evaluate.Invoke(null, new object[] { shape, sample, 0.48f, 37f });
            Vector3 jx = (at(uv + (Vector2.right * epsilon)) - at(uv - (Vector2.right * epsilon))) / (4f * epsilon);
            Vector3 jy = (at(uv + (Vector2.up * epsilon)) - at(uv - (Vector2.up * epsilon))) / (4f * epsilon);
            return (jx * state.b) + (jy * state.a);
        }

        private static RenderTexture MakeRt(int size, List<UnityEngine.Object> resources)
        {
            RenderTexture texture = new(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                filterMode = FilterMode.Point,
            };
            texture.Create();
            resources.Add(texture);
            return texture;
        }

        private static Color[] Read(RenderTexture source)
        {
            RenderTexture active = RenderTexture.active;
            RenderTexture.active = source;
            Texture2D output = new(source.width, source.height, TextureFormat.RGBAFloat, false, true);
            output.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
            output.Apply();
            RenderTexture.active = active;
            Color[] pixels = output.GetPixels();
            UnityEngine.Object.DestroyImmediate(output);
            return pixels;
        }

        private static void Assert(bool condition, string what)
        {
            if (!condition)
                throw new Exception(what);
        }
    }
}
