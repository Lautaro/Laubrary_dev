// CurveEditor_UIToolkit — Framework Trial round 2: multi-point curve/envelope editor built with
// pure UI Toolkit (VisualElement + generateVisualContent/Painter2D + pointer events), reproducing
// the shape of Laubrary's real ZUIEnvelope from spec alone (no access to that source, built fresh).
// Hard constraint for this trial: NO IMGUIContainer, NO EditorGUI/EditorGUILayout/GUILayout anywhere.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.FrameworkTrial
{
    public class CurveEditor_UIToolkit : EditorWindow
    {
        [MenuItem("Laubrary/Framework Trial/Curve Editor - UI Toolkit (Test)")]
        static void Open()
        {
            // utility:true forces a standalone floating OS window (never docks into the main
            // editor layout) so the trial's own screenshot capture-by-exact-title tooling works.
            var win = GetWindow<CurveEditor_UIToolkit>(true);
            win.titleContent = new GUIContent("Curve Editor (UIToolkit Trial)");
            win.minSize = new Vector2(460f, 320f);
            win.Show();
        }

        Label _countLabel;
        CurvePlotElement _plot;

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 12f;
            root.style.paddingRight = 12f;
            root.style.paddingTop = 12f;
            root.style.paddingBottom = 12f;

            var header = new Label("Curve Editor — UI Toolkit trial");
            header.style.fontSize = 14f;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginBottom = 6f;
            root.Add(header);

            var hint = new Label("Drag a point to move it. Click empty space to insert a point. Right-click a point to remove it (min 2).");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginBottom = 8f;
            hint.style.color = new Color(0.7f, 0.7f, 0.7f);
            root.Add(hint);

            _plot = new CurvePlotElement();
            _plot.OnPointsChanged += RefreshCount;
            root.Add(_plot);

            _countLabel = new Label();
            _countLabel.style.marginTop = 8f;
            root.Add(_countLabel);

            RefreshCount();
        }

        void RefreshCount()
        {
            _countLabel.text = $"Point count: {_plot.PointCount}";
        }

        // ── the curve plot control itself ───────────────────────────────────────────────────────
        internal class CurvePlotElement : VisualElement
        {
            const float PlotWidth = 400f;
            const float PlotHeight = 220f;
            const float PointRadius = 5f;
            const float HitRadius = 9f; // slightly bigger than the visual dot, for easier picking
            const float YMin = 0f;
            const float YMax = 10f;

            // Points sorted by time ascending. Public for test/verification purposes.
            internal readonly List<Vector2> Points = new List<Vector2>
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 10f),
            };

            int _dragIndex = -1;

            public event Action OnPointsChanged;

            internal int PointCount => Points.Count;

            public CurvePlotElement()
            {
                style.width = PlotWidth;
                style.height = PlotHeight;
                style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = 1f;
                var borderCol = new Color(0.35f, 0.35f, 0.35f);
                style.borderTopColor = style.borderBottomColor = style.borderLeftColor = style.borderRightColor = borderCol;
                style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);

                generateVisualContent += OnGenerateVisualContent;

                RegisterCallback<PointerDownEvent>(OnPointerDown);
                RegisterCallback<PointerMoveEvent>(OnPointerMove);
                RegisterCallback<PointerUpEvent>(OnPointerUp);
                RegisterCallback<ContextClickEvent>(OnContextClick);

                // ContextClickEvent alone doesn't fire reliably from a synthetic right mouse-down in
                // every dispatch path, so also handle a right-button PointerDownEvent as the removal
                // trigger — belt and suspenders, both paths funnel into TryRemovePointAt.
            }

            // ── coordinate mapping ──────────────────────────────────────────────────────────────
            Vector2 PlotToLocal(Vector2 p)
            {
                float x = Mathf.Clamp01(p.x) * PlotWidth;
                float t = Mathf.InverseLerp(YMin, YMax, p.y);
                float y = PlotHeight - t * PlotHeight; // Y axis flipped: value increases upward
                return new Vector2(x, y);
            }

            Vector2 LocalToPlot(Vector2 local)
            {
                float x = Mathf.Clamp01(local.x / PlotWidth);
                float t = Mathf.Clamp01(1f - local.y / PlotHeight);
                float y = Mathf.Lerp(YMin, YMax, t);
                return new Vector2(x, y);
            }

            // ── drawing ──────────────────────────────────────────────────────────────────────────
            void OnGenerateVisualContent(MeshGenerationContext ctx)
            {
                var painter = ctx.painter2D;

                // connecting lines
                if (Points.Count >= 2)
                {
                    painter.strokeColor = new Color(0.3f, 0.8f, 1f);
                    painter.lineWidth = 2f;
                    painter.BeginPath();
                    Vector2 first = PlotToLocal(Points[0]);
                    painter.MoveTo(first);
                    for (int i = 1; i < Points.Count; i++)
                        painter.LineTo(PlotToLocal(Points[i]));
                    painter.Stroke();
                }

                // point dots
                painter.fillColor = new Color(1f, 0.9f, 0.2f);
                for (int i = 0; i < Points.Count; i++)
                {
                    Vector2 c = PlotToLocal(Points[i]);
                    painter.BeginPath();
                    painter.Arc(c, PointRadius, 0f, 360f);
                    painter.Fill();
                }
            }

            // ── hit testing ──────────────────────────────────────────────────────────────────────
            int FindPointNear(Vector2 local)
            {
                for (int i = 0; i < Points.Count; i++)
                {
                    if (Vector2.Distance(PlotToLocal(Points[i]), local) <= HitRadius)
                        return i;
                }
                return -1;
            }

            // ── pointer interaction ─────────────────────────────────────────────────────────────
            void OnPointerDown(PointerDownEvent evt)
            {
                Vector2 local = evt.localPosition;

                if (evt.button == 1)
                {
                    // right-click: remove nearest point under cursor
                    TryRemovePointAt(local);
                    evt.StopPropagation();
                    return;
                }

                if (evt.button != 0) return;

                int hit = FindPointNear(local);
                if (hit >= 0)
                {
                    _dragIndex = hit;
                    this.CapturePointer(evt.pointerId);
                    evt.StopPropagation();
                }
                else
                {
                    InsertPointAt(local);
                    evt.StopPropagation();
                }
            }

            void OnPointerMove(PointerMoveEvent evt)
            {
                if (_dragIndex < 0 || !this.HasPointerCapture(evt.pointerId)) return;
                DragActiveIndexTo(evt.localPosition);
                evt.StopPropagation();
            }

            // shared by the real pointer-move handler and the test hook below, so both paths run
            // the identical clamp/assign logic.
            void DragActiveIndexTo(Vector2 local)
            {
                Vector2 plotPos = LocalToPlot(local);

                // clamp X to stay ordered relative to neighbors (endpoints stay pinned to their edge)
                float xMin = _dragIndex > 0 ? Points[_dragIndex - 1].x : 0f;
                float xMax = _dragIndex < Points.Count - 1 ? Points[_dragIndex + 1].x : 1f;
                if (_dragIndex == 0) xMin = 0f;
                if (_dragIndex == Points.Count - 1) xMax = 1f;
                float clampedX = Mathf.Clamp(plotPos.x, xMin, xMax);

                Points[_dragIndex] = new Vector2(clampedX, plotPos.y);
                MarkDirtyRepaint();
            }

            void OnPointerUp(PointerUpEvent evt)
            {
                if (_dragIndex >= 0 && this.HasPointerCapture(evt.pointerId))
                {
                    this.ReleasePointer(evt.pointerId);
                    _dragIndex = -1;
                    evt.StopPropagation();
                }
            }

            void OnContextClick(ContextClickEvent evt)
            {
                // fallback path in case a real right-click reaches ContextClickEvent instead of/after
                // the PointerDownEvent(button==1) handler above (context menus route differently
                // across platforms) — TryRemovePointAt is itself idempotent-safe against a stale index.
                TryRemovePointAt(evt.localMousePosition);
                evt.StopPropagation();
            }

            void InsertPointAt(Vector2 local)
            {
                Vector2 plotPos = LocalToPlot(local);
                int insertIdx = Points.Count;
                for (int i = 0; i < Points.Count; i++)
                {
                    if (plotPos.x < Points[i].x) { insertIdx = i; break; }
                }
                Points.Insert(insertIdx, plotPos);
                MarkDirtyRepaint();
                OnPointsChanged?.Invoke();
            }

            void TryRemovePointAt(Vector2 local)
            {
                if (Points.Count <= 2) return;
                int hit = FindPointNear(local);
                if (hit < 0) return;
                Points.RemoveAt(hit);
                if (_dragIndex == hit) _dragIndex = -1;
                MarkDirtyRepaint();
                OnPointsChanged?.Invoke();
            }

            // ── internal test hooks (used only by the trial's own functional verification) ─────
            // These call the exact same private helpers as the real pointer handlers above — no
            // parallel/duplicated logic — they just bypass pointer capture plumbing for tests that
            // prefer a direct call over constructing synthetic PointerEvents.
            internal void TestBeginDrag(int index) => _dragIndex = index;
            internal void TestDragTo(Vector2 localPos) { if (_dragIndex >= 0) DragActiveIndexTo(localPos); }
            internal void TestEndDrag() => _dragIndex = -1;
            internal void TestInsertAt(Vector2 localPos) => InsertPointAt(localPos);
            internal void TestRemoveNear(Vector2 localPos) => TryRemovePointAt(localPos);
        }
    }
}
