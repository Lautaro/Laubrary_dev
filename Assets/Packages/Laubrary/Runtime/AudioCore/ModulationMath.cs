using Laubrary.Zounds.Dsp;
using Mathf = Laubrary.Audio.AudioMath;

namespace Laubrary.Audio {

    /// <summary>
    /// How far along its own control a parameter's value sits, and back again.
    ///
    /// **This is the whole answer to "a modulator's depth means something different on every parameter".** A filter cutoff
    /// runs from 20 to 20000 and a resonance runs from 0 to 1. Expressing how far a modulator should move them as a raw
    /// amount in the parameter's own units means the same authored number is a barely audible nudge on one and slams the
    /// other into its end stop — and there is no way to guess the right number without knowing an internal range the
    /// interface never shows. So modulation is not expressed in the parameter's units at all. It is expressed as a
    /// FRACTION OF THE CONTROL'S TRAVEL, which means the same thing on every parameter of every effect: a quarter is a
    /// quarter of the slider.
    ///
    /// **Doing it in the control's own curve is what makes it sound right, not just tidy.** A cutoff's slider is
    /// logarithmic, because that is how pitch and frequency are heard: the distance from 100 Hz to 200 Hz sounds like the
    /// distance from 1000 Hz to 2000 Hz, though one is a hundredth of the other in raw hertz. Moving a fixed fraction of a
    /// logarithmic control is therefore moving by a fixed RATIO, so a sweep covers the same musical distance wherever it
    /// starts. A fixed offset in hertz cannot do that — it is enormous at the bottom of the range and inaudible at the top,
    /// which is exactly the complaint that "these numbers make no sense for what they are modifying".
    ///
    /// **And it removes a whole class of nonsense for free.** Because the position is clamped between nought and one
    /// BEFORE it is turned back into a value, a modulator can never ask for something outside the parameter's legal range.
    /// The old arrangement let a modulator produce a wild number and truncated it afterwards, which is why an oscillator
    /// attached the obvious way spent half of every cycle pinned against an end stop rather than sweeping.
    ///
    /// Everything here needs only a minimum, a maximum and which kind of control the parameter gets — all three of which
    /// every parameter of all sixteen effects already declares. There is nothing per-effect to write, now or when the
    /// seventeenth is added.
    /// </summary>
    public static class ModulationMath {

        /// <summary>Below this a logarithmic control cannot be mapped, since the logarithm of nought is undefined.</summary>
        private const float LOG_FLOOR = 1e-4f;

        /// <summary>
        /// Whether a parameter's control is spaced by ratio rather than by amount.
        ///
        /// Decibels are deliberately treated as linear: a decibel scale is ALREADY logarithmic in the underlying quantity,
        /// so its slider is evenly spaced and taking a logarithm of it a second time would bend it the wrong way.
        /// </summary>
        public static bool IsRatioSpaced(ParamCurve curve) => curve == ParamCurve.Logarithmic;

        /// <summary>Where <paramref name="value"/> sits along its control, from nought at the minimum to one at the maximum.</summary>
        public static float ToPosition(float value, float min, float max, bool ratioSpaced) {
            if (ratioSpaced) {
                float lo = Mathf.Log(Mathf.Max(min, LOG_FLOOR));
                float hi = Mathf.Log(Mathf.Max(max, LOG_FLOOR));
                if (hi - lo < 1e-6f) return 0f;
                return Mathf.Clamp01((Mathf.Log(Mathf.Max(value, LOG_FLOOR)) - lo) / (hi - lo));
            }
            if (max - min < 1e-9f) return 0f;
            return Mathf.Clamp01((value - min) / (max - min));
        }

        /// <summary>The value at a given position along the control. The inverse of <see cref="ToPosition"/>.</summary>
        public static float FromPosition(float position, float min, float max, bool ratioSpaced) {
            position = Mathf.Clamp01(position);
            if (ratioSpaced) {
                float lo = Mathf.Log(Mathf.Max(min, LOG_FLOOR));
                float hi = Mathf.Log(Mathf.Max(max, LOG_FLOOR));
                return Mathf.Exp(lo + (hi - lo) * position);
            }
            return min + (max - min) * position;
        }

