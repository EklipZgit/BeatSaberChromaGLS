using System;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class FerrofluidDepthTests
{
    [Test]
    public void GrabDepthDeltaMatchesRampIntegral()
    {
        MethodInfo delta = GetPhysicsMethod("EvaluateGrabDepthDelta");
        Assert.That(delta, Is.Not.Null, "Depth movement needs an exact per-step integral of the ramped, depth-scaled speed.");

        float first = (float)delta!.Invoke(null, [1f, 1, 0f, 0.02f])!;
        Assert.That(first, Is.EqualTo(0.00236f).Within(0.000001f), "The first 0.02s step at 1m must move 0.00236m.");

        float crossing = (float)delta.Invoke(null, [1f, 1, 0.49f, 0.02f])!;
        Assert.That(crossing, Is.EqualTo(0.01991f).Within(0.000001f), "A step crossing the 0.5s ramp end must integrate both pieces exactly.");

        float steady = (float)delta.Invoke(null, [1f, 1, 0.6f, 0.02f])!;
        Assert.That(steady, Is.EqualTo(0.02f).Within(0.000001f), "After 0.5s the speed is constant 1x depth per second.");

        float far = (float)delta.Invoke(null, [2f, 1, 0f, 0.02f])!;
        Assert.That(far, Is.EqualTo(first * 2f).Within(0.0000001f), "Speed scales linearly with current depth.");

        float closer = (float)delta.Invoke(null, [1f, -1, 0f, 0.02f])!;
        Assert.That(closer, Is.EqualTo(-first).Within(0.0000001f), "The closer direction mirrors the away displacement.");

        Assert.That((float)delta.Invoke(null, [1f, 0, 0f, 0.02f])!, Is.EqualTo(0f), "Neutral input must not move.");
        Assert.That((float)delta.Invoke(null, [1f, 0, 0.6f, 0.02f])!, Is.EqualTo(0f));
    }

    [Test]
    public void GrabDepthInwardCannotCrossBehindPointer()
    {
        MethodInfo delta = GetPhysicsMethod("EvaluateGrabDepthDelta");
        Assert.That(delta, Is.Not.Null);

        float clamped = (float)delta!.Invoke(null, [0.01f, -1, 0f, 2f])!;
        Assert.That(clamped, Is.EqualTo(-0.01f).Within(0.000001f), "Inward movement must stop at the pointer instead of crossing behind the hand.");

        float exact = (float)delta.Invoke(null, [0.4f, -1, 0.6f, 0.4f])!;
        Assert.That(exact, Is.EqualTo(-0.16f).Within(0.000001f));
    }

    [Test]
    public void SetGrabDepthInputClampsAndResetsRampOnDirectionChange()
    {
        Type grabbable = GetPluginType("ChromaGLS.Settings.FerrofluidGrabbable");
        MethodInfo setDepth = grabbable.GetMethod("SetGrabDepthInput", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(setDepth, Is.Not.Null, "Each held body needs its depth-button sample stored per render.");

        BindingFlags inst = BindingFlags.Instance | BindingFlags.NonPublic;
        object body = FormatterServices.GetUninitializedObject(grabbable);
        setDepth!.Invoke(body, [1]);
        grabbable.GetField("_grabDepthHeldTime", inst)!.SetValue(body, 0.4f);
        Assert.That((int)grabbable.GetField("_grabDepthInput", inst)!.GetValue(body)!, Is.EqualTo(1));

        setDepth.Invoke(body, [0]);
        Assert.That((float)grabbable.GetField("_grabDepthHeldTime", inst)!.GetValue(body)!, Is.EqualTo(0f), "Neutral must reset the ramp.");

        setDepth.Invoke(body, [1]);
        grabbable.GetField("_grabDepthHeldTime", inst)!.SetValue(body, 0.4f);
        setDepth.Invoke(body, [-1]);
        Assert.That((float)grabbable.GetField("_grabDepthHeldTime", inst)!.GetValue(body)!, Is.EqualTo(0f), "A reversal must reset the ramp.");
        Assert.That((int)grabbable.GetField("_grabDepthInput", inst)!.GetValue(body)!, Is.EqualTo(-1));

        setDepth.Invoke(body, [-7]);
        Assert.That((int)grabbable.GetField("_grabDepthInput", inst)!.GetValue(body)!, Is.EqualTo(-1), "Input must clamp to [-1, 1].");
        setDepth.Invoke(body, [9]);
        Assert.That((int)grabbable.GetField("_grabDepthInput", inst)!.GetValue(body)!, Is.EqualTo(1));

        grabbable.GetField("_grabDepthHeldTime", inst)!.SetValue(body, 0.4f);
        setDepth.Invoke(body, [1]);
        Assert.That((float)grabbable.GetField("_grabDepthHeldTime", inst)!.GetValue(body)!, Is.EqualTo(0.4f), "Holding the same direction must keep the ramp running.");
    }

    [Test]
    public void ReleaseAndAcquireClearDepthInput()
    {
        Type grabbable = GetPluginType("ChromaGLS.Settings.FerrofluidGrabbable");
        MethodInfo setDepth = grabbable.GetMethod("SetGrabDepthInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo release = grabbable.GetMethod("Release", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(release, Is.Not.Null, "Release must drop the depth sample before its unheld return.");

        BindingFlags inst = BindingFlags.Instance | BindingFlags.NonPublic;
        object body = FormatterServices.GetUninitializedObject(grabbable);
        setDepth.Invoke(body, [1]);
        grabbable.GetField("_grabDepthHeldTime", inst)!.SetValue(body, 0.4f);
        release!.Invoke(body, null);
        Assert.That((int)grabbable.GetField("_grabDepthInput", inst)!.GetValue(body)!, Is.EqualTo(0), "An unheld release must clear the depth direction.");
        Assert.That((float)grabbable.GetField("_grabDepthHeldTime", inst)!.GetValue(body)!, Is.EqualTo(0f), "An unheld release must clear the ramp clock.");
    }

    [Test]
    public void DepthButtonSamplingExistsOnInputOwner()
    {
        Type magnet = GetPluginType("ChromaGLS.Settings.FerrofluidMagnetController");
        MethodInfo sample = magnet.GetMethod("SampleGrabDepthInput", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(sample, Is.Not.Null, "The input owner must expose depth-button sampling for the holding controller.");
        Assert.That(sample!.GetParameters().Length, Is.EqualTo(1));
        Assert.That(sample.ReturnType, Is.EqualTo(typeof(int)));
    }

    private static MethodInfo GetPhysicsMethod(string name)
    {
        return GetPluginType("ChromaGLS.Settings.FerrofluidGrabPhysics")
            .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new AssertionException($"Missing depth method: {name}");
    }

    private static Type GetPluginType(string typeName)
    {
        return Assembly.Load("ChromaGLS").GetType(typeName, throwOnError: false)
            ?? throw new AssertionException($"Missing plugin type: {typeName}");
    }
}
