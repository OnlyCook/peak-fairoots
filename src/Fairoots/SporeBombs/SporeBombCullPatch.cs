using System;
using System.Collections.Generic;
using System.Linq;
using Fairoots.Core;
using Fairoots.Diagnostics;
using UnityEngine;

namespace Fairoots.SporeBombs
{
    /// <summary>
    /// Phase 4 (ROADMAP.md): the actual spore-bomb removal logic. Scans an
    /// already-placed Roots scene and applies the two-pass decision from
    /// <see cref="SporeBombCull"/>: unconditional foliage removal, then a seeded
    /// cull budgeted against it.
    ///
    /// Triggered by <see cref="RootsLevelWatcher"/> - NOT a Harmony postfix on
    /// <c>PropGrouper.RunAll</c> (an earlier version of this patch used that seam,
    /// like <see cref="SceneDiagnostics"/> still does, but live testing confirmed
    /// it never fires at all: Roots prop placement is baked into the level scene at
    /// author time, not regenerated at runtime, so that hook was dead code).
    ///
    /// Foliage detection (RESEARCH.md Q7's open item, resolved via the F9/F10/ping
    /// diagnostic probes): the scene has no dedicated foliage class or tag, only
    /// scene-authored naming - "Fern" and "BushLeaves" clumps are the two types
    /// confirmed, by standing next to (and pinging) camouflaged spore bombs, to
    /// actually hide one, and both are colliderless (a spore bomb can only land
    /// inside something with no hitbox - anything solid blocks placement).
    ///
    /// Two bugs in the first version of this detection, both found via live-tested
    /// ping probes before this fix:
    ///   1. The name match ran against each renderer's *own* name, but the actual
    ///      mesh renderers under a clump are named "Mesh"/"sourceName_LOD_N" - only
    ///      an *ancestor* is literally named "Fern". Every Fern clump was therefore
    ///      silently excluded; only BushLeaves (whose renderer name really does
    ///      contain the substring) ever contributed. Fixed by walking up the parent
    ///      chain instead of checking the renderer's own name.
    ///   2. Detection used <c>Renderer.bounds.Contains(pos)</c> - but these clumps
    ///      are one combined mesh spanning many scattered fronds, so their AABB is
    ///      huge (~10m per side) relative to the sparse geometry inside it: false
    ///      positives across the whole box, and (per the maintainer's own ping
    ///      tests) still a false *negative* on a spore bomb sitting only 0.49m from
    ///      an actual frond vertex, because that vertex happened to fall outside
    ///      the LOD renderer's own reported bounds. Fixed by testing actual
    ///      mesh-vertex proximity (<see cref="FoliageVertexThreshold"/>) instead of
    ///      the bounding box - confirmed against live ping data: true overlaps
    ///      measured 0.15-0.53m to the nearest vertex, the next-nearest unrelated
    ///      fern was 1.0m+, a clean gap.
    /// </summary>
    internal static class SporeBombCullPatch
    {
        /// <summary>
        /// Case-insensitive substring identifying a foliage clump capable of
        /// camouflaging a spore bomb, matched against a renderer's name or any
        /// ancestor's name. "BushLeaves" (always nested under "Funky Mushrooms" /
        /// "moss patch") was in this list too, and every attempt to make it behave
        /// via geometry - bounding-box containment, nearest-vertex distance, vertex
        /// count, XYZ spread, height-above-ground with a single global threshold -
        /// kept producing false positives on live-tested ground-cover instances,
        /// twice even after tuning the threshold against confirmed data points
        /// (see git history on this file for the full sequence). Across every ping
        /// test run during development, "BushLeaves" was *never once* the source of
        /// a confirmed true positive, while "Fern" was the source of every
        /// confirmed true positive and, on its own with a plain 3D-distance test,
        /// produced zero confirmed false positives. That's a structural difference
        /// in prefab family, not a matter of degree - so rather than continue
        /// chasing a geometric heuristic for a clump type the data says never
        /// actually camouflages anything, "BushLeaves" is dropped from foliage
        /// detection entirely. If real BushLeaves-camouflage cases turn up later,
        /// revisit with per-cluster geometry (these are merged multi-plant meshes;
        /// a single global vertex threshold can't tell one small instance in the
        /// merge from one large instance a few meters away in the same mesh) rather
        /// than another global constant.
        /// </summary>
        private static readonly string[] FoliageNameSubstrings = { "Fern" };

