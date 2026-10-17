using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ChromaGLS.Tests;

[TestFixture]
internal sealed class UpdateCheckTests
{
    [Test]
    public void DebugOverrideMatchesThePluginBuildConfiguration()
    {
        Assembly assembly = Assembly.Load("ChromaGLS");
        bool debug = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration.StartsWith("Debug-", StringComparison.Ordinal);
        Assert.That(ProductionType("StartupUpdateCheck").GetField("IsDebugBuild")!.GetRawConstantValue(), Is.EqualTo(debug));
    }

    [TestCase("1.0.2", "1.37.1", false, true)]
    [TestCase("1.0.3", "1.37.1", false, false)]
    [TestCase("1.0.45", "1.37.1", false, false)]
    [TestCase("1.0.3+1.37.1", "1.37.1", false, false)]
    [TestCase("1.0.3-beta.1", "1.37.1", false, true)]
    [TestCase("1.0.45", "1.37.1", true, true)]
    [TestCase("1.0.2", "1.29.1", false, false)]
    [TestCase("1.0.45", "1.29.1", true, false)]
    [TestCase("1.0.2", "1.34.2", false, true)]
    [TestCase("1.0.2", "1.44.1_abc", false, true)]
    public void ComparesPluginVersionAndOldestPublishedGameVersion(
        string installedVersion,
        string gameVersion,
        bool debugBuild,
        bool expected)
    {
        Assert.That(Evaluate(Release(), installedVersion, gameVersion, debugBuild) != null, Is.EqualTo(expected));
    }

    [TestCase("v1.0.3")]
    [TestCase("v2026-08-31-1.0.3")]
    public void ReturnsVersionAndBrowserReleasePage(string tag)
    {
        JObject release = Release();
        release["tag_name"] = tag;
        object? update = Evaluate(release, "1.0.2", "1.37.1", false);
        Assert.That(update, Is.Not.Null);
        Assert.That(Property(update!, "Version"), Is.EqualTo("1.0.3"));
        Assert.That(Property(update!, "ReleaseUrl"), Is.EqualTo(release["html_url"]!.Value<string>()));
    }

    [TestCase("1.0.2", "1.0.2")]
    [TestCase("1.0.2-beta.1", "1.0.2-beta.1")]
    [TestCase("1.0.2+1.37.1", "1.0.2")]
    [TestCase("1.0.2_abc", "1.0.2")]
    public void ReturnsInstalledVersionWithoutBuildMetadata(string installedVersion, string expectedInstalled)
    {
        object? update = Evaluate(Release(), installedVersion, "1.37.1", true);
        Assert.That(update, Is.Not.Null);
        Assert.That(Property(update!, "InstalledVersion"), Is.EqualTo(expectedInstalled));
        Assert.That(Property(update!, "Version"), Is.EqualTo("1.0.3"));
    }

    [Test]
    public async Task UpdateBannerVersionTextStaysEmptyWhileCheckIsPending()
    {
        TaskCompletionSource<string> response = new();
        object presenter = CreatePresenter(CreateChecker(() => response.Task), _ => { }, _ => { });
        Assert.That(BannerVersionText(presenter), Is.EqualTo(string.Empty));
        Task initialize = InitializeBanner(presenter);
        Assert.That(BannerVersionText(presenter), Is.EqualTo(string.Empty), "The version line must stay empty until the release response resolves.");
        response.SetResult(Release().ToString());
        await initialize;
        Assert.That(BannerVersionText(presenter), Is.EqualTo("1.0.2 -> 1.0.3"), "A resolved release must produce the installed -> latest pair.");
    }

    [Test]
    public async Task UpdateBannerVersionTextIsSetBeforeRevealWhenResponseArrivesFirst()
    {
        string? atReveal = null;
        object? presenter = null;
        presenter = CreatePresenter(
            CreateChecker(() => Task.FromResult(Release().ToString())),
            visible =>
            {
                if (visible)
                {
                    atReveal = BannerVersionText(presenter!);
                }
            },
            _ => { });
        await InitializeBanner(presenter);
        SetBannerMenuActive(presenter, true);
        Assert.That(atReveal, Is.EqualTo("1.0.2 -> 1.0.3"), "The reveal callback must see the populated version pair.");
    }

