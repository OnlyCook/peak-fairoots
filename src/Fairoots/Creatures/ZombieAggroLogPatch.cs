using System.Collections.Generic;
using Fairoots.Core;
using Fairoots.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace Fairoots.Creatures
{
    /// <summary>
    /// Verbose aggro-lifecycle logging for zombies - see <see cref="CreatureAggroLog"/>
    /// for why the deaggro mechanics need to be observable rather than inferred.
    ///
    /// A postfix on <c>MushroomZombie.Update</c> that diffs the zombie's
    /// <c>currentTarget</c> against what it was last frame. Deliberately a diff rather
    /// than a hook on the assignment: a zombie's target is cleared through <b>two
    /// different paths</b> - <c>SetCurrentTarget(null)</c> (which round-trips through a
    /// Photon RPC) from <c>VerifyTarget</c>, and a direct <c>currentTarget = null</c>
    /// write from <c>ValidateTarget</c> - so patching either one alone would miss half
    /// the transitions. Comparing state per frame catches every path by construction,
    /// and costs one dictionary lookup per zombie per frame in a game that caps
    /// concurrent zombies at a handful (1 in Roots, runtime-confirmed).
    ///
    /// Entirely inert unless <c>enable-debug-logging</c> is on - the first thing it
    /// does is check, before touching any state.
    /// </summary>
    [HarmonyPatch(typeof(MushroomZombie), "Update")]
    internal static class ZombieAggroLogPatch
    {
        /// <summary>Last frame's target per zombie instance ID, for transition detection.</summary>
        private static readonly Dictionary<int, Character> LastTarget = new Dictionary<int, Character>();

        internal static void ClearLevelState() => LastTarget.Clear();

        private static void Postfix(MushroomZombie __instance)
        {
            if (!RootsState.Active || !Diag.Enabled || Plugin.Cfg == null)
            {
                return;
            }

            int id = __instance.GetInstanceID();
            Character current = __instance.currentTarget;
            LastTarget.TryGetValue(id, out Character previous);

            if (!ReferenceEquals(current, previous))
            {
                LastTarget[id] = current;
                LogTransition(__instance, previous, current);
                return;
            }

            if (current != null && CreatureAggroLog.DueForStatus(id))
            {
                CreatureAggroLog.Status("zombie", __instance.gameObject, current, DescribeChaseState(__instance, current));
            }
        }

        private static void LogTransition(MushroomZombie zombie, Character previous, Character current)
        {
            if (current != null)
            {
                var self = zombie.GetComponent<Character>();
                float distance = self == null ? 0f : Vector3.Distance(self.Center, current.Center);
                CreatureAggroLog.Aggro("zombie", zombie.gameObject, current, distance, DescribeLimits());
                return;
            }

            if (previous != null)
            {
                CreatureAggroLog.Deaggro(
                    "zombie",
                    zombie.gameObject,
                    previous,
                    CreatureAggroLog.ConsumeReason(zombie.GetInstanceID()),
                    DescribeLimits());
            }
        }

        /// <summary>
        /// The live measurements next to the thresholds they'll be compared against -
        /// this is the line that makes "did the multiplier actually do anything?"
        /// answerable, by showing the gap closing before it trips.
        /// </summary>
        private static string DescribeChaseState(MushroomZombie zombie, Character target)
        {
            double multiplier = Plugin.Cfg.EffectiveZombieDeaggroMultiplier;
            var self = zombie.GetComponent<Character>();
            float distance = self == null ? 0f : Vector3.Distance(self.Center, target.Center);
            float unseenFor = ZombieDeaggroPatch.SinceSeenTarget == null ? -1f : ZombieDeaggroPatch.SinceSeenTarget(zombie);

            string sight = unseenFor < 0f
                ? "unseen-for=<unavailable>"
                : $"unseen-for={unseenFor:0.#}s/{ZombieDeaggro.ResolveSightLossSeconds(multiplier):0.#}s";

            return
                $"distance={GameUnits.ToMeters(distance):0.#}m/" +
                $"{GameUnits.ToMeters(ZombieDeaggro.ResolveDistanceWorldUnits(multiplier)):0.#}m, {sight}";
        }

        private static string DescribeLimits()
        {
            double multiplier = Plugin.Cfg.EffectiveZombieDeaggroMultiplier;
            if (!Plugin.Cfg.EffectiveZombieDeaggroEnabled)
            {
                return "deaggro DISABLED (vanilla: zombies never give up)";
            }

            return
                $"multiplier={multiplier:0.##} => gives up after " +
                $"{ZombieDeaggro.ResolveSightLossSeconds(multiplier):0.#}s unseen or " +
                $"{GameUnits.ToMeters(ZombieDeaggro.ResolveDistanceWorldUnits(multiplier)):0.#}m away";
        }
    }
}
