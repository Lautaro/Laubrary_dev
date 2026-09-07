// JetFormBase — everything a jet-family form (JetForm, RadialJetForm, the explosive fork to come) does that is not
// "which program and which parameter sets": the placement dials, the solo / swarm instancing through `JetFrame`, the
// LUT cache, the shade call, the parity field publisher and ramp probe, the pixel modifiers, and the contract loader the
// parity harness drives. A concrete form supplies its variant enum, one settings box per variant (`Active`), the
// program that renders them (`Program`) and the variant parser; nothing else.
//
// Placement: the program runs in the active box's SOURCE px frame (w × h, y-down, +aim = down) sampled through
// u = (Scale × canvas W) / w, so every px value inside a box is in source px and `reach` / `buoy` / `grav` are canvas
// WIDTHS of that frame — the same picture at Pyre's 64 px and at the contract's canvas. Shared dials are canvas
// fractions: Anchor X / Y (the nozzle or centre; Y from the TOP, the contract's convention), Scale.
using System;
using Laubrary.SpriteFx;
using System.Collections;
using UnityEngine;

namespace Laubrary.Pyre.Forms.Kiln
{
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.PyrePlus.Forms.Kiln", "com.Lautaro-Arino.Laubrary.PyrePlus.Forms.Kiln", null)]
    public abstract class JetFormBase : PyreForm, IPlusFieldPublisher, IPlusRampProbe
    {
        /// Colour is the variant's ramp, never the layer Fill.
        public override bool UsesFill => false;

        // ── placement (shared) ──
        [Tooltip("Full name: \"Nozzle across frame\". Where the nozzle (or, for a radial jet, the centre) sits across the canvas, as a fraction of the width.")]
        [ZUILabel("Nozzle X")] [ZUIGroup("Placement & size", Tooltip = "Where the jet sits and how big it is.")]
        [Range(0f, 1f)] public ZUIValue anchorX = new ZUIValue(0.5f);
        [Tooltip("Full name: \"Nozzle down frame\". Where the nozzle (or centre) sits down the canvas, as a fraction of the height from the TOP (the source's y-down frame: a positive Aim points down).")]
        [ZUILabel("Nozzle Y")] [ZUIGroup("Placement & size")]
        [Range(0f, 1f)] public ZUIValue anchorY = new ZUIValue(0.5f);
        [Tooltip("Scale of the jet: the variant's source frame width as a fraction of the canvas width; every length inside the variant scales with it (1 = the source frame spans the canvas).")]
        [ZUILabel("Jet scale")] [ZUIGroup("Placement & size")]
        [Range(0.2f, 2f)] public ZUIValue scale = new ZUIValue(1f);

        // ── swarm ──
        [PyreSwarmOnly]
        [Tooltip("Full name: \"Swarm jet size\". Scale of each swarm particle's jet as a fraction of the solo Scale (the swarm's own size / depth shading multiplies it).")]
        [ZUILabel("Swarm size")] [ZUIGroup("Placement & size")]
        [Range(0.1f, 1f)] public ZUIValue swarmSize = new ZUIValue(0.5f);

        // ── runtime ──
        /// The placement envelopes resolved at one layer life (slots 0–3; the box's dials take 10 onward).
        public struct Live { public float anchorX, anchorY, scale, swarmSize; }
        [NonSerialized] public Live live;

        public override void Prepare(in PyreFormPrepareCtx ctx)
        {
            live = new Live { anchorX = ctx.Eval(anchorX, 0), anchorY = ctx.Eval(anchorY, 1), scale = ctx.Eval(scale, 2), swarmSize = ctx.Eval(swarmSize, 3) };
            Active.Resolve(ctx);
        }

        [NonSerialized] JetScratch _scratch;
        [NonSerialized] JetShade.Lut _hot, _soot; [NonSerialized] int _hotHash, _sootHash;
        [NonSerialized] float[] _dumpH, _dumpT, _dumpRt;

        /// The settings box of the selected variant.
        public abstract JetSettings Active { get; }
        /// The stage program that renders this family member (a stateless shared instance).
        protected abstract JetProgram Program { get; }
        /// Select a variant by its contract name (`tag` / `draw`); false when the name is not one of this form's.
        protected abstract bool TrySetVariant(string name);
        /// The shade pass (a stateless shared instance); a member whose settings carry `opaq` supplies its own subclass.
        protected virtual JetShade Shader => JetShade.Default;
        /// The settings one SWARM instance renders with; the default is the shared box (a member with an authored
        /// schedule hands each particle one blast of it).
        protected virtual JetSettings InstanceSettings(JetSettings s, int instanceIndex) => s;

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

        // Stops, blend space AND the ramp's Adjust knobs — the shared identity in PyreShade, so turning Hue
        // re-bakes this cached LUT exactly the way moving a stop does instead of repainting nothing.
        static int RampHash(PyreRamp r) => PyreShade.RampHash(r);