    [Test]
    public async Task UpdateBannerVersionTextIsSetBeforeRevealInsideActiveMenu()
    {
        string? atReveal = null;
        object? presenter = null;
        TaskCompletionSource<string> response = new();
        presenter = CreatePresenter(
            CreateChecker(() => response.Task),
            visible =>
            {
                if (visible)
                {
                    atReveal = BannerVersionText(presenter!);
                }
            },
            _ => { });
        SetBannerMenuActive(presenter, true);
        Task initialize = InitializeBanner(presenter);
        response.SetResult(Release().ToString());
        await initialize;
        Assert.That(atReveal, Is.EqualTo("1.0.2 -> 1.0.3"), "The reveal callback must see the populated version pair.");
    }

    [Test]
    public async Task UpdateBannerVersionTextStaysEmptyWhenDisposedBeforeResponse()
    {
        TaskCompletionSource<string> response = new();
        object presenter = CreatePresenter(CreateChecker(() => response.Task), _ => { }, _ => { });
        Task initialize = InitializeBanner(presenter);
        DisposeBanner(presenter);
        response.SetResult(Release().ToString());
        await initialize;
        Assert.That(BannerVersionText(presenter), Is.EqualTo(string.Empty), "A disposed banner must never populate the version line.");
    }

    [Test]
    public async Task UpdateBannerVersionTextStaysEmptyWithoutAnUpdate()
    {
        object presenter = CreatePresenter(CreateChecker(() => Task.FromResult("invalid JSON")), _ => { }, _ => { });
        await InitializeBanner(presenter);
        Assert.That(BannerVersionText(presenter), Is.EqualTo(string.Empty), "A failed or empty check must leave the version line empty.");
    }

    [TestCase("draft")]
    [TestCase("prerelease")]
    public void IgnoresUnpublishedAndPreviewReleasesEvenInDebug(string flag)
    {
        JObject release = Release();
        release[flag] = true;
        Assert.That(Evaluate(release, "1.0.2", "1.37.1", true), Is.Null);
    }

    [TestCase("tag_name", "not-a-version")]
    [TestCase("tag_name", "v2026-08-31-1.0.3-beta")]
    [TestCase("html_url", "https://example.com/release")]
    [TestCase("html_url", "http://github.com/EklipZgit/BeatSaberChromaGLS/releases/tag/v1.0.3")]
    public void IgnoresInvalidReleaseMetadata(string key, string value)
    {
        JObject release = Release();
        release[key] = value;
        Assert.That(Evaluate(release, "1.0.2", "1.37.1", true), Is.Null);
    }

    [Test]
    public void RequiresPublishedChromaGlsGameArchives()
    {
        JObject release = Release();
        release["assets"] = new JArray(new JObject { ["name"] = "OtherMod-bs1.29.1.zip" });
        Assert.That(Evaluate(release, "1.0.2", "1.37.1", true), Is.Null);
        release.Remove("assets");
        Assert.That(Evaluate(release, "1.0.2", "1.37.1", true), Is.Null);
    }

    [Test]
    public void ComparesNumericVersionComponents()
    {
        JObject release = Release();
        release["tag_name"] = "v2026-10-15-1.0.10";
        Assert.That(Evaluate(release, "1.0.9", "1.37.1", false), Is.Not.Null);
    }

    [Test]
    public async Task StartupCheckSharesOneRequestAcrossMenuRecreation()
    {
        int requests = 0;
        TaskCompletionSource<string> response = new();
        object checker = CreateChecker(() =>
        {
            requests++;
            return response.Task;
        });
        Task first = Check(checker);
        Task second = Check(checker);
        Assert.That(second, Is.SameAs(first));
        Assert.That(requests, Is.EqualTo(1));
        response.SetResult(Release().ToString());
        await first;
        Assert.That(Property(first, "Result"), Is.Not.Null);
        Assert.That(Check(checker), Is.SameAs(first));
        Assert.That(requests, Is.EqualTo(1));
    }

    [TestCase("network")]
    [TestCase("timeout")]
    [TestCase("json")]
    public async Task FailedChecksStayQuietAndAreNotRetried(string failure)
    {
        int requests = 0;
        object checker = CreateChecker(() =>
        {
            requests++;
            if (failure == "network")
                throw new System.Net.Http.HttpRequestException("Offline");

            if (failure == "timeout")
                throw new TaskCanceledException();

            return Task.FromResult("invalid JSON");
        });
        Task check = Check(checker);
        await check;
        Assert.That(Property(check, "Result"), Is.Null);
        Assert.That(Check(checker), Is.SameAs(check));
        Assert.That(requests, Is.EqualTo(1));
    }

