# ChromaGLS staged-changes review checklist

Source: adversarial review of the staged diff (307 files, ~57.9k insertions) on 2026-11-09.

## How to work through this file

- Work top to bottom. P1 items block shipping; P2 items are bugs; later sections are cleanup.
- Check an item (`- [x]`) only when its **Done when** condition is met and verified.
- Do not stage, unstage, or commit. The user manages staging.
- Follow the workspace rules in `C:\src\BeatSaberStuff\.windsurfrules` (no source-text assertions in new tests, no new tests for performance-only refactors, undo failed attempts, use `Scratch/` for temporary work).
- Test evidence below came from the **pre-existing** `ChromaGLS.Tests/bin/Release-1.44.1/net48/ChromaGLS.Tests.exe` (built 2026-11-08 19:54), run without rebuilding under Unity's Mono:
  `& "C:\Program Files\Unity\Hub\Editor\6000.5.1f1\Editor\Data\MonoBleedingEdge\bin\mono.exe" ChromaGLS.Tests.exe --noresult [--test=<FullName>]`
  Result: 884 tests, 850 passed, 11 failed, 23 skipped. Full output: `C:\src\BeatSaberStuff\Scratch\chromagls-review\full-run.txt`.
  That binary may be slightly stale relative to the staged source. Rebuild through `pwsh ./RunAllTests.ps1 -Version 1.44.1 -NoDeploy [-Test <names>]` before trusting a result.
- Memory figures are computed from texture sizes/formats, not from a GPU or memory capture. Use the `unity-pre-ship-check` skill for measured confirmation.

---

## P1 — blocks shipping

### 1. Release DLL is ~303 MB and the bundle stays resident

Evidence:
- `ChromaGLS/Settings/ferrofluid-windows.bundle` = 303,367,481 bytes; `ChromaGLS/bin/Release-1.44.1/net48/ChromaGLS.dll` = 303,817,728 bytes (bundle is an `EmbeddedResource` in `ChromaGLS/ChromaGLS.csproj`).
- `ChromaGLS/Settings/FerrofluidGpuResources.cs:296-356` reads the whole resource into a `byte[]` and keeps it forever in `_bundleBytes`.
- `FerrofluidGpuResources.TryLoadReflections` (`:87-141`) calls `AssetBundle.LoadFromMemory(_bundleBytes)` per tier and keeps `_reflectionBundle` loaded, so a second native copy stays resident.
- `Ferrofluid.Unity/Assets/Editor/FerrofluidRoomReflectionAssets.cs:65-77,90,113` imports EXRs uncompressed (`RGBAHalf`) and builds `RGB9e5Float` textures/cubemaps with mips. Both Low and High tiers plus a 4096² floor texture are in the bundle.
- Many on/off cube faces are byte-identical (same git blob hash), e.g. `SourceTextures/RoomReflections/off-0-positiveX.exr` and `on-0-positiveX.exr`. The bundle stores both.

Tasks:
- [ ] 1a. Compress reflection textures (e.g. BC6H) in `FerrofluidRoomReflectionAssets.cs` and confirm shaders still decode correctly (run the existing reflection validations in `Ferrofluid.Unity/Assets/Editor/`).
- [ ] 1b. Move reflection-tier assets out of the embedded core bundle into a separately shipped bundle file loaded with `AssetBundle.LoadFromFile`/`LoadFromStream`; stop retaining `_bundleBytes`.
- [ ] 1c. De-duplicate identical on/off faces (store once, or store only the faces that differ).
- [ ] 1d. Check whether both Low and High tiers need to ship; drop or download-on-demand if not.

Done when: the release `ChromaGLS.dll` is a few MB, no full-bundle `byte[]` is retained after load, and reflection rendering still passes the existing reflection validations. Record before/after DLL size and resident memory.

### 2. High reflection tier allocates ~1 GB VRAM and is the default for GPUs with ≥ 2 GB

