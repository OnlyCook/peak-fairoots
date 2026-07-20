using System.Collections.Generic;
using System.Linq;
using Fairoots.SporeBombs;
using HarmonyLib;
using UnityEngine;

namespace Fairoots.Diagnostics
{
    /// <summary>
    /// Dev-only verification tool: postfixes the vanilla ping RPC
    /// (<c>PointPinger.ReceivePoint_Rpc</c>) so that, with debug logging on,
    /// throwing a normal ping (default bound key) dumps everything within 3m of
    /// exactly where it landed. Requested as a more precise alternative to
    /// <see cref="SceneDiagnostics.ProbeFoliageNearestSporeBomb"/>'s "nearest
    /// candidate to the player" heuristic, which picked the wrong spore bomb once
    /// several were clustered together - aiming a ping lets the exact spot be
    /// chosen deliberately instead. Purely additive (a postfix, not a replacement)
    /// so it never interferes with the ping itself actually landing/rendering.
    /// </summary>
    [HarmonyPatch(typeof(PointPinger), "ReceivePoint_Rpc")]
    internal static class PingRadiusProbePatch
    {
        private const float ProbeRadius = 3f;

        private static void Postfix(Vector3 point)
        {
            if (!Diag.Enabled)
            {
                return;
            }

            try
            {
                Diag.Info($"===== Fairoots ping-radius probe (radius={ProbeRadius:0}m @ {Fmt(point)}) =====");

                var (nearestDist, nearestOwner, countWithinRadius, maxHeightAbove) = SporeBombCullPatch.NearestFoliageVertex(point);
                Diag.Info(
                    $"[PingProbe] nearest foliage vertex (against the exact set the last cull run used, whole-level, " +
                    $"not just this 3m radius): {nearestDist:0.00}m, countWithinRadius={countWithinRadius}, " +
                    $"maxHeightAbove={maxHeightAbove:0.00}m, owner=[{nearestOwner}]");

                var cols = Physics.OverlapSphere(point, ProbeRadius);
                Diag.Info($"[PingProbe] {cols.Length} collider(s) within {ProbeRadius:0}m:");
                foreach (var c in cols)
                {
                    Diag.Info($"[PingProbe]   collider \"{c.gameObject.name}\" tag={c.tag} layer={LayerMask.LayerToName(c.gameObject.layer)}");
                }

                int shown = 0, skippedCharacter = 0;
                foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>(true))
                {
                    if (Vector3.Distance(r.transform.position, point) > ProbeRadius)
                    {
                        continue;
                    }

                    if (r.GetComponentInParent<Character>() != null)
                    {
                        skippedCharacter++;
                        continue;
                    }

                    if (shown++ >= 40)
                    {
                        Diag.Info("[PingProbe]   ...(more renderers, truncated)");
                        break;
                    }

                    var chain = new List<string>();
                    var anc = r.transform.parent;
                    for (int i = 0; i < 5 && anc != null; i++, anc = anc.parent)
                    {
                        chain.Add(anc.name);
                    }

                    string meshInfo = "";
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        var mesh = mf.sharedMesh;
                        if (mesh.isReadable)
                        {
                            float nearest = float.MaxValue;
                            var verts = mesh.vertices;
                            var m = r.transform.localToWorldMatrix;
                            for (int vi = 0; vi < verts.Length; vi++)
                            {
                                float d = Vector3.Distance(m.MultiplyPoint3x4(verts[vi]), point);
                                if (d < nearest)
                                {
                                    nearest = d;
                                }
                            }

                            meshInfo = $" mesh(readable, verts={verts.Length}, nearestVertexDist={nearest:0.00})";
                        }
                        else
                        {
                            meshInfo = $" mesh(NOT readable, verts={mesh.vertexCount})";
                        }
                    }

                    Diag.Info(
                        $"[PingProbe]   renderer \"{r.gameObject.name}\" @ {Fmt(r.transform.position)} " +
                        $"tag={r.tag} layer={LayerMask.LayerToName(r.gameObject.layer)} " +
                        $"boundsSize={Fmt(r.bounds.size)}{meshInfo} parents=[{string.Join(" < ", chain)}]");
                }

                Diag.Info($"[PingProbe] (skipped {skippedCharacter} character-attached renderer(s))");
                Diag.Info("===== end ping-radius probe =====");
            }
            catch (System.Exception e)
            {
                Diag.Error($"[PingProbe] threw: {e.GetType().Name}: {e.Message}");
            }
        }

        private static string Fmt(Vector3 p) => $"({p.x:0.0}, {p.y:0.0}, {p.z:0.0})";
    }
}
