using Fairoots.Core;
using Fairoots.Core.Presets;
using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// The zombie deaggro rule - the one Phase 7 mechanic that is new logic rather
    /// than a field tweak, and the one dial in the mod where 1.0 is deliberately NOT
    /// vanilla (vanilla is "never deaggro", which no finite multiplier expresses).
    /// These pin down that inverted scale, since it's exactly the sort of thing a
    /// later reader would "fix" back to the usual convention.
    /// </summary>
    public class ZombieDeaggroTests
    {
        [Fact]
        public void MaxMultiplier_UsesTheGamesOwnThirtySecondLostTrackConstant()
        {
            // 30s is Scoutmaster's own sinceSeenTarget threshold - the number PEAK
            // itself considers "they got away from a determined pursuer". If this ever
            // changes it should be a deliberate decision, not a drifting guess.
            Assert.Equal(30f, ZombieDeaggro.ResolveSightLossSeconds(ZombieDeaggro.MaxMultiplier));
        }

        [Fact]
        public void LowerMultiplierIsStrictlyMoreForgiving_OnBothEscapeRoutes()
        {
            Assert.True(ZombieDeaggro.ResolveSightLossSeconds(0.3) < ZombieDeaggro.ResolveSightLossSeconds(0.9));
            Assert.True(ZombieDeaggro.ResolveDistanceWorldUnits(0.3) < ZombieDeaggro.ResolveDistanceWorldUnits(0.9));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-5.0)]
        [InlineData(double.NaN)]
        public void OutOfRangeMultipliersCannotProduceAnInstantlyDeaggroingZombie(double multiplier)
        {
            // Zero is excluded by design: a zero threshold means the zombie drops its
            // target the moment it has one, i.e. a disabled zombie wearing a tuning
            // dial. Clamping on read means a hand-edited config can't produce that.
            Assert.True(ZombieDeaggro.ResolveSightLossSeconds(multiplier) > 0f);
            Assert.True(ZombieDeaggro.ResolveDistanceWorldUnits(multiplier) > 0f);
            Assert.False(ZombieDeaggro.ShouldDeaggroForSightLoss(0f, multiplier));
            Assert.False(ZombieDeaggro.ShouldDeaggroForDistance(0f, multiplier));
        }

        [Fact]
        public void MultipliersAboveOneAreClampedDown_SoOneReallyIsTheCeiling()
        {
            Assert.Equal(
                ZombieDeaggro.ResolveSightLossSeconds(ZombieDeaggro.MaxMultiplier),
                ZombieDeaggro.ResolveSightLossSeconds(50.0));
        }

        [Fact]
        public void AtTheToughestSetting_BrieflyBreakingLineOfSightIsNotEnough()
        {
            // The maintainer's explicit design constraint: a player must not be able to
            // duck behind a zombie for a few seconds and be free of it.
            Assert.False(ZombieDeaggro.ShouldDeaggroForSightLoss(3f, ZombieDeaggro.MaxMultiplier));
            Assert.False(ZombieDeaggro.ShouldDeaggroForSightLoss(29f, ZombieDeaggro.MaxMultiplier));
            Assert.True(ZombieDeaggro.ShouldDeaggroForSightLoss(31f, ZombieDeaggro.MaxMultiplier));
        }

        [Fact]
        public void TheTwoEscapeRoutesAreIndependent()
        {
            // Far away but in plain sight still escapes; unseen for ages while standing
            // next to it also escapes. Neither requires the other.
            const double m = 1.0;
            Assert.True(ZombieDeaggro.ShouldDeaggroForDistance(ZombieDeaggro.BaseDistanceWorldUnits + 1f, m));
            Assert.False(ZombieDeaggro.ShouldDeaggroForSightLoss(0f, m));

            Assert.True(ZombieDeaggro.ShouldDeaggroForSightLoss(ZombieDeaggro.BaseSightLossSeconds + 1f, m));
            Assert.False(ZombieDeaggro.ShouldDeaggroForDistance(0f, m));
        }

        [Fact]
        public void DeaggroDistanceStaysWellClearOfTheZombiesOwnAwarenessRange()
        {
            // distanceBeforeWakeup and distanceBeforeChase are both 30 world units. If
            // the deaggro distance ever dropped near that at the toughest setting, a
            // zombie would give up at roughly the range it starts chasing from, which
            // would read as the mechanic being broken rather than tough.
            Assert.True(ZombieDeaggro.ResolveDistanceWorldUnits(ZombieDeaggro.MaxMultiplier) > 30f * 2f);
        }

        [Fact]
        public void SubtleKeepsVanillaNeverDeaggro_EveryOtherPresetEnablesIt()
        {
            Assert.False(PresetCatalog.ZombieDeaggroEnabled(PresetId.Subtle));
            Assert.True(PresetCatalog.ZombieDeaggroEnabled(PresetId.Balanced));
            Assert.True(PresetCatalog.ZombieDeaggroEnabled(PresetId.Generous));
            Assert.True(PresetCatalog.ZombieDeaggroEnabled(PresetId.Tame));
        }

        [Theory]
        [InlineData(PresetId.Subtle)]
        [InlineData(PresetId.Balanced)]
        [InlineData(PresetId.Generous)]
        [InlineData(PresetId.Tame)]
        [InlineData(PresetId.Custom)]
        public void EveryPresetSitsInsideTheAllowedRange(PresetId preset)
        {
            double zombie = PresetCatalog.ZombieDeaggroMultiplier(preset);
            Assert.InRange(zombie, ZombieDeaggro.MinMultiplier, ZombieDeaggro.MaxMultiplier);
            Assert.Equal(zombie, ZombieDeaggro.ClampMultiplier(zombie));

            // The beetle dial is vanilla-anchored instead, so it only has to be sane.
            double beetle = PresetCatalog.BeetleDeaggroMultiplier(preset);
            Assert.InRange(beetle, BeetleDeaggro.MinMultiplier, BeetleDeaggro.MaxMultiplier);
            Assert.Equal(beetle, BeetleDeaggro.ClampMultiplier(beetle));
        }

        [Fact]
        public void MoreAggressivePresetsMakeBothCreaturesEasierToEscape()
        {
            Assert.True(PresetCatalog.ZombieDeaggroMultiplier(PresetId.Balanced) > PresetCatalog.ZombieDeaggroMultiplier(PresetId.Generous));
            Assert.True(PresetCatalog.ZombieDeaggroMultiplier(PresetId.Generous) > PresetCatalog.ZombieDeaggroMultiplier(PresetId.Tame));
            Assert.True(PresetCatalog.BeetleDeaggroMultiplier(PresetId.Balanced) > PresetCatalog.BeetleDeaggroMultiplier(PresetId.Generous));
            Assert.True(PresetCatalog.BeetleDeaggroMultiplier(PresetId.Generous) > PresetCatalog.BeetleDeaggroMultiplier(PresetId.Tame));
        }

        [Fact]
        public void BeetleDeaggroDistance_OneIsExactlyVanillaUnlikeTheZombieDial()
        {
            Assert.Equal(VanillaAggroDistance, CreatureTuning.ScaleDeaggroDistance(VanillaAggroDistance, 1.0));
            Assert.Equal(2.5f, CreatureTuning.ScaleDeaggroDistance(VanillaAggroDistance, 0.5), 4);
            Assert.Equal(10f, CreatureTuning.ScaleDeaggroDistance(VanillaAggroDistance, 2.0), 4);
        }

        // --- Beetle: the 2026-07-29 live-testing regression ------------------
        // The first version of this dial showed no difference between its extremes.
        // These pin down both causes so neither can quietly come back.

        // Mob's class default. The Roots prefab actually ships 14 (~22.4m), which is why
        // the game-facing code reads the live field per instance rather than assuming; this
        // constant only needs to be *a* baseline for the arithmetic below.
        private const float VanillaAggroDistance = 5f;

        [Fact]
        public void Beetle_ZeroIsNotReachable_MinimumIsATenth()
        {
            // Maintainer's call: 0 reads as "never aggros at all", which makes beetles
            // pointless rather than escapable. Clamped on read, so even a hand-edited
            // config can't get there.
            Assert.Equal(BeetleDeaggro.MinMultiplier, BeetleDeaggro.ClampMultiplier(0.0));
            Assert.Equal(BeetleDeaggro.MinMultiplier, BeetleDeaggro.ClampMultiplier(-3.0));
            Assert.True(BeetleDeaggro.ResolveRetentionDistance(VanillaAggroDistance, 0.0) > 0f);
        }

        [Fact]
        public void Beetle_MultipliersAboveTheCeilingAreClamped()
        {
            Assert.Equal(
                BeetleDeaggro.ResolveRetentionDistance(VanillaAggroDistance, BeetleDeaggro.MaxMultiplier),
                BeetleDeaggro.ResolveRetentionDistance(VanillaAggroDistance, 99.0));
        }

        [Fact]
        public void Beetle_SuppressionWindowIsLongEnoughToOutlastAReCheck()
        {
            // The bug this exists to prevent: vanilla re-runs targeting every ~2s
            // (targetCheckCooldown), so a suppression window shorter than that would
            // let the beetle re-acquire on the very next scan and the dial would cancel
            // itself out - which is exactly what was reported in-game.
            Assert.True(BeetleDeaggro.SuppressionSeconds > 2f);
        }

        [Fact]
        public void Beetle_RetentionIsDistanceOnly_AndOrdersCorrectly()
        {
            // Just inside is kept, just outside is dropped - and a bigger multiplier
            // always keeps a target at least as far as a smaller one does.
            float limit = BeetleDeaggro.ResolveRetentionDistance(VanillaAggroDistance, 2.0);

            Assert.True(BeetleDeaggro.ShouldKeepTarget(limit - 0.1f, VanillaAggroDistance, 2.0));
            Assert.False(BeetleDeaggro.ShouldKeepTarget(limit + 0.1f, VanillaAggroDistance, 2.0));

            Assert.True(BeetleDeaggro.ShouldKeepTarget(9f, VanillaAggroDistance, 2.0));
            Assert.False(BeetleDeaggro.ShouldKeepTarget(9f, VanillaAggroDistance, 0.5));
        }

        [Fact]
        public void Beetle_TheExtremesActuallyDiffer()
        {
            // The regression in one assertion: min and max must not resolve to
            // behaviour a player can't tell apart.
            float atMin = BeetleDeaggro.ResolveRetentionDistance(VanillaAggroDistance, BeetleDeaggro.MinMultiplier);
            float atMax = BeetleDeaggro.ResolveRetentionDistance(VanillaAggroDistance, BeetleDeaggro.MaxMultiplier);

            Assert.True(atMax > atMin * 10f);
        }
    }
}
