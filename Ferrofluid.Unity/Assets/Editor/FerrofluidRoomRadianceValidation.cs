using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidRoomRadianceValidation
    {
        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidRoomRadianceValidation));

        private static void RunRuntime()
        {
            Material probe = new(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidBakedProbe.shader"));
            RenderTexture target = new(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            Texture2D read = new(1, 1, TextureFormat.RGBAFloat, false, true);
            try
            {
                List<string> failures = new();
                Color[] halo = new Color[2];
                target.Create();
                probe.SetFloat("_FaceMip", 0f);
                foreach (string state in new[] { "on", "off" })
                {
                    Cubemap room = AssetDatabase.LoadAssetAtPath<Cubemap>($"Assets/Generated/room-reflections-{state}.cubemap");
                    if (room == null)
                        throw new Exception($"The full room {state} cubemap is missing.");

                    probe.SetTexture("_Cube", room);
                    float largestSeamStep = 0f;
                    for (int axes = 0; axes < 3; axes++)
                    {
                        for (int signs = 0; signs < 4; signs++)
                        {
                            for (int sample = 0; sample < 33; sample++)
                            {
                                float t = Mathf.Lerp(-0.8f, 0.8f, sample / 32f);
                                Color left = Sample(SeamDirection(axes, signs, t, -1f / room.width));
                                Color right = Sample(SeamDirection(axes, signs, t, 1f / room.width));
                                for (int channel = 0; channel < 3; channel++)
                                {
                                    float step = Mathf.Abs(right[channel] - left[channel]);
                                    if (step > largestSeamStep)
                                    {
                                        largestSeamStep = step;
                                        Debug.Log($"[FerrofluidRoomRadianceValidation] largest candidate axes={axes} signs={signs} t={t:R} channel={channel} left={left:R} right={right:R}.");
                                    }
                                }
                            }
                        }
                    }

                    Debug.Log($"[FerrofluidRoomRadianceValidation] {state} largest face-boundary RGB step={largestSeamStep:R}.");
                    if (largestSeamStep > 0.01f)
                        throw new Exception($"Room {state} has a hard capture-face boundary: linear RGB step {largestSeamStep:R} exceeds 0.01 across two neighbouring texels.");

                    Color background = new Color(0.0221862793f, 0.0564270020f, 0.0800781250f, 1f) * 0.15f;
                    background.a = 1f;
                    foreach (Vector3 direction in new[] { new Vector3(0f, 0.5f, -1f), new Vector3(-0.5f, 0.5f, -1f), new Vector3(0.5f, 0.5f, -1f) })
                    {
                        Color actual = Sample(direction);
                        if (Mathf.Abs(actual.r - background.r) > 0.002f
                            || Mathf.Abs(actual.g - background.g) > 0.002f
                            || Mathf.Abs(actual.b - background.b) > 0.002f)
                            failures.Add($"Room {state} must retain only 15% of the captured teal backdrop at {direction}: {actual}, expected {background}.");
                    }

                    Color horizon = Sample(Vector3.forward) - background;
                    Debug.Log($"[FerrofluidRoomRadianceValidation] {state} horizon without backdrop={horizon:F6}.");
                    if (horizon.b <= 4f * horizon.r || horizon.g <= 2f * horizon.r)
                        failures.Add($"Room {state} horizon needs the native blue hue, not a neutral white band: {horizon}.");

                    // Five degrees above the E is outside the letter sprite but inside its coloured bloom.
                    halo[state == "on" ? 0 : 1] = Sample(new Vector3(-1.19f, 20.5f, 26.71f));
                    if (state == "on")
                    {
                        Color sign = Sample(new Vector3(-1.19f, 17.20f, 26.71f));
                        if (sign.r < 0.1f)
                            throw new Exception($"Room {state} lost the captured red sign radiance: {sign}.");
                    }
                }

                Color haloDelta = halo[0] - halo[1];
                Debug.Log($"[FerrofluidRoomRadianceValidation] E halo on={halo[0]:F6} off={halo[1]:F6} delta={haloDelta:F6}.");
                if (haloDelta.r <= 0.005f || haloDelta.g > haloDelta.r * 0.25f || haloDelta.b > haloDelta.r * 0.25f)
                    failures.Add($"The E needs red bloom outside its letter sprite, not only a brighter core: {haloDelta}.");
                if (Mathf.Abs(haloDelta.r - 0.59765625f) > 0.02f)
                    failures.Add($"The approved E bloom must retain its on/off red delta of 0.59765625: {haloDelta.r}.");

                if (failures.Count > 0)
                    throw new Exception(string.Join(" | ", failures));

                Debug.Log("[FerrofluidRoomRadianceValidation] PASS: continuous cube faces, captured teal backdrop and coloured neon bloom.");

                Color Sample(Vector3 direction)
                {
                    probe.SetVector("_Direction", direction);
                    Graphics.Blit(null, target, probe);
                    RenderTexture.active = target;
                    read.ReadPixels(new Rect(0f, 0f, 1f, 1f), 0, 0);
                    read.Apply();
                    return read.GetPixel(0, 0);
                }
            }
            finally
            {
                RenderTexture.active = previous;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(probe);
                Object.DestroyImmediate(read);
            }
        }

        private static Vector3 SeamDirection(int axes, int signs, float t, float epsilon)
        {
            float a = signs % 2 == 0 ? -1f : 1f;
            float b = signs < 2 ? -1f : 1f;
            if (axes == 0)
                return new Vector3(a * (1f + epsilon), b * (1f - epsilon), t);
            if (axes == 1)
                return new Vector3(a * (1f + epsilon), t, b * (1f - epsilon));

            return new Vector3(t, a * (1f + epsilon), b * (1f - epsilon));
        }
    }
}
