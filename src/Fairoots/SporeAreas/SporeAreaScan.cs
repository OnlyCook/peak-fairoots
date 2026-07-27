using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fairoots.SporeAreas
{
    /// <summary>
    /// Shared "what is a spore area, and which GameObject <em>is</em> it" logic for
    /// everything under <c>SporeAreas/</c>. Kept in one place because every
    /// spore-area mechanic (disabling them, thinning them, resizing them) has to
    /// agree exactly on which objects it's looking at - two slightly different
    /// identity checks would mean e.g. the removal pass and the disable pass
    /// disagreeing about how many spore areas the level has.
    ///
    /// Game-facing (Unity types throughout), so deliberately outside <c>Core/</c> -
    /// the actual decisions live in <c>Core/SporeAreaCull.cs</c>.
    /// </summary>
    internal static class SporeAreaScan
    {
        /// <summary>
        /// How far up the hierarchy <see cref="ResolveAreaRoot"/> is willing to walk
        /// from the emitter component to find the object that owns the whole spore
        /// area (mushroom mesh + cloud VFX + emitter). Small on purpose: walking too
        /// far would eventually hit the <c>PropSpawner</c> group holding *every*
        /// spore area in the level. The structural guards below stop that on their
        /// own; this is just a second backstop.
        /// </summary>
        private const int MaxParentWalk = 3;

        /// <summary>
        /// A spore area is identified by what the emitter actually does - applying
        /// the <c>Spores</c> status - not by a name: there is no spore-area class
        /// and the objects are all called the same thing
        /// ("Mushroom tree Spore Cloud", runtime-confirmed). <c>amount &gt; 0</c>
        /// excludes the mirror-image case of an emitter that *subtracts* spores
        /// (the same component serves both directions - see
        /// <c>StatusEmitter.Update</c>, which calls <c>SubtractStatus</c> for a
        /// negative amount), which would be a cure, not a hazard.
        /// </summary>
        internal static bool IsSporeArea(StatusEmitter emitter) =>
            emitter != null
            && emitter.statusType == CharacterAfflictions.STATUSTYPE.Spores
            && emitter.amount > 0f;

        /// <summary>
        /// True for the short-lived spore area a detonating spore bomb spawns, which
        /// every mechanic in this folder deliberately leaves alone (a spore bomb's
        /// mini spore area is tuned by the <c>Spore-Bombs</c> settings instead). It
        /// hangs under the spawned explosion object, so both the spore-bomb name
        /// check and the explosion object itself are reachable by walking up.
        /// Level-load scans can't see these anyway (they don't exist until
        /// detonation), but the scene-wide reapply passes run while a detonation may
        /// be live, so this is checked explicitly rather than left to timing.
        /// </summary>
        internal static bool IsSporeBombSpawned(Transform t)
        {
            for (var cur = t; cur != null; cur = cur.parent)
            {
                if (SporeBombs.SporeBombCullPatch.ClassifySporeBomb(cur.name)
                    || cur.name.IndexOf("Explosion", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                if (cur.name == "Roots Segment")
                {
                    break;
                }
            }

            return false;
        }

        /// <summary>
        /// The object that <em>is</em> this spore area: the highest ancestor still
        /// representing this one area, so hiding or removing it takes the emitter
        /// mushroom in the middle of the cloud and the cloud VFX with it rather than
        /// leaving them floating beside an inert emitter.
        ///
        /// The walk stops before any ancestor that owns more than this single spore
        /// area (a second <c>StatusEmitter</c> in its subtree), any
        /// level-generation grouping node (<c>PropSpawner</c>/<c>PropGrouper</c>/
        /// <c>Biome</c> - the <c>*Shrooms</c>-style group parents hold every
        /// instance of a prop type at once), and the Roots Segment itself.
        ///
        /// In practice (confirmed in-game) this resolves to the emitter's own
        /// object, named "Mushroom tree Spore Cloud", whose parent is the scenery
        /// mushroom tree it grows on (<c>Evil Shroom</c> / <c>Mush Trees (spores)</c>)
        /// - which correctly stays put.
        /// </summary>
        internal static GameObject ResolveAreaRoot(StatusEmitter emitter)
        {
            Transform best = emitter.transform;

            for (int step = 0; step < MaxParentWalk; step++)
            {
                Transform parent = best.parent;
                if (parent == null || parent.name == "Roots Segment" || IsGroupingNode(parent))
                {
                    break;
                }

                if (parent.GetComponentsInChildren<StatusEmitter>(true).Length > 1)
                {
                    break;
                }

                best = parent;
            }

            return best.gameObject;
        }

        private static bool IsGroupingNode(Transform t) =>
            t.GetComponent<PropSpawner>() != null
            || t.GetComponent<PropGrouper>() != null
            || t.GetComponent<Biome>() != null;

        /// <summary>
        /// Filters a raw <c>StatusEmitter</c> sweep down to real spore areas, in a
        /// stable order. Order matters for nothing seeded (every decision keys off
        /// world position, never iteration order - CLAUDE.md), but it keeps logs
        /// comparable between runs.
        /// </summary>
        internal static List<StatusEmitter> FilterSporeAreas(IReadOnlyList<StatusEmitter> emitters)
        {
            var result = new List<StatusEmitter>();
            for (int i = 0; i < emitters.Count; i++)
            {
                var e = emitters[i];
                if (IsSporeArea(e) && !IsSporeBombSpawned(e.transform))
                {
                    result.Add(e);
                }
            }

            return result;
        }

        /// <summary>Ancestor chain up to the Roots Segment, for verbose logs - the emitters have no distinctive names, so the path is what identifies one.</summary>
        internal static string DescribePath(Transform t)
        {
            var chain = new List<string>();
            for (var cur = t; cur != null; cur = cur.parent)
            {
                chain.Add(cur.name);
                if (cur.name == "Roots Segment")
                {
                    break;
                }
            }

            return string.Join(" < ", chain);
        }
    }
}
