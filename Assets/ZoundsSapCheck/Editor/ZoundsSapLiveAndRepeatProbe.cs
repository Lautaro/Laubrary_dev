// A kept check, runnable from the Laubrary menu — checks the two capabilities that were lost when the native path was stripped and can be
// checked without a running audio device: retriggering (a sound replaying itself on a schedule) and live
// changes reaching a sound that is already playing.
//
// The live-change checks work by equivalence rather than by listening, which makes them decidable:
//   * applying a change BEFORE the first block must produce exactly what authoring that value produces;
//   * applying it PART WAY THROUGH must match the untouched render up to that point and the authored render
//     after it;
//   * applying it to a parameter a modifier is already driving must produce NO change at all, because
//     accepting it would be overwritten by the modifier a few milliseconds later and look like a glitch.
//
// The transport those changes travel through in a real game (a message to the generator's control half, which
// forwards it into the value channel the audio side drains) is NOT exercised here — it only moves while the
// graph is running. What is exercised is the receiving end, which is where all the logic lives.
using System.Text;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

public static class ZoundsSapLiveAndRepeatProbe {

    const int SR = 48000;
    const int SRC_RATE = 48000;
    const float SECONDS = 1.2f;
    const int BLOCK = 1024;

    [MenuItem("Laubrary/Zounds/Checks/4 - Retriggering and live changes")]
    public static void RunFromMenu() { Execute(); }

    public static string Execute() {
        var sb = new StringBuilder();
        int failures = 0;

        int frames = SRC_RATE / 8;              // an eighth of a second, so repeats are clearly separated
        var src = new float[frames * 2];
        for (int i = 0; i < frames; i++) {
            float t = (float)i / SRC_RATE;
            float env = 1f - (float)i / frames;  // a decaying blip, so each repeat is recognisable
            float s = 0.5f * env * Mathf.Sin(2f * Mathf.PI * 330f * t);
            src[i * 2] = s;
            src[i * 2 + 1] = s * 0.8f;
        }

        // ───────────────────────── retriggering ─────────────────────────
        sb.Append("=== RETRIGGERING ===\n");

        var plan = new RepeatPlan {
            enabled = true,
            count = 4,
            intervalSamples = SR / 5,            // a repeat every fifth of a second
            spaceFromEnd = false,
            retrigger = true,
            pitchMulMin = 0.9f, pitchMulMax = 1.1f,
            gainMulMin = 0.7f, gainMulMax = 1f,
            durationLimitSamples = 0,
            nominalLengthSamples = frames,
        };

        var single = Render(null, src, null, 0, 0f, out int singleFrames);
        var repeated = Render(null, src, plan, 0, 0f, out int repeatFrames);

        int singleOnsets = Onsets(single, singleFrames);
        int repeatOnsets = Onsets(repeated, repeatFrames);
        sb.Append("one play:      frames=").Append(singleFrames).Append("  bursts detected=").Append(singleOnsets).Append('\n');
        sb.Append("four repeats:  frames=").Append(repeatFrames).Append("  bursts detected=").Append(repeatOnsets).Append('\n');

        if (repeatOnsets != 4) { sb.Append("   <<< expected 4 bursts, got ").Append(repeatOnsets).Append('\n'); failures++; }
        if (repeatFrames <= singleFrames) { sb.Append("   <<< a repeat train should outlast a single play\n"); failures++; }

        // The same train rendered through the compiled path must agree with the uncompiled one, since the
        // schedule is arithmetic on sample counts and should not be affected by the compiler at all.
        var repeatedCompiled = RenderCompiled(null, src, plan, out int rcFrames);
        float trainDiff = Worst(repeated, repeatedCompiled, Mathf.Min(repeatFrames, rcFrames));
        sb.Append("compiled vs uncompiled train: frames=").Append(rcFrames)
          .Append("  worst difference=").Append(trainDiff.ToString("R")).Append('\n');
        if (rcFrames != repeatFrames) { sb.Append("   <<< train length differs between compilers\n"); failures++; }
        if (trainDiff > 1e-5f) { sb.Append("   <<< train differs by more than last-place rounding\n"); failures++; }

        // ───────────────────────── live changes ─────────────────────────
        sb.Append("\n=== LIVE CHANGES TO A PLAYING SOUND ===\n");

        var quiet = new ZoundEffectChain();
        var qn = new ZoundEffectNode(ZoundEffectType.Gain);
        qn.p[0] = 0.5f;
        quiet.nodes.Add(qn);

        var unity = new ZoundEffectChain();
        unity.nodes.Add(new ZoundEffectNode(ZoundEffectType.Gain));   // gain of one

        var authored = Render(quiet, src, null, 0, 0f, out int aFrames);
        var untouched = Render(unity, src, null, 0, 0f, out int uFrames);
        var atStart = Render(unity, src, null, 0, 0.5f, out int sFrames);
        var midway = Render(unity, src, null, 4, 0.5f, out int mFrames);

        failures += Check(sb, "gain set before the first block equals authoring it",
                          Worst(authored, atStart, Mathf.Min(aFrames, sFrames)), 0f);
        failures += Check(sb, "untouched render differs from the authored one (the test would be vacuous otherwise)",
                          -Worst(authored, untouched, Mathf.Min(aFrames, uFrames)), -1e-3f);
        failures += Check(sb, "gain set part way matches the untouched render before that point",
                          Worst(untouched, midway, 4 * BLOCK), 0f);
        failures += Check(sb, "gain set part way matches the authored render after that point",
                          WorstFrom(authored, midway, 4 * BLOCK, Mathf.Min(aFrames, mFrames)), 0f);

        // Pitch is deliberately NOT instant, so it must not be tested as if it were. An abrupt jump in playback
        // rate is an audible click, so a live pitch change is glided towards over a short time, and the play
        // length was already resolved on the main thread from the pitch the sound STARTED at — a live change
        // does not retroactively rewrite how long the sound is. So a live change to a given pitch cannot equal
        // having started at that pitch, and an earlier version of this probe wrongly asserted that it would.
        //
        // What is true, and decidable: a live pitch change must alter the output, and must not alter anything
        // rendered before it was applied.
        var pitchUntouched = RenderPitch(unity, src, 1f, 0, out int puFrames);
        // Applied two blocks in, NOT late: this source is only about six blocks long, and an earlier
        // version applied the change after the source had already run out, where it correctly did nothing.
        var pitchLateChange = RenderPitch(unity, src, 1f, 2, out int pcFrames, livePitch: 0.75f);
        failures += Check(sb, "a live pitch change does alter the sound",
                          -Worst(pitchUntouched, pitchLateChange, Mathf.Min(puFrames, pcFrames)), -1e-3f);
        failures += Check(sb, "a live pitch change leaves everything before it untouched",
                          Worst(pitchUntouched, pitchLateChange, 2 * BLOCK), 0f);

        // A parameter a modifier drives must refuse a live change.
        var modulated = new ZoundEffectChain();
        modulated.nodes.Add(new ZoundEffectNode(ZoundEffectType.Gain));
        var mod = new ZoundModifier(ZoundModifierType.Lfo);
        mod.p[0] = 0.5f; mod.p[1] = 7f;
        modulated.modifiers.Add(mod);
        modulated.bindings.Add(new ZoundModifierBinding {
            modifierIndex = 0, nodeIndex = 0, paramIndex = 0, op = ModifierOp.Multiply, depth = 1f,
        });
        var modPlain = Render(modulated, src, null, 0, 0f, out int mpFrames);
        var modPoked = Render(modulated, src, null, 2, 0.5f, out int mkFrames);
        failures += Check(sb, "a live change to a modifier-driven parameter is refused",
                          Worst(modPlain, modPoked, Mathf.Min(mpFrames, mkFrames)), 0f);

        sb.Append(failures == 0 ? "\nVERDICT: every check passed.\n" : "\nVERDICT: " + failures + " check(s) FAILED.\n");
        var result = sb.ToString();
        Debug.Log("[ZoundsSapLiveAndRepeatProbe]\n" + result);
        return result;
    }

