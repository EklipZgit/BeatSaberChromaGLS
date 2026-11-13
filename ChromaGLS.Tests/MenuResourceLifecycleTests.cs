using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class MenuResourceLifecycleTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Assembly Plugin = Assembly.Load("ChromaGLS");
    private static readonly Type ConfigType = Plugin.GetType("ChromaGLS.Settings.Config", true)!;
    private static readonly Type OwnerType = Plugin.GetType("ChromaGLS.Settings.ParabolicFloorLight", true)!;
    private static readonly Type DefinitionType = Plugin.GetType("ChromaGLS.Settings.MenuLightDefinition", true)!;

    [Test]
    public void MenuSuspendReleasesRuntimeStateAndKeepsSavedData()
    {
        object config = Activator.CreateInstance(ConfigType, nonPublic: true)!;
        object owner = Activator.CreateInstance(OwnerType, Instance, null, new[] { config }, null)!;
        {
            // Seed live-session state so the release is observable, not vacuous.
            Field(owner, "_canCreate").SetValue(owner, true);
            Field(owner, "_renderBloom").SetValue(owner, new Action<RenderTexture, RenderTexture>((s, d) => { }));
            object definition = Activator.CreateInstance(DefinitionType, true)!;
            definition.GetType().GetProperty("Id", Instance)!.SetValue(definition, "saved-light");
            IDictionary saved = (IDictionary)Field(owner, "_savedDefinitions").GetValue(owner)!;
            saved.Add("saved-light", definition);

            OwnerType.GetMethod("SetMenuActive", Instance)!.Invoke(owner, new object[] { false });

            Assert.IsFalse((bool)Field(owner, "_canCreate").GetValue(owner)!, "suspend must stop creation");
            Assert.IsFalse((bool)Field(owner, "_menuActive").GetValue(owner)!);
            Assert.IsEmpty((ICollection)Field(owner, "_renderers").GetValue(owner)!, "suspend must dispose renderers");
            Assert.IsEmpty((ICollection)saved, "suspend must drop saved-definition clones");
            Assert.IsEmpty((ICollection)Field(owner, "_savedMeshes").GetValue(owner)!, "suspend must drop mesh caches");
            Assert.Null(Field(owner, "_reflectionCache").GetValue(owner), "suspend must drop the reflection cache");
            Assert.Null(Field(owner, "_shader").GetValue(owner), "suspend must drop the renderer shader");
            Assert.Null(Field(owner, "_renderBloom").GetValue(owner), "suspend must drop the bloom delegate");
            Assert.Null(Field(owner, "_mainEffectContainer").GetValue(owner), "suspend must drop the effect container");
            Assert.NotNull(OwnerType.GetProperty("Store", Instance)!.GetValue(owner), "the saved store must survive menu exit");

            // A second suspend is idempotent and must not resurrect state.
            OwnerType.GetMethod("SetMenuActive", Instance)!.Invoke(owner, new object[] { false });
            Assert.IsEmpty((ICollection)Field(owner, "_renderers").GetValue(owner)!);

            // A sync while suspended must not recreate renderers or saved caches.
            Type listType = typeof(List<>).MakeGenericType(DefinitionType);
            IList definitions = (IList)Activator.CreateInstance(listType)!;
            definitions.Add(definition);
            OwnerType.GetMethod("Synchronize", Instance)!.Invoke(owner, new object[] { definitions });
            Assert.IsEmpty((ICollection)Field(owner, "_renderers").GetValue(owner)!, "suspended sync must not rebuild renderers");
            Assert.IsEmpty((ICollection)saved, "suspended sync must not rebuild saved caches");
        }
    }

    private static FieldInfo Field(object target, string name) => target.GetType().GetField(name, Instance)!;
}
