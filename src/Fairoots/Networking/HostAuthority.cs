using ExitGames.Client.Photon;
using Fairoots.Diagnostics;
using Photon.Pun;

namespace Fairoots.Networking
{
    /// <summary>
    /// Host authority (locked in 2026-07-22 - see ROADMAP.md's "Host authority"
    /// section and CODEBASE.md). Every setting that changes actual shared game
    /// logic - what spore bombs get removed, wind force/timing/item/backpack
    /// immunity, whether wind occurs at all - is decided by the host's config
    /// only; an individual (non-host) client's own local config for these is
    /// never used, no matter what they've set it to.
    ///
    /// <b>The rule is now the whole config file minus two sections</b> (maintainer's
    /// call, 2026-07-30): every setting outside <c>General</c> and <c>Debug</c> is
    /// host-authoritative, full stop. The earlier carve-out for "purely local
    /// per-player feel" (the wind-preceded-fall camera dampening clamp and its
    /// window) is gone - if a setting can change the biome, only the host's copy of
    /// it does, and a client's own value starts mattering the moment they become the
    /// host themselves. <c>General</c>'s genuinely cosmetic/local choices (label
    /// visibility, cloud opacity, the recolor, the cover-mouth keybind) and every
    /// <c>Debug</c> setting stay per-client by design. The two exceptions inside
    /// <c>General</c> are the seed and the preset, which are host-decided by
    /// definition - the seed is published directly below, and the preset needs no key
    /// of its own because every value it resolves to is published individually.
    ///
    /// Mechanism: the host publishes every host-authoritative resolved value as
    /// a Photon room custom property (<see cref="PublishAll"/>) whenever it
    /// changes (wired in <c>Plugin.cs</c>) or host authority itself might have
    /// changed (<see cref="HostAuthoritySync"/>'s join-room/master-switched
    /// callbacks). Every client's own <see cref="Resolve"/> calls read from
    /// there instead of their local config, unless they themselves are the
    /// host (in which case their local value already <em>is</em> the
    /// authoritative one, so there's nothing to override it with) or no room
    /// property has been published yet (solo play, or a brief window before
    /// the host's first publish - falls back to the local value rather than a
    /// meaningless default).
    ///
    /// This requires every client in the lobby to have Fairoots installed - a
    /// client without the mod has no code path that understands these custom
    /// room properties at all, so nothing here can affect them (Photon RPCs/
    /// properties don't invent behavior on a receiver that has no code for it -
    /// see ROADMAP.md for the full explanation of why "only the host needs to
    /// install it" isn't achievable for these specific mechanics, unlike wind
    /// gust timing, which vanilla itself already gates by
    /// <c>PhotonNetwork.IsMasterClient</c> and propagates via its own native RPC).
    /// </summary>
    internal static class HostAuthority
    {
        private const string KeyPrefix = "Fairoots.";

        internal static bool IsHost => PhotonNetwork.IsMasterClient;

        internal static int Resolve(string key, int localValue)
        {
            if (IsHost)
            {
                return localValue;
            }

            if (!TryGetRoomProperty(key, out object value) || !(value is int i))
            {
                LogFallback(key, value, localValue);
                return localValue;
            }

            return i;
        }

        internal static float Resolve(string key, float localValue)
        {
            if (IsHost)
            {
                return localValue;
            }

            if (!TryGetRoomProperty(key, out object value) || !(value is float f))
            {
                LogFallback(key, value, localValue);
                return localValue;
            }

            return f;
        }

        internal static double Resolve(string key, double localValue)
        {
            if (IsHost)
            {
                return localValue;
            }

            if (!TryGetRoomProperty(key, out object value) || !(value is double d))
            {
                LogFallback(key, value, localValue);
                return localValue;
            }

            return d;
        }

        internal static bool Resolve(string key, bool localValue)
        {
            if (IsHost)
            {
                return localValue;
            }

            if (!TryGetRoomProperty(key, out object value) || !(value is bool b))
            {
                LogFallback(key, value, localValue);
                return localValue;
            }

            return b;
        }

