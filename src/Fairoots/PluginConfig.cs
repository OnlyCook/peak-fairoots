using BepInEx.Configuration;
using Fairoots.Core.Presets;
using Fairoots.Networking;
using UnityEngine;

namespace Fairoots
{
    /// <summary>
    /// Config binding for Fairoots. Holds the mod's defining knobs: the
    /// deterministic seed, the preset 1-4 selector, and per-mechanic Custom-only
    /// entries (only read when <see cref="Preset"/> is <see cref="PresetId.Custom"/>
    /// - see <see cref="OverrideResolution"/>).
    ///
    /// Config keys are kebab-case per CLAUDE.md; the C# property names stay
    /// PascalCase. Section names follow the maintainer's other PEAK mods
    /// (Capitalized-Hyphenated, or a plain word where there's nothing to hyphenate).
    ///
    /// Sections, in bind (and therefore file) order: <c>General</c> (seed,
    /// preset, and the client-side spore-bomb recolor), the per-mechanic
    /// Custom-only sections, then <c>Debug</c> last.
    ///
    /// Resolved accessors (e.g. <see cref="SporeBombCullFraction"/>) fold preset +
    /// override together so callers never re-implement that logic. Every
    /// gameplay setting is live by default (edits made in-game, e.g. via
    /// PEAKLib.ModConfig, take effect immediately) - <see cref="ApplyChangesLive"/>
    /// (in <c>Debug</c>, since freezing values mid-run is a comparison-testing
    /// tool) turns that off, in which case game-facing code should read the
    /// <c>Effective*</c> accessors (snapshotted once per Roots level load via
    /// <see cref="CaptureLevelSnapshot"/>) instead of the raw resolved ones. The
    /// spore-bomb removal fraction and the seed are always level-load-only
    /// regardless of this flag - which spore bombs were already removed can't be
    /// undone mid-level, so there's nothing for "live" to mean there.
    ///
    /// <b>Host authority (locked in 2026-07-22 - see ROADMAP.md).</b> Every
    /// <c>Effective*</c> accessor that decides actual shared game logic (what
    /// gets removed/spawned, how much force applies, whether wind occurs at
    /// all) additionally runs through <see cref="HostAuthority.Resolve"/>: on
    /// the host, this is a no-op (the local value already is the authoritative
    /// one); on any other client, it's overridden by whatever the host has
    /// published, so an individual client's own local config for these can
    /// never diverge from the host's. Purely local per-player feel settings -
    /// <see cref="EffectiveWindFallCameraDampenClamp"/>,
    /// <see cref="WindRecentForceWindowSeconds"/>,
    /// <see cref="RecolorSporeBombs"/> (purely cosmetic - see its own remarks),
    /// and everything in <c>Debug</c> - are deliberately excluded, since they
    /// don't affect anyone but the player who set them.
    /// </summary>
    public class PluginConfig
    {
        // --- General -------------------------------------------------------
        public ConfigEntry<int> Seed { get; }

        public ConfigEntry<PresetId> Preset { get; }

        /// <summary>
        /// Whether spore bombs are tinted toward the game's own Spores status
        /// color (pink/red) instead of their vanilla green - see
        /// <see cref="Core.SporeBombRecolor"/> for the reasoning and the math,
        /// and <c>SporeBombs/SporeBombRecolorPatch</c> for how it's applied.
        /// On by default.
        ///
        /// <b>The one deliberately client-side setting in this mod.</b> Every
        /// other non-Debug setting here is host-authoritative (see the class
        /// remarks) because it changes shared gameplay; this one changes
        /// nothing but what the player looking at their own screen sees, so
        /// there's no consistency to enforce and no reason a host should get to
        /// dictate it. Read the raw entry directly - there's deliberately no
        /// <c>Effective*</c> accessor, since there's neither a host lookup nor a
        /// level-load snapshot to apply (it always takes effect immediately,
        /// regardless of <see cref="ApplyChangesLive"/> - a cosmetic toggle
        /// waiting for a level reload would just be confusing).
        /// </summary>
        public ConfigEntry<bool> RecolorSporeBombs { get; }

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
        /// Custom-preset value for the multiplier feeding
        /// <see cref="Core.SporeBombExplosionTuning.ResolveTriggerHeightCutoffMeters"/> -
        /// how high above a spore bomb's base a player can be and still set it
        /// off. Vanilla's trigger sphere reaches absurdly far above the actual
        /// mushroom mesh for the "Spore Bomb"/"Poison Spore Bomb" variants
        /// (confirmed by the maintainer via the trigger-radius wireframe overlay
        /// - a genuinely oversized hitbox, not a misreading), so a player jumping
        /// over a short, wide mushroom clump still sets it off mid-air. 1.0 =
        /// vanilla (cutoff disabled - the fix doesn't engage at all). Does not
        /// apply to the "Explosive Spore Bomb" variant, which is genuinely round.
        /// Only takes effect when preset is set to Custom (5) - ignored under
        /// presets 1-4.
        /// </summary>
        public ConfigEntry<double> SporeBombTriggerHeightMultiplierOverride { get; }

        /// <summary>
        /// Custom-preset value for the multiplier applied to the radius (and
        /// proportionally, the inner/outer fade) of the temporary spore area a
        /// regular "Spore Bomb"/"Poison Spore Bomb" creates when triggered - the
        /// small AOE that applies the Spores status effect, not the "Explosive
        /// Spore Bomb" variant, which has no spore area of its own. 1.0 = vanilla
        /// size. Only takes effect when preset is set to Custom (5) - ignored
        /// under presets 1-4; see <see cref="SporeBombCullFractionOverride"/>.
        /// </summary>
        public ConfigEntry<double> SporeBombSporeAreaRadiusMultiplierOverride { get; }

        // --- Spore-Areas ----------------------------------------------------
        /// <summary>
        /// Master kill switch for the Roots biome's persistent spore areas (the
        /// game's "Mushroom Spore Clouds" - a <c>Spores</c>-type
        /// <c>WindAffectedStatusEmitter</c> plus the emitter mushroom in the
        /// middle of the cloud and its cloud VFX). When on, the whole spore-area
        /// object is deactivated, so it applies no Spores status, shows no
        /// screen-filter warning, and isn't visible either -
        /// <c>SporeAreas/SporeAreaDisablePatch</c> does the work.
        ///
        /// Deliberately scoped to the level's own baked-in spore areas: the small
        /// temporary spore area a spore bomb leaves behind on detonation is a
        /// separate hazard with its own settings under <c>Spore-Bombs</c> and is
        /// never touched by this. <b>Host-authoritative</b> (read via
        /// <see cref="EffectiveDisableSporeAreas"/>), flat (not folded through the
        /// preset/override system - no preset ever turns it on) and always
        /// immediate regardless of <see cref="ApplyChangesLive"/>, exactly like
        /// <see cref="DisableWindEntirely"/>.
        /// </summary>
        public ConfigEntry<bool> DisableSporeAreas { get; }

