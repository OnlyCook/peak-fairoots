using ExitGames.Client.Photon;
using Fairoots.Diagnostics;
using Fairoots.SporeAreas;
using Fairoots.SporeBombs;
using Fairoots.Wind;
using Photon.Pun;

namespace Fairoots.Networking
{
    /// <summary>
    /// A single always-present, <c>DontDestroyOnLoad</c> component (instantiated
    /// once by <c>Plugin.Awake</c>) whose only job is re-publishing
    /// <see cref="HostAuthority.PublishAll"/> whenever host authority itself
    /// might have changed, rather than a config value changing: joining a room
    /// that's already in progress (so a late joiner immediately gets the
    /// current host's values instead of waiting for the next config edit), and
    /// host migration (the previous host disconnected and Photon promoted
    /// someone else to master - that new master needs to publish its own
    /// config immediately, since it's now the authoritative one). Every other
    /// publish trigger (an actual config value changing) is wired directly to
    /// the relevant <c>SettingChanged</c> event in <c>Plugin.cs</c> and to
    /// <c>RootsLevelWatcher</c>. <see cref="HostAuthority.PublishAll"/> itself
    /// already no-ops for whichever client is *not* the host, so both callbacks
    /// below are safe to fire on every client unconditionally.
    /// </summary>
    internal class HostAuthoritySync : MonoBehaviourPunCallbacks
    {
        public override void OnJoinedRoom()
        {
            HostAuthority.PublishAll();
        }

        public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
        {
            HostAuthority.PublishAll();
        }

        /// <summary>
        /// Fires on every client (including the host, who ignores it below)
        /// whenever the room's custom properties actually change - i.e. right
        /// after the host's <see cref="HostAuthority.PublishAll"/> write lands.
        /// Without this, a non-host client whose <c>WindChillZone</c>/spore-bomb
        /// trigger-radius tuning was already applied once (at their own Roots
        /// level load, from <c>Core/Presets</c> onward these are only computed
        /// at scene-load/config-change time, not read fresh every frame) would
        /// stay stuck on whatever it resolved to at that moment - typically its
        /// own local fallback value, since the host's very first publish (on
        /// <see cref="OnJoinedRoom"/>) and this client's own level load can race
        /// in either order. Re-running both reapply passes here closes that gap
        /// regardless of who won the race. The screen-shake/knockback/VFX
        /// spore-bomb-detonation tuning doesn't need this - those already read
        /// <c>Plugin.Cfg.Effective*</c> fresh at the moment of each detonation
        /// (<see cref="SporeBombs.SporeBombExplosionPatch"/>), so they self-heal
        /// the instant a property arrives with no caching to invalidate.
        /// </summary>
        public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
        {
            if (HostAuthority.IsHost)
            {
                return; // our own local values are already authoritative - nothing to refresh.
            }

            Diag.V("[HostAuthoritySync] room properties updated - reapplying cached wind/trigger-radius/spore-area state.");
            WindChillZoneTuningPatch.ReapplyAll();
            SporeBombCullPatch.ReapplyTriggerRadiusToAll();
            SporeAreaDisablePatch.ReapplyToAll();
        }
    }
}
