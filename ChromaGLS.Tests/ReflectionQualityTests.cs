using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using IPA.Config;
using IPA.Config.Stores;
using NUnit.Framework;

namespace ChromaGLS.Tests;

[TestFixture]
public class ReflectionQualityTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static Assembly Plugin => Assembly.Load("ChromaGLS");

    [Test]
    public void FreshConfigurationDefaultsToHighBeforeFirstRunDetection()
    {
        object config = NewConfig();
        Assert.That(Get(config, "ReflectionsQuality").ToString(), Is.EqualTo("High"));
        Assert.That(Get(config, "ReflectionsQualityInitialized"), Is.EqualTo(false));
    }

    [TestCase(0, "None")]
    [TestCase(512, "None")]
    [TestCase(1023, "None")]
    [TestCase(1024, "Low")]
    [TestCase(1536, "Low")]
    [TestCase(2047, "Low")]
    [TestCase(2048, "High")]
    [TestCase(8192, "High")]
    public void FirstRunUsesStrictGraphicsMemoryBoundaries(int megabytes, string expected)
    {
        object config = NewConfig();
        int scans = 0;
        Initialize(config, () =>
        {
            scans++;
            return megabytes;
        });
        Assert.That(Get(config, "ReflectionsQuality").ToString(), Is.EqualTo(expected));
        Assert.That(Get(config, "ReflectionsQualityInitialized"), Is.EqualTo(true));
        Assert.That(scans, Is.EqualTo(1), "Only the first initialization may query the GPU.");
    }

    [TestCase("None")]
    [TestCase("Low")]
    [TestCase("High")]
    public void InitializedChoiceNeverRescansOrChangesTheSavedSetting(string choice)
    {
        object config = NewConfig();
        SetQuality(config, choice);
        Set(config, "ReflectionsQualityInitialized", true);
        Initialize(config, () => throw new AssertionException("A saved choice must not trigger another graphics-memory scan."));
        Assert.That(Get(config, "ReflectionsQuality").ToString(), Is.EqualTo(choice));
    }

    [Test]
    public void AutomaticSelectionRunsOnlyOnceAndLaterManualChoiceWins()
    {
        object config = NewConfig();
        Initialize(config, () => 512);
        Assert.That(Get(config, "ReflectionsQuality").ToString(), Is.EqualTo("None"));
        SetQuality(config, "High");
        Initialize(config, () => throw new AssertionException("Restart must retain the user's override, even on the same small GPU."));
        Assert.That(Get(config, "ReflectionsQuality").ToString(), Is.EqualTo("High"));
    }

    [TestCase("None", 0)]
    [TestCase("Low", 1024)]
    [TestCase("High", 4096)]
    public void MenuCacheResolutionMatchesTheSelectedTier(string choice, int expected)
    {
        object quality = Enum.Parse(RequiredType("ReflectionQuality"), choice);
        MethodInfo? resolution = RequiredType("ReflectionQualitySettings").GetMethod("GetResolution", Static);
        Assert.That(resolution, Is.Not.Null, "The renderer and bake must share the tier's resolution policy.");
        Assert.That(resolution!.Invoke(null, new[] { quality }), Is.EqualTo(expected));
    }

    [TestCase("None", 1024)]
    [TestCase("Low", 1024)]
    [TestCase("High", 8192)]
    public void ProceduralLightResolutionMatchesTheSelectedTier(string choice, int expected)
    {
        object quality = Enum.Parse(RequiredType("ReflectionQuality"), choice);
        MethodInfo? resolution = RequiredType("ReflectionQualitySettings").GetMethod("GetMenuLightResolution", Static);
        Assert.That(resolution, Is.Not.Null, "Procedural lights must select their resolution independently of captured feet.");
        Assert.That(resolution!.Invoke(null, new[] { quality }), Is.EqualTo(expected));
    }

    [TestCase("None", 0)]
    [TestCase("Low", 1024)]
    [TestCase("High", 2048)]
    public void RoomFaceResolutionMatchesTheSelectedTier(string choice, int expected)
    {
        object quality = Enum.Parse(RequiredType("ReflectionQuality"), choice);
        MethodInfo? resolution = RequiredType("ReflectionQualitySettings").GetMethod("GetRoomResolution", Static);
        Assert.That(resolution, Is.Not.Null, "Baked room maps must have an explicit tier resolution.");
        Assert.That(resolution!.Invoke(null, new[] { quality }), Is.EqualTo(expected));
    }

    [Test]
    public void QualityDropdownIsAtTheTopAndBindsThePersistentChoice()
    {
        using Stream markup = Plugin.GetManifestResourceStream("ChromaGLS.Settings.menu-lights.bsml")!;
        XElement document = XElement.Load(markup);
        XElement? dropdown = document.Descendants("dropdown-list-setting").FirstOrDefault();
        Assert.That(dropdown, Is.Not.Null, "Menu Lights must expose Reflections Quality.");
        Assert.That((string?)dropdown!.Attribute("text"), Is.EqualTo("Reflections Quality"));
        Assert.That((string?)dropdown.Attribute("value"), Is.EqualTo("reflection-quality"));
        Assert.That((string?)dropdown.Attribute("choices"), Is.EqualTo("reflection-quality-options"));
        Assert.That((string?)dropdown.Attribute("apply-on-change"), Is.EqualTo("true"));
        Assert.That(dropdown.ElementsBeforeSelf().Any(element => (string?)element.Attribute("id") == "add-light"), Is.False, "The quality control must precede Add Light.");
        Assert.That(dropdown.Ancestors("scroll-view").Any(), Is.False, "Quality must stay outside the scrolling list.");

        object config = NewConfig();
        Type ownerType = RequiredType("ParabolicFloorLight");
        object owner = Activator.CreateInstance(ownerType, Instance, null, new[] { config }, null)!;
        object store = ownerType.GetProperty("Store", Instance)!.GetValue(owner)!;
        Type listType = RequiredType("MenuLightListView");
        object list = Activator.CreateInstance(listType, Instance, null, new object[] { config, owner, store, (Action<string?>)(_ => { }) }, null)!;
        PropertyInfo choices = FindBinding(listType, "reflection-quality-options");
        string[] names = ((IEnumerable)choices.GetValue(list)!).Cast<object>().Select(value => value.ToString()!).ToArray();
        Assert.That(names, Is.EqualTo(new[] { "None", "Low", "High" }));
        PropertyInfo selection = FindBinding(listType, "reflection-quality");
        Assert.That(selection.GetValue(list)!.ToString(), Is.EqualTo("High"));
        SetQuality(config, "Low");
        Assert.That(selection.GetValue(list)!.ToString(), Is.EqualTo("Low"), "The visible selection must read the persisted config, not a separate UI copy.");
    }

    [TestCase("None")]
    [TestCase("Low")]
    [TestCase("High")]
    public void GeneratedStoreRoundTripsChoiceAndInitializationMarker(string choice)
    {
        Type configType = RequiredType("Config");
        MethodInfo create = typeof(GeneratedStore).GetMethods(Static)
            .Single(method => method.Name == "Create" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
            .MakeGenericMethod(configType);
        object config = create.Invoke(null, null)!;
        SetQuality(config, choice);
        Set(config, "ReflectionsQualityInitialized", true);
        FileInfo file = new(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"reflection-quality-{choice}-{Guid.NewGuid():N}.json"));
        Type jsonProvider = typeof(ConfigProvider).Assembly.GetType("IPA.Config.Providers.JsonConfigProvider", true)!;
        ConstructorInfo constructor = typeof(ConfigProvider).GetConstructor(Instance, null, new[] { typeof(FileInfo), typeof(IConfigProvider) }, null)!;
        ConfigProvider provider = (ConfigProvider)constructor.Invoke(new[] { (object)file, Activator.CreateInstance(jsonProvider, true)! });
        IConfigStore store = (IConfigStore)config;
        store.WriteSyncObject.EnterReadLock();
        try
        {
            store.WriteTo(provider);
        }
        finally
        {
            store.WriteSyncObject.ExitReadLock();
        }

        object restored = create.Invoke(null, null)!;
        IConfigStore freshStore = (IConfigStore)restored;
        freshStore.WriteSyncObject.EnterWriteLock();
        try
        {
            freshStore.ReadFrom(provider);
        }
        finally
        {
            freshStore.WriteSyncObject.ExitWriteLock();
        }

        Assert.That(Get(restored, "ReflectionsQuality").ToString(), Is.EqualTo(choice));
        Assert.That(Get(restored, "ReflectionsQualityInitialized"), Is.EqualTo(true));
        Initialize(restored, () => throw new AssertionException("Loading persisted settings must not re-run hardware detection."));
    }

    private static Type RequiredType(string name)
    {
        Type? type = Plugin.GetType("ChromaGLS.Settings." + name);
        Assert.That(type, Is.Not.Null, "Reflection quality must expose the compiled " + name + " behavior.");
        return type!;
    }

    private static object NewConfig() => Activator.CreateInstance(RequiredType("Config"), true)!;

    private static object Get(object config, string name)
    {
        PropertyInfo? property = config.GetType().GetProperty(name, Instance);
        Assert.That(property, Is.Not.Null, name + " must be a real persisted config property.");
        return property!.GetValue(config)!;
    }

    private static void Set(object config, string name, object value)
    {
        Get(config, name);
        config.GetType().GetProperty(name, Instance)!.SetValue(config, value);
    }

    private static void SetQuality(object config, string choice)
        => Set(config, "ReflectionsQuality", Enum.Parse(RequiredType("ReflectionQuality"), choice));

    private static void Initialize(object config, Func<int> scan)
    {
        MethodInfo? method = RequiredType("ReflectionQualitySettings").GetMethod("Initialize", Static);
        Assert.That(method, Is.Not.Null, "First-run initialization must own the one-time hardware scan.");
        method!.Invoke(null, new object[] { config, scan });
    }

    private static PropertyInfo FindBinding(Type type, string value)
    {
        PropertyInfo? result = type.GetProperties(Instance).SingleOrDefault(property => property.GetCustomAttributes(true)
            .Any(attribute => attribute.GetType().Name == "UIValue" && (string?)attribute.GetType().GetProperty("Id")!.GetValue(attribute) == value));
        Assert.That(result, Is.Not.Null, "The dropdown must bind the compiled UI property " + value + ".");
        return result!;
    }
}
