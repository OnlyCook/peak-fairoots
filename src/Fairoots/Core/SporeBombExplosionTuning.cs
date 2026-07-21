using System;

namespace Fairoots.Core
{
    /// <summary>
    /// Phase 4 continued (ROADMAP.md preset table rows "Spore bomb trigger
    /// radius" / "knockback/explosion force" / "screen-shake distance cap" /
    /// "particle/VFX count"). Pure arithmetic only - no Unity/BepInEx
    /// dependency, so it's testable standalone (see CODEBASE.md's Core split
    /// rule). The actual field reads/writes on the game's <c>SphereCollider</c>,
    /// <c>AOE</c>, <c>ExplosionEffect</c> and <c>AddScreenshake</c> components
    /// live in the game-facing Harmony patches under <c>src/Fairoots/SporeBombs/</c>
    /// and just call into these functions for the numbers.
    ///
    /// None of this is seed-gated: unlike the cull decision, every kept spore
    /// bomb gets the same trigger-radius/knockback/shake/VFX treatment, so
    /// there's no per-instance RNG involved (CLAUDE.md's determinism rule only
    /// applies to Fairoots-owned probabilistic decisions - this is a flat
    /// multiplier, not a decision).
    /// </summary>
    public static class SporeBombExplosionTuning
    {
        /// <summary>
        /// Sentinel for <see cref="CapScreenshakeRange"/>: a cap of 0 or less means
        /// "leave the vanilla range alone" (Preset 1/Subtle's "vanilla, ~75m
        /// unconfirmed" row - there's no real-world reason to cap a shake range at
        /// literally 0m, so that value is repurposed as "no cap" rather than adding
        /// a second sentinel constant).
        /// </summary>
        public const float NoScreenshakeCap = 0f;

        /// <summary>Scale a trigger hitbox's <c>SphereCollider.radius</c> by a preset/override multiplier.</summary>
        public static float ScaleTriggerRadius(float vanillaRadius, double multiplier)
        {
            return (float)(vanillaRadius * multiplier);
        }

        /// <summary>Scale the spawned explosion's <c>AOE.knockback</c> (and item knockback) by a preset/override multiplier.</summary>
        public static float ScaleKnockback(float vanillaKnockback, double multiplier)
        {
            return (float)(vanillaKnockback * multiplier);
        }

        /// <summary>
        /// Scale <c>ExplosionEffect.explosionPointCount</c>/<c>subExplosionPointCount</c>
        /// by a preset/override multiplier, rounded to the nearest whole orb and never
        /// negative (a multiplier of 0 means "no extra orbs", not a crash).
        /// </summary>
        public static int ScaleVfxCount(int vanillaCount, double multiplier)
        {
            int scaled = (int)Math.Round(vanillaCount * multiplier, MidpointRounding.AwayFromZero);
            return scaled < 0 ? 0 : scaled;
        }

        /// <summary>
        /// Cap <c>AddScreenshake.range</c> (the distance at which the shake falls off
        /// to zero) at <paramref name="capMeters"/>. <see cref="NoScreenshakeCap"/>
        /// (or any non-positive value) leaves the vanilla range untouched; otherwise
        /// the smaller of the vanilla range and the cap wins (never *increases* an
        /// already-tighter vanilla range).
        /// </summary>
        public static float CapScreenshakeRange(float vanillaRange, float capMeters)
        {
            if (capMeters <= NoScreenshakeCap)
            {
                return vanillaRange;
            }

            return vanillaRange < capMeters ? vanillaRange : capMeters;
        }

        /// <summary>
        /// True if a player at <paramref name="heightAboveBase"/> meters above a
        /// spore bomb's base should be treated as having jumped over it rather
        /// than actually triggering it - a bug fix for the "Spore Bomb"/"Poison
        /// Spore Bomb" variants' vanilla trigger sphere reaching absurdly far
        /// above the actual (short, wide) mushroom mesh (confirmed via the
        /// trigger-radius wireframe overlay), not a preset-scaled balance dial.
        /// <paramref name="maxTriggerHeightMeters"/> of 0 or less disables the
        /// cutoff entirely (vanilla behavior - never suppresses).
        /// </summary>
        public static bool ShouldSuppressTriggerForHeight(float heightAboveBase, float maxTriggerHeightMeters)
        {
            if (maxTriggerHeightMeters <= 0f)
            {
                return false;
            }

            return heightAboveBase > maxTriggerHeightMeters;
        }
    }
}
