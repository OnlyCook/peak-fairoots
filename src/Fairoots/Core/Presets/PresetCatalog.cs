using Fairoots.Core;

namespace Fairoots.Core.Presets
{
    /// <summary>
    /// The documented front door onto the per-preset numbers - "what does preset
    /// N set mechanic X to." One method per setting, each carrying the reasoning
    /// for its row; the numbers themselves live in <see cref="PresetValues"/>,
    /// generated from <c>docs/PRESETS.md</c> by <c>scripts/apply-presets.sh</c>.
    ///
    /// <b>Deliberately no numbers in this file.</b> Every value is tuned by
    /// editing the table in <c>docs/PRESETS.md</c> and re-running the script, so
    /// the maintainer can tune between runs without touching code and players can
    /// read the whole preset table in one place. A number restated in a doc
    /// comment here would silently go stale the first time a cell changed, so
    /// these comments say what a row <em>means</em> and which way it runs, never
    /// what it currently is.
    ///
    /// Values here are the preset *defaults*. They are only what applies when the
    /// active preset is one of 1-4; under <see cref="PresetId.Custom"/> the
    /// player's own config value is used instead - see
    /// <see cref="OverrideResolution"/>.
    /// </summary>
    public static class PresetCatalog
    {
        /// <summary>
        /// <see cref="PresetId.Custom"/> has no catalog numbers of its own - every
        /// per-mechanic setting under Custom is meant to come straight from the
        /// player's config (see <see cref="PresetId.Custom"/>'s remarks). This maps
        /// Custom to Balanced purely so a catalog lookup never throws and never
        /// returns a nonsense value if a caller reaches for a preset value while
        /// Custom is active - it is a safety fallback, not "Custom follows
        /// Balanced." Note that a Custom player who has touched nothing gets
        /// vanilla, not Balanced: their config entries are all bound to
        /// <see cref="ConfigDefaults"/>, every one of which is the vanilla value.
        /// </summary>
        private static PresetId CatalogKey(PresetId preset) =>
            preset == PresetId.Custom ? PresetId.Balanced : preset;

        // --- Spore bombs --------------------------------------------------

        /// <summary>
        /// Target fraction of spore bombs to remove overall (foliage pass +
        /// seeded cull combined), per <c>docs/PRESETS.md</c>. Subtle removes
        /// nothing beyond the foliage pass; the most forgiving presets are where
        /// OVERVIEW.md's literal "cut them in half" ask and beyond lives.
        /// </summary>
        public static double SporeBombCullFraction(PresetId preset) =>
            PresetValues.SporeBombCullFraction(CatalogKey(preset));

        /// <summary>
        /// Multiplier applied to a kept spore bomb's trigger-hitbox
        /// <c>SphereCollider.radius</c>. 1.0 = vanilla. Balanced's value is
        /// live-playtest-confirmed against the actual mushroom mesh (via
        /// <c>TriggerRadiusOverlay</c>'s wireframe) - the maintainer's own in-game
        /// comparison called it "the perfect value."
        /// </summary>
        public static double SporeBombTriggerRadiusMultiplier(PresetId preset) =>
            PresetValues.SporeBombTriggerRadiusMultiplier(CatalogKey(preset));

        /// <summary>
        /// Multiplier applied to the spawned explosion's <c>AOE.knockback</c>
        /// (and item knockback). 1.0 = vanilla.
        /// </summary>
        public static double SporeBombKnockbackMultiplier(PresetId preset) =>
            PresetValues.SporeBombKnockbackMultiplier(CatalogKey(preset));

        /// <summary>
        /// Cap, in meters, on the spawned explosion's <c>AddScreenshake.range</c>.
        /// <see cref="SporeBombExplosionTuning.NoScreenshakeCap"/> (0) leaves the
        /// vanilla range alone.
        /// </summary>
        public static double SporeBombScreenshakeRangeCapMeters(PresetId preset) =>
            PresetValues.SporeBombScreenshakeRangeCapMeters(CatalogKey(preset));

