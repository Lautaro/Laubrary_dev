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
    /// <summary>
    /// The moment a play counts as triggered, for modifiers that join a running clock (an oscillator set to Always, a timed
    /// step list with Retrigger off). Normally just now.
    ///
    /// The one other caller is the editor's analyser, which re-creates a play that has ALREADY started — it only notices a
    /// play a frame or more after it began — and has to join the clocks where that play joined them, or its picture runs
    /// ahead of the sound by that much (measured: with 0.6 s steps, the analyser's line and the playing sound disagreed
    /// until this existed, T-0443). Set only for the duration of that one call, on the main thread, then cleared.
    /// </summary>
    public static class ZoundTriggerClock {
        public static double? overrideTime;
        public static double Now => overrideTime ?? UnityEngine.Time.realtimeSinceStartupAsDouble;

        /// <summary>
        /// True while a display re-creates a play rather than a sound really playing. A re-created play takes the value
        /// the real play of that sound drew for each Random modifier, instead of drawing its own (which would draw a
        /// different line from the one heard), and never records a draw of its own. Set and cleared around one call on
        /// the main thread, like <see cref="overrideTime"/>.
        /// </summary>
        public static bool recreating;
    }

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
        /// <summary>
        /// Sets a looping voice's crossmix range, in source frames (T-0474), and draws the current cycle's outgoing length.
        /// Both 0 means a plain wrap at the end; equal values a fixed length; otherwise every cycle draws its own length
        /// in the range. The top of the range is limited to half the loop, so one cycle's fade-in can never overlap its
        /// own fade-out.
        /// </summary>
        internal static void SetLoopCrossmix(ref SapVoiceState sap, in SapPcm pcm, double minFrames, double maxFrames) {
            if (!sap.looping.enabled) return;
            if (minFrames < 0) minFrames = 0;
            if (maxFrames < minFrames) maxFrames = minFrames;
            sap.looping.crossMin = minFrames;
            sap.looping.crossMax = maxFrames;
            if (!sap.looping.inFade) {
                var main = sap.slots[sap.looping.mainSlot];
                sap.looping.outLen = SapVoiceRender.LoopCrossmixLength(in sap.looping, in pcm, main.startFrame, main.endFrame, sap.looping.cycle);
            }
        }

        /// <summary>Moves a looping voice's region (source frames), in both of its slots (T-0474).</summary>
        internal static void SetLoopRegion(ref SapVoiceState sap, in SapPcm pcm, double startFrame, double endFrame) {
            startFrame = System.Math.Round(startFrame); endFrame = System.Math.Round(endFrame);
            if (!sap.looping.enabled || !(endFrame > startFrame + 1)) return;
            for (int s = 0; s < 2; s++) {
                var sl = sap.slots[s];
                sl.startFrame = startFrame; sl.endFrame = endFrame;
                sap.slots[s] = sl;
            }
            if (!sap.looping.inFade) sap.looping.outLen = SapVoiceRender.LoopCrossmixLength(in sap.looping, in pcm, startFrame, endFrame, sap.looping.cycle);
        }

        internal static void Reset(ref SapVoiceState sap, in SapChainLayout sapLayout, ChainLayout layout,
                                  int sampleRate, float basePitch, float outGain, long tokenId,
                                  bool armSource, double startFrame, double endFrame, bool loop,
                                  Zound zound = null) {
            sap.basePitchLive = basePitch;
            sap.outGainLive = outGain;
            sap.baseSpeedLive = 1f;
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

            // The Looper (T-0474): the loop reader runs this voice's slots 0 and 1. No crossmix until one is set.
            // A loop's ends are whole frames: a region ending a hair past a frame (1.1 s x 48000 in floating point is
            // 52800.00000001) made the seam read a clamped copy of the last frame, which was measured as a break in an
            // otherwise seamless loop.
            sap.looping = default;
            sap.looping.enabled = armSource && loop && sap.slots.Length >= 2 && endFrame > startFrame + 1;
            if (sap.looping.enabled) {
                var s0 = sap.slots[0];
                s0.startFrame = System.Math.Round(startFrame); s0.endFrame = System.Math.Round(endFrame);
                s0.cursor = s0.startFrame;
                sap.slots[0] = s0;
            }
            sap.looping.seed = 0x9E3779B9u ^ (uint)tokenId * 0x85EBCA77u ^ (uint)(tokenId >> 32);

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
            lastRandomDraw.Clear();
        }

        /// <summary>The value each sound's Random modifiers drew on its latest real play, by sound name and modifier, so a
        /// display re-creating that play shows the value actually heard.</summary>
        private static readonly System.Collections.Generic.Dictionary<(string, int), float> lastRandomDraw
            = new System.Collections.Generic.Dictionary<(string, int), float>();

        /// <summary>
        /// A Random modifier's draw. The value is a swing like every other modifier's output: -1 is as far down as the
        /// binding reaches, +1 as far up (under Set, the bottom and top of the range). Min and Max are held to that, since
        /// anything beyond it only pins the parameter against an end. Bias bends the draw towards one end: above one
        /// towards Max, below one towards Min — the old draw had this backwards (raising to the power of Bias, which
        /// favours the minimum for a Bias above one, the opposite of what the setting says).
        /// </summary>
        internal static float DrawRandom(float min, float max, float bias, float r01) {
            min = UnityEngine.Mathf.Clamp(min, -1f, 1f);
            max = UnityEngine.Mathf.Clamp(max, -1f, 1f);
            if (bias > 0f && System.Math.Abs(bias - 1f) > 1e-4f) r01 = UnityEngine.Mathf.Pow(r01, 1f / bias);
            return min + (max - min) * r01;
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
        /// <summary>
        /// Puts a play of a Random-mode oscillator set to "Always" onto the one random walk that oscillator follows all
        /// the time, at the point the walk has reached now.
        ///
        /// **The walk is a function of the clock, not of anyone's history.** Time is cut into intervals of the "New target
        /// every" length, numbered from the clock; each interval's target is a fixed hash of its number, so every play
        /// works out the same targets without sharing state. The value between targets is a glide, and a glide that has
        /// not finished when the next target arrives carries its unfinished position forward — so the value now depends
        /// on the glides before it. Rather than store that history anywhere, it is replayed here from far enough back
        /// that where the replay started no longer shows (each interval closes at least the glide's share of the gap, so
        /// the starting point fades geometrically); a play started now and a play that has been running for a minute then
        /// agree to within a thousandth.
        ///
        /// The seed is taken from the sound's name and the oscillator's position in its list: two oscillators in the same
        /// sound wander independently, and the same one wanders the same way wherever that sound is played.
        /// </summary>
        private static void SeedSharedRandomWalk(ref SapVoiceState sap, ChainLayout layout, int m, int po, int pc, int so,
                                                 Zound zound, double now) {
            float rate = pc > 1 ? layout.modParamFlat[po + 1] : 1f;
            float every = pc > 5 ? layout.modParamFlat[po + 5] : 0.5f;
            if (every < 0.001f) every = 0.001f;
            int seed = StableSeed(zound != null ? zound.name : null, m);

            double intervals = now / every;
            double whole = System.Math.Floor(intervals);
            float intoInterval = (float)((intervals - whole) * every);
            long k = (long)whole;

            // How much of the remaining gap one interval's glide closes. A rate of zero jumps straight to the target.
            float share = rate <= 0f ? 1f : System.Math.Min(1f, rate * every);
            int replay = share >= 1f ? 1 : (int)System.Math.Min(4000, System.Math.Ceiling(7.0 / share));

            float current = SapVoiceRender.LfoWalkTarget(Wrap(k - replay - 1), seed);
            for (long i = k - replay; i < k; i++) {
                float target = SapVoiceRender.LfoWalkTarget(Wrap(i), seed);
                current += (target - current) * share;
            }
            // Now inside interval k: gliding from where the last one left off towards this interval's target.
            float from = current;
            float to = SapVoiceRender.LfoWalkTarget(Wrap(k), seed);
            float progress = rate <= 0f ? 1f : System.Math.Min(1f, rate * intoInterval);

            sap.arena[so + 1] = from + (to - from) * progress;
            sap.arena[so + 2] = to;
            sap.arena[so + 3] = every - intoInterval;
            sap.arena[so + 4] = from;
            sap.arena[so + 5] = rate <= 0f ? 0f : rate * intoInterval;
            sap.arena[so + 7] = 1f;          // started: the render must not draw its own first targets over this
            sap.arena[so + 8] = Wrap(k);
            sap.arena[so + 9] = seed;
        }

        private static int Wrap(long interval) {
            long w = interval % (long)SapVoiceRender.LfoWalkWrap;
            if (w < 0) w += (long)SapVoiceRender.LfoWalkWrap;
            return (int)w;
        }

        /// <summary>A seed that is the same in every session for the same sound and oscillator, and small enough to be
        /// held exactly in the float state the audio side reads it from.</summary>
        private static int StableSeed(string name, int modifierIndex) {
            uint h = 2166136261u;
            if (name != null) for (int i = 0; i < name.Length; i++) { h ^= name[i]; h *= 16777619u; }
            h ^= (uint)modifierIndex * 0x9E3779B1u;
            return (int)(h & 0xFFFFF);
        }

        private static void SeedModifiersAtTrigger(ref SapVoiceState sap, ChainLayout layout, Zound zound) {
            for (int m = 0; m < layout.modCount && m < ZoundDspConstants.MAX_MODIFIERS; m++) {
                int po = layout.modParamOffset[m];
                int pc = layout.modParamCountOf[m];
                int so = layout.modStateOffset[m];
                if (so < 0 || so >= sap.arena.Length) continue;

                switch (layout.modType[m]) {
                    case ZoundModifierType.Random: {
                        // One value per play, drawn here and held by the render for the whole play. This draw was lost
                        // when the native voice's start-up code was removed (f500b5fd) and never carried over, so every
                        // play held nought: under Set that parked the parameter at the middle of its range whatever Min
                        // and Max said, and under Shift it did nothing (T-0447).
                        float v;
                        string key = zound != null ? zound.name : null;
                        if (ZoundTriggerClock.recreating && key != null && lastRandomDraw.TryGetValue((key, m), out float drawn)) v = drawn;
                        else {
                            v = DrawRandom(pc > 0 ? layout.modParamFlat[po] : -0.25f, pc > 1 ? layout.modParamFlat[po + 1] : 0.25f,
                                           pc > 2 ? layout.modParamFlat[po + 2] : 1f, UnityEngine.Random.value);
                            if (!ZoundTriggerClock.recreating && key != null) lastRandomDraw[(key, m)] = v;
                        }
                        sap.arena[so] = v;
                        break;
                    }
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
                        double now = ZoundTriggerClock.Now;
                        double phase = (now * rate) % 1.0;
                        if (phase < 0d) phase += 1d;
                        sap.arena[so] = (float)phase;

                        bool random = pc > 4 && (int)layout.modParamFlat[po + 4] == (int)LfoMode.Random;
                        if (random && so + 9 < sap.arena.Length) SeedSharedRandomWalk(ref sap, layout, m, po, pc, so, zound, now);
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

                        // Per interval with Retrigger OFF: the list keeps stepping on its own clock whether or not
                        // anything plays, and this play joins it wherever it has got to — the same meaning "Always" has
                        // for an oscillator. This case used to fall through the code below untouched, so every play
                        // started on the first step exactly as if Retrigger were on: the setting did nothing (T-0432).
                        //
                        // Worked out from the clock rather than carried over from the previous play, because carrying it
                        // over would mean reading a finished voice's memory, which may already have been released.
                        if (!perPlay && !resetOnTrigger && so + 7 < sap.arena.Length) {
                            float interval = System.Math.Max(0.0005f, (pc > 1 ? layout.modParamFlat[po + 1] : 250f) * 0.001f);
                            double now = ZoundTriggerClock.Now;
                            double stepsSoFar = now / interval;
                            double whole = System.Math.Floor(stepsSoFar);
                            int k = Wrap((long)whole);
                            int seed = StableSeed(zound.name, m);
                            int sso = layout.modStepOffset[m];
                            int idx = SapVoiceRender.FreeRunStep(k, count, roundRobin, seed);
                            int prev = SapVoiceRender.FreeRunStep(k > 0 ? k - 1 : 0, count, roundRobin, seed);
                            sap.arena[so] = idx;
                            sap.arena[so + 1] = (float)((stepsSoFar - whole) * interval);
                            sap.arena[so + 3] = k;
                            // A glide into this step started from the previous step's value (a glide always finishes by
                            // the end of its step), so a play joining mid-glide lands on the same value as one already running.
                            sap.arena[so + 4] = layout.modStepFlat[sso + prev];
                            sap.arena[so + 5] = 1f;
                            sap.arena[so + 6] = seed;
                            sap.arena[so + 7] = 1f;
                            break;
                        }

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
