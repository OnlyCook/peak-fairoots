using System;
using System.Collections;
using Fairoots.Diagnostics;
using Fairoots.SporeBombs;
using Fairoots.Ui;
using UnityEngine;
using Zorro.Core; // Singleton<T> (Zorro.Core.Runtime.dll) - MapHandler's base

namespace Fairoots
{
    /// <summary>
    /// Decides when Fairoots is awake. Answers one question every frame - "is the
    /// player standing in a live Roots biome right now?" - and drives the per-level
    /// setup passes on the way in and the vanilla restore on the way out
    /// (<see cref="RootsState"/>).
    ///
    /// <b>It asks the game, rather than searching the scene.</b> Earlier versions
    /// polled <c>GameObject.Find("Roots Segment")</c> twice a second, which is a
    /// linear search over every active object in every loaded scene - a real cost on
    /// a slower machine, paid forever, in every biome, to answer a question the game
    /// already knows. <c>MapHandler</c> tracks the current segment itself, so
    /// <see cref="IsInLiveRootsBiome"/> is now an array index and a couple of field
    /// reads. Cheap enough to run every frame, which also makes it more responsive
    /// than the old half-second poll rather than less.
    ///
    /// A Harmony hook was the other option and is the worse one here.
    /// <c>MapHandler.ActivateWithoutMessageQueue</c> is the single funnel every
    /// segment activation passes through (<c>GoToSegment</c> for normal campfire
    /// progression, <c>JumpToSegmentLogic</c> for a save resume or debug jump), so it
    /// would work - but segment <em>deactivation</em> is a bare
    /// <c>segmentParent.SetActive(false)</c> at three separate call sites with no
    /// shared seam, so leaving the biome would still need watching for. One cheap
    /// state read covers both directions with nothing to keep in sync, and it can't
    /// silently stop working if a game update renames a private method.
    ///
    /// <b>Why the game's own answer and not "is there a Roots Segment in the scene".</b>
    /// Live-reported 2026-07-30: PEAK's main menu runs a Roots biome as its animated
    /// background, so a name/scene search finds a perfectly real Roots Segment (107
    /// spore bombs, 12 spore areas) sitting behind the menu, and Fairoots dutifully
    /// culled it - visible in the log as a setup pass with zero characters in the
    /// scene. <c>MapHandler</c> only exists and reports a current segment inside an
    /// actual run, so asking it makes the menu background a non-event by
    /// construction instead of by another special case.
    ///
    /// <b>The setup passes run behind a loading screen, one per frame</b>
    /// (<see cref="RunSetup"/>). They used to all fire inside the single
    /// <c>Update()</c> tick that detected the biome, which is a long stall on the
    /// main thread - live-reported as a huge stutter the moment the biome loads in
    /// after lighting the campfire. The work itself can't be deferred into gameplay
    /// (the player must not reach a spore bomb the cull is about to remove), so what
    /// changed is the presentation: <see cref="RootsLoadingOverlay"/> goes up and is
    /// given a frame to actually render before the first heavy pass starts, and the
    /// passes then take a frame each so the screen stays alive rather than frozen.
    /// </summary>
    internal static class RootsLevelWatcher
    {
        /// <summary>How fast the setup overlay fades in and out, in alpha per second.</summary>
        private const float OverlayFadeSpeed = 6f;

        /// <summary>
        /// The Roots biome object currently being treated as live, or <c>null</c>. Only
        /// used to hand the setup passes something to walk and to notice the object
        /// being destroyed underneath us; whether Fairoots should be awake at all is
        /// decided by <see cref="IsInLiveRootsBiome"/>, not by this reference.
        /// </summary>
        private static Transform _processed;

        /// <summary>
        /// Whether we currently believe we are in Roots. Tracked separately from
        /// <see cref="_processed"/> because a destroyed <see cref="Transform"/> compares
        /// equal to Unity's overridden <c>null</c>, so <c>_processed != null</c> alone
        /// can't tell "never entered a biome" from "entered one, and it's since been
        /// torn down".
        /// </summary>
        private static bool _levelLoaded;

        /// <summary>
        /// The running setup coroutine, so a biome torn down mid-setup can stop it
        /// rather than let it keep applying passes to a level that no longer exists.
        /// </summary>
        private static Coroutine _setup;

