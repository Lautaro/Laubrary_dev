using System.Collections.Generic;
using System;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// A curve point's random settings (T-0483), opened by right-clicking the point on any Klip curve (volume, pitch, time)
    /// or a chain card's curve: Random on/off, how far each play may move it across (a share of the curve's length) and
    /// up and down (a share of its height) -- an ellipse, drawn around the point -- and the bias (where in the ellipse it
    /// tends to land). One wide row. Settings keep it open; a click elsewhere closes it. All the changes made while it is
    /// open are one Undo step.
    /// </summary>
    public class RandomPointPopup : PopupWindowContent {

        const float RowH = 20f;
        readonly ZUIEnvelopePoint point;
        readonly float xRange;
        readonly Func<float> yRange;
        readonly Action beforeFirstChange, changed;
        readonly List<ZUIEnvelopePoint> group;   // what Reset, Squash and Expand act on: the selection, or just this point
        readonly Func<float?> neutral;           // the curve's "no change" value, read after the first change's conversion
        readonly Func<Vector2> valueRange;       // the curve's (bottom, top)
        bool begun;

        RandomPointPopup(ZUIEnvelopePoint point, float xRange, Func<float> yRange, Action beforeFirstChange, Action changed,
                         List<ZUIEnvelopePoint> group, Func<float?> neutral, Func<Vector2> valueRange) {
            this.point = point; this.xRange = xRange > 0f ? xRange : 1f; this.yRange = yRange;
            this.beforeFirstChange = beforeFirstChange; this.changed = changed;
            this.group = group != null && group.Count > 1 && group.Contains(point) ? group : new List<ZUIEnvelopePoint> { point };
            this.neutral = neutral; this.valueRange = valueRange;
        }

        /// <summary>Opens the settings for <paramref name="point"/> next to <paramref name="worldPosition"/> (the point's centre in
        /// the calling window). <paramref name="yRange"/> is read when needed: the first change may rescale the curve (an old
        /// pitch curve converts).</summary>
        /// <param name="selection">The curve's selected points: when the clicked point is one of several selected, Reset acts on
        /// all of them and Squash and Expand are offered.</param>
        /// <param name="neutral">The value that means "no change" on this curve (Reset), or null when there is none.</param>
        /// <param name="valueRange">The curve's bottom and top, which Squash and Expand stay inside.</param>
        public static void Show(Vector2 worldPosition, ZUIEnvelopePoint point, float xRange, Func<float> yRange,
                                Action beforeFirstChange, Action changed,
                                List<ZUIEnvelopePoint> selection = null, Func<float?> neutral = null, Func<Vector2> valueRange = null) {
            if (point == null) return;
            UnityEditor.PopupWindow.Show(new Rect(worldPosition.x - 4f, worldPosition.y + 6f, 8f, 8f),
                                          new RandomPointPopup(point, xRange, yRange, beforeFirstChange, changed, selection, neutral, valueRange));
        }

        const float ActionsW = 56f + 4f + 62f + 2f + 62f + 12f;
        public override Vector2 GetWindowSize() => new Vector2(556f + (group.Count > 1 ? ActionsW : 56f + 12f), RowH + 12f);

        public override void OnGUI(Rect rect) { }

        public override void OnOpen() {
            var root = editorWindow.rootVisualElement;
            ZS.Attach(root);
            root.style.paddingLeft = 6f; root.style.paddingTop = 6f;
            Build(root);
        }

        public override void OnClose() {
            if (begun) ZoundsWindow.EndDragUndo();
        }

        void Change(Action a) {
            if (!begun) { begun = true; ZoundsWindow.BeginDragUndo("random curve point"); beforeFirstChange?.Invoke(); }
            a();
            EditorUtility.SetDirty(ZoundsProject.Instance);
            changed?.Invoke();
        }

        void Build(VisualElement root) {
            root.Clear();
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row; r.style.height = RowH; r.style.flexShrink = 0;
            // Actions first, so switching Random on or off never moves them.
            bool many = group.Count > 1;
            var reset = ZS.Button("Reset", many
                    ? "Puts the " + group.Count + " selected points back at this curve's \"no change\" value (the middle of a pitch or time curve, ×1 on a volume curve, where the setting is set on a Set curve)."
                    : "Puts this point back at this curve's \"no change\" value (the middle of a pitch or time curve, ×1 on a volume curve, where the setting is set on a Set curve).",
                "RichButton", () => {
                    Change(() => { var v = neutral?.Invoke(); if (v.HasValue) foreach (var p in group) p.value = v.Value; });
                    editorWindow.Close();
                }, ZUICornerMask.All, 56f, RowH);
            reset.SetEnabled(neutral != null && neutral() != null);
            r.Add(reset);
            if (many) {
                r.Add(Gap(4f));
                r.Add(ZS.Button("Squash", "Brings the " + group.Count + " selected points' values closer together, around their average (each press: the distance from the average × 0.8). Stays open so you can press again.",
                    "RichButton", () => Spread(0.8f), ZUICornerMask.Left, 62f, RowH));
                r.Add(Gap(2f));
                r.Add(ZS.Button("Expand", "Spreads the " + group.Count + " selected points' values further apart, around their average (each press: the distance from the average × 1.25), never past the curve's top or bottom. Stays open so you can press again.",
                    "RichButton", () => Spread(1.25f), ZUICornerMask.Right, 62f, RowH));
            }
            r.Add(Gap(12f));
            bool on = point.randomX > 0f || point.randomY > 0f;
            r.Add(ZS.Toggle("Random", on
                    ? "Make this an ordinary point again: every play hears it exactly where it is drawn."
                    : "Let every play move this point somewhere inside an ellipse around where it is drawn (a different place each play, the same for the whole of one play). The ellipse is drawn around the point.",
                on, v => {
                    Change(() => {
                        if (v) { point.randomX = 0.05f * xRange; point.randomY = 0.1f * yRange(); if (point.randomBias <= 0f) point.randomBias = 0.5f; }
                        else { point.randomX = 0f; point.randomY = 0f; }
                    });
                    Build(root);
                }, "RichToggle", ZUICornerMask.All, 72f, RowH));
            if (on) {
                r.Add(Gap(8f));
                ZuiSkinSlider sx = null;
                float fx = point.randomX / xRange;
                sx = ZS.Slider("Across ±" + (fx * 100f).ToString("0") + " %", fx, 0f, 0.5f,
                    "How far each play may move the point along the curve, either way, as a share of the curve's length. Nought: it stays at its time. Points never pass their neighbours: a point stops halfway to the next one.",
                    v => { Change(() => point.randomX = v * xRange); sx.text = "Across ±" + (v * 100f).ToString("0") + " %"; },
                    ZuiSkinSlider.LabelMode.LabelOnly, 0.05f, "Default", 150f, RowH - 2f);
                r.Add(sx);
                r.Add(Gap(6f));
                ZuiSkinSlider sy = null;
                float yr = yRange() > 0f ? yRange() : 1f;
                float fy = point.randomY / yr;
                sy = ZS.Slider("Height ±" + (fy * 100f).ToString("0") + " %", fy, 0f, 0.5f,
                    "How far each play may move the point up or down, either way, as a share of the curve's height (on a pitch curve the full height is 48 semitones, so 10 % is about ±5 semitones). It never leaves the curve's range.",
                    v => { Change(() => point.randomY = v * (yRange() > 0f ? yRange() : 1f)); sy.text = "Height ±" + (v * 100f).ToString("0") + " %"; },
                    ZuiSkinSlider.LabelMode.LabelOnly, 0.1f, "Default", 150f, RowH - 2f);
                r.Add(sy);
                r.Add(Gap(6f));
                r.Add(ZS.Slider("Bias", point.randomBias, 0f, 1f,
                    "Where in the ellipse the point tends to land. 0.5: evenly anywhere in it. Towards 0: mostly near the middle, where it was drawn. Towards 1: mostly out near the edge.",
                    v => Change(() => point.randomBias = v), ZuiSkinSlider.LabelMode.LabelAndValue, 0.5f, "Default", 140f, RowH - 2f));
            }
            root.Add(r);
        }

        /// <summary>Squash (factor below one) or Expand (above one) the selected values around their average (T-0510).</summary>
        void Spread(float factor) {
            Change(() => {
                float mean = 0f; foreach (var p in group) mean += p.value; mean /= group.Count;
                var range = valueRange != null ? valueRange() : new Vector2(float.NegativeInfinity, float.PositiveInfinity);
                foreach (var p in group) p.value = Mathf.Clamp(mean + (p.value - mean) * factor, range.x, range.y);
            });
        }

        static VisualElement Gap(float w) { var e = new VisualElement(); e.style.width = w; e.style.flexShrink = 0; return e; }
    }
}
