using System;
using System.Collections.Generic;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds.EditorTools {

    /// <summary>
    /// Measures what a whole effect chain actually does to each part of the frequency range, over the length of one play,
    /// and what each modulated parameter does over that same play.
    ///
    /// **It measures rather than predicts, and that is the entire point.** The obvious way to show "what is this chain
    /// doing" would be to ask each effect to describe itself and add the descriptions up. That approach is wrong for
    /// this engine in two directions at once: some effects have no such description to give (a waveshaper does not
    /// attenuate frequencies, it invents new ones), and any description that did exist would be a second implementation
    /// of the effect, free to drift from the one you are listening to. So instead this runs real audio through the real
    /// chain, using the same offline renderer that shares its code with playback, and compares what came out against
    /// what went in. Whatever the chain does — including things nobody thought to describe — shows up.
    ///
    /// **The test signal is noise, not an impulse.** Several effects behave differently depending on how loud the signal
    /// is; a compressor does nothing to a quiet impulse and a great deal to sustained music. Noise at a realistic level
    /// provokes level-dependent behaviour honestly and gives every band something to work with.
    ///
    /// **Time is measured along the sound's own play, not a fixed test length.** Anything that changes over a play — an
    /// oscillator, an envelope, an oscillator's strength curve — is laid out by the engine against the length of the play
    /// it is part of. A first version measured a fixed second and a half regardless of the sound, which stretched a
    /// strength curve over the wrong length and, with a slow oscillator, showed a small slice of one cycle looping over
    /// and over: a sine that looked like a ramp. Measuring over the real play makes the picture match what is heard.
    /// </summary>
    public static class ChainSpectrumProbe {

        /// <summary>One band of the display: where it sits, and what the chain did to it over time.</summary>
        public struct Band {
            public float centreHz;
            public float lowHz, highHz;
            /// <summary>Change in decibels per reading. Positive is louder than the dry signal.</summary>
            public float[] overTime;
            public float minDb, maxDb, averageDb;
            /// <summary>False when the test signal had too little energy here to divide by, so no honest reading exists.</summary>
            public bool measurable;
        }

        /// <summary>
        /// One modulated parameter, followed across the play: where the engine actually put it, block by block, as a
        /// position along that parameter's own slider (0 = left end, 1 = right end), next to where it was set.
        /// </summary>
        public struct Lane {
            public int modifierIndex, nodeIndex, paramIndex;
            public string label;
            public float[] position01;
            public float authored01;
        }

        public struct Measurement {
            public Band[] bands;
            public int windows;
            /// <summary>The moment within the play each reading describes: the middle of the stretch it was taken over.</summary>
            public float[] readingSeconds;
            /// <summary>How long the measured play is. Readings and lanes both run along this.</summary>
            public float playSeconds;
            /// <summary>True when the play was longer than is worth measuring and only its start was measured.</summary>
            public bool truncated;
            /// <summary>True when the chain turned out to do nothing measurable at all.</summary>
            public bool silentOrUnchanged;
            /// <summary>Broadband level change, for effects whose whole job is level rather than tone.</summary>
            public float[] broadbandDbOverTime;
            public Lane[] lanes;
            /// <summary>Each modifier's own output across the play, block by block, before it is applied to anything —
            /// indexed like the chain's modifier list. Lets a modifier's own editor draw what it produces.</summary>
            public float[][] modifierOutput;
            /// <summary>Time between successive lane values: the engine's own control step.</summary>
            public float laneStepSeconds;
            /// <summary>Wall-clock cost of taking this measurement, so a slow one can be noticed rather than suspected.</summary>
            public double costMs;
        }

        // Sixteen thousand samples, about a third of a second at 48 kHz. Long, because the lowest bands are only a few
        // hertz wide and the window has to be long to tell apart frequencies that close. The price is time resolution: one
        // reading describes a third of a second, so a modulation faster than a few cycles per second is averaged away in
        // the per-band view. That is the honest limit of a frequency reading; the per-parameter lanes, which are read
        // straight from the engine every block, are what show fast movement.
        const int WINDOW = 16384;
        const int SAMPLE_RATE = 48000;

        /// <summary>How much time one per-band reading describes. Anything that changes several times within this is
        /// averaged in the bars; the lanes, read from the engine at its own control step, are what show such changes.</summary>
        public const float ReadingSeconds = (float)WINDOW / SAMPLE_RATE;

        /// <summary>
        /// How many times a second the fastest modifier in the chain changes what it outputs: twice per cycle for an
        /// oscillator (up, then down), once per new target for a random oscillator, once per step for a timed step list.
        /// An envelope moves once over the whole play and a per-play value not at all, so neither counts.
        /// Only modifiers that are on and drive something are considered.
        /// </summary>
        public static float FastestChangesPerSecond(ZoundEffectChain chain) {
            float fastest = 0f;
            if (chain?.modifiers == null) return 0f;
            for (int m = 0; m < chain.modifiers.Count; m++) {
                var mod = chain.modifiers[m];
                if (!mod.enabled || mod.p == null) continue;
                bool drives = false;
                foreach (var b in chain.bindings) if (b.modifierIndex == m) drives = true;
                if (!drives) continue;
                float perSecond = 0f;
                if (mod.type == ZoundModifierType.Lfo && mod.p.Length > 4)
                    perSecond = (int)mod.p[4] == (int)LfoMode.Random
                        ? (mod.p.Length > 5 && mod.p[5] > 0f ? 1f / mod.p[5] : 0f)
                        : 2f * mod.p[1];
                else if (mod.type == ZoundModifierType.Step && mod.p.Length > 1 && (int)mod.p[0] == (int)StepTiming.PerInterval && mod.p[1] > 0f)
                    perSecond = 1000f / mod.p[1];
                if (perSecond > fastest) fastest = perSecond;
            }
            return fastest;
        }

        /// <summary>
        /// The length of the pattern the test signal repeats, and also how far the analysis window moves between readings.
        ///
        /// **These two being the same number is what keeps the reading steady, and getting it wrong cost a regression.**
        /// Before the frequencies are read, the samples are faded up and down across the window to stop its own edges
        /// registering as a click. Fading weights the start and end of the window differently from its middle, so material
        /// that has slid along sits under a different part of the fade and comes out slightly different — and where an
        /// effect has cut a band almost to nothing, a slight change in a tiny number is a large change in the ratio. It was
        /// caught by measurement: a waveshaper, which cannot vary over time at all, read as moving by five and a half
        /// decibels. Moving the window by exactly one whole repeat puts identical samples under the fade every time.
        /// </summary>
        const int PERIOD = 8192;
        const int HOP = PERIOD;

        /// <summary>The repeating test signal has energy only at whole multiples of this, and nowhere in between.</summary>
        const double LINE_HZ = (double)SAMPLE_RATE / PERIOD;

        /// <summary>
        /// The longest play measured. A longer sound has its first stretch measured and says so; the modulators worth
        /// watching repeat well within this, and measuring minutes of audio on every edit would make editing crawl.
        /// </summary>
        public const float MaxSeconds = 12f;

        /// <summary>Below this, a bin of the dry signal is too quiet to divide by.</summary>
        const float BIN_FLOOR = 1e-5f;

        /// <summary>The most negative reading reported. A band cut past this reads as a steady "gone".</summary>
        const float FLOOR_DB = -80f;

        /// <summary>The range the display covers: the range of human hearing.</summary>
        public const float LowHz = 20f, HighHz = 20000f;

        /// <summary>
        /// Runs noise through <paramref name="chain"/> for one play of <paramref name="playSeconds"/> and reports what it
        /// did, band by band and reading by reading, plus what every modulated parameter did across the same play.
        /// </summary>
        public static Measurement Measure(ZoundEffectChain chain, float playSeconds, int bandCount = 72, float level = 0.25f) {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = new Measurement();
            if (chain == null || chain.IsEmpty) return result;

            // A private copy with every oscillator made to start from the top of its cycle. An oscillator whose phase is
            // not reset picks up wherever a free-running clock has got to, which is right for playback and wrong for a
            // picture: it would draw a different slice of the wave on every re-measure. Starting at a fixed point makes the
            // drawing repeatable; the live marker drawn over it during playback shows where the real one actually is.
            var measured = chain.DeepCopy();
            foreach (var m in measured.modifiers) if (m.type == ZoundModifierType.Lfo && m.p != null && m.p.Length > 3) m.p[3] = 1f;

            result.truncated = playSeconds > MaxSeconds;
            float play = Math.Min(Math.Max(playSeconds, 0.05f), MaxSeconds);
            result.playSeconds = play;

            MeasureBands(measured, play, bandCount, level, ref result);
            MeasureLanes(measured, play, ref result);
            result.costMs = sw.Elapsed.TotalMilliseconds;
            return result;
        }

        /// <summary>
        /// Only the modulation half of <see cref="Measure"/>: what every modifier outputs, and what every modulated
        /// parameter does, across one play. A few milliseconds, so it can run for a modifier's own editor whether or not
        /// the analyser is open.
        /// </summary>
        public static Measurement MeasureModulation(ZoundEffectChain chain, float playSeconds) {
            var result = new Measurement();
            if (chain == null || chain.IsEmpty) return result;
            var measured = chain.DeepCopy();
            foreach (var m in measured.modifiers) if (m.type == ZoundModifierType.Lfo && m.p != null && m.p.Length > 3) m.p[3] = 1f;
            result.truncated = playSeconds > MaxSeconds;
            result.playSeconds = Math.Min(Math.Max(playSeconds, 0.05f), MaxSeconds);
            MeasureLanes(measured, result.playSeconds, ref result);
            return result;
        }

        static void MeasureBands(ZoundEffectChain chain, float play, int bandCount, float level, ref Measurement result) {
            // The test signal lasts as long as the play, so the engine lays out everything that runs "over the play"
            // against the right length. A play shorter than two windows is padded to that, because nothing can be read
            // from less; such a sound is too short for its changes over time to be resolved here anyway.
            int frames = Math.Max(WINDOW * 2, (int)(play * SAMPLE_RATE));
            float seconds = (float)frames / SAMPLE_RATE;

            // One period of noise, REPEATED. Every window then sees the identical signal, so the input side of the
            // comparison is exactly the same each time and any variation left is the effect genuinely doing something
            // different. Seeded fixed, for the same reason.
            var noise = new float[frames * 2];
            var rng = new System.Random(12345);
            var period = new float[PERIOD];
            for (int i = 0; i < PERIOD; i++) period[i] = (float)(rng.NextDouble() * 2.0 - 1.0) * level;
            for (int i = 0; i < frames; i++) { float s = period[i % PERIOD]; noise[i * 2] = s; noise[i * 2 + 1] = s; }

            var dry = ZoundDspOffline.Render(noise, 2, SAMPLE_RATE, SAMPLE_RATE, null, 1f, 1f, seconds);
            var wet = ZoundDspOffline.Render(noise, 2, SAMPLE_RATE, SAMPLE_RATE, chain, 1f, 1f, seconds);
            if (dry == null || wet == null) return;

            // Readings are taken only while the play is still running: past its end the source has stopped and there is
            // nothing to compare.
            int usable = Math.Min(Math.Min(dry.frames, wet.frames), Math.Max(WINDOW, (int)(play * SAMPLE_RATE)));
            int windows = usable >= WINDOW ? (usable - WINDOW) / HOP + 1 : 0;
            if (windows < 1) windows = Math.Min(dry.frames, wet.frames) >= WINDOW ? 1 : 0;
            if (windows < 1) return;

            var bands = MakeBands(bandCount);
            var binLo = new int[bands.Length];
            var binHi = new int[bands.Length];
            for (int b = 0; b < bands.Length; b++) {
                // The signal has energy only on its lines, at every second bin. A band narrow enough to fall between two
                // lines — which happens only in the deep bass — reads the line nearest its middle instead of reading
                // nothing: the neighbouring line is a few hertz away, and nothing an effect does to bass changes over a
                // few hertz. Without this the lowest octave was a row of blanks.
                int lo = BinOf(bands[b].lowHz), hi = BinOf(bands[b].highHz);
                if (!ContainsLine(lo, hi)) {
                    int nearest = (int)Math.Round(bands[b].centreHz / LINE_HZ) * 2;
                    lo = nearest; hi = nearest + 1;
                }
                binLo[b] = lo; binHi[b] = hi;
                bands[b].overTime = new float[windows];
            }
            result.broadbandDbOverTime = new float[windows];
            result.readingSeconds = new float[windows];

            var dryMag = new float[WINDOW / 2];
            var wetMag = new float[WINDOW / 2];
            bool anyChange = false;
            for (int w = 0; w < windows; w++) {
                int off = w * HOP;
                result.readingSeconds[w] = (off + WINDOW * 0.5f) / SAMPLE_RATE;
                Spectrum(dry.left, off, dryMag);
                Spectrum(wet.left, off, wetMag);

                double dryTotal = 0, wetTotal = 0;
                for (int k = 0; k < dryMag.Length; k++) { dryTotal += dryMag[k]; wetTotal += wetMag[k]; }
                result.broadbandDbOverTime[w] = ToDb(wetTotal, dryTotal);

                for (int b = 0; b < bands.Length; b++) {
                    // One ratio PER BIN, averaged in decibels, not one ratio for the summed band: taken per bin the test
                    // signal cancels out of the answer, so a static effect reads the same every time.
                    // How much of the band's ENERGY came through: everything the chain put out across the band, over
                    // everything that went in. Measured, and chosen over a per-frequency ratio for this reason: a
                    // modulated effect moves energy sideways, into frequencies a few hertz either side of the test
                    // signal's own. A per-frequency ratio counts that moved energy as lost, so the faster the modulation
                    // the more jagged the bars became — a still filter read smooth, and the same filter wobbled at a few
                    // cycles a second read as a row of bars jumping up and down by the better part of a decibel. The
                    // band's total is unchanged by where inside it the energy sits, which is what "how much of the bass
                    // got through" actually asks. The test signal is identical in every reading, so the answer is still
                    // perfectly steady for an effect that is not moving. Checked against a case with a known answer — a
                    // cutoff wobbled at up to ten cycles a second but kept far above the bass, where nothing below 1 kHz
                    // should change at all: every bass band read within 0.12 dB of unchanged. So where the bass DOES move
                    // under a modulated filter, it is because the filter really reaches it — a resonant cutoff sweeping
                    // down through a band genuinely lifts it for a moment.
                    double wetE = 0, dryE = 0;
                    for (int k = binLo[b]; k < binHi[b] && k < dryMag.Length; k++) {
                        dryE += (double)dryMag[k] * dryMag[k];
                        wetE += (double)wetMag[k] * wetMag[k];
                    }
                    if (dryE < BIN_FLOOR * BIN_FLOOR) { bands[b].overTime[w] = 0f; continue; }
                    bands[b].measurable = true;
                    // The output needs a floor as well: where an effect cuts almost completely, what remains is rounding
                    // noise, and a ratio of rounding noise is a different random number every time.
                    float db = wetE < BIN_FLOOR * BIN_FLOOR ? FLOOR_DB : (float)(10.0 * Math.Log10(wetE / dryE));
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
                bands[b].minDb = min; bands[b].maxDb = max; bands[b].averageDb = sum / windows;
            }

            result.bands = bands;
            result.windows = windows;
            result.silentOrUnchanged = !anyChange;
        }

        /// <summary>
        /// Follows every modulated parameter across one play, read straight from the engine after each block.
        ///
        /// This is the part of the picture that can show FAST movement, and a modulator's shape. The per-band reading has to
        /// average over a third of a second to resolve the bass; this has no such limit, because it is not a frequency
        /// reading at all — it is the very value the engine hands the effect, at the rate the engine updates it. What the
        /// listener gets is what is drawn, including where a modulator's swing is pinned against the end of a parameter's
        /// range, which no amount of looking at the modulator's own settings would reveal.
        /// </summary>
        static void MeasureLanes(ZoundEffectChain chain, float play, ref Measurement result) {
            // Rendered in steps of exactly the engine's own control step, so every value the engine applies is seen.
            // Reading once per larger block and joining the dots with straight lines draws a shape the engine never
            // produces — at ten cycles a second, a polygon of five corners where the engine applies a clean sine. That
            // mistake was made once while investigating this very display; this is the guard against it.
            //
            // A long play is read in whole multiples of that step instead, so there are never more than about two thousand
            // values — more than the widest lane has pixels to show. At the longest play measured that spacing is about six
            // milliseconds of sound, still far finer than any modulation worth drawing. (Measured cost of this render: about
            // twelve milliseconds for a one-second sound, and about a sixth of a second at the twelve-second cap.)
            int step = ZoundDspConstants.CONTROL_BLOCK;
            int block = step * Math.Max(1, (int)Math.Ceiling(play * SAMPLE_RATE / (2048.0 * step)));
            result.laneStepSeconds = (float)block / SAMPLE_RATE;
            if (chain.modifiers == null || chain.modifiers.Count == 0) { result.lanes = new Lane[0]; return; }

            // The effects themselves are switched off for this render, because nothing here listens to the audio and
            // processing it is wasted work. Bypassing an effect does not bypass its modulation: the engine still evaluates
            // every modifier and moves every parameter exactly as it would — checked, the lanes came out value-for-value
            // identical with the effects on and off.
            chain = chain.DeepCopy();
            foreach (var n in chain.nodes) n.enabled = false;
            var layout = ChainLayout.Build(chain, SAMPLE_RATE);
            var lanes = new List<Lane>();
            var flats = new List<int>();
            var descs = new List<ParamDesc>();
            foreach (var b in chain.bindings) {
                if (b.modifierIndex < 0 || b.modifierIndex >= chain.modifiers.Count) continue;
                if (!chain.modifiers[b.modifierIndex].enabled) continue;
                if (!TryParam(chain, b.nodeIndex, b.paramIndex, out var pd, out float authored, out string name)) continue;
                int flat = SapVoiceRegistry.FlatIndexOf(layout, b.nodeIndex, b.paramIndex);
                if (flat < 0) continue;
                // One lane per parameter, however many modifiers drive it: the lane shows their combined result, which is
                // what the effect receives.
                bool dup = false;
                foreach (var l in lanes) if (l.nodeIndex == b.nodeIndex && l.paramIndex == b.paramIndex) dup = true;
                if (dup) continue;
                lanes.Add(new Lane { modifierIndex = b.modifierIndex, nodeIndex = b.nodeIndex, paramIndex = b.paramIndex,
                                     label = name, authored01 = Position01(pd, authored) });
                flats.Add(flat);
                descs.Add(pd);
            }
            int frames = Math.Max(block, (int)(play * SAMPLE_RATE));
            // Any input will do — no modifier listens to the audio — but it has to last exactly one play, because the
            // engine measures "over the play" against the length of what it is reading.
            var input = new float[frames * 2];
            for (int i = 0; i < input.Length; i++) input[i] = 0.1f;
            var values = new List<float>[lanes.Count];
            for (int l = 0; l < lanes.Count; l++) values[l] = new List<float>(frames / block + 2);
            var outputs = new List<float>[chain.modifiers.Count];
            for (int m = 0; m < outputs.Length; m++) outputs[m] = new List<float>(frames / block + 2);

            var final = ZoundDspOffline.Render(input, 2, SAMPLE_RATE, SAMPLE_RATE, chain, 1f, 1f, (float)frames / SAMPLE_RATE,
                                               0f, 0f, block, false, (voice, written) => {
                if (written == 0) return;
                var live = voice.pLive;
                var mods = voice.modValues;
                for (int m = 0; m < outputs.Length; m++) outputs[m].Add(m < mods.Length ? mods[m] : 0f);
                for (int l = 0; l < lanes.Count; l++)
                    values[l].Add(flats[l] < live.Length ? Position01(descs[l], live[flats[l]]) : lanes[l].authored01);
            });
            if (final == null) { result.lanes = new Lane[0]; return; }

            var arr = lanes.ToArray();
            for (int l = 0; l < arr.Length; l++) arr[l].position01 = values[l].ToArray();
            result.lanes = arr;
            result.modifierOutput = new float[outputs.Length][];
            for (int m = 0; m < outputs.Length; m++) result.modifierOutput[m] = outputs[m].ToArray();
        }

        /// <summary>Where a value sits along its slider, 0 to 1, matching how the slider maps its travel (log or linear).</summary>
        public static float Position01(ParamDesc pd, float value) {
            float t;
            if (pd.curve == ParamCurve.Logarithmic) {
                double lmin = Math.Log(Math.Max(pd.min, 1e-4f)), lmax = Math.Log(Math.Max(pd.max, 1e-4f));
                t = lmax > lmin ? (float)((Math.Log(Math.Max(value, 1e-4f)) - lmin) / (lmax - lmin)) : 0f;
            }
            else t = pd.max > pd.min ? (value - pd.min) / (pd.max - pd.min) : 0f;
            return t < 0f ? 0f : t > 1f ? 1f : t;
        }

        static bool TryParam(ZoundEffectChain chain, int nodeIndex, int paramIndex, out ParamDesc pd, out float authored, out string name) {
            pd = default; authored = 0f; name = null;
            if (nodeIndex < 0) {
                if (paramIndex < 0 || paramIndex >= SourceStageParam.Count) return false;
                pd = ZoundEffectDescriptors.SourceStageParams[paramIndex];
                authored = pd.def;
                name = "Source " + pd.name.ToLowerInvariant();
                return true;
            }
            if (nodeIndex >= chain.nodes.Count) return false;
            var node = chain.nodes[nodeIndex];
            var d = ZoundEffectDescriptors.Get(node.type);
            if (d == null || paramIndex < 0 || paramIndex >= d.parameters.Length) return false;
            node.EnsureParams();
            pd = d.parameters[paramIndex];
            authored = node.p[paramIndex];
            name = d.displayName + " " + pd.name.ToLowerInvariant();
            return true;
        }

        /// <summary>
        /// Bands spaced so that each covers a similar musical interval rather than a similar number of hertz, because
        /// hearing works that way: the octave from 100 to 200 Hz matters as much as the one from 5 to 10 kHz. They cover
        /// the whole range of hearing — a first version started at 60 Hz, a limit inherited from a shorter analysis window,
        /// and so hid the sub-bass entirely.
        /// </summary>
        static Band[] MakeBands(int count) {
            var bands = new Band[count];
            double ratio = Math.Pow(HighHz / LowHz, 1.0 / count);
            double edge = LowHz;
            for (int i = 0; i < count; i++) {
                double next = edge * ratio;
                bands[i].lowHz = (float)edge;
                bands[i].highHz = (float)next;
                bands[i].centreHz = (float)Math.Sqrt(edge * next);
                edge = next;
            }
            return bands;
        }

        /// <summary>Where along the display a frequency sits, 0 at <see cref="LowHz"/> to 1 at <see cref="HighHz"/>.</summary>
        public static float FrequencyPosition01(float hz) {
            return (float)(Math.Log(Math.Max(hz, 1f) / LowHz) / Math.Log(HighHz / LowHz));
        }

        static bool ContainsLine(int lo, int hi) {
            for (int k = lo; k < hi; k++) if ((k & 1) == 0) return true;
            return false;
        }

        static int BinOf(float hz) {
            int bin = (int)Math.Round(hz * WINDOW / (double)SAMPLE_RATE);
            if (bin < 0) bin = 0;
            if (bin > WINDOW / 2 - 1) bin = WINDOW / 2 - 1;
            return bin;
        }

        /// <summary>Decibel difference between two energies, with a floor, so near-silence over near-silence is not believed.</summary>
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

        /// <summary>Magnitude spectrum of one window, faded in and out first so its own edges do not read as content.</summary>
        static void Spectrum(float[] samples, int offset, float[] magOut) {
            int n = WINDOW;
            var re = new float[n];
            var im = new float[n];
            for (int i = 0; i < n; i++) {
                int s = offset + i;
                float v = s < samples.Length ? samples[s] : 0f;
                float win = 0.5f - 0.5f * (float)Math.Cos(2.0 * Math.PI * i / (n - 1));   // Hann
                re[i] = v * win;
            }
            Fft(re, im);
            for (int k = 0; k < magOut.Length; k++) magOut[k] = (float)Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
        }

        /// <summary>Iterative radix-2 transform. Length must be a power of two, which WINDOW is.</summary>
        static void Fft(float[] re, float[] im) {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++) {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
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
                        re[a] += tRe; im[a] += tIm;
                        float nRe = curRe * wRe - curIm * wIm;
                        curIm = curRe * wIm + curIm * wRe;
                        curRe = nRe;
                    }
                }
            }
        }
    }
}