        /// <summary>
        /// Multiplier applied to the spawned explosion's
        /// <c>ExplosionEffect.explosionPointCount</c>/<c>subExplosionPointCount</c>.
        /// 1.0 = vanilla.
        /// </summary>
        public static double SporeBombVfxCountMultiplier(PresetId preset) =>
            PresetValues.SporeBombVfxCountMultiplier(CatalogKey(preset));

        /// <summary>
        /// Multiplier feeding <see cref="SporeBombExplosionTuning.ResolveTriggerHeightCutoffMeters"/> -
        /// how high above a spore bomb's base a player can be and still set it
        /// off. 1.0 = vanilla (cutoff disabled entirely). Balanced's value
        /// reproduces the exact absolute cutoff (2.25m) the maintainer had
        /// playtest-tuned before this became a multiplier - see
        /// <see cref="SporeBombExplosionTuning.TriggerHeightBaselineMeters"/>.
        /// </summary>
        public static double SporeBombTriggerHeightMultiplier(PresetId preset) =>
            PresetValues.SporeBombTriggerHeightMultiplier(CatalogKey(preset));

        /// <summary>
        /// Multiplier applied to the temporary spore area's radius on detonation,
        /// per <see cref="SporeBombExplosionTuning.ScaleSporeAreaRadius"/>. Not
        /// yet tuned per preset - vanilla on every preset for now.
        /// </summary>
        public static double SporeBombSporeAreaRadiusMultiplier(PresetId preset) =>
            PresetValues.SporeBombSporeAreaRadiusMultiplier(CatalogKey(preset));

        /// <summary>
        /// Whether the bush/grass placement-removal pass runs - the game never
        /// prevents a spore bomb landing inside foliage, so this closes a gap
        /// rather than rebalancing anything, and every preset runs it. Vanilla
        /// (and therefore the config default) is off, so a Custom player who has
        /// touched nothing keeps every camouflaged bomb the game placed.
        ///
        /// Doesn't change how much gets removed overall: the
        /// <see cref="SporeBombCullFraction"/> target still applies, the seeded
        /// pass just picks from every candidate instead of the camouflaged ones
        /// first.
        /// </summary>
        public static bool EnableFoliageRemoval(PresetId preset) =>
            PresetValues.EnableFoliageRemoval(CatalogKey(preset));

        /// <summary>
        /// Whether covering your mouth also blocks the spore status from a spore
        /// bomb's temporary cloud, on top of the biome's persistent spore areas.
        /// Off on every preset today (see <c>SporeBombs/CoverMouthSporeBombPatch</c>
        /// for why the mechanic is scoped to spore areas) - kept as a preset row
        /// so the tuning pass can turn it on for the forgiving presets without a
        /// code change. Only the spore status is ever suppressed either way:
        /// knockback and screen shake still land.
        /// </summary>
        public static bool CoverMouthBlocksSporeBombs(PresetId preset) =>
            PresetValues.CoverMouthBlocksSporeBombs(CatalogKey(preset));

        // --- Spore areas --------------------------------------------------

        /// <summary>
        /// Target fraction of the level's persistent spore areas ("Mushroom Spore
        /// Clouds") to remove, per <see cref="SporeAreaCull"/>.
        ///
        /// <b>Zero on both Subtle and Balanced</b> - the maintainer's explicit
        /// call (2026-07-27), and a deliberate difference from
        /// <see cref="SporeBombCullFraction"/> (which already thins at Balanced).
        /// Roots has only ~12-23 spore areas in a whole level, against 400+ spore
        /// bombs: they're landmarks, not clutter, so removing any at the default
        /// preset would change the shape of the biome rather than just its
        /// fairness. Only the two most forgiving presets thin them, and even
        /// there the cluster-first rule means what goes is the overlap, not the
        /// landmark.
        /// </summary>
        public static double SporeAreaRemovalFraction(PresetId preset) =>
            PresetValues.SporeAreaRemovalFraction(CatalogKey(preset));

