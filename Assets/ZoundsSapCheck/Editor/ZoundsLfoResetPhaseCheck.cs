// A kept check, runnable from the Laubrary menu — proves that an oscillating modulator's "Runs" setting (formerly
// "Reset phase": Per play = reset on, Always = reset off) actually does something, by reading the phase the engine starts
// each play at, and, for Random mode, that a later play joins the same random walk an earlier one is following.
//
// The behaviour under test, in plain terms: with reset ON, every play of a sound should start its oscillation at the
// same point, so the sound is identical each time. With reset OFF the oscillation is meant to run continuously whether
// or not anything is playing, so each play should catch it at wherever it happens to be — which is what makes the
// sound vary from play to play.
//
// This reads the phase out of the voice's own state rather than inferring it from the audio, because that is the value
// the setting is supposed to control, and comparing rendered audio would confound a phase difference with every other
// reason two renders might differ.
//
// Non-destructive: the sound it tests is built in memory and never added to the library.
using Stopwatch = System.Diagnostics.Stopwatch;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

public static class ZoundsLfoResetPhaseCheck {

    const int SR = 48000;
    const float RATE_HZ = 1f;

    [MenuItem("Laubrary/Zounds/Checks/9 - Does an oscillator's Runs (always or per play) setting work")]
    public static void RunFromMenu() { Debug.Log("[ZoundsLfoResetPhaseCheck]\n" + Execute()); }

