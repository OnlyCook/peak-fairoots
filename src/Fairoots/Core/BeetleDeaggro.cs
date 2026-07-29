using System;

namespace Fairoots.Core
{
    /// <summary>
    /// The beetle deaggro rule. Unlike <see cref="ZombieDeaggro"/> this is tuning
    /// rather than invention - beetles genuinely do give up in vanilla - so
    /// <b>1.0 means exactly vanilla</b> here, and the game-facing patch no-ops entirely
    /// at that value rather than reproducing vanilla by arithmetic.
    ///
    /// <b>Why this needs a suppression window at all</b> (live-reported 2026-07-29: the
    /// first version showed no difference between 0.0 and 3.0). Vanilla
    /// <c>Mob.Targeting()</c> uses one number, <c>aggroDistance</c>, for two different
    /// questions - "who do I notice?" and "do I still have them?" - and re-answers both
    /// from scratch every couple of seconds. Scaling that number only while a target
    /// exists, which is the obvious way to tune retention without touching
    /// acquisition, <b>defeats itself</b>: shrinking it drops the target, and the very
    /// next scan has no target, so it runs at the full vanilla radius and re-acquires
    /// the same player immediately. The beetle flickers between chasing and not, twice
    /// a second-ish, which in play is indistinguishable from just chasing.
    ///
    /// So a dropped target has to <em>stay</em> dropped for a moment. The window is
    /// <see cref="SuppressionSeconds"/>, taken from the game's own
    /// <c>Mob.targetSwitchCooldown</c> (5s) - i.e. exactly how long vanilla already
    /// makes a mob wait before it's allowed to change its mind about a target.
    ///
    /// <b>And why the high end did nothing either</b>: vanilla retention also requires
    /// an unbroken <c>LineCheck</c> to the target. In Roots' terrain, line of sight
    /// breaks long before 8m does, so widening the radius alone changed almost nothing
    /// - the sight test was the real limit. Above 1.0 the patch therefore holds the
    /// existing target directly instead of re-running the scan, which is what "harder
    /// to shake off" has to mean once sight is the binding constraint.
    /// </summary>
    public static class BeetleDeaggro
    {
        /// <summary>
        /// The most forgiving setting. Not zero, at the maintainer's direction: a zero
        /// radius means a beetle can never hold a target at all, which reads as a
        /// disabled beetle rather than a tuned one (that's <c>disable-beetles</c>).
        /// </summary>
        public const double MinMultiplier = 0.1;

        /// <summary>The stickiest setting - roughly three times vanilla's reach.</summary>
        public const double MaxMultiplier = 3.0;

        /// <summary>
        /// How long a beetle ignores everyone after this dial has made it give up.
        /// Taken from the game's own <c>Mob.targetSwitchCooldown</c> (5s), the interval
        /// vanilla already imposes before a mob may change its mind about a target -
        /// so the pause reads as the beetle losing interest rather than as a mod-shaped
        /// hitch. Without it the mechanic cancels itself out entirely; see the class
        /// remarks.
        /// </summary>
        public const float SuppressionSeconds = 5f;

        /// <summary>
        /// Clamps a configured multiplier into <see cref="MinMultiplier"/>..
        /// <see cref="MaxMultiplier"/>, applied on every read so a hand-edited config
        /// can't produce a beetle that never aggros.
        /// </summary>
        public static double ClampMultiplier(double multiplier)
        {
            if (double.IsNaN(multiplier))
            {
                return 1.0;
            }

            return Math.Min(MaxMultiplier, Math.Max(MinMultiplier, multiplier));
        }

        /// <summary>
        /// How far a beetle will keep chasing a target it already has, given its own
        /// vanilla <c>aggroDistance</c> (5 world units on the stock prefab).
        /// </summary>
        public static float ResolveRetentionDistance(float vanillaAggroDistance, double multiplier)
        {
            return CreatureTuning.ScaleDeaggroDistance(vanillaAggroDistance, ClampMultiplier(multiplier));
        }

        /// <summary>
        /// Should this beetle hold on to the target it already has? Distance only -
        /// deliberately no line-of-sight term, because sight is what vanilla already
        /// tests and what made the high end of this dial inert.
        /// </summary>
        public static bool ShouldKeepTarget(float distanceToTarget, float vanillaAggroDistance, double multiplier)
        {
            return distanceToTarget <= ResolveRetentionDistance(vanillaAggroDistance, multiplier);
        }
    }
}
