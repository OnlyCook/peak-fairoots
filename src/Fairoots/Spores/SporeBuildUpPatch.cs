using System;
using Fairoots.Core;
using Fairoots.Diagnostics;
using HarmonyLib;

namespace Fairoots.Spores
{
    /// <summary>
    /// The <c>Spores/build-up-multiplier</c> dial: scales every dose of the Spores
    /// status before it lands, whatever applied it.
    ///
    /// <b>Why <c>CharacterAfflictions.AddStatus</c> and not each hazard.</b> Spores
    /// arrive from several unrelated systems - a spore area's <c>StatusEmitter</c>, the
    /// <c>AOE</c> a spore bomb's detonation re-explodes on a timer, a zombie's bite
    /// (<c>MushroomZombie.biteInitialSpores</c>) and the <c>Affliction_ZombieBite</c>
    /// it leaves behind, which keeps adding spores per second on its own - and the list
    /// is only as complete as the decompile happened to be. <c>AddStatus</c> is the one
    /// seam every single one of them funnels through, so scaling it here is what makes
    /// the setting mean "spores are weaker" rather than "these three specific hazards
    /// are weaker". That's also the difference from
    /// <c>SporeAreas/SporeAreaTuningPatch</c>'s rate dial, which scales one hazard's
    /// own emitter; the two compound on purpose (see <see cref="SporeStatusTuning"/>).
    ///
    /// <b>Prefix, by value written through a <c>ref</c> parameter</b> rather than
    /// suppressing the original and re-invoking it: <c>AddStatus</c> does a great deal
    /// besides changing a number (SFX, particles, screen FX, achievement bookkeeping,
    /// Photon pushes, zombification checks), all of which should still happen exactly
    /// as it decides. Only the incoming amount is this patch's business.
    ///
    /// <b>Not scoped to the local character</b>, unlike the camera/HUD patches. Statuses
    /// are applied on the owning client and pushed from there, so in the normal case
    /// each character's own client is the one running this; the <c>fromRPC</c> paths
    /// (e.g. <c>Character.RPCA_Stick</c>) run on other clients, and since the setting is
    /// host-authoritative every client resolves the same multiplier, so scaling
    /// unconditionally cannot produce a disagreement about how many spores someone got.
    /// Scoping to the local character would instead leave those RPC paths at vanilla.
    /// </summary>
    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddStatus))]
    internal static class SporeBuildUpPatch
    {
        /// <summary>
        /// Minimum gap between verbose log lines. Spores arrive far more often than
        /// once per tick - <c>Affliction_ZombieBite</c> calls <c>AddStatus</c> every
        /// single frame while it's active - so an unthrottled per-application line
        /// would bury the rest of the debug log while telling the reader nothing the
        /// first line didn't.
        /// </summary>
        private const float LogIntervalSeconds = 2f;

        private static float _nextLogTime;

        /// <summary>
        /// Runs at low priority so <c>Diagnostics/SporeStatusSourcePatch</c>, the other
        /// Fairoots prefix on this method, still logs the amount a spore source actually
        /// asked for rather than the amount this patch reduced it to - that tracer exists
        /// to identify unexpected spore sources, and an already-scaled number would make
        /// its output harder to compare against the native values it's being matched up
        /// with.
        /// </summary>
        [HarmonyPriority(Priority.Low)]
        private static void Prefix(CharacterAfflictions.STATUSTYPE statusType, ref float amount)
        {
            try
            {
                if (!RootsState.Active
                    || statusType != CharacterAfflictions.STATUSTYPE.Spores
                    || amount <= 0f
                    || Plugin.Cfg == null)
                {
                    return;
                }

                double multiplier = Plugin.Cfg.EffectiveSporeBuildUpMultiplier;
                if (SporeStatusTuning.IsVanilla(multiplier))
                {
                    return;
                }

                float scaled = SporeStatusTuning.ScaleBuildUp(amount, multiplier);
                if (Diag.Enabled && UnityEngine.Time.unscaledTime >= _nextLogTime)
                {
                    _nextLogTime = UnityEngine.Time.unscaledTime + LogIntervalSeconds;
                    Diag.V($"[Spores] build-up x{multiplier:0.###}: +{amount:0.#####} -> +{scaled:0.#####} Spores");
                }

                amount = scaled;
            }
            catch (Exception e)
            {
                Diag.Error($"[Spores] build-up prefix threw: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
