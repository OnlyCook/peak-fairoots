using System;
using System.Collections.Generic;
using System.Linq;
using Fairoots.Core;
using Fairoots.Diagnostics;
using UnityEngine;

namespace Fairoots.SporeAreas
{
    /// <summary>
    /// Phase 6 (ROADMAP.md), second mechanic: "make spore areas less common."
    /// Removes a configured fraction of the level's persistent spore areas,
    /// deciding <em>which</em> ones via <see cref="SporeAreaCull"/> - seeded, and
    /// cluster-first (the emitter closest to another emitter goes first), so
    /// overlapping clouds get thinned before isolated landmarks.
    ///
    /// Level-load-only by design, the same as the spore-bomb cull fraction: a spore
    /// area that's already gone can't come back mid-level, so there's nothing for a
    /// live config change to mean here (and nothing wired to
    /// <c>SettingChanged</c> for it). Removal is
    /// <c>SetActive(false)</c> on the whole area object
    /// (<see cref="SporeAreaScan.ResolveAreaRoot"/>) rather than destroying it,
    /// matching how <see cref="SporeBombs.SporeBombCullPatch"/> removes spore bombs
    /// and keeping the operation reversible in principle.
    ///
    /// Determinism (CLAUDE.md's non-negotiable rule): this runs as a postfix-shaped
    /// pass over an <em>already-placed</em> scene, keys every decision off the
    /// rounded world position of each emitter plus the host's configured seed, and
    /// touches no RNG stream of any kind - so every client independently reaches an
    /// identical result with no networking beyond the already-host-authoritative
    /// seed and fraction.
    /// </summary>
    internal static class SporeAreaCullPatch
    {
        /// <summary>
        /// Position of every spore area removed this level load, for the debug
        /// overlay/log. Cleared by <see cref="ClearLevelState"/> when the level is
        /// torn down, so stale world positions don't linger into the next biome.
        /// </summary>
        internal static readonly List<Vector3> RemovedPositions = new List<Vector3>();

        internal static void ClearLevelState() => RemovedPositions.Clear();

        internal static void Run(Transform rootsSegment)
        {
            try
            {
                RemovedPositions.Clear();

                var areas = SporeAreaScan.FilterSporeAreas(
                    rootsSegment.GetComponentsInChildren<StatusEmitter>(true));
                if (areas.Count == 0)
                {
                    Diag.V("[SporeAreaCull] no spore areas found under Roots Segment");
                    return;
                }

                double fraction = Plugin.Cfg.EffectiveSporeAreaRemovalFraction;
                var positions = new List<GridPos>(areas.Count);
                foreach (var e in areas)
                {
                    Vector3 p = e.transform.position;
                    positions.Add(GridPos.Round(p.x, p.y, p.z));
                }

                bool[] remove = SporeAreaCull.Decide(positions, fraction, Plugin.Cfg.EffectiveSeed);

                int removed = 0;
                var removedSpacing = new List<float>();
                var keptSpacing = new List<float>();
                for (int i = 0; i < areas.Count; i++)
                {
                    float spacing = GameUnits.ToMeters(NearestOtherDistance(positions, i));
                    if (!remove[i])
                    {
                        keptSpacing.Add(spacing);
                        continue;
                    }

                    GameObject root = SporeAreaScan.ResolveAreaRoot(areas[i]);
                    RemovedPositions.Add(areas[i].transform.position);
                    root.SetActive(false);
                    removed++;
                    removedSpacing.Add(spacing);
                    Diag.V(
                        $"[SporeAreaCull]   removed \"{SporeAreaScan.DescribePath(root.transform)}\" @ {positions[i]} " +
                        $"(nearest other spore area {spacing:0.0}m)");
                }

                Diag.Info(
                    $"[SporeAreaCull] {areas.Count} spore area(s): removed {removed}, kept {areas.Count - removed} " +
                    $"(fraction={fraction:0.###}, seed={Plugin.Cfg.EffectiveSeed})");

                // Whether the cluster-first rule actually bit, in one comparable
                // number per group. The per-removal lines above are logged in scene
                // order, so they can't show it on their own; a removed group whose
                // median spacing isn't clearly below the kept group's means the
                // ranking isn't doing its job (or the fraction is high enough that
                // there simply aren't enough crowded areas left to take).
                if (removed > 0 && removed < areas.Count)
                {
                    Diag.Info(
                        $"[SporeAreaCull] nearest-neighbour spacing - removed: median {Median(removedSpacing):0.0}m " +
                        $"(min {removedSpacing.Min():0.0}m), kept: median {Median(keptSpacing):0.0}m " +
                        $"(min {keptSpacing.Min():0.0}m). Removed median should be the lower of the two.");
                }
            }
            catch (Exception e)
            {
                Diag.Error($"[SporeAreaCull] threw: {e.GetType().Name}: {e.Message}");
            }
        }

        private static float Median(List<float> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) * 0.5f;
        }

        /// <summary>
        /// Distance from one spore area to its nearest neighbor, in world units -
        /// verbose-log only, so a removal can be read against the cluster-first rule
        /// it's supposed to follow ("did it really take the crowded ones first?")
        /// instead of taken on faith.
        /// </summary>
        private static float NearestOtherDistance(IReadOnlyList<GridPos> positions, int index)
        {
            long best = long.MaxValue;
            GridPos pi = positions[index];
            for (int j = 0; j < positions.Count; j++)
            {
                if (j == index) continue;

                GridPos pj = positions[j];
                long dx = pi.X - pj.X;
                long dy = pi.Y - pj.Y;
                long dz = pi.Z - pj.Z;
                long distSq = dx * dx + dy * dy + dz * dz;
                if (distSq < best) best = distSq;
            }

            return best == long.MaxValue ? 0f : Mathf.Sqrt(best);
        }
    }
}
