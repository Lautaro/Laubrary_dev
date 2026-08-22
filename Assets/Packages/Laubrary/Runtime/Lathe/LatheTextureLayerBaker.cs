// LatheTextureLayerBaker — blends a solid's Texture field (Layer A) with a second texture (Layer B), each
// independently tiled/scrolled, into ONE composite baked at render time — same bake-then-feed-into-texProp
// technique LatheSurfaceFill already uses for procedural gradients. This is deliberately NOT a custom shader:
// the lit material only ever has one texture slot either way, so a Graphics.Blit (which already supports a
// UV scale+offset with no shader of its own) per layer, followed by a plain CPU pixel blend, gets the same
// result without introducing shader-authoring risk. Same runtime-safe APIs LatheSurfaceFill's baker uses.
using UnityEngine;

namespace Laubrary.Lathe
{
    public enum LatheBlendMode { Alpha, Add, Multiply, Screen }

    public static class LatheTextureLayerBaker
    {
        public static Texture2D Bake(LatheSolid solid, float animT, int resolution)
        {
            if (solid == null || !solid.secondTextureLayer || solid.texture2 == null) return null;
            resolution = Mathf.Clamp(resolution, 8, 256);

            var layerA = solid.texture != null ? solid.texture : Texture2D.whiteTexture;
            var layerB = solid.texture2;
            Vector2 offsA = solid.animateTexture ? solid.scrollSpeed * animT : Vector2.zero;
            Vector2 offsB = solid.animateTexture2 ? solid.scrollSpeed2 * animT : Vector2.zero;

            var rtA = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.ARGB32);
            var rtB = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.ARGB32);
            // Graphics.Blit's own scale/offset overload samples the source with a UV transform directly —
            // exactly the per-layer tiling/scroll we need, with no material or shader involved.
            Graphics.Blit(layerA, rtA, new Vector2(solid.tileScale, solid.tileScale), offsA);
            Graphics.Blit(layerB, rtB, new Vector2(solid.tileScale2, solid.tileScale2), offsB);

            var pxA = ReadPixels(rtA, resolution);
            var pxB = ReadPixels(rtB, resolution);
            RenderTexture.ReleaseTemporary(rtA);
            RenderTexture.ReleaseTemporary(rtB);

            var outTex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var outPx = new Color32[resolution * resolution];
            for (int i = 0; i < outPx.Length; i++)
                outPx[i] = Blend(pxA[i], pxB[i], solid.blendMode, solid.blendAmount);
            outTex.SetPixels32(outPx);
            outTex.Apply(false, false);
            return outTex;
        }

        static Color32[] ReadPixels(RenderTexture rt, int resolution)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
            tex.Apply(false, false);
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return px;
        }

        static Color32 Blend(Color32 a32, Color32 b32, LatheBlendMode mode, float amount)
        {
            Color a = a32, b = b32;
            switch (mode)
            {
                case LatheBlendMode.Add:
                    return new Color(a.r + b.r * amount, a.g + b.g * amount, a.b + b.b * amount, a.a);
                case LatheBlendMode.Multiply:
                    return new Color(a.r * Mathf.Lerp(1f, b.r, amount), a.g * Mathf.Lerp(1f, b.g, amount),
                        a.b * Mathf.Lerp(1f, b.b, amount), a.a);
                case LatheBlendMode.Screen:
                {
                    var screened = new Color(1f - (1f - a.r) * (1f - b.r), 1f - (1f - a.g) * (1f - b.g),
                        1f - (1f - a.b) * (1f - b.b), a.a);
                    return Color.Lerp(a, screened, amount);
                }
                default: // Alpha — Layer B's own alpha (scaled by Blend Amount) decides how much shows through.
                {
                    var lerped = Color.Lerp(a, b, b.a * amount);
                    lerped.a = a.a;
                    return lerped;
                }
            }
        }
    }
}
