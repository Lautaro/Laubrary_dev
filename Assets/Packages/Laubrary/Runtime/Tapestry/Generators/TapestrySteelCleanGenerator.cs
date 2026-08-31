// TapestrySteelCleanGenerator — a dark, almost-black riveted alloy panel, ported from Kiln's
// "steel_clean" Surface agent (Tapestry Surface project, gen 12/13; see project_kiln.md /
// steel_clean/gen.py). Two families on one metal<->polymer dial, split hard at alloyVal's own
// midpoint: coal-dark anodised alloy in a near-black studio, or shiny snow plasti-steel in a
// lit one. Lit entirely by a closed-form reflected studio sampled by the reflection vector
// (a floor/sky gradient, a soft key softbox, three light bars, a faint horizon band), plus a
// sharp untinted clearcoat reflection of the same room. Finish: orange peel + a single broad
// bow (what makes an otherwise-flat plate catch the studio), satin micro-tooth, and sparse
// wrapped hairline scratches that dull the surface rather than draw on it.
//
// THE ARCHITECTURAL ADAPTATION. steel_clean's one distinguishing, evolved feature is its
// rivets: beads walked along an inset contour of a shape's own boundary, corners bolted first
// and unconditionally, sides grouped as a run / bare / corner-triad / one-accent. In Kiln that
// boundary came from an externally supplied SDF (a shape the harness handed the material).
// Tapestry's TapestryGenerator contract has no such input — a generator paints the whole
// tileable field itself. Rather than drop the rivet feature (which is the entire point of this
// agent, distinguishing it from its "steel" sibling), this port gives itself its OWN notion of
// "panel" the same way TapestryPanelsGenerator does: a wrapped grid of jittered rectangular
// cells, built from the exact HashCell-per-wrapped-cell + RoundBox SDF technique that generator
// already uses. Every cell is then a genuine rectangle with real corners and real sides, so the
// corner-first / side-grouped placement logic ports as EXACT analytic geometry (corner points,
// straight-side arc length) instead of Kiln's jump-flooded distance field + predictor-corrector
// contour walk — that machinery existed only because Kiln's shape was arbitrary; ours is
// authored, so its corners are already known in closed form. The panel grid also stands in for
// the "handed height field" everywhere Kiln's shading read it (the macro bevel normal, the
// cavity/occlusion term) — each cell is a shallow stamped plateau with a soft edge bevel, and
// the gap between cells reads as a darker recessed seam (folded into `occlusion` — see Generate)
// rather than as transparency, since a Surface must still blanket the canvas opaquely.
//
// Simplifications from the source, all deliberate (see the class's port report for the fuller
// account): panel corners are sharp, not optionally rounded (a bolt needs a real corner to sit
// on); Kiln's evolutionary weighted-sampling tables (GROUP_PICK / STYLE_PICK, which biased a
// uniformly-sampled gene toward the layouts/styles the sheet liked) collapse to direct dial
// choices — an artist's slider should offer every option evenly, not favour breeding history;
// the disc/stripe "ring" contour branch and the thin-shape medial-axis fallback don't apply,
// since a self-generated cell is always a rectangle with four real corners and never too thin
// to hold an inset row; and a few Python locals that were themselves dead code in the source
// (mz/fv, the deleted thin-film index; crest/slope/flat, computed only for debug output) are
// dropped rather than carried forward as unused work.
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Tapestry
{
    public enum TapestrySteelRivetLayout { Run, Bare, Triad, Accent }
    public enum TapestrySteelRivetStyle { Blob, Square, Diamond }

    [TapestryGeneratorInfo("Steel (Riveted)", "Surface")]
    [System.Serializable]
    public class TapestrySteelCleanGenerator : TapestryGenerator
    {
        // ---- the self-generated panel grid (this port's own device for "where a border is") ----
        [Range(2, 20)] public int gridSize = 6;
        [Range(0f, 0.4f)] public float panelGap = 0.08f;
        [Range(0f, 0.5f)] public float panelJitter = 0.10f;
        // Fraction of a cell's own width the edge bevel ramps over.
        [Range(0.005f, 0.08f)] public float bevelWidth = 0.035f;
        [Range(2.5f, 9f)] public float bevelHeight = 5f;

        // ---- substance (alloy family / tint) ----
        [Range(0f, 1f)] public float alloyHue = 0.55f;
        [Range(0f, 0.20f)] public float alloySat = 0.08f;
        // Hard split at its own midpoint (0.55): below is the coal-dark anodised family, above
        // is shiny snow plasti-steel — a deliberate two-family jump, not a crossfade (see the
        // Python module docstring: a crossfade would fill the middle with the mid-grey both
        // families were built to get away from).
        [Range(0.20f, 0.90f)] public float alloyVal = 0.32f;
        [Range(0f, 1f)] public float polymer = 0.25f;
        [Range(0f, 0.09f)] public float tintDrift = 0.03f;

        // ---- finish (isotropic only — nothing here has a direction) ----
        [Range(2, 5)] public int peelCells = 3;
        [Range(0.10f, 0.50f)] public float peel = 0.25f;
        [Range(0.10f, 1.00f)] public float warp = 0.45f;
        [Range(0f, 0.18f)] public float micro = 0.08f;

        // ---- texture: sparse wrapped hairline scratches ----
        [Range(0.05f, 0.24f)] public float scratch = 0.14f;
        [Range(3, 5)] public int scratchCells = 4;

        // ---- texture: the rivets ----
        [Range(0.40f, 1.00f)] public float bump = 0.70f;
        public TapestrySteelRivetStyle bumpStyle = TapestrySteelRivetStyle.Blob;
        public TapestrySteelRivetLayout bumpLayout = TapestrySteelRivetLayout.Run;
        // Fraction of a cell's own width the rivet row sits in from that cell's edge.
        [Range(0.074f, 0.106f)] public float bumpInset = 0.090f;
        // Fraction of a cell's own width between two rivets along a side.
        [Range(0.190f, 0.310f)] public float bumpSpacing = 0.250f;
        // Fraction of a cell's own width — the rivet's own radius.
        [Range(0.0115f, 0.0160f)] public float bumpSize = 0.0140f;

        [Range(0f, 0.55f)] public float glossVar = 0.20f;
        [Range(0.15f, 0.65f)] public float occlusion = 0.40f;

        // ---- environment (the reflected studio) ----
        [Range(0f, 6.2831853f)] public float envRot = 1.0f;
        [Range(0.35f, 1f)] public float envSharp = 0.6f;
        [Range(0f, 1f)] public float studioDark = 0.6f;
        [Range(0.42f, 0.90f)] public float reflect = 0.65f;

        // ---- direct light ----
        [Range(0f, 6.2831853f)] public float lightAngle = 0.9f;
        [Range(0.05f, 0.26f)] public float specRough = 0.15f;
        [Range(0.40f, 2.00f)] public float specStrength = 1.0f;
        [Range(0.15f, 1.00f)] public float coat = 0.5f;
        [Range(0.10f, 0.85f)] public float coatRefl = 0.35f;
        [Range(1.05f, 1.65f)] public float contrast = 1.25f;

        public int seed = 0;

        public override string DisplayName => "Steel (Riveted)";
        public override string Description =>
            "A dark, near-black riveted alloy panel — coal-dark anodised or shiny snow "
            + "plasti-steel (a hard split at alloyVal's own midpoint), lit by a closed-form "
            + "reflected studio. Builds its own wrapped grid of panel cells purely to know "
            + "where a border is, corner-bolts every cell first and unconditionally, then "
            + "fits a run / bare / corner-triad / single-accent along each side. Always seamless.";

        // ---- constants mirroring the Python module's own file-level constants ----
        static readonly float[] AlloyHues = { 0.09f, 0.13f, 0.42f, 0.50f, 0.55f, 0.58f, 0.63f, 0.70f };
        const int EnvBars = 3;
        const float BarGain = 2.6f;
        const float ExposureCoal = 0.84f;
        const float ExposureSnow = 1.30f;
        const float PivotCoal = 0.07f;
        const float PivotSnow = 0.42f;
        const float CornerBoltClearS = 0.90f;
        const float CornerBoltClearR = 6.0f;
        const float CornerTriadArmFrac = 0.34f;
        const float RivetClearancePx = 3.0f;
        const float RivetInsetMaxFrac = 0.42f;

        public override void Generate(in TapestryGenCtx ctx, Color32[] target)
        {
            int size = ctx.width;
            int grid = Mathf.Max(2, gridSize);
            int fullSeed = seed + ctx.seed;
            float cellPx = size / (float)grid;

            // -------------------------- per-draw substance / family constants --------------------------
            float toneRaw = Mathf.Clamp01((alloyVal - 0.20f) / 0.70f);
            bool snow = toneRaw >= 0.5f;
            float pos = snow ? (toneRaw - 0.5f) / 0.5f : toneRaw / 0.5f;

            float polymerEff = snow ? (0.45f + 0.55f * polymer) : polymer;
            float metalness = 1f - polymerEff;

            float alloyHueResolved = InterpAlloyHue(alloyHue);
            float tone = Mathf.Pow(pos, 2.2f);
            float val = snow
                ? 0.72f + 0.24f * pos
                : (0.018f + 0.150f * tone) * (0.55f + 0.42f * polymerEff) + 0.110f * polymerEff * (0.30f + 0.70f * pos);
            float hueGate = snow ? (0.13f + 0.10f * pos) : (0.04f + 0.14f * pos);
            float sat = alloySat * (0.55f + 1.35f * polymerEff) * hueGate;
            Color alloy = Hsv(alloyHueResolved, Mathf.Min(sat, 1f), Mathf.Min(val, 1f));

            float envDark = Mathf.Clamp01(snow
                ? (0.28f + 0.40f * studioDark)
                : (0.74f + 0.24f * studioDark - 0.16f * pos));

            float alloyMax = Mathf.Max(1e-6f, Mathf.Max(alloy.r, Mathf.Max(alloy.g, alloy.b)));
            Color tint = new Color(alloy.r / alloyMax, alloy.g / alloyMax, alloy.b / alloyMax, 1f);
            Color specTint = new Color(
                tint.r * metalness + polymerEff, tint.g * metalness + polymerEff, tint.b * metalness + polymerEff, 1f);

            Color sky = Hsv(alloyHueResolved + 0.02f, (0.10f + 0.10f * Hash1(fullSeed, 341)) * hueGate, 1f);
            Color floorColor = Hsv(alloyHueResolved - 0.05f, 0.16f * hueGate, 0.26f + 0.14f * Hash1(fullSeed, 342));

            float f0Metal = snow ? (0.50f + 0.42f * pos) : (0.10f + 0.34f * pos);
            Color f0 = new Color(
                0.04f + (0.85f * tint.r * f0Metal - 0.04f) * metalness,
                0.04f + (0.85f * tint.g * f0Metal - 0.04f) * metalness,
                0.04f + (0.85f * tint.b * f0Metal - 0.04f) * metalness,
                1f);

            float fill = (snow ? 0.36f : 0.55f) * polymerEff;

            float lx = Mathf.Cos(lightAngle) * 0.80f, ly = Mathf.Sin(lightAngle) * 0.80f, lz = 0.60f;
            float lInv = 1f / Mathf.Sqrt(lx * lx + ly * ly + lz * lz);
            lx *= lInv; ly *= lInv; lz *= lInv;
            float hxv = lx, hyv = ly, hzv = lz + 1f;
            float hInv = 1f / Mathf.Sqrt(hxv * hxv + hyv * hyv + hzv * hzv);
            hxv *= hInv; hyv *= hInv; hzv *= hInv;

            // -------------------------- the panel grid's own macro height field --------------------------
            var hgtF = new float[size * size];
            float bevelWidthPx = Mathf.Max(1f, bevelWidth * cellPx);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float gxp = (x + 0.5f) / size * grid;
                    float gyp = (y + 0.5f) / size * grid;
                    int baseCx = Mathf.FloorToInt(gxp), baseCy = Mathf.FloorToInt(gyp);
                    float bestDist = float.MaxValue;
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int wcx = Wrap(baseCx + ox, grid), wcy = Wrap(baseCy + oy, grid);
                            CellGeometry(wcx, wcy, fullSeed, panelGap, panelJitter, out Vector2 halfG, out Vector2 offG);
                            Vector2 cellCenter = new Vector2(baseCx + ox + 0.5f, baseCy + oy + 0.5f) + offG;
                            Vector2 pLocal = new Vector2(gxp, gyp) - cellCenter;
                            float d = TapestrySdf.RoundBox(pLocal, halfG, 0f);
                            if (d < bestDist) bestDist = d;
                        }
                    }
                    float distPx = bestDist * cellPx;
                    hgtF[y * size + x] = Mathf.Clamp01(-distPx / bevelWidthPx);
                }
            }
            var loF = new float[size * size];
            {
                var b4 = BoxBlurWrapped(hgtF, size, 4);
                var b13 = BoxBlurWrapped(hgtF, size, 13);
                for (int i = 0; i < loF.Length; i++) loF[i] = 0.5f * (b4[i] + b13[i]);
            }

            // -------------------------- rivets: corner-first, side-grouped --------------------------
            List<RivetBead> beads = BuildRivets(grid, cellPx, fullSeed, out float radiusPx);
            float[] bumpF = StampBumps(size, beads, radiusPx);
            var haloF = new float[size * size];
            {
                int haloR = Mathf.Max(1, Mathf.RoundToInt(0.55f * Mathf.Max(radiusPx, 1f)));
                var blurred = BoxBlurWrapped(bumpF, size, haloR);
                for (int i = 0; i < haloF.Length; i++) haloF[i] = Mathf.Clamp01(blurred[i] - bumpF[i]);
            }

            // -------------------------- isotropic finish fields --------------------------
            var peelF = Fbm(size, peelCells, fullSeed, 11, 1);
            for (int i = 0; i < peelF.Length; i++) peelF[i] -= 0.5f;
            var warpF = Fbm(size, 2, fullSeed, 53, 1);
            for (int i = 0; i < warpF.Length; i++) warpF[i] -= 0.5f;
            var microF = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    microF[y * size + x] = HashCell(x, y, fullSeed, 97);
            microF = BoxBlurWrapped(microF, size, 1);
            for (int i = 0; i < microF.Length; i++) microF[i] -= 0.5f;
            var scratchF = ScratchField(size, scratchCells, fullSeed, 131);
            var driftF = Fbm(size, 3, fullSeed, 31, 4);
            for (int i = 0; i < driftF.Length; i++) driftF[i] -= 0.5f;
            var glossNoiseF = Fbm(size, 3, fullSeed, 71, 1);
            for (int i = 0; i < glossNoiseF.Length; i++) glossNoiseF[i] -= 0.5f;

            float pk = peel * 1.25f * (size / (float)Mathf.Max(peelCells, 1)) / 40f;
            float wk = warp * 4.5f * (size / 2f) / 40f;
            float mk = micro * 0.7f;
            float bk = bump * 9f;
            float sk = scratch * 0.9f;

            const float whitePoint = 1.90f;

            // -------------------------- final per-pixel shading --------------------------
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int idx = y * size + x;

                    GradAt(hgtF, size, x, y, out float gx, out float gy);
                    GradAt(warpF, size, x, y, out float wgx, out float wgy);
                    GradAt(peelF, size, x, y, out float pgx, out float pgy);
                    GradAt(microF, size, x, y, out float mgx, out float mgy);
                    GradAt(bumpF, size, x, y, out float bgx, out float bgy);
                    GradAt(scratchF, size, x, y, out float sgx, out float sgy);

                    float sxN = gy * bevelHeight + wgy * wk + pgy * pk + mgy * mk + bgy * bk + sgy * sk;
                    float syN = gx * bevelHeight + wgx * wk + pgx * pk + mgx * mk + bgx * bk + sgx * sk;
                    float nz = 1f / Mathf.Sqrt(sxN * sxN + syN * syN + 1f);
                    float nx = -sxN * nz, ny = -syN * nz;

                    float bwx = gy * bevelHeight + 0.5f * wgy * wk + bgy * bk;
                    float bwy = gx * bevelHeight + 0.5f * wgx * wk + bgx * bk;
                    float dz = 1f / Mathf.Sqrt(bwx * bwx + bwy * bwy + 1f);
                    float mnx = -bwx * dz, mny = -bwy * dz;

                    float ndl = Mathf.Clamp01(nx * lx + ny * ly + nz * lz);
                    float ndlD = Mathf.Clamp01(mnx * lx + mny * ly + dz * lz);
                    float ndh = Mathf.Clamp01(nx * hxv + ny * hyv + nz * hzv);
                    float ndv = Mathf.Clamp01(nz);

                    float cavity = Smoothstep(0.02f, 0.55f, loF[idx] - hgtF[idx]);
                    // A gap between panels reads as a distinct dark recessed seam rather than as
                    // transparency (a Surface must still blanket the canvas opaquely) — folded into
                    // occlusion, which already darkens albedo/reflect/ambient consistently everywhere.
                    float seamDark = Mathf.Lerp(0.30f, 1f, hgtF[idx]);
                    float occBase = Mathf.Clamp01(1f - occlusion * cavity - 0.55f * bump * haloF[idx]);
                    float occ = Mathf.Max(0.03f, occBase * seamDark);

                    float gloss = Mathf.Clamp(
                        1f + glossVar * glossNoiseF[idx] * 0.9f - 0.55f * micro - 0.30f * cavity
                            - 1.10f * scratchF[idx] * scratch,
                        0.35f, 1.15f);

                    float rx = 2f * nz * nx, ry = 2f * nz * ny;
                    Color env = EnvSample(rx, ry, fullSeed, envRot, envSharp, sky, floorColor, envDark);

                    float fs = Mathf.Pow(1f - ndv, 5f);
                    float fR = f0.r + (1f - f0.r) * fs;
                    float fG = f0.g + (1f - f0.g) * fs;
                    float fB = f0.b + (1f - f0.b) * fs;

                    float reflScale = reflect * (0.65f + 0.35f * gloss) * occ;
                    float reflR = env.r * fR * reflScale;
                    float reflG = env.g * fG * reflScale;
                    float reflB = env.b * fB * reflScale;

                    float rough = Mathf.Clamp(
                        specRough * (1f + 1.6f * micro) * (1f + 5f * scratchF[idx] * scratch) / Mathf.Max(gloss, 0.35f),
                        0.03f, 0.70f);
                    float spec = Ggx(ndh, rough) * specStrength * ndl;
                    float coatV = Ggx(ndh, 0.085f) * ndl * coat * (0.45f + 0.55f * polymerEff);

                    float crx = 2f * dz * mnx, cry = 2f * dz * mny;
                    Color cenv = EnvSample(crx, cry, fullSeed, envRot, 1f, sky, floorColor, envDark);
                    float cF = 0.04f + 0.96f * Mathf.Pow(1f - Mathf.Clamp01(dz), 5f);
                    float coatReflScale = coatRefl * cF * occ;
                    float coatReflR = cenv.r * coatReflScale;
                    float coatReflG = cenv.g * coatReflScale;
                    float coatReflB = cenv.b * coatReflScale;

                    float albMul = (1f + driftF[idx] * 2f * tintDrift) * occ;
                    float albR = Mathf.Clamp01(alloy.r * albMul);
                    float albG = Mathf.Clamp01(alloy.g * albMul);
                    float albB = Mathf.Clamp01(alloy.b * albMul);

                    float wrapAmt = 0.40f * polymerEff;
                    float ndlw = (ndlD + wrapAmt) / (1f + wrapAmt);
                    float kdR = (1f - fR) * (1f - 0.80f * metalness);
                    float kdG = (1f - fG) * (1f - 0.80f * metalness);
                    float kdB = (1f - fB) * (1f - 0.80f * metalness);
                    float diffuseScale = 0.30f + 0.82f * ndlw;

                    float litR = albR * kdR * diffuseScale;
                    float litG = albG * kdG * diffuseScale;
                    float litB = albB * kdB * diffuseScale;

                    litR += reflR; litG += reflG; litB += reflB;

                    float ambMetalScale = (0.045f + 0.10f * (1f - 0.88f * envDark)) * metalness * occ;
                    litR += tint.r * sky.r * ambMetalScale;
                    litG += tint.g * sky.g * ambMetalScale;
                    litB += tint.b * sky.b * ambMetalScale;

                    float fillScale = fill * occ * (1f - 0.80f * envDark);
                    litR += albR * sky.r * fillScale;
                    litG += albG * sky.g * fillScale;
                    litB += albB * sky.b * fillScale;

                    float specScale = spec * gloss;
                    litR += specTint.r * specScale;
                    litG += specTint.g * specScale;
                    litB += specTint.b * specScale;

                    litR += coatV; litG += coatV; litB += coatV;
                    litR += coatReflR; litG += coatReflG; litB += coatReflB;

                    float exposure = snow ? ExposureSnow : ExposureCoal;
                    litR *= exposure; litG *= exposure; litB *= exposure;

                    float pivot = snow ? PivotSnow : PivotCoal;
                    litR = (litR - pivot) * contrast + pivot;
                    litG = (litG - pivot) * contrast + pivot;
                    litB = (litB - pivot) * contrast + pivot;
                    litR = Mathf.Max(litR, 0f); litG = Mathf.Max(litG, 0f); litB = Mathf.Max(litB, 0f);

                    litR = litR * (1f + litR / (whitePoint * whitePoint)) / (1f + litR);
                    litG = litG * (1f + litG / (whitePoint * whitePoint)) / (1f + litG);
                    litB = litB * (1f + litB / (whitePoint * whitePoint)) / (1f + litB);

                    litR = Mathf.Clamp01(litR); litG = Mathf.Clamp01(litG); litB = Mathf.Clamp01(litB);

                    // A material blankets the whole field it paints, opaquely.
                    target[idx] = new Color32(
                        (byte)Mathf.RoundToInt(litR * 255f),
                        (byte)Mathf.RoundToInt(litG * 255f),
                        (byte)Mathf.RoundToInt(litB * 255f),
                        255);
                }
            }
        }

        // ------------------------------------------------------------------ rivet placement
        struct RivetBead
        {
            public Vector2 pos;
            public bool forceRound;
            public RivetBead(Vector2 p, bool force) { pos = p; forceRound = force; }
        }

        // Builds every rivet bead across the whole wrapped panel grid. Corners are bolted first
        // and unconditionally on every cell; what `bumpLayout` then adds is fitted along each
        // side, clear of the corner bolts by CORNER_BOLT_CLEAR_S/_R — the exact placement rules
        // steel_clean's `_beads` polygon branch uses, just against an analytic rectangle instead
        // of a traced contour.
        List<RivetBead> BuildRivets(int grid, float cellPx, int fullSeed, out float radiusPx)
        {
            var beads = new List<RivetBead>();
            float nominalHalfPx = Mathf.Max(0.05f, (1f - panelGap) * 0.5f) * cellPx;
            float maxInsetPx = Mathf.Max(1.2f, RivetInsetMaxFrac * nominalHalfPx);
            float insetPx = Mathf.Clamp(bumpInset * cellPx, 1.2f, maxInsetPx);
            radiusPx = Mathf.Min(bumpSize * cellPx, insetPx - RivetClearancePx);
            if (radiusPx < 0.9f) return beads;

            float spacingPx = Mathf.Max(bumpSpacing * cellPx, 3.40f * radiusPx);
            float pitchIn = Mathf.Max(4f * radiusPx, 0.5f * spacingPx);
            float clear = Mathf.Max(CornerBoltClearS * spacingPx, CornerBoltClearR * radiusPx);
            bool forceCornerRound = bumpLayout == TapestrySteelRivetLayout.Triad;

            for (int cy = 0; cy < grid; cy++)
            {
                for (int cx = 0; cx < grid; cx++)
                {
                    CellGeometry(cx, cy, fullSeed, panelGap, panelJitter, out Vector2 halfG, out Vector2 offG);
                    Vector2 halfPx = halfG * cellPx;
                    Vector2 centerPx = new Vector2((cx + 0.5f) * cellPx, (cy + 0.5f) * cellPx) + offG * cellPx;
                    Vector2 insetHalf = new Vector2(halfPx.x - insetPx, halfPx.y - insetPx);
                    if (insetHalf.x <= 0.5f || insetHalf.y <= 0.5f) continue;   // cell too small for this inset

                    Vector2 tl = centerPx + new Vector2(-insetHalf.x, -insetHalf.y);
                    Vector2 tr = centerPx + new Vector2(insetHalf.x, -insetHalf.y);
                    Vector2 br = centerPx + new Vector2(insetHalf.x, insetHalf.y);
                    Vector2 bl = centerPx + new Vector2(-insetHalf.x, insetHalf.y);
                    Vector2[] corners = { tl, tr, br, bl };
                    float[] sideLen = { 2f * insetHalf.x, 2f * insetHalf.y, 2f * insetHalf.x, 2f * insetHalf.y };
                    float shortest = Mathf.Min(sideLen[0], sideLen[1]);

                    for (int i = 0; i < 4; i++)
                        beads.Add(new RivetBead(corners[i], forceCornerRound));

                    if (bumpLayout == TapestrySteelRivetLayout.Bare)
                    {
                        // corner bolts only — nothing else, the sparsest layout there is.
                    }
                    else if (bumpLayout == TapestrySteelRivetLayout.Triad)
                    {
                        // A three-bolt cleat at each corner: the bend keeps its (forced-round)
                        // corner bolt, and one flanker runs out along each adjacent side.
                        float arm = Mathf.Min(pitchIn, CornerTriadArmFrac * shortest);
                        if (arm > 2.6f * radiusPx)
                        {
                            for (int i = 0; i < 4; i++)
                            {
                                Vector2 c = corners[i];
                                Vector2 next = corners[(i + 1) % 4];
                                Vector2 prev = corners[(i + 3) % 4];
                                beads.Add(new RivetBead(PointToward(c, next, arm), false));
                                beads.Add(new RivetBead(PointToward(c, prev, arm), false));
                            }
                        }
                    }
                    else if (bumpLayout == TapestrySteelRivetLayout.Accent)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            if (sideLen[i] - 2f * clear >= 0f)
                            {
                                Vector2 mid = Vector2.Lerp(corners[i], corners[(i + 1) % 4], 0.5f);
                                beads.Add(new RivetBead(mid, false));
                            }
                        }
                    }
                    else // Run
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            float room = sideLen[i] - 2f * clear;
                            if (room < 0f) continue;
                            int m = FitCount(room, 0f, spacingPx, 2.8f * radiusPx, out float step);
                            for (int j = 0; j < m; j++)
                            {
                                float d = clear + step * (j + 0.5f);
                                beads.Add(new RivetBead(PointToward(corners[i], corners[(i + 1) % 4], d), false));
                            }
                        }
                    }
                }
            }
            return beads;
        }

        // Stamps every bead into a whole-canvas relief field, wrapped — local windows rather than
        // whole-canvas passes per bead, same technique as TapestryPanelsGenerator's neighbour scan.
        // Every rivet is stamped at a fixed angle (RIVET_ANGLE = 0 in the source — every head on
        // every cell the same part fitted the same way round), so `a`/`b` are just the offset in
        // canvas axes, no rotation needed.
        float[] StampBumps(int size, List<RivetBead> beads, float radiusPx)
        {
            var outp = new float[size * size];
            if (radiusPx <= 0.05f || beads.Count == 0) return outp;
            int rWin = Mathf.CeilToInt(radiusPx * 1.75f) + 2;
            float soft = Mathf.Clamp(1.25f / radiusPx, 0.06f, 0.95f);
            int styleDial = (int)bumpStyle;
            for (int bi = 0; bi < beads.Count; bi++)
            {
                RivetBead b = beads[bi];
                int ix = Mathf.FloorToInt(b.pos.x), iy = Mathf.FloorToInt(b.pos.y);
                int style = b.forceRound ? 0 : styleDial;
                for (int oy = -rWin; oy <= rWin; oy++)
                {
                    for (int ox = -rWin; ox <= rWin; ox++)
                    {
                        float ddx = (ix + ox) - b.pos.x;
                        float ddy = (iy + oy) - b.pos.y;
                        float a = ddx / radiusPx;
                        float bb = ddy / radiusPx;
                        float m = Motif(a, bb, style, soft);
                        int wx = Wrap(ix + ox, size), wy = Wrap(iy + oy, size);
                        int idx = wy * size + wx;
                        if (m > outp[idx]) outp[idx] = m;
                    }
                }
            }
            return outp;
        }

        // ------------------------------------------------------------------ static helpers
        static Vector2 PointToward(Vector2 a, Vector2 b, float dist)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1e-6f) return a;
            return a + d * (dist / len);
        }

        static int FitCount(float room, float span, float gap, float minGap, out float step)
        {
            int m = Mathf.Max(1, Mathf.RoundToInt(room / Mathf.Max(span + gap, 1e-6f)));
            while (m > 1 && (room - span) / m - span < minGap) m--;
            step = (room - span) / m;
            return m;
        }

        // Same per-cell jitter/half-extent formula TapestryPanelsGenerator uses (salts 1-3; salt
        // 4, corner rounding, is not used here — see the class doc on sharp corners).
        static void CellGeometry(int wcx, int wcy, int fullSeed, float gap, float jitter,
            out Vector2 halfGrid, out Vector2 offsetGrid)
        {
            float h1 = HashCell(wcx, wcy, fullSeed, 1);
            float h2 = HashCell(wcx, wcy, fullSeed, 2);
            float h3 = HashCell(wcx, wcy, fullSeed, 3);
            float halfBase = (1f - gap) * 0.5f;
            float halfV = Mathf.Max(0.05f, halfBase * (1f - jitter * h1 * 0.6f));
            halfGrid = new Vector2(halfV, halfV);
            offsetGrid = new Vector2((h2 - 0.5f) * jitter, (h3 - 0.5f) * jitter);
        }

        static float Motif(float a, float b, int style, float soft)
        {
            if (style == 0) return Smoothstep(1.00f + soft, 0.55f, Mathf.Sqrt(a * a + b * b));           // blob
            if (style == 1) return Smoothstep(1.00f + soft, 0.72f, Mathf.Max(Mathf.Abs(a), Mathf.Abs(b))); // square
            return Smoothstep(1.10f + soft, 0.62f, Mathf.Abs(a) + Mathf.Abs(b));                          // diamond
        }

        static float Ggx(float ndh, float rough)
        {
            float a = Mathf.Clamp(rough * rough, 1e-6f, 1f);
            float a2 = a * a;
            float d = ndh * ndh * (a2 - 1f) + 1f;
            float v = a2 / Mathf.Max(d, 1e-9f);
            return v * v;
        }

        static Color EnvSample(float rx, float ry, int seed, float rot, float sharp, Color sky, Color floorColor, float dark)
        {
            dark = Mathf.Clamp01(dark);
            float cr = Mathf.Cos(rot), sr = Mathf.Sin(rot);
            float up = Mathf.Clamp(rx * sr + ry * cr, -1f, 1f);
            float az = rx * cr - ry * sr;

            float kx = (Hash1(seed, 350) - 0.5f) * 1.00f;
            float ky = (Hash1(seed, 351) - 0.5f) * 1.00f;
            float kw = 1.10f * (1f - 0.72f * dark);
            float kdx = rx - kx, kdy = ry - ky;
            float ku = kdx * cr - kdy * sr;
            float kv = kdx * sr + kdy * cr;
            float key = 1.55f * (1f - 0.94f * dark) * Mathf.Exp(-(ku * ku / (kw * 2.2f) + kv * kv / (kw * 0.55f)));

            float grad = Smoothstep(-0.75f, 0.75f, up);
            float swell = 0.16f * Mathf.Cos(2.6f * az + 6.283f * Hash1(seed, 331));

            float rr = Mathf.Sqrt(rx * rx + ry * ry);
            float soft = Smoothstep(0.40f, 0.04f, rr);
            float horizon = 0.22f * Mathf.Exp(-Mathf.Pow(up / (0.30f * (1f + 1.8f * soft)), 2f));
            float bargate = 0.35f + 0.65f * Smoothstep(0.02f, 0.30f, rr);
            float bars = 0f;
            for (int i = 0; i < EnvBars; i++)
            {
                float posB = -0.60f + 1.50f * Hash1(seed, 300 + i);
                float wid = (0.05f + 0.13f * Hash1(seed, 310 + i)) / Mathf.Max(sharp, 0.35f);
                wid = wid * (1f + 1.8f * soft);
                float amp = ((0.20f + 0.55f * Hash1(seed, 320 + i)) * (1f + 0.60f * dark) * BarGain) / (1f + 1.05f * soft);
                bars += amp * Mathf.Exp(-Mathf.Pow((up - posB) / wid, 2f));
            }
            bars *= bargate;

            float lum = Mathf.Max((0.34f + 1.00f * grad - horizon + swell) * (1f - 0.95f * dark), 0f);
            float colR = floorColor.r * (1f - grad) + sky.r * grad;
            float colG = floorColor.g * (1f - grad) + sky.g * grad;
            float colB = floorColor.b * (1f - grad) + sky.b * grad;
            float scale = lum + key;
            return new Color(colR * scale + bars * 0.80f, colG * scale + bars * 0.80f, colB * scale + bars * 0.80f, 1f);
        }

        static Color Hsv(float h, float s, float v)
        {
            h -= Mathf.Floor(h);
            int i = ((int)(h * 6f)) % 6;
            if (i < 0) i += 6;
            float f = h * 6f - Mathf.Floor(h * 6f);
            float p = v * (1f - s), q = v * (1f - s * f), t = v * (1f - s * (1f - f));
            switch (i)
            {
                case 0: return new Color(v, t, p, 1f);
                case 1: return new Color(q, v, p, 1f);
                case 2: return new Color(p, v, t, 1f);
                case 3: return new Color(p, q, v, 1f);
                case 4: return new Color(t, p, v, 1f);
                default: return new Color(v, p, q, 1f);
            }
        }

        static float InterpAlloyHue(float t)
        {
            int n = AlloyHues.Length;
            float p = Mathf.Clamp01(t) * (n - 1);
            int i0 = Mathf.FloorToInt(p);
            if (i0 >= n - 1) return AlloyHues[n - 1];
            float frac = p - i0;
            return Mathf.Lerp(AlloyHues[i0], AlloyHues[i0 + 1], frac);
        }

        static float VNoise(float u, float v, int cu, int cv, int seed, int salt)
        {
            cu = Mathf.Max(1, cu); cv = Mathf.Max(1, cv);
            float gu = u * cu, gv = v * cv;
            int u0 = Wrap(Mathf.FloorToInt(gu), cu);
            int v0 = Wrap(Mathf.FloorToInt(gv), cv);
            int u1 = Wrap(u0 + 1, cu), v1 = Wrap(v0 + 1, cv);
            float tu = gu - Mathf.Floor(gu), tv = gv - Mathf.Floor(gv);
            float su = tu * tu * (3f - 2f * tu), sv = tv * tv * (3f - 2f * tv);
            float n00 = HashCell(u0, v0, seed, salt);
            float n10 = HashCell(u1, v0, seed, salt);
            float n01 = HashCell(u0, v1, seed, salt);
            float n11 = HashCell(u1, v1, seed, salt);
            float top = n00 * (1f - su) + n10 * su;
            float bot = n01 * (1f - su) + n11 * su;
            return top * (1f - sv) + bot * sv;
        }

        static float[] Fbm(int size, int cells, int seed, int salt, int octaves)
        {
            var total = new float[size * size];
            float amp = 1f, asum = 0f;
            int c = Mathf.Max(1, cells);
            int oc = Mathf.Max(1, octaves);
            for (int o = 0; o < oc; o++)
            {
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                        total[y * size + x] += VNoise(u, v, c, c, seed, salt + o * 17) * amp;
                    }
                }
                asum += amp;
                amp *= 0.5f;
                c *= 2;
            }
            float inv = 1f / Mathf.Max(asum, 1e-9f);
            for (int i = 0; i < total.Length; i++) total[i] *= inv;
            return total;
        }

        // Sparse wrapped hairline scratches — a coarse grid, roughly a fifth of whose cells carry
        // one segment each with its own hashed angle, length and depth, tapered at both ends.
        static float[] ScratchField(int size, int cells, int seed, int salt)
        {
            var outp = new float[size * size];
            cells = Mathf.Max(2, cells);
            for (int j = 0; j < cells; j++)
            {
                for (int i = 0; i < cells; i++)
                {
                    if (HashCell(i, j, seed, salt) < 0.80f) continue;
                    float cx = (i + 0.15f + 0.70f * HashCell(i, j, seed, salt + 1)) / cells;
                    float cy = (j + 0.15f + 0.70f * HashCell(i, j, seed, salt + 2)) / cells;
                    float ang = 6.2831853f * HashCell(i, j, seed, salt + 3);
                    float ln = Mathf.Min((0.75f + 1.10f * HashCell(i, j, seed, salt + 4)) / cells, 0.45f);
                    float wd = (0.34f + 0.58f * HashCell(i, j, seed, salt + 5)) / size;
                    float dep = 0.30f + 0.70f * HashCell(i, j, seed, salt + 6);
                    float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                            float dx = Mod1(u - cx + 0.5f) - 0.5f;
                            float dy = Mod1(v - cy + 0.5f) - 0.5f;
                            float along = dx * ca + dy * sa;
                            float tclamp = Mathf.Clamp(along, -ln * 0.5f, ln * 0.5f);
                            float px = dx - tclamp * ca, py = dy - tclamp * sa;
                            float perp = Mathf.Sqrt(px * px + py * py);
                            float taper = 1f - 0.85f * Smoothstep(ln * 0.28f, ln * 0.50f, Mathf.Abs(along));
                            float val = dep * taper * Smoothstep(wd * 2.4f, wd * 0.35f, perp);
                            int idx = y * size + x;
                            if (val > outp[idx]) outp[idx] = val;
                        }
                    }
                }
            }
            return outp;
        }

        static float Mod1(float x) => x - Mathf.Floor(x);

        // Wrapped separable box blur (direct windowed average — the field IS the whole tileable
        // canvas, same reasoning TapestrySdf/canvas.gradient use for a wrapped finite difference).
        static float[] BoxBlurWrapped(float[] f, int size, int r)
        {
            if (r < 1) return (float[])f.Clone();
            int k = 2 * r + 1;
            var tmp = new float[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float sum = 0f;
                    for (int o = -r; o <= r; o++) sum += f[y * size + Wrap(x + o, size)];
                    tmp[y * size + x] = sum / k;
                }
            }
            var outp = new float[size * size];
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    float sum = 0f;
                    for (int o = -r; o <= r; o++) sum += tmp[Wrap(y + o, size) * size + x];
                    outp[y * size + x] = sum / k;
                }
            }
            return outp;
        }

        static void GradAt(float[] f, int size, int x, int y, out float gx, out float gy)
        {
            int xp = Wrap(x + 1, size), xm = Wrap(x - 1, size);
            int yp = Wrap(y + 1, size), ym = Wrap(y - 1, size);
            gx = (f[y * size + xp] - f[y * size + xm]) * 0.5f;
            gy = (f[yp * size + x] - f[ym * size + x]) * 0.5f;
        }

        // Standard smoothstep. e1 < e0 is legal and INVERTS the ramp.
        static float Smoothstep(float e0, float e1, float x)
        {
            float d = e1 - e0;
            if (Mathf.Abs(d) <= 1e-6f) d = 1e-6f;
            float t = Mathf.Clamp01((x - e0) / d);
            return t * t * (3f - 2f * t);
        }

        static float Hash1(int seed, int salt) => HashCell(1, 1, seed, salt);

        static int Wrap(int v, int n) => ((v % n) + n) % n;

        // Deterministic per-cell pseudo-random value keyed by (cx, cy, seed, salt) — same hash
        // TapestryPanelsGenerator uses, so a wrapped neighbour cell always agrees with its "home".
        static float HashCell(int cx, int cy, int seed, int salt)
        {
            unchecked
            {
                int h = cx * 374761393 + cy * 668265263 + seed * 1103515245 + salt * 2032854233;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7fffffff) / (float)int.MaxValue;
            }
        }
    }
}
