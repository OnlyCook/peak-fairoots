# CODEBASE — where things live in `src/`

A brief map of `src/Fairoots/` (what each file/folder is responsible for), the
same way `peak-sense-of-direction/CODEBASE.md` and `peak-checkpoint-save`'s
structure do, so a reader can find where a given mechanic lives without
re-scanning the whole tree.

**Phase 2 (seed/preset core) and Phase 4 (spore bombs) are in.** Wind, spore
areas, and creatures are not written yet; see `ROADMAP.md`'s phased plan.

## The Core / game-facing split (read this first)

The single most important structural rule: **all of the mod's actual decision
logic lives in `src/Fairoots/Core/`, which is 100% Unity/BepInEx-free.** It
depends only on `System.*`. That is what lets the test project compile the same
source (via a `<Compile Include>` glob — no game install needed) and prove the
in-game behavior directly. Anything that touches `UnityEngine.*`, `BepInEx.*`,
or Harmony lives *outside* `Core/` (in the project root or future mechanic
folders) and calls into `Core/` for every probabilistic/seeded decision.

If you're adding logic and it doesn't strictly need a Unity type, it belongs in
`Core/` with a unit test — not in a Harmony patch.

## Current layout

- `src/Fairoots/` — the BepInEx plugin project (game-facing).
  - `Plugin.cs` — entry point (`BepInPlugin`); loads config + Harmony, logs.
  - `PluginConfig.cs` — config binding: the `seed` field, the `preset` 1-5
    selector (1-4 are the fixed presets, 5 is Custom — see
    `Core/Presets/PresetId.cs`), per-mechanic override entries (default to a
    "follow preset" sentinel), and the `Debug` section (bound last). Exposes
    *resolved* accessors (e.g. `SporeBombCullFraction`,
    `SporeBombKnockbackMultiplier`) that fold preset + override together via
    `Core/Presets/OverrideResolution`.
  - `PluginInfo.cs` — GUID/name/version constants.
  - `RootsLevelWatcher.cs` — detects a freshly-loaded Roots level (Roots prop
    placement is baked into the scene at author time, not regenerated at
    runtime — see `SporeBombCullPatch`'s remarks) and triggers the spore-bomb
    cull pass once per level.
  - **`Diagnostics/`** — the debug/runtime-logging harness (game-facing, off
    unless `Debug/enable-debug-logging` is on). See `docs/TESTING.md`.
    - `Diag.cs` — gated logger wrapper; `Diag.V(...)` only logs when debug is on.
    - `SceneDiagnostics.cs` — scans a loaded level and reports what the mod can
      and can't find (biome, wind zone + live field values, spore-bomb
      candidates + their hazard components/values, spore-area emitters,
      creatures). This is the Phase 3 tool for confirming the RESEARCH.md open
      questions from a real Roots level. Triggered by a postfix on
      `PropGrouper.RunAll` (auto, after level gen) and by a config hotkey.
    - `PingRadiusProbePatch.cs` / `RemovedMarkerOverlay.cs` — dev-only probes
      for the foliage-detection and cull-removal debug loop.
    - `TriggerRadiusOverlay.cs` — draws a red 3D wireframe (via `GL` immediate-
      mode drawing hooked into URP's `RenderPipelineManager.endCameraRendering`
      - confirmed PEAK runs URP, so the legacy `Camera.onPostRender` an earlier
      version tried never fires) around a nearby (within 10m) kept spore
      bomb's *actual* trigger `Collider`, matching its exact live shape/size,
      so the configured trigger-radius shrink can be eyeballed against the
      real prefab instead of guessed at. For the "Spore Bomb"/"Poison Spore
      Bomb" variants, the sphere is also visually flattened at exactly
      `SporeBombHeightGatePatch`'s trigger-height-cutoff plane (clipped
      meridian circles plus a filled, semi-transparent "cap" disc at the cut
      height - a thin ring alone barely read in a screenshot), so the
      wireframe shows the *actual* functional trigger volume, not the full
      vanilla sphere.
  - **`SporeBombs/`** — the Phase 4 Harmony patches (game-facing, calls into
    `Core/` for every decision or number):
    - `SporeBombCullPatch.cs` — scans a loaded Roots level once for spore-bomb
      candidates, applies `Core/SporeBombCull.cs`'s two-pass removal decision,
      and shrinks the trigger-hitbox `SphereCollider`(s) on every kept spore
      bomb by the configured multiplier (`Core/SporeBombExplosionTuning.cs`'s
      `ScaleTriggerRadius`) — a flat, seed-independent tweak, not a decision.
    - `SporeBombExplosionPatch.cs` — a narrowly-scoped Harmony prefix on the
      generic, game-wide `SpawnGameObject.Go` (the confirmed trigger→explosion
      spawn seam — the named spore-bomb object is only a trigger volume; the
      actual `AOE`/`ExplosionEffect`/`AddScreenshake` explosion doesn't exist
      until this fires). Only acts when the *triggering* object matches the
      spore-bomb name check; otherwise the original method runs untouched.
      Scales knockback, particle/VFX-orb count, and caps the screen-shake
      range via `Core/SporeBombExplosionTuning.cs`.
    - `SporeBombHeightGatePatch.cs` — a bug fix, not a preset dial: a Harmony
      prefix on the generic `TriggerEvent.OnTriggerEnter`, scoped to spore
      bombs by name, that suppresses the trigger entirely when the player is
      above a configurable height over the object's base
      (`Core/SporeBombExplosionTuning.cs`'s `ShouldSuppressTriggerForHeight`) -
      fixes the "Spore Bomb"/"Poison Spore Bomb" variants' vanilla trigger
      sphere reaching absurdly far above the actual mushroom mesh (confirmed
      via `TriggerRadiusOverlay`'s wireframe), which made jumping over one
      impossible. Left alone for the round "Explosive Spore Bomb" variant.
  - **`Core/`** — the pure, Unity-free decision layer (see split rule above):
    - `GridPos.cs` — a world position rounded to an integer grid; the stable
      per-object identity every seeded decision keys off. `GridPos.Round(...)`
      is the one rounding definition the whole mod uses.
    - `DeterministicHash.cs` — the determinism engine: a hand-rolled FNV-1a +
      murmur-finalizer hash mapping `(seed, mechanicTag, GridPos)` → a stable
      value / rank key, identical on every runtime and every launch.
      Deliberately **not** `HashCode.Combine`/`string.GetHashCode`/`System.Random`
      (all per-process-randomized or runtime-dependent — would break the seed
      guarantee). See its file comment.
    - `SporeBombCull.cs` — the two-pass spore-bomb removal decision (pure
      arithmetic + seeded ranked selection): unconditional foliage removal,
      then a seeded cull budgeted against it. Returns per-candidate outcomes;
      `SporeBombCullPatch` maps them back onto real GameObjects.
    - `SporeBombExplosionTuning.cs` — pure arithmetic for the trigger-radius/
      knockback/screen-shake-cap/VFX-count multipliers, plus the
      trigger-height-cutoff bug-fix decision (`ShouldSuppressTriggerForHeight`)
      (not seed-gated — every kept spore bomb gets the same flat treatment, so
      there's no per-instance decision here, just scaling/thresholding).
      `SporeBombExplosionPatch`/`SporeBombCullPatch`/`SporeBombHeightGatePatch`
      call into this for the numbers.
    - `Presets/PresetId.cs` — the preset enum: 1-4 are the fixed presets
      (Balanced is default), 5 is Custom (ignores the catalog and uses the
      player's own config directly).
    - `Presets/PresetCatalog.cs` — the per-preset numeric values (single source
      of truth): spore-bomb cull fraction, trigger-radius/knockback/
      screen-shake-cap/VFX-count multipliers, and always-on mechanic flags;
      grows one entry per mechanic as its phase lands. Custom (`PresetId.Custom`)
      isn't a real row in this catalog — every method maps it to Balanced's
      numbers internally, purely as a fallback for a setting the player hasn't
      touched yet under Custom (not "Custom follows Balanced").
    - `Presets/OverrideResolution.cs` — non-destructive preset resolution
      (sentinel pattern): a player-set value always wins over the preset.
- `tests/Fairoots.Tests/` — xUnit project. Links `src/Fairoots/Core/**/*.cs`
  directly (no game/BepInEx dependency, runs anywhere). One test file per Core
  area; see `docs/TESTING.md` for what each covers.
- `packaging/` — Thunderstore/Nexus packaging pipeline (`build-release.sh`,
  `gen-readme.sh`, `manifest.json`, `CHANGELOG.md`), same pattern as the
  other two PEAK mods in this GitHub account.
- `docs/TESTING.md` — automated-test coverage summary + manual in-game loop.

## Planned structure (fills in as phases land — see ROADMAP.md)

`SporeBombs/` (above) is the first mechanic-group folder; expect one more per
remaining mechanic group, mirroring `OVERVIEW.md`'s sections: `Wind/`,
`SporeAreas/`, `Creatures/` — each holding the Harmony patches that scan the
scene and apply removals/tweaks, delegating every seeded decision to `Core/`.
New per-mechanic preset values land in `Core/Presets/PresetCatalog.cs` as each
phase is implemented.
