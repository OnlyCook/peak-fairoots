# ROADMAP — Fairoots

> Makes PEAK's Roots biome more fair and balanced, through a seed-deterministic
> preset system. **Fully open source, MIT licensed.** No paid/monetized
> component of any kind.

**Status:** Repo scaffold only (BepInEx plugin skeleton, packaging pipeline,
test project wired up). **No gameplay code yet.** This file is the researched,
structured version of `OVERVIEW.md`'s original wishlist — read that first for
the maintainer's original framing/priorities, then this file for the
architecture and phased plan. Technical findings behind every design decision
below (decompiled game classes/methods/fields, exact file:line references)
live in `RESEARCH.md` (gitignored, local-only — not shipped, not public).

**Last updated:** 2026-07-19 (session 1, repo setup + research).

---

## Design premise (why this mod)

Roots is PEAK's second biome, and it swings hard between "beaten without
touching anything but tree leaves" and "why does this game hate me" runs.
Subjectively, this isn't because Roots is *harder* than it should be — it's
because a handful of its hazards (spore bombs, spore clouds, wind, zombies,
beetles) lean heavily on raw randomness with too few player-facing mechanics
to counter them, so a bad roll just *happens to you* with no way to have
played around it. Fairoots' job is narrow on purpose: rebalance those specific
hazards, add a couple of small counterplay mechanics, and do it all through a
**seed-bound, deterministic** system rather than adding more randomness on
top of randomness.

## Seed & determinism (the mod's core architecture — read this first)

**Every decision Fairoots makes that affects what spawns, what gets removed,
or what odds apply — most importantly, which spore bombs get culled — is
bound to a seed the player sets in config.** Same seed + same Roots level
load = identical result, every single time. This is not a "nice to have"
config toggle, it's the mod's defining feature (see `OVERVIEW.md`'s framing
and `CLAUDE.md`'s "Seed determinism is non-negotiable" rule for the
maintainer-facing version of this same point).

