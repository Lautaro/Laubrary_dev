// TapestryCompositor — composites a TapestrySpec's layer stack into one final Color32[] buffer: each
// enabled layer's generator paints into its own scratch buffer (with everything composited so far handed in
// as read-only feedback), the layer's own modifiers run over that buffer, then it's blended into the
// accumulator via its blend mode + opacity — mirrors Pyre's FrameComposer applied to a single static
// image instead of an animated frame, then runs the spec-wide globalModifiers pass exactly like
// FrameComposer.Finish does over the fully-composited frame.
using UnityEngine;

namespace Laubrary.Tapestry
{
    public static class TapestryCompositor
    {
        /// Caller owns the returned Texture2D (HideAndDontSave, never an asset) and must destroy it. `animT`
        /// (0..1, wraps) drives every layer's own Animate Transform drift — 0 for a purely static bake.
        public static Texture2D Bake(TapestrySpec spec, float animT = 0f)
        {
            var px = BakePixels(spec, out int res, animT);
            if (px == null) return null;
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        public static Color32[] BakePixels(TapestrySpec spec, out int resolution, float animT = 0f)
        {
            resolution = spec != null ? Mathf.Clamp(spec.resolution, 16, 512) : 0;
            if (spec == null) return null;
            var accum = new Color32[resolution * resolution];   // starts transparent black

            if (spec.layers != null)
                foreach (var layer in spec.layers)
                {
                    if (layer == null || !layer.enabled || layer.generator == null) continue;
                    var scratch = new Color32[resolution * resolution];
                    // `accum` is handed in as read-only feedback BEFORE this layer's own contribution is
                    // blended into it below — every layer sees exactly what's below it, nothing above.
                    var ctx = new TapestryGenCtx(resolution, resolution, spec.seed, accum);
                    layer.generator.Generate(ctx, scratch);
                    if (layer.modifiers != null)
                        foreach (var m in layer.modifiers)
                            if (m != null && m.enabled) m.Apply(scratch, resolution, resolution);

                    Vector2 pos = layer.position + (layer.animateTransform ? layer.positionSpeed * animT : Vector2.zero);
                    float rotDeg = layer.rotation + (layer.animateTransform ? layer.rotationTurns * 360f * animT : 0f);
                    Vector2 scale = layer.scale + (layer.animateTransform ? layer.scaleSpeed * animT : Vector2.zero);
                    scratch = ApplyTransform(scratch, resolution, resolution, pos, rotDeg, scale);

                    Blend(accum, scratch, layer.blendMode, layer.opacity);
                }

            if (spec.globalModifiers != null)
                foreach (var m in spec.globalModifiers)
                    if (m != null && m.enabled) m.Apply(accum, resolution, resolution);

            return accum;
        }

        // Resamples a layer's own rendered buffer by its transform — a post-process on the RASTER, not the
        // generator, so every generator (present and future) gets position/rotation/scale for free with zero
        // changes to its own code, exactly like transforming a rasterized layer in an image editor. The
        // source is always sampled WRAPPED (Mathf.Repeat) — a plain translate still tiles cleanly since UV
        // wrapping is exact for any offset, but a rotation/non-1 scale will generally NOT tile against the
        // canvas edge any more. That's an accepted, user-opted-into trade — not a bug.
        static Color32[] ApplyTransform(Color32[] src, int W, int H, Vector2 pos, float rotDeg, Vector2 scale)
        {
            if (pos == Vector2.zero && rotDeg == 0f && scale == Vector2.one) return src;   // fast path, no-op

            var outBuf = new Color32[W * H];
            float rad = -rotDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            Vector2 invScale = new Vector2(1f / Mathf.Max(0.0001f, scale.x), 1f / Mathf.Max(0.0001f, scale.y));

            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    Vector2 outputUV = new Vector2((x + 0.5f) / W, (y + 0.5f) / H);
                    Vector2 rel = outputUV - pos - new Vector2(0.5f, 0.5f);
                    Vector2 unscaled = new Vector2(rel.x * invScale.x, rel.y * invScale.y);
                    Vector2 unrotated = new Vector2(
                        unscaled.x * cos - unscaled.y * sin,
                        unscaled.x * sin + unscaled.y * cos);
                    Vector2 sourceUV = unrotated + new Vector2(0.5f, 0.5f);
                    sourceUV.x = Mathf.Repeat(sourceUV.x, 1f);
                    sourceUV.y = Mathf.Repeat(sourceUV.y, 1f);
                    outBuf[y * W + x] = BilinearSample(src, W, H, sourceUV);
                }
            }
            return outBuf;
        }

        static Color32 BilinearSample(Color32[] buf, int W, int H, Vector2 uv)
        {
            float fx = uv.x * W - 0.5f, fy = uv.y * H - 0.5f;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            Color c00 = SampleWrapped(buf, W, H, x0, y0);
            Color c10 = SampleWrapped(buf, W, H, x0 + 1, y0);
            Color c01 = SampleWrapped(buf, W, H, x0, y0 + 1);
            Color c11 = SampleWrapped(buf, W, H, x0 + 1, y0 + 1);
            Color top = Color.Lerp(c00, c10, tx);
            Color bot = Color.Lerp(c01, c11, tx);
            return Color.Lerp(top, bot, ty);
        }

        static Color32 SampleWrapped(Color32[] buf, int W, int H, int x, int y)
        {
            x = ((x % W) + W) % W;
            y = ((y % H) + H) % H;
            return buf[y * W + x];
        }

        static void Blend(Color32[] below, Color32[] layer, TapestryBlendMode mode, float opacity)
        {
            for (int i = 0; i < below.Length; i++)
            {
                Color32 l32 = layer[i];
                float a = (l32.a / 255f) * opacity;
                if (a <= 0f) continue;
                Color b = below[i], l = l32;
                Color result;
                switch (mode)
                {
                    case TapestryBlendMode.Add:
                        result = new Color(b.r + l.r * a, b.g + l.g * a, b.b + l.b * a, Mathf.Clamp01(b.a + a * (1f - b.a)));
                        break;
                    case TapestryBlendMode.Multiply:
                        result = Color.Lerp(b, new Color(b.r * l.r, b.g * l.g, b.b * l.b, b.a), a);
                        break;
                    case TapestryBlendMode.Screen:
                        result = Color.Lerp(b, new Color(1f - (1f - b.r) * (1f - l.r), 1f - (1f - b.g) * (1f - l.g),
                            1f - (1f - b.b) * (1f - l.b), b.a), a);
                        break;
                    default: // Normal — straight alpha Over, accumulating coverage same as any layer editor.
                        result = Color.Lerp(b, l, a);
                        result.a = Mathf.Clamp01(b.a + a * (1f - b.a));
                        break;
                }
                below[i] = result;
            }
        }
    }
}
