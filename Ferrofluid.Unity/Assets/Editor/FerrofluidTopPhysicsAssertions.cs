using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    // These checks distinguish a travelling rounded top from a center of mass orbiting a pinned tip.
    internal static class FerrofluidTopPhysicsAssertions
    {
        internal const float BodyScale = 0.55f;
        internal const float Roundness = 0.48f;
        internal const float Duration = 10f;
        internal const float TravelWindowStart = 2f;

        internal readonly struct Case
        {
            internal readonly string Name;
            internal readonly float DeltaTime;
            internal readonly float TiltDegrees;
            internal readonly float RevolutionsPerSecond;
            internal readonly bool WorldUpSpin;

            internal Case(float deltaTime, float tiltDegrees, float revolutionsPerSecond, bool worldUpSpin = false)
            {
                DeltaTime = deltaTime;
                TiltDegrees = tiltDegrees;
                RevolutionsPerSecond = revolutionsPerSecond;
                WorldUpSpin = worldUpSpin;
                Name = $"dt{deltaTime:F5}-tilt{tiltDegrees:F0}-rps{revolutionsPerSecond:F0}-{(worldUpSpin ? "world-up" : "corner-axis")}";
            }
        }

        internal static IEnumerable<Case> Cases()
        {
            foreach (float deltaTime in new[] { 0.02f, 1f / 90f })
            {
                foreach (float revolutionsPerSecond in new[] { 10f, -10f, 20f, -20f })
                    yield return new Case(deltaTime, 10f, revolutionsPerSecond);
                yield return new Case(deltaTime, -10f, 10f);
                yield return new Case(deltaTime, 10f, 10f, worldUpSpin: true);
            }
        }

        internal struct Sample
        {
            internal float Time;
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal Vector3 Velocity;
            internal Vector3 AngularVelocity;
            internal Vector3 AngularMomentum;
            internal Vector3 ColliderSupportPoint;
        }

        [Serializable]
        internal sealed class Result
        {
            public string name;
            public bool passed;
            public string[] failures;
            public int travelSamples;
            public float centerSpan;
            public float supportSpan;
            public float supportOverCenterSpan;
            public float signedRollingCoefficient;
            public float signedRollingCorrelation;
            public float renderedSlipOverRollingSpeed;
            public float maximumTiltDegrees;
            public float finalMomentumFraction;
            public float nearFloorFraction;
        }

        // The rendered base is sum(abs(x/a)^p) = 1, p = 8 - 6*roundness.
        // Its support follows by maximizing n.dot(x) under that constraint.
        // This reference uses the visible surface, not the collider's chosen shape.
        internal static Vector3 RenderedSupportOffset(Quaternion rotation)
        {
            Vector3 normal = Quaternion.Inverse(rotation) * Vector3.down;
            double exponent = 8.0 - (6.0 * Roundness);
            double dual = exponent / (exponent - 1.0);
            double sum = Math.Pow(Math.Abs(normal.x), dual)
                + Math.Pow(Math.Abs(normal.y), dual)
                + Math.Pow(Math.Abs(normal.z), dual);
            double denominator = Math.Pow(sum, (dual - 1.0) / dual);
            double half = BodyScale * 0.5;
            Vector3 local = new(
                (float)(half * Math.Sign(normal.x) * Math.Pow(Math.Abs(normal.x), dual - 1.0) / denominator),
                (float)(half * Math.Sign(normal.y) * Math.Pow(Math.Abs(normal.y), dual - 1.0) / denominator),
                (float)(half * Math.Sign(normal.z) * Math.Pow(Math.Abs(normal.z), dual - 1.0) / denominator));
            return rotation * local;
        }

        internal static Result EvaluateGrounded(string name, IList<Sample> samples)
        {
            if (samples.Count < 2 || samples[0].Time > 0.0001f
                || samples[samples.Count - 1].Time < Duration - 0.03f)
            {
                throw new ArgumentException("A grounded case must include its initial state and ten simulated seconds.");
            }

            List<string> failures = new();
            Vector3 centerMin = new(float.PositiveInfinity, 0f, float.PositiveInfinity);
            Vector3 centerMax = new(float.NegativeInfinity, 0f, float.NegativeInfinity);
            Vector3 supportMin = centerMin;
            Vector3 supportMax = centerMax;
            double sumDot = 0;
            double sumVelocitySquared = 0;
            double sumRollingSquared = 0;
            double sumSlipSquared = 0;
            double endMomentum = 0;
            int endCount = 0;
            int travelCount = 0;
            int nearFloorCount = 0;
            float maximumTilt = 0f;
            Vector3 cornerAxis = Vector3.one.normalized;
            for (int i = 0; i < samples.Count; i++)
            {
                Sample sample = samples[i];
                if (sample.Time >= 0.5f)
                {
                    float tilt = Vector3.Angle(sample.Rotation * cornerAxis, Vector3.up);
                    maximumTilt = Mathf.Max(maximumTilt, tilt);
                }

                if (sample.Time >= Duration - 0.5f)
                {
                    endMomentum += sample.AngularMomentum.magnitude;
                    endCount++;
                }

                if (sample.Time < TravelWindowStart)
                    continue;

                Vector3 center = Horizontal(sample.Position);
                Vector3 support = Horizontal(sample.ColliderSupportPoint);
                centerMin = Vector3.Min(centerMin, center);
                centerMax = Vector3.Max(centerMax, center);
                supportMin = Vector3.Min(supportMin, support);
                supportMax = Vector3.Max(supportMax, support);
                Vector3 velocity = Horizontal(sample.Velocity);
                Vector3 rolling = Horizontal(-Vector3.Cross(
                    sample.AngularVelocity, RenderedSupportOffset(sample.Rotation)));
                sumDot += Vector3.Dot(velocity, rolling);
                sumVelocitySquared += velocity.sqrMagnitude;
                sumRollingSquared += rolling.sqrMagnitude;
                sumSlipSquared += (velocity - rolling).sqrMagnitude;
                if (sample.ColliderSupportPoint.y >= -0.005f && sample.ColliderSupportPoint.y <= 0.01f)
                    nearFloorCount++;
                travelCount++;
            }

            if (travelCount < 10 || endCount == 0 || samples[0].AngularMomentum.magnitude <= 0f)
                throw new ArgumentException("The trace lacks a nonzero initial spin or sufficient measured samples.");

            float centerSpan = (centerMax - centerMin).magnitude;
            float supportSpan = (supportMax - supportMin).magnitude;
            float spanRatio = supportSpan / Mathf.Max(centerSpan, 0.000001f);
            float coefficient = (float)(sumDot / Math.Max(sumRollingSquared, 1e-12));
            float correlation = (float)(sumDot / Math.Sqrt(Math.Max(sumVelocitySquared * sumRollingSquared, 1e-12)));
            float slipRatio = (float)Math.Sqrt(sumSlipSquared / Math.Max(sumRollingSquared, 1e-12));
            float momentumFraction = (float)(endMomentum / endCount) / samples[0].AngularMomentum.magnitude;
            float nearFloorFraction = (float)nearFloorCount / travelCount;

            // These are acceptance bounds for the configured rounded fidget, not laws for every pointed top.
            Require(supportSpan >= BodyScale * 0.05f, "The supporting contact must travel at least 5% of the cube width.", failures);
            Require(spanRatio >= 0.5f, "Contact travel must not be dwarfed by a center-of-mass orbit around a pinned corner.", failures);
            Require(coefficient >= 0.25f, "Travel must have meaningful amplitude in the signed rolling direction of the rounded surface.", failures);
            Require(correlation >= 0.5f, "Travel must follow, not oppose, the rounded surface's signed lean/spin rolling direction.", failures);
            Require(slipRatio <= 0.9f, "The default contact must make progress toward rolling rather than leave the rendered surface slipping in place.", failures);
            Require(maximumTilt <= 35f, "The original spinning corner axis must remain within 35 degrees of upright for ten seconds.", failures);
            Require(momentumFraction >= 0.6f, "The unbumped top must retain at least 60% of its angular-momentum magnitude after ten seconds.", failures);
            Require(nearFloorFraction >= 0.9f, "The unbumped top must remain near the floor, not pass by flying or falling away.", failures);
            return new Result
            {
                name = name,
                passed = failures.Count == 0,
                failures = failures.ToArray(),
                travelSamples = travelCount,
                centerSpan = centerSpan,
                supportSpan = supportSpan,
                supportOverCenterSpan = spanRatio,
                signedRollingCoefficient = coefficient,
                signedRollingCorrelation = correlation,
                renderedSlipOverRollingSpeed = slipRatio,
                maximumTiltDegrees = maximumTilt,
                finalMomentumFraction = momentumFraction,
                nearFloorFraction = nearFloorFraction,
            };
        }

        internal static void AssertTorqueFreeMomentum(IList<Sample> samples)
        {
            if (samples.Count < 2 || samples[samples.Count - 1].Time < 1.99f)
                throw new ArgumentException("The torque-free trace must cover two seconds.");
            Vector3 initial = samples[0].AngularMomentum;
            if (initial.magnitude <= 0f)
                throw new ArgumentException("The torque-free trace must start spinning.");
            foreach (Sample sample in samples)
            {
                if ((sample.AngularMomentum - initial).magnitude > initial.magnitude * 0.01f)
                    throw new Exception("Torque-free flight changes the angular-momentum vector by more than 1%.");
            }
        }

        internal static void AssertRelease(
            Vector3 beforeFreeMomentum,
            Vector3 afterFreeMomentum,
            Vector3 beforeFreeCenterVelocity,
            Vector3 afterFreeCenterVelocity)
        {
            if (beforeFreeMomentum.magnitude < 0.01f)
                throw new ArgumentException("Controller input must spin the held body before release.");
            if ((afterFreeMomentum - beforeFreeMomentum).magnitude > beforeFreeMomentum.magnitude * 0.001f)
                throw new Exception("Release changes angular momentum about the physical free center by more than 0.1%.");
            if ((afterFreeCenterVelocity - beforeFreeCenterVelocity).magnitude > 0.001f)
                throw new Exception("Release changes physical-center velocity by more than 1 mm/s.");
        }

        private static Vector3 Horizontal(Vector3 value)
            => new(value.x, 0f, value.z);

        private static void Require(bool condition, string message, List<string> failures)
        {
            if (!condition)
                failures.Add(message);
        }
    }
}
