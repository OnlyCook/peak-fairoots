using System;
using System.Collections.Generic;
using Fairoots.Core;
using Fairoots.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fairoots.SporeBombs
{
    /// <summary>
    /// Applies <see cref="SporeBombRecolor"/>'s hue replacement to every spore
    /// bomb's renderers, so a green hazard sitting on green ground stops reading
    /// as scenery. See that class for the color math and why it's a hue
    /// replacement rather than a tint.
    ///
    /// Not a Harmony patch despite the file name convention in this folder -
    /// there's nothing to intercept, the scene objects are just already there.
    /// Driven the same way the trigger-radius shrink is: once per Roots level
    /// load from <see cref="SporeBombCullPatch.Run"/> (which walks the same
    /// candidate list it already has), plus a scene-wide
    /// <see cref="ReapplyToAll"/> wired to the setting's <c>SettingChanged</c>
    /// in <c>Plugin.Awake</c> so toggling it takes effect immediately.
    ///
    /// <b>Purely client-side and purely cosmetic</b> - the one setting in this
    /// mod deliberately outside host authority (see <c>PluginConfig</c>).
    ///
    /// <b>Which shader properties get recolored.</b> Every <c>Color</c>-typed
    /// property the shader declares, minus <see cref="ExcludedProperties"/>,
    /// enumerated live off the shader rather than guessed by name. Two earlier
    /// versions guessed and both produced the same class of artifact - a
    /// mushroom whose crevices and shading bands were recolored independently
    /// of its surface (pink veins over an otherwise-green body), because
    /// PEAK's <c>W/Peak_Standard</c> shader drives its stylized look from
    /// several color slots at once and recoloring a subset desynchronizes them.
    /// Uniformity requires doing all of them or none. Slot names vary per
    /// shader (<c>_BaseColor</c>, <c>_TopColor</c>, <c>_Color1</c>...), which is
    /// exactly why this enumerates instead of hardcoding - and why
    /// <see cref="MaterialProbe"/> exists to show the real list.
    ///
    /// <b>Implementation notes.</b>
    /// <list type="bullet">
    /// <item>Writes through a <see cref="MaterialPropertyBlock"/>, never
    /// <c>Renderer.material</c>: the latter instantiates a private material copy
    /// per renderer, which with 400+ spore bombs in a Roots level (confirmed
    /// count) means 400+ material allocations and a broken batch for each. A
    /// property block is a per-renderer override with neither cost.</item>
    /// <item>Reads the original color off <see cref="Renderer.sharedMaterials"/>
    /// (never <c>.materials</c>, same instantiation problem) and caches it the
    /// first time each renderer/submaterial/property is seen, so restoring on
    /// toggle-off writes back the true original rather than whatever the last
    /// recolor left behind - the same baseline-caching pattern as
    /// <see cref="SporeBombCullPatch"/>'s vanilla trigger radii.</item>
    /// <item>Restricted to <see cref="MeshRenderer"/>/
    /// <see cref="SkinnedMeshRenderer"/>. Particle/trail renderers under the
    /// same object drive their color from their own <c>ParticleSystem</c>
    /// modules, not the material color.</item>
    /// </list>
    /// </summary>
    internal static class SporeBombRecolorPatch
    {
        /// <summary>
        /// Color properties never recolored, because they don't describe the
        /// surface's own color: specular/rim highlights (a colored highlight is
        /// a lighting property, not an albedo one), emission (HDR, and a glow
        /// is better left as the artist set it than scaled by a hue swap), and
        /// the character-only status/skin slots, which exist on shaders shared
        /// with character meshes and are driven by the game itself.
        /// </summary>
        private static readonly HashSet<string> ExcludedProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "_SpecColor",
            "_EmissionColor",
            "_RimColor",
            "_StatusColor",
            "_SkinColor",
        };

        /// <summary>
        /// Vanilla color of every (renderer, submaterial slot, property) this
        /// session has ever recolored, captured the first time it's seen - see
        /// the class remarks.
        /// </summary>
        private static readonly Dictionary<(int RendererId, int MaterialIndex, int PropertyId), Color> VanillaColors =
            new Dictionary<(int, int, int), Color>();

        /// <summary>
        /// Shaders whose full color-property inventory has already been logged.
        /// A Roots level holds 400+ spore bombs across a handful of distinct
        /// materials, so the inventory is worth exactly one line per shader, not
        /// one per renderer.
        /// </summary>
        private static readonly HashSet<int> LoggedShaders = new HashSet<int>();

        /// <summary>
        /// The live Spores status color, cached the first time it can actually
        /// be read. <c>CharacterAfflictions.colorSpores</c> is a serialized
        /// inspector value (constant per game build), so one successful read is
        /// enough - but a Roots level can finish loading before
        /// <c>Character.localCharacter</c> exists, hence the caching rather than
        /// resolving once at startup.
        /// </summary>
        private static Rgb? _sporeColor;

        /// <summary>
        /// Applies (or removes) the recolor on one spore bomb's renderers.
        /// Called per kept candidate by <see cref="SporeBombCullPatch.Run"/>.
        /// </summary>
        internal static void Apply(Transform candidate, Rgb sporeColor, bool enabled)
        {
            foreach (var renderer in candidate.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                {
                    continue;
                }

                // Belt-and-braces scope guard: a spore bomb is scenery, never a
                // pickup. Anything with an Item anywhere up its parent chain is
                // something the player can hold (berries, food, tools) and is by
                // definition not part of the hazard - so even if a pickup ends
                // up parented under a spore-bomb-named object, or a prop group
                // happens to carry a matching name, it can't be recolored by
                // accident. Items also drive their own MaterialPropertyBlock for
                // the hover-highlight (`Item.PROPERTY_INTERACTABLE`), which is
                // one more reason not to be writing into theirs.
                if (renderer.GetComponentInParent<Item>() != null)
                {
                    Diag.V($"[SporeBombRecolor] skipped {Path(renderer.transform)} - it belongs to an Item, not scenery");
                    continue;
                }

                ApplyToRenderer(renderer, sporeColor, enabled);
            }
        }

        private static void ApplyToRenderer(Renderer renderer, Rgb sporeColor, bool enabled)
        {
            var materials = renderer.sharedMaterials;
            int rendererId = renderer.GetInstanceID();

            for (int slot = 0; slot < materials.Length; slot++)
            {
                var material = materials[slot];
                var shader = material != null ? material.shader : null;
                if (shader == null)
                {
                    continue;
                }

                LogShaderInventoryOnce(material, shader);

                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block, slot);
                bool wrote = false;

                int propertyCount = shader.GetPropertyCount();
                for (int i = 0; i < propertyCount; i++)
                {
                    if (shader.GetPropertyType(i) != ShaderPropertyType.Color)
                    {
                        continue;
                    }

                    string name = shader.GetPropertyName(i);
                    if (ExcludedProperties.Contains(name))
                    {
                        continue;
                    }

                    int propertyId = shader.GetPropertyNameId(i);
                    var key = (rendererId, slot, propertyId);
                    if (!VanillaColors.TryGetValue(key, out Color vanilla))
                    {
                        vanilla = material.GetColor(propertyId);
                        VanillaColors[key] = vanilla;
                    }

                    // Alpha is carried through untouched: on these shaders it
                    // isn't opacity but a per-slot blend weight (an unused slot
                    // is authored with alpha 0), so rewriting it would enable or
                    // disable parts of the look rather than recolor them.
                    Color next = enabled
                        ? ToColor(SporeBombRecolor.Recolor(ToRgb(vanilla), sporeColor, SporeBombRecolor.SaturationBlend), vanilla.a)
                        : vanilla;

                    // Restoring writes the vanilla color back explicitly rather
                    // than clearing the block: the block may legitimately carry
                    // other properties the game itself set, and there's no API
                    // to remove a single entry.
                    block.SetColor(propertyId, next);
                    wrote = true;
                }

                if (wrote)
                {
                    renderer.SetPropertyBlock(block, slot);
                }
            }
        }

        /// <summary>
        /// One log line per distinct shader listing every color slot it declares
        /// and the material's value for each, marking which ones the recolor
        /// skips. This is the inventory that makes a wrong-looking result
        /// diagnosable from a log alone - see <see cref="MaterialProbe"/> for
        /// the on-demand version.
        /// </summary>
        private static void LogShaderInventoryOnce(Material material, Shader shader)
        {
            if (!Diag.Enabled || !LoggedShaders.Add(shader.GetInstanceID()))
            {
                return;
            }

            var parts = new List<string>();
            int propertyCount = shader.GetPropertyCount();
            for (int i = 0; i < propertyCount; i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Color)
                {
                    continue;
                }

                string name = shader.GetPropertyName(i);
                Color value = material.GetColor(shader.GetPropertyNameId(i));
                parts.Add(
                    $"{name}={ToRgb(value)}a={value.a:0.##}" +
                    (ExcludedProperties.Contains(name) ? " (excluded)" : string.Empty));
            }

            Diag.Info(
                $"[SporeBombRecolor] shader \"{shader.name}\" (e.g. material \"{material.name}\") color slots: " +
                (parts.Count == 0 ? "(none)" : string.Join(", ", parts)));
        }

        /// <summary>
        /// Forces every spore bomb currently in the loaded scene to re-resolve
        /// its color against the current config - the immediate-effect path for
        /// the setting being toggled in-game, mirroring
        /// <see cref="SporeBombCullPatch.ReapplyTriggerRadiusToAll"/> (including
        /// its deliberately heavy full-scene scan: this runs on a config change,
        /// not during play).
        /// </summary>
        internal static void ReapplyToAll()
        {
            if (Plugin.Cfg == null)
            {
                return;
            }

            try
            {
                bool enabled = RootsState.Active && Plugin.Cfg.RecolorSporeBombs.Value;
                Rgb sporeColor = ResolveSporeColor();

                int found = 0;
                foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
                {
                    if (!SporeBombCullPatch.ClassifySporeBomb(t.name))
                    {
                        continue;
                    }

                    found++;
                    Apply(t, sporeColor, enabled);
                }

                Diag.Info(
                    $"[SporeBombRecolor] full refresh: {found} spore bomb(s) found scene-wide, " +
                    $"recolor={(enabled ? "ON" : "OFF")}, target hue from {sporeColor}");
            }
            catch (Exception e)
            {
                Diag.Error($"[SporeBombRecolor] ReapplyToAll threw: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// The game's Spores status color (<c>CharacterAfflictions.colorSpores</c>
        /// - the same one <c>PlayParticle</c> pulses the player with), falling
        /// back to <see cref="SporeBombRecolor.FallbackSporeColor"/> when
        /// there's no local character to read it off yet. Cached on the first
        /// successful read; until then a level that loaded early keeps asking,
        /// so it picks up the real color on the next reapply.
        /// </summary>
        internal static Rgb ResolveSporeColor()
        {
            if (_sporeColor.HasValue)
            {
                return _sporeColor.Value;
            }

            try
            {
                var character = Character.localCharacter;
                var afflictions = character != null && character.refs != null ? character.refs.afflictions : null;
                if (afflictions != null && afflictions.colorSpores.maxColorComponent > 0f)
                {
                    var live = ToRgb(afflictions.colorSpores);
                    _sporeColor = live;
                    var hsv = SporeBombRecolor.ToHsv(live);
                    Diag.Info(
                        $"[SporeBombRecolor] read live Spores status color {live} " +
                        $"(hue={hsv.H:0.#}deg, saturation={hsv.S:0.##})");
                    return live;
                }
            }
            catch (Exception e)
            {
                Diag.V($"[SporeBombRecolor] could not read the live Spores status color ({e.GetType().Name}) - using the fallback");
            }

            return SporeBombRecolor.FallbackSporeColor;
        }

        /// <summary>Full hierarchy path of a transform, for logs that have to be readable against a real scene.</summary>
        private static string Path(Transform t)
        {
            string path = t.name;
            for (var cur = t.parent; cur != null; cur = cur.parent)
            {
                path = cur.name + "/" + path;
                if (cur.name == "Roots Segment")
                {
                    break;
                }
            }

            return path;
        }

        private static Rgb ToRgb(Color c) => new Rgb(c.r, c.g, c.b);

        private static Color ToColor(Rgb c, float alpha) => new Color((float)c.R, (float)c.G, (float)c.B, alpha);
    }
}
