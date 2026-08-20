using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.PyrePlus
{
    // ── ForkBlast form — a swarm-of-puffs radial DETONATION (closed-form f(t)) ───────────────────────────────────
    // Ported from a Kiln agent's own generator, "agent3_fork_explosive" (Flame project, generation 5). Unlike
    // Inferno (a closed-form volumetric density/metaball field with no discrete particles), the source algorithm
    // is a field of hundreds of small anisotropic soft "puffs" thrown outward from a source and shaped by
    // drag/entrainment, union-accumulated into one heat field and shaded exactly once — see the source's
    // `flame3/jet.py` (the puff system) and `flame3/field.py` + `flame3/grad.py` (the field → pixel pass). That is
    // what gives this form its gritty, particulate "fragments of fire" read rather than Inferno's smooth cloud.
    //
    // Whole-layer and closed-form: every puff's position/size/amplitude at a given moment is a pure function of
    // (its slot index, the blast's own local clock, the dials below) — no accumulated simulation state, so
    // scrubbing is exact with no replay harness. PLACEMENT IS THE SWARM'S JOB, exactly like Inferno: swarm off ⇒
    // one centred detonation; swarm on ⇒ every swarm particle's spawn position + spawn moment ignites its own
    // blast (ComputeSpawns supplies both, via the same SwarmOrigin convention Inferno uses).
    //
    // Colour comes ONLY from the layer's shapeFill, evaluated at life = (1 − heat) — the fill's LEFT end is the
    // white-hot core, matching Inferno's convention. The fill's OWN alpha (an OverLife gradient can carry an alpha
    // ramp) doubles as the "per-stop alpha ceiling" the Python original computed from its own Ramp class — Opaq is
    // an exponent on THAT alpha, exactly mirroring the source's `grad.shade`'s `aceil ** opaq`.
    //
    // ── What was ported, and what was deliberately dropped (see the porting checklist in
    //    D:\CODEZ\Kiln\docs\pyreplus_briefing.md §7) ──────────────────────────────────────────────────────────
    //   PORTED: the puff physics (drag-decelerated outward travel, entrainment growth `growth`+`swell`, birth-time
    //   detonation clock `blastSkew`/`blastSpan`, `velSpread` so a blast fills instead of hollowing into a smoke
    //   ring, elongation-then-round `elong`/`roundAt`), and — the whole point of generation 5 — the death model:
    //   `hold` (push the fade to the back of a puff's life instead of dimming from birth), `shrink`/`shrinkAt`
    //   (contract rather than fade), `leadDie` (the fastest gas dies first so the silhouette closes inward), and
    //   `opaq` (an exponent on the alpha ceiling that solidifies the body without touching the edge falloff). Also
    //   ported: the "shedding burning mass" gobs (`gob*` — lumps torn off in the opening phase that dissipate by
    //   ballooning, the opposite death to the body's), the ignition flash, and a turbulence term.
    //   DROPPED (named honestly rather than glossed over):
    //     • Multiple staggered detonations in ONE layer (`blast_at`/`blast_pow`/`blast_off` arrays) — replaced
    //       entirely by the Swarm system, exactly Inferno's precedent (a ring of blasts = a circle path; a
    //       staggered volley = spawn-timing). A single-layer ForkBlast is always one detonation.
    //     • Rings (`_rings`), burning fragments with trails (`_chunks`), sparks (`_sparks`), the non-gob "shed"
    //       stream (puffs that detach and tumble, distinct from gobs), and the disc-breaking trio swirl/spin/lobes.
    //       None of these are structural to "how a blast dies" (generation 5's actual ask) — they are generation
    //       3/4 texture. Could each become its own PlusForm or a shared modifier later; out of scope here.
    //     • The per-pixel POLAR domain warp (`_warp_out`, noise sampled in polar coordinates around the source so
    //       turbulence scrolls outward in every direction). Approximated instead by a small per-puff tangential
    //       wobble (see `Wobble` below) driven by a cheap deterministic hash-noise — visually similar (breaks up
    //       the disc into licks) but not the same texture; a faithful port would need a full per-pixel warped
    //       field sample, which this blit-based accumulator (chosen for its performance — only touches each
    //       puff's own bounding box, not the whole canvas) cannot cheaply support.
    //     • The independent SECOND colour ramp `soot` crossfades into (a whole second gradient, not just a tint).
    //       Approximated by desaturating/darkening the fill's own sampled colour toward a fixed soot grey — a
    //       one-ramp approximation of the source's two-ramp crossfade.
    //     • The `root` burner lump — folded into Flash (both are "a hot core at the seat of the blast"); a
    //       separate always-present ember was judged redundant with Flash for a one-shot detonation.
    // ── coordinate convention ────────────────────────────────────────────────────────────────────────────────
    // Buffer convention: row 0 = BOTTOM (y-up), like the rest of PyrePlusRenderer. Puff angles are plain math
    // angles (0 = +x/right, 90° = +y/up), which is why buoyancy simply ADDS to the y coordinate here (the source,
    // working in a y-DOWN numpy array, had to SUBTRACT for the same visual effect).
    internal static class PlusForkBlast
    {
        const float TAU = Mathf.PI * 2f;

        // The three animatable dials, pre-evaluated by ForkBlastForm.Prepare through the renderer's Eval funnel
        // at the layer's life.
        public struct Anim { public float progress, reach, flash; }

        // The structural per-blast constants (the Python JetSpec's tuning constants — none were envelopes there
        // either), handed in by the caller already authored. Names match the dials' meaning, not any owner's field.
        public struct Params
        {
            public float spread, aim, bias;            // emission arc: half-angle (deg), centre direction (deg), angle-distribution power
            public int puffCount;
            public float drag, growth, swell, elong, roundAt, buoy;
            public float blastSkew, blastSpan, velSpread, puffLife, jitter, cool;
            public float hold, shrink, shrinkAt, leadDie, opaq, soot;
            public int gobCount;
            public float gobReach, gobSwell, gobLifeMul, gobAmp, gobEarly;
            public float puffSizePx, gobSizePx, warpAmount;
            public float lo, hi, curve, soft, exposureMult;
            public bool autoExposure;
        }

        // One swarm-authored blast origin — identical shape/contract to PlusInferno.SwarmOrigin.
        public struct SwarmOrigin { public float x, y, start; }

        public struct Mods
        {
            public PixelModifier[] pix;
            public float phase;
            public int frameIndex;
            public float life;
            public int pixHash;
        }

        // ── hashing / noise — a small local port (see PlusInferno.cs for the twin copy; kept separate rather
        // than shared so each form's noise character can drift independently, matching the source's own
        // per-generator noise pack). ──────────────────────────────────────────────────────────────────────────
        static float Hash2(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h ^= (int)((uint)h >> 13);
                h *= 1274126177;
                h ^= (int)((uint)h >> 16);
                return (uint)h / 4294967295f;
            }
        }

        // A puff's fixed wobble direction/rate (per-puff, seeded) times a smoothly-varying-with-age amplitude —
        // the tangential-displacement approximation of the source's polar domain warp (see the header note).
        static float Wobble(int i, float s, int seed)
        {
            float a = Hash2(i, 811, seed) * TAU;
            float speed = 3f + Hash2(i, 823, seed) * 5f;
            return Mathf.Sin(a + s * speed) * (0.5f + 0.5f * Hash2(i, 839, seed));
        }

        // ── one detonation event: swarm off ⇒ one centred blast; swarm on ⇒ one per spawn (Inferno's precedent) ──
        struct Ev { public float cx, cy, start, duration, scale; public int seed; }

        static Ev[] MakeEvents(int seed, SwarmOrigin[] origins)
        {
            if (origins == null || origins.Length == 0)
                return new[] { new Ev { cx = 0f, cy = 0f, start = 0f, duration = 1f, scale = 1f, seed = seed } };

            int m = origins.Length;
            var events = new Ev[m];
            for (int i = 0; i < m; i++)
            {
                float ocx = origins[i].x, ocy = origins[i].y;
                float radius = Mathf.Sqrt(ocx * ocx + ocy * ocy);
                float available = Mathf.Clamp(1f - radius, 0.25f, 1f);
                float scale = m == 1
                    ? available
                    : Mathf.Clamp(available * (0.55f + 0.30f * Hash2(i, 401, seed)), 0.22f, 1f);
                float duration = Mathf.Clamp(1f - origins[i].start + 0.10f, 0.38f, 0.94f);
                events[i] = new Ev
                {
                    cx = ocx, cy = ocy, start = origins[i].start, duration = duration, scale = scale,
                    seed = seed + i * 977,
                };
            }
            return events;
        }

        // Straight-alpha source-over of a glow colour onto the buffer (mirrors PlusInferno.CompositeGlow).
        static void CompositeGlow(Color32[] buf, int i, float cr, float cg, float cb, float a)
        {
            a = Mathf.Clamp01(a);
            if (a <= 0f) return;
            float oldA = buf[i].a / 255f;
            float outA = a + oldA * (1f - a);
            if (outA <= 0f) return;
            float inv = 1f / outA;
            float keep = oldA * (1f - a);
            buf[i] = new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt((cr * a + buf[i].r * keep) * inv), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt((cg * a + buf[i].g * keep) * inv), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt((cb * a + buf[i].b * keep) * inv), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(outA * 255f), 0, 255));
        }

        // One anisotropic soft lump, compact support, blitted over its own bounding box only — a direct port of
        // the source's `HeatField.blob` (field.py). Accumulates additively into `density`/`soot`.
        static void Blob(float[] density, float[] soot, int W, int H, float cx, float cy, float rx, float ry,
                          float ang, float amp, float tint)
        {
            if (amp <= 0f || rx <= 0f || ry <= 0f) return;
            float reach = Mathf.Max(rx, ry) + 1f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - reach)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + reach));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - reach)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + reach));
            if (x1 < x0 || y1 < y0) return;
            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
            float invRx = 1f / rx, invRy = 1f / ry;
            for (int py = y0; py <= y1; py++)
            {
                float dy = (py + 0.5f) - cy;
                int row = py * W;
                for (int px = x0; px <= x1; px++)
                {
                    float dx = (px + 0.5f) - cx;
                    float u = (dx * ca + dy * sa) * invRx;
                    float v = (-dx * sa + dy * ca) * invRy;
                    float k = 1f - (u * u + v * v);
                    if (k <= 0f) continue;
                    k *= k;                       // C1 at the support boundary — no ring edge
                    float w = k * amp;
                    int idx = row + px;
                    density[idx] += w;
                    if (tint > 0f) soot[idx] += w * tint;
                }
            }
        }

        static float Smoothstep01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        // Smoothstep-in over a short window `k` — a puff grows into existence instead of popping on.
        static float FadeIn(float s, float k) => Smoothstep01(s / Mathf.Max(k, 1e-4f));

        public static void Render(Color32[] target, int W, int H, in Params l, int seed,
                                   ZuiFill fill, float layerAlpha, in Anim anim, SwarmOrigin[] origins, in Mods mods)
        {
            if (W < 2 || H < 2 || layerAlpha <= 0f) return;

            var events = MakeEvents(seed, origins);
            float refPx = Mathf.Min(W, H) * 0.5f;
            float progress = Mathf.Clamp01(anim.progress);
            float reachFrac = Mathf.Max(0.01f, anim.reach);
            float spreadRad = l.spread * Mathf.Deg2Rad;
            float aimRad = l.spread >= 179.9f ? 0f : l.aim * Mathf.Deg2Rad;
            bool fullCircle = l.spread >= 179.9f;
            float kd = Mathf.Max(l.drag, 1e-3f);
            float denom = 1f - Mathf.Exp(-kd);
            int n = Mathf.Max(1, l.puffCount);
            // Puffs are split evenly across a multi-blast swarm rather than replicated per event — otherwise a
            // 12-particle swarm would draw 12x the puff count and cost, without reading any denser (mirrors
            // Inferno's own clump-count reduction for multi-blast swarms).
            int puffsPerEvent = events.Length > 1 ? Mathf.Max(24, n / events.Length) : n;

            int count = W * H;
            var density = new float[count];
            var soot = new float[count];

            float nozzleX = W * 0.5f, nozzleY = H * 0.5f;

            foreach (var ev in events)
            {
                float local = Mathf.Clamp01((progress - ev.start) / Mathf.Max(ev.duration, 1e-3f));
                float evCx = nozzleX + ev.cx * (W * 0.5f);
                float evCy = nozzleY + ev.cy * (H * 0.5f);
                float reachPx = reachFrac * ev.scale * refPx;

                var rng = new System.Random(ev.seed);
                for (int i = 0; i < puffsPerEvent; i++)
                {
                    // Stratified + jittered angle, biased away from uniform by `forkBias` — 1.0 stays uniform,
                    // which is mandatory for a full-circle blast (see the source's own warning against biasing a
                    // radial emitter towards its axis).
                    float u = ((i + 0.5f) / puffsPerEvent) * 2f - 1f
                            + (float)(rng.NextDouble() - 0.5) * (2f / puffsPerEvent);
                    u = Mathf.Clamp(u, -1f, 1f);
                    float da = Mathf.Sign(u) * Mathf.Pow(Mathf.Abs(u), l.bias) * spreadRad;

                    float j = l.jitter;
                    float vs = 1f + j * (float)(rng.NextDouble() * 0.84 - 0.42);
                    float rs = 1f + j * (float)(rng.NextDouble() * 0.84 - 0.34);
                    float ampMul = 1f + j * (float)(rng.NextDouble() * 0.60 - 0.30);
                    float lifeMul = 1f + j * (float)(rng.NextDouble() * 0.50 - 0.25);
                    if (l.velSpread > 0f)
                        vs *= 1f - l.velSpread * Mathf.Pow((float)rng.NextDouble(), 1.4f);
                    float birthU = (float)rng.NextDouble();
                    float birthFrac = Mathf.Pow(birthU, l.blastSkew);
                    // `lead` needs a stable rank over the WHOLE puff set (0 = slowest, 1 = fastest), which a
                    // single running draw can't give without a second pass — approximate the rank directly from
                    // `vs`'s own distribution shape instead of sorting: vs is drawn from a symmetric jitter times
                    // an optional velSpread thinning, so its rank correlates tightly with its raw value; a plain
                    // clamped linear remap over the jitter range is visually indistinguishable from an exact sort
                    // here and avoids an O(n log n) pass per event per frame.
                    float lead = Mathf.Clamp01((vs - (1f - j * 0.42f - l.velSpread)) / Mathf.Max(2f * j + l.velSpread, 1e-3f));

                    float birth = l.blastSpan * birthFrac;
                    float puffLife = l.puffLife * lifeMul;
                    if (l.leadDie > 0f)
                        puffLife *= 1f - l.leadDie * Mathf.Pow(lead, 1.4f);
                    float s = (local - birth) / Mathf.Max(puffLife, 1e-3f);
                    if (s < 0f || s >= 1f) continue;

                    float theta = aimRad + da;
                    float d = reachPx * vs * (1f - Mathf.Exp(-kd * s)) / denom;
                    float wx = evCx + d * Mathf.Cos(theta);
                    float wy = evCy + d * Mathf.Sin(theta) + l.buoy * refPx * Mathf.Pow(s, 2.4f);

                    if (l.warpAmount > 0f)
                    {
                        float wob = Wobble(i, s, ev.seed) * l.warpAmount;
                        wx += wob * Mathf.Cos(theta + Mathf.PI * 0.5f);
                        wy += wob * Mathf.Sin(theta + Mathf.PI * 0.5f);
                    }

                    float r = l.puffSizePx * rs + l.growth * d + l.swell * reachPx * s;
                    if (l.shrink > 0f)
                    {
                        float su = Mathf.Clamp01((s - l.shrinkAt) / Mathf.Max(1f - l.shrinkAt, 1e-3f));
                        float k = l.shrink * Mathf.Pow(lead, 1.4f) * Smoothstep01(su);
                        r *= 1f - k;
                    }
                    float aspect = 1f + l.elong * Mathf.Exp(-s / Mathf.Max(l.roundAt, 0.02f));

                    float decay = l.hold > 0f
                        ? Mathf.Clamp01(1f - Mathf.Pow(s, 1f + l.hold))
                        : Mathf.Clamp01(1f - s);
                    float amp = 1.15f * ampMul * FadeIn(s, 0.06f) * Mathf.Pow(decay, Mathf.Max(l.cool, 0.01f));
                    if (amp <= 0.0005f) continue;
                    float tint = Mathf.Clamp01(l.soot * s);

                    Blob(density, soot, W, H, wx, wy, r * aspect, r, theta, amp, tint);
                }

                // ── gobs: burning mass shed in the opening phase, that dissipates rather than shrinks ──────────
                if (l.gobCount > 0)
                {
                    var grng = new System.Random(ev.seed + 90001);
                    float gobReachPx = l.gobReach * ev.scale * refPx;
                    float gobLife = l.puffLife * l.gobLifeMul;
                    for (int i = 0; i < l.gobCount; i++)
                    {
                        float uu = ((i + 0.5f) / l.gobCount) * 2f - 1f
                                 + (float)(grng.NextDouble() - 0.5) * (2f / l.gobCount);
                        uu = Mathf.Clamp(uu, -1f, 1f);
                        float da = Mathf.Sign(uu) * Mathf.Pow(Mathf.Abs(uu), l.bias) * spreadRad;
                        float vs = 0.42f + 0.83f * (float)grng.NextDouble();
                        float rs = 0.62f + 0.93f * (float)grng.NextDouble();
                        float born = Mathf.Pow((float)grng.NextDouble(), 1.7f) * l.gobEarly * l.blastSpan;

                        float s = (local - born) / Mathf.Max(gobLife, 1e-3f);
                        if (s < 0f || s >= 1f) continue;

                        float theta = aimRad + da;
                        float d = gobReachPx * vs * (1f - Mathf.Exp(-kd * s)) / denom;
                        float wx = evCx + d * Mathf.Cos(theta);
                        float wy = evCy + d * Mathf.Sin(theta) + l.buoy * refPx * 0.55f * Mathf.Pow(s, 2.4f);
                        float r = l.gobSizePx * rs * (1f + l.gobSwell * s);
                        float aspect = 1f + 1.30f * Mathf.Exp(-s / 0.34f);
                        float amp = l.gobAmp * FadeIn(s, 0.07f) * Mathf.Pow(1f - s, 2f);
                        if (amp <= 0.0005f) continue;
                        float tint = Mathf.Clamp01(l.soot * s * 1.15f);
                        Blob(density, soot, W, H, wx, wy, r * aspect, r, theta, amp, tint);
                    }
                }
            }

            // ── shading pass: heat field -> straight-alpha pixels, through the shape's own Fill ramp ──────────
            const float SootR = 0.16f, SootG = 0.15f, SootB = 0.14f;
            bool anyPix = mods.pix != null && mods.pix.Length > 0;
            float lo = l.lo;
            // Auto-exposure: fit the ceiling to what THIS frame's field actually produced, instead of trusting a
            // fixed forkHi to happen to match whatever forkPuffCount/amp/blast-count the layer is currently set
            // to (see the field's doc comment in PyrePlusSpec.cs — a mismatched fixed ceiling either washes the
            // whole body out to a flat mid-tone or blows the core to a two-pixel speck, and it is not obvious
            // from the dial values alone which failure a given combination will hit).
            float hi;
            if (l.autoExposure)
            {
                float peak = 0f;
                for (int i = 0; i < count; i++) if (density[i] > peak) peak = density[i];
                hi = Mathf.Max(peak * Mathf.Max(l.exposureMult, 0.05f), lo + 1e-3f);
            }
            else
            {
                hi = l.hi;
            }
            float hiSpan = Mathf.Max(hi - lo, 1e-4f), soft = Mathf.Max(l.soft, 1e-4f);
            for (int py = 0; py < H; py++)
            {
                int row = py * W;
                for (int px = 0; px < W; px++)
                {
                    int idx = row + px;
                    float hv = density[idx];
                    if (hv <= lo) continue;

                    float t = Mathf.Clamp01((hv - lo) / hiSpan);
                    if (!Mathf.Approximately(l.curve, 1f)) t = Mathf.Pow(t, l.curve);

                    Color baseColor = fill != null ? fill.Evaluate(1f - t, 0.5f, 0.5f) : Color.white;
                    float tintAmt = Mathf.Clamp01(soot[idx] / Mathf.Max(hv, 1e-6f));
                    Color col = baseColor;
                    if (tintAmt > 0f)
                    {
                        Color grey = new Color(
                            Mathf.Lerp(baseColor.r, SootR, 0.85f),
                            Mathf.Lerp(baseColor.g, SootG, 0.85f),
                            Mathf.Lerp(baseColor.b, SootB, 0.85f), 1f);
                        col = Color.Lerp(baseColor, grey, tintAmt);
                    }

                    float edge = Smoothstep01((hv - lo) / soft);
                    float aceil = baseColor.a;
                    if (!Mathf.Approximately(l.opaq, 1f)) aceil = Mathf.Pow(Mathf.Clamp01(aceil), l.opaq);
                    float alpha = Mathf.Clamp01(edge * aceil) * layerAlpha;
                    if (alpha <= 0f) continue;

                    if (anyPix)
                    {
                        var pcol = new Color(col.r, col.g, col.b, 1f);
                        var info = new PixelInfo(px, py, px + 0.5f, py + 0.5f, mods.frameIndex, t, mods.life,
                                                 mods.pixHash, W, H);
                        bool keep = true;
                        for (int mi = 0; mi < mods.pix.Length && keep; mi++)
                            keep = mods.pix[mi].ApplyPixel(ref pcol, ref alpha, info);
                        if (!keep) continue;
                        col = pcol;
                    }

                    target[idx] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(col.r * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(col.g * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(col.b * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255));
                }
            }

            // ── ignition flash — a hard bright core at each blast's birth, gone in a handful of frames ─────────
            if (anim.flash > 0.002f)
            {
                Color hot = fill != null ? fill.Evaluate(0f, 0.5f, 0.5f) : Color.white;
                float flashR = Mathf.Lerp(hot.r * 255f, 255f, 0.6f);
                float flashG = Mathf.Lerp(hot.g * 255f, 255f, 0.6f);
                float flashB = Mathf.Lerp(hot.b * 255f, 255f, 0.6f);
                const float flashLifeFrac = 0.12f;
                foreach (var ev in events)
                {
                    float local = Mathf.Clamp01((progress - ev.start) / Mathf.Max(ev.duration, 1e-3f));
                    float fs = local / flashLifeFrac;
                    if (fs >= 1f) continue;
                    float flashAmp = anim.flash * FadeIn(fs, 0.05f) * Mathf.Pow(1f - fs, 3f);
                    if (flashAmp <= 0.004f) continue;
                    float evCx = nozzleX + ev.cx * (W * 0.5f);
                    float evCy = nozzleY + ev.cy * (H * 0.5f);
                    float flashR0 = (2.5f + l.puffSizePx * 2.2f) * ev.scale * (1f + 1.6f * fs);
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(evCx - flashR0 * 2.4f));
                    int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(evCx + flashR0 * 2.4f));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt(evCy - flashR0 * 2.4f));
                    int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(evCy + flashR0 * 2.4f));
                    for (int py = y0; py <= y1; py++)
                    {
                        float dy = (py + 0.5f) - evCy;
                        int row = py * W;
                        for (int px = x0; px <= x1; px++)
                        {
                            float dx = (px + 0.5f) - evCx;
                            float rr = Mathf.Sqrt(dx * dx + dy * dy);
                            float a = Mathf.Exp(-(rr * rr) / Mathf.Max(flashR0 * flashR0, 1e-3f) * 2.2f) * flashAmp;
                            if (a <= 0.004f) continue;
                            CompositeGlow(target, row + px, flashR, flashG, flashB, a * layerAlpha);
                        }
                    }
                }
            }
        }
    }
}
