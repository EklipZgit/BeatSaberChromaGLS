using System;
using System.Reflection;
using NUnit.Framework;

namespace ChromaGLS.Tests;

[TestFixture]
public class MenuLightReflectionLayoutTests
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static Assembly Plugin => Assembly.Load("ChromaGLS");

    [TestCase(0, 8)]
    [TestCase(1024, 8)]
    [TestCase(4096, 8)]
    [TestCase(8192, 5)]
    [TestCase(16384, 1)]
    public void BankCapacityKeepsEveryArrayUnderTheGuaranteedBound(int resolution, int expected)
    {
        MethodInfo? method = RequiredType().GetMethod("GetSlicesPerArray", Static);
        Assert.That(method, Is.Not.Null, "The reflection cache must expose the bank capacity policy.");
        Assert.That(method!.Invoke(null, new object[] { resolution }), Is.EqualTo(expected));
    }

    private static Type RequiredType()
    {
        Type? type = Plugin.GetType("ChromaGLS.Settings.MenuLightReflectionCache");
        Assert.That(type, Is.Not.Null, "The reflection cache must expose the compiled bank layout.");
        return type!;
    }
}
