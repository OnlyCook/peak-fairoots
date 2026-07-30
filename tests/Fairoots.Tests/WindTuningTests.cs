using Fairoots.Core;
using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// Pure arithmetic tests for the wind force/duration/item/occlusion
    /// scaling and the wind-preceded-fall camera-dampening decision (ROADMAP.md
    /// Phase 5 preset table rows) - no Unity/BepInEx dependency needed, mirroring
    /// <see cref="SporeBombExplosionTuningTests"/> since none of this is
    /// seed-gated (see <see cref="WindTuning"/>'s remarks).
    /// </summary>
    public class WindTuningTests
    {
        [Fact]
        public void ScaleWindForce_VanillaMultiplier_IsUnchanged()
        {
            Assert.Equal(20f, WindTuning.ScaleWindForce(20f, 1.0));
        }

        [Fact]
        public void ScaleWindForce_AppliesMultiplier()
        {
            Assert.Equal(16f, WindTuning.ScaleWindForce(20f, 0.8), 3);
        }

        [Fact]
        public void ScaleWindActiveDuration_AppliesMultiplier()
        {
            Assert.Equal(7f, WindTuning.ScaleWindActiveDuration(10f, 0.7), 3);
        }

        [Fact]
        public void ScaleWindActiveDuration_ZeroMultiplier_NeverCollapsesToZero()
        {
            // A genuinely zero-length "on" phase breaks the native wind on/off
            // timer (see MinWindActiveDurationSeconds's remarks) - this is the
            // exact live-reported regression, so this floor must hold.
            float result = WindTuning.ScaleWindActiveDuration(10f, 0.0);
            Assert.True(result >= WindTuning.MinWindActiveDurationSeconds);
            Assert.Equal(WindTuning.MinWindActiveDurationSeconds, result);
        }

        [Fact]
        public void ScaleWindActiveDuration_NearZeroMultiplier_StillFloored()
        {
            float result = WindTuning.ScaleWindActiveDuration(10f, 0.01);
            Assert.Equal(WindTuning.MinWindActiveDurationSeconds, result);
        }

        [Fact]
        public void ScaleWindRestDuration_LowerMultiplier_MeansLongerCalm()
        {
            // A gentler wind-force/frequency multiplier (0.5) should double the calm period.
            Assert.Equal(20f, WindTuning.ScaleWindRestDuration(10f, 0.5), 3);
        }

        [Fact]
        public void ScaleWindRestDuration_NonPositiveMultiplier_LeavesVanillaAlone()
        {
            Assert.Equal(10f, WindTuning.ScaleWindRestDuration(10f, 0.0));
        }

        [Fact]
        public void ScaleItemForceFactor_ZeroMultiplier_ZerosItOut()
        {
            Assert.Equal(0f, WindTuning.ScaleItemForceFactor(1f, 0.0));
        }

        [Fact]
        public void ScaleItemForceFactor_AppliesMultiplier()
        {
            Assert.Equal(0.4f, WindTuning.ScaleItemForceFactor(1f, 0.4), 3);
        }

        [Fact]
        public void ScaleRaycastDistance_WidensRange()
        {
            Assert.Equal(6.5f, WindTuning.ScaleRaycastDistance(5f, 1.3), 3);
        }

        [Fact]
        public void IsWindForceStillRecent_NeverRecorded_IsNotRecent()
        {
            Assert.False(WindTuning.IsWindForceStillRecent(lastWindForceTime: -1f, currentTime: 10f, windowSeconds: 1.5f));
        }

        [Fact]
        public void IsWindForceStillRecent_WellWithinWindow_IsRecent()
        {
            Assert.True(WindTuning.IsWindForceStillRecent(lastWindForceTime: 9f, currentTime: 10f, windowSeconds: 1.5f));
        }

        [Fact]
        public void IsWindForceStillRecent_ExactlyAtWindowBoundary_CountsAsRecent()
        {
            Assert.True(WindTuning.IsWindForceStillRecent(lastWindForceTime: 0f, currentTime: 1.5f, windowSeconds: 1.5f));
        }

        [Fact]
        public void IsWindForceStillRecent_PastWindow_NoLongerRecent()
        {
            Assert.False(WindTuning.IsWindForceStillRecent(lastWindForceTime: 0f, currentTime: 1.51f, windowSeconds: 1.5f));
        }

        [Fact]
        public void ApplyFallCameraDampening_NotWindPreceded_LeavesVanillaAlone()
        {
            Assert.Equal(0f, WindTuning.ApplyFallCameraDampening(vanillaTargetRagdollControl: 0f, fallIsWindPreceded: false, dampenClampValue: 0.5f));
        }

        [Fact]
        public void ApplyFallCameraDampening_ClampDisabled_LeavesVanillaAlone()
        {
            Assert.Equal(0f, WindTuning.ApplyFallCameraDampening(vanillaTargetRagdollControl: 0f, fallIsWindPreceded: true, dampenClampValue: 0f));
        }

        [Fact]
        public void ApplyFallCameraDampening_WindPrecededFall_RaisesFloor()
        {
            Assert.Equal(0.35f, WindTuning.ApplyFallCameraDampening(vanillaTargetRagdollControl: 0f, fallIsWindPreceded: true, dampenClampValue: 0.35f));
        }

        [Fact]
        public void ApplyFallCameraDampening_NeverLowersAnAlreadyHigherVanillaValue()
        {
            // e.g. the carrier/passed-out branches already returning >= the clamp.
            Assert.Equal(1f, WindTuning.ApplyFallCameraDampening(vanillaTargetRagdollControl: 1f, fallIsWindPreceded: true, dampenClampValue: 0.35f));
        }

        // --- prevent-wind-ragdoll (Wind/prevent-wind-ragdoll, 2026-07-30) --------

        [Fact]
        public void ApplyWindRagdollImmunity_Disabled_LeavesVanillaRagdollAlone()
        {
            // Off is the vanilla contract the setting promises: wind blowing you off
            // an edge ragdolls you exactly as the game would.
            Assert.Equal(0f, WindTuning.ApplyWindRagdollImmunity(
                vanillaTargetRagdollControl: 0f, fallIsWindPreceded: true, immunityEnabled: false));
        }

        [Fact]
        public void ApplyWindRagdollImmunity_OrdinaryFall_LeavesVanillaRagdollAlone()
        {
            // Scoped to wind-preceded falls only - walking off a ledge yourself is
            // still your own doing, same scoping as the camera clamp.
            Assert.Equal(0f, WindTuning.ApplyWindRagdollImmunity(
                vanillaTargetRagdollControl: 0f, fallIsWindPreceded: false, immunityEnabled: true));
        }

        [Fact]
        public void ApplyWindRagdollImmunity_WindPrecededFall_GivesFullControl()
        {
            Assert.Equal(WindTuning.FullRagdollControl, WindTuning.ApplyWindRagdollImmunity(
                vanillaTargetRagdollControl: 0f, fallIsWindPreceded: true, immunityEnabled: true));
        }

        [Fact]
        public void ApplyWindRagdollImmunity_ThenClamp_ImmunityWins()
        {
            // How the patch actually composes the two (immunity first, then the
            // clamp): with immunity on, the partial clamp can never claw control
            // back down, no matter which preset's value is in play.
            float result = WindTuning.ApplyWindRagdollImmunity(
                vanillaTargetRagdollControl: 0f, fallIsWindPreceded: true, immunityEnabled: true);
            result = WindTuning.ApplyFallCameraDampening(result, fallIsWindPreceded: true, dampenClampValue: 0.35f);

            Assert.Equal(WindTuning.FullRagdollControl, result);
        }

        [Fact]
        public void ApplyWindRagdollImmunity_Off_StillLeavesTheClampWorking()
        {
            // The other half of the same composition: turning immunity off must not
            // take the pre-existing partial dampening with it.
            float result = WindTuning.ApplyWindRagdollImmunity(
                vanillaTargetRagdollControl: 0f, fallIsWindPreceded: true, immunityEnabled: false);
            result = WindTuning.ApplyFallCameraDampening(result, fallIsWindPreceded: true, dampenClampValue: 0.35f);

            Assert.Equal(0.35f, result);
        }
    }
}
