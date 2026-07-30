using Fairoots.Core;
using Fairoots.Core.Presets;
using Xunit;

namespace Fairoots.Tests
{
    /// <summary>
    /// The cover-mouth input state machine. Not seed-gated (it's a player action),
    /// so these are behavioural proofs: the two input modes behave as documented,
    /// and - the load-bearing part - the outside veto can always force a cover off
    /// and can never be latched around, in either mode.
    /// </summary>
    public class CoverMouthTests
    {
        private const bool Hold = true;
        private const bool Toggle = false;

        [Fact]
        public void HoldMode_FollowsTheKeyExactly()
        {
            Assert.True(CoverMouth.NextState(covering: false, keyPressedThisFrame: true, keyHeld: true, Hold, allowed: true));
            // Still held on a later frame, with no fresh key-down edge.
            Assert.True(CoverMouth.NextState(covering: true, keyPressedThisFrame: false, keyHeld: true, Hold, allowed: true));
            // Released.
            Assert.False(CoverMouth.NextState(covering: true, keyPressedThisFrame: false, keyHeld: false, Hold, allowed: true));
            Assert.False(CoverMouth.NextState(covering: false, keyPressedThisFrame: false, keyHeld: false, Hold, allowed: true));
        }

        [Fact]
        public void ToggleMode_FlipsOnEachPressAndIgnoresHolding()
        {
            // Press starts it...
            Assert.True(CoverMouth.NextState(covering: false, keyPressedThisFrame: true, keyHeld: true, Toggle, allowed: true));
            // ...holding the key down does not end it...
            Assert.True(CoverMouth.NextState(covering: true, keyPressedThisFrame: false, keyHeld: true, Toggle, allowed: true));
            // ...nor does releasing it...
            Assert.True(CoverMouth.NextState(covering: true, keyPressedThisFrame: false, keyHeld: false, Toggle, allowed: true));
            // ...only a second press does.
            Assert.False(CoverMouth.NextState(covering: true, keyPressedThisFrame: true, keyHeld: true, Toggle, allowed: true));
        }

        [Fact]
        public void ToggleMode_OnlyReactsToTheKeyDownEdge_NotTheHeldState()
        {
            // A held key must not re-toggle every frame (which would flicker the
            // cover on and off at framerate).
            bool covering = CoverMouth.NextState(false, keyPressedThisFrame: true, keyHeld: true, Toggle, allowed: true);
            for (int frame = 0; frame < 10; frame++)
            {
                covering = CoverMouth.NextState(covering, keyPressedThisFrame: false, keyHeld: true, Toggle, allowed: true);
            }

            Assert.True(covering);
        }

        [Theory]
        [InlineData(Hold)]
        [InlineData(Toggle)]
        public void NotAllowed_ForcesTheCoverOff_InBothModes(bool holdMode)
        {
            Assert.False(CoverMouth.NextState(covering: true, keyPressedThisFrame: false, keyHeld: true, holdMode, allowed: false));
        }

        [Theory]
        [InlineData(Hold)]
        [InlineData(Toggle)]
        public void NotAllowed_BlocksStarting_EvenOnAFreshPress(bool holdMode)
        {
            Assert.False(CoverMouth.NextState(covering: false, keyPressedThisFrame: true, keyHeld: true, holdMode, allowed: false));
        }

        [Fact]
        public void ToggleMode_CannotBeLatchedThroughAVeto()
        {
            // The trap this guards: if a veto (started climbing, ran out of stamina)
            // only suppressed the *effect* while leaving the toggle latched, the next
            // press would read as "start covering" while actually clearing a stuck
            // flag - so the player would press once and nothing would happen.
            bool covering = CoverMouth.NextState(false, keyPressedThisFrame: true, keyHeld: false, Toggle, allowed: true);
            Assert.True(covering);

            covering = CoverMouth.NextState(covering, keyPressedThisFrame: false, keyHeld: false, Toggle, allowed: false);
            Assert.False(covering);

            // One press after the veto lifts starts it again immediately.
            covering = CoverMouth.NextState(covering, keyPressedThisFrame: true, keyHeld: false, Toggle, allowed: true);
            Assert.True(covering);
        }

        [Fact]
        public void StaminaCostIsFramerateIndependent()
        {
            // 60fps for one second must cost the same as 144fps for one second.
            float at60 = 0f, at144 = 0f;
            for (int i = 0; i < 60; i++) at60 += CoverMouth.StaminaCost(0.03, 1f / 60f);
            for (int i = 0; i < 144; i++) at144 += CoverMouth.StaminaCost(0.03, 1f / 144f);

            Assert.Equal(0.03f, at60, 4);
            Assert.Equal(at60, at144, 4);
        }

        [Fact]
        public void StaminaCostIsZeroWhenDisabledOrTimeIsNotPassing()
        {
            Assert.Equal(0f, CoverMouth.StaminaCost(0.0, 0.016f));
            Assert.Equal(0f, CoverMouth.StaminaCost(-1.0, 0.016f));
            Assert.Equal(0f, CoverMouth.StaminaCost(0.03, 0f));
        }

        [Fact]
        public void MechanicIsOnForEveryPreset()
        {
            // ROADMAP.md's preset table: cover-mouth is available on all four, unlike
            // the climb-wind shelter (off on Subtle) - it costs stamina and both
            // hands, so it isn't a free win even at the lightest preset.
            foreach (PresetId preset in new[] { PresetId.Subtle, PresetId.Balanced, PresetId.Generous, PresetId.Tame, PresetId.Custom })
            {
                Assert.True(PresetCatalog.EnableCoverMouth(preset));
            }
        }
    }
}
