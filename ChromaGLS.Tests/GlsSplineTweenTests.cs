using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using NUnit.Framework;

namespace ChromaGLS.Tests
{
    // Scalar Catmull-Rom tween behavior: nodes values [0,10,20,0] at beats [0,1,2.5,5] must produce
    // ChroMapper's uniform spline midpoint 16.96875 at beat 1.75, while the native tween for the
    // same easing marker keeps its linear fallback.
    [NonParallelizable]
    internal sealed class GlsSplineTweenTests
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        private static Type Patch(string name) =>
            Assembly.Load("ChromaGLS").GetType("ChromaGLS.HarmonyPatches." + name, true)!;

        private static Type Tween() => Assembly.Load("ChromaGLS").GetType("ChromaGLS.Helpers.GlsFloatTween", true)!;

        private static void SetEnabled(bool enabled) =>
            Assembly.Load("ChromaGLS").GetType("ChromaGLS.Helpers.GlsSplineRuntime")!
                .GetProperty("Enabled", All)!.SetValue(null, enabled);

        private bool _savedEnabled;

        [SetUp]
        public void SaveEnabled() =>
            _savedEnabled = (bool)Assembly.Load("ChromaGLS").GetType("ChromaGLS.Helpers.GlsSplineRuntime")!
                .GetProperty("Enabled", All)!.GetValue(null)!;

        [TearDown]
        public void RestoreEnabled() => SetEnabled(_savedEnabled);

        private static object NewNativeTween(float from, float to, object ease)
        {
            Type type = Tween().BaseType;
            ConstructorInfo ctor = type.GetConstructors().Single(c => c.GetParameters().Length == 6)!;
            return ctor.Invoke([from, to, (Action<float>)(_ => { }), 1.5f, ease, 0f]);
        }

        private static float Sample(object tween, float normalized)
        {
            return (float)tween.GetType().GetMethod("GetValue", All)!.Invoke(tween, [normalized])!;
        }

        [Test]
        public void CatmullRomTweenSamplesSplineNotLinear()
        {
            Type tweenType = Tween().BaseType;
            Type easeType = tweenType.GetConstructors().Single(c => c.GetParameters().Length == 6)!
                .GetParameters()[4].ParameterType;
            var ease = Enum.ToObject(easeType, 0);
            // Sanity: an unused but real ease keeps the native path honest.
            object linear = NewNativeTween(10f, 20f, ease);
            Assert.That(Sample(linear, 0.5f), Is.EqualTo(15f).Within(1e-4), "native tween stays linear");

            object spline = Tween().GetConstructor(Type.EmptyTypes)!.Invoke(null);
            // Fill the same inner values then stage the four-point controls the handler prepares.
            spline.GetType().GetField("fromValue", All)!.SetValue(spline, 10f);
            spline.GetType().GetField("toValue", All)!.SetValue(spline, 20f);
            SetEnabled(true);
            spline.GetType().GetMethod("SetSpline", All)!.Invoke(
                spline, [0f, 0f, 10f, 1f, 20f, 2.5f, 0f, 5f]);

            Assert.That(Sample(spline, 0.5f), Is.EqualTo(16.96875f).Within(1e-4));
        }

        [Test]
        public void DisabledRuntimeFallsBackToNative()
        {
            object spline = Tween().GetConstructor(Type.EmptyTypes)!.Invoke(null);
            spline.GetType().GetField("fromValue", All)!.SetValue(spline, 10f);
            spline.GetType().GetField("toValue", All)!.SetValue(spline, 20f);
            spline.GetType().GetMethod("SetSpline", All)!.Invoke(
                spline, [0f, 0f, 10f, 1f, 20f, 2.5f, 0f, 5f]);
            SetEnabled(false);
            Assert.That(Sample(spline, 0.5f), Is.EqualTo(15f).Within(1e-4), "disabled tween behaves native");
        }
    }
}
