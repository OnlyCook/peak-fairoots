using System.Collections.Generic;
using Fairoots.SporeAreas;
using Fairoots.SporeBombs;
using UnityEngine;

namespace Fairoots
{
    /// <summary>
    /// The one answer to "is the local player standing in spores right now?", across
    /// both hazards - the biome's persistent spore areas and the temporary cloud a
    /// spore bomb leaves. Shared by everything that has to show the player that fact:
    /// <see cref="SporeBombCloudWarning"/> (the screen overlay) and
    /// <c>Ui/SporeWarningLabel</c> (the text label). One copy, for the same reason
    /// <see cref="SporeAreaScan"/> is one copy: two slightly different answers would
    /// mean the overlay and the label disagreeing about whether the player is in
    /// danger, which is worse than either being wrong on its own.
    ///
    /// Game-facing (Unity types throughout), so deliberately outside <c>Core/</c> -
    /// the geometry rule it applies to bomb clouds lives in
    /// <c>Core/SporeBombCloudPresence</c>.
    ///
    /// <b>The spore-area list is captured per level, not searched per frame.</b>
    /// These callers ask every frame, and <c>FindObjectsOfType&lt;StatusEmitter&gt;</c>
    /// allocates across every loaded scene each time - the exact unconditional
    /// full-scene sweep that already cost this mod a mod-wide framerate drop once
    /// (see <see cref="RootsLevelWatcher"/>'s remarks). Emitters that get deactivated
    /// later by the removal/disable passes stay in the list and are skipped by the
    /// <c>isActiveAndEnabled</c> check, so it needs no invalidation when those run.
    /// </summary>
    internal static class SporePresence
    {
        private static readonly List<StatusEmitter> LevelAreas = new List<StatusEmitter>();

        /// <summary>Captures this level's spore areas, called once per Roots load from <see cref="RootsLevelWatcher"/>.</summary>
        internal static void CaptureLevel(Transform rootsSegment)
        {
            LevelAreas.Clear();
            LevelAreas.AddRange(SporeAreaScan.FilterSporeAreas(rootsSegment.GetComponentsInChildren<StatusEmitter>(true)));
        }

        internal static void ClearLevelState() => LevelAreas.Clear();

        /// <summary>Whether the player is in spores from either hazard - what a warning should key off.</summary>
        internal static bool InAnySpores() => InSporeArea() || SporeBombCloudWarning.InBombCloud();

        /// <summary>
        /// Whether a persistent spore area currently has the local player in range,
        /// using the same test the native emitter uses on itself
        /// (<c>StatusEmitter.InRange</c>: centre distance against
        /// <c>radius + outerFade</c>).
        ///
        /// <c>emitterDisabledByWind</c> is honoured because it is the game's own "this
        /// emitter is not applying anything right now" flag - set by wind dispersal,
        /// and reused by <see cref="CoverMouthImmunityPatch"/> for a covered mouth. So
        /// both of those correctly stop a warning claiming the player is being spored
        /// when they aren't, with no special case for either.
        /// </summary>
        internal static bool InSporeArea()
        {
            var character = Character.localCharacter;
            if (character == null || LevelAreas.Count == 0)
            {
                return false;
            }

            Vector3 center = character.Center;
            for (int i = 0; i < LevelAreas.Count; i++)
            {
                var emitter = LevelAreas[i];
                if (emitter == null || !emitter.isActiveAndEnabled || emitter.emitterDisabledByWind || emitter.amount <= 0f)
                {
                    continue;
                }

                if (Vector3.Distance(center, emitter.transform.position) < emitter.radius + emitter.outerFade)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