        /// <summary>
        /// Custom-preset value for the fraction of the level's spore areas to
        /// remove outright (see <see cref="Core.SporeAreaCull"/>) - "make spore
        /// areas less common." Seeded and cluster-first: the emitter closest to
        /// another emitter goes first, so overlapping clouds get thinned before
        /// isolated ones. 0 = remove none. Only takes effect under
        /// <see cref="PresetId.Custom"/>; see
        /// <see cref="SporeBombCullFractionOverride"/>. Like the spore-bomb
        /// removal fraction, this is level-load-only either way - which spore
        /// areas were already removed can't be un-removed mid-level.
        /// </summary>
        public ConfigEntry<double> SporeAreaRemovalFractionOverride { get; }

        /// <summary>
        /// Custom-preset value for the multiplier applied to every persistent spore
        /// area's radius - and, proportionally, its inner/outer fade and the visible
        /// size of the cloud itself, so what you see matches what actually applies
        /// the status (see <see cref="Core.SporeAreaTuning"/>). 1.0 = vanilla
        /// (radius 16 world units). Only takes effect under
        /// <see cref="PresetId.Custom"/>; see
        /// <see cref="SporeBombCullFractionOverride"/>.
        ///
        /// Not to be confused with
        /// <see cref="SporeBombSporeAreaRadiusMultiplierOverride"/>, which is the
        /// temporary mini spore area a *spore bomb* leaves behind.
        /// </summary>
        public ConfigEntry<double> SporeAreaRadiusMultiplierOverride { get; }

        // --- Wind -----------------------------------------------------------
        /// <summary>
        /// Master kill switch for the entire wind mechanic. When on, wind
        /// should never occur at all - not "vanilla-strength wind," genuinely
        /// no wind (clarified 2026-07-22: an earlier version only reverted the
        /// force/duration/item/occlusion scaling to vanilla numbers, which
        /// still let vanilla wind gusts happen; that was wrong). Achieved by
        /// forcing <c>WindChillZone.RPCA_ToggleWind</c>'s incoming "turn wind
        /// on" signal to false (see <c>WindToggleSuppressionPatch</c>) so no
        /// zone can ever go active again, plus forcing <c>windActive</c> off
        /// immediately for a gust already in progress the instant this is
        /// flipped on. <b>Host-authoritative</b> (ROADMAP.md's "Host
        /// authority" section, locked in 2026-07-22): read this via
        /// <see cref="PluginConfig.EffectiveDisableWindEntirely"/>, never this
        /// raw entry directly - only the host's value is ever used, an
        /// individual client's own local value here has no effect on its own.
        /// Off by default; no preset ever sets this to true - it's a
        /// manual-only override for a host who wants zero wind for the whole
        /// lobby, full stop. Not folded through the preset/override system
        /// (deliberately flat, like <see cref="KeepVanillaTriggerRadius"/>) and
        /// always applies immediately regardless of <see cref="ApplyChangesLive"/>,
        /// since a safety switch like this should never wait for a level reload.
        /// </summary>
        public ConfigEntry<bool> DisableWindEntirely { get; }

        /// <summary>
        /// Whether backpacks are always fully immune to wind force, regardless
        /// of <see cref="WindItemForceMultiplierOverride"/>/preset. On by
        /// default (ROADMAP.md's "backpack only" is the minimum immunity level
        /// on every preset) - turn off if you want backpacks affected by wind
        /// like any other ground item (scaled by the same item-force
        /// multiplier as everything else). <b>Host-authoritative</b> (same as
        /// <see cref="DisableWindEntirely"/>): read via
        /// <see cref="PluginConfig.EffectiveWindBackpackAlwaysImmune"/>, only
        /// the host's value is ever used. Not folded through the preset/
        /// override system (deliberately flat) - a player override on top of
        /// whatever preset is active, and always applies immediately regardless
        /// of <see cref="ApplyChangesLive"/>, same reasoning as
        /// <see cref="DisableWindEntirely"/>.
        /// </summary>
        public ConfigEntry<bool> WindBackpackAlwaysImmune { get; }

        /// <summary>
        /// Custom-preset value for the multiplier applied to
        /// <c>WindChillZone.windForce</c> only. Split from gust duration/frequency
        /// (<see cref="WindGustDurationMultiplierOverride"/>) on 2026-07-22 so
        /// each can be tuned independently. Only takes effect under
        /// <see cref="PresetId.Custom"/>; see <see cref="SporeBombCullFractionOverride"/>.
        /// </summary>
        public ConfigEntry<double> WindForceMultiplierOverride { get; }

        /// <summary>
        /// Custom-preset value for the multiplier applied to gust duration
        /// (<c>windTimeRangeOn</c>) and, inversely, the calm period between
        /// gusts (<c>windTimeRangeOff</c> - see
        /// <see cref="Core.WindTuning.ScaleWindRestDuration"/>). Independent of
        /// <see cref="WindForceMultiplierOverride"/> - lets you test gust
        /// timing without changing push strength, and vice versa. Only takes
        /// effect under <see cref="PresetId.Custom"/>; see
        /// <see cref="SporeBombCullFractionOverride"/>.
        /// </summary>
        public ConfigEntry<double> WindGustDurationMultiplierOverride { get; }

        /// <summary>
        /// Custom-preset value for the multiplier applied to non-backpack ground
        /// items' wind force (backpacks are always fully immune, regardless of
        /// this value). Only takes effect under <see cref="PresetId.Custom"/>;
        /// see <see cref="SporeBombCullFractionOverride"/>.
        /// </summary>
        public ConfigEntry<double> WindItemForceMultiplierOverride { get; }

        /// <summary>
        /// Custom-preset value for the multiplier applied to the existing
        /// obstacle-occlusion raycast's min/max distance (already enabled in
        /// Roots - see <see cref="Core.WindTuning.ScaleRaycastDistance"/>). Only
        /// takes effect under <see cref="PresetId.Custom"/>; see
        /// <see cref="SporeBombCullFractionOverride"/>.
        /// </summary>
        public ConfigEntry<double> WindObstacleOcclusionRangeMultiplierOverride { get; }

        /// <summary>
        /// Custom-preset value for the camera-control floor applied while a fall
        /// is wind-preceded (0 = off). Only takes effect under
        /// <see cref="PresetId.Custom"/>; see <see cref="SporeBombCullFractionOverride"/>.
        /// </summary>
        public ConfigEntry<double> WindFallCameraDampenClampOverride { get; }

