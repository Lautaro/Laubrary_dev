// TorchForm — Kiln "Flame / agent2" the GROUNDED FLAME (torch / brazier / campfire), generation 2, as a PlusForm.
//
// Source: D:/CODEZ/Kiln/projects/Flame/agents/agent2 (gen.py + flamelib.py, MANIFEST "generation 2 — the GROUNDED
// flame, ranged by HOW IT MOVES"). The algorithm and the per-component ported / approximated / dropped list are in
// PlusTorch.cs's header. The five published draws are ONE program (`heat_field`) run with five parameter sets, ranged
// on MOTION — calm → pulsating → licking → rolling → violent — so the form has a `variant` and one settings box per
// variant, each box a `TorchSettings` whose defaults are that draw's contract values (D:/Claude@GDrive/Flame/GEN2/
// contract/agent2/<draw>/params.json). Default variant = `barbs` (#003 of the set, reference 0115): MANIFEST names no
// canonical and #001 `emberbed` is a 124 px-wide BRAZIER that cannot fit a 64 px square; `barbs` is the 64 px-wide
// licking TORCH the form is named for. The shared placement dials default to barbs' own frame.
//
// Dial names in each box ARE the contract's parameter keys (h_flame → hFlame, tongue_x → tongueX …) so
// Envelopes: a dial `Accumulate` or the shade reads as a per-frame AMOUNT (placement, the width / falloff / ceiling /
// root profile, the breathing / sway / lean / lash / pulse / bulge amplitudes, the noise amplitudes and bias, the glow,
// the tongue / ember gains, cooling, the alpha thresholds, the ramp top) is a ZUIValue — Static draws the same bytes as
// a plain float, a Curve drives it over the layer's life. `Prepare` resolves the form's dials and the ACTIVE box's into
// `live` structs the program reads. `hFlame` (the source-px reference every other px dial is relative to), noise
// frequencies / scales / flicker speed (the texture would swim), phases, counts, the pulse schedule, the per-element
// tongue / ember ranges and the noise kinds stay plain.
// `SetContractParam` loads any draw and a reader of MANIFEST.md / Appendix A finds the same words; the meaning is in
// every [Tooltip]. Units: the program runs in the draw's SOURCE px frame (X from the axis, Y above the fuel bed) and
// is sampled through u = (Height × canvas) / hFlame, so every px value inside a box is in source px and the picture
// is the source's magnified to the dialled height — the same flame at PyrePlus's 64 px and at the contract canvas.
// Shared dials are canvas fractions: Axis X, Ground (the fuel bed, from the bottom), Height.
//
// The loop: t = frame / frameCount, exactly periodic by construction (sinusoids in 2πt, noise scrolled by whole
// lattice periods, integer surge / whip counts, population ages (t + phase) mod 1) — one period = the clip, whatever
// the frame count; the seam frame N−1 → 0 is an ordinary step.
using System;
using Laubrary.SpriteFx;
using System.Collections;
using UnityEngine;

namespace Laubrary.PyrePlus.Forms.Kiln
{
    [Serializable]
    [PlusFormInfo("Torch", group: "Kiln/Flame", icon: "flame")]
    public sealed class TorchForm : PlusForm, IPlusFieldPublisher, IPlusRampProbe
    {
        public override string DisplayName => "Torch";
        public override string Description =>
            "A GROUNDED flame licking upward from a fixed fuel bed — torch, brazier or campfire (Kiln Flame, agent2 gen 2): "
            + "an anchored envelope plus periodic 3-D Perlin noise (ridged for licks, billow for lumps) on a 3× supersampled "
            + "grid, a divergence-free curl warp, a surge envelope with a lump of heat riding up the column, a whip whose wave "
            + "travels up the flame, and populations of discrete tongues and embers summed into the heat. Colour is the "
            + "variant's hard 7-band cel palette on a copy of the heat cooled with height; alpha is a continuous smoothstep on "
            + "the raw heat. Five variants ranged on motion: Emberbed (calm coals), Surge (pulsating, fed twice a loop), Barbs "
            + "(licking torch shedding curved barbs), Curl (heavy rolling lobes), Lash (violent, the whole column whips). Loops "
            + "seamlessly over the clip's frame count. Colour comes from the variant's Ramp, NOT the layer Fill. SWARM: off = "
            + "one flame rooted at Axis X / Ground; on = one flame per swarm particle rooted at its position, sized by Swarm "
            + "Size and the particle's depth shading, heats summing where they overlap.";

        /// Colour is the variant's banded palette, never the layer Fill.
        public override bool UsesFill => false;

        public enum Variant { Emberbed, Surge, Barbs, Curl, Lash }

