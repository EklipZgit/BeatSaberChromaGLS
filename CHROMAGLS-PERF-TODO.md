# ChromaGLS performance TODO

This audit covers non-ferrofluid ChromaGLS gameplay, conversion, patching, and diagnostic costs. Priorities are based on execution frequency, expected map sizes, allocation pressure, and opportunities to move work from event-time paths into conversion or initialization.

## P1: remove hot-path allocations and repeated parsing

### Lazily allocate GLS custom data

- [x] In `GlsDeserializePatch`, allocate box and light `CustomData` only when a `customData` property or legacy strobe compatibility field requires it; unsupported group metadata is skipped without allocation.
- [x] Remove the unused group-level `CustomData` allocation because group custom data is intentionally unsupported.
- [x] Construct native `_LightColorBaseData` for nodes with no custom values.
- [x] Construct `LightColorBaseDataSaveData` only when custom metadata must survive conversion.
- [x] Add a 512-node ordinary-GLS-map regression test proving that empty custom dictionaries and wrappers are no longer retained while an authored custom node keeps its carrier.

### Compile typed event metadata during conversion

- [ ] Introduce one immutable typed metadata object for a GLS color event.
- [ ] Populate it once during conversion/unpack with:
  - Main and strobe colors.
  - Strobe interval, brightness, and fade.
  - Interval, normal-color, strobe-color, and strobe-fade easing evaluators.
  - HSV/RGB transition mode.
  - Parsed color-distribution instructions and progress.
  - A precomputed `RequiresCustomRendering` feature mask.
- [ ] Make event activation consume typed fields instead of repeatedly hashing string keys in `CustomData`.
- [ ] Remove event-time `List<object>` color conversion and boxed `Convert.ToSingle` calls.
- [ ] Preserve the typed metadata through every gameplay copy/transform.

### Parse color distributions once per source node

- [ ] Parse event-level normal and strobe distribution strings in `LightColorEventBoxConverterPostfix`.
- [ ] Store parsed arrays alongside the per-index custom-data handoff.
- [ ] Reuse those arrays for every element/filter unpack of the box.
- [ ] Represent `Instruction` as a readonly value type if profiling shows object allocation remains material.
- [ ] Replace `string.Split` with a small span/index parser if distribution parsing still appears in load profiles.

### Eliminate redundant unpack materialization

- [x] Cast modern `__result` to `List<BeatmapEventData>` before calling `ToList`.
- [ ] Prefer a version-specific direct output-list patch where supported.
- [x] Give the conversion, emitted-event, and filter-projection temporary lists their known maximum capacity.
- [ ] Determine the final event subtype before construction so custom-plus-distribution events allocate one replacement object rather than two.
- [ ] Avoid allocating a new empty `CustomData` for every box-only distribution event; use a representation that can carry distribution context without pretending it has mutable custom data.

### Reduce the `LightColorGroupEffect.SetColor` interception cost

- [ ] Profile the prefix on a large ordinary GLS map and a fully customized map.
- [ ] Make the first branch a minimal staged-state/feature-mask check before calculating interval, normal, and strobe progress.
- [ ] Investigate replacing the tween callback only for affected effect instances with a precompiled custom callback.
- [ ] Alternatively, assign each effect a dense state slot during initialization rather than hashing the Unity object through multiple dictionaries per callback.
- [ ] Consider installing the custom render patch only while the loaded map contains a feature requiring it, provided song transitions and cached beatmap data remain correct.

## P1: quarantine diagnostic costs after validation

Behavior has been confirmed, so production retains only actionable startup/failure messages:

- [x] Remove recurring color-distribution and strobe diagnostics together with their counters and dormant diagnostic blocks.
- [x] Remove the reflection-hit sampling postfix so production retains the corrective prefix without manager traversal and logging.
- [x] Remove `SeenHitPoints` and the diagnostic-only reflected manager/material accessors rather than retaining lifecycle state solely for logging.
- [x] Keep short failure/startup messages that are useful for support while removing recurring state dumps from ordinary Release builds.

## P2: replace cold reflection where version-specific builds permit it

These are lower priority because they run during patch installation, object creation, or song selection rather than every frame.

### Harmony toggle installation

- [ ] Replace `PatchAll(assembly)` on every re-enable with an explicit list of effect patch classes.
- [ ] Keep the persistent annotation patch list explicit and ensure every method remains installed exactly once.
- [ ] Measure toggle latency before choosing between explicit repatching and permanently installed cold-path gates.

## Existing reflection that is already appropriately amortized

Do not prioritize these ahead of the event metadata and allocation work:

- The legacy CustomJSONData parser method is resolved once and converted to a delegate.
- Harmony target-method discovery occurs at patch installation.
- The legacy GLS transpiler resolves operands only while installing the patch.
- Tube-bloom hit-point colors use direct publicized field access, with the patch excluded at compile time on older versions.

## Verification checklist

- [ ] Record allocations for ordinary GLS maps and heavily customized GLS maps separately.
- [ ] Profile ordinary, native-only GLS maps to establish the unaffected-event baseline.
- [ ] Profile maps using custom colors, easing, distributions, strobes, and HSV transitions independently and together.
- [ ] Verify gameplay output and event ordering after moving metadata work into conversion.
- [ ] Verify every supported game build because conversion targets and publicized type layouts differ.
