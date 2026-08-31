# GLS Color Chromafier Patch Architecture

## Purpose

This document separates production behavior from temporary diagnostics in `GlsColorChromafier` and records why some work occurs in `HandleColorChangeBeatmapEvent` while phase-dependent work remains in `SetColor`.

## Harmony guidance applied

Harmony prefixes and postfixes execute as part of every invocation of the patched original. Transpilers execute while constructing the patched method rather than once per original invocation. Reverse patches copy an otherwise inaccessible original method into a callable stub.

Harmony private-field injection uses a patch argument whose name starts with three underscores. Because the game fields themselves start with an underscore, `_fromColor` maps to `____fromColor`. A field only needs `ref` when the patch mutates it. Normal argument injection is preferred over `object[] __args`, which Harmony documents as having additional overhead.

References:

- <https://harmony.pardeike.net/articles/patching.html>
- <https://harmony.pardeike.net/articles/patching-injections.html>
- <https://harmony.pardeike.net/articles/reverse-patching.html>

## Production paths

### Event-time endpoint preparation

`Prefix`, `Postfix`, and `ApplyCustomColors` patch `HandleColorChangeBeatmapEvent`.

This handler runs when a GLS node activates and supplies `currentEventData`, including access to Chroma custom JSON and the next-event chains. Native `HandleColorChangeBeatmapEvent` invokes `SetData` internally. The prefix stages data before that native call, and the postfix composes unclamped custom RGB with custom alpha multiplied by the native brightness alpha.

The injected `_fromColor`, `_toColor`, `_alternativeFromColor`, and `_alternativeToColor` fields avoid reflection and boxing. This path does not run once per rendered frame.

### Strobe interval

`ApplyStrobeInterval` runs during the `HandleColorChangeBeatmapEvent` postfix. It resolves `customData.strobeInterval` (beats per strobe cycle) for the current and, when a valid transition exists, the next same-type event. The values are converted to strobe frequency in Hz using `IBpmController.oneBeatDuration` and written to `_fromStrobeFrequency` and `_toStrobeFrequency` so the native per-frame `SetColor` path uses them.

If the next event has no `strobeInterval`, the `to` endpoint is left as the native `strobeBeatFrequency`, producing the same mixed-OEM-strobe transition the game already uses. If the next event has no transition, the `to` endpoint equals the `from` endpoint, matching native `SetData` behavior.

`ResumeStrobeTweenIfNeeded` reactivates `FloatTween` when a custom `strobeInterval` is present but the native handler did not resume the tween because `strobeBeatFrequency` was `0`.

### Modern per-frame custom strobe selection

`SetColorPrefix` patches `SetColor` for versions after 1.29.1.

It returns immediately and allows native `SetColor` when there is no strobe frequency or custom strobe rendering requirement. It replaces native execution when an explicit `strobeColor` needs separate RGB or when `color`/`strobeColor` alpha must multiply `sb` during the strobe phase.

`_fromColor`/`_toColor` and `_alternativeFromColor`/`_alternativeToColor` remain the native regular/boost pairs. A per-effect `Dictionary<LightColorGroupEffect, StrobeColorState>` stores the additional raw strobe RGBA endpoints and is cleared by a `Cleanup` postfix. The normal RGBA path always reads the active native pair, so `UseBoostColors` remains authoritative.

`SetData` can stage fixed endpoints, brightness, frequency, and fade values, but cannot express an additional independently interpolated RGBA track selected by strobe phase. Each strobe endpoint composes `custom alpha * sb` before straight `Color.LerpUnclamped` interpolation, preserving values above `1.0` and keeping RGB independent from alpha. Removing this prefix therefore requires either changing the runtime data representation or using a narrowly scoped transpiler inside native `SetColor`.

The production calculations and `LightWithIdManager.SetColorForId` call use strongly typed Harmony field injection. They do not use reflection or allocate an invocation argument array. The only recurring state lookup is the direct dictionary lookup needed to obtain the extra strobe track.

### 1.29.1 per-frame backport

