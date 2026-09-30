using Unity.Collections;

namespace Laubrary.Audio {
    /// <summary>Borrowed buffers for the shared evaluator. The host owns every allocation and retains the returned RNG.</summary>
    public struct ChainModulationState {
        public NativeArray<float> arena, modValue, modCtlLive, modCtlTarget;
        public uint rng, curveSeed;
    }

    /// <summary>Values at the end of the control block. Source progress is supplied by the host's source reader.</summary>
    public struct ModulationContext {
        public float elapsedSeconds, sourceDuration, sourceProgress;
        /// <summary>For curves anchored to the source file (T-0501): the whole file's length and where the read head is within it, both in source seconds. A file length of zero means the host has no source to anchor to.</summary>
        public float sourceFileSeconds, sourceAtSeconds;
        public bool sourceExhausted, followSource;
    }
}
