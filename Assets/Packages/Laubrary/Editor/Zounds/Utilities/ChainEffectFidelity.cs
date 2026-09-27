namespace Laubrary.Zounds.EditorTools {

    /// <summary>How faithfully a per-band loudness reading represents what an effect actually does.</summary>
    public enum EffectFidelity {
        /// <summary>The effect IS a frequency-shaping operation. A per-band number is not an approximation of it — it is
        /// what the effect is.</summary>
        Exact,
        /// <summary>Frequency-shaping, but the shape moves. Shown correctly only because the display is time-resolved;
        /// a single averaged number would hide the entire point of the effect.</summary>
        Moving,
        /// <summary>Its gain depends on how loud the signal is, so the reading describes what it did to THIS test
        /// signal, not a fixed property of the effect. Your own audio will get different numbers.</summary>
        LevelDependent,
        /// <summary>Energy really does move, but it arrives LATE rather than being shaped. The bars do show something
        /// real and useful — repeats landing, a tail building and decaying — it just is not a frequency curve, and its
        /// average over time says little.</summary>
        TimeSmeared,
        /// <summary>A per-band loudness number does not describe this effect. Reading one is worse than reading
        /// nothing, because it looks like an answer.</summary>
        Misrepresented,
    }

    /// <summary>
    /// Says, per effect, whether a combined per-band view can honestly represent it.
    ///
    /// **Why this exists at all.** It is easy to build a display that shows a number for every effect and looks
    /// authoritative. For roughly a third of these effects such a number would be a fabrication, and a confident
    /// fabrication is worse than a blank: somebody would tune a chain against it and wonder why their ears disagreed.
    /// So the display is expected to consult this and say plainly which of its contributors it is actually describing.
    ///
    /// The distinction is not about which effects are complicated. It is about whether "this band got louder or quieter"
    /// is the right SHAPE of answer:
    ///
    /// - A filter's whole nature is that it changes some frequencies and not others, so the answer fits perfectly.
    /// - A compressor's gain depends on the signal, so the answer fits but only for the signal it was measured with.
    /// - A waveshaper does not change the level of a band; it manufactures new content that was not there. Energy does
    ///   appear in bands that were quiet, and calling that "boost" invites exactly the wrong mental model.
    /// - A delay and a reverb were expected to show nothing, and measurement proved that WRONG, which is why the
    ///   expectation was tested instead of asserted. Their fine comb structure does average away, so their long-run
    ///   average per band is close to meaningless — but the time-resolved view shows plenty: a delay moved a band by
    ///   nearly 28 dB across a measurement and a reverb by nearly 48. That is the repeats arriving and the tail
    ///   decaying, which is real and worth seeing. So they get their own verdict: trust the movement, ignore the
    ///   average.
    /// </summary>
    public static class ChainEffectFidelity {

        public static EffectFidelity Of(ZoundEffectType type) {
            switch (type) {
                case ZoundEffectType.Gain:
                case ZoundEffectType.Normalize:
                case ZoundEffectType.Fade:
                case ZoundEffectType.EQ:
                case ZoundEffectType.LowPass:
                case ZoundEffectType.HighPass:
                    return EffectFidelity.Exact;

                case ZoundEffectType.Flanger:
                case ZoundEffectType.Chorus:
                case ZoundEffectType.Phaser:
                    return EffectFidelity.Moving;

                case ZoundEffectType.Compressor:
                case ZoundEffectType.Limiter:
                case ZoundEffectType.TransientShaper:
                    return EffectFidelity.LevelDependent;

                case ZoundEffectType.Delay:
                case ZoundEffectType.Reverb:
                    return EffectFidelity.TimeSmeared;

                case ZoundEffectType.Distortion:
                case ZoundEffectType.BitCrush:
                    return EffectFidelity.Misrepresented;

                default:
                    // An effect added later is assumed unrepresented until somebody decides otherwise. Guessing
                    // "exact" for an unknown effect is how a display starts quietly lying.
                    return EffectFidelity.Misrepresented;
            }
        }

        /// <summary>A short phrase for the display, explaining the verdict in the reader's terms rather than in jargon.</summary>
        public static string Explain(ZoundEffectType type) {
            switch (Of(type)) {
                case EffectFidelity.Exact:
                    return "shown exactly — changing the balance between frequencies is all this effect does";
                case EffectFidelity.Moving:
                    return "shown, and it moves — watch the bars rather than their average";
                case EffectFidelity.LevelDependent:
                    return "shown for this test signal only — its gain depends on how loud the input is, so your own audio will differ";
                case EffectFidelity.TimeSmeared:
                    return "the movement is real, the average is not — you are watching energy arrive late, not a frequency curve";
                default:
                    switch (type) {
                        case ZoundEffectType.Distortion:
                        case ZoundEffectType.BitCrush:
                            return "apparent boost is MANUFACTURED — it invents harmonics rather than raising a band, so reading these bars as 'louder here' is the wrong mental model";
                        default:
                            return "NOT shown — nobody has established that a per-band number describes this effect";
                    }
            }
        }
    }
}