    static int Check(StringBuilder sb, string what, float measured, float limit) {
        bool ok = measured <= limit;
        sb.Append(ok ? "  ok   " : "  FAIL ").Append(what)
          .Append("   (measured ").Append(measured.ToString("R")).Append(", limit ").Append(limit.ToString("R")).Append(")\n");
        return ok ? 0 : 1;
    }

    // ───────────────────────── rendering helpers ─────────────────────────

    static PcmClip Clip(float[] src) {
        var pcm = new PcmClip { channels = 2, frequency = SRC_RATE, frames = src.Length / 2, samples = src, valid = true };
        float p = 0f;
        for (int i = 0; i < src.Length; i++) { float a = src[i] < 0 ? -src[i] : src[i]; if (a > p) p = a; }
        pcm.peak = p;
        return pcm;
    }

    /// Renders through the voice, optionally arming repeats and optionally applying a live gain change after
    /// <paramref name="afterBlocks"/> blocks. A change value of zero means "do not apply one".
    static float[] Render(ZoundEffectChain chain, float[] src, RepeatPlan? plan, int afterBlocks, float liveGain, out int written) {
        return Core(chain, src, plan, afterBlocks, liveGain, 1f, 0f, out written);
    }

    static float[] RenderPitch(ZoundEffectChain chain, float[] src, float basePitch, int afterBlocks, out int written, float livePitch = 0f) {
        return Core(chain, src, null, afterBlocks, 0f, basePitch, livePitch, out written);
    }

