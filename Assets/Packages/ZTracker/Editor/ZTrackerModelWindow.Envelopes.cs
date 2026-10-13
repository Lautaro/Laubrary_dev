using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    // Envelope columns (T-0601): a line drawn down the pattern that drives one command smoothly. The left edge is 00
    // (the low end of the command's range), the right edge FF. Points sit on rows; each segment runs straight to the
    // next point or holds until it. Points are stored as the track's automation lane for the column, so the engine
    // plays them like any lane (ZTrackerEnvelopes; COMMAND_MODEL_DESIGN.md section 10).
    public sealed partial class ZTrackerModelWindow
    {
        const float EnvelopeWidth = 48, EnvelopePad = 4, EnvelopeRise = 3;
        bool envelopeGesture, envelopeMoved, envelopeMove, envelopeRecorded;
        int envelopeTrack, envelopeColumn, envelopeGroup, envelopeLastRow, envelopeStartRow;
        float envelopeLastValue;
        Vector2 envelopePress;
        List<AutomationPoint> envelopeOriginal;
        int envelopeMoving;

        // ───────────── what a column is bound to ─────────────

        EnvelopeColumnData EnvelopeColumn(int trackIndex, int column) => Data == null || trackIndex < 0 || trackIndex >= Data.tracks.Count ? null : ZTrackerEnvelopes.Column(Data.tracks[trackIndex], column);
        EnvelopeColumnData EnvelopeColumn(FieldSlot f) => EnvelopeColumn(f.track, f.column);
        AutomationLane EnvelopeLane(FieldSlot f) => ZTrackerEnvelopes.Lane(PatternTrack(f.track), EnvelopeColumn(f));

        /// The command id the column drives (a built-in or the song command's current id), or null when unbound or removed.
        string EnvelopeId(int trackIndex, int column)
        {
            var c = EnvelopeColumn(trackIndex, column); if (c == null || string.IsNullOrEmpty(c.command)) return null;
            if (ZTrackerEnvelopes.IsBuiltin(c.command)) return c.command;
            var p = ZTrackerCommandRegistry.FindByUid(Data, c.command); return p == null ? null : ZTrackerCommandRegistry.Normalize(p.id);
        }
        bool EnvelopeBroken(int trackIndex, int column) { var c = EnvelopeColumn(trackIndex, column); return c != null && !string.IsNullOrEmpty(c.command) && EnvelopeId(trackIndex, column) == null; }
        string EnvelopeCommandName(int trackIndex, int column)
        {
            var c = EnvelopeColumn(trackIndex, column); string id = EnvelopeId(trackIndex, column); if (id == null) return null;
            var p = ZTrackerEnvelopes.IsBuiltin(c.command) ? null : ZTrackerCommandRegistry.FindByUid(Data, c.command);
            if (p != null) return id + " " + p.name + " (00 = " + p.min.ToString("0.##") + ", FF = " + p.max.ToString("0.##") + ")";
            var entry = FxCommandCatalog.Builtin(id, FieldKind.EffectId); return id + " " + (entry == null ? "" : entry.description + " (" + entry.range + ")");
        }
        Color EnvelopeColor(int trackIndex, int column)
        {
            string id = EnvelopeId(trackIndex, column); if (id == null) return new Color(.6f, .6f, .6f, 1);
            RefreshLookDefinitions(); var look = Look(FieldKind.EffectId, trackIndex, id); return look.known ? look.color : CommandColors.UndefinedBack;
        }
        /// "VL 9F (62%)": the command, the byte and the share of its range a normalized value means.
        string EnvelopeValueText(int trackIndex, int column, float u) => (EnvelopeId(trackIndex, column) ?? "ENV") + " " + Mathf.RoundToInt(u * 255).ToString("X2") + " (" + Mathf.RoundToInt(u * 100) + "%)";

        // ───────────── grid: titles, cells, drawing ─────────────

        void AddEnvelopeTitle(FieldSlot f, float y)
        {
            int ti = f.track, column = f.column; string id = EnvelopeId(ti, column);
            var l = Z.Text(EnvelopeBroken(ti, column) ? "??" : id ?? "ENV" + (column + 1), tooltip: EnvelopeTitleTooltip(ti, column));
            l.name = "pattern-env-title-" + ti + "-" + column; l.AddToClassList("tracker-cell"); l.AddToClassList("tracker-env-title");
            int at = fields.IndexOf(f); Place(l, fieldX[at], y, EnvelopeWidth); l.style.textOverflow = TextOverflow.Ellipsis; l.style.backgroundColor = new Color(.14f, .14f, .14f, 1);
            if (EnvelopeBroken(ti, column)) CommandColors.Paint(l, new CommandLook(true, false, false, CommandKind.None, CommandColors.UndefinedBack, null));
            else if (id != null) { RefreshLookDefinitions(); var look = Look(FieldKind.EffectId, ti, id); CommandColors.Paint(l, look); if (!look.user && look.known) l.style.backgroundColor = new Color(.14f, .14f, .14f, 1); }
            l.RegisterCallback<PointerDownEvent>(e => { if (e.button != 1) return; ShowEnvelopeMenu(ti, column, l); e.StopPropagation(); });
            gridCanvas.Add(l); Column(ti).Add(l);
        }

        string EnvelopeTitleTooltip(int trackIndex, int column)
        {
            string name = "ENV" + (column + 1);
            if (EnvelopeBroken(trackIndex, column)) return name + " drove a command that no longer exists; its points are kept. Right-click (or Alt+B in the column) to bind it to another command or remove the column.";
            string bound = EnvelopeCommandName(trackIndex, column);
            if (bound == null) return "Envelope column " + name + ", not bound yet. Right-click (or Alt+B in the column) to choose the command its line drives.";
            return name + " draws " + bound + " down the pattern: left edge 00, right edge FF. Right-click (or Alt+B in the column) to drive another command or remove the column.";
        }

        string EnvelopeTooltip(FieldSlot f)
        {
            string bound = EnvelopeCommandName(f.track, f.column);
            return Data.tracks[f.track].name + ", envelope column " + (f.column + 1) + (bound == null ? " (not bound: right-click its title or press Alt+B)" : " driving " + bound) + ". "
                + "The line runs down the pattern: left edge 00, right edge FF; the command follows it smoothly between the first and last point, and a typed command for it takes over for its own row. "
                + "In Edit mode: click to add a point, drag a point to move it, drag across rows to draw (hold Alt to snap to sixteenths); right-click a point to delete it, right-click the line for straight or held; "
                + "Left/Right nudge the value (Alt for fine), two hex digits set it, Delete removes the point; Ctrl+Left/Right moves to the next column.";
        }

        string EnvelopeCellTooltip(int r, FieldSlot f)
        {
            var lane = EnvelopeLane(f); var point = ZTrackerEnvelopes.PointAt(lane, r); var at = ZTrackerEnvelopes.ValueAt(lane, r);
            string state = point != null ? "point " + EnvelopeValueText(f.track, f.column, point.value) : at.HasValue ? "on the line, " + EnvelopeValueText(f.track, f.column, at.Value) : "no line here (the command keeps its value)";
            return "Row " + r + ": " + state + ". " + EnvelopeTooltip(f);
        }

        string EnvelopeHelp(FieldSlot f)
        {
            string id = EnvelopeId(f.track, f.column), name = EnvelopeCommandName(f.track, f.column);
            if (EnvelopeBroken(f.track, f.column)) return "⚠ ENV" + (f.column + 1) + " drives a command that no longer exists — right-click its title to bind it again";
            if (id == null) return "ENV" + (f.column + 1) + " is not bound — Alt+B or right-click its title to choose the command its line drives";
            var lane = EnvelopeLane(f); var point = ZTrackerEnvelopes.PointAt(lane, row); var at = ZTrackerEnvelopes.ValueAt(lane, row);
            return name + " — row " + row + ": " + (point != null ? "point " + EnvelopeValueText(f.track, f.column, point.value) : at.HasValue ? "line " + EnvelopeValueText(f.track, f.column, at.Value) : "no line (value held)");
        }

        void AttachEnvelopePainter(Label label, int r, int fieldIndex)
        {
            var painter = new VisualElement { name = "env-line", pickingMode = PickingMode.Ignore };
            painter.style.position = Position.Absolute; painter.style.left = 0; painter.style.top = 0; painter.style.width = EnvelopeWidth; painter.style.height = 20;
            painter.generateVisualContent += ctx => { if (fieldIndex < fields.Count && fields[fieldIndex].kind == FieldKind.Envelope) PaintEnvelopeCell(ctx.painter2D, r, fields[fieldIndex]); };
            label.Add(painter);
        }

        static float EnvelopeX(float u) => EnvelopePad + Mathf.Clamp01(u) * (EnvelopeWidth - 2 * EnvelopePad);

        /// One row's share of the line: times r−0.15 … r+0.85, drawn so a point on row r sits just under the row's top.
        void PaintEnvelopeCell(Painter2D painter, int r, FieldSlot f)
        {
            var lane = EnvelopeLane(f); if (lane?.points == null || lane.points.Count == 0) return;
            var color = EnvelopeColor(f.track, f.column);
            double from = r - EnvelopeRise / 20d, to = from + 1, first = lane.points[0].line, last = lane.points[lane.points.Count - 1].line;
            float Y(double t) => (float)((t - r) * 20 + EnvelopeRise);
            double t0 = Math.Max(from, first), t1 = Math.Min(to, last);
            if (t1 > t0)
            {
                painter.strokeColor = color; painter.lineWidth = 1.5f; painter.lineJoin = LineJoin.Round; painter.BeginPath();
                painter.MoveTo(new Vector2(EnvelopeX(ZTrackerEnvelopes.ValueAt(lane, t0) ?? 0), Y(t0)));
                bool ended = false;
                foreach (var p in lane.points)
                {
                    if (p.line <= t0 || p.line > t1) continue;
                    // A held segment arrives at the old value and jumps across to the point's value.
                    float before = ZTrackerEnvelopes.ValueAt(lane, p.line, before: true) ?? p.value;
                    painter.LineTo(new Vector2(EnvelopeX(before), Y(p.line)));
                    painter.LineTo(new Vector2(EnvelopeX(p.value), Y(p.line)));
                    ended = p.line == t1;
                }
                if (!ended) painter.LineTo(new Vector2(EnvelopeX(ZTrackerEnvelopes.ValueAt(lane, t1) ?? 0), Y(t1)));
                painter.Stroke();
            }
            painter.fillColor = color;
            foreach (var p in lane.points)
            {
                if (p.line < from || p.line >= to) continue;
                painter.BeginPath(); painter.Arc(new Vector2(EnvelopeX(p.value), Y(p.line)), 2.6f, 0, 360); painter.Fill();
            }
        }

        // ───────────── editing helpers (data edits; callers wrap them in one Undo step) ─────────────

        /// The points of a column within one row (a row's own point, plus any fractional ones an automation edit made).
        List<AutomationPoint> EnvelopeRowPoints(FieldSlot f, int r)
        {
            var lane = EnvelopeLane(f); var list = new List<AutomationPoint>();
            if (lane?.points != null) foreach (var p in lane.points) if (p != null && p.line >= r && p.line < r + 1) list.Add(new AutomationPoint { line = p.line - r, value = p.value, segment = p.segment });
            return list;
        }

        void RemoveEnvelopeRow(FieldSlot f, int r)
        {
            var pt = PatternTrack(f.track); var lane = ZTrackerEnvelopes.Lane(pt, EnvelopeColumn(f)); if (lane == null) return;
            lane.points.RemoveAll(p => p != null && p.line >= r && p.line < r + 1);
            if (lane.points.Count == 0) pt.automation.Remove(lane);
        }

        void ClearEnvelopeRows(FieldSlot f, int r0, int r1, string label)
        {
            bool any = false; for (int r = r0; r <= r1; r++) any |= EnvelopeRowPoints(f, r).Count > 0; if (!any) return;
            SongEdit(label, () => { for (int r = r0; r <= r1; r++) RemoveEnvelopeRow(f, r); });
        }

        /// Paste Insert: points from <paramref name="start"/> down move down by <paramref name="height"/> rows; any pushed past the last row are dropped.
        void ShiftEnvelope(FieldSlot f, int start, int height)
        {
            var pt = PatternTrack(f.track); var lane = ZTrackerEnvelopes.Lane(pt, EnvelopeColumn(f)); if (lane == null) return;
            foreach (var p in lane.points) if (p != null && p.line >= start) p.line += height;
            lane.points.RemoveAll(p => p != null && p.line > Pattern.lineCount - 1);
            if (lane.points.Count == 0) pt.automation.Remove(lane);
        }

        /// Writes copied row points into a column's row (replacing what is there). False when the column cannot take them.
        bool PasteEnvelopeRow(FieldSlot f, int r, List<AutomationPoint> points)
        {
            var column = EnvelopeColumn(f); if (column == null || string.IsNullOrEmpty(column.command)) return false;
            RemoveEnvelopeRow(f, r);
            if (points == null || points.Count == 0) return true;
            var lane = ZTrackerEnvelopes.EnsureLane(PatternTrack(f.track, true), Data.tracks[f.track], column);
            foreach (var p in points) if (r + p.line <= Pattern.lineCount - 1) lane.points.Add(new AutomationPoint { line = r + p.line, value = p.value, segment = p.segment });
            lane.points.Sort((a, b) => a.line.CompareTo(b.line));
            if (lane.points.Count == 0) PatternTrack(f.track).automation.Remove(lane);
            return true;
        }

        bool EnvelopeEditable(FieldSlot f)
        {
            if (EnvelopeId(f.track, f.column) != null) return true;
            patternNotice = "ENV" + (f.column + 1) + " is not bound to a command yet: Alt+B or right-click its title to choose one, then draw.";
            RefreshHelp(); return false;
        }

        void SetEnvelopePoint(FieldSlot f, int r, float u, string label)
        {
            if (!EnvelopeEditable(f)) return;
            SongEdit(label, () => ZTrackerEnvelopes.Set(ZTrackerEnvelopes.EnsureLane(PatternTrack(f.track, true), Data.tracks[f.track], EnvelopeColumn(f)), r, u));
        }

        // ───────────── keyboard ─────────────

        /// Left/Right in Edit mode: move the value at the cursor row by steps of 1/255 (a point is made from the line or the middle).
        void NudgeEnvelope(int steps)
        {
            var f = fields[sub]; var lane = EnvelopeLane(f);
            float current = ZTrackerEnvelopes.PointAt(lane, row)?.value ?? ZTrackerEnvelopes.ValueAt(lane, row) ?? .5f;
            float next = Mathf.Clamp(Mathf.Round(current * 255 + steps), 0, 255) / 255f;
            SetEnvelopePoint(f, row, next, "nudge envelope point");
        }

        /// Two hex digits set the point at the cursor row (00 = left edge, FF = right edge), then the cursor advances.
        bool EnterEnvelopeCharacter(FieldSlot f, char ch)
        {
            int nibble = ch >= '0' && ch <= '9' ? ch - '0' : ch >= 'A' && ch <= 'F' ? ch - 'A' + 10 : -1;
            if (nibble < 0) { patternNotice = "ENV" + (f.column + 1) + " takes two hex digits (00 = left edge, FF = right edge); Left/Right nudge, Delete removes the point."; RefreshHelp(); return true; }
            if (!EnvelopeEditable(f)) return true;
            var point = ZTrackerEnvelopes.PointAt(EnvelopeLane(f), row); int previous = point == null ? 0 : Mathf.RoundToInt(point.value * 255);
            int value = hexDigit == 0 ? nibble << 4 : (previous & 0xF0) | nibble;
            SetEnvelopePoint(f, row, value / 255f, "enter envelope value"); hexDigit++; if (hexDigit >= 2) Advance(); return true;
        }

        // ───────────── mouse ─────────────

        float EnvelopeValueUnder(Vector2 world, int fieldIndex, bool snap)
        {
            var p = gridCanvas.WorldToLocal(world); float u = Mathf.Clamp01((p.x - fieldX[fieldIndex] - EnvelopePad) / (EnvelopeWidth - 2 * EnvelopePad));
            // The outermost sliver of each side is the edge itself, so 00 and FF are easy to hit.
            if (u < .015f) u = 0; else if (u > .985f) u = 1;
            return snap ? Mathf.Round(u * 16) / 16 : u;
        }
        int RowUnder(Vector2 world) => Mathf.Clamp(Mathf.FloorToInt(gridCanvas.WorldToLocal(world).y / 20) - 2, 0, Pattern.lineCount - 1);

        /// A press in an envelope cell (Edit mode): on a row with a point it picks the point up to move it; elsewhere it
        /// adds a point and keeps drawing one point per row while the pointer moves. The whole gesture is one Undo step.
        bool BeginEnvelopeGesture(int r, int c, Vector2 world, bool snap)
        {
            var f = fields[c]; Select(r, c, false); if (!EnvelopeEditable(f)) return false;
            envelopeGesture = true; envelopeMoved = false; envelopeRecorded = false; envelopeTrack = f.track; envelopeColumn = f.column; envelopePress = world; envelopeStartRow = r;
            var lane = EnvelopeLane(f); var point = ZTrackerEnvelopes.PointAt(lane, r);
            envelopeMove = point != null;
            if (envelopeMove) { envelopeOriginal = lane.points.Select(p => new AutomationPoint { line = p.line, value = p.value, segment = p.segment }).ToList(); envelopeMoving = lane.points.IndexOf(point); return true; }
            float u = EnvelopeValueUnder(world, c, snap);
            if (!RecordEnvelopeGesture()) return false;
            ZTrackerEnvelopes.Set(ZTrackerEnvelopes.EnsureLane(PatternTrack(f.track, true), Data.tracks[f.track], EnvelopeColumn(f)), r, u);
            envelopeLastRow = r; envelopeLastValue = u; RefreshCells(); return true;
        }

        bool RecordEnvelopeGesture()
        {
            if (envelopeRecorded) return true;
            Undo.IncrementCurrentGroup(); envelopeGroup = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Tracker: draw envelope");
            try { CompleteUndo(song, "Tracker: draw envelope"); } catch (Exception ex) { lastError = ex.Message; RefreshTransport(); envelopeGesture = false; return false; }
            envelopeRecorded = true; return true;
        }

        void EnvelopeDragTo(Vector2 world, bool snap)
        {
            if (!envelopeGesture || gridCanvas == null || Pattern == null) return;
            int fieldIndex = fields.FindIndex(x => x.track == envelopeTrack && x.column == envelopeColumn && x.kind == FieldKind.Envelope); if (fieldIndex < 0) return;
            var f = fields[fieldIndex]; int r = RowUnder(world); float u = EnvelopeValueUnder(world, fieldIndex, snap);
            if (envelopeMove)
            {
                // A small wobble while clicking an existing point does not move it.
                if (!envelopeMoved && (world - envelopePress).sqrMagnitude < 9) return;
                if (!RecordEnvelopeGesture()) return; envelopeMoved = true;
                var lane = EnvelopeLane(f); var points = envelopeOriginal.Select(p => new AutomationPoint { line = p.line, value = p.value, segment = p.segment }).ToList();
                var moving = points[envelopeMoving]; points.RemoveAt(envelopeMoving); points.RemoveAll(p => p.line == r);
                moving.line = r; moving.value = u; points.Add(moving); points.Sort((a, b) => a.line.CompareTo(b.line));
                lane.points.Clear(); lane.points.AddRange(points);
                row = r; RefreshCells(); return;
            }
            var target = ZTrackerEnvelopes.EnsureLane(PatternTrack(f.track, true), Data.tracks[f.track], EnvelopeColumn(f));
            int from = envelopeLastRow;
            // A fast stroke skips rows between pointer events; fill them along the straight line between the two.
            for (int at = Math.Min(from, r); at <= Math.Max(from, r); at++)
            {
                float v = from == r ? u : Mathf.Lerp(envelopeLastValue, u, (float)Math.Abs(at - from) / Math.Abs(r - from));
                ZTrackerEnvelopes.Set(target, at, v);
            }
            envelopeLastRow = r; envelopeLastValue = u; RefreshCells();
        }

        void EndEnvelopeGesture()
        {
            if (!envelopeGesture) return; envelopeGesture = false;
            if (!envelopeRecorded) return;
            EditorUtility.SetDirty(song); CollapseUndo(envelopeGroup, song); RefreshLive(); RefreshTransport(); RefreshCells(); RefreshCellDetails();
        }

        /// Right-click in an envelope cell: on a point, delete it; on the line, choose how the segment runs.
        void EnvelopeContext(int r, int c, VisualElement anchor)
        {
            var f = fields[c]; Select(r, c, false); grid?.Focus(); var lane = EnvelopeLane(f);
            if (ZTrackerEnvelopes.PointAt(lane, r) != null) { ClearEnvelopeRows(f, r, r, "delete envelope point"); return; }
            var segment = ZTrackerEnvelopes.SegmentAt(lane, r);
            if (segment == null) { patternNotice = "No line on row " + r + (editMode ? ": click to add a point." : ": turn on Edit mode, then click to add a point."); RefreshHelp(); return; }
            ShowSegmentMenu(f, segment, anchor);
        }

        void ShowSegmentMenu(FieldSlot f, AutomationPoint segment, VisualElement anchor)
        {
            var lane = EnvelopeLane(f); int at = lane.points.IndexOf(segment); int next = (int)lane.points[at + 1].line, from = (int)segment.line;
            bool step = segment.segment == AutomationShape.Step || segment.segment == AutomationShape.Lane && lane.interpolation == AutomationInterpolation.Step;
            var menu = Z.Menu(anchor).Width(220).Section("Rows " + from + "–" + next);
            menu.Item("Straight line", "Run straight from row " + from + " to row " + next + ": the command glides smoothly.", () => SetSegment(f, from, AutomationShape.Linear), @checked: !step);
            menu.Item("Hold, then jump", "Hold row " + from + "'s value until row " + next + ", then jump to it.", () => SetSegment(f, from, AutomationShape.Step), @checked: step);
            menu.OnClosed(() => grid?.Focus()); menu.Show();
        }

        void SetSegment(FieldSlot f, int pointRow, AutomationShape shape)
        {
            SongEdit(shape == AutomationShape.Step ? "hold envelope segment" : "straight envelope segment", () => { var p = ZTrackerEnvelopes.PointAt(EnvelopeLane(f), pointRow); if (p != null) p.segment = shape; });
            RefreshCellDetails();
        }

        /// The Cell box for an envelope column: how the segment under the cursor runs (straight or held).
        void AddEnvelopeCellDetails(FieldSlot f)
        {
            var lane = EnvelopeLane(f); var segment = ZTrackerEnvelopes.SegmentAt(lane, row); if (segment == null) return;
            bool step = segment.segment == AutomationShape.Step || segment.segment == AutomationShape.Lane && lane.interpolation == AutomationInterpolation.Step;
            int from = (int)segment.line;
            cellDetails.Add(Named(Z.Segmented(step ? 1 : 0, new[] { "Line", "Hold" }, "How the envelope runs from row " + from + " to the next point: a straight line (smooth glide) or held, then a jump at the next point.", v => SetSegment(f, from, v == 1 ? AutomationShape.Step : AutomationShape.Linear)), "envelope-segment"));
        }

        // ───────────── columns: count, binding ─────────────

        internal void SetEnvelopeColumns(int trackIndex, int count, bool bindNew = false)
        {
            if (Data == null || trackIndex < 0 || trackIndex >= Data.tracks.Count) return; var t = Data.tracks[trackIndex]; if (t.kind == TrackKind.Event) return;
            int now = t.envelopeColumns?.Count ?? 0; count = Mathf.Clamp(count, 0, ZTrackerEnvelopes.MaxColumns); if (count == now) return;
            FieldSlot? cursor = fields.Count > 0 ? fields[sub] : (FieldSlot?)null;
            SongEdit(count > now ? "add envelope column" : "remove envelope column", () =>
            {
                if (t.envelopeColumns == null) t.envelopeColumns = new List<EnvelopeColumnData>();
                while (t.envelopeColumns.Count < count) t.envelopeColumns.Add(new EnvelopeColumnData { uid = Id() });
                while (t.envelopeColumns.Count > count) ZTrackerEnvelopes.Remove(Data, t, t.envelopeColumns[t.envelopeColumns.Count - 1]);
            }, true);
            if (cursor.HasValue) { var c = cursor.Value; int at = fields.FindIndex(x => x.track == c.track && x.column == c.column && x.kind == c.kind); if (at < 0) at = fields.FindLastIndex(x => x.track == c.track); if (at >= 0 && at != sub) { sub = at; RefreshCells(); RefreshCellDetails(); } }
            // A new column has nothing to draw until it drives a command: offer the choice straight away.
            if (bindNew && count > now) { int column = count - 1; grid?.schedule.Execute(() => ShowEnvelopeMenu(trackIndex, column)); }
        }

        internal void BindEnvelope(int trackIndex, int column, string key)
        {
            var t = Data.tracks[trackIndex]; var c = ZTrackerEnvelopes.Column(t, column); if (c == null) return;
            SongEdit("bind envelope column", () => { c.command = key ?? ""; ZTrackerEnvelopes.Retarget(Data, t, c); }, true);
        }

        void RemoveEnvelopeColumn(int trackIndex, int column)
        {
            var t = Data.tracks[trackIndex]; var c = ZTrackerEnvelopes.Column(t, column); if (c == null) return;
            SongEdit("remove envelope column", () => ZTrackerEnvelopes.Remove(Data, t, c), true);
        }

        Label EnvelopeTitleLabel(int trackIndex, int column) => gridCanvas?.Q<Label>("pattern-env-title-" + trackIndex + "-" + column);

        internal void ShowEnvelopeMenu(int trackIndex, int column, VisualElement anchor = null)
        {
            CloseFxSuggestions(); bindMenu?.Close(); var t = Data?.tracks.ElementAtOrDefault(trackIndex); var c = ZTrackerEnvelopes.Column(t, column); if (c == null) return;
            if (anchor == null) anchor = EnvelopeTitleLabel(trackIndex, column); if (anchor == null && labels.TryGetValue(new Vector2Int(row, sub), out var cell)) anchor = cell; if (anchor == null) anchor = grid; if (anchor == null) return;
            string key = c.command ?? "", id = EnvelopeId(trackIndex, column);
            var menu = Z.Menu(anchor).Width(320).Search("Type a command id, then Enter", 300);
            menu.Section("ENV" + (column + 1) + (id != null ? " · " + id : key != "" ? " · removed command" : ""));
            menu.Item("Remove column", "Remove ENV" + (column + 1) + " and its points in every pattern; Undo restores them.", () => RemoveEnvelopeColumn(trackIndex, column));
            menu.Section("Song commands", "Settings this song has given a command id. Right-click a slider (or Song › Commands › Add…) to give one an id.");
            var defined = (Data.dynamicParameters ?? new List<DynamicParameterData>()).Where(p => p != null && ZTrackerCommandRegistry.ValidateId(null, p.id, null, out _) == null).OrderBy(p => ZTrackerCommandRegistry.Normalize(p.id), StringComparer.Ordinal).ToList();
            if (defined.Count == 0) menu.Item("None defined yet", "Right-click any slider (or Song › Commands › Add…) to give a setting a command id; an envelope column can then drive it.", null, enabled: false);
            foreach (var p in defined) { var uid = p.uid; menu.Item(ZTrackerCommandRegistry.Normalize(p.id) + "  " + p.name, "Draw " + ZTrackerCommandRegistry.Normalize(p.id) + " with this column: left edge " + p.min.ToString("0.##") + ", right edge " + p.max.ToString("0.##") + ".", () => BindEnvelope(trackIndex, column, uid), @checked: key == uid); }
            menu.Section("Built-in", "Built-in commands that set a continuous value.");
            foreach (var builtin in ZTrackerEnvelopes.Builtins) { var entry = FxCommandCatalog.Builtin(builtin, FieldKind.EffectId); string b = builtin; menu.Item(b + "  " + entry?.description, "Draw " + entry?.Display + " with this column.", () => BindEnvelope(trackIndex, column, b), @checked: key == b); }
            menu.OnClosed(() => { bindMenu = null; grid?.Focus(); }); bindMenu = menu.Show();
        }

        /// + and − for envelope columns, laid over the track header to the left of the FX ones.
        float AddEnvelopeHeaderControls(int ti, float right, float y, List<VisualElement> column)
        {
            var t = Data.tracks[ti]; int count = t.envelopeColumns?.Count ?? 0;
            var plus = HeaderGlyph("+", "pattern-env-add-" + ti, "Add an envelope column to this track (" + count + " of " + ZTrackerEnvelopes.MaxColumns + "): a line drawn down the pattern that drives one command smoothly.", () => SetEnvelopeColumns(ti, count + 1, bindNew: true));
            var minus = HeaderGlyph("−", "pattern-env-remove-" + ti, "Remove this track's last envelope column and its points in every pattern; Undo restores them.", () => SetEnvelopeColumns(ti, count - 1));
            right -= 10; Place(plus, right, y, 10); Place(minus, right, y + 10, 10); plus.style.height = minus.style.height = 10; plus.style.minHeight = minus.style.minHeight = 10;
            foreach (var g in new[] { plus, minus }) { g.AddToClassList("tracker-header-glyph-half"); g.AddToClassList("tracker-header-glyph-env"); }
            plus.style.visibility = count < ZTrackerEnvelopes.MaxColumns ? UnityEngine.UIElements.Visibility.Visible : UnityEngine.UIElements.Visibility.Hidden;
            minus.style.visibility = count > 0 ? UnityEngine.UIElements.Visibility.Visible : UnityEngine.UIElements.Visibility.Hidden;
            gridCanvas.Add(minus); gridCanvas.Add(plus); column.Add(minus); column.Add(plus);
            return 10;
        }
    }
}