    public static string Execute() {
        var sb = new System.Text.StringBuilder();
        sb.Append("=== DOES 'RESET PHASE' ACTUALLY DO ANYTHING? ===\n");
        sb.Append("oscillator rate ").Append(RATE_HZ).Append(" Hz, driving a low-pass cutoff\n\n");
        int failures = 0;

        // Reset ON: two plays, separated in time, must start at the SAME phase.
        float onA = StartPhaseOf(resetPhase: true);
        WaitForTheClockToMove();
        float onB = StartPhaseOf(resetPhase: true);
        sb.Append("reset ON  -> play 1 phase = ").Append(onA.ToString("R"))
          .Append(", play 2 phase = ").Append(onB.ToString("R")).Append('\n');
        if (!Mathf.Approximately(onA, onB)) {
            sb.Append("   <<< FAILED: with reset on, both plays must start at the same phase\n"); failures++;
        }
        else sb.Append("   ok: identical, so the sound repeats exactly\n");

        // Reset OFF: two plays, separated in time, must start at DIFFERENT phases.
        float offA = StartPhaseOf(resetPhase: false);
        double clockMoved = WaitForTheClockToMove();
        float offB = StartPhaseOf(resetPhase: false);
        sb.Append("reset OFF -> play 1 phase = ").Append(offA.ToString("R"))
          .Append(", play 2 phase = ").Append(offB.ToString("R"))
          .Append("   (clock advanced ").Append(clockMoved.ToString("F3")).Append(" s between them)\n");

        if (clockMoved <= 0d) {
            sb.Append("   INCONCLUSIVE: no time passed between the two plays, so there was nothing for the\n")
              .Append("   oscillation to move through.\n");
        }
        else if (Mathf.Approximately(offA, offB)) {
            sb.Append("   <<< FAILED: with reset off, a later play must catch the oscillation further along\n"); failures++;
        }
        else {
            sb.Append("   ok: different, so each play catches the oscillation where it currently is\n");
        }

        // "Always" in Random mode (T-0428): the oscillator glides between random targets on ONE walk that runs all the
        // time, so a play started later must pick up exactly where an earlier play has got to, not start a walk of its
        // own. Checked by rendering an earlier play for a while and comparing it with a later play's first value.
        sb.Append("\n\n=== RANDOM MODE, 'ALWAYS': DOES A LATER PLAY JOIN THE SAME WALK? ===\n");
        var walkChain = RandomWalkChain();
        double earlierStart = Time.realtimeSinceStartupAsDouble;
        var earlier = RenderModifierOutput(walkChain, 1f);
        WaitForTheClockToMove();
        double laterStart = Time.realtimeSinceStartupAsDouble;
        var later = RenderModifierOutput(walkChain, 0.02f);
        int at = Mathf.Clamp((int)System.Math.Round((laterStart - earlierStart) * SR / ZoundDspConstants.CONTROL_BLOCK) - 1, 1, earlier.Count - 2);
        // Timing is only known to within one control step either side (reading the clock and starting a render are not
        // the same instant), so the later play must fall inside what the earlier play covered around that moment.
        float lo = Mathf.Min(earlier[at - 1], Mathf.Min(earlier[at], earlier[at + 1])) - 0.002f;
        float hi = Mathf.Max(earlier[at - 1], Mathf.Max(earlier[at], earlier[at + 1])) + 0.002f;
        float wanderLo = float.MaxValue, wanderHi = float.MinValue;
        foreach (var v in earlier) { wanderLo = Mathf.Min(wanderLo, v); wanderHi = Mathf.Max(wanderHi, v); }
        sb.Append("later play starts at ").Append(later[0].ToString("0.0000")).Append("; earlier play at that moment ")
          .Append(earlier[at].ToString("0.0000")).Append("   (").Append((laterStart - earlierStart).ToString("F3"))
          .Append(" s later; the walk covered ").Append(wanderLo.ToString("0.00")).Append(" to ").Append(wanderHi.ToString("0.00")).Append(")\n");
        if (wanderHi - wanderLo < 0.05f) { sb.Append("   <<< FAILED: the walk barely moves, so this proves nothing\n"); failures++; }
        else if (later[0] < lo || later[0] > hi) { sb.Append("   <<< FAILED: the later play started a walk of its own\n"); failures++; }
        else sb.Append("   ok: the later play joined the walk the earlier one was following\n");

        // The same class of bug affected a step list set to advance once per play: its position also lived in state that
        // is wiped before every play, so it took the first step every time. Checked here because it is the same fix.
        sb.Append("\n=== DOES A STEP LIST ADVANCE FROM ONE PLAY TO THE NEXT? ===\n");
        var sound = new Klip(-4242) { name = "(step advance check)" };
        var seen = new int[5];
        for (int i = 0; i < seen.Length; i++) seen[i] = StepIndexOf(sound);
        sb.Append("steps taken across ").Append(seen.Length).Append(" consecutive plays: ")
          .Append(string.Join(", ", seen)).Append('\n');

        bool movedOn = false;
        for (int i = 1; i < seen.Length; i++) if (seen[i] != seen[0]) movedOn = true;
        if (movedOn) sb.Append("   ok: it moves on, so consecutive plays use different values\n");
        else { sb.Append("   <<< FAILED: every play took the same step, so the list never advances\n"); failures++; }

        // A timed step list with Retrigger OFF keeps stepping on its own clock, and each play joins it where it has got to
        // (T-0432). It used to start every play on the first step, exactly as if Retrigger were on. Checked by comparing
        // each play's starting step with the step the clock says it should be on, over several plays at odd spacings.
        sb.Append("\n=== TIMED STEP LIST, RETRIGGER OFF: DOES A PLAY JOIN THE LIST'S OWN CLOCK? ===\n");
        var joinSound = new Klip(-4343) { name = "(step clock check)" };
        int joined = 0, tries = 0;
        var startsSeen = new System.Collections.Generic.HashSet<int>();
        foreach (int gap in new[] { 37, 53, 71, 29, 61 }) {
            System.Threading.Thread.Sleep(gap);
            double before = Time.realtimeSinceStartupAsDouble;
            int start = TimedStepStart(joinSound, out double after);
            int expectBefore = (int)(System.Math.Floor(before / 0.05) % 3), expectAfter = (int)(System.Math.Floor(after / 0.05) % 3);
            if (start == expectBefore || start == expectAfter) joined++;
            startsSeen.Add(start);
            tries++;
        }
        sb.Append("plays joined the clock's step ").Append(joined).Append("/").Append(tries).Append("; distinct starting steps seen: ").Append(startsSeen.Count).Append("\n");
        if (joined != tries) { sb.Append("   <<< FAILED: a play did not start on the step the list's clock was on\n"); failures++; }
        else if (startsSeen.Count < 2) { sb.Append("   <<< FAILED: every play started on the same step, which is what Retrigger ON does\n"); failures++; }
        else sb.Append("   ok: each play joins the list where its clock has got to\n");

        sb.Append(failures == 0 ? "\nVERDICT: both settings behave as described.\n"
                                : "\nVERDICT: " + failures + " check(s) FAILED.\n");
        return sb.ToString();
    }

