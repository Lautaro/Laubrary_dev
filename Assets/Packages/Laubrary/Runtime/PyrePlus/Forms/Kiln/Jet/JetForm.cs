// JetForm — Kiln "Flame / agent3" THE FLAMETHROWER STREAM (the directional jet), generation 2, as a PlusForm — the
// first consumer of the shared jet engine (PlusJetEngine.cs, whose header carries the algorithm, the per-component
// ported / approximated / dropped list and the hand-off note for the radial and explosive forks).
//
// Source: D:/CODEZ/Kiln/projects/Flame/agents/agent3 (gen.py + flame3/, MANIFEST "generation 2 — THE FLAMETHROWER
// STREAM, REGRADED"). The five published draws are ONE program (`flame3.jet.frame` + `grad.shade`) run with five
// parameter sets — needle / cone / drooping / swept / ringed — so the form has a `variant` and one settings box per
// variant, each a `JetSettings` built by `JetDraws.<Draw>()` from that draw's contract values (D:/Claude@GDrive/Flame/
// GEN2/contract/agent3/<draw>/params.json). Default variant = `gout` (#002): MANIFEST calls it "the classic … the draw
// that carries the territory … the flagship"; `lance` is #001 only by the listing order. The shared placement dials
// default to gout's own nozzle.
//
// Dial names in each box ARE the contract's parameter keys (round_at → roundAt, warp_cell → warpCell …) so
// `SetContractParam` loads any draw and a reader of MANIFEST.md / Appendix A finds the same words; the meaning is in
// every [Tooltip]. Units: the program runs in the draw's SOURCE px frame (w × h, y-down, +aim = down) sampled through
// u = (Scale × canvas W) / w, so every px value inside a box is in source px, `reach` / `buoy` / `grav` are canvas
// WIDTHS of that frame, and the picture is the source's scaled to the dialled scale — the same jet at PyrePlus's 64 px
// and at the contract's canvas. Shared dials are canvas fractions: Anchor X / Y (the nozzle; Y from the top, the
// contract's convention), Scale.
//
// The loop: phase = frame / frameCount, exactly periodic by construction (slot ages mod 1, noise scrolled by whole
// lattice periods per axis, integer pulse / sweep counts) — one period = the clip, whatever the frame count.
using System;
using System.Collections;
using UnityEngine;

namespace Laubrary.PyrePlus.Forms.Kiln
{
    [Serializable]
    [PlusFormInfo("Jet", group: "Kiln/Flame", icon: "flame")]
    public sealed class JetForm : PlusForm, IPlusFieldPublisher, IPlusRampProbe
    {
        public override string DisplayName => "Jet";
        public override string Description =>
            "A DIRECTIONAL jet of burning gas thrown from a nozzle — the flamethrower stream (Kiln Flame, agent3 gen 2): "
            + "puffs leave fast, small and stretched along their velocity, slow under drag, fatten by entrainment and only "
            + "lift under buoyancy once they are slow, so the stream is a needle at the root, a cone through the middle "
            + "and a rolling boil at the tip; a shared domain warp scrolling downstream turns the puffs into one sheet of "
            + "fire. Colour is a continuous linear-light ramp with per-stop opacity ceilings, crossfading into a second "
            + "ramp where the gas goes sooty. Five variants: Lance (a cutting torch with standing shock diamonds), Gout "
            + "(the classic weapon gout shedding fireballs), Sputter (fuel-rich, drooping, guttering twice a loop), Whip "
            + "(the weapon swung — an S-curve), Wyrm (a lance collared by vortex rings). Loops seamlessly over the clip. "
            + "Colour comes from the variant's Ramp, NOT the layer Fill. SWARM: off = one jet from Anchor X / Y aimed "
            + "by the variant's Aim; on = one jet per swarm particle rooted at its position, turned by its orientation, "
            + "sized by Swarm Size and its depth shading, heats summing where they overlap.";

        /// Colour is the variant's ramp, never the layer Fill.
        public override bool UsesFill => false;

