namespace Fairoots
{
    /// <summary>
    /// The single "is Fairoots allowed to do anything right now?" gate.
    ///
    /// <b>Why this exists.</b> Fairoots is a Roots-biome mod, but a lot of what it
    /// patches is not Roots-specific code: <c>WindChillZone</c> drives the wind in
    /// every biome, <c>CharacterAfflictions</c> is the player's own status component
    /// and outlives every level, <c>CharacterClimbing</c>/<c>CharacterRopeHandling</c>/
    /// <c>CharacterVineClimbing</c> run wherever the player climbs, and <c>Mob</c>/
    /// <c>MushroomZombie</c> are shared creature base classes. Left ungated, those
    /// patches would happily rebalance biomes the mod has no business touching.
    /// Every game-facing patch therefore checks <see cref="Active"/> before doing
    /// anything, and everything that writes onto a native field restores its cached
    /// vanilla baseline when it's false (see <c>RootsLevelWatcher.RestoreVanilla</c>,
    /// which drives that restore the moment the biome unloads).
    ///
    /// <b>Two flags, deliberately.</b> <see cref="Active"/> flips true the instant the
    /// Roots Segment is detected - before the per-level setup passes run - because
    /// those passes are themselves Fairoots work and must see the gate open.
    /// <see cref="Busy"/> covers just the setup window, and is what
    /// <see cref="FairootsInterop"/> publishes so another mod's loading screen can be
    /// held up until Fairoots has finished (see that class).
    ///
    /// Written only by <see cref="RootsLevelWatcher"/>; read from everywhere.
    /// </summary>
    internal static class RootsState
    {
        /// <summary>
        /// Whether a Roots Segment is currently loaded. The gate every game-facing
        /// patch checks - false means "behave exactly like unmodded PEAK".
        /// </summary>
        internal static bool Active { get; private set; }

        /// <summary>
        /// Whether the per-level setup passes are running right now. True from the
        /// frame the Roots Segment is detected until the last pass has finished.
        /// </summary>
        internal static bool Busy { get; private set; }

        internal static void EnterLevel()
        {
            Active = true;
            Busy = true;
        }

        internal static void SetupFinished() => Busy = false;

        internal static void ExitLevel()
        {
            Active = false;
            Busy = false;
        }
    }

    /// <summary>
    /// Fairoots' only public, deliberately-stable API surface: a "don't take your
    /// loading screen down yet" signal for other mods, resolved by reflection rather
    /// than an assembly reference (neither mod should have to build against the
    /// other).
    ///
    /// Written for <c>peak-checkpoint-save</c>, whose Quick Resume flow loads a Roots
    /// campfire and then fades its own loading screen out. Fairoots does a burst of
    /// per-level work the moment that biome appears (the seeded spore-bomb cull and
    /// friends - see <see cref="RootsLevelWatcher"/>), and a loading screen that
    /// disappears mid-burst drops the player into a visibly hitching world. Polling
    /// <see cref="ShouldHoldLoadingScreen"/> until it returns false keeps the two in
    /// step, in single-player and co-op alike, since every client runs its own copy of
    /// this work.
    ///
    /// Contract, so it can be relied on from the outside: the name, the namespace and
    /// the signature are stable; a caller that can't find the type should carry on as
    /// though Fairoots isn't installed; and the flag is guaranteed to become false on
    /// its own (the setup runner clears it in a <c>finally</c>), so a poll loop can
    /// safely wait on it - though a caller should still cap the wait, since a caller
    /// that hangs forever on another mod is worse than one that gives up.
    /// </summary>
    public static class FairootsInterop
    {
        /// <summary>
        /// True while Fairoots either is applying its per-level changes to a
        /// freshly-loaded Roots biome, or is about to. Another mod's loading screen
        /// should stay up until this reads false.
        ///
        /// <b>The "or is about to" half is the part that matters</b>, and it's why
        /// this isn't simply <c>RootsState.Busy</c>. There is a window where the biome
        /// is live, the other mod's teleport has finished, and Fairoots hasn't started
        /// its passes yet - a loading screen taken down inside that window would hand
        /// the player a world that then visibly hitches. Deferring to
        /// <see cref="RootsLevelWatcher.IsInLiveRootsBiome"/> (the game's own current
        /// biome, not a cached flag) closes it regardless of which mod's timing wins
        /// the race, and costs an array index plus two field reads - so a resume into
        /// any other biome pays essentially nothing per frame.
        /// </summary>
        public static bool ShouldHoldLoadingScreen()
        {
            if (RootsState.Busy)
            {
                return true;
            }

            if (RootsState.Active)
            {
                return false; // setup already finished for the loaded biome.
            }

            // Session-diagnosed 2026-08-06: RootsLevelWatcher.CheckAndRun() won't enter
            // Roots (and so never flips Busy true) until a local character exists to do
            // the entering FOR (see its own remarks on why that check is deliberately
            // entry-only). Without this, a character that's slow to (re)spawn - a real
            // observed failure mode, unrelated to Fairoots itself - reads as "biome live,
            // never entering" forever: RootsBiomeIsLive alone doesn't need a character, so
            // it would keep answering true and strand a caller's loading screen up for its
            // own full timeout instead of the brief, real setup window this method exists
            // to describe. No character also means RootsLevelWatcher.CheckAndRun() can't
            // possibly be about to start Busy work this frame either way, so "false" here
            // is never a lie - just correctly refusing to promise a hold this method can't
            // back up
            if (Character.localCharacter == null)
            {
                return false;
            }

            return RootsLevelWatcher.RootsBiomeIsLive;
        }

        /// <summary>Whether a Roots Segment is loaded and Fairoots is active on it.</summary>
        public static bool RootsActive => RootsState.Active;
    }
}
