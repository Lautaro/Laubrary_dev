// DotGenWindow.Gizmos — the process overlays drawn over the frame preview (W2.3, POC §13).
//
// Every gizmo here is read straight off the last evaluation (`Result`, from DotGenWindow.Preview.cs) — never
// re-run the pipeline. The one exception that is NOT a re-evaluation: DotWarpMutator.Centers and
// DotDrawer.Targets are pure functions of data already sitting in the result (an area, a seed, a generator's
// finished dots), the same way a selector's Weight(...) is — calling them here costs nothing the evaluator
// didn't already pay for and re-derives nothing the evaluator decided.
//
// Coordinate space: everything is built in frame-normalized space (DotGenMath.Xform, DotArea) and converted
// to screen points with the window's own FrameToScreen — the one mapping guaranteed to agree with the blitted
// texture (see DotGenWindow.Preview.cs's header). Handles draws in GUI space between BeginGUI()/EndGUI(),
// exactly like PyreWindow.Preview's swarm overlay.
//
// Dash rendering: Handles has no native dashed stroke, so DrawDashedPolyline marches along a point chain in
// dash/gap steps, carrying the phase across each edge — the same visual result as the reference's one
// ctx.stroke() call over a whole Path2D (rect/ellipse/diamond as one path), because a canvas dash phase also
// runs continuously around one stroked path rather than restarting per edge. Dash and gap lengths are used as
// constant SCREEN points (the reference's raw pixel literals), not scaled by frameScale/zoom, matching how
// Pyre's own preview overlays already draw fixed-width lines regardless of zoom.
//
// Decisions made in this task (recorded here, not re-asked):
//  1. "All" mode is scoped to the SELECTED generator's own modules only, per this task's brief and
//     DOTGEN-DESIGN-DECISIONS.md §6 — not every generator in the document, which is what the reference's
//     activeIds() literally does (`allGens().flatMap(...)`) for anything other than the Generator Area gizmo.
//     A real document can have dozens of generators; drawing every one's placement/selector/mutator/drawer
//     gizmos at once would be visual noise a single-generator scope avoids. The Generator Area gizmo was
//     already limited to the selected generator by the reference itself (POC §13.1) in every mode.
//  2. Hovered mode is driven from BOTH a card (its own module's gizmo) and a Hierarchy tree ROW (that
//     generator's Area outline, `hoveredGeneratorId` in DotGenWindow.cs). A row wins when both are set,
//     because the pointer can only be over one of them and the row is the more specific request. The row
//     half is deliberately not limited to the selected generator: asking "where does that one sit?" without
//     leaving the one being edited is the only thing hovering a row can mean.
//  3. The Selector gizmo reads each dot's own `DotPoint.area` (populated by the evaluator) to find which
//     area a base dot belongs to, rather than the reference's `Math.floor(i / (base.length/areas.length))`
//     guess — strictly more accurate, and free, since the field is already sitting on every DotPoint.
//  4. No GUI.BeginClip around the gizmo pass. The frame texture blit immediately above it (DrawPreview, W2.1)
//     already draws unclipped to `frameRect`, so a gizmo drawing slightly outside that rect (a child area
//     grown past the frame edge, a warp field line) is no different a risk than what already ships; adding a
//     clip would mean re-deriving every point in clip-local space for a problem nothing has shown yet.
//  5. Radial Grid's "center" cap item (POC §13.2) is the innermost ring ellipse (r≈0 when Inner radius is
//     low) plus the generator's real centre dot when Centre dot is on — drawn by the ordinary dot-marker
//     pass, not a gizmo. The reference's drawPlacement has no separate centre-cross draw for radial; matched
//     as-is rather than inventing one.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.DotGen.Editor
{
    public partial class DotGenWindow
    {
        // ── caps (POC §13.2) ─────────────────────────────────────────────────────────────────
        const int GeneratorAreaCap = 120;
        const int PlacementAreaCap = 60;
        const int SelectorDotCap = 2200;
        const int SelectorAreaCap = 50;
        const int MutatorTraceCap = 50;
        const int MutatorVectorCap = 80;
        const int DrawerTargetCap = 220;

        /// A per-line safety valve on the dash marcher below (DrawDashedPolyline / DrawDashedLine), not a POC
        /// value: it exists only so a degenerate geometry case (extreme zoom, a near-zero dash) can never spin
        /// the editor UI thread instead of just drawing a slightly short dashed line.
        const int DashSafetyLimit = 4000;

        // ── colours (POC §13, hex+alpha literals transcribed to Color once) ────────────────────
        const float Alpha54 = 84f / 255f;
        const float Alpha77 = 119f / 255f;
        const float Alpha88 = 136f / 255f;
        const float AlphaAA = 170f / 255f;
        const float AlphaBB = 187f / 255f;
        const float AlphaCC = 204f / 255f;

        static readonly Color GizmoAnchorFill = DotGenMath.Hex("#0c1017");
        static readonly Color GizmoAmber = new Color(1f, 0.8f, 0.4f, 1f);            // #ffcc66
        static readonly Color GizmoCull = WithAlpha(DotGenMath.Hex("#ff6b7a"), AlphaCC);
        static readonly Color GizmoNudgeVector = WithAlpha(DotGenMath.Hex("#62d8ff"), AlphaAA);
        static readonly Color GizmoWarpVector = WithAlpha(DotGenMath.Hex("#a78bfa"), AlphaAA);
        static readonly Color GizmoWarpCenter = DotGenMath.Hex("#a78bfa");
        static readonly Color GizmoWarpLine = WithAlpha(DotGenMath.Hex("#a78bfa"), Alpha77);
        static readonly Color GizmoDrawer = WithAlpha(DotGenMath.Hex("#d8a7ff"), AlphaCC);

        // ── reusable buffers (allocation-light: cleared and refilled, never re-allocated) ───────
        static readonly List<Vector2> gzShapePts = new List<Vector2>(40);
        static readonly List<Vector2> gzRingPts = new List<Vector2>(40);
        static readonly Vector3[] gzSegBuf = new Vector3[2];
        static readonly Vector3[] gzCircleBuf = new Vector3[25];
        static readonly List<DotMutatorTrace> gzTraceBuf = new List<DotMutatorTrace>(64);
        static readonly List<Vector2> gzCenterBuf = new List<Vector2>(8);
        static readonly List<DotDrawTarget> gzTargetBuf = new List<DotDrawTarget>(64);

        // ── entry point (the W2.1 hook) ─────────────────────────────────────────────────────────

        partial void DrawGizmos(Rect frame, float scale)
        {
            if (doc == null || Result == null) return;
            var mode = doc.gizmoMode;
            if (mode == DotGizmoMode.Off) return;

            var gen = doc.Selected;
            var gd = gen != null ? Result.For(gen) : null;   // a disabled selected generator evaluates to nothing

            // A hovered TREE ROW asks about a generator other than the selected one, so it is resolved on its
            // own rather than through the selection — that is the whole point of hovering it.
            DotGenerator hoverGen = mode == DotGizmoMode.Hovered && !string.IsNullOrEmpty(hoveredGeneratorId)
                ? doc.Find(hoveredGeneratorId) : null;
            var hoverGd = hoverGen != null ? Result.For(hoverGen) : null;

            if (gd == null && hoverGd == null) return;

            Handles.BeginGUI();
            var prevColor = Handles.color;

            switch (mode)
            {
                case DotGizmoMode.Hovered:
                    // A tree row wins over a card: the pointer can only be over one of them, and the row is
                    // the more specific answer to "show me that generator".
                    if (hoverGd != null) DrawGeneratorArea(hoverGen, hoverGd);
                    else if (gd != null) DrawForModule(gen, gd, hoveredModuleId);
                    break;

                case DotGizmoMode.Selected:
                    if (gd == null) break;
                    // Null means "the generator's own area" (DotGenWindow.Cards.cs's own doc comment).
                    if (string.IsNullOrEmpty(selectedModuleId)) DrawGeneratorArea(gen, gd);
                    else DrawForModule(gen, gd, selectedModuleId);
                    break;

                case DotGizmoMode.All:
                    if (gd == null) break;
                    DrawGeneratorArea(gen, gd);
                    var placement = gen.ActivePlacement;
                    if (placement != null) DrawPlacementGizmo(gen, gd, placement);
                    if (gen.selectors != null)
                        for (int i = 0; i < gen.selectors.Count; i++)
                            if (gen.selectors[i] != null) DrawSelectorGizmo(gen, gd, gen.selectors[i]);
                    if (gen.mutators != null)
                        for (int i = 0; i < gen.mutators.Count; i++)
                            if (gen.mutators[i] != null) DrawMutatorGizmo(gen, gd, gen.mutators[i]);
                    if (gen.drawers != null)
                        for (int i = 0; i < gen.drawers.Count; i++)
                            if (gen.drawers[i] != null) DrawDrawerGizmo(gen, gd, gen.drawers[i]);
                    break;
            }

            Handles.color = prevColor;
            Handles.EndGUI();
        }

        /// Resolve one targeted module id (from a hovered or selected card) to its category and draw only that
        /// module's gizmo. Never the Generator Area — that one is reached only via `selectedModuleId == null`
        /// in Selected mode, or explicitly in All mode.
        void DrawForModule(DotGenerator gen, DotGenGeneratorData gd, string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId)) return;

            var placement = gen.ActivePlacement;
            if (placement != null && placement.id == moduleId) { DrawPlacementGizmo(gen, gd, placement); return; }

            if (gen.selectors != null)
                for (int i = 0; i < gen.selectors.Count; i++)
                {
                    var s = gen.selectors[i];
                    if (s != null && s.id == moduleId) { DrawSelectorGizmo(gen, gd, s); return; }
                }

            if (gen.mutators != null)
                for (int i = 0; i < gen.mutators.Count; i++)
                {
                    var m = gen.mutators[i];
                    if (m != null && m.id == moduleId) { DrawMutatorGizmo(gen, gd, m); return; }
                }

            if (gen.drawers != null)
                for (int i = 0; i < gen.drawers.Count; i++)
                {
                    var d = gen.drawers[i];
                    if (d != null && d.id == moduleId) { DrawDrawerGizmo(gen, gd, d); return; }
                }
        }

        // ── Generator Area ──────────────────────────────────────────────────────────────────────

        void DrawGeneratorArea(DotGenerator gen, DotGenGeneratorData gd)
        {
            Color outline = WithAlpha(gen.color, AlphaAA);
            int n = Mathf.Min(gd.areas.Count, GeneratorAreaCap);
            for (int i = 0; i < n; i++)
            {
                var a = gd.areas[i];
                DrawDashedShape(a, gen.shape, outline, 1.5f, 6f, 4f);
                DrawAnchorMark(a, gen.color);
            }
        }

        void DrawAnchorMark(in DotArea a, Color color)
        {
            Vector2 q = FrameToScreen(a.anchorX, a.anchorY);
            Handles.color = GizmoAnchorFill;
            Handles.DrawSolidDisc(new Vector3(q.x, q.y, 0f), Vector3.forward, 7f);

            Color solid = color; solid.a = 1f;
            DrawCircleOutline(q, 7f, solid, 2f);
            DrawLineSeg(q + new Vector2(-11f, 0f), q + new Vector2(11f, 0f), solid, 2f);
            DrawLineSeg(q + new Vector2(0f, -11f), q + new Vector2(0f, 11f), solid, 2f);
        }

        // ── Placement (Grid / Radial Grid / Box Row) ────────────────────────────────────────────

        void DrawPlacementGizmo(DotGenerator gen, DotGenGeneratorData gd, DotPlacement placement)
        {
            Color outline = WithAlpha(gen.color, Alpha88);
            Color guide = WithAlpha(gen.color, Alpha54);
            int n = Mathf.Min(gd.areas.Count, PlacementAreaCap);

            if (placement is DotBoxRowPlacement)
            {
                // gd.baseDots is appended per-area, in the same order as gd.areas (DotGenEvaluator.Walk), so a
                // single forward cursor finds each area's own cells without a re-scan or a re-evaluation.
                int cursor = 0;
                for (int i = 0; i < n; i++)
                {
                    var a = gd.areas[i];
                    DrawDashedShape(a, gen.shape, outline, 1f, 5f, 5f);
                    while (cursor < gd.baseDots.Count && gd.baseDots[cursor].area.idx == a.idx)
                    {
                        var p = gd.baseDots[cursor];
                        if (p.hasCell)
                        {
                            var cell = new DotArea { cx = p.x, cy = p.y, w = p.cellW, h = p.cellH, rot = p.cellRot };
                            DrawDashedShape(cell, DotShape.Rectangle, outline, 1f, 3f, 3f);
                        }
                        cursor++;
                    }
                }
                return;
            }

            if (placement is DotGridPlacement grid)
            {
                for (int i = 0; i < n; i++)
                {
                    var a = gd.areas[i];
                    DrawDashedShape(a, gen.shape, outline, 1f, 5f, 5f);
                    DrawGridGuides(a, grid, guide);
                }
                return;
            }

            if (placement is DotRadialPlacement radial)
            {
                for (int i = 0; i < n; i++)
                {
                    var a = gd.areas[i];
                    DrawDashedShape(a, gen.shape, outline, 1f, 5f, 5f);
                    DrawRadialRings(a, radial, guide);
                }
            }
        }

        /// Nominal row/column guides at the lattice's spacing only — never the grid's own skew, rotation or
        /// offset, matching the reference (those show up on the DOTS themselves, not this guide). Local points
        /// run the full local -0.5..0.5 span at each column/row's x/y, transformed by the area exactly like a
        /// real dot would be.
        void DrawGridGuides(in DotArea a, DotGridPlacement grid, Color color)
        {
            int co = Mathf.Max(1, grid.columns);
            int ro = Mathf.Max(1, grid.rows);
            float spanX = 0.9f + grid.gapX / 180f;
            float spanY = 0.9f + grid.gapY / 180f;

            for (int x = 0; x < co; x++)
            {
                float lx = co == 1 ? 0f : (x / (float)(co - 1) - 0.5f) * spanX;
                DrawDashedLine(FrameToScreen(DotGenMath.Xform(lx, -0.5f, a)),
                                FrameToScreen(DotGenMath.Xform(lx, 0.5f, a)), color, 0.8f, 3f, 5f);
            }
            for (int y = 0; y < ro; y++)
            {
                float ly = ro == 1 ? 0f : (y / (float)(ro - 1) - 0.5f) * spanY;
                DrawDashedLine(FrameToScreen(DotGenMath.Xform(-0.5f, ly, a)),
                                FrameToScreen(DotGenMath.Xform(0.5f, ly, a)), color, 0.8f, 3f, 5f);
            }
        }

        /// One dashed ellipse per ring, at the same radius curve DotRadialPlacement.Evaluate uses — a ring is
        /// always drawn as an ellipse regardless of the generator's own area shape, matching the reference.
        void DrawRadialRings(in DotArea a, DotRadialPlacement radial, Color color)
        {
            int n = Mathf.Max(1, radial.rings);
            float exp = Mathf.Pow(2f, radial.ringSpacingCurve / 55f);

            for (int r = 0; r < n; r++)
            {
                float t = n == 1 ? 0.5f : r / (float)(n - 1);
                float ord = radial.flow == DotRadialFlow.EdgeToCenter ? 1f - t : t;
                float u = Mathf.Pow(ord, exp);
                float rad = DotGenMath.Lerp(radial.innerRadius / 200f, radial.outerRadius / 200f, u);
                DrawDashedRing(a, rad, color, 0.8f, 3f, 5f);
            }
        }

        // ── Selector (amber weight dots; Margin band; Gradient arrow) ──────────────────────────

        void DrawSelectorGizmo(DotGenerator gen, DotGenGeneratorData gd, DotSelector sel)
        {
            int seed = doc.seed;
            int nDots = Mathf.Min(gd.baseDots.Count, SelectorDotCap);
            for (int i = 0; i < nDots; i++)
            {
                var p = gd.baseDots[i];
                float w = sel.Weight(gen, p.area, p.x, p.y, i, seed);
                Vector2 q = FrameToScreen(p.x, p.y);
                Handles.color = new Color(GizmoAmber.r, GizmoAmber.g, GizmoAmber.b, 0.08f + 0.72f * w);
                Handles.DrawSolidDisc(new Vector3(q.x, q.y, 0f), Vector3.forward, 2f + 3f * w);
            }

            int nAreas = Mathf.Min(gd.areas.Count, SelectorAreaCap);
            for (int i = 0; i < nAreas; i++)
            {
                var a = gd.areas[i];
                if (sel is DotGradientSelector grad)
                {
                    float an = grad.angle * Mathf.Deg2Rad;
                    Vector2 cc = new Vector2(a.cx + Mathf.Cos(an) * a.w * 0.35f, a.cy + Mathf.Sin(an) * a.h * 0.35f);
                    Vector2 dd = new Vector2(a.cx - Mathf.Cos(an) * a.w * 0.35f, a.cy - Mathf.Sin(an) * a.h * 0.35f);
                    DrawLineSeg(FrameToScreen(dd), FrameToScreen(cc), WithAlpha(GizmoAmber, AlphaBB), 2f);
                    Vector2 q = FrameToScreen(cc);
                    Handles.color = GizmoAmber;
                    Handles.DrawSolidDisc(new Vector3(q.x, q.y, 0f), Vector3.forward, 4f);
                }
                else if (sel is DotMarginSelector)
                {
                    DrawDashedShape(a, gen.shape, WithAlpha(GizmoAmber, Alpha88), 1f, 2f, 3f);
                }
            }
        }

        // ── Mutator (Nudge / Warp vectors; Cull marks; Warp centres/lines) ──────────────────────

        void DrawMutatorGizmo(DotGenerator gen, DotGenGeneratorData gd, DotMutator m)
        {
            gzTraceBuf.Clear();
            for (int i = 0; i < gd.traces.Count; i++)
                if (gd.traces[i].moduleId == m.id) gzTraceBuf.Add(gd.traces[i]);

            int nTraces = Mathf.Min(gzTraceBuf.Count, MutatorTraceCap);
            bool isCull = m is DotCullMutator;
            var warp = m as DotWarpMutator;
            Color vectorColor = warp != null ? GizmoWarpVector : GizmoNudgeVector;

            for (int ti = 0; ti < nTraces; ti++)
            {
                var t = gzTraceBuf[ti];

                if (isCull)
                {
                    int nMarks = Mathf.Min(t.removed.Count, MutatorVectorCap);
                    for (int i = 0; i < nMarks; i++)
                        DrawCullMark(FrameToScreen(t.removed[i].x, t.removed[i].y));
                    continue;
                }

                int n = Mathf.Min(Mathf.Min(t.before.Count, t.after.Count), MutatorVectorCap);
                for (int i = 0; i < n; i++)
                    DrawLineSeg(FrameToScreen(t.before[i].x, t.before[i].y),
                                FrameToScreen(t.after[i].x, t.after[i].y), vectorColor, 1f);

                if (warp == null) continue;

                if (warp.fieldSource == DotWarpField.PointCenters)
                {
                    gzCenterBuf.Clear();
                    warp.Centers(t.area, doc.seed, gzCenterBuf);
                    for (int i = 0; i < gzCenterBuf.Count; i++)
                        DrawWarpCenter(FrameToScreen(gzCenterBuf[i]));
                }
                else
                {
                    DrawWarpFieldLines(t.area, warp);
                }
            }
        }

        void DrawCullMark(Vector2 q)
        {
            DrawLineSeg(q + new Vector2(-3f, -3f), q + new Vector2(3f, 3f), GizmoCull, 1.2f);
            DrawLineSeg(q + new Vector2(3f, -3f), q + new Vector2(-3f, 3f), GizmoCull, 1.2f);
        }

        void DrawWarpCenter(Vector2 q)
        {
            DrawCircleOutline(q, 7f, GizmoWarpCenter, 2f);
            DrawLineSeg(q + new Vector2(-10f, 0f), q + new Vector2(10f, 0f), GizmoWarpCenter, 2f);
            DrawLineSeg(q + new Vector2(0f, -10f), q + new Vector2(0f, 10f), GizmoWarpCenter, 2f);
        }

        /// Nine parallel lines through the area's field, each a full-length segment centred on the area (the
        /// reference draws these long enough to run off both edges of the frame rather than clipped to the
        /// area — matched as-is: a field line is a property of the whole plane, not just the area it warps).
        void DrawWarpFieldLines(in DotArea a, DotWarpMutator warp)
        {
            float an = warp.lineAngle * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(an), Mathf.Sin(an));
            Vector2 norm = new Vector2(-Mathf.Sin(an), Mathf.Cos(an));
            float spacing = warp.lineSpacing / 100f * Mathf.Min(a.w, a.h);

            for (int k = -4; k <= 4; k++)
            {
                Vector2 c = new Vector2(a.cx + norm.x * k * spacing, a.cy + norm.y * k * spacing);
                DrawDashedLine(FrameToScreen(c - dir), FrameToScreen(c + dir), GizmoWarpLine, 1f, 4f, 4f);
            }
        }

        // ── Drawer (magenta dashed eligible-target outlines) ────────────────────────────────────

        void DrawDrawerGizmo(DotGenerator gen, DotGenGeneratorData gd, DotDrawer drawer)
        {
            gzTargetBuf.Clear();
            drawer.Targets(gen, gd, doc.seed, gzTargetBuf);
            int n = Mathf.Min(gzTargetBuf.Count, DrawerTargetCap);
            for (int i = 0; i < n; i++)
            {
                var t = gzTargetBuf[i];
                DrawDashedShape(t.area, t.shape, GizmoDrawer, 1.2f, 3f, 3f);
            }
        }

        // ── low-level drawing primitives ────────────────────────────────────────────────────────

        static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        void BuildShapePoints(in DotArea a, DotShape shape, List<Vector2> outPts)
        {
            outPts.Clear();
            switch (shape)
            {
                case DotShape.Ellipse:
                    const int seg = 40;
                    for (int i = 0; i < seg; i++)
                    {
                        float t = i / (float)seg * DotGenMath.Tau;
                        outPts.Add(FrameToScreen(DotGenMath.Xform(Mathf.Cos(t) * 0.5f, Mathf.Sin(t) * 0.5f, a)));
                    }
                    break;

                case DotShape.Diamond:
                    outPts.Add(FrameToScreen(DotGenMath.Xform(0f, -0.5f, a)));
                    outPts.Add(FrameToScreen(DotGenMath.Xform(0.5f, 0f, a)));
                    outPts.Add(FrameToScreen(DotGenMath.Xform(0f, 0.5f, a)));
                    outPts.Add(FrameToScreen(DotGenMath.Xform(-0.5f, 0f, a)));
                    break;

                default:   // Rectangle
                    outPts.Add(FrameToScreen(DotGenMath.Xform(-0.5f, -0.5f, a)));
                    outPts.Add(FrameToScreen(DotGenMath.Xform(0.5f, -0.5f, a)));
                    outPts.Add(FrameToScreen(DotGenMath.Xform(0.5f, 0.5f, a)));
                    outPts.Add(FrameToScreen(DotGenMath.Xform(-0.5f, 0.5f, a)));
                    break;
            }
        }

        void DrawDashedShape(in DotArea a, DotShape shape, Color color, float width, float dash, float gap)
        {
            BuildShapePoints(a, shape, gzShapePts);
            DrawDashedPolyline(gzShapePts, true, color, width, dash, gap);
        }

        void DrawDashedRing(in DotArea a, float localRadius, Color color, float width, float dash, float gap)
        {
            gzRingPts.Clear();
            const int seg = 40;
            for (int i = 0; i < seg; i++)
            {
                float t = i / (float)seg * DotGenMath.Tau;
                gzRingPts.Add(FrameToScreen(DotGenMath.Xform(Mathf.Cos(t) * localRadius, Mathf.Sin(t) * localRadius, a)));
            }
            DrawDashedPolyline(gzRingPts, true, color, width, dash, gap);
        }

        /// Marches a chain of screen points in dash/gap steps, carrying the phase across each edge — one
        /// continuous dash pattern around the whole shape, matching a canvas stroking one closed Path2D.
        static void DrawDashedPolyline(List<Vector2> pts, bool closed, Color color, float width, float dash, float gap)
        {
            if (pts.Count < 2) return;
            Handles.color = color;
            bool on = true;
            float remaining = dash;
            int edges = closed ? pts.Count : pts.Count - 1;

            for (int e = 0; e < edges; e++)
            {
                Vector2 a = pts[e];
                Vector2 b = pts[(e + 1) % pts.Count];
                Vector2 segv = b - a;
                float len = segv.magnitude;
                if (len < 1e-4f) continue;
                Vector2 dir = segv / len;

                float travelled = 0f;
                int guard = 0;
                // The guard is a safety valve, not a design cap: at any sane zoom this loop ends in a handful
                // of dashes. It only matters if geometry ever goes degenerate (e.g. an extreme zoom driving
                // `len` far past what dash/gap were sized for) — better a slightly short dashed line than a
                // frozen editor.
                while (travelled < len && guard++ < DashSafetyLimit)
                {
                    float step = Mathf.Min(remaining, len - travelled);
                    if (on)
                    {
                        gzSegBuf[0] = a + dir * travelled;
                        gzSegBuf[1] = a + dir * (travelled + step);
                        Handles.DrawAAPolyLine(width, gzSegBuf);
                    }
                    travelled += step;
                    remaining -= step;
                    if (remaining <= 1e-4f) { on = !on; remaining = on ? dash : gap; }
                }
            }
        }

        static void DrawDashedLine(Vector2 a, Vector2 b, Color color, float width, float dash, float gap)
        {
            Handles.color = color;
            Vector2 segv = b - a;
            float len = segv.magnitude;
            if (len < 1e-4f) return;
            Vector2 dir = segv / len;

            bool on = true;
            float remaining = dash;
            float travelled = 0f;
            int guard = 0;
            while (travelled < len && guard++ < DashSafetyLimit)
            {
                float step = Mathf.Min(remaining, len - travelled);
                if (on)
                {
                    gzSegBuf[0] = a + dir * travelled;
                    gzSegBuf[1] = a + dir * (travelled + step);
                    Handles.DrawAAPolyLine(width, gzSegBuf);
                }
                travelled += step;
                remaining -= step;
                if (remaining <= 1e-4f) { on = !on; remaining = on ? dash : gap; }
            }
        }

        static void DrawLineSeg(Vector2 a, Vector2 b, Color color, float width)
        {
            Handles.color = color;
            gzSegBuf[0] = a;
            gzSegBuf[1] = b;
            Handles.DrawAAPolyLine(width, gzSegBuf);
        }

        static void DrawCircleOutline(Vector2 center, float radius, Color color, float width)
        {
            Handles.color = color;
            for (int i = 0; i <= 24; i++)
            {
                float t = i / 24f * DotGenMath.Tau;
                gzCircleBuf[i] = new Vector3(center.x + Mathf.Cos(t) * radius, center.y + Mathf.Sin(t) * radius, 0f);
            }
            Handles.DrawAAPolyLine(width, gzCircleBuf);
        }
    }
}
