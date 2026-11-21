using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ChromaGLS.HarmonyPatches
{
    [HarmonyPatch]
    internal static class GlsTranslationAxisPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.DeclaredConstructor(typeof(LightTranslationGroupEffect.InitData), new[]
            {
                typeof(int), typeof(int), typeof(bool), typeof(bool), typeof(bool),
                typeof(Transform), typeof(Transform), typeof(Transform),
                typeof(Vector2), typeof(Vector2), typeof(Vector2), typeof(Vector2), typeof(Vector2), typeof(Vector2),
            });

        private static void Prefix(
            ref bool xMirrored,
            ref bool yMirrored,
            ref bool zMirrored,
            ref Transform? xTransform,
            ref Transform? yTransform,
            ref Transform? zTransform,
            ref Vector2 xTranslationLimits,
            ref Vector2 xDistributionLimits,
            ref Vector2 yTranslationLimits,
            ref Vector2 yDistributionLimits,
            ref Vector2 zTranslationLimits,
            ref Vector2 zDistributionLimits)
        {
            if (!GlsAxisUnlockScope.IsEnabled)
            {
                return;
            }

            bool hasX = xTransform != null;
            bool hasY = yTransform != null;
            bool hasZ = zTransform != null;
            if ((hasX && hasY && hasZ) || (!hasX && !hasY && !hasZ))
            {
                return;
            }

            Transform? source = hasX ? xTransform : hasY ? yTransform : zTransform;
            bool mirrored = hasX ? xMirrored : hasY ? yMirrored : zMirrored;
            Vector2 translationLimits = ResolveLimits(
                hasX ? xTranslationLimits : hasY ? yTranslationLimits : zTranslationLimits,
                xTranslationLimits,
                yTranslationLimits,
                zTranslationLimits);
            Vector2 distributionLimits = ResolveLimits(
                hasX ? xDistributionLimits : hasY ? yDistributionLimits : zDistributionLimits,
                xDistributionLimits,
                yDistributionLimits,
                zDistributionLimits);

            AliasAxis(ref xTransform, ref xMirrored, ref xTranslationLimits, ref xDistributionLimits);
            AliasAxis(ref yTransform, ref yMirrored, ref yTranslationLimits, ref yDistributionLimits);
            AliasAxis(ref zTransform, ref zMirrored, ref zTranslationLimits, ref zDistributionLimits);

            void AliasAxis(ref Transform? target, ref bool mirror, ref Vector2 translation, ref Vector2 distribution)
            {
                if (target != null)
                {
                    return;
                }

                // The native handler groups repeated transform references into one axis mask.
                target = source;
                mirror = mirrored;
                if (!HasLimits(translation))
                {
                    translation = translationLimits;
                }

                if (!HasLimits(distribution))
                {
                    distribution = distributionLimits;
                }
            }
        }

        private static bool HasLimits(Vector2 limits) => limits.x != 0f || limits.y != 0f;

        private static Vector2 ResolveLimits(Vector2 source, Vector2 x, Vector2 y, Vector2 z)
        {
            if (HasLimits(source))
            {
                return source;
            }

            return HasLimits(x) ? x : HasLimits(y) ? y : z;
        }
    }
}
