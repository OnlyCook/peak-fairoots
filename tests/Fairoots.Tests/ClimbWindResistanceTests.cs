using Fairoots.Core;
using Fairoots.Core.Presets;
using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// Covers the climb-to-shelter-from-wind cost model
    /// (<see cref="ClimbWindResistance"/>). The mechanic's headline promise is
    /// "you pay only when the wind was actually going to hit you," so the
    /// zero-pressure cases matter as much as the slowdown ones.
    /// </summary>
    public class ClimbWindResistanceTests
    {
        private const double Base = 0.90;
        private const double Upward = 0.85;
        private const double IntoWind = 0.85;

        private static ClimbMove Resist(float lateral, float up, float windLateral, float windUp, float pressure) =>
            ClimbWindResistance.Resist(
                new ClimbMove(lateral, up), windLateral, windUp, pressure, Base, Upward, IntoWind);

        [Fact]
        public void NoPressure_LeavesMovementExactlyUntouched()
        {
            var move = Resist(0.3f, 0.7f, windLateral: -1f, windUp: 0f, pressure: 0f);

            Assert.Equal(0.3f, move.Lateral);
            Assert.Equal(0.7f, move.Up);
        }

        [Fact]
        public void ShelteredClimber_PaysNothing_EvenWithHarshMultipliers()
        {
            // A climber behind a rock resolves to pressure 0: the mechanic must be
            // free for them no matter how punishing the configured cost is.
            var move = ClimbWindResistance.Resist(
                new ClimbMove(1f, 1f), windLateral: -1f, windUp: 0f, pressure: 0f,
                baseMultiplier: 0.05, upwardMultiplier: 0.05, intoWindMultiplier: 0.05);

            Assert.Equal(1f, move.Lateral);
            Assert.Equal(1f, move.Up);
        }

        [Fact]
        public void AllMultipliersOne_LeavesMovementUntouched_EvenAtFullPressure()
        {
            var move = ClimbWindResistance.Resist(
                new ClimbMove(0.4f, 0.9f), windLateral: -1f, windUp: 0f, pressure: 1f,
                baseMultiplier: 1.0, upwardMultiplier: 1.0, intoWindMultiplier: 1.0);

            Assert.Equal(0.4f, move.Lateral, 5);
            Assert.Equal(0.9f, move.Up, 5);
        }

        [Fact]
        public void FullPressure_CrosswindUpwardClimb_PaysBaseTimesUpward()
        {
            // Wind blows straight across the wall, so no component of an upward
            // climb opposes it - only base and upward apply.
            var move = Resist(0f, 1f, windLateral: 1f, windUp: 0f, pressure: 1f);

            Assert.Equal((float)(Base * Upward), move.Up, 5);
        }

        [Fact]
        public void FullPressure_DownwardClimb_PaysBaseOnly()
        {
            // Sliding/descending is never charged the upward penalty - the wind is
            // a reason to go down, and slowing a descent would read as broken physics.
            var move = Resist(0f, -1f, windLateral: 1f, windUp: 0f, pressure: 1f);

            Assert.Equal((float)(-1f * Base), move.Up, 5);
        }

        [Fact]
        public void UpwardIsSlowerThanDownward_AtEqualPressure()
        {
            var up = Resist(0f, 1f, windLateral: 1f, windUp: 0f, pressure: 1f).Up;
            var down = Resist(0f, -1f, windLateral: 1f, windUp: 0f, pressure: 1f).Up;

            Assert.True(up < -down, $"upward {up} should be slower than downward {-down}");
        }

        [Fact]
        public void MovingIntoTheWind_IsSlowerThanMovingWithIt()
        {
            // Wind blows toward +lateral; moving -lateral is moving into it.
            float into = Resist(-1f, 0f, windLateral: 1f, windUp: 0f, pressure: 1f).Lateral;
            float with = Resist(1f, 0f, windLateral: 1f, windUp: 0f, pressure: 1f).Lateral;

            Assert.Equal((float)(Base * IntoWind), -into, 5);
            Assert.Equal((float)Base, with, 5);
            Assert.True(-into < with);
        }

        [Fact]
        public void MovingWithTheWind_IsNeverFasterThanVanilla()
        {
            float with = Resist(1f, 0f, windLateral: 1f, windUp: 0f, pressure: 1f).Lateral;

            Assert.True(with <= 1f, "the mechanic is a cost, not a sail");
        }

        [Fact]
        public void IntoWindPenalty_ScalesWithHowMuchWindLiesOnThatAxis()
        {
            // Wind mostly across the wall (0.2 along lateral) costs less to push
            // into than wind squarely along it (1.0).
            float glancing = -Resist(-1f, 0f, windLateral: 0.2f, windUp: 0f, pressure: 1f).Lateral;
            float head_on = -Resist(-1f, 0f, windLateral: 1.0f, windUp: 0f, pressure: 1f).Lateral;

            Assert.True(head_on < glancing, $"head-on {head_on} should be slower than glancing {glancing}");
            Assert.True(glancing < (float)Base + 1e-5f);
        }

        [Fact]
        public void PartialPressure_FadesTheSlowdownIn_Proportionally()
        {
            float half = Resist(1f, 0f, windLateral: 0f, windUp: 0f, pressure: 0.5f).Lateral;

            Assert.Equal((float)(1.0 + (Base - 1.0) * 0.5), half, 5);
        }

        [Fact]
        public void HigherPressure_IsAlwaysAtLeastAsSlow()
        {
            float previous = float.MaxValue;
            for (float p = 0f; p <= 1.0001f; p += 0.1f)
            {
                float speed = Resist(0f, 1f, windLateral: -1f, windUp: 0f, pressure: p).Up;
                Assert.True(speed <= previous + 1e-5f, $"pressure {p} was faster than the step before it");
                previous = speed;
            }
        }

        [Theory]
        [InlineData(-0.5f, 0f)]
        [InlineData(0f, 0f)]
        [InlineData(0.5f, 0.5f)]
        [InlineData(1f, 1f)]
        [InlineData(4f, 1f)]
        [InlineData(float.NaN, 0f)]
        public void ClampPressure_KeepsPressureInRange(float raw, float expected)
        {
            Assert.Equal(expected, ClimbWindResistance.ClampPressure(raw));
        }

        [Fact]
        public void OutOfRangePressure_IsClampedRatherThanExtrapolated()
        {
            // A pressure above 1 must not overshoot into "slower than the
            // configured floor" (or, with a >1 multiplier, into a speed boost).
            float clamped = Resist(0f, 1f, windLateral: 0f, windUp: 0f, pressure: 5f).Up;
            float atOne = Resist(0f, 1f, windLateral: 0f, windUp: 0f, pressure: 1f).Up;

            Assert.Equal(atOne, clamped, 5);
        }

        [Fact]
        public void NegativeMultiplier_NeverReversesMovementDirection()
        {
            var move = ClimbWindResistance.Resist(
                new ClimbMove(1f, 1f), windLateral: 0f, windUp: 0f, pressure: 1f,
                baseMultiplier: -2.0, upwardMultiplier: 1.0, intoWindMultiplier: 1.0);

            Assert.Equal(0f, move.Lateral);
            Assert.Equal(0f, move.Up);
        }

        [Fact]
        public void PressureIsCurrent_OnlyWithinTheFreshnessWindow()
        {
            const float now = 100f;

            Assert.False(ClimbWindResistance.IsPressureCurrent(-1f, now)); // never recorded
            Assert.True(ClimbWindResistance.IsPressureCurrent(now, now));
            Assert.True(ClimbWindResistance.IsPressureCurrent(now - ClimbWindResistance.PressureFreshnessSeconds, now));
            Assert.False(ClimbWindResistance.IsPressureCurrent(now - ClimbWindResistance.PressureFreshnessSeconds - 0.01f, now));
        }

        [Fact]
        public void PressureRecordedAfterAClockReset_CountsAsCurrent()
        {
            // A level reload can move Time.time backwards under a stored reading;
            // treating that as current (it expires a moment later anyway) is safer
            // than treating a live gust as "no wind."
            Assert.True(ClimbWindResistance.IsPressureCurrent(50f, 10f));
        }

        // --- Let-go grace window ------------------------------------------
        private const float Grace = 0.5f;
        private const double GraceForce = 0.15;

        private static float GraceAt(float elapsed) =>
            ClimbWindResistance.GraceForceMultiplier(
                lastHeldOnTime: 0f, currentTime: elapsed, graceSeconds: Grace, reducedMultiplier: GraceForce);

        [Fact]
        public void NeverHeldOn_GetsNoGrace()
        {
            Assert.Equal(1f, ClimbWindResistance.GraceForceMultiplier(-1f, 10f, Grace, GraceForce));
        }

        [Fact]
        public void RightAfterLettingGo_WindIsNearlyGone()
        {
            // The catapult moment: full vanilla force landing on a character the
            // game has just declared airborne.
            Assert.Equal((float)GraceForce, GraceAt(0f));
            Assert.Equal((float)GraceForce, GraceAt(0.01f));
        }

        [Fact]
        public void GraceIsNeverFullImmunity_SoWallTappingCantBeAbused()
        {
            Assert.True(GraceAt(0f) > 0f, "a player could otherwise wall-tap across an exposed stretch wind-free");
        }

        [Fact]
        public void AfterTheWindow_WindIsBackToFullStrength()
        {
            Assert.Equal(1f, GraceAt(Grace));
            Assert.Equal(1f, GraceAt(Grace + 1f));
        }

        [Fact]
        public void GraceRampsBackUpInsteadOfEndingInACliff()
        {
            // A snap from near-immune to full force would itself read as an
            // unexplained shove - the exact thing the window exists to prevent.
            float holdUntil = Grace * (1f - ClimbWindResistance.GraceRampFraction);

            Assert.Equal((float)GraceForce, GraceAt(holdUntil));
            float midRamp = GraceAt((holdUntil + Grace) * 0.5f);
            Assert.True(midRamp > (float)GraceForce && midRamp < 1f, $"mid-ramp was {midRamp}");
        }

        [Fact]
        public void GraceIsMonotonic_AcrossTheWholeWindow()
        {
            float previous = 0f;
            for (float t = 0f; t <= Grace + 0.2f; t += 0.02f)
            {
                float m = GraceAt(t);
                Assert.True(m >= previous - 1e-5f, $"wind got weaker again at t={t}");
                previous = m;
            }
        }

        [Fact]
        public void ZeroGraceSeconds_DisablesTheWindowEntirely()
        {
            Assert.Equal(1f, ClimbWindResistance.GraceForceMultiplier(0f, 0f, 0f, GraceForce));
            Assert.Equal(1f, ClimbWindResistance.GraceForceMultiplier(0f, 0f, -1f, GraceForce));
        }

        [Fact]
        public void GraceMultiplierAboveOne_NeverAmplifiesTheWind()
        {
            Assert.Equal(1f, ClimbWindResistance.GraceForceMultiplier(0f, 0f, Grace, 4.0));
        }

        [Fact]
        public void NegativeGraceMultiplier_ClampsToNoForceRatherThanReversingIt()
        {
            Assert.Equal(0f, ClimbWindResistance.GraceForceMultiplier(0f, 0f, Grace, -2.0));
        }

        [Fact]
        public void GraceAfterAClockReset_TreatsTheReadingAsJustNow()
        {
            Assert.Equal((float)GraceForce, ClimbWindResistance.GraceForceMultiplier(50f, 10f, Grace, GraceForce));
        }

        [Theory]
        [InlineData(PresetId.Balanced)]
        [InlineData(PresetId.Generous)]
        [InlineData(PresetId.Tame)]
        [InlineData(PresetId.Custom)]
        public void EveryPresetWithTheMechanic_HasAWeakButNonZeroGraceWindow(PresetId preset)
        {
            double m = PresetCatalog.ClimbWindGraceForceMultiplier(preset);

            Assert.InRange(m, 0.01, 0.5);
        }

        [Fact]
        public void SubtlePresetsGraceMultiplierIsMoot()
        {
            Assert.Equal(1.00, PresetCatalog.ClimbWindGraceForceMultiplier(PresetId.Subtle));
        }

        [Fact]
        public void SubtlePresetTurnsTheMechanicOffEntirely()
        {
            // Subtle leaves vanilla mechanics alone, and outright wind immunity
            // is the least subtle thing in the mod (maintainer, 2026-07-27).
            Assert.False(PresetCatalog.ClimbToCounterWind(PresetId.Subtle));

            // Its multipliers are moot, and must read as "no slowdown" rather
            // than implying a Subtle-strength one exists.
            Assert.Equal(1.0, PresetCatalog.ClimbWindSpeedMultiplier(PresetId.Subtle));
            Assert.Equal(1.0, PresetCatalog.ClimbWindUpwardSpeedMultiplier(PresetId.Subtle));
            Assert.Equal(1.0, PresetCatalog.ClimbWindIntoWindSpeedMultiplier(PresetId.Subtle));
        }

        [Theory]
        [InlineData(PresetId.Balanced)]
        [InlineData(PresetId.Generous)]
        [InlineData(PresetId.Tame)]
        [InlineData(PresetId.Custom)]
        public void EveryPresetThatHasTheMechanic_SlowsClimbingWithoutStoppingIt(PresetId preset)
        {
            double baseMult = PresetCatalog.ClimbWindSpeedMultiplier(preset);
            double upward = PresetCatalog.ClimbWindUpwardSpeedMultiplier(preset);
            double intoWind = PresetCatalog.ClimbWindIntoWindSpeedMultiplier(preset);

            Assert.True(PresetCatalog.ClimbToCounterWind(preset), $"{preset} should have the mechanic on");
            foreach (double m in new[] { baseMult, upward, intoWind })
            {
                Assert.InRange(m, 0.05, 1.0);
            }
            Assert.True(baseMult < 1.0, $"{preset} should cost something for the shelter");

            // The worst case (climbing up, into the wind, at full pressure) still
            // has to leave the player able to move - a climber frozen in place
            // would just be the vanilla "you lose" outcome by another route.
            float worst = ClimbWindResistance.Resist(
                new ClimbMove(0f, 1f), windLateral: 0f, windUp: -1f, pressure: 1f,
                baseMult, upward, intoWind).Up;
            Assert.True(worst > 0.05f, $"{preset} leaves only {worst:0.###} of climb speed");
        }

        [Fact]
        public void TamePresetIsMoreForgivingThanBalanced()
        {
            // Same direction as every other row in the preset table: the more
            // forgiving the preset, the less the shelter costs. Subtle is
            // excluded - it doesn't have the mechanic at all.
            Assert.True(PresetCatalog.ClimbWindSpeedMultiplier(PresetId.Tame)
                > PresetCatalog.ClimbWindSpeedMultiplier(PresetId.Generous));
            Assert.True(PresetCatalog.ClimbWindSpeedMultiplier(PresetId.Generous)
                > PresetCatalog.ClimbWindSpeedMultiplier(PresetId.Balanced));
            Assert.True(PresetCatalog.ClimbWindUpwardSpeedMultiplier(PresetId.Tame)
                > PresetCatalog.ClimbWindUpwardSpeedMultiplier(PresetId.Balanced));
            Assert.True(PresetCatalog.ClimbWindIntoWindSpeedMultiplier(PresetId.Tame)
                > PresetCatalog.ClimbWindIntoWindSpeedMultiplier(PresetId.Balanced));
        }

        [Fact]
        public void CustomPresetFollowsBalanced_ForTheMechanicsOnOffRow()
        {
            // Custom has no catalog row of its own (it maps to Balanced), so
            // picking Custom must not silently inherit Subtle's "off".
            Assert.True(PresetCatalog.ClimbToCounterWind(PresetId.Custom));
            Assert.Equal(
                PresetCatalog.ClimbWindSpeedMultiplier(PresetId.Balanced),
                PresetCatalog.ClimbWindSpeedMultiplier(PresetId.Custom));
        }
    }
}
