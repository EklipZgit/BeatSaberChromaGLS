# Ferrofluid performance TODO

This audit covers the ferrofluid rendering, simulation, resources, presentation, and related menu integration. Non-ferrofluid GLS event and conversion work is tracked separately in `CHROMAGLS-PERF-TODO.md`.

## P0: make the ferrofluid pipeline viable at 180 Hz

### Replace the all-particles-per-height-texel pass

- [x] Replace the `HeightMapSize * HeightMapSize * ParticleCount` scan in `FerrofluidHeightMapFragment`.
- [ ] Preserve the current output contract while optimizing:
  - Smooth-min `resultDistance`, not only the geometrically nearest site.
  - Exact nearest and second-nearest distances used by `boundaryVisibility`.
  - Owning particle ID/UV used by the per-site spring state.
  - Torus wrap and corner-stretch metric.
  - Primitive per-site spacing and real surface-distance metric.
  - Octahedral chart fold/seam behavior for cube and sphere.
- [ ] Add GPU image/capture comparisons before replacing the existing implementation. Include static, moving, seam-crossing, corner, densely packed, and maximum-displacement cases.

The current 256-by-256 pass performs approximately:

- Torus: `65,536 * 576 = 37.7 million` particle comparisons per tick.
- Default cube: approximately 31.7 million comparisons per tick.
- Default sphere: approximately 21.2 million comparisons per tick.
- All three at 180 Hz: approximately 16.3 billion height-pass particle comparisons per second, before SPH, spring, site-surface, vertex, or fragment work.

#### Recommended first implementation: conservative candidate reduction

- [x] Build a coarse candidate texture or spatial-bin stage, then evaluate the existing exact distance/profile code against a conservative shortlist in the final 256-by-256 pass.
- [x] Retain the nearest four candidate IDs per 16-by-16 coarse cell; inspect a 3-by-3 coarse-cell neighborhood with explicit torus wrapping in the final pass.
- [x] Keep the existing smooth-min, nearest/second-nearest, boundary, and spring-owner equations unchanged after candidate selection.
- [ ] Increase the candidate count or neighborhood until reference captures are pixel-equivalent within an agreed tolerance.
- [x] Keep a development-only brute-force mode for A/B screenshots and GPU timings until parity is established.

Implementation status: the production shader now evaluates at most 36 gathered candidate entries per height texel instead of 324-576 particles. Candidate IDs are deduplicated and sorted before the exact equations run so pairwise smooth-min order remains deterministic. Managed/source-shape regression tests pass; GPU timing and pixel-equivalence captures remain required before removing the brute-force reference path.

This is preferable to a direct nearest-only jump flood because the current shader is not merely calculating a Voronoi owner. Distant sites stop influencing the smooth minimum once they are outside its narrow blend band, so a conservative local candidate list should retain the visual result without scanning all 324-576 sites for every texel.

#### Jump-flood compatibility assessment

A plain jump-flood algorithm that returns only one nearest site is highly likely to change existing behavior. It would lose the second-nearest boundary term and the multi-site smooth minimum, and seed collisions or chart seams could select a different owner. Treat that approach as high risk.

A k-nearest jump flood that propagates several unique site IDs and then runs the existing exact equations over those candidates is much safer, but still has moderate risk around:

- Torus wrapping.
- Rounded-rectangle corner metric changes.
- Octahedral cube/sphere folds.
- Multiple particles landing in the same seed texel.
- Sites that are not nearest at an intermediate jump step but matter to the final smooth-min band.

Engineering estimate before image-based validation:

- Nearest-only JFA: high chance of visible behavior changes.
- K-nearest JFA plus exact final evaluation: moderate chance of subtle seam/boundary changes.
- Conservative coarse bins/candidate lists plus exact final evaluation: low-to-moderate risk and the recommended starting point.

Do not delete the brute-force path until the optimized result has passed capture comparisons. If exact parity is required, use the optimized candidate set only when it can prove a conservative distance bound; otherwise fall back for that tile.

### Reduce the number of fullscreen passes

- [ ] Measure and record GPU time for every simulation pass at 90, 120, 144, and 180 Hz.
- [x] Treat `SimulationSteps` as the literal solver-pass count; the primitive default now submits two passes instead of the former hidden three, while the torus remains at two.
- [ ] Evaluate batching torus/cube/sphere state in texture arrays or one compute dispatch.
- [ ] Evaluate combining density and force work, or using a uniform-grid neighbor list instead of two all-pairs SPH scans.
- [ ] Submit the tick through one command buffer rather than many independent immediate `Graphics.Blit` calls.
- [ ] Decouple visual presentation rate from expensive field reconstruction if a 180 Hz simulation does not require a 180 Hz height-map rebuild.
- [ ] Consider rebuilding the height map only when particle state actually advances and updating lighting/pointer uniforms independently.

