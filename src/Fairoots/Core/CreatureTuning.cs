using System;

namespace Fairoots.Core
{
    /// <summary>
    /// Pure arithmetic for the Phase 7 creature dials (ROADMAP.md). Not seed-gated:
    /// every zombie and every beetle gets the identical flat treatment, so there's no
    /// per-instance decision here, just scaling - same shape and same reasoning as
    /// <see cref="SporeBombExplosionTuning"/> and <see cref="SporeAreaTuning"/>.
    ///
    /// Unity-free so the awkward parts are unit-tested rather than only observable by
    /// walking up to a beetle (see CODEBASE.md's Core / game-facing split).
    /// </summary>
    public static class CreatureTuning
    {
        /// <summary>
        /// Scales a creature's vanilla movement speed. Applies to two different
        /// native fields with the same meaning: a beetle's <c>Mob.movementSpeed</c>
        /// (vanilla 5, used directly as a per-second position step) and a zombie's
        /// inherited <c>CharacterMovement.movementForce</c> (vanilla 10, a force
        /// magnitude - so this changes how hard the zombie pushes itself along, which
        /// is how PEAK expresses character speed).
        ///
        /// <paramref name="vanillaSpeed"/> must be the cached vanilla baseline, never
        /// the field's current value - re-scaling an already-scaled field compounds,
        /// and 1.0 would stop meaning "vanilla". That's the caller's job (the
        /// baseline caches in <c>Creatures/CreatureSpeedPatch</c>); this function only
        /// guarantees that given a true baseline, 1.0 returns it exactly.
        ///
        /// Clamped at zero: a negative multiplier would make a creature accelerate
        /// backwards rather than stand still, which is not what anyone typing a
        /// negative number into a "speed" setting means.
        /// </summary>
        public static float ScaleMovementSpeed(float vanillaSpeed, double multiplier)
        {
            return (float)(vanillaSpeed * Math.Max(0.0, multiplier));
        }

        /// <summary>
        /// Whether a multiplier is close enough to 1.0 to be treated as "leave the
        /// game alone". Used by the restore paths so a dial sitting at vanilla puts
        /// the exact authored value back rather than a float-rounded near-miss of it.
        /// </summary>
        public static bool IsVanilla(double multiplier)
        {
            return Math.Abs(multiplier - 1.0) < 1e-6;
        }
    }
}
