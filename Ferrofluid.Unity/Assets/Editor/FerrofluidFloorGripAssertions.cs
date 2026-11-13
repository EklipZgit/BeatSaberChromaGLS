using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidFloorGripAssertions
    {
        internal const float AcquireTime = 0.6f;
        internal const float PushTime = 0.8f;
        internal const float MoveStart = 1f;
        internal const float MoveEnd = 2.2f;
        internal const float ReleaseTime = 2.3f;
        internal const float Duration = 2.8f;
        internal const float ConfiguredFriction = 0.25f;

        internal struct Sample
        {
            internal FerrofluidTopPhysicsAssertions.Sample Body;
            internal float EffectiveDynamicFriction;
            internal float EffectiveStaticFriction;
        }

        [Serializable]
        internal sealed class Result
        {
            public string name;
            public bool passed;
            public string[] failures;
            public float minimumHeldFriction = float.MaxValue;
            public float maximumHeldFriction;
            public int heldContactSamples;
            public float initialSlideSpeed;
            public float slideDistance;
            public float finalSlideSpeed;
            public float maximumTilt;
            public bool reachedTip;
            public float skidBeforeTipWidths;
            public int movingSamples;
            public int groundedMovingSamples;
        }

        internal static float CombineFriction(float a, PhysicsMaterialCombine aMode, float b, PhysicsMaterialCombine bMode)
        {
            if (aMode == PhysicsMaterialCombine.Maximum || bMode == PhysicsMaterialCombine.Maximum)
                return Mathf.Max(a, b);
            if (aMode == PhysicsMaterialCombine.Multiply || bMode == PhysicsMaterialCombine.Multiply)
                return a * b;
            if (aMode == PhysicsMaterialCombine.Minimum || bMode == PhysicsMaterialCombine.Minimum)
                return Mathf.Min(a, b);
            return (a + b) * 0.5f;
        }

        internal static Result Evaluate(string name, bool held, int direction, float width, IList<Sample> samples)
        {
            if (samples.Count < 2 || samples[samples.Count - 1].Body.Time < Duration - 0.03f)
                throw new ArgumentException("A grip trace must include the complete manipulation and release.");

            Result result = new() { name = name };
            List<string> failures = new();
            Vector3 origin = samples[0].Body.Position;
            bool slideStarted = false;
            float slideStartX = 0f;
            foreach (Sample observation in samples)
            {
                FerrofluidTopPhysicsAssertions.Sample body = observation.Body;
                float tilt = Vector3.Angle(body.Rotation * Vector3.up, Vector3.up);
                result.maximumTilt = Mathf.Max(result.maximumTilt, tilt);
                if (held)
                {
                    if (body.Time >= 0.7f && body.Time <= 0.95f
                        && Mathf.Abs(body.ColliderSupportPoint.y) <= 0.03f && tilt <= 5f)
                    {
                        result.heldContactSamples++;
                        result.minimumHeldFriction = Mathf.Min(result.minimumHeldFriction, observation.EffectiveDynamicFriction);
                        result.maximumHeldFriction = Mathf.Max(result.maximumHeldFriction, observation.EffectiveDynamicFriction);
                    }

                    if (body.Time >= MoveStart && body.Time <= MoveEnd)
                    {
                        result.movingSamples++;
                        if (Mathf.Abs(body.ColliderSupportPoint.y) <= 0.03f)
                            result.groundedMovingSamples++;
                        Vector3 leadingEdge = body.Position
                            + body.Rotation * new Vector3(direction * 0.5f * width, -0.5f * width, 0f);
                        float initialEdgeX = origin.x + direction * 0.5f * width;
                        if (!result.reachedTip)
                            result.skidBeforeTipWidths = Mathf.Max(result.skidBeforeTipWidths,
                                Mathf.Abs(leadingEdge.x - initialEdgeX) / width);
                        float roll = -direction * Vector3.SignedAngle(Vector3.up, body.Rotation * Vector3.up, Vector3.forward);
                        if (roll >= 15f)
                            result.reachedTip = true;
                    }
                }
                else if (body.Time > PushTime + 0.00001f)
                {
                    if (!slideStarted)
                    {
                        slideStarted = true;
                        slideStartX = body.Position.x;
                        result.initialSlideSpeed = direction * body.Velocity.x;
                    }
                    result.slideDistance = Mathf.Max(result.slideDistance, Mathf.Abs(body.Position.x - slideStartX));
                    result.finalSlideSpeed = Mathf.Abs(body.Velocity.x);
                }
            }

            if (held)
            {
                if (result.heldContactSamples < 5)
                    failures.Add("The held cube did not remain flat and grounded long enough to measure its floor grip.");
                else if (Mathf.Abs(result.minimumHeldFriction - ConfiguredFriction) > 0.01f
                    || Mathf.Abs(result.maximumHeldFriction - ConfiguredFriction) > 0.01f)
                    failures.Add($"Grabbing bypassed configured floor friction {ConfiguredFriction:F3}: effective range {result.minimumHeldFriction:F3}..{result.maximumHeldFriction:F3}.");
                if (!result.reachedTip || result.skidBeforeTipWidths > 0.25f)
                    failures.Add($"A diagonal corner lift skated instead of catching: reached15={result.reachedTip}, skid={result.skidBeforeTipWidths:F3} widths.");
                if (result.movingSamples == 0 || result.groundedMovingSamples < 0.8f * result.movingSamples)
                    failures.Add($"The lift lost floor support ({result.groundedMovingSamples}/{result.movingSamples} moving samples grounded).");
            }
            else
            {
                if (!slideStarted || result.initialSlideSpeed < 0.75f)
                    failures.Add($"The native one-metre-per-second impulse was not delivered (speed={result.initialSlideSpeed:F3}).");
                // At mu=0.25 and g=11.27, a 1 m/s slide should stop in about 0.18 m.
                if (result.slideDistance > 0.26f || result.finalSlideSpeed > 0.1f)
                    failures.Add($"The free face kept skating: distance={result.slideDistance:F3} m, finalSpeed={result.finalSlideSpeed:F3} m/s.");
                if (result.maximumTilt > 5f)
                    failures.Add($"A horizontal impulse through the COM tipped the flat cube {result.maximumTilt:F3} degrees.");
            }

            result.failures = failures.ToArray();
            result.passed = failures.Count == 0;
            return result;
        }
    }
}
