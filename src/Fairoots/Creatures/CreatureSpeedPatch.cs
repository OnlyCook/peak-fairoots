using System;
using System.Collections.Generic;
using Fairoots.Core;
using Fairoots.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace Fairoots.Creatures
{
    /// <summary>
    /// Phase 7 (ROADMAP.md), "Zombie/beetle move speed": scales how fast the two
    /// chasing creatures actually move, from a per-instance cached vanilla baseline.
    ///
    /// <b>Two different native fields, same meaning</b> - the two creatures share no
    /// code at all (see <see cref="CreatureScan"/>):
    /// <list type="bullet">
    /// <item><b>Beetle</b>: <c>Mob.movementSpeed</c> (vanilla 5), used directly as a
    /// per-second position step in <c>Mob.Movement()</c>.</item>
    /// <item><b>Zombie</b>: <c>CharacterMovement.movementForce</c> (vanilla 10) on the
    /// zombie's own <c>CharacterMovementZombie</c>. <b>This resolves RESEARCH.md's Q8
    /// open question</b> ("which base-class field actually governs zombie speed"):
    /// <c>CharacterMovementZombie</c> overrides only <c>EvaluateGroundChecks</c> and
    /// declares no speed field of its own, and <c>CharacterMovement.GetMovementForce()</c>
    /// computes <c>movementForce * movementModifier</c>, then multiplies by
    /// <c>sprintMultiplier</c> while sprinting. So scaling <c>movementForce</c> scales
    /// walk and sprint together, which is what the setting should mean.</item>
    /// </list>
    ///
    /// <b>Why <c>movementForce</c> and not the sibling <c>movementModifier</c></b>,
    /// which reads like the field designed for exactly this: the game's own
    /// energy-drink affliction adjusts <c>movementModifier</c> <em>additively</em>
    /// (<c>movementModifier += moveSpeedMod</c> on apply, <c>-=</c> on removal), so
    /// writing a computed value into it would clobber the affliction's bookkeeping and
    /// leave the zombie permanently mis-scaled once the affliction expired. Same trap,
    /// same resolution as <c>climbSpeedMod</c> in <c>ClimbWindShelterPatch</c>: scale
    /// the base magnitude, never the modifier the game itself accumulates into.
    ///
    /// <b>Baselines are cached per instance ID</b> and every application is computed
    /// from the baseline, never from the field's current value - the established
    /// pattern from <c>WindChillZoneTuningPatch</c> and <c>SporeAreaTuningPatch</c>.
    /// Without it, a reapply would compound (0.8 twice = 0.64) and 1.0 would stop
    /// meaning vanilla. Prefab values are read live rather than assumed, since a
    /// specific zombie or beetle instance may have been authored off the class default.
    /// </summary>
    internal static class CreatureSpeedPatch
    {
        /// <summary>Vanilla <c>Mob.movementSpeed</c>, keyed by beetle instance ID.</summary>
        private static readonly Dictionary<int, float> BeetleBaselines = new Dictionary<int, float>();

        /// <summary>Vanilla <c>CharacterMovement.movementForce</c>, keyed by zombie instance ID.</summary>
        private static readonly Dictionary<int, float> ZombieBaselines = new Dictionary<int, float>();

        /// <summary>
        /// Re-applies the current multipliers to every live beetle and zombie. Wired
        /// to the settings' own <c>SettingChanged</c> (gated on
        /// <c>apply-changes-live</c>, like the wind/spore-area tuning dials - a speed
        /// change can be undone, unlike a removal) and to <c>HostAuthoritySync</c>'s
        /// room-property update, so a client whose level load raced ahead of the
        /// host's first publish still converges on the host's numbers.
        ///
        /// Reads the managers' own registries rather than
        /// <c>FindObjectsOfType</c>: <c>MobManager.mobs</c> and
        /// <c>ZombieManager.zombies</c> are exactly the live-creature lists the game
        /// already maintains, and a full-scene sweep is the kind of unconditional
        /// scan that already cost this mod a mod-wide framerate drop once.
        /// </summary>
        internal static void ReapplyToAll()
        {
            if (Plugin.Cfg == null)
            {
                return;
            }

            try
            {
                int beetles = 0, zombies = 0;

                if (MobManager.instance != null)
                {
                    foreach (var mob in MobManager.instance.mobs)
                    {
                        if (mob is Beetle beetle && ApplyToBeetle(beetle))
                        {
                            beetles++;
                        }
                    }
                }

                var zombieManager = ZombieManager.Instance;
                if (zombieManager != null)
                {
                    foreach (var zombie in zombieManager.zombies)
                    {
                        if (zombie != null && ApplyToZombie(zombie))
                        {
                            zombies++;
                        }
                    }
                }

                Diag.Info(
                    $"[Creatures] speed reapply: zombie x{Plugin.Cfg.EffectiveZombieSpeedMultiplier:0.###} " +
                    $"({zombies} live), beetle x{Plugin.Cfg.EffectiveBeetleSpeedMultiplier:0.###} ({beetles} live)");
            }
            catch (Exception e)
            {
                Diag.Error($"[Creatures] speed ReapplyToAll threw: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>Drops the baseline caches - called when the Roots level is torn down.</summary>
        internal static void ClearLevelState()
        {
            BeetleBaselines.Clear();
            ZombieBaselines.Clear();
        }

        /// <summary>
        /// Applies the beetle speed multiplier to one beetle, caching its authored
        /// <c>movementSpeed</c> the first time it's seen. Returns whether it was
        /// applied (i.e. the beetle was usable).
        /// </summary>
        internal static bool ApplyToBeetle(Beetle beetle)
        {
            if (beetle == null || Plugin.Cfg == null)
            {
                return false;
            }

            int id = beetle.GetInstanceID();
            if (!BeetleBaselines.TryGetValue(id, out float baseline))
            {
                baseline = beetle.movementSpeed;
                BeetleBaselines[id] = baseline;
            }

            double multiplier = Plugin.Cfg.EffectiveBeetleSpeedMultiplier;
            beetle.movementSpeed = CreatureTuning.IsVanilla(multiplier)
                ? baseline
                : CreatureTuning.ScaleMovementSpeed(baseline, multiplier);

            Diag.V($"[Creatures]   beetle \"{beetle.gameObject.name}\" movementSpeed {baseline:0.##} -> {beetle.movementSpeed:0.##}");
            return true;
        }

        /// <summary>
        /// Applies the zombie speed multiplier to one zombie, caching its authored
        /// <c>movementForce</c> the first time it's seen. Returns whether it was
        /// applied - a zombie whose <c>Character</c>/movement refs aren't wired up yet
        /// is skipped rather than caching a garbage baseline off a half-built object.
        /// </summary>
        internal static bool ApplyToZombie(MushroomZombie zombie)
        {
            if (zombie == null || Plugin.Cfg == null)
            {
                return false;
            }

            // GetComponentInChildren rather than the zombie's own Character.refs.movement:
            // MushroomZombie.character is `internal` to Assembly-CSharp, so it isn't
            // reachable from this assembly at all. The zombie's CharacterMovementZombie
            // is a CharacterMovement, so this finds it either way.
            CharacterMovement movement = zombie.GetComponentInChildren<CharacterMovement>(true);
            if (movement == null)
            {
                return false;
            }

            int id = movement.GetInstanceID();
            if (!ZombieBaselines.TryGetValue(id, out float baseline))
            {
                baseline = movement.movementForce;
                ZombieBaselines[id] = baseline;
            }

            double multiplier = Plugin.Cfg.EffectiveZombieSpeedMultiplier;
            movement.movementForce = CreatureTuning.IsVanilla(multiplier)
                ? baseline
                : CreatureTuning.ScaleMovementSpeed(baseline, multiplier);

            Diag.V($"[Creatures]   zombie \"{zombie.gameObject.name}\" movementForce {baseline:0.##} -> {movement.movementForce:0.##}");
            return true;
        }
    }

    /// <summary>
    /// Applies the beetle speed multiplier the moment a beetle goes live.
    /// <c>Mob.Start</c> is the game's own "this creature is now registered and
    /// running" seam, so it's both the earliest safe point to read an authored
    /// baseline and the one that catches a beetle created after the level-load pass.
    /// Scoped to <see cref="Beetle"/> - <c>Scorpion</c> is the other <c>Mob</c>
    /// subclass in the build and isn't a Roots creature.
    /// </summary>
    [HarmonyPatch(typeof(Mob), "Start")]
    internal static class MobStartSpeedPatch
    {
        private static void Postfix(Mob __instance)
        {
            if (__instance is Beetle beetle)
            {
                CreatureSpeedPatch.ApplyToBeetle(beetle);
            }
        }
    }

    /// <summary>
    /// Applies the zombie speed multiplier the moment a zombie goes live. Zombies are
    /// spawned at runtime (never placed in the scene), so unlike the beetles there is
    /// no level-load pass that could cover them - this hook is the only thing that
    /// catches a freshly spawned zombie, and <c>ReapplyToAll</c> only handles the ones
    /// already standing when a setting changes.
    ///
    /// <c>Start</c> rather than <c>Awake</c>: <c>MushroomZombie.Awake</c> is where
    /// <c>character</c> is resolved, so at <c>Start</c> the refs this needs are
    /// guaranteed wired up.
    /// </summary>
    [HarmonyPatch(typeof(MushroomZombie), "Start")]
    internal static class MushroomZombieStartSpeedPatch
    {
        private static void Postfix(MushroomZombie __instance)
        {
            CreatureSpeedPatch.ApplyToZombie(__instance);
        }
    }
}
