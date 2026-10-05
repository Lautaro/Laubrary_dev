// What a drag on the stage does, decided by the tab (the tab IS the mode):
//   Shape / Frame — no shape yet: drag a box corner to corner (a tap makes nothing). A shape exists: drag inside to move
//                   it, near its edge to resize it anchored (the far side stays put). A drag elsewhere never replaces it.
//   Rotate        — drag the U or F dot (trackball: over the rim and back continues on the far half); tap flips a dot.
//   Paint         — brush the chosen mask; right-drag always erases.
//   Test          — press, drag, release = a swipe / aim line, wounding the frame; the preview shows what will leave.
// Middle-drag belongs to ZuiPanZoom in every tab. Each drag is one undo step (GoreLabWindow.BeginGesture/EndGesture).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.GoreLab.Editor
{
    internal sealed partial class GoreStage
    {
        enum DragKind { None, Ignore, New, Move, Edge, Up, Forward, Paint, Wound, GizmoUp, GizmoForward, GizmoEast }

        DragKind dragging;
        bool gestureOpen;
        Vector2 pressSprite, lastSprite, pressLocal;
        bool pressMoved;      // the pointer travelled more than a tap's few screen pixels since it went down
        Vector2 grabOffset, edge0;
        MemberTag startTag;
        double zSign = 1;
        bool rimOut, created, eraseStroke;
        HashSet<int> strokeMine, strokeOther;
        bool hasHover;
        Vector2 hoverLocal;
        bool toldShapeStays;

        void RegisterGestures()
        {
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => Cancel());
            RegisterCallback<PointerLeaveEvent>(_ => { hasHover = false; overlay.MarkDirtyRepaint(); });
        }

        void Open()
        {
            w.BeginGesture();
            gestureOpen = true;
        }

        void OnDown(PointerDownEvent e)
        {
            if (e.button == 2 || w.shown == null || w.Rig == null) return;   // middle belongs to ZuiPanZoom
            var p = LocalToSprite(e.localPosition);
            pressSprite = lastSprite = p;
            pressLocal = (Vector2)e.localPosition;
            pressMoved = false;
            dragging = DragKind.None;

            if (e.button == 0 && TryBeginGizmo(e.localPosition)) { }
            else switch (w.tab)
            {
                case GoreLabWindow.Tab.Test:
                    if (e.button != 0) return;
                    dragging = DragKind.Wound;
                    w.pendingWound.Clear();
                    break;
                case GoreLabWindow.Tab.Paint:
                    BeginPaint(p, e.button == 1);
                    break;
                case GoreLabWindow.Tab.Rotate:
                    if (e.button == 0) BeginRotate(p);
                    break;
                default:
                    if (e.button == 0) BeginShape(p);
                    break;
            }
            if (dragging == DragKind.None) return;
            this.CapturePointer(e.pointerId);
            Refresh();
            e.StopPropagation();
        }

        bool EditableOrSay()
        {
            if (w.CanEditShown) return true;
            Flash($"Mirror of {w.shown.group.source?.label}: tag the drawn direction");
            return false;
        }

        void BeginShape(Vector2 p)
        {
            if (!EditableOrSay() || w.ActiveMember == null) return;
            if (!w.TryActiveTag(out var t)) { dragging = DragKind.New; created = false; return; }

            float hitR = Mathf.Max(4f, 26f / Mathf.Max(0.0001f, Scale));
            // The resize band never eats the whole inside of a small shape, so there is always a core to grab and move.
            float band = Mathf.Min(hitR, 0.35f * (float)System.Math.Min(t.rx, t.ry));
            if (GoreTagEdit.EdgeDistance(t, p) < band)
            {
                dragging = DragKind.Edge;
                startTag = t;
                var q = GoreTagEdit.ToShape(t, p);
                edge0 = new Vector2(q.x / (float)t.rx, q.y / (float)t.ry);
            }
            else if (GoreTagEdit.Inside(t, p.x, p.y))
            {
                dragging = DragKind.Move;
                grabOffset = new Vector2((float)t.cx - p.x, (float)t.cy - p.y);
            }
            else
            {
                dragging = DragKind.Ignore;
                if (!toldShapeStays)
                {
                    toldShapeStays = true;
                    Flash("The shape stays put: drag inside to move it, near its edge to resize it");
                }
                return;
            }
            Open();
        }

        void BeginRotate(Vector2 p)
        {
            if (!EditableOrSay()) return;
            if (!w.TryActiveTag(out var t)) { Flash("Nothing to rotate yet: draw the shape on the Shape tab"); return; }
            float hitDot = Mathf.Max(3f, 30f / Mathf.Max(0.0001f, Scale));
            float du = Vector2.Distance(p, GoreTagEdit.DotPosition(t, true));
            float df = Vector2.Distance(p, GoreTagEdit.DotPosition(t, false));
            if (du >= hitDot && df >= hitDot) { dragging = DragKind.Ignore; Flash("Drag the U or the F dot to turn the member"); return; }
            bool up = du <= df;
            dragging = up ? DragKind.Up : DragKind.Forward;
            rimOut = false;
            double z = up ? t.uz : t.fz;
            zSign = z < 0 ? -1 : 1;
            Open();
        }

        void BeginPaint(Vector2 p, bool rightButton)
        {
            if (!EditableOrSay()) return;
            var mf = w.ActiveMemberFrame(false);
            if (mf == null || !mf.present) { Flash("Draw the shape first: only pixels inside it can be painted"); return; }
            eraseStroke = rightButton || w.paintErase;
            bool front = w.paintLayer == 1;
            strokeMine = new HashSet<int>((front ? mf.exempt : mf.behind) ?? new int[0]);
            strokeOther = new HashSet<int>((front ? mf.behind : mf.exempt) ?? new int[0]);
            dragging = DragKind.Paint;
            Open();
            w.Record("Paint mask");
            Dab(p, mf.tag);
            w.WriteMasks(mf, strokeMine, strokeOther);
        }

        void OnMove(PointerMoveEvent e)
        {
            hasHover = true;
            hoverLocal = e.localPosition;
            if (dragging == DragKind.None || dragging == DragKind.Ignore)
            {
                if (w.tab == GoreLabWindow.Tab.Paint) overlay.MarkDirtyRepaint();
                return;
            }
            if (((Vector2)e.localPosition - pressLocal).magnitude > 4f) pressMoved = true;
            var p = LocalToSprite(e.localPosition);
            switch (dragging)
            {
                case DragKind.New:
                {
                    if (!created && (p - pressSprite).magnitude < 2f) break;   // a tap is not a box
                    if (!created)
                    {
                        Open();
                        w.Record("Draw shape");
                        var nf = w.ActiveMemberFrame(true);
                        nf.present = true;
                        nf.skip = false;
                        nf.tag = GoreTagEdit.NewTag(w.ActiveMember.kind, pressSprite.x, pressSprite.y, w.DefaultForward());
                        created = true;
                    }
                    w.Record("Draw shape");
                    var mf = w.ActiveMemberFrame(false);
                    var t = mf.tag;
                    GoreTagEdit.Span(ref t, pressSprite, p);
                    mf.tag = t;
                    break;
                }
                case DragKind.Move:
                {
                    var mf = w.ActiveMemberFrame(false);
                    if (mf == null) break;
                    w.Record("Move shape");
                    var t = mf.tag;
                    t.cx = p.x + grabOffset.x;
                    t.cy = p.y + grabOffset.y;
                    mf.tag = t;
                    break;
                }
                case DragKind.Edge:
                {
                    var mf = w.ActiveMemberFrame(false);
                    if (mf == null) break;
                    w.Record("Resize shape");
                    mf.tag = GoreTagEdit.ResizeFromEdge(startTag, edge0, p);
                    break;
                }
                case DragKind.Up:
                case DragKind.Forward:
                {
                    var mf = w.ActiveMemberFrame(false);
                    if (mf == null) break;
                    w.Record("Turn member");
                    var t = mf.tag;
                    bool up = dragging == DragKind.Up;
                    var v = GoreTagEdit.Trackball(t, up, p, ref zSign, ref rimOut);
                    GoreTagEdit.ApplyDot(ref t, up, v);
                    mf.tag = t;
                    break;
                }
                case DragKind.GizmoUp:
                case DragKind.GizmoForward:
                case DragKind.GizmoEast:
                    DragGizmo(e.localPosition);
                    break;
                case DragKind.Paint:
                {
                    var mf = w.ActiveMemberFrame(false);
                    if (mf == null) break;
                    w.Record("Paint mask");
                    int n = Mathf.Max(1, Mathf.CeilToInt((p - lastSprite).magnitude * 2f));
                    for (int i = 1; i <= n; i++) Dab(Vector2.Lerp(lastSprite, p, i / (float)n), mf.tag);
                    w.WriteMasks(mf, strokeMine, strokeOther);
                    break;
                }
                case DragKind.Wound:
                    if ((p - pressSprite).magnitude >= 2f) w.PreviewWound(pressSprite, p);
                    else w.previewMask = null;
                    break;
            }
            lastSprite = p;
            Refresh();
        }

        void OnUp(PointerUpEvent e)
        {
            if (dragging == DragKind.None) return;
            var p = LocalToSprite(e.localPosition);
            var kind = dragging;

            if ((kind == DragKind.Up || kind == DragKind.Forward) && !pressMoved)
            {
                // A tap on a dot flips it to the other half of the sphere.
                var mf = w.ActiveMemberFrame(false);
                if (mf != null)
                {
                    w.Record("Flip dot");
                    var t = mf.tag;
                    GoreTagEdit.FlipDot(ref t, kind == DragKind.Up);
                    mf.tag = t;
                }
            }
            if ((kind == DragKind.GizmoUp || kind == DragKind.GizmoForward || kind == DragKind.GizmoEast) && !pressMoved) TapGizmo(kind);
            if (kind == DragKind.Wound)
            {
                if ((p - pressSprite).magnitude >= 2f) w.FireWound(pressSprite, p);
                w.previewMask = null;
            }

            Finish();
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
            w.AfterEdit();
            e.StopPropagation();
        }

        void Cancel()
        {
            if (dragging == DragKind.None) return;
            w.previewMask = null;
            Finish();
            w.AfterEdit();
        }

        void Finish()
        {
            if (gestureOpen) w.EndGesture();
            gestureOpen = false;
            dragging = DragKind.None;
            gizmoAxis = -1;
            created = false;
            strokeMine = strokeOther = null;
        }

        /// One round brush dab: solid sprite pixels inside the outline only; a pixel is in at most one mask.
        void Dab(Vector2 c, MemberTag tag)
        {
            var g = w.shown.pixels.grid;
            int size = Mathf.Clamp(w.brushSize, 1, 6);
            float r = size - 0.25f;
            int bx = Mathf.FloorToInt(c.x), by = Mathf.FloorToInt(c.y);
            for (int dy = -size; dy <= size; dy++)
                for (int dx = -size; dx <= size; dx++)
                {
                    int x = bx + dx, y = by + dy;
                    if (new Vector2(x + 0.5f - c.x, y + 0.5f - c.y).magnitude > r) continue;
                    if (!g.Solid(x, y) || !GoreTagEdit.Inside(tag, x + 0.5, y + 0.5)) continue;
                    int k = y * g.w + x;
                    if (eraseStroke) strokeMine.Remove(k);
                    else { strokeMine.Add(k); strokeOther.Remove(k); }
                }
        }
    }
}
