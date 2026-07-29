using System;
using Fairoots.Core;
using Fairoots.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace Fairoots.Creatures
{
    /// <summary>
    /// Phase 7 (ROADMAP.md), "Zombie deaggro" - the one creature change that is new
    /// logic rather than a field tweak, because <b>vanilla zombies have no deaggro at
    /// all</b>. See <see cref="ZombieDeaggro"/> for the rule and for why 1.0 means
    /// "toughest" here instead of "vanilla".
    ///
    /// <b>The hook is <c>TargetIsValid</c>, and that choice is doing a lot of work.</b>
    /// It's the single predicate the whole zombie AI already routes every targeting
    /// question through, so one postfix covers all of them at once:
    /// <list type="bullet">
    /// <item><c>ValidateTarget()</c> (every frame) and <c>VerifyTarget()</c> (every
    /// chase frame) both drop <c>currentTarget</c> when it returns false - that's the
    /// deaggro itself.</item>
    /// <item><c>GetClosestCharacter()</c> skips candidates it rejects - <b>this half is
    /// essential, not a bonus.</b> <c>TryLookForTarget</c> re-picks the nearest player
    /// every 10 seconds with no distance limit whatsoever, so a deaggro that only
    /// cleared the target would be undone within 10 seconds and the mechanic would do
    /// nothing. Rejecting far candidates here is what makes an escape stick.</item>
    /// </list>
    ///
    /// <b>Asymmetry between the two rules, on purpose.</b> The distance rule applies to
    /// any candidate (so a zombie can neither keep nor re-acquire a player who has run
    /// far enough), but the sight-loss timer applies only to the target the zombie is
    /// <em>currently</em> locked onto - "I have lost the one I was chasing" is a
    /// statement about a chase in progress, and the game keeps no per-candidate sight
    /// history to ask it of anyone else.
    ///
    /// <b>Sleeping zombies are skipped entirely.</b> <c>DoSleeping</c> also calls
    /// <c>TargetIsValid</c>, to decide whether a passing player wakes it. A zombie that
    /// hasn't aggroed anyone can't deaggro, and letting the distance rule reach that
    /// path would quietly turn this into a "how close before a zombie notices you"
    /// dial as well - a different mechanic, and one that would fight the zombie's own
    /// <c>distanceBeforeWakeup</c>.
    ///
    /// Reads the game's own private <c>sinceSeenTarget</c> rather than tracking sight
    /// separately: <c>MushroomZombie.CalcVars</c> already maintains it every frame
    /// using the zombie's own <c>CanSeeTarget</c> line check (resetting it to 0 both
    /// when the target is visible and when there is no target). Reusing it means
    /// identical semantics to the game's own notion of "can I see them", and - since
    /// that line check is a raycast per zombie per frame - <b>no duplicated raycasts</b>.
    /// Amusingly the field is computed but never read anywhere in
    /// <c>MushroomZombie</c>; the only code that reads its own copy is
    /// <c>Scoutmaster</c>, which is also where this mechanic's 30-second base comes
    /// from.
    /// </summary>
    [HarmonyPatch(typeof(MushroomZombie), "TargetIsValid")]
    internal static class ZombieDeaggroPatch
    {
        /// <summary>
        /// Accessor for <c>MushroomZombie.sinceSeenTarget</c> (private). Resolved once;
        /// if the field ever disappears from a future game build this stays null and
        /// the sight-loss half of the rule is skipped rather than throwing every frame
        /// - failing back to "distance only" is a degraded mechanic, whereas an
        /// exception in a targeting predicate would break zombie AI outright.
        /// </summary>
        internal static readonly AccessTools.FieldRef<MushroomZombie, float> SinceSeenTarget = ResolveSinceSeenTarget();

        private static bool _loggedMissingField;

        private static AccessTools.FieldRef<MushroomZombie, float> ResolveSinceSeenTarget()
        {
            try
            {
                return AccessTools.FieldRefAccess<MushroomZombie, float>("sinceSeenTarget");
            }
            catch (Exception e)
            {
                Diag.Error(
                    $"[Creatures] could not bind MushroomZombie.sinceSeenTarget ({e.GetType().Name}) - " +
                    "zombie deaggro will fall back to distance only.");
                return null;
            }
        }

        private static void Postfix(MushroomZombie __instance, Character target, ref bool __result)
        {
            if (!__result || Plugin.Cfg == null || target == null)
            {
                return; // Already invalid for the game's own reasons, or nothing to judge.
            }

            if (!Plugin.Cfg.EffectiveZombieDeaggroEnabled)
            {
                return; // Vanilla behavior: this zombie never gives up.
            }

            // A sleeping zombie hasn't aggroed anyone - see class remarks.
            if (__instance.currentState == MushroomZombie.State.Sleeping)
            {
                return;
            }

            double multiplier = Plugin.Cfg.EffectiveZombieDeaggroMultiplier;

            var self = __instance.GetComponent<Character>();
            if (self == null)
            {
                return;
            }

            float distance = Vector3.Distance(self.Center, target.Center);
            if (ZombieDeaggro.ShouldDeaggroForDistance(distance, multiplier))
            {
                __result = false;
                LogDeaggro(__instance, target, "distance", distance, multiplier);
                return;
            }

            // Sight-loss applies only to the target actually being chased.
            if (!ReferenceEquals(target, __instance.currentTarget))
            {
                return;
            }

            if (SinceSeenTarget == null)
            {
                if (!_loggedMissingField)
                {
                    _loggedMissingField = true;
                    Diag.Warn("[Creatures] zombie deaggro running distance-only (sinceSeenTarget unavailable).");
                }

                return;
            }

            float unseenFor = SinceSeenTarget(__instance);
            if (ZombieDeaggro.ShouldDeaggroForSightLoss(unseenFor, multiplier))
            {
                __result = false;
                LogDeaggro(__instance, target, "lost sight", unseenFor, multiplier);
            }
        }

        private static void LogDeaggro(MushroomZombie zombie, Character target, string reason, float value, double multiplier)
        {
            if (!Diag.Enabled)
            {
                return;
            }

            string detail = reason == "distance"
                ? $"ran {GameUnits.ToMeters(value):0.#}m away (limit {GameUnits.ToMeters(ZombieDeaggro.ResolveDistanceWorldUnits(multiplier)):0.#}m)"
                : $"stayed unseen {value:0.#}s (limit {ZombieDeaggro.ResolveSightLossSeconds(multiplier):0.#}s)";

            // Handed to the transition logger rather than printed here: this method can
            // fire on several frames before the AI actually drops the target, so
            // printing directly would repeat the same event. ZombieAggroLogPatch emits
            // exactly one line, on the frame the target really goes away.
            CreatureAggroLog.NoteForcedDeaggro(zombie.GetInstanceID(), $"Fairoots deaggro rule - {detail}");
        }
    }
}
