using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using NUnitLite;

namespace ChromaGLS.Tests;

// NUnitLite's AutoRun turns this net48 assembly into a self-contained runner for Unity's bundled mono.exe.
internal static class Program
{
    // Unity icalls like Shader.PropertyToID are registered by the engine when it hosts Mono; the
    // standalone test host must stub the few that plugin type initializers invoke so pure math
    // helpers stay invocable without a live renderer.
    [DllImport("mono-2.0-sgen.dll")]
    private static extern void mono_add_internal_call(string name, IntPtr method);

    private delegate int PropertyToIDDelegate(IntPtr name);

    private delegate int ObjectInstanceOffsetDelegate();

    private static readonly PropertyToIDDelegate PropertyToIDStub = _ => System.Threading.Interlocked.Increment(ref _nextPropertyId);
    private static readonly ObjectInstanceOffsetDelegate ObjectInstanceOffsetStub = () => 0;
    private static int _nextPropertyId;

    public static int Main(string[] args)
    {
        return new AutoRun().Execute(args);
    }

    internal static void InitializeHost()
    {
        // GlsEasingTests patches SongCore's MonoMod-generated selection hook as part of the real plugin
        // lifecycle. Unity Mono's Cecil DMD backend moves that hook into another assembly and loses
        // access to SongCore's private fields; DynamicMethod retains skip-visibility like the game host.
        Environment.SetEnvironmentVariable("MONOMOD_DMDType", "dynamicmethod");

        try
        {
            // Beat Saber 1.44.1's Unity 6 module dispatches through the injected span overload, so both icall names need the deterministic test stub.
            mono_add_internal_call(
                "UnityEngine.Shader::PropertyToID",
                Marshal.GetFunctionPointerForDelegate(PropertyToIDStub));
            mono_add_internal_call(
                "UnityEngine.Shader::PropertyToID_Injected",
                Marshal.GetFunctionPointerForDelegate(PropertyToIDStub));
            mono_add_internal_call(
                "UnityEngine.Object::GetOffsetOfInstanceIDInCPlusPlusObject",
                Marshal.GetFunctionPointerForDelegate(ObjectInstanceOffsetStub));
        }
        catch (Exception error)
        {
            // Missing icall registration leaves the GPU-static tests failing loudly rather than silently skipped.
            throw new InvalidOperationException("Could not initialize Unity internal calls. Run ChromaGLS.Tests with Unity's MonoBleedingEdge runtime. See README.md: Running tests in Rider.", error);
        }
    }
}

[SetUpFixture]
public sealed class UnityMonoTestHost
{
    [OneTimeSetUp]
    public void Initialize()
    {
        Assert.That(Type.GetType("Mono.Runtime"), Is.Not.Null,
            "ChromaGLS.Tests requires Unity's MonoBleedingEdge runtime, not desktop .NET Framework or .NET. " +
            "In Rider, run ChromaGLS.Tests as a .NET Project configuration with Runtime = Mono, " +
            "or run pwsh ./RunAllTests.ps1 -Version 1.40.8 from the repository root. " +
            "See README.md: Running tests in Rider.");

        Program.InitializeHost();
    }
}
