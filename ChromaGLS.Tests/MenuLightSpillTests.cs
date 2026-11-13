using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
public class MenuLightSpillTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Vector3[] Triangle = { new(.25f, 0f, .25f), new(1.25f, 0f, .25f), new(.25f, 0f, 1.25f) };

    [Test]
    public void WhiteTriangleIntegratesAreaAndTintAlphaOnce()
    {
        Array samples = Calculate(Triangle, new[] { Color.white, Color.white, Color.white });
        Assert.That(samples.Length, Is.EqualTo(1));
        object sample = samples.GetValue(0)!;
        AssertVector(Field<Vector3>(sample, "Energy"), new Vector3(.25f, .25f, .25f));
        AssertVector(Field<Vector3>(sample, "Position"), new Vector3(7f / 12f, 0f, 7f / 12f));
        AssertVector(Field<Vector3>(sample, "Normal"), Vector3.up);
        Assert.That(Field<float>(sample, "RadiusSquared"), Is.EqualTo(.5f / Mathf.PI).Within(1e-6f));
    }

    [Test]
    public void VaryingColorAndAlphaIntegrateTheirProduct()
    {
        Color[] colors = { new(1f, 0f, 0f, 1f), new(0f, 1f, 0f, 0f), new(0f, 0f, 1f, 0f) };
        Array samples = Calculate(Triangle, colors);
        Assert.That(samples.Length, Is.EqualTo(1));
        AssertVector(Field<Vector3>(samples.GetValue(0)!, "Energy"), new Vector3(1f / 24f, 1f / 48f, 1f / 48f));
    }

    [Test]
    public void DegenerateTriangleProducesNoSource()
    {
        Vector3[] vertices = { Vector3.zero, Vector3.right, Vector3.right * 2f };
        Assert.That(Calculate(vertices, new[] { Color.white, Color.white, Color.white }), Is.Empty);
    }

    [Test]
    public void RotationAndPositionMoveTheEmitterPlaneAndCentroid()
    {
        Quaternion rotation = Quaternion.AngleAxis(90f, Vector3.forward);
        Vector3 position = new(2f, 3f, 4f);
        Array samples = Calculate(Triangle, new[] { Color.white, Color.white, Color.white }, position, rotation);
        Assert.That(samples.Length, Is.EqualTo(1));
        object sample = samples.GetValue(0)!;
        AssertVector(Field<Vector3>(sample, "Position"), position + new Vector3(0f, 7f / 12f, 7f / 12f));
        AssertVector(Field<Vector3>(sample, "Normal"), Vector3.left);
    }

    private static Array Calculate(Vector3[] vertices, Color[] colors, Vector3? position = null, Quaternion? rotation = null)
    {
        object core = New("ParabolicFloorLightMeshData", vertices, new[] { 0, 1, 2 }, colors, new Vector2[3]);
        object glow = New("ParabolicFloorLightGlowMeshData", Array.Empty<Vector3>(), Array.Empty<int>(), Array.Empty<Color>(), Array.Empty<Vector2>());
        object bounds = New("MenuLightReflectionBounds");
        bounds.GetType().GetField("InverseWidth", Members)!.SetValue(bounds, .5f);
        bounds.GetType().GetField("InverseHeight", Members)!.SetValue(bounds, .5f);
        object meshes = New("MenuLightMeshes", core, glow, core, position ?? Vector3.zero, rotation ?? Quaternion.identity, bounds);
        object appearance = TypeOf("ParabolicFloorLightSettings").GetField("Default", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        appearance = Update(appearance, "WithBrightness", .5f);
        appearance = Update(appearance, "WithRed", 1f);
        appearance = Update(appearance, "WithGreen", 1f);
        appearance = Update(appearance, "WithBlue", 1f);
        return (Array)TypeOf("MenuLightSpill").GetMethod("Calculate", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new[] { meshes, appearance, (object)false })!;
    }

    private static object Update(object value, string name, float setting)
        => value.GetType().GetMethod(name, Members)!.Invoke(value, new object[] { setting })!;

    private static Type TypeOf(string name) => Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings." + name, true)!;

    private static object New(string name, params object[] args) => Activator.CreateInstance(TypeOf(name), Members, null, args, null)!;

    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Members)!.GetValue(value)!;

    private static void AssertVector(Vector3 actual, Vector3 expected)
    {
        Assert.That(actual.x, Is.EqualTo(expected.x).Within(1e-6f));
        Assert.That(actual.y, Is.EqualTo(expected.y).Within(1e-6f));
        Assert.That(actual.z, Is.EqualTo(expected.z).Within(1e-6f));
    }
}
