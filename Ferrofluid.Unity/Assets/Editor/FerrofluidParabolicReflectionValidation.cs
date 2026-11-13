using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    // Independent colour quadrants catch world-XZ flips and position-dependent sampling errors.
    internal static class FerrofluidParabolicReflectionValidation
    {
        private const string TextureId = "_FerrofluidParabolaReflection";
        private const string BoundsId = "_FerrofluidParabolaReflectionBounds";
        private const string PlaneYId = "_FerrofluidParabolaReflectionY";
        private const string EnabledId = "_FerrofluidParabolaReflectionEnabled";

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidParabolicReflectionValidation));

        private static void RunRuntime()
        {
            Texture savedTexture = Shader.GetGlobalTexture(TextureId);
            Vector4 savedBounds = Shader.GetGlobalVector(BoundsId);
            float savedPlaneY = Shader.GetGlobalFloat(PlaneYId);
            float savedEnabled = Shader.GetGlobalFloat(EnabledId);
            Texture savedRoom = Shader.GetGlobalTexture("_FerrofluidRoomEnvironment");
            Texture savedFloor = Shader.GetGlobalTexture("_FerrofluidFloorReflection");
            Vector4 savedFloorBounds = Shader.GetGlobalVector("_FerrofluidFloorReflectionBounds");
            float savedFloorY = Shader.GetGlobalFloat("_FerrofluidFloorReflectionY");
            bool roomKeyword = Shader.IsKeywordEnabled("FERROFLUID_ROOM_REFLECTIONS");
            RenderTexture savedActive = RenderTexture.active;
            List<Object> owned = new();
            try
            {
                Texture2D marker = new(64, 64, TextureFormat.RGBAHalf, false, true);
                owned.Add(marker);
                Color[] pixels = new Color[64 * 64];
                Color[] quadrants = { new(2.5f, 0f, 0f), new(0f, 2.5f, 0f), new(0f, 0f, 2.5f), new(2.5f, 2.5f, 2.5f) };
                for (int index = 0; index < pixels.Length; index++)
                    pixels[index] = quadrants[((index % 64) / 32) + (((index / 64) / 32) * 2)];
                marker.SetPixels(pixels);
                marker.Apply();
                Shader.SetGlobalTexture(TextureId, marker);
                Shader.SetGlobalVector(BoundsId, new Vector4(-2f, -2f, 0.25f, 0.25f));
                Shader.SetGlobalFloat(PlaneYId, 0.006f);
                Shader.SetGlobalFloat(EnabledId, 1f);

                Cubemap room = new(16, TextureFormat.RGBAHalf, false);
                owned.Add(room);
                for (int face = 0; face < 6; face++)
                {
                    Color[] black = new Color[16 * 16];
                    room.SetPixels(black, (CubemapFace)face);
                }
                room.Apply(true);
                Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", room);
                Shader.SetGlobalTexture("_FerrofluidFloorReflection", Texture2D.blackTexture);
                Shader.SetGlobalVector("_FerrofluidFloorReflectionBounds", Vector4.zero);
                Shader.SetGlobalFloat("_FerrofluidFloorReflectionY", 0f);
                Shader.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");

                Material material = new(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader"));
                owned.Add(material);
                material.SetTexture("_EnvironmentTex", Texture2D.blackTexture);
                material.SetTexture("_ThinFilmTex", Texture2D.blackTexture);
                material.SetFloat("_FadeAlpha", 1f);
                material.SetFloat("_Exposure", 1f);
                material.SetFloat("_EmissionStrength", 0f);
                material.SetFloat("_PrimitiveAmbientScale", 0f);
                material.SetFloat("_MatteSheen", 0f);
                material.SetFloat("_Reflectivity", 1f);
                material.SetFloat("_ReflectionAngle", 90f);
                material.SetVector("_ReflectionRotation", Vector3.zero);
                material.SetVector("_ValidationWorldOffset", Vector3.zero);
                material.SetFloat("_SurfaceShapeMode", 1f);

                Vector3 origin = new(0.2f, 1.5f, 0.1f);
                Vector3 markerPoint = new(-0.5f, 0.006f, -1.5f);
                material.SetVector("_ValidationWorldPosition", origin);
                Vector3 direction = (markerPoint - origin).normalized;
                Color sampled = FerrofluidEnvironmentReflectionValidation.Sample(material, Vector3.forward, direction);
                Debug.Log($"[FerrofluidParabolicReflectionValidation] marker=({markerPoint.x},{markerPoint.z}) ray=({direction.x:F3},{direction.y:F3},{direction.z:F3}) sampled={sampled:F4} expected=red>1.5");
                if (sampled.r <= 1.5f || sampled.g > 0.01f || sampled.b > 0.01f)
                    throw new Exception($"Parabolic floor reflection must add the HDR red marker at the specular plane; sampled {sampled:F4} expected red>1.5 others~0.");

                foreach (float shape in new[] { 0f, 1f, 2f })
                {
                    material.SetFloat("_SurfaceShapeMode", shape);
                    foreach (Vector3 position in new[] { origin, new Vector3(-0.8f, 0.65f, 0.2f), new Vector3(0.8f, 2f, -0.5f) })
                    {
                        material.SetVector("_ValidationWorldPosition", position);
                        for (int quadrant = 0; quadrant < 4; quadrant++)
                        {
                            Vector3 hit = new(quadrant % 2 == 0 ? -1f : 1f, 0.006f, quadrant < 2 ? -1f : 1f);
                            Color actual = FerrofluidEnvironmentReflectionValidation.Sample(material, Vector3.forward, (hit - position).normalized);
                            for (int channel = 0; channel < 3; channel++)
                            {
                                if (Mathf.Abs(actual[channel] - quadrants[quadrant][channel]) > 0.05f)
                                    throw new Exception($"Shape {shape} at {position} must reflect quadrant {quadrant}, got {actual:F4}.");
                            }
                        }
                    }
                }

                foreach ((Vector3 position, Vector3 ray) in new[]
                {
                    (origin, Vector3.up),
                    (origin, Vector3.right),
                    (new Vector3(0f, -1f, 0f), Vector3.down),
                    (new Vector3(8f, 1f, 8f), Vector3.down),
                })
                {
                    material.SetVector("_ValidationWorldPosition", position);
                    Color missed = FerrofluidEnvironmentReflectionValidation.Sample(material, Vector3.forward, ray);
                    if (missed.maxColorComponent > 0.01f)
                        throw new Exception($"Invalid planar hit from {position} along {ray} must not reflect the patch: {missed:F4}.");
                }

                Shader.SetGlobalFloat(EnabledId, 0f);
                material.SetVector("_ValidationWorldPosition", origin);
                Color disabled = FerrofluidEnvironmentReflectionValidation.Sample(material, Vector3.forward, direction);
                if (disabled.maxColorComponent > 0.01f)
                    throw new Exception($"Disabled cache must not contribute: {disabled:F4}.");

                Debug.Log("[FerrofluidParabolicReflectionValidation] PASS");
            }
            finally
            {
                Shader.SetGlobalTexture(TextureId, savedTexture);
                Shader.SetGlobalVector(BoundsId, savedBounds);
                Shader.SetGlobalFloat(PlaneYId, savedPlaneY);
                Shader.SetGlobalFloat(EnabledId, savedEnabled);
                Shader.SetGlobalTexture("_FerrofluidRoomEnvironment", savedRoom);
                Shader.SetGlobalTexture("_FerrofluidFloorReflection", savedFloor);
                Shader.SetGlobalVector("_FerrofluidFloorReflectionBounds", savedFloorBounds);
                Shader.SetGlobalFloat("_FerrofluidFloorReflectionY", savedFloorY);
                if (roomKeyword)
                    Shader.EnableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                else
                    Shader.DisableKeyword("FERROFLUID_ROOM_REFLECTIONS");
                RenderTexture.active = savedActive;
                foreach (Object item in owned)
                    Object.DestroyImmediate(item);
            }
        }
    }
}
