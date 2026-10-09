using System;
using System.Collections.Generic;
using Laubrary.Zounds.Dsp;
using UnityEngine;

namespace Laubrary.Zounds.Destructive {

    /// <summary>
    /// What an edit of a file did to its timeline, in seconds of that file: the audio from <see cref="at"/> for
    /// <see cref="removed"/> seconds was replaced by <see cref="inserted"/> seconds of new audio. A cut removes and inserts
    /// nothing; an insert removes nothing; an overwrite replaces as much as it writes (more is added only past the end).
    /// <see cref="overwrite"/> marks the last: nothing moves, the new audio simply sits where the old did.
    /// </summary>
    internal struct AudioSpan {
        public double at, removed, inserted;
        public double oldLength, newLength;
        public bool overwrite;
        public double Shift => inserted - removed;
    }

    /// <summary>
    /// Keeps everything that points at seconds of a file on the same audio after a destructive edit of that file, the
    /// way a DAW ripples its regions: a sound's trim, the points of its curves that follow the waveform (its own Volume,
    /// Pitch and Time curves and any waveform-time curve in its chain), a time-stretch region, and every Zequence track that
    /// plays its own excerpt of the sound.
    ///
    /// The rule for a moment t (seconds of the file): before the edit it stays; after the replaced audio it moves by the
    /// change in length; inside audio that was cut it closes onto the cut point. A START of something (a trim or excerpt
    /// start) sitting exactly where audio is inserted stays, so the new audio falls inside; an END sitting exactly there
    /// moves, so the new audio is inside too -- repeating a hit that ends right at the trim end keeps the repeat audible.
    /// Something that ended at the end of the file still ends at the end of the file. A curve's extra time after the audio
    /// travels with the end. A curve keeps its exact value over every bit of untouched audio: its segments are split at the
    /// edit's edges first (exact for straight segments), points inside cut audio go, and inserted audio carries the curve's
    /// value at the insert point, flat. An overwrite moves nothing.
    /// </summary>
    internal static class AudioRipple {

        const double Eps = 1e-7;

        public static double MapStart(double t, in AudioSpan e) => Map(t, e, false);
        public static double MapEnd(double t, in AudioSpan e) => Map(t, e, true);

        static double Map(double t, in AudioSpan e, bool isEnd) {
            if (isEnd && t >= e.oldLength - 1e-5) return e.newLength;          // ended at the end of the file: still does
            if (e.overwrite) return t > e.oldLength ? t + (e.newLength - e.oldLength) : t;
            double a = e.at, b = e.at + e.removed;
            if (e.removed <= 0d) {                                             // an insert
                if (t < a - Eps) return t;
                if (t <= a + Eps) return isEnd ? t + e.inserted : t;
                return t + e.inserted;
            }
            if (t <= a + Eps) return t;
            if (t >= b - Eps) return t + e.Shift;
            return a + Math.Min(t - a, e.inserted);
        }

        /// <summary>Ripples every reference to the file into seconds of the edited file, for each of <paramref name="users"/>
        /// (the sounds that play it) and every track, anywhere in the project, that plays its own excerpt of one of them.</summary>
        public static void Apply(IEnumerable<Klip> users, in AudioSpan e, int rate) {
            var ids = new HashSet<int>();
            foreach (var k in users) {
                if (k == null) continue;
                ids.Add(k.id);
                Klip(k, e, rate);
            }
            var span = e;
            ZoundsProject.Instance.zoundLibrary.ForEachZound(z => {
                if (!(z is CompositeZound c)) return;
                foreach (var entry in c.zoundEntries) {
                    if (!ids.Contains(entry.zoundId) || !entry.ownTrim) continue;
                    entry.trimStart = (float)MapStart(entry.trimStart, span);
                    entry.trimEnd = (float)MapEnd(entry.trimEnd, span);
                }
            });
        }

        /// <summary>
        /// Before the file changes: curves still on the old "fraction of the trim" footing move onto the file's own seconds
        /// (the project's usual first-edit conversion, which reads the file's length -- so it must see the old file). That is
        /// what lets them ripple exactly afterwards.
        /// </summary>
        public static void Prepare(IEnumerable<Klip> users) {
            foreach (var k in users) if (k != null) KlipChainEnvelopes.EnsureSourceAnchored(k);
        }

