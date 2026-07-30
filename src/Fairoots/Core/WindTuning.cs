using System;

namespace Fairoots.Core
{
    /// <summary>
    /// Phase 5 (ROADMAP.md preset table rows "Wind force / frequency", "Wind:
    /// items/backpack immunity", "Wind: obstacle occlusion", "Wind-induced fall
    /// camera spin dampening"). "Wind: fog-while-active density" is deliberately
    /// NOT implemented here - a prior version scaled
    /// <c>FogConfig.windFogDensity</c>/<c>WindFogTextureDensity</c> and was
    /// reverted as a precaution: the real density/opacity relationship for
    /// those shader globals is baked into shader code this mod can't decompile
    /// or verify, so scaling them from decompiled-C#-only assumptions isn't
    /// safe, even though a since-fixed *different* bug (see
    /// <see cref="MinWindActiveDurationSeconds"/>) turned out to be the actual
    /// cause of the "screen turns solid black" symptom reported at the time.
    /// Pure arithmetic only - no Unity/BepInEx dependency, mirroring
    /// <see cref="SporeBombExplosionTuning"/>'s split (CODEBASE.md's Core rule).
    /// The actual field reads/writes on <c>WindChillZone</c>/<c>FogConfig</c>/
    /// <c>CharacterData</c> live in the game-facing Harmony patches under
    /// <c>src/Fairoots/Wind/</c>, which just call into these functions for the
    /// numbers.
    ///
    /// None of this is seed-gated: every wind zone, item, and fall gets the same
    /// flat multiplier treatment for the active preset - there's no per-instance
    /// RNG decision involved (CLAUDE.md's determinism rule only applies to
    /// Fairoots-owned probabilistic decisions, and this mod never touches
    /// <c>UnityEngine.Random</c> at all here, same as the spore-bomb tuning).
    /// </summary>
    public static class WindTuning
    {
        /// <summary>Scale <c>WindChillZone.windForce</c> by a preset/override multiplier. 1.0 = vanilla.</summary>
        public static float ScaleWindForce(float vanillaForce, double multiplier)
        {
            return (float)(vanillaForce * multiplier);
        }

        /// <summary>
        /// Floor for <see cref="ScaleWindActiveDuration"/>'s result - live-tested
        /// and confirmed necessary (2026-07-22): a low/zero force multiplier
        /// scaling <c>windTimeRangeOn</c> down to (or near) 0 gives
        /// <c>WindChillZone.GetNextWindTime</c> a genuinely zero-length "on"
        /// phase, which makes the *native* windActive on/off timer
        /// (<c>HandleTime</c>) flip rapidly - and since the game's own
        /// <c>FogConfig.SetFog</c>/storm-blend logic only decays
        /// <c>_WeatherBlend</c> after 0.1s of no re-trigger, rapid re-toggling
        /// never gives it that gap, so it ratchets up to fully opaque and never
        /// comes back down - the exact "screen turns solid black" bug reported
        /// after setting the force multiplier near 0. This mod never touches
        /// <c>FogConfig</c> at all (see class remarks) - the fix here is simply
        /// never letting the *duration* input degenerate to zero, regardless of
        /// how low the force multiplier goes (0 is a legitimate "no wind force"
        /// ask and stays exact - see <see cref="ScaleWindForce"/> - only the
        /// duration gets floored).
        /// </summary>
        public const float MinWindActiveDurationSeconds = 1f;

        /// <summary>
        /// Scale how long a wind gust lasts once it starts (<c>windTimeRangeOn</c>'s
        /// x/y bounds) by the same multiplier used for force - a lower multiplier
        /// means both a gentler push and a shorter gust, matching ROADMAP.md's
        /// combined "Wind force / frequency" row. Never scales below
        /// <see cref="MinWindActiveDurationSeconds"/> - see that constant's remarks
        /// for why a genuinely zero-length gust breaks the native wind timer.
        /// </summary>
        public static float ScaleWindActiveDuration(float vanillaSeconds, double multiplier)
        {
            float scaled = (float)(vanillaSeconds * multiplier);
            return scaled < MinWindActiveDurationSeconds ? MinWindActiveDurationSeconds : scaled;
        }

        /// <summary>
        /// Scale how long the calm period between gusts lasts
        /// (<c>windTimeRangeOff</c>'s x/y bounds) - the inverse of the active-duration
        /// scale, so a lower "wind force/frequency" multiplier means longer calm
        /// periods as well as shorter, gentler gusts. Guards against a non-positive
        /// multiplier (never divide by zero or go negative) by leaving the vanilla
        /// value untouched in that case.
        /// </summary>
        public static float ScaleWindRestDuration(float vanillaSeconds, double multiplier)
        {
            if (multiplier <= 0.0)
            {
                return vanillaSeconds;
            }

            return (float)(vanillaSeconds / multiplier);
        }

