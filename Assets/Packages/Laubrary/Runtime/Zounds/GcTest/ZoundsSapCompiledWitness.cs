using System.Text;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds.Checks {

    /// <summary>
    /// Answers one question that nothing else can: **is the audio chain actually running as compiled native
    /// code right now, in this build, on this machine — or has it silently fallen back to ordinary code?**
    ///
    /// **Why that question needs a special answer.** A fallback is invisible. The compiled and uncompiled
    /// versions are the same function, so the sound is right either way; the only difference is that the
    /// uncompiled one re-attaches the audio thread to the scripting runtime and brings back the freezes this
    /// whole engine exists to avoid. It already happened once during development, and produced a perfect
    /// passing result. So "it sounds fine" and "the numbers match" are both consistent with total failure.
    ///
    /// **How this tells them apart without needing to know anything in advance.** It renders the same chains
    /// twice — once through the ordinary path, once through the compiled path — and compares. Compiled and
    /// uncompiled floating-point arithmetic differ very slightly, and three chains are used whose differences
    /// have been measured and are reliably non-zero. So:
    ///
    ///   * any difference at all  ->  the compiled path is genuinely compiled;
    ///   * every chain identical  ->  the compiled path is running as ordinary code.
    ///
    /// That inverts the usual instinct, and it is worth stating plainly: **here, a perfect match is the failure
    /// signal.** Nothing is hardcoded, so it stays valid on any processor and in any build, unlike comparing
    /// against remembered numbers — compiled code is generated per processor, so remembered values would not
    /// travel.
    ///
    /// Safe to run anywhere: it needs no audio device, no listener and no assets, so it works in a headless
    /// build as well as in the editor.
    /// </summary>
    public static class ZoundsSapCompiledWitness {

        public struct Result {
            /// <summary>True when at least one chain rendered differently, i.e. compilation is real.</summary>
            public bool compiled;
            /// <summary>Whether the compiler reports itself switched on. A necessary condition, not a sufficient one.</summary>
            public bool compilerSaysEnabled;
            /// <summary>Largest difference seen across the discriminating chains.</summary>
            public float largestDifference;
            public string report;
        }

        private const int SR = 48000;
        private const int BLOCK = 1024;
        private const float SECONDS = 0.6f;

        public static Result Run() {
            var sb = new StringBuilder();
            sb.Append("=== IS THE AUDIO CHAIN REALLY COMPILED? ===\n");
            sb.Append("compiler reports itself enabled: ").Append(BurstCompiler.IsEnabled ? "yes" : "NO").Append('\n');
            sb.Append("(a perfect match below is the FAILURE signal - see the note in this file)\n\n");

            var src = Source(out int frames);
            float largest = 0f;

            // Three chains whose compiled and uncompiled results are known to differ. A delay whose time lands on
            // a whole number of samples is the most sensitive of them, because its read position then straddles an
            // exact integer and a last-place difference lands on opposite sides of it.
            largest = Mathf.Max(largest, One(sb, "delay on a sample boundary", DelayChain(), src, frames));
            largest = Mathf.Max(largest, One(sb, "reverb", ReverbChain(), src, frames));
            largest = Mathf.Max(largest, One(sb, "gain driven by an oscillator", ModulatedChain(), src, frames));

            bool compiled = largest > 0f;
            sb.Append('\n').Append(compiled
                ? "VERDICT: COMPILED. The two paths differ, which only happens when one of them is genuinely compiled.\n"
                : "VERDICT: NOT COMPILED - running as ordinary code. Audio will freeze during garbage collection.\n");

            return new Result {
                compiled = compiled,
                compilerSaysEnabled = BurstCompiler.IsEnabled,
                largestDifference = largest,
                report = sb.ToString(),
            };
        }

        static float One(StringBuilder sb, string label, ZoundEffectChain chain, float[] src, int srcFrames) {
            var pcm = new PcmClip {
                channels = 2, frequency = SR, frames = srcFrames, samples = src, valid = true, peak = 1f,
            };
            var layout = ChainLayout.Build(chain, SR);
            int total = Mathf.CeilToInt(SECONDS * SR);
            float duration = (float)srcFrames / SR;

            // Ordinary path.
            var plain = new float[total];
            var a = SapRealtimeVoice.Create(pcm, layout, SR, 0d, srcFrames, 1f, 1f, duration, false, 1, true, Allocator.Persistent);
            int wroteA = 0;
            try {
                while (wroteA < total && !a.finished) {
                    int n = Mathf.Min(BLOCK, total - wroteA);
                    a.RenderBlock(n);
                    for (int i = 0; i < n; i++) plain[wroteA + i] = a.sap.bufL[i];
                    wroteA += n;
                }
            }
            finally { a.Dispose(); }

            // Compiled path, through the job that lives in the package assembly. It has to be that job: an
            // equivalent declared in a script compiled at runtime is not registered with the compiler and would
            // silently run as ordinary code, which is exactly the failure being tested for.
            var b = SapRealtimeVoice.Create(pcm, layout, SR, 0d, srcFrames, 1f, 1f, duration, false, 1, true, Allocator.Persistent);
            var jL = new NativeArray<float>(total, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var jR = new NativeArray<float>(total, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var tally = new NativeArray<int>(2, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            int wroteB;
            float worst = 0f;
            try {
                new SapVoiceRenderJob {
                    voice = b, outLeft = jL, outRight = jR, totalFrames = total, blockFrames = BLOCK, tally = tally,
                }.Run();
                wroteB = tally[0];
                int n = Mathf.Min(wroteA, wroteB);
                for (int i = 0; i < n; i++) {
                    float d = jL[i] - plain[i];
                    if (d < 0) d = -d;
                    if (d > worst) worst = d;
                }
            }
            finally { b.Dispose(); jL.Dispose(); jR.Dispose(); tally.Dispose(); }

            sb.Append("  ").Append(label.PadRight(30))
              .Append(" difference=").Append(worst.ToString("R"))
              .Append(worst > 0f ? "   (differs - good)" : "   (IDENTICAL - suspicious)")
              .Append('\n');
            return worst;
        }

        static float[] Source(out int frames) {
            frames = SR / 4;
            var src = new float[frames * 2];
            for (int i = 0; i < frames; i++) {
                // An impulse plus a tone. The impulse is the point: it puts a step in the signal exactly where a
                // delayed copy is read, which is what turns a last-place difference into a visible one.
                float s = (i == 0) ? 1f : 0f;
                s += 0.25f * Mathf.Sin(2f * Mathf.PI * 440f * i / SR);
                src[i * 2] = s;
                src[i * 2 + 1] = s * 0.5f;
            }
            return src;
        }

        static ZoundEffectChain DelayChain() {
            var c = new ZoundEffectChain();
            var n = new ZoundEffectNode(ZoundEffectType.Delay);
            n.p[0] = 250f; n.p[1] = 0.4f; n.p[2] = 0.5f; n.p[3] = 500f; n.p[4] = 0f;
            c.nodes.Add(n);
            return c;
        }

        static ZoundEffectChain ReverbChain() {
            var c = new ZoundEffectChain();
            var n = new ZoundEffectNode(ZoundEffectType.Reverb);
            n.p[0] = 0.5f; n.p[1] = 0.5f; n.p[2] = 1f; n.p[3] = 0.5f;
            c.nodes.Add(n);
            return c;
        }

        static ZoundEffectChain ModulatedChain() {
            var c = new ZoundEffectChain();
            c.nodes.Add(new ZoundEffectNode(ZoundEffectType.Gain));
            var m = new ZoundModifier(ZoundModifierType.Lfo);
            m.p[0] = 0.5f; m.p[1] = 7f;
            c.modifiers.Add(m);
            c.bindings.Add(new ZoundModifierBinding {
                modifierIndex = 0, nodeIndex = 0, paramIndex = 0, op = ModifierOp.Multiply, depth = 1f,
            });
            return c;
        }
    }
}
