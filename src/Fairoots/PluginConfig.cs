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

        /// <summary>
        /// Per-mechanic override for the trigger-hitbox radius multiplier applied
        /// to every kept spore bomb (1.0 = vanilla size, lower = smaller/harder to
        /// set off accidentally). Defaults to the follow-preset sentinel.
        /// </summary>
        public ConfigEntry<double> SporeBombTriggerRadiusMultiplierOverride { get; }

        /// <summary>
        /// Per-mechanic override for the knockback/explosion-force multiplier
        /// applied when a spore bomb detonates (1.0 = vanilla force). Defaults to
        /// the follow-preset sentinel.
        /// </summary>
        public ConfigEntry<double> SporeBombKnockbackMultiplierOverride { get; }

        /// <summary>
        /// Per-mechanic override for the screen-shake distance cap, in meters, on
        /// a spore-bomb detonation. 0 leaves the vanilla range uncapped; a positive
        /// value caps it. Defaults to the follow-preset sentinel.
        /// </summary>
        public ConfigEntry<double> SporeBombScreenshakeRangeCapOverride { get; }

        /// <summary>
        /// Per-mechanic override for the particle/VFX-orb-count multiplier applied
        /// to a spore-bomb detonation (1.0 = vanilla orb count). Defaults to the
        /// follow-preset sentinel.
        /// </summary>
        public ConfigEntry<double> SporeBombVfxCountMultiplierOverride { get; }

        /// <summary>
        /// Max height, in meters above a spore bomb's base, a player can be at and
        /// still set it off. Vanilla's trigger sphere reaches absurdly far above
        /// the actual mushroom mesh for the "Spore Bomb"/"Poison Spore Bomb"
        /// variants (confirmed by the maintainer via the trigger-radius wireframe
        /// overlay - a genuinely oversized hitbox, not a misreading), so a player
        /// jumping over a short, wide mushroom clump still sets it off mid-air.
        /// This isn't a preset-scaled balance dial - it's a bug fix for a vanilla
        /// hitbox mistake, so it's a flat value, not folded through the
        /// preset/override system. 0 disables the cutoff entirely (vanilla
        /// behavior). Does not apply to the "Explosive Spore Bomb" variant, which
        /// is genuinely round.
        /// </summary>
        public ConfigEntry<float> SporeBombMaxTriggerHeightMeters { get; }

        // --- Debug (kept last) --------------------------------------------
        /// <summary>Master switch for verbose diagnostic logging (the whole Debug harness is a no-op unless this is on).</summary>
        public ConfigEntry<bool> EnableDebugLogging { get; }

        /// <summary>Auto-dump a scene diagnostics report each time a level finishes generating.</summary>
        public ConfigEntry<bool> LogSceneScanOnLoad { get; }

        /// <summary>Hotkey to dump a scene diagnostics report on demand while playing.</summary>
        public ConfigEntry<KeyCode> SceneScanHotkey { get; }

        /// <summary>Hotkey to probe the nearest spore-bomb candidate for a foliage-detection method (Phase 4 research, not shipped functionality).</summary>
        public ConfigEntry<KeyCode> FoliageProbeHotkey { get; }

        /// <summary>Draw a 2D screen-space label over every spore bomb removed this level load, tagged by why (foliage/seeded). Off by default.</summary>
        public ConfigEntry<bool> ShowRemovedSporeBombMarkers { get; }

        /// <summary>Draw a 3D wireframe (red) over every nearby kept spore bomb's current trigger hitbox. Off by default.</summary>
        public ConfigEntry<bool> ShowSporeBombTriggerRadius { get; }

        /// <summary>
        /// For before/after comparison screenshots: when on, spore-bomb trigger
        /// hitboxes are left at vanilla size instead of being shrunk, and the
        /// trigger-height cutoff (<see cref="SporeBombMaxTriggerHeightMeters"/>,
        /// see <see cref="SporeBombHeightGatePatch"/>) is bypassed too, so a
        /// "vanilla" comparison is genuinely vanilla in every respect, not just
        /// radius. This is a gameplay override (not a diagnostic), so unlike the
        /// rest of this section it takes effect regardless of
        /// <see cref="EnableDebugLogging"/>. Off by default.
        /// </summary>
        public ConfigEntry<bool> KeepVanillaTriggerRadius { get; }

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
                "heaviest, Balanced (2) is the default. Custom (5) ignores the hard-coded " +
                "preset numbers entirely and uses your own Spore-Bombs settings directly " +
                "instead. Under presets 1-4, any per-mechanic setting you change yourself " +
                "still overrides the preset for that mechanic and is never overwritten when " +
                "you switch presets.");

            SporeBombCullFractionOverride = config.Bind(
                "Spore-Bombs",
                "cull-fraction",
                OverrideResolution.FollowPreset,
                new ConfigDescription(
                    "Fraction of spore bombs to remove overall (foliage removal + seeded cull " +
                    "combined), e.g. 0.5 cuts them in half. Leave at -1 to follow the active " +
                    "preset; set 0-1 to override it.",
                    new AcceptableValueRange<double>(OverrideResolution.FollowPreset, 1.0)));

            SporeBombTriggerRadiusMultiplierOverride = config.Bind(
                "Spore-Bombs",
                "trigger-radius-multiplier",
                OverrideResolution.FollowPreset,
                new ConfigDescription(
                    "Multiplier applied to every kept spore bomb's trigger hitbox, e.g. 0.7 " +
                    "shrinks it to 70% of vanilla size. Leave at -1 to follow the active " +
                    "preset; set 0-1 to override it (1.0 = vanilla size).",
                    new AcceptableValueRange<double>(OverrideResolution.FollowPreset, 1.0)));

            SporeBombKnockbackMultiplierOverride = config.Bind(
                "Spore-Bombs",
                "knockback-multiplier",
                OverrideResolution.FollowPreset,
                new ConfigDescription(
                    "Multiplier applied to a spore bomb's knockback/explosion force on " +
                    "detonation, e.g. 0.6 cuts it to 60% of vanilla. Leave at -1 to follow the " +
                    "active preset; set 0-1 to override it (1.0 = vanilla force).",
                    new AcceptableValueRange<double>(OverrideResolution.FollowPreset, 1.0)));

            SporeBombScreenshakeRangeCapOverride = config.Bind(
                "Spore-Bombs",
                "screenshake-range-cap-meters",
                OverrideResolution.FollowPreset,
                new ConfigDescription(
                    "Caps how far away (in meters) a spore-bomb detonation's screen-shake can " +
                    "still be felt. 0 leaves the vanilla range (~75m, uncapped) alone; a " +
                    "positive value caps it, e.g. 20 means no shake past 20m. Leave at -1 to " +
                    "follow the active preset.",
                    new AcceptableValueRange<double>(OverrideResolution.FollowPreset, 100.0)));

            SporeBombVfxCountMultiplierOverride = config.Bind(
                "Spore-Bombs",
                "vfx-count-multiplier",
                OverrideResolution.FollowPreset,
                new ConfigDescription(
                    "Multiplier applied to a spore bomb's particle/VFX orb count on " +
                    "detonation, e.g. 0.5 halves it. Leave at -1 to follow the active preset; " +
                    "set 0-1 to override it (1.0 = vanilla count).",
                    new AcceptableValueRange<double>(OverrideResolution.FollowPreset, 1.0)));

            SporeBombMaxTriggerHeightMeters = config.Bind(
                "Spore-Bombs",
                "max-trigger-height-meters",
                1.75f,
                new ConfigDescription(
                    "Max height, in meters above its base, a player can be at and still set " +
                    "off a \"Spore Bomb\" or \"Poison Spore Bomb\" (not the round \"Explosive " +
                    "Spore Bomb\", which is unaffected) - fixes vanilla's oversized trigger " +
                    "sphere reaching absurdly far above the actual mushroom mesh, which made " +
                    "it impossible to jump over one without setting it off. 0 disables the " +
                    "cutoff (vanilla behavior).",
                    new AcceptableValueRange<float>(0f, 10f)));

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

            ShowRemovedSporeBombMarkers = config.Bind(
                "Debug",
                "show-removed-spore-bomb-markers",
                false,
                "When debug logging is on, draw a 2D on-screen label over every spot a spore " +
                "bomb was removed this level load, tagged by why (foliage vs. seeded cull) - " +
                "useful for eyeballing what the cull actually touched. Off by default.");

            ShowSporeBombTriggerRadius = config.Bind(
                "Debug",
                "show-spore-bomb-trigger-radius",
                false,
                "When debug logging is on, draw a red 3D wireframe around every kept spore " +
                "bomb's current trigger hitbox (matching its exact shape/size after the " +
                "configured radius multiplier is applied) for spore bombs within 10m - useful " +
                "for eyeballing how much the trigger box was actually shrunk against the real " +
                "prefab. Off by default.");

            KeepVanillaTriggerRadius = config.Bind(
                "Debug",
                "keep-vanilla-trigger-radius",
                false,
                "For before/after comparison screenshots: when on, spore-bomb trigger " +
                "hitboxes are left at their original (vanilla) size instead of being shrunk " +
                "by the configured trigger-radius multiplier, and the trigger-height cutoff " +
                "(max-trigger-height-meters) is bypassed too, so jumping over one behaves like " +
                "vanilla again. This is a gameplay override, so it takes effect regardless of " +
                "the debug-logging master switch above. Off by default.");
        }

        /// <summary>
        /// The effective spore-bomb removal fraction: the player's override if set,
        /// otherwise the active preset's value (or, under <see cref="PresetId.Custom"/>,
        /// the Balanced fallback if the player hasn't set this one yet - see
        /// <see cref="PresetCatalog"/>'s remarks).
        /// </summary>
        public double SporeBombCullFraction =>
            OverrideResolution.Resolve(
                PresetCatalog.SporeBombCullFraction(Preset.Value),
                SporeBombCullFractionOverride.Value);

        /// <summary>The effective trigger-hitbox radius multiplier for kept spore bombs.</summary>
        public double SporeBombTriggerRadiusMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.SporeBombTriggerRadiusMultiplier(Preset.Value),
                SporeBombTriggerRadiusMultiplierOverride.Value);

        /// <summary>The effective knockback/explosion-force multiplier for a spore-bomb detonation.</summary>
        public double SporeBombKnockbackMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.SporeBombKnockbackMultiplier(Preset.Value),
                SporeBombKnockbackMultiplierOverride.Value);

        /// <summary>The effective screen-shake distance cap, in meters (0 = uncapped).</summary>
        public float SporeBombScreenshakeRangeCapMeters =>
            (float)OverrideResolution.Resolve(
                PresetCatalog.SporeBombScreenshakeRangeCapMeters(Preset.Value),
                SporeBombScreenshakeRangeCapOverride.Value);

        /// <summary>The effective particle/VFX-orb-count multiplier for a spore-bomb detonation.</summary>
        public double SporeBombVfxCountMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.SporeBombVfxCountMultiplier(Preset.Value),
                SporeBombVfxCountMultiplierOverride.Value);
    }
}
