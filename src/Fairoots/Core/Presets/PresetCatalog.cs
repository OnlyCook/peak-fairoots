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

        /// <summary>Climb-to-counter-wind counterplay mechanic - on for all presets.</summary>
        public static bool ClimbToCounterWind(PresetId preset) => true;

        /// <summary>Cover-mouth-vs-spore-areas counterplay mechanic - on for all presets.</summary>
        public static bool CoverMouth(PresetId preset) => true;
    }
}
