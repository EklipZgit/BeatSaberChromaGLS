using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class GlsSplineHandlerTests
{
    private const BindingFlags All =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private readonly GlsEasingTests lifecycle = new();
    private Harmony services = null!;
    private Type translationType = null!;
    private Type eventType = null!;
    private Type tweenType = null!;

    [SetUp]
    public void SetUp()
    {
        lifecycle.SetUp();
        services = new Harmony("ChromaGLS.Tests.SplineServices");
        translationType = AccessTools.TypeByName("LightTranslationGroupEffect");
        eventType = AccessTools.TypeByName("LightTranslationBeatmapEventData");
        tweenType = Assembly.Load("ChromaGLS").GetType("ChromaGLS.Helpers.GlsFloatTween", true)!;
        Type manager = AccessTools.TypeByName("Tweening.TweeningManager");
        services.Patch(
            manager.GetMethod("ResumeTween", All),
            prefix: new HarmonyMethod(typeof(GlsSplineHandlerTests), nameof(SkipService)));
        services.Patch(
            manager.GetMethod("KillAllTweens", All),
            prefix: new HarmonyMethod(typeof(GlsSplineHandlerTests), nameof(SkipService)));
    }

    [TearDown]
    public void TearDown()
    {
        services?.UnpatchSelf();
        lifecycle.TearDown();
    }

    [TestCase(103)]
    [TestCase(104)]
    public void TranslationHandlerPreparesGeneratedSpline(int easing)
    {
        object effect = NewEffect();
        object tween = NewTween();
        object[] nodes =
        [
            NewEvent(0f, 0f, easing),
            NewEvent(1f, 10f, easing),
            NewEvent(2.5f, 20f, easing),
            NewEvent(5f, 0f, easing),
        ];
        Connect(nodes);
        Delegate handler = (Delegate)translationType.GetMethod("GetTranslationEventHandler", All)!
            .Invoke(effect, [tween, new Vector2(-1f, 1f), new Vector2(-1f, 1f), false])!;
        handler.DynamicInvoke(nodes[1]);
        float actual = (float)tweenType.GetMethod("GetValue", All)!.Invoke(tween, [0.5f])!;
        Assert.That(actual, Is.EqualTo(16.96875f).Within(0.00001f),
            "The native event callback must prepare the spline, not merely retain a linear easing marker.");
    }

    private object NewEffect()
    {
        object effect = TestHostCompat.GetUninitializedObject(translationType);
        FieldInfo masks = translationType.GetField("_transformMask", All)!;
        masks.SetValue(effect, Activator.CreateInstance(masks.FieldType));
        FieldInfo manager = translationType.GetField("_tweeningManager", All)!;
        manager.SetValue(effect, TestHostCompat.GetUninitializedObject(manager.FieldType));
        foreach (string axis in new[] { "_xTranslationTween", "_yTranslationTween", "_zTranslationTween" })
        {
            translationType.GetField(axis, All)!.SetValue(effect, NewTween());
        }
        return effect;
    }

    private object NewTween()
    {
        ConstructorInfo constructor = tweenType.GetConstructors().Single(c => c.GetParameters().Length == 6);
        Type easing = constructor.GetParameters()[4].ParameterType;
        return constructor.Invoke([0f, 0f, (Action<float>)(_ => { }), 0f, Enum.Parse(easing, "Linear"), 0f]);
    }

    private object NewEvent(float time, float value, int easing)
    {
        ConstructorInfo constructor = eventType.GetConstructors().Single();
        return constructor.Invoke(constructor.GetParameters().Select(parameter => parameter.Name switch
        {
            "time" => (object)time,
            "translation" => value,
            "easeType" => Enum.ToObject(parameter.ParameterType, easing),
            _ => Activator.CreateInstance(parameter.ParameterType),
        }).ToArray());
    }

    private static void Connect(object[] nodes)
    {
        MethodInfo connect = nodes[0].GetType().GetMethod("__ConnectWithNextSameTypeEventData", All)!;
        for (int i = 0; i < nodes.Length - 1; i++)
        {
            connect.Invoke(nodes[i], [nodes[i + 1]]);
        }
    }

    private static bool SkipService() => false;
}
