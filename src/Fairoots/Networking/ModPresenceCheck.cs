using System;
using System.Collections.Generic;
using System.Linq;
using ExitGames.Client.Photon;
using Fairoots.Diagnostics;
using Photon.Pun;

namespace Fairoots.Networking
{
    /// <summary>
    /// Tracks who in the lobby has Fairoots installed, backing the "every
    /// client needs Fairoots installed" requirement from ROADMAP.md's "Host
    /// authority" section - a client without the mod isn't merely "not
    /// tuned," it silently breaks the shared-experience premise entirely for
    /// itself (full vanilla spore bombs/wind while everyone else sees the
    /// host's configured version).
    ///
    /// Mechanism: every Fairoots client marks itself via a Photon player
    /// custom property (<see cref="InstalledPropertyKey"/>) as soon as it
    /// joins a room - this is already fully replicated to every other client
    /// by Photon itself, so any client (not just the host) can independently
    /// compute the exact same "who's missing it" answer via
    /// <see cref="GetMissingPlayers"/> with no extra networking of our own.
    ///
    /// The actual player-facing warning/decision now lives at the one moment
    /// it actually matters - clicking "Start" on the Boarding Pass
    /// (<see cref="BoardingPassStartGatePatch"/>) - not here. This class just
    /// keeps the underlying membership state current and logs passively
    /// (<see cref="Diag.Warn"/>) whenever the room composition changes, so
    /// there's always a fresh trail in <c>LogOutput.log</c> even between
    /// Boarding Pass visits.
    /// </summary>
    internal class ModPresenceCheck : MonoBehaviourPunCallbacks
    {
        private const string InstalledPropertyKey = "Fairoots.Installed";

        public override void OnJoinedRoom()
        {
            MarkSelfInstalled();
            LogIfMissing();
        }

        public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
        {
            LogIfMissing();
        }

        public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
        {
            LogIfMissing();
        }

        private static void MarkSelfInstalled()
        {
            try
            {
                if (!PhotonNetwork.InRoom)
                {
                    return;
                }

                PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { InstalledPropertyKey, true } });
            }
            catch (Exception e)
            {
                Diag.Error($"[ModPresenceCheck] MarkSelfInstalled threw: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void LogIfMissing()
        {
            try
            {
                var missing = GetMissingPlayers();
                if (missing.Count == 0)
                {
                    return;
                }

                Diag.Warn(
                    $"[ModPresenceCheck] {missing.Count} player(s) in this lobby do not have Fairoots " +
                    $"installed: {string.Join(", ", missing.Select(p => p.NickName))}. They'll be warned " +
                    "(with a chance to cancel) the next time anyone clicks Start on the Boarding Pass - " +
                    "see BoardingPassStartGatePatch.");
            }
            catch (Exception e)
            {
                Diag.Error($"[ModPresenceCheck] LogIfMissing threw: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// Every player currently in the room without <see cref="InstalledPropertyKey"/>
        /// set - empty if not in a room at all, or if everyone has it. Used
        /// both by the passive logging above and by
        /// <see cref="BoardingPassStartGatePatch"/>'s real-time check at the
        /// moment Start is clicked.
        /// </summary>
        internal static List<Photon.Realtime.Player> GetMissingPlayers()
        {
            if (!PhotonNetwork.InRoom)
            {
                return new List<Photon.Realtime.Player>();
            }

            return PhotonNetwork.PlayerList
                .Where(p => !IsInstalled(p))
                .OrderBy(p => p.ActorNumber)
                .ToList();
        }

        private static bool IsInstalled(Photon.Realtime.Player player)
        {
            return player.CustomProperties.TryGetValue(InstalledPropertyKey, out object value)
                && value is bool installed
                && installed;
        }
    }
}