        [Tooltip("Which of the five published flames this is, calm → violent. Each is its own settings box below; switching keeps the shared placement dials.")]
        public Variant variant = Variant.Barbs;

        // ── placement (shared) ──
        [Tooltip("Where the flame axis sits across the canvas, as a fraction of the width.")]
        [Range(0.1f, 0.9f)] public ZUIValue axisX = new ZUIValue(0.5f);
        [Tooltip("Height of the fuel bed (the flame's root) above the canvas bottom, as a fraction of the canvas height. Nothing burns below it. Source: barbs' bed is 10 px up a 118 px frame.")]
        [Range(0f, 0.5f)] public ZUIValue ground = new ZUIValue(10f / 118f);
        [Tooltip("Reach of the flame (the variant's h_flame) as a fraction of the canvas height; every length inside the variant scales with it. Source: barbs reaches 74 px on its 118 px frame.")]
        [Range(0.1f, 1.5f)] public ZUIValue height = new ZUIValue(74f / 118f);

        // ── the variants ──
        [ZUIShowIf("variant", "Emberbed")] [Tooltip("emberbed's contract values (seed 811, 124 × 80, 26 frames @ 12 fps) — THE CALM END: a brazier burned down to coals, one slow breath per loop.")] public TorchSettings emberbed = TorchSettings.Emberbed();
        [ZUIShowIf("variant", "Surge")] [Tooltip("surge's contract values (seed 233, 64 × 126, 24 frames @ 15 fps) — THE PULSATING ONE: a torch being fed, two surges per loop, a lump of heat riding up each.")] public TorchSettings surge = TorchSettings.Surge();
        [ZUIShowIf("variant", "Barbs")] [Tooltip("barbs' contract values (seed 115, 64 × 118, 24 frames @ 16 fps) — THE LICKING ONE (0115): a near-parallel column that sheds curved barbs from both flanks.")] public TorchSettings barbs = TorchSettings.Barbs();
        [ZUIShowIf("variant", "Curl")] [Tooltip("curl's contract values (seed 139, 96 × 112, 24 frames @ 11 fps) — THE ROLLING ONE (0139 / 0140): chunky lobes turning over as they rise.")] public TorchSettings curl = TorchSettings.Curl();
        [ZUIShowIf("variant", "Lash")] [Tooltip("lash's contract values (seed 707, 104 × 124, 22 frames @ 20 fps) — THE VIOLENT END: a fire being blown about, the whole column whips and tears.")] public TorchSettings lash = TorchSettings.Lash();

        // ── swarm ──
        [PlusSwarmOnly]
        [Tooltip("Height of each swarm particle's flame as a fraction of the solo Height (the swarm's own size / depth shading multiplies it).")]
        [Range(0.1f, 1f)] public ZUIValue swarmSize = new ZUIValue(0.5f);

        // ── runtime ──
        [NonSerialized] float[] _Fp, _C, _pr, _pg, _pb, _pa, _A;
        [NonSerialized] float[] _dumpH, _dumpC, _dumpT, _dumpA;
        [NonSerialized] TorchScratch _scratch;
        [NonSerialized] float[] _thr; [NonSerialized] Color32[] _cols;

        /// The form's own envelopes resolved at one layer life (slots 0–3).
        public struct Live { public float axisX, ground, height, swarmSize; }
        [NonSerialized] public Live live;

        public override void Prepare(in PlusFormPrepareCtx ctx)
        {
            live = new Live { axisX = ctx.Eval(axisX, 0), ground = ctx.Eval(ground, 1), height = ctx.Eval(height, 2), swarmSize = ctx.Eval(swarmSize, 3) };
            Active.Resolve(ctx);
        }

        public TorchSettings Active => variant switch
        {
            Variant.Emberbed => emberbed, Variant.Surge => surge, Variant.Curl => curl, Variant.Lash => lash, _ => barbs,
        };

        public TorchSource Source => variant switch
        {
            Variant.Emberbed => TorchSource.Emberbed, Variant.Surge => TorchSource.Surge, Variant.Curl => TorchSource.Curl,
            Variant.Lash => TorchSource.Lash, _ => TorchSource.Barbs,
        };

        /// The band colour at contract ramp position t (hard steps — what the pixels are painted with), opaque.
        Color IPlusRampProbe.ProbeRamp(float t)
        {
            EnsureBands(Active);
            return PlusShade.Banded(t, _thr, _cols);
        }

        void IPlusFieldPublisher.PublishFields(Action<string, float[]> sink)
        {
            if (_dumpH != null) sink("H", _dumpH);
            if (_dumpC != null) sink("C", _dumpC);
            if (_dumpT != null) sink("ramp_t", _dumpT);
            if (_dumpA != null) sink("alpha_f", _dumpA);
            _dumpH = _dumpC = _dumpT = _dumpA = null;
        }

