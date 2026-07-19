using BepInEx.Logging;

namespace Fairoots.Diagnostics
{
    /// <summary>
    /// Thin gated wrapper around the BepInEx log source. Verbose diagnostic lines
    /// go through <see cref="V"/> and only appear when the
    /// <c>Debug/enable-debug-logging</c> config toggle is on, so shipping the
    /// harness costs players nothing during normal play. Always-relevant lines
    /// (load banner, errors) use <see cref="Info"/>/<see cref="Warn"/> directly.
    ///
    /// This is game-facing (it holds a BepInEx <see cref="ManualLogSource"/>) and so
    /// deliberately lives outside <c>Core/</c>.
    /// </summary>
    internal static class Diag
    {
        internal static ManualLogSource Source;

        /// <summary>True when the player has opted into verbose diagnostics.</summary>
        internal static bool Enabled =>
            Plugin.Cfg != null && Plugin.Cfg.EnableDebugLogging.Value;

        internal static void Info(string msg) => Source?.LogInfo(msg);

        internal static void Warn(string msg) => Source?.LogWarning(msg);

        internal static void Error(string msg) => Source?.LogError(msg);

        /// <summary>A verbose diagnostic line - suppressed unless debug logging is on.</summary>
        internal static void V(string msg)
        {
            if (Enabled)
            {
                Source?.LogInfo(msg);
            }
        }
    }
}
