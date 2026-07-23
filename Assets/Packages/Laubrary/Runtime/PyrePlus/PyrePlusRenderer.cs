// PyrePlusRenderer — the parallel renderer for PyrePlusSpec (see PYREPLUS_DESIGN.md).
//
// Mirrors BlastRenderer's determinism contract exactly: pure, static, every random value derived from a
// seeded System.Random keyed by (seed, particleIndex, fieldId, salt) — never UnityEngine.Random, never Time —
// so preview, bake and runtime all produce byte-identical output from the same inputs, and any frame renders
// standalone. Swarm off ⇒ a single centred particle through the Shape fields. Swarm on ⇒ N particles, each
// spawned at its own point on the blast timeline and placed by a spawn-time snapshot of the shape transform
// (T1: Area+Circle placement). Modifiers follow.
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.PyrePlus
{
    public static class PyrePlusRenderer
    {
        static readonly Color32 Transparent = new Color32(0, 0, 0, 0);

        // ── field-ID registry ──────────────────────────────────────────────────────
        // Every ZUIValue Eval and every seeded random draw takes a UNIQUE field id so independent fields never
        // share an RNG stream (which would silently CORRELATE their draws — e.g. a MinMax size and a MinMax
        // placement landing in lock-step). Slice 1 used bare ints (size=1, alpha=2); these name them and RESERVE
        // the rest for later tasks. LATER TASKS MUST take their ids from this table — never invent a colliding
        // literal, and never reuse an id already spoken for.
        const int FldSize = 1;       // Shape size envelope, particle's own life          [T1]
        const int FldAlpha = 2;      // Shape alpha envelope, particle's own life         [T1]
        const int FldPlacement = 3;  // Area disc sampling (u1/u2 random draw)            [T1]
        const int FldScale = 4;      // shapeScale — shape radius, spawn-time snapshot    [T1]
        const int FldProgress = 5;   // swarmProgress — Path position, spawn-time snapshot [T2]  (reserved)
        const int FldOffsetX = 6;    // shapeOffsetX, spawn-time snapshot                 [T3]  (reserved)
        const int FldOffsetY = 7;    // shapeOffsetY, spawn-time snapshot                 [T3]  (reserved)
        const int FldRotation = 8;   // shapeRotation 2D, spawn-time snapshot             [T3]  (reserved)
        const int FldPitch = 9;      // shapePitch, spawn-time snapshot                   [T3]  (reserved)
        const int FldYaw = 10;       // shapeYaw, spawn-time snapshot                     [T3]  (reserved)
        const int FldSpin = 11;      // per-particle 2D spin, particle's own life         [T7]  (reserved)
        const int FldPathX = 12;     // per-particle position-path X, own life            [T7]  (reserved)
        const int FldPathY = 13;     // per-particle position-path Y, own life            [T7]  (reserved)

        /// One swarm particle's spawn data: where it lands on the blast timeline and its absolute canvas-pixel
        /// position. RenderFrame consumes this; the preview overlay (T5) calls ComputeSpawns to draw a dot at
        /// each particle's ACTUAL computed spawn location.
        public struct SpawnPoint
        {
            public float spawnLife;   // this particle's spawn point on the blast timeline, [0..1]
            public Vector2 pos;       // absolute canvas-pixel position (origin = buffer's (0,0))
        }

        public static Color32[] RenderFrame(PyrePlusSpec spec, int frameIndex)
        {
            int W = spec != null ? spec.Width : 1;
            int H = spec != null ? spec.Height : 1;
            var buf = new Color32[W * H];
            Color32 bg = spec != null ? (Color32)spec.background : Transparent;
            for (int i = 0; i < buf.Length; i++) buf[i] = bg;
            if (spec == null) return buf;

            int frames = Mathf.Max(1, spec.frameCount);
            float life = frames > 1 ? frameIndex / (float)(frames - 1) : 0f;

            if (!spec.swarmEnabled)
            {
                // Single particle, at the centre, on its own life = the blast life. (Unchanged Slice-1 path.)
                DrawParticle(buf, W, H, W * 0.5f, H * 0.5f, life, spec, particleIndex: 0);
                return buf;
            }

            RenderSwarm(buf, W, H, life, spec);
            return buf;
        }

        // ── swarm ──────────────────────────────────────────────────────────────────

        static void RenderSwarm(Color32[] buf, int W, int H, float life, PyrePlusSpec spec)
        {
            var spawns = new List<SpawnPoint>(Mathf.Max(2, spec.swarmCount));
            ComputeSpawns(spec, spawns);

            for (int i = 0; i < spawns.Count; i++)
            {
                var sp = spawns[i];
                // Each particle's own life clock: 0 at its spawn frame, 1 at its death. Skip when not alive yet
                // or already dead this frame. All Shape fields (size/alpha/colour) then evaluate at `own`, exactly
                // as the single particle evaluates them at `life`.
                float own = (life - sp.spawnLife) / Mathf.Max(0.0001f, spec.swarmParticleLife);
                if (own < 0f || own > 1f) continue;
                DrawParticle(buf, W, H, sp.pos.x, sp.pos.y, own, spec, particleIndex: i);
            }
        }

        /// Compute every swarm particle's spawn life (its point on the blast timeline) and spawn POSITION
        /// (absolute canvas-pixel coords). Pure and deterministic — RenderSwarm consumes it, and the preview
        /// overlay (T5) calls it directly to draw a dot at each particle's real spawn location. Fills `into`
        /// (cleared first); allocation-light beyond the list's own growth. Empty when the swarm is off.
        public static void ComputeSpawns(PyrePlusSpec spec, List<SpawnPoint> into)
        {
            if (into == null) return;
            into.Clear();
            if (spec == null || !spec.swarmEnabled) return;

            int n = Mathf.Max(2, spec.swarmCount);
            float cx = spec.Width * 0.5f;
            float cy = spec.Height * 0.5f;
            float window = Mathf.Clamp01(spec.swarmSpawnWindow);

            for (int i = 0; i < n; i++)
            {
                // Even spawn distribution across the window; particle 0 at 0. n ≥ 2 so (n-1) ≥ 1.
                float spawnLife = window * i / (n - 1);

                // The shape radius is a SPAWN-TIME SNAPSHOT: evaluate shapeScale at THIS particle's spawn life,
                // NOT the current frame — so a Curve-mode shapeScale leaves a growing trail of placements rather
                // than retroactively resizing already-placed particles (the same live-vs-snapshot split Pyre's
                // scatter makes: ringExpand is live, spawnRadius/ringStartAngle are spawn-snapshot). T3 extends
                // this exact principle to the whole transform (offset/rotation/pitch/yaw), all sampled here.
                float r = Mathf.Max(0f, Eval(spec.shapeScale, spawnLife, spec.seed, i, FldScale));
                float snap = spec.shapeScaleSnap;
                if (snap > 0f) r = Mathf.Round(r / snap) * snap;

                // T2: only Area + Circle is placed here — a uniform point in a disc centred on the canvas. Every
                // other swarmSpawnMode / swarmShapeKind combination falls back to this same placement for now
                // (T2 replaces it with polygon-boundary sampling and Path-progress placement).
                Vector2 off = SampleDisc(r, spec.seed, i);
                into.Add(new SpawnPoint { spawnLife = spawnLife, pos = new Vector2(cx + off.x, cy + off.y) });
            }
        }

        /// A uniformly-distributed random point inside a disc of the given radius — sqrt-distributed radius so
        /// points spread evenly by AREA (not clumped at the centre), angle uniform. Seeded per
        /// (seed, particleIndex, FldPlacement) so the draw is stable across frames and identical in
        /// preview/bake/runtime.
        static Vector2 SampleDisc(float radius, int seed, int particleIndex)
        {
            var rng = new System.Random(Hash(seed, particleIndex, FldPlacement, 0));
            float u1 = (float)rng.NextDouble();
            float u2 = (float)rng.NextDouble();
            float rr = radius * Mathf.Sqrt(u1);
            float ang = u2 * 2f * Mathf.PI;
            return new Vector2(rr * Mathf.Cos(ang), rr * Mathf.Sin(ang));
        }

        static void DrawParticle(Color32[] buf, int W, int H, float cx, float cy, float life,
                                 PyrePlusSpec spec, int particleIndex)
        {
            float radius = Mathf.Max(0f, Eval(spec.size, life, spec.seed, particleIndex, FldSize));
            if (radius <= 0.01f) return;
            float alpha = Mathf.Clamp01(Eval(spec.alpha, life, spec.seed, particleIndex, FldAlpha));
            if (alpha <= 0.002f) return;

            Color col = spec.colorOverLife != null ? spec.colorOverLife.Evaluate(life) : Color.white;
            // Soft rim: alpha ramps from `soft`·radius out to the edge. 0 softness = a hard pixel disc.
            float soft = Mathf.Clamp01(spec.edgeSoftness);
            float inner = radius * (1f - soft);

            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - radius));
            int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + radius));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - radius));
            int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + radius));

            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > radius) continue;
                    float edge = d <= inner ? 1f : 1f - Mathf.InverseLerp(inner, radius, d);
                    float a = alpha * col.a * edge;
                    if (a <= 0.002f) continue;
                    Over(buf, y * W + x, col.r, col.g, col.b, a);
                }
        }

        static void Over(Color32[] buf, int i, float r, float g, float b, float a)
        {
            var dst = buf[i];
            float da = dst.a * (1f / 255f);
            float outA = a + da * (1f - a);
            if (outA <= 0.0001f) { buf[i] = Transparent; return; }
            float inv = 1f / outA;
            buf[i] = new Color32(
                (byte)(Mathf.Clamp01((r * a + dst.r * (1f / 255f) * da * (1f - a)) * inv) * 255f),
                (byte)(Mathf.Clamp01((g * a + dst.g * (1f / 255f) * da * (1f - a)) * inv) * 255f),
                (byte)(Mathf.Clamp01((b * a + dst.b * (1f / 255f) * da * (1f - a)) * inv) * 255f),
                (byte)(Mathf.Clamp01(outA) * 255f));
        }

        /// Evaluate a ZUIValue for a given particle deterministically — Static reads the value, Curve reads
        /// the envelope at the particle's life, MinMax draws once from a seeded RNG keyed by (seed, index,
        /// field). Mirrors BlastRenderer.Eval; MinMax is what makes each particle differ reproducibly.
        static float Eval(ZUIValue v, float life, int seed, int particleIndex, int fieldId)
        {
            if (v == null) return 0f;
            switch (v.mode)
            {
                case ZUIValue.Mode.Static: return v.staticValue;
                case ZUIValue.Mode.MinMax:
                {
                    var rng = new System.Random(Hash(seed, particleIndex, fieldId, 0));
                    return Mathf.Lerp(v.min, v.max, (float)rng.NextDouble());
                }
                case ZUIValue.Mode.Curve: return v.EvaluateRaw(Mathf.Clamp01(life));
                default: return v.staticValue;
            }
        }

        static int Hash(int a, int b, int c, int d)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)a) * 16777619u;
                h = (h ^ (uint)b) * 16777619u;
                h = (h ^ (uint)c) * 16777619u;
                h = (h ^ (uint)d) * 16777619u;
                return (int)h;
            }
        }

        public static Texture2D RenderFrameTexture(PyrePlusSpec spec, int frameIndex)
        {
            int W = spec != null ? spec.Width : 1;
            int H = spec != null ? spec.Height : 1;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels32(RenderFrame(spec, frameIndex));
            tex.Apply();
            return tex;
        }
    }
}