        /// <summary>
        /// Multiplier applied to a persistent spore area's <c>radius</c> (and,
        /// proportionally, its <c>innerFade</c>/<c>outerFade</c> and its cloud
        /// VFX scale - see <see cref="SporeAreaTuning"/>). 1.0 = vanilla, which
        /// is <c>radius = 16</c> world units (~26m).
        ///
        /// Not to be confused with <see cref="SporeBombSporeAreaRadiusMultiplier"/>,
        /// which is the *spore bomb's* temporary mini area.
        /// </summary>
        public static double SporeAreaRadiusMultiplier(PresetId preset) =>
            PresetValues.SporeAreaRadiusMultiplier(CatalogKey(preset));

        /// <summary>
        /// Multiplier applied to how fast the Spores status builds up inside a
        /// persistent spore area (<c>StatusEmitter.amount</c>, vanilla 0.025 in
        /// Roots - see <see cref="SporeAreaTuning.ScaleStatusRate"/>). 1.0 =
        /// vanilla.
        /// </summary>
        public static double SporeAreaStatusRateMultiplier(PresetId preset) =>
            PresetValues.SporeAreaStatusRateMultiplier(CatalogKey(preset));

        /// <summary>
        /// Whether the cover-your-mouth-vs-spore-areas counterplay mechanic
        /// exists at all - on for every preset, off in vanilla (and therefore off
        /// by default, so Custom starts from vanilla). The key that triggers it
        /// and whether it's hold or toggle stay per-client player preferences;
        /// this and its stamina cost are the shared-balance half.
        /// </summary>
        public static bool EnableCoverMouth(PresetId preset) =>
            PresetValues.EnableCoverMouth(CatalogKey(preset));

        /// <summary>
        /// Stamina drained per second while holding a mouth cover - what the
        /// mechanic costs, cheaper on the more forgiving presets. Only means
        /// anything while <see cref="EnableCoverMouth"/> is on, which is why its
        /// config default is the tuned number rather than a vanilla one (there is
        /// no vanilla cost for a mechanic vanilla doesn't have) - see
        /// <c>docs/PRESETS.md</c>'s note on gated parameters. For scale, vanilla
        /// wall climbing costs up to 0.2/s.
        /// </summary>
        public static float CoverMouthStaminaPerSecond(PresetId preset) =>
            PresetValues.CoverMouthStaminaPerSecond(CatalogKey(preset));

        // --- Spores (the status itself) ------------------------------------

        /// <summary>
        /// Multiplier applied to how long the Spores status takes to drain off a
        /// player once nothing is applying it any more
        /// (<see cref="SporeStatusTuning.ScaleDecayRate"/> and
        /// <see cref="SporeStatusTuning.ScaleDecayCooldown"/>). 1.0 = vanilla;
        /// below 1.0 clears faster. Balanced's value is live-tuned (2026-07-30).
        ///
        /// A genuinely new axis: nothing else in the catalog touches recovery, only
        /// how much and how often spores are applied in the first place. That's
        /// exactly why it can carry real per-preset numbers while
        /// <see cref="SporeBuildUpMultiplier"/> can't.
        /// </summary>
        public static double SporeClearTimeMultiplier(PresetId preset) =>
            PresetValues.SporeClearTimeMultiplier(CatalogKey(preset));

        /// <summary>
        /// Multiplier applied to <em>every</em> incoming Spores application, whatever
        /// its source (<see cref="SporeStatusTuning.ScaleBuildUp"/>).
        ///
        /// <b>Deliberately vanilla on all four presets - this is not an unfilled
        /// row.</b> The presets already reduce spore build-up per hazard
        /// (<see cref="SporeAreaStatusRateMultiplier"/> for the areas, the
        /// spore-bomb removal/radius rows for the bombs), and this dial multiplies
        /// on top of all of them at once (see <see cref="SporeStatusTuning"/>).
        /// Giving it preset values too would compound with rows that already
        /// express the same intent and make the per-hazard numbers stop meaning
        /// what they say. So it stays a Custom-only global lever for players who
        /// want one knob for all spores. Do not "finish" this row in the Phase 9
        /// tuning pass without also rebalancing those.
        /// </summary>
        public static double SporeBuildUpMultiplier(PresetId preset) =>
            PresetValues.SporeBuildUpMultiplier(CatalogKey(preset));

