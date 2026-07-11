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

## Building

BeatSaberChromaGLS follows the exact same build pattern as Heck and ChroMapper: game DLLs are referenced from
`$(BeatSaberDir)`, and the manifest + Plugins copy are produced by the BeatSaberModdingTools (Luna)
MSBuild tasks. Anyone set up to build Heck can build this.

```powershell
dotnet build BeatSaberChromaGLS\ChromaGLS.sln -c Debug-1.40.8 -p:BeatSaberDir="C:\Users\tdrak\BSManager\BSInstances\1.40.8"
```

On a successful build the BSMT `CopyToPlugins` task copies `ChromaGLS.dll` to
`$(BeatSaberDir)\Plugins`.

> **Note:** The Aeroluna GitHub Packages feed hosts the `BeatSaberModdingTools.Tasks.Luna` and
> `LunaBSMod.Tasks` build packages (auth required even for public packages). The repo `NuGet.config`
> uses the `AEROLUNA_PAT` environment variable for the feed password. Set it before building, or add
> the credentials to your user-level `NuGet.config` instead:
>
> ```powershell
> $env:AEROLUNA_PAT = "ghp_..."
> # or permanently for the machine
> [Environment]::SetEnvironmentVariable("AEROLUNA_PAT", "ghp_...", "User")
> ```
