using System;
using System.Reflection;
using ExitGames.Client.Photon;
using Fairoots.Core;
using Fairoots.Diagnostics;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using Zorro.Core; // Optionable<T> (Zorro.Core.Runtime.dll) - the game's own item-slot option type

namespace Fairoots.SporeAreas
{
    /// <summary>
    /// The cover-your-mouth counterplay mechanic (ROADMAP.md's "New: cover-mouth vs.
    /// spore areas" row), driven from <c>Plugin.Update</c>: reads the key, runs
    /// <see cref="CoverMouth.NextState"/>, charges stamina, frees the player's hands
    /// on start, and publishes the state so other clients can see it.
    ///
    /// <b>Per-client by design.</b> Each player decides for themselves whether their
    /// own mouth is covered - there is nothing here for a host to arbitrate, and the
    /// immunity it grants only ever affects the covering player (spore areas apply
    /// status to <c>Character.localCharacter</c> only, so suppressing one on this
    /// client can't affect anyone else's game). The one host-authoritative part is
    /// what it costs (<see cref="PluginConfig.EffectiveCoverMouthStaminaPerSecond"/>).
    ///
    /// <b>Replication</b> is a Photon <em>player</em> custom property rather than an
    /// RPC: it's a single boolean that changes at most a few times a second, Photon
    /// replicates player properties to everyone (including late joiners) with no
    /// receiving code needed beyond reading them, and it needs no PhotonView of our
    /// own to register an RPC against - the same technique
    /// <c>Networking/ModPresenceCheck</c> already uses for mod presence. Remote
    /// clients only need it for the pose (see <c>CoverMouthPosePatch</c>); nothing
    /// about the immunity or the restrictions depends on the network at all, so a
    /// dropped/late property update can never desync gameplay, only the visuals.
    /// </summary>
    internal static class CoverMouthController
    {
        private const string CoveringProperty = "Fairoots.CoveringMouth";

        /// <summary>
        /// Whether the local player's mouth is covered right now. Read by the
        /// immunity and restriction patches - the single source of truth for
        /// "are my hands over my mouth."
        /// </summary>
        internal static bool LocalCovering { get; private set; }

        /// <summary>
        /// <c>Character.UseStamina</c> is <c>internal</c> in Assembly-CSharp, so it
        /// can't be called directly from this assembly. Resolved once via Harmony's
        /// <see cref="AccessTools"/> rather than per frame.
        ///
        /// <b>Matched on its leading <c>(float, bool)</c> parameters, not an exact
        /// signature.</b> PEAK 2.0.a appended a third optional parameter
        /// (<c>bool ignoreAscents = false</c>), which made the previous exact
        /// <c>(float, bool)</c> lookup return <c>null</c> - covering your mouth
        /// silently became free. The game keeps appending optional parameters to
        /// this method, so pinning the full signature is guaranteed to break again;
        /// matching the prefix and letting <see cref="BuildUseStaminaArgs"/> fill
        /// whatever follows with the game's own declared defaults survives that.
        /// </summary>
        private static readonly MethodInfo UseStaminaMethod = ResolveUseStamina();

