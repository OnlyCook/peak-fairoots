using System.Collections.Generic;

namespace Fairoots.Networking
{
    /// <summary>
    /// Translations for the "not everyone in this lobby has Fairoots installed"
    /// dialog (<see cref="ModPresenceCheck"/>). English only for now (the
    /// maintainer wants to review the wording before other languages are
    /// added) - each array has a single English entry;
    /// <see cref="LocalizationHelper"/>'s own bounds check already falls back
    /// to index 0 for any language index the array doesn't cover, so a
    /// single-entry array is exactly equivalent to a full one where every
    /// other slot would otherwise be empty. Add more languages later by
    /// appending entries in <c>LocalizedText.Language</c>'s declaration order,
    /// same convention peak-checkpoint-save's <c>MessagesLocalization</c> uses.
    ///
    /// No em dashes anywhere in the English text, per the maintainer's request
    /// (so the wording is easy to review before other languages are added).
    /// </summary>
    internal enum ModPresenceMsgKey
    {
        DialogTitle,
        DialogBody,
        OkButton,
    }

    internal static class ModPresenceLocalization
    {
        private static readonly Dictionary<ModPresenceMsgKey, string[]> Table = new Dictionary<ModPresenceMsgKey, string[]>
        {
            [ModPresenceMsgKey.DialogTitle] = new[]
            {
                "Fairoots",
            },
            [ModPresenceMsgKey.DialogBody] = new[]
            {
                "Not everyone in this lobby has Fairoots installed. Fairoots needs every " +
                "player to have it installed, or gameplay will be inconsistent between " +
                "players (whoever is missing it will see full vanilla behavior instead of " +
                "the host's configured settings). Check the log for who is missing it.",
            },
            [ModPresenceMsgKey.OkButton] = new[]
            {
                "OK",
            },
        };

        /// <summary>Text for the current value of <c>LocalizedText.CURRENT_LANGUAGE</c>.</summary>
        public static string Get(ModPresenceMsgKey key) => LocalizationHelper.Resolve(Table[key]);
    }
}
