using Fairoots.Core.Presets;
using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// Preset non-destructiveness (ROADMAP.md "Testing strategy"): applying or
    /// switching a preset must never overwrite a value the player explicitly set,
    /// tested at the resolution-logic level, not through the UI. Also pins the
    /// preset scale's ordering so a future edit can't silently flip it.
    /// </summary>
    public class PresetResolutionTests
    {
        [Fact]
        public void UntouchedSetting_FollowsPreset()
        {
            double resolved = OverrideResolution.Resolve(
                presetValue: 0.5,
                configuredValue: OverrideResolution.FollowPreset);
            Assert.Equal(0.5, resolved);
        }

        [Fact]
        public void ExplicitSetting_OverridesPreset()
        {
            double resolved = OverrideResolution.Resolve(
                presetValue: 0.5,
                configuredValue: 0.9);
            Assert.Equal(0.9, resolved);
        }

        [Fact]
        public void ExplicitZero_IsRespected_NotTreatedAsUnset()
        {
            // 0 is a legitimate value (cull nothing) and must not be confused with
            // the -1 "follow preset" sentinel.
            double resolved = OverrideResolution.Resolve(
                presetValue: 0.5,
                configuredValue: 0.0);
            Assert.Equal(0.0, resolved);
        }

        [Fact]
        public void SwitchingPreset_DoesNotClobberExplicitValue()
        {
            // Player pinned 0.33. Whatever preset is active, resolution returns 0.33.
            const double pinned = 0.33;
            foreach (PresetId p in new[] { PresetId.Subtle, PresetId.Balanced, PresetId.Generous, PresetId.Tame })
            {
                double resolved = OverrideResolution.Resolve(
                    PresetCatalog.SporeBombCullFraction(p), pinned);
                Assert.Equal(pinned, resolved);
            }
        }

        [Fact]
        public void GenericResolve_WorksForEnums()
        {
            // configured wins unless it's the sentinel.
            Assert.Equal(PresetId.Tame,
                OverrideResolution.Resolve(PresetId.Balanced, PresetId.Tame, sentinel: PresetId.Subtle));
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
    }
}
