using System;
using System.Text;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Laubrary.Audio;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

/// <summary>Kept, asset-free check. Invoke Execute through reflection/CLI; deliberately has no menu item.</summary>
public static class AudioCoreChainCheck {
    const int Rate = 48000, Frames = 16384;

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict)]
    struct ChainJob : IJob {
        public AudioChainProcessor processor;
        public SapChainLayout layout;
        public NativeArray<float> left, right;
        public NativeArray<int> compiled;
        public int split;
        [BurstDiscard] void Managed() { compiled[0] = 0; }
        public void Execute() {
            compiled[0] = 1; Managed();
            int off = 0;
            while (off < left.Length) {
                int n = Math.Min(split, left.Length - off);
                var context = Context(off);
                processor.Process(in layout, left, right, off, n, in context);
                off += n;
            }
            AudioThreadGuard.CountBlock();
        }
    }

    static ChainProcessContext Context(int off) => new ChainProcessContext {
        effects = new VoiceContext { sampleRate = Rate, sourceDuration = (float)Frames / Rate, sourcePeak = 1f },
        followSource = true, sourceProgress = (double)off / Frames, sourceProgressPerSample = 1d / Frames,
    };

    public static string Execute() {
        var report = new StringBuilder();
        var src = new float[Frames * 2];
        for (int i = 0; i < Frames; i++) { src[2 * i] = i == 0 ? 1f : 0.2f * Mathf.Sin(i * 0.057f); src[2 * i + 1] = 0.17f * Mathf.Cos(i * 0.073f); }
        double worstManaged = 0, worstCompiled = 0, worstSplit = 0;
        for (int i = 0; i < 16; i++) {
            var chain = new ZoundEffectChain(); chain.nodes.Add(new ZoundEffectNode((ZoundEffectType)i));
            Push(chain.nodes[0]);
            Compare(chain, src, report, ref worstManaged, ref worstCompiled, ref worstSplit);
        }
        var mixed = new ZoundEffectChain();
        for (int i = 0; i < 16; i++) { var node = new ZoundEffectNode((ZoundEffectType)i); Push(node); mixed.nodes.Add(node); }
        AddModifiers(mixed);
        Compare(mixed, src, report, ref worstManaged, ref worstCompiled, ref worstSplit);
        var modulatedGain = new ZoundEffectChain(); modulatedGain.nodes.Add(new ZoundEffectNode(ZoundEffectType.Gain)); AddModifiers(modulatedGain);
        Compare(modulatedGain, src, report, ref worstManaged, ref worstCompiled, ref worstSplit);
        Require(worstManaged <= 1e-5 && worstCompiled <= 1e-5 && worstSplit <= 1e-5, "chain parity ceiling exceeded\n" + report);
        report.AppendLine("PASS managed/voice worst=" + worstManaged.ToString("R") + " compiled/voice worst=" + worstCompiled.ToString("R") + " split worst=" + worstSplit.ToString("R"));
        return report.ToString();
    }

    static void Push(ZoundEffectNode n) {
        switch (n.type) {
            case ZoundEffectType.Gain: n.p[0] = 0.8f; break;
            case ZoundEffectType.Delay: n.p[0] = 49.7f; n.p[2] = 0.5f; break;
            case ZoundEffectType.Reverb: n.p[3] = 0.6f; break;
            case ZoundEffectType.LowPass: n.p[0] = 2600f; break;
            case ZoundEffectType.HighPass: n.p[0] = 250f; break;
            case ZoundEffectType.Limiter: n.p[0] = -20f; break;
            case ZoundEffectType.Compressor: n.p[0] = -24f; break;
            case ZoundEffectType.EQ: n.p[1] = 4f; break;
            case ZoundEffectType.Fade: n.p[0] = 0.04f; n.p[1] = 0.06f; break;
            case ZoundEffectType.TransientShaper: n.p[0] = 6f; n.p[1] = -3f; break;
        }
    }

    static void AddModifiers(ZoundEffectChain c) {
        foreach (var type in new[] { ZoundModifierType.Envelope, ZoundModifierType.Lfo, ZoundModifierType.Random, ZoundModifierType.Step, ZoundModifierType.Code, ZoundModifierType.Lfo, ZoundModifierType.Step }) {
            var m = new ZoundModifier(type);
            var points = m.curve.GetPointsList(); points.Clear();
            points.Add(new ZUIEnvelopePoint(0f, 0.3f));
            points.Add(new ZUIEnvelopePoint(0.5f, 0.7f, 0.7f) { randomX = 0.1f, randomY = 0.15f });
            points.Add(new ZUIEnvelopePoint(1f, 0.4f, 1.8f));
            if (type == ZoundModifierType.Step) { m.p[0] = (float)StepTiming.PerInterval; m.p[1] = 11f; m.p[2] = (float)StepOrder.RoundRobinNoRepeat; m.p[5] = 0.6f; m.steps = new[] { 0.2f, 0.7f, -0.3f, 0.5f }; }
            if (c.modifiers.Count == 5) { m.p[4] = (float)LfoMode.Random; m.p[1] = 8f; m.p[5] = 0.02f; }
            int index = c.modifiers.Count; c.modifiers.Add(m);
            c.bindings.Add(new ZoundModifierBinding { modifierIndex = index, nodeIndex = index == 5 && c.nodes.Count > 5 ? 5 : 0, paramIndex = 0, combine = index == 0 ? ModulationCombine.Set : ModulationCombine.Shift, schema = 2, depth = 0.12f });
        }
    }

    static void Compare(ZoundEffectChain chain, float[] src, StringBuilder report, ref double worstManaged, ref double worstCompiled, ref double worstSplit) {
        var layout = ChainLayout.Build(chain, Rate);
        var pcm = new PcmClip { channels = 2, frequency = Rate, frames = Frames, samples = src, valid = true, peak = 1f };
        var voice = SapRealtimeVoice.Create(pcm, layout, Rate, 0, Frames, 1f, 1f, (float)Frames / Rate, false, 123, true, Allocator.TempJob);
        var flat = ZoundsAudioCoreLayout.Create(layout, Allocator.TempJob);
        var processor = AudioChainProcessor.Create(in flat, Rate, Allocator.TempJob, voice.sap.rng, voice.sap.curveSeed);
        var left = new NativeArray<float>(Frames, Allocator.TempJob); var right = new NativeArray<float>(Frames, Allocator.TempJob);
        var referenceL = new NativeArray<float>(Frames, Allocator.TempJob); var referenceR = new NativeArray<float>(Frames, Allocator.TempJob);
        var compiled = new NativeArray<int>(1, Allocator.TempJob); var tally = new NativeArray<int>(2, Allocator.TempJob);
        var seedArena = new NativeArray<float>(Math.Max(1, layout.stateFloats), Allocator.TempJob);
        try {
            // Free-running step state supplied by the host, with no asset-keyed clock/history in AudioCore.
            if (layout.modCount == 7) { int s = layout.modStateOffset[6]; voice.sap.arena[s + 5] = 1f; voice.sap.arena[s + 6] = 912f; }
            Seed(ref processor, in voice, in flat);
            for (int off = 0; off < Frames; off += 256) {
                if (off == 1024 && layout.modCount > 4) { voice.sap.modCtlTarget[4] = 0.9f; processor.modCtlTarget[4] = 0.9f; }
                Fill(left, right, src, off, 256);
                var context = Context(off); processor.Process(in flat, left, right, off, 256, in context);
                voice.RenderBlock(256);
                for (int j = 0; j < 256; j++) { referenceL[off + j] = voice.sap.bufL[j]; referenceR[off + j] = voice.sap.bufR[j]; }
            }
            double managed = Difference(left, right, referenceL, referenceR); worstManaged = Math.Max(worstManaged, managed);
            // Restart both with identical native modifier seeds; compiled comparison deliberately uses the same registered job as Zounds.
            voice.Dispose(); voice = SapRealtimeVoice.Create(pcm, layout, Rate, 0, Frames, 1f, 1f, (float)Frames / Rate, false, 123, true, Allocator.TempJob);
            if (layout.modCount == 7) { int s = layout.modStateOffset[6]; voice.sap.arena[s + 5] = 1f; voice.sap.arena[s + 6] = 912f; }
            processor.Reset(in flat, Rate, voice.sap.rng, voice.sap.curveSeed); Seed(ref processor, in voice, in flat);
            for (int i = 0; i < layout.stateFloats; i++) seedArena[i] = processor.arena[i];
            uint seedRng = processor.rng, seedCurve = processor.curveSeed;
            new SapVoiceRenderJob { voice = voice, totalFrames = Frames, blockFrames = 256, outLeft = referenceL, outRight = referenceR, tally = tally }.Run();
            var voiceGridL = referenceL.ToArray(); var voiceGridR = referenceR.ToArray();
            Fill(left, right, src, 0, Frames);
            long blocksBefore = AudioThreadGuard.Blocks, managedBefore = AudioThreadGuard.ManagedBlocks;
            new ChainJob { processor = processor, layout = flat, left = left, right = right, compiled = compiled, split = 333 }.Run();
            Require(compiled[0] == 1 && AudioThreadGuard.Blocks == blocksBefore + 1 && AudioThreadGuard.ManagedBlocks == managedBefore, "processor Burst/guard proof failed");
            var splitL = left.ToArray(); var splitR = right.ToArray();
            processor.Reset(in flat, Rate, seedRng, seedCurve);
            for (int i = 0; i < layout.stateFloats; i++) processor.arena[i] = seedArena[i];
            Fill(left, right, src, 0, Frames);
            new ChainJob { processor = processor, layout = flat, left = left, right = right, compiled = compiled, split = 256 }.Run();
            double burstGrid = Difference(left, right, referenceL, referenceR);
            voice.Dispose(); voice = SapRealtimeVoice.Create(pcm, layout, Rate, 0, Frames, 1f, 1f, (float)Frames / Rate, false, 123, true, Allocator.TempJob);
            for (int i = 0; i < layout.stateFloats; i++) voice.sap.arena[i] = seedArena[i];
            voice.sap.rng = seedRng; voice.sap.curveSeed = seedCurve;
            new SapVoiceRenderJob { voice = voice, totalFrames = Frames, blockFrames = 333, outLeft = referenceL, outRight = referenceR, tally = tally }.Run();
            double burstOdd = 0, splitParity = 0, partitionDifference = 0;
            for (int i = 0; i < Frames; i++) {
                burstOdd = Math.Max(burstOdd, Math.Max(Math.Abs(referenceL[i] - splitL[i]), Math.Abs(referenceR[i] - splitR[i])));
                double coreDeltaL = left[i] - splitL[i], coreDeltaR = right[i] - splitR[i];
                double voiceDeltaL = voiceGridL[i] - referenceL[i], voiceDeltaR = voiceGridR[i] - referenceR[i];
                splitParity = Math.Max(splitParity, Math.Max(Math.Abs(coreDeltaL - voiceDeltaL), Math.Abs(coreDeltaR - voiceDeltaR)));
                partitionDifference = Math.Max(partitionDifference, Math.Max(Math.Abs(voiceDeltaL), Math.Abs(voiceDeltaR)));
            }
            double burst = Math.Max(burstGrid, burstOdd); worstCompiled = Math.Max(worstCompiled, burst); worstSplit = Math.Max(worstSplit, splitParity);
            // Allocation measurement excludes preparation and scheduling; no layout/state changes while rendering.
            var allocContext = Context(0); processor.Process(in flat, left, right, 0, 64, in allocContext);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 400; i++) processor.Process(in flat, left, right, 0, 64, in allocContext);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Require(bytes == 0, "processor allocated " + bytes + " bytes");
            Require(processor.arena.Length == Math.Max(1, flat.stateFloats), "processor arena exceeds layout size");
            report.AppendLine((layout.modCount > 0 ? layout.nodeCount + " effects + seven modifiers" : chain.nodes[0].type.ToString()) + " managed=" + managed.ToString("R") + " compiled=" + burst.ToString("R") + " splitParity=" + splitParity.ToString("R") + " voicePartitionDifference=" + partitionDifference.ToString("R") + " alloc=" + bytes + " Burst=True");
        }
        finally { voice.Dispose(); processor.Dispose(); flat.Dispose(); left.Dispose(); right.Dispose(); referenceL.Dispose(); referenceR.Dispose(); compiled.Dispose(); tally.Dispose(); seedArena.Dispose(); }
    }

    static void Seed(ref AudioChainProcessor p, in SapRealtimeVoice v, in SapChainLayout l) {
        for (int i = 0; i < l.stateFloats; i++) p.arena[i] = v.sap.arena[i];
        p.rng = v.sap.rng; p.curveSeed = v.sap.curveSeed;
    }
    static void Fill(NativeArray<float> l, NativeArray<float> r, float[] src, int off, int n) { for (int i = off; i < off + n; i++) { l[i] = src[2 * i]; r[i] = src[2 * i + 1]; } }
    static double Difference(NativeArray<float> l, NativeArray<float> r, NativeArray<float> a, NativeArray<float> b) {
        double worst = 0; for (int i = 0; i < l.Length; i++) { Require(!float.IsNaN(l[i]) && !float.IsNaN(r[i]) && !float.IsInfinity(l[i]) && !float.IsInfinity(r[i]), "nonfinite output"); worst = Math.Max(worst, Math.Max(Math.Abs(l[i] - a[i]), Math.Abs(r[i] - b[i]))); } return worst;
    }
    static void Require(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
}
