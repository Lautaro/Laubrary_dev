using UnityEngine;

namespace Laubrary.Zounds.Dsp.Native {

    /// <summary>
    /// One playing voice or group, as seen from the main thread: a handle onto a node
    /// that lives, renders and frees itself inside the native engine.
    ///
    /// This replaces <see cref="DspVoice"/> on the realtime path. DspVoice itself is
    /// not gone — it is still the offline renderer, and still the reference the native
    /// engine is tested against — but nothing on the audio thread is managed any more.
    ///
    /// Reads and writes of per-node control state go straight into the shared control
    /// block through a pointer, with no P/Invoke: the same publish-by-release-store
    /// handshake the managed voices used, in native memory. Structural operations
    /// (prepare, publish, seed a modifier) are P/Invoke, and all of them are main
    /// thread only.
    /// </summary>
    public sealed class NativeNode {

        /// <summary>Index into the native pool: voices 0..MAX_VOICES-1, then groups.</summary>
        public readonly int nodeId;
        /// <summary>Index within its own kind — the voice number, or the group number.</summary>
        public readonly int index;
        public readonly bool isGroup;
        public readonly bool heavyTier;

        /// <summary>The layout this node was prepared with. Main thread only; the audio thread has its own copy.</summary>
        internal ChainLayout layout;
        internal long tokenId;

        // ── main-thread bookkeeping the native side does not need ──
        /// <summary>Kept out of the steal search while a repeat train is still pending.</summary>
        internal bool protectedFromSteal;
        internal double allocatedAtDsp;
        /// <summary>Realtime when this node was first seen Tailing (0 = not tailing). See the stale-tail sweep.</summary>
        internal float tailingSeenAt;
        internal long onsetReportedForToken;
        /// <summary>Onset count and timestamp at the last sweep, so a rate can be derived without reading the native ring.</summary>
        internal int lastOnsetTotal;
        internal float lastOnsetSampleAt;

        public NativeNode(int nodeId, int index, bool isGroup, bool heavyTier) {
            this.nodeId = nodeId;
            this.index = index;
            this.isGroup = isGroup;
            this.heavyTier = heavyTier;
        }

        private unsafe ZoundsNative.NodeControl* C => ZoundsNative.Node(nodeId);

        public unsafe VoiceState State => (VoiceState)C->state;
        public unsafe float LastPeak => C->lastPeak;
        public unsafe int OnsetTotal => C->onsetTotal;
        public unsafe long ElapsedSamples => C->elapsedSamples;
        public unsafe long TrainEndSample => C->trainEndSample;
        public unsafe int BusIndex => C->busIndex;
        /// <summary>The parent as the native engine sees it: a flat node id, or -1.</summary>
        public unsafe int GroupNodeIndex => C->groupIndex;

        /// <summary>
        /// The parent as the rest of Zounds counts groups (0..MAX_GROUPS-1), or -1.
        /// The native side addresses voices and groups in ONE flat index space, so the
        /// two numberings are not interchangeable — passing a group index straight
        /// through would make group 0 resolve to voice 0.
        /// </summary>
        public int GroupIndex {
            get { int n = GroupNodeIndex; return n >= ZoundDspConstants.MAX_VOICES ? n - ZoundDspConstants.MAX_VOICES : -1; }
        }

        private static int ToNodeId(int groupIndex) =>
            groupIndex >= 0 ? ZoundDspConstants.MAX_VOICES + groupIndex : -1;
        public unsafe int Depth => C->depth;
        public unsafe int RepeatsDone => C->repeatsDone;
        public unsafe int RepeatsTotal => C->repeatsTotal;
        public unsafe int LiveChildren => C->liveChildren;
        public unsafe int SlotsStolen => C->slotsStolen;

        /// <summary>True while a repeat train is armed on this node.</summary>
        public bool RepeatEnabled { get; private set; }

        /// <summary>
        /// Grows the resolved play length on a node that is already playing. A repeat
        /// train that runs long extends it, and the source duration is what an Envelope
        /// and a Fade measure themselves against, so it has to reach the audio thread.
        /// </summary>
        public void SetSourceDuration(float seconds) { ZoundsNative.Zounds_SetSourceDuration(nodeId, seconds); }

        /// <summary>One float out of this node's state arena (a modifier's phase or step index), for probes.</summary>
        public float ReadState(int offset) => ZoundsNative.Zounds_ReadNodeState(nodeId, offset);

        /// <summary>Hard stop: declick and flush. The node frees itself on its next block.</summary>
        public unsafe void RequestKill() { C->killRequest = 1; }

        /// <summary>Stop feeding the source and let the tail ring out.</summary>
        public unsafe void RequestRelease() { C->releaseRequest = 1; }

        public unsafe void SetPaused(bool paused) { C->pauseRequest = paused ? 1 : 0; }

        public unsafe void SetBasePitchTarget(float v) { C->basePitchTarget = v; }
        public unsafe void SetOutGainTarget(float v) { C->outGainTarget = v; }

