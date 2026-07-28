using System;
using System.Collections.Generic;
using Fairoots.Core;
using Fairoots.Diagnostics;
using UnityEngine;

namespace Fairoots.SporeAreas
{
    /// <summary>
    /// Thins the persistent spore areas' cloud VFX (<c>General/spore-area-cloud-opacity</c>)
    /// so the game's own Spores screen overlay is readable through it - see
    /// <see cref="SporeCloudOpacity"/> for why that matters and
    /// <see cref="ParticleOpacity"/> for how the alpha is actually written.
    ///
    /// Not a Harmony patch despite this folder's naming convention, for the same
    /// reason as <see cref="SporeAreaDisablePatch"/> and
    /// <c>SporeBombs/SporeBombRecolorPatch</c>: the emitters are baked into the
    /// Roots scene at author time, so there's no runtime placement call to hook.
    /// Driven once per level from <see cref="RootsLevelWatcher"/>, plus a scene-wide
    /// <see cref="ReapplyToAll"/> on the setting changing.
    ///
    /// <b>Deliberately separate from <see cref="SporeAreaTuningPatch"/></b> even
    /// though both end up walking the same particle systems. That one applies
    /// host-authoritative gameplay values gated on <c>apply-changes-live</c>; this
    /// one is per-client cosmetics that must apply immediately in both directions
    /// (the same treatment <c>recolor-spore-bombs</c> gets), and folding them
    /// together would mean one of the two behaving wrongly. The two write different
    /// properties - transform scale there, particle start color here - so they
    /// compose rather than fight.
    /// </summary>
    internal static class SporeCloudOpacityPatch
    {
        internal static void Run(Transform rootsSegment)
        {
            try
            {
                Apply(SporeAreaScan.FilterSporeAreas(rootsSegment.GetComponentsInChildren<StatusEmitter>(true)), "level load");
            }
            catch (Exception e)
            {
                Diag.Error($"[SporeCloudOpacity] Run threw: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// Re-resolves every spore cloud's translucency scene-wide - the
        /// immediate-effect path for the setting being changed in-game, in both
        /// directions (turning it back to 1.0 restores the authored VFX right away
        /// rather than waiting for a level reload).
        /// </summary>
        internal static void ReapplyToAll()
        {
            if (Plugin.Cfg == null)
            {
                return;
            }

            try
            {
                Apply(SporeAreaScan.FilterSporeAreas(UnityEngine.Object.FindObjectsOfType<StatusEmitter>(true)), "config change");
            }
            catch (Exception e)
            {
                Diag.Error($"[SporeCloudOpacity] ReapplyToAll threw: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void Apply(IReadOnlyList<StatusEmitter> areas, string reason)
        {
            if (areas.Count == 0)
            {
                return;
            }

            double opacity = Plugin.Cfg.SporeAreaCloudOpacity.Value;

            int systems = 0, areasWithoutVfx = 0;
            for (int i = 0; i < areas.Count; i++)
            {
                GameObject root = SporeAreaScan.ResolveAreaRoot(areas[i]);
                int scaled = ParticleOpacity.Apply(root, opacity);
                systems += scaled;
                if (scaled == 0)
                {
                    areasWithoutVfx++;
                }
            }

            Diag.Info(
                $"[SporeCloudOpacity] {reason}: {areas.Count} spore area(s), {systems} particle system(s) " +
                $"at opacity x{opacity:0.###}{(SporeCloudOpacity.IsVanilla(opacity) ? " (vanilla - authored values restored)" : string.Empty)}" +
                (areasWithoutVfx > 0 ? $", {areasWithoutVfx} area(s) had no VFX to thin" : string.Empty));
        }
    }
}
