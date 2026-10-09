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
    /// The Klip editor's waveform block (T-0468): the curve bar (the one shared with the Zequence tracks: Trim, then Time /
    /// Pitch / Vol each with on, edit and eye), Clamp and Keep length, the length, the audio edit bar, and the waveform
    /// surface -- the same <see cref="WaveSurfaceTK"/> every Klip track of the Zequence editor uses, so the sound is drawn
    /// and edited the same way in both. This block is that surface's Klip-editor host: the axis is the old view object's
    /// (its view window and wheel zoom, its trim geometry and drags), a left click places the edit cursor or selects audio
    /// for the edit bar, and a right-click plays only this sound from the clicked second.
    /// </summary>
    public partial class KlipWaveformTK : VisualElement, IWaveSurfaceHost {

        /// <summary>The waveform's height: the owner sets it by dragging the grip along the bottom (2026-10-08), and it is
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
        readonly Label length;
        /// <summary>The waveform surface, shared with the Zequence tracks.</summary>
        internal readonly WaveSurfaceTK surface;
        VisualElement area => surface.area;

        /// <summary>Asked before any edit of the sound's trim or curves begins; false cancels it (the host's shared-sound guard).</summary>
        public Func<bool> beforeEdit;
        /// <summary>Opens the window's drag Undo step for an edit made on the waveform (closed by <see cref="onReleased"/>).</summary>
        public Action<string> onBeginDrag;
        /// <summary>After any press ends on the waveform — the host closes its drag Undo step here, as the old window does
        /// on its mouse-up.</summary>
        public Action onReleased;
        /// <summary>Plays only this sound from a second of its file (a right-click on the waveform); again while it sounds: stop.</summary>
        public Action<float> onPlayFrom;
        internal SwapNoticeTK notice => surface.notice;

        bool Guarded() => beforeEdit == null || beforeEdit();

        public KlipWaveformTK(AudioSpectrumView model, Klip klip) {
            this.model = model; this.klip = klip;
            AddToClassList("zs-klip-waveform__root");
            // One curve at a time is selected for editing (the others are backdrops); the model starts with every
            // curve's handles on, which would show every point, so a fresh view starts with none selected.
            int selectedCount = (model.ShowVolumeHandles ? 1 : 0) + (model.ShowPitchHandles ? 1 : 0) + (model.ShowTimeHandles ? 1 : 0);
            if (selectedCount > 1) Select(Curve.Volume, false);

            // ── toolbar: the shared curve bar, then Clamp and Keep length (Klip-only), then the length ──
            var row = new VisualElement();
            row.AddToClassList("zs-klip-waveform__bar"); row.style.height = CurveBarTK.H;
            bar = new CurveBarTK(Curves(),
                () => model.TrimEnabled, v => { if (!Guarded()) { bar.Sync(); return; } model.SetTrimEnabled(v); Refresh(); },
                () => model.TrimEnabled ? "Source trim is active. Click to use the whole recording." : "Source trim is off. Click to use the authored start and end points.",
                () => model.ShowTimeHandles ? 0 : model.ShowPitchHandles ? 1 : model.ShowVolumeHandles ? 2 : -1,
                i => { Select(i == 0 ? Curve.Time : i == 1 ? Curve.Pitch : Curve.Volume, i >= 0); Refresh(); },
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
            length = new Label();
            length.AddToClassList("zs-lbl"); length.AddToClassList("zs-mini");
            length.AddToClassList("zs-klip-waveform__length");
            var flex = new VisualElement(); flex.AddToClassList("zs-klip-waveform__flex"); row.Add(flex);
            row.Add(length);
            Add(row);
            Add(Space(3f));
            AddEditBar();   // the audio edit bar (destructive editing, 2026-10-09)

            // ── the waveform surface (the one the Zequence tracks use too) ──
            surface = new WaveSurfaceTK(this, framed: true);
            surface.AddToClassList("zs-klip-waveform__box");
            surface.style.height = AreaH;
            model.height = AreaH;
            Add(surface);
            AddEditMarks();   // the edit selection and cursor, on the surface's marks layer

            area.RegisterCallback<WheelEvent>(OnWheel);
            schedule.Execute(Refresh).Every(33);
        }

        /// <summary>The three curves as the shared bar sees them: on / selected / shown, all read from and written to the model.</summary>
        List<CurveBarTK.Curve> Curves() {
            var es = ZoundsProject.Instance.projectSettings.editorStyle;
            return new List<CurveBarTK.Curve> {
                new CurveBarTK.Curve {
                    label = "Time", colour = AudioSpectrumView.TimeCurveColor,
                    enabled = () => model.TimeEnvelope.enabled, setEnabled = v => { if (!Guarded()) { bar.Sync(); return; } model.RequestTimeEnabled(v); Refresh(); },
                    shown = () => CurveView.IsVisible(ModifierOf(Curve.Time)), setShown = v => { CurveView.SetVisible(ModifierOf(Curve.Time), v); Refresh(); },
                    onContext = a => WaveSurfaceTK.ShowCurveSettings(klip, Curve.Time, a, Guarded, Refresh),
                },
                new CurveBarTK.Curve {
                    label = "Pitch", colour = es.pitchEnvelopeColor,
                    enabled = () => model.PitchEnvelope.enabled, setEnabled = v => { if (!Guarded()) { bar.Sync(); return; } model.RequestPitchEnabled(v); Refresh(); },
                    shown = () => CurveView.IsVisible(ModifierOf(Curve.Pitch)), setShown = v => { CurveView.SetVisible(ModifierOf(Curve.Pitch), v); Refresh(); },
                    warn = () => surface != null && surface.PitchOldScale, warnTip = KlipChainEnvelopes.OldScaleTip,
                    onContext = a => WaveSurfaceTK.ShowCurveSettings(klip, Curve.Pitch, a, Guarded, Refresh),
                },
                new CurveBarTK.Curve {
                    label = "Vol", colour = es.volumeEnvelopeColor,
                    enabled = () => model.VolumeEnvelope.enabled, setEnabled = v => { if (!Guarded()) { bar.Sync(); return; } model.RequestVolumeEnabled(v); Refresh(); },
                    shown = () => CurveView.IsVisible(ModifierOf(Curve.Volume)), setShown = v => { CurveView.SetVisible(ModifierOf(Curve.Volume), v); Refresh(); },
                    onContext = a => WaveSurfaceTK.ShowCurveSettings(klip, Curve.Volume, a, Guarded, Refresh),
                },
            };
        }

        static VisualElement Gap(float w) { var e = new VisualElement(); e.style.width = w; e.AddToClassList("zs-klip-waveform__gap"); return e; }
        static VisualElement Space(float h) { var e = new VisualElement(); e.style.height = h; e.AddToClassList("zs-klip-waveform__space"); return e; }
        static VisualElement Abs() {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("zs-wave-surface__positioned");
            return e;
        }
        static void Place(VisualElement e, Rect r) {
            e.style.left = r.x; e.style.top = r.y; e.style.width = Mathf.Max(0f, r.width); e.style.height = Mathf.Max(0f, r.height);
        }

        // The audio edits' parts, in KlipWaveformTK.AudioEdits.cs (destructive editing, 2026-10-09).
        partial void AddEditBar();
        partial void AddEditMarks();
        partial void PlaceEditMarks(Rect r);
        partial void EditKey(KeyDownEvent e, ref bool handled);
        partial void EditPress(PointerDownEvent e, Vector2 m, Rect r, ref bool handled);
        partial void EditMove(Vector2 m, ref bool handled);
        partial void EditRelease();

        /// <summary>
        /// Selecting a curve for editing (its pencil): one at a time, so a press can only ever grab the curve you chose
        /// (T-0494).
        /// </summary>
        internal void Select(Curve which, bool on) {
            model.SetShowVolumeHandles(on && which == Curve.Volume);
            model.SetShowPitchHandles(on && which == Curve.Pitch);
            model.SetShowTimeHandles(on && which == Curve.Time);
        }

        Envelope EnvelopeOf(Curve which) =>
            which == Curve.Volume ? model.VolumeEnvelope : which == Curve.Pitch ? model.PitchEnvelope : model.TimeEnvelope;

        /// <summary>The chain modifier a waveform curve is, or null (an overlay with no modifier yet).</summary>
        ZoundModifier ModifierOf(Curve which) => KlipChainEnvelopes.ModifierOf(klip, EnvelopeOf(which));

        // ─────────────────────────── refresh ───────────────────────────

        public void Refresh() {
            if (panel == null) return;
            var clip = model.OriginalClip;
            style.display = clip == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (clip == null) return;
            model.BeginFrame(false);
            clamp.SetValueWithoutNotify(model.ClampToTrim);
            clamp.tooltip = model.ClampToTrim ? "Curves span the trimmed source. Click to span the whole recording." : "Curves span the whole recording. Click to span the trimmed source.";
            length.text = model.LengthText;
            if (model.HasKlip) keepLen.SetValueWithoutNotify(model.KeepLength);
            surface.Refresh();
            var r = surface.AreaRect;
            if (r.width > 1f && r.height > 1f) PlaceEditMarks(r);
            bar.Sync();
        }

        void OnWheel(WheelEvent e) {
            if (model.WheelZoom(e.localMousePosition.x, surface.AreaRect, e.delta.y)) { Refresh(); e.StopPropagation(); }
        }

        // ─────────────────────────── the surface's host ───────────────────────────

        Klip IWaveSurfaceHost.Sound => klip;
        AudioClip IWaveSurfaceHost.Source => model.OriginalClip;
        float IWaveSurfaceHost.FileLength => model.OriginalClip != null ? model.OriginalClip.length : 0f;
        float IWaveSurfaceHost.XOf(float s, Rect r) => model.TimeToXIn(s, r);
        float IWaveSurfaceHost.SourceAt(float x, Rect r) => model.XToTimeIn(x, r);

        bool IWaveSurfaceHost.Heard(out float from, out float to) {
            if (model.TrimEnabled) { from = model.trimStart; to = model.trimEnd; return true; }
            from = 0f; to = model.OriginalClip != null ? model.OriginalClip.length : 0f;
            return false;
        }

        void IWaveSurfaceHost.TrimHandlesLive(out bool start, out bool end) => model.TrimHandlesLive(out start, out end);
        bool IWaveSurfaceHost.TrimRandomEditable => model.TrimEnabled;

        bool IWaveSurfaceHost.BeginTrim(TrimDrag which, float x, Rect r) {
            // Moving the trim is an edit of the sound: a shared one goes to a copy first, and the drag carries on on it.
            if (!Guarded()) return false;
            model.BeginFrame(true);
            model.BeginTrimDrag(which, model.XToTimeIn(x, r));
            return true;
        }

        void IWaveSurfaceHost.DragTrim(float x, Rect r) {
            model.BeginFrame(true);
            if (model.DragTrim(model.XToTimeIn(x, r))) Refresh();
        }

        void IWaveSurfaceHost.EndTrim() => model.EndTrimDrag();

        Envelope IWaveSurfaceHost.CurveOf(Curve which) => EnvelopeOf(which);

        int IWaveSurfaceHost.SelectedCurve => model.ShowVolumeHandles ? (int)Curve.Volume : model.ShowPitchHandles ? (int)Curve.Pitch : model.ShowTimeHandles ? (int)Curve.Time : -1;

        void IWaveSurfaceHost.CurveDomain(Curve which, Envelope env, ZoundModifier mod, Rect r, out Rect rect, out float xMin, out float xMax,
                                          out Func<float, float> toX, out Func<float, float> fromX) {
            // Over the trimmed range when clamped, else the whole area; the model's view window says which part of the
            // curve's x that is.
            rect = r;
            if (model.ClampToTrim && model.TrimEnabled)
                rect = Rect.MinMaxRect(model.TimeToXIn(klip.trimStart, r), r.y, model.TimeToXIn(klip.trimEnd, r), r.yMax);
            model.OverlayDomain(env, out xMin, out xMax);
            toX = null; fromX = null;
        }

        void IWaveSurfaceHost.BeginEditUndo(string name) {
            if (onBeginDrag != null) onBeginDrag(name); else ZoundsWindow.BeginDragUndo(name);
        }
        void IWaveSurfaceHost.EndEditUndo() { if (onReleased != null) onReleased(); else ZoundsWindow.EndDragUndo(); }
        void IWaveSurfaceHost.SoundChanged() { }
        bool IWaveSurfaceHost.BeforeEdit() => Guarded();

        void IWaveSurfaceHost.Playheads(List<float> seconds, List<float> weights) {
            var clip = model.OriginalClip;
            if (clip == null) return;
            ZoundEngine.CullingGroups.TryGetValue(klip, out var playing);
            var w = new List<float>();
            var fractions = model.PlayheadFractions(playing, out _, w);
            // The model gives each as a share of the part that plays: back to seconds of the file.
            float from = model.TrimEnabled ? model.trimStart : 0f, to = model.TrimEnabled ? model.trimEnd : clip.length;
            for (int i = 0; i < fractions.Count; i++) {
                seconds.Add(from + fractions[i] * (to - from));
                weights.Add(i < w.Count ? w[i] : 1f);
            }
        }

        void IWaveSurfaceHost.PlayFrom(float s) => onPlayFrom?.Invoke(s);

        bool IWaveSurfaceHost.Press(PointerDownEvent e, Vector2 m, Rect r) {
            model.BeginFrame(true);
            bool took = false;
            EditPress(e, m, r, ref took);
            return took;
        }

        bool IWaveSurfaceHost.Move(Vector2 m, Rect r) { bool moving = false; EditMove(m, ref moving); return moving; }
        void IWaveSurfaceHost.Release() { EditRelease(); onReleased?.Invoke(); }
        bool IWaveSurfaceHost.Key(KeyDownEvent e) { bool handled = false; EditKey(e, ref handled); return handled; }

        string IWaveSurfaceHost.ClickTip =>
            "Click to place the edit cursor; drag to select audio (Shift+click extends the selection) for the edit bar above. Mouse wheel: zoom within the trimmed range.";

        float IWaveSurfaceHost.Height {
            get => AreaH;
            set { AreaH = value; surface.style.height = AreaH; model.height = AreaH; }
        }

        void IWaveSurfaceHost.EndHeightDrag() { }

        Texture IWaveSurfaceHost.FallbackWave(int w, int h, out Rect uv) => model.WaveformImage(w, h, true, out uv);
    }
}
