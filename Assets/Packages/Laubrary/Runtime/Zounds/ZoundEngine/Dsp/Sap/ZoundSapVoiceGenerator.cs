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

        public bool isFinite => false;
        public bool isRealtime => false;
        public DiscreteTime? length => null;

        /// <summary>Describes the next play. Does not allocate; resolution happens when the graph asks.</summary>
        public void SetPlay(PcmClip clip, ChainLayout layout, double startFrame, double endFrame,
                           float basePitch, float outGain, float sourceDuration, bool loop,
                           long tokenId, bool heavyTier, Zound zound = null) {
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

            voice = SapRealtimeVoice.Create(clip, layout, preparedSampleRate, startFrame, endFrame,
                                            basePitch, outGain, sourceDuration, loop, tokenId, heavyTier,
                                            Allocator.Persistent, playingZound, monitorSamples);
            if (repeat.enabled) voice.SetRepeat(in repeat);
            created = true;
            handedOff = true;
            instance = context.AllocateGenerator(voice, new Control { declaredSampleRate = preparedSampleRate });
            hasInstance = true;
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

        /// <summary>Stops hard at the next block: declick and flush, without waiting for the tail.</summary>
        public bool StopLive() => Send(SapVoiceCommand.Stop());

        /// <summary>Stops feeding source material and lets the tail ring out naturally.</summary>
        public bool ReleaseLive() => Send(SapVoiceCommand.Release());

        /// <summary>
        /// Tears the sound down through the graph immediately, rather than asking it to stop and hoping.
        ///
        /// **The difference between this and stopping matters, and it is the reason this exists.** A stop is a
        /// request that reaches the sound at the start of its next block, so for a moment afterwards the sound is
        /// still reading its audio. Tearing down goes through the graph's own destroy call, which is the only
        /// thing on offer that ends with the graph having released the sound's memory rather than merely having
        /// been told to. Anything about to FREE what a sound is reading needs this, not a stop.
        ///
        /// The pending-work flush before it is there so a change sent a moment ago cannot still be in flight
        /// towards something that no longer exists.
        ///
        /// Note what is still unproven: that the graph's destroy call does not return until its audio side has
        /// genuinely let go. It would be a strange design if it did not, and it is the strongest guarantee the
        /// documented surface offers — but it has not been watched happening, which is why sharing one copy of
        /// decoded audio between sounds is still not switched on.
        /// </summary>
        public bool DestroyNow() {
            if (!hasInstance) return false;
            var control = ControlContext.builtIn;
            if (!control.Exists(instance)) { hasInstance = false; return false; }

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
            SapVoiceRegistry.Unregister(this);
            ReleaseOwnVoice();
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