        /// <summary>
        /// Max full 3D distance from a spore-bomb candidate to a foliage vertex for
        /// it to count as camouflage. Live-ping-validated: true overlaps measured
        /// 0.15-0.53m, next-nearest unrelated Fern clump 1.0m+ - a clean gap with a
        /// plain distance test once foliage detection is restricted to Fern (see
        /// <see cref="FoliageNameSubstrings"/>).
        /// </summary>
        private const float FoliageProximityRadius = 0.75f;

        /// <summary>
        /// Position + outcome of every candidate removed this level load - read by
        /// <see cref="RemovedMarkerOverlay"/> to draw the "a spore bomb used to be
        /// here" screen-space debug marker, labelled by outcome so a plant-camouflage
        /// removal is never confused with the unrelated, foliage-independent seeded
        /// balance cull (both draw a marker, but only one has anything to do with
        /// nearby plants - conflating them was the source of several "false
        /// positive" reports that turned out to be the seeded pass working exactly
        /// as designed). Cleared by <see cref="RootsLevelWatcher"/> each time a new
        /// Roots Segment instance is detected.
        /// </summary>
        internal static readonly List<(Vector3 Pos, CullOutcome Outcome)> RemovedPositions =
            new List<(Vector3, CullOutcome)>();

        /// <summary>
        /// The exact vertex set the last cull run tested against, kept (position,
        /// owning renderer) so <see cref="NearestFoliageVertex"/> can explain *why*
        /// a candidate was flagged, not just that it was - a plain distance number
        /// with no owner was useless for diagnosing the false positives found by
        /// live ping testing, since the culprit vertex often belongs to a renderer
        /// whose own transform is many meters away (see class remarks).
        /// </summary>
        private static readonly List<(Vector3 Pos, Transform Owner)> LastFoliageVertices =
            new List<(Vector3, Transform)>();

