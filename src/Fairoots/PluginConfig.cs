using BepInEx.Configuration;
using Fairoots.Core;
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
    /// <b>No balance value is written in this file.</b> Every default in the five
    /// gameplay sections comes from <see cref="ConfigDefaults"/>, generated from
    /// <c>docs/PRESETS.md</c> by <c>scripts/apply-presets.sh</c> - which is also
    /// where the rule those defaults encode is documented: <b>every default is the
    /// vanilla value</b>, so a player who installs the mod, selects the Custom
    /// preset and changes nothing plays exactly unmodded PEAK. The one documented
    /// exception is the gated parameters (a dial that means nothing until the
    /// mechanic it belongs to is switched on). Never hardcode a balance default
    /// here; tune the table and re-run the script.
    ///
    /// <c>General</c> and <c>Debug</c> are the exception to that: nothing in either
    /// is preset-driven or a balance number (the seed, the preset selector,
    /// keybinds, the client-side cosmetic settings, the diagnostics), so they are
    /// deliberately absent from the table and their defaults stay ordinary literals
    /// below.
    ///
    /// It follows that every gameplay setting outside <c>General</c>/<c>Debug</c> is
    /// preset-driven: if a mechanic is to be on under Balanced while its config entry
    /// defaults to vanilla, the preset row is the only thing that can turn it on. The
    /// pre-2026-07-30 category of "flat gameplay setting that applies the same under
    /// every preset" is therefore gone; what stays flat is only the <c>disable-*</c>
    /// kill switches (already vanilla when off, and no preset ever sets them), the
    /// keybinds, and <c>Debug</c>.
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
    /// never diverge from the host's.
    ///
    /// <b>Widened 2026-07-30 (maintainer's call): the rule is now every setting
    /// outside <c>General</c> and <c>Debug</c>, with no exceptions.</b> The two
    /// wind-preceded-fall settings (<see cref="EffectiveWindFallCameraDampenClamp"/>,
    /// <see cref="WindRecentForceWindowSeconds"/>) used to be excluded as "purely
    /// local camera feel" and no longer are - if a setting can change how the biome
    /// behaves for the player, a non-host flipping it does nothing until they are the
    /// host themselves. What stays per-client: everything in <c>Debug</c>, and
    /// <c>General</c>'s genuinely cosmetic/local entries -
    /// <see cref="RecolorSporeBombs"/>, <see cref="SporeAreaCloudOpacity"/>,
    /// <see cref="SporeBombCloudOpacity"/>,
    /// <see cref="ShowOverlayInSporeBombClouds"/>,
    /// <see cref="ShowSporeCloudLabel"/>, <see cref="ShowSpiderWarningLabel"/> and
    /// the cover-mouth keybind - since they only change what one player sees or
    /// presses. <c>General</c>'s seed and preset are host-decided by definition.
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

        /// <summary>
        /// How opaque the persistent spore areas' cloud VFX is drawn, as a fraction
        /// of what the artist authored (1.0 = vanilla). See
        /// <see cref="Core.SporeCloudOpacity"/> for why a *readability* setting
        /// belongs here at all and <c>SporeAreas/SporeCloudOpacityPatch</c> for how
        /// it's applied.
        ///
        /// <b>Client-side and cosmetic</b>, the same category as
        /// <see cref="RecolorSporeBombs"/> - it changes nothing about where the
        /// hazard is or what it does (that's
        /// <see cref="SporeAreaRadiusMultiplierOverride"/>'s and
        /// <see cref="SporeAreaStatusRateMultiplierOverride"/>'s business), only how
        /// densely one player's own screen draws it. So: no <c>Effective*</c>
        /// accessor, no host lookup, no level-load snapshot, and it always applies
        /// immediately regardless of <see cref="ApplyChangesLive"/>.
        /// </summary>
        public ConfigEntry<double> SporeAreaCloudOpacity { get; }

        /// <summary>
        /// The same translucency dial as <see cref="SporeAreaCloudOpacity"/>, for the
        /// temporary cloud a detonating spore bomb leaves behind. Split into its own
        /// setting because the two hazards read very differently on screen - a bomb's
        /// cloud is a brief burst right on top of the player, a spore area is a
        /// permanent landmark seen from a distance - so the density that makes one
        /// readable isn't automatically right for the other. Applied by
        /// <see cref="SporeBombs.SporeBombCloudOpacity"/>; client-side and cosmetic
        /// on the same terms as the spore-area one.
        /// </summary>
        public ConfigEntry<double> SporeBombCloudOpacity { get; }

        /// <summary>
        /// Whether the game's own "you are standing in spores" screen overlay is held
        /// up for as long as the local player is inside a spore bomb's cloud, the way
        /// it already is inside a persistent spore area.
        ///
        /// <b>This is a gap in the vanilla feedback, not a new effect.</b> The game
        /// has exactly one such warning (<c>GUIManager.sporesWarning</c>), but only
        /// <c>StatusEmitter</c> raises it - and a spore bomb's cloud isn't a
        /// <c>StatusEmitter</c>, it's a repeating <c>AOE</c> (see
        /// <see cref="SporeBombs.SporeBombDetonationMarker"/>). So standing in a
        /// bomb's cloud gives only the per-tick damage flash every couple of seconds,
        /// with nothing in between saying you're still in it. This raises the same
        /// warning the same way for the same reason; the per-tick flash is a separate
        /// overlay layer and is left exactly as the game plays it, so it still reads
        /// as a spike on top.
        ///
        /// Client-side and cosmetic, on the same terms as
        /// <see cref="SporeAreaCloudOpacity"/> - it shows the player something that
        /// was already true rather than changing it, and only on their own screen.
        /// </summary>
        public ConfigEntry<bool> ShowOverlayInSporeBombClouds { get; }

        /// <summary>
        /// Whether an on-screen text warning is shown while the local player is
        /// standing in spores - either hazard, see <see cref="SporePresence"/>.
        /// Applied by <see cref="Ui.SporeWarningLabel"/>.
        ///
        /// <b>Off by default, unlike the rest of this group.</b> The other
        /// readability settings make the game's <em>own</em> feedback legible - they
        /// thin a cloud the game already drew, or raise a warning the game already
        /// owns. This one adds a HUD element PEAK never had, which is a much louder
        /// intervention and a matter of taste rather than of fairness, so it's opt-in.
        ///
        /// Client-side and cosmetic on the same terms as
        /// <see cref="SporeAreaCloudOpacity"/>.
        /// </summary>
        public ConfigEntry<bool> ShowSporeCloudLabel { get; }

        /// <summary>
        /// The spider strike indicator: an on-screen warning while a spider is
        /// descending on you (<c>Ui/SpiderWarningLabel</c>).
        ///
        /// Off by default, matching <see cref="ShowSporeCloudLabel"/> and the rest of
        /// the label group: every setting that puts a HUD element on screen that PEAK
        /// doesn't have is opted into, not out of, regardless of how well it fills a
        /// gap. And the gap is real - vanilla gives a dropping spider no advance
        /// warning at all, since its only cue is a sound that plays in the same frame
        /// the drop starts and the grab that follows is instant on contact - which is
        /// what makes it worth offering, just not worth imposing. See
        /// <c>Creatures/SpiderStrikeWarning</c>.
        ///
        /// Purely cosmetic and per-client, like the spore label and the spore-bomb
        /// recolor: it changes what one player sees, not what happens.
        /// </summary>
        public ConfigEntry<bool> ShowSpiderWarningLabel { get; }

        // --- Spore-Bombs ---------------------------------------------------
        /// <summary>
        /// Custom-preset value for the spore-bomb total removal target. Only takes
        /// effect when <see cref="Preset"/> is set to <see cref="PresetId.Custom"/>
        /// (5) - ignored under presets 1-4, which always use their own catalog
        /// numbers regardless of this value. Defaults to Balanced's number.
        /// </summary>
        public ConfigEntry<double> SporeBombCullFractionOverride { get; }

        /// <summary>
        /// Custom-preset value for whether the bush/grass placement-removal pass
        /// runs (<see cref="Core.SporeBombCull"/>'s pass 1). Off = vanilla, which
        /// is the default; every preset 1-4 turns it on
        /// (<see cref="PresetCatalog.EnableFoliageRemoval"/>), since the game never
        /// prevents a spore bomb landing inside foliage and that gap is worth
        /// closing at any balance level. Only takes effect under
        /// <see cref="PresetId.Custom"/>; see
        /// <see cref="SporeBombCullFractionOverride"/>.
        ///
        /// Doesn't change how much gets removed overall:
        /// <see cref="SporeBombCullFractionOverride"/>'s target still applies, the
        /// seeded pass just picks from every candidate instead of only the
        /// camouflaged ones first.
        /// </summary>
        /// <remarks>
        /// Was the negative <c>disable-foliage-removal</c> until 2026-07-30, when
        /// the polarity was flipped so that "off = vanilla" holds for every
        /// setting in the mod (see <c>docs/PRESETS.md</c>). Existing config files
        /// carrying the old key are ignored and re-bound at the new one.
        /// </remarks>
        public ConfigEntry<bool> EnableFoliageRemovalOverride { get; }

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

        /// <summary>
        /// Key that covers the player's mouth against spore areas (default
        /// <see cref="KeyCode.X"/>). In <c>General</c> rather than
        /// <c>Spore-Areas</c> because it's a keybind, not a balance dial - it sits
        /// with the other whole-mod settings.
        ///
        /// <b>Deliberately per-client, not host-authoritative</b> (the maintainer's
        /// explicit call): which key a player presses, and whether they hold or
        /// toggle it, changes nothing about shared game state - the same reasoning
        /// that exempts <see cref="RecolorSporeBombs"/> and the wind-fall camera
        /// clamp. What the mechanic *costs* (<see cref="CoverMouthStaminaPerSecond"/>)
        /// is host-authoritative, because that is shared balance.
        /// </summary>
        public ConfigEntry<KeyCode> CoverMouthKey { get; }

        /// <summary>
        /// Whether <see cref="CoverMouthKey"/> must be held down (true, the default)
        /// or acts as a press-to-toggle (false). Per-client, same reasoning as
        /// <see cref="CoverMouthKey"/>.
        /// </summary>
        public ConfigEntry<bool> CoverMouthHold { get; }

        /// <summary>
        /// Whether covering your mouth may dip into bonus stamina (the temporary extra
        /// the game grants from food and similar) once ordinary stamina is spent. Off by
        /// default: bonus stamina is a scarce resource a player spent something to get,
        /// so quietly burning it to hold a breath - when the alternative is simply
        /// uncovering - is the kind of thing that should be opted into.
        ///
        /// Per-client, like the keybind and hold/toggle mode: it decides what *your own*
        /// action costs *you*, and nothing about anyone else's game. What the drain rate
        /// is remains host-authoritative
        /// (<see cref="CoverMouthStaminaPerSecond"/>).
        /// </summary>
        public ConfigEntry<bool> CoverMouthUseBonusStamina { get; }

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

        /// <summary>
        /// Custom-preset value for the multiplier applied to how fast the Spores
        /// status builds up while a player stands in a spore area
        /// (<see cref="Core.SporeAreaTuning.ScaleStatusRate"/>; vanilla is
        /// <c>amount = 0.025</c> per second in Roots). 1.0 = vanilla, 0 = the area
        /// never applies spores at all. Only takes effect under
        /// <see cref="PresetId.Custom"/>; see
        /// <see cref="SporeBombCullFractionOverride"/>.
        /// </summary>
        public ConfigEntry<double> SporeAreaStatusRateMultiplierOverride { get; }

        // --- Spores ---------------------------------------------------------
        // The two dials that act on the Spores *status* rather than on any one
        // hazard that applies it. Everything under Spore-Bombs and Spore-Areas
        // above changes a hazard; these change what having spores is like no
        // matter where they came from. See Core/SporeStatusTuning.cs for how the
        // two groups compose (they compound, on purpose).

        /// <summary>
        /// Custom-preset value for the multiplier on how long the Spores status takes
        /// to drain off a player once nothing is applying it any more - scaling both
        /// the drain rate (<c>CharacterAfflictions.sporesReductionPerSecond</c>) and
        /// the delay before draining starts (<c>sporesReductionCooldown</c>) so the
        /// multiplier reads as a multiplier on <em>time</em>: 0.5 means spores clear
        /// in half as long, 2.0 twice as long. See
        /// <see cref="Core.SporeStatusTuning.ScaleDecayRate"/> for why the rate is
        /// divided rather than multiplied, and
        /// <see cref="Core.SporeStatusTuning.ScaleDecayCooldown"/> for why the
        /// cooldown has to move with it. 1.0 = vanilla. Only takes effect under
        /// <see cref="PresetId.Custom"/>; see
        /// <see cref="SporeBombCullFractionOverride"/>.
        /// </summary>
        public ConfigEntry<double> SporeClearTimeMultiplierOverride { get; }

        /// <summary>
        /// Custom-preset value for the multiplier applied to <em>every</em> incoming
        /// Spores application, whatever applied it - spore areas, a spore bomb's
        /// cloud, a zombie's bite and the lingering affliction it leaves. Hooked at
        /// <c>CharacterAfflictions.AddStatus</c>, the one seam every spore source
        /// funnels through (see <see cref="Core.SporeStatusTuning.ScaleBuildUp"/>).
        /// 1.0 = vanilla, 0 = spores are never applied at all.
        ///
        /// <b>Compounds with the per-hazard rate dials</b> (notably
        /// <see cref="SporeAreaStatusRateMultiplierOverride"/>) rather than
        /// overriding them, since they scale a hazard's own emitter and this scales
        /// the result. That's why no shipped preset moves this one off 1.0 - see
        /// <c>PresetCatalog.SporeBuildUpMultiplier</c> - and why <b>its Custom default
        /// is 1.0 as well</b> (set 2026-07-30 at the maintainer's request): every other
        /// dial in the mod ships pre-tuned, but this one stacks on top of dials that
        /// already express the same intent, so it's the one setting that does nothing
        /// until a player deliberately reaches for it. Only takes effect under
        /// <see cref="PresetId.Custom"/>.
        /// </summary>
        public ConfigEntry<double> SporeBuildUpMultiplierOverride { get; }

        // --- Creatures ------------------------------------------------------
        /// <summary>
        /// Master kill switch for the Roots biome's NPC mushroom zombies. When on,
        /// no zombie is ever spawned and any already-live one is despawned -
        /// <c>Creatures/CreatureDisablePatch</c>'s <c>ZombieSpawnSuppressionPatch</c>
        /// does the work, at the game's own spawn loop rather than by writing over
        /// <c>ZombieManager.maxActiveZombies</c>.
        ///
        /// Deliberately scoped to <em>NPC</em> zombies: a zombie raised from a dead
        /// player is that player's death state, not an ambient hazard, and is never
        /// touched. <b>Host-authoritative</b> (read via
        /// <see cref="EffectiveDisableZombies"/>) - and unavoidably so, since vanilla
        /// only ever spawns zombies on the master client. Flat (no preset ever turns
        /// it on) and always immediate regardless of <see cref="ApplyChangesLive"/>,
        /// exactly like <see cref="DisableSporeAreas"/>.
        /// </summary>
        public ConfigEntry<bool> DisableZombies { get; }

        /// <summary>
        /// Master kill switch for the Roots biome's beetles (runtime-confirmed: ~15
        /// per level). When on, every beetle object is deactivated outright, so
        /// there's nothing to walk into and nothing to knock you off a ledge. Same
        /// shape as <see cref="DisableZombies"/>: <b>host-authoritative</b> (read via
        /// <see cref="EffectiveDisableBeetles"/>), flat, off by default, always
        /// immediate, and restorable - turning it back off restores exactly the
        /// beetles Fairoots hid.
        /// </summary>
        public ConfigEntry<bool> DisableBeetles { get; }

        /// <summary>
        /// Master kill switch for the Roots biome's ceiling spiders (runtime-confirmed:
        /// ~90 per level). When on, a spider never drops and never grabs, and its mesh
        /// and web are hidden.
        ///
        /// Note the asymmetry with <see cref="DisableBeetles"/>, explained in
        /// <c>Creatures/CreatureDisablePatch</c>: a spider's own distance culling
        /// re-drives its root GameObject's active state, so this suppresses the
        /// behavior at its two entry points and hides only the mesh child, rather
        /// than deactivating the root the way a beetle's is. Same
        /// <b>host-authoritative</b>/flat/off-by-default/immediate shape otherwise.
        /// </summary>
        public ConfigEntry<bool> DisableSpiders { get; }

        /// <summary>
        /// Custom-preset value for the multiplier applied to a mushroom zombie's
        /// movement speed. 1.0 = vanilla, 0 = a zombie that can still turn, aggro and
        /// bite but never closes any distance. Only takes effect under
        /// <see cref="PresetId.Custom"/>; see
        /// <see cref="SporeBombCullFractionOverride"/>.
        ///
        /// Scales <c>CharacterMovement.movementForce</c> (vanilla 10) on the zombie's
        /// own movement component - deliberately <em>not</em> the sibling
        /// <c>movementModifier</c> field, which the game's own energy-drink affliction
        /// adjusts additively; writing a computed value there would clobber it (the
        /// same trap already documented for <c>climbSpeedMod</c> in
        /// <c>ClimbWindShelterPatch</c>).
        /// </summary>
        public ConfigEntry<double> ZombieSpeedMultiplierOverride { get; }

        /// <summary>
        /// Custom-preset value for the multiplier applied to a beetle's movement
        /// speed (<c>Mob.movementSpeed</c>, vanilla 5). 1.0 = vanilla, 0 = a beetle
        /// that still turns to face you and still attacks if you walk into it, but
        /// never chases. Only takes effect under <see cref="PresetId.Custom"/>; see
        /// <see cref="SporeBombCullFractionOverride"/>.
        /// </summary>
        public ConfigEntry<double> BeetleSpeedMultiplierOverride { get; }

        /// <summary>
        /// Custom-preset value for the multiplier applied to how hard a beetle's hit
        /// throws you (<c>Beetle.bonkForce</c>/<c>bonkForceUp</c>, both vanilla 100).
        /// 1.0 = vanilla, 0 = the hit still lands (and still ragdolls you - that's a
        /// separate dial) but doesn't move you. Only takes effect under
        /// <see cref="PresetId.Custom"/>; see
        /// <see cref="SporeBombCullFractionOverride"/>.
        ///
        /// Both force components are scaled together so the shove keeps its vanilla
        /// angle - see <see cref="Core.CreatureTuning.ScaleKnockback"/>. There is no
        /// zombie equivalent because zombies have no scripted knockback to scale; see
        /// <c>Creatures/CreatureKnockbackPatch</c>.
        /// </summary>
        public ConfigEntry<double> BeetleKnockbackMultiplierOverride { get; }

        /// <summary>
        /// Custom-preset value for the multiplier applied to how long a beetle's or a
        /// zombie's hit keeps the player ragdolled (<c>Beetle.ragdollTime</c> 2s and
        /// <c>MushroomZombie.biteStunTime</c> 3s). 1.0 = vanilla; 0 = you are never
        /// knocked off your feet by either creature and always keep control. Only
        /// takes effect under <see cref="PresetId.Custom"/>; see
        /// <see cref="SporeBombCullFractionOverride"/>.
        ///
        /// One dial for both creatures deliberately - see
        /// <see cref="PresetCatalog.CreatureRagdollMultiplier"/>. This is also what
        /// stands in for a "zombie knockback" setting: zombies apply no scripted
        /// knockback at all, so their hit's actual cost to the player is this ragdoll
        /// (see <c>Creatures/CreatureKnockbackPatch</c>).
        /// </summary>
        public ConfigEntry<double> CreatureRagdollMultiplierOverride { get; }

        /// <summary>
        /// Custom-preset value for whether zombies can lose a target at all. Vanilla
        /// zombies never do (see <see cref="Core.ZombieDeaggro"/>), so this exists to
        /// let a Custom player keep that behavior; presets 1-4 decide it via
        /// <see cref="PresetCatalog.ZombieDeaggroEnabled"/>, which is off on Subtle
        /// only. Only takes effect under <see cref="PresetId.Custom"/>.
        /// </summary>
        public ConfigEntry<bool> ZombieDeaggroEnabledOverride { get; }

        /// <summary>
        /// Custom-preset value for how hard it is to shake a zombie.
        /// <b>Unlike every other multiplier in this config, 1.0 is not vanilla</b> -
        /// it's the toughest setting, and the range stops at 0.1 rather than 0. See
        /// <see cref="Core.ZombieDeaggro"/> for the full reasoning; the short version
        /// is that vanilla is "never deaggro", which no finite multiplier can express.
        /// Only takes effect under <see cref="PresetId.Custom"/>.
        /// </summary>
        public ConfigEntry<double> ZombieDeaggroMultiplierOverride { get; }

        /// <summary>
        /// Custom-preset value for how hard it is to shake a beetle - a multiplier on
        /// the distance at which it keeps a target it already has
        /// (<see cref="Core.CreatureTuning.ScaleDeaggroDistance"/>). 1.0 = vanilla
        /// here, unlike <see cref="ZombieDeaggroMultiplierOverride"/>, because beetles
        /// genuinely do deaggro in vanilla. Only takes effect under
        /// <see cref="PresetId.Custom"/>.
        /// </summary>
        public ConfigEntry<double> BeetleDeaggroMultiplierOverride { get; }

        /// <summary>
        /// Custom-preset value for how long a zombie is knocked out by a thrown item,
        /// in seconds. Vanilla already ragdolls a zombie for about a second when an
        /// item hits it (a zombie is a <c>Character</c>, so <c>Bonkable</c> finds
        /// it), so this extends an existing interaction rather than inventing one -
        /// see <see cref="Core.CreatureKnockout"/>. 0 = vanilla, and the default.
        /// Only takes effect under <see cref="PresetId.Custom"/>; see
        /// <see cref="SporeBombCullFractionOverride"/>.
        /// </summary>
        public ConfigEntry<double> ZombieKnockoutSecondsOverride { get; }

        /// <summary>
        /// Custom-preset value for how long a beetle is knocked onto its back by a
        /// thrown item, in seconds. Unlike the zombie's, this is an entirely new
        /// interaction: a beetle is a <c>Mob</c> with no <c>Character</c> and no
        /// <c>EventOnItemCollision</c>, so vanilla thrown items pass straight by it.
        /// 0 = vanilla, and the default.
        ///
        /// Shorter than <see cref="ZombieKnockoutSecondsOverride"/> on every preset,
        /// at the maintainer's direction - a beetle's shell should visibly shrug off
        /// a thrown rock better than a zombie or a spider does. Only takes effect
        /// under <see cref="PresetId.Custom"/>.
        /// </summary>
        public ConfigEntry<double> BeetleKnockoutSecondsOverride { get; }

        /// <summary>
        /// Custom-preset value for how fast a thrown item must be going, in
        /// <b>meters per second</b>, to knock out a beetle or a zombie. Shared by
        /// both so "a hard throw" means one thing. Only takes effect under
        /// <see cref="PresetId.Custom"/>.
        ///
        /// Exists because matching the game's own <c>Bonkable</c> threshold (5 world
        /// units/s) turned out to accept any contact at all - see
        /// <see cref="Core.CreatureKnockout.VanillaBonkableThresholdUnits"/>. 0
        /// restores that anything-goes behaviour for anyone who wants it. A gated
        /// parameter, so its default is the tuned number rather than a vanilla one:
        /// it means nothing until one of the two knockout durations is non-zero, and
        /// both of those default to 0.
        /// </summary>
        public ConfigEntry<double> CreatureKnockoutMinThrowSpeedOverride { get; }

        /// <summary>
        /// Custom-preset value for how close to the creature the thrower must have
        /// been, in <b>meters</b>, for a thrown item to knock it out. The second half
        /// of the mechanic's cost, alongside
        /// <see cref="CreatureKnockoutMinThrowSpeedOverride"/>: a hard throw is still
        /// travelling fast a long way out, so speed alone would license picking
        /// creatures off from safety. 0 removes the distance requirement.
        ///
        /// Measured from <c>Item.lastHolderCharacter</c> at the moment of impact.
        /// Gated on a non-zero knockout duration like the speed dial, and
        /// Custom-only like the rest of the group.
        /// </summary>
        public ConfigEntry<double> CreatureKnockoutMaxThrowDistanceOverride { get; }

        /// <summary>
        /// Custom-preset value for whether a blowgun dart takes a creature out of the
        /// fight: zombies die, spiders and beetles are stunned for
        /// <see cref="BlowgunCreatureStunSecondsOverride"/>. See
        /// <c>Creatures/BlowgunCreaturePatch</c> for why the outcomes differ (only the
        /// zombie has a death state to reach) and why vanilla darts can't hit a spider
        /// or beetle at all.
        ///
        /// Off = vanilla, and the default; on under every preset 1-4, because the dart
        /// is a consumable fired from an uncommon item so the mechanic is
        /// self-limiting, and its whole purpose is to give the blowgun a use against
        /// creatures it currently passes straight through. Only takes effect under
        /// <see cref="PresetId.Custom"/>.
        /// </summary>
        public ConfigEntry<bool> BlowgunAffectsCreaturesOverride { get; }

        /// <summary>
        /// Custom-preset value for how long a blowgun dart stuns a spider or a beetle,
        /// in seconds. Zombies aren't covered by this - they die outright, which has no
        /// duration - so turning this to 0 leaves darts lethal to zombies while
        /// harmless to the other two. Gated by
        /// <see cref="BlowgunAffectsCreaturesOverride"/>, so its default is the tuned
        /// number rather than a vanilla one. Only takes effect under
        /// <see cref="PresetId.Custom"/>.
        /// </summary>
        public ConfigEntry<double> BlowgunCreatureStunSecondsOverride { get; }

        /// <summary>
        /// Custom-preset value for the multiplier on the wind force a zombie receives.
        /// 1.0 = vanilla, which is already nonzero: a zombie is a bot
        /// <c>Character</c>, so the game pushes it at 0.6x what it pushes a player (see
        /// <see cref="Core.CreatureWind"/>). Above 1.0 the wind shoves zombies harder
        /// than the game does, which is the direction the presets run. Only takes
        /// effect under <see cref="PresetId.Custom"/>.
        /// </summary>
        public ConfigEntry<double> ZombieWindMultiplierOverride { get; }

        /// <summary>
        /// Custom-preset value for how susceptible beetles are to wind, as a fraction
        /// of their own walking speed - so 1.0 means wind slides a beetle about as fast
        /// as it walks. <b>0 is vanilla</b>, not 1.0, because vanilla beetles are
        /// completely wind-immune and cannot be made otherwise by scaling:
        /// <c>Mob.FixedUpdate</c> zeroes their velocity every tick. Any positive value
        /// grants an effect the game never had. Only takes effect under
        /// <see cref="PresetId.Custom"/>.
        /// </summary>
        public ConfigEntry<double> BeetleWindSusceptibilityOverride { get; }

        /// <summary>
        /// Custom-preset value for whether the cover-your-mouth mechanic exists at
        /// all. Off = vanilla (the key does nothing: no immunity, no stamina drain,
        /// no hand restrictions, no pose), which is the default; every preset 1-4
        /// turns it on (<see cref="PresetCatalog.EnableCoverMouth"/>). Only takes
        /// effect under <see cref="PresetId.Custom"/>; read via
        /// <see cref="EffectiveEnableCoverMouth"/>, which is host-authoritative -
        /// unlike the keybind and hold/toggle mode, which are the player's own
        /// business, whether a counterplay mechanic <em>exists in this run</em> is
        /// shared balance.
        ///
        /// A player who simply doesn't want the mechanic for themselves can set
        /// <see cref="CoverMouthKey"/> to <see cref="KeyCode.None"/> instead; that
        /// needs no host cooperation, because opting out of a move you could make is
        /// not altering anyone else's game.
        /// </summary>
        /// <remarks>
        /// Was the negative <c>disable-cover-mouth</c> until 2026-07-30 - see
        /// <see cref="EnableFoliageRemovalOverride"/>'s remarks for why the polarity
        /// was flipped.
        /// </remarks>
        public ConfigEntry<bool> EnableCoverMouthOverride { get; }

        /// <summary>
        /// Custom-preset value for the stamina drained per second while holding a
        /// mouth cover: what the counterplay move costs, which is shared balance
        /// unlike the keybind that triggers it. Only takes effect under
        /// <see cref="PresetId.Custom"/>; see
        /// <see cref="SporeBombCullFractionOverride"/>. 0 makes covering free - for
        /// scale, vanilla wall climbing costs up to 0.2/s.
        ///
        /// Its default is the tuned cost rather than a vanilla value, because
        /// vanilla has no cover-mouth mechanic to have a cost: it is gated behind
        /// <see cref="EnableCoverMouthOverride"/>, which is what defaults to off.
        /// See <c>docs/PRESETS.md</c>'s note on gated parameters.
        /// </summary>
        public ConfigEntry<float> CoverMouthStaminaPerSecondOverride { get; }


        /// <summary>
        /// Custom-preset value for whether covering your mouth also blocks the spore
        /// status from a spore bomb's temporary mini spore cloud, on top of the
        /// biome's persistent spore areas. Off = vanilla, and off on every preset
        /// today - see <c>SporeBombs/CoverMouthSporeBombPatch</c> for why the
        /// mechanic is scoped to spore areas. Only the spore status is ever
        /// suppressed either way: knockback and screen shake still land. Only takes
        /// effect under <see cref="PresetId.Custom"/>.
        /// </summary>
        public ConfigEntry<bool> CoverMouthBlocksSporeBombsOverride { get; }

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
        /// Custom-preset value for whether backpacks are fully immune to wind force
        /// regardless of <see cref="WindItemForceMultiplierOverride"/>. Off =
        /// vanilla, and the default; on under every preset 1-4 (ROADMAP.md's
        /// "backpack only" is the minimum immunity level). Turn it off to have
        /// backpacks blown around like any other ground item (scaled by the same
        /// item-force multiplier as everything else). Only takes effect under
        /// <see cref="PresetId.Custom"/>; read via
        /// <see cref="PluginConfig.EffectiveWindBackpackAlwaysImmune"/>, which is
        /// host-authoritative and always applies immediately regardless of
        /// <see cref="ApplyChangesLive"/>.
        /// </summary>
        public ConfigEntry<bool> WindBackpackAlwaysImmuneOverride { get; }

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
        /// Custom-preset value for how many seconds after wind force was last
        /// applied to the local character a subsequent fall still counts as
        /// "wind-preceded" - for both the camera-dampening clamp above and
        /// <see cref="PreventWindRagdollOverride"/>. A gated parameter (a timing
        /// window for two mechanics that both default to off), so its default is
        /// the tuned number rather than a vanilla one. Only takes effect under
        /// <see cref="PresetId.Custom"/>; read via
        /// <see cref="EffectiveWindRecentForceWindowSeconds"/>.
        /// </summary>
        public ConfigEntry<float> WindRecentForceWindowSecondsOverride { get; }

        /// <summary>
        /// Custom-preset value for whether wind is allowed to ragdoll the player at
        /// all. On (every preset 1-4): a fall that wind caused
        /// (<see cref="Core.WindTuning.IsWindForceStillRecent"/>, same window as the
        /// camera clamp) keeps the player fully in control instead of collapsing
        /// into physics - <see cref="Core.WindTuning.ApplyWindRagdollImmunity"/>.
        /// Off = vanilla, and the default: wind blowing you off a ledge ragdolls
        /// you, and only the partial
        /// <see cref="WindFallCameraDampenClampOverride"/> floor applies. Only takes
        /// effect under <see cref="PresetId.Custom"/>; host-authoritative, since it
        /// decides whether a player keeps control of their character.
        /// </summary>
        public ConfigEntry<bool> PreventWindRagdollOverride { get; }

        /// <summary>
        /// Custom-preset value for whether holding onto something (wall climbing, a
        /// rope, a vine, a climb handle) makes the player fully immune to wind
        /// force, at the cost of climbing slower while the wind is actually pushing
        /// on them - see <see cref="Core.ClimbWindResistance"/> for why this is a
        /// real mechanic rather than the vanilla behavior an earlier research pass
        /// thought it was. Off = vanilla, and the default; on under every preset
        /// except Subtle (<see cref="PresetCatalog.ClimbSheltersFromWind"/>). Only
        /// takes effect under <see cref="PresetId.Custom"/>; read via
        /// <see cref="PluginConfig.EffectiveClimbSheltersFromWind"/>. The three
        /// speed multipliers below are what it costs; this is whether it happens.
        /// </summary>
        public ConfigEntry<bool> ClimbSheltersFromWindOverride { get; }

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
        /// Custom-preset value for how long the much-weaker-wind grace window lasts
        /// after letting go of a climb (see
        /// <see cref="Core.ClimbWindResistance.GraceForceMultiplier"/> for why it
        /// exists). A gated parameter, like
        /// <see cref="WindRecentForceWindowSecondsOverride"/>: it only has any effect
        /// while the climb shelter itself is on
        /// (<see cref="ClimbSheltersFromWindOverride"/>), so its default is the tuned
        /// number rather than a vanilla one. Only takes effect under
        /// <see cref="PresetId.Custom"/>; read via
        /// <see cref="PluginConfig.EffectiveClimbShelterGraceSeconds"/>.
        /// </summary>
        public ConfigEntry<float> ClimbShelterGraceSecondsOverride { get; }

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

        // --- Cover-mouth pose tuning (Debug) ------------------------------
        // Six knobs for one pose, which needs justifying: the pose is pure
        // visuals, so the only way to judge it is to look at it, and every
        // value here (which clip, which frame of it, where the hands sit) is
        // Unity *asset*-dependent - none of it can be derived from the
        // decompiled code or checked by a test. Live knobs turn "rebuild,
        // relaunch, load a run, look" into "drag a slider," which is why they
        // live in Debug rather than being hardcoded constants. Expect them to
        // collapse into constants once the pose is locked in.

        /// <summary>
        /// Holds the cover-mouth pose on permanently so it can be tuned without keeping
        /// a key held. <b>Visual only</b> - no immunity, no stamina drain, no hands-busy
        /// restrictions, and nothing is published to other players; the mechanic itself
        /// is untouched. Local player only.
        /// </summary>
        public ConfigEntry<bool> CoverMouthPosePreview { get; }

        /// <summary>Animator state to freeze as the cover-mouth pose. Empty = auto-detect the "it's so over" emote.</summary>
        public ConfigEntry<string> CoverMouthPoseEmote { get; }

        /// <summary>Normalised time (0-1) within the pose clip to freeze on.</summary>
        public ConfigEntry<float> CoverMouthPoseEmoteTime { get; }

        /// <summary>How far in front of the head the hands sit, in centimetres.</summary>
        public ConfigEntry<float> CoverMouthPoseForwardCm { get; }

        /// <summary>How far below the head (mouth height) the hands sit, in centimetres.</summary>
        public ConfigEntry<float> CoverMouthPoseBelowHeadCm { get; }

        /// <summary>Vertical gap between the two stacked hands, in centimetres.</summary>
        public ConfigEntry<float> CoverMouthPoseHandGapCm { get; }

        /// <summary>Sideways offset of each hand from the centre line, in centimetres.</summary>
        public ConfigEntry<float> CoverMouthPoseSideCm { get; }

        /// <summary>Extra hand turn about the body's vertical axis, in degrees, mirrored per hand (thumbs out vs. thumbs to the face).</summary>
        public ConfigEntry<float> CoverMouthPoseHandYawDeg { get; }

        /// <summary>Extra hand tilt about the body's forward axis, in degrees, mirrored per hand.</summary>
        public ConfigEntry<float> CoverMouthPoseHandRollDeg { get; }

        /// <summary>Extra hand pitch about the body's sideways axis, in degrees (not mirrored - both hands pitch the same way).</summary>
        public ConfigEntry<float> CoverMouthPoseHandPitchDeg { get; }

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

            SporeAreaCloudOpacity = config.Bind(
                "General",
                "spore-area-cloud-opacity",
                0.35,
                new ConfigDescription(
                    "How opaque the mushroom spore clouds are drawn, as a fraction of vanilla " +
                    "(1.0 = untouched, 0 = fully invisible). Lower makes them see-through, so the " +
                    "game's own green Spores screen overlay is readable through them - in vanilla " +
                    "the cloud and the overlay are the same colour and blend together, so it's " +
                    "hard to tell whether you're actually inside one and taking spores or just " +
                    "standing next to it. The cloud stays visible enough to spot and walk around; " +
                    "the hazard itself (its size and how fast it applies spores) is NOT changed - " +
                    "that's what the Spore-Areas settings are for. Purely cosmetic and PER-PLAYER: " +
                    "not host-authoritative, set it however you like. Applies immediately, " +
                    "regardless of apply-changes-live.",
                    new AcceptableValueRange<double>(0.0, 1.0)));

            SporeBombCloudOpacity = config.Bind(
                "General",
                "spore-bomb-cloud-opacity",
                0.5,
                new ConfigDescription(
                    "The same see-through-cloud setting as spore-area-cloud-opacity, but for the " +
                    "temporary spore cloud a spore bomb leaves when it goes off (1.0 = vanilla, " +
                    "0 = fully invisible). Separate from the spore-area one because a bomb's cloud " +
                    "erupts right on top of you while a spore area is a landmark seen from a " +
                    "distance. Cosmetic only - the blast, the knockback and the spores it applies " +
                    "are unchanged. Purely cosmetic and PER-PLAYER: not host-authoritative. " +
                    "Applies immediately, regardless of apply-changes-live.",
                    new AcceptableValueRange<double>(0.0, 1.0)));

            ShowOverlayInSporeBombClouds = config.Bind(
                "General",
                "show-overlay-in-spore-bomb-clouds",
                true,
                "Keeps the game's green 'you're standing in spores' screen overlay up for as " +
                "long as you're inside the cloud a spore bomb leaves behind - the same way it " +
                "already stays up inside a mushroom spore cloud. In vanilla a bomb's cloud only " +
                "gives you the brief flash each time it damages you, with nothing in between " +
                "telling you you're still standing in it. The damage flash is untouched, so it " +
                "still stands out on top of the steady overlay. Purely a readability fix and " +
                "PER-PLAYER: not host-authoritative, and it changes nothing about the cloud " +
                "itself. Applies immediately, regardless of apply-changes-live.");

            ShowSporeCloudLabel = config.Bind(
                "General",
                "show-spore-cloud-label",
                false,
                "Shows a text warning on screen, in the game's own font and in the Spores " +
                "status colour, whenever you're standing in spores - both the mushroom spore " +
                "clouds and the cloud a spore bomb leaves. Off by default: the other " +
                "readability settings just make the game's own feedback legible, while this " +
                "adds a HUD element PEAK doesn't have, which is more a matter of taste. " +
                "Purely cosmetic and PER-PLAYER: not host-authoritative. Applies immediately, " +
                "regardless of apply-changes-live.");

            ShowSpiderWarningLabel = config.Bind(
                "General",
                "show-spider-warning-label",
                false,
                "Shows a text warning on screen, in the game's own font and in the Poison status " +
                "colour, while a spider is dropping down on you, and hides it again about a " +
                "second after the spider lands without catching anyone. Worth turning on if " +
                "spiders keep getting you from above: the game gives you no warning at all for " +
                "this - the only spider sound plays at the same moment the drop starts, and " +
                "being grabbed is instant on contact - so this turns the time the spider spends " +
                "descending into time you can actually react in. Off by default like the other " +
                "on-screen labels, since it adds a HUD element PEAK doesn't have. Purely " +
                "cosmetic and PER-PLAYER: not host-authoritative. Applies immediately, " +
                "regardless of apply-changes-live.");

            SporeBombCullFractionOverride = config.Bind(
                "Spore-Bombs",
                "cull-fraction",
                ConfigDefaults.SporeBombCullFraction,
                new ConfigDescription(
                    "Fraction of spore bombs to remove overall (foliage removal + seeded cull " +
                    "combined), e.g. 0.5 cuts them in half. Only takes effect when preset is " +
                    "set to Custom (5) - ignored under presets 1-4. 0 (the default) = remove " +
                    "none beyond whatever the foliage pass takes, which is itself off by " +
                    "default, so an untouched Custom preset removes nothing at all.",
                    new AcceptableValueRange<double>(0.0, 1.0)));

            EnableFoliageRemovalOverride = config.Bind(
                "Spore-Bombs",
                "enable-foliage-removal",
                ConfigDefaults.EnableFoliageRemoval,
                "Removes spore bombs that spawned hidden inside a bush or clump of ferns, " +
                "because a bomb you physically cannot see before setting it off isn't a " +
                "hazard you can play around. Off = vanilla, where the game leaves them " +
                "wherever they landed. Turning it on does NOT mean fewer spore bombs " +
                "overall than cull-fraction asks for: that removal target still applies, " +
                "the seeded removal just picks the hidden ones first. Only takes effect " +
                "when preset is set to Custom (5) - every preset 1-4 has it on. " +
                "HOST-AUTHORITATIVE: only the host's value counts for the whole lobby. " +
                "Takes effect on the next Roots level load (spore-bomb removal happens " +
                "once, when the biome is placed).");

            SporeBombTriggerRadiusMultiplierOverride = config.Bind(
                "Spore-Bombs",
                "trigger-radius-multiplier",
                ConfigDefaults.SporeBombTriggerRadiusMultiplier,
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
                ConfigDefaults.SporeBombKnockbackMultiplier,
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
                ConfigDefaults.SporeBombScreenshakeRangeCapMeters,
                new ConfigDescription(
                    "Caps how far away (in meters) a spore-bomb detonation's screen-shake can " +
                    "still be felt. 0 leaves the vanilla range (~75m, uncapped) alone; a " +
                    "positive value caps it, e.g. 20 means no shake past 20m. Only takes " +
                    "effect when preset is set to Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.0, 100.0)));

            SporeBombVfxCountMultiplierOverride = config.Bind(
                "Spore-Bombs",
                "vfx-count-multiplier",
                ConfigDefaults.SporeBombVfxCountMultiplier,
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
                ConfigDefaults.SporeBombTriggerHeightMultiplier,
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
                ConfigDefaults.SporeBombSporeAreaRadiusMultiplier,
                new ConfigDescription(
                    "Multiplier applied to the radius (and proportionally, the inner/outer " +
                    "fade) of the temporary spore area a regular \"Spore Bomb\"/\"Poison Spore " +
                    "Bomb\" creates when triggered, e.g. 0.5 halves how far it reaches, 2.0 " +
                    "doubles it. 1.0 always means vanilla size. Doesn't affect the \"Explosive " +
                    "Spore Bomb\" variant, which has no spore area. Only takes effect when " +
                    "preset is set to Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.0, 5.0)));

            CoverMouthBlocksSporeBombsOverride = config.Bind(
                "Spore-Bombs",
                "cover-mouth-blocks-spore-bombs",
                ConfigDefaults.CoverMouthBlocksSporeBombs,
                "Whether covering your mouth (see General/cover-mouth-key) also protects you from " +
                "the small spore cloud a spore bomb leaves behind when it goes off. Off by " +
                "default: the mechanic is meant for the biome's spore areas, which you can see " +
                "coming and choose to walk into, whereas a spore bomb is a surprise you've " +
                "already set off. Either way this only stops the spores - the blast still knocks " +
                "you around. Only takes effect when preset is set to Custom (5) - it is off " +
                "under presets 1-4 as well, for now. HOST-AUTHORITATIVE: only the host's value " +
                "counts for the whole lobby.");

            DisableSporeAreas = config.Bind(
                "Spore-Areas",
                "disable-spore-areas",
                ConfigDefaults.DisableSporeAreas,
                "Master switch: when on, the Roots biome's spore areas (\"Mushroom Spore Clouds\") " +
                "are removed entirely - no Spores status, no green screen filter, and the emitter " +
                "mushroom in the middle of the cloud plus the cloud itself disappear with them. " +
                "Doesn't touch the small temporary spore area a spore bomb leaves behind when it " +
                "goes off (that's the Spore-Bombs section). HOST-AUTHORITATIVE: if you're not the " +
                "host, this has no effect at all - only the host's value counts for the whole " +
                "lobby. Off by default; no preset ever turns this on automatically. Applies " +
                "immediately, regardless of apply-changes-live.");

            CoverMouthKey = config.Bind(
                "General",
                "cover-mouth-key",
                KeyCode.X,
                "Key to cover your mouth with both hands, making you immune to spore areas while " +
                "you hold it. Your hands are busy while covering: you can't climb, pick things up, " +
                "or switch items, and anything you're holding is put away (an item you're carrying " +
                "in your hands with no free pocket for it gets dropped). Set to None to disable " +
                "the mechanic entirely. PER-PLAYER: this and cover-mouth-hold below are yours " +
                "alone - unlike most settings, the host's value has no effect on you (how much " +
                "stamina it costs IS host-controlled, though).");

            CoverMouthHold = config.Bind(
                "General",
                "cover-mouth-hold",
                true,
                "How cover-mouth-key behaves: on (default) means hold the key to keep your mouth " +
                "covered and let go to stop; off makes it a toggle - press once to start, press " +
                "again to stop. PER-PLAYER, same as cover-mouth-key above.");

            CoverMouthUseBonusStamina = config.Bind(
                "General",
                "cover-mouth-use-bonus-stamina",
                false,
                "Whether covering your mouth is allowed to eat into your bonus stamina (the extra " +
                "you get from food) once your normal stamina runs out. Off by default - covering " +
                "simply stops when you run out of normal stamina, leaving your bonus intact for " +
                "climbing. PER-PLAYER: this is your own call, like the keybind; how fast covering " +
                "drains stamina in the first place is still the host's setting.");

            SporeAreaRemovalFractionOverride = config.Bind(
                "Spore-Areas",
                "removal-fraction",
                ConfigDefaults.SporeAreaRemovalFraction,
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
                ConfigDefaults.SporeAreaRadiusMultiplier,
                new ConfigDescription(
                    "Multiplier applied to how far every spore area reaches, e.g. 0.7 shrinks it " +
                    "to 70% of vanilla, 1.5 makes it half again as big. The visible cloud is " +
                    "resized to match, so what you can see is what actually gives you spores. " +
                    "1.0 always means vanilla size (radius 16, about 26m across from the middle). " +
                    "How quickly the spores themselves are applied is a separate setting - this " +
                    "only changes the size. Only takes effect when preset is set to Custom (5) - " +
                    "ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.0, 3.0)));

            SporeAreaStatusRateMultiplierOverride = config.Bind(
                "Spore-Areas",
                "status-rate-multiplier",
                ConfigDefaults.SporeAreaStatusRateMultiplier,
                new ConfigDescription(
                    "Multiplier applied to how often/quickly you're given spores while standing " +
                    "in a spore area, e.g. 0.5 means the Spores meter fills half as fast, 2.0 " +
                    "twice as fast, 0 means a spore area never gives you spores at all (its cloud " +
                    "is still there - use disable-spore-areas to remove them outright). 1.0 always " +
                    "means vanilla. How far the area reaches is the separate radius-multiplier " +
                    "above; this only changes the rate. Only takes effect when preset is set to " +
                    "Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.0, 3.0)));

            SporeClearTimeMultiplierOverride = config.Bind(
                "Spores",
                "clear-time-multiplier",
                ConfigDefaults.SporeClearTimeMultiplier,
                new ConfigDescription(
                    "Multiplier applied to how long it takes for spores to wear off once you're " +
                    "out of them, e.g. 0.5 means they clear in half the time, 2.0 means twice as " +
                    "long. Covers the whole wait, including the short pause before the meter " +
                    "starts going down at all. 1.0 always means vanilla. This only affects " +
                    "recovery - how fast you GET spores is build-up-multiplier below. Only takes " +
                    "effect when preset is set to Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.05, 5.0)));

            SporeBuildUpMultiplierOverride = config.Bind(
                "Spores",
                "build-up-multiplier",
                ConfigDefaults.SporeBuildUpMultiplier,
                new ConfigDescription(
                    "Multiplier applied to every dose of spores you're given, no matter what gave " +
                    "it to you - a spore area, a spore bomb's cloud, or a zombie's bite. 0.5 " +
                    "means every dose counts for half, 0 means you never get spores at all, 2.0 " +
                    "means double. 1.0 always means vanilla. NOTE: this stacks on top of the " +
                    "per-hazard settings - if Spore-Areas/status-rate-multiplier is 0.5 and this " +
                    "is 0.5, standing in a spore area gives you a quarter of vanilla. Because of " +
                    "that this one is fully OPT-IN: it defaults to 1.0 (vanilla) and no preset " +
                    "changes it, so it does nothing at all until you set it yourself. The presets " +
                    "tune each hazard separately instead. Only takes effect when preset is set to " +
                    "Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.0, 3.0)));

            DisableZombies = config.Bind(
                "Creatures",
                "disable-zombies",
                ConfigDefaults.DisableZombies,
                "Master switch: when on, the Roots biome's mushroom zombies never spawn, and any " +
                "already wandering around are removed. A zombie raised from a dead player is NOT " +
                "affected - that's a player's death, not a hazard. HOST-AUTHORITATIVE: if you're " +
                "not the host, this has no effect at all - only the host's value counts for the " +
                "whole lobby (the game only ever spawns zombies on the host in the first place). " +
                "Off by default; no preset ever turns this on automatically. Applies immediately, " +
                "regardless of apply-changes-live.");

            DisableBeetles = config.Bind(
                "Creatures",
                "disable-beetles",
                ConfigDefaults.DisableBeetles,
                "Master switch: when on, the Roots biome's beetles (about 15 per level) are removed " +
                "entirely - nothing to walk into, nothing to knock you off a ledge. Turning it back " +
                "off brings back exactly the beetles this mod removed. HOST-AUTHORITATIVE: if " +
                "you're not the host, this has no effect at all - only the host's value counts for " +
                "the whole lobby. Off by default; no preset ever turns this on automatically. " +
                "Applies immediately, regardless of apply-changes-live.");

            DisableSpiders = config.Bind(
                "Creatures",
                "disable-spiders",
                ConfigDefaults.DisableSpiders,
                "Master switch: when on, the Roots biome's ceiling spiders (about 90 per level) " +
                "never drop and never grab you, and their webs and bodies are hidden. " +
                "HOST-AUTHORITATIVE: if you're not the host, this has no effect at all - only the " +
                "host's value counts for the whole lobby. Off by default; no preset ever turns this " +
                "on automatically. Applies immediately, regardless of apply-changes-live.");

            ZombieSpeedMultiplierOverride = config.Bind(
                "Creatures",
                "zombie-speed-multiplier",
                ConfigDefaults.ZombieSpeedMultiplier,
                new ConfigDescription(
                    "Multiplier applied to how fast mushroom zombies move, e.g. 0.5 makes them " +
                    "half as fast, 1.5 half again as fast. 1.0 always means vanilla. Affects both " +
                    "their walk and their sprint, since the sprint is a multiple of the same " +
                    "speed. 0 means a zombie can still notice you, turn towards you and bite you " +
                    "if you stand next to it, but never closes any distance. Only takes effect " +
                    "when preset is set to Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.0, 3.0)));

            BeetleSpeedMultiplierOverride = config.Bind(
                "Creatures",
                "beetle-speed-multiplier",
                ConfigDefaults.BeetleSpeedMultiplier,
                new ConfigDescription(
                    "Multiplier applied to how fast beetles move, e.g. 0.5 makes them half as " +
                    "fast, 1.5 half again as fast. 1.0 always means vanilla. 0 means a beetle " +
                    "still turns to face you and still hits you if you walk into it, but never " +
                    "chases. Only takes effect when preset is set to Custom (5) - ignored under " +
                    "presets 1-4.",
                    new AcceptableValueRange<double>(0.0, 3.0)));

            BeetleKnockbackMultiplierOverride = config.Bind(
                "Creatures",
                "beetle-knockback-multiplier",
                ConfigDefaults.BeetleKnockbackMultiplier,
                new ConfigDescription(
                    "Multiplier applied to how hard a beetle's hit throws you, e.g. 0.5 halves " +
                    "the shove, 0 means it doesn't move you at all. 1.0 always means vanilla. " +
                    "This is only the force - whether the hit knocks you off your feet is the " +
                    "separate creature-ragdoll-resistance-multiplier below. Zombies have no " +
                    "equivalent setting because the game gives them no knockback of their own: " +
                    "what a zombie does is ragdoll you (see that other setting) and shove you " +
                    "with its body, which is ordinary physics. Only takes effect when preset is " +
                    "set to Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.0, 3.0)));

            CreatureRagdollMultiplierOverride = config.Bind(
                "Creatures",
                "creature-ragdoll-multiplier",
                ConfigDefaults.CreatureRagdollMultiplier,
                new ConfigDescription(
                    "Multiplier applied to how long a beetle's hit or a zombie's bite knocks you " +
                    "off your feet, e.g. 0.5 gets you back up twice as fast. 0 means you are " +
                    "never ragdolled by either creature and always keep control. 1.0 always means " +
                    "vanilla (2 seconds for a beetle, 3 for a zombie bite). The hit still lands " +
                    "either way: you still take the injury and spores, and a beetle still shoves " +
                    "you (that's beetle-knockback-multiplier above) - this only decides whether " +
                    "you lose control of your character. Zombies have no separate knockback " +
                    "setting because this ragdoll IS what a zombie's hit costs you. Only takes " +
                    "effect when preset is set to Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.0, 3.0)));

            ZombieDeaggroEnabledOverride = config.Bind(
                "Creatures",
                "zombie-deaggro-enabled",
                ConfigDefaults.ZombieDeaggroEnabled,
                "Whether zombies can ever lose track of you. In the unmodded game they CANNOT: " +
                "once a zombie has seen you it chases you forever, at any distance, with no way " +
                "to shake it. Turn this off to keep that vanilla behavior. Only takes effect when " +
                "preset is set to Custom (5) - under presets 1-4 this is off on Subtle (which is " +
                "meant to be vanilla) and on for the other three.");

            ZombieDeaggroMultiplierOverride = config.Bind(
                "Creatures",
                "zombie-deaggro-multiplier",
                ConfigDefaults.ZombieDeaggroMultiplier,
                new ConfigDescription(
                    "How hard it is to shake a zombie once it's after you. NOTE: unlike every " +
                    "other multiplier in this file, 1.0 does NOT mean vanilla - vanilla zombies " +
                    "never lose you at all, so there's no vanilla number to scale. Here 1.0 is " +
                    "the TOUGHEST setting (you must stay out of the zombie's sight for a full 30 " +
                    "seconds, or get about 120m away) and 0.1 is the most forgiving (about 3 " +
                    "seconds or 12m). Either escape works on its own. 0 is not allowed - a zombie " +
                    "that deaggros instantly is a disabled zombie, which is what disable-zombies " +
                    "is for. Only takes effect when preset is set to Custom (5) - ignored under " +
                    "presets 1-4.",
                    new AcceptableValueRange<double>(0.1, 1.0)));

            BeetleDeaggroMultiplierOverride = config.Bind(
                "Creatures",
                "beetle-deaggro-multiplier",
                ConfigDefaults.BeetleDeaggroMultiplier,
                new ConfigDescription(
                    "How hard it is to shake a beetle once it's after you, as a multiplier on the " +
                    "distance it will keep chasing you from. 1.0 means vanilla (about 22m in " +
                    "Roots), 0.5 is " +
                    "twice as easy to escape, 2.0 twice as hard. Unlike zombies, beetles DO give " +
                    "up on their own in the unmodded game - both by distance and by losing sight " +
                    "of you - so this only tunes how sticky that already is. It doesn't change how " +
                    "far away a beetle can first notice you. Once a beetle does give up, it " +
                    "ignores everyone for 5 seconds, so it can't instantly re-notice you. 0.1 is " +
                    "the minimum - a beetle that can never hold a target at all is a disabled " +
                    "beetle, which is what disable-beetles is for. Only takes effect when preset " +
                    "is set to Custom (5) - ignored under presets 1-4.",
                    new AcceptableValueRange<double>(0.1, 3.0)));

            ZombieKnockoutSecondsOverride = config.Bind(
                "Creatures",
                "zombie-knockout-seconds",
                ConfigDefaults.ZombieKnockoutSeconds,
                new ConfigDescription(
                    "How many seconds a zombie is knocked out for when you hit it with a thrown " +
                    "item, the way you can already stun a spider by throwing something at it. " +
                    "The unmodded game already knocks a zombie down for about a second, so this " +
                    "mostly decides how long that lasts; 0 (the default) leaves the vanilla " +
                    "behaviour alone. " +
                    "Only takes effect when preset is set to Custom (5) - ignored under " +
                    "presets 1-4. " +
                    "Deliberately less than the 5 seconds a spider gets. Note the zombie also " +
                    "needs a moment to get up and re-orient afterwards, so the total time it's " +
                    "out of the fight is a few seconds longer than this. HOST-AUTHORITATIVE: " +
                    "only the host's value counts for the whole lobby.",
                    new AcceptableValueRange<double>(0.0, 60.0)));

            BeetleKnockoutSecondsOverride = config.Bind(
                "Creatures",
                "beetle-knockout-seconds",
                ConfigDefaults.BeetleKnockoutSeconds,
                new ConfigDescription(
                    "How many seconds a beetle is knocked onto its back for when you hit it with " +
                    "a thrown item. Unlike zombies and spiders, beetles are completely immune to " +
                    "thrown items in the unmodded game - nothing happens at all - so this adds " +
                    "the interaction outright; 0 (the default) turns it back off. " +
                    "Only takes effect when preset is set to Custom (5) - ignored under " +
                    "presets 1-4. " +
                    "Shortest of the three on " +
                    "purpose: a beetle has a shell, so it should shrug off a thrown rock better " +
                    "than a zombie or a spider does. A knocked-out beetle can't chase or attack, " +
                    "and rights itself with its own flip animation afterwards. Only counts if " +
                    "the item is actually thrown hard - dropping something on it does nothing. " +
                    "HOST-AUTHORITATIVE: only the host's value counts for the whole lobby.",
                    new AcceptableValueRange<double>(0.0, 60.0)));

            CreatureKnockoutMinThrowSpeedOverride = config.Bind(
                "Creatures",
                "creature-knockout-min-throw-speed",
                ConfigDefaults.CreatureKnockoutMinThrowSpeed,
                new ConfigDescription(
                    "How fast a thrown item has to be travelling, in meters per second, to knock " +
                    "out a beetle or a zombie - so a genuine throw does it and gently lobbing or " +
                    "dropping something doesn't. Applies to both creatures, so \"a hard throw\" " +
                    "means the same thing for each. Lower it if throws that feel hard aren't " +
                    "landing, raise it if soft ones are. 0 means any contact counts at all, " +
                    "however gentle. Turn on Debug/enable-debug-logging to see the measured speed " +
                    "of each throw next to this threshold in the log, which is the easy way to " +
                    "pick a value. The default is set from measured throws: medium throws land " +
                    "around 23-31 m/s and near-full-strength ones around 37-43, and the default " +
                    "of 36 was picked by play-testing as the point where a knockout needs a " +
                    "genuinely committed throw. " +
                    "Only takes effect when preset is set to Custom (5) - ignored under " +
                    "presets 1-4. " + "HOST-AUTHORITATIVE: only the " +
                    "host's value counts for the whole lobby.",
                    new AcceptableValueRange<double>(0.0, 50.0)));

            CreatureKnockoutMaxThrowDistanceOverride = config.Bind(
                "Creatures",
                "creature-knockout-max-throw-distance",
                ConfigDefaults.CreatureKnockoutMaxThrowDistance,
                new ConfigDescription(
                    "How close you have to be to a beetle or zombie, in meters, for a thrown item " +
                    "to knock it out. Together with creature-knockout-min-throw-speed above this " +
                    "is what the knockout costs you: a charged throw from close range, which " +
                    "takes time to wind up and usually loses you the item. Without a distance " +
                    "limit a hard throw is still fast a long way out, so you could pick creatures " +
                    "off from somewhere safe. 0 removes the distance requirement. " +
                    "Only takes effect when preset is set to Custom (5) - ignored under " +
                    "presets 1-4. " +
                    "HOST-AUTHORITATIVE: only the host's value counts for the whole lobby.",
                    new AcceptableValueRange<double>(0.0, 200.0)));

            BlowgunAffectsCreaturesOverride = config.Bind(
                "Creatures",
                "blowgun-affects-creatures",
                ConfigDefaults.BlowgunAffectsCreatures,
                "When on, shooting a creature with a blowgun dart takes it out of the fight: a " +
                "zombie dies (exactly the way it already does on its own after two minutes, " +
                "skeleton included), while a spider or a beetle is knocked out for a long time " +
                "(see blowgun-creature-stun-seconds). Spiders and beetles get stunned rather than " +
                "killed because the game has no death state for them at all. In the unmodded game " +
                "a dart passes straight through a spider or beetle and merely poisons a zombie, " +
                "which is what off (the default) restores. Only takes effect when preset is set " +
                "to Custom (5) - every preset 1-4 has it on, since darts are consumable and the " +
                "blowgun is uncommon, so this can't be spammed. HOST-AUTHORITATIVE: only the " +
                "host's value counts for the whole lobby.");

            BlowgunCreatureStunSecondsOverride = config.Bind(
                "Creatures",
                "blowgun-creature-stun-seconds",
                ConfigDefaults.BlowgunCreatureStunSeconds,
                new ConfigDescription(
                    "How many seconds a blowgun dart knocks out a spider or a beetle for. Doesn't " +
                    "apply to zombies, which die outright instead; set this to 0 if you want darts " +
                    "to kill zombies but leave spiders and beetles alone. Much longer than a " +
                    "thrown item's knockout on purpose - a dart costs you a consumable and only " +
                    "works if you actually hit. " +
                    "Only takes effect when preset is set to Custom (5) - ignored under " +
                    "presets 1-4. " + "HOST-AUTHORITATIVE: only the " +
                    "host's value counts for the whole lobby.",
                    new AcceptableValueRange<double>(0.0, 600.0)));

            ZombieWindMultiplierOverride = config.Bind(
                "Creatures",
                "zombie-wind-multiplier",
                ConfigDefaults.ZombieWindMultiplier,
                new ConfigDescription(
                    "Multiplier on how hard the wind pushes zombies around, e.g. 2.0 pushes them " +
                    "twice as hard as normal, 0 makes them immune. 1.0 means vanilla - note that " +
                    "vanilla is NOT zero: the game already pushes zombies at 60% of the force it " +
                    "uses on you, because a zombie counts as a bot character. Useful for making a " +
                    "storm a real hazard for the things chasing you and not just for you. " +
                    "Only takes effect when preset is set to Custom (5) - ignored under " +
                    "presets 1-4. HOST-AUTHORITATIVE: only the host's value counts for the whole " +
                    "lobby.",
                    new AcceptableValueRange<double>(0.0, 5.0)));

            BeetleWindSusceptibilityOverride = config.Bind(
                "Creatures",
                "beetle-wind-susceptibility",
                ConfigDefaults.BeetleWindSusceptibility,
                new ConfigDescription(
                    "How much the wind slides beetles around, as a fraction of their own walking " +
                    "speed - 1.0 means wind moves a beetle about as fast as it walks, 0.5 half " +
                    "that. NOTE: 0 is the vanilla value here, not 1.0, because beetles are " +
                    "completely immune to wind in the unmodded game and can't be made otherwise " +
                    "by scaling anything - the game resets a walking beetle's velocity every " +
                    "physics tick, so any wind force on it is erased before it moves. Set 0 to " +
                    "restore that. Only applies while a beetle is walking normally, never while " +
                    "it's tumbling, flipped or knocked out. " +
                    "Only takes effect when preset is set to Custom (5) - ignored under " +
                    "presets 1-4. " + "HOST-AUTHORITATIVE: " +
                    "only the host's value counts for the whole lobby.",
                    new AcceptableValueRange<double>(0.0, 5.0)));

            EnableCoverMouthOverride = config.Bind(
                "Spore-Areas",
                "enable-cover-mouth",
                ConfigDefaults.EnableCoverMouth,
                "Master switch for the cover-your-mouth mechanic: hold the key (see " +
                "General/cover-mouth-key) to breathe through your hands and take no spores " +
                "while inside a spore area, at the cost of stamina and both hands. Off = " +
                "vanilla, where the key does nothing at all. Only takes effect when preset is " +
                "set to Custom (5) - every preset 1-4 has it on. HOST-AUTHORITATIVE: if you're " +
                "not the host, this has no effect at all; only the host's value counts for the " +
                "whole lobby. If you just don't want to use the mechanic yourself, set " +
                "cover-mouth-key to None in General instead - that works regardless of the " +
                "host. Applies immediately, regardless of apply-changes-live.");

            CoverMouthStaminaPerSecondOverride = config.Bind(
                "Spore-Areas",
                "cover-mouth-stamina-per-second",
                ConfigDefaults.CoverMouthStaminaPerSecond,
                new ConfigDescription(
                    "How much stamina covering your mouth drains per second. Small by default - " +
                    "for scale, climbing a wall costs up to 0.2 per second, so this is about a " +
                    "sixth of that. You stop covering automatically when you run out of stamina. " +
                    "0 makes it free. Only takes effect when preset is set to Custom (5) - " +
                    "ignored under presets 1-4, which get cheaper the more forgiving they are. " +
                    "HOST-AUTHORITATIVE: only the host's value counts for the " +
                    "whole lobby (the keybind itself is per-player - see General).",
                    new AcceptableValueRange<float>(0f, 0.5f)));

            DisableWindEntirely = config.Bind(
                "Wind",
                "disable-wind-entirely",
                ConfigDefaults.DisableWindEntirely,
                "Master switch: when on, wind should NEVER occur at all - not vanilla-strength " +
                "wind, genuinely no wind, ever. HOST-AUTHORITATIVE: if you're not the host, " +
                "this has no effect at all - only the host's value counts for the whole lobby " +
                "(same for everyone in the run, no exceptions - see ROADMAP.md's Host " +
                "authority section). Any gust already blowing stops immediately when the host " +
                "turns this on, and none can start again while it stays on. Off by default - " +
                "no preset ever turns this on automatically. Applies immediately, regardless " +
                "of apply-changes-live.");

            WindBackpackAlwaysImmuneOverride = config.Bind(
                "Wind",
                "backpack-always-immune",
                ConfigDefaults.WindBackpackAlwaysImmune,
                "Whether backpacks are fully immune to wind force, regardless of " +
                "item-force-multiplier. Off = vanilla, where a gust sends a dropped backpack " +
                "down the mountain like anything else. Only takes effect when preset is set to " +
                "Custom (5) - every preset 1-4 has it on. HOST-AUTHORITATIVE: only the " +
                "host's value counts for the whole lobby, regardless of what non-host players " +
                "have set locally. Applies immediately, regardless of apply-changes-live.");

            WindForceMultiplierOverride = config.Bind(
                "Wind",
                "force-multiplier",
                ConfigDefaults.WindForceMultiplier,
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
                ConfigDefaults.WindGustDurationMultiplier,
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
                ConfigDefaults.WindItemForceMultiplier,
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
                ConfigDefaults.WindObstacleOcclusionRangeMultiplier,
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
                ConfigDefaults.WindFallCameraDampenClamp,
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

            WindRecentForceWindowSecondsOverride = config.Bind(
                "Wind",
                "fall-camera-dampen-window-seconds",
                ConfigDefaults.WindRecentForceWindowSeconds,
                new ConfigDescription(
                    "How many seconds after wind last pushed you that a fall still counts as " +
                    "wind-preceded for fall-camera-dampen-clamp above and for " +
                    "prevent-wind-ragdoll below. Only takes effect when preset is set to " +
                    "Custom (5) - ignored under presets 1-4, and it does nothing while both of " +
                    "those settings are off anyway. HOST-AUTHORITATIVE: only the host's " +
                    "value counts for the whole lobby.",
                    new AcceptableValueRange<float>(0.1f, 5f)));

            PreventWindRagdollOverride = config.Bind(
                "Wind",
                "prevent-wind-ragdoll",
                ConfigDefaults.PreventWindRagdoll,
                "Whether wind is allowed to ragdoll you. On (every preset 1-4): when wind " +
                "blows you off a ledge you keep full control of your " +
                "character on the way down, so you can grab a wall or use a Rescue Hook " +
                "instead of flailing helplessly. Off: vanilla, where being pushed off an edge " +
                "collapses you into physics - fall-camera-dampen-clamp above then still " +
                "softens it partway, if the preset sets one. Only ever applies to a fall wind " +
                "actually caused (see fall-camera-dampen-window-seconds); an ordinary fall " +
                "you walked into yourself ragdolls exactly like vanilla either way. Only " +
                "takes effect when preset is set to Custom (5) - ignored under presets 1-4. " +
                "HOST-AUTHORITATIVE: only the host's value counts for the whole lobby. " +
                "Applies immediately, regardless of apply-changes-live.");

            ClimbSheltersFromWindOverride = config.Bind(
                "Wind",
                "climb-shelters-from-wind",
                ConfigDefaults.ClimbSheltersFromWind,
                "Whether holding onto something shelters you from wind: while climbing a wall, " +
                "a rope, a vine or a climb handle, wind can't push you at all - instead " +
                "climbing gets much slower for as long as the wind is actually blowing on you " +
                "(see the three climb-*-multiplier settings below). Vanilla only shelters you " +
                "on a climb handle, so a gust mid-climb normally rips you off the wall, which " +
                "is why walking into the wind is the only reliable tactic. Off = vanilla, " +
                "and the default. Only takes effect when preset is set to Custom (5) - under " +
                "presets 1-4 it is on everywhere except Subtle (1). If the wind can't reach you " +
                "anyway - behind a rock, no gust - " +
                "you climb at full speed, so this never costs you anything when it isn't " +
                "protecting you. HOST-AUTHORITATIVE: only the host's value counts for the " +
                "whole lobby. Applies immediately, regardless of apply-changes-live.");

            ClimbWindSpeedMultiplierOverride = config.Bind(
                "Wind",
                "climb-speed-multiplier-in-wind",
                ConfigDefaults.ClimbWindSpeedMultiplier,
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
                ConfigDefaults.ClimbWindUpwardSpeedMultiplier,
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
                ConfigDefaults.ClimbWindIntoWindSpeedMultiplier,
                new ConfigDescription(
                    "Extra slowdown on climbing INTO the wind (toward where it's blowing " +
                    "from), multiplied on top of climb-speed-multiplier-in-wind above. " +
                    "Climbing with the wind is never sped up. 1.0 means direction doesn't " +
                    "matter. Only takes effect when preset is set to Custom (5) - ignored " +
                    "under presets 1-4.",
                    new AcceptableValueRange<double>(0.05, 1.0)));

            ClimbShelterGraceSecondsOverride = config.Bind(
                "Wind",
                "climb-shelter-grace-seconds",
                ConfigDefaults.ClimbShelterGraceSeconds,
                new ConfigDescription(
                    "How long wind stays much weaker after you let go of a climb - the window " +
                    "that stops finishing a climb mid-gust from catapulting you, and gives you " +
                    "time to start sprinting away or re-grab the wall if you let go by " +
                    "accident. Wind is held at climb-shelter-grace-force-multiplier below for " +
                    "most of the window, then ramps back to full over the tail of it so it " +
                    "doesn't end in a sudden shove. Set to 0 to switch the window off entirely " +
                    "(full wind force the instant you let go, like vanilla). Only takes " +
                    "effect when preset is set to Custom (5) - ignored under presets 1-4, and " +
                    "it does nothing while the climb shelter itself is off. HOST-AUTHORITATIVE: " +
                    "only the host's value counts for the whole lobby.",
                    new AcceptableValueRange<float>(0f, 3f)));

            ClimbWindGraceForceMultiplierOverride = config.Bind(
                "Wind",
                "climb-shelter-grace-force-multiplier",
                ConfigDefaults.ClimbWindGraceForceMultiplier,
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
                false,
                "OFF by default, and off is the fast path: Fairoots resolves every gameplay " +
                "value once as the Roots biome loads and then leaves it alone for the rest of " +
                "the run, which is why this lives in Debug - it is a testing convenience, not " +
                "something normal play needs. Turn it on to have settings take effect the " +
                "instant you change them in-game (e.g. via PEAKLib.ModConfig): kept spore bombs " +
                "resize live, the next detonation uses the new knockback/VFX/shake numbers, and " +
                "the jump-over-height cutoff updates immediately - useful while tuning, at the " +
                "cost of re-resolving those values as the game asks for them instead of reading " +
                "one frozen set. IMPORTANT: this is read once, when a Roots biome loads. " +
                "Switching it on while already standing in Roots does nothing until the next " +
                "Roots load - so turn it on first, then load in. The spore-bomb removal fraction " +
                "and the seed are always level-load-only either way, since which spore bombs " +
                "were already removed can't be undone mid-level; the spore-bomb recolor and the " +
                "cloud-opacity sliders are per-player cosmetics and always immediate either way. " +
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
                false,
                "When debug logging is on, automatically dump a scene diagnostics report " +
                "each time a Roots biome finishes loading. Off by default, like everything " +
                "else in this section - the dump walks the whole level, so it belongs behind " +
                "a deliberate choice rather than riding along with the logging switch. Leave " +
                "it off and trigger the report manually with the hotkey below instead.");

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

            CoverMouthPosePreview = config.Bind(
                "Debug",
                "cover-mouth-pose-preview",
                false,
                "Holds the cover-your-mouth pose on permanently so you can tune the pose settings " +
                "below without having to keep the key held down while you edit them. This is " +
                "PURELY VISUAL: it doesn't make you immune to spores, doesn't drain stamina, " +
                "doesn't tie up your hands, and other players don't see it - the mechanic itself " +
                "behaves exactly as normal while this is on. Off by default.");

            CoverMouthPoseEmote = config.Bind(
                "Debug",
                "cover-mouth-pose-emote",
                string.Empty,
                "Which animation the cover-your-mouth pose borrows its hand and finger shape " +
                "from. Leave empty to auto-pick the \"it's so over\" emote, which is what the " +
                "pose is designed around. With debug logging on, every available emote is listed " +
                "in the log the first time you cover your mouth, so you can copy an exact name " +
                "from there if you'd rather use a different one. Pose-tuning knob.");

            CoverMouthPoseEmoteTime = config.Bind(
                "Debug",
                "cover-mouth-pose-emote-time",
                0.6f,
                new ConfigDescription(
                    "Which moment of the pose animation to hold, from 0 (its first frame) to 1 " +
                    "(its last). The pose is frozen on a single frame rather than played, so this " +
                    "picks which one. Pose-tuning knob.",
                    new AcceptableValueRange<float>(0f, 1f)));

            CoverMouthPoseForwardCm = config.Bind(
                "Debug",
                "cover-mouth-pose-forward-cm",
                80f,
                new ConfigDescription(
                    "How far in front of the face your hands are held while covering your mouth, " +
                    "in centimetres. Default is the maintainer's own playtest-tuned value. " +
                    "Pose-tuning knob.",
                    new AcceptableValueRange<float>(0f, 80f)));

            CoverMouthPoseBelowHeadCm = config.Bind(
                "Debug",
                "cover-mouth-pose-below-head-cm",
                0f,
                new ConfigDescription(
                    "How far below eye level your hands are held while covering your mouth, in " +
                    "centimetres - i.e. mouth height. Pose-tuning knob.",
                    new AcceptableValueRange<float>(-20f, 40f)));

            CoverMouthPoseHandGapCm = config.Bind(
                "Debug",
                "cover-mouth-pose-hand-gap-cm",
                7f,
                new ConfigDescription(
                    "Vertical gap between your two hands while covering your mouth, in " +
                    "centimetres - they're stacked one above the other so they don't intersect. " +
                    "Default is the maintainer's own playtest-tuned value. Pose-tuning knob.",
                    new AcceptableValueRange<float>(0f, 40f)));

            CoverMouthPoseSideCm = config.Bind(
                "Debug",
                "cover-mouth-pose-side-cm",
                13f,
                new ConfigDescription(
                    "How far to each side of centre your hands sit while covering your mouth, in " +
                    "centimetres. Near 0 means both hands are on the centre line, which is what " +
                    "makes the pose read as covering your mouth rather than gesturing. " +
                    "Pose-tuning knob.",
                    new AcceptableValueRange<float>(0f, 25f)));

            CoverMouthPoseHandYawDeg = config.Bind(
                "Debug",
                "cover-mouth-pose-hand-yaw-deg",
                0f,
                new ConfigDescription(
                    "Turns both hands outward (or inward) while covering your mouth, in degrees - " +
                    "this is the one that decides where your thumbs point. 0 leaves the hands as " +
                    "the borrowed animation has them, which is palm-to-palm like praying; turning " +
                    "them opens the palms toward your face so the thumbs point away from it. " +
                    "Mirrored, so the two hands always stay symmetric. The sign is what decides " +
                    "which side of your hands faces you: negative turns the palms toward your " +
                    "face (correct for covering your mouth), positive turns them away. " +
                    "Pose-tuning knob.",
                    new AcceptableValueRange<float>(-180f, 180f)));

            CoverMouthPoseHandRollDeg = config.Bind(
                "Debug",
                "cover-mouth-pose-hand-roll-deg",
                10f,
                new ConfigDescription(
                    "Tips both hands so the thumbs ride higher or lower while covering your mouth, " +
                    "in degrees - use it with cover-mouth-pose-hand-yaw-deg to aim the thumbs " +
                    "up-and-out rather than straight out. Mirrored between the two hands; negate " +
                    "it if the thumbs end up pointing down instead of up. Pose-tuning knob.",
                    new AcceptableValueRange<float>(-180f, 180f)));

            CoverMouthPoseHandPitchDeg = config.Bind(
                "Debug",
                "cover-mouth-pose-hand-pitch-deg",
                -10f,
                new ConfigDescription(
                    "Tilts both hands' fingers up or down while covering your mouth, in degrees. " +
                    "Not mirrored - both hands tilt the same way. Pose-tuning knob.",
                    new AcceptableValueRange<float>(-180f, 180f)));

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

        /// <summary>The effective Spores build-up rate multiplier for every persistent spore area.</summary>
        public double SporeAreaStatusRateMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.SporeAreaStatusRateMultiplier(Preset.Value),
                SporeAreaStatusRateMultiplierOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective multiplier on how long the Spores status takes to clear.</summary>
        public double SporeClearTimeMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.SporeClearTimeMultiplier(Preset.Value),
                SporeClearTimeMultiplierOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective multiplier on every incoming Spores application, from any source.</summary>
        public double SporeBuildUpMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.SporeBuildUpMultiplier(Preset.Value),
                SporeBuildUpMultiplierOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective movement-speed multiplier for every mushroom zombie.</summary>
        public double ZombieSpeedMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.ZombieSpeedMultiplier(Preset.Value),
                ZombieSpeedMultiplierOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective movement-speed multiplier for every beetle.</summary>
        public double BeetleSpeedMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.BeetleSpeedMultiplier(Preset.Value),
                BeetleSpeedMultiplierOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective knockback multiplier for every beetle.</summary>
        public double BeetleKnockbackMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.BeetleKnockbackMultiplier(Preset.Value),
                BeetleKnockbackMultiplierOverride.Value,
                UseCustomOverrides);

        /// <summary>The effective ragdoll-duration multiplier for beetle hits and zombie bites.</summary>
        public double CreatureRagdollMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.CreatureRagdollMultiplier(Preset.Value),
                CreatureRagdollMultiplierOverride.Value,
                UseCustomOverrides);

        /// <summary>Whether zombies can lose a target at all.</summary>
        public bool ZombieDeaggroEnabled =>
            UseCustomOverrides
                ? ZombieDeaggroEnabledOverride.Value
                : PresetCatalog.ZombieDeaggroEnabled(Preset.Value);

        /// <summary>The effective zombie deaggro difficulty (1.0 = toughest, NOT vanilla).</summary>
        public double ZombieDeaggroMultiplier =>
            ZombieDeaggro.ClampMultiplier(
                OverrideResolution.Resolve(
                    PresetCatalog.ZombieDeaggroMultiplier(Preset.Value),
                    ZombieDeaggroMultiplierOverride.Value,
                    UseCustomOverrides));

        /// <summary>The effective beetle deaggro-distance multiplier (1.0 = vanilla).</summary>
        public double BeetleDeaggroMultiplier =>
            BeetleDeaggro.ClampMultiplier(
                OverrideResolution.Resolve(
                    PresetCatalog.BeetleDeaggroMultiplier(Preset.Value),
                    BeetleDeaggroMultiplierOverride.Value,
                    UseCustomOverrides));

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
        private double _snapSporeAreaStatusRateMultiplier;
        private double _snapSporeClearTimeMultiplier;
        private double _snapSporeBuildUpMultiplier;
        private double _snapZombieSpeedMultiplier;
        private double _snapBeetleSpeedMultiplier;
        private double _snapBeetleKnockbackMultiplier;
        private double _snapCreatureRagdollMultiplier;
        private bool _snapZombieDeaggroEnabled;
        private double _snapZombieDeaggroMultiplier;
        private double _snapBeetleDeaggroMultiplier;
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
            _snapSporeAreaStatusRateMultiplier = SporeAreaStatusRateMultiplier;
            _snapSporeClearTimeMultiplier = SporeClearTimeMultiplier;
            _snapSporeBuildUpMultiplier = SporeBuildUpMultiplier;
            _snapZombieSpeedMultiplier = ZombieSpeedMultiplier;
            _snapBeetleSpeedMultiplier = BeetleSpeedMultiplier;
            _snapBeetleKnockbackMultiplier = BeetleKnockbackMultiplier;
            _snapCreatureRagdollMultiplier = CreatureRagdollMultiplier;
            _snapZombieDeaggroEnabled = ZombieDeaggroEnabled;
            _snapZombieDeaggroMultiplier = ZombieDeaggroMultiplier;
            _snapBeetleDeaggroMultiplier = BeetleDeaggroMultiplier;
            _snapWindForceMultiplier = WindForceMultiplier;
            _snapWindGustDurationMultiplier = WindGustDurationMultiplier;
            _snapWindItemForceMultiplier = WindItemForceMultiplier;
            _snapWindObstacleOcclusionRangeMultiplier = WindObstacleOcclusionRangeMultiplier;
            _snapWindFallCameraDampenClamp = WindFallCameraDampenClamp;
            _snapClimbWindSpeedMultiplier = ClimbWindSpeedMultiplier;
            _snapClimbWindUpwardSpeedMultiplier = ClimbWindUpwardSpeedMultiplier;
            _snapClimbWindIntoWindSpeedMultiplier = ClimbWindIntoWindSpeedMultiplier;
            _snapClimbWindGraceForceMultiplier = ClimbWindGraceForceMultiplier;

            // Captured last and read from everywhere else via LiveUpdatesActive: the
            // whole point is that the rest of this level answers to what the player had
            // set when it loaded, not to what they flip mid-run.
            _snapLiveUpdates = ApplyChangesLive.Value;
            _snapshotTaken = true;
        }

        /// <summary>
        /// True while a live value should be used as-is: either the player wants
        /// live updates, or no level has loaded yet to snapshot from (falling back
        /// to live rather than a meaningless zeroed snapshot).
        /// </summary>
        /// <summary>
        /// <see cref="ApplyChangesLive"/> as it stood when this level's snapshot was
        /// taken - see <see cref="LiveUpdatesActive"/> for why the live entry isn't
        /// read directly.
        /// </summary>
        private bool _snapLiveUpdates;

        /// <summary>
        /// Whether live config updates are in force for the current Roots biome.
        ///
        /// <b>Deliberately the value captured at level load, not the live one.</b>
        /// Reading <see cref="ApplyChangesLive"/> directly meant flipping it on
        /// mid-run retroactively switched every accessor in this file over to
        /// resolving fresh, and every <c>SettingChanged</c> hook in <c>Plugin.Awake</c>
        /// over to firing scene-wide reapply passes - a mode change in the middle of a
        /// run, from a setting the player may well have flipped just to see what it
        /// did. Freezing it per level makes the rule simple and the cost predictable:
        /// what you had set when the biome loaded is how that biome behaves.
        /// Turn it on, then load in.
        ///
        /// Before the first Roots load of the session there is no snapshot to read, so
        /// the live entry stands in - there is nothing frozen to contradict yet.
        /// </summary>
        internal bool LiveUpdatesActive => !_snapshotTaken || _snapLiveUpdates;

        private bool UseLiveValue => LiveUpdatesActive;

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

        /// <summary>Game-facing code should read this instead of <see cref="SporeAreaStatusRateMultiplier"/>. Host-authoritative.</summary>
        public double EffectiveSporeAreaStatusRateMultiplier =>
            HostAuthority.Resolve("SporeAreaStatusRateMultiplier", UseLiveValue ? SporeAreaStatusRateMultiplier : _snapSporeAreaStatusRateMultiplier);

        /// <summary>Game-facing code should read this instead of <see cref="SporeClearTimeMultiplier"/>. Host-authoritative.</summary>
        public double EffectiveSporeClearTimeMultiplier =>
            HostAuthority.Resolve("SporeClearTimeMultiplier", UseLiveValue ? SporeClearTimeMultiplier : _snapSporeClearTimeMultiplier);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="SporeBuildUpMultiplier"/>. Host-authoritative - a player who
        /// scaled their own spore intake down would be playing a different game from
        /// the rest of the lobby, which is exactly what the host-authority rule
        /// exists to prevent (ROADMAP.md's "Host authority" section).
        /// </summary>
        public double EffectiveSporeBuildUpMultiplier =>
            HostAuthority.Resolve("SporeBuildUpMultiplier", UseLiveValue ? SporeBuildUpMultiplier : _snapSporeBuildUpMultiplier);

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
        /// <see cref="WindBackpackAlwaysImmuneOverride"/>.Value.
        /// Host-authoritative: only the host's value counts. Preset-resolved and
        /// always immediate, not level-load-snapshotted.
        /// </summary>
        public bool EffectiveWindBackpackAlwaysImmune =>
            HostAuthority.Resolve("WindBackpackAlwaysImmune", WindBackpackAlwaysImmune);

        /// <summary>Whether backpacks are fully wind-immune regardless of the item-force multiplier (off = vanilla). Resolved from the preset, or from the player's own value under Custom.</summary>
        public bool WindBackpackAlwaysImmune =>
            OverrideResolution.Resolve(
                PresetCatalog.WindBackpackAlwaysImmune(Preset.Value),
                WindBackpackAlwaysImmuneOverride.Value,
                UseCustomOverrides);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="CoverMouthBlocksSporeBombsOverride"/>.Value.
        /// Host-authoritative.
        /// </summary>
        public bool EffectiveCoverMouthBlocksSporeBombs =>
            HostAuthority.Resolve("CoverMouthBlocksSporeBombs", CoverMouthBlocksSporeBombs);

        /// <summary>Whether covering your mouth also blocks a spore bomb's cloud, not just the persistent spore areas. Resolved from the preset, or from the player's own value under Custom.</summary>
        public bool CoverMouthBlocksSporeBombs =>
            OverrideResolution.Resolve(
                PresetCatalog.CoverMouthBlocksSporeBombs(Preset.Value),
                CoverMouthBlocksSporeBombsOverride.Value,
                UseCustomOverrides);

        /// <summary>Whether the cover-your-mouth mechanic exists at all (on under every preset 1-4).</summary>
        public bool EnableCoverMouth =>
            OverrideResolution.Resolve(
                PresetCatalog.EnableCoverMouth(Preset.Value),
                EnableCoverMouthOverride.Value,
                UseCustomOverrides);

        /// <summary>Game-facing code should read this instead of <see cref="EnableCoverMouthOverride"/>.Value. Host-authoritative - whether a counterplay mechanic exists in this run is shared balance.</summary>
        public bool EffectiveEnableCoverMouth =>
            HostAuthority.Resolve("EnableCoverMouth", EnableCoverMouth);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="CoverMouthStaminaPerSecondOverride"/>.Value.
        /// Host-authoritative: it decides what a counterplay move costs, which is
        /// shared balance, unlike the keybind that triggers it. Preset-resolved but
        /// always immediate (not level-load-snapshotted), matching the toggle it
        /// belongs to.
        /// </summary>
        public float EffectiveCoverMouthStaminaPerSecond =>
            HostAuthority.Resolve("CoverMouthStaminaPerSecond", CoverMouthStaminaPerSecond);

        /// <summary>Stamina drained per second while holding a mouth cover - what the counterplay move costs. Resolved from the preset, or from the player's own value under Custom.</summary>
        public float CoverMouthStaminaPerSecond =>
            OverrideResolution.Resolve(
                PresetCatalog.CoverMouthStaminaPerSecond(Preset.Value),
                CoverMouthStaminaPerSecondOverride.Value,
                UseCustomOverrides);

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
        /// <see cref="ZombieSpeedMultiplier"/>. Host-authoritative (how fast a
        /// creature chases is shared balance) and level-load-snapshotted like the
        /// other numeric dials, so it respects <see cref="ApplyChangesLive"/>.
        /// </summary>
        public double EffectiveZombieSpeedMultiplier =>
            HostAuthority.Resolve("ZombieSpeedMultiplier", UseLiveValue ? ZombieSpeedMultiplier : _snapZombieSpeedMultiplier);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="BeetleSpeedMultiplier"/>. Host-authoritative and
        /// level-load-snapshotted, same shape as
        /// <see cref="EffectiveZombieSpeedMultiplier"/>.
        /// </summary>
        public double EffectiveBeetleSpeedMultiplier =>
            HostAuthority.Resolve("BeetleSpeedMultiplier", UseLiveValue ? BeetleSpeedMultiplier : _snapBeetleSpeedMultiplier);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="BeetleKnockbackMultiplier"/>. Host-authoritative and
        /// level-load-snapshotted, same shape as
        /// <see cref="EffectiveBeetleSpeedMultiplier"/>.
        /// </summary>
        public double EffectiveBeetleKnockbackMultiplier =>
            HostAuthority.Resolve("BeetleKnockbackMultiplier", UseLiveValue ? BeetleKnockbackMultiplier : _snapBeetleKnockbackMultiplier);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="CreatureRagdollMultiplier"/>. Host-authoritative and
        /// level-load-snapshotted, same shape as
        /// <see cref="EffectiveBeetleKnockbackMultiplier"/>.
        /// </summary>
        public double EffectiveCreatureRagdollMultiplier =>
            HostAuthority.Resolve("CreatureRagdollMultiplier", UseLiveValue ? CreatureRagdollMultiplier : _snapCreatureRagdollMultiplier);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="ZombieKnockoutSecondsOverride"/>.Value. Host-authoritative -
        /// how strong a counterplay move is, is shared balance.
        /// </summary>
        public double EffectiveZombieKnockoutSeconds =>
            HostAuthority.Resolve("ZombieKnockoutSeconds", ZombieKnockoutSeconds);

        /// <summary>How long a thrown item knocks a zombie out for, in seconds (0 = vanilla). Resolved from the preset, or from the player's own value under Custom.</summary>
        public double ZombieKnockoutSeconds =>
            OverrideResolution.Resolve(
                PresetCatalog.ZombieKnockoutSeconds(Preset.Value),
                ZombieKnockoutSecondsOverride.Value,
                UseCustomOverrides);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="BeetleKnockoutSecondsOverride"/>.Value. Host-authoritative -
        /// same shape as <see cref="EffectiveCoverMouthStaminaPerSecond"/>.
        /// </summary>
        public double EffectiveBeetleKnockoutSeconds =>
            HostAuthority.Resolve("BeetleKnockoutSeconds", BeetleKnockoutSeconds);

        /// <summary>How long a thrown item knocks a beetle onto its back for, in seconds (0 = vanilla). Resolved from the preset, or from the player's own value under Custom.</summary>
        public double BeetleKnockoutSeconds =>
            OverrideResolution.Resolve(
                PresetCatalog.BeetleKnockoutSeconds(Preset.Value),
                BeetleKnockoutSecondsOverride.Value,
                UseCustomOverrides);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="CreatureKnockoutMinThrowSpeedOverride"/>.Value. Host-authoritative,
        /// preset-resolved and always immediate.
        /// </summary>
        public double EffectiveCreatureKnockoutMinThrowSpeed =>
            HostAuthority.Resolve("CreatureKnockoutMinThrowSpeed", CreatureKnockoutMinThrowSpeed);

        /// <summary>How fast a thrown item must travel, in m/s, to knock a creature out. Resolved from the preset, or from the player's own value under Custom.</summary>
        public double CreatureKnockoutMinThrowSpeed =>
            OverrideResolution.Resolve(
                PresetCatalog.CreatureKnockoutMinThrowSpeed(Preset.Value),
                CreatureKnockoutMinThrowSpeedOverride.Value,
                UseCustomOverrides);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="CreatureKnockoutMaxThrowDistanceOverride"/>.Value. Host-authoritative,
        /// preset-resolved and always immediate.
        /// </summary>
        public double EffectiveCreatureKnockoutMaxThrowDistance =>
            HostAuthority.Resolve("CreatureKnockoutMaxThrowDistance", CreatureKnockoutMaxThrowDistance);

        /// <summary>How close the thrower must have been, in meters, for a thrown item to knock a creature out. Resolved from the preset, or from the player's own value under Custom.</summary>
        public double CreatureKnockoutMaxThrowDistance =>
            OverrideResolution.Resolve(
                PresetCatalog.CreatureKnockoutMaxThrowDistance(Preset.Value),
                CreatureKnockoutMaxThrowDistanceOverride.Value,
                UseCustomOverrides);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="BlowgunAffectsCreaturesOverride"/>.Value. Host-authoritative,
        /// preset-resolved and always immediate.
        /// </summary>
        public bool EffectiveBlowgunAffectsCreatures =>
            HostAuthority.Resolve("BlowgunAffectsCreatures", BlowgunAffectsCreatures);

        /// <summary>Whether a blowgun dart takes a creature out of the fight (off = vanilla). Resolved from the preset, or from the player's own value under Custom.</summary>
        public bool BlowgunAffectsCreatures =>
            OverrideResolution.Resolve(
                PresetCatalog.BlowgunAffectsCreatures(Preset.Value),
                BlowgunAffectsCreaturesOverride.Value,
                UseCustomOverrides);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="BlowgunCreatureStunSecondsOverride"/>.Value. Host-authoritative,
        /// preset-resolved and always immediate.
        /// </summary>
        public double EffectiveBlowgunCreatureStunSeconds =>
            HostAuthority.Resolve("BlowgunCreatureStunSeconds", BlowgunCreatureStunSeconds);

        /// <summary>How long a blowgun dart stuns a spider or beetle, in seconds. Resolved from the preset, or from the player's own value under Custom.</summary>
        public double BlowgunCreatureStunSeconds =>
            OverrideResolution.Resolve(
                PresetCatalog.BlowgunCreatureStunSeconds(Preset.Value),
                BlowgunCreatureStunSecondsOverride.Value,
                UseCustomOverrides);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="ZombieWindMultiplierOverride"/>.Value. Host-authoritative,
        /// preset-resolved and always immediate.
        /// </summary>
        public double EffectiveZombieWindMultiplier =>
            HostAuthority.Resolve("ZombieWindMultiplier", ZombieWindMultiplier);

        /// <summary>Multiplier on the wind force a zombie receives (1.0 = vanilla, which is already nonzero). Resolved from the preset, or from the player's own value under Custom.</summary>
        public double ZombieWindMultiplier =>
            OverrideResolution.Resolve(
                PresetCatalog.ZombieWindMultiplier(Preset.Value),
                ZombieWindMultiplierOverride.Value,
                UseCustomOverrides);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="BeetleWindSusceptibilityOverride"/>.Value. Host-authoritative,
        /// preset-resolved and always immediate.
        /// </summary>
        public double EffectiveBeetleWindSusceptibility =>
            HostAuthority.Resolve("BeetleWindSusceptibility", BeetleWindSusceptibility);

        /// <summary>How much wind slides a beetle, as a fraction of its own walking speed (0 = vanilla). Resolved from the preset, or from the player's own value under Custom.</summary>
        public double BeetleWindSusceptibility =>
            OverrideResolution.Resolve(
                PresetCatalog.BeetleWindSusceptibility(Preset.Value),
                BeetleWindSusceptibilityOverride.Value,
                UseCustomOverrides);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="ZombieDeaggroEnabled"/>. Host-authoritative - whether a chase can
        /// be escaped at all is shared balance, not local feel.
        /// </summary>
        public bool EffectiveZombieDeaggroEnabled =>
            HostAuthority.Resolve("ZombieDeaggroEnabled", UseLiveValue ? ZombieDeaggroEnabled : _snapZombieDeaggroEnabled);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="ZombieDeaggroMultiplier"/>. Host-authoritative and
        /// level-load-snapshotted.
        /// </summary>
        public double EffectiveZombieDeaggroMultiplier =>
            HostAuthority.Resolve("ZombieDeaggroMultiplier", UseLiveValue ? ZombieDeaggroMultiplier : _snapZombieDeaggroMultiplier);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="BeetleDeaggroMultiplier"/>. Host-authoritative and
        /// level-load-snapshotted.
        /// </summary>
        public double EffectiveBeetleDeaggroMultiplier =>
            HostAuthority.Resolve("BeetleDeaggroMultiplier", UseLiveValue ? BeetleDeaggroMultiplier : _snapBeetleDeaggroMultiplier);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="DisableZombies"/>.Value. Host-authoritative - flat, same shape
        /// as <see cref="EffectiveDisableSporeAreas"/>.
        /// </summary>
        public bool EffectiveDisableZombies =>
            HostAuthority.Resolve("DisableZombies", DisableZombies.Value);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="DisableBeetles"/>.Value. Host-authoritative - flat, same shape
        /// as <see cref="EffectiveDisableSporeAreas"/>.
        /// </summary>
        public bool EffectiveDisableBeetles =>
            HostAuthority.Resolve("DisableBeetles", DisableBeetles.Value);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="DisableSpiders"/>.Value. Host-authoritative - flat, same shape
        /// as <see cref="EffectiveDisableSporeAreas"/>.
        /// </summary>
        public bool EffectiveDisableSpiders =>
            HostAuthority.Resolve("DisableSpiders", DisableSpiders.Value);

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
        /// <see cref="WindFallCameraDampenClamp"/>. Host-authoritative as of
        /// 2026-07-30 (maintainer's call): every setting outside <c>General</c> and
        /// <c>Debug</c> may only change the biome when the host changes it, with no
        /// exception carved out for "it's only camera feel" - a client setting this
        /// for themselves now does nothing until they are the host.
        /// </summary>
        public double EffectiveWindFallCameraDampenClamp =>
            HostAuthority.Resolve("WindFallCameraDampenClamp", UseLiveValue ? WindFallCameraDampenClamp : _snapWindFallCameraDampenClamp);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="WindRecentForceWindowSecondsOverride"/>.Value.
        /// Host-authoritative - see
        /// <see cref="EffectiveWindFallCameraDampenClamp"/> for why this one stopped
        /// being per-client too.
        /// </summary>
        public float EffectiveWindRecentForceWindowSeconds =>
            HostAuthority.Resolve("WindRecentForceWindowSeconds", WindRecentForceWindowSeconds);

        /// <summary>How long after wind last pushed the player a fall still counts as wind-preceded. Resolved from the preset, or from the player's own value under Custom.</summary>
        public float WindRecentForceWindowSeconds =>
            OverrideResolution.Resolve(
                PresetCatalog.WindRecentForceWindowSeconds(Preset.Value),
                WindRecentForceWindowSecondsOverride.Value,
                UseCustomOverrides);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="PreventWindRagdollOverride"/>.Value. Host-authoritative -
        /// same shape as <see cref="EffectiveClimbSheltersFromWind"/>, except that
        /// this one is on under every preset rather than off on Subtle.
        /// </summary>
        public bool EffectivePreventWindRagdoll =>
            HostAuthority.Resolve("PreventWindRagdoll", PreventWindRagdoll);

        /// <summary>Whether a wind-caused fall leaves the player in control instead of ragdolling (off = vanilla). Resolved from the preset, or from the player's own value under Custom.</summary>
        public bool PreventWindRagdoll =>
            OverrideResolution.Resolve(
                PresetCatalog.PreventWindRagdoll(Preset.Value),
                PreventWindRagdollOverride.Value,
                UseCustomOverrides);

        /// <summary>Whether the bush/grass placement-removal pass runs. Host-authoritative - which spore bombs exist in the level is shared game state.</summary>
        public bool EnableFoliageRemoval =>
            OverrideResolution.Resolve(
                PresetCatalog.EnableFoliageRemoval(Preset.Value),
                EnableFoliageRemovalOverride.Value,
                UseCustomOverrides);

        /// <summary>Game-facing code should read this instead of <see cref="EnableFoliageRemovalOverride"/>.Value. Host-authoritative.</summary>
        public bool EffectiveEnableFoliageRemoval =>
            HostAuthority.Resolve("EnableFoliageRemoval", EnableFoliageRemoval);

        /// <summary>Whether holding onto something shelters the player from wind (off under Subtle - see <see cref="PresetCatalog.ClimbSheltersFromWind"/>).</summary>
        public bool ClimbSheltersFromWind =>
            OverrideResolution.Resolve(
                PresetCatalog.ClimbSheltersFromWind(Preset.Value),
                ClimbSheltersFromWindOverride.Value,
                UseCustomOverrides);

        /// <summary>
        /// Game-facing code should read this instead of
        /// <see cref="ClimbSheltersFromWindOverride"/>.Value. Host-authoritative:
        /// whether wind can push a climber is shared game logic, not local feel.
        /// Deliberately immediate rather than level-load-snapshotted, matching the
        /// toggle it gates.
        /// </summary>
        public bool EffectiveClimbSheltersFromWind =>
            HostAuthority.Resolve("ClimbSheltersFromWind", ClimbSheltersFromWind);

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
        /// <see cref="ClimbShelterGraceSecondsOverride"/>.Value.
        /// Host-authoritative: it decides how much force actually lands on a
        /// player. Preset-resolved and always immediate.
        /// </summary>
        public float EffectiveClimbShelterGraceSeconds =>
            HostAuthority.Resolve("ClimbShelterGraceSeconds", ClimbShelterGraceSeconds);

        /// <summary>How long wind stays weakened after the player lets go of a climb. Resolved from the preset, or from the player's own value under Custom.</summary>
        public float ClimbShelterGraceSeconds =>
            OverrideResolution.Resolve(
                PresetCatalog.ClimbShelterGraceSeconds(Preset.Value),
                ClimbShelterGraceSecondsOverride.Value,
                UseCustomOverrides);
    }
}