        // --- Creatures -----------------------------------------------------

        /// <summary>
        /// Multiplier applied to a mushroom zombie's movement speed. 1.0 =
        /// vanilla.
        ///
        /// Vanilla is <c>CharacterMovement.movementForce = 10</c>, inherited by
        /// <c>CharacterMovementZombie</c> (which overrides only its ground checks) -
        /// this resolves RESEARCH.md's Q8 open question about which field actually
        /// governs zombie speed. Kept as its own row rather than shared with
        /// <see cref="BeetleSpeedMultiplier"/> even though ROADMAP.md lists them
        /// together: they're different fields with different units on unrelated
        /// classes, and a chase you can't outrun and a beetle you can't sidestep are
        /// separate complaints worth tuning apart.
        /// </summary>
        public static double ZombieSpeedMultiplier(PresetId preset) =>
            PresetValues.ZombieSpeedMultiplier(CatalogKey(preset));

        /// <summary>
        /// Multiplier applied to a beetle's movement speed (<c>Mob.movementSpeed</c>,
        /// vanilla 5). 1.0 = vanilla. See <see cref="ZombieSpeedMultiplier"/> for
        /// why the two are separate rows.
        /// </summary>
        public static double BeetleSpeedMultiplier(PresetId preset) =>
            PresetValues.BeetleSpeedMultiplier(CatalogKey(preset));

        /// <summary>
        /// Multiplier applied to a beetle's knockback shove
        /// (<c>Beetle.bonkForce</c>/<c>bonkForceUp</c>, both vanilla 100). 1.0 =
        /// vanilla.
        ///
        /// There is deliberately no zombie counterpart in this catalog: a zombie
        /// applies no scripted knockback at all (decompile-confirmed - see
        /// <c>Creatures/CreatureKnockbackPatch</c>), so there is no vanilla number for
        /// a zombie row to scale.
        /// </summary>
        public static double BeetleKnockbackMultiplier(PresetId preset) =>
            PresetValues.BeetleKnockbackMultiplier(CatalogKey(preset));

        /// <summary>
        /// Multiplier applied to how long a beetle's or a zombie's hit keeps the
        /// player ragdolled (<c>Beetle.ragdollTime</c> 2s /
        /// <c>MushroomZombie.biteStunTime</c> 3s - see
        /// <see cref="CreatureTuning.ScaleRagdollTime"/>). 1.0 = vanilla,
        /// 0 = never lose control.
        ///
        /// One row for both creatures on purpose: this is the player's own
        /// "how long am I not in control of my character" budget, which is the same
        /// complaint regardless of what hit them - unlike speed, where a chase you
        /// can't outrun and a beetle you can't sidestep are separate problems.
        /// </summary>
        public static double CreatureRagdollMultiplier(PresetId preset) =>
            PresetValues.CreatureRagdollMultiplier(CatalogKey(preset));

        /// <summary>
        /// Whether zombies can lose a target at all.
        ///
        /// This is why the mechanic needs an on/off row and not just a multiplier:
        /// vanilla zombies never deaggro, so <em>any</em> multiplier is a behavior
        /// change, and Subtle's whole identity is "vanilla except the always-on
        /// gap-closing fixes". Off on Subtle (and off by default, matching
        /// vanilla), on everywhere else.
        /// </summary>
        public static bool ZombieDeaggroEnabled(PresetId preset) =>
            PresetValues.ZombieDeaggroEnabled(CatalogKey(preset));

        /// <summary>
        /// How hard it is for a player to shake a zombie.
        /// <b>1.0 is the toughest setting here, not vanilla</b> - see
        /// <see cref="ZombieDeaggro"/> for why this one dial has to invert the mod's
        /// usual convention. Subtle's value is unused (the mechanic is off there per
        /// <see cref="ZombieDeaggroEnabled"/>) but is kept at the maximum so that
        /// turning it on by hand under Subtle gives the toughest behavior rather than
        /// the most forgiving.
        /// </summary>
        public static double ZombieDeaggroMultiplier(PresetId preset) =>
            PresetValues.ZombieDeaggroMultiplier(CatalogKey(preset));

