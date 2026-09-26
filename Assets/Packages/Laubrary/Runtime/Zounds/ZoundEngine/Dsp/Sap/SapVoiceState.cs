using Unity.Collections;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// The Burst-compatible equivalent of everything <see cref="DspVoice"/> holds in plain arrays. One
    /// instance is owned exclusively by one generator instance's real-time struct for the life of one
    /// play — there is no cross-voice sharing and no cross-thread flag traffic, unlike the native voice
    /// pool this replaces: SAP already gives each play its own isolated instance, so the pooling problem
    /// native code had to solve by hand (a fixed arena, manual recycle-by-index, Volatile handshakes)
    /// mostly does not exist here. What a chain author edits live (a slider drag) still has to cross from
    /// the main thread to here through a message, same as any other live SAP parameter change.
    ///
    /// Allocated once when a play starts (sized to the chain's real per-effect state need, not a fixed
    /// worst-case arena) and disposed when the voice is freed.
    /// </summary>
    public struct SapVoiceState {
        public NativeArray<float> arena;      // per-effect state (filter history, delay lines, etc.)
        public NativeArray<float> pLive;      // current value of every flat parameter
        public NativeArray<float> pStart;     // value at the start of the current control block
        public NativeArray<float> pStep;      // per-sample step to reach the block's target
        public NativeArray<float> pTarget;    // this block's target (only used when a modifier binds the param)
        public NativeArray<SourceSlot> slots; // read cursors over the source PCM (repeats use more than one)
        public NativeArray<float> modValue;   // scratch: this block's evaluated value for each modifier

        public static SapVoiceState Create(int stateFloats, int paramCount, int sourceSlots, int modifierCount, Allocator allocator) {
            return new SapVoiceState {
                arena = new NativeArray<float>(stateFloats, allocator, NativeArrayOptions.ClearMemory),
                pLive = new NativeArray<float>(paramCount, allocator, NativeArrayOptions.ClearMemory),
                pStart = new NativeArray<float>(paramCount, allocator, NativeArrayOptions.ClearMemory),
                pStep = new NativeArray<float>(paramCount, allocator, NativeArrayOptions.ClearMemory),
                pTarget = new NativeArray<float>(paramCount, allocator, NativeArrayOptions.ClearMemory),
                slots = new NativeArray<SourceSlot>(sourceSlots, allocator, NativeArrayOptions.ClearMemory),
                modValue = new NativeArray<float>(modifierCount, allocator, NativeArrayOptions.ClearMemory),
            };
        }

        public void Dispose() {
            if (arena.IsCreated) arena.Dispose();
            if (pLive.IsCreated) pLive.Dispose();
            if (pStart.IsCreated) pStart.Dispose();
            if (pStep.IsCreated) pStep.Dispose();
            if (pTarget.IsCreated) pTarget.Dispose();
            if (slots.IsCreated) slots.Dispose();
            if (modValue.IsCreated) modValue.Dispose();
        }
    }
}
