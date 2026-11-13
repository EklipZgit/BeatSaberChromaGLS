using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidSurfaceDistribution
    {
        internal static float MeanSpacing(Vector3[] sites)
        {
            double sum = 0.0;
            for (int index = 0; index < sites.Length; index++)
                sum += Mathf.Sqrt(NearestSquared(sites[index], sites, index));

            return (float)(sum / sites.Length);
        }

        internal static float MaximumHole(Vector3[] sites, Vector3[] probes)
        {
            float maximum = 0f;
            foreach (Vector3 probe in probes)
                maximum = Mathf.Max(maximum, NearestSquared(probe, sites));

            return Mathf.Sqrt(maximum);
        }

        internal static float NearestSquared(Vector3 point, Vector3[] sites, int excluded = -1)
        {
            float nearest = float.MaxValue;
            for (int index = 0; index < sites.Length; index++)
            {
                if (index != excluded)
                    nearest = Mathf.Min(nearest, (sites[index] - point).sqrMagnitude);
            }

            return nearest;
        }
    }
}