        /// <summary>
        /// How many seconds after wind force was last applied to the local
        /// character that a subsequent fall still counts as "wind-preceded" for
        /// the camera-dampening clamp above. Flat setting (not folded through the
        /// preset/override system) - it's a timing window, not a balance dial
        /// that scales per preset.
        /// </summary>
        public ConfigEntry<float> WindRecentForceWindowSeconds { get; }

        /// <summary>
        /// Whether holding onto something (wall climbing, a rope, a vine, a climb
        /// handle) makes the player fully immune to wind force, at the cost of
        /// climbing much slower while the wind is actually pushing on them - see
        /// <see cref="Core.ClimbWindResistance"/> for why this is a real mechanic
        /// rather than the vanilla behavior an earlier research pass thought it
        /// was. Flat (not preset-gated - every preset has it on, per ROADMAP.md's
        /// "New: climb-to-counter-wind" row) but player-toggleable, same shape as
        /// <see cref="WindBackpackAlwaysImmune"/>. Host-authoritative: read via
        /// <see cref="PluginConfig.EffectiveClimbSheltersFromWind"/>. The three
        /// speed multipliers below are what it costs; this is whether it happens.
        /// </summary>
        public ConfigEntry<bool> ClimbSheltersFromWind { get; }

        /// <summary>
        /// Custom-preset value for the all-directions climb-speed multiplier
        /// applied while wind is actually pushing on the climber. Only takes
        /// effect under <see cref="PresetId.Custom"/>; see
        /// <see cref="SporeBombCullFractionOverride"/>.
        /// </summary>
        public ConfigEntry<double> ClimbWindSpeedMultiplierOverride { get; }

        /// <summary>
        /// Custom-preset value for the extra multiplier on upward climb movement
        /// in wind (on top of <see cref="ClimbWindSpeedMultiplierOverride"/>).
        /// Only takes effect under <see cref="PresetId.Custom"/>.
        /// </summary>
        public ConfigEntry<double> ClimbWindUpwardSpeedMultiplierOverride { get; }

        /// <summary>
        /// Custom-preset value for the extra multiplier on climb movement that
        /// opposes the wind direction (on top of
        /// <see cref="ClimbWindSpeedMultiplierOverride"/>). Only takes effect
        /// under <see cref="PresetId.Custom"/>.
        /// </summary>
        public ConfigEntry<double> ClimbWindIntoWindSpeedMultiplierOverride { get; }

        /// <summary>
        /// How long the much-weaker-wind grace window lasts after letting go of a
        /// climb (see <see cref="Core.ClimbWindResistance.GraceForceMultiplier"/>
        /// for why it exists). Flat (not folded through the preset/override
        /// system) - it's a timing window, not a balance dial, same reasoning as
        /// <see cref="WindRecentForceWindowSeconds"/> - but unlike that one it IS
        /// host-authoritative (<see cref="PluginConfig.EffectiveClimbShelterGraceSeconds"/>),
        /// because it changes how much force actually gets applied rather than
        /// how the local camera feels. Only has any effect while the climb
        /// shelter itself is active (<see cref="ClimbSheltersFromWind"/>).
        /// </summary>
        public ConfigEntry<float> ClimbShelterGraceSeconds { get; }

        /// <summary>
        /// Custom-preset value for how strong wind is during the let-go grace
        /// window, as a fraction of normal. Only takes effect under
        /// <see cref="PresetId.Custom"/>; see <see cref="SporeBombCullFractionOverride"/>.
        /// </summary>
        public ConfigEntry<double> ClimbWindGraceForceMultiplierOverride { get; }

        // --- Debug (kept last) --------------------------------------------
        /// <summary>
        /// When on (default), every gameplay setting takes effect the instant
        /// you change it in-game - e.g. via PEAKLib.ModConfig. When off, changes
        /// are only picked up the next time you load into a Roots biome;
        /// whatever was configured at that moment stays in effect for the whole
        /// level, regardless of what you change afterward. Useful for
        /// A/B-testing a mechanic without values shifting under you mid-run,
        /// which is why it lives in <c>Debug</c> alongside the other
        /// comparison-testing tools rather than in <c>General</c>.
        ///
        /// Like <see cref="KeepVanillaTriggerRadius"/>, this is a behavior
        /// override rather than a diagnostic, so it applies regardless of
        /// <see cref="EnableDebugLogging"/>. It doesn't gate anything else in
        /// this section (those always apply immediately), nor the settings that
        /// are flat by design - <see cref="DisableWindEntirely"/>,
        /// <see cref="WindBackpackAlwaysImmune"/> and
        /// <see cref="RecolorSporeBombs"/>.
        /// </summary>
        public ConfigEntry<bool> ApplyChangesLive { get; }

        /// <summary>Master switch for verbose diagnostic logging (the whole Debug harness is a no-op unless this is on).</summary>
        public ConfigEntry<bool> EnableDebugLogging { get; }

        /// <summary>Auto-dump a scene diagnostics report each time a level finishes generating.</summary>
        public ConfigEntry<bool> LogSceneScanOnLoad { get; }

        /// <summary>Hotkey to dump a scene diagnostics report on demand while playing.</summary>
        public ConfigEntry<KeyCode> SceneScanHotkey { get; }

        /// <summary>
        /// Log every camera shake the game queues, with the call stack that asked for
        /// it. Diagnostic only, and separately gated because it's far noisier than the
        /// rest of the Debug harness (a stack trace per shake, and the game shakes the
        /// camera constantly while climbing).
        /// </summary>
        public ConfigEntry<bool> LogScreenshakeSources { get; }

        /// <summary>Hotkey to probe the nearest spore-bomb candidate for a foliage-detection method (Phase 4 research, not shipped functionality).</summary>
        public ConfigEntry<KeyCode> FoliageProbeHotkey { get; }

        /// <summary>
        /// Hotkey to dump the material/shader setup of whatever the player is
        /// standing next to (<see cref="Diagnostics.MaterialProbe"/>) - which
        /// color slots the shader actually declares and which ones Fairoots is
        /// overriding. Research tool for the spore-bomb recolor; shaders are
        /// assets rather than code, so this is the only way to find out what a
        /// prop's albedo slot is really called.
        /// </summary>
        public ConfigEntry<KeyCode> MaterialProbeHotkey { get; }

        /// <summary>Draw a 2D screen-space label over every spore bomb removed this level load, tagged by why (foliage/seeded). Off by default.</summary>
        public ConfigEntry<bool> ShowRemovedSporeBombMarkers { get; }

        /// <summary>Draw a 3D wireframe (red) over every nearby kept spore bomb's current trigger hitbox. Off by default.</summary>
        public ConfigEntry<bool> ShowSporeBombTriggerRadius { get; }

