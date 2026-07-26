using Fairoots.Core;
using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// PEAK's world units are not meters - the game multiplies by
    /// <c>CharacterStats.unitsToMeters</c> (1.6) everywhere it shows a player a
    /// distance. Getting this backwards is a 60% error, not a rounding one, so the
    /// conversion has its own tests.
    /// </summary>
    public class WorldUnitsTests
    {
        [Fact]
        public void OneWorldUnit_IsOnePointSixMeters()
        {
            Assert.Equal(1.6f, WorldUnits.UnitsToMeters(1f, WorldUnits.DefaultUnitsToMeters), 4);
            Assert.Equal(0.625f, WorldUnits.MetersToUnits(1f, WorldUnits.DefaultUnitsToMeters), 4);
        }

        [Theory]
        [InlineData(75f)]
        [InlineData(2.8f)]
        [InlineData(0f)]
        public void MetersToUnits_AndBack_RoundTrips(float meters)
        {
            float units = WorldUnits.MetersToUnits(meters, WorldUnits.DefaultUnitsToMeters);
            Assert.Equal(meters, WorldUnits.UnitsToMeters(units, WorldUnits.DefaultUnitsToMeters), 3);
        }

        [Fact]
        public void MetersToUnits_IsAlwaysTheSmallerNumber()
        {
            // Direction sanity: a distance in meters is a *bigger* number than the same
            // distance in world units, so converting a meters setting into units must
            // shrink it. This is the assertion that would have caught the shake bug.
            Assert.True(WorldUnits.MetersToUnits(75f, WorldUnits.DefaultUnitsToMeters) < 75f);
            Assert.True(WorldUnits.UnitsToMeters(75f, WorldUnits.DefaultUnitsToMeters) > 75f);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-1.6f)]
        [InlineData(float.NaN)]
        public void SafeFactor_FallsBackWhenTheLiveValueIsUnusable(float bogus)
        {
            // Reading the static before the game initialises it must not produce a
            // divide-by-zero, a sign flip, or a NaN range silently disabling the cap.
            Assert.Equal(WorldUnits.DefaultUnitsToMeters, WorldUnits.SafeFactor(bogus));
            Assert.Equal(46.875f, WorldUnits.MetersToUnits(75f, bogus), 3);
        }
    }
}
