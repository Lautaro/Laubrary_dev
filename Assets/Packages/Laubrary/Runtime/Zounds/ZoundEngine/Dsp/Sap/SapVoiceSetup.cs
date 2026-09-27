using Unity.Collections;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// The main-thread half of starting a play: everything that has to be true about a voice's native state
    /// before the first block is rendered.
    ///
    /// This exists so there is exactly ONE copy of that initialisation. Two very different hosts need it —
    /// the long-lived voice object the offline renderer drives, and the per-play generator the audio graph
    /// drives — and the two sets of steps are subtle, order-sensitive and easy to get half right (clearing
    /// the state arena only as far as the chain actually uses, seeding every flat parameter from the
    /// layout's authored base in three separate places, deriving the tail and hangover budgets from the
    /// sample rate, and resetting each effect's internal state LAST because that step reads the values the
    /// earlier ones wrote). Duplicating it would mean every future change to a chain's start-up had to be
    /// made twice and verified twice, and the second copy would be the one that silently rots.
    ///
    /// Everything here runs on the main thread and may allocate, so it is deliberately NOT part of the
    /// per-block render path. Nothing here is Burst-compiled and nothing here should ever need to be.
    /// </summary>
    internal static class SapVoiceSetup {

        /// <summary>
        /// Brings a voice's native state to its start-of-play condition and resets every effect in the
        /// chain. Pass <paramref name="armSource"/> false for a group node, which has no source stage of
        /// its own because its children sum into its buffers instead.
        ///
        /// The caller is responsible for having built <paramref name="sapLayout"/> from
        /// <paramref name="layout"/> already, and for the host-level bookkeeping that is none of this
        /// function's business (which bus or parent group the node feeds, the token, steal protection, the
        /// clock reading taken when the play was allocated).
        /// </summary>
        internal static void Reset(ref SapVoiceState sap, in SapChainLayout sapLayout, ChainLayout layout,
                                  int sampleRate, float basePitch, float outGain, long tokenId,
                                  bool armSource, double startFrame, double endFrame, bool loop) {
            sap.basePitchLive = basePitch;
            sap.outGainLive = outGain;
            sap.stopping = false;
            sap.released = false;
            sap.sourceExhausted = false;
            sap.elapsedSamples = 0;
            sap.samplesSinceSourceEnd = 0;
            sap.silentSamples = 0;
            sap.lastPeak = 0f;

            // Cleared for a group as well as a source. The source path always cleared all of this; the group
            // path used to clear only the three counters and leave the schedule itself, which is harmless
            // today only because a node is a group for its whole life and so can never have had a repeat
            // schedule armed on it. Clearing unconditionally costs nothing, removes the asymmetry, and means
            // a stale schedule cannot be inherited if that ever stops being true.
            sap.repeat = default;
            sap.repeatsPending = 0;
            sap.repeatsDone = armSource ? 1 : 0;
            sap.repeatsTotal = 1;
            sap.nextRepeatSample = 0;
            sap.lastRepeatEndSample = 0;
            sap.trainEndSample = 0;
            sap.slotsStolen = 0;

            // Seeded from the token so two voices started in the same block, on the same chain, do not make
            // identical "random" choices. The constant is the standard seed for this generator shape; a zero
            // seed would make it produce zero forever.
            sap.rng = 2463534242u ^ (uint)tokenId;

            for (int i = 0; i < sap.slots.Length; i++) sap.slots[i] = default;
            if (armSource) {
                sap.slots[0] = new SourceSlot {
                    active = true, cursor = startFrame, startFrame = startFrame, endFrame = endFrame,
                    gain = 1f, pitchMul = 1f, loop = loop,
                };
            }

            // Only as far as this chain actually uses, not the whole arena: the arena is sized for the
            // worst-case chain in its tier, and clearing all of it on every play would be a large pointless
            // write. The lower bound of one keeps an empty chain from producing a zero-length clear whose
            // intent would be ambiguous to read.
            int clearLen = sap.arena.Length < layout.stateFloats ? sap.arena.Length : layout.stateFloats;
            if (clearLen < 1) clearLen = 1;
            if (clearLen > sap.arena.Length) clearLen = sap.arena.Length;
            for (int ci = 0; ci < clearLen; ci++) sap.arena[ci] = 0f;

            for (int i = 0; i < layout.paramCount; i++) {
                float b = layout.pBase[i];
                sap.pLive[i] = b;
                sap.pStart[i] = b;
                sap.pStep[i] = 0f;
                sap.pTarget[i] = b;
            }

            sap.tailBudgetSamples = (long)(layout.tailSeconds * sampleRate);
            sap.hangoverSamples = ZoundDspConstants.HANGOVER_MS * sampleRate / 1000;

            // LAST, deliberately: resetting the effects reads the state arena and the parameter block this
            // function has just filled in.
            ZoundEffects.ResetChain(sapLayout, sap.arena, sampleRate);
        }

        /// <summary>
        /// Arms a repeat train — the sound replaying itself a number of times, optionally re-rolling its
        /// pitch and loudness per repeat. Call after <see cref="Reset"/> and before the first block.
        ///
        /// Shared between both hosts for the same reason the rest of the setup is: the schedule's opening
        /// state is derived from the plan in a few non-obvious ways (the first repeat is counted as already
        /// done because the original play IS the first, an unlimited train is marked by the largest possible
        /// pending count rather than by a flag, and spacing measured from the end of the previous repeat
        /// starts from one nominal play length rather than from zero), and a second copy of that reasoning
        /// would drift.
        ///
        /// <paramref name="protectedFromSteal"/> comes back true while repeats are still outstanding, so
        /// whatever reclaims voices under pressure leaves a train that has not finished alone.
        /// </summary>
        internal static void ArmRepeats(ref SapVoiceState sap, in RepeatPlan plan, out bool protectedFromSteal) {
            sap.repeat = plan;
            if (!plan.enabled) { protectedFromSteal = false; return; }

            sap.repeatsTotal = plan.count;
            sap.repeatsDone = 1;
            sap.repeatsPending = plan.count == int.MaxValue ? int.MaxValue : plan.count - 1;
            sap.lastRepeatEndSample = plan.nominalLengthSamples;
            sap.nextRepeatSample = plan.spaceFromEnd
                ? sap.lastRepeatEndSample + plan.intervalSamples
                : plan.intervalSamples;
            protectedFromSteal = sap.repeatsPending > 0;
            SapVoiceRender.ProjectTrainEnd(ref sap);
        }

        /// <summary>
        /// Builds (or rebuilds) the two per-play native snapshots a voice reads on the audio thread: the
        /// chain layout and the source samples. Releases any previous pair first, so a host may set the same
        /// voice up more than once without leaking.
        ///
        /// <paramref name="pcm"/> may be null for a group node, which reads no source; the sample snapshot
        /// is then left uncreated rather than being an empty buffer, so that a group accidentally trying to
        /// read a source fails visibly instead of reading silence.
        /// </summary>
        internal static void BuildSnapshots(ref SapChainLayout sapLayout, ref SapPcm sapPcm,
                                            ChainLayout layout, PcmClip pcm, Allocator allocator) {
            if (sapLayout.IsCreated) sapLayout.Dispose();
            sapLayout = SapChainLayout.Create(layout, allocator);
            if (sapPcm.IsCreated) sapPcm.Dispose();
            sapPcm = pcm != null ? SapPcm.Create(pcm, allocator) : default;
        }
    }
}
