using Fairoots.Core;
using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// The persistent spore-bomb overlay's "am I actually being spored?" rule. The
    /// load-bearing property is that it agrees with the native <c>AOE.Explode</c>
    /// falloff instead of using the advertised radius: an overlay that lights up
    /// where nothing can hurt you defeats the entire point of the setting, which is
    /// to make the overlay trustworthy.
    /// </summary>
    public class SporeBombCloudPresenceTests
    {
        // The native defaults an AOE ships with.
        private const double Range = 5.0;
        private const double MinFactor = 0.2;
        private const double FactorPow = 1.0;

        [Fact]
        public void DeadCentre_IsInside()
        {
            Assert.True(SporeBombCloudPresence.IsInsideStatusRange(0.0, Range, MinFactor, FactorPow));
        }

        [Fact]
        public void OutsideTheRadius_IsNotInside()
        {
            Assert.False(SporeBombCloudPresence.IsInsideStatusRange(Range + 0.01, Range, MinFactor, FactorPow));
        }

        [Fact]
        public void TheOuterShellIsExcluded_NotJustTheOutsideOfTheRadius()
        {
            // With minFactor 0.2 and a linear falloff, the outer 20% of the radius
            // applies nothing at all. Standing there must not raise the overlay.
            Assert.False(SporeBombCloudPresence.IsInsideStatusRange(4.5, Range, MinFactor, FactorPow));
            Assert.True(SporeBombCloudPresence.IsInsideStatusRange(3.5, Range, MinFactor, FactorPow));
        }

        [Fact]
        public void TheBoundaryIsWhereTheGamePutsIt()
        {
            // The native check skips on `factor < minFactor`, so the cutoff sits at
            // distance = range * (1 - minFactor) = 4. Asserted from either side rather
            // than exactly on it: at the boundary the comparison is decided by the
            // last bit of a subtraction (1 - 0.8 is not 0.2 in binary floating point),
            // which is a property of the arithmetic, not of this rule - and the game's
            // own float version has the same wobble.
            Assert.True(SporeBombCloudPresence.IsInsideStatusRange(3.99, Range, MinFactor, FactorPow));
            Assert.False(SporeBombCloudPresence.IsInsideStatusRange(4.01, Range, MinFactor, FactorPow));
        }

        [Fact]
        public void ZeroRange_IsNeverInside_MatchingTheNativeEarlyReturn()
        {
            Assert.False(SporeBombCloudPresence.IsInsideStatusRange(0.0, 0.0, MinFactor, FactorPow));
        }

        [Fact]
        public void ScalingTheRadiusScalesTheOverlay_SoTheTwoCantDisagree()
        {
            // A spot that's inside a doubled cloud but outside a vanilla one. This is
            // what keeps the overlay honest when spore-area-radius-multiplier moves.
            Assert.False(SporeBombCloudPresence.IsInsideStatusRange(6.0, Range, MinFactor, FactorPow));
            Assert.True(SporeBombCloudPresence.IsInsideStatusRange(6.0, Range * 2, MinFactor, FactorPow));
        }

        [Theory]
        [InlineData(0.5)]
        [InlineData(2.0)]
        public void ANonLinearFalloffStillCutsOffInsideTheRadius(double factorPow)
        {
            Assert.True(SporeBombCloudPresence.IsInsideStatusRange(0.0, Range, MinFactor, factorPow));
            Assert.False(SporeBombCloudPresence.IsInsideStatusRange(Range, Range, MinFactor, factorPow));
        }

        [Fact]
        public void NonPositiveExponent_DoesNotSilentlyBecomeAnywhereInRange()
        {
            // Math.Pow(x, 0) is 1 for every x, which would make the whole radius
            // count as full strength - it falls back to the linear ramp instead.
            Assert.False(SporeBombCloudPresence.IsInsideStatusRange(4.9, Range, MinFactor, 0.0));
        }

        [Fact]
        public void FactorFallsOffWithDistance()
        {
            double near = SporeBombCloudPresence.Factor(1.0, Range, FactorPow);
            double far = SporeBombCloudPresence.Factor(4.0, Range, FactorPow);

            Assert.True(near > far);
            Assert.Equal(0.8, near, 6);
        }
    }
}
