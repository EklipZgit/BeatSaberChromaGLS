using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class FerrofluidFloorFrictionTests
{
    [Test]
    public void FloorSpinAngularDecelerationBrakesSignedSpinTowardZero()
    {
        MethodInfo accel = GetPhysicsMethod("EvaluateFloorSpinAngularAcceleration");
        Assert.That(accel, Is.Not.Null, "Corner spin needs a floor-only torsional deceleration helper.");
        float decel = GetPhysicsFloat("FloorSpinAngularDeceleration");
        Assert.That(decel, Is.EqualTo(20f * 2f * Mathf.PI / 90f).Within(0.001f), "20 RPS must bleed to rest in 90 seconds.");

        float positive = (float)accel!.Invoke(null, [50f, 0.02f])!;
        Assert.That(positive, Is.EqualTo(-decel).Within(0.0001f), "Positive spin decelerates at the 90-second rate.");
        float negative = (float)accel.Invoke(null, [-50f, 0.02f])!;
        Assert.That(negative, Is.EqualTo(decel).Within(0.0001f), "Negative spin mirrors the brake.");
        Assert.That((float)accel.Invoke(null, [0f, 0.02f])!, Is.EqualTo(0f), "No spin means no torque.");
    }

    [Test]
    public void FloorSpinAngularDecelerationNeverOvershootsRest()
    {
        MethodInfo accel = GetPhysicsMethod("EvaluateFloorSpinAngularAcceleration");
        Assert.That(accel, Is.Not.Null);
        float decel = GetPhysicsFloat("FloorSpinAngularDeceleration");

        float tiny = (float)accel!.Invoke(null, [0.005f, 0.02f])!;
        Assert.That(tiny, Is.EqualTo(-0.25f).Within(0.0001f), "The last step stops exactly at rest instead of reversing sign.");
        Assert.That((float)accel.Invoke(null, [-0.005f, 0.02f])!, Is.EqualTo(0.25f).Within(0.0001f));

        float slowA = (float)accel.Invoke(null, [1f, 0.01f])!;
        float slowB = (float)accel.Invoke(null, [1f, 0.05f])!;
        Assert.That(slowA, Is.EqualTo(-decel).Within(0.0001f));
        Assert.That(slowB, Is.EqualTo(-decel).Within(0.0001f), "The rate is a deceleration, not a per-step loss.");
    }

    [Test]
    public void FloorSpinDecelerationIntegratesToRestInNinetySeconds()
    {
        MethodInfo accel = GetPhysicsMethod("EvaluateFloorSpinAngularAcceleration");
        Assert.That(accel, Is.Not.Null);

        float omega = 20f * 2f * Mathf.PI;
        float elapsed = 0f;
        const float dt = 0.02f;
        while (elapsed < 120f && Mathf.Abs(omega) > 0.0001f)
        {
            omega += (float)accel!.Invoke(null, [omega, dt])! * dt;
            elapsed += dt;
        }

        Assert.That(omega, Is.EqualTo(0f).Within(0.0001f), "The helper must reach exact rest, not asymptote.");
        Assert.That(elapsed, Is.EqualTo(90f).Within(0.1f), $"20 RPS must reach rest in ~90s, took {elapsed:F1}s.");
    }

    [Test]
    public void MaximumAngularSpeedIsTwentyRevolutions()
    {
        float cap = GetPhysicsFloat("MaximumAngularSpeed");
        Assert.That(cap, Is.EqualTo(20f * 2f * Mathf.PI).Within(0.001f), "The shared cap must be 20 RPS, not the earlier 30.");
    }

    [Test]
    public void CornerFrictionGateKeepsTiltedSharpBoxCornerLowGrip()
    {
        MethodInfo gate = GetPhysicsMethod("ShouldUseCubeCornerFriction");
        Vector3 diagonal = Vector3.one.normalized * (20f * 2f * Mathf.PI);
        Vector3 tiltAxis = new Vector3(-1f, 1f, 0f).normalized;
        foreach (float angle in new[] { 20f, 30f })
        {
            Vector3 localUp = Quaternion.AngleAxis(angle, tiltAxis) * Vector3.one.normalized;
            Vector3 cornerClearances = 2f * 0.220784f * new Vector3(
                Mathf.Abs(localUp.x),
                Mathf.Abs(localUp.y),
                Mathf.Abs(localUp.z));
            Assert.That(
                Gate(gate, false, cornerClearances: cornerClearances, localAngular: diagonal),
                Is.True,
                $"a {angle}-degree tilted sharp-box top still has a single supporting corner (adjacent vertex clearances={cornerClearances})");
        }
    }

    [Test]
    public void CornerFrictionGateAcceptsBodyDiagonalSupportAndSpin()
    {
        MethodInfo gate = GetPhysicsMethod("ShouldUseCubeCornerFriction");
        Vector3 support = new(0.4f, 0.4f, 0.4f);
        Vector3 diagonal = Vector3.one.normalized * 125.66f;
        Assert.That(Gate(gate, false, cornerClearances: support, localAngular: diagonal), Is.True, "a sharp-box corner support plus body-diagonal spin is a corner top");
        Assert.That(Gate(gate, true, cornerClearances: support, localAngular: diagonal), Is.True);
    }

    [Test]
    public void CornerFrictionGateCornerClearanceHysteresis()
    {
        MethodInfo gate = GetPhysicsMethod("ShouldUseCubeCornerFriction");
        Vector3 diagonal = Vector3.one.normalized * 10f;
        Assert.That(Gate(gate, false, cornerClearances: new Vector3(0.003f, 0.3f, 0.3f), localAngular: diagonal), Is.False, "a clearance under 4mm must not enter low grip");
        Assert.That(Gate(gate, true, cornerClearances: new Vector3(0.003f, 0.3f, 0.3f), localAngular: diagonal), Is.True, "the same clearance stays low-grip once active");
        Assert.That(Gate(gate, true, cornerClearances: new Vector3(0.001f, 0.3f, 0.3f), localAngular: diagonal), Is.False, "a sub-2mm edge leaves low grip even while active");
    }

    [Test]
    public void CornerFrictionGateRejectsEdgesFacesAndNonDiagonalStates()
    {
        MethodInfo gate = GetPhysicsMethod("ShouldUseCubeCornerFriction");
        Vector3 diagonal = Vector3.one.normalized * 10f;
        Vector3 support = new(0.4f, 0.4f, 0.4f);
        foreach (bool active in new[] { false, true })
        {
            Assert.That(Gate(gate, active, cornerClearances: new Vector3(0.4f, 0.4f, 0f), localAngular: diagonal), Is.False, $"an edge support keeps normal grip (active={active})");
            Assert.That(Gate(gate, active, cornerClearances: new Vector3(0.5f, 0f, 0f), localAngular: diagonal), Is.False, $"a face support keeps normal grip (active={active})");
            Assert.That(Gate(gate, active, cornerClearances: support, localAngular: new Vector3(10f, 0f, 0f)), Is.False, $"spin about a body axis is a tumble, not a top (active={active})");
            Assert.That(Gate(gate, active, cornerClearances: support, localAngular: diagonal, gap: 0.02f), Is.False, $"an airborne cube keeps normal grip (active={active})");
            Assert.That(Gate(gate, active, cornerClearances: support, localAngular: new Vector3(0.02f, 0.02f, 0.02f)), Is.False, $"a nearly stopped top keeps normal grip (active={active})");
        }
    }

    [Test]
    public void CornerFrictionGateTracksSupportGeometryNotWorldUprightness()
    {
        MethodInfo gate = GetPhysicsMethod("ShouldUseCubeCornerFriction");
        Quaternion cornerUp = Quaternion.FromToRotation(Vector3.one.normalized, Vector3.up);
        float worldHalf = 0.220784f;
        float omega = 20f * 2f * Mathf.PI;
        foreach (float angle in new[] { 10f, 20f, 30f })
        {
            Quaternion tilt = Quaternion.AngleAxis(angle, Vector3.right);
            Quaternion rotation = tilt * cornerUp;
            Vector3 localUp = Quaternion.Inverse(rotation) * Vector3.up;
            Vector3 clearances = 2f * worldHalf * new Vector3(
                Mathf.Abs(localUp.x),
                Mathf.Abs(localUp.y),
                Mathf.Abs(localUp.z));
            Vector3 localAngular = Quaternion.Inverse(rotation) * (tilt * Vector3.up * omega);
            Assert.That(
                Gate(gate, false, cornerClearances: clearances, localAngular: localAngular),
                Is.True,
                $"a {angle}-degree tilted sharp-box top must stay low-grip while one corner still supports it (clearances={clearances}, localW={localAngular.normalized})");
        }
    }

    [Test]
    public void SupportPointCentroidReflectsActualHullFeature()
    {
        MethodInfo extent = GetPhysicsMethod("EvaluateSupportExtentWithPoint");
        object shape = CreateRoundedShape(extent);
        Quaternion diagonal = Quaternion.FromToRotation(Vector3.one.normalized, Vector3.up);
        Vector3 support = ExtentSupport(extent, shape, diagonal);
        Assert.That(Mathf.Min(Mathf.Abs(support.x), Mathf.Min(Mathf.Abs(support.y), Mathf.Abs(support.z))), Is.GreaterThan(0.05f), "a corner support keeps all three axes nonzero");

        Quaternion edge = Quaternion.FromToRotation(new Vector3(1f, 1f, 0f).normalized, Vector3.up);
        Vector3 edgeSupport = ExtentSupport(extent, shape, edge);
        Assert.That(Mathf.Abs(edgeSupport.z), Is.LessThan(0.02f), $"an edge support centroid loses the third axis ({edgeSupport})");

        Quaternion face = Quaternion.FromToRotation(Vector3.right, Vector3.down);
        Vector3 faceSupport = ExtentSupport(extent, shape, face);
        Assert.That(Mathf.Abs(faceSupport.y), Is.LessThan(0.02f), $"a face support centroid has no lateral axes ({faceSupport})");
        Assert.That(Mathf.Abs(faceSupport.z), Is.LessThan(0.02f));
    }

    private static object CreateRoundedShape(MethodInfo extent)
    {
        // The native Mesh object cannot exist in managed tests; the vertex data still flows
        // through production's own surface projection and convex shape factory.
        Type environment = GetPluginType("ChromaGLS.Settings.FerrofluidEnvironmentObjects");
        MethodInfo surface = environment.GetMethod(
            "EvaluateSurfacePositionFromDirection",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        Type shapeEnum = GetPluginType("ChromaGLS.Settings.FerrofluidSurfaceShape");
        object rounded = Enum.ToObject(shapeEnum, 1);
        float[] grid = { -0.5f, -0.25f, 0f, 0.25f, 0.5f };
        List<Vector3> vertices = new();
        for (int axis = 0; axis < 3; axis++)
        {
            foreach (float sign in new[] { -1f, 1f })
            {
                for (int i = 0; i < grid.Length; i++)
                {
                    for (int j = 0; j < grid.Length; j++)
                    {
                        Vector3 direction = default;
                        direction[axis] = sign * 0.5f;
                        direction[(axis + 1) % 3] = grid[i];
                        direction[(axis + 2) % 3] = grid[j];
                        vertices.Add((Vector3)surface.Invoke(null, [rounded, direction, 0.48f])!);
                    }
                }
            }
        }

        Type shapeType = GetPluginType("ChromaGLS.Settings.FerrofluidGrabShape");
        return shapeType.GetMethod("ForConvexBox", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [Vector3.one * 0.275f, vertices.ToArray()])!;
    }

    private static Vector3 ExtentSupport(MethodInfo extent, object shape, Quaternion rotation)
    {
        object[] args = [shape, rotation, Vector3.down, null!];
        extent.Invoke(null, args);
        return (Vector3)args[3]!;
    }

    private static bool Gate(MethodInfo gate, bool active, Vector3? cornerClearances = null, Vector3? localAngular = null, float gap = 0f)
    {
        Vector3 point = cornerClearances ?? new Vector3(0.4f, 0.4f, 0.4f);
        Vector3 omega = localAngular ?? (Vector3.one.normalized * 10f);
        return (bool)gate.Invoke(null, [active, gap, point, omega])!;
    }

    [Test]
    public void SphereFloorSpinKeepsOldRateAtAndAboveOneRps()
    {
        MethodInfo accel = GetPhysicsMethod("EvaluateSphereFloorSpinAngularAcceleration");
        Assert.That(accel, Is.Not.Null, "Sphere twist needs a dedicated angular-acceleration helper.");
        foreach (float rps in new[] { 1f, 2f, 20f })
        {
            float omega = rps * 2f * Mathf.PI;
            Assert.That(
                (float)accel!.Invoke(null, [omega, 0.02f])!,
                Is.EqualTo(-0.1875f * omega).Within(0.0001f),
                $"{rps} RPS keeps the measured 0.1875 twist rate");
        }
    }

    [Test]
    public void SphereFloorSpinAddsConstantBrakeBelowOneRps()
    {
        MethodInfo accel = GetPhysicsMethod("EvaluateSphereFloorSpinAngularAcceleration");
        float omega = Mathf.PI;
        float expected = -(0.1875f * omega) - (0.35f * (1f - (omega / (2f * Mathf.PI))));
        Assert.That(
            (float)accel!.Invoke(null, [omega, 0.02f])!,
            Is.EqualTo(expected).Within(0.0001f),
            "sub-RPS twist gains a constant finish brake without changing the high-speed rate");
        float justBelow = (float)accel.Invoke(null, [2f * Mathf.PI * 0.999f, 0.02f])!;
        float atThreshold = (float)accel.Invoke(null, [2f * Mathf.PI, 0.02f])!;
        Assert.That(justBelow, Is.EqualTo(atThreshold).Within(0.01f), "the brake is continuous across the threshold");
    }

    [Test]
    public void SphereFloorSpinIntegratesSignedSpinToExactRest()
    {
        MethodInfo accel = GetPhysicsMethod("EvaluateSphereFloorSpinAngularAcceleration");
        foreach (float sign in new[] { 1f, -1f })
        {
            foreach (float dt in new[] { 0.01f, 0.02f, 0.05f })
            {
                float omega = sign * 2f * Mathf.PI;
                float elapsed = 0f;
                while (elapsed < 12f && omega != 0f)
                {
                    float next = omega + ((float)accel!.Invoke(null, [omega, dt])! * dt);
                    Assert.That(
                        next == 0f || Mathf.Sign(next) == Mathf.Sign(omega) || Mathf.Abs(next) < 0.0005f,
                        $"the brake must not meaningfully reverse spin (sign={sign}, dt={dt}, omega={omega:F6} next={next:F6})");
                    omega = next;
                    elapsed += dt;
                }

                Assert.That(omega, Is.EqualTo(0f), $"1 RPS must finish at exact rest (sign={sign}, dt={dt})");
                Assert.That(elapsed, Is.LessThanOrEqualTo(12f), $"1 RPS took {elapsed:F1}s (sign={sign}, dt={dt})");
            }
        }
    }

    [Test]
    public void SphereFloorSpinMirrorsAndNeverReverses()
    {
        MethodInfo accel = GetPhysicsMethod("EvaluateSphereFloorSpinAngularAcceleration");
        float positive = (float)accel!.Invoke(null, [1.7f, 0.02f])!;
        float negative = (float)accel.Invoke(null, [-1.7f, 0.02f])!;
        Assert.That(negative, Is.EqualTo(-positive).Within(0.00001f), "the finish brake mirrors for negative spin");
        float tiny = (float)accel.Invoke(null, [0.001f, 0.02f])!;
        Assert.That(tiny * 0.02f, Is.EqualTo(-0.001f).Within(0.000001f), "the last step clamps to exact rest");
    }

    private static float GetPhysicsFloat(string name)
    {
        FieldInfo? field = GetPluginType("ChromaGLS.Settings.FerrofluidGrabPhysics")
            .GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.That(field, Is.Not.Null, $"Missing physics constant: {name}");
        return (float)field!.GetRawConstantValue()!;
    }

    private static MethodInfo GetPhysicsMethod(string name)
    {
        return GetPluginType("ChromaGLS.Settings.FerrofluidGrabPhysics")
            .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new AssertionException($"Missing floor friction method: {name}");
    }

    private static Type GetPluginType(string typeName)
    {
        return Assembly.Load("ChromaGLS").GetType(typeName, throwOnError: false)
            ?? throw new AssertionException($"Missing plugin type: {typeName}");
    }

    [Test]
    public void ContactConstantsMatchRequestedPhysics()
    {
        Assert.That(GetPhysicsFloat("GravityMagnitude"), Is.EqualTo(11.27f).Within(0.0001f), "gravity must be 15 percent stronger");
        Assert.That(GetPhysicsFloat("EnclosureRestitution"), Is.EqualTo(0.468f).Within(0.0001f), "walls must bounce like the floor");
        Assert.That(GetPhysicsFloat("FloorRestitution"), Is.EqualTo(0.468f).Within(0.0001f), "restitution increased 30 percent from 0.36");
        Assert.That(GetPhysicsFloat("DefaultCubeFaceLandingStrength"), Is.EqualTo(0.55f).Within(0.0001f));
        Assert.That(GetPhysicsFloat("FloorFriction"), Is.EqualTo(0.15f).Within(0.0001f));
        Assert.That(GetPhysicsFloat("DefaultCubeFloorFriction"), Is.EqualTo(0.25f).Within(0.0001f));
        Assert.That(GetPhysicsFloat("MaximumCubeFloorFriction"), Is.EqualTo(0.5f).Within(0.0001f));
    }

    private static float MaterialFriction(float desired)
        => (float)GetPhysicsMethod("EvaluateCubeFloorMaterialFriction").Invoke(null, [desired])!;

    [Test]
    public void FloorMaterialCoefficientConvertsEffectiveGrip()
    {
        Assert.That(MaterialFriction(0f), Is.EqualTo(0f).Within(0.0001f));
        Assert.That(MaterialFriction(0.125f), Is.EqualTo(0.8333f).Within(0.001f), "legacy minimum reproduces 0.125 against the floor");
        Assert.That(MaterialFriction(0.25f), Is.EqualTo(1.6667f).Within(0.001f), "default doubles the previous grip");
        Assert.That(MaterialFriction(0.5f), Is.EqualTo(3.3333f).Within(0.001f), "cap reaches 3.33x the floor");
        Assert.That(MaterialFriction(-1f), Is.EqualTo(0f).Within(0.0001f));
        Assert.That(MaterialFriction(2f), Is.EqualTo(3.3333f).Within(0.001f), "values above the cap clamp");
    }

    private static bool Snap(float gap, float incoming, float resolved, float dt, bool isStay)
        => (bool)GetPhysicsMethod("ShouldSnapFloorContact").Invoke(null, [gap, incoming, resolved, dt, isStay])!;

    [Test]
    public void FloorSnapOnlyCoversSlopPlusOneStep()
    {
        Assert.That(Snap(0.02f, 0f, 0f, 0.02f, false), Is.False, "a real rotation gap must not teleport");
        Assert.That(Snap(0.02f, 0f, 0f, 0.02f, true), Is.False);
        Assert.That(Snap(0.0015f, 0f, 0f, 0.02f, false), Is.True, "contact slop still snaps");
        Assert.That(Snap(0.0015f, 0f, 0f, 0.02f, true), Is.True);
        Assert.That(Snap(0.5f, -25f, 0f, 0.02f, false), Is.True, "high speed CCD reach must still snap");
        Assert.That(Snap(0.001f, 1f, 0f, 0.02f, false), Is.False, "outgoing bodies never snap down");
        Assert.That(Snap(0.0015f, 0f, -1f, 0.02f, true), Is.False, "stay with downward resolve never snaps");
        Assert.That(Snap(0.004f, -0.15f, 1f, 0.02f, false), Is.True, "entry inside the travel budget snaps");
        Assert.That(Snap(0.0126f, -0.11f, 0f, 1f / 90f, false), Is.False, "fine step gap beyond budget must not snap");
    }
}