**Research finding that reshaped this design (full detail in `RESEARCH.md`
Q1–Q5):** PEAK itself has no native, exposed "level seed" that
deterministically drives prop placement. The game's own spore-bomb/hazard
placement runs off Unity's shared global RNG stream, never explicitly
reseeded before it runs, with no cross-client synchronization for that
specific step (a few *other* systems in the game, like the wind timer and a
puzzle-room shuffle, *do* use a real host-seed-then-replicate pattern — prop
placement just isn't one of them). Concretely: **there is no existing
"same seed → same spore bombs" behavior in vanilla PEAK to hook into or
preserve.** This actually simplifies the mod's job — it isn't intercepting or
replaying an existing deterministic system, it's introducing determinism that
doesn't natively exist, entirely within its own code, with no risk of
desyncing anything the base game does downstream.

**Architecture:**

1. Fairoots never reads from or writes to Unity's own RNG stream. No
   `UnityEngine.Random.*` call, ever, anywhere in this mod's code — not even
   a "harmless peek."
2. Every mechanic's spawn/culling decisions are made **after** the game has
   already finished its own native placement for that area (a Harmony
   postfix, not a prefix or replacement) — Fairoots reads the
   already-spawned scene, decides what to do with it, and removes/adjusts
   from there.
3. Each decision gets its own independent, fresh `System.Random` (or
   equivalent hash), seeded from a combination of: the player's configured
   seed value, an identifier for which mechanic is deciding (so different
   mechanics never accidentally correlate), and a stable, already-placed
   world-position (rounded to an integer grid) or other stable identity of
   the specific object in question. No shared/sequential RNG instance is
   reused across multiple decisions — iteration order over a scene isn't a
   guaranteed-stable thing to depend on, only the final position is.
4. Because the decision function's only inputs are "the seed the player
   typed in" and "where the game placed this object," it is naturally
   **multiplayer-consistent** (every client sees the same objects at the
   same positions and reaches the same decision, no networking/RPC needed
   for the mod's own logic) and **fully unit-testable without a game
   install** (pure `(seed, mechanic, position) → decision` function, no
   `MonoBehaviour`/Unity types involved — see "Testing strategy" below).

## Presets

Four presets, numbered 1 (lightest touch) through 4 (heaviest). **Preset 2 is
the default.** Presets are **non-destructive**: any per-mechanic setting the
player has explicitly touched always overrides whatever the active preset
would otherwise set for that mechanic — applying/switching a preset never
silently clobbers a hand-tuned value.

Exact numeric values below are **starting targets, not final** — several of
the underlying vanilla defaults are Unity scene/asset data rather than
compiled code (see `RESEARCH.md`'s per-mechanic "open questions"), so the
precise vanilla baseline for things like wind force or spore-bomb spawn
weight needs a runtime logging pass before the exact multiplier can be
locked in. The columns below express intent (direction and rough magnitude)
per preset; a testing/tuning pass against real gameplay is expected to adjust
the specific numbers before each preset ships (per the maintainer's framing —
these are "difficult to eyeball," extracted through testing, not guessed
once and left alone).

| Mechanic | Preset 1 — Subtle | Preset 2 — Balanced (default) | Preset 3 — Generous | Preset 4 — Tame |
|---|---|---|---|---|
| New: climb-to-counter-wind | ✅ on | ✅ on | ✅ on | ✅ on |
| New: cover-mouth vs. spore areas | ✅ on | ✅ on | ✅ on | ✅ on |
| Wind force / frequency | −10% | −20% | −40% | −65% |
| Wind: items/backpack immunity | backpack only | backpack + reduced item force | backpack immune, items −60% | backpack + items fully immune |
| Wind: obstacle occlusion | off (vanilla) | on, coarse | on, tuned | on, generous radius |
| Wind: fog-while-active density | vanilla | −25% | −50% | −80% |
| Spore bomb cull rate (seeded) | 0% (none culled) | −25% | −50% (OVERVIEW's literal ask) | −75% |
| Spore bomb trigger radius | vanilla | −15% | −30% | −45% |
| Spore bomb knockback/explosion force | vanilla | −20% | −40% | −60% |
| Spore bomb screen-shake distance cap | vanilla (~75m, unconfirmed) | 30m | 20m | 10m |
| Spore bomb particle/VFX count | vanilla | −25% | −50% | −65% |
| Spore area radius | vanilla | −15% | −30% | −45% |
| Spore area lethality (status/sec) | vanilla | −15% | −35% | −55% |
| Spore area screen-filter opacity | vanilla | −20% | −40% | −60% |
| Wind disperses spore areas | if not already vanilla behavior, on | on | on | on, generous |
| Zombie/beetle move speed | vanilla | −10% | −20% | −35% |
| Zombie deaggro (currently: never) | none (still never, matches vanilla) | deaggro past a large distance | deaggro past a moderate distance | deaggro quickly past a short distance |
| Beetle knockback force | vanilla | −20% | −35% | −50% |
| Spider attack audio telegraph | ✅ on (uses existing ~0.25s pre-attack window) | ✅ on | ✅ on, slightly earlier | ✅ on, earliest |
| Full zombie disable option | available, off by default | available, off by default | available, off by default | available, **on by default** |
| Honeycomb / stove spawn-weight nudge | none | slight increase | moderate increase | generous increase |

Preset 2's specific numbers are meant to represent "the tuning the maintainer
would ship if this were the base game's own balance pass" — i.e. the most
testing-intensive preset to get right, since it's the one most players will
actually experience (it's the default). Presets 1/3/4 are meant to be cheap
derivatives of the same underlying per-mechanic sliders (scaled up/down from
preset 2's values), not independently hand-tuned from scratch, so most of the
tuning effort concentrates on preset 2.

## Mechanic notes (implementation feasibility, from `RESEARCH.md`)

Brief summary only — see `RESEARCH.md` for exact classes/fields/citations.

- **Wind**: single core class owns nearly everything (force, timing,
  item/character distinction, an already-existing-but-currently-unused
  obstacle-raycast option, and a climbing-stamina-multiplier field that's
  almost exactly the hook the counter-wind-by-climbing mechanic needs).
  Backpack immunity and fog-during-wind scaling are both simple, isolated
  patches. The "screen goes crazy while falling in wind" complaint does not
  have a dedicated wind-shake code path — it's most likely a combination of
  existing generic fall-shake calls, needs a runtime logging pass to
  pinpoint before it can be scoped precisely.
- **Spore bombs**: no dedicated class exists — the hazard is built from
  generic, reusable components (an area-of-effect/explosion component, a
  separate particle-orb VFX spawner, a generic proximity-screenshake
  component). This means most of the levers are plain public fields,
  directly patchable once a runtime logging pass confirms which specific
  prefab/GameObject the Roots spore bomb actually is (open question, shared
  with the achievement-spawn item below — several mechanics need the same
  one-time "log what actually spawns in Roots and what components it has"
  pass before implementation starts).
- **Spore areas** (the status-effect gas clouds — a different hazard from
  spore bombs, despite the similar name) run through a single generic
  radius-based hazard-zone component with public radius/lethality/falloff
  fields. Wind-suppression of spore areas may already partially exist in
  vanilla for some instances — needs the same runtime confirmation pass to
  know whether this is "tune an existing interaction" or "build a new one."
- **Creatures**: zombies currently have **no distance-based deaggro at
  all** once they've targeted a player (confirmed absent in code, not just
  hard to find) — this is the one creature change that's genuinely new
  logic rather than a field tweak. Beetles already deaggro past a fixed
  sleep distance, so tuning that existing distance is simpler. A
  full-zombie-disable option is a very cheap win — the spawner system
  already has an internal mechanism that discards most placed zombie
  spawners at random on level load, and a global cap that's trivial to zero
  out. Spiders already have a ~0.25-second animation-trigger window before
  their attack lands, which the new audio-telegraph mechanic can hook
  directly instead of inventing attack-phase detection from scratch.
- **Achievements** (honeycomb/Gourmand, stove/Cryptogastronomy): the
  achievement logic itself has no RNG in it — the actual rarity is entirely
  in level-gen spawn-pool weight data, which lives in Unity scene assets, not
  compiled code, and isn't visible to static decompilation at all. Any
  spawn-weight nudge has to work by intercepting the game's own
  weighted-random item-selection call and re-biasing it post-hoc (or
  re-rolling on a miss), not by editing a static number, since no such
  static number exists in code to edit.

## Testing strategy

Playing a change out by hand cannot prove a spawn-count or
seed-reproducibility claim — a person can't reliably tell "was that the same
6 spore bombs as last run." Every mechanic that culls/keeps entities or
otherwise makes a probabilistic decision ships with **automated unit tests**
in `tests/Fairoots.Tests/` (`dotnet test`, no game install required) that
assert on the actual outcome, not just "it didn't crash." This is possible
specifically *because* of the architecture above: the decision logic is pure
C# — `(seed, mechanicId, position) → keep/cull` — with zero Unity/BepInEx
dependency, so it's testable in complete isolation from the Harmony patch
that calls it and from the game itself.

Minimum bar per RNG-touching mechanic:

- **Same-seed reproducibility**: the same seed + the same input set (e.g.
  the same list of spore-bomb positions) produces an identical set of
  keep/cull decisions across repeated runs of the test — not just the same
  *count*, the same *specific* objects.
- **Count/ratio correctness**: e.g. a "cull half" preset value should
  produce a post-cull count within one of exactly half the input count,
  verified across a range of input sizes including edge cases (0 candidates,
  1 candidate, odd counts, very large counts for a statistical
  sanity-of-distribution check).
- **Different-seed variance**: two different seed values should, with very
  high probability, produce different decisions on the same input — a
  regression guard against the sub-RNG accidentally being wired to a
  constant or to something that isn't actually the configured seed.
- **Isolation guard**: nothing in the mod's own code calls
  `UnityEngine.Random.*` — enforced as a matter of code review discipline
  (see `CLAUDE.md`) backed by, where practical, a simple static-analysis-style
  test (e.g. reflection over the built assembly's IL, or just a grep-based
  check run in CI) rather than a runtime test, since the whole point is that
  the mod's logic never touches that stream in the first place.
- **Preset non-destructiveness**: applying/switching a preset never
  overwrites a config value the player has explicitly changed — tested at
  the config-resolution-logic level, not by clicking through the UI.

Anything only observable at runtime (visual clutter, actual screen-shake
feel, whether an obstacle-occlusion raycast looks right in a real level)
stays in the manual loop documented in `docs/TESTING.md`, which grows one
checklist entry per shipped mechanic, mirroring the format the maintainer's
other PEAK mods already use.

## Reference material (gitignored, local-only research inputs)

- `~/Projects/GitHub/peak-checkpoint-save/` and
  `~/Projects/GitHub/peak-sense-of-direction/` — the maintainer's other two
  PEAK mods, used throughout this project as the reference for project
  structure, packaging conventions, coding style, and (for
  sense-of-direction specifically) precedent on how to document open
  research questions inline without letting them block a roadmap.
- `scratch/decomp/game/` — full decompile of the currently-installed PEAK
  build. See `CLAUDE.md` for how to regenerate it after a game update.

## Phased implementation plan (proposed — not started)

1. **Phase 1 (done):** repo scaffold, packaging pipeline, empty plugin,
   test project wired up, research complete.
2. **Phase 2:** seed/preset config plumbing — the seed field, the preset 1-4
   selector, the pure-C# deterministic-decision helper (with its full unit
   test suite from day one, before any Harmony patch consumes it), and the
   non-destructive preset-application/override-resolution logic (also
   tested standalone).
3. **Phase 3:** one-time runtime logging pass — a temporary debug harness
   (not shipped) that, run once in an actual Roots level, dumps the exact
   `GameObject` names/component signatures/prefab identities of spore
   bombs, spore areas, and the honeycomb/stove spawn pools. Resolves the
   open questions in `RESEARCH.md` that block precise implementation of
   Phase 4+. (candidate for the AssetRipper/Unity-asset-extraction route
   mentioned throughout `RESEARCH.md`, if runtime logging alone doesn't
   resolve everything, e.g. exact spawn-pool weights.)
4. **Phase 4:** Spore Bombs — cull rate (the mechanic the seed system exists
   for), trigger radius, knockback, screenshake range, particle count,
   bush/grass placement avoidance if found to be missing.
5. **Phase 5:** Wind — force/frequency scaling, backpack/item immunity,
   obstacle occlusion tuning, fog scaling, the new climb-to-counter-wind
   mechanic.
6. **Phase 6:** Spore Areas — radius/lethality/opacity scaling, wind
   interaction, the new cover-mouth mechanic.
7. **Phase 7:** Creatures — zombie deaggro (new logic), zombie/beetle speed
   and knockback scaling, full-disable option, spider attack telegraph
   audio.
8. **Phase 8:** Achievement spawn-rate nudges (honeycomb, stove) — lowest
   priority per `OVERVIEW.md`'s own parenthetical framing of these two
   bullets.
9. **Phase 9:** preset value tuning pass — playtesting to lock in the actual
   per-preset numbers (the table above is a starting point, not final),
   packaging polish (icon, Thunderstore listing), first public release.

## Open questions

See `RESEARCH.md`'s per-section "Open questions" for the full technical
list (mostly: exact prefab/GameObject names and Inspector-configured values
that live in Unity scene assets rather than compiled code, and thus need a
runtime logging pass or AssetRipper rather than more decompilation). At the
design level, still undecided:

- **Host-only vs. every-client-installs.** Unlike the maintainer's
  checkpoint-save mod (explicitly host-only) or sense-of-direction
  (explicitly client-side-only), Fairoots sits in between: since spore-bomb
  culling happens per-client against already-placed objects (see "Seed &
  determinism" above), it technically *can* run client-side-only and still
  be internally consistent for that client — but for a *shared, consistent*
  experience across a whole lobby, every client needs the mod installed with
  the same seed. `OVERVIEW.md` tentatively frames this as "host-only
  (probably)" — needs a decision before Phase 2's config plumbing is
  finalized, since it affects whether the mod needs any networking code at
  all (if fully client-side-only is acceptable, it needs none).
- Whether the "full zombie disable" option (Preset 4 default) should also be
  exposed as a standalone toggle independent of presets, given it overlaps
  with the game's own pre-existing (cosmetic-only) `ZombiePhobiaSetting`
  accessibility option.