        internal static void CheckAndRun()
        {
            bool inRoots = IsInLiveRootsBiome();

            if (_levelLoaded && (!inRoots || _processed == null))
            {
                ExitLevel();
                return;
            }

            if (_levelLoaded || !inRoots)
            {
                return;
            }

            // Entry-only, deliberately not part of IsInLiveRootsBiome: nothing this mod
            // does means anything before there is a player to do it to, but a character
            // that goes momentarily null while the biome is still loaded (a death, a
            // respawn, a rebuild) must NOT read as "left Roots" - that would tear down
            // the level state and then run the whole cull again mid-biome.
            if (Character.localCharacter == null)
            {
                return;
            }

            Transform segment = ResolveSegmentRoot();
            if (segment == null)
            {
                return; // biome is live but its object isn't reachable yet - try again next frame.
            }

            EnterLevel(segment);
        }

        /// <summary>
        /// Whether the player is standing in a live Roots biome, straight out of the
        /// game's own state. Every term is O(1) and this runs once per frame - see the
        /// class remarks for why it replaced a twice-a-second scene search.
        ///
        /// Each condition earns its place:
        /// <list type="bullet">
        /// <item><c>MapHandler.ExistsAndInitialized</c> - there is a real run in
        /// progress. This is what keeps the main menu's animated Roots background from
        /// waking the mod up (live-reported 2026-07-30), and it covers the airport and
        /// every loading screen for free.</item>
        /// <item><c>GetCurrentBiome() == Roots</c> - the game's own answer, which
        /// correctly resolves Roots as the variant occupying the Tropics segment
        /// rather than making us infer it from object names.</item>
        /// <item><b>The segment object is actually active.</b> Load-bearing, not
        /// belt-and-braces: <c>GoToSegment</c> increments <c>currentSegment</c> and
        /// then waits on a fog transition plus a full second before switching the
        /// object on, so for that whole window the game says "Roots" while the biome
        /// is still dark. Running the setup passes then would cull a segment that
        /// hasn't been enabled.</item>
        /// </list>
        ///
        /// A local character having spawned is checked too, but by the caller and only
        /// on the way <em>in</em> - see <see cref="CheckAndRun"/>.
        /// </summary>
        internal static bool RootsBiomeIsLive => IsInLiveRootsBiome();

