using System;
using System.Collections.Generic;

namespace Fairoots.Core
{
    /// <summary>
    /// "Given these world positions and a budget of N to remove, which N?" —
    /// the seeded, deterministic, cluster-first selection shared by every
    /// Fairoots mechanic that thins out a set of placed hazards. Currently
    /// <see cref="SporeBombCull"/>'s budgeted second pass and
    /// <see cref="SporeAreaCull"/>.
    ///
    /// The rule: candidates are ranked by how close their <em>nearest neighbor</em>
    /// is, closest-clustered first, so thinning always attacks the densest
    /// clumps (two hazards on top of each other) before touching an isolated
    /// one. Ties are broken by the deterministic per-seed hash and then by
    /// position, so the result never depends on scene-enumeration order.
    ///
    /// A pair's two members are each other's nearest neighbor, so a naive
    /// rank-and-take-the-top-N would remove both at once instead of thinning one
    /// and leaving the other. To avoid that without paying for a full
    /// all-pairs recompute after every single removal, the walk "protects" a
    /// candidate's nearest neighbor as soon as the candidate itself is removed —
    /// a cheap approximation of "that pair isn't tight any more." This can
    /// under-protect in bigger clusters (3+ mutually close candidates), the
    /// accepted quality/speed tradeoff versus recomputing every step. If
    /// protection leaves the budget unmet (everything left is protected), a
    /// second pass fills the remainder in the same ranked order with protection
    /// dropped, so the requested budget is always met exactly.
    ///
    /// Pure and seed-driven: no <c>UnityEngine.Random</c>, no
    /// <c>System.Random</c>, no wall clock — the only inputs are the configured
    /// seed, a mechanic tag, and the already-placed positions (CLAUDE.md's
    /// seed-determinism rule).
    /// </summary>
    public static class ClusteredRemovalSelection
    {
        /// <summary>
        /// Picks exactly <paramref name="budget"/> of <paramref name="positions"/>
        /// for removal (fewer only if the budget exceeds the candidate count).
        /// Returns a parallel array: <c>true</c> = remove.
        /// </summary>
        public static bool[] Select(
            IReadOnlyList<GridPos> positions,
            int budget,
            int userSeed,
            string mechanicTag)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));

            int count = positions.Count;
            var removed = new bool[count];
            if (budget <= 0 || count == 0)
            {
                return removed;
            }

            if (budget > count) budget = count;

            // Each candidate's nearest neighbor, computed ONCE (O(n^2), not
            // O(budget * n^2)).
            var nearestDistSq = new long[count];
            var nearestPartner = new int[count];
            for (int i = 0; i < count; i++)
            {
                GridPos pi = positions[i];
                long best = long.MaxValue;
                int bestPartner = -1;
                uint bestKey = 0;
                for (int j = 0; j < count; j++)
                {
                    if (i == j) continue;
                    GridPos pj = positions[j];
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
                    if (key < bestKey || (key == bestKey && ComparePos(pj, positions[bestPartner]) < 0))
                    {
                        bestPartner = j;
                        bestKey = key;
                    }
                }

                nearestDistSq[i] = best;
                nearestPartner[i] = bestPartner;
            }

            var order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;

            Array.Sort(order, (a, b) =>
            {
                int cmp = nearestDistSq[a].CompareTo(nearestDistSq[b]);
                if (cmp != 0) return cmp;

                GridPos pa = positions[a];
                GridPos pb = positions[b];
                uint ka = DeterministicHash.RankKey(userSeed, mechanicTag, pa);
                uint kb = DeterministicHash.RankKey(userSeed, mechanicTag, pb);
                if (ka != kb) return ka < kb ? -1 : 1;
                return ComparePos(pa, pb);
            });

            var spared = new bool[count];
            int removedCount = 0;

            // Pass 1: take the closest-ranked candidate not currently spared,
            // sparing its nearest neighbor for the rest of this pass.
            for (int oi = 0; oi < count && removedCount < budget; oi++)
            {
                int i = order[oi];
                if (spared[i]) continue;

                removed[i] = true;
                removedCount++;

                int partner = nearestPartner[i];
                if (partner >= 0 && !removed[partner]) spared[partner] = true;
            }

            // Pass 2: budget still unmet (everything left was spared) - fill the
            // remainder in the same ranked order, protection dropped.
            if (removedCount < budget)
            {
                for (int oi = 0; oi < count && removedCount < budget; oi++)
                {
                    int i = order[oi];
                    if (removed[i]) continue;

                    removed[i] = true;
                    removedCount++;
                }
            }

            return removed;
        }

        /// <summary>
        /// The one rounding rule every Fairoots thinning pass uses: at least
        /// <c>floor(total * (1 - fraction))</c> candidates always survive (half,
        /// rounded down, for 0.5), so the removal budget is whatever's left over.
        /// The <c>+1e-9</c> keeps an exact fraction (0.5 * 100 = 50.0) flooring
        /// predictably instead of tripping on a representation like 49.9999999.
        /// </summary>
        public static int RemovalBudget(int total, double fraction)
        {
            if (total <= 0) return 0;

            double clamped = fraction < 0.0 ? 0.0 : (fraction > 1.0 ? 1.0 : fraction);
            int survivorTarget = (int)Math.Floor(total * (1.0 - clamped) + 1e-9);
            return total - survivorTarget;
        }

        internal static int ComparePos(GridPos a, GridPos b)
        {
            if (a.X != b.X) return a.X < b.X ? -1 : 1;
            if (a.Y != b.Y) return a.Y < b.Y ? -1 : 1;
            if (a.Z != b.Z) return a.Z < b.Z ? -1 : 1;
            return 0;
        }
    }
}
