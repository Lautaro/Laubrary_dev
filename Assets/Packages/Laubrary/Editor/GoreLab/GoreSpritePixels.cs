// A drawn frame as the engine sees it (a GoreGrid: sprite-local, top-left origin, y down, 0xAABBGGRR) plus a
// point-filtered texture of the same pixels for the stage. Mirrored views are the horizontally flipped pixels.
using Laubrary.PreviewKit;
using UnityEngine;

namespace Laubrary.GoreLab.Editor
{
    internal sealed class GoreSpritePixels
    {
        public readonly GoreGrid grid;
        public readonly Texture2D texture;
        public int W => grid.w;
        public int H => grid.h;

        GoreSpritePixels(GoreGrid grid)
        {
            this.grid = grid;
            texture = NewTexture(grid.w, grid.h);
            Write(texture, grid.px, grid.w, grid.h);
        }

        /// Reads the sprite's declared rect (never the trimmed textureRect, which can be fractional). Readable
        /// textures are read directly; others go through a GPU copy. Null when there is nothing to read.
        public static GoreSpritePixels Read(Sprite s, bool mirrored)
        {
            if (s == null || s.texture == null) return null;
            var r = s.rect;
            int w = Mathf.Max(1, Mathf.RoundToInt(r.width)), h = Mathf.Max(1, Mathf.RoundToInt(r.height));
            int x0 = Mathf.RoundToInt(r.x), y0 = Mathf.RoundToInt(r.y);

            Color32[] src = null;
            int srcW = 0;
            Texture2D temp = null;
            if (s.texture.isReadable)
            {
                try { src = s.texture.GetPixels32(); srcW = s.texture.width; }
                catch { src = null; }
            }
            if (src == null)
            {
                temp = PreviewTex.ReadRect(s.texture, r);
                if (temp == null) return null;
                src = temp.GetPixels32();
                srcW = temp.width;
                x0 = 0; y0 = 0;
            }

            var grid = new GoreGrid(w, h);
            for (int y = 0; y < h; y++)
            {
                int row = (y0 + (h - 1 - y)) * srcW;     // texture rows run bottom-up, the grid's top-down
                for (int x = 0; x < w; x++)
                {
                    var c = src[row + x0 + x];
                    int gx = mirrored ? w - 1 - x : x;
                    grid.px[y * w + gx] = Pack(c);
                }
            }
            if (temp != null) Object.DestroyImmediate(temp);
            return new GoreSpritePixels(grid);
        }

        public void Dispose()
        {
            if (texture != null) Object.DestroyImmediate(texture);
        }

        public static uint Pack(Color32 c) => (uint)(c.r | (c.g << 8) | (c.b << 16) | (c.a << 24));
        public static Color32 Unpack(uint p) => new Color32((byte)(p & 255), (byte)((p >> 8) & 255), (byte)((p >> 16) & 255), (byte)(p >> 24));

        public static Texture2D NewTexture(int w, int h)
            => new Texture2D(Mathf.Max(1, w), Mathf.Max(1, h), TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

        static Color32[] s_buf;

        /// Writes a top-down pixel array into a texture (whose rows run bottom-up), resizing it when needed.
        public static void Write(Texture2D tex, uint[] px, int w, int h)
        {
            if (tex.width != w || tex.height != h) tex.Reinitialize(w, h);
            int n = w * h;
            if (s_buf == null || s_buf.Length != n) s_buf = new Color32[n];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    s_buf[(h - 1 - y) * w + x] = Unpack(px[y * w + x]);
            tex.SetPixels32(s_buf);
            tex.Apply(false);
        }

        /// The same for an overlay already laid out top-down as colours.
        public static void Write(Texture2D tex, Color32[] topDown, int w, int h)
        {
            if (tex.width != w || tex.height != h) tex.Reinitialize(w, h);
            int n = w * h;
            if (s_buf == null || s_buf.Length != n) s_buf = new Color32[n];
            for (int y = 0; y < h; y++)
                System.Array.Copy(topDown, y * w, s_buf, (h - 1 - y) * w, w);
            tex.SetPixels32(s_buf);
            tex.Apply(false);
        }
    }
}
