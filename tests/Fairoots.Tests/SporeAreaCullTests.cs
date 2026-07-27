using System;
using System.Collections.Generic;
using System.Linq;
using Fairoots.Core;
using Fairoots.Core.Presets;
using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// The seed-determinism proofs CLAUDE.md demands for the spore-area thinning
    /// pass: same seed → the same *specific* areas removed (not just the same
    /// count), exact count correctness across sizes and edge cases, different seeds
    /// → different-but-still-correctly-constrained results, and the cluster-first
    /// rule actually holding (the crowded areas go before the isolated ones - the
    /// whole point of the mechanic, and the part a count check alone can't catch).
    /// </summary>
    public class SporeAreaCullTests
    {
        /// <summary>A spread-out field where no two areas are near each other.</summary>
        private static List<GridPos> MakeField(int n)
        {
            var pos = new List<GridPos>(n);
            for (int i = 0; i < n; i++)
            {
                pos.Add(new GridPos(i * 100, (i % 7) * 40, (i % 5) * 60));
            }

            return pos;
        }

        [Fact]
        public void SameSeed_RemovesTheSameSpecificAreas()
        {
            var pos = MakeField(20);
            var a = SporeAreaCull.Decide(pos, 0.35, userSeed: 4242);
            var b = SporeAreaCull.Decide(pos, 0.35, userSeed: 4242);
            Assert.Equal(a, b); // element-wise, not just the count
        }

        [Fact]
        public void ZeroFraction_RemovesNothing()
        {
            foreach (int n in new[] { 0, 1, 2, 12, 23, 100 })
            {
                var flags = SporeAreaCull.Decide(MakeField(n), 0.0, userSeed: 7);
                Assert.Equal(n, flags.Length);
                Assert.All(flags, f => Assert.False(f));
            }
        }

        [Fact]
        public void RemovalCount_KeepsAtLeastFloorOfTheSurvivorTarget()
        {
            foreach (int n in new[] { 0, 1, 2, 3, 12, 23, 51, 100 })
            {
                foreach (double f in new[] { 0.0, 0.2, 0.35, 0.5, 1.0 })
                {
                    var flags = SporeAreaCull.Decide(MakeField(n), f, userSeed: 3);
                    int expectedSurvivors = (int)Math.Floor(n * (1.0 - f) + 1e-9);
                    int removed = SporeAreaCull.CountRemoved(flags);

                    Assert.Equal(n - expectedSurvivors, removed);
                    Assert.Equal(expectedSurvivors, n - removed);
                }
            }
        }

        [Fact]
        public void FractionIsClamped_OutOfRangeValuesDoNotThrowOrOverRemove()
        {
            var pos = MakeField(10);
            Assert.Equal(0, SporeAreaCull.CountRemoved(SporeAreaCull.Decide(pos, -1.0, userSeed: 1)));
            Assert.Equal(10, SporeAreaCull.CountRemoved(SporeAreaCull.Decide(pos, 2.0, userSeed: 1)));
        }

        [Fact]
        public void DifferentSeeds_ProduceDifferentSelections_ButTheSameCount()
        {
            var pos = MakeField(40);
            var a = SporeAreaCull.Decide(pos, 0.35, userSeed: 1);
            var b = SporeAreaCull.Decide(pos, 0.35, userSeed: 2);

            Assert.Equal(SporeAreaCull.CountRemoved(a), SporeAreaCull.CountRemoved(b));
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void MechanicTagsAreIndependent_SporeAreasDoNotCorrelateWithSporeBombs()
        {
            // Same seed, same positions, different mechanic tag - the selections
            // must not track each other, or one mechanic's removals would leak
            // information into (and visibly correlate with) the other's.
            var pos = MakeField(40);
            var areas = SporeAreaCull.Decide(pos, 0.35, userSeed: 99);
            var other = SporeAreaCull.Decide(pos, 0.35, userSeed: 99, mechanicTag: SporeBombCull.MechanicTag);
            Assert.NotEqual(areas, other);
        }

        [Fact]
        public void ClusteredAreasAreRemovedBeforeIsolatedOnes()
        {
            // Three tight pairs (2 units apart) plus four isolated areas far from
            // everything. With a budget of 3, all three removals should land inside
            // the pairs - and exactly one member of each pair, never both, so a
            // cluster gets thinned rather than erased.
            var pos = new List<GridPos>();
            for (int p = 0; p < 3; p++)
            {
                pos.Add(new GridPos(p * 500, 0, 0));
                pos.Add(new GridPos(p * 500 + 2, 0, 0));
            }

            for (int i = 0; i < 4; i++)
            {
                pos.Add(new GridPos(10_000 + i * 5_000, 0, 0));
            }

            // 10 areas, remove 3 -> fraction 0.3 (floor(10*0.7) = 7 survivors).
            var flags = SporeAreaCull.Decide(pos, 0.3, userSeed: 12345);
            Assert.Equal(3, SporeAreaCull.CountRemoved(flags));

            for (int p = 0; p < 3; p++)
            {
                int removedInPair = (flags[p * 2] ? 1 : 0) + (flags[p * 2 + 1] ? 1 : 0);
                Assert.Equal(1, removedInPair);
            }

            for (int i = 6; i < 10; i++)
            {
                Assert.False(flags[i], "an isolated spore area was removed before the clustered ones");
            }
        }

        [Fact]
        public void ResultIsIndependentOfInputOrder()
        {
            // Scene enumeration order is not a stable thing to depend on - the same
            // set of positions in a different order must produce the same set of
            // removed *positions* (CLAUDE.md: decisions key off position only).
            var pos = MakeField(30);
            var flags = SporeAreaCull.Decide(pos, 0.35, userSeed: 808);
            var removedByPosition = pos.Where((_, i) => flags[i]).OrderBy(p => p.X).ToList();

            var shuffled = pos.AsEnumerable().Reverse().ToList();
            var shuffledFlags = SporeAreaCull.Decide(shuffled, 0.35, userSeed: 808);
            var shuffledRemoved = shuffled.Where((_, i) => shuffledFlags[i]).OrderBy(p => p.X).ToList();

            Assert.Equal(removedByPosition, shuffledRemoved);
        }

        [Fact]
        public void SubtleAndBalancedRemoveNoSporeAreas()
        {
            // ROADMAP.md/maintainer's explicit call: the two lightest presets must
            // not thin spore areas at all, unlike spore bombs.
            Assert.Equal(0.0, PresetCatalog.SporeAreaRemovalFraction(PresetId.Subtle));
            Assert.Equal(0.0, PresetCatalog.SporeAreaRemovalFraction(PresetId.Balanced));
            Assert.True(PresetCatalog.SporeAreaRemovalFraction(PresetId.Generous) > 0.0);
            Assert.True(PresetCatalog.SporeAreaRemovalFraction(PresetId.Tame)
                        > PresetCatalog.SporeAreaRemovalFraction(PresetId.Generous));
        }
    }
}
