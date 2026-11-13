using System;
using System.Reflection;
using NUnit.Framework;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class FerrofluidSimulationTimingTests
{
    private const float IntervalSeconds = 1f / 120f;

    private static (int Ticks, float TickSeconds, float Remainder) Schedule(
        float accumulated,
        float elapsed,
        int substeps,
        bool primitive = true)
    {
        MethodInfo method = Assembly.Load("ChromaGLS")
            .GetType("ChromaGLS.Settings.FerrofluidSimulationTiming", true)!
            .GetMethod("CalculateStepSchedule", BindingFlags.Static | BindingFlags.NonPublic)!;
        return ((int, float, float))method.Invoke(null, new object[] { accumulated, elapsed, substeps, primitive })!;
    }

    [TestCase(45)]
    [TestCase(50)]
    [TestCase(72)]
    [TestCase(90)]
    [TestCase(120)]
    [TestCase(144)]
    public void DefaultPrimitiveScheduleConservesElapsedTime(int renderedFramesPerSecond)
    {
        float remainder = 0f;
        double simulatedSeconds = 0;
        for (int frame = 0; frame < renderedFramesPerSecond; frame++)
        {
            var schedule = Schedule(remainder, 1f / renderedFramesPerSecond, 2);
            Assert.That(schedule.Ticks, Is.InRange(0, 2), "do not add GPU ticks to fix the clock");
            Assert.That(schedule.TickSeconds / 2f, Is.LessThanOrEqualTo(IntervalSeconds + 0.000001f), "each SPH integration step stays bounded");
            simulatedSeconds += schedule.Ticks * schedule.TickSeconds;
            remainder = schedule.Remainder;
        }

        Assert.That(simulatedSeconds + remainder, Is.EqualTo(1.0).Within(0.00001), "the primitive must not silently discard ordinary rendered-frame time");
        Assert.That(simulatedSeconds, Is.GreaterThanOrEqualTo(1.0 - IntervalSeconds - 0.00001), "at most one nominal interval remains queued");
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(4)]
    [TestCase(7)]
    public void LongHitchCannotCreateUnboundedCatchupOrIntegrationSteps(int substeps)
    {
        var schedule = Schedule(0f, 0.5f, substeps);
        Assert.That(schedule.Ticks, Is.EqualTo(2));
        Assert.That(schedule.TickSeconds / substeps, Is.LessThanOrEqualTo(IntervalSeconds + 0.000001f));
        Assert.That(schedule.Remainder, Is.InRange(0f, IntervalSeconds));
        Assert.That(schedule.Ticks * schedule.TickSeconds, Is.LessThanOrEqualTo(2f * substeps * IntervalSeconds + 0.000001f));
    }

    [Test]
    public void FractionalFramesRetainTheirRemainderUntilAWholeTickIsDue()
    {
        var first = Schedule(0f, 0.004f, 2);
        Assert.That(first.Ticks, Is.Zero);
        var second = Schedule(first.Remainder, 0.004f, 2);
        Assert.That(second.Ticks, Is.Zero);
        var third = Schedule(second.Remainder, 0.0005f, 2);
        Assert.That(third.Ticks, Is.EqualTo(1));
        Assert.That(third.TickSeconds, Is.EqualTo(IntervalSeconds));
        Assert.That(third.TickSeconds + third.Remainder, Is.EqualTo(0.0085f).Within(0.000001f));
    }

    [Test]
    public void TorusKeepsItsAuthoredTwoTickBudget()
    {
        float remainder = 0f;
        double simulatedSeconds = 0;
        for (int frame = 0; frame < 50; frame++)
        {
            var schedule = Schedule(remainder, 0.02f, 2, primitive: false);
            Assert.That(schedule.TickSeconds, Is.EqualTo(IntervalSeconds));
            simulatedSeconds += schedule.Ticks * schedule.TickSeconds;
            remainder = schedule.Remainder;
        }

        Assert.That(simulatedSeconds, Is.EqualTo(50.0 * 2.0 / 120.0).Within(0.00001));
    }
}
