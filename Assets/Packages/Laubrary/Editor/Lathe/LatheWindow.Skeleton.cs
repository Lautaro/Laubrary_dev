// LatheWindow.Skeleton — the interactive "Edit Skeleton" tool for SkeletonSweepModule: click empty space
// to place a node, click a node to select it, shift-click a second node to connect them, drag a node to
// move it, Delete/Backspace (or the button) to remove the selected one. This is the click-to-place gizmo
// authoring the original design conversation flagged as the missing piece for a hand-drawn skeleton.
//
// Nodes are stored in the SOLID's own LOCAL space (SkeletonSweepModule.nodes), but a click lands in WORLD
// space (through the live preview camera) — every hit-test/placement goes through the solid's own
// LocalToWorld/inverse to convert between the two. The turntable is frozen at 0 while editing (DrawPreview)
// so what's on screen always matches what a click is measured against.
//
// The projection/hit-test math (GuiOf/TryHit) and the click LOGIC (ProcessSkeletonClick) are plain methods,
// not closures over Event.current — Event.current can't be faked outside Unity's real GUI dispatch (its
// setter doesn't stick), so this split is what makes the click logic unit-testable at all, not just cleaner.
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Lathe.Editor
{
    public partial class LatheWindow
    {
        int skeletonSelectedNode = -1;
        int skeletonDraggingNode = -1;
        float skeletonPlacementY = 0f;

        // Called from LatheWindow.Module.cs's BuildModuleBox when the selected solid's module is a
        // SkeletonSweepModule — the extra controls the reflected card doesn't (and shouldn't) draw itself.
        VisualElement BuildSkeletonEditorControls(SkeletonSweepModule skel)
        {
            var box = Z.Section("Edit Skeleton", "Click-to-place authoring for the node/edge graph above — "
                + "an alternative to typing coordinates into the Nodes/Edges lists.", "lathe.skeletonEdit", icon: "path");

            var toggle = Z.Toggle(editingSkeleton ? "Editing — click the preview" : "Edit Skeleton",
                "While ON: click empty space to place a node, click a node to select it, SHIFT-click a "
                + "second node to connect them, drag a node to move it. The turntable freezes at 0 and the "
                + "camera stops responding to the left mouse button (middle-drag still orbits) so clicks "
                + "land where you expect.",
                editingSkeleton, v =>
                {
                    editingSkeleton = v;
                    if (!v) { skeletonSelectedNode = -1; skeletonDraggingNode = -1; }
                    preview?.MarkDirtyRepaint();
                });
            box.Add(toggle);

            box.Add(Z.MicroSlider("Placement Y", skeletonPlacementY, -5f, 5f,
                "New nodes are placed on a horizontal plane at this local height — drag it between clicks "
                + "to build in 3D rather than flat on one plane.",
                v => { skeletonPlacementY = v; preview?.MarkDirtyRepaint(); }, 150f, showValue: true));

            box.Add(Z.Button("Delete Selected Node",
                skeletonSelectedNode >= 0
                    ? $"Remove node {skeletonSelectedNode + 1} and any edges touching it (undoable)."
                    : "Select a node first (click it in the preview while Edit Skeleton is on).",
                () => DeleteSelectedSkeletonNode(skel)));

            return box;
        }

        // Rebuild() tears down and rebuilds the WHOLE window tree, including the `preview` IMGUIContainer
        // that's mid-dispatch whenever this fires from inside the click handler (itself called from
        // DrawPreview, the IMGUIContainer's own callback) — destroying that container while its callback is
        // still on the call stack is exactly the reentrancy Unity's UI Toolkit doesn't like. Deferring one
        // tick (delayCall) is the same reasoning PyrePlusWindow's own drag-commit rebuilds use.
        void DeferredRebuild()
        {
            EditorApplication.delayCall += () => { if (this != null) Rebuild(); };
        }

        void DeleteSelectedSkeletonNode(SkeletonSweepModule skel)
        {
            int del = skeletonSelectedNode;
            if (del < 0 || skel.nodes == null || del >= skel.nodes.Count) return;
            Dirty(() =>
            {
                skel.nodes.RemoveAt(del);
                skel.edges.RemoveAll(ed => ed.x == del || ed.y == del);
                for (int i = 0; i < skel.edges.Count; i++)
                {
                    var ed = skel.edges[i];
                    if (ed.x > del) ed.x--;
                    if (ed.y > del) ed.y--;
                    skel.edges[i] = ed;
                }
            });
            skeletonSelectedNode = -1;
            DeferredRebuild();
        }

        // World-space GUI position (rect-relative, y-down, in GUI POINTS) of a node given in the solid's
        // LOCAL space. Camera.WorldToScreenPoint answers in PHYSICAL pixels (PreviewRenderUtility allocates
        // its buffer at rect-size × EditorGUIUtility.pixelsPerPoint on a high-DPI display — see
        // printwindow-editor-screenshot / PyrePlusPlayback3DPreview's own note on the same gotcha), so it
        // has to be divided back down to points before it means anything in GUI space.
        static Vector2 SkeletonGuiOf(Rect rect, Matrix4x4 local2world, Camera cam, Vector3 localPos)
        {
            Vector3 wp = local2world.MultiplyPoint3x4(localPos);
            Vector3 sp = cam.WorldToScreenPoint(wp);
            float ppp = EditorGUIUtility.pixelsPerPoint;
            return new Vector2(rect.x + sp.x / ppp, rect.y + (rect.height - sp.y / ppp));
        }

        // Ray-casts a GUI-space (points) point through `cam` against the solid-local horizontal plane at
        // `planeY`, returning the hit in the solid's own LOCAL space. The inverse of SkeletonGuiOf's
        // points-vs-physical-pixels conversion.
        static bool SkeletonTryHit(Rect rect, Matrix4x4 local2world, Matrix4x4 world2local, Camera cam,
            Vector2 guiPos, float planeY, out Vector3 localHit)
        {
            localHit = Vector3.zero;
            float ppp = EditorGUIUtility.pixelsPerPoint;
            var screenPos = new Vector3((guiPos.x - rect.x) * ppp, (rect.height - (guiPos.y - rect.y)) * ppp, 0f);
            Ray ray = cam.ScreenPointToRay(screenPos);
            Vector3 planePointWorld = local2world.MultiplyPoint3x4(new Vector3(0f, planeY, 0f));
            Vector3 planeNormalWorld = local2world.MultiplyVector(Vector3.up).normalized;
            float denom = Vector3.Dot(ray.direction, planeNormalWorld);
            if (Mathf.Abs(denom) < 1e-6f) return false;
            float t = Vector3.Dot(planePointWorld - ray.origin, planeNormalWorld) / denom;
            if (t < 0f) return false;
            localHit = world2local.MultiplyPoint3x4(ray.origin + ray.direction * t);
            return true;
        }

        // The actual click logic — a plain function of (where, whether shift was held), so it's testable
        // without needing a real Event.current. Mutates skel/selection and schedules undo/dirty/rebuild.
        void ProcessSkeletonClick(Rect rect, LatheSolid solid, SkeletonSweepModule skel, Vector2 mousePos, bool shiftHeld)
        {
            var cam = previewRenderer?.Camera;
            if (cam == null) return;
            skel.nodes ??= new List<Vector3>();
            skel.edges ??= new List<Vector2Int>();

            var local2world = solid.LocalToWorld(Quaternion.identity);
            var world2local = local2world.inverse;

            int hitNode = -1;
            float bestSqr = 14f * 14f;
            for (int i = 0; i < skel.nodes.Count; i++)
            {
                float d = (SkeletonGuiOf(rect, local2world, cam, skel.nodes[i]) - mousePos).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; hitNode = i; }
            }

            if (hitNode >= 0)
            {
                if (shiftHeld && skeletonSelectedNode >= 0 && skeletonSelectedNode != hitNode)
                {
                    int a = skeletonSelectedNode, b = hitNode;
                    Dirty(() =>
                    {
                        bool exists = skel.edges.Exists(ed => (ed.x == a && ed.y == b) || (ed.x == b && ed.y == a));
                        if (!exists) skel.edges.Add(new Vector2Int(a, b));
                    });
                    skeletonSelectedNode = hitNode;
                    DeferredRebuild();
                }
                else
                {
                    skeletonSelectedNode = hitNode;
                    skeletonDraggingNode = hitNode;
                    if (spec != null) Undo.RecordObject(spec, "Move Lathe Skeleton Node");
                    preview?.MarkDirtyRepaint();
                }
            }
            else if (SkeletonTryHit(rect, local2world, world2local, cam, mousePos, skeletonPlacementY, out var localHit))
            {
                Dirty(() => skel.nodes.Add(localHit));
                skeletonSelectedNode = skel.nodes.Count - 1;
                DeferredRebuild();
            }
        }

        void HandleSkeletonEditorInput(Rect rect, LatheSolid solid, SkeletonSweepModule skel)
        {
            var cam = previewRenderer?.Camera;
            if (cam == null) return;
            skel.nodes ??= new List<Vector3>();
            skel.edges ??= new List<Vector2Int>();
            var local2world = solid.LocalToWorld(Quaternion.identity);

            // Draw the graph as a GUI-space overlay on top of the rendered frame — Handles.BeginGUI switches
            // Handles' coordinate system to plain GUI pixels for exactly this (2D-over-a-custom-camera-preview).
            Handles.BeginGUI();
            try
            {
                Handles.color = new Color(0.3f, 0.9f, 1f, 0.85f);
                foreach (var edge in skel.edges)
                {
                    if (edge.x < 0 || edge.x >= skel.nodes.Count || edge.y < 0 || edge.y >= skel.nodes.Count) continue;
                    Handles.DrawLine(SkeletonGuiOf(rect, local2world, cam, skel.nodes[edge.x]),
                        SkeletonGuiOf(rect, local2world, cam, skel.nodes[edge.y]));
                }
                for (int i = 0; i < skel.nodes.Count; i++)
                {
                    Handles.color = i == skeletonSelectedNode ? Color.yellow : Color.white;
                    Handles.DrawSolidDisc(SkeletonGuiOf(rect, local2world, cam, skel.nodes[i]), Vector3.forward, 6f);
                }
            }
            finally { Handles.EndGUI(); }

            var e = Event.current;
            if (e == null) return;
            int id = GUIUtility.GetControlID(FocusType.Passive, rect);
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                ProcessSkeletonClick(rect, solid, skel, e.mousePosition, e.shift);
                if (skeletonDraggingNode >= 0) GUIUtility.hotControl = id;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id && skeletonDraggingNode >= 0)
            {
                var world2local = local2world.inverse;
                if (SkeletonTryHit(rect, local2world, world2local, cam, e.mousePosition, skeletonPlacementY, out var localHit))
                {
                    skel.nodes[skeletonDraggingNode] = localHit;
                    if (spec != null) EditorUtility.SetDirty(spec);
                    preview?.MarkDirtyRepaint();
                }
                e.Use();
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == id)
            {
                skeletonDraggingNode = -1;
                GUIUtility.hotControl = 0;
                e.Use();
            }
            else if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
                     && skeletonSelectedNode >= 0)
            {
                DeleteSelectedSkeletonNode(skel);
                e.Use();
            }
        }
    }
}
