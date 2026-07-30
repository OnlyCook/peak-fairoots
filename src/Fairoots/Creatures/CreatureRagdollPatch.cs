using System;
using System.Collections.Generic;
using Fairoots.Core;
using Fairoots.Diagnostics;
using HarmonyLib;

namespace Fairoots.Creatures
{
    /// <summary>
    /// Phase 7 (ROADMAP.md), the creature ragdoll dial: scales how long a beetle's hit
    /// or a zombie's bite keeps the player off their feet. At 0 the player is never
    /// knocked down by either creature.
    ///
    /// <b>One dial, two fields, and each field has exactly one caller</b> - which is
    /// what makes scaling the fields safe rather than needing a patch on
    /// <c>Character.Fall</c> itself (that method is the game's universal knockdown and
    /// is called by falls, rockfalls, items, spore bombs and the creatures' own
    /// self-ragdolls; scoping a patch there to "only creature hits on the player"
    /// would mean inferring intent from a call stack):
    /// <list type="bullet">
    /// <item><c>Beetle.ragdollTime</c> (vanilla 2s) - read only by
    /// <c>Beetle.InflictAttack</c>, which passes it straight to
    /// <c>character.Fall</c>.</item>
    /// <item><c>MushroomZombie.biteStunTime</c> (vanilla 3s) - read only by
    /// <c>MushroomZombieBiteCollider.OnTriggerEnter</c>, likewise.</item>
    /// </list>
    /// Note what is <em>not</em> in that list: the zombie's own
    /// <c>OnBitCharacter</c> does <c>character.Fall(8f)</c> on a hard-coded literal,
    /// but that <c>character</c> is the <em>zombie's</em>, not the player's - it's the
    /// zombie flopping over after a successful bite, and it is deliberately left
    /// alone. Likewise <c>Bonkable.ragdollTime</c>, which is a thrown item hitting
    /// something, not a creature hitting you.
    ///
    /// <b>Why 0 is genuinely "never ragdolled"</b>: vanilla's <c>Character.RPCA_Fall</c>
    /// only ever raises the timer (<c>if (seconds &gt; data.fallSeconds)</c>), so a
    /// zero-length knockdown can never satisfy that check and the player's control is
    /// left untouched. No special case needed here - see
    /// <see cref="CreatureTuning.ScaleRagdollTime"/>.
    ///
    /// Baselines cached per instance ID, applied from the baseline every time - same
    /// rule as <see cref="CreatureSpeedPatch"/> and <see cref="CreatureKnockbackPatch"/>.
    /// </summary>
    internal static class CreatureRagdollPatch
    {
        private static readonly Dictionary<int, float> BeetleBaselines = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> ZombieBaselines = new Dictionary<int, float>();

        /// <summary>Re-applies the current multiplier to every live beetle and zombie.</summary>
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
                        if (ApplyToZombie(zombie))
                        {
                            zombies++;
                        }
                    }
                }

                Diag.Info(
                    RootsState.Active
                        ? $"[Creatures] ragdoll reapply: x{Plugin.Cfg.EffectiveCreatureRagdollMultiplier:0.###} "
                          + $"({beetles} beetle(s), {zombies} zombie(s) live)"
                        : $"[Creatures] not in Roots - vanilla ragdoll restored on {beetles} beetle(s), {zombies} zombie(s)");
            }
            catch (Exception e)
            {
                Diag.Error($"[Creatures] ragdoll ReapplyToAll threw: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>Drops the baseline caches - called when the Roots level is torn down.</summary>
        internal static void ClearLevelState()
        {
            BeetleBaselines.Clear();
            ZombieBaselines.Clear();
        }

        internal static bool ApplyToBeetle(Beetle beetle)
        {
            if (beetle == null || Plugin.Cfg == null)
            {
                return false;
            }

            int id = beetle.GetInstanceID();
            if (!BeetleBaselines.TryGetValue(id, out float baseline))
            {
                baseline = beetle.ragdollTime;
                BeetleBaselines[id] = baseline;
            }

            // See CreatureSpeedPatch on why !RootsState.Active restores rather than skips.
            double multiplier = Plugin.Cfg.EffectiveCreatureRagdollMultiplier;
            beetle.ragdollTime = !RootsState.Active || CreatureTuning.IsVanilla(multiplier)
                ? baseline
                : CreatureTuning.ScaleRagdollTime(baseline, multiplier);

            Diag.V($"[Creatures]   beetle \"{beetle.gameObject.name}\" ragdollTime {baseline:0.##}s -> {beetle.ragdollTime:0.##}s");
            return true;
        }

        internal static bool ApplyToZombie(MushroomZombie zombie)
        {
            if (zombie == null || Plugin.Cfg == null)
            {
                return false;
            }

            int id = zombie.GetInstanceID();
            if (!ZombieBaselines.TryGetValue(id, out float baseline))
            {
                baseline = zombie.biteStunTime;
                ZombieBaselines[id] = baseline;
            }

            double multiplier = Plugin.Cfg.EffectiveCreatureRagdollMultiplier;
            zombie.biteStunTime = !RootsState.Active || CreatureTuning.IsVanilla(multiplier)
                ? baseline
                : CreatureTuning.ScaleRagdollTime(baseline, multiplier);

            Diag.V($"[Creatures]   zombie \"{zombie.gameObject.name}\" biteStunTime {baseline:0.##}s -> {zombie.biteStunTime:0.##}s");
            return true;
        }
    }

    /// <summary>
    /// Applies the ragdoll multiplier to a beetle the moment it goes live - same seam
    /// and reasoning as <see cref="MobStartSpeedPatch"/>.
    /// </summary>
    [HarmonyPatch(typeof(Mob), "Start")]
    internal static class MobStartRagdollPatch
    {
        private static void Postfix(Mob __instance)
        {
            if (__instance is Beetle beetle)
            {
                CreatureRagdollPatch.ApplyToBeetle(beetle);
            }
        }
    }

    /// <summary>
    /// Applies the ragdoll multiplier to a zombie the moment it spawns - same seam and
    /// reasoning as <see cref="MushroomZombieStartSpeedPatch"/>. Load-bearing for
    /// zombies specifically: they're spawned at runtime, so no level-load pass could
    /// ever cover them.
    /// </summary>
    [HarmonyPatch(typeof(MushroomZombie), "Start")]
    internal static class MushroomZombieStartRagdollPatch
    {
        private static void Postfix(MushroomZombie __instance)
        {
            CreatureRagdollPatch.ApplyToZombie(__instance);
        }
    }
}
