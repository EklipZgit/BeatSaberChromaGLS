using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using NUnitLite;
using UnityEngine;

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

    // Quaternion factories and axis-angle queries are engine icalls; the grab physics math exercises
    // them, so the host provides managed equivalents rather than declaring the tests GPU-only.
    private delegate void AngleAxisDelegate(float angle, ref Vector3 axis, out Quaternion ret);

    private delegate void FromEulerRadDelegate(ref Vector3 euler, out Quaternion ret);

    private delegate void ToEulerRadDelegate(ref Quaternion rotation, out Vector3 ret);

    private delegate void ToAxisAngleRadDelegate(ref Quaternion rotation, out Vector3 axis, out float angle);

    private delegate void InverseDelegate(ref Quaternion rotation, out Quaternion ret);

    private delegate void FromToRotationDelegate(ref Vector3 fromDirection, ref Vector3 toDirection, out Quaternion ret);

    private delegate void LookRotationDelegate(ref Vector3 forward, ref Vector3 upwards, out Quaternion ret);

    private delegate void SlerpDelegate(ref Quaternion a, ref Quaternion b, float t, out Quaternion ret);

    private static readonly PropertyToIDDelegate PropertyToIDStub = _ => System.Threading.Interlocked.Increment(ref _nextPropertyId);
    private static readonly ObjectInstanceOffsetDelegate ObjectInstanceOffsetStub = () => 0;
    private static readonly AngleAxisDelegate AngleAxisStub = QuaternionShim.AngleAxis;
    private static readonly FromEulerRadDelegate FromEulerRadStub = QuaternionShim.FromEulerRad;
    private static readonly ToEulerRadDelegate ToEulerRadStub = QuaternionShim.ToEulerRad;
    private static readonly ToAxisAngleRadDelegate ToAxisAngleRadStub = QuaternionShim.ToAxisAngleRad;
    private static readonly InverseDelegate InverseStub = QuaternionShim.Inverse;
    private static readonly FromToRotationDelegate FromToRotationStub = QuaternionShim.FromToRotation;
    private static readonly LookRotationDelegate LookRotationStub = QuaternionShim.LookRotation;
    private static readonly SlerpDelegate SlerpStub = QuaternionShim.Slerp;
    private static int _nextPropertyId;

    public static int Main(string[] args)
    {
        // NUnit owns stdout; the game's Harmony console interception must stay disabled or it
        // installs console hooks that crash this standalone Mono host during shutdown.
        bool savedInterceptor = IPA.Logging.StdoutInterceptor.usingInterceptor;
        IPA.Logging.StdoutInterceptor.usingInterceptor = true;
        try
        {
            return new AutoRun().Execute(args);
        }
        finally
        {
            try
            {
                // BSIPA's foreground log thread blocks mono exit; drain and join it so the
                // runner returns after writing results.
                if (IPA.Logging.StandardLogger.logThread != null)
                    IPA.Logging.StandardLogger.StopLogThread();
            }
            finally
            {
                IPA.Logging.StdoutInterceptor.usingInterceptor = savedInterceptor;
            }
        }
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
            mono_add_internal_call(
                "UnityEngine.Quaternion::AngleAxis_Injected",
                Marshal.GetFunctionPointerForDelegate(AngleAxisStub));
            mono_add_internal_call(
                "UnityEngine.Quaternion::Internal_FromEulerRad_Injected",
                Marshal.GetFunctionPointerForDelegate(FromEulerRadStub));
            mono_add_internal_call(
                "UnityEngine.Quaternion::Internal_ToEulerRad_Injected",
                Marshal.GetFunctionPointerForDelegate(ToEulerRadStub));
            mono_add_internal_call(
                "UnityEngine.Quaternion::Internal_ToAxisAngleRad_Injected",
                Marshal.GetFunctionPointerForDelegate(ToAxisAngleRadStub));
            mono_add_internal_call(
                "UnityEngine.Quaternion::Inverse_Injected",
                Marshal.GetFunctionPointerForDelegate(InverseStub));
            mono_add_internal_call(
                "UnityEngine.Quaternion::FromToRotation_Injected",
                Marshal.GetFunctionPointerForDelegate(FromToRotationStub));
            mono_add_internal_call(
                "UnityEngine.Quaternion::LookRotation_Injected",
                Marshal.GetFunctionPointerForDelegate(LookRotationStub));
            mono_add_internal_call(
                "UnityEngine.Quaternion::Slerp_Injected",
                Marshal.GetFunctionPointerForDelegate(SlerpStub));
            mono_add_internal_call(
                "UnityEngine.Quaternion::SlerpUnclamped_Injected",
                Marshal.GetFunctionPointerForDelegate(SlerpStub));
            mono_add_internal_call(
                "UnityEngine.Quaternion::Lerp_Injected",
                Marshal.GetFunctionPointerForDelegate(SlerpStub));
            mono_add_internal_call(
                "UnityEngine.Quaternion::LerpUnclamped_Injected",
                Marshal.GetFunctionPointerForDelegate(SlerpStub));
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

// Managed counterparts of the engine's quaternion icalls, kept numerically equivalent for tests.
internal static class QuaternionShim
{
    public static void AngleAxis(float angle, ref Vector3 axis, out Quaternion ret)
    {
        float halfRadians = angle * 0.5f * Mathf.Deg2Rad;
        float sin = Mathf.Sin(halfRadians);
        Vector3 normalized = axis.normalized;
        ret = new Quaternion(normalized.x * sin, normalized.y * sin, normalized.z * sin, Mathf.Cos(halfRadians));
    }

    // Unity builds eulers as ZXY: q = qY * qX * qZ.
    public static void FromEulerRad(ref Vector3 euler, out Quaternion ret)
    {
        float cx = Mathf.Cos(euler.x * 0.5f);
        float sx = Mathf.Sin(euler.x * 0.5f);
        float cy = Mathf.Cos(euler.y * 0.5f);
        float sy = Mathf.Sin(euler.y * 0.5f);
        float cz = Mathf.Cos(euler.z * 0.5f);
        float sz = Mathf.Sin(euler.z * 0.5f);
        ret = new Quaternion(
            (cy * sx * cz) + (sy * cx * sz),
            (sy * cx * cz) - (cy * sx * sz),
            (cy * cx * sz) - (sy * sx * cz),
            (cy * cx * cz) + (sy * sx * sz));
    }

    public static void ToEulerRad(ref Quaternion rotation, out Vector3 ret)
    {
        Quaternion q = rotation;
        float sinX = 2f * ((q.w * q.x) + (q.y * q.z));
        float cosX = 1f - (2f * ((q.x * q.x) + (q.y * q.y)));
        float x = Mathf.Atan2(sinX, cosX);
        float sinY = 2f * ((q.w * q.y) - (q.z * q.x));
        float y = Mathf.Abs(sinY) >= 1f
            ? Mathf.Sign(sinY) * Mathf.PI * 0.5f
            : Mathf.Asin(sinY);
        float sinZ = 2f * ((q.w * q.z) + (q.x * q.y));
        float cosZ = 1f - (2f * ((q.z * q.z) + (q.y * q.y)));
        ret = new Vector3(x, y, Mathf.Atan2(sinZ, cosZ));
    }

    public static void ToAxisAngleRad(ref Quaternion rotation, out Vector3 axis, out float angle)
    {
        float sinHalfSquared = Mathf.Max((rotation.x * rotation.x) + (rotation.y * rotation.y) + (rotation.z * rotation.z), 0f);
        float sinHalf = Mathf.Sqrt(sinHalfSquared);
        angle = 2f * Mathf.Atan2(sinHalf, rotation.w);
        axis = sinHalf > 0.0000001f
            ? new Vector3(rotation.x / sinHalf, rotation.y / sinHalf, rotation.z / sinHalf)
            : Vector3.right;
    }

    public static void Inverse(ref Quaternion rotation, out Quaternion ret)
    {
        float normSquared = Mathf.Max(
            (rotation.x * rotation.x) + (rotation.y * rotation.y) + (rotation.z * rotation.z) + (rotation.w * rotation.w),
            0.0000001f);
        ret = new Quaternion(-rotation.x / normSquared, -rotation.y / normSquared, -rotation.z / normSquared, rotation.w / normSquared);
    }

    public static void FromToRotation(ref Vector3 fromDirection, ref Vector3 toDirection, out Quaternion ret)
    {
        Vector3 axis = Vector3.Cross(fromDirection, toDirection);
        float w = Mathf.Sqrt(fromDirection.sqrMagnitude * toDirection.sqrMagnitude) + Vector3.Dot(fromDirection, toDirection);
        if (axis.sqrMagnitude < 0.0000001f && w <= 0f)
        {
            axis = Vector3.Cross(Vector3.right, fromDirection).sqrMagnitude > 0.0000001f
                ? Vector3.Cross(Vector3.right, fromDirection)
                : Vector3.Cross(Vector3.up, fromDirection);
        }

        Quaternion q = new(axis.x, axis.y, axis.z, w);
        float norm = Mathf.Sqrt((q.x * q.x) + (q.y * q.y) + (q.z * q.z) + (q.w * q.w));
        ret = norm > 0.0000001f
            ? new Quaternion(q.x / norm, q.y / norm, q.z / norm, q.w / norm)
            : Quaternion.identity;
    }

    public static void LookRotation(ref Vector3 forward, ref Vector3 upwards, out Quaternion ret)
    {
        Vector3 forwardDirection = forward.normalized;
        Vector3 right = Vector3.Cross(upwards, forwardDirection).normalized;
        Vector3 up = Vector3.Cross(forwardDirection, right);
        float m00 = right.x;
        float m01 = up.x;
        float m02 = forwardDirection.x;
        float m10 = right.y;
        float m11 = up.y;
        float m12 = forwardDirection.y;
        float m20 = right.z;
        float m21 = up.z;
        float m22 = forwardDirection.z;
        float trace = m00 + m11 + m22;
        Quaternion q;
        if (trace > 0f)
        {
            float s = Mathf.Sqrt(trace + 1f) * 2f;
            q = new Quaternion((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, s * 0.25f);
        }
        else if (m00 > m11 && m00 > m22)
        {
            float s = Mathf.Sqrt((1f + m00) - m11 - m22) * 2f;
            q = new Quaternion(s * 0.25f, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s);
        }
        else if (m11 > m22)
        {
            float s = Mathf.Sqrt((1f + m11) - m00 - m22) * 2f;
            q = new Quaternion((m01 + m10) / s, s * 0.25f, (m12 + m21) / s, (m02 - m20) / s);
        }
        else
        {
            float s = Mathf.Sqrt((1f + m22) - m00 - m11) * 2f;
            q = new Quaternion((m02 + m20) / s, (m12 + m21) / s, s * 0.25f, (m10 - m01) / s);
        }

        ret = Quaternion.Normalize(q);
    }

    public static void Slerp(ref Quaternion a, ref Quaternion b, float t, out Quaternion ret)
    {
        float dot = (a.x * b.x) + (a.y * b.y) + (a.z * b.z) + (a.w * b.w);
        float sign = dot < 0f ? -1f : 1f;
        float absDot = Mathf.Min(Mathf.Abs(dot), 1f);
        float scaleA;
        float scaleB;
        if (absDot > 0.9995f)
        {
            scaleA = 1f - t;
            scaleB = t * sign;
        }
        else
        {
            float angle = Mathf.Acos(absDot);
            float sinAngle = Mathf.Sin(angle);
            scaleA = Mathf.Sin((1f - t) * angle) / sinAngle;
            scaleB = Mathf.Sin(t * angle) / sinAngle * sign;
        }

        Quaternion q = new(
            (a.x * scaleA) + (b.x * scaleB),
            (a.y * scaleA) + (b.y * scaleB),
            (a.z * scaleA) + (b.z * scaleB),
            (a.w * scaleA) + (b.w * scaleB));
        ret = Quaternion.Normalize(q);
    }
}
