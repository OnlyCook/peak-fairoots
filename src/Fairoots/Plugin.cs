using System;
using BepInEx;
using Fairoots.Diagnostics;
using Fairoots.SporeBombs;
using Fairoots.Wind;
using HarmonyLib;
using UnityEngine;

namespace Fairoots
{
    /// <summary>
    /// Fairoots: makes the Roots biome more fair and balanced via a seed-deterministic
    /// preset system (subtle to aggressive rebalancing of wind, spore bombs, spore areas,
    /// and creatures). See ROADMAP.md for the full feature spec and phased plan.
    ///
    /// Phase 1 (this state): empty scaffold, no gameplay code yet - just a
    /// loadable, versioned plugin with config plumbing in place.
    /// </summary>
    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance { get; private set; }

        /// <summary>The bound config. Mechanic patches read seed/preset/resolved values from here.</summary>
        internal static PluginConfig Cfg { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Cfg = new PluginConfig(Config);
            Diag.Source = Logger;

            _harmony = new Harmony(PluginInfo.Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            TriggerRadiusOverlay.Init();

            // Any live change to a setting that feeds trigger-radius resolution
            // forces an immediate, full scene-wide re-scan (SporeBombCullPatch.
            // ReapplyTriggerRadiusToAll) rather than waiting for the next level
            // load - live testing showed a level-load-only refresh left some spore
            // bombs at their old size after a config change (whichever ones the
            // per-level pass didn't happen to touch that load), which was
            // confusing for exactly the before/after comparison this toggle exists
            // for. KeepVanillaTriggerRadius lives in the Debug section, so unlike
            // the other two it always reapplies immediately regardless of
            // Cfg.ApplyChangesLive (see that flag's remarks) - the other two are
            // regular gameplay settings, so they only get this immediate-reapply
            // treatment while live updates are actually turned on; with it off,
            // ResolveTriggerRadiusMultiplier's EffectiveSporeBombTriggerRadiusMultiplier
            // read already keeps kept spore bombs at whatever was snapshotted at
            // the last Roots level load instead.
            Cfg.KeepVanillaTriggerRadius.SettingChanged += (s, e) => SporeBombCullPatch.ReapplyTriggerRadiusToAll();
            Cfg.SporeBombTriggerRadiusMultiplierOverride.SettingChanged += (s, e) =>
            {
                if (Cfg.ApplyChangesLive.Value) SporeBombCullPatch.ReapplyTriggerRadiusToAll();
            };
            Cfg.Preset.SettingChanged += (s, e) =>
            {
                if (Cfg.ApplyChangesLive.Value)
                {
                    SporeBombCullPatch.ReapplyTriggerRadiusToAll();
                    WindChillZoneTuningPatch.ReapplyAll();
                }
            };

            // Same "reapply immediately on a live config change" treatment as the
            // spore-bomb trigger-radius settings above - a level-load-only refresh
            // would leave wind mid-storm at whatever the previous config said.
            EventHandler reapplyWindTuning = (s, e) =>
            {
                if (Cfg.ApplyChangesLive.Value) WindChillZoneTuningPatch.ReapplyAll();
            };
            Cfg.WindForceMultiplierOverride.SettingChanged += reapplyWindTuning;
            Cfg.WindGustDurationMultiplierOverride.SettingChanged += reapplyWindTuning;
            Cfg.WindItemForceMultiplierOverride.SettingChanged += reapplyWindTuning;
            Cfg.WindObstacleOcclusionRangeMultiplierOverride.SettingChanged += reapplyWindTuning;

            // Master kill switch - always reapplies immediately regardless of
            // ApplyChangesLive (see its own remarks), same treatment as
            // KeepVanillaTriggerRadius above.
            Cfg.DisableWindEntirely.SettingChanged += (s, e) => WindChillZoneTuningPatch.ReapplyAll();

            Logger.LogInfo(
                $"{PluginInfo.Name} {PluginInfo.Version} loaded. " +
                $"seed={Cfg.Seed.Value}, preset={Cfg.Preset.Value}, " +
                $"spore-bomb cull fraction={Cfg.SporeBombCullFraction:0.##}");
            if (Cfg.EnableDebugLogging.Value)
            {
                Logger.LogInfo(
                    $"Debug logging ON. Auto scene scan on load={Cfg.LogSceneScanOnLoad.Value}, " +
                    $"scan hotkey={Cfg.SceneScanHotkey.Value}.");
            }
        }

        private void Update()
        {
            if (Cfg == null)
            {
                return;
            }

            RootsLevelWatcher.CheckAndRun();

            if (!Cfg.EnableDebugLogging.Value)
            {
                return;
            }

            var key = Cfg.SceneScanHotkey.Value;
            if (key != KeyCode.None && Input.GetKeyDown(key))
            {
                SceneDiagnostics.DumpReport($"hotkey {key}");
            }

            var foliageKey = Cfg.FoliageProbeHotkey.Value;
            if (foliageKey != KeyCode.None && Input.GetKeyDown(foliageKey))
            {
                SceneDiagnostics.ProbeFoliageNearestSporeBomb();
            }
        }

        private void OnGUI()
        {
            if (Cfg == null)
            {
                return;
            }

            RemovedMarkerOverlay.Draw();
        }
    }
}
