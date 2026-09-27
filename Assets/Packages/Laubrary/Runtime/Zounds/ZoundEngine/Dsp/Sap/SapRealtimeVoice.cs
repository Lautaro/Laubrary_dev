using Unity.Burst;
using Unity.Collections;
using Unity.IntegerTime;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// One playing sound, as a value type with no managed references at all: its chain, its source samples,
    /// its per-effect state and its output buffers are all native memory it owns, and its per-block work is
    /// the same shared render function the offline renderer drives. This is the form the audio graph can run
    /// directly, and the form the compiler can translate to native code — neither of which is possible for
    /// an ordinary managed object.
    ///
    /// **Why this is a plain struct with plain methods, and not just the generator's inner workings.**
    /// Keeping it separate from the component that plugs it into the audio graph means the whole of it can
    /// be set up and rendered from an ordinary editor script: allocate it, ask it for blocks, compare the
    /// result against the offline renderer sample for sample. Driving the audio graph's own setup machinery
    /// by hand, by contrast, was tried and ended in a hard editor crash rather than an exception, so the
    /// verification path deliberately does not go anywhere near it.
    ///
    /// **What it deliberately does NOT do yet.** There is no live control surface: nothing can change its
    /// pitch, its gain or a chain parameter once it has started, and nothing can ask it to stop or to
    /// release into its tail. Those all belong to the graph's own message and value channels, which is a
    /// step of its own — the fields for them are here and readable, so wiring them up later does not change
    /// this shape. It also does not do the onset bookkeeping the long-lived voice object does, because that
    /// is diagnostics kept in a managed array and has no place in native per-block work.
    /// </summary>
    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict)]
    public struct SapRealtimeVoice : GeneratorInstance.IRealtime {

        /// <summary>Buffers, per-effect state and every per-block scalar.</summary>
        public SapVoiceState sap;
        /// <summary>This play's private copy of the resolved chain. Owned; disposed with the voice.</summary>
        public SapChainLayout chain;
        /// <summary>This play's private copy of the source samples. Owned; disposed with the voice.</summary>
        public SapPcm pcm;

        public int sampleRate;
        /// <summary>Source frames consumed per output frame at unity pitch.</summary>
        public double clipRate;
        /// <summary>Resolved play length of the source material, in seconds.</summary>
        public float sourceDuration;

        /// <summary>Target pitch and output gain. Constant for now; see the note above.</summary>
        public float basePitch;
        public float outGain;

        /// <summary>Set while a repeat train still has armed repeats, so a voice stealer leaves it alone.</summary>
        public bool protectedFromSteal;

        /// <summary>Stop hard at the next block (declick and flush), or stop feeding and let the tail ring.</summary>
        public int killRequested;
        public int releaseRequested;

        /// <summary>True once the render has reported the sound complete. Blocks after that are silence.</summary>
        public bool finished;
        /// <summary>
        /// The last state change the render reported, as the underlying number (0 none, 1 stopping, 2 became
        /// tailing). Held as a number rather than the named type because that type is internal to the
        /// package and this struct is not. Whatever owns this voice publishes it onward; the render itself
        /// cannot, because publishing safely across threads is not something native code compiled from this
        /// path is able to do.
        /// </summary>
        public int lastTransition;

        /// <summary>How many blocks have been rendered. Diagnostics only; nothing reads it to make a decision.</summary>
        public int blocksRendered;

        // The audio graph asks these three of every generator. A sound of unknown final length (a tail can
        // outlast the source, and a repeat train can extend it) answers "not finite, no length", the same
        // answer the working reference implementation gives.
        public bool isFinite => false;
        public bool isRealtime => false;
        public DiscreteTime? length => null;

        /// <summary>
        /// Allocates and prepares a voice for one play. Main thread only: it allocates, it reads managed
        /// objects, and it copies them into native form.
        ///
        /// <paramref name="heavyTier"/> selects the same fixed per-effect state budget the long-lived voice
        /// object uses. Sizing the budget to what the chain actually needs would be smaller and is the
        /// eventual intent, but it is a separate change with its own verification: this one's job is to prove
        /// the generator renders identically, so it deliberately keeps the allocation shape identical too and
        /// changes only the host.
        /// </summary>
        public static SapRealtimeVoice Create(PcmClip clip, ChainLayout layout, int sampleRate,
                                              double startFrame, double endFrame,
                                              float basePitch, float outGain, float sourceDuration,
                                              bool loop, long tokenId, bool heavyTier, Allocator allocator) {
            int arenaFloats = heavyTier ? ZoundDspConstants.HEAVY_ARENA_FLOATS : ZoundDspConstants.LIGHT_ARENA_FLOATS;

            var v = new SapRealtimeVoice {
                sap = SapVoiceState.Create(arenaFloats, ChainLayout.MAX_PARAMS, ZoundDspConstants.MAX_SOURCE_SLOTS,
                                           ZoundDspConstants.MAX_MODIFIERS, ZoundDspConstants.MAX_DSP_BUFFER, allocator),
                sampleRate = sampleRate,
                sourceDuration = sourceDuration,
                basePitch = basePitch,
                outGain = outGain,
                clipRate = clip != null ? (double)clip.frequency / sampleRate : 1.0,
            };

            SapVoiceSetup.BuildSnapshots(ref v.chain, ref v.pcm, layout, clip, allocator);
            SapVoiceSetup.Reset(ref v.sap, in v.chain, layout, sampleRate, basePitch, outGain, tokenId,
                                armSource: true, startFrame, endFrame, loop);
            return v;
        }

        /// <summary>
        /// Renders one block into the voice's own output buffers. Returns false when the voice produced
        /// nothing, either because it had already finished or because the render declined the block.
        ///
        /// This is the seam the audio graph's per-block call and the offline comparison both go through, so
        /// that there is no version of the per-block work that only one of them exercises.
        /// </summary>
        public bool RenderBlock(int frames) {
            if (finished) return false;

            bool voiceFinished = SapVoiceRender.Render(
                ref sap, in chain, frames, sampleRate, in pcm,
                isGroup: false, clipRate, sourceDuration, liveChildren: 0,
                killRequested != 0, releaseRequested != 0, basePitch, outGain,
                ref protectedFromSteal, out VoiceStateTransition transition);

            lastTransition = (int)transition;
            blocksRendered++;
            if (voiceFinished) finished = true;
            return true;
        }

        public void Dispose() {
            sap.Dispose();
            if (chain.IsCreated) chain.Dispose();
            if (pcm.IsCreated) pcm.Dispose();
        }

        // ───────────────────────── the audio graph's own entry points ─────────────────────────

        public void Update(UpdatedDataContext context, Pipe pipe) { }

        /// <summary>
        /// The graph's per-block call. Renders one block and writes it out, or writes silence once the sound
        /// has finished.
        ///
        /// It keeps answering with full blocks of silence after the end rather than reporting the stream as
        /// over, because how this graph wants a finished generator to announce itself has not been confirmed
        /// by observation, and guessing wrong here is the difference between a clean stop and a stuck or
        /// spinning voice. Whatever owns the voice can see that it has finished and shut it down; making the
        /// generator itself say so belongs with the rest of the control-channel work.
        /// </summary>
        public GeneratorInstance.Result Process(in RealtimeContext context, Pipe pipe,
                                                ChannelBuffer buffer, GeneratorInstance.Arguments args) {
            int frames = buffer.frameCount;
            if (frames > sap.bufL.Length) frames = sap.bufL.Length;

            bool produced = RenderBlock(frames);
            WriteTo(buffer, frames, produced);
            return buffer.frameCount;
        }

        /// <summary>
        /// Copies the rendered block into the graph's buffer, mapping the voice's two channels onto however
        /// many the output has: one channel gets the average of the two, two get them as they are, and any
        /// channel beyond the second is left silent rather than fed a duplicate — a surround output should
        /// not have the sound arriving from behind the listener as well as in front.
        /// </summary>
        private void WriteTo(ChannelBuffer buffer, int frames, bool produced) {
            int channels = buffer.channelCount;
            int total = buffer.frameCount;

            for (int frame = 0; frame < total; frame++) {
                bool have = produced && frame < frames;
                float l = have ? sap.bufL[frame] : 0f;
                float r = have ? sap.bufR[frame] : 0f;

                if (channels == 1) {
                    buffer[0, frame] = (l + r) * 0.5f;
                    continue;
                }
                buffer[0, frame] = l;
                buffer[1, frame] = r;
                for (int ch = 2; ch < channels; ch++) buffer[ch, frame] = 0f;
            }
        }
    }
}