        /// <summary>One sound: its trim, its waveform-following curves, its time-stretch region. Call <see cref="Prepare"/>
        /// before the file is changed.</summary>
        public static void Klip(Klip k, in AudioSpan e, int rate) {
            float oldEnd = k.trimEnd > k.trimStart && k.trimEnd < e.oldLength - 1e-5 ? k.trimEnd : (float)e.oldLength;
            k.trimStart = (float)MapStart(k.trimStart, e);
            k.trimEnd = (float)MapEnd(oldEnd, e);
            if (k.timeStretch != null && k.timeStretch.regionEnd > k.timeStretch.regionStart) {
                k.timeStretch.regionStart = (float)MapStart(k.timeStretch.regionStart, e);
                k.timeStretch.regionEnd = (float)MapEnd(k.timeStretch.regionEnd, e);
            }
            if (k.chainPresetId == 0 && k.effectChain != null)
                foreach (var m in k.effectChain.modifiers) Curve(m, e, rate);
            if (k.ownCurves != null)
                foreach (int p in new[] { SourceStageParam.Volume, SourceStageParam.Pitch, SourceStageParam.Speed }) {
                    var slot = k.ownCurves.Of(p);
                    if (slot.Has) Curve(slot.modifier, e, rate);
                }
            KlipChainEnvelopes.Touch(k);
            ZoundDspPlayback.InvalidateLayout(k);
        }

        /// <summary>A curve that follows the waveform on the file's own seconds (<see cref="CurveAnchor.Source"/>). Others
        /// (a curve on its own time base, or one a shared preset keeps on the trim) are left as they are.</summary>
        public static bool Curve(ZoundModifier m, in AudioSpan e, int rate) {
            if (m == null || m.curve == null || !CurveAnchor.FollowsWaveform(m) || m.curveAnchor != CurveAnchor.Source) return false;
            double extra = Math.Max(0f, m.Param(0));
            double total0 = e.oldLength + extra, total1 = e.newLength + extra;
            if (total0 <= 0d || total1 <= 0d) return false;
            var env = m.curve;
            var pts = env.GetPointsList();
            double a = e.at, b = e.at + e.removed;
            if (!e.overwrite) {
                SplitAt(env, pts, a / total0);
                if (e.removed > 0d) SplitAt(env, pts, b / total0);
            }
            var outPts = new List<ZUIEnvelopePoint>(pts.Count + 1);
            double sample = rate > 0 ? 1d / rate : 1e-5;
            foreach (var p in pts) {
                double s = p.time * total0;
                double s1;
                if (s > e.oldLength + Eps) s1 = s + (e.newLength - e.oldLength);             // the extra time travels with the end
                else if (e.overwrite) s1 = s;
                else if (e.removed > 0d) {
                    if (s > a + Eps && s < b - Eps) continue;                                // on audio that was cut
                    s1 = s >= b - Eps ? s + e.Shift : s;
                    // The two edges meet at the cut: the audio from it on is what followed the cut, so the far edge's value
                    // starts there, and the near edge's value ends one sample before.
                    if (Math.Abs(s - a) <= Eps && e.inserted <= 0d) s1 = Math.Max(0d, a - sample);
                }
                else {
                    if (s < a - Eps) s1 = s;
                    else if (s <= a + Eps) {                                                 // the insert point: the new audio is flat at its value
                        p.time = (float)(s / total1);
                        p.randomX = (float)(p.randomX * total0 / total1);
                        outPts.Add(p);
                        if (e.inserted > 0d) outPts.Add(new ZUIEnvelopePoint((float)((s + e.inserted) / total1), p.value, 1f));
                        continue;
                    }
                    else s1 = s + e.inserted;
                }
                p.time = (float)Math.Min(1d, Math.Max(0d, s1 / total1));
                p.randomX = (float)(p.randomX * total0 / total1);
                outPts.Add(p);
            }
            // Two points left at the very same moment (a cut whose edges carried equal values) say nothing twice.
            for (int i = outPts.Count - 1; i > 0; i--)
                if (Math.Abs(outPts[i].time - outPts[i - 1].time) < 1e-9f && Math.Abs(outPts[i].value - outPts[i - 1].value) < 1e-7f) outPts.RemoveAt(i);
            pts.Clear(); pts.AddRange(outPts);
            if (pts.Count > 0 && pts[0].time > 0f) pts.Insert(0, new ZUIEnvelopePoint(0f, pts[0].value, 1f));
            if (pts.Count > 0 && pts[pts.Count - 1].time < 1f) pts.Add(new ZUIEnvelopePoint(1f, pts[pts.Count - 1].value, 1f));
            return true;
        }

        /// <summary>Puts a point at <paramref name="x"/> with the curve's own value there, unless one is already there.</summary>
        static void SplitAt(Envelope env, List<ZUIEnvelopePoint> pts, double x) {
            if (x <= 0d || x >= 1d) return;
            for (int i = 0; i < pts.Count; i++) {
                if (Math.Abs(pts[i].time - x) < 1e-7) return;
                if (pts[i].time > x) {
                    if (i == 0) return;
                    float v = env.Evaluate((float)x);
                    pts.Insert(i, new ZUIEnvelopePoint((float)x, v, pts[i].exponent));
                    return;
                }
            }
        }
    }
}
