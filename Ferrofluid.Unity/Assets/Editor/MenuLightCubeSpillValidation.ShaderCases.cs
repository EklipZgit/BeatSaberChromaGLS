using System;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    internal static partial class MenuLightCubeSpillValidation
    {
        private static void ValidateShaderCases(
            Material material,
            Camera camera,
            RenderTexture target,
            Texture2D readback,
            Mesh mesh,
            Vector3 point,
            Vector3 normal)
        {
            Vector4[] positions = new Vector4[32];
            Vector4[] energy = new Vector4[32];
            Vector4[] normals = new Vector4[32];
            positions[0] = new Vector4(0, 1, -2, 1);
            energy[0] = new Vector4(100, 150, 200, 0);
            normals[0] = Vector3.up;
            Check("facing", 1);

            positions[0] = new Vector4(0, 1, 2, 1);
            Check("away", 1);
            positions[0] = new Vector4(0, 2, -4, 1);
            Check("farther source", 1);
            positions[0] = new Vector4(1, 1, -2, 1);
            normals[0] = Vector3.right;
            Check("rotated emitter", 1);
            positions[0] = new Vector4(0, 1, -2, 1);
            normals[0] = Vector3.up;
            Check("count disabled", 0);
            energy[0] = new Vector4(100, 0, 0, 0);
            positions[1] = new Vector4(1, 1, -2, 1);
            energy[1] = new Vector4(0, 150, 200, 0);
            normals[1] = Vector3.up;
            Check("two colored sources", 2);
            for (int index = 0; index < 32; index++)
            {
                positions[index] = new Vector4(0, 1, -2, 1);
                energy[index] = new Vector4(1, 2, 3, 0);
                normals[index] = Vector3.up;
            }

            Check("all 32 source slots", 32);
            energy[0] = new Vector4(100, 150, 200, 0);
            Vector3 originalPosition = camera.transform.position;
            Matrix4x4 originalProjection = camera.projectionMatrix;
            try
            {
                camera.transform.position += new Vector3(.25f, 0, 0);
                camera.projectionMatrix = Matrix4x4.Ortho(-.75f, .25f, -.5f, .5f, .01f, 10f);
                float coordinate = (SamplePixel + .5f) / Resolution;
                (Vector3 movedPoint, Vector3 movedNormal) = RayMesh(camera.ViewportPointToRay(new Vector3(coordinate, coordinate, 0)), mesh);
                if ((point - movedPoint).sqrMagnitude > 1e-10f || Vector3.Dot(normal, movedNormal) < .99999f)
                    throw new InvalidOperationException("The camera-stability fixture must sample the same world point and normal.");

                Check("moved camera, fixed surface point", 1);
            }
            finally
            {
                camera.transform.position = originalPosition;
                camera.projectionMatrix = originalProjection;
            }

            string[] fogNames = { "_CustomFogAttenuation", "_CustomFogOffset", "_CustomFogHeightFogStartY", "_CustomFogHeightFogHeight" };
            float[] previous = new float[fogNames.Length];
            for (int index = 0; index < fogNames.Length; index++)
                previous[index] = Shader.GetGlobalFloat(fogNames[index]);

            try
            {
                material.SetFloat("_MenuLightUseFog", 1);
                Shader.SetGlobalFloat(fogNames[0], .5f);
                Shader.SetGlobalFloat(fogNames[1], 0);
                float distanceSquared = (point - camera.transform.position).sqrMagnitude;
                float clear = 1f / ((Mathf.Max(((distanceSquared - 1f) * .08f), 0f) * .5f) + 1f);
                Check("native distance fog", 1, clear);
                material.SetFloat("_MenuLightUseHeightFog", 1);
                Shader.SetGlobalFloat(fogNames[2], -1.5f);
                Shader.SetGlobalFloat(fogNames[3], 1f);
                float height = Mathf.Clamp01(point.y + .5f);
                Check("native partial height fog", 1, clear * height * height * (3f - (2f * height)));
                Shader.SetGlobalFloat(fogNames[2], 0f);
                Check("native full height fog", 1, 0);
            }
            finally
            {
                material.SetFloat("_MenuLightUseFog", 0);
                material.SetFloat("_MenuLightUseHeightFog", 0);
                for (int index = 0; index < fogNames.Length; index++)
                    Shader.SetGlobalFloat(fogNames[index], previous[index]);
            }

            void Check(string name, int count, float clear = 1f)
            {
                material.SetVectorArray("_MenuLightSpillPositions", positions);
                material.SetVectorArray("_MenuLightSpillEnergy", energy);
                material.SetVectorArray("_MenuLightSpillNormals", normals);
                material.SetInt("_MenuLightSpillCount", count);
                Vector3 expected = Vector3.zero;
                for (int index = 0; index < count; index++)
                    expected += Expected(point, normal, positions[index], positions[index].w, energy[index], normals[index]);

                expected *= clear;
                Color actual = ReadCenter(camera, target, readback);
                Debug.Log($"[MenuLightCubeSpill] {name}: expectedRGB={expected:F8} actualRGBA={actual:F8}");
                AssertColor(actual, expected, name);
            }
        }
    }
}
