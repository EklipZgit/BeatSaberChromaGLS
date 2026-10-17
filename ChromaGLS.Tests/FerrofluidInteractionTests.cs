using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class FerrofluidInteractionTests
{
    [Test]
    public void FoldNeighborsRetainTheirDistanceAcrossTheSurfaceEdge()
    {
        Type simulation = Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", true)!;
        MethodInfo delta = simulation.GetMethod("EvaluatePrimitiveFoldDelta", BindingFlags.Static | BindingFlags.NonPublic)!;
        Vector2 from = new(0.99f, 0.3f);
        Vector2 to = new(0.99f, -0.3f);
        Vector2 result = (Vector2)delta.Invoke(null, [from, to])!;
        Assert.That(result.x, Is.EqualTo(0.02f).Within(0.000001f), "The neighbor must be reflected through x=1, preserving the distance to both sides of the edge.");
        Assert.That(result.y, Is.EqualTo(0f).Within(0.000001f));
    }

    [Test]
    public void FoldSpringDeltaStaysContinuousAcrossTheKernelBand()
    {
        Type simulation = Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings.FerrofluidGpuSimulation", true)!;
        MethodInfo delta = simulation.GetMethod("EvaluatePrimitiveFoldDelta", BindingFlags.Static | BindingFlags.NonPublic)!;
        const float gate = 0.832f;
        Vector2 target = new(0.99f, -0.3f);
        Vector2 before = (Vector2)delta.Invoke(null, [new Vector2(gate - 0.000001f, 0.3f), target])!;
        Vector2 after = (Vector2)delta.Invoke(null, [new Vector2(gate + 0.000001f, 0.3f), target])!;
        Assert.That(Vector2.Distance(before, after), Is.LessThan(0.0001f), "Spring pull must not switch from a long direct delta to a short folded delta when a site crosses the pressure kernel band.");
    }

    [Test]
    public void MagnetSpringFiltersShakeWithoutOvershootAndIsFrameRateIndependent()
    {
        MethodInfo smooth = GetInteractionMethod("SmoothPosition");
        Vector3 slow = Vector3.zero;
        Vector3 slowVelocity = Vector3.zero;
        Vector3 fast = Vector3.zero;
        Vector3 fastVelocity = Vector3.zero;
        for (int frame = 0; frame < 12; frame++)
        {
            slow = Smooth(smooth, slow, Vector3.one, ref slowVelocity, 1f / 60f);
            Assert.That(slow.x, Is.InRange(0f, 1f));
        }

        for (int frame = 0; frame < 24; frame++)
        {
            fast = Smooth(smooth, fast, Vector3.one, ref fastVelocity, 1f / 120f);
        }

        Assert.That(Vector3.Distance(fast, slow), Is.LessThan(0.000001f));
        Assert.That(slow.x, Is.InRange(0.9f, 1f));
        float maximumShake = 0f;
        for (int frame = 0; frame < 240; frame++)
        {
            Vector3 target = Vector3.one + (Vector3.right * (0.01f * Mathf.Sin(frame * Mathf.PI / 3f)));
            fast = Smooth(smooth, fast, target, ref fastVelocity, 1f / 120f);
            if (frame > 120)
            {
                maximumShake = Mathf.Max(maximumShake, Mathf.Abs(fast.x - 1f));
            }
        }

        Assert.That(maximumShake, Is.LessThan(0.001f), "20Hz hand shake should leave the magnet near its center average.");
    }

    [Test]
    public void MagnetSpringUsesSixtyMillisecondSmoothing()
    {
        MethodInfo smooth = GetInteractionMethod("SmoothPosition");
        Vector3 velocity = Vector3.zero;
        Vector3 actual = Smooth(smooth, Vector3.zero, Vector3.one, ref velocity, 0.06f);
        Assert.That(Vector3.Distance(actual, Vector3.one * 0.59399415f), Is.LessThan(0.000001f));
    }

    [Test]
    public void RoundedCubeHitsMatchTheRenderedSurfaceAndNearMissesStayContinuous()
    {
        Assembly plugin = Assembly.Load("ChromaGLS");
        Type shapeType = plugin.GetType("ChromaGLS.Settings.FerrofluidSurfaceShape", true)!;
        object cube = Enum.Parse(shapeType, "RoundedCube");
        MethodInfo target = GetInteractionMethod("EvaluateRayTarget");
        foreach (float roundness in new[] { 0f, 0.2f, 0.5f, 1f })
        {
            float exponent = Mathf.Lerp(8f, 2f, roundness);
            Vector3 point = RayTarget(target, cube, new Vector3(0.35f, 0.35f, -2f), Vector3.forward, roundness, out bool hit);
            Assert.That(hit, Is.True);
            float equation = Mathf.Pow(Mathf.Abs(point.x * 2f), exponent)
                + Mathf.Pow(Mathf.Abs(point.y * 2f), exponent)
                + Mathf.Pow(Mathf.Abs(point.z * 2f), exponent);
            Assert.That(equation, Is.EqualTo(1f).Within(0.00001f));
            Assert.That(point.z, Is.LessThan(0f), "The laser ends at the front surface.");
            float edge = 0.5f * Mathf.Pow(0.5f, 1f / exponent);
            Vector3 inside = RayTarget(target, cube, new Vector3(edge - 0.0000001f, edge - 0.0000001f, -2f), Vector3.forward, roundness, out bool insideHit);
            Vector3 outside = RayTarget(target, cube, new Vector3(edge + 0.0000001f, edge + 0.0000001f, -2f), Vector3.forward, roundness, out bool outsideHit);
            Assert.That(insideHit, Is.True);
            Assert.That(outsideHit, Is.False);
            float expectedFront = -0.5f * Mathf.Pow(1f - (2f * Mathf.Pow((edge - 0.0000001f) * 2f, exponent)), 1f / exponent);
            Assert.That(inside.z, Is.EqualTo(expectedFront).Within(0.00001f));
            Assert.That(outside.z, Is.EqualTo(0f).Within(0.000001f));
        }
    }

    private static MethodInfo GetInteractionMethod(string name)
    {
        return Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings.FerrofluidInteraction", true)!
            .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
    }

    private static Vector3 Smooth(MethodInfo smooth, Vector3 position, Vector3 target, ref Vector3 velocity, float time)
    {
        object[] args = [position, target, velocity, time];
        Vector3 result = (Vector3)smooth.Invoke(null, args)!;
        velocity = (Vector3)args[2];
        return result;
    }

    private static Vector3 RayTarget(MethodInfo target, object shape, Vector3 origin, Vector3 direction, float roundness, out bool hit)
    {
        object[] args = [shape, origin, direction, roundness, false];
        Vector3 result = (Vector3)target.Invoke(null, args)!;
        hit = (bool)args[4];
        return result;
    }
}
