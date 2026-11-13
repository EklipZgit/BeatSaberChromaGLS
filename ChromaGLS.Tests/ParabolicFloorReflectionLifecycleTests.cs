using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class ParabolicFloorReflectionLifecycleTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly string[] Fields =
    {
        "Brightness", "Red", "Green", "Blue", "WidthScale", "OffsetX", "OffsetZ", "Rotation",
        "LightWidthScale", "BloomWidthScale", "BloomBrightness", "HdrGlowThreshold", "HdrGlowWidthScale", "HdrGlowBrightness",
    };
    private readonly List<object> _previews = new();
    private readonly List<object> _saves = new();
    private object _config = null!;
    private object _session = null!;
    private object _default = null!;

    [SetUp]
    public void SetUp()
    {
        _previews.Clear();
        _saves.Clear();
        Assembly plugin = Assembly.Load("ChromaGLS");
        Type configType = plugin.GetType("ChromaGLS.Settings.Config", true)!;
        Type settingsType = plugin.GetType("ChromaGLS.Settings.ParabolicFloorLightSettings", true)!;
        Type sessionType = plugin.GetType("ChromaGLS.Settings.ParabolicFloorLightEditSession", true)!;
        _config = Activator.CreateInstance(configType, nonPublic: true)!;
        _default = settingsType.GetField("Default", Static)!.GetValue(null)!;
        Type action = typeof(Action<>).MakeGenericType(settingsType);
        Delegate preview = Delegate.CreateDelegate(
            action,
            this,
            GetType().GetMethod(nameof(RecordPreview), Instance)!.MakeGenericMethod(settingsType));
        Delegate saved = Delegate.CreateDelegate(
            action,
            this,
            GetType().GetMethod(nameof(RecordSaved), Instance)!.MakeGenericMethod(settingsType));
        ConstructorInfo? constructor = sessionType.GetConstructor(Instance, null, new[] { configType, action, action }, null);
        Assert.That(constructor, Is.Not.Null, "the saved-settings callback must be separate from live preview");
        _session = constructor!.Invoke(new object[] { _config, preview, saved });
    }

    [Test]
    public void PreviewResetAndCancelNeverNotifySaved()
    {
        Invoke("Open");
        foreach (string field in Fields)
        {
            object changed = _default.GetType().GetMethod("With" + field, Instance)!.Invoke(_default, new object[] { 0.75f })!;
            Invoke("Preview", changed);
        }

        Invoke("Preview", _default);
        Invoke("Cancel");
        Invoke("Cancel");
        Invoke("Save");
        Assert.That(_previews.Count, Is.EqualTo(Fields.Length + 2));
        Assert.That(_saves, Is.Empty);
        foreach (string field in Fields)
        {
            Assert.That(
                _config.GetType().GetProperty("ParabolicFloorLight" + field)!.GetValue(_config),
                Is.EqualTo(_default.GetType().GetField(field, Instance)!.GetValue(_default)),
                $"preview/reset/cancel must preserve saved {field}");
        }
    }

    [Test]
    public void SaveNotifiesOnceAfterAllFourteenValuesAreCommitted()
    {
        float[] values = { 1.25f, 3.1f, 0.5f, 2.4f, 4.1f, 0.8f, -0.4f, 61f, 1.8f, 2.1f, 3.2f, 1.1f, 0.24f, 2.6f };
        object changed = _default;
        for (int index = 0; index < Fields.Length; index++)
            changed = changed.GetType().GetMethod("With" + Fields[index], Instance)!.Invoke(changed, new object[] { values[index] })!;

        Invoke("Save");
        Assert.That(_saves, Is.Empty, "Save outside an edit session has no effect");
        Invoke("Open");
        Invoke("Preview", changed);
        Invoke("Save");
        Invoke("Save");
        Invoke("Cancel");
        Assert.That(_saves.Count, Is.EqualTo(1));
        Assert.That(_previews.Count, Is.EqualTo(1), "Save must not replay the live preview");
        for (int index = 0; index < Fields.Length; index++)
        {
            Assert.That(
                _saves[0].GetType().GetField(Fields[index], Instance)!.GetValue(_saves[0]),
                Is.EqualTo(values[index]));
        }
    }

    [Test]
    public void ReopeningAndCancellingPreservesTheLastSavedSnapshot()
    {
        Invoke("Open");
        object blue = _default.GetType().GetMethod("WithBlue", Instance)!.Invoke(_default, new object[] { 5f })!;
        Invoke("Preview", blue);
        Invoke("Save");
        Invoke("Open");
        Invoke("Preview", _default);
        Invoke("Cancel");
        Assert.That(_saves.Count, Is.EqualTo(1));
        Assert.That(_previews[_previews.Count - 1].GetType().GetField("Blue", Instance)!.GetValue(_previews[_previews.Count - 1]), Is.EqualTo(5f));
    }

    private void RecordPreview<T>(T settings) => _previews.Add(settings!);

    private void RecordSaved<T>(T settings)
    {
        Assert.That(_session.GetType().GetProperty("IsEditing", Instance)!.GetValue(_session), Is.False);
        foreach (string field in Fields)
        {
            Assert.That(
                _config.GetType().GetProperty("ParabolicFloorLight" + field)!.GetValue(_config),
                Is.EqualTo(settings!.GetType().GetField(field, Instance)!.GetValue(settings)),
                $"{field} must be persisted before rebuilding its reflection");
        }

        _saves.Add(settings!);
    }

    private void Invoke(string method, params object[] arguments)
        => _session.GetType().GetMethod(method, Instance)!.Invoke(_session, arguments);
}
