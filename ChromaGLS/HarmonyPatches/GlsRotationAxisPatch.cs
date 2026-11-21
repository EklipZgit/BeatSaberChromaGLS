using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Tweening;
using UnityEngine;

namespace ChromaGLS.HarmonyPatches
{
    [HarmonyPatch(typeof(LightRotationGroupEffectManager), "Start")]
    internal static class GlsRotationAxisPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = instructions.ToList();
            ConstructorInfo constructor = AccessTools.DeclaredConstructor(typeof(LightRotationGroupEffect.InitData), new[]
            {
                typeof(int), typeof(int), typeof(LightAxis), typeof(bool), typeof(Transform),
            });
            MethodInfo groupIdGetter = AccessTools.PropertyGetter(typeof(LightGroupSubsystem), nameof(LightGroupSubsystem.groupId));
            MethodInfo factory = AccessTools.DeclaredMethod(typeof(GlsRotationAxisPatch), nameof(CreateInitData));
            List<int> sites = new(3);

            // Stock Start loads the group local just before get_groupId, nine instructions
            // before constructing InitData. Reuse that local as an extra factory argument.
            const int GroupLoadOffset = 9;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Newobj && Equals(code[i].operand, constructor))
                {
                    if (i < GroupLoadOffset || !code[i - GroupLoadOffset].IsLdloc() || !code[i - GroupLoadOffset + 1].Calls(groupIdGetter))
                    {
                        sites.Clear();
                        break;
                    }

                    sites.Add(i);
                }
            }

            if (sites.Count != 3)
            {
                Plugin.Log?.Warn("Could not locate all three GLS rotation initialization sites. Using the game's rotation effects.");
                return code;
            }

            for (int i = sites.Count - 1; i >= 0; i--)
            {
                int site = sites[i];
                CodeInstruction groupLoad = code[site - GroupLoadOffset];
                code[site].opcode = groupLoad.opcode;
                code[site].operand = groupLoad.operand;
                code.Insert(site + 1, new CodeInstruction(OpCodes.Call, factory));
            }

            return code;
        }

        private static LightRotationGroupEffect.InitData CreateInitData(
            int groupId, int elementId, LightAxis axis, bool mirrored, Transform target, LightRotationGroup group)
        {
            if (!GlsAxisUnlockScope.IsEnabled)
            {
                return new LightRotationGroupEffect.InitData(groupId, elementId, axis, mirrored, target);
            }

            bool hasX = group.xTransforms.Count != 0;
            bool hasY = group.yTransforms.Count != 0;
            bool hasZ = group.zTransforms.Count != 0;
            LightAxis source = hasX ? LightAxis.X : hasY ? LightAxis.Y : LightAxis.Z;

            if (axis == source && !(hasX && hasY && hasZ))
            {
                return new UnlockedRotationData(
                    groupId,
                    elementId,
                    axis,
                    mirrored,
                    target,
                    !hasX,
                    !hasY,
                    !hasZ);
            }

            return new LightRotationGroupEffect.InitData(groupId, elementId, axis, mirrored, target);
        }

        [HarmonyPatch]
        private static class RotationEffectPatch
        {
            private static MethodBase TargetMethod() =>
                AccessTools.DeclaredConstructor(typeof(LightRotationGroupEffect), new[]
                {
                    typeof(LightRotationGroupEffect.InitData),
                    typeof(SongTimeTweeningManager),
                    typeof(BeatmapCallbacksController),
                });

            private static bool Prefix(
                LightRotationGroupEffect __instance,
                LightRotationGroupEffect.InitData initData,
                SongTimeTweeningManager tweeningManager,
                BeatmapCallbacksController beatmapCallbacksController,
                ref BeatmapCallbacksController ____beatmapCallbacksController,
                ref BeatmapDataCallbackWrapper ____lightRotationBeatmapEventCallbackWrapper,
                ref SongTimeTweeningManager ____tweeningManager)
            {
                if (initData is not UnlockedRotationData unlocked)
                {
                    return true;
                }

                // Use the vanilla wrapper field so Cleanup removes every subtype registration together.
                ____beatmapCallbacksController = beatmapCallbacksController;
                ____tweeningManager = tweeningManager;
                initData.transform.localRotation = Quaternion.identity;
                LegacyRotationHandler handler = new(initData.transform, initData.mirrored, tweeningManager, __instance);
                BeatmapDataCallback<LightRotationBeatmapEventData> callback = handler.HandleRotationEvent;
                ____lightRotationBeatmapEventCallbackWrapper =
                    beatmapCallbacksController.AddBeatmapCallback(callback, unlocked.GetSubtypeIdentifiers());
                return false;
            }
        }

        private sealed class UnlockedRotationData : LightRotationGroupEffect.InitData
        {
            internal UnlockedRotationData(
                int groupId,
                int elementId,
                LightAxis axis,
                bool mirrored,
                Transform transform,
                bool unlockX,
                bool unlockY,
                bool unlockZ)
                : base(groupId, elementId, axis, mirrored, transform)
            {
                X = unlockX || axis == LightAxis.X;
                Y = unlockY || axis == LightAxis.Y;
                Z = unlockZ || axis == LightAxis.Z;
            }

            private bool X { get; }

            private bool Y { get; }

            private bool Z { get; }

            internal int[] GetSubtypeIdentifiers()
            {
                int[] ids = new int[(X ? 1 : 0) + (Y ? 1 : 0) + (Z ? 1 : 0)];
                int i = 0;
                if (X)
                {
                    ids[i++] = LightRotationBeatmapEventData.SubtypeIdentifier(groupId, elementId, LightAxis.X);
                }

                if (Y)
                {
                    ids[i++] = LightRotationBeatmapEventData.SubtypeIdentifier(groupId, elementId, LightAxis.Y);
                }

                if (Z)
                {
                    ids[i] = LightRotationBeatmapEventData.SubtypeIdentifier(groupId, elementId, LightAxis.Z);
                }

                return ids;
            }
        }

        // The standalone legacy effect overwrites localRotation with a single axis. Use its
        // angle calculation and tween timing, and compose the independent axis values at the target.
        private sealed class LegacyRotationHandler(
            Transform target, bool mirrored, SongTimeTweeningManager tweeningManager, LightRotationGroupEffect owner)
        {
            private AxisRotation _x = new(Quaternion.identity);
            private AxisRotation _y = new(Quaternion.identity);
            private AxisRotation _z = new(Quaternion.identity);
            private DrivenAxes _drivenAxes = DrivenAxes.None;

            private enum DrivenAxes
            {
                None = -1,
                X = (int)LightAxis.X,
                Y = (int)LightAxis.Y,
                Z = (int)LightAxis.Z,
                Multiple = -2,
            }

            internal void HandleRotationEvent(LightRotationBeatmapEventData current)
            {
                switch (current.axis)
                {
                    case LightAxis.X:
                        ApplyEvent(ref _x, Vector3.right, current);
                        break;
                    case LightAxis.Y:
                        ApplyEvent(ref _y, Vector3.up, current);
                        break;
                    case LightAxis.Z:
                        ApplyEvent(ref _z, Vector3.forward, current);
                        break;
                }
            }

            private void ApplyEvent(ref AxisRotation axis, Vector3 direction, LightRotationBeatmapEventData current)
            {
                DrivenAxes currentAxis = (DrivenAxes)current.axis;
                if (_drivenAxes == DrivenAxes.None)
                {
                    _drivenAxes = currentAxis;
                }
                else if (_drivenAxes != currentAxis)
                {
                    _drivenAxes = DrivenAxes.Multiple;
                }

                float angle = Mathf.Repeat(current.rotation, 360f);
                axis.Tween?.Kill();
                SetRotation(ref axis, direction, angle);

                LightRotationBeatmapEventData? next = current.nextSameTypeEventData;
                if (next == null || next.easeType == EaseType.None)
                {
                    return;
                }

                FloatTween tween = axis.Tween ??= CreateTween(current.axis);
                tween.fromValue = angle;
                tween.toValue = LightRotationGroupEffect.ComputeTargetAngle(
                    angle, Mathf.Repeat(next.rotation, 360f), next.loopCount, next.rotationDirection);
                tween.SetStartTimeAndEndTime(current.time, next.time);
                tween.easeType = next.easeType;
                tweeningManager.ResumeTween(tween, owner);
            }

            private FloatTween CreateTween(LightAxis axis)
            {
                Action<float> onUpdate = axis switch
                {
                    LightAxis.X => SetX,
                    LightAxis.Y => SetY,
                    _ => SetZ,
                };
                return new FloatTween(0f, 0f, onUpdate, 0f, EaseType.Linear);
            }

            private void SetX(float angle) => SetRotation(ref _x, Vector3.right, angle);

            private void SetY(float angle) => SetRotation(ref _y, Vector3.up, angle);

            private void SetZ(float angle) => SetRotation(ref _z, Vector3.forward, angle);

            private void SetRotation(ref AxisRotation axis, Vector3 direction, float angle)
            {
                axis.Rotation = Quaternion.AngleAxis(mirrored ? -angle : angle, direction);
                target.localRotation = _drivenAxes == DrivenAxes.Multiple
                    ? _x.Rotation * _y.Rotation * _z.Rotation
                    : axis.Rotation;
            }

            private struct AxisRotation(Quaternion rotation)
            {
                internal Quaternion Rotation = rotation;
                internal FloatTween? Tween;
            }
        }
    }
}
