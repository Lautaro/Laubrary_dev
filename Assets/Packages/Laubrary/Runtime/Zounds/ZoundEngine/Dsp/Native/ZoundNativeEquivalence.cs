using System.Text;
using UnityEngine;

namespace Laubrary.Zounds.Dsp.Native {

    /// <summary>
    /// Renders the same chain through the managed engine and the native one and compares
    /// the two outputs sample for sample.
    ///
    /// This is the acceptance test for the port, and the reason the managed DSP is kept
    /// in the project rather than deleted: it is the reference implementation, already
    /// trusted and already audited, so "the native Gain is right" can be a measurement
    /// instead of an opinion. Every effect ported from here on gets a case in
    /// <see cref="RunAll"/>, and an effect is not done until its case passes.
    ///
    /// The two engines are not expected to be bit-identical forever — a native build may
    /// use different transcendental approximations — so the comparison is a tolerance on
    /// the largest absolute difference, tight enough (1e-4, about -80 dB) that a real
    /// behavioural difference cannot hide under it.
    /// </summary>
    public static class ZoundNativeEquivalence {

        private const int SR = 48000;
        private const float TOLERANCE = 1e-4f;

        private struct Case {
            public string name;
            public ZoundEffectChain chain;
            public float pitch, gain, seconds;
        }

        /// <summary>Runs every case and returns a report. Never throws; a failure is reported, not raised.</summary>
        public static string RunAll() {
            var sb = new StringBuilder();
            sb.Append("[Zounds] native / managed equivalence\n");
            if (!ZoundsNative.Available && !ZoundsNative.Initialise(SR, 1024)) {
                sb.Append("  the native engine is not available: ").Append(ZoundsNative.LoadError).Append('\n');
                return sb.ToString();
            }
            sb.Append("  plugin ABI ").Append(ZoundsNative.Zounds_GetAbiVersion())
              .Append(", control block ").Append(ZoundsNative.Zounds_GetControlBlockSize()).Append(" bytes\n");

            var source = MakeSource(1.0f);
            int passed = 0, failed = 0;

            foreach (var c in Cases()) {
                var managed = ZoundDspOffline.Render(source, 2, SR, SR, c.chain, c.pitch, c.gain, c.seconds);
                var native = ZoundNativeOffline.Render(source, 2, SR, SR, c.chain, c.pitch, c.gain, c.seconds);
                if (native.error != null) {
                    sb.Append("  FAIL  ").Append(c.name).Append(": ").Append(native.error).Append('\n');
                    failed++;
                    continue;
                }
                int frames = Mathf.Min(managed.frames, native.frames);
                float worst = 0f;
                int worstAt = -1;
                for (int i = 0; i < frames; i++) {
                    float d = Mathf.Abs(managed.left[i] - native.left[i]);
                    if (d > worst) { worst = d; worstAt = i; }
                    d = Mathf.Abs(managed.right[i] - native.right[i]);
                    if (d > worst) { worst = d; worstAt = i; }
                }
                bool lengthOk = Mathf.Abs(managed.frames - native.frames) <= 1024;
                bool ok = worst <= TOLERANCE && lengthOk && frames > 0;
                sb.Append(ok ? "  ok    " : "  FAIL  ").Append(c.name)
                  .Append(": worst diff ").Append(worst.ToString("G4"))
                  .Append(worstAt >= 0 ? " at frame " + worstAt : "")
                  .Append(", managed ").Append(managed.frames).Append(" frames / peak ").Append(managed.peak.ToString("F4"))
                  .Append(", native ").Append(native.frames).Append(" frames / peak ").Append(native.peak.ToString("F4"))
                  .Append('\n');
                if (ok) passed++; else failed++;
            }

            sb.Append("  ").Append(passed).Append(" passed, ").Append(failed).Append(" failed\n");
            sb.Append(PortStatus());
            return sb.ToString();
        }

        /// <summary>Which effect types the native engine renders, and which still pass through untouched.</summary>
        public static string PortStatus() {
            var sb = new StringBuilder("  ported natively: ");
            var pending = new StringBuilder();
            for (int t = 0; t < ZoundEffectDescriptors.EffectTypeCount; t++) {
                string name = ((ZoundEffectType)t).ToString();
                if (ZoundsNative.Zounds_EffectImplemented(t) != 0) sb.Append(name).Append(' ');
                else pending.Append(name).Append(' ');
            }
            sb.Append("\n  still passing through: ").Append(pending).Append('\n');
            return sb.ToString();
        }

