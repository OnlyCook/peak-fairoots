using System;
using Fairoots.Core;
using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// Proves the spore-bomb recolor actually does what it claims - moves a
    /// spore bomb out of "looks like grass" green and into a magenta that a
    /// red-green colorblind player can still separate from foliage, without
    /// flattening the shading. Not seed-gated (the recolor is a flat,
    /// per-instance-identical treatment, like the explosion tuning), so there's
    /// no determinism proof to make; what needs proving is the color math.
    ///
    /// The two source colors below are the <em>real</em> values, read off the
    /// live <c>W/Peak_Standard</c> materials in a Roots level (2026-07-26) - not
    /// invented samples - so these tests fail if the math stops working on the
    /// actual game data rather than on a convenient hypothetical.
    /// </summary>
    public class SporeBombRecolorTests
    {
        /// <summary>Regular spore bomb (<c>Forest_SporeFungus</c>) - the green one that camouflages.</summary>
        private static readonly Rgb VanillaRegular = new Rgb(0.24, 0.406, 0.109);

        /// <summary>Explosive spore bomb (<c>Jungle_SporeMushroomExplo</c>) - orange, and critically, <b>zero blue</b>.</summary>
        private static readonly Rgb VanillaExplosive = new Rgb(0.717, 0.252, 0.0);

        private static Rgb Recolor(Rgb vanilla) =>
            SporeBombRecolor.Recolor(vanilla, SporeBombRecolor.FallbackSporeColor, SporeBombRecolor.SaturationBlend);

        [Fact]
        public void FallbackSporeColorIsActuallyMagenta()
        {
            var hsv = SporeBombRecolor.ToHsv(SporeBombRecolor.FallbackSporeColor);

            // 270-350 deg is the magenta/pink wedge - past red (0/360) and short
            // of violet. Anything outside it isn't the hue this feature is for.
            Assert.InRange(hsv.H, 270.0, 350.0);
            Assert.True(hsv.S > 0.5, "the reference color has to carry a strong hue to adopt");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RecoloredSporeBombsCarryRealBlue(bool explosive)
        {
            // The whole point, and the reason this isn't a multiplicative tint:
            // red-green colorblindness leaves the blue channel intact, so a
            // hazard is only reliably distinguishable from green foliage if it
            // actually has blue in it. The explosive variant's vanilla color has
            // *zero* blue, which no multiplication could ever fix - it could only
            // ever be pushed to pure red, the one result that defeats the
            // feature's purpose.
            var result = Recolor(explosive ? VanillaExplosive : VanillaRegular);

            Assert.True(result.B > 0.0, $"result must have blue content (got {result})");
            Assert.True(
                result.B > result.G,
                $"blue must exceed green or it reads as red/orange rather than magenta (got {result})");
            Assert.True(result.R > result.B, $"red should still lead (got {result})");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RecolorAdoptsTheSporeHue(bool explosive)
        {
            var result = SporeBombRecolor.ToHsv(Recolor(explosive ? VanillaExplosive : VanillaRegular));
            var target = SporeBombRecolor.ToHsv(SporeBombRecolor.FallbackSporeColor);

            Assert.Equal(target.H, result.H, 3);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RecolorPreservesLuminance(bool explosive)
        {
            // Brightness is carried over deliberately: it's the per-slot lightness
            // differences that read as shading and surface detail, so a recolor
            // that overwrote them would turn the mushroom into a flat silhouette.
            var vanilla = explosive ? VanillaExplosive : VanillaRegular;
            var result = Recolor(vanilla);

            Assert.Equal(SporeBombRecolor.Luminance(vanilla), SporeBombRecolor.Luminance(result), 6);
        }

        [Fact]
        public void MatchingHsvValueAloneWouldHaveBeenTooDark()
        {
            // Regression guard for the 2026-07-26 "it's a bit too dark" report.
            // Preserving HSV value *looks* like preserving brightness but isn't:
            // value is just the largest channel, while perceived brightness is
            // dominated by green. This asserts the exact trap - an equal-value
            // magenta really does lose about half the green's luminance - so
            // nobody "simplifies" MatchLuminance back out of Recolor.
            var equalValue = SporeBombRecolor.FromHsv(
                SporeBombRecolor.ToHsv(SporeBombRecolor.FallbackSporeColor).H,
                SporeBombRecolor.Saturation(VanillaRegular),
                SporeBombRecolor.ToHsv(VanillaRegular).V);

            Assert.True(
                SporeBombRecolor.Luminance(equalValue) < SporeBombRecolor.Luminance(VanillaRegular) * 0.65,
                "the equal-HSV-value magenta should be markedly darker than the green it replaces");
            Assert.Equal(
                SporeBombRecolor.Luminance(VanillaRegular),
                SporeBombRecolor.Luminance(Recolor(VanillaRegular)),
                6);
        }

        [Fact]
        public void LuminanceMatchingNeverPushesAnLdrColorPastWhite()
        {
            // A dark, heavily green-weighted source needs a big gain to reach
            // luminance parity; the cap has to stop that blowing out to a clipped
            // white-pink. When it binds, the result may be dimmer than the
            // reference - that's the intended trade, not a bug.
            var result = Recolor(new Rgb(0.05, 0.95, 0.05));

            Assert.InRange(result.R, 0.0, 1.0);
            Assert.InRange(result.G, 0.0, 1.0);
            Assert.InRange(result.B, 0.0, 1.0);
        }

        [Fact]
        public void LuminanceMatchingKeepsHdrHeadroom()
        {
            // An HDR source may legitimately exceed 1, so the cap must be its own
            // peak rather than a hardcoded white point.
            var result = Recolor(new Rgb(0.5, 3.0, 0.5));

            Assert.True(
                Math.Max(result.R, Math.Max(result.G, result.B)) > 1.0,
                $"an HDR source must keep its headroom (got {result})");
        }

        [Fact]
        public void RecolorKeepsTheGreenVariantSaturated()
        {
            // "Slightly more saturated" was the ask - what must not happen is the
            // hue swap washing the hazard out into something even less visible.
            var result = Recolor(VanillaRegular);

            Assert.True(
                SporeBombRecolor.Saturation(result) >= SporeBombRecolor.Saturation(VanillaRegular),
                $"recolor must not desaturate (before={SporeBombRecolor.Saturation(VanillaRegular):0.###}, " +
                $"after={SporeBombRecolor.Saturation(result):0.###})");
        }

        [Fact]
        public void DistinctVanillaColorsStayDistinct()
        {
            // Two variants sharing a hue is fine and intended; collapsing to the
            // *identical* color would mean the recolor had thrown away the
            // brightness information that keeps them looking like different props.
            var regular = Recolor(VanillaRegular);
            var explosive = Recolor(VanillaExplosive);

            Assert.NotEqual(regular.ToString(), explosive.ToString());
        }

        [Fact]
        public void BlackStaysBlack()
        {
            // Unused/disabled shader slots are authored black. Giving them a hue
            // would switch on parts of the look the artist turned off - which is
            // exactly the class of bug that produced pink veins over an otherwise
            // green mushroom in the first version of this feature.
            var result = Recolor(new Rgb(0, 0, 0));

            Assert.Equal(0.0, result.R, 6);
            Assert.Equal(0.0, result.G, 6);
            Assert.Equal(0.0, result.B, 6);
        }

        [Fact]
        public void UnreadableSporeColorLeavesTheOriginalAlone()
        {
            // A black/zeroed status color means "we couldn't read it" - the honest
            // response is to not recolor, never to produce garbage.
            var result = SporeBombRecolor.Recolor(VanillaRegular, new Rgb(0, 0, 0), SporeBombRecolor.SaturationBlend);

            Assert.Equal(VanillaRegular.R, result.R, 6);
            Assert.Equal(VanillaRegular.G, result.G, 6);
            Assert.Equal(VanillaRegular.B, result.B, 6);
        }

        [Fact]
        public void HdrIntensityOfTheSporeColorDoesNotChangeTheResult()
        {
            // colorSpores is an HDR field ([ColorUsage(false, true)]), so the value
            // read off the game may be scaled well past 1. Only hue and saturation
            // may matter - brightness comes from the material being recolored.
            var spore = SporeBombRecolor.FallbackSporeColor;
            var normal = Recolor(VanillaRegular);
            var hdr = SporeBombRecolor.Recolor(
                VanillaRegular,
                new Rgb(spore.R * 5.0, spore.G * 5.0, spore.B * 5.0),
                SporeBombRecolor.SaturationBlend);

            Assert.Equal(normal.R, hdr.R, 6);
            Assert.Equal(normal.G, hdr.G, 6);
            Assert.Equal(normal.B, hdr.B, 6);
        }

        [Fact]
        public void ZeroSaturationBlendKeepsTheOriginalSaturation()
        {
            var result = SporeBombRecolor.Recolor(VanillaRegular, SporeBombRecolor.FallbackSporeColor, 0.0);

            Assert.Equal(SporeBombRecolor.Saturation(VanillaRegular), SporeBombRecolor.Saturation(result), 6);
        }

        [Theory]
        [InlineData(0.0, 0.0, 0.0)]
        [InlineData(1.0, 1.0, 1.0)]
        [InlineData(0.24, 0.406, 0.109)]
        [InlineData(0.717, 0.252, 0.0)]
        [InlineData(0.5, 0.5, 0.5)]
        [InlineData(2.6, 0.509, 4.0)]
        public void HsvRoundTripsExactly(double r, double g, double b)
        {
            // Including an HDR sample (the last row is a real _StatusColor value
            // from the game) - the conversion must not clamp or wrap it.
            var original = new Rgb(r, g, b);
            var hsv = SporeBombRecolor.ToHsv(original);
            var back = SporeBombRecolor.FromHsv(hsv.H, hsv.S, hsv.V);

            Assert.Equal(original.R, back.R, 6);
            Assert.Equal(original.G, back.G, 6);
            Assert.Equal(original.B, back.B, 6);
        }

        [Fact]
        public void SaturationIsZeroForBlack()
        {
            Assert.Equal(0.0, SporeBombRecolor.Saturation(new Rgb(0, 0, 0)));
        }
    }
}
