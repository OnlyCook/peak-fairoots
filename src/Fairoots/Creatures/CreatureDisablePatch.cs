using System;
using System.Collections.Generic;
using Fairoots.Diagnostics;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace Fairoots.Creatures
{
    /// <summary>
    /// Phase 7 (ROADMAP.md), first mechanic: the three <c>Creatures</c> master kill
    /// switches - <c>disable-zombies</c>, <c>disable-beetles</c>,
    /// <c>disable-spiders</c>. Three separate switches rather than one
    /// "disable-creatures", because the three are wildly different complaints: a
    /// beetle is a knockback nuisance, a zombie is an unshakeable chase, a spider is
    /// an unavoidable ambush, and a player who wants one gone rarely wants all three
    /// gone. Same shape as <c>Spore-Areas/disable-spore-areas</c>: flat (no preset
    /// ever turns one on), off by default, host-authoritative, and always immediate
    /// regardless of <c>apply-changes-live</c> - a creature either exists in this run
    /// or it doesn't, and waiting for a level reload would just read as broken.
    ///
    /// <b>Each creature needs a different mechanism, because the three are unrelated
    /// implementations</b> (see <see cref="CreatureScan"/>):
    /// <list type="bullet">
    /// <item><b>Zombies</b> are not placed in the scene at all - they're spawned at
    /// runtime by <c>ZombieManager.Update</c>, master-client-only, from registered
    /// <c>MushroomZombieSpawner</c>s under a <c>maxActiveZombies</c> cap (1 in Roots,
    /// runtime-confirmed). So there is nothing to deactivate at level load; the lever
    /// is the spawn loop itself, handled by <see cref="ZombieSpawnSuppressionPatch"/>.
    /// Deliberately <em>not</em> done by zeroing the public <c>maxActiveZombies</c>
    /// field, which would mean writing over one of the game's own values and having
    /// to remember to put it back.</item>
    /// <item><b>Beetles</b> are scene-placed <c>Mob</c>s, so they get the
    /// spore-area treatment: deactivate the object, remember that we did, restore
    /// only what we hid.</item>
    /// <item><b>Spiders</b> can't be deactivated at all: <c>Spider.UpdateCulled</c>
    /// re-drives its own root GameObject's active state on every scan from player
    /// distance, so a <c>SetActive(false)</c> on the root would be undone the moment
    /// a player walked up to it - which is exactly when it matters. The behavior is
    /// suppressed at its two entry points instead
    /// (<see cref="SpiderScanSuppressionPatch"/>/<see cref="SpiderGrabSuppressionPatch"/>)
    /// and only the mesh child is hidden.</item>
    /// </list>
    /// </summary>
    internal static class CreatureDisablePatch
    {
        /// <summary>
        /// Every GameObject this session deactivated, keyed by
        /// <see cref="UnityEngine.Object.GetInstanceID"/>, so turning a switch back
        /// off restores exactly what Fairoots hid and nothing else - re-activating
        /// whatever happens to be inactive would also undo the game's own
        /// deactivations (<c>DisableBasedOnRunSettings</c>, a spider's own distance
        /// culling). Same rule and same reason as
        /// <c>SporeAreaDisablePatch.Deactivated</c>.
        /// </summary>
        private static readonly Dictionary<int, GameObject> Deactivated = new Dictionary<int, GameObject>();

        /// <summary>
        /// Applies the current switches to every creature under a freshly-loaded
        /// Roots Segment. Called once per level load from <see cref="RootsLevelWatcher"/>.
        /// </summary>
        internal static void Run(Transform rootsSegment)
        {
            try
            {
                Apply(
                    rootsSegment.GetComponentsInChildren<Beetle>(true),
                    rootsSegment.GetComponentsInChildren<Spider>(true),
                    "level load");
            }
            catch (Exception e)
            {
                Diag.Error($"[Creatures] Run threw: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// Re-resolves every creature <em>anywhere in the loaded scene</em> against
        /// the current switches, in both directions - wired to each switch's own
        /// <c>SettingChanged</c> and to <c>HostAuthoritySync</c>'s room-property
        /// update, so a client whose level load raced ahead of the host's first
        /// publish still ends up matching the host. Scene-wide rather than
        /// Roots-Segment-scoped because it also has to work the moment the player
        /// flips a toggle, from wherever they are.
        /// </summary>
        internal static void ReapplyToAll()
        {
            if (Plugin.Cfg == null)
            {
                return;
            }

            try
            {
                // includeInactive: true is load-bearing - the creatures this pass has
                // to be able to find again are precisely the ones it deactivated.
                Apply(
                    UnityEngine.Object.FindObjectsOfType<Beetle>(true),
                    UnityEngine.Object.FindObjectsOfType<Spider>(true),
                    "config change");
            }
            catch (Exception e)
            {
                Diag.Error($"[Creatures] ReapplyToAll threw: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>Drops the restore registry - called when the Roots level is torn down.</summary>
        internal static void ClearLevelState() => Deactivated.Clear();

        /// <summary>
        /// Applies the beetle switch to a single beetle the moment it starts, so a
        /// beetle that came into existence after the level-load pass (a runtime
        /// spawn, or one whose object was enabled late) is covered too. Called from
        /// <see cref="MobStartDisablePatch"/>.
        /// </summary>
        internal static void ApplyToBeetle(Beetle beetle)
        {
            if (!RootsState.Active || Plugin.Cfg == null || !Plugin.Cfg.EffectiveDisableBeetles)
            {
                return;
            }

            GameObject root = CreatureScan.ResolveBeetleRoot(beetle);
            if (root.activeSelf)
            {
                root.SetActive(false);
                Deactivated[root.GetInstanceID()] = root;
                Diag.V($"[Creatures]   disabled late beetle \"{CreatureScan.DescribePath(root.transform)}\"");
            }
        }

        private static void Apply(IReadOnlyList<Beetle> beetles, IReadOnlyList<Spider> spiders, string reason)
        {
            // Both switches read as "off" outside Roots, which is deliberately not the
            // same as skipping the pass: taking the restore branch is what brings back
            // anything this mod hid when the biome unloads (see RootsState).
            bool disableBeetles = RootsState.Active && Plugin.Cfg.EffectiveDisableBeetles;
            bool disableSpiders = RootsState.Active && Plugin.Cfg.EffectiveDisableSpiders;

            int beetlesHidden = 0, beetlesRestored = 0;
            foreach (var beetle in beetles)
            {
                if (beetle == null)
                {
                    continue;
                }

                if (SetDisabled(CreatureScan.ResolveBeetleRoot(beetle), disableBeetles, "beetle", out bool restored))
                {
                    beetlesHidden++;
                }
                else if (restored)
                {
                    beetlesRestored++;
                }
            }

            int spidersHidden = 0, spidersRestored = 0;
            foreach (var spider in spiders)
            {
                if (spider == null)
                {
                    continue;
                }

                GameObject visual = CreatureScan.ResolveSpiderVisual(spider);
                if (visual == null)
                {
                    continue;
                }

                if (SetDisabled(visual, disableSpiders, "spider", out bool restored))
                {
                    spidersHidden++;
                }
                else if (restored)
                {
                    spidersRestored++;
                }

                // The web is a separate LineRenderer on the root, so hiding the mesh
                // alone would leave a stub of web hanging out of the ceiling with
                // nothing on the end of it. Turning it off here is only half the job -
                // see SpiderRopeSuppressionPatch for why.
                if (spider.line != null)
                {
                    spider.line.enabled = !disableSpiders;
                }
            }

            Diag.Info(
                $"[Creatures] {reason}: disable-zombies={OnOff(RootsState.Active && Plugin.Cfg.EffectiveDisableZombies)}, " +
                $"disable-beetles={OnOff(disableBeetles)} ({beetles.Count} found, {beetlesHidden} newly hidden, {beetlesRestored} restored), " +
                $"disable-spiders={OnOff(disableSpiders)} ({spiders.Count} found, {spidersHidden} newly hidden, {spidersRestored} restored)" +
                (RootsState.Active ? string.Empty : " [not in Roots - everything restored]"));
        }

        /// <summary>
        /// The shared hide/restore rule. Returns whether this call newly hid the
        /// object; <paramref name="restored"/> reports the other direction. An
        /// already-inactive object is never <em>claimed</em> into the registry -
        /// it's either already ours or somebody else's, and claiming it would mean
        /// turning the switch off activates something that was meant to stay gone.
        /// </summary>
        private static bool SetDisabled(GameObject root, bool disable, string kind, out bool restored)
        {
            restored = false;
            int id = root.GetInstanceID();

            if (disable)
            {
                if (!root.activeSelf)
                {
                    return false;
                }

                root.SetActive(false);
                Deactivated[id] = root;
                Diag.V($"[Creatures]   disabled {kind} \"{CreatureScan.DescribePath(root.transform)}\"");
                return true;
            }

            if (Deactivated.TryGetValue(id, out GameObject ours))
            {
                Deactivated.Remove(id);
                if (ours != null)
                {
                    ours.SetActive(true);
                    restored = true;
                }
            }

            return false;
        }

        private static string OnOff(bool value) => value ? "ON" : "off";
    }

    /// <summary>
    /// The zombie half of <c>Creatures/disable-zombies</c>. A prefix on
    /// <c>ZombieManager.Update</c> - the one place zombies come from and the one
    /// place they're cleaned up - which, while the switch is on, despawns every live
    /// NPC zombie and then skips the original entirely so no replacement is ever
    /// spawned.
    ///
    /// Both halves are inherently host-only and that's deliberate, not a gap:
    /// vanilla's own <c>Update</c> returns immediately unless
    /// <c>PhotonNetwork.IsMasterClient</c>, so spawning is already the host's
    /// decision alone, and <c>DestroyZombie</c> is a <c>PhotonNetwork.Destroy</c>
    /// that only the owning client may call. A non-host client's prefix simply
    /// suppresses a method that was going to do nothing anyway - which is why this
    /// setting is host-authoritative (see <see cref="PluginConfig.EffectiveDisableZombies"/>).
    ///
    /// <b>Player-turned zombies are spared</b> (<c>isNPCZombie</c>): a zombie raised
    /// from a dead player via <c>MushroomZombie.RPC_Arise</c> is that player's death
    /// state, not an ambient hazard, and despawning it would delete a teammate's body
    /// out from under a different mechanic.
    /// </summary>
    [HarmonyPatch(typeof(ZombieManager), "Update")]
    internal static class ZombieSpawnSuppressionPatch
    {
        private static bool Prefix(ZombieManager __instance)
        {
            if (!RootsState.Active || Plugin.Cfg == null || !Plugin.Cfg.EffectiveDisableZombies)
            {
                return true;
            }

            if (!PhotonNetwork.IsMasterClient)
            {
                return false;
            }

            // Reverse iteration: DestroyZombie leads to MushroomZombie.OnDestroy,
            // which deregisters itself out of this very list.
            for (int i = __instance.zombies.Count - 1; i >= 0; i--)
            {
                if (i >= __instance.zombies.Count)
                {
                    continue;
                }

                var zombie = __instance.zombies[i];
                if (zombie == null || !zombie.isNPCZombie)
                {
                    continue;
                }

                Diag.V($"[Creatures] despawning NPC zombie \"{zombie.gameObject.name}\" (disable-zombies is on)");
                zombie.DestroyZombie();
            }

            return false;
        }
    }

    /// <summary>
    /// The behavioral half of <c>Creatures/disable-spiders</c>: a prefix on
    /// <c>Spider.Scan()</c>, the per-spider round-robin tick <c>SpiderManager</c>
    /// drives 3-per-frame, which is the only thing that ever starts a drop. With it
    /// suppressed a spider never notices a player below it, so it never drops and
    /// never reaches the state where its trigger can grab.
    ///
    /// Skipping <c>Scan</c> also skips its <c>UpdateCulled</c> call, which is what
    /// leaves the mesh hidden by <c>CreatureDisablePatch</c> hidden - vanilla would
    /// otherwise re-activate the spider root as a player approached.
    /// </summary>
    [HarmonyPatch(typeof(Spider), "Scan")]
    internal static class SpiderScanSuppressionPatch
    {
        private static bool Prefix() =>
            !RootsState.Active || Plugin.Cfg == null || !Plugin.Cfg.EffectiveDisableSpiders;
    }

    /// <summary>
    /// The other half of <c>Creatures/disable-spiders</c>: a prefix on
    /// <c>Spider.GrabCharacter</c>, the single funnel <c>SpiderTrigger.OnTriggerEnter</c>
    /// calls to actually catch someone. Belt and braces next to
    /// <see cref="SpiderScanSuppressionPatch"/> - a spider that was already mid-drop
    /// when the switch was flipped is still <c>Dropped</c>, and its trigger volume is
    /// still live, so suppressing future scans alone would let that one spider get a
    /// free grab.
    /// </summary>
    [HarmonyPatch(typeof(Spider), "GrabCharacter")]
    internal static class SpiderGrabSuppressionPatch
    {
        private static bool Prefix() =>
            !RootsState.Active || Plugin.Cfg == null || !Plugin.Cfg.EffectiveDisableSpiders;
    }

    /// <summary>
    /// The third piece of <c>Creatures/disable-spiders</c>, and the one that makes the
    /// web actually go away: a prefix on <c>Spider.LateUpdate</c>.
    ///
    /// Disabling the <c>LineRenderer</c> once (which is what
    /// <see cref="CreatureDisablePatch"/> does) is not enough on its own, and the
    /// reason is worth not rediscovering: <c>LateUpdate</c> calls
    /// <c>RopeRender.DisplayRope</c> unconditionally every single frame, and the very
    /// first line of <c>DisplayRope</c> is <c>line.enabled = true</c>. So the web came
    /// back one frame after being hidden - live-reported as "a bit of the string is
    /// always visible" (it renders as a short stub while the spider is idle, because
    /// it's drawn from the ceiling anchor to the spider's own body, which hasn't
    /// dropped anywhere yet).
    ///
    /// Patching <c>DisplayRope</c> itself was the other option and is the wrong one:
    /// it's shared with the rescue hook's rope, which has nothing to do with spiders.
    ///
    /// Nothing else in <c>LateUpdate</c> needs to keep running for a disabled spider:
    /// the grab-follow only matters while <c>Grabbing</c> (unreachable - see
    /// <see cref="SpiderGrabSuppressionPatch"/>), and the stun countdown and
    /// escape-achievement test are both about a spider that caught someone.
    /// </summary>
    [HarmonyPatch(typeof(Spider), "LateUpdate")]
    internal static class SpiderRopeSuppressionPatch
    {
        private static bool Prefix() =>
            !RootsState.Active || Plugin.Cfg == null || !Plugin.Cfg.EffectiveDisableSpiders;
    }

    /// <summary>
    /// Catches a beetle that starts existing after the level-load pass has already
    /// run. <c>Mob.Start</c> is where every mob registers itself with
    /// <c>MobManager</c>, so it's the game's own "this creature is now live" seam.
    /// Scoped to <see cref="Beetle"/> specifically - <c>Scorpion</c> is the other
    /// <c>Mob</c> subclass in the build and isn't a Roots creature, so
    /// <c>disable-beetles</c> has no business touching it.
    /// </summary>
    [HarmonyPatch(typeof(Mob), "Start")]
    internal static class MobStartDisablePatch
    {
        private static void Postfix(Mob __instance)
        {
            if (__instance is Beetle beetle)
            {
                CreatureDisablePatch.ApplyToBeetle(beetle);
            }
        }
    }
}
