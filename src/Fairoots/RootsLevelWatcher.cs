using Fairoots.Diagnostics;
using Fairoots.SporeBombs;
using UnityEngine;

namespace Fairoots
{
    /// <summary>
    /// Detects a freshly-loaded Roots level and fires the one-shot per-level work
    /// (the spore-bomb cull, and the debug auto scene-scan) exactly once per
    /// instance. Polled from <see cref="Plugin.Update"/> rather than driven by a
    /// Harmony hook on level generation - <c>PropGrouper.RunAll</c> looked like the
    /// right seam (it's what <see cref="SceneDiagnostics"/> still assumes) but live
    /// testing showed it never actually fires: Roots prop placement is baked into
    /// the level scene at author time, not regenerated at runtime, so there is no
    /// "level generated" event to hook. Watching for the "Roots Segment" transform
    /// to appear works regardless of how the level was reached (fresh generation,
    /// a save resume, whatever) since it only depends on the scene actually being
    /// there.
    /// </summary>
    internal static class RootsLevelWatcher
    {
        /// <summary>
        /// The Roots Segment instance already processed this load. Unity's
        /// overridden `!= null` treats a destroyed object as null even though the
        /// C# reference isn't, so this naturally goes "stale" again when the level
        /// unloads without any explicit reset.
        /// </summary>
        private static Transform _processed;

        internal static void CheckAndRun()
        {
            if (_processed != null)
            {
                return;
            }

            Transform found = null;
            foreach (var t in Object.FindObjectsOfType<Transform>(true))
            {
                if (t.name == "Roots Segment")
                {
                    found = t;
                    break;
                }
            }

            if (found == null)
            {
                return;
            }

            _processed = found;
            SporeBombCullPatch.Run(found);

            if (Plugin.Cfg.EnableDebugLogging.Value && Plugin.Cfg.LogSceneScanOnLoad.Value)
            {
                SceneDiagnostics.AutoDump("Roots Segment detected");
            }
        }
    }
}
