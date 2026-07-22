using System;
using System.Collections.Generic;

namespace Fairoots.Core
{
    /// <summary>What happened to a single spore-bomb candidate.</summary>
    public enum CullOutcome
    {
        /// <summary>Survives - not in foliage and not picked by the seeded cull.</summary>
        Kept = 0,

        /// <summary>Removed unconditionally because it was placed inside bush/grass geometry (pass 1).</summary>
        RemovedFoliage = 1,

        /// <summary>Removed by the seeded, budgeted cull pass (pass 2).</summary>
        RemovedSeededCull = 2,
    }

    /// <summary>Aggregate counts from a cull decision, handy for logging and test assertions.</summary>
    public readonly struct CullSummary
    {
        public readonly int Total;
        public readonly int FoliageRemoved;
        public readonly int SeededRemoved;
        public readonly int Kept;

        public CullSummary(int total, int foliageRemoved, int seededRemoved, int kept)
        {
            Total = total;
            FoliageRemoved = foliageRemoved;
            SeededRemoved = seededRemoved;
            Kept = kept;
        }

        public int TotalRemoved => FoliageRemoved + SeededRemoved;
    }

    /// <summary>
    /// The spore-bomb removal decision, as pure arithmetic + seeded ranked
    /// selection (RESEARCH.md Q7, ROADMAP.md "Spore bomb removal is two passes").
    /// Zero Unity dependency - the game-facing patch scans the placed scene, hands
    /// this a parallel list of rounded positions and foliage flags, and maps the
    /// returned per-index outcomes back onto the actual GameObjects.
    ///
    /// Two passes, the second budgeted against the first:
    ///   1. Foliage removal - unconditional, seed-independent. Every candidate
    ///      sitting inside bush/grass is removed. This is a geometric fact, not a
    ///      roll, so it needs no RNG.
    ///   2. Seeded cull - removes only enough *additional* candidates to reach the
    ///      preset's total removal target. If pass 1 already met/exceeded the
    ///      target, pass 2 removes nothing. It never removes more than the target
    ///      just because foliage removal overshot. Candidates are ranked once by
    ///      nearest-neighbor distance (closest-clustered first) and taken greedily,
    ///      sparing each removed candidate's nearest neighbor for the rest of the
    ///      pass so a tight pair loses one member rather than both at once. This is
    ///      a single-pass approximation (not a full recompute after every removal)
    ///      - cheap, and correct for the common case of distinct clusters, at the
    ///      cost of being merely a good heuristic for bigger mutually-close
    ///      clumps. The deterministic per-seed hash breaks ties.
    ///
    /// "Removal target" follows CLAUDE.md's exact wording - at least
    /// <c>floor(total * (1 - cullFraction))</c> candidates always survive (half,
    /// rounded down, for the 0.5 preset), so removal = total - that survivor floor.
    /// </summary>
    public static class SporeBombCull
    {
        public const string MechanicTag = "spore-bomb-cull";

        /// <summary>
        /// Decide the fate of every candidate. <paramref name="positions"/> and
        /// <paramref name="inFoliage"/> are parallel arrays (same length, same
        /// order); the returned array is indexed identically so the caller can zip
        /// outcomes back onto its objects.
        /// </summary>
        /// <param name="cullFraction">
        /// Target fraction of the total to remove overall (foliage + seeded
        /// combined), in [0, 1]. e.g. 0.5 = "cut spore bombs in half".
        /// </param>
        public static CullOutcome[] Decide(
            IReadOnlyList<GridPos> positions,
            IReadOnlyList<bool> inFoliage,
            double cullFraction,
            int userSeed,
            string mechanicTag = MechanicTag)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (inFoliage == null) throw new ArgumentNullException(nameof(inFoliage));
            if (positions.Count != inFoliage.Count)
            {
                throw new ArgumentException(
                    "positions and inFoliage must be the same length (parallel arrays).");
            }

            int total = positions.Count;
            var outcomes = new CullOutcome[total];

            // Pass 1: unconditional foliage removal. Collect the survivors' indices.
            int foliageRemoved = 0;
            var survivors = new List<int>(total);
            for (int i = 0; i < total; i++)
            {
                if (inFoliage[i])
                {
                    outcomes[i] = CullOutcome.RemovedFoliage;
                    foliageRemoved++;
                }
                else
                {
                    outcomes[i] = CullOutcome.Kept;
                    survivors.Add(i);
                }
            }

            // Removal target: keep at least floor(total * (1 - f)) survivors.
            double clampedFraction = cullFraction < 0.0 ? 0.0 : (cullFraction > 1.0 ? 1.0 : cullFraction);
            // +1e-9 so an exact fraction (0.5 * 100 = 50.0) floors predictably rather
            // than tripping on a representation like 49.9999999.
            int survivorTarget = (int)Math.Floor(total * (1.0 - clampedFraction) + 1e-9);
            int removalTarget = total - survivorTarget;

            // Pass 2: budgeted seeded cull over the non-foliage survivors only.
            int remainingBudget = removalTarget - foliageRemoved;
            if (remainingBudget < 0) remainingBudget = 0;
            if (remainingBudget > survivors.Count) remainingBudget = survivors.Count;

