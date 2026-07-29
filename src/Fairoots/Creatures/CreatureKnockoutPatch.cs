using System;
using System.Collections.Generic;
using System.Reflection;
using Fairoots.Core;
using Fairoots.Diagnostics;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace Fairoots.Creatures
{
    /// <summary>
    /// Phase 7 (ROADMAP.md), new mechanic: throwing an item at a beetle or a zombie
    /// knocks it out for a configurable time, extending what PEAK already lets you do to
    /// spiders. See <see cref="CreatureKnockout"/> for what vanilla offers per creature
    /// and why the three cases are so different.
    ///
    /// <b>Both creatures need to detect the hit themselves</b>
    /// (<see cref="BeetleKnockoutReceiver"/>, <see cref="ZombieKnockoutReceiver"/>),
    /// because the game's own thrown-item damage path, <c>Bonkable</c>, is a component on
    /// particular item prefabs rather than on items in general - so relying on it means
    /// working for an arbitrary subset of the things a player can pick up. The zombie's
    /// receiver documents how that assumption produced a first version that did nothing
    /// at all; only the beetle's, which never had a <c>Bonkable</c> path to rely on in the
    /// first place, was right by accident.
    /// </summary>
    internal static class CreatureKnockoutPatch
    {
        /// <summary>
        /// <c>Time.time</c> until which each beetle stays down, by instance ID. Also
        /// read by <see cref="BeetleFlipSuppressionPatch"/>, which is what actually
        /// keeps it there.
        /// </summary>
        private static readonly Dictionary<int, float> BeetleKnockedOutUntil = new Dictionary<int, float>();

        /// <summary><c>Time.time</c> until which each zombie stays down, by instance ID.</summary>
        private static readonly Dictionary<int, float> ZombieKnockedOutUntil = new Dictionary<int, float>();

        internal static void ClearLevelState()
        {
            BeetleKnockedOutUntil.Clear();
            ZombieKnockedOutUntil.Clear();
        }

        /// <summary>Seconds left on this zombie's knockout, or 0 if it isn't knocked out.</summary>
        internal static float RemainingZombieKnockout(MushroomZombie zombie)
        {
            if (zombie == null || !ZombieKnockedOutUntil.TryGetValue(zombie.GetInstanceID(), out float until))
            {
                return 0f;
            }

            return Mathf.Max(0f, until - Time.time);
        }

        /// <summary>
        /// Knocks a zombie out for the configured time. Returns false if it's already
        /// down, so a second item landing during a knockout can't restart the clock.
        ///
        /// Sends <c>RPCA_Fall</c> directly rather than calling <c>Character.Fall</c>, for
        /// two reasons. The mundane one is that <c>Fall</c> is <c>internal</c> to
        /// Assembly-CSharp and unreachable from here. The useful one is that <c>Fall</c>
        /// also raises <c>GlobalEvents.OnCharacterFell</c>, which is precisely what the
        /// zombie's own <c>TestCharacterFell</c> listens to in order to stamp
        /// <c>fallSeconds</c> back down to 3 - so going straight to the RPC skips the
        /// handler that would fight us. The RPC is what replicates the ragdoll, so remote
        /// players still see the zombie go down rather than watching it stand upright
        /// while it lies down on the owner's screen.
        /// </summary>
        internal static bool KnockOutZombie(MushroomZombie zombie, float seconds, bool force = false)
        {
            if (zombie == null || seconds <= 0f || (!force && RemainingZombieKnockout(zombie) > 0f))
            {
                return false;
            }

            var character = zombie.GetComponent<Character>();
            if (character == null)
            {
                return false;
            }

            ZombieKnockedOutUntil[zombie.GetInstanceID()] = Time.time + seconds;
            character.photonView.RPC("RPCA_Fall", RpcTarget.All, seconds);

            Diag.V($"[Creatures] zombie \"{zombie.gameObject.name}\" knocked out for {seconds:0.##}s");
            return true;
        }

        /// <summary>
        /// The single "does this collision count as a hard throw?" rule, shared by both
        /// receivers so a beetle and a zombie can never disagree about what a hard throw
        /// is. Returns the item on success.
        ///
        /// <b>Two gates, and both were live-calibrated rather than guessed</b>
        /// (2026-07-29 - see <see cref="CreatureKnockout.CalibratedMinThrowSpeedMeters"/>
        /// for the measured numbers):
        /// <list type="number">
        /// <item><b>Speed.</b> The first version reused the game's own <c>Bonkable</c>
        /// threshold of 5 world units/s, which accepted essentially any contact: an item
        /// merely dropped from hand height is already past it on landing, and
        /// <c>relativeVelocity</c> folds in whatever the creature itself was doing. Even
        /// raising it to 14 m/s left medium throws (23-31 m/s measured) working. The bar
        /// is now near a full-strength throw.</item>
        /// <item><b>Distance from the thrower.</b> Speed alone can't express "you have to
        /// commit to getting close", because a hard throw is still fast a long way out -
        /// so speed alone would license sniping a zombie across a ravine for free.</item>
        /// </list>
        /// Together these are what make the mechanic cost something: a near-max throw has
        /// to be charged, is likely to lose you the item, and now has to be made from
        /// close range.
        ///
        /// Every rejection is logged with the measured values next to both thresholds,
        /// naming which gate stopped it - that log is how these numbers were arrived at,
        /// and how they can be re-tuned without guesswork.
        /// </summary>
        internal static bool IsHardThrow(Collision collision, string kind, GameObject creature, out Item item)
        {
            item = null;

            if (collision == null || Plugin.Cfg == null)
            {
                return false;
            }

            // Loose in the world rather than held - the game's own test, kept.
            var candidate = collision.gameObject.GetComponentInParent<Item>();
            if (candidate == null || candidate.itemState != ItemState.Ground)
            {
                return false;
            }

            float impactUnits = collision.relativeVelocity.magnitude;
            float speedThresholdMeters = CreatureKnockout.ResolveMinThrowSpeedMeters(
                Plugin.Cfg.EffectiveCreatureKnockoutMinThrowSpeed);
            float speedThresholdUnits = GameUnits.MetersToUnits(speedThresholdMeters);

            float distanceThresholdMeters = CreatureKnockout.ResolveMaxThrowDistanceMeters(
                Plugin.Cfg.EffectiveCreatureKnockoutMaxThrowDistance);
            float distanceThresholdUnits = GameUnits.MetersToUnits(distanceThresholdMeters);

            float distanceUnits = ThrowerDistanceUnits(candidate, creature);

            bool fastEnough = CreatureKnockout.IsHardEnough(impactUnits, speedThresholdUnits);

            // A negative distance means "couldn't work out who threw it" - fail open on
            // that gate only, so an unknown thrower costs the distance requirement rather
            // than the whole mechanic.
            bool closeEnough = distanceUnits < 0f
                || CreatureKnockout.IsCloseEnough(distanceUnits, distanceThresholdUnits);

            if (fastEnough && closeEnough)
            {
                item = candidate;
                return true;
            }

            if (Diag.Enabled)
            {
                string reason = !fastEnough
                    ? (closeEnough ? "too soft" : "too soft and too far")
                    : "thrown from too far away";
                string distanceText = distanceUnits < 0f
                    ? "thrower unknown"
                    : $"{GameUnits.ToMeters(distanceUnits):0.#}m from thrower" +
                      (distanceThresholdMeters > 0f ? $"/{distanceThresholdMeters:0.#}m" : " (no limit)");

                Diag.V(
                    $"[Creatures] {kind} \"{creature.name}\" ignored \"{candidate.gameObject.name}\" ({reason}): " +
                    $"{GameUnits.ToMeters(impactUnits):0.#}/{speedThresholdMeters:0.#} m/s, {distanceText}");
            }

            return false;
        }

        /// <summary>
        /// Distance in world units from whoever threw this item to the creature, or -1 if
        /// that can't be determined.
        ///
        /// Uses <c>Item.lastHolderCharacter</c> - private, hence the reflection - rather
        /// than <c>holderCharacter</c>, which is deliberately <b>null for a thrown
        /// item</b>: the game clears it the moment the item leaves a hand. The "last"
        /// variant is only ever assigned and never cleared (see <c>Item.holderCharacter</c>'s
        /// setter), which makes it exactly "who last had this", i.e. the thrower.
        /// </summary>
        private static float ThrowerDistanceUnits(Item item, GameObject creature)
        {
            if (LastHolder == null)
            {
                return -1f;
            }

            Character thrower = LastHolder(item);
            if (thrower == null)
            {
                return -1f;
            }

            return Vector3.Distance(thrower.Center, creature.transform.position);
        }

        /// <summary>
        /// Accessor for <c>Item.lastHolderCharacter</c> (private). Null if the field is
        /// ever renamed, in which case the distance gate is skipped rather than the
        /// mechanic breaking.
        /// </summary>
        private static readonly AccessTools.FieldRef<Item, Character> LastHolder = ResolveLastHolder();

        private static AccessTools.FieldRef<Item, Character> ResolveLastHolder()
        {
            try
            {
                return AccessTools.FieldRefAccess<Item, Character>("lastHolderCharacter");
            }
            catch (Exception e)
            {
                Diag.Error(
                    $"[Creatures] could not bind Item.lastHolderCharacter ({e.GetType().Name}) - " +
                    "knockout throws will not be distance-limited.");
                return null;
            }
        }

        /// <summary>Whether this beetle is currently knocked out.</summary>
        internal static bool IsBeetleKnockedOut(Mob beetle)
        {
            if (beetle == null)
            {
                return false;
            }

            return BeetleKnockedOutUntil.TryGetValue(beetle.GetInstanceID(), out float until) && Time.time < until;
        }

        /// <summary>
        /// Knocks a beetle onto its back for the configured time. Returns false if the
        /// mechanic is off or the beetle is already down.
        ///
        /// <b>The knockout is <c>MobState.RigidbodyControlled</c></b> - the state vanilla
        /// itself uses when a beetle has been knocked off something and is tumbling. That
        /// choice does the work for free: reading <c>Mob.Update</c>, <c>Attacking()</c>
        /// and <c>Targeting()</c> only run while the state is <c>Walking</c> (or
        /// <c>forceNoMovement</c>), so a beetle in this state cannot attack, cannot
        /// chase, and cannot even pick a target - and <c>FixedUpdate</c> releases its
        /// rigidbody constraints, so it visibly tumbles and lies there instead of
        /// standing frozen.
        ///
        /// The alternatives were worse: <c>sleeping</c> is public but
        /// <c>UpdateSleeping()</c> deactivates the visuals, so the beetle would vanish
        /// rather than lie there; <c>forceNoMovement</c> stops it moving but explicitly
        /// still calls <c>Attacking()</c>, so it would keep hitting anyone in reach while
        /// "unconscious".
        /// </summary>
        internal static bool KnockOutBeetle(Beetle beetle, float seconds, bool force = false)
        {
            if (beetle == null || seconds <= 0f || (!force && IsBeetleKnockedOut(beetle)))
            {
                return false;
            }

            if (!MobStateAccess.Available)
            {
                return false;
            }

            BeetleKnockedOutUntil[beetle.GetInstanceID()] = Time.time + seconds;
            MobStateAccess.SetRigidbodyControlled(beetle);

            // Tell every client to show the stun marker for this long. Sent from here
            // rather than driven by polling the registry above, because that registry only
            // exists on the beetle's owner - see BeetleStunIndicator.
            beetle.GetComponent<BeetleStunIndicator>()?.Broadcast(seconds);

            Diag.V($"[Creatures] beetle \"{beetle.gameObject.name}\" knocked out for {seconds:0.##}s");
            return true;
        }
    }

    /// <summary>
    /// Reflection access to <c>Mob.mobState</c>, which is <c>internal</c> - as is the
    /// <c>Mob.MobState</c> enum itself - so neither is nameable from this assembly.
    ///
    /// Goes through the <b>property setter</b> rather than writing the <c>_mobState</c>
    /// backing field, and that matters: the setter is what fires
    /// <c>RPC_SyncMobState</c> to the other clients. Writing the field would knock the
    /// beetle out on one machine only, leaving everyone else watching it walk around
    /// normally.
    /// </summary>
    internal static class MobStateAccess
    {
        private static readonly MethodInfo Setter = AccessTools.PropertySetter(typeof(Mob), "mobState");
        private static readonly Type StateType = AccessTools.Inner(typeof(Mob), "MobState");

        /// <summary>
        /// <c>MobState.RigidbodyControlled</c>, boxed once. It's the first member of the
        /// enum (<c>RigidbodyControlled, Walking, Flipping, Dead</c>), hence 0 - resolved
        /// by value because the type can't be named here to resolve it by name.
        /// </summary>
        private static readonly object RigidbodyControlled =
            StateType != null ? Enum.ToObject(StateType, 0) : null;

        private static bool _loggedFailure;

        private static readonly MethodInfo Getter = AccessTools.PropertyGetter(typeof(Mob), "mobState");

        /// <summary><c>MobState.Walking</c>, boxed once - the second enum member, hence 1.</summary>
        private static readonly object Walking =
            StateType != null ? Enum.ToObject(StateType, 1) : null;

        internal static bool Available => Setter != null && RigidbodyControlled != null;

        /// <summary>
        /// Whether this mob is in its normal walking state - i.e. the game is driving its
        /// position directly rather than letting the rigidbody do it. Anything that writes
        /// to a mob's transform has to check this, or it fights the physics engine.
        ///
        /// Fails <b>closed</b> if the reflection is unavailable: better to skip an added
        /// effect than to apply it in a state where it does damage.
        /// </summary>
        internal static bool IsWalking(Mob mob)
        {
            if (Getter == null || Walking == null)
            {
                return false;
            }

            try
            {
                return Equals(Getter.Invoke(mob, null), Walking);
            }
            catch
            {
                return false;
            }
        }

        internal static void SetRigidbodyControlled(Mob mob)
        {
            try
            {
                Setter.Invoke(mob, new[] { RigidbodyControlled });
            }
            catch (Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Diag.Error($"[Creatures] could not set Mob.mobState ({e.GetType().Name}) - beetle knockouts disabled.");
                }
            }
        }
    }

    /// <summary>
    /// Keeps a knocked-out beetle down. <c>Mob.TestStartFlippingMyself</c> is the game's
    /// own "I've stopped tumbling, right myself" check, called every
    /// <c>FixedUpdate</c> while the state is <c>RigidbodyControlled</c>; suppressing it
    /// is what turns a momentary tumble into a timed knockout, and letting it resume is
    /// what ends one. Recovery is therefore the game's own animation and state flow
    /// (<c>Flipping</c> → <c>Walking</c>), not something this mod has to drive.
    /// </summary>
    [HarmonyPatch(typeof(Mob), "TestStartFlippingMyself")]
    internal static class BeetleFlipSuppressionPatch
    {
        private static bool Prefix(Mob __instance)
        {
            return !CreatureKnockoutPatch.IsBeetleKnockedOut(__instance);
        }
    }

    /// <summary>
    /// The zombie half. <b>Rewritten 2026-07-29 after live testing found it did nothing
    /// at all</b> - and the original approach was wrong twice over, in ways worth
    /// recording because each hid the other.
    ///
    /// <b>Bug 1: <c>Bonkable</c> is not a reliable path to a zombie.</b> The first
    /// version simply lengthened the ragdoll that a thrown item's <c>Bonkable</c>
    /// already applies, on the reasoning that a zombie is a <c>Character</c> so
    /// <c>Bonkable.Bonk</c>'s <c>GetComponentInParent&lt;Character&gt;()</c> would find
    /// it. The lookup is right, but <c>Bonkable</c> is a component on <em>particular
    /// item prefabs</em>, not on items in general - so for most things a player picks up
    /// and throws, there is no <c>Bonkable</c> and nothing happens. That matches the
    /// live report exactly: it didn't work before this mod either. Fixed the same way
    /// the beetle's is, by having the creature notice the hit itself.
    ///
    /// <b>Bug 2: the zombie overwrites the duration with a hard-coded 3 seconds.</b>
    /// Even where <c>Bonkable</c> did apply, the fix would not have held.
    /// <c>Character.Fall</c> raises <c>GlobalEvents.OnCharacterFell</c>, and the zombie
    /// subscribes to it: <c>MushroomZombie.TestCharacterFell</c> responds by setting
    /// <c>currentState = LungeRecovery</c> and <c>character.data.fallSeconds = 3f</c> -
    /// a literal, ignoring whatever duration was actually requested. So any configured
    /// knockout longer than 3s was silently truncated by the zombie's own handler.
    /// <see cref="ZombieKnockoutEnforcePatch"/> holds the real duration against it.
    ///
    /// Attaches to the ragdoll's <b>bodypart rigidbodies</b>, not the zombie root:
    /// Unity raises <c>OnCollisionEnter</c> on the object carrying the
    /// <c>Rigidbody</c>/collider, and a character's colliders live on its individual
    /// bodyparts. A receiver on the root would never fire.
    /// </summary>
    internal sealed class ZombieKnockoutReceiver : MonoBehaviour
    {
        /// <summary>The zombie this bodypart belongs to; set when the receiver is attached.</summary>
        internal MushroomZombie Zombie;

        private void OnCollisionEnter(Collision collision)
        {
            if (Zombie == null || Plugin.Cfg == null || collision == null)
            {
                return;
            }

            var view = Zombie.GetComponent<Photon.Pun.PhotonView>();
            if (view != null && !view.IsMine)
            {
                return; // The owner drives the AI; every other client would race it.
            }

            double configured = Plugin.Cfg.EffectiveZombieKnockoutSeconds;
            if (CreatureKnockout.IsDisabled(configured))
            {
                return;
            }

            if (!CreatureKnockoutPatch.IsHardThrow(collision, "zombie", Zombie.gameObject, out Item item))
            {
                return;
            }

            if (CreatureKnockoutPatch.KnockOutZombie(Zombie, CreatureKnockout.ResolveSeconds(configured)))
            {
                Diag.V(
                    $"[Creatures]   ...by \"{item.gameObject.name}\" at " +
                    $"{GameUnits.ToMeters(collision.relativeVelocity.magnitude):0.#} m/s");
            }
        }
    }

    /// <summary>
    /// Holds a knocked-out zombie down for the configured time, against the zombie's own
    /// <c>TestCharacterFell</c> handler, which resets <c>fallSeconds</c> to a hard-coded
    /// 3 (see <see cref="ZombieKnockoutReceiver"/>, bug 2).
    ///
    /// A postfix on <c>MushroomZombie.Update</c> - i.e. after the state machine has had
    /// its turn each frame - that rewrites the remaining fall time and pins the state to
    /// <c>LungeRecovery</c>. Pinning the state matters as much as the timer: without it
    /// the zombie would keep running <c>DoChasing</c> and could still lunge while
    /// nominally unconscious.
    ///
    /// Recovery needs no code. Once this stops overriding, vanilla's own
    /// <c>DoLungeRecovery</c> waits out <c>lungeRecoveryTime</c> and returns to chasing,
    /// which is why the setting's description warns the zombie is out of the fight for
    /// somewhat longer than the number configured.
    /// </summary>
    [HarmonyPatch(typeof(MushroomZombie), "Update")]
    internal static class ZombieKnockoutEnforcePatch
    {
        private static void Postfix(MushroomZombie __instance)
        {
            float remaining = CreatureKnockoutPatch.RemainingZombieKnockout(__instance);
            if (remaining <= 0f)
            {
                return;
            }

            var character = __instance.GetComponent<Character>();
            if (character == null || character.data == null)
            {
                return;
            }

            character.data.fallSeconds = remaining;
            __instance.currentState = MushroomZombie.State.LungeRecovery;
        }
    }

    /// <summary>
    /// Gives every beetle the ability to notice being hit by a thrown item, which it
    /// otherwise entirely lacks - see <see cref="CreatureKnockout"/>.
    ///
    /// <b>Its own <c>OnCollisionEnter</c> rather than a patch on <c>Bonkable</c>:</b>
    /// <c>Bonkable</c> is a component on <em>some</em> items, so hooking it would make
    /// beetles vulnerable to exactly the subset of the game's items that happen to carry
    /// it, for no reason a player could infer. A component on the beetle responds to
    /// anything thrown at it. The acceptance rules are copied from the game's own
    /// (<c>ItemState.Ground</c>, i.e. actually loose in the world rather than held, and
    /// an impact speed over <see cref="CreatureKnockout.MinImpactVelocity"/>) so a
    /// gently dropped item doesn't fell a beetle.
    ///
    /// Acts only on the beetle's owning client, since the knockout is applied through a
    /// state change that the owner replicates by RPC; every other client would otherwise
    /// race it with a duplicate.
    /// </summary>
    internal sealed class BeetleKnockoutReceiver : MonoBehaviour
    {
        private Beetle _beetle;

        private void Awake()
        {
            _beetle = GetComponent<Beetle>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_beetle == null || Plugin.Cfg == null || collision == null)
            {
                return;
            }

            var view = _beetle.GetComponent<Photon.Pun.PhotonView>();
            if (view != null && !view.IsMine)
            {
                return;
            }

            double configured = Plugin.Cfg.EffectiveBeetleKnockoutSeconds;
            if (CreatureKnockout.IsDisabled(configured))
            {
                return;
            }

            if (!CreatureKnockoutPatch.IsHardThrow(collision, "beetle", _beetle.gameObject, out Item item))
            {
                return;
            }

            if (CreatureKnockoutPatch.KnockOutBeetle(_beetle, CreatureKnockout.ResolveSeconds(configured)))
            {
                Diag.V(
                    $"[Creatures]   ...by \"{item.gameObject.name}\" at " +
                    $"{GameUnits.ToMeters(collision.relativeVelocity.magnitude):0.#} m/s");
            }
        }
    }

    /// <summary>
    /// Attaches <see cref="BeetleKnockoutReceiver"/> as each beetle goes live - the same
    /// <c>Mob.Start</c> seam the speed/knockback/ragdoll passes use, so a beetle created
    /// after level load is covered too.
    /// </summary>
    /// <summary>
    /// Attaches a <see cref="ZombieKnockoutReceiver"/> to each of a zombie's ragdoll
    /// bodyparts as it spawns. <c>Start</c> rather than <c>Awake</c>, so the ragdoll's
    /// <c>partList</c> is populated by the time this runs.
    /// </summary>
    [HarmonyPatch(typeof(MushroomZombie), "Start")]
    internal static class MushroomZombieStartKnockoutPatch
    {
        private static void Postfix(MushroomZombie __instance)
        {
            try
            {
                var character = __instance.GetComponent<Character>();
                var ragdoll = character != null && character.refs != null ? character.refs.ragdoll : null;
                if (ragdoll == null)
                {
                    return;
                }

                int attached = 0;
                foreach (var part in ragdoll.partList)
                {
                    if (part == null || part.Rig == null)
                    {
                        continue;
                    }

                    // The collider that an item actually hits lives on the bodypart, so
                    // that's where the collision callback has to be.
                    var go = part.Rig.gameObject;
                    if (go.GetComponent<ZombieKnockoutReceiver>() == null)
                    {
                        go.AddComponent<ZombieKnockoutReceiver>().Zombie = __instance;
                        attached++;
                    }
                }

                Diag.V($"[Creatures] zombie \"{__instance.gameObject.name}\": knockout receivers on {attached} bodypart(s)");
            }
            catch (Exception e)
            {
                Diag.Error($"[Creatures] attaching zombie knockout receivers threw: {e.GetType().Name}: {e.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Mob), "Start")]
    internal static class MobStartKnockoutPatch
    {
        private static void Postfix(Mob __instance)
        {
            if (__instance is Beetle && __instance.GetComponent<BeetleKnockoutReceiver>() == null)
            {
                __instance.gameObject.AddComponent<BeetleKnockoutReceiver>();
            }
        }
    }
}
