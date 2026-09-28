using System.Collections.Generic;
using Unity.Collections;
using Unity.IntegerTime;
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// Plugs one <see cref="SapRealtimeVoice"/> into the audio graph, so a chain renders on the audio thread
    /// as native code instead of through a managed callback.
    ///
    /// **Why this matters, in one line:** a managed audio callback permanently ties the audio mixing thread
    /// to the scripting runtime, so from then on any garbage collection anywhere in the game freezes audio
    /// for as long as the collection takes — measured at hundreds of milliseconds, against tens for the
    /// native path. This component is how the chain gets off that thread-binding entirely.
    ///
    /// **This component is a shell on purpose.** All the substance is in the voice struct, which can be set
    /// up and rendered from an ordinary script. Everything here is the graph's own protocol: hand it the
    /// prepared voice, tell it the output format to expect, give the voice its blocks, release it at the end.
    /// Keeping it this thin is what lets the render be compared against the offline renderer without going
    /// near the graph.
    ///
    /// **How a sound is played:** describe the play with <see cref="SetPlay"/>, then point an audio source's
    /// generator at this component and start it. The description is resolved into native memory at that
    /// point, on the main thread, and nothing managed is touched afterwards.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class ZoundSapVoiceGenerator : MonoBehaviour, IAudioGenerator {

        // The play description, as authored. Kept managed and resolved into native form only when the graph
        // asks for an instance, because the graph may ask more than once and each instance needs its own copy.
        private PcmClip clip;
        private ChainLayout layout;
        private double startFrame, endFrame;
        private float basePitch = 1f, outGain = 1f, sourceDuration;
        private bool loop;
        private long tokenId;
        private bool heavyTier;
        private bool described;

        /// <summary>
        /// The sample rate the voice was prepared for. Taken from the audio output on the main thread, and
        /// declared back to the graph so the two cannot disagree: the chain's buffer lengths are derived from
        /// this rate when the chain is laid out, so a voice prepared for one rate and run at another would be
        /// wrong in a way that is inaudible in a short test and obvious in a long one.
        /// </summary>
        private int preparedSampleRate;

        /// <summary>
        /// The voice this component owns while it still owns it. Once the graph has taken a copy, the copy
        /// shares this voice's native memory, and releasing it is then the graph's job through its own
        /// disposal call — so this component must not also release it. That is what <see cref="handedOff"/>
        /// tracks, and getting it wrong would free memory twice.
        /// </summary>
        private SapRealtimeVoice voice;
        private bool created;
        private bool handedOff;

        /// <summary>
        /// The graph's handle to the running instance. Kept because it is the ONLY way to reach a sound that is
        /// already playing: once the graph has the voice, the copy this component still holds is a dead copy of
        /// the scalars, and writing to it would change nothing audible. Live changes must go through the graph.
        /// </summary>
        private GeneratorInstance instance;
        private bool hasInstance;

        /// <summary>The repeat train to arm, if any. Applied when the voice is created.</summary>
        private RepeatPlan repeat;

        /// <summary>
        /// The block counter every voice this component starts bumps on its way in and out of a block — see the field
        /// of the same purpose on the voice for the even/odd convention.
        ///
        /// **Owned here, not by the voice, and that is the point.** It has to still be readable after the voice has
        /// been released, because the only useful moment to ask "has the audio side finished with that voice" is after
        /// asking it to finish.
        ///
        /// **One per voice, not one per component, and that distinction was learned the hard way.** A single shared
        /// counter cannot answer the question that matters: it keeps moving when ANY voice this component has started
        /// is rendering, so a still-running old voice and a freshly started new one are indistinguishable. The first
        /// version of this shared one counter across plays and produced a reading that looked exactly like "the voice
        /// we just tore down is still being rendered" when the truthful reading may have been "a different voice
        /// started". A barrier that cannot tell those apart is not a barrier.
        /// </summary>
        private NativeArray<long> renderTicket;

        /// <summary>
        /// Counters belonging to voices this component started earlier and has not yet seen go quiet.
        ///
        /// **Why they cannot simply be released when the voice is replaced.** A sound with a long tail can still be
        /// rendering when the pool hands its audio source to the next sound, so the previous voice's counter may still
        /// be being written. Releasing it then is the very use-after-free the counter exists to detect. Instead each
        /// one is kept, with the last value seen beside it, and released on a later pass once it has been observed not
        /// to have moved — which, since passes happen a whole play apart, is a generous margin.
        /// </summary>
        private readonly List<NativeArray<long>> retiredTickets = new List<NativeArray<long>>();
        private readonly List<long> retiredLastSeen = new List<long>();

        /// <summary>
        /// How many recent samples each new voice should keep for a visualiser, or zero for none.
        ///
        /// Off by default and set only while something is actually looking, so a shipped game never pays for it. Unity's
        /// own sample readers cannot see audio produced this way — measured, not assumed — so a visualiser has no other
        /// way to obtain it.
        /// </summary>
        public static int monitorSamples;

        /// <summary>
        /// Which sound this is playing. Remembered so that an edit made in the editor can be delivered to exactly
        /// the voices playing the sound being edited, rather than to everything currently audible.
        /// </summary>
        public Zound playingZound { get; private set; }

        /// <summary>
        /// The layout this voice was started with. An edit arrives as an effect index and a parameter index, and
        /// turning that pair into the single flat position the engine uses requires the layout it was built with --
        /// not the sound's current layout, which may already have been rebuilt by the edit itself.
        /// </summary>
        public ChainLayout playingLayout => layout;

        /// <summary>
        /// When the current play was handed to the audio graph, on the editor's real-time clock.
        ///
        /// **Why a timestamp and not the voice's own sample counter.** The voice does count the samples it has rendered,
        /// but that counter is a plain field of a structure the graph took a COPY of, so the copy this component kept never
        /// sees it move. The native arrays are different — a copy of one is a handle onto the same memory — which is why
        /// live parameter values can be read back and this cannot. A clock started at the same moment is accurate to within
        /// an audio block, which is all a playhead drawn on screen needs.
        /// </summary>
        public double playStartedAt { get; private set; }

        /// <summary>
        /// How much of the current play the engine has actually rendered, in seconds — the play's own clock, exact to the
        /// frame. False when nothing is playing. For displays: it is read without synchronisation.
        /// </summary>
        public bool TryReadRenderedSeconds(out double seconds) {
            seconds = 0d;
            if (!renderTicket.IsCreated || renderTicket.Length < 2 || !IsPlaying || preparedSampleRate <= 0) return false;
            seconds = renderTicket[1] / (double)preparedSampleRate;
            return true;
        }

        /// <summary>The length of the current play, in seconds, as the engine was told it (source length under pitch).</summary>
        public float playDuration => sourceDuration;

        /// <summary>
        /// The value one modifier produced on its most recent evaluation, read from the voice the graph is playing. Same
        /// shared-memory reasoning, and the same caveat, as <see cref="TryReadLiveParam"/>: for a display only.
        /// </summary>
        public bool TryReadLiveModifier(int modifierIndex, out float value) {
            value = 0f;
            if (!created || modifierIndex < 0) return false;
            var m = voice.sap.modValue;
            if (!m.IsCreated || modifierIndex >= m.Length) return false;
            if (!IsPlaying) return false;
            value = m[modifierIndex];
            return true;
        }

        public bool isFinite => false;
        public bool isRealtime => false;
        public DiscreteTime? length => null;

        /// <summary>
        /// True once the current voice has finished — source, stretch and tail — as reported by the audio side itself.
        /// This is how a live-speed sound's end is known (T-0409), since its length cannot be worked out in advance.
        /// </summary>
        public bool VoiceFinished => renderTicket.IsCreated && renderTicket.Length > 2 && renderTicket[2] != 0;

        /// <summary>Whether the current play has live speed (its stretcher runs, and <see cref="SetSpeedLive"/> is heard).</summary>
        public bool HasLiveSpeed => stretch.enabled;

        private SapStretchConfig stretch;
        /// <summary>The sound's own authored speed, and the playing token's multiplier; the global speed multiplies on top.</summary>
        private float authoredSpeed = 1f, tokenSpeed = 1f;
        private float baseSpeed => authoredSpeed * tokenSpeed * ZoundEngine.globalSpeed;

        /// <summary>Describes the next play. Does not allocate; resolution happens when the graph asks.</summary>
        public void SetPlay(PcmClip clip, ChainLayout layout, double startFrame, double endFrame,
                           float basePitch, float outGain, float sourceDuration, bool loop,
                           long tokenId, bool heavyTier, Zound zound = null,
                           SapStretchConfig stretch = default, float authoredSpeed = 1f) {
            this.stretch = stretch;
            this.authoredSpeed = authoredSpeed;
            tokenSpeed = 1f;
            playingZound = zound;
            this.clip = clip;
            this.layout = layout;
            this.startFrame = startFrame;
            this.endFrame = endFrame;
            this.basePitch = basePitch;
            this.outGain = outGain;
            this.sourceDuration = sourceDuration;
            this.loop = loop;
            this.tokenId = tokenId;
            this.heavyTier = heavyTier;
            described = true;
        }

        /// <summary>
        /// True once the render has reported the sound complete. Reading it needs the graph's own value
        /// channel, which is not wired up yet, so today this only reflects a voice this component still owns
        /// — which is the case for an offline or editor-driven render, and not for one the graph is playing.
        /// </summary>
        public bool Finished => created && voice.finished;

        public GeneratorInstance CreateInstance(ControlContext context,
                                                AudioFormat? nestedConfiguration,
                                                CreationParameters creationParameters) {
            ReleaseOwnVoice();

            preparedSampleRate = AudioSettings.outputSampleRate;
            if (!described || layout == null) {
                // Nothing described: hand over a silent voice rather than refusing, so a misconfigured
                // source is an audible nothing with a warning rather than a null reference inside the graph.
                Debug.LogWarning("[Zounds] " + name + ": a SAP voice was started with no play described. It will be silent.");
                layout = ChainLayout.Empty;
            }

            RotateRenderTicket();

            voice = SapRealtimeVoice.Create(clip, layout, preparedSampleRate, startFrame, endFrame,
                                            basePitch, outGain, sourceDuration, loop, tokenId, heavyTier,
                                            Allocator.Persistent, playingZound, monitorSamples, renderTicket,
                                            stretch, baseSpeed);
            if (repeat.enabled) voice.SetRepeat(in repeat);
            created = true;
            handedOff = true;
            instance = context.AllocateGenerator(voice, new Control { declaredSampleRate = preparedSampleRate });
            hasInstance = true;
            playStartedAt = Time.realtimeSinceStartupAsDouble;
            SapVoiceRegistry.Register(this);
            return instance;
        }

        /// <summary>
        /// Whether the graph still has a live instance for this component. Checked against the graph rather than
        /// remembered, because a sound can end without telling this component — the graph disposes it on its own
        /// thread. A false answer also clears the stale handle, so this is cheap to poll.
        /// </summary>
        public bool IsPlaying {
            get {
                if (!hasInstance) return false;
                if (ControlContext.builtIn.Exists(instance)) return true;
                hasInstance = false;
                return false;
            }
        }

        /// <summary>Arms a repeat train for the next play. Has no effect on a sound already started.</summary>
        public void SetRepeat(in RepeatPlan plan) {
            repeat = plan;
        }

        // ───────────────────────── reaching a sound that is already playing ─────────────────────────
        //
        // All of these go through the graph's own value channel rather than writing to this component's copy
        // of the voice, which the graph no longer reads. Each returns false when there is nothing playing to
        // change, so a caller can tell "too late" apart from "done".

        /// <summary>Changes one chain parameter live, by its flat index in the layout. A parameter a modifier
        /// is already driving is ignored at the receiving end — see the note on the receiving function.</summary>
        public bool SetParameterLive(int flatIndex, float value) => Send(SapVoiceCommand.Parameter(flatIndex, value));

        /// <summary>Changes the playing sound's pitch.</summary>
        public bool SetPitchLive(float pitch) => Send(SapVoiceCommand.Pitch(pitch));

        /// <summary>Changes the playing sound's output gain.</summary>
        public bool SetGainLive(float gain) => Send(SapVoiceCommand.Gain(gain));

        /// <summary>The playing sound's own speed changed (an edit). Heard only when the play has live speed (T-0409).</summary>
        public bool SetAuthoredSpeedLive(float speed) { authoredSpeed = speed; return RefreshSpeed(); }

        /// <summary>Game code's speed for this play (the token's), multiplied with the sound's own and the global one.</summary>
        public bool SetTokenSpeedLive(float speed) { tokenSpeed = speed; return RefreshSpeed(); }

        /// <summary>Sends the current combined speed to the playing voice. False when there is nothing to change.</summary>
        public bool RefreshSpeed() => stretch.enabled && Send(SapVoiceCommand.Speed(baseSpeed));

        /// <summary>Stops hard at the next block: declick and flush, without waiting for the tail.</summary>
        public bool StopLive() => Send(SapVoiceCommand.Stop());

        /// <summary>Stops feeding source material and lets the tail ring out naturally.</summary>
        public bool ReleaseLive() => Send(SapVoiceCommand.Release());

        /// <summary>
        /// Tears the sound down through the graph, in the only order that is safe: silence what is pulling on it,
        /// wait until the audio side has been seen to stop, and only then destroy it.
        ///
        /// **This order is not caution, it is the fix for a crash that was reproduced and diagnosed from the engine's
        /// own logs.** Destroying the sound while its audio source was still playing left the mixer pulling on a
        /// processor that no longer existed. It did that once per audio block, complaining each time, for
        /// twenty-seven seconds, and then the engine's own thread-safety validation faulted and took the editor down.
        /// Three things follow from that, and each is a step below:
        ///
        /// - **Silencing has to come first.** Destroying the sound does not stop the audio source carrying it, and an
        ///   audio source that is still playing will keep asking for audio — either from the corpse of what was just
        ///   destroyed, or by starting a fresh sound behind you. Neither is wanted.
        /// - **The destroy has to come last, and only once nothing is reading.** This is the reverse of the obvious
        ///   order and the whole point: a destroy issued while a block is in flight is exactly what crashed.
        /// - **A refusal has to be honoured.** When the audio side cannot be seen to go quiet, this does NOT destroy
        ///   the sound. Leaving it playing wastes a voice; destroying it takes the editor with it.
        ///
        /// Returns false when there was nothing playing. <paramref name="confirmed"/> is false when the sound could not
        /// be seen to stop, in which case nothing was destroyed and nothing it is reading may be freed.
        /// </summary>
        public bool DestroyAndConfirm(out bool confirmed, double settleSeconds = 0d, double timeoutSeconds = 0.5d) {
            confirmed = true;
            if (!hasInstance) return false;
            var control = ControlContext.builtIn;
            if (!control.Exists(instance)) { hasInstance = false; return false; }

            SilenceForTeardown();
            confirmed = SapVoiceRegistry.WaitUntilQuiet(this, settleSeconds, timeoutSeconds);
            if (confirmed) DestroyAfterQuiet();
            else WarnNotQuiet();
            return true;
        }

        /// <summary>
        /// Step one of a teardown: stops the audio source from pulling on the sound. Returns whether there is still an
        /// instance to destroy afterwards.
        ///
        /// Separate from the destroy so that many sounds can be silenced first and then waited for ONCE, instead of
        /// each paying its own settle window. Tearing down twenty sounds one at a time would be twenty waits.
        /// </summary>
        internal bool SilenceForTeardown() {
            if (!hasInstance) return false;
            if (!ControlContext.builtIn.Exists(instance)) { hasInstance = false; return false; }
            var carrier = GetComponent<AudioSource>();
            if (carrier != null && carrier.isPlaying) carrier.Stop();
            return true;
        }

        /// <summary>
        /// The last step of a teardown, to be called ONLY once the sound has been observed to have stopped being
        /// rendered. Calling it before that is the crash this whole sequence exists to avoid.
        /// </summary>
        internal bool DestroyAfterQuiet() {
            if (!hasInstance) return false;
            var control = ControlContext.builtIn;
            if (!control.Exists(instance)) { hasInstance = false; return false; }

            // Flushed first, so a change sent a moment ago cannot still be in flight towards something that is about
            // to stop existing.
            ControlContext.WaitForBuiltInQueueFlush();
            control.Destroy(instance);
            hasInstance = false;

            // The graph disposed the voice as part of destroying it, and this component's copy shares that same
            // memory — so it must NOT be disposed again here. Marking it handed off is what prevents that.
            handedOff = true;
            created = false;
            SapVoiceRegistry.Unregister(this);
            return true;
        }

        /// <summary>Says plainly that a voice is being wasted on purpose, and why that is the better of two bad options.</summary>
        internal void WarnNotQuiet() {
            Debug.LogWarning("[Zounds] " + name + ": a sound would not stop being rendered after its audio source was " +
                             "silenced, so it has deliberately been left alone rather than destroyed. Destroying a " +
                             "sound the mixer is still reading is what crashes the editor, so the voice is being " +
                             "wasted on purpose. Nothing this sound is reading may be freed.");
        }

        /// <summary>
        /// Tears the sound down, without telling the caller whether it worked. Kept for callers that only want the
        /// sound gone and have nothing to free, and it goes through exactly the same safe sequence — there is
        /// deliberately no version of this that destroys a sound without first silencing it and watching it stop,
        /// because that version is what crashed the editor.
        /// </summary>
        public bool DestroyNow() => DestroyAndConfirm(out _);

        /// <summary>
        /// Gives the next voice its own counter, and releases the counters of earlier voices that have been observed to
        /// have stopped. Called when a new voice is created, which is the natural moment: it is at least one whole play
        /// since the last pass, so anything still moving really is still running.
        /// </summary>
        private void RotateRenderTicket() {
            for (int i = retiredTickets.Count - 1; i >= 0; i--) {
                var ticket = retiredTickets[i];
                if (!ticket.IsCreated) { retiredTickets.RemoveAt(i); retiredLastSeen.RemoveAt(i); continue; }
                long now = ticket[0];
                // Unchanged since the last look and not mid-block: that voice is finished with it.
                if (now == retiredLastSeen[i] && (now & 1L) == 0L) {
                    ticket.Dispose();
                    retiredTickets.RemoveAt(i);
                    retiredLastSeen.RemoveAt(i);
                }
                else retiredLastSeen[i] = now;
            }

            if (renderTicket.IsCreated) {
                retiredTickets.Add(renderTicket);
                retiredLastSeen.Add(renderTicket[0]);
            }
            // [0]: the in-block counter the stopped-for-sure barrier reads. [1]: frames rendered, this play's own clock.
            // [2]: set once the voice has finished, tail and all (T-0409: a live-speed sound's end cannot be predicted).
            renderTicket = new NativeArray<long>(3, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        }

        // ───────────────────────── knowing a sound has actually stopped ─────────────────────────
        //
        // Everything above can ASK a sound to stop. None of it can tell you that it HAS. The three members below are
        // the difference, and they exist because that difference is the only thing standing between the engine and
        // sharing one copy of a sound's audio between every voice playing it: sharing is safe exactly when freeing can
        // wait for the readers to finish, and unsafe when it can only ask them to.

        /// <summary>
        /// The raw block counter. Odd means a block is running right now, even means none is. -1 when this component
        /// has never started a voice, so there is nothing to have finished.
        ///
        /// Read without any synchronisation, on purpose and safely: a single value is either odd or even, so no read
        /// can see a state that never existed. The value may of course be out of date by the time it is looked at —
        /// which is why nothing decides anything from ONE reading, only from a reading that has stopped changing.
        /// </summary>
        public long RenderTicket => renderTicket.IsCreated ? renderTicket[0] : -1L;

        /// <summary>True while the audio side is inside a block for this component's voice.</summary>
        public bool RenderInProgress => renderTicket.IsCreated && (renderTicket[0] & 1L) != 0L;

        /// <summary>
        /// Sends one change down the two-hop route the graph provides. There is no single call that reaches the
        /// audio side directly: a message goes to the generator's control half, which forwards it into the
        /// value channel that the audio side drains at the start of each block. The forwarding hop is what puts
        /// the change on the right thread; skipping it and writing the audio state from here would be a data
        /// race.
        ///
        /// Returns false when there is nothing playing any more, so a caller can tell "too late" from "done".
        /// </summary>
        private bool Send(SapVoiceCommand command) {
            if (!hasInstance) return false;
            var control = ControlContext.builtIn;
            if (!control.Exists(instance)) { hasInstance = false; return false; }
            return control.SendMessage(instance, ref command) == Response.Handled;
        }

        /// <summary>
        /// How far the monitor has been written, or -1 when this voice is not being monitored.
        ///
        /// Diagnostic, and a pointed one: if this never advances while the graph claims the sound is playing, then the
        /// graph is not calling the render at all, which is a completely different problem from the render producing
        /// silence. Worth being able to tell those apart without guessing.
        /// </summary>
        public int MonitorWritePosition =>
            created && voice.monitorCursor.IsCreated ? voice.monitorCursor[0] : -1;

        /// <summary>
        /// Copies the most recent output into <paramref name="dest"/>, oldest first, and reports whether there was any.
        /// Returns false when this voice is not being monitored or has produced nothing yet.
        /// </summary>
        public bool ReadMonitor(float[] dest) {
            if (dest == null || !created) return false;
            var ring = voice.monitor;
            if (!ring.IsCreated || !voice.monitorCursor.IsCreated) return false;

            int size = ring.Length;
            int cursor = voice.monitorCursor[0];
            int want = dest.Length;
            // Walk backwards from the write position so the newest sample lands at the end of the destination, which is
            // what both a waveform and a scrolling display expect.
            for (int i = 0; i < want; i++) {
                int idx = cursor - want + i;
                idx %= size;
                if (idx < 0) idx += size;
                dest[i] = ring[idx];
            }
            return true;
        }

        /// <summary>
        /// The value the engine is CURRENTLY using for one flat parameter — after every modifier bound to it has had its
        /// say — or false when there is nothing playing to ask.
        ///
        /// This works for the same reason the monitor does: the graph took a copy of the voice, but a copy's native arrays
        /// are handles onto the same memory, so a value the audio thread writes is visible through the copy this component
        /// kept. It is a read of a float being written by another thread without any synchronisation, which is acceptable
        /// precisely because of what it is for: a display that is redrawn many times a second and where a single frame
        /// reading a half-updated value is invisible and harmless. Do not build anything that must be correct on it.
        ///
        /// Note what this is NOT. It is not the value the user typed, which the editor already has; it is not the value
        /// stored anywhere; and it is only meaningful while the sound is playing, because a modifier's output only exists
        /// while there is a voice evaluating it.
        /// </summary>
        public bool TryReadLiveParam(int flatIndex, out float value) {
            value = 0f;
            if (!created || flatIndex < 0) return false;
            var p = voice.sap.pLive;
            if (!p.IsCreated || flatIndex >= p.Length) return false;
            if (!IsPlaying) return false;
            value = p[flatIndex];
            return true;
        }

        private void OnDestroy() {
            // The sound is silenced and seen to stop FIRST, because everything after this releases memory the audio
            // side may otherwise still be reading. Only a component that actually had something playing pays the wait.
            bool quiet = true;
            if (hasInstance) DestroyAndConfirm(out quiet, timeoutSeconds: 0.25d);
            SapVoiceRegistry.Unregister(this);
            ReleaseOwnVoice();

            // A counter is released only when its voice has been seen to stop. When it has not, it is deliberately
            // LEAKED — eight bytes, reported by the leak detector, against a crash. That is not a close call: freeing
            // this is the one thing guaranteed to fault, because the audio side writes to it on every block.
            if (quiet && renderTicket.IsCreated) renderTicket.Dispose();
            // Each earlier voice is judged on its own counter rather than on the current one's verdict, because an
            // earlier voice can still be ringing out its tail on a source the pool has already moved on from.
            for (int i = 0; i < retiredTickets.Count; i++) {
                var ticket = retiredTickets[i];
                if (!ticket.IsCreated) continue;
                long now = ticket[0];
                if (now == retiredLastSeen[i] && (now & 1L) == 0L) ticket.Dispose();
            }
            retiredTickets.Clear();
            retiredLastSeen.Clear();
        }

        /// <summary>Releases the voice only while this component is still the owner (see <see cref="handedOff"/>).</summary>
        private void ReleaseOwnVoice() {
            if (created && !handedOff) voice.Dispose();
            created = false;
            handedOff = false;
        }

        /// <summary>
        /// The graph's control-side half. It runs where managed code is allowed, but is kept empty of it
        /// anyway: every allocation happens before the instance is created, so there is nothing to do here
        /// but state the output format and release the voice at the end.
        /// </summary>
        private struct Control : GeneratorInstance.IControl<SapRealtimeVoice> {

            public int declaredSampleRate;

            public void Configure(ControlContext context, ref SapRealtimeVoice realtime, in AudioFormat format,
                                  out GeneratorInstance.Setup setup, ref GeneratorInstance.Properties properties) {
                // Declared, not adopted: the voice's chain was laid out for this rate already and cannot be
                // re-laid out here, so the graph is asked to run it at the rate it was prepared for.
                setup = new GeneratorInstance.Setup(speakerMode: AudioSpeakerMode.Stereo,
                                                    sampleRate: declaredSampleRate);
            }

            public void Dispose(ControlContext context, ref SapRealtimeVoice realtime) {
                realtime.Dispose();
            }

            public void Update(ControlContext context, Pipe pipe) { }

            /// <summary>
            /// Receives a live change on the control side and forwards it into the value channel, which the
            /// audio side drains at the start of its next block. This hop exists to get the change onto the
            /// right thread — the control half cannot reach the audio state itself, and that is the point.
            ///
            /// Anything that is not one of our own changes is reported as unhandled rather than swallowed, so
            /// the graph can pass it to whoever it was actually meant for.
            /// </summary>
            public Response OnMessage(ControlContext context, Pipe pipe, Message message) {
                if (!message.Is<SapVoiceCommand>()) return Response.Unhandled;
                var command = message.Get<SapVoiceCommand>();
                pipe.SendData(context, command);
                return Response.Handled;
            }
        }
    }
}
