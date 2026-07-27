using System;
using System.Collections.Generic;
using Fairoots.Diagnostics;
using UnityEngine;

namespace Fairoots.SporeAreas
{
    /// <summary>
    /// Phase 6 (ROADMAP.md), first mechanic: the <c>Spore-Areas/disable-spore-areas</c>
    /// master switch. Removes the Roots biome's persistent spore areas (the game's
    /// "Mushroom Spore Clouds") outright - status ticks, screen filter, the emitter
    /// mushroom in the middle and the cloud VFX all go with them.
    ///
    /// Identity (runtime-confirmed, see the roots-runtime-findings memory and
    /// RESEARCH.md Q9): a spore area is a <c>StatusEmitter</c> whose
    /// <c>statusType</c> is <c>Spores</c> - in Roots always the
    /// <c>WindAffectedStatusEmitter</c> subclass, with <c>radius=16</c>,
    /// <c>innerFade=8</c>, <c>amount=0.025</c>. There is no dedicated spore-area
    /// class or name to match on, which is why the component's own status type is
    /// the identity check here rather than a name substring (the way spore bombs
    /// are matched).
    ///
    /// Not a Harmony patch despite the folder's naming convention, for the same
    /// reason <c>SporeBombRecolorPatch</c> isn't: the emitters are baked into the
    /// Roots scene at author time, so there's no runtime placement call to hook.
    /// Driven once per level from <see cref="RootsLevelWatcher"/>, plus a
    /// scene-wide <see cref="ReapplyToAll"/> whenever the setting changes.
    ///
    /// <b>Deliberately excludes a spore bomb's own temporary spore area.</b> Those
    /// are spawned by <c>SpawnGameObject</c> at detonation time rather than baked
    /// into the scene, so a level-load scan can't see them anyway - but
    /// <see cref="ReapplyToAll"/> runs while a detonation may be live, so
    /// <see cref="IsSporeBombSpawned"/> filters them out explicitly instead of
    /// relying on timing.
    /// </summary>
    internal static class SporeAreaDisablePatch
    {
        /// <summary>
        /// How far up the hierarchy <see cref="ResolveAreaRoot"/> is willing to walk
        /// from the emitter component to find the object that owns the whole spore
        /// area (mushroom mesh + cloud VFX + emitter). Small on purpose: walking too
        /// far would eventually hit the <c>PropSpawner</c> group holding *every*
        /// spore area in the level and deactivate all of them at once. The
        /// structural guards in <see cref="ResolveAreaRoot"/> stop that on their
        /// own; this is just a second backstop.
        /// </summary>
        private const int MaxParentWalk = 3;

        /// <summary>
        /// Every GameObject this session deactivated, so turning the setting back
        /// off restores exactly what Fairoots hid and nothing else - re-activating
        /// whatever happens to be inactive around a spore emitter would also undo
        /// the game's own deactivations (e.g. <c>DisableBasedOnRunSettings</c>).
        /// Keyed by <see cref="UnityEngine.Object.GetInstanceID"/>; entries whose
        /// object has since been destroyed are skipped and dropped on the next pass.
        /// </summary>
        private static readonly Dictionary<int, GameObject> Deactivated = new Dictionary<int, GameObject>();