        /// <summary>
        /// How hard it is for a player to shake a beetle - a multiplier on the
        /// distance at which it keeps an existing target
        /// (<see cref="CreatureTuning.ScaleDeaggroDistance"/>). 1.0 = vanilla,
        /// lower = easier to escape, matching the direction of
        /// <see cref="ZombieDeaggroMultiplier"/> even though only this one is
        /// vanilla-anchored. Beetles already deaggro in vanilla, so unlike the zombie
        /// row this needs no on/off companion.
        /// </summary>
        public static double BeetleDeaggroMultiplier(PresetId preset) =>
            PresetValues.BeetleDeaggroMultiplier(CatalogKey(preset));

        /// <summary>
        /// How long a zombie is knocked out by a thrown item, in seconds. Vanilla
        /// already ragdolls a zombie for about a second when an item hits it (a
        /// zombie is a <c>Character</c>, so <c>Bonkable</c> finds it), so this
        /// extends an existing interaction rather than inventing one - see
        /// <see cref="CreatureKnockout"/>. 0 = vanilla, and the default.
        /// </summary>
        public static double ZombieKnockoutSeconds(PresetId preset) =>
            PresetValues.ZombieKnockoutSeconds(CatalogKey(preset));

        /// <summary>
        /// How long a beetle is knocked onto its back by a thrown item, in
        /// seconds. Unlike the zombie's, an entirely new interaction: a beetle is
        /// a <c>Mob</c> with no <c>Character</c> and no
        /// <c>EventOnItemCollision</c>, so vanilla thrown items pass straight by
        /// it. 0 = vanilla. Shorter than <see cref="ZombieKnockoutSeconds"/> on
        /// every preset, at the maintainer's direction - a beetle's shell should
        /// visibly shrug off a thrown rock better than a zombie or a spider does.
        /// </summary>
        public static double BeetleKnockoutSeconds(PresetId preset) =>
            PresetValues.BeetleKnockoutSeconds(CatalogKey(preset));

        /// <summary>
        /// How fast a thrown item must be going, in <b>meters per second</b>, to
        /// knock out a beetle or a zombie. Shared by both so "a hard throw" means
        /// one thing. Lower = easier, so it falls on the more forgiving presets.
        ///
        /// Exists because matching the game's own <c>Bonkable</c> threshold (5
        /// world units/s) turned out to accept any contact at all - see
        /// <see cref="CreatureKnockout.VanillaBonkableThresholdUnits"/>. A gated
        /// parameter: it only means anything once a knockout duration is non-zero,
        /// so its config default is the tuned number rather than a vanilla one.
        /// </summary>
        public static double CreatureKnockoutMinThrowSpeed(PresetId preset) =>
            PresetValues.CreatureKnockoutMinThrowSpeed(CatalogKey(preset));

        /// <summary>
        /// How close to the creature the thrower must have been, in <b>meters</b>,
        /// for a thrown item to knock it out. The second half of the mechanic's
        /// cost, alongside <see cref="CreatureKnockoutMinThrowSpeed"/>: a hard
        /// throw is still travelling fast a long way out, so speed alone would
        /// license picking creatures off from safety. Higher = easier, so it rises
        /// on the more forgiving presets. Measured from
        /// <c>Item.lastHolderCharacter</c> at the moment of impact. Gated on a
        /// non-zero knockout duration, like the speed dial.
        /// </summary>
        public static double CreatureKnockoutMaxThrowDistance(PresetId preset) =>
            PresetValues.CreatureKnockoutMaxThrowDistance(CatalogKey(preset));

        /// <summary>
        /// Whether a blowgun dart takes a creature out of the fight: zombies die,
        /// spiders and beetles are stunned for
        /// <see cref="BlowgunCreatureStunSeconds"/>. See
        /// <c>Creatures/BlowgunCreaturePatch</c> for why the outcomes differ (only
        /// the zombie has a death state to reach) and why vanilla darts can't hit a
        /// spider or beetle at all. On for every preset - the dart is a consumable
        /// fired from an uncommon item, so the mechanic is self-limiting - but off
        /// in vanilla, and therefore off by default.
        /// </summary>
        public static bool BlowgunAffectsCreatures(PresetId preset) =>
            PresetValues.BlowgunAffectsCreatures(CatalogKey(preset));

