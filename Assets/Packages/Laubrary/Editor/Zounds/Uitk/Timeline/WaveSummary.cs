using System.Collections.Generic;
using Laubrary.Zounds.Dsp;
using UnityEngine;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// A waveform at every zoom, fast (T-0562). For each source file, the lowest and highest sample of every block of 64
    /// frames, and again of every 4 of those blocks, and so on up, across all channels. Drawing a pixel column asks for
    /// the range of source seconds it covers and reads the coarsest level whose blocks are still no wider than that range,
    /// so a minute of audio across 300 pixels and a tenth of a second across the same 300 pixels both cost one pass over a
    /// few hundred numbers. Closer in than one block per column it reads the samples themselves.
    ///
    /// Held in memory only, one per source file however many tracks play excerpts of it; never written anywhere, never a
    /// rendered file (the project's rule). Rebuilt if the file's samples are reloaded.
    /// </summary>
    internal sealed class WaveSummary {

        const int BaseBlock = 64, Factor = 4;

        readonly PcmClip pcm;
        readonly List<float[]> mins = new List<float[]>(), maxs = new List<float[]>();
        readonly List<int> blockFrames = new List<int>();

        static readonly Dictionary<int, WaveSummary> cache = new Dictionary<int, WaveSummary>();

        /// <summary>The summary of <paramref name="clip"/>'s samples; null when they cannot be read.</summary>
        public static WaveSummary For(AudioClip clip) {
            if (clip == null) return null;
            var pcm = ZoundPcmCache.Get(clip);
            if (pcm == null || !pcm.valid || pcm.samples == null) return null;
            int key = clip.GetInstanceID();
            if (cache.TryGetValue(key, out var s) && ReferenceEquals(s.pcm, pcm)) return s;
            s = new WaveSummary(pcm);
            cache[key] = s;
            return s;
        }

        public float LengthSeconds => pcm.LengthSeconds;
        public float Peak => pcm.peak > 0f ? pcm.peak : 1f;

        WaveSummary(PcmClip pcm) {
            this.pcm = pcm;
            int ch = Mathf.Max(1, pcm.channels), frames = pcm.frames;
            var s = pcm.samples;
            int n = (frames + BaseBlock - 1) / BaseBlock;
            var lo = new float[n]; var hi = new float[n];
            for (int b = 0; b < n; b++) {
                float mn = float.MaxValue, mx = float.MinValue;
                int f0 = b * BaseBlock, f1 = Mathf.Min(frames, f0 + BaseBlock);
                for (int f = f0; f < f1; f++) {
                    int i = f * ch;
                    for (int c = 0; c < ch; c++) { float v = s[i + c]; if (v < mn) mn = v; if (v > mx) mx = v; }
                }
                lo[b] = mn; hi[b] = mx;
            }
            mins.Add(lo); maxs.Add(hi); blockFrames.Add(BaseBlock);
            while (lo.Length > 1) {
                int m = (lo.Length + Factor - 1) / Factor;
                var l2 = new float[m]; var h2 = new float[m];
                for (int b = 0; b < m; b++) {
                    float mn = float.MaxValue, mx = float.MinValue;
                    for (int k = b * Factor; k < Mathf.Min(lo.Length, b * Factor + Factor); k++) { if (lo[k] < mn) mn = lo[k]; if (hi[k] > mx) mx = hi[k]; }
                    l2[b] = mn; h2[b] = mx;
                }
                mins.Add(l2); maxs.Add(h2); blockFrames.Add(blockFrames[blockFrames.Count - 1] * Factor);
                lo = l2; hi = h2;
            }
        }

        /// <summary>The lowest and highest sample between source seconds <paramref name="from"/> and <paramref name="to"/>;
        /// false when that range lies outside the file.</summary>
        public bool Range(float from, float to, out float min, out float max) {
            min = 0f; max = 0f;
            if (to < from) { var t = from; from = to; to = t; }
            int frames = pcm.frames, fr = pcm.frequency, ch = Mathf.Max(1, pcm.channels);
            int f0 = Mathf.FloorToInt(from * fr), f1 = Mathf.CeilToInt(to * fr);
            if (f1 <= 0 || f0 >= frames) return false;
            f0 = Mathf.Clamp(f0, 0, frames - 1); f1 = Mathf.Clamp(f1, f0 + 1, frames);
            int span = f1 - f0;
            float mn = float.MaxValue, mx = float.MinValue;
            if (span < BaseBlock * 2) {
                var s = pcm.samples;
                for (int f = f0; f < f1; f++) {
                    int i = f * ch;
                    for (int c = 0; c < ch; c++) { float v = s[i + c]; if (v < mn) mn = v; if (v > mx) mx = v; }
                }
            }
            else {
                int level = 0;
                while (level + 1 < blockFrames.Count && blockFrames[level + 1] * 2 <= span) level++;
                int bf = blockFrames[level];
                var lo = mins[level]; var hi = maxs[level];
                int b0 = f0 / bf, b1 = Mathf.Min(lo.Length - 1, (f1 - 1) / bf);
                for (int b = b0; b <= b1; b++) { if (lo[b] < mn) mn = lo[b]; if (hi[b] > mx) mx = hi[b]; }
            }
            min = mn; max = mx;
            return true;
        }
    }
}