        /// <summary>
        /// Applies the current setting to every spore area under a freshly-loaded
        /// Roots Segment. Called once per level load; a no-op (beyond a log line)
        /// while the setting is off and nothing has been hidden yet.
        /// </summary>
        internal static void Run(Transform rootsSegment)
        {
            try
            {
                Apply(rootsSegment.GetComponentsInChildren<StatusEmitter>(true), "level load");
            }
            catch (Exception e)
            {
                Diag.Error($"[SporeAreas] Run threw: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// Re-resolves every spore area <em>anywhere in the loaded scene</em>
        /// against the current setting, in both directions (hide when it's turned
        /// on, restore when it's turned off) - wired to the setting's own
        /// <c>SettingChanged</c> and to <c>HostAuthoritySync</c>'s room-property
        /// update, so a client whose level load raced ahead of the host's first
        /// publish still ends up matching the host. Scene-wide rather than
        /// Roots-Segment-scoped because a hidden emitter's own segment lookup would
        /// need the segment to still be around, and this also has to work the
        /// moment the player flips the toggle from anywhere.
        /// </summary>
        internal static void ReapplyToAll()
        {
            if (Plugin.Cfg == null)
            {
                return;
            }

            try
            {
                // includeInactive: true is load-bearing - the emitters this pass
                // has to be able to find again are precisely the ones sitting on
                // GameObjects it deactivated itself.
                Apply(UnityEngine.Object.FindObjectsOfType<StatusEmitter>(true), "config change");
            }
            catch (Exception e)
            {
                Diag.Error($"[SporeAreas] ReapplyToAll threw: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>Drops the restore registry - called when the Roots level is torn down.</summary>
        internal static void ClearLevelState() => Deactivated.Clear();

        private static void Apply(IReadOnlyList<StatusEmitter> emitters, string reason)
        {
            bool disable = Plugin.Cfg.EffectiveDisableSporeAreas;

            int found = 0, hidden = 0, restored = 0;
            for (int i = 0; i < emitters.Count; i++)
            {
                var emitter = emitters[i];
                if (emitter == null || !IsSporeArea(emitter) || IsSporeBombSpawned(emitter.transform))
                {
                    continue;
                }

                found++;
                GameObject root = ResolveAreaRoot(emitter);
                int id = root.GetInstanceID();

                if (disable)
                {
                    if (!root.activeSelf)
                    {
                        // Either already ours, or the game deactivated it for its
                        // own reasons - leave it alone and don't claim it, or
                        // turning the setting off would activate something vanilla
                        // wanted hidden.
                        continue;
                    }

                    root.SetActive(false);
                    Deactivated[id] = root;
                    hidden++;
                    Diag.V($"[SporeAreas]   disabled \"{DescribePath(root.transform)}\" (radius={emitter.radius}, amount={emitter.amount})");
                }
                else if (Deactivated.TryGetValue(id, out GameObject ours))
                {
                    Deactivated.Remove(id);
                    if (ours != null)
                    {
                        ours.SetActive(true);
                        restored++;
                    }
                }
            }

            Diag.Info(
                $"[SporeAreas] {reason}: disable-spore-areas={(disable ? "ON" : "off")}, " +
                $"{found} spore area(s) found, {hidden} newly hidden, {restored} restored");
        }

        /// <summary>
        /// A spore area is identified by what the emitter actually does - applying
        /// the <c>Spores</c> status - not by a name. <c>amount &gt; 0</c> excludes
        /// the mirror-image case of an emitter that *subtracts* spores (the same
        /// component type is used for both directions - see
        /// <c>StatusEmitter.Update</c>, which calls <c>SubtractStatus</c> for a
        /// negative amount), which would be a cure, not a hazard.
        /// </summary>
        private static bool IsSporeArea(StatusEmitter emitter) =>
            emitter.statusType == CharacterAfflictions.STATUSTYPE.Spores && emitter.amount > 0f;

        /// <summary>
        /// True for the short-lived spore area a detonating spore bomb spawns,
        /// which this feature deliberately leaves alone (see class remarks) - it
        /// hangs under the spawned explosion object, so the spore-bomb name check
        /// and the explosion object itself are both reachable by walking up.
        /// </summary>
        private static bool IsSporeBombSpawned(Transform t)
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
        /// The object to deactivate: the highest ancestor that still represents
        /// this one spore area, so the emitter mushroom in the middle of the cloud
        /// and the cloud VFX go with it rather than being left floating in mid-air
        /// with an invisible, inert emitter beside them.
        ///
        /// The walk stops before any ancestor that owns more than this single spore
        /// area (another <c>StatusEmitter</c> in its subtree), any level-generation
        /// grouping node (<c>PropSpawner</c>/<c>PropGrouper</c>/<c>Biome</c> - the
        /// <c>*Shrooms</c>-style group parents confirmed in the runtime scan hold
        /// every instance of a prop type at once), and the Roots Segment itself.
        /// If the emitter is its own root - no shared parent to claim - that's what
        /// gets deactivated, which is still correct whenever the mesh and VFX are
        /// children of the emitter object rather than its siblings.
        /// </summary>
        private static GameObject ResolveAreaRoot(StatusEmitter emitter)
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

        /// <summary>Ancestor chain up to the Roots Segment, for the verbose log - the emitters have no distinctive names, so the path is what identifies them.</summary>
        private static string DescribePath(Transform t)
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
