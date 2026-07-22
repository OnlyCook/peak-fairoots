# Testing guide

Two layers, per ROADMAP.md's testing strategy:

1. **Automated unit tests** (`tests/Fairoots.Tests/`, `dotnet test`) — cover
   the seed-deterministic decision logic in isolation (no game install
   needed). This is the primary net for "did the RNG change actually apply
   correctly," since playing it out by hand can't easily prove a spawn count
   or a seed's reproducibility. See ROADMAP.md "Testing strategy" for the full
   rationale.

   Run them with:

   ```bash
   cd tests/Fairoots.Tests
   dotnet test
   ```

   Current coverage (Phase 2 seed/preset core + Phase 4 spore bombs, no game
   install required):
   - **Determinism** (`DeterministicHashTests`): the `(seed, mechanic,
     position) → value` hash is stable for identical inputs, uniform enough to
     use as a probability, and decorrelated across different seeds and
     mechanic tags. Guards the "same seed = same result" premise.
   - **Spore-bomb cull budget** (`SporeBombCullTests`): same seed culls the
     same *specific* objects (not just the same count); the two-pass budget
     matches ROADMAP.md's worked example (foliage removal counts toward, not
     on top of, the seeded target, and never overshoots it); the selection is
     independent of input order (so it's multiplayer-consistent).
   - **Spore-bomb explosion tuning** (`SporeBombExplosionTuningTests`): the
     trigger-radius/knockback/screen-shake-cap/VFX-count arithmetic (not
     seed-gated — every kept spore bomb gets the same flat scaling, so this is
     plain multiplier math, not a decision) — a screen-shake cap never widens
     an already-tighter vanilla range, a VFX-count multiplier never rounds
     below zero, etc. Also covers the trigger-height-cutoff bug-fix decision
     (`ShouldSuppressTriggerForHeight`): disabled (0) never suppresses, below
     the configured height never suppresses, above it does, and the exact
     boundary height still counts as "touching it."
   - **Preset resolution** (`PresetResolutionTests`): a hand-set config value
     always wins over the active preset and is never clobbered by switching
     presets; every spore-bomb preset row (cull fraction, trigger radius,
     knockback, screen-shake cap, VFX count) matches the ROADMAP table and
     increases with preset strength; Custom (preset 5) uses the player's
     config directly and falls back to Balanced's numbers (not a crash) for a
     setting the player hasn't touched yet under Custom.
2. **Manual in-game loop** (this doc) — for anything only observable at
   runtime (feel, visual clutter, screen shake, actual spawn positions in a
   real level).

## Build & deploy in one command

```bash
cd src/Fairoots
dotnet build -c Release -p:DeployToProfile=true
```

Copies `Fairoots.dll` into
`~/.config/r2modmanPlus-local/PEAK/profiles/Default/BepInEx/plugins/OnlyCook-Fairoots/`.

Then in **r2modman**: make sure *BepInExPack PEAK* is installed & enabled in
the same profile, and click **Start modded**.

## Where the logs are

`~/.config/r2modmanPlus-local/PEAK/profiles/Default/BepInEx/LogOutput.log`
(rewritten every launch). Filter for our lines:

```bash
grep -iE "Fairoots" ~/.config/r2modmanPlus-local/PEAK/profiles/Default/BepInEx/LogOutput.log
```

## Debug diagnostics (runtime "what works / what doesn't" report)

The mod ships a diagnostic harness, off by default. Turn it on in the config's
**`Debug`** section (bottom of `OnlyCook.Fairoots.cfg`, or the in-game settings
menu if PEAKLib.ModConfig is installed):

- `enable-debug-logging = true` — master switch (nothing below runs without it).
- `log-scene-scan-on-load = true` — auto-dump a report each time a level
  finishes generating.
- `scene-scan-hotkey = F9` — press in-game to dump a report on demand (e.g.
  while standing next to a spore bomb). Set to `None` to disable.
- `show-removed-spore-bomb-markers = true` — 2D on-screen label over every
  spot a spore bomb was removed this level load, tagged by why (foliage vs.
  seeded cull). Off by default even with debug logging on.
- `show-spore-bomb-trigger-radius = true` — red 3D wireframe drawn around a
  kept spore bomb's *actual* trigger collider (exact shape/size/orientation),
  only for one within 10m of the camera. Off by default even with debug
  logging on.
- `keep-vanilla-trigger-radius = true` — leaves spore-bomb trigger hitboxes at
  their original size instead of shrinking them; combine with the wireframe
  above for before/after comparison screenshots. Unlike the rest of this
  section this is a gameplay override, so it applies even with
  `enable-debug-logging` off.

The report goes to `LogOutput.log` (see above) and prints, per section, whether
each thing was found (`OK`/`MISSING`) plus live values read straight off the
scene objects:

- **Biome** — current biome, whether Roots is present, current segment.
- **Wind** — `WindChillZone` field values (`windForce`, `windMovesItems`,
  `windItemFactor`, and crucially `useRaycast` + its min/max distances).
- **SporeBombs** — every object matching the
  `SporeFungus`/`SporeMushroom`/`SporeMushroomExplo` name substrings, grouped by
  exact name (this confirms the real Roots prefab prefix), with each one's
  `AOE` / `ExplosionEffect` / `AddScreenshake` / `StatusEmitter` components and
  their configured values (range, knockback, shake range, particle counts).
- **SporeAreas** — `StatusEmitter` count and how many are
  `WindAffectedStatusEmitter` (tells us if "wind disperses spore areas" already
  partly exists), with radius/amount/fade per emitter.
- **Creatures** — `ZombieManager.maxActiveZombies` and live
  MushroomZombie/Beetle/Spider counts.

To capture a report: enable the three settings above, **Start modded**, load
into a Roots run (press F9 next to a spore bomb), then:

```bash
grep -iE "Fairoots|scene diagnostics|\[SporeBombs\]|\[Wind\]|\[SporeAreas\]|\[Biome\]|\[Creatures\]" \
  ~/.config/r2modmanPlus-local/PEAK/profiles/Default/BepInEx/LogOutput.log
```

Paste that back and it resolves most of RESEARCH.md's remaining runtime open
questions in one pass.

## Test checklist

### Spore-bomb explosion tuning (trigger radius / knockback / screen-shake / VFX)

**Pre-req:** debug logging on (`enable-debug-logging = true`), in a Roots run.

1. Load into Roots on the default preset (Balanced) and note the log line
   `[SporeBombCull] ... trigger-radius shrunk on N (multiplier=0.85)` — confirms
   every kept spore bomb's trigger hitbox was resized.
2. Walk up to a kept spore bomb until it's about to trigger; it should require
   getting noticeably closer than before this change (smaller hitbox).
3. Set it off and check `LogOutput.log` for `[SporeBombExplosion] tuned
   detonation ... (knockback x0.8, vfx x0.75, shake-cap=30m)` — confirms the
   patch fired and used Balanced's numbers.
4. In-game, the detonation should throw you noticeably less far, spawn
   visibly fewer particle orbs, and screen-shake should stop being felt past
   ~30m instead of vanilla's much larger range.
5. Switch `preset` to `Custom` (5), set `Spore-Bombs/knockback-multiplier` to
   e.g. `0.1` and leave the rest at their defaults, reload, trigger another
   spore bomb: knockback should be barely noticeable while VFX count/shake
   cap use their own configured (default) values, not Balanced's - Custom
   never falls back to a preset, it always reads its own config values
   directly (report back what you see for each of the four values).
6. Switch `preset` back to `Balanced` (2) without changing the Custom values
   back: the spore bomb should behave exactly like step 3/4 again, confirming
   presets 1-4 ignore the `Spore-Bombs` config entries entirely.

**Report back:** preset used, whether the four log lines above showed up,
and whether the felt in-game effect (distance thrown, particle count, shake
range) matched the logged multipliers.

### Live vs. level-load-only setting updates (`General/apply-changes-live`)

**Pre-req:** debug logging on, in a Roots run, preset set to `Custom` (5) so
the `Spore-Bombs` entries are actually in effect.

1. With `apply-changes-live` at its default (`true`), change
   `knockback-multiplier` while standing in Roots (e.g. via PEAKLib.ModConfig)
   and trigger a spore bomb - the new value should apply to that very
   detonation. Change `trigger-radius-multiplier` too - already-placed spore
   bombs should visibly resize immediately, no reload needed.
2. Set `apply-changes-live` to `false`. Change `knockback-multiplier` again
   and trigger a spore bomb - it should still use whatever was in effect
   *before* you flipped the flag off (the level's frozen snapshot), not the
   new value. Same for `trigger-radius-multiplier` - existing hitboxes should
   stay exactly the size they already were, not resize.
3. Leave Roots and reload into a fresh Roots level (or `Custom` → any preset →
   `Custom` isn't required here, an actual level load is): the new
   `knockback-multiplier`/`trigger-radius-multiplier` values should now be in
   effect, confirming the freeze only lasts until the next Roots load.
4. Confirm `Debug` section settings (e.g. `keep-vanilla-trigger-radius`) still
   apply immediately regardless of `apply-changes-live`'s value.

**Report back:** whether step 1's changes applied instantly, whether step 2's
changes were correctly ignored until step 3's reload, and whether step 4's
debug toggle stayed live throughout.

### Trigger-box wireframe + vanilla-size comparison (for the README screenshot)

**Pre-req:** debug logging on, `show-spore-bomb-trigger-radius = true`.

1. Walk within 10m of a kept spore bomb — a red wireframe (drawn as thick
   camera-facing quads, not hairline `GL.LINES`, so it should read clearly in
   a screenshot) should appear around its trigger collider (sphere, matching
   its exact current size/position); it should disappear again past 10m.
2. Toggle `keep-vanilla-trigger-radius` **without leaving the run** (via
   PEAKLib.ModConfig's live menu, or editing the `.cfg` and letting BepInEx's
   file-watcher pick it up) and walk around checking several different spore
   bombs, not just the one you were just looking at. Every single one should
   now show the vanilla (larger) size — not just the one nearest you when you
   toggled it. Check the log for `[SporeBombCull] full trigger-radius
   refresh: N active spore bomb(s) found scene-wide, M resized` confirming a
   scene-wide pass ran, and that `N` matches roughly how many spore bombs are
   actually loaded.
3. Toggle it back off the same way and re-check the same several spore bombs
   — all of them should shrink back, matching the *original* configured
   multiplier again (not a smaller/compounded value from being shrunk twice).
4. Take the "before"/"after" screenshots once you're satisfied every nearby
   spore bomb responds consistently, not just some of them.

**Report back:** whether the wireframe rendered at all and in the right color
(confirmed URP - see `TriggerRadiusOverlay`'s remarks - so this uses
`RenderPipelineManager.endCameraRendering`, not the legacy `Camera.onPostRender`
an earlier version tried, which never fired), whether it disappears past 10m,
and whether the vanilla-vs-shrunk size difference matches expectations.

### Trigger-height cutoff (jumping over a Spore Bomb / Poison Spore Bomb)

**Pre-req:** default `max-trigger-height-meters = 1.75` (playtest-confirmed as
the right value), debug logging on to see the suppression log line (the fix
itself works regardless of debug logging).

1. Find a "Spore Bomb" (`SporeFungus`) or "Poison Spore Bomb" (`SporeMushroom`,
   non-Explo) - not the round "Explosive Spore Bomb" - and try jumping over it
   from a height/approach where you're clearly above the mushroom mound itself.
   It should no longer trigger; check the log for `[SporeBombHeightGate]
   suppressed trigger on "..." - player X.XXm above base (cutoff 1.75m)`.
2. Walk directly into the same spore bomb at ground level - it should still
   trigger normally (the fix only suppresses height, not proximity).
3. Set `max-trigger-height-meters = 0` and repeat step 1 - it should go back
   to vanilla behavior (triggers even when jumped over).
4. Try jumping over an "Explosive Spore Bomb" - it should behave exactly as
   before this change (still triggers), since that variant is intentionally
   excluded.
5. With `show-spore-bomb-trigger-radius = true`, look at a "Spore Bomb"/
   "Poison Spore Bomb" wireframe - it should now be visibly flattened at the
   cutoff height (a filled, semi-transparent "cap" disc where the top used to
   be, not a full sphere reaching high overhead) - this is the shot for the
   README screenshot showing the trigger area now matches the mushroom
   instead of the oversized vanilla sphere. Confirm an "Explosive Spore Bomb"
   still draws as a full, unflattened sphere.
6. Set `keep-vanilla-trigger-radius = true` (leave `max-trigger-height-meters`
   at 1.75) and repeat step 1 - jumping over should trigger it again (full
   vanilla behavior, height cutoff included), and the wireframe should show a
   full, unflattened sphere again. Set it back to `false` afterward and
   confirm both the cutoff and the flattened wireframe return.

**Report back:** whether jumping over now actually works, whether the 1.75m
default still feels right against the actual mushroom height (adjust
`max-trigger-height-meters` and re-test if not), and whether ground-level
triggering and the Explosive variant are both unaffected.
