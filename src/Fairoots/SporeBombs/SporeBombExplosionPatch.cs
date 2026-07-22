using System;
using Fairoots.Core;
using Fairoots.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace Fairoots.SporeBombs
{
    /// <summary>
    /// Phase 4 continued (ROADMAP.md): knockback/explosion-force, screen-shake
    /// distance cap, and particle/VFX-count tuning for a spore bomb's detonation.
    ///
    /// Per the confirmed runtime architecture (roots-runtime-findings memory),
    /// the named spore-bomb object is only a trigger volume - the actual
    /// explosion (<c>AOE</c> + <c>ExplosionEffect</c> + <c>AddScreenshake</c>,
    /// all generic reusable components, RESEARCH.md Q7) doesn't exist in the
    /// scene until <see cref="SpawnGameObject.Go"/> instantiates it on trigger.
    /// That means <see cref="SporeBombCullPatch"/>'s scene-scan pass (which runs
    /// once, at level load) can never reach these fields - they have to be tuned
    /// at the moment of detonation instead.
    ///
    /// This patches <see cref="SpawnGameObject.Go"/> itself rather than the
    /// spawned components directly: <c>SpawnGameObject</c> is also used
    /// game-wide for unrelated triggers (item spawns, other hazards), so
    /// patching it must stay narrowly scoped to spore bombs specifically - done
    /// here by checking the *triggering* object's name (the same
    /// <see cref="SporeBombCullPatch.ClassifySporeBomb"/> substring match the
    /// cull pass already uses) before doing anything. For a non-spore-bomb
    /// trigger, the prefix is a no-op and the original method runs unmodified.
    ///
    /// The scaling itself happens by mutating the newly-instantiated explosion's
    /// public fields directly, in the same frame, before returning - Unity
    /// defers a fresh MonoBehaviour's <c>Start()</c> (where <c>AOE</c>/
    /// <c>ExplosionEffect</c> read these fields) to the next frame, so there is
    /// no race between "we set the field" and "the game reads the field."
    /// </summary>
    [HarmonyPatch(typeof(SpawnGameObject), nameof(SpawnGameObject.Go))]
    internal static class SporeBombExplosionPatch
    {
        private static bool Prefix(SpawnGameObject __instance)
        {
            try
            {
                if (__instance == null || __instance.toSpawn == null
                    || !SporeBombCullPatch.ClassifySporeBomb(__instance.gameObject.name))
                {
                    return true; // not a spore bomb - let the original run untouched.
                }

                var spawned = UnityEngine.Object.Instantiate(
                    __instance.toSpawn, __instance.transform.position, __instance.transform.rotation);
                ApplyTuning(spawned);
                return false; // we've already done what Go() would have done.
            }
            catch (Exception e)
            {
                Diag.Error($"[SporeBombExplosion] threw: {e.GetType().Name}: {e.Message}");
                return true; // fall back to vanilla behavior rather than eat the spawn entirely.
            }
        }

        private static void ApplyTuning(GameObject spawned)
        {
            double knockbackMultiplier = Plugin.Cfg.EffectiveSporeBombKnockbackMultiplier;
            foreach (var aoe in spawned.GetComponentsInChildren<AOE>(true))
            {
                aoe.knockback = SporeBombExplosionTuning.ScaleKnockback(aoe.knockback, knockbackMultiplier);
                aoe.itemKnockbackMultiplier =
                    SporeBombExplosionTuning.ScaleKnockback(aoe.itemKnockbackMultiplier, knockbackMultiplier);
            }

            double vfxMultiplier = Plugin.Cfg.EffectiveSporeBombVfxCountMultiplier;
            foreach (var vfx in spawned.GetComponentsInChildren<ExplosionEffect>(true))
            {
                vfx.explosionPointCount = SporeBombExplosionTuning.ScaleVfxCount(vfx.explosionPointCount, vfxMultiplier);
                vfx.subExplosionPointCount =
                    SporeBombExplosionTuning.ScaleVfxCount(vfx.subExplosionPointCount, vfxMultiplier);
            }

            float shakeCapMeters = Plugin.Cfg.EffectiveSporeBombScreenshakeRangeCapMeters;
            foreach (var shake in spawned.GetComponentsInChildren<AddScreenshake>(true))
            {
                shake.range = SporeBombExplosionTuning.CapScreenshakeRange(shake.range, shakeCapMeters);
            }

            Diag.V(
                $"[SporeBombExplosion] tuned detonation @ {spawned.transform.position} " +
                $"(knockback x{knockbackMultiplier:0.##}, vfx x{vfxMultiplier:0.##}, " +
                $"shake-cap={(shakeCapMeters <= SporeBombExplosionTuning.NoScreenshakeCap ? "vanilla" : $"{shakeCapMeters:0}m")})");
        }
    }
}
