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
        /// The unconditional bush/grass placement-removal pass is on for every
        /// preset, including Subtle (ROADMAP.md - the game never prevents a spore
        /// bomb landing in foliage, so this gap is always fixed). Kept as a method
        /// for symmetry / a future "let paranoid players disable even this" toggle.
        /// </summary>
        public static bool SporeBombFoliageRemoval(PresetId preset) => true;

        /// <summary>
        /// Climb-to-counter-wind counterplay mechanic - on for all presets.
        /// Runtime-confirmed (2026-07-22 decompile pass, RESEARCH.md Q6):
        /// <c>WindChillZone.AddWindForceToCharacter</c> already returns
        /// immediately whenever <c>character.data.currentClimbHandle != null</c>
        /// (i.e. the player is actively gripping a climb handle) - wind force is
        /// already fully suppressed while climbing/hanging in vanilla, and
        /// <c>ApplyStatus</c> already raises <c>climbingStamMinimumMultiplier</c>
        /// (the stamina cost) during wind regardless. This mechanic is therefore
        /// tune-not-build, like the spore-area wind-dispersal feature turned out
        /// to be - there's nothing to patch, this flag exists purely so the
        /// preset table has a row to point at confirming the behavior stays on.
        /// </summary>
        public static bool ClimbToCounterWind(PresetId preset) => true;

        /// <summary>Cover-mouth-vs-spore-areas counterplay mechanic - on for all presets.</summary>
        public static bool CoverMouth(PresetId preset) => true;

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
