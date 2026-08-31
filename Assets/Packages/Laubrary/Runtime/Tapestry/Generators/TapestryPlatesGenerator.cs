// TapestryPlatesGenerator — sci-fi beveled plating, ported from the Kiln evolutionary generator
// `agents/plates/gen.py` (gen 11, 3517 lines, tapshape's Shape-project contract: render(genome, rng) -> HxW
// float height field via HeightCanvas.stamp()) into Tapestry's per-pixel Color32-painting model. Eleven
// generations of that generator's own evolution converged on ONE genuinely distinctive read, and this port
// exists to keep that read rather than just "some beveled rectangles":
//
//   * a wrapped grid of plates whose SIZE and CORNER TREATMENT vary plate to plate (round / chamfer /
//     octagon — one vocabulary per tile, gen 6's "should have less 90-degree corners" fix), with real
//     per-plate bevel shading (a lit edge and a shadowed edge, from the plate's own SDF gradient);
//   * a single diagonal FAULT channel across the whole tile, wrap-exact by construction, milled straight to
//     the seam colour with no bevel or depth of its own — "it's enough that they divide the other shapes,
//     they shouldn't also have their own bevel, just the background colour" (gen 8's fix, after the human
//     wrote the same complaint on three separate tiles);
//   * genuine ELEVATION LAYERING: a `nest` — a smaller copy of a plate's own silhouette stamped concentrically
//     ON it at a higher tier ("I like the doubble plates", the only unqualified praise gen 7 got) — and a
//     `rivet` — a small domed stamp raised again above THAT. Panel, then nest, then rivet, each genuinely
//     above the last, and the fault channel cuts through all three where it crosses, exactly as gen.py's own
//     draw_fault() mills its channel after every plate in a region is already drawn.
//
// ALWAYS tileable by construction, matching TapestryPanelsGenerator's own convention (and gen.py's own
// frand()/SdfCanvas): every random decision is a deterministic hash of a cell's WRAPPED index via HashCell,
// never a sequentially-advancing RNG, so a cell reached from either side of a seam agrees with itself.
//
// SIMPLIFICATIONS from the Python source (deliberate, and worth stating plainly rather than dropping quietly):
//   * Tapestry has no HeightCanvas / no true height field and no separate visualiser pass — this generator
//     paints Color32 pixels directly. Elevation "layering" is therefore expressed as painting order (panel,
//     then nest overriding it where present, then rivet overriding that) rather than max-composition on a
//     shared height buffer, and gen.py's coarse elevation TERRACES (whole patches of plates sitting visibly
//     higher/lower than their neighbours) are approximated as a brightness step over the same coarse patches
//     (`tierVariance`/`terraceGrid`) rather than true elevation the shading pass would read.
//   * gen.py builds an explicit list of Plate objects (greedy rectangle-merge grid, connected-component fault
//     splitting via scipy, symmetry folding by array transform, an 11-entry LAYOUT MENU of grammars — blocks,
//     bands, chamfer, hex, radial, pinwheel, truchet, weave, ziggurat, lozenge — plus split/bay mixes and a
//     "one variation axis per tile" governor). This port keeps only the wrapped-grid family (closest to
//     `blocks`/`chamfer`), expressed the same way TapestryPanelsGenerator already is: a per-pixel nearest-cell
//     SDF search, no intermediate plate-object list. "Varying sizes" is therefore approximated by giving each
//     cell its own hashed size multiplier (a bigger cell's SDF simply wins the nearest-cell contest over its
//     smaller neighbours near its own centre) rather than a literal greedy multi-cell rectangle merge — a
//     Voronoi/Worley-style approximation appropriate to this architecture, not a 1:1 port of the merge grid.
//   * gen.py's PROFILES (linear / step / smooth, a genuine width-vs-shape curve choice per bevel) collapse
//     here to one continuous shading model (the same linear-in-lit blend TapestryPanelsGenerator already
//     uses) — a discrete "hard cliff" step profile has no direct translation without a true height buffer for
//     a later shading pass to read a zero gradient off.
//   * `hull` mode (plates on a milled deck rather than bare ground — a seam-depth choice only), the "one
//     variation axis" governor, and the port/vent/fin/inset face motifs are not ported; `nest` and `rivet`
//     were chosen because they are the two motifs that are genuine ELEVATION LAYERING (the behaviour this
//     port is explicitly about), while inset/vent/port/fins are groove/hole treatments Tapestry's other
//     generators and modifiers already cover in spirit.
using UnityEngine;

