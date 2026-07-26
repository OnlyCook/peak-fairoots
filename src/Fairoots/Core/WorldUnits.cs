namespace Fairoots.Core
{
    /// <summary>
    /// Conversion between PEAK's world-space units and the meters the game (and
    /// every Fairoots setting) talks in. **They are not the same thing:** the game
    /// keeps a static `CharacterStats.unitsToMeters` (1.6 in the current build) and
    /// multiplies by it everywhere it shows a player a distance or a height - the
    /// altitude readout, the throw-distance stat, the climb checks. One world unit
    /// is 1.6 meters.
    ///
    /// So any Fairoots setting named `*-meters` has to be divided by that factor
    /// before it's compared against, or written into, anything positional
    /// (`Vector3.Distance`, a transform's `y`, `AddScreenshake.range`), and any raw
    /// distance has to be multiplied by it before it's *logged* as meters.
    /// Forgetting this is not a subtle rounding difference - it's a 60% error, and
    /// it's exactly what made a "75m" screen-shake cap actually reach 120m.
    ///
    /// Pure arithmetic, no Unity dependency (see CODEBASE.md's Core split rule): the
    /// live factor is read off the game by <c>GameUnits</c> and passed in.
    /// </summary>
    public static class WorldUnits
    {
        /// <summary>
        /// The value of <c>CharacterStats.unitsToMeters</c> in the current PEAK
        /// build, used as the fallback when the live static can't be read (before
        /// the game's statics are initialised, or in tests).
        /// </summary>
        public const float DefaultUnitsToMeters = 1.6f;

        /// <summary>
        /// A sane <paramref name="unitsToMeters"/>: guards against a zero/negative
        /// factor turning a conversion into a divide-by-zero or a sign flip.
        /// </summary>
        public static float SafeFactor(float unitsToMeters)
        {
            return unitsToMeters > 0f ? unitsToMeters : DefaultUnitsToMeters;
        }

        /// <summary>Meters → world units (what positional game fields want).</summary>
        public static float MetersToUnits(float meters, float unitsToMeters)
        {
            return meters / SafeFactor(unitsToMeters);
        }

        /// <summary>World units → meters (what a player-facing number or log line wants).</summary>
        public static float UnitsToMeters(float units, float unitsToMeters)
        {
            return units * SafeFactor(unitsToMeters);
        }
    }
}
