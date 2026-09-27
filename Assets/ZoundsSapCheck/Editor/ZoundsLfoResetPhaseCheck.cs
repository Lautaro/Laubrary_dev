// A kept check, runnable from the Laubrary menu — proves that an oscillating modulator's "reset phase" setting
// actually does something, by reading the phase the engine starts each play at.
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

    [MenuItem("Laubrary/Zounds/Checks/9 - Does an oscillator's reset-phase setting work")]
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

        sb.Append(failures == 0 ? "\nVERDICT: both settings behave as described.\n"
                                : "\nVERDICT: " + failures + " check(s) FAILED.\n");
        return sb.ToString();
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
