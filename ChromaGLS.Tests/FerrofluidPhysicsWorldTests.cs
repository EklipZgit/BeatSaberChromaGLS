using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class FerrofluidPhysicsWorldTests
{
    [Test]
    public void CubePhysicsSubstepsBoundTwentyDegreesPerStep()
    {
        MethodInfo substeps = GetPhysicsMethod("EvaluateCubePhysicsSubsteps");
        float rps10 = 10f * 2f * Mathf.PI;
        float rps20 = 20f * 2f * Mathf.PI;
        float dt50 = 1f / 50f;
        float dt90 = 1f / 90f;

        Assert.That(Substeps(substeps, 0f, dt50), Is.EqualTo(1), "an idle cube needs no substeps");
        Assert.That(Substeps(substeps, 0f, dt90), Is.EqualTo(1));
        Assert.That(Substeps(substeps, 2f * Mathf.PI, dt50), Is.EqualTo(1), "1 RPS stays under the bound");
        Assert.That(Substeps(substeps, 2f * Mathf.PI, dt90), Is.EqualTo(1));
        Assert.That(Substeps(substeps, rps10, dt50), Is.EqualTo(4), "10 RPS at 50 Hz is 4 substeps");
        Assert.That(Substeps(substeps, rps10, dt90), Is.EqualTo(2), "10 RPS at 90 Hz is 2 substeps");
        Assert.That(Substeps(substeps, rps20, dt50), Is.EqualTo(8), "20 RPS at 50 Hz is 8 substeps");
        Assert.That(Substeps(substeps, rps20, dt90), Is.EqualTo(4), "20 RPS at 90 Hz is 4 substeps");
    }

    [Test]
    public void CubeCornerMotionGateUsesPointZeroFiveSpeed()
    {
        MethodInfo gate = GetPhysicsMethod("IsCubeCornerStationary");

        Assert.That((bool)gate.Invoke(null, [0f])!, Is.True, "an idle corner top keeps low grip");
        Assert.That((bool)gate.Invoke(null, [0.001f])!, Is.True, "below 0.05 m/s stays low grip");
        Assert.That((bool)gate.Invoke(null, [0.0025f])!, Is.True, "the exact 0.05 m/s boundary stays low grip");
        Assert.That((bool)gate.Invoke(null, [0.0026f])!, Is.False, "above 0.05 m/s uses normal floor grip");
        Assert.That((bool)gate.Invoke(null, [0.09f])!, Is.False, "a sliding corner top uses normal floor grip");
    }

    private static int Substeps(MethodInfo method, float angularSpeed, float deltaTime)
        => (int)method.Invoke(null, [angularSpeed, deltaTime])!;

    private static MethodInfo GetPhysicsMethod(string name)
    {
        return GetPluginType("ChromaGLS.Settings.FerrofluidGrabPhysics")
            .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new AssertionException($"Missing physics method: {name}");
    }

    private static Type GetPluginType(string typeName)
    {
        return Assembly.Load("ChromaGLS").GetType(typeName, throwOnError: false)
            ?? throw new AssertionException($"Missing plugin type: {typeName}");
    }
}
