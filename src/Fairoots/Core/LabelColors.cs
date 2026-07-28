namespace Fairoots.Core
{
    /// <summary>
    /// Color math for Fairoots' own on-screen text (currently just the spore-cloud
    /// warning label, <c>Ui/SporeWarningLabel</c>). Unity-free, like the rest of
    /// <c>Core/</c> - it works on <see cref="Rgb"/> and reuses
    /// <see cref="SporeBombRecolor"/>'s HSV conversion rather than carrying its own.
    /// </summary>
    public static class LabelColors
    {
        /// <summary>How far <see cref="Outline"/> pulls the value down by default.</summary>
        public const double DefaultOutlineDarkening = 0.7;

        /// <summary>
        /// The stroke color for text drawn in <paramref name="foreground"/>: the same
        /// hue and saturation, with the HSV value scaled down.
        ///
        /// <b>Darkened rather than flattened to black</b>, the same rule
        /// <c>peak-sense-of-direction</c>'s <c>ColorUtil.Darken</c> uses for its HUD
        /// labels, and for the same reason: a stroke that keeps its owner's hue reads
        /// as part of the same object, while a flat black outline reads as a separate
        /// black shape behind the text and looks pasted on. It still does the job an
        /// outline exists for - a pink label over a pink spore cloud would otherwise
        /// vanish into it, which is exactly the fusing problem this whole feature set
        /// is about.
        /// </summary>
        public static Rgb Outline(Rgb foreground, double amount = DefaultOutlineDarkening)
        {
            var hsv = SporeBombRecolor.ToHsv(foreground);
            double scale = 1.0 - amount;
            if (scale < 0.0)
            {
                scale = 0.0;
            }
            else if (scale > 1.0)
            {
                scale = 1.0;
            }

            return SporeBombRecolor.FromHsv(hsv.H, hsv.S, hsv.V * scale);
        }
    }
}
