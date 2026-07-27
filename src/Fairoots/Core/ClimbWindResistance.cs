using System;

namespace Fairoots.Core
{
    /// <summary>
    /// A climb movement step decomposed onto the climbed surface's own plane:
    /// <see cref="Up"/> is the component along "up the wall" (positive = climbing
    /// upward), <see cref="Lateral"/> the component along the wall's sideways axis.
    /// Unity-free by design (CODEBASE.md's Core rule) - the game-facing patch does
    /// the <c>Vector3</c> projection and hands the two scalars over.
    /// </summary>
    public readonly struct ClimbMove
    {
        public ClimbMove(float lateral, float up)
        {
            Lateral = lateral;
            Up = up;
        }

        public float Lateral { get; }

        public float Up { get; }
    }

    /// <summary>
    /// The climb-to-shelter-from-wind mechanic's arithmetic (ROADMAP.md's
    /// "New: climb-to-counter-wind" row, rescoped 2026-07-27 from "already exists
    /// natively" to a real mechanic - see below).
    ///
    /// **Why this exists at all.** The earlier reading (RESEARCH.md, and the
    /// original remarks on <see cref="Presets.PresetCatalog.ClimbToCounterWind"/>)
    /// was that vanilla already suppresses wind while climbing. It doesn't:
    /// <c>WindChillZone.AddWindForceToCharacter</c>'s early return only covers
    /// <c>character.data.currentClimbHandle != null</c> - hanging off a climb
    /// *handle*, which is a specific prop, not the ordinary
    /// grab-the-wall-with-your-hands climbing (<c>CharacterData.isClimbing</c>)
    /// that players actually spend Roots doing, and not rope or vine climbing
    /// either. In vanilla, wind pushes a wall-climbing player exactly as hard as a
    /// walking one, and being pushed while climbing is *worse* than being pushed
    /// while walking: the shove ragdolls you, <c>CharacterClimbing.Update</c> drops
    /// the climb the moment <c>currentRagdollControll</c> falls below 0.25, and you
    /// leave the wall entirely. That's the maintainer's complaint (2026-07-27):
    /// walking into the wind and hoping your stamina holds is the only real tactic,
    /// because grabbing a wall is a coin flip on being launched off it.
    ///
    /// **The mechanic.** Holding onto something makes you *safe* from wind rather
    /// than "less likely to be pushed" - the game-facing patch suppresses the wind
    /// force outright while climbing - and the cost is paid in speed instead of in
    /// randomness: climbing gets much slower while the wind is actually pushing on
    /// you, and slower still climbing upward or into the wind. This file is that
    /// cost.
    ///
    /// **Pressure** is how hard the wind would be pushing right now, 0-1, taken
    /// from the same terms the native force formula uses (light-volume exposure,
    /// intensity curve, the obstacle-occlusion raycast, the gust's own ramp). So
    /// shelter that already protects a player - standing behind a rock, out of the
    /// gust's ramp-up, no gust at all - costs them nothing here either: pressure
    /// is 0 and climbing runs at full vanilla speed. The slowdown only ever
    /// applies exactly when the wind would otherwise have thrown them off the wall.
    ///
    /// Not seed-gated (every climber gets the same flat treatment for the active
    /// preset), same as <see cref="WindTuning"/>/<see cref="SporeBombExplosionTuning"/>.
    /// </summary>
    public static class ClimbWindResistance
    {
        /// <summary>
        /// How long a recorded wind pressure stays valid. The game-facing patch
        /// records pressure from <c>WindChillZone.FixedUpdate</c>'s force pass,
        /// which simply stops being called when the gust ends or the player leaves
        /// the zone - there's no "wind stopped" event to listen for, so a stale
        /// reading is what "no wind" looks like. A few fixed frames' worth: long
        /// enough that a normal 0.02s physics step never expires mid-gust, short
        /// enough that climbing returns to full speed essentially the instant the
        /// gust drops.
        /// </summary>
        public const float PressureFreshnessSeconds = 0.35f;

        /// <summary>
        /// Whether a pressure reading recorded at <paramref name="recordedAtTime"/>
        /// still describes the present. A negative timestamp means "nothing recorded
        /// yet" (no gust has ever pushed this player). A reading from the future
        /// (a level reload resetting the clock) counts as current rather than
        /// stale - it expires on its own a moment later.
        /// </summary>
        public static bool IsPressureCurrent(float recordedAtTime, float currentTime)
        {
            if (recordedAtTime < 0f)
            {
                return false;
            }

            return currentTime - recordedAtTime <= PressureFreshnessSeconds;
        }

        /// <summary>
        /// How much of the let-go grace window (see
        /// <see cref="GraceForceMultiplier"/>) is spent ramping the wind back to
        /// full strength rather than holding it at the reduced value. Exists so
        /// the window doesn't end in a cliff: snapping from near-immune to full
        /// force in one physics step is its own unexplained shove, which is the
        /// thing the window is there to prevent. The first 60% of the window
        /// holds, the last 40% ramps.
        /// </summary>
        public const float GraceRampFraction = 0.4f;

