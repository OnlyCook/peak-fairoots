using System;

namespace Fairoots.Core
{
    /// <summary>
    /// The "throw something at it and it stops moving" mechanic, extending to beetles
    /// and zombies what PEAK already lets you do to spiders.
    ///
    /// <b>What vanilla already has, per creature</b> - three completely different
    /// answers, which is why the game-facing side needs three approaches rather than
    /// one:
    /// <list type="bullet">
    /// <item><b>Spider</b>: fully implemented, and this is the model being copied.
    /// Hitting one with a thrown item fires an <c>EventOnItemCollision</c> whose
    /// UnityEvent (wired in the prefab, which is why nothing in the decompiled code
    /// appears to call it) reaches <c>SpiderTrigger.Bonk</c> →
    /// <c>Spider._stunnedTime = bonkStunTime</c>, a 5-second stun during which it can
    /// neither scan nor grab.</item>
    /// <item><b>Zombie</b>: <em>partially</em> there already, by accident of being a
    /// <c>Character</c>. A thrown item's <c>Bonkable</c> calls
    /// <c>GetComponentInParent&lt;Character&gt;()</c>, finds the zombie, and ragdolls it
    /// for <c>Bonkable.ragdollTime</c> - about a second. So the interaction exists but
    /// is far too brief to be counterplay; this mechanic is about its duration.</item>
    /// <item><b>Beetle</b>: nothing at all. A beetle is a <c>Mob</c>, not a
    /// <c>Character</c>, so <c>Bonkable</c> never finds anything to bonk, and it has no
    /// <c>EventOnItemCollision</c> either. Thrown items simply pass it by.</item>
    /// </list>
    ///
    /// <b>Deliberately weaker than the spider's 5 seconds</b>, per the maintainer, and
    /// weaker again for beetles specifically because of the shell - a beetle should feel
    /// like it shrugs off a thrown rock far better than a soft-bodied spider does.
    /// </summary>
    public static class CreatureKnockout
    {
        /// <summary>
        /// The spider's vanilla <c>bonkStunTime</c>, and the reference point both dials
        /// are meant to sit below. Not used as a value - kept as the documented anchor
        /// for what "less powerful than a spider knockout" is measured against.
        /// </summary>
        public const float SpiderStunSeconds = 5f;

        /// <summary>
        /// Longest knockout either dial will honour. Generous enough for "I want to be
        /// able to clear a path" without allowing a value that removes the creature from
        /// the run altogether - that's what the disable switches are for.
        /// </summary>
        public const float MaxSeconds = 60f;

        /// <summary>
        /// The game's own thrown-item impact threshold
        /// (<c>Bonkable.minBonkVelocity</c>/<c>minBonkVelocityThrown</c>, both 5) in
        /// <b>world units</b> per second.
        ///
        /// Kept as documentation, not as the value used: matching it was the first
        /// version's mistake. Live testing (2026-07-29) found that at 5 units/s
        /// <em>any</em> contact qualified - even the gentlest toss - because an item
        /// dropped from hand height is already past that speed by the time it lands, and
        /// <c>relativeVelocity</c> also picks up whatever the creature itself is doing.
        /// The threshold is a config setting now, defaulting well above this.
        /// </summary>
        public const float VanillaBonkableThresholdUnits = 5f;

        /// <summary>The largest throw speed the setting will accept, in meters per second.</summary>
        public const double MaxMinThrowSpeedMeters = 100.0;

        /// <summary>The largest throw distance the setting will accept, in meters.</summary>
        public const double MaxThrowDistanceMeters = 200.0;

