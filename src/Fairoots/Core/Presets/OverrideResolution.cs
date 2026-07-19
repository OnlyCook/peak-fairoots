using System.Collections.Generic;

namespace Fairoots.Core.Presets
{
    /// <summary>
    /// Non-destructive preset resolution (ROADMAP.md "Presets" - "any per-mechanic
    /// setting the player has explicitly touched always overrides whatever the
    /// active preset would otherwise set"). Switching presets must never silently
    /// clobber a hand-tuned value.
    ///
    /// The mechanism is a sentinel default: each per-mechanic config entry defaults
    /// to a "follow preset" sentinel. If the live config value still equals the
    /// sentinel, the player never touched it, so the preset value applies; any other
    /// value means the player set it explicitly and it wins. This is deliberately a
    /// pure function of (preset value, configured value, sentinel) so it is
    /// unit-testable at the config-resolution level with no BepInEx/UI involved.
    /// </summary>
    public static class OverrideResolution
    {
        /// <summary>
        /// Sentinel for numeric per-mechanic settings. Negative so it can never
        /// collide with a legitimate value: every real Fairoots numeric knob
        /// (fractions, radii, force multipliers) is >= 0.
        /// </summary>
        public const double FollowPreset = -1.0;

        /// <summary>
        /// Resolve a numeric setting: the configured value wins unless it is still
        /// the <see cref="FollowPreset"/> sentinel, in which case the preset value
        /// applies.
        /// </summary>
        public static double Resolve(double presetValue, double configuredValue, double sentinel = FollowPreset)
        {
            return configuredValue == sentinel ? presetValue : configuredValue;
        }

        /// <summary>
        /// Generic resolution for non-numeric settings (enums, bools exposed as a
        /// nullable/tri-state, etc.): configured value wins unless it equals the
        /// given sentinel.
        /// </summary>
        public static T Resolve<T>(T presetValue, T configuredValue, T sentinel)
        {
            return EqualityComparer<T>.Default.Equals(configuredValue, sentinel)
                ? presetValue
                : configuredValue;
        }
    }
}