Evidence:
- `ChromaGLS/Settings/ReflectionQuality.cs:24-29`: `graphicsMemoryMb >= 2048` → `High`.
- `ReflectionQuality.cs:40-41`: `GetMenuLightResolution(High)` = 8192.
- `ChromaGLS/Settings/MenuLightReflectionCache.cs:274-286`: 8192² `RGB111110Float` Tex2DArray with mips ≈ 341 MB **per light**, up to 8 lights; `:142` allocates an extra 8192² temporary per bake (~256 MB).
- `MenuLightStore` constructor always seeds at least one light; `Config.ParabolicFloorLightEnabled` defaults to `true`.
- Room cubemaps at High are 2048 per face (`GetRoomResolution`), on + off ≈ 270 MB with mips; 4096² floor ≈ 90 MB.

Tasks:
- [ ] 2a. Capture actual VRAM use for Low and High in the menu (unity-pre-ship-check skill) to confirm the estimate.
- [ ] 2b. With the user, pick tier resolutions and the auto-select threshold justified by that capture. Update `ReflectionQuality.cs` and `ReflectionQualityTests` expectations together.
- [ ] 2c. Re-check the temporary bake texture size after the change.

Done when: measured High-tier VRAM fits an agreed budget and the auto-default no longer picks a tier the device can't comfortably hold.

### 3. Remove junk and decide on large binaries in the staged set

Tasks:
- [ ] 3a. Remove the botched-`cp` file `Ferrofluid.Unity/Assets/Editor\357\200\242\357\200\212cp FerrofluidProbeObserver.cs C\357\200\272srcBeatSaberStuffBeatSaberChromaGLSFerrofluid.UnityAssets\357\200\242` (contains a one-off `FerrofluidFloorContactProbe`). Confirm with the user before deleting.
- [ ] 3b. Remove `Ferrofluid.Unity/SourceTextures/RoomReflections$b` (4.3 MB EXR, no references). Confirm with the user before deleting.
- [ ] 3c. `Ferrofluid.Unity/SourceTextures/PositionalRoomHigh/` and `PositionalRoomLow/` (28 files, ~20 MB): nothing in `BuildFerrofluidBundle.ps1`, `BuildFerrofluidBundle.cs`, `FerrofluidRoomReflectionAssets.cs`, or plugin code references them. Confirm with the user, then remove or document why they are kept.
- [ ] 3d. `RoomReflections/*.exr` (13 × 6.3 MB) and `RoomReflectionsHigh/*.exr` (12 × 25 MB + floor) are committed without LFS (no `.gitattributes`). Ask the user: Git LFS, external storage, or keep as-is.
- [ ] 3e. `Ferrofluid.Unity/Assets/Editor/FerrofluidOrbitConstraintProbe.cs` has no references. Remove or wire into `FerrofluidValidationRunner`.
- [ ] 3f. Ask the user whether `CHROMAGLS-PERF-TODO.md` and `FERRO-TODO.md` belong in the repo.

Done when: every listed path is removed, ignored, or explicitly kept by user decision.

---

## P2 — bugs

### 4. Corrupt `MenuLightsJson` can break menu setup

Evidence: `ChromaGLS/Settings/MenuLightStore.cs:15-48` calls `JsonConvert.DeserializeObject<MenuLightDefinition[]>` with no try/catch. It runs from the `ParabolicFloorLight` constructor (`ParabolicFloorLight.cs:301-305`), outside the try in `Initialize()`, and `ParabolicFloorLight` is bound `NonLazy` in `ChromaGLSMenuInstaller`. Null array entries would NRE at `definition.GetRotation()`; more than 8 entries makes `MenuLightReflectionCache.Synchronize` throw.

- [ ] 4a. Write a failing test first: a `Config` with malformed `MenuLightsJson`, one with a `null` entry, and one with 9 entries; constructing the store must not throw and must fall back to a valid list.
- [ ] 4b. Make `MenuLightStore` log and fall back (skip nulls, clamp to `MaximumLights`, default on parse failure).

Done when: the new tests fail before the fix and pass after.

### 5. `FerrofluidTweaksAreSessionOnly` fails

