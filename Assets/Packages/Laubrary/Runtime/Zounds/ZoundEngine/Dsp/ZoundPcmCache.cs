using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// One clip's samples, read once and shared read-only by every voice that plays it.
    /// Interleaved float PCM exactly as AudioClip.GetData returns it.
    /// </summary>
    public sealed class PcmClip {
        public AudioClip clip;
        public float[] samples;
        public int channels;
        public int frequency;
        public int frames;
        public float peak;
        public bool valid;
        public string problem;
        public bool problemLogged;
        public float LengthSeconds => frequency > 0 ? (float)frames / frequency : 0f;
        public long Bytes => samples != null ? samples.LongLength * 4 : 0;
    }

    /// <summary>
    /// Shared per-clip PCM cache. AudioClip.GetData is main-thread only and returns silence for a clip
    /// whose Load Type does not permit reading, so every clip is validated on first use and the problem
    /// is recorded rather than played as zeros.
    /// </summary>
    public static class ZoundPcmCache {

        private static readonly Dictionary<AudioClip, PcmClip> cache = new Dictionary<AudioClip, PcmClip>();
        private static long totalBytes;

        public static int Count => cache.Count;
        public static long TotalBytes => totalBytes;

        public static PcmClip Get(AudioClip clip) {
            if (clip == null) return null;
            if (cache.TryGetValue(clip, out var pcm)) return pcm;
            pcm = Read(clip);
            cache.Add(clip, pcm);
            totalBytes += pcm.Bytes;
            return pcm;
        }

        public static bool TryGetCached(AudioClip clip, out PcmClip pcm) => cache.TryGetValue(clip, out pcm);

        public static void Clear() {
            cache.Clear();
            totalBytes = 0;
            ZoundTimeStretcher.Clear();
        }

        public static void Remove(AudioClip clip) {
            if (clip != null && cache.TryGetValue(clip, out var pcm)) {
                totalBytes -= pcm.Bytes;
                cache.Remove(clip);
            }
        }

        /// <summary>Why a clip cannot be read into the engine, or null when it can.</summary>
        public static string Validate(AudioClip clip) {
            if (clip == null) return "No clip.";
            if (clip.loadType == AudioClipLoadType.Streaming) return "Load Type is Streaming; the DSP engine needs Decompress On Load.";
            if (clip.loadType == AudioClipLoadType.CompressedInMemory) return "Load Type is Compressed In Memory; the DSP engine needs Decompress On Load.";
            if (clip.channels < 1) return "Clip has no channels.";
            if (clip.samples <= 0) return "Clip has no samples.";
            return null;
        }

        private static PcmClip Read(AudioClip clip) {
            var pcm = new PcmClip { clip = clip, channels = clip.channels, frequency = clip.frequency, frames = clip.samples };
            pcm.problem = Validate(clip);
            if (pcm.problem != null) { pcm.samples = new float[0]; pcm.frames = 0; return pcm; }
            if (clip.loadState != AudioDataLoadState.Loaded) {
                clip.LoadAudioData();
                if (clip.loadState != AudioDataLoadState.Loaded) {
                    pcm.problem = "Audio data is not loaded (state " + clip.loadState + "); preload audio data or load in foreground.";
                    pcm.samples = new float[0]; pcm.frames = 0; return pcm;
                }
            }
            var data = new float[clip.samples * clip.channels];
            bool ok;
            try { ok = clip.GetData(data, 0); }
            catch (System.Exception e) { ok = false; pcm.problem = e.Message; }
            if (!ok) {
                if (pcm.problem == null) pcm.problem = "AudioClip.GetData failed.";
                pcm.samples = new float[0]; pcm.frames = 0; return pcm;
            }
            float peak = 0f;
            for (int i = 0; i < data.Length; i++) {
                float a = data[i] < 0f ? -data[i] : data[i];
                if (a > peak) peak = a;
            }
            pcm.samples = data;
            pcm.peak = peak;
            pcm.valid = true;
            if (peak <= 0f) pcm.problem = "Sample data is all zeros; check the clip's import settings.";
            return pcm;
        }

        public static IEnumerable<PcmClip> All => cache.Values;
    }

}
