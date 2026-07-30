using System;

namespace Fairoots.Core
{
    /// <summary>
    /// Pure arithmetic for the two <c>Spores</c>-section dials, which act on the
    /// <b>Spores status itself</b> rather than on any one hazard that applies it:
    /// how long a full spore meter takes to clear, and how much of every incoming
    /// spore application actually lands. Not seed-gated - every player and every
    /// spore source gets the identical flat treatment, so there is no per-instance
    /// decision here, just scaling (same shape as <see cref="SporeAreaTuning"/> and
    /// <see cref="SporeBombExplosionTuning"/>).
    ///
    /// <b>How this relates to the per-hazard rate dials.</b>
    /// <see cref="SporeAreaTuning.ScaleStatusRate"/> scales one hazard's emitter
    /// (<c>StatusEmitter.amount</c>) and reaches nothing else;
    /// <see cref="ScaleBuildUp"/> scales the status application at the point every
    /// source funnels through (<c>CharacterAfflictions.AddStatus</c>), so it also
    /// covers a spore bomb's cloud, a zombie bite and its lingering
    /// <c>Affliction_ZombieBite</c>, and anything else the game ever decides to
    /// apply spores from. The two therefore <b>compound</b> on a spore area
    /// (0.5 x 0.5 = a quarter rate), which is intended: the area dial is "this
    /// hazard is weaker", this one is "spores are weaker everywhere." Config
    /// descriptions say so explicitly, and the presets deliberately leave this dial
    /// at 1.0 so the shipped presets can't double-dip (see
    /// <c>PresetCatalog.SporeBuildUpMultiplier</c>).
    /// </summary>
    public static class SporeStatusTuning
    {
        /// <summary>
        /// Floor for the clear-time multiplier. A multiplier of 0 would mean "spores
        /// clear in no time at all", which as arithmetic is a division by zero and
        /// as gameplay is indistinguishable from a very small value - the meter
        /// drains in chunks of <c>0.025</c> on the game's own frame timing either
        /// way. 0.05 is 20x vanilla drain speed, well past the point where the
        /// difference is visible.
        /// </summary>
        public const double MinClearTimeMultiplier = 0.05;

        /// <summary>Whether a multiplier means "leave this exactly as the game shipped it".</summary>
        public static bool IsVanilla(double multiplier)
        {
            return Math.Abs(multiplier - 1.0) < 1e-6;
        }

        /// <summary>
        /// Scale the natural Spores drain rate
        /// (<c>CharacterAfflictions.sporesReductionPerSecond</c>) so that the
        /// configured multiplier reads as a multiplier on <b>time</b>, not on rate.
        /// The setting is "how long spores take to go away": 0.5 must mean half as
        /// long, so the per-second drain has to go <em>up</em> - hence a division,
        /// not a multiplication. Getting this backwards is the obvious bug here, so
        /// it has a dedicated test.
        ///
        /// <b>Returns the vanilla value untouched when vanilla is 0 or less.</b>
        /// <c>sporesReductionPerSecond</c> is a serialized prefab field, so its real
        /// value isn't knowable from the decompiled C#; if a build ever shipped it at
        /// 0 (i.e. spores never drain on their own), no multiplier should be able to
        /// invent a drain rate out of nothing - a "clear faster" dial silently
        /// becoming a "spores now clear at all" dial would be a bigger change than
        /// the setting promises. The applying patch logs the live baseline so the
        /// real number is visible in a debug log rather than assumed.
        /// </summary>
        public static float ScaleDecayRate(float vanillaPerSecond, double clearTimeMultiplier)
        {
            if (vanillaPerSecond <= 0f)
            {
                return vanillaPerSecond;
            }

            double multiplier = ClampClearTime(clearTimeMultiplier);
            return (float)(vanillaPerSecond / multiplier);
        }

