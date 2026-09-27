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

        /// <summary>
        /// A rolling copy of recent output, for a visualiser to read. Not created unless somebody asked for it.
        ///
        /// **Why the engine has to provide this at all.** Unity's own facilities for reading a playing sound's samples
        /// were tried first and return silence for audio produced this way — both per-source and at the final mix — because
        /// they tap the ordinary clip-playback route that this deliberately bypasses. So a visualiser cannot see anything
        /// unless the engine hands it the samples.
        ///
        /// **It costs nothing when unused.** The buffer is only allocated when monitoring was requested, and the per-block
        /// cost otherwise is a single check that it does not exist. A visualiser that slowed down the thing it visualises
        /// would be a poor trade, so the default is off.
        /// </summary>
        public NativeArray<float> monitor;

        /// <summary>
        /// Where the next monitor sample goes. A single-element buffer rather than a plain number on purpose: this struct
        /// is copied when the audio graph takes it, so an ordinary field written on the audio side would never be seen by
        /// the reader. A native buffer is shared by handle, so both sides see the same one.
        /// </summary>
        public NativeArray<int> monitorCursor;

        /// <summary>
        /// A counter bumped once when a block starts and once when it ends, so a reader can tell not only HOW MANY
        /// blocks have happened but whether one is happening right now: an odd value means the audio side is inside
        /// a block, an even value means it is between blocks.
        ///
        /// **This is deliberately NOT owned by the voice, and that is the entire reason it exists separately from the
        /// block count already on this struct.** The question it answers is "has the audio side finished with this
        /// voice's memory", and every other piece of bookkeeping lives in that same memory — so reading it after the
        /// voice has been released would be reading exactly the thing whose safety is in doubt. This counter is
        /// allocated by whoever hosts the voice, outlives the voice on purpose, and is never disposed here.
        ///
        /// **Why one counter and not a count plus a busy flag.** Two separate values can be seen half-updated, and the
        /// half that arrives first decides whether a reader believes the wrong thing. One counter with the even/odd
        /// convention cannot disagree with itself: any single value read is either clearly mid-block or clearly
        /// between blocks, and a value that is both even and unchanged over a whole block's worth of time means no
        /// block has begun in that time.
        ///
        /// Costs two additions per block when present and one check when absent, so it is affordable to leave on
        /// permanently — which matters, because a barrier nobody switched on is not a barrier.
        /// </summary>
        public NativeArray<long> renderTicket;

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
                                              bool loop, long tokenId, bool heavyTier, Allocator allocator,
                                              Zound zound = null, int monitorSamples = 0,
                                              NativeArray<long> renderTicket = default) {
            int arenaFloats = heavyTier ? ZoundDspConstants.HEAVY_ARENA_FLOATS : ZoundDspConstants.LIGHT_ARENA_FLOATS;

            var v = new SapRealtimeVoice {
                sap = SapVoiceState.Create(arenaFloats, ChainLayout.MAX_PARAMS, ZoundDspConstants.MAX_SOURCE_SLOTS,
                                           ZoundDspConstants.MAX_MODIFIERS, ZoundDspConstants.MAX_DSP_BUFFER, allocator),
                sampleRate = sampleRate,
                sourceDuration = sourceDuration,
                basePitch = basePitch,
                outGain = outGain,
                clipRate = clip != null ? (double)clip.frequency / sampleRate : 1.0,
                // Borrowed, not created here: it belongs to the host so that it survives this voice.
                renderTicket = renderTicket,
            };

            if (monitorSamples > 0) {
                v.monitor = new NativeArray<float>(monitorSamples, allocator, NativeArrayOptions.ClearMemory);
                v.monitorCursor = new NativeArray<int>(1, allocator, NativeArrayOptions.ClearMemory);
            }

            SapVoiceSetup.BuildSnapshots(ref v.chain, ref v.pcm, layout, clip, allocator);
            SapVoiceSetup.Reset(ref v.sap, in v.chain, layout, sampleRate, basePitch, outGain, tokenId,
                                armSource: true, startFrame, endFrame, loop, zound);
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

        /// <summary>
        /// Arms a repeat train: the sound replaying itself a number of times, optionally re-rolling its pitch
        /// and loudness each time. Call after creation and before the first block. The per-block machinery that
        /// executes the train already exists in the shared render — this only sets its opening state, through
        /// the same shared function the long-lived voice uses.
        /// </summary>
        public void SetRepeat(in RepeatPlan plan) {
            SapVoiceSetup.ArmRepeats(ref sap, in plan, out bool protect);
            if (plan.enabled) protectedFromSteal = protect;
        }

        /// <summary>
        /// Applies one live change. Safe to call on whichever thread the change arrives on, including from
        /// inside compiled code on the audio thread, because it only writes native memory this voice owns.
        /// </summary>
        public void Apply(in SapVoiceCommand command) {
            switch (command.kind) {
                case SapVoiceCommandKind.SetParameter:
                    SapVoiceRender.ApplyLiveParam(ref sap, in chain, command.index, command.value);
                    break;
                case SapVoiceCommandKind.SetPitch:
                    basePitch = command.value;
                    break;
                case SapVoiceCommandKind.SetGain:
                    outGain = command.value;
                    break;
                case SapVoiceCommandKind.Stop:
                    killRequested = 1;
                    break;
                case SapVoiceCommandKind.Release:
                    releaseRequested = 1;
                    break;
            }
        }

        public void Dispose() {
            sap.Dispose();
            if (chain.IsCreated) chain.Dispose();
            if (pcm.IsCreated) pcm.Dispose();
            if (monitor.IsCreated) monitor.Dispose();
            if (monitorCursor.IsCreated) monitorCursor.Dispose();
            // renderTicket is deliberately NOT released here. Its whole purpose is to be readable AFTER this voice is
            // gone, so that whoever wants to free what the voice was reading can first confirm it has stopped reading.
            // Freeing it here would destroy the only evidence available at exactly the moment it is needed.
        }

        // ───────────────────────── the audio graph's own entry points ─────────────────────────

        /// <summary>
        /// Drains whatever live changes arrived since the last block and applies them in the order they were
        /// sent. The graph calls this on the audio side, so this is where a change actually reaches a playing
        /// sound — nothing else may write to this voice from another thread.
        ///
        /// Every change in the batch is applied, not just the newest, because the kinds are not all
        /// idempotent: a stop and a release mean different things and both matter, and a caller streaming a
        /// value expects the last one it sent to win, which it does by arriving last.
        /// </summary>
        public void Update(UpdatedDataContext context, Pipe pipe) {
            foreach (var element in pipe.GetAvailableData(context)) {
                if (element.TryGetData(out SapVoiceCommand command)) Apply(in command);
            }
        }

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
            // Opened before anything is touched and closed after everything is, so that the window the counter
            // describes is wider than the window in which this voice's memory is actually in use, never narrower.
            // Erring wide only makes a waiting barrier wait slightly longer; erring narrow would let it conclude
            // "finished" while a block was still running, which is the one answer that must never be wrong.
            bool ticketed = renderTicket.IsCreated;
            if (ticketed) renderTicket[0] = renderTicket[0] + 1;   // now odd: inside a block
            // Counts this block, and separately counts it again only if it is running as managed code (T-0448).
            ZoundAudioThreadGuard.CountBlock();

            int frames = buffer.frameCount;
            if (frames > sap.bufL.Length) frames = sap.bufL.Length;

            bool produced = RenderBlock(frames);
            WriteTo(buffer, frames, produced);
            if (monitor.IsCreated && produced) CopyToMonitor(frames);

            // The second slot counts frames rendered: this play's own clock, readable from the main thread, which is what
            // the editor's displays follow a playing sound by. The wall clock is not the same thing — audio starts a moment
            // after the voice is created and is produced in blocks — and following it put the analyser out of step (T-0443).
            if (ticketed && renderTicket.Length > 1) renderTicket[1] = renderTicket[1] + frames;
            if (ticketed) renderTicket[0] = renderTicket[0] + 1;   // now even: between blocks
            return buffer.frameCount;
        }

        /// <summary>
        /// Appends this block to the rolling monitor copy, oldest overwritten. One channel only, because a visualiser
        /// showing a spectrum or a waveform gains nothing from the second and it would double the cost.
        /// </summary>
        private void CopyToMonitor(int frames) {
            int size = monitor.Length;
            if (size <= 0) return;
            int cursor = monitorCursor[0];
            for (int i = 0; i < frames; i++) {
                monitor[cursor] = sap.bufL[i];
                cursor++;
                if (cursor >= size) cursor = 0;
            }
            monitorCursor[0] = cursor;
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
