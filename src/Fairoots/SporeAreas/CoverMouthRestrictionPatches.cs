using HarmonyLib;
using UnityEngine;

namespace Fairoots.SporeAreas
{
    /// <summary>
    /// What covering your mouth costs, besides stamina: both hands are busy, so while
    /// covering the local player can't climb a wall, interact with or pick up
    /// anything, or switch items/backpack. Together with
    /// <c>CoverMouthController.FreeHands</c> (which empties the hands as the cover
    /// starts) and its climbing check (which refuses to *start* covering while
    /// holding onto something), the rule is simply "your hands are either over your
    /// mouth or doing something else, never both."
    ///
    /// Each patch is scoped to the local character and no-ops entirely when not
    /// covering, so nothing here is reachable during normal play.
    ///
    /// <b>Why these three seams specifically</b> (all confirmed against the
    /// decompile): <c>Interaction.canInteract</c> is the single gate every
    /// interaction in the game passes through, which covers picking up items, doors,
    /// and - the reason it matters here - grabbing ropes, vines and climb handles,
    /// since those are all interactibles rather than a separate climbing input.
    /// <c>CharacterItems.DoSwitching</c> is the whole slot/backpack switching input
    /// handler. <c>CharacterClimbing.TryToStartWallClimb</c> is the one climb that
    /// isn't an interaction - ordinary grab-the-wall climbing, polled from
    /// <c>Update</c>. Note it's only *starting* a climb that's blocked; a climb
    /// already in progress can't coexist with covering because covering can't begin
    /// during one.
    /// </summary>
    internal static class CoverMouthRestrictions
    {
        /// <summary>True while the local player's hands are over their mouth and the mechanic should be blocking hand actions.</summary>
        internal static bool HandsBusy => CoverMouthController.LocalCovering;
    }

    /// <summary>Blocks every interaction (pickups, ropes, vines, climb handles, doors) while covering.</summary>
    [HarmonyPatch(typeof(Interaction), "canInteract", MethodType.Getter)]
    internal static class CoverMouthInteractionPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (CoverMouthRestrictions.HandsBusy)
            {
                __result = false;
            }
        }
    }

    /// <summary>Blocks item/backpack slot switching while covering.</summary>
    [HarmonyPatch(typeof(CharacterItems), "DoSwitching")]
    internal static class CoverMouthSwitchingPatch
    {
        private static bool Prefix(CharacterItems __instance)
        {
            // Scoped to the local character: DoSwitching runs on every character's
            // component, and only this client's own player is covering their mouth
            // as far as this client's input is concerned.
            if (!CoverMouthRestrictions.HandsBusy)
            {
                return true;
            }

            var character = __instance.GetComponent<Character>();
            return character == null || character != Character.localCharacter;
        }
    }

    /// <summary>Blocks starting an ordinary wall climb while covering (rope/vine/handle grabs go through <see cref="CoverMouthInteractionPatch"/>).</summary>
    [HarmonyPatch(typeof(CharacterClimbing), "TryToStartWallClimb",
        new[] { typeof(bool), typeof(Vector3), typeof(bool), typeof(float) })]
    internal static class CoverMouthWallClimbPatch
    {
        private static bool Prefix(CharacterClimbing __instance)
        {
            if (!CoverMouthRestrictions.HandsBusy)
            {
                return true;
            }

            var character = __instance.GetComponent<Character>();
            return character == null || character != Character.localCharacter;
        }
    }
}