        /// <summary>
        /// How long a blowgun dart stuns a spider or a beetle, in seconds.
        /// Zombies aren't covered by this - they die outright, which has no
        /// duration. Gated by <see cref="BlowgunAffectsCreatures"/>, so its config
        /// default is the tuned number rather than a vanilla one.
        /// </summary>
        public static double BlowgunCreatureStunSeconds(PresetId preset) =>
            PresetValues.BlowgunCreatureStunSeconds(CatalogKey(preset));

        /// <summary>
        /// Multiplier on the wind force a zombie receives. 1.0 = vanilla, which
        /// is already nonzero: a zombie is a bot <c>Character</c>, so the game
        /// pushes it at 0.6x what it pushes a player (see
        /// <see cref="CreatureWind"/>). Runs <em>above</em> 1.0 on the presets -
        /// wind shoving a chasing zombie around is help, not hazard.
        /// </summary>
        public static double ZombieWindMultiplier(PresetId preset) =>
            PresetValues.ZombieWindMultiplier(CatalogKey(preset));

        /// <summary>
        /// How susceptible beetles are to wind, as a fraction of their own
        /// walking speed - so 1.0 means wind slides a beetle about as fast as it
        /// walks. <b>0 is vanilla</b>, not 1.0, because vanilla beetles are
        /// completely wind-immune and cannot be made otherwise by scaling:
        /// <c>Mob.FixedUpdate</c> zeroes their velocity every tick. Any positive
        /// value grants an effect the game never had, which is why the presets
        /// climb from 0 rather than down towards it.
        /// </summary>
        public static double BeetleWindSusceptibility(PresetId preset) =>
            PresetValues.BeetleWindSusceptibility(CatalogKey(preset));

        // --- Wind ----------------------------------------------------------

        /// <summary>
        /// Multiplier applied to <c>WindChillZone.windForce</c>. 1.0 = vanilla.
        /// Gust duration/frequency is the separate
        /// <see cref="WindGustDurationMultiplier"/> row (split out 2026-07-22 at
        /// the maintainer's request so each can be tested independently).
        /// </summary>
        public static double WindForceMultiplier(PresetId preset) =>
            PresetValues.WindForceMultiplier(CatalogKey(preset));

        /// <summary>
        /// Multiplier applied to <c>windTimeRangeOn</c>'s duration (with
        /// <c>windTimeRangeOff</c> scaled inversely - see
        /// <see cref="WindTuning.ScaleWindRestDuration"/>). 1.0 = vanilla. Carries
        /// the same numbers as <see cref="WindForceMultiplier"/> per preset, but
        /// resolves independently so Custom can tune gust timing without changing
        /// push strength (and vice versa).
        /// </summary>
        public static double WindGustDurationMultiplier(PresetId preset) =>
            PresetValues.WindGustDurationMultiplier(CatalogKey(preset));

        /// <summary>
        /// Multiplier applied to <c>WindChillZone.windItemFactor</c> for every
        /// non-backpack ground item (backpacks are separately immune - see
        /// <see cref="WindBackpackAlwaysImmune"/>). 1.0 = vanilla; Subtle leaves
        /// other items untouched and the most forgiving preset reaching 0 is what
        /// "every item, backpacks included, is fully immune" means.
        /// </summary>
        public static double WindItemForceMultiplier(PresetId preset) =>
            PresetValues.WindItemForceMultiplier(CatalogKey(preset));

        /// <summary>
        /// Whether backpacks are fully immune to wind force regardless of
        /// <see cref="WindItemForceMultiplier"/> - on for every preset
        /// (ROADMAP.md's "backpack only" is the minimum immunity level), off in
        /// vanilla and therefore off by default. Turn it off to have backpacks
        /// blown around like any other ground item.
        /// </summary>
        public static bool WindBackpackAlwaysImmune(PresetId preset) =>
            PresetValues.WindBackpackAlwaysImmune(CatalogKey(preset));

