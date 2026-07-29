using Fairoots.Core;
using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// The throw-an-item-at-it knockout. Arithmetic and range rules only - what the
    /// knockout actually does to each creature is scene behaviour, but the "how long"
    /// and "does this hit count" decisions are pure and worth pinning down, since both
    /// dials accept a raw duration straight from a config file.
    /// </summary>
    public class CreatureKnockoutTests
    {
        [Fact]
        public void ZeroDisablesTheMechanic_AndIsReachable()
        {
            // 0 is a real setting, not an error: it means "leave vanilla alone".
            Assert.True(CreatureKnockout.IsDisabled(0.0));
            Assert.Equal(0f, CreatureKnockout.ResolveSeconds(0.0));
        }

        [Theory]
        [InlineData(-1.0)]
        [InlineData(double.NaN)]
        public void NonsenseDurationsCollapseToDisabledRatherThanThrowing(double seconds)
        {
            Assert.Equal(0f, CreatureKnockout.ResolveSeconds(seconds));
            Assert.True(CreatureKnockout.IsDisabled(seconds));
        }

        [Fact]
        public void AbsurdDurationsAreCappedRatherThanRemovingTheCreatureFromTheRun()
        {
            Assert.Equal(CreatureKnockout.MaxSeconds, CreatureKnockout.ResolveSeconds(10000.0));
        }

        [Theory]
        [InlineData(2.0)]
        [InlineData(4.0)]
        [InlineData(4.9)]
        public void SaneDurationsPassThroughUntouched(double seconds)
        {
            Assert.Equal((float)seconds, CreatureKnockout.ResolveSeconds(seconds), 4);
            Assert.False(CreatureKnockout.IsDisabled(seconds));
        }

        [Fact]
        public void DefaultsSitBelowTheSpidersVanillaStun()
        {
            // The maintainer's constraint: both new knockouts are meant to be weaker
            // than the 5s the game already grants against spiders, and the beetle's
            // weaker again because of its shell. These are the shipped defaults.
            const double zombieDefault = 4.0;
            const double beetleDefault = 2.0;

            Assert.True(CreatureKnockout.ResolveSeconds(zombieDefault) < CreatureKnockout.SpiderStunSeconds);
            Assert.True(CreatureKnockout.ResolveSeconds(beetleDefault) < CreatureKnockout.ResolveSeconds(zombieDefault));
        }

        // --- The hard-throw gate (live-corrected 2026-07-29) -----------------
        // Matching the game's own Bonkable threshold of 5 world units/s accepted
        // essentially any contact, including the gentlest possible toss. These pin down
        // the configurable replacement.

        [Fact]
        public void ThresholdIsWellAboveVanillasBonkableValue()
        {
            // Vanilla's 5 units/s accepted any contact at all. If a future edit drops the
            // default back near it, the soft-throw bug returns.
            const float unitsToMeters = 1.6f; // CharacterStats.unitsToMeters
            float defaultUnits = (float)(CreatureKnockout.CalibratedMinThrowSpeedMeters / unitsToMeters);

            Assert.True(defaultUnits > CreatureKnockout.VanillaBonkableThresholdUnits * 3f);
        }

        [Theory]
        // Measured in-game and judged too gentle to deserve a knockout.
        [InlineData(23.0, false)]
        [InlineData(26.3, false)]
        [InlineData(30.6, false)]
        // Measured near-full-strength throws, which must still work.
        [InlineData(36.6, true)]
        [InlineData(42.5, true)]
        public void TheDefaultSeparatesRealThrowsFromMediumOnes(double measuredMeters, bool shouldCount)
        {
            // The whole point of the calibrated default: these are real logged impacts, so
            // this test fails if anyone retunes the threshold past the gap between them.
            float threshold = CreatureKnockout.ResolveMinThrowSpeedMeters(
                CreatureKnockout.CalibratedMinThrowSpeedMeters);

            Assert.Equal(shouldCount, CreatureKnockout.IsHardEnough((float)measuredMeters, threshold));
        }

        [Fact]
        public void TheDefaultStaysBelowTheWeakestNearMaxThrow()
        {
            // The buffer requirement: a full-strength throw must not have to be
            // frame-perfect, so the bar sits strictly under the weakest near-max throw
            // actually logged (36.6). The margin is deliberately thin - 36 is the value
            // the maintainer confirmed by feel in-game, not one derived arithmetically.
            float threshold = CreatureKnockout.ResolveMinThrowSpeedMeters(
                CreatureKnockout.CalibratedMinThrowSpeedMeters);

            Assert.True(threshold < 36.6f);
            Assert.True(threshold > 31f); // still clears every throw judged too gentle
        }

        [Fact]
        public void OnlyThrowsAtOrAboveTheThresholdCount()
        {
            const float threshold = 22.5f; // 36 m/s in world units

            Assert.False(CreatureKnockout.IsHardEnough(0f, threshold));
            Assert.False(CreatureKnockout.IsHardEnough(threshold - 0.01f, threshold));
            Assert.True(CreatureKnockout.IsHardEnough(threshold, threshold));
            Assert.True(CreatureKnockout.IsHardEnough(threshold + 20f, threshold));
        }

        // --- The distance gate ------------------------------------------------

        [Fact]
        public void DistanceGate_FarThrowsAreRejectedEvenAtFullSpeed()
        {
            // Speed alone can't express "commit to getting close": a hard throw is still
            // fast a long way out, so without this you could snipe from safety.
            float limit = CreatureKnockout.ResolveMaxThrowDistanceMeters(12.0);

            Assert.True(CreatureKnockout.IsCloseEnough(limit, limit));
            Assert.True(CreatureKnockout.IsCloseEnough(limit - 5f, limit));
            Assert.False(CreatureKnockout.IsCloseEnough(limit + 0.01f, limit));
            Assert.False(CreatureKnockout.IsCloseEnough(limit * 4f, limit));
        }

        [Fact]
        public void DistanceGate_ZeroMeansNoLimit_NotMustBeTouching()
        {
            // 0 reads the same way it does on the speed threshold: no requirement.
            Assert.True(CreatureKnockout.IsCloseEnough(500f, 0f));
            Assert.Equal(0f, CreatureKnockout.ResolveMaxThrowDistanceMeters(0.0));
        }

        [Theory]
        [InlineData(-5.0, 0.0)]
        [InlineData(double.NaN, 0.0)]
        [InlineData(12.0, 12.0)]
        [InlineData(9999.0, CreatureKnockout.MaxThrowDistanceMeters)]
        public void DistanceGate_IsClampedToASaneRange(double configured, double expected)
        {
            Assert.Equal((float)expected, CreatureKnockout.ResolveMaxThrowDistanceMeters(configured), 4);
        }

        [Fact]
        public void ZeroThresholdAcceptsAnyContact_TheOldBehaviourKeptAsAnOption()
        {
            Assert.True(CreatureKnockout.IsHardEnough(0f, 0f));
            Assert.Equal(0f, CreatureKnockout.ResolveMinThrowSpeedMeters(0.0));
        }

        [Theory]
        [InlineData(-5.0, 0.0)]
        [InlineData(double.NaN, 0.0)]
        [InlineData(14.0, 14.0)]
        [InlineData(500.0, CreatureKnockout.MaxMinThrowSpeedMeters)]
        public void ThresholdIsClampedToASaneRange(double configured, double expected)
        {
            Assert.Equal((float)expected, CreatureKnockout.ResolveMinThrowSpeedMeters(configured), 4);
        }

        [Fact]
        public void ThresholdIsSharedSoBothCreaturesAgreeOnWhatAHardThrowIs()
        {
            // One setting, one rule: the same speed must decide both, or a player learns
            // two different throwing strengths for no discoverable reason.
            float threshold = CreatureKnockout.ResolveMinThrowSpeedMeters(14.0);
            Assert.Equal(threshold, CreatureKnockout.ResolveMinThrowSpeedMeters(14.0));
        }
    }
}
