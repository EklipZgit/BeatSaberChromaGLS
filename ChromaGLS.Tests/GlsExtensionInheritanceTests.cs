using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CustomJSONData.CustomBeatmap;
using HarmonyLib;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

// Extension nodes (V3 usePreviousValue / legacy Extend) must clone the entire CustomData of the last
// same-lane event behind them when the game's LightColorBeatmapEventDataProcessor propagates values via
// CopyColorDataFrom. The predecessor can live in a different box or group with its own authored payload,
// and every filtered light lane must inherit independently. These tests drive the real patched
// deserialize -> Convert -> Unpack -> insert-processor path, which only detours under Unity's Mono.
[TestFixture]
[NonParallelizable]
internal sealed class GlsExtensionInheritanceTests
{
    private const string HarmonyId = "com.eklipz.ChromaGLS";
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    private Assembly plugin = null!;
    private Harmony harmony = null!;
    private Type pluginType = null!;
    private MethodInfo setEnabled = null!;
    private Type eventType = null!;
    private Type dataItemType = null!;
    private MethodInfo processInsertedData = null!;
    private Type processorType = null!;
    private Type effectType = null!;
    private MethodInfo handler = null!;
    private Type tweenEaseType = null!;

    [SetUp]
    public void SetUp()
    {
        // The game's MonoMod detours only run on Unity Mono; other hosts crash during patch installation.
        if (Type.GetType("Mono.Runtime") == null)
        {
            Assert.Ignore("Harmony detour verification requires Unity's Mono runtime.");
        }

        harmony = new Harmony(HarmonyId);
        plugin = Assembly.Load("ChromaGLS");
        foreach (AssemblyName reference in plugin.GetReferencedAssemblies().Where(reference => reference.Name is "Main" or "BeatmapCore" or "DataModels" or "Tweening"))
        {
            Assembly.Load(reference);
        }

        pluginType = plugin.GetType("ChromaGLS.Plugin", true)!;
        object instance = TestHostCompat.GetUninitializedObject(pluginType);
        pluginType.GetField("_harmonyInstance", All)!.SetValue(instance, harmony);
        pluginType.GetField("_patchesApplied", All)!.SetValue(instance, false);
        pluginType.GetField("_instance", All)!.SetValue(null, instance);
        setEnabled = pluginType.GetMethod("SetEnabled", All)!;
        setEnabled.Invoke(null, [true]);

        eventType = FindType("LightColorBeatmapEventData")!;
        dataItemType = FindType("BeatmapDataItem")!;
        processorType = FindType("LightColorBeatmapEventDataProcessor")!;
        processInsertedData = processorType.GetMethod("ProcessInsertedData", All)!;
        effectType = FindType("LightColorGroupEffect")!;
        handler = effectType.GetMethod("HandleColorChangeBeatmapEvent", All)!;
        tweenEaseType = AccessTools.TypeByName("Tweening.FloatTween")
            .GetConstructors().Single(ctor => ctor.GetParameters().Length == 6)
            .GetParameters()[4].ParameterType;
    }

    [TearDown]
    public void TearDown()
    {
        setEnabled?.Invoke(null, [false]);
        harmony?.UnpatchSelf();
        pluginType?.GetField("_instance", All)!.SetValue(null, null);
    }

