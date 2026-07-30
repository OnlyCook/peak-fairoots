using System;
using Fairoots.Core;
using Fairoots.Diagnostics;
using HarmonyLib;
using Peak.Afflictions;
using UnityEngine;

namespace Fairoots.Wind
{
    /// <summary>
    /// Shared state for the climb-to-shelter-from-wind mechanic (ROADMAP.md's
    /// "New: climb-to-counter-wind" row; <see cref="ClimbWindResistance"/> holds
    /// the rationale and the arithmetic).
    ///
    /// Two halves that have to agree with each other: the wind patch decides
    /// "the wind is not allowed to push this climber" and records how hard it
    /// *would* have pushed, and the climbing patches read that recorded pressure
    /// to decide how much slower the climb is. Only the local character is ever
    /// recorded - the pressure is only ever consumed by the local player's own
    /// climb movement, and every other character's climb position arrives
    /// pre-decided over the network.
    /// </summary>
    internal static class ClimbWindShelter
    {
        private static float _pressure;
        private static Vector3 _windDirection = Vector3.zero;
        private static float _recordedAt = -1f;
        private static float _lastHeldOnAt = -1f;
        private static bool _loggedSheltered;
        private static bool _loggedGrace;

        /// <summary>
        /// Whether the mechanic is active at all: off entirely outside the Roots
        /// biome (<see cref="RootsState"/> - climbing is a whole-game activity, so
        /// this is the single check that keeps all four of this file's patches from
        /// slowing a climb anywhere else), when the host has killed wind (nothing to
        /// shelter from - and the slowdown would be a pure penalty), or when the
        /// feature is turned off.
        /// </summary>
        internal static bool Enabled =>
            RootsState.Active
            && Plugin.Cfg != null
            && !Plugin.Cfg.EffectiveDisableWindEntirely
            && Plugin.Cfg.EffectiveClimbSheltersFromWind;

        /// <summary>
        /// "Holding onto something" - every way the game lets a player grip the
        /// world, not just the climb-handle case vanilla's wind code already
        /// exempts (see <see cref="ClimbWindResistance"/>'s remarks on why that
        /// single check was mistaken for full climbing immunity).
        /// </summary>
        internal static bool IsHoldingOn(Character character)
        {
            if (character == null || character.data == null)
            {
                return false;
            }

            var data = character.data;
            return data.isClimbing
                || data.isRopeClimbing
                || data.isVineClimbing
                || data.currentClimbHandle != null;
        }

        /// <summary>
        /// Notes that the local player is holding onto something right now, which
        /// starts the clock for the let-go grace window
        /// (<see cref="ClimbWindResistance.GraceForceMultiplier"/>).
        ///
        /// Only ever called from the wind-force pass, which is exactly the right
        /// scope: it runs every physics step of an active gust regardless of
        /// whether the player is climbing, so a release mid-gust always has a
        /// fresh timestamp behind it - and a release outside a gust doesn't need
        /// one, since there's no force to buffer against.
        /// </summary>
        internal static void NoteHoldingOn()
        {
            _lastHeldOnAt = Time.time;
        }

        /// <summary>
        /// The wind-force multiplier owed to a player who just let go of a climb,
        /// or 1 (untouched) if they didn't, the window is off, or the shelter
        /// mechanic isn't active at all.
        /// </summary>
        internal static float GraceForceMultiplier()
        {
            if (!Enabled)
            {
                return 1f;
            }

            float multiplier = ClimbWindResistance.GraceForceMultiplier(
                _lastHeldOnAt,
                Time.time,
                Plugin.Cfg.EffectiveClimbShelterGraceSeconds,
                Plugin.Cfg.EffectiveClimbWindGraceForceMultiplier);

            if (Diag.Enabled && multiplier < 1f != _loggedGrace)
            {
                _loggedGrace = multiplier < 1f;
                Diag.V($"[ClimbWindShelter] let-go grace window {(_loggedGrace ? "started" : "ended")} (wind ×{multiplier:0.###})");
            }

            return multiplier;
        }

        /// <summary>Records how hard the wind would be pushing the local climber right now, and from where.</summary>
        internal static void Record(float pressure, Vector3 windDirection)
        {
            _pressure = ClimbWindResistance.ClampPressure(pressure);
            _windDirection = windDirection;
            _recordedAt = Time.time;

            if (Diag.Enabled && _pressure > 0f != _loggedSheltered)
            {
                _loggedSheltered = _pressure > 0f;
                Diag.V($"[ClimbWindShelter] climbing wind pressure {(_loggedSheltered ? "engaged" : "released")} ({_pressure:0.###})");
            }
        }

