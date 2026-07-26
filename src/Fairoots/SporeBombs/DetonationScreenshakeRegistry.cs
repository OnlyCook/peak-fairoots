using Fairoots.Core;
using UnityEngine;

namespace Fairoots.SporeBombs
{
    /// <summary>
    /// A tiny fixed-size ring of "a spore bomb just went off here, at this time",
    /// written by <see cref="SporeBombExplosionPatch"/> at detonation and read by
    /// <see cref="DetonationScreenshakePatch"/> to decide whether a screen shake
    /// that fires a moment later belongs to that detonation.
    ///
    /// Needed because the detonation's screen shakes don't all exist at spawn
    /// time: <c>ExplosionEffect</c> instantiates its explosion orbs on a staggered
    /// coroutine over the following ~second, unparented, so any
    /// <c>AddScreenshake</c> those orb prefabs carry is invisible to the
    /// spawn-time tuning pass and has to be caught as it fires.
    ///
    /// Fixed capacity and no allocation per detonation: this is touched from a
    /// Harmony prefix on a component the whole game uses, so it has to stay cheap.
    /// </summary>
    internal static class DetonationScreenshakeRegistry
    {
        private const int Capacity = 16;

        private static readonly Vector3[] Positions = new Vector3[Capacity];
        private static readonly float[] Times = new float[Capacity];
        private static int _next;
        private static bool _any;

        internal static void Record(Vector3 position)
        {
            Positions[_next] = position;
            Times[_next] = Time.time;
            _next = (_next + 1) % Capacity;
            _any = true;
        }

        /// <summary>
        /// True if <paramref name="position"/> is inside the space/time window of a
        /// recently recorded detonation (see
        /// <see cref="SporeBombExplosionTuning.IsDetonationScreenshake"/>).
        /// </summary>
        internal static bool IsFromRecentDetonation(Vector3 position)
        {
            if (!_any)
            {
                return false;
            }

            float now = Time.time;
            for (int i = 0; i < Capacity; i++)
            {
                if (Times[i] <= 0f)
                {
                    continue;
                }

                if (SporeBombExplosionTuning.IsDetonationScreenshake(
                        now - Times[i], GameUnits.ToMeters(Vector3.Distance(Positions[i], position))))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Drops every recorded detonation. Called on level load - <c>Time.time</c>
        /// keeps running across scene loads, but stale positions from the previous
        /// level would otherwise linger and could match a shake in the new one.
        /// </summary>
        internal static void Clear()
        {
            for (int i = 0; i < Capacity; i++)
            {
                Times[i] = 0f;
            }

            _next = 0;
            _any = false;
        }
    }
}
