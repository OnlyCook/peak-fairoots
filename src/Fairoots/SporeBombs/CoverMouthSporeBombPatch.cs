using Fairoots.Diagnostics;
using Fairoots.SporeAreas;
using HarmonyLib;

namespace Fairoots.SporeBombs
{
    /// <summary>
    /// The opt-in half of the cover-your-mouth mechanic: with
    /// <c>Spore-Bombs/cover-mouth-blocks-spore-bombs</c> on, covering your mouth also
    /// protects you from the small temporary spore cloud a spore bomb leaves when it
    /// detonates - not just the biome's persistent spore areas.
    ///
    /// <b>Off by default, on purpose.</b> The mechanic is scoped to spore areas
    /// (ROADMAP.md's "cover-mouth vs. spore areas" row): a spore area is something you
    /// see coming and choose to walk into, which is what makes holding your breath
    /// through it counterplay rather than a get-out-of-jail card, while a spore bomb is
    /// a surprise you've already triggered. This exists because the maintainer asked
    /// for the freedom to enable it anyway.
    ///
    /// <b>Only the spore status is suppressed</b> - the knockback, the noise and the
    /// screen shake all still land. Holding your hands over your mouth is not a shield.
    ///
    /// Mechanism: a spore bomb's mini spore area isn't a <c>StatusEmitter</c> at all
    /// (so <c>CoverMouthImmunityPatch</c>'s approach doesn't reach it) - it's the
    /// spawned explosion's <c>AOE</c>, which applies its status once, in-line, during
    /// <c>Explode()</c>. So the status amount is zeroed for the duration of that call
    /// and restored immediately afterwards.
    ///
    /// Safe despite <c>Explode</c> looping over every character in range: the game's own
    /// <c>CharacterAfflictions.AddStatus</c> early-returns unless the character is the
    /// caller's own (<c>photonView.IsMine</c>) or the call came from an RPC, so an AOE
    /// only ever applies status to the local player anyway. Zeroing it here therefore
    /// affects exactly the player who is covering their mouth, and no one else.
    /// </summary>
    [HarmonyPatch(typeof(AOE), "Explode")]
    internal static class CoverMouthSporeBombPatch
    {
        private static void Prefix(AOE __instance, out (float StatusAmount, bool HasAffliction) __state)
        {
            __state = (__instance.statusAmount, __instance.hasAffliction);

            if (!ShouldSuppress(__instance))
            {
                return;
            }

            __instance.statusAmount = 0f;

            // The status amount is not the only way an AOE delivers a payload: it can
            // also carry an Affliction, applied on a separate branch of Explode() that
            // zeroing statusAmount doesn't touch. Suppressed too, since it's part of the
            // same lungful of spores the setting promises to block.
            __instance.hasAffliction = false;
            Diag.V($"[CoverMouth] suppressed a spore bomb's spore cloud at {__instance.transform.position} (mouth covered)");
        }

        private static void Postfix(AOE __instance, (float StatusAmount, bool HasAffliction) __state)
        {
            // Always restored, whether or not this detonation was suppressed: the AOE
            // component can be pooled or reused, and leaving a zeroed payload behind
            // would silently defuse a later, unrelated explosion.
            __instance.statusAmount = __state.StatusAmount;
            __instance.hasAffliction = __state.HasAffliction;
        }

        private static bool ShouldSuppress(AOE aoe)
        {
            if (!CoverMouthController.LocalCovering
                || !Plugin.Cfg.EffectiveCoverMouthBlocksSporeBombs
                || aoe.statusType != CharacterAfflictions.STATUSTYPE.Spores
                || aoe.statusAmount <= 0f)
            {
                return false;
            }

            // Scoped to actual spore-bomb detonations rather than to "any AOE that
            // applies spores", so a future spores-applying hazard that isn't a spore bomb
            // doesn't quietly inherit this setting's behaviour.
            //
            // Identified by the tag Fairoots puts on the spawned explosion, NOT by the
            // recent-detonation registry this originally used: a bomb's cloud is one AOE
            // re-exploding on a timer for far longer than that registry remembers, so the
            // registry version blocked the first few seconds of a cloud and then let the
            // rest through (live-reported 2026-07-27, and confirmed from the call stack -
            // AOE.Explode driven by TimeEvent.Update). The registry is still consulted as
            // a fallback for anything that somehow wasn't tagged.
            return aoe.GetComponentInParent<SporeBombDetonationMarker>() != null
                   || DetonationScreenshakeRegistry.IsFromRecentDetonation(aoe.transform.position);
        }
    }
}
