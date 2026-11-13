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
    public void TrackPositionFiltersShakeAndIsFrameRateIndependent()
    {
        MethodInfo track = GetInteractionMethod("TrackPosition");
        Assert.That(track, Is.Not.Null, "TrackPosition must exist");
        Vector3 slow = Vector3.zero;
        Vector3 fast = Vector3.zero;
        for (int frame = 0; frame < 120; frame++)
        {
            slow = (Vector3)track.Invoke(null, [slow, Vector3.one, 1f, 1f / 60f])!;
            Assert.That(slow.x, Is.InRange(0f, 1f));
        }

        for (int frame = 0; frame < 240; frame++)
        {
            fast = (Vector3)track.Invoke(null, [fast, Vector3.one, 1f, 1f / 120f])!;
        }

        Assert.That(Vector3.Distance(fast, slow), Is.LessThan(0.001f), "60Hz and 120Hz simulations of the same elapsed time must agree");
        Assert.That(slow.x, Is.InRange(0.9f, 1f));
        float maximumShake = 0f;
        for (int frame = 0; frame < 240; frame++)
        {
            Vector3 target = Vector3.one + (Vector3.right * (0.01f * Mathf.Sin(frame * Mathf.PI / 3f)));
            fast = (Vector3)track.Invoke(null, [fast, target, 1f, 1f / 120f])!;
            if (frame > 120)
            {
                maximumShake = Mathf.Max(maximumShake, Mathf.Abs(fast.x - 1f));
            }
        }

        Assert.That(maximumShake, Is.LessThan(0.001f), "20Hz hand shake should leave the magnet near its center average.");
    }

    [Test]
    public void TrackPositionIsStatelessAndNeverCarriesVelocity()
    {
        MethodInfo track = GetInteractionMethod("TrackPosition");
        Assert.That(track, Is.Not.Null, "TrackPosition must exist");
        // Two identical calls from the same state must agree exactly: no hidden velocity state.
        Vector3 current = new(0.2f, -0.1f, 0.05f);
        Vector3 target = new(0.4f, 0.3f, -0.2f);
        Vector3 first = (Vector3)track.Invoke(null, [current, target, 0.55f, 1f / 90f])!;
        Vector3 second = (Vector3)track.Invoke(null, [current, target, 0.55f, 1f / 90f])!;
        Assert.That(second, Is.EqualTo(first));
        // Approaching the same target from opposite sides must converge toward it, not carry momentum through.
        Vector3 fromLeft = (Vector3)track.Invoke(null, [target + new Vector3(-0.5f, 0f, 0f), target, 0.55f, 1f / 90f])!;
        Vector3 fromRight = (Vector3)track.Invoke(null, [target + new Vector3(0.5f, 0f, 0f), target, 0.55f, 1f / 90f])!;
        Assert.That(fromLeft.x, Is.LessThan(target.x).And.GreaterThan(target.x - 0.5f));
        Assert.That(fromRight.x, Is.GreaterThan(target.x).And.LessThan(target.x + 0.5f));
    }

    [Test]
    public void TrackPositionHitsExactInstantaneousRates()
    {
        MethodInfo track = GetInteractionMethod("TrackPosition");
        Assert.That(track, Is.Not.Null, "TrackPosition must exist");
        float width = 0.55f;
        // Instantaneous gap-closing rates: 10/s at 0.02 widths, 50/s at 0.5, 200/s at 2.
        foreach ((float distance, float expectedRate) in new[] { (0.02f * width, 10f), (0.5f * width, 50f), (2f * width, 200f) })
        {
            Vector3 current = Vector3.zero;
            Vector3 target = new(distance, 0f, 0f);
            float dt = 0.00001f;
            Vector3 result = (Vector3)track.Invoke(null, [current, target, width, dt])!;
            float rate = (result.x - current.x) / (distance * dt);
            Assert.That(rate, Is.EqualTo(expectedRate).Within(expectedRate * 0.01f),
                $"rate at {distance / width:F2} widths must be {expectedRate:F0}/s, got {rate:F2}");
        }
    }

    [Test]
    public void TrackPositionNeverOvershoots()
    {
        MethodInfo track = GetInteractionMethod("TrackPosition");
        Assert.That(track, Is.Not.Null, "TrackPosition must exist");
        float width = 0.55f;
        foreach (float dt in new[] { 0.001f, 0.005f, 0.02f, 0.1f, 1f })
        {
            Vector3 position = new(0.3f, -0.2f, 0.1f);
            Vector3 target = new(-0.4f, 0.5f, -0.3f);
            for (int step = 0; step < 200; step++)
            {
                if (step == 40)
                    target = new(0.5f, -0.4f, 0.2f);
                Vector3 before = position;
                float beforeDistance = Vector3.Distance(before, target);
                position = (Vector3)track.Invoke(null, [position, target, width, dt])!;
                for (int axis = 0; axis < 3; axis++)
                {
                    float lo = Mathf.Min(before[axis], target[axis]) - 0.000001f;
                    float hi = Mathf.Max(before[axis], target[axis]) + 0.000001f;
                    Assert.That(position[axis], Is.InRange(lo, hi),
                        $"dt={dt} step={step} axis={axis}: position {position[axis]:F4} outside [{lo:F4},{hi:F4}]");
                }
                if (step != 40)
                    Assert.That(Vector3.Distance(position, target), Is.LessThanOrEqualTo(beforeDistance + 0.0001f),
                        $"dt={dt} step={step}: distance grew");
            }
        }
    }

    [Test]
    public void TrackPositionScalesWithSurfaceWidth()
    {
        MethodInfo track = GetInteractionMethod("TrackPosition");
        Assert.That(track, Is.Not.Null, "TrackPosition must exist");
        // A 0.5-width gap moves the same fraction of the gap at every surface size.
        float reference = ((Vector3)track.Invoke(null, [Vector3.zero, new Vector3(0.5f, 0f, 0f), 1f, 1f / 90f])!).x;
        foreach (float width in new[] { 0.3f, 0.55f, 1.2f })
        {
            Vector3 result = (Vector3)track.Invoke(null, [Vector3.zero, new Vector3(0.5f * width, 0f, 0f), width, 1f / 90f])!;
            Assert.That(result.x / width, Is.EqualTo(reference).Within(0.00001f),
                $"width {width:F2}: normalized step must match the width=1 reference {reference:F6}");
        }
    }

    [Test]
    public void TrackPositionAttenuatesJitter()
    {
        MethodInfo track = GetInteractionMethod("TrackPosition");
        Assert.That(track, Is.Not.Null, "TrackPosition must exist");
        float width = 0.55f;
        // 240 frames at 90Hz of +/-0.01-width alternating jitter must shrink below quarter RMS.
        Vector3 position = new(0f, 0f, 0f);
        float inputSumSq = 0f;
        float outputSumSq = 0f;
        for (int frame = 0; frame < 240; frame++)
        {
            float jitter = (frame % 2 == 0 ? 1f : -1f) * 0.01f * width;
            Vector3 target = new(jitter, 0f, 0f);
            position = (Vector3)track.Invoke(null, [position, target, width, 1f / 90f])!;
            inputSumSq += jitter * jitter;
            outputSumSq += position.x * position.x;
        }
        float inputRms = Mathf.Sqrt(inputSumSq / 240f);
        float outputRms = Mathf.Sqrt(outputSumSq / 240f);
        Assert.That(outputRms, Is.LessThan(0.25f * inputRms),
            $"jitter output RMS {outputRms:F5} must attenuate below 25% of input {inputRms:F5}");
    }

    [Test]
    public void TrackPositionCoversTwoWidthJumpInOneFrame()
    {
        MethodInfo track = GetInteractionMethod("TrackPosition");
        Assert.That(track, Is.Not.Null, "TrackPosition must exist");
        // A 2-width target jump at 90Hz must move more than 1.5 widths in one frame.
        float width = 0.55f;
        Vector3 result = (Vector3)track.Invoke(null, [Vector3.zero, new Vector3(2f * width, 0f, 0f), width, 1f / 90f])!;
        Assert.That(result.x, Is.GreaterThan(1.5f * width),
            $"a 2-width jump must move >1.5 widths in one 90Hz frame, got {result.x:F4}");
    }

    [Test]
    public void TrackPositionZeroDeltaTimeReturnsCurrent()
    {
        MethodInfo track = GetInteractionMethod("TrackPosition");
        Assert.That(track, Is.Not.Null, "TrackPosition must exist");
        Vector3 current = new(0.1f, -0.2f, 0.3f);
        Vector3 result = (Vector3)track.Invoke(null, [current, new Vector3(0.9f, 0.8f, 0.7f), 0.55f, 0f])!;
        Assert.That(result, Is.EqualTo(current));
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

    private static Vector3 RayTarget(MethodInfo target, object shape, Vector3 origin, Vector3 direction, float roundness, out bool hit)
    {
        object[] args = [shape, origin, direction, roundness, false];
        Vector3 result = (Vector3)target.Invoke(null, args)!;
        hit = (bool)args[4];
        return result;
    }
}
