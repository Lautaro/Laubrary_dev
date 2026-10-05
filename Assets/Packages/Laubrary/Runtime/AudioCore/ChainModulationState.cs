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
        public bool sourceExhausted, followSource;
    }
}
