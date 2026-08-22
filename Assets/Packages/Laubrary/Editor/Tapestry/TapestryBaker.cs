// TapestryBaker — renders every frame of a Tapestry's animation loop (only meaningful when a layer has
// Animate Transform on) into a sprite STRIP, mirroring LatheBaker.BakeStrip exactly: one row, one tile per
// frame, ready for Unity's Sprite Editor grid slicer.
using UnityEngine;

namespace Laubrary.Tapestry.Editor
{
    public static class TapestryBaker
    {
        /// Caller owns the returned Texture2D.
        public static Texture2D BakeStrip(TapestrySpec spec)
        {
            if (spec == null || spec.layers == null || spec.layers.Count == 0) return null;
            int size = Mathf.Clamp(spec.resolution, 16, 512);
            int frames = Mathf.Max(1, spec.frameCount);

            var strip = new Texture2D(size * frames, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            for (int f = 0; f < frames; f++)
            {
                float animT = f / (float)frames;
                var frameTex = TapestryCompositor.Bake(spec, animT);
                strip.SetPixels32(f * size, 0, size, size, frameTex.GetPixels32());
                Object.DestroyImmediate(frameTex);
            }
            strip.Apply(false, false);
            return strip;
        }
    }
}
