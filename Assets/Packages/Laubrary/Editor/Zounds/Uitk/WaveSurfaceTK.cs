using System;
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Curve = Laubrary.Zounds.AudioSpectrumView.Curve;
using TrimDrag = Laubrary.Zounds.AudioSpectrumView.TrimDrag;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// What a <see cref="WaveSurfaceTK"/> needs from the window it sits in. The surface does everything that is about the
    /// SOUND; the host says where the sound's seconds are on screen and carries out what only the host can do (its own
    /// gestures, its Undo step, playing).
    /// </summary>
    internal interface IWaveSurfaceHost {
        Klip Sound { get; }
        /// <summary>The file the sound plays from, and its length in seconds.</summary>
        AudioClip Source { get; }
        float FileLength { get; }
        /// <summary>The axis: the x in the surface's area where a second of the file is drawn, and back. Need not be a
        /// straight line (a Zequence track draws the file where it SOUNDS, through the sound's pitch and time curves).</summary>
        float XOf(float sourceSeconds, Rect area);
        float SourceAt(float x, Rect area);
        /// <summary>The part of the file the sound plays (its trim, or a track's excerpt), and whether that part has edges
        /// to drag at all (false: the whole file plays, no handles, no dimmed parts).</summary>
        bool Heard(out float from, out float to);
        void TrimHandlesLive(out bool start, out bool end);
        /// <summary>The trim edges are the sound's own trim (not a track's excerpt), so they can be made random.</summary>
        bool TrimRandomEditable { get; }
        /// <summary>A trim drag begins (the host's guard and Undo step); false cancels it.</summary>
        bool BeginTrim(TrimDrag which, float x, Rect area);
        void DragTrim(float x, Rect area);
        void EndTrim();
        /// <summary>The sound's own volume, pitch or time curve (null or disabled: none), and which one is selected for
        /// editing (-1: none).</summary>
        Envelope CurveOf(Curve which);
        int SelectedCurve { get; }
        /// <summary>Where a curve is drawn: its rect in the area, the part of its x drawn across that rect, and, when the
        /// host's axis is not a straight line, the curve-x to area-x mapping and back (null: linear over the rect).</summary>
        void CurveDomain(Curve which, Envelope env, ZoundModifier mod, Rect area, out Rect rect, out float xMin, out float xMax,
                         out Func<float, float> toX, out Func<float, float> fromX);
        /// <summary>Opens / closes the Undo step of an edit of the sound made on the surface.</summary>
        void BeginEditUndo(string name);
        void EndEditUndo();
        /// <summary>After an edit of the sound's curves (already marked changed): the host redraws what depends on it.</summary>
        void SoundChanged();
        /// <summary>The shared-sound guard, before any edit of the sound's curves; false cancels the edit.</summary>
        bool BeforeEdit();
        /// <summary>Where each play of this sound (and only this sound) is reading its file, in seconds, with how strongly
        /// to draw each (a Looper's copy fading out is fainter).</summary>
        void Playheads(List<float> seconds, List<float> weights);
        /// <summary>Plays only this sound from that second of its file (a right-click); pressed again while it sounds: stops.</summary>
        void PlayFrom(float sourceSeconds);
        /// <summary>A left press nothing on the surface took (no trim edge, no curve being edited): the host's own gesture.
        /// False lets it go on to whatever holds the surface.</summary>
        bool Press(PointerDownEvent e, Vector2 m, Rect area);
        /// <summary>A right press nothing on the surface took (no trim edge, no point of the curve being edited), before it
        /// plays from there: true when the host took it instead (the Klip editor's selection menu).</summary>
        bool ContextPress(PointerDownEvent e, Vector2 m, Rect area);
        bool Move(Vector2 m, Rect area);
        void Release();
        bool Key(KeyDownEvent e);
        /// <summary>What a left click does in this host, for the tooltip.</summary>
        string ClickTip { get; }
        /// <summary>What a right click does in this host, for the tooltip.</summary>
        string RightClickTip { get; }
        /// <summary>The height the grip along the bottom edge sets.</summary>
        float Height { get; set; }
        /// <summary>The grip was let go.</summary>
        void EndHeightDrag();
        /// <summary>A picture of the waveform for a file whose samples cannot be read (streamed or compressed in memory), or null.</summary>
        Texture FallbackWave(int w, int h, out Rect uv);
    }

    /// <summary>
    /// The waveform surface (2026-10-09): ONE component, used by the Klip editor and by every Klip track of the Zequence
    /// editor, so everything about the sound looks and behaves the same in both. It draws the file (bright, the parts the
    /// sound does not play dimmed), a Looper's crossmix, the sound's own Volume, Pitch and Time curves (the one selected
    /// for editing with its points, the others as backdrops: half transparent and twice as wide) with their axes and the
    /// combined result of everything moving them, the trim edges, one playhead per read head of each play of this sound,
    /// what the newest play hears, and the height grip. Its gestures: a press on a point or on the line of the curve being
    /// edited edits it (add, drag, delete, random points, bend, box select); an edge of the trim drags it (a right-drag on
    /// one moves both edges together); a right-click anywhere else is offered to the host (the Klip editor's selection menu), else it plays only this sound from that second; the Delete key
    /// removes selected points. A left press nothing here took is the host's (the Klip editor's edit cursor, a track's
    /// timeline gestures). The host only says where the seconds are drawn (<see cref="IWaveSurfaceHost"/>).
    /// </summary>
    internal sealed class WaveSurfaceTK : VisualElement {

        internal const float MinH = 24f;
        readonly IWaveSurfaceHost host;
        Klip Sound => host.Sound;

        /// <summary>The drawing area; a host adds its own marks to <see cref="marks"/> and its overlays to <see cref="overlay"/>.</summary>
        internal readonly VisualElement area;
        internal readonly VisualElement marks, overlay;
        readonly VisualElement waveLayer, heads, handleStart, handleEnd, randStart, randEnd, combined, grip;
        readonly Image fallback;
        readonly VisualElement[] xmix = new VisualElement[4];
        readonly Label pitchTop, pitchMid, pitchBottom, timeTop, timeMid, timeBottom, live;
        readonly VisualElement pitchLine, timeLine;
        /// <summary>The sound's own curves: Volume, Pitch and Time (in <see cref="Curve"/>'s order).</summary>
        internal const int CurveCount = 3;
        readonly ZuiSkinEnvelope[] envs = new ZuiSkinEnvelope[CurveCount];
        readonly ZUIEnvelopeRuntime[] runtimes = new ZUIEnvelopeRuntime[CurveCount];
        readonly List<VisualElement> headPool = new List<VisualElement>();
        readonly List<float> headSeconds = new List<float>(), headWeights = new List<float>();
        readonly List<(Color colour, OwnValueCurves.Line line, Rect rect, float xMin, float xMax, float yMin, float yMax, Func<float, float> toX)> combinedLines
            = new List<(Color, OwnValueCurves.Line, Rect, float, float, float, float, Func<float, float>)>();
        internal SwapNoticeTK notice;

        ZuiSkinEnvelope active;
        bool trimDragging, editOpen;
        int hotHandle = -1;
        float gripDownY, gripDownH;
        string lastTip;

        /// <summary>Whether the pitch curve is still on its old scale (the curve bar shows the warning).</summary>
        internal bool PitchOldScale { get; private set; }

        /// <param name="framed">The Klip editor's look: a box with the drawing inset by 4 (a track's lane is drawn edge to edge).</param>
        public WaveSurfaceTK(IWaveSurfaceHost host, bool framed) {
            this.host = host;
            AddToClassList("zs-wave-surface");
            if (framed) { AddToClassList("zs-waveform-box"); AddToClassList("zs-wave-surface--framed"); }
            area = new VisualElement();
            area.AddToClassList("zs-wave-surface__area");
            area.focusable = true;
            Add(area);

            waveLayer = Layer(); waveLayer.generateVisualContent += PaintWave; area.Add(waveLayer);
            fallback = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            fallback.AddToClassList("zs-wave-surface__layer"); fallback.style.display = DisplayStyle.None;
            area.Add(fallback);
            // A Looper's crossmix spans (T-0476): the longest possible span, and the part every loop uses, at both ends.
            for (int i = 0; i < 4; i++) {
                xmix[i] = Abs(); xmix[i].AddToClassList("zs-xmix");
                if (i >= 2) xmix[i].AddToClassList("zs-xmix--min");
                area.Add(xmix[i]);
            }
            pitchLine = Abs(); area.Add(pitchLine);
            pitchTop = AxisLabel(); pitchMid = AxisLabel(); pitchBottom = AxisLabel();
            area.Add(pitchTop); area.Add(pitchMid); area.Add(pitchBottom);
            timeLine = Abs(); area.Add(timeLine);
            timeTop = AxisLabel(); timeMid = AxisLabel(); timeBottom = AxisLabel();
            foreach (var l in new[] { timeTop, timeMid, timeBottom }) { l.AddToClassList("zs-wave-surface__time-axis-label"); area.Add(l); }
            timeTop.text = "×4"; timeMid.text = "×1"; timeBottom.text = "×¼";
            // The combined results (T-0494), then the host's marks (an edit selection, a timeline selection), under the
            // playheads, the trim edges and the curves.
            combined = Layer(); combined.generateVisualContent += PaintCombined; area.Add(combined);
            marks = Layer(); area.Add(marks);
            heads = Layer(); area.Add(heads);
            handleStart = Abs(); handleEnd = Abs();
            handleStart.AddToClassList("zs-trimhandle"); handleEnd.AddToClassList("zs-trimhandle");
            // A random trim edge's range (owner, 2026-10-09), shaded around the edge, under it.
            randStart = Abs(); randEnd = Abs();
            randStart.AddToClassList("zs-wave-surface__trim-random"); randEnd.AddToClassList("zs-wave-surface__trim-random");
            area.Add(randStart); area.Add(randEnd);
            area.Add(handleStart); area.Add(handleEnd);
            for (int i = 0; i < CurveCount; i++) {
                var which = (Curve)i;
                runtimes[i] = new ZUIEnvelopeRuntime {
                    onDragStarted = () => BeginCurveEdit(which),
                    onDragUpdated = CurveChanged,
                    onMutated = CurveChanged,
                };
                var env = new ZuiSkinEnvelope(null, Color.white, null, null, standalone: false) { pickingMode = PickingMode.Ignore };
                env.AddToClassList("zs-wave-surface__envelope");
                envs[i] = env;
                area.Add(env);
                // Right-click a point: its random settings (T-0483), inside the curve's own edit start.
                env.onPointContext = (pi, world) => {
                    if (env.points == null || pi < 0 || pi >= env.points.Count || env.rt == null) return;
                    var sel = new List<ZUIEnvelopePoint>();
                    foreach (int s in env.SelectedPoints) if (s >= 0 && s < env.points.Count) sel.Add(env.points[s]);
                    RandomPointPopup.Show(world, env.points[pi], Mathf.Max(env.rt.dataXMax - env.rt.dataXMin, 1e-3f),
                        () => env.rt.yMax - env.rt.yMin,
                        () => KlipChainEnvelopes.BeginCurveEdit(Sound, (int)which),
                        () => { KlipChainEnvelopes.Touch(Sound); host.SoundChanged(); Refresh(); },
                        sel,
                        () => KlipChainEnvelopes.WaveformCurveNeutral(Sound, (int)which, out float v) ? v : (float?)null,
                        () => new Vector2(env.rt.yMin, env.rt.yMax));
                };
            }
            overlay = Layer(); area.Add(overlay);
            // What the newest play hears, floating in the top-right corner (a floating mark never moves anything).
            live = new Label { pickingMode = PickingMode.Ignore, tooltip = "What the newest play of this sound hears right now for its own values (volume, pitch in semitones, speed), after its curves, modulators and game-code values." };
            live.AddToClassList("zs-lbl"); live.AddToClassList("zs-greymini"); live.AddToClassList("zs-wave-surface__live");
            live.style.display = DisplayStyle.None;
            area.Add(live);

            // The grip along the bottom edge: drag it to make the waveform taller or shorter.
            grip = new VisualElement { tooltip = "Drag to change the waveform's height." };
            grip.AddToClassList("zs-wave-surface__grip");
            grip.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; gripDownY = e.position.y; gripDownH = host.Height; grip.CapturePointer(e.pointerId); e.StopPropagation(); });
            grip.RegisterCallback<PointerMoveEvent>(e => {
                if (!grip.HasPointerCapture(e.pointerId)) return;
                host.Height = gripDownH + (e.position.y - gripDownY);
                Refresh();
                e.StopPropagation();
            });
            grip.RegisterCallback<PointerUpEvent>(e => { if (grip.HasPointerCapture(e.pointerId)) { grip.ReleasePointer(e.pointerId); e.StopPropagation(); host.EndHeightDrag(); } });
            Add(grip);
            // The "Tell me" notice floats over the top of the waveform, so it never moves anything.
            notice = new SwapNoticeTK();
            notice.AddToClassList("zs-wave-surface__notice");
            Add(notice);

            area.RegisterCallback<PointerDownEvent>(OnDown);
            area.RegisterCallback<PointerMoveEvent>(OnMove);
            area.RegisterCallback<PointerUpEvent>(OnUp);
            area.RegisterCallback<PointerLeaveEvent>(_ => { foreach (var e in envs) e.PointerLeft(); SetHot(-1); });
            area.RegisterCallback<KeyDownEvent>(OnKey);
            area.RegisterCallback<GeometryChangedEvent>(_ => Refresh());
        }

        // ─────────────────────────── helpers ───────────────────────────

        static VisualElement Abs() {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("zs-wave-surface__positioned");
            return e;
        }

        static VisualElement Layer() {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("zs-wave-surface__layer");
            return e;
        }

        static Label AxisLabel() {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.AddToClassList("zs-lbl"); l.AddToClassList("zs-mini");
            l.AddToClassList("zs-wave-surface__axis-label");
            return l;
        }

        static void Place(VisualElement e, Rect r) {
            e.style.left = r.x; e.style.top = r.y; e.style.width = Mathf.Max(0f, r.width); e.style.height = Mathf.Max(0f, r.height);
        }

        internal Rect AreaRect => new Rect(0f, 0f, area.contentRect.width, area.contentRect.height);

        static ZoundsProject.ProjectSettings.EditorStyle Es => ZoundsProject.Instance.projectSettings.editorStyle;

        internal static Color ColourOf(Curve which) =>
            which == Curve.Volume ? Es.volumeEnvelopeColor : which == Curve.Pitch ? Es.pitchEnvelopeColor : AudioSpectrumView.TimeCurveColor;

        static string UndoName(Curve which) => which == Curve.Volume ? "edit volume envelope" : which == Curve.Pitch ? "edit pitch envelope" : "edit time curve";

        internal static string NameOf(int which) => which == 0 ? "volume" : which == 1 ? "pitch" : "time";

        /// <summary>The curve selected for editing, when it is drawn and editable, else null.</summary>
        ZuiSkinEnvelope SelectedEnv(out Curve which) {
            int i = host.SelectedCurve;
            which = (Curve)Mathf.Clamp(i, 0, CurveCount - 1);
            if (i < 0 || i >= CurveCount) return null;
            var e = envs[i];
            return e.resolvedStyle.display != DisplayStyle.None && e.rt != null && e.points != null ? e : null;
        }

        /// <summary>The trim edges in the area: the start edge's strip begins at its second, the end edge's ends at its
        /// second (the Klip editor's handles, now everywhere). Null when that edge is out of view.</summary>
        Rect? Handle(bool start, Rect r) {
            if (!host.Heard(out float a, out float b)) return null;
            float th = Mathf.Max(1f, Es.trimHandleThickness);
            float x = start ? host.XOf(a, r) : host.XOf(b, r) - th;
            if (x < r.x - th || x > r.xMax) return null;
            x = Mathf.Clamp(x, r.x, Mathf.Max(r.x, r.xMax - th));
            return new Rect(x, r.y, th, r.height);
        }

        /// <summary>Which trim edge a press at <paramref name="x"/> takes (0 start, 1 end, -1 none), with a little slop.</summary>
        int HandleAt(float x, Rect r) {
            host.TrimHandlesLive(out bool sl, out bool el);
            const float slop = 3f;
            var he = el ? Handle(false, r) : null;
            var hs = sl ? Handle(true, r) : null;
            if (he.HasValue && x >= he.Value.x - slop && x <= he.Value.xMax + slop) return 1;
            if (hs.HasValue && x >= hs.Value.x - slop && x <= hs.Value.xMax + slop) return 0;
            return -1;
        }

        void SetHot(int h) {
            if (h == hotHandle) return;
            hotHandle = h;
            area.EnableInClassList("zs-wave-surface--trim-hot", h >= 0);
            Refresh();
        }

        // ─────────────────────────── refresh ───────────────────────────

        public void Refresh() {
            if (panel == null) return;
            var clip = host.Source;
            area.style.visibility = clip == null ? Visibility.Hidden : Visibility.Visible;
            if (clip == null || Sound == null) return;
            var r = AreaRect;
            if (r.width <= 1f || r.height <= 1f) return;

            string heard = KlipChainEnvelopes.LiveReadout(Sound);
            if (live.text != heard) live.text = heard;
            live.style.display = heard.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            // The file's picture: painted from its samples at any zoom; a picture of it only when they cannot be read.
            bool painted = WaveSummary.For(clip) != null;
            fallback.style.display = painted ? DisplayStyle.None : DisplayStyle.Flex;
            if (!painted) {
                var tex = host.FallbackWave(Mathf.FloorToInt(r.width), Mathf.FloorToInt(r.height), out var uv);
                if (fallback.image != tex) fallback.image = tex;
                fallback.uv = uv;
            }
            waveLayer.MarkDirtyRepaint();

            // The trim edges, brighter and wider under the pointer.
            host.TrimHandlesLive(out bool sl, out bool el);
            var hs = sl ? Handle(true, r) : null;
            var he = el ? Handle(false, r) : null;
            var hc = Es.trimHandleColor;
            if (!enabledInHierarchy) hc.a = 0.35f;
            PlaceHandle(handleStart, hs, hc, hotHandle == 0 || (trimDragging && hotHandle < 0));
            PlaceHandle(handleEnd, he, hc, hotHandle == 1);
            PlaceTrimRandom(r, hc);

            PlaceCrossmix(r);

            // Playheads, over the dims and under the curves: where each play is READING its file (T-0493).
            headSeconds.Clear(); headWeights.Clear();
            host.Playheads(headSeconds, headWeights);
            int shown = 0;
            for (int i = 0; i < headSeconds.Count; i++) {
                float x = host.XOf(headSeconds[i], r);
                if (x < r.x - 1f || x > r.xMax + 1f) continue;
                if (shown >= headPool.Count) headPool.Add(PlayHead());
                var h = headPool[shown++];
                if (h.parent != heads) heads.Add(h);
                h.style.display = DisplayStyle.Flex;
                h.style.opacity = i < headWeights.Count ? headWeights[i] : 1f;
                h.style.left = x; h.style.top = r.y; h.style.height = r.height;
            }
            for (int i = shown; i < headPool.Count; i++) headPool[i].style.display = DisplayStyle.None;

            // The sound's own curves.
            combinedLines.Clear();
            Rect pitchRect = r, timeRect = r;
            bool anySelected = false;
            for (int i = 0; i < CurveCount; i++) if (host.SelectedCurve == i && Drawn((Curve)i)) anySelected = true;
            for (int i = 0; i < CurveCount; i++) {
                var rect = Overlay((Curve)i, r, anySelected);
                if (i == 1) pitchRect = rect; else if (i == 2) timeRect = rect;
            }
            foreach (var e in envs) if (e.showHandles && e.resolvedStyle.display != DisplayStyle.None) e.BringToFront();
            overlay.BringToFront(); live.BringToFront();
            PlacePitchAxis(pitchRect);
            PlaceTimeAxis(timeRect);
            Place(combined, r);
            combined.MarkDirtyRepaint();

            string tip = Tip();
            if (tip != lastTip) { lastTip = tip; area.tooltip = tip; }
        }

        /// <summary>The range each random trim edge may land in, shaded across the waveform's height in the trim colour.</summary>
        void PlaceTrimRandom(Rect r, Color c) {
            var k = Sound;
            bool any = host.TrimRandomEditable && k != null && k.HasTrimRandom && host.Heard(out _, out _);
            float a = 0f, b = 0f;
            if (any) host.Heard(out a, out b);
            for (int i = 0; i < 2; i++) {
                var e = i == 0 ? randStart : randEnd;
                float rad = !any ? 0f : i == 0 ? k.trimStartRandom : k.trimEndRandom;
                float edge = i == 0 ? a : b;
                e.style.display = rad > 0f ? DisplayStyle.Flex : DisplayStyle.None;
                if (rad <= 0f) continue;
                float x0 = Mathf.Max(r.x, host.XOf(Mathf.Max(0f, edge - rad), r)), x1 = Mathf.Min(r.xMax, host.XOf(Mathf.Min(host.FileLength, edge + rad), r));
                Place(e, Rect.MinMaxRect(x0, r.y, Mathf.Max(x0 + 1f, x1), r.yMax));
                e.style.backgroundColor = new Color(c.r, c.g, c.b, 0.16f);
                e.style.borderLeftColor = e.style.borderRightColor = new Color(c.r, c.g, c.b, 0.45f);
            }
        }

        void PlaceHandle(VisualElement e, Rect? rect, Color c, bool hot) {
            e.style.display = rect.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            if (!rect.HasValue) return;
            var r = rect.Value;
            if (hot) { r.x -= 1f; r.width += 2f; c = Color.Lerp(c, Color.white, 0.35f); }
            Place(e, r);
            e.style.backgroundColor = c;
        }

        string Tip() {
            var s = new System.Text.StringBuilder(host.ClickTip);
            s.Append("\nRight-click: play only this sound from here (again: stop).");
            if (host.Heard(out _, out _)) s.Append("\nDrag a trim edge to move it; right-drag one to move both edges together" + (host.TrimRandomEditable ? "; right-click one for its random range (a different edge each play)." : "."));
            int sel = host.SelectedCurve;
            if (sel >= 0 && sel < CurveCount && Drawn((Curve)sel))
                s.Append("\nEditing the ").Append(NameOf(sel))
                 .Append(" curve: drag a point; click its line (or double-click anywhere) to add one; double-click a point to remove it; right-click a point for its random range; Shift+drag the line to bend it; drag empty space to select several points (Delete removes them).");
            return s.ToString();
        }

        bool Drawn(Curve which) {
            var env = host.CurveOf(which);
            return env != null && env.enabled;
        }

        readonly Dictionary<(Color, float), ZUIEnvelopeDef> defs = new Dictionary<(Color, float), ZUIEnvelopeDef>();

        /// <summary>Lays out one of the sound's curves; returns the rect it is drawn in.</summary>
        Rect Overlay(Curve which, Rect r, bool anySelected) {
            var env = envs[(int)which];
            var authored = host.CurveOf(which);
            var mod = authored != null ? KlipChainEnvelopes.ModifierOf(Sound, authored) : null;
            bool selected = host.SelectedCurve == (int)which;
            bool shown = CurveView.IsVisible(mod);
            // Eye off: not drawn -- unless it is the curve selected for editing, whose points stay (T-0494).
            bool draw = authored != null && authored.enabled && (shown || selected);
            env.style.display = draw ? DisplayStyle.Flex : DisplayStyle.None;
            if (!draw) return r;
            host.CurveDomain(which, authored, mod, r, out var rect, out float xMin, out float xMax, out var toX, out var fromX);
            var rt = runtimes[(int)which];
            rt.xMin = xMin; rt.xMax = xMax;
            rt.dataXMin = authored.xMin; rt.dataXMax = authored.xMax;
            rt.yMin = authored.yMin; rt.yMax = authored.yMax;
            rt.editable = selected; rt.allowAddPoints = selected;
            var pts = authored.GetPointsList();
            AudioSpectrumView.PinEndpointsYOnly(pts);
            var colour = ColourOf(which);
            float thick = which == Curve.Volume ? Es.volumeEnvelopeThickness : Es.pitchEnvelopeThickness;
            if (!defs.TryGetValue((colour, thick), out var def)) defs[(colour, thick)] = def = AudioSpectrumView.BuildOverlayDef(colour, thick);
            env.points = pts; env.def = def; env.rt = rt; env.curveColor = colour;
            // Only the curve selected for editing shows its points; while one is being edited the others step back as
            // backdrops (half transparent, twice as wide), owner's request 2026-10-08.
            env.showHandles = selected;
            env.backdrop = anySelected && !selected;
            // How a backdrop is drawn: the Settings tab's one choice for every curve (owner, 2026-10-09), live.
            env.backdropDotted = Es.backdropDotted; env.backdropTransparent = Es.backdropTransparent; env.backdropWidthBonus = Mathf.Max(0f, Es.backdropWidthBonus);
            float ox = rect.x;
            env.timeToLocalX = toX == null ? null : (Func<float, float>)(t => toX(t) - ox);
            env.localXToTime = fromX == null ? null : (Func<float, float>)(x => fromX(x + ox));
            // What the plays under way hear, dotted (T-0484); nothing while hidden.
            if (shown) LiveDrawnCurves.Fill(ref env.liveCurves, Sound, authored, KlipChainEnvelopes.ModifierIndexOf(Sound, authored));
            else env.liveCurves = null;
            Place(env, rect);
            env.Repaint();
            // The combined result of everything on this curve's value, when something besides the curve moves it.
            if (shown && OwnValueCurves.TryGet(Sound, mod, out var line))
                combinedLines.Add((colour, line, rect, xMin, xMax, authored.yMin, authored.yMax, toX));
            if (which == Curve.Pitch) PitchOldScale = KlipChainEnvelopes.PitchIsOldScale(Sound);
            return rect;
        }

        /// <summary>The time curve's axis on the right of its rect (T-0482), when there is room for it.</summary>
        void PlaceTimeAxis(Rect envRect) {
            bool on = Drawn(Curve.Time) && CurveView.IsVisible(KlipChainEnvelopes.ModifierOf(Sound, host.CurveOf(Curve.Time))) && envRect.height >= 40f;
            foreach (var e in new VisualElement[] { timeTop, timeMid, timeBottom, timeLine }) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) return;
            var c = AudioSpectrumView.TimeCurveColor;
            float right = Mathf.Min(envRect.xMax, AreaRect.xMax);
            foreach (var l in new[] { timeTop, timeMid, timeBottom }) { l.style.color = c; l.style.left = right - 29f; }
            timeTop.style.top = envRect.y + 1f;
            timeBottom.style.top = envRect.yMax - 14f;
            float my = Mathf.Round(envRect.y + envRect.height * 0.5f);
            timeMid.style.top = my - 14f;
            float l0 = Mathf.Max(0f, envRect.x);
            timeLine.style.left = l0; timeLine.style.width = Mathf.Max(0f, Mathf.Min(envRect.xMax, AreaRect.xMax) - l0);
            timeLine.style.top = my; timeLine.AddToClassList("zs-wave-surface__axis-line");
            timeLine.style.backgroundColor = new Color(c.r, c.g, c.b, 0.3f);
        }

        /// <summary>The pitch curve's axis (T-0479): what its top, middle and bottom mean, and the "no change" line -- or,
        /// for a curve still on its old scale, only the warning on the curve bar.</summary>
        void PlacePitchAxis(Rect envRect) {
            string top = null, mid = null, bottom = null; bool oldScale = false;
            bool on = Drawn(Curve.Pitch) && KlipChainEnvelopes.PitchAxis(Sound, out top, out mid, out bottom, out oldScale);
            PitchOldScale = on && oldScale;
            bool axis = on && !oldScale && top != null && CurveView.IsVisible(KlipChainEnvelopes.ModifierOf(Sound, host.CurveOf(Curve.Pitch))) && envRect.height >= 40f;
            foreach (var e in new VisualElement[] { pitchTop, pitchMid, pitchBottom }) e.style.display = axis ? DisplayStyle.Flex : DisplayStyle.None;
            pitchLine.style.display = axis && mid != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (!axis) return;
            var c = Es.pitchEnvelopeColor;
            foreach (var l in new[] { pitchTop, pitchMid, pitchBottom }) l.style.color = new Color(c.r, c.g, c.b, 0.9f);
            pitchTop.text = top; pitchMid.text = mid ?? ""; pitchBottom.text = bottom;
            float x = Mathf.Max(0f, envRect.x) + 3f;
            pitchTop.style.left = pitchMid.style.left = pitchBottom.style.left = x;
            pitchTop.style.top = envRect.y + 1f;
            pitchBottom.style.top = envRect.yMax - 14f;
            float my = Mathf.Round(envRect.y + envRect.height * 0.5f);
            pitchMid.style.top = my - 14f;
            float l0 = Mathf.Max(0f, envRect.x);
            pitchLine.style.left = l0; pitchLine.style.width = Mathf.Max(0f, Mathf.Min(envRect.xMax, AreaRect.xMax) - l0);
            pitchLine.style.top = my; pitchLine.AddToClassList("zs-wave-surface__axis-line");
            pitchLine.style.backgroundColor = new Color(c.r, c.g, c.b, 0.35f);
        }

        /// <summary>
        /// Shades the parts of a Looper's source its crossmix uses: at the start, what the incoming copy fades in over; at
        /// the end, what the outgoing copy fades out over. The faint band is the longest crossmix the range allows, the
        /// stronger one inside it the shortest. Bounds are the ones the engine hears (limited to half the loop).
        /// </summary>
        void PlaceCrossmix(Rect r) {
            var k = Sound;
            float len = host.FileLength;
            bool show = k.IsLooper;
            float from = 0f, to = 0f, lo = 0f, hi = 0f;
            if (show) {
                from = k.trimEnabled ? k.trimStart : 0f;
                to = k.trimEnabled && k.trimEnd > k.trimStart ? Mathf.Min(k.trimEnd, len) : len;
                k.loop.Effective(to - from, out lo, out hi);
                show = hi > 0f;
            }
            for (int i = 0; i < 4; i++) xmix[i].style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            float x0 = host.XOf(from, r), x1 = host.XOf(to, r);
            Place(xmix[0], Rect.MinMaxRect(x0, r.y, host.XOf(from + hi, r), r.yMax));
            Place(xmix[1], Rect.MinMaxRect(host.XOf(to - hi, r), r.y, x1, r.yMax));
            xmix[2].style.display = xmix[3].style.display = lo > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            Place(xmix[2], Rect.MinMaxRect(x0, r.y, host.XOf(from + lo, r), r.yMax));
            Place(xmix[3], Rect.MinMaxRect(host.XOf(to - lo, r), r.y, x1, r.yMax));
        }

        VisualElement PlayHead() {
            var root = Abs();
            root.AddToClassList("zs-wave-surface__play-head");
            var cursor = new Image { image = AudioWaveformUtility.playerHeadTexture, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore, tintColor = AudioWaveformUtility.playerHeadColor };
            var sz = AudioWaveformUtility.playerHeadSize;
            cursor.AddToClassList("zs-wave-surface__play-head-cursor"); cursor.style.left = -sz.x / 2f; cursor.style.width = sz.x; cursor.style.height = sz.x * 1.82f;
            var line = Abs();
            float th = AudioWaveformUtility.playerHeadThickness;
            line.style.left = -th * 0.5f; line.style.width = th; line.style.top = sz.y; line.AddToClassList("zs-wave-surface__play-head-line");
            line.style.backgroundColor = AudioWaveformUtility.playerHeadColor;
            root.Add(line); root.Add(cursor);
            return root;
        }

        // ─────────────────────────── painting ───────────────────────────

        /// <summary>
        /// The file: its background where the file is, and one lowest-to-highest stroke per pixel column, read from the
        /// file's in-memory summary at the seconds the host draws that column at (so a track's piece, stretched unevenly by
        /// its curves, is drawn where it sounds), scaled by the sound's Gain (what its effects receive). Full scale reaches the
        /// edge (see <see cref="WaveHeightPerUnit"/>); what the Gain lifts past it is cut at the edge and marked red. The parts the sound does not play are drawn too, clearly but dimmer: their background takes the
        /// Settings tab's trim colour and their waveform is drawn at half strength, so the played part stands out without
        /// hiding what is around it.
        /// </summary>
        void PaintWave(MeshGenerationContext ctx) {
            var clip = host.Source;
            var r = AreaRect;
            if (clip == null || r.width < 2f || r.height < 2f) return;
            var p2 = ctx.painter2D;
            float len = host.FileLength;
            float x0 = Mathf.Max(0f, host.XOf(0f, r)), x1 = Mathf.Min(r.width, host.XOf(len, r));
            if (x1 <= x0) return;
            var bg = Es.klipWaveformBGColor;
            Fill(p2, x0, 0f, x1 - x0, r.height, bg);
            // The unplayed parts: the trim colour over the background.
            float xa = x0, xb = x1;
            bool heard = host.Heard(out float ha, out float hb);
            if (heard) {
                xa = Mathf.Clamp(host.XOf(ha, r), x0, x1); xb = Mathf.Clamp(host.XOf(hb, r), x0, x1);
                var dim = Es.trimAreaColor;
                Fill(p2, x0, 0f, xa - x0, r.height, dim);
                Fill(p2, xb, 0f, x1 - xb, r.height, dim);
            }
            var sum = WaveSummary.For(clip);
            if (sum == null) return;
            float g = GainOf(Sound);
            float mid = r.height * 0.5f, amp = r.height * WaveHeightPerUnit;
            var full = Es.waveformColor;
            var faint = new Color(full.r, full.g, full.b, full.a * 0.5f);
            // Two passes, so each colour is one path: the played part, then the unplayed parts.
            clipped.Clear();
            for (int pass = 0; pass < (heard ? 2 : 1); pass++) {
                p2.strokeColor = pass == 0 ? full : faint; p2.lineWidth = 1f;
                p2.BeginPath();
                for (float x = Mathf.Floor(x0); x < x1; x += 1f) {
                    bool inside = !heard || (x + 0.5f >= xa && x + 0.5f <= xb);
                    if (inside != (pass == 0)) continue;
                    float sa = host.SourceAt(x, r), sb = host.SourceAt(x + 1f, r);
                    if (!sum.Range(sa, sb, out float mn, out float mx)) continue;
                    if (pass == 0 && (mx * g > 1f || mn * g < -1f)) clipped.Add(x + 0.5f);
                    float y0 = Mathf.Max(0f, mid - mx * g * amp), y1 = Mathf.Min(r.height, mid - mn * g * amp);
                    if (y1 - y0 < 1f) { y0 -= 0.5f; y1 += 0.5f; }
                    p2.MoveTo(new Vector2(x + 0.5f, y0)); p2.LineTo(new Vector2(x + 0.5f, y1));
                }
                p2.Stroke();
            }
            // Where the Gain lifts the audio past full scale, a red mark along both edges (what the effects receive
            // is louder than the file can hold).
            if (clipped.Count > 0) {
                p2.strokeColor = new Color(1f, 0.25f, 0.2f, 0.95f); p2.lineWidth = 1f;
                p2.BeginPath();
                foreach (float cx in clipped) { p2.MoveTo(new Vector2(cx, 0f)); p2.LineTo(new Vector2(cx, 3f)); p2.MoveTo(new Vector2(cx, r.height - 3f)); p2.LineTo(new Vector2(cx, r.height)); }
                p2.Stroke();
            }
        }
        readonly List<float> clipped = new List<float>();

        /// <summary>A sample's height on the waveform, as a share of the area's height per unit of amplitude: full scale
        /// reaches (just inside) the edge. The old waveform picture drew 0.75, so anything above two thirds of full scale
        /// was cut flat at the edge; with the Gain drawn into the waveform that hid exactly what it should show (a loud file
        /// looked the same at 100 % and 400 %), so full scale is the edge now. Never scaled to the file's own
        /// peak: a quiet file looks quiet.</summary>
        internal const float WaveHeightPerUnit = 0.48f;

        /// <summary>The sound's Gain as the engine applies it (1 for anything but a Klip): the waveform is drawn as the
        /// effects receive it.</summary>
        internal static float GainOf(Zound sound) => sound is Klip k ? k.BoostApplied : 1f;

        /// <summary>
        /// Draws each combined result in its curve's colour, lighter and thinner than the curve's own editable line, in the
        /// curve's own space. Where the modulation is denser than the pixels it is drawn the way a waveform is: one
        /// lowest-to-highest stroke per pixel column, so it stays legible at any zoom.
        /// </summary>
        void PaintCombined(MeshGenerationContext ctx) {
            var p = ctx.painter2D;
            var origin = combined.layout.position;
            foreach (var c in combinedLines) {
                var line = c.line;
                if (line.x == null || line.x.Length < 2) continue;
                float xr = Mathf.Max(1e-6f, c.xMax - c.xMin), yr = Mathf.Max(1e-6f, c.yMax - c.yMin);
                var rect = c.rect; float xMin = c.xMin, yMin = c.yMin;
                var toX = c.toX;
                Vector2 P(float x, float y) => new Vector2((toX != null ? toX(x) : rect.x + (x - xMin) / xr * rect.width) - origin.x,
                                                           rect.y - origin.y + (1f - (y - yMin) / yr) * rect.height);
                var col = c.colour; col.a = 0.55f;
                p.strokeColor = col;
                p.lineWidth = 1.25f;
                int cols = Mathf.Max(1, Mathf.CeilToInt(rect.width));
                if (line.x.Length > cols * 2) {
                    var lo = new float[cols]; var hi = new float[cols]; var has = new bool[cols];
                    for (int i = 0; i < line.x.Length; i++) {
                        int k = Mathf.Clamp(Mathf.FloorToInt((line.x[i] - xMin) / xr * cols), 0, cols - 1);
                        if (!has[k]) { lo[k] = hi[k] = line.y[i]; has[k] = true; }
                        else { if (line.y[i] < lo[k]) lo[k] = line.y[i]; if (line.y[i] > hi[k]) hi[k] = line.y[i]; }
                    }
                    p.BeginPath();
                    for (int k = 0; k < cols; k++) {
                        if (!has[k]) continue;
                        float x = xMin + (k + 0.5f) / cols * xr;
                        p.MoveTo(P(x, lo[k])); p.LineTo(P(x, hi[k]) + new Vector2(0f, -0.5f));
                    }
                    p.Stroke();
                }
                else {
                    p.BeginPath();
                    p.MoveTo(P(line.x[0], line.y[0]));
                    for (int i = 1; i < line.x.Length; i++) p.LineTo(P(line.x[i], line.y[i]));
                    p.Stroke();
                }
            }
        }

        static void Fill(Painter2D p2, float x, float y, float w, float h, Color c) {
            if (w <= 0f || h <= 0f) return;
            p2.fillColor = c;
            p2.BeginPath();
            p2.MoveTo(new Vector2(x, y)); p2.LineTo(new Vector2(x + w, y)); p2.LineTo(new Vector2(x + w, y + h)); p2.LineTo(new Vector2(x, y + h));
            p2.ClosePath(); p2.Fill();
        }

        // ─────────────────────────── editing the curves ───────────────────────────

        bool Guarded() => host.BeforeEdit();

        /// <summary>The start of an edit of one of the sound's curves: the Undo step, then the conversions that keep the
        /// sound as it was. Once per gesture.</summary>
        void BeginCurveEdit(Curve which) {
            if (editOpen) return;
            editOpen = true;
            host.BeginEditUndo(UndoName(which));
            KlipChainEnvelopes.BeginCurveEdit(Sound, (int)which);
        }

        void CurveChanged() {
            KlipChainEnvelopes.Touch(Sound);
            host.SoundChanged();
        }

        void CloseEdit() {
            if (!editOpen) return;
            editOpen = false;
            host.EndEditUndo();
        }

        // ─────────────────────────── input ───────────────────────────

        /// <summary>Kept check 34 only: receives a right-click's play instead of the host (so the check plays nothing).</summary>
        internal static Action<WaveSurfaceTK, float> playFromProbe;
        /// <summary>The host this surface draws for, and one of its curve layers (kept check 34).</summary>
        internal IWaveSurfaceHost Host => host;
        internal ZuiSkinEnvelope EnvelopeOf(Curve which) => envs[(int)which];

        /// <summary>The source second a right-click at <paramref name="x"/> plays from (shared by both hosts; kept check 34).</summary>
        internal float PlayFromSecondAt(float x) => Mathf.Clamp(host.SourceAt(x, AreaRect), 0f, host.FileLength);

        /// <summary>Plays only this sound from the second at <paramref name="x"/> (again while it sounds: stops). Every
        /// play-from-the-pointer gesture of either host comes through here.</summary>
        internal void PlayAt(float x) {
            if (playFromProbe != null) playFromProbe(this, PlayFromSecondAt(x)); else host.PlayFrom(PlayFromSecondAt(x));
        }

        void OnDown(PointerDownEvent e) {
            if (host.Source == null || Sound == null) return;
            var m = (Vector2)e.localPosition;
            var r = AreaRect;
            area.Focus();
            var sel = SelectedEnv(out var selWhich);
            var lm = sel != null ? area.ChangeCoordinatesTo(sel, m) : default;
            // A curve's first and last points sit exactly on the trim edges, so a press on a POINT goes to the curve (T-0507,
            // T-0508); the edges are still grabbed anywhere else along them.
            bool onPoint = sel != null && sel.IsOverPoint(lm);
            if (!onPoint && (e.button == 0 || e.button == 1)) {
                int h = HandleAt(m.x, r);
                if (h >= 0) {
                    // A right-drag on either edge moves both, keeping the length; a right-click without a drag opens that
                    // edge's random range instead (on release, see OnUp).
                    rightPress = e.button == 1; rightMoved = false; pressX = m.x; pressHandle = h;
                    StartTrim(e.button == 1 ? TrimDrag.Both : h == 0 ? TrimDrag.Start : TrimDrag.End, m.x, e);
                    return;
                }
            }
            if (e.button == 1) {
                // On the curve being edited: a point's random settings, or (with Shift) bending the line.
                bool onCurve = sel != null && (onPoint || (e.shiftKey && sel.FindSegmentIndexNear(lm) >= 0));
                if (onCurve) {
                    if (!Guarded()) { e.StopPropagation(); return; }
                    if (e.shiftKey) BeginCurveEdit(selWhich);
                    if (sel.PointerDown(lm, 1, e.clickCount, e.shiftKey)) { active = sel; area.CapturePointer(e.pointerId); e.StopPropagation(); return; }
                    CloseEdit();
                }
                // Anywhere else: the host's (a selection's menu), else play only this sound, from the second under the pointer.
                if (!host.ContextPress(e, m, r)) PlayAt(m.x);
                e.StopPropagation();
                return;
            }
            if (e.button == 0 && sel != null) {
                // The curve being edited takes presses on it (points, its line, a box selection): an edit of the sound, so
                // a shared one goes to a copy first; the curve objects stay the editor's own, so the press carries on.
                if (!Guarded()) { e.StopPropagation(); return; }
                bool mutates = onPoint || sel.FindSegmentIndexNear(lm) >= 0 || e.clickCount == 2;
                if (mutates) BeginCurveEdit(selWhich);
                if (sel.PointerDown(lm, 0, e.clickCount, e.shiftKey)) {
                    active = sel;
                    area.CapturePointer(e.pointerId);
                    e.StopPropagation();
                    return;
                }
                CloseEdit();
            }
            if (e.button == 0 && host.Press(e, m, r)) e.StopPropagation();
        }

        bool rightPress, rightMoved;
        float pressX;
        int pressHandle = -1;

        /// <summary>Kept checks: receives a right-click's request to open an edge's random range instead of the popup.</summary>
        internal static Action<WaveSurfaceTK, bool> trimRandomProbe;

        /// <summary>A trim edge's random range: an edit of the sound, so the host's guard first (a shared sound goes to a copy).</summary>
        void OpenTrimRandom(bool end) {
            if (trimRandomProbe != null) { trimRandomProbe(this, end); return; }
            if (!host.BeforeEdit()) return;
            TrimRandomPopupTK.Show(end ? handleEnd : handleStart, Sound, end, host.FileLength, () => { host.SoundChanged(); Refresh(); });
        }

        void StartTrim(TrimDrag which, float x, PointerDownEvent e) {
            // Moving the trim is an edit of the sound (the host's guard and Undo step).
            e.StopPropagation();
            if (!host.BeginTrim(which, x, AreaRect)) return;
            trimDragging = true;
            area.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e) {
            var m = (Vector2)e.localPosition;
            if (trimDragging) {
                // A right press only becomes a drag once it has moved (a click opens the edge's random range instead).
                if (rightPress && !rightMoved) { if (Mathf.Abs(m.x - pressX) < 3f) { e.StopPropagation(); return; } rightMoved = true; }
                host.DragTrim(m.x, AreaRect);
                Refresh();
                e.StopPropagation();
                return;
            }
            if (host.Move(m, AreaRect)) return;
            if (active != null) { active.PointerMove(area.ChangeCoordinatesTo(active, m), e.deltaPosition, e.shiftKey, e.pressedButtons); e.StopPropagation(); return; }
            foreach (var env in envs)
                if (env.resolvedStyle.display != DisplayStyle.None && env.rt != null)
                    env.PointerMove(area.ChangeCoordinatesTo(env, m), e.deltaPosition, e.shiftKey, e.pressedButtons);
            var sel = SelectedEnv(out _);
            SetHot(e.pressedButtons == 0 && !(sel != null && sel.IsOverPoint(area.ChangeCoordinatesTo(sel, m))) ? HandleAt(m.x, AreaRect) : -1);
        }

        void OnUp(PointerUpEvent e) {
            bool mine = area.HasPointerCapture(e.pointerId);
            if (mine) area.ReleasePointer(e.pointerId);
            host.Release();
            if (trimDragging) {
                trimDragging = false; host.EndTrim();
                if (rightPress && !rightMoved && host.TrimRandomEditable && Sound != null) OpenTrimRandom(pressHandle == 1);
                rightPress = false;
            }
            if (active != null) { active.PointerUp(); active = null; }
            CloseEdit();
            Refresh();
            if (mine) e.StopPropagation();
        }

        void OnKey(KeyDownEvent e) {
            if (host.Key(e)) { e.StopPropagation(); return; }
            var sel = SelectedEnv(out _);
            if (e.keyCode == KeyCode.Delete && sel != null && sel.SelectedPoints.Count > 0 && !Guarded()) { e.StopPropagation(); return; }
            bool used = false;
            foreach (var env in envs) used |= env.KeyDown(e.keyCode);
            CloseEdit();
            if (used) { e.StopPropagation(); Refresh(); }
        }

        // ─────────────────────────── the curve settings (both curve bars) ───────────────────────────

        /// <summary>
        /// A curve's own settings, on a right-click of its name in the curve bar (the Klip editor's and a track's alike): the
        /// extra time it keeps going after the audio ends (a tail), and, for the volume curve, its value range (the top and
        /// bottom of its drawing, as a factor of the sound's level: 0..1 fades, a top above 1 boosts; points keep their
        /// values, clamped into the new range). One undo step per change.
        /// </summary>
        internal static void ShowCurveSettings(Klip klip, Curve which, VisualElement anchor, Func<bool> guard, Action refresh) {
            Envelope Env() => which == Curve.Volume ? KlipChainEnvelopes.VolumeCurve(klip, false) : which == Curve.Pitch ? KlipChainEnvelopes.PitchCurve(klip, false)
                            : KlipChainEnvelopes.TimeCurve(klip, false);
            var env0 = Env();
            if (env0 == null || !env0.enabled || KlipChainEnvelopes.ModifierOf(klip, env0) == null) return;
            // Its settings are an edit of the sound: a shared one goes to a copy first, and the curves are re-read.
            if (guard != null && !guard()) return;
            var env = Env();
            var mod = env != null ? KlipChainEnvelopes.ModifierOf(klip, env) : null;
            if (env == null || !env.enabled || mod == null) return;
            Z.Popover(anchor, panel => {
                panel.AddToClassList("zs-curve-settings");
                var row = new VisualElement(); row.AddToClassList("zs-curve-settings__row");
                var extraLabel = new Label("Extra time") { tooltip = "Seconds the curve keeps going after the sound's audio ends, so it can shape a reverb or delay tail." };
                extraLabel.AddToClassList("zs-lbl"); extraLabel.AddToClassList("zs-curve-settings__label");
                row.Add(extraLabel);
                var extra = Z.Float(mod.p != null && mod.p.Length > 0 ? mod.p[0] : 0f, extraLabel.tooltip, v => {
                    ZoundsWindow.ModifyAndSaveZoundsProject("change curve extra time", () => {
                        if (mod.p == null || mod.p.Length == 0) return;
                        mod.p[0] = Mathf.Max(0f, v);
                        KlipChainEnvelopes.Touch(klip);
                        Dsp.ZoundDspPlayback.InvalidateLayout(klip);
                    });
                    refresh?.Invoke();
                }, 70f, 2);
                row.Add(extra);
                var unit = new Label("s"); unit.AddToClassList("zs-lbl"); unit.AddToClassList("zs-curve-settings__unit");
                row.Add(unit);
                panel.Add(row);
                if (which != Curve.Volume) return;
                var minmax = Z.MicroMinMax("Range", env.yMin, env.yMax, 0f, 10f,
                    "The lowest and highest value the volume curve can reach: the bottom and top of its drawing. 0 – 1 fades; a top above 1 boosts.",
                    (lo, hi) => {
                        if (hi <= lo + 0.01f) hi = lo + 0.01f;
                        ZoundsWindow.ModifyAndSaveZoundsProject("change volume curve range", () => {
                            env.yMin = lo; env.yMax = hi;
                            foreach (var p in env.GetPointsList()) p.value = Mathf.Clamp(p.value, lo, hi);
                            KlipChainEnvelopes.Touch(klip);
                        });
                        refresh?.Invoke();
                    }, 200f, true, 0f, 1f, 2);
                minmax.AddToClassList("zs-curve-settings__range");
                panel.Add(minmax);
            }, new ZuiPopover.Options { preferredSide = ZuiPopover.Side.Below });
        }
    }
}
