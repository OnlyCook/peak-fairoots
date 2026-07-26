using System;
using Fairoots.Core;
using Fairoots.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace Fairoots.SporeBombs
{
    /// <summary>
    /// Second half of the spore-bomb screen-shake distance cap (the first being the
    /// spawn-time pass in <see cref="SporeBombExplosionPatch"/>).
    ///
    /// <c>AddScreenshake.Shake()</c> is what actually asks
    /// <c>GamefeelHandler</c> for the shake, and it branches on the component's
    /// <c>positional</c> flag: positional shakes attenuate to nothing at
    /// <c>range</c> meters, non-positional ones call the *global*
    /// <c>AddPerlinShake</c>, which shakes every player's camera at full strength
    /// no matter where they are. Capping <c>range</c> on a non-positional shake
    /// therefore does exactly nothing - which is why a detonation on the far side
    /// of the map still shook everyone.
    ///
    /// This prefix catches every shake fired within the space/time window of a
    /// recorded detonation (see <see cref="DetonationScreenshakeRegistry"/>) - both
    /// the ones the spawn-time pass already tuned and the ones on explosion orbs
    /// that didn't exist yet then - and makes them positional with the configured
    /// range. Anything outside that window (every other shake in the game: falls,
    /// rockfalls, items, creatures) is left completely untouched.
    /// </summary>
    [HarmonyPatch(typeof(AddScreenshake), nameof(AddScreenshake.Shake))]
    internal static class DetonationScreenshakePatch
    {
        private static void Prefix(AddScreenshake __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }

                float capMeters = Plugin.Cfg.EffectiveSporeBombScreenshakeRangeCapMeters;
                if (!SporeBombExplosionTuning.ShouldForcePositionalScreenshake(capMeters))
                {
                    return; // "vanilla" cap - nothing to enforce.
                }

                Vector3 position = __instance.transform.position;
                if (!DetonationScreenshakeRegistry.IsFromRecentDetonation(position))
                {
                    return; // not a spore-bomb shake - leave it alone.
                }

                bool wasPositional = __instance.positional;
                float vanillaRange = __instance.range;
                __instance.positional = true;

                // range is world units, the cap is meters (Core/WorldUnits.cs).
                __instance.range = SporeBombExplosionTuning.ResolveScreenshakeRange(
                    vanillaRange, wasPositional, GameUnits.MetersToUnits(capMeters));

                Diag.V(
                    $"[SporeBombShake] capped detonation shake on '{__instance.name}' @ {position} " +
                    $"(positional {wasPositional}->true, range {GameUnits.ToMeters(vanillaRange):0.#}m->" +
                    $"{GameUnits.ToMeters(__instance.range):0.#}m)");
            }
            catch (Exception e)
            {
                Diag.Error($"[SporeBombShake] threw: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
