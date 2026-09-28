using System;
using System.Collections.Generic;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The curves the plays under way are actually hearing (T-0484), for the dotted line drawn over an authored curve: one
    /// per live play of the sound (at most four), each the authored curve as that play draws its random points -- the same
    /// function the engine and the play-length calculation use, with the play's own seed read from its voice. Nothing while
    /// the sound is not playing (T-0446: nothing is shown that no play is doing). Without random points the drawn curve is
    /// the authored one, so the dotted line simply lies on it.
    /// </summary>
    public static class LiveDrawnCurves {

        static readonly uint[] s_seeds = new uint[4];

        /// <summary>The seeds of the sound's live plays (real-time voices still playing), up to four.</summary>
        public static int Seeds(Zound zound) {
            int n = 0;
            if (zound == null || !ZoundEngine.CullingGroups.TryGetValue(zound, out var tokens) || tokens == null) return 0;
            foreach (var token in tokens) {
                if (n >= s_seeds.Length) break;
                if (token == null || token.state == ZoundToken.State.Killed || token.audioSource == null) continue;
                if (token.audioSource.generator is ZoundSapVoiceGenerator g && g.IsPlaying) s_seeds[n++] = g.CurveSeed;
            }
            return n;
        }

        /// <summary>Fills <paramref name="into"/> with one drawn-curve function per live play of <paramref name="zound"/>, for
        /// <paramref name="curve"/> (modifier index <paramref name="mod"/> in the sound's chain); cleared when nothing plays.
        /// Returns how many.</summary>
        public static int Fill(ref List<Func<float, float>> into, Zound zound, Envelope curve, int mod) {
            int n = curve != null && mod >= 0 ? Seeds(zound) : 0;
            if (n == 0) { into?.Clear(); return 0; }
            if (into == null) into = new List<Func<float, float>>();
            into.Clear();
            for (int i = 0; i < n; i++) {
                uint seed = s_seeds[i];
                into.Add(t => EnvelopeRandom.Evaluate(curve, true, seed, mod, t));
            }
            return n;
        }
    }
}
