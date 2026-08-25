// ZuiShapeBrowser — the inline shape / step picker for a Curve- or Steps-mode ZUIValue, embedded DIRECTLY in the
// value's config menu (not a button that opens yet another panel): Built-in / User filter toggles, a one-click
// ＋ Save, and a wrapping grid of shape thumbnails. Clicking a thumbnail APPLIES (recalls) that shape right away
// and closes the menu — so the thumbnails ARE the recall control. Shapes are identified by their thumbnail, not
// a name (per user direction). Built from ZUI controls, so it inherits ZuiToolkit.uss for free.
//
// It adapts to the value's mode: in Envelope mode it browses/saves CURVE shapes (point lists); in Steps mode it
// browses/saves STEP shapes (bar heights). One shared preset library, told apart by which list a preset carries.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    static class ZuiShapeBrowser
    {
        /// Build the browser as an element to drop into a menu (or any container). Reads/writes
        /// <paramref name="value"/>'s shape (points in Envelope mode, steps in Steps mode) and yMin/yMax.
        /// <paramref name="onBeforeMutate"/> is the Undo.RecordObject hook (fired before a pick overwrites the
        /// shape); <paramref name="onApplied"/> refreshes the host control after a pick; <paramref name="closeMenu"/>
        /// dismisses the surrounding menu once a shape is applied (null = leave it open).
        public static VisualElement BuildBrowser(ZUIValue value, Action onBeforeMutate, Action onApplied, Action closeMenu)
        {
            bool stepsMode = value != null && value.mode == ZUIValue.Mode.Steps;
            // Read-only load — merely browsing must NOT create an empty library asset; only Save does.
            var lib = ZUIEnvelopePresetLibrary.Load(createIfMissing: false);
            bool showBuiltin = true, showUser = true;   // both filters on by default

            var wrap = new VisualElement();
            wrap.style.width = 236f;

            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.marginTop = 3f;

            void Rebuild()
            {
                grid.Clear();
                if (showBuiltin)
                {
                    if (stepsMode)
                        foreach (var s in BuiltInStepShapes())
                        {
                            var steps = s;
                            grid.Add(Tile(new StepThumb(steps),
                                () => { ApplySteps(value, steps, onBeforeMutate, onApplied); closeMenu?.Invoke(); }, null));
                        }
                    else
                        foreach (var p in ZUIEnvelopeBuiltInPresets.All)
                        {
                            var pts = p.points;
                            grid.Add(Tile(new ShapeThumb(pts),
                                () => { Apply(value, pts, onBeforeMutate, onApplied); closeMenu?.Invoke(); }, null));
                        }
                }
                if (showUser && lib != null)
                    for (int i = 0; i < lib.presets.Count; i++)
                    {
                        var preset = lib.presets[i];
                        if (preset.isSteps != stepsMode) continue;   // only this mode's kind of saved shape
                        int idx = i;
                        if (stepsMode)
                        {
                            var steps = preset.steps;
                            grid.Add(Tile(new StepThumb(steps),
                                () => { ApplySteps(value, steps, onBeforeMutate, onApplied); closeMenu?.Invoke(); },
                                () => { lib.RemoveAt(idx); Rebuild(); }));
                        }
                        else
                        {
                            var pts = preset.points;
                            grid.Add(Tile(new ShapeThumb(pts),
                                () => { Apply(value, pts, onBeforeMutate, onApplied); closeMenu?.Invoke(); },
                                () => { lib.RemoveAt(idx); Rebuild(); }));
                        }
                    }
                if (grid.childCount == 0)
                    grid.Add(Z.Text(showUser && !showBuiltin ? "No saved shapes yet — hit ＋ Save." : "(nothing to show)",
                        ZuiText.Subtle, "Toggle Built-in / User, or save a shape."));
            }

            // ── filter toggles + save ──
            // Carries the class, not just the inline equivalent: a child's align-self beats a parent's align-items,
            // so the sheet's row exemption is what keeps these toggles vertically centred rather than top-aligned.
            var bar = new VisualElement();
            bar.AddToClassList("zui-row");
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.Add(Z.ToggleButton("Built-in", "Show the shapes that ship with ZUI.", showBuiltin,
                on => { showBuiltin = on; Rebuild(); }));
            bar.Add(Z.ToggleButton("User", "Show shapes you've saved in this project.", showUser,
                on => { showUser = on; Rebuild(); }));
            bar.Add(Z.Flexible());
            bar.Add(Z.Button("＋ Save",
                stepsMode ? "Save the current step sequence as a reusable user shape (identified by its thumbnail)."
                          : "Save the current curve as a reusable user shape (identified by its thumbnail).", () =>
            {
                if (lib == null) lib = ZUIEnvelopePresetLibrary.Load(createIfMissing: true);
                string id = Guid.NewGuid().ToString("N").Substring(0, 8);
                if (stepsMode) lib.AddSteps(id, NormalizedSteps(value));
                else lib.Add(id, Normalized(value));
                showUser = true;
                Rebuild();
            }));
            wrap.Add(bar);

            // Make the recall action unmistakable — the thumbnails ARE clickable to apply.
            wrap.Add(Z.Text(stepsMode ? "Click a pattern to apply it." : "Click a shape to apply it.",
                ZuiText.Subtle, "Applying overwrites the current shape (undoable). × removes a saved one."));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = 168f;
            scroll.Add(grid);
            wrap.Add(scroll);
            Rebuild();
            return wrap;
        }

        // One shape as a clickable thumbnail tile; a user tile carries a small × to delete. No name text — the
        // thumbnail IS the identity.
        static VisualElement Tile(VisualElement thumb, Action onApply, Action onDelete)
        {
            var tile = new VisualElement { tooltip = onDelete == null ? "Apply this shape." : "Apply this shape (× deletes it)." };
            tile.style.width = 52f; tile.style.height = 40f;
            tile.style.marginRight = 4f; tile.style.marginBottom = 4f;
            tile.style.position = Position.Relative;

            thumb.style.width = 52f; thumb.style.height = 40f;
            thumb.AddManipulator(new Clickable(onApply));
            tile.Add(thumb);

            if (onDelete != null)
            {
                var del = new Label("×") { tooltip = "Delete this saved shape." };
                del.style.position = Position.Absolute;
                del.style.right = 1f; del.style.top = -1f;
                del.style.fontSize = 12f;
                del.style.color = new Color(1f, 0.6f, 0.6f);
                del.RegisterCallback<PointerDownEvent>(e =>
                {
                    e.StopPropagation();   // don't also apply the shape
                    onDelete();
                });
                tile.Add(del);
            }
            return tile;
        }

        // Overwrite the value's points with a shape, remapped from normalized [0,1] onto the field's yMin..yMax.
        static void Apply(ZUIValue value, List<ZUIEnvelopePoint> presetPoints, Action onBeforeMutate, Action onApplied)
        {
            onBeforeMutate?.Invoke();
            value.points.Clear();
            foreach (var p in presetPoints)
                value.points.Add(new ZUIEnvelopePoint(
                    p.time, Mathf.Lerp(value.yMin, value.yMax, p.value), p.exponent, p.editState));
            onApplied?.Invoke();
        }

        // Overwrite the value's step sequence with a shape, remapped from normalized [0,1] onto yMin..yMax and
        // sized to the preset's bar count.
        static void ApplySteps(ZUIValue value, List<float> normalizedSteps, Action onBeforeMutate, Action onApplied)
        {
            if (normalizedSteps == null || normalizedSteps.Count == 0) return;
            onBeforeMutate?.Invoke();
            value.SetStepCount(normalizedSteps.Count);
            int n = Mathf.Min(value.steps.Count, normalizedSteps.Count);
            for (int i = 0; i < n; i++)
                value.steps[i] = Mathf.Lerp(value.yMin, value.yMax, Mathf.Clamp01(normalizedSteps[i]));
            onApplied?.Invoke();
        }

        // Normalize the value's current points to [0,1] so a saved shape is range-independent.
        static List<ZUIEnvelopePoint> Normalized(ZUIValue value)
        {
            var result = new List<ZUIEnvelopePoint>();
            float range = value.yMax - value.yMin;
            foreach (var p in value.points)
                result.Add(new ZUIEnvelopePoint(
                    p.time, range > 0.0001f ? Mathf.InverseLerp(value.yMin, value.yMax, p.value) : 0f,
                    p.exponent, p.editState));
            return result;
        }

        // Normalize the value's current step heights to [0,1] so a saved step shape is range-independent.
        static List<float> NormalizedSteps(ZUIValue value)
        {
            var result = new List<float>();
            float range = value.yMax - value.yMin;
            foreach (var v in value.steps)
                result.Add(range > 0.0001f ? Mathf.InverseLerp(value.yMin, value.yMax, v) : 0f);
            return result;
        }

        // A handful of ready-made step patterns (normalized [0,1], 8 bars each) so the Built-in filter is useful
        // in Steps mode too. No names — the bar thumbnail is the identity.
        static List<List<float>> BuiltInStepShapes()
        {
            const int N = 8;
            var rise = new List<float>();
            var fall = new List<float>();
            var tri = new List<float>();
            var alt = new List<float>();
            for (int i = 0; i < N; i++)
            {
                float t = i / (float)(N - 1);
                rise.Add(t);
                fall.Add(1f - t);
                tri.Add(1f - Mathf.Abs(t * 2f - 1f));   // 0 → 1 → 0 across the bars
                alt.Add(i % 2 == 0 ? 0f : 1f);
            }
            return new List<List<float>> { rise, fall, tri, alt };
        }

        // A curve thumbnail painting a shape's normalized [0,1] points.
        sealed class ShapeThumb : VisualElement
        {
            readonly List<ZUIEnvelopePoint> _pts;
            public ShapeThumb(List<ZUIEnvelopePoint> pts)
            {
                _pts = pts;
                AddToClassList("zui-envelope");
                style.flexShrink = 0f;
                generateVisualContent += Paint;
            }
            void Paint(MeshGenerationContext mgc)
            {
                var r = contentRect;
                if (!(r.width > 4f) || _pts == null || _pts.Count == 0) return;
                var p2 = mgc.painter2D;
                p2.strokeColor = new Color(0.4f, 0.85f, 1f);
                p2.lineWidth = 1.2f;
                p2.BeginPath();
                const int Samples = 32;
                for (int s = 0; s <= Samples; s++)
                {
                    float t = s / (float)Samples;
                    float v = Mathf.Clamp01(ZUIEnvelopeEvaluator.Evaluate(_pts, t, 1f));
                    var pt = new Vector2(r.x + 2f + t * (r.width - 4f), r.yMax - 2f - v * (r.height - 4f));
                    if (s == 0) p2.MoveTo(pt); else p2.LineTo(pt);
                }
                p2.Stroke();
            }
        }

        // A step thumbnail painting a shape's normalized [0,1] bar heights as filled columns.
        sealed class StepThumb : VisualElement
        {
            readonly List<float> _steps;
            public StepThumb(List<float> steps)
            {
                _steps = steps;
                AddToClassList("zui-envelope");
                style.flexShrink = 0f;
                generateVisualContent += Paint;
            }
            void Paint(MeshGenerationContext mgc)
            {
                var r = contentRect;
                if (!(r.width > 4f) || _steps == null || _steps.Count == 0) return;
                var p2 = mgc.painter2D;
                int n = _steps.Count;
                float x0 = r.x + 2f, w = r.width - 4f, yMax = r.yMax - 2f, h = r.height - 4f;
                float bw = w / n;
                p2.fillColor = new Color(0.4f, 0.85f, 1f, 0.85f);
                for (int i = 0; i < n; i++)
                {
                    float v = Mathf.Clamp01(_steps[i]);
                    float bx = x0 + i * bw;
                    float bh = v * h;
                    if (bh < 1f) bh = 1f;   // a floor so a zero bar still reads as a bar
                    p2.BeginPath();
                    p2.MoveTo(new Vector2(bx + 0.5f, yMax));
                    p2.LineTo(new Vector2(bx + 0.5f, yMax - bh));
                    p2.LineTo(new Vector2(bx + bw - 0.5f, yMax - bh));
                    p2.LineTo(new Vector2(bx + bw - 0.5f, yMax));
                    p2.ClosePath();
                    p2.Fill();
                }
            }
        }
    }
}
