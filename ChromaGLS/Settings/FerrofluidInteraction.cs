using UnityEngine;

namespace ChromaGLS.Settings
{
    internal static class FerrofluidInteraction
    {
        // Solve the damped spring analytically so controller response stays consistent across headset refresh rates.
        internal static Vector3 SmoothPosition(Vector3 current, Vector3 target, ref Vector3 velocity, float deltaTime)
        {
            const float smoothTime = 0.06f;
            const float omega = 2f / smoothTime;
            float decay = Mathf.Exp(-omega * deltaTime);
            Vector3 error = current - target;
            Vector3 impulse = (velocity + (error * omega)) * deltaTime;
            velocity = (velocity - (impulse * omega)) * decay;
            return target + ((error + impulse) * decay);
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
