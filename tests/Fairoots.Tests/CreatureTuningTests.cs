using Fairoots.Core;
using Fairoots.Core.Presets;
using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// Creature speed scaling. Not seed-gated (every zombie and beetle gets the same
    /// flat treatment), so these are arithmetic/invariant proofs rather than
    /// determinism ones. The load-bearing ones: 1.0 is an *exact* restore of the
    /// authored value (the whole baseline-caching design depends on it), and repeated
    /// application from a cached baseline can't compound the way applying to the live
    /// field would.
    /// </summary>
    public class CreatureTuningTests
    {
        // Decompile-confirmed vanilla values.
        private const float VanillaBeetleSpeed = 5f;      // Mob.movementSpeed
        private const float VanillaZombieForce = 10f;     // CharacterMovement.movementForce
        private const float VanillaBeetleBonkForce = 100f; // Beetle.bonkForce / bonkForceUp

        [Fact]
        public void MultiplierOfOne_IsExactlyVanilla()
        {
            Assert.Equal(VanillaBeetleSpeed, CreatureTuning.ScaleMovementSpeed(VanillaBeetleSpeed, 1.0));
            Assert.Equal(VanillaZombieForce, CreatureTuning.ScaleMovementSpeed(VanillaZombieForce, 1.0));
            Assert.True(CreatureTuning.IsVanilla(1.0));
        }

        [Theory]
        [InlineData(0.65, 3.25f)]
        [InlineData(0.8, 4f)]
        [InlineData(0.9, 4.5f)]
        [InlineData(1.5, 7.5f)]
        public void ScalesProportionally(double multiplier, float expected)
        {
            Assert.Equal(expected, CreatureTuning.ScaleMovementSpeed(VanillaBeetleSpeed, multiplier), 4);
        }

        [Fact]
        public void ZeroStopsTheCreatureDead()
        {
            Assert.Equal(0f, CreatureTuning.ScaleMovementSpeed(VanillaBeetleSpeed, 0.0));
        }

        [Fact]
        public void NegativeMultiplierClampsToZero_NeverReversesTheCreature()
        {
            // A negative "speed" would send a beetle backwards rather than stop it,
            // which is not what anyone typing -1 into a speed setting means.
            Assert.Equal(0f, CreatureTuning.ScaleMovementSpeed(VanillaBeetleSpeed, -1.0));
            Assert.Equal(0f, CreatureTuning.ScaleMovementSpeed(VanillaZombieForce, -0.001));
        }

        [Fact]
        public void RepeatedApplicationFromBaselineDoesNotCompound()
        {
            // The reason every game-facing caller must keep a vanilla baseline: applying
            // the same multiplier twice from the baseline is idempotent, whereas applying
            // it to the live field would square it.
            float once = CreatureTuning.ScaleMovementSpeed(VanillaBeetleSpeed, 0.8);
            float twice = CreatureTuning.ScaleMovementSpeed(VanillaBeetleSpeed, 0.8);
            Assert.Equal(once, twice);
            Assert.NotEqual(once, CreatureTuning.ScaleMovementSpeed(once, 0.8));
        }

        [Fact]
        public void IsVanilla_ToleratesFloatNoiseButNotRealChanges()
        {
            Assert.True(CreatureTuning.IsVanilla(1.0 + 1e-9));
            Assert.False(CreatureTuning.IsVanilla(0.99));
            Assert.False(CreatureTuning.IsVanilla(1.01));
        }

        [Fact]
        public void Knockback_MultiplierOfOne_IsExactlyVanilla()
        {
            Assert.Equal(VanillaBeetleBonkForce, CreatureTuning.ScaleKnockback(VanillaBeetleBonkForce, 1.0));
        }

        [Fact]
        public void Knockback_ScalesBothComponentsIdentically_SoTheShoveKeepsItsAngle()
        {
            // bonkForce and bonkForceUp are the horizontal and vertical halves of one
            // shove. Scaling them by the same factor must preserve their ratio, or the
            // knockback dial quietly becomes a "beetles launch you upwards" dial.
            const float forward = 100f;
            const float up = 100f;
            const double multiplier = 0.35;

            float scaledForward = CreatureTuning.ScaleKnockback(forward, multiplier);
            float scaledUp = CreatureTuning.ScaleKnockback(up, multiplier);

            Assert.Equal(forward / up, scaledForward / scaledUp, 5);
        }

        [Fact]
        public void Knockback_AsymmetricBaselineKeepsItsOwnAsymmetry()
        {
            // A beetle authored off the class default must keep its own ratio, which is
            // why both components are cached rather than one plus a shared ratio.
            float scaledForward = CreatureTuning.ScaleKnockback(120f, 0.5);
            float scaledUp = CreatureTuning.ScaleKnockback(80f, 0.5);

            Assert.Equal(60f, scaledForward, 4);
            Assert.Equal(40f, scaledUp, 4);
            Assert.Equal(120f / 80f, scaledForward / scaledUp, 5);
        }

        [Fact]
        public void Knockback_ZeroAndNegativeBothMeanNoShove_NeverAPull()
        {
            Assert.Equal(0f, CreatureTuning.ScaleKnockback(VanillaBeetleBonkForce, 0.0));
            Assert.Equal(0f, CreatureTuning.ScaleKnockback(VanillaBeetleBonkForce, -2.0));
        }

        [Theory]
        [InlineData(PresetId.Subtle)]
        [InlineData(PresetId.Balanced)]
        [InlineData(PresetId.Generous)]
        [InlineData(PresetId.Tame)]
        [InlineData(PresetId.Custom)]
        public void EveryPresetYieldsASaneSpeedMultiplier(PresetId preset)
        {
            double zombie = PresetCatalog.ZombieSpeedMultiplier(preset);
            double beetle = PresetCatalog.BeetleSpeedMultiplier(preset);
            double knockback = PresetCatalog.BeetleKnockbackMultiplier(preset);
            Assert.InRange(knockback, 0.0, 1.0);

            // ROADMAP.md's row only ever slows creatures down; a preset that sped one
            // up would be a typo, not a balance choice.
            Assert.InRange(zombie, 0.0, 1.0);
            Assert.InRange(beetle, 0.0, 1.0);
        }

        [Fact]
        public void SubtleIsVanillaSpeed_MoreAggressivePresetsAreStrictlySlower()
        {
            Assert.Equal(1.0, PresetCatalog.ZombieSpeedMultiplier(PresetId.Subtle));
            Assert.Equal(1.0, PresetCatalog.BeetleSpeedMultiplier(PresetId.Subtle));
            Assert.Equal(1.0, PresetCatalog.BeetleKnockbackMultiplier(PresetId.Subtle));

            Assert.True(PresetCatalog.ZombieSpeedMultiplier(PresetId.Balanced) > PresetCatalog.ZombieSpeedMultiplier(PresetId.Generous));
            Assert.True(PresetCatalog.ZombieSpeedMultiplier(PresetId.Generous) > PresetCatalog.ZombieSpeedMultiplier(PresetId.Tame));
            Assert.True(PresetCatalog.BeetleSpeedMultiplier(PresetId.Balanced) > PresetCatalog.BeetleSpeedMultiplier(PresetId.Generous));
            Assert.True(PresetCatalog.BeetleSpeedMultiplier(PresetId.Generous) > PresetCatalog.BeetleSpeedMultiplier(PresetId.Tame));
        }
    }
}
