namespace Fairoots.Core
{
    /// <summary>
    /// The heart of Fairoots' determinism guarantee: a pure, runtime-independent
    /// hash mapping <c>(userSeed, mechanicTag, roundedPosition)</c> to a stable
    /// value. Same inputs -> same output, on any machine, on any .NET/Mono runtime,
    /// on every game launch. Nothing here touches <c>UnityEngine.Random</c> or any
    /// process-global state.
    ///
    /// Why a hand-rolled hash rather than <c>System.HashCode.Combine</c> or
    /// <c>string.GetHashCode()</c>: both of those are randomized once per process
    /// on modern .NET (a per-run seed), so they would produce *different* results
    /// on different game launches - silently breaking the "same seed = same spore
    /// bombs" premise that is this mod's entire reason to exist (CLAUDE.md
    /// "seed-determinism rule"). And <c>System.Random</c>'s internal sequence has
    /// differed between .NET Framework/Mono and .NET Core, so seeding it wouldn't
    /// give identical results between the game (Mono) and the test runner (.NET 10).
    /// This FNV-1a + murmur3-finalizer construction is fully specified in code, so
    /// it is bit-for-bit identical everywhere - which is exactly what lets the unit
    /// tests actually prove the in-game behavior.
    /// </summary>
    public static class DeterministicHash
    {
        private const uint FnvOffsetBasis = 2166136261u;
        private const uint FnvPrime = 16777619u;

        /// <summary>
        /// Hash the full decision key to a well-distributed 32-bit value. The
        /// <paramref name="mechanicTag"/> namespaces each mechanic's stream so two
        /// different decisions at the same position never correlate (RESEARCH.md Q5).
        /// </summary>
        public static uint Hash(int userSeed, string mechanicTag, GridPos pos)
        {
            uint h = FnvOffsetBasis;
            h = MixInt(h, userSeed);
            h = MixString(h, mechanicTag);
            h = MixInt(h, pos.X);
            h = MixInt(h, pos.Y);
            h = MixInt(h, pos.Z);
            return Finalize(h);
        }

        /// <summary>
        /// A deterministic value in the half-open range [0, 1) for the given key.
        /// Use this for probability-style "does this pass at rate p" decisions:
        /// <c>UnitValue(...) &lt; p</c>. Derived from the top 24 bits of the hash so
        /// the mantissa maps cleanly onto representable doubles.
        /// </summary>
        public static double UnitValue(int userSeed, string mechanicTag, GridPos pos)
        {
            uint h = Hash(userSeed, mechanicTag, pos);
            return (h >> 8) * (1.0 / 16777216.0); // top 24 bits / 2^24
        }

        /// <summary>
        /// A deterministic sort key for ranked selection (e.g. "cull exactly the N
        /// lowest-ranked survivors"). Returns the raw 32-bit hash for maximum
        /// spread; callers break ties on position so the total order is stable.
        /// </summary>
        public static uint RankKey(int userSeed, string mechanicTag, GridPos pos)
        {
            return Hash(userSeed, mechanicTag, pos);
        }

        private static uint MixInt(uint h, int value)
        {
            uint v = unchecked((uint)value);
            h = (h ^ (v & 0xFF)) * FnvPrime;
            h = (h ^ ((v >> 8) & 0xFF)) * FnvPrime;
            h = (h ^ ((v >> 16) & 0xFF)) * FnvPrime;
            h = (h ^ ((v >> 24) & 0xFF)) * FnvPrime;
            return h;
        }

        private static uint MixString(uint h, string s)
        {
            // Length-prefixed so ("a","b") can never collide with ("ab","") etc.,
            // and hashed per-char (two bytes) with no reliance on string.GetHashCode.
            h = MixInt(h, s == null ? -1 : s.Length);
            if (s == null)
            {
                return h;
            }

            for (int i = 0; i < s.Length; i++)
            {
                ushort c = s[i];
                h = (h ^ (uint)(c & 0xFF)) * FnvPrime;
                h = (h ^ (uint)((c >> 8) & 0xFF)) * FnvPrime;
            }

            return h;
        }

        // murmur3 fmix32 finalizer - cheap avalanche so nearby keys (adjacent grid
        // cells, off-by-one seeds) don't produce visibly correlated outputs.
        private static uint Finalize(uint h)
        {
            unchecked
            {
                h ^= h >> 16;
                h *= 0x85ebca6bu;
                h ^= h >> 13;
                h *= 0xc2b2ae35u;
                h ^= h >> 16;
                return h;
            }
        }
    }
}