        /// <summary>
        /// Multiplier applied to <c>WindChillZone.minRaycastDistance</c>/
        /// <c>maxRaycastDistance</c>. Runtime-confirmed (roots-runtime-findings
        /// memory) the raycast is already enabled in Roots
        /// (<c>useRaycast=true</c>, vanilla min=4/max=5) - this is a
        /// tune-not-build lever widening how far the occlusion check reaches, not
        /// a toggle, so the presets run <em>above</em> 1.0 (= vanilla).
        /// </summary>
        public static double WindObstacleOcclusionRangeMultiplier(PresetId preset) =>
            PresetValues.WindObstacleOcclusionRangeMultiplier(CatalogKey(preset));

        /// <summary>
        /// Floor value for <c>CharacterData.GetTargetRagdollControll()</c> while a
        /// fall is wind-preceded (see <see cref="WindTuning.IsWindForceStillRecent"/>),
        /// per the maintainer's scoping decision (2026-07-22): dampen only
        /// wind-preceded falls, not every fall, since an ordinary fall is the
        /// player's own doing but a wind-off-a-ledge fall is close to pure bad
        /// luck. 0 = off (vanilla, no clamp).
        /// </summary>
        public static double WindFallCameraDampenClamp(PresetId preset) =>
            PresetValues.WindFallCameraDampenClamp(CatalogKey(preset));

        /// <summary>
        /// How many seconds after wind force was last applied to the local
        /// character a subsequent fall still counts as "wind-preceded" - for both
        /// <see cref="WindFallCameraDampenClamp"/> and
        /// <see cref="PreventWindRagdoll"/>. A timing window rather than a
        /// strength dial, and gated on those two, so its config default is the
        /// tuned number rather than a vanilla one.
        /// </summary>
        public static float WindRecentForceWindowSeconds(PresetId preset) =>
            PresetValues.WindRecentForceWindowSeconds(CatalogKey(preset));

        /// <summary>
        /// Whether wind is allowed to ragdoll the player at all. On under every
        /// preset: a fall that wind caused
        /// (<see cref="WindTuning.IsWindForceStillRecent"/>, same window as the
        /// camera clamp) keeps the player fully in control instead of collapsing
        /// into physics - <see cref="WindTuning.ApplyWindRagdollImmunity"/>. Off =
        /// vanilla (and the default): wind blowing you off a ledge ragdolls you,
        /// and only the partial <see cref="WindFallCameraDampenClamp"/> floor
        /// applies.
        /// </summary>
        public static bool PreventWindRagdoll(PresetId preset) =>
            PresetValues.PreventWindRagdoll(CatalogKey(preset));

        /// <summary>
        /// Whether holding onto something (wall climbing, a rope, a vine, a climb
        /// handle) makes the player fully immune to wind force, at the cost of
        /// climbing slower while the wind is actually pushing on them.
        ///
        /// **Corrects an earlier misreading (2026-07-27).** The 2026-07-22
        /// decompile pass concluded this was tune-not-build because
        /// <c>WindChillZone.AddWindForceToCharacter</c> returns early whenever
        /// <c>character.data.currentClimbHandle != null</c>. That check only
        /// covers hanging off a climb *handle* prop - ordinary wall climbing
        /// (<c>CharacterData.isClimbing</c>), rope climbing and vine climbing all
        /// take full wind force in vanilla, and being shoved mid-climb drops the
        /// climb entirely (<c>CharacterClimbing.Update</c> lets go below 0.25
        /// ragdoll control). So it is built, not just tuned: see
        /// <see cref="ClimbWindResistance"/> for the mechanic and
        /// <c>Wind/ClimbWindShelterPatch.cs</c> for the patches.
        ///
        /// **Off on Subtle** (maintainer's call, 2026-07-27): Subtle's job is to
        /// leave vanilla mechanics as close to untouched as the mod gets, and
        /// handing out outright wind immunity is the least subtle thing in it. On
        /// for every other preset. Off in vanilla, so off by default too.
        /// </summary>
        public static bool ClimbSheltersFromWind(PresetId preset) =>
            PresetValues.ClimbSheltersFromWind(CatalogKey(preset));

