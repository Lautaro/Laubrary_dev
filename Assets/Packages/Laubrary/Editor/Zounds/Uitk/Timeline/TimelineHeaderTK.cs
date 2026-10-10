using System;
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The Zequence window's timeline header (T-0562..T-0565): the edit bar (view, playback and editing actions, then the
    /// readout), the overview strip (the whole Zequence in miniature, the visible window as a box you can drag, and the
    /// time cursor), and the ruler (seconds, finer as you zoom in; click it to set the moment Play from here and Paste use).
    ///
    /// The edit bar is one row that never wraps: every control is always there, and only the readout's text changes, so
    /// nothing below it ever moves when a selection appears or a notice arrives.
    /// </summary>
    internal sealed class TimelineHeaderTK : VisualElement {

        readonly ZequenceEditorWindowTK win;
        readonly VisualElement overview, ruler, editBar;
        Label readout;
        readonly List<Label> tickLabels = new List<Label>();
        readonly Dictionary<string, VisualElement> controls = new Dictionary<string, VisualElement>();
        ZuiToggleButton follow, ripple, loop;

        public const float OverviewHeight = 12f, RulerHeight = 16f;

        ZequenceTimeline TL => win.timeline;
        float lh => EditorGUIUtility.singleLineHeight;

        public TimelineHeaderTK(ZequenceEditorWindowTK win) {
            this.win = win;
            AddToClassList("zs-timeline-header");
            editBar = EditBar();
            Add(editBar);
            overview = new VisualElement();
            overview.AddToClassList("zs-timeline-header__overview"); overview.style.height = OverviewHeight;
            overview.generateVisualContent += PaintOverview;
            overview.tooltip = "The whole Zequence. The box is what the tracks below show: drag it to move the view, or click anywhere to jump there. The white line is the time cursor.";
            overview.RegisterCallback<PointerDownEvent>(OverviewDown);
            overview.RegisterCallback<PointerMoveEvent>(OverviewMove);
            overview.RegisterCallback<PointerUpEvent>(e => { if (overview.HasPointerCapture(e.pointerId)) overview.ReleasePointer(e.pointerId); });
            Add(overview);
            ruler = new VisualElement();
            ruler.AddToClassList("zs-timeline-header__ruler"); ruler.style.height = RulerHeight;
            ruler.generateVisualContent += PaintRuler;
            ruler.tooltip = "Seconds on the Zequence's timeline; the shaded part is past its authored duration. Click to set the moment Play from here and Paste use; drag to select a time range across every track. Ctrl+wheel zooms, the wheel pans.";
            ruler.RegisterCallback<PointerDownEvent>(RulerDown);
            ruler.RegisterCallback<PointerMoveEvent>(RulerMove);
            ruler.RegisterCallback<PointerUpEvent>(e => { if (ruler.HasPointerCapture(e.pointerId)) ruler.ReleasePointer(e.pointerId); rulerDrag = false; win.OnTimelineChanged(); });
            ruler.RegisterCallback<WheelEvent>(e => {
                float t = RulerTime(e.localMousePosition.x);
                if (e.ctrlKey || e.commandKey) TL.ZoomAround(t, e.delta.y > 0f ? 1.18f : 1f / 1.18f);
                else TL.Pan((e.delta.y + e.delta.x) * 0.06f * TL.Span);
                win.OnViewChanged(); e.StopPropagation();
            });
            Add(ruler);
        }

        /// <summary>The edit tools (the edit bar and the overview strip) are optional (owner, 2026-10-08); the ruler stays,
        /// being the tracks' time axis.</summary>
        public void SetEditToolsVisible(bool on) {
            editBar.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            overview.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ─────────────────────────── edit bar ───────────────────────────

        VisualElement EditBar() {
            var r = new VisualElement();
            r.AddToClassList("zs-timeline-header__edit-bar");
            r.style.height = lh;
            float h = lh;
            // Every button and switch is built by the shared verb faces (EditVerbsTK), as the Klip editor's edit bar is:
            // the same verb has the same icon, label and shortcut in both editors.
            Button B(string key, EditVerb v, string tip, Action a, ZUICornerMask corners = ZUICornerMask.All) {
                var b = EditVerbsTK.Button(v, tip, () => { a(); win.OnTimelineChanged(); }, corners, h);
                b.AddToClassList("zs-timeline-header__control");
                controls[key] = b; r.Add(b);
                return b;
            }
            ZuiToggleButton Tg(EditVerb v, string tip, bool val, Action<bool> set, ZUICornerMask corners = ZUICornerMask.All) {
                var t = EditVerbsTK.Toggle(v, tip, val, x => { set(x); win.OnTimelineChanged(); }, corners, h, new Color(0.22f, 0.45f, 0.75f, 1f));
                t.AddToClassList("zs-timeline-header__control");
                r.Add(t);
                return t;
            }
            VisualElement Gap() { var g = ZequenceEditorWindowTK.Gap(6f); r.Add(g); return g; }

            B("fit", EditVerb.FitView, "Fit: show the whole Zequence.", () => TL.Fit(), ZUICornerMask.Left);
            B("zsel", EditVerb.ZoomSelection, "Zoom to the selected time range.", () => { if (TL.hasSel && TL.selB > TL.selA) TL.Show(TL.selA, TL.selB); }, ZUICornerMask.None);
            B("ztrack", EditVerb.ZoomTrack, "Zoom to the selected track's whole source, the parts it does not play included, so a long recording can be searched for the part you want.", ZoomTrack, ZUICornerMask.Right);
            Gap();
            follow = Tg(EditVerb.Follow, "", TL.follow, v => TL.follow = v, ZUICornerMask.Left);
            ripple = Tg(EditVerb.Ripple, "", TL.ripple, v => TL.ripple = v, ZUICornerMask.None);
            loop = Tg(EditVerb.Loop, "", TL.loop, v => TL.loop = v, ZUICornerMask.Right);
            Gap();
            B("here", EditVerb.PlayFromMarker, "Play the whole Zequence from the moment you clicked on the ruler (or the selection's start). Tracks already sounding then start part-way through their audio.", win.PlayFromHere, ZUICornerMask.Left);
            B("aud", EditVerb.PlaySelection, "Play only the selection (Space). With tracks selected, just those tracks, through their live effects; with only a time range, the whole Zequence over that range. Loop repeats it until stopped.", win.AuditionSelection, ZUICornerMask.Right);
            Gap();
            B("trim", EditVerb.Trim, "Trim each selected track to the selected part (T). The kept audio stays exactly where it was; the piece's start moves. In a Zequence a trim never changes the sound anywhere else: a sound used elsewhere gets an excerpt of its own on this track.", () => win.Say(TimelineEdits.TrimToSelection(win, TL)), ZUICornerMask.Left);
            B("untrim", EditVerb.Untrim, "Play each selected track's whole source again, its audio staying where it was. Its curves come back with it.", () => win.Say(TimelineEdits.Untrim(win, TL)), ZUICornerMask.None);
            B("split", EditVerb.Split, "Split each selected track at the selection's start and end, or at the clicked moment (S). Each piece of a local sound gets a sound of its own; pieces of a library sound each play their part of it.", () => win.Say(TimelineEdits.Split(win, TL)), ZUICornerMask.None);
            B("del", EditVerb.Delete, "Delete the selected part of each selected track, or whole tracks when the selection covers them (Delete). With Ripple on, the tracks after it move up to close the gap.", () => win.Say(TimelineEdits.Delete(win, TL)), ZUICornerMask.Right);
            Gap();
            B("copy", EditVerb.Copy, "Copy the selected part of each selected track (Ctrl+C).", () => win.Say(TimelineEdits.Copy(TL)), ZUICornerMask.Left);
            B("paste", EditVerb.Paste, "", () => win.Say(TimelineEdits.Paste(win, TL, win.PasteTime())), ZUICornerMask.Right);
            Gap();
            readout = new Label { pickingMode = PickingMode.Position };
            readout.AddToClassList("zs-lbl"); readout.AddToClassList("zs-greymini");
            readout.AddToClassList("zs-timeline-header__readout");
            r.Add(readout);
            return r;
        }

        void ZoomTrack() {
            foreach (var p in TimelineEdits.Selected(TL)) {
                if (p.klip != null) { TL.Show(p.SourceToTime(0f), p.SourceToTime(p.fileLen)); return; }
                TL.Show(p.start, p.End); return;
            }
            win.Say("Select a track first.");
        }

        /// <summary>Values and state that change without a rebuild (5 Hz and after edits).</summary>
        public void Sync() {
            if (TL == null) return;
            follow.SetValueWithoutNotify(TL.follow); ZS.ApplyOnColor(follow, new Color(0.22f, 0.45f, 0.75f, 1f));
            ripple.SetValueWithoutNotify(TL.ripple); ZS.ApplyOnColor(ripple, new Color(0.22f, 0.45f, 0.75f, 1f));
            loop.SetValueWithoutNotify(TL.loop); ZS.ApplyOnColor(loop, new Color(0.22f, 0.45f, 0.75f, 1f));
            bool parallel = TL.zeq != null && TL.zeq.mode == CompositeZound.Mode.Parallel;
            ripple.SetEnabled(parallel);
            // Auto-tidy holds the view fitted: the zoom buttons have nothing to do then.
            foreach (var k in new[] { "fit", "zsel", "ztrack" }) if (controls.TryGetValue(k, out var zb)) zb.SetEnabled(TL.CanMoveView);
            follow.tooltip = TL.follow ? "Follow is on: once the time cursor enters the view, the view turns a page each time the cursor reaches its right edge. Click to keep the view where you put it."
                                       : "Follow is off: the view stays where you put it while playing, and markers at the edges show where playheads are. Click to have the view turn a page as the cursor reaches its edge.";
            ripple.tooltip = !parallel ? "Ripple only applies to a Parallel Zequence: in the other modes the tracks are alternatives, not one timeline."
                           : TL.ripple ? "Ripple is on: a Delete closes the gap, and a Paste pushes every track starting after it later. A track sounding across that moment stays where it is and is marked. Click to turn off."
                                       : "Ripple is off: Delete and Paste leave every other track where it is. Click to have them move the tracks after them.";
            loop.tooltip = TL.loop ? "Loop is on: Audition repeats the selection until you stop it. Click to play it once."
                                   : "Loop is off: Audition plays the selection once. Click to have it repeat until stopped, to tune a trim by ear.";
            controls["paste"].tooltip = TimelineEdits.CanPaste ? "Paste the copied pieces at the clicked moment or the selection's start, as new tracks (Ctrl+V). A piece of a local sound gets a copy of its own; a piece of a library sound plays its excerpt of it."
                                                                 : "Nothing copied yet: copy a part of a track first (Ctrl+C).";
            bool sel = TL.hasSel, tracks = TL.selTracks.Count > 0;
            controls["zsel"].SetEnabled(sel && TL.selB > TL.selA);
            controls["ztrack"].SetEnabled(tracks);
            controls["aud"].SetEnabled(sel && TL.selB > TL.selA);
            controls["trim"].SetEnabled(sel && tracks && TL.selB > TL.selA);
            controls["untrim"].SetEnabled(tracks);
            controls["split"].SetEnabled(sel && tracks);
            controls["del"].SetEnabled(tracks);
            controls["copy"].SetEnabled(tracks);
            controls["paste"].SetEnabled(TimelineEdits.CanPaste);
            string sText = sel ? (TL.selB > TL.selA ? ZequenceTimeline.Seconds(TL.selA) + " – " + ZequenceTimeline.Seconds(TL.selB) + " (" + ZequenceTimeline.Seconds(TL.selB - TL.selA) + ")" : "at " + ZequenceTimeline.Seconds(TL.selA))
                                   + (tracks ? ", " + TL.selTracks.Count + (TL.selTracks.Count == 1 ? " track" : " tracks") : ", all tracks") : "";
            readout.text = string.IsNullOrEmpty(TL.readout) ? sText : (sText.Length > 0 ? sText + "   ·   " : "") + TL.readout;
            readout.tooltip = readout.text.Length > 0 ? readout.text : "The selection and the result of the last edit show here.";
        }

        // ─────────────────────────── overview ───────────────────────────

        bool overviewDrag; float overviewGrab;
        Rect OverviewLane => new Rect(TL.laneWorld.x - overview.worldBound.x, 0f, TL.laneWorld.width, OverviewHeight);
        float OvX(float t) { var l = OverviewLane; return l.x + t / Mathf.Max(1e-4f, TL.FitEnd) * l.width; }
        float OvT(float x) { var l = OverviewLane; return (x - l.x) / Mathf.Max(1f, l.width) * TL.FitEnd; }

        void PaintOverview(MeshGenerationContext ctx) {
            if (TL == null || TL.laneWorld.width < 2f) return;
            var p2 = ctx.painter2D; var l = OverviewLane;
            Fill(p2, l.x, 0, l.width, OverviewHeight, new Color(0f, 0f, 0f, 0.3f));
            int n = Mathf.Max(1, TL.tracks.Count);
            float rowH = Mathf.Max(1f, (OverviewHeight - 2f) / n);
            for (int i = 0; i < TL.tracks.Count; i++) {
                var p = TL.tracks[i]; if (!p.found) continue;
                Fill(p2, OvX(p.start), 1f + i * rowH, Mathf.Max(1f, OvX(p.End) - OvX(p.start)), Mathf.Max(1f, rowH - 0.5f), new Color(0.55f, 0.7f, 1f, 0.55f));
            }
            float a = Mathf.Max(l.x, OvX(TL.t0)), b = Mathf.Min(l.xMax, OvX(TL.t1));
            Fill(p2, a, 0, Mathf.Max(2f, b - a), OverviewHeight, new Color(1f, 1f, 1f, 0.16f));
            Stroke(p2, a, 0, Mathf.Max(2f, b - a), OverviewHeight, new Color(1f, 1f, 1f, 0.7f));
            if (TL.cursor >= 0f) Fill(p2, OvX(TL.cursor), 0, 1f, OverviewHeight, Color.white);
        }

        void OverviewDown(PointerDownEvent e) {
            if (e.button != 0 || TL == null || !TL.CanMoveView) return;
            float t = OvT(e.localPosition.x);
            if (t < TL.t0 || t > TL.t1) { float s = TL.Span; TL.t0 = t - s * 0.5f; TL.t1 = TL.t0 + s; TL.fitted = false; TL.Changed(); }
            overviewDrag = true; overviewGrab = t - TL.t0;
            overview.CapturePointer(e.pointerId); win.OnViewChanged(); e.StopPropagation();
        }

        void OverviewMove(PointerMoveEvent e) {
            if (!overviewDrag || !overview.HasPointerCapture(e.pointerId)) { overviewDrag = false; return; }
            float s = TL.Span; TL.t0 = OvT(e.localPosition.x) - overviewGrab; TL.t1 = TL.t0 + s; TL.fitted = false; TL.Changed();
            win.OnViewChanged();
        }

        // ─────────────────────────── ruler ───────────────────────────

        bool rulerDrag; float rulerDown;
        float RulerLaneX => TL.laneWorld.x - ruler.worldBound.x;
        float RulerX(float t) => RulerLaneX + (t - TL.t0) / TL.Span * TL.laneWorld.width;
        float RulerTime(float x) => TL.t0 + (x - RulerLaneX) / Mathf.Max(1f, TL.laneWorld.width) * TL.Span;

        void RulerDown(PointerDownEvent e) {
            if (e.button != 0 || TL == null) return;
            rulerDown = RulerTime(e.localPosition.x);
            TL.selTracks.Clear(); TL.hasSel = true; TL.selA = TL.selB = rulerDown; TL.Changed();
            rulerDrag = true; ruler.CapturePointer(e.pointerId); win.OnTimelineChanged(); e.StopPropagation();
        }

        void RulerMove(PointerMoveEvent e) {
            if (!rulerDrag || !ruler.HasPointerCapture(e.pointerId)) return;
            float t = RulerTime(e.localPosition.x);
            TL.selA = Mathf.Min(rulerDown, t); TL.selB = Mathf.Max(rulerDown, t); TL.Changed();
            win.OnViewChanged();
        }

        static readonly float[] Steps = { 0.001f, 0.002f, 0.005f, 0.01f, 0.02f, 0.05f, 0.1f, 0.2f, 0.5f, 1f, 2f, 5f, 10f, 20f, 30f, 60f, 120f, 300f };

        void PaintRuler(MeshGenerationContext ctx) {
            if (TL == null || TL.laneWorld.width < 2f) return;
            var p2 = ctx.painter2D;
            float lx = RulerLaneX, lw = TL.laneWorld.width;
            Fill(p2, lx, 0, lw, RulerHeight, new Color(0f, 0f, 0f, 0.22f));
            float ax = RulerX(TL.authored);
            if (ax < lx + lw) Fill(p2, Mathf.Max(lx, ax), 0, lx + lw - Mathf.Max(lx, ax), RulerHeight, new Color(0f, 0f, 0f, 0.3f));
            float step = RulerStep(), minor = step / 5f;
            float first = Mathf.Floor(TL.t0 / minor) * minor;
            p2.strokeColor = new Color(1f, 1f, 1f, 0.45f); p2.lineWidth = 1f;
            p2.BeginPath();
            for (float t = first; t <= TL.t1 + minor * 0.5f; t += minor) {
                float x = RulerX(t);
                if (x < lx || x > lx + lw) continue;
                bool major = Mathf.Abs(t / step - Mathf.Round(t / step)) < 1e-3f;
                p2.MoveTo(new Vector2(x, major ? 4f : 10f)); p2.LineTo(new Vector2(x, RulerHeight));
            }
            p2.Stroke();
            if (TL.hasSel) {
                float a = RulerX(TL.selA), b = RulerX(TL.selB);
                Fill(p2, a, 0, Mathf.Max(1f, b - a), RulerHeight, new Color(0.35f, 0.62f, 1f, 0.35f));
                Fill(p2, a - 0.5f, 0, 1f, RulerHeight, new Color(0.5f, 0.75f, 1f, 1f));
            }
            if (TL.cursor >= 0f) { float cx = RulerX(TL.cursor); if (cx >= lx && cx <= lx + lw) Fill(p2, cx, 0, 1f, RulerHeight, Color.white); }
        }

        float RulerStep() {
            foreach (var s in Steps) if (s / TL.SecondsPerPixel >= 70f) return s;
            return Steps[Steps.Length - 1];
        }

        /// <summary>The seconds printed on the ruler: plain labels, placed here and never while the ruler paints.</summary>
        void PlaceTickLabels() {
            int li = 0;
            if (TL != null && TL.laneWorld.width >= 2f) {
                float step = RulerStep(), lx = RulerLaneX, lw = TL.laneWorld.width;
                for (float t = Mathf.Floor(TL.t0 / step) * step; t <= TL.t1; t += step) {
                    float x = RulerX(t);
                    if (x < lx || x > lx + lw - 16f) continue;
                    if (li >= tickLabels.Count) {
                        var lab = new Label { pickingMode = PickingMode.Ignore };
                        lab.AddToClassList("zs-lbl"); lab.AddToClassList("zs-timeline-header__tick-label");
                        ruler.Add(lab); tickLabels.Add(lab);
                    }
                    var l = tickLabels[li++];
                    l.style.display = DisplayStyle.Flex;
                    string txt = step >= 1f ? t.ToString("0") : step >= 0.1f ? t.ToString("0.0") : step >= 0.01f ? t.ToString("0.00") : t.ToString("0.000");
                    if (l.text != txt) l.text = txt;
                    l.style.left = x + 2f;
                }
            }
            for (int i = li; i < tickLabels.Count; i++) tickLabels[i].style.display = DisplayStyle.None;
        }

        public void Repaint() { PlaceTickLabels(); overview.MarkDirtyRepaint(); ruler.MarkDirtyRepaint(); }

        static void Fill(Painter2D p2, float x, float y, float w, float h, Color c) {
            if (w <= 0f || h <= 0f) return;
            p2.fillColor = c; p2.BeginPath();
            p2.MoveTo(new Vector2(x, y)); p2.LineTo(new Vector2(x + w, y)); p2.LineTo(new Vector2(x + w, y + h)); p2.LineTo(new Vector2(x, y + h));
            p2.ClosePath(); p2.Fill();
        }

        static void Stroke(Painter2D p2, float x, float y, float w, float h, Color c) {
            p2.strokeColor = c; p2.lineWidth = 1f; p2.BeginPath();
            p2.MoveTo(new Vector2(x + 0.5f, y + 0.5f)); p2.LineTo(new Vector2(x + w - 0.5f, y + 0.5f)); p2.LineTo(new Vector2(x + w - 0.5f, y + h - 0.5f)); p2.LineTo(new Vector2(x + 0.5f, y + h - 0.5f));
            p2.ClosePath(); p2.Stroke();
        }
    }
}