`LegacySetColorPrefix` replaces `SetColor` for Beat Saber 1.29.1 strobes.

The original 1.29.1 implementation strobes to its static off color and lacks the later strobe brightness/fade behavior. Event-time endpoint staging alone cannot reproduce the newer phase, brightness, fade, and explicit `strobeColor` behavior. This path therefore remains a full per-frame compatibility implementation.

The `LightColorGroupEffect` fields and final `SetColorForId` call are strongly typed and do not use reflection. Explicitly cleaned dictionaries carry the 1.29.1 strobe brightness/fade and cached fog compatibility state absent from the legacy instance.

### Production reflection boundary

There is no reflection in either production `SetColor` hot path.

#### `PrepareLegacyFogState` (1.29.1 only)

`PrepareLegacyFogState` runs inside `ApplyCustomColors` only during the `HandleColorChangeBeatmapEvent` postfix (`forceNoTweenColor == true`). It executes at most once per `LightColorGroupEffect` instance, because it immediately returns if the instance already has an entry in `LegacyFogStates`. The `Cleanup` postfix removes that entry when the effect is disposed, so the discovery can repeat if the same group object is recycled.

Its purpose is to find the single `TubeBloomPrePassLightWithId` renderer for the light ID and cache a compiled `FieldRef<object, float>` to that renderer's `_tubeBloomPrePassLight._bloomFogIntensityMultiplier` field. The lookups for `LightWithIdManager._lights`, `TubeBloomPrePassLightWithId._tubeBloomPrePassLight`, and the multiplier are also cached in static dictionaries keyed by `Type`, so repeated events for the same renderer type do not re-run `AccessTools.Field`. There is no per-frame reflection or `FieldInfo.GetValue`/`SetValue` in this path.

Per-frame `SetLegacyFogCompensation` (called by `LegacySetColorPrefix`) performs only a `Dictionary` lookup and a direct `FieldRef` assignment to scale the cached base multiplier.

The event-time non-tween `SetColor(0)` call uses `InvokeOriginalSetColor`, a typed Harmony reverse patch, rather than `MethodInfo.Invoke`.

## Diagnostics-only paths

The following code is observational and may be removed after behavior is fully reconfirmed. It must not be treated as required rendering behavior:

- `SetColorPostfix` in non-1.29.1 builds.
- `GetReferenceRendererState`.
- `GetDetailedRendererState`.
- `GetInheritedFieldValue`.
- `GetLegacyRendererState`.
- `LogMaterialStrobeState`.
- All blocks guarded by diagnostic counters such as `EventColorDiagnosticCount`, `StrobeFadeDiagnosticCount`, `DifferentColorStrobeDiagnosticCount`, `GroupEightRendererDiagnosticCount`, `LegacyOutputDiagnosticCount`, and related counters.
- `LegacyStrobeState.RecordProgress`, `ShouldLogSparseOutput`, and diagnostic-only group/element/timing properties. Its brightness and fade fields remain production state.

Renderer reflection inside these blocks is deliberately retained for diagnosis. Although bounded, diagnostics located inside `SetColorPrefix` or `LegacySetColorPrefix` still execute their guards on the per-frame path and may perform reflection until their counters are exhausted.

## SetData investigation boundary

A direct `SetData` patch is suitable for values that are fixed when an event activates. It is not currently sufficient as the only patch because:

1. `SetData` does not receive `currentEventData`, so custom JSON and exact event-chain origin would require state passed from `HandleColorChangeBeatmapEvent`.
2. Normal custom RGBA can be represented by `_fromColor` and `_toColor` after custom alpha is multiplied into each native brightness endpoint.
3. Explicit strobe RGB and inherited `color`/`strobeColor` alpha require phase-dependent RGBA selection every frame.
4. Beat Saber 1.29.1 lacks the modern strobe brightness/fade behavior being backported.

The useful next experiment is therefore not moving all logic to `SetData`. It is isolating whether a small transpiler can replace only native strobe RGBA endpoint selection while leaving native `SetColor` phase, fade, brightness, and manager dispatch intact for modern versions.
