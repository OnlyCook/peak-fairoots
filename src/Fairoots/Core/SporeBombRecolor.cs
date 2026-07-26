using System;

namespace Fairoots.Core
{
    /// <summary>
    /// A Unity-free RGB triple, so the recolor math below can live in
    /// <c>Core/</c> (and be unit-tested without a game install) instead of
    /// depending on <c>UnityEngine.Color</c>. The game-facing
    /// <c>SporeBombs/SporeBombRecolorPatch</c> converts to/from
    /// <c>UnityEngine.Color</c> at the boundary.
    ///
    /// Channels are deliberately unclamped: these are linear-space material
    /// colors, and some of PEAK's are HDR (values above 1 are normal).
    /// </summary>
    public readonly struct Rgb
    {
        public Rgb(double r, double g, double b)
        {
            R = r;
            G = g;
            B = b;
        }

        public double R { get; }

        public double G { get; }

        public double B { get; }

        public static readonly Rgb White = new Rgb(1.0, 1.0, 1.0);

        public override string ToString() => $"({R:0.###}, {G:0.###}, {B:0.###})";
    }

    /// <summary>
    /// The spore-bomb recolor decision (ROADMAP.md's readability complaint:
    /// spore bombs are green hazards sitting in green grass on green ground, so
    /// they camouflage into the terrain even when they aren't literally hidden
    /// inside a fern - see <c>SporeBombCullPatch</c> for that separate, physical
    /// case). This shifts them to the pink/magenta hue the game's own Spores
    /// status effect uses (<c>CharacterAfflictions.colorSpores</c>, read live at
    /// runtime by the patch - see <see cref="FallbackSporeColor"/>), so a hazard
    /// reads as a hazard at a glance.
    ///
    /// <b>Not seed-gated</b>, same as <see cref="SporeBombExplosionTuning"/>:
    /// every spore bomb gets the identical flat treatment, so there's no
    /// per-instance probabilistic decision here, just arithmetic. It's also
    /// purely cosmetic and therefore purely client-side - deliberately NOT
    /// host-authoritative (see <c>PluginConfig</c>'s remarks), since one
    /// player's readability preference has no business being dictated by
    /// whoever happens to be hosting.
    ///
    /// <b>Why a hue replacement and not a multiplicative tint.</b> The first
    /// version multiplied each material color by a per-channel gain, on the
    /// assumption that the mushroom's green came from a texture over a neutral
    /// white base color (the usual case), where multiplying is the only
    /// available lever. Runtime probing (2026-07-26) disproved that: PEAK's
    /// props use a <c>W/Peak_Standard</c> shader whose color slots carry
    /// genuine authored colors - the regular spore bomb's is
    /// <c>(0.24, 0.406, 0.109)</c> green, the explosive one's
    /// <c>(0.717, 0.252, 0)</c> orange. That makes a direct hue replacement both
    /// possible and strictly better, because <b>multiplication can never add a
    /// channel that isn't already there</b>: the explosive variant has zero blue,
    /// so no gain whatsoever could make it magenta - it could only ever go pure
    /// red. Pure red against green foliage is precisely the pair red-green
    /// colorblind players can't separate, which would defeat the entire point of
    /// the feature. Adopting the target hue outright puts real blue into the
    /// result, so it lands as magenta/pink - distinguishable from green on the
    /// blue channel, which red-green colorblindness leaves intact.
    ///
    /// Brightness is deliberately carried over from the original color rather
    /// than taken from the target, so the per-slot lightness differences the
    /// artist authored - which is what reads as shading and surface detail -
    /// survive the recolor instead of flattening into one uniform pink blob.
    ///
    /// <b>Brightness means luminance, not HSV value</b> (fixed 2026-07-26 after
    /// the first hue-replacement build came out visibly too dark in-game).
    /// Matching HSV value looks like it preserves brightness and doesn't: value
    /// is just the largest channel, while perceived brightness is dominated by
    /// green (0.715 of Rec. 709 luminance vs. red's 0.213 and blue's 0.072).
    /// Swapping a green hue for a magenta one at identical "value" therefore
    /// throws away most of the luminance - the regular spore bomb's green
    /// measures 0.349 and its equal-value magenta only 0.182, roughly half,
    /// which read in-game as a near-black maroon lump that looked out of place
    /// against the terrain. <see cref="MatchLuminance"/> rescales the recolored
    /// result back onto the original's luminance, which is what actually keeps
    /// it looking like the same object in a different color.
    /// </summary>
    public static class SporeBombRecolor
    {
        /// <summary>
        /// How far to move each color's saturation toward the target's, 0-1.
        /// The hue is always taken outright (a partial hue blend just lands in
        /// whatever muddy in-between the two hues share, which is the opposite
        /// of the goal); saturation blends so an intentionally washed-out slot
        /// stays relatively washed out. This is the single tuning point for
        /// "how strong is the recolor" - the config setting is a plain on/off,
        /// by design.
        /// </summary>
        public const double SaturationBlend = 0.8;

