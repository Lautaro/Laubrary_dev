// PyrePlusRenderer — the parallel renderer for PyrePlusSpec (see PYREPLUS_DESIGN.md).
//
// Mirrors BlastRenderer's determinism contract exactly: pure, static, every random value derived from a
// seeded System.Random keyed by (seed, particleIndex, fieldId) — never UnityEngine.Random — so preview,
// bake and runtime all produce byte-identical output from the same inputs. SLICE 1 renders the Shape
// section as a single centred particle; Swarm (many particles, placed in a shape) and Modifiers follow.
using UnityEngine;

namespace Laubrary.PyrePlus
{
    public static class PyrePlusRenderer
    {
        static readonly Color32 Transparent = new Color32(0, 0, 0, 0);

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

            // Slice 1: exactly one particle, at the centre, on its own life = the blast life.
            DrawParticle(buf, W, H, W * 0.5f, H * 0.5f, life, spec, particleIndex: 0);

            return buf;
        }

        static void DrawParticle(Color32[] buf, int W, int H, float cx, float cy, float life,
                                 PyrePlusSpec spec, int particleIndex)
        {
            float radius = Mathf.Max(0f, Eval(spec.size, life, spec.seed, particleIndex, 1));
            if (radius <= 0.01f) return;
            float alpha = Mathf.Clamp01(Eval(spec.alpha, life, spec.seed, particleIndex, 2));
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