        public enum Variant { Lance, Gout, Sputter, Whip, Wyrm }

        [Tooltip("Which of the five published jets this is. Each is its own settings box below; switching keeps the shared placement dials.")]
        public Variant variant = Variant.Gout;

        // ── placement (shared) ──
        [Tooltip("Where the nozzle sits across the canvas, as a fraction of the width. Source: gout's nozzle is 6 % in.")]
        [Range(0f, 1f)] public float anchorX = 0.06f;
        [Tooltip("Where the nozzle sits down the canvas, as a fraction of the height from the TOP (the source's y-down frame: a positive Aim points down). Source: gout's nozzle is 68 % down.")]
        [Range(0f, 1f)] public float anchorY = 0.68f;
        [Tooltip("Scale of the jet: the variant's source frame width as a fraction of the canvas width; every length inside the variant scales with it (1 = the source frame spans the canvas).")]
        [Range(0.2f, 2f)] public float scale = 1f;

        // ── the variants ──
        [ZUIShowIf("variant", "Lance")] [Tooltip("lance's contract values (seed 11, 168 × 52, 28 frames @ 16 fps) — THE NEEDLE: a cutting torch, indigo → cyan → white, standing shock diamonds the gas travels through.")] public JetSettings lance = JetDraws.Lance();
        [ZUIShowIf("variant", "Gout")] [Tooltip("gout's contract values (seed 23, 160 × 92, 30 frames @ 15 fps) — THE CLASSIC: a broad cone rolling over at the tip, brick → white with a greasy soot crossfade, fireballs tumbling off the end.")] public JetSettings gout = JetDraws.Gout();
        [ZUIShowIf("variant", "Sputter")] [Tooltip("sputter's contract values (seed 37, 152 × 116, 30 frames @ 13 fps) — THE DIRTY ONE: fuel-rich, drooping under its own weight, guttering twice a loop, an ember ramp that never reaches white, 16 shades.")] public JetSettings sputter = JetDraws.Sputter();
        [ZUIShowIf("variant", "Whip")] [Tooltip("whip's contract values (seed 53, 168 × 104, 32 frames @ 15 fps) — THE WEAPON SWUNG: the aim sweeps 20° either side once a loop and the stream is an S-curve, olive → gold → cream with a hot orange head, 24 shades.")] public JetSettings whip = JetDraws.Whip();
        [ZUIShowIf("variant", "Wyrm")] [Tooltip("wyrm's contract values (seed 71, 180 × 100, 30 frames @ 13 fps) — THE RINGED ONE: a coherent lance shedding three vortex rings a loop, violet → periwinkle → cyan-white, the most transparent, 32 shades.")] public JetSettings wyrm = JetDraws.Wyrm();

        // ── swarm ──
        [PlusSwarmOnly]
        [Tooltip("Scale of each swarm particle's jet as a fraction of the solo Scale (the swarm's own size / depth shading multiplies it).")]
        [Range(0.1f, 1f)] public float swarmSize = 0.5f;

        // ── runtime ──
        [NonSerialized] JetScratch _scratch;
        [NonSerialized] JetShade.Lut _hot, _soot; [NonSerialized] int _hotHash, _sootHash;
        [NonSerialized] float[] _dumpH, _dumpT, _dumpRt;

        public JetSettings Active => variant switch
        {
            Variant.Lance => lance, Variant.Sputter => sputter, Variant.Whip => whip, Variant.Wyrm => wyrm, _ => gout,
        };

        /// The primary ramp's colour at contract position t (the hot LUT's sRGB entry, alpha = its opacity ceiling).
        Color IPlusRampProbe.ProbeRamp(float t)
        {
            EnsureLuts(Active);
            int idx = Mathf.Min((int)(Mathf.Clamp01(t) * (JetShade.N_LUT - 1) + 0.5f), JetShade.N_LUT - 1);
            return new Color(_hot.sr[idx] / 255f, _hot.sg[idx] / 255f, _hot.sb[idx] / 255f, (float)_hot.a[idx]);
        }

