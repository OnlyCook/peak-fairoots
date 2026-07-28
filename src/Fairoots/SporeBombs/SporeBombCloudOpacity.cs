using System.Collections.Generic;
using Fairoots.Core;
using Fairoots.Diagnostics;
using UnityEngine;

namespace Fairoots.SporeBombs
{
    /// <summary>
    /// Thins the temporary spore cloud a detonating spore bomb leaves behind
    /// (<c>General/spore-bomb-cloud-opacity</c>), the spore-bomb half of the
    /// readability fix <see cref="SporeCloudOpacity"/> describes. The persistent
    /// spore areas' half is <c>SporeAreas/SporeCloudOpacityPatch</c>; both write the
    /// alpha through the same <see cref="ParticleOpacity"/>.
    ///
    /// <b>Why a component rather than a one-shot call at spawn time.</b> A spore
    /// bomb's detonation is not a single instant: the spawned object's <c>AOE</c>
    /// re-explodes on a timer for as long as the cloud lasts (established from a
    /// live call stack - see <see cref="SporeBombDetonationMarker"/>), and
    /// <c>ExplosionEffect</c> keeps instantiating VFX on a staggered coroutine after
    /// the spawn frame has already passed. Anything created after a single spawn-time
    /// pass would come out at full vanilla density. Re-applying on an interval for
    /// the object's lifetime catches all of it - and since the object destroys
    /// itself when the cloud ends, the polling ends with it, with no lifetime
    /// bookkeeping of its own.
    ///
    /// Cheap by construction: a handful of live detonations at most, each ticking a
    /// short <c>GetComponentsInChildren</c> a few times a second, and
    /// <see cref="ParticleOpacity"/> caches the authored values so repeats can't
    /// compound.
    /// </summary>
    internal sealed class SporeBombCloudOpacity : MonoBehaviour
    {
        /// <summary>
        /// How often the detonation's VFX is re-swept for particle systems that
        /// didn't exist yet. Fast enough that a late system is thinned well within
        /// the frame or two it takes to fade in, slow enough to be irrelevant to
        /// framerate.
        /// </summary>
        private const float ReapplyIntervalSeconds = 0.25f;

        /// <summary>Whether the one-time structure dump has been logged this session - see <see cref="LogStructureOnce"/>.</summary>
        private static bool _structureLogged;

        private float _nextApplyTime;

        private void OnEnable()
        {
            _nextApplyTime = 0f;
            LogStructureOnce();
        }

        private void Update()
        {
            if (Plugin.Cfg == null || Time.unscaledTime < _nextApplyTime)
            {
                return;
            }

            _nextApplyTime = Time.unscaledTime + ReapplyIntervalSeconds;
            ParticleOpacity.Apply(gameObject, Plugin.Cfg.SporeBombCloudOpacity.Value);
        }

        /// <summary>
        /// One-time verbose dump of a real detonation's hierarchy and components.
        /// The spawned explosion is a prefab - a Unity <em>asset</em> - so nothing in
        /// the decompiled C# says what a spore bomb's cloud is actually built from,
        /// and the only way to confirm this component is thinning the thing the
        /// player is looking at (or to see what to target instead if it isn't) is to
        /// look at a live one. Exactly the same reasoning, and the same one-shot
        /// shape, as <c>SporeAreaTuningPatch.LogStructureOnce</c>, which has already
        /// corrected one wrong assumption about spore-area layout.
        /// </summary>
        private void LogStructureOnce()
        {
            if (_structureLogged || !Diag.Enabled)
            {
                return;
            }

            _structureLogged = true;
            Diag.V($"[SporeBombCloudOpacity] structure of a live detonation \"{name}\":");
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                var names = new List<string>();
                foreach (var c in t.GetComponents<Component>())
                {
                    names.Add(c == null ? "<missing-script>" : c.GetType().Name);
                }

                Diag.V($"[SporeBombCloudOpacity]   \"{t.name}\" : {string.Join(", ", names)}");
            }
        }
    }
}