        /// <summary>
        /// Measured impact speeds from live testing (2026-07-29), in m/s, kept because
        /// they are the only real calibration data this mechanic has and the next person
        /// to retune it will want them rather than a fresh guessing round:
        /// <list type="bullet">
        /// <item>Casual/medium throws that the maintainer judged <em>too gentle</em> to
        /// deserve a knockout: 23, 26.3, 30.6.</item>
        /// <item>Near-full-strength throws, the level that <em>should</em> be required:
        /// 36.6 (at a beetle) and 42.5 (at a zombie).</item>
        /// </list>
        /// So the threshold has to sit above ~31 to reject the first group. The shipped
        /// value was then chosen by the maintainer from in-game feel rather than by
        /// splitting the gap arithmetically: <b>36</b>, confirmed by play as the point
        /// where a knockout requires a genuinely committed throw. It sits just under the
        /// weakest logged near-max throw (36.6), so a full-strength throw still lands
        /// without having to be frame-perfect.
        /// </summary>
        public const double CalibratedMinThrowSpeedMeters = 36.0;

        /// <summary>
        /// Clamps the configured minimum throw speed, in <b>meters</b> per second. Kept
        /// in meters because that's the unit a player can reason about, and because PEAK's
        /// world units are not meters (1 unit = 1.6m - see <see cref="WorldUnits"/>); the
        /// game-facing side converts before comparing against a physics value.
        ///
        /// 0 is allowed and means "any contact counts", i.e. the behaviour that was
        /// reported as wrong - available deliberately, for anyone who wants it.
        /// </summary>
        public static float ResolveMinThrowSpeedMeters(double configuredMetersPerSecond)
        {
            if (double.IsNaN(configuredMetersPerSecond))
            {
                return 0f;
            }

            return (float)Math.Min(MaxMinThrowSpeedMeters, Math.Max(0.0, configuredMetersPerSecond));
        }

        /// <summary>
        /// Clamps a configured duration. Negative is treated as zero rather than
        /// rejected: zero is a meaningful setting (it turns the mechanic off for that
        /// creature), so there's no reason for a typo to throw instead of landing on the
        /// nearest sensible value.
        /// </summary>
        public static float ResolveSeconds(double configuredSeconds)
        {
            if (double.IsNaN(configuredSeconds))
            {
                return 0f;
            }

            return (float)Math.Min(MaxSeconds, Math.Max(0.0, configuredSeconds));
        }

        /// <summary>
        /// Whether the mechanic is switched off for this creature. Zero means "leave
        /// vanilla alone" - which for a zombie still leaves the ~1s <c>Bonkable</c>
        /// ragdoll it always had, and for a beetle means thrown items keep doing nothing
        /// at all.
        /// </summary>
        public static bool IsDisabled(double configuredSeconds)
        {
            return ResolveSeconds(configuredSeconds) <= 0f;
        }

        /// <summary>
        /// Whether an impact is hard enough to count. Both arguments are in the same
        /// units - the caller converts the configured meters-per-second threshold into
        /// world units first, since the impact speed comes straight from Unity physics.
        /// </summary>
        public static bool IsHardEnough(float impactSpeedUnits, float thresholdUnits)
        {
            return impactSpeedUnits >= thresholdUnits;
        }

        /// <summary>
        /// Clamps the configured maximum throw distance, in <b>meters</b>. 0 means "no
        /// distance requirement", matching how 0 reads on the speed threshold.
        /// </summary>
        public static float ResolveMaxThrowDistanceMeters(double configuredMeters)
        {
            if (double.IsNaN(configuredMeters))
            {
                return 0f;
            }

            return (float)Math.Min(MaxThrowDistanceMeters, Math.Max(0.0, configuredMeters));
        }

        /// <summary>
        /// Whether the thrower was close enough to the creature. The second gate on the
        /// mechanic, added because throw speed alone doesn't express "you have to commit
        /// to getting near it": a hard throw is still travelling fast a long way out, so
        /// speed alone would license sniping a zombie from across a ravine at no risk.
        ///
        /// A non-positive threshold disables the check, so distance can be opted out of
        /// without disabling the mechanic.
        /// </summary>
        public static bool IsCloseEnough(float distanceUnits, float thresholdUnits)
        {
            return thresholdUnits <= 0f || distanceUnits <= thresholdUnits;
        }
    }
}
