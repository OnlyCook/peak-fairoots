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

## Host authority (locked in 2026-07-22 — read this before touching any config or Wind/SporeBombs code)

**Every client in the lobby must have Fairoots installed, but only the
host's config is ever actually used for anything that changes shared
gameplay.** An individual (non-host) client's own local seed/preset/per-
mechanic settings are never used for those mechanics — no matter what they
set locally, they get the host's values instead. This is deliberate, not a
limitation to work around: the maintainer's explicit reasoning is that a
client having the power to unilaterally alter shared game balance (wind
strength, which spore bombs exist, etc.) hands too much control to any one
player without a central authority. This is the locked-in interpretation of
`OVERVIEW.md`'s original "host-only (probably)" framing — the "probably"
was about a few things not being possible to make host-only at all (purely
local camera-feel/UI/diagnostics), not about whether it's *desirable* to
default to client authority where technically convenient.

**Why "only the host needs the mod installed" (the more literal reading of
"host-only") is not achievable for most of this mod's mechanics — a real
networking constraint, confirmed 2026-07-22, not a design choice:**

- Wind force/item-force/obstacle-occlusion are computed **per-client, from
  each client's own local `WindChillZone` field values** — vanilla's own
  `AddWindForceToCharacter` even explicitly checks
  `character.photonView.IsMine` before applying anything, confirming each
  client only ever simulates force for characters/items *it* owns. There is
  no existing native RPC that lets the host override another client's local
  computation for these fields.
