using UnityEngine;

namespace Laubrary.Zounds.Dsp {

    public enum ParamCurve { Linear = 0, Logarithmic = 1, Decibel = 2, Integer = 3, Toggle = 4 }

    public struct ParamDesc {
        public string name;
        public string unit;
        public float min, max, def;
        public ParamCurve curve;
        public bool automatable;
        public ModifierOp defaultOp;
        /// <summary>For an Integer parameter that names a few fixed choices: the option labels (index = value).</summary>
        public string[] options;

        public ParamDesc(string name, string unit, float min, float max, float def, ParamCurve curve = ParamCurve.Linear,
                         bool automatable = true, ModifierOp defaultOp = ModifierOp.Add, string[] options = null) {
            this.name = name; this.unit = unit; this.min = min; this.max = max; this.def = def;
            this.curve = curve; this.automatable = automatable; this.defaultOp = defaultOp; this.options = options;
        }

        public bool IsChoice => options != null && options.Length > 0;
    }

    public delegate int StateFloatsFn(float[] p, int sampleRate);
    public delegate float TailFn(float[] p);

    public sealed class EffectDesc {
        public ZoundEffectType type;
        public string displayName;
        public string summary;
        public ParamDesc[] parameters;
        public bool isStateful;
        public bool heavy;                  // wants the heavy arena tier
        public StateFloatsFn stateFloats;   // per-voice state size in floats
        public TailFn tailSeconds;          // declared decay after the source stops
        public TailFn tailMultiplier;       // how much this node prolongs a tail arriving from upstream
    }

    public sealed class ModifierDesc {
        public ZoundModifierType type;
        public string displayName;
        public string summary;
        public ParamDesc[] parameters;
        public int stateFloats;
    }

    /// <summary>
    /// The single source of truth for every effect and modifier: names, parameter ranges, defaults,
    /// per-voice state size and tail. The editor draws generic parameter rows from it, the layout
    /// builder sizes arenas from it, and Audio End budgets tails from it. Adding an effect is one
    /// entry here plus one static Process method.
    /// </summary>
    public static class ZoundEffectDescriptors {

        public const float MAX_DELAY_MS = 2000f;

        private static readonly EffectDesc[] effects;
        private static readonly ModifierDesc[] modifiers;

        public static ParamDesc[] SourceStageParams = {
            new ParamDesc("Pitch", "x", 0.1f, 4f, 1f, ParamCurve.Logarithmic, true, ModifierOp.Multiply),
            new ParamDesc("Source gain", "x", 0f, 4f, 1f, ParamCurve.Linear, true, ModifierOp.Multiply),
        };

