using System.Reflection;
using CustomJSONData.CustomBeatmap;
using HarmonyLib;
using UnityEngine;

namespace ChromaGLS.HarmonyPatches
{
    // Applies Chroma custom colors to GLS (LightColorGroupEffect) events.
    //
    // Plain-Harmony port of the Heck/Chroma GlsColorChromafier (originally a SiraUtil.Affinity patch).
    // This plugin does not depend on SiraUtil/Zenject, so it is a static Harmony postfix instead.
    //
    // Patch strategy:
    //   POSTFIX HandleColorChangeBeatmapEvent - after SetData has run, patch _fromColor/_toColor directly.
    //     SetColor(t) does Color.LerpUnclamped(_fromColor, _toColor, t) every frame, so these two fields
    //     fully control color interpolation. We preserve the game's alpha (brightness) from each field
    //     and replace only the RGB with the custom color. _fromColor gets currentEventData's color;
    //     _toColor gets nextSameTypeEventData's color (if it has a custom color), otherwise left alone.
    [HarmonyPatch(typeof(LightColorGroupEffect), nameof(LightColorGroupEffect.HandleColorChangeBeatmapEvent))]
    internal static class GlsColorChromafier
    {
        private static readonly FieldInfo FromColorField =
            typeof(LightColorGroupEffect).GetField("_fromColor", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo ToColorField =
            typeof(LightColorGroupEffect).GetField("_toColor", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo AltFromColorField =
            typeof(LightColorGroupEffect).GetField("_alternativeFromColor", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo AltToColorField =
            typeof(LightColorGroupEffect).GetField("_alternativeToColor", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo SetColorMethod =
            typeof(LightColorGroupEffect).GetMethod("SetColor", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly object[] NoTweenIndicator = { 0f };

        [HarmonyPostfix]
        private static void Postfix(
            LightColorGroupEffect __instance,
            LightColorBeatmapEventData currentEventData)
        {
            Color? fromColor = ResolveCustomColor(currentEventData);
            LightColorBeatmapEventData? nextEventData = currentEventData.nextSameTypeEventData as LightColorBeatmapEventData;
            bool hasTween = nextEventData != null && nextEventData.easeType != EaseType.None;
            Color? toColor = hasTween ? ResolveCustomColor(nextEventData!) : fromColor;

            if (fromColor.HasValue)
            {
                ApplyColorWithAlpha(FromColorField, __instance, fromColor.Value);
                ApplyColorWithAlpha(AltFromColorField, __instance, fromColor.Value);

                if (!hasTween)
                {
                    // Without this we would deviate from the out of the box behavior and change to the
                    // next color immediately instead of waiting for its node. The default implementation
                    // sets the color to 0f when there is no tween.
                    SetColorMethod.Invoke(__instance, NoTweenIndicator);
                    return;
                }
            }

            if (toColor.HasValue)
            {
                ApplyColorWithAlpha(ToColorField, __instance, toColor.Value);
                ApplyColorWithAlpha(AltToColorField, __instance, toColor.Value);
            }
        }

        private static void ApplyColorWithAlpha(FieldInfo field, LightColorGroupEffect instance, Color customColor)
        {
            Color existing = (Color)field.GetValue(instance);
            field.SetValue(instance, new Color(customColor.r, customColor.g, customColor.b, existing.a));
        }

        private static Color? ResolveCustomColor(LightColorBeatmapEventData eventData)
        {
            if (eventData is ICustomData customDataEvent)
            {
                return customDataEvent.customData.GetColor("color");
            }

            return null;
        }
    }
}
