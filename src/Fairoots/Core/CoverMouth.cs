namespace Fairoots.Core
{
    /// <summary>
    /// The pure state logic behind the cover-your-mouth counterplay mechanic
    /// (ROADMAP.md's "New: cover-mouth vs. spore areas" row): hold-vs-toggle input
    /// resolution and the stamina cost. Unity-free, so the awkward part - a two-mode
    /// input state machine that also has to be force-cancellable from outside - is
    /// unit-testable instead of only observable by pressing a key in-game.
    ///
    /// Not seed-gated: it's a player action, not a placement decision, so there's no
    /// RNG anywhere near it.
    /// </summary>
    public static class CoverMouth
    {
        /// <summary>
        /// The next covering state.
        ///
        /// <paramref name="allowed"/> is the outside veto (climbing, unconscious, out
        /// of stamina, a menu open, the mechanic switched off...). It force-cancels an
        /// active cover and blocks a new one, in <em>both</em> input modes - a toggle
        /// that stayed latched while the game had already stopped honouring it would
        /// leave the player holding an invisible breath forever, and re-pressing the
        /// key would then read as "start covering" while it was actually "clear a
        /// stuck flag."
        /// </summary>
        /// <param name="covering">The current state.</param>
        /// <param name="keyPressedThisFrame">Key went down this frame (the toggle edge).</param>
        /// <param name="keyHeld">Key is down right now (hold mode's whole input).</param>
        /// <param name="holdMode">True = hold the key to cover, false = press to toggle.</param>
        /// <param name="allowed">Whether covering is permitted at all right now.</param>
        public static bool NextState(
            bool covering,
            bool keyPressedThisFrame,
            bool keyHeld,
            bool holdMode,
            bool allowed)
        {
            if (!allowed)
            {
                return false;
            }

            if (holdMode)
            {
                return keyHeld;
            }

            return keyPressedThisFrame ? !covering : covering;
        }

        /// <summary>
        /// Stamina to consume this frame for holding a mouth cover.
        /// <paramref name="ratePerSecond"/> is the configured drain; multiplying by
        /// the frame time makes the cost framerate-independent (the alternative -
        /// a flat per-frame cost - would charge a 144fps player over twice what a
        /// 60fps player pays for the same held breath).
        /// </summary>
        public static float StaminaCost(double ratePerSecond, float deltaSeconds)
        {
            if (ratePerSecond <= 0.0 || deltaSeconds <= 0f)
            {
                return 0f;
            }

            return (float)(ratePerSecond * deltaSeconds);
        }
    }
}
