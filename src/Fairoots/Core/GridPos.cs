namespace Fairoots.Core
{
    /// <summary>
    /// A world position rounded to an integer grid, used as the stable per-object
    /// identity that every Fairoots seeded decision keys off of.
    ///
    /// This is deliberately a plain, Unity-free struct: the pure decision layer
    /// (hashing, cull budgeting, presets) never references <c>UnityEngine.Vector3</c>
    /// so it stays unit-testable with no game install (see ROADMAP.md "Testing
    /// strategy"). The game-facing Harmony patches are responsible for rounding a
    /// real <c>Transform.position</c> into one of these via <see cref="Round"/>,
    /// mirroring the <c>Mathf.RoundToInt</c> convention the native
    /// <c>HelperFunctions.SetRandomSeedFromWorldPos</c> already uses (RESEARCH.md Q5).
    /// </summary>
    public readonly struct GridPos
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;

        public GridPos(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>
        /// Round three world-space float coordinates to the nearest integer grid
        /// cell. Rounds half away from zero for symmetry around the origin, so a
        /// position and its mirror hash to mirror cells rather than both biasing
        /// toward zero. This is the single rounding definition the whole mod uses;
        /// the game-facing layer must go through here rather than rolling its own,
        /// so the seed a decision sees is identical every load.
        /// </summary>
        public static GridPos Round(float x, float y, float z)
        {
            return new GridPos(RoundToInt(x), RoundToInt(y), RoundToInt(z));
        }

        private static int RoundToInt(float value)
        {
            // System.MathF.Round with AwayFromZero, but spelled out against double
            // to avoid depending on MathF availability across target frameworks.
            return (int)System.Math.Round(value, System.MidpointRounding.AwayFromZero);
        }

        public override string ToString() => $"({X}, {Y}, {Z})";
    }
}