    [Test]
    public async Task UpdateBannerWaitsForMenuActivationBeforeShowing()
    {
        List<bool> shown = new();
        List<string> opened = new();
        TaskCompletionSource<string> response = new();
        object presenter = CreatePresenter(CreateChecker(() => response.Task), shown.Add, opened.Add);
        SetBannerMenuActive(presenter, false);
        Task initialize = InitializeBanner(presenter);
        response.SetResult(Release().ToString());
        await initialize;
        Assert.That(shown, Is.Empty, "An update that arrives before the main menu activates must stay hidden.");
        SetBannerMenuActive(presenter, true);
        Assert.That(shown, Is.EqualTo(new[] { true }), "Activating the main menu with an available update must show the banner.");
        OpenBannerReleases(presenter);
        Assert.That(opened, Is.EqualTo(new[] { "https://github.com/EklipZgit/BeatSaberChromaGLS/releases" }), "The link must open the general releases page, not a tag-specific URL.");
    }

    [Test]
    public async Task UpdateBannerAppearsWhenResponseArrivesInsideActiveMenu()
    {
        List<bool> shown = new();
        TaskCompletionSource<string> response = new();
        object presenter = CreatePresenter(CreateChecker(() => response.Task), shown.Add, _ => { });
        SetBannerMenuActive(presenter, true);
        Task initialize = InitializeBanner(presenter);
        Assert.That(shown, Is.Empty, "Nothing may render while the release request is still pending.");
        response.SetResult(Release().ToString());
        await initialize;
        Assert.That(shown, Is.EqualTo(new[] { true }), "A response arriving while the menu is active must show the banner immediately.");
    }

    [Test]
    public async Task UpdateBannerHidesOnDeactivateAndBlocksClicks()
    {
        List<bool> shown = new();
        List<string> opened = new();
        object presenter = CreatePresenter(CreateChecker(() => Task.FromResult(Release().ToString())), shown.Add, opened.Add);
        SetBannerMenuActive(presenter, true);
        await InitializeBanner(presenter);
        SetBannerMenuActive(presenter, false);
        Assert.That(shown, Is.EqualTo(new[] { true, false }), "Leaving the main menu must hide the banner.");
        OpenBannerReleases(presenter);
        Assert.That(opened, Is.Empty, "A click cannot reach the browser while the banner is hidden.");
    }

    [Test]
    public async Task UpdateBannerReactivatesWithoutRefetchingOrDuplicating()
    {
        int requests = 0;
        List<bool> shown = new();
        object presenter = CreatePresenter(
            CreateChecker(() =>
            {
                requests++;
                return Task.FromResult(Release().ToString());
            }),
            shown.Add,
            _ => { });
        SetBannerMenuActive(presenter, true);
        await InitializeBanner(presenter);
        SetBannerMenuActive(presenter, false);
        SetBannerMenuActive(presenter, true);
        Assert.That(shown, Is.EqualTo(new[] { true, false, true }), "Returning from gameplay must re-show the same banner without restarting the check.");
        Assert.That(requests, Is.EqualTo(1), "Menu reactivation must reuse the cached startup request.");
    }

    [Test]
    public async Task UpdateBannerDisposedBeforeResponseStaysSilent()
    {
        List<bool> shown = new();
        List<string> opened = new();
        TaskCompletionSource<string> response = new();
        object presenter = CreatePresenter(CreateChecker(() => response.Task), shown.Add, opened.Add);
        SetBannerMenuActive(presenter, true);
        Task initialize = InitializeBanner(presenter);
        DisposeBanner(presenter);
        response.SetResult(Release().ToString());
        await initialize;
        SetBannerMenuActive(presenter, true);
        OpenBannerReleases(presenter);
        Assert.That(shown, Is.Empty, "A disposed banner must never become visible from a late response.");
        Assert.That(opened, Is.Empty, "A disposed banner must never open the browser.");
    }

    [Test]
    public async Task UpdateBannerDisposeAfterShowingHidesOnceThenStaysSilent()
    {
        List<bool> shown = new();
        object presenter = CreatePresenter(CreateChecker(() => Task.FromResult(Release().ToString())), shown.Add, _ => { });
        SetBannerMenuActive(presenter, true);
        await InitializeBanner(presenter);
        DisposeBanner(presenter);
        SetBannerMenuActive(presenter, false);
        SetBannerMenuActive(presenter, true);
        Assert.That(shown, Is.EqualTo(new[] { true, false }), "Dispose must hide the banner exactly once and ignore later menu events.");
    }