    /// A Random-mode oscillator set to "Always", gliding only part of the way between targets, so a later play lands on
    /// the right value only if it also replays the unfinished glides before it, which is the hard part.
    static ZoundEffectChain RandomWalkChain() {
        var chain = new ZoundEffectChain();
        chain.nodes.Add(new ZoundEffectNode(ZoundEffectType.LowPass));
        var lfo = new ZoundModifier(ZoundModifierType.Lfo);
        lfo.p[0] = 1f;       // amount
        lfo.p[1] = 3f;       // glide rate: 60% of the way per interval, so glides are left unfinished
        lfo.p[3] = 0f;       // runs: always
        lfo.p[4] = 1f;       // random
        lfo.p[5] = 0.2f;     // new target every 0.2 s
        lfo.p[6] = 0f;       // offset
        lfo.curve = null;
        chain.modifiers.Add(lfo);
        chain.bindings.Add(new ZoundModifierBinding {
            modifierIndex = 0, nodeIndex = 0, paramIndex = 0, combine = ModulationCombine.Shift, depth = 0.25f,
            schema = ChainModulationCompat.CURRENT_SCHEMA,
        });
        return chain;
    }

    /// The modifier's own output after every engine control step of an offline render.
    static System.Collections.Generic.List<float> RenderModifierOutput(ZoundEffectChain chain, float seconds) {
        var values = new System.Collections.Generic.List<float>();
        var input = new float[(int)(seconds * SR) * 2];
        ZoundDspOffline.Render(input, 2, SR, SR, chain, 1f, 1f, seconds, 0f, 0f, ZoundDspConstants.CONTROL_BLOCK, false,
                               (voice, written) => { if (written > 0) values.Add(voice.modValues[0]); });
        return values;
    }

    /// The step a fresh play starts on, for a three-step list stepping every 50 ms with Retrigger off.
    static int TimedStepStart(Klip sound, out double createdAt) {
        var chain = new ZoundEffectChain();
        chain.nodes.Add(new ZoundEffectNode(ZoundEffectType.Gain));
        var step = new ZoundModifier(ZoundModifierType.Step);
        step.EnsureParams();
        step.p[0] = 1f;   // timing: per interval
        step.p[1] = 50f;  // every 50 ms
        step.p[2] = 0f;   // sequential
        step.p[3] = 0f;   // no random start
        step.p[4] = 0f;   // Retrigger off
        step.steps = new float[] { 0.2f, 0.4f, 0.6f };
        chain.modifiers.Add(step);
        chain.bindings.Add(new ZoundModifierBinding {
            modifierIndex = 0, nodeIndex = 0, paramIndex = 0, combine = ModulationCombine.Shift, depth = 0.25f,
            schema = ChainModulationCompat.CURRENT_SCHEMA,
        });
        sound.effectChain = chain;
        var layout = ChainLayout.Build(chain, SR);
        var pcm = SilentClip();
        var voice = SapRealtimeVoice.Create(pcm, layout, SR, 0d, pcm.frames, 1f, 1f,
                                            (float)pcm.frames / SR, false, 1, true, Allocator.Persistent, sound);
        createdAt = Time.realtimeSinceStartupAsDouble;
        try { return (int)voice.sap.arena[layout.modStateOffset[0]]; }
        finally { voice.Dispose(); }
    }

