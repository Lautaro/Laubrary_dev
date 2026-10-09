using System;
using System.Collections.Generic;
using Laubrary.Zounds.Dsp;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Laubrary.Zui;
using Curve = Laubrary.Zounds.AudioSpectrumView.Curve;
using TrimDrag = Laubrary.Zounds.AudioSpectrumView.TrimDrag;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// One track's lane in the Zequence window (T-0560..T-0566): the piece where it sounds on the shared time window, and
    /// the timeline's gestures on it.
    ///
    /// A Klip piece is drawn by the waveform surface (<see cref="WaveSurfaceTK"/>), the same component the Klip editor
    /// uses, so the sound looks and edits the same in both: its file (the parts the track does not play dimmed), its own
    /// Volume, Pitch, Time and Gain curves (the one being edited with its points, the others as backdrops), the trim edges (a
    /// right-drag on one moves both: the same part of the file, moved along), its playheads, a right-click playing only
    /// this track from the clicked second. This strip is that surface's host: it draws the file where it SOUNDS on the
    /// timeline (through the sound's pitch and time curves), and keeps the timeline's own: the move strip along the top,
    /// the selection and the shared cursor, the chain's tail and a pitch range's spread, out-of-view marks, a nested
    /// Zequence's block, and the track's own volume curve (dashed).
    ///
    /// Gestures of the timeline (a press the surface did not take): a click plays this track from its start (owner,
    /// 2026-10-08; not while one of its curves is being edited, when a click is for the curve); a drag selects a time range
    /// (Shift adds this track to the selection), the thin strip along the top moves the piece, Alt-drag slips which part
    /// of the source it plays, a double click selects the whole piece. Ctrl+wheel zooms around the pointer; the wheel and
    /// Shift+wheel pan; the middle button drags the view.
    /// </summary>
    internal sealed class TrackStripTK : VisualElement, IWaveSurfaceHost {

        readonly ZequenceEditorWindowTK win;
        readonly CompositeZound.ZoundEntry entry;
        readonly Label leftMark, rightMark, warn;
        EnvelopeTK trackCurve;
        WaveSurfaceTK surface;

        /// <summary>The card's height, read and set by the surface's grip (set by the card).</summary>
        internal Func<float> cardHeight;
        internal Action<float> setCardHeight;
        internal Action heightDone;

        public const float TopBar = 7f;
        const float ClickSlop = 3f;

        ZequenceTimeline TL => win.timeline;
        TrackPlacement P => TL != null && TL.byEntry.TryGetValue(entry, out var p) ? p : null;

        enum Drag { None, Select, Move, Slip, Pan }
        Drag drag;
        TrackPlacement dragP;
        float downTime, start0, exA0, exB0, panT0;
        Vector2 downLocal;
        Vector2 hover = new Vector2(-1f, -1f);
        bool moved;
        TrimDrag trimKind;

        static ZoundsProject.ProjectSettings.EditorStyle Es => ZoundsProject.Instance.projectSettings.editorStyle;

        const string LaneTip = "Click to play this track from its start. "
                    + "Drag to select a part (Shift adds this track to the selection; double-click selects the whole piece). "
                    + "Drag the thin strip along the top to move the piece, Alt-drag to slip which part of the sound it plays. "
                    + "Ctrl+wheel zooms, the wheel pans. Keys: Space auditions, T trims to the selection, S splits, Delete deletes, Ctrl+C/X/V.";

        public TrackStripTK(ZequenceEditorWindowTK win, CompositeZound.ZoundEntry entry) {
            this.win = win; this.entry = entry;
            AddToClassList("zs-track-strip");
            focusable = true;
            tooltip = LaneTip;
            generateVisualContent += Paint;
            leftMark = Mark(); rightMark = Mark();
            leftMark.RegisterCallback<PointerDownEvent>(e => { if (e.clickCount == 2) JumpToPiece(); e.StopPropagation(); });
            rightMark.RegisterCallback<PointerDownEvent>(e => { if (e.clickCount == 2) JumpToPiece(); e.StopPropagation(); });
            warn = new Label("⚠") { pickingMode = PickingMode.Position, tooltip = "This track was sounding across the point where a ripple edit moved the tracks after it, so it stayed where it was. Split it or move it yourself if it should follow." };
            warn.AddToClassList("zs-lbl");
            warn.AddToClassList("zs-track-strip__warn");
            warn.style.display = DisplayStyle.None;
            Add(warn);

            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerLeaveEvent>(_ => { hover = new Vector2(-1f, -1f); MarkDirtyRepaint(); });
            RegisterCallback<WheelEvent>(OnWheel);
        }

        Label Mark() {
            var l = new Label { pickingMode = PickingMode.Position };
            l.AddToClassList("zs-lbl"); l.AddToClassList("zs-greymini");
            l.AddToClassList("zs-track-strip__mark");
            l.style.display = DisplayStyle.None;
            Add(l);
            return l;
        }

        // ─────────────────────────── geometry ───────────────────────────

        float W => contentRect.width;
        float H => contentRect.height;
        float X(float t) => (t - TL.t0) / TL.Span * W;
        float T(float x) => TL.t0 + x / Mathf.Max(1f, W) * TL.Span;

        /// <summary>The waveform surface of a Klip piece (null for a nested Zequence), for the window's checks.</summary>
        internal WaveSurfaceTK Surface => surface;

        /// <summary>Whether the pitch curve is still on its old scale (the curve bar's warning).</summary>
        internal bool PitchOldScale => surface != null && surface.PitchOldScale;

        /// <summary>Called by the window every frame it ticks: keep to the shared lane, refresh marks, repaint.</summary>
        public void Sync() {
            if (TL == null || parent == null) return;
            var lane = TL.laneWorld;
            if (lane.width > 1f) {
                float left = lane.x - parent.worldBound.x;
                if (!Mathf.Approximately(resolvedStyle.left, left)) style.left = left;
                if (!Mathf.Approximately(resolvedStyle.width, lane.width)) style.width = lane.width;
            }
            SyncSurface();
            SyncMarks();
            SyncTrackCurve();
            MarkDirtyRepaint();
        }

        void SyncSurface() {
            var p = P;
            bool klip = p != null && p.found && p.klip != null;
            if (klip && surface == null) {
                surface = new WaveSurfaceTK(this, framed: false);
                surface.AddToClassList("zs-wave-surface--lane");
                surface.style.top = TopBar;
                // Under the strip's own labels (the out-of-view marks, the ripple warning).
                Insert(0, surface);
                surface.marks.generateVisualContent += PaintMarks;
                if (trackCurve != null) surface.overlay.Add(trackCurve);
            }
            if (surface == null) return;
            surface.style.display = klip ? DisplayStyle.Flex : DisplayStyle.None;
            if (!klip) return;
            surface.Refresh();
            surface.marks.MarkDirtyRepaint();
        }

        void SyncMarks() {
            var p = P;
            leftMark.style.display = rightMark.style.display = DisplayStyle.None;
            warn.style.display = TL.straddling.Contains(entry) && p != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (p == null || !p.found) return;
            if (warn.style.display == DisplayStyle.Flex) { warn.style.left = Mathf.Clamp(X(p.start) + 2f, 0f, W - 12f); warn.style.top = TopBar; }
            // The piece is out of view: say which way and how far, so an empty lane is never mistaken for a silent track.
            if (p.End < TL.t0) {
                leftMark.text = "◂ ends " + (TL.t0 - p.End).ToString("0.0#") + " s before";
                leftMark.tooltip = "This track's audio is to the left of the view. Double-click to show it.";
                leftMark.style.display = DisplayStyle.Flex; leftMark.style.left = 2;
            }
            else if (p.start > TL.t1) {
                rightMark.text = "starts " + (p.start - TL.t1).ToString("0.0#") + " s after ▸";
                rightMark.tooltip = "This track's audio is to the right of the view. Double-click to show it.";
                rightMark.style.display = DisplayStyle.Flex; rightMark.style.right = 2; rightMark.style.left = StyleKeyword.Auto;
            }
            // A playhead out of view: where it is, and how soon it arrives.
            foreach (var h in Heads(p)) {
                if (h < TL.t0) {
                    leftMark.text = "◂ playhead, in " + (TL.t0 - h).ToString("0.0") + " s";
                    leftMark.tooltip = "A play of this track is sounding to the left of the view and reaches it in about this long.";
                    leftMark.style.display = DisplayStyle.Flex; leftMark.style.left = 2;
                }
                else if (h > TL.t1) {
                    rightMark.text = "playhead " + (h - TL.t1).ToString("0.0") + " s on ▸";
                    rightMark.tooltip = "A play of this track is sounding to the right of the view, this far past its edge.";
                    rightMark.style.display = DisplayStyle.Flex; rightMark.style.right = 2; rightMark.style.left = StyleKeyword.Auto;
                }
            }
        }

        void JumpToPiece() {
            var p = P; if (p == null || !TL.CanMoveView) return;
            float span = TL.Span;
            float c = 0.5f * (p.start + Mathf.Min(p.End, p.start + span * 0.9f));
            TL.t0 = c - span * 0.5f; TL.t1 = TL.t0 + span; TL.fitted = false; TL.Changed();
        }

        // ─────────────────────────── the sound's own curves (T-0566) ───────────────────────────

        static int ParamOf(Curve which) => which == Curve.Volume ? SourceStageParam.Volume : which == Curve.Pitch ? SourceStageParam.Pitch : which == Curve.Gain ? SourceStageParam.Gain : SourceStageParam.Speed;

        /// <summary>The sound's own curve on <paramref name="which"/>'s value when it follows the waveform, else null.</summary>
        static ZoundModifier OwnCurve(Klip k, Curve which) {
            if (k == null) return null;
            var o = KlipChainEnvelopes.Find(k, ParamOf(which));
            return o.Valid && o.mod.curve != null && CurveAnchor.FollowsWaveform(o.mod) ? o.mod : null;
        }

        /// <summary>The curve being edited on this track (one at a time, chosen on the card's curve bar; none = the timeline's gestures).</summary>
        ZoundModifier Editing => TL.editingCurve.TryGetValue(entry, out var m) ? m : null;

        /// <summary>Which source-stage parameter's curve is being edited here, or -1.</summary>
        public int EditingParam {
            get {
                var ed = Editing; var k = P?.klip;
                if (ed == null || k == null) return -1;
                foreach (Curve c in new[] { Curve.Volume, Curve.Pitch, Curve.Time, Curve.Gain }) if (ReferenceEquals(OwnCurve(k, c), ed)) return ParamOf(c);
                return -1;
            }
        }

        /// <summary>Select the curve bound to <paramref name="param"/> for editing on this track, or -1 for none.</summary>
        public void SetEditing(int param) {
            var k = P?.klip;
            Curve which = param == SourceStageParam.Volume ? Curve.Volume : param == SourceStageParam.Pitch ? Curve.Pitch : param == SourceStageParam.Gain ? Curve.Gain : Curve.Time;
            var mod = param < 0 ? null : OwnCurve(k, which);
            if (mod == null) TL.editingCurve.Remove(entry); else TL.editingCurve[entry] = mod;
            Sync();
        }

        /// <summary>Curve x (the curve's 0..1) at Zequence time <paramref name="t"/>, for a curve on this piece.</summary>
        static float CurveXAt(TrackPlacement p, ZoundModifier m, float t, in CurveAnchor.Axis own) {
            if (m.curveAnchor == CurveAnchor.Source) {
                float total = own.sourceLength + Mathf.Max(0f, m.Param(0));
                float s = t <= p.End ? p.TimeToSource(t) : own.sourceLength + (t - p.End) * p.pitch;
                return total > 0f ? s / total : 0f;
            }
            return CurveAnchor.XAtSourceSeconds(m, p.TimeToSource(t), own);
        }

        /// <summary>
        /// The Zequence time a curve x is drawn at: the inverse of <see cref="CurveXAt"/>. The part of the file after the
        /// piece's end is not heard (the tail after the end is the curve's extra time), so it folds onto the end, which keeps
        /// the mapping monotonic.
        /// </summary>
        static float TimeAtCurveX(TrackPlacement p, ZoundModifier m, float x, in CurveAnchor.Axis own) {
            float extra = Mathf.Max(0f, m.Param(0));
            if (m.curveAnchor == CurveAnchor.Source) {
                float s = x * (own.sourceLength + extra);
                if (s > own.sourceLength) return p.End + (s - own.sourceLength) / Mathf.Max(p.pitch, 0.01f);
                if (s >= p.exB) return p.End;
                return p.SourceToTime(s);
            }
            // Still on "a fraction of the sound's own trim" (an older sound, until its first edit): the audio part, then the extra time.
            float len = own.TrimLength, r = len + extra > 0f ? len / (len + extra) : 1f;
            if (x <= r) return p.SourceToTime(own.trimStart + (r > 0f ? x / r : 0f) * len);
            return p.End + (x - r) * (len + extra) / Mathf.Max(p.pitch, 0.01f);
        }

        void SyncTrackCurve() {
            // The track's own volume curve (the timeline's, not the sound's): over the piece, dashed, editable in place.
            var p = P;
            bool show = p != null && p.found && entry.volumeEnvelope != null && entry.volumeEnvelope.enabled;
            if (!show) { if (trackCurve != null) trackCurve.style.display = DisplayStyle.None; return; }
            if (trackCurve == null) {
                trackCurve = new EnvelopeTK(entry.volumeEnvelope.DeepCopy(), Es.volumeEnvelopeColor) { thickness = Es.volumeEnvelopeThickness, dashed = true, pointsAndLineOnly = true };
                trackCurve.AddToClassList("zs-track-strip__curve");
                trackCurve.tooltip = "This track's own volume curve (dashed): it belongs to the timeline, so it stays where it is when the piece moves.";
                trackCurve.onChanged = () => win.Modify("modify entry volume envelope", () => { entry.volumeEnvelope = trackCurve.envelope.DeepCopy(); entry.volumeEnvelope.enabled = true; });
            }
            else if (trackCurve.panel?.focusController?.focusedElement != trackCurve) trackCurve.envelope = entry.volumeEnvelope.DeepCopy();
            // Over the waveform surface (so a press beside its line still reaches the surface), or the lane for a block.
            var host = surface != null && surface.resolvedStyle.display != DisplayStyle.None ? surface.overlay : (VisualElement)this;
            if (trackCurve.parent != host) { if (host == this) Insert(0, trackCurve); else host.Add(trackCurve); }
            float top = host == this ? TopBar : 0f;
            trackCurve.style.display = DisplayStyle.Flex;
            // It spans the whole play; the lane clips it when the play runs off an edge.
            float x0 = X(p.start), x1 = X(p.End);
            trackCurve.style.left = x0; trackCurve.style.width = Mathf.Max(1f, x1 - x0);
            trackCurve.style.top = top; trackCurve.style.height = Mathf.Max(4f, H - TopBar);
            // While one of the sound's curves is being edited here, the track curve steps aside.
            trackCurve.pickingMode = Editing != null ? PickingMode.Ignore : PickingMode.Position;
            trackCurve.backdrop = Editing != null;
        }

        // ─────────────────────────── drawing ───────────────────────────

        IEnumerable<float> Heads(TrackPlacement p) {
            if (p?.klip == null) yield break;
            var tokens = win.TokensPlaying(entry);
            foreach (var t in tokens) {
                if (t?.audioSource == null || !(t.audioSource.generator is ZoundSapVoiceGenerator g)) continue;
                int n = g.ReadSourcePositions(headBuf, headW);
                for (int i = 0; i < n; i++) yield return p.SourceToTime((float)headBuf[i]);
            }
        }
        static readonly double[] headBuf = new double[16];
        static readonly float[] headW = new float[16];

        bool SurfaceShown => surface != null && surface.resolvedStyle.display != DisplayStyle.None;

        void Paint(MeshGenerationContext ctx) {
            if (TL == null || W < 2f || H < 2f) return;
            var p2 = ctx.painter2D;
            var p = P;
            Rect(p2, 0, 0, W, H, Es.trackLaneColor);
            // Where the authored duration ends.
            float ax = X(TL.authored);
            if (ax > 0 && ax < W) Rect(p2, ax, 0, W - ax, H, new Color(0f, 0f, 0f, 0.18f));
            if (p != null && p.found) {
                float x0 = X(p.start), x1 = X(p.End);
                if (p.klip == null) Rect(p2, x0, TopBar, x1 - x0, H - TopBar, new Color(0.5f, 0.5f, 0.6f, 0.25f));
                // The move strip along the top.
                Rect(p2, x0, 0, x1 - x0, TopBar, MoveBarColour(x0, x1));
            }
            // The selection and the cursor; under a Klip piece they are drawn again over its waveform (PaintMarks).
            PaintSelection(p2, 0f, H, float.NegativeInfinity, float.PositiveInfinity);
        }

        /// <summary>The timeline's marks over a Klip piece's waveform (the surface's marks layer, under its playheads and
        /// curves): the chain's tail, a pitch range's spread, the selection, the shared cursor, "continues" arrows.</summary>
        void PaintMarks(MeshGenerationContext ctx) {
            var p = P;
            if (TL == null || p == null || !p.found || p.klip == null) return;
            var p2 = ctx.painter2D;
            float h = H - TopBar, mid = h * 0.5f;
            float x0 = X(p.start), x1 = X(p.End);
            // The chain's tail: still sounding after the source runs out.
            if (p.tail > 0f) {
                float tx = X(p.End + p.tail);
                Rect(p2, x1, 0f, tx - x1, h, new Color(0.55f, 0.75f, 1f, 0.10f));
                Rect(p2, x1, mid - 0.5f, tx - x1, 1f, new Color(0.55f, 0.75f, 1f, 0.35f));
            }
            // A per-play pitch range: where the piece can end, as a band along the bottom.
            if (p.pitchHi > p.pitchLo + 1e-4f) {
                float len = p.PlayLength * p.pitch;
                float ea = X(p.start + len / p.pitchHi), eb = X(p.start + len / p.pitchLo);
                Rect(p2, ea, h - 4f, eb - ea, 4f, new Color(1f, 0.8f, 0.3f, 0.45f));
            }
            // Only over the file (outside it the lane's own drawing shows through the surface).
            PaintSelection(p2, 0f, h, X(p.SourceToTime(0f)), X(p.SourceToTime(p.fileLen)));
            // A piece running off an edge of the view: "continues".
            if (x0 < 0f && x1 > 0f) Tri(p2, 2f, mid, -1f, new Color(1f, 1f, 1f, 0.5f));
            if (x1 > W && x0 < W) Tri(p2, W - 2f, mid, 1f, new Color(1f, 1f, 1f, 0.5f));
        }

        void PaintSelection(Painter2D p2, float y, float h, float lo, float hi) {
            // The selection's time range over the piece, strong on selected tracks, faint on the others.
            if (TL.hasSel) {
                bool mine = TL.selTracks.Contains(entry);
                float ra = X(TL.selA), rb = X(TL.selB);
                float a = Mathf.Max(ra, lo), b = Mathf.Min(rb, hi);
                if (b >= a) Rect(p2, a, y, Mathf.Max(1f, b - a), h, mine ? new Color(0.35f, 0.62f, 1f, 0.30f) : new Color(0.35f, 0.62f, 1f, 0.07f));
                var edge = new Color(0.5f, 0.75f, 1f, 0.9f);
                if (mine && ra >= lo && ra <= hi) Rect(p2, ra, y, 1f, h, edge);
                if (mine && rb >= lo && rb <= hi) Rect(p2, rb - 1f, y, 1f, h, edge);
            }
            if (TL.cursor >= 0f) { float cx = X(TL.cursor); if (cx >= Mathf.Max(0f, lo) && cx <= Mathf.Min(W, hi)) Rect(p2, cx, y, 1f, h, new Color(1f, 1f, 1f, 0.55f)); }
        }

        Color MoveBarColour(float x0, float x1) {
            bool hot = drag == Drag.Move || (drag == Drag.None && hover.y >= 0f && hover.y < TopBar && hover.x >= x0 && hover.x <= x1);
            return hot ? new Color(1f, 1f, 1f, 0.45f) : new Color(1f, 1f, 1f, 0.18f);
        }

        // ─────────────────────────── the timeline's gestures ───────────────────────────

        void OnDown(PointerDownEvent e) {
            var p = P;
            if (TL == null) return;
            Focus();
            var l = (Vector2)e.localPosition;
            downLocal = l; downTime = T(l.x); moved = false;
            if (e.button == 2) { drag = Drag.Pan; panT0 = TL.t0; this.CapturePointer(e.pointerId); e.StopPropagation(); return; }
            if (p == null || !p.found) return;
            // A right-click on a Klip's waveform is the surface's (play from there); on a nested Zequence's block, the same.
            if (e.button == 1) {
                if (p.klip == null && l.y >= TopBar) { win.PlayTrackFrom(entry, float.NaN); e.StopPropagation(); }
                return;
            }
            if (e.button != 0) return;
            float x0 = X(p.start), x1 = X(p.End);
            dragP = p; start0 = p.start; exA0 = p.exA; exB0 = p.exB;
            if (l.y < TopBar && l.x >= x0 && l.x <= x1) Begin(Drag.Move, "move piece", e);
            else if (e.altKey && p.klip != null && l.x >= x0 && l.x <= x1) Begin(Drag.Slip, "slip piece", e);
            else if (e.clickCount == 2 && l.x >= x0 && l.x <= x1) {
                TL.Select(p.start, p.End, entry, e.shiftKey);
                win.OnTimelineChanged();
            }
            else if (e.shiftKey && TL.hasSel && !TL.selTracks.Contains(entry)) {
                // Shift-click another track: add it to the selection, keeping the same time range.
                TL.selTracks.Add(entry); TL.Changed(); win.OnTimelineChanged();
            }
            else { drag = Drag.Select; TL.Select(downTime, downTime, entry, e.shiftKey); this.CapturePointer(e.pointerId); }
            e.StopPropagation();
        }

        void Begin(Drag d, string undo, PointerDownEvent e) {
            drag = d;
            ZoundsWindow.BeginDragUndo(undo);
            // A slip keeps the sound's curves on its audio from the first change (T-0501).
            if (d != Drag.Move && dragP.klip != null) KlipChainEnvelopes.EnsureSourceAnchored(dragP.klip);
            this.CapturePointer(e.pointerId);
        }

        void OnMove(PointerMoveEvent e) {
            var l = (Vector2)e.localPosition;
            hover = l;
            if (drag == Drag.None) { MarkDirtyRepaint(); return; }
            if (!this.HasPointerCapture(e.pointerId)) return;
            if ((l - downLocal).magnitude > ClickSlop) moved = true;
            float t = T(l.x);
            switch (drag) {
                case Drag.Pan: {
                    if (!TL.CanMoveView) break;
                    float dt = (l.x - downLocal.x) / Mathf.Max(1f, W) * TL.Span;
                    float span = TL.Span; TL.t0 = panT0 - dt; TL.t1 = TL.t0 + span; TL.fitted = false; TL.Changed();
                    break;
                }
                case Drag.Select: if (moved) { TL.selA = Mathf.Min(downTime, t); TL.selB = Mathf.Max(downTime, t); TL.Changed(); } break;
                case Drag.Move: TimelineEdits.Move(dragP, start0, t - downTime); win.OnTimelineChanged(); break;
                case Drag.Slip: TimelineEdits.Slip(dragP, exA0, exB0, -(t - downTime) * dragP.pitch); win.OnTimelineChanged(); break;
            }
            e.StopPropagation();
        }

        void OnUp(PointerUpEvent e) {
            if (drag == Drag.None) return;
            var d = drag; drag = Drag.None;
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
            if (d == Drag.Move || d == Drag.Slip) ZoundsWindow.EndDragUndo();
            if (d == Drag.Select && !moved) {
                // A click: a moment, not a range (split and paste use it), and the track plays from its start.
                TL.selA = TL.selB = downTime; TL.Changed();
                if (e.clickCount < 2) win.PlayTrackFrom(entry, float.NaN);
            }
            win.OnTimelineChanged();
            e.StopPropagation();
        }

        void OnWheel(WheelEvent e) {
            // Under auto-tidy the view does not move: the wheel scrolls the track list as anywhere else in the window.
            if (TL == null || !TL.CanMoveView) return;
            float t = T(e.localMousePosition.x);
            if (e.ctrlKey || e.commandKey) TL.ZoomAround(t, e.delta.y > 0f ? 1.18f : 1f / 1.18f);
            else TL.Pan((e.delta.y + e.delta.x) * 0.06f * TL.Span);
            win.OnViewChanged();
            e.StopPropagation();
        }

        // ─────────────────────────── the waveform surface's host ───────────────────────────

        Klip IWaveSurfaceHost.Sound => P?.klip;
        AudioClip IWaveSurfaceHost.Source => P != null && P.found ? P.sourceClip : null;
        float IWaveSurfaceHost.FileLength => P != null ? P.fileLen : 0f;
        // The surface sits under the move strip, full width: its x is the lane's x.
        float IWaveSurfaceHost.XOf(float s, Rect r) => X(P.SourceToTime(s));
        float IWaveSurfaceHost.SourceAt(float x, Rect r) => P.TimeToSource(T(x));
        bool IWaveSurfaceHost.Heard(out float from, out float to) { var p = P; from = p != null ? p.exA : 0f; to = p != null ? p.exB : 0f; return p != null; }
        void IWaveSurfaceHost.TrimHandlesLive(out bool start, out bool end) { start = end = true; }
        // Only a local sound's own trim can be random here: a track's excerpt has edges of its own, and a shared sound's
        // trim is never changed from a Zequence.
        bool IWaveSurfaceHost.TrimRandomEditable { get { var p = P; return p != null && p.klip != null && p.entry.local && !p.ownExcerpt && p.klip.trimEnabled; } }

        bool IWaveSurfaceHost.BeginTrim(TrimDrag which, float x, Rect r) {
            var p = P;
            if (p?.klip == null) return false;
            dragP = p; start0 = p.start; exA0 = p.exA; exB0 = p.exB; downTime = T(x); trimKind = which;
            ZoundsWindow.BeginDragUndo(which == TrimDrag.Both ? "move trim" : "trim piece");
            // A trim keeps the sound's curves on its audio from the first change (T-0501).
            KlipChainEnvelopes.EnsureSourceAnchored(p.klip);
            return true;
        }

        void IWaveSurfaceHost.DragTrim(float x, Rect r) {
            if (dragP == null) return;
            float t = T(x);
            switch (trimKind) {
                case TrimDrag.Start: {
                    float a = Mathf.Clamp(dragP.TimeToSource(t), 0f, exB0 - 0.002f);
                    float at = dragP.SourceToTime(a);
                    TimelineEdits.SetExcerpt(dragP, a, exB0);
                    TimelineEdits.StartAt(dragP, at);
                    break;
                }
                case TrimDrag.End: {
                    float b = Mathf.Clamp(dragP.TimeToSource(t), exA0 + 0.002f, dragP.fileLen);
                    TimelineEdits.SetExcerpt(dragP, exA0, b);
                    break;
                }
                default: {
                    // Both edges together, as in the Klip editor: the same length of the file, further along it, and the
                    // piece moved with it so the audio stays where it was drawn.
                    float pitch = Mathf.Max(dragP.pitch, 0.01f);
                    float d = Mathf.Clamp((t - downTime) * pitch, -exA0, Mathf.Max(0f, dragP.fileLen - exB0));
                    TimelineEdits.Slip(dragP, exA0, exB0, d);
                    TimelineEdits.Move(dragP, start0, d / pitch);
                    break;
                }
            }
            win.OnTimelineChanged();
        }

        void IWaveSurfaceHost.EndTrim() { ZoundsWindow.EndDragUndo(); dragP = null; win.OnTimelineChanged(); }

        Envelope IWaveSurfaceHost.CurveOf(Curve which) => OwnCurve(P?.klip, which)?.curve;

        int IWaveSurfaceHost.SelectedCurve {
            get {
                var ed = Editing; var k = P?.klip;
                if (ed == null || k == null) return -1;
                for (int i = 0; i < WaveSurfaceTK.CurveCount; i++) if (ReferenceEquals(OwnCurve(k, (Curve)i), ed)) return i;
                return -1;
            }
        }

        void IWaveSurfaceHost.CurveDomain(Curve which, Envelope env, ZoundModifier mod, Rect r, out Rect rect, out float xMin, out float xMax,
                                          out Func<float, float> toX, out Func<float, float> fromX) {
            var p = P;
            rect = r;
            xMin = env.xMin; xMax = env.xMax;
            toX = null; fromX = null;
            if (p == null || mod == null) return;
            var own = CurveAnchor.Axis.Of(p.klip, p.fileLen);
            // Clamped to the trim (the sound's own switch): only over the part the track plays.
            if (p.klip.clampToTrim && p.klip.trimEnabled) { xMin = CurveXAt(p, mod, p.start, own); xMax = CurveXAt(p, mod, p.End, own); }
            // The curve lies on its audio, which the timeline draws where it sounds (not a straight line).
            toX = cx => X(TimeAtCurveX(p, mod, cx, own));
            fromX = x => CurveXAt(p, mod, T(x), own);
        }

        void IWaveSurfaceHost.BeginEditUndo(string name) => ZoundsWindow.BeginDragUndo(name);
        void IWaveSurfaceHost.EndEditUndo() { ZoundsWindow.EndDragUndo(); win.OnTimelineChanged(); }
        void IWaveSurfaceHost.SoundChanged() => win.OnTimelineChanged();
        // A track's curves are only editable once the card's curve bar has given a shared sound's track its own copy.
        bool IWaveSurfaceHost.BeforeEdit() => true;

        void IWaveSurfaceHost.Playheads(List<float> seconds, List<float> weights) {
            var p = P;
            if (p?.klip == null) return;
            foreach (var t in win.TokensPlaying(entry)) {
                if (t?.audioSource == null || !(t.audioSource.generator is ZoundSapVoiceGenerator g)) continue;
                int n = g.ReadSourcePositions(headBuf, headW);
                for (int i = 0; i < n; i++) { seconds.Add((float)headBuf[i]); weights.Add(headW[i]); }
            }
        }

        void IWaveSurfaceHost.PlayFrom(float s) => win.PlayTrackFrom(entry, s);
        // Every other press is the timeline's: it goes on to this strip.
        bool IWaveSurfaceHost.Press(PointerDownEvent e, Vector2 m, Rect r) => false;
        bool IWaveSurfaceHost.Move(Vector2 m, Rect r) => false;
        void IWaveSurfaceHost.Release() { }
        bool IWaveSurfaceHost.Key(KeyDownEvent e) => false;
        string IWaveSurfaceHost.ClickTip => LaneTip;

        float IWaveSurfaceHost.Height {
            get => cardHeight != null ? cardHeight() : H;
            set => setCardHeight?.Invoke(value);
        }
        void IWaveSurfaceHost.EndHeightDrag() => heightDone?.Invoke();
        Texture IWaveSurfaceHost.FallbackWave(int w, int h, out Rect uv) { uv = new Rect(0f, 0f, 1f, 1f); return null; }

        // ─────────────────────────── painter helpers ───────────────────────────

        static void Rect(Painter2D p2, float x, float y, float w, float h, Color c) {
            if (w <= 0f || h <= 0f) return;
            p2.fillColor = c;
            p2.BeginPath();
            p2.MoveTo(new Vector2(x, y)); p2.LineTo(new Vector2(x + w, y)); p2.LineTo(new Vector2(x + w, y + h)); p2.LineTo(new Vector2(x, y + h));
            p2.ClosePath(); p2.Fill();
        }

        static void Tri(Painter2D p2, float x, float y, float dir, Color c) {
            p2.fillColor = c; p2.BeginPath();
            p2.MoveTo(new Vector2(x, y)); p2.LineTo(new Vector2(x - dir * 6f, y - 5f)); p2.LineTo(new Vector2(x - dir * 6f, y + 5f));
            p2.ClosePath(); p2.Fill();
        }
    }
}
