using System;

namespace ChromaGLS.Settings
{
    // Stretch primitive ticks within the existing GPU budget, with each integration substep
    // bounded to one nominal interval. Keep the torus's authored fixed-rate schedule.
    internal static class FerrofluidSimulationTiming
    {
        internal const float IntervalSeconds = 1f / 120f;
        internal const int MaximumTicksPerFrame = 2;

        internal static (int TickCount, float TickSeconds, float Remainder) CalculateStepSchedule(
            float accumulatedSeconds,
            float elapsedSeconds,
            int substepsPerTick,
            bool primitive)
        {
            float maximumElapsed = IntervalSeconds * MaximumTicksPerFrame * (primitive ? substepsPerTick : 1);
            float available = Math.Min(accumulatedSeconds + elapsedSeconds, maximumElapsed);
            int wholeTicks = (int)(available / IntervalSeconds);
            int ticks = Math.Min(wholeTicks, MaximumTicksPerFrame);
            float tickSeconds = ticks > 0 ? (wholeTicks * IntervalSeconds) / ticks : IntervalSeconds;
            return (ticks, tickSeconds, available - (wholeTicks * IntervalSeconds));
        }
    }
}
