using System;
using Fairoots.Core;
using Fairoots.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace Fairoots.Wind
{
    /// <summary>
    /// Phase 5 (ROADMAP.md "Wind-induced fall camera spin dampening" row).
    /// Per the maintainer's scoping decision (2026-07-22): dampen the camera only
    /// for falls preceded by recent wind force, not every Roots fall - an
    /// ordinary fall is generally the player's own doing, but wind blowing you
    /// off a ledge mid-jump is close to pure bad luck, so only that specific case
    /// gets the assist (a chance to grab a wall or use a Rescue Hook instead of
    /// being helplessly spun by uncontrolled ragdoll-head physics - RESEARCH.md Q6).
    ///
    /// Two cooperating patches, both scoped to <see cref="Character.localCharacter"/>
    /// only - a character's ragdoll control is driven by whoever owns it, so every
    /// client applying this to its own character is exactly the full coverage (and
    /// why both settings still have to be host-authoritative: the values must match
    /// across the lobby, even though each client applies them itself). See
    /// RESEARCH.md Q6's remarks on why the underlying mechanism is fall-general, not
    /// wind-specific code:
    /// 1. A postfix on <c>WindChillZone.AddWindForceToCharacter</c> records
    ///    "when did wind force last actually apply to me" - re-deriving the same
    ///    two escape checks (<c>photonView.IsMine</c>, actively gripping a climb
    ///    handle) the original method itself uses, so a postfix that always fires
    ///    (even when the original early-returned without applying any force)
    ///    doesn't record a false timestamp.
    /// 2. A postfix on <c>CharacterData.GetTargetRagdollControll()</c> - the exact
    ///    method RESEARCH.md Q6 identified as the source of the unconditional
    ///    "0 the instant fallSeconds &gt; 0" result - raises that floor whenever the
    ///    recorded timestamp is still within the configured window, either all the
    ///    way to full control (<c>Wind/prevent-wind-ragdoll</c>, on by default under
    ///    every preset - <see cref="WindTuning.ApplyWindRagdollImmunity"/>) or
    ///    partway (<c>Wind/fall-camera-dampen-clamp</c> -
    ///    <see cref="WindTuning.ApplyFallCameraDampening"/>).
    /// </summary>
    [HarmonyPatch(typeof(WindChillZone), "AddWindForceToCharacter")]
    internal static class WindRecentForceTrackerPatch
    {
        private static float _lastWindForceTime = -1f;

        internal static float LastWindForceTime => _lastWindForceTime;

        private static void Postfix(Character character)
        {
            if (!RootsState.Active || Plugin.Cfg.EffectiveDisableWindEntirely)
            {
                return;
            }

            if (character == null || character != Character.localCharacter)
            {
                return;
            }

            // Mirrors WindChillZone.AddWindForceToCharacter's own early-return
            // conditions - a postfix runs even when the original bailed out
            // without applying any force, so re-check them here rather than
            // record a timestamp for force that never actually happened.
            if (!character.photonView.IsMine || character.data.currentClimbHandle != null)
            {
                return;
            }

            // Same reasoning for Fairoots' own suppression: a postfix still runs
            // when ClimbWindShelterPatch's prefix skipped the original, and a
            // climber who was sheltered from the push was never pushed, so a fall
            // right after letting go isn't wind-caused.
            if (ClimbWindShelter.Enabled && ClimbWindShelter.IsHoldingOn(character))
            {
                return;
            }

            _lastWindForceTime = Time.time;
        }
    }

    [HarmonyPatch(typeof(CharacterData), nameof(CharacterData.GetTargetRagdollControll))]
    internal static class WindFallCameraDampingPatch
    {
        private static void Postfix(CharacterData __instance, ref float __result)
        {
            try
            {
                // GetTargetRagdollControll governs every fall in the game, so this
                // gate is what keeps a Roots-only assist out of every other biome.
                if (!RootsState.Active || Plugin.Cfg.EffectiveDisableWindEntirely)
                {
                    return;
                }

                if (Character.localCharacter == null || __instance != Character.localCharacter.data)
                {
                    return;
                }

                if (__instance.fallSeconds <= 0f)
                {
                    return; // not the unconditional-0 fall branch this feature targets.
                }

                float windowSeconds = Plugin.Cfg.EffectiveWindRecentForceWindowSeconds;
                bool fallIsWindPreceded = WindTuning.IsWindForceStillRecent(
                    WindRecentForceTrackerPatch.LastWindForceTime, Time.time, windowSeconds);

                // Both mechanics key off the same "did wind cause this fall" test and
                // both only ever raise the floor, so they can just be applied in
                // sequence - with prevent-wind-ragdoll on, its full-control result
                // already dominates whatever clamp the preset asked for.
                __result = WindTuning.ApplyWindRagdollImmunity(
                    __result, fallIsWindPreceded, Plugin.Cfg.EffectivePreventWindRagdoll);

                float clamp = (float)Plugin.Cfg.EffectiveWindFallCameraDampenClamp;
                __result = WindTuning.ApplyFallCameraDampening(__result, fallIsWindPreceded, clamp);
            }
            catch (Exception e)
            {
                Diag.Error($"[WindFallCameraDamping] threw: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
