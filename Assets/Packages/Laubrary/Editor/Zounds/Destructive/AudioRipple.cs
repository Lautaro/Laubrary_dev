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
            // The curves on the file's seconds follow in stage 3 (AudioRipple.Curve).
            KlipChainEnvelopes.Touch(k);
            ZoundDspPlayback.InvalidateLayout(k);
        }
    }
}