        public override void Render(in PyreFormCtx ctx, Color32[] target)
        {
            // The renderer Prepares before Render; a direct caller (a test, a probe) may not — same funnel, same life, idempotent.
            Prepare(ctx.PrepareCtxAt(ctx.life));
            int W = ctx.W, H = ctx.H, n = W * H;
            _scratch ??= new JetScratch();
            _scratch.Ensure(n);
            _scratch.Clear();
            var s = Active;
            // The spec seed IS the Kiln seed (layer 0 of seed 23 draws gout's own slot table, sparks and lattices); further
            // layers decorrelate by a large stride, swarm instances by their index.
            int seed = unchecked(ctx.seed + ctx.layerSalt * 1000003);
            // T-0167: frameIndex/frameCount (never reaching exactly 1 for the last real frame, index max = N-1) is
            // deliberately NOT ctx.life's frameIndex/(frameCount-1) convention — using ctx.life here would make the
            // clip's last frame phase-equal (mod 1) to its first, duplicating a frame on every loop wrap. That
            // matters only when frameCount is a REAL multi-frame timeline; a host with no such timeline (frameCount
            // <= 1, e.g. the Shaper composite bridge, which renders one phase sample per call with no frame index
            // of its own) always divided 0/1 = 0 here, freezing every phase-driven draw regardless of ctx.life —
            // the bug behind JetForm/RadialJetForm never animating when hosted. Falling back to ctx.life exactly
            // when there is no real timeline reaches the same value a real single-frame Pyre clip already got
            // (frameIndex 0 / frameCount 1 = 0 = ctx.life at frame 0), so this changes nothing for existing
            // multi-frame Pyre usage and only fixes the previously-frozen no-timeline host case.
            double phase = ctx.frameCount > 1 ? ctx.frameIndex / (double)ctx.frameCount : ctx.life;
            double uSolo = live.scale * W / Math.Max(s.w, 1);
            var program = Program;

            if (ctx.swarm == null)
                program.Frame(s, JetFrame.Solo(W, H, live.anchorX * W, live.anchorY * H, uSolo, seed), phase, _scratch);
            else
                for (int i = 0; i < ctx.swarm.Length; i++)
                {
                    var sp = ctx.swarm[i];
                    if (sp.own < 0f || sp.own > 1f) continue;
                    // swarm positions are y-up canvas px and orientations CCW in that frame; the program runs y-down
                    var fr = JetFrame.Solo(W, H, sp.x, H - sp.y, uSolo * live.swarmSize * Math.Max(sp.sizeMul, 0.01f), unchecked(seed + sp.index * 104729));
                    fr.rot = -sp.orientDeg * Math.PI / 180.0;
                    fr.amp = Math.Max(sp.brightMul, 0f);
                    program.Frame(InstanceSettings(s, sp.index), fr, phase, _scratch);
                }

            EnsureLuts(s);
            bool dump = PyreFormDebug.FieldSink != null;
            float[] rampT = dump ? new float[n] : null;
            Shader.Shade(s, _hot, _soot, _scratch.H, _scratch.T, W, H, target, rampT);

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
                        _dumpH[o] = _scratch.H[i] * s.live.gain; _dumpT[o] = _scratch.T[i];
                    }
            }
            ApplyPixelModifiers(ctx, target);
        }

        /// The layer's pixel modifiers, per lit canvas pixel (the heat-ramp forms' convention).
        void ApplyPixelModifiers(in PyreFormCtx ctx, Color32[] target)
        {
            if (ctx.pix == null || ctx.pix.Length == 0) return;
            int W = ctx.W, H = ctx.H;
            int pixHash = PyreRenderer.Hash(ctx.seed, PyreRenderer.ModParticleIndex, ctx.layerSalt, 0x1f);
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
        /// frame AND the shared placement dials for Pyre's square max(w, h) canvas with the frame letterboxed at the
        /// centre (left column ⌊(S − w)/2⌋, top row ⌊(S − h)/2⌋ — what `PyreParityDump.DumpOptions.cropW/cropH` cuts back
        /// out; Scale = w / S); `ramp` (the contract's `extra.ramp` name) sets the box's ramps and crossfade window; every
        /// other numeric / bool key goes to the same-named field of the ACTIVE variant's box. Informational keys return
        /// true; unknown strings false.
        public virtual bool SetContractParam(string key, object value)
        {
            switch (key)
            {
                case "tag": case "draw": case "variant":
                    return TrySetVariant(value?.ToString());
                case "w": Active.w = Convert.ToInt32(value); ResolveGeometry(); return true;
                case "h": Active.h = Convert.ToInt32(value); ResolveGeometry(); return true;
                case "nozzle":
                    if (value is IList nz && nz.Count == 2) { Active.nozzleX = Convert.ToSingle(nz[0]); Active.nozzleY = Convert.ToSingle(nz[1]); ResolveGeometry(); return true; }
                    return false;
                case "ramp":
                {
                    string name = value?.ToString();
                    var r = PyreRampPresets.Jet(name); if (r == null) return false;
                    Active.ramp = r; Active.sootRamp = PyreRampPresets.JetSecondary(name);
                    var win = PyreRampPresets.JetSootWindow(name); Active.sootLo = new ZUIValue(win.x); Active.sootHi = new ZUIValue(win.y);
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
            scale = new ZUIValue((float)(s.w / S));
            anchorX = new ZUIValue((float)((left + s.nozzleX * s.w) / S));
            anchorY = new ZUIValue((float)((top + s.nozzleY * s.h) / S));
        }

        protected static bool SetField(object owner, string name, object value)
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
