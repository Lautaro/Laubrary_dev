using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Launimator
{
    /// <summary>
    /// An authored meta-layer on an animation: a named (id) overlay that carries, PER animation frame, an
    /// optional free-text parameter and a hand-drawn monochrome pixel mask (values 0–10, 0 = no data). It is
    /// gameplay metadata aligned to the final sequence — e.g. a melee weapon's swing trajectory, or the exact
    /// pixel a bullet spawns from on the shooting frame. The mask is monochrome: the layer picks ONE display
    /// colour and each cell's value scales its alpha (0 transparent → 10 full), so authoring stays simple while
    /// values can still encode intensity/order. Purely additive — animations without layers are unaffected.
    /// </summary>
    /// <summary>Shape (the original mechanism — a painted region, e.g. a hitbox mask) or Point (exactly one
    /// painted cell per frame — a lighter "here's the one pixel that matters" marker, e.g. where a shockwave
    /// should originate). Both read through the SAME runtime API (<c>TryGetMetaPoint</c> already computes a
    /// value-weighted centroid, which for a single painted cell just IS that cell) — Point only changes how the
    /// Animation Builder's paint tool behaves and which layers a "pick a point marker" UI offers.</summary>
    public enum MetaLayerMode { Shape, Point }

    [System.Serializable]
    public class MetaLayer
    {
        [Tooltip("Layer identity the game looks up (e.g. \"hitbox\", \"muzzle\", \"trail\").")]
        public string id = "layer";

        [Tooltip("Shape (painted region) or Point (one pixel per frame). See MetaLayerMode's own doc comment.")]
        public MetaLayerMode mode = MetaLayerMode.Shape;

        [Tooltip("Display colour for this layer's mask. Monochrome — each cell's value 0–10 just scales alpha.")]
        public Color color = new Color(1f, 0.25f, 0.25f, 1f);

        [Tooltip("One entry per animation frame, in sequence order.")]
        public List<MetaFrame> frames = new List<MetaFrame>();

        /// <summary>Display colour for a cell value (0 = fully transparent). Value 5 = the layer colour; values
        /// ramp the BRIGHTNESS so they read apart visually — 1 = almost black, 5 = the layer colour, 10 = almost
        /// white. Alpha = the layer colour's alpha (the per-layer transparency).</summary>
        public static Color CellColor(Color baseColor, int value)
        {
            if (value <= 0) return new Color(0f, 0f, 0f, 0f);
            float t = Mathf.Clamp(value, 1, 10);
            Color rgb = t <= 5f
                ? Color.Lerp(new Color(0.06f, 0.06f, 0.06f), baseColor, (t - 1f) / 4f)   // 1→5: near-black → colour
                : Color.Lerp(baseColor, new Color(0.96f, 0.96f, 0.96f), (t - 5f) / 5f);  // 5→10: colour → near-white
            rgb.a = baseColor.a;
            return rgb;
        }

        /// <summary>The 5 predetermined layer colours offered by the Builder's palette popup.</summary>
        public static readonly Color[] Palette =
        {
            new Color(1f,   0.30f, 0.30f, 1f),  // red
            new Color(0.40f, 0.85f, 0.45f, 1f), // green
            new Color(0.40f, 0.65f, 1f,   1f),  // blue
            new Color(1f,   0.82f, 0.28f, 1f),  // amber
            new Color(0.85f, 0.45f, 1f,   1f),  // violet
        };
    }

    /// <summary>One animation frame's meta data for a layer: a free-text parameter plus a value grid (0–10,
    /// row-major, bottom-left origin) sized to the baked frame so it overlays the sprite 1:1.</summary>
    [System.Serializable]
    public class MetaFrame
    {
        [Tooltip("Free-text parameter for this frame on this layer (game-defined meaning).")]
        public string param = "";

        public int w, h;
        [Tooltip("Per-cell value 0–10 (0 = empty). Length w*h, row-major, bottom-left origin. May be null = empty.")]
        public byte[] cells;

        public MetaFrame Clone()
            => new MetaFrame { param = param, w = w, h = h, cells = cells != null ? (byte[])cells.Clone() : null };

        public byte Get(int x, int y)
            => (cells != null && x >= 0 && x < w && y >= 0 && y < h) ? cells[y * w + x] : (byte)0;

        public void Set(int x, int y, int value)
        {
            EnsureAlloc();
            if (x >= 0 && x < w && y >= 0 && y < h) cells[y * w + x] = (byte)Mathf.Clamp(value, 0, 10);
        }

        public bool HasAny()
        {
            if (cells == null) return false;
            for (int i = 0; i < cells.Length; i++) if (cells[i] > 0) return true;
            return false;
        }

        void EnsureAlloc()
        {
            int need = Mathf.Max(0, w * h);
            if (cells == null || cells.Length != need) cells = new byte[need];
        }

        /// <summary>Resize the grid to <paramref name="nw"/>×<paramref name="nh"/>, preserving the drawing by
        /// nearest-neighbor resampling (so a mask survives a change in baked frame size).</summary>
        public void EnsureSize(int nw, int nh)
        {
            nw = Mathf.Max(0, nw); nh = Mathf.Max(0, nh);
            if (nw == w && nh == h && cells != null && cells.Length == nw * nh) return;

            var old = cells; int ow = w, oh = h;
            w = nw; h = nh; cells = new byte[nw * nh];
            if (old == null || ow <= 0 || oh <= 0) return;
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    int sx = (ow == nw) ? x : Mathf.Clamp(Mathf.FloorToInt((x + 0.5f) * ow / nw), 0, ow - 1);
                    int sy = (oh == nh) ? y : Mathf.Clamp(Mathf.FloorToInt((y + 0.5f) * oh / nh), 0, oh - 1);
                    cells[y * nw + x] = old[sy * ow + sx];
                }
        }
    }
}
