using System;
using System.Collections.Generic;
using System.Linq;
using ExitGames.Client.Photon;
using Fairoots.Diagnostics;
using Photon.Pun;

namespace Fairoots.Networking
{
    /// <summary>
    /// Enforces the "every client needs Fairoots installed" requirement from
    /// ROADMAP.md's "Host authority" section - a client without the mod isn't
    /// merely "not tuned," it silently breaks the shared-experience premise
    /// entirely for itself (full vanilla spore bombs/wind while everyone else
    /// sees the host's configured version), so this warns as soon as a gap is
    /// detected rather than leaving it to be discovered by confused players.
    ///
    /// Mechanism: every Fairoots client marks itself via a Photon player
    /// custom property (<see cref="InstalledPropertyKey"/>) as soon as it
    /// joins a room. Every client (any of them, not just the host - "everyone
    /// benefits from knowing the lobby isn't fully in sync" won out over
    /// "only the host needs to know") then checks every other player in the
    /// room for that same property. A missing property means that player
    /// either doesn't have Fairoots at all, or hasn't finished joining/setting
    /// it yet (a brief, expected transient state, not a real gap).
    ///
    /// No player names ever appear in <see cref="ModPresenceDialog"/> itself
    /// (would clip/bloat with several missing players, per the maintainer's
    /// request) - only in the log, via <see cref="Diag"/>. The dialog only
    /// (re)opens when the *specific set* of missing players changes, so it
    /// doesn't reappear on every routine re-check once you've already seen it
    /// for the current gap.
    /// </summary>
    internal class ModPresenceCheck : MonoBehaviourPunCallbacks
    {
        private const string InstalledPropertyKey = "Fairoots.Installed";

        /// <summary>
        /// A stable signature of whoever was missing the last time the dialog
        /// was shown (sorted actor numbers, joined) - re-showing only happens
        /// when this changes (a new gap, not the same one persisting).
        /// Cleared back to empty once nobody is missing, so a future gap
        /// (even with the exact same players, e.g. someone reconnecting
        /// without the mod) triggers a fresh warning.
        /// </summary>
        private string _lastWarnedSignature = string.Empty;

        public override void OnJoinedRoom()
        {
            MarkSelfInstalled();
            Recheck();
        }

        public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
        {
            Recheck();
        }

        public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
        {
            Recheck();
        }

        private void MarkSelfInstalled()
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

        private void Recheck()
        {
            try
            {
                if (!PhotonNetwork.InRoom)
                {
                    return;
                }

                var missing = PhotonNetwork.PlayerList
                    .Where(p => !IsInstalled(p))
                    .OrderBy(p => p.ActorNumber)
                    .ToList();

                if (missing.Count == 0)
                {
                    _lastWarnedSignature = string.Empty;
                    return;
                }

                string signature = string.Join(",", missing.Select(p => p.ActorNumber));
                if (signature == _lastWarnedSignature)
                {
                    return; // same gap already warned about - not a new occurrence.
                }

                _lastWarnedSignature = signature;

                Diag.Warn(
                    $"[ModPresenceCheck] {missing.Count} player(s) in this lobby do not have Fairoots " +
                    $"installed: {string.Join(", ", missing.Select(p => p.NickName))}. Shared Fairoots " +
                    "features (spore-bomb culling, wind tuning) will be inconsistent for them - see " +
                    "ROADMAP.md's Host authority section.");

                ModPresenceDialog.Show();
            }
            catch (Exception e)
            {
                Diag.Error($"[ModPresenceCheck] Recheck threw: {e.GetType().Name}: {e.Message}");
            }
        }

        private static bool IsInstalled(Photon.Realtime.Player player)
        {
            return player.CustomProperties.TryGetValue(InstalledPropertyKey, out object value)
                && value is bool installed
                && installed;
        }
    }
}
