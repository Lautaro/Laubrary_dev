using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// Which surface-direction provider a layer uses. LR-3.1.
    ///
    /// APPEND-ONLY, and the discipline is the point: the same append-only rule <c>ShaperQuantity</c> and
    /// <c>ShaperFillKind</c> already carry (<c>ShaperFillContract.cs:9-13</c>, <c>:206</c>). Never renumber.
    /// </summary>
    public enum ShaperNormalKind
    {
        /// <summary>An authored constant unit direction. LR-3.2. Wave 2's only Silhouette provider.</summary>
        Constant = 0,

        /// <summary>
        /// The extrusion/bevel height profile's analytic surface normal. T-0109, HS-8.1.
        ///
        /// Reserved and named by LR-3.1 before it existed so its arrival would be an ADDITION and not a
        /// renumbering; it arrived exactly that way. It is a case in <see cref="ShaperNormals.FillTile"/> —
        /// not a second entry point — and it touches neither <c>ShaperLightLaw</c> nor one line of
        /// <c>ShaperSolids</c>. LT-13 is the test that makes that claim mechanical rather than asserted.
        ///
        /// Both requirements handed forward with the reservation are met. The profile publishes
        /// <c>dh/dd</c> ANALYTICALLY (<see cref="ShaperHeight.ComposedDerivative"/>, each technique's own
        /// closed form), so the provider is never back to differencing, which LR-3.3 forbids. And
        /// <c>∇d</c> comes from a central difference of <see cref="ShaperEvaluator.Distance"/>, a PURE
        /// FUNCTION OF A CANVAS POINT — LR-3.2's own licence, the same one the light rig already takes. That
        /// is categorically not a read of a buffer's screen-space neighbours, so LR-3.3's prohibition is
        /// satisfied STRUCTURALLY rather than by promise: this case never indexes
        /// <c>heightSheet</c> at all.
        /// </summary>
        Profile = 1,
    }

    /// <summary>
    /// A compiled normal provider. LR-3.1.
    ///
    /// Flat, blittable, one struct and a switch — FC-5.2's form, for the same reason: a provider does not
    /// nest, so its "program" is a single op struct and the flat form is SIMPLER than a class hierarchy
    /// rather than a concession to performance.
    /// </summary>
    public struct ShaperNormalOp
    {
        public ShaperNormalKind kind;

        /// <summary>Constant: the authored UNIT direction, normalised at compile (LR-3.5).</summary>
        public float cx, cy, cz;

        // ── BC-3.6's three dials, named NOW with their provenance, unused until T-0109 ────────────────────
        //
        // BC-3.6 is explicit: "All of these become named, defaulted dials with their provenance in a comment.
        // None of them is a magic number in the new code" (BUFFER_CONTRACT.md:248). Naming them here
        // discharges that instruction in Wave 2 and gives T-0109 a calibration target rather than a memory.
        //
        // Only the RATIO slopeGain/normalZBase == 0.464... sets tilt sensitivity (a unit height difference
        // tilts the normal by atan(0.65/1.4) == 24.9 degrees), but they are NOT interchangeable in the
        // reference, because normalZ is also read on its own by its Fresnel term (BUFFER_CONTRACT.md:246).
        // Under LR-2.5 that asymmetry does not reproduce here, because this law's N is contractually unit;
        // they are kept as two dials anyway so a ported look has both of the reference's handles.

        /// <summary>0.65 — <c>index.html:1491</c>. Scales <c>dh/dd</c> into the tangent components. T-0109.</summary>
        public float slopeGain;

        /// <summary>1.4 — <c>index.html:1492</c>. The out-of-screen component tilt is measured against. T-0109.</summary>
        public float normalZBase;

        /// <summary>1.5 — <c>index.html:1492</c>, the undocumented third dial BC correction 3 found. T-0109.</summary>
        public float reflectionFlatten;

        /// <summary>
        /// T-0109, HS-8.3 — the layer's per-material reflection, 0..1, which is the ONE input
        /// <see cref="reflectionFlatten"/> rides on: the reference's Z term is
        /// <c>normalZBase + 1.5·clamp01(reflection)</c> (<c>index.html:1492</c>), so a fully reflective
        /// material sits at 2.9 rather than 1.4 and reads flatter.
        ///
        /// Wave 2 has no authored reflection dial, so this defaults to 0 and <c>reflectionFlatten</c> is
        /// multiplied by it — but it is GENUINELY READ, on every sample, by the <see cref="ShaperNormalKind.Profile"/>
        /// case, and audit check H10 fails if it ever stops being. That is what stops the dial rotting into a
        /// dead field, which is the failure BC-3.6 named it to prevent.
        /// </summary>
        public float reflection;

        /// <summary>
        /// T-0109 — the compiled height stage this provider differentiates. Read only by
        /// <see cref="ShaperNormalKind.Profile"/>; <see cref="ShaperNormalKind.Constant"/> ignores it.
        ///
        /// <see cref="ShaperHeightOp"/> holds no managed reference of any kind, so carrying it here keeps
        /// <see cref="ShaperNormalOp"/> flat and blittable — the FC-5.2 form this struct was built in.
        /// </summary>
        public ShaperHeightOp height;

        /// <summary>The defaults BC-3.6 names, plus the degenerate <c>(0,0,1)</c> direction of LR-3.5.</summary>
        public static ShaperNormalOp Default => new ShaperNormalOp
        {
            kind = ShaperNormalKind.Constant,
            cx = 0f, cy = 0f, cz = 1f,
            slopeGain = 0.65f,
            normalZBase = 1.4f,
            reflectionFlatten = 1.5f,
            reflection = 0f,
        };
    }

    /// <summary>
    /// The normal provider stage (LR-3.1): a named, declared, swappable stage that writes a <c>float[3n]</c>
    /// sheet of UNIT vectors in the canvas frame, one per sample, before the law runs. The provider interface
    /// IS that sheet plus a declaration. Anything that can write the sheet is a provider; the law reads the
    /// sheet and knows nothing about who wrote it.
    ///
    /// <b>Wave 2 ships two implementations, and they are genuinely different machinery — which is what makes
    /// the interface real rather than a single-implementation fiction.</b> This one, kind <c>Constant</c>, is
    /// Silhouette's and consumes no sheet. The other is <see cref="ShaperSolids"/>, which writes the same
    /// array directly from its own geometry — the facet normal at <c>PyreRenderer.cs:4236</c>, the sphere's
    /// <c>N = P/R</c> at <c>:4533</c>, the ring plane's constant <c>N</c> at <c>:4639</c>. It is not a case in
    /// this enum and does not go through this type; it is a second writer of the same array.
    ///
    /// <b>LR-3.3 — screen-space differencing is FORBIDDEN in Wave 2.</b> No provider may read the screen-space
    /// neighbours of any intermediate buffer. Not on purity grounds, on two measured ones: the only
    /// differenceable buffer is <c>ShaperFillBuffers.height</c> (<c>ShaperFillResolver.cs:227</c>), every value
    /// in which comes from <c>buf.height[i] += buf.heightDelta[i] * ce</c> (<c>:1046</c>) and is therefore
    /// entirely FILL-authored, so differencing it would describe the paint's bumps and not the surface's; and
    /// <c>PaintTile</c> has no apron and no mechanism to supply one, so a differencing provider would need
    /// every tile grown by 2 in each dimension and re-rendered — for BC-1.6's prescribed 7x5 decomposition,
    /// <c>(9*7)/(7*5) = 1.80x</c> the work on the entire fill stage, paid on every tile, to obtain a normal
    /// describing the wrong surface. LT-3 is the continuous check: a differencing provider seams at every tile
    /// boundary.
    ///
    /// <b>LR-3.4 — a fill's height delta does not reach the normal in Wave 2.</b> It continues to accumulate
    /// into <c>ShaperFillBuffers.height</c> and is consumed by nothing, which is exactly the state FC-2.5
    /// shipped knowingly.
    /// </summary>
    public static class ShaperNormals
    {
        /// <summary>
        /// Fill a rectangular tile of the normal sheet. Mirrors <c>ShaperFillOps.FillTile</c>
        /// (<c>ShaperFillOps.cs:48-53</c>) including <c>srcOffset</c>/<c>srcStride</c> and
        /// <c>dstOffset</c>/<c>dstStride</c>, so provider, shape and fill all tile identically and one host
        /// loop drives all three.
        ///
        /// <paramref name="distance"/> and <paramref name="heightSheet"/> MAY be null — a provider reads only
        /// what it declared, and <c>Constant</c> declares nothing. They are in the signature so that T-0109's
        /// <c>Profile</c> case is an addition to a body rather than a change to a signature.
        ///
        /// Writes 3 floats per sample at <c>3 * (dstOffset + row * dstStride + i)</c>. Every written vector is
        /// UNIT to within 1e-4, finite, and in the canvas frame of LR-1.5 (LR-3.5). It never writes NaN, never
        /// leaves a sample unwritten, and never writes zero — zero is refused because <c>N.L = 0</c> everywhere
        /// would silently render an unlit black layer with no diagnostic, which is the silent-inertness failure
        /// BC-3.1 names as "the single most-reported confusion in the existing tool".
        ///
        /// Allocates nothing (LT-2).
        /// </summary>
        /// <param name="field">
        /// T-0109 — the compiled shape program <see cref="ShaperNormalKind.Profile"/> takes <c>∇d</c> from, by
        /// a central difference of <see cref="ShaperEvaluator.Distance"/> at the sample POINT.
        ///
        /// It is an OPTIONAL TRAILING parameter, so LR-3.1's signature is extended and not changed: every
        /// existing call site — including <c>ShaperFillResolver.PaintTile</c>'s
        /// (<c>ShaperFillResolver.cs:934</c>) — compiles untouched, and this is still ONE entry point with a
        /// switch inside it rather than a second one. Null while <c>kind == Profile</c> is a declared-input
        /// failure and falls back to <c>(0,0,1)</c> per LR-3.5, never to a differencing path.
        /// </param>
        /// <param name="stack">A value stack for <paramref name="field"/>. Allocate once per thread, never per sample.</param>
        /// <returns>
        /// <b>T-0109 FIX F4b — the diagnostic.</b> The number of samples that declared
        /// <see cref="ShaperNormalKind.Profile"/> and were answered with LR-3.5's <c>(0,0,1)</c> fallback
        /// because the inputs that kind declares were absent. 0 on every other path, including a correctly
        /// supplied <c>Profile</c>. The return type changed from <c>void</c>, which every existing call site
        /// ignores compatibly; the point is that a caller which cares can now TELL, instead of a
        /// silently-flat surface being indistinguishable from an authored flat one.
        /// </returns>
        public static int FillTile(in ShaperNormalOp op, in ShaperSampleGrid grid,
                                    int x0, int y0, int width, int height,
                                    float[] distance, float[] heightSheet,
                                    float[] normal,
                                    int srcOffset, int srcStride,
                                    int dstOffset, int dstStride,
                                    ShaperProgram field = null, float[] stack = null)
        {
            if (normal == null) return 0;

            // ── HS-8.1 — the Profile case. A CASE IN THIS BODY, exactly the shape LR-3.1 reserved it in.
            //
            // It reads the compiled height op and the shape program. It does NOT read `heightSheet`, and it
            // does NOT read any screen-space neighbour of any buffer, so LR-3.3's prohibition holds
            // structurally rather than by promise.
            if (op.kind == ShaperNormalKind.Profile && op.height.present && field != null && stack != null)
            {
                FillProfile(op, grid, x0, y0, width, height, normal, dstOffset, dstStride, field, stack);
                return 0;
            }

            int degenerate = 0;
            float nx, ny, nz;
            switch (op.kind)
            {
                case ShaperNormalKind.Profile:
                    // Declared Profile but the inputs it declared were not supplied (or the stage is absent).
                    // LR-3.5: a provider that cannot produce a direction writes (0,0,1) — never leaves the
                    // sample unwritten, never writes zero, never falls back to differencing.
                    //
                    // T-0109 FIX F4b: it is also COUNTED and returned. Writing (0,0,1) here is the right
                    // pixel; doing it without telling anyone is what made an authored Profile provider
                    // indistinguishable from Constant for a whole wave.
                    nx = 0f; ny = 0f; nz = 1f;
                    degenerate = width * height;
                    break;

                case ShaperNormalKind.Constant:
                default:
                {
                    // Normalised at compile, but re-checked here for the degenerate authored case: LR-3.5's
                    // "a provider that cannot produce a direction writes (0,0,1), never leaves the sample
                    // unwritten and never writes zero". That is the same choice the shipped engine already
                    // makes for a singular transform — publish the empty field and flag, rather than divide
                    // (ShaperCompiler.cs:131-142).
                    nx = op.cx; ny = op.cy; nz = op.cz;
                    float len = Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (!(len > 1e-6f) || float.IsNaN(len)) { nx = 0f; ny = 0f; nz = 1f; }
                    else { float inv = 1f / len; nx *= inv; ny *= inv; nz *= inv; }
                    break;
                }
            }

            // Deliberately NOT a read of `distance` or `heightSheet`: Constant consumes no sheet, and a
            // provider reads only what it declared. The parameters are unused here on purpose.
            for (int j = 0; j < height; j++)
            {
                int row = dstOffset + j * dstStride;
                for (int i = 0; i < width; i++)
                {
                    int t = (row + i) * 3;
                    normal[t + 0] = nx;
                    normal[t + 1] = ny;
                    normal[t + 2] = nz;
                }
            }
            return degenerate;
        }

        /// <summary>
        /// HS-8.2/HS-8.3 — the <see cref="ShaperNormalKind.Profile"/> body. Private to
        /// <see cref="FillTile"/>: it is a CASE of that one entry point, not a second one, and nothing outside
        /// this type can reach it.
        ///
        /// <b>The surface gradient is the chain rule, never a difference of neighbouring samples:</b>
        /// <code>
        /// ∇h = body · G′(t) · ∇t = body · G′(t) · (−∇d / span)
        /// </code>
        /// with <c>∇d</c> from a central difference of <see cref="ShaperEvaluator.Distance"/> at the sample
        /// point — legal because that is a PURE FUNCTION OF A CANVAS POINT, LR-3.2's own licence and the same
        /// one the light rig already takes — and <c>G′</c> supplied ANALYTICALLY by each technique
        /// (<see cref="ShaperHeight.ComposedDerivative"/>).
        ///
        /// <b>One term HS-8.2's formula omits and this supplies, because dropping it would be visibly
        /// wrong.</b> <see cref="ShaperExtrusionTechnique.Linear"/> is not a function of <c>t</c> at all
        /// (HS-2.3) — its slope lives in the node-local coordinates and is declared separately by HS-4.4 as a
        /// 2-vector. So the general gradient is
        /// <code>
        /// ∂h/∂x = body · [ G′(t)·∂t/∂x  +  B(t)·∂E/∂x|local ]
        /// </code>
        /// where the second term is zero for the six <c>t</c>-profiles (recovering HS-8.2 exactly) and is the
        /// whole of the tilt for <c>Linear</c>. Without it a tilted slab would shade perfectly flat, because
        /// <c>E′(t) ≡ 0</c> for that technique and HS-8.2's product would be identically zero. The two figures
        /// are still never SUMMED as bounds (HS-4.4); they are summed here as gradients of the same height
        /// with respect to the same variable, which is a different operation.
        ///
        /// <b>The clamp is differentiated honestly.</b> <c>t = clamp01(−d/span)</c> has derivative zero
        /// outside <c>(0,1)</c>, so the profile term vanishes there rather than being evaluated at a
        /// singularity — which is also what stops <c>∞ · 0</c> ever being formed.
        ///
        /// <b>HS-8.4 — <c>G′</c> is NOT clamped to a magic ceiling.</b> Seven of the twelve techniques have
        /// unbounded <c>G′</c> somewhere. Where the value is genuinely infinite the code takes the LIMIT
        /// DIRECTION (the infinite components alone, with <c>nz = 0</c>) rather than normalising an infinity
        /// into a NaN — that is <c>normalize</c> doing the work, at the one place IEEE arithmetic cannot do it
        /// unaided, and it is a limit rather than a ceiling: no finite magnitude is invented. The only guarded
        /// case is the exactly-degenerate <c>∇d = 0</c> (a local extremum of the field), which falls back to
        /// <c>(0,0,1)</c> per LR-3.5.
        ///
        /// Allocates nothing.
        /// </summary>
        static void FillProfile(in ShaperNormalOp op, in ShaperSampleGrid grid,
                                int x0, int y0, int width, int height,
                                float[] normal, int dstOffset, int dstStride,
                                ShaperProgram field, float[] stack)
        {
            ShaperHeightOp h = op.height;

            // The central-difference step for ∇d. A fraction of the sample spacing, floored so a sub-pixel
            // grid cannot drive it into float cancellation: d is exact to ~1 ulp and the quotient divides by
            // 2h, so h must stay well above eps·|d|. This is a step on a PURE FUNCTION, not a neighbour read
            // — the two are different things and only the second is what LR-3.3 forbids.
            float hs = Mathf.Max(1e-3f, 0.25f * grid.pixelSize);
            float inv2h = 0.5f / hs;

            // HS-8.3 — the reference's Z term, `normalZBase + 1.5·clamp01(reflection)` (index.html:1492).
            // `reflectionFlatten` IS read, on every sample, which is what audit check H10 verifies: BC-3.6
            // named the dial precisely so it would not be a magic number, and a named dial nothing reads is
            // the same defect wearing a better hat.
            float nzBase = op.normalZBase + op.reflectionFlatten * Mathf.Clamp01(op.reflection);

            bool linear = h.technique == ShaperExtrusionTechnique.Linear;

            for (int j = 0; j < height; j++)
            {
                float y = grid.originY + (y0 + j) * grid.pixelSize;
                int row = dstOffset + j * dstStride;
                for (int i = 0; i < width; i++)
                {
                    float x = grid.originX + (x0 + i) * grid.pixelSize;
                    int o = (row + i) * 3;

                    float d = ShaperEvaluator.Distance(field, x, y, stack);

                    // Outside the silhouette the surface is the flat base plane. Same rule
                    // ShaperHeight.FillTile uses for the sheet, so the two never disagree about where the
                    // solid is.
                    if (d > 0f || ShaperField.IsEmpty(d))
                    {
                        normal[o] = 0f; normal[o + 1] = 0f; normal[o + 2] = 1f;
                        continue;
                    }

                    float tRaw = -d * h.invSpan;
                    float t = tRaw <= 0f ? 0f : (tRaw >= 1f ? 1f : tRaw);

                    float nlx = 0f, nly = 0f;
                    if (linear) ShaperHeight.LocalNormalised(h, x, y, out nlx, out nly);

                    float dhdx = 0f, dhdy = 0f;

                    // ── the profile term, through t ───────────────────────────────────────────────────────
                    if (tRaw > 0f && tRaw < 1f)
                    {
                        float dxp = ShaperEvaluator.Distance(field, x + hs, y, stack);
                        float dxm = ShaperEvaluator.Distance(field, x - hs, y, stack);
                        float dyp = ShaperEvaluator.Distance(field, x, y + hs, stack);
                        float dym = ShaperEvaluator.Distance(field, x, y - hs, stack);
                        float gdx = (dxp - dxm) * inv2h;
                        float gdy = (dyp - dym) * inv2h;

                        if (gdx != 0f || gdy != 0f)
                        {
                            float gp = ShaperHeight.ComposedDerivative(h, t, nlx, nly);
                            float k = h.body * gp * (-h.invSpan);
                            dhdx += k * gdx;
                            dhdy += k * gdy;
                        }
                        // ∇d == 0 exactly is a local extremum of the field. LR-3.5's degenerate case: leave
                        // the profile term at zero rather than dividing, and let nzBase carry the sample.
                    }

                    // ── the Linear term, through the node-local coordinates (HS-4.4) ─────────────────────
                    if (linear)
                    {
                        // ∂E/∂x = 0.6·(cosθ·∂nx/∂x − sinθ·∂ny/∂x), with nx = (m00·x + m01·y + m02 − cx)/halfW.
                        // The clamp to [−1,1] is differentiated honestly: outside it the coordinate is frozen
                        // and contributes nothing.
                        float dnxdx = Mathf.Abs(nlx) < 1f ? h.m00 * h.invLocalHalfW : 0f;
                        float dnxdy = Mathf.Abs(nlx) < 1f ? h.m01 * h.invLocalHalfW : 0f;
                        float dnydx = Mathf.Abs(nly) < 1f ? h.m10 * h.invLocalHalfH : 0f;
                        float dnydy = Mathf.Abs(nly) < 1f ? h.m11 * h.invLocalHalfH : 0f;

                        float e = ShaperHeight.Profile(h, t, nlx, nly);
                        if (e > 0f)   // the max(0,·) branch of index.html:1148 is flat where it fires
                        {
                            float b = ShaperHeight.Bevel(h, t);
                            float s = h.body * b * 0.6f;
                            // T-0109 FIX F7 — the `+` on sinθ is the flipped, +Y-UP frame. See the FRAME NOTE
                            // on ShaperHeight.Profile's Linear case: `Linear`'s angle is a reference-app
                            // angle and T-0105's precedent flips those. This sign MUST match the forward
                            // formula's or the shading leans the opposite way to the geometry.
                            dhdx += s * (h.cosAngle * dnxdx + h.sinAngle * dnydx);
                            dhdy += s * (h.cosAngle * dnxdy + h.sinAngle * dnydy);
                        }
                    }

                    // ── the normal (index.html:1491-1492) ────────────────────────────────────────────────
                    float vx, vy, vz;
                    bool ix = float.IsInfinity(dhdx), iy = float.IsInfinity(dhdy);
                    if (ix || iy)
                    {
                        // HS-8.4's unbounded case, taken as a LIMIT and not as a ceiling: the finite
                        // components and nzBase are dominated, so the direction is that of the infinite
                        // components alone, exactly vertical-walled.
                        vx = ix ? -Mathf.Sign(dhdx) : 0f;
                        vy = iy ? -Mathf.Sign(dhdy) : 0f;
                        vz = 0f;
                    }
                    else
                    {
                        vx = -op.slopeGain * dhdx;
                        vy = -op.slopeGain * dhdy;
                        vz = nzBase;
                    }

                    float len = Mathf.Sqrt(vx * vx + vy * vy + vz * vz);
                    if (!(len > 1e-12f) || float.IsNaN(len))
                    {
                        // LR-3.5, the last line of defence: never unwritten, never zero, never NaN.
                        normal[o] = 0f; normal[o + 1] = 0f; normal[o + 2] = 1f;
                    }
                    else
                    {
                        float inv = 1f / len;
                        normal[o] = vx * inv; normal[o + 1] = vy * inv; normal[o + 2] = vz * inv;
                    }
                }
            }
        }
    }
}
