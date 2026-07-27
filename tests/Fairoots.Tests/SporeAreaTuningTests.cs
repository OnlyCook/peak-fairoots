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
        public void PresetProgression_SubtleIsVanillaAndEachPresetShrinksFurther()
        {
            Assert.Equal(1.0, PresetCatalog.SporeAreaRadiusMultiplier(PresetId.Subtle));
            Assert.True(PresetCatalog.SporeAreaRadiusMultiplier(PresetId.Balanced)
                        < PresetCatalog.SporeAreaRadiusMultiplier(PresetId.Subtle));
            Assert.True(PresetCatalog.SporeAreaRadiusMultiplier(PresetId.Generous)
                        < PresetCatalog.SporeAreaRadiusMultiplier(PresetId.Balanced));
            Assert.True(PresetCatalog.SporeAreaRadiusMultiplier(PresetId.Tame)
                        < PresetCatalog.SporeAreaRadiusMultiplier(PresetId.Generous));
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
