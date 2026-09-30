using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// Where a waveform-following curve's points sit on the audio (T-0501, T-0560). A curve's x always runs 0..1; what 0..1
    /// MEANS depends on the modifier's <see cref="ZoundModifier.curveAnchor"/>:
    ///
    /// <b>Trim (0, every sound saved before this existed).</b> 0..r is the trimmed region (a point is "30% of the way through
    /// the trim"), r..1 the extra time after it, where r = trimmed length / (trimmed length + extra time). Moving a trim
    /// edge therefore slides every point onto different audio.
    ///
    /// <b>Source (1).</b> 0..S/(S+E) is the whole source file in seconds (a point is "2.41 s into the file"), and past S is
    /// "seconds after the trim end" for the extra time, where S = the source's length and E = the extra time. A point over
    /// "boom" stays on "boom" through any re-trim, split or slip; points outside the trim are simply not heard; the tail
    /// travels with the end of the piece. A sound converts from Trim to Source on its first curve or trim edit
    /// (<see cref="ConvertToSource"/>), an exact rescale for every point over the audio.
    ///
    /// Everything that reads such a curve (the voice, the length calculations, the waveform and chain-card drawings) goes
    /// through the two mapping functions here, so the rule lives in one place.
    /// </summary>
    public static class CurveAnchor {

        public const int Trim = 0, Source = 1;

        /// <summary>A sound's source as a curve sees it, in source seconds.</summary>
        public struct Axis {
            public float trimStart, trimEnd, sourceLength;
            public float TrimLength => Mathf.Max(0f, trimEnd - trimStart);
            public bool Valid => sourceLength > 0f && trimEnd > trimStart;

            /// <summary>The axis of a Klip whose source is <paramref name="sourceLength"/> seconds long.</summary>
            public static Axis Of(Klip k, float sourceLength) {
                float s = Mathf.Max(0f, sourceLength);
                float a = k != null && k.trimEnabled ? Mathf.Clamp(k.trimStart, 0f, s) : 0f;
                float b = k != null && k.trimEnabled && k.trimEnd > k.trimStart ? Mathf.Min(k.trimEnd, s) : s;
                if (b <= a) { a = 0f; b = s; }
                return new Axis { trimStart = a, trimEnd = b, sourceLength = s };
            }

            /// <summary>A trimmed region treated as the whole source (when the file's length is not known).</summary>
            public static Axis OfRegion(float regionSeconds) => new Axis { trimStart = 0f, trimEnd = regionSeconds, sourceLength = regionSeconds };
        }

        /// <summary>Whether a modifier's curve follows the waveform (an Envelope on the Waveform time base).</summary>
        public static bool FollowsWaveform(ZoundModifier m)
            => m != null && m.type == ZoundModifierType.Envelope && (m.p == null || m.p.Length < 2 || m.p[1] < 0.5f);

        static float Extra(ZoundModifier m) => m != null && m.type == ZoundModifierType.Envelope ? Mathf.Max(m.Param(0), 0f) : 0f;

        /// <summary>The curve x for a point <paramref name="throughTrim"/> (0..1) of the way through the trimmed region.</summary>
        public static float X(ZoundModifier m, float throughTrim, in Axis axis) => X(m == null ? Trim : m.curveAnchor, Extra(m), throughTrim, axis);

        public static float X(int anchor, float extra, float throughTrim, in Axis axis) {
            if (anchor == Source) {
                float total = axis.sourceLength + extra;
                return total > 0f ? (axis.trimStart + throughTrim * axis.TrimLength) / total : throughTrim;
            }
            float len = axis.TrimLength;
            return len + extra > 0f ? throughTrim * len / (len + extra) : throughTrim;
        }

        /// <summary>The curve x <paramref name="secondsAfterEnd"/> into the extra time after the source (or trim) ends.</summary>
        public static float XAfterEnd(ZoundModifier m, float secondsAfterEnd, in Axis axis) {
            float extra = Extra(m);
            if (m != null && m.curveAnchor == Source) {
                float total = axis.sourceLength + extra;
                return total > 0f ? Mathf.Min(1f, (axis.sourceLength + secondsAfterEnd) / total) : 1f;
            }
            float len = axis.TrimLength;
            return len + extra > 0f ? Mathf.Min(1f, (len + secondsAfterEnd) / (len + extra)) : 1f;
        }

        /// <summary>The curve x at <paramref name="sourceSeconds"/> into the file (Source anchor), or through the trim (Trim).</summary>
        public static float XAtSourceSeconds(ZoundModifier m, float sourceSeconds, in Axis axis) {
            float len = axis.TrimLength;
            return X(m, len > 0f ? (sourceSeconds - axis.trimStart) / len : 0f, axis);
        }

        /// <summary>
        /// Converts a Trim-anchored curve to Source anchoring for <paramref name="axis"/>, so it plays exactly as before:
        /// every point over the audio is rescaled (the shape of a segment does not change under a rescale, so this is exact);
        /// points in the extra time move to "the same seconds after the end". A segment that crossed from the audio into the
        /// extra time is split at that boundary (exact for straight segments). Returns false (nothing done) when the curve is
        /// already Source-anchored, does not follow the waveform, or the axis is unknown.
        /// </summary>
        public static bool ConvertToSource(ZoundModifier m, in Axis axis) {
            if (m == null || m.curve == null || m.curveAnchor == Source || !FollowsWaveform(m) || !axis.Valid) return false;
            float extra = Extra(m);
            float len = axis.TrimLength;
            float r = len + extra > 0f ? len / (len + extra) : 1f;
            float total = axis.sourceLength + extra;
            if (total <= 0f) return false;

            var pts = m.curve.GetPointsList();
            // A segment crossing r (audio into extra time) is split there, at its exact value, so the boundary survives.
            if (extra > 0f && r < 1f) {
                for (int i = 1; i < pts.Count; i++) {
                    if (pts[i - 1].time < r && pts[i].time > r) {
                        float v = m.curve.Evaluate(r);
                        pts.Insert(i, new ZUIEnvelopePoint(r, v, pts[i].exponent));
                        break;
                    }
                }
            }
            float audioScale = len / Mathf.Max(r, 1e-6f) / total;   // x units of the audio part, old -> new
            foreach (var p in pts) {
                if (p.time <= r + 1e-6f) {
                    float through = r > 0f ? p.time / r : 0f;
                    p.time = (axis.trimStart + through * len) / total;
                    p.randomX *= audioScale;
                }
                else {
                    float after = (p.time - r) * (len + extra);
                    p.time = Mathf.Min(1f, (axis.sourceLength + after) / total);
                    p.randomX *= (len + extra) / total;
                }
            }
            // Editors keep a curve's first and last points at the ends of its axis. The rescaled curve begins at the trim
            // start and ends at the trim end, and evaluation already holds its end values beyond them, so flat points at 0
            // and 1 (the same values) change nothing that plays.
            if (pts.Count > 0 && pts[0].time > 0f) pts.Insert(0, new ZUIEnvelopePoint(0f, pts[0].value, 1f));
            if (pts.Count > 0 && pts[pts.Count - 1].time < 1f) pts.Add(new ZUIEnvelopePoint(1f, pts[pts.Count - 1].value, 1f));
            m.curveAnchor = Source;
            return true;
        }
    }
}
