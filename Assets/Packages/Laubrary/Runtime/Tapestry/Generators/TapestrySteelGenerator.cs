// TapestrySteelGenerator — a standalone, full-canvas brushed/painted steel material, ported from Kiln's
// Tapestry-Surface "steel" agent (gen 8, agents/steel/gen.py). That project fed the generator a per-shape
// HEIGHT FIELD (sdf) it rendered onto; Tapestry generators get no such input, so per the porting brief this
// is adapted into a self-contained Surface: it paints its own brushed-steel pattern across the WHOLE canvas,
// fully opaque. A Tapestry Shape generator stacked above in Multiply blend is how a consumer clips it to a
// silhouette — masking is a layer-compositing concern, never this generator's own.
//
// PORTED FAITHFULLY: the broad tonal blotch, the isotropic finish/roughness field (mottle + micro-scale
// grit — never a directional brush), the meso-scale struck "dings" (discrete elliptical impressions with a
// ploughed lip), the broad waviness normal, TWO CROSSED sets of signed scratches (bright = exposed metal,
// dark = a shadowed groove — the one genuinely directional element, exactly as gen 8 built it, combined by
// magnitude so an intersection reads as whichever mark is deeper rather than a bright woven node), wrapped
// crater lattices for pitting/specks, noise-driven rust patches with a core/bloom crust variation, the
// roughness-driven reflection blur pyramid, the Blinn-Phong specular whose lobe widens with roughness, and
// the wrapped-unsharp clarity pass + contrast/knee tone mapping.
//
// SIMPLIFIED, AND WHY:
//  - Every term gen.py derived from the HANDED HEIGHT FIELD is gone: the bevel-height normal term, the
//    crest/slope-driven edge wear (chips), and oxide/wear PLACEMENT weighted toward cavities and edges. This
//    is the actual source of the "soft edge highlight/bevel rim" the reference montage showed on its test
//    shapes' silhouettes — it is a property of the SHAPE this material used to be handed, which a Tapestry
//    generator never receives, so it could not be ported and is not faked here.
//  - The dual wet-film/cured-plastic CLEARCOAT system (coat coverage, orange peel, wet pooling, the coat's
//    own second mirror/specular lobe) is dropped entirely — a large, mostly orthogonal "paint/coating" axis,
//    not the steel identity itself. What ships is the bare metal's own reflection and specular lobe.
//  - The rust patch's flake/undercut micro-layer is dropped for scope; the core/bloom crust variation that
//    remains still reads as patchy corrosion.
// Everything spatial is tileable by construction, same as every other Tapestry generator: every noise
// sample is a wrapped-lattice hash (a wrapped neighbour cell always agrees with its home cell), the
// scratches run along an INTEGER lattice direction (exactly periodic by construction), and every blur wraps.
using UnityEngine;

namespace Laubrary.Tapestry
{
    [TapestryGeneratorInfo("Steel", "Surface")]
    [System.Serializable]
    public class TapestrySteelGenerator : TapestryGenerator
    {
        // --- colour -------------------------------------------------------------------------
        public Color baseColor = new Color(0.40f, 0.45f, 0.52f, 1f);      // cool blue-grey steel tone
        public Color metalColor = new Color(0.62f, 0.64f, 0.66f, 1f);     // bare alloy exposed by wear
        [Range(0f, 0.3f)] public float tonalVariance = 0.11f;             // broad slow tonal drift
        public Color oxideColor = new Color(0.32f, 0.17f, 0.08f, 1f);     // rust; core is darker, bloom paler
        [Range(0f, 0.85f)] public float oxideAmount = 0.3f;
        [Range(0.15f, 0.9f)] public float oxideCrust = 0.5f;              // dark iron core <-> pale powdery bloom

        // --- finish: isotropic roughness, never a directional brush ---------------------------
        [Range(0f, 0.75f)] public float finish = 0.35f;                   // 0 = polished mirror, 1 = blasted satin
        [Range(0f, 0.45f)] public float finishMottle = 0.3f;              // how patchy that roughness is
        [Range(0.12f, 0.45f)] public float grit = 0.2f;                   // micro-scale roughness tooth

