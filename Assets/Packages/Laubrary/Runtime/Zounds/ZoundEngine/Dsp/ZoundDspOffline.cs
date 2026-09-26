using UnityEngine;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// Renders a voice on the calling thread, outside the graph: the same DspVoice code path the audio
    /// thread runs, driven block by block over a PCM buffer. Used by verification probes (null tests,
    /// allocation checks, A/B against the old bake) and by editor previews that need a rendered buffer.
    /// </summary>
    public static class ZoundDspOffline {

        public sealed class Result {
            public float[] left, right;
            public int frames;
            public int sampleRate;
            public float peak;
            public long gcBytesDelta;
            public int blocks;
        }

        /// <summary>
        /// Renders <paramref name="seconds"/> of output (or until the voice finishes, whichever comes first).
        /// pitch is the base pitch (the carrier's), outGain the output gain; the chain is laid out from
        /// <paramref name="chain"/>; source-stage trim from startSeconds/endSeconds (0 = full clip).
        /// </summary>
        public static Result Render(float[] interleaved, int channels, int frequency, int sampleRate, ZoundEffectChain chain,
                                    float pitch, float outGain, float seconds, float startSeconds = 0f, float endSeconds = 0f,
                                    int blockFrames = 1024, bool measureAllocation = false, System.Action<DspVoice, int> perBlock = null) {
            var pcm = new PcmClip { channels = channels, frequency = frequency, frames = interleaved.Length / channels, samples = interleaved, valid = true };
            float peak = 0f;
            for (int i = 0; i < interleaved.Length; i++) { float a = interleaved[i] < 0 ? -interleaved[i] : interleaved[i]; if (a > peak) peak = a; }
            pcm.peak = peak;
            var layout = chain != null && !chain.IsEmpty ? ChainLayout.Build(chain, sampleRate) : ChainLayout.Empty;
            var voice = new DspVoice(0, true);
            var events = new ZoundDspEventRing();
            double startFrame = Mathf.Clamp(startSeconds, 0f, pcm.LengthSeconds) * frequency;
            double endFrame = endSeconds > startSeconds ? Mathf.Min(endSeconds, pcm.LengthSeconds) * frequency : pcm.frames;
            float sourceDuration = (float)((endFrame - startFrame) / frequency) / Mathf.Max(pitch, 0.01f);
            int totalFrames = Mathf.CeilToInt(seconds * sampleRate);
            var result = new Result { left = new float[totalFrames], right = new float[totalFrames], sampleRate = sampleRate };
            try {
                voice.Prepare(1, 0, pcm, layout, sampleRate, startFrame, endFrame, pitch, outGain, sourceDuration, false);
                voice.Publish();

                long before = 0;
                if (measureAllocation) { System.GC.Collect(); System.GC.WaitForPendingFinalizers(); System.GC.Collect(); before = System.GC.GetTotalMemory(false); }
                int written = 0;
                int blocks = 0;
                while (written < totalFrames && voice.State != VoiceState.Free) {
                    int n = Mathf.Min(blockFrames, totalFrames - written);
                    perBlock?.Invoke(voice, written);
                    voice.Render(n, sampleRate, events, written);
                    blocks++;
                    // NativeArray<T>.CopyTo(T[]) requires the destination array's length to match exactly,
                    // so it cannot target a `written`-offset slice of result.left/right in one call the way
                    // Array.Copy(src, 0, dst, offset, n) could. A plain indexed loop copies exactly indices
                    // 0..n-1 of voice.bufL/bufR into result.left/right starting at `written`, which is the
                    // same range and the same values the original Array.Copy calls moved.
                    for (int i = 0; i < n; i++) { result.left[written + i] = voice.bufL[i]; result.right[written + i] = voice.bufR[i]; }
                    written += n;
                }
                if (measureAllocation) result.gcBytesDelta = System.GC.GetTotalMemory(false) - before;
                result.frames = written;
                result.blocks = blocks;
                float op = 0f;
                for (int i = 0; i < written; i++) { float a = result.left[i] < 0 ? -result.left[i] : result.left[i]; if (a > op) op = a; }
                result.peak = op;
                return result;
            }
            finally {
                voice.Dispose();
            }
        }
    }

}
