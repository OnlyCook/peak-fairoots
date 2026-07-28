using Fairoots.Core;
using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// The outline color derived for Fairoots' own on-screen text. The properties
    /// that matter are that the stroke stays the *same colour family* as the text
    /// (so it reads as part of it rather than a black shape behind it) while being
    /// unambiguously darker (so it actually separates the text from a same-coloured
    /// spore cloud behind it).
    /// </summary>
    public class LabelColorsTests
    {
        private static readonly Rgb SporePink = SporeBombRecolor.FallbackSporeColor;

        [Fact]
        public void OutlineIsDarkerThanTheText()
        {
            Rgb outline = LabelColors.Outline(SporePink);

            Assert.True(SporeBombRecolor.Luminance(outline) < SporeBombRecolor.Luminance(SporePink));
        }

        [Fact]
        public void OutlineKeepsTheHue_ItIsNotFlattenedToBlack()
        {
            var text = SporeBombRecolor.ToHsv(SporePink);
            var outline = SporeBombRecolor.ToHsv(LabelColors.Outline(SporePink));

            Assert.Equal(text.H, outline.H, 3);
            Assert.Equal(text.S, outline.S, 3);
        }

        [Fact]
        public void FullDarkening_IsBlack_AndIsTheFloor()
        {
            Rgb black = LabelColors.Outline(SporePink, 1.0);
            Assert.Equal(0.0, SporeBombRecolor.Luminance(black), 6);

            // Past the floor rather than wrapping into a bright color again.
            Assert.Equal(0.0, SporeBombRecolor.Luminance(LabelColors.Outline(SporePink, 2.0)), 6);
        }

        [Fact]
        public void ZeroDarkening_LeavesTheColorAlone()
        {
            Rgb same = LabelColors.Outline(SporePink, 0.0);

            Assert.Equal(SporePink.R, same.R, 4);
            Assert.Equal(SporePink.G, same.G, 4);
            Assert.Equal(SporePink.B, same.B, 4);
        }

        [Fact]
        public void ItTracksWhateverColorTheTextIs_NotJustPink()
        {
            // The label reads its colour live off the game's Spores status colour, so
            // the outline has to follow any hue rather than assuming one.
            var green = new Rgb(0.24, 0.406, 0.109);
            var outline = SporeBombRecolor.ToHsv(LabelColors.Outline(green));

            Assert.Equal(SporeBombRecolor.ToHsv(green).H, outline.H, 3);
            Assert.True(SporeBombRecolor.Luminance(LabelColors.Outline(green)) < SporeBombRecolor.Luminance(green));
        }
    }
}
