// A kept check, runnable from the Laubrary menu — establishes whether the per-block render allocates managed memory, which the earlier
// attempt at this could not.
//
// Why the earlier figure was worthless: it sampled the whole managed heap around a loop that was ALSO building
// report text between captures, in an editor that allocates on its own. Identical engine code reported anything
// between nothing and a hundred and eighty kilobytes purely depending on what else was happening.
//
// What this does instead: one chain at a time, nothing else running, no text built inside the measured region,
// a warm-up pass first so anything that only allocates once has already done it, a full collection immediately
// before, and a long run of blocks so a per-block allocation of even a few bytes would accumulate into an
// obvious number. Each chain is measured several times and the LOWEST reading is the one judged, with all of them
// shown so the spread is visible.
//
// Why the lowest: other activity in the process can only ever ADD to a heap reading, never subtract, so a genuine
// per-block allocation appears in every repeat while noise does not. A single-shot control run that rendered nothing
// was tried first and was not good enough — it finished too quickly to overlap a busy period, so it reported a clean
// floor while the real measurements were being inflated by hundreds of kilobytes.
//
// Note on what this can and cannot show. The compiled path cannot allocate managed memory at all — that is a
// property of compiled code, not something to measure. So the measurement here is of the UNCOMPILED path, which
// is what the offline renderer and editor previews run, and which is also the version whose allocation
// behaviour a person can accidentally change while editing.
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

public static class ZoundsSapAllocationProof {

    const int SR = 48000;
    const int BLOCK = 1024;
    const int MEASURED_BLOCKS = 400;      // over eight seconds of audio; a few bytes per block would show as kilobytes
    const int WARMUP_BLOCKS = 8;

    [MenuItem("Laubrary/Zounds/Checks/5 - Does the render allocate")]
    public static void RunFromMenu() { Execute(); }

    public static string Execute() {
        int frames = SR / 4;
        var src = new float[frames * 2];
        for (int i = 0; i < frames; i++) {
            float t = (float)i / SR;
            float s = 0.4f * Mathf.Sin(2f * Mathf.PI * 180f * t);
            src[i * 2] = s;
            src[i * 2 + 1] = s * 0.9f;
        }

        var cases = new List<KeyValuePair<string, ZoundEffectChain>>();
        cases.Add(new KeyValuePair<string, ZoundEffectChain>("no effects", null));
        cases.Add(new KeyValuePair<string, ZoundEffectChain>("delay", Single(ZoundEffectType.Delay)));
        cases.Add(new KeyValuePair<string, ZoundEffectChain>("reverb", Single(ZoundEffectType.Reverb)));
        cases.Add(new KeyValuePair<string, ZoundEffectChain>("all sixteen effects", All()));
        cases.Add(new KeyValuePair<string, ZoundEffectChain>("gain driven by an oscillator", Modulated()));

        // Measure everything first, build the report afterwards: nothing may allocate between a baseline
        // reading and its matching final reading.
        var names = new string[cases.Count];
        var blocksRun = new int[cases.Count];

        // One whole measurement run first, discarded. The very first measurement of a session picks up costs that
        // belong to the session rather than to the render — the heap growing by a segment, one-time setup inside
        // the measuring code itself — and an earlier version of this probe reported exactly one such figure,
        // always on whichever chain happened to be measured first. Throwing the first one away moves that cost
        // out of the results instead of leaving it to be argued about.
        Measure(cases[0].Value, src, out _);

        const int REPEATS = 3;
        var best = new long[cases.Count];
        var spread = new string[cases.Count];
        for (int c = 0; c < cases.Count; c++) {
            names[c] = cases[c].Key;
            long lowest = long.MaxValue;
            var seen = new StringBuilder();
            for (int r = 0; r < REPEATS; r++) {
                long bytes = Measure(cases[c].Value, src, out int blocks);
                if (r == 0) blocksRun[c] = blocks;
                if (bytes < lowest) lowest = bytes;
                if (r > 0) seen.Append(", ");
                seen.Append(bytes);
            }
            best[c] = lowest;
            spread[c] = seen.ToString();
        }



        var sb = new StringBuilder();
        sb.Append("=== DOES THE PER-BLOCK RENDER ALLOCATE? ===\n");
        sb.Append(MEASURED_BLOCKS).Append(" blocks of ").Append(BLOCK).Append(" frames per measurement, after ")
          .Append(WARMUP_BLOCKS).Append(" warm-up blocks and a full collection\n\n");
        int bad = 0;
        for (int c = 0; c < cases.Count; c++) {
            // The LOWEST of several repeats. Other activity in the process can only ever ADD to a heap reading, so a
            // genuine per-block allocation would show up in every repeat, and the minimum is the closest this kind of
            // measurement can get to the engine's own cost. A one-shot control run was tried first and was not good
            // enough: it finished too quickly to overlap the busy period and reported a clean floor while the real
            // measurements were being inflated.
            bool clean = best[c] <= 0;
            if (!clean) bad++;
            sb.Append(clean ? "  clean " : "  ALLOC ").Append(names[c].PadRight(30))
              .Append(" lowest of ").Append(REPEATS).Append(" = ").Append(best[c].ToString().PadLeft(8)).Append(" bytes")
              .Append("   (saw: ").Append(spread[c]).Append(")\n");
        }
        sb.Append(bad == 0
            ? "\nVERDICT: no measurable managed allocation in the per-block render, on any chain.\n"
            : "\nVERDICT: " + bad + " chain(s) allocated in EVERY repeat — that is the engine, worth investigating.\n");

        var result = sb.ToString();
        Debug.Log("[ZoundsSapAllocationProof]\n" + result);
        return result;
    }