        static ZoundEffectDescriptors() {
            effects = new EffectDesc[16];
            Def(ZoundEffectType.Gain, "Gain", "Level. Position matters: before a distortion it drives it, after it only scales.",
                false, false, (p, sr) => 0, p => 0f, p => 1f,
                new ParamDesc("Gain", "x", 0f, 4f, 1f, ParamCurve.Linear, true, ModifierOp.Multiply));

            Def(ZoundEffectType.Limiter, "Limiter", "Fast zero-latency peak limiter (no lookahead, so timing never drifts).",
                true, false, (p, sr) => 4, p => 0f, p => 1f,
                new ParamDesc("Ceiling", "dB", -40f, 0f, -1f, ParamCurve.Decibel),
                new ParamDesc("Release", "ms", 1f, 1000f, 50f, ParamCurve.Logarithmic));

            Def(ZoundEffectType.Compressor, "Compressor", "Dynamics compressor with makeup gain.",
                true, false, (p, sr) => 4, p => 0f, p => 1f,
                new ParamDesc("Threshold", "dB", -60f, 0f, -10f, ParamCurve.Decibel),
                new ParamDesc("Ratio", ":1", 1f, 20f, 4f, ParamCurve.Logarithmic),
                new ParamDesc("Attack", "ms", 0.1f, 200f, 10f, ParamCurve.Logarithmic),
                new ParamDesc("Release", "ms", 1f, 2000f, 100f, ParamCurve.Logarithmic),
                new ParamDesc("Makeup", "dB", -12f, 24f, 0f, ParamCurve.Decibel));

            Def(ZoundEffectType.Delay, "Delay", "Stereo delay line with feedback. Max time sizes the buffer.",
                true, true, (p, sr) => 2 * DelayRingFrames(p, sr) + 8,
                p => DelayTail(p), p => 1f + DelayTail(p) * 0.5f,
                new ParamDesc("Time", "ms", 1f, MAX_DELAY_MS, 250f, ParamCurve.Logarithmic),
                new ParamDesc("Feedback", "", 0f, 0.98f, 0.4f),
                new ParamDesc("Mix", "", 0f, 1f, 0.3f),
                new ParamDesc("Max time", "ms", 10f, MAX_DELAY_MS, 500f, ParamCurve.Logarithmic, false),
                new ParamDesc("Ping-pong", "", 0f, 1f, 0f, ParamCurve.Toggle, false));

            Def(ZoundEffectType.Reverb, "Reverb", "Freeverb-style stereo reverb.",
                true, true, (p, sr) => ReverbStateFloats(sr),
                p => ReverbTail(p), p => 1f + ReverbTail(p) * 0.5f,
                new ParamDesc("Room size", "", 0f, 1f, 0.5f),
                new ParamDesc("Damping", "", 0f, 1f, 0.5f),
                new ParamDesc("Width", "", 0f, 1f, 1f),
                new ParamDesc("Mix", "", 0f, 1f, 0.3f));

            Def(ZoundEffectType.LowPass, "Low pass", "12 dB/oct resonant low-pass filter.",
                true, false, (p, sr) => 16, p => 0f, p => 1f,
                new ParamDesc("Cutoff", "Hz", 20f, 20000f, 20000f, ParamCurve.Logarithmic),
                new ParamDesc("Resonance", "Q", 0.1f, 10f, 0.707f, ParamCurve.Logarithmic));

            Def(ZoundEffectType.HighPass, "High pass", "12 dB/oct resonant high-pass filter.",
                true, false, (p, sr) => 16, p => 0f, p => 1f,
                new ParamDesc("Cutoff", "Hz", 20f, 20000f, 20f, ParamCurve.Logarithmic),
                new ParamDesc("Resonance", "Q", 0.1f, 10f, 0.707f, ParamCurve.Logarithmic));

            Def(ZoundEffectType.Flanger, "Flanger", "Short modulated delay with feedback.",
                true, false, (p, sr) => 2 * ModDelayFrames(12f, sr) + 8, p => 0.012f, p => 1f,
                new ParamDesc("Rate", "Hz", 0.01f, 10f, 0.5f, ParamCurve.Logarithmic),
                new ParamDesc("Depth", "ms", 0.1f, 10f, 2f),
                new ParamDesc("Feedback", "", -0.95f, 0.95f, 0.5f),
                new ParamDesc("Mix", "", 0f, 1f, 0.5f));

            Def(ZoundEffectType.Chorus, "Chorus", "Two to four detuned copies from modulated delays.",
                true, false, (p, sr) => 2 * ModDelayFrames(40f, sr) + 16, p => 0.04f, p => 1f,
                new ParamDesc("Rate", "Hz", 0.01f, 5f, 0.8f, ParamCurve.Logarithmic),
                new ParamDesc("Depth", "ms", 1f, 30f, 8f),
                new ParamDesc("Voices", "", 1f, 4f, 2f, ParamCurve.Integer, false),
                new ParamDesc("Mix", "", 0f, 1f, 0.5f));

            Def(ZoundEffectType.Phaser, "Phaser", "All-pass cascade swept by an LFO.",
                true, false, (p, sr) => 64, p => 0f, p => 1f,
                new ParamDesc("Rate", "Hz", 0.01f, 10f, 0.5f, ParamCurve.Logarithmic),
                new ParamDesc("Depth", "", 0f, 1f, 0.7f),
                new ParamDesc("Stages", "", 2f, 12f, 4f, ParamCurve.Integer, false),
                new ParamDesc("Feedback", "", -0.9f, 0.9f, 0.3f),
                new ParamDesc("Mix", "", 0f, 1f, 0.5f));

            Def(ZoundEffectType.BitCrush, "Bit crush", "Bit-depth and sample-rate reduction.",
                true, false, (p, sr) => 8, p => 0f, p => 1f,
                new ParamDesc("Bits", "", 1f, 16f, 8f, ParamCurve.Linear),
                new ParamDesc("Downsample", "x", 1f, 64f, 1f, ParamCurve.Logarithmic),
                new ParamDesc("Mix", "", 0f, 1f, 1f));

            Def(ZoundEffectType.Distortion, "Distortion", "Soft-clip waveshaper with tone control.",
                true, false, (p, sr) => 8, p => 0f, p => 1f,
                new ParamDesc("Drive", "", 1f, 100f, 10f, ParamCurve.Logarithmic),
                new ParamDesc("Tone", "", 0f, 1f, 0.5f),
                new ParamDesc("Mix", "", 0f, 1f, 1f));

            Def(ZoundEffectType.EQ, "EQ", "Seven peaking bands (60 Hz to 12 kHz) plus low and high cut.",
                true, false, (p, sr) => 9 * 2 * 8, p => 0f, p => 1f,
                new ParamDesc("Sub 60", "dB", -24f, 24f, 0f, ParamCurve.Decibel),
                new ParamDesc("Low 150", "dB", -24f, 24f, 0f, ParamCurve.Decibel),
                new ParamDesc("Low-mid 400", "dB", -24f, 24f, 0f, ParamCurve.Decibel),
                new ParamDesc("Mid 1k", "dB", -24f, 24f, 0f, ParamCurve.Decibel),
                new ParamDesc("High-mid 2.5k", "dB", -24f, 24f, 0f, ParamCurve.Decibel),
                new ParamDesc("High 6k", "dB", -24f, 24f, 0f, ParamCurve.Decibel),
                new ParamDesc("Air 12k", "dB", -24f, 24f, 0f, ParamCurve.Decibel),
                new ParamDesc("Low cut", "Hz", 10f, 20000f, 10f, ParamCurve.Logarithmic),
                new ParamDesc("High cut", "Hz", 20f, 22000f, 22000f, ParamCurve.Logarithmic));

            Def(ZoundEffectType.Normalize, "Normalize", "Scales the source so its peak lands on the target level (peak read from the sample data).",
                false, false, (p, sr) => 0, p => 0f, p => 1f,
                new ParamDesc("Target", "dB", -30f, 0f, -0.5f, ParamCurve.Decibel, false));

            Def(ZoundEffectType.Fade, "Fade", "Fade in from the start and fade out into the end of the source.",
                false, false, (p, sr) => 0, p => 0f, p => 1f,
                new ParamDesc("Fade in", "s", 0f, 10f, 0f, ParamCurve.Linear, false),
                new ParamDesc("Fade out", "s", 0f, 10f, 0f, ParamCurve.Linear, false),
                new ParamDesc("S-curve", "", 0f, 1f, 0f, ParamCurve.Toggle, false));

            Def(ZoundEffectType.TransientShaper, "Transient shaper", "Boosts or cuts the hit and the body separately (broadband, level independent).",
                true, false, (p, sr) => 4, p => 0f, p => 1f,
                new ParamDesc("Attack", "dB", -24f, 24f, 0f, ParamCurve.Decibel),
                new ParamDesc("Sustain", "dB", -24f, 24f, 0f, ParamCurve.Decibel),
                new ParamDesc("Speed", "ms", 2f, 100f, 20f, ParamCurve.Logarithmic),
                new ParamDesc("Release", "ms", 10f, 1000f, 100f, ParamCurve.Logarithmic));

            modifiers = new ModifierDesc[4];
            // Time base: 0 = source position (the curve follows the waveform it is drawn over, as the old
            // baked envelopes did), 1 = play time (elapsed over the resolved duration; what a Zequence uses).
            ModDef(ZoundModifierType.Envelope, "Envelope", "A curve over the play length (plus extra time past the end).", 4,
                new ParamDesc("Extra time", "s", 0f, 30f, 0f, ParamCurve.Linear, false),
                new ParamDesc("Time base", "", 0f, 1f, 0f, ParamCurve.Integer, false, ModifierOp.Add, new[] { "Waveform", "Play time" }));
            ModDef(ZoundModifierType.Lfo, "LFO", "Oscillates, or glides between random targets.", 8,
                new ParamDesc("Amount", "", -4f, 4f, 1f, ParamCurve.Linear, false),
                new ParamDesc("Rate", "Hz", 0f, 50f, 1f, ParamCurve.Logarithmic, false),
                new ParamDesc("Shape", "", 0f, 3f, 0f, ParamCurve.Integer, false, ModifierOp.Add, new[] { "Sine", "Triangle", "Saw", "Square" }),
                new ParamDesc("Reset phase", "", 0f, 1f, 1f, ParamCurve.Toggle, false),
                new ParamDesc("Mode", "", 0f, 1f, 0f, ParamCurve.Integer, false, ModifierOp.Add, new[] { "Oscillate", "Random" }),
                new ParamDesc("New target every", "s", 0.01f, 10f, 0.5f, ParamCurve.Logarithmic, false),
                new ParamDesc("Offset", "", -4f, 4f, 0f, ParamCurve.Linear, false));
            ModDef(ZoundModifierType.Random, "Random", "One value per play, held for the whole play.", 1,
                new ParamDesc("Min", "", -4f, 4f, 0.9f, ParamCurve.Linear, false),
                new ParamDesc("Max", "", -4f, 4f, 1.1f, ParamCurve.Linear, false),
                new ParamDesc("Bias", "", 0.1f, 10f, 1f, ParamCurve.Logarithmic, false));
            ModDef(ZoundModifierType.Step, "Step", "Steps through a list of values, per play or on a timer.", 4,
                new ParamDesc("Timing", "", 0f, 1f, 0f, ParamCurve.Integer, false, ModifierOp.Add, new[] { "Per play", "Per interval" }),
                new ParamDesc("Interval", "ms", 1f, 10000f, 250f, ParamCurve.Logarithmic, false),
                new ParamDesc("Order", "", 0f, 1f, 0f, ParamCurve.Integer, false, ModifierOp.Add, new[] { "Sequential", "Round robin" }),
                new ParamDesc("Start random", "", 0f, 1f, 0f, ParamCurve.Toggle, false),
                new ParamDesc("Reset on trigger", "", 0f, 1f, 0f, ParamCurve.Toggle, false));
        }

