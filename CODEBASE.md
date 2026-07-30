# CODEBASE — where things live in `src/`

A brief map of `src/Fairoots/` (what each file/folder is responsible for), the
same way `peak-sense-of-direction/CODEBASE.md` and `peak-checkpoint-save`'s
structure do, so a reader can find where a given mechanic lives without
re-scanning the whole tree.

**Phase 2 (seed/preset core), Phase 4 (spore bombs), Phase 5 (wind), Phase 6
(spore areas) and Phase 7 (creatures) are in.** Phase 8 (achievement spawn-rate
nudges) and Phase 9 (preset tuning pass, packaging) are not started; see
`ROADMAP.md`'s phased plan.

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
    per-mechanic Custom-only sections (**every default is the vanilla value** —
    only read when preset is set to Custom, ignored under presets 1-4 even if
    changed); then
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
    client. **As of 2026-07-30 that is every single one, with no exceptions**
    (maintainer's call): the rule is now "everything outside `General` and
    `Debug` is host-authoritative," which retired the last two carve-outs —
    `EffectiveWindFallCameraDampenClamp` and the flat
    `WindRecentForceWindowSeconds`, both previously per-client as
    camera-feel/accessibility settings. What has no host lookup is what has no
    `Effective*` accessor at all: the `Debug` section, and `General`'s cosmetic
    entries — the two warning-label toggles, the cloud-opacity pair, the
    cover-mouth keybind, and `recolor-spore-bombs`, which is
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
      value for the rest of that level; this re-runs every reapply pass whose
      state is cached rather than re-read — `WindChillZoneTuningPatch` /
      `SporeBombCullPatch.ReapplyTriggerRadiusToAll` / `SporeAreaDisablePatch` /
      `SporeAreaTuningPatch` / `SporeDecayPatch`, plus the four creature passes
      (`CreatureDisablePatch`, `CreatureSpeedPatch`, `CreatureKnockbackPatch`,
      `CreatureRagdollPatch`) — whenever a fresh room-property write actually
      lands, closing that race regardless of who wins it. **The creature passes
      were missing here until 2026-07-30** and that was a real, live-reported
      co-op bug, not just a race: hiding a beetle or a spider is a one-shot
      `SetActive` on each client's own scene objects with nothing that re-reads
      the setting per frame, so a host turning creatures off never hid them for
      anybody else. The spore-bomb detonation tuning (knockback/VFX/screen-shake
      cap) genuinely doesn't need this - it already reads `Plugin.Cfg.Effective*`
      fresh at the moment of each detonation.
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
  - `SporePresence.cs` — the one answer to "is the local player standing in
    spores right now?", across both hazards (persistent areas and bomb clouds).
    Shared by `SporeBombCloudWarning` and `Ui/SporeWarningLabel` for the same
    reason `SporeAreaScan` is one copy: two slightly different answers would mean
    the overlay and the label disagreeing about whether the player is in danger.
    The spore-area list is **captured per level** by `RootsLevelWatcher` rather
    than searched per frame — its callers ask every frame, and a per-frame
    `FindObjectsOfType` is the unconditional full-scene sweep that already cost
    this mod a mod-wide framerate drop once. Areas the removal/disable passes
    deactivate later stay in the list and are skipped by an `isActiveAndEnabled`
    check, so nothing has to invalidate it. Honors the game's own
    `emitterDisabledByWind`, which means wind dispersal *and* a covered mouth both
    correctly stop a warning claiming the player is being spored when they aren't.
  - **`Ui/`** — Fairoots' own on-screen elements (game-facing, TMP/uGUI):
    - `NativeUiAssets.cs` — finds the game's UI font and outlined text material at
      runtime by scanning live `TextMeshProUGUI` for the material name
      `DarumaDropOne-Regular SDF Outline`, taking that label's font with it (which
      also guarantees the two match). They have to be discovered rather than
      loaded by name: a font and a material are Unity *assets*, so nothing in the
      decompile references them. Same technique and same key as
      `peak-sense-of-direction`'s `Labels/NativeAssets`. Retried until it succeeds,
      since no native label necessarily exists yet at plugin load.
    - `RootsLoadingOverlay.cs` — the dimmed "preparing the Roots..." screen shown
      while `RootsLevelWatcher` runs its per-level passes. Deliberately minimal (a
      full-screen dim plus one centred line, no procedural sprites), copied in
      shape and dim colour from `peak-checkpoint-save`'s save-picker first-open
      loading indicator so the two mods' loading beats read as one thing. Its
      sorting order sits **below** that mod's own loading screen: during a Quick
      Resume into a Roots campfire both are up at once, and the right thing to
      show is that mod's "LOADING SAVE...", not two stacked overlays.
    - `SporeWarningLabel.cs` — the opt-in `General/show-spore-cloud-label` text
      warning, centred between the top of the screen and the crosshair. Says in
      words what the green overlay says in colour, because a colored tint is
      exactly the signal that competes with a colored cloud while text competes
      with nothing in the scene. Text color is the live Spores status color (the
      same field `SporeBombRecolorPatch` reads, so label, status and recolored
      hazards agree by construction) over a darkened-but-same-hue outline from
      `Core/LabelColors`. The outline is written into an **instanced** copy of the
      native material — writing the shared asset would repaint the outline of
      every native label in the game. Off by default: unlike the rest of the
      readability group it adds a HUD element PEAK never had, rather than making
      the game's own feedback legible.
    - `SpiderWarningLabel.cs` — the opt-in `General/show-spider-warning-label`
      text warning shown while a spider is dropping on the local player. A
      deliberate copy of `SporeWarningLabel`'s look and placement (60px lower, so
      both can be on screen at once) so the two read as one HUD language. Colored
      from the live **Poison** status color, poison being what a spider does to
      you — same "the label matches the status it warns about" rule as the spore
      label's `colorSpores`; there is no `colorWeb` in the build. Presence comes
      from `Creatures/SpiderStrikeWarning`, never a scene scan.
    - `WarningLabelLocalization.cs` — both labels' text in all 14 languages the
      game ships with (added 2026-07-30, live-reported as the one player-visible
      string in the mod that ignored the game's language setting). Same
      `Dictionary<Key, string[]>`-indexed-by-`LocalizedText.Language` shape, and
      the same English/TraditionalChinese fallback convention, as
      `Networking/ModPresenceLocalization` — it reuses that folder's
      `LocalizationHelper` outright rather than growing a second copy. Both labels
      re-resolve their text whenever `LocalizedText.CURRENT_LANGUAGE` changes
      (they build their `TextMeshProUGUI` once and then only tick colour/alpha, and
      the language can change mid-session with no scene reload), but only then —
      assigning `.text` forces a mesh rebuild.
  - `ParticleOpacity.cs` — the shared "thin out this VFX" applier behind both
    spore-cloud translucency settings (`SporeAreas/SporeCloudOpacityPatch` and
    `SporeBombs/SporeBombCloudOpacity`), game-facing so deliberately outside
    `Core/` — same root-level placement, and same reason, as `GameUnits.cs`.
    **There is no single lever that thins every particle system, so it picks one
    per system:** if the shader declares an opacity float of its own
    (`_Opacity`/`_Alpha`/`_Transparency`, matched exactly — the same materials
    also declare `_AlphaClip`/`_AlphaCutoff`/`_AlphaRemap`/`_ClampAlpha`, none of
    which are opacity), that's scaled through a per-renderer
    `MaterialPropertyBlock`; otherwise it falls back to scaling the alpha of
    `ParticleSystem.main.startColor`, the per-particle vertex color every stock
    particle shader multiplies in. Exactly one path is ever live on a system and
    the other is actively restored, since a shader honoring both would dim twice
    and land at the *square* of the requested opacity. The split is not
    hypothetical: the first version was vertex-alpha only, which thinned a spore
    bomb's cloud perfectly and did nothing at all to a spore area's at any value
    down to zero (live-confirmed 2026-07-28) — the areas' clouds are drawn by
    custom Shader Graph shaders (`SmokeParticle` / `GD/FireParticle`) that never
    wired up a vertex-color node but do expose `_Opacity`. Properties are
    enumerated off the **`Shader`**, never the material's serialized values: a
    Unity material keeps stale entries from every shader it has ever been
    assigned (these two carry a dozen URP Lit leftovers), so "the material has a
    float called `_Opacity`" doesn't mean the shader reads one. Property blocks
    rather than `Renderer.material` because these materials are shared across
    every cloud in the level — the same allocation-per-object problem
    `SporeBombRecolorPatch` avoids. Handles all five `MinMaxGradient` modes
    explicitly (the struct carries a different pair of its fields per mode and
    reading the wrong one returns garbage rather than throwing), baseline-caches
    both an authored `startColor` and an authored opacity float (same pattern as
    `SporeAreaTuningPatch`), and always builds a scaled *copy* of a gradient
    rather than mutating the cached one, which is what keeps a multiplier of 1.0
    an exact restore. Also owns the one-time verbose per-material inventory
    (shader, render mode, every declared property, which one is the lever) — the
    line that answers "why didn't this cloud thin?" instead of the next guess.
  - `RootsState.cs` — **the mod's on/off switch, and the first thing to read
    before adding any game-facing code.** `RootsState.Active` is true only while a
    Roots Segment is loaded, and every patch in the mod checks it. This is not
    decoration: most of what Fairoots patches is *not* Roots-specific code —
    `WindChillZone` drives wind on the whole mountain, `CharacterAfflictions` is
    the player's own component and follows them into every biome,
    `CharacterClimbing`/`CharacterRopeHandling`/`CharacterVineClimbing` run
    wherever the player climbs, and `Mob`/`MushroomZombie` are shared creature
    base classes. Two shapes of gate, and which one a patch needs depends on
    whether it writes to a native field: a patch that *decides* something reads
    `if (!RootsState.Active) return <passthrough>`, while a patch that *writes* a
    field takes its cached-vanilla-baseline restore branch instead — that's what
    makes leaving Roots hand the game back rather than just stop re-applying.
    Same file also holds `FairootsInterop`, the mod's only public API: a
    reflection-friendly `ShouldHoldLoadingScreen()` that `peak-checkpoint-save`
    polls so its Quick Resume loading screen stays up until the setup below is
    done (see that repo's `FairootsCompat`). It answers true while the work is
    *pending* as well as running, so a caller can't lose the race by asking
    before the watcher's poll has noticed the biome.
  - `RootsLevelWatcher.cs` — decides when the mod is awake: asks `MapHandler` once
    a frame whether the player is in a live Roots biome, opens the `RootsState` gate,
    and runs the per-level passes; on the way out it closes the gate, drives one
    restore pass over everything that can outlive the biome, and drops the
    per-level registries. **It asks the game rather than searching the scene** —
    earlier versions polled `GameObject.Find("Roots Segment")` twice a second, a
    linear search over every active object in every loaded scene, paid forever in
    every biome to answer something `MapHandler` already tracks. Two consequences
    beyond the cost: the check is now an array index and a couple of field reads, so
    it runs every frame and is *more* responsive than the poll was; and it can no
    longer be fooled by PEAK's main menu, which runs a Roots biome as its animated
    background (live-reported 2026-07-30 — the old search found that perfectly real
    Roots Segment behind the menu and culled it). The passes run **behind `Ui/RootsLoadingOverlay`, one
    per frame**: they used to all fire inside the single `Update()` tick that
    detected the segment, which is a long main-thread stall (live-reported as a
    huge stutter as the biome loads in after lighting the campfire). The work
    can't be deferred into gameplay — the player must not reach a spore bomb the
    cull is about to remove — so what changed is the presentation: the overlay is
    given a frame to actually render before the first heavy pass starts. Pass
    order is load-bearing in three places; see the `SetupSteps` remarks.
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
    - `SporeStatusSourcePatch.cs` — logs any Spores application that lands on the
      local player *while their mouth is covered*, with the call stack that asked
      for it. That combination should be impossible, so every line is a bug report.
      Earned its keep immediately: it's what revealed a bomb's cloud to be a
      timer-driven repeating `AOE`, after both plausible readings of the decompile
      turned out to be wrong. Same approach and rationale as
      `ScreenshakeSourcePatch`.
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
      in `DetonationScreenshakeRegistry` for the shake patch below. Also resizes
      the visible cloud to match the spore-area radius it just gave the AOEs
      (`ScaleCloudVfx`, reusing `Core/SporeAreaTuning.ScaleVisual` — same
      what-you-see-is-what-can-hurt-you requirement as the persistent areas).
      Only the **outermost** particle systems are scaled: unlike a spore area's
      two sibling systems, a detonation has one on the spawned root *and* more on
      children, so scaling every system found would apply the multiplier twice to
      the nested ones. No baseline caching, unlike the spore-area version — this
      runs once against a freshly instantiated object, so there's nothing to
      compound with and nothing outliving the cloud to restore.
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
    - `CoverMouthSporeBombPatch.cs` + `SporeBombDetonationMarker.cs` — the opt-in
      `Spore-Bombs/cover-mouth-blocks-spore-bombs` setting (off by default,
      host-authoritative): covering your mouth also blocks the spore payload of a
      spore bomb's temporary cloud. Only the *status* is suppressed — knockback,
      noise and shake still land. A bomb's cloud is not a `StatusEmitter`, so the
      spore-area immunity can't reach it: it's **one `AOE` that re-explodes on a
      timer** (`TimeEvent` invoking `Explode` repeatedly — established from a live
      call stack, not the decompile), so the patch zeroes `statusAmount` *and*
      `hasAffliction` around each `Explode` call and always restores them. Scoping
      uses `SporeBombDetonationMarker`, a tag put on the spawned explosion, rather
      than `DetonationScreenshakeRegistry` — read that file's remarks before
      changing it: the registry expires after seconds (it's for screen shakes), so
      the registry-based version blocked a cloud's first few seconds and silently
      let the rest through.
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
    - `SporeBombCloudWarning.cs` — `General/show-overlay-in-spore-bomb-clouds`:
      holds the game's own "you are standing in spores" screen overlay up while the
      local player is inside a spore bomb's cloud. Fills a genuine vanilla gap
      rather than adding an effect — the game has exactly one such warning
      (`GUIManager.sporesWarning`) and only `StatusEmitter` ever raises it, so a
      bomb's cloud (an `AOE`, not an emitter) gives the per-tick damage flash and
      nothing in between. Raises the native warning through the native
      `StartFX`/`EndFX`, inheriting its look, fade timing and photosensitivity
      handling for free (same reuse-the-game's-own-mechanism approach as
      `CoverMouthImmunityPatch`); the damage flash is a separate overlay layer and
      is left untouched, so it still spikes on top. Presence is judged by
      `Core/SporeBombCloudPresence`'s port of the native falloff rule, **not** by
      `AOE.range` — the radius that actually applies status is meaningfully smaller
      than the advertised one. Live clouds are a registry populated at detonation
      (`SporeBombExplosionPatch`), never a per-frame `FindObjectsOfType` — that
      kind of unconditional sweep already cost this mod a mod-wide framerate drop
      once. Two coordination rules with the spore areas that share the one overlay:
      it only ever ends a warning it started, and it neither raises nor lowers the
      overlay while a spore area is also in range (raising would reset the tween to
      0 and read as a dip; lowering would blank the area's warning with nothing to
      re-raise it, since the emitter only calls `StartFX` on the entry frame).
    - `SporeBombCloudOpacity.cs` — the spore-bomb half of the cloud-translucency
      readability fix (`General/spore-bomb-cloud-opacity`): a component attached
      to the spawned explosion by `SporeBombExplosionPatch`, re-applying
      `ParticleOpacity` on a 0.25s interval for the object's lifetime rather than
      once at spawn. A detonation isn't a single instant — its `AOE` re-explodes
      on a timer and `ExplosionEffect` keeps instantiating VFX on a staggered
      coroutine — so anything created after a one-shot spawn-time pass would come
      out at full vanilla density; polling ends by itself when the object is
      destroyed. Also owns the one-time verbose structure dump of a live
      detonation (same purpose as `SporeAreaTuningPatch`'s: the explosion is a
      prefab, i.e. an asset, so the decompile can't say what a bomb's cloud is
      built from).
  - **`SporeAreas/`** — the Phase 6 spore-area mechanics (the persistent
    "Mushroom Spore Clouds", a different hazard from spore bombs):
    - `SporeAreaDisablePatch.cs` — the `Spore-Areas/disable-spore-areas` master
      switch. Not a Harmony patch despite the folder convention (same as
      `SporeBombRecolorPatch`): the emitters are baked into the Roots scene at
      author time, so there's no runtime placement call to hook — driven once
      per level from `RootsLevelWatcher`, plus a scene-wide `ReapplyToAll()` on
      the setting changing or a host room-property update. Identity is the
      **component**, not a name: a `StatusEmitter` with `statusType == Spores`
      and `amount > 0` (runtime-confirmed - 12-23 per level in Roots, all
      `WindAffectedStatusEmitter`, `radius=16`, `innerFade=8`, `amount=0.025`;
      there is no spore-area class or prefab name to match on, and `amount > 0`
      excludes the mirror-image "subtracts spores" use of the same component).
      Deactivates the whole area object rather than just the component, so the
      emitter mushroom and cloud VFX go with the status ticks and screen
      filter; `ResolveAreaRoot` walks *up* from the emitter to the highest
      ancestor that still represents that one area (stopping before any
      ancestor owning a second `StatusEmitter`, any
      `PropSpawner`/`PropGrouper`/`Biome` grouping node, and `Roots Segment`) —
      confirmed in-game to land on `"Mushroom tree Spore Cloud"`, which is the
      **whole mushroom-tree prop** (mushroom meshes + `MeshCollider`s as direct
      children, plus a `"Spore Cloud"` child carrying the emitter and two
      `"Particles"` systems — see `SporeAreaScan.ResolveAreaRoot`'s remarks for
      the confirmed layout). So the emitter mushroom and its collision geometry
      go with the hazard, which is the maintainer's confirmed intent, not an
      overreach. Restores only what it
      hid itself (a registry keyed by instance ID), so turning the setting off
      can't un-hide something the game deactivated for its own reasons.
      Deliberately excludes a spore bomb's own temporary spore area
      (`IsSporeBombSpawned`) — that's the `Spore-Bombs` section's business.
    - `SporeAreaScan.cs` — the shared "what is a spore area, and which
      GameObject *is* it" logic (identity check, spore-bomb-spawned exclusion,
      `ResolveAreaRoot`'s parent walk, path formatting) that every mechanic in
      this folder calls. One copy on purpose: two slightly different identity
      checks would mean the removal pass and the disable pass disagreeing about
      how many spore areas the level has.
    - `SporeAreaTuningPatch.cs` — the flat, non-seeded field tuning applied to
      every spore area: size (`radius` + `innerFade`/`outerFade` + the two cloud
      particle systems' transforms) and Spores build-up rate (`amount`), both in
      one pass since they act on the same components. Baseline-cached per
      instance ID (same pattern as `WindChillZoneTuningPatch`), so repeated
      reapplies can't compound and 1.0 always restores true vanilla.
      Live-updatable, unlike removal. Also owns the one-time verbose structure
      dump that established the prefab layout above.
    - `SporeCloudOpacityPatch.cs` — the spore-area half of the cloud-translucency
      readability fix (`General/spore-area-cloud-opacity`): thins the cloud VFX so
      the game's own Spores screen overlay is readable through it — in vanilla the
      cloud and the overlay are the same color, so "next to a cloud" and "inside
      one, taking spores" look nearly identical. Not a Harmony patch (same reason
      as `SporeAreaDisablePatch`): driven once per level from `RootsLevelWatcher`
      plus a scene-wide `ReapplyToAll()` on the setting changing. Deliberately
      **not** folded into `SporeAreaTuningPatch` despite walking the same particle
      systems — that one applies host-authoritative gameplay values gated on
      `apply-changes-live`, this one is per-client cosmetics that must apply
      immediately in both directions (the `recolor-spore-bombs` treatment). They
      write different properties (transform scale vs. particle start color), so
      they compose. The hazard volume is untouched on purpose: a cloud that
      *looked* smaller than it is would be worse than an opaque one.
    - `CoverMouthController.cs` — the cover-your-mouth mechanic's driver (polled
      from `Plugin.Update`): reads the key, runs `Core/CoverMouth.NextState`,
      charges stamina, empties the player's hands as the cover starts (a slot item
      is *pocketed*, the temporary fourth held item is *dropped* via the game's own
      `DropItemRpc`), and publishes the state as a Photon **player** custom
      property so other clients can pose it. Per-client by design — each player
      decides about their own mouth, and the immunity only ever affects them, since
      spore areas apply status to `Character.localCharacter` only. Refuses to start
      while the player is holding onto anything: the restriction patches stop a
      covering player from starting a climb, and this stops a climbing player from
      covering, so a defensive button can never turn into a fall.
    - `CoverMouthImmunityPatch.cs` — what covering buys, via the game's **own**
      gate: `StatusEmitter.emitterDisabledByWind`, the flag wind already uses to
      disperse spore areas. Reusing it means the mechanic ends the green screen
      filter and suppresses the status exactly the way dispersal already does,
      instead of reimplementing both. Wind-affected emitters (all of them, in
      Roots) are handled by ORing the flag in on their own `FixedUpdate` postfix,
      where the game rewrites it every tick so no restore bookkeeping is needed;
      plain emitters are tracked and restored explicitly.
      **Plus the anti-exploit half:** progress toward the next spore tick is
      *paused* by covering, never reset, so tapping the key buys exactly the time
      it was held. Read that field's remarks before touching it — the leak is in
      vanilla's own re-entry path (`timeSinceLastTick = -extraWarningTime`, a fresh
      1.5s grace every time the emitter thinks you re-entered), and the first fix
      for it failed on a one-frame ordering detail also documented there.
    - `CoverMouthPosePatch.cs` — the visible half: both hands over the mouth, on
      every client. Three systems, each doing the part it can: **hand/finger
      shape** is captured from an existing emote clip (finger curl is
      unreachable from a Harmony mod — it's animation data, and arm IK solves
      only three bones); **the clip is never left playing**, its wrist/finger
      rotations are captured once and re-applied per frame, so the emote's legs
      and head motion never happen and there's no animation state to reset; and
      **arm IK** places the hands via `HandleIK` (weights) / `ConfigureIK`
      (targets). Neither method is gated to the local character, so remote
      players' poses come free off the replicated player property — no animation
      networking. Three findings worth not rediscovering, all documented at
      their fields: the clip is `A_Scout_Emote_Defeat` (the emote *labelled*
      "it's so over" — the wheel shows `LocalizedText.GetText(key)`, so keys
      don't resemble labels, and only the `PlayEmote` probe could resolve it);
      the wrist must be stored **body-relative and applied via the IK target**,
      not stored forearm-relative and written to the bone (that flips the hands
      and starts a solver-feedback oscillation — "twitching"); and the capture
      must be **synchronous with other layers muted** (`Animator.Update(0)`),
      or it silently captures the clip blended with the session's idle state and
      the six other weight-1 layers, making the pose differ per session.
    - `CoverMouthRestrictionPatches.cs` — what covering costs besides stamina: no
      interaction (`Interaction.canInteract`, the single gate every interaction
      passes through — which is also how rope/vine/climb-handle grabs are covered,
      since those are interactibles rather than a separate climb input), no
      slot/backpack switching (`CharacterItems.DoSwitching`), no starting a wall
      climb (`CharacterClimbing.TryToStartWallClimb`, the one climb that isn't an
      interaction). All scoped to the local character and no-ops when not covering.
    - `SporeAreaCullPatch.cs` — the seeded thinning pass
      (`Spore-Areas/removal-fraction`): deactivates a fraction of the level's
      spore areas, with `Core/SporeAreaCull.cs` deciding which. Level-load-only
      (like the spore-bomb cull fraction — a removed area can't come back
      mid-level, so nothing is wired to `SettingChanged`), and runs **before**
      `SporeAreaDisablePatch` each load so the two compose. Verbose logging
      reports each removal's nearest-neighbour spacing plus a
      removed-vs-kept median comparison, which is what makes the cluster-first
      claim checkable against a real level instead of taken on faith (the
      per-removal lines alone are in scene order, so they can't show it).
  - **`Spores/`** — the two dials that act on the Spores **status** rather than on
    any one hazard that applies it. Everything in `SporeBombs/` and `SporeAreas/`
    tunes a thing that gives you spores; this folder tunes having them. Both
    settings live in the `Spores` config section and are host-authoritative.
    - `SporeDecayPatch.cs` — `Spores/clear-time-multiplier`: how long spores take
      to wear off. Scales **two** native fields together and in opposite
      directions (`CharacterAfflictions.sporesReductionPerSecond` divided,
      `sporesReductionCooldown` multiplied), because native recovery is
      `cooldown + status / rate` and only that pairing turns the setting into a
      clean multiplier on the whole wait — see `Core/SporeStatusTuning.cs`. Field
      scaling with a per-instance baseline cache (the `WindChillZoneTuningPatch`
      pattern) rather than a transpiler on the private `UpdateNormalStatuses`,
      which handles every status type in one method. **Its baseline cache is
      deliberately never cleared on level teardown**, unlike the creature
      patches': a `Character` outlives a biome segment, so dropping a live
      character's baseline would re-cache the already-scaled field as vanilla and
      compound the multiplier permanently — read that file's remarks before adding
      a `ClearLevelState`. Also owns the one-time Info-level log of what vanilla
      recovery actually is in seconds; those two fields are serialized prefab
      values, so that log line is the only place the real number is ever stated.
    - `SporeBuildUpPatch.cs` — `Spores/build-up-multiplier`: scales every incoming
      dose of Spores, from any source. A prefix on
      `CharacterAfflictions.AddStatus` (`ref float amount`, so the original still
      does all its SFX/VFX/networking/zombification work) because that is the one
      seam a spore area's emitter, a bomb's `AOE`, a zombie's bite and
      `Affliction_ZombieBite` all funnel through — hooking each hazard instead
      would only ever cover the sources the decompile happened to reveal. Reads
      `Effective*` fresh per application, so it needs no reapply hook. Runs at
      `Priority.Low` so `Diagnostics/SporeStatusSourcePatch` still logs the amount
      a source *asked* for. Compounds with the per-hazard rate dials on purpose.
  - **`Creatures/`** — the Phase 7 creature mechanics. **Read this first: the
    three Roots creatures share no code whatsoever**, and nearly every quirk in
    this folder comes from that. A beetle is a `Beetle : Mob` (a rigidbody prop
    that is also a `MobItem : Item`); a zombie is a `MushroomZombie` wrapping a
    full `Character` with its own state machine, spawned at runtime rather than
    placed; a spider is a plain `MonoBehaviour` that culls itself by toggling its
    own root GameObject. So "do X to creatures" is always three
    implementations, and `Core/` holds three sets of rules rather than one.
    - `CreatureScan.cs` — the shared "which GameObject *is* this creature"
      answers, the same one-copy-on-purpose rule as `SporeAreas/SporeAreaScan`.
    - `CreatureDisablePatch.cs` — the three `Creatures/disable-*` kill switches,
      one mechanism each: zombies are suppressed at `ZombieManager.Update`, the
      game's own (master-client-only) spawn loop, with player-turned zombies
      spared; beetles are deactivated and restored; spiders have `Scan`,
      `GrabCharacter` **and** `LateUpdate` suppressed plus their mesh hidden,
      because their own distance culling re-drives the root's active state and
      `RopeRender.DisplayRope` re-enables the web every frame (a `SetActive(false)`
      on a spider root is silently undone the moment a player walks up to it —
      i.e. exactly when it matters).
    - `CreatureSpeedPatch.cs` — zombie/beetle move speed. Two fields with the same
      meaning: `Mob.movementSpeed` and, for zombies,
      `CharacterMovement.movementForce` (**resolves RESEARCH.md Q8's open
      question** — `CharacterMovementZombie` declares no speed field of its own).
      Deliberately *not* the sibling `movementModifier`, which the game's own
      energy-drink affliction adjusts additively; same trap as `climbSpeedMod`.
    - `CreatureKnockbackPatch.cs` — beetle knockback (`bonkForce`/`bonkForceUp`
      scaled together so the shove keeps its angle). Its remarks record **why
      there is no zombie counterpart**: zombies apply no scripted knockback
      anywhere, and `MushroomZombie.reachForce` is a dead field.
    - `CreatureRagdollPatch.cs` — how long a beetle hit or zombie bite keeps the
      player down. Scales the two fields rather than patching `Character.Fall`,
      which is the game's universal knockdown; each field has exactly one caller,
      which is what makes that safe.
    - `ZombieDeaggroPatch.cs` / `BeetleDeaggroPatch.cs` — the two deaggro dials.
      Both files document a non-obvious load-bearing detail: the zombie hook must
      be `TargetIsValid` because that also gates *re-acquisition*
      (`TryLookForTarget` re-picks the nearest player every 10s with no distance
      limit, so clearing the target alone accomplishes nothing), and the beetle
      one needs a suppression window for the mirror-image reason.
    - `CreatureAggroLog.cs` / `ZombieAggroLogPatch.cs` — verbose aggro-lifecycle
      logging for both dials, including whether a deaggro came from Fairoots' rule
      or vanilla's. Exists because these two mechanics are close to unverifiable
      by eye; see `docs/TESTING.md`.
    - `SpiderStrikeWarning.cs` — "is a spider coming for me right now?", behind
      `Ui/SpiderWarningLabel`. Registry-driven off the drop RPC, never a scan
      (Roots has ~90 spiders). Its window deliberately excludes
      `spiderWaitTime`, because `SpiderState.Dropped` persists through the
      spider's whole retreat.
    - `CreatureKnockoutPatch.cs` — thrown-item knockouts. Both creatures detect the
      hit themselves rather than relying on `Bonkable`, which is a component on
      *particular item prefabs* rather than items in general. Beetles are put into
      `MobState.RigidbodyControlled` (vanilla's own tumbling state, which already
      stops them attacking and targeting); zombies are held down against their own
      `TestCharacterFell`, which otherwise stamps `fallSeconds` back to 3.
    - `BlowgunCreaturePatch.cs` — darts kill zombies and stun spiders/beetles.
      Hooked on `RPC_DartImpact`, not `FireDart`, because the effects need each
      creature's *owner* to apply them and that RPC runs on every client while
      carrying the dart's origin/endpoint.
    - `CreatureStunIndicator.cs` — the "out cold" marker for both creatures, cloned
      from `Spider.stunnedParticle` (it's an asset, so it can't be constructed in
      code). Placed over a beetle's head via `bonkPoint` and replicated by one
      `[PunRPC]` on the beetle's existing `PhotonView`.
    - `CreatureWindPatch.cs` — wind on creatures; see the "1.0 is not always
      vanilla" note under "Planned structure" above.

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
    - `ClusteredRemovalSelection.cs` — "given these positions and a budget of N
      to remove, which N?": the seeded, cluster-first selection shared by every
      mechanic that thins placed hazards (ranked by nearest-neighbour distance,
      closest-clustered first, sparing a removed candidate's nearest neighbour
      so a tight pair loses one member rather than both; ties broken by the seed
      hash then position, so scene-enumeration order never matters). Also owns
      `RemovalBudget` — the one rounding rule ("at least `floor(total * (1 -
      fraction))` always survive"). Extracted from `SporeBombCull` when
      `SporeAreaCull` needed the same logic; the pre-existing spore-bomb cull
      tests passing unchanged is the proof the extraction was
      behavior-preserving.
    - `SporeBombCull.cs` — the two-pass spore-bomb removal decision: foliage
      removal, then a `ClusteredRemovalSelection` cull budgeted against it.
      Returns per-candidate outcomes; `SporeBombCullPatch` maps them back onto
      real GameObjects. The foliage pass runs under every preset but is off in
      vanilla and under an untouched Custom (`Spore-Bombs/enable-foliage-removal`
      → `Decide`'s `foliageRemovalEnabled`); switching it off makes the pass a
      no-op **without** changing the overall removal target: the seeded pass
      still removes up to the same count, it just selects from every candidate
      instead of only the non-camouflaged ones. So it's an opt-out of *which*
      bombs go, not of removal.
    - `CoverMouth.cs` — the cover-mouth input state machine (hold vs. toggle, plus
      the outside veto that force-cancels a cover and can't be latched around) and
      its framerate-independent stamina cost. Unity-free so the awkward part is
      unit-tested rather than only observable by pressing a key in-game.
    - `SporeAreaTuning.cs` — pure arithmetic for the spore areas' size and
      Spores build-up rate (not seed-gated - every area gets the identical flat
      treatment, same shape as `SporeBombExplosionTuning`). Two non-obvious
      rules live here, both with tests: the fades scale *with* the radius (the
      native falloff ramp is measured inward from the boundary, so scaling
      radius alone would turn the size dial into a lethality dial), and the
      rate dial scales `amount` rather than the tick interval (the native
      per-tick amount is proportional to the interval, so the rate doesn't
      contain it - scaling the interval changes granularity only, not rate).
    - `SporeStatusTuning.cs` — pure arithmetic for the two `Spores`-section dials
      (recovery time and global build-up). Not seed-gated, same shape as
      `SporeAreaTuning.cs`. Documents why the clear-time dial divides the drain
      rate while multiplying the cooldown, why a vanilla rate of 0 is left alone,
      and why the build-up dial refuses to scale non-positive amounts (several
      native paths reach `AddStatus` with one, and scaling a subtraction by a
      "fewer spores" dial would *add* spores). `SecondsToClear` exists purely so
      the tests can assert the "0.5 means half as long" promise end-to-end across
      both fields at once instead of checking each direction in isolation.
    - `LabelColors.cs` — the outline color for Fairoots' own on-screen text:
      same hue and saturation as the text, HSV value scaled down. Darkened rather
      than flattened to black (the rule `peak-sense-of-direction`'s `ColorUtil`
      uses) so the stroke reads as part of the text instead of a black shape
      pasted behind it, while still separating pink text from a pink cloud.
    - `SporeBombCloudPresence.cs` — "is the player somewhere a spore bomb's cloud
      would actually apply spores?", the geometry behind `SporeBombCloudWarning`.
      Mirrors the native `AOE.Explode` rule (falloff factor
      `(1 - distance/range)^factorPow` vs. `minFactor`) rather than doing a plain
      distance check: an AOE does not affect everything inside its `range`, so an
      overlay driven by the advertised radius would light up in a ring where
      nothing can hurt you — worse than no overlay, given the setting exists to
      make the overlay trustworthy.
    - `SporeCloudOpacity.cs` — the pure alpha arithmetic behind both
      cloud-translucency settings, plus the "is this multiplier vanilla?" rule the
      restore path depends on. Multiplicative rather than absolute so a cloud's
      *internal* alpha variation survives — flattening every particle to one alpha
      would replace a soft volume with a uniform sheet, which is the look the
      setting exists to remove. Not seed-gated (every cloud gets the identical flat
      treatment), same shape as `SporeAreaTuning.cs`.
    - `SporeAreaCull.cs` — the spore-area thinning decision. Simpler than
      `SporeBombCull`: no foliage pass (a spore cloud is a 16-unit-radius volume
      around a mushroom, not a small prop that can get buried in a fern), just
      one budgeted `ClusteredRemovalSelection` pass under its own mechanic tag so
      the two mechanics' choices never correlate. Cluster-first is what makes it
      a fairness change rather than "less content": what hurts a run is
      overlapping 16-unit radii forming a stretch you can't cross, so removal
      starts there and leaves walk-around-able isolated clouds alone.
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
    - `CreatureTuning.cs` — the flat creature arithmetic (speed, knockback,
      ragdoll duration, beetle deaggro distance). Not seed-gated; same shape as
      `SporeBombExplosionTuning`.
    - `ZombieDeaggro.cs` — the zombie deaggro rule, and **the one dial in the mod
      where 1.0 is not vanilla**: vanilla zombies never deaggro, so 1.0 is the
      toughest setting and 0 is excluded. Its 30-second base at 1.0 is the game's
      own `Scoutmaster` lost-track constant, not an invented number.
    - `BeetleDeaggro.cs` — the beetle half (1.0 *is* vanilla here, since beetles
      genuinely do give up). Documents why the first version was inert at both
      extremes and why the suppression window is load-bearing.
    - `CreatureKnockout.cs` — durations and the hard-throw gate for thrown-item
      knockouts, plus the blowgun stun. Carries the **live-measured throw speeds**
      the 36 m/s threshold was calibrated from; those five measurements are
      regression tests.
    - `CreatureWind.cs` — wind on creatures, and the other place the vanilla point
      isn't 1.0: zombies already take wind (0.6× a player's) so theirs is a true
      multiplier, while beetles are immovable by force (`Mob.FixedUpdate` zeroes
      their velocity every tick) so theirs is a susceptibility with **0** as
      vanilla. Beetle drift is expressed relative to the beetle's own walking
      speed rather than the zone's `windForce`, which is an acceleration and would
      fling them across the map.
    - `Presets/PresetId.cs` — the preset enum: 1-4 are the fixed presets
      (Balanced is default), 5 is Custom (ignores the catalog and uses the
      player's own config directly).
    - `Presets/PresetCatalog.cs` — the documented front door onto the per-preset
      values: one method per setting, each carrying the reasoning for its row and
      **no numbers at all**. The numbers live in `Presets/PresetValues.g.cs`.
      Custom (`PresetId.Custom`) isn't a real row — every method maps it to
      Balanced's numbers internally, purely so a catalog lookup never throws (it's
      still called as an unused argument under Custom — see
      `OverrideResolution.cs`).
    - `Presets/PresetValues.g.cs` and `ConfigDefaults.g.cs` — **generated, do not
      hand-edit.** `scripts/apply-presets.sh` writes both from the table in
      `docs/PRESETS.md`: the first is the four preset columns (what
      `PresetCatalog` returns), the second is the Default column (what
      `PluginConfig` binds for every setting in the five gameplay sections;
      `General`/`Debug` defaults stay literals). Tune by editing that table and re-running the
      script; `bash scripts/apply-presets.sh --check` fails if either file is
      stale or a setting has drifted out of the table. `PresetValues` is indexed
      by presets 1-4 only and throws on Custom, since mapping Custom is
      `PresetCatalog`'s job.
    - `Presets/OverrideResolution.cs` — preset-vs-Custom resolution: presets 1-4
      always use their own catalog numbers, ignoring the player's config
      entirely; Custom (5) always uses the player's configured value (0
      included). No sentinel/"unset" value to track. Generic since 2026-07-30 —
      presets drive on/off toggles and timing windows as well as multipliers.
    - `WindTuning.cs` — pure arithmetic for wind force/gust-duration scaling,
      non-backpack item-force scaling, and obstacle-occlusion raycast-distance
      scaling (not seed-gated, same reasoning as `SporeBombExplosionTuning`),
      plus the two wind-preceded-fall decisions, which share one recency test
      (`IsWindForceStillRecent`): `ApplyWindRagdollImmunity`
      (`Wind/prevent-wind-ragdoll`, added 2026-07-30 — holds ragdoll control at
      full so wind can't ragdoll you off an edge at all; on under every preset)
      and the older, softer `ApplyFallCameraDampening` (a partial floor). Both
      only ever *raise* the vanilla result, so the patch just applies them in
      sequence and the more generous one wins. Both are scoped to wind-preceded
      falls by the maintainer's original scoping call (ROADMAP.md "Open
      questions"): only falls preceded by recent wind force, never every Roots
      fall. Deliberately
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
    `Character.localCharacter` only (a character's ragdoll control is driven by
    whoever owns it, so every client applying this to its own character is the
    full coverage — which is also why both settings are still
    host-authoritative: the values have to match across the lobby even though
    each client applies them itself): `WindRecentForceTrackerPatch` records a timestamp on
    `WindChillZone.AddWindForceToCharacter`'s postfix (re-deriving the
    original method's own early-return checks, since a postfix always fires
    even when the original bailed out without applying force); the actual
    dampening patches `CharacterData.GetTargetRagdollControll()` (the method
    RESEARCH.md Q6 traced as the source of the "0 the instant any fall
    starts" camera-spin mechanism), raising its floor only when the fall is
    within the configured recency window of a real wind-force application —
    all the way to full control while `Wind/prevent-wind-ragdoll` is on
    (the default), otherwise partway, up to
    `Wind/fall-camera-dampen-clamp`'s preset value.
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
- `scripts/apply-presets.sh` — the balance-tuning generator: parses
  `docs/PRESETS.md` and writes `Core/ConfigDefaults.g.cs` +
  `Core/Presets/PresetValues.g.cs`. Run it after every edit to that table, and
  `--check` to verify without writing.
- `docs/PRESETS.md` — **the source of truth for every balance number in the
  mod**: one row per setting in the five gameplay sections, with its default and
  its value under each of the four presets (`General`/`Debug` are excluded — no
  balance values, no preset involvement, defaults stay literal in
  `PluginConfig.cs`). Also documents the two rules the values encode: every
  default is vanilla, and every gameplay setting is preset-driven.
- `docs/TESTING.md` — automated-test coverage summary + manual in-game loop.

## Planned structure (fills in as phases land — see ROADMAP.md)

`SporeBombs/`, `Wind/`, `SporeAreas/` and `Creatures/` (above) are the
mechanic-group folders, one per section of `OVERVIEW.md`, each holding the
Harmony patches that scan the scene and apply removals/tweaks and delegating
every seeded decision to `Core/`. All four are now in, plus `Spores/`, which is
the one folder that doesn't map to an `OVERVIEW.md` section — it groups the dials
that act on the *status* instead of on a hazard (added 2026-07-30). What's left
(`ROADMAP.md` Phases 8-9) needs no new folder: the achievement spawn-weight
nudge intercepts the game's own weighted item selection, and the preset tuning
pass changes no code at all — it edits the table in `docs/PRESETS.md` and re-runs
`scripts/apply-presets.sh`.

**Note for Phase 7 readers:** the creature dials are the one group where
"1.0 = vanilla" is not universal, because two of the mechanics have no vanilla
value to scale. `zombie-deaggro-multiplier` makes 1.0 the *toughest* setting
(vanilla is "never deaggro", which no finite multiplier expresses) and
`beetle-wind-susceptibility` makes **0** vanilla (beetles are wind-immune by
construction). Both are documented at length in their `Core/` files; don't
"fix" either back to the usual convention.
