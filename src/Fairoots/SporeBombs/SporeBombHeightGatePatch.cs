using Fairoots.Core;
using Fairoots.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace Fairoots.SporeBombs
{
    /// <summary>
    /// Bug fix, not a balance dial: the "Spore Bomb" (<c>SporeFungus</c>) and
    /// "Poison Spore Bomb" (<c>SporeMushroom</c>, non-Explo) variants are short,
    /// wide mushroom clumps, but their vanilla trigger <see cref="SphereCollider"/>
    /// reaches absurdly far above the actual mesh (confirmed by the maintainer via
    /// <see cref="TriggerRadiusOverlay"/>'s wireframe against the real prefab -
    /// screenshot showed the sphere's top well above nearby hanging vines, nowhere
    /// near the mushroom cap) - which makes it physically impossible to jump over
    /// one without triggering it anyway, since the trigger volume is a full sphere,
    /// not the flattened/short shape the actual hazard visually is. The "Explosive
    /// Spore Bomb" (<c>SporeMushroomExplo</c>) variant is genuinely round and left
    /// untouched (<see cref="SporeBombCullPatch.IsExplosiveVariant"/>).
    ///
    /// Unity has no way to reshape a <see cref="SphereCollider"/> into a
    /// flattened/capped shape directly, so rather than replace it with an
    /// approximating <see cref="CapsuleCollider"/>/<see cref="BoxCollider"/> (which
    /// would also change the horizontal footprint at ground level, the one thing
    /// that must stay correct - see <see cref="SporeBombCullPatch.ShrinkTriggerRadius"/>'s
    /// vanilla-baseline scaling), this instead intercepts the actual trigger event:
    /// a Harmony prefix on <c>TriggerEvent.OnTriggerEnter</c> (the generic,
    /// game-wide trigger-hit callback every <c>TriggerEvent</c> in the game uses -
    /// scoped here to spore bombs specifically via the triggering object's name, the
    /// same way <see cref="SporeBombExplosionPatch"/> scopes its
    /// <c>SpawnGameObject.Go</c> patch) that suppresses the hit entirely when the
    /// player is high enough above the spore bomb's base to have clearly jumped
    /// over it, per <see cref="SporeBombExplosionTuning.ShouldSuppressTriggerForHeight"/>.
    ///
    /// Also fully bypassed when <see cref="PluginConfig.KeepVanillaTriggerRadius"/>
    /// is on - that debug toggle exists specifically for before/after comparison
    /// screenshots (ROADMAP.md), and a "vanilla" comparison that still quietly
    /// applies this fix wouldn't actually be vanilla.
    /// </summary>
    [HarmonyPatch(typeof(TriggerEvent), "OnTriggerEnter")]
    internal static class SporeBombHeightGatePatch
    {
        private static bool Prefix(TriggerEvent __instance, Collider other)
        {
            if (Plugin.Cfg.KeepVanillaTriggerRadius.Value)
            {
                return true; // before/after comparison mode - full vanilla behavior, height cutoff included.
            }

            float maxHeight = SporeBombExplosionTuning.ResolveTriggerHeightCutoffMeters(
                Plugin.Cfg.EffectiveSporeBombTriggerHeightMultiplier);
            if (maxHeight <= 0f)
            {
                return true; // cutoff disabled - vanilla behavior.
            }

            string name = __instance.gameObject.name;
            if (!SporeBombCullPatch.ClassifySporeBomb(name) || SporeBombCullPatch.IsExplosiveVariant(name))
            {
                return true; // not a spore bomb, or the round Explosive variant - leave it alone.
            }

            if (!CharacterRagdoll.TryGetCharacterFromCollider(other, out var character))
            {
                return true; // not a player - let vanilla logic decide (nothing else sets these off anyway).
            }

            // Transform y is world units, the cutoff setting is meters - convert the
            // measured height rather than the setting, so the log reads in meters too
            // (see Core/WorldUnits.cs; they differ by 1.6x).
            float heightAboveBase = GameUnits.ToMeters(character.Center.y - __instance.transform.position.y);
            if (SporeBombExplosionTuning.ShouldSuppressTriggerForHeight(heightAboveBase, maxHeight))
            {
                Diag.V($"[SporeBombHeightGate] suppressed trigger on \"{name}\" - player {heightAboveBase:0.00}m above base (cutoff {maxHeight:0.00}m)");
                return false;
            }

            return true;
        }
    }
}
