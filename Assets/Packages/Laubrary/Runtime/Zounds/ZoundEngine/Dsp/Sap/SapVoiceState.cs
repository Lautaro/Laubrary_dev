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
    ///
    /// Besides the buffers, this also holds every per-block render scalar that used to live directly on
    /// <see cref="DspVoice"/>: the live pitch/gain, source-exhaustion and tail bookkeeping, the RNG, and
    /// the repeat schedule's own counters. Everything here is touched only by whichever thread is
    /// currently rendering this voice's blocks (today that is always the audio thread once a voice is
    /// published) — the genuinely cross-thread control surface (pause/kill/release requests, the pitch
    /// and gain *targets*, and the published <c>VoiceState</c>) deliberately stays on <see cref="DspVoice"/>
    /// itself and is passed into the render functions as parameters/by-ref, unchanged from before.
    /// </summary>
    /// <summary>
    /// A looping voice's state (the Looper, T-0474). Two read slots of the same voice take turns: the "main" one plays
    /// the loop region; when it comes within the crossmix length of the region's end, the other slot starts from the
    /// region's start and the two are cross-faded until the main one reaches the end, where the other becomes main.
    /// With no crossmix the main slot simply wraps. Everything is sample-accurate and happens inside one voice.
    ///
    /// Lengths are in SOURCE frames: the outgoing copy reads [end - X, end] while the incoming copy reads
    /// [start, start + X], both advancing at the same rate, so they finish together at any pitch.
    /// </summary>
    public struct SapLoopState {
        /// <summary>This voice loops (the loop reader is in charge of slots 0 and 1).</summary>
        public bool enabled;
        /// <summary>The crossmix range, in source frames. Both 0 = a plain wrap; equal = a fixed length.</summary>
        public double crossMin, crossMax;
        /// <summary>Keys the per-cycle draw together with the cycle number (from the play's token).</summary>
        public uint seed;
        /// <summary>How many times the loop has begun again; each cycle draws its own outgoing crossmix length.</summary>
        public int cycle;
        /// <summary>The current main copy's outgoing crossmix length (frames), drawn when that copy started.</summary>
        public double outLen;
        /// <summary>A crossfade is running: the other slot is the incoming copy.</summary>
        public bool inFade;
        /// <summary>The running crossfade's length (frames).</summary>
        public double fadeLen;
        /// <summary>Which of slots 0 and 1 is the main copy.</summary>
        public int mainSlot;
        /// <summary>How alike the running crossfade's two regions are (their normalised correlation, -0.5..1), measured
        /// when the fade started; it decides the gain law (see SapVoiceRender.LoopFadeGains).</summary>
        public float fadeCorr;
    }

    public struct SapVoiceState {
        public NativeArray<float> arena;      // per-effect state (filter history, delay lines, etc.)
        public NativeArray<float> pLive;      // current value of every flat parameter
        public NativeArray<float> pStart;     // value at the start of the current control block
        public NativeArray<float> pStep;      // per-sample step to reach the block's target
        public NativeArray<float> pTarget;    // this block's target (only used when a modifier binds the param)
        public NativeArray<SourceSlot> slots; // read cursors over the source PCM (repeats use more than one)
        public NativeArray<float> modValue;   // scratch: this block's evaluated value for each modifier
        public NativeArray<float> modCtlTarget; // ZPOC: the control value last sent by the main thread, per modifier
        public NativeArray<float> modCtlLive;   // ZPOC: the eased control value the render uses (and a display reads)
        public NativeArray<float> bufL;       // rendered output, left
        public NativeArray<float> bufR;       // rendered output, right

        // ── per-block render scalars (moved from DspVoice) ──
        public float basePitchLive;
        public float outGainLive;
        /// <summary>
        /// The sound's fixed boost into its effects (T-0521): the source is multiplied by it right after it is read, so
        /// it multiplies Drive whatever moves Drive. Ramped from <see cref="boostLive"/> to <see cref="boostTarget"/> over
        /// one control block, so a change while playing never steps.
        /// </summary>
        public float boostLive, boostTarget, ctlBoostStep;
        /// <summary>Live base speed (T-0409), ramped like the base pitch. Only read when <see cref="stretch"/> is on.</summary>
        public float baseSpeedLive;
        /// <summary>The live time-stretcher, when this voice has live speed; otherwise not created and never read.</summary>
        public SapStretch stretch;
        /// <summary>One control block of a slot's stretched output, before its gain is applied.</summary>
        [Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction] public NativeArray<float> stretchScratchL, stretchScratchR;
        public bool sourceExhausted;
        public long elapsedSamples;
        /// <summary>The current control block's per-sample slopes for the base pitch, output gain and base speed, kept so
        /// a block split across two render calls carries on with the same ramps (T-0479).</summary>
        public float ctlBasePitchStep, ctlOutGainStep, ctlBaseSpeedStep;
        /// <summary>This play's seed for random envelope points (T-0483): set when the play starts, from its token.</summary>
        public uint curveSeed;
        public long samplesSinceSourceEnd;
        public int silentSamples;
        public long tailBudgetSamples;
        public int hangoverSamples;
        public float lastPeak;
        public bool stopping;
        public bool released;
        public uint rng;

        // ── repeat schedule + its counters (moved from DspVoice) ──
        public RepeatPlan repeat;
        public int repeatsPending;
        public int repeatsDone;
        public int repeatsTotal;
        public long nextRepeatSample;
        public long lastRepeatEndSample;
        /// <summary>Output sample (voice time) at which the last armed repeat ends. Written as a plain
        /// value (the volatile-write helper is unavailable to Burst) and read by the main thread through
        /// a volatile read (see DspVoice.trainEndSample); see SapVoiceRender.ProjectTrainEnd for why that
        /// is safe today and what still needs to happen before it is safe in general.</summary>
        public long trainEndSample;
        public int slotsStolen;

        /// <summary>The Looper's state (T-0474): whether this voice loops its region, and its crossmix.</summary>
        public SapLoopState looping;

        // ── snapshot glide (T-0498) ──
        // A glide moves this voice's own copy of its settings from where they were when it began to a snapshot's values,
        // over a number of samples, advanced once per control block. Per value: where it started, where it is going, and
        // how (0 not gliding; 1 glide, 2 switch at the midpoint -- each +2 when a modifier also moves it, so its resting
        // value is what moves rather than its live value).
        public NativeArray<float> gpFrom, gpTo;   public NativeArray<byte> gpKind;   // effect / own-value parameters (flat)
        public NativeArray<float> gmFrom, gmTo;   public NativeArray<byte> gmKind;   // modifier parameters (flat)
        public NativeArray<float> gbFrom, gbTo;   public NativeArray<byte> gbKind;   // binding depths
        public NativeArray<float> gnFrom, gnTo;   public NativeArray<byte> gnKind;   // effect presence
        /// <summary>How much of each effect is heard (1 on, 0 off, between while a glide fades it in or out), and its value
        /// at the previous block, so the change is ramped across the block instead of stepped.</summary>
        public NativeArray<float> presence, presencePrev;
        /// <summary>One control block of the signal going into an effect that is fading in or out.</summary>
        public NativeArray<float> dryL, dryR;
        public int glideTotal, glideDone;
        public bool glideActive, glideSettled;

        public static SapVoiceState Create(int stateFloats, int paramCount, int sourceSlots, int modifierCount, int outputBufferFrames, Allocator allocator) {
            return new SapVoiceState {
                arena = new NativeArray<float>(stateFloats, allocator, NativeArrayOptions.ClearMemory),
                pLive = new NativeArray<float>(paramCount, allocator, NativeArrayOptions.ClearMemory),
                pStart = new NativeArray<float>(paramCount, allocator, NativeArrayOptions.ClearMemory),
                pStep = new NativeArray<float>(paramCount, allocator, NativeArrayOptions.ClearMemory),
                pTarget = new NativeArray<float>(paramCount, allocator, NativeArrayOptions.ClearMemory),
                slots = new NativeArray<SourceSlot>(sourceSlots, allocator, NativeArrayOptions.ClearMemory),
                modValue = new NativeArray<float>(modifierCount, allocator, NativeArrayOptions.ClearMemory),
                modCtlTarget = new NativeArray<float>(modifierCount, allocator, NativeArrayOptions.ClearMemory),
                modCtlLive = new NativeArray<float>(modifierCount, allocator, NativeArrayOptions.ClearMemory),
                bufL = new NativeArray<float>(outputBufferFrames, allocator, NativeArrayOptions.ClearMemory),
                bufR = new NativeArray<float>(outputBufferFrames, allocator, NativeArrayOptions.ClearMemory),
                gpFrom = new NativeArray<float>(paramCount, allocator, NativeArrayOptions.ClearMemory),
                gpTo = new NativeArray<float>(paramCount, allocator, NativeArrayOptions.ClearMemory),
                gpKind = new NativeArray<byte>(paramCount, allocator, NativeArrayOptions.ClearMemory),
                gmFrom = new NativeArray<float>(ChainLayout.MAX_MOD_PARAMS, allocator, NativeArrayOptions.ClearMemory),
                gmTo = new NativeArray<float>(ChainLayout.MAX_MOD_PARAMS, allocator, NativeArrayOptions.ClearMemory),
                gmKind = new NativeArray<byte>(ChainLayout.MAX_MOD_PARAMS, allocator, NativeArrayOptions.ClearMemory),
                gbFrom = new NativeArray<float>(ZoundDspConstants.MAX_BINDINGS, allocator, NativeArrayOptions.ClearMemory),
                gbTo = new NativeArray<float>(ZoundDspConstants.MAX_BINDINGS, allocator, NativeArrayOptions.ClearMemory),
                gbKind = new NativeArray<byte>(ZoundDspConstants.MAX_BINDINGS, allocator, NativeArrayOptions.ClearMemory),
                gnFrom = new NativeArray<float>(ZoundDspConstants.MAX_NODES, allocator, NativeArrayOptions.ClearMemory),
                gnTo = new NativeArray<float>(ZoundDspConstants.MAX_NODES, allocator, NativeArrayOptions.ClearMemory),
                gnKind = new NativeArray<byte>(ZoundDspConstants.MAX_NODES, allocator, NativeArrayOptions.ClearMemory),
                presence = new NativeArray<float>(ZoundDspConstants.MAX_NODES, allocator, NativeArrayOptions.ClearMemory),
                presencePrev = new NativeArray<float>(ZoundDspConstants.MAX_NODES, allocator, NativeArrayOptions.ClearMemory),
                dryL = new NativeArray<float>(ZoundDspConstants.CONTROL_BLOCK, allocator, NativeArrayOptions.ClearMemory),
                dryR = new NativeArray<float>(ZoundDspConstants.CONTROL_BLOCK, allocator, NativeArrayOptions.ClearMemory),
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
            if (modCtlTarget.IsCreated) modCtlTarget.Dispose();
            if (modCtlLive.IsCreated) modCtlLive.Dispose();
            if (bufL.IsCreated) bufL.Dispose();
            if (bufR.IsCreated) bufR.Dispose();
            if (gpFrom.IsCreated) gpFrom.Dispose(); if (gpTo.IsCreated) gpTo.Dispose(); if (gpKind.IsCreated) gpKind.Dispose();
            if (gmFrom.IsCreated) gmFrom.Dispose(); if (gmTo.IsCreated) gmTo.Dispose(); if (gmKind.IsCreated) gmKind.Dispose();
            if (gbFrom.IsCreated) gbFrom.Dispose(); if (gbTo.IsCreated) gbTo.Dispose(); if (gbKind.IsCreated) gbKind.Dispose();
            if (gnFrom.IsCreated) gnFrom.Dispose(); if (gnTo.IsCreated) gnTo.Dispose(); if (gnKind.IsCreated) gnKind.Dispose();
            if (presence.IsCreated) presence.Dispose(); if (presencePrev.IsCreated) presencePrev.Dispose();
            if (dryL.IsCreated) dryL.Dispose(); if (dryR.IsCreated) dryR.Dispose();
            stretch.Dispose();
            if (stretchScratchL.IsCreated) stretchScratchL.Dispose();
            if (stretchScratchR.IsCreated) stretchScratchR.Dispose();
        }
    }
}