    static float[] Core(ZoundEffectChain chain, float[] src, RepeatPlan? plan, int afterBlocks,
                       float liveGain, float basePitch, float livePitch, out int written) {
        var pcm = Clip(src);
        var layout = chain != null && !chain.IsEmpty ? ChainLayout.Build(chain, SR) : ChainLayout.Empty;
        int totalFrames = Mathf.CeilToInt(SECONDS * SR);
        var outBuf = new float[totalFrames];

        var voice = SapRealtimeVoice.Create(pcm, layout, SR, 0d, pcm.frames, basePitch, 1f,
                                            (float)pcm.frames / SRC_RATE / Mathf.Max(basePitch, 0.01f),
                                            false, 1, true, Allocator.Persistent);
        if (plan.HasValue) { var p = plan.Value; voice.SetRepeat(in p); }

        int w = 0, blocks = 0;
        try {
            if (afterBlocks == 0) ApplyLive(ref voice, liveGain, livePitch);
            while (w < totalFrames && !voice.finished) {
                if (blocks == afterBlocks && afterBlocks > 0) ApplyLive(ref voice, liveGain, livePitch);
                int n = Mathf.Min(BLOCK, totalFrames - w);
                voice.RenderBlock(n);
                for (int i = 0; i < n; i++) outBuf[w + i] = voice.sap.bufL[i];
                w += n;
                blocks++;
            }
        }
        finally { voice.Dispose(); }
        written = w;
        return outBuf;
    }

    static void ApplyLive(ref SapRealtimeVoice voice, float liveGain, float livePitch) {
        // The gain node's own first parameter, found through the layout rather than assumed, so this does not
        // silently target the wrong slot if the flat layout ever changes.
        if (liveGain != 0f && voice.chain.nodeCount > 0) {
            int flat = voice.chain.paramOffset[0];
            voice.Apply(SapVoiceCommand.Parameter(flat, liveGain));
        }
        if (livePitch != 0f) voice.Apply(SapVoiceCommand.Pitch(livePitch));
    }

    /// Same render, but through the compiled job.
    static float[] RenderCompiled(ZoundEffectChain chain, float[] src, RepeatPlan? plan, out int written) {
        var pcm = Clip(src);
        var layout = chain != null && !chain.IsEmpty ? ChainLayout.Build(chain, SR) : ChainLayout.Empty;
        int totalFrames = Mathf.CeilToInt(SECONDS * SR);
        var outBuf = new float[totalFrames];

        var voice = SapRealtimeVoice.Create(pcm, layout, SR, 0d, pcm.frames, 1f, 1f,
                                            (float)pcm.frames / SRC_RATE, false, 1, true, Allocator.Persistent);
        if (plan.HasValue) { var p = plan.Value; voice.SetRepeat(in p); }

        var jL = new NativeArray<float>(totalFrames, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        var jR = new NativeArray<float>(totalFrames, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        var tally = new NativeArray<int>(2, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        try {
            new SapVoiceRenderJob {
                voice = voice, outLeft = jL, outRight = jR,
                totalFrames = totalFrames, blockFrames = BLOCK, tally = tally,
            }.Run();
            written = tally[0];
            for (int i = 0; i < totalFrames; i++) outBuf[i] = jL[i];
        }
        finally { voice.Dispose(); jL.Dispose(); jR.Dispose(); tally.Dispose(); }
        return outBuf;
    }

    static float Worst(float[] a, float[] b, int n) => WorstFrom(a, b, 0, n);

    static float WorstFrom(float[] a, float[] b, int from, int to) {
        float worst = 0f;
        for (int i = from; i < to && i < a.Length && i < b.Length; i++) {
            float d = a[i] - b[i];
            if (d < 0) d = -d;
            if (d > worst) worst = d;
        }
        return worst;
    }

    /// Counts distinct bursts, on a short-window ENVELOPE rather than on raw samples.
    ///
    /// Counting raw samples does not work and the first version of this got it wrong: a tone crosses back
    /// through zero twice per cycle, so a rise-and-fall detector applied to the waveform counts oscillations,
    /// not events — it reported fifty-eight bursts for a single blip. Taking the peak of each short window
    /// first collapses the oscillation and leaves the shape of the sound, which is what "how many times did it
    /// play" is actually asking about.
    static int Onsets(float[] buf, int n) {
        const int WINDOW = 240;               // five milliseconds at this rate: shorter than any gap, longer than a cycle
        int count = 0;
        bool armed = true;
        for (int start = 0; start + WINDOW <= n; start += WINDOW) {
            float peak = 0f;
            for (int i = start; i < start + WINDOW; i++) {
                float a = buf[i] < 0 ? -buf[i] : buf[i];
                if (a > peak) peak = a;
            }
            if (armed && peak > 0.05f) { count++; armed = false; }
            else if (!armed && peak < 0.01f) armed = true;
        }
        return count;
    }
}
