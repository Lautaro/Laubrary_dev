using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// Renders a voice to completion as compiled native code, writing the result into two buffers the caller
    /// owns. Running it executes on the calling thread, so it is a plain synchronous call from the caller's
    /// point of view.
    ///
    /// **This is the only compiled entry point into the render that ordinary code can reach, and that is
    /// deliberate.** The compiler will not build a bridge for a compiled function whose parameter is a
    /// mutable reference to a structure, and hand-building one would mean raw pointers into managed memory.
    /// A job needs no bridge. It also renders every block in one call, so none of the voice's running state
    /// has to survive a round trip back into uncompiled code — which matters because a job's own fields are
    /// passed by value and changes to them do not come back.
    ///
    /// **It must live in a real assembly, not in a script compiled on the fly.** The compiler collects its
    /// entry points when an assembly is built; a job type defined in an assembly created at runtime is not
    /// among them, and the job then silently runs as ordinary code instead. That failure is invisible in the
    /// output — the numbers come out the same, because it is the same function — so anything claiming to
    /// compare compiled against uncompiled output has to confirm from the compiler's own reporting that
    /// compilation actually happened. This was found the hard way: the first comparison "passed" while
    /// running uncompiled on both sides.
    ///
    /// Beyond verification this is also what an offline bounce or a preview render wants: the real render, at
    /// full speed, off the audio thread.
    /// </summary>
    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict)]
    public struct SapVoiceRenderJob : IJob {

        /// <summary>The voice to render. Its buffers are shared, so its output is visible to the caller; its
        /// scalar state is not written back, which is why the job renders to completion in one call.</summary>
        public SapRealtimeVoice voice;

        public NativeArray<float> outLeft;
        public NativeArray<float> outRight;

        /// <summary>How many output frames to produce at most; the voice may finish sooner.</summary>
        public int totalFrames;
        /// <summary>Frames per block, matching whatever block size is being compared against.</summary>
        public int blockFrames;

        /// <summary>Results, because a job cannot return anything: [0] frames written, [1] blocks rendered.</summary>
        public NativeArray<int> tally;

        public void Execute() {
            int written = 0;
            int blocks = 0;
            while (written < totalFrames && !voice.finished) {
                int n = blockFrames < totalFrames - written ? blockFrames : totalFrames - written;
                voice.RenderBlock(n);
                blocks++;
                for (int i = 0; i < n; i++) {
                    outLeft[written + i] = voice.sap.bufL[i];
                    outRight[written + i] = voice.sap.bufR[i];
                }
                written += n;
            }
            tally[0] = written;
            tally[1] = blocks;
        }
    }
}