namespace Laubrary.Tapestry
{
    [TapestryGeneratorInfo("Plates", "Shape")]
    [System.Serializable]
    public class TapestryPlatesGenerator : TapestryGenerator
    {
        [Range(2, 20)] public int gridSize = 6;
        [Range(0f, 0.4f)] public float gap = 0.05f;
        [Range(0f, 0.5f)] public float jitter = 0.12f;
        [Range(0f, 0.6f)] public float sizeVariety = 0.35f;

        [Range(0f, 1f)] public float cornerRadius = 0.28f;
        [Range(0f, 1f)] public float roundedChance = 0.35f;
        [Range(0f, 1f)] public float octagonChance = 0.5f;

        [Range(0.002f, 0.02f)] public float bevelWidth = 0.008f;
        [Range(0f, 0.5f)] public float bevelVariance = 0.2f;
        [Range(0f, 360f)] public float lightAngle = 135f;

        public Color baseColor = new Color(0.60f, 0.61f, 0.63f, 1f);
        [Range(0f, 0.5f)] public float tintVariance = 0.10f;
        public Color highlightColor = new Color(0.95f, 0.96f, 0.98f, 1f);
        public Color shadowColor = new Color(0.15f, 0.15f, 0.17f, 1f);
        public Color seamColor = new Color(0.08f, 0.08f, 0.09f, 1f);

        [Range(0f, 0.4f)] public float tierVariance = 0.15f;
        [Range(2, 6)] public int terraceGrid = 3;

        [Range(0f, 1f)] public float nestChance = 0.25f;
        public Color nestColor = new Color(0.66f, 0.67f, 0.69f, 1f);

        [Range(0f, 1f)] public float rivetChance = 0.2f;
        public Color rivetColor = new Color(0.72f, 0.73f, 0.75f, 1f);

        [Range(0f, 1f)] public float faultChance = 0.5f;
        [Range(0.02f, 0.08f)] public float faultWidth = 0.045f;

        public int seed = 0;

        public override string DisplayName => "Plates";
        public override string Description =>
            "Sci-fi beveled plating ported from the Kiln 'plates' evolutionary generator (gen 11): a wrapped "
            + "grid of plates that vary in size and corner treatment, a diagonal fault channel milled straight "
            + "to the seam colour, and layered nest/rivet details stamped genuinely above their parent plate. "
            + "Neutral grey by default so it Multiply-blends cleanly over a coloured Surface layer below it. "
            + "Always seamless, no matter the settings.";

        // Gen-11 constants from agents/plates/gen.py — the crispness cap that is the bulk of that source's
        // answer to four generations of "too blurry" notes, and the flat-shelf guarantee that stops a nested
        // stamp's own ramp from running into its parent's.
        const float BevelFrac = 0.115f;
        const float BevelMax = 0.0115f;
        const float BevelMin = 0.0035f;
        const float ShelfFlat = 0.012f;

