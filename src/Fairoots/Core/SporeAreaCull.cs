using System;
using System.Collections.Generic;

namespace Fairoots.Core
{
    /// <summary>
    /// Phase 6 (ROADMAP.md): the decision behind "make spore areas less common."
    /// Pure and Unity-free - the game-facing
    /// <c>SporeAreas/SporeAreaCullPatch</c> scans the already-placed Roots scene
    /// for spore emitters, hands this their rounded positions, and maps the
    /// returned per-index flags back onto real GameObjects.
    ///
    /// Simpler than <see cref="SporeBombCull"/>: there is no unconditional
    /// foliage pass here (a spore cloud is a 16-unit-radius volume around a
    /// mushroom, not a small prop that can get buried in a fern), so this is
    /// just one budgeted, cluster-first seeded removal -
    /// <see cref="ClusteredRemovalSelection"/>, the same selection the spore-bomb
    /// cull's second pass uses, under its own mechanic tag so the two mechanics'
    /// choices never correlate.
    ///
    /// Cluster-first is the maintainer's explicit requirement, and it's what
    /// makes this a fairness change rather than just "less content": the spore
    /// areas that actually hurt a run are the ones whose 16-unit radii overlap,
    /// forming a stretch of the biome you can't cross without taking spores. So
    /// removal always starts with the emitter closest to another emitter and
    /// works outward, thinning dense overlaps first and leaving isolated clouds
    /// (which a player can simply walk around) alone.
    /// </summary>
    public static class SporeAreaCull
    {
        public const string MechanicTag = "spore-area-cull";

        /// <summary>
        /// Which spore areas to remove. Returns a parallel array: <c>true</c> =
        /// remove. A <paramref name="removalFraction"/> of 0 (Subtle and Balanced,
        /// per ROADMAP.md's preset table) removes nothing at all, so the returned
        /// array is all-<c>false</c> and the caller touches nothing.
        /// </summary>
        /// <param name="removalFraction">
        /// Target fraction of the level's spore areas to remove, in [0, 1]. At
        /// least <c>floor(total * (1 - fraction))</c> always survive - see
        /// <see cref="ClusteredRemovalSelection.RemovalBudget"/>.
        /// </param>
        public static bool[] Decide(
            IReadOnlyList<GridPos> positions,
            double removalFraction,
            int userSeed,
            string mechanicTag = MechanicTag)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));

            int budget = ClusteredRemovalSelection.RemovalBudget(positions.Count, removalFraction);
            return ClusteredRemovalSelection.Select(positions, budget, userSeed, mechanicTag);
        }

        /// <summary>How many of <paramref name="flags"/> are marked for removal - for logging and test assertions.</summary>
        public static int CountRemoved(IReadOnlyList<bool> flags)
        {
            if (flags == null) throw new ArgumentNullException(nameof(flags));

            int n = 0;
            for (int i = 0; i < flags.Count; i++)
            {
                if (flags[i]) n++;
            }

            return n;
        }
    }
}
