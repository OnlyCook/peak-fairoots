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
