using System;
using System.Collections;
using System.Linq.Expressions;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
public class MenuLightTests
{
    private static Type TypeOf(string name) => System.Reflection.Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings." + name, true)!;

    private static object NewDefinition(string type)
    {
        Type def = TypeOf("MenuLightDefinition");
        object instance = Activator.CreateInstance(def, nonPublic: true)!;
        def.GetProperty("Type")!.SetValue(instance, Enum.Parse(TypeOf("MenuLightType"), type));
        return instance;
    }

    private static object NewStore(object? config = null)
    {
        config ??= Activator.CreateInstance(TypeOf("Config"), nonPublic: true)!;
        return Activator.CreateInstance(TypeOf("MenuLightStore"), BindingFlags.Instance | BindingFlags.NonPublic, null, [config], null)!;
    }

    private static object CreateDefault(object store, string type)
        => store.GetType().GetMethod("CreateDefault", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(store, [Enum.Parse(TypeOf("MenuLightType"), type)])!;

    private static object NewSession(object store)
    {
        Type session = TypeOf("MenuLightEditSession");
        ParameterInfo[] parameters = session.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)[0].GetParameters();
        object[] delegates = new object[3];
        for (int index = 0; index < 3; index++)
        {
            Type delegateType = parameters[index + 1].ParameterType;
            ParameterExpression param = Expression.Parameter(delegateType.GetMethod("Invoke")!.GetParameters()[0].ParameterType, "arg");
            delegates[index] = Expression.Lambda(delegateType, Expression.Empty(), param).Compile();
        }

        return Activator.CreateInstance(session, BindingFlags.Instance | BindingFlags.NonPublic, null, [store, delegates[0], delegates[1], delegates[2]], null)!;
    }

    private static object Field(object instance, string name)
        => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static object Prop(object instance, string name)
        => instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static object Invoke(object instance, string name, params object[] arguments)
        => instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(instance, arguments)!;

    [Test]
    public void StoreCreateDefaultProducesCommonDefaultsForAllFiveTypes()
    {
        object store = NewStore();
        foreach (string type in new[] { "Parabola", "Square", "Cube", "Sphere", "Circle" })
        {
            object def = CreateDefault(store, type);
            Assert.That((string)Prop(def, "Id")!, Is.Not.Empty, $"{type} must generate a stable id");
            Assert.That((bool)Prop(def, "Enabled")!, Is.True);
            Assert.That((float)Prop(def, "Height")!, Is.EqualTo(0.006f).Within(0.0001f));
            Assert.That((float)Prop(def, "Depth")!, Is.EqualTo(0.02f).Within(0.0001f));
            Assert.That((float)Prop(def, "Size")!, Is.EqualTo(1f).Within(0.0001f));
            Assert.That((float)Prop(def, "QuaternionW")!, Is.EqualTo(1f).Within(0.0001f));
            Assert.That((float)Prop(def, "FadeStart")!, Is.EqualTo(0.45f).Within(0.0001f));
        }
    }

    [Test]
    public void StoreUpsertPersistsJsonAndRemoveAllowsZeroLights()
    {
        object config = Activator.CreateInstance(TypeOf("Config"), nonPublic: true)!;
        config.GetType().GetProperty("MenuLightsJson")!.SetValue(config, "[]");
        object store = NewStore(config);
        object def = CreateDefault(store, "Square");
        Invoke(store, "Upsert", def);
        string json = (string)TypeOf("Config").GetProperty("MenuLightsJson")!.GetValue(config)!;
        Assert.That(json, Does.Contain((string)Prop(def, "Id")!), "upsert must persist the serialized definition");

        Invoke(store, "Remove", Prop(def, "Id"));
        json = (string)TypeOf("Config").GetProperty("MenuLightsJson")!.GetValue(config)!;
        Assert.That(json.Trim(), Is.EqualTo("[]"), "removing the last light must persist an empty list, not resurrect migration");
    }

    [Test]
    public void StoreRejectsANinthLight()
    {
        object config = Activator.CreateInstance(TypeOf("Config"), nonPublic: true)!;
        config.GetType().GetProperty("MenuLightsJson")!.SetValue(config, "[]");
        object store = NewStore(config);
        for (int index = 0; index < 8; index++)
            Invoke(store, "Upsert", CreateDefault(store, "Cube"));
        object ninth = CreateDefault(store, "Cube");
        Assert.That(() => Invoke(store, "Upsert", ninth), Throws.Exception, "the store must cap at eight lights");
    }

    [Test]
    public void LegacyMigrationExtractsOffsetsYawAndZeroesAppearance()
    {
        object config = Activator.CreateInstance(TypeOf("Config"), nonPublic: true)!;
        config.GetType().GetProperty("ParabolicFloorLightOffsetX")!.SetValue(config, 0.7f);
        config.GetType().GetProperty("ParabolicFloorLightOffsetZ")!.SetValue(config, -0.4f);
        config.GetType().GetProperty("ParabolicFloorLightRotation")!.SetValue(config, 37f);
        object store = NewStore(config);
        IList lights = (IList)Prop(store, "Lights")!;
        Assert.That(lights.Count, Is.EqualTo(1), "empty MenuLightsJson must migrate the legacy parabola");
        object migrated = lights[0]!;
        Assert.That((float)Prop(migrated, "PositionX")!, Is.EqualTo(0.7f).Within(0.001f));
        Assert.That((float)Prop(migrated, "PositionZ")!, Is.EqualTo(-0.4f).Within(0.001f));
        object appearance = Prop(migrated, "Appearance")!;
        Assert.That((float)Field(appearance, "OffsetX"), Is.EqualTo(0f).Within(0.0001f), "the migrated transform must live on the definition, not the appearance");
        Assert.That((float)Field(appearance, "Rotation"), Is.EqualTo(0f).Within(0.0001f));
    }

