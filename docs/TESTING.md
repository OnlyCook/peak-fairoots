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
   wind + Phase 6 spore areas + Phase 7 creatures, no game install required):
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
   - **Spore-area thinning** (`SporeAreaCullTests`): same seed removes the same
     *specific* areas; the count is exactly `floor(total × (1 - fraction))`
     survivors across sizes and fractions; out-of-range fractions clamp;
     different seeds give a different set but the same count; the mechanic tag
     is independent of the spore-bomb cull's, so the two never correlate; the
     result is independent of input order; and the **cluster-first rule** is
     asserted directly (three tight pairs plus four isolated areas, budget 3 →
     exactly one member of each pair removed, no isolated area touched), plus
     Subtle and Balanced removing nothing at all.
   - **Spore-area size** (`SporeAreaTuningTests`): not seed-gated (every area
     gets the same flat scaling), so these are invariant proofs — a multiplier
     of 1.0 is *exactly* vanilla, scaling is proportional, negatives clamp to
     zero instead of producing a nonsensical negative radius, the visual scale
     has a positive floor (never a degenerate zero-scale transform), and the
     visible cloud scale always equals the radius scale so what you see is what
     applies the status. The load-bearing one: `innerFade` stays the same
     *fraction* of the radius at every multiplier, so the falloff shape is
     preserved and the radius dial can't quietly double as a lethality dial.
   - **Spore status dials** (`SporeStatusTuningTests`): the `Spores` section's two
     multipliers, not seed-gated, so invariant proofs again. The load-bearing one
     is that the clear-time multiplier scales **total** recovery time (`cooldown +
     status / rate`) by exactly the configured factor — the setting only means
     "half as long" if the drain rate is divided while the cooldown is multiplied,
     each direction is easy to get backwards, and neither shows up in a build log.
     Asserted end-to-end on the combined time *and* per-field, since two
     compensating sign errors would satisfy the combined check alone; plus the
     maintainer's own worked example (a 15s vanilla clear becomes 7.5s at 0.5).
     Also: the multiplier clamps instead of dividing by zero, and a vanilla drain
     rate of 0 stays 0 at every multiplier (a "clear faster" dial must not become
     a "spores now clear at all" dial). For the build-up dial: the literal ask
     (0.5 on a dose of 10 gives 5), non-positive amounts are left untouched
     (several native paths reach `AddStatus` with one, and scaling a subtraction
     would *add* spores), the result never goes negative, and a documented proof
     that it **compounds** with the spore-area rate dial rather than replacing it.
     Two preset-table guards: Subtle's clear time is exactly vanilla, and *no*
     preset moves the global build-up dial off 1.0 — deliberate, since the presets
     already reduce build-up per hazard.
   - **Spore-cloud translucency** (`SporeCloudOpacityTests`): not seed-gated, so
     invariant proofs again — a multiplier of 1.0 is *exactly* vanilla (the
     restore path depends on it), 0 is fully invisible, thinning can never make a
     cloud denser than vanilla, negatives clamp to 0 rather than writing a
     negative color channel, and >1 counts as vanilla so restoring stays exact.
     The load-bearing one: two particles authored at different alphas still
     differ by the same *ratio* afterwards, so the cloud stays a soft volume
     rather than flattening into a uniform sheet.
   - **Spore-bomb cloud presence** (`SporeBombCloudPresenceTests`): the rule behind
     the persistent spore-bomb overlay. Proves it agrees with the native
     `AOE.Explode` falloff rather than the advertised radius — the outer shell of
     the radius, where `factor < minFactor` and nothing is applied, must *not*
     count as inside — plus the cutoff moving with a scaled radius (so the overlay
     can't disagree with the hazard), a zero range never counting as inside, and a
     non-positive falloff exponent not silently degrading to "anywhere in range"
     (`Math.Pow(x, 0)` is 1 for every x).
   - **Label outline color** (`LabelColorsTests`): the stroke behind the
     spore-cloud warning label stays the same hue and saturation as the text while
     being unambiguously darker (a black outline would read as pasted on, no
     outline would let pink text vanish into a pink cloud), clamps to black at full
     darkening instead of wrapping back to a bright color, and tracks whatever
     color the text is rather than assuming pink — the label reads its color live
     off the game's Spores status color.
   - **Cover-mouth input/cost** (`CoverMouthTests`): hold mode follows the key
     exactly; toggle mode flips only on the key-*down* edge (a held key must not
     re-toggle every frame) and survives being held or released; the outside veto
     (climbing, out of stamina, a menu open, the host's kill switch) force-cancels
     a cover and blocks starting one in *both* modes, and specifically **cannot be
     latched around** — the trap being that a veto which only suppressed the effect
     while leaving a toggle latched would make the next press read as "start" when
     it was really "clear a stuck flag," so the player would press once and see
     nothing happen. Plus: the stamina cost is framerate-independent (60fps and
     144fps pay the same for one second), and zero when disabled.
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
   - **Creature dials** (`CreatureTuningTests`): the flat speed/knockback/
     ragdoll arithmetic (not seed-gated — every zombie and beetle gets the same
     treatment). 1.0 is an *exact* restore of the authored value, which the whole
     baseline-caching design depends on; applying a multiplier twice from a cached
     baseline is idempotent where applying it to the live field would square it; a
     negative multiplier clamps to zero rather than sending a creature backwards.
     Knockback specifically: both force components scale together so the shove
     keeps its vanilla *angle* (scaling one alone would turn a knockback dial into
     a "beetles launch you upwards" dial) and an asymmetric prefab keeps its own
     ratio. Ragdoll specifically: **zero genuinely means "never knocked down"**,
     which falls out of vanilla's `RPCA_Fall` only ever *raising* the timer, so a
     zero-length knockdown can never satisfy that check.
   - **Zombie/beetle deaggro** (`ZombieDeaggroTests`): pins down the fact that the
     zombie dial's scale is **inverted** relative to every other multiplier in the
     mod — 1.0 is the toughest setting, not vanilla, because vanilla is "never
     deaggro" — since that's exactly what a later reader would "fix" back. Also:
     the 30s base at 1.0 is the game's own `Scoutmaster` constant; briefly breaking
     line of sight is *not* enough at the toughest setting (the maintainer's
     explicit design constraint); the two escape routes (sight and distance) work
     independently; out-of-range or NaN multipliers can never produce an instantly
     deaggroing zombie; and the deaggro distance stays well clear of the zombie's
     own 30-unit awareness range, so it can't give up at roughly the range it
     starts chasing from. The beetle half covers the **2026-07-29 regression** that
     live testing caught: its two extremes must produce behaviour more than 10×
     apart, and the suppression window must outlast a targeting re-check — without
     that the dial cancelled itself out and looked identical at 0.1 and 3.0.
   - **Creature knockouts** (`CreatureKnockoutTests`): durations clamp sanely, 0 is
     a real "leave vanilla alone" setting rather than an error, and the defaults sit
     below the spider's vanilla 5s with the beetle's below the zombie's (the shell).
     The load-bearing part is the **hard-throw gate, calibrated from real logged
     impacts rather than guessed**: five measured throws are baked in as data —
     23/26.3/30.6 m/s were judged too gentle and must fail, 36.6/42.5 were
     near-full-strength and must pass — so nobody can retune the 36 m/s threshold
     back across that gap without a test failing. Plus the distance gate (far throws
     rejected even at full speed, 0 meaning "no limit" rather than "must be
     touching") and the blowgun stun being far longer than a thrown item's.
   - **Wind on creatures** (`CreatureWindTests`): the other place the vanilla point
     isn't 1.0. Asserts that the zombie dial's vanilla is 1.0 while the beetle's is
     **0**, that 0 on the zombie dial is a real change rather than "leave it alone"
     (vanilla already pushes zombies at 0.6× a player's force), and that a NaN
     resolves to *vanilla* rather than to zero — a malformed value should leave the
     game as it was, not silently make zombies wind-immune. Beetle drift is
     proportional to the beetle's own walking speed, so a differently-scaled prefab
     drifts proportionally, and a negative speed can never produce reverse drift.

   - **Config defaults and the preset table** (`ConfigDefaultsTests`): guards the
     rule `docs/PRESETS.md` exists to enforce — **every balance default is the
     vanilla value**, so Custom-plus-untouched-settings is unmodded PEAK. It
     restates by hand what vanilla means for each setting (1.0 for a multiplier, 0
     for a removal fraction, off for a mechanic the game lacks) rather than
     re-reading the table, which is the only way to catch a Default cell edited to
     something that isn't vanilla. A setting that is neither listed nor explicitly
     exempt fails the test, so a newly added one can't slip past. The two exempt
     categories are pinned too: each **gated parameter** (a dial that only means
     anything once some other setting is on) must name a parent that really does
     default to off, and must itself ship a usable non-zero value — otherwise the
     mechanic would come up broken the moment a player enabled it. Also pins which
     preset-driven rows are deliberately vanilla on all four presets (so an
     unfinished row can't hide among them), and that `PresetValues` throws on
     Custom rather than quietly handing back Subtle's number.

   **The preset tests assert shape, not values** (`PresetResolutionTests` and the
   per-mechanic preset assertions). Since the numbers are re-tuned between play
   sessions via `docs/PRESETS.md`, pinning "Balanced is 0.25" would break the build
   on every tuning pass and the loop would stop being worth using. What is pinned
   is the direction of the scale — Subtle closest to vanilla, each later preset at
   least as forgiving, Tame strictly further than Subtle — plus anchors on values
   that are genuine design invariants rather than tuning choices (a fraction
   staying inside 0-1, a spore-area rate never reaching 0, a gated parameter's
   parent toggle being off under Subtle). Ties between neighbouring presets are
   allowed: that's a legitimate tuning outcome.

   **"Subtle is exactly vanilla" is not one of those invariants**, and asserting it
   per row was the single biggest source of false build failures — the 2026-07-30
   tuning pass broke five tests at once by giving Subtle a light trigger-radius
   shrink and a wide screen-shake cap, both perfectly legitimate. Subtle is the
   *lightest* preset, not a vanilla one. It is still anchored where the row's own
   design says so (spore clear time), but a new test should assume tuning may move
   it and assert the shape instead. Four `*_MatchesRoadmapTable` tests that pinned
   exact per-preset numbers outright were removed in that same pass for the same
   reason — they were the rule this section states, being broken in the one file
   that should have followed it hardest.

2. **Manual in-game loop** (this doc) — for anything only observable at
   runtime (feel, visual clutter, screen shake, actual spawn positions in a
   real level).

## Tuning balance values (the Phase 9 loop)

**Never edit balance numbers in code.** Every gameplay default and every
per-preset value lives in one table, [`docs/PRESETS.md`](PRESETS.md), which is
parsed into the C# the mod reads. (`General`, `Host` and `Debug` aren't in it —
nothing there is a balance value or preset-driven.) The loop is:

```bash
# 1. edit a cell in docs/PRESETS.md
# 2. regenerate the two .g.cs files from it
bash scripts/apply-presets.sh
# 3. build + deploy, then play
cd src/Fairoots && dotnet build -c Release -p:DeployToProfile=true
```

`bash scripts/apply-presets.sh --check` verifies without writing anything: it
fails if the checked-in generated files are stale, if a value in the table is
malformed (a bool that isn't `on`/`off`, preset columns half-filled, a new row
missing its trailing `<!--Id-->` comment), or if a setting exists in the table
that no code reads. Run it before committing a tuning
pass.

For tuning a *single* mechanic mid-session you don't need the loop at all: set
`Host/preset = Custom` and edit that mechanic's own config entry in-game (via
PEAKLib.ModConfig or the config file — turn `Debug/apply-changes-live` on
*before* loading into Roots so most settings apply immediately; it is off by
default and is read once per Roots load, so flipping it while already standing in
the biome does nothing until the next one). Under Custom every setting starts
at its vanilla value, so whatever you change is the *only* thing differing from
unmodded PEAK, which is what makes a single dial judgeable in isolation. Once a
value feels right, write it into the preset column it belongs to in
`docs/PRESETS.md` and regenerate.

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

### Spore-cloud translucency (`General/spore-area-cloud-opacity`, `General/spore-bomb-cloud-opacity`)

**Pre-req:** in a Roots run. Debug logging optional but useful — it adds a
`[SporeCloudOpacity]` line per pass (how many areas and particle systems were
thinned, at what multiplier), a one-time `[ParticleOpacity]` inventory per cloud
material (its shader, render mode, every property that shader declares, and
which one is being used as the opacity lever), and a one-time
`[SporeBombCloudOpacity]` dump of a live detonation's structure.

1. With both at their default (`0.35`), walk up to a mushroom spore cloud. It
   should still be obviously *there* from a distance — visible enough to route
   around — but thin enough to see terrain and other players through.
2. Walk into it and watch the moment the green Spores screen overlay comes up.
   **This is the whole point of the setting:** "inside, taking spores" and
   "standing next to it" must now look clearly different. If you still can't
   tell the two apart, lower the value.
3. Back out again — the overlay should visibly clear while the cloud stays
   drawn where it is.
4. Change the value **without leaving the run** (e.g. to `1.0`): every cloud in
   the level should snap to full vanilla density immediately, and back to thin
   when you set it low again — not progressively fainter each time, which would
   mean the authored baseline wasn't cached correctly.
5. Set off a spore bomb and repeat 1-3 for its temporary cloud
   (`spore-bomb-cloud-opacity`). Check the *whole* cloud, not just its first
   second: the detonation keeps spawning VFX after the initial burst, so a
   cloud that starts thin and then goes opaque means the re-apply interval
   isn't catching the late systems.
6. Confirm the hazard itself is unchanged: you should still take spores over
   the same area, at the same rate, from the same distance. This setting must
   never move the hazard — a cloud that looks smaller than it really is would
   be worse than an opaque one.
7. Multiplayer check: like the recolor, these are deliberately **not**
   host-authoritative. Set them differently on two clients in the same run —
   each should see their own value.

**Report back:** whether step 2 actually resolves the "am I in it?" ambiguity,
and whether the default 0.35 is too faint (clouds stop reading as landmarks) or
still too dense.

**If a cloud doesn't get thinner at all**, check the verbose
`[ParticleOpacity] material ...` line for that system. Particle shaders are Unity
assets, not code, so nothing in the decompile says whether a given one honours
per-particle alpha or exposes an opacity float of its own — that line lists
everything the shader actually declares and marks which property is being used,
so a no-op says exactly which lever is missing rather than requiring another
guess. (This is not hypothetical: the first version of this feature scaled only
per-particle alpha, which the spore areas' Shader Graph clouds ignore
completely.) Likewise, if a spore *bomb*'s cloud is untouched,
the `[SporeBombCloudOpacity] structure of a live detonation` dump says whether
the visible cloud is even a particle system (it may be mesh-based
`ExplosionEffect` orbs, which this feature does not reach).

### Persistent spores overlay in bomb clouds (`General/show-overlay-in-spore-bomb-clouds`)

**Pre-req:** in a Roots run, setting on (the default). Debug logging optional —
it logs each entry/exit with the reason.

1. Set off a spore bomb and stand in its cloud. The green spores overlay should
   come up and **stay** up the whole time you're in it, instead of only flashing
   each time the cloud ticks damage.
2. The per-tick damage flash should still read as a distinct spike on top of the
   steady overlay — it's a separate overlay layer the mod doesn't touch, so it
   should behave exactly as it does inside a mushroom spore cloud. **If it
   doesn't stand out enough, say so** — that's a dial worth adding, not
   something to live with.
3. Walk out. The overlay should fade out promptly, and it must not linger.
4. **Edge case worth its own check** — set off a bomb while standing *inside* a
   mushroom spore cloud. Entering the bomb's cloud must not make the already-up
   overlay dip or flicker, and walking out of the bomb's cloud (while still in
   the spore area) must **not** clear the overlay: the area's own warning only
   raises on the frame you enter it, so a wrong clear here would blank it for as
   long as you stand there.
5. Stand near the edge of a cloud, just outside where it damages you. The
   overlay should be off — it follows the radius that actually applies spores,
   which is smaller than the cloud's nominal range.
6. If `Spore-Bombs/cover-mouth-blocks-spore-bombs` is on, cover your mouth
   inside a bomb cloud: the overlay should drop, the same way it does in a spore
   area, since the cloud can't reach you.
7. Turn the setting off mid-run — the overlay should stop behaving persistently
   immediately, and vanilla flashing should be all that's left.

### Spore-cloud warning label (`General/show-spore-cloud-label`)

**Pre-req:** in a Roots run. The setting is **off** by default, so turn it on.

1. Walk into a mushroom spore cloud. "Breathing in spores!" should fade in
   between the top of the screen and the crosshair, in the game's own font, in
   the same pink/red as the Spores status, with a darker outline of the same
   hue.
2. Walk out — it should fade out promptly and leave nothing behind.
3. Set off a spore bomb and stand in its cloud: the same label, on the same
   terms. The label and the green overlay must never disagree — if one says
   you're in spores and the other doesn't, that's a bug, not a tuning question.
4. Check it against a bright background (looking at the sky) and a dark one
   (deep in the biome). The outline exists so pink text stays readable over a
   pink cloud, which is exactly the situation this label is shown in.
5. Confirm it never blocks clicks or shows in menus/pause — it has no raycaster
   at all, so anything to the contrary is worth reporting.
6. Turn the setting off mid-run: it should disappear immediately.

**Report back:** the wording (it's a placeholder pick, not a decided string),
the size, and the vertical position. The text is localized into all 14 languages
the game ships with, so it's also worth switching the game's language mid-run
once: the label should re-word itself without a scene reload, and the string
should still fit on one line (both labels have word wrap off by design).

### Bomb cloud VFX vs. its spore radius (`Spore-Bombs/spore-area-radius-multiplier`)

**Pre-req:** preset `Custom` (5) so the `Spore-Bombs` entries apply, in a Roots
run.

1. Set the multiplier well above 1 (e.g. `2.0`) and detonate a bomb: the visible
   cloud should be visibly bigger, and the distance at which you start taking
   spores should grow with it.
2. Set it well below 1 (e.g. `0.4`) and detonate another: visibly smaller, and
   the spore radius should shrink to match.
3. The check that matters is **agreement**, not size: walk to the visible edge of
   the cloud at each setting and confirm the spores overlay (step 1 of the
   section above) turns on roughly where the cloud looks like it ends. A cloud
   that looks smaller than it really is would be worse than no scaling at all.
4. This is applied per detonation at spawn time, so it only affects bombs set off
   *after* the change — a cloud already in the air keeps the size it was born
   with. That's expected, not a bug.

### Live vs. level-load-only setting updates (`Debug/apply-changes-live`)

**Pre-req:** debug logging on, in a Roots run, preset set to `Custom` (5) so
the `Spore-Bombs` entries are actually in effect.

0. **The flag is read once, as a Roots biome loads.** That is the whole design:
   what you had set when the biome loaded is how that biome behaves, so switching
   it *on* mid-run must do nothing at all. Test that first - stand in Roots with
   it off, turn it on, change `knockback-multiplier`, trigger a spore bomb: the
   old value must still apply. Then leave and come back for the steps below.
1. Turn `apply-changes-live` on, then load into Roots. Change
   `knockback-multiplier` while standing there (e.g. via PEAKLib.ModConfig)
   and trigger a spore bomb - the new value should apply to that very
   detonation. Change `trigger-radius-multiplier` too - already-placed spore
   bombs should visibly resize immediately, no reload needed.
2. Leave Roots, set `apply-changes-live` back to `false` (its default), and load
   into Roots again. Change `knockback-multiplier` and trigger a spore bomb - it
   should use whatever was in effect when the biome loaded, not the new value.
   Same for `trigger-radius-multiplier` - existing hitboxes should stay exactly
   the size they already were, not resize.
3. Leave Roots and reload into a fresh Roots level: the new
   `knockback-multiplier`/`trigger-radius-multiplier` values should now be in
   effect, confirming the freeze only lasts until the next Roots load.
4. Confirm the rest of the `Debug` section (e.g. `keep-vanilla-trigger-radius`)
   and the flat settings that are always immediate by design
   (`disable-wind-entirely`, `backpack-always-immune`, `recolor-spore-bombs`)
   still apply instantly regardless of `apply-changes-live`'s value.

**Report back:** whether step 0's mid-run switch-on was correctly ignored,
whether step 1's changes applied instantly, whether step 2's changes were
correctly ignored until step 3's reload, and whether step 4's debug toggle stayed
live throughout.

### Foliage removal (`Spore-Bombs/enable-foliage-removal`)

**Pre-req:** debug logging on, a fixed `seed`, and `Debug/show-removed-spore-bomb-
markers = true` (the F10 overlay tags every removal `FOLIAGE` or `SEEDED`). This
setting only applies at Roots level load, so each change needs a fresh run.

Note the polarity: this is preset-driven and **on under every preset 1-4**, so
test it either by switching to Custom (where its default is off, like every other
setting) or by flipping it under Custom.

1. On (any preset 1-4, or Custom with it enabled): the level-load line should
   read `[SporeBombCull] N candidate(s): removed M (foliage=X, seeded=Y, ...)`
   with `X > 0`, and the overlay should show `FOLIAGE` markers sitting in
   ferns/bushes.
2. Off (Custom, untouched): the log should now read `foliage=off`, no `FOLIAGE`
   marker should appear anywhere, and you should be able to find at least one
   spore bomb genuinely hidden inside foliage still in the level.
3. **The important check — total removals must not drop.** With the same
   non-zero `cull-fraction` in both runs, `M` should be the same (the seeded pass
   absorbs the whole target), only the split changes. A smaller `M` with foliage
   removal off is a bug.
4. Same seed twice with the setting on: identical bombs removed both runs
   (`SEEDED` markers in the same places). Change the seed: a different set, same
   count.

**Report back:** whether the count held steady across the toggle, and whether
leaving camouflaged bombs in actually feels as unfair as expected (this switch
exists to be able to answer that).

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

**Note:** with `Wind/prevent-wind-ragdoll` on (the default under every preset —
see the next section) the clamp is redundant, because full control already beats
any partial floor. Turn `prevent-wind-ragdoll` **off** first if you want to test
the clamp on its own.

### Wind can't ragdoll you (`Wind/prevent-wind-ragdoll`)

**Pre-req:** debug logging on, in a Roots run, default `prevent-wind-ragdoll =
true` and `fall-camera-dampen-window-seconds = 1.5`.

1. Stand near an exposed ledge and let a gust push you off. You should stay
   **fully in control** on the way down — an upright, animated fall, not a
   flailing ragdoll — with enough control to grab a wall or fire a Rescue Hook.
2. Walk off the same ledge yourself with **no wind active**: this must ragdoll
   exactly like vanilla. The mechanic is scoped to falls wind actually caused,
   and a self-inflicted fall isn't one.
3. Get pushed near the edge, wait out the gust (longer than 1.5s), then jump —
   also vanilla, confirming the recency window is what gates it.
4. Turn the setting **off** and repeat step 1: wind should ragdoll you again,
   softened only partway if the preset sets a `fall-camera-dampen-clamp`
   (every preset sets one; `0` would mean full vanilla spin). Read the current
   numbers off `docs/PRESETS.md` rather than trusting a number quoted here.
5. Being ragdolled by something that *isn't* wind (a beetle hit, a zombie bite,
   fall damage on landing) must be completely unaffected in every case above.

**Report back:** whether keeping control actually converted "blown off a ledge"
from a death sentence into a recoverable moment, and whether full immunity feels
right for every preset (including Subtle) or wants to be preset-gated after all.

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
   *visually* as well: the cloud particles **and** the whole mushroom-tree prop
   in the middle of it, colliders included — the emitter mushroom is part of the
   hazard here, so a mushroom cap you could previously stand on goes with it
   (confirmed intended, 2026-07-27). Standing where a cloud was should apply no Spores and show no
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
4. Walk to a couple of removed coordinates: no cloud, no mushroom, no spores.
   The mushroom tree itself is part of what's removed (see the disable-switch
   section above), so the spot should be bare.
5. With a non-zero fraction *and* `disable-spore-areas` on, then toggling that
   switch back off: only the areas the seed kept should reappear — the
   seed-removed ones must stay gone.

**Report back:** whether Generous's 20% / Tame's 35% feel like the right amount
of thinning, and whether the cluster-first choice reads as sensible on the
ground (are the *right* clouds gone — the overlapping ones — or does it feel
arbitrary?).

### Spore areas: radius (`Spore-Areas/radius-multiplier`)

**Pre-req:** debug logging on, `preset = Custom` (presets 1-4 use their own
values: 1.00 / 0.85 / 0.70 / 0.55). Live-updatable, so stand at a cloud and
change it while watching.

1. Try something obvious in both directions (2.0, then 0.4). The **visible**
   cloud must grow/shrink to match, and the point where spores start ticking
   should move with it (walk in from outside).
2. The emitter mushroom must **not** change size. Structurally confirmed
   (2026-07-27): the two `"Particles"` systems that get scaled are children of
   the `"Spore Cloud"` node, while the mushroom meshes are siblings of it — so
   only the gas scales. Worth an eyeball anyway.
3. `radius-multiplier = 1.0` must be pixel-for-pixel vanilla, including after
   having been set to something else first and back (every value is applied from
   a cached vanilla baseline, so repeated changes can't compound).
4. Check the log line: `multiplier=..., N spore area(s) resized (e.g. radius 11
   -> X world units, 17.6m -> Ym), 2N cloud VFX transform(s) scaled` — the VFX
   count should be exactly twice the area count (two particle systems each). If
   it ever says `had no VFX to scale`, the prefab layout changed.

**Report back:** whether Balanced's 0.85 is a meaningful improvement or too
timid, and whether the resized cloud still *looks* right (a heavily shrunk cloud
shouldn't look like a sparse puff, a heavily enlarged one shouldn't look thin).

### Spores: clear time (`Spores/clear-time-multiplier`)

**Pre-req:** debug logging on, `preset = Custom` (presets 1-4 use 1.00 / 0.70 /
0.65 / 0.45 — Balanced was raised from 0.85 on 2026-07-30 after the first live
pass). Live-updatable, but the value that matters is the one in effect while the
meter is actually draining.

**Read the log line first — it's the whole baseline for this test.** Once per
session, at Info level:

```
[Spores] vanilla spore recovery: 3.00s delay + 0.0500/s = 23.0s to clear a full
meter. At x0.500 that becomes 11.5s.
```

Those two field values are serialized on the player prefab and appear *nowhere*
in the decompiled game code, so this line is the only statement of what vanilla
recovery actually is. If it instead warns that `sporesReductionPerSecond is 0`,
the dial is inert by design (nothing to scale — see `Core/SporeStatusTuning.cs`)
and the rest of this section doesn't apply.

1. At `1.0`, get a decent amount of spores, leave the cloud, and time the meter
   emptying with a stopwatch. It should match the log's vanilla figure, scaled
   for however full the meter was.
2. Set `0.5` and repeat from a similar meter level: it must take **about half**
   as long, including the pause before the meter starts moving at all — that
   pause is scaled too, so it should visibly shorten, not stay put.
3. Set `2.0` and repeat: about twice as long, with a noticeably longer pause.
4. Back to `1.0` and time it again — it must match step 1 exactly (both fields
   are applied from a cached vanilla baseline, so repeated changes can't
   compound and 1.0 always restores true vanilla).
5. Verbose lines show the per-character write:
   `"Player" sporesReductionPerSecond 0.05 -> 0.1, cooldown 3.00s -> 1.50s`.
   Rate and cooldown must move in **opposite** directions.

**Report back:** the real vanilla clear time from the log line (it goes in the
config description, which currently doesn't quote a number), and whether
Balanced's 0.70 now lands right — and if so, whether Generous (0.65) needs
widening, since one live pass has already squeezed the two to 5 points apart.

### Spores: global build-up (`Spores/build-up-multiplier`)

**Pre-req:** debug logging on, `preset = Custom` — **no preset changes this
dial and its own default is 1.00**, so out of the box it does nothing at all and
that is not a bug (it stacks on top of the per-hazard dials, which is exactly why
it ships neutral; the presets reduce build-up per hazard instead). Reads its value
fresh on every application, so changes apply instantly with no reapply.

1. Set `0.5` and stand in a spore area. The meter must fill at **about half** the
   usual pace. Verbose log (throttled to one line every 2s):
   `[Spores] build-up x0.500: +0.01250 -> +0.00625 Spores`.
2. Confirm it covers **every** source, not just areas — this is the point of the
   setting. Walk into a spore bomb's cloud, and take a zombie bite (the bite's
   lingering affliction keeps adding spores for a while afterwards; that should
   be reduced too).
3. Set `0` — you must be unable to gain any spores at all from any source, while
   the clouds themselves are still there and still visible.
4. Set `1.0` — exactly vanilla, and the verbose line above should stop appearing
   entirely (the patch returns early rather than multiplying by 1).
5. **Compounding check** (the intended behaviour, worth confirming rather than
   assuming): set this to `0.5` *and* `Spore-Areas/status-rate-multiplier` to
   `0.5`. Inside a spore area the meter should fill at roughly a **quarter** of
   vanilla, not a half — the two dials multiply.

**Report back:** whether one global knob is actually the useful shape here, or
whether the per-hazard dials cover it well enough that this mostly adds
confusion.

### Cover your mouth vs. spore areas (`General/cover-mouth-key`)

**Pre-req:** debug logging on, in a Roots run, standing in a spore cloud. Default
key `X`, hold mode. Transitions log as
`[CoverMouth] local player covered/uncovered their mouth (reason); N spore area(s)
with parked tick progress`. The animation is **not implemented yet**, so the log
is currently the only feedback that it engaged.

1. Hold the key in a cloud: the Spores meter stops climbing and the green screen
   filter goes away. Release and both resume.
2. While holding it, try to climb a wall, pick something up, grab a rope, and
   switch slots/backpack — all refused, all working again the instant you let go.
3. Hold an item from a slot and press the key → it's pocketed. Pick up a fourth
   (temporary, in-hands) item and press the key → it's dropped.
4. Grab a wall and *then* press the key → nothing happens (log:
   `holding onto something (climbing)`). This direction is deliberate — covering
   must never drop you off a wall.
5. Hold it with the stamina bar nearly empty → cuts out at zero (log:
   `out of stamina`).
6. `cover-mouth-hold = false` → one press on, one press off, and holding the key
   doesn't flicker it.
7. **The anti-exploit check.** Spam the key on a fast cycle (~300ms) inside a
   cloud. Every `uncovered` line must report **1** parked area (0 means the
   mechanic is broken) followed by `resumed spore tick progress at Xs`, and X
   should climb across taps and wrap past 0.5 as ticks actually land. The meter
   must visibly fill roughly in proportion to the time the key is *up*. Verified
   2026-07-27: 66 releases, 66 resumes.
8. Hold the key, walk fully out of the cloud, come back, release → behaves like a
   fresh entry, not an instant hit.
9. Host sets `enable-cover-mouth = false` mid-run (under Custom — presets 1-4
   have it on) → the key goes dead for everyone immediately, cancelling any cover
   in progress. A non-host setting it has no effect; a player setting their own
   `cover-mouth-key = None` opts out regardless of the host.
10. Spore *bombs*: with `Spore-Bombs/cover-mouth-blocks-spore-bombs` off (the
    default) a bomb should still give you spores while covering. Turn it on and
    set one off while covered — no spores at all, though the blast should still
    knock you around. Then keep covering, and uncover/re-cover several times over
    the following ten seconds or so: still no spores. That last part is the real
    test — the cloud is one AOE re-exploding on a timer, and an earlier version
    blocked only its first few seconds. Any `[SporeSource]` line in the log means
    a spore got through while covered, which should be impossible.

11. **The pose.** Both hands should come up over your mouth with a real
    hand/finger shape, and *only* the arms should change — walk, jump and look
    around while covering: legs and head must animate completely normally, and
    releasing the key must snap straight back with nothing lingering.
12. Restart the game and check the pose looks the same as it did last session
    (it used to bake in whatever idle animation was playing at the first cover),
    and that neither hand drops out while holding an item or wearing cosmetics.
13. For tuning, set `Debug/cover-mouth-pose-preview = true` to hold the pose on
    permanently — it's visual only, so the mechanic still behaves normally
    while it's on. The seven `cover-mouth-pose-*` values apply live, except the
    clip and its frame, which force a re-capture when changed.

**Report back:** whether 0.03 stamina/second feels like the intended "small
amount," and whether the immunity feels worth the hands-busy cost.

### Creatures: disable switches (`Creatures/disable-zombies` / `-beetles` / `-spiders`)

**Pre-req:** debug logging on, in a Roots run. Each level load and config change
logs `[Creatures] level load: disable-zombies=off, disable-beetles=off (15 found,
0 newly hidden, 0 restored), disable-spiders=off (90 found, ...)`.

1. Flip each switch **on** mid-run. Beetles and spiders should vanish
   immediately; zombies should despawn and none should spawn again.
2. Flip each back **off** — beetles and spiders return in place, with `N
   restored` matching what was hidden.
3. With `disable-spiders` on, look up at a ceiling a spider was on: **no web
   stub should be left hanging.** That's the specific regression — a spider stays
   in `SpiderState.Dropped` through its whole retreat and `DisplayRope` re-enables
   its LineRenderer every frame, so this needs `LateUpdate` suppressed, not just
   the renderer disabled once.
4. Kill a teammate and let them turn into a zombie with `disable-zombies` **on**:
   that zombie must **not** be removed. Only NPC zombies are affected.

### Creatures: speed, knockback and ragdoll dials

1. Set `zombie-speed-multiplier` and `beetle-speed-multiplier` to something
   obvious (0.3, then 2.0). Both creatures should visibly change pace — including
   a zombie's *sprint*, since the sprint is a multiple of the same field.
2. Set `beetle-knockback-multiplier` to 0 and let a beetle hit you: it should
   still hit and still knock you off your feet, but not move you.
3. Set `creature-ragdoll-multiplier` to 0, then take a beetle hit and a zombie
   bite: you should still take the shove, the injury and the spores, but **never
   lose control**. Then 2.0 to confirm it goes the other way.
4. With `apply-changes-live` on, all of the above should apply mid-run without a
   level reload.

**Report back:** any dial that appears to do nothing. The `[Creatures] speed
reapply: zombie x… (N live), beetle x… (N live)` line confirms the pass ran and
how many creatures it reached.

### Creatures: deaggro (`Creatures/zombie-deaggro-multiplier` / `beetle-deaggro-multiplier`)

**These two are close to unverifiable by eye** — a creature that stops chasing
looks the same whether this mod's rule did it, vanilla lost line of sight, or it
just wandered off. So verify from the log, not by feel. With debug logging on you
get an aggro line, a 1/second status heartbeat while chasing, and a deaggro line
that **names the cause**:

```
[Aggro] zombie "Zombie (NPC)" chasing You - distance=18.4m/102.0m, unseen-for=25.1s/25.5s
[Aggro] zombie "Zombie (NPC)" DEAGGROED You - cause: Fairoots deaggro rule - stayed unseen 25.6s (limit 25.5s); ...
[Aggro] beetle "Beetle" DEAGGROED You - cause: vanilla rule (lost sight, out of range, or target downed); ...
```

1. **Zombie, `1.0`:** it should be genuinely stubborn — 30 seconds unseen or
   ~120m. Ducking behind it for a few seconds must *not* work.
2. **Zombie, `0.1`:** break line of sight and it should give up in ~3s **and not
   re-acquire you**. If it re-aggros within 10s the re-acquisition gate has
   broken (`TryLookForTarget` re-picks the nearest player with no distance limit).
3. **Zombie, `zombie-deaggro-enabled` off:** vanilla — it never gives up.
4. **Beetle, `0.1` vs `3.0`:** at 0.1 it should give up almost at once and then
   visibly lose interest for ~5s; at 3.0 it should keep coming even after you
   break line of sight.
5. **Check the cause field.** If a beetle stops chasing at 0.1 and the log says
   `vanilla rule`, then this dial didn't do it and the result is coincidence.

### Creatures: spider strike indicator (`General/show-spider-warning-label`)

Off by default. Turn it on and walk under spiders.

1. The label should appear as a spider begins dropping on **you** — a teammate
   being jumped on across the level must not raise it.
2. If it misses, the label should clear about a second after the spider bottoms
   out — **well before it climbs back up.** This is the regression to watch:
   `SpiderState.Dropped` persists through the whole retreat, so the window is the
   descent plus a second and deliberately excludes `spiderWaitTime`. The log
   prints the numbers: `warning for 1.43s (descent 0.43s + 1s linger; its
   post-landing hang of 6s is excluded)`.
3. With `disable-spiders` on, no warning should ever appear.

### Creatures: thrown-item knockouts and blowgun

1. **Gentle tosses must do nothing** to either creature. Rejections log the
   measured speed next to the threshold, which is how to tune it:
   `ignored "Rock" (too soft): 26.3/36.0 m/s, 4.2m from thrower/12.0m`.
2. **A charged throw from close range** should knock a beetle onto its back
   (~2s, then it rights itself with its own flip animation) and a zombie down
   (~4s plus a few seconds to get up).
3. **A hard throw from far away should be rejected** — the log says `thrown from
   too far away`. Both gates exist so the knockout costs a charged throw *and*
   closing the distance.
4. **Blowgun dart:** a zombie should **die** (and leave a skeleton, exactly as it
   does on its own after two minutes); a spider and a beetle should go down for
   ~60s.
5. **Stun markers:** the "out cold" particle should last the *whole* stun and
   **stop when it ends** — the regression here was `StopEmitting` letting live
   particles outlast the stun, so a 5s spider stun appeared to run a second 5s
   cycle while the spider was already active again. Check both a 5s thrown-item
   stun and a 60s dart stun. On a beetle the marker should sit over its **head**,
   not its body, and be visible to other clients too.

### Creatures: wind (`Creatures/zombie-wind-multiplier` / `beetle-wind-susceptibility`)

**Note the two dials have different vanilla points** — 1.0 for zombies (the game
already pushes them at 0.6× a player's force) and **0** for beetles (they're
wind-immune by construction, since `Mob.FixedUpdate` zeroes their velocity every
tick).

1. In a storm, set `zombie-wind-multiplier` to 4.0: zombies should get shoved
   around noticeably. The throttled log line
   `[Creatures] wind on zombie "…": windForce 20 -> 80` confirms it's applying —
   **if that line never appears at all, zombies aren't receiving wind and the
   patch needs a different approach.**
2. Set `beetle-wind-susceptibility` to 2.0: beetles should slide along the ground
   with the wind, and should **stay on the ground** rather than being shoved
   through it or juddering.
3. A knocked-out or tumbling beetle must **not** drift — the push only applies
   while it's walking, because anything else is real rigidbody control.
4. With `Wind/disable-wind-entirely` on, neither creature should be affected.

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
6. **Creatures (this was broken until 2026-07-30 — worth re-testing
   specifically).** With both players already in a Roots level, host flips
   `Creatures/disable-beetles` and `Creatures/disable-spiders` on: the beetles
   and spiders must vanish on the **non-host's** screen too, within a moment and
   with no reload. Flip them back off and they must come back for both. Then
   have the non-host flip the same switches themselves — nothing should change
   for anyone, since only the host's value counts. Repeat for
   `Creatures/zombie-speed-multiplier` (a host change should be visibly felt by
   the client on the next chase).
7. **No per-client exceptions left (changed 2026-07-30).** The
   wind-preceded-fall settings used to be deliberately per-client and no longer
   are: with the host on `fall-camera-dampen-clamp = 0` /
   `prevent-wind-ragdoll = false`, a non-host who sets both generously for
   themselves should still ragdoll exactly like vanilla when wind blows them off
   a ledge. Their own values should start applying the moment they become the
   host (step 5).

**Report back:** whether the non-host client's spore bombs/wind genuinely
matched the host's config (not their own), whether live host changes
propagated to the client promptly (creatures especially), and whether
disable-wind-entirely affected both players.

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