        void IPlusFieldPublisher.PublishFields(Action<string, float[]> sink)
        {
            if (_dumpH != null) sink("H", _dumpH);
            if (_dumpT != null) sink("T", _dumpT);
            if (_dumpRt != null) sink("ramp_t", _dumpRt);
            _dumpH = _dumpT = _dumpRt = null;
        }

        void EnsureLuts(JetSettings s)
        {
            int h = RampHash(s.ramp);
            if (_hot == null || _hotHash != h) { _hot = JetShade.Bake(s.ramp); _hotHash = h; }
            if (!s.HasSoot) { _soot = null; _sootHash = 0; return; }
            int h2 = RampHash(s.sootRamp);
            if (_soot == null || _sootHash != h2) { _soot = JetShade.Bake(s.sootRamp); _sootHash = h2; }
        }

        static int RampHash(PlusRamp r)
        {
            if (r == null) return 0;
            unchecked
            {
                int h = (int)2166136261u ^ (int)r.space;
                if (r.stops != null) foreach (var s in r.stops) { h = (h ^ s.pos.GetHashCode()) * 16777619; h = (h ^ s.color.GetHashCode()) * 16777619; }
                return h;
            }
        }

        public override void Render(in PlusFormCtx ctx, Color32[] target)
        {
            int W = ctx.W, H = ctx.H, n = W * H;
            _scratch ??= new JetScratch();
            _scratch.Ensure(n);
            _scratch.Clear();
            var s = Active;
            // The spec seed IS the Kiln seed (layer 0 of seed 23 draws gout's own slot table, sparks and lattices); further
            // layers decorrelate by a large stride, swarm instances by their index.
            int seed = unchecked(ctx.seed + ctx.layerSalt * 1000003);
            double phase = ctx.frameIndex / (double)Math.Max(1, ctx.frameCount);
            double uSolo = scale * W / Math.Max(s.w, 1);
            var program = JetProgram.Default;

            if (ctx.swarm == null)
                program.Frame(s, JetFrame.Solo(W, H, anchorX * W, anchorY * H, uSolo, seed), phase, _scratch);
            else
                for (int i = 0; i < ctx.swarm.Length; i++)
                {
                    var sp = ctx.swarm[i];
                    if (sp.own < 0f || sp.own > 1f) continue;
                    // swarm positions are y-up canvas px and orientations CCW in that frame; the program runs y-down
                    var fr = JetFrame.Solo(W, H, sp.x, H - sp.y, uSolo * swarmSize * Math.Max(sp.sizeMul, 0.01f), unchecked(seed + sp.index * 104729));
                    fr.rot = -sp.orientDeg * Math.PI / 180.0;
                    fr.amp = Math.Max(sp.brightMul, 0f);
                    program.Frame(s, fr, phase, _scratch);
                }

            EnsureLuts(s);
            bool dump = PlusFormDebug.FieldSink != null;
            float[] rampT = dump ? new float[n] : null;
            JetShade.Default.Shade(s, _hot, _soot, _scratch.H, _scratch.T, W, H, target, rampT);

            // the layer's Alpha envelope, one overall multiplier (the contract has none — forced to 1 by the harness)
            if (ctx.alpha < 1f)
                for (int i = 0; i < n; i++)
                    if (target[i].a != 0) target[i].a = (byte)Mathf.Clamp(Mathf.RoundToInt(target[i].a * ctx.alpha), 0, 255);

            if (dump)
            {
                // the contract's planes are y-down; the harness expects renderer buffers (y-up) and flips them itself
                _dumpH = new float[n]; _dumpT = new float[n]; _dumpRt = rampT;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int i = y * W + x, o = (H - 1 - y) * W + x;
                        _dumpH[o] = _scratch.H[i] * s.gain; _dumpT[o] = _scratch.T[i];
                    }
            }
            ApplyPixelModifiers(ctx, target);
        }