The current default pass budget is approximately 480 blits/second for the torus at 60 Hz and 720 blits/second for each enabled primitive. Tripling the cadence without restructuring would approach 5,760 blits/second with all three enabled.

### Bake and share the immutable surface mesh

- [ ] Add the 256-by-256 UV surface mesh to `ferrofluid-windows.bundle`.
- [ ] Share one immutable mesh among torus, preview, cube, and sphere instances.
- [ ] Remove per-instance `CreateSurfaceMesh` allocations and per-instance mesh destruction.
- [ ] Consider a lower-resolution mesh/height-map LOD for the small SongCore requirement marker.

Each current instance constructs 65,536 vertices, 65,536 UVs, and 390,150 indices even though the mesh data is identical.

### Prebake production primitive rest positions

- [ ] Bake the authored default cube and sphere relaxed rest-position textures during the AssetBundle build.
- [ ] Load those baked textures in normal builds.
- [ ] Keep the 400-iteration runtime relaxation generator only for `FERROFLUID_TWEAKS` or non-default topology.
- [ ] Run non-default regeneration outside an interactive frame where possible.
- [ ] Retain the bounded cache for tweak builds, but expose cache misses in profiling diagnostics.

Normal builds expose only enable toggles, so their authored default density and roundness combinations do not need runtime procedural relaxation.

## P1: stop uploading invariant GPU state every frame

- [ ] Split ferrofluid uniforms into construction-time, configuration-time, transform-dirty, simulation-tick, and presentation-frame groups.
- [ ] Cache the last `Rect`, pivot, skew, curvature, matrices, viewer sign, and source-world scale.
- [ ] Call `ApplyGeometry` only when one of those inputs changes.
- [ ] Assign `_HeightMapTex` once after creation/recreation rather than every frame.
- [ ] Skip `ApplyWorldScaleParameters` when scale is unchanged.
- [ ] Upload object-to-world only when the renderer transform changes.
- [ ] Avoid recalculating curved-canvas matrices while neither transform has changed.
- [ ] Consider `MaterialPropertyBlock` for truly per-instance presentation values if material duplication is otherwise unnecessary.

### Compile immutable shader modes as variants

- [ ] Compile separate torus and primitive shader variants, or use local keywords selected once at construction.
- [ ] Split cube and sphere only if captured GPU profiles show their uniform branches inhibit optimization.
- [ ] Compile an always-visible variant that omits screen-door fade work where appropriate.
- [ ] Strip unused variants during AssetBundle construction.
- [ ] Confirm that variant changes reduce GPU time on the actual Beat Saber graphics APIs before keeping the extra bundle complexity.

## P1: quarantine diagnostic costs after validation

Behavior has been confirmed, so production retains only actionable startup/failure messages:

- [x] Remove once-per-second ferrofluid material, lighting, pointer, and state dumps together with their diagnostic-only helpers and retained state.
- [x] Remove the per-frame transition watcher and its armed-window `StringBuilder`/interpolated-message allocations.
- [x] Keep short failure/startup messages that are useful for support while removing recurring debug state from ordinary Release builds.

## P2: replace cold reflection where version-specific builds permit it

These are lower priority because they run during patch installation, object creation, or song selection rather than every frame.

### Menu controller discovery

- [ ] Use typed `MenuPlayerController` and controller access in game versions where the publicized target assemblies expose them.
- [ ] Where reflection is still required, cache the resolved type and compiled left/right/view-anchor getters statically.
- [ ] Do not repeat `AccessTools.TypeByName` during every failed two-second anchor poll.
- [ ] Prefer a menu-scene lifecycle/DI registration over repeated `Resources.FindObjectsOfTypeAll` scans.

### SongCore and HMUI accessors

- [ ] Replace stable `RequirementsUI` field/property `GetValue` calls with `AccessTools.FieldRef` or compiled getters.
- [ ] Cache getters by runtime type for SongCore layouts that genuinely vary independently of the game build.
- [ ] Use typed `CurvedCanvasSettings`, skew, and radius access where a target build provides stable types.
- [ ] Keep SongCore writer target resolution at patch-install time; it is not a gameplay hot-path problem.

## Verification checklist

- [ ] Capture GPU timings per pass rather than relying only on total frame time.
- [ ] Test torus only, each primitive separately, and all three together.
- [ ] Test 60, 90, 120, 144, and 180 Hz configurations.
- [ ] Compare no-controller idle, one moving controller, both controllers, and menu transition cases.
- [ ] Include torus wrap, rounded corners, cube folds, sphere folds, direct hits, lateral misses, and density extremes.
- [ ] Verify every supported game build because shader bundle compatibility and publicized type layouts differ.
- [ ] Retain the current brute-force/reference paths until optimized GPU captures and managed regression tests agree.