            if (remainingBudget > 0)
            {
                // Compute each survivor's nearest neighbor ONCE (O(m^2) for m
                // survivors, not O(budget * m^2) - the earlier per-removal
                // recompute was correct but re-did the full all-pairs scan after
                // every single removal, which is wasteful once budgets/counts
                // grow). Ties are broken deterministically (seed hash, then
                // position) so the result never depends on scene-enumeration
                // order.
                //
                // A pair's two members are each other's nearest neighbor, so a
                // naive one-shot rank-and-take-the-top-N would remove both at
                // once instead of thinning one and leaving the other. To avoid
                // that without paying for a full recompute per removal, walk the
                // ranked order greedily and "protect" a candidate's nearest
                // neighbor for one round as soon as the candidate itself is
                // removed - a cheap approximation of "the pair is no longer
                // tight" without actually re-measuring it. This can under-protect
                // in bigger clusters (3+ mutually close candidates), which is the
                // accepted quality/speed tradeoff versus the exact recompute-every-
                // step version. If protection leaves the budget unmet (nothing
                // left to spare), a second pass fills the remainder in the same
                // ranked order, ignoring protection.
                int survivorCount = survivors.Count;
                var nearestDistSq = new long[survivorCount];
                var nearestPartner = new int[survivorCount];
                for (int i = 0; i < survivorCount; i++)
                {
                    GridPos pi = positions[survivors[i]];
                    long best = long.MaxValue;
                    int bestPartner = -1;
                    uint bestKey = 0;
                    for (int j = 0; j < survivorCount; j++)
                    {
                        if (i == j) continue;
                        GridPos pj = positions[survivors[j]];
                        long dx = pi.X - pj.X;
                        long dy = pi.Y - pj.Y;
                        long dz = pi.Z - pj.Z;
                        long distSq = dx * dx + dy * dy + dz * dz;

                        if (distSq > best) continue;

                        if (distSq < best)
                        {
                            best = distSq;
                            bestPartner = j;
                            bestKey = DeterministicHash.RankKey(userSeed, mechanicTag, pj);
                            continue;
                        }

                        // distSq == best: tie-break the nearest-partner choice
                        // deterministically too.
                        uint key = DeterministicHash.RankKey(userSeed, mechanicTag, pj);
                        if (key < bestKey || (key == bestKey && ComparePos(pj, positions[survivors[bestPartner]]) < 0))
                        {
                            bestPartner = j;
                            bestKey = key;
                        }
                    }

                    nearestDistSq[i] = best;
                    nearestPartner[i] = bestPartner;
                }

                var order = new int[survivorCount];
                for (int i = 0; i < survivorCount; i++) order[i] = i;

                Array.Sort(order, (a, b) =>
                {
                    int cmp = nearestDistSq[a].CompareTo(nearestDistSq[b]);
                    if (cmp != 0) return cmp;

                    GridPos pa = positions[survivors[a]];
                    GridPos pb = positions[survivors[b]];
                    uint ka = DeterministicHash.RankKey(userSeed, mechanicTag, pa);
                    uint kb = DeterministicHash.RankKey(userSeed, mechanicTag, pb);
                    if (ka != kb) return ka < kb ? -1 : 1;
                    return ComparePos(pa, pb);
                });

                var removed = new bool[survivorCount];
                var spared = new bool[survivorCount];
                int removedCount = 0;

                // Pass 1: take the closest-ranked candidate not currently spared,
                // sparing its nearest neighbor for the rest of this pass.
                for (int oi = 0; oi < survivorCount && removedCount < remainingBudget; oi++)
                {
                    int i = order[oi];
                    if (spared[i]) continue;

                    removed[i] = true;
                    removedCount++;

                    int partner = nearestPartner[i];
                    if (partner >= 0 && !removed[partner]) spared[partner] = true;
                }

                // Pass 2: budget still unmet (everything left was spared) - fill
                // the remainder in the same ranked order, protection dropped.
                if (removedCount < remainingBudget)
                {
                    for (int oi = 0; oi < survivorCount && removedCount < remainingBudget; oi++)
                    {
                        int i = order[oi];
                        if (removed[i]) continue;

                        removed[i] = true;
                        removedCount++;
                    }
                }

                for (int i = 0; i < survivorCount; i++)
                {
                    if (removed[i]) outcomes[survivors[i]] = CullOutcome.RemovedSeededCull;
                }
            }

            return outcomes;
        }

        /// <summary>Tally a set of outcomes into a <see cref="CullSummary"/>.</summary>
        public static CullSummary Summarize(IReadOnlyList<CullOutcome> outcomes)
        {
            if (outcomes == null) throw new ArgumentNullException(nameof(outcomes));

            int foliage = 0, seeded = 0, kept = 0;
            for (int i = 0; i < outcomes.Count; i++)
            {
                switch (outcomes[i])
                {
                    case CullOutcome.RemovedFoliage: foliage++; break;
                    case CullOutcome.RemovedSeededCull: seeded++; break;
                    default: kept++; break;
                }
            }

            return new CullSummary(outcomes.Count, foliage, seeded, kept);
        }

        private static int ComparePos(GridPos a, GridPos b)
        {
            if (a.X != b.X) return a.X < b.X ? -1 : 1;
            if (a.Y != b.Y) return a.Y < b.Y ? -1 : 1;
            if (a.Z != b.Z) return a.Z < b.Z ? -1 : 1;
            return 0;
        }
    }
}
