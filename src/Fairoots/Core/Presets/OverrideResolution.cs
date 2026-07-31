using System.Collections.Generic;

namespace Fairoots.Core.Presets
{
    /// <summary>
    /// Preset vs. Custom resolution (ROADMAP.md "Presets"). Presets 1-4 are fixed
    /// catalog numbers - a player's per-mechanic config entries are read but
    /// ignored while one of them is active and <see cref="PluginConfig.ApplyPurePreset"/>
    /// is on, so there's no sentinel value to remember and no risk of a stray
    /// override silently bending a "vanilla" preset. <see cref="PresetId.Custom"/>
    /// (5) is the only preset where the player's own config values always apply,
    /// and every value the player can type in (0 included) is used exactly as
    /// configured - no "unset" state to detect.
    ///
    /// When a non-Custom preset is active and apply-pure-preset is turned OFF,
    /// resolution becomes per-setting: a setting the player left at its vanilla
    /// default still takes the preset's number, but a setting the player has
    /// actually changed keeps the player's value instead of being overwritten by
    /// the preset. This lets a player pick, say, Subtle and tweak a single
    /// setting without first having to copy every one of Subtle's numbers into
    /// their own Custom overrides.
    /// </summary>
    public static class OverrideResolution
    {
        /// <summary>
        /// Resolve a setting of any type.
        ///
        /// Under <see cref="PresetId.Custom"/>, <paramref name="configuredValue"/>
        /// always wins. Otherwise, if <paramref name="applyPurePreset"/> is true,
        /// the preset's own catalog value always wins, regardless of what the
        /// player has configured (the original, all-or-nothing behavior). If
        /// <paramref name="applyPurePreset"/> is false, the player's configured
        /// value wins only when it differs from <paramref name="defaultValue"/>
        /// (the setting's vanilla default) - an untouched setting still takes the
        /// preset's value. Generic because presets drive on/off toggles and timing
        /// windows as well as multipliers - see <c>docs/PRESETS.md</c>.
        /// </summary>
        public static T Resolve<T>(
            T presetValue,
            T configuredValue,
            T defaultValue,
            PresetId preset,
            bool applyPurePreset)
        {
            if (preset == PresetId.Custom)
            {
                return configuredValue;
            }

            if (applyPurePreset)
            {
                return presetValue;
            }

            return EqualityComparer<T>.Default.Equals(configuredValue, defaultValue)
                ? presetValue
                : configuredValue;
        }
    }
}