        private static void Def(ZoundEffectType type, string name, string summary, bool stateful, bool heavy,
                                StateFloatsFn stateFloats, TailFn tail, TailFn tailMul, params ParamDesc[] ps) {
            effects[(int)type] = new EffectDesc {
                type = type, displayName = name, summary = summary, parameters = ps, isStateful = stateful, heavy = heavy,
                stateFloats = stateFloats, tailSeconds = tail, tailMultiplier = tailMul
            };
        }

        private static void ModDef(ZoundModifierType type, string name, string summary, int stateFloats, params ParamDesc[] ps) {
            modifiers[(int)type] = new ModifierDesc { type = type, displayName = name, summary = summary, parameters = ps, stateFloats = stateFloats };
        }

        public static EffectDesc Get(ZoundEffectType type) => effects[(int)type];
        public static ModifierDesc GetModifier(ZoundModifierType type) => modifiers[(int)type];
        public static int EffectTypeCount => effects.Length;
        public static int ModifierTypeCount => modifiers.Length;

        public static float[] DefaultParams(ZoundEffectType type) {
            var ps = Get(type).parameters;
            var r = new float[ps.Length];
            for (int i = 0; i < ps.Length; i++) r[i] = ps[i].def;
            return r;
        }

        public static float[] DefaultModifierParams(ZoundModifierType type) {
            var ps = GetModifier(type).parameters;
            var r = new float[ps.Length];
            for (int i = 0; i < ps.Length; i++) r[i] = ps[i].def;
            return r;
        }

