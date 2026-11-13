using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChromaGLS.Settings
{
    internal static class FerrofluidPrimitiveLayout
    {
        private static readonly Dictionary<(FerrofluidSurfaceShape Shape, int Count, float Roundness), Layout> Cache = new();

        internal static Layout Get(FerrofluidSurfaceShape shape, int count, float roundness)
        {
            var key = (shape, count, roundness);
            if (Cache.TryGetValue(key, out Layout layout))
                return layout;

            CreateSamples(shape, count, roundness, out Vector3[] samples, out float[] weights, out float area);
            float spacing = Mathf.Sqrt(area / (count * Mathf.Sqrt(3f) * 0.5f));
            int pinned = 0;
            Vector3[] positions;
            if (shape == FerrofluidSurfaceShape.RoundedCube)
            {
                positions = SeedCube(count, roundness, spacing, out pinned);
            }
            else
            {
                positions = new Vector3[count];
                for (int index = 0; index < count; index++)
                    positions[index] = EvaluateFibonacciDirection(index, count) * 0.5f;
            }

            Relax(shape, roundness, positions, pinned, samples, weights, spacing);
            layout = new Layout(positions, pinned);
            if (Cache.Count >= 24)
                Cache.Clear();

            Cache.Add(key, layout);
            return layout;
        }

        internal static Vector3 EvaluateFibonacciDirection(int index, int count)
        {
            float y = 1f - ((index + 0.5f) * 2f / count);
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - (y * y)));
            float angle = index * Mathf.PI * (3f - Mathf.Sqrt(5f));
            return new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
        }

        private static Vector3 Project(FerrofluidSurfaceShape shape, Vector3 direction, float roundness)
        {
            return FerrofluidEnvironmentObjects.EvaluateSurfacePositionFromDirection(shape, direction, roundness);
        }

        private static void CreateSamples(
            FerrofluidSurfaceShape shape,
            int count,
            float roundness,
            out Vector3[] samples,
            out float[] weights,
            out float area)
        {
            if (shape == FerrofluidSurfaceShape.Sphere)
            {
                samples = new Vector3[count * 24];
                weights = new float[samples.Length];
                for (int index = 0; index < samples.Length; index++)
                {
                    samples[index] = EvaluateFibonacciDirection(index, samples.Length) * 0.5f;
                    weights[index] = 1f;
                }

                area = Mathf.PI;
                return;
            }

            int side = Mathf.CeilToInt(Mathf.Sqrt(count * 4f));
            samples = new Vector3[side * side * 6];
            weights = new float[samples.Length];
            float step = 2f / side;
            float exponent = 8f - (6f * roundness);
            area = 0f;
            int emitted = 0;
            for (int row = 0; row < side; row++)
            {
                float v = -1f + ((row + 0.5f) * step);
                for (int column = 0; column < side; column++)
                {
                    float u = -1f + ((column + 0.5f) * step);
                    Vector3 q = new(u, v, 1f);
                    float sum = Mathf.Pow(Mathf.Abs(u), exponent) + Mathf.Pow(Mathf.Abs(v), exponent) + 1f;
                    float radius = 0.5f / Mathf.Pow(sum, 1f / exponent);
                    float gu = Mathf.Sign(u) * Mathf.Pow(Mathf.Abs(u), exponent - 1f) / sum;
                    float gv = Mathf.Sign(v) * Mathf.Pow(Mathf.Abs(v), exponent - 1f) / sum;
                    Vector3 du = radius * (Vector3.right - (q * gu));
                    Vector3 dv = radius * (Vector3.up - (q * gv));
                    float weight = Vector3.Cross(du, dv).magnitude * step * step;
                    for (int axis = 0; axis < 3; axis++)
                    {
                        for (int sign = -1; sign <= 1; sign += 2)
                        {
                            samples[emitted] = Project(shape, Cycle(new Vector3(u, v, sign), axis), roundness);
                            weights[emitted++] = weight;
                            area += weight;
                        }
                    }
                }
            }
        }

        private static Vector3 Cycle(Vector3 value, int axis)
        {
            return axis == 0
                ? value
                : axis == 1
                    ? new Vector3(value.z, value.x, value.y)
                    : new Vector3(value.y, value.z, value.x);
        }

        private static Vector3[] SeedCube(int count, float roundness, float spacing, out int pinned)
        {
            const FerrofluidSurfaceShape Shape = FerrofluidSurfaceShape.RoundedCube;
            Vector3 middle = Project(Shape, new Vector3(0f, 1f, 1f), roundness);
            float low = 0f;
            float high = 0.75f;
            for (int iteration = 0; iteration < 32; iteration++)
            {
                float delta = (low + high) * 0.5f;
                Vector3 point = Project(Shape, new Vector3(0f, 1f, 1f - delta), roundness);
                if (Vector3.Distance(point, middle) < spacing * Mathf.Sqrt(3f) * 0.25f)
                    low = delta;
                else
                    high = delta;
            }

            float inset = (low + high) * 0.5f;
            Vector3 corner = Project(Shape, Vector3.one, roundness);
            low = 0f;
            high = 1f;
            for (int iteration = 0; iteration < 32; iteration++)
            {
                float t = (low + high) * 0.5f;
                Vector3 point = Project(Shape, new Vector3(t, 1f, 1f - inset), roundness);
                if (Vector3.Distance(point, corner) > spacing)
                    low = t;
                else
                    high = t;
            }

            float maximum = (low + high) * 0.5f;
            float span = Vector3.Distance(
                Project(Shape, new Vector3(-maximum, 1f, 1f), roundness),
                Project(Shape, new Vector3(maximum, 1f, 1f), roundness));
            int edgeCount = Mathf.Max(2, 2 * Mathf.RoundToInt(span / spacing));
            edgeCount = Mathf.Min(edgeCount, Mathf.Max(2, (((count / 2) - 8) / 24) * 2));
            List<Vector3> edge = new();
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                        edge.Add(Project(Shape, new Vector3(x, y, z), roundness));
                }
            }

            for (int index = 0; index < edgeCount / 2; index++)
            {
                float t = -maximum + (index * 2f * maximum / (edgeCount - 1));
                Vector3 point = index % 2 == 0
                    ? new Vector3(t, 1f, 1f - inset)
                    : new Vector3(t, 1f - inset, 1f);
                AddCubeRotations(edge, point, roundness);
            }

            pinned = edge.Count;
            Vector3[] positions = new Vector3[count];
            edge.CopyTo(positions);
            List<Vector3> candidates = new();
            int extent = Mathf.CeilToInt(2f / spacing);
            for (int row = -extent; row <= extent; row++)
            {
                for (int column = -extent; column <= extent; column++)
                {
                    float u = (column + ((row & 1) * 0.5f)) * spacing;
                    float v = row * spacing * Mathf.Sqrt(3f) * 0.5f;
                    if (Mathf.Abs(u) >= 1f || Mathf.Abs(v) >= 1f)
                        continue;

                    for (int axis = 0; axis < 3; axis++)
                    {
                        for (int sign = -1; sign <= 1; sign += 2)
                            candidates.Add(Project(Shape, Cycle(new Vector3(u, v, sign), axis), roundness));
                    }
                }
            }

            float[] distances = new float[candidates.Count];
            for (int index = 0; index < candidates.Count; index++)
            {
                distances[index] = float.MaxValue;
                foreach (Vector3 boundary in edge)
                    distances[index] = Mathf.Min(distances[index], (candidates[index] - boundary).sqrMagnitude);
            }

            SurfaceGrid candidateGrid = new(candidates.ToArray(), spacing * 1.5f);
            candidateGrid.Rebuild();
            for (int emitted = pinned; emitted < count; emitted++)
            {
                int best = 0;
                for (int index = 1; index < distances.Length; index++)
                {
                    if (distances[index] > distances[best])
                        best = index;
                }

                Vector3 chosen = candidates[best];
                positions[emitted] = chosen;
                candidateGrid.UpdateNearestDistances(chosen, distances[best], distances);
            }

            return positions;
        }

        private static void AddCubeRotations(List<Vector3> positions, Vector3 point, float roundness)
        {
            for (int a = 0; a < 3; a++)
            {
                for (int b = 0; b < 3; b++)
                {
                    if (a == b)
                        continue;

                    int c = 3 - a - b;
                    int parity = ((a - b) * (b - c) * (c - a)) / 2;
                    for (int x = -1; x <= 1; x += 2)
                    {
                        for (int y = -1; y <= 1; y += 2)
                        {
                            int z = parity * x * y;
                            Vector3 rotated = new(point[a] * x, point[b] * y, point[c] * z);
                            positions.Add(Project(FerrofluidSurfaceShape.RoundedCube, rotated, roundness));
                        }
                    }
                }
            }
        }

        private static void Relax(
            FerrofluidSurfaceShape shape,
            float roundness,
            Vector3[] positions,
            int pinned,
            Vector3[] samples,
            float[] weights,
            float spacing)
        {
            SurfaceGrid grid = new(positions, spacing * 1.5f);
            Vector3[] sums = new Vector3[positions.Length];
            float[] masses = new float[positions.Length];
            for (int iteration = 0; iteration < 48; iteration++)
            {
                grid.Rebuild();
                Array.Clear(sums, 0, sums.Length);
                Array.Clear(masses, 0, masses.Length);
                for (int sample = 0; sample < samples.Length; sample++)
                {
                    int owner = grid.Nearest(samples[sample]);
                    sums[owner] += samples[sample] * weights[sample];
                    masses[owner] += weights[sample];
                }

                // Boundary sites define the edge pattern only during baking, not during interaction.
                for (int index = pinned; index < positions.Length; index++)
                {
                    if (masses[index] == 0f)
                        continue;

                    Vector3 displacement = (sums[index] / masses[index]) - positions[index];
                    FerrofluidEnvironmentObjects.EvaluateSurfacePositionAndNormal(
                        shape,
                        positions[index],
                        roundness,
                        out _,
                        out Vector3 normal);
                    displacement -= normal * Vector3.Dot(displacement, normal);
                    positions[index] = Project(shape, positions[index] + (displacement * 0.6f), roundness);
                }
            }
        }

        internal sealed class Layout
        {
            internal Layout(Vector3[] positions, int pinnedCount)
            {
                Positions = positions;
                PinnedCount = pinnedCount;
            }

            internal Vector3[] Positions { get; }

            internal int PinnedCount { get; }
        }

        private sealed class SurfaceGrid
        {
            private readonly Vector3[] _positions;
            private readonly int[] _heads;
            private readonly int[] _next;
            private readonly float _cellSize;
            private readonly int _axis;

            internal SurfaceGrid(Vector3[] positions, float cellSize)
            {
                _positions = positions;
                _cellSize = cellSize;
                _axis = Mathf.CeilToInt(1f / cellSize) + 2;
                _heads = new int[_axis * _axis * _axis];
                _next = new int[positions.Length];
            }

            internal void Rebuild()
            {
                for (int cell = 0; cell < _heads.Length; cell++)
                    _heads[cell] = -1;

                for (int index = 0; index < _positions.Length; index++)
                {
                    Vector3 point = _positions[index];
                    int cell = Cell(Coordinate(point.x), Coordinate(point.y), Coordinate(point.z));
                    _next[index] = _heads[cell];
                    _heads[cell] = index;
                }
            }

            internal int Nearest(Vector3 point)
            {
                int x = Coordinate(point.x);
                int y = Coordinate(point.y);
                int z = Coordinate(point.z);
                float fx = ((point.x + 0.5f) / _cellSize) - (x - 1);
                float fy = ((point.y + 0.5f) / _cellSize) - (y - 1);
                float fz = ((point.z + 0.5f) / _cellSize) - (z - 1);
                float edge = Mathf.Min(Mathf.Min(fx, 1f - fx), Mathf.Min(Mathf.Min(fy, 1f - fy), Mathf.Min(fz, 1f - fz)));
                float nearest = float.MaxValue;
                int owner = -1;
                for (int shell = 0; shell < _axis; shell++)
                {
                    int minX = Mathf.Max(0, x - shell);
                    int maxX = Mathf.Min(_axis - 1, x + shell);
                    int minY = Mathf.Max(0, y - shell);
                    int maxY = Mathf.Min(_axis - 1, y + shell);
                    int minZ = Mathf.Max(0, z - shell);
                    int maxZ = Mathf.Min(_axis - 1, z + shell);
                    for (int cz = minZ; cz <= maxZ; cz++)
                    {
                        for (int cy = minY; cy <= maxY; cy++)
                        {
                            for (int cx = minX; cx <= maxX; cx++)
                            {
                                int ring = Mathf.Max(Mathf.Abs(cx - x), Mathf.Max(Mathf.Abs(cy - y), Mathf.Abs(cz - z)));
                                if (ring != shell)
                                    continue;

                                for (int index = _heads[Cell(cx, cy, cz)]; index >= 0; index = _next[index])
                                {
                                    float distance = (_positions[index] - point).sqrMagnitude;
                                    if (distance < nearest)
                                    {
                                        nearest = distance;
                                        owner = index;
                                    }
                                }
                            }
                        }
                    }

                    // Include the query's distance from its own cell boundary before opening another shell.
                    float outside = (shell + edge) * _cellSize;
                    if (nearest <= outside * outside)
                        return owner;
                }

                return owner;
            }

            internal void UpdateNearestDistances(Vector3 point, float maximumSquared, float[] distances)
            {
                // No candidate can improve outside the current largest gap; its existing distance is already smaller.
                float radius = Mathf.Sqrt(maximumSquared);
                int minX = Mathf.Max(0, Coordinate(point.x - radius) - 1);
                int maxX = Mathf.Min(_axis - 1, Coordinate(point.x + radius) + 1);
                int minY = Mathf.Max(0, Coordinate(point.y - radius) - 1);
                int maxY = Mathf.Min(_axis - 1, Coordinate(point.y + radius) + 1);
                int minZ = Mathf.Max(0, Coordinate(point.z - radius) - 1);
                int maxZ = Mathf.Min(_axis - 1, Coordinate(point.z + radius) + 1);
                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        for (int x = minX; x <= maxX; x++)
                        {
                            for (int index = _heads[Cell(x, y, z)]; index >= 0; index = _next[index])
                                distances[index] = Mathf.Min(distances[index], (_positions[index] - point).sqrMagnitude);
                        }
                    }
                }
            }

            private int Coordinate(float value)
            {
                return Mathf.Clamp(Mathf.FloorToInt((value + 0.5f) / _cellSize) + 1, 0, _axis - 1);
            }

            private int Cell(int x, int y, int z)
            {
                return (((z * _axis) + y) * _axis) + x;
            }
        }
    }
}
