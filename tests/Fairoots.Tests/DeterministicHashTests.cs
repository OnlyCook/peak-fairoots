using System.Collections.Generic;
using Fairoots.Core;
using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// Proves the property the whole mod rests on: the decision hash is a pure,
    /// stable function of (seed, mechanic, position) - same in, same out, and
    /// different in, (almost always) different out. If any of these break, "same
    /// seed = same spore bombs" is a lie (CLAUDE.md seed-determinism rule).
    /// </summary>
    public class DeterministicHashTests
    {
        [Fact]
        public void SameKey_ProducesSameHash()
        {
            var pos = new GridPos(12, -4, 88);
            uint a = DeterministicHash.Hash(1234, "spore-bomb-cull", pos);
            uint b = DeterministicHash.Hash(1234, "spore-bomb-cull", pos);
            Assert.Equal(a, b);
        }

        [Fact]
        public void UnitValue_IsInHalfOpenUnitInterval()
        {
            // Sweep a range of positions; every value must land in [0, 1).
            for (int x = -50; x <= 50; x++)
            {
                double v = DeterministicHash.UnitValue(7, "m", new GridPos(x, x * 3, -x));
                Assert.InRange(v, 0.0, 0.9999999999);
            }
        }

        [Fact]
        public void UnitValue_IsRoughlyUniform()
        {
            // A weak distribution sanity check: over many positions, the mean of a
            // uniform [0,1) value should sit near 0.5. Guards against the hash
            // collapsing to a narrow band.
            double sum = 0;
            int n = 0;
            for (int x = 0; x < 200; x++)
            {
                for (int z = 0; z < 50; z++)
                {
                    sum += DeterministicHash.UnitValue(42, "dist", new GridPos(x, 0, z));
                    n++;
                }
            }

            double mean = sum / n;
            Assert.InRange(mean, 0.45, 0.55);
        }

        [Fact]
        public void DifferentSeed_ChangesMostDecisions()
        {
            // Two different seeds should disagree on the great majority of a fixed
            // set of positions (regression guard against the seed being ignored).
            int changed = 0;
            const int count = 500;
            for (int i = 0; i < count; i++)
            {
                var pos = new GridPos(i, i % 7, i % 13);
                bool cullA = DeterministicHash.UnitValue(111, "spore-bomb-cull", pos) < 0.5;
                bool cullB = DeterministicHash.UnitValue(222, "spore-bomb-cull", pos) < 0.5;
                if (cullA != cullB) changed++;
            }

            // Independent 50/50 coins disagree ~50% of the time; require a wide
            // margin so this is a real signal, not a flake.
            Assert.InRange(changed, count * 0.35, count * 0.65);
        }

        [Fact]
        public void DifferentMechanicTag_Decorrelates()
        {
            // Same seed and position, different mechanic tag => independent streams,
            // so two mechanics never accidentally cull/keep in lockstep.
            int changed = 0;
            const int count = 500;
            for (int i = 0; i < count; i++)
            {
                var pos = new GridPos(i * 2, -i, i % 5);
                bool a = DeterministicHash.UnitValue(9, "mechanic-a", pos) < 0.5;
                bool b = DeterministicHash.UnitValue(9, "mechanic-b", pos) < 0.5;
                if (a != b) changed++;
            }

            Assert.InRange(changed, count * 0.35, count * 0.65);
        }

        [Fact]
        public void DistinctPositions_MostlyDistinctRankKeys()
        {
            // Rank keys must spread across positions; heavy collisions would make
            // the cull selection order degenerate. Allow a few birthday-paradox
            // collisions but demand near-uniqueness.
            var seen = new HashSet<uint>();
            int total = 0;
            for (int x = 0; x < 60; x++)
            {
                for (int z = 0; z < 60; z++)
                {
                    seen.Add(DeterministicHash.RankKey(3, "spore-bomb-cull", new GridPos(x, 0, z)));
                    total++;
                }
            }

            Assert.True(seen.Count > total * 0.999,
                $"expected near-unique rank keys, got {seen.Count} distinct of {total}");
        }

        [Fact]
        public void GridPos_RoundsHalfAwayFromZero()
        {
            Assert.Equal(new GridPos(1, -1, 3), GridPos.Round(0.5f, -0.5f, 2.5f));
            Assert.Equal(new GridPos(2, -3, 0), GridPos.Round(1.6f, -2.6f, 0.4f));
        }
    }
}