        /// <summary>
        /// Multiplier applied to climb speed in every direction while wind is
        /// actually pushing on the climber (<see cref="ClimbWindResistance.Resist"/>),
        /// the price of the wind immunity climbing now grants. Faded in by live
        /// wind pressure, so a climber the wind can't reach anyway (behind a rock,
        /// no gust) is never slowed at all. Gentler on the forgiving presets,
        /// matching every other row's direction - there, "forgiving" means paying
        /// less for the shelter. Subtle's value is moot (the mechanic is off there
        /// - see <see cref="ClimbSheltersFromWind"/>) and is left at vanilla
        /// rather than a number that would misleadingly imply a Subtle slowdown
        /// exists.
        ///
        /// **Balanced is playtest-tuned** (maintainer, 2026-07-27), replacing a
        /// much harsher original estimate: with the immunity itself being the real
        /// prize, a heavy slowdown made waiting the gust out strictly better than
        /// climbing through it, which is the failure mode this mechanic exists to
        /// remove.
        /// </summary>
        public static double ClimbWindSpeedMultiplier(PresetId preset) =>
            PresetValues.ClimbWindSpeedMultiplier(CatalogKey(preset));

        /// <summary>
        /// Extra multiplier on *upward* climb movement only, on top of
        /// <see cref="ClimbWindSpeedMultiplier"/> - climbing up through a gust is
        /// the hardest thing you can do, per the maintainer's framing (2026-07-27).
        /// Downward movement is never penalised beyond the base multiplier.
        /// Balanced is playtest-tuned, Subtle is moot (mechanic off there) - see
        /// <see cref="ClimbWindSpeedMultiplier"/> for both.
        /// </summary>
        public static double ClimbWindUpwardSpeedMultiplier(PresetId preset) =>
            PresetValues.ClimbWindUpwardSpeedMultiplier(CatalogKey(preset));

        /// <summary>
        /// Extra multiplier on climb movement that opposes the wind direction, on
        /// top of <see cref="ClimbWindSpeedMultiplier"/>. Moving with the wind is
        /// never sped up - this mechanic is a cost, not a sail. Balanced is
        /// playtest-tuned, Subtle is moot (mechanic off there) - see
        /// <see cref="ClimbWindSpeedMultiplier"/> for both.
        /// </summary>
        public static double ClimbWindIntoWindSpeedMultiplier(PresetId preset) =>
            PresetValues.ClimbWindIntoWindSpeedMultiplier(CatalogKey(preset));

        /// <summary>
        /// How long the much-weaker-wind grace window lasts after letting go of a
        /// climb (see <see cref="ClimbWindResistance.GraceForceMultiplier"/> for
        /// why it exists). A timing window gated on
        /// <see cref="ClimbSheltersFromWind"/>, so its config default is the tuned
        /// number rather than a vanilla one.
        /// </summary>
        public static float ClimbShelterGraceSeconds(PresetId preset) =>
            PresetValues.ClimbShelterGraceSeconds(CatalogKey(preset));

        /// <summary>
        /// Multiplier applied to wind force during the short window just after a
        /// player lets go of a climb (<see cref="ClimbWindResistance.GraceForceMultiplier"/>) -
        /// the fix for "finishing a climb catapults you," which is the worst
        /// moment in a gust (maintainer, 2026-07-27). Low but deliberately
        /// non-zero on every preset that has the mechanic: full immunity here
        /// would let a player wall-tap their way across an exposed stretch.
        /// Subtle's vanilla value is moot, the whole mechanic is off there
        /// (<see cref="ClimbSheltersFromWind"/>).
        /// </summary>
        public static double ClimbWindGraceForceMultiplier(PresetId preset) =>
            PresetValues.ClimbWindGraceForceMultiplier(CatalogKey(preset));
    }
}