        /// <summary>
        /// Scale <c>WindChillZone.windItemFactor</c> (applies to every non-backpack
        /// ground item) by a preset/override multiplier. 1.0 = vanilla. Backpacks
        /// get full immunity separately (see <c>WindChillZoneTuningPatch</c>'s
        /// prefix on <c>AddWindForceToItem</c>) regardless of this factor, per
        /// ROADMAP.md's "backpack only" minimum immunity even on Subtle.
        /// </summary>
        public static float ScaleItemForceFactor(float vanillaFactor, double multiplier)
        {
            float scaled = (float)(vanillaFactor * multiplier);
            return scaled < 0f ? 0f : scaled;
        }

        /// <summary>
        /// Scale <c>WindChillZone.minRaycastDistance</c>/<c>maxRaycastDistance</c>
        /// (the existing, already-enabled-in-Roots obstacle-occlusion raycast - see
        /// roots-runtime-findings memory) by a preset/override multiplier. Widening
        /// both distances lets an obstacle start blocking wind from further away,
        /// per ROADMAP.md's "on, coarse" -&gt; "on, generous radius" progression.
        /// 1.0 = vanilla (Subtle) - this is a tune-not-build lever, not a toggle,
        /// since the raycast is already on in Roots.
        /// </summary>
        public static float ScaleRaycastDistance(float vanillaDistance, double multiplier)
        {
            float scaled = (float)(vanillaDistance * multiplier);
            return scaled < 0f ? 0f : scaled;
        }

        /// <summary>
        /// True while a wind-force application on the local character is still
        /// "recent enough" to count as having caused whatever fall is now happening
        /// (ROADMAP.md's "wind-preceded falls only" scoping decision - the
        /// maintainer's own framing: falling is generally the player's fault, but
        /// being blown off a ledge by wind mid-jump is close to pure bad luck, so
        /// only that specific case gets dampened). Zero/negative
        /// <paramref name="lastWindForceTime"/> means "no wind force recorded yet".
        /// </summary>
        public static bool IsWindForceStillRecent(float lastWindForceTime, float currentTime, float windowSeconds)
        {
            if (lastWindForceTime < 0f)
            {
                return false;
            }

            return currentTime - lastWindForceTime <= windowSeconds;
        }

        /// <summary>
        /// Full control, i.e. "not a ragdoll at all" -
        /// <c>CharacterData.GetTargetRagdollControll()</c>'s own maximum, which is
        /// what <see cref="ApplyWindRagdollImmunity"/> hands back.
        /// </summary>
        public const float FullRagdollControl = 1f;

        /// <summary>
        /// <c>Wind/prevent-wind-ragdoll</c> (live-reported gap, 2026-07-30): while a
        /// fall is wind-preceded (<see cref="IsWindForceStillRecent"/>), the player
        /// keeps <see cref="FullRagdollControl"/> instead of vanilla's
        /// unconditional 0 - so being blown off a ledge no longer collapses you into
        /// physics on the way down, and you can still grab a wall or use a Rescue
        /// Hook. The stronger sibling of <see cref="ApplyFallCameraDampening"/>,
        /// which only raises the floor partway; when this is on it makes that clamp
        /// redundant, and when it's off the clamp still applies on its own (the
        /// two compose by <see cref="Math.Max"/>, so whichever is more generous
        /// wins and neither can ever <em>lower</em> the vanilla result).
        ///
        /// Deliberately scoped to wind-preceded falls only, exactly like the clamp:
        /// an ordinary fall is the player's own doing and ragdolls as vanilla does.
        /// </summary>
        public static float ApplyWindRagdollImmunity(
            float vanillaTargetRagdollControl, bool fallIsWindPreceded, bool immunityEnabled)
        {
            if (!immunityEnabled || !fallIsWindPreceded)
            {
                return vanillaTargetRagdollControl;
            }

            return Math.Max(vanillaTargetRagdollControl, FullRagdollControl);
        }

        /// <summary>
        /// Raises <c>CharacterData.GetTargetRagdollControll()</c>'s result from 0
        /// (vanilla's unconditional value the instant any fall starts, RESEARCH.md
        /// Q6) up to <paramref name="dampenClampValue"/> when the fall was
        /// wind-preceded - <see cref="IsWindForceStillRecent"/> - so the camera
        /// keeps partial player-controlled framing instead of fully surrendering to
        /// raw ragdoll-head physics, giving the player a chance to grab a wall or
        /// use a Rescue Hook instead of being helplessly spun. Never *lowers* the
        /// vanilla result (a clamp of 0, or a fall that isn't wind-preceded, leaves
        /// it untouched) - this only ever raises the floor.
        /// </summary>
        public static float ApplyFallCameraDampening(float vanillaTargetRagdollControl, bool fallIsWindPreceded, float dampenClampValue)
        {
            if (!fallIsWindPreceded || dampenClampValue <= 0f)
            {
                return vanillaTargetRagdollControl;
            }

            return Math.Max(vanillaTargetRagdollControl, dampenClampValue);
        }
    }
}
