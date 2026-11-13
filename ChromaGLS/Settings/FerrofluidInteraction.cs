using UnityEngine;

namespace ChromaGLS.Settings
{
    internal static class FerrofluidInteraction
    {
        // Instantaneous gap-closing rate a + d*(b + c*d) fitted through three tunable
        // time constants at three normalized distances, solved at compile time so each
        // point can be retuned independently.
        private const float TrackingTimeNear = 0.1f;
        private const float TrackingTimeMid = 0.02f;
        private const float TrackingTimeFar = 0.005f;
        private const float TrackingDistanceNear = 0.02f;
        private const float TrackingDistanceMid = 0.5f;
        private const float TrackingDistanceFar = 2f;
        private const float TrackingRateNear = 1f / TrackingTimeNear;
        private const float TrackingRateMid = 1f / TrackingTimeMid;
        private const float TrackingRateFar = 1f / TrackingTimeFar;

        private const float TrackingRateA =
            (TrackingRateNear * TrackingDistanceMid * TrackingDistanceFar / ((TrackingDistanceNear - TrackingDistanceMid) * (TrackingDistanceNear - TrackingDistanceFar)))
            + (TrackingRateMid * TrackingDistanceNear * TrackingDistanceFar / ((TrackingDistanceMid - TrackingDistanceNear) * (TrackingDistanceMid - TrackingDistanceFar)))
            + (TrackingRateFar * TrackingDistanceNear * TrackingDistanceMid / ((TrackingDistanceFar - TrackingDistanceNear) * (TrackingDistanceFar - TrackingDistanceMid)));

        private const float TrackingRateB =
            (TrackingRateNear * (TrackingDistanceMid + TrackingDistanceFar) / ((TrackingDistanceNear - TrackingDistanceMid) * (TrackingDistanceFar - TrackingDistanceNear)))
            + (TrackingRateMid * (TrackingDistanceNear + TrackingDistanceFar) / ((TrackingDistanceMid - TrackingDistanceNear) * (TrackingDistanceFar - TrackingDistanceMid)))
            + (TrackingRateFar * (TrackingDistanceNear + TrackingDistanceMid) / ((TrackingDistanceFar - TrackingDistanceNear) * (TrackingDistanceMid - TrackingDistanceFar)));

        private const float TrackingRateC =
            (TrackingRateNear / ((TrackingDistanceNear - TrackingDistanceMid) * (TrackingDistanceNear - TrackingDistanceFar)))
            + (TrackingRateMid / ((TrackingDistanceMid - TrackingDistanceNear) * (TrackingDistanceMid - TrackingDistanceFar)))
            + (TrackingRateFar / ((TrackingDistanceFar - TrackingDistanceNear) * (TrackingDistanceFar - TrackingDistanceMid)));

        internal static float CalculateTrackingRate(float normalizedDistance)
        {
            float d = normalizedDistance;
            return TrackingRateA + (d * (TrackingRateB + (TrackingRateC * d)));
        }

        internal static Vector3 TrackPosition(Vector3 current, Vector3 target, float surfaceWidth, float deltaTime)
        {
            if (deltaTime <= 0f)
                return current;

            float rate = CalculateTrackingRate(Vector3.Distance(current, target) / surfaceWidth);
            return current + ((target - current) * (1f - Mathf.Exp(-rate * deltaTime)));
        }

        // The rounded-cube minimum also gives missed rays a continuous target at the silhouette.
        internal static Vector3 EvaluateRayTarget(
            FerrofluidSurfaceShape shape,
            Vector3 origin,
            Vector3 direction,
            float roundness,
            out bool intersects)
        {
            if (shape == FerrofluidSurfaceShape.Sphere)
            {
                float closest = Mathf.Max(0f, -Vector3.Dot(origin, direction));
                Vector3 minimum = origin + (direction * closest);
                float discriminant = 0.25f - minimum.sqrMagnitude;
                intersects = discriminant >= 0f && closest > 0f;
                return intersects
                    ? origin + (direction * Mathf.Max(0f, closest - Mathf.Sqrt(discriminant)))
                    : minimum;
            }

            float exponent = Mathf.Lerp(8f, 2f, Mathf.Clamp01(roundness));
            float lower = float.PositiveInfinity;
            float upper = float.NegativeInfinity;
            for (int axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(direction[axis]) < 0.000001f)
                {
                    continue;
                }

                float crossing = -origin[axis] / direction[axis];
                lower = Mathf.Min(lower, crossing);
                upper = Mathf.Max(upper, crossing);
            }

            lower = Mathf.Max(0f, lower);
            upper = Mathf.Max(lower, upper);
            for (int iteration = 0; iteration < 32; iteration++)
            {
                float middle = (lower + upper) * 0.5f;
                Vector3 point = (origin + (direction * middle)) * 2f;
                float derivative = 0f;
                for (int axis = 0; axis < 3; axis++)
                {
                    derivative += Mathf.Sign(point[axis]) * Mathf.Pow(Mathf.Abs(point[axis]), exponent - 1f) * direction[axis];
                }

                if (derivative < 0f)
                {
                    lower = middle;
                }
                else
                {
                    upper = middle;
                }
            }

            float minimumDistance = (lower + upper) * 0.5f;
            Vector3 minimumPoint = origin + (direction * minimumDistance);
            intersects = EvaluateCubeEquation(minimumPoint, exponent) <= 1f && minimumDistance > 0f;
            if (!intersects)
            {
                return minimumPoint;
            }

            lower = 0f;
            upper = minimumDistance;
            for (int iteration = 0; iteration < 24; iteration++)
            {
                float middle = (lower + upper) * 0.5f;
                if (EvaluateCubeEquation(origin + (direction * middle), exponent) > 1f)
                {
                    lower = middle;
                }
                else
                {
                    upper = middle;
                }
            }

            return origin + (direction * ((lower + upper) * 0.5f));
        }

        private static float EvaluateCubeEquation(Vector3 point, float exponent)
        {
            return Mathf.Pow(Mathf.Abs(point.x * 2f), exponent)
                + Mathf.Pow(Mathf.Abs(point.y * 2f), exponent)
                + Mathf.Pow(Mathf.Abs(point.z * 2f), exponent);
        }
    }
}