Evidence: `ChromaGLS.Tests/FerrofluidToggleTests.cs:1514-1543`. Confirmed failure: "ReflectionsQuality must be ignored by BSIPA serialization...". `ReflectionsQuality`, `ReflectionsQualityInitialized`, and `MenuLightsJson` are intentionally persisted (no `[Ignore]` in `Config.cs`) but missing from the exemption list. The mutation step `(float)original + 0.1f` also cannot handle enum or string properties.

- [ ] 5a. Add the three properties to the persisted list (or exempt non-scalar persisted properties) without weakening the `[Ignore]` guarantee for tweak values.

Done when: the test passes and still fails if a tweak property loses `[Ignore]`.

### 6. Other failing tests in the existing build

- [ ] 6a. `FerrofluidMenuTimingProbeTests` (7 tests) error with `ArgumentNullException` in `NewProbe`: `FerrofluidMenuTimingProbe` is `#if FERROFLUID_TWEAKS` only. Make these tests ignore themselves in normal builds, matching `FerrofluidSnapshotTests` / `FerrofluidFpfcBindingTests` ("Normal builds do not compile ...").
- [ ] 6b. `FerrofluidToggleTests.PrimitiveRestPositionRelaxationMeetsLoadTimeBudget`: measured 1734 ms vs 1500 ms budget. This is a wall-clock assertion (flaky) but also signals a real menu-entry CPU stall. Profile the relaxation in `FerrofluidGpuSimulation`/`FerrofluidPrimitiveLayout`; escalate to the user before changing the budget.
- [ ] 6c. `FerrofluidFluidObjectsTests.BothViewsExposeTheTwoFluidSwitches`: expects "Add Fluid cube", BSML has "Enable Fluid Cube". Ask the user which label is intended; fix the other side.
- [ ] 6d. `ParabolicFloorLightSettingsTests.TweakModalBindsAllLightSlidersWithExpectedRanges`: expects "Height", BSML has "Up/down". Same: ask the user which is intended.

Done when: rebuilt test run (`RunAllTests.ps1 -Version 1.44.1 -NoDeploy`) shows these passing or deliberately ignored with user approval.

### 7. `Tests/BuildConfigurations.Tests.ps1` cannot pass

Evidence: `Tests/BuildConfigurations.Tests.ps1:11` creates `ChromaGLS.sln` in the fixture, but `BuildAllVersions.ps1:90,97-99` requires `ChromaGLS.Managed.slnf`. The test loops over six game versions (`:39-42`) without setting `BEATSABER_*`, which `BuildAllVersions.ps1` preflight requires. It writes fixtures to `$PSScriptRoot/../../Scratch/...`, which only exists in this workspace layout.

- [ ] 7a. Create `ChromaGLS.Managed.slnf` in the fixture.
- [ ] 7b. Set fake `BEATSABER_*` environment variables pointing at fixture directories inside the test.
- [ ] 7c. Write fixtures under a temp directory (`[IO.Path]::GetTempPath()`) instead of `../../Scratch`.

Done when: the script passes on a machine without Beat Saber installs.

### 8. Hardcoded developer path in a validation

Evidence: `Ferrofluid.Unity/Assets/Editor/FerrofluidGrabValidation.cs:44` uses `C:\Users\tdrak\BSManager\BSInstances\1.44.1\Beat Saber_Data\Managed`.

- [ ] 8a. Resolve the managed directory from `BEATSABER_1_44_1` (or the shared `FerrofluidValidationRuntime` helper) instead.

Done when: no literal user path remains in `Ferrofluid.Unity/Assets`.

### 9. Duplicate statement

Evidence: `ChromaGLS/Settings/FerrofluidGrabbable.cs:522-523` repeats `_body.rotation = Quaternion.Euler(0f, _body.rotation.eulerAngles.y, 0f);`.

- [ ] 9a. Delete one line (or the whole method; see item 13).

---

## Reflection that should be compile-time

