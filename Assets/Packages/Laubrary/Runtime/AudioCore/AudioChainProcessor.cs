using System;
using Unity.Collections;
using Laubrary.Zounds;

namespace Laubrary.Audio {
    /// <summary>Effects elapsedSeconds is the clock origin at processor sample zero. Source progress is at the first sample of this call.</summary>
    public struct ChainProcessContext {
        public VoiceContext effects;
        public bool followSource;
        public double sourceProgress, sourceProgressPerSample;
    }

    /// <summary>Chain-only processing with owned, precisely sized state. The native layout is borrowed and must outlive rendering.</summary>
    public struct AudioChainProcessor : IDisposable {
        public NativeArray<float> arena, pLive, pStart, pStep, pTarget;
        public NativeArray<float> modValue, modCtlLive, modCtlTarget;
        public NativeArray<float> presence, presencePrev, dryL, dryR;
        public uint rng, curveSeed;
        public long elapsedSamples;

        public static AudioChainProcessor Create(in SapChainLayout layout, int sampleRate, Allocator allocator, uint randomSeed = 2463534242u, uint envelopeSeed = 0) {
            var p = new AudioChainProcessor {
                arena = Buffer(layout.stateFloats, allocator),
                pLive = Buffer(layout.paramCount, allocator), pStart = Buffer(layout.paramCount, allocator),
                pStep = Buffer(layout.paramCount, allocator), pTarget = Buffer(layout.paramCount, allocator),
                modValue = Buffer(layout.modCount, allocator), modCtlLive = Buffer(layout.modCount, allocator), modCtlTarget = Buffer(layout.modCount, allocator),
                presence = Buffer(layout.nodeCount, allocator), presencePrev = Buffer(layout.nodeCount, allocator),
                dryL = Buffer(AudioControl.BlockFrames, allocator), dryR = Buffer(AudioControl.BlockFrames, allocator),
            };
            p.Reset(in layout, sampleRate, randomSeed, envelopeSeed);
            return p;
        }

        static NativeArray<float> Buffer(int count, Allocator allocator) => new NativeArray<float>(Math.Max(1, count), allocator, NativeArrayOptions.ClearMemory);

        /// <summary>Reset on the owning thread before publishing. Hosts may then seed random/step/walk state explicitly.</summary>
        public void Reset(in SapChainLayout layout, int sampleRate, uint randomSeed, uint envelopeSeed) {
            for (int i = 0; i < arena.Length; i++) arena[i] = 0f;
            for (int i = 0; i < layout.paramCount; i++) { pLive[i] = pStart[i] = pTarget[i] = layout.pBase[i]; pStep[i] = 0f; }
            for (int i = 0; i < layout.modCount; i++) { modValue[i] = 0f; modCtlLive[i] = modCtlTarget[i] = layout.modCtlInit[i]; }
            for (int i = 0; i < layout.nodeCount; i++) presence[i] = presencePrev[i] = layout.enabled[i] ? 1f : 0f;
            rng = randomSeed; curveSeed = envelopeSeed; elapsedSamples = 0;
            ZoundEffects.ResetChain(in layout, arena, sampleRate);
        }

        /// <summary>Render-thread only. No source playback, source gain, output gain or completion detection is applied.</summary>
        public void Process(in SapChainLayout layout, NativeArray<float> left, NativeArray<float> right, int offset, int count, in ChainProcessContext context) {
            int consumed = 0;
            while (consumed < count) {
                int phase = (int)(elapsedSamples % AudioControl.BlockFrames);
                int n = Math.Min(count - consumed, AudioControl.BlockFrames - phase);
                var effects = context.effects;
                effects.elapsedSeconds += (float)elapsedSamples / effects.sampleRate;
                if (layout.bindCount > 0) {
                    if (phase == 0) {
                        var state = new ChainModulationState { arena = arena, modValue = modValue, modCtlLive = modCtlLive, modCtlTarget = modCtlTarget, rng = rng, curveSeed = curveSeed };
                        var modContext = new ModulationContext {
                            elapsedSeconds = effects.elapsedSeconds + (float)AudioControl.BlockFrames / effects.sampleRate,
                            sourceDuration = effects.sourceDuration, sourceExhausted = effects.sourceExhausted,
                            followSource = context.followSource,
                            sourceProgress = (float)(context.sourceProgress + (consumed + AudioControl.BlockFrames) * context.sourceProgressPerSample),
                        };
                        ChainModulation.EvaluateModifiers(ref state, in layout, effects.sampleRate, AudioControl.BlockFrames, in modContext);
                        rng = state.rng;
                        ChainModulation.PrepareTargets(in layout, modValue, modCtlLive, pLive, pStart, pStep, pTarget);
                    }
                    else for (int r = 0; r < layout.rampedCount; r++) { int t = layout.ramped[r]; pStart[t] = pLive[t]; }
                }
                ZoundEffects.ProcessChain(in layout, arena, pStart, pStep, left, right, offset + consumed, n, in effects, presence, presencePrev, dryL, dryR);
                for (int i = 0; i < layout.nodeCount; i++) presencePrev[i] = presence[i];
                for (int r = 0; r < layout.rampedCount; r++) { int t = layout.ramped[r]; pLive[t] = pStart[t] + pStep[t] * n; }
                elapsedSamples += n;
                consumed += n;
            }
        }

        /// <summary>Only after the host has stopped rendering and confirmed quiet. Struct copies borrow the same buffers.</summary>
        public void Dispose() {
            if (arena.IsCreated) arena.Dispose();
            if (pLive.IsCreated) pLive.Dispose(); if (pStart.IsCreated) pStart.Dispose();
            if (pStep.IsCreated) pStep.Dispose(); if (pTarget.IsCreated) pTarget.Dispose();
            if (modValue.IsCreated) modValue.Dispose(); if (modCtlLive.IsCreated) modCtlLive.Dispose(); if (modCtlTarget.IsCreated) modCtlTarget.Dispose();
            if (presence.IsCreated) presence.Dispose(); if (presencePrev.IsCreated) presencePrev.Dispose();
            if (dryL.IsCreated) dryL.Dispose(); if (dryR.IsCreated) dryR.Dispose();
            this = default;
        }
    }
}
