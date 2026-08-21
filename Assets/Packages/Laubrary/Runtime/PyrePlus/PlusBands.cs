// PlusBands — a HARD band table (cel shading): N (threshold, colour) entries; colour i paints every t from its
// threshold up to the next one, no interpolation. PlusRamp is the smooth LUT; this is the stepped one — a form
// whose source is a band table (Torch's seven cel bands, Arc Burst's five) holds a PlusBands, and ZuiReflect draws
// it as ONE row (a band-count slider + a stepped strip) instead of a card per stop.
//
// Storage is deliberately the same serialized shape as PlusRamp (a `stops` list of PlusRampStop): a layer saved
// while the field was still a PlusRamp loads straight into this type, and OnAfterDeserialize collapses the old
// repeated-position "step stop" encoding (the contract's way of writing a band set) into one entry per band.
// Not a Unity Gradient: that caps at 8 colour keys (Torch alone has 7 and the count slider goes past 8), and its
// Fixed mode colours a band by the key at its END, the reverse of Kiln's threshold-at-START rule.
//
// The renderer reads `Thresholds` / `Colors32` and calls PlusShade.Banded — the same arrays and the same lookup
// as before this type existed, so the control changes presentation, never bytes.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.PyrePlus
{
    [Serializable]
    public sealed class PlusBands : IZuiBands, ISerializationCallbackReceiver
    {
        public const int Max = 16;

        [Tooltip("One entry per band: its position is the threshold (in 0..1) where the band STARTS, its colour the band. Bands are hard — no blending between them.")]
        public List<PlusRampStop> stops = new List<PlusRampStop>();
        [Tooltip("Below the first threshold: nothing (transparent) instead of the first band. A form whose own rule cuts pixels under the floor sets this so the preview tells the truth.")]
        public bool transparentBelowFirst;

        // The lookup tables the renderer samples, rebuilt only when the content hash moves (an edit, an undo).
        [NonSerialized] float[] _thr;
        [NonSerialized] Color32[] _cols;
        [NonSerialized] int _tableHash;

        public PlusBands() { }
        public PlusBands(bool transparentBelowFirst) { this.transparentBelowFirst = transparentBelowFirst; }

        public int Count => stops?.Count ?? 0;
        public bool IsEmpty => Count == 0;
        int IZuiBands.MinCount => 1;
        int IZuiBands.MaxCount => Max;
        bool IZuiBands.FirstIsFloor => transparentBelowFirst;

        /// Ascending band-start thresholds (one per band; a single { 0 } white band when empty).
        public float[] Thresholds { get { EnsureTables(); return _thr; } }
        /// The band colours as Color32, index-aligned with Thresholds.
        public Color32[] Colors32 { get { EnsureTables(); return _cols; } }

        /// The band colour at t, as the renderer paints it (PlusShade.Banded); transparent below the floor when
        /// `transparentBelowFirst`.
        public Color Eval(float t)
        {
            EnsureTables();
            if (transparentBelowFirst && t < _thr[0]) return Color.clear;
            return PlusShade.Banded(t, _thr, _cols);
        }

        public float GetThreshold(int i) => stops[i].pos;
        public Color GetColor(int i) => stops[i].color;
        public void SetColor(int i, Color c) => stops[i].color = c;

        /// Move band i's threshold, kept strictly between its neighbours. Band 0 starts at 0 unless the region below
        /// it is a floor (transparentBelowFirst) — then it is the floor's position and editable.
        public void SetThreshold(int i, float t)
        {
            const float gap = 1e-3f;
            if (i == 0 && !transparentBelowFirst) { stops[0].pos = 0f; return; }
            float lo = i == 0 ? 0f : stops[i - 1].pos + gap;
            float hi = i + 1 < stops.Count ? stops[i + 1].pos - gap : 1f;
            stops[i].pos = Mathf.Clamp(t, lo, Mathf.Max(lo, hi));
        }

        /// Resample to n bands without moving what exists: growing splits the WIDEST band at its midpoint (the new
        /// band copies the colour, so the picture is unchanged until it is recoloured); shrinking removes the
        /// NARROWEST band (never band 0) and the band below absorbs its span. An empty table seeds n even grey bands.
        public void SetCount(int n)
        {
            n = Mathf.Clamp(n, 1, Max);
            stops ??= new List<PlusRampStop>();
            if (stops.Count == 0)
            {
                for (int k = 0; k < n; k++)
                {
                    float g = n == 1 ? 1f : k / (float)(n - 1);
                    stops.Add(new PlusRampStop(k / (float)n, new Color(g, g, g, 1f)));
                }
                return;
            }
            while (stops.Count < n)
            {
                int wide = 0; float best = -1f;
                for (int k = 0; k < stops.Count; k++)
                {
                    float w = (k + 1 < stops.Count ? stops[k + 1].pos : 1f) - stops[k].pos;
                    if (w > best) { best = w; wide = k; }
                }
                float end = wide + 1 < stops.Count ? stops[wide + 1].pos : 1f;
                stops.Insert(wide + 1, new PlusRampStop((stops[wide].pos + end) * 0.5f, stops[wide].color));
            }
            while (stops.Count > n)
            {
                int narrow = 1; float best = float.MaxValue;
                for (int k = 1; k < stops.Count; k++)
                {
                    float w = (k + 1 < stops.Count ? stops[k + 1].pos : 1f) - stops[k].pos;
                    if (w < best) { best = w; narrow = k; }
                }
                stops.RemoveAt(narrow);
            }
        }

        /// Read a step-encoded PlusRamp (the contract's band encoding: each boundary as a REPEATED position — the band
        /// before it, then the band starting there) or a plain one-stop-per-band ramp. Repeated positions collapse
        /// to the later stop, thresholds ascend, a closing stop at 1 is dropped. Lossless for any table this type writes (see ToStops).
        public static PlusBands FromStops(PlusRamp ramp, bool transparentBelowFirst = false)
        {
            var b = new PlusBands(transparentBelowFirst);
            if (ramp?.stops != null) foreach (var s in ramp.stops) b.stops.Add(new PlusRampStop(s.pos, s.color));
            b.Normalise();
            return b;
        }

        /// The bands as a PlusRamp of one stop per band (threshold, colour) — what FromStops reads back unchanged.
        public PlusRamp ToStops()
        {
            var r = new PlusRamp { space = PlusRampSpace.Srgb };
            foreach (var s in stops) r.stops.Add(new PlusRampStop(s.pos, s.color));
            return r;
        }

        public PlusBands Clone()
        {
            var c = new PlusBands(transparentBelowFirst);
            if (stops != null) foreach (var s in stops) c.stops.Add(new PlusRampStop(s.pos, s.color));
            return c;
        }

        /// A stable hash of the table's content (what the cached lookup tables are keyed by).
        public int ContentHash()
        {
            unchecked
            {
                int h = (int)2166136261u ^ (transparentBelowFirst ? 1 : 0);
                if (stops != null) foreach (var s in stops) { h = (h ^ s.pos.GetHashCode()) * 16777619; h = (h ^ s.color.GetHashCode()) * 16777619; }
                return h;
            }
        }

        /// Sort by position (stable) and collapse repeated positions to the later stop — the band that STARTS there.
        void Normalise()
        {
            if (stops == null || stops.Count < 2) return;
            var idx = new int[stops.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            var src = stops;
            Array.Sort(idx, (a, b) => { int c = src[a].pos.CompareTo(src[b].pos); return c != 0 ? c : a.CompareTo(b); });
            var outList = new List<PlusRampStop>(stops.Count);
            foreach (int i in idx)
            {
                var st = src[i];
                if (outList.Count > 0 && outList[outList.Count - 1].pos == st.pos) outList[outList.Count - 1] = st;
                else outList.Add(st);
            }
            stops = outList;
            // The contract closes a band set with a stop AT position 1 repeating the top band's colour (the ramp's
            // "top"); as a band it would be zero-width and the same colour, so it is not a band — drop it.
            int n = stops.Count;
            if (n >= 2 && stops[n - 1].pos >= 1f && stops[n - 1].color == stops[n - 2].color) stops.RemoveAt(n - 1);
        }

        void EnsureTables()
        {
            int h = ContentHash();
            if (_thr != null && _tableHash == h) return;
            _tableHash = h;
            if (stops == null || stops.Count == 0) { _thr = new[] { 0f }; _cols = new[] { new Color32(255, 255, 255, 255) }; return; }
            _thr = new float[stops.Count]; _cols = new Color32[stops.Count];
            for (int i = 0; i < stops.Count; i++) { _thr[i] = stops[i].pos; _cols[i] = stops[i].color; }
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }
        void ISerializationCallbackReceiver.OnAfterDeserialize() => Normalise();
    }
}