        // --- wear ------------------------------------------------------------------------------
        [Range(0f, 0.75f)] public float scratchAmount = 0.4f;
        [Range(0, 5)] public int scratchDir = 0;                          // which of 6 integer lattice directions
        [Range(10, 30)] public int scratchDensity = 18;
        [Range(0f, 0.3f)] public float pitting = 0.15f;                   // craters + finer specks

        // --- shading structure -------------------------------------------------------------------
        [Range(0.2f, 1f)] public float dent = 0.5f;                       // hand-sized struck impressions
        [Range(0.4f, 1f)] public float waviness = 0.5f;                   // broad normal undulation

        // --- light / reflection ---------------------------------------------------------------------
        [Range(0f, 360f)] public float lightAngle = 52f;
        [Range(0.2f, 0.95f)] public float envStrength = 0.5f;
        [Range(0.26f, 0.58f)] public float envContrast = 0.42f;
        [Range(0.15f, 1.1f)] public float specStrength = 0.5f;
        [Range(6, 90)] public int specSharp = 26;
        [Range(1.1f, 1.38f)] public float contrast = 1.25f;

        public int seed = 0;

        public override string DisplayName => "Steel";
        public override string Description =>
            "A flat, calmly-toned brushed/painted steel plate: broad tonal drift, isotropic finish (roughness "
            + "blurs the reflection rather than dimming it), hand-sized struck dents, and two crossed sets of "
            + "fine scratches (bright exposed-metal marks and dark shadowed grooves) over scattered corrosion "
            + "pits and specks. Full-canvas and fully opaque — stack a Shape generator above in Multiply to "
            + "clip it to a silhouette.";

        static readonly Vector2Int[] Dirs =
        {
            new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1),
            new Vector2Int(1, -1), new Vector2Int(2, 1), new Vector2Int(1, 2),
        };

