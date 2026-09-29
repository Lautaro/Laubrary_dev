// A kept check for ZPOC (Zound Programmatic Control, T-0495/T-0496): game code reaching a playing sound through ids.
//
// What is claimed, and measured on real rendered voices rather than reasoned about:
//
//   1. Exposing a modifier changes nothing until code speaks: Scale at one sweeps exactly as the unexposed modifier does.
//   2. Scale is "how much of the authored movement": nought freezes the parameter, one half halves the sweep.
//   3. Set is "the strength itself": the strongest binding lands exactly on the value, the other keeps its proportion.
//   4. A resting value applies from the first block with no command at all.
//   5. A Code modifier's number means the obvious thing per control: on pitch (Ratio) one half is unchanged, one is x4,
//      nought x1/4; on a cutoff (Set) it is the position along the control; under Shift one half is unchanged.
//   6. Easing: a value sent mid-play is approached smoothly (about two thirds of the way in the smoothing time), and the
//      path does not depend on how the host slices its calls.
//   7. A value sent before the first block is heard exactly from the first block, not eased into.
//   8. Ids match the way Zound names do, and a Zound knows which ids it (and what it plays) declares.
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

public static class ZoundsZpocCheck {

    const int SR = 48000;

    [MenuItem("Laubrary/Zounds/Checks/20 - ZPOC: modifier amount, Code modifier, easing")]
    public static void RunFromMenu() { Debug.Log(Execute()); }

