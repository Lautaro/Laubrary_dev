using Unity.Collections;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;
using Mathf = Laubrary.Audio.AudioMath;

namespace Laubrary.Audio {
    public static class AudioEffectSizing {
        public const float MAX_DELAY_MS = 2000f;
        /// <summary>State budgets, including the historical padding retained by Zounds. Call during preparation.</summary>
        public static int EffectStateFloats(ZoundEffectType type, float[] parameters, int sampleRate) {
            switch (type) {
                case ZoundEffectType.Delay: return 2 * DelayRingFrames(parameters, sampleRate) + 8;
                case ZoundEffectType.Reverb: return ReverbStateFloats(sampleRate);
                case ZoundEffectType.Flanger: return 2 * ModDelayFrames(12f, sampleRate) + 8;
                case ZoundEffectType.Chorus: return 2 * ModDelayFrames(40f, sampleRate) + 16;
                case ZoundEffectType.LowPass:
                case ZoundEffectType.HighPass: return 16;
                case ZoundEffectType.Phaser: return 64;
                case ZoundEffectType.BitCrush:
                case ZoundEffectType.Distortion: return 8;
                case ZoundEffectType.EQ: return 9 * 2 * 8;
                case ZoundEffectType.Limiter:
                case ZoundEffectType.Compressor:
                case ZoundEffectType.TransientShaper: return 4;
                default: return 0;
            }
        }

        public static int ModifierStateFloats(ZoundModifierType type) {
            switch (type) {
                case ZoundModifierType.Envelope: return 4;
                case ZoundModifierType.Lfo: return 10;
                case ZoundModifierType.Random: return 1;
                case ZoundModifierType.Step: return 8;
                default: return 0;
            }
        }
        public static int DelayRingFrames(float[] p, int sr) {
            float maxMs = p != null && p.Length > 3 ? p[3] : 500f;
            return DelayRingFramesFromMaxMs(maxMs, sr);
        }

        /// <summary>
        /// The delay's ring length, from its longest permitted delay time. **This is the single definition.**
        ///
        /// It exists separately because the same number decides two different things — how much memory the
        /// delay is given, and which positions inside that memory it reads and writes — and those two are
        /// computed at different times, from different places. Writing the arithmetic out twice is how a delay
        /// ends up reading past the end of its own buffer, which is a memory fault rather than a wrong sound.
        /// The same trap was already found and removed in the reverb; this is the last effect that had it.
        /// </summary>
        public static int DelayRingFramesFromMaxMs(float maxMs, int sr) {
            maxMs = Mathf.Clamp(maxMs, 10f, MAX_DELAY_MS);
            return Mathf.CeilToInt(maxMs * 0.001f * sr) + 4;
        }

        public static int ModDelayFrames(float maxMs, int sr) => Mathf.CeilToInt(maxMs * 0.001f * sr) + 4;

        /// <summary>Delay tail: time × ln(threshold)/ln(feedback), clamped to MAX_TAIL_SEC.</summary>
        public static float DelayTail(float[] p) {
            float time = (p != null && p.Length > 0 ? p[0] : 250f) * 0.001f;
            float fb = p != null && p.Length > 1 ? p[1] : 0.4f;
            float mix = p != null && p.Length > 2 ? p[2] : 0.3f;
            if (mix <= 0.0001f) return 0f;
            if (fb <= 0.0001f) return time;
            float repeats = Mathf.Log(AudioControl.SilenceLinear) / Mathf.Log(Mathf.Clamp(fb, 0.0001f, 0.999f));
            return Mathf.Min(time * repeats + time, AudioControl.MaxTailSeconds);
        }

        // Freeverb comb lengths at 44.1 kHz, scaled to the device rate at layout time.
        public static readonly int[] ReverbCombTuning = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
        public static readonly int[] ReverbAllpassTuning = { 556, 441, 341, 225 };
        public const int ReverbStereoSpread = 23;

        /// <summary>
        /// Sizes the arena from the exact same per-channel lengths the render side actually allocates
        /// (<see cref="ReverbEffect.CombLen"/> / <see cref="ReverbEffect.AllpassLen"/>), instead of a separate
        /// copy of the tuning-table math. The two used to diverge: this loop added the stereo-spread offset
        /// for both channels of every comb/allpass, while the render side (see ZoundEffects.cs) only adds it
        /// for channel 1 — safe only because this over-allocated, and fragile because a future change to one
        /// without the other could silently under-allocate. Now there is exactly one formula, so the arena is
        /// always exactly as large as what gets used, never smaller.
        /// </summary>
        public static int ReverbStateFloats(int sr) {
            // One formula, shared with the render and with the layout's precompute, so the size of the
            // arena can no longer disagree with the indexes taken into it. Previously this function
            // computed the same lengths a second time, in a different shape, which is the kind of
            // duplication that turns into a buffer overrun the moment somebody edits one copy.
            int total = 0;
            for (int i = 0; i < ReverbCombTuning.Length; i++) for (int ch = 0; ch < 2; ch++) total += ReverbEffect.CombLen(i, ch, sr);
            for (int i = 0; i < ReverbAllpassTuning.Length; i++) for (int ch = 0; ch < 2; ch++) total += ReverbEffect.AllpassLen(i, ch, sr);

            // HISTORICAL SLACK, RETAINED DELIBERATELY AND MEASURED.
            // The old duplicate computation added the stereo-spread offset for BOTH channels (the render
            // applies it to one) plus two floats per slot, which made every reverb arena exactly 324
            // floats larger than the render consumes. Unifying the formula above would drop that slack --
            // and shrinking an allocation is a different change from making this code compilable, so it
            // does not belong in the same step. Keeping it also keeps a borderline chain on the same
            // arena tier it used before, rather than letting a size reduction quietly move it.
            // Tightening this is a worthwhile follow-up, verified on its own.
            int slots = ReverbCombTuning.Length + ReverbAllpassTuning.Length;
            int historicalSlack = slots * (ReverbStereoSpread + 4);

            return total + historicalSlack + 64 + 3 * AudioControl.BlockFrames; // header (cursors, filter states, lengths) + block scratch
        }

        /// <summary>Reverb RT60 from the longest comb: len/sr × ln(0.001)/ln(feedback), f = room × 0.28 + 0.7.</summary>
        public static float ReverbTail(float[] p) {
            float room = p != null && p.Length > 0 ? p[0] : 0.5f;
            float mix = p != null && p.Length > 3 ? p[3] : 0.3f;
            if (mix <= 0.0001f) return 0f;
            float f = Mathf.Clamp(room * 0.28f + 0.7f, 0.01f, 0.995f);
            float seconds = (ReverbCombTuning[ReverbCombTuning.Length - 1] / 44100f) * Mathf.Log(0.001f) / Mathf.Log(f);
            return Mathf.Min(seconds, AudioControl.MaxTailSeconds);
        }

    }
}
