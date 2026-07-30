using System.Collections.Generic;
using Fairoots.Diagnostics;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace Fairoots.Creatures
{
    /// <summary>
    /// The one answer to "is a spider coming for me right now?", behind
    /// <c>Ui/SpiderWarningLabel</c> - the same single-source-of-truth arrangement
    /// <see cref="SporePresence"/> has for the spore hazards, and for the same reason:
    /// two slightly different answers would mean the warning disagreeing with what is
    /// actually about to happen.
    ///
    /// <b>What vanilla gives you, and why that's the gap.</b> A spider hangs from the
    /// ceiling and <c>Scan()</c>s downward on a round-robin; the moment that spherecast
    /// finds a player it RPCs <c>RPCA_DropSpider</c>, which plays the <c>webMovement</c>
    /// SFX and starts the drop <em>in the same frame</em>. So there is an audio cue, but
    /// it is simultaneous with the attack rather than ahead of it - and the grab itself
    /// is <c>SpiderTrigger.OnTriggerEnter</c>, i.e. instant on contact with no windup at
    /// all once the spider is down. A player who doesn't happen to be looking up gets no
    /// usable warning. This turns the existing drop travel time - <c>spiderMoveSpeed *
    /// sqrt(distance)</c>, plus <c>spiderWaitTime</c> hanging there afterwards - into
    /// actual reaction time by making it visible.
    ///
    /// <b>Tracked by registry, never by scanning.</b> A per-frame sweep over
    /// <c>SpiderManager.instance.spiders</c> would mean touching ~90 objects every
    /// frame in Roots (runtime-confirmed count) to answer a question that is almost
    /// always "no" - the same unconditional-scan mistake that already cost this mod a
    /// mod-wide framerate drop once. Instead the drop RPC itself registers the spider,
    /// and only spiders actually mid-drop are ever examined.
    ///
    /// <b>Scoped to drops aimed at the local player.</b> <c>RPCA_DropSpider</c> is sent
    /// to every client and carries the target's <c>PhotonView</c>, so a teammate being
    /// jumped on across the level never raises a warning here.
    /// </summary>
    internal static class SpiderStrikeWarning
    {
        /// <summary>
        /// How long the warning lingers after the spider has finished dropping without
        /// catching anyone - the maintainer's requested "hide it about a second after it
        /// misses". Long enough that the label doesn't vanish the instant a near miss
        /// resolves, short enough that it isn't still claiming danger afterwards.
        /// </summary>
        private const float MissLingerSeconds = 1f;

        /// <summary>
        /// A registered drop: the spider, plus when its warning stops being true.
        /// </summary>
        private readonly struct Strike
        {
            internal Strike(Spider spider, float expiresAt)
            {
                Spider = spider;
                ExpiresAt = expiresAt;
            }

            internal Spider Spider { get; }

            /// <summary><c>Time.time</c> after which this drop is treated as a miss.</summary>
            internal float ExpiresAt { get; }
        }

        /// <summary>
        /// Spiders currently mid-drop <em>on the local player</em>, by instance ID.
        /// Entries are removed when the spider leaves the <c>Dropped</c> state (it
        /// grabbed, was bonked, or the level went away) <b>or</b> when its strike window
        /// expires - see <see cref="NoteDrop"/> for why the state alone isn't enough.
        /// </summary>
        private static readonly Dictionary<int, Strike> Incoming = new Dictionary<int, Strike>();

        internal static void ClearLevelState() => Incoming.Clear();

        /// <summary>
        /// Registers a drop aimed at the local player, and works out when it stops being
        /// a threat. Called from <see cref="SpiderDropWarningPatch"/>.
        ///
        /// <b>Why an explicit expiry rather than just watching <c>spiderState</c></b>
        /// (live-reported 2026-07-29: the label stayed up far too long after a miss).
        /// <c>SpiderState.Dropped</c> is not "currently attacking" - reading the drop
        /// coroutine, the state stays <c>Dropped</c> through the <em>entire retreat</em>:
        /// after the descent and the hang it starts a 2-second <c>DOLocalMove</c> back up
        /// and then waits a further 3 seconds before finally assigning
        /// <c>SpiderState.Idle</c>. So a spider that missed kept the warning on screen for
        /// about five more seconds while it climbed away.
        ///
        /// The warned-about event is <b>the drop itself</b>, and nothing else: the
        /// descent, <c>spiderMoveSpeed * sqrt(distance)</c> - the same expression the
        /// coroutine uses, with <paramref name="distance"/> being the value the drop RPC
        /// itself carries - read live off the spider rather than assumed, so an
        /// author-tweaked prefab still gets a correct window.
        ///
        /// <b><c>spiderWaitTime</c> is deliberately excluded</b>, which corrects a second
        /// pass at this (2026-07-29): including it looked principled, since the spider
        /// really does hang at player height afterwards with a live trigger, but it made
        /// the fix invisible - the hang is long enough that expiring after it landed the
        /// warning back within a second or two of the state change it was meant to
        /// pre-empt. The maintainer's call, twice stated, is that the label should mean
        /// "a spider is coming down at you", not "a spider is nearby and could still grab
        /// you". A spider that has landed and missed is hanging in plain sight; the drop
        /// is the part that arrives without warning, so the drop is the part that gets a
        /// warning.
        /// </summary>
        internal static void NoteDrop(Spider spider, float distance)
        {
            if (spider == null)
            {
                return;
            }

            float descent = spider.spiderMoveSpeed * Mathf.Sqrt(Mathf.Max(0f, distance));
            float window = descent + MissLingerSeconds;
            float expiresAt = Time.time + window;

            int id = spider.GetInstanceID();

            // Refresh rather than skip: a re-drop is a fresh threat and must extend the
            // window, not inherit the stale one from the previous attempt.
            bool isNew = !Incoming.ContainsKey(id);
            Incoming[id] = new Strike(spider, expiresAt);

            if (isNew)
            {
                // Logs spiderWaitTime alongside, even though it no longer counts, because
                // it's the number that made the previous attempt look like it did nothing
                // - seeing it makes the window's length self-explanatory in a log.
                Diag.V(
                    $"[Creatures] spider \"{spider.gameObject.name}\" is dropping on the local player - " +
                    $"warning for {window:0.##}s (descent {descent:0.##}s + {MissLingerSeconds:0.#}s linger; " +
                    $"its post-landing hang of {spider.spiderWaitTime:0.##}s is excluded)");
            }
        }

        /// <summary>
        /// Whether at least one spider is currently descending on the local player.
        /// Prunes as it goes, so a spider that finished its drop (or was destroyed with
        /// the level) drops out of the registry without needing anything to notice.
        /// </summary>
        internal static bool StrikeIncoming()
        {
            if (Incoming.Count == 0)
            {
                return false; // The overwhelmingly common case: one int compare.
            }

            List<int> finished = null;
            bool incoming = false;

            foreach (var pair in Incoming)
            {
                Spider spider = pair.Value.Spider;

                // A spider that has left Dropped is no longer a threat in flight: it
                // either already grabbed (LetGo/Grabbing is the game's problem now, and
                // being caught is its own unmistakable feedback) or it went back up.
                if (spider == null || spider.spiderState != Spider.SpiderState.Dropped)
                {
                    (finished ?? (finished = new List<int>())).Add(pair.Key);
                    continue;
                }

                // Missed: the strike window closed while the spider is still nominally
                // Dropped, which is what it looks like for the whole retreat. See
                // NoteDrop.
                if (Time.time >= pair.Value.ExpiresAt)
                {
                    (finished ?? (finished = new List<int>())).Add(pair.Key);
                    Diag.V($"[Creatures] spider \"{spider.gameObject.name}\" missed the local player - warning cleared");
                    continue;
                }

                incoming = true;
            }

            if (finished != null)
            {
                foreach (int id in finished)
                {
                    Incoming.Remove(id);
                }
            }

            return incoming;
        }
    }

    /// <summary>
    /// Registers an incoming spider drop with <see cref="SpiderStrikeWarning"/>.
    ///
    /// A postfix on <c>Spider.RPCA_DropSpider</c> rather than a prefix, deliberately:
    /// the method's body is a local coroutine, and <c>StartCoroutine</c> runs it
    /// synchronously up to its first <c>yield</c> - which is past the
    /// <c>spiderState = SpiderState.Dropped</c> assignment. So by the time this
    /// postfix runs the state is already correct, and
    /// <see cref="SpiderStrikeWarning.StrikeIncoming"/> can key off it immediately
    /// instead of racing the coroutine for a frame.
    ///
    /// Does nothing while <c>disable-spiders</c> is on - that switch already suppresses
    /// <c>Scan</c>, so no drop should ever start, but a warning about an attack that
    /// cannot happen would be worse than no warning at all.
    /// </summary>
    [HarmonyPatch(typeof(Spider), "RPCA_DropSpider")]
    internal static class SpiderDropWarningPatch
    {
        private static void Postfix(Spider __instance, PhotonView characterView, float distance)
        {
            if (!RootsState.Active || Plugin.Cfg == null || characterView == null)
            {
                return;
            }

            if (Plugin.Cfg.EffectiveDisableSpiders)
            {
                return;
            }

            var target = characterView.GetComponent<Character>();
            if (target == null || !target.IsLocal)
            {
                return; // Someone else's problem - the RPC goes to every client.
            }

            // distance is the drop RPC's own argument - the same value the coroutine
            // feeds into its descent time - so the warning window is derived from what
            // the game actually does, not estimated.
            SpiderStrikeWarning.NoteDrop(__instance, distance);
        }
    }
}