        /// <summary>
        /// Applies one modulator to one parameter and returns the new value.
        ///
        /// <paramref name="signal"/> is the modulator's output. <paramref name="depth"/> is the fraction of the control's
        /// travel it may move the parameter through, so one means "this modulator can drive the parameter from one end of
        /// its range to the other" on every parameter alike.
        ///
        /// The three ways of combining are deliberately all expressed as movement along the control:
        ///
        /// * SHIFT moves the parameter away from where it was set, by the signal. An oscillator swings both ways around the
        ///   authored value, which is what attaching an oscillator is supposed to do and what multiplying by one never did.
        /// * SET ignores where the parameter was set and hands the whole control to the modulator, with depth blending
        ///   between the two so that a partial amount is still meaningful rather than a jump.
        /// * SCALE is the one genuinely proportional operation — a true tremolo, where doubling means doubling. It is kept
        ///   because on a level it is what is actually wanted, and it is the caller's job not to offer it where it means
        ///   nothing (see <see cref="ScaleIsMeaningful"/>).
        /// </summary>
        public static float Apply(ModulationCombine combine, float baseValue, float signal, float depth,
                                  float min, float max, bool ratioSpaced) {
            switch (combine) {
                case ModulationCombine.SetFromZero:
                case ModulationCombine.Set: {
                    // A modifier that swings both ways (-1..1) spans the control with its whole swing; one that only ever
                    // outputs 0..1 (an envelope) spans it with that. Read as a swing, an envelope's 0 landed on the MIDDLE
                    // of the control, so under Set it could only ever reach the top half (T-0444).
                    float at = combine == ModulationCombine.SetFromZero ? Mathf.Clamp01(signal) : SignalToPosition(signal);
                    float target = FromPosition(at, min, max, ratioSpaced);
                    float p = Mathf.Lerp(ToPosition(baseValue, min, max, ratioSpaced),
                                         ToPosition(target, min, max, ratioSpaced), Mathf.Clamp01(depth));
                    return FromPosition(p, min, max, ratioSpaced);
                }
                case ModulationCombine.Ratio: {
                    // Multiplies the value that was set by a ratio read from the curve on a symmetric, evenly spaced
                    // scale: the middle is x1 (no change), the top x4 and the bottom x1/4 -- two octaves either way on a
                    // pitch -- with depth shrinking the swing towards none. Up and down are the same distance, which a
                    // position across the parameter's own (lopsided) range can never be (T-0479).
                    float factor = RatioFromPosition(0.5f + (Mathf.Clamp01(signal) - 0.5f) * Mathf.Clamp01(depth));
                    return Mathf.Clamp(baseValue * factor, min, max);
                }
                case ModulationCombine.Scale: {
                    // Proportional, in the parameter's own units, because that is the entire point of scaling. Depth fades
                    // between no scaling and the modulator's full effect so the control still behaves at small settings.
                    float factor = 1f + (signal - 1f) * Mathf.Clamp01(depth);
                    return Mathf.Clamp(baseValue * factor, min, max);
                }
                case ModulationCombine.ShiftWholeRange: {
                    // A binding saved before Shift became room-relative: depth is a share of the WHOLE control, so one
                    // swings a full range each way and pins against the ends. Kept exactly, so an old sound does not change
                    // until someone touches that binding.
                    float p = ToPosition(baseValue, min, max, ratioSpaced) + signal * depth;
                    return FromPosition(p, min, max, ratioSpaced);
                }
                case ModulationCombine.ShiftFromCentre: {
                    // Shift for a modifier whose output runs 0..1 with its middle meaning "no change" (a Code modifier):
                    // read as a swing around one half, then exactly the room-relative Shift below.
                    float pos = ToPosition(baseValue, min, max, ratioSpaced);
                    float move = (signal * 2f - 1f) * depth;
                    float room = move >= 0f ? 1f - pos : pos;
                    return FromPosition(pos + move * room, min, max, ratioSpaced);
                }
                default: {
                    // SHIFT, as a share of the ROOM the parameter has in the direction it is being moved (T-0436): one is
                    // "can travel all the way to that end, never past it", nought is "does not move". So a full-strength
                    // swing reaches the ends without ever pinning against them, and one is a setting worth leaving on —
                    // which a share of the whole control never was: from mid-slider it pinned two thirds of the time.
                    float pos = ToPosition(baseValue, min, max, ratioSpaced);
                    float move = signal * depth;
                    float room = move >= 0f ? 1f - pos : pos;
                    return FromPosition(pos + move * room, min, max, ratioSpaced);
                }
            }
        }

        /// <summary>How many octaves the top (and, downwards, the bottom) of a <see cref="ModulationCombine.Ratio"/> curve
        /// reaches: two, so x4 up and x1/4 down -- the pitch control's own top is x4 (T-0479).</summary>
        public const float RatioOctaves = 2f;

        /// <summary>The multiplier at a position (0..1) on a Ratio curve: 0.5 is x1, one is x4, nought x1/4.</summary>
        public static float RatioFromPosition(float position) => Mathf.Pow(2f, (Mathf.Clamp01(position) - 0.5f) * 2f * RatioOctaves);

        /// <summary>The position (0..1) on a Ratio curve that gives <paramref name="ratio"/>; clamped to the curve's reach.</summary>
        public static float PositionFromRatio(float ratio) =>
            Mathf.Clamp01(0.5f + Mathf.Log(Mathf.Max(ratio, 1e-6f), 2f) / (2f * RatioOctaves));

        /// <summary>A modulator's output read as a position, for the case where it takes the control over entirely.</summary>
        private static float SignalToPosition(float signal) => Mathf.Clamp01(signal * 0.5f + 0.5f);

        /// <summary>
        /// Whether multiplying is worth offering for a parameter at all.
        ///
        /// Multiplying a value that rests at nought leaves it at nought however the modulator moves, so the modulator
        /// appears attached, reports a depth and does absolutely nothing. Several effects have parameters that rest there —
        /// a mix or an amount usually starts at none — so this is not a corner case, it is a trap the interface used to set
        /// for people. A parameter that can go negative is equally unsuited, since scaling flips its sign halfway.
        /// </summary>
        public static bool ScaleIsMeaningful(float restValue, float min) => min >= 0f && Mathf.Abs(restValue) > 1e-6f;
    }

}
