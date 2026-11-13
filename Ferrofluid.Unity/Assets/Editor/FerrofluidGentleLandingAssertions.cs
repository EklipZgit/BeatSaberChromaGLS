using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidGentleLandingAssertions
    {
        internal const float Duration = 3f;
        private static readonly (string Name, Vector3 Direction)[] Faces =
        {
            ("up", Vector3.up), ("down", Vector3.down),
            ("left", Vector3.left), ("right", Vector3.right),
            ("forward", Vector3.forward), ("back", Vector3.back),
        };

        internal readonly struct Case
        {
            internal readonly string Name;
            internal readonly Vector3 Face;
            internal readonly Quaternion Rotation;
            internal readonly float DeltaTime;

            internal Case(string faceName, Vector3 face, float yaw, float sign, float deltaTime)
            {
                Name = $"owned-gentle-{faceName}-y{yaw:F0}-s{(sign > 0f ? "p" : "n")}-dt{deltaTime:F5}";
                Face = face;
                DeltaTime = deltaTime;
                Rotation = Quaternion.AngleAxis(yaw, Vector3.up)
                    * Quaternion.AngleAxis(sign * 44f, Vector3.forward)
                    * Quaternion.FromToRotation(face, Vector3.up);
            }
        }

        [Serializable]
        internal sealed class Result
        {
            public string name;
            public bool passed;
            public string[] failures;
            public float arrivalTime = -1f;
            public float maximumAngleAfterArrival;
            public int rockingEpisodes;
            public float lateMaximumAngle;
            public float lateMaximumAngularSpeed;
        }

        internal static IEnumerable<Case> Cases(float deltaTime)
        {
            foreach ((string name, Vector3 face) in Faces)
            {
                foreach (float yaw in new[] { 0f, 37f })
                {
                    foreach (float sign in new[] { 1f, -1f })
                        yield return new Case(name, face, yaw, sign, deltaTime);
                }
            }
        }

        // Keep the existing landing-quality thresholds in the actual World.Step path.
        // Default-scene pose-step bounds remain independently checked by the older fixture.
        internal static Result Evaluate(Case test, IList<FerrofluidTopPhysicsAssertions.Sample> samples)
        {
            if (samples.Count < 2 || samples[0].Time > 0.00001f
                || samples[samples.Count - 1].Time < Duration - 0.03f)
                throw new ArgumentException("A landing trace must contain the initial state and three simulated seconds.");

            Result result = new() { name = test.Name };
            bool armed = false;
            bool outsideEpisode = false;
            int lateSamples = 0;
            foreach (FerrofluidTopPhysicsAssertions.Sample sample in samples)
            {
                float angle = Vector3.Angle(sample.Rotation * test.Face, Vector3.up);
                if (result.arrivalTime < 0f && angle <= 1f)
                    result.arrivalTime = sample.Time;
                if (result.arrivalTime < 0f)
                    continue;

                result.maximumAngleAfterArrival = Mathf.Max(result.maximumAngleAfterArrival, angle);
                if (!armed && angle <= 0.25f)
                    armed = true;
                if (armed && !outsideEpisode && angle >= 0.5f)
                    outsideEpisode = true;
                if (outsideEpisode && angle <= 0.25f)
                {
                    result.rockingEpisodes++;
                    outsideEpisode = false;
                }

                if (sample.Time >= result.arrivalTime + 0.5f)
                {
                    lateSamples++;
                    result.lateMaximumAngle = Mathf.Max(result.lateMaximumAngle, angle);
                    result.lateMaximumAngularSpeed = Mathf.Max(result.lateMaximumAngularSpeed, sample.AngularVelocity.magnitude);
                }
            }

            if (outsideEpisode)
                result.rockingEpisodes++;
            List<string> failures = new();
            if (result.arrivalTime <= 0f || result.arrivalTime > 2f)
                failures.Add($"The cube must reach its face within two seconds (arrival={result.arrivalTime:F3}).");
            if (result.maximumAngleAfterArrival > 1.5f)
                failures.Add($"Landing overshoot {result.maximumAngleAfterArrival:F3} exceeds 1.5 degrees.");
            if (result.rockingEpisodes > 1)
                failures.Add($"The cube rocked {result.rockingEpisodes} times past 0.5 degrees.");
            if (lateSamples == 0 || result.lateMaximumAngle > 0.25f || result.lateMaximumAngularSpeed > 0.15f)
                failures.Add($"The cube kept rocking late (angle={result.lateMaximumAngle:F3}, angularSpeed={result.lateMaximumAngularSpeed:F3}).");
            result.failures = failures.ToArray();
            result.passed = failures.Count == 0;
            return result;
        }
    }
}