        public override void Generate(in TapestryGenCtx ctx, Color32[] target)
        {
            int W = ctx.width, H = ctx.height;
            int grid = Mathf.Max(2, gridSize);
            int fullSeed = seed + ctx.seed;
            Vector2 lightDir = new Vector2(Mathf.Cos(lightAngle * Mathf.Deg2Rad), Mathf.Sin(lightAngle * Mathf.Deg2Rad));

            // ONE corner vocabulary for the whole tile (gen.py's `cornerStyle` gene — "should have less
            // 90-degree corners", and every tile the human graded well kept a single silhouette family rather
            // than mixing round/chamfer/octagon plate to plate). Chosen once from the tile's own seed, not
            // per cell.
            float styleU = HashCell(0, 0, fullSeed, 500);
            int cornerStyle; // 0 = round, 1 = chamfer, 2 = octagon
            if (styleU < roundedChance) cornerStyle = 0;
            else
            {
                float rem = (styleU - roundedChance) / Mathf.Max(1e-6f, 1f - roundedChance);
                cornerStyle = rem < octagonChance ? 2 : 1;
            }

            // THE FAULT — one diagonal channel across the whole tile, wrap-exact by construction (the line
            // family a*x + b*y = k + n is periodic in both axes for integer a, b), milled straight to the
            // seam colour with no bevel of its own. Decided once per Generate call, not per pixel.
            bool faultOn = faultChance > 0f && HashCell(0, 0, fullSeed, 101) < faultChance;
            int fa = 1, fb = 1; float fk = 0f, fHalfWidth = 0f, fHyp = 1f;
            if (faultOn)
            {
                fb = HashCell(0, 0, fullSeed, 102) < 0.5f ? 1 : -1;
                fk = HashCell(0, 0, fullSeed, 103);
                fHalfWidth = faultWidth * Mathf.Lerp(0.9f, 1.1f, HashCell(0, 0, fullSeed, 104));
                fHyp = Mathf.Sqrt(fa * fa + fb * fb);
            }

            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float gx = (x + 0.5f) / W * grid;
                    float gy = (y + 0.5f) / H * grid;
                    int baseCx = Mathf.FloorToInt(gx), baseCy = Mathf.FloorToInt(gy);

                    float bestDist = float.MaxValue;
                    Vector2 bestLocal = Vector2.zero, bestHalf = Vector2.one;
                    float bestTint = 1f;
                    int bestWcx = 0, bestWcy = 0;

                    // Same 3x3 wrapped-neighbourhood search TapestryPanelsGenerator uses: a plate's jitter and
                    // (here) its size roll can bleed slightly into a neighbouring cell, and near the canvas
                    // edge the "neighbour" IS the wrapped cell on the opposite side — a bigger cell's SDF
                    // simply wins the nearest-cell contest over its smaller neighbours, which is this port's
                    // stand-in for gen.py's literal greedy rectangle merge (see class doc).
                    for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int cx = baseCx + ox, cy = baseCy + oy;
                        int wcx = Wrap(cx, grid), wcy = Wrap(cy, grid);
                        float h1 = HashCell(wcx, wcy, fullSeed, 1);
                        float h2 = HashCell(wcx, wcy, fullSeed, 2);
                        float h3 = HashCell(wcx, wcy, fullSeed, 3);
                        float hSize = HashCell(wcx, wcy, fullSeed, 15);

                        float halfBase = (1f - gap) * 0.5f;
                        float sizeMul = Mathf.Clamp(1f + (hSize - 0.5f) * 2.4f * sizeVariety, 0.55f, 1.7f);
                        Vector2 half = Vector2.one * Mathf.Max(0.05f, halfBase * sizeMul);
                        Vector2 offset = new Vector2((h2 - 0.5f) * jitter, (h3 - 0.5f) * jitter);

                        Vector2 cellCenter = new Vector2(cx + 0.5f, cy + 0.5f) + offset;
                        Vector2 pLocal = new Vector2(gx, gy) - cellCenter;
                        float d = PlateSdf(pLocal, half, cornerRadius, cornerStyle);

                        if (d < bestDist)
                        {
                            bestDist = d;
                            bestLocal = pLocal;
                            bestHalf = half;
                            bestTint = 1f + (h1 - 0.5f) * 2f * tintVariance;
                            bestWcx = wcx; bestWcy = wcy;
                        }
                    }

