namespace ChromaGLS.Helpers
{
    // Single switch wired to Plugin.ApplyEnabled/SetEnabled so every spline path falls back to the
    // native tween behavior while the plugin is disabled. The default must stay off so a missing
    // patch wiring never changes playback silently.
    internal static class GlsSplineRuntime
    {
        internal static bool Enabled { get; set; }
    }
}
