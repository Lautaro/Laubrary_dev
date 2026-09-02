using System;
using Laubrary.Pyre;
using Laubrary.Shaper;
using UnityEngine;

namespace Laubrary.PyreShaper
{
    /// <summary>
    /// T-0173 — the parts <see cref="FireCompositeSource"/> and <see cref="FireballCompositeSource"/> genuinely
    /// share: the replay gate that keeps scrubbing a stateful sim linear instead of quadratic, the phase→sim-frame
    /// conversion, the default smoke→fire ramp, and gradient hashing.
    ///
    /// <b>Why a shared gate here when Pyre deliberately duplicated its own.</b> <c>PyreRenderer</c> keeps two
    /// separate harnesses and says why (<c>PyreRenderer.cs:744-747</c>): <c>FireSim.Step</c> and
    /// <c>FireballSim.Step</c> have different signatures, so a shared *generic entry* would have bought nothing
    /// there. That reasoning is about the STEP, not about the warm/cold decision — and the warm/cold decision is
    /// the part that is actually subtle (get it wrong and a scrub silently shows a frame built under old dial
    /// values). Taking delegates keeps the differing step out of the shared code and leaves exactly one copy of
    /// the subtle half, so the two sources cannot drift apart in the way that matters.
    /// </summary>
    public static class PyreShaperSimSupport
    {
        /// <summary>A composite node hosts exactly one source — there is no sibling layer whose seeded MinMax
        /// stream this one could collide with — so the salt is a fixed constant, the same reasoning and the same
        /// value <see cref="PyreFormCompositeSource"/> uses.</summary>
        public const int LayerSalt = 0;

        /// <summary>
        /// Which simulation step the requested phase lands on. This is the INVERSE of
        /// <see cref="ShaperClock.PhaseOfFrame"/>, and it is deliberately the same expression the engine already
        /// inverts with when a caller hands it a phase but no frame index
        /// (<c>ShaperDocumentRenderer.cs:186</c>) — rounding, not flooring, so phase 1 reaches the LAST step
        /// rather than falling one short of it. Writing a different rounding here would be the "second frame↔phase
        /// conversion" the clock's own doc forbids.
        /// </summary>
        public static int SimFrameOfPhase(float phase01, int simFrames)
        {
            int n = Mathf.Max(1, simFrames);
            return Mathf.RoundToInt(Mathf.Clamp01(phase01) * Mathf.Max(0, n - 1));
        }

        /// <summary>
        /// Reach <paramref name="target"/> on a stateful sim without replaying the whole history every time.
        ///
        /// Three cases, mirroring the harness Pyre proved (<c>PyreRenderer.cs:419-437</c>). Content UNCHANGED and
        /// the target is one past where the sim already sits ⇒ ONE step (normal playback and a bake, both O(1)
        /// per frame). Content unchanged and the SAME frame is asked again ⇒ no step at all; the grid is already
        /// there, so a repaint costs only the re-render. Anything else — a backward step, a skip, or ANY authored
        /// change — ⇒ a cold replay from a reset. The cold path is what makes this correct rather than merely
        /// fast: a checkpoint would restore state that was built under the OLD dial values, because changing a
        /// dial changes every EARLIER frame too, not just this one.
        /// </summary>
        public static void Replay(ref ShaperCacheKey lastKey, ref int lastFrame,
                                  ShaperCacheKey key, int target, Action reset, Action<int> step)
        {
            bool invalid = lastFrame < 0 || key != lastKey;
            if (!invalid && target == lastFrame + 1)
            {
                step(target);
            }
            else if (invalid || target != lastFrame)
            {
                reset();
                for (int f = 0; f <= target; f++) step(f);
            }
            lastFrame = target;
            lastKey = key;
        }

        /// <summary>
        /// The ignite → hold → fade envelope a flame's own progress dial starts at: the burn is a SHAPE over the
        /// document's phase, not a speed. Same four points as Pyre's own fire-intensity default
        /// (<c>Pyre.cs:1039-1050</c>), so a Shaper flame reads like a Pyre one out of the box.
        /// </summary>
        public static ZUIValue IgniteHoldFade()
        {
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = 1f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, 0f));
            v.points.Add(new ZUIEnvelopePoint(0.18f, 1f));
            v.points.Add(new ZUIEnvelopePoint(0.7f, 1f));
            v.points.Add(new ZUIEnvelopePoint(1f, 0f));
            return v;
        }

        /// <summary>
        /// The smoke→fire ramp a fresh flame starts with, converted from Pyre's own Ember preset through the
        /// bridge that already owns that conversion (<see cref="PyreShaperRampPresets.ToZuiGradient"/>) rather
        /// than by hand-typing a second set of stops. Falls back to a plain black→white ramp if the preset ever
        /// yields nothing, because a null ramp would make the sim render pure white and read as a broken
        /// generator rather than an unconfigured one.
        /// </summary>
        public static Gradient DefaultRamp()
        {
            var converted = PyreShaperRampPresets.ToZuiGradient(PyreRampPresets.Ember());
            if (converted?.gradient != null) return converted.gradient;

            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        /// <summary>
        /// Fold a gradient's stops into a cache key. Needed separately from the whole-object JSON hash the sim
        /// sources take, because <c>JsonUtility</c> gives a <see cref="Gradient"/> no stable textual form — so
        /// without this an edited ramp would leave the key unchanged and the node would keep showing the old
        /// colours until something else happened to change.
        /// </summary>
        public static ShaperCacheMixer MixGradient(ShaperCacheMixer m, Gradient g)
        {
            m.MixBool(g != null);
            if (g == null) return m;

            var ck = g.colorKeys;
            var ak = g.alphaKeys;
            m.MixInt(ck.Length);
            for (int i = 0; i < ck.Length; i++)
            {
                m.MixFloat(ck[i].time);
                m.MixFloat(ck[i].color.r); m.MixFloat(ck[i].color.g); m.MixFloat(ck[i].color.b);
            }
            m.MixInt(ak.Length);
            for (int i = 0; i < ak.Length; i++) { m.MixFloat(ak[i].time); m.MixFloat(ak[i].alpha); }
            m.MixInt((int)g.mode);
            return m;
        }
    }
}
