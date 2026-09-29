// A kept check for snapshot glides (T-0498): the engine half on voices rendered directly, the token half on an in-memory
// copy of a real Klip (never added to the library, never saved).
//
//   1. A continuous value glides along its own control: a cutoff from 500 to 5000 Hz passes ~1581 Hz (the geometric
//      middle) halfway, lands exactly, and leaves nothing ramping after.
//   2. A value a modifier moves has its resting value glided; the modifier goes on moving it around the new resting value.
//   3. A whole-number setting switches at the midpoint, not before.
//   4. An effect glided on fades in: no jump in the output larger than the signal's own steps.
//   5. A glide sent before the first block is on its target from the first block.
//   6. The glided path is the same whatever the call slicing, and the render allocates nothing while gliding.
//   7. Tokens: GlideToSnapshot moves a playing sound, Default brings it back, a replayed token starts on its snapshot,
//      GlideBack returns exactly, a stale handle does nothing, a missing name is reported.
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

public static class ZoundsGlideCheck {

    const int SR = 48000;

    [MenuItem("Laubrary/Zounds/Checks/24 - Snapshot glides (along the control, switches, fades, tokens)")]
    public static void RunFromMenu() { Debug.Log(Execute()); }

    static PcmClip Tone(int frames) {
        var d = new float[frames * 2];
        for (int i = 0; i < frames; i++) { float s = 0.3f * Mathf.Sin(i * 0.05f) + 0.1f * Mathf.Sin(i * 0.31f); d[i * 2] = s; d[i * 2 + 1] = s; }
        return new PcmClip { channels = 2, frequency = SR, frames = frames, samples = d, valid = true, peak = 0.4f };
    }

    static SapRealtimeVoice Voice(ChainLayout L) {
        var pcm = Tone(SR * 2);
        return SapRealtimeVoice.Create(pcm, L, SR, 0d, pcm.frames, 1f, 1f, (float)pcm.frames / SR, false, 11, true, Allocator.Persistent);
    }

    static void Render(ref SapRealtimeVoice v, int samples, int block) {
        for (int done = 0; done < samples; done += block) v.RenderBlock(Mathf.Min(block, samples - done));
    }

