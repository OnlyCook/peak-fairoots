using BepInEx.Configuration;
using Fairoots.Core.Presets;
using UnityEngine;

namespace Fairoots
{
    /// <summary>
    /// Config binding for Fairoots. Holds the mod's defining knobs: the
    /// deterministic seed, the preset 1-4 selector, and per-mechanic override
    /// entries. Per-mechanic entries default to the "follow preset" sentinel
    /// (<see cref="OverrideResolution.FollowPreset"/>) so switching presets never
    /// clobbers a value the player explicitly changed (ROADMAP.md "Presets").
    ///
    /// Config keys are kebab-case per CLAUDE.md; the C# property names stay
    /// PascalCase. Section names follow the maintainer's other PEAK mods
    /// (Capitalized-Hyphenated, or a plain word where there's nothing to hyphenate).
    ///
    /// Resolved accessors (e.g. <see cref="SporeBombCullFraction"/>) fold preset +
    /// override together so callers never re-implement that logic.
    /// </summary>
    public class PluginConfig
    {
        // --- General -------------------------------------------------------
        public ConfigEntry<int> Seed { get; }

        // --- Presets -------------------------------------------------------
        public ConfigEntry<PresetId> Preset { get; }

        // --- Spore-Bombs ---------------------------------------------------
        /// <summary>
        /// Per-mechanic override for the spore-bomb total removal target. Defaults
        /// to the follow-preset sentinel; a value in [0, 1] overrides the preset.
        /// </summary>
        public ConfigEntry<double> SporeBombCullFractionOverride { get; }

        // --- Debug (kept last) --------------------------------------------
        /// <summary>Master switch for verbose diagnostic logging (the whole Debug harness is a no-op unless this is on).</summary>
        public ConfigEntry<bool> EnableDebugLogging { get; }

        /// <summary>Auto-dump a scene diagnostics report each time a level finishes generating.</summary>
        public ConfigEntry<bool> LogSceneScanOnLoad { get; }

        /// <summary>Hotkey to dump a scene diagnostics report on demand while playing.</summary>
        public ConfigEntry<KeyCode> SceneScanHotkey { get; }

        /// <summary>Hotkey to probe the nearest spore-bomb candidate for a foliage-detection method (Phase 4 research, not shipped functionality).</summary>
        public ConfigEntry<KeyCode> FoliageProbeHotkey { get; }

        public PluginConfig(ConfigFile config)
        {
            Seed = config.Bind(
                "General",
                "seed",
                0,
                "Deterministic seed for every random decision Fairoots makes (which spore " +
                "bombs get culled, etc.). Same seed + same Roots level = identical result, " +
                "every load. Change it to reroll; share it with your lobby so everyone sees " +
                "the same layout (all clients must run the mod with this same seed).");

            Preset = config.Bind(
                "Presets",
                "preset",
                PresetId.Balanced,
                "Overall balance preset. Subtle (1) is the lightest touch, Tame (4) the " +
                "heaviest. Balanced (2) is the default. Any per-mechanic setting you change " +
                "yourself overrides the preset for that mechanic and is never overwritten " +
                "when you switch presets.");

            SporeBombCullFractionOverride = config.Bind(
                "Spore-Bombs",
                "cull-fraction",
                OverrideResolution.FollowPreset,
                new ConfigDescription(
                    "Fraction of spore bombs to remove overall (foliage removal + seeded cull " +
                    "combined), e.g. 0.5 cuts them in half. Leave at -1 to follow the active " +
                    "preset; set 0-1 to override it.",
                    new AcceptableValueRange<double>(OverrideResolution.FollowPreset, 1.0)));

            // --- Debug section: bound last so it sorts to the bottom of the
            // config file. Everything here is diagnostic-only and off by default.
            EnableDebugLogging = config.Bind(
                "Debug",
                "enable-debug-logging",
                false,
                "Master switch for Fairoots' verbose diagnostic logging. When on, the mod " +
                "reports what it can and can't find in a loaded Roots level (spore bombs, " +
                "wind zone, spore areas, zombies) so you can see what's working. Leave off " +
                "for normal play - it's noisy and only useful for development/bug reports.");

            LogSceneScanOnLoad = config.Bind(
                "Debug",
                "log-scene-scan-on-load",
                true,
                "When debug logging is on, automatically dump a scene diagnostics report " +
                "each time a level finishes generating. Turn this off if you only want to " +
                "trigger the report manually with the hotkey below.");

            SceneScanHotkey = config.Bind(
                "Debug",
                "scene-scan-hotkey",
                KeyCode.F9,
                "When debug logging is on, press this key in-game to dump a scene " +
                "diagnostics report on demand (e.g. right when you're standing next to a " +
                "spore bomb). Set to None to disable the hotkey.");

            FoliageProbeHotkey = config.Bind(
                "Debug",
                "foliage-probe-hotkey",
                KeyCode.F10,
                "When debug logging is on, press this key while standing next to a spore " +
                "bomb visibly camouflaged in bush/grass to probe it for a foliage-detection " +
                "method (grass-blade density, nearby colliders/renderers). Research tool for " +
                "Phase 4 (ROADMAP.md); not part of the shipped cull feature. Set to None to " +
                "disable the hotkey.");
        }

        /// <summary>
        /// The effective spore-bomb removal fraction: the player's override if set,
        /// otherwise the active preset's value.
        /// </summary>
        public double SporeBombCullFraction =>
            OverrideResolution.Resolve(
                PresetCatalog.SporeBombCullFraction(Preset.Value),
                SporeBombCullFractionOverride.Value);
    }
}