                    float distUV = bestDist / grid;
                    Color result;
                    if (distUV >= 0f)
                    {
                        result = seamColor;
                    }
                    else
                    {
                        Vector2 halfUV = bestHalf / grid;
                        float smallUV = Mathf.Min(halfUV.x, halfUV.y);

                        // Terrace banding — a coarse patch of plates sits one visible brightness step from its
                        // neighbour patch, standing in for gen.py's real elevation terraces (see class doc:
                        // this generator paints colour directly, it has no separate height-shading pass).
                        int tg = Mathf.Max(2, terraceGrid);
                        Vector2 cellUV = new Vector2(bestWcx + 0.5f, bestWcy + 0.5f) / grid;
                        int tcx = Wrap(Mathf.FloorToInt(cellUV.x * tg), tg);
                        int tcy = Wrap(Mathf.FloorToInt(cellUV.y * tg), tg);
                        float terrace = HashCell(tcx, tcy, fullSeed, 777) - 0.5f;

                        float tint = bestTint * (1f + terrace * tierVariance * 0.6f);
                        Color panelColor = new Color(baseColor.r * tint, baseColor.g * tint, baseColor.b * tint, baseColor.a);

                        float w = CrispWidth(bevelWidth * (1f + bevelVariance *
                            (HashCell(bestWcx, bestWcy, fullSeed, 63) - 0.5f) * 0.8f), smallUV);
                        Vector2 grad = TapestrySdf.Gradient(
                            p => PlateSdf(p, bestHalf, cornerRadius, cornerStyle), bestLocal);
                        result = Shade(panelColor, distUV, w, grad, lightDir, highlightColor, shadowColor);

                        // NEST — a smaller copy of the SAME silhouette stamped concentrically on top, at a
                        // higher tier: genuine elevation layering ("I like the doubble plates", gen 8's only
                        // unqualified praise), not a second, unrelated shape on the plate's face. Inset by at
                        // least the flat SHELF gap (gen 11's fix) so its own ramp and the parent's never run
                        // into each other.
                        if (smallUV > 0.06f && HashCell(bestWcx, bestWcy, fullSeed, 20) < nestChance)
                        {
                            float pad = Mathf.Max(0.030f, w * 2.5f);
                            float shelf = w + w * 0.85f + ShelfFlat;
                            float margin = Mathf.Min(Mathf.Max(pad, shelf), smallUV * 0.40f);
                            float marginGrid = margin * grid;
                            Vector2 nestHalf = bestHalf - Vector2.one * marginGrid;
                            if (nestHalf.x > 0f && nestHalf.y > 0f)
                            {
                                float nestSdf = PlateSdf(bestLocal, nestHalf, cornerRadius, cornerStyle);
                                float nestDistUV = nestSdf / grid;
                                if (nestDistUV < 0f)
                                {
                                    float nestSmallUV = Mathf.Min(nestHalf.x, nestHalf.y) / grid;
                                    float nw = CrispWidth(bevelWidth * 0.85f, nestSmallUV);
                                    Vector2 nestGrad = TapestrySdf.Gradient(
                                        p => PlateSdf(p, nestHalf, cornerRadius, cornerStyle), bestLocal);
                                    Color nestBase = new Color(nestColor.r * tint, nestColor.g * tint,
                                        nestColor.b * tint, nestColor.a);
                                    result = Shade(nestBase, nestDistUV, nw, nestGrad, lightDir, highlightColor, shadowColor);
                                }
                            }
                        }

                        // RIVET — a small domed stamp raised again above the plate (and above a nest, if this
                        // plate carries one): the topmost tier this port's layering reaches. A disc's gradient
                        // is analytic (it always points radially outward from its own centre), so no finite-
                        // difference probe is needed here.
                        if (smallUV > 0.055f && HashCell(bestWcx, bestWcy, fullSeed, 30) < rivetChance)
                        {
                            float rrUV = Mathf.Min(0.010f + 0.010f * HashCell(bestWcx, bestWcy, fullSeed, 31),
                                smallUV * 0.22f);
                            float rrGrid = rrUV * grid;
                            float discSdf = bestLocal.magnitude - rrGrid;
                            float discDistUV = discSdf / grid;
                            if (discDistUV < 0f)
                            {
                                float rw = CrispWidth(bevelWidth * 0.45f, rrUV * 0.9f);
                                Vector2 rGrad = bestLocal.sqrMagnitude > 1e-10f ? bestLocal.normalized : Vector2.zero;
                                Color rivetBase = new Color(rivetColor.r * tint, rivetColor.g * tint,
                                    rivetColor.b * tint, rivetColor.a);
                                result = Shade(rivetBase, discDistUV, rw, rGrad, lightDir, highlightColor, shadowColor);
                            }
                        }
                    }

