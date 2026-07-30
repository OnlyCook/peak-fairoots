using System;
using System.Collections.Generic;
using Fairoots.Core;
using Fairoots.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace Fairoots.Creatures
{
    /// <summary>
    /// Phase 7 (ROADMAP.md), new mechanic: a blowgun dart takes a creature out of the
    /// fight. Zombies <b>die</b>; spiders and beetles are knocked out for a long time.
    ///
    /// <b>Why the two outcomes differ.</b> The maintainer's spec, and it maps cleanly onto
    /// what each creature actually is. A zombie has a real death state and a natural
    /// lifespan already - it dies on its own after two minutes
    /// (<c>MushroomZombie.lifetime</c>) - so killing one is expressing something the game
    /// already does, and it despawns with the game's own skeleton drop. A spider and a
    /// beetle have no death state at all, so "kill" has nowhere to go; a long stun is the
    /// honest equivalent.
    ///
    /// <b>Why vanilla can't already do this.</b> <c>Action_RaycastDart.FireDart</c>
    /// spherecasts against <c>LayerMask.GetMask("Character")</c> and applies its
    /// <c>afflictionsOnHit</c> to whatever <c>Character</c> it finds. A zombie <em>is</em>
    /// a <c>Character</c>, so darts already touch zombies - but they apply an affliction,
    /// not death. Spiders and beetles are not <c>Character</c>s at all (a beetle is a
    /// <c>Mob</c>, a spider a plain <c>MonoBehaviour</c>), so the cast cannot see them
    /// and a dart passes straight through.
    ///
    /// <b>Hooked on <c>RPC_DartImpact</c>, not <c>FireDart</c>, and that's the crux of
    /// making it work in multiplayer.</b> <c>FireDart</c> runs only on the shooter, but
    /// the effects here need whoever <em>owns</em> each creature to apply them: a zombie's
    /// death and a beetle's knockout both replicate by way of the owner's own state
    /// setters (which fire Photon RPCs), so a non-owner applying them locally would change
    /// nothing on anyone else's screen. <c>RPC_DartImpact</c> runs on <em>every</em>
    /// client and carries the dart's <c>origin</c> and <c>endpoint</c> as arguments - so
    /// every client can re-derive the exact flight path the shooter computed, and each one
    /// acts on only the creatures it owns. No new networking of our own.
    /// </summary>
    [HarmonyPatch(typeof(Action_RaycastDart), "RPC_DartImpact")]
    internal static class BlowgunCreaturePatch
    {
        private static void Postfix(Action_RaycastDart __instance, Vector3 origin, Vector3 endpoint)
        {
            try
            {
                if (!RootsState.Active || Plugin.Cfg == null || !Plugin.Cfg.EffectiveBlowgunAffectsCreatures)
                {
                    return;
                }

                Vector3 delta = endpoint - origin;
                float distance = delta.magnitude;
                if (distance <= 0.001f)
                {
                    return;
                }

                object creature = FindFirstCreatureAlongDart(origin, delta / distance, distance, __instance.dartCollisionSize);
                if (creature == null)
                {
                    return;
                }

                Apply(creature);
            }
            catch (Exception e)
            {
                Diag.Error($"[Creatures] blowgun dart handling threw: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// The nearest zombie/beetle/spider along the dart's path, or null.
        ///
        /// Casts against <b>every</b> layer and filters by component rather than by a
        /// layer mask: the three creatures are unrelated types and nothing in the
        /// decompile says what layers their colliders sit on, so a guessed mask would
        /// silently miss one. Triggers are <b>included</b> - a spider's only relevant
        /// collider is the trigger volume it grabs with, so ignoring triggers (as the
        /// native dart cast does) would make spiders unhittable.
        ///
        /// Nearest-first, so a dart is stopped by the first creature it meets rather than
        /// affecting everything on the line.
        /// </summary>
        private static object FindFirstCreatureAlongDart(Vector3 origin, Vector3 direction, float distance, float radius)
        {
            RaycastHit[] hits = Physics.SphereCastAll(
                origin,
                Mathf.Max(0.01f, radius),
                direction,
                distance,
                ~0,
                QueryTriggerInteraction.Collide);

            if (hits.Length == 0)
            {
                return null;
            }

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null)
                {
                    continue;
                }

                // Zombie first: its ragdoll bodyparts are children of the zombie root, so
                // a bodypart collider resolves upward to the zombie itself.
                var zombie = hit.collider.GetComponentInParent<MushroomZombie>();
                if (zombie != null)
                {
                    return zombie;
                }

                var beetle = hit.collider.GetComponentInParent<Beetle>();
                if (beetle != null)
                {
                    return beetle;
                }

                // Two routes to a spider: the body sits under the Spider root, but the
                // grab trigger is a SpiderTrigger that holds its own reference back and
                // is not necessarily parented under it.
                var spider = hit.collider.GetComponentInParent<Spider>();
                if (spider != null)
                {
                    return spider;
                }

                var trigger = hit.collider.GetComponentInParent<SpiderTrigger>();
                if (trigger != null && trigger.spider != null)
                {
                    return trigger.spider;
                }
            }

            return null;
        }

        private static void Apply(object creature)
        {
            float stunSeconds = CreatureKnockout.ResolveBlowgunStunSeconds(
                Plugin.Cfg.EffectiveBlowgunCreatureStunSeconds);

            if (creature is MushroomZombie zombie)
            {
                KillZombie(zombie);
                return;
            }

            if (creature is Beetle beetle)
            {
                // Owner only: KnockOutBeetle replicates via the mobState setter's RPC, so
                // a non-owner doing it would change nothing for anybody else.
                var view = beetle.GetComponent<Photon.Pun.PhotonView>();
                if (view != null && !view.IsMine)
                {
                    return;
                }

                if (stunSeconds > 0f &&
                    CreatureKnockoutPatch.KnockOutBeetle(beetle, stunSeconds, force: true))
                {
                    Diag.Info($"[Creatures] blowgun dart knocked out beetle \"{beetle.gameObject.name}\" for {stunSeconds:0.#}s");
                }

                return;
            }

            if (creature is Spider spider)
            {
                StunSpider(spider, stunSeconds);
            }
        }

        /// <summary>
        /// Kills a zombie the same way its own two-minute lifespan does: by putting it in
        /// <c>State.Dead</c>. Everything downstream is then the game's own - <c>Update</c>
        /// holds it passed out, and <c>ZombieManager</c> eventually calls
        /// <c>DestroyZombie</c>, which drops the skeleton. Deliberately not a bespoke
        /// death path, so a dart-killed zombie is indistinguishable from one that timed
        /// out.
        ///
        /// Owner only: the <c>currentState</c> setter calls <c>PushState()</c> when
        /// <c>photonView.IsMine</c>, which is what tells the other clients. A non-owner
        /// setting it would kill the zombie on one screen alone.
        /// </summary>
        private static void KillZombie(MushroomZombie zombie)
        {
            if (!zombie.photonView.IsMine)
            {
                return;
            }

            if (zombie.currentState == MushroomZombie.State.Dead)
            {
                return;
            }

            zombie.currentState = MushroomZombie.State.Dead;
            Diag.Info($"[Creatures] blowgun dart killed zombie \"{zombie.gameObject.name}\"");
        }

        /// <summary>
        /// Stuns a spider for the configured time by borrowing its own
        /// <c>bonkStunTime</c> around a call to the game's own <c>Bonk()</c>, then putting
        /// the value back.
        ///
        /// Reusing <c>Bonk()</c> rather than writing the stun directly is what buys the
        /// animation trigger, the stunned particle effect and the web release for free -
        /// and <c>Bonk()</c> is itself an RPC to all clients, so the stun replicates
        /// without help. It's gated to the spider's owner purely so that ten clients
        /// don't each fire the same RPC.
        /// </summary>
        private static void StunSpider(Spider spider, float stunSeconds)
        {
            if (stunSeconds <= 0f)
            {
                return;
            }

            if (spider.photonView != null && !spider.photonView.IsMine)
            {
                return;
            }

            float vanilla = spider.bonkStunTime;
            try
            {
                spider.bonkStunTime = stunSeconds;
                spider.Bonk();
            }
            finally
            {
                spider.bonkStunTime = vanilla;
            }

            Diag.Info($"[Creatures] blowgun dart stunned spider \"{spider.gameObject.name}\" for {stunSeconds:0.#}s");
        }
    }
}