        /// <summary>
        /// For before/after comparison screenshots: when on, spore-bomb trigger
        /// hitboxes are left at vanilla size instead of being shrunk, and the
        /// trigger-height cutoff (<see cref="SporeBombTriggerHeightMultiplierOverride"/>,
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
                "every load. HOST-AUTHORITATIVE: everyone in the lobby must have Fairoots " +
                "installed, but only the HOST's seed is ever actually used - non-host players' " +
                "own seed value here is ignored entirely, so there's nothing to coordinate " +
                "manually (see ROADMAP.md's Host authority section).");

            Preset = config.Bind(
                "General",
                "preset",
                PresetId.Balanced,
                "Overall balance preset. Subtle (1) is the lightest touch, Tame (4) the " +
                "heaviest, Balanced (2) is the default. Custom (5) ignores the hard-coded " +
                "preset numbers entirely and uses your own Spore-Bombs/Wind settings directly " +
                "instead. Under presets 1-4, the per-mechanic settings below are ignored " +
                "entirely, even if you've changed them - switch to Custom to use them. " +
                "HOST-AUTHORITATIVE: only the host's preset (and, under Custom, the host's " +
                "own per-mechanic values) is ever actually used for the whole lobby.");

            RecolorSporeBombs = config.Bind(
                "General",
                "recolor-spore-bombs",
                true,
                "Tints spore bombs (and explosive spore bombs) toward the pink/red the game's " +
                "own Spores status effect uses, instead of leaving them vanilla green - a green " +
                "hazard on green grass camouflages into the terrain, which is exactly what a " +
                "hazard shouldn't do. Purely cosmetic and PER-PLAYER: unlike every other " +
                "gameplay setting here, this one is NOT host-authoritative - it only changes " +
                "what you see on your own screen, so set it however you like regardless of what " +
                "the host or anyone else in the lobby has. Applies immediately, regardless of " +
                "apply-changes-live.");

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

