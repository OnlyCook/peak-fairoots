using Fairoots.Core;

namespace Fairoots
{
    /// <summary>
    /// Game-facing half of <see cref="WorldUnits"/>: reads the live
    /// <c>CharacterStats.unitsToMeters</c> off the game and applies it. Every
    /// meters↔units conversion in the mod goes through here so there's exactly one
    /// place that knows where the factor comes from.
    ///
    /// (The maintainer's Sense of Direction mod does the same thing inline as
    /// <c>* CharacterStats.unitsToMeters</c>; this wraps it because Fairoots
    /// converts in both directions and mostly the *meters → units* way, which is
    /// the easier one to get backwards.)
    /// </summary>
    internal static class GameUnits
    {
        /// <summary>Meters per world unit, as the running game defines it.</summary>
        internal static float UnitsToMetersFactor => WorldUnits.SafeFactor(CharacterStats.unitsToMeters);

        /// <summary>A meters-denominated setting, converted to the world units game fields use.</summary>
        internal static float MetersToUnits(float meters) => WorldUnits.MetersToUnits(meters, CharacterStats.unitsToMeters);

        /// <summary>A raw world-space distance/height, converted to meters for display.</summary>
        internal static float ToMeters(float units) => WorldUnits.UnitsToMeters(units, CharacterStats.unitsToMeters);
    }
}
