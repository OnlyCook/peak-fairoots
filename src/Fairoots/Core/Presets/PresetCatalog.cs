using System;

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
        /// Target fraction of spore bombs to remove overall (foliage + seeded cull
        /// combined), per ROADMAP.md's "Spore bomb total removal target" row.
        /// Preset 1 removes nothing beyond the always-on foliage pass; Preset 3 is
        /// OVERVIEW.md's literal "cut them in half" ask.
        /// </summary>
        public static double SporeBombCullFraction(PresetId preset)
        {
            switch (preset)
            {
                case PresetId.Subtle: return 0.00;
                case PresetId.Balanced: return 0.25;
                case PresetId.Generous: return 0.50;
                case PresetId.Tame: return 0.75;
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