    public static string Execute() {
        var sb = new StringBuilder();
        int fail = 0;
        void Check(bool ok, string what) { sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n'); if (!ok) fail++; }
        var cutPd = ZoundEffectDescriptors.Get(ZoundEffectType.LowPass).parameters[0];

        // ── 1: a cutoff glides along its control ──
        float Cutoff(int block, int at, out float stepAfter) {
            var chain = new ZoundEffectChain();
            var lp = new ZoundEffectNode(ZoundEffectType.LowPass); lp.p[0] = 500f; chain.nodes.Add(lp);
            var L = ChainLayout.Build(chain, SR);
            var v = Voice(L);
            try {
                Render(ref v, 4800, block);
                int f = L.FlatIndex(0, 0);
                v.Apply(SapVoiceCommand.Glide(SapVoiceCommandKind.GlideClear, 0, 0));
                v.Apply(SapVoiceCommand.Glide(SapVoiceCommandKind.GlideParam, f, 5000f));
                v.Apply(SapVoiceCommand.Glide(SapVoiceCommandKind.GlideBegin, 4800, 0));
                Render(ref v, at, block);
                float c = v.sap.pLive[f];
                Render(ref v, 9600, block);
                stepAfter = v.sap.pStep[f];
                return c;
            }
            finally { v.Dispose(); }
        }
        float half = Cutoff(64, 2400, out _), end = Cutoff(64, 4800, out float step), sliced = Cutoff(333, 2400, out _);
        sb.Append("cutoff 500 -> 5000 Hz over 0.1 s: halfway ").Append(half.ToString("F0")).Append(" Hz, at the end ").Append(end.ToString("F1")).Append(" Hz\n");
        Check(Mathf.Abs(half - Mathf.Sqrt(500f * 5000f)) / 1581f < 0.05f, "1. halfway it is at the geometric middle of its control (~1581 Hz)");
        Check(Mathf.Abs(end - 5000f) < 0.5f, "1. it lands exactly on the target");
        Check(step == 0f, "1. nothing is left ramping once it has arrived");

        // ── 6a: slicing ──
        float s64 = Cutoff(64, 2432, out _), s256 = Cutoff(256, 2432, out _);
        Check(Mathf.Abs(s64 - s256) < 1e-2f, "6. the glided path is the same rendered in 64s or 256s (" + s64.ToString("F2") + " vs " + s256.ToString("F2") + ")");

        // ── 2: a modulated value's resting value glides ──
        {
            var chain = new ZoundEffectChain();
            var lp = new ZoundEffectNode(ZoundEffectType.LowPass); lp.p[0] = 1000f; chain.nodes.Add(lp);
            var lfo = new ZoundModifier(ZoundModifierType.Lfo); lfo.p[1] = 10f; lfo.p[3] = 1f;
            chain.modifiers.Add(lfo);
            chain.bindings.Add(new ZoundModifierBinding { modifierIndex = 0, nodeIndex = 0, paramIndex = 0, combine = ModulationCombine.Shift, depth = 0.2f, schema = ChainModulationCompat.CURRENT_SCHEMA });
            var L = ChainLayout.Build(chain, SR);
            var v = Voice(L);
            try {
                int f = L.FlatIndex(0, 0);
                v.Apply(SapVoiceCommand.Glide(SapVoiceCommandKind.GlideClear, 0, 0));
                v.Apply(SapVoiceCommand.Glide(SapVoiceCommandKind.GlideParam, f, 4000f));
                v.Apply(SapVoiceCommand.Glide(SapVoiceCommandKind.GlideBegin, 0, 0));   // before the first block: at once
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < 100; i++) { v.RenderBlock(256); float x = v.sap.pLive[f]; if (x < lo) lo = x; if (x > hi) hi = x; }
                float centre = Mathf.Sqrt(lo * hi);
                sb.Append("modulated cutoff after gliding its rest to 4000 Hz: swings ").Append(lo.ToString("F0")).Append("..").Append(hi.ToString("F0")).Append(" Hz\n");
                Check(v.chain.pBase[f] == 4000f, "2. its resting value moved to 4000 Hz");
                Check(hi > lo * 1.2f && centre > 2500f && centre < 6000f, "2. the oscillator still swings it, now around the new resting value");
            }
            finally { v.Dispose(); }
        }

        // ── 3: a whole-number setting switches at the midpoint ──
        {
            var chain = new ZoundEffectChain();
            var ph = new ZoundEffectNode(ZoundEffectType.Phaser); chain.nodes.Add(ph);
            var d = ZoundEffectDescriptors.Get(ZoundEffectType.Phaser);
            int k = -1;
            for (int i = 0; i < d.parameters.Length; i++) if (d.parameters[i].curve == ParamCurve.Integer) { k = i; break; }
            if (k < 0) Check(false, "3. the phaser has a whole-number setting to test with");
            else {
                var L = ChainLayout.Build(chain, SR);
                var v = Voice(L);
                try {
                    int f = L.FlatIndex(0, k);
                    float from = v.sap.pLive[f], to = from == d.parameters[k].max ? d.parameters[k].min : d.parameters[k].max;
                    Render(ref v, 640, 64);
                    v.Apply(SapVoiceCommand.Glide(SapVoiceCommandKind.GlideClear, 0, 0));
                    v.Apply(SapVoiceCommand.Glide(SapVoiceCommandKind.GlideParamSwitch, f, to));
                    v.Apply(SapVoiceCommand.Glide(SapVoiceCommandKind.GlideBegin, 6400, 0));
                    Render(ref v, 2560, 64); float before = v.sap.pLive[f];
                    Render(ref v, 1280, 64); float after = v.sap.pLive[f];
                    Check(before == from && after == to, "3. '" + d.parameters[k].name + "' is still " + from + " at 40% and " + to + " at 60%");
                }
                finally { v.Dispose(); }
            }
        }

        // ── 4: an effect fades in ──
        {
            var chain = new ZoundEffectChain();
            var bc = new ZoundEffectNode(ZoundEffectType.BitCrush) { enabled = false };
            chain.nodes.Add(bc);
            var L = ChainLayout.Build(chain, SR);
            var v = Voice(L);
            float worstJump = 0f, worstSource = 0f;
            try {
                Render(ref v, 1280, 64);
                v.Apply(SapVoiceCommand.Glide(SapVoiceCommandKind.GlideClear, 0, 0));
                v.Apply(SapVoiceCommand.Glide(SapVoiceCommandKind.GlidePresence, 0, 1f));
                v.Apply(SapVoiceCommand.Glide(SapVoiceCommandKind.GlideBegin, 9600, 0));
                float prev = 0f; bool first = true;
                for (int b = 0; b < 200; b++) {
                    v.RenderBlock(64);
                    for (int i = 0; i < 64; i++) {
                        float x = v.sap.bufL[i];
                        if (!first) worstJump = Mathf.Max(worstJump, Mathf.Abs(x - prev));
                        prev = x; first = false;
                    }
                }
                Check(v.sap.presence[0] == 1f, "4. the effect is fully in after the glide");
            }
            finally { v.Dispose(); }
            // Reference: the same effect simply on, whose own output steps set what a normal jump looks like.
            var chain2 = new ZoundEffectChain(); chain2.nodes.Add(new ZoundEffectNode(ZoundEffectType.BitCrush));
            var L2 = ChainLayout.Build(chain2, SR);
            var v2 = Voice(L2);
            try {
                float prev = 0f; bool first = true;
                for (int b = 0; b < 200; b++) {
                    v2.RenderBlock(64);
                    for (int i = 0; i < 64; i++) { float x = v2.sap.bufL[i]; if (!first) worstSource = Mathf.Max(worstSource, Mathf.Abs(x - prev)); prev = x; first = false; }
                }
            }
            finally { v2.Dispose(); }
            sb.Append("largest sample step while fading a bit crusher in: ").Append(worstJump.ToString("F4")).Append(" (the crusher on its own: ").Append(worstSource.ToString("F4")).Append(")\n");
            Check(worstJump <= worstSource * 1.05f + 1e-4f, "4. fading an effect in adds no jump beyond the effect's own");
        }

        // ── 5: before the first block ──
        {
            var chain = new ZoundEffectChain();
            var lp = new ZoundEffectNode(ZoundEffectType.LowPass); lp.p[0] = 500f; chain.nodes.Add(lp);
            var L = ChainLayout.Build(chain, SR);
            var v = Voice(L);
            try {
                int f = L.FlatIndex(0, 0);
                v.Apply(SapVoiceCommand.Glide(SapVoiceCommandKind.GlideClear, 0, 0));
                v.Apply(SapVoiceCommand.Glide(SapVoiceCommandKind.GlideParam, f, 3000f));
                v.Apply(SapVoiceCommand.Glide(SapVoiceCommandKind.GlideBegin, 48000, 0));
                v.RenderBlock(64);
                Check(Mathf.Abs(v.sap.pLive[f] - 3000f) < 0.5f, "5. a glide sent before the first block is on its target from the first block");
            }
            finally { v.Dispose(); }
        }

        // ── 6b: no allocation while gliding ──
        {
            var chain = new ZoundEffectChain();
            var lp = new ZoundEffectNode(ZoundEffectType.LowPass); lp.p[0] = 500f; chain.nodes.Add(lp);
            chain.nodes.Add(new ZoundEffectNode(ZoundEffectType.BitCrush) { enabled = false });
            var L = ChainLayout.Build(chain, SR);
            var v = Voice(L);
            try {
                v.RenderBlock(64);
                v.Apply(SapVoiceCommand.Glide(SapVoiceCommandKind.GlideClear, 0, 0));
                v.Apply(SapVoiceCommand.Glide(SapVoiceCommandKind.GlideParam, L.FlatIndex(0, 0), 8000f));
                v.Apply(SapVoiceCommand.Glide(SapVoiceCommandKind.GlidePresence, 1, 1f));
                v.Apply(SapVoiceCommand.Glide(SapVoiceCommandKind.GlideBegin, 48000, 0));
                v.RenderBlock(64);
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 400; i++) v.RenderBlock(256);
                long used = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Check(used == 0, "6. rendering 400 blocks while gliding allocated " + used + " bytes");
            }
            finally { v.Dispose(); }
        }

