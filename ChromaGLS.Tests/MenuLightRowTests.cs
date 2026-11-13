using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;

namespace ChromaGLS.Tests;

[TestFixture]
public class MenuLightRowTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static Type TypeOf(string name) => Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings." + name, true)!;
    private static object New(string type, params object[] args) => Activator.CreateInstance(TypeOf(type), Flags, null, args, null)!;
    private static object Def(string id) { object d = New("MenuLightDefinition"); d.GetType().GetProperty("Id", Flags)!.SetValue(d, id); return d; }
    private static object CloneOf(object def) => def.GetType().GetMethod("Clone", Flags)!.Invoke(def, null)!;
    private static object NewConfig() { object c = New("Config"); c.GetType().GetProperty("MenuLightsJson", Flags)!.SetValue(c, "[]"); return c; }
    private static object NewLight(object config) => New("ParabolicFloorLight", config);
    private static object StoreOf(object light) => light.GetType().GetProperty("Store", Flags)!.GetValue(light)!;
    private static object Lights(object store) => store.GetType().GetProperty("Lights", Flags)!.GetValue(store)!;
    private static void Upsert(object store, object def) => store.GetType().GetMethod("Upsert", Flags)!.Invoke(store, new[] { def });
    private static object Row(object def, int index, Action<string?> open, object store, object light, Action removed) => New("MenuLightRow", def, index, open, store, light, removed);

    [Test]
    public void EnabledSetterPersistsTheSavedDefinition()
    {
        object config = NewConfig();
        object light = NewLight(config);
        object store = StoreOf(light);
        object definition = Def("a");
        Upsert(store, definition);
        var removed = 0;
        object row = Row(CloneOf(definition), 0, _ => { }, store, light, () => removed++);
        row.GetType().GetProperty("Enabled", Flags)!.SetValue(row, false);
        object saved = Lights(New("MenuLightStore", config));
        object savedDef = ((IList)saved)[0]!;
        Assert.That((bool)savedDef.GetType().GetProperty("Enabled", Flags)!.GetValue(savedDef)!, Is.False);
    }

    [Test]
    public void TweakOpensTheSavedId()
    {
        object config = NewConfig();
        object light = NewLight(config);
        object store = StoreOf(light);
        string? opened = null;
        object row = Row(Def("b"), 0, id => opened = id, store, light, () => { });
        row.GetType().GetMethod("Tweak", Flags)!.Invoke(row, null);
        Assert.That(opened, Is.EqualTo("b"));
    }

    [Test]
    public void RemoveDeletesTheSavedIdAfterAnIndexShift()
    {
        object config = NewConfig();
        object light = NewLight(config);
        object store = StoreOf(light);
        Upsert(store, Def("x"));
        Upsert(store, Def("y"));
        Upsert(store, Def("z"));
        var rebuilds = 0;
        object row = Row(CloneOf(Def("y")), 1, _ => { }, store, light, () => rebuilds++);
        store.GetType().GetMethod("Remove", Flags)!.Invoke(store, new object?[] { "x" });
        row.GetType().GetMethod("Remove", Flags)!.Invoke(row, null);
        object saved = Lights(New("MenuLightStore", config));
        Assert.That(((IList)saved).Count, Is.EqualTo(1));
        Assert.That(((IList)saved)[0]!.GetType().GetProperty("Id", Flags)!.GetValue(((IList)saved)[0]!), Is.EqualTo("z"));
        Assert.That(rebuilds, Is.EqualTo(1));
    }

    [Test]
    public void CanAddIsFalseAtEightSavedLights()
    {
        object config = NewConfig();
        object light = NewLight(config);
        object store = StoreOf(light);
        for (int i = 0; i < 8; i++)
            Upsert(store, Def("id" + i));
        object list = New("MenuLightListView", config, light, store, (Action<string?>)(_ => { }));
        Assert.That((bool)list.GetType().GetProperty("CanAdd", Flags)!.GetValue(list)!, Is.False);
    }
}
