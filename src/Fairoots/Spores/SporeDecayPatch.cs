using System;
using System.Collections.Generic;
using Fairoots.Core;
using Fairoots.Diagnostics;
using HarmonyLib;

namespace Fairoots.Spores
{
    /// <summary>
    /// The <c>Spores/clear-time-multiplier</c> dial: how long the Spores status takes
    /// to wear off once nothing is applying it any more.
    ///
    /// <b>Two fields, one dial.</b> The native recovery
    /// (<c>CharacterAfflictions.UpdateNormalStatuses</c>) is gated on
    /// <c>Time.time - LastAddedStatus(Spores) &gt; sporesReductionCooldown</c> and then
    /// drains <c>sporesReductionPerSecond * Time.deltaTime</c> per frame, so the
    /// wall-clock recovery time is <c>cooldown + status / rate</c>. Both fields are
    /// scaled together, in opposite directions, so the setting reads as a multiplier
    /// on time - see <see cref="SporeStatusTuning.ScaleDecayCooldown"/> for why the
    /// cooldown can't be left alone and <see cref="SporeStatusTuning.ScaleDecayRate"/>
    /// for why the rate is divided rather than multiplied.
    ///
    /// <b>Field scaling rather than a patch on the recovery method itself.</b>
    /// <c>UpdateNormalStatuses</c> is one private method handling every status type at
    /// once; a transpiler on it to reach only the Spores branch would be far more
    /// fragile than writing two serialized floats, and the fields have exactly one
    /// reader each (that branch), which is what makes writing them safe. Same shape as
    /// <c>Creatures/CreatureRagdollPatch</c>'s reasoning about not patching
    /// <c>Character.Fall</c>.
    ///
    /// <b>Baselines are cached per instance ID</b> and every application is computed
    /// from the baseline, never from the field's current value - the established
    /// pattern from <c>WindChillZoneTuningPatch</c>, <c>SporeAreaTuningPatch</c> and
    /// <c>CreatureSpeedPatch</c>. Without it a reapply would compound and 1.0 would
    /// stop meaning vanilla.
    ///
    /// <b>Unlike the creature patches, the baseline cache is never cleared on level
    /// teardown</b> - and must not be. A <c>Character</c> outlives an individual biome
    /// segment (the same <c>CharacterAfflictions</c> carries your spore meter from
    /// Roots into the next biome), so dropping its baseline while the object is still
    /// alive would mean the next apply pass caching the *already-scaled* field as if it
    /// were vanilla, permanently compounding the multiplier. Entries for characters
    /// that really are destroyed just go stale; instance IDs are never reused within a
    /// session, so a stale entry can't be mistaken for a live one, and the cache is a
    /// handful of floats per player.
    /// </summary>
    internal static class SporeDecayPatch
    {
        private readonly struct DecayBaseline
        {
            internal readonly float PerSecond;
            internal readonly float Cooldown;

            internal DecayBaseline(float perSecond, float cooldown)
            {
                PerSecond = perSecond;
                Cooldown = cooldown;
            }
        }

        private static readonly Dictionary<int, DecayBaseline> Baselines = new Dictionary<int, DecayBaseline>();

        /// <summary>Whether the vanilla-baseline summary has already been logged this session (see <see cref="LogBaselineOnce"/>).</summary>
        private static bool _baselineLogged;

