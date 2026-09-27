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

        /// <summary>
        /// What each option actually does, for hovering. A one-word label on a button says what it is CALLED, not what it
        /// does to the sound, and the difference matters most exactly where the words are jargon — telling somebody a
        /// waveform is a "saw" explains nothing about why they would pick it.
        /// </summary>
        public string[] optionTips;

        /// <summary>
        /// What this parameter does to the sound, for hovering.
        ///
        /// The hover text used to be the parameter's own name read back with the units appended, which told a reader who
        /// did not already know exactly nothing. A name is a handle for something you understand; it is not an
        /// explanation. Where this is left empty the hover falls back to that old behaviour, so filling one in is always
        /// an improvement and never a regression.
        /// </summary>
        public string desc;

        public ParamDesc(string name, string unit, float min, float max, float def, ParamCurve curve = ParamCurve.Linear,
                         bool automatable = true, ModifierOp defaultOp = ModifierOp.Add, string[] options = null,
                         string[] optionTips = null, string desc = null) {
            this.name = name; this.unit = unit; this.min = min; this.max = max; this.def = def;
            this.curve = curve; this.automatable = automatable; this.defaultOp = defaultOp;
            this.options = options; this.optionTips = optionTips; this.desc = desc;
        }

        /// <summary>The hover text for one option, falling back to its label when nobody has written one.</summary>
        public string OptionTip(int index) {
            if (optionTips != null && index >= 0 && index < optionTips.Length && !string.IsNullOrEmpty(optionTips[index]))
                return optionTips[index];
            return options != null && index >= 0 && index < options.Length ? options[index] : "";
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
                new ParamDesc("Gain", "x", 0f, 4f, 1f, ParamCurve.Linear, true, ModifierOp.Multiply, null, null,
                    "How much louder or quieter, as a multiplier. Where it sits in the chain matters: ahead of a distortion it decides how hard that distortion is pushed, after one it only changes the level."));

            Def(ZoundEffectType.Limiter, "Limiter", "Fast zero-latency peak limiter (no lookahead, so timing never drifts).",
                true, false, (p, sr) => 4, p => 0f, p => 1f,
                new ParamDesc("Ceiling", "dB", -40f, 0f, -1f, ParamCurve.Decibel, true, ModifierOp.Add, null, null,
                    "The level nothing is allowed to exceed. Peaks above it are pushed down; everything below it passes untouched."),
                new ParamDesc("Release", "ms", 1f, 1000f, 50f, ParamCurve.Logarithmic, true, ModifierOp.Add, null, null,
                    "How quickly it stops holding the level down after a peak has passed. Short sounds lively but can pump audibly; long sounds smoother but ducks the sound for longer after each hit."));

            Def(ZoundEffectType.Compressor, "Compressor", "Dynamics compressor with makeup gain.",
                true, false, (p, sr) => 4, p => 0f, p => 1f,
                new ParamDesc("Threshold", "dB", -60f, 0f, -10f, ParamCurve.Decibel, true, ModifierOp.Add, null, null,
                    "The level above which the sound starts being held back. Nothing quieter than this is touched at all."),
                new ParamDesc("Ratio", ":1", 1f, 20f, 4f, ParamCurve.Logarithmic, true, ModifierOp.Add, null, null,
                    "How hard it holds back what is above the threshold. Four to one means four decibels over becomes one; very high settings stop it getting louder at all."),
                new ParamDesc("Attack", "ms", 0.1f, 200f, 10f, ParamCurve.Logarithmic, true, ModifierOp.Add, null, null,
                    "How fast it clamps down once the sound gets loud. Fast catches the initial hit and softens it; slow lets the hit through and only controls what follows, which keeps a sound punchy."),
                new ParamDesc("Release", "ms", 1f, 2000f, 100f, ParamCurve.Logarithmic, true, ModifierOp.Add, null, null,
                    "How fast it lets go again once the sound drops. Too short on a sustained sound audibly breathes."),
                new ParamDesc("Makeup", "dB", -12f, 24f, 0f, ParamCurve.Decibel, true, ModifierOp.Add, null, null,
                    "A level change applied afterwards, to bring the sound back up to where it was before the compression pulled it down."));

            Def(ZoundEffectType.Delay, "Delay", "Stereo delay line with feedback. Max time sizes the buffer.",
                true, true, (p, sr) => 2 * DelayRingFrames(p, sr) + 8,
                p => DelayTail(p), p => 1f + DelayTail(p) * 0.5f,
                new ParamDesc("Time", "ms", 1f, MAX_DELAY_MS, 250f, ParamCurve.Logarithmic, true, ModifierOp.Add, null, null,
                    "The gap before each echo. Below about thirty it stops being heard as an echo and starts colouring the tone instead."),
                new ParamDesc("Feedback", "", 0f, 0.98f, 0.4f, ParamCurve.Linear, true, ModifierOp.Add, null, null,
                    "How much of each echo is fed back in to make the next one. Zero gives a single repeat; near the top the repeats take a very long time to die away."),
                new ParamDesc("Mix", "", 0f, 1f, 0.3f, ParamCurve.Linear, true, ModifierOp.Add, null, null,
                    "How much of the echoing is heard against the original. Nought is dry, one is echo only."),
                new ParamDesc("Max time", "ms", 10f, MAX_DELAY_MS, 500f, ParamCurve.Logarithmic, false, ModifierOp.Add, null, null,
                    "The longest gap this delay can ever be set to. It reserves memory, so it cannot be changed while the sound plays — set it above the longest Time you intend to use and leave it."),
                new ParamDesc("Ping-pong", "", 0f, 1f, 0f, ParamCurve.Toggle, false, ModifierOp.Add, null, null,
                    "Bounces each successive echo between left and right instead of keeping it in place."));

            Def(ZoundEffectType.Reverb, "Reverb", "Freeverb-style stereo reverb.",
                true, true, (p, sr) => ReverbStateFloats(sr),
                p => ReverbTail(p), p => 1f + ReverbTail(p) * 0.5f,
                new ParamDesc("Room size", "", 0f, 1f, 0.5f, ParamCurve.Linear, true, ModifierOp.Add, null, null,
                    "How big the imagined space is, which is mostly how long the tail takes to fade. Small reads as a room, large as a hall."),
                new ParamDesc("Damping", "", 0f, 1f, 0.5f, ParamCurve.Linear, true, ModifierOp.Add, null, null,
                    "How quickly the high frequencies disappear from the tail. High settings sound like soft furnishings and curtains; low settings sound like tile and glass."),
                new ParamDesc("Width", "", 0f, 1f, 1f, ParamCurve.Linear, true, ModifierOp.Add, null, null,
                    "How far the tail spreads across the stereo field. Nought collapses it to the centre."),
                new ParamDesc("Mix", "", 0f, 1f, 0.3f, ParamCurve.Linear, true, ModifierOp.Add, null, null,
                    "How much of the space is heard against the original. Nought is dry, one is tail only."));

            Def(ZoundEffectType.LowPass, "Low pass", "12 dB/oct resonant low-pass filter.",
                true, false, (p, sr) => 16, p => 0f, p => 1f,
                new ParamDesc("Cutoff", "Hz", 20f, 20000f, 20000f, ParamCurve.Logarithmic, true, ModifierOp.Add, null, null,
                    "Everything above this is progressively removed, so lowering it makes the sound duller and more distant. At the top it does nothing at all."),
                new ParamDesc("Resonance", "Q", 0.1f, 10f, 0.707f, ParamCurve.Logarithmic, true, ModifierOp.Add, null, null,
                    "Emphasises the frequencies right at the cutoff, giving the filter a vocal, whistling quality. High settings make a sweep sing; the default is the neutral setting that adds no emphasis."));

            Def(ZoundEffectType.HighPass, "High pass", "12 dB/oct resonant high-pass filter.",
                true, false, (p, sr) => 16, p => 0f, p => 1f,
                new ParamDesc("Cutoff", "Hz", 20f, 20000f, 20f, ParamCurve.Logarithmic, true, ModifierOp.Add, null, null,
                    "Everything below this is progressively removed, so raising it thins the sound out and takes the weight away. At the bottom it does nothing at all."),
                new ParamDesc("Resonance", "Q", 0.1f, 10f, 0.707f, ParamCurve.Logarithmic, true, ModifierOp.Add, null, null,
                    "Emphasises the frequencies right at the cutoff. High settings make a sweep sing; the default is the neutral setting that adds no emphasis."));

            Def(ZoundEffectType.Flanger, "Flanger", "Short modulated delay with feedback.",
                true, false, (p, sr) => 2 * ModDelayFrames(12f, sr) + 8, p => 0.012f, p => 1f,
                new ParamDesc("Rate", "Hz", 0.01f, 10f, 0.5f, ParamCurve.Logarithmic, true, ModifierOp.Add, null, null,
                    "How fast the sweep goes back and forth. Slow gives a long jet-plane whoosh; fast becomes a warble."),
                new ParamDesc("Depth", "ms", 0.1f, 10f, 2f, ParamCurve.Linear, true, ModifierOp.Add, null, null,
                    "How far the sweep travels. Larger covers more of the spectrum and sounds more dramatic."),
                new ParamDesc("Feedback", "", -0.95f, 0.95f, 0.5f, ParamCurve.Linear, true, ModifierOp.Add, null, null,
                    "Feeds the effect back into itself, sharpening the swept peaks into a much more metallic, ringing sound. Negative values invert it, which shifts where the peaks sit."),
                new ParamDesc("Mix", "", 0f, 1f, 0.5f, ParamCurve.Linear, true, ModifierOp.Add, null, null,
                    "How much of the effect is heard against the original. A flanger needs both to work at all, so the strongest sound is near the middle, not at one."));

            Def(ZoundEffectType.Chorus, "Chorus", "Two to four detuned copies from modulated delays.",
                true, false, (p, sr) => 2 * ModDelayFrames(40f, sr) + 16, p => 0.04f, p => 1f,
                new ParamDesc("Rate", "Hz", 0.01f, 5f, 0.8f, ParamCurve.Logarithmic, true, ModifierOp.Add, null, null,
                    "How fast the copies drift in and out of tune with the original. Slow and gentle sounds natural; fast sounds seasick."),
                new ParamDesc("Depth", "ms", 1f, 30f, 8f, ParamCurve.Linear, true, ModifierOp.Add, null, null,
                    "How far out of tune the copies are allowed to drift. Small is a subtle thickening; large is an obvious wobble."),
                new ParamDesc("Voices", "", 1f, 4f, 2f, ParamCurve.Integer, false, ModifierOp.Add, null, null,
                    "How many detuned copies are added. More sounds like a bigger group playing together, at more processing cost."),
                new ParamDesc("Mix", "", 0f, 1f, 0.5f, ParamCurve.Linear, true, ModifierOp.Add, null, null,
                    "How much of the copies is heard against the original."));

            Def(ZoundEffectType.Phaser, "Phaser", "All-pass cascade swept by an LFO.",
                true, false, (p, sr) => 64, p => 0f, p => 1f,
                new ParamDesc("Rate", "Hz", 0.01f, 10f, 0.5f, ParamCurve.Logarithmic, true, ModifierOp.Add, null, null,
                    "How fast the notches sweep up and down the spectrum."),
                new ParamDesc("Depth", "", 0f, 1f, 0.7f, ParamCurve.Linear, true, ModifierOp.Add, null, null,
                    "How far the notches travel. Larger sweeps across more of the sound."),
                new ParamDesc("Stages", "", 2f, 12f, 4f, ParamCurve.Integer, false, ModifierOp.Add, null, null,
                    "How many notches there are. Few sounds gentle and watery; many sounds thick and obviously electronic."),
                new ParamDesc("Feedback", "", -0.9f, 0.9f, 0.3f, ParamCurve.Linear, true, ModifierOp.Add, null, null,
                    "Feeds the effect back into itself, making the notches sharper and more pronounced. Negative values shift where they sit."),
                new ParamDesc("Mix", "", 0f, 1f, 0.5f, ParamCurve.Linear, true, ModifierOp.Add, null, null,
                    "How much of the effect is heard against the original. Like a flanger, it needs both, so the strongest sound is near the middle."));

            Def(ZoundEffectType.BitCrush, "Bit crush", "Bit-depth and sample-rate reduction.",
                true, false, (p, sr) => 8, p => 0f, p => 1f,
                new ParamDesc("Bits", "", 1f, 16f, 8f, ParamCurve.Linear, true, ModifierOp.Add, null, null,
                    "How finely the level is measured. Fewer steps means the quiet parts turn grainy and gritty first, the way very old game hardware sounded."),
                new ParamDesc("Downsample", "x", 1f, 64f, 1f, ParamCurve.Logarithmic, true, ModifierOp.Add, null, null,
                    "Throws away samples, holding each one for longer. This dulls the top end and folds it back as a harsh metallic ring, quite different from the grit that fewer bits gives."),
                new ParamDesc("Mix", "", 0f, 1f, 1f, ParamCurve.Linear, true, ModifierOp.Add, null, null,
                    "How much of the degraded version is heard against the original."));

            Def(ZoundEffectType.Distortion, "Distortion", "Soft-clip waveshaper with tone control.",
                true, false, (p, sr) => 8, p => 0f, p => 1f,
                new ParamDesc("Drive", "", 1f, 100f, 10f, ParamCurve.Logarithmic, true, ModifierOp.Add, null, null,
                    "How hard the sound is pushed into the shaping. Low adds warmth and thickness; high flattens it into something aggressive and buzzing."),
                new ParamDesc("Tone", "", 0f, 1f, 0.5f, ParamCurve.Linear, true, ModifierOp.Add, null, null,
                    "Tilts the balance of what comes out, from dark and thick at nought to bright and biting at one."),
                new ParamDesc("Mix", "", 0f, 1f, 1f, ParamCurve.Linear, true, ModifierOp.Add, null, null,
                    "How much of the distorted version is heard against the original. Blending some clean sound back in keeps the attack readable."));

            Def(ZoundEffectType.EQ, "EQ", "Seven peaking bands (60 Hz to 12 kHz) plus low and high cut.",
                true, false, (p, sr) => 9 * 2 * 8, p => 0f, p => 1f,
                new ParamDesc("Sub 60", "dB", -24f, 24f, 0f, ParamCurve.Decibel, true, ModifierOp.Add, null, null,
                    "The deepest weight, felt more than heard. Boosting adds rumble and power; cutting tightens a sound that booms."),
                new ParamDesc("Low 150", "dB", -24f, 24f, 0f, ParamCurve.Decibel, true, ModifierOp.Add, null, null,
                    "Body and fullness. Too much here is what makes a sound muddy."),
                new ParamDesc("Low-mid 400", "dB", -24f, 24f, 0f, ParamCurve.Decibel, true, ModifierOp.Add, null, null,
                    "Warmth, and also where boxiness lives. Cutting a little here often clears a sound up more than boosting anything else."),
                new ParamDesc("Mid 1k", "dB", -24f, 24f, 0f, ParamCurve.Decibel, true, ModifierOp.Add, null, null,
                    "The core of most sounds, and the range the ear is most sensitive to. Changes here are the most obvious of any band."),
                new ParamDesc("High-mid 2.5k", "dB", -24f, 24f, 0f, ParamCurve.Decibel, true, ModifierOp.Add, null, null,
                    "Presence and attack, which is what makes a sound cut through a mix. Boosting too far gets harsh and tiring quickly."),
                new ParamDesc("High 6k", "dB", -24f, 24f, 0f, ParamCurve.Decibel, true, ModifierOp.Add, null, null,
                    "Detail, edge and sibilance. Boosting adds definition; cutting softens something spiky."),
                new ParamDesc("Air 12k", "dB", -24f, 24f, 0f, ParamCurve.Decibel, true, ModifierOp.Add, null, null,
                    "The open, airy top. A boost here adds sparkle without making a sound louder in any obvious way."),
                new ParamDesc("Low cut", "Hz", 10f, 20000f, 10f, ParamCurve.Logarithmic, true, ModifierOp.Add, null, null,
                    "Removes everything below this outright, rather than merely turning it down. Useful for clearing out rumble the sound never needed."),
                new ParamDesc("High cut", "Hz", 20f, 22000f, 22000f, ParamCurve.Logarithmic, true, ModifierOp.Add, null, null,
                    "Removes everything above this outright. Bringing it down pushes a sound into the background, or makes it feel like it is heard through a wall."));

            Def(ZoundEffectType.Normalize, "Normalize", "Scales the source so its peak lands on the target level (peak read from the sample data).",
                false, false, (p, sr) => 0, p => 0f, p => 1f,
                new ParamDesc("Target", "dB", -30f, 0f, -0.5f, ParamCurve.Decibel, false, ModifierOp.Add, null, null,
                    "The level the loudest moment of the source is brought to. It is worked out from the sample data, so it evens out sounds recorded at different levels without touching their dynamics."));

            Def(ZoundEffectType.Fade, "Fade", "Fade in from the start and fade out into the end of the source.",
                false, false, (p, sr) => 0, p => 0f, p => 1f,
                new ParamDesc("Fade in", "s", 0f, 10f, 0f, ParamCurve.Linear, false, ModifierOp.Add, null, null,
                    "How long the sound takes to come up from silence at its start. A few thousandths of a second is enough to stop a click on a sound that begins abruptly."),
                new ParamDesc("Fade out", "s", 0f, 10f, 0f, ParamCurve.Linear, false, ModifierOp.Add, null, null,
                    "How long the sound takes to fall to silence before its end, measured back from the end of the source."),
                new ParamDesc("S-curve", "", 0f, 1f, 0f, ParamCurve.Toggle, false, ModifierOp.Add, null, null,
                    "Eases the fade in and out of its start and end instead of ramping at a constant rate. Smoother on long fades, and almost indistinguishable on very short ones."));

            Def(ZoundEffectType.TransientShaper, "Transient shaper", "Boosts or cuts the hit and the body separately (broadband, level independent).",
                true, false, (p, sr) => 4, p => 0f, p => 1f,
                new ParamDesc("Attack", "dB", -24f, 24f, 0f, ParamCurve.Decibel, true, ModifierOp.Add, null, null,
                    "Boosts or cuts the initial hit of the sound without touching what follows. Boosting makes it snappier and more percussive; cutting softens the impact."),
                new ParamDesc("Sustain", "dB", -24f, 24f, 0f, ParamCurve.Decibel, true, ModifierOp.Add, null, null,
                    "Boosts or cuts the body and tail after the hit. Boosting makes a sound feel longer and roomier; cutting makes it tight and dry."),
                new ParamDesc("Speed", "ms", 2f, 100f, 20f, ParamCurve.Logarithmic, true, ModifierOp.Add, null, null,
                    "Where it draws the line between the hit and the body. Short counts only the very first moment as the hit; long counts more of the sound as attack."),
                new ParamDesc("Release", "ms", 10f, 1000f, 100f, ParamCurve.Logarithmic, true, ModifierOp.Add, null, null,
                    "How quickly it returns to normal after shaping a hit, which sets how much of the following sound is still affected."));

            modifiers = new ModifierDesc[4];
            // Time base: 0 = source position (the curve follows the waveform it is drawn over, as the old
            // baked envelopes did), 1 = play time (elapsed over the resolved duration; what a Zequence uses).
            ModDef(ZoundModifierType.Envelope, "Envelope", "A curve over the play length (plus extra time past the end).", 4,
                new ParamDesc("Extra time", "s", 0f, 30f, 0f, ParamCurve.Linear, false, ModifierOp.Add, null, null,
                    "Stretches the curve past the end of the source audio, so it can keep working while a delay or reverb tail rings out. Zero means the curve ends when the audio does."),
                new ParamDesc("Time base", "", 0f, 1f, 0f, ParamCurve.Integer, false, ModifierOp.Add,
                    new[] { "Waveform", "Play time" },
                    new[] {
                        "Waveform: the curve follows the position of the read head through the source audio, so it stays aligned with the sound even if the pitch changes.",
                        "Play time: the curve follows the clock instead, so it takes the same real time whatever the pitch is doing."
                    },
                    "Whether the curve is measured against the audio being read or against elapsed time. They differ as soon as pitch is not one."));
            ModDef(ZoundModifierType.Lfo, "LFO", "Oscillates, or glides between random targets.", 8,
                new ParamDesc("Amount", "", -4f, 4f, 1f, ParamCurve.Linear, false, ModifierOp.Add, null, null,
                    "How big a swing this oscillator produces before the binding's own depth scales it. One is a full swing; a negative value turns the wave upside down."),
                new ParamDesc("Rate", "Hz", 0f, 50f, 1f, ParamCurve.Logarithmic, false, ModifierOp.Add, null, null,
                    "How many times a second it goes round. A few per second reads as a wobble; above about twenty it stops being heard as movement and starts colouring the tone itself."),
                new ParamDesc("Shape", "", 0f, 3f, 0f, ParamCurve.Integer, false, ModifierOp.Add,
                    new[] { "Sine", "Triangle", "Saw", "Square" },
                    new[] {
                        "Sine: a smooth swing with no corners — the natural choice for vibrato or a gentle sweep.",
                        "Triangle: rises and falls at a constant rate, turning sharply at each end. Slightly more insistent than a sine.",
                        "Saw: climbs steadily, then drops instantly back. Good for a repeated fall or rise that restarts.",
                        "Square: jumps between the two extremes with nothing in between — a hard alternation, not a sweep."
                    }),
                new ParamDesc("Reset phase", "", 0f, 1f, 1f, ParamCurve.Toggle, false, ModifierOp.Add, null, null,
                    "On: every play starts at the same point in the wave, so repeated plays sound identical. Off: the wave runs continuously in the background and each play catches it wherever it happens to be, which makes repeats differ from each other."),
                new ParamDesc("Mode", "", 0f, 1f, 0f, ParamCurve.Integer, false, ModifierOp.Add,
                    new[] { "Oscillate", "Random" },
                    new[] {
                        "Oscillate: repeats the chosen shape at the chosen rate, forever and predictably.",
                        "Random: ignores the shape and glides to a new random value every so often, set by 'New target every'."
                    }),
                new ParamDesc("New target every", "s", 0.01f, 10f, 0.5f, ParamCurve.Logarithmic, false, ModifierOp.Add, null, null,
                    "In Random mode only: how often it picks a new value to glide towards. It always glides rather than jumping, so a short setting sounds restless and a long one sounds like slow drift."),
                new ParamDesc("Offset", "", -4f, 4f, 0f, ParamCurve.Linear, false, ModifierOp.Add, null, null,
                    "Shifts the whole wave up or down, so it no longer swings evenly about the middle. Use it to make an oscillator push mostly one way."));
            ModDef(ZoundModifierType.Random, "Random", "One value per play, held for the whole play.", 1,
                new ParamDesc("Min", "", -4f, 4f, 0.9f, ParamCurve.Linear, false, ModifierOp.Add, null, null,
                    "The lowest value this can pick. One value is drawn when the sound starts and held for the whole play."),
                new ParamDesc("Max", "", -4f, 4f, 1.1f, ParamCurve.Linear, false, ModifierOp.Add, null, null,
                    "The highest value this can pick. Keeping Min and Max close gives subtle variation between plays; spreading them wide makes every play noticeably different."),
                new ParamDesc("Bias", "", 0.1f, 10f, 1f, ParamCurve.Logarithmic, false, ModifierOp.Add, null, null,
                    "Which end of the range the draw favours. One is even; below one leans towards the minimum, above one towards the maximum."));
            ModDef(ZoundModifierType.Step, "Step", "Steps through a list of values, per play or on a timer.", 4,
                new ParamDesc("Timing", "", 0f, 1f, 0f, ParamCurve.Integer, false, ModifierOp.Add,
                    new[] { "Per play", "Per interval" },
                    new[] {
                        "Per play: advances one step each time the sound is played, and holds that value for the whole play.",
                        "Per interval: advances on a timer while the sound plays, so a single play can step through several values."
                    },
                    "When the list moves on to its next value."),
                new ParamDesc("Interval", "ms", 1f, 10000f, 250f, ParamCurve.Logarithmic, false, ModifierOp.Add, null, null,
                    "In Per interval mode only: how long each value is held before moving to the next."),
                new ParamDesc("Order", "", 0f, 1f, 0f, ParamCurve.Integer, false, ModifierOp.Add,
                    new[] { "Sequential", "Round robin" },
                    new[] {
                        "Sequential: walks the list top to bottom, then starts again — fully predictable.",
                        "Round robin: shuffles, but plays every value once before any repeats, so it sounds random without ever landing on the same one twice running."
                    },
                    "How it moves through the list."),
                new ParamDesc("Start random", "", 0f, 1f, 0f, ParamCurve.Toggle, false, ModifierOp.Add, null, null,
                    "Begins somewhere in the middle of the list rather than always at the top, so a scene that starts fresh does not always open on the same value."),
                new ParamDesc("Reset on trigger", "", 0f, 1f, 0f, ParamCurve.Toggle, false, ModifierOp.Add, null, null,
                    "Returns to the start of the list every time the sound is played, instead of carrying on from where it left off."));
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
            float repeats = Mathf.Log(ZoundDspConstants.SILENCE_LINEAR) / Mathf.Log(Mathf.Clamp(fb, 0.0001f, 0.999f));
            return Mathf.Min(time * repeats + time, ZoundDspConstants.MAX_TAIL_SEC);
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

            return total + historicalSlack + 64 + 3 * ZoundDspConstants.CONTROL_BLOCK; // header (cursors, filter states, lengths) + block scratch
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
