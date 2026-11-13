using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidGrabFloorAssertions
    {
        internal const float AcquireTime = 0.6f;
        internal const float MoveStart = 0.8f;
        internal const float MoveEnd = 2.6f;
        internal const float ReleaseTime = 3f;
        internal const float EndTime = 3.8f;

        // Samples use the body origin and its point velocity, not the temporary held COM.
        internal static List<string> Evaluate(
            IList<FerrofluidTopPhysicsAssertions.Sample> samples,
            bool tip,
            int direction,
            float width)
        {
            if (samples.Count < 2 || samples[samples.Count - 1].Time < EndTime - 0.03f)
                throw new ArgumentException("A manipulation case must include its complete controller and release trace.");

            List<string> failures = new();
            FerrofluidTopPhysicsAssertions.Sample acquired = At(samples, AcquireTime);
            FerrofluidTopPhysicsAssertions.Sample heldEnd = At(samples, ReleaseTime - 0.03f);
            float quietTranslation = 0f;
            float quietTilt = 0f;
            float maximumTilt = 0f;
            float translationBeforeTip = 0f;
            float finalRoll = 0f;
            int supported = 0;
            int moving = 0;
            bool reachedTip = false;
            foreach (FerrofluidTopPhysicsAssertions.Sample sample in samples)
            {
                if (sample.Time < AcquireTime - 0.00001f || sample.Time >= ReleaseTime - 0.00001f)
                    continue;

                float translation = Mathf.Abs(sample.Position.x - acquired.Position.x);
                float tilt = Vector3.Angle(
                    acquired.Rotation * Vector3.up,
                    sample.Rotation * Vector3.up);
                float roll = -direction * Vector3.SignedAngle(
                    acquired.Rotation * Vector3.up,
                    sample.Rotation * Vector3.up,
                    Vector3.forward);
                if (sample.Time <= MoveStart + 0.00001f)
                {
                    quietTranslation = Mathf.Max(quietTranslation, translation);
                    quietTilt = Mathf.Max(quietTilt, tilt);
                }

                maximumTilt = Mathf.Max(maximumTilt, tilt);
                if (!reachedTip)
                    translationBeforeTip = Mathf.Max(translationBeforeTip, translation);
                if (roll >= 15f)
                    reachedTip = true;
                finalRoll = roll;
                if (sample.Time >= MoveStart && sample.Time <= MoveEnd)
                {
                    moving++;
                    if (Mathf.Abs(sample.ColliderSupportPoint.y) <= 0.03f)
                        supported++;
                }
            }

            if (quietTranslation > 0.1f * width)
                failures.Add($"Acquiring a stationary grip moved the cube {quietTranslation / width:F3} widths before controller motion.");
            if (quietTilt > 5f)
                failures.Add($"Acquiring a stationary grip tipped the cube {quietTilt:F2} degrees before controller motion.");
            if (moving == 0 || supported < 0.8f * moving)
                failures.Add($"The manipulation lost floor support ({supported}/{moving} moving samples near the floor).");

            if (tip)
            {
                if (!reachedTip || finalRoll < 30f || finalRoll > 75f)
                    failures.Add($"The edge pull must tip toward the requested face; reached15={reachedTip}, finalRoll={finalRoll:F2} degrees.");
                if (translationBeforeTip > 0.35f * width)
                    failures.Add($"The cube skated {translationBeforeTip / width:F3} widths before tipping 15 degrees.");
            }
            else
            {
                float movement = direction * (heldEnd.Position.x - acquired.Position.x) / width;
                if (movement < 0.2f || movement > 0.6f)
                    failures.Add($"A center-face push should produce a controlled slide, not lock or launch ({movement:F3} widths).");
                if (maximumTilt > 5f)
                    failures.Add($"A center-face slide tipped {maximumTilt:F2} degrees instead of staying flat.");
                FerrofluidTopPhysicsAssertions.Sample release = At(samples, ReleaseTime);
                FerrofluidTopPhysicsAssertions.Sample final = samples[samples.Count - 1];
                float coast = Mathf.Abs(final.Position.x - release.Position.x) / width;
                if (coast > 0.2f || Mathf.Abs(final.Velocity.x) > 0.1f)
                    failures.Add($"After release the face slide kept skating: coast={coast:F3} widths, finalVx={final.Velocity.x:F3} m/s.");
            }

            return failures;
        }

        private static FerrofluidTopPhysicsAssertions.Sample At(
            IList<FerrofluidTopPhysicsAssertions.Sample> samples,
            float time)
        {
            foreach (FerrofluidTopPhysicsAssertions.Sample sample in samples)
            {
                if (sample.Time >= time - 0.00001f)
                    return sample;
            }

            throw new ArgumentException($"The manipulation trace is missing time {time}.");
        }
    }
}
