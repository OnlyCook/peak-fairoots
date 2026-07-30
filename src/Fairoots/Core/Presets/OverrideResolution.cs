namespace Fairoots.Core.Presets
{
    /// <summary>
    /// Preset vs. Custom resolution (ROADMAP.md "Presets"). Presets 1-4 are fixed
    /// catalog numbers - a player's per-mechanic config entries are read but
    /// ignored while one of them is active, so there's no sentinel value to
    /// remember and no risk of a stray override silently bending a "vanilla"
    /// preset. <see cref="PresetId.Custom"/> (5) is the only preset where the
    /// player's own config values apply, and every value the player can type in
    /// (0 included) is used exactly as configured - no "unset" state to detect.
    /// </summary>
    public static class OverrideResolution
    {
        /// <summary>
        /// Resolve a setting of any type: <paramref name="configuredValue"/>
        /// applies only when <paramref name="useOverride"/> (i.e. the active
        /// preset is <see cref="PresetId.Custom"/>); otherwise the preset's own
        /// catalog value always wins, regardless of what the player has
        /// configured. Generic because presets drive on/off toggles and timing
        /// windows as well as multipliers - see <c>docs/PRESETS.md</c>.
        /// </summary>
        public static T Resolve<T>(T presetValue, T configuredValue, bool useOverride)
        {
            return useOverride ? configuredValue : presetValue;
        }
    }
}