        /// The layer's pixel modifiers, per lit canvas pixel (the heat-ramp forms' convention).
        void ApplyPixelModifiers(in PlusFormCtx ctx, Color32[] target)
        {
            if (ctx.pix == null || ctx.pix.Length == 0) return;
            int W = ctx.W, H = ctx.H;
            int pixHash = PyrePlusRenderer.Hash(ctx.seed, PyrePlusRenderer.ModParticleIndex, ctx.layerSalt, 0x1f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    var c = target[i];
                    if (c.a == 0) continue;
                    var col = new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f);
                    float alpha = c.a / 255f;
                    var info = new Laubrary.SpriteFx.PixelInfo(x, y, x + 0.5f, y + 0.5f, ctx.frameIndex, ctx.phase, ctx.life, pixHash, W, H);
                    bool keep = true;
                    for (int m = 0; m < ctx.pix.Length && keep; m++) keep = ctx.pix[m].ApplyPixel(ref col, ref alpha, info);
                    target[i] = keep
                        ? new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(col.r * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(col.g * 255f), 0, 255),
                                      (byte)Mathf.Clamp(Mathf.RoundToInt(col.b * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255))
                        : default;
                }
        }

        // ── contract loading, for the parity runs ──

        /// Apply a contract param by its key: `tag`/`draw` picks the variant; `w`/`h`/`nozzle` set the active box's source
        /// frame AND the shared placement dials for PyrePlus's square max(w, h) canvas with the frame letterboxed at the
        /// centre (left column ⌊(S − w)/2⌋, top row ⌊(S − h)/2⌋ — what `PlusParityDump.DumpOptions.cropW/cropH` cuts back
        /// out; Scale = w / S); `ramp` (the contract's `extra.ramp` name) sets the box's ramps and crossfade window; every
        /// other numeric key goes to the same-named field of the ACTIVE variant's box. Informational keys return true;
        /// unknown strings false.
        public bool SetContractParam(string key, object value)
        {
            switch (key)
            {
                case "tag": case "draw": case "variant":
                    if (Enum.TryParse(value?.ToString(), true, out Variant v)) { variant = v; return true; }
                    return false;
                case "w": Active.w = Convert.ToInt32(value); ResolveGeometry(); return true;
                case "h": Active.h = Convert.ToInt32(value); ResolveGeometry(); return true;
                case "nozzle":
                    if (value is IList nz && nz.Count == 2) { Active.nozzleX = Convert.ToSingle(nz[0]); Active.nozzleY = Convert.ToSingle(nz[1]); ResolveGeometry(); return true; }
                    return false;
                case "ramp":
                {
                    string name = value?.ToString();
                    var r = PlusRampPresets.Jet(name); if (r == null) return false;
                    Active.ramp = r; Active.sootRamp = PlusRampPresets.JetSecondary(name);
                    var win = PlusRampPresets.JetSootWindow(name); Active.sootLo = win.x; Active.sootHi = win.y;
                    return true;
                }
                case "frames": case "fps": case "seed": return true;
            }
            if (value is string) return false;
            return SetField(Active, key.Replace("_", ""), value);
        }

        /// The letterbox of the active box's source frame on the square canvas.
        public void ResolveGeometry()
        {
            var s = Active;
            if (s.w <= 0 || s.h <= 0) return;
            double S = Math.Max(s.w, s.h), left = Math.Floor((S - s.w) / 2.0), top = Math.Floor((S - s.h) / 2.0);
            scale = (float)(s.w / S);
            anchorX = (float)((left + s.nozzleX * s.w) / S);
            anchorY = (float)((top + s.nozzleY * s.h) / S);
        }

        static bool SetField(object owner, string name, object value)
        {
            foreach (var fi in owner.GetType().GetFields())
            {
                if (!string.Equals(fi.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (fi.FieldType == typeof(int)) fi.SetValue(owner, Convert.ToInt32(value));
                else if (fi.FieldType == typeof(float)) fi.SetValue(owner, Convert.ToSingle(value));
                else if (fi.FieldType == typeof(bool)) fi.SetValue(owner, value is bool b ? b : Convert.ToSingle(value) != 0f);
                else return false;
                return true;
            }
            return false;
        }
    }
}
