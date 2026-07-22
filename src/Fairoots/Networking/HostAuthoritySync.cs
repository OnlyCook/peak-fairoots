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
    }
}