    /// The step a fresh play of this sound starts on. Called repeatedly with the SAME sound, because the position is
    /// meant to be remembered against the sound rather than against any one play of it.
    static int StepIndexOf(Klip sound) {
        var chain = new ZoundEffectChain();
        chain.nodes.Add(new ZoundEffectNode(ZoundEffectType.Gain));

        var step = new ZoundModifier(ZoundModifierType.Step);
        step.p[0] = 0f;   // timing: per play
        step.p[2] = 0f;   // order: sequential, so the expected result is simply 0, 1, 2, ...
        step.p[3] = 0f;   // do not start at a random step, so this is deterministic
        step.p[4] = 0f;   // do not reset on every trigger
        step.steps = new float[] { 0.2f, 0.4f, 0.6f, 0.8f };
        chain.modifiers.Add(step);
        chain.bindings.Add(new ZoundModifierBinding {
            modifierIndex = 0, nodeIndex = 0, paramIndex = 0, op = ModifierOp.Multiply, depth = 1f,
        });
        sound.effectChain = chain;

        var layout = ChainLayout.Build(chain, SR);
        var pcm = SilentClip();
        var voice = SapRealtimeVoice.Create(pcm, layout, SR, 0d, pcm.frames, 1f, 1f,
                                            (float)pcm.frames / SR, false, 1, true, Allocator.Persistent, sound);
        try { return (int)voice.sap.arena[layout.modStateOffset[0]]; }
        finally { voice.Dispose(); }
    }

    /// The phase the engine seeds a brand-new play with, read straight out of the voice's own state.
    static float StartPhaseOf(bool resetPhase) {
        var chain = new ZoundEffectChain();
        chain.nodes.Add(new ZoundEffectNode(ZoundEffectType.LowPass));

        var lfo = new ZoundModifier(ZoundModifierType.Lfo);
        lfo.p[0] = 1f;              // amount
        lfo.p[1] = RATE_HZ;         // rate
        lfo.p[2] = 0f;              // sine
        lfo.p[3] = resetPhase ? 1f : 0f;
        chain.modifiers.Add(lfo);
        chain.bindings.Add(new ZoundModifierBinding {
            modifierIndex = 0, nodeIndex = 0, paramIndex = 0, op = ModifierOp.Multiply, depth = 1f,
        });

        var layout = ChainLayout.Build(chain, SR);
        var pcm = SilentClip();

        var voice = SapRealtimeVoice.Create(pcm, layout, SR, 0d, pcm.frames, 1f, 1f,
                                            (float)pcm.frames / SR, false, 1, true, Allocator.Persistent);
        try {
            // Read before rendering anything: this is the seeded starting phase, not a phase the render has advanced.
            int slot = layout.modStateOffset[0];
            return voice.sap.arena[slot];
        }
        finally { voice.Dispose(); }
    }

    /// Waits for real time to pass, and reports how much. Real time is the clock the seeding uses, deliberately, and
    /// unlike the audio clock it keeps moving whether or not audio is playing.
    static double WaitForTheClockToMove() {
        double start = Time.realtimeSinceStartupAsDouble;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 120) System.Threading.Thread.Sleep(5);
        return Time.realtimeSinceStartupAsDouble - start;
    }

    static PcmClip SilentClip() {
        int frames = SR / 4;
        var samples = new float[frames * 2];
        for (int i = 0; i < frames; i++) {
            float s = 0.2f * Mathf.Sin(2f * Mathf.PI * 300f * i / SR);
            samples[i * 2] = s;
            samples[i * 2 + 1] = s;
        }
        return new PcmClip { channels = 2, frequency = SR, frames = frames, samples = samples, valid = true, peak = 0.2f };
    }
}
