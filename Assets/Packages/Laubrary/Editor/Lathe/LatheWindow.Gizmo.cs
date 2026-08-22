// LatheWindow.Gizmo — a real draggable 3-axis move handle for the selected solid, drawn over the live
// preview. Directly answers "3D vector transforms are hard to place with just numeric inputs": drag the
// red/green/blue axis line instead of typing X/Y/Z.
//
// Of the three gizmo ideas from the design conversation (redraw Unity's own gizmos in the preview; a
// compact gizmo-in-a-UI-box; move authoring into a real Scene View with actual GameObjects), this is the
// first, and the one actually built — it reuses the same camera-ray-projection technique the Skeleton Sweep
// editor already proved out (including its pixelsPerPoint fix), with no architecture change. The Scene View
// option is the strongest LONG-TERM answer (Unity's real gizmos/snapping/undo for free) but means turning
// every solid into a real scene GameObject and retiring PreviewRenderUtility as the authoring surface — a
// rewrite of how Lathe is edited, not an incremental step, so it's not attempted here. The "gizmo in a UI
// box" option is a weaker version of this one with the same camera-math needs, so it wasn't built separately.
//
// Rotate/scale handles are the natural next step (same math, a rotation ring / axis-aligned scale handle
// instead of a line) — not attempted this pass; position was the highest-value piece to get right first.
using UnityEditor;
using UnityEngine;

namespace Laubrary.Lathe.Editor
{
    public partial class LatheWindow
    {
        bool showGizmo;
        int gizmoDragAxis = -1;
        Vector3 gizmoDragStartPos;
        float gizmoDragStartT;

        static readonly Vector3[] GizmoAxes = { Vector3.right, Vector3.up, Vector3.forward };
        static readonly Color[] GizmoColors =
        {
            new Color(0.9f, 0.25f, 0.25f), new Color(0.3f, 0.85f, 0.3f), new Color(0.3f, 0.55f, 0.95f),
        };
        const float GizmoHandleLength = 1.2f;
        const float GizmoHitPx = 12f;

        static Vector2 GizmoGuiOf(Rect rect, Camera cam, Vector3 worldPos)
        {
            Vector3 sp = cam.WorldToScreenPoint(worldPos);
            float ppp = EditorGUIUtility.pixelsPerPoint;
            return new Vector2(rect.x + sp.x / ppp, rect.y + (rect.height - sp.y / ppp));
        }

        static Ray GizmoRayFromGui(Rect rect, Camera cam, Vector2 guiPos)
        {
            float ppp = EditorGUIUtility.pixelsPerPoint;
            var screenPos = new Vector3((guiPos.x - rect.x) * ppp, (rect.height - (guiPos.y - rect.y)) * ppp, 0f);
            return cam.ScreenPointToRay(screenPos);
        }

        // The point along the 3D axis line (through axisOrigin, direction axisDir) closest to the ray —
        // the standard closest-point-between-two-lines solve. Verified by hand: a ray known to pass exactly
        // through axis point t=3 returns t=3.
        static float ClosestTOnAxis(Ray ray, Vector3 axisOrigin, Vector3 axisDir)
        {
            Vector3 D = ray.direction.normalized;
            Vector3 A = axisDir.normalized;
            Vector3 w0 = axisOrigin - ray.origin;
            float b = Vector3.Dot(D, A);
            float d = Vector3.Dot(D, w0);
            float e = Vector3.Dot(A, w0);
            float denom = 1f - b * b;
            if (Mathf.Abs(denom) < 1e-6f) return 0f;   // ray parallel to the axis — no well-defined closest point
            return (b * d - e) / denom;
        }

        static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + ab * t);
        }

        void DrawAndHandleGizmo(Rect rect, LatheSolid solid)
        {
            var cam = previewRenderer?.Camera;
            if (cam == null) return;
            Vector3 origin = solid.position;
            Vector2 originGui = GizmoGuiOf(rect, cam, origin);
            var tipsGui = new Vector2[3];

            Handles.BeginGUI();
            try
            {
                for (int a = 0; a < 3; a++)
                {
                    tipsGui[a] = GizmoGuiOf(rect, cam, origin + GizmoAxes[a] * GizmoHandleLength);
                    Handles.color = a == gizmoDragAxis ? Color.white : GizmoColors[a];
                    Handles.DrawAAPolyLine(4f, originGui, tipsGui[a]);
                }
                Handles.color = Color.white;
                Handles.DrawSolidDisc(originGui, Vector3.forward, 4f);
            }
            finally { Handles.EndGUI(); }

            var e = Event.current;
            if (e == null) return;
            int id = GUIUtility.GetControlID(FocusType.Passive, rect);

            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                int hitAxis = -1;
                float bestD = GizmoHitPx;
                for (int a = 0; a < 3; a++)
                {
                    float d = DistancePointToSegment(e.mousePosition, originGui, tipsGui[a]);
                    if (d < bestD) { bestD = d; hitAxis = a; }
                }
                if (hitAxis >= 0)
                {
                    gizmoDragAxis = hitAxis;
                    gizmoDragStartPos = solid.position;
                    if (spec != null) Undo.RecordObject(spec, "Move Lathe Solid");
                    Ray ray = GizmoRayFromGui(rect, cam, e.mousePosition);
                    gizmoDragStartT = ClosestTOnAxis(ray, gizmoDragStartPos, GizmoAxes[hitAxis]);
                    GUIUtility.hotControl = id;
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id && gizmoDragAxis >= 0)
            {
                Ray ray = GizmoRayFromGui(rect, cam, e.mousePosition);
                float t = ClosestTOnAxis(ray, gizmoDragStartPos, GizmoAxes[gizmoDragAxis]);
                solid.position = gizmoDragStartPos + GizmoAxes[gizmoDragAxis] * (t - gizmoDragStartT);
                if (spec != null) EditorUtility.SetDirty(spec);
                preview?.MarkDirtyRepaint();
                e.Use();
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == id)
            {
                gizmoDragAxis = -1;
                GUIUtility.hotControl = 0;
                // The numeric Position fields in the left pane don't live-track a raw field mutation — same
                // trade-off the skeleton editor makes. Refresh them once the drag actually ends, deferred
                // for the same reentrancy reason DeferredRebuild documents.
                DeferredRebuild();
                e.Use();
            }
        }
    }
}
