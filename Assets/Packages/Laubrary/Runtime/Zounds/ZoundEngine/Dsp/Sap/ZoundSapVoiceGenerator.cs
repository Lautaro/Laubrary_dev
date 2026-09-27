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

        public bool isFinite => false;
        public bool isRealtime => false;
        public DiscreteTime? length => null;

        /// <summary>Describes the next play. Does not allocate; resolution happens when the graph asks.</summary>
        public void SetPlay(PcmClip clip, ChainLayout layout, double startFrame, double endFrame,
                           float basePitch, float outGain, float sourceDuration, bool loop,
                           long tokenId, bool heavyTier) {
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
                                            Allocator.Persistent);
            if (repeat.enabled) voice.SetRepeat(in repeat);
            created = true;
            handedOff = true;
            instance = context.AllocateGenerator(voice, new Control { declaredSampleRate = preparedSampleRate });
            hasInstance = true;
            return instance;
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

        private void OnDestroy() {
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