        /// <summary>Descriptor of a parameter addressed by (node type, index); nodeIndex -1 addresses the source stage.</summary>
        public static ParamDesc ParamOf(ZoundEffectChain chain, int nodeIndex, int paramIndex) {
            if (nodeIndex < 0) return SourceStageParams[paramIndex];
            return Get(chain.nodes[nodeIndex].type).parameters[paramIndex];
        }

        // ── sizing helpers ──

        public static int DelayRingFrames(float[] p, int sr) {
            float maxMs = p != null && p.Length > 3 ? p[3] : 500f;
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
            float repeats = Mathf.Log(ZoundDspConstants.SILENCE_LINEAR) / Mathf.Log(Mathf.Clamp(fb, 0.0001f, 0.999f));
            return Mathf.Min(time * repeats + time, ZoundDspConstants.MAX_TAIL_SEC);
        }

        // Freeverb comb lengths at 44.1 kHz, scaled to the device rate at layout time.
        public static readonly int[] ReverbCombTuning = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
        public static readonly int[] ReverbAllpassTuning = { 556, 441, 341, 225 };
        public const int ReverbStereoSpread = 23;

        public static int ReverbStateFloats(int sr) {
            float scale = sr / 44100f;
            int total = 0;
            for (int i = 0; i < ReverbCombTuning.Length; i++) total += 2 * (Mathf.CeilToInt(ReverbCombTuning[i] * scale) + ReverbStereoSpread + 2);
            for (int i = 0; i < ReverbAllpassTuning.Length; i++) total += 2 * (Mathf.CeilToInt(ReverbAllpassTuning[i] * scale) + ReverbStereoSpread + 2);
            return total + 64 + 3 * ZoundDspConstants.CONTROL_BLOCK; // header (cursors, filter states, lengths) + block scratch
        }

