// ZuiStepSequencer — the editor for a Steps-mode ZUIValue: N equal-width columns, each a bar whose height is
// that section's value in [yMin..yMax]. Click or drag to paint values — drag vertically to set a level,
// horizontally to sweep several columns at once (the step-sequencer feel). Fires OnBeforeMutate once at the
// start of a gesture (the Undo hook) and OnChanged on every edit.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    sealed class ZuiStepSequencer : VisualElement
    {
        readonly List<float> _steps;
        float _yMin, _yMax;
        bool _dragging;

        public Action OnBeforeMutate;
        public Action OnChanged;

        public ZuiStepSequencer(List<float> steps, float yMin, float yMax)
        {
            _steps = steps; _yMin = yMin; _yMax = yMax;
            AddToClassList("zui-envelope");   // same framed backdrop as the curve editor
            style.height = 96f;
            generateVisualContent += Paint;
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
        }

        public void SetRange(float yMin, float yMax) { _yMin = yMin; _yMax = yMax; MarkDirtyRepaint(); }
        public void Refresh() => MarkDirtyRepaint();

        void OnDown(PointerDownEvent e)
        {
            if (e.button != 0 || _steps == null || _steps.Count == 0) return;
            OnBeforeMutate?.Invoke();
            _dragging = true;
            this.CapturePointer(e.pointerId);
            SetFrom(e.localPosition);
            e.StopPropagation();
        }
        void OnMove(PointerMoveEvent e)
        {
            if (!_dragging) return;
            SetFrom(e.localPosition);
            e.StopPropagation();
        }
        void OnUp(PointerUpEvent e)
        {
            if (!_dragging) return;
            _dragging = false;
            this.ReleasePointer(e.pointerId);
            e.StopPropagation();
        }

        void SetFrom(Vector3 local)
        {
            var r = contentRect;
            if (r.width <= 0f || r.height <= 0f || _steps.Count == 0) return;
            int n = _steps.Count;
            int i = Mathf.Clamp(Mathf.FloorToInt((local.x - r.x) / (r.width / n)), 0, n - 1);
            float ty = 1f - Mathf.Clamp01((local.y - r.y) / r.height);
            _steps[i] = Mathf.Lerp(_yMin, _yMax, ty);
            MarkDirtyRepaint();
            OnChanged?.Invoke();
        }

        void Paint(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (!(r.width > 2f) || _steps == null || _steps.Count == 0) return;
            int n = _steps.Count;
            float colW = r.width / n;
            var p2 = mgc.painter2D;
            var fill = new Color(0.4f, 0.85f, 1f, 0.5f);
            var edge = new Color(0.4f, 0.85f, 1f);
            for (int i = 0; i < n; i++)
            {
                float ty = Mathf.Clamp01(Mathf.InverseLerp(_yMin, _yMax, _steps[i]));
                float x0 = r.x + i * colW + 1f;
                float x1 = r.x + (i + 1) * colW - 1f;
                float yTop = r.yMax - ty * r.height;
                p2.fillColor = fill;
                p2.BeginPath();
                p2.MoveTo(new Vector2(x0, yTop));
                p2.LineTo(new Vector2(x1, yTop));
                p2.LineTo(new Vector2(x1, r.yMax));
                p2.LineTo(new Vector2(x0, r.yMax));
                p2.ClosePath();
                p2.Fill();
                p2.strokeColor = edge; p2.lineWidth = 1.5f;
                p2.BeginPath(); p2.MoveTo(new Vector2(x0, yTop)); p2.LineTo(new Vector2(x1, yTop)); p2.Stroke();
            }
        }
    }
}