- [ ] 10. `ChromaGLS/Settings/FerrofluidGpuSimulation.cs:367-379` uses `Type.GetType("UnityEngine.XR.XRSettings, UnityEngine.VRModule")` + `GetProperty` for a log line. On newer Unity `XRSettings` is in `XRModule`, so it logs "unknown". The csproj already references `UnityEngine.XRModule`. Use `XRSettings.stereoRenderingMode` / `isDeviceActive` directly, with an `#if` for versions where it lives in VRModule (verify per supported version which assembly defines it).
- [ ] 11. Unity validation scripts: 23 of 78 `*Validation*.cs` call `Assembly.LoadFrom`, 22 hardcode `Release-1.44.1/net48`, each repeating path discovery and string-named reflection (e.g. `FerrofluidCameraCollisionValidation.cs:61-74`, `FerrofluidControllerValidation.cs:46-58`, `FerrofluidContactResponseValidation.cs:74-108`). Route them all through `FerrofluidValidationRuntime.cs` (or a publicized plugin reference) and make the configuration a parameter.
- [ ] 12. (Pre-existing, optional) `ChromaGLS/Settings/ChromaGlsRequirementEffect.cs:16,86-90,598,1203-1211` uses `AccessTools.Field/Property` on `RequirementsUI` and related types. Replace with publicized access where the field exists in all supported versions, using `#if` for differences.

Done when: no runtime reflection remains for fields/types available at compile time.

---

## Performance

- [ ] 13. Test-only spin-test code ships in Release: `FerrofluidGrabbable.StartSpinTest/StopSpinTest/UpdateSpinTest/IsSpinTesting` and `FerrofluidEnvironmentObjects.RunSpinTest/StopSpinTests` are only called by `Ferrofluid.Unity/Assets/Editor/FerrofluidSpinValidation.cs` (and referenced by `FerrofluidPivotDampingValidation`, `FerrofluidDepthValidation`, `FerrofluidControllerValidation`) via reflection, yet `_spinTestPhase` checks run in every `FixedUpdate`. Guard with `#if FERROFLUID_TWEAKS` or remove, and update those validations.
- [ ] 14. `ChromaGLS/HarmonyPatches/NodeEasingReader.cs:76-84` loads every rotation/translation/FX node into a `JObject` and replays it, for every map (persistent patch, active even when ChromaGLS is disabled). Pass nodes through unbuffered when they have no `customData.easing`. Run the existing `GlsCustomNodeEasingIntegrationTests` afterwards.
- [ ] 15. `ChromaGLS/Helpers/GlsSplineEventIndex.cs:13-79` scans `allBeatmapDataItems` once per level for all users (persistent constructor patches) and stores every translation node per element plus `UsesDistancePacing` customData reads for every color event, even when nothing is distance-paced. Defer list building to channels that are actually paced. Ask the user first per the "evidence before added complexity" rule; capture load-time cost on a large map.
- [ ] 16. `ChromaGLS/Settings/FerrofluidGpuSimulation.cs:1005-1012` (`UpdatePrimitiveFrame`) sets constant uniforms (`MagnetOpeningNormal = up`, `Direction = zero`, `Strength = 0`) every frame per primitive. Set once at creation.
- [ ] 17. (Optional) `FerrofluidMagnetController.TryGetPointerState` (`:217-260`) runs `EventSystem.RaycastAll` every menu `LateUpdate`, in addition to the EventSystem's own raycast. Menu-only; profile before changing.
- [ ] 18. (Suspected, needs GPU capture) `Ferrofluid.Unity/Assets/FerrofluidSurface.shader:804-831`: 16-tap per-vertex kernel (height sample + `SpikeDeformation` + spring state each). `Ferrofluid.Unity/Assets/MenuLightCubeSpill.shader:59-69`: up to 32-iteration per-fragment loop. Profile before changing.

---

## Duplication and simplification