        public override void Generate(in TapestryGenCtx ctx, Color32[] target)
        {
            int W = ctx.width, H = ctx.height;
            int N = W * H;
            int sd = seed + ctx.seed;
            int capHalf = Mathf.Max(1, Mathf.Min(W, H) / 2);

            // ---- pass 1: base noise fields -----------------------------------------------------
            var mottleF = new float[N];
            var gritRaw = new float[N];
            var microRaw = new float[N];
            var blotchF = new float[N];
            var wavRaw = new float[N];
            var dentBowl = new float[N];
            var dentLip = new float[N];
            var oxFieldRaw = new float[N];
            var crustRaw = new float[N];

            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    float U = (x + 0.5f) / W, V = (y + 0.5f) / H;

                    mottleF[i] = IsoFbm(U, V, 4, sd, 211, 3, -1);
                    gritRaw[i] = IsoFbm(U, V, 30, sd, 223, 2, capHalf);
                    microRaw[i] = IsoFbm(U, V, 16, sd, 11, 2, capHalf);
                    blotchF[i] = IsoFbm(U, V, 3, sd, 31, 4, -1) - 0.5f;
                    wavRaw[i] = IsoFbm(U, V, 3, sd, 151, 2, -1);

                    Dings(U, V, sd, 7, 0.22f, out float bowl, out float lip);
                    dentBowl[i] = bowl; dentLip[i] = lip;

                    float wxf = IsoFbm(U, V, 6, sd, 61, 2, -1) - 0.5f;
                    float wyf = IsoFbm(U, V, 6, sd, 67, 2, -1) - 0.5f;
                    float ou = Wrap01(U + wxf * 0.20f), ov = Wrap01(V + wyf * 0.20f);
                    float oxA = AnisoFbm(ou, ov, 4, 4, sd, 47, 4, -1, 0.5f);
                    float oxB = AnisoFbm(ou, ov, 11, 11, sd, 53, 3, -1, 0.5f);
                    oxFieldRaw[i] = 0.85f * oxA + 0.15f * oxB;

                    crustRaw[i] = IsoFbm(U, V, 26, sd, 59, 3, -1);
                }
            }

            Stretch(mottleF);
            var gritF = BoxBlur(gritRaw, W, H, 1); Stretch(gritF);
            var microF = BoxBlur(microRaw, W, H, 1);
            var wavF = BoxBlur(wavRaw, W, H, 3);
            var dentF = new float[N];
            for (int i = 0; i < N; i++) dentF[i] = dentLip[i] * 0.06f - dentBowl[i];
            dentF = BoxBlur(dentF, W, H, 2);
            Stretch(oxFieldRaw);
            var oxF = oxFieldRaw;
            Stretch(crustRaw);
            var crustF = new float[N];
            for (int i = 0; i < N; i++)
                crustF[i] = Mathf.Clamp01(crustRaw[i] * (0.45f + 0.85f * oxideCrust) + 0.35f * oxF[i] - 0.18f);

            // ---- roughness field ------------------------------------------------------------------
            var rough = new float[N];
            for (int i = 0; i < N; i++)
                rough[i] = Mathf.Clamp(finish + finishMottle * (mottleF[i] - 0.5f) * 1.6f
                                        + grit * (gritF[i] - 0.5f) * 1.3f, 0.02f, 1f);

            // ---- oxide mask -------------------------------------------------------------------------
            float oxEff = oxideAmount * oxideAmount * (3f - 2f * oxideAmount);
            float thr = 0.99f - 0.38f * oxEff;
            var oxideMask = new float[N];
            for (int i = 0; i < N; i++)
            {
                float core = Smoothstep(thr, thr + 0.035f, oxF[i]) * 0.90f;
                float halo = Smoothstep(thr - 0.22f, thr + 0.06f, oxF[i]) * 0.22f;
                oxideMask[i] = core + halo * (1f - core);
            }
            oxideMask = BoxBlur(oxideMask, W, H, 1);
            for (int i = 0; i < N; i++) oxideMask[i] = Mathf.Clamp01(oxideMask[i]);

            // ---- scratches: two crossed signed sets --------------------------------------------------
            var scratch = new float[N];
            var scratchDark = new float[N];
            int dirB = (scratchDir + 2) % Dirs.Length;
            int densB = Mathf.Max(2, Mathf.FloorToInt(scratchDensity * 0.8f));
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    float U = (x + 0.5f) / W, V = (y + 0.5f) / H;
                    float scrA = Scratches(U, V, scratchDir, scratchDensity, sd);
                    float scrB = Scratches(U, V, dirB, densB, sd + 4409);
                    float scr = (Mathf.Abs(scrA) >= Mathf.Abs(scrB * 0.60f)) ? scrA : scrB * 0.60f;
                    scr *= scratchAmount;
                    scratch[i] = Mathf.Clamp01(scr);
                    scratchDark[i] = Mathf.Clamp01(-scr);
                }
            }
            var scratchBlur = BoxBlur(scratch, W, H, 1);
            var scratchLip = new float[N];
            for (int i = 0; i < N; i++) scratchLip[i] = Mathf.Clamp01(scratchBlur[i] - scratch[i]) * 2f;
            var scrRelief = new float[N];
            for (int i = 0; i < N; i++) scrRelief[i] = Mathf.Clamp01(scratch[i] + scratchDark[i]);
            scrRelief = BoxBlur(scrRelief, W, H, 1);

            // ---- craters: pits + finer specks -----------------------------------------------------------
            var pit = new float[N];
            var speck = new float[N];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    float U = (x + 0.5f) / W, V = (y + 0.5f) / H;
                    float pitRaw = Craters(U, V, sd, 26, 0.06f + 1.5f * pitting);
                    pit[i] = Mathf.Clamp01(pitRaw * (0.09f + 0.91f * oxideMask[i]) * (0.35f + 2.2f * pitting));
                    float speckRaw = Craters(U, V, sd + 977, 40, 0.03f + 0.32f * pitting);
                    speck[i] = Mathf.Clamp01(speckRaw * (0.30f + 0.70f * pitting));
                }
            }
            var pitBlur = BoxBlur(pit, W, H, 1);
            var speckBlur = BoxBlur(speck, W, H, 1);

            var bare = new float[N];
            for (int i = 0; i < N; i++) bare[i] = Mathf.Clamp01(0.62f * scratch[i]);

            // ---- gradients (wrapped central difference) --------------------------------------------------
            var wvx = new float[N]; var wvy = new float[N]; Gradient(wavF, W, H, wvx, wvy);
            var dvx = new float[N]; var dvy = new float[N]; Gradient(dentF, W, H, dvx, dvy);
            var mgx = new float[N]; var mgy = new float[N]; Gradient(microF, W, H, mgx, mgy);
            var ggx = new float[N]; var ggy = new float[N]; Gradient(gritF, W, H, ggx, ggy);
            var sgx = new float[N]; var sgy = new float[N]; Gradient(scrRelief, W, H, sgx, sgy);
            var ptx = new float[N]; var pty = new float[N]; Gradient(pitBlur, W, H, ptx, pty);
            var spx = new float[N]; var spy = new float[N]; Gradient(speckBlur, W, H, spx, spy);

            // ---- normals: waviness + dent + micro-finish + grit + (crater/scratch grooves cut IN) ---------
            float wamp = 8f * waviness;
            float damp = 1.6f * dent;
            float gamp = 3f * grit;
            const float pampPit = 2.6f;
            const float sampSpk = 1.8f;
            const float sampScr = 1.1f;

            var nx = new float[N]; var ny = new float[N]; var nz = new float[N];
            for (int i = 0; i < N; i++)
            {
                float micro = 1.1f * rough[i] + 1.6f * scratch[i];
                float px = wvy[i] * wamp + dvy[i] * damp + mgy[i] * micro + ggy[i] * gamp
                           - pty[i] * pampPit - spy[i] * sampSpk - sgy[i] * sampScr;
                float py = wvx[i] * wamp + dvx[i] * damp + mgx[i] * micro + ggx[i] * gamp
                           - ptx[i] * pampPit - spx[i] * sampSpk - sgx[i] * sampScr;
                float nzv = 1f / Mathf.Sqrt(px * px + py * py + 1f);
                nx[i] = -px * nzv; ny[i] = -py * nzv; nz[i] = nzv;
            }

            // ---- light + half vector --------------------------------------------------------------------
            float ang = lightAngle * Mathf.Deg2Rad;
            float lx = Mathf.Cos(ang) * 0.80f, ly = Mathf.Sin(ang) * 0.80f, lz = 0.60f;
            float ln = 1f / Mathf.Sqrt(lx * lx + ly * ly + lz * lz);
            lx *= ln; ly *= ln; lz *= ln;
            float hx = lx, hy = ly, hz = lz + 1f;
            float hn = 1f / Mathf.Sqrt(hx * hx + hy * hy + hz * hz);
            hx *= hn; hy *= hn; hz *= hn;

            // ---- the metal's own reflection ------------------------------------------------------------
            var env = new float[N];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    float U = (x + 0.5f) / W, V = (y + 0.5f) / H;
                    env[i] = Environment(nx[i], ny[i], nz[i], U, V, sd, 7, envContrast, lx, ly,
                        0.30f, 131, 0.10f, 1.0f, 0.34f, 4, 0.16f);
                }
            }
            var roughScaled = new float[N];
            for (int i = 0; i < N; i++) roughScaled[i] = 0.95f * rough[i];
            env = Roughen(env, roughScaled, W, H, 16);

            // ---- final compositing -----------------------------------------------------------------------
            var lumaW = new Vector3(0.2126f, 0.7152f, 0.0722f);
            Color oxideCore = new Color(oxideColor.r * 0.60f, oxideColor.g * 0.60f, oxideColor.b * 0.60f, 1f);
            Color oxideBloom = Color.Lerp(oxideColor, Color.white, 0.45f);
            Color metalTint55 = new Color(0.55f + 0.45f * metalColor.r, 0.55f + 0.45f * metalColor.g, 0.55f + 0.45f * metalColor.b, 1f);

            var lit = new Color[N];
            for (int i = 0; i < N; i++)
            {
                float ndl = Mathf.Clamp01(nx[i] * lx + ny[i] * ly + nz[i] * lz);
                float ndh = Mathf.Clamp01(nx[i] * hx + ny[i] * hy + nz[i] * hz);

                float gloss = Mathf.Clamp(0.35f + 0.75f * bare[i] - 0.55f * oxideMask[i]
                                           - 0.6f * pit[i] - 0.45f * scratchDark[i] - 0.5f * speck[i], 0.05f, 1f);
                float gritMod = 1f + grit * (gritF[i] - 0.5f) * 0.85f;
                gloss *= (0.62f + 0.38f * (1f - rough[i])) * gritMod;

                float sharpEff = specSharp * (0.10f + 0.90f * Mathf.Pow(1f - rough[i], 1.6f)) + 2f;
                float spec = Mathf.Pow(ndh, sharpEff) * specStrength * (0.45f + 0.55f * (1f - rough[i])) * gloss;

                float fres = 0.05f + 0.95f * Mathf.Pow(Mathf.Clamp01(1f - nz[i]), 3f);
                float refl = env[i] * envStrength * gloss * (0.30f + 0.70f * fres);

                // -- albedo --
                float blotchMul = 1f + blotchF[i] * 2f * tonalVariance;
                Color alb = new Color(baseColor.r * blotchMul, baseColor.g * blotchMul, baseColor.b * blotchMul, 1f);

                Color oxCol = Color.Lerp(oxideCore, oxideBloom, crustF[i]);
                float oxStrong = Smoothstep(0.06f, 0.45f, oxideMask[i]);
                float oxLuma = oxCol.r * lumaW.x + oxCol.g * lumaW.y + oxCol.b * lumaW.z;
                oxCol = new Color(oxLuma + (oxCol.r - oxLuma) * (0.25f + 0.75f * oxStrong),
                                   oxLuma + (oxCol.g - oxLuma) * (0.25f + 0.75f * oxStrong),
                                   oxLuma + (oxCol.b - oxLuma) * (0.25f + 0.75f * oxStrong), 1f);
                alb = Color.Lerp(alb, oxCol, oxideMask[i]);

                Color metalCol = metalColor * (0.80f + 0.30f * oxF[i]);
                alb = Color.Lerp(alb, metalCol, bare[i]);

                float scratchDarkMul = 1f - Mathf.Clamp(scratchDark[i] * 0.55f, 0f, 0.55f);
                float scratchLipMul = 1f - Mathf.Clamp(scratchLip[i] * 0.45f, 0f, 0.45f);
                alb.r *= scratchDarkMul * scratchLipMul; alb.g *= scratchDarkMul * scratchLipMul; alb.b *= scratchDarkMul * scratchLipMul;
                float pitMul = 1f - pit[i] * 0.6f, speckMul = 1f - speck[i] * 0.72f;
                alb.r *= pitMul * speckMul; alb.g *= pitMul * speckMul; alb.b *= pitMul * speckMul;
                alb.r = Mathf.Clamp01(alb.r); alb.g = Mathf.Clamp01(alb.g); alb.b = Mathf.Clamp01(alb.b);

                float diffuse = 0.40f + 0.68f * ndl;
                float rr = alb.r * diffuse + refl * (0.40f + 0.60f * alb.r) + spec * metalTint55.r;
                float gg = alb.g * diffuse + refl * (0.40f + 0.60f * alb.g) + spec * metalTint55.g;
                float bb = alb.b * diffuse + refl * (0.40f + 0.60f * alb.b) + spec * metalTint55.b;
                lit[i] = new Color(rr, gg, bb, 1f);
            }

            // ---- clarity: wrapped unsharp on luma only ---------------------------------------------------
            var luma = new float[N];
            for (int i = 0; i < N; i++) luma[i] = lit[i].r * lumaW.x + lit[i].g * lumaW.y + lit[i].b * lumaW.z;
            var lumaLo = BoxBlur(luma, W, H, 2);
            const float clarity = 0.62f;
            for (int i = 0; i < N; i++)
            {
                float mul = 1f + clarity * (luma[i] - lumaLo[i]) / Mathf.Max(lumaLo[i], 0.06f);
                lit[i] = new Color(Mathf.Max(0f, lit[i].r * mul), Mathf.Max(0f, lit[i].g * mul), Mathf.Max(0f, lit[i].b * mul), 1f);
            }

            // ---- contrast + soft knee, then write out (opaque everywhere) -------------------------------
            const float knee = 0.85f;
            for (int i = 0; i < N; i++)
            {
                Color c = lit[i];
                c.r = (c.r - 0.42f) * contrast + 0.42f;
                c.g = (c.g - 0.42f) * contrast + 0.42f;
                c.b = (c.b - 0.42f) * contrast + 0.42f;
                c.r /= 1f + Mathf.Max(0f, c.r - knee) * 1.6f;
                c.g /= 1f + Mathf.Max(0f, c.g - knee) * 1.6f;
                c.b /= 1f + Mathf.Max(0f, c.b - knee) * 1.6f;
                c.r = Mathf.Clamp01(c.r); c.g = Mathf.Clamp01(c.g); c.b = Mathf.Clamp01(c.b);
                c.a = 1f;   // a Surface blankets the whole field it is handed, opaquely
                target[i] = c;
            }
        }

        // ------------------------------------------------------------------ tileable primitives
        static int Wrap(int v, int n) => ((v % n) + n) % n;
        static float Wrap01(float x) => x - Mathf.Floor(x);

        // Same deterministic per-cell hash TapestryPanelsGenerator uses — a wrapped cell index gets the
        // SAME value as its "home" cell, which is what keeps every field here seamless across the tile edge.
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

        static float Smoothstep(float e0, float e1, float x)
        {
            float d = e1 - e0;
            if (Mathf.Abs(d) < 1e-6f) d = d >= 0f ? 1e-6f : -1e-6f;
            float t = Mathf.Clamp01((x - e0) / d);
            return t * t * (3f - 2f * t);
        }

        static void Stretch(float[] f)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < f.Length; i++) { if (f[i] < lo) lo = f[i]; if (f[i] > hi) hi = f[i]; }
            float span = hi - lo; if (span <= 1e-6f) span = 1f;
            for (int i = 0; i < f.Length; i++) f[i] = (f[i] - lo) / span;
        }

        // Wrapped separable box blur — wrapped because the field IS the whole tileable canvas.
        static float[] BoxBlur(float[] src, int W, int H, int r)
        {
            if (r < 1) return src;
            int k = 2 * r + 1;
            var tmp = new float[W * H];
            for (int y = 0; y < H; y++)
            {
                int row = y * W;
                for (int x = 0; x < W; x++)
                {
                    float sum = 0f;
                    for (int t = -r; t <= r; t++) sum += src[row + Wrap(x + t, W)];
                    tmp[row + x] = sum / k;
                }
            }
            var outArr = new float[W * H];
            for (int x = 0; x < W; x++)
            {
                for (int y = 0; y < H; y++)
                {
                    float sum = 0f;
                    for (int t = -r; t <= r; t++) sum += tmp[Wrap(y + t, H) * W + x];
                    outArr[y * W + x] = sum / k;
                }
            }
            return outArr;
        }

        // Wrapped central-difference gradient of a whole-canvas field.
        static void Gradient(float[] f, int W, int H, float[] gx, float[] gy)
        {
            for (int y = 0; y < H; y++)
            {
                int row = y * W;
                int rowUp = Wrap(y - 1, H) * W, rowDown = Wrap(y + 1, H) * W;
                for (int x = 0; x < W; x++)
                {
                    int i = row + x;
                    int xl = Wrap(x - 1, W), xr = Wrap(x + 1, W);
                    gx[i] = (f[row + xr] - f[row + xl]) * 0.5f;
                    gy[i] = (f[rowDown + x] - f[rowUp + x]) * 0.5f;
                }
            }
        }

        // Wrapped bilinear/smoothstep value noise at an arbitrary continuous (pu,pv), with independent
        // cell counts per axis — the domain-warp and lattice-scratch callers need that independence.
        static float ValueNoise2D(float pu, float pv, int cu, int cv, int seed, int salt)
        {
            cu = Mathf.Max(1, cu); cv = Mathf.Max(1, cv);
            float gu = pu * cu, gv = pv * cv;
            int u0raw = Mathf.FloorToInt(gu), v0raw = Mathf.FloorToInt(gv);
            int u0 = Wrap(u0raw, cu), v0 = Wrap(v0raw, cv);
            int u1 = Wrap(u0 + 1, cu), v1 = Wrap(v0 + 1, cv);
            float tu = gu - u0raw, tv = gv - v0raw;
            float su = tu * tu * (3f - 2f * tu), sv = tv * tv * (3f - 2f * tv);
            float n00 = HashCell(u0, v0, seed, salt), n10 = HashCell(u1, v0, seed, salt);
            float n01 = HashCell(u0, v1, seed, salt), n11 = HashCell(u1, v1, seed, salt);
            float top = n00 * (1f - su) + n10 * su, bot = n01 * (1f - su) + n11 * su;
            return top * (1f - sv) + bot * sv;
        }

        // fBm at doubling frequencies. maxCells>0 drops octaves finer than the canvas can resolve
        // (pass -1 for no cap). persistence 0.5 is standard; the environment uses it explicitly too, but
        // gen 8's higher-persistence variant for a "detailed reflected world" isn't needed once the coat/
        // wet-vs-dry environment split is gone, so every caller here uses the plain 0.5 value.
        static float AnisoFbm(float pu, float pv, int cu, int cv, int seed, int salt, int octaves, int maxCells, float persistence)
        {
            float total = 0f, amp = 1f, asum = 0f;
            for (int o = 0; o < octaves; o++)
            {
                int cU = cu << o, cV = cv << o;
                if (maxCells > 0 && cU > maxCells && o > 0) break;
                total += ValueNoise2D(pu, pv, cU, cV, seed, salt + o * 17) * amp;
                asum += amp;
                amp *= persistence;
            }
            return total / Mathf.Max(asum, 1e-9f);
        }

        static float IsoFbm(float u, float v, int cells, int seed, int salt, int octaves, int maxCells)
            => AnisoFbm(u, v, cells, cells, seed, salt, octaves, maxCells, 0.5f);

        // Wrapped lattice of small, jittered, hard-edged round pits — checks the 3x3 wrapped neighbourhood
        // so jitter can span a whole cell and a pit can overlap into the next one, which is what keeps a
        // sparse lattice from reading as a visible regular grid of dots.
        static float Craters(float u, float v, int seed, int cells, float prob)
        {
            float gx = u * cells, gy = v * cells;
            int cxRaw = Mathf.FloorToInt(gx), cyRaw = Mathf.FloorToInt(gy);
            float fx = gx - cxRaw, fy = gy - cyRaw;
            int cx = Wrap(cxRaw, cells), cy = Wrap(cyRaw, cells);
            float best = 0f;
            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    int ncx = Wrap(cx + ox, cells), ncy = Wrap(cy + oy, cells);
                    float jx = HashCell(ncx, ncy, seed, 311);
                    float jy = HashCell(ncx, ncy, seed, 313);
                    float exists = HashCell(ncx, ncy, seed, 317) < prob ? 1f : 0f;
                    float rad = 0.13f + 0.17f * HashCell(ncx, ncy, seed, 319);
                    float ddx = fx - (ox + jx), ddy = fy - (oy + jy);
                    float d = Mathf.Sqrt(ddx * ddx + ddy * ddy);
                    float lo = 0.62f * rad;
                    float t = Mathf.Clamp01((d - lo) / Mathf.Max(rad - lo, 1e-6f));
                    float val = (1f - t * t * (3f - 2f * t)) * exists;
                    if (val > best) best = val;
                }
            }
            return best;
        }

        // Wrapped lattice of discrete, jittered, elliptical struck impressions with a ploughed lip ring —
        // same 3x3-neighbourhood technique as Craters, for the same reason.
        static void Dings(float u, float v, int seed, int cells, float prob, out float bowl, out float lip)
        {
            float gx = u * cells, gy = v * cells;
            int cxRaw = Mathf.FloorToInt(gx), cyRaw = Mathf.FloorToInt(gy);
            float fx = gx - cxRaw, fy = gy - cyRaw;
            int cx = Wrap(cxRaw, cells), cy = Wrap(cyRaw, cells);
            bowl = 0f; lip = 0f;
            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    int ncx = Wrap(cx + ox, cells), ncy = Wrap(cy + oy, cells);
                    float jx = HashCell(ncx, ncy, seed, 401);
                    float jy = HashCell(ncx, ncy, seed, 409);
                    float exists = HashCell(ncx, ncy, seed, 419) < prob ? 1f : 0f;
                    float rad = 0.26f + 0.44f * HashCell(ncx, ncy, seed, 421);
                    float a = HashCell(ncx, ncy, seed, 431) * Mathf.PI;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    float ecc = 0.42f + 0.58f * HashCell(ncx, ncy, seed, 433);
                    float ddx = fx - (ox + jx), ddy = fy - (oy + jy);
                    float rxp = ddx * ca + ddy * sa;
                    float ryp = (-ddx * sa + ddy * ca) / ecc;
                    float d = Mathf.Sqrt(rxp * rxp + ryp * ryp) / rad;
                    float t = Mathf.Clamp01(d);
                    float bowlVal = Mathf.Pow(1f - t * t, 1.5f) * exists;
                    if (bowlVal > bowl) bowl = bowlVal;
                    float ring = Mathf.Exp(-Mathf.Pow((d - 1.06f) / 0.16f, 2f));
                    float lipVal = ring * exists;
                    if (lipVal > lip) lip = lipVal;
                }
            }
        }

        // Thin, sparse, broken, SIGNED lines along one integer lattice direction (see Dirs — the (a,b)
        // components are integers, so shifting a whole tile changes the rotated (lu,lv) frame by whole
        // numbers and it stays exactly periodic). A per-band hash decides existence/position/width/polarity;
        // a slow wrapped noise displaces the mark along its own length so it reads as a scrape, not a ruled
        // line, and another cuts it into segments. Positive = bright exposed metal, negative = a dark groove.
        static float Scratches(float U, float V, int dir, int density, int seed)
        {
            Vector2Int d = Dirs[Wrap(dir, Dirs.Length)];
            float a = d.x, b = d.y;
            float lu = Wrap01(a * U + b * V);
            float lv = Wrap01(-b * U + a * V);

            int nb = Mathf.Max(2, density);
            float gu = lu * nb;
            int bandRaw = Mathf.FloorToInt(gu);
            int band = Wrap(bandRaw, nb);
            float frac = gu - bandRaw;

            float exists = HashCell(band, 0, seed, 701) < 0.30f ? 1f : 0f;
            float pos = HashCell(band, 1, seed, 702);
            float bright = 0.28f + 0.46f * HashCell(band, 3, seed, 704);
            float sign = HashCell(band, 4, seed, 706) < 0.45f ? -1f : 1f;

            pos += 1.0f * (ValueNoise2D(lv, lu, 4, nb, seed, 708) - 0.5f);
            float dd = Mathf.Abs(frac - pos);
            dd = Mathf.Min(dd, 1f - dd) / nb;
            float hwid = HashCell(band, 5, seed, 707);
            float wid = 0.0085f + 0.007f * hwid * hwid;
            float line = 1f - Smoothstep(0.35f, 1.0f, dd / wid);
            float segs = Smoothstep(0.30f, 0.44f, ValueNoise2D(lv, lu, 4, nb, seed, 705));
            return line * exists * bright * segs * sign;
        }

        // What a normal-mapped point reflects: a wrapped low-frequency noise field DISPLACED by the
        // reflected direction's xy, blended with a broad light-bearing horizon gradient (world_w). A
        // fraction (hard) of the noise is replaced by a two-level posterisation of itself so the reflected
        // "room" has occasional bright shapes in it rather than being fog at every frequency.
        static float Environment(float nxv, float nyv, float nzv, float U, float V, int seed, int cells,
            float contrast, float lx, float ly, float disp, int salt, float floor, float hgain, float worldW,
            int octaves, float hard)
        {
            float rx = 2f * nzv * nxv, ry = 2f * nzv * nyv;
            float eu = Wrap01(U + rx * disp), ev = Wrap01(V + ry * disp);
            float world = AnisoFbm(eu, ev, cells, cells, seed, salt, octaves, 64, 0.50f);
            if (hard > 0f)
            {
                float shapes = Smoothstep(0.60f, 0.68f, world);
                world = world * (1f - hard) + (0.34f + 0.60f * shapes) * hard;
            }
            world = (world - 0.5f) * (1.7f * contrast) + 0.5f;
            float horizon = 0.5f + 0.5f * Mathf.Clamp((rx * lx + ry * ly) * hgain / 1.15f, -1f, 1f);
            return Mathf.Clamp(worldW * world + (1f - worldW) * horizon, floor, 1f);
        }

        // Blur a reflection field by roughness, as a two-stage pyramid (near tap ~radius/4, far tap
        // radius) rather than one lerp straight to a wide blur — that is what keeps a polished-to-satin
        // draw sharp and only lets a genuinely blasted one reach the wide, defocused cone.
        static float[] Roughen(float[] env, float[] roughScaled, int W, int H, int radius)
        {
            var near = BoxBlur(env, W, H, Mathf.Max(1, Mathf.RoundToInt(radius / 4f)));
            var far = BoxBlur(env, W, H, radius);
            var outArr = new float[env.Length];
            for (int i = 0; i < env.Length; i++)
            {
                float t = Mathf.Clamp01(roughScaled[i]);
                float a = Mathf.Clamp01(t * 2f);
                float bl = Mathf.Clamp01(t * 2f - 1f);
                float mid = env[i] * (1f - a) + near[i] * a;
                outArr[i] = mid * (1f - bl) + far[i] * bl;
            }
            return outArr;
        }
    }
}
