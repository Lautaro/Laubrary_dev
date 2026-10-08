using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// A small waveform for a picker row or tile, drawn in the Settings tab's waveform colour on the waveform background.
    /// The peaks (one low/high pair per column) are worked out once per clip from the shared in-memory summary and kept
    /// in a tiny cache, at most a few clips per frame, so a list of thousands scrolls without a hitch and never decodes
    /// a clip nobody has looked at. A clip whose samples cannot be read on this machine shows a dotted midline.
    /// </summary>
    internal sealed class ZoundPickerThumbTK : VisualElement {

        const int Columns = 64;
        static readonly Dictionary<string, float[]> peaks = new Dictionary<string, float[]>();
        static readonly HashSet<string> unreadable = new HashSet<string>();
        static readonly Queue<(ZoundPickerItem item, ZoundPickerThumbTK thumb)> pending = new Queue<(ZoundPickerItem, ZoundPickerThumbTK)>();
        static bool pumping;

        ZoundPickerItem item;
        float[] mine;
        bool unknown;

        public ZoundPickerThumbTK() {
            AddToClassList("zs-picker__thumb");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Paint;
        }

        bool blank;

        public void Bind(ZoundPickerItem it) {
            item = it; mine = null; unknown = false;
            // A Zequence has no single waveform: its slot stays empty (no false "unreadable" dots), the kind chip says what it is.
            blank = it != null && it.clip == null && !it.missing && it.kind == ZoundPickerItem.Kind.Zequence;
            if (it == null || it.clip == null) { unknown = true; MarkDirtyRepaint(); return; }
            if (peaks.TryGetValue(it.key, out mine)) { MarkDirtyRepaint(); return; }
            if (unreadable.Contains(it.key)) { unknown = true; MarkDirtyRepaint(); return; }
            pending.Enqueue((it, this));
            Pump();
        }

        static void Pump() {
            if (pumping) return;
            pumping = true;
            EditorApplication.update += Step;   // every editor tick while anything is queued (delayCall may never fire in an idle editor)
        }

        /// <summary>A few clips per editor tick: enough to fill a screen in a moment, never enough to stall a scroll.</summary>
        static void Step() {
            int budget = 3;
            bool any = false;
            while (budget-- > 0 && pending.Count > 0) {
                var (it, thumb) = pending.Dequeue();
                if (!peaks.ContainsKey(it.key) && !unreadable.Contains(it.key)) {
                    var p = Compute(it);
                    if (p != null) peaks[it.key] = p; else unreadable.Add(it.key);
                }
                if (thumb != null && ReferenceEquals(thumb.item, it)) {
                    if (peaks.TryGetValue(it.key, out thumb.mine)) thumb.unknown = false; else thumb.unknown = true;
                    thumb.MarkDirtyRepaint();
                }
                any = true;
            }
            if (any) foreach (var w in Resources.FindObjectsOfTypeAll<ZoundPickerWindowTK>()) w.Repaint();
            if (pending.Count == 0) { EditorApplication.update -= Step; pumping = false; }
        }

        static float[] Compute(ZoundPickerItem it) {
            WaveSummary s;
            try { s = WaveSummary.For(it.clip); } catch { s = null; }
            if (s == null) return null;
            float len = s.LengthSeconds; if (len <= 0f) return null;
            float scale = 1f / s.Peak;
            var p = new float[Columns * 2];
            for (int c = 0; c < Columns; c++) {
                if (s.Range(len * c / Columns, len * (c + 1) / Columns, out float mn, out float mx)) { p[c * 2] = mn * scale; p[c * 2 + 1] = mx * scale; }
            }
            return p;
        }

        /// <summary>The peaks of a clip the details pane draws large; null while not yet computed (it is queued).</summary>
        public static float[] PeaksFor(ZoundPickerItem it) {
            if (it == null || it.clip == null) return null;
            if (peaks.TryGetValue(it.key, out var p)) return p;
            if (!unreadable.Contains(it.key)) { pending.Enqueue((it, null)); Pump(); }
            return null;
        }

        void Paint(MeshGenerationContext ctx) {
            float w = resolvedStyle.width, h = resolvedStyle.height;
            if (w < 2f || h < 2f || blank) return;
            var es = ZoundsProject.Instance != null ? ZoundsProject.Instance.projectSettings.editorStyle : null;
            var p2 = ctx.painter2D;
            var bg = es != null ? es.klipWaveformBGColor : new Color(0.12f, 0.12f, 0.2f, 1f);
            p2.fillColor = bg; p2.BeginPath(); p2.MoveTo(new Vector2(0, 0)); p2.LineTo(new Vector2(w, 0)); p2.LineTo(new Vector2(w, h)); p2.LineTo(new Vector2(0, h)); p2.ClosePath(); p2.Fill();
            var wave = es != null ? es.waveformColor : new Color(1f, 0.6f, 0.1f, 1f);
            float mid = h * 0.5f;
            if (mine == null) {
                // Not read yet (queued), or unreadable: a faint midline says "audio, no picture", never a blank square.
                p2.strokeColor = new Color(wave.r, wave.g, wave.b, unknown ? 0.25f : 0.5f); p2.lineWidth = 1f;
                p2.BeginPath();
                if (unknown) { for (float x = 2f; x < w; x += 6f) { p2.MoveTo(new Vector2(x, mid)); p2.LineTo(new Vector2(x + 3f, mid)); } }
                else { p2.MoveTo(new Vector2(0, mid)); p2.LineTo(new Vector2(w, mid)); }
                p2.Stroke();
                return;
            }
            p2.fillColor = wave;
            p2.BeginPath();
            float cw = w / Columns, half = (h - 2f) * 0.5f;
            // One filled polygon: the highs left to right, then the lows back.
            for (int c = 0; c < Columns; c++) { float x = c * cw; float y = mid - Mathf.Max(0.5f, mine[c * 2 + 1] * half); if (c == 0) p2.MoveTo(new Vector2(x, y)); else p2.LineTo(new Vector2(x, y)); p2.LineTo(new Vector2(x + cw, y)); }
            for (int c = Columns - 1; c >= 0; c--) { float x = c * cw; float y = mid - Mathf.Min(-0.5f, mine[c * 2] * half); p2.LineTo(new Vector2(x + cw, y)); p2.LineTo(new Vector2(x, y)); }
            p2.ClosePath(); p2.Fill();
        }
    }
}
