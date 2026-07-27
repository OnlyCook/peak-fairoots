using System;
using System.Collections.Generic;
using Fairoots.Core;
using Fairoots.Diagnostics;
using UnityEngine;

namespace Fairoots.SporeAreas
{
    /// <summary>
    /// Phase 6 (ROADMAP.md's "Spore area radius" row): resizes every persistent
    /// spore area by the configured multiplier - both the hazard itself
    /// (<c>StatusEmitter.radius</c> plus its <c>innerFade</c>/<c>outerFade</c>, so
    /// the falloff shape survives - see <see cref="SporeAreaTuning.ScaleFade"/>)
    /// and the visible cloud, so the two can't disagree.
    ///
    /// Baseline caching, same pattern as <c>WindChillZoneTuningPatch</c> and
    /// <c>SporeBombCullPatch</c>'s trigger radii: each emitter's and each VFX
    /// transform's vanilla values are captured the *first* time it's ever seen and
    /// every reapply scales from that cached baseline, never from the current
    /// (possibly already-scaled) value. Without it, reprocessing the same live
    /// objects - which happens, e.g. re-entering a run without the previous level's
    /// objects being destroyed - would compound the scaling each time, and setting
    /// the multiplier back to 1.0 would preserve whatever the last one left behind
    /// instead of restoring vanilla.
    ///
    /// Live-updatable (unlike the removal fraction): a resize can be undone, so
    /// this reapplies on a config change while <c>apply-changes-live</c> is on.
    /// </summary>
    internal static class SporeAreaRadiusPatch
    {
        private readonly struct EmitterBaseline
        {
            internal readonly float Radius;
            internal readonly float InnerFade;
            internal readonly float OuterFade;

            internal EmitterBaseline(float radius, float innerFade, float outerFade)
            {
                Radius = radius;
                InnerFade = innerFade;
                OuterFade = outerFade;
            }
        }

        private static readonly Dictionary<int, EmitterBaseline> VanillaEmitters = new Dictionary<int, EmitterBaseline>();

        private static readonly Dictionary<int, Vector3> VanillaVfxScales = new Dictionary<int, Vector3>();

        /// <summary>Whether the one-time structure dump has already been logged this session (verbose only, see <see cref="LogStructureOnce"/>).</summary>
        private static bool _structureLogged;

