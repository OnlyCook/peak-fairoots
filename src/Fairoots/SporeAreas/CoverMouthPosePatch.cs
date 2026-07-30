using System.Collections.Generic;
using System.Linq;
using Fairoots.Diagnostics;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace Fairoots.SporeAreas
{
    /// <summary>
    /// The visible half of the cover-your-mouth mechanic: both hands come up over the
    /// player's mouth, on their own screen and on everyone else's.
    ///
    /// <b>Three game systems, each doing the part it's good at.</b>
    /// <list type="number">
    /// <item><b>Hand and finger shape is borrowed from an existing emote clip</b> - the
    /// one the game labels "it's so over" (<see cref="DefaultPoseState"/>). Finger curl
    /// is otherwise unreachable from a Harmony mod: it's animation data, and the arm IK
    /// below only solves three bones. The maintainer's verdict on the earlier version
    /// that guessed a wrist rotation instead: "looks like I am choking myself, with
    /// vertically flipped hands."</item>
    /// <item><b>But the clip is never left playing.</b> Its wrist/finger rotations are
    /// captured once (<see cref="TryCapturePose"/>) and re-applied per frame afterwards,
    /// so the emote's <em>legs and head motion never happen</em> - the maintainer's
    /// requirement, and simultaneously the fix for the emote visibly failing to reset
    /// when the key was released. Walking, looking around and everything else keep their
    /// normal animation; only the wrist and finger bones are overwritten.</item>
    /// <item><b>Arm IK puts the hands where they belong.</b> The clip's hands are apart
    /// and at chest height; the pose needs them together over the mouth, one above the
    /// other so they don't intersect. Both hand IK targets are placed on the look axis
    /// with a vertical gap, and each target is <em>also</em> given the captured wrist
    /// orientation (see <see cref="_leftWristBodyRelative"/>), so the solver aims the
    /// hand rather than anything editing its output afterwards.</item>
    /// </list>
    ///
    /// The IK seam: PEAK rigs both arms with a <c>TwoBoneIKConstraint</c>
    /// (<c>refs.ikLeft</c>/<c>ikRight</c>) plus a world-space target transform per hand,
    /// driven from <c>CharacterAnimations.HandleIK</c> (weights, from <c>Update</c>) and
    /// <c>ConfigureIK</c> (target positions, from <c>CharacterRagdoll.FixedUpdate</c>,
    /// immediately after the animator graph is evaluated - which is also why the captured
    /// finger rotations are written there: it's the one point where the animator has
    /// finished writing the frame's pose and nothing has consumed it yet - and fingers,
    /// unlike the wrist, are outside the IK chain, so writing them can't feed back into
    /// the solver).
    ///
    /// <b>Why the pose shows up for remote players too:</b> neither method is gated to
    /// the local character, so the same postfixes pose whichever characters
    /// <see cref="CoverMouthController.IsCovering"/> reports - the local player from its
    /// own state, everyone else from the replicated Photon player property. No custom
    /// animation networking: one bool per player, and each client poses its own copies.
    ///
    /// <b>Why not an avatar-masked animator layer</b> (the obvious alternative for "arms
    /// only"): the <c>Scout</c> controller does have arm-ish layers (<c>Throw</c>,
    /// <c>Item</c>, <c>Reach</c>, <c>Point</c>), but the emote state exists only on the
    /// base layer, and a state can't be played on a layer whose state machine doesn't
    /// contain it. Capturing the pose sidesteps layers entirely.
    /// </summary>
    internal static class CoverMouthPose
    {
        /// <summary>
        /// The animator state whose hand pose this mechanic borrows: the emote the game
        /// labels <b>"it's so over"</b> on the second page of the emote wheel, which the
        /// maintainer identified as having the right hand and finger shape.
        ///
        /// Confirmed at runtime (2026-07-27), and it could not have been guessed: emotes
        /// are Unity <em>asset</em> data (<c>EmoteWheelData</c> ScriptableObjects), so no
        /// clip name appears anywhere in the decompiled code, and the wheel displays
        /// <c>LocalizedText.GetText(emoteName)</c> rather than the key - so the emote a
        /// player knows as "it's so over" is stored under the unrelated key <c>Defeat</c>.
        /// Matching on the phrase found nothing; <see cref="EmotePlayedProbe"/>, which
        /// logs whatever emote the player triggers, is what actually resolved it.
        /// </summary>
        private const string DefaultPoseState = "A_Scout_Emote_Defeat";

        private static readonly AccessTools.FieldRef<CharacterAnimations, Character> CharacterRef =
            AccessTools.FieldRefAccess<CharacterAnimations, Character>("character");

        private static readonly int AnimEmote = Animator.StringToHash("Emote");

        /// <summary>Constraints whose rotation weight this patch has forced, with the value to put back.</summary>
        private static readonly Dictionary<int, float> OverriddenRotationWeights = new Dictionary<int, float>();

        /// <summary>
        /// The captured finger pose: local rotations of every bone <em>under</em> each
        /// hand bone, <b>keyed by bone name</b> (the hand bone itself is excluded - see
        /// <see cref="_leftWristBodyRelative"/>). Local rotations are the right
        /// representation because a finger joint's rotation is relative to its parent, so
        /// the shape holds no matter where the arm ends up.
        ///
        /// Keyed by name rather than by depth-first index because the bone chain under a
        /// hand is not the same on every character or in every session: a held item,
        /// cosmetic or attachment parented under a hand adds children. An index-based
        /// version silently skipped a whole hand whenever the counts disagreed, which is
        /// the "leaves one hand behind" the maintainer reported. Matching by name applies
        /// whatever it recognises and ignores the rest.
        /// </summary>
        private static Dictionary<string, Quaternion> _leftFingerPose;

        private static Dictionary<string, Quaternion> _rightFingerPose;

        /// <summary>
        /// The captured wrist orientation, stored <b>relative to the body</b> (the
        /// animated rig's root) rather than to the forearm, and applied through the IK
        /// target rather than written to the bone.
        ///
        /// Both of those are corrections of the first attempt, which stored the wrist's
        /// local (forearm-relative) rotation and wrote it onto the bone after the solve.
        /// That failed twice over, exactly as reported from the playtest:
        /// <list type="bullet">
        /// <item><b>Flipped hands.</b> IK puts the forearm somewhere quite different from
        /// where the emote had it, so replaying the same forearm-relative rotation gives a
        /// different world orientation - differently per arm, which is why one hand came
        /// out merely inverted and the other also had its fingers pointing down.</item>
        /// <item><b>Twitching arms and wrists "collapsing".</b> Writing the hand bone
        /// after the rig had solved meant the next solve started from a pose this patch
        /// had already modified - a feedback loop between the solver and the override,
        /// which oscillates. Driving the wrist through the constraint's own target
        /// rotation removes the loop: the solver is told what to aim for, once, and
        /// nothing edits its output.</item>
        /// </list>
        /// </summary>
        private static Quaternion _leftWristBodyRelative = Quaternion.identity;

        private static Quaternion _rightWristBodyRelative = Quaternion.identity;

        /// <summary>How long after the local character appears to wait before capturing (see <see cref="Prewarm"/>).</summary>
        private const float PrewarmDelaySeconds = 3f;

        private const float PrewarmRetrySeconds = 2f;

        private const int MaxPrewarmAttempts = 5;

        private static float _nextPrewarmAttempt;
        private static int _prewarmAttempts;
        private static bool _captureFailed;
        private static bool _layersLogged;
        private static string _poseState;
        private static bool _poseStateResolved;

        private static bool HasCapturedPose => _leftFingerPose != null && _rightFingerPose != null;

        /// <summary>
        /// Whether this character should be posed right now: because they're actually
        /// covering their mouth, or - local player only - because the
        /// <c>Debug/cover-mouth-pose-preview</c> tuning toggle is holding the pose on.
        ///
        /// The preview deliberately lives <em>here</em>, in the visual layer, and not in
        /// <see cref="CoverMouthController"/>: it must not grant immunity, drain stamina,
        /// tie up the player's hands or be published to other clients. It exists so the
        /// pose offsets can be dialled in against a live character without holding a key
        /// down while alt-tabbing to a config editor - which is how every value in this
        /// pose has had to be found, since none of it can be derived from code.
        /// </summary>
        private static bool ShouldPose(Character character)
        {
            if (!RootsState.Active)
            {
                return false;
            }

            if (CoverMouthController.IsCovering(character))
            {
                return true;
            }

            return Plugin.Cfg.CoverMouthPosePreview.Value && character == Character.localCharacter;
        }

        /// <summary>Drops the cached clip choice and captured pose so changed Debug settings take effect without a restart.</summary>
        internal static void InvalidateEmote()
        {
            _poseStateResolved = false;
            _poseState = null;
            _leftFingerPose = null;
            _rightFingerPose = null;
            _captureFailed = false;
            _nextPrewarmAttempt = 0f;
            _prewarmAttempts = 0;
        }

        /// <summary>
        /// The animator state to capture from: the <c>Debug</c> override if set, else
        /// <see cref="DefaultPoseState"/>. Validated against the animator, because an
        /// unknown state name makes <c>Animator.Play</c> a silent no-op - exactly what
        /// happened when an emote's on-screen label was pasted into the config, leaving no
        /// hand pose at all and nothing explaining why.
        /// </summary>
        private static string ResolvePoseState(Animator animator)
        {
            if (_poseStateResolved)
            {
                return _poseState;
            }

            _poseStateResolved = true;

            string configured = Plugin.Cfg.CoverMouthPoseEmote.Value;
            bool overridden = !string.IsNullOrWhiteSpace(configured);
            _poseState = overridden ? configured.Trim() : DefaultPoseState;

            if (animator != null && !animator.HasState(0, Animator.StringToHash(_poseState)))
            {
                Diag.Warn(
                    $"[CoverMouthPose] animator has no state \"{_poseState}\" - the pose will have no hand/finger " +
                    "shape (arm IK only). It needs an animator state name like \"A_Scout_Emote_Defeat\", not an " +
                    "emote's on-screen label; play an emote in-game to have its real name logged.");
                LogEmoteCatalogue();
                _poseState = null;
                return null;
            }

            Diag.V($"[CoverMouthPose] capturing hand pose from \"{_poseState}\"{(overridden ? " (from config)" : " - the \"it's so over\" emote")}");
            return _poseState;
        }

        /// <summary>
        /// Dumps every emote with its key, <em>on-screen label</em> and animator state.
        /// The label is the load-bearing column: it's what a player reads off the wheel,
        /// and the keys don't resemble the labels (see <see cref="DefaultPoseState"/>).
        /// </summary>
        private static void LogEmoteCatalogue()
        {
            var candidates = Resources.FindObjectsOfTypeAll<EmoteWheelData>();
            if (candidates == null || candidates.Length == 0)
            {
                Diag.Warn("[CoverMouthPose] no EmoteWheelData found to list.");
                return;
            }

            Diag.Info(
                "[CoverMouthPose] available emotes (key | on-screen label | animator state): " +
                string.Join("; ", candidates.Select(c =>
                    $"{c.emoteName} | \"{LocalizedText.GetText(c.emoteName, printDebug: false)}\" | {c.anim}")));
        }

        /// <summary>
        /// Forces a constraint's target-rotation weight to 1 while covering, and puts the
        /// original back afterwards, so the solver drives the wrist to the orientation
        /// this patch asks for (see <see cref="_leftWristBodyRelative"/>) instead of the
        /// wrist being left to whatever the current animation happens to want.
        /// <c>data</c> is a struct property, hence read-modify-write.
        /// </summary>
        private static void ApplyRotationWeight(TwoBoneIKConstraint constraint, bool covering)
        {
            if (constraint == null)
            {
                return;
            }

            int id = constraint.GetInstanceID();
            var data = constraint.data;

            if (covering)
            {
                if (!OverriddenRotationWeights.ContainsKey(id))
                {
                    OverriddenRotationWeights[id] = data.targetRotationWeight;
                }

                if (data.targetRotationWeight != 1f)
                {
                    data.targetRotationWeight = 1f;
                    constraint.data = data;
                }

                return;
            }

            if (OverriddenRotationWeights.TryGetValue(id, out float original))
            {
                OverriddenRotationWeights.Remove(id);
                data.targetRotationWeight = original;
                constraint.data = data;
            }
        }

        /// <summary>Every bone from a hand bone down (the hand itself plus all finger joints), depth-first.</summary>
        private static void CollectHandBones(Transform root, List<Transform> into)
        {
            into.Add(root);
            for (int i = 0; i < root.childCount; i++)
            {
                CollectHandBones(root.GetChild(i), into);
            }
        }

        /// <summary>
        /// Captures the emote's wrist and finger rotations <b>synchronously and in
        /// isolation</b>, which is the whole trick: the pose must come out identical in
        /// every session, on every character, whatever the animator was doing when the
        /// player first covered their mouth.
        ///
        /// The first version failed exactly there (reported 2026-07-27: values tuned in
        /// one session "completely broke" after a restart). It asked for the clip and read
        /// the bones a frame later, so what it captured was the clip <em>blended with
        /// whatever else was playing</em> - the session's randomly-chosen idle state, plus
        /// the six other animator layers (<c>Throw</c>, <c>Item</c>, <c>Head</c>,
        /// <c>Reach</c>, <c>Point</c>, <c>Myres Music</c>), which all run at weight 1 and
        /// also write the arms. The captured pose therefore depended on the character's
        /// mood at that instant, and so did every pose offset tuned against it.
        ///
        /// So this instead: mute every other layer, play the clip, force an immediate
        /// evaluation with <c>Animator.Update(0)</c> rather than waiting a frame, read the
        /// bones, then put the animator back exactly as it was - layer weights, previous
        /// state and its normalised time - and evaluate again so not even a single frame
        /// of the emote is visible. Nothing about the result depends on timing or on what
        /// the character was doing.
        /// </summary>
        private static bool TryCapturePose(Character character)
        {
            var refs = character.refs;
            Transform leftTip = refs.ikLeft != null ? refs.ikLeft.data.tip : null;
            Transform rightTip = refs.ikRight != null ? refs.ikRight.data.tip : null;
            Animator animator = refs.animator;
            Transform body = BodyRoot(character);

            if (leftTip == null || rightTip == null || animator == null || !animator.isInitialized || body == null)
            {
                // Transient during spawn - the caller retries rather than giving up.
                return false;
            }

            string state = ResolvePoseState(animator);
            if (string.IsNullOrEmpty(state))
            {
                _captureFailed = true;
                return false;
            }

            LogAnimatorLayersOnce(animator);

            // Remember exactly where the animator was, so it can be put back.
            var previous = animator.GetCurrentAnimatorStateInfo(0);
            var layerWeights = new float[animator.layerCount];
            for (int i = 1; i < animator.layerCount; i++)
            {
                layerWeights[i] = animator.GetLayerWeight(i);
                animator.SetLayerWeight(i, 0f);
            }

            try
            {
                animator.Play(state, 0, Plugin.Cfg.CoverMouthPoseEmoteTime.Value);
                animator.Update(0f);

                _leftWristBodyRelative = Quaternion.Inverse(body.rotation) * leftTip.rotation;
                _rightWristBodyRelative = Quaternion.Inverse(body.rotation) * rightTip.rotation;
                _leftFingerPose = FingerLocals(leftTip);
                _rightFingerPose = FingerLocals(rightTip);
            }
            finally
            {
                for (int i = 1; i < animator.layerCount; i++)
                {
                    animator.SetLayerWeight(i, layerWeights[i]);
                }

                animator.SetBool(AnimEmote, value: false);
                animator.Play(previous.fullPathHash, 0, previous.normalizedTime);
                animator.Update(0f);
            }

            Diag.V(
                $"[CoverMouthPose] captured hand pose from \"{state}\" at t={Plugin.Cfg.CoverMouthPoseEmoteTime.Value:0.##}: " +
                $"{_leftFingerPose.Count} left finger bone(s), {_rightFingerPose.Count} right finger bone(s)");
            return true;
        }

        /// <summary>
        /// Captures the pose <b>at spawn</b>, well before anyone covers their mouth.
        /// Polled from <c>Plugin.Update</c>.
        ///
        /// The capture forces the animator into the emote state and evaluates it
        /// immediately (<c>Animator.Update(0)</c>), and PEAK's characters are physics
        /// ragdolls whose bodyparts chase the animated pose - so slamming a whole new
        /// pose in and out within one frame gives the ragdoll a real impulse. Doing it
        /// mid-run was violent enough to be a bug in its own right: the maintainer
        /// reported being <em>teleported backwards</em> with arms twitching the first
        /// time they covered their mouth in a live run (2026-07-27).
        ///
        /// Doing it while the player is standing in the airport before the run makes the
        /// same blip harmless: nothing is at stake there, and by the time the mechanic is
        /// ever used the pose is long since cached. Retried rather than latched, because
        /// early in a spawn the rig genuinely isn't ready yet.
        /// </summary>
        internal static void Prewarm()
        {
            if (HasCapturedPose || _captureFailed || Plugin.Cfg == null)
            {
                return;
            }

            var character = Character.localCharacter;
            if (character == null)
            {
                _nextPrewarmAttempt = 0f;
                return;
            }

            // First sighting of a character: wait a moment before touching its animator -
            // the rig and its IK constraints are built during spawn, and a capture from a
            // half-built rig is exactly the kind of thing that produces a subtly wrong
            // pose rather than an obvious failure.
            if (_nextPrewarmAttempt <= 0f)
            {
                _nextPrewarmAttempt = Time.time + PrewarmDelaySeconds;
                return;
            }

            if (Time.time < _nextPrewarmAttempt)
            {
                return;
            }

            _nextPrewarmAttempt = Time.time + PrewarmRetrySeconds;
            _prewarmAttempts++;

            if (TryCapturePose(character))
            {
                Diag.V($"[CoverMouthPose] pose pre-warmed at spawn after {_prewarmAttempts} attempt(s)");
                return;
            }

            if (_prewarmAttempts >= MaxPrewarmAttempts && !_captureFailed)
            {
                _captureFailed = true;
                Diag.Warn(
                    $"[CoverMouthPose] gave up pre-warming the hand pose after {_prewarmAttempts} attempts - " +
                    "covering your mouth will use arm IK only (no finger shape).");
            }
        }

        /// <summary>
        /// One-time dump of the animator's layers. Kept because it's what ruled out the
        /// obvious "just play the emote on an arms-only masked layer" approach (the
        /// controller has arm-ish layers, but the emote state lives only on the base
        /// layer), and because those same layers are why the capture has to mute them -
        /// asset data like this can't be read from the decompile.
        /// </summary>
        private static void LogAnimatorLayersOnce(Animator animator)
        {
            if (_layersLogged || !Diag.Enabled)
            {
                return;
            }

            _layersLogged = true;
            var layers = new List<string>();
            for (int i = 0; i < animator.layerCount; i++)
            {
                layers.Add($"[{i}] \"{animator.GetLayerName(i)}\" weight={animator.GetLayerWeight(i):0.##}");
            }

            Diag.V(
                $"[CoverMouthPose] animator \"{(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "none")}\" " +
                $"has {animator.layerCount} layer(s): {string.Join(", ", layers)}");
        }

        /// <summary>The animated rig's root - the reference frame the wrist orientation is stored in.</summary>
        private static Transform BodyRoot(Character character) =>
            character.refs.animator != null ? character.refs.animator.transform : null;

        /// <summary>Local rotations of every bone below a hand bone, keyed by bone name (the hand itself excluded).</summary>
        private static Dictionary<string, Quaternion> FingerLocals(Transform tip)
        {
            var bones = new List<Transform>();
            CollectHandBones(tip, bones);

            var pose = new Dictionary<string, Quaternion>();
            foreach (var bone in bones.Skip(1))
            {
                pose[bone.name] = bone.localRotation;
            }

            return pose;
        }

        /// <summary>
        /// Writes the captured finger rotations onto one hand's finger bones. Safe to do
        /// after the rig has solved - fingers aren't in the IK chain (which is upper arm →
        /// forearm → hand), so unlike the wrist there's no solver output being edited and
        /// no feedback loop to start.
        /// </summary>
        private static void ApplyFingerPose(Transform tip, Dictionary<string, Quaternion> pose)
        {
            if (tip == null || pose == null)
            {
                return;
            }

            var bones = new List<Transform>();
            CollectHandBones(tip, bones);

            // Applies whatever it recognises and ignores anything else, so an extra child
            // under a hand (a held item, a cosmetic) can't cost the whole hand its pose.
            for (int i = 1; i < bones.Count; i++)
            {
                if (pose.TryGetValue(bones[i].name, out Quaternion local))
                {
                    bones[i].localRotation = local;
                }
            }
        }

        /// <summary>
        /// Forces both arm IK constraints fully on while covering (overriding the game's
        /// own "no item, not reaching → rig off" decision), asks for the one-off pose
        /// capture the first time it's needed, and restores what it changed as soon as
        /// covering ends.
        /// </summary>
        [HarmonyPatch(typeof(CharacterAnimations), "HandleIK")]
        internal static class HandleIKPatch
        {
            private static void Postfix(CharacterAnimations __instance)
            {
                var character = CharacterRef(__instance);
                if (character == null)
                {
                    return;
                }

                var refs = character.refs;
                if (refs.ikRig == null || refs.ikLeft == null || refs.ikRight == null)
                {
                    return;
                }

                bool covering = ShouldPose(character);
                ApplyRotationWeight(refs.ikLeft, covering);
                ApplyRotationWeight(refs.ikRight, covering);

                if (!covering)
                {
                    return;
                }

                refs.ikRig.weight = 1f;
                refs.ikLeft.weight = 1f;
                refs.ikRight.weight = 1f;

                // Last resort only. The pose is normally captured at spawn (Prewarm),
                // precisely so this never runs mid-run - capturing here is what
                // teleported the maintainer backwards with twitching arms. Kept so a
                // failed pre-warm degrades to "one ugly moment" rather than "no pose at
                // all", and only ever fires once.
                if (!HasCapturedPose && !_captureFailed)
                {
                    Diag.Warn("[CoverMouthPose] pose wasn't pre-warmed at spawn - capturing now, which may jolt the character.");
                    TryCapturePose(character);
                }
            }

        }

        /// <summary>
        /// Runs immediately after the animator graph is evaluated: captures the pose on
        /// the one frame the clip is played, then on every later frame writes the captured
        /// wrist/finger rotations back over whatever the current animation put there, and
        /// places both hand targets over the mouth.
        /// </summary>
        [HarmonyPatch(typeof(CharacterAnimations), "ConfigureIK")]
        internal static class ConfigureIKPatch
        {
            private static void Postfix(CharacterAnimations __instance)
            {
                var character = CharacterRef(__instance);
                if (character == null || !ShouldPose(character))
                {
                    return;
                }

                var refs = character.refs;

                PlaceHands(character, refs);

                ApplyFingerPose(refs.ikLeft != null ? refs.ikLeft.data.tip : null, _leftFingerPose);
                ApplyFingerPose(refs.ikRight != null ? refs.ikRight.data.tip : null, _rightFingerPose);
            }

            /// <summary>
            /// The configured extra turn applied to a hand on top of the captured
            /// orientation, in <b>body space</b> - so the knobs mean the same thing
            /// regardless of which way the player is facing or how the arm ended up
            /// being solved.
            ///
            /// It exists because the borrowed emote holds its hands palm-to-palm: the
            /// maintainer's report was that the captured pose alone "looks as if I am
            /// praying." Opening the palms toward the face (thumbs pointing away from
            /// it) is a rotation about the body's vertical axis, which is what the yaw
            /// knob does; roll aims the thumbs up rather than straight out.
            ///
            /// Yaw and roll are <b>mirrored</b> between the hands so the pose stays
            /// symmetric from one knob - the sign flip on the y and z components is the
            /// mirror across the body's own plane. Pitch is deliberately not mirrored:
            /// tilting fingers up is the same direction for both hands.
            /// </summary>
            private static Quaternion HandTurn(bool mirrored)
            {
                var cfg = Plugin.Cfg;
                float pitch = cfg.CoverMouthPoseHandPitchDeg.Value;
                float yaw = cfg.CoverMouthPoseHandYawDeg.Value;
                float roll = cfg.CoverMouthPoseHandRollDeg.Value;

                return mirrored
                    ? Quaternion.Euler(pitch, -yaw, -roll)
                    : Quaternion.Euler(pitch, yaw, roll);
            }

            private static void PlaceHands(Character character, Character.CharacterRefs refs)
            {
                if (refs.IKHandTargetLeft == null || refs.IKHandTargetRight == null
                    || refs.animationHeadTransform == null || refs.animationLookTransform == null)
                {
                    return;
                }

                var cfg = Plugin.Cfg;
                // Configured in centimetres and converted here: PEAK's world units are not
                // metres (1 unit = 1.6m - Core/WorldUnits.cs), and at this scale that
                // factor is the difference between hands at the mouth and hands a forearm
                // in front of the face.
                float forward = GameUnits.MetersToUnits(cfg.CoverMouthPoseForwardCm.Value / 100f);
                float down = GameUnits.MetersToUnits(cfg.CoverMouthPoseBelowHeadCm.Value / 100f);
                float gap = GameUnits.MetersToUnits(cfg.CoverMouthPoseHandGapCm.Value / 100f) * 0.5f;
                float side = GameUnits.MetersToUnits(cfg.CoverMouthPoseSideCm.Value / 100f);

                Vector3 head = refs.animationHeadTransform.position;
                Transform look = refs.animationLookTransform;

                // Both hands near the centre line with one stacked above the other: the
                // pose only reads as "covering my mouth" if the hands are together, and
                // the vertical gap is what stops them occupying the same space.
                refs.IKHandTargetLeft.position =
                    head + look.TransformDirection(new Vector3(-side, -down + gap, forward));
                refs.IKHandTargetRight.position =
                    head + look.TransformDirection(new Vector3(side, -down - gap, forward));

                // Wrist orientation: the captured pose, re-expressed in world space
                // against the body's *current* facing. Set on the target (not the bone)
                // so the solver aims for it - see _leftWristBodyRelative for why the
                // first attempt, which wrote the bone directly, flipped and twitched.
                Transform body = BodyRoot(character);
                if (body != null && HasCapturedPose)
                {
                    refs.IKHandTargetLeft.rotation = body.rotation * HandTurn(mirrored: true) * _leftWristBodyRelative;
                    refs.IKHandTargetRight.rotation = body.rotation * HandTurn(mirrored: false) * _rightWristBodyRelative;
                }
            }
        }

        /// <summary>
        /// Logs whichever emote the player triggers from the wheel, so "the one called
        /// 'it's so over'" can be turned into an exact animator state name by playing it.
        /// This is what identified <see cref="DefaultPoseState"/>; kept as a dev aid in
        /// case a game update renames the clips.
        /// </summary>
        [HarmonyPatch(typeof(CharacterAnimations), "PlayEmote")]
        internal static class EmotePlayedProbe
        {
            private static void Prefix(string emoteName)
            {
                Diag.V($"[CoverMouthPose] emote-played: animator state \"{emoteName}\" - paste this into Debug/cover-mouth-pose-emote to pose from it");
            }
        }
    }
}
