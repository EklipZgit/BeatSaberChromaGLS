using System;

namespace ChromaGLS.Settings
{
    public enum ReflectionQuality
    {
        None,
        Low,
        High,
        Ultra,
    }

    internal static class ReflectionQualitySettings
    {
        internal static void Initialize(Config config, Func<int> readGraphicsMemoryMb)
        {
            if (config.ReflectionsQualityInitialized)
                return;

            config.ReflectionsQuality = GetInitialQuality(readGraphicsMemoryMb());
            config.ReflectionsQualityInitialized = true;
        }

        internal static ReflectionQuality GetInitialQuality(int graphicsMemoryMb)
        {
            if (graphicsMemoryMb < 1024)
                return ReflectionQuality.None;

            return graphicsMemoryMb < 2048 ? ReflectionQuality.Low : ReflectionQuality.High;
        }

        internal static int GetResolution(ReflectionQuality quality)
            => quality switch
            {
                ReflectionQuality.None => 0,
                ReflectionQuality.Low => 1024,
                ReflectionQuality.High or ReflectionQuality.Ultra => 4096,
                _ => throw new ArgumentOutOfRangeException(nameof(quality)),
            };

        // Procedural floor-light reflections stay live without the optional room pack; None
        // keeps the Low menu-light atlas rather than disposing it.
        internal static int GetMenuLightResolution(ReflectionQuality quality)
            => quality switch
            {
                ReflectionQuality.Ultra => 16384,
                ReflectionQuality.High => 8192,
                _ => 1024,
            };

        internal static int GetRoomResolution(ReflectionQuality quality)
            => quality switch
            {
                ReflectionQuality.None => 0,
                ReflectionQuality.Low => 1024,
                ReflectionQuality.High or ReflectionQuality.Ultra => 2048,
                _ => throw new ArgumentOutOfRangeException(nameof(quality)),
            };
    }
}
