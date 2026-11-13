using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class FerrofluidCubeFaceSettlingTests
{
    private static MethodInfo GetDamping() => Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings.FerrofluidGrabPhysics", true)!
        .GetMethod("EvaluateCubeFaceAngularDamping", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static float Rate(Quaternion rotation, Vector3 omega)
        => (float)GetDamping().Invoke(null, new object[] { rotation, omega, 2f, 2f, 10f })!;

    private static float Rate(Quaternion rotation, Vector3 omega, float strength, float exponent, float startAngle)
        => (float)GetDamping().Invoke(null, new object[] { rotation, omega, strength, exponent, startAngle })!;

    [Test]
    public void RetreatingFiveDegreeFaceMustBeEligible()
    {
        Assert.That(Rate(Quaternion.Euler(0f, 0f, 5f), new Vector3(0f, 0f, 0.5f)), Is.GreaterThan(0f));
    }

    [Test]
    public void ApproachingTwoDegreeFaceMustNotBeBraked()
    {
        Assert.That(Rate(Quaternion.Euler(0f, 0f, 2f), new Vector3(0f, 0f, -0.5f)), Is.EqualTo(0f));
    }

    [Test]
    public void AllSixFacesBrakeOnRetreat()
    {
        foreach (Vector3 face in new[] { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
        {
            foreach (float yaw in new[] { 0f, 60f, 210f })
            {
                foreach (float sign in new[] { 1f, -1f })
                {
                    Quaternion rotation = Quaternion.AngleAxis(yaw, Vector3.up)
                        * Quaternion.AngleAxis(5f * sign, Vector3.right)
                        * Quaternion.FromToRotation(face, Vector3.up);
                    Vector3 tiltedNormal = Quaternion.AngleAxis(yaw, Vector3.up)
                        * Quaternion.AngleAxis(5f * sign, Vector3.right) * Vector3.up;
                    Vector3 omega = Vector3.Cross(Vector3.up, tiltedNormal).normalized * 0.5f;
                    Assert.That(Rate(rotation, omega), Is.GreaterThan(0f), $"{face} yaw {yaw} sign {sign} must brake on retreat");
                    Assert.That(Rate(rotation, -omega), Is.EqualTo(0f), $"{face} yaw {yaw} sign {sign} must not brake on approach");
                }
            }
        }
    }

    [Test]
    public void NoBrakeOnPureYawOrFaceSpin()
    {
        Assert.That(Rate(Quaternion.identity, new Vector3(0f, 0.5f, 0f)), Is.EqualTo(0f), "pure world-up yaw never tilts a face");
        Quaternion tilted = Quaternion.Euler(5f, 0f, 0f);
        Assert.That(Rate(tilted, tilted * Vector3.up * 0.5f), Is.EqualTo(0f), "spin about the up-facing face normal never tilts");
    }

    [Test]
    public void EdgeCornerAndTiltedTopAreIneligible()
    {
        Assert.That(Rate(Quaternion.Euler(0f, 0f, 45f), new Vector3(0f, 0f, 0.5f)), Is.EqualTo(0f), "45 degree edge");
        Assert.That(Rate(Quaternion.FromToRotation(Vector3.one.normalized, Vector3.up), new Vector3(0.3f, 0.3f, 0.3f)), Is.EqualTo(0f), "corner diagonal");
        Quaternion tiltedTop = Quaternion.Euler(20f, 0f, 0f) * Quaternion.FromToRotation(Vector3.one.normalized, Vector3.up);
        Assert.That(Rate(tiltedTop, new Vector3(0.5f, 0f, 0f)), Is.EqualTo(0f), "20 degree tilted top");
        Assert.That(Rate(tiltedTop, new Vector3(0.5f, 0f, 0f), 2f, 2f, 30f), Is.EqualTo(0f), "tilted top stays out even at max start angle");
    }

    [Test]
    public void RateFollowsStrengthTimesStartOverAngleToExponent()
    {
        foreach ((float angle, float expected) in new[] { (9f, 2f * 100f / 81f), (5f, 8f), (2.5f, 32f), (1f, 200f) })
        {
            float rate = Rate(Quaternion.Euler(0f, 0f, angle), new Vector3(0f, 0f, 0.5f));
            Assert.That(rate, Is.EqualTo(expected).Within(expected * 0.02f), $"{angle} degrees must rate {expected}");
        }

        float halved = Rate(Quaternion.Euler(0f, 0f, 2.5f), new Vector3(0f, 0f, 0.5f));
        float doubled = Rate(Quaternion.Euler(0f, 0f, 5f), new Vector3(0f, 0f, 0.5f));
        Assert.That(halved / doubled, Is.EqualTo(4f).Within(0.2f), "halving angle quadruples damping at exponent 2");
        Assert.That(
            Rate(Quaternion.Euler(0f, 0f, 5f), new Vector3(0f, 0f, 0.5f), 4f, 2f, 10f),
            Is.EqualTo(16f).Within(0.5f),
            "strength is linear");
        Assert.That(
            Rate(Quaternion.Euler(0f, 0f, 5f), new Vector3(0f, 0f, 0.5f), 2f, 0f, 10f),
            Is.EqualTo(2f).Within(0.05f),
            "exponent 0 is constant inside the band");
        Assert.That(Rate(Quaternion.Euler(0f, 0f, 5f), new Vector3(0f, 0f, 0.5f), 0f, 2f, 10f), Is.EqualTo(0f), "strength 0 disables");
        Assert.That(Rate(Quaternion.Euler(0f, 0f, 5f), new Vector3(0f, 0f, 0.5f), 2f, 2f, 0f), Is.EqualTo(0f), "start angle 0 disables");
    }

    [Test]
    public void CutoffBoundariesAndFastRetreat()
    {
        Assert.That(Rate(Quaternion.Euler(0f, 0f, 9.99f), new Vector3(0f, 0f, 0.5f)), Is.GreaterThan(0f), "just inside start angle brakes");
        Assert.That(Rate(Quaternion.Euler(0f, 0f, 10.01f), new Vector3(0f, 0f, 0.5f)), Is.EqualTo(0f), "just outside start angle does not");
        Assert.That(Rate(Quaternion.Euler(0f, 0f, 0.5f), new Vector3(0f, 0f, 5f)), Is.GreaterThan(0f), "fast retreat still brakes");
        float flat = Rate(Quaternion.identity, new Vector3(0f, 0.5f, 0f), 10f, 6f, 30f);
        Assert.That(flat, Is.EqualTo(0f), "exact flat with pure-face-normal spin must not brake at max settings");
        float tipping = Rate(Quaternion.identity, new Vector3(0f, 0f, 0.5f), 10f, 6f, 30f);
        Assert.That(tipping, Is.GreaterThan(0f), "an exactly flat face already tipping must brake");
        float nearFlat = Rate(Quaternion.Euler(0f, 0f, 0.01f), new Vector3(0f, 0f, 0.5f), 10f, 6f, 30f);
        Assert.That(!float.IsNaN(nearFlat) && !float.IsInfinity(nearFlat), "near-flat stays finite at max settings");
    }

    private static MethodInfo GetLanding() => Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings.FerrofluidGrabPhysics", true)!
        .GetMethod("EvaluateCubeFaceLandingAngularVelocity", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static Vector3 Land(Quaternion rotation, Vector3 omega, float dt, float strength = 0.5f)
        => (Vector3)GetLanding().Invoke(null, new object[] { rotation, omega, dt, strength })!;

    [Test]
    public void LandingImpulseSoftensTheFinalStepWithoutForcingFlat()
    {
        Vector3 result = Land(Quaternion.Euler(0f, 0f, 3f), new Vector3(0f, 0f, -4f), 0.02f);
        float fullCorrection = -3f * Mathf.Deg2Rad / 0.02f;
        Assert.That(result.z, Is.EqualTo(Mathf.Lerp(-4f, fullCorrection, 0.5f)).Within(0.002f));
        Quaternion next = Quaternion.AngleAxis(result.magnitude * 0.02f * Mathf.Rad2Deg, result.normalized) * Quaternion.Euler(0f, 0f, 3f);
        float unbrakedOvershoot = (4f * 0.02f * Mathf.Rad2Deg) - 3f;
        Assert.That(Quaternion.Angle(next, Quaternion.identity), Is.EqualTo(unbrakedOvershoot * 0.5f).Within(0.05f));
    }

    [Test]
    public void LandingImpulseUsesCompiledDefaultStrength()
    {
        FieldInfo constant = Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings.FerrofluidGrabPhysics", true)!
            .GetField("DefaultCubeFaceLandingStrength", BindingFlags.Static | BindingFlags.NonPublic)!;
        float defaultStrength = (float)constant.GetRawConstantValue()!;
        Vector3 result = Land(Quaternion.Euler(0f, 0f, 3f), new Vector3(0f, 0f, -4f), 0.02f, defaultStrength);
        Quaternion next = Quaternion.AngleAxis(result.magnitude * 0.02f * Mathf.Rad2Deg, result.normalized) * Quaternion.Euler(0f, 0f, 3f);
        float expectedOvershoot = ((4f * 0.02f * Mathf.Rad2Deg) - 3f) * 0.45f;
        Assert.That(
            Quaternion.Angle(next, Quaternion.identity),
            Is.EqualTo(expectedOvershoot).Within(0.02f),
            "the compiled default strength must leave only 45% residual overshoot");
    }

    [Test]
    public void LandingImpulseDoesNotFireOnNonCrossingApproach()
    {
        Quaternion rotation = Quaternion.Euler(0f, 0f, 3f);
        Assert.That(Land(rotation, new Vector3(0f, 0f, -1f), 0.02f), Is.EqualTo(new Vector3(0f, 0f, -1f)));
        Assert.That(Land(rotation, new Vector3(0f, 0f, 4f), 0.02f), Is.EqualTo(new Vector3(0f, 0f, 4f)));
        Assert.That(Land(Quaternion.identity, new Vector3(0f, 0f, -4f), 0.02f), Is.EqualTo(new Vector3(0f, 0f, -4f)));
        Assert.That(Land(Quaternion.Euler(0f, 0f, 15f), new Vector3(0f, 0f, -4f), 0.02f), Is.EqualTo(new Vector3(0f, 0f, -4f)));
        Assert.That(Land(rotation, new Vector3(0f, 0f, -10f), 0.02f), Is.EqualTo(new Vector3(0f, 0f, -10f)));
    }

    [Test]
    public void LandingImpulsePreservesFaceNormalSpin()
    {
        Quaternion rotation = Quaternion.Euler(0f, 0f, 3f);
        Vector3 omega = new Vector3(0f, 0f, -4f) + rotation * Vector3.up * 1f;
        Vector3 result = Land(rotation, omega, 0.02f);
        Quaternion expected = Quaternion.AngleAxis(Vector3.Dot(omega, rotation * Vector3.up) * 0.02f * Mathf.Rad2Deg, Vector3.up)
            * Quaternion.FromToRotation(rotation * Vector3.up, Vector3.up)
            * rotation;
        Quaternion delta = expected * Quaternion.Inverse(rotation);
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        Vector3 fullCorrection = axis * (angle * Mathf.Deg2Rad / 0.02f);
        Assert.That(Vector3.Distance(result, Vector3.Lerp(omega, fullCorrection, 0.5f)), Is.LessThan(0.002f));
    }

    [Test]
    public void LandingImpulseStrengthScalesBraking()
    {
        Quaternion rotation = Quaternion.Euler(0f, 0f, 3f);
        Vector3 omega = new(0f, 0f, -4f);
        Assert.That(Land(rotation, omega, 0.02f, 0f), Is.EqualTo(omega), "zero strength disables the landing impulse");
        float fullCorrection = -3f * Mathf.Deg2Rad / 0.02f;
        Assert.That(Land(rotation, omega, 0.02f, 0.6f).z, Is.EqualTo(Mathf.Lerp(-4f, fullCorrection, 0.6f)).Within(0.002f), "0.6 reproduces the previous braking");
        Assert.That(Land(rotation, omega, 0.02f).z, Is.EqualTo(Mathf.Lerp(-4f, fullCorrection, 0.5f)).Within(0.002f), "legacy 0.5 profile brakes less than 0.6");
    }
}
