# ChromaGLS

A standalone Beat Saber plugin that carries beatmap `customData` colors through the **V3 GLS**
(`LightColorGroupEffect`) lighting pipeline and applies them as Chroma-style RGB colors. It respects everything else GLS does, and simply swaps out the colors GLS is transitioning lights to.
You have full RGBA control, including per light id.

Demo here: https://youtu.be/3hOmEZGkvDM (I also talk through what is being demonstrated). The map files from the video are in the TestMaps folder in this repo.


## How to use it as a Mapper

ChromaGLS applies Chroma-style RGB colors to individual V3 Group Lighting System (`lightColorEventBoxGroups`) events.
For each light color event, add a `customData` object with a `color` array.

ChroMapper will support this natively in the dev build in the very near future, and will ship to stable build when GLS ships to stable build.

Add `strobeColor` inside the same event's `customData` to choose the RGB color used during the strobe-on phase. It accepts `[r, g, b]` or `[r, g, b, a]`; alpha is ignored. The event's `sb` value controls the strobe light level, and `sf` controls whether the strobe transitions with a fade. If `strobeColor` is omitted, the event's normal `color` is used while strobing.

```json
"lightColorEventBoxGroups": [
  {
    "b": 10.0,
    "g": 0,
    "e": [
      {
        "f": { "p": 0, "n": 4, "f": 0 },
        "w": 1.0,
        "d": 1,
        "r": 1.0,
        "t": 1,
        "b": 1,
        "i": 0,
        "e": [
          {
            "b": 0.0,
            "i": 0,
            "c": 1,
            "s": 1.0,
            "f": 0,
            "sb": 0.0,
            "sf": 0,
            "customData": {
              "color": [1.0, 0.7, 0.0],
              "strobeColor": [1.0, 1.0, 1.0]
            }
          },
          {
            "b": 0.5,
            "i": 0,
            "c": 1,
            "s": 1.0,
            "f": 0,
            "sb": 0.0,
            "sf": 0,
            "customData": {
              "color": [1.0, 0.2, 0.0, 0.9],
              "strobeColor": [1.0, 1.0, 1.0, 1.0]
            }
          }
        ]
      }
    ]
  }
]
```

### `color` format

- The `color` value is an array of floats: `[r, g, b]` or `[r, g, b, a]`. Values are in `0.0`–`1.0`.
- The alpha component is ignored; ChromaGLS preserves the game's brightness/alpha from the normal GLS pipeline.
- If `color` is absent, the event uses the default `colorType` (primary/secondary/wave/flash) as usual.
- `strobeColor` is an optional `[r, g, b]` or `[r, g, b, a]` value used while the event is in its strobe phase. Its alpha is ignored; the strobe brightness comes from the event's `sb` value.
- `strobeColor` is supported on Beat Saber 1.29.1 as well as newer supported versions.

### Tweening / transitions

