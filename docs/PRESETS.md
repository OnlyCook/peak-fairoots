**This file is the source of truth for every balance number in the mod,** the tables and its values below are used directly by the mod.

## The two rules the tables follow

**1. Every default is the vanilla value.** Freshly install Fairoots (or delete its config file), set `Host/preset` to `Custom`, change nothing else, and the game plays exactly like unmodded PEAK: every multiplier is `1.0`, every removal fraction is `0`, and every new mechanic is `off`. Custom is a blank slate you build up from, not balanced with the numbers exposed.

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
| `trigger-height-multiplier` | double | 1.0 | 0.90 | 0.804 | 0.804 | 0.804 | <!--SporeBombTriggerHeightMultiplier-->
| `spore-area-radius-multiplier` | double | 1.0 | 1.00 | 1.00 | 1.00 | 1.00 | <!--SporeBombSporeAreaRadiusMultiplier-->
| `cover-mouth-blocks-spore-bombs` | bool | off | off | off | off | off | <!--CoverMouthBlocksSporeBombs-->
- `cull-fraction` is the **total** removal target (the foliage pass plus the seeded cull combined), not an extra cull on top. `0` means the seeded pass removes nothing; the foliage pass still runs if `enable-foliage-removal` is on.
- `screenshake-range-cap-meters`: `0` means uncapped (vanilla). Any positive value is a cap in meters. Vanilla does not have a cap which means that a player triggering a spore bomb will make the screen of another player shake even those that are very far away.

## Spore-Areas

| Setting | Type | Default | Subtle | Balanced | Generous | Tame |
|---|---|---|---|---|---|---|
| `disable-spore-areas` | bool | off | — | — | — | — | <!--DisableSporeAreas-->
| `removal-fraction` | double | 0.0 | 0.00 | 0.00 | 0.20 | 0.35 | <!--SporeAreaRemovalFraction-->
| `radius-multiplier` | double | 1.0 | 1.00 | 1.00 | 0.90 | 0.80 | <!--SporeAreaRadiusMultiplier-->
| `status-rate-multiplier` | double | 1.0 | 1.00 | 1.00 | 0.90 | 0.80 | <!--SporeAreaStatusRateMultiplier-->
| `enable-cover-mouth` | bool | off | on | on | on | on | <!--EnableCoverMouth-->
| └ `cover-mouth-stamina-per-second` | float | 0.03* | 0.04 | 0.03 | 0.02 | 0.01 | <!--CoverMouthStaminaPerSecond-->
- `removal-fraction` decides how much percent of spore areas should be removed.
- `cover-mouth-stamina-per-second` is a gated parameter (`*`): it costs nothing until `enable-cover-mouth` is on. For scale, vanilla static wall climbing costs 0.2/s.

## Spores

Dials on the *Spores* status effect itself rather than on any one hazard that applies it. These combine with the per-hazard rows above.

| Setting | Type | Default | Subtle | Balanced | Generous | Tame |
|---|---|---|---|---|---|---|
| `clear-time-multiplier` | double | 1.0 | 1.00 | 0.80 | 0.75 | 0.65 | <!--SporeClearTimeMultiplier-->
| `build-up-multiplier` | double | 1.0 | 1.00 | 1.00 | 1.00 | 1.00 | <!--SporeBuildUpMultiplier-->
- `build-up-multiplier` is `1.00` on all four presets intentionally, as it stacks too strongly with the other setting. 

## Creatures

