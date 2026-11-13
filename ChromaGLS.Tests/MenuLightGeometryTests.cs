using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class MenuLightGeometryTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [TestCase("Square")]
    [TestCase("Circle")]
    [TestCase("Cube")]
    [TestCase("Sphere")]
    public void PoseIncludesPlayerFrameOffsetsAndReferenceHeight(string type)
    {
        object light = NewLight(type);
        Set(light, "PositionX", 0.7f);
        Set(light, "PositionZ", -0.3f);
        Set(light, "Height", 1.2f);
        Invoke(light, "RotateAxis", 0, 90f);
        Invoke(light, "RotateAxis", 2, 35f);
        object meshes = Create(light, new Vector3(2f, 0f, -1f), Vector3.right, 0.4f);
        Vector3 position = Field<Vector3>(meshes, "Position");
        AssertVector(position, new Vector3(1.7f, 1.6f, -1.7f));
        Quaternion expected = Quaternion.AngleAxis(90f, Vector3.up)
            * Quaternion.AngleAxis(90f, Vector3.right)
            * Quaternion.AngleAxis(35f, Vector3.forward);
        AssertVector(Field<Quaternion>(meshes, "Rotation") * Vector3.forward, expected * Vector3.forward);
    }

    [Test]
    public void FloorHasIndependentCoreGlowAndUndersideHeights()
    {
        object light = NewLight("Floor");
        Set(light, "Depth", 0.17f);
        object meshes = Create(light);
        Vector3[] vertices = Vertices(meshes, "Core");
        Assert.That(vertices.Max(vertex => vertex.y), Is.EqualTo(0f).Within(0.00001f));
        Assert.That(vertices.Min(vertex => vertex.y), Is.EqualTo(-0.02f).Within(0.00001f), "floor depth follows height so the bottom stays floor-flush, stale Depth is ignored");
        Assert.That(Field<Vector3>(meshes, "Position").y, Is.EqualTo(0.02f).Within(0.00001f));
        Assert.That(Vertices(meshes, "Glow"), Is.Not.Empty);
        foreach (Vector3 vertex in Vertices(meshes, "Glow"))
            Assert.That(vertex.y, Is.EqualTo(-0.019f).Within(0.00001f));
    }

    [TestCase("Parabola", 0f)]
    [TestCase("Square", 0f)]
    [TestCase("Circle", 0f)]
    [TestCase("Cube", 1f)]
    [TestCase("Sphere", 1f)]
    [TestCase("Floor", 0f)]
    public void OnlyFloorGlowHoversOneMillimetreAboveTheFloor(string type, float coreTopExtra)
    {
        const float floor = 0.4f;
        object light = NewLight(type);
        if (type == "Parabola")
        {
            Set(light, "Height", .006f);
            Set(light, "Depth", .02f);
            Set(light, "RotationX", 0f);
            Set(light, "RotationZ", 0f);
            Set(light, "QuaternionX", 0f);
            Set(light, "QuaternionY", 0f);
            Set(light, "QuaternionZ", 0f);
            Set(light, "QuaternionW", 1f);
        }

        object meshes = Create(light, new Vector3(0.5f, 0f, -0.2f), new Vector3(0.6f, 0f, 0.8f), floor);
        Vector3 position = Field<Vector3>(meshes, "Position");
        Quaternion rotation = Field<Quaternion>(meshes, "Rotation");
        float height = type == "Floor" ? .02f : .006f;
        Assert.That(position.y, Is.EqualTo(floor + height).Within(0.00001f));
        Assert.That(Vertices(meshes, "Glow").Length, type == "Floor" ? Is.GreaterThan(0) : Is.EqualTo(0));
        Vector3 ToWorld(Vector3 local) => position + (rotation * local);
        foreach (Vector3 vertex in Vertices(meshes, "Glow"))
            Assert.That(ToWorld(vertex).y, Is.EqualTo(floor + 0.001f).Within(0.00001f), type + " glow must hug the floor so its edges cannot overlap the lit strip at grazing angles");
        Vector3[] reflection = Vertices(meshes, "ReflectionCore");
        foreach (int index in Triangles(meshes, "ReflectionCore"))
            Assert.That(ToWorld(reflection[index]).y, Is.EqualTo(floor + height).Within(0.00001f), type + " reflection must keep sampling the reference surface");
        Vector3[] core = Vertices(meshes, "Core");
        Assert.That(core.Max(vertex => ToWorld(vertex).y), Is.EqualTo(floor + height + coreTopExtra).Within(0.00001f));
        Assert.That(core.Min(vertex => ToWorld(vertex).y), Is.EqualTo(floor + height - .02f).Within(0.00001f), type + " core underside depth must not change");
    }

    [Test]
    public void LegacyGlowHoversOneMillimetreAboveTheFloor()
    {
        const float floor = 0.4f;
        object enclosure = NewEnclosure(new Vector3(0f, 0f, -0.5f), floor);
        Type geometry = PluginType("ParabolicFloorLightGeometry");
        object?[] arguments = { enclosure, Vector3.zero, Vector3.forward, null };
        Assert.That((bool)geometry.GetMethod("TryCreate", Members)!.Invoke(null, arguments)!, Is.True, "the strip must generate inside the enclosure");
        object core = arguments[3]!;
        Vector3[] coreVertices = Field<Vector3[]>(core, "Vertices");
        Assert.That(coreVertices.Max(vertex => vertex.y), Is.EqualTo(floor + 0.006f).Within(0.00001f), "the default strip top is 6mm above the floor");
        Assert.That(coreVertices.Min(vertex => vertex.y), Is.EqualTo(floor - 0.015f).Within(0.00001f), "the default strip underside is 15mm below the floor");
        object settings = PluginType("ParabolicFloorLightSettings").GetField("Default", Members)!.GetValue(null)!;
        object glow = geometry.GetMethod("CreateGlow", Members)!.Invoke(null, new[] { enclosure, core, settings })!;
        foreach (Vector3 vertex in Field<Vector3[]>(glow, "Vertices"))
            Assert.That(vertex.y, Is.EqualTo(floor + 0.001f).Within(0.00001f), "the legacy floor glow must hover 1mm above the floor");
    }

    [Test]
    public void ParabolaExplicitLengthResamplesTheCurveRatherThanClampingItsWidth()
    {
        object light = NewLight("Parabola");
        Set(light, "EndLength", 0.6f);
        Vector3[] vertices = Vertices(Create(light), "Core");
        int final = vertices.Length - 4;
        Assert.That(((vertices[0] + vertices[1]) * 0.5f).z, Is.EqualTo(0.6f).Within(0.0001f));
        Assert.That(((vertices[final] + vertices[final + 1]) * 0.5f).z, Is.EqualTo(0.6f).Within(0.0001f));
        for (int station = 0; station < vertices.Length / 4; station++)
        {
            int corner = station * 4;
            Assert.That(Vector3.Distance(vertices[corner], vertices[corner + 1]), Is.EqualTo(0.18375f).Within(0.0001f));
        }
    }

    [Test]
    public void ParabolaFadeStartChangesSourceEmission()
    {
        object light = NewLight("Parabola");
        Set(light, "EndLength", 2f);
        Set(light, "FadeStart", 0.2f);
        Color[] early = Colors(Create(light), "Core");
        Set(light, "FadeStart", 0.8f);
        Color[] late = Colors(Create(light), "Core");
        Assert.That(early[84 * 4].a, Is.LessThan(late[84 * 4].a));
        Assert.That(early[0].a, Is.EqualTo(0.1f).Within(0.00001f));
        Assert.That(late[late.Length - 1].a, Is.EqualTo(0.1f).Within(0.00001f));
        Assert.That(early[48 * 4].a, Is.EqualTo(1f).Within(0.00001f));
    }

    [TestCase("Parabola")]
    [TestCase("Floor")]
    public void FadedRibbonEndsKeepAMinimumEmission(string type)
    {
        object light = NewLight(type);
        Set(light, "EndLength", 2f);
        Set(light, "FadeStart", 0.13f);
        float[] earlyStations = StationEmission(type, Create(light), "Core");
        Set(light, "FadeStart", 0.8f);
        float[] lateStations = StationEmission(type, Create(light), "Core");
        Assert.That(earlyStations.Min(), Is.EqualTo(0.1f).Within(0.00001f));
        Assert.That(earlyStations.Max(), Is.EqualTo(1f).Within(0.00001f));
        Assert.That(earlyStations[0], Is.EqualTo(0.1f).Within(0.00001f), "the first physical end must keep residual emission");
        Assert.That(earlyStations[earlyStations.Length - 1], Is.EqualTo(0.1f).Within(0.00001f), "the last physical end must keep residual emission");
        int branch = earlyStations.Length / 2;
        for (int station = 1; station <= branch; station++)
        {
            Assert.That(earlyStations[station - 1], Is.LessThanOrEqualTo(earlyStations[station] + 0.00001f), "emission must fall monotonically from the apex");
            Assert.That(earlyStations[earlyStations.Length - station], Is.LessThanOrEqualTo(earlyStations[earlyStations.Length - 1 - station] + 0.00001f));
        }

        Assert.That(earlyStations[84], Is.LessThan(lateStations[84]), "an early fade start must dim a fixed interior station more");

        float[] reflection = StationEmission(type, Create(light), "ReflectionCore");
        Assert.That(reflection.Min(), Is.EqualTo(0.1f).Within(0.00001f));
        Assert.That(reflection.Max(), Is.EqualTo(1f).Within(0.00001f));
    }

    [TestCase("Parabola")]
    [TestCase("Square")]
    [TestCase("Circle")]
    [TestCase("Cube")]
    [TestCase("Sphere")]
    public void ReflectionCoreUsesOnlyTheReferenceSurface(string type)
    {
        object meshes = Create(NewLight(type));
        Vector3[] vertices = Vertices(meshes, "ReflectionCore");
        foreach (int index in Triangles(meshes, "ReflectionCore"))
            Assert.That(vertices[index].y, Is.EqualTo(0f).Within(0.00001f), type + " must not bake its bottom face");
    }

    [TestCase("Square")]
    [TestCase("Circle")]
    public void FrameIsClosedHasCorrectWidthAndAHollowCenter(string type)
    {
        object meshes = Create(NewLight(type));
        Vector3[] vertices = Vertices(meshes, "Core");
        AssertVector(vertices[0], vertices[vertices.Length - 4]);
        AssertVector(vertices[1], vertices[vertices.Length - 3]);
        float expectedSpan = type == "Square" ? 0.175f * Mathf.Sqrt(2f) : 0.175f;
        Assert.That(Vector3.Distance(vertices[0], vertices[1]), Is.EqualTo(expectedSpan).Within(0.00001f));
        int[] indices = Triangles(meshes, "Core");
        float topArea = 0f;
        for (int triangle = 0; triangle < indices.Length; triangle += 3)
        {
            Vector3 a = vertices[indices[triangle]];
            Vector3 b = vertices[indices[triangle + 1]];
            Vector3 c = vertices[indices[triangle + 2]];
            if (a.y != 0f || b.y != 0f || c.y != 0f)
                continue;
            Vector3 cross = Vector3.Cross(b - a, c - a);
            Assert.That(cross.y, Is.GreaterThan(0f), "top faces must point toward local +Y");
            topArea += cross.y * 0.5f;
            Assert.That(ContainsOrigin(a, b, c), Is.False, "no top triangle may cover the hollow center");
        }

        float expectedArea = type == "Square"
            ? 1f - (0.65f * 0.65f)
            : 48f * Mathf.Sin(2f * Mathf.PI / 96f) * ((0.5f * 0.5f) - (0.325f * 0.325f));
        Assert.That(topArea, Is.EqualTo(expectedArea).Within(0.0001f));
    }

    [TestCase("Cube")]
    [TestCase("Sphere")]
    public void SolidTrianglesFaceOutward(string type)
    {
        object meshes = Create(NewLight(type));
        Vector3[] vertices = Vertices(meshes, "Core");
        int[] triangles = Triangles(meshes, "Core");
        Vector3 center = new(0f, 0.5f, 0f);
        for (int index = 0; index < triangles.Length; index += 3)
        {
            Vector3 a = vertices[triangles[index]];
            Vector3 b = vertices[triangles[index + 1]];
            Vector3 c = vertices[triangles[index + 2]];
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 0.0000000001f)
                continue;
            Assert.That(Vector3.Dot(normal, ((a + b + c) / 3f) - center), Is.GreaterThan(0f), type + " has an inward face");
        }
    }

    [TestCase("Square")]
    [TestCase("Circle")]
    public void GlowVerticesDoNotEnterTheMaterialFootprint(string type)
    {
        Assert.That(Vertices(Create(NewLight(type)), "Glow"), Is.Empty, "movable frames use native bloom instead of planar glow");
    }

    [TestCase("Parabola")]
    [TestCase("Square")]
    [TestCase("Circle")]
    [TestCase("Cube")]
    [TestCase("Sphere")]
    [TestCase("Floor")]
    public void InnerBloomBrightnessScalesOnlyItsRgb(string type)
    {
        object light = NewLight(type);
        ChangeAppearance(light, "WithHdrGlowBrightness", 1f);
        object first = Create(light);
        ChangeAppearance(light, "WithHdrGlowBrightness", 2f);
        object second = Create(light);
        Assert.That(Vertices(second, "Glow"), Is.EqualTo(Vertices(first, "Glow")));
        Assert.That(Colors(second, "Core"), Is.EqualTo(Colors(first, "Core")));
        Color[] original = Colors(first, "Glow");
        Color[] brighter = Colors(second, "Glow");
        if (type != "Floor")
        {
            Assert.That(original, Is.Empty, "custom halo settings must not recreate a planar halo on movable lights");
            return;
        }
        int changed = 0;
        for (int index = 0; index < original.Length; index++)
        {
            Assert.That(brighter[index].a, Is.EqualTo(original[index].a));
            if (original[index].r == brighter[index].r && original[index].g == brighter[index].g && original[index].b == brighter[index].b)
                continue;
            changed++;
            Assert.That(brighter[index].r, Is.EqualTo(2f * original[index].r).Within(0.00001f));
            Assert.That(brighter[index].g, Is.EqualTo(2f * original[index].g).Within(0.00001f));
            Assert.That(brighter[index].b, Is.EqualTo(2f * original[index].b).Within(0.00001f));
        }

        Assert.That(changed, Is.GreaterThan(0), "Floor must expose its HDR inner halo");
        Assert.That(changed, Is.LessThan(original.Length), "the broad spill must not change");
    }

    [TestCase("Parabola")]
    [TestCase("Square")]
    [TestCase("Circle")]
    [TestCase("Cube")]
    [TestCase("Sphere")]
    [TestCase("Floor")]
    public void InnerBloomThresholdOnlyChangesAlpha(string type)
    {
        object light = NewLight(type);
        ChangeAppearance(light, "WithHdrGlowThreshold", 0.5f);
        object first = Create(light);
        ChangeAppearance(light, "WithHdrGlowThreshold", 10f);
        object second = Create(light);
        Assert.That(Vertices(second, "Glow"), Is.EqualTo(Vertices(first, "Glow")));
        Assert.That(Colors(second, "Core"), Is.EqualTo(Colors(first, "Core")));
        Color[] original = Colors(first, "Glow");
        Color[] gated = Colors(second, "Glow");
        if (type != "Floor")
        {
            Assert.That(original, Is.Empty, "custom halo settings must not recreate a planar halo on movable lights");
            return;
        }
        int changed = 0;
        for (int index = 0; index < original.Length; index++)
        {
            Assert.That(gated[index].r, Is.EqualTo(original[index].r));
            Assert.That(gated[index].g, Is.EqualTo(original[index].g));
            Assert.That(gated[index].b, Is.EqualTo(original[index].b));
            if (original[index].a == gated[index].a)
                continue;
            changed++;
            Assert.That(gated[index].a, Is.EqualTo(0f));
        }

        Assert.That(changed, Is.GreaterThan(0), "HDR threshold must control the Floor halo");
        Assert.That(changed, Is.LessThan(original.Length), "threshold must not gate the broad spill");
    }

    [TestCase("Square")]
    [TestCase("Circle")]
    [TestCase("Cube")]
    [TestCase("Sphere")]
    [TestCase("Floor")]
    public void BroadBloomReachRespondsToItsWidthNotMaterialThickness(string type)
    {
        object light = NewLight(type);
        if (type != "Floor")
        {
            Assert.That(Vertices(Create(light), "Glow"), Is.Empty);
            ChangeAppearance(light, "WithBloomWidthScale", 2f);
            Assert.That(Vertices(Create(light), "Glow"), Is.Empty, "a width change must not revive a non-floor halo");
            return;
        }

        Set(light, "EndLength", 2f);
        float Reach()
        {
            // Compare one station's broad-band outer edge to its own emitter boundary; global
            // X maxima can sit at different stations when width changes.
            object meshes = Create(light);
            Vector3[] glow = Vertices(meshes, "Glow");
            return Vector3.Distance(glow[(48 * 14) + 0], glow[(48 * 14) + 6]);
        }

        ChangeAppearance(light, "WithLightWidthScale", 0.25f);
        float thin = Reach();
        ChangeAppearance(light, "WithLightWidthScale", 1f);
        float thick = Reach();
        Assert.That(thick, Is.EqualTo(thin).Within(0.00001f));
        ChangeAppearance(light, "WithBloomWidthScale", 2f);
        Assert.That(Reach(), Is.GreaterThan(thick));
    }

    [Test]
    public void SourceTipTransparencyDoesNotCopyTheSameMaskIntoFloorSpill()
    {
        object meshes = Create(NewLight("Floor"));
        Assert.That(Colors(meshes, "Core")[0].a, Is.EqualTo(0.1f).Within(0.00001f));
        Assert.That(Colors(meshes, "Glow")[7].a, Is.GreaterThan(0f), "spill must extend smoothly from nearby source samples");
        Assert.That(Colors(meshes, "Glow")[7].a, Is.LessThan(0.1f), "spill keeps fading to zero past the residual core emission");
    }

    [TestCase("Parabola")]
    [TestCase("Square")]
    [TestCase("Circle")]
    [TestCase("Cube")]
    [TestCase("Sphere")]
    [TestCase("Floor")]
    public void EveryMeshLayerHasFiniteAlignedDataAndValidIndices(string type)
    {
        object light = NewLight(type);
        ChangeAppearance(light, "WithBloomWidthScale", 0f);
        object meshes = Create(light);
        foreach (string layer in new[] { "Core", "Glow", "ReflectionCore" })
        {
            Vector3[] vertices = Vertices(meshes, layer);
            Color[] colors = Colors(meshes, layer);
            Vector2[] uv = Field<Vector2[]>(Field<object>(meshes, layer), "UV");
            Assert.That(colors.Length, Is.EqualTo(vertices.Length));
            Assert.That(uv.Length, Is.EqualTo(vertices.Length));
            foreach (Vector3 vertex in vertices)
            {
                Assert.That(float.IsNaN(vertex.x) || float.IsInfinity(vertex.x), Is.False);
                Assert.That(float.IsNaN(vertex.y) || float.IsInfinity(vertex.y), Is.False);
                Assert.That(float.IsNaN(vertex.z) || float.IsInfinity(vertex.z), Is.False);
            }

            foreach (int index in Triangles(meshes, layer))
                Assert.That(index, Is.InRange(0, vertices.Length - 1));
        }
    }

    private static void ChangeAppearance(object light, string method, float value)
    {
        object appearance = light.GetType().GetProperty("Appearance", Members)!.GetValue(light)!;
        Set(light, "Appearance", Invoke(appearance, method, value));
    }

    private static bool ContainsOrigin(Vector3 a, Vector3 b, Vector3 c)
    {
        float ab = (a.x * b.z) - (a.z * b.x);
        float bc = (b.x * c.z) - (b.z * c.x);
        float ca = (c.x * a.z) - (c.z * a.x);
        return (ab > 0f && bc > 0f && ca > 0f) || (ab < 0f && bc < 0f && ca < 0f);
    }

    private static Type PluginType(string name)
        => Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings." + name, true)!;

    private static object NewLight(string name)
    {
        object config = Activator.CreateInstance(PluginType("Config"), true)!;
        Set(config, "MenuLightsJson", "[]");
        object store = Activator.CreateInstance(PluginType("MenuLightStore"), Members, null, new[] { config }, null)!;
        return Invoke(store, "CreateDefault", Enum.Parse(PluginType("MenuLightType"), name));
    }

    private static object Create(object light, Vector3 player = default, Vector3 forward = default, float floor = 0f)
    {
        if (forward == Vector3.zero)
            forward = Vector3.forward;
        object enclosure = NewEnclosure(player, floor);
        object?[] arguments = { enclosure, player, forward, light, null };
        bool created = (bool)PluginType("MenuLightGeometry").GetMethod("TryCreate", Members)!.Invoke(null, arguments)!;
        Assert.That(created, Is.True, "the requested light must generate geometry");
        return arguments[4]!;
    }

    private static object NewEnclosure(Vector3 center, float floor)
    {
        object enclosure = Activator.CreateInstance(PluginType("FerrofluidGrabEnclosure"), true)!;
        enclosure.GetType().GetField("Radius", Members)!.SetValue(enclosure, 3f);
        enclosure.GetType().GetField("FloorY", Members)!.SetValue(enclosure, floor);
        enclosure.GetType().GetField("Center", Members)!.SetValue(enclosure, center);
        return enclosure;
    }

    private static object Invoke(object instance, string method, params object[] arguments)
        => instance.GetType().GetMethod(method, Members)!.Invoke(instance, arguments)!;

    private static void Set(object instance, string property, object value)
        => instance.GetType().GetProperty(property, Members)!.SetValue(instance, value);

    private static T Field<T>(object instance, string field)
        => (T)instance.GetType().GetField(field, Members)!.GetValue(instance)!;

    private static Vector3[] Vertices(object meshes, string layer)
        => Field<Vector3[]>(Field<object>(meshes, layer), "Vertices");

    private static Color[] Colors(object meshes, string layer)
        => Field<Color[]>(Field<object>(meshes, layer), "Colors");

    private static int[] Triangles(object meshes, string layer)
        => Field<int[]>(Field<object>(meshes, layer), "Triangles");

    private static float[] StationEmission(string type, object meshes, string layer)
    {
        Vector3[] vertices = Vertices(meshes, layer);
        Vector2[] uvs = Field<Vector2[]>(Field<object>(meshes, layer), "UV");
        Color[] colors = Colors(meshes, layer);
        int[] triangles = Triangles(meshes, layer);
        float step = uvs.Where(uv => uv.x > 0.0001f).Min(uv => uv.x);
        int stations = (int)Mathf.Round(1f / step) + 1;
        float[] alphas = new float[stations];
        bool sideScale = type == "Floor";
        for (int triangle = 0; triangle < triangles.Length; triangle += 3)
        {
            Vector3 a = vertices[triangles[triangle]];
            Vector3 b = vertices[triangles[triangle + 1]];
            Vector3 c = vertices[triangles[triangle + 2]];
            Vector3 normal = Vector3.Cross(b - a, c - a);
            float scale = sideScale && Mathf.Abs(normal.y) < 0.0001f ? 1.3f : 1f;
            for (int corner = 0; corner < 3; corner++)
            {
                int index = triangles[triangle + corner];
                int station = (int)Mathf.Round(uvs[index].x * (stations - 1));
                alphas[station] = colors[index].a / scale;
            }
        }

        return alphas;
    }

    private static void AssertVector(Vector3 actual, Vector3 expected)
        => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.0001f), $"expected {expected}, got {actual}");
}
