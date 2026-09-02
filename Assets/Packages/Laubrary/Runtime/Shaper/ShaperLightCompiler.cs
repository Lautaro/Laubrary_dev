using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// A compiled light rig plus its diagnostics.
    ///
    /// Every diagnostic block mirrors FC-4.4's shape exactly — a boolean, the FIRST offender's name, the full
    /// reason sentence, and A COUNT. The count is not decoration: <c>ShaperProgram.leadingNonAddCount</c>
    /// exists because recording only the first name meant "a tree with three offending bags would have
    /// surfaced one and silently hidden two" (<c>ShaperProgram.cs:90-95</c>), and the fill stage applied that
    /// correction from the start rather than after making the same mistake twice
    /// (<c>ShaperFillResolver.cs:108-114</c>). The same is done here, before the mistake rather than after.
    /// </summary>
    public sealed class ShaperLightProgram
    {
        /// <summary>The blittable rig the law takes by <c>in</c>.</summary>
        public ShaperLightRigCompiled rig;

        // ── LR-1.4: more than eight lights ────────────────────────────────────────────────────────────────

        /// <summary>
        /// True when the rig held more than <see cref="ShaperLightRigCompiled.MaxLights"/> <b>ENABLED</b>
        /// lights.
        ///
        /// <b>ENABLED and not AUTHORED, and the distinction is a fixed defect rather than a nicety.</b> The
        /// first build raised this from <c>list.Count</c>, which includes disabled entries, while the compile
        /// loop skips <c>!l.enabled</c> — so the two disagreed the moment anything was switched off and the
        /// tool told the author lights had been dropped when every enabled light was lit. Measured false in
        /// three of five configurations (9 authored / 1 disabled: reported "1 dropped", actually 0; 11
        /// authored / 5 disabled: reported 3, actually 0; 12 authored / first 8 disabled: reported 4, actually
        /// 0, and the sentence additionally claimed "the first 8" when the lit ones were 9–12).
        /// <see cref="ShaperLightCompiler.Finish"/> in this same file already counted the right thing
        /// (<c>prog.rig.count</c>); the two now agree.
        /// </summary>
        public bool hasTooManyLights;
        /// <summary>e.g. "the rig holds 11 enabled lights; only the first 8 enabled are lit. The cap is 8."</summary>
        public string tooManyLightsReason;
        /// <summary>How many ENABLED lights were dropped — not just that some were (FT-8b's lesson, LR-1.4).</summary>
        public int tooManyLightsCount;
        /// <summary>How many authored entries carried <c>enabled == true</c>. The number the cap is measured against.</summary>
        public int enabledLightCount;
        /// <summary>How many entries the rig held, enabled or not. Reported alongside, never used for the cap.</summary>
        public int authoredLightCount;

        // ── LR-7.2 / LR-7.3: a dial that is on but cannot do anything ─────────────────────────────────────

        /// <summary>
        /// True when a layer set <c>rimStrength</c> above zero while the document ambient is BLACK.
        ///
        /// Rim is ambient-tinted by LR-2.3's own arithmetic (<c>S += amb · rim</c>), so on a black ambient the
        /// rim term is <b>exactly</b> zero at every rim strength — measured <c>S = 0</c> at
        /// <c>rimStrength = 5</c> against <c>S = 0.06361176</c> at ambient 0.2. "Only my lamps, no ambient" is
        /// an ordinary authoring choice, and in it Rim Strength is a dead knob.
        ///
        /// The arithmetic is NOT changed here: it is what LIGHT-RIG-CONTRACT LR-2.3 specifies and the ruling
        /// is the owner's to overturn. What is changed is that the inertness is now DECLARED rather than
        /// silent, in the same shape as <see cref="hasTooManyLights"/>, which is what LR-7.3 requires of every
        /// control that does nothing.
        /// </summary>
        public bool hasInertRim;
        /// <summary><see cref="ShaperLightRig.RimNeedsRelief"/>'s black-ambient clause, verbatim (LR-7.1).</summary>
        public string inertRimReason;
        /// <summary>The first layer that asked for rim it cannot get.</summary>
        public string inertRimNode;
        /// <summary>How many layers did.</summary>
        public int inertRimCount;

        // ── LR-6.5 / LR-7.3: a Solids dial that does nothing on THIS form ─────────────────────────────────

        /// <summary>
        /// True when a Solids generator authored a dial away from its neutral value on a form that ignores it
        /// — <c>aspect</c> on a Gem, <c>depth</c> on an Orb, <c>roll</c> on a Ring, and the rest of
        /// <see cref="ShaperSolids.InertReason"/>'s table.
        ///
        /// Measured before this diagnostic existed, by rendering each form twice at rotation (35, 28, 12) and
        /// counting differing encoded pixels for a 1 → 0.4 sweep: Box 3470 / 3014, Pyramid 2589 / 2006, Can
        /// 2623 / <b>0</b>, Orb <b>0</b> / <b>0</b>, Gem <b>0</b> / <b>0</b>, Ring <b>0</b> / <b>0</b>
        /// (aspect / depth). Only Can's <c>depth</c> was documented. LR-7.3 states the standard those five
        /// silent zeroes violate in its own words: "A control that silently does nothing is the failure B4
        /// says must not survive the rebuild."
        ///
        /// The dials are NOT wired up, and that is deliberate rather than lazy: Pyre's own
        /// <c>BuildGemGeometry</c> (<c>PyreRenderer.cs:4796-4818</c>) ignores <c>solidAspect</c> and
        /// <c>solidDepth</c> too — a gem's proportions are <c>gemCrown</c> and <c>gemPavilion</c> — so wiring
        /// them would be INVENTING GEOMETRY that the reference does not have, against LR-6.2's "verbatim,
        /// because it is correct". Declared instead, so a UI can grey the control and show the reason.
        /// </summary>
        public bool hasInertDial;
        /// <summary>The full sentence: dial name, form name and the reason. e.g. "Aspect does nothing on a Gem: …".</summary>
        public string inertDialReason;
        /// <summary>The first offending dial's name, e.g. "Aspect".</summary>
        public string inertDialName;
        /// <summary>The form it was inert on, e.g. "Gem".</summary>
        public string inertDialForm;
        /// <summary>How many authored dials are inert — not just that some are (FT-8b's lesson).</summary>
        public int inertDialCount;

        // ── LR-4.5: a layer asked for shadows ─────────────────────────────────────────────────────────────

        /// <summary>True when any layer set <c>castShadows</c> or <c>receiveShadows</c>.</summary>
        public bool hasUnimplementedShadow;
        /// <summary><see cref="ShaperLightRig.ShadowsNotComputed"/>, VERBATIM (LR-7.1: one sentence per fact).</summary>
        public string unimplementedShadowReason;
        /// <summary>The first layer that asked.</summary>
        public string unimplementedShadowNode;
        /// <summary>How many did.</summary>
        public int unimplementedShadowCount;

        // ── LR-4.3: lights but nobody listening ───────────────────────────────────────────────────────────

        /// <summary>
        /// True when the document holds at least one ENABLED light and ZERO receivers. That is the exact
        /// silent-inertness case B4 names — an author adds three lights, sees nothing change, and has no way
        /// to find out why — and the diagnostic is the runtime analogue of a greyed control, following
        /// FC-4.4's reasoning unchanged: "the control is present, its reason is stated, and the thing it would
        /// have done does not happen".
        /// </summary>
        public bool hasLightsButNoReceivers;
        public string lightsButNoReceiversReason;
        /// <summary>How many layers have <c>receiveLighting</c> on.</summary>
        public int receiverCount;
        /// <summary>How many layers were considered.</summary>
        public int layerCount;
    }

    /// <summary>
    /// Compiles the authored rig and the per-layer response blocks into the blittable forms the law takes.
    ///
    /// LR-1.8: every scalar is sampled ONCE per compile through <see cref="ShaperValue.Sample"/>, never from
    /// inside a tile loop, and the <c>phase01</c> is the DOCUMENT's, not any node's. That document clock is
    /// the ONE exception to FC-1.4, and it needs its argument: FC-1.4 protects a thing ATTACHED TO A NODE from
    /// ignoring the node's window (<c>ShaperFillContract.cs:264-267</c>). A light is not attached to a node
    /// and has no window to ignore, so there is no second answer to give it and nothing to bypass. A node's
    /// own window remaps the node's clock and has no authority over the scene's lamps — which is also the
    /// intuitive reading: a light does not stop moving because one layer's animation finished.
    ///
    /// Using <see cref="ShaperValue.Sample"/> rather than <c>ZUIValue.Evaluate</c> is not style:
    /// <c>Evaluate(t)</c> divides by the value's <c>duration</c> and would sweep only the curve's first
    /// quarter (<c>ShaperValue.cs:9-13</c>). LT-4's stated mutation is exactly that substitution.
    /// </summary>
    public static class ShaperLightCompiler
    {
        // ── the legal range of every dial, and why there is one at all ────────────────────────────────────
        //
        // EVERY dial on the rig and on the response block is clamped HERE, at compile, and nowhere else. The
        // law does not clamp (LR-2.2 says so explicitly and it is right: an overbright highlight is
        // legitimate and the only clamp is at the byte) — which means a value the law cannot survive has to
        // be refused before it reaches the law.
        //
        // This was a MEASURED defect, not a precaution. `rimPower` was unclamped, `Shade` evaluates
        // `Mathf.Pow(1 - ndv, resp.rimPower)`, and on a flat normal `ndv` clamps to exactly 1 so the base is
        // exactly 0: a NEGATIVE exponent therefore yields +Infinity, and the rim is then tinted by the
        // ambient, so a BLACK ambient turns that Infinity into NaN via 0 * Inf. Measured through a full
        // 128x128 render of a rect fill:
        //
        //     rimPower = 2.2  (baseline)      ->     0 NaN,      0 Inf,     0 garbage pixels
        //     rimPower = -1,  ambient 0.2     ->     0 NaN, 19 200 Inf,     0 garbage pixels
        //     rimPower = -1,  ambient 0.0     -> 19 200 NaN,     0 Inf, 6 400 garbage pixels
        //
        // 19 200 = 6 400 covered samples x 3 colour channels; the 6 400 encoded pixels come out r == 0 with a
        // non-zero alpha — black garbage with no diagnostic. The build had ALREADY decided this class of
        // failure matters: `ShaperFillResolver.cs:1105` guards coverage with `if (!(ce > 0f)) continue;`,
        // chosen over `ce <= 0` precisely because "the two differ on NaN, and only the first rejects it".
        // That guard is UPSTREAM of the law and catches NaN arriving from COVERAGE; nothing guarded NaN
        // produced BY the law, which enters downstream at `LightSample`'s `cr = ar * lr + sr`.
        //
        // Reachability is not "an author types -1": `ShaperValue.Sample` returns `raw * v.Multiplier()` with
        // no floor, and the Curve and Oscillation modes evaluate an AnimationCurve that overshoots below zero
        // between keys as a matter of routine.
        //
        // The sweep every dial got, rather than only the one somebody happened to look at:
        //   ambientIntensity  >= 0     NaN/Inf -> 0.18. NaN here poisons every channel of every sample.
        //   ambient / light colour     the decoded triples, in case a Color was written by script.
        //   intensity         >= 0     a negative lamp SUBTRACTS light, which no dial should be able to do.
        //   specular (light)  >= 0
        //   yaw / pitch       finite   NaN already fell back to (0,0,1) via the `len > 1e-6` test, but only
        //                              by accident of NaN comparisons; made explicit.
        //   posX/posY/posZ    finite   THE SECOND REAL NaN PATH. A NaN position gives a NaN `dist`, which
        //                              fails `dist > 1e-4f`, so `inv` is finite and `ldx = NaN * inv` = NaN;
        //                              an INFINITE position gives dist = Inf, `inv` = 0, and `Inf * 0` = NaN.
        //                              Both reach `lr += li.r * dw` unguarded.
        //   range             >= 0     see MinRange below — this one INVERTED as well as being unguarded.
        //   intensityScale    >= 0
        //   rimStrength       >= 0
        //   rimPower          >= 0.01  the finding above, plus its sibling: rimPower == 0 gives
        //                              Mathf.Pow(0, 0) == 1, so rim was `rimStrength` on a FLAT normal rather
        //                              than zero (measured S = 1.1 against a baseline 0.9) — which
        //                              contradicts LR-2.5's own stated consequence that "on a flat surface
        //                              N.V = 1, so rim = 0 identically". A positive floor makes the
        //                              contract's sentence true: pow(0, 0.01) is exactly 0.
        //   specular          >= 0
        //   specularPower     >= 0.01  specularPower = -4 at a grazing normal returned S = 8.999998E+11:
        //                              finite, so it encoded to a clamped 255, but an unbounded blow-out
        //                              from a dial with no stated legal range.
        //   specularTint      >= 0

        /// <summary>The largest a strength-like dial may compile to. Finite, so Infinity can never reach the law.</summary>
        const float MaxStrength = 1e4f;
        /// <summary>The largest an exponent may compile to. <c>Mathf.Pow(nh &lt; 1, 512)</c> is already 0.</summary>
        const float MaxPower = 512f;
        /// <summary>The smallest an exponent may compile to. See the rimPower note above.</summary>
        const float MinPower = 0.01f;
        /// <summary>The largest a canvas coordinate may compile to. A canvas is 32-256 samples (B9); 1e6 is far outside.</summary>
        const float MaxCoord = 1e6f;

        /// <summary>
        /// LR-2.4. The smallest range that still divides. Below it a light's reach has VANISHED, and
        /// <c>invRangeSq</c> saturates at <c>1 / MinRange²</c> rather than collapsing to 0 — see the note at
        /// the point-light branch for why the previous guard inverted the dial.
        /// </summary>
        const float MinRange = 1e-3f;

        /// <summary>
        /// Refuse a non-finite dial and clamp a finite one into its legal range. THE one gate every dial goes
        /// through (see the block comment above).
        /// </summary>
        static float Dial(float v, float lo, float hi, float fallback)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) v = fallback;
            return v < lo ? lo : (v > hi ? hi : v);
        }

        /// <summary>
        /// Compile the document's rig. <paramref name="phase01"/> is the DOCUMENT's phase (LR-1.8).
        ///
        /// LR-1.4 — a ninth light is REFUSED with a diagnostic; it is never silently dropped and never
        /// silently rendered. The count is recorded from the start, not just the fact.
        /// </summary>
        public static ShaperLightProgram Compile(ShaperLightRig rig, float phase01, uint seed)
        {
            var prog = new ShaperLightProgram();
            if (rig == null) return prog;

            float p = Mathf.Clamp01(phase01);

            // LR-1.3: ONE ambient, on the rig, folded to linear colour times intensity at compile. The law
            // never decodes (LR-2.6) and never sees the authored Color.
            ShaperSrgb.Decode(rig.ambientColour, out float ar, out float ag, out float ab);
            ar = Dial(ar, 0f, MaxStrength, 1f);
            ag = Dial(ag, 0f, MaxStrength, 1f);
            ab = Dial(ab, 0f, MaxStrength, 1f);
            float ai = Dial(ShaperValue.Sample(rig.ambientIntensity, p, seed + 1u, 0.18f),
                            0f, MaxStrength, 0.18f);
            prog.rig.ambR = ar * ai;
            prog.rig.ambG = ag * ai;
            prog.rig.ambB = ab * ai;

            var list = rig.lights;
            int authored = list != null ? list.Count : 0;
            int n = 0, enabled = 0;

            // The loop walks the WHOLE list rather than stopping at the cap, because the cap diagnostic is
            // measured against the ENABLED count and a loop that stops at eight cannot know how many enabled
            // entries came after the eighth. Compiling still stops at eight — `n >= MaxLights` skips the work
            // and only the counting continues.
            for (int i = 0; i < authored; i++)
            {
                ShaperLight l = list[i];
                if (l == null || !l.enabled) continue;
                enabled++;
                if (n >= ShaperLightRigCompiled.MaxLights) continue;

                var c = new ShaperLightCompiled();
                c.kind = (int)l.kind;

                // Decoded ONCE, here, and folded with the intensity. FC-2.2's rule applied to a light: the
                // alpha of the authored Color is NOT read.
                ShaperSrgb.Decode(l.colour, out float lr, out float lg, out float lb);
                lr = Dial(lr, 0f, MaxStrength, 1f);
                lg = Dial(lg, 0f, MaxStrength, 1f);
                lb = Dial(lb, 0f, MaxStrength, 1f);
                float inten = Dial(ShaperValue.Sample(l.intensity, p, seed + 16u + (uint)i, 1f),
                                   0f, MaxStrength, 1f);
                c.r = lr * inten; c.g = lg * inten; c.b = lb * inten;

                c.specular = Dial(ShaperValue.Sample(l.specular, p, seed + 48u + (uint)i, 1f),
                                  0f, MaxStrength, 1f);

                if (l.kind == ShaperLightKind.Directional)
                {
                    // The UNIT vector TOWARD the light, precomputed. Yaw is measured about +Y from +Z toward
                    // +X and pitch is elevation above the canvas plane, so the default (-55, 36) is
                    // up-and-to-the-left-and-toward-the-viewer — the same quadrant Pyre's solids default to
                    // (Pyre.cs:352) and the same elevation band PyreField.ReliefLight's lz = 0.72 sits in
                    // (LR-0.4).
                    float yaw = Dial(ShaperValue.Sample(l.yaw, p, seed + 80u + (uint)i, -55f),
                                     -MaxCoord, MaxCoord, -55f) * Mathf.Deg2Rad;
                    float pit = Dial(ShaperValue.Sample(l.pitch, p, seed + 112u + (uint)i, 36f),
                                     -MaxCoord, MaxCoord, 36f) * Mathf.Deg2Rad;
                    float ch = Mathf.Cos(pit);
                    float dx = ch * Mathf.Sin(yaw), dy = Mathf.Sin(pit), dz = ch * Mathf.Cos(yaw);
                    float len = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (len > 1e-6f) { dx /= len; dy /= len; dz /= len; }
                    else { dx = 0f; dy = 0f; dz = 1f; }
                    c.dirX = dx; c.dirY = dy; c.dirZ = dz;

                    // LR-2.4: a directional light has NO falloff. invRangeSq is exactly zero and the branch
                    // is on `kind`. Stated because "a directional light with a range" is a thing people add
                    // by accident.
                    c.invRangeSq = 0f;
                }
                else
                {
                    c.posX = Dial(ShaperValue.Sample(l.posX, p, seed + 144u + (uint)i), -MaxCoord, MaxCoord, 0f);
                    c.posY = Dial(ShaperValue.Sample(l.posY, p, seed + 176u + (uint)i), -MaxCoord, MaxCoord, 0f);
                    c.posZ = Dial(ShaperValue.Sample(l.posZ, p, seed + 208u + (uint)i, 40f), -MaxCoord, MaxCoord, 40f);

                    // LR-2.4 — `atten = 1 / (1 + dist² / range²)`. As `range` shrinks toward zero the light's
                    // REACH vanishes: `atten -> 0` at every distance above zero, so `invRangeSq` must go UP,
                    // not down. **`range == 0` means this light reaches nothing** — a receiver at any
                    // non-zero distance falls back to ambient, which is the honest reading of a zero-range
                    // lamp and the continuous limit of the dial's own travel.
                    //
                    // The previous guard was `range > 1e-6f ? 1f / (range * range) : 0f`, and `invRangeSq ==
                    // 0` is the value that means NO FALLOFF — the exact opposite — so the dial INVERTED at
                    // the bottom of its travel. Measured at canvas (0,0) with the light at (0,0,40):
                    //
                    //   range   40      1         1e-2  1e-4  1e-5  1e-6      1e-7      0
                    //   L       0.5000  0.000625  0     0     0     1.000000  1.000000  1.000000
                    //
                    // The darkest reachable setting was 1e-5; at 1e-6 and below the light SNAPPED to full,
                    // unattenuated, infinite reach. Saturating at `1 / MinRange²` instead is exactly
                    // continuous with the branch above it (at `range == MinRange` both give the same number)
                    // and keeps `dist² · invRangeSq` finite for every coordinate `MaxCoord` allows.
                    float range = Dial(ShaperValue.Sample(l.range, p, seed + 240u + (uint)i, 40f),
                                       0f, MaxCoord, 40f);
                    float reach = range > MinRange ? range : MinRange;
                    c.invRangeSq = 1f / (reach * reach);
                }

                prog.rig.Set(n, c);
                n++;
            }
            prog.rig.count = n;
            prog.enabledLightCount = enabled;
            prog.authoredLightCount = authored;

            // LR-1.4's refusal, measured against the ENABLED count. `enabled` and not `authored`, because a
            // disabled entry is not a light that was dropped — it is a light the author switched off — and
            // saying otherwise is a diagnostic that lies. See ShaperLightProgram.hasTooManyLights for the
            // five configurations that were measured and the three it was wrong in.
            if (enabled > ShaperLightRigCompiled.MaxLights)
            {
                prog.hasTooManyLights = true;
                prog.tooManyLightsCount = enabled - ShaperLightRigCompiled.MaxLights;
                prog.tooManyLightsReason =
                    "the rig holds " + enabled + " enabled lights" +
                    (authored != enabled ? " (" + authored + " authored)" : "") +
                    "; only the first " + ShaperLightRigCompiled.MaxLights +
                    " enabled are lit. The cap is " + ShaperLightRigCompiled.MaxLights + ".";
            }
            return prog;
        }

        /// <summary>
        /// Compile one layer's response block, and record the shadow diagnostic on
        /// <paramref name="prog"/> if it asked for one (LR-4.5).
        /// </summary>
        public static ShaperResponseCompiled CompileResponse(ShaperLightResponse resp, string layerName,
                                                             float phase01, uint seed,
                                                             ShaperLightProgram prog = null)
        {
            var c = new ShaperResponseCompiled();
            if (resp == null) { c.receive = 0; return c; }

            float p = Mathf.Clamp01(phase01);
            c.receive = resp.receiveLighting ? 1 : 0;

            // T-0200 — THE gate, in this one place rather than at every call site (BindLayer covers both
            // Silhouette and Solids through the same CompileResponse call, so there is only one site to begin
            // with). When the rig compiled to zero ENABLED lights, `prog.rig.count` is already 0 here — Compile
            // always runs before CompileResponse in the real render path (CompileDocument, then BindLayer per
            // layer) — and every layer is forced to LR-4.3's existing `receive == 0` branch regardless of its
            // OWN receiveLighting flag. That branch already does exactly what an empty rig should: L = (1,1,1),
            // S = (0,0,0), the albedo written through unchanged. Before this gate a fresh document still ran
            // every layer through `ambR/G/B` (default 0.18) with receive == 1, so a white fill encoded ~46%
            // grey and a red ramp encoded dark red before an author had authored a single light — ambient acting
            // as a global darkener rather than "a floor under real lights". `prog` is null only from the
            // Audits' standalone probes (ShaperHeightAudit.cs:2529,2664) that construct a response with no rig
            // to speak of; those keep testing `resp.receiveLighting` verbatim, which is correct for what they
            // measure.
            if (prog != null && prog.rig.count == 0) c.receive = 0;

            // Every one of these goes through Dial (see the block comment at the top of this class). The
            // measured failure this closes is a negative rimPower writing 19 200 Infinity or NaN floats into
            // `dst` plus 6 400 black garbage pixels; the sweep covers every sibling dial rather than only the
            // one somebody happened to look at.
            c.intensityScale = Dial(ShaperValue.Sample(resp.intensityScale, p, seed + 301u, 1f),
                                    0f, MaxStrength, 1f);
            c.rimStrength = Dial(ShaperValue.Sample(resp.rimStrength, p, seed + 302u),
                                 0f, MaxStrength, 0f);
            c.rimPower = Dial(ShaperValue.Sample(resp.rimPower, p, seed + 303u, 2.2f),
                              MinPower, MaxPower, 2.2f);
            c.specular = Dial(ShaperValue.Sample(resp.specular, p, seed + 304u, 0.9f),
                              0f, MaxStrength, 0.9f);
            c.specularPower = Dial(ShaperValue.Sample(resp.specularPower, p, seed + 305u, 48f),
                                   MinPower, MaxPower, 48f);
            ShaperSrgb.Decode(resp.specularTint, out c.specTintR, out c.specTintG, out c.specTintB);
            c.specTintR = Dial(c.specTintR, 0f, MaxStrength, 1f);
            c.specTintG = Dial(c.specTintG, 0f, MaxStrength, 1f);
            c.specTintB = Dial(c.specTintB, 0f, MaxStrength, 1f);

            if (prog != null)
            {
                prog.layerCount++;
                if (resp.receiveLighting) prog.receiverCount++;

                // LR-7.3 — rim is ambient-tinted by LR-2.3's own arithmetic (`S += amb · rim`), so on a
                // BLACK ambient the Rim Strength control is wholly dead: S = 0 exactly, at every rim
                // strength. The arithmetic is the contract's ruling and is NOT changed here; what is changed
                // is that the inertness is declared. See ShaperLightProgram.hasInertRim.
                bool blackAmbient = prog.rig.ambR <= 0f && prog.rig.ambG <= 0f && prog.rig.ambB <= 0f;
                if (c.receive != 0 && c.rimStrength > 0f && blackAmbient)
                {
                    if (!prog.hasInertRim)
                    {
                        prog.hasInertRim = true;
                        prog.inertRimNode = layerName;
                        prog.inertRimReason = ShaperLightRig.RimNeedsBlackAmbientRelief;
                    }
                    prog.inertRimCount++;
                }

                // LR-4.5 — recorded AND inert. The flags never reach ShaperResponseCompiled, so the law
                // cannot see them and cannot later be blamed for them; the ONLY thing setting one does is
                // raise this, carrying ShaperLightRig.ShadowsNotComputed VERBATIM (LR-7.1). LT-12 asserts
                // BOTH halves — that nothing happens, and that the tool says so — which is what makes
                // "nothing-but-recorded" an honest ruling rather than an excuse.
                if (resp.castShadows || resp.receiveShadows)
                {
                    if (!prog.hasUnimplementedShadow)
                    {
                        prog.hasUnimplementedShadow = true;
                        prog.unimplementedShadowNode = layerName;
                        prog.unimplementedShadowReason = ShaperLightRig.ShadowsNotComputed;
                    }
                    prog.unimplementedShadowCount++;
                }
            }
            return c;
        }

        /// <summary>
        /// <b>Compile a DOCUMENT's rig, on the DOCUMENT's clock (LR-1.1, LR-1.8).</b> Added by the fix pass.
        ///
        /// <c>ShaperDocument</c> and <c>ShaperLayer</c> are this task's headline deliverable — "the document
        /// owns the lights" — and an independent verification found they had <b>zero references outside their
        /// own declaration file</b>: never constructed, never read, never exercised by the runtime or by any
        /// audit leg, so <c>canvasWidth</c>, <c>canvasHeight</c>, <c>pixelSize</c>, <c>layers</c>,
        /// <c>phase01</c>, <c>seed</c> and <c>Grid()</c> had never been run once. This method and
        /// <see cref="BindLayer"/> are the document-to-compile path a renderer would actually take, so the
        /// deliverable is wired rather than declared. LT-21 drives it.
        ///
        /// <b>What this is NOT.</b> It is not a compositor. Wave 2 stacks no layers into one picture and this
        /// contract does not ask it to; the document is the source of the rig, the grid, the clock, the seed
        /// and each layer's response, and the host still drives the paint pass per layer. Saying so is the
        /// point — a helper that pretended to be a renderer would be worse than none.
        /// </summary>
        public static ShaperLightProgram CompileDocument(ShaperDocument document)
        {
            if (document == null) return new ShaperLightProgram();
            // LR-1.8's document clock, used here and only here: a light is not attached to a node and has no
            // window to ignore, so there is no second answer to give it.
            return Compile(document.lightRig, document.phase01, document.seed);
        }

        /// <summary>
        /// <b>Build the paint pass's lighting binding for ONE layer of a document</b> (LR-4.1, LR-3.1). Added
        /// by the fix pass; see <see cref="CompileDocument"/> for why.
        ///
        /// The layer's own response block and its own normal provider are compiled here and applied to every
        /// owner in that layer, which is what <c>SetAll</c> means and what LR-4.1's "every LAYER carries
        /// exactly one" requires. The shared rig on <paramref name="prog"/> is copied in unchanged: LR-1.1
        /// says no stage may add a light at render time, and this one cannot — it has no path to the authored
        /// rig at all.
        ///
        /// <paramref name="sampleCapacity"/> and <paramref name="ownerCapacity"/> MUST be the paired
        /// <c>ShaperFillBuffers</c>' own, because the paint pass indexes both with the same
        /// <c>ownerIndex * sampleCapacity</c> stride.
        /// </summary>
        /// <param name="layerProgram">
        /// T-0109 FIX F4 — the compiled shape program the layer's HEIGHT stage is measured against: it
        /// supplies HS-1.2's <c>span</c> and the node-local frame <c>Linear</c> reads (HS-2.3). Optional, and
        /// <c>null</c> is a real answer rather than an error — a caller that has no program yet gets a stage
        /// whose <c>span</c> falls back to <paramref name="pixelSize"/>, which is what
        /// <see cref="ShaperHeightCompiler.Compile"/> already does.
        /// </param>
        /// <param name="pixelSize">The grid's sample spacing, <c>span</c>'s floor (HS-1.2).</param>
        public static ShaperLightScene BindLayer(ShaperDocument document, int layerIndex,
                                                 ShaperLightProgram prog,
                                                 int sampleCapacity, int ownerCapacity,
                                                 ShaperProgram layerProgram = null, float pixelSize = 1f)
        {
            if (document == null || prog == null) return null;
            if (layerIndex < 0 || layerIndex >= document.layers.Count) return null;

            ShaperLayer layer = document.layers[layerIndex];
            var scene = new ShaperLightScene(sampleCapacity, ownerCapacity) { rig = prog.rig };
            if (layer == null) return scene;

            // T-0109 FIX F4a — the layer's height stage, compiled HERE because this is where HS-7.2's base
            // is already computable and where the per-owner sheets it feeds are bound. A null `layer.height`
            // compiles to `present = false`, and every consumer below treats that as "no height stage", not
            // as "a zero one".
            float baseZ = ShaperHeightCompiler.LayerBase(document, layerIndex, document.phase01, document.seed);
            var hop = ShaperHeightCompiler.Compile(layer.height, layerProgram, pixelSize, baseZ,
                                                   document.phase01, document.seed);

            var resp = CompileResponse(layer.response, layer.name, document.phase01, document.seed, prog);
            var nop = CompileNormal(layer.response, hop);
            scene.SetAll(resp, nop);
            scene.SetAllHeight(hop);
            return scene;
        }

        /// <summary>Compile one layer's normal provider (LR-3.1), normalising the constant at compile.</summary>
        public static ShaperNormalOp CompileNormal(ShaperLightResponse resp)
            => CompileNormal(resp, default(ShaperHeightOp));

        /// <summary>
        /// Compile one layer's normal provider (LR-3.1), normalising the constant at compile, and give it the
        /// compiled height stage that <see cref="ShaperNormalKind.Profile"/> declares as its input.
        ///
        /// <b>T-0109 FIX F4b.</b> The one-argument overload above never populated <c>op.height</c>, so
        /// selecting <c>Profile</c> — which IS authorable, because this method copies
        /// <c>resp.normalKind</c> verbatim — was indistinguishable from selecting <c>Constant(0,0,1)</c>: the
        /// provider took LR-3.5's fallback on every sample, silently, and still wrote the pixel.
        /// </summary>
        public static ShaperNormalOp CompileNormal(ShaperLightResponse resp, in ShaperHeightOp height)
        {
            var op = ShaperNormalOp.Default;
            op.height = height;
            if (resp == null) return op;

            op.kind = resp.normalKind;
            Vector3 v = resp.normalConstant;
            float len = Mathf.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
            if (len > 1e-6f) { op.cx = v.x / len; op.cy = v.y / len; op.cz = v.z / len; }
            else { op.cx = 0f; op.cy = 0f; op.cz = 1f; }   // LR-3.5: never zero, never NaN
            return op;
        }

        /// <summary>
        /// Close the receiver diagnostic. Called once, after every layer's response has been compiled.
        /// </summary>
        public static void Finish(ShaperLightProgram prog)
        {
            if (prog == null) return;
            bool anyLight = prog.rig.count > 0;
            if (anyLight && prog.receiverCount == 0 && prog.layerCount > 0)
            {
                prog.hasLightsButNoReceivers = true;
                prog.lightsButNoReceiversReason =
                    "the document holds " + prog.rig.count + " enabled light" +
                    (prog.rig.count == 1 ? "" : "s") + " and no layer receives lighting, so none of them " +
                    "changes a pixel. Turn on Receive Lighting on a layer.";
            }
        }
    }

    /// <summary>
    /// The host-owned lighting binding for one paint pass: the compiled rig, one response block and one
    /// surface-direction provider per owner, and the sheets those providers write.
    ///
    /// HOST-ALLOCATED AND HOST-OWNED, on exactly the terms BC-3.7f sets and
    /// <see cref="ShaperFillBuffers"/> already follows: <c>ShaperFillResolver.PaintTile</c> never allocates,
    /// replaces, resizes or frees one of these inside a paint call. That is what LT-2 measures, and the
    /// violation it names is the one <c>PyreRenderer.cs:1803</c> and <c>:1857</c> commit today —
    /// <c>light = new float[W * H]</c>, per layer per frame.
    ///
    /// <b>Why the providers live here and not on the owner.</b> A normal provider is a lighting-stage input:
    /// LR-3.1 says "the provider interface IS that sheet plus a declaration", and the sheet is per owner
    /// because a border's strip and its host's face may one day need different ones. Keeping the binding in
    /// one host-owned object means the shape tree, the fill and the border stages are untouched by this task
    /// and a document with no rig is bit-for-bit what it was.
    ///
    /// <b>Passing <c>null</c> for the scene is the pre-T-0108 build, exactly.</b>
    /// </summary>
    public sealed class ShaperLightScene
    {
        /// <summary>
        /// MUST equal the paired <see cref="ShaperFillBuffers.sampleCapacity"/>: the paint pass indexes both
        /// this object's sheets and the fill buffers' per-owner slabs with the SAME
        /// <c>ownerIndex * sampleCapacity</c> stride, which is the scheme <c>ownCoverage</c> already uses and
        /// the one FC-4.5 tells a future sheet producer to follow.
        /// </summary>
        public int sampleCapacity;
        public int ownerCapacity;

        /// <summary>The compiled document rig (LR-1.1) — one, shared by every owner.</summary>
        public ShaperLightRigCompiled rig;

        /// <summary>Per owner. A border's entry is never read: it is lit by its HOST's block (LR-5.4).</summary>
        public ShaperResponseCompiled[] response;

        /// <summary>Per owner. A border's entry is never read: it uses its HOST's normal (LR-5.4).</summary>
        public ShaperNormalOp[] normalOp;

        /// <summary>
        /// T-0109 FIX F4 — per owner: the LAYER's compiled height stage (HS-1.1). One layer has one stage, so
        /// every entry is the same value; it is per owner because <c>PaintTile</c> indexes everything else
        /// that way and because a future per-node stage is then a fill rather than a re-shape.
        ///
        /// It is kept in lockstep with <c>normalOp[i].height</c> by <see cref="SetAllHeight"/> — the
        /// <c>Profile</c> normal provider reads the copy on the normal op, and <c>PaintTile</c>'s height sheet
        /// reads this one, and the two disagreeing would be exactly the class of bug F4 was.
        /// </summary>
        public ShaperHeightOp[] heightOp;

        /// <summary>
        /// <b>T-0109 FIX F4b — the diagnostic that makes the degenerate normal LOUD.</b> Per owner: how many
        /// samples in the last painted tile declared <see cref="ShaperNormalKind.Profile"/> and were answered
        /// with LR-3.5's <c>(0,0,1)</c> fallback because the inputs that kind declares were not supplied.
        ///
        /// Non-zero means an authored <c>Profile</c> provider is silently rendering as
        /// <c>Constant(0,0,1)</c> — which before this fix was the ONLY thing it ever did, since
        /// <c>CompileNormal</c> never populated <c>op.height</c> and <c>PaintTile</c> omitted the
        /// <c>field</c>/<c>stack</c> arguments. That is the design doc's own second named failure mode: not a
        /// rule invoked from nowhere, but a rule invoked and quietly answered with a fallback, which is harder
        /// to notice because the pixel still gets written.
        /// </summary>
        public int[] normalDegenerate;

        /// <summary>Per owner: the compiled Solids generator, when this owner is one.</summary>
        public ShaperSolidOp[] solidOp;

        /// <summary>
        /// Per owner: the prebuilt Solids geometry, or null when this owner is an ordinary shape node.
        /// When it is non-null, <c>PaintTile</c>'s step 1 takes this owner's coverage and edge distance from
        /// <see cref="ShaperSolids.FillTile"/> instead of <c>ShaperEvaluator.FillTile</c> — the generator
        /// REPLACES the shape stage for that owner, and everything downstream (the claim, the exclusivity
        /// partition, the fill, the border, the composite) is identical to any other owner's. That is LR-6.1's
        /// "goes through the ordinary fill and light pipeline like every other generator", implemented rather
        /// than asserted.
        /// </summary>
        public ShaperSolidGeometry[] solid;

        /// <summary>Per owner, per sample: 3 floats, UNIT, canvas frame (LR-3.5).</summary>
        public float[] normal;
        /// <summary>Per owner, per sample: the facet-edge-line mask (LR-6.3). Solids only.</summary>
        public float[] lineMask;
        /// <summary>Per owner, per sample: 3 floats of additive, unlit glow (LR-6.4). Solids only.</summary>
        public float[] glow;
        /// <summary>Per owner, per sample: the surface point's Z, canvas pixels (LR-1.5).</summary>
        public float[] pointZ;

        /// <summary>
        /// The unit direction toward the viewer. v1 has exactly one: <c>(0,0,1)</c>. It is a field rather
        /// than a constant so the law's signature never has to change when a camera arrives — but LR-6.5d
        /// records that a camera is a rewrite of the barycentric interpolation, not a matrix change.
        /// </summary>
        public float vx = 0f, vy = 0f, vz = 1f;

        public ShaperLightScene(int sampleCapacity, int ownerCapacity)
        {
            this.sampleCapacity = Mathf.Max(1, sampleCapacity);
            this.ownerCapacity = Mathf.Max(1, ownerCapacity);
            int n = this.sampleCapacity, k = this.ownerCapacity;

            response = new ShaperResponseCompiled[k];
            normalOp = new ShaperNormalOp[k];
            heightOp = new ShaperHeightOp[k];
            normalDegenerate = new int[k];
            solidOp = new ShaperSolidOp[k];
            solid = new ShaperSolidGeometry[k];

            normal = new float[n * k * 3];
            lineMask = new float[n * k];
            glow = new float[n * k * 3];
            pointZ = new float[n * k];

            for (int i = 0; i < k; i++)
            {
                response[i] = ShaperResponseCompiled.Unlit;
                normalOp[i] = ShaperNormalOp.Default;
            }
        }

        /// <summary>Set every owner's response and provider from one layer's block. The ordinary case.</summary>
        public void SetAll(in ShaperResponseCompiled resp, in ShaperNormalOp nop)
        {
            for (int i = 0; i < ownerCapacity; i++) { response[i] = resp; normalOp[i] = nop; }
        }

        /// <summary>
        /// T-0109 FIX F4 — set every owner's LAYER height stage, and the copy the <c>Profile</c> normal
        /// provider reads, from one value. Writing both here is what keeps them from drifting apart.
        /// </summary>
        public void SetAllHeight(in ShaperHeightOp hop)
        {
            for (int i = 0; i < ownerCapacity; i++)
            {
                heightOp[i] = hop;
                var n = normalOp[i];
                n.height = hop;
                normalOp[i] = n;
            }
        }

        /// <summary>Bind a compiled Solids generator to one owner, prebuilding its geometry.</summary>
        public void SetSolid(int owner, in ShaperSolidOp op)
        {
            if (owner < 0 || owner >= ownerCapacity) return;
            solidOp[owner] = op;
            solid[owner] = ShaperSolids.Build(op);
        }
    }
}
