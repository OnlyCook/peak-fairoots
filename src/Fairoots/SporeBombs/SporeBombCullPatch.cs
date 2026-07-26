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
        /// The current (post-shrink) trigger <see cref="Collider"/> of every spore
        /// bomb kept this level load, one per candidate - read live by
        /// <see cref="TriggerRadiusOverlay"/> so its wireframe always matches
        /// whatever the collider's actual fields say right now, not a cached
        /// snapshot. Cleared and repopulated each <see cref="Run"/>.
        /// </summary>
        internal static readonly List<Collider> KeptTriggerColliders = new List<Collider>();

        /// <summary>
        /// The true, never-shrunk trigger size of every <see cref="SphereCollider"/>/
        /// <see cref="BoxCollider"/> this session has ever touched, keyed by
        /// <see cref="Object.GetInstanceID"/> and captured the *first* time each
        /// collider is ever seen. Every reshrink (a normal level load, or a forced
        /// <see cref="ReapplyTriggerRadiusToAll"/>) scales from this cached baseline,
        /// never from the collider's current (possibly already-shrunk) value -
        /// without this, repeatedly reprocessing the same still-alive GameObjects
        /// (which live testing showed does happen: leaving and re-entering a run
        /// doesn't always destroy the previous level's objects) would compound the
        /// shrink further each time, and toggling
        /// <see cref="PluginConfig.KeepVanillaTriggerRadius"/> back on would just
        /// preserve whatever the *last* multiplier left behind instead of actually
        /// restoring vanilla size.
        /// </summary>
        private static readonly Dictionary<int, float> VanillaSphereRadii = new Dictionary<int, float>();

        private static readonly Dictionary<int, Vector3> VanillaBoxSizes = new Dictionary<int, Vector3>();

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
                KeptTriggerColliders.Clear();

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
                    Plugin.Cfg.EffectiveSporeBombCullFraction,
                    Plugin.Cfg.EffectiveSeed);

                int removed = 0;
                int shrunk = 0;
                double triggerRadiusMultiplier = ResolveTriggerRadiusMultiplier();
                bool recolor = Plugin.Cfg.RecolorSporeBombs.Value;
                Core.Rgb sporeColor = SporeBombRecolorPatch.ResolveSporeColor();
                if (Plugin.Cfg.KeepVanillaTriggerRadius.Value)
                {
                    Diag.Info("[SporeBombCull] keep-vanilla-trigger-radius is ON - trigger hitboxes left at vanilla size (for before/after comparison screenshots)");
                }

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
                            // Both are raw world-space distances; the foliage-proximity
                            // thresholds themselves are deliberately kept in world units
                            // (they were tuned against mesh geometry, not meters), so only
                            // the display is converted - see Core/WorldUnits.cs.
                            why = $", nearestFoliageVertex={GameUnits.ToMeters(dist):0.00}m countWithinRadius={count} " +
                                  $"maxHeightAbove={GameUnits.ToMeters(maxHeightAbove):0.00}m owner=[{owner}]";
                        }
                        Diag.V($"[SporeBombCull]   removed \"{candidates[i].name}\" @ {positions[i]} ({outcomes[i]}{why})");
                    }
                    else
                    {
                        if (ShrinkTriggerRadius(candidates[i], triggerRadiusMultiplier, out Collider triggerCollider))
                        {
                            shrunk++;
                            KeptTriggerColliders.Add(triggerCollider);
                        }

                        // Only the "on" direction here - restoring vanilla colors
                        // is ReapplyToAll's job when the setting is toggled off,
                        // so a level loaded with the setting already off doesn't
                        // pay for 400+ pointless property-block writes.
                        if (recolor)
                        {
                            SporeBombRecolorPatch.Apply(candidates[i], sporeColor, enabled: true);
                        }
                    }
                }

                var summary = SporeBombCull.Summarize(outcomes);
                Diag.Info(
                    $"[SporeBombCull] {summary.Total} candidate(s): removed {removed} " +
                    $"(foliage={summary.FoliageRemoved}, seeded={summary.SeededRemoved}), kept {summary.Kept}, " +
                    $"trigger-radius shrunk on {shrunk} (multiplier={triggerRadiusMultiplier:0.##}), " +
                    $"recolor={(recolor ? $"ON target={sporeColor}" : "OFF")}");
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

        /// <summary>
        /// The effective trigger-radius multiplier right now: vanilla (1.0) if the
        /// screenshot-comparison debug toggle is on, otherwise the resolved preset/
        /// override value (frozen at the last level load if
        /// <see cref="PluginConfig.ApplyChangesLive"/> is off). Shared by
        /// <see cref="Run"/> and <see cref="ReapplyTriggerRadiusToAll"/> so both
        /// apply the exact same number.
        /// </summary>
        private static double ResolveTriggerRadiusMultiplier() =>
            Plugin.Cfg.KeepVanillaTriggerRadius.Value ? 1.0 : Plugin.Cfg.EffectiveSporeBombTriggerRadiusMultiplier;

        /// <summary>
        /// Forces every currently-active spore bomb <em>anywhere in the loaded
        /// scene</em> (not just the last-processed Roots Segment) to immediately
        /// re-resolve its trigger-hitbox size against the current config - a full,
        /// deliberately heavy scene-wide re-scan, wired up to
        /// <c>SettingChanged</c> on <see cref="PluginConfig.KeepVanillaTriggerRadius"/>/
        /// <see cref="PluginConfig.SporeBombTriggerRadiusMultiplierOverride"/>/
        /// <see cref="PluginConfig.Preset"/> (see <c>Plugin.Awake</c>) so flipping
        /// the debug toggle refreshes every spore bomb's collider *and* the
        /// <see cref="TriggerRadiusOverlay"/> wireframe right away, without waiting
        /// for (or requiring) a level reload. Safe to call as often as needed - it's
        /// diagnostic tooling, not something that runs during normal play.
        /// </summary>
        internal static void ReapplyTriggerRadiusToAll()
        {
            if (Plugin.Cfg == null)
            {
                return;
            }

            try
            {
                double multiplier = ResolveTriggerRadiusMultiplier();
                KeptTriggerColliders.Clear();

                int found = 0, resized = 0;
                foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
                {
                    if (!t.gameObject.activeInHierarchy || !ClassifySporeBomb(t.name))
                    {
                        continue;
                    }

                    found++;
                    if (ShrinkTriggerRadius(t, multiplier, out Collider triggerCollider))
                    {
                        resized++;
                        KeptTriggerColliders.Add(triggerCollider);
                    }
                }

                Diag.Info(
                    $"[SporeBombCull] full trigger-radius refresh: {found} active spore bomb(s) found scene-wide, " +
                    $"{resized} resized (multiplier={multiplier:0.##})");
            }
            catch (Exception e)
            {
                Diag.Error($"[SporeBombCull] ReapplyTriggerRadiusToAll threw: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// Shrinks every trigger <see cref="Collider"/> directly on a kept spore
        /// bomb's trigger object by the configured multiplier (ROADMAP.md "Spore
        /// bomb trigger radius" row), always scaling from each collider's cached
        /// <see cref="VanillaSphereRadii"/>/<see cref="VanillaBoxSizes"/> baseline
        /// (captured the first time it's ever seen) rather than its current value -
        /// see those fields' remarks for why that matters. Per the confirmed
        /// runtime architecture (roots-runtime-findings memory), the named object
        /// itself carries the oversized trigger hitbox(es) - always
        /// <see cref="SphereCollider"/>s in every case seen so far, but
        /// <see cref="BoxCollider"/> is handled too in case some variant/future
        /// biome uses one. The explosion components (AOE etc.) don't exist until
        /// <c>SpawnGameObject</c> fires on trigger, so this is the only place the
        /// trigger *size* can be tuned; see <see cref="SporeBombExplosionPatch"/>
        /// for the knockback/shake/VFX tuning applied at spawn time instead.
        /// </summary>
        private static bool ShrinkTriggerRadius(Transform candidate, double multiplier, out Collider largestCollider)
        {
            largestCollider = null;
            float largestExtent = 0f;

            foreach (var col in candidate.GetComponents<Collider>())
            {
                float extent;
                switch (col)
                {
                    case SphereCollider sphere:
                    {
                        int id = sphere.GetInstanceID();
                        if (!VanillaSphereRadii.TryGetValue(id, out float vanillaRadius))
                        {
                            vanillaRadius = sphere.radius;
                            VanillaSphereRadii[id] = vanillaRadius;
                        }

                        sphere.radius = SporeBombExplosionTuning.ScaleTriggerRadius(vanillaRadius, multiplier);
                        extent = sphere.radius;
                        break;
                    }
                    case BoxCollider box:
                    {
                        int id = box.GetInstanceID();
                        if (!VanillaBoxSizes.TryGetValue(id, out Vector3 vanillaSize))
                        {
                            vanillaSize = box.size;
                            VanillaBoxSizes[id] = vanillaSize;
                        }

                        box.size = vanillaSize * (float)multiplier;
                        extent = box.size.magnitude;
                        break;
                    }
                    default:
                        continue;
                }

                if (extent > largestExtent)
                {
                    largestExtent = extent;
                    largestCollider = col;
                }
            }

            return largestCollider != null;
        }

        /// <summary>
        /// Match the confirmed hazard name substrings (RESEARCH.md Q7 /
        /// roots-runtime-findings). Internal (not private) so
        /// <see cref="SporeBombExplosionPatch"/> can reuse the same identity check
        /// against the triggering object at spawn time.
        /// </summary>
        internal static bool ClassifySporeBomb(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            return name.IndexOf("SporeFungus", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("SporeMushroom", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// True for the "Explosive Spore Bomb" variant (<c>SporeMushroomExplo</c>)
        /// specifically - confirmed by the maintainer to actually be round (its
        /// vanilla sphere trigger already matches its visual shape reasonably
        /// well), unlike the plain "Spore Bomb" (<c>SporeFungus</c>) and "Poison
        /// Spore Bomb" (<c>SporeMushroom</c>, non-Explo) variants, which are short/
        /// wide mushroom clusters with a trigger sphere that reaches ridiculously
        /// far above the actual mesh - used by
        /// <see cref="SporeBombHeightGatePatch"/> to scope the trigger
        /// height-cutoff fix to exactly the non-round variants it's meant for.
        /// </summary>
        internal static bool IsExplosiveVariant(string name) =>
            !string.IsNullOrEmpty(name) && name.IndexOf("SporeMushroomExplo", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
