// ZuiEnvelopeGenerators — point-pattern GENERATORS for a Curve-mode ZUIValue: Oscillation (a triangle/sine
// wave between two values, with N peaks) and Cycles (a sawtooth ramp that snaps back, for a wrapping value).
// They are TOOLS, not modes — each writes the value's points (Oscillation also sets its smoothness), so the
// result is an ordinary envelope the user can still hand-tweak afterwards. Opened from the Curve ⋯ menu.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    static class ZuiEnvelopeGenerators
    {
        // ── pure point generation (also handy to unit-test without UI) ─────────────────────
        /// An oscillation between start and end with `reps` peaks (reaches `end` reps times) over [0,1].
        public static List<ZUIEnvelopePoint> Oscillate(float start, float end, int reps)
        {
            var pts = new List<ZUIEnvelopePoint>();
            int segs = Mathf.Max(1, reps) * 2;
            for (int k = 0; k <= segs; k++)
                pts.Add(new ZUIEnvelopePoint(k / (float)segs, (k % 2 == 0) ? start : end));
            return pts;
        }

        /// A sawtooth of `cycles` ramps start→end, each snapping back at its boundary — reads as continuous
        /// motion on a WRAPPING value (looping the envelope closes the final snap).
        public static List<ZUIEnvelopePoint> Sawtooth(float start, float end, int cycles)
        {
            var pts = new List<ZUIEnvelopePoint>();
            int n = Mathf.Max(1, cycles);
            const float eps = 0.0006f;
            for (int c = 0; c < n; c++)
            {
                float t0 = c / (float)n, t1 = (c + 1) / (float)n;
                pts.Add(new ZUIEnvelopePoint(t0, start));
                pts.Add(new ZUIEnvelopePoint(Mathf.Max(t0 + eps, t1 - eps), end));
            }
            return pts;
        }

        // ── Oscillation generator popover ──────────────────────────────────────────────────
        public static void OpenOscillation(VisualElement anchor, ZUIValue value, float absMin, float absMax,
            Action onBeforeMutate, Action onApplied)
        {
            if (anchor == null || value == null) return;
            float lo = Mathf.Min(absMin, absMax), hi = Mathf.Max(absMin, absMax);
            float start = value.yMin, end = value.yMax;
            int reps = 3;
            float smooth = value.smoothness;
            var holder = new ZuiPopover[1];
            Action close = () => holder[0]?.Close();

            holder[0] = ZuiPopover.Show(anchor, panel =>
            {
                panel.style.paddingLeft = panel.style.paddingRight = 6f;
                panel.style.paddingTop = panel.style.paddingBottom = 6f;

                var preview = new GenPreview();
                void Regen() => preview.Set(Oscillate(start, end, reps), lo, hi, smooth);

                panel.Add(Title("Oscillation", "A wave between two values, repeated over the envelope's life."));
                panel.Add(preview);
                panel.Add(Z.MicroSlider("Start", start, absMin, absMax, "Value at the troughs.",
                    v => { start = v; Regen(); }, 200f));
                panel.Add(Z.MicroSlider("End", end, absMin, absMax, "Value at the peaks.",
                    v => { end = v; Regen(); }, 200f));
                panel.Add(Z.MicroSlider("Reps", reps, 1f, 8f, "How many peaks over the envelope's life.",
                    v => { reps = Mathf.RoundToInt(v); Regen(); }, 200f, decimals: 0));
                panel.Add(Z.MicroSlider("Smooth", smooth, 0f, 1f, "0 = hard triangle corners, 1 = rounded (sine-like).",
                    v => { smooth = v; Regen(); }, 200f));
                panel.Add(Z.Button("Apply", "Write this oscillation into the curve.", () =>
                {
                    onBeforeMutate?.Invoke();
                    value.points.Clear(); value.points.AddRange(Oscillate(start, end, reps));
                    value.smoothness = smooth;
                    onApplied?.Invoke();
                    close();
                }));
                Regen();
            }, new ZuiPopover.Options { minWidth = 224f });
        }

        // ── Cycles generator popover ───────────────────────────────────────────────────────
        public static void OpenCycles(VisualElement anchor, ZUIValue value, float absMin, float absMax,
            Action onBeforeMutate, Action onApplied)
        {
            if (anchor == null || value == null) return;
            float lo = Mathf.Min(absMin, absMax), hi = Mathf.Max(absMin, absMax);
            float start = value.yMin, end = value.yMax;
            int cycles = 3;
            var holder = new ZuiPopover[1];
            Action close = () => holder[0]?.Close();

            holder[0] = ZuiPopover.Show(anchor, panel =>
            {
                panel.style.paddingLeft = panel.style.paddingRight = 6f;
                panel.style.paddingTop = panel.style.paddingBottom = 6f;

                var preview = new GenPreview();
                void Regen() => preview.Set(Sawtooth(start, end, cycles), lo, hi, 0f);

                panel.Add(Title("Cycles", "A sawtooth that snaps back — continuous motion on a wrapping value."));
                panel.Add(preview);
                panel.Add(Z.MicroSlider("From", start, absMin, absMax, "Value each cycle starts at.",
                    v => { start = v; Regen(); }, 200f));
                panel.Add(Z.MicroSlider("To", end, absMin, absMax, "Value each cycle ramps to before snapping back.",
                    v => { end = v; Regen(); }, 200f));
                panel.Add(Z.MicroSlider("Cycles", cycles, 1f, 12f, "How many ramps over the envelope's life.",
                    v => { cycles = Mathf.RoundToInt(v); Regen(); }, 200f, decimals: 0));
                panel.Add(Z.Button("Apply", "Write this cycle sawtooth into the curve (turns Loop on so the last snap wraps).", () =>
                {
                    onBeforeMutate?.Invoke();
                    value.points.Clear(); value.points.AddRange(Sawtooth(start, end, cycles));
                    value.smoothness = 0f;                              // a sawtooth must stay hard-cornered
                    if (value.cooldown < 0f) value.cooldown = 0f;       // enable looping so the wrap reads continuous
                    onApplied?.Invoke();
                    close();
                }));
                Regen();
            }, new ZuiPopover.Options { minWidth = 224f });
        }

        static Label Title(string text, string tooltip)
        {
            var l = Z.Text(text, ZuiText.Small, tooltip);   // Small (not Section) — a Section label would fold on click
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginBottom = 4f;
            return l;
        }

        // A live preview thumbnail that repaints when its points/range/smoothness change.
        sealed class GenPreview : VisualElement
        {
            List<ZUIEnvelopePoint> _pts;
            float _yMin, _yMax, _smooth;
            public GenPreview()
            {
                AddToClassList("zui-envelope");
                style.height = 58f; style.marginBottom = 6f;
                generateVisualContent += Paint;
            }
            public void Set(List<ZUIEnvelopePoint> pts, float yMin, float yMax, float smooth)
            {
                _pts = pts; _yMin = yMin; _yMax = yMax; _smooth = smooth;
                MarkDirtyRepaint();
            }
            void Paint(MeshGenerationContext mgc)
            {
                var r = contentRect;
                if (!(r.width > 4f) || _pts == null || _pts.Count == 0) return;
                float lo = Mathf.Min(_yMin, _yMax), hi = Mathf.Max(_yMin, _yMax);
                if (hi - lo < 1e-4f) hi = lo + 1f;
                var p2 = mgc.painter2D;
                p2.strokeColor = new Color(0.4f, 0.85f, 1f);
                p2.lineWidth = 1.3f;
                p2.BeginPath();
                const int Samples = 96;
                for (int s = 0; s <= Samples; s++)
                {
                    float t = s / (float)Samples;
                    float v = ZUIEnvelopeEvaluator.Evaluate(_pts, t, hi, _smooth);
                    float ty = Mathf.Clamp01(Mathf.InverseLerp(lo, hi, v));
                    var pt = new Vector2(r.x + 2f + t * (r.width - 4f), r.yMax - 2f - ty * (r.height - 4f));
                    if (s == 0) p2.MoveTo(pt); else p2.LineTo(pt);
                }
                p2.Stroke();
            }
        }
    }
}
