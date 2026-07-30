using System;
using System.Collections.Generic;
using Fairoots.Core;
using Fairoots.Diagnostics;
using HarmonyLib;

namespace Fairoots.Creatures
{
    /// <summary>
    /// Phase 7 (ROADMAP.md), "Beetle knockback force": scales how hard a beetle's hit
    /// throws the player, from a per-instance cached vanilla baseline.
    ///
    /// <b>There is deliberately no zombie counterpart, and that's a finding rather
    /// than an omission.</b> A zombie applies no scripted knockback anywhere in the
    /// build:
    /// <list type="bullet">
    /// <item><c>MushroomZombieBiteCollider.OnTriggerEnter</c> - the zombie's only
    /// attack - applies Injury/Spores status, an <c>Affliction_ZombieBite</c>, and
    /// <c>character.Fall(biteStunTime)</c>. No force call.</item>
    /// <item><c>MushroomZombie</c> declares a <c>reachForce</c> field, which looks
    /// exactly like the lever this would want, but <b>nothing ever reads it</b> - the
    /// only <c>reachForce</c> actually used in the build belongs to
    /// <c>Scoutmaster</c>, an unrelated creature.</item>
    /// <item>The lunge is not a scripted impulse either: <c>StartLunging</c> just sets
    /// the zombie's own jump/sprint input and walks it at the player, so what a lunge
    /// does to you is ordinary Unity physics between two ragdolls.</item>
    /// </list>
    /// So what reads as "zombie knockback" in play is really the bite's ragdoll (the
    /// creature-ragdoll-resistance dial's business) plus a physics shove that has no
    /// number attached to it. Scaling it would mean inventing an impulse the game
    /// never applies, or altering the zombie's ragdoll mass - neither is "scaling
    /// vanilla knockback", so neither is done silently here.
    ///
    /// Baselines are cached per instance ID and every application is computed from the
    /// baseline, never the live field - same rule, and same reason, as
    /// <see cref="CreatureSpeedPatch"/>.
    /// </summary>
    internal static class CreatureKnockbackPatch
    {
        /// <summary>
        /// Vanilla <c>(bonkForce, bonkForceUp)</c> keyed by beetle instance ID. Both
        /// are stored (rather than one plus a ratio) so a beetle authored with an
        /// asymmetric shove keeps its own asymmetry on restore.
        /// </summary>
        private static readonly Dictionary<int, (float Forward, float Up)> Baselines =
            new Dictionary<int, (float, float)>();

        /// <summary>Re-applies the current multiplier to every live beetle.</summary>
        internal static void ReapplyToAll()
        {
            if (Plugin.Cfg == null || MobManager.instance == null)
            {
                return;
            }

            try
            {
                int count = 0;
                foreach (var mob in MobManager.instance.mobs)
                {
                    if (mob is Beetle beetle && ApplyToBeetle(beetle))
                    {
                        count++;
                    }
                }

                Diag.Info(
                    RootsState.Active
                        ? $"[Creatures] knockback reapply: beetle x{Plugin.Cfg.EffectiveBeetleKnockbackMultiplier:0.###} ({count} live)"
                        : $"[Creatures] not in Roots - vanilla knockback restored on {count} beetle(s)");
            }
            catch (Exception e)
            {
                Diag.Error($"[Creatures] knockback ReapplyToAll threw: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>Drops the baseline cache - called when the Roots level is torn down.</summary>
        internal static void ClearLevelState() => Baselines.Clear();

        /// <summary>
        /// Applies the knockback multiplier to one beetle, caching its authored
        /// forces the first time it's seen.
        /// </summary>
        internal static bool ApplyToBeetle(Beetle beetle)
        {
            if (beetle == null || Plugin.Cfg == null)
            {
                return false;
            }

            int id = beetle.GetInstanceID();
            if (!Baselines.TryGetValue(id, out var baseline))
            {
                baseline = (beetle.bonkForce, beetle.bonkForceUp);
                Baselines[id] = baseline;
            }

            // !RootsState.Active takes the restore branch rather than skipping, for
            // the same reason as CreatureSpeedPatch: Mob.Start fires for beetles in
            // every biome, and leaving Roots has to hand them back untouched.
            double multiplier = Plugin.Cfg.EffectiveBeetleKnockbackMultiplier;
            if (!RootsState.Active || CreatureTuning.IsVanilla(multiplier))
            {
                beetle.bonkForce = baseline.Forward;
                beetle.bonkForceUp = baseline.Up;
            }
            else
            {
                beetle.bonkForce = CreatureTuning.ScaleKnockback(baseline.Forward, multiplier);
                beetle.bonkForceUp = CreatureTuning.ScaleKnockback(baseline.Up, multiplier);
            }

            Diag.V(
                $"[Creatures]   beetle \"{beetle.gameObject.name}\" bonkForce {baseline.Forward:0.#}/{baseline.Up:0.#} " +
                $"-> {beetle.bonkForce:0.#}/{beetle.bonkForceUp:0.#}");
            return true;
        }
    }

    /// <summary>
    /// Applies the beetle knockback multiplier the moment a beetle goes live - same
    /// seam, and same reasoning, as <see cref="MobStartSpeedPatch"/>.
    /// </summary>
    [HarmonyPatch(typeof(Mob), "Start")]
    internal static class MobStartKnockbackPatch
    {
        private static void Postfix(Mob __instance)
        {
            if (__instance is Beetle beetle)
            {
                CreatureKnockbackPatch.ApplyToBeetle(beetle);
            }
        }
    }
}
