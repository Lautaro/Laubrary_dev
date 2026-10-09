using System;
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// UI Toolkit twin of the Klip editor's waveform block (AudioSpectrumView.DrawLayout, T-0468): the curve bar (the one
    /// shared with the Zequence tracks: Trim, then Time / Pitch / Vol each with on, edit and eye), Clamp and Keep length,
    /// the length, and the waveform area with its background, waveform image (whole clip, or the detailed visible range
    /// when zoomed), trim dims and handles, playheads, and the volume, pitch and time curves drawn over it. The area's
    /// height is the owner's to set, by the grip along its bottom edge.
    ///
    /// The old view object is the model: every rule (the view window, the zoom, which texture to show, trim geometry and
    /// drags, playhead positions, the overlay set-up) is its shared code, and its callbacks are wired by the old window's
    /// own <c>WireSpectrumView</c>. This element only draws and routes input — in the old order: wheel zoom, the right-
    /// drag on either trim handle (moves both), the end handle, the start handle, then the curve selected for editing (the
    /// others are backdrops: half transparent, twice as wide, no handles, no input).
    /// </summary>
    public partial class KlipWaveformTK : VisualElement {

        /// <summary>The waveform area's height: the owner sets it by dragging the grip along the bottom (2026-10-08), and it is
        /// kept per machine, for every Klip editor.</summary>
        const string HeightKey = "Laubrary.Zounds.KlipWaveformHeight";
        const float MinAreaH = 60f, MaxAreaH = 800f, DefaultAreaH = 150f;
        internal static float AreaH {
            get => Mathf.Clamp(EditorPrefs.GetFloat(HeightKey, DefaultAreaH), MinAreaH, MaxAreaH);
            set => EditorPrefs.SetFloat(HeightKey, Mathf.Clamp(value, MinAreaH, MaxAreaH));
        }
        readonly AudioSpectrumView model;
        readonly Klip klip;
        // The one curve toolbar (shared with the Zequence tracks), then the two Klip-only switches.
        readonly CurveBarTK bar;
        readonly ZuiToggleButton clamp, keepLen;
        bool pitchOldScale;
        readonly VisualElement grip;
        float gripDownY, gripDownH;
        // What each shown curve's value actually does once every modifier on it is applied (T-0494), drawn lighter.
        readonly VisualElement combined;
        readonly List<(Color colour, OwnValueCurves.Line line, Rect rect, float xMin, float xMax, float yMin, float yMax)> combinedLines
            = new List<(Color colour, OwnValueCurves.Line line, Rect rect, float xMin, float xMax, float yMin, float yMax)>();
        readonly Label length, live;
        readonly VisualElement box, area, bg, dimStart, dimEnd, handleStart, handleEnd, heads;
        readonly VisualElement[] xmix;
        // The pitch curve's axis (T-0479): top / middle / bottom labels and the "no change" line.
        readonly Label pitchTop, pitchMid, pitchBottom;
        readonly VisualElement pitchLine;
        readonly Image wave;
        readonly ZuiSkinEnvelope volEnv, pitchEnv, timeEnv;
        // The time curve's axis on the right (T-0482): x4 / x1 / x1/4 speed and the "unchanged" line.
        readonly Label timeTop, timeMid, timeBottom;
        readonly VisualElement timeLine;
        ZuiSkinEnvelope active;
        bool trimDragging;

        // ── destructive editing (2026-10-09): the shared-sound guard and its notice ──
        /// <summary>Asked before any edit of the sound's trim or curves begins; false cancels it (the host's shared-sound guard).</summary>
        public Func<bool> beforeEdit;
        internal SwapNoticeTK notice;

        bool Guarded() => beforeEdit == null || beforeEdit();

        readonly List<VisualElement> headPool = new List<VisualElement>();
        readonly List<float> headWeights = new List<float>();

        public KlipWaveformTK(AudioSpectrumView model, Klip klip) {
            this.model = model; this.klip = klip;
            AddToClassList("zs-klip-waveform__root");
            // One curve at a time is selected for editing (the others are backdrops); the model starts with every
            // curve's handles on, which would show every point, so a fresh view starts with none selected.
            int selectedCount = (model.ShowVolumeHandles ? 1 : 0) + (model.ShowPitchHandles ? 1 : 0) + (model.ShowTimeHandles ? 1 : 0);
            if (selectedCount > 1) Select(AudioSpectrumView.Curve.Volume, false);

            // ── toolbar: the shared curve bar, then Clamp and Keep length (Klip-only), then the length ──
            var row = new VisualElement();
            row.AddToClassList("zs-klip-waveform__bar"); row.style.height = CurveBarTK.H;
            bar = new CurveBarTK(Curves(),
                () => model.TrimEnabled, v => { if (!Guarded()) { bar.Sync(); return; } model.SetTrimEnabled(v); Refresh(); },
                () => model.TrimEnabled ? "Source trim is active. Click to use the whole recording." : "Source trim is off. Click to use the authored start and end points.",
                () => model.ShowTimeHandles ? 0 : model.ShowPitchHandles ? 1 : model.ShowVolumeHandles ? 2 : -1,
                i => { Select(i == 0 ? AudioSpectrumView.Curve.Time : i == 1 ? AudioSpectrumView.Curve.Pitch : AudioSpectrumView.Curve.Volume, i >= 0); Refresh(); },
                ZoundsProject.Instance.projectSettings.editorStyle.trimHandleColor);
            row.Add(bar);
            row.Add(Gap(8f));
            clamp = ZS.Toggle("Clamp", "", model.ClampToTrim, v => { if (!Guarded()) { clamp.SetValueWithoutNotify(model.ClampToTrim); return; } model.SetClampToTrim(v); Refresh(); }, "RichToggle", ZUICornerMask.All, 46f, CurveBarTK.H);
            clamp.AddToClassList("zs-curvebar__toggle");
            row.Add(clamp);
            // Keep length (on the pitch curve), T-0482. Present only for a Klip's chain curves.
            keepLen = ZS.Toggle("Keep length", AudioSpectrumView.KeepLengthTip(model.KeepLength), model.KeepLength,
                v => { if (!Guarded()) { keepLen.SetValueWithoutNotify(model.KeepLength); return; } model.RequestKeepLength(v); keepLen.tooltip = AudioSpectrumView.KeepLengthTip(v); Refresh(); }, "RichToggle", ZUICornerMask.All, 72f, CurveBarTK.H);
            keepLen.AddToClassList("zs-curvebar__toggle");
            if (model.HasKlip) { row.Add(Gap(4f)); row.Add(keepLen); }
            // The sound's own values as the newest play hears them (curves and modulators included), while it plays. The
            // slot is reserved (hidden, not removed) so the bar never reflows when a play starts or stops.
            live = new Label { tooltip = "What the newest play of this sound hears right now for its own values: the volume, the pitch in semitones and the speed, after its curves, modulators and game-code values. Shown only while it plays." };
            live.AddToClassList("zs-lbl"); live.AddToClassList("zs-mini"); live.AddToClassList("zs-klip-waveform__live");
            live.style.visibility = Visibility.Hidden;
            row.Add(Gap(8f)); row.Add(live);
            length = new Label();
            length.AddToClassList("zs-lbl"); length.AddToClassList("zs-mini");
            length.AddToClassList("zs-klip-waveform__length");
            var flex = new VisualElement(); flex.AddToClassList("zs-klip-waveform__flex"); row.Add(flex);
            row.Add(length);
            Add(row);
            Add(Space(3f));
            AddEditBar();   // the audio edit bar (destructive editing, 2026-10-09)

            // ── the waveform area: GUI.Box, then the picture inset by 4 ──
            box = new VisualElement();
            box.AddToClassList("zs-waveform-box");
            box.AddToClassList("zs-klip-waveform__box");
            box.style.height = AreaH;
            model.height = AreaH;
            Add(box);
            area = new VisualElement { tooltip = "Click to place the edit cursor; drag to select audio (Shift+click extends the selection) for the edit bar above. Mouse wheel: zoom within the trimmed range." };
            area.AddToClassList("zs-waveform-area");
            area.AddToClassList("zs-klip-waveform__area");
            // Not clipped: the old view lets an envelope's end handles spill past the picture's edge, and so does this.
            box.Add(area);
            bg = Abs(); area.Add(bg);
            wave = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            wave.AddToClassList("zs-klip-waveform__wave");
            area.Add(wave);
            dimStart = Abs(); dimEnd = Abs(); area.Add(dimStart); area.Add(dimEnd);
            // A Looper's crossmix spans (T-0476): the longest possible span, and the part every loop uses, at both ends.
            xmix = new VisualElement[4];
            for (int i = 0; i < 4; i++) {
                xmix[i] = Abs();
                xmix[i].AddToClassList("zs-xmix");
                if (i >= 2) xmix[i].AddToClassList("zs-xmix--min");
                area.Add(xmix[i]);
            }
            pitchLine = Abs(); area.Add(pitchLine);
            pitchTop = AxisLabel(); pitchMid = AxisLabel(); pitchBottom = AxisLabel();
            area.Add(pitchTop); area.Add(pitchMid); area.Add(pitchBottom);
            timeLine = Abs(); area.Add(timeLine);
            timeTop = AxisLabel(); timeMid = AxisLabel(); timeBottom = AxisLabel();
            foreach (var l in new[] { timeTop, timeMid, timeBottom }) { l.AddToClassList("zs-klip-waveform__time-axis-label"); area.Add(l); }
            timeTop.text = "×4"; timeMid.text = "×1"; timeBottom.text = "×¼";
            // The combined results, under the playheads and the editable curves (T-0494).
            combined = Abs(); combined.pickingMode = PickingMode.Ignore;
            combined.generateVisualContent += PaintCombined;
            area.Add(combined);
            AddEditMarks();   // the edit selection and cursor (apart from the playheads), under the handles and curves
            heads = Abs(); area.Add(heads);
            handleStart = Abs(); handleEnd = Abs(); area.Add(handleStart); area.Add(handleEnd);
            handleStart.AddToClassList("zs-trimhandle"); handleEnd.AddToClassList("zs-trimhandle");
            volEnv = new ZuiSkinEnvelope(null, Color.white, null, null, standalone: false) { pickingMode = PickingMode.Ignore };
            pitchEnv = new ZuiSkinEnvelope(null, Color.white, null, null, standalone: false) { pickingMode = PickingMode.Ignore };
            timeEnv = new ZuiSkinEnvelope(null, Color.white, null, null, standalone: false) { pickingMode = PickingMode.Ignore };
            foreach (var e in new[] { volEnv, pitchEnv, timeEnv }) { e.AddToClassList("zs-klip-waveform__envelope"); area.Add(e); }
            // Right-click a point: its random settings (T-0483). The pitch curve moves off its old scale first, inside
            // the same Undo step, as any other edit of it does.
            foreach (var e in new[] { volEnv, pitchEnv, timeEnv }) {
                var env = e;
                env.onPointContext = (i, world) => {
                    if (env.points == null || i < 0 || i >= env.points.Count || env.rt == null) return;
                    var sel = new List<ZUIEnvelopePoint>();
                    foreach (int s in env.SelectedPoints) if (s >= 0 && s < env.points.Count) sel.Add(env.points[s]);
                    int which = env == volEnv ? 0 : env == pitchEnv ? 1 : 2;
                    RandomPointPopup.Show(world, env.points[i], Mathf.Max(env.rt.dataXMax - env.rt.dataXMin, 1e-3f),
                        () => env.rt.yMax - env.rt.yMin,
                        () => { if (env == pitchEnv) KlipChainEnvelopes.EnsurePitchRatio(klip); if (env == volEnv) KlipChainEnvelopes.EnsureVolumeOwnValue(klip); },
                        () => { KlipChainEnvelopes.Touch(klip); Refresh(); },
                        sel,
                        () => KlipChainEnvelopes.WaveformCurveNeutral(klip, which, out float v) ? v : (float?)null,
                        () => new Vector2(env.rt.yMin, env.rt.yMax));
                };
            }
            area.focusable = true;

            // The grip along the bottom edge: drag it to make the waveform taller or shorter.
            grip = new VisualElement { tooltip = "Drag to change the waveform's height." };
            grip.AddToClassList("zs-klip-waveform__grip");
            grip.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; gripDownY = e.position.y; gripDownH = AreaH; grip.CapturePointer(e.pointerId); e.StopPropagation(); });
            grip.RegisterCallback<PointerMoveEvent>(e => {
                if (!grip.HasPointerCapture(e.pointerId)) return;
                AreaH = gripDownH + (e.position.y - gripDownY);
                box.style.height = AreaH; model.height = AreaH;
                Refresh();
            });
            grip.RegisterCallback<PointerUpEvent>(e => { if (grip.HasPointerCapture(e.pointerId)) grip.ReleasePointer(e.pointerId); });
            box.Add(grip);
            // The "Tell me" notice floats over the top of the waveform, so it never moves anything.
            notice = new SwapNoticeTK();
            notice.AddToClassList("zs-klip-waveform__notice");
            box.Add(notice);

            area.RegisterCallback<WheelEvent>(OnWheel);
            area.RegisterCallback<PointerDownEvent>(OnDown);
            area.RegisterCallback<PointerMoveEvent>(OnMove);
            area.RegisterCallback<PointerUpEvent>(OnUp);
            area.RegisterCallback<PointerLeaveEvent>(_ => { volEnv.PointerLeft(); pitchEnv.PointerLeft(); timeEnv.PointerLeft(); });
            area.RegisterCallback<KeyDownEvent>(OnKey);
            area.RegisterCallback<GeometryChangedEvent>(_ => Refresh());
            schedule.Execute(Refresh).Every(33);
        }

        /// <summary>The three curves as the shared bar sees them: on / selected / shown, all read from and written to the model.</summary>
        List<CurveBarTK.Curve> Curves() {
            var es = ZoundsProject.Instance.projectSettings.editorStyle;
            return new List<CurveBarTK.Curve> {
                new CurveBarTK.Curve {
                    label = "Time", colour = AudioSpectrumView.TimeCurveColor,
                    enabled = () => model.TimeEnvelope.enabled, setEnabled = v => { if (!Guarded()) { bar.Sync(); return; } model.RequestTimeEnabled(v); Refresh(); },
                    shown = () => CurveView.IsVisible(ModifierOf(AudioSpectrumView.Curve.Time)), setShown = v => { CurveView.SetVisible(ModifierOf(AudioSpectrumView.Curve.Time), v); Refresh(); },
                    onContext = a => ShowCurveSettings(AudioSpectrumView.Curve.Time, a),
                },
                new CurveBarTK.Curve {
                    label = "Pitch", colour = es.pitchEnvelopeColor,
                    enabled = () => model.PitchEnvelope.enabled, setEnabled = v => { if (!Guarded()) { bar.Sync(); return; } model.RequestPitchEnabled(v); Refresh(); },
                    shown = () => CurveView.IsVisible(ModifierOf(AudioSpectrumView.Curve.Pitch)), setShown = v => { CurveView.SetVisible(ModifierOf(AudioSpectrumView.Curve.Pitch), v); Refresh(); },
                    warn = () => pitchOldScale, warnTip = KlipChainEnvelopes.OldScaleTip,
                    onContext = a => ShowCurveSettings(AudioSpectrumView.Curve.Pitch, a),
                },
                new CurveBarTK.Curve {
                    label = "Vol", colour = es.volumeEnvelopeColor,
                    enabled = () => model.VolumeEnvelope.enabled, setEnabled = v => { if (!Guarded()) { bar.Sync(); return; } model.RequestVolumeEnabled(v); Refresh(); },
                    shown = () => CurveView.IsVisible(ModifierOf(AudioSpectrumView.Curve.Volume)), setShown = v => { CurveView.SetVisible(ModifierOf(AudioSpectrumView.Curve.Volume), v); Refresh(); },
                    onContext = a => ShowCurveSettings(AudioSpectrumView.Curve.Volume, a),
                },
            };
        }

        /// <summary>
        /// A curve's own settings, on a right-click of its name in the bar (the sound's curves are not modifier cards any
        /// more, owner 2026-10-08): the extra time it keeps going after the audio ends (a tail), and, for the volume
        /// curve, its value range (the top and bottom of its drawing, as a factor of the sound's level: 0..1 fades, a top
        /// above 1 boosts; points keep their values, clamped into the new range). One undo step per change.
        /// </summary>
        void ShowCurveSettings(AudioSpectrumView.Curve which, VisualElement anchor) {
            if (EnvelopeOf(which) == null || !EnvelopeOf(which).enabled || ModifierOf(which) == null) return;
            // Its settings are an edit of the sound: a shared one goes to a copy first, and the editor's curves are re-read.
            if (!Guarded()) return;
            var env = EnvelopeOf(which);
            var mod = ModifierOf(which);
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
                    Refresh();
                }, 70f, 2);
                row.Add(extra);
                var unit = new Label("s"); unit.AddToClassList("zs-lbl"); unit.AddToClassList("zs-curve-settings__unit");
                row.Add(unit);
                panel.Add(row);
                if (which != AudioSpectrumView.Curve.Volume) return;
                var minmax = Z.MicroMinMax("Range", env.yMin, env.yMax, 0f, 10f,
                    "The lowest and highest value the volume curve can reach: the bottom and top of its drawing. 0 – 1 fades; a top above 1 boosts.",
                    (lo, hi) => {
                        if (hi <= lo + 0.01f) hi = lo + 0.01f;
                        ZoundsWindow.ModifyAndSaveZoundsProject("change volume curve range", () => {
                            env.yMin = lo; env.yMax = hi;
                            foreach (var p in env.GetPointsList()) p.value = Mathf.Clamp(p.value, lo, hi);
                            KlipChainEnvelopes.Touch(klip);
                        });
                        Refresh();
                    }, 200f, true, 0f, 1f, 2);
                minmax.AddToClassList("zs-curve-settings__range");
                panel.Add(minmax);
            }, new ZuiPopover.Options { preferredSide = ZuiPopover.Side.Below });
        }

        static VisualElement Gap(float w) { var e = new VisualElement(); e.style.width = w; e.AddToClassList("zs-klip-waveform__gap"); return e; }

        // The audio edits' parts, in KlipWaveformTK.AudioEdits.cs (destructive editing, 2026-10-09).
        partial void AddEditBar();
        partial void AddEditMarks();
        partial void PlaceEditMarks(Rect r);
        partial void EditKey(KeyDownEvent e, ref bool handled);
        partial void EditPress(PointerDownEvent e, Vector2 m, Rect r, ref bool handled);
        partial void EditMove(Vector2 m, ref bool handled);
        partial void EditRelease();

        void OnKey(KeyDownEvent e) {
            bool handled = false;
            EditKey(e, ref handled);
            if (handled) { e.StopPropagation(); return; }
            // Delete removes the selected curve points: an edit of the sound, so a shared one goes to a copy first.
            if (e.keyCode == KeyCode.Delete && AnySelected && !Guarded()) { e.StopPropagation(); return; }
            if (volEnv.KeyDown(e.keyCode) | pitchEnv.KeyDown(e.keyCode) | timeEnv.KeyDown(e.keyCode)) e.StopPropagation();
        }

        static Label AxisLabel() {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.AddToClassList("zs-lbl"); l.AddToClassList("zs-mini");
            l.AddToClassList("zs-klip-waveform__axis-label");
            // A dark backing so a label reads on the bright waveform as well as on the dimmed parts.
            l.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            return l;
        }

        /// <summary>The time curve's axis on the right of the envelope area (T-0482).</summary>
        void PlaceTimeAxis(Rect envRect) {
            bool on = model.TimeEnvelope.enabled && CurveView.IsVisible(ModifierOf(AudioSpectrumView.Curve.Time));
            foreach (var e in new VisualElement[] { timeTop, timeMid, timeBottom, timeLine }) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) return;
            var c = AudioSpectrumView.TimeCurveColor;
            foreach (var l in new[] { timeTop, timeMid, timeBottom }) { l.style.color = c; l.style.left = envRect.xMax - 29f; }
            timeTop.style.top = envRect.y + 1f;
            timeBottom.style.top = envRect.yMax - 14f;
            float my = Mathf.Round(envRect.y + envRect.height * 0.5f);
            timeMid.style.top = my - 14f;
            timeLine.style.left = envRect.x; timeLine.style.width = Mathf.Max(0f, envRect.width);
            timeLine.style.top = my; timeLine.AddToClassList("zs-klip-waveform__place-time-axis-time-line");
            timeLine.style.backgroundColor = new Color(c.r, c.g, c.b, 0.3f);
        }

        /// <summary>The pitch curve's axis on the waveform (T-0479): what its top, middle and bottom mean, and the middle
        /// line that is "no change" -- or, for a curve still on its old scale, only the warning beside the toggle.</summary>
        void PlacePitchAxis(Rect r, Rect envRect) {
            string top = null, mid = null, bottom = null; bool oldScale = false;
            bool on = model.PitchEnvelope.enabled && KlipChainEnvelopes.PitchAxis(klip, out top, out mid, out bottom, out oldScale);
            bool old = on && oldScale;
            pitchOldScale = old;
            bool axis = on && !old && top != null && CurveView.IsVisible(ModifierOf(AudioSpectrumView.Curve.Pitch));
            foreach (var e in new VisualElement[] { pitchTop, pitchMid, pitchBottom }) e.style.display = axis ? DisplayStyle.Flex : DisplayStyle.None;
            pitchLine.style.display = axis && mid != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (!axis) return;
            var c = ZoundsProject.Instance.projectSettings.editorStyle.pitchEnvelopeColor;
            foreach (var l in new[] { pitchTop, pitchMid, pitchBottom }) l.style.color = new Color(c.r, c.g, c.b, 0.9f);
            pitchTop.text = top; pitchMid.text = mid ?? ""; pitchBottom.text = bottom;
            float x = envRect.x + 3f;
            pitchTop.style.left = pitchMid.style.left = pitchBottom.style.left = x;
            pitchTop.style.top = envRect.y + 1f;
            pitchBottom.style.top = envRect.yMax - 14f;
            float my = Mathf.Round(envRect.y + envRect.height * 0.5f);
            pitchMid.style.top = my - 14f;
            pitchLine.style.left = envRect.x; pitchLine.style.width = Mathf.Max(0f, envRect.width);
            pitchLine.style.top = my; pitchLine.AddToClassList("zs-klip-waveform__place-pitch-axis-pitch-line");
            pitchLine.style.backgroundColor = new Color(c.r, c.g, c.b, 0.35f);
        }
        static VisualElement Space(float h) { var e = new VisualElement(); e.style.height = h; e.AddToClassList("zs-klip-waveform__space"); return e; }
        static VisualElement Abs() {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("zs-klip-waveform__positioned-control");
            return e;
        }

        /// <summary>
        /// Selecting a curve for editing (its pencil): one at a time, so a press can only ever grab the curve you chose
        /// (T-0494). Before, every curve with its pencil on accepted presses in a fixed order and the wrong one could win.
        /// </summary>
        void Select(AudioSpectrumView.Curve which, bool on) {
            model.SetShowVolumeHandles(on && which == AudioSpectrumView.Curve.Volume);
            model.SetShowPitchHandles(on && which == AudioSpectrumView.Curve.Pitch);
            model.SetShowTimeHandles(on && which == AudioSpectrumView.Curve.Time);
        }

        Envelope EnvelopeOf(AudioSpectrumView.Curve which) =>
            which == AudioSpectrumView.Curve.Volume ? model.VolumeEnvelope : which == AudioSpectrumView.Curve.Pitch ? model.PitchEnvelope : model.TimeEnvelope;

        /// <summary>The chain modifier a waveform curve is, or null (an overlay with no modifier yet).</summary>
        ZoundModifier ModifierOf(AudioSpectrumView.Curve which) => KlipChainEnvelopes.ModifierOf(klip, EnvelopeOf(which));

        bool Selected(AudioSpectrumView.Curve which) =>
            which == AudioSpectrumView.Curve.Volume ? model.ShowVolumeHandles : which == AudioSpectrumView.Curve.Pitch ? model.ShowPitchHandles : model.ShowTimeHandles;

        bool AnySelected => model.ShowVolumeHandles || model.ShowPitchHandles || model.ShowTimeHandles;

        Rect AreaRect => new Rect(0f, 0f, area.contentRect.width, area.contentRect.height);

        // ─────────────────────────── refresh ───────────────────────────

        public void Refresh() {
            if (panel == null) return;
            var clip = model.OriginalClip;
            style.display = clip == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (clip == null) return;
            model.BeginFrame(false);
            var es = ZoundsProject.Instance.projectSettings.editorStyle;

            clamp.SetValueWithoutNotify(model.ClampToTrim);
            length.text = model.LengthText;
            string heard = klip != null ? KlipChainEnvelopes.LiveReadout(klip) : "";
            if (live.text != heard) live.text = heard;
            live.style.visibility = heard.Length > 0 ? Visibility.Visible : Visibility.Hidden;
            clamp.tooltip = model.ClampToTrim ? "Curves span the trimmed source. Click to span the whole recording." : "Curves span the whole recording. Click to span the trimmed source.";

            var r = AreaRect;
            if (r.width <= 1f || r.height <= 1f) { bar.Sync(); return; }
            bg.AddToClassList("zs-klip-waveform__refresh-bg"); bg.style.width = r.width; bg.style.height = r.height;
            bg.style.backgroundColor = es.klipWaveformBGColor;

            // The IMGUI view sizes its texture in whole points of its rect; the same numbers here give the same texture.
            var tex = model.WaveformImage(Mathf.FloorToInt(r.width), Mathf.FloorToInt(r.height), true, out var uv);
            if (wave.image != tex) wave.image = tex;
            wave.uv = uv;

            Rect trimmed = r;
            bool trimOn = model.TrimEnabled;
            dimStart.style.display = dimEnd.style.display = handleStart.style.display = handleEnd.style.display = trimOn ? DisplayStyle.Flex : DisplayStyle.None;
            if (trimOn) {
                var hs = model.TrimStartHandle(r, out var ds);
                var he = model.TrimEndHandle(r, out var de);
                Place(dimStart, ds); Place(dimEnd, de);
                dimStart.style.backgroundColor = dimEnd.style.backgroundColor = es.trimAreaColor;
                model.TrimHandlesLive(out bool startLive, out bool endLive);
                var hc = es.trimHandleColor;
                if (!enabledInHierarchy) hc.a = 0.35f;
                Place(handleStart, hs); Place(handleEnd, he);
                handleStart.style.backgroundColor = handleEnd.style.backgroundColor = hc;
                handleStart.style.display = startLive ? DisplayStyle.Flex : DisplayStyle.None;
                handleEnd.style.display = endLive ? DisplayStyle.Flex : DisplayStyle.None;
                trimmed = Rect.MinMaxRect(model.TimeToXIn(klip.trimStart, r), r.y, model.TimeToXIn(klip.trimEnd, r), r.yMax);
            }

            PlaceCrossmix(r, clip.length);
            PlaceEditMarks(r);

            // Playheads, drawn over the dims and under the envelopes, as the old view draws them.
            ZoundEngine.CullingGroups.TryGetValue(klip, out var playing);
            // Where each play is READING its source (T-0493), so the line stays on the sound heard whatever pitch, speed
            // or a time curve do; a Looper's copy fading out of a crossmix is drawn fainter.
            var fractions = model.PlayheadFractions(playing, out _, headWeights);
            Place(heads, r);
            for (int i = 0; i < fractions.Count; i++) {
                if (i >= headPool.Count) headPool.Add(PlayHead());
                var h = headPool[i];
                if (h.parent != heads) heads.Add(h);
                h.style.display = DisplayStyle.Flex;
                h.style.opacity = i < headWeights.Count ? headWeights[i] : 1f;
                h.style.left = trimmed.x + fractions[i] * trimmed.width;
                h.style.top = trimmed.y; h.style.height = trimmed.height;
            }
            for (int i = fractions.Count; i < headPool.Count; i++) headPool[i].style.display = DisplayStyle.None;

            // The envelopes, over the trimmed range when clamped, else the whole area.
            var envRect = model.ClampToTrim ? trimmed : r;
            PlacePitchAxis(r, envRect);
            PlaceTimeAxis(envRect);
            combinedLines.Clear();
            Overlay(volEnv, AudioSpectrumView.Curve.Volume, envRect);
            Overlay(pitchEnv, AudioSpectrumView.Curve.Pitch, envRect);
            Overlay(timeEnv, AudioSpectrumView.Curve.Time, envRect);
            // The curve being edited draws over the backdrops.
            foreach (var e in new[] { volEnv, pitchEnv, timeEnv }) if (e.showHandles) e.BringToFront();
            Place(combined, r);
            combined.MarkDirtyRepaint();
            if (model.HasKlip) keepLen.SetValueWithoutNotify(model.KeepLength);
            bar.Sync();
        }

        void Overlay(ZuiSkinEnvelope env, AudioSpectrumView.Curve which, Rect rect) {
            var def = model.PrepareOverlay(which, out var runtime, out var pts, out var colour);
            var mod = ModifierOf(which);
            bool shown = CurveView.IsVisible(mod);
            bool selected = Selected(which);
            // Eye off: not drawn -- unless it is the curve selected for editing, whose points stay (T-0494).
            bool draw = def != null && (shown || selected);
            env.style.display = draw ? DisplayStyle.Flex : DisplayStyle.None;
            if (!draw) return;
            env.points = pts; env.def = def; env.rt = runtime; env.curveColor = colour;
            // Only the curve selected for editing shows its points; while one is being edited the others step back as
            // backdrops (half transparent, twice as wide), owner's request 2026-10-08.
            env.showHandles = selected;
            env.backdrop = AnySelected && !selected;
            // What the plays under way hear, dotted (T-0484); nothing while the sound is not playing, or while hidden.
            var authored = EnvelopeOf(which);
            if (shown) LiveDrawnCurves.Fill(ref env.liveCurves, klip, authored, KlipChainEnvelopes.ModifierIndexOf(klip, authored));
            else env.liveCurves = null;
            Place(env, rect);
            env.Repaint();
            // The combined result of everything on this curve's value, when something besides the curve moves it.
            if (shown && OwnValueCurves.TryGet(klip, mod, out var line))
                combinedLines.Add((colour, line, rect, runtime.xMin, runtime.xMax, runtime.yMin, runtime.yMax));
        }

        /// <summary>
        /// Draws each combined result in its curve's colour, lighter and thinner than the curve's own editable line, in the
        /// curve's own space. Where the modulation is denser than the pixels (a fast oscillator over a long sound) it is drawn
        /// the way a waveform is: one lowest-to-highest stroke per pixel column, so it stays legible at any zoom.
        /// </summary>
        void PaintCombined(MeshGenerationContext ctx) {
            var p = ctx.painter2D;
            var origin = combined.layout.position;
            foreach (var c in combinedLines) {
                var line = c.line;
                if (line.x == null || line.x.Length < 2) continue;
                float xr = Mathf.Max(1e-6f, c.xMax - c.xMin), yr = Mathf.Max(1e-6f, c.yMax - c.yMin);
                var rect = c.rect; float xMin = c.xMin, yMin = c.yMin;
                Vector2 P(float x, float y) => new Vector2(rect.x - origin.x + (x - xMin) / xr * rect.width,
                                                           rect.y - origin.y + (1f - (y - yMin) / yr) * rect.height);
                var col = c.colour; col.a = 0.55f;
                p.strokeColor = col;
                p.lineWidth = 1.25f;
                int cols = Mathf.Max(1, Mathf.CeilToInt(rect.width));
                if (line.x.Length > cols * 2) {
                    // Dense: per pixel column, from the lowest to the highest value within it.
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

        /// <summary>
        /// Shades the parts of a Looper's source its crossmix uses: at the start, what the incoming copy fades in over;
        /// at the end, what the outgoing copy fades out over. The faint band is the longest crossmix the range allows,
        /// the stronger one inside it the shortest, so a fixed length shows as one strong band and a random range shows
        /// how far it can reach. Bounds are the ones the engine hears (limited to half the loop).
        /// </summary>
        void PlaceCrossmix(Rect r, float clipLength) {
            bool show = klip.IsLooper;
            float from = 0f, to = 0f, lo = 0f, hi = 0f;
            if (show) {
                from = klip.trimEnabled ? klip.trimStart : 0f;
                to = klip.trimEnabled && klip.trimEnd > klip.trimStart ? Mathf.Min(klip.trimEnd, clipLength) : clipLength;
                klip.loop.Effective(to - from, out lo, out hi);
                show = hi > 0f;
            }
            for (int i = 0; i < 4; i++) xmix[i].style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            float x0 = model.TimeToXIn(from, r), x1 = model.TimeToXIn(to, r);
            Place(xmix[0], Rect.MinMaxRect(x0, r.y, model.TimeToXIn(from + hi, r), r.yMax));
            Place(xmix[1], Rect.MinMaxRect(model.TimeToXIn(to - hi, r), r.y, x1, r.yMax));
            xmix[2].style.display = xmix[3].style.display = lo > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            Place(xmix[2], Rect.MinMaxRect(x0, r.y, model.TimeToXIn(from + lo, r), r.yMax));
            Place(xmix[3], Rect.MinMaxRect(model.TimeToXIn(to - lo, r), r.y, x1, r.yMax));
        }

        static void Place(VisualElement e, Rect r) {
            e.style.left = r.x; e.style.top = r.y; e.style.width = Mathf.Max(0f, r.width); e.style.height = Mathf.Max(0f, r.height);
        }

        VisualElement PlayHead() {
            var root = Abs();
            root.AddToClassList("zs-klip-waveform__play-head-root");
            var cursor = new Image { image = AudioWaveformUtility.playerHeadTexture, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore, tintColor = AudioWaveformUtility.playerHeadColor };
            var sz = AudioWaveformUtility.playerHeadSize;
            cursor.AddToClassList("zs-klip-waveform__play-head-cursor"); cursor.style.left = -sz.x / 2f; cursor.style.width = sz.x; cursor.style.height = sz.x * 1.82f;
            var line = Abs();
            float th = AudioWaveformUtility.playerHeadThickness;
            line.style.left = -th * 0.5f; line.style.width = th; line.style.top = sz.y; line.AddToClassList("zs-klip-waveform__play-head-line");
            line.style.backgroundColor = AudioWaveformUtility.playerHeadColor;
            root.Add(line); root.Add(cursor);
            return root;
        }

        // ─────────────────────────── input ───────────────────────────

        void OnWheel(WheelEvent e) {
            var r = AreaRect;
            if (model.WheelZoom(e.localMousePosition.x, r, e.delta.y)) { Refresh(); e.StopPropagation(); }
        }

        void OnDown(PointerDownEvent e) {
            var m = (Vector2)e.localPosition;
            var r = AreaRect;
            model.BeginFrame(true);
            area.Focus();
            // A curve's first and last points sit exactly on the trim handles, so a press on a POINT goes to the curve: the
            // trim handles checked first used to take every press on the first point (measured 2026-09-30: right-click gave
            // no menu, a click started a trim drag; T-0507, T-0508). The handles are still grabbed anywhere else along them.
            bool onPoint = false;
            foreach (var env in new[] { timeEnv, volEnv, pitchEnv })
                if (env.resolvedStyle.display != DisplayStyle.None && env.rt != null && env.IsOverPoint(area.ChangeCoordinatesTo(env, m))) { onPoint = true; break; }
            if (model.TrimEnabled && !onPoint) {
                var hs = model.TrimStartHandle(r, out _);
                var he = model.TrimEndHandle(r, out _);
                model.TrimHandlesLive(out bool startLive, out bool endLive);
                float time = model.XToTimeIn(m.x, r);
                // Right-drag on either thin handle (with a little slop) moves both.
                if (e.button == 1) {
                    const float slop = 3f;
                    if (new Rect(hs.x - slop, hs.y, hs.width + slop * 2f, hs.height).Contains(m) ||
                        new Rect(he.x - slop, he.y, he.width + slop * 2f, he.height).Contains(m)) {
                        StartTrim(AudioSpectrumView.TrimDrag.Both, time, e); return;
                    }
                }
                if (endLive && he.Contains(m) && (e.button == 0 || e.button == 1)) { StartTrim(e.button == 0 ? AudioSpectrumView.TrimDrag.End : AudioSpectrumView.TrimDrag.Both, time, e); return; }
                if (startLive && hs.Contains(m) && (e.button == 0 || e.button == 1)) { StartTrim(e.button == 0 ? AudioSpectrumView.TrimDrag.Start : AudioSpectrumView.TrimDrag.Both, time, e); return; }
            }
            // A curve being edited takes presses on it (points, its line, a box selection): an edit of the sound, so a
            // shared one goes to a copy first. The curve objects stay the editor's own, so the press carries on.
            if (AnySelected && e.button <= 1 && !Guarded()) { e.StopPropagation(); return; }
            foreach (var env in new[] { timeEnv, volEnv, pitchEnv }) {
                if (env.resolvedStyle.display == DisplayStyle.None || env.rt == null) continue;
                if (env.PointerDown(area.ChangeCoordinatesTo(env, m), e.button, e.clickCount, e.shiftKey)) {
                    active = env;
                    area.CapturePointer(e.pointerId);
                    e.StopPropagation();
                    return;
                }
            }
            // Nothing else took it: the edit cursor (a click) or a selection (a drag).
            bool took = false;
            EditPress(e, m, r, ref took);
            if (took) e.StopPropagation();
        }

        void StartTrim(AudioSpectrumView.TrimDrag which, float time, PointerDownEvent e) {
            // Moving the trim is an edit of the sound: a shared one goes to a copy first, and the drag carries on on it.
            if (!Guarded()) { e.StopPropagation(); return; }
            model.BeginTrimDrag(which, time);
            trimDragging = true;
            area.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e) {
            var m = (Vector2)e.localPosition;
            if (trimDragging) {
                model.BeginFrame(true);
                if (model.DragTrim(model.XToTimeIn(m.x, AreaRect))) Refresh();
                return;
            }
            bool moving = false;
            EditMove(m, ref moving);
            if (moving) return;
            if (active != null) { active.PointerMove(area.ChangeCoordinatesTo(active, m), e.deltaPosition, e.shiftKey, e.pressedButtons); return; }
            foreach (var env in new[] { volEnv, pitchEnv, timeEnv })
                if (env.resolvedStyle.display != DisplayStyle.None && env.rt != null)
                    env.PointerMove(area.ChangeCoordinatesTo(env, m), e.deltaPosition, e.shiftKey, e.pressedButtons);
        }

        void OnUp(PointerUpEvent e) {
            if (area.HasPointerCapture(e.pointerId)) area.ReleasePointer(e.pointerId);
            EditRelease();
            if (trimDragging) { trimDragging = false; model.EndTrimDrag(); }
            if (active != null) { active.PointerUp(); active = null; }
            onReleased?.Invoke();
            Refresh();
        }

        /// <summary>After any press ends on the waveform — the host closes its drag Undo step here, as the old window does
        /// on its mouse-up.</summary>
        public System.Action onReleased;
    }
}