    [Test]
    public void EditSessionOpenNewPreviewSaveCancelFlow()
    {
        object config = Activator.CreateInstance(TypeOf("Config"), nonPublic: true)!;
        config.GetType().GetProperty("MenuLightsJson")!.SetValue(config, "[]");
        object store = NewStore(config);
        object session = NewSession(store);
        Invoke(session, "OpenNew", Enum.Parse(TypeOf("MenuLightType"), "Square"));
        Assert.That((bool)Prop(session, "IsEditing")!, Is.True);
        Assert.That((bool)Prop(session, "IsNew")!, Is.True);
        object working = Prop(session, "Working")!;
        Prop(working, "Size").GetType(); // warm the accessor
        Type editDelegateType = session.GetType().GetMethod("Preview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetParameters()[0].ParameterType;
        Action<object> resize = definition => definition.GetType().GetProperty("Size")!.SetValue(definition, 2f);
        Invoke(session, "Preview", Delegate.CreateDelegate(editDelegateType, resize.Target!, resize.Method));
        Invoke(session, "Save");
        Assert.That((bool)Prop(session, "IsEditing")!, Is.False);
        Assert.That(((IList)Prop(store, "Lights")!).Count, Is.EqualTo(1));
        Invoke(session, "OpenExisting", Prop(((IList)Prop(store, "Lights")!)[0]!, "Id"));
        Invoke(session, "Cancel");
        Assert.That((bool)Prop(session, "IsEditing")!, Is.False);
    }

    [Test]
    public void RotateAxisAppliesLocalDeltaAndGetRotationNormalizes()
    {
        object def = CreateDefault(NewStore(), "Parabola");
        Invoke(def, "RotateAxis", 1, 90f);
        Quaternion rotation = (Quaternion)Invoke(def, "GetRotation");
        Assert.That(Mathf.Abs(Quaternion.Angle(rotation, Quaternion.Euler(0f, 90f, 0f))), Is.LessThan(1f));
        Invoke(def, "RotateAxis", 1, 180f);
        rotation = (Quaternion)Invoke(def, "GetRotation");
        Assert.That(Mathf.Abs(Quaternion.Angle(rotation, Quaternion.Euler(0f, 180f, 0f))), Is.LessThan(1f), "the second call applies only the 90-degree delta");
    }

    [Test]
    public void GeometryTryCreateProducesMeshesForAllFiveTypes()
    {
        Type geometry = TypeOf("MenuLightGeometry");
        MethodInfo tryCreate = geometry.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .FirstOrDefault(method => method.Name == "TryCreate" && method.GetParameters().Length == 5);
        Assert.That(tryCreate, Is.Not.Null, "MenuLightGeometry.TryCreate must exist");
        object enclosure = DefaultEnclosure();
        foreach (string type in new[] { "Parabola", "Square", "Cube", "Sphere", "Circle" })
        {
            object def = CreateDefault(NewStore(), type);
            object?[] arguments = [enclosure, Vector3.zero, Vector3.forward, def, null];
            object result = tryCreate!.Invoke(null, arguments)!;
            Assert.That(result, Is.EqualTo(true), $"{type} must produce a mesh bundle");
            object meshes = arguments[4]!;
            Assert.That(meshes, Is.Not.Null);
            Vector3[] coreVerts = (Vector3[])Field(Field(meshes, "Core"), "Vertices")!;
            Assert.That(coreVerts.Length, Is.GreaterThan(0), $"{type} must emit core vertices");
            foreach (Vector3 vertex in coreVerts)
            {
                Assert.That(!float.IsNaN(vertex.x) && !float.IsInfinity(vertex.x), Is.True, $"{type} vertex x must be finite");
                Assert.That(!float.IsNaN(vertex.y) && !float.IsInfinity(vertex.y), Is.True);
                Assert.That(!float.IsNaN(vertex.z) && !float.IsInfinity(vertex.z), Is.True);
            }

            int[] triangles = (int[])Field(Field(meshes, "Core"), "Triangles")!;
            foreach (int index in triangles)
                Assert.That(index, Is.InRange(0, coreVerts.Length - 1), $"{type} triangle index out of range");
        }
    }

    private static object DefaultEnclosure()
    {
        Type type = TypeOf("FerrofluidGrabEnclosure");
        object instance = Activator.CreateInstance(type, nonPublic: true)!;
        instance.GetType().GetField("Radius", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(instance, 2f);
        instance.GetType().GetField("FloorY", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(instance, 0f);
        return instance!;
    }
}
