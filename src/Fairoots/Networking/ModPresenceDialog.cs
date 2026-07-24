using System;
using System.Reflection;
using Fairoots.Diagnostics;
using HarmonyLib;
using Peak.UI;
using Zorro.Core;

namespace Fairoots.Networking
{
    /// <summary>
    /// Shows the Boarding Pass Start-confirmation by entirely reusing the
    /// native game's own generic confirm dialog (<c>Peak.UI.ConfirmPage</c> -
    /// the same reusable OK/Cancel popup used for "Leave Game" and the
    /// save-destroy-on-join/host confirmations), rather than building custom
    /// UI. <c>ConfirmPage.Open</c> only accepts a localization-table key, so
    /// this opens it with a real existing key first (avoids a missing-key
    /// warning) and immediately overwrites the prompt's text with our own
    /// already-localized message via <c>LocalizedText.SetText</c> - the same
    /// "reflect the private field, then call the plain (non-localized)
    /// SetText" technique peak-checkpoint-save's <c>PauseMenuPatch.OpenConfirm</c>
    /// uses for the pause menu's own confirm window. The OK/Cancel button
    /// labels are left as whatever the native dialog already shows for them
    /// (not overridden), since that's what "fully native" means here.
    /// </summary>
    internal static class ModPresenceDialog
    {
        private static FieldInfo _promptField;
        private static bool _isOpen;

        /// <summary>True while the dialog is currently up - prevents stacking duplicates.</summary>
        internal static bool IsOpen => _isOpen;

        /// <summary>
        /// Shows the Cancel / Start Anyway confirmation. No-op (does not
        /// re-open or replace) if the dialog is already up. Exactly one of
        /// <paramref name="onConfirm"/>/<paramref name="onDecline"/> fires,
        /// whichever button is clicked.
        /// </summary>
        internal static void ShowStartConfirm(Action onConfirm, Action onDecline)
        {
            try
            {
                if (_isOpen)
                {
                    return;
                }

                _isOpen = true;

                ConfirmPage.Open(
                    "LEAVE_GAME_CONFIRM", // placeholder key, overwritten below - just needs to be a real one to avoid a log warning
                    () =>
                    {
                        _isOpen = false;
                        onConfirm?.Invoke();
                    },
                    () =>
                    {
                        _isOpen = false;
                        onDecline?.Invoke();
                    });

                OverridePromptText(ModPresenceLocalization.Get(ModPresenceMsgKey.DialogBody));
            }
            catch (Exception e)
            {
                Diag.Error($"[ModPresenceDialog] ShowStartConfirm threw: {e.GetType().Name}: {e.Message}");
                _isOpen = false;
                // Fail open - a bug in our own dialog should never permanently
                // block the player from starting the game at all.
                onConfirm?.Invoke();
            }
        }

        private static void OverridePromptText(string text)
        {
            var instance = RetrievableResourceSingleton<ConfirmPage>.Instance;
            if (instance == null)
            {
                return;
            }

            _promptField ??= AccessTools.Field(typeof(ConfirmPage), "prompt");
            if (_promptField?.GetValue(instance) is LocalizedText prompt)
            {
                prompt.SetText(text);
            }
        }
    }
}