        /// <summary>
        /// The multiplier on wind force just after a player lets go of a climb -
        /// 1.0 (untouched vanilla force) unless they were holding on within
        /// <paramref name="graceSeconds"/>, otherwise
        /// <paramref name="reducedMultiplier"/>, ramping back to 1 across the
        /// tail of the window (<see cref="GraceRampFraction"/>).
        ///
        /// **Why this exists** (maintainer, 2026-07-27): finishing a climb is the
        /// worst moment in a gust. The game's own release path
        /// (<c>CharacterClimbing.StopClimbingRpc</c>) hands you back to physics
        /// already declared airborne - it sets <c>sinceGrounded</c> to a fake
        /// fall time - so full wind force lands on you at the exact instant you
        /// have the least control, and stacks onto whatever momentum the climb
        /// left you with. That's frequently lethal, and it's just as lethal when
        /// the release was an accident. A short window of much-weaker wind gives
        /// the player time to start sprinting out of it or re-grab the wall.
        ///
        /// Deliberately *not* full immunity: a player could otherwise tap a wall
        /// repeatedly to cross an exposed stretch wind-free, which would make the
        /// shelter mechanic a movement exploit rather than a counter. The wind
        /// still pushes - it just no longer catapults.
        /// </summary>
        public static float GraceForceMultiplier(
            float lastHeldOnTime,
            float currentTime,
            float graceSeconds,
            double reducedMultiplier)
        {
            if (lastHeldOnTime < 0f || graceSeconds <= 0f)
            {
                return 1f; // never held on, or the window is switched off.
            }

            float elapsed = currentTime - lastHeldOnTime;
            if (elapsed >= graceSeconds)
            {
                return 1f;
            }

            if (elapsed < 0f)
            {
                elapsed = 0f; // clock reset under a stored timestamp - treat as "just now".
            }

            float reduced = (float)reducedMultiplier;
            if (reduced < 0f || float.IsNaN(reduced))
            {
                reduced = 0f;
            }
            else if (reduced > 1f)
            {
                reduced = 1f; // this is a reduction; it may never amplify the wind.
            }

            float holdUntil = graceSeconds * (1f - GraceRampFraction);
            if (elapsed <= holdUntil)
            {
                return reduced;
            }

            float rampProgress = (elapsed - holdUntil) / (graceSeconds - holdUntil);
            return reduced + (1f - reduced) * rampProgress;
        }

        /// <summary>Clamps a raw computed wind pressure into the 0-1 range this file's math assumes.</summary>
        public static float ClampPressure(float pressure)
        {
            if (pressure < 0f || float.IsNaN(pressure))
            {
                return 0f;
            }

            return pressure > 1f ? 1f : pressure;
        }

        /// <summary>
        /// Scales one climb movement step by the wind's resistance.
        ///
        /// Three multipliers compose, each faded in by <paramref name="pressure"/>
        /// so that a fully sheltered climber (pressure 0) always moves at exactly
        /// vanilla speed no matter how harsh the settings are:
        /// <list type="bullet">
        /// <item><paramref name="baseMultiplier"/> - applies to the whole step:
        /// climbing in wind is slower in every direction.</item>
        /// <item><paramref name="upwardMultiplier"/> - additionally applies to
        /// upward movement only. Downward movement (sliding, letting go of height)
        /// is never penalised beyond the base: the wind is a reason to descend, and
        /// slowing a slide down would just look like broken physics.</item>
        /// <item><paramref name="intoWindMultiplier"/> - additionally applies to
        /// whichever component actually opposes the wind, scaled by how much of the
        /// wind lies along that axis. Moving *with* the wind is never sped up: this
        /// mechanic is a cost, not a sail.</item>
        /// </list>
        ///
        /// <paramref name="windLateral"/>/<paramref name="windUp"/> are the wind
        /// direction (the direction it blows *toward*) projected onto the same
        /// surface plane as <paramref name="move"/>; a component of the move
        /// opposes the wind when the two have opposite signs.
        /// </summary>
        public static ClimbMove Resist(
            ClimbMove move,
            float windLateral,
            float windUp,
            float pressure,
            double baseMultiplier,
            double upwardMultiplier,
            double intoWindMultiplier)
        {
            pressure = ClampPressure(pressure);
            if (pressure <= 0f)
            {
                return move;
            }

            float baseFactor = Fade(baseMultiplier, pressure);

            float up = move.Up * baseFactor;
            if (move.Up > 0f)
            {
                up *= Fade(upwardMultiplier, pressure);
            }

            up *= OpposingFactor(move.Up, windUp, pressure, intoWindMultiplier);

            float lateral = move.Lateral * baseFactor
                * OpposingFactor(move.Lateral, windLateral, pressure, intoWindMultiplier);

            return new ClimbMove(lateral, up);
        }

        /// <summary>
        /// The into-the-wind penalty for a single axis: 1 (untouched) unless the
        /// movement opposes the wind on that axis, otherwise
        /// <paramref name="multiplier"/> faded in by both the pressure and how much
        /// of the wind actually lies along this axis (wind blowing straight across
        /// a wall costs nothing to climb up through, only to traverse into).
        /// </summary>
        private static float OpposingFactor(float moveComponent, float windComponent, float pressure, double multiplier)
        {
            if (moveComponent * windComponent >= 0f)
            {
                return 1f; // moving with the wind, or no movement/wind on this axis.
            }

            return Fade(multiplier, pressure * Math.Abs(windComponent));
        }

        /// <summary>
        /// Fades a multiplier in from 1 (no effect) at zero weight to its full
        /// value at weight 1 - <c>Lerp(1, multiplier, weight)</c>, guarded so a
        /// negative resolved multiplier can never flip a movement's direction.
        /// </summary>
        private static float Fade(double multiplier, float weight)
        {
            if (multiplier < 0.0)
            {
                multiplier = 0.0;
            }

            float faded = (float)(1.0 + (multiplier - 1.0) * weight);
            return faded < 0f ? 0f : faded;
        }
    }
}
