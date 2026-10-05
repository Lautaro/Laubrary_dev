// The Painter2D overlay: member outlines, the ball as a sphere with three great circles, the box as a wireframe whose
// faces toward the viewer are opaque and whose far faces are faint (the forward face tinted), the U F E W S marks, the
// brush cursor, and in the Test tab the swipe line and the frame status border. Ported from the web prototype's
// drawSphere / drawBox / outline.
//
// Paths are plain MoveTo/LineTo, every closed outline closed by hand: Painter2D's join tessellation has crashed on
// closed subpaths drawn in bulk in this codebase before (see MetaStage).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.GoreLab.Editor
{
    internal sealed partial class GoreStage
    {
        struct Mark { public Vector2 at; public float z; public Color colour; public bool big; }

        static readonly Color[] MarkColours =
        {
            new Color(0.60f, 0.67f, 0.60f),   // S
            new Color(0.80f, 0.60f, 0.60f),   // W
            new Color(1.00f, 0.47f, 0.47f),   // E
            new Color(0.47f, 0.93f, 0.47f),   // F
            new Color(0.27f, 0.87f, 0.93f),   // U
        };

        readonly Mark[] marks = new Mark[5];
        readonly List<Vector2> ring = new List<Vector2>();
        const float BigDot = 7f, SmallDot = 4f;

        /// The five marks of a member: on the sphere for a ball, on the face centres for a box.
        void ComputeMarks(MemberTag t)
        {
            bool box = t.kind == MemberKind.Box;
            double r = (t.rx + t.ry) * 0.5;
            float rU = (float)(box ? t.ry : r), rF = (float)(box ? GoreTagEdit.Depth(t) : r), rE = (float)(box ? t.rx : r);
            Vector3 U = GoreTagEdit.Up(t), F = GoreTagEdit.Forward(t), E = GoreTagEdit.East(t);
            Set(0, -F * rF, false);
            Set(1, -E * rE, false);
            Set(2, E * rE, false);
            Set(3, F * rF, true);
            Set(4, U * rU, true);

            void Set(int i, Vector3 v, bool big)
            {
                marks[i] = new Mark { at = SpriteToLocal(t.cx + v.x, t.cy + v.y), z = v.z, colour = MarkColours[i], big = big };
            }
        }

        bool ShowsMember(out MemberTag tag)
        {
            tag = default;
            return w.shown != null && w.tab != GoreLabWindow.Tab.Test && w.TryShownMember(w.memberIndex, out tag, out _, out _, out _);
        }

        void PlaceLetters()
        {
            bool on = ShowsMember(out var t);
            if (on) ComputeMarks(t);
            for (int i = 0; i < letters.Length; i++)
            {
                var l = letters[i];
                bool vis = on && !(w.hideFar && marks[i].z < 0f);
                l.style.display = vis ? DisplayStyle.Flex : DisplayStyle.None;
                if (!vis) continue;
                float r = marks[i].big ? BigDot : SmallDot;
                l.style.left = marks[i].at.x - 7f;
                l.style.top = marks[i].at.y - r - 16f;
                var c = marks[i].colour;
                c.a = w.shapeAlpha;
                l.style.color = c;
            }
        }

        void PaintOverlay(MeshGenerationContext ctx)
        {
            var s = w.shown;
            if (s == null || w.canvas.x <= 0) return;
            var p = ctx.painter2D;

            if (w.tab == GoreLabWindow.Tab.Test) { PaintTest(p); return; }

            for (int i = 0; i < w.MemberCount; i++)
            {
                if (i == w.memberIndex || !w.TryShownMember(i, out var other, out _, out _, out _)) continue;
                Outline(p, other, 0.3f * w.outlineAlpha);                        // the other members, faint, for context
            }
            if (!ShowsMember(out var t)) return;
            float a = w.shapeAlpha;
            Outline(p, t, 0.9f * w.outlineAlpha);
            if (t.kind == MemberKind.Box) PaintBox(p, t, a); else PaintSphere(p, t, a);
            PaintMarks(p, t, a);
            PaintGizmo(p, t, Mathf.Max(0.6f, a));
            if (!s.mirrored) Disc(p, SpriteToLocal(t.cx, t.cy), 4f, new Color(1f, 1f, 1f, a));
            if (w.tab == GoreLabWindow.Tab.Paint && hasHover && !s.mirrored)
            {
                p.strokeColor = new Color(1f, 1f, 1f, 0.8f);
                p.lineWidth = 1f;
                Ring(p, hoverLocal, Mathf.Max(2f, (w.brushSize - 0.25f) * Scale), 24);
            }
        }

        /// The marked 2D outline, dashed.
        void Outline(Painter2D p, in MemberTag t, float alpha)
        {
            GoreTagEdit.Outline(t, ring, 96);
            p.lineWidth = 2f;
            p.strokeColor = new Color(1f, 1f, 1f, alpha);
            p.BeginPath();
            for (int i = 0; i < ring.Count; i++)
            {
                if ((i / 3) % 2 == 1) continue;
                var a = ring[i]; var b = ring[(i + 1) % ring.Count];
                p.MoveTo(SpriteToLocal(a.x, a.y));
                p.LineTo(SpriteToLocal(b.x, b.y));
            }
            p.Stroke();
        }

        void PaintSphere(Painter2D p, in MemberTag t, float alpha)
        {
            float R = (float)((t.rx + t.ry) * 0.5);
            var c = SpriteToLocal(t.cx, t.cy);
            Disc(p, c, R * Scale, new Color(1f, 1f, 1f, 0.07f * alpha), 40);
            Vector3 u = GoreTagEdit.Up(t), f = GoreTagEdit.Forward(t), e = GoreTagEdit.East(t);
            GreatCircle(p, t, f, e, R, new Color(0.43f, 0.9f, 0.43f), alpha);   // equator (perpendicular to up)
            GreatCircle(p, t, u, f, R, new Color(0.31f, 0.86f, 0.94f), alpha);  // up-forward meridian
            GreatCircle(p, t, u, e, R, new Color(0.98f, 0.47f, 0.47f), alpha);  // up-east meridian
        }

        void GreatCircle(Painter2D p, in MemberTag t, Vector3 e1, Vector3 e2, float R, Color colour, float alpha)
        {
            const int N = 64;
            for (int pass = 0; pass < 2; pass++)
            {
                bool front = pass == 1;                          // far half first, near half drawn over it
                if (!front && w.hideFar) continue;
                var col = colour; col.a = (front ? 1f : 0.16f) * alpha;
                p.strokeColor = col;
                p.lineWidth = front ? 2.5f : 1f;
                p.BeginPath();
                for (int i = 0; i < N; i++)
                {
                    float a0 = i / (float)N * Mathf.PI * 2f, a1 = (i + 1) / (float)N * Mathf.PI * 2f;
                    Vector3 A = e1 * Mathf.Cos(a0) + e2 * Mathf.Sin(a0), B = e1 * Mathf.Cos(a1) + e2 * Mathf.Sin(a1);
                    if ((A.z + B.z >= 0f) != front) continue;
                    p.MoveTo(SpriteToLocal(t.cx + A.x * R, t.cy + A.y * R));
                    p.LineTo(SpriteToLocal(t.cx + B.x * R, t.cy + B.y * R));
                }
                p.Stroke();
            }
        }

        void PaintBox(Painter2D p, in MemberTag t, float alpha)
        {
            Vector3 E = GoreTagEdit.East(t), U = GoreTagEdit.Up(t), F = GoreTagEdit.Forward(t);
            float rx = (float)t.rx, ry = (float)t.ry, rz = (float)GoreTagEdit.Depth(t);
            double cx = t.cx, cy = t.cy;
            Vector3 Corner(float a, float b, float c) => a * rx * E + b * ry * U + c * rz * F;
            Vector2 L(Vector3 v) => SpriteToLocal(cx + v.x, cy + v.y);

            // The face of each axis that points at the viewer is drawn opaque; the forward face is tinted and heavier.
            int ve = E.z > 0 ? 1 : -1, vu = U.z > 0 ? 1 : -1, vf = F.z > 0 ? 1 : -1;
            if (vf == 1)
            {
                p.fillColor = new Color(0.47f, 1f, 0.55f, 0.2f * alpha);
                p.BeginPath();
                p.MoveTo(L(Corner(-1, -1, 1))); p.LineTo(L(Corner(1, -1, 1)));
                p.LineTo(L(Corner(1, 1, 1))); p.LineTo(L(Corner(-1, 1, 1)));
                p.ClosePath();
                p.Fill();
            }

            for (int pass = 0; pass < 2; pass++)
            {
                bool near = pass == 1;
                if (!near && w.hideFar) continue;
                for (int i = -1; i <= 1; i += 2)
                    for (int j = -1; j <= 1; j += 2)
                    {
                        // along E: between the U face (i) and the F face (j); along U: E face and F face; along F: E and U.
                        Edge(Corner(-1, i, j), Corner(1, i, j), i == vu || j == vf, vf == 1 && j == 1);
                        Edge(Corner(i, -1, j), Corner(i, 1, j), i == ve || j == vf, vf == 1 && j == 1);
                        Edge(Corner(i, j, -1), Corner(i, j, 1), i == ve || j == vu, false);
                    }

                void Edge(Vector3 A, Vector3 B, bool isNear, bool wide)
                {
                    if (isNear != near) return;
                    p.strokeColor = near ? (wide ? new Color(0.67f, 1f, 0.7f, alpha) : new Color(0.47f, 1f, 0.55f, alpha))
                                         : new Color(0.47f, 1f, 0.55f, 0.16f * alpha);
                    p.lineWidth = wide ? 4f : near ? 2.5f : 1f;
                    p.BeginPath();
                    p.MoveTo(L(A));
                    p.LineTo(L(B));
                    p.Stroke();
                }
            }
        }

        /// Filled dot = on the near half, ring = on the far half.
        void PaintMarks(Painter2D p, in MemberTag t, float alpha)
        {
            ComputeMarks(t);
            foreach (var m in marks)
            {
                if (m.z < 0f && w.hideFar) continue;
                var c = m.colour; c.a = alpha;
                float r = m.big ? BigDot : SmallDot;
                if (m.z >= 0f) Disc(p, m.at, r, c);
                else { p.strokeColor = c; p.lineWidth = 2f; Ring(p, m.at, r); }
            }
        }

        void PaintTest(Painter2D p)
        {
            for (int i = 0; i < w.MemberCount; i++)
                if (w.TryShownMember(i, out var t, out _, out _, out bool skip) && !skip) Outline(p, t, 0.3f * w.outlineAlpha);

            if (dragging == DragKind.Wound)
            {
                var a = SpriteToLocal(pressSprite.x, pressSprite.y);
                var b = SpriteToLocal(lastSprite.x, lastSprite.y);
                p.strokeColor = Color.white;
                p.lineWidth = 2f;
                p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
                Disc(p, a, 4f, Color.white);
            }

            Color border;
            if (w.testState == GoreLabWindow.TestState.NotSetUp) border = new Color(0.9f, 0.2f, 0.18f);
            else if (w.testState == GoreLabWindow.TestState.Hidden) border = new Color(0.9f, 0.64f, 0.1f);
            else return;
            var lo = view.Origin + new Vector2(1.5f, 1.5f);
            var hi = view.Origin + new Vector2(w.canvas.x, w.canvas.y) * Scale - new Vector2(1.5f, 1.5f);
            p.strokeColor = border;
            p.lineWidth = 3f;
            p.BeginPath();
            p.MoveTo(lo); p.LineTo(new Vector2(hi.x, lo.y)); p.LineTo(hi); p.LineTo(new Vector2(lo.x, hi.y)); p.LineTo(lo);
            p.Stroke();
        }

        static void Disc(Painter2D p, Vector2 c, float radius, Color colour, int segments = 14)
        {
            p.fillColor = colour;
            p.BeginPath();
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var v = new Vector2(c.x + Mathf.Cos(a) * radius, c.y + Mathf.Sin(a) * radius);
                if (i == 0) p.MoveTo(v); else p.LineTo(v);
            }
            p.ClosePath();
            p.Fill();
        }

        static void Ring(Painter2D p, Vector2 c, float radius, int segments = 14)
        {
            p.BeginPath();
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var v = new Vector2(c.x + Mathf.Cos(a) * radius, c.y + Mathf.Sin(a) * radius);
                if (i == 0) p.MoveTo(v); else p.LineTo(v);
            }
            p.Stroke();
        }
    }
}
