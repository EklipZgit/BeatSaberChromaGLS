using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class FerrofluidMagnetBulgeTests
{
    private static Type GetPluginType(string typeName)
    {
        return Assembly.Load("ChromaGLS").GetType(typeName, throwOnError: false)
            ?? throw new AssertionException($"Missing plugin type: {typeName}");
    }

    private static Type Deformation => GetPluginType("ChromaGLS.Settings.FerrofluidPrimitiveDeformation");
    private static Type EnvironmentObjects => GetPluginType("ChromaGLS.Settings.FerrofluidEnvironmentObjects");
    private static Type ShapeEnum => GetPluginType("ChromaGLS.Settings.FerrofluidSurfaceShape");

    private static object Settings(float falloff, object shape, float sourceWorldScale = 1f)
    {
        object config = Activator.CreateInstance(GetPluginType("ChromaGLS.Settings.Config"), true)!;
        config.GetType().GetProperty("BulgeRadius")!.SetValue(config, 0.31f);
        config.GetType().GetProperty("BulgeHeight")!.SetValue(config, 0.12f);
        config.GetType().GetProperty("MagnetHeightDeadZoneRadius")!.SetValue(config, 0.13f);
        config.GetType().GetProperty("MagnetHeightStrengthCap")!.SetValue(config, 0.74f);
        config.GetType().GetProperty("FerrofluidSphereBulgeScale")!.SetValue(config, 0.6f);
        object settings = Deformation.GetMethod("EvaluateSettings", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [shape, config, sourceWorldScale])!;
        settings.GetType().GetField("HeightFalloffExponent", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(settings, falloff);
        return settings;
    }

    private static Vector3 Offset(object settings, object shape, Vector4 site, Vector3 magnet, float strength, float amp = 1f)
    {
        MethodInfo method = Deformation.GetMethod(
            "EvaluateSiteBulgeOffset",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (Vector3)method.Invoke(null, [settings, shape, site, magnet, strength, amp])!;
    }

    private static Vector3 Surface(object shape, Vector3 direction)
    {
        MethodInfo method = EnvironmentObjects.GetMethod(
            "EvaluateSurfacePositionFromDirection",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (Vector3)method.Invoke(null, [shape, direction, 0.48f])!;
    }

    private static Vector3 Normal(object shape, Vector3 direction)
    {
        MethodInfo method = EnvironmentObjects.GetMethod(
            "EvaluateSurfaceNormalFromDirection",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (Vector3)method.Invoke(null, [shape, direction, 0.48f])!;
    }

    private static object Sphere => Enum.ToObject(ShapeEnum, 2);
    private static object RoundedCube => Enum.ToObject(ShapeEnum, 1);

    private static float HeightAt(object settings, object shape, Vector3 basePosition, Vector3 magnet, float strength, float amp = 1f)
    {
        Vector3 offset = Offset(settings, shape, basePosition, magnet, strength, amp);
        Vector3 normal = Normal(shape, basePosition.normalized);
        return Vector3.Dot(offset, normal);
    }

    [Test]
    public void BulgeCrownCurvesQuadratically()
    {
        Vector3 magnet = new(0f, 0.64f, 0f);
        foreach (object shape in new[] { Sphere, RoundedCube })
        {
            object settings = Settings(2.4f, shape);
            float cap = (float)settings.GetType().GetField("HeightStrengthCap", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
            float scale = (float)settings.GetType().GetField("SphereBulgeScale", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
            float height = (float)settings.GetType().GetField("BulgeHeight", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
            float h0 = HeightAt(settings, shape, Surface(shape, Vector3.up), magnet, cap);
            float hNear = HeightAt(settings, shape, Surface(shape, new Vector3(Mathf.Sin(0.02f), Mathf.Cos(0.02f), 0f)), magnet, cap);
            float hFar = HeightAt(settings, shape, Surface(shape, new Vector3(Mathf.Sin(0.04f), Mathf.Cos(0.04f), 0f)), magnet, cap);
            float dSmall = h0 - hNear;
            float dLarge = h0 - hFar;
            Assert.That(dSmall, Is.GreaterThan(0.0005f * h0), $"the crown must curve away from the centre, not plateau ({shape} dSmall={dSmall:F5} h0={h0:F4})");
            Assert.That(dLarge / dSmall, Is.InRange(3f, 5f), $"the dome deficit must be quadratic ({shape} ratio={dLarge / dSmall:F2})");
            float expected = height * cap * (Convert.ToInt32(shape) == 2 ? scale : 1f)
                * Mathf.Exp(-0.5f * 2.4f * (0.14f * 0.14f) / (0.31f * 0.31f));
            Assert.That(h0, Is.EqualTo(expected).Within(expected * 0.01f), $"the centre keeps the physical-distance peak ({shape} {h0:F4} expected {expected:F4})");
        }
    }

    [Test]
    public void BulgeSkirtExtendsPastOriginalFootprint()
    {
        Vector3 magnet = new(0f, 0.64f, 0f);
        foreach (object shape in new[] { Sphere, RoundedCube })
        {
            object settings = Settings(2.4f, shape);
            float cap = (float)settings.GetType().GetField("HeightStrengthCap", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
            Vector3 beyond = Surface(shape, new Vector3(Mathf.Sin(0.8f), Mathf.Cos(0.8f), 0f));
            Assert.That(Vector3.Distance(magnet, beyond), Is.GreaterThan(0.13f + 0.31f), $"the probe must sit outside the original radius ({shape})");
            float h0 = HeightAt(settings, shape, Surface(shape, Vector3.up), magnet, cap);
            float skirt = HeightAt(settings, shape, beyond, magnet, cap);
            Assert.That(skirt, Is.GreaterThan(0.005f * h0), $"the skirt must continue outward instead of cutting at the original radius ({shape} {skirt:F5})");
        }
    }

    [Test]
    public void BulgeFlankSlopeStaysShallowerThanOriginal()
    {
        Vector3 magnet = new(0f, 0.64f, 0f);
        foreach (object shape in new[] { Sphere, RoundedCube })
        {
            object settings = Settings(2.4f, shape);
            float cap = (float)settings.GetType().GetField("HeightStrengthCap", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
            float scale = (float)settings.GetType().GetField("SphereBulgeScale", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
            float height = (float)settings.GetType().GetField("BulgeHeight", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
            float amplitude = height * cap * (Convert.ToInt32(shape) == 2 ? scale : 1f);
            float maxSlope = 0f;
            Vector3 previous = Surface(shape, Vector3.up);
            float previousH = HeightAt(settings, shape, previous, magnet, cap);
            for (int index = 1; index <= 512; index++)
            {
                float theta = index * (Mathf.PI / 2f) / 512f;
                Vector3 current = Surface(shape, new Vector3(Mathf.Sin(theta), Mathf.Cos(theta), 0f));
                float currentH = HeightAt(settings, shape, current, magnet, cap);
                float slope = Mathf.Abs(currentH - previousH) / Vector3.Distance(current, previous);
                maxSlope = Mathf.Max(maxSlope, slope);
                previous = current;
                previousH = currentH;
            }

            float bulgeRadius = (float)settings.GetType().GetField("BulgeRadius", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
            float normalized = maxSlope * bulgeRadius / amplitude;
            Assert.That(normalized, Is.LessThanOrEqualTo(1.05f), $"the flank must stay gentle, not ring-steep ({shape} normalized={normalized:F3})");
        }
    }

    [Test]
    public void BulgeKeepsLinearStrengthCapAndAmplification()
    {
        object settings = Settings(2.4f, Sphere);
        Vector3 magnet = new(0f, 0.64f, 0f);
        Vector3 centre = Surface(Sphere, Vector3.up);
        float atHalf = HeightAt(settings, Sphere, centre, magnet, 0.2f);
        float atFull = HeightAt(settings, Sphere, centre, magnet, 0.4f);
        float cap = (float)settings.GetType().GetField("HeightStrengthCap", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
        float atCapped = HeightAt(settings, Sphere, centre, magnet, 3f);
        float atCap = HeightAt(settings, Sphere, centre, magnet, cap);
        Assert.That(atFull, Is.EqualTo(atHalf * 2f).Within(atFull * 0.01f), "strength stays linear");
        Assert.That(atCapped, Is.EqualTo(atCap).Within(0.0001f), "the strength cap stays unchanged");
        float amplified = HeightAt(settings, Sphere, centre, magnet, 0.4f, 1.6f);
        Assert.That(amplified, Is.EqualTo(atFull * 1.6f).Within(atFull * 0.01f), "height amplification stays a constant multiplier");
        float sphereScale = HeightAt(settings, Sphere, centre, magnet, 0.4f);
        object cubeSettings = Settings(2.4f, RoundedCube);
        float cubeScale = HeightAt(cubeSettings, RoundedCube, Surface(RoundedCube, Vector3.up), magnet, 0.4f);
        float sphereFactor = (float)settings.GetType().GetField("SphereBulgeScale", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
        Assert.That(sphereScale, Is.EqualTo(cubeScale * sphereFactor).Within(sphereScale * 0.02f), "the sphere amplitude scale is preserved");
    }

    [Test]
    public void PrimitiveBulgeHeightStaysHalfOfConfigured()
    {
        foreach ((float scale, float expected) in new[] { (0.5f, 0.03f), (1f, 0.06f), (2f, 0.12f) })
        {
            foreach (object shape in new[] { RoundedCube, Sphere })
            {
                object settings = Settings(2.4f, shape, scale);
                float height = (float)settings.GetType().GetField("BulgeHeight", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
                Assert.That(height, Is.EqualTo(expected).Within(0.0001f), $"primitive {shape} scale {scale} keeps half the configured height");
            }
        }

        object torusSettings = Settings(2.4f, Enum.ToObject(ShapeEnum, 0));
        float torusHeight = (float)torusSettings.GetType().GetField("BulgeHeight", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(torusSettings)!;
        Assert.That(torusHeight, Is.EqualTo(0.12f).Within(0.0001f), "the torus keeps its configured height");
    }

    [Test]
    public void BulgeTerminusSlopeGoesToZero()
    {
        object settings = Settings(2.4f, Sphere);
        Vector3 magnet = new(0f, 0.64f, 0f);
        float radius = Surface(Sphere, Vector3.up).magnitude;
        float support = 0.9f * (0.13f + (1.5f * 0.31f));
        float theta = Mathf.Acos(((0.64f * 0.64f) + (radius * radius) - (support * support)) / (2f * 0.64f * radius));
        Assert.That(
            HeightAt(settings, Sphere, Surface(Sphere, new Vector3(Mathf.Sin(theta - 0.02f), Mathf.Cos(theta - 0.02f), 0f)), magnet, 0.8f),
            Is.GreaterThan(0f),
            "the probe must sit inside the widened support, not past it");
        foreach (float eps in new[] { 0.005f, 0.0025f })
        {
            Vector3 justInside = Surface(Sphere, new Vector3(Mathf.Sin(theta - eps), Mathf.Cos(theta - eps), 0f));
            Vector3 atCut = Surface(Sphere, new Vector3(Mathf.Sin(theta), Mathf.Cos(theta), 0f));
            Vector3 outside = Surface(Sphere, new Vector3(Mathf.Sin(theta + eps), Mathf.Cos(theta + eps), 0f));
            float inside = HeightAt(settings, Sphere, justInside, magnet, 0.8f);
            float at = HeightAt(settings, Sphere, atCut, magnet, 0.8f);
            float beyond = HeightAt(settings, Sphere, outside, magnet, 0.8f);
            Assert.That(beyond, Is.EqualTo(0f).Within(0.0001f), "the back of the terminator keeps zero bulge");
            Assert.That(at, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(
                (inside - at) / eps,
                Is.LessThan(0.001f),
                $"the terminus slope must vanish ({(inside - at) / eps:F4} at eps {eps})");
        }
    }

    [Test]
    public void BulgeFrontHasOneRadialProfile()
    {
        foreach (float gap in new[] { 0.07f, 0.14f })
        {
            foreach (object shape in new[] { Sphere, RoundedCube })
            {
                object settings = Settings(2.4f, shape);
                float cap = (float)settings.GetType().GetField("HeightStrengthCap", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
                Vector3 anchor = Surface(shape, Vector3.up);
                Vector3 magnet = anchor + (Vector3.up * gap);
                float h0 = HeightAt(settings, shape, anchor, magnet, cap);
                float outer = 0.9f * ((0.31f * 1.5f) + 0.13f);
                float kMin = float.MaxValue;
                float kMax = float.MinValue;
                foreach (float theta in new[] { 0.1f, 0.2f, 0.3f })
                {
                    Vector3 position = Surface(shape, new Vector3(Mathf.Sin(theta), Mathf.Cos(theta), 0f));
                    Vector3 delta = magnet - position;
                    Assert.That(Vector3.Dot(Normal(shape, position.normalized), delta), Is.GreaterThan(0f), $"probe must stay front-facing ({shape} {gap} {theta})");
                    Assert.That(delta.magnitude, Is.LessThan(0.75f * outer), $"probe must sit inside the feather ({shape} {gap} {theta})");
                    float h = HeightAt(settings, shape, position, magnet, cap);
                    float k = -Mathf.Log(h / h0) / (delta.sqrMagnitude - (gap * gap));
                    Assert.That(k, Is.GreaterThan(0f), $"a single radial profile keeps k positive ({shape} {gap} {theta} k={k:F5})");
                    kMin = Mathf.Min(kMin, k);
                    kMax = Mathf.Max(kMax, k);
                }

                Assert.That(kMax / kMin, Is.LessThanOrEqualTo(1.02f), $"the front profile is one coherent radial bell, not a facing-driven crest ({shape} {gap} {kMin:F4}..{kMax:F4})");
            }
        }
    }

    [Test]
    public void BulgeSupportIsTenPercentNarrower()
    {
        object settings = Settings(2.4f, Sphere);
        Vector3 magnet = new(0f, 0.57f, 0f);
        float radius = Surface(Sphere, Vector3.up).magnitude;
        float oldSupport = 0.13f + (1.5f * 0.31f);
        float newSupport = 0.9f * oldSupport;
        Vector3 At(float distance)
        {
            float theta = Mathf.Acos(((0.57f * 0.57f) + (radius * radius) - (distance * distance)) / (2f * 0.57f * radius));
            return Surface(Sphere, new Vector3(Mathf.Sin(theta), Mathf.Cos(theta), 0f));
        }
        float insideNew = HeightAt(settings, Sphere, At(newSupport - 0.01f), magnet, 0.74f);
        float pastNew = HeightAt(settings, Sphere, At(newSupport + 0.001f), magnet, 0.74f);
        float atOld = HeightAt(settings, Sphere, At(oldSupport - 0.01f), magnet, 0.74f);
        Assert.That(insideNew, Is.GreaterThan(0f), "the new support still bulges just inside its boundary");
        Assert.That(pastNew, Is.EqualTo(0f).Within(0.00000001f), "the dome ends at the new boundary");
        Assert.That(atOld, Is.EqualTo(0f).Within(0.00000001f), "the old support radius is already feathered out");
    }

    [Test]
    public void BulgeStaysZeroOutsideField()
    {
        object settings = Settings(2.4f, Sphere);
        Vector3 magnet = new(0f, 0.64f, 0f);
        Vector3 away = Surface(Sphere, new Vector3(0f, -1f, 0f));
        Assert.That(HeightAt(settings, Sphere, away, magnet, 0.8f), Is.EqualTo(0f).Within(0.0001f), "points beyond the radius get nothing");
        Assert.That(HeightAt(settings, Sphere, Surface(Sphere, Vector3.up), magnet, 0f), Is.EqualTo(0f).Within(0.0001f), "zero field means zero bulge");
    }
}
