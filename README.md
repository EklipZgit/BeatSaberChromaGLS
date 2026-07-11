# ChromaGLS

A standalone Beat Saber plugin that carries beatmap `customData` colors through the **V3 GLS**
(`LightColorGroupEffect`) lighting pipeline and applies them as Chroma-style RGB colors — **without
modifying CustomJSONData or Heck/Chroma**.

It reproduces the exact behavior of two feature branches:

- `CustomJSONData` branch `GlsCustomDataAndChroma`
- `Heck` branch `GlsRgbChroma` (the `Chroma/GlsColorChromafier`)

## How it works

The GLS color data is normally dropped at two points, so ChromaGLS re-inserts it at each:

1. **Deserialization** — `GlsDeserializePatch` prefix-skips CustomJSONData's public
   `Version3CustomBeatmapSaveData.DeserializeLightColorEventBoxGroupArray` with a verbatim clone of
   that method plus a single addition: it reads each light-color base event's `customData` and
   constructs a `LightColorBaseDataSaveData` (which implements CJD's `ICustomData`) instead of a
   plain `LightColorBaseData`. Everything else in CJD's deserializer pipeline is untouched and still
   calls into this (patched) method.

2. **Conversion** — `GlsConverterPatches` postfixes `LightColorEventBoxConverter.Convert` and
   `LightColorBeatmapEventDataBox.Unpack` (both game types) to carry the `ICustomData` through into
   `CustomLightColorBeatmapEventData`, using a `ConditionalWeakTable` keyed by the event box.

3. **Application** — `GlsColorChromafier` postfixes `LightColorGroupEffect.HandleColorChangeBeatmapEvent`
   and rewrites the private `_fromColor` / `_toColor` (+ alternatives) fields, preserving the game's
   alpha/brightness and replacing only the RGB with the custom color.

Because the data flows via `ICustomData` (not time matching), there is no risk of float rounding
mismatches assigning colors to the wrong nodes.

## Dependencies

- `BSIPA` `^4.2.2`
- `CustomJSONData` `^2.6.3`

## Prerequisites

This project uses the **Aeroluna GitHub Packages** NuGet feed for the BeatSaberModdingTools build packages
(`BeatSaberModdingTools.Tasks.Luna` and `LunaBSMod.Tasks`). GitHub Package Registry requires authentication
even for public packages, so you must add the feed to your user-level `NuGet.config` before building.

1. Create a GitHub Personal Access Token with the `read:packages` scope:
   https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/creating-a-personal-access-token
2. Open a PowerShell window and run:
   ```powershell
   dotnet nuget add source "https://nuget.pkg.github.com/Aeroluna/index.json" `
       --name "Aeroluna Github Packages" `
       --username EklipZ `
       --password "ghp_..." `
       --store-password-in-clear-text
   ```
   Replace `ghp_...` with your token. If you prefer to keep the token in an environment variable, set
   `$env:NUGET_AUTH_TOKEN` first and pass `--password $env:NUGET_AUTH_TOKEN` instead.

## Building

BeatSaberChromaGLS follows the exact same build pattern as Heck and ChroMapper: game DLLs are referenced from
`$(BeatSaberDir)`, and the manifest + Plugins copy are produced by the BeatSaberModdingTools (Luna)
MSBuild tasks. Anyone set up to build Heck can build this.

```powershell
dotnet build BeatSaberChromaGLS\ChromaGLS.sln -c Debug-1.40.8 -p:BeatSaberDir="C:\Users\tdrak\BSManager\BSInstances\1.40.8"
```

On a successful build the BSMT `CopyToPlugins` task copies `ChromaGLS.dll` to
`$(BeatSaberDir)\Plugins`.
