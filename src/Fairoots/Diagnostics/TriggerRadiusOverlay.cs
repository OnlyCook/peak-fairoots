using System;
using System.Collections.Generic;
using Fairoots.Core;
using Fairoots.SporeBombs;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fairoots.Diagnostics
{
    /// <summary>
    /// Dev-only "trigger box" indicator - draws an actual 3D wireframe (red) around
    /// every kept spore bomb's live trigger <see cref="Collider"/>(s), matching its
    /// exact current shape/size/orientation (a wire sphere for a
    /// <see cref="SphereCollider"/>, a wire cube for a <see cref="BoxCollider"/> -
    /// whatever the object actually has, per <see cref="SporeBombCullPatch"/>'s
    /// trigger-radius shrink pass), so the configured trigger-radius multiplier
    /// (ROADMAP.md "Spore bomb trigger radius" row) can be eyeballed directly
    /// against the real prefab rather than an approximate 2D screen projection - an
    /// earlier version of this overlay did exactly that (a 2D <c>OnGUI</c> circle)
    /// and it wasn't intuitive for visualizing a 3D volume. Deliberately only drawn
    /// within <see cref="MaxDrawDistance"/> - with hundreds of spore bombs on a
    /// Roots level, drawing all of them at once is unnecessary clutter; only the
    /// one(s) actually nearby matter for eyeballing.
    ///
    /// Uses immediate-mode <c>GL</c> drawing hooked into
    /// <see cref="RenderPipelineManager.endCameraRendering"/> - **not** the legacy
    /// <c>Camera.onPostRender</c> callback an earlier version of this file used,
    /// which silently never fires: PEAK ships
    /// <c>Unity.RenderPipelines.Universal.Runtime.dll</c> in its Managed folder, i.e.
    /// it runs on URP (a Scriptable Render Pipeline), and SRPs bypass that legacy
    /// callback entirely. Confirmed by manual playtest (nothing rendered) and by
    /// checking the game's installed assemblies. Since URP doesn't set up the fixed-
    /// function `GL` projection/modelview state the way the built-in pipeline did,
    /// this explicitly loads the camera's own matrices before drawing
    /// (<see cref="GL.LoadProjectionMatrix"/> / <see cref="GL.modelview"/>) - without
    /// that, lines would draw in whatever stale/identity space happened to be bound.
    ///
    /// Gated on debug logging plus its own opt-in toggle
    /// (<c>Debug/show-spore-bomb-trigger-radius</c>, off by default).
    /// </summary>
    internal static class TriggerRadiusOverlay
    {
        private const float MaxDrawDistance = 10f;
        private const int CircleSegments = 32;

        /// <summary>
        /// World-space thickness (meters) of the wireframe, drawn as camera-facing
        /// quads rather than raw <c>GL.LINES</c> - hairline `GL.LINES` render at a
        /// literal single pixel on most modern GPUs/URP regardless of any
        /// "line width" setting (that fixed-function knob is ignored almost
        /// everywhere today), so a visibly thick line has to be built from
        /// geometry. A fixed world-space size (rather than a fixed screen-space
        /// pixel size) is fine here since this overlay only ever draws within
        /// <see cref="MaxDrawDistance"/> of the camera - it won't look thin from far
        /// away or absurdly fat up close the way a naive fixed-thickness line would
        /// at arbitrary distance.
        /// </summary>
        private const float LineThickness = 0.05f;

        private static Material _lineMaterial;
        private static bool _initialized;
        private static bool _loggedFailure;

        /// <summary>
        /// Reused every frame to avoid a per-frame allocation - holds the
        /// flattened "cap" disc(s) found while drawing wireframes this pass, so
        /// the semi-transparent fill (see <see cref="OnEndCameraRendering"/>) can
        /// be drawn in a second, separate <c>GL.TRIANGLES</c> block afterward
        /// without recomputing the cut-plane geometry.
        /// </summary>
        private static readonly List<(Vector3 Center, float Radius)> PendingCapDiscs = new List<(Vector3, float)>();

        /// <summary>Hook the render callback once at plugin startup. Safe to call more than once.</summary>
        internal static void Init()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        private static void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (Plugin.Cfg == null || !Plugin.Cfg.EnableDebugLogging.Value || !Plugin.Cfg.ShowSporeBombTriggerRadius.Value)
            {
                return;
            }

            if (cam == null || cam != Camera.main)
            {
                return;
            }

            var kept = SporeBombCullPatch.KeptTriggerColliders;
            if (kept.Count == 0)
            {
                return;
            }

            try
            {
                EnsureMaterial();
                if (_lineMaterial == null)
                {
                    return;
                }

                _lineMaterial.SetPass(0);

                PendingCapDiscs.Clear();

                GL.PushMatrix();
                GL.LoadProjectionMatrix(cam.projectionMatrix);
                GL.modelview = cam.worldToCameraMatrix;
                GL.Begin(GL.QUADS);
                GL.Color(Color.red);

                Vector3 camPos = cam.transform.position;
                for (int i = 0; i < kept.Count; i++)
                {
                    var col = kept[i];
                    if (col == null)
                    {
                        continue; // culled/despawned since the last scan
                    }

                    if (Vector3.Distance(camPos, col.transform.position) > MaxDrawDistance)
                    {
                        continue;
                    }

                    DrawColliderWireframe(col, camPos);
                }

                GL.End();

                // A thin ring around the cut plane barely reads in a screenshot -
                // fill it in as a solid, semi-transparent disc in a second pass so
                // the flattening is unmistakable at a glance (per the maintainer's
                // ask to make the cut "look bigger" - the cutoff height itself is
                // unchanged, only how obviously it's drawn).
                if (PendingCapDiscs.Count > 0)
                {
                    GL.Begin(GL.TRIANGLES);
                    GL.Color(new Color(1f, 0f, 0f, 0.55f));
                    for (int i = 0; i < PendingCapDiscs.Count; i++)
                    {
                        DrawFilledDisc(PendingCapDiscs[i].Center, PendingCapDiscs[i].Radius);
                    }

                    GL.End();
                }

                GL.PopMatrix();
            }
            catch (Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true; // don't spam every frame if this keeps throwing.
                    Diag.Error($"[TriggerRadiusOverlay] threw: {e.GetType().Name}: {e.Message}");
                }
            }
        }

        private static void DrawColliderWireframe(Collider col, Vector3 camPos)
        {
            switch (col)
            {
                case SphereCollider sphere:
                {
                    Vector3 worldCenter = sphere.transform.TransformPoint(sphere.center);
                    float worldRadius = sphere.radius * MaxAbsAxis(sphere.transform.lossyScale);
                    DrawWireSphere(worldCenter, worldRadius, camPos, CutoffY(sphere));
                    break;
                }
                case BoxCollider box:
                    DrawWireBox(box.transform, box.center, box.size, camPos);
                    break;
            }
        }

        /// <summary>
        /// The world-space Y above which <see cref="SporeBombHeightGatePatch"/>
        /// actually suppresses the trigger for this collider's spore bomb, or
        /// <c>null</c> if the cutoff doesn't apply here (disabled, the round
        /// "Explosive Spore Bomb" variant, or <see cref="PluginConfig.KeepVanillaTriggerRadius"/>
        /// is on - matching that patch's own bypass, so a "vanilla" comparison
        /// screenshot shows a genuinely full, uncut sphere) - so the wireframe
        /// visually flattens at the exact same plane the gameplay fix uses, not an
        /// approximation. Base height matches <see cref="SporeBombHeightGatePatch"/>'s
        /// own <c>__instance.transform.position.y</c> convention exactly.
        /// </summary>
        private static float? CutoffY(SphereCollider sphere)
        {
            if (Plugin.Cfg.KeepVanillaTriggerRadius.Value)
            {
                return null;
            }

            float maxHeight = SporeBombExplosionTuning.ResolveTriggerHeightCutoffMeters(
                Plugin.Cfg.EffectiveSporeBombTriggerHeightMultiplier);
            if (maxHeight <= 0f)
            {
                return null;
            }

            string name = sphere.gameObject.name;
            if (!SporeBombCullPatch.ClassifySporeBomb(name) || SporeBombCullPatch.IsExplosiveVariant(name))
            {
                return null;
            }

            // maxHeight is meters; the returned y is a world-space coordinate.
            return sphere.transform.position.y + GameUnits.MetersToUnits(maxHeight);
        }

        private static float MaxAbsAxis(Vector3 v) =>
            Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>
        /// Draws a wire sphere, optionally flattened at <paramref name="cutoffY"/>
        /// (world-space height) to visually match
        /// <see cref="SporeBombHeightGatePatch"/>'s actual trigger-suppression
        /// plane - anything above that height is clipped away from the two
        /// vertical meridian circles, and a flat "cap" circle is drawn at exactly
        /// that height to show the cut clearly, the same way the reference
        /// screenshot that prompted this marks it.
        /// </summary>
        private static void DrawWireSphere(Vector3 center, float radius, Vector3 camPos, float? cutoffY)
        {
            DrawCircle(center, radius, Vector3.right, Vector3.up, camPos, cutoffY);
            DrawCircle(center, radius, Vector3.forward, Vector3.up, camPos, cutoffY);
            DrawCircle(center, radius, Vector3.right, Vector3.forward, camPos, cutoffY);

            if (cutoffY == null)
            {
                return;
            }

            float heightAboveCenter = cutoffY.Value - center.y;
            if (Mathf.Abs(heightAboveCenter) >= radius)
            {
                return; // cutoff plane doesn't actually intersect the sphere - nothing to cap.
            }

            float capRadius = Mathf.Sqrt(radius * radius - heightAboveCenter * heightAboveCenter);
            Vector3 capCenter = new Vector3(center.x, cutoffY.Value, center.z);
            DrawCircle(capCenter, capRadius, Vector3.right, Vector3.forward, camPos, cutoffAtCenter: null);
            PendingCapDiscs.Add((capCenter, capRadius));
        }

        private static void DrawCircle(Vector3 center, float radius, Vector3 axisA, Vector3 axisB, Vector3 camPos, float? cutoffAtCenter)
        {
            Vector3 prev = center + axisA * radius;
            for (int i = 1; i <= CircleSegments; i++)
            {
                float t = i / (float)CircleSegments * Mathf.PI * 2f;
                Vector3 next = center + (axisA * Mathf.Cos(t) + axisB * Mathf.Sin(t)) * radius;
                DrawClippedSegment(prev, next, camPos, cutoffAtCenter);
                prev = next;
            }
        }

        /// <summary>
        /// Draws a line segment as-is if no cutoff applies, drops it entirely if
        /// both endpoints are above <paramref name="cutoffY"/>, or clips it to the
        /// exact plane crossing if only one endpoint is above - the same
        /// above/below split <see cref="SporeBombHeightGatePatch"/> tests at
        /// runtime, just applied to geometry instead of a trigger decision.
        /// </summary>
        private static void DrawClippedSegment(Vector3 a, Vector3 b, Vector3 camPos, float? cutoffY)
        {
            if (cutoffY == null)
            {
                DrawThickLine(a, b, camPos);
                return;
            }

            float cutoff = cutoffY.Value;
            bool aBelow = a.y <= cutoff;
            bool bBelow = b.y <= cutoff;

            if (aBelow && bBelow)
            {
                DrawThickLine(a, b, camPos);
                return;
            }

            if (!aBelow && !bBelow)
            {
                return; // fully above the cutoff plane - clipped away entirely.
            }

            float t = (cutoff - a.y) / (b.y - a.y);
            Vector3 cross = Vector3.Lerp(a, b, t);
            DrawThickLine(aBelow ? a : cross, aBelow ? cross : b, camPos);
        }

        /// <summary>
        /// Fills a cap disc (see <see cref="PendingCapDiscs"/>) as a triangle fan.
        /// Cap discs are always built flat in the horizontal (X/Z) plane (see
        /// <see cref="DrawWireSphere"/>), so unlike <see cref="DrawThickLine"/>
        /// this needs no camera-facing computation - it's genuinely a horizontal
        /// surface, not a billboard standing in for a thin line.
        /// </summary>
        private static void DrawFilledDisc(Vector3 center, float radius)
        {
            Vector3 prev = center + Vector3.right * radius;
            for (int i = 1; i <= CircleSegments; i++)
            {
                float t = i / (float)CircleSegments * Mathf.PI * 2f;
                Vector3 next = center + (Vector3.right * Mathf.Cos(t) + Vector3.forward * Mathf.Sin(t)) * radius;
                GL.Vertex(center);
                GL.Vertex(prev);
                GL.Vertex(next);
                prev = next;
            }
        }

        private static void DrawWireBox(Transform transform, Vector3 center, Vector3 size, Vector3 camPos)
        {
            Vector3 e = size * 0.5f;
            Vector3[] local =
            {
                center + new Vector3(-e.x, -e.y, -e.z),
                center + new Vector3(e.x, -e.y, -e.z),
                center + new Vector3(e.x, -e.y, e.z),
                center + new Vector3(-e.x, -e.y, e.z),
                center + new Vector3(-e.x, e.y, -e.z),
                center + new Vector3(e.x, e.y, -e.z),
                center + new Vector3(e.x, e.y, e.z),
                center + new Vector3(-e.x, e.y, e.z),
            };

            var world = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                world[i] = transform.TransformPoint(local[i]);
            }

            // bottom face, top face, verticals connecting them.
            DrawEdge(world, 0, 1, camPos); DrawEdge(world, 1, 2, camPos); DrawEdge(world, 2, 3, camPos); DrawEdge(world, 3, 0, camPos);
            DrawEdge(world, 4, 5, camPos); DrawEdge(world, 5, 6, camPos); DrawEdge(world, 6, 7, camPos); DrawEdge(world, 7, 4, camPos);
            DrawEdge(world, 0, 4, camPos); DrawEdge(world, 1, 5, camPos); DrawEdge(world, 2, 6, camPos); DrawEdge(world, 3, 7, camPos);
        }

        private static void DrawEdge(Vector3[] pts, int a, int b, Vector3 camPos)
        {
            DrawThickLine(pts[a], pts[b], camPos);
        }

        /// <summary>
        /// Draws a single logical line segment as a camera-facing quad
        /// <see cref="LineThickness"/> meters wide, since raw <c>GL.LINES</c>
        /// render hairline-thin regardless of any width setting on most modern
        /// graphics backends (see <see cref="LineThickness"/>'s remarks).
        /// </summary>
        private static void DrawThickLine(Vector3 a, Vector3 b, Vector3 camPos)
        {
            Vector3 lineDir = b - a;
            if (lineDir.sqrMagnitude < 1e-8f)
            {
                return;
            }

            Vector3 viewDir = (camPos - (a + b) * 0.5f).normalized;
            Vector3 side = Vector3.Cross(viewDir, lineDir).normalized * (LineThickness * 0.5f);
            if (side.sqrMagnitude < 1e-8f)
            {
                // line direction ~parallel to the view direction - fall back to an
                // arbitrary perpendicular so the quad doesn't collapse to nothing.
                side = Vector3.Cross(Vector3.up, lineDir).normalized * (LineThickness * 0.5f);
            }

            GL.Vertex(a - side);
            GL.Vertex(a + side);
            GL.Vertex(b + side);
            GL.Vertex(b - side);
        }

        private static void EnsureMaterial()
        {
            if (_lineMaterial != null)
            {
                return;
            }

            // The standard Unity-documented shader for runtime GL immediate-mode
            // drawing - normally part of the built-in resources, but URP projects
            // can strip it if nothing else references it, so fall back to a shader
            // guaranteed to exist in any URP build if it's missing.
            var shader = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Diag.Error("[TriggerRadiusOverlay] no usable line shader found (both Hidden/Internal-Colored and Universal Render Pipeline/Unlit are missing) - wireframe cannot be drawn");
                return;
            }

            _lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _lineMaterial.SetInt("_ZWrite", 0);

            // Belt-and-braces: Hidden/Internal-Colored reads per-vertex GL.Color,
            // but the URP Unlit fallback shader may not - setting its own base
            // color property too means the wireframe is still red either way.
            if (_lineMaterial.HasProperty("_BaseColor"))
            {
                _lineMaterial.SetColor("_BaseColor", Color.red);
            }

            if (_lineMaterial.HasProperty("_Color"))
            {
                _lineMaterial.SetColor("_Color", Color.red);
            }
        }
    }
}
