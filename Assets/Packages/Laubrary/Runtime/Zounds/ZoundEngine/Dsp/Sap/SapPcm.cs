using Unity.Collections;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// The Burst-readable form of a <see cref="PcmClip"/>: the same interleaved float samples plus the
    /// scalars the render reads, copied into a native buffer so the audio thread (and later a
    /// Burst-compiled generator) can read them without touching a managed object.
    ///
    /// Every voice that plays a clip makes its OWN copy today, taken when the voice is set up and
    /// released when it is torn down or set up again — deliberately wasteful (per-clip data is
    /// duplicated once per voice playing it), accepted for now because it makes the lifetime trivially
    /// safe: nothing is shared, so nothing can be freed out from under a reader. The later, cheaper design
    /// is one shared native buffer per cached <see cref="PcmClip"/>, kept alive only while at least one
    /// voice is still playing it.
    ///
    /// A registry of playing sounds now exists, and the cache clear asks them all to stop before it frees
    /// anything — but that is still not enough to make sharing safe, and the difference is the whole point:
    /// **asking every sound to stop is not the same as knowing they have stopped.** A stop request reaches a
    /// sound at the start of its next block, so for a moment afterwards sounds are still reading. Sharing turns
    /// that moment from a harmless inconsistency into reading freed memory. Sharing therefore waits on a barrier
    /// that confirms completion, not merely a request — and until that exists, the wasteful copies are the thing
    /// keeping this correct.
    /// </summary>
    public struct SapPcm {
        public NativeArray<float> samples;
        public int channels;
        public int frequency;
        public int frames;
        public float peak;

        public readonly bool IsCreated => samples.IsCreated;

        /// <summary>Copies a managed clip's samples into a native buffer. The source is not retained.</summary>
        public static SapPcm Create(PcmClip clip, Allocator allocator) {
            var src = clip.samples;
            // A zero-length buffer is a valid buffer, but NativeArray rejects a length of zero for some
            // allocators, so an empty source becomes a one-element buffer that nothing reads (every
            // reader is bounded by frames/channels, which describe zero usable samples). Mirrors
            // SapChainLayout.Copy.
            int n = src != null && src.Length > 0 ? src.Length : 1;
            var buf = new NativeArray<float>(n, allocator, NativeArrayOptions.ClearMemory);
            if (src != null && src.Length > 0) buf.CopyFrom(src);
            return new SapPcm {
                samples = buf,
                channels = clip.channels,
                frequency = clip.frequency,
                frames = clip.frames,
                peak = clip.peak,
            };
        }

        public void Dispose() {
            if (samples.IsCreated) samples.Dispose();
        }
    }
}
