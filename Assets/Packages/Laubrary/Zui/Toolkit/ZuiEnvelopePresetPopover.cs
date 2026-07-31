// ZuiEnvelopePresetPopover — the UI Toolkit shape picker for a Curve-mode ZUIValue. A thumbnail browser:
// two filter toggles (Built-in / User), a "＋ Save current" button, and a wrapping grid of curve thumbnails.
// Shapes are identified by their THUMBNAIL, not a name (per user direction) — clicking a thumbnail remaps its
// normalized [0,1] shape onto the field's yMin..yMax and writes it into the points (through the caller's Undo
// hook); saving stores the field's current (normalized) points as a user shape (one click, no name prompt).
// Built on ZuiPopover, so it inherits ZuiToolkit.uss for free (no Z.Attach, no separate OS window).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    static class ZuiEnvelopePresetPopover
    {
        /// Open the picker anchored to <paramref name="anchor"/>, reading/writing <paramref name="value"/>'s points
        /// and yMin/yMax. <paramref name="onBeforeMutate"/> is the Undo.RecordObject hook (fired before a pick
        /// overwrites the points); <paramref name="onApplied"/> refreshes the host control after a pick.
        public static void Open(VisualElement anchor, ZUIValue value, Action onBeforeMutate, Action onApplied)
        {
            if (anchor == null || value == null) return;
            // Read-only load — merely browsing must NOT create an empty library asset; only Save does.
            var lib = ZUIEnvelopePresetLibrary.Load(createIfMissing: false);
            bool showBuiltin = true, showUser = true;   // both filters on by default each open
            var holder = new ZuiPopover[1];
            Action close = () => holder[0]?.Close();

            holder[0] = ZuiPopover.Show(anchor, panel =>
            {
                panel.style.paddingLeft = panel.style.paddingRight = 6f;
                panel.style.paddingTop = panel.style.paddingBottom = 6f;
                panel.style.minWidth = 236f;
                panel.style.maxWidth = 236f;

                var grid = new VisualElement();
                grid.style.flexDirection = FlexDirection.Row;
                grid.style.flexWrap = Wrap.Wrap;

                void Rebuild()
                {
                    grid.Clear();
                    if (showBuiltin)
                        foreach (var p in ZUIEnvelopeBuiltInPresets.All)
                        {
                            var pts = p.points;
                            grid.Add(Tile(pts, () => { Apply(value, pts, onBeforeMutate, onApplied); close(); }, null));
                        }
                    if (showUser && lib != null)
                        for (int i = 0; i < lib.presets.Count; i++)
                        {
                            int idx = i;
                            var pts = lib.presets[i].points;
                            grid.Add(Tile(pts, () => { Apply(value, pts, onBeforeMutate, onApplied); close(); },
                                () => { lib.RemoveAt(idx); Rebuild(); }));
                        }
                    if (grid.childCount == 0)
                        grid.Add(Z.Text(showUser && !showBuiltin ? "No saved shapes yet — hit ＋ Save current." : "(nothing to show)",
                            ZuiText.Subtle, "Toggle Built-in / User, or save a shape."));
                }

                // ── filter toggles + save ──
                var bar = new VisualElement();
                bar.style.flexDirection = FlexDirection.Row;
                bar.style.alignItems = Align.Center;
                bar.style.marginBottom = 6f;
                bar.Add(Z.ToggleButton("Built-in", "Show the shapes that ship with ZUI.", showBuiltin,
                    on => { showBuiltin = on; Rebuild(); }));
                bar.Add(Z.ToggleButton("User", "Show shapes you've saved in this project.", showUser,
                    on => { showUser = on; Rebuild(); }));
                bar.Add(Z.Flexible());
                bar.Add(Z.Button("＋ Save", "Save the current curve as a reusable user shape (identified by its thumbnail).", () =>
                {
                    if (lib == null) lib = ZUIEnvelopePresetLibrary.Load(createIfMissing: true);
                    lib.Add(Guid.NewGuid().ToString("N").Substring(0, 8), Normalized(value));
                    showUser = true;
                    Rebuild();
                }));
                panel.Add(bar);

                var scroll = new ScrollView(ScrollViewMode.Vertical);
                scroll.style.maxHeight = 300f;
                scroll.Add(grid);
                panel.Add(scroll);
                Rebuild();
            }, new ZuiPopover.Options { minWidth = 236f });
        }

        // One shape as a clickable thumbnail tile; a user tile carries a small × to delete. No name text — the
        // thumbnail IS the identity.
        static VisualElement Tile(List<ZUIEnvelopePoint> pts, Action onApply, Action onDelete)
        {
            var tile = new VisualElement { tooltip = onDelete == null ? "Apply this shape." : "Apply this shape (× deletes it)." };
            tile.style.width = 52f; tile.style.height = 40f;
            tile.style.marginRight = 4f; tile.style.marginBottom = 4f;
            tile.style.position = Position.Relative;

            var thumb = new ShapeThumb(pts);
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
    }
}