        /// <summary>
        /// Verbose-only trail for why a non-host client fell back to its own
        /// local value instead of the host's published one - either nothing has
        /// been published yet under this key (<paramref name="rawValue"/> is
        /// <c>null</c>), or something was published but under an unexpected CLR
        /// type (logs the actual type so a Photon serialization mismatch is
        /// visible instead of silently masquerading as "solo play fallback").
        /// </summary>
        private static void LogFallback(string key, object rawValue, object localValue)
        {
            if (!Diag.Enabled)
            {
                return;
            }

            string reason = rawValue == null
                ? "no room property published yet"
                : $"unexpected type {rawValue.GetType().Name} (value={rawValue})";
            Diag.V($"[HostAuthority] Resolve(\"{key}\") fell back to local value {localValue} - {reason}");
        }

        private static bool TryGetRoomProperty(string key, out object value)
        {
            value = null;
            var room = PhotonNetwork.CurrentRoom;
            return room != null && room.CustomProperties.TryGetValue(KeyPrefix + key, out value);
        }

        /// <summary>
        /// Publishes every host-authoritative resolved value to the room's
        /// custom properties in one batched write, so every other client's
        /// <see cref="Resolve"/> calls pick them up. No-op if we're not
        /// actually the host, or not in a Photon room at all (main menu). Cheap
        /// enough to call on every relevant config change and level load - one
        /// dictionary of already-resolved property reads plus a single network
        /// write, not a per-frame operation.
        /// </summary>
        internal static void PublishAll()
        {
            if (!IsHost)
            {
                Diag.V("[HostAuthority] PublishAll skipped - not the master client.");
                return;
            }

            var room = PhotonNetwork.CurrentRoom;
            if (room == null)
            {
                Diag.V("[HostAuthority] PublishAll skipped - not in a Photon room.");
                return;
            }

            var cfg = Plugin.Cfg;
            var props = new Hashtable
            {
                { KeyPrefix + "Seed", cfg.Seed.Value },
                { KeyPrefix + "SporeBombCullFraction", cfg.EffectiveSporeBombCullFraction },
                { KeyPrefix + "SporeBombTriggerRadiusMultiplier", cfg.EffectiveSporeBombTriggerRadiusMultiplier },
                { KeyPrefix + "SporeBombKnockbackMultiplier", cfg.EffectiveSporeBombKnockbackMultiplier },
                { KeyPrefix + "SporeBombScreenshakeRangeCapMeters", cfg.EffectiveSporeBombScreenshakeRangeCapMeters },
                { KeyPrefix + "SporeBombVfxCountMultiplier", cfg.EffectiveSporeBombVfxCountMultiplier },
                { KeyPrefix + "SporeBombTriggerHeightMultiplier", cfg.EffectiveSporeBombTriggerHeightMultiplier },
                { KeyPrefix + "DisableFoliageRemoval", cfg.EffectiveDisableFoliageRemoval },
                { KeyPrefix + "SporeBombSporeAreaRadiusMultiplier", cfg.EffectiveSporeBombSporeAreaRadiusMultiplier },
                { KeyPrefix + "WindForceMultiplier", cfg.EffectiveWindForceMultiplier },
                { KeyPrefix + "WindGustDurationMultiplier", cfg.EffectiveWindGustDurationMultiplier },
                { KeyPrefix + "WindItemForceMultiplier", cfg.EffectiveWindItemForceMultiplier },
                { KeyPrefix + "WindObstacleOcclusionRangeMultiplier", cfg.EffectiveWindObstacleOcclusionRangeMultiplier },
                { KeyPrefix + "WindBackpackAlwaysImmune", cfg.EffectiveWindBackpackAlwaysImmune },
                { KeyPrefix + "DisableWindEntirely", cfg.EffectiveDisableWindEntirely },
                { KeyPrefix + "WindFallCameraDampenClamp", cfg.EffectiveWindFallCameraDampenClamp },
                { KeyPrefix + "WindRecentForceWindowSeconds", cfg.EffectiveWindRecentForceWindowSeconds },
                { KeyPrefix + "PreventWindRagdoll", cfg.EffectivePreventWindRagdoll },
                { KeyPrefix + "DisableSporeAreas", cfg.EffectiveDisableSporeAreas },
                { KeyPrefix + "DisableZombies", cfg.EffectiveDisableZombies },
                { KeyPrefix + "ZombieSpeedMultiplier", cfg.EffectiveZombieSpeedMultiplier },
                { KeyPrefix + "BeetleSpeedMultiplier", cfg.EffectiveBeetleSpeedMultiplier },
                { KeyPrefix + "BeetleKnockbackMultiplier", cfg.EffectiveBeetleKnockbackMultiplier },
                { KeyPrefix + "CreatureRagdollMultiplier", cfg.EffectiveCreatureRagdollMultiplier },
                { KeyPrefix + "ZombieDeaggroEnabled", cfg.EffectiveZombieDeaggroEnabled },
                { KeyPrefix + "ZombieDeaggroMultiplier", cfg.EffectiveZombieDeaggroMultiplier },
                { KeyPrefix + "BeetleDeaggroMultiplier", cfg.EffectiveBeetleDeaggroMultiplier },
                { KeyPrefix + "ZombieKnockoutSeconds", cfg.EffectiveZombieKnockoutSeconds },
                { KeyPrefix + "BeetleKnockoutSeconds", cfg.EffectiveBeetleKnockoutSeconds },
                { KeyPrefix + "CreatureKnockoutMinThrowSpeed", cfg.EffectiveCreatureKnockoutMinThrowSpeed },
                { KeyPrefix + "CreatureKnockoutMaxThrowDistance", cfg.EffectiveCreatureKnockoutMaxThrowDistance },
                { KeyPrefix + "BlowgunAffectsCreatures", cfg.EffectiveBlowgunAffectsCreatures },
                { KeyPrefix + "BlowgunCreatureStunSeconds", cfg.EffectiveBlowgunCreatureStunSeconds },
                { KeyPrefix + "ZombieWindMultiplier", cfg.EffectiveZombieWindMultiplier },
                { KeyPrefix + "BeetleWindSusceptibility", cfg.EffectiveBeetleWindSusceptibility },
                { KeyPrefix + "DisableBeetles", cfg.EffectiveDisableBeetles },
                { KeyPrefix + "DisableSpiders", cfg.EffectiveDisableSpiders },
                { KeyPrefix + "SporeAreaRemovalFraction", cfg.EffectiveSporeAreaRemovalFraction },
                { KeyPrefix + "SporeAreaRadiusMultiplier", cfg.EffectiveSporeAreaRadiusMultiplier },
                { KeyPrefix + "SporeAreaStatusRateMultiplier", cfg.EffectiveSporeAreaStatusRateMultiplier },
                { KeyPrefix + "SporeClearTimeMultiplier", cfg.EffectiveSporeClearTimeMultiplier },
                { KeyPrefix + "SporeBuildUpMultiplier", cfg.EffectiveSporeBuildUpMultiplier },
                { KeyPrefix + "CoverMouthStaminaPerSecond", cfg.EffectiveCoverMouthStaminaPerSecond },
                { KeyPrefix + "DisableCoverMouth", cfg.EffectiveDisableCoverMouth },
                { KeyPrefix + "CoverMouthBlocksSporeBombs", cfg.EffectiveCoverMouthBlocksSporeBombs },
                { KeyPrefix + "ClimbSheltersFromWind", cfg.EffectiveClimbSheltersFromWind },
                { KeyPrefix + "ClimbWindSpeedMultiplier", cfg.EffectiveClimbWindSpeedMultiplier },
                { KeyPrefix + "ClimbWindUpwardSpeedMultiplier", cfg.EffectiveClimbWindUpwardSpeedMultiplier },
                { KeyPrefix + "ClimbWindIntoWindSpeedMultiplier", cfg.EffectiveClimbWindIntoWindSpeedMultiplier },
                { KeyPrefix + "ClimbWindGraceForceMultiplier", cfg.EffectiveClimbWindGraceForceMultiplier },
                { KeyPrefix + "ClimbShelterGraceSeconds", cfg.EffectiveClimbShelterGraceSeconds },
            };
            room.SetCustomProperties(props);
            Diag.V(
                $"[HostAuthority] PublishAll wrote {props.Count} propert(y/ies) to room " +
                $"\"{room.Name}\": windForce={cfg.EffectiveWindForceMultiplier:0.###}, " +
                $"shakeCap={cfg.EffectiveSporeBombScreenshakeRangeCapMeters:0.#}");
        }
    }
}