        /// <summary>Prepares a Free voice for a play. Returns false when the native side refused it.</summary>
        internal bool Prepare(long tokenId, int busIndex, int pcmId, ChainLayout layout, int layoutId,
                              double startFrame, double endFrame, float basePitch, float outGain,
                              float sourceDuration, bool loop, int parentGroup) {
            this.layout = layout;
            this.tokenId = tokenId;
            RepeatEnabled = false;
            protectedFromSteal = false;
            tailingSeenAt = 0f;
            onsetReportedForToken = 0;
            lastOnsetTotal = 0;
            lastOnsetSampleAt = 0f;
            allocatedAtDsp = AudioSettings.dspTime;
            var args = new ZoundsNative.PrepareArgs {
                tokenId = tokenId, busIndex = busIndex, groupIndex = ToNodeId(parentGroup), depth = 0,
                pcmId = pcmId, layoutId = layoutId, isGroup = 0,
                startFrame = startFrame, endFrame = endFrame,
                basePitch = basePitch, outGain = outGain, sourceDuration = sourceDuration,
                loop = loop ? 1 : 0,
            };
            return ZoundsNative.Zounds_PrepareNode(nodeId, ref args) != 0;
        }

        /// <summary>Prepares a Free group node: no source stage, children sum into it.</summary>
        internal bool PrepareGroup(long tokenId, int busIndex, int parentGroup, int depth,
                                   ChainLayout layout, int layoutId, float duration) {
            this.layout = layout;
            this.tokenId = tokenId;
            RepeatEnabled = false;
            protectedFromSteal = true;
            tailingSeenAt = 0f;
            onsetReportedForToken = 0;
            lastOnsetTotal = 0;
            lastOnsetSampleAt = 0f;
            allocatedAtDsp = AudioSettings.dspTime;
            var args = new ZoundsNative.PrepareArgs {
                tokenId = tokenId, busIndex = busIndex, groupIndex = ToNodeId(parentGroup), depth = depth,
                pcmId = 0, layoutId = layoutId, isGroup = 1,
                startFrame = 0, endFrame = 0,
                basePitch = 1f, outGain = 1f, sourceDuration = duration, loop = 0,
            };
            return ZoundsNative.Zounds_PrepareNode(nodeId, ref args) != 0;
        }

        internal void SetRepeat(in RepeatPlan plan) {
            var p = new ZoundsNative.NativeRepeatPlan {
                enabled = plan.enabled ? 1 : 0,
                count = plan.count,
                intervalSamples = plan.intervalSamples,
                spaceFromEnd = plan.spaceFromEnd ? 1 : 0,
                retrigger = plan.retrigger ? 1 : 0,
                pitchMulMin = plan.pitchMulMin, pitchMulMax = plan.pitchMulMax,
                gainMulMin = plan.gainMulMin, gainMulMax = plan.gainMulMax,
                durationLimitSamples = plan.durationLimitSamples,
                nominalLengthSamples = plan.nominalLengthSamples,
            };
            ZoundsNative.Zounds_SetRepeat(nodeId, ref p);
            RepeatEnabled = plan.enabled;
            protectedFromSteal = plan.enabled && plan.count > 1;
        }

        /// <summary>Seeds a modifier's per-voice state (trigger-time values resolved on the main thread).</summary>
        internal void SeedModifierState(int modifierIndex, int slot, float value) {
            ZoundsNative.Zounds_SeedModifier(nodeId, modifierIndex, slot, value);
        }

        internal void Publish() { ZoundsNative.Zounds_PublishNode(nodeId); }

        /// <summary>Pushes a single parameter value into a node that is already playing.</summary>
        internal void PushLiveParam(int flatIndex, float value) {
            ZoundsNative.Zounds_PushLiveParam(nodeId, flatIndex, value);
        }

        /// <summary>
        /// Main-thread emergency free for a node the audio thread stopped visiting. Only
        /// called after a kill request went unanswered for seconds.
        /// </summary>
        internal void ForceFree() {
            ZoundsNative.Zounds_ForceFree(nodeId);
            layout = null;
        }

        /// <summary>Main-thread diagnostic snapshot (values may be a block stale).</summary>
        internal unsafe string DebugCompletionState() {
            var c = C;
            int f = c->flags;
            return "state=" + (VoiceState)c->state + " bus=" + c->busIndex + " parent=" + c->groupIndex
                 + " released=" + ((f & 1) != 0) + " stopping=" + ((f & 2) != 0)
                 + " sourceExhausted=" + ((f & 4) != 0) + " arenaOverflow=" + ((f & 32) != 0)
                 + " elapsed=" + c->elapsedSamples + " pause=" + c->pauseRequest
                 + " release=" + c->releaseRequest + " kill=" + c->killRequest
                 + " liveChildren=" + c->liveChildren + " lastPeak=" + c->lastPeak.ToString("F4")
                 + " repeats=" + c->repeatsDone + "/" + c->repeatsTotal;
        }
    }
}
