using Fairoots.Core;
using Fairoots.SporeBombs;
using UnityEngine;

namespace Fairoots.Diagnostics
{
    /// <summary>
    /// Dev-only "here used to be a spore bomb" indicator (not a shipped feature -
    /// requested for testing convenience while there's no other way to eyeball
    /// which candidates the Phase 4 cull actually touched). Draws a plain
    /// <c>OnGUI</c> label at the screen-space projection of every position in
    /// <see cref="SporeBombCullPatch.RemovedPositions"/>, colored/labelled by
    /// *why* it was removed - the two outcomes have nothing to do with each other
    /// (plant camouflage vs. the unrelated, foliage-independent seeded balance
    /// cull) and showing them identically caused several "false positive" reports
    /// that turned out to just be the seeded pass working as designed. Deliberately
    /// a 2D overlay rather than a 3D world marker: <c>OnGUI</c> draws on top of
    /// everything, completely unaffected by depth/occlusion, so it's visible
    /// through walls for free with no custom shader - the vanilla ping prefab was
    /// considered instead but its own visibility model actually *fades* through
    /// walls (<c>PointPing.visibilityFullNoneNoLos</c>), the opposite of what's
    /// wanted here. Gated on debug logging; costs nothing during normal play.
    /// </summary>
    internal static class RemovedMarkerOverlay
    {
        private const float MaxDrawDistance = 250f;

        internal static void Draw()
        {
            if (Plugin.Cfg == null || !Plugin.Cfg.EnableDebugLogging.Value)
            {
                return;
            }

            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            var positions = SporeBombCullPatch.RemovedPositions;
            for (int i = 0; i < positions.Count; i++)
            {
                Vector3 world = positions[i].Pos;
                float distance = Vector3.Distance(camera.transform.position, world);
                if (distance > MaxDrawDistance)
                {
                    continue;
                }

                Vector3 screen = camera.WorldToScreenPoint(world);
                if (screen.z <= 0f)
                {
                    continue; // behind the camera
                }

                bool isFoliage = positions[i].Outcome == CullOutcome.RemovedFoliage;
                string reason = isFoliage ? "FOLIAGE" : "SEEDED";
                Color color = isFoliage ? Color.red : new Color(1f, 0.6f, 0f);

                float x = screen.x;
                float y = Screen.height - screen.y;
                string label = $"REMOVED\n({reason})\n{distance:0}m";

                var rect = new Rect(x - 45f, y - 27f, 90f, 48f);
                GUI.color = Color.black;
                GUI.Box(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), string.Empty);
                GUI.color = color;
                GUI.Box(rect, string.Empty);
                GUI.color = Color.white;
                GUI.Label(rect, label);
            }

            GUI.color = Color.white;
        }
    }
}
