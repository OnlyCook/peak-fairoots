using Fairoots.Core;
using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// Spore-cloud translucency arithmetic. Not seed-gated (every cloud gets the
    /// same flat treatment), so these are invariant proofs rather than determinism
    /// ones - the load-bearing ones being that 1.0 is bit-exactly vanilla (the
    /// restore path depends on it) and that the result can never leave the valid
    /// alpha range, since it is written straight into a color channel.
    /// </summary>
    public class SporeCloudOpacityTests
    {
        [Theory]
        [InlineData(0f)]
        [InlineData(0.25f)]
        [InlineData(0.6f)]
        [InlineData(1f)]
        public void MultiplierOfOne_IsExactlyVanilla(float alpha)
        {
            Assert.Equal(alpha, SporeCloudOpacity.ScaleAlpha(alpha, 1.0));
            Assert.True(SporeCloudOpacity.IsVanilla(1.0));
        }

        [Fact]
        public void ZeroMultiplier_IsFullyInvisible()
        {
            Assert.Equal(0f, SporeCloudOpacity.ScaleAlpha(1f, 0.0));
            Assert.False(SporeCloudOpacity.IsVanilla(0.0));
        }

        [Fact]
        public void RelativeAlphaVariationSurvives_TheCloudStaysAVolume()
        {
            // Two particles the artist authored at different alphas must still
            // differ by the same *ratio* afterwards - that internal variation is
            // what reads as a soft volume rather than a flat sheet.
            float dense = SporeCloudOpacity.ScaleAlpha(0.8f, 0.25);
            float faint = SporeCloudOpacity.ScaleAlpha(0.2f, 0.25);

            Assert.Equal(0.8f / 0.2f, dense / faint, 4);
        }

        [Theory]
        [InlineData(0.5f, 0.35)]
        [InlineData(1f, 0.1)]
        [InlineData(0.05f, 0.9)]
        public void ResultAlwaysStaysInValidAlphaRange(float alpha, double multiplier)
        {
            float result = SporeCloudOpacity.ScaleAlpha(alpha, multiplier);

            Assert.InRange(result, 0f, 1f);
            Assert.True(result <= alpha, "thinning must never make a cloud denser than vanilla");
        }

        [Fact]
        public void NegativeMultiplier_ClampsToZero_NeverToANegativeChannel()
        {
            Assert.Equal(0f, SporeCloudOpacity.ScaleAlpha(0.8f, -2.0));
        }

        [Fact]
        public void AboveOneMultiplier_CountsAsVanilla_SoRestoreStaysExact()
        {
            Assert.True(SporeCloudOpacity.IsVanilla(1.5));
        }
    }
}
