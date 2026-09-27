using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds.EditorTools {

    /// <summary>
    /// Draws what is coming out of the speakers right now: a spectrum, a scrolling record of that spectrum over time, or
    /// the waveform itself.
    ///
    /// **This is the honest counterpart to the chain analyser.** The analyser answers "what is this chain doing", which
    /// requires interpretation and cannot describe every effect fairly. This answers "what is actually happening", which
    /// requires no interpretation at all — it is the signal, including everything, with nothing modelled and nothing
    /// classified. When the two disagree, this one is right.
    ///
    /// **Where the samples come from, and why it had to be this way.** Unity's own readers for a playing sound were tried
    /// first and return silence for audio produced through a generator — both the per-source reader and the one for the
    /// final mix — because they tap the ordinary clip-playback route that this engine deliberately bypasses. That was
    /// measured rather than assumed, and it is the reason the engine keeps a small rolling copy of its own output instead.
    ///
    /// **It costs the audio path nothing unless something is looking.** That rolling copy is only allocated for voices
    /// started while this window is open; otherwise the per-block cost is a single check that it does not exist. A
    /// visualiser that slowed down the thing it visualises would be a poor trade.
    /// </summary>
    public class LiveOutputView {

        public enum Mode { Spectrum, Spectrogram, Waveform }

        const int SAMPLES = 1024;               // a power of two, as the spectrum reader requires
        const int HISTORY = 220;                // columns of scrolling history

        readonly float[] spectrum = new float[SAMPLES];
        readonly float[] wave = new float[SAMPLES];
        float[,] history;
        int historyHead;
        float peakHold;

        public float gain = 1f;
        public bool logFrequency = true;

        /// <summary>
        /// Pulls the current samples from whichever audio source is actually producing sound, and reports whether it
        /// found one. Returns false when nothing is playing, which is a normal state and not an error.
        /// </summary>
        public bool Sample(out string sourceName) {
            sourceName = null;

            // Asking for a monitor buffer is what makes the engine keep one. Done here rather than globally so it is on
            // exactly while somebody is watching.
            if (Dsp.ZoundSapVoiceGenerator.monitorSamples < SAMPLES * 4) {
                Dsp.ZoundSapVoiceGenerator.monitorSamples = SAMPLES * 4;
            }

            var generator = FindMonitorableVoice();
            if (generator == null || !generator.ReadMonitor(wave)) {
                // Decay what is on screen instead of freezing it, so a stopped sound visibly falls away rather than
                // leaving a misleading still image of the last moment it was audible.
                for (int i = 0; i < SAMPLES; i++) { spectrum[i] *= 0.85f; wave[i] *= 0.85f; }
                return false;
            }
            sourceName = generator.gameObject.name;
            ComputeSpectrum();
            return true;
        }

        /// <summary>Turns the monitor samples into a magnitude spectrum, windowed so that cutting the signal at an
        /// arbitrary point does not read as broadband content that is not there.</summary>
        void ComputeSpectrum() {
            int n = SAMPLES;
            var re = new float[n];
            var im = new float[n];
            for (int i = 0; i < n; i++) {
                float w = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * i / (n - 1));
                re[i] = wave[i] * w;
                im[i] = 0f;
            }
            Fft(re, im);
            for (int k = 0; k < n / 2; k++) spectrum[k] = Mathf.Sqrt(re[k] * re[k] + im[k] * im[k]) * 2f / n;
        }

        static void Fft(float[] re, float[] im) {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++) {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }
            for (int len = 2; len <= n; len <<= 1) {
                float ang = -2f * Mathf.PI / len;
                float wRe = Mathf.Cos(ang), wIm = Mathf.Sin(ang);
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

        /// <summary>
        /// A source that is currently making sound. Searched rather than held, because the engine hands its pooled
        /// sources out and takes them back constantly, so any reference kept between frames would usually be stale.
        /// </summary>
        static Dsp.ZoundSapVoiceGenerator FindMonitorableVoice() {
            var all = Resources.FindObjectsOfTypeAll<Dsp.ZoundSapVoiceGenerator>();
            for (int i = 0; i < all.Length; i++) {
                var g = all[i];
                if (g == null) continue;
                if (g.IsPlaying) return g;
            }
            return null;
        }

        public void Draw(Rect area, Mode mode, bool playing, string sourceName) {
            EditorGUI.DrawRect(area, new Color(0.09f, 0.09f, 0.11f));
            switch (mode) {
                case Mode.Spectrum: DrawSpectrum(area); break;
                case Mode.Spectrogram: DrawSpectrogram(area); break;
                default: DrawWaveform(area); break;
            }

            var label = new GUIStyle(EditorStyles.miniLabel);
            label.normal.textColor = playing ? new Color(0.6f, 0.85f, 0.65f) : new Color(0.6f, 0.6f, 0.65f);
            GUI.Label(new Rect(area.x + 6f, area.y + 4f, area.width - 12f, 16f),
                      playing ? "live — " + sourceName : "nothing playing", label);
        }

        void DrawSpectrum(Rect area) {
            int bins = SAMPLES / 2;
            float mid = area.yMax;
            int columns = Mathf.Max(32, (int)(area.width / 3f));
            float colW = area.width / columns;

            for (int c = 0; c < columns; c++) {
                // Log spacing by default: linear spacing crams everything musical into the leftmost eighth and wastes
                // most of the width on frequencies nobody is listening for.
                float t0 = c / (float)columns, t1 = (c + 1) / (float)columns;
                int b0 = BinFor(t0, bins), b1 = BinFor(t1, bins);
                if (b1 <= b0) b1 = b0 + 1;

                float sum = 0f;
                for (int b = b0; b < b1 && b < bins; b++) sum += spectrum[b];
                float v = sum / (b1 - b0);
                float db = 20f * Mathf.Log10(Mathf.Max(v * gain, 1e-6f));
                float norm = Mathf.InverseLerp(-80f, 0f, db);
                float h = Mathf.Clamp01(norm) * area.height;

                // Colour by height rather than by frequency: it makes a loud band obvious at a glance, which is what a
                // level display is for.
                var colour = Color.Lerp(new Color(0.25f, 0.45f, 0.6f), new Color(0.95f, 0.8f, 0.35f), Mathf.Clamp01(norm));
                EditorGUI.DrawRect(new Rect(area.x + c * colW, mid - h, Mathf.Max(1f, colW - 1f), h), colour);
            }
        }

        void DrawSpectrogram(Rect area) {
            int bins = SAMPLES / 2;
            int rows = Mathf.Max(16, (int)area.height / 2);
            if (history == null || history.GetLength(1) != rows) { history = new float[HISTORY, rows]; historyHead = 0; }

            // One new column per repaint, oldest scrolling off. This is the view that shows a pattern over time — a
            // repeating modulation, or a tail decaying — which a single instant cannot.
            for (int r = 0; r < rows; r++) {
                float t0 = r / (float)rows, t1 = (r + 1) / (float)rows;
                int b0 = BinFor(t0, bins), b1 = BinFor(t1, bins);
                if (b1 <= b0) b1 = b0 + 1;
                float sum = 0f;
                for (int b = b0; b < b1 && b < bins; b++) sum += spectrum[b];
                history[historyHead, r] = sum / (b1 - b0);
            }
            historyHead = (historyHead + 1) % HISTORY;

            float colW = area.width / HISTORY;
            float rowH = area.height / rows;
            for (int c = 0; c < HISTORY; c++) {
                int idx = (historyHead + c) % HISTORY;
                float x = area.x + c * colW;
                for (int r = 0; r < rows; r++) {
                    float db = 20f * Mathf.Log10(Mathf.Max(history[idx, r] * gain, 1e-6f));
                    float norm = Mathf.Clamp01(Mathf.InverseLerp(-80f, 0f, db));
                    if (norm <= 0.02f) continue;
                    var colour = norm < 0.5f
                        ? Color.Lerp(new Color(0.1f, 0.12f, 0.25f), new Color(0.2f, 0.5f, 0.7f), norm * 2f)
                        : Color.Lerp(new Color(0.2f, 0.5f, 0.7f), new Color(1f, 0.9f, 0.5f), (norm - 0.5f) * 2f);
                    // Low frequencies at the bottom, which is the convention everywhere else this gets compared to.
                    EditorGUI.DrawRect(new Rect(x, area.yMax - (r + 1) * rowH, Mathf.Max(1f, colW), Mathf.Max(1f, rowH)), colour);
                }
            }
        }

        void DrawWaveform(Rect area) {
            float mid = area.y + area.height * 0.5f;
            EditorGUI.DrawRect(new Rect(area.x, mid, area.width, 1f), new Color(0.3f, 0.3f, 0.34f));

            int columns = Mathf.Max(32, (int)area.width);
            int per = Mathf.Max(1, SAMPLES / columns);
            float loudest = 0f;

            for (int c = 0; c < columns; c++) {
                int start = c * per;
                float lo = 0f, hi = 0f;
                for (int i = start; i < start + per && i < SAMPLES; i++) {
                    float v = wave[i] * gain;
                    if (v < lo) lo = v;
                    if (v > hi) hi = v;
                    float a = Mathf.Abs(v);
                    if (a > loudest) loudest = a;
                }
                float yTop = mid - Mathf.Clamp(hi, -1f, 1f) * area.height * 0.5f;
                float yBot = mid - Mathf.Clamp(lo, -1f, 1f) * area.height * 0.5f;
                EditorGUI.DrawRect(new Rect(area.x + c, yTop, 1f, Mathf.Max(1f, yBot - yTop)),
                                   new Color(0.45f, 0.75f, 0.85f));
            }

            // A peak that falls back slowly, because an instantaneous number is unreadable and clipping is worth
            // catching after the moment it happened.
            peakHold = Mathf.Max(loudest, peakHold * 0.95f);
            bool clipping = peakHold >= 0.999f;
            var style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = clipping ? new Color(1f, 0.5f, 0.45f) : new Color(0.6f, 0.6f, 0.65f);
            style.alignment = TextAnchor.UpperRight;
            GUI.Label(new Rect(area.x, area.y + 4f, area.width - 6f, 16f),
                      "peak " + peakHold.ToString("0.00") + (clipping ? "  CLIPPING" : ""), style);
        }

        int BinFor(float t, int bins) {
            if (!logFrequency) return Mathf.Clamp((int)(t * bins), 0, bins - 1);
            // 30 Hz to about 18 kHz spread evenly by octave.
            const float low = 30f, high = 18000f;
            float hz = low * Mathf.Pow(high / low, t);
            int bin = (int)(hz * SAMPLES / (float)AudioSettings.outputSampleRate);
            return Mathf.Clamp(bin, 0, bins - 1);
        }
    }
}
