**This file is the source of truth for every balance number in the mod,** the tables and its values below are used directly by the mod.

## The two rules the tables follow

**1. Every default is the vanilla value.** Freshly install Fairoots (or delete its config file), set `General/preset` to `Custom`, change nothing else, and the game plays exactly like unmodded PEAK: every multiplier is `1.0`, every removal fraction is `0`, and every new mechanic is `off`. Custom is a blank slate you build up from, not Balanced with the numbers exposed.

The rows marked `*` on the default are the one documented exception: **gated parameters**, a dial that only means anything while some other setting is on. There is no vanilla value for the cost of a mechanic vanilla doesn't have, so the default is the sensible tuned number and the parent toggle is what defaults to off. So when the parent row isn't enabled these values aren't read anyway and therefore still play like vanilla.

**2. Presets 1-4 ignore the config entries entirely.** The config entries are read only under `Custom`.

## How to read the columns

| Column | Meaning |
|---|---|
| **Setting** | The BepInEx config key, inside the section the table is headed by. |
| **Type** | `bool`, `double` or `float`, decides how the value is written into C#. |
| **Default** | What the config entry ships with (rule 1 above). `on`/`off` for bools. |
| **Subtle … Tame** | What presets 1-4 set it to. `—` = not preset-driven. |

---

## Spore-Bombs

| Setting | Type | Default | Subtle | Balanced | Generous | Tame |
|---|---|---|---|---|---|---|
| `cull-fraction` | double | 0.0 | 0.00 | 0.05 | 0.15 | 0.25 | <!--SporeBombCullFraction-->
| `enable-foliage-removal` | bool | off | on | on | on | on | <!--EnableFoliageRemoval-->
| `trigger-radius-multiplier` | double | 1.0 | 0.90 | 0.75 | 0.70 | 0.60 | <!--SporeBombTriggerRadiusMultiplier-->
| `knockback-multiplier` | double | 1.0 | 1.00 | 0.90 | 0.70 | 0.50 | <!--SporeBombKnockbackMultiplier-->
| `screenshake-range-cap-meters` | double | 0.0 | 75 | 75 | 50 | 50 | <!--SporeBombScreenshakeRangeCapMeters-->
| `vfx-count-multiplier` | double | 1.0 | 1.00 | 0.75 | 0.50 | 0.35 | <!--SporeBombVfxCountMultiplier-->
| `trigger-height-multiplier` | double | 1.0 | 0.9 | 0.804 | 0.804 | 0.804 | <!--SporeBombTriggerHeightMultiplier-->
| `spore-area-radius-multiplier` | double | 1.0 | 1.00 | 1.00 | 1.00 | 1.00 | <!--SporeBombSporeAreaRadiusMultiplier-->
| `cover-mouth-blocks-spore-bombs` | bool | off | off | off | off | off | <!--CoverMouthBlocksSporeBombs-->
- `cull-fraction` is the **total** removal target (the foliage pass plus the seeded cull combined), not an extra cull on top. `0` means the seeded pass removes nothing; the foliage pass still runs if `enable-foliage-removal` is on.
- `screenshake-range-cap-meters`: `0` means uncapped (vanilla). Any positive value is a cap in meters. Vanilla does not have a cap which means that a player triggering a spore bomb will make the screen of another player shake even those that are very far away.
- `cover-mouth-blocks-spore-bombs` is off on every preset today. It is a real preset row rather than a flat setting so the tuning pass can turn it on for the more forgiving presets without a code change.

## Spore-Areas

| Setting | Type | Default | Subtle | Balanced | Generous | Tame |
|---|---|---|---|---|---|---|
| `disable-spore-areas` | bool | off | — | — | — | — | <!--DisableSporeAreas-->
| `removal-fraction` | double | 0.0 | 0.00 | 0.00 | 0.20 | 0.35 | <!--SporeAreaRemovalFraction-->
| `radius-multiplier` | double | 1.0 | 1.00 | 0.85 | 0.70 | 0.55 | <!--SporeAreaRadiusMultiplier-->
| `status-rate-multiplier` | double | 1.0 | 1.00 | 0.85 | 0.65 | 0.45 | <!--SporeAreaStatusRateMultiplier-->
| `enable-cover-mouth` | bool | off | on | on | on | on | <!--EnableCoverMouth-->
| `cover-mouth-stamina-per-second` | float | 0.03* | 0.04 | 0.03 | 0.02 | 0.01 | <!--CoverMouthStaminaPerSecond-->
- `removal-fraction` is 0 on Subtle **and** Balanced on purpose: a level has only
  ~12–23 spore areas against 400+ spore bombs, so they are landmarks rather than
  clutter and removing any at the default preset would reshape the biome instead
  of just making it fairer.
- `cover-mouth-stamina-per-second` is a gated parameter (`*`): it costs nothing
  until `enable-cover-mouth` is on. For scale, vanilla wall climbing costs up to
  0.2/s.

