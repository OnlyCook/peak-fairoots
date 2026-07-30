using System;
using System.Collections.Generic;
using System.Linq;
using Fairoots.Core;
using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// The cull-budget proofs ROADMAP.md "Testing strategy" demands: same-seed
    /// reproducibility of the *specific* objects (not just the count), exact
    /// count/ratio correctness including the foliage-overshoot edge case, and
    /// different-seed variance. Foliage removal is a geometric fact, so it must be
    /// seed-independent and always unconditional.
    /// </summary>
    public class SporeBombCullTests
    {
        // Deterministic, spread-out candidate field with no foliage unless stated.
        private static (List<GridPos> pos, List<bool> foliage) MakeField(int n, Func<int, bool> foliage = null)
        {
            var pos = new List<GridPos>(n);
            var fol = new List<bool>(n);
            for (int i = 0; i < n; i++)
            {
                // Spread positions widely so rounded cells never collide.
                pos.Add(new GridPos(i * 10, (i % 9) * 7, (i % 5) * 13));
                fol.Add(foliage != null && foliage(i));
            }

            return (pos, fol);
        }

        [Fact]
        public void SameSeed_ProducesIdenticalSpecificDecisions()
        {
            var (pos, fol) = MakeField(100);
            var a = SporeBombCull.Decide(pos, fol, 0.5, userSeed: 555);
            var b = SporeBombCull.Decide(pos, fol, 0.5, userSeed: 555);
            Assert.Equal(a, b); // element-wise: same objects culled, not just same count
        }

        [Fact]
        public void HalfCull_RemovesExactlyHalfRoundedDown_Survives()
        {
            // CLAUDE.md wording: "exactly half, rounded down, survives".
            foreach (int n in new[] { 0, 1, 2, 3, 7, 50, 51, 100, 999 })
            {
                var (pos, fol) = MakeField(n);
                var outcomes = SporeBombCull.Decide(pos, fol, 0.5, userSeed: 1);
                var s = SporeBombCull.Summarize(outcomes);

                int expectedSurvivors = (int)Math.Floor(n * 0.5); // half, rounded down
                Assert.Equal(expectedSurvivors, s.Kept);
                Assert.Equal(n - expectedSurvivors, s.TotalRemoved);
                Assert.Equal(0, s.FoliageRemoved);
            }
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.25)]
        [InlineData(0.5)]
        [InlineData(0.75)]
        [InlineData(1.0)]
        public void CullFraction_HitsSurvivorFloorTarget(double fraction)
        {
            const int n = 400;
            var (pos, fol) = MakeField(n);
            var s = SporeBombCull.Summarize(SporeBombCull.Decide(pos, fol, fraction, userSeed: 9));

            int expectedSurvivors = (int)Math.Floor(n * (1.0 - fraction));
            Assert.Equal(expectedSurvivors, s.Kept);
        }

        [Fact]
        public void FoliageRemoval_IsUnconditionalAndSeedIndependent()
        {
            // Every 3rd candidate is in foliage. Foliage removals must be identical
            // regardless of seed or cull fraction - it's geometry, not a roll.
            var (pos, fol) = MakeField(90, i => i % 3 == 0);

            var s1 = SporeBombCull.Summarize(SporeBombCull.Decide(pos, fol, 0.0, userSeed: 1));
            var s2 = SporeBombCull.Summarize(SporeBombCull.Decide(pos, fol, 0.9, userSeed: 999));

            Assert.Equal(30, s1.FoliageRemoved);
            Assert.Equal(30, s2.FoliageRemoved);

            // With fraction 0, nothing beyond foliage is removed.
            Assert.Equal(0, s1.SeededRemoved);
        }

        [Fact]
        public void SeededCull_IsBudgetedAgainstFoliage_WorkedExample()
        {
            // RESEARCH.md Q7 worked example: 100 total, 20 in foliage, target 50%
            // => 50 removed total (20 foliage + 30 seeded), 50 kept. NOT 70 removed.
            var (pos, fol) = MakeField(100, i => i < 20);
            var s = SporeBombCull.Summarize(SporeBombCull.Decide(pos, fol, 0.5, userSeed: 7));

            Assert.Equal(20, s.FoliageRemoved);
            Assert.Equal(30, s.SeededRemoved);
            Assert.Equal(50, s.Kept);
            Assert.Equal(50, s.TotalRemoved);
        }

        [Fact]
        public void FoliageOvershoot_RemovesNothingExtra()
        {
            // 60 of 100 in foliage, target only 50% => foliage already exceeds the
            // target, so the seeded pass removes nothing (never overshoots the
            // preset just because foliage happened to).
            var (pos, fol) = MakeField(100, i => i < 60);
            var s = SporeBombCull.Summarize(SporeBombCull.Decide(pos, fol, 0.5, userSeed: 3));

            Assert.Equal(60, s.FoliageRemoved);
            Assert.Equal(0, s.SeededRemoved);
            Assert.Equal(40, s.Kept);
        }

        // --- enable-foliage-removal (Spore-Bombs, 2026-07-30) -------------------

        [Fact]
        public void FoliageRemovalDisabled_RemovesNoneForFoliage()
        {
            // Every 3rd candidate is in foliage, but the pass is off: not one
            // candidate may come back tagged RemovedFoliage, and with a removal
            // target of 0 that means nothing is removed at all.
            var (pos, fol) = MakeField(90, i => i % 3 == 0);
            var outcomes = SporeBombCull.Decide(pos, fol, 0.0, userSeed: 4, foliageRemovalEnabled: false);
            var s = SporeBombCull.Summarize(outcomes);

            Assert.Equal(0, s.FoliageRemoved);
            Assert.Equal(0, s.TotalRemoved);
            Assert.Equal(90, s.Kept);
            Assert.DoesNotContain(CullOutcome.RemovedFoliage, outcomes);
        }

        [Fact]
        public void FoliageRemovalDisabled_KeepsTheSameOverallRemovalTarget()
        {
            // The setting must not turn into "more spore bombs overall": the 50%
            // target still lands, it's just all seeded now (20 foliage + 30 seeded
            // with the pass on - see SeededCull_IsBudgetedAgainstFoliage - vs. 0 + 50
            // with it off).
            var (pos, fol) = MakeField(100, i => i < 20);
            var s = SporeBombCull.Summarize(
                SporeBombCull.Decide(pos, fol, 0.5, userSeed: 7, foliageRemovalEnabled: false));

            Assert.Equal(0, s.FoliageRemoved);
            Assert.Equal(50, s.SeededRemoved);
            Assert.Equal(50, s.Kept);
        }

        [Fact]
        public void FoliageRemovalDisabled_MakesFoliageCandidatesEligibleToSurvive()
        {
            // The actual player-visible promise: with the pass off, a bomb hidden in
            // a bush can survive. 60 of 100 in foliage at a 50% target means at
            // least 10 of them must still be standing (the budget can't reach them
            // all), and per-instance - not just as a count.
            var (pos, fol) = MakeField(100, i => i < 60);
            var outcomes = SporeBombCull.Decide(pos, fol, 0.5, userSeed: 3, foliageRemovalEnabled: false);

            int foliageSurvivors = Enumerable.Range(0, 100)
                .Count(i => fol[i] && outcomes[i] == CullOutcome.Kept);
            Assert.True(foliageSurvivors >= 10, $"expected at least 10 foliage survivors, got {foliageSurvivors}");

            // Contrast: with the pass on, the same field leaves none of them.
            var onOutcomes = SporeBombCull.Decide(pos, fol, 0.5, userSeed: 3, foliageRemovalEnabled: true);
            Assert.DoesNotContain(
                Enumerable.Range(0, 100),
                i => fol[i] && onOutcomes[i] == CullOutcome.Kept);
        }

        [Fact]
        public void FoliageRemovalDisabled_IsStillSeedDeterministic()
        {
            // CLAUDE.md's rule applies to the new code path too: same seed twice =>
            // the same specific bombs, and a different seed => a different set at
            // the same count.
            var (pos, fol) = MakeField(100, i => i % 4 == 0);

            var a = SporeBombCull.Decide(pos, fol, 0.5, userSeed: 555, foliageRemovalEnabled: false);
            var b = SporeBombCull.Decide(pos, fol, 0.5, userSeed: 555, foliageRemovalEnabled: false);
            Assert.Equal(a, b);

            var c = SporeBombCull.Decide(pos, fol, 0.5, userSeed: 556, foliageRemovalEnabled: false);
            Assert.NotEqual(a, c);
            Assert.Equal(
                a.Count(o => o != CullOutcome.Kept),
                c.Count(o => o != CullOutcome.Kept));
        }

        [Fact]
        public void DifferentSeed_ChangesWhichSurvivorsAreCulled()
        {
            // Same field, same count removed, but a different specific set.
            var (pos, fol) = MakeField(100);
            var a = SporeBombCull.Decide(pos, fol, 0.5, userSeed: 100);
            var b = SporeBombCull.Decide(pos, fol, 0.5, userSeed: 200);

            Assert.NotEqual(a, b); // some object's fate differs
            Assert.Equal(
                a.Count(o => o != CullOutcome.Kept),
                b.Count(o => o != CullOutcome.Kept)); // ...but the count is identical
        }

        [Fact]
        public void CullSelection_IsIndependentOfInputOrder()
        {
            // Shuffling the candidate list must not change which world positions get
            // culled - the decision keys off position, not iteration order. This is
            // what makes it multiplayer-consistent across clients (RESEARCH.md Q5).
            var (pos, fol) = MakeField(120);

            var baseline = SporeBombCull.Decide(pos, fol, 0.5, userSeed: 77);
            var culledPositions = pos
                .Where((_, i) => baseline[i] == CullOutcome.RemovedSeededCull)
                .ToHashSet();

            // Reverse the input order and re-run.
            var revPos = Enumerable.Reverse(pos).ToList();
            var revFol = Enumerable.Reverse(fol).ToList();
            var reversed = SporeBombCull.Decide(revPos, revFol, 0.5, userSeed: 77);
            var revCulled = revPos
                .Where((_, i) => reversed[i] == CullOutcome.RemovedSeededCull)
                .ToHashSet();

            Assert.Equal(culledPositions, revCulled);
        }

        [Fact]
        public void SeededCull_PrefersClusteredCandidatesOverIsolatedOnes()
        {
            // Two tight pairs (1 unit apart) plus two far-flung loners. Culling one
            // from the budget should always take from a clustered pair, never one
            // of the isolated candidates - clustered spore bombs are the whole
            // point of this pass, not a fair per-object lottery.
            var pos = new List<GridPos>
            {
                new GridPos(0, 0, 0),     // clustered pair A
                new GridPos(1, 0, 0),     // clustered pair A
                new GridPos(1000, 0, 0),  // clustered pair B
                new GridPos(1001, 0, 0),  // clustered pair B
                new GridPos(5000, 0, 0),  // isolated
                new GridPos(-5000, 0, 0), // isolated
            };
            var fol = new List<bool> { false, false, false, false, false, false };

            for (int seed = 0; seed < 20; seed++)
            {
                var outcomes = SporeBombCull.Decide(pos, fol, cullFraction: 1.0 / 6.0, userSeed: seed);
                var s = SporeBombCull.Summarize(outcomes);
                Assert.Equal(1, s.SeededRemoved);

                int culledIndex = outcomes.ToList().FindIndex(o => o == CullOutcome.RemovedSeededCull);
                Assert.True(culledIndex is 0 or 1 or 2 or 3, $"seed {seed} culled isolated candidate {culledIndex}");
            }
        }

        [Fact]
        public void SeededCull_TakesOneFromEachPairInsteadOfBothAtOnce()
        {
            // Two tight pairs (1 unit apart) and one isolated loner. Budget of 2
            // must take exactly one from *each* pair, never both from a single
            // pair while the loner and the other pair's second member survive -
            // that would mean the cull re-ranked once and gutted the first
            // cluster it found instead of moving on after thinning it.
            var pos = new List<GridPos>
            {
                new GridPos(0, 0, 0),     // pair A
                new GridPos(1, 0, 0),     // pair A
                new GridPos(1000, 0, 0),  // pair B
                new GridPos(1001, 0, 0),  // pair B
                new GridPos(5000, 0, 0),  // isolated
            };
            var fol = new List<bool> { false, false, false, false, false };

            for (int seed = 0; seed < 20; seed++)
            {
                var outcomes = SporeBombCull.Decide(pos, fol, cullFraction: 2.0 / 5.0, userSeed: seed);
                var s = SporeBombCull.Summarize(outcomes);
                Assert.Equal(2, s.SeededRemoved);

                // Loner always survives - it's never the closest pair.
                Assert.Equal(CullOutcome.Kept, outcomes[4]);

                // Exactly one of each pair is removed, one kept.
                Assert.Equal(1, new[] { outcomes[0], outcomes[1] }.Count(o => o == CullOutcome.RemovedSeededCull));
                Assert.Equal(1, new[] { outcomes[2], outcomes[3] }.Count(o => o == CullOutcome.RemovedSeededCull));
            }
        }

        [Fact]
        public void MismatchedLengths_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                SporeBombCull.Decide(new[] { new GridPos(0, 0, 0) }, new bool[0], 0.5, 1));
        }
    }
}
