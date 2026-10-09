using System;
using System.Collections.Generic;
using Laubrary.Zounds.Dsp;
using UnityEngine;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// One placed piece on a Zequence's timeline, as drawn (T-0560, T-0562): where it starts in Zequence time, at what
    /// speed it is drawn, which part of its source it plays, and how that source maps onto time.
    ///
    /// The drawing is a prediction on the shared time axis: pitch and time curves and a stretch warp it exactly as they
    /// warp the play (one integration, shared with the play length); a per-play pitch range is drawn at the middle of the
    /// range, with the spread shown as a band at the end; game-code and modulated values at rest. The live playheads
    /// show where each play really is.
    /// </summary>
    internal sealed class TrackPlacement {
        public CompositeZound parent;
        public CompositeZound.ZoundEntry entry;
        public int index, depth;
        public Zound zound;
        public Klip klip;                 // null for a nested Zequence
        public bool found;
        /// <summary>Zequence seconds where this piece starts sounding, and where its parent starts (its delay counts from there).</summary>
        public float start, parentStart;
        /// <summary>The play speed it is drawn at (its pitch at the middle of every range), and the range's two ends.</summary>
        public float pitch = 1f, pitchLo = 1f, pitchHi = 1f;
        /// <summary>The parent's drawn pitch: a delay in the data is this many times the delay in seconds.</summary>
        public float parentPitch = 1f;
        /// <summary>The part of the source the piece plays, in source seconds, and the whole file's length.</summary>
        public float exA, exB, fileLen;
        public bool ownExcerpt;
        /// <summary>The chain's declared tail (a reverb still ringing after the source runs out), in seconds.</summary>
        public float tail;
        /// <summary>For a nested Zequence: its drawn length.</summary>
        public float groupLength;
        public AudioClip sourceClip;

        const int MapPoints = 129;
        readonly float[] mapSrc = new float[MapPoints], mapPlay = new float[MapPoints];
        bool mapped;

        public float PlayLength => klip != null ? (mapped ? mapPlay[MapPoints - 1] / Mathf.Max(pitch, 0.01f) : (exB - exA) / Mathf.Max(pitch, 0.01f)) : groupLength;
        public float End => start + PlayLength;
        public float EndWithTail => End + tail;
        public bool IsKlip => klip != null;

        /// <summary>The time this source second sounds at; outside the excerpt, where it WOULD sound at the drawn speed.</summary>
        public float SourceToTime(float s) {
            float p = Mathf.Max(pitch, 0.01f);
            if (!mapped || s <= exA) return start + (s - exA) / p;
            if (s >= exB) return End + (s - exB) / p;
            float f = (s - exA) / Mathf.Max(exB - exA, 1e-6f) * (MapPoints - 1);
            int i = Mathf.Clamp((int)f, 0, MapPoints - 2);
            float play = Mathf.Lerp(mapPlay[i], mapPlay[i + 1], f - i);
            return start + play / p;
        }

        /// <summary>The source second sounding at Zequence time <paramref name="t"/> (the inverse of <see cref="SourceToTime"/>).</summary>
        public float TimeToSource(float t) {
            float p = Mathf.Max(pitch, 0.01f);
            if (!mapped || t <= start) return exA + (t - start) * p;
            if (t >= End) return exB + (t - End) * p;
            float play = (t - start) * p;
            int lo = 0, hi = MapPoints - 1;
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (mapPlay[mid] <= play) lo = mid; else hi = mid; }
            float span = mapPlay[hi] - mapPlay[lo];
            float w = span > 1e-9f ? (play - mapPlay[lo]) / span : 0f;
            return Mathf.Lerp(mapSrc[lo], mapSrc[hi], w);
        }

        /// <summary>Works out the map from the engine (its source, its trim or excerpt, its curves).</summary>
        public void Map() {
            mapped = false;
            if (klip == null) return;
            var ex = ownExcerpt ? ZoundSapPlayback.Excerpt.Of(exA, exB) : default;
            if (ZoundSapPlayback.TryMapSourceToPlay(klip, ex, mapSrc, mapPlay, out float file)) {
                fileLen = file;
                exA = mapSrc[0]; exB = mapSrc[MapPoints - 1];
                mapped = true;
            }
        }
    }

    /// <summary>
    /// The Zequence window's shared timeline (T-0562..T-0565): ONE visible time window for every track, the selection,
    /// the Follow / Ripple / Loop switches, the focused track, and every track's placement. View state only -- nothing
    /// here is saved with the sound, and none of it takes an undo step.
    /// </summary>
    internal sealed class ZequenceTimeline {

        public Zequence zeq;
        public readonly List<TrackPlacement> tracks = new List<TrackPlacement>();
        public readonly Dictionary<CompositeZound.ZoundEntry, TrackPlacement> byEntry = new Dictionary<CompositeZound.ZoundEntry, TrackPlacement>();

        /// <summary>The visible window, in Zequence seconds. While <see cref="fitted"/>, it follows the content.</summary>
        public float t0, t1 = 1f;
        public bool fitted = true;
        /// <summary>Where the content ends (the last piece's end, with its tail), where the last piece's audio ends (no tail),
        /// and the authored duration.</summary>
        public float contentEnd, piecesEnd, authored;
        /// <summary>The Zequence's length follows its tracks (Auto length): the authored duration then plays no part in the fit.</summary>
        public bool autoLength = true;
        /// <summary>Auto-tidy (a right-click on Tidy): the view always shows the whole Zequence, fitted exactly, and cannot be
        /// zoomed or dragged.</summary>
        public bool autoTidy;
        /// <summary>The view may be zoomed or moved (false while auto-tidy holds it).</summary>
        public bool CanMoveView => !autoTidy;

        public bool hasSel;
        public float selA, selB;
        public readonly HashSet<CompositeZound.ZoundEntry> selTracks = new HashSet<CompositeZound.ZoundEntry>();

        public bool follow, ripple, loop;
        /// <summary>Tracks that were sounding across a ripple insert or delete and stayed where they were (marked until the next edit).</summary>
        public readonly HashSet<CompositeZound.ZoundEntry> straddling = new HashSet<CompositeZound.ZoundEntry>();
        public CompositeZound.ZoundEntry focus;
        /// <summary>Which of a track's sound curves is being edited on the track (at most one per track; none = the track's own gestures).</summary>
        public readonly Dictionary<CompositeZound.ZoundEntry, ZoundModifier> editingCurve = new Dictionary<CompositeZound.ZoundEntry, ZoundModifier>();

        /// <summary>The Zequence time the window's own play is at, or below zero when nothing plays.</summary>
        public float cursor = -1f;

        /// <summary>The lane every track draws in, in world (panel) coordinates, set by the ruler.</summary>
        public Rect laneWorld;

        /// <summary>The one-line readout (selection, notices); its slot is always there, only the text changes.</summary>
        public string readout = "";

        public event Action changed;
        public void Changed() => changed?.Invoke();

        public float Span => Mathf.Max(1e-4f, t1 - t0);
        public float TimeToLaneX(float t) => (t - t0) / Span * laneWorld.width;
        public float LaneXToTime(float x) => t0 + x / Mathf.Max(1f, laneWorld.width) * Span;
        public float SecondsPerPixel => Span / Mathf.Max(1f, laneWorld.width);

        /// <summary>
        /// Where a fitted view ends: exactly where the last piece's audio ends. The authored duration counts only when the
        /// length is set by hand (Auto length off); with Auto length it is worked out at the SLOWEST pitch of every range
        /// (the longest a play can last), while the pieces are drawn at the middle of their range, so fitting to it left a
        /// gap after the last trim. A chain's tail is not part of the fit either (it rings after the trim end).
        /// </summary>
        public float FitEnd => Mathf.Max(0.05f, autoLength ? (piecesEnd > 0f ? piecesEnd : contentEnd) : Mathf.Max(piecesEnd, authored));

        public static float Mid(Zound z) => z == null ? 1f : 0.5f * (z.minPitch + z.maxPitch);
        static float Factor(CompositeZound.ZoundEntry e, Zound z) => e.overridePitch ? e.pitch : e.pitch * Mid(z);

        /// <summary>Works out every track's placement (5 Hz, and after every edit).</summary>
        public void Rebuild() {
            tracks.Clear(); byEntry.Clear();
            contentEnd = 0f; piecesEnd = 0f;
            if (zeq == null) return;
            float zp = Mid(zeq);
            authored = zeq.editor_maxDuration / Mathf.Max(zeq.minPitch, 0.01f);
            Add(zeq, 0f, zp, 0);
            if (autoTidy) fitted = true;
            if (fitted) { t0 = 0f; t1 = FitEnd; }
        }

        float Add(CompositeZound parent, float parentStart, float parentPitch, int depth) {
            float end = parentStart;
            for (int i = 0; i < parent.zoundEntries.Count; i++) {
                var e = parent.zoundEntries[i];
                var p = new TrackPlacement { parent = parent, entry = e, index = i, depth = depth, parentPitch = parentPitch, parentStart = parentStart };
                p.found = parent.TryGetEntryZound(e, out p.zound);
                p.start = parentStart + e.delay / Mathf.Max(parentPitch, 0.01f);
                tracks.Add(p); byEntry[e] = p;
                if (!p.found) continue;
                float f = Factor(e, p.zound);
                p.pitch = parentPitch * f;
                float lo = e.overridePitch ? e.pitch : e.pitch * p.zound.minPitch, hi = e.overridePitch ? e.pitch : e.pitch * p.zound.maxPitch;
                p.pitchLo = parentPitch * lo; p.pitchHi = parentPitch * hi;
                if (p.zound is Klip k) {
                    p.klip = k;
                    p.sourceClip = ZoundSapPlayback.LoadSourceClip(k, out _);
                    p.ownExcerpt = e.ownTrim && e.trimEnd > e.trimStart;
                    if (p.ownExcerpt) { p.exA = e.trimStart; p.exB = e.trimEnd; }
                    p.Map();
                    if (!p.ownExcerpt && p.fileLen <= 0f && p.sourceClip != null) { p.fileLen = p.sourceClip.length; p.exA = 0f; p.exB = p.fileLen; }
                    var chain = ZoundSapPlayback.ResolveChainForPlayback(k);
                    p.tail = chain != null && !chain.IsEmpty ? TailOf(k, chain) : 0f;
                    end = Mathf.Max(end, p.EndWithTail);
                    piecesEnd = Mathf.Max(piecesEnd, p.End);
                }
                else if (p.zound is CompositeZound cz && !ZoundHandlerRecursion(cz, parent)) {
                    int before = tracks.Count;
                    float childEnd = Add(cz, p.start, p.pitch, depth + 1);
                    // A shared (not local) nested Zequence is one block: its own tracks are not listed as ours.
                    if (!e.local) { tracks.RemoveRange(before, tracks.Count - before); }
                    p.groupLength = Mathf.Max(0f, childEnd - p.start);
                    end = Mathf.Max(end, childEnd);
                }
            }
            contentEnd = Mathf.Max(contentEnd, end);
            return end;
        }

        static bool ZoundHandlerRecursion(CompositeZound child, CompositeZound parent) => ReferenceEquals(child, parent) || ZequenceHandler.CheckRecursiveness(child, parent);

        static float TailOf(Klip k, ZoundEffectChain chain) {
            try { var L = ZoundDspPlayback.GetLayoutFor(chain, k, AudioSettings.outputSampleRate); return L != null ? L.tailSeconds : 0f; }
            catch { return 0f; }
        }

        // ─────────────────────────── the window ───────────────────────────

        public void ZoomAround(float t, float factor) {
            if (!CanMoveView) return;
            float span = Mathf.Clamp(Span * factor, 0.005f, Mathf.Max(FitEnd * 4f, 1f));
            float w = Mathf.Clamp01((t - t0) / Span);
            t0 = t - w * span; t1 = t0 + span;
            Clamp(); fitted = false; Changed();
        }

        public void Pan(float seconds) { if (!CanMoveView) return; t0 += seconds; t1 += seconds; Clamp(); fitted = false; Changed(); }

        public void Show(float a, float b) {
            if (!CanMoveView) return;
            if (b - a < 0.005f) { float m = 0.5f * (a + b); a = m - 0.0025f; b = m + 0.0025f; }
            float pad = (b - a) * 0.04f;
            t0 = a - pad; t1 = b + pad; Clamp(); fitted = false; Changed();
        }

        public void Fit() { fitted = true; t0 = 0f; t1 = FitEnd; Changed(); }

        void Clamp() {
            float span = Span;
            float lo = -span * 0.5f, hi = FitEnd + span * 0.5f;
            if (t0 < lo) { t0 = lo; t1 = lo + span; }
            if (t1 > hi && hi - span > lo) { t1 = hi; t0 = hi - span; }
        }

        /// <summary>Pages the window along with the cursor when Follow is on (a window's width at a time, never sliding).</summary>
        public void FollowCursor() {
            if (!follow || cursor < 0f || fitted) return;
            if (cursor > t1 && cursor < t1 + Span * 0.5f) { float s = Span; t0 = t1; t1 = t0 + s; Changed(); }
        }

        // ─────────────────────────── selection ───────────────────────────

        public void Select(float a, float b, CompositeZound.ZoundEntry track, bool add) {
            if (!add) selTracks.Clear();
            if (track != null) selTracks.Add(track);
            hasSel = true; selA = Mathf.Min(a, b); selB = Mathf.Max(a, b);
            Changed();
        }

        public void ClearSelection() { hasSel = false; selTracks.Clear(); Changed(); }

        public static string Seconds(float s) => s.ToString(Mathf.Abs(s) < 10f ? "0.000" : "0.00") + " s";
    }
}
