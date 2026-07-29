using System;
using System.Collections.Generic;
using Fairoots.Core;
using Fairoots.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace Fairoots.Creatures
{
    /// <summary>
    /// Phase 7 (ROADMAP.md), the beetle half of the deaggro tuning. Beetles already
    /// give up in vanilla - <c>Mob.Targeting()</c> re-picks the nearest conscious
    /// player within <c>aggroDistance</c> (14 world units, ~22.4m on the Roots prefab -
    /// <c>Mob</c>'s class default of 5 is not what ships) that it has clear line
    /// of sight to, and assigns the result <b>including <c>null</c></b> - so this is
    /// tuning, not invention, and 1.0 means exactly vanilla.
    ///
    /// <b>Rewritten 2026-07-29 after live testing found the first version inert</b> at
    /// both extremes. <see cref="BeetleDeaggro"/> has the full explanation; the short
    /// version is that vanilla uses one number for both "who do I notice" and "do I
    /// still have them", re-answered from scratch every couple of seconds, so:
    /// <list type="bullet">
    /// <item>Shrinking that number only while a target existed <b>undid itself</b> - the
    /// drop left the beetle target-less, so the next scan ran at full vanilla range and
    /// instantly re-acquired the same player. Fixed by <see cref="SuppressedUntil"/>, a
    /// short window in which the beetle acquires nobody.</item>
    /// <item>Widening it did almost nothing, because vanilla retention also demands an
    /// unbroken <c>LineCheck</c>, and in Roots sight breaks well before 22m does. Fixed
    /// by holding an in-range target directly (skipping the scan) rather than hoping a
    /// bigger radius survives the sight test.</item>
    /// </list>
    ///
    /// The patch therefore takes one of three routes per call, and only ever the one:
    /// <list type="number">
    /// <item><b>Holding</b> - has a target still inside the scaled retention radius:
    /// skip the native scan entirely, so the target is kept regardless of line of sight
    /// or re-check cooldowns.</item>
    /// <item><b>Giving up</b> - has a target beyond it: zero <c>aggroDistance</c> and
    /// let the native scan run, so it assigns <c>null</c> <em>through the game's own
    /// property setter</em> (which fires the Photon RPC that tells other clients the
    /// beetle lost its target - writing the backing field directly would desync them),
    /// then start the suppression window.</item>
    /// <item><b>Suppressed or idle</b> - suppressed: zero <c>aggroDistance</c> so
    /// nothing is acquired. Otherwise: leave the call completely alone, which is what
    /// keeps acquisition exactly vanilla.</item>
    /// </list>
    ///
    /// <c>aggroDistance</c> is always restored in the postfix from a value carried
    /// through Harmony's <c>__state</c>, so it can't drift even if the native method
    /// throws - the same borrow-and-restore shape <c>ClimbWindRopeSlowdownPatch</c>
    /// uses for <c>climbSpeedMod</c>.
    /// </summary>
    [HarmonyPatch(typeof(Mob), "Targeting")]
    internal static class BeetleDeaggroPatch
    {
        /// <summary>
        /// Per-beetle time (<c>Time.time</c>) until which it acquires nobody, after
        /// this dial made it give up. Keyed by instance ID. Load-bearing - see class
        /// remarks; without it the mechanic cancels itself out.
        /// </summary>
        private static readonly Dictionary<int, float> SuppressedUntil = new Dictionary<int, float>();

        /// <summary>Drops the suppression state - called when the Roots level is torn down.</summary>
        internal static void ClearLevelState() => SuppressedUntil.Clear();

        /// <summary>
        /// What the prefix hands the postfix. More than the restore value, because the
        /// postfix is also where aggro/deaggro transitions are detected - it has to know
        /// what the target was <em>before</em> the native scan ran to see it change.
        /// </summary>
        internal sealed class TargetingState
        {
            /// <summary>Vanilla <c>aggroDistance</c> to put back, or null if untouched.</summary>
            internal float? RestoreAggroDistance;

            /// <summary>The target before the native scan, for transition detection.</summary>
            internal Character TargetBefore;

            /// <summary>True when the native scan was skipped, so the target can't have changed.</summary>
            internal bool Skipped;
        }

        private static bool Prefix(Mob __instance, out TargetingState __state)
        {
            __state = null;

            // Scorpion is the other Mob subclass in the build and isn't a Roots creature.
            if (Plugin.Cfg == null || !(__instance is Beetle))
            {
                return true;
            }

            int id = __instance.GetInstanceID();
            Character target = GetTarget(__instance);
            __state = new TargetingState { TargetBefore = target };

            double multiplier = Plugin.Cfg.EffectiveBeetleDeaggroMultiplier;
            if (CreatureTuning.IsVanilla(multiplier))
            {
                return true; // 1.0 is vanilla by doing nothing at all, not by arithmetic.
            }

            if (target != null && target.data != null && target.data.fullyConscious)
            {
                // vanilla Targeting measures from transform.position, not Center - match it.
                float distance = Vector3.Distance(__instance.transform.position, target.Center);

                if (BeetleDeaggro.ShouldKeepTarget(distance, __instance.aggroDistance, multiplier))
                {
                    SuppressedUntil.Remove(id);
                    __state.Skipped = true;

                    if (Diag.Enabled && CreatureAggroLog.DueForStatus(id))
                    {
                        CreatureAggroLog.Status(
                            "beetle", __instance.gameObject, target,
                            $"distance={GameUnits.ToMeters(distance):0.#}m/" +
                            $"{GameUnits.ToMeters(BeetleDeaggro.ResolveRetentionDistance(__instance.aggroDistance, multiplier)):0.#}m " +
                            "(held past vanilla's line-of-sight test)");
                    }

                    return false; // Hold on to it: skip the scan, sight test included.
                }

                SuppressedUntil[id] = Time.time + BeetleDeaggro.SuppressionSeconds;

                // Capture the vanilla radius (and the limit derived from it) BEFORE
                // zeroing the field, or both the restore and the log line read 0.
                float vanillaAggro = __instance.aggroDistance;
                float limit = BeetleDeaggro.ResolveRetentionDistance(vanillaAggro, multiplier);
                __state.RestoreAggroDistance = vanillaAggro;
                __instance.aggroDistance = 0f; // Native scan finds nobody -> assigns null via the real setter.

                CreatureAggroLog.NoteForcedDeaggro(
                    id,
                    $"Fairoots deaggro rule - {GameUnits.ToMeters(distance):0.#}m away " +
                    $"(limit {GameUnits.ToMeters(limit):0.#}m), now ignoring everyone for " +
                    $"{BeetleDeaggro.SuppressionSeconds:0.#}s");
                return true;
            }

            if (SuppressedUntil.TryGetValue(id, out float until))
            {
                if (Time.time < until)
                {
                    __state.RestoreAggroDistance = __instance.aggroDistance;
                    __instance.aggroDistance = 0f;
                    return true;
                }

                SuppressedUntil.Remove(id);
                Diag.V(
                    $"[Aggro] beetle \"{__instance.gameObject.name}\" suppression expired - " +
                    "may notice players again");
            }

            return true; // Idle and not suppressed - vanilla acquisition, untouched.
        }

        /// <summary>
        /// Always restores the game's own value, whatever the native method did, and
        /// emits the aggro/deaggro transition line by diffing the target across the
        /// native scan. The diff lives here rather than on the target assignment
        /// because the native scan assigns <c>targetChar</c> unconditionally - including
        /// <c>null</c> - so "did this call change anything" is only answerable after it.
        /// </summary>
        private static void Postfix(Mob __instance, TargetingState __state)
        {
            if (__state == null)
            {
                return;
            }

            if (__state.RestoreAggroDistance.HasValue)
            {
                __instance.aggroDistance = __state.RestoreAggroDistance.Value;
            }

            if (!Diag.Enabled || __state.Skipped)
            {
                return; // A skipped scan can't have changed the target.
            }

            Character after = GetTarget(__instance);
            if (ReferenceEquals(after, __state.TargetBefore))
            {
                return;
            }

            if (after != null)
            {
                float distance = Vector3.Distance(__instance.transform.position, after.Center);
                CreatureAggroLog.Aggro("beetle", __instance.gameObject, after, distance, DescribeLimits(__instance));
            }
            else if (__state.TargetBefore != null)
            {
                CreatureAggroLog.Deaggro(
                    "beetle",
                    __instance.gameObject,
                    __state.TargetBefore,
                    CreatureAggroLog.ConsumeReason(__instance.GetInstanceID()),
                    DescribeLimits(__instance));
            }
        }

        private static string DescribeLimits(Mob beetle)
        {
            double multiplier = Plugin.Cfg.EffectiveBeetleDeaggroMultiplier;
            return
                $"multiplier={multiplier:0.##} => keeps chasing to " +
                $"{GameUnits.ToMeters(BeetleDeaggro.ResolveRetentionDistance(beetle.aggroDistance, multiplier)):0.#}m " +
                $"(vanilla {GameUnits.ToMeters(beetle.aggroDistance):0.#}m)";
        }

        /// <summary>
        /// This beetle's current target, read from <c>Mob._targetChar</c> - the backing
        /// field behind the private <c>targetChar</c> property. Deliberately the field
        /// and not the property: the property's <em>setter</em> fires a Photon RPC, and
        /// while that's exactly what we want when clearing a target (route 2 above lets
        /// the native code do it), a read-only question has no business near it.
        /// </summary>
        private static Character GetTarget(Mob mob)
        {
            return TargetChar == null ? null : TargetChar(mob);
        }

        /// <summary>
        /// Accessor for <c>Mob._targetChar</c> (private). Resolved once; null if the
        /// field is ever renamed, in which case <see cref="GetTarget"/> reports "no
        /// target" and the dial degrades to vanilla targeting rather than throwing
        /// inside the AI's own scan.
        /// </summary>
        private static readonly AccessTools.FieldRef<Mob, Character> TargetChar = ResolveTargetChar();

        private static AccessTools.FieldRef<Mob, Character> ResolveTargetChar()
        {
            try
            {
                return AccessTools.FieldRefAccess<Mob, Character>("_targetChar");
            }
            catch (Exception e)
            {
                Diag.Error(
                    $"[Creatures] could not bind Mob._targetChar ({e.GetType().Name}) - " +
                    "beetle deaggro tuning disabled (vanilla behavior).");
                return null;
            }
        }
    }
}
