# Using ChromaGLS

[Project overview](README.md) | [Color and bloom guide](README-Colors.md) | [Build instructions](BUILD.md)

ChromaGLS adds custom RGBA colors, independent strobe colors, RGB/HSV transitions, and per-light color distributions to GLS lighting. You still choose lights with the normal GLS event-box filters.

- [Installation](#installation)
- [Settings and updates](#settings-and-updates)
- [Mapping and property reference](#mapping)
- [Color distributions](#color-and-brightness-distributions-colordistributions--strobecolordistributions)
- [Strobes](#strobes)
- [Transitions and easing](#transitions-and-easing)
- [Compatibility](#compatibility)
- [Integer easing IDs](#integer-easing-ids)
- [Troubleshooting](#troubleshooting)

## Installation

1. Download the ZIP for your Beat Saber version from [Releases](https://github.com/EklipZgit/BeatSaberChromaGLS/releases). Use a published game-specific package, not GitHub's source-code archive.
2. Install the dependencies below, using versions compatible with your game.
3. With Beat Saber closed, extract the package into the game installation root. Its `Plugins` folder should merge with the game's `Plugins` folder.
4. Start the game and open the **ChromaGLS** tab in the gameplay setup's mod settings.

| Dependency | Minimum declared version |
|---|---|
| BSIPA | `4.2.2` |
| BeatToTheFuture | `1.0.0` |
| CustomJSONData | `2.5.2` |
| SongCore | `3.11.1` |
| BeatSaberMarkupLanguage (BSML) | `1.5.9` |
| SiraUtil | `3.0.5` |

Build targets with working local dependencies are 1.29.1, 1.34.2, 1.37.1, 1.40.8, 1.42.1, and 1.44.1. Check the release's actual assets for available downloads. The 1.44.2 target is not ready for normal use because compatible dependencies are incomplete.

ChromaGLS works alongside Chroma and Heck; it does not require replacing their DLLs. BeatToTheFuture supplies V4-to-V3 compatibility for newer maps.

## Settings and Updates

The ChromaGLS tab has four public toggles:

| Setting | Default | Effect |
|---|---|---|
| Enable ChromaGLS | On | Enable or disable custom GLS lighting immediately, without restarting the game. |
| Enable Ferrofluid Info Button | On | Show the reactive ferrofluid map indicator around SongCore's info button for difficulties requiring or suggesting ChromaGLS. |
| Enable Ferrofluid Cube | Off | Show a ferrofluid cube fidget toy on the main-menu floor. |
| Enable Ferrofluid Sphere | Off | Show a ferrofluid sphere fidget toy on the main-menu floor. |

These preferences persist across launches. The ferrofluid visuals are optional and have separate toggles from gameplay lighting.

At startup, ChromaGLS checks GitHub's latest published stable release once. When an update passes its game-version compatibility check, the main menu shows **ChromaGLS has an update available!**, the installed and available plugin versions, and a **Find the latest here** link. Clicking the link opens the release page; it does not install anything.

There is no alert if your game is older than every version targeted by the release's ZIP assets. This check is not a promise that a ZIP exists for your exact game version: choose a matching asset on the release page. Offline or failed checks do not interrupt play and are retried on the next launch.

## Mapping

Put custom color properties in each GLS color event's `customData` inside the V3 `lightColorEventBoxGroups` array. Color-distribution arrays may also go on the enclosing color event box. These keys are not group-level properties and do not recolor rotation, translation, or FloatFX nodes.

Use a ChroMapper build that includes the extensions you want to author and preview. Not every published stable build includes the GLS color-distribution, easing, or pulse-width controls. You can also edit JSON directly. See the [demo](https://youtu.be/3hOmEZGkvDM) and [example maps](TestMaps).

### Declare the Map's Requirement

Add **ChromaGLS** to the difficulty's requirements when the map needs it, or suggestions when the custom lighting is optional. For an `Info.dat` using underscore-prefixed difficulty metadata, merge this into the selected difficulty entry:

```json
{
  "_customData": {
    "_requirements": ["ChromaGLS"]
  }
}
```

Use `_suggestions` instead for an optional visual enhancement. Preserve any existing requirements, suggestions, and other custom data. In other Info formats, use your mapper's requirement editor or the matching format's metadata keys.

### Property Reference

All properties below belong to color-event `customData` unless noted otherwise.

| Property | Value | Purpose / default |
|---|---|---|
| `color` | `[r,g,b]` or `[r,g,b,a]` | Normal RGBA endpoint. Omitting the property uses the environment color; omitted alpha is `1`. |
| `strobeColor` | `[r,g,b]` or `[r,g,b,a]` | Strobe RGBA endpoint. Falls back to normal color; omitted alpha is `1`. |
| `colorDistributions` | Array of instruction strings | Spatial normal-color/alpha offsets, on the box, event, or both. |
| `strobeColorDistributions` | Array of instruction strings | Independent spatial strobe-color/alpha offsets, on the box, event, or both. |
| `easing` | Integer `1`-`30`, `100`-`102` | Incoming transition easing. Omitted/invalid values leave native timing in place. |
| `colorEasing` | Integer `0`-`30`, `100`-`102` | Incoming normal RGB curve. Defaults to the interval easing. |
| `strobeColorEasing` | Integer `0`-`30`, `100`-`102` | Incoming strobe RGB curve. Defaults to the interval easing. |
| `easingType` | `"HSV"` or `"RGB"` | Incoming color interpolation model. Default is RGB. |
| `strobeInterval` | Positive number | Beats per strobe cycle, overriding native `f`. |
| `strobePulseWidth` | Number clamped to `[0,1]` | Normal-phase fraction for hard strobes, or strobe peak position for fades. Default `0.5`. |
| `strobeEasing` | Integer `0`-`30`, `100`-`102` | Active node's pulse fade curve. Default InOutCubic (`9`). Requires `sf: 1` to affect output. |

For the practical difference between **RGB**, **alpha**, and **GLS brightness**, read the [color and bloom guide](README-Colors.md). It includes screenshots and recipes for saturated surfaces, white surfaces, and controlled fog bloom.

### Basic Color Example

This V3 difficulty fragment uses an all-lights Division filter. It sets two colors half a beat apart; `f: 0` means this example does not strobe.

```json
{
  "lightColorEventBoxGroups": [
    {
      "b": 10.0,
      "g": 0,
      "e": [
        {
          "f": { "f": 1, "p": 1, "t": 0, "r": 0, "c": 0, "n": 0, "s": 0, "l": 0, "d": 0 },
          "w": 0,
          "d": 1,
          "r": 0,
          "t": 1,
          "b": 0,
          "i": 0,
          "e": [
            {
              "b": 0.0,
              "i": 0,
              "c": 1,
              "s": 1.0,
              "f": 0,
              "customData": {
                "color": [1.0, 0.7, 0.0]
              }
            },
            {
              "b": 0.5,
              "i": 0,
              "c": 1,
              "s": 1.0,
              "f": 0,
              "customData": {
                "color": [1.0, 0.2, 0.0, 0.9]
              }
            }
          ]
        }
      ]
    }
  ]
}
```

### `color` format

- The `color` value is an array of floats: `[r, g, b]` or `[r, g, b, a]`. Components are not clamped; values above `1.0` retain Chroma's HDR color and intensity behavior.
- `color` alpha multiplies the normal GLS brightness without scaling its RGB components. This preserves independent control over emissive color, fog bloom, and bloom whitening.
- If `color` is absent, the event uses its native environment color as usual.
- `strobeColor` is an optional `[r, g, b]` or `[r, g, b, a]` value used while the event is in its strobe phase. Its alpha multiplies the event's `sb` value; when omitted, the normal `color` RGBA is used.
- `strobeColor` is supported on Beat Saber 1.29.1 as well as newer supported versions.

### Color and brightness distributions: `colorDistributions` / `strobeColorDistributions`

Both **ChromaGLS and ChroMapper's GLS color-distribution implementation** support these ordered arrays of strings:

- `customData.colorDistributions` changes the normal color/alpha endpoint.
- `customData.strobeColorDistributions` changes the strobe color/alpha endpoint independently.
- Put either array on a **color event box** to affect its events, on an **individual color event**, or both. These are color-box/color-event extensions, not group-level custom-data keys or rotation/translation/FloatFX instructions.
- Instructions run in array order: **box instructions first, then event instructions**. Event instructions add to the result; they do not replace the box list. Order matters when mixing color models or clamping saturation.

Each string has the form `targets,offset,easing` or `targets,offset,easing,mode`.

| Field | Meaning |
|---|---|
| `targets` | One or more channel letters from the table below. |
| `offset` | Finite signed **total offset across the selected distribution**, not an increment added once per chunk. |
| `easing` | Spatial easing applied to the selected chunk/light progress. See the abbreviation table below. |
| Optional fourth field `mode` | `l` selects **per affected light** mode. `c`, or omitting the field, selects **per affected chunk** mode. Parsing is case-insensitive. |

The offset applied to each selected light is `offset * easing(progress)`. This is a spatial distribution: it does not change beat distribution, strobe timing, or the event's temporal transition easing.

#### Targets and color semantics

| Target | Meaning | Behavior |
|---|---|---|
| `h` | Hue | Signed turns around the color wheel; `1` is a full turn. Hue wraps cyclically. |
| `s` | HSV saturation | Add the eased offset, then clamp to `[0,1]`. |
| `v` | HSV value / RGB intensity | Add the eased offset; HDR values above `1` remain supported. This is distinct from alpha. |
| `r`, `g`, `b` | Individual RGB channels | Add the eased offset without clamping to `[0,1]`. |
| `f` | Brightness / alpha | Add the eased offset to alpha without scaling RGB; the result subsequently multiplies the phase's GLS light level. |

Combine channels within a model, such as `hs`, `sv`, `rgb`, or `hsvf`. **The first recognized HSV or RGB channel chooses the color model for that instruction.** Channels from the other model are ignored: `rsf` applies red and alpha, while `srf` applies saturation and alpha. Use separate instructions if you want both RGB and HSV operations. `f` is independent of either model.

Hue offsets retain their authored direction and distance. For example, `h,-0.7,L` travels backwards by 70% of a turn; it is **not** shortened into a positive 30% turn by a shortest-path angular interpolation. The eased signed offset is added before wrapping the resulting hue.

#### Default: distribute by affected chunk

Lights in the same selected chunk receive the same color-distribution offset. Skipped chunks do not consume progress. With `N` affected chunks, their progress is `chunkIndex / max(N - 1, 1)`.

For **8 lights, Chunks = 4, Step = 2, Offset = 0**, the box selects physical lights **0, 1, 4, 5**: two selected chunks of two lights. These equivalent V3 filter values are `{"f":2,"p":0,"t":2,"c":4}`.

| Physical light ID | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Selected | Yes | Yes | No | No | Yes | Yes | No | No |
| Chunk progress | 0 | 0 | — | — | 1 | 1 | — | — |
| `h,-0.7,L` hue offset | 0 | 0 | — | — | -0.7 | -0.7 | — | — |

The selected lights therefore receive offsets **`0, 0, -0.7, -0.7`**.

#### Optional `,l`: distribute by affected light

Appending `,l` spreads the color-distribution offset across individual **selected lights**, including different lights within one chunk. With `M` affected lights, progress is `lightIndex / max(M - 1, 1)`.

Using the same 8-light filter:

| Physical light ID | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Per-light progress | 0 | 1/3 | — | — | 2/3 | 1 | — | — |
| `h,-0.7,L,l` hue offset | 0 | -0.233333 | — | — | -0.466667 | -0.7 | — | — |

The selected lights therefore receive offsets **`0, -0.233333, -0.466667, -0.7`**.

In both modes:

- Selection and ordering come from the GLS filter, including reverse, random/seed, limits, and partial chunks; progress is not derived directly from the physical light ID.
- With one selected chunk/light, that mode's progress is `0` rather than dividing by zero. With no selected lights, the box has nothing to affect.
- Excluded lights are **not modified by this box**; they may still be controlled by other events. ChroMapper shows excluded IDs as black in its distribution bands, which is a selection preview, not an instruction to turn those in-game lights off.
- Chunks = 0 uses individual-light chunks, so default chunk and `,l` progress coincide for the same selection.

#### Normal versus strobe brightness

Normal distributions start from the event's authored `color`, or its environment color when absent. Strobe distributions start from `strobeColor`, falling back to the **undistributed** authored main color (or environment color), then apply only `strobeColorDistributions`. Normal distributions do not leak into the strobe fallback.

After the corresponding distributions, normal alpha multiplies the effective normal GLS brightness (including applicable brightness distribution), and strobe alpha multiplies `sb`. Neither phase's light level is applied to the other phase.

For example, with authored alpha `1`, normal brightness `0.5`, and `f,0.1,L`, the final selected chunk has alpha `(1 + 0.1) * 0.5 = 0.55`. It is **not** `0.5 + 0.1`. With `sb = 0`, an alpha offset still multiplies zero. Omitted authored alpha defaults to `1`.

`v` instead changes RGB intensity. A value offset can produce RGB above `1` while `f` independently changes alpha, preserving Chroma's distinct surface/fog/bloom behavior. See [the color guide](README-Colors.md) for examples of those renderer differences.

**Do not confuse target letters with JSON field names:** `f` *inside a color-distribution string* means alpha, whereas a color event's native JSON `f` is strobe frequency. Likewise, target `s` is saturation, while the event's native `s` is normal brightness.

#### Spatial easing abbreviations

| Family | In | Out | In/out |
|---|---|---|---|
| Quadratic | `I^2` | `O^2` | `IO^2` |
| Cubic | `I^3` | `O^3` | `IO^3` |
| Quartic | `I^4` | `O^4` | `IO^4` |
| Quintic | `I^5` | `O^5` | `IO^5` |
| Sine | `ISn` | `OSn` | `IOSn` |
| Exponential | `IEx` | `OEx` | `IOEx` |
| Circular | `ICr` | `OCr` | `IOCr` |
| Back | `IBk` | `OBk` | `IOTBk` |
| Elastic | `IEl` | `OEl` | `IOTEl` |
| Bounce | `IBo` | `OBo` | `IOTBo` |

Also supported: `L` (linear), `N` (step), and Beat Saber InOut variants `IOBk`, `IOEl`, and `IOBo`. Abbreviations are case-insensitive. Full names such as `easeLinear`, `easeInQuad`, `easeInOutQuint`, and `easeBeatSaberInOutBack` are accepted.

#### Example: compose box and event distributions

This is one V3 color event box inside a group's `e` array. The filter selects lights 0, 1, 4, 5 in an eight-light group. The box applies chunk-based hue and alpha distributions; its event adds per-light saturation/value distributions. Strobe hue/saturation and HDR value are distributed independently by light.

```json
{
  "f": { "f": 2, "p": 0, "t": 2, "r": 0, "c": 4, "n": 0, "s": 0, "l": 0, "d": 0 },
  "w": 0,
  "d": 1,
  "r": 0,
  "t": 1,
  "b": 0,
  "i": 0,
  "customData": {
    "colorDistributions": ["h,-0.2,IO^5", "f,0.1,I^2"]
  },
  "e": [
    {
      "b": 0,
      "i": 0,
      "c": 0,
      "s": 0.5,
      "f": 1,
      "sb": 1,
      "sf": 1,
      "customData": {
        "color": [0.179, 1, 0],
        "strobeColor": [0.969, 0, 0.941],
        "colorDistributions": ["sv,0.3,L,l"],
        "strobeColorDistributions": ["hs,0.4,L,l", "v,2,L,l"]
      }
    }
  ]
}
```

#### Forward-compatible parsing

- Unknown target characters are ignored while recognized channels in the same token still work. A token with no recognized targets produces no instruction.
- `l` selects per-light mode; `c` and an omitted fourth field select chunk mode. Mode tokens ignore surrounding whitespace and case. Empty or unknown modes retain chunk behavior.
- All fields after the fourth are ignored. A later `l` does not select per-light mode.
- Missing/invalid offsets, `NaN`/infinite offsets, or missing/unknown easing names make that instruction inert. Other valid instructions in the list still run.
- Non-string array entries are ignored. Supply a JSON array of strings, not one comma-delimited string for the whole list.

Examples:

| Instruction | Result |
|---|---|
| `xh?,-0.7,L,l,future` | Hue offset, per-light; unknown target characters and the fifth field are ignored. |
| `h,-0.7,L,c` | Hue offset, explicitly per-chunk. |
| `h,-0.7,L,future,l` | Hue offset, per-chunk; the fourth token is unknown and the fifth is ignored. |
| `h,-0.7,L,,l` | Hue offset, per-chunk; an empty fourth field does not enable `l`. |
| `h,-0.7,futureEase,l` | Ignored because the required easing is unknown. |

## Strobes

A strobe alternates between the event's **normal** and **strobe** endpoints. Native V3 `s` sets normal brightness, `sb` sets strobe brightness, `sf: 0` switches sharply, and `sf: 1` fades between the endpoints. Custom color alpha multiplies the corresponding brightness.

`strobeColor` can be RGB or RGBA, just like `color`. Omitting it uses the normal color, including alpha. When using independent strobe distributions, their fallback starts from the undistributed normal color so the normal distribution does not get applied twice.

### `strobeInterval`

Add `strobeInterval` inside the event's `customData` to override the native `f` strobe frequency with a continuous beats-per-strobe-cycle value.

- `strobeInterval` is a positive float measured in beats per strobe cycle (e.g. `0.5` = two strobe cycles per beat, `1.0` = one cycle per beat, `2.0` = one cycle every two beats).
- When present, the runtime strobe frequency is computed from `1 / (strobeInterval * oneBeatDuration)` and replaces the integer `f` frequency.
- It supports the same transitions as native strobe: if the next transition event affecting that light has a `strobeInterval`, the strobe frequency tweens between them. If an endpoint has no `strobeInterval`, its native `f` value is used.
- The beats-to-seconds conversion uses the BPM when the event starts. Frequency, rather than the authored interval value, is interpolated during a transition.
- A positive `strobeInterval` can enable strobing even with native `f: 0`. Zero or a negative interval does not override native frequency.
- Use it with `strobeColor`, `sb`, and `sf` to control both the color and timing of a custom strobe.

### `strobePulseWidth`

Set `customData.strobePulseWidth` on a GLS color event. It is a **fraction of one cycle**, not a duration in beats or seconds. Numeric values are clamped to `[0,1]`; the default is `0.5`. ChroMapper's pulse-width control removes the property when set to that default.

- With `sf: 0`, the normal color/brightness lasts for this fraction of the cycle, followed by the strobe color/brightness. `0.9` means 90% normal and 10% strobe. `0` stays on the strobe endpoint; `1` stays on the normal endpoint.
- With `sf: 1`, the blend reaches the strobe endpoint at this fraction, then returns to normal. `strobeEasing` controls the rising curve; the falling curve is its vertical reflection. At `0`, only the falling curve remains; at `1`, only the rising curve remains, with an instantaneous reset each cycle.
- During a transition, width interpolates linearly from the active node's width to the next node's width on that light. It follows elapsed transition time, **not** `easing`, `colorEasing`, or `strobeEasing`. An omitted endpoint uses `0.5`.
- Before an Instant node, the active width holds until that node starts. An Extend node inherits the previous width for that light.
- It does not change frequency or enable strobing by itself. It works with native `f` or custom `strobeInterval` and can be the event's only custom-data property.

| Width | Hard strobe (`sf: 0`) | Faded strobe (`sf: 1`) |
|---:|---|---|
| `0.2` | 20% normal, 80% strobe | Reach the strobe endpoint at 20%, then return over 80%. |
| `0.5` | Equal normal/strobe phases | Reach the strobe endpoint halfway through. |
| `0.9` | 90% normal, 10% strobe | Reach the strobe endpoint at 90%, then return over 10%. |

The width describes the **normal** portion of a hard strobe. If your strobe endpoint is dark, `0.9` produces a brief dark gap, not a brief bright flash.

```json
{
  "customData": { "strobePulseWidth": 0.9 }
}
```

### `strobeEasing`

`strobeEasing` selects the fade **within each strobe cycle**, using an [integer easing ID](#integer-easing-ids). It only affects `sf: 1`; it does not affect hard switching, cycle frequency, or the transition between event colors.

- Missing or invalid values use InOutCubic (`9`).
- An explicit `0` means Linear. It is not the same as omitting the property.
- The active node supplies the curve, including on Instant nodes. Extend nodes inherit it.
- The rising half applies the selected curve from normal to strobe. The return half uses `1 - easing(returnProgress)`, a vertical reflection of that same curve.
- `strobePulseWidth` determines where the rise ends and the return begins. It does not change which easing curve is used.

For example, `strobeEasing: 7` uses InCubic to approach the strobe endpoint, then its vertically reflected return. Use `strobeEasing: 0` for a straight rise and return even while colors transition with another easing.

### Example: Asymmetric Faded Strobe

This is a color event inside a V3 color event box. It fades from green at normal brightness to dim magenta and back once per beat. The magenta endpoint is reached at 20% of each cycle, followed by an 80% return.

```json
{
  "b": 0,
  "i": 0,
  "c": 0,
  "s": 1,
  "f": 0,
  "sb": 0.2,
  "sf": 1,
  "customData": {
    "color": [0, 1, 0.2, 1],
    "strobeColor": [1, 0, 1, 1],
    "strobeInterval": 1,
    "strobePulseWidth": 0.2,
    "strobeEasing": 0
  }
}
```

Here `strobeInterval` supplies the frequency despite `f: 0`. Change `sf` to `0` for a hard strobe with the same 20% normal / 80% strobe split.

## Transitions and Easing

GLS transitions are **per light**. The next event selected for a light supplies its destination, even when it comes from another box in the group. Place incoming transition properties on that destination node, not on the source.

V3 color-event `i` is the transition type: `0` Instant, `1` Interpolate, `2` Extend. It is **not an easing ID**.

### Independent Easing Tracks

| Property on the destination node | Controls |
|---|---|
| `easing` | Overall incoming interval progress, including brightness/alpha. |
| `colorEasing` | Normal RGB interpolation only. |
| `strobeColorEasing` | Strobe RGB interpolation only. |
| `easingType` | RGB or shortest-path HSV interpolation for the color tracks. |

`colorEasing` and `strobeColorEasing` fall back to the interval easing when omitted. Explicit `0` forces a linear RGB transition even if the interval is eased. Their curves do not retime the strobe clock, and alpha still follows the interval easing.

`strobeEasing` is different: it belongs to the **active** node and controls its pulse fades. Pulse width interpolates in uneased time, independently of all three color/interval curves.

For example, this destination node starts four beats after the box:

```json
{
  "b": 4,
  "i": 1,
  "c": 0,
  "s": 0.8,
  "f": 2,
  "sb": 0.2,
  "sf": 1,
  "customData": {
    "color": [0, 0.2, 1, 1],
    "strobeColor": [1, 1, 0, 1],
    "easing": 6,
    "colorEasing": 0,
    "strobeColorEasing": 5,
    "easingType": "HSV",
    "strobePulseWidth": 0.8,
    "strobeEasing": 7
  }
}
```

The incoming interval uses InOutSine (`6`), normal color uses Linear (`0`), and strobe color uses OutSine (`5`). The width moves linearly toward `0.8`. The new `strobeEasing: 7` takes effect when this node becomes active, not during its predecessor's pulses.

### RGB and HSV

`easingType: "HSV"` interpolates hue around the shortest path on the color wheel, with saturation and value interpolated separately. Omit it, or use `"RGB"`, for direct RGB interpolation. The value is case-sensitive: use uppercase `"HSV"`.

During that interval, HSV applies to both endpoint color tracks and their faded strobe blend. Alpha remains separate. Temporal HSV transitions take the shortest hue path; spatial `h` distributions instead preserve the full signed offset you authored.

### Instant and Extend Nodes

- Instant nodes do not request an incoming color interpolation. Their node state, such as `strobeEasing` and pulse width, still applies when they become active.
- Extend nodes inherit the previous event's colors, distributions, strobe interval, pulse width, and pulse easing **for each affected light**, including across boxes.
- Incoming `easing`, `colorEasing`, `strobeColorEasing`, and `easingType` do not inherit from the predecessor. Author those on the Extend node when you want to ease into its inherited endpoint.
- When a strobe transitions to a non-strobing event, both phases converge on the destination's normal RGBA instead of leaving a separate strobe color behind.

## Compatibility

### Forward-Port Compatibility

ChromaGLS depends on BeatToTheFuture for V4-to-V3 compatibility and cross-version Chroma ring propagation correction. With **Correct Chroma Ring Prop** enabled, BeatToTheFuture compares the map's original BeatSaver publication date with the Beat Saber 1.42.1 cutoff:

- Pre-1.42.1 games multiply newer maps' `prop`/`_prop` by `1.6666667`.
- 1.42.1+ games multiply older maps' `prop`/`_prop` by `0.6`.
- Matching eras, disabled correction, and unknown publication dates are unchanged; on pre-1.42.1 games, WIP maps are assumed to use current ChroMapper behavior and receive `1.6666667`.

Maps authored with the old propagation behavior can override a newer publication date in the selected difficulty (level or beatmap-file custom data is also accepted):

```json
{
  "_customData": {
    "_mappedForOldRingPropogationSpeed": true
  }
}
```

Use `mappedForOldRingPropogationSpeed` without the leading underscore for the V3 convention. The spelling `Propogation` is part of the property name.

ChromaGLS remains responsible for GLS colors, GLS compatibility, and Chroma ring `customData`, including The Second's precise ring-position controls.

### Beat Saber 1.29.1 Compatibility

Beat Saber 1.29.1's GLS implementation does not expose the newer strobe light level and strobe fade behavior. ChromaGLS backports that behavior for 1.29.1, so V3 GLS color events can use the saved `sb` strobe brightness and `sf` strobe fade values instead of always strobing to the legacy off color.

### The Second's Ring Positions

For The Second's ring-position Basic Events, `customData.step` accepts a fractional position scalar instead of the native integer `i` distance. It scales the environment's normal ring step size and bypasses the integer clamp:

```json
{
  "customData": {
    "step": 0.5
  }
}
```

Merge this into the appropriate ring-position event, not a GLS color event. Without `step`, native integer positioning is unchanged. The Second keeps its next-event tween timing; this is not a ring-speed control.

## Integer Easing IDs

These GLS properties accept integer easing IDs:

- Color-event `customData.easing`: `1`–`30`, `100`–`102` for the incoming transition.
- Color-event `customData.colorEasing` and `customData.strobeColorEasing`: `0`–`30`, `100`–`102` for normal RGB and strobe RGB transitions, respectively.
- Color-event `customData.strobeEasing`: `0`–`30`, `100`–`102` for the strobe fade curve.
- Native V3 event-box `i`: brightness, rotation, translation, or FloatFX distribution easing. Native V3 rotation/translation event `e` and FloatFX event `i`: transition easing. The V3 color-event `i` is a transition type, not an easing ID.
- Native V4 event-box `e` and color/rotation/translation/FloatFX event `e`: distribution and transition easing, respectively.

| Integer | Easing |
|---:|---|
| `-1` | None (native fields only) |
| `0` | Linear |
| `1` | InQuad |
| `2` | OutQuad |
| `3` | InOutQuad |
| `4` | InSine |
| `5` | OutSine |
| `6` | InOutSine |
| `7` | InCubic |
| `8` | OutCubic |
| `9` | InOutCubic |
| `10` | InQuart |
| `11` | OutQuart |
| `12` | InOutQuart |
| `13` | InQuint |
| `14` | OutQuint |
| `15` | InOutQuint |
| `16` | InExpo |
| `17` | OutExpo |
| `18` | InOutExpo |
| `19` | InCirc |
| `20` | OutCirc |
| `21` | InOutCirc |
| `22` | InBack |
| `23` | OutBack |
| `24` | InOutBack |
| `25` | InElastic |
| `26` | OutElastic |
| `27` | InOutElastic |
| `28` | InBounce |
| `29` | OutBounce |
| `30` | InOutBounce |
| `100` | BeatSaberInOutBack |
| `101` | BeatSaberInOutElastic |
| `102` | BeatSaberInOutBounce |

See [easings.net](https://easings.net/) for visualizations of the standard easing curves.

## Troubleshooting

- **No custom colors:** check that Enable ChromaGLS is on, the installed package matches your game version, and its dependencies loaded. Confirm that the data is on color events or their color boxes, not the group itself.
- **A pulse setting does nothing:** enable strobing with native `f` or a positive `strobeInterval`. `strobePulseWidth` does not start a strobe, and `strobeEasing` only affects `sf: 1`.
- **A fade changes the wrong interval:** put incoming `easing`, `colorEasing`, `strobeColorEasing`, and `easingType` on the destination event. Put pulse `strobeEasing` on the active event.
- **Preview differs from the game:** check that your ChroMapper build supports the authored extension. Excluded lights shown as black in distribution bands indicate selection, not an instruction to switch them off.
- **Lights wash out to white or bloom too much:** separate RGB from alpha/brightness using the [color and bloom guide](README-Colors.md) and its [visual recipes](README-Colors.md#color-goal-cheat-sheet).

For loading errors, inspect your installation's live `Logs/_latest.log` or `Logs_latest.log`. Building, test runners, and developer diagnostics are documented in [BUILD.md](BUILD.md).
