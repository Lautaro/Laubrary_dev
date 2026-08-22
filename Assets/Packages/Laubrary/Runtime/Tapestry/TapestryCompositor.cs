// TapestryCompositor — composites a TapestrySpec's layer stack into one final Color32[] buffer: each
// enabled layer's generator paints into its own scratch buffer (with everything composited so far handed in
// as read-only feedback), the layer's own modifiers run over that buffer, then it's blended into the
// accumulator via its blend mode + opacity — mirrors PyrePlus's FrameComposer applied to a single static
// image instead of an animated frame, then runs the spec-wide globalModifiers pass exactly like
// FrameComposer.Finish does over the fully-composited frame.
using UnityEngine;

namespace Laubrary.Tapestry
{
    public static class TapestryCompositor
    {
        /// Caller owns the returned Texture2D (HideAndDontSave, never an asset) and must destroy it.
        public static Texture2D Bake(TapestrySpec spec)
        {
            var px = BakePixels(spec, out int res);
            if (px == null) return null;
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        public static Color32[] BakePixels(TapestrySpec spec, out int resolution)
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
                    Blend(accum, scratch, layer.blendMode, layer.opacity);
                }

            if (spec.globalModifiers != null)
                foreach (var m in spec.globalModifiers)
                    if (m != null && m.enabled) m.Apply(accum, resolution, resolution);

            return accum;
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
