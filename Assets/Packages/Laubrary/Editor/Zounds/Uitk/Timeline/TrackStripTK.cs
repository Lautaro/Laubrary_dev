using System.Collections.Generic;
using Laubrary.Zounds.Dsp;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Laubrary.Zui;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// One track's lane in the Zequence window (T-0560..T-0566): the piece drawn where and how it sounds on the shared
    /// time window, and the gestures that edit it.
    ///
    /// Drawn: the bright part of the source that plays; the rest of the source dimmed on both sides, at the place it would
    /// sound if the trim were widened; the chain's ringing tail as a band after the end; the spread of a per-play pitch
    /// range as a band at the end; the selection; the shared time cursor; one playhead per read head of every play of this
    /// track, where it is really reading its source; markers at the edges when the piece or a playhead is out of view;
    /// the sound's own Volume, Pitch and Time curves over its audio (the one being edited bright with its points, the
    /// others as backdrops), and the track's own volume curve dashed.
    ///
    /// Gestures: a click on the waveform plays this track from its start, a right-click plays it from the clicked moment
    /// (owner, 2026-10-08; not while one of its curves is being edited, when a click is for the curve); a drag selects a
    /// time range (Shift adds this track to the selection), the thin strip along the top moves the piece, an edge trims it,
    /// Alt-drag slips which part of the source it plays, a double click selects the whole piece. Ctrl+wheel zooms around
    /// the pointer; the wheel and Shift+wheel pan; the middle button drags the view.
    /// </summary>
    internal sealed class TrackStripTK : VisualElement {

        readonly ZequenceEditorWindowTK win;
        readonly CompositeZound.ZoundEntry entry;
        readonly Label leftMark, rightMark, warn;
        EnvelopeTK trackCurve;

        public const float TopBar = 7f;
        const float EdgeGrab = 4f;
        const float ClickSlop = 3f;

        ZequenceTimeline TL => win.timeline;
        TrackPlacement P => TL != null && TL.byEntry.TryGetValue(entry, out var p) ? p : null;

        enum Drag { None, Select, Move, TrimA, TrimB, Slip, Pan, Point }
        Drag drag;
        TrackPlacement dragP;
        float downTime, start0, exA0, exB0, panT0;
        Vector2 downLocal;
        Vector2 hover = new Vector2(-1f, -1f);
        int dragPoint = -1; ZoundModifier dragMod;
        bool moved;

        static ZoundsProject.ProjectSettings.EditorStyle Es => ZoundsProject.Instance.projectSettings.editorStyle;

        public TrackStripTK(ZequenceEditorWindowTK win, CompositeZound.ZoundEntry entry) {
            this.win = win; this.entry = entry;
            AddToClassList("zs-track-strip");
            focusable = true;
            tooltip = "Click to play this track from its start; right-click to play it from here. "
                    + "Drag on the waveform to select a part (Shift adds this track to the selection; double-click selects the whole piece). "
                    + "Drag the thin strip along the top to move the piece, an edge to trim it, Alt-drag to slip which part of the sound it plays. "
                    + "Ctrl+wheel zooms, the wheel pans. Keys: Space auditions, T trims to the selection, S splits, Delete deletes, Ctrl+C/X/V.";
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

        /// <summary>Called by the window every frame it ticks: keep to the shared lane, refresh marks, repaint.</summary>
        public void Sync() {
            if (TL == null || parent == null) return;
            var lane = TL.laneWorld;
            if (lane.width > 1f) {
                float left = lane.x - parent.worldBound.x;
                if (!Mathf.Approximately(resolvedStyle.left, left)) style.left = left;
                if (!Mathf.Approximately(resolvedStyle.width, lane.width)) style.width = lane.width;
            }
            SyncMarks();
            CollectOwnCurves(P);
            SyncTrackCurve();
            MarkDirtyRepaint();
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
            var p = P; if (p == null) return;
            float span = TL.Span;
            float c = 0.5f * (p.start + Mathf.Min(p.End, p.start + span * 0.9f));
            TL.t0 = c - span * 0.5f; TL.t1 = TL.t0 + span; TL.fitted = false; TL.Changed();
        }

        // ─────────────────────────── the sound's own curves (T-0566) ───────────────────────────

        public struct OwnCurve { public ZoundModifier mod; public int param; public Color colour; }
        readonly List<OwnCurve> ownCurves = new List<OwnCurve>();

        void CollectOwnCurves(TrackPlacement p) {
            ownCurves.Clear();
            if (p?.klip == null) return;
            var chain = ZoundDspPlayback.ResolveChain(p.klip, out _);
            if (chain == null) return;
            foreach (var b in chain.bindings) {
                if (b.nodeIndex != -1 || b.modifierIndex < 0 || b.modifierIndex >= chain.modifiers.Count) continue;
                if (b.paramIndex != SourceStageParam.Volume && b.paramIndex != SourceStageParam.Pitch && b.paramIndex != SourceStageParam.Speed) continue;
                var m = chain.modifiers[b.modifierIndex];
                if (!m.enabled || !CurveAnchor.FollowsWaveform(m) || m.curve == null) continue;
                if (ownCurves.Exists(o => ReferenceEquals(o.mod, m))) continue;
                ownCurves.Add(new OwnCurve {
                    mod = m, param = b.paramIndex,
                    colour = b.paramIndex == SourceStageParam.Volume ? Es.volumeEnvelopeColor : b.paramIndex == SourceStageParam.Pitch ? Es.pitchEnvelopeColor : AudioSpectrumView.TimeCurveColor,
                });
            }
        }

        /// <summary>The curve being edited on this track (one at a time, chosen on the card's curve bar; none = the waveform's gestures only).</summary>
        ZoundModifier Editing => TL.editingCurve.TryGetValue(entry, out var m) ? m : null;

        /// <summary>Which source-stage parameter's curve is being edited here, or -1.</summary>
        public int EditingParam {
            get {
                var ed = Editing;
                if (ed == null) return -1;
                foreach (var oc in ownCurves) if (ReferenceEquals(oc.mod, ed)) return oc.param;
                return -1;
            }
        }

        /// <summary>Select the curve bound to <paramref name="param"/> for editing on this track, or -1 for none.</summary>
        public void SetEditing(int param) {
            CollectOwnCurves(P);
            var oc = ownCurves.Find(o => o.param == param);
            if (param < 0 || oc.mod == null) TL.editingCurve.Remove(entry); else TL.editingCurve[entry] = oc.mod;
            Sync();
        }

        /// <summary>Curve x (the curve's 0..1) at Zequence time <paramref name="t"/>, and back, for a curve on this piece.</summary>
        static float CurveXAt(TrackPlacement p, ZoundModifier m, float t, in CurveAnchor.Axis own) {
            if (m.curveAnchor == CurveAnchor.Source) {
                float total = own.sourceLength + Mathf.Max(0f, m.Param(0));
                float s = t <= p.End ? p.TimeToSource(t) : own.sourceLength + (t - p.End) * p.pitch;
                return total > 0f ? s / total : 0f;
            }
            return CurveAnchor.XAtSourceSeconds(m, p.TimeToSource(t), own);
        }

        static float TimeAtCurveX(TrackPlacement p, ZoundModifier m, float x, in CurveAnchor.Axis own) {
            float extra = Mathf.Max(0f, m.Param(0));
            if (m.curveAnchor == CurveAnchor.Source) {
                float s = x * (own.sourceLength + extra);
                if (s > own.sourceLength) return p.End + (s - own.sourceLength) / Mathf.Max(p.pitch, 0.01f);
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
                Insert(0, trackCurve);
            }
            else if (trackCurve.panel?.focusController?.focusedElement != trackCurve) trackCurve.envelope = entry.volumeEnvelope.DeepCopy();
            trackCurve.style.display = DisplayStyle.Flex;
            // It spans the whole play; the lane clips it when the play runs off an edge.
            float x0 = X(p.start), x1 = X(p.End);
            trackCurve.style.left = x0; trackCurve.style.width = Mathf.Max(1f, x1 - x0);
            trackCurve.style.top = TopBar; trackCurve.style.height = Mathf.Max(4f, H - TopBar);
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

        void Paint(MeshGenerationContext ctx) {
            if (TL == null || W < 2f || H < 2f) return;
            var p2 = ctx.painter2D;
            var p = P;
            Rect(p2, 0, 0, W, H, Es.trackLaneColor);
            // Where the authored duration ends.
            float ax = X(TL.authored);
            if (ax > 0 && ax < W) Rect(p2, ax, 0, W - ax, H, new Color(0f, 0f, 0f, 0.18f));
            if (p != null && p.found) {
                if (p.klip != null) PaintKlip(p2, p);
                else PaintBlock(p2, p);
            }
            // The selection's time range over the piece, strong on selected tracks, faint on the others.
            if (TL.hasSel) {
                bool mine = TL.selTracks.Contains(entry);
                float a = X(TL.selA), b = X(TL.selB);
                Rect(p2, a, 0, Mathf.Max(1f, b - a), H, mine ? new Color(0.35f, 0.62f, 1f, 0.30f) : new Color(0.35f, 0.62f, 1f, 0.07f));
                if (mine) { Rect(p2, a, 0, 1f, H, new Color(0.5f, 0.75f, 1f, 0.9f)); Rect(p2, b - 1f, 0, 1f, H, new Color(0.5f, 0.75f, 1f, 0.9f)); }
            }
            if (TL.cursor >= 0f) { float cx = X(TL.cursor); if (cx >= 0f && cx <= W) Rect(p2, cx, 0, 1f, H, new Color(1f, 1f, 1f, 0.55f)); }
            if (p != null) foreach (var h in Heads(p)) { float hx = X(h); if (hx >= 0f && hx <= W) Rect(p2, hx - 0.75f, 0, 1.5f, H, Es.playerHeadColor); }
        }

        void PaintBlock(Painter2D p2, TrackPlacement p) {
            float x0 = X(p.start), x1 = X(p.End);
            Rect(p2, x0, TopBar, x1 - x0, H - TopBar, new Color(0.5f, 0.5f, 0.6f, 0.25f));
            Rect(p2, x0, 0, x1 - x0, TopBar, MoveBarColour(p, x0, x1));
        }

        Color MoveBarColour(TrackPlacement p, float x0, float x1) {
            bool hot = drag == Drag.Move || (drag == Drag.None && hover.y >= 0f && hover.y < TopBar && hover.x >= x0 && hover.x <= x1);
            return hot ? new Color(1f, 1f, 1f, 0.45f) : new Color(1f, 1f, 1f, 0.18f);
        }

        void PaintKlip(Painter2D p2, TrackPlacement p) {
            float top = TopBar, h = H - TopBar, mid = top + h * 0.5f;
            float x0 = X(p.start), x1 = X(p.End);
            // The whole source, at the place it would sound: dim before and after the piece, bright where it plays.
            float s0x = X(p.SourceToTime(0f)), s1x = X(p.SourceToTime(p.fileLen));
            Rect(p2, s0x, top, s1x - s0x, h, new Color(0.10f, 0.10f, 0.16f, 0.55f));
            Rect(p2, x0, top, x1 - x0, h, Es.klipWaveformBGColor);
            // The chain's tail: still sounding after the source runs out.
            if (p.tail > 0f) {
                float tx = X(p.End + p.tail);
                Rect(p2, x1, top, tx - x1, h, new Color(0.55f, 0.75f, 1f, 0.10f));
                Rect(p2, x1, mid - 0.5f, tx - x1, 1f, new Color(0.55f, 0.75f, 1f, 0.35f));
            }
            // A per-play pitch range: where the piece can end, as a band along the bottom.
            if (p.pitchHi > p.pitchLo + 1e-4f) {
                float len = p.PlayLength * p.pitch;
                float ea = X(p.start + len / p.pitchHi), eb = X(p.start + len / p.pitchLo);
                Rect(p2, ea, H - 4f, eb - ea, 4f, new Color(1f, 0.8f, 0.3f, 0.45f));
            }
            // The waveform, one lowest-to-highest stroke per pixel column, bright over the part that plays.
            var sum = WaveSummary.For(p.sourceClip);
            if (sum != null) {
                float peak = Mathf.Max(sum.Peak, 1e-4f);
                float lo = Mathf.Max(0f, s0x), hi = Mathf.Min(W, s1x);
                var bright = Es.waveformColor; var dim = new Color(bright.r, bright.g, bright.b, bright.a * 0.32f);
                for (int pass = 0; pass < 2; pass++) {
                    p2.strokeColor = pass == 0 ? dim : bright; p2.lineWidth = 1f;
                    p2.BeginPath();
                    for (float x = Mathf.Floor(lo); x < hi; x += 1f) {
                        float sa = p.TimeToSource(T(x)), sb = p.TimeToSource(T(x + 1f));
                        float sm = 0.5f * (sa + sb);
                        bool inside = sm >= p.exA && sm <= p.exB;
                        if (inside != (pass == 1)) continue;
                        if (!sum.Range(sa, sb, out float mn, out float mx)) continue;
                        float y0 = mid - mx / peak * h * 0.48f, y1 = mid - mn / peak * h * 0.48f;
                        if (y1 - y0 < 1f) { y0 -= 0.5f; y1 += 0.5f; }
                        p2.MoveTo(new Vector2(x + 0.5f, y0)); p2.LineTo(new Vector2(x + 0.5f, y1));
                    }
                    p2.Stroke();
                }
            }
            // The move strip along the top, and the two trim edges.
            Rect(p2, x0, 0, x1 - x0, TopBar, MoveBarColour(p, x0, x1));
            var edge = Es.trimHandleColor;
            bool hotA = drag == Drag.TrimA || (drag == Drag.None && hover.y >= TopBar && Mathf.Abs(hover.x - x0) <= EdgeGrab);
            bool hotB = drag == Drag.TrimB || (drag == Drag.None && hover.y >= TopBar && Mathf.Abs(hover.x - x1) <= EdgeGrab);
            Rect(p2, x0 - (hotA ? 1.5f : 0.75f), top, hotA ? 3f : 1.5f, h, edge);
            Rect(p2, x1 - (hotB ? 1.5f : 0.75f), top, hotB ? 3f : 1.5f, h, edge);
            // A piece running off an edge of the view: "continues".
            if (x0 < 0f && x1 > 0f) Tri(p2, 2f, mid, -1f, new Color(1f, 1f, 1f, 0.5f));
            if (x1 > W && x0 < W) Tri(p2, W - 2f, mid, 1f, new Color(1f, 1f, 1f, 0.5f));
            PaintOwnCurves(p2, p);
        }

        void PaintOwnCurves(Painter2D p2, TrackPlacement p) {
            if (ownCurves.Count == 0) return;
            var own = CurveAnchor.Axis.Of(p.klip, p.fileLen);
            float top = TopBar, h = H - TopBar;
            float xs = Mathf.Max(0f, X(p.SourceToTime(0f))), xe = Mathf.Min(W, X(p.End + (p.tail > 0f ? p.tail : 0f)));
            var editingMod = Editing;
            foreach (var oc in ownCurves) {
                if (!CurveView.IsVisible(oc.mod) && !ReferenceEquals(editingMod, oc.mod)) continue;
                var env = oc.mod.curve;
                float yr = Mathf.Max(1e-6f, env.yMax - env.yMin);
                bool editing = ReferenceEquals(editingMod, oc.mod);
                // While one curve is being edited the others step back: half transparent and twice as wide, no points.
                bool backdrop = editingMod != null && !editing;
                for (int pass = 0; pass < 2; pass++) {
                    var c = oc.colour; c.a = pass == 0 ? 0.35f : (editing ? 1f : backdrop ? 0.5f : 0.8f);
                    p2.strokeColor = c; p2.lineWidth = editing ? 1.6f : backdrop ? 2.2f : 1.1f;
                    p2.BeginPath(); bool started = false;
                    for (float x = xs; x <= xe; x += 2f) {
                        float t = T(x);
                        float s = p.TimeToSource(t);
                        bool heard = (s >= p.exA && s <= p.exB) || t > p.End;
                        if (heard != (pass == 1)) { started = false; continue; }
                        float cx = CurveXAt(p, oc.mod, t, own);
                        if (cx < 0f || cx > 1f) { started = false; continue; }
                        float v = env.Evaluate(cx);
                        var pt = new Vector2(x, top + h - (v - env.yMin) / yr * h);
                        if (!started) { p2.MoveTo(pt); started = true; } else p2.LineTo(pt);
                    }
                    p2.Stroke();
                }
                if (!editing) continue;
                for (int i = 0; i < env.Count; i++) {
                    var pt = env.GetPoint(i);
                    float t = TimeAtCurveX(p, oc.mod, pt.time, own);
                    float x = X(t);
                    if (x < -6f || x > W + 6f) continue;
                    var c = i == dragPoint ? Color.white : oc.colour;
                    Disc(p2, new Vector2(x, top + h - (pt.value - env.yMin) / yr * h), 3.5f, c);
                }
            }
        }

        // ─────────────────────────── input ───────────────────────────

        void OnDown(PointerDownEvent e) {
            var p = P;
            if (TL == null) return;
            Focus();
            var l = (Vector2)e.localPosition;
            downLocal = l; downTime = T(l.x); moved = false;
            if (e.button == 2) { drag = Drag.Pan; panT0 = TL.t0; this.CapturePointer(e.pointerId); e.StopPropagation(); return; }
            if (p == null || !p.found) return;
            // Right-click: play this track from the clicked moment (not while a curve is being edited: that click is the curve's).
            if (e.button == 1) {
                if (Editing == null) { win.PlayTrackFrom(entry, p.klip != null ? p.TimeToSource(downTime) : float.NaN); e.StopPropagation(); }
                return;
            }
            if (e.button != 0) return;
            float x0 = X(p.start), x1 = X(p.End);
            dragP = p; start0 = p.start; exA0 = p.exA; exB0 = p.exB;

            // Editing one of the sound's curves here: points first.
            var ed = Editing;
            if (ed != null && p.klip != null && CurvePress(e, p, ed, l)) return;

            if (p.klip != null && l.y >= TopBar && Mathf.Abs(l.x - x0) <= EdgeGrab) Begin(Drag.TrimA, "trim piece", e);
            else if (p.klip != null && l.y >= TopBar && Mathf.Abs(l.x - x1) <= EdgeGrab) Begin(Drag.TrimB, "trim piece", e);
            else if (l.y < TopBar && l.x >= x0 && l.x <= x1) Begin(Drag.Move, "move piece", e);
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
            // A trim or slip keeps the sound's curves on its audio from the first change (T-0501).
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
                    float dt = (l.x - downLocal.x) / Mathf.Max(1f, W) * TL.Span;
                    float span = TL.Span; TL.t0 = panT0 - dt; TL.t1 = TL.t0 + span; TL.fitted = false; TL.Changed();
                    break;
                }
                case Drag.Select: if (moved) { TL.selA = Mathf.Min(downTime, t); TL.selB = Mathf.Max(downTime, t); TL.Changed(); } break;
                case Drag.Move: TimelineEdits.Move(dragP, start0, t - downTime); win.OnTimelineChanged(); break;
                case Drag.TrimA: {
                    float a = Mathf.Clamp(dragP.TimeToSource(t), 0f, exB0 - 0.002f);
                    float at = dragP.SourceToTime(a);
                    TimelineEdits.SetExcerpt(dragP, a, exB0);
                    TimelineEdits.StartAt(dragP, at);
                    win.OnTimelineChanged();
                    break;
                }
                case Drag.TrimB: {
                    float b = Mathf.Clamp(dragP.TimeToSource(t), exA0 + 0.002f, dragP.fileLen);
                    TimelineEdits.SetExcerpt(dragP, exA0, b);
                    win.OnTimelineChanged();
                    break;
                }
                case Drag.Slip: TimelineEdits.Slip(dragP, exA0, exB0, -(t - downTime) * dragP.pitch); win.OnTimelineChanged(); break;
                case Drag.Point: CurveDrag(l); break;
            }
            e.StopPropagation();
        }

        void OnUp(PointerUpEvent e) {
            if (drag == Drag.None) return;
            var d = drag; drag = Drag.None;
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
            if (d == Drag.Move || d == Drag.TrimA || d == Drag.TrimB || d == Drag.Slip || d == Drag.Point) {
                ZoundsWindow.EndDragUndo();
                dragPoint = -1; dragMod = null;
            }
            if (d == Drag.Select && !moved) {
                // A click: a moment, not a range (split and paste use it), and the track plays from its start.
                TL.selA = TL.selB = downTime; TL.Changed();
                if (e.clickCount < 2) win.PlayTrackFrom(entry, float.NaN);
            }
            win.OnTimelineChanged();
            e.StopPropagation();
        }

        void OnWheel(WheelEvent e) {
            if (TL == null) return;
            float t = T(e.localMousePosition.x);
            if (e.ctrlKey || e.commandKey) TL.ZoomAround(t, e.delta.y > 0f ? 1.18f : 1f / 1.18f);
            else TL.Pan((e.delta.y + e.delta.x) * 0.06f * TL.Span);
            win.OnViewChanged();
            e.StopPropagation();
        }

        // ── curve points on the track ──

        bool CurvePress(PointerDownEvent e, TrackPlacement p, ZoundModifier m, Vector2 l) {
            var own = CurveAnchor.Axis.Of(p.klip, p.fileLen);
            var env = m.curve;
            float top = TopBar, h = H - TopBar, yr = Mathf.Max(1e-6f, env.yMax - env.yMin);
            int hit = -1;
            for (int i = 0; i < env.Count && hit < 0; i++) {
                var pt = env.GetPoint(i);
                var c = new Vector2(X(TimeAtCurveX(p, m, pt.time, own)), top + h - (pt.value - env.yMin) / yr * h);
                if (Vector2.Distance(c, l) <= 6f) hit = i;
            }
            float cx = CurveXAt(p, m, T(l.x), own);
            bool onLine = cx >= 0f && cx <= 1f && Mathf.Abs(top + h - (env.Evaluate(cx) - env.yMin) / yr * h - l.y) <= 5f;
            if (hit < 0 && !onLine) return false;
            ZoundsWindow.BeginDragUndo("edit curve");
            // First edit: onto the source's own seconds, and the hit point is found again in the converted curve.
            if (KlipChainEnvelopes.EnsureSourceAnchored(p.klip)) {
                own = CurveAnchor.Axis.Of(p.klip, p.fileLen);
                cx = CurveXAt(p, m, T(l.x), own);
                hit = -1;
                for (int i = 0; i < env.Count && hit < 0; i++) {
                    var pt = env.GetPoint(i);
                    var c = new Vector2(X(TimeAtCurveX(p, m, pt.time, own)), top + h - (pt.value - env.yMin) / yr * h);
                    if (Vector2.Distance(c, l) <= 6f) hit = i;
                }
            }
            if (hit >= 0 && e.clickCount == 2 && hit > 0 && hit < env.Count - 1) {
                env.RemovePoint(hit);
                Touch(p); ZoundsWindow.EndDragUndo(); win.OnTimelineChanged();
            }
            else if (hit >= 0) { drag = Drag.Point; dragPoint = hit; dragMod = m; this.CapturePointer(e.pointerId); }
            else if (e.clickCount == 2) {
                env.AddPoint(Mathf.Clamp01(cx), env.Evaluate(Mathf.Clamp01(cx)));
                Touch(p); ZoundsWindow.EndDragUndo(); win.OnTimelineChanged();
            }
            else { ZoundsWindow.EndDragUndo(); return false; }
            e.StopPropagation();
            return true;
        }

        void CurveDrag(Vector2 l) {
            var p = dragP; var m = dragMod;
            if (p == null || m == null || dragPoint < 0) return;
            var own = CurveAnchor.Axis.Of(p.klip, p.fileLen);
            var env = m.curve;
            float top = TopBar, h = H - TopBar;
            float x = Mathf.Clamp01(CurveXAt(p, m, T(l.x), own));
            float v = env.yMin + (env.yMax - env.yMin) * Mathf.Clamp01((top + h - l.y) / h);
            var pts = env.GetPointsList();
            if (dragPoint == 0) x = 0f;
            else if (dragPoint == pts.Count - 1) x = 1f;
            else x = Mathf.Clamp(x, pts[dragPoint - 1].time + 1e-4f, pts[dragPoint + 1].time - 1e-4f);
            pts[dragPoint].time = x; pts[dragPoint].value = v;
            Touch(p);
            win.OnTimelineChanged();
        }

        static void Touch(TrackPlacement p) { var chain = ZoundDspPlayback.ResolveChain(p.klip, out _); chain?.Touch(); EditorUtility.SetDirty(ZoundsProject.Instance); }

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

        static void Disc(Painter2D p2, Vector2 c, float r, Color col) {
            p2.fillColor = col; p2.BeginPath(); p2.Arc(c, r, 0f, 360f); p2.Fill();
        }
    }
}
