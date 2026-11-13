using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class ParabolicFloorLightTests
{
    private const int StationCount = 97;
    private const int CornersPerStation = 4;
    private const float CircleTolerance = 0.0001f;

    [Test]
    public void MenuFloorLightLifecycleIsAMenuSessionService()
    {
        Type? lifecycle = GetPluginTypeOrNull("ChromaGLS.Settings.ParabolicFloorLight");
        Assert.That(lifecycle, Is.Not.Null, "The floor light needs its own menu-owned service, independent of the ferrofluid toggles and the gameplay patch gate.");
        Assert.That(lifecycle!.GetInterface("Zenject.IInitializable"), Is.Not.Null, "The service must create the light when the menu container initializes.");
        Assert.That(lifecycle.GetInterface("System.IDisposable"), Is.Not.Null, "The service must tear the light down when the menu container disposes.");
    }

    [Test]
    public void CurveVertexSitsBehindPlayerAndIsTheDeepestPoint()
    {
        (Vector3[] vertices, _) = CreateLight(DefaultEnclosure(), Vector3.zero, Vector3.forward);

        Vector3 apex = RingCenter(vertices, 48);
        Assert.That(apex.x, Is.EqualTo(0f).Within(0.0001f));
        Assert.That(apex.z, Is.EqualTo(-0.35f).Within(0.0001f), "The curve vertex must sit 0.35m behind the player.");
        for (int station = 0; station < StationCount; station++)
            Assert.That(RingCenter(vertices, station).z, Is.GreaterThanOrEqualTo(apex.z - 0.0001f), $"Station {station} dips behind the curve vertex.");
    }

    [Test]
    public void CenterlineFollowsSymmetricParabolaOpeningForward()
    {
        (Vector3[] vertices, _) = CreateLight(DefaultEnclosure(), Vector3.zero, Vector3.forward);
        Vector3 apex = RingCenter(vertices, 48);

        for (int station = 0; station < StationCount; station++)
        {
            Vector3 center = RingCenter(vertices, station);
            float xRelative = center.x - apex.x;
            float zRelative = center.z - apex.z;
            Assert.That(zRelative, Is.EqualTo((0.75f / (3.2f * 3.2f)) * xRelative * xRelative).Within(0.001f), $"Station {station} leaves the parabola.");
        }

        Assert.That(RingCenter(vertices, 0).z - apex.z, Is.GreaterThan(0.5f), "The left branch must open forward of the vertex.");
        Assert.That(RingCenter(vertices, 96).z - apex.z, Is.GreaterThan(0.5f), "The right branch must open forward of the vertex.");
        for (int station = 0; station <= 48; station++)
        {
            Vector3 left = RingCenter(vertices, station);
            Vector3 right = RingCenter(vertices, 96 - station);
            Assert.That(left.x, Is.EqualTo(-right.x).Within(0.0001f), $"Stations {station} and {96 - station} are not mirrored.");
            Assert.That(left.z, Is.EqualTo(right.z).Within(0.0001f));
        }
    }

    [Test]
    public void StripKeepsConstantNormalWidthAndFloorHeights()
    {
        (Vector3[] vertices, _) = CreateLight(DefaultEnclosure(), Vector3.zero, Vector3.forward);

        for (int station = 0; station < StationCount; station++)
        {
            Vector3 topPositive = vertices[station * CornersPerStation];
            Vector3 topNegative = vertices[(station * CornersPerStation) + 1];
            Vector3 bottomNegative = vertices[(station * CornersPerStation) + 2];
            Vector3 bottomPositive = vertices[(station * CornersPerStation) + 3];
            Vector3 widthDirection = (topPositive - topNegative).normalized;
            Assert.That(Vector3.Distance(topPositive, topNegative), Is.EqualTo(0.175f).Within(0.00001f), $"Top width of station {station} is not 0.175m.");
            Assert.That(Vector3.Distance(bottomPositive, bottomNegative), Is.EqualTo(0.175f).Within(0.00001f), $"Bottom width of station {station} is not 0.175m.");
            Assert.That(topPositive.y, Is.EqualTo(0.006f).Within(0.00001f));
            Assert.That(topNegative.y, Is.EqualTo(0.006f).Within(0.00001f));
            Assert.That(bottomPositive.y, Is.EqualTo(-0.015f).Within(0.00001f));
            Assert.That(bottomNegative.y, Is.EqualTo(-0.015f).Within(0.00001f));

            Vector3 center = RingCenter(vertices, station);
            Vector3 tangent = station < StationCount - 1
                ? (RingCenter(vertices, station + 1) - center).normalized
                : (center - RingCenter(vertices, station - 1)).normalized;
            Assert.That(Vector3.Dot(widthDirection, tangent), Is.EqualTo(0f).Within(0.05f), $"Width of station {station} is not normal to the curve tangent.");
        }
    }

    [Test]
    public void DoubledForwardReachExtendsBeyondMenu()
    {
        object enclosure = DefaultEnclosure();
        (Vector3[] vertices, _) = CreateLight(enclosure, Vector3.zero, Vector3.forward);

        Vector3 apex = RingCenter(vertices, 48);
        AssertBranchDoublesPastCircle(vertices, apex, 0, Vector3.forward, Vector3.right, new Vector3(0f, 0f, -0.5f), 2.5f);
        AssertBranchDoublesPastCircle(vertices, apex, 96, Vector3.forward, Vector3.right, new Vector3(0f, 0f, -0.5f), 2.5f);
    }

    [Test]
    public void RotatedEnclosureExtendsEachBranchIndependently()
    {
        Vector3 player = new(1.25f, 0f, 0.4f);
        Vector3 forward = new Vector3(0.6f, 0f, 0.8f).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 center = player - (forward * 0.3f) + (right * 0.9f);
        object enclosure = CreateEnclosure(center, 2f, player - (forward * 2f), forward, 0.02f);
        (Vector3[] vertices, _) = CreateLight(enclosure, player, forward);

        Vector3 apex = RingCenter(vertices, 48);
        Vector3 expectedApex = player - (forward * 0.35f);
        Assert.That(apex.x, Is.EqualTo(expectedApex.x).Within(0.0001f), "The vertex must follow the rotated/translated player anchor.");
        Assert.That(apex.z, Is.EqualTo(expectedApex.z).Within(0.0001f));

        AssertBranchDoublesPastCircle(vertices, apex, 0, forward, right, center, 2f);
        AssertBranchDoublesPastCircle(vertices, apex, 96, forward, right, center, 2f);
        float leftReach = Vector3.Distance(RingCenter(vertices, 0), apex);
        float rightReach = Vector3.Distance(RingCenter(vertices, 96), apex);
        Assert.That(rightReach, Is.GreaterThan(leftReach + 0.2f), "An off-center enclosure must still give the two branches different lengths.");
    }

    [Test]
    public void EndFadeStartsJustBeforeMenuBoundaryAndReachesZeroAtTheTip()
    {
        MethodInfo evaluate = GetGeometryMethod("EvaluateEndFade");
        Assert.That((float)evaluate.Invoke(null, [0f])!, Is.EqualTo(1f).Within(0.000001f));
        Assert.That((float)evaluate.Invoke(null, [0.45f])!, Is.EqualTo(1f).Within(0.000001f), "the fade starts just before the menu boundary");
        Assert.That((float)evaluate.Invoke(null, [0.5f])!, Is.GreaterThan(0.9f).And.LessThan(1f), "the menu boundary itself stays near full brightness");
        Assert.That((float)evaluate.Invoke(null, [0.725f])!, Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That((float)evaluate.Invoke(null, [1f])!, Is.EqualTo(0f).Within(0.000001f));

        float previous = 1f;
        for (int step = 0; step <= 20; step++)
        {
            float opacity = (float)evaluate.Invoke(null, [step / 20f])!;
            Assert.That(opacity, Is.InRange(0f, 1f));
            Assert.That(opacity, Is.LessThanOrEqualTo(previous + 0.000001f), "the fade must be monotonic toward the tip");
            previous = opacity;
        }
    }

    [Test]
    public void CoreVertexColorsFadeAllFourCornersTogether()
    {
        object enclosure = DefaultEnclosure();
        object[] arguments = [enclosure, Vector3.zero, Vector3.forward, null!];
        Assert.That(GetGeometryMethod("TryCreate").Invoke(null, arguments), Is.EqualTo(true));
        object mesh = arguments[3]!;
        Type meshType = mesh.GetType();
        Vector3[] vertices = (Vector3[])meshType.GetField("Vertices", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(mesh)!;
        Color[] colors = (Color[])meshType.GetField("Colors", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(mesh)!;
        Vector2[] uv = (Vector2[])meshType.GetField("UV", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(mesh)!;

        Assert.That(colors.Length, Is.EqualTo(vertices.Length), "vertex fade data must cover every prism corner");
        Assert.That(uv.Length, Is.EqualTo(vertices.Length));
        Vector3 apex = RingCenter(vertices, 48);
        float leftDepth = RingCenter(vertices, 0).z - apex.z;
        float rightDepth = RingCenter(vertices, 96).z - apex.z;
        for (int station = 0; station < StationCount; station++)
        {
            float depth = RingCenter(vertices, station).z - apex.z;
            float progress = depth / (station <= 48 ? leftDepth : rightDepth);
            float t = Mathf.Clamp01((progress - 0.45f) / 0.55f);
            float expectedAlpha = 1f - (t * t * (3f - (2f * t)));
            float stationAlpha = colors[station * 4].a;
            Assert.That(stationAlpha, Is.EqualTo(expectedAlpha).Within(0.00001f), $"station {station} alpha must fade over the actual forward depth");
            for (int corner = 0; corner < CornersPerStation; corner++)
            {
                Color color = colors[(station * 4) + corner];
                Assert.That(color.a, Is.EqualTo(stationAlpha).Within(0.000001f), $"station {station} corner {corner} must share the longitudinal fade");
                Assert.That(color.r, Is.EqualTo(1f).And.EqualTo(color.g).And.EqualTo(color.b), "fade data stays white; the material supplies the tint");
            }
        }

        Assert.That(colors[48 * 4].a, Is.EqualTo(1f).Within(0.000001f), "the apex must be full brightness");
        Assert.That(colors[0].a, Is.EqualTo(0f).Within(0.000001f), "the left tip must fade to zero");
        Assert.That(colors[96 * 4].a, Is.EqualTo(0f).Within(0.000001f), "the right tip must fade to zero");
    }

    [Test]
    public void MeshIsFiniteWatertightNondegenerateAndOutwardFacing()
    {
        (Vector3[] vertices, int[] triangles) = CreateLight(DefaultEnclosure(), Vector3.zero, Vector3.forward);

        foreach (Vector3 vertex in vertices)
            Assert.That(IsFinite(vertex.x) && IsFinite(vertex.y) && IsFinite(vertex.z), Is.True, $"Non-finite mesh vertex {vertex}.");

        Assert.That(triangles.Length % 3, Is.EqualTo(0));
        foreach (int index in triangles)
            Assert.That(index, Is.InRange(0, vertices.Length - 1));

        Vector3[] centers = new Vector3[StationCount];
        for (int station = 0; station < StationCount; station++)
            centers[station] = RingCenter(vertices, station);

        Dictionary<long, int> edgeCounts = new();
        for (int triangle = 0; triangle < triangles.Length; triangle += 3)
        {
            int a = triangles[triangle];
            int b = triangles[triangle + 1];
            int c = triangles[triangle + 2];
            Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            Assert.That(normal.sqrMagnitude, Is.GreaterThan(0.00000001f), $"Triangle {triangle / 3} is degenerate.");
            CountEdge(edgeCounts, a, b);
            CountEdge(edgeCounts, b, c);
            CountEdge(edgeCounts, c, a);

            Vector3 centroid = (vertices[a] + vertices[b] + vertices[c]) / 3f;
            Vector3 interior = InteriorPoint(centers, centroid);
            Assert.That(Vector3.Dot(normal, centroid - interior), Is.GreaterThan(0f), $"Triangle {triangle / 3} faces into the prism.");
        }

        foreach (KeyValuePair<long, int> edge in edgeCounts)
            Assert.That(edge.Value, Is.EqualTo(2), "A watertight prism shares every edge between exactly two triangles.");
    }

    [Test]
    public void VertexOutsideEnclosureSkipsTheLight()
    {
        object tinyEnclosure = CreateEnclosure(new Vector3(0f, 0f, -0.5f), 0.1f, new Vector3(0f, 0f, -2f), Vector3.forward, 0f);
        MethodInfo tryCreate = GetGeometryMethod("TryCreate");
        object[] arguments = [tinyEnclosure, Vector3.zero, Vector3.forward, null!];
        Assert.That(tryCreate.Invoke(null, arguments), Is.EqualTo(false), "A vertex outside the enclosure circle must not produce geometry.");
        Assert.That(arguments[3], Is.Null);
    }

    private static void AssertBranchDoublesPastCircle(Vector3[] vertices, Vector3 apex, int endStation, Vector3 forward, Vector3 right, Vector3 circleCenter, float radius)
    {
        Vector3 endpoint = RingCenter(vertices, endStation);
        float oldX = Vector3.Dot(endpoint - apex, right) / Mathf.Sqrt(2f);
        float oldZ = Vector3.Dot(endpoint - apex, forward) / 2f;
        Assert.That(oldZ, Is.GreaterThanOrEqualTo(0f), "the reference reach must point forward of the apex");

        Vector3 reference = apex + (right * oldX) + (forward * oldZ);
        Vector3 tangent = right + (forward * (2f * (0.75f / (3.2f * 3.2f)) * oldX));
        Vector3 normal = Vector3.Cross(Vector3.up, tangent).normalized;
        float outermost = Mathf.Max(
            Radial(reference + (normal * 0.0875f), circleCenter),
            Radial(reference - (normal * 0.0875f), circleCenter));
        Assert.That(outermost, Is.InRange(radius - 0.02f, radius + CircleTolerance), $"the old menu boundary must sit at the half-depth reference point for station {endStation}");
        Assert.That(Radial(endpoint, circleCenter), Is.GreaterThan(radius), $"the doubled tip must pass the old menu boundary for station {endStation}");
        Assert.That(Vector3.Dot(endpoint - apex, forward), Is.EqualTo(2f * oldZ).Within(0.001f), "the tip's forward depth must be twice the reference");
    }

    private static object DefaultEnclosure()
    {
        return CreateEnclosure(new Vector3(0f, 0f, -0.5f), 2.5f, new Vector3(0f, 0f, -2f), Vector3.forward, 0f);
    }

    private static object CreateEnclosure(Vector3 center, float radius, Vector3 rearPoint, Vector3 rearNormal, float floorY)
    {
        Type enclosureType = GetPluginType("ChromaGLS.Settings.FerrofluidGrabEnclosure");
        ConstructorInfo? constructor = enclosureType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float)],
            null);
        Assert.That(constructor, Is.Not.Null, "The enclosure needs a direct constructor for deterministic tests.");
        return constructor!.Invoke([center, radius, rearPoint, rearNormal, floorY]);
    }

    private static (Vector3[] Vertices, int[] Triangles) CreateLight(object enclosure, Vector3 playerPosition, Vector3 forward)
    {
        MethodInfo tryCreate = GetGeometryMethod("TryCreate");
        object[] arguments = [enclosure, playerPosition, forward, null!];
        Assert.That(tryCreate.Invoke(null, arguments), Is.EqualTo(true), "A vertex inside the enclosure must produce floor light geometry.");
        object? mesh = arguments[3];
        Assert.That(mesh, Is.Not.Null);
        Type meshType = mesh!.GetType();
        Vector3[]? vertices = meshType.GetField("Vertices", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(mesh) as Vector3[];
        int[]? triangles = meshType.GetField("Triangles", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(mesh) as int[];
        Assert.That(vertices, Is.Not.Null);
        Assert.That(triangles, Is.Not.Null);
        Assert.That(vertices!.Length, Is.EqualTo(StationCount * CornersPerStation), "The strip needs 4 prism corners for each of its 97 stations.");
        Assert.That(triangles!.Length, Is.EqualTo(((96 * 8) + 4) * 3), "96 quad segments and two end caps need 772 triangles.");
        return (vertices, triangles);
    }

    private static MethodInfo GetGeometryMethod(string name)
    {
        Type? geometryType = GetPluginTypeOrNull("ChromaGLS.Settings.ParabolicFloorLightGeometry");
        Assert.That(geometryType, Is.Not.Null, "The floor light needs a managed geometry seam the renderer consumes verbatim.");
        MethodInfo? method = geometryType!.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing floor light geometry method: {name}");
        return method!;
    }

    private static Vector3 RingCenter(Vector3[] vertices, int station)
    {
        Vector3 sum = Vector3.zero;
        for (int corner = 0; corner < CornersPerStation; corner++)
            sum += vertices[(station * CornersPerStation) + corner];

        return sum / CornersPerStation;
    }

    private static float OutermostCornerRadial(Vector3[] vertices, int station, Vector3 center)
    {
        float outermost = 0f;
        for (int corner = 0; corner < CornersPerStation; corner++)
            outermost = Mathf.Max(outermost, Radial(vertices[(station * CornersPerStation) + corner], center));

        return outermost;
    }

    private static float Radial(Vector3 position, Vector3 center)
    {
        float dx = position.x - center.x;
        float dz = position.z - center.z;
        return Mathf.Sqrt((dx * dx) + (dz * dz));
    }

    private static Vector3 InteriorPoint(Vector3[] centers, Vector3 centroid)
    {
        int nearest = 0;
        int secondNearest = 0;
        float nearestDistance = float.MaxValue;
        float secondNearestDistance = float.MaxValue;
        for (int station = 0; station < centers.Length; station++)
        {
            float distance = Vector3.Distance(centers[station], centroid);
            if (distance < nearestDistance)
            {
                secondNearest = nearest;
                secondNearestDistance = nearestDistance;
                nearest = station;
                nearestDistance = distance;
            }
            else if (distance < secondNearestDistance)
            {
                secondNearest = station;
                secondNearestDistance = distance;
            }
        }

        return (centers[nearest] + centers[secondNearest]) * 0.5f;
    }

    private static void CountEdge(Dictionary<long, int> edgeCounts, int from, int to)
    {
        long key = from < to
            ? ((long)from << 32) | (uint)to
            : ((long)to << 32) | (uint)from;
        edgeCounts[key] = edgeCounts.TryGetValue(key, out int count)
            ? count + 1
            : 1;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static Type? GetPluginTypeOrNull(string typeName)
    {
        return Assembly.Load("ChromaGLS").GetType(typeName, throwOnError: false);
    }

    private static Type GetPluginType(string typeName)
    {
        return GetPluginTypeOrNull(typeName)
            ?? throw new AssertionException($"Missing plugin type: {typeName}");
    }
}
