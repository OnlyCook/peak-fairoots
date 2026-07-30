using System;
using System.Collections.Generic;
using Fairoots.Core;
using Fairoots.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace Fairoots.Wind
{
    /// <summary>
    /// Phase 5 (ROADMAP.md): wind force/frequency scaling, non-backpack item
    /// force scaling, and obstacle-occlusion raycast-range scaling, all applied
    /// directly onto the live <c>WindChillZone</c> scene instance's public
    /// fields - mirroring how <c>SporeBombCullPatch</c> shrinks trigger colliders
    /// by scaling from a cached vanilla baseline rather than the field's current
    /// (possibly already-scaled) value, so repeated re-applies (a live config
    /// change, or the same instance surviving a level transition) never compound.
    ///
    /// Backpack wind immunity is a separate, narrower prefix on
    /// <c>AddWindForceToItem</c> below - the shared <c>windItemFactor</c> field
    /// applies to every ground item alike in vanilla, so it can't by itself give
    /// backpacks full immunity while only partially reducing other items; the
    /// prefix skips the method entirely for <c>Backpack</c> instances instead.
    /// </summary>
    [HarmonyPatch(typeof(WindChillZone), "Awake")]
    internal static class WindChillZoneTuningPatch
    {
        private sealed class Baseline
        {
            public float WindForce;
            public Vector2 WindTimeRangeOn;
            public Vector2 WindTimeRangeOff;
            public float WindItemFactor;
            public float MinRaycastDistance;
            public float MaxRaycastDistance;
        }

        /// <summary>
        /// Vanilla (pre-tuning) field values, keyed by <c>GetInstanceID()</c> -
        /// not a direct object reference, since a destroyed
        /// <see cref="WindChillZone"/> compares equal to Unity's overridden
        /// <c>null</c> in ways that make it an unreliable dictionary key (same
        /// reasoning as <c>SporeBombCullPatch.VanillaSphereRadii</c>).
        /// </summary>
        private static readonly Dictionary<int, Baseline> Baselines = new Dictionary<int, Baseline>();

        private static void Postfix(WindChillZone __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }

                var baseline = new Baseline
                {
                    WindForce = __instance.windForce,
                    WindTimeRangeOn = __instance.windTimeRangeOn,
                    WindTimeRangeOff = __instance.windTimeRangeOff,
                    WindItemFactor = __instance.windItemFactor,
                    MinRaycastDistance = __instance.minRaycastDistance,
                    MaxRaycastDistance = __instance.maxRaycastDistance,
                };
                Baselines[__instance.GetInstanceID()] = baseline;
                Apply(__instance, baseline);

                Diag.Info(
                    $"[WindTuning] captured baseline, then {(RootsState.Active ? "applied tuning" : "left it at vanilla (not in Roots)")} " +
                    $"(vanilla windForce={baseline.WindForce}, " +
                    $"on={baseline.WindTimeRangeOn}, off={baseline.WindTimeRangeOff}, " +
                    $"itemFactor={baseline.WindItemFactor}, raycast=[{baseline.MinRaycastDistance}," +
                    $"{baseline.MaxRaycastDistance}])");
            }
            catch (Exception e)
            {
                Diag.Error($"[WindTuning] Awake postfix threw: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void Apply(WindChillZone zone, Baseline baseline)
        {
            // Outside the Roots biome this mod does nothing at all (RootsState), and
            // wind is the clearest case for why that rule exists: WindChillZone drives
            // the gusts on the whole mountain, so a scaled windForce left on it would
            // silently rebalance every other biome. Restoring rather than just
            // skipping is what makes leaving Roots put the wind back.
            if (!RootsState.Active)
            {
                Restore(zone, baseline);
                Diag.V($"[WindTuning] not in Roots - WindChillZone#{zone.GetInstanceID()} restored to vanilla");
                return;
            }

            if (Plugin.Cfg.EffectiveDisableWindEntirely)
            {
                Restore(zone, baseline);

                // "Disabled" means no wind ever occurs, not just vanilla-strength
                // wind (2026-07-22 clarification) - force this zone off right now
                // (in case a gust happened to be active the instant the switch was
                // flipped) on top of WindToggleSuppressionPatch below, which stops
                // it from ever being switched back on for as long as the master
                // switch stays on.
                zone.windActive = false;
                Diag.V($"[WindTuning] disable-wind-entirely is ON - WindChillZone#{zone.GetInstanceID()} reverted to pure vanilla and forced inactive");
                return;
            }

            double forceMultiplier = Plugin.Cfg.EffectiveWindForceMultiplier;
            double durationMultiplier = Plugin.Cfg.EffectiveWindGustDurationMultiplier;
            double itemMultiplier = Plugin.Cfg.EffectiveWindItemForceMultiplier;
            double occlusionMultiplier = Plugin.Cfg.EffectiveWindObstacleOcclusionRangeMultiplier;

            zone.windForce = WindTuning.ScaleWindForce(baseline.WindForce, forceMultiplier);
            zone.windTimeRangeOn = new Vector2(
                WindTuning.ScaleWindActiveDuration(baseline.WindTimeRangeOn.x, durationMultiplier),
                WindTuning.ScaleWindActiveDuration(baseline.WindTimeRangeOn.y, durationMultiplier));
            zone.windTimeRangeOff = new Vector2(
                WindTuning.ScaleWindRestDuration(baseline.WindTimeRangeOff.x, durationMultiplier),
                WindTuning.ScaleWindRestDuration(baseline.WindTimeRangeOff.y, durationMultiplier));
            zone.windItemFactor = WindTuning.ScaleItemForceFactor(baseline.WindItemFactor, itemMultiplier);
            zone.minRaycastDistance = WindTuning.ScaleRaycastDistance(baseline.MinRaycastDistance, occlusionMultiplier);
            zone.maxRaycastDistance = WindTuning.ScaleRaycastDistance(baseline.MaxRaycastDistance, occlusionMultiplier);

            Diag.V(
                $"[WindTuning] applied to WindChillZone#{zone.GetInstanceID()}: " +
                $"windForce {baseline.WindForce}->{zone.windForce}, " +
                $"on {baseline.WindTimeRangeOn}->{zone.windTimeRangeOn}, " +
                $"off {baseline.WindTimeRangeOff}->{zone.windTimeRangeOff}, " +
                $"itemFactor {baseline.WindItemFactor}->{zone.windItemFactor}, " +
                $"raycast [{baseline.MinRaycastDistance},{baseline.MaxRaycastDistance}]->" +
                $"[{zone.minRaycastDistance},{zone.maxRaycastDistance}]");
        }

        /// <summary>Writes every cached vanilla field back onto the zone, untouched.</summary>
        private static void Restore(WindChillZone zone, Baseline baseline)
        {
            zone.windForce = baseline.WindForce;
            zone.windTimeRangeOn = baseline.WindTimeRangeOn;
            zone.windTimeRangeOff = baseline.WindTimeRangeOff;
            zone.windItemFactor = baseline.WindItemFactor;
            zone.minRaycastDistance = baseline.MinRaycastDistance;
            zone.maxRaycastDistance = baseline.MaxRaycastDistance;
        }

        /// <summary>
        /// Re-applies wind tuning to every currently-loaded <see cref="WindChillZone"/>
        /// from its cached baseline, wired up to <c>SettingChanged</c> on the Wind
        /// config entries/<see cref="PluginConfig.Preset"/> (see <c>Plugin.Awake</c>)
        /// so a live config change takes effect immediately instead of waiting for
        /// the next level load - mirrors
        /// <see cref="SporeBombs.SporeBombCullPatch.ReapplyTriggerRadiusToAll"/>.
        /// </summary>
        internal static void ReapplyAll()
        {
            if (Plugin.Cfg == null)
            {
                return;
            }

            try
            {
                int count = 0;
                foreach (var zone in UnityEngine.Object.FindObjectsOfType<WindChillZone>(true))
                {
                    if (!Baselines.TryGetValue(zone.GetInstanceID(), out var baseline))
                    {
                        // Never seen this instance's Awake fire (shouldn't normally
                        // happen since Awake always runs before any other code can
                        // reach it) - capture whatever it currently has as baseline
                        // rather than skip it silently.
                        baseline = new Baseline
                        {
                            WindForce = zone.windForce,
                            WindTimeRangeOn = zone.windTimeRangeOn,
                            WindTimeRangeOff = zone.windTimeRangeOff,
                            WindItemFactor = zone.windItemFactor,
                            MinRaycastDistance = zone.minRaycastDistance,
                            MaxRaycastDistance = zone.maxRaycastDistance,
                        };
                        Baselines[zone.GetInstanceID()] = baseline;
                    }

                    Apply(zone, baseline);
                    count++;
                }

                Diag.Info(
                    RootsState.Active
                        ? $"[WindTuning] reapplied tuning to {count} WindChillZone instance(s)"
                        : $"[WindTuning] not in Roots - vanilla wind restored on {count} WindChillZone instance(s)");
            }
            catch (Exception e)
            {
                Diag.Error($"[WindTuning] ReapplyAll threw: {e.GetType().Name}: {e.Message}");
            }
        }
    }

    /// <summary>
    /// While <see cref="PluginConfig.DisableWindEntirely"/> is on, no wind zone is
    /// ever allowed to switch itself back on - "disabled" means no wind ever
    /// occurs, not just vanilla-strength wind (2026-07-22 clarification).
    /// <c>RPCA_ToggleWind</c> is the one place <c>windActive</c> ever gets set
    /// true (called via Photon RPC, driven by the host's own randomized storm
    /// timer - RESEARCH.md Q6), so forcing its <c>set</c> parameter to
    /// <c>false</c> here means this client's own zone instance never goes
    /// active again regardless of what the host/network says, purely
    /// client-side (matching this mod's usual client-side-only architecture -
    /// no need to touch the host's own timer or any other client). Works
    /// alongside <see cref="WindChillZoneTuningPatch.Apply"/> forcing
    /// <c>windActive</c> off immediately for a gust already in progress the
    /// instant the switch is flipped.
    /// </summary>
    [HarmonyPatch(typeof(WindChillZone), "RPCA_ToggleWind")]
    internal static class WindToggleSuppressionPatch
    {
        private static void Prefix(ref bool set)
        {
            if (RootsState.Active && Plugin.Cfg.EffectiveDisableWindEntirely)
            {
                set = false;
            }
        }
    }

    /// <summary>
    /// Backpacks are fully immune to wind force by default, on every preset
    /// (ROADMAP.md's "backpack only" is the minimum immunity level even on
    /// Subtle) - a narrow prefix on the shared <c>AddWindForceToItem</c> rather
    /// than a field scale, since <c>windItemFactor</c> applies identically to
    /// every ground item and can't otherwise single out one item type.
    /// Player-toggleable via <see cref="PluginConfig.WindBackpackAlwaysImmune"/>
    /// (flat, not preset-gated) and fully bypassed when
    /// <see cref="PluginConfig.DisableWindEntirely"/> is on.
    /// </summary>
    [HarmonyPatch(typeof(WindChillZone), "AddWindForceToItem")]
    internal static class WindBackpackImmunityPatch
    {
        private static bool Prefix(Item item)
        {
            if (!RootsState.Active
                || Plugin.Cfg.EffectiveDisableWindEntirely
                || !Plugin.Cfg.EffectiveWindBackpackAlwaysImmune)
            {
                return true; // let the original run untouched - no immunity override.
            }

            return !(item is Backpack); // false = skip the original entirely (no force).
        }
    }
}
