using System;

namespace Fairoots.Core
{
    /// <summary>
    /// The zombie deaggro rule - <b>genuinely new logic, not a field tweak</b>, and the
    /// only mechanic in Phase 7 that is (ROADMAP.md called this out in advance).
    ///
    /// <b>Vanilla zombies never lose a target, at any distance.</b> Decompile-confirmed
    /// and not merely hard to find: <c>MushroomZombie.TargetIsValid</c> checks only
    /// <c>isBot</c>, <c>data.dead</c> and <c>data.fullyPassedOut</c> - there is no
    /// distance term, no line-of-sight term and no timer anywhere in the chase state.
    /// Once a zombie has locked on it chases forever.
    ///
    /// That makes this the one dial in the whole mod where <b>1.0 cannot mean
    /// "vanilla"</b>, because vanilla is "never deaggro" and no finite multiple of a
    /// threshold reproduces it. So the scale is redefined, at the maintainer's
    /// direction: <see cref="MaxMultiplier"/> (1.0) is the <em>toughest</em> setting -
    /// the longest sight-loss timer and the largest distance - and
    /// <see cref="MinMultiplier"/> (0.1) is the most forgiving. Zero is excluded
    /// outright: it would mean "deaggro instantly and never aggro at all", which is
    /// what the <c>disable-zombies</c> switch is for.
    ///
    /// <b>Two independent escape routes, either of which deaggros</b> (see
    /// <see cref="ShouldDeaggroForSightLoss"/> / <see cref="ShouldDeaggroForDistance"/>):
    /// stay out of the zombie's line of sight long enough, or simply get far enough
    /// away. Both thresholds move together on the one multiplier.
    ///
    /// <b>The base sight-loss threshold is the game's own number, not an invented
    /// one.</b> PEAK's other stalking creature, the <c>Scoutmaster</c>, decides it has
    /// lost track of a player with <c>sinceSeenTarget &gt; 30f</c> - so 30 seconds is
    /// what this game already considers "they got away from a determined pursuer".
    /// Anchoring to it means the toughest setting is demonstrably in keeping with how
    /// PEAK itself tunes a stalker, and it satisfies the design constraint that
    /// breaking line of sight for a few seconds must not be enough: at 1.0 a player
    /// has to stay unseen for a full half-minute.
    /// </summary>
    public static class ZombieDeaggro
    {
        /// <summary>
        /// The most forgiving setting. Not zero: a zero-length threshold would mean a
        /// zombie deaggros the instant it blinks and can never hold a target at all,
        /// which is a disabled zombie rather than a tuned one.
        /// </summary>
        public const double MinMultiplier = 0.1;

        /// <summary>
        /// The toughest setting, and the top of the range. Unlike every other dial in
        /// this mod, this is <em>not</em> "vanilla" - see the class remarks.
        /// </summary>
        public const double MaxMultiplier = 1.0;

        /// <summary>
        /// Seconds a player must stay out of a zombie's line of sight at
        /// <see cref="MaxMultiplier"/>. Taken from <c>Scoutmaster</c>'s own
        /// <c>sinceSeenTarget &gt; 30f</c> lost-track rule - see the class remarks for
        /// why the game's own constant is used rather than a fresh guess.
        /// </summary>
        public const float BaseSightLossSeconds = 30f;

        /// <summary>
        /// How far a player must get from a zombie at <see cref="MaxMultiplier"/>, in
        /// <b>world units</b> (PEAK's world units are not meters - 1 unit = 1.6m, so
        /// this is ~120m; see <see cref="WorldUnits"/>). Chosen to sit far above the
        /// zombie's own vanilla awareness distances (<c>distanceBeforeWakeup</c> and
        /// <c>distanceBeforeChase</c> are both 30 units), so that at the toughest
        /// setting outrunning a zombie means genuinely leaving the area rather than
        /// jogging past its notice radius.
        /// </summary>
        public const float BaseDistanceWorldUnits = 75f;

        /// <summary>
        /// Clamps a configured multiplier into <see cref="MinMultiplier"/>..
        /// <see cref="MaxMultiplier"/>. Applied on every read rather than trusted from
        /// config, so a hand-edited config file can't produce a zombie that deaggros
        /// instantly (0) or never (a huge value), both of which would silently defeat
        /// the mechanic.
        /// </summary>
        public static double ClampMultiplier(double multiplier)
        {
            if (double.IsNaN(multiplier))
            {
                return MaxMultiplier;
            }

            return Math.Min(MaxMultiplier, Math.Max(MinMultiplier, multiplier));
        }

        /// <summary>How long the player must stay unseen, at this multiplier.</summary>
        public static float ResolveSightLossSeconds(double multiplier)
        {
            return (float)(BaseSightLossSeconds * ClampMultiplier(multiplier));
        }

        /// <summary>How far the player must get away, at this multiplier, in world units.</summary>
        public static float ResolveDistanceWorldUnits(double multiplier)
        {
            return (float)(BaseDistanceWorldUnits * ClampMultiplier(multiplier));
        }

        /// <summary>
        /// Has the player been out of this zombie's sight long enough to have lost it?
        /// Strictly greater-than, so the threshold is a duration the player must
        /// actually exceed.
        /// </summary>
        public static bool ShouldDeaggroForSightLoss(float secondsOutOfSight, double multiplier)
        {
            return secondsOutOfSight > ResolveSightLossSeconds(multiplier);
        }

        /// <summary>
        /// Is the player far enough away that the zombie gives up? Independent of
        /// sight: this is the "I outran it across the map" escape, and it applies even
        /// while the zombie can see the player.
        /// </summary>
        public static bool ShouldDeaggroForDistance(float distanceWorldUnits, double multiplier)
        {
            return distanceWorldUnits > ResolveDistanceWorldUnits(multiplier);
        }
    }
}