        // The band table's own cached lookup arrays (PlusBands rebuilds them only when its content changes).
        void EnsureBands(TorchSettings s)
        {
            s.ramp ??= new PlusBands();
            _thr = s.ramp.Thresholds; _cols = s.ramp.Colors32;
        }

        public override void Render(in PlusFormCtx ctx, Color32[] target)
        {
            // The renderer Prepares before Render; a direct caller (a test, a probe) may not — same funnel, same life, idempotent.
            Prepare(ctx.PrepareCtxAt(ctx.life));
            const int SS = PlusTorch.SS;
            int W = ctx.W, H = ctx.H, W2 = W * SS, H2 = H * SS, n2 = W2 * H2;
            if (_Fp == null || _Fp.Length != n2)
            {
                _Fp = new float[n2]; _C = new float[n2]; _pr = new float[n2]; _pg = new float[n2]; _pb = new float[n2]; _pa = new float[n2]; _A = null;
            }
            Array.Clear(_Fp, 0, n2); Array.Clear(_C, 0, n2);
            _scratch ??= new TorchScratch();
            bool dump = PlusFormDebug.FieldSink != null;
            if (dump && (_A == null || _A.Length != n2)) _A = new float[n2];

            var s = Active;
            var src = Source;
            // The spec seed IS the Kiln seed (layer 0 of seed 115 draws barbs' own lattice, tongues and embers); further
            // layers decorrelate by a large stride, swarm instances by their index.
            uint seed = unchecked((uint)((long)ctx.seed + (long)ctx.layerSalt * 1000003L));
            double t = ctx.frameIndex / (double)Math.Max(1, ctx.frameCount);
            double uSolo = live.height * H / Math.Max(s.hFlame, 1f);

            if (ctx.swarm == null)
                PlusTorch.Accumulate(s, Frame(W2, H2, live.axisX * W, H * (1.0 - live.ground), uSolo, 1.0, seed, src), t, _scratch, _Fp, _C);
            else
                for (int i = 0; i < ctx.swarm.Length; i++)
                {
                    var sp = ctx.swarm[i];
                    if (sp.own < 0f || sp.own > 1f) continue;
                    double u = uSolo * live.swarmSize * Math.Max(sp.sizeMul, 0.01f);
                    // swarm positions are y-up canvas px; the program runs y-down, flipped back at the write
                    PlusTorch.Accumulate(s, Frame(W2, H2, sp.x, H - sp.y, u, sp.brightMul, unchecked(seed + (uint)(sp.index * 104729)), src), t, _scratch, _Fp, _C);
                }

            // ── shade on the supersampled grid: hard bands on the cooled field, smoothstep alpha on the raw heat ──
            EnsureBands(s);
            double top = Math.Max(s.live.rampTop, 1e-6f), aLo = s.live.aLo, aHi = s.live.aHi;
            float la = ctx.alpha;
            for (int k = 0; k < n2; k++)
            {
                double a = PlusTorch.SmoothStep(aLo, aHi, _Fp[k]);
                if (dump) _A[k] = (float)a;
                if (a <= 0.0) { _pr[k] = _pg[k] = _pb[k] = _pa[k] = 0f; continue; }
                var col = PlusShade.Banded((float)(_C[k] / top), _thr, _cols);
                float af = (float)a * la;
                _pr[k] = col.r / 255f * af; _pg[k] = col.g / 255f * af; _pb[k] = col.b / 255f * af; _pa[k] = af;
            }
            PlusSupersample.Downsample(_pr, _pg, _pb, _pa, W2, H2, SS, target, W, H, flipY: true);

            if (dump)
            {
                _dumpH = BoxDown(_Fp, W, H, 1f, false);
                _dumpC = BoxDown(_C, W, H, 1f, false);
                _dumpT = BoxDown(_C, W, H, (float)(1.0 / top), true);
                _dumpA = BoxDown(_A, W, H, 1f, false);
            }
            ApplyPixelModifiers(ctx, target);
        }

        static TorchFrame Frame(int W2, int H2, double ox, double oy, double u, double amp, uint seed, in TorchSource src) => new TorchFrame
        {
            W2 = W2, H2 = H2, ox = ox, oy = oy, u = u, amp = amp, seed = seed, src = src,
        };

