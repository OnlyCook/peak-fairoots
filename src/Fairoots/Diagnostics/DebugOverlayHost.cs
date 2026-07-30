using UnityEngine;

namespace Fairoots.Diagnostics
{
    /// <summary>
    /// Carries the <c>OnGUI</c> that draws <see cref="RemovedMarkerOverlay"/>, and
    /// exists <em>only while that overlay is actually switched on</em>.
    ///
    /// <b>Why a whole component for one call.</b> The draw itself was already gated
    /// (debug logging plus its own opt-in toggle, both off by default) and returned on
    /// the first line - but the gate was inside an <c>OnGUI</c> on the plugin's own
    /// always-present behaviour, and merely *having* an <c>OnGUI</c> is not free in
    /// Unity: IMGUI runs its own event loop over every behaviour that declares one,
    /// several times a frame, laying out and dispatching events before any of our code
    /// gets a say. A player with every debug setting off was paying that forever. So
    /// the method now lives on a component that isn't there at all unless the overlay
    /// is on, which is the difference between "cheap" and "absent".
    ///
    /// Same reasoning as <see cref="TriggerRadiusOverlay"/>'s subscribe-on-demand, and
    /// the same rule the rest of the mod follows: debug tooling may cost whatever it
    /// likes while it's switched on, and must cost nothing when it isn't.
    /// </summary>
    internal sealed class DebugOverlayHost : MonoBehaviour
    {
        private static DebugOverlayHost _instance;

        /// <summary>The object the component lives on - the plugin's own DontDestroyOnLoad holder.</summary>
        private static GameObject _host;

        /// <summary>Remembers where to attach; called once from <c>Plugin.Awake</c>.</summary>
        internal static void Bind(GameObject host)
        {
            _host = host;
            Sync();
        }

        /// <summary>
        /// Adds or removes the component to match the current toggles. Wired to both
        /// settings' <c>SettingChanged</c> so flipping either one takes effect without
        /// a restart.
        /// </summary>
        internal static void Sync()
        {
            if (_host == null || Plugin.Cfg == null)
            {
                return;
            }

            bool wanted = Plugin.Cfg.EnableDebugLogging.Value && Plugin.Cfg.ShowRemovedSporeBombMarkers.Value;

            if (wanted && _instance == null)
            {
                _instance = _host.AddComponent<DebugOverlayHost>();
                Diag.Info("[Debug] removed-spore-bomb marker overlay is ON - its OnGUI pass is now running.");
                return;
            }

            if (!wanted && _instance != null)
            {
                Destroy(_instance);
                _instance = null;
                Diag.Info("[Debug] removed-spore-bomb marker overlay is off - its OnGUI pass is gone.");
            }
        }

        private void OnGUI() => RemovedMarkerOverlay.Draw();
    }
}
