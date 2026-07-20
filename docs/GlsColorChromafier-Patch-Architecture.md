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

This handler runs when a GLS node activates and supplies `currentEventData`, including access to Chroma custom JSON and the next-event chains. Native `HandleColorChangeBeatmapEvent` invokes `SetData` internally. The prefix stages data before that native call, and the postfix finalizes RGB while preserving the brightness alpha calculated by native code.

The injected `_fromColor`, `_toColor`, `_alternativeFromColor`, and `_alternativeToColor` fields avoid reflection and boxing. This path does not run once per rendered frame.

### Modern per-frame custom strobe selection

`SetColorPrefix` patches `SetColor` for versions after 1.29.1.

It returns immediately and allows native `SetColor` when there is no explicit custom `strobeColor` state or no strobe frequency. Color-only custom strobes therefore remain entirely native. It replaces native execution only when the strobe requires phase-dependent selection between the normal RGB endpoint and a distinct `strobeColor` RGB endpoint.

`_fromColor`/`_toColor` and `_alternativeFromColor`/`_alternativeToColor` remain the native regular/boost pairs. A per-effect `Dictionary<LightColorGroupEffect, StrobeColorState>` stores only the additional strobe RGB track and is cleared by a `Cleanup` postfix. The normal RGB path always reads the active native pair, so `UseBoostColors` remains authoritative.

`SetData` can stage fixed endpoints, brightness, frequency, and fade values, but cannot express an additional independently interpolated RGB track selected by strobe phase. Removing this prefix therefore requires either changing the runtime data representation or using a narrowly scoped transpiler inside native `SetColor`.

The production calculations and `LightWithIdManager.SetColorForId` call use strongly typed Harmony field injection. They do not use reflection or allocate an invocation argument array. The only recurring state lookup is the direct dictionary lookup needed to obtain the extra strobe track.

### 1.29.1 per-frame backport

`LegacySetColorPrefix` replaces `SetColor` for Beat Saber 1.29.1 strobes.

The original 1.29.1 implementation strobes to its static off color and lacks the later strobe brightness/fade behavior. Event-time endpoint staging alone cannot reproduce the newer phase, brightness, fade, and explicit `strobeColor` behavior. This path therefore remains a full per-frame compatibility implementation.

The `LightColorGroupEffect` fields and final `SetColorForId` call are strongly typed and do not use reflection. Explicitly cleaned dictionaries carry the 1.29.1 strobe brightness/fade and cached fog compatibility state absent from the legacy instance.

### Production reflection boundary

There is no reflection in either production `SetColor` hot path.

For 1.29.1, `PrepareLegacyFogState` discovers the version-specific tube renderer once when an event activates. It creates and caches Harmony's compiled `FieldRef<object, float>` for `_bloomFogIntensityMultiplier`. Per-frame `SetLegacyFogCompensation` performs a dictionary lookup and direct ref assignment without `FieldInfo.GetValue`, `FieldInfo.SetValue`, or boxing.

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
2. Normal custom RGB can be represented by `_fromColor` and `_toColor` and is already left to native `SetColor`.
3. Explicit `strobeColor` requires phase-dependent RGB selection every frame.
4. Beat Saber 1.29.1 lacks the modern strobe brightness/fade behavior being backported.

The useful next experiment is therefore not moving all logic to `SetData`. It is isolating whether a small transpiler can replace only native strobe RGB endpoint selection while leaving native `SetColor` phase, fade, brightness, and manager dispatch intact for modern versions.
