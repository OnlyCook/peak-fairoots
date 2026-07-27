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
    ///
    /// Two perf guards, both load-bearing (an earlier version without them caused a
    /// severe, mod-wide framerate drop - worst in non-Roots biomes, where "Roots
    /// Segment" never appears so the unguarded scan below never stopped running):
    /// polled at <see cref="ScanIntervalSeconds"/> rather than every single
    /// <c>Update()</c> tick, and searching via <see cref="GameObject.Find(string)"/>
    /// (a targeted named lookup) instead of <c>Object.FindObjectsOfType&lt;Transform&gt;</c>
    /// (which allocates an array of literally every Transform in every loaded
    /// scene, active or not, and did so unconditionally every frame).
    /// </summary>
    internal static class RootsLevelWatcher
    {
        /// <summary>
        /// How often to poll for "Roots Segment" appearing/disappearing. Small
        /// enough that a level transition is picked up within a fraction of a
        /// second (nothing here is latency-sensitive - the cull pass only needs to
        /// run once per level, not on the exact frame the segment activates), large
        /// enough to keep this a non-issue for framerate even in the worst case
        /// (polling indefinitely in a non-Roots biome).
        /// </summary>
        private const float ScanIntervalSeconds = 0.5f;

        private static float _nextScanTime;

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

        /// <summary>
        /// Whether the last-processed Roots Segment is still believed loaded -
        /// tracked separately from <see cref="_processed"/> itself because a
        /// destroyed <see cref="Transform"/> compares equal to Unity's overridden
        /// `null` (so <c>_processed != null</c> alone can't distinguish "never
        /// processed anything" from "processed one, and it's since been torn
        /// down"). Needed so leaving the biome (deactivation *or* destruction, e.g.
        /// returning to the main menu) is detected as a real transition and not
        /// just silently falls through to "nothing to do".
        /// </summary>
        private static bool _levelLoaded;

        internal static void CheckAndRun()
        {
            bool currentlyActive = _processed != null && _processed.gameObject.activeInHierarchy;

            if (_levelLoaded && !currentlyActive)
            {
                // The Roots Segment we processed is gone (deactivated or
                // destroyed) - drop the per-level debug-overlay state with it, or
                // the "removed spore bomb" markers would keep drawing at stale
                // world positions in the main menu / next non-Roots biome.
                _levelLoaded = false;
                _processed = null;
                SporeBombCullPatch.RemovedPositions.Clear();
                SporeBombCullPatch.KeptTriggerColliders.Clear();
                SporeAreas.SporeAreaDisablePatch.ClearLevelState();
                SporeAreas.SporeAreaCullPatch.ClearLevelState();
            }

            if (currentlyActive)
            {
                return;
            }

            if (Time.unscaledTime < _nextScanTime)
            {
                return;
            }

            _nextScanTime = Time.unscaledTime + ScanIntervalSeconds;

            var found = GameObject.Find("Roots Segment");
            if (found == null || !found.activeInHierarchy)
            {
                return;
            }

            _processed = found.transform;
            _levelLoaded = true;
            Plugin.Cfg.CaptureLevelSnapshot();
            Networking.HostAuthority.PublishAll();
            DetonationScreenshakeRegistry.Clear();
            SporeBombCullPatch.Run(found.transform);
            // Order matters: the seeded removal runs first, so the disable switch
            // below never claims an already-removed area into its restore registry
            // (see SporeAreaDisablePatch's remarks).
            SporeAreas.SporeAreaCullPatch.Run(found.transform);
            SporeAreas.SporeAreaDisablePatch.Run(found.transform);
            SporeAreas.SporeAreaTuningPatch.Run(found.transform);

            if (Plugin.Cfg.EnableDebugLogging.Value && Plugin.Cfg.LogSceneScanOnLoad.Value)
            {
                SceneDiagnostics.AutoDump("Roots Segment detected");
            }
        }
    }
}
