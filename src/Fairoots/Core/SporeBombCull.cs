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
    ///      just because foliage removal overshot.
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
                // Rank each survivor by its deterministic hash; cull the lowest
                // `remainingBudget`. Ranking (rather than a per-object probability
                // threshold) is what makes the removed *count* exact while staying
                // fully deterministic. Ties break on position so the total order is
                // stable regardless of the order the caller enumerated the scene.
                survivors.Sort((a, b) =>
                {
                    uint ka = DeterministicHash.RankKey(userSeed, mechanicTag, positions[a]);
                    uint kb = DeterministicHash.RankKey(userSeed, mechanicTag, positions[b]);
                    if (ka != kb) return ka < kb ? -1 : 1;
                    return ComparePos(positions[a], positions[b]);
                });

                for (int i = 0; i < remainingBudget; i++)
                {
                    outcomes[survivors[i]] = CullOutcome.RemovedSeededCull;
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
