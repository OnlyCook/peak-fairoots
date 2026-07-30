using Fairoots.Core;
using Fairoots.Core.Presets;
using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// The two <c>Spores</c>-section dials. Not seed-gated (every player and every
    /// spore source gets the same flat treatment), so these are arithmetic/invariant
    /// proofs rather than determinism ones.
    ///
    /// The load-bearing one is <see cref="ClearTimeMultiplier_ScalesTotalRecoveryTime"/>:
    /// the setting promises "0.5 = half as long", which is only true if the drain rate
    /// is <em>divided</em> while the pre-drain cooldown is <em>multiplied</em>. Both
    /// halves are easy to get backwards and neither is visible in a build log, so the
    /// promise is asserted end-to-end on the combined wall-clock time rather than one
    /// field at a time.
    /// </summary>
    public class SporeStatusTuningTests
    {
        // Stand-ins for the serialized prefab values (they aren't knowable from the
        // decompile - SporeDecayPatch logs the live ones). Nothing here depends on the
        // specific numbers: every assertion is a ratio against the same baseline.
        private const float VanillaPerSecond = 0.05f;
        private const float VanillaCooldown = 3f;
        private const float FullMeter = 1f;

        [Fact]
        public void MultiplierOfOne_IsExactlyVanilla()
        {
            Assert.Equal(VanillaPerSecond, SporeStatusTuning.ScaleDecayRate(VanillaPerSecond, 1.0));
            Assert.Equal(VanillaCooldown, SporeStatusTuning.ScaleDecayCooldown(VanillaCooldown, 1.0));
            Assert.Equal(0.1f, SporeStatusTuning.ScaleBuildUp(0.1f, 1.0));
            Assert.True(SporeStatusTuning.IsVanilla(1.0));
        }

        [Theory]
        [InlineData(0.25)]
        [InlineData(0.5)]
        [InlineData(0.85)]
        [InlineData(2.0)]
        [InlineData(4.0)]
        public void ClearTimeMultiplier_ScalesTotalRecoveryTime(double multiplier)
        {
            double vanilla = SporeStatusTuning.SecondsToClear(VanillaCooldown, VanillaPerSecond, FullMeter, 1.0);
            double scaled = SporeStatusTuning.SecondsToClear(VanillaCooldown, VanillaPerSecond, FullMeter, multiplier);

            // The whole point of the setting: the multiplier is a multiplier on time.
            Assert.Equal(vanilla * multiplier, scaled, 5);
        }

        [Fact]
        public void ClearTimeMultiplier_BelowOne_DrainsFasterAndWaitsLess()
        {
            // Guards the direction of each field independently of the combined-time
            // assertion above, which two compensating sign errors could satisfy.
            Assert.True(SporeStatusTuning.ScaleDecayRate(VanillaPerSecond, 0.5) > VanillaPerSecond);
            Assert.True(SporeStatusTuning.ScaleDecayCooldown(VanillaCooldown, 0.5) < VanillaCooldown);

            Assert.True(SporeStatusTuning.ScaleDecayRate(VanillaPerSecond, 2.0) < VanillaPerSecond);
            Assert.True(SporeStatusTuning.ScaleDecayCooldown(VanillaCooldown, 2.0) > VanillaCooldown);
        }

        [Fact]
        public void ClearTimeMultiplier_WorkedExampleFromTheAsk()
        {
            // The maintainer's own framing: "a value of 1.0 should be 15 seconds
            // (vanilla) and setting it to 0.5 should only take 7.5 seconds". Expressed
            // against a baseline that clears a full meter in exactly 15s.
            const float cooldown = 3f;
            const float perSecond = 1f / 12f; // 12s of draining after a 3s delay.

            Assert.Equal(15.0, SporeStatusTuning.SecondsToClear(cooldown, perSecond, FullMeter, 1.0), 5);
            Assert.Equal(7.5, SporeStatusTuning.SecondsToClear(cooldown, perSecond, FullMeter, 0.5), 5);
        }

        [Fact]
        public void ClearTimeMultiplier_IsClampedRatherThanDividingByZero()
        {
            // A hand-edited config below the bound (or exactly 0) must not produce
            // infinity or a negative rate - it clamps to the fastest supported drain.
            float atFloor = SporeStatusTuning.ScaleDecayRate(VanillaPerSecond, SporeStatusTuning.MinClearTimeMultiplier);
            Assert.Equal(atFloor, SporeStatusTuning.ScaleDecayRate(VanillaPerSecond, 0.0));
            Assert.Equal(atFloor, SporeStatusTuning.ScaleDecayRate(VanillaPerSecond, -5.0));
            Assert.True(atFloor > 0f);
            Assert.True(float.IsFinite(atFloor));

            Assert.Equal(
                SporeStatusTuning.ScaleDecayCooldown(VanillaCooldown, SporeStatusTuning.MinClearTimeMultiplier),
                SporeStatusTuning.ScaleDecayCooldown(VanillaCooldown, -5.0));
        }

        [Fact]
        public void ClearTimeMultiplier_CannotInventRecoveryThatVanillaDoesNotHave()
        {
            // If a build ever shipped sporesReductionPerSecond at 0, "clear faster"
            // must stay inert rather than silently becoming "spores now clear at all".
            Assert.Equal(0f, SporeStatusTuning.ScaleDecayRate(0f, 0.25));
            Assert.Equal(0f, SporeStatusTuning.ScaleDecayCooldown(0f, 0.25));
            Assert.Equal(double.PositiveInfinity, SporeStatusTuning.SecondsToClear(VanillaCooldown, 0f, FullMeter, 0.25));
        }

        [Theory]
        [InlineData(0.5, 10f, 5f)]
        [InlineData(0.5, 0.1f, 0.05f)]
        [InlineData(0.0, 0.1f, 0f)]
        [InlineData(2.0, 0.025f, 0.05f)]
        public void BuildUpMultiplier_ScalesTheDose(double multiplier, float amount, float expected)
        {
            // The literal ask: "if the player gets spores - lets say 10 - but the
            // multiplier is set to 0.5 they should only get half of it - so 5".
            Assert.Equal(expected, SporeStatusTuning.ScaleBuildUp(amount, multiplier), 5);
        }

        [Fact]
        public void BuildUpMultiplier_LeavesNonPositiveAmountsAlone()
        {
            // The native AddStatus is reached with non-positive amounts on several
            // paths. Scaling a subtraction by a "fewer spores" dial would add spores.
            Assert.Equal(0f, SporeStatusTuning.ScaleBuildUp(0f, 0.5));
            Assert.Equal(-0.2f, SporeStatusTuning.ScaleBuildUp(-0.2f, 0.5));
            Assert.Equal(-0.2f, SporeStatusTuning.ScaleBuildUp(-0.2f, 2.0));
        }

        [Fact]
        public void BuildUpMultiplier_NeverGoesNegative()
        {
            // An out-of-range hand-edited config must not flip the native code into
            // its SubtractStatus branch and turn spore clouds into a cure.
            Assert.Equal(0f, SporeStatusTuning.ScaleBuildUp(0.1f, -3.0));
        }

        [Fact]
        public void EveryPresetProducesAUsableClearTime()
        {
            foreach (PresetId preset in new[] { PresetId.Subtle, PresetId.Balanced, PresetId.Generous, PresetId.Tame, PresetId.Custom })
            {
                double multiplier = PresetCatalog.SporeClearTimeMultiplier(preset);
                Assert.InRange(multiplier, SporeStatusTuning.MinClearTimeMultiplier, 5.0);
                Assert.True(SporeStatusTuning.ScaleDecayRate(VanillaPerSecond, multiplier) > 0f);
            }

            // Subtle is the "barely changed" preset, so recovery there must be vanilla.
            Assert.True(SporeStatusTuning.IsVanilla(PresetCatalog.SporeClearTimeMultiplier(PresetId.Subtle)));
        }

        [Fact]
        public void NoPresetTouchesTheGlobalBuildUpDial()
        {
            // Deliberate, not an unfilled row: the presets already reduce build-up per
            // hazard, and this dial multiplies on top of all of them at once, so preset
            // values here would compound with rows that mean the same thing. See
            // PresetCatalog.SporeBuildUpMultiplier's remarks before "finishing" it.
            foreach (PresetId preset in new[] { PresetId.Subtle, PresetId.Balanced, PresetId.Generous, PresetId.Tame, PresetId.Custom })
            {
                Assert.True(SporeStatusTuning.IsVanilla(PresetCatalog.SporeBuildUpMultiplier(preset)));
            }
        }

        [Fact]
        public void BuildUpAndAreaRateDials_Compound()
        {
            // Documents the intended interaction rather than guarding an invariant:
            // the area dial scales the emitter, this one scales what the emitter's
            // application turns into, so a player who sets both to 0.5 gets a quarter.
            float emitterAmount = SporeAreaTuning.ScaleStatusRate(0.025f, 0.5);
            float landed = SporeStatusTuning.ScaleBuildUp(emitterAmount, 0.5);

            Assert.Equal(0.025f * 0.25f, landed, 6);
        }
    }
}
