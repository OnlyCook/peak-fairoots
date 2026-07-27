using System;
using System.Collections.Generic;
using Fairoots.Diagnostics;
using UnityEngine;

namespace Fairoots.SporeAreas
{
    /// <summary>
    /// Phase 6 (ROADMAP.md), first mechanic: the <c>Spore-Areas/disable-spore-areas</c>
    /// master switch. Removes the Roots biome's persistent spore areas (the game's
    /// "Mushroom Spore Clouds") outright - status ticks, screen filter, the emitter
    /// mushroom in the middle and the cloud VFX all go with them.
    ///
    /// Identity and "which GameObject <em>is</em> the spore area" both live in
    /// <see cref="SporeAreaScan"/>, shared with every other mechanic in this folder
    /// (runtime-confirmed: in Roots every spore area is a
    /// <c>WindAffectedStatusEmitter</c> with <c>radius=16</c>, <c>innerFade=8</c>,
    /// <c>amount=0.025</c>, all named "Mushroom tree Spore Cloud").
    ///
    /// Not a Harmony patch despite the folder's naming convention, for the same
    /// reason <c>SporeBombRecolorPatch</c> isn't: the emitters are baked into the
    /// Roots scene at author time, so there's no runtime placement call to hook.
    /// Driven once per level from <see cref="RootsLevelWatcher"/>, plus a
    /// scene-wide <see cref="ReapplyToAll"/> whenever the setting changes.
    ///
    /// <b>Deliberately excludes a spore bomb's own temporary spore area</b>
    /// (<see cref="SporeAreaScan.IsSporeBombSpawned"/>) - that hazard is tuned by
    /// the <c>Spore-Bombs</c> settings instead.
    ///
    /// Runs <em>after</em> <see cref="SporeAreaCullPatch"/> each level load, which
    /// is what makes the two compose correctly: an area the seeded removal pass
    /// already deactivated is skipped here (never claimed into
    /// <see cref="Deactivated"/>), so turning this switch off restores only the
    /// areas the level was actually supposed to have.
    /// </summary>
    internal static class SporeAreaDisablePatch
    {
        /// <summary>
        /// Every GameObject this session deactivated, so turning the setting back
        /// off restores exactly what Fairoots hid and nothing else - re-activating
        /// whatever happens to be inactive around a spore emitter would also undo
        /// the game's own deactivations (e.g. <c>DisableBasedOnRunSettings</c>) and
        /// the seeded removal pass's. Keyed by
        /// <see cref="UnityEngine.Object.GetInstanceID"/>; entries whose object has
        /// since been destroyed are skipped and dropped on the next pass.
        /// </summary>
        private static readonly Dictionary<int, GameObject> Deactivated = new Dictionary<int, GameObject>();

        /// <summary>
        /// Applies the current setting to every spore area under a freshly-loaded
        /// Roots Segment. Called once per level load; a no-op (beyond a log line)
        /// while the setting is off and nothing has been hidden yet.
        /// </summary>
        internal static void Run(Transform rootsSegment)
        {
            try
            {
                Apply(rootsSegment.GetComponentsInChildren<StatusEmitter>(true), "level load");
            }
            catch (Exception e)
            {
                Diag.Error($"[SporeAreas] Run threw: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// Re-resolves every spore area <em>anywhere in the loaded scene</em>
        /// against the current setting, in both directions (hide when it's turned
        /// on, restore when it's turned off) - wired to the setting's own
        /// <c>SettingChanged</c> and to <c>HostAuthoritySync</c>'s room-property
        /// update, so a client whose level load raced ahead of the host's first
        /// publish still ends up matching the host. Scene-wide rather than
        /// Roots-Segment-scoped because it also has to work the moment the player
        /// flips the toggle, from wherever they are.
        /// </summary>
        internal static void ReapplyToAll()
        {
            if (Plugin.Cfg == null)
            {
                return;
            }

            try
            {
                // includeInactive: true is load-bearing - the emitters this pass
                // has to be able to find again are precisely the ones sitting on
                // GameObjects it deactivated itself.
                Apply(UnityEngine.Object.FindObjectsOfType<StatusEmitter>(true), "config change");
            }
            catch (Exception e)
            {
                Diag.Error($"[SporeAreas] ReapplyToAll threw: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>Drops the restore registry - called when the Roots level is torn down.</summary>
        internal static void ClearLevelState() => Deactivated.Clear();

        private static void Apply(IReadOnlyList<StatusEmitter> emitters, string reason)
        {
            bool disable = Plugin.Cfg.EffectiveDisableSporeAreas;

            var areas = SporeAreaScan.FilterSporeAreas(emitters);
            int hidden = 0, restored = 0;
            foreach (var emitter in areas)
            {
                GameObject root = SporeAreaScan.ResolveAreaRoot(emitter);
                int id = root.GetInstanceID();

                if (disable)
                {
                    if (!root.activeSelf)
                    {
                        // Either already ours, or something else deactivated it -
                        // the seeded removal pass, or the game for its own reasons.
                        // Leave it alone and don't claim it, or turning the setting
                        // off would activate something that was meant to stay gone.
                        continue;
                    }

                    root.SetActive(false);
                    Deactivated[id] = root;
                    hidden++;
                    Diag.V($"[SporeAreas]   disabled \"{SporeAreaScan.DescribePath(root.transform)}\" (radius={emitter.radius}, amount={emitter.amount})");
                }
                else if (Deactivated.TryGetValue(id, out GameObject ours))
                {
                    Deactivated.Remove(id);
                    if (ours != null)
                    {
                        ours.SetActive(true);
                        restored++;
                    }
                }
            }

            Diag.Info(
                $"[SporeAreas] {reason}: disable-spore-areas={(disable ? "ON" : "off")}, " +
                $"{areas.Count} spore area(s) found, {hidden} newly hidden, {restored} restored");
        }
    }
}
