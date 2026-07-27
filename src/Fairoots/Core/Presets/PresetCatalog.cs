using System;
using Fairoots.Core;

namespace Fairoots.Core.Presets
{
    /// <summary>
    /// The per-preset numeric backing values - the single source of truth for
    /// "what does preset N set mechanic X to." As each implementation phase lands
    /// (ROADMAP.md phased plan), the corresponding mechanic's values move here so
    /// the whole preset table lives in one Unity-free, unit-tested place.
    ///
    /// Currently populated: the spore-bomb total-removal target (Phase 4, the
    /// mechanic the seed system primarily exists for) and the two always-on new
    /// counterplay mechanics. Everything else in ROADMAP.md's preset table is
    /// intentionally not encoded yet - the numbers there are "starting targets, not
    /// final" pending the runtime tuning pass, so they get added mechanic-by-mechanic
    /// as their phase is implemented rather than committed as guesses up front.
    ///
    /// Values here are the preset *defaults*. They are only what applies when the
    /// player hasn't overridden the specific setting - see
    /// <see cref="OverrideResolution"/> for how a hand-tuned config value wins.
    /// </summary>
    public static class PresetCatalog
    {
        /// <summary>
        /// <see cref="PresetId.Custom"/> has no catalog numbers of its own - every
        /// per-mechanic setting under Custom is meant to come straight from the
        /// player's config (see <see cref="PresetId.Custom"/>'s remarks). This maps
        /// Custom to Balanced purely so a catalog lookup never throws and never
        /// returns a nonsense value if a setting's config entry is still sitting at
        /// the "follow preset" sentinel (e.g. the player switched to Custom without
        /// touching every slider yet) - it is a safety fallback, not "Custom follows
        /// Balanced."
        /// </summary>
        private static PresetId CatalogKey(PresetId preset) =>
            preset == PresetId.Custom ? PresetId.Balanced : preset;

