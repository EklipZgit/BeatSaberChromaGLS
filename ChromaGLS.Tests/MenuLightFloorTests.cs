using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
public class MenuLightFloorTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [TestCase("Parabola", 0)]
    [TestCase("Square", 1)]
    [TestCase("Cube", 2)]
    [TestCase("Sphere", 3)]
    [TestCase("Circle", 4)]
    [TestCase("Floor", 5)]
    public void SerializedTypeNumbersStayCompatible(string name, int number)
        => Assert.That(Convert.ToInt32(Kind(name)), Is.EqualTo(number));

    [Test]
    public void FloorGeometryPinsTheStripButDoesNotRaiseTheGlowOrMutateInput()
    {
        object definition = Default("Floor");
        Set(definition, "Height", 7f);
        Set(definition, "RotationY", 35f);
        Set(definition, "QuaternionX", .5f);
        Set(definition, "PositionX", .25f);
        Set(definition, "PositionZ", .75f);
        Set(definition, "Depth", .2f);
        object meshes = Geometry(definition, 2f);
        Vector3 origin = Field<Vector3>(meshes, "Position");
        Quaternion rotation = Field<Quaternion>(meshes, "Rotation");
        Assert.That(origin.y, Is.EqualTo(2.02f).Within(1e-5f));
        Assert.That(origin.x, Is.EqualTo(.25f).Within(1e-5f));
        Assert.That(origin.z, Is.EqualTo(.4f).Within(1e-5f));
        Assert.That(Vector3.Distance(rotation * Vector3.up, Vector3.up), Is.LessThan(1e-5f));
        Assert.That(Vector3.Distance(rotation * Vector3.forward, Quaternion.AngleAxis(35f, Vector3.up) * Vector3.forward), Is.LessThan(1e-5f));
        Vector3[] core = Vertices(meshes, "Core");
        Assert.That(core.Min(vertex => vertex.y) + origin.y, Is.EqualTo(1.82f).Within(1e-5f));
        Vector3[] glow = Vertices(meshes, "Glow");
        Assert.That(glow, Is.Not.Empty);
        foreach (Vector3 vertex in glow)
            Assert.That((origin + (rotation * vertex)).y, Is.EqualTo(2.001f).Within(1e-5f));
        Assert.That(Get(definition, "Height"), Is.EqualTo(7f), "geometry must not mutate a saved definition");
    }

    [TestCase("Parabola")]
    [TestCase("Square")]
    [TestCase("Cube")]
    [TestCase("Sphere")]
    [TestCase("Circle")]
    public void MovableLightsHaveNoPlanarGlowButKeepReflectionGeometryAndPose(string type)
    {
        object definition = Default(type);
        Set(definition, "Height", 1.7f);
        Invoke(definition, "RotateAxis", 0, 53f);
        object meshes = Geometry(definition);
        Assert.That(Vertices(meshes, "Glow"), Is.Empty);
        Assert.That(Field<int[]>(Field<object>(meshes, "Glow"), "Triangles"), Is.Empty);
        Assert.That(Vertices(meshes, "Core"), Is.Not.Empty);
        Assert.That(Vertices(meshes, "ReflectionCore"), Is.Not.Empty);
        Assert.That(Field<Vector3>(meshes, "Position").y, Is.EqualTo(1.7f));
        Assert.That((Field<Quaternion>(meshes, "Rotation") * Vector3.up).y, Is.LessThan(.9f));
    }

    [Test]
    public void SavingFloorConstrainsTheCopyWithoutChangingTheCaller()
    {
        object config = Config("[]");
        object store = New("MenuLightStore", config);
        object definition = Default("Floor");
        Set(definition, "Height", 9f);
        Set(definition, "RotationX", 70f);
        Set(definition, "RotationZ", -25f);
        Set(definition, "RotationY", 42f);
        Invoke(store, "Upsert", definition);
        object saved = Lights(New("MenuLightStore", config))[0]!;
        Assert.That(Get(saved, "Height"), Is.EqualTo(.02f));
        Assert.That(Get(saved, "RotationX"), Is.EqualTo(0f));
        Assert.That(Get(saved, "RotationZ"), Is.EqualTo(0f));
        Assert.That(Get(saved, "RotationY"), Is.EqualTo(42f));
        Assert.That(Get(definition, "Height"), Is.EqualTo(9f));
        Assert.That(Get(saved, "Appearance"), Is.EqualTo(Get(Default("Parabola"), "Appearance")));
    }

    [TestCase(.006f, false, "Floor")]
    [TestCase(.03f, false, "Floor")]
    [TestCase(.8f, false, "Parabola")]
    [TestCase(.006f, true, "Parabola")]
    public void OnlyTheKnownHorizontalDefaultMigrates(float height, bool tilted, string expected)
    {
        object definition = Default("Parabola");
        Set(definition, "Id", "legacy-parabola");
        Set(definition, "Height", height);
        if (tilted)
        {
            Set(definition, "QuaternionX", .5f);
            Set(definition, "QuaternionW", Mathf.Sqrt(.75f));
        }
        object saved = Lights(New("MenuLightStore", Config(JsonConvert.SerializeObject(new[] { definition }))))[0]!;
        Assert.That(Get(saved, "Type").ToString(), Is.EqualTo(expected));
        Assert.That(Get(saved, "Height"), Is.EqualTo(expected == "Floor" ? .02f : height));
        if (tilted)
            Assert.That(Get(saved, "QuaternionX"), Is.EqualTo(.5f), "quaternion tilt matters even if slider values are zero");
    }

    [Test]
    public void OtherSavedParabolasAndDeliberateRemovalArePreserved()
    {
        object definition = Default("Parabola");
        Set(definition, "Id", "custom");
        object saved = Lights(New("MenuLightStore", Config(JsonConvert.SerializeObject(new[] { definition }))))[0]!;
        Assert.That(Get(saved, "Type").ToString(), Is.EqualTo("Parabola"));
        Assert.That(Lights(New("MenuLightStore", Config("[]"))), Is.Empty);
    }

    [Test]
    public void EditorTypeSwitchConstrainsImmediatelyAndReturningToParabolaRestoresFreePose()
    {
        object config = Config("[]");
        object light = New("ParabolicFloorLight", config);
        object ui = New("ParabolicFloorLightSettingsUI", config, light);
        Invoke(ui, "BeginEditor", null!, (Action)(() => { }));
        Set(ui, "HeightValue", 3f);
        Set(ui, "RotationXValue", 45f);
        Set(ui, "TypeValue", Kind("Floor"));
        Set(ui, "HeightValue", 8f);
        Set(ui, "RotationZValue", 32f);
        Assert.That(Get(ui, "HeightValue"), Is.EqualTo(.02f));
        Assert.That(Get(ui, "RotationXValue"), Is.EqualTo(0f));
        Assert.That(Get(ui, "RotationZValue"), Is.EqualTo(0f));
        Set(ui, "RotationYValue", 28f);
        Assert.That(Get(ui, "RotationYValue"), Is.EqualTo(28f));
        Invoke(ui, "HandleReset");
        Assert.That(Get(ui, "TypeValue"), Is.EqualTo(Kind("Floor")));
        Assert.That(Get(ui, "HeightValue"), Is.EqualTo(.02f));
        Set(ui, "TypeValue", Kind("Parabola"));
        Set(ui, "HeightValue", 2f);
        Set(ui, "RotationXValue", 20f);
        Assert.That(Get(ui, "HeightValue"), Is.EqualTo(2f));
        Assert.That(Get(ui, "RotationXValue"), Is.EqualTo(20f));
        Invoke(ui, "HandleCancel");
        Assert.That(Lights(Get(light, "Store")), Is.Empty);
    }

    [TestCase(.01f)]
    [TestCase(1f)]
    public void FloorCoreShadesTopAndSideFacesIndependently(float lightWidth)
    {
        object floor = Default("Floor");
        Set(floor, "Appearance", Invoke(Get(floor, "Appearance"), "WithLightWidthScale", lightWidth));
        object meshes = Geometry(floor);
        object core = Field<object>(meshes, "Core");
        Vector3[] vertices = Field<Vector3[]>(core, "Vertices");
        int[] triangles = Field<int[]>(core, "Triangles");
        Color[] colors = Field<Color[]>(core, "Colors");
        Vector2[] uv = Field<Vector2[]>(core, "UV");
        object parabola = Default("Parabola");
        Set(parabola, "Height", .02f);
        Set(parabola, "Appearance", Get(floor, "Appearance"));
        object original = Field<object>(Geometry(parabola), "Core");
        Vector3[] originalVertices = Field<Vector3[]>(original, "Vertices");
        int[] originalTriangles = Field<int[]>(original, "Triangles");
        Color[] originalColors = Field<Color[]>(original, "Colors");
        Vector2[] originalUv = Field<Vector2[]>(original, "UV");
        Assert.That(triangles.Length, Is.EqualTo(originalTriangles.Length));
        int topCount = 0;
        int sideCount = 0;
        int bottomCount = 0;
        for (int triangle = 0; triangle < triangles.Length; triangle += 3)
        {
            Vector3 first = originalVertices[originalTriangles[triangle]];
            Vector3 normal = Vector3.Cross(
                originalVertices[originalTriangles[triangle + 1]] - first,
                originalVertices[originalTriangles[triangle + 2]] - first);
            float factor;
            if (normal.y > 0f)
            {
                factor = .64f;
                topCount++;
            }
            else if (normal.y < 0f)
            {
                factor = 1f;
                bottomCount++;
            }
            else
            {
                factor = 1.44f;
                sideCount++;
            }

            for (int corner = 0; corner < 3; corner++)
            {
                int index = triangles[triangle + corner];
                int originalIndex = originalTriangles[triangle + corner];
                Color expected = originalColors[originalIndex];
                Assert.That(colors[index].r, Is.EqualTo(expected.r * factor).Within(1e-6f));
                Assert.That(colors[index].g, Is.EqualTo(expected.g * factor).Within(1e-6f));
                Assert.That(colors[index].b, Is.EqualTo(expected.b * factor).Within(1e-6f));
                Assert.That(colors[index].a, Is.EqualTo(expected.a));
                Assert.That(vertices[index], Is.EqualTo(originalVertices[originalIndex]));
                Assert.That(uv[index], Is.EqualTo(originalUv[originalIndex]));
            }
        }

        Assert.That(topCount, Is.GreaterThan(0));
        Assert.That(sideCount, Is.GreaterThan(0));
        Assert.That(bottomCount, Is.GreaterThan(0));
        foreach (Color color in Field<Color[]>(Field<object>(meshes, "ReflectionCore"), "Colors"))
        {
            Assert.That(color.r, Is.EqualTo(.64f).Within(1e-6f));
            Assert.That(color.g, Is.EqualTo(.64f).Within(1e-6f));
            Assert.That(color.b, Is.EqualTo(.64f).Within(1e-6f));
        }
    }

    [Test]
    public void CubeHeightMovesTheWholeShapeWhileDepthDoesNot()
    {
        object cube = Default("Cube");
        Set(cube, "Height", .25f);
        object lower = Geometry(cube);
        Set(cube, "Height", .75f);
        object raised = Geometry(cube);
        Assert.That(
            Field<Vector3>(raised, "Position").y - Field<Vector3>(lower, "Position").y,
            Is.EqualTo(.5f).Within(1e-6f));
        Assert.That(Vertices(raised, "Core"), Is.EqualTo(Vertices(lower, "Core")));

        Set(cube, "Depth", .02f);
        object shallow = Geometry(cube);
        Set(cube, "Depth", .52f);
        object deep = Geometry(cube);
        Assert.That(Field<Vector3>(deep, "Position"), Is.EqualTo(Field<Vector3>(shallow, "Position")));
        Assert.That(
            Vertices(deep, "Core").Min(vertex => vertex.y) - Vertices(shallow, "Core").Min(vertex => vertex.y),
            Is.EqualTo(-.5f).Within(1e-6f));
        Assert.That(
            Vertices(deep, "Core").Max(vertex => vertex.y),
            Is.EqualTo(Vertices(shallow, "Core").Max(vertex => vertex.y)));
    }

    private static object Geometry(object definition, float floor = 0f)
    {
        object enclosure = New("FerrofluidGrabEnclosure", Vector3.zero, 5f, new Vector3(0f, 0f, -5f), Vector3.forward, floor);
        object?[] arguments = { enclosure, Vector3.zero, Vector3.forward, definition, null };
        Assert.That((bool)TypeOf("MenuLightGeometry").GetMethod("TryCreate", Members)!.Invoke(null, arguments)!, Is.True);
        return arguments[4]!;
    }

    private static object Default(string type)
        => Invoke(New("MenuLightStore", Config("[]")), "CreateDefault", Kind(type));

    private static object Config(string json)
    {
        object config = New("Config");
        Set(config, "MenuLightsJson", json);
        return config;
    }

    private static Type TypeOf(string name) => Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings." + name, true)!;
    private static object Kind(string name) => Enum.Parse(TypeOf("MenuLightType"), name);
    private static object New(string type, params object[] arguments) => Activator.CreateInstance(TypeOf(type), Members, null, arguments, null)!;
    private static object Invoke(object instance, string name, params object[] arguments) => instance.GetType().GetMethod(name, Members)!.Invoke(instance, arguments)!;
    private static object Get(object instance, string name) => instance.GetType().GetProperty(name, Members)!.GetValue(instance)!;
    private static void Set(object instance, string name, object value) => instance.GetType().GetProperty(name, Members)!.SetValue(instance, value);
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Members)!.GetValue(instance)!;
    private static IList Lights(object store) => (IList)Get(store, "Lights");
    private static Vector3[] Vertices(object meshes, string layer) => Field<Vector3[]>(Field<object>(meshes, layer), "Vertices");

}