| Setting | Type | Default | Subtle | Balanced | Generous | Tame |
|---|---|---|---|---|---|---|
| `disable-zombies` | bool | off | — | — | — | — | <!--DisableZombies-->
| `disable-beetles` | bool | off | — | — | — | — | <!--DisableBeetles-->
| `disable-spiders` | bool | off | — | — | — | — | <!--DisableSpiders-->
| `zombie-speed-multiplier` | double | 1.0 | 1.00 | 0.90 | 0.80 | 0.65 | <!--ZombieSpeedMultiplier-->
| `beetle-speed-multiplier` | double | 1.0 | 1.00 | 1.00 | 0.90 | 0.80 | <!--BeetleSpeedMultiplier-->
| `beetle-knockback-multiplier` | double | 1.0 | 1.00 | 0.80 | 0.65 | 0.50 | <!--BeetleKnockbackMultiplier-->
| `creature-ragdoll-multiplier` | double | 1.0 | 1.00 | 0.85 | 0.65 | 0.40 | <!--CreatureRagdollMultiplier-->
| `zombie-deaggro-enabled` | bool | off | off | on | on | on | <!--ZombieDeaggroEnabled-->
| └ `zombie-deaggro-multiplier` | double | 1.0 | 1.00 | 1.00 | 0.85 | 0.75 | <!--ZombieDeaggroMultiplier-->
| `beetle-deaggro-multiplier` | double | 1.0 | 1.00 | 1.00 | 0.90 | 0.80 | <!--BeetleDeaggroMultiplier-->
| `zombie-knockout-seconds` | double | 0.0 | 1.0 | 2.0 | 3.0 | 4.0 | <!--ZombieKnockoutSeconds-->
| `beetle-knockout-seconds` | double | 0.0 | 2.0 | 3.0 | 4.0 | 4.0 | <!--BeetleKnockoutSeconds-->
| `creature-knockout-min-throw-speed` | double | 36.0* | 38 | 36 | 34 | 32 | <!--CreatureKnockoutMinThrowSpeed-->
| `creature-knockout-max-throw-distance` | double | 12.0* | 10 | 12 | 13 | 15 | <!--CreatureKnockoutMaxThrowDistance-->
| `blowgun-affects-creatures` | bool | off | on | on | on | on | <!--BlowgunAffectsCreatures-->
| └ `blowgun-creature-stun-seconds` | double | 45.0* | 30 | 45 | 60 | 60 | <!--BlowgunCreatureStunSeconds-->
| `zombie-wind-multiplier` | double | 1.0 | 1.2 | 1.4 | 1.5 | 1.6 | <!--ZombieWindMultiplier-->
| `beetle-wind-susceptibility` | double | 0.0 | 0.25 | 0.40 | 0.60 | 0.80 | <!--BeetleWindSusceptibility-->
- `zombie-deaggro-multiplier` is the one dial in the file where **1.0 is not vanilla**, because vanilla zombies never deaggro at all. But because vanilla is `zombie-deaggro-enabled = off` we don't have to worry about the value.
- `creature-knockout-min-throw-speed` (m/s) and `creature-knockout-max-throw-distance` (m) are gated parameters (`*`): they only mean anything once a knockout duration is non-zero (for each creature). Lower speed / higher distance = easier.
- `zombie-wind-multiplier`: `1.0` is vanilla and already non-zero, the game pushes a zombie `Character` at 0.6× what it pushes a player.
- `beetle-wind-susceptibility`: **`0` is vanilla**, not 1.0. Vanilla beetles are completely wind-immune, so any positive value grants an effect the game never had. It is a fraction of the beetle's own walking speed, so 1.0 means wind slides one about as fast as it walks.

## Wind

| Setting | Type | Default | Subtle | Balanced | Generous | Tame |
|---|---|---|---|---|---|---|
| `disable-wind-entirely` | bool | off | — | — | — | — | <!--DisableWindEntirely-->
| `backpack-always-immune` | bool | off | on | on | on | on | <!--WindBackpackAlwaysImmune-->
| `force-multiplier` | double | 1.0 | 0.95 | 0.95 | 0.90 | 0.85 | <!--WindForceMultiplier-->
| `gust-duration-multiplier` | double | 1.0 | 1.00 | 1.00 | 0.90 | 0.90 | <!--WindGustDurationMultiplier-->
| `item-force-multiplier` | double | 1.0 | 0.70 | 0.60 | 0.40 | 0.20 | <!--WindItemForceMultiplier-->
| `obstacle-occlusion-range-multiplier` | double | 1.0 | 1.00 | 1.20 | 1.30 | 1.50 | <!--WindObstacleOcclusionRangeMultiplier-->
| `fall-camera-dampen-clamp` | double | 0.0 | 0.20 | 0.35 | 0.55 | 0.75 | <!--WindFallCameraDampenClamp-->
| └ `fall-camera-dampen-window-seconds` | float | 1.5* | 1.0 | 1.5 | 2.0 | 2.5 | <!--WindRecentForceWindowSeconds-->
| `prevent-wind-ragdoll` | bool | off | on | on | on | on | <!--PreventWindRagdoll-->
| `climb-shelters-from-wind` | bool | off | off | on | on | on | <!--ClimbSheltersFromWind-->
| └ `climb-speed-multiplier-in-wind` | double | 1.0 | 0.85 | 0.90 | 0.93 | 0.96 | <!--ClimbWindSpeedMultiplier-->
| └ `climb-upward-speed-multiplier-in-wind` | double | 1.0 | 0.80 | 0.85 | 0.89 | 0.94 | <!--ClimbWindUpwardSpeedMultiplier-->
| └ `climb-into-wind-speed-multiplier` | double | 1.0 | 0.80 | 0.85 | 0.89 | 0.94 | <!--ClimbWindIntoWindSpeedMultiplier-->
| └ `climb-shelter-grace-seconds` | float | 0.5* | 0.4 | 0.5 | 0.5 | 0.6 | <!--ClimbShelterGraceSeconds-->
| └ `climb-shelter-grace-force-multiplier` | double | 1.0 | 0.25 | 0.15 | 0.12 | 0.10 | <!--ClimbWindGraceForceMultiplier-->
- `item-force-multiplier` reaching `0.00` means every item/backpacks are fully wind-immune.
- `obstacle-occlusion-range-multiplier` runs **above** 1.0: the occlusion raycast is already enabled in Roots (vanilla min 4 / max 5 units), so the presets widen how far it reaches.
- `climb-shelters-from-wind` means you are immune from the wind while climbing, the game kind of already includes this but you aren't immune, the wind's force is just much less.
- `climb-shelter-grace-force-multiplier` stays deliberately non-zero: full immunity in the let-go window would let a player wall-tap across an exposed stretch.
