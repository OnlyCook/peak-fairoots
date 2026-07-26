using System;
using System.Collections.Generic;
using System.Linq;
using Fairoots.SporeBombs;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fairoots.Diagnostics
{
    /// <summary>
    /// Dumps the real material/shader setup of whatever the player is standing
    /// next to - every color property the shader actually declares, with its
    /// current value, plus which of them Fairoots has overridden.
    ///
    /// <b>Why this exists.</b> The spore-bomb recolor
    /// (<see cref="SporeBombRecolorPatch"/>) has to write a tint into *the*
    /// albedo color slot, and nothing in the decompiled C# says what that slot
    /// is called: shaders are assets, not code, and PEAK's props use stylized
    /// shaders with several color slots at once rather than stock URP Lit. The
    /// first version of the recolor guessed by tinting every color property it
    /// recognized, which live screenshots showed recolors the shading bands and
    /// crevices independently of the surface (pink veins over a green mushroom
    /// instead of a pink mushroom). Guessing again would be the same mistake -
    /// this reads the answer off the live material instead.
    ///
    /// Purely diagnostic: reads and logs, never mutates. Point it at a spore
    /// bomb to find the right slot; point it at anything that looks wrongly
    /// recolored to find out whether Fairoots touched it at all (the report says
    /// so explicitly per property, so "the mod did this" and "this is just what
    /// the prop looks like" stop being a matter of opinion).
    /// </summary>
    internal static class MaterialProbe
    {
        /// <summary>How far down the camera's view ray to look, in world units.</summary>
        private const float ProbeRange = 15f;

        /// <summary>Cap on reported renderers, nearest along the ray first.</summary>
        private const int MaxRenderers = 6;

        /// <summary>
        /// Reports whatever the player is <em>looking at</em>, not what's nearest
        /// to them. The first version reported the nearest renderers instead,
        /// which turned out to be useless in practice: the player's own body
        /// sits at 0.00m and consumed the entire report before anything in the
        /// world got a look-in.
        ///
        /// Selection is by ray-vs-renderer-bounds rather than a physics
        /// raycast, because the interesting objects here often have no collider
        /// on the mesh at all - a spore bomb's own collider is its (invisible,
        /// much larger) trigger volume, and the mushroom mesh underneath it is a
        /// separate colliderless child - so a physics ray would report the wrong
        /// thing or nothing.
        /// </summary>
        internal static void DumpLookedAt()
        {
            Diag.Info("===== Fairoots material probe =====");
            try
            {
                var player = Character.localCharacter;
                var camera = Camera.main;
                if (player == null || camera == null)
                {
                    Diag.Info("[MaterialProbe] MISSING: no local Character or main Camera yet");
                    return;
                }

                var ray = new Ray(camera.transform.position, camera.transform.forward);
                var hits = UnityEngine.Object.FindObjectsOfType<Renderer>(false)
                    .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer)
                    .Where(r => !IsOwnCharacter(r.transform, player))
                    .Select(r => (Renderer: r, Hit: r.bounds.IntersectRay(ray, out float d), Distance: Distance(r, ray)))
                    .Where(x => x.Hit && x.Distance <= ProbeRange)
                    .OrderBy(x => x.Distance)
                    .Take(MaxRenderers)
                    .ToList();

                Diag.Info(
                    $"[MaterialProbe] {hits.Count} renderer(s) under the crosshair within " +
                    $"{GameUnits.ToMeters(ProbeRange):0.0}m (nearest first, capped at {MaxRenderers}; " +
                    $"the local player's own meshes are excluded)");

                foreach (var (renderer, _, distance) in hits)
                {
                    DumpRenderer(renderer, distance);
                }
            }
            catch (Exception e)
            {
                Diag.Error($"[MaterialProbe] threw: {e.GetType().Name}: {e.Message}");
            }

            Diag.Info("===== end material probe =====");
        }

        private static float Distance(Renderer r, Ray ray)
        {
            r.bounds.IntersectRay(ray, out float d);
            return d;
        }

        /// <summary>
        /// True for the local player's own body/clothing meshes, which are
        /// always right in front of the camera and would otherwise crowd out
        /// everything actually being looked at.
        /// </summary>
        private static bool IsOwnCharacter(Transform t, Character player)
        {
            var owner = t.GetComponentInParent<Character>();
            return owner != null && owner == player;
        }

        private static void DumpRenderer(Renderer renderer, float distance)
        {
            bool isSporeBomb = IsUnderSporeBomb(renderer.transform);
            bool isItem = renderer.GetComponentInParent<Item>() != null;
            Diag.Info(
                $"[MaterialProbe] {Path(renderer.transform)} @ {GameUnits.ToMeters(distance):0.00}m " +
                $"[{renderer.GetType().Name}] sporeBomb={isSporeBomb} item={isItem}");

            var materials = renderer.sharedMaterials;
            var block = new MaterialPropertyBlock();

            for (int slot = 0; slot < materials.Length; slot++)
            {
                var material = materials[slot];
                if (material == null)
                {
                    Diag.Info($"[MaterialProbe]     slot {slot}: (null material)");
                    continue;
                }

                var shader = material.shader;
                Diag.Info($"[MaterialProbe]     slot {slot}: material \"{material.name}\" shader \"{shader?.name}\"");
                if (shader == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(block, slot);

                foreach (string line in DescribeColorProperties(shader, material, block))
                {
                    Diag.Info($"[MaterialProbe]         {line}");
                }
            }
        }

        /// <summary>
        /// Every Color-typed property the shader declares, with the material's
        /// own value and - crucially - whether a MaterialPropertyBlock is
        /// currently overriding it (that override is what Fairoots' recolor
        /// writes, so this is how you tell "the mod changed this" from "the
        /// artist authored it this way").
        /// </summary>
        private static IEnumerable<string> DescribeColorProperties(Shader shader, Material material, MaterialPropertyBlock block)
        {
            int count = shader.GetPropertyCount();
            bool any = false;

            for (int i = 0; i < count; i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Color)
                {
                    continue;
                }

                any = true;
                string name = shader.GetPropertyName(i);
                Color materialValue = material.GetColor(name);
                // A property block reports Color.clear for an entry it doesn't
                // hold, which is indistinguishable from a genuinely transparent
                // override - so report the raw value and let the reader compare
                // it against the material's own.
                Color blockValue = block.GetColor(name);
                bool overridden = blockValue != Color.clear && blockValue != materialValue;

                yield return
                    $"{name}: material=({materialValue.r:0.###}, {materialValue.g:0.###}, {materialValue.b:0.###}, {materialValue.a:0.###})" +
                    (overridden
                        ? $"  OVERRIDDEN by property block -> ({blockValue.r:0.###}, {blockValue.g:0.###}, {blockValue.b:0.###})"
                        : string.Empty);
            }

            if (!any)
            {
                yield return "(shader declares no Color properties at all)";
            }
        }

        private static bool IsUnderSporeBomb(Transform t)
        {
            for (var cur = t; cur != null; cur = cur.parent)
            {
                if (SporeBombCullPatch.ClassifySporeBomb(cur.name))
                {
                    return true;
                }
            }

            return false;
        }

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
    }
}