        /// <summary>
        /// The current wind pressure on the local climber, or false when there
        /// isn't one - no gust, out of the zone, fully sheltered, or the feature
        /// off. False means "climb at exactly vanilla speed": every caller treats
        /// this as the gate, so a player the wind can't reach never pays for the
        /// immunity they aren't getting.
        /// </summary>
        internal static bool TryGetPressure(out float pressure, out Vector3 windDirection)
        {
            pressure = 0f;
            windDirection = Vector3.zero;

            if (!Enabled || !ClimbWindResistance.IsPressureCurrent(_recordedAt, Time.time) || _pressure <= 0f)
            {
                return false;
            }

            pressure = _pressure;
            windDirection = _windDirection;
            return true;
        }

        /// <summary>
        /// Whether this character's climb movement is ours to slow down: the local
        /// player's own. Remote characters' climb positions are network-synced
        /// (<c>CharacterData</c>'s climbPos), so scaling their movement locally
        /// would just fight the sync - each client already applies this to itself.
        /// </summary>
        internal static bool IsLocalClimber(Character character) =>
            character != null && Character.localCharacter != null && character == Character.localCharacter;

        /// <summary>
        /// The flat (non-directional) slowdown for climbing modes whose movement
        /// isn't expressed on a wall plane - rope and vine climbing, which move
        /// along their own line rather than across a surface. Base multiplier,
        /// plus the upward penalty when the player is pulling themselves up.
        /// The into-the-wind term is deliberately not applied here: movement is
        /// constrained to the rope/vine, so "into the wind" isn't a direction the
        /// player can choose.
        /// </summary>
        internal static float FlatSlowdown(Character character, float pressure)
        {
            bool movingUp = character.input != null && character.input.movementInput.y > 0.01f;
            var move = ClimbWindResistance.Resist(
                new ClimbMove(0f, 1f),
                windLateral: 0f,
                windUp: 0f,
                pressure: pressure,
                baseMultiplier: Plugin.Cfg.EffectiveClimbWindSpeedMultiplier,
                upwardMultiplier: movingUp ? Plugin.Cfg.EffectiveClimbWindUpwardSpeedMultiplier : 1.0,
                intoWindMultiplier: 1.0);
            return move.Up;
        }
    }