    static ZoundEffectChain Single(ZoundEffectType type) {
        var chain = new ZoundEffectChain();
        chain.nodes.Add(new ZoundEffectNode(type));
        return chain;
    }

    static ZoundEffectChain All() {
        var chain = new ZoundEffectChain();
        int count = Math.Min(ZoundEffectDescriptors.EffectTypeCount, ZoundDspConstants.MAX_NODES);
        for (int i = 0; i < count; i++) chain.nodes.Add(new ZoundEffectNode((ZoundEffectType)i));
        return chain;
    }

    static ZoundEffectChain Modulated() {
        var chain = new ZoundEffectChain();
        chain.nodes.Add(new ZoundEffectNode(ZoundEffectType.Gain));
        var mod = new ZoundModifier(ZoundModifierType.Lfo);
        mod.p[0] = 0.5f; mod.p[1] = 7f;
        chain.modifiers.Add(mod);
        chain.bindings.Add(new ZoundModifierBinding {
            modifierIndex = 0, nodeIndex = 0, paramIndex = 0, op = ModifierOp.Multiply, depth = 1f,
        });
        return chain;
    }

    /// Renders a fixed number of blocks and returns how much the managed heap grew across them.
    static long Measure(ZoundEffectChain chain, float[] src, out int blocks) {
        var pcm = new PcmClip { channels = 2, frequency = SR, frames = src.Length / 2, samples = src, valid = true };
        float peak = 0f;
        for (int i = 0; i < src.Length; i++) { float a = src[i] < 0 ? -src[i] : src[i]; if (a > peak) peak = a; }
        pcm.peak = peak;

        var layout = chain != null && !chain.IsEmpty ? ChainLayout.Build(chain, SR) : ChainLayout.Empty;

        // Looping, so the source never runs out and every measured block does real work. A voice that finished
        // early would render silence and prove nothing about the busy case.
        var voice = SapRealtimeVoice.Create(pcm, layout, SR, 0d, pcm.frames, 1f, 1f,
                                            (float)pcm.frames / SR, true, 1, true, Allocator.Persistent);
        blocks = 0;
        try {
            for (int i = 0; i < WARMUP_BLOCKS; i++) voice.RenderBlock(BLOCK);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetTotalMemory(false);

            for (int i = 0; i < MEASURED_BLOCKS; i++) {
                voice.RenderBlock(BLOCK);
                blocks++;
            }

            return GC.GetTotalMemory(false) - before;
        }
        finally { voice.Dispose(); }
    }
}
