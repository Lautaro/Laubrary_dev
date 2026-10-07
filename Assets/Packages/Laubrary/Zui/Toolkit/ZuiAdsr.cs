using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// <summary>A fixed-stage envelope: each lane owns exactly one parameter, never a movable point list.</summary>
    public sealed class ZuiAdsr : VisualElement
    {
        public sealed class Options
        {
            public float attackMax = 5f, holdMax = 5f, decayMax = 10f, releaseMax = 10f;
            public float width = 440f, height = 112f;
        }

        readonly Func<float>[] _get;
        readonly Action<float>[] _set;
        readonly float[] _max;
        readonly int[] _stages;
        readonly ZuiMicroSlider[] _inputs = new ZuiMicroSlider[5];
        readonly Label[] _labels = new Label[5];
        readonly VisualElement _canvas;
        readonly ZuiEnvelopePresentation _presentation = new ZuiEnvelopePresentation();
        readonly ZUIEnvelopeDef _definition = new ZUIEnvelopeDef();
        static readonly string[] Names = { "Attack", "Hold", "Decay", "Sustain", "Release" };
        static readonly string[] ShortNames = { "A ms", "H ms", "D ms", "S %", "R ms" };
        static readonly string[] Tips = {
            "Time from note-on to full level. Drag horizontally; shorter times have more room. Exact input is milliseconds.",
            "Time held at full level before decay. Drag horizontally. Exact input is milliseconds.",
            "Time from the peak to the sustain level. Drag horizontally. Exact input is milliseconds.",
            "Level held while the note is down. Drag vertically. Exact input is percent.",
            "Time to fade out after note-off. Drag horizontally. Exact input is milliseconds."
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
            _stages = getHold != null && setHold != null ? new[] { 0, 1, 2, 3, 4 } : new[] { 0, 2, 3, 4 };
            foreach (int i in _stages)
            {
                if (_get[i] == null || _set[i] == null) throw new ArgumentNullException(Names[i]);
                _max[i] = Mathf.Max(0.001f, _max[i]);
            }
            AddToClassList("zui-adsr");
            this.tooltip = tooltip;
            style.width = opt.width;
            style.maxWidth = Length.Percent(100f);
            style.flexShrink = 1;
            style.alignSelf = Align.FlexStart;
            if (!string.IsNullOrEmpty(label)) Add(Z.Text(label, ZuiText.Small, tooltip));

            _canvas = new VisualElement { name = "adsr-canvas", tooltip = tooltip + " Each handle edits only its named stage. Shift = fine adjustment." };
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

            var row = Z.Row();
            row.style.flexWrap = Wrap.Wrap;
            foreach (int stage in _stages)
            {
                int i = stage;
                var caption = Z.Text(Names[i], ZuiText.Small, Tips[i]);
                caption.name = "adsr-label-" + Names[i].ToLowerInvariant();
                caption.style.position = Position.Absolute;
                caption.style.bottom = 1f;
                caption.style.width = Length.Percent(100f / _stages.Length);
                caption.style.left = Length.Percent(Array.IndexOf(_stages, i) * 100f / _stages.Length);
                caption.style.unityTextAlign = TextAnchor.MiddleCenter;
                caption.pickingMode = PickingMode.Ignore;
                _labels[i] = caption;
                _canvas.Add(caption);
                float scale = i == 3 ? 100f : 1000f;
                _inputs[i] = Z.MicroSlider(ShortNames[i], Read(i) * scale, 0, _max[i] * scale,
                    Tips[i], v => SetStage(i, v / scale), 82f, decimals: i == 3 ? 0 : 1,
                    prefsKey: "adsr." + Names[i]);
                _inputs[i].name = "adsr-input-" + Names[i].ToLowerInvariant();
                row.Add(_inputs[i]);
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
            foreach (int i in _stages)
            {
                _inputs[i].value = Read(i) * (i == 3 ? 100f : 1000f);
                _labels[i].tooltip = Tips[i] + " Current: " + (i == 3 ? (Read(i) * 100f).ToString("0.#") + "%" : FormatTime(Read(i)));
            }
            _canvas.MarkDirtyRepaint();
        }

        public static string FormatTime(float seconds) => seconds < 1f
            ? (seconds * 1000f).ToString("0.#") + " ms" : seconds.ToString("0.##") + " s";

        /// <summary>Square-root time mapping preserves zero and makes short attacks easy to reach.</summary>
        public static float TimeToPosition(float seconds, float maximum) => Mathf.Sqrt(Mathf.Clamp01(seconds / Mathf.Max(0.001f, maximum)));
        public static float PositionToTime(float position, float maximum) => Mathf.Clamp01(position) * Mathf.Clamp01(position) * Mathf.Max(0.001f, maximum);

        void SetStage(int stage, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            value = Mathf.Clamp(value, 0, _max[stage]);
            value = (float)Math.Round(value, stage == 3 ? 3 : 4);
            if (Mathf.Approximately(Read(stage), value)) return;
            _set[stage](value);
            Refresh();
        }

        Rect Plot => new Rect(10, 10, Mathf.Max(1, _canvas.contentRect.width - 20), Mathf.Max(1, _canvas.contentRect.height - 32));
        float LaneWidth => Plot.width / _stages.Length;
        Vector2 Handle(int stage)
        {
            Rect r = Plot;
            float lane = LaneWidth;
            int index = Array.IndexOf(_stages, stage);
            // Inset even the zero-time handle from the preceding stage, so every stage remains pickable.
            float x = r.x + index * lane + 7f + (stage == 3 ? 0.5f : TimeToPosition(Read(stage), _max[stage])) * Mathf.Max(1, lane - 14f);
            float y = stage == 0 || stage == 1 ? r.y : stage == 4 ? r.yMax : Mathf.Lerp(r.yMax, r.y, Mathf.Clamp01(Read(3)));
            return new Vector2(x, y);
        }

        /// <summary>Canvas-local handle location. Stages: 0 attack, 1 hold, 2 decay, 3 sustain, 4 release.</summary>
        public Vector2 HandlePosition(int stage)
        {
            if (Array.IndexOf(_stages, stage) < 0) throw new ArgumentOutOfRangeException(nameof(stage));
            return Handle(stage);
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
                pos += (position.x - _dragStart.x) / Mathf.Max(1, LaneWidth - 14) * factor;
                SetStage(_dragStage, PositionToTime(pos, _max[_dragStage]));
            }
        }

        void Down(PointerDownEvent e)
        {
            if (e.button != 0 || _dragStage >= 0) return;
            var mouse = new Vector2(e.localPosition.x, e.localPosition.y);
            int stage = -1;
            float best = 14f * 14f;
            foreach (int i in _stages)
            {
                float d = (Handle(i) - mouse).sqrMagnitude;
                if (d <= best) { best = d; stage = i; }
            }
            if (stage < 0) return;
            BeginDrag(stage);
            _dragStart = mouse;
            _pointer = e.pointerId;
            _canvas.CapturePointer(e.pointerId);
            _canvas.MarkDirtyRepaint();
            e.StopPropagation();
        }

        void Move(PointerMoveEvent e)
        {
            if (_dragStage < 0)
            {
                var mouse = new Vector2(e.localPosition.x, e.localPosition.y);
                foreach (int i in _stages)
                    if ((Handle(i) - mouse).sqrMagnitude <= 196f) { _canvas.tooltip = Names[i] + ": " + Tips[i]; return; }
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
            for (int n = 0; n <= 4; n++)
            {
                float y = Mathf.Lerp(r.y, r.yMax, n / 4f);
                p.BeginPath(); p.MoveTo(new Vector2(r.x, y)); p.LineTo(new Vector2(r.xMax, y)); p.Stroke();
            }
            for (int n = 1; n < _stages.Length; n++)
            {
                float x = r.x + n * LaneWidth;
                p.BeginPath(); p.MoveTo(new Vector2(x, r.y)); p.LineTo(new Vector2(x, r.yMax)); p.Stroke();
            }
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
                p.BeginPath(); p.Arc(Handle(i), _dragStage == i ? 5 : 4, 0, 360); p.Fill();
            }
        }
    }
}