        /// <summary>
        /// Scale the delay before the natural drain starts
        /// (<c>CharacterAfflictions.sporesReductionCooldown</c> - the game only
        /// drains spores once this many seconds have passed since the last spore
        /// application) by the same multiplier, in the same direction as the time it
        /// represents.
        ///
        /// <b>Why the cooldown and not just the rate.</b> The wall-clock time to
        /// clear a meter is <c>cooldown + status / rate</c>. Scaling only the rate
        /// would leave a fixed dead delay in front of it, so "half as long" wouldn't
        /// actually be half - and at the small end the dial would asymptote at the
        /// cooldown instead of approaching zero. Scaling the cooldown by
        /// <c>m</c> and the rate by <c>1/m</c> scales the whole expression by exactly
        /// <c>m</c>, which is what the setting claims to do.
        /// </summary>
        public static float ScaleDecayCooldown(float vanillaCooldown, double clearTimeMultiplier)
        {
            if (vanillaCooldown <= 0f)
            {
                return vanillaCooldown;
            }

            float scaled = (float)(vanillaCooldown * ClampClearTime(clearTimeMultiplier));
            return scaled < 0f ? 0f : scaled;
        }

        /// <summary>
        /// Total wall-clock seconds to clear <paramref name="status"/> worth of
        /// Spores under a given multiplier, assuming nothing re-applies spores in the
        /// meantime: <c>cooldown + status / rate</c>. Not used by gameplay code -
        /// it's what lets the tests assert the "0.5 means half as long" promise
        /// end-to-end across both fields at once, instead of checking each scaling
        /// direction in isolation and hoping they compose. Also what the applying
        /// patch logs, so a debug log states the real vanilla clear time in seconds
        /// rather than two raw field values the reader has to combine themselves.
        /// </summary>
        public static double SecondsToClear(float vanillaCooldown, float vanillaPerSecond, float status, double clearTimeMultiplier)
        {
            float rate = ScaleDecayRate(vanillaPerSecond, clearTimeMultiplier);
            if (rate <= 0f)
            {
                return double.PositiveInfinity;
            }

            return ScaleDecayCooldown(vanillaCooldown, clearTimeMultiplier) + (double)status / rate;
        }

        /// <summary>
        /// Scale one incoming Spores application. A multiplier of 0.5 on a
        /// <c>0.1</c> application yields <c>0.05</c> - the maintainer's literal ask
        /// ("if the player gets spores - lets say 10 - but the multiplier is set to
        /// 0.5 they should only get half of it").
        ///
        /// <b>Nothing is scaled unless it is a positive amount.</b> The native
        /// <c>AddStatus</c> is reached with a non-positive amount by several paths
        /// (<c>AdjustStatus</c> routes negatives to <c>SubtractStatus</c>, but
        /// <c>AddStatus</c> is also called directly, and its Hot/Cold cancellation
        /// branch subtracts from the amount mid-method), and scaling a subtraction by
        /// a "less spores please" dial would make the dial <em>add</em> spores on
        /// those paths. Clamped at 0 rather than allowed to go negative for the same
        /// reason: a negative amount flips the native code into a branch that removes
        /// status, so an out-of-range hand-edited config would turn spore clouds into
        /// a cure.
        ///
        /// <b>No precision is lost at small multipliers.</b> The native code
        /// accumulates every application into <c>currentIncrementalStatuses</c> and
        /// only moves the visible meter once that accumulator reaches <c>0.025</c>,
        /// so halving each application halves the rate the meter fills at rather than
        /// rounding small applications away to nothing.
        /// </summary>
        public static float ScaleBuildUp(float amount, double multiplier)
        {
            if (amount <= 0f)
            {
                return amount;
            }

            float scaled = (float)(amount * multiplier);
            return scaled < 0f ? 0f : scaled;
        }

        private static double ClampClearTime(double multiplier)
        {
            return multiplier < MinClearTimeMultiplier ? MinClearTimeMultiplier : multiplier;
        }
    }
}
