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
        public void NonCustomPreset_PurePreset_IgnoresConfiguredValue()
        {
            double resolved = OverrideResolution.Resolve(
                presetValue: 0.5,
                configuredValue: 0.9,
                defaultValue: 0.0,
                preset: PresetId.Balanced,
                applyPurePreset: true);
            Assert.Equal(0.5, resolved);
        }

        [Fact]
        public void CustomPreset_UsesConfiguredValue()
        {
            double resolved = OverrideResolution.Resolve(
                presetValue: 0.5,
                configuredValue: 0.9,
                defaultValue: 0.0,
                preset: PresetId.Custom,
                applyPurePreset: true);
            Assert.Equal(0.9, resolved);
        }

        [Fact]
        public void CustomPreset_ExplicitZero_IsRespected()
        {
            double resolved = OverrideResolution.Resolve(
                presetValue: 0.5,
                configuredValue: 0.0,
                defaultValue: 0.0,
                preset: PresetId.Custom,
                applyPurePreset: true);
            Assert.Equal(0.0, resolved);
        }

        [Fact]
        public void SwitchingAwayFromCustom_PurePreset_DiscardsConfiguredValue()
        {
            // Player set 0.33 under Custom. Any non-Custom preset ignores it and
            // returns its own catalog value instead, as long as apply-pure-preset
            // is on.
            const double configured = 0.33;
            foreach (PresetId p in new[] { PresetId.Subtle, PresetId.Balanced, PresetId.Generous, PresetId.Tame })
            {
                double resolved = OverrideResolution.Resolve(
                    PresetCatalog.SporeBombCullFraction(p),
                    configured,
                    ConfigDefaults.SporeBombCullFraction,
                    p,
                    applyPurePreset: true);
                Assert.Equal(PresetCatalog.SporeBombCullFraction(p), resolved);
            }
        }

        [Fact]
        public void NonCustomPreset_ImpurePreset_KeepsChangedValueButNotUntouchedOne()
        {
            // apply-pure-preset off: a setting left at its vanilla default still
            // takes the preset's own number...
            double untouched = OverrideResolution.Resolve(
                presetValue: 0.5,
                configuredValue: ConfigDefaults.SporeBombCullFraction,
                defaultValue: ConfigDefaults.SporeBombCullFraction,
                preset: PresetId.Balanced,
                applyPurePreset: false);
            Assert.Equal(0.5, untouched);

            // ...but a setting the player has actually changed from its default
            // keeps the player's own value instead of being overwritten.
            double changed = OverrideResolution.Resolve(
                presetValue: 0.5,
                configuredValue: 0.9,
                defaultValue: ConfigDefaults.SporeBombCullFraction,
                preset: PresetId.Balanced,
                applyPurePreset: false);
            Assert.Equal(0.9, changed);
        }

        [Fact]
        public void CustomPreset_ApplyPurePresetFlag_HasNoEffect()
        {
            // Under Custom, the player's own value always wins regardless of
            // apply-pure-preset - the flag is only meaningful for presets 1-4.
            double resolved = OverrideResolution.Resolve(
                presetValue: 0.5,
                configuredValue: ConfigDefaults.SporeBombCullFraction,
                defaultValue: ConfigDefaults.SporeBombCullFraction,
                preset: PresetId.Custom,
                applyPurePreset: false);
            Assert.Equal(ConfigDefaults.SporeBombCullFraction, resolved);
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
            // No "Subtle is exactly vanilla" anchor here, deliberately. Subtle is the
            // *lightest* preset, not a vanilla one - a tuning pass is free to give it
            // a small shrink (0.90 as of 2026-07-30), and pinning 1.0 would fail the
            // build for a legitimate balance decision. What must hold is the shape,
            // which is what AssertRunsFromVanilla covers: Subtle never harsher than
            // vanilla, the scale running one way, Tame strictly furthest.
            AssertRunsFromVanilla(
                "trigger-radius-multiplier", PresetCatalog.SporeBombTriggerRadiusMultiplier,
                vanilla: 1.0, awayFromVanillaIsUp: false);
        }

        [Fact]
        public void SporeBombKnockbackMultiplier_FallsAsPresetsGetForgiving()
        {
            AssertRunsFromVanilla(
                "knockback-multiplier", PresetCatalog.SporeBombKnockbackMultiplier,
                vanilla: 1.0, awayFromVanillaIsUp: false);
        }

        [Fact]
        public void SporeBombScreenshakeRangeCap_TightensAsPresetsGetForgiving()
        {
            // This row can't use AssertRunsFromVanilla: vanilla is "uncapped", which
            // the mechanic spells as 0 rather than as a large number, so a numeric
            // distance from vanilla runs backwards. The scale is checked directly
            // instead - every preset that caps at all must cap at least as hard as
            // the one before it, and the last must cap hardest.
            //
            // Subtle is NOT required to leave the range uncapped. It was originally,
            // but an uncapped shake is one of the mod's loudest complaints (a
            // detonation across the map shaking everyone), so a tuning pass giving
            // even the lightest preset a wide cap is a legitimate call - 75m as of
            // 2026-07-30. All that's pinned is that a preset which does cap uses a
            // positive number.
            double[] caps = Scale.Select(PresetCatalog.SporeBombScreenshakeRangeCapMeters).ToArray();
            Assert.All(caps, c => Assert.True(
                c >= SporeBombExplosionTuning.NoScreenshakeCap,
                "a screen-shake cap is either 0 (uncapped) or a positive distance"));

            double[] capping = caps.Where(c => c > SporeBombExplosionTuning.NoScreenshakeCap).ToArray();
            for (int i = 1; i < capping.Length; i++)
            {
                Assert.True(capping[i] <= capping[i - 1] + 1e-9, "screen-shake caps must tighten, never loosen");
            }

            Assert.True(capping.Length > 0, "no preset caps the screen shake at all - the row does nothing");
            Assert.True(
                capping[capping.Length - 1] < capping[0],
                "the most forgiving preset must cap hardest");
        }

        [Fact]
        public void SporeBombVfxCountMultiplier_FallsAsPresetsGetForgiving()
        {
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
