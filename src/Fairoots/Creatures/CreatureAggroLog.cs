using System.Collections.Generic;
using Fairoots.Diagnostics;
using UnityEngine;

namespace Fairoots.Creatures
{
    /// <summary>
    /// Shared verbose logging for the two deaggro mechanics
    /// (<see cref="ZombieDeaggroPatch"/>, <see cref="BeetleDeaggroPatch"/>).
    ///
    /// Exists because both dials are, by nature, <b>hard to verify by eye</b>: a
    /// creature that stops chasing looks much the same whether it gave up because of
    /// this mod's threshold, because vanilla lost line of sight, or because it simply
    /// wandered off. Live testing of the beetle dial (2026-07-29) reached "I think it
    /// works, but it might be coincidence", which is exactly the situation this is for
    /// - it turns a judgement call into a log line naming the cause, the measured
    /// value, the threshold it was compared against, and the multiplier in force.
    ///
    /// Three kinds of line per creature, all gated behind <c>enable-debug-logging</c>:
    /// <list type="bullet">
    /// <item><b>aggro</b> - the frame a creature acquires a target.</item>
    /// <item><b>status</b> - a throttled heartbeat while chasing, so the run-up to a
    /// threshold is visible instead of only the moment it trips.</item>
    /// <item><b>deaggro</b> - the frame it loses one, naming which rule did it: this
    /// mod's, or vanilla's own (sight/consciousness/range).</item>
    /// </list>
    /// The status heartbeat is what makes a threshold checkable: the deaggro line alone
    /// proves the code ran, but the heartbeat approaching the limit and then tripping it
    /// proves the number means what it claims.
    /// </summary>
    internal static class CreatureAggroLog
    {
        /// <summary>
        /// How often a still-chasing creature reports its distance/sight state. Fast
        /// enough to watch a threshold approach in real time, slow enough that one
        /// zombie doesn't produce 60 lines a second.
        /// </summary>
        private const float StatusIntervalSeconds = 1f;

        private static readonly Dictionary<int, float> NextStatusTime = new Dictionary<int, float>();

        /// <summary>
        /// The reason this mod last forced a deaggro, keyed by creature instance ID, so
        /// the transition line can name the cause rather than just reporting that a
        /// target vanished. Cleared as it's consumed - a leftover reason would
        /// mislabel a later, unrelated vanilla deaggro.
        /// </summary>
        private static readonly Dictionary<int, string> PendingReason = new Dictionary<int, string>();

        internal static void ClearLevelState()
        {
            NextStatusTime.Clear();
            PendingReason.Clear();
        }

        /// <summary>Records that Fairoots itself caused this creature's next deaggro, and why.</summary>
        internal static void NoteForcedDeaggro(int instanceId, string reason)
        {
            PendingReason[instanceId] = reason;
        }

        /// <summary>
        /// Consumes and returns the recorded cause, or "vanilla (lost sight/range/target
        /// downed)" if this mod didn't cause it - which is itself the useful answer,
        /// since it says the dial is <em>not</em> what stopped the chase.
        /// </summary>
        internal static string ConsumeReason(int instanceId)
        {
            if (PendingReason.TryGetValue(instanceId, out string reason))
            {
                PendingReason.Remove(instanceId);
                return reason;
            }

            return "vanilla rule (lost sight, out of range, or target downed)";
        }

        /// <summary>Whether this creature is due to emit a status heartbeat.</summary>
        internal static bool DueForStatus(int instanceId)
        {
            if (NextStatusTime.TryGetValue(instanceId, out float next) && Time.time < next)
            {
                return false;
            }

            NextStatusTime[instanceId] = Time.time + StatusIntervalSeconds;
            return true;
        }

        /// <summary>
        /// A readable name for whoever is being chased - the Photon nickname when
        /// there is one, since "Player(Clone)" tells a tester nothing in a lobby.
        /// </summary>
        internal static string Describe(Character character)
        {
            if (character == null)
            {
                return "<none>";
            }

            string nickname = character.photonView?.Owner?.NickName;
            return string.IsNullOrEmpty(nickname) ? character.name : nickname;
        }

        internal static void Aggro(string kind, GameObject creature, Character target, float distanceUnits, string limits)
        {
            Diag.V(
                $"[Aggro] {kind} \"{creature.name}\" AGGROED {Describe(target)} " +
                $"at {GameUnits.ToMeters(distanceUnits):0.#}m - {limits}");
        }

        internal static void Status(string kind, GameObject creature, Character target, string detail)
        {
            Diag.V($"[Aggro] {kind} \"{creature.name}\" chasing {Describe(target)} - {detail}");
        }

        internal static void Deaggro(string kind, GameObject creature, Character target, string reason, string limits)
        {
            Diag.V(
                $"[Aggro] {kind} \"{creature.name}\" DEAGGROED {Describe(target)} - " +
                $"cause: {reason}; {limits}");
        }
    }
}