- Spore-bomb culling deactivates a **static, scene-baked GameObject** (Roots
  prop placement is baked into the level scene at author time, not
  network-instantiated — see `SporeBombCullPatch`'s remarks) — every client
  has its own independent local copy, so the host calling `SetActive(false)`
  only ever affects what the host itself sees/can trigger.
- A **custom** Photon RPC or room property broadcasting the host's tuned
  values would only work on clients that have code to *receive and act on*
  it — Photon does not invent behavior on a receiver with no matching code,
  so a non-modded client would simply ignore it entirely (at best a silent
  no-op, matching normal Photon behavior for an unrecognized RPC).

The one mechanic that genuinely *is* host-only "for free," with zero custom
networking needed: **wind gust timing/frequency.** Vanilla's own
`WindChillZone.HandleTime()` only calls `GetNextWindTime()` when
`PhotonNetwork.IsMasterClient` is true, and broadcasts the result to every
client (modded or not) via the existing native `RPCA_ToggleWind` RPC. Since
only the host's `GetNextWindTime()` call ever matters, scaling the host's
own `windTimeRangeOn`/`windTimeRangeOff` fields propagates correctly to
everyone automatically — this is the one place the mod doesn't need any of
the machinery described below.

**The actual mechanism (everything else): host publishes, every client
reads.** See `Core`-adjacent `Fairoots/Networking/`:

- `HostAuthority.Resolve(key, localValue)` — called from every host-
  authoritative `Effective*` accessor in `PluginConfig.cs`. On the host, a
  no-op (the local value already *is* authoritative). On any other client,
  overridden by whatever the host last published to the Photon room's custom
  properties, falling back to the local value only if nothing's been
  published yet (solo play, or the brief window before the host's first
  publish).
- `HostAuthority.PublishAll()` — the host writes every host-authoritative
  resolved value (seed, every spore-bomb/wind multiplier, the backpack-
  immunity and disable-wind-entirely flags) to the room's custom properties
  in one batched write. Wired to fire on every relevant `SettingChanged` (via
  a single config-file-wide hook, not one per entry), on every Roots level
  load (`RootsLevelWatcher`), and via `HostAuthoritySync` (a small
  `MonoBehaviourPunCallbacks` component) on joining a room already in
  progress or a host migration (Photon promoting a new master client after
  the previous host disconnects).

**What stays purely client-local, deliberately excluded from host
authority:** the wind-preceded-fall camera-dampening clamp/window (a
camera-feel/accessibility setting — it only affects how *your own* camera
reacts to *your own* fall, never anyone else's experience or the shared
world state), the spore-bomb recolor (`General/recolor-spore-bombs`, added
2026-07-26 — purely cosmetic, see the spore-bomb mechanic note below), and
the entire `Debug` section (diagnostics/overlays, never gameplay-affecting;
this is also where `apply-changes-live` lives, since freezing values
mid-run is a comparison-testing tool). Everything else that decides what
spawns, what gets removed, or how much force applies is host-authoritative.

**Enforcement (added 2026-07-22, refined 2026-07-22): every client must
actually have Fairoots installed, and this is checked, not just
documented.** A client missing the mod isn't merely "not tuned" — it
silently breaks the shared-experience premise for itself (full vanilla spore
bombs/wind while everyone else sees the host's configured version). Every
Fairoots client marks itself via a Photon player custom property on join
(`Networking/ModPresenceCheck.cs`) — already fully replicated to every other
client by Photon itself, no extra networking needed to check it.

The actual player-facing gate lives at the one moment it matters: clicking
**Start on the Boarding Pass** (opened via the Gate Kiosk) —
`BoardingPassStartGatePatch.cs` prefixes `BoardingPass.StartGame()`
(confirmed via decompile to be callable by *any* player, not just the host,
since it just sends an RPC to whoever the MasterClient is — so the check has
to run client-side on whoever clicks it, not host-exclusive). If everyone in
the room has Fairoots installed, this is a complete no-op — vanilla
behavior, unchanged. If not, the click is suppressed and a confirm dialog
(`ModPresenceDialog.cs`) appears: **Cancel** leaves the Boarding Pass
untouched (nothing starts); **Start Anyway** re-invokes `StartGame()` for
real. The dialog never shows player names (would clip/bloat with several
missing players) — those go to the log only
(`[BoardingPassStartGatePatch] Start blocked pending confirmation - N
player(s) missing Fairoots: ...`). Text is fully localized into all 14
languages the game ships with (`LocalizedText.Language`), following
peak-checkpoint-save's `MessagesLocalization`/`LocalizationHelper` convention
exactly (falls back to English for any language an entry doesn't cover —
used here only for TraditionalChinese, since the game's own
`LocalizedText.LANGUAGE_COUNT` is 14, one less than the 15-value enum).

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
| Wind force / frequency (two independent config entries as of 2026-07-22 — `force-multiplier` and `gust-duration-multiplier` — but the same numbers per preset below, so presets 1-4 behave identically to the original combined row) | −10% | −20% | −40% | −65% |
| Wind: items/backpack immunity (backpack immunity itself is now player-toggleable via the flat `Wind/backpack-always-immune` setting, added 2026-07-22 — on by default on every preset, as below) | backpack only | backpack + reduced item force | backpack immune, items −60% | backpack + items fully immune |
| Wind: obstacle occlusion | off (vanilla) | on, coarse | on, tuned | on, generous radius |
| Wind: fog-while-active density | vanilla | vanilla (reverted — see note) | vanilla (reverted — see note) | vanilla (reverted — see note) |
| Wind-induced fall camera spin dampening (new — see below) | off | on, mild clamp | on, moderate clamp | on, strong clamp |
| Spore bomb total removal target (bush/grass removal + seeded cull, combined — see below) | 0% seeded on top of bush removal | 25% | 50% (OVERVIEW's literal ask) | 75% |
| Spore bomb bush/grass placement removal | ✅ on, all presets (see below) | ✅ on | ✅ on | ✅ on |
| Spore bomb trigger radius | vanilla | −25% (playtest-confirmed) | −30% | −45% |
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

**Spore bomb removal is two passes, not one, and the second is budgeted
against the first (confirmed design, see `RESEARCH.md` Q7):**

1. **Bush/grass placement removal** — unconditional, seed-independent,
   runs on every preset including Subtle. The game genuinely does nothing
   to stop a spore bomb landing inside bush/grass geometry (confirmed —
   this isn't existing behavior to tune, it's a gap this mod fixes
   outright), so any spore bomb detected sitting inside foliage is always
   removed, regardless of preset.
2. **Seeded cull, budgeted against pass 1** — the preset's "total removal
   target" percentage is a target for *combined* removal, not an additional
   cut on top of whatever pass 1 already removed. Worked example: 100 spore
   bombs, 20 sit in bushes (removed by pass 1), preset target is 50% → only
   30 more get removed by the seeded pass (50 total target − 20 already
   gone), not 50 more. If pass 1 alone already meets or exceeds a preset's
   target (plausible on Preset 1, whose target is 0% beyond bush removal),
   the seeded pass removes nothing further — it never removes *more* than
   the preset's configured fraction just because bush placement happened to
   overshoot it.

**Wind-induced fall camera spin dampening** targets a specific, now fully
understood mechanism (`RESEARCH.md` Q6): the moment a character starts
falling — from any cause, wind-knockback-off-a-ledge being the common Roots
case — the game's camera stops being player-controlled and starts directly
tracking the physics-simulated rotation of the ragdoll's head bone, which is
what actually produces the disorienting "screen spins" effect (not
screen-shake, which is a separate, additive effect). Since the underlying
game mechanism doesn't distinguish "fell because of wind" from "fell because
you jumped badly," this mod needs to decide whether to dampen it for every
fall in Roots (simpler) or only fall episodes preceded by a recent wind-force
application (closer to the original complaint, more implementation
complexity — see "Open questions" below, not yet decided).

## Mechanic notes (implementation feasibility, from `RESEARCH.md`)

Brief summary only — see `RESEARCH.md` for exact classes/fields/citations.

- **Wind**: single core class owns nearly everything (force, timing,
  item/character distinction, an already-existing-but-currently-unused
  obstacle-raycast option, and a climbing-stamina-multiplier field that's
  almost exactly the hook the counter-wind-by-climbing mechanic needs).
  Backpack immunity and fog-during-wind scaling are both simple, isolated
  patches. The "screen goes crazy while falling" complaint is now **fully
  traced and understood** (`RESEARCH.md` Q6) — it's the game's camera
  directly tracking uncontrolled ragdoll-head physics rotation the instant
  a character starts falling (any fall, not wind-exclusive), not a
  wind-specific shake effect. The fix targets that camera-blend mechanism
  directly; the remaining open item is a scoping/playtesting decision, not
  a code-location question.
- **Spore bombs**: no dedicated class exists — the hazard is built from
  generic, reusable components (an area-of-effect/explosion component, a
  separate particle-orb VFX spawner, a generic proximity-screenshake
  component). Prefab identity is **now confirmed**, cross-referenced from
  the maintainer's `peak-sense-of-direction` mod, which independently
  solved the same "what GameObject is this" problem for its item-ping
  feature via in-game debug logging: the hazard is one of three
  name-substring-matched variants, `SporeFungus`/`SporeMushroom`/
  `SporeMushroomExplo` (see `RESEARCH.md` Q7 for the full table). Bush/grass
  placement removal is a **confirmed real gap in vanilla** (not a
  misunderstanding to double check) — the game does nothing to prevent it,
  and this mod's fix budgets that removal against the seeded cull target
  rather than stacking on top of it (see the Presets section above for the
  worked example). Remaining open items are narrower now: confirming the
  same name substrings on the Roots-specific instance, the exact trigger
  hitbox size, and the foliage-detection method for the bush/grass check —
  all runtime-logging tasks, not further decompilation.

  **Trigger-height cutoff (implemented, not in the preset table above - a bug
  fix, not a balance dial):** live playtesting against the trigger-radius
  wireframe overlay confirmed the "Spore Bomb"/"Poison Spore Bomb" variants'
  vanilla trigger sphere reaches absurdly far above the actual (short, wide)
  mushroom mesh - tall enough that jumping over one is physically impossible
  in vanilla, since the trigger volume is a full sphere reaching well above
  head height rather than the flattened shape the hazard visually is. Fixed
  via a Harmony prefix on `TriggerEvent.OnTriggerEnter` that suppresses the
  hit when the player is above a configurable height over the spore bomb's
  base (`Spore-Bombs/max-trigger-height-meters`, default 1.75m -
  playtest-confirmed by the maintainer as "perfect" - 0 = vanilla). Left
  untouched for the "Explosive Spore Bomb" variant, which is genuinely round.

  **Recolor (implemented 2026-07-26, not in the preset table above - a
  readability fix, not a balance dial):** vanilla spore bombs are green
  hazards sitting on green grass and green ground, so they camouflage into
  the terrain even when they aren't literally buried inside a fern (that
  separate, physical case is what the bush/grass placement removal above
  handles). `General/recolor-spore-bombs` (on by default) recolors both the
  mushroom-cluster variants and the explosive one to the magenta/pink the
  game's own Spores status effect uses — the target hue is read live off
  `CharacterAfflictions.colorSpores`, the same field the game pulses the
  player's own body with when spores are applied, so the hazard's color and
  the status it inflicts match by construction rather than by a guessed
  hex value.

  **Magenta specifically, not "some warm color."** The mechanism is a hue
  replacement (adopt the target hue, blend saturation toward it, then rescale
  onto the original's Rec. 709 luminance so shading survives and the object
  doesn't get darker), not a multiplicative tint, and
  that choice is an accessibility requirement rather than an implementation
  detail. Multiplying can only ever scale channels that are already present:
  the explosive variant's authored color `(0.717, 0.252, 0)` has **zero
  blue**, so no gain could push it past pure red — and red against green
  foliage is exactly the pair a red-green colorblind player cannot separate,
  which would defeat the whole feature. Replacing the hue puts real blue into
  the result, and blue is the channel red-green colorblindness leaves intact.
  Every color slot the shader declares is recolored, not a subset:
  `W/Peak_Standard` drives its stylized look from several at once, and doing
  only some of them recolors the crevices and shading bands independently of
  the surface (confirmed in-game — it looks like pink veins over a green
  mushroom).

  This is **the one setting in the mod that is deliberately not
  host-authoritative** (see the Host authority section above). The rule
  there is that no client may unilaterally alter shared gameplay; a color
  changes nothing shared, only what one player sees on their own screen, so
  there is nothing to keep consistent and no reason a host should get to
  dictate it — the same reasoning that already exempts the wind-fall camera
  dampening clamp. It's also always immediate, ignoring
  `Debug/apply-changes-live`: a cosmetic toggle that waits for a level
  reload would just read as broken.
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
  structure, packaging conventions, and coding style. `sense-of-direction`
  specifically turned out to be a genuine research **data source**, not just
  a style reference: its item-ping feature independently solved the
  spore-bomb prefab-identity problem this mod also needed (see
  `RESEARCH.md` Q7) — worth checking that repo's `RESEARCH.md`/source
  comments first any time this mod hits a "no dedicated class in the
  decompile" wall, since the same object may have already been identified
  there via debug logging for an unrelated feature.
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
   (not shipped) that, run once in an actual Roots level, confirms the
   `SporeFungus`/`SporeMushroom`/`SporeMushroomExplo` prefab-name matches
   (cross-referenced from `peak-sense-of-direction`, see `RESEARCH.md` Q7)
   actually apply in Roots specifically, dumps their exact
   Inspector-configured field values (trigger hitbox size, knockback,
   screenshake range) and component signatures, identifies the
   bush/grass-foliage detection method for the cull-budget algorithm, and
   does the same for spore areas and the honeycomb/stove spawn pools.
   Resolves the open questions in `RESEARCH.md` that block precise
   implementation of Phase 4+ — narrower in scope now than originally
   planned, since spore-bomb identity itself is already resolved. (candidate
   for the AssetRipper/Unity-asset-extraction route mentioned throughout
   `RESEARCH.md`, if runtime logging alone doesn't resolve everything, e.g.
   exact spawn-pool weights.)
4. **Phase 4:** Spore Bombs — bush/grass placement removal (unconditional,
   all presets), seeded cull rate budgeted against it (the mechanic the seed
   system primarily exists for), trigger radius, knockback, screenshake
   range, particle count.
5. **Phase 5 (done, fog scaling reverted):** Wind — force/frequency scaling,
   backpack/item immunity, obstacle occlusion tuning, and the wind-induced-
   fall camera spin dampening (scoped to wind-preceded falls only, per the
   maintainer's decision below). The climb-to-counter-wind mechanic turned
   out to already exist natively (`WindChillZone.AddWindForceToCharacter`
   already fully suppresses wind force while actively gripping a climb
   handle) — tune-not-build, no patch needed; see `CODEBASE.md`'s `Wind/`
   section. **Fog-while-active density scaling was implemented and reverted
   as a precaution (2026-07-22):** scaling `FogConfig.windFogDensity`/
   `WindFogTextureDensity` relies on decompiled-C#-only assumptions about
   what those shader globals actually mean, with no way to verify against
   the real shader code — so it was pulled rather than left in place on a
   guess, even though it later turned out *not* to be the cause of the
   "screen turns solid black" bug reported at the time (that turned out to
   be a separate, now-fixed issue: scaling `windTimeRangeOn` down to a
   genuinely zero-length gust broke the *native* wind on/off timer, which
   in turn kept re-triggering the game's own fog/storm-blend logic faster
   than it could ever decay — see `Core/WindTuning.cs`'s
   `MinWindActiveDurationSeconds` remarks). This row stays untouched
   (vanilla) until/unless the fog shader semantics can be confirmed some
   other way (e.g. AssetRipper pulling the actual shader graph). See
   `docs/TESTING.md`'s wind test section. **Two follow-up settings added
   2026-07-22 at the maintainer's request:** `Wind/force-multiplier` and
   `Wind/gust-duration-multiplier` are now independent config entries (were
   one shared multiplier at first) so push strength and gust timing/
   frequency can be tuned separately; and a flat, non-preset-gated
   `Wind/disable-wind-entirely` master switch (off by default, never enabled by
   any preset) fully reverts the entire wind mechanic to vanilla for players
   who don't want it at all.
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

See `RESEARCH.md`'s "OPEN QUESTIONS / COULDN'T CONFIRM" section for the full
technical list and current status per item (several were resolved in the
2026-07-19 follow-up pass — spore bomb prefab identity, the bush/grass
cull-budget algorithm, and the wind-fall camera-spin mechanism are all now
understood; what's left is mostly exact Inspector-configured values and a
couple of items — trigger hitbox size, foliage-detection method, wind
obstacle-occlusion tuning, spore-area wind interaction, zombie speed field,
honeycomb/stove spawn weights — that need a runtime logging pass or
AssetRipper, not more decompilation). At the design level, still undecided:

- **Host-only vs. every-client-installs — RESOLVED (2026-07-22), see "Host
  authority" section below.** Every client must have Fairoots installed, but
  only the host's config is ever actually used for anything that affects
  shared gameplay — an individual client's own local config for those
  settings is always overridden. `OVERVIEW.md`'s original "host-only
  (probably)" framing is now locked in with that clarification.
- Whether the "full zombie disable" option (Preset 4 default) should also be
  exposed as a standalone toggle independent of presets, given it overlaps
  with the game's own pre-existing (cosmetic-only) `ZombiePhobiaSetting`
  accessibility option.
- **Wind-induced fall camera-spin dampening scope — RESOLVED (2026-07-22).**
  The maintainer chose the wind-preceded-only option, not the simpler
  every-fall version: an ordinary fall is generally the player's own fault,
  but wind blowing you off a ledge mid-jump is close to pure bad luck and
  shouldn't be treated the same as a self-inflicted fall. Implemented via a
  short-lived timestamp set on `WindChillZone.AddWindForceToCharacter`'s
  postfix, checked against a configurable window
  (`Wind/fall-camera-dampen-window-seconds`, default 1.5s) when
  `CharacterData.GetTargetRagdollControll()` is about to return its
  unconditional 0 for a fall in progress. See `CODEBASE.md`'s `Wind/` section
  and `Core/WindTuning.cs`'s `IsWindForceStillRecent`/`ApplyFallCameraDampening`.