        /// The contract's planes are the 3×3 box mean of the supersampled plane at frame resolution (y-down); the harness
        /// expects renderer buffers (y-up) and flips them itself, so the copy is written bottom row first.
        static float[] BoxDown(float[] big, int W, int H, float scale, bool clip01)
        {
            const int SS = PlusTorch.SS;
            int W2 = W * SS;
            var o = new float[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float sum = 0f;
                    for (int sy = 0; sy < SS; sy++)
                    {
                        int row = (y * SS + sy) * W2 + x * SS;
                        for (int sx = 0; sx < SS; sx++)
                        {
                            float v = big[row + sx] * scale;
                            if (clip01) v = v < 0f ? 0f : (v > 1f ? 1f : v);
                            sum += v;
                        }
                    }
                    o[(H - 1 - y) * W + x] = sum / (SS * SS);
                }
            return o;
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
        [NonSerialized] double _cW, _cH, _cCx, _cBase, _cHf;

        /// Apply a contract param by its key: `tag`/`draw` picks the variant; `w`/`h`/`cx`/`base_y`/`h_flame` (source px)
        /// become the placement dials on PyrePlus's square max(w, h) canvas with the frame letterboxed at the centre
        /// (left column ⌊(S − w)/2⌋, top row ⌊(S − h)/2⌋ — what `PlusParityDump.DumpOptions.cropW/cropH` cuts back out),
        /// and `h_flame` also sets the box's reference reach; `ramp` picks the preset by its Kiln name and sets Ramp Top;
        /// `big_kind`/`turb_kind` parse the noise kind; a two-element list sets a range dial; every other numeric key goes
        /// to the same-named field of the ACTIVE variant's box. Informational keys return true; unknown strings false.
        public bool SetContractParam(string key, object value)
        {
            switch (key)
            {
                case "tag": case "draw": case "variant":
                    if (Enum.TryParse(value?.ToString(), true, out Variant v)) { variant = v; return true; }
                    return false;
                case "w": _cW = Convert.ToDouble(value); ResolveGeometry(); return true;
                case "h": _cH = Convert.ToDouble(value); ResolveGeometry(); return true;
                case "cx": _cCx = Convert.ToDouble(value); ResolveGeometry(); return true;
                case "base_y": _cBase = Convert.ToDouble(value); ResolveGeometry(); return true;
                case "h_flame": _cHf = Convert.ToDouble(value); Active.hFlame = (float)_cHf; ResolveGeometry(); return true;
                case "ramp":
                {
                    var r = PlusRampPresets.TorchBands(value?.ToString()); if (r == null) return false;
                    Active.ramp = r; Active.rampTop = new ZUIValue(PlusRampPresets.TorchTop(value?.ToString())); return true;
                }
                case "big_kind": return Enum.TryParse(value?.ToString(), true, out Active.bigKind);
                case "turb_kind": return Enum.TryParse(value?.ToString(), true, out Active.turbKind);
                case "frames": case "fps": case "seed": case "supersample": case "lattice_period": return true;
            }
            string name = key.Replace("_", "");
            if (value is IList list && list.Count == 2)
                return SetPair(Active, name, Convert.ToSingle(list[0]), Convert.ToSingle(list[1]));
            if (value is string) return false;
            return SetField(Active, name, value);
        }

        void ResolveGeometry()
        {
            if (_cW <= 0 || _cH <= 0) return;
            double S = Math.Max(_cW, _cH), left = Math.Floor((S - _cW) / 2.0), top = Math.Floor((S - _cH) / 2.0);
            if (_cCx > 0) axisX = new ZUIValue((float)((left + _cCx) / S));
            if (_cBase > 0) ground = new ZUIValue((float)((S - (top + _cBase)) / S));
            if (_cHf > 0) height = new ZUIValue((float)(_cHf / S));
        }

        static bool SetPair(object owner, string name, float lo, float hi)
        {
            foreach (var fi in owner.GetType().GetFields())
            {
                if (!string.Equals(fi.Name, name, StringComparison.OrdinalIgnoreCase) || fi.FieldType != typeof(Vector2)) continue;
                fi.SetValue(owner, new Vector2(lo, hi));
                return true;
            }
            return false;
        }

        static bool SetField(object owner, string name, object value)
        {
            foreach (var fi in owner.GetType().GetFields())
            {
                if (!string.Equals(fi.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (fi.FieldType == typeof(int)) fi.SetValue(owner, Convert.ToInt32(value));
                else if (fi.FieldType == typeof(float)) fi.SetValue(owner, Convert.ToSingle(value));
                else if (fi.FieldType == typeof(bool)) fi.SetValue(owner, value is bool b ? b : Convert.ToSingle(value) != 0f);
                else if (fi.FieldType == typeof(ZUIValue)) fi.SetValue(owner, new ZUIValue(Convert.ToSingle(value)));   // a contract scalar = the Static value
                else return false;
                return true;
            }
            return false;
        }
    }
}
