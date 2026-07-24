using System;
using Fairoots.Diagnostics;
using HarmonyLib;

namespace Fairoots.Networking
{
    /// <summary>
    /// Enforces "every client needs Fairoots installed" (ROADMAP.md's "Host
    /// authority" section) at the one moment it actually matters: clicking
    /// "Start" on the Boarding Pass (opened via the Gate Kiosk) - confirmed
    /// via decompile to be <c>BoardingPass.StartGame()</c>, callable by any
    /// player (not just the host - it sends an RPC to whoever the
    /// MasterClient is), which is exactly why this has to be checked
    /// client-side on whoever clicks it, not just the host.
    ///
    /// If everyone in the room has Fairoots installed
    /// (<see cref="ModPresenceCheck.GetMissingPlayers"/> is empty), this is a
    /// complete no-op - the original method runs immediately, same as
    /// vanilla. Otherwise the original call is suppressed and
    /// <see cref="ModPresenceDialog.ShowStartConfirm"/> takes over: Cancel
    /// leaves the Boarding Pass exactly as if Start was never clicked;
    /// Confirm re-invokes <c>StartGame()</c> for real (via
    /// <see cref="_bypassNextCheck"/>, so the same click doesn't just loop
    /// back into another confirmation).
    /// </summary>
    [HarmonyPatch(typeof(BoardingPass), nameof(BoardingPass.StartGame))]
    internal static class BoardingPassStartGatePatch
    {
        private static bool _bypassNextCheck;

        private static bool Prefix(BoardingPass __instance)
        {
            try
            {
                if (_bypassNextCheck)
                {
                    _bypassNextCheck = false;
                    return true; // the player already confirmed once - let it through this time.
                }

                var missing = ModPresenceCheck.GetMissingPlayers();
                if (missing.Count == 0)
                {
                    return true; // everyone has the mod - completely normal vanilla start.
                }

                Diag.Warn(
                    $"[BoardingPassStartGatePatch] Start blocked pending confirmation - {missing.Count} " +
                    $"player(s) missing Fairoots: {string.Join(", ", missing.ConvertAll(p => p.NickName))}");

                ModPresenceDialog.ShowStartConfirm(
                    onConfirm: () =>
                    {
                        _bypassNextCheck = true;
                        __instance.StartGame();
                    },
                    onDecline: () => Diag.Warn("[BoardingPassStartGatePatch] Start canceled by the player."));

                return false; // suppress the original click until the player decides.
            }
            catch (Exception e)
            {
                Diag.Error($"[BoardingPassStartGatePatch] threw: {e.GetType().Name}: {e.Message}");
                return true; // fail open - never let a bug in our check block starting the game entirely.
            }
        }
    }
}
