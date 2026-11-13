using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ChromaGLS.Tests;

[TestFixture]
public class MenuLightSettingsUITests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Type TypeOf(string name) => Assembly.Load("ChromaGLS").GetType("ChromaGLS.Settings." + name, true)!;
    private static object New(string type, params object[] args) => Activator.CreateInstance(TypeOf(type), Flags, null, args, null)!;

    [Test]
    public void BeginEditorShowsTheModalBeforeOpeningTheSession()
    {
        object config = New("Config");
        config.GetType().GetProperty("MenuLightsJson", Flags)!.SetValue(config, "[]");
        object light = New("ParabolicFloorLight", config);
        object controller = New("ParabolicFloorLightSettingsUI", config, light);
        object session = controller.GetType().GetField("_session", Flags)!.GetValue(controller)!;
        PropertyInfo editing = session.GetType().GetProperty("IsEditing", Flags)!;
        PropertyInfo isNew = session.GetType().GetProperty("IsNew", Flags)!;
        var shown = false;
        Action show = () =>
        {
            shown = true;
            Assert.That((bool)editing.GetValue(session)!, Is.False, "the session must still be closed while the native modal shows");
            controller.GetType().GetMethod("CloseWithoutSaving", Flags)!.Invoke(controller, null);
        };
        MethodInfo? begin = controller.GetType().GetMethod("BeginEditor", Flags);
        Assert.That(begin, Is.Not.Null, "the list-to-editor seam must exist so the first native Show precedes the session");
        begin!.Invoke(controller, new object?[] { null, show });
        Assert.That(shown, Is.True);
        Assert.That((bool)editing.GetValue(session)!, Is.True);
        Assert.That((bool)isNew.GetValue(session)!, Is.True);
    }

    [Test]
    public void TypeChangeEmitsRefreshToUpdateShapeGroups()
    {
        object config = New("Config");
        config.GetType().GetProperty("MenuLightsJson", Flags)!.SetValue(config, "[]");
        object light = New("ParabolicFloorLight", config);
        object controller = New("ParabolicFloorLightSettingsUI", config, light);
        object session = controller.GetType().GetField("_session", Flags)!.GetValue(controller)!;
        controller.GetType().GetMethod("BeginEditor", Flags)!.Invoke(controller, new object?[] { null, (Action)(() => { }) });
        // Inject a real parser so the refresh event binding is exercised end to end.
        Type parserType = Type.GetType("BeatSaberMarkupLanguage.Parser.BSMLParserParams, BSML")!;
        object parserParams = Activator.CreateInstance(parserType, Flags, null, new object[] { controller }, null)!;
        controller.GetType().GetField("_parserParams", Flags)!.SetValue(controller, parserParams);
        var refreshes = 0;
        parserParams.GetType().GetMethod("AddEvent", Flags)!.Invoke(parserParams, new object[] { "refresh-parabola", (Action)(() => refreshes++) });
        PropertyInfo typeValue = controller.GetType().GetProperty("TypeValue", Flags)!;
        // Reenter the setter once inside the refresh callback; _loading must block recursion.
        var reentered = false;
        Action reenter = () =>
        {
            if (!reentered)
            {
                reentered = true;
                typeValue.SetValue(controller, Enum.Parse(TypeOf("MenuLightType"), "Square"));
            }
        };
        parserParams.GetType().GetMethod("AddEvent", Flags)!.Invoke(parserParams, new object[] { "refresh-parabola", reenter });
        foreach (string name in new[] { "Floor", "Parabola", "Square", "Cube", "Sphere", "Circle" })
            typeValue.SetValue(controller, Enum.Parse(TypeOf("MenuLightType"), name));
        Assert.That(refreshes, Is.EqualTo(6));
        Assert.That(session.GetType().GetProperty("Working", Flags)!.GetValue(session)!.GetType().GetProperty("Type", Flags)!.GetValue(session.GetType().GetProperty("Working", Flags)!.GetValue(session)!), Is.EqualTo(Enum.Parse(TypeOf("MenuLightType"), "Circle")));
    }


    private static object FreshController(object config, object light)
    {
        object controller = New("ParabolicFloorLightSettingsUI", config, light);
        return controller;
    }

    private static object Session(object controller) => controller.GetType().GetField("_session", Flags)!.GetValue(controller)!;
    private static bool Editing(object s) => (bool)s.GetType().GetProperty("IsEditing", Flags)!.GetValue(s)!;
    private static object Working(object s) => s.GetType().GetProperty("Working", Flags)!.GetValue(s)!;
    private static void Call(object o, string name) => o.GetType().GetMethod(name, Flags)!.Invoke(o, null);
    private static object Config() { object c = New("Config"); c.GetType().GetProperty("MenuLightsJson", Flags)!.SetValue(c, "[]"); return c; }
    private static object Store(object light) => light.GetType().GetProperty("Store", Flags)!.GetValue(light)!;
    private static object Def(string id) { object d = New("MenuLightDefinition"); d.GetType().GetProperty("Id", Flags)!.SetValue(d, id); return d; }
    private static void Upsert(object store, object def) => store.GetType().GetMethod("Upsert", Flags)!.Invoke(store, new[] { def });
    private static int Count(object store) => ((System.Collections.IList)store.GetType().GetProperty("Lights", Flags)!.GetValue(store)!).Count;

    [Test]
    public void AddCancelConsumesNoSlot()
    {
        object config = Config();
        object light = New("ParabolicFloorLight", config);
        object controller = FreshController(config, light);
        object session = Session(controller);
        controller.GetType().GetMethod("BeginEditor", Flags)!.Invoke(controller, new object?[] { null, (Action)(() => { }) });
        Assert.That(Editing(session), Is.True);
        Call(controller, "HandleCancel");
        Assert.That(Count(Store(light)), Is.EqualTo(0));
    }

    [Test]
    public void SaveNewAssignsAStableId()
    {
        object config = Config();
        object light = New("ParabolicFloorLight", config);
        object controller = FreshController(config, light);
        object session = Session(controller);
        controller.GetType().GetMethod("BeginEditor", Flags)!.Invoke(controller, new object?[] { null, (Action)(() => { }) });
        object draft = Working(session);
        string id = (string)draft.GetType().GetProperty("Id", Flags)!.GetValue(draft)!;
        controller.GetType().GetProperty("TypeValue", Flags)!.SetValue(controller, Enum.Parse(TypeOf("MenuLightType"), "Circle"));
        Call(controller, "HandleSave");
        AssertSavedId();
        controller.GetType().GetMethod("BeginEditor", Flags)!.Invoke(controller, new object?[] { id, (Action)(() => { }) });
        Call(controller, "HandleSave");
        AssertSavedId();

        void AssertSavedId()
        {
            object fresh = New("MenuLightStore", config);
            Assert.That(Count(fresh), Is.EqualTo(1));
            object saved = ((System.Collections.IList)fresh.GetType().GetProperty("Lights", Flags)!.GetValue(fresh)!)[0]!;
            Assert.That(saved.GetType().GetProperty("Id", Flags)!.GetValue(saved), Is.EqualTo(id).And.Not.Empty);
            Assert.That(saved.GetType().GetProperty("Type", Flags)!.GetValue(saved), Is.EqualTo(Enum.Parse(TypeOf("MenuLightType"), "Circle")));
        }
    }

    [Test]
    public void SecondLightEditsDoNotTouchTheFirst()
    {
        object config = Config();
        object light = New("ParabolicFloorLight", config);
        object store = Store(light);
        Upsert(store, Def("a"));
        Upsert(store, Def("b"));
        string before = config.GetType().GetProperty("MenuLightsJson", Flags)!.GetValue(config)!.ToString()!;
        object controller = FreshController(config, light);
        object session = Session(controller);
        controller.GetType().GetMethod("BeginEditor", Flags)!.Invoke(controller, new object?[] { "b", (Action)(() => { }) });
        controller.GetType().GetProperty("ParabolaBlue", Flags)!.SetValue(controller, 0.75f);
        controller.GetType().GetProperty("HeightValue", Flags)!.SetValue(controller, 7f);
        controller.GetType().GetProperty("DepthValue", Flags)!.SetValue(controller, 0.37f);
        controller.GetType().GetProperty("RotationXValue", Flags)!.SetValue(controller, 35f);
        controller.GetType().GetProperty("RotationZValue", Flags)!.SetValue(controller, -20f);
        Assert.That(config.GetType().GetProperty("MenuLightsJson", Flags)!.GetValue(config), Is.EqualTo(before));
        Call(controller, "HandleSave");
        object fresh = New("MenuLightStore", config);
        object savedA = ((System.Collections.IList)fresh.GetType().GetProperty("Lights", Flags)!.GetValue(fresh)!)[0]!;
        object savedB = ((System.Collections.IList)fresh.GetType().GetProperty("Lights", Flags)!.GetValue(fresh)!)[1]!;
        string after = (string)config.GetType().GetProperty("MenuLightsJson", Flags)!.GetValue(config)!;
        Assert.That(JToken.DeepEquals(JArray.Parse(before)[0], JArray.Parse(after)[0]), Is.True, "editing b must preserve every saved field of a");
        Assert.That((float)savedB.GetType().GetProperty("Height", Flags)!.GetValue(savedB)!, Is.EqualTo(7f));
        Assert.That((float)savedB.GetType().GetProperty("Depth", Flags)!.GetValue(savedB)!, Is.EqualTo(0.37f));
        Assert.That((float)savedB.GetType().GetProperty("RotationX", Flags)!.GetValue(savedB)!, Is.EqualTo(35f));
        Assert.That((float)savedB.GetType().GetProperty("RotationZ", Flags)!.GetValue(savedB)!, Is.EqualTo(-20f));
        object appearance = savedB.GetType().GetProperty("Appearance", Flags)!.GetValue(savedB)!;
        Assert.That((float)appearance.GetType().GetField("Blue", Flags)!.GetValue(appearance)!, Is.EqualTo(3.25f).Within(0.0001f));
        float[] quaternion = new[] { "QuaternionX", "QuaternionY", "QuaternionZ", "QuaternionW" }
            .Select(name => (float)savedB.GetType().GetProperty(name, Flags)!.GetValue(savedB)!)
            .ToArray();
        Assert.That(Math.Abs(quaternion[0]), Is.GreaterThan(0.1f), "pitch must reach the saved quaternion");
        Assert.That(Math.Abs(quaternion[2]), Is.GreaterThan(0.1f), "roll must reach the saved quaternion");
        Assert.That(quaternion.Sum(component => component * component), Is.EqualTo(1f).Within(0.0001f));
        Assert.That((float)savedA.GetType().GetProperty("Height", Flags)!.GetValue(savedA)!, Is.EqualTo(0.006f));
    }

    [Test]
    public void ResetPreservesIdAndTypeWithDefaults()
    {
        object config = Config();
        object light = New("ParabolicFloorLight", config);
        object store = Store(light);
        object saved = New("MenuLightDefinition");
        saved.GetType().GetProperty("Id", Flags)!.SetValue(saved, "s");
        saved.GetType().GetProperty("Type", Flags)!.SetValue(saved, Enum.Parse(TypeOf("MenuLightType"), "Cube"));
        saved.GetType().GetProperty("Depth", Flags)!.SetValue(saved, 9f);
        store.GetType().GetMethod("Upsert", Flags)!.Invoke(store, new[] { saved });
        object controller = FreshController(config, light);
        object session = Session(controller);
        controller.GetType().GetMethod("BeginEditor", Flags)!.Invoke(controller, new object?[] { "s", (Action)(() => { }) });
        Call(controller, "HandleReset");
        object working = Working(session);
        Assert.That(working.GetType().GetProperty("Id", Flags)!.GetValue(working), Is.EqualTo("s"));
        Assert.That(working.GetType().GetProperty("Type", Flags)!.GetValue(working), Is.EqualTo(Enum.Parse(TypeOf("MenuLightType"), "Cube")));
        Assert.That((float)working.GetType().GetProperty("Depth", Flags)!.GetValue(working)!, Is.EqualTo(0.02f));
        Assert.That((float)working.GetType().GetProperty("Height", Flags)!.GetValue(working)!, Is.EqualTo(0.006f));
        string after = config.GetType().GetProperty("MenuLightsJson", Flags)!.GetValue(config)!.ToString()!;
        Call(controller, "HandleCancel");
        Assert.That(config.GetType().GetProperty("MenuLightsJson", Flags)!.GetValue(config), Is.EqualTo(after));
    }

    [Test]
    public void GameplayCancelDoesNotReopen()
    {
        object config = Config();
        object light = New("ParabolicFloorLight", config);
        object controller = FreshController(config, light);
        object session = Session(controller);
        controller.GetType().GetMethod("BeginEditor", Flags)!.Invoke(controller, new object?[] { null, (Action)(() => { }) });
        Assert.That(Editing(session), Is.True);
        Call(controller, "CloseForGameplay");
        Assert.That(Editing(session), Is.False);
        string json = config.GetType().GetProperty("MenuLightsJson", Flags)!.GetValue(config)!.ToString()!;
        Assert.That(json, Is.EqualTo("[]"));
        Call(controller, "CloseForGameplay");
        Assert.That(Editing(session), Is.False);
    }

    [Test]
    public void BeginEditorBlocksAddAtEight()
    {
        object config = Config();
        object light = New("ParabolicFloorLight", config);
        object store = Store(light);
        for (int i = 0; i < 8; i++)
        {
            object def = New("MenuLightDefinition");
            def.GetType().GetProperty("Id", Flags)!.SetValue(def, "id" + i);
            store.GetType().GetMethod("Upsert", Flags)!.Invoke(store, new[] { def });
        }
        object controller = FreshController(config, light);
        object session = Session(controller);
        var shown = false;
        controller.GetType().GetMethod("BeginEditor", Flags)!.Invoke(controller, new object?[] { null, (Action)(() => shown = true) });
        Assert.That(shown, Is.False);
        Assert.That(Editing(session), Is.False);
    }

    [Test]
    public void EditorBsmlKeepsPoseControlsFirst()
    {
        using System.IO.Stream stream = Assembly.Load("ChromaGLS").GetManifestResourceStream("ChromaGLS.Settings.parabolic-floor-light.bsml")!;
        var xml = System.Xml.Linq.XElement.Load(stream);
        System.Collections.Generic.List<System.Xml.Linq.XElement> sliders = xml.Descendants("slider-setting").ToList();
        System.Collections.Generic.List<string> order = sliders.Select(s => s.Attribute("value")!.Value).ToList();
        Assert.That(order.IndexOf("light-height"), Is.LessThan(order.IndexOf("parabola-brightness")), "Up/down must precede the appearance sliders");
        Assert.That(order.IndexOf("light-height"), Is.LessThan(order.IndexOf("light-depth")), "Up/down must precede Underside depth");
        Assert.That(order.IndexOf("light-height"), Is.LessThan(order.IndexOf("parabola-rotation")), "Up/down must precede Yaw");
        Assert.That(order.IndexOf("parabola-offset-x"), Is.LessThan(order.IndexOf("light-height")), "Left/right must precede Up/down");
        Assert.That(order.IndexOf("parabola-offset-z"), Is.LessThan(order.IndexOf("light-height")), "Forward/back must precede Up/down");
        System.Xml.Linq.XElement height = sliders.First(s => s.Attribute("value")!.Value == "light-height");
        Assert.That(height.Attribute("text")!.Value, Is.EqualTo("Up/down"));
        Assert.That(height.Parent!.Attribute("id")!.Value, Is.EqualTo("free-pose-controls"));
        System.Xml.Linq.XElement depth = sliders.First(s => s.Attribute("value")!.Value == "light-depth");
        Assert.That(depth.Attribute("text")!.Value, Is.EqualTo("Underside depth"));
    }
}