        /// <summary>
        /// Target fraction of spore bombs to remove overall (foliage + seeded cull
        /// combined), per ROADMAP.md's "Spore bomb total removal target" row.
        /// Preset 1 removes nothing beyond the always-on foliage pass; Preset 3 is
        /// OVERVIEW.md's literal "cut them in half" ask.
        /// </summary>
        public static double SporeBombCullFraction(PresetId preset)
        {
            switch (CatalogKey(preset))
            {
                case PresetId.Subtle: return 0.00;
                case PresetId.Balanced: return 0.25;
                case PresetId.Generous: return 0.50;
                case PresetId.Tame: return 0.75;
                default: throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        /// <summary>
        /// Multiplier applied to a kept spore bomb's trigger-hitbox
        /// <c>SphereCollider.radius</c>, per ROADMAP.md's "Spore bomb trigger
        /// radius" row. 1.0 = vanilla (Subtle). Balanced's 0.75 is
        /// live-playtest-confirmed against the actual mushroom mesh (via
        /// <c>TriggerRadiusOverlay</c>'s wireframe) - the maintainer's own
        /// in-game comparison called it "the perfect value," overriding the
        /// original -15% starting estimate from ROADMAP.md's table.
        /// </summary>
        public static double SporeBombTriggerRadiusMultiplier(PresetId preset)
        {
            switch (CatalogKey(preset))
            {
                case PresetId.Subtle: return 1.00;
                case PresetId.Balanced: return 0.75;
                case PresetId.Generous: return 0.70;
                case PresetId.Tame: return 0.55;
                default: throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        /// <summary>
        /// Multiplier applied to the spawned explosion's <c>AOE.knockback</c>
        /// (and item knockback), per ROADMAP.md's "Spore bomb knockback/explosion
        /// force" row (-20%/-40%/-60%). 1.0 = vanilla (Subtle).
        /// </summary>
        public static double SporeBombKnockbackMultiplier(PresetId preset)
        {
            switch (CatalogKey(preset))
            {
                case PresetId.Subtle: return 1.00;
                case PresetId.Balanced: return 0.80;
                case PresetId.Generous: return 0.60;
                case PresetId.Tame: return 0.40;
                default: throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        /// <summary>
        /// Cap, in meters, on the spawned explosion's <c>AddScreenshake.range</c>,
        /// per ROADMAP.md's "Spore bomb screen-shake distance cap" row. Subtle uses
        /// <see cref="SporeBombExplosionTuning.NoScreenshakeCap"/> ("vanilla, ~75m
        /// unconfirmed" - left alone).
        /// </summary>
        public static float SporeBombScreenshakeRangeCapMeters(PresetId preset)
        {
            switch (CatalogKey(preset))
            {
                case PresetId.Subtle: return SporeBombExplosionTuning.NoScreenshakeCap;
                case PresetId.Balanced: return 30f;
                case PresetId.Generous: return 20f;
                case PresetId.Tame: return 10f;
                default: throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        /// <summary>
        /// Multiplier applied to the spawned explosion's
        /// <c>ExplosionEffect.explosionPointCount</c>/<c>subExplosionPointCount</c>,
        /// per ROADMAP.md's "Spore bomb particle/VFX count" row (-25%/-50%/-65%).
        /// 1.0 = vanilla (Subtle).
        /// </summary>
        public static double SporeBombVfxCountMultiplier(PresetId preset)
        {
            switch (CatalogKey(preset))
            {
                case PresetId.Subtle: return 1.00;
                case PresetId.Balanced: return 0.75;
                case PresetId.Generous: return 0.50;
                case PresetId.Tame: return 0.35;
                default: throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        /// <summary>
        /// Multiplier feeding <see cref="Core.SporeBombExplosionTuning.ResolveTriggerHeightCutoffMeters"/>,
        /// per the maintainer's 2026-07-27 fix (folded into the preset system -
        /// previously this was a flat, preset-exempt absolute-meters bug fix,
        /// which meant a manual edit under a non-Custom preset silently took
        /// effect, which shouldn't happen). 1.0 = vanilla (Subtle - cutoff
        /// disabled). Balanced's 0.804 reproduces the exact absolute cutoff
        /// (2.25m) the maintainer had playtest-tuned before this became a
        /// multiplier - see <see cref="Core.SporeBombExplosionTuning.TriggerHeightBaselineMeters"/>.
        /// Generous/Tame extrapolate the same relative progression
        /// <see cref="SporeBombTriggerRadiusMultiplier"/> uses (unconfirmed
        /// starting estimates pending playtest, like every other not-yet-tuned
        /// dial in this file).
        /// </summary>
        public static double SporeBombTriggerHeightMultiplier(PresetId preset)
        {
            switch (CatalogKey(preset))
            {
                case PresetId.Subtle: return 1.00;
                case PresetId.Balanced: return 0.804;
                case PresetId.Generous: return 0.75;
                case PresetId.Tame: return 0.589;
                default: throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        /// <summary>
        /// Multiplier applied to the temporary spore area's radius on
        /// detonation, per <see cref="Core.SporeBombExplosionTuning.ScaleSporeAreaRadius"/>.
        /// Not yet tuned per preset - every preset uses 1.0 (vanilla) for now,
        /// same as before this was folded into the preset/override system
        /// (2026-07-27, fixing the bug where a manual edit under a non-Custom
        /// preset silently took effect).
        /// </summary>
        public static double SporeBombSporeAreaRadiusMultiplier(PresetId preset)
        {
            switch (CatalogKey(preset))
            {
                case PresetId.Subtle: return 1.00;
                case PresetId.Balanced: return 1.00;
                case PresetId.Generous: return 1.00;
                case PresetId.Tame: return 1.00;
                default: throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        /// <summary>
        /// Target fraction of the level's persistent spore areas ("Mushroom Spore
        /// Clouds") to remove, per <see cref="SporeAreaCull"/>.
        ///
        /// <b>Zero on both Subtle and Balanced</b> - the maintainer's explicit
        /// call (2026-07-27), and a deliberate difference from
        /// <see cref="SporeBombCullFraction"/> (which already thins at Balanced).
        /// Roots has only ~12-23 spore areas in a whole level, against 400+ spore
        /// bombs: they're landmarks, not clutter, so removing any at the default
        /// preset would change the shape of the biome rather than just its
        /// fairness. Only the two most forgiving presets thin them, and even
        /// there the cluster-first rule means what goes is the overlap, not the
        /// landmark. Starting estimates pending playtest.
        /// </summary>
        public static double SporeAreaRemovalFraction(PresetId preset)
        {
            switch (CatalogKey(preset))
            {
                case PresetId.Subtle: return 0.00;
                case PresetId.Balanced: return 0.00;
                case PresetId.Generous: return 0.20;
                case PresetId.Tame: return 0.35;
                default: throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        /// <summary>
        /// Multiplier applied to a persistent spore area's
        /// <c>radius</c> (and, proportionally, its <c>innerFade</c>/<c>outerFade</c>
        /// and its cloud VFX scale - see <see cref="SporeAreaTuning"/>), per
        /// ROADMAP.md's "Spore area radius" row (-15%/-30%/-45%). 1.0 = vanilla
        /// (Subtle). Vanilla is <c>radius = 16</c> world units (~26m), so Balanced
        /// takes it to ~13.6 units. Starting estimates pending playtest.
        ///
        /// Not to be confused with <see cref="SporeBombSporeAreaRadiusMultiplier"/>,
        /// which is the *spore bomb's* temporary mini area.
        /// </summary>
        public static double SporeAreaRadiusMultiplier(PresetId preset)
        {
            switch (CatalogKey(preset))
            {
                case PresetId.Subtle: return 1.00;
                case PresetId.Balanced: return 0.85;
                case PresetId.Generous: return 0.70;
                case PresetId.Tame: return 0.55;
                default: throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        /// <summary>
        /// The unconditional bush/grass placement-removal pass is on for every
        /// preset, including Subtle (ROADMAP.md - the game never prevents a spore
        /// bomb landing in foliage, so this gap is always fixed). Kept as a method
        /// for symmetry / a future "let paranoid players disable even this" toggle.
        /// </summary>
        public static bool SporeBombFoliageRemoval(PresetId preset) => true;

        /// <summary>
        /// Climb-to-counter-wind counterplay mechanic - on for all presets.
        ///
        /// **Corrects an earlier misreading (2026-07-27).** The 2026-07-22
        /// decompile pass concluded this was tune-not-build because
        /// <c>WindChillZone.AddWindForceToCharacter</c> returns early whenever
        /// <c>character.data.currentClimbHandle != null</c>. That check only
        /// covers hanging off a climb *handle* prop - ordinary wall climbing
        /// (<c>CharacterData.isClimbing</c>), rope climbing and vine climbing all
        /// take full wind force in vanilla, and being shoved mid-climb drops the
        /// climb entirely (<c>CharacterClimbing.Update</c> lets go below 0.25
        /// ragdoll control). So it is built, not just tuned: see
        /// <see cref="ClimbWindResistance"/> for the mechanic and
        /// <c>Wind/ClimbWindShelterPatch.cs</c> for the patches. Whether the
        /// shelter applies at all is the flat, player-facing
        /// <c>Wind/climb-shelters-from-wind</c> toggle (on by default); what it
        /// costs is the three multipliers below.
        ///
        /// **Off on Subtle** (maintainer's call, 2026-07-27): Subtle's job is to
        /// leave vanilla mechanics as close to untouched as the mod gets, and
        /// handing out outright wind immunity is the least subtle thing in it.
        /// On for every other preset, and for Custom (which follows Balanced
        /// here). The player-facing toggle can turn it off on top of this, but
        /// can't turn it on under Subtle - a preset row that says "this mechanic
        /// doesn't exist here" outranks a per-player switch, same as every other
        /// preset value.
        /// </summary>
        public static bool ClimbToCounterWind(PresetId preset) => CatalogKey(preset) != PresetId.Subtle;

        /// <summary>
        /// Multiplier applied to climb speed in every direction while wind is
        /// actually pushing on the climber (<see cref="ClimbWindResistance.Resist"/>),
        /// the price of the wind immunity climbing now grants. Faded in by live
        /// wind pressure, so a climber the wind can't reach anyway (behind a rock,
        /// no gust) is never slowed at all. Gentler on Tame than on Balanced,
        /// matching every other row's direction - Tame is the most forgiving
        /// preset, and here "forgiving" means paying less for the shelter.
        /// Subtle's value is moot (the mechanic is off there - see
        /// <see cref="ClimbToCounterWind"/>) and is left at 1.0 rather than a
        /// number that would misleadingly imply a Subtle slowdown exists.
        ///
        /// **Balanced's 0.90 is playtest-tuned** (maintainer, 2026-07-27),
        /// replacing the original 0.55 starting estimate: with the immunity
        /// itself being the real prize, a heavy slowdown made waiting the gust
        /// out strictly better than climbing through it, which is the failure
        /// mode this mechanic exists to remove.
        /// </summary>
        public static double ClimbWindSpeedMultiplier(PresetId preset)
        {
            switch (CatalogKey(preset))
            {
                case PresetId.Subtle: return 1.00;
                case PresetId.Balanced: return 0.90;
                case PresetId.Generous: return 0.93;
                case PresetId.Tame: return 0.96;
                default: throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        /// <summary>
        /// Extra multiplier on *upward* climb movement only, on top of
        /// <see cref="ClimbWindSpeedMultiplier"/> - climbing up through a gust is
        /// the hardest thing you can do, per the maintainer's framing (2026-07-27).
        /// Downward movement is never penalised beyond the base multiplier.
        /// Balanced's 0.85 is playtest-tuned, Subtle's 1.0 is moot (mechanic off
        /// there) - see <see cref="ClimbWindSpeedMultiplier"/> for both.
        /// </summary>
        public static double ClimbWindUpwardSpeedMultiplier(PresetId preset)
        {
            switch (CatalogKey(preset))
            {
                case PresetId.Subtle: return 1.00;
                case PresetId.Balanced: return 0.85;
                case PresetId.Generous: return 0.89;
                case PresetId.Tame: return 0.94;
                default: throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        /// <summary>
        /// Extra multiplier on climb movement that opposes the wind direction, on
        /// top of <see cref="ClimbWindSpeedMultiplier"/>. Moving with the wind is
        /// never sped up - this mechanic is a cost, not a sail. Balanced's 0.85
        /// is playtest-tuned, Subtle's 1.0 is moot (mechanic off there) - see
        /// <see cref="ClimbWindSpeedMultiplier"/> for both.
        /// </summary>
        public static double ClimbWindIntoWindSpeedMultiplier(PresetId preset)
        {
            switch (CatalogKey(preset))
            {
                case PresetId.Subtle: return 1.00;
                case PresetId.Balanced: return 0.85;
                case PresetId.Generous: return 0.89;
                case PresetId.Tame: return 0.94;
                default: throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        /// <summary>Cover-mouth-vs-spore-areas counterplay mechanic - on for all presets.</summary>
        public static bool CoverMouth(PresetId preset) => true;

        /// <summary>
        /// Multiplier applied to wind force during the short window just after a
        /// player lets go of a climb (<see cref="ClimbWindResistance.GraceForceMultiplier"/>) -
        /// the fix for "finishing a climb catapults you," which is the worst
        /// moment in a gust (maintainer, 2026-07-27). Low but deliberately
        /// non-zero: full immunity here would let a player wall-tap their way
        /// across an exposed stretch. Gentler (lower force) on the more forgiving
        /// presets, same direction as every other row; Subtle's 1.0 is moot, the
        /// whole mechanic is off there (<see cref="ClimbToCounterWind"/>).
        /// Starting estimates pending playtest.
        /// </summary>
        public static double ClimbWindGraceForceMultiplier(PresetId preset)
        {
            switch (CatalogKey(preset))
            {
                case PresetId.Subtle: return 1.00;
                case PresetId.Balanced: return 0.15;
                case PresetId.Generous: return 0.12;
                case PresetId.Tame: return 0.08;
                default: throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        /// <summary>
        /// Multiplier applied to <c>WindChillZone.windForce</c> and, in the same
        /// direction, <c>windTimeRangeOn</c>'s duration - see
        /// <see cref="WindGustDurationMultiplier"/>, a separate dial (split out
        /// 2026-07-22 at the maintainer's request for independent testing -
        /// ROADMAP.md's preset table still lists them as one combined "Wind
        /// force / frequency" row since both use the same numbers per preset
        /// below, but they're two independent config entries/patches now, not
        /// one shared multiplier). 1.0 = vanilla (Subtle).
        /// </summary>
        public static double WindForceMultiplier(PresetId preset)
        {
            switch (CatalogKey(preset))
            {
                case PresetId.Subtle: return 0.90;
                case PresetId.Balanced: return 0.80;
                case PresetId.Generous: return 0.60;
                case PresetId.Tame: return 0.35;
                default: throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        /// <summary>
        /// Multiplier applied to <c>windTimeRangeOn</c>'s duration (with
        /// <c>windTimeRangeOff</c> scaled inversely - see
        /// <see cref="WindTuning.ScaleWindRestDuration"/>), per ROADMAP.md's
        /// combined "Wind force / frequency" row (-10%/-20%/-40%/-65%) - kept
        /// as the same numbers as <see cref="WindForceMultiplier"/> per preset
        /// so presets 1-4 behave identically to before the split, but resolved
        /// independently so Custom can tune gust duration/frequency without
        /// also changing push strength (and vice versa). 1.0 = vanilla (Subtle).
        /// </summary>
        public static double WindGustDurationMultiplier(PresetId preset)
        {
            switch (CatalogKey(preset))
            {
                case PresetId.Subtle: return 0.90;
                case PresetId.Balanced: return 0.80;
                case PresetId.Generous: return 0.60;
                case PresetId.Tame: return 0.35;
                default: throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        /// <summary>
        /// Multiplier applied to <c>WindChillZone.windItemFactor</c> for every
        /// non-backpack ground item (backpacks are always fully immune - see
        /// <see cref="ClimbToCounterWind"/>'s sibling remark on
        /// <c>WindChillZoneTuningPatch</c>), per ROADMAP.md's "Wind:
        /// items/backpack immunity" row: Subtle leaves other items untouched,
        /// Balanced/Generous progressively reduce it, Tame makes every item
        /// (including backpacks) fully immune.
        /// </summary>
        public static double WindItemForceMultiplier(PresetId preset)
        {
            switch (CatalogKey(preset))
            {
                case PresetId.Subtle: return 1.00;
                case PresetId.Balanced: return 0.70;
                case PresetId.Generous: return 0.40;
                case PresetId.Tame: return 0.00;
                default: throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        /// <summary>
        /// Multiplier applied to <c>WindChillZone.minRaycastDistance</c>/
        /// <c>maxRaycastDistance</c>, per ROADMAP.md's "Wind: obstacle occlusion"
        /// row. Runtime-confirmed (roots-runtime-findings memory) the raycast is
        /// already enabled in Roots (<c>useRaycast=true</c>, vanilla min=4/max=5) -
        /// this is a tune-not-build lever widening how far the occlusion check
        /// reaches, not a toggle. 1.0 = vanilla (Subtle).
        /// </summary>
        public static double WindObstacleOcclusionRangeMultiplier(PresetId preset)
        {
            switch (CatalogKey(preset))
            {
                case PresetId.Subtle: return 1.00;
                case PresetId.Balanced: return 1.30;
                case PresetId.Generous: return 1.60;
                case PresetId.Tame: return 2.00;
                default: throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        /// <summary>
        /// Floor value for <c>CharacterData.GetTargetRagdollControll()</c> while a
        /// fall is wind-preceded (see <see cref="WindTuning.IsWindForceStillRecent"/>),
        /// per ROADMAP.md's "Wind-induced fall camera spin dampening" row and the
        /// maintainer's scoping decision (2026-07-22): dampen only wind-preceded
        /// falls, not every fall, since an ordinary fall is the player's own doing
        /// but a wind-off-a-ledge fall is close to pure bad luck. 0 = off (Subtle -
        /// vanilla, no clamp).
        /// </summary>
        public static double WindFallCameraDampenClamp(PresetId preset)
        {
            switch (CatalogKey(preset))
            {
                case PresetId.Subtle: return 0.00;
                case PresetId.Balanced: return 0.35;
                case PresetId.Generous: return 0.55;
                case PresetId.Tame: return 0.75;
                default: throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }
    }
}