        private static MethodInfo ResolveUseStamina()
        {
            foreach (var candidate in AccessTools.GetDeclaredMethods(typeof(Character)))
            {
                if (candidate.Name != "UseStamina")
                {
                    continue;
                }

                var parameters = candidate.GetParameters();
                if (parameters.Length >= 2
                    && parameters[0].ParameterType == typeof(float)
                    && parameters[1].ParameterType == typeof(bool))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// The argument array for <see cref="UseStaminaMethod"/>: the two the mod
        /// actually cares about, then each remaining parameter at the default the
        /// game itself declares for it - so an added optional parameter keeps
        /// vanilla behavior rather than needing a code change here.
        /// </summary>
        private static object[] BuildUseStaminaArgs(float cost, bool useBonusStamina)
        {
            var parameters = UseStaminaMethod.GetParameters();
            var args = new object[parameters.Length];
            args[0] = cost;
            args[1] = useBonusStamina;

            for (int i = 2; i < parameters.Length; i++)
            {
                args[i] = parameters[i].HasDefaultValue
                    ? parameters[i].DefaultValue
                    : (parameters[i].ParameterType.IsValueType
                        ? Activator.CreateInstance(parameters[i].ParameterType)
                        : null);
            }

            return args;
        }

        private static bool _loggedMissingUseStamina;

        /// <summary>
        /// Polled every frame from <c>Plugin.Update</c> (not a
        /// <c>MonoBehaviour</c> of its own - it needs to run exactly once per frame
        /// for the local character only, which is what the plugin's own
        /// <c>Update</c> already is).
        /// </summary>
        internal static void Tick()
        {
            try
            {
                var cfg = Plugin.Cfg;
                KeyCode key = cfg.CoverMouthKey.Value;
                var character = Character.localCharacter;

                // The host-authoritative master switch and the per-player "None" keybind
                // both land here: one means the mechanic doesn't exist for the lobby
                // (vanilla, or a preset/Custom value that leaves it off), the other opts
                // this player out. Either way an active cover is dropped immediately
                // rather than left latched.
                // Three ways out, all of which drop an active cover rather than leave
                // it latched: the biome is gone (RootsState - this mechanic exists to
                // counter Roots spore areas, so it has no business being available on
                // the rest of the mountain, and dropping it here is also what makes
                // every cover-mouth patch downstream go inert, since they all key off
                // LocalCovering), the host-authoritative master switch is off, or this
                // player has unbound the key.
                if (!RootsState.Active || key == KeyCode.None || character == null || !cfg.EffectiveEnableCoverMouth)
                {
                    SetCovering(false, character, !RootsState.Active
                        ? "not in the Roots biome"
                        : !cfg.EffectiveEnableCoverMouth
                            ? "not enabled by host"
                            : "no keybind set, or no local character");
                    return;
                }

                bool allowed = IsAllowed(character, out string blockedReason);
                bool next = CoverMouth.NextState(
                    LocalCovering,
                    Input.GetKeyDown(key),
                    Input.GetKey(key),
                    cfg.CoverMouthHold.Value,
                    allowed);

                if (next != LocalCovering)
                {
                    SetCovering(next, character, next ? "key" : (allowed ? "key" : blockedReason));
                }

                if (LocalCovering)
                {
                    DrainStamina(character);
                }
            }
            catch (Exception e)
            {
                Diag.Error($"[CoverMouth] Tick threw: {e.GetType().Name}: {e.Message}");
                LocalCovering = false;
            }
        }

        /// <summary>
        /// Whether the local player is in a state where covering their mouth makes
        /// sense at all. Climbing is in here rather than in the restriction patches
        /// for a reason: the restrictions stop a covering player from *starting* a
        /// climb, and this stops a climbing player from starting to cover. Doing it
        /// the other way round - letting the cover begin and dropping the player off
        /// the wall - would turn a defensive button into a fall, which is the exact
        /// opposite of what the mechanic is for.
        /// </summary>
        private static bool IsAllowed(Character character, out string reason)
        {
            reason = string.Empty;

            if (character.data.dead || character.data.fullyPassedOut || !character.data.fullyConscious)
            {
                reason = "unconscious or dead";
                return false;
            }

            if (character.data.isClimbing || character.data.isRopeClimbing || character.data.isVineClimbing
                || character.data.currentClimbHandle != null)
            {
                reason = "holding onto something (climbing)";
                return false;
            }

            if (character.refs.afflictions.isWebbed)
            {
                reason = "webbed";
                return false;
            }

            // Out of stamina ends a cover: the drain has to actually be payable, or
            // "it costs stamina" would be decoration. Checked as a *state* rather than
            // by trusting UseStamina's return value. Inlines the game's own
            // OutOfStamina() thresholds (it's `internal`, and its two backing fields are
            // public) rather than reflecting into it - two float comparisons per frame
            // are not worth a reflective call.
            //
            // Whether the bonus-stamina reserve counts is the player's own choice: with
            // it off (the default) a cover ends the moment ordinary stamina is gone,
            // leaving the bonus for climbing.
            if (Plugin.Cfg.EffectiveCoverMouthStaminaPerSecond > 0f
                && character.data.currentStamina < 0.005f
                && (!Plugin.Cfg.CoverMouthUseBonusStamina.Value || character.data.extraStamina < 0.001f))
            {
                reason = "out of stamina";
                return false;
            }

            if (GUIManager.instance != null
                && (GUIManager.instance.windowBlockingInput || GUIManager.instance.wheelActive))
            {
                reason = "a menu is open";
                return false;
            }

            return true;
        }

        private static void SetCovering(bool covering, Character character, string reason)
        {
            if (covering == LocalCovering)
            {
                return;
            }

            LocalCovering = covering;

            if (covering && character != null)
            {
                FreeHands(character);
            }

            Publish(covering);
            Diag.V(
                $"[CoverMouth] local player {(covering ? "covered" : "uncovered")} their mouth ({reason}); " +
                $"{CoverMouthImmunityPatch.ParkedCount} spore area(s) with parked tick progress");
        }

        /// <summary>
        /// Empties the player's hands as the cover starts - the maintainer's
        /// requirement that this occupies both hands, made literal. An item held from
        /// a real inventory slot is <em>put away</em> (the game's own
        /// <c>EquipSlot(None)</c>), which is reversible and loses nothing; an item in
        /// the temporary fourth "in your hands" slot has no pocket to go back to, so
        /// the game's own equip path would drop it anyway - this drops it explicitly
        /// (via the game's own <c>DropItemRpc</c>, so every client agrees where it
        /// landed) rather than leaving it to a side effect.
        /// </summary>
        private static void FreeHands(Character character)
        {
            var items = character.refs.items;
            if (items == null || character.data.currentItem == null || !items.currentSelectedSlot.IsSome)
            {
                return;
            }

            byte slot = items.currentSelectedSlot.Value;
            if (slot == TemporarySlotId)
            {
                DropTemporaryItem(character, items);
                return;
            }

            items.EquipSlot(Optionable<byte>.None);
            Diag.V($"[CoverMouth] put away held item from slot {slot} to free both hands");
        }

        /// <summary>
        /// The game's own id for the temporary "carried in your hands" slot - the
        /// fourth item a player can hold beyond their three pockets and backpack
        /// (<c>TemporaryItemSlot(250)</c> in Assembly-CSharp; also the id its own
        /// wall-stow-accident handler drops from).
        /// </summary>
        private const byte TemporarySlotId = 250;

        private static void DropTemporaryItem(Character character, CharacterItems items)
        {
            var slot = character.player.GetItemSlot(TemporarySlotId);
            if (slot == null)
            {
                return;
            }

            // Drop it where it already is, with no velocity - the item is in the
            // player's hands, so its own transform is exactly "in front of the
            // player" without needing the character's (internal) bodypart lookup, and
            // a thrown-clear drop would be wrong anyway: this isn't a throw, the
            // player just needs the hand.
            Vector3 position = character.data.currentItem.transform.position;
            character.photonView.RPC(
                "DropItemRpc",
                RpcTarget.All,
                0f,
                TemporarySlotId,
                position,
                Vector3.zero,
                character.data.currentItem.transform.rotation,
                slot.data,
                true);
            Diag.V("[CoverMouth] dropped the temporarily-held (fourth) item to free both hands");
        }

        private static void DrainStamina(Character character)
        {
            float cost = CoverMouth.StaminaCost(Plugin.Cfg.EffectiveCoverMouthStaminaPerSecond, Time.deltaTime);
            if (cost <= 0f)
            {
                return;
            }

            if (UseStaminaMethod == null)
            {
                if (!_loggedMissingUseStamina)
                {
                    _loggedMissingUseStamina = true;
                    Diag.Warn("[CoverMouth] Character.UseStamina not found - covering your mouth will cost no stamina. Game update?");
                }

                return;
            }

            // The second argument is the game's own useBonusStamina flag.
            UseStaminaMethod.Invoke(
                character, BuildUseStaminaArgs(cost, Plugin.Cfg.CoverMouthUseBonusStamina.Value));
        }

        // --- Replication --------------------------------------------------

        private static void Publish(bool covering)
        {
            var self = PhotonNetwork.LocalPlayer;
            if (self == null || !PhotonNetwork.InRoom)
            {
                return;
            }

            self.SetCustomProperties(new Hashtable { { CoveringProperty, covering } });
        }

        /// <summary>
        /// Whether <em>any</em> character (local or remote) is currently covering
        /// their mouth - the local player from our own state, everyone else from the
        /// replicated player property. Used by the pose so covering is visible to the
        /// rest of the lobby, never for gameplay (see the class remarks).
        /// </summary>
        internal static bool IsCovering(Character character)
        {
            if (character == null)
            {
                return false;
            }

            if (character == Character.localCharacter)
            {
                return LocalCovering;
            }

            var owner = character.photonView != null ? character.photonView.Owner : null;
            if (owner == null)
            {
                return false;
            }

            return owner.CustomProperties.TryGetValue(CoveringProperty, out object value) && value is bool b && b;
        }

        /// <summary>
        /// Re-publishes the current state on joining a room, so a player who joins
        /// mid-cover (or whose property write happened while roomless) isn't shown
        /// with their hands down. Called from <c>HostAuthoritySync.OnJoinedRoom</c>.
        /// </summary>
        internal static void RepublishOnJoin() => Publish(LocalCovering);
    }
}
