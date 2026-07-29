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
        /// Scales a creature's vanilla knockback impulse. Currently the beetle's
        /// <c>bonkForce</c>/<c>bonkForceUp</c> (both vanilla 100), the horizontal and
        /// vertical components of the shove in <c>Beetle.InflictAttack</c>. Both are
        /// scaled by the same multiplier so the shove keeps its vanilla *angle* and
        /// only changes in magnitude - scaling one alone would turn a knockback dial
        /// into a "beetles now launch you straight up" dial.
        ///
        /// Deliberately does not touch the third parameter of that same call,
        /// <c>bonkRange</c>: that's the radius over which the impulse falls off across
        /// the player's bodyparts (see <c>Character.RPCA_AddForceAtPosition</c>), i.e.
        /// how the hit is distributed, not how hard it is.
        ///
        /// Same baseline rule as <see cref="ScaleMovementSpeed"/>: pass the cached
        /// vanilla value, never the field's current one. Clamped at zero, so a
        /// negative multiplier means "no knockback" rather than "beetles now pull you
        /// towards them".
        /// </summary>
        public static float ScaleKnockback(float vanillaForce, double multiplier)
        {
            return (float)(vanillaForce * Math.Max(0.0, multiplier));
        }

        /// <summary>
        /// Scales how long a creature's hit keeps the player ragdolled - the argument
        /// both creatures pass to <c>Character.Fall(seconds)</c>:
        /// <c>Beetle.ragdollTime</c> (vanilla 2s) and <c>MushroomZombie.biteStunTime</c>
        /// (vanilla 3s). Lower means you get back on your feet sooner, i.e. it is
        /// harder for a creature to take control away from you.
        ///
        /// <b>Zero genuinely means "never ragdoll", not "ragdoll for an instant"</b>,
        /// and that falls out of vanilla's own code rather than needing a special
        /// case: <c>Character.RPCA_Fall</c> only ever raises the timer
        /// (<c>if (seconds &gt; data.fallSeconds) data.fallSeconds = seconds;</c>), so a
        /// scaled-to-zero duration can never satisfy that comparison and the player's
        /// ragdoll control is left exactly as it was. That same one-way rule is also
        /// why this can only ever shorten a *new* knockdown and never cuts an
        /// in-progress one short.
        ///
        /// Only the ragdoll is affected. The hit still lands, still applies its
        /// status/afflictions, and still shoves you - being pushed around while
        /// keeping control is the point of the dial.
        /// </summary>
        public static float ScaleRagdollTime(float vanillaSeconds, double multiplier)
        {
            return (float)(vanillaSeconds * Math.Max(0.0, multiplier));
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
