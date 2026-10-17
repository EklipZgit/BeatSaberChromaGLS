using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

// DispatchProxy emits a dynamic assembly named ProxyBuilder; Mono enforces member visibility strictly,
// so the generated proxy cannot call an internal test proxy's constructor without this grant.
[assembly: InternalsVisibleTo("ProxyBuilder")]

namespace ChromaGLS.Tests;

// The net48 host lacks RuntimeHelpers.GetUninitializedObject and the two-argument DispatchProxy.Create
// overload; these shims keep the shared test sources compiling on the runtime Unity's Mono understands.
internal static class TestHostCompat
{
    public static object GetUninitializedObject(Type type)
    {
        return FormatterServices.GetUninitializedObject(type);
    }

    public static object CreateProxy(Type interfaceType, Type proxyType)
    {
        return typeof(System.Reflection.DispatchProxy)
            .GetMethod(nameof(System.Reflection.DispatchProxy.Create))!
            .MakeGenericMethod(interfaceType, proxyType)
            .Invoke(null, null)!;
    }
}