        private static System.Collections.Generic.IEnumerable<Case> Cases() {
            yield return new Case { name = "no chain", chain = new ZoundEffectChain(), pitch = 1f, gain = 1f, seconds = 1.2f };

            var gain = new ZoundEffectChain();
            gain.nodes.Add(new ZoundEffectNode(ZoundEffectType.Gain) { p = new[] { 0.5f } });
            yield return new Case { name = "Gain 0.5", chain = gain, pitch = 1f, gain = 1f, seconds = 1.2f };

            var bypassed = new ZoundEffectChain();
            var b = new ZoundEffectNode(ZoundEffectType.Gain) { p = new[] { 0.25f } };
            b.enabled = false;
            bypassed.nodes.Add(b);
            yield return new Case { name = "Gain bypassed", chain = bypassed, pitch = 1f, gain = 1f, seconds = 1.2f };

            var pitched = new ZoundEffectChain();
            pitched.nodes.Add(new ZoundEffectNode(ZoundEffectType.Gain) { p = new[] { 1f } });
            yield return new Case { name = "Gain at 1.5x pitch", chain = pitched, pitch = 1.5f, gain = 1f, seconds = 1.2f };

            var quiet = new ZoundEffectChain();
            quiet.nodes.Add(new ZoundEffectNode(ZoundEffectType.Gain) { p = new[] { 1f } });
            yield return new Case { name = "output gain 0.3", chain = quiet, pitch = 1f, gain = 0.3f, seconds = 1.2f };

            // Gain driven by an Envelope: the whole modulation path (control-rate
            // evaluation, binding, per-sample ramp) has to agree, not just the effect.
            var modulated = new ZoundEffectChain();
            modulated.nodes.Add(new ZoundEffectNode(ZoundEffectType.Gain) { p = new[] { 1f } });
            var env = new ZoundModifier(ZoundModifierType.Envelope) { curve = new Envelope(1f, 0f) };
            modulated.modifiers.Add(env);
            modulated.bindings.Add(new ZoundModifierBinding { modifierIndex = 0, nodeIndex = 0, paramIndex = 0, op = ModifierOp.Multiply, depth = 1f });
            yield return new Case { name = "Gain under an Envelope", chain = modulated, pitch = 1f, gain = 1f, seconds = 1.2f };

            // An Envelope on the SOURCE pitch: the modulation reaches the read cursor
            // rather than an effect parameter, which is a different path again.
            var pitchEnv = new ZoundEffectChain();
            var penv = new ZoundModifier(ZoundModifierType.Envelope) { curve = new Envelope(1f, 2f) };
            pitchEnv.modifiers.Add(penv);
            pitchEnv.bindings.Add(new ZoundModifierBinding { modifierIndex = 0, nodeIndex = -1, paramIndex = SourceStageParam.Pitch, op = ModifierOp.Multiply, depth = 1f });
            yield return new Case { name = "source pitch under an Envelope", chain = pitchEnv, pitch = 1f, gain = 1f, seconds = 1.2f };

            var fade = new ZoundEffectChain();
            fade.nodes.Add(new ZoundEffectNode(ZoundEffectType.Fade) { p = new[] { 0.1f, 0.2f, 0f } });
            yield return new Case { name = "Fade in/out", chain = fade, pitch = 1f, gain = 1f, seconds = 1.2f };

            var norm = new ZoundEffectChain();
            norm.nodes.Add(new ZoundEffectNode(ZoundEffectType.Normalize) { p = new[] { -6f } });
            yield return new Case { name = "Normalize to -6 dB", chain = norm, pitch = 1f, gain = 1f, seconds = 1.2f };
        }

        /// <summary>A second of stereo material with some structure to it, so a difference has somewhere to show.</summary>
        private static float[] MakeSource(float seconds) {
            int frames = Mathf.CeilToInt(seconds * SR);
            var s = new float[frames * 2];
            for (int i = 0; i < frames; i++) {
                float t = (float)i / SR;
                float v = 0.45f * Mathf.Sin(2f * Mathf.PI * 220f * t)
                        + 0.2f * Mathf.Sin(2f * Mathf.PI * 1310f * t)
                        + 0.1f * Mathf.Sin(2f * Mathf.PI * 47f * t);
                s[i * 2] = v;
                s[i * 2 + 1] = v * 0.8f;
            }
            return s;
        }
    }
}
