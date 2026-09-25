using UnityEngine;

namespace Laubrary.Zounds.Dsp.Native {

    /// <summary>
    /// Renders a chain through the NATIVE engine on the calling thread, block by block,
    /// with the same signature as <see cref="ZoundDspOffline"/> renders it through the
    /// managed one. Having both is the point: an effect counts as ported when the same
    /// chain rendered both ways matches sample for sample, which is a far better test
    /// than listening to it, and it is what lets the managed DSP stay in the project as
    /// the reference implementation without the two silently drifting.
    ///
    /// It runs on a reserved node and a reserved bus so it can never disturb, or be
    /// disturbed by, anything playing for real.
    /// </summary>
    public static class ZoundNativeOffline {

        /// <summary>The bus offline renders on. Reserved: the live graph never hands it out.</summary>
        public const int OFFLINE_BUS = ZoundDspConstants.MAX_BUSES - 1;

        /// <summary>
        /// The node offline renders on. The last of the heavy tier, so an offline render
        /// gets the same arena size the managed offline renderer gives itself. Reserved
        /// the same way the bus is.
        /// </summary>
        public const int OFFLINE_NODE = ZoundDspConstants.HEAVY_VOICES - 1;

        private const int SCRATCH_PCM_ID = 1;

        public sealed class Result {
            public float[] left, right;
            public int frames;
            public int sampleRate;
            public float peak;
            public int blocks;
            public string error;
        }

        public static Result Render(float[] interleaved, int channels, int frequency, int sampleRate, ZoundEffectChain chain,
                                    float pitch, float outGain, float seconds, float startSeconds = 0f, float endSeconds = 0f,
                                    int blockFrames = 1024) {
            var result = new Result { sampleRate = sampleRate };
            if (!ZoundsNative.Available && !ZoundsNative.Initialise(sampleRate, blockFrames)) {
                result.error = ZoundsNative.LoadError ?? "the native engine is not available.";
                result.left = new float[0]; result.right = new float[0];
                return result;
            }

            int frames = interleaved.Length / channels;
            float peak = 0f;
            for (int i = 0; i < interleaved.Length; i++) { float a = interleaved[i] < 0 ? -interleaved[i] : interleaved[i]; if (a > peak) peak = a; }
            if (ZoundsNative.Zounds_UploadPcm(SCRATCH_PCM_ID, interleaved, frames, channels, frequency, peak) == 0) {
                result.error = "the native engine refused the PCM upload.";
                result.left = new float[0]; result.right = new float[0];
                return result;
            }

            var layout = chain != null && !chain.IsEmpty ? ChainLayout.Build(chain, sampleRate) : ChainLayout.Empty;
            int layoutId = NativeChainBlob.IdFor(layout);
            if (layoutId == 0) {
                result.error = "the native engine refused the chain layout.";
                result.left = new float[0]; result.right = new float[0];
                return result;
            }

            float clipSeconds = (float)frames / frequency;
            double startFrame = Mathf.Clamp(startSeconds, 0f, clipSeconds) * frequency;
            double endFrame = endSeconds > startSeconds ? Mathf.Min(endSeconds, clipSeconds) * frequency : frames;
            float sourceDuration = (float)((endFrame - startFrame) / frequency) / Mathf.Max(pitch, 0.01f);

            ZoundsNative.Zounds_ForceFree(OFFLINE_NODE);
            var args = new ZoundsNative.PrepareArgs {
                tokenId = 1,
                busIndex = OFFLINE_BUS,
                groupIndex = -1,
                depth = 0,
                pcmId = SCRATCH_PCM_ID,
                layoutId = layoutId,
                isGroup = 0,
                startFrame = startFrame,
                endFrame = endFrame,
                basePitch = pitch,
                outGain = outGain,
                sourceDuration = sourceDuration,
                loop = 0,
            };
            if (ZoundsNative.Zounds_PrepareNode(OFFLINE_NODE, ref args) == 0) {
                result.error = "the native engine refused to prepare the offline node (the chain may need more state than the arena holds).";
                result.left = new float[0]; result.right = new float[0];
                return result;
            }
            ZoundsNative.Zounds_PublishNode(OFFLINE_NODE);

            int totalFrames = Mathf.CeilToInt(seconds * sampleRate);
            result.left = new float[totalFrames];
            result.right = new float[totalFrames];
            var block = new float[blockFrames * 2];
            int written = 0, blocks = 0;
            unsafe {
                var node = ZoundsNative.Node(OFFLINE_NODE);
                while (written < totalFrames && node->state != (int)VoiceState.Free) {
                    int n = Mathf.Min(blockFrames, totalFrames - written);
                    System.Array.Clear(block, 0, n * 2);
                    ZoundsNative.Zounds_RenderOffline(OFFLINE_BUS, block, n, 2);
                    for (int i = 0; i < n; i++) { result.left[written + i] = block[i * 2]; result.right[written + i] = block[i * 2 + 1]; }
                    written += n;
                    blocks++;
                }
            }
            // The Audio End this render pushed belongs to nobody. It is deliberately left
            // in the ring rather than drained here, because draining would also swallow
            // live voices' events: the graph's own drain discards anything from
            // OFFLINE_NODE instead.
            ZoundsNative.Zounds_ForceFree(OFFLINE_NODE);

            result.frames = written;
            result.blocks = blocks;
            float op = 0f;
            for (int i = 0; i < written; i++) { float a = result.left[i] < 0 ? -result.left[i] : result.left[i]; if (a > op) op = a; }
            result.peak = op;
            return result;
        }
    }
}