        /// <summary>
        /// Re-applies the current multiplier to every live character. Wired to the
        /// setting's and the preset's <c>SettingChanged</c> (gated on
        /// <c>apply-changes-live</c>, like the other tuning dials - a recovery-speed
        /// change can be undone), to the Roots level-load pass, and to
        /// <c>HostAuthoritySync</c>'s room-property update so a client whose level load
        /// raced ahead of the host's first publish still converges on the host's value.
        ///
        /// Reads <c>Character.AllCharacters</c>, the game's own live-player list, rather
        /// than <c>FindObjectsOfType</c> - the kind of unconditional full-scene sweep
        /// that already cost this mod a mod-wide framerate drop once.
        /// </summary>
        internal static void ReapplyToAll()
        {
            if (Plugin.Cfg == null)
            {
                return;
            }

            try
            {
                int applied = 0;
                foreach (var character in Character.AllCharacters)
                {
                    if (character != null && Apply(character.refs?.afflictions))
                    {
                        applied++;
                    }
                }

                Diag.Info(
                    $"[Spores] clear-time x{Plugin.Cfg.EffectiveSporeClearTimeMultiplier:0.###} " +
                    $"applied to {applied} character(s)");
            }
            catch (Exception e)
            {
                Diag.Error($"[Spores] decay ReapplyToAll threw: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// Applies the clear-time multiplier to one character's afflictions, caching its
        /// authored field values the first time it's seen. Returns whether it was
        /// applied.
        /// </summary>
        internal static bool Apply(CharacterAfflictions afflictions)
        {
            if (afflictions == null || Plugin.Cfg == null)
            {
                return false;
            }

            int id = afflictions.GetInstanceID();
            if (!Baselines.TryGetValue(id, out DecayBaseline vanilla))
            {
                vanilla = new DecayBaseline(afflictions.sporesReductionPerSecond, afflictions.sporesReductionCooldown);
                Baselines[id] = vanilla;
            }

            double multiplier = Plugin.Cfg.EffectiveSporeClearTimeMultiplier;
            if (SporeStatusTuning.IsVanilla(multiplier))
            {
                afflictions.sporesReductionPerSecond = vanilla.PerSecond;
                afflictions.sporesReductionCooldown = vanilla.Cooldown;
            }
            else
            {
                afflictions.sporesReductionPerSecond = SporeStatusTuning.ScaleDecayRate(vanilla.PerSecond, multiplier);
                afflictions.sporesReductionCooldown = SporeStatusTuning.ScaleDecayCooldown(vanilla.Cooldown, multiplier);
            }

            LogBaselineOnce(vanilla, multiplier);
            Diag.V(
                $"[Spores]   \"{afflictions.gameObject.name}\" sporesReductionPerSecond " +
                $"{vanilla.PerSecond:0.####} -> {afflictions.sporesReductionPerSecond:0.####}, " +
                $"cooldown {vanilla.Cooldown:0.##}s -> {afflictions.sporesReductionCooldown:0.##}s");
            return true;
        }

        /// <summary>
        /// One-time log of what vanilla recovery actually is, in seconds.
        /// <c>sporesReductionPerSecond</c>/<c>sporesReductionCooldown</c> are serialized
        /// prefab fields, so their real values are <em>not</em> knowable from the
        /// decompiled C# - this is the only place the true vanilla clear time gets
        /// stated, which is what makes "0.5 halves it" checkable in-game rather than
        /// taken on faith. Also the thing to read first if the dial ever appears inert:
        /// a vanilla rate of 0 would mean spores never drain on their own, which no
        /// multiplier can or should change (see
        /// <see cref="SporeStatusTuning.ScaleDecayRate"/>).
        ///
        /// Logged at Info rather than verbose, unlike the per-character line above:
        /// it's one line per session and it's the number every report about this
        /// setting needs.
        /// </summary>
        private static void LogBaselineOnce(DecayBaseline vanilla, double multiplier)
        {
            if (_baselineLogged)
            {
                return;
            }

            _baselineLogged = true;

            if (vanilla.PerSecond <= 0f)
            {
                Diag.Warn(
                    "[Spores] vanilla sporesReductionPerSecond is 0 - this build never drains spores " +
                    "on its own, so clear-time-multiplier has nothing to scale and is inert.");
                return;
            }

            double vanillaSeconds = SporeStatusTuning.SecondsToClear(vanilla.Cooldown, vanilla.PerSecond, 1f, 1.0);
            double scaledSeconds = SporeStatusTuning.SecondsToClear(vanilla.Cooldown, vanilla.PerSecond, 1f, multiplier);
            Diag.Info(
                $"[Spores] vanilla spore recovery: {vanilla.Cooldown:0.##}s delay + " +
                $"{vanilla.PerSecond:0.####}/s = {vanillaSeconds:0.#}s to clear a full meter. " +
                $"At x{multiplier:0.###} that becomes {scaledSeconds:0.#}s.");
        }
    }

    /// <summary>
    /// Applies the clear-time multiplier the moment a character's afflictions come up.
    /// <c>CharacterAfflictions.Awake</c> is where the component resolves its own
    /// <c>Character</c> and initialises its status arrays, so it's both the earliest
    /// point at which the authored field values can be read back and the point before
    /// any recovery could have run.
    ///
    /// Needed in addition to the level-load pass: a player who joins mid-run (or the
    /// local character being rebuilt) never goes through a Roots level load on this
    /// client, and <c>ReapplyToAll</c> only covers characters that already exist when a
    /// setting changes.
    /// </summary>
    [HarmonyPatch(typeof(CharacterAfflictions), "Awake")]
    internal static class CharacterAfflictionsAwakeDecayPatch
    {
        private static void Postfix(CharacterAfflictions __instance)
        {
            SporeDecayPatch.Apply(__instance);
        }
    }
}
