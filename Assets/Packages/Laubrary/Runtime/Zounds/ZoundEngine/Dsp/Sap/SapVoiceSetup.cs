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
                                  bool armSource, double startFrame, double endFrame, bool loop,
                                  Zound zound = null) {
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

            SeedModifiersAtTrigger(ref sap, layout, zound);
        }

        /// <summary>
        /// Where a stepping modulator had got to, remembered PER SOUND rather than per play.
        ///
        /// A step list that advances one step per play cannot possibly work from per-voice state, because a voice is
        /// wiped clean before every play — it would take the first step every time and never move. The position has to
        /// outlive the play, and it belongs to the sound rather than to any one playing copy of it, so that two
        /// overlapping plays of the same sound continue the same sequence instead of each running their own.
        ///
        /// Keyed weakly in effect: an entry costs a handful of numbers and is only created for a sound that actually
        /// has a step list, so this does not grow with the size of the project.
        /// </summary>
        private struct StepState { public int index; public int usedMask; public bool started; }
        private static readonly System.Collections.Generic.Dictionary<Zound, StepState[]> stepStates
            = new System.Collections.Generic.Dictionary<Zound, StepState[]>();

        /// <summary>Forgets every remembered step position. For a cache clear or a project reload.</summary>
        internal static void ForgetStepPositions() {
            stepStates.Clear();
        }

        /// <summary>
        /// Gives a modulator the starting state it can only be given at the moment a sound is triggered, on the main
        /// thread, where the clock and a random number generator are available.
        ///
        /// **Why this stage has to exist at all.** Everything in the reset above brings a voice to a clean slate, which
        /// is exactly right for a modulator meant to behave identically on every play, and exactly wrong for one meant
        /// NOT to. An oscillator whose phase should not restart has to be told where the oscillation currently is, and a
        /// clean slate always says "at the beginning". A step list that advances per play has to be told which step this
        /// play is on, and a clean slate always says "the first". So without this stage both silently do the opposite of
        /// what their setting says — and the failure looks like the feature not working rather than like a bug.
        /// </summary>
        private static void SeedModifiersAtTrigger(ref SapVoiceState sap, ChainLayout layout, Zound zound) {
            for (int m = 0; m < layout.modCount && m < ZoundDspConstants.MAX_MODIFIERS; m++) {
                int po = layout.modParamOffset[m];
                int pc = layout.modParamCountOf[m];
                int so = layout.modStateOffset[m];
                if (so < 0 || so >= sap.arena.Length) continue;

                switch (layout.modType[m]) {
                    case ZoundModifierType.Lfo: {
                        // Parameter 3 is "reset phase". When it is ON the clean slate is correct and there is nothing
                        // to do. When it is OFF the oscillation is meant to run continuously whether or not anything
                        // is playing, so the phase it should start at is wherever that continuous oscillation has got
                        // to by now — which is the wall clock multiplied by the rate, keeping only the fraction.
                        //
                        // Deriving it from the clock rather than remembering it between plays is deliberate: two
                        // sounds playing at once then agree about where the oscillation is, without sharing any
                        // mutable state between voices that are rendered on the audio thread.
                        //
                        // The clock is ordinary elapsed real time, NOT the audio clock. The audio clock is the more
                        // natural choice and is what the earlier version of this engine used, but it stands still in
                        // the editor whenever nothing is playing — so two plays a few seconds apart would be handed the
                        // same phase, and the setting would appear to do nothing in exactly the place where somebody is
                        // trying it out. Elapsed real time advances whether or not audio is running, in the editor and
                        // in a built game alike. The audio clock's extra precision buys nothing here, because all this
                        // needs is roughly where in the cycle we are.
                        bool resetPhase = pc > 3 ? layout.modParamFlat[po + 3] >= 0.5f : true;
                        if (resetPhase) break;
                        float rate = pc > 1 ? layout.modParamFlat[po + 1] : 1f;
                        double phase = (UnityEngine.Time.realtimeSinceStartupAsDouble * rate) % 1.0;
                        if (phase < 0d) phase += 1d;
                        sap.arena[so] = (float)phase;
                        break;
                    }

                    case ZoundModifierType.Step: {
                        // A step list set to advance "per play" has to be told which step this play is on, because the
                        // position is the one thing about it that must survive the play ending. Without this it took the
                        // first step every time and never moved — the whole point of the modulator, lost silently.
                        if (zound == null) break;       // no sound to remember against, e.g. an offline render
                        int count = layout.modStepCountOf[m];
                        if (count <= 0) break;

                        bool roundRobin = pc > 2 && (int)layout.modParamFlat[po + 2] == (int)StepOrder.RoundRobinNoRepeat;
                        bool startRandom = pc > 3 && layout.modParamFlat[po + 3] >= 0.5f;
                        bool resetOnTrigger = pc > 4 && layout.modParamFlat[po + 4] >= 0.5f;
                        bool perPlay = pc > 0 && (int)layout.modParamFlat[po] == (int)StepTiming.PerTrigger;

                        if (!stepStates.TryGetValue(zound, out var states) || states.Length < layout.modCount) {
                            states = new StepState[ZoundDspConstants.MAX_MODIFIERS];
                            stepStates[zound] = states;
                        }

                        if (!states[m].started || resetOnTrigger) {
                            int last = states[m].started ? states[m].index : -1;
                            states[m].index = startRandom ? UnityEngine.Random.Range(0, count) : 0;
                            states[m].usedMask = 0;
                            // Avoid opening on the step that just played, which would be heard as a stutter rather
                            // than as a new pick.
                            if (roundRobin && last >= 0 && count > 1) {
                                while (states[m].index == last) states[m].index = UnityEngine.Random.Range(0, count);
                            }
                            states[m].usedMask |= 1 << states[m].index;
                            states[m].started = true;
                        }
                        else if (perPlay) {
                            int used = states[m].usedMask;
                            states[m].index = ZoundDspPlayback.NextStepIndex(states[m].index, count, roundRobin, ref used);
                            states[m].usedMask = used;
                        }

                        sap.arena[so] = states[m].index;
                        if (so + 2 < sap.arena.Length) sap.arena[so + 2] = states[m].usedMask;
                        break;
                    }
                }
            }
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
