using Fairoots.Core;
using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// Pure arithmetic tests for the spore-bomb trigger-radius/knockback/
    /// screen-shake/VFX-count tuning (ROADMAP.md preset table rows), mirroring
    /// how <see cref="SporeBombCullTests"/> covers the cull decision - no
    /// Unity/BepInEx dependency needed, since none of this is seed-gated (see
    /// <see cref="SporeBombExplosionTuning"/>'s remarks).
    /// </summary>
    public class SporeBombExplosionTuningTests
    {
        [Fact]
        public void ScaleTriggerRadius_VanillaMultiplier_IsUnchanged()
        {
            Assert.Equal(5f, SporeBombExplosionTuning.ScaleTriggerRadius(5f, 1.0));
        }

        [Fact]
        public void ScaleTriggerRadius_AppliesMultiplier()
        {
            Assert.Equal(3.5f, SporeBombExplosionTuning.ScaleTriggerRadius(5f, 0.7), 3);
        }

        [Fact]
        public void ScaleKnockback_AppliesMultiplier()
        {
            Assert.Equal(20f, SporeBombExplosionTuning.ScaleKnockback(25f, 0.8), 3);
        }

        [Fact]
        public void ScaleKnockback_ZeroMultiplier_ZerosItOut()
        {
            Assert.Equal(0f, SporeBombExplosionTuning.ScaleKnockback(25f, 0.0));
        }

        [Theory]
        [InlineData(4, 1.0, 4)]
        [InlineData(4, 0.75, 3)]
        [InlineData(4, 0.50, 2)]
        [InlineData(4, 0.35, 1)]
        [InlineData(2, 0.0, 0)]
        public void ScaleVfxCount_RoundsAndNeverGoesNegative(int vanilla, double multiplier, int expected)
        {
            Assert.Equal(expected, SporeBombExplosionTuning.ScaleVfxCount(vanilla, multiplier));
        }

        [Fact]
        public void CapScreenshakeRange_NoCap_LeavesVanillaRangeAlone()
        {
            Assert.Equal(75f, SporeBombExplosionTuning.CapScreenshakeRange(75f, SporeBombExplosionTuning.NoScreenshakeCap));
        }

        [Fact]
        public void CapScreenshakeRange_AppliesCapWhenVanillaExceedsIt()
        {
            Assert.Equal(20f, SporeBombExplosionTuning.CapScreenshakeRange(75f, 20f));
        }

        [Fact]
        public void CapScreenshakeRange_NeverIncreasesATighterVanillaRange()
        {
            // A cap of 30m should never *widen* a vanilla range that's already 10m.
            Assert.Equal(10f, SporeBombExplosionTuning.CapScreenshakeRange(10f, 30f));
        }

        [Theory]
        [InlineData(0f, false)]
        [InlineData(-5f, false)]
        [InlineData(0.5f, true)]
        [InlineData(75f, true)]
        public void ShouldForcePositionalScreenshake_OnlyWhenACapIsConfigured(float capMeters, bool expected)
        {
            Assert.Equal(expected, SporeBombExplosionTuning.ShouldForcePositionalScreenshake(capMeters));
        }

        [Fact]
        public void ResolveScreenshakeRange_NoCap_LeavesVanillaRangeAlone()
        {
            Assert.Equal(15f, SporeBombExplosionTuning.ResolveScreenshakeRange(
                15f, vanillaPositional: false, SporeBombExplosionTuning.NoScreenshakeCap));
            Assert.Equal(15f, SporeBombExplosionTuning.ResolveScreenshakeRange(
                15f, vanillaPositional: true, SporeBombExplosionTuning.NoScreenshakeCap));
        }

        [Fact]
        public void ScreenshakeCap_InMeters_ConvertsToATighterWorldUnitRange()
        {
            // The regression this guards: a 75m cap written straight into
            // AddScreenshake.range (world units) reached 75 * 1.6 = 120m in-game, so a
            // detonation ~104m away still shook the camera. 75m is 46.9 world units.
            float capUnits = WorldUnits.MetersToUnits(75f, WorldUnits.DefaultUnitsToMeters);
            Assert.Equal(46.875f, capUnits, 3);

            float written = SporeBombExplosionTuning.ResolveScreenshakeRange(
                15f, vanillaPositional: false, capUnits);
            Assert.Equal(75f, WorldUnits.UnitsToMeters(written, WorldUnits.DefaultUnitsToMeters), 3);
        }

        [Fact]
        public void ResolveScreenshakeRange_NonPositionalVanilla_UsesTheCapVerbatim()
        {
            // A non-positional AddScreenshake never had its range read by the game, so
            // the serialized value (here the 15m component default) is meaningless -
            // clamping against it would give a 15m falloff for a player who asked for 75m.
            Assert.Equal(75f, SporeBombExplosionTuning.ResolveScreenshakeRange(
                15f, vanillaPositional: false, 75f));
        }

        [Fact]
        public void ResolveScreenshakeRange_PositionalVanilla_ClampsAgainstIt()
        {
            Assert.Equal(20f, SporeBombExplosionTuning.ResolveScreenshakeRange(
                75f, vanillaPositional: true, 20f));
            Assert.Equal(10f, SporeBombExplosionTuning.ResolveScreenshakeRange(
                10f, vanillaPositional: true, 30f));
        }

        [Theory]
        [InlineData(0f, 0f, true)]              // the detonation's own shake, same frame
        [InlineData(1.2f, 6f, true)]            // a staggered explosion orb nearby
        [InlineData(-0.5f, 1f, false)]          // "before" the detonation - not ours
        [InlineData(10f, 1f, false)]            // long after the detonation finished
        [InlineData(0.5f, 200f, false)]         // simultaneous but across the map
        public void IsDetonationScreenshake_OnlyMatchesInsideTheWindow(
            float ageSeconds, float distanceMeters, bool expected)
        {
            Assert.Equal(expected, SporeBombExplosionTuning.IsDetonationScreenshake(ageSeconds, distanceMeters));
        }

        [Fact]
        public void ScaleSporeAreaRadius_VanillaMultiplier_IsUnchanged()
        {
            Assert.Equal(16f, SporeBombExplosionTuning.ScaleSporeAreaRadius(16f, 1.0));
        }

        [Fact]
        public void ScaleSporeAreaRadius_AppliesMultiplier()
        {
            Assert.Equal(8f, SporeBombExplosionTuning.ScaleSporeAreaRadius(16f, 0.5), 3);
        }

        [Fact]
        public void ScaleSporeAreaRadius_ZeroMultiplier_ZerosItOut()
        {
            Assert.Equal(0f, SporeBombExplosionTuning.ScaleSporeAreaRadius(16f, 0.0));
        }

        [Fact]
        public void ShouldSuppressTriggerForHeight_DisabledCutoff_NeverSuppresses()
        {
            Assert.False(SporeBombExplosionTuning.ShouldSuppressTriggerForHeight(heightAboveBase: 100f, maxTriggerHeightMeters: 0f));
        }

        [Fact]
        public void ShouldSuppressTriggerForHeight_BelowCutoff_DoesNotSuppress()
        {
            Assert.False(SporeBombExplosionTuning.ShouldSuppressTriggerForHeight(heightAboveBase: 1.0f, maxTriggerHeightMeters: 1.2f));
        }

        [Fact]
        public void ShouldSuppressTriggerForHeight_AboveCutoff_Suppresses()
        {
            Assert.True(SporeBombExplosionTuning.ShouldSuppressTriggerForHeight(heightAboveBase: 2.0f, maxTriggerHeightMeters: 1.2f));
        }

        [Fact]
        public void ShouldSuppressTriggerForHeight_ExactlyAtCutoff_DoesNotSuppress()
        {
            // The boundary itself still counts as "touching it," not "jumped over."
            Assert.False(SporeBombExplosionTuning.ShouldSuppressTriggerForHeight(heightAboveBase: 1.2f, maxTriggerHeightMeters: 1.2f));
        }
    }
}
