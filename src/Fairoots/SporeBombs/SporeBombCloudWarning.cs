using System;
using System.Collections.Generic;
using Fairoots.Core;
using Fairoots.Diagnostics;
using Fairoots.SporeAreas;
using UnityEngine;

namespace Fairoots.SporeBombs
{
    /// <summary>
    /// Holds the game's own "you are standing in spores" screen overlay up for as
    /// long as the local player is inside a spore bomb's cloud
    /// (<c>General/show-overlay-in-spore-bomb-clouds</c>).
    ///
    /// <b>The gap this fills.</b> The game has exactly one such warning -
    /// <c>GUIManager.instance.sporesWarning</c> - and only <c>StatusEmitter</c> ever
    /// raises it (<c>StatusEmitter.Update</c> calls <c>StartFX</c>/<c>EndFX</c> from
    /// its own <c>inZoneWarning</c> flag). A spore bomb's cloud is not a
    /// <c>StatusEmitter</c>; it's an <c>AOE</c> that re-explodes on a timer (see
    /// <see cref="SporeBombDetonationMarker"/>), so it gets the per-tick damage flash
    /// and nothing in between. That's the whole bug: the steady signal that says
    /// "still in it" simply has no code path for bombs.
    ///
    /// So this raises <em>the game's own</em> warning, through the game's own methods
    /// - the same reuse-the-native-mechanism approach as
    /// <c>CoverMouthImmunityPatch</c>. It inherits the exact look, fade timing and
    /// photosensitivity handling of the spore-area warning for free, and the per-tick
    /// damage flash (<c>sporesSVFX</c>) is a separate overlay layer that is left
    /// completely untouched, so it still spikes on top exactly as it does inside a
    /// spore area.
    ///
    /// <b>Presence is judged by the native falloff rule, not by <c>AOE.range</c></b> -
    /// see <see cref="SporeBombCloudPresence"/> for why that distinction is the
    /// difference between an overlay the player can trust and one that lies at the
    /// edges.
    /// </summary>
    internal static class SporeBombCloudWarning
    {
        /// <summary>
        /// Every live spore-bomb cloud, registered at detonation by
        /// <see cref="SporeBombExplosionPatch"/>. A registry rather than a per-frame
        /// scene scan: these objects are created and destroyed constantly, and
        /// <c>FindObjectsOfType&lt;AOE&gt;</c> every frame is exactly the kind of
        /// unconditional full-scene sweep that already cost this mod a mod-wide
        /// framerate drop once (see <c>RootsLevelWatcher</c>'s remarks). Entries are
        /// pruned in <see cref="Tick"/> as their objects are destroyed.
        /// </summary>
        private static readonly List<AOE> LiveClouds = new List<AOE>();

        /// <summary>
        /// Whether <em>this class</em> is the one currently holding the warning up.
        /// Tracked so it only ever ends an overlay it started - the same
        /// only-undo-your-own-work rule as <c>SporeAreaDisablePatch</c>'s restore
        /// registry.
        /// </summary>
        private static bool _showing;

        /// <summary>Registers a freshly-spawned detonation's spores-carrying AOE(s).</summary>
        internal static void Register(GameObject spawned)
        {
            foreach (var aoe in spawned.GetComponentsInChildren<AOE>(true))
            {
                if (aoe.statusType == CharacterAfflictions.STATUSTYPE.Spores && aoe.statusAmount > 0f)
                {
                    LiveClouds.Add(aoe);
                }
            }
        }

        /// <summary>
        /// Polled from <c>Plugin.Update</c>. Cheap by construction: a handful of live
        /// clouds at most, each costing one distance comparison.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                PruneDestroyed();
                Resolve(RootsState.Active && Plugin.Cfg.ShowOverlayInSporeBombClouds.Value && InBombCloud());
            }
            catch (Exception e)
            {
                Diag.Error($"[SporeBombCloudWarning] Tick threw: {e.GetType().Name}: {e.Message}");
                Resolve(false);
            }
        }

        /// <summary>
        /// Drops every tracked cloud and takes the overlay down with it - called on
        /// leaving the level, since a cloud that was never "exited" (the level
        /// unloaded around it) would otherwise leave the warning up in the main menu.
        /// </summary>
        internal static void ClearLevelState()
        {
            LiveClouds.Clear();
            Resolve(false);
        }

        private static void PruneDestroyed()
        {
            for (int i = LiveClouds.Count - 1; i >= 0; i--)
            {
                if (LiveClouds[i] == null)
                {
                    LiveClouds.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Whether the local player is standing in a live spore-bomb cloud, in the
        /// part of it that actually applies spores. Deliberately independent of this
        /// class's own setting: it is the shared presence fact (see
        /// <see cref="SporePresence"/>), which the text label reads too, while the
        /// overlay this class raises is gated on the setting by its caller.
        /// </summary>
        internal static bool InBombCloud()
        {
            if (LiveClouds.Count == 0)
            {
                return false;
            }

            var character = Character.localCharacter;
            if (character == null || character.data == null || character.data.dead)
            {
                return false;
            }

            // Covering your mouth against spore bombs (when that opt-in setting is on)
            // means the cloud can't reach you, so the warning shouldn't claim it can.
            // This matches what already happens in a spore area, where covering sets
            // the emitter's own wind-disable flag and the native code ends the FX -
            // see CoverMouthImmunityPatch.
            if (CoverMouthController.LocalCovering && Plugin.Cfg.EffectiveCoverMouthBlocksSporeBombs)
            {
                return false;
            }

            Vector3 center = character.Center;
            foreach (var aoe in LiveClouds)
            {
                // A cloud whose object has been deactivated isn't applying anything -
                // the spawned explosion carries a DisableIfWindActive, so this is the
                // wind dispersing it, the same case the spore areas already handle.
                if (!aoe.isActiveAndEnabled)
                {
                    continue;
                }

                if (SporeBombCloudPresence.IsInsideStatusRange(
                        Vector3.Distance(center, aoe.transform.position), aoe.range, aoe.minFactor, aoe.factorPow))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Resolve(bool inside)
        {
            if (inside == _showing)
            {
                return;
            }

            var warning = GUIManager.instance != null ? GUIManager.instance.sporesWarning : null;
            if (warning == null)
            {
                return;
            }

            if (inside)
            {
                _showing = true;

                // Already up because the player is standing in a spore area too:
                // StartFX resets the intensity to 0 and tweens back up, so calling it
                // on an overlay that's already showing would read as a dip - the
                // opposite of the steady signal this setting is for.
                if (SporePresence.InSporeArea())
                {
                    Diag.V("[SporeBombCloudWarning] entered a spore bomb's cloud inside a spore area - the warning is already up");
                    return;
                }

                warning.StartFX();
                Diag.V("[SporeBombCloudWarning] entered a spore bomb's cloud - holding the spores warning up");
                return;
            }

            _showing = false;

            // Only lower the warning if nothing else is entitled to keep it up. A
            // spore area raises the same single overlay from its own StatusEmitter,
            // and that emitter only calls StartFX on the frame the player *enters* -
            // so ending the FX here while standing inside one would blank the warning
            // for as long as the player stayed put, with nothing to re-raise it.
            if (SporePresence.InSporeArea())
            {
                Diag.V("[SporeBombCloudWarning] left a spore bomb's cloud inside a spore area - leaving the warning to the area");
                return;
            }

            warning.EndFX();
            Diag.V("[SporeBombCloudWarning] left a spore bomb's cloud - lowering the spores warning");
        }
    }
}
