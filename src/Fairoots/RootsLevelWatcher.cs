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
        /// The Roots Segment instance already processed this load. Checked via
        /// <c>gameObject.activeInHierarchy</c>, not a bare `!= null` - an earlier
        /// version only relied on Unity's overridden null-check making a
        /// *destroyed* object compare equal to null to detect "the level changed",
        /// which never re-triggered if the previous run's Roots Segment was instead
        /// left behind merely deactivated (not destroyed) rather than a full scene
        /// reload - live testing confirmed this: changing config (e.g.
        /// <see cref="PluginConfig.KeepVanillaTriggerRadius"/>) and starting a new
        /// run without restarting the game had zero effect, because the cull/shrink
        /// pass never ran again for the entire rest of the game session after the
        /// very first level. Checking `activeInHierarchy` catches both cases
        /// (destroyed *or* deactivated) while still avoiding a full scene scan on
        /// every frame once a still-active segment has already been processed.
        /// </summary>
        private static Transform _processed;

        internal static void CheckAndRun()
        {
            if (_processed != null && _processed.gameObject.activeInHierarchy)
            {
                return;
            }

            Transform found = null;
            foreach (var t in Object.FindObjectsOfType<Transform>(true))
            {
                if (t.name == "Roots Segment" && t.gameObject.activeInHierarchy)
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
