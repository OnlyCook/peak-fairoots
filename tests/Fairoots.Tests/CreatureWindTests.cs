using Fairoots.Core;
using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// Wind on creatures. The load-bearing thing here is that the two dials have
    /// *different* vanilla points - 1.0 for zombies, 0 for beetles - because zombies
    /// already take wind and beetles cannot be made to by scaling anything. That
    /// asymmetry is exactly what a later reader would "tidy up", so it's pinned down.
    /// </summary>
    public class CreatureWindTests
    {
        private const float VanillaBeetleSpeed = 5f; // Mob.movementSpeed

        [Fact]
        public void ZombieDial_OneMeansVanilla()
        {
            Assert.True(CreatureWind.IsVanillaZombieWind(1.0));
            Assert.False(CreatureWind.IsVanillaZombieWind(1.5));
            Assert.False(CreatureWind.IsVanillaZombieWind(0.0));
        }

        [Fact]
        public void ZombieDial_ZeroMakesThemImmune_AndIsNotVanilla()
        {
            // Vanilla for a zombie is 0.6x a player's force, not zero - so 0 is a real
            // change, and must not be mistaken for "leave it alone".
            Assert.Equal(0.0, CreatureWind.ResolveZombieMultiplier(0.0));
            Assert.False(CreatureWind.IsVanillaZombieWind(0.0));
            Assert.True(CreatureWind.VanillaBotWindShare > 0f);
        }

        [Theory]
        [InlineData(-1.0, 0.0)]
        [InlineData(double.NaN, 1.0)]
        [InlineData(2.0, 2.0)]
        [InlineData(99.0, CreatureWind.MaxMultiplier)]
        public void ZombieDial_IsClampedAndFailsSafeToVanilla(double configured, double expected)
        {
            // NaN resolving to 1.0 rather than 0 matters: a malformed value should leave
            // the game as it was, not silently make zombies wind-immune.
            Assert.Equal(expected, CreatureWind.ResolveZombieMultiplier(configured), 6);
        }

        [Fact]
        public void BeetleDial_ZeroIsVanillaAndProducesNoDrift()
        {
            // Beetles are wind-immune in vanilla and can't be scaled into susceptibility,
            // so 0 - not 1.0 - is the "leave the game alone" value for this dial.
            Assert.Equal(0.0, CreatureWind.ResolveBeetleSusceptibility(0.0));
            Assert.Equal(0f, CreatureWind.BeetleDriftSpeed(VanillaBeetleSpeed, 0.0));
        }

        [Fact]
        public void BeetleDial_OneMeansAboutItsOwnWalkingSpeed()
        {
            // The unit the setting promises: susceptibility 1.0 shoves a beetle about as
            // fast as it walks. Tied to movementSpeed rather than the zone's windForce,
            // which is an acceleration (15-20) and would fling beetles across the map.
            Assert.Equal(VanillaBeetleSpeed, CreatureWind.BeetleDriftSpeed(VanillaBeetleSpeed, 1.0), 4);
            Assert.Equal(VanillaBeetleSpeed * 0.5f, CreatureWind.BeetleDriftSpeed(VanillaBeetleSpeed, 0.5), 4);
        }

        [Fact]
        public void BeetleDial_ScalesWithTheBeetlesOwnSpeed_NotAnAbsoluteNumber()
        {
            // A differently-scaled beetle must drift proportionally, which is the whole
            // reason the drift is derived from movementSpeed.
            float slow = CreatureWind.BeetleDriftSpeed(2f, 1.0);
            float fast = CreatureWind.BeetleDriftSpeed(10f, 1.0);
            Assert.Equal(5f, fast / slow, 4);
        }

        [Theory]
        [InlineData(-2.0)]
        [InlineData(double.NaN)]
        public void BeetleDial_NonsenseValuesMeanImmune(double configured)
        {
            Assert.Equal(0f, CreatureWind.BeetleDriftSpeed(VanillaBeetleSpeed, configured));
        }

        [Fact]
        public void BeetleDial_IsCappedSoWindStaysWeatherRatherThanATeleport()
        {
            Assert.Equal(
                CreatureWind.BeetleDriftSpeed(VanillaBeetleSpeed, CreatureWind.MaxMultiplier),
                CreatureWind.BeetleDriftSpeed(VanillaBeetleSpeed, 500.0));
        }

        [Fact]
        public void NegativeMovementSpeedCannotProduceReverseDrift()
        {
            Assert.Equal(0f, CreatureWind.BeetleDriftSpeed(-5f, 1.0));
        }
    }
}