            SporeBombTriggerHeightMultiplierOverride = config.Bind(
                "Spore-Bombs",
                "trigger-height-multiplier",
                1.0,
                new ConfigDescription(
                    "Multiplier controlling how high above its base a player can be and still " +
                    "set off a \"Spore Bomb\" or \"Poison Spore Bomb\" (not the round " +
                    "\"Explosive Spore Bomb\", which is unaffected) - fixes vanilla's oversized " +
                    "trigger sphere reaching absurdly far above the actual mushroom mesh, which " +
                    "made it impossible to jump over one without setting it off. 1.0 always " +
                    "means vanilla (cutoff disabled), no matter what else you change - lower " +
                    "values engage the fix more aggressively. Only takes effect when preset is " +
                    "set to Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.0, 3.0)));

            SporeBombSporeAreaRadiusMultiplierOverride = config.Bind(
                "Spore-Bombs",
                "spore-area-radius-multiplier",
                1.0,
                new ConfigDescription(
                    "Multiplier applied to the radius (and proportionally, the inner/outer " +
                    "fade) of the temporary spore area a regular \"Spore Bomb\"/\"Poison Spore " +
                    "Bomb\" creates when triggered, e.g. 0.5 halves how far it reaches, 2.0 " +
                    "doubles it. 1.0 always means vanilla size. Doesn't affect the \"Explosive " +
                    "Spore Bomb\" variant, which has no spore area. Only takes effect when " +
                    "preset is set to Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.0, 5.0)));

            DisableSporeAreas = config.Bind(
                "Spore-Areas",
                "disable-spore-areas",
                false,
                "Master switch: when on, the Roots biome's spore areas (\"Mushroom Spore Clouds\") " +
                "are removed entirely - no Spores status, no green screen filter, and the emitter " +
                "mushroom in the middle of the cloud plus the cloud itself disappear with them. " +
                "Doesn't touch the small temporary spore area a spore bomb leaves behind when it " +
                "goes off (that's the Spore-Bombs section). HOST-AUTHORITATIVE: if you're not the " +
                "host, this has no effect at all - only the host's value counts for the whole " +
                "lobby. Off by default; no preset ever turns this on automatically. Applies " +
                "immediately, regardless of apply-changes-live.");

            SporeAreaRemovalFractionOverride = config.Bind(
                "Spore-Areas",
                "removal-fraction",
                0.0,
                new ConfigDescription(
                    "Fraction of the level's spore areas to remove entirely, e.g. 0.25 removes a " +
                    "quarter of them. Which ones is decided by the seed, and always starts with " +
                    "the spore area closest to another one - so overlapping clouds (the ones you " +
                    "can't get past without taking spores) get thinned first, and isolated ones " +
                    "you can simply walk around are left alone. 0 = remove none. Only takes " +
                    "effect when preset is set to Custom (5) - ignored under presets 1-4, which " +
                    "remove none at all on Subtle/Balanced. Only applies on the next Roots level " +
                    "load either way, since a spore area that's already gone can't come back " +
                    "mid-level.",
                    new AcceptableValueRange<double>(0.0, 1.0)));

            SporeAreaRadiusMultiplierOverride = config.Bind(
                "Spore-Areas",
                "radius-multiplier",
                0.85,
                new ConfigDescription(
                    "Multiplier applied to how far every spore area reaches, e.g. 0.7 shrinks it " +
                    "to 70% of vanilla, 1.5 makes it half again as big. The visible cloud is " +
                    "resized to match, so what you can see is what actually gives you spores. " +
                    "1.0 always means vanilla size (radius 16, about 26m across from the middle). " +
                    "How quickly the spores themselves are applied is a separate setting - this " +
                    "only changes the size. Only takes effect when preset is set to Custom (5) - " +
                    "ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.0, 3.0)));

            DisableWindEntirely = config.Bind(
                "Wind",
                "disable-wind-entirely",
                false,
                "Master switch: when on, wind should NEVER occur at all - not vanilla-strength " +
                "wind, genuinely no wind, ever. HOST-AUTHORITATIVE: if you're not the host, " +
                "this has no effect at all - only the host's value counts for the whole lobby " +
                "(same for everyone in the run, no exceptions - see ROADMAP.md's Host " +
                "authority section). Any gust already blowing stops immediately when the host " +
                "turns this on, and none can start again while it stays on. Off by default - " +
                "no preset ever turns this on automatically. Applies immediately, regardless " +
                "of apply-changes-live.");

            WindBackpackAlwaysImmune = config.Bind(
                "Wind",
                "backpack-always-immune",
                true,
                "Whether backpacks are always fully immune to wind force, regardless of " +
                "item-force-multiplier/preset. On by default. HOST-AUTHORITATIVE: only the " +
                "host's value counts for the whole lobby, regardless of what non-host players " +
                "have set locally. Turn off to let backpacks be affected by wind like any " +
                "other ground item. Applies immediately, regardless of apply-changes-live.");

            WindForceMultiplierOverride = config.Bind(
                "Wind",
                "force-multiplier",
                0.80,
                new ConfigDescription(
                    "Multiplier applied to wind's push force only (see " +
                    "gust-duration-multiplier below for timing), e.g. 0.6 cuts it to 60% of " +
                    "vanilla, 2.0 doubles it, 0 means no push at all. 1.0 always means vanilla " +
                    "force, no matter what else you change. Only takes effect when preset is " +
                    "set to Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.0, 3.0)));

            WindGustDurationMultiplierOverride = config.Bind(
                "Wind",
                "gust-duration-multiplier",
                0.80,
                new ConfigDescription(
                    "Multiplier applied to how long a gust lasts once it starts (in the same " +
                    "direction, the calm period between gusts scales inversely) - independent " +
                    "of force-multiplier above, so you can test gust timing/frequency without " +
                    "changing push strength. E.g. 0.6 makes gusts noticeably shorter and less " +
                    "frequent. 1.0 always means vanilla timing. Gust duration is always floored " +
                    "at 1 second regardless of how low this goes - a genuinely zero-length gust " +
                    "breaks the game's own wind on/off timer (confirmed live 2026-07-22). Only " +
                    "takes effect when preset is set to Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.0, 3.0)));

            WindItemForceMultiplierOverride = config.Bind(
                "Wind",
                "item-force-multiplier",
                0.70,
                new ConfigDescription(
                    "Multiplier applied to wind's push force on dropped items other than " +
                    "backpacks (backpacks are always fully immune to wind, on every preset), " +
                    "e.g. 0.4 cuts it to 40% of vanilla, 0.0 makes every item fully immune too. " +
                    "1.0 always means vanilla force. Only takes effect when preset is set to " +
                    "Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.0, 3.0)));

            WindObstacleOcclusionRangeMultiplierOverride = config.Bind(
                "Wind",
                "obstacle-occlusion-range-multiplier",
                1.30,
                new ConfigDescription(
                    "Multiplier applied to the existing obstacle-occlusion raycast's min/max " +
                    "distance (already enabled in Roots vanilla) - widening it lets standing " +
                    "behind an obstacle block wind from further away, e.g. 1.6 widens both " +
                    "distances by 60%. 1.0 always means vanilla range. Only takes effect when " +
                    "preset is set to Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.5, 4.0)));

            WindFallCameraDampenClampOverride = config.Bind(
                "Wind",
                "fall-camera-dampen-clamp",
                0.35,
                new ConfigDescription(
                    "Floor applied to camera-control while falling, but only when the fall " +
                    "was preceded by recent wind force (see fall-camera-dampen-window-seconds " +
                    "below) - keeps the camera partially player-controlled instead of fully " +
                    "surrendering to ragdoll-head physics, so you have a chance to grab a wall " +
                    "or use a Rescue Hook after wind blows you off a ledge. 0 disables it " +
                    "(vanilla - full ragdoll camera on every fall). An ordinary fall that " +
                    "wasn't wind-preceded is never affected. Only takes effect when preset is " +
                    "set to Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.0, 1.0)));

            WindRecentForceWindowSeconds = config.Bind(
                "Wind",
                "fall-camera-dampen-window-seconds",
                1.5f,
                new ConfigDescription(
                    "How many seconds after wind last pushed you that a fall still counts as " +
                    "wind-preceded for fall-camera-dampen-clamp above. Not tied to any preset - " +
                    "applies the same regardless of which preset is active.",
                    new AcceptableValueRange<float>(0.1f, 5f)));

            ClimbSheltersFromWind = config.Bind(
                "Wind",
                "climb-shelters-from-wind",
                true,
                "Whether holding onto something shelters you from wind: while climbing a wall, " +
                "a rope, a vine or a climb handle, wind can't push you at all - instead " +
                "climbing gets much slower for as long as the wind is actually blowing on you " +
                "(see the three climb-*-multiplier settings below). Vanilla only shelters you " +
                "on a climb handle, so a gust mid-climb normally rips you off the wall, which " +
                "is why walking into the wind is the only reliable tactic. On by default on " +
                "every preset EXCEPT Subtle (1), where the mechanic doesn't exist at all - " +
                "turning this on under Subtle does nothing. If the wind can't reach you " +
                "anyway - behind a rock, no gust - " +
                "you climb at full speed, so this never costs you anything when it isn't " +
                "protecting you. HOST-AUTHORITATIVE: only the host's value counts for the " +
                "whole lobby. Applies immediately, regardless of apply-changes-live.");

            ClimbWindSpeedMultiplierOverride = config.Bind(
                "Wind",
                "climb-speed-multiplier-in-wind",
                0.90,
                new ConfigDescription(
                    "How fast you climb while wind is pushing on you, as a fraction of normal " +
                    "climbing speed - the price of climb-shelters-from-wind above. E.g. 0.90 " +
                    "means a tenth slower at full wind pressure; 1.0 means no slowdown " +
                    "at all (free shelter). Fades in with how hard the wind is actually " +
                    "blowing, so partial cover means only a partial slowdown. Only takes " +
                    "effect when preset is set to Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.05, 1.0)));

            ClimbWindUpwardSpeedMultiplierOverride = config.Bind(
                "Wind",
                "climb-upward-speed-multiplier-in-wind",
                0.85,
                new ConfigDescription(
                    "Extra slowdown on climbing UPWARD in wind, multiplied on top of " +
                    "climb-speed-multiplier-in-wind above (so 0.90 and 0.85 together mean " +
                    "about three quarters of normal speed climbing up through a full-strength " +
                    "gust). " +
                    "Climbing down is never slowed beyond the base multiplier. 1.0 means " +
                    "upward climbing costs no more than any other direction. Only takes " +
                    "effect when preset is set to Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.05, 1.0)));

            ClimbWindIntoWindSpeedMultiplierOverride = config.Bind(
                "Wind",
                "climb-into-wind-speed-multiplier",
                0.85,
                new ConfigDescription(
                    "Extra slowdown on climbing INTO the wind (toward where it's blowing " +
                    "from), multiplied on top of climb-speed-multiplier-in-wind above. " +
                    "Climbing with the wind is never sped up. 1.0 means direction doesn't " +
                    "matter. Only takes effect when preset is set to Custom (5) - ignored " +
                    "under presets 1-4.",
                    new AcceptableValueRange<double>(0.05, 1.0)));

            ClimbShelterGraceSeconds = config.Bind(
                "Wind",
                "climb-shelter-grace-seconds",
                0.5f,
                new ConfigDescription(
                    "How long wind stays much weaker after you let go of a climb - the window " +
                    "that stops finishing a climb mid-gust from catapulting you, and gives you " +
                    "time to start sprinting away or re-grab the wall if you let go by " +
                    "accident. Wind is held at climb-shelter-grace-force-multiplier below for " +
                    "most of the window, then ramps back to full over the tail of it so it " +
                    "doesn't end in a sudden shove. Set to 0 to switch the window off entirely " +
                    "(full wind force the instant you let go, like vanilla). Not tied to any " +
                    "preset - applies the same regardless of which preset is active, though it " +
                    "does nothing while the climb shelter itself is off. HOST-AUTHORITATIVE: " +
                    "only the host's value counts for the whole lobby.",
                    new AcceptableValueRange<float>(0f, 3f)));

            ClimbWindGraceForceMultiplierOverride = config.Bind(
                "Wind",
                "climb-shelter-grace-force-multiplier",
                0.15,
                new ConfigDescription(
                    "How strong wind is during the grace window above, as a fraction of " +
                    "normal - e.g. 0.15 means 15% force, close to immune but not quite. " +
                    "Deliberately not 0: full immunity would let you tap a wall over and over " +
                    "to cross an exposed stretch wind-free, turning the climb shelter into a " +
                    "movement exploit. 1.0 means no reduction at all. Only takes effect when " +
                    "preset is set to Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.0, 1.0)));

            // --- Debug section: bound last so it sorts to the bottom of the
            // config file. Everything here is diagnostic or comparison-testing
            // tooling; the two behavior overrides in it (apply-changes-live,
            // keep-vanilla-trigger-radius) apply regardless of the debug-logging
            // master switch, everything else is a no-op without it.
            ApplyChangesLive = config.Bind(
                "Debug",
                "apply-changes-live",
                true,
                "When on (default), every gameplay setting takes effect the instant you change " +
                "it in-game (e.g. via PEAKLib.ModConfig): kept spore bombs resize live, the next " +
                "detonation uses the new knockback/VFX/shake numbers, and the jump-over-height " +
                "cutoff updates immediately. Turn this off to freeze all of that at whatever it " +
                "was the moment you loaded into Roots - further changes only take effect the " +
                "next time you load into a Roots biome, which is useful for A/B-testing a " +
                "mechanic without values shifting under you mid-run. The spore-bomb removal " +
                "fraction and the seed are always level-load-only either way, since which spore " +
                "bombs were already removed can't be undone mid-level; the wind kill switch, " +
                "backpack immunity and the spore-bomb recolor are always immediate either way. " +
                "This is a behavior override, not a diagnostic, so it works regardless of the " +
                "debug-logging switch below.");

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

            LogScreenshakeSources = config.Bind(
                "Debug",
                "log-screenshake-sources",
                false,
                "When debug logging is on, log every camera shake the game queues along " +
                "with the code that asked for it, and how far away the source was. Use " +
                "this to work out why a shake you expected to be distance-capped still " +
                "happened. Very noisy (the game shakes the camera constantly while " +
                "climbing) - leave off unless you're chasing a specific shake.");

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

            MaterialProbeHotkey = config.Bind(
                "Debug",
                "material-probe-hotkey",
                KeyCode.F11,
                "When debug logging is on, look at something and press this key to dump its material " +
                "and shader setup: every color slot the shader declares, its value, and whether " +
                "Fairoots is currently overriding it. Use it to work out why something looks " +
                "miscolored - the report says outright whether the mod touched a given object " +
                "or not. Set to None to disable the hotkey.");

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
                "(trigger-height-multiplier) is bypassed too, so jumping over one behaves like " +
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

        /// <summary>The effective trigger-height-cutoff multiplier (1.0 = vanilla/disabled) - see <see cref="Core.SporeBombExplosionTuning.ResolveTriggerHeightCutoffMeters"/>.</summary>
        public double SporeBombTriggerHeightMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.SporeBombTriggerHeightMultiplier(Preset.Value),
                SporeBombTriggerHeightMultiplierOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective spore-area radius multiplier for a spore-bomb detonation.</summary>
        public double SporeBombSporeAreaRadiusMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.SporeBombSporeAreaRadiusMultiplier(Preset.Value),
                SporeBombSporeAreaRadiusMultiplierOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective fraction of the level's spore areas to remove.</summary>
        public double SporeAreaRemovalFraction =>
            OverrideResolution.Resolve(
                PresetCatalog.SporeAreaRemovalFraction(Preset.Value),
                SporeAreaRemovalFractionOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective radius multiplier for every persistent spore area.</summary>
        public double SporeAreaRadiusMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.SporeAreaRadiusMultiplier(Preset.Value),
                SporeAreaRadiusMultiplierOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective wind-force (and gust-timing) multiplier.</summary>
        public double WindForceMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.WindForceMultiplier(Preset.Value),
                WindForceMultiplierOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective gust-duration/frequency multiplier (independent of <see cref="WindForceMultiplier"/>).</summary>
        public double WindGustDurationMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.WindGustDurationMultiplier(Preset.Value),
                WindGustDurationMultiplierOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective non-backpack item wind-force multiplier.</summary>
        public double WindItemForceMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.WindItemForceMultiplier(Preset.Value),
                WindItemForceMultiplierOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective obstacle-occlusion raycast-distance multiplier.</summary>
        public double WindObstacleOcclusionRangeMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.WindObstacleOcclusionRangeMultiplier(Preset.Value),
                WindObstacleOcclusionRangeMultiplierOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective wind-preceded-fall camera-control floor (0 = off).</summary>
        public double WindFallCameraDampenClamp =>
            OverrideResolution.Resolve(
                PresetCatalog.WindFallCameraDampenClamp(Preset.Value),
                WindFallCameraDampenClampOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective all-directions climb-speed multiplier while wind is pushing on the climber.</summary>
        public double ClimbWindSpeedMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.ClimbWindSpeedMultiplier(Preset.Value),
                ClimbWindSpeedMultiplierOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective extra multiplier on upward climb movement in wind.</summary>
        public double ClimbWindUpwardSpeedMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.ClimbWindUpwardSpeedMultiplier(Preset.Value),
                ClimbWindUpwardSpeedMultiplierOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective extra multiplier on climb movement opposing the wind.</summary>
        public double ClimbWindIntoWindSpeedMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.ClimbWindIntoWindSpeedMultiplier(Preset.Value),
                ClimbWindIntoWindSpeedMultiplierOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective wind-force multiplier for the let-go grace window.</summary>
        public double ClimbWindGraceForceMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.ClimbWindGraceForceMultiplier(Preset.Value),
                ClimbWindGraceForceMultiplierOverride.Value,
                UseCustomOverrides);

        // --- Level-load snapshot (ApplyChangesLive == false) --------------
        // Captured once per Roots level load (RootsLevelWatcher, right before
        // SporeBombCullPatch.Run) so game-facing code has a single, consistent
        // "what was configured when this level loaded" view to read from when the
        // player has opted out of live updates. Not used at all while
        // ApplyChangesLive is on - the Effective* accessors below just pass the
        // live resolved value straight through in that case.
        private bool _snapshotTaken;
        private double _snapCullFraction;
        private double _snapTriggerRadiusMultiplier;
        private double _snapKnockbackMultiplier;
        private float _snapScreenshakeRangeCapMeters;
        private double _snapVfxCountMultiplier;
        private double _snapTriggerHeightMultiplier;
        private double _snapSporeAreaRadiusMultiplier;
        private double _snapSporeAreaRemovalFraction;
        private double _snapSporeAreaRadiusMultiplierValue;
        private double _snapWindForceMultiplier;
        private double _snapWindGustDurationMultiplier;
        private double _snapWindItemForceMultiplier;
        private double _snapWindObstacleOcclusionRangeMultiplier;
        private double _snapWindFallCameraDampenClamp;
        private double _snapClimbWindSpeedMultiplier;
        private double _snapClimbWindUpwardSpeedMultiplier;
        private double _snapClimbWindIntoWindSpeedMultiplier;
        private double _snapClimbWindGraceForceMultiplier;

        /// <summary>
        /// Freezes every non-Debug resolved setting at its current (live) value.
        /// Called once per Roots level load, unconditionally (cheap - a handful of
        /// property reads), so a snapshot is always ready the moment the player
        /// turns <see cref="ApplyChangesLive"/> off mid-session.
        /// </summary>
        internal void CaptureLevelSnapshot()
        {
            _snapCullFraction = SporeBombCullFraction;
            _snapTriggerRadiusMultiplier = SporeBombTriggerRadiusMultiplier;
            _snapKnockbackMultiplier = SporeBombKnockbackMultiplier;
            _snapScreenshakeRangeCapMeters = SporeBombScreenshakeRangeCapMeters;
            _snapVfxCountMultiplier = SporeBombVfxCountMultiplier;
            _snapTriggerHeightMultiplier = SporeBombTriggerHeightMultiplier;
            _snapSporeAreaRadiusMultiplier = SporeBombSporeAreaRadiusMultiplier;
            _snapSporeAreaRemovalFraction = SporeAreaRemovalFraction;
            _snapSporeAreaRadiusMultiplierValue = SporeAreaRadiusMultiplier;
            _snapWindForceMultiplier = WindForceMultiplier;
            _snapWindGustDurationMultiplier = WindGustDurationMultiplier;
            _snapWindItemForceMultiplier = WindItemForceMultiplier;
            _snapWindObstacleOcclusionRangeMultiplier = WindObstacleOcclusionRangeMultiplier;
            _snapWindFallCameraDampenClamp = WindFallCameraDampenClamp;
            _snapClimbWindSpeedMultiplier = ClimbWindSpeedMultiplier;
            _snapClimbWindUpwardSpeedMultiplier = ClimbWindUpwardSpeedMultiplier;
            _snapClimbWindIntoWindSpeedMultiplier = ClimbWindIntoWindSpeedMultiplier;
            _snapClimbWindGraceForceMultiplier = ClimbWindGraceForceMultiplier;
            _snapshotTaken = true;
        }

        /// <summary>
        /// True while a live value should be used as-is: either the player wants
        /// live updates, or no level has loaded yet to snapshot from (falling back
        /// to live rather than a meaningless zeroed snapshot).
        /// </summary>
        private bool UseLiveValue => ApplyChangesLive.Value || !_snapshotTaken;

        /// <summary>
        /// Game-facing code should read this instead of <see cref="Seed"/>.Value -
        /// host-authoritative (<see cref="HostAuthority"/>): every client uses
        /// the HOST's seed, never their own, so the seeded spore-bomb cull is
        /// guaranteed identical across the whole lobby.
        /// </summary>
        public int EffectiveSeed => HostAuthority.Resolve("Seed", Seed.Value);

        /// <summary>Game-facing code should read this instead of <see cref="SporeBombCullFraction"/>. Host-authoritative.</summary>
        public double EffectiveSporeBombCullFraction =>
            HostAuthority.Resolve("SporeBombCullFraction", UseLiveValue ? SporeBombCullFraction : _snapCullFraction);

        /// <summary>Game-facing code should read this instead of <see cref="SporeBombTriggerRadiusMultiplier"/>. Host-authoritative.</summary>
        public double EffectiveSporeBombTriggerRadiusMultiplier =>
            HostAuthority.Resolve("SporeBombTriggerRadiusMultiplier", UseLiveValue ? SporeBombTriggerRadiusMultiplier : _snapTriggerRadiusMultiplier);

        /// <summary>Game-facing code should read this instead of <see cref="SporeBombKnockbackMultiplier"/>. Host-authoritative.</summary>
        public double EffectiveSporeBombKnockbackMultiplier =>
            HostAuthority.Resolve("SporeBombKnockbackMultiplier", UseLiveValue ? SporeBombKnockbackMultiplier : _snapKnockbackMultiplier);

        /// <summary>Game-facing code should read this instead of <see cref="SporeBombScreenshakeRangeCapMeters"/>. Host-authoritative.</summary>
        public float EffectiveSporeBombScreenshakeRangeCapMeters =>
            HostAuthority.Resolve("SporeBombScreenshakeRangeCapMeters", UseLiveValue ? SporeBombScreenshakeRangeCapMeters : _snapScreenshakeRangeCapMeters);

        /// <summary>Game-facing code should read this instead of <see cref="SporeBombVfxCountMultiplier"/>. Host-authoritative.</summary>
        public double EffectiveSporeBombVfxCountMultiplier =>
            HostAuthority.Resolve("SporeBombVfxCountMultiplier", UseLiveValue ? SporeBombVfxCountMultiplier : _snapVfxCountMultiplier);

        /// <summary>Game-facing code should read this instead of <see cref="SporeBombTriggerHeightMultiplier"/>. Host-authoritative.</summary>
        public double EffectiveSporeBombTriggerHeightMultiplier =>
            HostAuthority.Resolve("SporeBombTriggerHeightMultiplier", UseLiveValue ? SporeBombTriggerHeightMultiplier : _snapTriggerHeightMultiplier);

        /// <summary>Game-facing code should read this instead of <see cref="SporeBombSporeAreaRadiusMultiplier"/>. Host-authoritative.</summary>
        public double EffectiveSporeBombSporeAreaRadiusMultiplier =>
            HostAuthority.Resolve("SporeBombSporeAreaRadiusMultiplier", UseLiveValue ? SporeBombSporeAreaRadiusMultiplier : _snapSporeAreaRadiusMultiplier);

        /// <summary>Game-facing code should read this instead of <see cref="SporeAreaRemovalFraction"/>. Host-authoritative.</summary>
        public double EffectiveSporeAreaRemovalFraction =>
            HostAuthority.Resolve("SporeAreaRemovalFraction", UseLiveValue ? SporeAreaRemovalFraction : _snapSporeAreaRemovalFraction);

        /// <summary>Game-facing code should read this instead of <see cref="SporeAreaRadiusMultiplier"/>. Host-authoritative.</summary>
        public double EffectiveSporeAreaRadiusMultiplier =>
            HostAuthority.Resolve("SporeAreaRadiusMultiplier", UseLiveValue ? SporeAreaRadiusMultiplier : _snapSporeAreaRadiusMultiplierValue);

        /// <summary>Game-facing code should read this instead of <see cref="WindForceMultiplier"/>. Host-authoritative.</summary>
        public double EffectiveWindForceMultiplier =>
            HostAuthority.Resolve("WindForceMultiplier", UseLiveValue ? WindForceMultiplier : _snapWindForceMultiplier);

        /// <summary>Game-facing code should read this instead of <see cref="WindGustDurationMultiplier"/>. Host-authoritative.</summary>
        public double EffectiveWindGustDurationMultiplier =>
            HostAuthority.Resolve("WindGustDurationMultiplier", UseLiveValue ? WindGustDurationMultiplier : _snapWindGustDurationMultiplier);

        /// <summary>Game-facing code should read this instead of <see cref="WindItemForceMultiplier"/>. Host-authoritative.</summary>
        public double EffectiveWindItemForceMultiplier =>
            HostAuthority.Resolve("WindItemForceMultiplier", UseLiveValue ? WindItemForceMultiplier : _snapWindItemForceMultiplier);

        /// <summary>Game-facing code should read this instead of <see cref="WindObstacleOcclusionRangeMultiplier"/>. Host-authoritative.</summary>
        public double EffectiveWindObstacleOcclusionRangeMultiplier =>
            HostAuthority.Resolve("WindObstacleOcclusionRangeMultiplier", UseLiveValue ? WindObstacleOcclusionRangeMultiplier : _snapWindObstacleOcclusionRangeMultiplier);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="WindBackpackAlwaysImmune"/>.Value. Host-authoritative -
        /// flat (not preset/live-snapshot resolved, matching the raw config
        /// entry itself), but still only the host's value counts.
        /// </summary>
        public bool EffectiveWindBackpackAlwaysImmune =>
            HostAuthority.Resolve("WindBackpackAlwaysImmune", WindBackpackAlwaysImmune.Value);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="DisableSporeAreas"/>.Value. Host-authoritative - flat (not
        /// preset/live-snapshot resolved, matching the raw config entry itself),
        /// but whether a hazard exists at all is shared game state, so only the
        /// host's value counts (same shape as
        /// <see cref="EffectiveDisableWindEntirely"/>).
        /// </summary>
        public bool EffectiveDisableSporeAreas =>
            HostAuthority.Resolve("DisableSporeAreas", DisableSporeAreas.Value);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="DisableWindEntirely"/>.Value. Host-authoritative - if the
        /// host hasn't disabled wind, an individual client flipping this
        /// locally has no effect (matches "no client can unilaterally alter
        /// the game" - see ROADMAP.md).
        /// </summary>
        public bool EffectiveDisableWindEntirely =>
            HostAuthority.Resolve("DisableWindEntirely", DisableWindEntirely.Value);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="WindFallCameraDampenClamp"/>. Deliberately NOT
        /// host-authoritative - purely local camera-feel/accessibility, doesn't
        /// affect anyone but the player it's set for (see class remarks).
        /// </summary>
        public double EffectiveWindFallCameraDampenClamp => UseLiveValue ? WindFallCameraDampenClamp : _snapWindFallCameraDampenClamp;

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="ClimbSheltersFromWind"/>.Value. Host-authoritative - flat
        /// (not preset/live-snapshot resolved, matching the raw config entry
        /// itself), but only the host's value counts, same as
        /// <see cref="EffectiveWindBackpackAlwaysImmune"/>: whether wind can push
        /// a climber is shared game logic, not local feel.
        /// </summary>
        /// <remarks>
        /// Folds the preset row in: the mechanic is off entirely under Subtle
        /// (<see cref="PresetCatalog.ClimbToCounterWind"/>), so the player-facing
        /// toggle can turn it off on top of that but can't turn it on there.
        /// Deliberately immediate rather than level-load-snapshotted, matching
        /// the flat toggle it gates.
        /// </remarks>
        public bool EffectiveClimbSheltersFromWind =>
            HostAuthority.Resolve(
                "ClimbSheltersFromWind",
                ClimbSheltersFromWind.Value && PresetCatalog.ClimbToCounterWind(Preset.Value));

        /// <summary>Game-facing code should read this instead of <see cref="ClimbWindSpeedMultiplier"/>. Host-authoritative.</summary>
        public double EffectiveClimbWindSpeedMultiplier =>
            HostAuthority.Resolve("ClimbWindSpeedMultiplier", UseLiveValue ? ClimbWindSpeedMultiplier : _snapClimbWindSpeedMultiplier);

        /// <summary>Game-facing code should read this instead of <see cref="ClimbWindUpwardSpeedMultiplier"/>. Host-authoritative.</summary>
        public double EffectiveClimbWindUpwardSpeedMultiplier =>
            HostAuthority.Resolve("ClimbWindUpwardSpeedMultiplier", UseLiveValue ? ClimbWindUpwardSpeedMultiplier : _snapClimbWindUpwardSpeedMultiplier);

        /// <summary>Game-facing code should read this instead of <see cref="ClimbWindIntoWindSpeedMultiplier"/>. Host-authoritative.</summary>
        public double EffectiveClimbWindIntoWindSpeedMultiplier =>
            HostAuthority.Resolve("ClimbWindIntoWindSpeedMultiplier", UseLiveValue ? ClimbWindIntoWindSpeedMultiplier : _snapClimbWindIntoWindSpeedMultiplier);

        /// <summary>Game-facing code should read this instead of <see cref="ClimbWindGraceForceMultiplier"/>. Host-authoritative.</summary>
        public double EffectiveClimbWindGraceForceMultiplier =>
            HostAuthority.Resolve("ClimbWindGraceForceMultiplier", UseLiveValue ? ClimbWindGraceForceMultiplier : _snapClimbWindGraceForceMultiplier);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="ClimbShelterGraceSeconds"/>.Value. Host-authoritative -
        /// flat (not preset/live-snapshot resolved, matching the raw config
        /// entry), but it decides how much force actually lands on a player, so
        /// only the host's value counts.
        /// </summary>
        public float EffectiveClimbShelterGraceSeconds =>
            HostAuthority.Resolve("ClimbShelterGraceSeconds", ClimbShelterGraceSeconds.Value);
    }
}