        internal static void Run(Transform rootsSegment)
        {
            try
            {
                RemovedPositions.Clear();

                var candidates = rootsSegment.GetComponentsInChildren<Transform>(true)
                    .Where(t => ClassifySporeBomb(t.name))
                    .ToList();

                if (candidates.Count == 0)
                {
                    Diag.V("[SporeBombCull] no spore-bomb candidates found under Roots Segment");
                    return;
                }

                CollectFoliageVertices(rootsSegment);
                Diag.V($"[SporeBombCull] {LastFoliageVertices.Count} foliage vertex sample(s) collected for proximity test");

                var positions = new List<GridPos>(candidates.Count);
                var inFoliage = new List<bool>(candidates.Count);
                foreach (var t in candidates)
                {
                    positions.Add(GridPos.Round(t.position.x, t.position.y, t.position.z));
                    inFoliage.Add(IsNearFoliage(t.position));
                }

                var outcomes = SporeBombCull.Decide(
                    positions,
                    inFoliage,
                    Plugin.Cfg.SporeBombCullFraction,
                    Plugin.Cfg.Seed.Value);

                int removed = 0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (outcomes[i] != CullOutcome.Kept)
                    {
                        RemovedPositions.Add((candidates[i].position, outcomes[i]));
                        candidates[i].gameObject.SetActive(false);
                        removed++;
                        string why = string.Empty;
                        if (outcomes[i] == CullOutcome.RemovedFoliage)
                        {
                            var (dist, owner, count, maxHeightAbove) = NearestFoliageVertex(candidates[i].position);
                            why = $", nearestFoliageVertex={dist:0.00}m countWithinRadius={count} " +
                                  $"maxHeightAbove={maxHeightAbove:0.00}m owner=[{owner}]";
                        }
                        Diag.V($"[SporeBombCull]   removed \"{candidates[i].name}\" @ {positions[i]} ({outcomes[i]}{why})");
                    }
                }

                var summary = SporeBombCull.Summarize(outcomes);
                Diag.Info(
                    $"[SporeBombCull] {summary.Total} candidate(s): removed {removed} " +
                    $"(foliage={summary.FoliageRemoved}, seeded={summary.SeededRemoved}), kept {summary.Kept}");
            }
            catch (Exception e)
            {
                Diag.Error($"[SporeBombCull] threw: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// World-space vertices of every readable mesh belonging to a foliage
        /// clump (name match walks the parent chain - see class remarks). Clumps
        /// often ship multiple LOD renderers under the same clump root; not every
        /// LOD is marked readable (the highest-detail "Mesh"/LOD_0 typically
        /// isn't, per live probing), so this just includes whichever ones are -
        /// same underlying geometry, so any one of them is enough for a proximity
        /// test.
        /// </summary>
        private static void CollectFoliageVertices(Transform rootsSegment)
        {
            LastFoliageVertices.Clear();
            foreach (var r in rootsSegment.GetComponentsInChildren<Renderer>(true))
            {
                if (!IsFoliageRenderer(r.transform))
                {
                    continue;
                }

                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null || !mesh.isReadable)
                {
                    continue;
                }

                var m = r.transform.localToWorldMatrix;
                var verts = mesh.vertices;
                for (int i = 0; i < verts.Length; i++)
                {
                    LastFoliageVertices.Add((m.MultiplyPoint3x4(verts[i]), r.transform));
                }
            }
        }

        /// <summary>
        /// Dev-only diagnostic: the nearest vertex (from the last cull run's
        /// foliage set) to an arbitrary world position, plus which renderer it
        /// belongs to, how many foliage vertices sit within
        /// <see cref="FoliageProximityRadius"/>, and the tallest any of those rise
        /// above the position's own Y - used by <see cref="PingRadiusProbePatch"/>
        /// to explain a suspected false positive/negative against the *exact* data
        /// the cull actually used, not a re-scan limited to "nearby" renderers
        /// (which misses the case where a clump's mesh reaches across several
        /// meters from a pivot that isn't nearby at all). The count/height fields
        /// are diagnostic only now - <see cref="IsNearFoliage"/> just tests plain
        /// 3D distance (see <see cref="FoliageNameSubstrings"/> for why that's
        /// sufficient once detection is restricted to Fern).
        /// </summary>
        internal static (float Distance, string Owner, int CountWithinRadius, float MaxHeightAbove) NearestFoliageVertex(Vector3 pos)
        {
            float nearest = float.MaxValue;
            Transform owner = null;
            int countWithinRadius = 0;
            float maxHeightAbove = float.NegativeInfinity;
            float radiusSq = FoliageProximityRadius * FoliageProximityRadius;
            for (int i = 0; i < LastFoliageVertices.Count; i++)
            {
                Vector3 v = LastFoliageVertices[i].Pos;
                float d = Vector3.Distance(v, pos);
                if (d < nearest)
                {
                    nearest = d;
                    owner = LastFoliageVertices[i].Owner;
                }

                if ((v - pos).sqrMagnitude <= radiusSq)
                {
                    countWithinRadius++;
                    float heightAbove = v.y - pos.y;
                    if (heightAbove > maxHeightAbove)
                    {
                        maxHeightAbove = heightAbove;
                    }
                }
            }

            if (owner == null)
            {
                return (float.MaxValue, "(no foliage vertices collected)", 0, 0f);
            }

            var chain = new List<string>();
            for (var t = owner; t != null; t = t.parent)
            {
                chain.Add(t.name);
                if (t.name == "Roots Segment")
                {
                    break;
                }
            }

            return (nearest, string.Join(" < ", chain), countWithinRadius, countWithinRadius > 0 ? maxHeightAbove : 0f);
        }

        private static bool IsFoliageRenderer(Transform t)
        {
            for (var cur = t; cur != null; cur = cur.parent)
            {
                for (int i = 0; i < FoliageNameSubstrings.Length; i++)
                {
                    if (cur.name.IndexOf(FoliageNameSubstrings[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }

                if (cur.name == "Roots Segment")
                {
                    break;
                }
            }

            return false;
        }

        private static bool IsNearFoliage(Vector3 pos)
        {
            float radiusSq = FoliageProximityRadius * FoliageProximityRadius;
            for (int i = 0; i < LastFoliageVertices.Count; i++)
            {
                if ((LastFoliageVertices[i].Pos - pos).sqrMagnitude <= radiusSq)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Match the confirmed hazard name substrings (RESEARCH.md Q7 / roots-runtime-findings).</summary>
        private static bool ClassifySporeBomb(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            return name.IndexOf("SporeFungus", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("SporeMushroom", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