        // ── 7: tokens -- started here, read a moment later (a play's voice is made by the audio system after this call returns) ──
        StartTokenPart();
        sb.Append("  (token part: running; its result is logged as [ZoundsGlideCheck tokens] and kept in LastTokenResult)\n");

        sb.Insert(0, fail == 0 ? "PASS - snapshot glides.\n" : "FAIL - " + fail + " problem(s).\n");
        return sb.ToString();
    }

    /// <summary>The token part's verdict, once it has run (see StartTokenPart).</summary>
    public static string LastTokenResult = "(not run yet)";

    static void StartTokenPart() {
        LastTokenResult = "(running)";
        Klip src = null;
        foreach (var z in ZoundsProject.Instance.zoundLibrary.GetAllZounds()) if (z is Klip k && ZoundSapPlayback.LoadSourceClip(k, out bool _) != null) { src = k; break; }
        if (src == null) { LastTokenResult = "SKIPPED - no playable Klip in this project"; return; }
        var klip = JsonUtility.FromJson<Klip>(JsonUtility.ToJson(src));
        typeof(Zound).GetField("id").SetValue(klip, -9601);
        klip.name = "glide check (in memory)";
        klip.chainPresetId = 0;
        klip.chainOverrides = new System.Collections.Generic.List<ChainParamOverride>();
        var chain = new ZoundEffectChain();
        var lp = new ZoundEffectNode(ZoundEffectType.LowPass); lp.p[0] = 400f; chain.nodes.Add(lp);
        klip.effectChain = chain;
        klip.snapshots = new System.Collections.Generic.List<ZoundSnapshot> { ZoundSnapshots.Capture(klip, "Calm") };
        lp.p[0] = 8000f;
        var args = new ZoundArgs { startImmediately = false, volumeOverride = 0f, pitchOverride = 1f, chanceOverride = 1f, ignoreCooldown = true, bypassGlobalSolo = true };

        var sb = new StringBuilder();
        int fail = 0;
        void Check(bool ok, string what) { sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n'); if (!ok) fail++; }
        float Heard(ZoundToken t) {
            var g = t != null && t.audioSource != null ? t.audioSource.generator as ZoundSapVoiceGenerator : null;
            if (g == null || !g.TryReadLiveParam(g.playingLayout.FlatIndex(0, 0), out float v)) return -1f;
            return v;
        }

        // Step 1: a token glided to Calm BEFORE Play.
        var tok = ZoundEngine.PlayZound(klip, args);
        var glide = tok.GlideToSnapshot("calm", 0f);
        tok.Play();
        ZoundGlide back = default;
        var steps = new System.Collections.Generic.List<System.Action>();
        steps.Add(() => {
            float h = Heard(tok);
            Check(Mathf.Abs(h - 400f) < 1f, "7. a token glided to 'Calm' before Play starts on it (" + h.ToString("F0") + " Hz)");
            Check(glide.isCurrent, "7. its glide handle is the current one");
            back = glide.GlideBack(0f);
            Check(back.isCurrent && !glide.isCurrent, "7. gliding back gives a new current handle; the old one is stale");
            Check(!glide.GlideBack(0f).isCurrent, "7. gliding back a second time on the stale handle does nothing");
        });
        steps.Add(() => {
            float h = Heard(tok);
            Check(Mathf.Abs(h - 8000f) < 1f, "7. gliding back returned it to where it began (" + h.ToString("F0") + " Hz)");
            tok.GlideToSnapshot("Calm", 0f);
            tok.Kill(); tok.Play();
        });
        steps.Add(() => {
            float h = Heard(tok);
            Check(Mathf.Abs(h - 400f) < 1f, "7. played again, the token starts on its snapshot (" + h.ToString("F0") + " Hz)");
            Check(!back.isCurrent, "7. a handle from an earlier run is stale");
            int before = 0; foreach (var e in ZoundDiagnostics.Entries) if (e.kind == ZoundDiagnostics.Kind.MissingSnapshot && e.detail == "nosuchsnap") before = e.count;
            Check(!tok.GlideToSnapshot("No Such Snap", 100f).isCurrent, "7. a snapshot name nothing has does nothing");
            int after = 0; foreach (var e in ZoundDiagnostics.Entries) if (e.kind == ZoundDiagnostics.Kind.MissingSnapshot && e.detail == "nosuchsnap") after = e.count;
            Check(after == before + 1, "7. and is recorded in the diagnostics list");
            tok.GlideToSnapshot("Default", 0f);
            tok.Kill(); tok.Play();
        });
        steps.Add(() => {
            float h = Heard(tok);
            Check(Mathf.Abs(h - 8000f) < 1f, "7. after gliding to Default, the next run is as authored (" + h.ToString("F0") + " Hz)");
            tok.Kill();
        });

        // One step every 0.3 s of editor time, so each play's voice has been made and has rendered before it is read.
        int next = 0; double at = EditorApplication.timeSinceStartup + 0.3;
        EditorApplication.CallbackFunction tick = null;
        tick = () => {
            if (EditorApplication.timeSinceStartup < at) return;
            try { steps[next](); }
            catch (System.Exception e) { Check(false, "7. step " + next + " threw " + e.Message); next = steps.Count; }
            next++;
            at = EditorApplication.timeSinceStartup + 0.3;
            if (next >= steps.Count) {
                EditorApplication.update -= tick;
                try { tok.Kill(); } catch { }
                LastTokenResult = (fail == 0 ? "PASS - glides through tokens.\n" : "FAIL - " + fail + " problem(s).\n") + sb;
                Debug.Log("[ZoundsGlideCheck tokens]\n" + LastTokenResult);
            }
        };
        EditorApplication.update += tick;
    }
}
