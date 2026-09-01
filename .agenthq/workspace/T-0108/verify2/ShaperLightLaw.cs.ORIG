using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// One compiled light. LR-2.2.
    ///
    /// Fully blittable, fixed size, taken by <c>in</c> as part of <see cref="ShaperLightRigCompiled"/>. No
    /// managed reference arrives through the parameter (BC-1.2), and the eight-light cap (LR-1.4) is what
    /// lets the rig be a fixed-size struct rather than a bulk-data indirection (which FC-5.5 reserves for
    /// things that are not floats).
    /// </summary>
    public struct ShaperLightCompiled
    {
        /// <summary>0 = Directional, 1 = Point. An <c>int</c>, not an enum, for the same reason
        /// <c>ShaperFillOp</c> stores its booleans as ints (<c>ShaperFillCompiler.cs:88</c>): a predictable
        /// blittable width rather than the runtime's choice of enum backing alignment.</summary>
        public int kind;

        /// <summary>Directional: UNIT vector TOWARD the light, in the canvas frame. Precomputed at compile.</summary>
        public float dirX, dirY, dirZ;

        /// <summary>Point: absolute canvas position, canvas pixels (LR-1.5).</summary>
        public float posX, posY, posZ;

        /// <summary>LINEAR colour times intensity, folded at compile. The one place sRGB is decoded for a light.</summary>
        public float r, g, b;

        /// <summary>
        /// <c>1 / range^2</c>, precomputed. Exactly ZERO for a directional light, so a directional light has
        /// no falloff at all and the branch is on <see cref="kind"/> alone. Stated because "a directional
        /// light with a range" is a thing people add by accident (LR-2.4).
        /// </summary>
        public float invRangeSq;

        /// <summary>This light's Blinn-Phong strength.</summary>
        public float specular;
    }

    /// <summary>
    /// The compiled rig. LR-2.2.
    ///
    /// Eight named slots rather than an array, because a managed array reference inside a parameter block is
    /// precisely what BC-1.2's fourth bullet forbids, and because LT-13b asserts by reflection that
    /// <see cref="ShaperLightLaw.Shade"/>'s parameter list contains no array type. A signature with no array
    /// in it cannot difference a buffer, and that is checkable by reflection in four lines — the mechanical,
    /// always-runnable enforcement of BC-3.6 that BC-4.2's table asks for.
    /// </summary>
    public struct ShaperLightRigCompiled
    {
        /// <summary>LR-1.4. The hard cap. Changing it touches this constant, the eight slots, and one test.</summary>
        public const int MaxLights = 8;

        /// <summary>How many of the eight slots are live. Never above <see cref="MaxLights"/>.</summary>
        public int count;

        /// <summary>The eight slots, in AUTHORED ORDER. No stage may reorder them (LR-2.3).</summary>
        public ShaperLightCompiled l0, l1, l2, l3, l4, l5, l6, l7;

        /// <summary>LINEAR ambient colour times ambient intensity, folded at compile (LR-1.3).</summary>
        public float ambR, ambG, ambB;

        /// <summary>
        /// Slot <paramref name="i"/>. Called by the law's own loop; never allocates.
        /// <c>readonly</c> is load-bearing rather than decorative: without it the compiler must take a
        /// DEFENSIVE COPY of the whole rig struct on every call through an <c>in</c> parameter, which is a
        /// stack copy of a hundred-odd floats per sample per light. It is not a heap allocation, so LT-2 would
        /// not see it; it is simply the cost the blittable-by-<c>in</c> design exists to avoid.
        /// </summary>
        public readonly ShaperLightCompiled At(int i)
        {
            switch (i)
            {
                case 0: return l0;
                case 1: return l1;
                case 2: return l2;
                case 3: return l3;
                case 4: return l4;
                case 5: return l5;
                case 6: return l6;
                default: return l7;
            }
        }

        /// <summary>Write slot <paramref name="i"/>. Compile-time only.</summary>
        public void Set(int i, ShaperLightCompiled v)
        {
            switch (i)
            {
                case 0: l0 = v; break;
                case 1: l1 = v; break;
                case 2: l2 = v; break;
                case 3: l3 = v; break;
                case 4: l4 = v; break;
                case 5: l5 = v; break;
                case 6: l6 = v; break;
                default: l7 = v; break;
            }
        }
    }

    /// <summary>
    /// The per-layer response block, compiled. LR-2.2 / LR-4.
    ///
    /// <c>castShadows</c> and <c>receiveShadows</c> are deliberately NOT here: they change no pixel (LR-4.5)
    /// and a field the law can see is a field the law will eventually be blamed for.
    /// </summary>
    public struct ShaperResponseCompiled
    {
        /// <summary>
        /// 0 or 1. An <c>int</c> and not a <c>bool</c>, because <c>bool</c> has no guaranteed blittable width
        /// — the same reason <c>ShaperFillOp</c> stores booleans as ints (<c>ShaperFillCompiler.cs:88</c>).
        /// </summary>
        public int receive;

        /// <summary>LR-4.2. Gates diffuse and specular; NOT ambient and NOT rim.</summary>
        public float intensityScale;

        /// <summary>LR-4.6 / LR-2.5.</summary>
        public float rimStrength, rimPower;

        /// <summary>LR-4.4.</summary>
        public float specular, specularPower;

        /// <summary>LR-4.4, LINEAR.</summary>
        public float specTintR, specTintG, specTintB;

        /// <summary>
        /// The identity response: <c>L = (1,1,1)</c>, <c>S = (0,0,0)</c> — LR-4.3's "pass through unlit".
        /// </summary>
        public static ShaperResponseCompiled Unlit => new ShaperResponseCompiled { receive = 0 };
    }

    /// <summary>
    /// THE shading law (LR-2.1). One implementation, called by every family — Silhouette and Solids alike.
    /// Enforced structurally by LT-1b, numerically by LT-1.
    ///
    /// <b>The law takes a surface normal as an INPUT.</b> It never derives one, never reads a sheet, never
    /// allocates, and never knows what coverage is. BC-3.6 states the same rule from the other side and makes
    /// it mandatory: "The shading stage MUST receive a surface direction published by the shape. It MUST NOT
    /// derive one by differencing a depth or height buffer" (<c>BUFFER_CONTRACT.md:230</c>).
    ///
    /// <b>Every parameter is a float or an <c>in</c> struct of floats and ints.</b> No array, no sheet, no
    /// interface, no delegate, no <c>Color</c>, no <c>Vector3</c>. That is not stylistic austerity: BC-4.2's
    /// own correction says the blittable signature "closes one door: nothing managed can arrive THROUGH THE
    /// PARAMETER", and LT-13b's reflection assertion is the always-runnable enforcement of it.
    /// <c>Vector3</c> is refused even though it is blittable, because a <c>Vector3</c> invites
    /// <c>.normalized</c> — a property call that is not free in a hot loop; the shipped shape engine makes
    /// the same choice, passing twelve loose floats through <c>ShaperSdf.Evaluate</c>
    /// (<c>ShaperEvaluator.cs:67-71</c>).
    ///
    /// <b>What the law does NOT do (LR-2.6), enumerated because each is a thing somebody will try to add:</b>
    /// it does not know about coverage, alpha, veil or opacity; it does not know about shadow casting or
    /// receiving; it does not difference anything; it does not encode or decode sRGB (<c>EncodeToByte</c> has
    /// exactly one caller in this assembly, <c>ShaperFillResolver.Encode</c> — asserted by LT-5b); it does not
    /// clamp; it does not read the fill, the shape, the grid or any sheet (<c>ShaperSampleGrid</c> is not a
    /// parameter, so <c>pixelSize</c> and <c>edgeSoftness</c> cannot leak into shading); and it does not
    /// allocate, box, use LINQ or call <c>System.Random</c> (FC-5.1, asserted by LT-2).
    /// </summary>
    public static class ShaperLightLaw
    {
        /// <summary>
        /// Shade one sample. LR-2.2's signature, exactly.
        ///
        /// Returns TWO per-channel linear triples, and that is the structural decision of this part:
        /// <code>final = albedo * (lr,lg,lb) + (sr,sg,sb)</code>
        /// A single combined output would force the law to be handed the albedo, at which point it stops being
        /// a law and becomes a shading pipeline that each family has to feed differently.
        ///
        /// It does NOT clamp. <c>L</c> and <c>S</c> may exceed 1; an overbright highlight is legitimate and
        /// the only clamp is at the byte (<c>ShaperSrgb.EncodeToByte</c>,
        /// <c>ShaperFillContract.cs:444-449</c>), which is also what Pyre's <c>Over</c> already does.
        ///
        /// <b>The arithmetic, and the order, fixed so two implementations cannot disagree (LR-2.3).</b>
        /// Ambient FIRST — it is the accumulator's initial value, not a term added at the end, because adding
        /// it last would make it survive <c>intensityScale</c>, which LR-1.3 forbids. Then each light's
        /// diffuse into <c>L</c> and specular into <c>S</c> in RIG ORDER, which is authored order and which no
        /// stage may reorder (floating-point summation is not associative, so two lists that look identical
        /// must not disagree about which end is which). Then rim into <c>S</c> last.
        /// </summary>
        /// <param name="rig">The compiled document rig (LR-1.1).</param>
        /// <param name="resp">The receiving layer's compiled response block (LR-4.1).</param>
        /// <param name="px">Surface point X, canvas pixels (LR-1.5).</param>
        /// <param name="py">Surface point Y, canvas pixels.</param>
        /// <param name="pz">Surface point Z, canvas pixels, +Z toward the viewer.</param>
        /// <param name="nx">Surface normal X. UNIT, canvas frame. AN INPUT (LR-2.1).</param>
        /// <param name="ny">Surface normal Y.</param>
        /// <param name="nz">Surface normal Z.</param>
        /// <param name="vx">Unit direction toward the viewer, X. v1: (0,0,1).</param>
        /// <param name="vy">Unit direction toward the viewer, Y.</param>
        /// <param name="vz">Unit direction toward the viewer, Z.</param>
        public static void Shade(
            in ShaperLightRigCompiled rig,
            in ShaperResponseCompiled resp,
            float px, float py, float pz,
            float nx, float ny, float nz,
            float vx, float vy, float vz,
            out float lr, out float lg, out float lb,
            out float sr, out float sg, out float sb)
        {
            // LR-4.3 — receiveLighting == false is L = (1,1,1), S = (0,0,0): the albedo is written through
            // unchanged, BIT FOR BIT. Not a no-op in the pejorative sense, because it is testable: LT-8
            // renders T-0107's conformance fixture with a fully populated eight-light rig and every layer set
            // to receive off, and asserts the output is bit-identical to the stored golden. A build that
            // quietly applied ambient, or quietly added S, fails immediately.
            if (resp.receive == 0)
            {
                lr = 1f; lg = 1f; lb = 1f;
                sr = 0f; sg = 0f; sb = 0f;
                return;
            }

            // L starts at the AMBIENT, which is already colour times intensity, folded at compile (LR-1.3).
            // It is the accumulator's initial value and NOT a term added at the end — see the method summary.
            lr = rig.ambR; lg = rig.ambG; lb = rig.ambB;
            sr = 0f; sg = 0f; sb = 0f;

            float scale = resp.intensityScale;

            int count = rig.count;
            if (count > ShaperLightRigCompiled.MaxLights) count = ShaperLightRigCompiled.MaxLights;

            for (int i = 0; i < count; i++)
            {
                ShaperLightCompiled li = rig.At(i);

                float ldx, ldy, ldz, atten;
                if (li.kind == 0)
                {
                    // Directional: the unit direction TOWARD the light was precomputed at compile, and there
                    // is no falloff at all (LR-2.4).
                    ldx = li.dirX; ldy = li.dirY; ldz = li.dirZ;
                    atten = 1f;
                }
                else
                {
                    float dvx = li.posX - px, dvy = li.posY - py, dvz = li.posZ - pz;
                    float dist = Mathf.Sqrt(dvx * dvx + dvy * dvy + dvz * dvz);
                    float inv = 1f / (dist > 1e-4f ? dist : 1e-4f);
                    ldx = dvx * inv; ldy = dvy * inv; ldz = dvz * inv;

                    // LR-2.4: atten = 1 / (1 + dist^2 / range^2). Ported in FORM from
                    // PyreRenderer.cs:4343, with `range` promoted from (gemLightDistance + 1.2)*R (:4267) to
                    // an absolute canvas dial (LR-1.6). Three properties, none of which the obvious
                    // alternatives have: it is exactly 1/2 at dist == range, which makes `range` mean
                    // something an author can predict (and is what LT-6 hand-checks); it never reaches zero,
                    // so a light never produces a hard cutoff circle; and it costs one multiply, one add and
                    // one divide with no pow and no sqrt beyond the dist the direction already needed.
                    atten = 1f / (1f + dist * dist * li.invRangeSq);
                }

                float ndl = nx * ldx + ny * ldy + nz * ldz;
                if (ndl < 0f) ndl = 0f;

                float w = atten * scale;

                // DIFFUSE, multiplicative.
                float dw = ndl * w;
                lr += li.r * dw;
                lg += li.g * dw;
                lb += li.b * dw;

                // SPECULAR, additive. Blinn-Phong on the half vector.
                float hx = ldx + vx, hy = ldy + vy, hz = ldz + vz;
                float hl = Mathf.Sqrt(hx * hx + hy * hy + hz * hz);
                if (hl > 1e-8f)
                {
                    float hinv = 1f / hl;
                    float nh = nx * hx * hinv + ny * hy * hinv + nz * hz * hinv;
                    if (nh > 0f)
                    {
                        float spec = Mathf.Pow(nh, resp.specularPower) * resp.specular * li.specular * w;
                        sr += li.r * resp.specTintR * spec;
                        sg += li.g * resp.specTintG * spec;
                        sb += li.b * resp.specTintB * spec;
                    }
                }
            }

            // RIM, additive, ambient-tinted, and NOT attenuated and NOT scaled by intensityScale (LR-2.3):
            // rim stands for grazing-angle light from everywhere, and a layer dialling down its response to
            // the LAMPS has not dialled down the sky.
            //
            // LR-2.5: rim = pow(1 - clamp01(N.V), rimPower) * rimStrength. With V = (0,0,1) — v1's only view
            // direction — N.V is exactly nz, so this is identical to the reference app's
            // pow(1 - clamp01(normalZ), 2.2) (index.html:1510). The reference reads normalZ directly rather
            // than through a dot product, which is why normalZBase and slopeGain are not interchangeable
            // there (BUFFER_CONTRACT.md:246); here N is contractually UNIT (LR-3.5) so N.V and nz cannot
            // diverge, and the asymmetry disappears by construction rather than by care.
            //
            // The visible consequence, stated rather than implied (ShaperLightRig.RimNeedsRelief): on a FLAT
            // surface N.V == 1, so rim == 0 identically. In Wave 2 every Silhouette layer has a flat normal
            // (LR-3.2), so rim is visible on Solids and on a Constant-TILTED layer and nowhere else. That is
            // arithmetic, not a bug.
            if (resp.rimStrength != 0f)
            {
                float ndv = nx * vx + ny * vy + nz * vz;
                if (ndv < 0f) ndv = 0f; else if (ndv > 1f) ndv = 1f;
                float rim = Mathf.Pow(1f - ndv, resp.rimPower) * resp.rimStrength;
                sr += rig.ambR * rim;
                sg += rig.ambG * rim;
                sb += rig.ambB * rim;
            }
        }
    }
}
