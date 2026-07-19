using BepInEx;
using HarmonyLib;

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
            _harmony = new Harmony(PluginInfo.Guid);

            Logger.LogInfo(
                $"{PluginInfo.Name} {PluginInfo.Version} loaded. " +
                $"seed={Cfg.Seed.Value}, preset={Cfg.Preset.Value}, " +
                $"spore-bomb cull fraction={Cfg.SporeBombCullFraction:0.##}");
        }
    }
}