    /// <summary>
    /// The shelter half: while a player is holding onto anything, wind force is
    /// suppressed outright instead of shoving them off (vanilla only does this
    /// for climb handles - <see cref="ClimbWindResistance"/>). The would-be push
    /// strength is recorded on the way out so the climbing patches below can
    /// charge for it in speed.
    ///
    /// Runs as a prefix rather than zeroing the force afterwards because the
    /// native method's last act is <c>AddForceAtPosition</c> - there is no
    /// after-the-fact seam, the force is already in the physics solver.
    ///
    /// The same prefix also runs the let-go grace window
    /// (<see cref="ClimbWindResistance.GraceForceMultiplier"/>): for the short
    /// moment after the local player releases a climb, the original *does* run,
    /// but with <c>windForce</c> temporarily scaled down around it. Temporarily -
    /// the postfix always restores the field, and it's never written with a
    /// computed value, because <c>WindChillZoneTuningPatch</c> owns that field's
    /// real value and rescales it from a cached vanilla baseline.
    /// </summary>
    [HarmonyPatch(typeof(WindChillZone), "AddWindForceToCharacter")]
    internal static class ClimbWindShelterPatch
    {
        private static bool Prefix(
            WindChillZone __instance,
            Character character,
            float mult,
            Vector3 ___currentWindDirection,
            out float __state)
        {
            __state = __instance.windForce;

            try
            {
                if (!ClimbWindShelter.Enabled)
                {
                    return true; // let the original run untouched.
                }

                if (character == null || character.photonView == null || !character.photonView.IsMine)
                {
                    return true; // the original's own first escape check - leave it to it.
                }

                bool local = ClimbWindShelter.IsLocalClimber(character);

                if (ClimbWindShelter.IsHoldingOn(character))
                {
                    if (local)
                    {
                        ClimbWindShelter.NoteHoldingOn();
                        ClimbWindShelter.Record(ComputePressure(__instance, character, mult, ___currentWindDirection), ___currentWindDirection);
                    }

                    return false; // skip the original entirely - no push while holding on.
                }

                if (local)
                {
                    // Just let go: run the real force, but a fraction of it, so
                    // finishing a climb doesn't catapult a player the game has
                    // already handed back to physics as "airborne".
                    float grace = ClimbWindShelter.GraceForceMultiplier();
                    if (grace < 1f)
                    {
                        __instance.windForce = __state * grace;
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                Diag.Error($"[ClimbWindShelter] prefix threw: {e.GetType().Name}: {e.Message}");
                return true;
            }
        }

        /// <summary>
        /// Restores <c>windForce</c> unconditionally - it's the same value the
        /// prefix read a moment earlier whenever no grace scaling happened, so
        /// this costs nothing in the common case and can't leave the field
        /// permanently reduced if some branch above returned early.
        /// </summary>
        private static void Postfix(WindChillZone __instance, float __state)
        {
            __instance.windForce = __state;
        }

        /// <summary>
        /// How hard the wind would be pushing this climber right now, 0-1, mirroring
        /// the terms of the native force formula we're skipping so that "sheltered
        /// from the wind" means the same thing to this mechanic as it does to the
        /// game: the light-volume exposure factor (<c>windPlayerFactor</c>, which
        /// the zone's own <c>ApplyStatus</c> keeps updated for the local character),
        /// the intensity curve, the obstacle-occlusion raycast, and the gust's
        /// ramp-in <paramref name="mult"/>.
        ///
        /// Two native terms are deliberately left out. The parasol/balloon
        /// multipliers only ever push the result above 1, where it clamps anyway -
        /// and neither is usable mid-climb. <c>ragdolledWindForceMult</c> keys off
        /// <c>fallSeconds</c>, which a climber holding a wall isn't accumulating.
        /// </summary>
        private static float ComputePressure(WindChillZone zone, Character character, float mult, Vector3 windDirection)
        {
            float factor = zone.useIntensityCurve
                ? Mathf.Clamp01(zone.windIntensity - 0.5f) * 2f
                : 1f;

            if (character.refs.afflictions.HasAfflictionType(Affliction.AfflictionType.LowGravity, out _))
            {
                factor = 0f;
            }

            if (zone.useRaycast && Physics.Raycast(
                    character.Center,
                    -windDirection,
                    out var hit,
                    zone.maxRaycastDistance,
                    HelperFunctions.GetMask(HelperFunctions.LayerType.TerrainMap)))
            {
                float distanceTerm = Mathf.InverseLerp(zone.minRaycastDistance, zone.maxRaycastDistance, hit.distance);
                float facing = Mathf.Clamp01(Vector3.Dot(hit.normal, windDirection));
                factor *= Mathf.Clamp01(distanceTerm + 1f - facing);
            }

            return ClimbWindResistance.ClampPressure(factor * mult * zone.windPlayerFactor);
        }
    }

    /// <summary>
    /// The cost half, for ordinary wall climbing:
    /// <c>CharacterClimbing.GetRequestedPostition()</c> (the game's own
    /// misspelling) is where a climb step's target position is produced, before
    /// <c>SampleWall</c> commits it to <c>climbPos</c> - so scaling the step here
    /// is the whole of "climbing is slower," including the stamina cost, which the
    /// game charges per second rather than per metre.
    ///
    /// The step is split onto the climbed surface's own plane - up-the-wall and
    /// across-the-wall - so <see cref="ClimbWindResistance.Resist"/> can charge
    /// upward and into-the-wind movement more than the rest. Anything left over
    /// (movement out of that plane, which the native formula doesn't produce, but
    /// a future game update might) is passed through untouched rather than
    /// silently dropped.
    /// </summary>
    [HarmonyPatch(typeof(CharacterClimbing), "GetRequestedPostition")]
    internal static class ClimbWindWallSlowdownPatch
    {
        private static void Postfix(Character ___character, ref Vector3 __result)
        {
            try
            {
                if (___character == null || !ClimbWindShelter.IsLocalClimber(___character))
                {
                    return;
                }

                if (!ClimbWindShelter.TryGetPressure(out float pressure, out Vector3 windDirection))
                {
                    return; // no wind on us right now - climb at exactly vanilla speed.
                }

                Vector3 origin = ___character.data.climbPos;
                Vector3 step = __result - origin;
                Vector3 normal = ___character.data.climbNormal;

                Vector3 up = Vector3.ProjectOnPlane(Vector3.up, normal);
                if (up.sqrMagnitude < 1e-6f)
                {
                    // Surface is (near enough) horizontal, so "up the wall" is
                    // undefined - no meaningful direction to charge extra for.
                    // Fall back to the flat base slowdown on the whole step.
                    float flat = ClimbWindShelter.FlatSlowdown(___character, pressure);
                    __result = origin + step * flat;
                    return;
                }

                up = up.normalized;
                Vector3 lateral = Vector3.Cross(up, normal).normalized;

                float upComponent = Vector3.Dot(step, up);
                float lateralComponent = Vector3.Dot(step, lateral);
                Vector3 residual = step - up * upComponent - lateral * lateralComponent;

                Vector3 windInPlane = Vector3.ProjectOnPlane(windDirection, normal);
                if (windInPlane.sqrMagnitude > 1e-6f)
                {
                    windInPlane = windInPlane.normalized;
                }

                var resisted = ClimbWindResistance.Resist(
                    new ClimbMove(lateralComponent, upComponent),
                    windLateral: Vector3.Dot(windInPlane, lateral),
                    windUp: Vector3.Dot(windInPlane, up),
                    pressure: pressure,
                    baseMultiplier: Plugin.Cfg.EffectiveClimbWindSpeedMultiplier,
                    upwardMultiplier: Plugin.Cfg.EffectiveClimbWindUpwardSpeedMultiplier,
                    intoWindMultiplier: Plugin.Cfg.EffectiveClimbWindIntoWindSpeedMultiplier);

                __result = origin + up * resisted.Up + lateral * resisted.Lateral + residual;
            }
            catch (Exception e)
            {
                Diag.Error($"[ClimbWindShelter] wall-climb postfix threw: {e.GetType().Name}: {e.Message}");
            }
        }
    }

    /// <summary>
    /// The cost half for rope climbing. Rope movement is a single
    /// percent-along-the-rope step scaled by the component's own
    /// <c>climbSpeedMod</c>, so the slowdown is applied by temporarily scaling
    /// that field around the method and restoring it afterwards - never by writing
    /// a computed value into it, since <c>climbSpeedMod</c> is also additively
    /// adjusted by the game's own climbing-speed affliction and a write would
    /// clobber it.
    /// </summary>
    [HarmonyPatch(typeof(CharacterRopeHandling), "Update")]
    internal static class ClimbWindRopeSlowdownPatch
    {
        private static void Prefix(CharacterRopeHandling __instance, Character ___character, out float __state)
        {
            __state = __instance.climbSpeedMod;

            try
            {
                if (___character == null || !ClimbWindShelter.IsLocalClimber(___character))
                {
                    return;
                }

                if (!ClimbWindShelter.TryGetPressure(out float pressure, out _))
                {
                    return;
                }

                __instance.climbSpeedMod = __state * ClimbWindShelter.FlatSlowdown(___character, pressure);
            }
            catch (Exception e)
            {
                Diag.Error($"[ClimbWindShelter] rope prefix threw: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void Postfix(CharacterRopeHandling __instance, float __state)
        {
            __instance.climbSpeedMod = __state;
        }
    }

    /// <summary>
    /// The cost half for vine climbing - same temporary-scale approach as
    /// <see cref="ClimbWindRopeSlowdownPatch"/>, applied to
    /// <c>CharacterVineClimbing.FixedUpdate</c>, which is where a vine's
    /// percent-along step is actually taken.
    /// </summary>
    [HarmonyPatch(typeof(CharacterVineClimbing), "FixedUpdate")]
    internal static class ClimbWindVineSlowdownPatch
    {
        private static void Prefix(CharacterVineClimbing __instance, Character ___character, out float __state)
        {
            __state = __instance.climbSpeedMod;

            try
            {
                if (___character == null || !ClimbWindShelter.IsLocalClimber(___character))
                {
                    return;
                }

                if (!ClimbWindShelter.TryGetPressure(out float pressure, out _))
                {
                    return;
                }

                __instance.climbSpeedMod = __state * ClimbWindShelter.FlatSlowdown(___character, pressure);
            }
            catch (Exception e)
            {
                Diag.Error($"[ClimbWindShelter] vine prefix threw: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void Postfix(CharacterVineClimbing __instance, float __state)
        {
            __instance.climbSpeedMod = __state;
        }
    }
}
