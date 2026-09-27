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
            created = true;
            handedOff = true;
            return context.AllocateGenerator(voice, new Control { declaredSampleRate = preparedSampleRate });
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

            public Response OnMessage(ControlContext context, Pipe pipe, Message message) => default;
        }
    }
}
