using System;
using Fairoots.Core;
using Fairoots.Core.Presets;
using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// Spore-area size scaling. Not seed-gated (every area gets the same flat
    /// treatment), so these are arithmetic/invariant proofs rather than
    /// determinism ones - the load-bearing one being that the falloff *shape* is
    /// preserved, so the radius dial can't quietly double as a lethality dial.
    /// </summary>
    public class SporeAreaTuningTests
    {
        // Roots' runtime-confirmed vanilla values.
        private const float VanillaRadius = 16f;
        private const float VanillaInnerFade = 8f;

        [Fact]
        public void MultiplierOfOne_IsExactlyVanilla()
        {
            Assert.Equal(VanillaRadius, SporeAreaTuning.ScaleRadius(VanillaRadius, 1.0));
            Assert.Equal(VanillaInnerFade, SporeAreaTuning.ScaleFade(VanillaInnerFade, 1.0));
            Assert.Equal(1f, SporeAreaTuning.ScaleVisual(1.0));
        }

        [Theory]
        [InlineData(0.55)]
        [InlineData(0.7)]
        [InlineData(0.85)]
        [InlineData(1.5)]
        public void FalloffShapeIsPreserved_InnerFadeStaysTheSameFractionOfRadius(double multiplier)
        {
            float radius = SporeAreaTuning.ScaleRadius(VanillaRadius, multiplier);
            float innerFade = SporeAreaTuning.ScaleFade(VanillaInnerFade, multiplier);

            // Vanilla: the ramp is exactly the outer half of the area. That must
            // hold at every size, or a shrunken area becomes all ramp (weaker
            // everywhere) and an enlarged one all full-strength core.
            Assert.Equal(VanillaInnerFade / VanillaRadius, innerFade / radius, 5);
        }

        [Fact]
        public void ScalingIsProportional()
        {
            Assert.Equal(8f, SporeAreaTuning.ScaleRadius(VanillaRadius, 0.5));
            Assert.Equal(24f, SporeAreaTuning.ScaleRadius(VanillaRadius, 1.5));
            Assert.Equal(0f, SporeAreaTuning.ScaleRadius(VanillaRadius, 0.0));
        }

        [Fact]
        public void NegativeMultipliersClampToZero_NeverToANegativeRadius()
        {
            // A negative radius would make the native InRange check nonsensical
            // rather than merely inert.
            Assert.Equal(0f, SporeAreaTuning.ScaleRadius(VanillaRadius, -0.5));
            Assert.Equal(0f, SporeAreaTuning.ScaleFade(VanillaInnerFade, -0.5));
        }

        [Fact]
        public void VisualScaleHasAPositiveFloor_NeverADegenerateTransform()
        {
            Assert.Equal(SporeAreaTuning.MinVisualScale, SporeAreaTuning.ScaleVisual(0.0));
            Assert.Equal(SporeAreaTuning.MinVisualScale, SporeAreaTuning.ScaleVisual(-2.0));
            Assert.True(SporeAreaTuning.MinVisualScale > 0f);
        }

        [Fact]
        public void VisualScaleMatchesTheRadiusScale_SoWhatYouSeeIsWhatApplies()
        {
            // The maintainer's explicit requirement: the visible cloud and the
            // actual hazard extent must move together.
            foreach (double m in new[] { 0.55, 0.85, 1.0, 2.0 })
            {
                float radiusRatio = SporeAreaTuning.ScaleRadius(VanillaRadius, m) / VanillaRadius;
                Assert.Equal(radiusRatio, SporeAreaTuning.ScaleVisual(m), 5);
            }
        }

        [Fact]
        public void StatusRateScalesProportionally_AndOneIsVanilla()
        {
            const float vanillaAmount = 0.025f; // runtime-confirmed Roots value
            Assert.Equal(vanillaAmount, SporeAreaTuning.ScaleStatusRate(vanillaAmount, 1.0));
            Assert.Equal(vanillaAmount * 0.5f, SporeAreaTuning.ScaleStatusRate(vanillaAmount, 0.5), 6);
            Assert.Equal(vanillaAmount * 2f, SporeAreaTuning.ScaleStatusRate(vanillaAmount, 2.0), 6);
        }

        [Fact]
        public void StatusRateNeverGoesNegative_SoAHazardCannotBecomeACure()
        {
            // A negative amount flips the native emitter into its SubtractStatus
            // branch - i.e. the spore cloud would start *healing* spores. Zero is
            // the intended floor ("never applies spores"). This clamp is also what
            // keeps SporeAreaScan.IsSporeArea's sign-based identity check valid on
            // an area Fairoots has zeroed.
            Assert.Equal(0f, SporeAreaTuning.ScaleStatusRate(0.025f, -1.0));
            Assert.Equal(0f, SporeAreaTuning.ScaleStatusRate(0.025f, 0.0));
        }

        [Fact]
        public void StatusRateAndRadiusAreIndependentDials()
        {
            // Scaling the rate must not touch the geometry, and vice versa - the two
            // settings exist precisely so "how big" and "how fast" can be tuned
            // separately (and the fade scaling exists so radius doesn't leak into
            // rate - see FalloffShapeIsPreserved above).
            Assert.Equal(VanillaRadius, SporeAreaTuning.ScaleRadius(VanillaRadius, 1.0));
            Assert.Equal(0.0125f, SporeAreaTuning.ScaleStatusRate(0.025f, 0.5), 6);
        }

        [Fact]
        public void StatusRatePresetProgression_EachPresetIsAtLeastAsGentle()
        {
            AssertGetsGentler("status-rate-multiplier", PresetCatalog.SporeAreaStatusRateMultiplier);

            // A rate of 0 is not "gentle", it's a spore area that does nothing - and
            // SporeAreaScan.IsSporeArea identifies an area by its positive amount, so
            // a zeroed one would stop being findable at all.
            Assert.True(PresetCatalog.SporeAreaStatusRateMultiplier(PresetId.Tame) > 0.0);
        }

        [Fact]
        public void RadiusPresetProgression_EachPresetShrinksAtLeastAsFar()
        {
            AssertGetsGentler("radius-multiplier", PresetCatalog.SporeAreaRadiusMultiplier);
        }

        /// <summary>
        /// The shape both spore-area dials have to keep, and nothing more: never above
        /// vanilla, never rebounding as the presets get more forgiving, and Tame
        /// strictly below Subtle so the row actually does something.
        ///
        /// <b>Ties between neighbours are allowed on purpose</b>, and both dials
        /// currently use them (Subtle and Balanced sit at 1.00 on each as of
        /// 2026-07-30) - "the two lightest presets leave spore areas alone" is a
        /// legitimate tuning outcome, and the strict less-than chain this replaced
        /// made it a build failure. Same reasoning, and the same rule, as
        /// <c>PresetResolutionTests.AssertRunsFromVanilla</c>: values are re-tuned
        /// between play sessions via <c>docs/PRESETS.md</c>, so tests pin direction,
        /// not numbers (see <c>docs/TESTING.md</c>).
        /// </summary>
        private static void AssertGetsGentler(string row, Func<PresetId, double> value)
        {
            PresetId[] scale = { PresetId.Subtle, PresetId.Balanced, PresetId.Generous, PresetId.Tame };

            foreach (PresetId p in scale)
            {
                Assert.True(
                    value(p) <= 1.0 + 1e-9,
                    $"{row}: {p} ({value(p)}) is above vanilla - no preset may make spore areas worse");
            }

            for (int i = 1; i < scale.Length; i++)
            {
                Assert.True(
                    value(scale[i]) <= value(scale[i - 1]) + 1e-9,
                    $"{row}: {scale[i]} ({value(scale[i])}) is harsher than {scale[i - 1]} "
                    + $"({value(scale[i - 1])}) - the scale runs one way only");
            }

            Assert.True(
                value(PresetId.Tame) < value(PresetId.Subtle) - 1e-9,
                $"{row}: Tame is no gentler than Subtle - the row does nothing");
        }

        [Fact]
        public void CustomFollowsBalanced_AsASafetyFallbackOnly()
        {
            Assert.Equal(
                PresetCatalog.SporeAreaRadiusMultiplier(PresetId.Balanced),
                PresetCatalog.SporeAreaRadiusMultiplier(PresetId.Custom));
        }
    }
}