    // The reported shape: a standalone extension box one group later must clone the custom payload of
    // the last same-lane event behind it — colors, strobe colors, strobe interval/easing, legacy cg_
    // backport keys, and arbitrary extra keys — as one dictionary clone.
    [Test]
    public void CrossBoxExtensionClonesAllPredecessorCustomData()
    {
        const string groups =
            "[{\"b\":0,\"g\":1,\"e\":[{\"f\":{\"f\":2,\"p\":0,\"t\":1},\"d\":1,\"t\":1,\"e\":[" +
            "{\"b\":0,\"i\":0,\"c\":0,\"s\":0.2,\"f\":0,\"sb\":0,\"sf\":0,\"customData\":{\"color\":[0,1,0.197],\"strobeColor\":[1,0,0.999]}}," +
            "{\"b\":0.5,\"i\":1,\"c\":0,\"s\":0.7,\"f\":2,\"sb\":0.8,\"sf\":1,\"customData\":{\"color\":[0,1,0.254],\"strobeColor\":[1,0,0.999],\"strobeInterval\":4,\"strobeEasing\":16,\"marker\":[7,8,9]}}]}]}," +
            "{\"b\":1.5,\"g\":1,\"e\":[{\"f\":{\"f\":2,\"p\":0,\"t\":1},\"d\":1,\"t\":1,\"e\":[" +
            "{\"b\":0,\"i\":2,\"c\":0,\"s\":0.5,\"f\":0,\"sb\":0,\"sf\":0}]}]}]";

        object[] predecessors = UnpackBoxEvents(groups, groupIndex: 0, boxIndex: 0, groupBoxBeat: 0f, elementId: 0);
        object[] extensions = UnpackBoxEvents(groups, groupIndex: 1, boxIndex: 0, groupBoxBeat: 1.5f, elementId: 0);
        object predecessor = predecessors.Last();
        object extension = extensions.Single();
        InsertInOrder(predecessors[0], predecessor, extension);

        Assert.That(extension, Is.InstanceOf<ICustomData>(),
            "An extension node must carry a custom-data carrier so the same-lane predecessor's payload can land on it.");
        CustomData inherited = ((ICustomData)extension).customData;
        CustomData source = ((ICustomData)predecessor).customData;
        Assert.That(inherited, Is.Not.SameAs(source),
            "The extension must own a clone of the predecessor dictionary, not share it.");

        AssertKeyValues(inherited, "color", 0f, 1f, 0.254f);
        AssertKeyValues(inherited, "strobeColor", 1f, 0f, 0.999f);
        AssertKeyValues(inherited, "marker", 7f, 8f, 9f);
        Assert.That(Convert.ToSingle(inherited.Get<object>("strobeInterval")), Is.EqualTo(4f).Within(0.0001f));
        Assert.That(Convert.ToSingle(inherited.Get<object>("strobeEasing")), Is.EqualTo(16f).Within(0.0001f));
        if (inherited.ContainsKey("cg_sb"))
        {
            // 1.29.1 backports sb/sf into cg_sb/cg_sf at parse time; modern versions carry them as native fields on the event instead.
            Assert.That(Convert.ToSingle(inherited.Get<object>("cg_sb")), Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(inherited.Get<object>("cg_sf"), Is.EqualTo(true));
        }
        else
        {
            Assert.That(Convert.ToSingle(GetMemberValue(extension, "strobeBrightness")), Is.EqualTo(0.8f).Within(0.0001f),
                "The native strobeBrightness field must still clone through CopyColorDataFrom.");
            Assert.That(GetMemberValue(extension, "strobeFade"), Is.EqualTo(true));
        }
    }

    // Interval-owned keys describe the transition that ends on their authored node; they must stay with
    // the predecessor interval exactly like the established in-box Extend normalization, while the
    // extension's own authored interval keys still apply.
    [Test]
    public void ExtensionKeepsOwnIntervalKeysWithoutInheritingPredecessors()
    {
        const string groups =
            "[{\"b\":0,\"g\":1,\"e\":[{\"f\":{\"f\":2,\"p\":0,\"t\":1},\"d\":1,\"t\":1,\"e\":[" +
            "{\"b\":0,\"i\":1,\"c\":0,\"s\":0.7,\"f\":0,\"sb\":0,\"sf\":0,\"customData\":{\"color\":[0,1,0.254],\"easing\":7,\"colorEasing\":20,\"strobeColorEasing\":5,\"easingType\":\"HSV\",\"strobeEasing\":16}}]}]}," +
            "{\"b\":1.5,\"g\":1,\"e\":[{\"f\":{\"f\":2,\"p\":0,\"t\":1},\"d\":1,\"t\":1,\"e\":[" +
            "{\"b\":0,\"i\":2,\"c\":0,\"s\":0.5,\"f\":0,\"sb\":0,\"sf\":0,\"customData\":{\"easing\":6}}]}]}]";

        object predecessor = UnpackBoxEvents(groups, 0, 0, 0f, 0).Last();
        object extension = UnpackBoxEvents(groups, 1, 0, 1.5f, 0).Single();
        InsertInOrder(predecessor, extension);

        CustomData inherited = ((ICustomData)extension).customData;
        Assert.That(Convert.ToSingle(inherited.Get<object>("easing")), Is.EqualTo(6f).Within(0.0001f),
            "The extension's own authored interval easing must survive the merge.");
        Assert.That(inherited.ContainsKey("colorEasing"), Is.False,
            "The predecessor interval's colorEasing must not leak into the extension node.");
        Assert.That(inherited.ContainsKey("strobeColorEasing"), Is.False);
        Assert.That(inherited.ContainsKey("easingType"), Is.False);
        Assert.That(Convert.ToSingle(inherited.Get<object>("strobeEasing")), Is.EqualTo(16f).Within(0.0001f),
            "strobeEasing is node state and must inherit across the extension like color does.");
        AssertKeyValues(inherited, "color", 0f, 1f, 0.254f);
    }

    // The user's second reported shape: two division-filtered boxes strobe different custom colors on
    // different light lanes; one whole-group extension box follows. Each lane's extension must continue
    // its own predecessor's payload — lane 0 stays green/red, lane 1 stays cyan/green.
    [Test]
    public void ExtensionClonesPredecessorDataIndependentlyPerLightLane()
    {
        const string groups =
            "[{\"b\":6,\"g\":1,\"e\":[" +
            "{\"f\":{\"f\":1,\"p\":2,\"t\":0,\"r\":0,\"c\":4,\"n\":0,\"s\":0,\"l\":0,\"d\":0},\"w\":1,\"d\":1,\"t\":1,\"e\":[" +
            "{\"b\":0.5,\"i\":1,\"c\":0,\"s\":0.7,\"f\":2,\"sb\":0.3,\"sf\":0,\"customData\":{\"color\":[0,1,0.254],\"strobeColor\":[1,0,0.999]}}]}," +
            "{\"f\":{\"f\":1,\"p\":2,\"t\":1,\"r\":0,\"c\":4,\"n\":0,\"s\":0,\"l\":0,\"d\":0},\"w\":1,\"d\":1,\"t\":1,\"e\":[" +
            "{\"b\":0.5,\"i\":1,\"c\":0,\"s\":0.7,\"f\":2,\"sb\":0.3,\"sf\":0,\"customData\":{\"color\":[0.9,1,0.254],\"strobeColor\":[0,1,0.999]}}]}]}," +
            "{\"b\":7.5,\"g\":1,\"e\":[{\"f\":{\"f\":1,\"p\":1,\"t\":0,\"r\":0,\"c\":0,\"n\":0,\"s\":0,\"l\":0,\"d\":0},\"w\":0,\"d\":1,\"t\":1,\"e\":[" +
            "{\"b\":0,\"i\":2,\"c\":0,\"s\":0.5,\"f\":0,\"sb\":0,\"sf\":0}]}]}]";

        object predecessor0 = UnpackBoxEvents(groups, 0, 0, 6f, 0).Last();
        object predecessor1 = UnpackBoxEvents(groups, 0, 1, 6f, 1).Last();
        object extension0 = UnpackBoxEvents(groups, 1, 0, 7.5f, 0).Single();
        object extension1 = UnpackBoxEvents(groups, 1, 0, 7.5f, 1).Single();
        InsertInOrder(predecessor0, predecessor1, extension0, extension1);

        Assert.That(extension0, Is.InstanceOf<ICustomData>(),
            "Lane 0's extension must carry lane 0's predecessor payload.");
        Assert.That(extension1, Is.InstanceOf<ICustomData>(),
            "Lane 1's extension must carry lane 1's predecessor payload.");
        AssertKeyValues(((ICustomData)extension0).customData, "color", 0f, 1f, 0.254f);
        AssertKeyValues(((ICustomData)extension0).customData, "strobeColor", 1f, 0f, 0.999f);
        AssertKeyValues(((ICustomData)extension1).customData, "color", 0.9f, 1f, 0.254f);
        AssertKeyValues(((ICustomData)extension1).customData, "strobeColor", 0f, 1f, 0.999f);
    }

    // The insert processor re-propagates through extension chains; every link must end up carrying the
    // same-lane payload, not just the first extension node.
    [Test]
    public void ExtensionChainClonesTransitively()
    {
        const string groups =
            "[{\"b\":0,\"g\":1,\"e\":[{\"f\":{\"f\":2,\"p\":0,\"t\":1},\"d\":1,\"t\":1,\"e\":[" +
            "{\"b\":0,\"i\":1,\"c\":0,\"s\":0.7,\"f\":0,\"sb\":0,\"sf\":0,\"customData\":{\"color\":[0,1,0.254],\"strobeColor\":[1,0,0.999]}}]}]}," +
            "{\"b\":1.5,\"g\":1,\"e\":[{\"f\":{\"f\":2,\"p\":0,\"t\":1},\"d\":1,\"t\":1,\"e\":[" +
            "{\"b\":0,\"i\":2,\"c\":0,\"s\":0.5,\"f\":0,\"sb\":0,\"sf\":0}," +
            "{\"b\":0.5,\"i\":2,\"c\":0,\"s\":0.5,\"f\":0,\"sb\":0,\"sf\":0}]}]}]";

        object predecessor = UnpackBoxEvents(groups, 0, 0, 0f, 0).Last();
        object[] extensions = UnpackBoxEvents(groups, 1, 0, 1.5f, 0);
        Assert.That(extensions.Length, Is.EqualTo(2));
        InsertInOrder(predecessor, extensions[0], extensions[1]);

        foreach (object extension in extensions)
        {
            Assert.That(extension, Is.InstanceOf<ICustomData>());
            AssertKeyValues(((ICustomData)extension).customData, "color", 0f, 1f, 0.254f);
            AssertKeyValues(((ICustomData)extension).customData, "strobeColor", 1f, 0f, 0.999f);
        }
    }

    // The user's rendered invariant: the half beat before the extension must read identically to the
    // half beat after — every light strobes the same custom colors at the same cycle points. Samples
    // the real patched HandleColorChangeBeatmapEvent + FloatTween path on both sides of the boundary.
    [Test]
    public void RenderedStrobeMatchesBeforeAndAfterExtension()
    {
        const string groups =
            "[{\"b\":0,\"g\":1,\"e\":[{\"f\":{\"f\":2,\"p\":0,\"t\":1},\"d\":1,\"t\":1,\"e\":[" +
            "{\"b\":0,\"i\":1,\"c\":0,\"s\":0.7,\"f\":2,\"sb\":0.3,\"sf\":1,\"customData\":{\"color\":[0,1,0.254],\"strobeColor\":[1,0,0.999],\"cg_sb\":1,\"cg_sf\":true}}]}]}," +
            "{\"b\":0.5,\"g\":1,\"e\":[{\"f\":{\"f\":2,\"p\":0,\"t\":1},\"d\":1,\"t\":1,\"e\":[" +
            "{\"b\":0,\"i\":2,\"c\":0,\"s\":0.5,\"f\":0,\"sb\":0,\"sf\":0}]}]}," +
            "{\"b\":1,\"g\":1,\"e\":[{\"f\":{\"f\":2,\"p\":0,\"t\":1},\"d\":1,\"t\":1,\"e\":[" +
            "{\"b\":0,\"i\":2,\"c\":0,\"s\":0.5,\"f\":0,\"sb\":0,\"sf\":0}]}]}]";

        object predecessor = UnpackBoxEvents(groups, 0, 0, 0f, 0).Last();
        object extension = UnpackBoxEvents(groups, 1, 0, 0.5f, 0).Single();
        object tail = UnpackBoxEvents(groups, 2, 0, 1f, 0).Single();
        InsertInOrder(predecessor, extension, tail);

        WithColorEffect((effect, tween) =>
        {
            // Both intervals are 0.5s at oneBeatDuration=1s with strobeBeatFrequency=2, so 0.125s lands mid normal
            // phase and 0.45s lands late in the strobe phase where the authored strobe-fade ramp has completed.
            MethodInfo sample = tween.GetType().GetMethod("Sample")!;
            handler.Invoke(effect, [predecessor]);
            sample.Invoke(tween, [0.001f]);
            sample.Invoke(tween, [0.125f]);
            Color normalBefore = capturedColor;
            sample.Invoke(tween, [0.45f]);
            Color strobeBefore = capturedColor;

            handler.Invoke(effect, [extension]);
            sample.Invoke(tween, [0.001f]);
            sample.Invoke(tween, [0.125f]);
            Color normalAfter = capturedColor;
            sample.Invoke(tween, [0.45f]);
            Color strobeAfter = capturedColor;

            Assert.That(normalAfter.r, Is.EqualTo(normalBefore.r).Within(0.001f),
                "The normal channel must keep the predecessor's custom color across the extension.");
            Assert.That(normalAfter.g, Is.EqualTo(normalBefore.g).Within(0.001f));
            Assert.That(normalAfter.b, Is.EqualTo(normalBefore.b).Within(0.001f));
            Assert.That(strobeAfter.r, Is.EqualTo(strobeBefore.r).Within(0.001f),
                "The strobe channel must keep the predecessor's custom strobeColor across the extension.");
            Assert.That(strobeAfter.g, Is.EqualTo(strobeBefore.g).Within(0.001f));
            Assert.That(strobeAfter.b, Is.EqualTo(strobeBefore.b).Within(0.001f));
            Assert.That(strobeAfter.r, Is.GreaterThan(0.9f),
                "The strobe phase must show the authored strobeColor (r~1), not the environment scheme.");
        });
    }

    // The reported GoneForGood 99.25->107 shape: a box-level colorDistributions box owns an OEM instant
    // node and a custom-colored interpolate node; a plain OEM Extend in a later box then continues the
    // lane into a plain OEM node. ChroMapper resolves a usePrevious state through the predecessor's own
    // box/event distributions, so the extension interval must start from the predecessor's DISTRIBUTED
    // color and strobeColor — inheriting the merged customData dictionary alone drops the spatial
    // offset, and the extension box's own instructions stay ignored exactly like an in-box Extend.
    [Test]
    public void ExtensionInheritsPredecessorColorDistributionContext()
    {
        const string groups =
            "[{\"b\":0,\"g\":1,\"e\":[{\"f\":{\"f\":2,\"p\":0,\"t\":1},\"d\":1,\"t\":1," +
            "\"customData\":{\"colorDistributions\":[\"r,0.5,L,l\"],\"strobeColorDistributions\":[\"b,0.3,L,l\"]},\"e\":[" +
            "{\"b\":0,\"i\":0,\"c\":1,\"s\":0.7,\"f\":0,\"sb\":0,\"sf\":0}," +
            "{\"b\":0.75,\"i\":1,\"c\":0,\"s\":0.8,\"f\":2,\"sb\":0.4,\"sf\":0," +
            "\"customData\":{\"color\":[0.2,0.5,0.9],\"strobeColor\":[0.1,0.8,0.3]}}]}]}," +
            "{\"b\":1.5,\"g\":1,\"e\":[{\"f\":{\"f\":2,\"p\":0,\"t\":1},\"d\":1,\"t\":1," +
            "\"customData\":{\"colorDistributions\":[\"g,0.9,L,l\"]},\"e\":[" +
            "{\"b\":0,\"i\":2,\"c\":-1,\"s\":-1,\"f\":0,\"sb\":0,\"sf\":0}]}]}," +
            "{\"b\":2.5,\"g\":1,\"e\":[{\"f\":{\"f\":2,\"p\":0,\"t\":1},\"d\":1,\"t\":1,\"e\":[" +
            "{\"b\":0,\"i\":1,\"c\":0,\"s\":0.9,\"f\":2,\"sb\":0.5,\"sf\":0}]}]}]";

        // Element lane 1 sits at light progress 1 of the two-element group, so per-light instructions apply fully.
        object[] predecessors = UnpackBoxEvents(groups, groupIndex: 0, boxIndex: 0, groupBoxBeat: 0f, elementId: 1);
        object extension = UnpackBoxEvents(groups, 1, 0, 1.5f, 1).Single();
        object oemTail = UnpackBoxEvents(groups, 2, 0, 2.5f, 1).Single();
        object oemHead = predecessors[0];
        object customPredecessor = predecessors[1];
        InsertInOrder(oemHead, customPredecessor, extension, oemTail);

        WithColorEffect((effect, tween) =>
        {
            MethodInfo sample = tween.GetType().GetMethod("Sample")!;

            // Box-level distributions alone make the fully OEM lead node a custom endpoint for the lane.
            handler.Invoke(effect, [oemHead]);
            sample.Invoke(tween, [0f]);
            Assert.That(capturedColor.r, Is.EqualTo(1.2f).Within(0.001f),
                "The OEM lead node must take the box's +0.5r distribution: 0.7 gray + 0.5 = 1.2.");

            // The extension interval opens on the predecessor's distributed color — (0.2,0.5,0.9)+0.5r
            // with the predecessor's composed brightness alpha — never the raw merged color and never
            // the extension box's own +0.9g instruction.
            handler.Invoke(effect, [extension]);
            sample.Invoke(tween, [0f]);
            Assert.That(capturedColor.r, Is.EqualTo(0.7f).Within(0.001f),
                "Extension start must be the predecessor's DISTRIBUTED color, not raw customData (0.2) or the extension box's own distribution.");
            Assert.That(capturedColor.g, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(capturedColor.b, Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(capturedColor.a, Is.EqualTo(0.8f).Within(0.001f));

            // At 40% of the 1s, 2Hz interval the strobe phase is 0.8: the inherited strobeColor under the
            // predecessor box's +0.3b strobe distribution lerps toward the OEM tail's strobe endpoint.
            sample.Invoke(tween, [0.4f]);
            Assert.That(capturedColor.r, Is.EqualTo(0.42f).Within(0.001f),
                "Strobe phase must render the predecessor's distributed strobeColor (0.1,0.8,0.6)->(0.9,0.9,0.9) at t=0.4.");
            Assert.That(capturedColor.g, Is.EqualTo(0.84f).Within(0.001f));
            Assert.That(capturedColor.b, Is.EqualTo(0.72f).Within(0.001f));
            Assert.That(capturedColor.a, Is.EqualTo(0.44f).Within(0.001f));

            // The undistributed OEM tail endpoint stays the native scheme color.
            sample.Invoke(tween, [1f]);
            Assert.That(capturedColor.r, Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(capturedColor.g, Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(capturedColor.b, Is.EqualTo(0.9f).Within(0.001f));
        });
    }

    // The purest form of the report: every node in the lane is OEM — the only custom data anywhere is
    // the lead box's colorDistributions. The extension still has to open its interval on the
    // predecessor's distributed OEM endpoint; with no merged "color" key the distribution context is
    // the only thing that can produce it.
    [Test]
    public void ExtensionInheritsDistributionContextFromOemPredecessor()
    {
        const string groups =
            "[{\"b\":0,\"g\":1,\"e\":[{\"f\":{\"f\":2,\"p\":0,\"t\":1},\"d\":1,\"t\":1," +
            "\"customData\":{\"colorDistributions\":[\"r,0.5,L,l\"]},\"e\":[" +
            "{\"b\":0,\"i\":0,\"c\":1,\"s\":0.7,\"f\":0,\"sb\":0,\"sf\":0}," +
            "{\"b\":0.75,\"i\":1,\"c\":0,\"s\":0.8,\"f\":0,\"sb\":0,\"sf\":0}]}]}," +
            "{\"b\":1.5,\"g\":1,\"e\":[{\"f\":{\"f\":2,\"p\":0,\"t\":1},\"d\":1,\"t\":1,\"e\":[" +
            "{\"b\":0,\"i\":2,\"c\":-1,\"s\":-1,\"f\":0,\"sb\":0,\"sf\":0}]}]}," +
            "{\"b\":2.5,\"g\":1,\"e\":[{\"f\":{\"f\":2,\"p\":0,\"t\":1},\"d\":1,\"t\":1,\"e\":[" +
            "{\"b\":0,\"i\":1,\"c\":0,\"s\":0.9,\"f\":0,\"sb\":0,\"sf\":0}]}]}]";

        object[] predecessors = UnpackBoxEvents(groups, 0, 0, 0f, 1);
        object extension = UnpackBoxEvents(groups, 1, 0, 1.5f, 1).Single();
        object oemTail = UnpackBoxEvents(groups, 2, 0, 2.5f, 1).Single();
        InsertInOrder(predecessors[0], predecessors[1], extension, oemTail);

        Assert.That(extension, Is.InstanceOf<ICustomData>(),
            "An extension node must carry the merge carrier even when nothing authored any custom data.");

        WithColorEffect((effect, tween) =>
        {
            MethodInfo sample = tween.GetType().GetMethod("Sample")!;
            handler.Invoke(effect, [extension]);

            // The inherited predecessor is the OEM interpolate node: 0.8 gray + 0.5r = 1.3 at the
            // interval start, fading to the undistributed 0.9 OEM tail.
            sample.Invoke(tween, [0f]);
            Assert.That(capturedColor.r, Is.EqualTo(1.3f).Within(0.001f),
                "The OEM extension must open on the predecessor's distributed OEM color.");
            Assert.That(capturedColor.g, Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(capturedColor.b, Is.EqualTo(0.8f).Within(0.001f));
            sample.Invoke(tween, [0.5f]);
            Assert.That(capturedColor.r, Is.EqualTo(1.1f).Within(0.001f));
            sample.Invoke(tween, [1f]);
            Assert.That(capturedColor.r, Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(capturedColor.g, Is.EqualTo(0.9f).Within(0.001f));
        });
    }

    // Extension node identity must survive the wrap: it stays a usePreviousValue/Extend event so native
    // propagation, tween gating, and per-light linking all keep working.
    [Test]
    public void CarrierPreservesExtensionIdentityAndFields()
    {
        const string groups =
            "[{\"b\":0,\"g\":1,\"e\":[{\"f\":{\"f\":2,\"p\":0,\"t\":1},\"d\":1,\"t\":1,\"e\":[" +
            "{\"b\":0,\"i\":1,\"c\":0,\"s\":0.7,\"f\":0,\"sb\":0,\"sf\":0,\"customData\":{\"color\":[0,1,0.254]}}]}]}," +
            "{\"b\":1.5,\"g\":1,\"e\":[{\"f\":{\"f\":2,\"p\":0,\"t\":1},\"d\":1,\"t\":1,\"e\":[" +
            "{\"b\":0,\"i\":2,\"c\":0,\"s\":0.5,\"f\":0,\"sb\":0.4,\"sf\":1}]}]}]";

        object predecessor = UnpackBoxEvents(groups, 0, 0, 0f, 0).Last();
        object extension = UnpackBoxEvents(groups, 1, 0, 1.5f, 0).Single();
        InsertInOrder(predecessor, extension);

        object? usePrevious = GetMemberValue(extension, "usePreviousValue");
        object? transition = GetMemberValue(extension, "transitionType");
        Assert.That(usePrevious != null && (bool)usePrevious || transition != null && Convert.ToInt32(transition) == 2, Is.True,
            "The carrier must keep the native Extend/usePrevious flag so it stays inside the extension propagation path.");
        Assert.That(Convert.ToSingle(GetMemberValue(extension, "brightness")), Is.EqualTo(0.7f).Within(0.0001f),
            "CopyColorDataFrom must still clone the predecessor's native brightness onto the carrier.");
        Assert.That(Convert.ToInt32(GetMemberValue(extension, "groupId")), Is.EqualTo(1));
        Assert.That(Convert.ToInt32(GetMemberValue(extension, "elementId")), Is.EqualTo(0));
    }

    // Drives the real patched deserialize -> Convert -> Unpack pipeline for one box inside the group
    // JSON and returns the emitted runtime events for a single filter element lane.
    private object[] UnpackBoxEvents(string json, int groupIndex, int boxIndex, float groupBoxBeat, int elementId)
    {
        Type deserializer = plugin.GetType("ChromaGLS.HarmonyPatches.GlsDeserializePatch", true)!;
        MethodInfo deserialize = deserializer.GetMethod("Prefix", All)!;
        IList groups = (IList)Activator.CreateInstance(deserialize.GetParameters()[1].ParameterType)!;
        using JsonTextReader reader = new(new StringReader(json));
        deserialize.Invoke(null, [reader, groups]);
        object saveBox = ((IEnumerable)groups[groupIndex]!.GetType().GetProperty("eventBoxes")!.GetValue(groups[groupIndex])!).Cast<object>().ElementAt(boxIndex);
        Type loader = FindType("BeatmapDataLoaderVersion3.BeatmapDataLoader") ?? FindType("BeatmapDataLoader")!;
        Type nativeConverter = loader.GetNestedTypes(All).Single(type => type.Name.StartsWith("LightColorEventBoxConvert", StringComparison.Ordinal));
        MethodInfo convert = nativeConverter.GetMethods(All | BindingFlags.DeclaredOnly)
            .Single(method => method.Name == "Convert"
                && method.GetParameters().Length == 2
                && method.GetParameters()[0].ParameterType.IsInstanceOfType(saveBox));

        // Legacy converters accept LightGroupSO directly rather than ILightGroup; initialize only its managed element-count data.
        Type lightGroupType = convert.GetParameters()[1].ParameterType;
        object lightGroup = lightGroupType.IsInterface
            ? TestHostCompat.CreateProxy(lightGroupType, typeof(LightGroupProxy))
            : TestHostCompat.GetUninitializedObject(lightGroupType);
        if (!lightGroupType.IsInterface)
        {
            lightGroupType.GetField("_numberOfElements", All)!.SetValue(lightGroup, 2);
        }

        // Harmony is installed, so the production converter postfix and Unpack postfix run inside these native calls.
        object box = convert.Invoke(convert.IsStatic ? null : TestHostCompat.GetUninitializedObject(nativeConverter), [saveBox, lightGroup])!;
        MethodInfo unpack = box.GetType().GetMethod("Unpack", All)!;
        IList? output = null;
        object?[] args = unpack.GetParameters().Select(parameter =>
        {
            if (parameter.ParameterType.IsGenericType && parameter.ParameterType.GetGenericTypeDefinition() == typeof(List<>))
            {
                output = (IList)Activator.CreateInstance(parameter.ParameterType)!;
                return output;
            }

            if (parameter.ParameterType.IsInterface && parameter.ParameterType.Name.Contains("BeatToTime"))
            {
                return TestHostCompat.CreateProxy(parameter.ParameterType, typeof(BeatTimeProxy));
            }

            if (parameter.ParameterType.Name == "IBeatmapLightEventConverter")
            {
                ConstructorInfo constructor = FindType("BeatmapLightEventConverterNoConvert")!.GetConstructors().Single();
                return constructor.Invoke(constructor.GetParameters().Select(option => option.HasDefaultValue
                    ? option.DefaultValue
                    : Activator.CreateInstance(option.ParameterType)).ToArray());
            }

            return parameter.Name switch
            {
                "groupId" => (object)1,
                "elementId" => elementId,
                "groupBoxBeat" => groupBoxBeat,
                "maxBeat" => float.MaxValue,
                _ => Activator.CreateInstance(parameter.ParameterType),
            };
        }).ToArray();
        object? unpacked = unpack.Invoke(box, args);
        if (unpacked is IEnumerable enumerable)
        {
            Type listType = typeof(List<>).MakeGenericType(FindType("BeatmapEventData")!);
            output = (IList)Activator.CreateInstance(listType)!;
            foreach (object evt in enumerable)
            {
                output.Add(evt);
            }
        }

        Assert.That(output, Is.Not.Null);
        return output!.Cast<object>().ToArray();
    }

    // Runs the actual game insert processor on each event in list order: same-lane linking plus the
    // usePreviousValue/Extend CopyColorDataFrom propagation that the production fix hooks into.
    private void InsertInOrder(params object[] events)
    {
        object processor = Activator.CreateInstance(processorType)!;
        Type listType = typeof(LinkedList<>).MakeGenericType(dataItemType);
        object list = Activator.CreateInstance(listType)!;
        MethodInfo addLast = listType.GetMethod(nameof(LinkedList<>.AddLast), [dataItemType])!;
        foreach (object evt in events)
        {
            object node = addLast.Invoke(list, [evt])!;
            processInsertedData.Invoke(processor, [node]);
        }
    }

    private static object? GetMemberValue(object instance, string name)
    {
        Type type = instance.GetType();
        return (object?)type.GetProperty(name, All)?.GetValue(instance) ?? type.GetField(name, All)?.GetValue(instance);
    }

    private static void AssertKeyValues(CustomData data, string key, params float[] expected)
    {
        object? raw = data.Get<object>(key);
        Assert.That(raw, Is.InstanceOf<IList>(), $"Inherited customData must contain '{key}'.");
        IList values = (IList)raw!;
        Assert.That(values.Count, Is.EqualTo(expected.Length));
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.That(Convert.ToSingle(values[index]), Is.EqualTo(expected[index]).Within(0.0001f), $"Key '{key}' element {index}");
        }
    }

    private static Color capturedColor;

    private static bool CaptureColor(Color __1)
    {
        capturedColor = __1;
        return false;
    }

    private static bool SkipResume() => false;

    private static bool GetOemColor(float brightness, ref Color __result)
    {
        __result = new Color(brightness, brightness, brightness, brightness);
        return false;
    }

    // Replace only scene/native services; the plugin event patches, native handler, and FloatTween all execute normally.
    private void WithColorEffect(Action<object, object> run)
    {
        Harmony services = new("ChromaGLS.Tests.ExtensionServices");
        Type managerType = AccessTools.TypeByName("LightWithIdManager");
        Type tweenManagerType = AccessTools.TypeByName("SongTimeTweeningManager");
        Type nativeTweenManager = AccessTools.TypeByName("Tweening.TweeningManager");
        object effect = TestHostCompat.GetUninitializedObject(effectType);
        try
        {
            services.Patch(effectType.GetMethod("GetColor", All), prefix: new HarmonyMethod(typeof(GlsExtensionInheritanceTests), nameof(GetOemColor)));
            services.Patch(managerType.GetMethod("SetColorForId", All), prefix: new HarmonyMethod(typeof(GlsExtensionInheritanceTests), nameof(CaptureColor)));
            services.Patch(nativeTweenManager.GetMethod("ResumeTween", All), prefix: new HarmonyMethod(typeof(GlsExtensionInheritanceTests), nameof(SkipResume)));
            services.Patch(nativeTweenManager.GetMethod("KillAllTweens", All), prefix: new HarmonyMethod(typeof(GlsExtensionInheritanceTests), nameof(SkipResume)));
            object lightManager = TestHostCompat.GetUninitializedObject(managerType);
            FieldInfo? lights = managerType.GetField("_lights", All);
            if (lights != null)
            {
                lights.SetValue(lightManager, Array.CreateInstance(lights.FieldType.GetElementType()!, 0));
            }

            effectType.GetField("_lightManager", All)!.SetValue(effect, lightManager);
            effectType.GetField("_tweeningManager", All)!.SetValue(effect, TestHostCompat.GetUninitializedObject(tweenManagerType));
            Type bpmType = effectType.GetField("_bpmController", All)!.FieldType;
            effectType.GetField("_bpmController", All)!.SetValue(effect, TestHostCompat.CreateProxy(bpmType, typeof(BpmProxy)));
            MethodInfo setColor = effectType.GetMethod("SetColor", All)!;
            Type tweenType = AccessTools.TypeByName("Tweening.FloatTween");
            object tween = tweenType.GetConstructors().Single(ctor => ctor.GetParameters().Length == 6)
                .Invoke([0f, 1f, setColor.CreateDelegate(typeof(Action<float>), effect), 1f, Enum.Parse(tweenEaseType, "Linear"), 0f]);
            effectType.GetField("_floatTween", All)!.SetValue(effect, tween);
            run(effect, tween);
        }
        finally
        {
            // Native Cleanup would enter Unity's tween manager without the service stub; the real production postfix is exercised too.
            try
            {
                effectType.GetMethod("Cleanup", All)!.Invoke(effect, null);
            }
            finally
            {
                services.UnpatchSelf();
            }
        }
    }

    private static Type? FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name)).FirstOrDefault(type => type != null);

    public class LightGroupProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => 2;
    }

    public class BeatTimeProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => args![0];
    }

    public class BpmProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                "get_oneBeatDuration" => 1f,
                "get_currentBpm" => 60f,
                _ => targetMethod?.ReturnType is { IsValueType: true } returnType
                    ? Activator.CreateInstance(returnType)
                    : null,
            };
        }
    }
}