- [ ] 19. `ChromaGLS/Settings/FerrofluidEnvironmentObjects.cs` (`FerrofluidEnvironmentManager`, ~`:582-1047`): `_cube/_sphere/_fluidCube/_fluidSphere` and matching `*CreationFailed` flags are handled four times in ~15 places. Replace with an array of four slots plus a shape/fluid descriptor.
- [ ] 20. `ChromaGLS/Settings/FerrofluidGpuSimulation.cs`: ~100 `Set*` + `RecordSnapshot*` pairs, often repeating `Mathf.Clamp(...)` arguments. Add one helper per value type that sets and records.
- [ ] 21. `ChromaGLS/Settings/ParabolicFloorLight.cs:11-41`: `ParabolicFloorLightMeshData` and `ParabolicFloorLightGlowMeshData` are identical. Merge.
- [ ] 22. `ParabolicFloorLight.cs:460-476`: the 17-field `changed` comparison belongs on `MenuLightDefinition` as an equality method.
- [ ] 23. Migration from settings that never shipped: the 14 persisted `ParabolicFloorLight*` properties in `Config.cs` and the `legacy-parabola` / `MigrateLegacy` path in `MenuLightStore.cs` convert a format that was never released (HEAD's `Config.cs` has none of them). Confirm with the user, then keep only `MenuLightsJson` and update the affected tests.
- [ ] 24. `FerrofluidEnvironmentObjects.EvaluateSurfacePositionFromDirection` (`:194-212`) duplicates the position math in `EvaluateSurfacePositionAndNormal` (`:233-261`). `FerrofluidPrimitiveEffect.MapPointer` (`:1287-1289`) calls position and normal separately; use the combined helper.
- [ ] 25. `FerrofluidMagnetController.CreateForGrab` (`:153-168`) builds a controller via `Create()` and copies it into a second one. Pass `enableHoverRaycast` into `Create` instead.
- [ ] 26. `ChromaGLSSettingsUI.cs:63-64,148-160` and `ParabolicFloorLight.cs:347-365` both subscribe to `sceneLoaded`/`activeSceneChanged` with the same GameCore/MainMenu checks. Share one menu/gameplay notifier.
- [ ] 27. `FerrofluidSettingsUI`, `FerrofluidTweakEditSession`, `FerrofluidTweakSettings`, and the embedded `ferrofluid-settings.bsml` compile into Release but are only constructed under `FERROFLUID_TWEAKS`. Guard them (and their tests) consistently.
- [ ] 28. `BuildAllVersions.ps1:88-95` and `RunAllTests.ps1:43,69-70` both define the supported-version list and `BEATSABER_*` mapping. Move to `ScriptCommon.ps1`. Remove the commented-out `$SupportedVersions` line at `BuildAllVersions.ps1:89`.
- [ ] 29. Style: `ChromaGLS/Helpers/GlsDistancePath.cs` uses an unindented namespace body and camelCase private fields (`intervals`, `lastInterval`) unlike the rest of the project.
- [ ] 30. `ChromaGLS/HarmonyPatches/GlsColorChromafier.cs`: ~30 blank-line-only edits add noise. Revert unless the user wants them.

---

## Test quality

- [ ] 31. Replace BSML source-text assertions (e.g. `FerrofluidToggleTests.cs:1506-1509` `Does.Contain("value='...' apply-on-change='true'")`, `AssertSlider` on raw markup in `FerrofluidToggleTests`, `FerrofluidTweakModalTests`, `ParabolicFloorLightSettingsTests`, `MenuLightSettingsUITests`) with behavior checks through parsed BSML/production APIs, or ask the user whether BSML counts as product output for their testing rule.
- [ ] 32. Fix stale comments that contradict the asserted values, e.g. `FerrofluidToggleTests.cs:1556` ("tall=1.70" vs asserted 0.9) and `:1595` ("pinned at 1.00 and dead zone zero" vs asserted 0.74 / 0.13). Ask the user whether tunable-default assertions should stay at all.

---

## Final verification (after all items)

- [ ] Rebuild and run the full suite once: `pwsh ./RunAllTests.ps1 -NoDeploy` (all versions) or the subset the user specifies. Report failures and the scope actually tested.
- [ ] Run `pwsh ./Tests/BuildConfigurations.Tests.ps1` and `pwsh ./Tests/BeatSaberDevelopmentPlayer.Tests.ps1`.
- [ ] Re-run the reflection/menu-light validations in `Ferrofluid.Unity` affected by items 1, 2, 11, and 13.
- [ ] Record final DLL size and measured menu RAM/VRAM for Low and High tiers.
