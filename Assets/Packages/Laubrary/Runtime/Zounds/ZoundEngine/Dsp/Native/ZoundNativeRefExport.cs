using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Laubrary.Zounds.Dsp.Native {

    /// <summary>
    /// Writes the managed engine's rendering of a set of chains to disk, as case files
    /// the native harness can replay and compare itself against.
    ///
    /// This exists because Unity holds the plugin DLL open: a freshly built engine
    /// cannot be loaded into the running editor, so an effect cannot be accepted by the
    /// in-editor equivalence probe while it is being written. Exporting the reference
    /// once breaks that dependency — after this runs, the whole port can be developed
    /// and verified against the managed engine with no editor in the loop at all.
    ///
    /// A case carries the chain as its serialized LAYOUT BLOB rather than as a chain
    /// description, so the harness cannot disagree with C# about what the chain was.
    /// The only thing being compared is the rendering.
    /// </summary>
    public static class ZoundNativeRefExport {

        private const int MAGIC = 0x5341435A;   // 'ZCAS'
        private const int VERSION = 1;
        private const int SR = 48000;

        public struct CaseSpec {
            public string name;
            public ZoundEffectChain chain;
            public float pitch, outGain, seconds;
        }

        /// <summary>Writes every case into <paramref name="folder"/> and returns a report.</summary>
        public static string ExportAll(string folder) {
            Directory.CreateDirectory(folder);
            // Cases are named by index, so adding one renames everything after it and
            // the previous export's files would linger beside the new ones. The
            // harness would then replay stale references — which it did, and reported
            // two passing effects as broken because an older copy of the same case,
            // rendered before a fix, was still sitting in the folder.
            foreach (var stale in Directory.GetFiles(folder, "*.zcase")) File.Delete(stale);
            var source = MakeSource(1.0f);
            var sb = new System.Text.StringBuilder("[Zounds] managed reference export -> ").Append(folder).Append('\n');
            int i = 0;
            foreach (var c in Cases()) {
                var managed = ZoundDspOffline.Render(source, 2, SR, SR, c.chain, c.pitch, c.outGain, c.seconds);
                var layout = c.chain != null && !c.chain.IsEmpty ? ChainLayout.Build(c.chain, SR) : ChainLayout.Empty;
                var blob = NativeChainBlob.Write(layout);

                float clipSeconds = (float)(source.Length / 2) / SR;
                double startFrame = 0;
                double endFrame = source.Length / 2;
                float sourceDuration = (float)((endFrame - startFrame) / SR) / Mathf.Max(c.pitch, 0.01f);

                string path = Path.Combine(folder, string.Format("{0:D2}_{1}.zcase", i, Sanitize(c.name)));
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                using (var w = new BinaryWriter(fs)) {
                    w.Write(MAGIC); w.Write(VERSION);
                    var name = System.Text.Encoding.UTF8.GetBytes(c.name);
                    w.Write(name.Length); w.Write(name);
                    w.Write(SR);
                    w.Write(2);                                  // channels
                    w.Write(source.Length / 2);                  // frames
                    w.Write(SR);                                 // frequency
                    float peak = 0f;
                    for (int k = 0; k < source.Length; k++) { float a = Mathf.Abs(source[k]); if (a > peak) peak = a; }
                    w.Write(peak);
                    w.Write(source.Length);
                    for (int k = 0; k < source.Length; k++) w.Write(source[k]);
                    w.Write(c.pitch); w.Write(c.outGain); w.Write(sourceDuration);
                    w.Write(startFrame); w.Write(endFrame);
                    w.Write(0);                                  // loop
                    w.Write(blob.Length); w.Write(blob);
                    w.Write(managed.frames);
                    for (int k = 0; k < managed.frames; k++) w.Write(managed.left[k]);
                    for (int k = 0; k < managed.frames; k++) w.Write(managed.right[k]);
                }
                sb.Append("  ").Append(Path.GetFileName(path)).Append(": ").Append(managed.frames)
                  .Append(" frames, peak ").Append(managed.peak.ToString("F4")).Append('\n');
                i++;
            }
            sb.Append("  ").Append(i).Append(" cases written\n");
            return sb.ToString();
        }

        private static string Sanitize(string s) {
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++) if (!char.IsLetterOrDigit(chars[i])) chars[i] = '_';
            return new string(chars);
        }

        private static ZoundEffectChain One(ZoundEffectType type, params float[] p) {
            var chain = new ZoundEffectChain();
            var node = new ZoundEffectNode(type);
            if (p != null && p.Length > 0) {
                node.EnsureParams();
                for (int i = 0; i < p.Length && i < node.p.Length; i++) node.p[i] = p[i];
            }
            chain.nodes.Add(node);
            return chain;
        }

        /// <summary>
        /// One case per effect at settings that actually exercise it, plus the paths that
        /// are not an effect at all (no chain, pitch, modulation, a multi-node chain).
        /// Defaults are deliberately avoided where a default would be a no-op.
        /// </summary>
        private static IEnumerable<CaseSpec> Cases() {
            yield return new CaseSpec { name = "no chain", chain = new ZoundEffectChain(), pitch = 1f, outGain = 1f, seconds = 1.2f };
            yield return new CaseSpec { name = "Gain", chain = One(ZoundEffectType.Gain, 0.5f), pitch = 1f, outGain = 1f, seconds = 1.2f };
            yield return new CaseSpec { name = "Normalize", chain = One(ZoundEffectType.Normalize, -6f), pitch = 1f, outGain = 1f, seconds = 1.2f };
            yield return new CaseSpec { name = "Fade", chain = One(ZoundEffectType.Fade, 0.1f, 0.2f, 0f), pitch = 1f, outGain = 1f, seconds = 1.2f };
            yield return new CaseSpec { name = "Fade s curve", chain = One(ZoundEffectType.Fade, 0.15f, 0.25f, 1f), pitch = 1f, outGain = 1f, seconds = 1.2f };

            yield return new CaseSpec { name = "Limiter", chain = One(ZoundEffectType.Limiter, -12f, 50f), pitch = 1f, outGain = 1f, seconds = 1.2f };
            yield return new CaseSpec { name = "Compressor", chain = One(ZoundEffectType.Compressor, -20f, 4f, 5f, 80f, 3f), pitch = 1f, outGain = 1f, seconds = 1.2f };
            yield return new CaseSpec { name = "TransientShaper", chain = One(ZoundEffectType.TransientShaper, 6f, -4f, 20f, 100f), pitch = 1f, outGain = 1f, seconds = 1.2f };

            yield return new CaseSpec { name = "LowPass", chain = One(ZoundEffectType.LowPass, 1200f, 1.4f), pitch = 1f, outGain = 1f, seconds = 1.2f };
            yield return new CaseSpec { name = "HighPass", chain = One(ZoundEffectType.HighPass, 800f, 0.9f), pitch = 1f, outGain = 1f, seconds = 1.2f };
            yield return new CaseSpec { name = "EQ", chain = One(ZoundEffectType.EQ, 4f, -3f, 2f, -5f, 3f, -2f, 1.5f, 80f, 9000f), pitch = 1f, outGain = 1f, seconds = 1.2f };

            yield return new CaseSpec { name = "Delay", chain = One(ZoundEffectType.Delay, 180f, 0.45f, 0.4f, 500f, 0f), pitch = 1f, outGain = 1f, seconds = 1.8f };
            yield return new CaseSpec { name = "Delay ping pong", chain = One(ZoundEffectType.Delay, 120f, 0.5f, 0.5f, 500f, 1f), pitch = 1f, outGain = 1f, seconds = 1.8f };
            yield return new CaseSpec { name = "Reverb", chain = One(ZoundEffectType.Reverb, 0.7f, 0.4f, 0.9f, 0.35f), pitch = 1f, outGain = 1f, seconds = 2.0f };
            yield return new CaseSpec { name = "Flanger", chain = One(ZoundEffectType.Flanger, 0.4f, 3f, 0.6f, 0.5f), pitch = 1f, outGain = 1f, seconds = 1.2f };
            yield return new CaseSpec { name = "Chorus", chain = One(ZoundEffectType.Chorus, 0.8f, 6f, 3f, 0.5f), pitch = 1f, outGain = 1f, seconds = 1.2f };
            yield return new CaseSpec { name = "Phaser", chain = One(ZoundEffectType.Phaser, 0.5f, 0.8f, 6f, 0.5f, 0.6f), pitch = 1f, outGain = 1f, seconds = 1.2f };
            yield return new CaseSpec { name = "BitCrush", chain = One(ZoundEffectType.BitCrush, 6f, 4f, 0.8f), pitch = 1f, outGain = 1f, seconds = 1.2f };
            yield return new CaseSpec { name = "Distortion", chain = One(ZoundEffectType.Distortion, 6f, 0.4f, 0.7f), pitch = 1f, outGain = 1f, seconds = 1.2f };

            // Diagnostics that isolate one term at a time. They exist because the first
            // ping-pong and reverb ports diverged from the reference and the whole-effect
            // cases could not say which term was wrong: with feedback off, a delay is
            // just its read path; with the cross off, ping-pong is just a delay; with
            // room and damping at zero a reverb is just its buffer layout.
            yield return new CaseSpec { name = "diag delay pp nofeedback", chain = One(ZoundEffectType.Delay, 120f, 0f, 1f, 500f, 1f), pitch = 1f, outGain = 1f, seconds = 1.4f };
            yield return new CaseSpec { name = "diag delay flat nofeedback", chain = One(ZoundEffectType.Delay, 120f, 0f, 1f, 500f, 0f), pitch = 1f, outGain = 1f, seconds = 1.4f };
            yield return new CaseSpec { name = "diag delay flat same params", chain = One(ZoundEffectType.Delay, 120f, 0.5f, 0.5f, 500f, 0f), pitch = 1f, outGain = 1f, seconds = 1.8f };
            yield return new CaseSpec { name = "diag reverb dry buffers", chain = One(ZoundEffectType.Reverb, 0f, 0f, 1f, 1f), pitch = 1f, outGain = 1f, seconds = 1.5f };
            yield return new CaseSpec { name = "diag reverb width zero", chain = One(ZoundEffectType.Reverb, 0.7f, 0.4f, 0f, 0.35f), pitch = 1f, outGain = 1f, seconds = 1.5f };

            yield return new CaseSpec { name = "pitch 1.5x", chain = One(ZoundEffectType.Gain, 1f), pitch = 1.5f, outGain = 1f, seconds = 1.2f };
            yield return new CaseSpec { name = "pitch 0.7x", chain = One(ZoundEffectType.Gain, 1f), pitch = 0.7f, outGain = 1f, seconds = 1.8f };
            yield return new CaseSpec { name = "output gain", chain = One(ZoundEffectType.Gain, 1f), pitch = 1f, outGain = 0.3f, seconds = 1.2f };

            // Modulation: the envelope path, the LFO path and a parameter that is swept
            // rather than held, each reaching a different part of the control-rate code.
            var env = new ZoundEffectChain();
            env.nodes.Add(new ZoundEffectNode(ZoundEffectType.Gain) { p = new[] { 1f } });
            env.modifiers.Add(new ZoundModifier(ZoundModifierType.Envelope) { curve = new Envelope(1f, 0f) });
            env.bindings.Add(new ZoundModifierBinding { modifierIndex = 0, nodeIndex = 0, paramIndex = 0, op = ModifierOp.Multiply, depth = 1f });
            yield return new CaseSpec { name = "Gain under Envelope", chain = env, pitch = 1f, outGain = 1f, seconds = 1.2f };

            var pitchEnv = new ZoundEffectChain();
            pitchEnv.modifiers.Add(new ZoundModifier(ZoundModifierType.Envelope) { curve = new Envelope(1f, 2f) });
            pitchEnv.bindings.Add(new ZoundModifierBinding { modifierIndex = 0, nodeIndex = -1, paramIndex = SourceStageParam.Pitch, op = ModifierOp.Multiply, depth = 1f });
            yield return new CaseSpec { name = "source pitch under Envelope", chain = pitchEnv, pitch = 1f, outGain = 1f, seconds = 1.2f };

            var lfo = new ZoundEffectChain();
            lfo.nodes.Add(new ZoundEffectNode(ZoundEffectType.LowPass) { p = new[] { 2000f, 1f } });
            var lfoMod = new ZoundModifier(ZoundModifierType.Lfo);
            lfoMod.EnsureParams();
            lfoMod.p[0] = 1500f;   // amount
            lfoMod.p[1] = 3f;      // rate
            lfoMod.p[2] = 0f;      // sine
            lfoMod.p[3] = 1f;      // reset phase, so the case is deterministic
            lfoMod.p[4] = 0f;      // oscillate
            if (lfoMod.p.Length > 6) lfoMod.p[6] = 2500f;   // offset
            lfo.modifiers.Add(lfoMod);
            lfo.bindings.Add(new ZoundModifierBinding { modifierIndex = 0, nodeIndex = 0, paramIndex = 0, op = ModifierOp.Replace, depth = 1f });
            yield return new CaseSpec { name = "LowPass cutoff under LFO", chain = lfo, pitch = 1f, outGain = 1f, seconds = 1.2f };

            // A real chain: several effects in series, which is where a state-offset
            // mistake shows up that a single-node case would never catch.
            var stack = new ZoundEffectChain();
            stack.nodes.Add(new ZoundEffectNode(ZoundEffectType.HighPass) { p = new[] { 200f, 0.8f } });
            stack.nodes.Add(new ZoundEffectNode(ZoundEffectType.Distortion) { p = new[] { 4f, 0.5f, 0.6f } });
            stack.nodes.Add(new ZoundEffectNode(ZoundEffectType.Delay) { p = new[] { 150f, 0.35f, 0.3f, 400f, 0f } });
            stack.nodes.Add(new ZoundEffectNode(ZoundEffectType.Compressor) { p = new[] { -18f, 3f, 8f, 120f, 2f } });
            stack.nodes.Add(new ZoundEffectNode(ZoundEffectType.Limiter) { p = new[] { -3f, 60f } });
            yield return new CaseSpec { name = "five node stack", chain = stack, pitch = 1f, outGain = 1f, seconds = 1.8f };

            // Reverb after a delay: the two biggest state consumers together, which is
            // also the case most likely to overflow an arena if the offsets are wrong.
            var big = new ZoundEffectChain();
            big.nodes.Add(new ZoundEffectNode(ZoundEffectType.Delay) { p = new[] { 200f, 0.4f, 0.35f, 600f, 1f } });
            big.nodes.Add(new ZoundEffectNode(ZoundEffectType.Reverb) { p = new[] { 0.8f, 0.3f, 1f, 0.4f } });
            yield return new CaseSpec { name = "delay into reverb", chain = big, pitch = 1f, outGain = 1f, seconds = 2.0f };

            // Every effect at once, bypass flags mixed in: catches a node that writes
            // outside its own state region.
            var all = new ZoundEffectChain();
            for (int t = 0; t < ZoundEffectDescriptors.EffectTypeCount; t++) {
                var node = new ZoundEffectNode((ZoundEffectType)t);
                node.enabled = (t % 3) != 2;
                all.nodes.Add(node);
            }
            yield return new CaseSpec { name = "every effect at defaults", chain = all, pitch = 1f, outGain = 1f, seconds = 1.5f };
        }

        private static float[] MakeSource(float seconds) {
            int frames = Mathf.CeilToInt(seconds * SR);
            var s = new float[frames * 2];
            for (int i = 0; i < frames; i++) {
                float t = (float)i / SR;
                // Tones for the filters to bite on, plus a repeating click train so the
                // dynamics and the transient shaper have real transients to work with.
                float v = 0.40f * Mathf.Sin(2f * Mathf.PI * 220f * t)
                        + 0.18f * Mathf.Sin(2f * Mathf.PI * 1310f * t)
                        + 0.09f * Mathf.Sin(2f * Mathf.PI * 47f * t);
                int intoClick = i % (SR / 4);
                if (intoClick < 240) v += 0.5f * Mathf.Exp(-intoClick / 60f) * Mathf.Sin(2f * Mathf.PI * 3000f * t);
                s[i * 2] = v;
                s[i * 2 + 1] = v * 0.8f;
            }
            return s;
        }
    }
}
