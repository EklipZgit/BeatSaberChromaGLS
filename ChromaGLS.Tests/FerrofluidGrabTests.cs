using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class FerrofluidGrabTests
{
    [Test]
    public void GrabPointVelocityRejectsConflictingMomentumAndCannotOvershoot()
    {
        MethodInfo follow = GetGrabMethod("EvaluateGrabPointVelocity");
        (Vector3 Velocity, Vector3 Gap, Vector3 Expected)[] cases =
        [
            (new(0f, 3f, 2f), Vector3.right, new(4f, 0f, 0f)),
            (new(3f, 5f, 0f), Vector3.right, new(7f, 0f, 0f)),
            (new(-100f, 2f, 0f), Vector3.right, new(4f, 0f, 0f)),
            (new(100f, 2f, 0f), Vector3.right * 0.01f, new(0.5f, 0f, 0f)),
            (new(4f, 5f, 6f), Vector3.zero, Vector3.zero),
            (new(7f, 0f, 0f), Vector3.up * 0.0000001f, Vector3.up * 0.000005f),
        ];
        foreach ((Vector3 velocity, Vector3 gap, Vector3 expected) in cases)
        {
            Vector3 actual = (Vector3)follow.Invoke(null, [velocity, gap, 0.02f])!;
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.000001f));
        }
    }

    [Test]
    public void CalculateGrabTargetStaysBetweenCanonicalAndRawAndIsExactMidSample()
    {
        Assembly plugin = Assembly.Load("ChromaGLS");
        Type grabbable = plugin.GetType("ChromaGLS.Settings.FerrofluidGrabbable", true)!;
        MethodInfo calculate = grabbable.GetMethod("CalculateGrabTarget", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        object body = FormatterServices.GetUninitializedObject(grabbable);
        BindingFlags inst = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => grabbable.GetField(name, inst)!.SetValue(body, value);
        Vector3 canonical = new(0.1f, 0.2f, -0.3f);
        Vector3 raw = new(0.9f, 0.8f, -0.7f);
        Vector3 previous = new(0f, 0.125f, -0.25f);
        Vector3 step = canonical - previous;
        Set("_smoothedGrabPosition", canonical);
        Set("_previousGrabTarget", previous);
        Set("_rawGrabTarget", raw);
        Set("_grabSampleTime", 2f);
        Set("_grabSampleDeltaTime", 0.02f);

        Vector3 mid = (Vector3)calculate.Invoke(body, [2.01f])!;
        Assert.That(Vector3.Distance(mid, canonical + (step * 0.5f)), Is.LessThan(0.000001f), "mid-step resample must follow the measured step exactly");

        Vector3 far = (Vector3)calculate.Invoke(body, [10f])!;
        Assert.That(Vector3.Distance(far, raw), Is.LessThan(0.000001f), "far-future resample must stop exactly at the known raw pointer");
        for (int axis = 0; axis < 3; axis++)
        {
            Assert.That(far[axis], Is.InRange(Mathf.Min(canonical[axis], raw[axis]) - 0.000001f, Mathf.Max(canonical[axis], raw[axis]) + 0.000001f));
        }

        // A reversed raw pointer reverses the bounded step on every axis.
        Vector3 reversedRaw = new(-0.7f, -0.4f, 0.1f);
        Vector3 reversedPrevious = new(0.2f, 0.275f, -0.35f);
        Set("_previousGrabTarget", reversedPrevious);
        Set("_rawGrabTarget", reversedRaw);
        Vector3 reversedStep = canonical - reversedPrevious;
        Vector3 reversedMid = (Vector3)calculate.Invoke(body, [2.01f])!;
        Assert.That(Vector3.Distance(reversedMid, canonical + (reversedStep * 0.5f)), Is.LessThan(0.000001f));
        Vector3 reversedFar = (Vector3)calculate.Invoke(body, [10f])!;
        Assert.That(Vector3.Distance(reversedFar, reversedRaw), Is.LessThan(0.000001f));
        for (int axis = 0; axis < 3; axis++)
        {
            Assert.That(reversedFar[axis], Is.InRange(Mathf.Min(canonical[axis], reversedRaw[axis]) - 0.000001f, Mathf.Max(canonical[axis], reversedRaw[axis]) + 0.000001f));
        }

        // A dt==0 sample leaves previous == canonical, so the zero step returns it verbatim.
        Set("_previousGrabTarget", canonical);
        Set("_grabSampleDeltaTime", 0f);
        Vector3 identity = (Vector3)calculate.Invoke(body, [99f])!;
        Assert.That(identity, Is.EqualTo(canonical));
        // A sample at or before the stamped time never extrapolates backwards.
        Set("_previousGrabTarget", previous);
        Set("_grabSampleDeltaTime", 0.02f);
        Vector3 past = (Vector3)calculate.Invoke(body, [1f])!;
        Assert.That(past, Is.EqualTo(canonical));
    }

    [Test]
    public void GrabSpikeScaleRampsLinearlyToOnePointSixAndHandlesBinaryTriggers()
    {
        MethodInfo scale = GetGrabMethod("EvaluateMagnetHeightAmplification");
        Assert.That(scale, Is.Not.Null, "The pre-grab spike scale must be a pure function of the analog trigger value.");
        Assert.That((float)scale!.Invoke(null, [0f])!, Is.EqualTo(1f).Within(0.000001f));
        Assert.That((float)scale.Invoke(null, [0.4f])!, Is.EqualTo(1.3f).Within(0.000001f));
        Assert.That((float)scale.Invoke(null, [0.8f])!, Is.EqualTo(1.6f).Within(0.000001f));
        Assert.That((float)scale.Invoke(null, [1f])!, Is.EqualTo(1.6f).Within(0.000001f), "Binary 0/1 triggers must saturate at the same held scale.");
        Assert.That((float)scale.Invoke(null, [-0.2f])!, Is.EqualTo(1f).Within(0.000001f));
    }

    [Test]
    public void GrabRequiresTriggerRiseAboveThresholdAndReleasesAtThreshold()
    {
        MethodInfo crossing = GetGrabMethod("IsGrabTriggerCrossing");
        MethodInfo release = GetGrabMethod("ShouldReleaseGrab");
        Assert.That(crossing, Is.Not.Null, "Acquisition must detect the trigger rising strictly above the threshold.");
        Assert.That(release, Is.Not.Null, "Release must trigger at or below the threshold.");
        Assert.That((bool)crossing!.Invoke(null, [0f, 0.9f])!, Is.True);
        Assert.That((bool)crossing.Invoke(null, [0.79f, 0.8f])!, Is.False, "Exactly 0.8 must not grab; the grab threshold is strictly above 0.8.");
        Assert.That((bool)crossing.Invoke(null, [0.8f, 0.80001f])!, Is.True);
        Assert.That((bool)crossing.Invoke(null, [0.9f, 1f])!, Is.False, "A trigger already held while the aim moves onto the object must not grab.");
        Assert.That((bool)crossing.Invoke(null, [0f, 1f])!, Is.True, "A binary trigger press counts as a crossing.");
        Assert.That((bool)release!.Invoke(null, [0.8f])!, Is.True);
        Assert.That((bool)release.Invoke(null, [0.80001f])!, Is.False);
        Assert.That((bool)release.Invoke(null, [0f])!, Is.True);
    }

    [Test]
    public void HeldPoseRetainsControllerLocalOffsetWithoutSnapping()
    {
        MethodInfo capture = GetGrabMethod("CaptureGrabOffset");
        MethodInfo held = GetGrabMethod("EvaluateHeldPose");
        Assert.That(capture, Is.Not.Null, "Grab must capture the object pose in controller-local space at acquisition.");
        Assert.That(held, Is.Not.Null, "The held pose must be recomputed from the captured local offset each frame.");

        Vector3 attachPosition = new(0.2f, 1.5f, 0.4f);
        Quaternion attachRotation = Quaternion.Euler(10f, 35f, -15f);
        Vector3 bodyPosition = new(-0.6f, 0.275f, 1.1f);
        Quaternion bodyRotation = Quaternion.Euler(0f, 60f, 20f);
        object[] captured = [attachPosition, attachRotation, bodyPosition, bodyRotation, null!, null!];
        capture!.Invoke(null, captured);
        Vector3 localOffset = (Vector3)captured[4]!;
        Quaternion localRotation = (Quaternion)captured[5]!;

        Vector3 movedPosition = new(0.8f, 1.1f, -0.3f);
        Quaternion movedRotation = Quaternion.Euler(-25f, 80f, 5f);
        object[] evaluated = [movedPosition, movedRotation, localOffset, localRotation, null!, null!];
        held!.Invoke(null, evaluated);
        Vector3 heldPosition = (Vector3)evaluated[4]!;
        Quaternion heldRotation = (Quaternion)evaluated[5]!;

        Vector3 expectedPosition = movedPosition + (movedRotation * localOffset);
        Quaternion expectedRotation = movedRotation * localRotation;
        Assert.That(Vector3.Distance(heldPosition, expectedPosition), Is.LessThan(0.000001f));
        Assert.That(Quaternion.Angle(heldRotation, expectedRotation), Is.LessThan(0.0001f));
        Assert.That(
            Vector3.Distance(localOffset, Quaternion.Inverse(attachRotation) * (bodyPosition - attachPosition)),
            Is.LessThan(0.000001f),
            "The captured offset must reproduce the grab-time world offset; recomputing it differently snaps the object.");
        Assert.That(
            Quaternion.Angle(localRotation, Quaternion.Inverse(attachRotation) * bodyRotation),
            Is.LessThan(0.0001f));
        Assert.That(heldPosition, Is.Not.EqualTo(movedPosition), "The held object must keep its grab offset instead of snapping into the hand.");
    }

    [Test]
    public void AngularVelocityUsesShortestArcBetweenRotations()
    {
        MethodInfo angular = GetGrabMethod("EvaluateAngularVelocity");
        Assert.That(angular, Is.Not.Null, "Release spin needs the shortest quaternion delta as radians per second.");
        const float deltaTime = 0.02f;

        Vector3 wrap = (Vector3)angular!.Invoke(null, [Quaternion.identity, Quaternion.Euler(0f, 350f, 0f), deltaTime])!;
        Assert.That(wrap.y, Is.EqualTo(-10f * Mathf.Deg2Rad / deltaTime).Within(0.001f), "350 degrees must integrate as -10 degrees, not +350.");
        Assert.That(wrap.x, Is.EqualTo(0f).Within(0.0001f));
        Assert.That(wrap.z, Is.EqualTo(0f).Within(0.0001f));

        Vector3 quarter = (Vector3)angular.Invoke(null, [Quaternion.identity, Quaternion.Euler(90f, 0f, 0f), deltaTime])!;
        Assert.That(quarter.x, Is.EqualTo(90f * Mathf.Deg2Rad / deltaTime).Within(0.001f));

        Vector3 still = (Vector3)angular.Invoke(null, [Quaternion.Euler(20f, 40f, 60f), Quaternion.Euler(20f, 40f, 60f), deltaTime])!;
        Assert.That(still.magnitude, Is.LessThan(0.0001f));
    }

    [Test]
    public void ThrowVelocityCombinesAttachTranslationAndOrbit()
    {
        MethodInfo throwVelocity = GetGrabMethod("EvaluateThrowVelocity");
        Assert.That(throwVelocity, Is.Not.Null, "Release must retain the attachment's recent translation and spin about the remote grab offset.");

        // The attach frame translates +x while rotating 90 degrees about +y over 0.1s; the body hangs 0.5m along +x.
        object[] thrown =
        [
            new Vector3(0f, 0f, 0f),
            Quaternion.identity,
            new Vector3(0.1f, 0f, 0f),
            Quaternion.Euler(0f, 9f, 0f),
            0.1f,
            new Vector3(0.6f, 0f, 0f),
            null!,
            null!,
        ];
        throwVelocity!.Invoke(null, thrown);
        Vector3 velocity = (Vector3)thrown[6]!;
        Vector3 angularVelocity = (Vector3)thrown[7]!;

        float expectedSpin = (9f * Mathf.Deg2Rad) / 0.1f;
        Assert.That(angularVelocity.y, Is.EqualTo(expectedSpin).Within(0.0001f), "The body must keep the hand's yaw rate.");
        Vector3 orbit = Vector3.Cross(new Vector3(0f, expectedSpin, 0f), new Vector3(0.5f, 0f, 0f));
        Assert.That(
            Vector3.Distance(velocity, new Vector3(1f, 0f, 0f) + orbit),
            Is.LessThan(0.0001f),
            "Release velocity must add the orbit term omega x offset to the attachment's translation.");
    }

    [Test]
    public void GrabOwnershipRejectsSecondTargetAndSecondHand()
    {
        Type ownershipType = GetPluginType("ChromaGLS.Settings.FerrofluidGrabOwnership");
        Assert.That(ownershipType, Is.Not.Null, "Two-hand arbitration must track hand and object ownership centrally.");
        object ownership = Activator.CreateInstance(ownershipType!, nonPublic: true)!;
        MethodInfo tryAcquire = ownershipType.GetMethod("TryAcquire", BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo ownerOf = ownershipType.GetMethod("OwnerOf", BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo heldTarget = ownershipType.GetMethod("HeldTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo releaseTarget = ownershipType.GetMethod("ReleaseTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.That(tryAcquire, Is.Not.Null);
        Assert.That(ownerOf, Is.Not.Null);
        Assert.That(heldTarget, Is.Not.Null);
        Assert.That(releaseTarget, Is.Not.Null);

        object leftHand = new();
        object rightHand = new();
        object cube = new();
        object sphere = new();

        Assert.That((bool)tryAcquire!.Invoke(ownership, [leftHand, cube])!, Is.True);
        Assert.That((bool)tryAcquire.Invoke(ownership, [leftHand, sphere])!, Is.False, "One hand cannot hold two overlapping targets.");
        Assert.That((bool)tryAcquire.Invoke(ownership, [rightHand, cube])!, Is.False, "A held object cannot be taken by the other hand.");
        Assert.That(ownerOf!.Invoke(ownership, [cube])!, Is.SameAs(leftHand), "Ownership is captured at acquisition.");
        Assert.That((bool)tryAcquire.Invoke(ownership, [rightHand, sphere])!, Is.True, "The other hand may hold a different object.");
        Assert.That(heldTarget!.Invoke(ownership, [rightHand])!, Is.SameAs(sphere));
        Assert.That(ownerOf.Invoke(ownership, [cube])!, Is.SameAs(leftHand), "Another hand acquiring its own object must not change existing ownership.");

        releaseTarget!.Invoke(ownership, [cube]);
        Assert.That(ownerOf.Invoke(ownership, [cube])!, Is.Null);
        Assert.That((bool)tryAcquire.Invoke(ownership, [leftHand, cube])!, Is.True, "A released object can be grabbed again.");
    }

    [Test]
    public void EnclosureProjectsBodyInsideAllBoundsButNeverCeiling()
    {
        object enclosure = CreateEnclosure();
        object sphere = CreateShape(true, Vector3.one * 0.275f, 0.275f);
        MethodInfo clamp = GetGrabMethod("ClampEnclosurePosition");
        MethodInfo support = GetGrabMethod("EvaluateSupportExtent");
        Assert.That(clamp, Is.Not.Null, "Held and thrown bodies must be projected back inside the D-shaped enclosure.");
        Assert.That(support, Is.Not.Null, "Bounds must include the shape's support extent so the whole body stays inside.");

        // Floor keeps the full radius above y=0.
        Vector3 floored = Clamp(clamp!, enclosure, sphere, new Vector3(0.3f, 0.1f, 0.4f), Quaternion.identity);
        Assert.That(floored.y, Is.EqualTo(0.275f).Within(0.00001f));

        // The rear plane pushes a body fully in front of it while the cylinder keeps it radially inside.
        Vector3 reared = Clamp(clamp, enclosure, sphere, new Vector3(0.3f, 0.5f, -2.6f), Quaternion.identity);
        Assert.That(Vector3.Dot(reared - new Vector3(0f, 0f, -2f), Vector3.forward), Is.GreaterThanOrEqualTo(0.275f - 0.0001f));
        Assert.That(Mathf.Sqrt((reared.x * reared.x) + (reared.z * reared.z)), Is.LessThanOrEqualTo(1.725f + 0.0001f));
        Assert.That(reared.z, Is.GreaterThan(-2.6f));

        // The cylindrical wall constrains every horizontal direction, including the forward menu side.
        Vector3 sided = Clamp(clamp, enclosure, sphere, new Vector3(3f, 0.5f, 0f), Quaternion.identity);
        Assert.That(Mathf.Sqrt((sided.x * sided.x) + (sided.z * sided.z)), Is.EqualTo(1.725f).Within(0.0001f));
        Vector3 fronted = Clamp(clamp, enclosure, sphere, new Vector3(0f, 0.5f, 3f), Quaternion.identity);
        Assert.That(Mathf.Sqrt((fronted.x * fronted.x) + (fronted.z * fronted.z)), Is.EqualTo(1.725f).Within(0.0001f));

        // A point violating both the rear plane and the cylinder lands inside both constraints.
        Vector3 cornered = Clamp(clamp, enclosure, sphere, new Vector3(1.2f, 0.5f, -2.4f), Quaternion.identity);
        float rearDepth = Vector3.Dot(cornered - new Vector3(0f, 0f, -2f), Vector3.forward);
        float radial = Mathf.Sqrt((cornered.x * cornered.x) + (cornered.z * cornered.z));
        Assert.That(rearDepth, Is.GreaterThanOrEqualTo(0.275f - 0.0001f));
        Assert.That(radial, Is.LessThanOrEqualTo(1.725f + 0.0001f));

        // A high arc stays unbounded vertically.
        Vector3 high = Clamp(clamp, enclosure, sphere, new Vector3(0.4f, 100f, 0.3f), Quaternion.identity);
        Assert.That(high.y, Is.EqualTo(100f).Within(0.0001f));

        // A rotated cube's support includes its projected half extents on the constraint normal.
        Quaternion tilted = Quaternion.Euler(0f, 0f, 45f);
        object box = CreateShape(false, Vector3.one * 0.275f, 0f);
        float downExtent = (float)support!.Invoke(null, [box, tilted, Vector3.down])!;
        Assert.That(downExtent, Is.EqualTo(0.275f * 2f * Mathf.Cos(Mathf.PI / 4f)).Within(0.0001f));
        Vector3 boxed = Clamp(clamp, enclosure, box, new Vector3(0.2f, 0.1f, 0.4f), tilted);
        Assert.That(boxed.y, Is.EqualTo(downExtent).Within(0.0001f));
    }

    [Test]
    public void EnforceEnclosureReflectsOutgoingVelocityWithSmallBounce()
    {
        object enclosure = CreateEnclosure();
        object sphere = CreateShape(true, Vector3.one * 0.275f, 0.275f);
        MethodInfo enforce = GetGrabMethod("EnforceEnclosure");
        Assert.That(enforce, Is.Not.Null, "Thrown bodies must lose their outward velocity with a small reflected bounce.");

        // A high-speed radial throw is projected inside and its outward speed reflects at restitution.
        object[] thrown =
        [
            enclosure,
            sphere,
            new Vector3(3f, 0.4f, 0f),
            Quaternion.identity,
            new Vector3(10f, 0f, 0f),
            Vector3.zero,
            Vector3.zero,
            0.02f,
            0.1f,
            null!,
            null!,
        ];
        enforce!.Invoke(null, thrown);
        Vector3 position = (Vector3)thrown[9]!;
        Vector3 velocity = (Vector3)thrown[10]!;
        Assert.That(position.x, Is.EqualTo(1.725f).Within(0.0001f));
        Assert.That(velocity.x, Is.EqualTo(-1f).Within(0.0001f), "Outward velocity must reflect at the configured restitution instead of being zeroed.");

        // Inward motion keeps its speed and direction.
        object[] inbound =
        [
            enclosure,
            sphere,
            new Vector3(1.8f, 0.4f, 0f),
            Quaternion.identity,
            new Vector3(-5f, 0f, 0f),
            Vector3.zero,
            Vector3.zero,
            0.02f,
            0.1f,
            null!,
            null!,
        ];
        enforce.Invoke(null, inbound);
        Assert.That(((Vector3)inbound[10]!).x, Is.EqualTo(-5f).Within(0.0001f));

        // A body still inside the wall crosses the boundary next tick: the predicted contact
        // reflects the outward component at restitution while tangential and upward motion pass through.
        object[] predicted =
        [
            enclosure,
            sphere,
            new Vector3(2.6f, 0.4f, 0f),
            Quaternion.identity,
            new Vector3(8f, 3f, 0f),
            Vector3.zero,
            new Vector3(0f, -0.196f, 0f),
            0.02f,
            0.2f,
            null!,
            null!,
        ];
        enforce.Invoke(null, predicted);
        Vector3 predictedVelocity = (Vector3)predicted[10]!;
        Assert.That(
            predictedVelocity.x,
            Is.EqualTo(-1.6f).Within(0.0001f),
            "The predicted wall contact must reflect the outward component at the configured restitution.");
        Assert.That(
            predictedVelocity.y,
            Is.EqualTo(3f).Within(0.0001f),
            "The wall reflection must preserve tangential and upward velocity.");

        // The rear plane reflects the same way through the predicted-contact path.
        object[] rear =
        [
            enclosure,
            sphere,
            new Vector3(0f, 0.4f, -1.9f),
            Quaternion.identity,
            new Vector3(0f, 0f, -8f),
            Vector3.zero,
            Vector3.zero,
            0.02f,
            0.2f,
            null!,
            null!,
        ];
        enforce.Invoke(null, rear);
        Vector3 rearVelocity = (Vector3)rear[10]!;
        Assert.That(
            rearVelocity.z,
            Is.EqualTo(1.6f).Within(0.0001f),
            "The rear plane must reflect the outward component at the configured restitution.");
    }

    [Test]
    public void FluidSpringOpposesImpulseAndSettlesAtAnyRate()
    {
        Type springType = GetPluginType("ChromaGLS.Settings.FluidSpringState");
        Assert.That(springType, Is.Not.Null, "Fluid lag needs an explicit offset plus velocity spring state.");
        MethodInfo impulse = GetGrabMethod("ApplyFluidImpulse");
        MethodInfo integrate = GetGrabMethod("IntegrateFluidSpring");
        Assert.That(impulse, Is.Not.Null, "Velocity changes must feed the lag springs as opposite impulses.");
        Assert.That(integrate, Is.Not.Null, "Lag springs must integrate with the configured spike spring response.");

        object state = Activator.CreateInstance(springType!)!;
        object impulsed = impulse!.Invoke(null, [state, new Vector3(2f, 0f, 0f)])!;
        Assert.That(((Vector3)springType.GetField("Velocity", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(impulsed)!).x, Is.EqualTo(-2f).Within(0.000001f), "An acceleration impulse must drive the fluid in the opposite direction.");

        foreach (float deltaTime in new[] { 1f / 60f, 1f / 120f, 1f / 240f })
        {
            object current = impulsed;
            float peakOffset = 0f;
            for (int step = 0; step < 600; step++)
            {
                current = integrate!.Invoke(null, [current, 250f, 12f, deltaTime])!;
                float offset = ((Vector3)springType.GetField("Offset", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(current)!).x;
                peakOffset = Mathf.Max(peakOffset, Mathf.Abs(offset));
            }

            float settled = ((Vector3)springType.GetField("Offset", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(current)!).magnitude;
            Assert.That(peakOffset, Is.GreaterThan(0.005f), $"The lag must visibly displace at dt={deltaTime} before settling.");
            Assert.That(settled, Is.LessThan(0.0001f), $"The lag spring must return to rest at dt={deltaTime}.");
        }

        // Constant velocity produces no impulses, so the fluid stays at rest rather than lagging forever.
        object steady = Activator.CreateInstance(springType)!;
        for (int step = 0; step < 120; step++)
        {
            steady = integrate.Invoke(null, [steady, 250f, 12f, 1f / 60f])!;
        }

        Assert.That(((Vector3)springType.GetField("Offset", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(steady)!).magnitude, Is.LessThan(0.0000001f), "Unchanging velocity must not keep forcing the lag spring.");
    }

    [Test]
    public void AngularFluidSpringOpposesSpinImpulse()
    {
        Type springType = GetPluginType("ChromaGLS.Settings.FluidSpringState");
        MethodInfo impulse = GetGrabMethod("ApplyFluidImpulse");
        MethodInfo integrate = GetGrabMethod("IntegrateFluidSpring");
        Assert.That(impulse, Is.Not.Null);
        Assert.That(integrate, Is.Not.Null);

        object state = impulse!.Invoke(null, [Activator.CreateInstance(springType!)!, new Vector3(0f, 3f, 0f)])!;
        float mostNegative = 0f;
        for (int step = 0; step < 600; step++)
        {
            state = integrate!.Invoke(null, [state, 250f, 12f, 1f / 120f])!;
            mostNegative = Mathf.Min(mostNegative, ((Vector3)springType.GetField("Offset", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!).y);
        }

        Assert.That(mostNegative, Is.LessThan(-0.01f), "A spin-up impulse must rotate the fluid lag in the opposite sense.");
        Assert.That(((Vector3)springType.GetField("Offset", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!).magnitude, Is.LessThan(0.0001f), "The angular lag must also return to rest.");
    }

    [Test]
    public void CpuMirrorHeightChangesOnlySpikeDisplacement()
    {
        Type deformationType = GetPluginType("ChromaGLS.Settings.FerrofluidPrimitiveDeformation");
        Type configType = GetPluginType("ChromaGLS.Settings.Config");
        Type environmentType = GetPluginType("ChromaGLS.Settings.FerrofluidEnvironmentObjects");
        Type shapeType = GetPluginType("ChromaGLS.Settings.FerrofluidSurfaceShape");
        object config = Activator.CreateInstance(configType, nonPublic: true)!;
        object sphere = Enum.ToObject(shapeType, 2);
        object settings = deformationType
            .GetMethod("EvaluateSettings", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [sphere, config, 1f])!;
        MethodInfo evaluateSites = environmentType.GetMethod("EvaluatePrimitiveSites", BindingFlags.Static | BindingFlags.NonPublic)!;
        float roundness = (float)environmentType.GetMethod("EvaluateRoundness", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [sphere, config])!;
        float yaw = (float)environmentType.GetMethod("EvaluateChartYawDegrees", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [sphere, config])!;
        Vector4[] sites = (Vector4[])evaluateSites.Invoke(
            null,
            [sphere, new Vector2[] { new(0f, 0f), new(0.4f, 0.4f), new(-0.4f, 0.3f), new(0.2f, -0.5f) }, roundness, yaw])!;
        Vector3 magnet = new(0f, 0f, 100f);

        MethodInfo? withFrame = deformationType
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .FirstOrDefault(method =>
                method.Name == "EvaluateDisplacedPositionWithFrame" && method.GetParameters().Length == 11);
        Assert.That(withFrame, Is.Not.Null, "The displaced-position frame needs the magnetic height scale to mirror the shader.");
        Vector2 uv = new(0.5f, 0.35f);

        // Height 0 is the rigid surface itself; the amplified height only scales the spike on top.
        Vector3 rigidBase = (Vector3)withFrame!.Invoke(
            null,
            [settings, sphere, uv, sites, Vector3.up, 0f, 0f, magnet, 0f, config, Vector3.zero])!;
        Vector3 baseline = (Vector3)withFrame.Invoke(
            null,
            [settings, sphere, uv, sites, Vector3.up, 0.44f, 0f, magnet, 0f, config, Vector3.zero])!;
        Vector3 stretched = (Vector3)withFrame.Invoke(
            null,
            [settings, sphere, uv, sites, Vector3.up, 0.8f, 0f, magnet, 0f, config, Vector3.zero])!;
        Vector3 expectedBase = (Vector3)environmentType
            .GetMethod("EvaluateSurfacePosition", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [sphere, uv, roundness, yaw])!;
        Assert.That(
            Vector3.Distance(rigidBase, expectedBase),
            Is.LessThan(0.000001f),
            "height 0 must return the rigid surface position");
        Vector3 expectedStretched = rigidBase + ((baseline - rigidBase) * (0.8f / 0.44f));
        Assert.That(
            Vector3.Distance(stretched, expectedStretched),
            Is.LessThan(0.000001f),
            "the spike displacement must scale linearly with the magnetic height");
        Assert.That(
            Vector3.Distance(new Vector3(baseline.x, 0f, baseline.z), new Vector3(rigidBase.x, 0f, rigidBase.z)),
            Is.LessThan(0.000001f),
            "an upright spike direction leaves X/Z on the rigid surface");
        Assert.That(
            stretched.y,
            Is.GreaterThan(baseline.y + 0.001f),
            "a larger magnetic height scale grows the spike height");

        // The rigid surface keeps its magnet bulge and per-site axis free of inertia terms.
        MethodInfo siteBulge = deformationType.GetMethod(
            "EvaluateSiteBulgeOffset",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            [settings.GetType(), shapeType, typeof(Vector4), typeof(Vector3), typeof(float), typeof(float)],
            null);
        Assert.That(siteBulge, Is.Not.Null, "The site bulge lookup must resolve for the rigid mirror.");
        Vector4 site = sites[0];
        Vector3 restBulge = (Vector3)siteBulge!.Invoke(null, [settings, sphere, site, magnet, 0f, 1f])!;
        Assert.That(
            restBulge,
            Is.EqualTo(Vector3.zero),
            "magnet strength 0 must produce no bulge at the rigid body shape.");

        MethodInfo siteDirection = deformationType.GetMethod(
            "EvaluateSiteDeformation",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            [
                settings.GetType(),
                shapeType,
                typeof(Vector4),
                typeof(Vector3),
                typeof(float),
                typeof(float).MakeByRefType(),
                typeof(float),
            ],
            null);
        Assert.That(siteDirection, Is.Not.Null, "The site deformation lookup must resolve for the rigid mirror.");
        object[] directionArgs = [settings, sphere, site, magnet, 0f, 0f, 1f];
        Vector4 direction = (Vector4)siteDirection!.Invoke(null, directionArgs)!;
        Vector3 axis = new(direction.x, direction.y, direction.z);
        Assert.That(
            Vector3.Dot(axis, new Vector3(site.x, site.y, site.z).normalized),
            Is.GreaterThan(0.9f),
            "Without a magnet the spike direction stays surface aligned at moved sites.");
    }

    [Test]
    public void MovedSurfaceSiteKeepsMagneticNormal()
    {
        Type deformationType = GetPluginType("ChromaGLS.Settings.FerrofluidPrimitiveDeformation");
        Type configType = GetPluginType("ChromaGLS.Settings.Config");
        Type environmentType = GetPluginType("ChromaGLS.Settings.FerrofluidEnvironmentObjects");
        Type shapeType = GetPluginType("ChromaGLS.Settings.FerrofluidSurfaceShape");
        object config = Activator.CreateInstance(configType, nonPublic: true)!;
        object sphere = Enum.ToObject(shapeType, 2);
        object settings = deformationType
            .GetMethod("EvaluateSettings", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [sphere, config, 1f])!;
        float roundness = (float)environmentType.GetMethod("EvaluateRoundness", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [sphere, config])!;
        float yaw = (float)environmentType.GetMethod("EvaluateChartYawDegrees", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [sphere, config])!;
        Vector4[] sites = (Vector4[])environmentType
            .GetMethod("EvaluatePrimitiveSites", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [sphere, new Vector2[] { new(0f, 0f), new(0.3f, 0.25f), new(-0.35f, 0.1f) }, roundness, yaw])!;

        // Surface-slide inertia moves bases along the sphere; the CPU mirror must still evaluate
        // their magnet bulge and spike direction on the rigid shape at the moved positions.
        MethodInfo siteBulge = deformationType
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .First(method => method.Name == "EvaluateSiteBulgeOffset");
        MethodInfo siteDirection = deformationType
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .First(method => method.Name == "EvaluateSiteDeformation");
        Vector3 magnet = new(0f, 0f, 100f);
        foreach (Vector4 rawSite in sites)
        {
            // A slid base lands somewhere else on the sphere: the CPU mirror must evaluate it at
            // the actual surface radius with the surface-aligned axis.
            Vector3 slidDirection = (new Vector3(rawSite.x, rawSite.y, rawSite.z)
                + new Vector3(0.15f, -0.1f, 0.2f)).normalized;
            Vector4 shiftedSite = new(slidDirection.x * 0.5f, slidDirection.y * 0.5f, slidDirection.z * 0.5f, rawSite.w);
            Assert.That(
                new Vector3(shiftedSite.x, shiftedSite.y, shiftedSite.z).magnitude,
                Is.EqualTo(0.5f).Within(0.000001f),
                "A slid site must stay on the sphere surface.");
            Vector3 bulge = (Vector3)siteBulge.Invoke(
                null,
                [settings, sphere, shiftedSite, magnet, 0f, 1f])!;
            object[] directionArgs = [settings, sphere, shiftedSite, magnet, 0f, 0f, 1f];
            Vector4 direction = (Vector4)siteDirection.Invoke(null, directionArgs)!;
            Vector3 axis = new(direction.x, direction.y, direction.z);
            Assert.That(
                Vector3.Dot(axis, slidDirection),
                Is.GreaterThan(0.9f),
                "A magnet-free slid site keeps its spike axis aligned with the surface normal.");
            Assert.That(bulge.magnitude, Is.LessThan(0.01f), "A magnet-free slid site keeps a near-zero bulge.");
        }
    }

    [Test]
    public void TriggerTrackerDetectsEachHandsOwnRise()
    {
        Type trackerType = GetPluginType("ChromaGLS.Settings.FerrofluidTriggerTracker");
        Assert.That(trackerType, Is.Not.Null, "Both hands need independent previous-trigger samples so a binary pull is not lost on a pointer switch.");
        object tracker = Activator.CreateInstance(trackerType!, nonPublic: true)!;
        MethodInfo update = trackerType.GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.That(update, Is.Not.Null);

        // Seeded with the right hand held at 0.9 while the left rests: no spurious crossing.
        object[] args = [0f, 0.9f, null!, null!];
        update.Invoke(tracker, args);
        Assert.That((bool)args[2]!, Is.False, "A trigger already held at session start must not count as a crossing.");
        Assert.That((bool)args[3]!, Is.False);

        // The pointer switches to the left hand on the same frame its binary trigger jumps to 1.
        args = [1f, 0.9f, null!, null!];
        update.Invoke(tracker, args);
        Assert.That((bool)args[2]!, Is.True, "The left hand's own rise must still register on the switch frame.");
        Assert.That((bool)args[3]!, Is.False);

        // The trigger stays held while the aim moves onto the object: no new crossing.
        args = [1f, 1f, null!, null!];
        update.Invoke(tracker, args);
        Assert.That((bool)args[2]!, Is.False);
        Assert.That((bool)args[3]!, Is.False);

        // Analog ramp above 0.8 on the other hand works while the first stays held.
        args = [1f, 0.1f, null!, null!];
        update.Invoke(tracker, args);
        args = [1f, 0.81f, null!, null!];
        update.Invoke(tracker, args);
        Assert.That((bool)args[3]!, Is.True, "An analog pull past the threshold on the other hand must register.");
    }

    [Test]
    public void EnclosureContainsEveryRotatedBoxCorner()
    {
        Type enclosureType = GetPluginType("ChromaGLS.Settings.FerrofluidGrabEnclosure");
        ConstructorInfo constructor = enclosureType!.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float)],
            null)!;
        object enclosure = constructor.Invoke([new Vector3(0f, 0f, -1f), 3f, new Vector3(0f, 0f, -2f), Vector3.forward, 0f]);
        object box = CreateShape(false, Vector3.one * 0.275f, 0f);
        object sphere = CreateShape(true, Vector3.one * 0.275f, 0.275f);
        MethodInfo clamp = GetGrabMethod("ClampEnclosurePosition");

        // A rotated cube pushed past the wall/rear seam must land with every corner inside both bounds.
        Quaternion tilted = Quaternion.Euler(30f, 45f, 20f);
        Vector3[] probes =
        [
            new(2.9f, 0.4f, -1.8f),
            new(-2.9f, 0.4f, -1.8f),
            new(10f, 0.5f, -10f),
            new(0f, 0.5f, -9f),
        ];
        foreach (Vector3 probe in probes)
        {
            Vector3 clamped = (Vector3)clamp!.Invoke(null, [enclosure, box, probe, tilted])!;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = new(
                    (corner & 1) == 0 ? -0.275f : 0.275f,
                    (corner & 2) == 0 ? -0.275f : 0.275f,
                    (corner & 4) == 0 ? -0.275f : 0.275f);
                Vector3 world = clamped + (tilted * local);
                float radial = Mathf.Sqrt((world.x * world.x) + (world.z + 1f) * (world.z + 1f));
                Assert.That(radial, Is.LessThanOrEqualTo(3f + 0.0005f), $"corner {corner} of the clamped cube escaped the curved wall from {probe}");
                Assert.That(world.z, Is.GreaterThanOrEqualTo(-2f - 0.0005f), $"corner {corner} of the clamped cube escaped the rear plane from {probe}");
                Assert.That(world.y, Is.GreaterThanOrEqualTo(0f - 0.0005f));
            }
        }

        Vector3 sphereClamped = (Vector3)clamp.Invoke(null, [enclosure, sphere, new Vector3(2.6f, 0.5f, -2.4f), Quaternion.identity])!;
        float sphereRadial = Mathf.Sqrt((sphereClamped.x * sphereClamped.x) + ((sphereClamped.z + 1f) * (sphereClamped.z + 1f)));
        Assert.That(sphereRadial + 0.275f, Is.LessThanOrEqualTo(3f + 0.0005f), "the sphere's far edge must stay inside the curved wall");
        Assert.That(sphereClamped.z - 0.275f, Is.GreaterThanOrEqualTo(-2f - 0.0005f), "the sphere's back edge must stay ahead of the rear plane");

        Vector3 high = (Vector3)clamp.Invoke(null, [enclosure, box, new Vector3(0.4f, 1000f, 0.3f), tilted])!;
        Assert.That(high.y, Is.EqualTo(1000f).Within(0.0001f), "There is no ceiling on the enclosure.");
    }

    [Test]
    public void EnclosureVelocityDeltaDrivesLagOutwardOnce()
    {
        object enclosure = CreateEnclosure();
        object sphere = CreateShape(true, Vector3.one * 0.275f, 0.275f);
        MethodInfo enforce = GetGrabMethod("EnforceEnclosure");
        Type springType = GetPluginType("ChromaGLS.Settings.FluidSpringState");
        MethodInfo impulse = GetGrabMethod("ApplyFluidImpulse");

        object[] thrown =
        [
            enclosure,
            sphere,
            new Vector3(1.9f, 0.4f, 0f),
            Quaternion.identity,
            new Vector3(10f, 0f, 0f),
            Vector3.zero,
            Vector3.zero,
            0.02f,
            0.1f,
            null!,
            null!,
        ];
        enforce!.Invoke(null, thrown);
        Vector3 newVelocity = (Vector3)thrown[10]!;
        Vector3 delta = newVelocity - new Vector3(10f, 0f, 0f);
        object state = impulse!.Invoke(null, [Activator.CreateInstance(springType!)!, delta])!;
        Assert.That(
            ((Vector3)springType.GetField("Velocity", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!).x,
            Is.GreaterThan(0.5f),
            "A wall reflection must kick the fluid lag in the outward direction the wall pushed back from.");
    }

    [TestCase(0f)]
    [TestCase(60f)]
    public void ExtendedEnclosureLeavesBodiesFreeAlongTheRearStretch(float yawDegrees)
    {
        Quaternion yaw = Quaternion.Euler(0f, yawDegrees, 0f);
        object enclosure = CreateExtendedEnclosure(yaw);
        object sphere = CreateShape(true, Vector3.one * 0.275f, 0.275f);
        object box = CreateShape(false, Vector3.one * 0.275f, 0f);
        MethodInfo clamp = GetGrabMethod("ClampEnclosureWallPosition");
        Assert.That(clamp, Is.Not.Null, "Held bodies must clamp against the extruded rear region.");

        Vector3 rearward = new(0f, 1f, -18f);
        Assert.That(Clamp(clamp!, enclosure, sphere, yaw * rearward, Quaternion.identity), Is.EqualTo(yaw * rearward), "a sphere well behind the circle must stay put inside the stretch");
        Assert.That(Clamp(clamp, enclosure, box, yaw * rearward, yaw * Quaternion.Euler(0f, 60f, 0f)), Is.EqualTo(yaw * rearward), "a rotated box deep in the stretch must stay put");

        Vector3 nearCap = new(0f, 1f, -23f);
        Assert.That(Clamp(clamp, enclosure, sphere, yaw * nearCap, Quaternion.identity), Is.EqualTo(yaw * nearCap), "the stretch must reach the rear plane");
    }

    [TestCase(0f)]
    [TestCase(60f)]
    public void ExtendedEnclosureStillClampsRearPlaneSidesAndFront(float yawDegrees)
    {
        Quaternion yaw = Quaternion.Euler(0f, yawDegrees, 0f);
        Quaternion inverse = Quaternion.Inverse(yaw);
        object enclosure = CreateExtendedEnclosure(yaw);
        object sphere = CreateShape(true, Vector3.one * 0.275f, 0.275f);
        MethodInfo clamp = GetGrabMethod("ClampEnclosureWallPosition");

        Vector3 beyond = inverse * Clamp(clamp!, enclosure, sphere, yaw * new Vector3(0f, 1f, -26f), Quaternion.identity);
        Assert.That(beyond.z, Is.GreaterThanOrEqualTo(-24f + 0.275f - 0.0001f), "the rear plane must still stop the full body");

        Vector3 sideDeep = inverse * Clamp(clamp, enclosure, sphere, yaw * new Vector3(4f, 1f, -18f), Quaternion.identity);
        Assert.That(sideDeep.x, Is.LessThanOrEqualTo(3f - 0.275f + 0.0001f), "the extruded sides must still cap the lateral radius at the rear");
        Assert.That(Mathf.Abs(sideDeep.z + 18f), Is.LessThan(0.0001f), "a side clamp must not drag the body forward");

        Vector3 front = inverse * Clamp(clamp, enclosure, sphere, yaw * new Vector3(0f, 1f, 3f), Quaternion.identity);
        Assert.That(
            Mathf.Sqrt((front.x * front.x) + ((front.z + 1f) * (front.z + 1f))),
            Is.LessThanOrEqualTo(3f - 0.275f + 0.0001f),
            "the front must keep the original circle boundary");

        Quaternion tilt = Quaternion.Euler(0f, 60f, 0f);
        object box = CreateShape(false, Vector3.one * 0.275f, 0f);
        Vector3 boxed = inverse * Clamp(clamp, enclosure, box, yaw * new Vector3(2.5f, 1f, -18f), yaw * tilt);
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 local = new(
                (corner & 1) == 0 ? -0.275f : 0.275f,
                (corner & 2) == 0 ? -0.275f : 0.275f,
                (corner & 4) == 0 ? -0.275f : 0.275f);
            Vector3 world = boxed + (tilt * local);
            Assert.That(Mathf.Abs(world.x), Is.LessThanOrEqualTo(3f + 0.0001f), $"corner {corner} escaped the extruded side wall");
            Assert.That(world.z, Is.GreaterThanOrEqualTo(-24f - 0.0001f), $"corner {corner} escaped the rear plane");
        }
    }

    [TestCase(0f)]
    [TestCase(60f)]
    public void ExtendedEnclosureEnforceLetsFreeBodiesCrossTheStretchUnchanged(float yawDegrees)
    {
        Quaternion yaw = Quaternion.Euler(0f, yawDegrees, 0f);
        object enclosure = CreateExtendedEnclosure(yaw);
        MethodInfo enforce = GetGrabMethod("EnforceEnclosure");

        foreach (bool useBox in new[] { false, true })
        {
            object shape = useBox
                ? CreateShape(false, Vector3.one * 0.275f, 0f)
                : CreateShape(true, Vector3.one * 0.275f, 0.275f);
            Quaternion rotation = useBox ? yaw * Quaternion.Euler(0f, 30f, 0f) : Quaternion.identity;
            Vector3 position = yaw * new Vector3(0f, 1f, -18f);
            Vector3 velocity = yaw * new Vector3(1f, 0.5f, -5f);
            object[] args = [enclosure, shape, position, rotation, velocity, Vector3.zero, Vector3.zero, 0.02f, 0.2f, null!, null!];
            enforce!.Invoke(null, args);
            Assert.That(
                Vector3.Distance((Vector3)args[9]!, position),
                Is.LessThan(0.0001f),
                $"a free {(useBox ? "box" : "sphere")} inside the stretch must keep its pose");
            Assert.That(
                Vector3.Distance((Vector3)args[10]!, velocity),
                Is.LessThan(0.0001f),
                $"a free {(useBox ? "box" : "sphere")} inside the stretch must keep its motion");
        }
    }

    [TestCase(0f)]
    [TestCase(60f)]
    public void ExtendedEnclosureEnforceReflectsRearPlaneApproach(float yawDegrees)
    {
        Quaternion yaw = Quaternion.Euler(0f, yawDegrees, 0f);
        Quaternion inverse = Quaternion.Inverse(yaw);
        object enclosure = CreateExtendedEnclosure(yaw);
        MethodInfo enforce = GetGrabMethod("EnforceEnclosure");
        object sphere = CreateShape(true, Vector3.one * 0.275f, 0.275f);

        Vector3 position = yaw * new Vector3(0f, 1f, -23.72f);
        Vector3 velocity = yaw * new Vector3(1.5f, 0.5f, -3f);
        object[] args = [enclosure, sphere, position, Quaternion.identity, velocity, Vector3.zero, Vector3.zero, 0.02f, 0.2f, null!, null!];
        enforce!.Invoke(null, args);
        Vector3 constrained = inverse * (Vector3)args[9]!;
        Vector3 reflected = inverse * (Vector3)args[10]!;
        Assert.That(
            constrained.z + (reflected.z * 0.02f) - 0.275f,
            Is.GreaterThanOrEqualTo(-24f - 0.0001f),
            "the predicted rear-plane contact must keep the full sphere extent inside");
        Assert.That(
            reflected.z,
            Is.EqualTo(0.6f).Within(0.001f),
            "the outgoing rear velocity must reflect at the configured restitution");
        Assert.That(
            reflected.x,
            Is.EqualTo(1.5f).Within(0.0001f),
            "the lateral velocity must not be eaten by the rear contact");
        Assert.That(
            reflected.y,
            Is.EqualTo(0.5f).Within(0.0001f),
            "the vertical velocity must not be eaten by the rear contact");
    }

    [TestCase(0f)]
    [TestCase(60f)]
    public void ProductionEnclosureRearCapTerminatesAtRequestedDepth(float yawDegrees)
    {
        Quaternion yaw = Quaternion.Euler(0f, yawDegrees, 0f);
        Vector3 forward = yaw * Vector3.forward;
        Vector3 playerPosition = yaw * new Vector3(1.5f, 0f, 0.5f);
        Vector3 center = playerPosition + (forward * 0.15f) + new Vector3(0f, 1.28f, 0f);
        object enclosure = CreateProductionEnclosure(center, 4.2f, playerPosition, forward);

        float extension = GetEnclosureField(enclosure, "RearExtension");
        Vector3 rearPoint = (Vector3)GetEnclosureObjectField(enclosure, "RearPoint")!;
        Vector3 capCenter = center - (forward * extension);
        Assert.That(
            Vector3.Dot(playerPosition - rearPoint, forward),
            Is.EqualTo(5f).Within(0.0001f),
            "the rear cap pole must sit 5m behind the player");
        Assert.That(
            extension,
            Is.EqualTo(0.95f).Within(0.0001f),
            "only the sliver of circle protruding past the tangent plane needs extrusion");
        Assert.That(
            Vector3.Distance(rearPoint + (forward * 4.2f), capCenter),
            Is.LessThan(0.0001f),
            "the plane must be tangent to a cap of the corridor radius, not a blade through the diameter");
    }

    [TestCase(0f, 0.5f)]
    [TestCase(0f, 0.8f)]
    [TestCase(60f, 0.5f)]
    [TestCase(60f, 0.8f)]
    public void ProductionEnclosureRearFollowsCapArcNotCorridor(float yawDegrees, float depthFraction)
    {
        Quaternion yaw = Quaternion.Euler(0f, yawDegrees, 0f);
        Vector3 forward = yaw * Vector3.forward;
        Vector3 right = yaw * Vector3.right;
        Vector3 center = new(0f, 1.28f, 0.15f);
        float radius = 4.2f;
        object enclosure = CreateProductionEnclosure(center, radius, Vector3.zero, forward);
        object sphere = CreateShape(true, Vector3.one * 0.275f, 0.275f);
        MethodInfo clamp = GetGrabMethod("ClampEnclosureWallPosition");

        float extension = GetEnclosureField(enclosure, "RearExtension");
        Vector3 capCenter = center - (forward * extension);
        float depth = depthFraction * radius;
        float innerHalfWidth = Mathf.Sqrt(
            ((radius - 0.275f - 0.01f) * (radius - 0.275f - 0.01f)) - (depth * depth));
        float outerHalfWidth = Mathf.Sqrt(
            ((radius - 0.275f + 0.1f) * (radius - 0.275f + 0.1f)) - (depth * depth));
        Vector3 axis = capCenter - (forward * depth) + new Vector3(0f, 1f, 0f);

        Vector3 inside = axis + (right * innerHalfWidth);
        Assert.That(
            Clamp(clamp!, enclosure, sphere, inside, Quaternion.identity),
            Is.EqualTo(inside).Within(Vector3.one * 0.0001f),
            $"a sphere inside the rear arc at {depthFraction} radius depth must stay put");

        Vector3 outside = axis + (right * outerHalfWidth);
        Vector3 clamped = Clamp(clamp, enclosure, sphere, outside, Quaternion.identity);
        Vector3 flat = clamped - capCenter;
        flat.y = 0f;
        Assert.That(
            flat.magnitude,
            Is.LessThanOrEqualTo(radius - 0.275f + 0.0001f),
            $"a sphere past the rear arc at {depthFraction} radius depth must clamp onto the round cap, not a corridor wall");

        Vector3 insideLeft = axis - (right * innerHalfWidth);
        Assert.That(
            Clamp(clamp, enclosure, sphere, insideLeft, Quaternion.identity),
            Is.EqualTo(insideLeft).Within(Vector3.one * 0.0001f),
            "the rear cap must be symmetric on both sides");
    }

    [TestCase(0f)]
    [TestCase(60f)]
    public void ProductionEnclosureKeepsBroadMidRearWidth(float yawDegrees)
    {
        Quaternion yaw = Quaternion.Euler(0f, yawDegrees, 0f);
        Vector3 forward = yaw * Vector3.forward;
        Vector3 right = yaw * Vector3.right;
        Vector3 center = new(0f, 1.28f, 0.15f);
        object enclosure = CreateProductionEnclosure(center, 4.2f, Vector3.zero, forward);
        object sphere = CreateShape(true, Vector3.one * 0.275f, 0.275f);
        MethodInfo clamp = GetGrabMethod("ClampEnclosureWallPosition");

        float extension = GetEnclosureField(enclosure, "RearExtension");
        Vector3 probe = center - (forward * (extension + 2f)) + (right * 3f) + new Vector3(0f, 1f, 0f);
        Assert.That(
            Clamp(clamp!, enclosure, sphere, probe, Quaternion.identity),
            Is.EqualTo(probe).Within(Vector3.one * 0.0001f),
            "3m off axis 2m behind the circle center must remain free inside the broad dome");
    }

    [Test]
    public void ProductionEnclosureSmallRadiusExtendsToRearCap()
    {
        Vector3 forward = Vector3.forward;
        Vector3 playerPosition = new(0.4f, 0f, -0.3f);
        Vector3 center = playerPosition + (forward * 0.5f) + new Vector3(0f, 1.28f, 0f);
        object enclosure = CreateProductionEnclosure(center, 1f, playerPosition, forward);

        float extension = GetEnclosureField(enclosure, "RearExtension");
        Vector3 rearPoint = (Vector3)GetEnclosureObjectField(enclosure, "RearPoint")!;
        Assert.That(extension, Is.EqualTo(4.5f).Within(0.0001f), "a small circle still extrudes its cap to the tangent plane");
        Assert.That(
            Vector3.Dot(playerPosition - rearPoint, forward),
            Is.EqualTo(5f).Within(0.0001f),
            "the cap pole must reach the requested depth");
        Assert.That(
            Vector3.Distance(rearPoint + (forward * 1f), center - (forward * extension)),
            Is.LessThan(0.0001f),
            "the plane must stay tangent to the radius-sized rear cap");
    }

    [Test]
    public void ProductionEnclosureLargeRadiusKeepsNaturalRearCap()
    {
        Vector3 forward = Vector3.forward;
        Vector3 playerPosition = new(0.4f, 0f, -0.3f);
        Vector3 center = playerPosition + (forward * 0.5f) + new Vector3(0f, 1.28f, 0f);
        object enclosure = CreateProductionEnclosure(center, 8f, playerPosition, forward);
        object sphere = CreateShape(true, Vector3.one * 0.275f, 0.275f);
        MethodInfo clamp = GetGrabMethod("ClampEnclosureWallPosition");

        float extension = GetEnclosureField(enclosure, "RearExtension");
        Vector3 rearPoint = (Vector3)GetEnclosureObjectField(enclosure, "RearPoint")!;
        Assert.That(extension, Is.EqualTo(0f).Within(0.0001f), "a circle already reaching the depth keeps its own cap");
        Assert.That(
            Vector3.Distance(rearPoint, center - (forward * 8f)),
            Is.LessThan(0.0001f),
            "the plane must stay tangent to the natural rear cap rather than shrink the circle");

        Vector3 front = center + (forward * (8f - 0.275f - 0.01f)) + new Vector3(0f, 1f, 0f);
        Assert.That(
            Clamp(clamp!, enclosure, sphere, front, Quaternion.identity),
            Is.EqualTo(front).Within(Vector3.one * 0.0001f),
            "the front circle must be unchanged");

        float depth = 0.8f * 8f;
        float innerHalfWidth = Mathf.Sqrt(
            ((8f - 0.275f - 0.01f) * (8f - 0.275f - 0.01f)) - (depth * depth));
        Vector3 inside = center - (forward * depth) + (Vector3.right * innerHalfWidth) + new Vector3(0f, 1f, 0f);
        Assert.That(
            Clamp(clamp, enclosure, sphere, inside, Quaternion.identity),
            Is.EqualTo(inside).Within(Vector3.one * 0.0001f),
            "the natural cap must keep the full arc width");
    }

    [TestCase(0f)]
    [TestCase(60f)]
    public void ProductionEnclosureEnforceReflectsCurvedRearApproach(float yawDegrees)
    {
        Quaternion yaw = Quaternion.Euler(0f, yawDegrees, 0f);
        Vector3 forward = yaw * Vector3.forward;
        Vector3 right = yaw * Vector3.right;
        Vector3 center = new(0f, 1.28f, 0.15f);
        object enclosure = CreateProductionEnclosure(center, 4.2f, Vector3.zero, forward);
        MethodInfo enforce = GetGrabMethod("EnforceEnclosure");
        object sphere = CreateShape(true, Vector3.one * 0.275f, 0.275f);

        float extension = GetEnclosureField(enclosure, "RearExtension");
        Vector3 capCenter = center - (forward * extension);
        Vector3 radial = (right * 2f) - (forward * 1.4f);
        radial.y = 0f;
        radial.Normalize();
        Vector3 position = capCenter + (radial * (4.2f - 0.275f - 0.01f)) + new Vector3(0f, 1f, 0f);
        Vector3 velocity = (radial * 3f) + new Vector3(0f, 0.5f, 0f);
        object[] args = [enclosure, sphere, position, Quaternion.identity, velocity, Vector3.zero, Vector3.zero, 0.02f, 0.2f, null!, null!];
        enforce!.Invoke(null, args);
        Vector3 constrained = (Vector3)args[9]!;
        Vector3 reflected = (Vector3)args[10]!;

        Vector3 flat = constrained + (reflected * 0.02f) - capCenter;
        flat.y = 0f;
        Assert.That(
            flat.magnitude,
            Is.LessThanOrEqualTo(4.2f - 0.275f + 0.0001f),
            "a sphere crossing the curved rear cap must predict inside the arc");
        Assert.That(
            Vector3.Dot(reflected, radial),
            Is.EqualTo(-0.6f).Within(0.001f),
            "the outward radial component must reflect at the configured restitution about the cap normal");
        Assert.That(
            reflected.y,
            Is.EqualTo(0.5f).Within(0.0001f),
            "the vertical velocity must survive the curved rear contact");
    }

    private static object CreateExtendedEnclosure(Quaternion yaw)
    {
        Type enclosureType = GetPluginType("ChromaGLS.Settings.FerrofluidGrabEnclosure");
        Assert.That(enclosureType, Is.Not.Null);
        ConstructorInfo? constructor = enclosureType!.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float), typeof(float)],
            null);
        Assert.That(constructor, Is.Not.Null, "The enclosure needs a six-argument constructor carrying the rear extrusion.");
        return constructor!.Invoke(
            [yaw * new Vector3(0f, 0f, -1f), 3f, yaw * new Vector3(0f, 0f, -24f), yaw * Vector3.forward, 0f, 23f]);
    }

    private static object CreateEnclosure()
    {
        Type enclosureType = GetPluginType("ChromaGLS.Settings.FerrofluidGrabEnclosure");
        Assert.That(enclosureType, Is.Not.Null, "The menu wall enclosure must carry the curve circle, rear plane, and floor.");
        ConstructorInfo? constructor = enclosureType!.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector3), typeof(float)],
            null);
        Assert.That(constructor, Is.Not.Null, "The enclosure needs a direct constructor for deterministic tests.");
        return constructor!.Invoke([Vector3.zero, 2f, new Vector3(0f, 0f, -2f), Vector3.forward, 0f]);
    }

    private static object CreateShape(bool isSphere, Vector3 halfExtents, float radius)
    {
        Type shapeType = GetPluginType("ChromaGLS.Settings.FerrofluidGrabShape");
        Assert.That(shapeType, Is.Not.Null, "Bounds need a shape description carrying sphere radius or box half extents.");
        ConstructorInfo? constructor = shapeType!.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [typeof(bool), typeof(Vector3), typeof(float)],
            null);
        Assert.That(constructor, Is.Not.Null);
        return constructor!.Invoke([isSphere, halfExtents, radius]);
    }

    private static Vector3 Clamp(MethodInfo clamp, object enclosure, object shape, Vector3 position, Quaternion rotation)
    {
        return (Vector3)clamp.Invoke(null, [enclosure, shape, position, rotation])!;
    }

    private static (Vector3 velocity, Vector3 spin) WallFriction(Vector3 incoming, Vector3 reflected, Vector3 angular, float radius, float inertiaPerMass, float friction)
    {
        object[] args = [incoming, reflected, angular, radius, inertiaPerMass, friction, Vector3.zero, Vector3.zero];
        GetGrabMethod("ApplySphereWallFriction")!.Invoke(null, args);
        return ((Vector3)args[6]!, (Vector3)args[7]!);
    }

    [Test]
    public void SphereWallFrictionKicksUpAndConsumesRoll()
    {
        float radius = 0.275f;
        float inertiaPerMass = 0.4f * radius * radius;
        (Vector3 velocity, Vector3 spin) = WallFriction(
            new Vector3(0f, 0f, 2f),
            new Vector3(0f, 0f, -0.72f),
            new Vector3(2f / radius, 0f, 0f),
            radius,
            inertiaPerMass,
            0.6f);
        Assert.That(velocity.y, Is.EqualTo(0.5714f).Within(0.001f), "the contact bite must kick up by the shared friction slip");
        Assert.That(velocity.z, Is.EqualTo(-0.72f).Within(0.001f), "the normal restitution stays untouched");
        Assert.That(spin.x, Is.EqualTo(2.0779f).Within(0.001f), "the roll must be consumed by the wall contact");
    }

    [Test]
    public void SphereWallFrictionInvariantsBoundImpulseAndEnergy()
    {
        foreach (float radius in new[] { 0.15f, 0.275f })
        {
            foreach (float yawDegrees in new[] { 0f, 60f })
            {
                Quaternion yaw = Quaternion.Euler(0f, yawDegrees, 0f);
                Vector3 outward = yaw * Vector3.forward;
                Vector3 incoming = outward * 2f;
                Vector3 reflected = outward * -0.72f;
                Vector3 angular = new Vector3(2f / radius, 0f, 0f);
                float inertiaPerMass = 0.4f * radius * radius;
                (Vector3 vOut, Vector3 wOut) = WallFriction(incoming, reflected, angular, radius, inertiaPerMass, 0.6f);
                float energyIn = (0.5f * incoming.sqrMagnitude) + (0.5f * inertiaPerMass * angular.sqrMagnitude);
                float energyOut = (0.5f * vOut.sqrMagnitude) + (0.5f * inertiaPerMass * wOut.sqrMagnitude);
                Assert.That(energyOut, Is.LessThanOrEqualTo(energyIn + 0.00001f), "the impulse must never add energy");
                Vector3 delta = vOut - reflected;
                float budget = 0.6f * (reflected - incoming).magnitude;
                Assert.That(delta.magnitude, Is.LessThanOrEqualTo(budget + 0.00001f), "the Coulomb cap must bound the tangential delta");
            }
        }

        (Vector3 noSlip, Vector3 noSlipW) = WallFriction(
            Vector3.zero, Vector3.zero, Vector3.zero, 0.275f, 0.4f * 0.275f * 0.275f, 0.6f);
        Assert.That(noSlip == Vector3.zero && noSlipW == Vector3.zero, Is.True, "no contact slip leaves everything untouched");
    }

    [Test]
    public void SphereWallFrictionSkipsNoImpactAndReverseSpin()
    {
        float radius = 0.275f;
        float inertiaPerMass = 0.4f * radius * radius;
        (Vector3 v1, Vector3 w1) = WallFriction(
            new Vector3(0f, 0f, 2f), new Vector3(0f, 0f, 2f), new Vector3(7f, 0f, 0f), radius, inertiaPerMass, 0.6f);
        Assert.That(v1 == new Vector3(0f, 0f, 2f) && w1 == new Vector3(7f, 0f, 0f), Is.True, "no normal delta means no wall contact impulse");
        (Vector3 v2, Vector3 w2) = WallFriction(
            new Vector3(0f, 0f, 2f), new Vector3(0f, 0f, -0.72f), new Vector3(-2f / radius, 0f, 0f), radius, inertiaPerMass, 0.6f);
        Assert.That(v2.y, Is.LessThan(0f), "reverse spin must pull the sphere down into the wall instead");
        (Vector3 v3, Vector3 w3) = WallFriction(
            new Vector3(0f, 0f, 2f), new Vector3(0f, 0f, -0.72f), new Vector3(2f / radius, 0f, 0f), radius, inertiaPerMass, 0f);
        Assert.That(v3 == new Vector3(0f, 0f, -0.72f) && w3 == new Vector3(2f / radius, 0f, 0f), Is.True, "zero friction leaves velocity and spin untouched");
    }


    private static object CreateProductionEnclosure(Vector3 center, float radius, Vector3 playerPosition, Vector3 forward)
    {
        MethodInfo create = GetGrabMethod("CreateEnclosure");
        return create.Invoke(null, [center, radius, playerPosition, forward, 0f])!;
    }

    private static float GetEnclosureField(object enclosure, string name)
    {
        return (float)GetEnclosureObjectField(enclosure, name)!;
    }

    private static object? GetEnclosureObjectField(object enclosure, string name)
    {
        return enclosure.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(enclosure);
    }

    private static MethodInfo GetGrabMethod(string name)
    {
        return GetPluginType("ChromaGLS.Settings.FerrofluidGrabPhysics")
            .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new AssertionException($"Missing grab method: {name}");
    }

    private static Type GetPluginType(string typeName)
    {
        return Assembly.Load("ChromaGLS").GetType(typeName, throwOnError: false)
            ?? throw new AssertionException($"Missing plugin type: {typeName}");
    }
}
