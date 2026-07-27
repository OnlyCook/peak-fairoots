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

   Current coverage (Phase 2 seed/preset core + Phase 4 spore bombs + Phase 5
   wind, no game install required):
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
   - **Spore-bomb recolor** (`SporeBombRecolorTests`): the hue-replacement
     math that shifts spore bombs out of "looks like grass" green (not
     seed-gated, same reasoning as the explosion tuning above). Asserted
     against the **real** material colors read off a live Roots level — the
     regular variant's `(0.24, 0.406, 0.109)` green and the explosive's
     `(0.717, 0.252, 0)` orange — not invented samples, so the tests fail if
     the math stops working on actual game data. Both variants come out with
     **real blue content, blue above green** (the accessibility crux: red-green
     colorblindness leaves the blue channel intact, so magenta separates from
     foliage where pure red does not — and the explosive variant's zero blue is
     precisely why a multiplicative tint could never get there), adopt the
     target hue exactly, keep their original **luminance** (not HSV value — a
     regression guard asserts the trap directly: an equal-value magenta really
     does lose ~half a green's perceived brightness, which is what made the
     first hue-replacement build look near-black in-game), don't desaturate,
     and stay distinguishable from each other. The luminance rescale is capped
     so an LDR color never clips past white while an HDR one keeps its
     headroom. Plus the guards: black stays black
     (unused shader slots must not be switched on), an unreadable status color
     leaves the original untouched, the target's HDR intensity doesn't change
     the result, and HSV round-trips exactly including HDR values.
   - **Preset resolution** (`PresetResolutionTests`): a hand-set config value
     always wins over the active preset and is never clobbered by switching
     presets; every spore-bomb preset row (cull fraction, trigger radius,
     knockback, screen-shake cap, VFX count) matches the ROADMAP table and
     increases with preset strength; Custom (preset 5) uses the player's
     config directly and falls back to Balanced's numbers (not a crash) for a
     setting the player hasn't touched yet under Custom.
   - **Wind tuning** (`WindTuningTests`): force/gust-duration/item-force/
     raycast-distance scaling arithmetic (not seed-gated, same reasoning as
     the spore-bomb explosion tuning above; fog-density scaling was tried and
     reverted — see below); the
     wind-preceded-fall camera-dampening decision (`IsWindForceStillRecent` /
     `ApplyFallCameraDampening`) — a fall with no recorded wind force, or a
     disabled clamp, is always left at vanilla's value; a wind-preceded fall
     within the configured window raises the floor but never lowers an
     already-higher vanilla result (e.g. the carrier/passed-out branches).
   - **Climb-to-shelter-from-wind cost** (`ClimbWindResistanceTests`): zero
     wind pressure (sheltered behind a rock, or no gust) leaves climb speed
     *exactly* untouched no matter how harsh the multipliers are — the
     mechanic can never be a stealth nerf to climbing in general; the
     slowdown fades in proportionally with pressure and is monotonic in it;
     climbing upward is slower than climbing down, climbing into the wind is
     slower than climbing with it, and climbing with the wind is never
     *faster* than vanilla (a cost, not a sail); the into-wind penalty scales
     with how much of the wind actually lies along the axis being moved
     along; out-of-range pressures clamp instead of extrapolating and a
     negative multiplier can never reverse a movement's direction; every
     preset (including Custom) slows climbing without ever freezing a climber
     in place, and Tame charges less for the shelter than Balanced, while Subtle
     turns the mechanic off entirely, matching
     every other preset row's direction; and the pressure-freshness window
     (how "the gust ended" is detected, since nothing fires an event for it)
     accepts a reading from a reset clock rather than reporting a live gust
     as calm. Also the **let-go grace window** (`GraceForceMultiplier`):
     wind is at its weakest the instant a climb is released and back to full
     strength once the window elapses, never weaker again in between
     (monotonic), never *zero* (full immunity would make wall-tapping across
     exposed ground a free crossing), never above 1 (it's a reduction, not an
     amplifier), and it ramps rather than ending in a cliff; a zero-length
     window disables the feature outright.
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
- `material-probe-hotkey = F11` — **look at** something and press this to dump
  its material/shader setup: every color slot the shader declares, its value,
  and whether Fairoots is overriding it. The tool for diagnosing anything that
  looks miscolored. Your own body is excluded from the report. Set to `None` to
  disable.
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

