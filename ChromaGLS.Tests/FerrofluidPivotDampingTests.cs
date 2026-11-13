using System;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class FerrofluidPivotDampingTests
{
    [Test]
    public void SwingDampingForceIgnoresTwistAboutThePivotLine()
    {
        MethodInfo damping = GetPhysicsMethod("EvaluatePivotSwingDampingForce");
        Assert.That(damping, Is.Not.Null, "Held swing damping needs a pivot-to-center tangential force helper.");

        Vector3 lever = new(0f, -0.275f, 0f);
        Assert.That(
            (Vector3)damping!.Invoke(null, [lever, new Vector3(0f, 10f, 0f), 2f])!,
            Is.EqualTo(Vector3.zero),
            "Pure twist along the pivot-to-center line must see no damping.");
        Assert.That(
            (Vector3)damping.Invoke(null, [-lever, new Vector3(0f, -10f, 0f), 2f])!,
            Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void SwingDampingForceOpposesTangentialSwing()
    {
        MethodInfo damping = GetPhysicsMethod("EvaluatePivotSwingDampingForce");
        Assert.That(damping, Is.Not.Null);

        Vector3 lever = new(0f, -0.275f, 0f);
        Vector3 force = (Vector3)damping!.Invoke(null, [lever, new Vector3(4f, 0f, 0f), 2f])!;
        Assert.That(force, Is.EqualTo(new Vector3(0f, 0f, 2.2f)), "Damping must oppose the COM's tangential velocity about the pivot.");

        Vector3 withTwist = (Vector3)damping.Invoke(null, [lever, new Vector3(4f, 10f, 0f), 2f])!;
        Assert.That(withTwist, Is.EqualTo(force), "Adding parallel twist must not change the damping force.");

        Vector3 yawed = (Vector3)damping.Invoke(
            null,
            [Quaternion.Euler(0f, 90f, 0f) * lever, new Vector3(0f, 0f, 4f), 2f])!;
        Assert.That(
            Vector3.Distance(yawed, new Vector3(-2.2f, 0f, 0f)),
            Is.LessThan(0.001f),
            "The force must follow the rotated lever frame identically.");
    }

    [Test]
    public void SwingDampingForceZeroLeverProducesNothing()
    {
        MethodInfo damping = GetPhysicsMethod("EvaluatePivotSwingDampingForce");
        Assert.That(damping, Is.Not.Null);

        Assert.That(
            (Vector3)damping!.Invoke(null, [Vector3.zero, new Vector3(4f, 8f, 4f), 2f])!,
            Is.EqualTo(Vector3.zero),
            "A center grab has no lever and must not invent friction.");
    }

    [Test]
    public void HeldSwingDampingScaleResetsOnRelease()
    {
        Type grabbable = GetPluginType("ChromaGLS.Settings.FerrofluidGrabbable");
        FieldInfo scale = grabbable.GetField("_heldSwingDampingForceScale", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(scale, Is.Not.Null, "The cached swing-damping scale must exist on the grabbable.");
        MethodInfo release = grabbable.GetMethod("Release", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(release, Is.Not.Null);

        object body = FormatterServices.GetUninitializedObject(grabbable);
        scale!.SetValue(body, 3.5f);
        release!.Invoke(body, null);
        Assert.That((float)scale.GetValue(body)!, Is.EqualTo(0f), "Release must clear the damping scale before its unheld return.");
    }

    private static MethodInfo GetPhysicsMethod(string name)
    {
        return GetPluginType("ChromaGLS.Settings.FerrofluidGrabPhysics")
            .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new AssertionException($"Missing damping method: {name}");
    }

    private static Type GetPluginType(string typeName)
    {
        return Assembly.Load("ChromaGLS").GetType(typeName, throwOnError: false)
            ?? throw new AssertionException($"Missing plugin type: {typeName}");
    }
}
