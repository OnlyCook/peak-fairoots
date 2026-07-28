namespace Fairoots.Core
{
    /// <summary>
    /// Pure arithmetic for the spore-cloud translucency setting - the readability
    /// fix for "am I actually in a spore cloud right now?".
    ///
    /// <b>The problem it solves.</b> The game already answers that question: the
    /// Spores status puts a colored filter over the screen while spores are being
    /// applied. But the cloud VFX itself is dense and the *same* color as that
    /// filter, so standing next to a cloud and standing inside one look nearly
    /// identical - the overlay reads as "there's green in front of me," which is
    /// also what the cloud looks like from outside. Thinning the cloud's own alpha
    /// separates the two: the overlay is then the only thing that fills the frame,
    /// while the cloud stays visible enough to be seen and walked around.
    ///
    /// Purely cosmetic and per-client, like <see cref="SporeBombRecolor"/> - it
    /// changes what one player sees, never where the hazard is or what it does. The
    /// hazard volume is <see cref="SporeAreaTuning"/>'s business and is deliberately
    /// untouched here: a cloud that *looks* smaller than it is would be worse than
    /// an opaque one.
    ///
    /// Not seed-gated (every cloud gets the identical flat treatment), same shape as
    /// <see cref="SporeAreaTuning"/> and <see cref="SporeBombExplosionTuning"/>.
    /// </summary>
    public static class SporeCloudOpacity
    {
        /// <summary>The multiplier that means "leave the VFX exactly as the artist authored it".</summary>
        public const double Vanilla = 1.0;

        /// <summary>
        /// Whether this multiplier is a no-op, i.e. the caller should write the
        /// cached vanilla values back rather than a scaled copy. Anything at or
        /// above 1 counts: alpha is capped at 1 anyway, so a multiplier above 1
        /// could only ever push already-opaque particles to the same place they
        /// started, and treating it as vanilla keeps "restore" exact.
        /// </summary>
        public static bool IsVanilla(double multiplier) => multiplier >= Vanilla;

        /// <summary>
        /// Scale one authored alpha value by the opacity multiplier, clamped to the
        /// valid <c>[0, 1]</c> range a color channel can hold. Multiplicative rather
        /// than absolute so the VFX's *internal* alpha variation survives: a spore
        /// cloud's particles fade in and out over their lifetime and vary between
        /// the two systems that make up one cloud, and flattening every particle to
        /// a single alpha would replace a soft volume with a uniform sheet - which
        /// is the look this setting exists to get rid of.
        /// </summary>
        public static float ScaleAlpha(float vanillaAlpha, double multiplier)
        {
            double scaled = vanillaAlpha * multiplier;
            if (scaled <= 0.0)
            {
                return 0f;
            }

            return scaled >= 1.0 ? 1f : (float)scaled;
        }
    }
}
