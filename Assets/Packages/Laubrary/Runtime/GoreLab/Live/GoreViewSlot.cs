using UnityEngine;

namespace Laubrary.GoreLab
{
    /// <summary>
    /// One VIEW of a drawn frame: the sprite as drawn, or its horizontal mirror. A mirrored view is baked from mirrored pixels and mirrored tags and
    /// shown without the renderer's flip, because turning a body around is a rotation: its wounds must not appear mirrored.
    /// One reusable texture and sprite per view, rewritten in place on every bake.
    /// </summary>
    public sealed class GoreViewSlot
    {
        public Sprite source;
        public bool flipped;
        public GoreFrameTags tags;
        public int w, h;
        public GoreGrid grid;                 // the view's pixels (mirrored when flipped), top-left origin
        public GoreMemberInput[] members;     // the view's tags and masks (mirrored when flipped)

        public Texture2D texture;
        public Sprite sprite;
        public uint[] upload;                 // bottom-up copy of the baked body, reused

        public int bakedVersion = -1;
        public bool missing;                  // a needed member has no tag on this frame: show as drawn
        public int changed;
        public GoreBleedPoint[] bleed = new GoreBleedPoint[0];

        /// <summary>The picture to show for this view right now, or null to show the character as drawn.</summary>
        public Sprite WoundedSprite { get { return missing || changed == 0 ? null : sprite; } }

        public void EnsureTexture(float pixelsPerUnit)
        {
            if (texture != null) return;
            texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "GoreView" };
            var piv = source.pivot;
            var pivot = new Vector2((flipped ? w - piv.x : piv.x) / w, piv.y / h);
            sprite = Sprite.Create(texture, new Rect(0, 0, w, h), pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = "GoreView";
            upload = new uint[w * h];
        }

        public void Destroy()
        {
            if (sprite != null) Object.Destroy(sprite);
            if (texture != null) Object.Destroy(texture);
            sprite = null; texture = null;
        }
    }

    /// <summary>Reads a sprite's pixels into the engine's layout (0xAABBGGRR, top-left origin, y down).</summary>
    public static class GoreSourcePixels
    {
        /// <summary>Returns null (and logs once per texture) when the sprite's texture is not readable.</summary>
        public static GoreGrid Read(Sprite s, bool mirrored)
        {
            if (s == null) return null;
            var tex = s.texture;
            if (tex == null || !tex.isReadable)
            {
                Debug.LogWarning("GoreLab: the sprite '" + (s != null ? s.name : "?") + "' is not readable, so it cannot be wounded. Enable Read/Write on its texture import settings.", s);
                return null;
            }
            Rect r = s.textureRect;
            int rx = Mathf.RoundToInt(r.x), ry = Mathf.RoundToInt(r.y), w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
            var px = tex.GetPixels32();
            int tw = tex.width;
            var grid = new GoreGrid(w, h);
            for (int y = 0; y < h; y++)
            {
                int texRow = ry + (h - 1 - y);                 // texture rows are bottom-up
                for (int x = 0; x < w; x++)
                {
                    Color32 c = px[texRow * tw + rx + x];
                    int dx = mirrored ? w - 1 - x : x;
                    grid.px[y * w + dx] = ((uint)c.a << 24) | ((uint)c.b << 16) | ((uint)c.g << 8) | c.r;
                }
            }
            return grid;
        }
    }
}