        /// <summary>Reverb RT60 from the longest comb: len/sr × ln(0.001)/ln(feedback), f = room × 0.28 + 0.7.</summary>
        public static float ReverbTail(float[] p) {
            float room = p != null && p.Length > 0 ? p[0] : 0.5f;
            float mix = p != null && p.Length > 3 ? p[3] : 0.3f;
            if (mix <= 0.0001f) return 0f;
            float f = Mathf.Clamp(room * 0.28f + 0.7f, 0.01f, 0.995f);
            float seconds = (ReverbCombTuning[ReverbCombTuning.Length - 1] / 44100f) * Mathf.Log(0.001f) / Mathf.Log(f);
            return Mathf.Min(seconds, ZoundDspConstants.MAX_TAIL_SEC);
        }

        /// <summary>Walks the chain in order accumulating the declared tail (§11.3 of the design).</summary>
        public static float TailBudgetSeconds(ZoundEffectChain chain) {
            float budget = 0f;
            if (chain == null) return 0f;
            for (int i = 0; i < chain.modifiers.Count; i++) {
                var m = chain.modifiers[i];
                if (m.enabled && m.type == ZoundModifierType.Envelope) budget = Mathf.Max(budget, m.Param(0));
            }
            for (int i = 0; i < chain.nodes.Count; i++) {
                var n = chain.nodes[i];
                if (!n.enabled) continue;
                var d = Get(n.type);
                budget = budget * d.tailMultiplier(n.p) + d.tailSeconds(n.p);
            }
            return Mathf.Min(budget, ZoundDspConstants.MAX_TAIL_SEC);
        }
    }

}
