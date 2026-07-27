using System.Collections.Generic;
using Fairoots.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace Fairoots.SporeAreas
{
    /// <summary>
    /// What covering your mouth actually buys: no Spores status (and no green screen
    /// warning) from a persistent spore area while your hands are over your mouth.
    ///
    /// <b>Mechanism: the game's own gate, not a new one.</b> <c>StatusEmitter</c>
    /// already has an <c>emitterDisabledByWind</c> flag that suppresses both the
    /// status tick and the in-zone screen FX (wind natively disperses spore areas -
    /// every spore emitter in Roots is a <c>WindAffectedStatusEmitter</c>). Setting
    /// that same flag while covering means this mechanic behaves exactly like the
    /// dispersal the player has already seen, including ending the screen filter, for
    /// free - rather than re-implementing status suppression and then having to
    /// re-implement the FX handling that goes with it.
    ///
    /// Two patches for the flag, because it needs different handling per subclass:
    /// <list type="bullet">
    /// <item><see cref="WindAffectedFixedUpdatePostfix"/> - the wind-affected subclass
    /// rewrites the flag from <c>windActive</c> every <c>FixedUpdate</c>, so ORing our
    /// state in right after needs no bookkeeping at all: the game itself clears it
    /// the moment we stop asking.</item>
    /// <item><see cref="UpdatePrefix"/> - a plain <c>StatusEmitter</c> (none exist for
    /// spores in Roots today, but the mod shouldn't quietly stop working if one ever
    /// does) has nothing rewriting the flag, so those are tracked and restored
    /// explicitly.</item>
    /// </list>
    ///
    /// Plus the anti-exploit half - see <see cref="SavedTickProgress"/>.
    ///
    /// Purely local, and safe to be: <c>StatusEmitter.Update</c> only ever applies
    /// status to <c>Character.localCharacter</c>, so suppressing an emitter on this
    /// client cannot affect what anyone else's client does to their own character.
    /// Scoped to real spore areas (<see cref="SporeAreaScan.IsSporeArea"/>), so
    /// covering your mouth does nothing against heat, cold, or a spore bomb's
    /// detonation.
    /// </summary>
    [HarmonyPatch]
    internal static class CoverMouthImmunityPatch
    {
        /// <summary>
        /// Plain (non-wind-affected) emitters this patch has forced off, so they can
        /// be restored exactly once when covering ends - see the class remarks.
        /// </summary>
        private static readonly HashSet<int> ForcedPlainEmitters = new HashSet<int>();

        /// <summary>Spore-bomb-spawned emitters already reported once - see <see cref="IsFromSporeBomb"/>.</summary>
        private static readonly HashSet<int> _loggedBombEmitters = new HashSet<int>();

        /// <summary>
        /// Each suppressed emitter's <c>timeSinceLastTick</c>, preserved across the
        /// cover so releasing the key resumes the tick that was already in progress
        /// instead of restarting it. <b>This is what stops the mechanic from being
        /// free</b> (exploit reported from live play, 2026-07-27: tapping the key on a
        /// ~300ms cycle gave near-total spore immunity for a fraction of the stamina).
        ///
        /// The leak is in vanilla's own re-entry path, not in the suppression: when a
        /// zone-warning emitter goes from "not in zone" back to "in zone" it sets
        /// <c>timeSinceLastTick = -extraWarningTime</c> (-1s), i.e. a fresh 1.5s grace
        /// before the next 0.5s tick can land. Our suppression makes the emitter
        /// believe the player left the zone, so <em>every</em> release re-triggered
        /// that grace - and a player who re-covered inside it never took a single
        /// tick, while paying stamina only for the moments the key was actually down.
        ///
        /// The fix (the maintainer's own specification): progress toward the next tick
        /// is <em>paused</em> by covering, never reset. It accumulates whenever the
        /// mouth is uncovered, is saved while it's covered, and is restored the moment
        /// the cover ends - so a player who was 90% of the way to a tick gets spores
        /// almost immediately on releasing, and tapping the key buys exactly the time
        /// it was held and nothing more. Cleared when the player actually leaves the
        /// area (see <see cref="IsPlayerInRange"/>), so walking out and back in is a
        /// genuine reset rather than a stored debt.
        /// </summary>
        private static readonly Dictionary<int, float> SavedTickProgress = new Dictionary<int, float>();

        /// <summary><c>StatusEmitter.timeSinceLastTick</c> is private; this is the fast (non-reflective per access) ref accessor.</summary>
        private static readonly AccessTools.FieldRef<StatusEmitter, float> TickProgressRef =
            AccessTools.FieldRefAccess<StatusEmitter, float>("timeSinceLastTick");

        /// <summary>
        /// How many spore areas currently hold parked tick progress - logged on
        /// release so "did the anti-exploit half actually engage" is answerable from a
        /// log rather than inferred from how fast a meter moved. 0 at the moment of
        /// release means nothing was parked and the reset will go unopposed.
        /// </summary>
        internal static int ParkedCount => SavedTickProgress.Count;

        /// <summary>
        /// Drops the parked tick progress and the forced-flag bookkeeping - called
        /// when the Roots level is torn down. Entries normally clean themselves up on
        /// the emitter's next uncovered frame; this catches the case where an emitter
        /// is destroyed while still covered, so nothing keyed by a dead instance ID
        /// survives into the next level.
        /// </summary>
        internal static void ClearLevelState()
        {
            SavedTickProgress.Clear();
            ForcedPlainEmitters.Clear();
            _loggedBombEmitters.Clear();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(WindAffectedStatusEmitter), "FixedUpdate")]
        private static void WindAffectedFixedUpdatePostfix(WindAffectedStatusEmitter __instance)
        {
            if (ShouldSuppress(__instance))
            {
                __instance.emitterDisabledByWind = true;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(StatusEmitter), "Update")]
        private static void UpdatePrefix(StatusEmitter __instance)
        {
            bool suppress = ShouldSuppress(__instance);
            int id = __instance.GetInstanceID();

            if (suppress)
            {
                // A plain emitter has nothing else writing this flag, so force it here
                // (the wind-affected subclass is handled in its own FixedUpdate above,
                // where the game re-writes the flag every tick anyway).
                if (!(__instance is WindAffectedStatusEmitter))
                {
                    __instance.emitterDisabledByWind = true;
                    ForcedPlainEmitters.Add(id);
                }

                // Park the in-progress tick, but only for the level's own spore areas.
                //
                // A spore bomb's lingering cloud is deliberately excluded (live-reported
                // 2026-07-27): parking exists to stop tap-spamming the key from buying
                // near-free immunity in an area you chose to stand in, and it works by
                // delivering the withheld tick the moment the cover drops. Applied to a
                // bomb, that reads as a bug - the player covered their mouth *before* the
                // blast, took nothing, and then got spores anyway on releasing, which is
                // precisely the opposite of what the opt-in setting promises. A bomb's
                // cloud is a one-off you either blocked or didn't.
                if (!IsFromSporeBomb(__instance) && IsPlayerInRange(__instance))
                {
                    SavedTickProgress[id] = TickProgressRef(__instance);
                }
                else
                {
                    // Left the area while covering: drop the parked progress so coming
                    // back doesn't inherit it.
                    SavedTickProgress.Remove(id);
                }

                return;
            }

            if (!(__instance is WindAffectedStatusEmitter) && ForcedPlainEmitters.Remove(id))
            {
                __instance.emitterDisabledByWind = false;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(StatusEmitter), "Update")]
        private static void UpdatePostfix(StatusEmitter __instance)
        {
            if (ShouldSuppress(__instance))
            {
                return;
            }

            int id = __instance.GetInstanceID();
            if (!SavedTickProgress.TryGetValue(id, out float parked))
            {
                return;
            }

            // Wait for the emitter to actually be running again before consuming the
            // parked value. This is load-bearing, and getting it wrong is what made
            // the first version of this fix do nothing at all (live-reported
            // 2026-07-27): the flag is written by WindAffectedStatusEmitter's
            // FixedUpdate at 50Hz while this runs every frame, so on the frame the key
            // is released the emitter is still gated - vanilla returns early, there is
            // no reset to repair yet, and consuming the parked progress here left the
            // *next* frame's -extraWarningTime write (the actual reset) unopposed.
            // Also covers the honest case of a real wind gust still holding the emitter
            // off after the player uncovers.
            if (__instance.emitterDisabledByWind)
            {
                return;
            }

            // Left the area while the progress was parked - drop it rather than
            // applying a stale debt on re-entry (the same rule as the prefix's).
            if (!IsPlayerInRange(__instance))
            {
                SavedTickProgress.Remove(id);
                return;
            }

            SavedTickProgress.Remove(id);

            // Restored *after* the original method, specifically to overwrite the
            // -extraWarningTime the re-entry branch just wrote (see the field's
            // remarks). Only ever restores a value the player had already earned, and
            // only upward: if vanilla has somehow got further along than we parked
            // (nothing does today, but a future emitter change might), leave it.
            if (TickProgressRef(__instance) < parked)
            {
                TickProgressRef(__instance) = parked;
                Diag.V($"[CoverMouth] resumed spore tick progress at {parked:0.###}s on \"{__instance.name}\" (uncovered)");
            }
        }

        /// <summary>
        /// Whether the local player is inside this emitter, using the same test
        /// vanilla's own (protected) <c>InRange</c> does - distance from the
        /// character's centre against <c>radius + outerFade</c>. Reimplemented rather
        /// than reflected into because it's one distance comparison and this runs per
        /// emitter per frame while covering.
        /// </summary>
        private static bool IsPlayerInRange(StatusEmitter emitter)
        {
            var character = Character.localCharacter;
            if (character == null)
            {
                return false;
            }

            return Vector3.Distance(character.Center, emitter.transform.position)
                   < emitter.radius + emitter.outerFade;
        }

        /// <summary>
        /// Whether this emitter should be gated off right now. A spore bomb's own
        /// lingering cloud counts only when the opt-in
        /// <c>Spore-Bombs/cover-mouth-blocks-spore-bombs</c> setting is on - otherwise
        /// the mechanic stays scoped to the biome's persistent spore areas.
        /// </summary>
        private static bool ShouldSuppress(StatusEmitter emitter)
        {
            if (!CoverMouthController.LocalCovering || !SporeAreaScan.IsSporeArea(emitter))
            {
                return false;
            }

            if (!IsFromSporeBomb(emitter))
            {
                return true;
            }

            return Plugin.Cfg.EffectiveCoverMouthBlocksSporeBombs;
        }

        /// <summary>
        /// Whether an emitter belongs to a spore bomb's detonation rather than to the
        /// level. Logs the first one it ever sees, because whether spore bombs leave a
        /// lingering <c>StatusEmitter</c> behind at all (as opposed to applying their
        /// spores once through the spawned <c>AOE</c>) decides which of the two
        /// cover-mouth spore-bomb paths is actually doing the work - and the answer isn't
        /// in the decompiled code, since it's prefab data.
        /// </summary>
        private static bool IsFromSporeBomb(StatusEmitter emitter)
        {
            bool fromBomb = SporeAreaScan.IsSporeBombSpawned(emitter.transform);
            if (fromBomb && _loggedBombEmitters.Add(emitter.GetInstanceID()))
            {
                Diag.V(
                    $"[CoverMouth] spore-bomb detonation left a lingering spore emitter: " +
                    $"\"{SporeAreaScan.DescribePath(emitter.transform)}\" (radius={emitter.radius}, amount={emitter.amount})");
            }

            return fromBomb;
        }
    }
}