## Spores

Dials on the Spores *status* itself rather than on any one hazard that applies
it. These compound with the per-hazard rows above, on purpose.

| Setting | Type | Default | Subtle | Balanced | Generous | Tame |
|---|---|---|---|---|---|---|
| `clear-time-multiplier` | double | 1.0 | 1.00 | 0.70 | 0.65 | 0.45 | <!--SporeClearTimeMultiplier-->
| `build-up-multiplier` | double | 1.0 | 1.00 | 1.00 | 1.00 | 1.00 | <!--SporeBuildUpMultiplier-->
- `clear-time-multiplier` Balanced `0.70` is live-tuned (2026-07-30): 0.85 was
  not noticeable enough at the default preset. That leaves Balanced and Generous
  only 0.05 apart, which is known and deliberate for now.
- `build-up-multiplier` is `1.00` on all four presets **and that is not an
  unfilled row.** It multiplies every incoming dose from every source, on top of
  the per-hazard rows that already express the same intent — giving it preset
  values would compound with them (Tame's 0.45 area rate would land at 0.20).
  It stays a Custom-only global lever. Do not "finish" this row without
  rebalancing the per-hazard ones.

## Creatures

| Setting | Type | Default | Subtle | Balanced | Generous | Tame |
|---|---|---|---|---|---|---|
| `disable-zombies` | bool | off | — | — | — | — | <!--DisableZombies-->
| `disable-beetles` | bool | off | — | — | — | — | <!--DisableBeetles-->
| `disable-spiders` | bool | off | — | — | — | — | <!--DisableSpiders-->
| `zombie-speed-multiplier` | double | 1.0 | 1.00 | 0.90 | 0.80 | 0.65 | <!--ZombieSpeedMultiplier-->
| `beetle-speed-multiplier` | double | 1.0 | 1.00 | 0.90 | 0.80 | 0.65 | <!--BeetleSpeedMultiplier-->
| `beetle-knockback-multiplier` | double | 1.0 | 1.00 | 0.80 | 0.65 | 0.50 | <!--BeetleKnockbackMultiplier-->
| `creature-ragdoll-multiplier` | double | 1.0 | 1.00 | 0.85 | 0.65 | 0.40 | <!--CreatureRagdollMultiplier-->
| `zombie-deaggro-enabled` | bool | off | off | on | on | on | <!--ZombieDeaggroEnabled-->
| `zombie-deaggro-multiplier` | double | 1.0 | 1.00 | 0.85 | 0.60 | 0.35 | <!--ZombieDeaggroMultiplier-->
| `beetle-deaggro-multiplier` | double | 1.0 | 1.00 | 0.90 | 0.75 | 0.55 | <!--BeetleDeaggroMultiplier-->
| `zombie-knockout-seconds` | double | 0.0 | 2.0 | 4.0 | 6.0 | 8.0 | <!--ZombieKnockoutSeconds-->
| `beetle-knockout-seconds` | double | 0.0 | 1.0 | 2.0 | 3.0 | 4.0 | <!--BeetleKnockoutSeconds-->
| `creature-knockout-min-throw-speed` | double | 36.0* | 44 | 36 | 28 | 20 | <!--CreatureKnockoutMinThrowSpeed-->
| `creature-knockout-max-throw-distance` | double | 12.0* | 8 | 12 | 18 | 25 | <!--CreatureKnockoutMaxThrowDistance-->
| `blowgun-affects-creatures` | bool | off | on | on | on | on | <!--BlowgunAffectsCreatures-->
| `blowgun-creature-stun-seconds` | double | 60.0* | 30 | 60 | 90 | 120 | <!--BlowgunCreatureStunSeconds-->
| `zombie-wind-multiplier` | double | 1.0 | 1.2 | 1.5 | 1.8 | 2.2 | <!--ZombieWindMultiplier-->
| `beetle-wind-susceptibility` | double | 0.0 | 0.25 | 0.50 | 0.75 | 1.00 | <!--BeetleWindSusceptibility-->
- `zombie-deaggro-multiplier` is the one dial in the file where **1.0 is not
  vanilla** — it is the *toughest* setting, because vanilla zombies never
  deaggro at all and no finite multiplier expresses "never". Vanilla is
  `zombie-deaggro-enabled = off`, which is the default and the Subtle column.
- `zombie-knockout-seconds`/`beetle-knockout-seconds`: `0` = vanilla. Vanilla
  already ragdolls a *zombie* for about a second when a thrown item hits it, so
  the zombie row extends an existing interaction; a beetle is a `Mob` that
  vanilla thrown items pass straight through, so the beetle row is a genuinely
  new one. The beetle numbers stay shorter than the zombie's on every preset —
  a shell should visibly shrug off a rock better.
- `creature-knockout-min-throw-speed` (m/s) and
  `creature-knockout-max-throw-distance` (m) are gated parameters (`*`): they
  only mean anything once a knockout duration is non-zero. Lower speed / higher
  distance = easier, hence the direction they run in.
- `zombie-wind-multiplier`: `1.0` is vanilla and already non-zero — the game
  pushes a bot `Character` at 0.6× what it pushes a player. Above 1.0 the wind
  shoves zombies harder than the game does, which helps the player.
- `beetle-wind-susceptibility`: **`0` is vanilla**, not 1.0. Vanilla beetles are
  completely wind-immune (`Mob.FixedUpdate` zeroes their velocity every tick), so
  any positive value grants an effect the game never had. It is a fraction of the
  beetle's own walking speed, so 1.0 means wind slides one about as fast as it
  walks.
- The three `disable-*` switches are flat manual overrides — no preset ever turns
  them on, they exist for a host who wants a creature gone outright.

## Wind

| Setting | Type | Default | Subtle | Balanced | Generous | Tame |
|---|---|---|---|---|---|---|
| `disable-wind-entirely` | bool | off | — | — | — | — | <!--DisableWindEntirely-->
| `backpack-always-immune` | bool | off | on | on | on | on | <!--WindBackpackAlwaysImmune-->
| `force-multiplier` | double | 1.0 | 0.90 | 0.80 | 0.60 | 0.35 | <!--WindForceMultiplier-->
| `gust-duration-multiplier` | double | 1.0 | 0.90 | 0.80 | 0.60 | 0.35 | <!--WindGustDurationMultiplier-->
| `item-force-multiplier` | double | 1.0 | 1.00 | 0.70 | 0.40 | 0.00 | <!--WindItemForceMultiplier-->
| `obstacle-occlusion-range-multiplier` | double | 1.0 | 1.00 | 1.30 | 1.60 | 2.00 | <!--WindObstacleOcclusionRangeMultiplier-->
| `fall-camera-dampen-clamp` | double | 0.0 | 0.00 | 0.35 | 0.55 | 0.75 | <!--WindFallCameraDampenClamp-->
| `fall-camera-dampen-window-seconds` | float | 1.5* | 1.0 | 1.5 | 2.0 | 2.5 | <!--WindRecentForceWindowSeconds-->
| `prevent-wind-ragdoll` | bool | off | on | on | on | on | <!--PreventWindRagdoll-->
| `climb-shelters-from-wind` | bool | off | off | on | on | on | <!--ClimbSheltersFromWind-->
| `climb-speed-multiplier-in-wind` | double | 1.0 | 1.00 | 0.90 | 0.93 | 0.96 | <!--ClimbWindSpeedMultiplier-->
| `climb-upward-speed-multiplier-in-wind` | double | 1.0 | 1.00 | 0.85 | 0.89 | 0.94 | <!--ClimbWindUpwardSpeedMultiplier-->
| `climb-into-wind-speed-multiplier` | double | 1.0 | 1.00 | 0.85 | 0.89 | 0.94 | <!--ClimbWindIntoWindSpeedMultiplier-->
| `climb-shelter-grace-seconds` | float | 0.5* | 0.3 | 0.5 | 0.7 | 1.0 | <!--ClimbShelterGraceSeconds-->
| `climb-shelter-grace-force-multiplier` | double | 1.0 | 1.00 | 0.15 | 0.12 | 0.08 | <!--ClimbWindGraceForceMultiplier-->
- `item-force-multiplier` reaching `0.00` on Tame is what "every item, backpacks
  included, is fully wind-immune" means.
- `obstacle-occlusion-range-multiplier` runs **above** 1.0: the occlusion
  raycast is already enabled in Roots (vanilla min 4 / max 5 units), so the
  presets widen how far it reaches rather than switching it on.
- `climb-shelters-from-wind` is off on Subtle: handing out outright wind
  immunity is the least subtle thing in the mod. The three climb-speed
  multipliers are what the shelter costs while wind is actually pushing on the
  climber, and are gentler on the more forgiving presets — "forgiving" here
  means paying less for the shelter. Their Subtle values are moot (mechanic off)
  and left at 1.0 rather than a number implying a slowdown that never happens.
- `climb-speed-multiplier-in-wind` Balanced `0.90` and the two `0.85`s are
  **playtest-tuned, not placeholders** (the original 0.55 estimate made waiting
  a gust out strictly better than climbing through it, which is the failure mode
  the mechanic exists to remove).
- `climb-shelter-grace-force-multiplier` stays deliberately non-zero: full
  immunity in the let-go window would let a player wall-tap across an exposed
  stretch.
- `fall-camera-dampen-window-seconds` and `climb-shelter-grace-seconds` are
  gated parameters (`*`) — timing windows that only matter while the mechanic
  they belong to is on.
