namespace Fairoots.Core
{
    /// <summary>
    /// Pure arithmetic for the persistent spore areas' tunable fields (Phase 6,
    /// ROADMAP.md's "Spore area radius" row). Not seed-gated: every spore area in
    /// the level gets the identical flat treatment, so there's no per-instance
    /// decision here, just scaling - same shape as
    /// <see cref="SporeBombExplosionTuning"/>.
    ///
    /// Distinct from <see cref="SporeBombExplosionTuning.ScaleSporeAreaRadius"/>,
    /// which scales the temporary mini spore area a *spore bomb* leaves on
    /// detonation (a spawned <c>AOE</c>'s range). This one scales the biome's own
    /// baked-in "Mushroom Spore Clouds" (a <c>StatusEmitter</c>'s
    /// <c>radius</c>/<c>innerFade</c>/<c>outerFade</c>). Two separate hazards,
    /// two separate settings.
    /// </summary>
    public static class SporeAreaTuning
    {
        /// <summary>
        /// Scale a spore area's radius. Never negative; a multiplier of 0 yields 0,
        /// which the game reads as "nothing is ever in range" (its own
        /// <c>InRange</c> check is a strict <c>&lt;</c> against
        /// <c>radius + outerFade</c>).
        /// </summary>
        public static float ScaleRadius(float vanillaRadius, double multiplier)
        {
            float scaled = (float)(vanillaRadius * multiplier);
            return scaled < 0f ? 0f : scaled;
        }

        /// <summary>
        /// Scale a spore area's <c>innerFade</c>/<c>outerFade</c> by the same
        /// multiplier as its radius, which is what keeps the falloff *shape*
        /// identical instead of just moving the outer boundary.
        ///
        /// This matters more than it looks. The native falloff (see
        /// <c>StatusEmitter.Update</c>) computes the applied fraction as
        /// <c>1 - (distance - (radius - innerFade)) / innerFade</c>, clamped to
        /// <c>[minAmount, 1]</c> - i.e. <c>innerFade</c> is the width of the
        /// ramp inside the boundary, measured inward from it. Roots' vanilla
        /// values are <c>radius = 16</c>, <c>innerFade = 8</c>: the ramp is
        /// exactly the outer half. Scaling the radius alone would leave that ramp
        /// at a fixed 8 units, so a shrunken area would be almost entirely ramp
        /// (weaker everywhere, its full-strength core gone) and an enlarged one
        /// almost entirely full-strength core - i.e. the radius dial would
        /// quietly double as a lethality dial, which is a separate setting's job.
        /// Scaling both keeps "the outer half fades in" true at every size.
        /// </summary>
        public static float ScaleFade(float vanillaFade, double multiplier)
        {
            float scaled = (float)(vanillaFade * multiplier);
            return scaled < 0f ? 0f : scaled;
        }

        /// <summary>
        /// Scale a cloud VFX transform's uniform scale factor. Same multiplier as
        /// the radius, so what a player sees matches what actually applies the
        /// status - the maintainer's explicit requirement (a hazard whose visible
        /// extent disagrees with its real extent is worse than either size on its
        /// own). Clamped to a small positive floor rather than 0: a zero-scaled
        /// transform is a degenerate matrix, and "invisible" is better expressed by
        /// the object being inactive (which the disable switch already does).
        /// </summary>
        public static float ScaleVisual(double multiplier)
        {
            float scaled = (float)multiplier;
            return scaled < MinVisualScale ? MinVisualScale : scaled;
        }

        /// <summary>Floor for <see cref="ScaleVisual"/> - see its remarks.</summary>
        public const float MinVisualScale = 0.01f;

        /// <summary>
        /// Scale how fast the Spores status builds up on a player standing in a
        /// spore area (ROADMAP.md's "Spore area lethality (status/sec)" row).
        /// Vanilla in Roots is <c>amount = 0.025</c>. Never negative: a negative
        /// <c>amount</c> flips the native code into its <c>SubtractStatus</c>
        /// branch, which would turn the hazard into a cure rather than merely
        /// switching it off. 0 means "never applies any spores," which is the
        /// intended reading of a 0 multiplier.
        /// </summary>
        /// <remarks>
        /// <b>Why this scales <c>amount</c> and not the tick interval, despite the
        /// setting being about how *often* spores are applied.</b> The native
        /// emitter ticks every <c>tickTime</c> (0.5s) and applies
        /// <c>amount * tickTime * falloff</c> per tick - so the amount applied per
        /// tick is itself proportional to the interval, and the resulting rate,
        /// <c>amount * falloff</c> per second, does not contain <c>tickTime</c> at
        /// all. Scaling the interval would therefore change only the granularity
        /// (fewer, bigger jumps vs. more, smaller ones) and leave the actual
        /// build-up rate exactly as it was - which is not what anyone asking for
        /// "spores get applied less often" wants. Scaling <c>amount</c> is the one
        /// lever that changes the rate, and it also preserves the smooth tick
        /// cadence the player already reads as normal.
        /// </remarks>
        public static float ScaleStatusRate(float vanillaAmount, double multiplier)
        {
            float scaled = (float)(vanillaAmount * multiplier);
            return scaled < 0f ? 0f : scaled;
        }
    }
}