- `fromColor` (the current event's color) is set from the current event's `customData.color`.
- If the next event in the same light group has a `color`, it is used for `toColor` (the end of the tween).

### Beat Saber 1.29.1 compatibility

Beat Saber 1.29.1's GLS implementation does not expose the newer strobe light level and strobe fade behavior. ChromaGLS backports that behavior for 1.29.1, so V3 GLS color events can use the saved `sb` strobe brightness and `sf` strobe fade values instead of always strobing to the legacy off color.

The 1.29.1 build also remaps easing IDs that the older game cannot convert. The supported values are preserved, while newer easing families fall back by direction:

- `In*` easings become `InQuad`.
- `Out*` easings become `OutQuad`.
- `InOut*` easings become `InOutQuad`.
- Unknown values become `Linear`.

This easing compatibility patch is compiled only for Beat Saber 1.29.1; newer supported versions use their native easing support.

## How it works

ChromaGLS keeps GLS `customData` attached from map deserialization through conversion into runtime light events. It does not replace GLS scheduling, light selection, easing, or brightness behavior.

When an event starts, the plugin substitutes only its RGB endpoints. `color` supplies the normal endpoint. `strobeColor`, when present, supplies a separate endpoint for the strobe-on phase. Native GLS still controls the strobe frequency, brightness, fade, and transition timing.

For transitions, the next event in the same GLS box provides the end color. Extension events inherit the previous event's custom color data, so they continue that event instead of taking color data from a later box.

On current game versions, the native renderer remains in use except while an explicit `strobeColor` must be selected for the active strobe phase. The 1.29.1 build also backports the newer GLS strobe brightness and fade behavior, including continuous strobe phase across extension events.


### Reading the data in your own mod (dunno why you'd need to but, here you go)

Because we shim into CustomJSONData's deserialize pipeline, you can access the data just like you'd access any other CustomJSONData data:
At runtime, cast `LightColorBeatmapEventData` to `CustomJSONData.CustomBeatmap.CustomLightColorBeatmapEventData`
(or `ICustomData`) and read the color array:

```csharp
if (lightColorEventData is CustomLightColorBeatmapEventData customData)
{
    List<object>? color = customData.customData.Get<List<object>>("color");
    if (color != null && color.Count >= 3)
    {
        float r = Convert.ToSingle(color[0]);
        float g = Convert.ToSingle(color[1]);
        float b = Convert.ToSingle(color[2]);
    }
}
```

or whatever color helper method you use with ICustomData in your own mod. Exact same way you'd get it from Basic Event light nodes. Because we build against CustomJSONData.dll, we extend the same interface, so you can use the exact same helpers etc to work with GLS color customdata as you would with Basic Event color customdata.


### Heck / Chroma

It co-exists peacefully with Heck / Chroma and friends without conflict. 
This was specifically engineered to be as unobtrusive to other mods as possible, so at runtime it could only interfere with any other mods which interfere with GLS Color (of which there are none), and it only shims over a small part of CustomJSONData's deserialize pipeline, specifically dealing with GLS nodes.


# CONTRIBUTING / BUILDING

## Dependencies (as in, you must have these mods installed in your beatsaber folder already - this is the minimum version required)

- `BSIPA` `^4.2.2`
- `CustomJSONData` `^2.5.2`
- `SongCore` `^3.11.1`


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
       --username {yourGithubUsername} `
       --password "{yourGithubPersonalAccessToken}" `
       --store-password-in-clear-text
   ```

## Building

BeatSaberChromaGLS follows the exact same build pattern as Heck and ChroMapper: game DLLs are referenced from
`$(BeatSaberDir)`, and the manifest + Plugins copy are produced by the BeatSaberModdingTools (Luna)
MSBuild tasks. Anyone set up to build Heck should be able to build this.

Build with:

```powershell
dotnet build BeatSaberChromaGLS\ChromaGLS.sln -c Debug-1.40.8 -p:BeatSaberDir="C:\Users\tdrak\BSManager\BSInstances\1.40.8"
```

On a successful build the BSMT `CopyToPlugins` task copies `ChromaGLS.dll` to
`$(BeatSaberDir)\Plugins`. If you have the game running, this will fail. And that's it, test in-game.


## Supported versions
- Beat Saber 1.29.1
- Beat Saber 1.34.2
- Beat Saber 1.37.1
- Beat Saber 1.40.8
- Beat Saber 1.42.1
- Beat Saber 1.44.1
- Beat Saber 1.44.2

There is a helper powershell script: 
* `.\build-all-versions.ps1` 
which will build all versions provided you set the following environment variables:

```powershell
[Environment]::SetEnvironmentVariable("BEATSABER_1_29_1", "C:\Users\{you}\BSManager\BSInstances\1.29.1", "User")
[Environment]::SetEnvironmentVariable("BEATSABER_1_34_2", "C:\Users\{you}\BSManager\BSInstances\1.34.2", "User")
[Environment]::SetEnvironmentVariable("BEATSABER_1_37_1", "C:\Users\{you}\BSManager\BSInstances\1.37.1", "User")
[Environment]::SetEnvironmentVariable("BEATSABER_1_40_8", "C:\Users\{you}\BSManager\BSInstances\1.40.8", "User")
[Environment]::SetEnvironmentVariable("BEATSABER_1_42_1", "C:\Users\{you}\BSManager\BSInstances\1.42.1", "User")
[Environment]::SetEnvironmentVariable("BEATSABER_1_44_1", "C:\Users\{you}\BSManager\BSInstances\1.44.1", "User")
[Environment]::SetEnvironmentVariable("BEATSABER_1_44_2", "C:\Users\{you}\BSManager\BSInstances\1.44.2", "User")
# don't forget to restart your shell for these to take effect, then
.\build-all-versions.ps1
```

## Future plans
At the moment my only goal was GLS RGB control.
Along the way I've grown a desire to be able create new GLS light groups into an environment the way you can create Basic Event lights with Chroma, so if I add support for anything it is likely to be that.
I also wish I could import lights (both Basic Event and GLS) cross-environment, so I might play with that as well.

### Contact
If you need something updated or find an urgent bug or whatever, or just want to chat about cool stuff we could do in BS, I can be reached on discord as EklipZ (find me in most of the relevant servers). 
I am in a billion servers for a billion different things so if you want my attention, DM me on discord. I'm liable to miss @'s in servers that I don't check up on frequently.
I am unlikely to check github issues often so if you make a PR and want me to look at it, DM me lol, or I'll have no idea it's there. I have 20,000 unread emails and I'm not about to start catching up now :)
