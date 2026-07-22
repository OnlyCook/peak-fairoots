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

        [Fact]
        public void SporeBombCullFraction_MatchesRoadmapTable()
        {
            Assert.Equal(0.00, PresetCatalog.SporeBombCullFraction(PresetId.Subtle));
            Assert.Equal(0.25, PresetCatalog.SporeBombCullFraction(PresetId.Balanced));
            Assert.Equal(0.50, PresetCatalog.SporeBombCullFraction(PresetId.Generous));
            Assert.Equal(0.75, PresetCatalog.SporeBombCullFraction(PresetId.Tame));
        }

        [Fact]
        public void CullFraction_IncreasesMonotonicallyWithPresetStrength()
        {
            Assert.True(
                PresetCatalog.SporeBombCullFraction(PresetId.Subtle) <
                PresetCatalog.SporeBombCullFraction(PresetId.Balanced));
            Assert.True(
                PresetCatalog.SporeBombCullFraction(PresetId.Balanced) <
                PresetCatalog.SporeBombCullFraction(PresetId.Generous));
            Assert.True(
                PresetCatalog.SporeBombCullFraction(PresetId.Generous) <
                PresetCatalog.SporeBombCullFraction(PresetId.Tame));
        }

        [Fact]
        public void AlwaysOnMechanics_OnForEveryPreset()
        {
            foreach (PresetId p in new[] { PresetId.Subtle, PresetId.Balanced, PresetId.Generous, PresetId.Tame })
            {
                Assert.True(PresetCatalog.SporeBombFoliageRemoval(p));
                Assert.True(PresetCatalog.ClimbToCounterWind(p));
                Assert.True(PresetCatalog.CoverMouth(p));
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
