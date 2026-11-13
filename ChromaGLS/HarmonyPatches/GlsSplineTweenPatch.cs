using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ChromaGLS.Helpers;
using HarmonyLib;

namespace ChromaGLS.HarmonyPatches
{
    // Replaces only the FloatTween newobj inside the three GLS effect constructors with the
    // spline-capable subclass. The subclass keeps an identical ctor signature and falls back to
    // native behavior when disabled or no spline is prepared, so re-enabling existing effects keeps
    // working without scene reconstruction.
    internal static class GlsSplineTweenPatch
    {
        private static readonly ConstructorInfo NativeDefaultCtor =
            typeof(Tweening.FloatTween).GetConstructor(Type.EmptyTypes)!;

        private static readonly ConstructorInfo NativeConfiguredCtor =
            typeof(Tweening.FloatTween).GetConstructors().Single(c => c.GetParameters().Length == 6);

        private static readonly ConstructorInfo SplineDefaultCtor =
            typeof(GlsFloatTween).GetConstructor(Type.EmptyTypes)!;

        private static readonly ConstructorInfo SplineConfiguredCtor =
            typeof(GlsFloatTween).GetConstructor(
                NativeConfiguredCtor.GetParameters().Select(p => p.ParameterType).ToArray())!;

        internal static IEnumerable<CodeInstruction> ReplaceTweenConstruction(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Newobj && instruction.operand is ConstructorInfo ctor)
                {
                    if (ctor == NativeDefaultCtor)
                    {
                        instruction.operand = SplineDefaultCtor;
                    }
                    else if (ctor == NativeConfiguredCtor)
                    {
                        instruction.operand = SplineConfiguredCtor;
                    }
                }

                yield return instruction;
            }
        }
    }

    [HarmonyPatch(
        typeof(LightTranslationGroupEffect),
        MethodType.Constructor,
        new[] { typeof(LightTranslationGroupEffect.InitData), typeof(Tweening.SongTimeTweeningManager), typeof(BeatmapCallbacksController) })]
    internal static class GlsSplineTweenTranslationPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            GlsSplineTweenPatch.ReplaceTweenConstruction(instructions);

        private static void Postfix(
            LightTranslationGroupEffect __instance,
            LightTranslationGroupEffect.InitData initData,
            BeatmapCallbacksController beatmapCallbacksController)
        {
            var path = GlsTranslationRuntime.Create(initData, beatmapCallbacksController);
            ((GlsFloatTween)__instance._xTranslationTween).SetTranslationPath(path, 0);
            ((GlsFloatTween)__instance._yTranslationTween).SetTranslationPath(path, 1);
            ((GlsFloatTween)__instance._zTranslationTween).SetTranslationPath(path, 2);
        }
    }

    [HarmonyPatch(
        typeof(LightRotationGroupEffect),
        MethodType.Constructor,
        new[] { typeof(LightRotationGroupEffect.InitData), typeof(Tweening.SongTimeTweeningManager), typeof(BeatmapCallbacksController) })]
    internal static class GlsSplineTweenRotationPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            GlsSplineTweenPatch.ReplaceTweenConstruction(instructions);

        private static void Postfix(
            LightRotationGroupEffect __instance,
            LightRotationGroupEffect.InitData initData,
            BeatmapCallbacksController beatmapCallbacksController)
        {
            ((GlsFloatTween)__instance._rotationTween).SetScalarPath(
                GlsScalarPath.Rotation(initData, beatmapCallbacksController));
        }
    }

#if !V1_29_1
    [HarmonyPatch(
        typeof(FloatFxGroupEffect),
        MethodType.Constructor,
        new[] { typeof(FloatFxGroupEffect.InitData), typeof(Tweening.SongTimeTweeningManager), typeof(BeatmapCallbacksController) })]
    internal static class GlsSplineTweenFloatFxPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            GlsSplineTweenPatch.ReplaceTweenConstruction(instructions);

        private static void Postfix(
            FloatFxGroupEffect __instance,
            FloatFxGroupEffect.InitData initData,
            BeatmapCallbacksController beatmapCallbacksController)
        {
            ((GlsFloatTween)__instance._floatTween).SetScalarPath(
                GlsScalarPath.FloatFx(initData, beatmapCallbacksController));
        }
    }
#endif
}