### Spore-bomb recolor (`General/recolor-spore-bombs`)

**Pre-req:** in a Roots run. Debug logging optional (it adds a
`[SporeBombRecolor]` line with the resolved tint and the Spores status color it
was derived from).

1. With the setting at its default (`true`), find a spore bomb sitting in
   grass. It should read as clearly pink/red against the green ground rather
   than blending into it — noticeably more saturated than vanilla, not just
   desaturated or muddy brown. Check both kinds: the mushroom clusters
   (`SporeFungus`/`SporeMushroom`) and the round explosive variant
   (`SporeMushroomExplo`).
2. Toggle it to `false` **without leaving the run** — every spore bomb in the
   level should snap back to its exact vanilla green immediately (this is the
   restore path, so look for any bomb left tinted or, worse, tinted *twice*).
3. Toggle it back to `true` — they should re-tint immediately, to the same
   shade as step 1 (not progressively darker, which would mean the vanilla
   baseline wasn't cached correctly).
4. Confirm nothing else in the level changed color — grass, ferns, the ground,
   other props.
5. Multiplayer check: this is the one setting that is deliberately **not**
   host-authoritative. With two clients in the same run, set it differently on
   each — each player should see their own choice, and neither should override
   the other.

**Report back:** whether it reads as magenta/pink rather than red or orange,
whether the recolor is uniform across the whole prop, and whether it's too
strong or too subtle (one constant, `Core/SporeBombRecolor.cs`'s
`SaturationBlend`), plus whether steps 2-3 restored/re-applied cleanly.

**If it looks wrong — patchy, veined, banded, or the wrong object entirely —
use the material probe (`Debug/material-probe-hotkey`, default F11) rather
than guessing.** Stand next to the object in question and press it: the log
names every color slot that object's shader declares, its value, and whether
Fairoots is overriding it. That distinguishes the two failure modes that look
alike from a screenshot — "the mod recolored the wrong subset of color slots
on the right object" (the classic veined/blotchy result; fix
`SporeBombRecolorPatch`'s `ExcludedProperties`) versus "the mod touched an
object it shouldn't have"
(the report shows an override on something that isn't a spore bomb at all).
A report showing *no* overrides means the mod isn't involved and whatever
looks off is vanilla.

### Live vs. level-load-only setting updates (`Debug/apply-changes-live`)

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
4. Confirm the rest of the `Debug` section (e.g. `keep-vanilla-trigger-radius`)
   and the flat settings that are always immediate by design
   (`disable-wind-entirely`, `backpack-always-immune`, `recolor-spore-bombs`)
   still apply instantly regardless of `apply-changes-live`'s value.

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

**Pre-req:** `preset = Custom` and `Spore-Bombs/trigger-height-multiplier` set
to a value below `1.0` (`1.0` is vanilla/disabled by design - presets 1-4
ignore this config entry entirely and always use their own catalog number,
Balanced's `0.804`), debug logging on to see the suppression log line (the fix
itself works regardless of debug logging).

1. Find a "Spore Bomb" (`SporeFungus`) or "Poison Spore Bomb" (`SporeMushroom`,
   non-Explo) - not the round "Explosive Spore Bomb" - and try jumping over it
   from a height/approach where you're clearly above the mushroom mound itself.
   It should no longer trigger; check the log for `[SporeBombHeightGate]
   suppressed trigger on "..." - player X.XXm above base (cutoff X.XXm)` -
   the cutoff in meters is the configured multiplier times the internal 2.8m
   baseline (`SporeBombExplosionTuning.TriggerHeightBaselineMeters`).
2. Walk directly into the same spore bomb at ground level - it should still
   trigger normally (the fix only suppresses height, not proximity).
3. Set `trigger-height-multiplier = 1.0` (or switch to any non-Custom preset)
   and repeat step 1 - it should go back to vanilla behavior (triggers even
   when jumped over).
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
6. Set `keep-vanilla-trigger-radius = true` (leave `trigger-height-multiplier`
   below `1.0`) and repeat step 1 - jumping over should trigger it again (full
   vanilla behavior, height cutoff included), and the wireframe should show a
   full, unflattened sphere again. Set it back to `false` afterward and
   confirm both the cutoff and the flattened wireframe return.
7. Switch to a non-Custom preset (e.g. Balanced) and edit
   `trigger-height-multiplier` directly in the config or via PEAKLib.ModConfig
   - it should have **no effect at all** while any preset other than Custom
   is active; only switching to Custom should make the edited value apply.

**Report back:** whether jumping over now actually works, whether Balanced's
`0.804` (reproducing the maintainer's previously playtest-confirmed 2.25m
absolute cutoff) still feels right against the actual mushroom height, and
whether ground-level triggering, the Explosive variant, and the
non-Custom-preset lockout are all unaffected.

### Wind force / gust duration / item / backpack immunity / obstacle occlusion

**Pre-req:** debug logging on, in a Roots run during an active wind gust.

> **Two regressions found and fixed during live testing (2026-07-22), plus a
> follow-up split.**
> 1. An earlier version of this patch also scaled `FogConfig.windFogDensity`/
>    `WindFogTextureDensity` during wind. That was pulled entirely (the real
>    density/opacity relationship for those shader globals lives in shader
>    code this mod can't decompile or verify) - fog is untouched by this mod.
> 2. The actual cause of the reported "screen turns solid black" was found
>    afterward, once it recurred with fog scaling already removed: scaling
>    gust duration down to a genuinely zero-length gust (at the time, still
>    tied to the same multiplier as force) made the *native* windActive
>    on/off timer flip rapidly - and since the *game's own* (untouched by
>    this mod) fog/storm-blend logic only decays after 0.1s of no
>    re-trigger, the rapid re-toggling never gave it that gap, so it
>    ratcheted up to fully opaque and stayed there. Fixed by flooring the
>    scaled gust duration at `WindTuning.MinWindActiveDurationSeconds` (1s) -
>    see that constant's remarks.
> 3. `force-multiplier` and gust duration/frequency were then **split into two
>    independent config entries** (`Wind/force-multiplier` and
>    `Wind/gust-duration-multiplier`) at the maintainer's request, so push
>    strength and gust timing can be A/B-tested separately - presets 1-4 still
>    use the same number for both (unchanged feel), only `Custom` can diverge
>    them. Force can still legitimately go to exactly 0 (no push); gust
>    duration is always floored at 1s regardless of its own multiplier.

1. Load into Roots on Balanced and check the log for `[WindTuning] captured
   baseline + applied tuning (vanilla windForce=20, ...)` — confirms the
   vanilla scene value (20, not the class default of 15) was captured and
   Balanced's 0.8x was applied on top of it, not the class default. Also
   check for the per-apply `[WindTuning] applied to WindChillZone#... windForce
   20->16, ...` verbose line (needs debug logging on) if you want to see the
   exact before/after numbers.
2. During a gust, drop a non-backpack item (e.g. a rock) and a backpack in the
   open: the backpack should never budge no matter how strong the wind is;
   the loose item should still move, but noticeably less forcefully than
   before this change. **Important:** confirm `Wind/force-multiplier` is
   genuinely non-zero first (check the F9 diagnostic's `[Wind] ... windForce=`
   line) - items get *zero* force whenever `windForce` is 0, regardless of
   `item-force-multiplier`, since the native game formula multiplies
   `windForce × windItemFactor` together (confirmed 2026-07-22: this looked
   like a broken item-multiplier at first, but was actually a leftover
   `force-multiplier=0` from earlier testing).
3. Stand behind a large obstacle during a gust — wind should stop pushing you
   noticeably sooner (from further away) than vanilla, confirming the widened
   raycast-occlusion range.
4. Confirm fog/visibility during a gust looks exactly like vanilla (no
   change) — this is the regression check for the fog revert above.
5. Switch to `Custom`, set `Wind/force-multiplier` to `0` (the extreme case
   that caused the black-screen regression above) but leave
   `gust-duration-multiplier` at its default, reload, and wait through at
   least two full gust cycles: wind should apply zero push (correct - 0 is a
   legitimate "no wind" ask), gusts should still last a normal (unscaled-by-
   force) duration, and the screen/fog should stay completely normal the
   whole time.
6. Now do the opposite: set `force-multiplier` back to a normal value (e.g.
   `0.8`) and set `gust-duration-multiplier` to `0` instead - gusts should
   become very short (floored at 1s) but still push you normally while
   they're active, confirming the two settings are genuinely independent of
   each other. Set both back to Balanced's defaults afterward.
7. Set `Wind/backpack-always-immune` to `false` and repeat step 2 - the
   backpack should now be pushed by wind just like any other item (scaled by
   the same `item-force-multiplier`). Set it back to `true` afterward.
8. Set `Wind/disable-wind-entirely` to `true` (works on every preset, not just
   Custom) - **wind should stop occurring at all**, not just revert to
   vanilla strength: if a gust is actively blowing the instant you flip it,
   it should cut off immediately, and no new gust should ever start again
   (watch through at least 2-3 minutes, long enough to cover a normal calm
   period) while the switch stays on. Set it back to `false` and confirm wind
   resumes normally (Fairoots' tuning returns immediately too, no reload
   needed).

**Report back:** the logged vanilla baseline vs. tuned values, whether
backpack immunity held up under a strong gust (and correctly stopped when
toggled off), whether the occlusion difference was noticeable, whether
`disable-wind-entirely` genuinely stopped all wind from occurring (not just
reverted its strength) and resumed cleanly when turned back off, and confirm
fog/visibility looked unchanged from vanilla throughout.

### Wind-preceded fall camera dampening

**Pre-req:** debug logging on, in a Roots run, default
`fall-camera-dampen-clamp = 0.35` / `fall-camera-dampen-window-seconds = 1.5`.

1. Get blown off a ledge by wind and immediately try to grab a wall or fire a
   Rescue Hook while falling — the camera should stay noticeably more
   player-controlled than before this change (less of the disorienting
   "spin"), giving you a real chance to react.
2. Jump off the same ledge deliberately with **no wind active** — the camera
   should spin exactly like vanilla (full ragdoll-head tracking), confirming
   the dampening only applies to wind-preceded falls, not every fall.
3. Get blown off a ledge, then wait *longer than 1.5s* before actually falling
   (e.g. stand near the edge until the gust passes, then jump on your own) —
   should also behave like vanilla, confirming the recency window matters.
4. Set `Custom` preset, `fall-camera-dampen-clamp = 0`, repeat step 1 — should
   go back to full vanilla spin even immediately after a wind-induced fall.

**Report back:** whether the wind-preceded case felt meaningfully calmer,
whether a non-wind fall was completely unaffected, and whether 0.35 is a good
default or needs adjusting (the maintainer's own framing: strong enough to
let you react, not so strong it removes all sense of falling).

### Climb to shelter from wind

**Pre-req:** debug logging on, in a Roots run, defaults
(`climb-shelters-from-wind = true`, Balanced → ×0.90 base / ×0.85 upward /
×0.85 into-wind). Find a climbable wall inside the wind zone with clear
exposure (no rock between you and the gust). Note the mechanic is **off
entirely on Subtle** — test on Balanced or later.

1. Grab the wall and hold on through a full gust. You should **not** be pushed
   off at all — no ragdoll, no losing the wall — where before the gust would
   typically rip you off. The log should show
   `[ClimbWindShelter] climbing wind pressure engaged (...)` once per gust
   (and `released` when it passes), not per frame.
2. During that same gust, climb **upward**: it should feel dramatically
   slower, and (because the game charges climbing stamina per second) burn
   noticeably more stamina for the same height than the same climb between
   gusts. Then traverse sideways *into* the wind vs. *with* it — into should
   be clearly slower.
3. Climb **down** during a gust — slowed by the base multiplier only, not the
   extra upward penalty; it shouldn't feel like the wall is holding you up.
4. Climb the same wall with **no gust active**, and again while behind a rock
   / under cover mid-gust: both should feel exactly like vanilla climbing.
   This is the important one — the slowdown must only exist in the moments
   it's actually buying you immunity.
5. Repeat step 1 on a rope and (if you can find one) a vine — same immunity,
   same flat slowdown while pulling yourself up.
6. Let go mid-gust and fall: the wind-preceded-fall camera dampening should
   *not* kick in (you were sheltered, so nothing pushed you), while getting
   blown off on foot still triggers it as before.
7. **The let-go grace window.** Climb a wall mid-gust, then deliberately let
   go while the wind is still blowing. You should get a noticeable moment
   (~0.5s) of much weaker wind — enough to start sprinting away from the
   ledge or re-grab the wall — instead of the vanilla catapult, and the wind
   should come back smoothly rather than snapping to full. The log shows
   `[ClimbWindShelter] let-go grace window started/ended`. Then check it
   can't be abused: repeatedly tap-grab and release a wall while trying to
   cross an exposed stretch — the wind should still clearly push you the
   whole way (reduced, never absent), so this isn't a free crossing.
   `climb-shelter-grace-seconds = 0` should restore the vanilla catapult
   exactly.
8. Set `climb-shelters-from-wind = false` and repeat step 1 — vanilla
   behavior should return immediately (pushed off the wall, no slowdown),
   without a level reload.
9. Switch to the `Subtle` preset and repeat step 1 — the mechanic is off there
   regardless of `climb-shelters-from-wind`, so the gust should rip you off
   the wall exactly like vanilla. Turning the toggle back on under Subtle must
   change nothing.

**Report back:** whether full immunity feels right or too strong, whether the
0.5s / ×0.15 grace window is enough to survive finishing a climb (and not so
generous that wall-tapping trivialises exposed ground), and whether
Balanced's ×0.90/×0.85/×0.85 is enough of a cost — the intent is "holding on
is always the safe option, but it costs you real time and stamina," so it's
wrong if either climbing through a gust still feels free, or the climb is so
slow that waiting the gust out is strictly better (the first playtest's
verdict on the original ×0.55/×0.60/×0.60: too slow, exactly that failure
mode).

### Spore areas: master disable switch (`Spore-Areas/disable-spore-areas`)

**Pre-req:** debug logging on, in a Roots run. The log reports one line per
level load and per config change:
`[SporeAreas] level load: disable-spore-areas=off, N spore area(s) found, ...`
— N should be in the low tens (12 and 23 in two live runs).

1. With the setting **off**, walk into a spore cloud: vanilla behavior — Spores
   status ticks up, green screen filter appears.
2. Flip it **on** mid-run (no level reload). Every cloud should disappear
   *visually* as well: the cloud particles **and** the mushroom in the middle
   of it, while the giant mushroom tree it grows on stays (that's scenery, not
   the hazard). Standing where a cloud was should apply no Spores and show no
   screen filter. The log line should report `N newly hidden`.
3. Flip it back **off** — every cloud returns immediately, in place. `N
   restored` should equal what was hidden. Nothing *else* in the level should
   pop into existence (the restore is registry-based specifically so it can't
   un-hide something the game itself disabled).
4. Set off a spore bomb with the setting **on**: its own temporary mini spore
   area must still work normally — this switch deliberately doesn't touch it.
5. Load a fresh Roots level with the setting already on: the clouds should be
   gone from the start (`level load: disable-spore-areas=ON`).

**Report back:** whether anything visible is left behind (a floating mushroom
or lingering particles = the parent-walk stopped too low) or whether too much
disappeared (terrain/props/whole trees = it walked too high).

### Spore areas: seeded thinning (`Spore-Areas/removal-fraction`)

**Pre-req:** debug logging on. Since Subtle and Balanced both remove 0%, either
switch to `Generous`/`Tame` or set `preset = Custom` and pick a fraction. This
one is **level-load-only** — changing it mid-level does nothing by design.

1. Load a Roots level and read the summary line:
   `[SporeAreaCull] N spore area(s): removed X, kept Y (fraction=..., seed=...)`.
   `Y` must equal `floor(N × (1 - fraction))` exactly (23 areas at 0.5 → kept
   11, verified live 2026-07-27).
2. Check the spacing line right below it:
   `nearest-neighbour spacing - removed: median Am ..., kept: median Bm ...`.
   The **removed median should be the lower** of the two — that's the
   cluster-first rule visible against a real level. At high fractions the gap
   narrows, which is expected (once the crowded areas are used up, removal has
   to reach isolated ones too); at a low fraction it should be pronounced.
3. Same seed, same level, load twice → an identical removed list (compare the
   `@ (grid)` coordinates on the per-removal lines). Then change only the seed
   and reload: same count, different set.
4. Walk to a couple of removed coordinates: no cloud, no mushroom, no spores —
   and the surrounding mushroom tree still there.
5. With a non-zero fraction *and* `disable-spore-areas` on, then toggling that
   switch back off: only the areas the seed kept should reappear — the
   seed-removed ones must stay gone.

**Report back:** whether Generous's 20% / Tame's 35% feel like the right amount
of thinning, and whether the cluster-first choice reads as sensible on the
ground (are the *right* clouds gone — the overlapping ones — or does it feel
arbitrary?).

### Host authority (multiplayer — needs a second player/PC, can't be verified solo)

**Pre-req:** debug logging on for both host and at least one non-host client,
both running the *same* Fairoots build. See ROADMAP.md's "Host authority"
section for the full rationale/mechanism before testing this.

1. Host sets a distinctive `seed` (e.g. `12345`) and `Custom` preset with an
   obviously different `Spore-Bombs/cull-fraction` (e.g. `0.9`) than the
   non-host client has locally configured (e.g. leave the client on `0.25`
   Balanced). Both load into the same Roots run: the non-host client's spore
   bomb count/positions should match the *host's* configured cull fraction
   and seed exactly, not its own local settings - confirms the host's config
   wins regardless of what the client has set locally.
2. While both are already in a Roots level, host changes `Wind/force-multiplier`
   live (e.g. to `0.1`) - the non-host client should feel the *same* weakened
   wind almost immediately (no reload needed), even though the client never
   touched their own config.
3. Host sets `Wind/disable-wind-entirely = true` - wind should stop for
   **both** players, not just the host. Set it back to `false` - wind
   resumes for both.
4. Non-host client changes any of their own local Wind/Spore-Bombs settings
   (seed, preset, force-multiplier, backpack-always-immune, etc.) - this
   should have **zero** effect on their actual in-game experience (still
   matches whatever the host has configured), confirming a client can't
   unilaterally alter shared gameplay.
5. (If testable) Have the host disconnect/leave and let Photon promote the
   non-host to master client - the new host's own config should immediately
   become authoritative for whoever's left (via `HostAuthoritySync`'s
   `OnMasterClientSwitched`), without needing a level reload.
6. Confirm the wind-preceded-fall camera-dampening clamp is the one exception
   that stays per-client: each player should be able to set their own
   `fall-camera-dampen-clamp` independently and have it apply to their own
   camera regardless of what the host or other players have set.

**Report back:** whether the non-host client's spore bombs/wind genuinely
matched the host's config (not their own), whether live host changes
propagated to the client promptly, whether disable-wind-entirely affected
both players, and whether the camera-dampening clamp correctly stayed
per-player independent.

### Mod-presence enforcement (multiplayer — needs a second player/PC without Fairoots installed)

**Pre-req:** debug logging on. One player has Fairoots installed, one doesn't,
both in the same lobby (order doesn't matter — host or non-host missing it).

1. Have a modded player open the Gate Kiosk (Boarding Pass) and click
   **Start**. Since someone in the lobby is missing Fairoots, the click
   should be blocked and a confirm popup should appear immediately: title
   "Fairoots", body ending in "Start anyway?", two buttons (Cancel / Start
   Anyway). No player names anywhere in the popup.
2. Check `LogOutput.log` on the modded player for
   `[BoardingPassStartGatePatch] Start blocked pending confirmation - N
   player(s) missing Fairoots: <nickname(s)>` — confirms the specific missing
   name(s) went to the log, not the popup.
3. Click **Cancel** — the popup should close, the run should NOT start, and
   you should still be sitting at the Boarding Pass exactly as before
   clicking Start (nothing skipped or half-started).
4. Click **Start** again, this time click **Start Anyway** — the run should
   start normally (same as vanilla), confirming Confirm genuinely bypasses
   the check rather than looping back into another popup.
5. Have the missing player install Fairoots (no need to rejoin — just having
   it running should update their player property next time anything
   rechecks), then click Start again — it should now proceed with zero popup
   (everyone modded, no gap).
6. With everyone modded, confirm clicking Start behaves exactly like vanilla
   — no popup, no delay, no logged warning at all.
7. Switch the current language (`LocalizedText`'s in-game language setting)
   to a couple of the 14 translated languages and repeat step 1 — confirm the
   popup text actually changes (not stuck on English) and doesn't look
   obviously broken/cut off for at least one non-Latin-script language (e.g.
   Japanese, Korean, or Simplified Chinese) and one Cyrillic one (Russian or
   Ukrainian).

**Report back:** whether the popup appeared right on the Start click (not
before/after), whether the log line had the correct nickname(s), whether
Cancel genuinely left the Boarding Pass untouched, whether Start Anyway
correctly bypassed the check on the same click without looping, and how the
non-English text looked.