        /// <summary>
        /// Used when the live <c>CharacterAfflictions.colorSpores</c> can't be
        /// read (no local character yet). Approximates the game's own Spores
        /// status color - a saturated magenta-pink. Only its hue and saturation
        /// matter; <see cref="Recolor"/> ignores its brightness.
        /// </summary>
        public static readonly Rgb FallbackSporeColor = new Rgb(0.93, 0.24, 0.62);

        /// <summary>Rec. 709 relative luminance - used by the tests and the patch's logging, not by the recolor itself.</summary>
        public static double Luminance(Rgb c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

        /// <summary>HSV-style saturation ((max - min) / max), 0 for black.</summary>
        public static double Saturation(Rgb c) => ToHsv(c).S;

        /// <summary>
        /// Recolors one authored material color to the target's hue.
        ///
        /// Returns <paramref name="original"/> untouched when either color is
        /// black - a black slot is either unused or a genuine "no contribution"
        /// value, and there's no hue to give it or take from it. That guard is
        /// what keeps unused/disabled shader slots from being switched on by
        /// the recolor.
        /// </summary>
        public static Rgb Recolor(Rgb original, Rgb sporeColor, double saturationBlend)
        {
            var target = ToHsv(sporeColor);
            var source = ToHsv(original);
            if (target.V <= 0.0 || source.V <= 0.0)
            {
                return original;
            }

            double blend = saturationBlend < 0.0 ? 0.0 : (saturationBlend > 1.0 ? 1.0 : saturationBlend);
            double saturation = source.S + (target.S - source.S) * blend;

            return MatchLuminance(FromHsv(target.H, saturation, source.V), original);
        }

        /// <summary>
        /// Rescales <paramref name="color"/> so its Rec. 709 luminance matches
        /// <paramref name="reference"/>'s - the step that stops a hue swap from
        /// silently changing how bright the object looks (see the class
        /// remarks for why HSV value isn't good enough).
        ///
        /// The scale is capped so no channel exceeds the brighter of 1.0 and
        /// the reference's own peak channel: an ordinary (LDR) color must not
        /// be pushed past white, while an HDR source keeps whatever headroom it
        /// already had. When the cap binds, the result is simply as close to the
        /// reference's luminance as it can get without clipping.
        /// </summary>
        public static Rgb MatchLuminance(Rgb color, Rgb reference)
        {
            double referenceLuminance = Luminance(reference);
            double luminance = Luminance(color);
            if (luminance <= 0.0 || referenceLuminance <= 0.0)
            {
                return color;
            }

            double scale = referenceLuminance / luminance;
            double peak = Math.Max(color.R, Math.Max(color.G, color.B));
            double ceiling = Math.Max(1.0, Math.Max(reference.R, Math.Max(reference.G, reference.B)));
            if (peak > 0.0 && peak * scale > ceiling)
            {
                scale = ceiling / peak;
            }

            return new Rgb(color.R * scale, color.G * scale, color.B * scale);
        }

        /// <summary>Hue (degrees, 0-360), saturation (0-1) and value (max channel; may exceed 1 for HDR colors).</summary>
        public static (double H, double S, double V) ToHsv(Rgb c)
        {
            double max = Math.Max(c.R, Math.Max(c.G, c.B));
            double min = Math.Min(c.R, Math.Min(c.G, c.B));
            double delta = max - min;

            if (max <= 0.0)
            {
                return (0.0, 0.0, max);
            }

            double s = delta / max;
            if (delta <= 0.0)
            {
                return (0.0, 0.0, max);
            }

            double h;
            if (max == c.R)
            {
                h = 60.0 * (((c.G - c.B) / delta) % 6.0);
            }
            else if (max == c.G)
            {
                h = 60.0 * ((c.B - c.R) / delta + 2.0);
            }
            else
            {
                h = 60.0 * ((c.R - c.G) / delta + 4.0);
            }

            if (h < 0.0)
            {
                h += 360.0;
            }

            return (h, s, max);
        }

        /// <summary>Inverse of <see cref="ToHsv"/>. <paramref name="v"/> may exceed 1 (HDR); <paramref name="s"/> is clamped to 0-1.</summary>
        public static Rgb FromHsv(double h, double s, double v)
        {
            s = s < 0.0 ? 0.0 : (s > 1.0 ? 1.0 : s);
            if (s <= 0.0)
            {
                return new Rgb(v, v, v);
            }

            h = h % 360.0;
            if (h < 0.0)
            {
                h += 360.0;
            }

            double c = v * s;
            double hp = h / 60.0;
            double x = c * (1.0 - Math.Abs(hp % 2.0 - 1.0));
            double m = v - c;

            double r, g, b;
            if (hp < 1.0)
            {
                r = c; g = x; b = 0.0;
            }
            else if (hp < 2.0)
            {
                r = x; g = c; b = 0.0;
            }
            else if (hp < 3.0)
            {
                r = 0.0; g = c; b = x;
            }
            else if (hp < 4.0)
            {
                r = 0.0; g = x; b = c;
            }
            else if (hp < 5.0)
            {
                r = x; g = 0.0; b = c;
            }
            else
            {
                r = c; g = 0.0; b = x;
            }

            return new Rgb(r + m, g + m, b + m);
        }
    }
}
