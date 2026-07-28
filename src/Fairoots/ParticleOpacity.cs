using System;
using System.Collections.Generic;
using Fairoots.Core;
using Fairoots.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fairoots
{
    /// <summary>
    /// The shared "thin out this VFX" applier behind both spore-cloud translucency
    /// settings (the persistent spore areas' clouds, see
    /// <c>SporeAreas/SporeCloudOpacityPatch</c>, and the temporary cloud a spore
    /// bomb leaves, see <c>SporeBombs/SporeBombCloudOpacity</c>). Game-facing, so
    /// deliberately outside <c>Core/</c> - the arithmetic it calls lives in
    /// <see cref="SporeCloudOpacity"/>. One copy, like <c>SporeAreaScan</c>, so the
    /// two hazards can't drift into looking differently thinned at the same setting.
    ///
    /// <b>There is no single lever that thins every particle system, so this picks
    /// one per system.</b> The first version only scaled
    /// <c>ParticleSystem.main.startColor</c>'s alpha - the per-particle vertex color
    /// every stock particle shader multiplies in - which thinned a spore bomb's
    /// cloud perfectly and did *nothing at all* to a spore area's, at any value down
    /// to zero (live-confirmed 2026-07-28). The reason is that the spore areas' two
    /// clouds are drawn by custom Shader Graph shaders - <c>SmokeParticle</c>
    /// (material "Spore Clouds2") and <c>GD/FireParticle</c> (material "Indivual
    /// Spores") - and a Shader Graph only reads vertex color if its author wired
    /// that node in. These didn't. What they do expose is an explicit
    /// <c>_Opacity</c> float.
    ///
    /// So: <b>if the shader declares an opacity float of its own, that is the lever;
    /// otherwise fall back to the vertex-color alpha.</b> Exactly one of the two is
    /// ever applied to a given system (the other is actively restored), because a
    /// shader that honours both would otherwise dim twice and land at the square of
    /// the requested opacity. Which path each system took is in the verbose log,
    /// along with the full inventory of what its shader declares.
    ///
    /// <b>Why property blocks and not <c>Renderer.material</c>.</b> These materials
    /// are shared across every cloud in the level (12-23 of them), so writing the
    /// material would either thin all of them together or, via <c>.material</c>,
    /// instantiate a private copy per renderer - the same allocation-per-object
    /// problem <c>SporeBombRecolorPatch</c> avoids. A property block is a
    /// per-renderer override with neither cost.
    ///
    /// <b>Baseline caching</b> (same pattern as <c>SporeAreaTuningPatch</c> and
    /// <c>WindChillZoneTuningPatch</c>): each system's authored values are captured
    /// the first time it is ever seen and every reapply scales from *that*, never
    /// from the current, possibly-already-thinned value. Without it, a second pass
    /// over the same live object would compound the thinning and a multiplier of 1.0
    /// would preserve whatever the last pass left instead of restoring vanilla. The
    /// cached gradients are never mutated in place - a scaled copy is always built -
    /// so the baseline stays a true original.
    /// </summary>
    internal static class ParticleOpacity
    {
        /// <summary>
        /// Float/Range shader properties treated as "this shader's own opacity dial",
        /// matched exactly (case-insensitively) rather than by prefix. Exactness is
        /// load-bearing: the same materials also declare <c>_AlphaClip</c>,
        /// <c>_AlphaCutoff</c>, <c>_AlphaRemap</c> and <c>_ClampAlpha</c>, none of
        /// which are opacity and at least one of which would produce a hard-clipped
        /// mess if scaled.
        /// </summary>
        private static readonly HashSet<string> OpacityProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "_Opacity",
            "_Alpha",
            "_Transparency",
        };

        /// <summary>
        /// Authored <c>startColor</c> of every particle system this session has
        /// touched, keyed by instance ID. See the class remarks.
        /// </summary>
        private static readonly Dictionary<int, ParticleSystem.MinMaxGradient> VanillaStartColors =
            new Dictionary<int, ParticleSystem.MinMaxGradient>();

        /// <summary>
        /// Authored value of every (renderer, opacity property) pair this session has
        /// touched. Keyed by renderer rather than material because the write is a
        /// per-renderer property-block override, while the material is shared.
        /// </summary>
        private static readonly Dictionary<(int RendererId, int PropertyId), float> VanillaOpacities =
            new Dictionary<(int, int), float>();

        /// <summary>Materials whose declared-property inventory has already been logged - see <see cref="LogInventoryOnce"/>.</summary>
        private static readonly HashSet<int> LoggedMaterials = new HashSet<int>();

        /// <summary>
        /// Applies the opacity multiplier to every particle system under
        /// <paramref name="root"/>, restoring the authored values when the multiplier
        /// is vanilla. Returns how many systems were touched, which is what lets a
        /// caller log "this hazard had no VFX to thin" rather than silently doing
        /// nothing.
        /// </summary>
        internal static int Apply(GameObject root, double multiplier)
        {
            int count = 0;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                LogInventoryOnce(renderer);

                // Exactly one path per system - see the class remarks. Whichever one
                // isn't chosen is restored to its authored value rather than merely
                // left alone, so a system can never end up carrying a leftover from
                // the other path.
                if (ApplyShaderOpacity(renderer, multiplier))
                {
                    ApplyStartColorAlpha(ps, SporeCloudOpacity.Vanilla);
                }
                else
                {
                    ApplyStartColorAlpha(ps, multiplier);
                }

                count++;
            }

            return count;
        }

        /// <summary>
        /// Scales the shader's own opacity property through a per-renderer property
        /// block, if it declares one. Returns whether it did - i.e. whether this
        /// system's translucency is handled here rather than by the vertex-color
        /// fallback.
        ///
        /// The property list is enumerated off the <see cref="Shader"/>, never off
        /// the material's serialized values: a Unity material keeps stale entries
        /// from every shader it has ever been assigned (these two carry
        /// <c>_TessEdgeLength</c>, <c>_ClearCoatMask</c> and a dozen other URP Lit
        /// leftovers), so "the material has a float called _Opacity" does not mean
        /// the shader reads one. Only the shader's declaration does.
        /// </summary>
        private static bool ApplyShaderOpacity(ParticleSystemRenderer renderer, double multiplier)
        {
            var material = renderer != null ? renderer.sharedMaterial : null;
            var shader = material != null ? material.shader : null;
            if (shader == null)
            {
                return false;
            }

            int rendererId = renderer.GetInstanceID();
            MaterialPropertyBlock block = null;

            int propertyCount = shader.GetPropertyCount();
            for (int i = 0; i < propertyCount; i++)
            {
                var type = shader.GetPropertyType(i);
                if ((type != ShaderPropertyType.Float && type != ShaderPropertyType.Range)
                    || !OpacityProperties.Contains(shader.GetPropertyName(i)))
                {
                    continue;
                }

                int propertyId = shader.GetPropertyNameId(i);
                var key = (rendererId, propertyId);
                if (!VanillaOpacities.TryGetValue(key, out float authored))
                {
                    authored = material.GetFloat(propertyId);
                    VanillaOpacities[key] = authored;
                }

                if (block == null)
                {
                    block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block);
                }

                // Restoring writes the authored value back explicitly rather than
                // clearing the block: it may legitimately carry other properties the
                // game itself set, and there's no API to remove a single entry.
                block.SetFloat(
                    propertyId,
                    SporeCloudOpacity.IsVanilla(multiplier)
                        ? authored
                        : SporeCloudOpacity.ScaleAlpha(authored, multiplier));
            }

            if (block == null)
            {
                return false;
            }

            renderer.SetPropertyBlock(block);
            return true;
        }

        /// <summary>
        /// The fallback lever: the per-particle vertex color, which every stock
        /// particle shader multiplies into its result. Used only for systems whose
        /// shader exposes no opacity of its own.
        /// </summary>
        private static void ApplyStartColorAlpha(ParticleSystem ps, double multiplier)
        {
            int id = ps.GetInstanceID();
            var main = ps.main;
            if (!VanillaStartColors.TryGetValue(id, out ParticleSystem.MinMaxGradient authored))
            {
                authored = main.startColor;
                VanillaStartColors[id] = authored;
            }

            main.startColor = SporeCloudOpacity.IsVanilla(multiplier)
                ? authored
                : ScaleAlpha(authored, multiplier);
        }

        /// <summary>
        /// A copy of a start-color gradient with every alpha scaled.
        /// <see cref="ParticleSystem.MinMaxGradient"/> carries a different pair of
        /// its fields depending on its <c>mode</c>, and reading the wrong one for
        /// the mode returns garbage rather than throwing - hence the explicit switch
        /// over all five modes instead of touching <c>color</c> and hoping.
        /// </summary>
        private static ParticleSystem.MinMaxGradient ScaleAlpha(ParticleSystem.MinMaxGradient authored, double multiplier)
        {
            switch (authored.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return new ParticleSystem.MinMaxGradient(WithScaledAlpha(authored.color, multiplier));

                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(
                        WithScaledAlpha(authored.colorMin, multiplier),
                        WithScaledAlpha(authored.colorMax, multiplier));

                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(
                        ScaleAlpha(authored.gradientMin, multiplier),
                        ScaleAlpha(authored.gradientMax, multiplier));

                case ParticleSystemGradientMode.Gradient:
                case ParticleSystemGradientMode.RandomColor:
                    // Both modes read the same `gradient` field; only RandomColor's
                    // sampling differs, so the mode has to be put back after
                    // construction (the Gradient constructor always sets Gradient).
                    var scaled = new ParticleSystem.MinMaxGradient(ScaleAlpha(authored.gradient, multiplier));
                    scaled.mode = authored.mode;
                    return scaled;

                default:
                    return authored;
            }
        }

        /// <summary>
        /// A new gradient with the same color keys and every alpha key scaled. Built
        /// fresh rather than edited in place: the authored <see cref="Gradient"/> is
        /// a reference type shared with the cached baseline (and quite possibly with
        /// the source asset), so mutating it would destroy the very thing the
        /// restore path reads back.
        /// </summary>
        private static Gradient ScaleAlpha(Gradient authored, double multiplier)
        {
            // Both accessors return fresh arrays, so these are already copies.
            var alphaKeys = authored.alphaKeys;
            for (int i = 0; i < alphaKeys.Length; i++)
            {
                alphaKeys[i].alpha = SporeCloudOpacity.ScaleAlpha(alphaKeys[i].alpha, multiplier);
            }

            var result = new Gradient { mode = authored.mode };
            result.SetKeys(authored.colorKeys, alphaKeys);
            return result;
        }

        private static Color WithScaledAlpha(Color authored, double multiplier) =>
            new Color(authored.r, authored.g, authored.b, SporeCloudOpacity.ScaleAlpha(authored.a, multiplier));

        /// <summary>
        /// One verbose line per distinct particle material: its shader, its render
        /// mode, every property that shader declares, and which of them this class
        /// would use as the opacity lever.
        ///
        /// Particle shaders are Unity <em>assets</em>, not code, so nothing in the
        /// decompile says whether a given one honours per-particle alpha or exposes
        /// an opacity of its own - and guessing that it did is exactly what made the
        /// first version of this feature a no-op on spore areas. When a cloud doesn't
        /// thin, this line is the answer rather than the next guess. Same reasoning
        /// as <c>SporeBombRecolorPatch.LogShaderInventoryOnce</c> and
        /// <c>Diagnostics/MaterialProbe</c>.
        /// </summary>
        private static void LogInventoryOnce(ParticleSystemRenderer renderer)
        {
            if (!Diag.Enabled || renderer == null)
            {
                return;
            }

            var material = renderer.sharedMaterial;
            var shader = material != null ? material.shader : null;
            if (shader == null || !LoggedMaterials.Add(material.GetInstanceID()))
            {
                return;
            }

            var parts = new List<string>();
            int propertyCount = shader.GetPropertyCount();
            for (int i = 0; i < propertyCount; i++)
            {
                var type = shader.GetPropertyType(i);
                string name = shader.GetPropertyName(i);
                bool isLever = (type == ShaderPropertyType.Float || type == ShaderPropertyType.Range)
                    && OpacityProperties.Contains(name);
                parts.Add($"{name}:{type}{(isLever ? " <-- opacity lever" : string.Empty)}");
            }

            Diag.V(
                $"[ParticleOpacity] material \"{material.name}\" on \"{renderer.name}\": shader \"{shader.name}\", " +
                $"render mode {renderer.renderMode}, declares: " +
                (parts.Count == 0 ? "(nothing)" : string.Join(", ", parts)));
        }
    }
}
