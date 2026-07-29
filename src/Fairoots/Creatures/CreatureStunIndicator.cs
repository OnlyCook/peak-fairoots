using System;
using Fairoots.Diagnostics;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace Fairoots.Creatures
{
    /// <summary>
    /// The "this creature is out cold" visual, for the two creatures Fairoots can knock
    /// out (see <see cref="CreatureKnockoutPatch"/>, <see cref="BlowgunCreaturePatch"/>).
    ///
    /// PEAK has exactly one such effect already - <c>Spider.stunnedParticle</c>, played by
    /// <c>BonkRPC</c> when a thrown item stuns a spider. Both halves of this file exist to
    /// make that one effect tell the truth:
    /// <list type="bullet">
    /// <item><see cref="SpiderStunIndicatorPatch"/> keeps it up for the <em>whole</em>
    /// stun. Vanilla just calls <c>Play()</c> once, which is fine when the stun is the
    /// prefab's own 5 seconds but wrong the moment a blowgun dart sets it to 60 - the
    /// particle would finish and leave a spider sitting there apparently fine while it's
    /// still helpless.</item>
    /// <item><see cref="BeetleStunIndicator"/> gives beetles the same marker, which they
    /// have no equivalent of, by <b>cloning the spider's own particle system</b> rather
    /// than inventing one. That's deliberate: the effect is a Unity <em>asset</em>, so
    /// nothing in the decompiled code can construct or reference it, and a hand-built
    /// approximation would be the one piece of this mod that visibly isn't PEAK. Cloning a
    /// live instance is the same trick <c>Ui/NativeUiAssets</c> uses to get the game's own
    /// font, and <c>CoverMouthPosePatch</c> to get a hand pose.</item>
    /// </list>
    /// </summary>
    internal static class CreatureStunIndicator
    {
        private static bool _loggedMissingTemplate;

        /// <summary>
        /// A live spider's stunned-particle system, to clone from. Found by asking
        /// <c>SpiderManager</c> - the game's own registry - rather than sweeping the
        /// scene. Not cached across calls: spiders are culled in and out constantly, so a
        /// cached reference would go stale, and this only runs when a beetle is actually
        /// knocked out.
        /// </summary>
        private static ParticleSystem FindTemplate()
        {
            var manager = SpiderManager.instance;
            if (manager == null)
            {
                return null;
            }

            foreach (var spider in manager.spiders)
            {
                if (spider != null && spider.stunnedParticle != null)
                {
                    return spider.stunnedParticle;
                }
            }

            return null;
        }

        /// <summary>
        /// Clones the spider stun effect onto <paramref name="host"/>, returning the new
        /// instance or null if no template could be found (a level with no spiders at all -
        /// so the beetle simply has no marker rather than the knockout failing).
        /// </summary>
        internal static ParticleSystem TryClone(Transform host, float heightOffset)
        {
            try
            {
                ParticleSystem template = FindTemplate();
                if (template == null)
                {
                    if (!_loggedMissingTemplate)
                    {
                        _loggedMissingTemplate = true;
                        Diag.V("[Creatures] no spider stun particle available to clone - beetles will have no stun marker this level.");
                    }

                    return null;
                }

                var clone = UnityEngine.Object.Instantiate(template, host, worldPositionStays: false);
                clone.gameObject.name = "FairootsStunIndicator";
                clone.transform.localPosition = new Vector3(0f, heightOffset, 0f);
                clone.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                return clone;
            }
            catch (Exception e)
            {
                Diag.Error($"[Creatures] cloning the stun indicator threw: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Drives a stun particle system to match a boolean "is stunned" state, holding it
        /// on for as long as needed.
        ///
        /// <b>Loops the system rather than re-calling <c>Play()</c></b>: the authored
        /// effect is a one-shot burst, so keeping it alive by replaying it whenever it
        /// finished would restart the burst over and over and read as a stutter. Forcing
        /// <c>loop</c> on makes it a continuous effect for the duration, and the authored
        /// value is put back when the stun ends so the system is left exactly as found.
        /// (The flag is local-only - it costs no networking.)
        ///
        /// <b>Stops with <c>StopEmittingAndClear</c>, not <c>StopEmitting</c></b>
        /// (live-reported 2026-07-29 against a vanilla 5-second spider stun: the marker
        /// showed for its 5 seconds, then appeared to run a <em>second</em> 5-second cycle
        /// during which the spider was already active again). <c>StopEmitting</c> only
        /// closes the tap - every particle already alive plays out its full lifetime, which
        /// for this effect is about as long as the stun itself, so the marker outlived the
        /// state it was reporting and actively lied about it. Clearing is abrupt by
        /// comparison, but an indicator that ends late is worse than one that ends sharply.
        /// This affected both creatures, since both drive their marker through here.
        /// </summary>
        internal static void Drive(ParticleSystem particles, bool stunned, ref bool authoredLoopCaptured, ref bool authoredLoop)
        {
            if (particles == null)
            {
                return;
            }

            var main = particles.main;

            if (!authoredLoopCaptured)
            {
                authoredLoop = main.loop;
                authoredLoopCaptured = true;
            }

            if (stunned)
            {
                if (!main.loop)
                {
                    main.loop = true;
                }

                if (!particles.isPlaying)
                {
                    particles.Play();
                }

                return;
            }

            if (particles.isPlaying)
            {
                main.loop = authoredLoop;
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particles.Clear(true);
            }
        }
    }

    /// <summary>
    /// Holds a spider's own stunned-particle effect up for the whole stun, not just the
    /// length of the authored burst.
    ///
    /// A postfix on <c>Spider.LateUpdate</c> - the method that already decrements
    /// <c>_stunnedTime</c> every frame, so it's both the natural place to read it and free
    /// of any extra polling. Reads that private field directly rather than tracking the
    /// stun separately, so the marker matches the game's real stun state whatever caused
    /// it: a thrown item's vanilla 5 seconds, or a Fairoots blowgun dart's much longer one.
    ///
    /// Note this deliberately sits <em>downstream</em> of
    /// <see cref="SpiderRopeSuppressionPatch"/>, which skips <c>LateUpdate</c> entirely
    /// while <c>disable-spiders</c> is on - a disabled spider needs no marker.
    /// </summary>
    [HarmonyPatch(typeof(Spider), "LateUpdate")]
    internal static class SpiderStunIndicatorPatch
    {
        private static readonly AccessTools.FieldRef<Spider, float> StunnedTime = ResolveStunnedTime();

        private static bool _loggedMissingField;

        /// <summary>
        /// Per-spider record of the authored <c>loop</c> flag, so it can be restored. Keyed
        /// by instance ID via a tiny struct rather than a component, since this runs from a
        /// static patch.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<int, (bool Captured, bool Loop)> LoopState =
            new System.Collections.Generic.Dictionary<int, (bool, bool)>();

        internal static void ClearLevelState() => LoopState.Clear();

        private static AccessTools.FieldRef<Spider, float> ResolveStunnedTime()
        {
            try
            {
                return AccessTools.FieldRefAccess<Spider, float>("_stunnedTime");
            }
            catch (Exception e)
            {
                Diag.Error(
                    $"[Creatures] could not bind Spider._stunnedTime ({e.GetType().Name}) - " +
                    "spider stun markers will keep vanilla's fixed length.");
                return null;
            }
        }

        private static void Postfix(Spider __instance)
        {
            if (StunnedTime == null)
            {
                if (!_loggedMissingField)
                {
                    _loggedMissingField = true;
                    Diag.Warn("[Creatures] spider stun marker running vanilla-length (_stunnedTime unavailable).");
                }

                return;
            }

            if (__instance.stunnedParticle == null)
            {
                return;
            }

            int id = __instance.GetInstanceID();
            LoopState.TryGetValue(id, out var state);

            bool captured = state.Captured;
            bool loop = state.Loop;
            CreatureStunIndicator.Drive(__instance.stunnedParticle, StunnedTime(__instance) > 0f, ref captured, ref loop);
            LoopState[id] = (captured, loop);
        }
    }

    /// <summary>
    /// Gives a beetle the spider's stun marker while it's knocked out - by thrown item or
    /// by blowgun dart alike. Beetles have no such effect of their own, so this clones the
    /// spider's - see <see cref="CreatureStunIndicator"/>'s remarks for why cloning rather
    /// than building one.
    ///
    /// <b>Placed over the beetle's head, via its own <c>bonkPoint</c></b> (2026-07-29:
    /// live-reported as sitting over the body instead). <c>Beetle.bonkPoint</c> is the
    /// transform the beetle's attack force originates from - the front of the shell, where
    /// its head is - so it locates the head exactly, at any rotation and any prefab scale,
    /// without a hard-coded offset that a differently-sized beetle would break. Only the
    /// horizontal part is taken from it; the height that already looked right is kept, which
    /// matches the one-axis correction the maintainer described. A ping placed on a beetle's
    /// head measured ~0.45 world units forward of where the marker was, consistent with this.
    ///
    /// <b>Replicated to every client</b> (also 2026-07-29). It used to be driven by polling
    /// <see cref="CreatureKnockoutPatch.IsBeetleKnockedOut"/>, whose registry only exists on
    /// the beetle's owner, so nobody else saw the marker. It is now started by a single
    /// <c>[PunRPC]</c> carrying the duration, sent once when the knockout happens, and each
    /// client runs its own local timer from there.
    ///
    /// That RPC works without any networking of Fairoots' own because this component sits on
    /// the <em>same GameObject as the beetle's <c>PhotonView</c></em>, and PUN dispatches an
    /// RPC to any MonoBehaviour on that object. The cache of eligible components has to be
    /// refreshed after attaching, since the view was built before this existed.
    ///
    /// To be clear about a cost that isn't one: the particle system's <c>loop</c> flag is a
    /// purely local rendering setting. Looping the effect for the duration costs no
    /// networking at all - it's one RPC either way. Looping is only how a short authored
    /// burst is made to last, and the timer below is what ends it.
    /// </summary>
    internal sealed class BeetleStunIndicator : MonoBehaviour
    {
        /// <summary>
        /// How far above the beetle the marker floats, in world units - the height that
        /// tested correctly; only the horizontal placement was wrong.
        /// </summary>
        private const float HeightOffset = 0.9f;

        /// <summary>
        /// How far along the root-to-<c>bonkPoint</c> line to place the marker.
        ///
        /// Not 1.0 because <c>bonkPoint</c> is an <em>attack origin</em>, not a head
        /// marker - it sits slightly ahead of the beetle's head, so placing the marker
        /// exactly on it left it hanging off the front (live-reported 2026-07-29). A
        /// fraction rather than a subtracted distance, so it stays correct on a beetle of
        /// any scale, which is the whole reason for deriving this from a transform rather
        /// than hard-coding an offset.
        /// </summary>
        private const float HeadOffsetFraction = 0.75f;

        private Beetle _beetle;
        private ParticleSystem _particles;
        private bool _authoredLoopCaptured;
        private bool _authoredLoop;

        /// <summary><c>Time.time</c> at which the marker stops. Set locally on every client by the RPC.</summary>
        private float _showUntil;

        private void Awake() => _beetle = GetComponent<Beetle>();

        /// <summary>
        /// Called on the beetle's owner when a knockout starts; tells every client to show
        /// the marker for <paramref name="seconds"/>. Falls back to showing it locally if
        /// there's no PhotonView to send through (solo/offline).
        /// </summary>
        internal void Broadcast(float seconds)
        {
            if (seconds <= 0f)
            {
                return;
            }

            var view = GetComponent<PhotonView>();
            if (view == null)
            {
                Show(seconds);
                return;
            }

            try
            {
                view.RPC("FairootsBeetleStun", RpcTarget.All, seconds);
            }
            catch (Exception e)
            {
                // Never let a cosmetic marker break the knockout itself.
                Diag.V($"[Creatures] stun-marker RPC failed ({e.GetType().Name}) - showing locally only.");
                Show(seconds);
            }
        }

        [PunRPC]
        public void FairootsBeetleStun(float seconds) => Show(seconds);

        private void Show(float seconds)
        {
            _showUntil = Time.time + seconds;

            if (_particles == null)
            {
                // Cloned on first need rather than at spawn: most beetles are never knocked
                // out, and a level has ~15 of them.
                _particles = CreatureStunIndicator.TryClone(transform, HeightOffset);
                if (_particles == null)
                {
                    return;
                }
            }

            PlaceOverHead();
            CreatureStunIndicator.Drive(_particles, stunned: true, ref _authoredLoopCaptured, ref _authoredLoop);
        }

        /// <summary>
        /// Puts the marker over the head rather than the body centre, taking the horizontal
        /// position from <c>bonkPoint</c> and keeping the tested height. Re-applied while
        /// visible, since the beetle tumbles while knocked out.
        /// </summary>
        private void PlaceOverHead()
        {
            if (_particles == null)
            {
                return;
            }

            Vector3 local = new Vector3(0f, HeightOffset, 0f);

            Transform head = _beetle != null ? _beetle.bonkPoint : null;
            if (head != null)
            {
                Vector3 headLocal = transform.InverseTransformPoint(head.position);
                local = new Vector3(
                    headLocal.x * HeadOffsetFraction,
                    HeightOffset,
                    headLocal.z * HeadOffsetFraction);
            }

            _particles.transform.localPosition = local;
        }

        private void LateUpdate()
        {
            if (_particles == null)
            {
                return;
            }

            bool visible = Time.time < _showUntil;

            if (visible)
            {
                PlaceOverHead();

                // The beetle tumbles freely while knocked out (its rigidbody constraints are
                // released), so the marker is kept upright rather than rolling with the shell.
                _particles.transform.rotation = Quaternion.identity;
            }

            CreatureStunIndicator.Drive(_particles, visible, ref _authoredLoopCaptured, ref _authoredLoop);
        }
    }

    /// <summary>
    /// Attaches <see cref="BeetleStunIndicator"/> as each beetle goes live - the same
    /// <c>Mob.Start</c> seam every other beetle pass uses.
    /// </summary>
    [HarmonyPatch(typeof(Mob), "Start")]
    internal static class MobStartStunIndicatorPatch
    {
        private static void Postfix(Mob __instance)
        {
            if (!(__instance is Beetle) || __instance.GetComponent<BeetleStunIndicator>() != null)
            {
                return;
            }

            __instance.gameObject.AddComponent<BeetleStunIndicator>();

            // PUN caches which MonoBehaviours on a view's GameObject can receive RPCs, and
            // the beetle's view was built long before this component existed - without this
            // the [PunRPC] above is never found and the marker silently stays local.
            try
            {
                __instance.GetComponent<PhotonView>()?.RefreshRpcMonoBehaviourCache();
            }
            catch (Exception e)
            {
                Diag.V($"[Creatures] could not refresh the beetle RPC cache ({e.GetType().Name}) - stun markers may not replicate.");
            }
        }
    }
}
