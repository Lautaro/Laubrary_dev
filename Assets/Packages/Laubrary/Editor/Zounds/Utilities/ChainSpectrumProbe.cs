using System;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds.EditorTools {

    /// <summary>
    /// Measures what a whole effect chain actually does to each part of the frequency range, over time.
    ///
    /// **It measures rather than predicts, and that is the entire point.** The obvious way to show "what is this chain
    /// doing" would be to ask each effect to describe itself and add the descriptions up. That approach is wrong for
    /// this engine in two directions at once: some effects have no such description to give (a waveshaper does not
    /// attenuate frequencies, it invents new ones), and any description that did exist would be a second implementation
    /// of the effect, free to drift from the one you are listening to. So instead this runs real audio through the real
    /// chain, using the same offline renderer that shares its code with playback, and compares what came out against
    /// what went in. Whatever the chain does — including things nobody thought to describe — shows up.
    ///
    /// **The test signal is noise, not an impulse.** An impulse would be the textbook way to get a frequency response
    /// and would be actively misleading here, because several of these effects behave differently depending on how loud
    /// the signal is. A compressor does nothing to a quiet impulse and a great deal to sustained music. Noise at a
    /// realistic level provokes level-dependent behaviour honestly, and gives every band something to work with.
    ///
    /// **Time matters, so this returns a series of measurements rather than one.** A modulated effect is doing something
    /// different from moment to moment — that oscillation is usually the whole reason it is there. One number per band
    /// would average it away into a shrug. Measuring successive windows shows a band moving, which is what somebody
    /// actually wants to see.
    /// </summary>
    public static class ChainSpectrumProbe {

        /// <summary>One band of the display: where it sits, and what the chain did to it over time.</summary>
        public struct Band {
            public float centreHz;
            public float lowHz, highHz;
            /// <summary>Change in decibels per time window. Positive is louder than the dry signal.</summary>
            public float[] overTime;
            public float minDb, maxDb, averageDb;
            /// <summary>False when the test signal had too little energy here to divide by, so no honest reading exists.</summary>
            public bool measurable;
        }

        public struct Measurement {
            public Band[] bands;
            public int windows;
            public float secondsPerWindow;
            /// <summary>True when the chain turned out to do nothing measurable at all.</summary>
            public bool silentOrUnchanged;
            /// <summary>Broadband level change, for effects whose whole job is level rather than tone.</summary>
            public float[] broadbandDbOverTime;
        }

        // Four thousand samples, about 85 ms at 48 kHz. The first version used a quarter of that and the low bands were
        // unusable for it: a 1024-sample window resolves only about 47 Hz, so a band spanning 30 to 39 Hz was NARROWER
        // THAN ONE BIN and what it actually reported was spectral leakage from the loud passband — which depends on the
        // random phases of the test signal and therefore changed every window. That is where the flicker came from, and no
        // amount of smoothing would have fixed it, because the number was never measuring the band in the first place.
        //
        // The cost is time resolution: 85 ms cannot resolve a modulation faster than a few cycles per second. That is an
        // acceptable trade here, since the modulators worth watching move at a few hertz, and the alternative was a low end
        // that lied.
        const int WINDOW = 4096;
        const int SAMPLE_RATE = 48000;

        /// <summary>
        /// Below this, a bin of the dry signal is too quiet to divide by. Chosen well above rounding noise: the whole
        /// reason bars used to swing to the extremes was dividing near-silence by near-silence and believing the answer.
        /// </summary>
        const float BIN_FLOOR = 1e-5f;

        /// <summary>The most negative reading reported. Anything cut below the floor reports exactly this, so that a band
        /// which is simply gone reads as a steady "gone" rather than as a flickering estimate of nothing.</summary>
        const float FLOOR_DB = -80f;

        /// <summary>
        /// Runs noise through <paramref name="chain"/> and reports what it did, band by band, window by window.
        ///
        /// <paramref name="seconds"/> should cover at least one full cycle of the slowest modulator in the chain, or the
        /// movement it produces will be sampled too briefly to see.
        /// </summary>
        public static Measurement Measure(ZoundEffectChain chain, int bandCount = 24, float seconds = 2f,
                                          float level = 0.25f) {
            var result = new Measurement();
            int frames = Math.Max(WINDOW * 2, (int)(seconds * SAMPLE_RATE));

            // The SAME noise for both renders, so the comparison isolates the chain rather than two different signals.
            // Seeded fixed: a measurement that jitters because its own test signal changed would be unreadable.
            // One window's worth of noise, REPEATED, rather than continuous noise.
            //
            // This is what makes the reading stable, and it is worth understanding why. With continuous noise every analysis
            // window sees different samples, so the energy in each bin differs window to window, and in a band that an
            // effect has cut deeply what remains is leakage from whatever is loud nearby — which depends on those samples.
            // The reading then moves by tens of decibels while the effect sits perfectly still. Repeating one period means
            // every window sees the IDENTICAL signal, so the input side of the comparison is exactly the same each time and
            // any variation left in the result is the effect genuinely doing something different.
            //
            // The windows line up with the repeats by construction, since the period is the window length.
            var noise = new float[frames * 2];
            var rng = new System.Random(12345);
            var period = new float[WINDOW];
            for (int i = 0; i < WINDOW; i++) period[i] = (float)(rng.NextDouble() * 2.0 - 1.0) * level;
            for (int i = 0; i < frames; i++) {
                float s = period[i % WINDOW];
                noise[i * 2] = s;
                noise[i * 2 + 1] = s;
            }

            var dry = ZoundDspOffline.Render(noise, 2, SAMPLE_RATE, SAMPLE_RATE, null, 1f, 1f, seconds);
            var wet = ZoundDspOffline.Render(noise, 2, SAMPLE_RATE, SAMPLE_RATE, chain, 1f, 1f, seconds);
            if (dry == null || wet == null) return result;

            int usable = Math.Min(dry.frames, wet.frames);
            int windows = usable / WINDOW;
            if (windows < 1) return result;

            var bands = MakeBands(bandCount);
            for (int b = 0; b < bands.Length; b++) bands[b].overTime = new float[windows];
            result.broadbandDbOverTime = new float[windows];

            var dryMag = new float[WINDOW / 2];
            var wetMag = new float[WINDOW / 2];
            bool anyChange = false;

            for (int w = 0; w < windows; w++) {
                int off = w * WINDOW;
                Spectrum(dry.left, off, dryMag);
                Spectrum(wet.left, off, wetMag);

                double dryTotal = 0, wetTotal = 0;
                for (int k = 0; k < dryMag.Length; k++) { dryTotal += dryMag[k]; wetTotal += wetMag[k]; }
                result.broadbandDbOverTime[w] = ToDb(wetTotal, dryTotal);

                for (int b = 0; b < bands.Length; b++) {
                    int lo = BinOf(bands[b].lowHz), hi = BinOf(bands[b].highHz);

                    // A band narrower than two bins cannot be measured, only guessed at, and the guess is leakage from
                    // whatever is loud nearby. Refused outright rather than reported — a blank is honest where a number
                    // would be fiction.
                    if (hi - lo < 2) {
                        bands[b].overTime[w] = 0f;
                        bands[b].measurable = false;
                        continue;
                    }

                    // One ratio PER BIN, averaged in decibels — not one ratio for the summed band.
                    //
                    // This is the difference between a stable reading and a flickering one, and the first version got it
                    // wrong. Summing the band first makes the answer a noise-WEIGHTED average of the effect's response:
                    // whichever frequencies the random test signal happened to put energy into that window dominate the
                    // result, so the number moves from window to window even when the effect is completely static. Taken
                    // per bin, the ratio is the effect's response at that frequency and the test signal cancels out of it
                    // entirely, so a static effect reads the same every time.
                    //
                    // Bins where the dry signal is too weak are skipped rather than divided by. Dividing near-silence by
                    // near-silence is what produced bars slamming to full scale for bands that simply had nothing in them.
                    double sumDb = 0;
                    int binsUsed = 0;
                    for (int k = lo; k < hi && k < dryMag.Length; k++) {
                        if (dryMag[k] < BIN_FLOOR) continue;

                        // The OUTPUT needs a floor as well as the input, and leaving it out was the rest of the flicker.
                        // Where an effect cuts a band almost completely, what remains is rounding noise, and a ratio
                        // computed from rounding noise is a different random number every window — which showed up as
                        // twenty decibels of phantom movement on a filter that was not moving at all. Below the floor the
                        // honest answer is not a number but "cut past what this can measure", so it reports the floor
                        // itself, identically every time.
                        double binDb;
                        if (wetMag[k] < BIN_FLOOR) binDb = FLOOR_DB;
                        else {
                            double r = wetMag[k] / dryMag[k];
                            binDb = 20.0 * Math.Log10(r);
                            if (binDb < FLOOR_DB) binDb = FLOOR_DB;
                        }
                        sumDb += binDb;
                        binsUsed++;
                    }

                    if (binsUsed == 0) {
                        // Nothing measurable here. Recorded as such so the display can leave the band blank instead of
                        // inventing a value for it.
                        bands[b].overTime[w] = 0f;
                        bands[b].measurable = false;
                        continue;
                    }
                    bands[b].measurable = true;

                    float db = (float)(sumDb / binsUsed);
                    if (db > 40f) db = 40f;
                    if (db < FLOOR_DB) db = FLOOR_DB;
                    bands[b].overTime[w] = db;
                    if (Math.Abs(db) > 0.05f) anyChange = true;
                }
            }

            for (int b = 0; b < bands.Length; b++) {
                float min = float.MaxValue, max = float.MinValue, sum = 0f;
                for (int w = 0; w < windows; w++) {
                    float v = bands[b].overTime[w];
                    if (v < min) min = v;
                    if (v > max) max = v;
                    sum += v;
                }
                bands[b].minDb = min;
                bands[b].maxDb = max;
                bands[b].averageDb = sum / windows;
            }

            result.bands = bands;
            result.windows = windows;
            result.secondsPerWindow = (float)WINDOW / SAMPLE_RATE;
            result.silentOrUnchanged = !anyChange;
            return result;
        }

        /// <summary>
        /// Bands spaced so that each covers a similar musical interval rather than a similar number of hertz, because
        /// hearing works that way: the octave from 100 to 200 Hz matters as much as the one from 5 to 10 kHz, even
        /// though the second is a hundred times wider.
        /// </summary>
        static Band[] MakeBands(int count) {
                // Starting at 60 rather than 30 Hz, because below that a band would be narrower than this window can resolve
            // even at 4096 samples, and there is no point creating bands that have to be discarded.
            const float low = 60f, high = 18000f;
            var bands = new Band[count];
            double ratio = Math.Pow(high / low, 1.0 / count);
            double edge = low;
            for (int i = 0; i < count; i++) {
                double next = edge * ratio;
                bands[i].lowHz = (float)edge;
                bands[i].highHz = (float)next;
                bands[i].centreHz = (float)Math.Sqrt(edge * next);
                edge = next;
            }
            return bands;
        }

        static int BinOf(float hz) {
            int bin = (int)Math.Round(hz * WINDOW / (double)SAMPLE_RATE);
            if (bin < 0) bin = 0;
            if (bin > WINDOW / 2 - 1) bin = WINDOW / 2 - 1;
            return bin;
        }

        /// <summary>
        /// Decibel difference between two energies, with a floor.
        ///
        /// The floor matters: where the dry signal has almost nothing, the ratio becomes meaningless and would otherwise
        /// produce a wild number from rounding noise — a band reading plus forty decibels because both sides were
        /// essentially zero. Clamping keeps the display honest about the difference between "boosted a lot" and
        /// "there was nothing here to begin with".
        /// </summary>
        static float ToDb(double wet, double dry) {
            const double floor = 1e-7;
            if (dry < floor && wet < floor) return 0f;
            if (dry < floor) return 40f;
            double r = wet / dry;
            if (r < 1e-6) return -120f;
            float db = (float)(20.0 * Math.Log10(r));
            if (db > 40f) db = 40f;
            if (db < -120f) db = -120f;
            return db;
        }

        /// <summary>
        /// Magnitude spectrum of one window, via a plain in-place transform. Windowed first, because chopping a signal
        /// at an arbitrary point implies a discontinuity that the transform would otherwise report as broadband content
        /// that is not there.
        /// </summary>
        static void Spectrum(float[] samples, int offset, float[] magOut) {
            int n = WINDOW;
            var re = new float[n];
            var im = new float[n];
            for (int i = 0; i < n; i++) {
                int s = offset + i;
                float v = s < samples.Length ? samples[s] : 0f;
                float win = 0.5f - 0.5f * (float)Math.Cos(2.0 * Math.PI * i / (n - 1));   // Hann
                re[i] = v * win;
                im[i] = 0f;
            }
            Fft(re, im);
            for (int k = 0; k < magOut.Length; k++) {
                magOut[k] = (float)Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
            }
        }

        /// <summary>Iterative radix-2 transform. Length must be a power of two, which WINDOW is.</summary>
        static void Fft(float[] re, float[] im) {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++) {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }
            for (int len = 2; len <= n; len <<= 1) {
                double ang = -2.0 * Math.PI / len;
                float wRe = (float)Math.Cos(ang), wIm = (float)Math.Sin(ang);
                for (int i = 0; i < n; i += len) {
                    float curRe = 1f, curIm = 0f;
                    for (int k = 0; k < len / 2; k++) {
                        int a = i + k, b = i + k + len / 2;
                        float tRe = re[b] * curRe - im[b] * curIm;
                        float tIm = re[b] * curIm + im[b] * curRe;
                        re[b] = re[a] - tRe; im[b] = im[a] - tIm;
                        re[a] += tRe;        im[a] += tIm;
                        float nRe = curRe * wRe - curIm * wIm;
                        curIm = curRe * wIm + curIm * wRe;
                        curRe = nRe;
                    }
                }
            }
        }
    }
}
