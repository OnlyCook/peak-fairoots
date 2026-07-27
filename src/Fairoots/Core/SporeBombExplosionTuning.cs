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
        /// The distance-cap setting only means anything if the shake is actually
        /// distance-attenuated. The game's <c>AddScreenshake</c> only consults its
        /// <c>range</c> field when <c>positional</c> is set - a non-positional shake
        /// calls <c>GamefeelHandler.AddPerlinShake</c>, which is global and shakes
        /// every player's camera at full strength no matter how far away the
        /// detonation was. So whenever a cap is configured, Fairoots forces the
        /// detonation's shakes positional; this returns true exactly then.
        /// </summary>
        public static bool ShouldForcePositionalScreenshake(float capMeters)
        {
            return capMeters > NoScreenshakeCap;
        }

        /// <summary>
        /// The <c>AddScreenshake.range</c> to actually write, given whether the
        /// component was positional to begin with.
        ///
        /// <paramref name="capUnits"/> is in **world units**, not the meters the
        /// setting is denominated in - the caller converts via
        /// <see cref="WorldUnits.MetersToUnits"/> first, because the vanilla range
        /// it's compared against (and the field it's written to) are world units.
        ///
        /// If it already was, <see cref="CapScreenshakeRange"/> applies (take
        /// whichever of vanilla/cap is tighter). If it wasn't, its <c>range</c> is a
        /// dead field the game never read - the serialized value is meaningless
        /// (typically the 15m component default) and clamping against it would
        /// silently produce a far tighter falloff than the player asked for, so the
        /// configured cap is used verbatim.
        /// </summary>
        public static float ResolveScreenshakeRange(float vanillaRange, bool vanillaPositional, float capUnits)
        {
            if (capUnits <= NoScreenshakeCap)
            {
                return vanillaRange;
            }

            return vanillaPositional ? CapScreenshakeRange(vanillaRange, capUnits) : capUnits;
        }

        /// <summary>
        /// How long after a spore-bomb detonation (seconds) and how far from it
        /// (meters) a screen shake is still attributed to that detonation. The
        /// explosion VFX spawns its orbs on a staggered coroutine *after* the
        /// detonation prefab is instantiated, so those orbs' own
        /// <c>AddScreenshake</c> components don't exist yet when the spawn-time
        /// tuning pass runs and have to be caught as they fire instead. The window
        /// is deliberately tight so unrelated shakes (a fall, a rockfall, another
        /// player's item) are never mistaken for detonation shakes.
        /// </summary>
        public const float DetonationScreenshakeWindowSeconds = 4f;

        /// <inheritdoc cref="DetonationScreenshakeWindowSeconds"/>
        public const float DetonationScreenshakeRadiusMeters = 20f;

        /// <summary>
        /// True if a screen shake <paramref name="ageSeconds"/> after and
        /// <paramref name="distanceMeters"/> away from a known spore-bomb detonation
        /// belongs to that detonation.
        /// </summary>
        public static bool IsDetonationScreenshake(float ageSeconds, float distanceMeters)
        {
            return ageSeconds >= 0f
                && ageSeconds <= DetonationScreenshakeWindowSeconds
                && distanceMeters <= DetonationScreenshakeRadiusMeters;
        }

        /// <summary>
        /// Scale a status-carrying spawned AOE's <c>range</c> (which the game
        /// reuses as both the <c>OverlapSphere</c> radius and the Spores
        /// status-effect radius - runtime-confirmed 2026-07-22: the "Spore Bomb"/
        /// "Poison Spore Bomb" detonation prefabs have no <c>StatusEmitter</c> of
        /// their own, so the temporary spore area is just this same <c>AOE</c>'s
        /// range) by a preset/override multiplier.
        /// </summary>
        public static float ScaleSporeAreaRadius(float vanillaValue, double multiplier)
        {
            float scaled = (float)(vanillaValue * multiplier);
            return scaled < 0f ? 0f : scaled;
        }

        /// <summary>
        /// Internal reference absolute cutoff, in meters, that
        /// <c>trigger-height-multiplier</c>'s <c>1.0</c> used to mean outright
        /// before it became a preset-scaled multiplier (2026-07-27) - the
        /// maintainer's own playtest-tuned value at the time. Kept only as the
        /// anchor so a hand-picked multiplier (Balanced's in particular) can
        /// reproduce a specific previously-known absolute cutoff; not itself
        /// user-facing.
        /// </summary>
        public const float TriggerHeightBaselineMeters = 2.8f;

        /// <summary>
        /// Converts a <c>trigger-height-multiplier</c> preset/override value into
        /// the absolute cutoff <see cref="ShouldSuppressTriggerForHeight"/>
        /// consumes. <c>1.0</c> or higher means vanilla (cutoff disabled,
        /// matching the old "0 disables" convention from before this setting
        /// became a multiplier) - anything above vanilla has no separate meaning,
        /// since a taller-than-vanilla cutoff would never fire either. Below
        /// <c>1.0</c> scales down from <see cref="TriggerHeightBaselineMeters"/>.
        /// </summary>
        public static float ResolveTriggerHeightCutoffMeters(double multiplier)
        {
            return multiplier >= 1.0 ? 0f : (float)(multiplier * TriggerHeightBaselineMeters);
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
