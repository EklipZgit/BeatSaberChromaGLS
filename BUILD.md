# Building and Contributing

[Project overview](README.md) | [Using ChromaGLS](UsingChromaGLS.md)

This document covers development setup, builds, tests, and implementation details. Players and mappers should start with [Using ChromaGLS](UsingChromaGLS.md).

## Prerequisites

- PowerShell 7 or newer (`pwsh`).
- .NET SDK with support for the project's `net48` target. GitHub Actions uses .NET SDK 8.
- A modded Beat Saber installation for each version you want to build, including the [runtime dependencies](UsingChromaGLS.md#installation).
- Unity matching [Ferrofluid.Unity/ProjectSettings/ProjectVersion.txt](Ferrofluid.Unity/ProjectSettings/ProjectVersion.txt), with Windows build support, for the initial local shader-bundle build.
- Access to the Aeroluna GitHub Packages NuGet feed.

Game assemblies are referenced from the selected installation's `Beat Saber_Data/Managed`, `Libs`, and `Plugins` folders. BeatSaberModdingTools generates the manifest, packages the plugin, and deploys it to that installation.

### NuGet Authentication

The `BeatSaberModdingTools.Tasks.Luna` and `LunaBSMod.Tasks` packages use the Aeroluna GitHub Packages feed. GitHub requires authentication even for public packages.

Create a [GitHub personal access token](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/creating-a-personal-access-token) with `read:packages`, then add the source to your user-level NuGet configuration:

```powershell
dotnet nuget add source "https://nuget.pkg.github.com/Aeroluna/index.json" `
    --name "Aeroluna Github Packages" `
    --username "YOUR_GITHUB_USERNAME" `
    --password "YOUR_READ_PACKAGES_TOKEN" `
    --store-password-in-clear-text
```

This stores the token unencrypted in your user configuration. Protect that file, avoid leaving the token in shared shell history, and never commit credentials.

## Game Versions and Installation Paths

Local versioned configurations exist for **1.29.1, 1.34.2, 1.37.1, 1.40.8, 1.42.1, 1.44.1, and 1.44.2**. The default build/test sweep excludes 1.44.2 because compatible dependencies are incomplete. The current GitHub Actions plugin matrix covers 1.29.1 through 1.42.1, not the 1.44.x targets.

Each `BEATSABER_<version>` variable points at the installation root containing `Beat Saber.exe`, `Beat Saber_Data`, `Libs`, and `Plugins`. Dots in the version become underscores in the variable name.

On Windows, configure persistent user variables for the installations you have:

```powershell
[Environment]::SetEnvironmentVariable("BEATSABER_1_29_1", "C:\path\to\1.29.1", "User")
[Environment]::SetEnvironmentVariable("BEATSABER_1_34_2", "C:\path\to\1.34.2", "User")
[Environment]::SetEnvironmentVariable("BEATSABER_1_37_1", "C:\path\to\1.37.1", "User")
[Environment]::SetEnvironmentVariable("BEATSABER_1_40_8", "C:\path\to\1.40.8", "User")
[Environment]::SetEnvironmentVariable("BEATSABER_1_42_1", "C:\path\to\1.42.1", "User")
[Environment]::SetEnvironmentVariable("BEATSABER_1_44_1", "C:\path\to\1.44.1", "User")
[Environment]::SetEnvironmentVariable("BEATSABER_1_44_2", "C:\path\to\1.44.2", "User")
```

Restart the terminal and IDE after changing persistent variables. A single-version build needs only that version's variable. The default sweep requires all six non-excluded installations.

## Building

Run the repository script from the repository root so the embedded shader bundle and version-specific packaging stay consistent:

```powershell
# One version, Release configuration.
pwsh ./BuildAllVersions.ps1 -Version 1.44.1 -Release

# All default versions, Release configurations.
pwsh ./BuildAllVersions.ps1 -Release

# Debug is the default when -Release is omitted.
pwsh ./BuildAllVersions.ps1 -Version 1.40.8
```

Use your preconfigured installation paths rather than overriding `BEATSABER_*` for a build. Outputs are under `ChromaGLS/bin/<configuration>/net48/`, including the DLL and packaged ZIP.

**Building also deploys the plugin.** Close Beat Saber before rebuilding and restart it afterward. BSIPA may queue a deployment while the game is running, and an already-loaded assembly will not be replaced in memory.

Normal builds expose only the public enable toggles. To include the experimental ferrofluid tuning menu:

```powershell
pwsh ./BuildAllVersions.ps1 -Version 1.44.1 -Release -FerrofluidTweaks
```

Tuning values are session-only and reset on restart. The main plugin and ferrofluid enable preferences persist.

### Building from an IDE

In Rider or Visual Studio, select a versioned solution configuration such as `Debug-1.40.8` or `Release-1.42.1`. Plain `Debug`/`Release` do not select a game version.

The matching `BEATSABER_*` variable is read automatically. Game-path resolution takes precedence in this order:

1. MSBuild global properties such as `-p:BeatSaberDir=...` or `-p:GameDirectory=...`.
2. A gitignored `Directory.Build.props.user` next to `Directory.Build.props`.
3. The matching `BEATSABER_<version>` variable.
4. A `BeatSaberDir` or `GameDirectory` environment variable.

An optional local override looks like:

```xml
<Project>
  <PropertyGroup>
    <BeatSaberDir>C:\path\to\Beat Saber install</BeatSaberDir>
  </PropertyGroup>
</Project>
```

Rider also accepts `BeatSaberDir` under **Settings > Build, Execution, Deployment > Toolset and Build > MSBuild global properties**. A global override applies to every configuration, so use it only when that is intentional.

If shader or bundle inputs changed, run `BuildAllVersions.ps1` before relying on an IDE build. Successful IDE builds also copy the plugin into the selected installation.

## Ferrofluid Bundle and GitHub Actions

The workflow compiles `ferrofluid-windows.bundle` from `Ferrofluid.Unity` using the Unity version in `ProjectSettings/ProjectVersion.txt`. It uploads the bundle as an intermediate Actions artifact, downloads it into each plugin build, and embeds it in `ChromaGLS.dll`. Only plugin packages go into the draft release.

Commit the shaders and includes, editor scripts, `Packages/manifest.json`, `Packages/packages-lock.json`, `ProjectSettings/ProjectVersion.txt`, and the original `FerroLUT.png` and `env-map-01.jpg` textures in `Ferrofluid.Unity/SourceTextures`. A separate prototype checkout is not required. The compiled bundle, local hash stamp, Unity caches, XR settings, and generated stereo scene are ignored.

Local builds rebuild the bundle when its recorded inputs change. To rebuild or verify only the asset:

```powershell
pwsh ./BuildFerrofluidBundle.ps1 -Force
pwsh ./BuildFerrofluidBundle.ps1 -VerifyOnly
```

Pass `-UnityEditorPath` if the editor is installed elsewhere. A clean checkout needs Unity for the first local bundle build. GitHub Actions supplies Unity through GameCI.

### Unity Personal License Setup

Add these in the repository's [Actions secrets settings](https://github.com/EklipZgit/BeatSaberChromaGLS/settings/secrets/actions):

| Secret | Value |
|---|---|
| `UNITY_EMAIL` | The Unity account email. |
| `UNITY_PASSWORD` | The Unity account password. |

The workflow uses GameCI Builder 6.0.0 with CLI 0.1.72. Its [Personal activation handler](https://github.com/game-ci/cli/blob/v0.1.72/dist/platforms/ubuntu/steps/activate.sh) requests a Personal seat with those credentials and returns it after the build. No `UNITY_LICENSE` secret or `.ulf` file is required. Store credentials only as GitHub secrets.

Unity Hub may store a named-user entitlement at `%LOCALAPPDATA%\Unity\licenses\UnityEntitlementLicense.xml`. That is not the serial-based `.ulf` file expected by older GameCI instructions. Do not rename it or copy it into `UNITY_LICENSE`. See [Unity's license locations](https://docs.unity3d.com/6000.3/Documentation/Manual/ActivationFAQ.html#licensefilefolders).

Run **Actions > Build and release > Run workflow**, or push a build-related change to `main`. The workflow builds the bundle once, builds the configured plugin matrix, and prepares a draft release. Missing license secrets fail before Unity starts.

## Running Managed Tests

`ChromaGLS.Tests` is a self-running **net48 NUnitLite executable hosted by Unity's bundled Mono**. It is not a .NET 8/VSTest project. It loads the built plugin and the matching game's assemblies.

From the repository root:

```powershell
pwsh ./RunAllTests.ps1 -Version 1.40.8
pwsh ./RunAllTests.ps1

# Run a specific fixture.
pwsh ./RunAllTests.ps1 -Version 1.44.1 -Test ChromaGLS.Tests.GlsColorDistributionTests
pwsh ./RunAllTests.ps1 -Version 1.44.1 -Test ChromaGLS.Tests.GlsStrobeTransitionTests
```

The script builds the Release plugin and tests through `BuildAllVersions.ps1`, then runs them under Unity's MonoBleedingEdge runtime. Builds also deploy to the selected installation. The default matrix excludes 1.44.2.

Mono is discovered from Unity Hub on Windows, or can be supplied with `-MonoPath`. Mono can fail during shutdown after writing results, so the script uses a fresh NUnit XML report to determine the test outcome and reports a nonzero process exit separately.

### Running Tests in Rider

Rider's normal NUnit gutter actions and Unit Tests window on Windows use desktop .NET Framework, which cannot host all of the game's APIs. Use a **.NET Project run configuration with Mono** instead:

1. Set **Settings > Build, Execution, Deployment > Toolset and Build > Mono executable path** to Unity's bundled Mono, for example `C:\Program Files\Unity\Hub\Editor\6000.3.13f1\Editor\Data\MonoBleedingEdge\bin\mono.exe`.
2. Add a **.NET Project** configuration for **ChromaGLS.Tests**, framework **net48**, runtime **Mono**.
3. Select a matching versioned solution configuration. Keep the default working directory at the test executable's output directory and build before launching. Refresh changed bundle inputs with `BuildAllVersions.ps1`.
4. Leave program arguments empty for the whole suite, or use `--test=ChromaGLS.Tests.GlsStrobeTransitionTests` for one fixture.

Use this run configuration rather than the NUnit gutter action. Results appear in the console and `TestResult.xml` beside the executable.

NUnit's one-time setup registers the standalone host's Unity internal-call substitutes even when a Mono runner bypasses `Main`. These support managed math and patch tests, not actual GPU rendering. Unsupported desktop CLR hosts fail at setup with runtime instructions.

In Visual Studio, run the PowerShell test script from the terminal rather than Test Explorer. `dotnet test` is not this project's runner.

### Coverage and Rendering Checks

Color-distribution coverage includes chunk/per-light progress, tolerant parsing, box/event composition, independent alpha and HDR RGB, production conversion/unpack behavior, and context surviving gameplay event copies. This coverage has run against 1.29.1, 1.34.2, 1.37.1, 1.40.8, 1.42.1, and 1.44.1.

ChroMapper has separate filter and preview tests. Run them from a checkout containing the feature:

```powershell
pwsh ./Assets/Tests/Test-ChroMapper.ps1 -TestFilter 'TestsEditMode.IndexFilterHelperTest'
pwsh ./Assets/Tests/Test-ChroMapper.ps1 -TestFilter 'Tests.Editor.GLSEventAppearanceTest'
```

Only a Unity editor open on that exact ChroMapper project path blocks its CLI runner. A different worktree's editor does not.

For ferrofluid GPU interaction and stereo rendering checks, use the repository's Unity harnesses:

```powershell
pwsh ./TestFerrofluidInteraction.ps1
pwsh ./TestFerrofluidStereo.ps1
```

The stereo harness builds a standalone Mock HMD player and captures both eyes. `-UseSourceShader` selects the project-compiled surface shader instead of the embedded bundle. GPU harnesses are separate from the managed NUnit suite.

**Managed tests do not verify final pixels or every live Harmony attachment.** Restart Beat Saber for visual checks, confirm the intended plugin and game version in that installation's live `Logs/_latest.log` or `Logs_latest.log`, and look for `Gameplay patch installation completed`. Recurring color-distribution diagnostic traces have been removed, so do not rely on old `converted box`, `unpacked group`, or `event lookup` messages.

## How It Works

ChromaGLS retains GLS `customData` through deserialization, conversion, and gameplay copies. Native GLS still schedules events and selects lights. Event activation composes unclamped RGBA endpoints, multiplying normal alpha by GLS brightness and strobe alpha by `sb`.

Transitions use the next event for each affected light, including events from another box. Custom easing, HSV interpolation, pulse widths, and independent strobe colors use custom rendering where native fields cannot express the requested result. Beat Saber 1.29.1 also needs the newer strobe brightness/fade behavior backported.

The converter prepares parsed color-distribution instructions and per-light chunk/light progress. Runtime event copies retain that immutable context, so playback does not need to parse strings or scan filters every frame.

Use build-time publicized fields and direct typed access where available, rather than runtime reflection or cached field-access delegates. Handle version-specific fields at compile time.

Debug builds show the update banner for any published stable release that passes the game-version compatibility check, even if its plugin version is equal to or older than the local build. Release builds show it only for an eligible upgrade.

### Reading Authored Data in Another Mod

Test a runtime color event for `CustomJSONData.CustomBeatmap.ICustomData`, not a specific CJD subclass. Color-distributed events implement the interface while retaining native callback routing and copy behavior:

```csharp
using System;
using System.Collections.Generic;
using CustomJSONData.CustomBeatmap;

// Inside your color-event handler:
if (lightColorEventData is ICustomData customEvent)
{
    List<object>? color = customEvent.customData.Get<List<object>>("color");
    if (color != null && color.Count >= 3)
    {
        float r = Convert.ToSingle(color[0]);
        float g = Convert.ToSingle(color[1]);
        float b = Convert.ToSingle(color[2]);
        float a = color.Count > 3 ? Convert.ToSingle(color[3]) : 1f;
    }
}
```

This reads the **authored** color, not the distributed, brightness-composed output for that physical light. Box-only distributions may have no event-level `color` key.

## Future Work

Ideas include creating new GLS light groups in an environment and importing Basic Event or GLS lights across environments. These are possible future extensions, not features currently provided by ChromaGLS.
