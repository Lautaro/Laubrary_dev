// The orientation gizmo: three arrows (Front, Up, East) drawn beside the shape the way a 3D editor draws its move handle, so it is
// always clear which way a member faces even when its box or sphere looks the same from both sides. The arrows are the member's own
// axes seen from the viewer: an arrow pointing toward the viewer has a filled head, one pointing away has a hollow, faint head.
// Dragging an arrow head turns the member (the shape follows at once); a tap on a head turns that axis to face the other way.
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.GoreLab.Editor
{
    internal sealed partial class GoreStage
    {
        const float GizmoLen = 46f, GizmoHeadSize = 9f, GizmoHitRadius = 14f, GizmoGap = 22f;
        // axis order: 0 = Up, 1 = Front, 2 = East
        static readonly string[] GizmoNames = { "U", "F", "E" };
        static readonly int[] GizmoMark = { 4, 3, 2 };            // colours shared with the marks on the shape

        readonly Label[] gizmoLetters = new Label[3];
        int gizmoAxis = -1;
        Vector2 gizmoDragOrigin;

        void BuildGizmoLetters()
        {
            for (int i = 0; i < gizmoLetters.Length; i++)
            {
                var l = Child(new Label(GizmoNames[i]));
                l.pickingMode = PickingMode.Ignore;
                l.style.width = 14f;
                l.style.height = 14f;
                l.style.fontSize = 11f;
                l.style.unityFontStyleAndWeight = FontStyle.Bold;
                l.style.unityTextAlign = TextAnchor.MiddleCenter;
                l.style.paddingLeft = l.style.paddingRight = l.style.paddingTop = l.style.paddingBottom = 0f;
                gizmoLetters[i] = l;
            }
        }

        bool GizmoOn(out MemberTag t)
        {
            t = default;
            return w.tab != GoreLabWindow.Tab.Paint && ShowsMember(out t);
        }

        /// Beside the shape on the roomier side, kept inside the stage.
        Vector2 GizmoOrigin(in MemberTag t)
        {
            Vector2 c = SpriteToLocal(t.cx, t.cy);
            float reach = (float)System.Math.Max(t.rx, t.ry) * Scale + GizmoLen + GizmoGap;
            float width = Mathf.Max(contentRect.width, 2f * (GizmoLen + 16f)), height = Mathf.Max(contentRect.height, 2f * (GizmoLen + 16f));
            float x = c.x + reach;
            if (x + GizmoLen + 10f > width) x = c.x - reach;
            x = Mathf.Clamp(x, GizmoLen + 10f, width - GizmoLen - 10f);
            float y = Mathf.Clamp(c.y, GizmoLen + 10f, height - GizmoLen - 10f);
            return new Vector2(x, y);
        }

        static Vector3 GizmoAxis(in MemberTag t, int axis)
            => axis == 0 ? GoreTagEdit.Up(t) : axis == 1 ? GoreTagEdit.Forward(t) : GoreTagEdit.East(t);

        static Vector2 GizmoTip(Vector2 origin, Vector3 v) => origin + new Vector2(v.x, v.y) * GizmoLen;

        void PaintGizmo(Painter2D p, in MemberTag t, float alpha)
        {
            Vector2 o = GizmoOrigin(t);
            // the boundary the heads travel on, so the length of an arrow reads as "pointing at / away from you"
            p.strokeColor = new Color(1f, 1f, 1f, 0.12f * alpha);
            p.lineWidth = 1f;
            Ring(p, o, GizmoLen, 40);

            // far arrows first, near ones over them
            int[] order = { 0, 1, 2 };
            MemberTag tag = t;
            System.Array.Sort(order, (a, b) => GizmoAxis(tag, a).z.CompareTo(GizmoAxis(tag, b).z));
            foreach (int ax in order)
            {
                Vector3 v = GizmoAxis(t, ax);
                bool near = v.z >= 0f;
                Color col = MarkColours[GizmoMark[ax]];
                col.a = (near ? 1f : 0.45f) * alpha;
                Vector2 tip = GizmoTip(o, v);
                p.strokeColor = col;
                p.lineWidth = near ? 3f : 1.5f;
                p.BeginPath();
                p.MoveTo(o);
                p.LineTo(tip);
                p.Stroke();

                // arrow head: a triangle along the arrow's on-screen direction (up when it points straight at or away from you)
                Vector2 d = tip - o;
                d = d.sqrMagnitude > 1f ? d.normalized : Vector2.up * -1f;
                Vector2 side = new Vector2(-d.y, d.x);
                Vector2 b0 = tip - d * GizmoHeadSize + side * (GizmoHeadSize * 0.55f), b1 = tip - d * GizmoHeadSize - side * (GizmoHeadSize * 0.55f);
                Vector2 apex = tip + d * (GizmoHeadSize * 0.6f);
                p.BeginPath();
                p.MoveTo(apex); p.LineTo(b0); p.LineTo(b1); p.LineTo(apex);
                if (near)
                {
                    p.fillColor = col;
                    p.Fill();
                }
                else
                {
                    p.strokeColor = col;
                    p.lineWidth = 1.5f;
                    p.Stroke();
                }
                if (ax == gizmoAxis) { p.strokeColor = new Color(1f, 1f, 1f, alpha); p.lineWidth = 2f; Ring(p, tip, GizmoHitRadius * 0.7f, 20); }
            }
            Disc(p, o, 3f, new Color(1f, 1f, 1f, alpha));
        }

        void PlaceGizmoLetters()
        {
            bool on = GizmoOn(out var t);
            Vector2 o = on ? GizmoOrigin(t) : Vector2.zero;
            for (int i = 0; i < gizmoLetters.Length; i++)
            {
                var l = gizmoLetters[i];
                l.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (!on) continue;
                Vector3 v = GizmoAxis(t, i);
                Vector2 d = new Vector2(v.x, v.y);
                d = d.sqrMagnitude > 1e-3f ? d.normalized : Vector2.up * -1f;
                Vector2 at = GizmoTip(o, v) + d * (GizmoHeadSize + 6f);
                l.style.left = at.x - 7f;
                l.style.top = at.y - 7f;
                var c = MarkColours[GizmoMark[i]];
                c.a = v.z >= 0f ? 1f : 0.5f;
                l.style.color = c;
            }
        }

        /// Which arrow head is under the pointer (the nearer one if two overlap), or -1.
        int GizmoPick(in MemberTag t, Vector2 local)
        {
            Vector2 o = GizmoOrigin(t);
            int best = -1;
            float bestScore = float.MaxValue;
            for (int ax = 0; ax < 3; ax++)
            {
                Vector3 v = GizmoAxis(t, ax);
                float dist = Vector2.Distance(local, GizmoTip(o, v));
                if (dist > GizmoHitRadius) continue;
                float score = dist - v.z * 4f;                 // an arrow toward the viewer wins a tie
                if (score < bestScore) { bestScore = score; best = ax; }
            }
            return best;
        }

        bool TryBeginGizmo(Vector2 local)
        {
            if (!GizmoOn(out var t)) return false;
            int ax = GizmoPick(t, local);
            if (ax < 0) return false;
            if (!EditableOrSay()) { dragging = DragKind.Ignore; return true; }
            gizmoAxis = ax;
            gizmoDragOrigin = GizmoOrigin(t);
            rimOut = false;
            zSign = GizmoAxis(t, ax).z < 0f ? -1 : 1;
            dragging = ax == 0 ? DragKind.GizmoUp : ax == 1 ? DragKind.GizmoForward : DragKind.GizmoEast;
            Open();
            return true;
        }

        /// Turns the member so the dragged axis points where the pointer is, on the sphere the heads ride (over the rim and back continues on the far half).
        void DragGizmo(Vector2 local)
        {
            var mf = w.ActiveMemberFrame(false);
            if (mf == null) return;
            w.Record("Turn member");
            var t = mf.tag;
            double x = (local.x - gizmoDragOrigin.x) / GizmoLen, y = (local.y - gizmoDragOrigin.y) / GizmoLen, l = System.Math.Sqrt(x * x + y * y);
            if (l > 1) { x /= l; y /= l; }
            if (l > 1.02) rimOut = true;
            else if (rimOut && l < 0.98) { zSign = -zSign; rimOut = false; }
            var v = new Vector3((float)x, (float)y, (float)(System.Math.Sqrt(System.Math.Max(0, 1 - x * x - y * y)) * zSign));

            if (dragging == DragKind.GizmoUp) GoreTagEdit.ApplyDot(ref t, true, v);
            else if (dragging == DragKind.GizmoForward) GoreTagEdit.ApplyDot(ref t, false, v);
            else
            {
                // East: turn the front about up until east points at the pointer. East = up x front, so front = east x up.
                double d = v.x * t.ux + v.y * t.uy + v.z * t.uz;
                double ex = v.x - t.ux * d, ey = v.y - t.uy * d, ez = v.z - t.uz * d, el = System.Math.Sqrt(ex * ex + ey * ey + ez * ez);
                if (el > 1e-3)
                {
                    ex /= el; ey /= el; ez /= el;
                    t.fx = ey * t.uz - ez * t.uy;
                    t.fy = ez * t.ux - ex * t.uz;
                    t.fz = ex * t.uy - ey * t.ux;
                    GoreTagEdit.FixForward(ref t);
                }
            }
            mf.tag = t;
        }

        /// A tap on a head turns that axis to the other side: up and front flip front-to-back, east turns the member half way round.
        void TapGizmo(DragKind kind)
        {
            var mf = w.ActiveMemberFrame(false);
            if (mf == null) return;
            w.Record("Flip axis");
            var t = mf.tag;
            if (kind == DragKind.GizmoUp) GoreTagEdit.FlipDot(ref t, true);
            else if (kind == DragKind.GizmoForward) GoreTagEdit.FlipDot(ref t, false);
            else GoreTagEdit.Yaw(ref t, 180);
            mf.tag = t;
        }
    }
}
