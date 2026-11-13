using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using NUnit.Framework;

namespace ChromaGLS.Tests;

// Pins the shipped menu-section markup on both tab variants: global toggles first,
// then the Main Menu Tweaks section with Menu lights directly under its header,
// the renamed fidget switches, and the action buttons last.
[TestFixture]
public class MenuSectionLayoutTests
{
    private static readonly Assembly Plugin = Assembly.Load("ChromaGLS");

    private static List<XElement> MenuChildren(string resource)
    {
        using System.IO.Stream stream = Plugin.GetManifestResourceStream(resource)!;
        XElement root = XElement.Load(stream);
        return root.Descendants("settings-container").Single().Elements().ToList();
    }

    [TestCase("ChromaGLS.Settings.modifiers.bsml")]
    [TestCase("ChromaGLS.Settings.modifiers-minimal.bsml")]
    public void GlobalTogglesStayFirstThenFixedGapThenHeader(string resource)
    {
        List<XElement> children = MenuChildren(resource);
        Assert.That(children[0].Attribute("value")?.Value, Is.EqualTo("enabled"));
        Assert.That(children[1].Attribute("value")?.Value, Is.EqualTo("ferrofluid-border-enabled"));
        XElement spacer = children[2];
        Assert.That(double.Parse(spacer.Attribute("preferred-height")?.Value ?? "0"), Is.GreaterThanOrEqualTo(4), "the section header needs a fixed gap of at least 4 units");
        Assert.That(double.Parse(spacer.Attribute("min-height")?.Value ?? "0"), Is.GreaterThanOrEqualTo(4));
        Assert.That(spacer.Attribute("flexible-height")?.Value ?? "0", Is.EqualTo("0"), "the gap must not stretch");
        XElement header = children[3];
        Assert.That(header.Name.LocalName, Is.EqualTo("text"));
        Assert.That(header.Attribute("text")!.Value, Is.EqualTo("Main Menu Tweaks"));
        Assert.That(header.Attribute("align")?.Value, Is.EqualTo("Left"));
    }

    [TestCase("ChromaGLS.Settings.modifiers.bsml")]
    [TestCase("ChromaGLS.Settings.modifiers-minimal.bsml")]
    public void MenuLightsSitsUnderTheHeaderBeforeTheFidgetSwitches(string resource)
    {
        List<XElement> children = MenuChildren(resource);
        XElement menuLights = children.Single(e => e.Attribute("id")?.Value == "menu-floor-light-setting");
        int index = children.IndexOf(menuLights);
        Assert.That(index, Is.EqualTo(4), "Menu lights must sit immediately under the Main Menu Tweaks header");
        Assert.That(menuLights.Attribute("value")!.Value, Is.EqualTo("parabola-light-enabled"));
        XElement tweak = menuLights.Descendants("primary-button").Single();
        Assert.That(tweak.Attribute("id")!.Value, Is.EqualTo("menu-floor-light-tweak"));
        Assert.That(tweak.Attribute("on-click")!.Value, Is.EqualTo("parabola-open"));

        var expected = new (string Text, string Value)[]
        {
            ("Enable Ferrofluid Fidget Cube", "ferrofluid-cube-enabled"),
            ("Enable Ferrofluid Fidget Sphere", "ferrofluid-sphere-enabled"),
            ("Enable Fluid Fidget Cube", "fluid-cube-enabled"),
            ("Enable Fluid Fidget Sphere", "fluid-sphere-enabled"),
        };
        for (int i = 0; i < expected.Length; i++)
        {
            XElement toggle = children[index + 1 + i];
            Assert.That(toggle.Name.LocalName, Is.EqualTo("bool-setting"));
            Assert.That(toggle.Attribute("text")!.Value, Is.EqualTo(expected[i].Text));
            Assert.That(toggle.Attribute("value")!.Value, Is.EqualTo(expected[i].Value));
        }
    }

    [TestCase("ChromaGLS.Settings.modifiers.bsml", true)]
    [TestCase("ChromaGLS.Settings.modifiers-minimal.bsml", false)]
    public void ActionButtonsSitBelowTheSwitches(string resource, bool tweakBuild)
    {
        List<XElement> children = MenuChildren(resource);
        int lastSwitch = children.FindIndex(e => e.Attribute("value")?.Value == "fluid-sphere-enabled");
        XElement maps = children.Single(e => e.Attribute("id")?.Value == "reflection-maps-button");
        Assert.That(children.IndexOf(maps), Is.EqualTo(children.Count - 1), "the reflection-maps entry point stays the last menu row");
        Assert.That(children.IndexOf(maps), Is.GreaterThan(lastSwitch));
        XElement? tweak = children.FirstOrDefault(e => e.Attribute("id")?.Value == "ferro-tweak-button");
        Assert.That(tweak != null, Is.EqualTo(tweakBuild), "the ferro tweak button only exists in the session-tuning variant");
    }

    [TestCase("ChromaGLS.Settings.modifiers.bsml")]
    [TestCase("ChromaGLS.Settings.modifiers-minimal.bsml")]
    public void EverySettingBindingStaysUnique(string resource)
    {
        List<XElement> children = MenuChildren(resource);
        List<string> bindings = children.Select(e => e.Attribute("value")?.Value).Where(v => v != null).Cast<string>().ToList();
        Assert.That(bindings.Distinct().Count(), Is.EqualTo(bindings.Count), "each menu setting must keep a unique config binding");
        CollectionAssert.Contains(bindings, "parabola-light-enabled");
    }
}
