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

        const int WINDOW = 1024;               // about 21 ms at 48 kHz: short enough to catch a modulator moving
        const int SAMPLE_RATE = 48000;

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
            var noise = new float[frames * 2];
            var rng = new System.Random(12345);
            for (int i = 0; i < frames; i++) {
                float s = (float)(rng.NextDouble() * 2.0 - 1.0) * level;
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
                    if (hi <= lo) hi = lo + 1;
                    double d = 0, t = 0;
                    for (int k = lo; k < hi && k < dryMag.Length; k++) { d += dryMag[k]; t += wetMag[k]; }
                    float db = ToDb(t, d);
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
            const float low = 30f, high = 18000f;
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
