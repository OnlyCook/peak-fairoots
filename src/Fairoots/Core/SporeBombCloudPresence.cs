using System;

namespace Fairoots.Core
{
    /// <summary>
    /// "Is the player standing somewhere a spore bomb's cloud would actually apply
    /// spores?" - the geometry behind the persistent spore-bomb overlay
    /// (<c>General/show-overlay-in-spore-bomb-clouds</c>, see
    /// <c>SporeBombs/SporeBombCloudWarning</c>).
    ///
    /// <b>This deliberately mirrors the native <c>AOE.Explode</c> rule rather than
    /// approximating it with a plain distance check.</b> An <c>AOE</c> does not
    /// affect everything inside its <c>range</c>: it computes a falloff factor
    /// <c>(1 - distance / range)^factorPow</c> and skips anything whose factor is
    /// below <c>minFactor</c>, so the radius that actually applies status is
    /// meaningfully smaller than the radius the field advertises. An overlay driven
    /// by the advertised radius would light up in a ring where nothing can hurt you -
    /// which is worse than no overlay, because this setting exists precisely so the
    /// player can trust the overlay to mean "you are being spored right now".
    ///
    /// Pure and Unity-free so the rule that has to agree with the game's own can be
    /// tested directly, rather than only by standing in a cloud and squinting.
    /// </summary>
    public static class SporeBombCloudPresence
    {
        /// <summary>
        /// Whether a character at <paramref name="distance"/> from the cloud's centre
        /// is inside the part of it that applies status, given the AOE's own
        /// <paramref name="range"/>, <paramref name="minFactor"/> and
        /// <paramref name="factorPow"/>.
        /// </summary>
        public static bool IsInsideStatusRange(double distance, double range, double minFactor, double factorPow)
        {
            // A zero-range AOE is inert - the native Explode() early-returns on it
            // outright - and a negative distance is nonsense rather than "very close".
            if (range <= 0.0 || distance < 0.0 || distance > range)
            {
                return false;
            }

            return Factor(distance, range, factorPow) >= minFactor;
        }

        /// <summary>
        /// The native falloff factor. Split out so a caller can log how strongly the
        /// player is currently being affected, not just whether they are.
        /// </summary>
        public static double Factor(double distance, double range, double factorPow)
        {
            if (range <= 0.0)
            {
                return 0.0;
            }

            double linear = 1.0 - distance / range;
            if (linear <= 0.0)
            {
                return 0.0;
            }

            // Guarded rather than passed straight to Pow: a zero or negative exponent
            // would make every distance score 1 (or worse, infinity at the boundary),
            // turning the check into "anywhere in range" without saying so.
            return factorPow <= 0.0 ? linear : Math.Pow(linear, factorPow);
        }
    }
}