    public static string Execute() {
        var sb = new StringBuilder();
        int fail = 0;
        void Check(bool ok, string what) { sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n'); if (!ok) fail++; }

        // ── 1..3: an LFO on two parameters, exposed with a ZPOC ──
        float SweepShare(ZpocMode? mode, float? send, float rest, int which, out float otherShare) {
            var chain = new ZoundEffectChain();
            var lp = new ZoundEffectNode(ZoundEffectType.LowPass); lp.p[0] = 2000f; lp.p[1] = 0.5f;
            chain.nodes.Add(lp);
            var lfo = new ZoundModifier(ZoundModifierType.Lfo);
            lfo.p[0] = 1f; lfo.p[1] = 8f; lfo.p[2] = 0f; lfo.p[3] = 1f;
            if (mode.HasValue) { lfo.zpocId = "Wobble Amount"; lfo.zpocMode = mode.Value; lfo.zpocRest = rest; lfo.zpocSmoothMs = 0f; }
            chain.modifiers.Add(lfo);
            chain.bindings.Add(new ZoundModifierBinding { modifierIndex = 0, nodeIndex = 0, paramIndex = 0, combine = ModulationCombine.Shift, depth = 0.4f, schema = ChainModulationCompat.CURRENT_SCHEMA });
            chain.bindings.Add(new ZoundModifierBinding { modifierIndex = 0, nodeIndex = 0, paramIndex = 1, combine = ModulationCombine.Shift, depth = 0.1f, schema = ChainModulationCompat.CURRENT_SCHEMA });
            var L = ChainLayout.Build(chain, SR);
            var v = Voice(L, 1f);
            try {
                if (send.HasValue) v.Apply(SapVoiceCommand.ModifierControl(0, L.ControlFor(0, send.Value)));
                Range(ref v, L, 0, 0, 1.0f, out float cLo, out float cHi);
                v.Dispose(); v = Voice(L, 1f);
                if (send.HasValue) v.Apply(SapVoiceCommand.ModifierControl(0, L.ControlFor(0, send.Value)));
                Range(ref v, L, 0, 1, 1.0f, out float rLo, out float rHi);
                var d = ZoundEffectDescriptors.Get(ZoundEffectType.LowPass);
                float cs = Share(cLo, cHi, d.parameters[0]), rs = Share(rLo, rHi, d.parameters[1]);
                otherShare = which == 0 ? rs : cs;
                return which == 0 ? cs : rs;
            }
            finally { v.Dispose(); }
        }

        float plain = SweepShare(null, null, -1f, 0, out float plainRes);
        float scale1 = SweepShare(ZpocMode.Scale, 1f, -1f, 0, out float scale1Res);
        float scaleNone = SweepShare(ZpocMode.Scale, null, -1f, 0, out _);
        float scale0 = SweepShare(ZpocMode.Scale, 0f, -1f, 0, out float scale0Res);
        float scaleHalf = SweepShare(ZpocMode.Scale, 0.5f, -1f, 0, out float scaleHalfRes);
        float set08 = SweepShare(ZpocMode.Set, 0.8f, -1f, 0, out float set08Res);
        float rest25 = SweepShare(ZpocMode.Scale, null, 0.25f, 0, out _);
        sb.Append("cutoff sweep, share of its control: unexposed ").Append(plain.ToString("F3"))
          .Append(", Scale 1 ").Append(scale1.ToString("F3")).Append(", nothing sent ").Append(scaleNone.ToString("F3"))
          .Append(", Scale 0 ").Append(scale0.ToString("F4")).Append(", Scale 0.5 ").Append(scaleHalf.ToString("F3"))
          .Append(", Set 0.8 ").Append(set08.ToString("F3")).Append(" (resonance ").Append(set08Res.ToString("F3")).Append(")")
          .Append(", resting 0.25 ").Append(rest25.ToString("F3")).Append('\n');
        Check(Mathf.Abs(scale1 - plain) < 1e-4f && Mathf.Abs(scale1Res - plainRes) < 1e-4f, "1. Scale at one sweeps exactly as the unexposed modifier");
        Check(Mathf.Abs(scaleNone - plain) < 1e-4f, "1. an exposed modifier nobody has sent to sweeps as authored");
        Check(scale0 < 1e-4f && scale0Res < 1e-4f, "2. Scale at nought freezes every bound parameter");
        Check(Mathf.Abs(scaleHalf - plain * 0.5f) < 0.01f && Mathf.Abs(scaleHalfRes - plainRes * 0.5f) < 0.01f, "2. Scale at one half halves the sweep on both bindings");
        // The sweep is proportional to the strength (Shift moves by a share of the room either side), so strength 0.8 on a
        // binding authored at 0.4 must sweep exactly twice what the authored modifier did.
        Check(Mathf.Abs(set08 - plain * (0.8f / 0.4f)) < 0.01f, "3. Set 0.8: the strongest binding (authored 0.4) sweeps exactly twice its authored amount");
        Check(Mathf.Abs(set08Res / Mathf.Max(set08, 1e-6f) - plainRes / Mathf.Max(plain, 1e-6f)) < 0.05f, "3. Set keeps the weaker binding's proportion to the strongest");
        Check(Mathf.Abs(rest25 - plain * 0.25f) < 0.01f, "4. a resting value of 0.25 applies from the first block with no command");

        // ── 5: a Code modifier ──
        float CodeOn(int nodeIndex, int paramIndex, ModulationCombine combine, float send) {
            var chain = new ZoundEffectChain();
            var lp = new ZoundEffectNode(ZoundEffectType.LowPass); lp.p[0] = 2000f; chain.nodes.Add(lp);
            var code = new ZoundModifier(ZoundModifierType.Code) { zpocId = "throttle", zpocSmoothMs = 0f };
            chain.modifiers.Add(code);
            chain.bindings.Add(new ZoundModifierBinding { modifierIndex = 0, nodeIndex = nodeIndex, paramIndex = paramIndex, combine = combine, depth = 1f, schema = ChainModulationCompat.CURRENT_SCHEMA });
            var L = ChainLayout.Build(chain, SR);
            var v = Voice(L, 1f);
            try {
                v.Apply(SapVoiceCommand.ModifierControl(0, L.ControlFor(0, send)));
                int f = SapVoiceRegistry.FlatIndexOf(L, nodeIndex, paramIndex);
                for (int i = 0; i < 20; i++) v.RenderBlock(256);
                return v.sap.pLive[f];
            }
            finally { v.Dispose(); }
        }
        var pitchPd = ZoundEffectDescriptors.SourceStageParams[SourceStageParam.Pitch];
        var cutPd = ZoundEffectDescriptors.Get(ZoundEffectType.LowPass).parameters[0];
        var pitchCombine = ChainModulationCompat.DefaultCombineForCode(pitchPd);
        Check(pitchCombine == ModulationCombine.Ratio, "5. binding a Code modifier to pitch defaults to Ratio");
        Check(ChainModulationCompat.DefaultCombineForCode(ZoundEffectDescriptors.SourceStageParams[SourceStageParam.Gain]) == ModulationCombine.Scale,
              "5. binding a Code modifier to a level defaults to Scale");
        Check(ChainModulationCompat.DefaultCombineForCode(cutPd) == ModulationCombine.Set, "5. binding a Code modifier to a cutoff defaults to Set");
        float pHalf = CodeOn(-1, SourceStageParam.Pitch, ModulationCombine.Ratio, 0.5f);
        float pTop = CodeOn(-1, SourceStageParam.Pitch, ModulationCombine.Ratio, 1f);
        float pBottom = CodeOn(-1, SourceStageParam.Pitch, ModulationCombine.Ratio, 0f);
        sb.Append("Code on pitch (Ratio): 0.5 -> x").Append(pHalf.ToString("F3")).Append(", 1 -> x").Append(pTop.ToString("F3")).Append(", 0 -> x").Append(pBottom.ToString("F3")).Append('\n');
        Check(Mathf.Abs(pHalf - 1f) < 1e-3f && Mathf.Abs(pTop - 4f) < 1e-2f && Mathf.Abs(pBottom - 0.25f) < 1e-3f, "5. pitch: one half unchanged, one x4, nought x1/4");
        float c03 = CodeOn(0, 0, ModulationCombine.Set, 0.3f);
        float pos = ModulationMath.ToPosition(c03, cutPd.min, cutPd.max, true);
        sb.Append("Code on cutoff (Set) 0.3 -> ").Append(c03.ToString("F1")).Append(" Hz = position ").Append(pos.ToString("F3")).Append('\n');
        Check(Mathf.Abs(pos - 0.3f) < 2e-3f, "5. cutoff under Set: the value is the position along the control");
        float sHalf = CodeOn(0, 0, ModulationCombine.Shift, 0.5f);
        Check(Mathf.Abs(sHalf - 2000f) < 0.5f, "5. under Shift a Code modifier's one half leaves the parameter where it was set (" + sHalf.ToString("F1") + " Hz)");

        // ── 6: easing ──
        // Control-grid value after one smoothing time (30 ms = 1440 samples = 22.5 blocks): about 63% there.
        float after30 = EaseAt(1440), after150 = EaseAt(7200);
        float EaseAt(int samplesAfter) {
            var chain = new ZoundEffectChain();
            var lp = new ZoundEffectNode(ZoundEffectType.LowPass); lp.p[0] = 2000f; chain.nodes.Add(lp);
            var code = new ZoundModifier(ZoundModifierType.Code) { zpocId = "x", zpocSmoothMs = 30f };
            code.p[0] = 0f;
            chain.modifiers.Add(code);
            chain.bindings.Add(new ZoundModifierBinding { modifierIndex = 0, nodeIndex = 0, paramIndex = 0, combine = ModulationCombine.Set, depth = 1f, schema = ChainModulationCompat.CURRENT_SCHEMA });
            var L = ChainLayout.Build(chain, SR);
            var v = Voice(L, 1f);
            try {
                for (int done = 0; done < 4800; done += 256) v.RenderBlock(Mathf.Min(256, 4800 - done));
                v.Apply(SapVoiceCommand.ModifierControl(0, 1f));
                int left = samplesAfter / 64 * 64;
                while (left > 0) { int n = Mathf.Min(256, left); v.RenderBlock(n); left -= n; }
                return v.sap.modCtlLive[0];
            }
            finally { v.Dispose(); }
        }
        sb.Append("eased control 30 ms after a jump 0 -> 1: ").Append(after30.ToString("F3")).Append(", after 150 ms: ").Append(after150.ToString("F4")).Append('\n');
        Check(after30 > 0.55f && after30 < 0.72f, "6. about two thirds of the way after one smoothing time");
        Check(after150 > 0.99f, "6. all the way after five smoothing times");
        float fa = FinalAfter(256), fb = FinalAfter(333);
        sb.Append("cutoff 0.1 s after the send, rendered in 256s vs 333s: ").Append(fa.ToString("F3")).Append(" vs ").Append(fb.ToString("F3")).Append('\n');
        float FinalAfter(int block) {
            var chain = new ZoundEffectChain();
            var lp = new ZoundEffectNode(ZoundEffectType.LowPass); lp.p[0] = 2000f; chain.nodes.Add(lp);
            var code = new ZoundModifier(ZoundModifierType.Code) { zpocId = "x", zpocSmoothMs = 30f };
            code.p[0] = 0f;
            chain.modifiers.Add(code);
            chain.bindings.Add(new ZoundModifierBinding { modifierIndex = 0, nodeIndex = 0, paramIndex = 0, combine = ModulationCombine.Set, depth = 1f, schema = ChainModulationCompat.CURRENT_SCHEMA });
            var L = ChainLayout.Build(chain, SR);
            var v = Voice(L, 1f);
            try {
                // The send lands on a control-grid boundary in both (4800 = 75 blocks of 64), then the same absolute length.
                int done = 0;
                while (done < 4800) { int n = Mathf.Min(block, 4800 - done); v.RenderBlock(n); done += n; }
                v.Apply(SapVoiceCommand.ModifierControl(0, 1f));
                int end = 4800 + 4800;
                while (done < end) { int n = Mathf.Min(block, end - done); v.RenderBlock(n); done += n; }
                return v.sap.pLive[SapVoiceRegistry.FlatIndexOf(L, 0, 0)];
            }
            finally { v.Dispose(); }
        }
        Check(Mathf.Abs(fa - fb) < 1e-3f, "6. the eased path does not depend on how calls are sliced");

        // ── 7: sent before the first block ──
        {
            var chain = new ZoundEffectChain();
            var lp = new ZoundEffectNode(ZoundEffectType.LowPass); lp.p[0] = 2000f; chain.nodes.Add(lp);
            var code = new ZoundModifier(ZoundModifierType.Code) { zpocId = "x", zpocSmoothMs = 500f };
            code.p[0] = 0f;
            chain.modifiers.Add(code);
            chain.bindings.Add(new ZoundModifierBinding { modifierIndex = 0, nodeIndex = 0, paramIndex = 0, combine = ModulationCombine.Set, depth = 1f, schema = ChainModulationCompat.CURRENT_SCHEMA });
            var L = ChainLayout.Build(chain, SR);
            var v = Voice(L, 1f);
            try {
                v.Apply(SapVoiceCommand.ModifierControl(0, 0.7f));
                v.RenderBlock(64);
                float p = ModulationMath.ToPosition(v.sap.pLive[SapVoiceRegistry.FlatIndexOf(L, 0, 0)], cutPd.min, cutPd.max, true);
                sb.Append("first block after a value sent before play: control ").Append(v.sap.modCtlLive[0].ToString("F3")).Append(", cutoff position ").Append(p.ToString("F3")).Append('\n');
                Check(Mathf.Abs(v.sap.modCtlLive[0] - 0.7f) < 1e-5f && Mathf.Abs(p - 0.7f) < 2e-3f, "7. a value sent before the first block is heard from the first block");
            }
            finally { v.Dispose(); }
        }

        // ── 8: ids ──
        Check(ZpocKeys.Same("Rotor Speed", "rotor-speed") && ZpocKeys.Same("rotor_speed", "ROTORSPEED"), "8. ids match ignoring case, spaces, underscores and hyphens");
        Check(!ZpocKeys.Same("rotor", "rotors") && ZpocKeys.Key("") == null, "8. different ids do not match; an empty id matches nothing");
        {
            var klip = new Klip(0) { name = "zpoc check (in memory)" };
            klip.effectChain.modifiers.Add(new ZoundModifier(ZoundModifierType.Lfo) { zpocId = "Wobble" });
            Check(ZpocIndex.Declares(klip, ZpocKeys.Key("wob-ble")) && !ZpocIndex.Declares(klip, ZpocKeys.Key("throttle")), "8. a Zound knows which ids it declares");
        }

        // ── 9: the multi-instance spread's ballistics (the editor display, T-0492) ──
        {
            float lo = 0.4f, hi = 0.6f;
            Laubrary.Zui.ZuiLiveOverlay.Ballistics(ref lo, ref hi, 0.1f, 0.9f, 0.016f, 0.3f);
            Check(lo == 0.1f && hi == 0.9f, "9. the spread widens at once when an instance moves further out");
            float lo2 = 0.1f, hi2 = 0.9f;
            Laubrary.Zui.ZuiLiveOverlay.Ballistics(ref lo2, ref hi2, 0.4f, 0.6f, 0.3f, 0.3f);
            float closed = (hi2 - 0.9f) / (0.6f - 0.9f);
            Check(Mathf.Abs(closed - (1f - Mathf.Exp(-1f))) < 1e-3f && Mathf.Abs((lo2 - 0.1f) / 0.3f - closed) < 1e-3f,
                  "9. it narrows back slowly: about 63% of the way in one release time (" + (closed * 100f).ToString("F1") + "%)");
            float a = 0.1f, b = 0.9f;
            for (int i = 0; i < 60; i++) Laubrary.Zui.ZuiLiveOverlay.Ballistics(ref a, ref b, 0.4f, 0.6f, 1f / 60f, 0.3f);
            float c = 0.1f, d = 0.9f;
            for (int i = 0; i < 20; i++) Laubrary.Zui.ZuiLiveOverlay.Ballistics(ref c, ref d, 0.4f, 0.6f, 3f / 60f, 0.3f);
            Check(Mathf.Abs(a - c) < 1e-4f && Mathf.Abs(b - d) < 1e-4f, "9. the narrowing does not depend on the redraw rate");
        }

        sb.Insert(0, fail == 0 ? "PASS - ZPOC modifier amount, Code modifier and easing.\n" : "FAIL - " + fail + " problem(s).\n");
        return sb.ToString();
    }

    static SapRealtimeVoice Voice(ChainLayout L, float seconds) {
        var pcm = Tone((int)(SR * 2f));
        return SapRealtimeVoice.Create(pcm, L, SR, 0d, pcm.frames, 1f, 1f, (float)pcm.frames / SR, false, 7, true, Allocator.Persistent);
    }

    static void Range(ref SapRealtimeVoice v, ChainLayout L, int node, int param, float seconds, out float lo, out float hi) {
        int f = SapVoiceRegistry.FlatIndexOf(L, node, param);
        lo = float.MaxValue; hi = float.MinValue;
        int frames = (int)(seconds * SR), w = 0;
        while (w < frames && !v.finished) {
            int n = Mathf.Min(256, frames - w);
            v.RenderBlock(n);
            float x = v.sap.pLive[f]; if (x < lo) lo = x; if (x > hi) hi = x;
            w += n;
        }
    }

    static float Share(float lo, float hi, ParamDesc pd) {
        bool r = ModulationMath.IsRatioSpaced(pd.curve);
        return ModulationMath.ToPosition(hi, pd.min, pd.max, r) - ModulationMath.ToPosition(lo, pd.min, pd.max, r);
    }

    static PcmClip Tone(int frames) {
        var data = new float[frames * 2];
        for (int i = 0; i < frames; i++) { float s = 0.3f * Mathf.Sin(i * 0.05f); data[i * 2] = s; data[i * 2 + 1] = s; }
        return new PcmClip { channels = 2, frequency = SR, frames = frames, samples = data, valid = true, peak = 0.3f };
    }
}
