# CODEBASE — where things live in `src/`

A brief map of `src/Fairoots/` (what each file/folder is responsible for), the
same way `peak-sense-of-direction/CODEBASE.md` and `peak-checkpoint-save`'s
structure do, so a reader can find where a given mechanic lives without
re-scanning the whole tree.

**Phase 2 (seed/preset core), Phase 4 (spore bombs), and Phase 5 (wind) are
in.** Spore areas and creatures are not written yet; see `ROADMAP.md`'s phased
plan.

**Host authority (locked in 2026-07-22 — read `ROADMAP.md`'s "Host authority"
section before touching `PluginConfig.cs` or anything under `Wind/`/
`SporeBombs/`).** Every client needs the mod installed, but only the host's
config is ever actually used for anything that changes shared gameplay — see
`Networking/` below.

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
  - `PluginConfig.cs` — config binding: the `seed` field, `apply-changes-live`
    (see below), the `preset` 1-5 selector (1-4 are the fixed presets, 5 is
    Custom — see `Core/Presets/PresetId.cs`), per-mechanic Custom-only entries
    (sane numeric defaults, only read when preset is set to Custom — ignored
    under presets 1-4 even if changed), and the `Debug` section (bound last).
    Exposes *resolved* accessors (e.g. `SporeBombCullFraction`,
    `SporeBombKnockbackMultiplier`) that fold preset + override together via
    `Core/Presets/OverrideResolution`, plus a parallel set of `Effective*`
    accessors (e.g. `EffectiveSporeBombKnockbackMultiplier`) that game-facing
    code should read instead: with `apply-changes-live` on (default) they just
    pass the live resolved value through, but with it off they return a
    snapshot frozen at the last Roots level load (`CaptureLevelSnapshot`,
    called by `RootsLevelWatcher` right before `SporeBombCullPatch.Run`), so
    every non-Debug setting only changes on the next Roots load instead of
    mid-level. The spore-bomb removal fraction and the seed are level-load-only
    either way — which bombs got removed can't un-happen mid-level. **Almost
    every `Effective*` accessor is also host-authoritative** (wrapped in
    `Networking/HostAuthority.Resolve` — see that folder below): a no-op on
    the host, but overridden by the host's published value on every other
    client. The only accessors deliberately excluded (stay purely per-client)
    are `EffectiveWindFallCameraDampenClamp` and the flat
    `WindRecentForceWindowSeconds`/`Debug` section entries.
  - **`Networking/`** — host authority (game-facing, Photon-dependent;
    `ROADMAP.md`'s "Host authority" section is the full rationale):
    - `HostAuthority.cs` — `Resolve(key, localValue)` (typed overloads for
      `int`/`float`/`double`/`bool`): returns `localValue` unchanged on the
      host, or on any other client, whatever the host last published to the
      Photon room's custom properties (falling back to `localValue` if
      nothing's been published yet — solo play, or the brief window before
      the host's first publish). `PublishAll()` — host-only, writes every
      host-authoritative resolved value from `Plugin.Cfg` to the room's
      custom properties in one batched `SetCustomProperties` call; a no-op
      for anyone who isn't currently the master client.
    - `HostAuthoritySync.cs` — a tiny always-present
      `MonoBehaviourPunCallbacks` component (instantiated once,
      `DontDestroyOnLoad`, in `Plugin.Awake`) that calls `PublishAll()` on
      `OnJoinedRoom`/`OnMasterClientSwitched` — the two cases where *who's*
      authoritative changes without any config value itself changing (a late
      joiner, or a host migration after the previous host disconnects).
      Every other publish trigger (an actual config value changing) is wired
      directly: one config-file-wide `Config.SettingChanged` hook in
      `Plugin.cs`, plus a call in `RootsLevelWatcher` right after
      `CaptureLevelSnapshot`. Also handles `OnRoomPropertiesUpdate` on every
      non-host client: wind-force/gust-timing/item-force/occlusion tuning and
      spore-bomb trigger-radius shrink are each computed once (at scene load
      or on a local config change) and cached onto live fields, not re-read
      every frame, so a client whose own level load raced ahead of the host's
      first `PublishAll` would otherwise stay stuck on its local fallback
      value for the rest of that level; this re-runs
      `WindChillZoneTuningPatch.ReapplyAll()` /
      `SporeBombCullPatch.ReapplyTriggerRadiusToAll()` whenever a fresh
      room-property write actually lands, closing that race regardless of who
      wins it. The spore-bomb detonation tuning (knockback/VFX/screen-shake
      cap) doesn't need this - it already reads `Plugin.Cfg.Effective*` fresh
      at the moment of each detonation.
    - `ModPresenceCheck.cs` — tracks who in the lobby has Fairoots installed,
      backing the "every client needs Fairoots installed" requirement
      (ROADMAP.md's Host authority section): every client marks itself via a
      Photon player custom property (`Fairoots.Installed`) on `OnJoinedRoom`
      (already fully replicated to every other client by Photon itself, no
      extra networking needed). `GetMissingPlayers()` is the shared query
      both this file's own passive `Diag.Warn` logging (on
      join/player-entered/player-left, informational only) and
      `BoardingPassStartGatePatch` (the actual player-facing gate) both call.
    - `BoardingPassStartGatePatch.cs` — the actual enforcement point: a
      Harmony prefix on `BoardingPass.StartGame()` (confirmed via decompile -
      callable by *any* player, not just the host, since it just sends an RPC
      to whoever the MasterClient is - so this has to be checked client-side
      on whoever clicks it). A no-op (original runs immediately) if
      `ModPresenceCheck.GetMissingPlayers()` is empty; otherwise suppresses
      the click and shows `ModPresenceDialog.ShowStartConfirm` - Cancel
      leaves the Boarding Pass untouched, Confirm re-invokes `StartGame()` for
      real via a one-shot bypass flag (so the same click doesn't loop back
      into another confirmation).
    - `ModPresenceDialog.cs` — the actual popup: a minimal runtime-built uGUI
      overlay (dim background + panel + title + word-wrapped body + Cancel/
      Start-Anyway buttons), reusing the game's own font
      (`Resources.FindObjectsOfTypeAll<TMP_FontAsset>()`, same technique as
      peak-checkpoint-save's `SavePicker.FindGameFont`) rather than an
      existing native `MenuWindow` instance (e.g. the pause menu's own
      confirm dialog) — that only exists while the pause menu itself is open,
      and this needs to appear over the Boarding Pass screen instead. Never
      shows player names (would clip/bloat with several missing players) —
      those go to the log only.
    - `ModPresenceLocalization.cs` / `LocalizationHelper.cs` — the dialog's
      text, fully localized into all 14 languages the game ships with (per
      the maintainer's request) - "Fairoots" itself stays untranslated
      everywhere (a proper name, same as how peak-checkpoint-save leaves mod
      names alone). Mirrors peak-checkpoint-save's
      `MessagesLocalization`/`LocalizationHelper` convention exactly (a
      `Dictionary<Key, string[]>` indexed by `LocalizedText.Language`'s
      declaration order, falling back to index 0/English for any language a
      given entry's array doesn't cover - used here only for
      TraditionalChinese, matching that same convention since the game's own
      `LANGUAGE_COUNT` is 14, one less than the 15-value enum).
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
      range via `Core/SporeBombExplosionTuning.cs`, and records the detonation
      in `DetonationScreenshakeRegistry` for the shake patch below.
    - `DetonationScreenshakePatch.cs` + `DetonationScreenshakeRegistry.cs` —
      the other half of the screen-shake distance cap. `AddScreenshake` only
      honours its `range` when its `positional` flag is set; otherwise it calls
      the *global* `AddPerlinShake`, which shakes every player's camera at full
      strength regardless of distance, so setting `range` alone does nothing.
      The patch is a prefix on `AddScreenshake.Shake()` that forces positional +
      the configured range on any shake firing inside a recent detonation's
      space/time window (the registry — a fixed-size ring of recent detonation
      positions), which also catches the shakes on `ExplosionEffect`'s
      explosion orbs, since those are instantiated on a staggered coroutine
      *after* the spawn-time tuning pass has already run. Every shake outside
      that window (falls, rockfalls, items, creatures) is untouched.
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
    - `WorldUnits.cs` (+ the game-facing `GameUnits.cs` wrapper in the project
      root) — **read this before adding any `*-meters` setting.** PEAK's world
      units are not meters: the game keeps a static
      `CharacterStats.unitsToMeters` (1.6 in the current build) and multiplies
      by it everywhere it shows a player a distance or height. So a meters
      setting must be divided by that factor before it's compared against, or
      written into, anything positional (`Vector3.Distance`, a transform `y`,
      `AddScreenshake.range`), and a raw distance must be multiplied by it
      before being *logged* as meters. It's a 60% error, not a rounding one —
      it's what made a "75m" screen-shake cap actually reach 120m.
      `GameUnits` reads the live factor off the game; `WorldUnits` is the pure,
      tested arithmetic that takes it as a parameter.
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
      numbers internally, purely so a catalog lookup never throws (it's still
      called as an unused argument under Custom — see `OverrideResolution.cs`).
    - `Presets/OverrideResolution.cs` — preset-vs-Custom resolution: presets 1-4
      always use their own catalog numbers, ignoring the player's config
      entirely; Custom (5) always uses the player's configured value (0
      included). No sentinel/"unset" value to track.
    - `WindTuning.cs` — pure arithmetic for wind force/gust-duration scaling,
      non-backpack item-force scaling, and obstacle-occlusion raycast-distance
      scaling (not seed-gated, same reasoning as `SporeBombExplosionTuning`),
      plus the wind-preceded-fall camera-dampening decision
      (`IsWindForceStillRecent` + `ApplyFallCameraDampening`) — the
      maintainer's scoping call (ROADMAP.md "Open questions"): dampen only
      falls preceded by recent wind force, not every Roots fall. Deliberately
      does **not** include fog-density scaling — reverted as a precaution
      (the actual density/opacity relationship lives in shader code this mod
      has no way to decompile or verify, so scaling
      `FogConfig.windFogDensity`/`WindFogTextureDensity` from decompiled-C#-
      only assumptions isn't safe), even though it later turned out not to be
      the cause of a live-reported "screen turns solid black" bug — that was
      actually `ScaleWindActiveDuration` letting the scaled gust duration
      collapse to zero at a low force multiplier, which broke the *native*
      wind on/off timer and kept re-triggering the game's own (untouched)
      fog/storm-blend logic faster than it could decay. Fixed via
      `MinWindActiveDurationSeconds` — see the file's remarks and
      `ROADMAP.md`'s "Wind: fog-while-active density" row.
- **`Wind/`** — the Phase 5 Harmony patches (game-facing, calls into `Core/`
  for every number):
  - `WindChillZoneTuningPatch.cs` — on `WindChillZone.Awake`, captures each
    instance's vanilla field values (`windForce`, `windTimeRangeOn/Off`,
    `windItemFactor`, `minRaycastDistance`/`maxRaycastDistance`) once, keyed by
    instance ID, then scales and re-applies them from that cached baseline
    (never from the field's current, possibly-already-scaled value) — same
    pattern as `SporeBombCullPatch`'s trigger-radius baseline. `windForce`
    and `windTimeRangeOn/Off` are scaled by two *independent* multipliers
    (`force-multiplier` / `gust-duration-multiplier` — split 2026-07-22 at
    the maintainer's request, so push strength and gust timing/frequency can
    be tested separately instead of always moving together) even though
    presets 1-4 still use the same number for both, matching the original
    combined ROADMAP.md row. `ReapplyAll()` re-applies to every loaded
    instance on a live config change. **Note:** `windForce` and
    `windItemFactor` multiply together in the *native* `AddWindForceToItem`
    formula — if `force-multiplier` is at/near 0, items get zero force
    regardless of `item-force-multiplier`, which looked like a scaling bug
    when live-tested (2026-07-22) but is actually how vanilla's own formula
    always worked; not something this mod can or should decouple further,
    since force genuinely needs to be a shared base magnitude for both
    characters and items. A second patch in the same file,
    `WindBackpackImmunityPatch`, prefixes `AddWindForceToItem` to skip
    `Backpack` instances entirely — `windItemFactor` alone can't single out
    one item type since it applies to every ground item alike. On by default
    on every preset, but now player-toggleable via the flat (non-preset-gated)
    `Wind/backpack-always-immune` setting requested 2026-07-22.
  - **`Wind/disable-wind-entirely`** (flat, non-preset-gated, off by default):
    a master kill switch meaning "wind never happens at all," not just
    "vanilla-strength wind" (clarified 2026-07-22 — an earlier version only
    reverted the scaling patches to vanilla numbers, which still let vanilla
    gusts occur). Two parts: `WindToggleSuppressionPatch` (in the same file
    as `WindChillZoneTuningPatch`) prefixes `WindChillZone.RPCA_ToggleWind`
    and forces its incoming `set` parameter to `false` whenever the switch is
    on, so this client's own zone instance can never go active again —
    purely client-side (RPCA_ToggleWind is a Photon RPC driven by the host's
    randomized storm timer, but suppressing it locally doesn't need host
    cooperation or touch other clients, matching this mod's usual
    client-side-only architecture); and `WindChillZoneTuningPatch.Apply`
    additionally forces `windActive = false` immediately, for a gust already
    in progress the instant the switch is flipped. `WindBackpackImmunityPatch.Prefix`
    and both patches in `WindFallCameraDampingPatch.cs` also check the switch
    and no-op entirely while it's on. Wired to reapply immediately in
    `Plugin.cs`, bypassing `ApplyChangesLive` (same treatment as
    `KeepVanillaTriggerRadius`).
  - `WindFallCameraDampingPatch.cs` — two cooperating patches, both scoped to
    `Character.localCharacter` only (a camera-feel effect, not networked
    physics): `WindRecentForceTrackerPatch` records a timestamp on
    `WindChillZone.AddWindForceToCharacter`'s postfix (re-deriving the
    original method's own early-return checks, since a postfix always fires
    even when the original bailed out without applying force); the actual
    dampening patches `CharacterData.GetTargetRagdollControll()` (the method
    RESEARCH.md Q6 traced as the source of the "0 the instant any fall
    starts" camera-spin mechanism), raising its floor only when the fall is
    within the configured recency window of a real wind-force application.
  - **Note (runtime-confirmed, no patch needed):** the ROADMAP "climb to
    counter wind" mechanic already exists natively —
    `WindChillZone.AddWindForceToCharacter` returns immediately whenever
    `character.data.currentClimbHandle != null` (actively gripping a climb
    handle), and `ApplyStatus` already raises climbing stamina cost during
    wind regardless. See `Core/Presets/PresetCatalog.cs`'s
    `ClimbToCounterWind` remarks.
- `tests/Fairoots.Tests/` — xUnit project. Links `src/Fairoots/Core/**/*.cs`
  directly (no game/BepInEx dependency, runs anywhere). One test file per Core
  area; see `docs/TESTING.md` for what each covers.
- `packaging/` — Thunderstore/Nexus packaging pipeline (`build-release.sh`,
  `gen-readme.sh`, `manifest.json`, `CHANGELOG.md`), same pattern as the
  other two PEAK mods in this GitHub account.
- `docs/TESTING.md` — automated-test coverage summary + manual in-game loop.

## Planned structure (fills in as phases land — see ROADMAP.md)

`SporeBombs/` and `Wind/` (above) are the first two mechanic-group folders;
expect one more per remaining mechanic group, mirroring `OVERVIEW.md`'s
sections: `SporeAreas/`, `Creatures/` — each holding the Harmony patches that
scan the scene and apply removals/tweaks, delegating every seeded decision to
`Core/`. New per-mechanic preset values land in `Core/Presets/PresetCatalog.cs`
as each phase is implemented.