        internal static void Run(Transform rootsSegment)
        {
            try
            {
                Apply(rootsSegment.GetComponentsInChildren<StatusEmitter>(true), "level load");
            }
            catch (Exception e)
            {
                Diag.Error($"[SporeAreaRadius] Run threw: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// Re-resolves every spore area's size scene-wide against the current
        /// config - wired to the radius setting's and the preset's
        /// <c>SettingChanged</c>, and to <c>HostAuthoritySync</c>'s room-property
        /// update so a non-host client whose level load raced ahead of the host's
        /// first publish still converges on the host's value.
        /// </summary>
        internal static void ReapplyToAll()
        {
            if (Plugin.Cfg == null)
            {
                return;
            }

            try
            {
                Apply(UnityEngine.Object.FindObjectsOfType<StatusEmitter>(true), "config change");
            }
            catch (Exception e)
            {
                Diag.Error($"[SporeAreaRadius] ReapplyToAll threw: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void Apply(IReadOnlyList<StatusEmitter> emitters, string reason)
        {
            double multiplier = Plugin.Cfg.EffectiveSporeAreaRadiusMultiplier;
            var areas = SporeAreaScan.FilterSporeAreas(emitters);
            if (areas.Count == 0)
            {
                return;
            }

            LogStructureOnce(areas[0]);

            int resized = 0, vfxScaled = 0, vfxMissing = 0;
            float sampleFrom = 0f, sampleTo = 0f;
            foreach (var emitter in areas)
            {
                int id = emitter.GetInstanceID();
                if (!VanillaEmitters.TryGetValue(id, out EmitterBaseline vanilla))
                {
                    vanilla = new EmitterBaseline(emitter.radius, emitter.innerFade, emitter.outerFade);
                    VanillaEmitters[id] = vanilla;
                }

                emitter.radius = SporeAreaTuning.ScaleRadius(vanilla.Radius, multiplier);
                emitter.innerFade = SporeAreaTuning.ScaleFade(vanilla.InnerFade, multiplier);
                emitter.outerFade = SporeAreaTuning.ScaleFade(vanilla.OuterFade, multiplier);
                resized++;
                sampleFrom = vanilla.Radius;
                sampleTo = emitter.radius;

                int scaled = ScaleVfx(SporeAreaScan.ResolveAreaRoot(emitter), multiplier);
                if (scaled > 0)
                {
                    vfxScaled += scaled;
                }
                else
                {
                    vfxMissing++;
                }
            }

            Diag.Info(
                $"[SporeAreaRadius] {reason}: multiplier={multiplier:0.###}, {resized} spore area(s) resized " +
                $"(e.g. radius {sampleFrom:0.##} -> {sampleTo:0.##} world units, " +
                $"{GameUnits.ToMeters(sampleFrom):0.#}m -> {GameUnits.ToMeters(sampleTo):0.#}m), " +
                $"{vfxScaled} cloud VFX transform(s) scaled" +
                (vfxMissing > 0 ? $", {vfxMissing} area(s) had no VFX to scale" : string.Empty));
        }

        /// <summary>
        /// Resizes a spore area's visible cloud to match the hazard. Targets each
        /// <see cref="ParticleSystem"/>'s <em>own</em> transform rather than the area
        /// root's, for two reasons: the root also carries the emitter mushroom in the
        /// middle of the cloud, which shouldn't grow or shrink with the gas; and a
        /// particle system's own transform scale is honoured under Unity's default
        /// <c>Local</c> scaling mode as well as <c>Hierarchy</c>, so this needs no
        /// change to how the VFX was authored. Scaling the transform (rather than
        /// poking <c>shape.radius</c>) also scales particle size and velocity with
        /// the volume, which is what keeps a resized cloud looking like the same
        /// cloud.
        /// </summary>
        private static int ScaleVfx(GameObject areaRoot, double multiplier)
        {
            int count = 0;
            float scale = SporeAreaTuning.ScaleVisual(multiplier);

            foreach (var ps in areaRoot.GetComponentsInChildren<ParticleSystem>(true))
            {
                Transform t = ps.transform;
                int id = t.GetInstanceID();
                if (!VanillaVfxScales.TryGetValue(id, out Vector3 vanilla))
                {
                    vanilla = t.localScale;
                    VanillaVfxScales[id] = vanilla;
                }

                t.localScale = vanilla * scale;
                count++;
            }

            return count;
        }

        /// <summary>
        /// One-time verbose dump of a real spore area's component/child layout.
        /// Shaders and prefab structure are Unity <em>assets</em>, not code, so
        /// nothing in the decompiled C# says what a spore cloud is actually built
        /// from - this is the only way to confirm the VFX scaling above is hitting
        /// the right objects (and to see what to target instead if a level ever
        /// reports "had no VFX to scale"). Same reasoning as
        /// <c>Diagnostics/MaterialProbe</c>.
        /// </summary>
        private static void LogStructureOnce(StatusEmitter sample)
        {
            if (_structureLogged || !Diag.Enabled)
            {
                return;
            }

            _structureLogged = true;
            GameObject root = SporeAreaScan.ResolveAreaRoot(sample);
            Diag.V($"[SporeAreaRadius] structure of \"{SporeAreaScan.DescribePath(root.transform)}\":");
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var names = new List<string>();
                foreach (var c in t.GetComponents<Component>())
                {
                    names.Add(c == null ? "<missing-script>" : c.GetType().Name);
                }

                Diag.V($"[SporeAreaRadius]   \"{t.name}\" scale={t.localScale} : {string.Join(", ", names)}");
            }
        }
    }
}
