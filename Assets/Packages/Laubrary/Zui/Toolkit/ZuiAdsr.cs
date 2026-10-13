using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// <summary>A compact fixed-stage envelope: each lane owns exactly one parameter, never a movable point list.
    /// A small filled shape with one draggable point per stage, and under it one slider per stage, each sitting
    /// under its own lane. Times are stored in seconds and sustain as 0..1; the control only changes how they
    /// are shown and which values an edit lands on (see <see cref="SnapSeconds"/>).</summary>
    public sealed class ZuiAdsr : VisualElement
    {
        public sealed class Options
        {
            public float attackMax = 5f, holdMax = 5f, decayMax = 10f, releaseMax = 10f;
            /// Whole control width. NaN = <see cref="laneWidth"/> per stage, so a five-stage AHDSR gets the
            /// same room per stage as a four-stage ADSR.
            public float width = float.NaN, height = 46f, laneWidth = 68f;
            public float attackDefault = .01f, holdDefault, decayDefault = .2f, sustainDefault = .7f, releaseDefault = .5f;
        }

        readonly Func<float>[] _get;
        readonly Action<float>[] _set;
        readonly float[] _max;
        readonly float[] _defaults;
        readonly int[] _stages;
        readonly ZuiMicroSlider[] _inputs = new ZuiMicroSlider[5];
        readonly VisualElement _canvas;
        readonly string _canvasTip;
        readonly ZuiEnvelopePresentation _presentation = new ZuiEnvelopePresentation();
        readonly ZUIEnvelopeDef _definition = new ZUIEnvelopeDef();
        static readonly string[] Names = { "Attack", "Hold", "Decay", "Sustain", "Release" };
        static readonly string[] ShortNames = { "A", "H", "D", "S", "R" };
        const string TimeSteps = " Steps: whole milliseconds below 100 ms, 10 ms up to 1 s, then 0.05 s.";
        static readonly string[] Tips = {
            "Attack: time from note-on to full level." + TimeSteps,
            "Hold: time held at full level before decay." + TimeSteps,
            "Decay: time from the peak down to the sustain level." + TimeSteps,
            "Sustain: level held while the note is down, in whole percent.",
            "Release: time to fade out after note-off." + TimeSteps
        };
        int _dragStage = -1, _pointer = -1, _undoGroup = -1;
        Vector2 _dragStart;
        float _dragValue;

        public ZuiAdsr(string label,
            Func<float> getAttack, Action<float> setAttack, Func<float> getHold, Action<float> setHold,
            Func<float> getDecay, Action<float> setDecay, Func<float> getSustain, Action<float> setSustain,
            Func<float> getRelease, Action<float> setRelease, string tooltip, Options options = null)
        {
            var opt = options ?? new Options();
            _get = new[] { getAttack, getHold, getDecay, getSustain, getRelease };
            _set = new[] { setAttack, setHold, setDecay, setSustain, setRelease };
            _max = new[] { opt.attackMax, opt.holdMax, opt.decayMax, 1f, opt.releaseMax };
            _defaults = new[] { opt.attackDefault, opt.holdDefault, opt.decayDefault, opt.sustainDefault, opt.releaseDefault };
            _stages = getHold != null && setHold != null ? new[] { 0, 1, 2, 3, 4 } : new[] { 0, 2, 3, 4 };
            foreach (int i in _stages)
            {
                if (_get[i] == null || _set[i] == null) throw new ArgumentNullException(Names[i]);
                _max[i] = Mathf.Max(0.001f, _max[i]);
            }
            AddToClassList("zui-adsr");
            this.tooltip = tooltip;
            style.width = float.IsNaN(opt.width) ? opt.laneWidth * _stages.Length : opt.width;
            style.maxWidth = Length.Percent(100f);
            style.flexShrink = 1;
            style.alignSelf = Align.FlexStart;
            if (!string.IsNullOrEmpty(label)) Add(Z.Text(label, ZuiText.Small, tooltip));

            _canvasTip = tooltip + " Drag a point to edit only its stage; double-click a lane to reset it. Shift = fine adjustment.";
            _canvas = new VisualElement { name = "adsr-canvas", tooltip = _canvasTip };
            _canvas.AddToClassList("zui-envelope");
            _canvas.AddToClassList("zui-envelope--standard");
            _presentation.RefreshFallbacks(_definition, new Color(0.4f, 0.85f, 1f));
            _canvas.style.width = Length.Percent(100f);
            _canvas.style.height = opt.height;
            _canvas.style.flexShrink = 0;
            _canvas.RegisterCallback<CustomStyleResolvedEvent>(e => {
                _presentation.Resolve(e, _definition, new Color(0.4f, 0.85f, 1f));
                _canvas.MarkDirtyRepaint();
            });
            _canvas.generateVisualContent += Paint;
            _canvas.RegisterCallback<PointerDownEvent>(Down);
            _canvas.RegisterCallback<PointerMoveEvent>(Move);
            _canvas.RegisterCallback<PointerUpEvent>(e => { if (_dragStage < 0) return; EndDrag(); e.StopPropagation(); });
            _canvas.RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag());
            _canvas.RegisterCallback<DetachFromPanelEvent>(_ => EndDrag());
            Add(_canvas);

            // One slider per stage, each as wide as its lane, so a slider sits under the point it edits.
            var row = Z.Row();
            row.name = "adsr-sliders";
            row.style.flexWrap = Wrap.NoWrap;
            row.style.width = Length.Percent(100f);
            row.style.marginTop = 1f;
            foreach (int stage in _stages)
            {
                int i = stage;
                bool level = i == 3;
                float scale = level ? 100f : 1000f;
                var slider = Z.MicroSlider(ShortNames[i], Read(i) * scale, 0, _max[i] * scale,
                    Tips[i], v => SetStage(i, v / scale), -1f,
                    prefsKey: "adsr." + Names[i], defaultValue: _defaults[i] * scale);
                if (level) slider.SetSteps(v => Mathf.Round(v), v => Mathf.RoundToInt(v) + "%");
                else slider.SetSteps(ms => SnapSeconds(ms / 1000f) * 1000f, ms => FormatTime(ms / 1000f), 2f);
                slider.name = "adsr-input-" + Names[i].ToLowerInvariant();
                // An equal share of the row each (no stretching), shrunk evenly to leave a hairline gap so
                // neighbouring readouts never touch.
                slider.style.width = Length.Percent(100f / _stages.Length);
                slider.style.flexGrow = 0;
                slider.style.minWidth = 0;
                slider.style.flexShrink = 1;
                slider.style.marginLeft = 0;
                slider.style.marginRight = stage == 4 ? 0 : 2;
                slider.style.marginTop = slider.style.marginBottom = 0;
                _inputs[i] = slider;
                row.Add(slider);
            }
            Add(row);
            Refresh();
        }

        float Read(int stage)
        {
            float v = _get[stage]?.Invoke() ?? 0;
            return float.IsNaN(v) || float.IsInfinity(v) ? 0 : v;
        }

        /// <summary>Read the host values again after an external edit; never mutates authored data.</summary>
        public void Refresh()
        {
            foreach (int i in _stages) _inputs[i].value = Read(i) * (i == 3 ? 100f : 1000f);
            _canvas.MarkDirtyRepaint();
        }

        /// <summary>How a stage time reads: whole milliseconds below one second, seconds (up to two
        /// decimals) from one second up. Matches the steps of <see cref="SnapSeconds"/>.</summary>
        public static string FormatTime(float seconds) => seconds < 0.9995f
            ? Mathf.RoundToInt(seconds * 1000f) + " ms" : seconds.ToString("0.##") + " s";

        /// <summary>The useful steps for an envelope time: 1 ms below 100 ms (short attacks are where a
        /// millisecond is audible), 10 ms up to one second, 50 ms from one second up. Every edit lands on
        /// these; a value stored before they existed is only shown rounded until it is edited.</summary>
        public static float SnapSeconds(float seconds)
        {
            float ms = Mathf.Max(0f, seconds * 1000f);
            float step = ms < 99.5f ? 1f : ms < 995f ? 10f : 50f;
            return Mathf.Round(ms / step) * step / 1000f;
        }

        /// <summary>Sustain is edited in whole percent.</summary>
        public static float SnapLevel(float level) => Mathf.Round(Mathf.Clamp01(level) * 100f) / 100f;

        static string Readout(int stage, float v) => stage == 3 ? Mathf.RoundToInt(v * 100f) + "%" : FormatTime(v);

        /// <summary>Square-root time mapping preserves zero and makes short attacks easy to reach.</summary>
        public static float TimeToPosition(float seconds, float maximum) => Mathf.Sqrt(Mathf.Clamp01(seconds / Mathf.Max(0.001f, maximum)));
        public static float PositionToTime(float position, float maximum) => Mathf.Clamp01(position) * Mathf.Clamp01(position) * Mathf.Max(0.001f, maximum);

        void SetStage(int stage, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            value = Mathf.Clamp(value, 0, _max[stage]);
            value = stage == 3 ? SnapLevel(value) : Mathf.Min(SnapSeconds(value), _max[stage]);
            if (Mathf.Approximately(Read(stage), value)) return;
            _set[stage](value);
            Refresh();
        }

        Rect Plot => new Rect(4, 5, Mathf.Max(1, _canvas.contentRect.width - 8), Mathf.Max(1, _canvas.contentRect.height - 10));
        float LaneWidth => Plot.width / _stages.Length;
        Vector2 Handle(int stage)
        {
            Rect r = Plot;
            float lane = LaneWidth;
            int index = Array.IndexOf(_stages, stage);
            // Inset even the zero-time handle from the preceding stage, so every stage remains pickable.
            float x = r.x + index * lane + 5f + (stage == 3 ? 0.5f : TimeToPosition(Read(stage), _max[stage])) * Mathf.Max(1, lane - 10f);
            float y = stage == 0 || stage == 1 ? r.y : stage == 4 ? r.yMax : Mathf.Lerp(r.yMax, r.y, Mathf.Clamp01(Read(3)));
            return new Vector2(x, y);
        }

        /// <summary>Canvas-local handle location. Stages: 0 attack, 1 hold, 2 decay, 3 sustain, 4 release.</summary>
        public Vector2 HandlePosition(int stage)
        {
            if (Array.IndexOf(_stages, stage) < 0) throw new ArgumentOutOfRangeException(nameof(stage));
            return Handle(stage);
        }

        /// <summary>Canvas-local centre of a stage's lane; any press inside the lane picks that stage.</summary>
        public Vector2 LaneCenter(int stage)
        {
            int index = Array.IndexOf(_stages, stage);
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(stage));
            Rect r = Plot;
            return new Vector2(r.x + (index + 0.5f) * LaneWidth, r.center.y);
        }

        /// <summary>Begin the same gesture used by pointer input; hosts can use it for accessible scrubbing.</summary>
        public void BeginDrag(int stage)
        {
            EndDrag();
            _dragStart = HandlePosition(stage);
            _dragStage = stage;
            _dragValue = Read(stage);
            _undoGroup = ZuiUndoGesture.Begin();
            _canvas.MarkDirtyRepaint();
        }

        /// <summary>Move the active handle in canvas-local coordinates. Only its own stage can change.</summary>
        public void DragToLocal(Vector2 position, bool fine = false)
        {
            if (_dragStage < 0) return;
            float factor = fine ? 0.15f : 1f;
            if (_dragStage == 3) SetStage(3, _dragValue - (position.y - _dragStart.y) / Plot.height * factor);
            else
            {
                float pos = TimeToPosition(_dragValue, _max[_dragStage]);
                pos += (position.x - _dragStart.x) / Mathf.Max(1, LaneWidth - 10) * factor;
                SetStage(_dragStage, PositionToTime(pos, _max[_dragStage]));
            }
            _canvas.tooltip = Names[_dragStage] + " " + Readout(_dragStage, Read(_dragStage));
        }

        void Down(PointerDownEvent e)
        {
            if (e.button != 0 || _dragStage >= 0) return;
            var mouse = new Vector2(e.localPosition.x, e.localPosition.y);
            int stage = StageAtLocal(mouse);
            if (stage < 0) return;
            if (e.clickCount == 2)
            {
                int group = ZuiUndoGesture.Begin();
                SetStage(stage, _defaults[stage]);
                ZuiUndoGesture.End(group);
                e.StopPropagation();
                return;
            }
            BeginDrag(stage);
            _dragStart = mouse;
            _pointer = e.pointerId;
            _canvas.CapturePointer(e.pointerId);
            _canvas.MarkDirtyRepaint();
            e.StopPropagation();
        }

        int StageAtLocal(Vector2 mouse)
        {
            int stage = -1;
            float best = 10f * 10f;
            foreach (int i in _stages)
            {
                float d = (Handle(i) - mouse).sqrMagnitude;
                if (d <= best) { best = d; stage = i; }
            }
            // The whole lane is the hit target, not only the small dot. Requiring a precision hit made the
            // graph appear read-only and let the surrounding ScrollView take the missed drag, which moved the
            // page instead of the envelope.
            if (stage < 0 && new Rect(0, 0, _canvas.contentRect.width, _canvas.contentRect.height).Contains(mouse))
            {
                int lane = Mathf.Clamp(Mathf.FloorToInt((mouse.x - Plot.x) / Mathf.Max(1, LaneWidth)), 0, _stages.Length - 1);
                stage = _stages[lane];
            }
            return stage;
        }

        void Move(PointerMoveEvent e)
        {
            if (_dragStage < 0)
            {
                int i = StageAtLocal(new Vector2(e.localPosition.x, e.localPosition.y));
                _canvas.tooltip = i < 0 ? _canvasTip : Names[i] + " " + Readout(i, Read(i)) + ". " + Tips[i] + " Drag to change; double-click to reset.";
                return;
            }
            DragToLocal(new Vector2(e.localPosition.x, e.localPosition.y), e.shiftKey);
            e.StopPropagation();
        }

        /// <summary>Finish the active gesture, collapsing all the owner's recorded edits into one Undo.</summary>
        public void EndDrag()
        {
            if (_dragStage < 0) return;
            _dragStage = -1;
            int pointer = _pointer;
            _pointer = -1;
            ZuiUndoGesture.End(_undoGroup);
            _undoGroup = -1;
            if (pointer >= 0 && _canvas.HasPointerCapture(pointer)) _canvas.ReleasePointer(pointer);
            _canvas.MarkDirtyRepaint();
        }

        void Paint(MeshGenerationContext context)
        {
            if (_canvas.contentRect.width < 2) return;
            var p = context.painter2D;
            var style = _presentation.Current;
            Rect r = Plot;
            p.fillColor = style.background;
            p.BeginPath();
            p.MoveTo(Vector2.zero); p.LineTo(new Vector2(_canvas.contentRect.width, 0));
            p.LineTo(new Vector2(_canvas.contentRect.width, _canvas.contentRect.height));
            p.LineTo(new Vector2(0, _canvas.contentRect.height)); p.ClosePath(); p.Fill();
            p.strokeColor = style.gridColor;
            p.lineWidth = 1;
            for (int n = 1; n < _stages.Length; n++)
            {
                float x = r.x + n * LaneWidth;
                p.BeginPath(); p.MoveTo(new Vector2(x, r.y)); p.LineTo(new Vector2(x, r.yMax)); p.Stroke();
            }
            // The shape is filled: the area under the envelope, then its outline, then the stage points.
            var fill = style.curveColor; fill.a *= 0.35f;
            p.fillColor = fill;
            p.BeginPath();
            p.MoveTo(new Vector2(r.x, r.yMax));
            foreach (int i in _stages) p.LineTo(Handle(i));
            p.LineTo(new Vector2(r.xMax, r.yMax));
            p.ClosePath();
            p.Fill();
            p.strokeColor = style.curveColor;
            p.lineWidth = Mathf.Max(1.5f, style.curveThickness);
            p.BeginPath();
            p.MoveTo(new Vector2(r.x, r.yMax));
            foreach (int i in _stages) p.LineTo(Handle(i));
            p.LineTo(new Vector2(r.xMax, r.yMax));
            p.Stroke();
            foreach (int i in _stages)
            {
                p.fillColor = _dragStage == i ? style.selectedColor : style.curveColor;
                p.BeginPath(); p.Arc(Handle(i), _dragStage == i ? 4.5f : 3.5f, 0, 360); p.Fill();
            }
        }
    }
}
