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
        /// Custom-preset value for the spore-bomb total removal target. Only takes
        /// effect when <see cref="Preset"/> is set to <see cref="PresetId.Custom"/>
        /// (5) - ignored under presets 1-4, which always use their own catalog
        /// numbers regardless of this value. Defaults to Balanced's number.
        /// </summary>
        public ConfigEntry<double> SporeBombCullFractionOverride { get; }

        /// <summary>
        /// Custom-preset value for the trigger-hitbox radius multiplier applied to
        /// every kept spore bomb (1.0 = vanilla size, lower = smaller/harder to set
        /// off accidentally, higher = larger than vanilla). Only takes effect under
        /// <see cref="PresetId.Custom"/>; see <see cref="SporeBombCullFractionOverride"/>.
        /// </summary>
        public ConfigEntry<double> SporeBombTriggerRadiusMultiplierOverride { get; }

        /// <summary>
        /// Custom-preset value for the knockback/explosion-force multiplier applied
        /// when a spore bomb detonates (1.0 = vanilla force). Only takes effect
        /// under <see cref="PresetId.Custom"/>; see
        /// <see cref="SporeBombCullFractionOverride"/>.
        /// </summary>
        public ConfigEntry<double> SporeBombKnockbackMultiplierOverride { get; }

        /// <summary>
        /// Custom-preset value for the screen-shake distance cap, in meters, on a
        /// spore-bomb detonation. 0 leaves the vanilla range uncapped; a positive
        /// value caps it. Only takes effect under <see cref="PresetId.Custom"/>;
        /// see <see cref="SporeBombCullFractionOverride"/>.
        /// </summary>
        public ConfigEntry<double> SporeBombScreenshakeRangeCapOverride { get; }

        /// <summary>
        /// Custom-preset value for the particle/VFX-orb-count multiplier applied to
        /// a spore-bomb detonation (1.0 = vanilla orb count). Only takes effect
        /// under <see cref="PresetId.Custom"/>; see
        /// <see cref="SporeBombCullFractionOverride"/>.
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
                "instead. Under presets 1-4, the per-mechanic settings below are ignored " +
                "entirely, even if you've changed them - switch to Custom to use them.");

            SporeBombCullFractionOverride = config.Bind(
                "Spore-Bombs",
                "cull-fraction",
                0.25,
                new ConfigDescription(
                    "Fraction of spore bombs to remove overall (foliage removal + seeded cull " +
                    "combined), e.g. 0.5 cuts them in half. Only takes effect when preset is " +
                    "set to Custom (5) - ignored under presets 1-4. 0 = remove none beyond " +
                    "the always-on foliage pass (vanilla-equivalent).",
                    new AcceptableValueRange<double>(0.0, 1.0)));

            SporeBombTriggerRadiusMultiplierOverride = config.Bind(
                "Spore-Bombs",
                "trigger-radius-multiplier",
                0.75,
                new ConfigDescription(
                    "Multiplier applied to every kept spore bomb's trigger hitbox, e.g. 0.7 " +
                    "shrinks it to 70% of vanilla size, 2.0 doubles it. 1.0 always means " +
                    "vanilla size, no matter what else you change - handy for A/B-testing " +
                    "against unmodified behavior. Only takes effect when preset is set to " +
                    "Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.0, 3.0)));

            SporeBombKnockbackMultiplierOverride = config.Bind(
                "Spore-Bombs",
                "knockback-multiplier",
                0.80,
                new ConfigDescription(
                    "Multiplier applied to a spore bomb's knockback/explosion force on " +
                    "detonation, e.g. 0.6 cuts it to 60% of vanilla, 2.0 doubles it. 1.0 " +
                    "always means vanilla force, no matter what else you change - handy for " +
                    "A/B-testing against unmodified behavior. Only takes effect when preset " +
                    "is set to Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.0, 5.0)));

            SporeBombScreenshakeRangeCapOverride = config.Bind(
                "Spore-Bombs",
                "screenshake-range-cap-meters",
                30.0,
                new ConfigDescription(
                    "Caps how far away (in meters) a spore-bomb detonation's screen-shake can " +
                    "still be felt. 0 leaves the vanilla range (~75m, uncapped) alone; a " +
                    "positive value caps it, e.g. 20 means no shake past 20m. Only takes " +
                    "effect when preset is set to Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.0, 100.0)));

            SporeBombVfxCountMultiplierOverride = config.Bind(
                "Spore-Bombs",
                "vfx-count-multiplier",
                0.75,
                new ConfigDescription(
                    "Multiplier applied to a spore bomb's particle/VFX orb count on " +
                    "detonation, e.g. 0.5 halves it, 2.0 doubles it. 1.0 always means vanilla " +
                    "count, no matter what else you change - handy for A/B-testing against " +
                    "unmodified behavior. Only takes effect when preset is set to Custom (5) " +
                    "- ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.0, 5.0)));

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

        /// <summary>True when the active preset is Custom - the only preset where the player's own config values apply.</summary>
        private bool UseCustomOverrides => Preset.Value == PresetId.Custom;

        /// <summary>
        /// The effective spore-bomb removal fraction: the player's Custom-preset
        /// value if Custom is active, otherwise the active preset's own catalog
        /// value regardless of what the player has configured.
        /// </summary>
        public double SporeBombCullFraction =>
            OverrideResolution.Resolve(
                PresetCatalog.SporeBombCullFraction(Preset.Value),
                SporeBombCullFractionOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective trigger-hitbox radius multiplier for kept spore bombs.</summary>
        public double SporeBombTriggerRadiusMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.SporeBombTriggerRadiusMultiplier(Preset.Value),
                SporeBombTriggerRadiusMultiplierOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective knockback/explosion-force multiplier for a spore-bomb detonation.</summary>
        public double SporeBombKnockbackMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.SporeBombKnockbackMultiplier(Preset.Value),
                SporeBombKnockbackMultiplierOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective screen-shake distance cap, in meters (0 = uncapped).</summary>
        public float SporeBombScreenshakeRangeCapMeters =>
            (float)OverrideResolution.Resolve(
                PresetCatalog.SporeBombScreenshakeRangeCapMeters(Preset.Value),
                SporeBombScreenshakeRangeCapOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective particle/VFX-orb-count multiplier for a spore-bomb detonation.</summary>
        public double SporeBombVfxCountMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.SporeBombVfxCountMultiplier(Preset.Value),
                SporeBombVfxCountMultiplierOverride.Value,
                UseCustomOverrides);
    }
}
