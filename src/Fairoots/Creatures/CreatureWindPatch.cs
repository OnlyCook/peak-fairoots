using System;
using Fairoots.Core;
using Fairoots.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace Fairoots.Creatures
{
    /// <summary>
    /// Phase 7 (ROADMAP.md), last row: wind pushes creatures around. See
    /// <see cref="CreatureWind"/> for why zombies and beetles need separate dials - one
    /// scales something vanilla already does, the other grants something it never did.
    /// </summary>
    internal static class CreatureWindPatch
    {
        /// <summary>
        /// Reflection accessor for <c>WindChillZone.currentWindDirection</c>, which is
        /// <c>internal</c> to Assembly-CSharp and so unreachable by name from here. Needed
        /// only by the beetle drift, which has to know which way to push; the zombie half
        /// goes through the game's own force call and never touches the direction.
        /// </summary>
        private static readonly AccessTools.FieldRef<WindChillZone, Vector3> WindDirection = ResolveWindDirection();

        private static AccessTools.FieldRef<WindChillZone, Vector3> ResolveWindDirection()
        {
            try
            {
                return AccessTools.FieldRefAccess<WindChillZone, Vector3>("currentWindDirection");
            }
            catch (Exception e)
            {
                Diag.Error(
                    $"[Creatures] could not bind WindChillZone.currentWindDirection ({e.GetType().Name}) - " +
                    "beetles will not be affected by wind.");
                return null;
            }
        }

        /// <summary>
        /// The wind direction to push a beetle at <paramref name="position"/>, or null when
        /// no wind should apply there right now.
        ///
        /// Uses <c>WindChillZone.instance</c> - the game's own public singleton - rather than
        /// searching, and re-checks <c>windActive</c> and <c>windZoneBounds</c> every tick so
        /// a beetle outside the zone or a lull between gusts is left alone. Also honours the
        /// mod's own <c>disable-wind-entirely</c> switch, since "no wind at all" has to mean
        /// no wind for creatures either.
        /// </summary>
        private static Vector3? ActiveWindAt(Vector3 position)
        {
            if (WindDirection == null || Plugin.Cfg == null)
            {
                return null;
            }

            if (Plugin.Cfg.EffectiveDisableWindEntirely)
            {
                return null;
            }

            var zone = WindChillZone.instance;
            if (zone == null || !zone.windActive || !zone.windZoneBounds.Contains(position))
            {
                return null;
            }

            Vector3 direction = WindDirection(zone);
            return direction.sqrMagnitude < 1e-6f ? (Vector3?)null : direction.normalized;
        }

        internal static Vector3? WindPushAt(Vector3 position) => ActiveWindAt(position);
    }

    /// <summary>
    /// The zombie half: scales the wind force a zombie already receives.
    ///
    /// Zombies are <c>Character</c>s flagged <c>isBot</c>, so they sit in
    /// <c>Character.AllBotCharacters</c> and <c>WindChillZone.FixedUpdate</c> already calls
    /// <c>AddWindForceToCharacter</c> on each of them at 0.6x a player's multiplier. This
    /// borrows <c>windForce</c> around that call and restores it afterwards - the
    /// scale-around-the-native-call pattern used by <c>ClimbWindRopeSlowdownPatch</c> - so
    /// the field's real value, which <c>WindChillZoneTuningPatch</c> owns, is never
    /// overwritten.
    ///
    /// Scoped by <c>Character.isZombie</c>, the game's own public flag, so players and any
    /// other bot character are untouched.
    /// </summary>
    [HarmonyPatch(typeof(WindChillZone), "AddWindForceToCharacter")]
    internal static class ZombieWindForcePatch
    {
        private static float _nextLogTime;

        private static void Prefix(WindChillZone __instance, Character character, out float? __state)
        {
            __state = null;

            if (Plugin.Cfg == null || character == null || !character.isZombie)
            {
                return;
            }

            double multiplier = Plugin.Cfg.EffectiveZombieWindMultiplier;
            if (CreatureWind.IsVanillaZombieWind(multiplier))
            {
                return;
            }

            float vanilla = __instance.windForce;
            __state = vanilla;
            __instance.windForce = (float)(vanilla * CreatureWind.ResolveZombieMultiplier(multiplier));

            // Throttled: this runs every physics tick per zombie, so an unthrottled line
            // would bury the log. Also the confirmation that zombies get wind at all.
            if (Diag.Enabled && Time.time >= _nextLogTime)
            {
                _nextLogTime = Time.time + 2f;
                Diag.V(
                    $"[Creatures] wind on zombie \"{character.name}\": windForce {vanilla:0.#} -> " +
                    $"{__instance.windForce:0.#} (x{multiplier:0.##}; the game applies " +
                    $"{CreatureWind.VanillaBotWindShare:0.##} of a player's on top)");
            }
        }

        private static void Postfix(WindChillZone __instance, float? __state)
        {
            if (__state.HasValue)
            {
                __instance.windForce = __state.Value;
            }
        }
    }

    /// <summary>
    /// The beetle half: slides a beetle along the wind while it's walking.
    ///
    /// <b>This is added behaviour, not scaled behaviour</b> - a walking beetle cannot be
    /// moved by force at all, because <c>Mob.FixedUpdate</c> zeroes its rigidbody velocity
    /// every tick (see <see cref="CreatureWind"/>). So the push is applied as a position
    /// change, in a postfix that runs <em>after</em> that zeroing and after the native
    /// <c>Movement()</c>/<c>GroundSnapping()</c> have had their say; the next tick's ground
    /// raycast then re-snaps the beetle to the surface at its new spot, so it slides along
    /// the ground rather than being shoved through it.
    ///
    /// Only while <b>walking</b>: a beetle that is tumbling, flipping, knocked out or dead
    /// is under genuine rigidbody control, where a position write would fight the physics
    /// engine instead of adding to it. A sleeping beetle is skipped too - the game has
    /// deactivated its visuals, so moving it would teleport it when it wakes.
    /// </summary>
    [HarmonyPatch(typeof(Mob), "FixedUpdate")]
    internal static class BeetleWindDriftPatch
    {
        private static void Postfix(Mob __instance)
        {
            try
            {
                if (Plugin.Cfg == null || !(__instance is Beetle beetle) || beetle.sleeping)
                {
                    return;
                }

                double susceptibility = Plugin.Cfg.EffectiveBeetleWindSusceptibility;
                float driftSpeed = CreatureWind.BeetleDriftSpeed(beetle.movementSpeed, susceptibility);
                if (driftSpeed <= 0f)
                {
                    return; // 0 = vanilla: beetles are wind-immune.
                }

                if (!MobStateAccess.IsWalking(beetle))
                {
                    return;
                }

                Vector3? push = CreatureWindPatch.WindPushAt(beetle.transform.position);
                if (!push.HasValue)
                {
                    return;
                }

                beetle.transform.position += push.Value * driftSpeed * Time.fixedDeltaTime;
            }
            catch (Exception e)
            {
                Diag.Error($"[Creatures] beetle wind drift threw: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
