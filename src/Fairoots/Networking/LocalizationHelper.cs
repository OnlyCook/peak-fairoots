namespace Fairoots.Networking
{
    /// <summary>
    /// Shared lookup mirroring the maintainer's other PEAK mods (e.g.
    /// peak-checkpoint-save's own <c>LocalizationHelper</c>): array order
    /// matches <c>LocalizedText.Language</c>'s declaration order, the current
    /// language selects the entry, and an empty/missing entry (including an
    /// array that simply doesn't cover the current language index yet - see
    /// <see cref="ModPresenceLocalization"/>'s remarks) falls back to English
    /// (index 0), exactly like the game's own <c>LocalizedText.GetText</c> does.
    /// </summary>
    internal static class LocalizationHelper
    {
        public static string Resolve(string[] arr)
        {
            int idx = (int)LocalizedText.CURRENT_LANGUAGE;
            if (idx >= 0 && idx < arr.Length && !string.IsNullOrEmpty(arr[idx]))
            {
                return arr[idx];
            }

            return arr[0]; // English fallback
        }
    }
}