                    // THE FAULT, drawn last — it mills through everything above it (panel, nest, rivet alike),
                    // exactly as gen.py's own draw_fault() runs after every plate in the region is finished.
                    if (faultOn)
                    {
                        float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                        float raw = fa * u + fb * v - fk;
                        float shear = WrapFold(raw) / fHyp;
                        float channel = Mathf.Abs(shear) - fHalfWidth;
                        float aa = 1.5f / W;
                        float t = Mathf.Clamp01(0.5f - channel / aa);
                        if (t > 0f) result = Color.Lerp(result, seamColor, t);
                    }

                    target[y * W + x] = result;
                }
            }
        }

        /// The one corner vocabulary, matching gen.py's `plate_silhouette` exactly: round is a plain fillet;
        /// octagon is a hard 45-degree cut of the plate's own smaller half-extent (eased with a small fillet
        /// so the eight corners aren't knife-edged); chamfer is a modest 45-degree nick on all four corners.
        /// None of the three cases ever returns a right angle.
        static float PlateSdf(Vector2 p, Vector2 half, float radiusGene, int style)
        {
            float small = Mathf.Min(half.x, half.y);
            if (style == 0) // round
                return TapestrySdf.RoundBox(p, half, radiusGene * small);
            if (style == 2) // octagon
            {
                float c = small * (0.50f + 0.36f * radiusGene);
                float baseR = Mathf.Min(radiusGene * small * 0.35f, small * 0.12f);
                float baseSdf = TapestrySdf.RoundBox(p, half, baseR);
                return Mathf.Max(baseSdf, Diamond(p, half.x + half.y - c));
            }
            // chamfer
            float cc = Mathf.Min(Mathf.Max(radiusGene, 0.16f) * small * 1.4f, small * 0.46f);
            return Mathf.Max(TapestrySdf.RoundBox(p, half, 0f), Diamond(p, half.x + half.y - cc));
        }

        /// A 45-degree square, used for the corner chamfers/octagon cut above — gen.py's `diamond_sdf`.
        static float Diamond(Vector2 p, float s) => (Mathf.Abs(p.x) + Mathf.Abs(p.y) - s) * 0.70710678f;

        /// THE CRISPNESS CAP (gen.py's `crisp_width`) — a feature keeps a flat top face only if its bevel is
        /// small against its own half-extent, capped both per-feature (BevelFrac of its own smaller half) and
        /// by an absolute ceiling (BevelMax), and never thinner than BevelMin.
        static float CrispWidth(float want, float halfUV)
        {
            float capped = Mathf.Max(halfUV * BevelFrac, BevelMin);
            float w = Mathf.Max(want, BevelMin);
            w = Mathf.Min(w, capped);
            w = Mathf.Min(w, BevelMax);
            return w;
        }

        /// Shared bevel shading for the panel, its nest and its rivet alike: blend towards the highlight or
        /// shadow colour based on how much the surface's own SDF gradient faces the light, fading to a flat
        /// top face once the bevel ramp is behind the pixel.
        static Color Shade(Color baseCol, float distUV, float crispW, Vector2 grad, Vector2 lightDir,
            Color hi, Color sh)
        {
            float bevelT = Mathf.Clamp01(-distUV / Mathf.Max(0.0001f, crispW));
            if (bevelT >= 1f) return baseCol;
            float lit = Vector2.Dot(grad, lightDir) * (1f - bevelT);
            return lit >= 0f ? Color.Lerp(baseCol, hi, lit) : Color.Lerp(baseCol, sh, -lit);
        }

        static int Wrap(int v, int n) => ((v % n) + n) % n;

        /// Fold an arbitrary real value into [-0.5, 0.5) — gen.py's `(v + 0.5) % 1.0 - 0.5`, used to make the
        /// fault's line family wrap-exact across the seam.
        static float WrapFold(float v)
        {
            v -= Mathf.Floor(v);
            return v >= 0.5f ? v - 1f : v;
        }

        // Deterministic per-cell pseudo-random value keyed by (cx, cy, seed, salt) — identical algorithm to
        // TapestryPanelsGenerator's own HashCell (and to gen.py's own frand()/SdfCanvas.hash_cell): NOT a
        // sequentially-advancing RNG, so a wrapped neighbour cell always agrees with its "home" cell.
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
