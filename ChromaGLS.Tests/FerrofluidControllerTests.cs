using System;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class FerrofluidControllerTests
{
    [Test]
    public void ControllerDeadzoneRemapsRadialDeflectionAndCapsMagnitude()
    {
        MethodInfo deadzone = GetPhysicsMethod("ApplyControllerDeadzone");
        Assert.That(deadzone, Is.Not.Null, "Stick input needs a shared radial deadzone before any force or spin math.");

        Assert.That((Vector2)deadzone!.Invoke(null, [new Vector2(0.05f, 0f)])!, Is.EqualTo(Vector2.zero), "Deflection inside the deadzone must produce no input.");
        Assert.That((Vector2)deadzone.Invoke(null, [new Vector2(0.1f, 0f)])!, Is.EqualTo(Vector2.zero), "Exactly the deadzone radius must still produce no input.");
        Assert.That(((Vector2)deadzone.Invoke(null, [new Vector2(0.55f, 0f)])!).x, Is.EqualTo(0.5f).Within(0.0001f), "A 0.55 deflection must remap linearly to 0.5.");
        Assert.That(((Vector2)deadzone.Invoke(null, [new Vector2(1f, 0f)])!).x, Is.EqualTo(1f).Within(0.0001f), "Full deflection must stay at 1.");

        Vector2 diagonal = (Vector2)deadzone.Invoke(null, [new Vector2(0.8f, 0.8f)])!;
        Assert.That(diagonal.magnitude, Is.EqualTo(1f).Within(0.0001f), "A diagonal stick must cap at magnitude 1 instead of gaining a root-2 boost.");
        Assert.That(diagonal.x, Is.EqualTo(diagonal.y).Within(0.0001f), "The remapped vector must keep the stick's direction.");

        Vector2 negative = (Vector2)deadzone.Invoke(null, [new Vector2(-0.3f, -0.4f)])!;
        Assert.That(negative.magnitude, Is.EqualTo((0.5f - 0.1f) / 0.9f).Within(0.0001f), "Remap applies to magnitude, not each axis.");
        Assert.That(negative.x, Is.LessThan(0f));
        Assert.That(negative.y, Is.LessThan(0f));
    }

    [Test]
    public void ControllerKeyVectorCancelsOppositesAndCapsDiagonal()
    {
        Type physics = GetPluginType("ChromaGLS.Settings.FerrofluidGrabPhysics");
        MethodInfo? keys = physics.GetMethod("EvaluateControllerKeyVector", BindingFlags.Static | BindingFlags.NonPublic);
        bool tweaks = (bool)GetPluginType("ChromaGLS.Settings.ChromaGLSSettingsUI")
            .GetField("FerrofluidTweakMenuEnabled", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetRawConstantValue()!;
        Assert.That(
            keys != null,
            Is.EqualTo(tweaks),
            "The keyboard key-vector helper must exist exactly in FERROFLUID_TWEAKS builds.");
        if (keys == null)
            return;

        MethodInfo deadzone = GetPhysicsMethod("ApplyControllerDeadzone");
        Assert.That(deadzone, Is.Not.Null);

        Assert.That((Vector2)keys!.Invoke(null, [true, false, true, false])!, Is.EqualTo(Vector2.zero), "Opposite up/down keys must cancel.");
        Assert.That((Vector2)keys.Invoke(null, [false, true, false, true])!, Is.EqualTo(Vector2.zero), "Opposite left/right keys must cancel.");
        Assert.That((Vector2)keys.Invoke(null, [true, false, false, false])!, Is.EqualTo(new Vector2(0f, 1f)));
        Assert.That((Vector2)keys.Invoke(null, [false, true, false, false])!, Is.EqualTo(new Vector2(-1f, 0f)));
        Assert.That((Vector2)keys.Invoke(null, [false, false, true, false])!, Is.EqualTo(new Vector2(0f, -1f)));
        Assert.That((Vector2)keys.Invoke(null, [false, false, false, true])!, Is.EqualTo(new Vector2(1f, 0f)));

        Vector2 diagonal = (Vector2)deadzone!.Invoke(null, [(Vector2)keys.Invoke(null, [true, false, false, true])!])!;
        Assert.That(diagonal.magnitude, Is.EqualTo(1f).Within(0.0001f), "Diagonal keys must cap through the same magnitude limit as the stick.");
    }

    [Test]
    public void ControllerRoutingUsesGrabOwnerThenHoverSelection()
    {
        MethodInfo route = GetPhysicsMethod("RouteControllerInput");
        Assert.That(route, Is.Not.Null, "Each body must route the dominant stick from its owner, or the selected pointer when unheld and hovered.");

        object leftHand = new();
        object rightHand = new();
        object ownership = CreateOwnership();

        object[] held = [ownership, leftHand, rightHand, false, leftHand, rightHand, null!, null!];
        route!.Invoke(null, held);
        Assert.That(held[6], Is.SameAs(leftHand), "The holding hand is dominant for its body regardless of pointer selection.");
        Assert.That(held[7], Is.SameAs(rightHand), "The other hand supplies the spin role.");

        object[] swapped = [ownership, rightHand, leftHand, true, leftHand, rightHand, null!, null!];
        route.Invoke(null, swapped);
        Assert.That(swapped[6], Is.SameAs(rightHand));
        Assert.That(swapped[7], Is.SameAs(leftHand));

        object[] hovered = [ownership, null!, rightHand, true, leftHand, rightHand, null!, null!];
        route.Invoke(null, hovered);
        Assert.That(hovered[6], Is.SameAs(rightHand));
        Assert.That(hovered[7], Is.SameAs(leftHand));

        object[] unrelated = [ownership, null!, rightHand, false, leftHand, rightHand, null!, null!];
        route.Invoke(null, unrelated);
        Assert.That(unrelated[6], Is.Null, "A body with no owner and no hover must not receive stick input.");
        Assert.That(unrelated[7], Is.Null);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ControllerRoutingBlocksSpinInputFromABusyHand(bool leftDominant)
    {
        MethodInfo route = GetPhysicsMethod("RouteControllerInput");
        Assert.That(route, Is.Not.Null, "Each body must route the dominant stick from its owner, or the selected pointer when unheld and hovered.");
        Type ownershipType = GetPluginType("ChromaGLS.Settings.FerrofluidGrabOwnership");
        object leftHand = new();
        object rightHand = new();
        object cubeTarget = new();
        object sphereTarget = new();
        object ownership = CreateOwnership();
        MethodInfo acquire = ownershipType
            .GetMethod("TryAcquire", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object dominantHand = leftDominant ? leftHand : rightHand;
        object otherHand = leftDominant ? rightHand : leftHand;
        Assert.That(acquire.Invoke(ownership, [dominantHand, cubeTarget]), Is.True);
        Assert.That(acquire.Invoke(ownership, [otherHand, sphereTarget]), Is.True);

        // The dominant hand holds the cube; the other already holds the sphere, so no hand is free for spin.
        object[] bothHeld = [ownership, dominantHand, leftHand, false, leftHand, rightHand, null!, null!];
        route!.Invoke(null, bothHeld);
        Assert.That(bothHeld[6], Is.SameAs(dominantHand), "The owning hand stays dominant for its body.");
        Assert.That(bothHeld[7], Is.Null, "A hand already holding a target must not also supply the spin role.");

        // A hovered free body must not borrow spin from the busy hand either.
        object[] hovered = [ownership, null!, dominantHand, true, leftHand, rightHand, null!, null!];
        route.Invoke(null, hovered);
        Assert.That(hovered[7], Is.Null, "A hovered body must not borrow spin input from a busy hand.");

        // Releasing the sphere frees the other hand for spin again.
        ownershipType
            .GetMethod("ReleaseTarget", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ownership, [sphereTarget]);
        object[] freed = [ownership, dominantHand, leftHand, false, leftHand, rightHand, null!, null!];
        route.Invoke(null, freed);
        Assert.That(freed[7], Is.SameAs(otherHand), "Releasing the other grip restores the spin role.");
    }

    [TestCase(true)]
    [TestCase(false)]
    public void SpinAccelerationFollowsPlayerRightAndShapeRadius(bool sphereShape)
    {
        MethodInfo spinRadius = GetPhysicsMethod("EvaluateSpinRadius");
        MethodInfo angular = GetPhysicsMethod("EvaluateSpinAngularAcceleration");
        Assert.That(spinRadius, Is.Not.Null, "The center-spin cap needs the body's approximate radius once from its grab shape.");
        Assert.That(angular, Is.Not.Null, "Spin must convert the non-dominant stick into a world angular acceleration.");

        Vector3 boxHalfExtents = new(0.3f, 0.2f, 0.1f);
        object sphere = CreateShape(true, Vector3.one * 0.275f, 0.275f);
        object box = CreateShape(false, boxHalfExtents, 0f);
        Assert.That((float)spinRadius!.Invoke(null, [sphere])!, Is.EqualTo(0.275f).Within(0.000001f));
        Assert.That((float)spinRadius.Invoke(null, [box])!, Is.EqualTo(boxHalfExtents.magnitude).Within(0.0001f), "A box uses the furthest-corner approximation.");

        float radius = sphereShape ? 0.275f : boxHalfExtents.magnitude;
        float cap = 12f / radius;
        Vector3 up = (Vector3)angular!.Invoke(null, [Vector3.right, new Vector2(0f, 1f), radius])!;
        Assert.That(Vector3.Distance(up, new Vector3(cap, 0f, 0f)), Is.LessThan(0.001f), "Stick up spins about player-right.");
        Vector3 right = (Vector3)angular.Invoke(null, [Vector3.right, new Vector2(1f, 0f), radius])!;
        Assert.That(Vector3.Distance(right, new Vector3(0f, -cap / 3f, 0f)), Is.LessThan(0.001f), "Stick right spins the front face toward player-right at a third of the stick-up rate.");
        Vector3 left = (Vector3)angular.Invoke(null, [Vector3.right, new Vector2(-1f, 0f), radius])!;
        Assert.That(Vector3.Distance(left, new Vector3(0f, cap / 3f, 0f)), Is.LessThan(0.001f), "Stick left mirrors the reduced stick-right rate.");
        Vector3 half = (Vector3)angular.Invoke(null, [Vector3.right, new Vector2(0.5f, 0f), radius])!;
        Assert.That(Vector3.Distance(half, new Vector3(0f, -cap / 6f, 0f)), Is.LessThan(0.001f), "The horizontal reduction scales with deflection.");

        Vector3 yawed = (Vector3)angular.Invoke(null, [new Vector3(0f, 0f, -1f), new Vector2(0f, 1f), radius])!;
        Assert.That(Vector3.Distance(yawed, new Vector3(0f, 0f, -cap)), Is.LessThan(0.001f), "The spin axis must follow the player's yaw, not the world axes.");

        Vector2 diagonalStick = new Vector2(1f, 1f).normalized;
        Vector3 diagonal = (Vector3)angular.Invoke(null, [Vector3.right, diagonalStick, radius])!;
        Assert.That(diagonal.x, Is.EqualTo(cap * diagonalStick.y).Within(0.001f), "A combined deflection keeps the full stick-up component.");
        Assert.That(diagonal.y, Is.EqualTo(-cap * diagonalStick.x / 3f).Within(0.001f), "A combined deflection keeps the reduced stick-right component.");
        Assert.That(diagonal.z, Is.EqualTo(0f).Within(0.001f), "The spin axis has no forward component.");
    }

    [Test]
    public void HeldForceFollowsTangentsAroundThePivot()
    {
        MethodInfo force = GetPhysicsMethod("EvaluateHeldForceAcceleration");
        Assert.That(force, Is.Not.Null, "Held stick force must orbit the body tangentially around the grab pivot.");

        Vector3 below = (Vector3)force!.Invoke(null, [new Vector3(0f, -1f, 0f), Vector3.right, new Vector2(0f, 1f)])!;
        Assert.That(Vector3.Distance(below, new Vector3(0f, 0f, -12f)), Is.LessThan(0.001f), "COM below the pivot plus stick up must push toward the player.");

        Vector3 front = (Vector3)force.Invoke(null, [new Vector3(0f, 0f, 1f), Vector3.right, new Vector2(-1f, 0f)])!;
        Assert.That(Vector3.Distance(front, new Vector3(12f, 0f, 0f)), Is.LessThan(0.001f), "Left of COM-to-pivot radial must give +X.");

        Vector3 rear = (Vector3)force.Invoke(null, [new Vector3(0f, 0f, -1f), Vector3.right, new Vector2(-1f, 0f)])!;
        Assert.That(Vector3.Distance(rear, new Vector3(-12f, 0f, 0f)), Is.LessThan(0.001f));

        Vector3 above = (Vector3)force.Invoke(null, [new Vector3(0f, 1f, 0f), Vector3.right, new Vector2(0f, 1f)])!;
        Assert.That(Vector3.Distance(above, new Vector3(0f, 0f, 12f)), Is.LessThan(0.001f), "As the radial swings over the top the up-stick continues around the pivot.");

        Vector3 lowFront = (Vector3)force.Invoke(null, [new Vector3(0.5f, -3f, 1f), Vector3.right, new Vector2(-1f, 0f)])!;
        Vector3 highFront = (Vector3)force.Invoke(null, [new Vector3(0.5f, 3f, 1f), Vector3.right, new Vector2(-1f, 0f)])!;
        Assert.That(Vector3.Distance(lowFront, highFront), Is.LessThan(0.0001f), "The horizontal tangent must depend only on the radial's horizontal projection.");
        Assert.That(lowFront.y, Is.EqualTo(0f).Within(0.0001f));

        Vector3 verticalRadial = (Vector3)force.Invoke(null, [new Vector3(0f, 1f, 0f), Vector3.right, new Vector2(-1f, 0f)])!;
        Assert.That(verticalRadial, Is.EqualTo(Vector3.zero), "A vertical radial has no horizontal tangent to push along.");

        Vector3 diagonal = (Vector3)force.Invoke(null, [new Vector3(0f, -1f, 1f), Vector3.right, new Vector2(-1f, 1f)])!;
        Assert.That(diagonal.magnitude, Is.EqualTo(12f).Within(0.001f), "Combined tangents must clamp to the acceleration cap.");

        Vector3 yawed = (Vector3)force.Invoke(null, [new Vector3(0f, -1f, 0f), new Vector3(0f, 0f, -1f), new Vector2(0f, 1f)])!;
        Assert.That(Vector3.Distance(yawed, new Vector3(-12f, 0f, 0f)), Is.LessThan(0.001f), "Facing +X must rotate the stick-up tangent to -X.");
    }

    [Test]
    public void HoverForceFollowsThePointerRightUpPlane()
    {
        MethodInfo force = GetPhysicsMethod("EvaluateHoverForceAcceleration");
        Assert.That(force, Is.Not.Null, "Unheld hovered force must live in the pointer's right/up plane.");

        Vector3 level = (Vector3)force!.Invoke(null, [Quaternion.identity, new Vector2(1f, 0f)])!;
        Assert.That(Vector3.Distance(level, new Vector3(12f, 0f, 0f)), Is.LessThan(0.001f));
        Vector3 up = (Vector3)force.Invoke(null, [Quaternion.identity, new Vector2(0f, 1f)])!;
        Assert.That(Vector3.Distance(up, new Vector3(0f, 12f, 0f)), Is.LessThan(0.001f));

        Quaternion down = Quaternion.Euler(90f, 0f, 0f);
        Vector3 forward = (Vector3)force.Invoke(null, [down, new Vector2(0f, 1f)])!;
        Assert.That(Vector3.Distance(forward, new Vector3(0f, 0f, 12f)), Is.LessThan(0.001f), "A straight-down pointer must map stick-up to world forward.");
        Vector3 toward = (Vector3)force.Invoke(null, [down, new Vector2(0f, -1f)])!;
        Assert.That(Vector3.Distance(toward, new Vector3(0f, 0f, -12f)), Is.LessThan(0.001f), "Stick-down on a down pointer pulls back toward the player.");

        Quaternion angled = Quaternion.Euler(45f, 0f, 0f);
        Vector3 diagonal = (Vector3)force.Invoke(null, [angled, new Vector2(0f, 1f)])!;
        Assert.That(
            Vector3.Distance(diagonal, new Vector3(0f, 12f * Mathf.Cos(Mathf.PI / 4f), 12f * Mathf.Sin(Mathf.PI / 4f))),
            Is.LessThan(0.001f),
            "A 45-degree pointer must tilt the force 45 degrees forward.");

        Quaternion yawed = Quaternion.Euler(0f, 90f, 0f);
        Vector3 sideways = (Vector3)force.Invoke(null, [yawed, new Vector2(1f, 0f)])!;
        Assert.That(Vector3.Distance(sideways, new Vector3(0f, 0f, -12f)), Is.LessThan(0.001f));

        Vector3 combined = (Vector3)force.Invoke(null, [Quaternion.identity, new Vector2(1f, 1f).normalized])!;
        Assert.That(combined.magnitude, Is.EqualTo(12f).Within(0.001f));
    }

    [Test]
    public void PlayerRightFollowsHeadYawAndIgnoresPitchRoll()
    {
        MethodInfo playerRight = GetPhysicsMethod("EvaluatePlayerRight");
        Assert.That(playerRight, Is.Not.Null, "Spin and vertical tangents need the player's horizontal right from head yaw.");

        Assert.That(Vector3.Distance((Vector3)playerRight!.Invoke(null, [Quaternion.identity])!, Vector3.right), Is.LessThan(0.0001f));
        Assert.That(
            Vector3.Distance((Vector3)playerRight.Invoke(null, [Quaternion.Euler(0f, 90f, 0f)])!, new Vector3(0f, 0f, -1f)),
            Is.LessThan(0.001f),
            "A 90-degree yaw must rotate player-right to -Z.");
        Assert.That(
            Vector3.Distance((Vector3)playerRight.Invoke(null, [Quaternion.Euler(60f, 0f, 0f)])!, Vector3.right),
            Is.LessThan(0.0001f),
            "Looking straight down must not change player-right.");
        Assert.That(
            Vector3.Distance((Vector3)playerRight.Invoke(null, [Quaternion.Euler(0f, 0f, 30f)])!, Vector3.right),
            Is.LessThan(0.0001f),
            "Head roll must not tilt player-right.");
    }

    [Test]
    public void InertiaTorqueScalesAccelerationThroughTheFreeTensor()
    {
        MethodInfo torque = GetPhysicsMethod("EvaluateInertiaTorque");
        Assert.That(torque, Is.Not.Null, "Requested angular acceleration must convert through the free body inertia tensor.");

        Vector3 aligned = (Vector3)torque!.Invoke(
            null,
            [new Vector3(1f, 2f, 3f), Quaternion.identity, Quaternion.identity, new Vector3(2f, 3f, 4f)])!;
        Assert.That(Vector3.Distance(aligned, new Vector3(2f, 6f, 12f)), Is.LessThan(0.001f), "Aligned frames scale each axis by its tensor entry.");

        Vector3 rotated = (Vector3)torque.Invoke(
            null,
            [new Vector3(0f, 0f, 4f), Quaternion.Euler(0f, 90f, 0f), Quaternion.identity, new Vector3(2f, 3f, 4f)])!;
        Assert.That(
            Vector3.Distance(rotated, new Vector3(0f, 0f, 8f)),
            Is.LessThan(0.001f),
            "The tensor must apply in the body's current rotated frame.");
    }

    [Test]
    public void ControllerInputStoresPerBodySampleOnGrabbable()
    {
        Type grabbable = GetPluginType("ChromaGLS.Settings.FerrofluidGrabbable");
        MethodInfo setInput = grabbable.GetMethod("SetControllerInput", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(setInput, Is.Not.Null, "Each body needs its per-render dominant/non-dominant stick sample.");
        Type magnet = GetPluginType("ChromaGLS.Settings.FerrofluidMagnetController");
        Assert.That(
            magnet.GetMethod("SampleForceInput", BindingFlags.Instance | BindingFlags.NonPublic),
            Is.Not.Null,
            "The input owner must expose the dominant-role stick sample per controller.");
        Assert.That(
            magnet.GetMethod("SampleSpinInput", BindingFlags.Instance | BindingFlags.NonPublic),
            Is.Not.Null,
            "The input owner must expose the non-dominant-role stick sample per controller.");

        object body = FormatterServices.GetUninitializedObject(grabbable);
        Vector2 force = new(0.25f, -0.5f);
        Vector2 spin = new(-0.75f, 0.5f);
        Quaternion pointer = Quaternion.Euler(10f, 20f, 30f);
        Vector3 playerRight = new(0.6f, 0f, -0.8f);
        setInput!.Invoke(body, [force, spin, pointer, playerRight]);

        BindingFlags inst = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.That((Vector2)grabbable.GetField("_forceStick", inst)!.GetValue(body)!, Is.EqualTo(force));
        Assert.That((Vector2)grabbable.GetField("_spinStick", inst)!.GetValue(body)!, Is.EqualTo(spin));
        Assert.That(Quaternion.Angle((Quaternion)grabbable.GetField("_dominantPointerRotation", inst)!.GetValue(body)!, pointer), Is.LessThan(0.001f));
        Assert.That((Vector3)grabbable.GetField("_playerRight", inst)!.GetValue(body)!, Is.EqualTo(playerRight));

        setInput.Invoke(body, [Vector2.zero, Vector2.zero, Quaternion.identity, Vector3.right]);
        Assert.That((Vector2)grabbable.GetField("_forceStick", inst)!.GetValue(body)!, Is.EqualTo(Vector2.zero));
        Assert.That((Vector2)grabbable.GetField("_spinStick", inst)!.GetValue(body)!, Is.EqualTo(Vector2.zero));
    }

    [Test]
    public void ReleaseClearsStoredControllerInput()
    {
        Type grabbable = GetPluginType("ChromaGLS.Settings.FerrofluidGrabbable");
        MethodInfo setInput = grabbable.GetMethod("SetControllerInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo release = grabbable.GetMethod("Release", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(release, Is.Not.Null, "Release must drop the stored sample so disable and unheld release leave no stale force.");

        object body = FormatterServices.GetUninitializedObject(grabbable);
        setInput!.Invoke(body, [new Vector2(0.5f, 0.25f), new Vector2(-0.5f, 0.5f), Quaternion.identity, Vector3.right]);
        release!.Invoke(body, null);

        BindingFlags inst = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.That((Vector2)grabbable.GetField("_forceStick", inst)!.GetValue(body)!, Is.EqualTo(Vector2.zero), "An unheld release must still clear the stored force sample.");
        Assert.That((Vector2)grabbable.GetField("_spinStick", inst)!.GetValue(body)!, Is.EqualTo(Vector2.zero), "An unheld release must still clear the stored spin sample.");
    }

    private static object CreateShape(bool isSphere, Vector3 halfExtents, float radius)
    {
        Type shapeType = GetPluginType("ChromaGLS.Settings.FerrofluidGrabShape");
        ConstructorInfo? constructor = shapeType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [typeof(bool), typeof(Vector3), typeof(float)],
            null);
        Assert.That(constructor, Is.Not.Null);
        return constructor!.Invoke([isSphere, halfExtents, radius]);
    }

    private static MethodInfo GetPhysicsMethod(string name)
    {
        return GetPluginType("ChromaGLS.Settings.FerrofluidGrabPhysics")
            .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new AssertionException($"Missing controller method: {name}");
    }

    private static Type GetPluginType(string typeName)
    {
        return Assembly.Load("ChromaGLS").GetType(typeName, throwOnError: false)
            ?? throw new AssertionException($"Missing plugin type: {typeName}");
    }

    private static object CreateOwnership()
        => Activator.CreateInstance(
            GetPluginType("ChromaGLS.Settings.FerrofluidGrabOwnership"),
            nonPublic: true)!;
}
