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
  - `PluginConfig.cs` — config binding. Sections in bind (and file) order:
    `General` — the `seed` field, the `preset` 1-5 selector (1-4 are the fixed
    presets, 5 is Custom — see `Core/Presets/PresetId.cs`), and
    `recolor-spore-bombs` (the one client-side setting, see below); then the
    per-mechanic Custom-only sections (sane numeric defaults, only read when
    preset is set to Custom — ignored under presets 1-4 even if changed); then
    `Debug`, bound last, which also holds `apply-changes-live` (see below —
    it's in `Debug` because freezing values mid-run is a comparison-testing
    tool, and like `keep-vanilla-trigger-radius` it's a behavior override that
    works regardless of the debug-logging master switch).
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
    `WindRecentForceWindowSeconds`/`Debug` section entries — plus
    `recolor-spore-bombs`, which has no `Effective*` accessor at all: it's
    purely cosmetic (what one player sees on their own screen), so there's
    neither a host lookup nor a level-load snapshot to apply, and game-facing
    code reads `Plugin.Cfg.RecolorSporeBombs.Value` directly.
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
    - `MaterialProbe.cs` — dumps the real material/shader setup of everything
      within ~5m of the player (hotkey, default F11): every color slot each
      shader declares, its value, and whether Fairoots is currently overriding
      it via a property block. Exists because shaders are *assets, not code* —
      nothing in the decompiled C# says what a prop's albedo slot is called, and
      PEAK's stylized prop shaders carry several color slots at once, so the
      spore-bomb recolor's first two versions guessed and recolored shading
      bands and crevices instead of surfaces. Also answers "did the mod do this
      to that object, or does it just look like that?" outright, per property.
      Targets whatever the player is **looking at** (ray vs. renderer bounds,
      not a physics raycast — the meshes that matter here are often
      colliderless, and a spore bomb's only collider is its invisible trigger
      volume), excluding the local player's own body, which otherwise sits at
      0.00m and crowds out the entire report.
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
    - `SporeBombRecolorPatch.cs` — the visual-readability fix: tints every spore
      bomb (both the mushroom-cluster variants and the explosive one) toward the
      pink/magenta of the game's own Spores status effect, so a green hazard
      stops camouflaging into green grass and green ground. Not actually a Harmony
      patch despite the folder's naming convention — the scene objects are just
      already there, so it's driven the same way the trigger-radius shrink is
      (once per level from `SporeBombCullPatch.Run`, plus a scene-wide
      `ReapplyToAll()` on the setting changing). Reads the target hue live off
      `CharacterAfflictions.colorSpores` (the same field the game pulses the
      player with) and gets the recolored value from `Core/SporeBombRecolor.cs`.
      Recolors **every** `Color`-typed property the shader declares (minus a
      short exclusion list: specular/rim/emission and the character-only
      status/skin slots), enumerated live off the shader rather than guessed by
      name — `W/Peak_Standard` drives its stylized look from several color slots
      at once, and recoloring a subset desynchronizes them into pink veins over
      an otherwise-green mushroom, which is exactly what the first two versions
      shipped. Uniformity requires all of them or none. Writes through a `MaterialPropertyBlock`, never
      `Renderer.material` — with 400+ spore bombs per level the latter would mean
      400+ material instantiations and a broken batch each — and caches every
      renderer/submaterial slot's vanilla color the first time it's seen so
      toggling the setting off restores the true original, the same
      baseline-caching pattern as `SporeBombCullPatch`'s vanilla trigger radii.
      **The only client-side, non-host-authoritative gameplay-adjacent feature in
      the mod** (it's cosmetic — see `PluginConfig.cs` above).
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
    - `SporeBombRecolor.cs` — the pure color math behind the spore-bomb recolor
      (plus `Rgb`, a Unity-free color triple so this can live in `Core/` at
      all, and HSV conversion): replaces each material color's **hue** with the
      Spores status color's, blends saturation toward it, then rescales the
      result onto the original's **Rec. 709 luminance** so the artist's
      per-slot lightness differences — which is what reads as shading — survive
      instead of flattening. Luminance, not HSV value: value is just the
      largest channel, while perceived brightness is ~72% green, so an
      equal-value magenta loses about half a green's brightness. That's not
      theoretical — the first hue-replacement build shipped with it and came
      back from in-game testing as a near-black maroon lump. Not seed-gated (every spore bomb gets the identical flat
      treatment), same as `SporeBombExplosionTuning.cs`.

      **Why hue replacement and not a multiplicative tint** (the first version
      tried that, see git history): runtime probing showed PEAK's props use a
      `W/Peak_Standard` shader whose color slots hold genuine authored colors
      — the regular spore bomb's is `(0.24, 0.406, 0.109)` green, the explosive
      one's `(0.717, 0.252, 0)` orange — not a neutral white multiplier over a
      texture. That matters beyond convenience: **multiplication can never add
      a channel that isn't already there**, and the explosive variant has zero
      blue, so no gain could make it magenta — only pure red. Pure red against
      green foliage is exactly the pair a red-green colorblind player cannot
      separate, which would defeat the feature. Adopting the hue outright puts
      real blue in the result, and blue is the channel red-green colorblindness
      leaves intact.
    - `ClimbWindResistance.cs` — the pure cost model behind the
      climb-to-shelter-from-wind mechanic (`Wind/ClimbWindShelterPatch.cs`
      above): scales a climb step, decomposed onto the climbed surface's own
      plane, by a base multiplier plus extra penalties for climbing upward and
      for climbing into the wind — each faded in by live wind pressure, so a
      climber the wind can't reach pays nothing. Also owns the
      pressure-freshness rule and the let-go grace window's force curve
      (`GraceForceMultiplier` — hold, then ramp back to full, never zero).
      Not seed-gated, same as `WindTuning`.
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
  - `ClimbWindShelterPatch.cs` — the climb-to-shelter-from-wind mechanic
    (ROADMAP.md's "New: climb-to-counter-wind" row). **Corrects an earlier
    claim in this file that the mechanic already existed natively and needed
    no patch:** `WindChillZone.AddWindForceToCharacter`'s early return only
    covers `currentClimbHandle != null` (hanging off a climb *handle* prop),
    not `CharacterData.isClimbing` (ordinary wall climbing), rope climbing or
    vine climbing — all of which take full wind force in vanilla, and being
    shoved mid-climb drops the climb entirely (`CharacterClimbing.Update`
    lets go below 0.25 ragdoll control). Four patches:
    `ClimbWindShelterPatch` (prefix on `AddWindForceToCharacter`) suppresses
    the push outright while the player holds onto anything and records how
    hard it *would* have pushed as a 0-1 "pressure";
    `ClimbWindWallSlowdownPatch` (postfix on
    `CharacterClimbing.GetRequestedPostition` — the game's own misspelling)
    charges for that immunity by scaling the climb step, split onto the
    climbed surface's plane so upward and into-the-wind movement can cost
    more than the rest; `ClimbWindRopeSlowdownPatch`/`ClimbWindVineSlowdownPatch`
    do the same flatly for rope/vine climbing by *temporarily* scaling
    `climbSpeedMod` around the native method and restoring it after (never
    writing a computed value into it — the game's own climbing-speed
    affliction adjusts that field additively and a write would clobber it).
    The same prefix also runs the **let-go grace window**: for a short window
    after the local player releases a climb (flat
    `Wind/climb-shelter-grace-seconds`, 0.5s), the original method *does* run
    but with `windForce` temporarily scaled down around it and restored in the
    postfix — never written with a computed value, since
    `WindChillZoneTuningPatch` owns that field's real value. Fixes the
    catapult on finishing a climb mid-gust (the game's own
    `StopClimbingRpc` sets `sinceGrounded` to a fake fall time, so full wind
    force lands the instant the player has least control). Reduced, not
    immune, so wall-tapping can't be used to cross exposed ground for free.
    Preset-gated as of 2026-07-27: **off entirely on Subtle** (folded into
    `EffectiveClimbSheltersFromWind`, so the player-facing
    `Wind/climb-shelters-from-wind` toggle can switch it off elsewhere but
    can't switch it on under Subtle).
    Pressure is recorded for the local character only and expires after
    `ClimbWindResistance.PressureFreshnessSeconds`; a stale reading is what
    "the gust ended" looks like, since nothing fires an event for it. All the
    arithmetic is in `Core/ClimbWindResistance.cs`. `WindRecentForceTrackerPatch`
    also re-checks the shelter, so a fall right after letting go of a wall
    isn't misattributed to wind.
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
