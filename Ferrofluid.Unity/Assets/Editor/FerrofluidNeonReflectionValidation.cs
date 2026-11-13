using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidNeonReflectionValidation
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string RoomTexture = "_FerrofluidRoomEnvironment";
        private const string RoomKeyword = "FERROFLUID_ROOM_REFLECTIONS";

        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidNeonReflectionValidation));

        private static void RunRuntime()
        {
            List<Object> owned = new();
            Texture previous = Shader.GetGlobalTexture(RoomTexture);
            bool wasEnabled = Shader.IsKeywordEnabled(RoomKeyword);
            Assembly plugin = FerrofluidValidationRuntime.CreateConfig().GetType().Assembly;
            Type provider = plugin.GetType("ChromaGLS.Settings.FerrofluidRoomReflections", true);
            MethodInfo end = provider.GetMethod("EndMenuSession", Static);
            object harmony = null;
            Type harmonyType = null;
            Camera.CameraCallback afterRender = null;
            try
            {
                end.Invoke(null, null);
                Material red = Own(new Material(Shader.Find("Unlit/Color")) { color = Color.red });
                Material black = Own(new Material(Shader.Find("Unlit/Color")) { color = Color.black });
                Texture2D texture = Own(new Texture2D(4, 4));
                Color[] pixels = new Color[16];
                Array.Fill(pixels, Color.white);
                texture.SetPixels(pixels);
                texture.Apply();
                Sprite sprite = Own(Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), Vector2.one * 0.5f, 1f));
                GameObject letter = Own(new GameObject("Neon E", typeof(SpriteRenderer)));
                SpriteRenderer renderer = letter.GetComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sharedMaterial = black;
                renderer.color = Color.black;

                MethodInfo apply = provider.GetMethod("ApplyEnvironment", Static);
                Type signType = apply.GetParameters()[2].ParameterType;
                GameObject signObject = Own(new GameObject("Inactive native sign"));
                signObject.SetActive(false);
                Component sign = signObject.AddComponent(signType);
                Set(sign, "_flickeringSprite", renderer);
                Type lightType = signType.GetField("_light", Instance).FieldType;
                GameObject lightObject = Own(new GameObject("Inactive native tube"));
                lightObject.SetActive(false);
                Component light = lightObject.AddComponent(lightType);
                Type boolType = lightType.GetField("_mainEffectPostProcessEnabled", Instance).FieldType;
                ScriptableObject postProcess = Own(ScriptableObject.CreateInstance(boolType));
                Set(light, "_mainEffectPostProcessEnabled", postProcess);
                lightType.GetProperty("color").SetValue(light, Color.black);
                Set(sign, "_light", light);
                Set(sign, "_onMaterial", red);
                Set(sign, "_offMaterial", black);
                Set(sign, "_spriteOnColor", Color.red);
                Set(sign, "_lightOnColor", Color.red);
                Set(sign, "_particleSystems", Array.Empty<ParticleSystem>());
                Set(sign, "_sparksAudioClips", Array.Empty<AudioClip>());
                bool dirty = (bool)lightType.GetField("_isDirty", Instance).GetValue(light);

                Cubemap on = Own(new Cubemap(64, TextureFormat.RGBAHalf, false));
                Cubemap off = Own(new Cubemap(64, TextureFormat.RGBAHalf, false));
                Color[] redPixels = new Color[64 * 64];
                Array.Fill(redPixels, Color.red);
                Color[] blackPixels = new Color[64 * 64];
                Array.Fill(blackPixels, Color.black);
                for (int face = 0; face < 6; face++)
                {
                    on.SetPixels(face == 4 ? redPixels : blackPixels, (CubemapFace)face);
                    off.SetPixels(blackPixels, (CubemapFace)face);
                }

                on.Apply();
                off.Apply();

                Texture2D floorPatch = Own(new Texture2D(4, 4, TextureFormat.RGBAHalf, false));
                int rendered = 0;
                afterRender = _ => rendered++;
                Camera.onPostRender += afterRender;
                provider.GetMethod("ApplyFloorEnvironment", Static).Invoke(null, new object[] { floorPatch, new Vector4(-2f, -2f, 0.25f, 0.25f), 0.005f });
                Check(ReferenceEquals(Shader.GetGlobalTexture("_FerrofluidFloorReflection"), floorPatch), "Floor binding must set the global floor texture.");
                Check((Vector4)Shader.GetGlobalVector("_FerrofluidFloorReflectionBounds") == new Vector4(-2f, -2f, 0.25f, 0.25f), "Floor binding must set bounds.");
                Check(Mathf.Approximately(Shader.GetGlobalFloat("_FerrofluidFloorReflectionY"), 0.005f), "Floor binding must set planeY.");
                apply.Invoke(null, new object[] { on, off, sign });
                Check(rendered == 0, $"Initial environment binding must not render the scene, got {rendered}.");
                Check(renderer.color == Color.black && ReferenceEquals(renderer.sharedMaterial, black), "Binding changed the live E sprite.");
                Check((Color)lightType.GetProperty("color").GetValue(light) == Color.black, "Binding changed the live E light.");
                Check((bool)lightType.GetField("_isDirty", Instance).GetValue(light) == dirty, "Binding changed the light's pending refresh state.");

                Material reflection = Own(new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FerrofluidSurface.shader")));
                reflection.SetFloat("_FadeAlpha", 1f);
                reflection.SetFloat("_Exposure", 1f);
                Check(ReferenceEquals(Shader.GetGlobalTexture(RoomTexture), off), "The unlit E must select the off map first.");
                Color offColor = FerrofluidEnvironmentReflectionValidation.Sample(reflection, Vector3.forward, Vector3.forward);
                Check(offColor.maxColorComponent < 0.02f, $"The unlit E must be absent: {offColor}.");

                harmonyType = Assembly.Load("0Harmony").GetType("HarmonyLib.Harmony", true);
                harmony = Activator.CreateInstance(harmonyType, "ChromaGLS.NeonReflectionValidation");
                object processor = harmonyType.GetMethod("CreateClassProcessor", new[] { typeof(Type) }).Invoke(
                    harmony,
                    new object[] { plugin.GetType("ChromaGLS.HarmonyPatches.FerrofluidNeonSignPatch", true) });
                processor.GetType().GetMethod("Patch", Type.EmptyTypes).Invoke(processor, null);
                MethodInfo setOn = signType.GetMethod("SetOn", Instance);
                setOn.Invoke(sign, new object[] { true });
                Check(ReferenceEquals(Shader.GetGlobalTexture(RoomTexture), on), "The lit E must select the other baked map.");
                Color onColor = FerrofluidEnvironmentReflectionValidation.Sample(reflection, Vector3.forward, Vector3.forward);
                Check(onColor.r > 0.5f && onColor.g < 0.02f && onColor.b < 0.02f, $"The lit E must be reflected red: {onColor}.");
                for (int index = 0; index < 32; index++)
                {
                    setOn.Invoke(sign, new object[] { false });
                    Check(ReferenceEquals(Shader.GetGlobalTexture(RoomTexture), off), "Flickering off allocated or selected another map.");
                    setOn.Invoke(sign, new object[] { true });
                    Check(ReferenceEquals(Shader.GetGlobalTexture(RoomTexture), on), "Flickering on allocated or selected another map.");
                }

                Check(rendered == 0, "Flicker changes rendered the scene.");
                GameObject otherObject = Own(new GameObject("Other inactive sign"));
                otherObject.SetActive(false);
                Component other = otherObject.AddComponent(signType);
                provider.GetMethod("NotifyNeonState", Static).Invoke(null, new object[] { other, false });
                Check(ReferenceEquals(Shader.GetGlobalTexture(RoomTexture), on), "Another sign changed the menu reflection.");
                Check(ReferenceEquals(Shader.GetGlobalTexture("_FerrofluidFloorReflection"), floorPatch), "Neon switching must not disturb the floor binding.");
                end.Invoke(null, null);
                Check(Shader.GetGlobalTexture(RoomTexture) == null, "Menu teardown retained the bound map.");
                Check(Shader.GetGlobalTexture("_FerrofluidFloorReflection") == null, "Menu teardown retained the floor texture.");
                Check(floorPatch != null, "Menu teardown destroyed the borrowed floor texture.");
                Check(on != null && off != null, "Menu teardown destroyed borrowed baked maps.");
                Check(!Shader.IsKeywordEnabled(RoomKeyword), "Menu teardown retained the reflection keyword.");
                Debug.Log("[FerrofluidNeonReflectionValidation] PASS: baked E on/off pixels, 32 synchronized switches without renders, source restoration, borrowed-map ownership, and teardown.");
            }
            finally
            {
                if (afterRender != null)
                    Camera.onPostRender -= afterRender;
                if (harmony != null)
                    harmonyType.GetMethod("UnpatchSelf", Type.EmptyTypes).Invoke(harmony, null);
                end.Invoke(null, null);
                Shader.SetGlobalTexture(RoomTexture, previous);
                if (wasEnabled)
                    Shader.EnableKeyword(RoomKeyword);
                else
                    Shader.DisableKeyword(RoomKeyword);
                for (int index = owned.Count - 1; index >= 0; index--)
                    Object.DestroyImmediate(owned[index]);
            }

            T Own<T>(T item) where T : Object
            {
                owned.Add(item);
                return item;
            }
        }

        private static void Set(object target, string name, object value)
            => target.GetType().GetField(name, Instance).SetValue(target, value);

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }
    }

}