        private static bool IsInLiveRootsBiome()
        {
            try
            {
                if (!MapHandler.ExistsAndInitialized)
                {
                    return false;
                }

                var handler = Singleton<MapHandler>.Instance;
                if (handler.GetCurrentBiome() != Biome.BiomeType.Roots)
                {
                    return false;
                }

                GameObject parent = MapHandler.CurrentMapSegment?.segmentParent;
                return parent != null && parent.activeInHierarchy;
            }
            catch (Exception e)
            {
                // A game update reshaping MapHandler must not throw once per frame.
                Diag.Error($"[RootsLevelWatcher] biome check threw: {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// The transform the setup passes walk. Prefers the object literally named
        /// "Roots Segment" - the scope every pass in this mod was written and
        /// field-tested against (confirmed candidate counts of ~390-410 spore bombs and
        /// 22-24 spore areas) - and falls back to the biome's own segment parent, one
        /// level up, if a game update ever renames it. Resolved once per biome entry,
        /// never per frame.
        /// </summary>
        private static Transform ResolveSegmentRoot()
        {
            var byName = GameObject.Find("Roots Segment");
            if (byName != null && byName.activeInHierarchy)
            {
                return byName.transform;
            }

            GameObject parent = MapHandler.CurrentMapSegment?.segmentParent;
            if (parent == null)
            {
                return null;
            }

            Diag.Warn(
                "[RootsLevelWatcher] no active \"Roots Segment\" object found - falling back to the biome's " +
                $"segment parent \"{parent.name}\". If spore-bomb counts look wrong, this is why.");
            return parent.transform;
        }

        private static void EnterLevel(Transform rootsSegment)
        {
            _processed = rootsSegment;
            _levelLoaded = true;

            // Opened before the passes below run, not after: those passes ARE the
            // mod's work, and every one of them reads a value or writes a field that
            // this gate controls (see RootsState).
            RootsState.EnterLevel();

            var host = Plugin.Instance;
            if (host == null)
            {
                // No MonoBehaviour to hang a coroutine off (shouldn't happen - the
                // watcher is polled from that very component's Update). Run the
                // passes inline rather than skip the level entirely; the player gets
                // the old single-frame stall, which is worse than a loading screen
                // but far better than an unmodded Roots biome.
                Diag.Warn("[RootsLevelWatcher] no plugin instance to drive the setup coroutine - running the passes inline.");
                RunPassesInline(rootsSegment);
                RootsState.SetupFinished();
                return;
            }

            _setup = host.StartCoroutine(RunSetup(rootsSegment));
        }

        private static void ExitLevel()
        {
            // Left the Roots biome (moved on to the next one, returned to the
            // airport, or the level was torn down under us).
            _levelLoaded = false;
            _processed = null;

            if (_setup != null && Plugin.Instance != null)
            {
                Plugin.Instance.StopCoroutine(_setup);
            }

            _setup = null;
            RootsLoadingOverlay.Hide();

            // Closes the gate FIRST: every restore pass below works by re-running the
            // mod's own apply logic, which with the gate shut resolves to "write the
            // cached vanilla baseline back" rather than "scale it" (see RootsState).
            RootsState.ExitLevel();
            RestoreVanilla();
            ClearLevelState();
        }

        /// <summary>
        /// Hands back every native field the mod wrote to that can outlive the Roots
        /// biome. Most of what Fairoots touches is Roots scenery and dies with the
        /// segment, but three categories don't and would otherwise carry modded values
        /// into the next biome:
        /// <list type="bullet">
        /// <item><c>CharacterAfflictions</c> - the player's own spore recovery fields.
        /// Explicitly documented in <c>SporeDecayPatch</c> as outliving a segment.</item>
        /// <item><c>WindChillZone</c> - wind is a whole-mountain system, not a Roots
        /// one, so a scaled gust duration left behind would rebalance every biome
        /// above it.</item>
        /// <item>Creatures and anything this mod deactivated - a zombie raised from a
        /// dead player, or a beetle a kill switch hid, can both survive the
        /// transition.</item>
        /// </list>
        /// Deliberately <em>not</em> included: <c>SporeBombRecolorPatch.ReapplyToAll</c>
        /// and the spore-area cosmetic passes. Those only ever touch objects that are
        /// part of the Roots scene and are destroyed with it, and the recolor pass in
        /// particular walks every Transform in every loaded scene - a heavy sweep to
        /// pay on every biome transition for objects that no longer exist.
        /// </summary>
        private static void RestoreVanilla()
        {
            Run("wind", Wind.WindChillZoneTuningPatch.ReapplyAll);
            Run("spore recovery", Spores.SporeDecayPatch.ReapplyToAll);
            Run("creature speed", Creatures.CreatureSpeedPatch.ReapplyToAll);
            Run("creature knockback", Creatures.CreatureKnockbackPatch.ReapplyToAll);
            Run("creature ragdoll", Creatures.CreatureRagdollPatch.ReapplyToAll);
            Run("creature kill switches", Creatures.CreatureDisablePatch.ReapplyToAll);
            Run("spore-area kill switch", SporeAreas.SporeAreaDisablePatch.ReapplyToAll);

            Diag.Info("[RootsLevelWatcher] Roots Segment unloaded - Fairoots is inactive and vanilla values are restored.");
        }

        private static void ClearLevelState()
        {
            SporeBombCullPatch.RemovedPositions.Clear();
            SporeBombCullPatch.KeptTriggerColliders.Clear();
            SporeAreas.SporeAreaDisablePatch.ClearLevelState();
            SporeAreas.SporeAreaCullPatch.ClearLevelState();
            SporeAreas.CoverMouthImmunityPatch.ClearLevelState();
            Creatures.CreatureDisablePatch.ClearLevelState();
            Creatures.CreatureSpeedPatch.ClearLevelState();
            Creatures.CreatureKnockbackPatch.ClearLevelState();
            Creatures.CreatureRagdollPatch.ClearLevelState();
            Creatures.BeetleDeaggroPatch.ClearLevelState();
            Creatures.ZombieAggroLogPatch.ClearLevelState();
            Creatures.CreatureAggroLog.ClearLevelState();
            Creatures.SpiderStrikeWarning.ClearLevelState();
            Creatures.CreatureKnockoutPatch.ClearLevelState();
            Creatures.SpiderStunIndicatorPatch.ClearLevelState();
            SporeBombCloudWarning.ClearLevelState();
            SporePresence.ClearLevelState();
        }

        /// <summary>
        /// The per-level setup, spread over frames behind the loading overlay. See the
        /// class remarks for why the presentation matters and why the work itself
        /// still has to finish before the player is let loose.
        ///
        /// <c>Busy</c> is cleared in a <c>finally</c> so a pass that throws (or a
        /// coroutine stopped by the level unloading underneath it) can never strand
        /// another mod's loading screen waiting on us - see
        /// <see cref="FairootsInterop"/>.
        /// </summary>
        private static IEnumerator RunSetup(Transform rootsSegment)
        {
            try
            {
                RootsLoadingOverlay.Show();

                // Fade in first, and don't start any heavy pass until the overlay has
                // actually been drawn at full alpha - the whole point is that the
                // stall happens with something on screen rather than behind a frozen
                // last frame of gameplay.
                while (!RootsLoadingOverlay.Fade(1f, OverlayFadeSpeed))
                {
                    yield return null;
                }

                yield return new WaitForEndOfFrame();

                foreach (var step in SetupSteps(rootsSegment))
                {
                    step();
                    yield return null;
                }

                while (!RootsLoadingOverlay.Fade(0f, OverlayFadeSpeed))
                {
                    yield return null;
                }
            }
            finally
            {
                RootsLoadingOverlay.Hide();
                RootsState.SetupFinished();
                _setup = null;
            }
        }

        /// <summary>Runs the same passes with no overlay and no frame breaks - the fallback path only.</summary>
        private static void RunPassesInline(Transform rootsSegment)
        {
            foreach (var step in SetupSteps(rootsSegment))
            {
                step();
            }
        }

        /// <summary>
        /// The per-level passes, in the order they have to run, as one step per frame.
        ///
        /// <b>Order is load-bearing in three places</b> and is unchanged from when
        /// these all ran in a single tick:
        /// <list type="number">
        /// <item>The config snapshot and the host-authority publish come first, so
        /// every pass after them reads one consistent set of values.</item>
        /// <item>The seeded spore-area removal runs before the spore-area kill switch,
        /// so the switch never claims an already-removed area into its restore
        /// registry (see <c>SporeAreaDisablePatch</c>).</item>
        /// <item><c>SporePresence.CaptureLevel</c> runs last, after everything that
        /// could have removed or deactivated an emitter.</item>
        /// </list>
        /// </summary>
        private static Action[] SetupSteps(Transform rootsSegment) => new Action[]
        {
            () =>
            {
                Plugin.Cfg.CaptureLevelSnapshot();
                Networking.HostAuthority.PublishAll();
                DetonationScreenshakeRegistry.Clear();
            },

            // By far the heaviest pass (400+ candidates plus every foliage mesh
            // vertex in the level), and the reason the overlay exists at all.
            () => SporeBombCullPatch.Run(rootsSegment),

            () => SporeAreas.SporeAreaCullPatch.Run(rootsSegment),
            () => SporeAreas.SporeAreaDisablePatch.Run(rootsSegment),
            () => SporeAreas.SporeAreaTuningPatch.Run(rootsSegment),
            () => SporeAreas.SporeCloudOpacityPatch.Run(rootsSegment),

            // Wind zones awake with the scene, which is before this watcher can
            // possibly have noticed the segment - and their Awake hook is gated on
            // the mod being active, so this pass is what actually applies the wind
            // tuning for the level.
            Wind.WindChillZoneTuningPatch.ReapplyAll,

            () => Creatures.CreatureDisablePatch.Run(rootsSegment),

            // After the disable pass, so a beetle it just deactivated isn't rescaled
            // on its way out. Beetles placed in the scene have already had Start() run
            // before the segment was detected, so - like the wind zones above - these
            // reapply passes are what actually tune this level's creatures.
            Creatures.CreatureSpeedPatch.ReapplyToAll,
            Creatures.CreatureKnockbackPatch.ReapplyToAll,
            Creatures.CreatureRagdollPatch.ReapplyToAll,

            // Spore recovery speed: the Awake hook covers characters that come up
            // while the mod is active, so this picks up the level-load snapshot
            // (apply-changes-live off), the host's freshly published value, and every
            // character that already existed outside the biome. Deliberately not
            // paired with a ClearLevelState - a Character outlives the biome segment,
            // and dropping its cached vanilla baseline while it's still alive would
            // compound the multiplier (see SporeDecayPatch's remarks).
            Spores.SporeDecayPatch.ReapplyToAll,

            // Last: caches this level's spore areas for the per-frame "is the player
            // in spores?" query, after every pass that could have removed or
            // deactivated one has already run.
            () =>
            {
                SporePresence.CaptureLevel(rootsSegment);

                if (Plugin.Cfg.EnableDebugLogging.Value && Plugin.Cfg.LogSceneScanOnLoad.Value)
                {
                    SceneDiagnostics.AutoDump("Roots Segment detected");
                }
            },
        };

        /// <summary>
        /// Runs one restore pass, keeping a throw in any single one of them from
        /// abandoning the rest - a half-restored transition would leave modded values
        /// on live objects in a biome the mod isn't supposed to touch, which is the
        /// exact failure this whole path exists to prevent.
        /// </summary>
        private static void Run(string what, Action pass)
        {
            try
            {
                pass();
            }
            catch (Exception e)
            {
                Diag.Error($"[RootsLevelWatcher] restoring {what} threw: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
