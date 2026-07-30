using System;
using System.Linq;
using Fairoots.Core;
using Fairoots.Core.Presets;
using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// Preset vs. Custom resolution (ROADMAP.md "Testing strategy"): presets 1-4
    /// always use their own catalog numbers, ignoring whatever the player has
    /// configured; Custom (5) always uses the player's configured value, 0
    /// included, tested at the resolution-logic level, not through the UI. Also
    /// pins the preset scale's ordering so a future edit can't silently flip it.
    ///
    /// <b>These tests deliberately assert shape, not values.</b> The numbers live in
    /// <c>docs/PRESETS.md</c> and are re-tuned between play sessions (see
    /// <c>scripts/apply-presets.sh</c>), so pinning "Balanced is 0.25" here would
    /// mean every tuning pass breaks the build and the tuning loop stops being worth
    /// using. What must not change silently is the direction of the scale - Subtle
    /// closest to vanilla, each later preset at least as forgiving - and that is what
    /// is pinned. Anchors on specific values are kept only where the value is a
    /// design invariant rather than a tuning choice (Subtle being exactly vanilla for
    /// a given row, or a fraction staying inside 0-1).
    /// </summary>
    public class PresetResolutionTests
    {
        [Fact]
        public void NonCustomPreset_IgnoresConfiguredValue()
        {
            double resolved = OverrideResolution.Resolve(
                presetValue: 0.5,
                configuredValue: 0.9,
                useOverride: false);
            Assert.Equal(0.5, resolved);
        }

        [Fact]
        public void CustomPreset_UsesConfiguredValue()
        {
            double resolved = OverrideResolution.Resolve(
                presetValue: 0.5,
                configuredValue: 0.9,
                useOverride: true);
            Assert.Equal(0.9, resolved);
        }

        [Fact]
        public void CustomPreset_ExplicitZero_IsRespected()
        {
            double resolved = OverrideResolution.Resolve(
                presetValue: 0.5,
                configuredValue: 0.0,
                useOverride: true);
            Assert.Equal(0.0, resolved);
        }

        [Fact]
        public void SwitchingAwayFromCustom_DiscardsConfiguredValue()
        {
            // Player set 0.33 under Custom. Any non-Custom preset ignores it and
            // returns its own catalog value instead.
            const double configured = 0.33;
            foreach (PresetId p in new[] { PresetId.Subtle, PresetId.Balanced, PresetId.Generous, PresetId.Tame })
            {
                double resolved = OverrideResolution.Resolve(
                    PresetCatalog.SporeBombCullFraction(p), configured, useOverride: false);
                Assert.Equal(PresetCatalog.SporeBombCullFraction(p), resolved);
            }
        }

        /// <summary>
        /// The four presets in scale order, Subtle (lightest touch) first.
        /// </summary>
        private static readonly PresetId[] Scale =
        {
            PresetId.Subtle, PresetId.Balanced, PresetId.Generous, PresetId.Tame,
        };

        /// <summary>
        /// Asserts a row runs the way the preset scale promises: each preset at least
        /// as far from vanilla as the one before it, and Tame strictly further than
        /// Subtle (a row identical across all four is not a scale - it's an unfilled
        /// or deliberately-vanilla row, and <c>ConfigDefaultsTests</c> is what tracks
        /// those). Ties in between are allowed: two neighbouring presets landing on
        /// the same number is a legitimate tuning outcome.
        /// </summary>
        private static void AssertRunsFromVanilla(
            string row, Func<PresetId, double> value, double vanilla, bool awayFromVanillaIsUp)
        {
            double[] distances = Scale
                .Select(p => awayFromVanillaIsUp ? value(p) - vanilla : vanilla - value(p))
                .ToArray();

            for (int i = 1; i < distances.Length; i++)
            {
                Assert.True(
                    distances[i] >= distances[i - 1] - 1e-9,
                    $"{row}: {Scale[i]} ({value(Scale[i])}) is closer to vanilla than "
                    + $"{Scale[i - 1]} ({value(Scale[i - 1])}) - the scale runs one way only");
            }

            Assert.True(
                distances[0] >= -1e-9,
                $"{row}: Subtle ({value(PresetId.Subtle)}) sits on the wrong side of vanilla ({vanilla})");
            Assert.True(
                distances[distances.Length - 1] > distances[0] + 1e-9,
                $"{row}: Tame is no further from vanilla than Subtle - the row does nothing");
        }

        [Fact]
        public void SporeBombCullFraction_RunsUpFromRemovingNothing()
        {
            // Vanilla removes none; the heavier presets remove more. Subtle removing
            // exactly nothing beyond the foliage pass is the design invariant here.
            Assert.Equal(0.0, PresetCatalog.SporeBombCullFraction(PresetId.Subtle));
            AssertRunsFromVanilla(
                "cull-fraction", PresetCatalog.SporeBombCullFraction, vanilla: 0.0, awayFromVanillaIsUp: true);
            foreach (PresetId p in Scale)
            {
                Assert.InRange(PresetCatalog.SporeBombCullFraction(p), 0.0, 1.0);
            }
        }

        [Fact]
        public void SporeBombTriggerRadiusMultiplier_ShrinksAsPresetsGetForgiving()
        {
            Assert.Equal(1.0, PresetCatalog.SporeBombTriggerRadiusMultiplier(PresetId.Subtle));
            AssertRunsFromVanilla(
                "trigger-radius-multiplier", PresetCatalog.SporeBombTriggerRadiusMultiplier,
                vanilla: 1.0, awayFromVanillaIsUp: false);
        }

        [Fact]
        public void SporeBombKnockbackMultiplier_FallsAsPresetsGetForgiving()
        {
            Assert.Equal(1.0, PresetCatalog.SporeBombKnockbackMultiplier(PresetId.Subtle));
            AssertRunsFromVanilla(
                "knockback-multiplier", PresetCatalog.SporeBombKnockbackMultiplier,
                vanilla: 1.0, awayFromVanillaIsUp: false);
        }

        [Fact]
        public void SporeBombScreenshakeRangeCap_TightensAsPresetsGetForgiving()
        {
            // Subtle leaves the vanilla range uncapped, which this mechanic spells
            // as 0 rather than a large number - so the scale can't just be compared
            // numerically against vanilla, it runs downward from the loosest cap.
            Assert.Equal(
                SporeBombExplosionTuning.NoScreenshakeCap,
                PresetCatalog.SporeBombScreenshakeRangeCapMeters(PresetId.Subtle));

            double[] caps = Scale
                .Where(p => p != PresetId.Subtle)
                .Select(PresetCatalog.SporeBombScreenshakeRangeCapMeters)
                .ToArray();
            Assert.All(caps, c => Assert.True(c > SporeBombExplosionTuning.NoScreenshakeCap));
            for (int i = 1; i < caps.Length; i++)
            {
                Assert.True(caps[i] <= caps[i - 1] + 1e-9, "screen-shake caps must tighten, never loosen");
            }
            Assert.True(caps[caps.Length - 1] < caps[0], "the most forgiving preset must cap hardest");
        }

        [Fact]
        public void SporeBombVfxCountMultiplier_FallsAsPresetsGetForgiving()
        {
            Assert.Equal(1.0, PresetCatalog.SporeBombVfxCountMultiplier(PresetId.Subtle));
            AssertRunsFromVanilla(
                "vfx-count-multiplier", PresetCatalog.SporeBombVfxCountMultiplier,
                vanilla: 1.0, awayFromVanillaIsUp: false);
        }

        [Fact]
        public void AlwaysOnMechanics_OnForEveryPreset()
        {
            foreach (PresetId p in new[] { PresetId.Subtle, PresetId.Balanced, PresetId.Generous, PresetId.Tame })
            {
                Assert.True(PresetCatalog.EnableFoliageRemoval(p));
                Assert.True(PresetCatalog.EnableCoverMouth(p));
            }

            // Climb-to-counter-wind used to belong on that list. It stopped being
            // unconditional on 2026-07-27, when it went from "a note about vanilla
            // behavior" to a real, patched mechanic granting outright wind
            // immunity - too strong for Subtle, which exists to leave vanilla
            // mechanics alone. See ClimbWindResistanceTests for its own coverage.
            Assert.False(PresetCatalog.ClimbSheltersFromWind(PresetId.Subtle));
            foreach (PresetId p in new[] { PresetId.Balanced, PresetId.Generous, PresetId.Tame })
            {
                Assert.True(PresetCatalog.ClimbSheltersFromWind(p));
            }
        }

        [Fact]
        public void SporeBombTriggerRadiusMultiplier_MatchesRoadmapTable()
        {
            Assert.Equal(1.00, PresetCatalog.SporeBombTriggerRadiusMultiplier(PresetId.Subtle));
            Assert.Equal(0.75, PresetCatalog.SporeBombTriggerRadiusMultiplier(PresetId.Balanced));
            Assert.Equal(0.70, PresetCatalog.SporeBombTriggerRadiusMultiplier(PresetId.Generous));
            Assert.Equal(0.55, PresetCatalog.SporeBombTriggerRadiusMultiplier(PresetId.Tame));
        }

        [Fact]
        public void SporeBombKnockbackMultiplier_MatchesRoadmapTable()
        {
            Assert.Equal(1.00, PresetCatalog.SporeBombKnockbackMultiplier(PresetId.Subtle));
            Assert.Equal(0.80, PresetCatalog.SporeBombKnockbackMultiplier(PresetId.Balanced));
            Assert.Equal(0.60, PresetCatalog.SporeBombKnockbackMultiplier(PresetId.Generous));
            Assert.Equal(0.40, PresetCatalog.SporeBombKnockbackMultiplier(PresetId.Tame));
        }

        [Fact]
        public void SporeBombScreenshakeRangeCapMeters_MatchesRoadmapTable()
        {
            Assert.Equal(SporeBombExplosionTuning.NoScreenshakeCap, PresetCatalog.SporeBombScreenshakeRangeCapMeters(PresetId.Subtle));
            Assert.Equal(30f, PresetCatalog.SporeBombScreenshakeRangeCapMeters(PresetId.Balanced));
            Assert.Equal(20f, PresetCatalog.SporeBombScreenshakeRangeCapMeters(PresetId.Generous));
            Assert.Equal(10f, PresetCatalog.SporeBombScreenshakeRangeCapMeters(PresetId.Tame));
        }

        [Fact]
        public void SporeBombVfxCountMultiplier_MatchesRoadmapTable()
        {
            Assert.Equal(1.00, PresetCatalog.SporeBombVfxCountMultiplier(PresetId.Subtle));
            Assert.Equal(0.75, PresetCatalog.SporeBombVfxCountMultiplier(PresetId.Balanced));
            Assert.Equal(0.50, PresetCatalog.SporeBombVfxCountMultiplier(PresetId.Generous));
            Assert.Equal(0.35, PresetCatalog.SporeBombVfxCountMultiplier(PresetId.Tame));
        }

        [Fact]
        public void CustomPreset_CatalogLookup_FallsBackToBalancedNumbers()
        {
            // Custom has no catalog numbers of its own - every catalog method must
            // fall back to Balanced's value rather than throwing or returning
            // garbage (used as PluginConfig's presetValue argument even when
            // useOverride discards it, so it must never blow up).
            Assert.Equal(PresetCatalog.SporeBombCullFraction(PresetId.Balanced), PresetCatalog.SporeBombCullFraction(PresetId.Custom));
            Assert.Equal(PresetCatalog.SporeBombTriggerRadiusMultiplier(PresetId.Balanced), PresetCatalog.SporeBombTriggerRadiusMultiplier(PresetId.Custom));
            Assert.Equal(PresetCatalog.SporeBombKnockbackMultiplier(PresetId.Balanced), PresetCatalog.SporeBombKnockbackMultiplier(PresetId.Custom));
            Assert.Equal(PresetCatalog.SporeBombScreenshakeRangeCapMeters(PresetId.Balanced), PresetCatalog.SporeBombScreenshakeRangeCapMeters(PresetId.Custom));
            Assert.Equal(PresetCatalog.SporeBombVfxCountMultiplier(PresetId.Balanced), PresetCatalog.SporeBombVfxCountMultiplier(PresetId.Custom));
        }
    }
}
