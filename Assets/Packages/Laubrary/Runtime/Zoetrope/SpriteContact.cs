using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// The pixels two drawn sprites share, as found by <see cref="SpriteAlphaHitFilter.Overlap"/>: a grid laid over
    /// the world rectangle both sprites cover, one cell per art pixel, true where both have a drawn pixel. A game
    /// can ask where the touch is (<see cref="Centre"/>, <see cref="Pixels"/>) or draw exactly the shared pixels
    /// (<see cref="ToSprite"/>), for instance as a hit spark that matches the contact instead of the whole body.
    /// </summary>
    public sealed class SpriteContact
    {
        /// <summary>World position of the grid's lower left corner.</summary>
        public readonly Vector2 Origin;
        /// <summary>World size of one cell (one art pixel).</summary>
        public readonly float PixelSize;
        public readonly int Width, Height;
        /// <summary>Row-major, bottom row first: true where both sprites have a drawn pixel.</summary>
        public readonly bool[] Shared;
        /// <summary>The middle of the shared pixels, in world space.</summary>
        public readonly Vector2 Centre;
        /// <summary>How many pixels are shared.</summary>
        public readonly int Pixels;

        public SpriteContact(Vector2 origin, float pixelSize, int width, int height, bool[] shared, Vector2 centre, int pixels)
        { Origin = origin; PixelSize = pixelSize; Width = width; Height = height; Shared = shared; Centre = centre; Pixels = pixels; }

        /// <summary>The shared pixels as a sprite of the given colour, drawn by placing it at <see cref="Origin"/> in world space. The caller owns the sprite and its texture and must destroy them.</summary>
        public Sprite ToSprite(Color color)
        {
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[Width * Height];
            var on = (Color32)color; var off = new Color32(0, 0, 0, 0);
            for (int i = 0; i < px.Length; i++) px[i] = Shared[i] ? on : off;
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, Width, Height), Vector2.zero, 1f / PixelSize);
        }
    }
}