    [Test]
    public async Task UpdateBannerNeverShowsWithoutAnUpdate()
    {
        List<bool> shown = new();
        List<string> opened = new();
        object presenter = CreatePresenter(CreateChecker(() => Task.FromResult("invalid JSON")), shown.Add, opened.Add);
        SetBannerMenuActive(presenter, true);
        await InitializeBanner(presenter);
        OpenBannerReleases(presenter);
        Assert.That(shown, Is.Empty, "A failed or empty check must leave the menu untouched.");
        Assert.That(opened, Is.Empty);
    }

    [Test]
    public async Task UpdateBannerDoesNotOpenBrowserOnResponseArrival()
    {
        List<string> opened = new();
        object presenter = CreatePresenter(CreateChecker(() => Task.FromResult(Release().ToString())), _ => { }, opened.Add);
        SetBannerMenuActive(presenter, true);
        await InitializeBanner(presenter);
        Assert.That(opened, Is.Empty, "Showing the banner must never launch the browser by itself.");
        OpenBannerReleases(presenter);
        OpenBannerReleases(presenter);
        Assert.That(opened, Has.Count.EqualTo(2), "Each deliberate click opens the browser once.");
    }

    [Test]
    public void UpdateBannerMarkupDeclaresTheExactThreeLineNotice()
    {
        using Stream? stream = Assembly.Load("ChromaGLS").GetManifestResourceStream("ChromaGLS.Updates.update-banner.bsml");
        Assert.That(stream, Is.Not.Null, "The header banner layout must ship as an embedded BSML resource.");
        using StreamReader reader = new(stream!);
        System.Xml.Linq.XDocument markup = System.Xml.Linq.XDocument.Parse(reader.ReadToEnd());
        System.Xml.Linq.XElement? message = markup.Descendants()
            .FirstOrDefault(e => e.Attribute("id")?.Value == "update-banner-message");
        Assert.That(message, Is.Not.Null, "The first banner line needs its message element.");
        Assert.That(
            message!.Attribute("text")?.Value,
            Is.EqualTo("ChromaGLS has an update available!"),
            "The first banner line must be the exact notice text.");
        System.Xml.Linq.XElement? versions = markup.Descendants()
            .FirstOrDefault(e => e.Attribute("id")?.Value == "update-banner-versions");
        Assert.That(versions, Is.Not.Null, "The second banner line needs the versions element the host fills in.");
        Assert.That(versions!.Attribute("text")?.Value, Is.EqualTo(string.Empty), "The versions line starts empty until the release response resolves.");
        System.Xml.Linq.XElement? link = markup.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "clickable-text");
        Assert.That(link, Is.Not.Null, "The third line needs a dedicated clickable link element.");
        Assert.That(link!.Attribute("id")?.Value, Is.EqualTo("update-banner-link"));
        Assert.That(link.Attribute("text")?.Value, Is.EqualTo("<u>Find the latest here</u>"), "The whole underlined phrase is the clickable link text.");
        Assert.That(link.Attribute("on-click")?.Value, Is.EqualTo("open-releases"), "The link must dispatch to the open-releases UI action.");
        Type controller = Assembly.Load("ChromaGLS").GetType("ChromaGLS.Updates.UpdateMenuButton")!;
        bool hostActionExists = controller
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Any(method => method.GetCustomAttributesData().Any(
                attribute => attribute.AttributeType.Name == "UIAction"
                    && Equals(attribute.ConstructorArguments.FirstOrDefault().Value, "open-releases")));
        Assert.That(hostActionExists, Is.True, "The banner host must expose the open-releases UI action the markup dispatches to.");
    }

    [Test]
    public async Task UpdateBannerSurvivesViewControllerSwapsInsideOneMenuContext()
    {
        int requests = 0;
        List<bool> shown = new();
        List<string> opened = new();
        object presenter = CreatePresenter(
            CreateChecker(() =>
            {
                requests++;
                return Task.FromResult(Release().ToString());
            }),
            shown.Add,
            opened.Add);
        object lifecycle = FormatterServices.GetUninitializedObject(ProductionType("UpdateBannerLifecycle"));
        InvokeBanner(lifecycle, "Initialize", presenter);

        InvokeBanner(lifecycle, "OnEnable");
        await InitializeBanner(presenter);
        Assert.That(shown, Is.EqualTo(new[] { true }), "Menu activation with an available update must show the banner.");

        OpenBannerReleases(presenter);
        Assert.That(opened, Is.EqualTo(new[] { "https://github.com/EklipZgit/BeatSaberChromaGLS/releases" }), "The banner must stay clickable while another view controller replaces the main menu.");
        Assert.That(shown, Is.EqualTo(new[] { true }), "A view-controller swap inside one menu context must not hide the banner.");

        InvokeBanner(lifecycle, "OnDisable");
        Assert.That(shown, Is.EqualTo(new[] { true, false }), "Gameplay or menu-scene deactivation must hide the banner once.");
        InvokeBanner(lifecycle, "OnEnable");
        Assert.That(shown, Is.EqualTo(new[] { true, false, true }), "Returning to the menu must re-show the same banner.");
        Assert.That(requests, Is.EqualTo(1), "Menu re-entry must reuse the cached startup request.");
    }

    [Test]
    public void UpdateBannerPulseIntensityStaysBoundedAndKeepsPulsing()
    {
        MethodInfo evaluate = ProductionType("UpdateBannerPulse")
            .GetMethod("EvaluatePulseIntensity", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        float lowest = float.MaxValue;
        float highest = float.MinValue;
        for (int i = 0; i < 200; i++)
        {
            float intensity = (float)evaluate.Invoke(null, new object[] { i * 0.037f })!;
            Assert.That(intensity, Is.InRange(0.4f, 1f), "The border pulse must stay inside its authored intensity range.");
            lowest = Math.Min(lowest, intensity);
            highest = Math.Max(highest, intensity);
        }

        Assert.That(highest - lowest, Is.GreaterThan(0.3f), "The border intensity must visibly pulse rather than sit at a constant value.");
    }

    private static void InvokeBanner(object target, string method, params object[] arguments)
    {
        target.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .Invoke(target, arguments);
    }

    private static object CreatePresenter(object checker, Action<bool> setVisible, Action<string> openUrl)
    {
        Type? presenterType = Assembly.Load("ChromaGLS").GetType("ChromaGLS.Updates.UpdateBannerPresenter");
        Assert.That(presenterType, Is.Not.Null, "The banner lifecycle must live in a testable presenter decoupled from native Unity objects.");
        return Activator.CreateInstance(presenterType!, checker, setVisible, openUrl)!;
    }

    private static Task InitializeBanner(object presenter)
    {
        return (Task)presenter.GetType().GetMethod("InitializeAsync")!.Invoke(presenter, null)!;
    }

    private static void SetBannerMenuActive(object presenter, bool active)
    {
        presenter.GetType()
            .GetMethod("SetMainMenuActive", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .Invoke(presenter, new object[] { active });
    }

    private static void OpenBannerReleases(object presenter)
    {
        presenter.GetType()
            .GetMethod("OpenReleases", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .Invoke(presenter, null);
    }

    private static void DisposeBanner(object presenter)
    {
        ((IDisposable)presenter).Dispose();
    }

    private static string BannerVersionText(object presenter)
    {
        return (string)presenter.GetType()
            .GetProperty("VersionText", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .GetValue(presenter)!;
    }

    private static JObject Release()
    {
        return JObject.Parse(@"{
            'tag_name': 'v2026-08-31-1.0.3',
            'html_url': 'https://github.com/EklipZgit/BeatSaberChromaGLS/releases/tag/v2026-08-31-1.0.3',
            'draft': false,
            'prerelease': false,
            'assets': [
                { 'name': 'ChromaGLS-1.0.3+1.42.1-bs1.42.1-dcc0ec1.zip' },
                { 'name': 'ChromaGLS-1.0.3-bs1.34.2.zip' },
                { 'name': 'ChromaGLS-1.0.3+1.37.1-bs1.37.1-dcc0ec1.zip' }
            ]
        }");
    }

    private static object? Evaluate(JObject release, string installedVersion, string gameVersion, bool debugBuild)
    {
        return ProductionType("AvailablePluginUpdate").GetMethod("Evaluate")!.Invoke(null, new object[]
        {
            release, installedVersion, gameVersion, debugBuild,
        });
    }

    private static object CreateChecker(Func<Task<string>> fetch)
    {
        return Activator.CreateInstance(ProductionType("StartupUpdateCheck"), fetch, "1.0.2", "1.37.1", false)!;
    }

    private static Task Check(object checker)
    {
        return (Task)checker.GetType().GetMethod("CheckOnceAsync")!.Invoke(checker, null)!;
    }

    private static object? Property(object instance, string name)
    {
        return instance.GetType().GetProperty(name)!.GetValue(instance);
    }

    private static Type ProductionType(string name)
    {
        Type? type = Assembly.Load("ChromaGLS").GetType("ChromaGLS.Updates." + name);
        Assert.That(type, Is.Not.Null, "The startup update check must be implemented in the plugin.");
        return type!;
    }
}
