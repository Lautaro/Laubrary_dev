using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Larder
{
    /// The ONE deterministic painter every part of Larder shares. Given a WareSpec and a damage stage it returns the
    /// exact same pixels for the editor preview, the PNG baker, and the runtime demo. Determinism is non-negotiable,
    /// so it uses System.Random(seed) — never UnityEngine.Random — and never touches UnityEditor. The paint is a long,
    /// readable, linear pass: build a silhouette mask, decide where damage has bitten the top off, fill the body,
    /// stamp the kind's identity, lay on bands/corners/spots/lid/label, then burn the torn edge.
    public static class WareGenerator
    {
        static readonly Color Clear = new Color(0, 0, 0, 0);

        // ── public entry points ──────────────────────────────────────────────────────────────────────

        /// Paint a Ware into a fresh Color32 buffer (row-major, y from the bottom). width/height come back via out.
        public static Color32[] Render(WareSpec spec, int damageStage, out int width, out int height)
        {
            int res = Mathf.Clamp(spec.resolution, 8, 128);
            width = Mathf.Max(4, Mathf.RoundToInt(res * Mathf.Clamp(spec.widthRatio, 0.15f, 1f)));
            height = Mathf.Max(4, Mathf.RoundToInt(res * Mathf.Clamp(spec.heightRatio, 0.15f, 1f)));
            int w = width, h = height;

            var pal = spec.Palette;
            var buf = new Color[w * h];
            for (int i = 0; i < buf.Length; i++) buf[i] = Clear;

            // Two independent, seed-derived rngs so decoration choices and the damage jaggedness stay stable no matter
            // how much either consumes — the tear looks the same whether or not there happens to be a label this roll.
            var rng = new System.Random(spec.seed);
            var dmgRng = new System.Random(spec.seed * 92821 + damageStage * 6151 + 17);

            // 1) silhouette mask
            bool[] mask = BuildMask(spec.shape, w, h);

            // 2) where the top has been torn off for this damage stage (cutY[x] = first removed row from the bottom)
            int stages = Mathf.Clamp(spec.damageStages, 1, 4);
            int[] cutY = BuildCut(damageStage, stages, w, h, dmgRng);

            // 3) fill the body
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (mask[y * w + x] && y < cutY[x])
                        buf[y * w + x] = BodyColor(spec.fill, pal, x, y, w, h);

            // 4) the kind's identity marks (planks, seams, rims, spine bands…)
            DrawKindDetails(spec, pal, buf, mask, cutY, w, h, rng);

            // 5) generic decoration, painted over the body
            if (spec.bands != BandMode.None) DrawBands(spec, pal, buf, mask, cutY, w, h);
            if (spec.spots != SpotMode.None) DrawSpots(spec, pal, buf, mask, cutY, w, h, rng);
            if (spec.hasLid) DrawLid(pal, buf, mask, cutY, w, h);
            if (spec.label != LabelStyle.None) DrawLabel(spec, pal, buf, mask, cutY, w, h, rng);
            if (spec.corner != CornerStyle.None) DrawCorner(spec, pal, buf, mask, cutY, w, h);

            // 6) char the torn edge last so it sits on top of everything
            BurnEdge(buf, mask, cutY, w, h, dmgRng);

            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = buf[i];
            return px;
        }

        /// Render + wrap in a point-filtered sprite pivoted at its centre. Names carry kind/seed/stage for debugging.
        public static Sprite CreateSprite(WareSpec spec, int damageStage)
        {
            var px = Render(spec, damageStage, out int w, out int h);
            string id = $"Ware_{spec.kind}_{spec.seed}_dmg{damageStage}";
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = id
            };
            tex.SetPixels32(px);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), Mathf.Max(1f, spec.pixelsPerUnit));
            sprite.name = id;
            return sprite;
        }

        /// All damage stages, index 0 (intact) .. damageStages-1 (most wrecked).
        public static Sprite[] CreateAllStages(WareSpec spec)
        {
            int stages = Mathf.Clamp(spec.damageStages, 1, 4);
            var arr = new Sprite[stages];
            for (int s = 0; s < stages; s++) arr[s] = CreateSprite(spec, s);
            return arr;
        }

        /// Grab a handful of opaque pixel colours from a finished texture — the palette for a destruction burst, so the
        /// debris literally flies off in the Ware's own colours.
        public static List<Color32> SampleOpaqueColors(Texture2D tex, System.Random rng, int count)
        {
            var list = new List<Color32>(count);
            if (tex == null) return list;
            var all = tex.GetPixels32();
            if (all.Length == 0) return list;
            int guard = 0;
            while (list.Count < count && guard < count * 40)
            {
                guard++;
                var c = all[rng.Next(all.Length)];
                if (c.a > 40) list.Add(c);
            }
            if (list.Count == 0) list.Add(new Color32(200, 200, 200, 255)); // never hand back an empty palette
            return list;
        }

        // ── 1) silhouette ────────────────────────────────────────────────────────────────────────────

        static bool[] BuildMask(WareShape shape, int w, int h)
        {
            var m = new bool[w * h];
            float cx = w * 0.5f, cy = h * 0.5f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    bool inside;
                    switch (shape)
                    {
                        case WareShape.Rectangular:
                            inside = true;
                            break;
                        case WareShape.RoundedRect:
                            inside = RoundRect(fx, fy, w, h, Mathf.Min(w, h) * 0.18f);
                            break;
                        case WareShape.Round: // vertical stadium: fully rounded left & right — a can seen front
                            inside = RoundRect(fx, fy, w, h, Mathf.Min(w * 0.5f, h * 0.5f));
                            break;
                        default: // Spherical: ellipse filling the box
                            float nx = (fx - cx) / cx, ny = (fy - cy) / cy;
                            inside = nx * nx + ny * ny <= 1f;
                            break;
                    }
                    m[y * w + x] = inside;
                }
            return m;
        }

        // Signed rounded-rectangle test: quarter-circle corners of radius r.
        static bool RoundRect(float fx, float fy, int w, int h, float r)
        {
            r = Mathf.Max(0.5f, Mathf.Min(r, Mathf.Min(w, h) * 0.5f));
            float dx = Mathf.Max(Mathf.Abs(fx - w * 0.5f) - (w * 0.5f - r), 0f);
            float dy = Mathf.Max(Mathf.Abs(fy - h * 0.5f) - (h * 0.5f - r), 0f);
            return dx * dx + dy * dy <= r * r;
        }

        // ── 2) damage ──────────────────────────────────────────────────────────────────────────────

        // For stage k the top k/stages of the height is gone, with a jagged per-column edge so no two tears match.
        static int[] BuildCut(int stage, int stages, int w, int h, System.Random dmgRng)
        {
            var cut = new int[w];
            if (stage <= 0) { for (int x = 0; x < w; x++) cut[x] = h; return cut; } // intact: keep everything

            float frac = Mathf.Clamp01(stage / (float)stages);
            int baseRow = Mathf.Clamp(Mathf.RoundToInt(h * (1f - frac)), 1, h);
            int amp = Mathf.Max(1, Mathf.RoundToInt(h * 0.07f) + 1);

            // A little correlated noise (random walk clamped to ±amp) reads more like a torn edge than pure per-column hash.
            int walk = 0;
            for (int x = 0; x < w; x++)
            {
                walk += dmgRng.Next(-1, 2);
                walk = Mathf.Clamp(walk, -amp, amp);
                if (dmgRng.Next(6) == 0) walk = dmgRng.Next(-amp, amp + 1); // occasional sharp notch
                cut[x] = Mathf.Clamp(baseRow + walk, 1, h);
            }
            return cut;
        }

        // ── 3) body fill ─────────────────────────────────────────────────────────────────────────────

        static Color BodyColor(FillMode fill, in WarePalette pal, int x, int y, int w, int h)
        {
            switch (fill)
            {
                case FillMode.Gradient:
                    return Color.Lerp(pal.bodyDark, pal.bodyLight, y / Mathf.Max(1f, h - 1f)); // dark bottom → light top
                case FillMode.InnerGlow:
                    return Color.Lerp(pal.bodyLight, pal.bodyDark, EdgeFactor(x, y, w, h)); // bright core
                case FillMode.InnerShadow:
                    return Color.Lerp(pal.body, pal.bodyDark, EdgeFactor(x, y, w, h)); // dark rim
                default:
                    return pal.body;
            }
        }

        // 0 at the very centre, 1 at the silhouette edge (elliptical distance) — the shading ramp for glow/shadow.
        static float EdgeFactor(int x, int y, int w, int h)
        {
            float nx = (x + 0.5f - w * 0.5f) / (w * 0.5f);
            float ny = (y + 0.5f - h * 0.5f) / (h * 0.5f);
            return Mathf.Clamp01(Mathf.Sqrt(nx * nx + ny * ny));
        }

        // ── 4) per-kind identity ──────────────────────────────────────────────────────────────────────

        static void DrawKindDetails(WareSpec spec, in WarePalette pal, Color[] buf, bool[] mask, int[] cutY, int w, int h, System.Random rng)
        {
            switch (spec.kind)
            {
                case WareKind.Book: DrawBookSpine(pal, buf, mask, cutY, w, h); break;
                case WareKind.Can: DrawCanBody(pal, buf, mask, cutY, w, h); break;
                case WareKind.Crate: DrawCratePlanks(pal, buf, mask, cutY, w, h); break;
                case WareKind.Carton: DrawCartonSeam(pal, buf, mask, cutY, w, h); break;
                // Box has no extra identity mark — it's the plainest Ware, defined by its decoration.
            }
        }

        // A book seen spine-on: two dark ridges near top & bottom framing the "text block".
        static void DrawBookSpine(in WarePalette pal, Color[] buf, bool[] mask, int[] cutY, int w, int h)
        {
            int b1 = Mathf.RoundToInt(h * 0.16f), b2 = Mathf.RoundToInt(h * 0.84f);
            for (int band = 0; band < 2; band++)
            {
                int y0 = band == 0 ? b1 - 1 : b2 - 1;
                for (int dy = 0; dy < 2; dy++)
                    HLine(buf, mask, cutY, w, y0 + dy, 0, w, pal.bodyDark);
            }
        }

        // A can: a wide central wraparound label band + bright rims top & bottom.
        static void DrawCanBody(in WarePalette pal, Color[] buf, bool[] mask, int[] cutY, int w, int h)
        {
            int rim = Mathf.Max(1, Mathf.RoundToInt(h * 0.06f));
            for (int r = 0; r < rim; r++)
            {
                HLine(buf, mask, cutY, w, r, 0, w, pal.bodyDark);           // bottom rim
                HLine(buf, mask, cutY, w, h - 1 - r, 0, w, pal.bodyLight);  // top rim highlight
            }
            int by0 = Mathf.RoundToInt(h * 0.34f), by1 = Mathf.RoundToInt(h * 0.66f);
            for (int y = by0; y < by1; y++) HLine(buf, mask, cutY, w, y, 0, w, pal.accent);
        }

        // A crate: vertical plank gaps + a diagonal X-brace + nail dots in the corners.
        static void DrawCratePlanks(in WarePalette pal, Color[] buf, bool[] mask, int[] cutY, int w, int h)
        {
            int planks = Mathf.Max(2, Mathf.RoundToInt(w / 9f));
            for (int p = 1; p < planks; p++)
            {
                int x = Mathf.RoundToInt(p / (float)planks * w);
                VLine(buf, mask, cutY, w, h, x, 0, h, pal.bodyDark);
            }
            // X-brace across the whole face
            for (int x = 0; x < w; x++)
            {
                int ya = Mathf.RoundToInt(x / (float)(w - 1) * (h - 1));
                int yb = h - 1 - ya;
                Plot(buf, mask, cutY, w, x, ya, pal.bodyDark);
                Plot(buf, mask, cutY, w, x, yb, pal.bodyDark);
            }
            // nails
            int m = 2;
            NailDot(buf, mask, cutY, w, m, m, pal.bodyLight);
            NailDot(buf, mask, cutY, w, w - 1 - m, m, pal.bodyLight);
            NailDot(buf, mask, cutY, w, m, h - 1 - m, pal.bodyLight);
            NailDot(buf, mask, cutY, w, w - 1 - m, h - 1 - m, pal.bodyLight);
        }

        static void NailDot(Color[] buf, bool[] mask, int[] cutY, int w, int cx, int cy, Color c)
        {
            Plot(buf, mask, cutY, w, cx, cy, c);
        }

        // A carton: a vertical taped seam down the middle + a folded flap triangle at the top.
        static void DrawCartonSeam(in WarePalette pal, Color[] buf, bool[] mask, int[] cutY, int w, int h)
        {
            int seam = w / 2;
            for (int d = 0; d <= 1; d++)
                VLine(buf, mask, cutY, w, h, seam + d, 0, h, pal.bodyLight);
            int flapH = Mathf.Max(2, Mathf.RoundToInt(h * 0.18f));
            for (int y = 0; y < flapH; y++)
            {
                int row = h - 1 - y;
                int half = Mathf.RoundToInt((1f - y / (float)flapH) * w * 0.5f);
                for (int x = seam - half; x <= seam + half; x++) Plot(buf, mask, cutY, w, x, row, pal.bodyDark);
            }
        }

        // ── 5) generic decoration ───────────────────────────────────────────────────────────────────

        static void DrawBands(WareSpec spec, in WarePalette pal, Color[] buf, bool[] mask, int[] cutY, int w, int h)
        {
            int n = Mathf.Clamp(spec.bandCount, 1, 6);
            var c = pal.accent;
            switch (spec.bands)
            {
                case BandMode.Horizontal:
                    for (int i = 0; i < n; i++)
                    {
                        int y = Mathf.RoundToInt((i + 0.5f) / n * h);
                        HLine(buf, mask, cutY, w, y, 0, w, c);
                        HLine(buf, mask, cutY, w, y + 1, 0, w, c);
                    }
                    break;
                case BandMode.Vertical:
                    for (int i = 0; i < n; i++)
                    {
                        int x = Mathf.RoundToInt((i + 0.5f) / n * w);
                        VLine(buf, mask, cutY, w, h, x, 0, h, c);
                        VLine(buf, mask, cutY, w, h, x + 1, 0, h, c);
                    }
                    break;
                case BandMode.Diagonal:
                    float step = (w + h) / (float)(n + 1);
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            float d = (x + y) % step;
                            if (d < 2f) Plot(buf, mask, cutY, w, x, y, c);
                        }
                    break;
            }
        }

        static void DrawSpots(WareSpec spec, in WarePalette pal, Color[] buf, bool[] mask, int[] cutY, int w, int h, System.Random rng)
        {
            if (spec.spots == SpotMode.Circle)
            {
                Disc(buf, mask, cutY, w, h, w * 0.5f, h * 0.5f, Mathf.Min(w, h) * 0.22f, pal.accent);
                return;
            }
            int count = Mathf.Clamp(w * h / 90, 3, 14);
            for (int i = 0; i < count; i++)
            {
                float sx = rng.Next(w), sy = rng.Next(h);
                Disc(buf, mask, cutY, w, h, sx, sy, rng.Next(1, 3) + 0.5f, pal.accent);
            }
        }

        static void DrawLid(in WarePalette pal, Color[] buf, bool[] mask, int[] cutY, int w, int h)
        {
            int lidH = Mathf.Max(2, Mathf.RoundToInt(h * 0.14f));
            var lid = Color.Lerp(pal.bodyDark, Color.black, 0.35f);
            var shine = Color.Lerp(pal.bodyLight, Color.white, 0.3f);
            for (int y = h - lidH; y < h; y++)
                HLine(buf, mask, cutY, w, y, 0, w, y == h - 1 ? shine : lid);
        }

        static void DrawLabel(WareSpec spec, in WarePalette pal, Color[] buf, bool[] mask, int[] cutY, int w, int h, System.Random rng)
        {
            float frac = spec.label == LabelStyle.CenterPatch ? Mathf.Clamp(spec.labelWidth, 0.2f, 1f) : 0.82f;
            int patchW = Mathf.Max(4, Mathf.RoundToInt(w * frac));
            int patchH = Mathf.Max(4, Mathf.RoundToInt(h * 0.30f));
            int x0 = (w - patchW) / 2;
            int yc = h / 2;
            int y0 = yc - patchH / 2;
            bool diag = spec.label == LabelStyle.Diagonal;
            float slope = diag ? 0.4f : 0f;

            // paper
            for (int y = 0; y < patchH; y++)
            {
                int shift = Mathf.RoundToInt((y - patchH * 0.5f) * slope);
                for (int x = 0; x < patchW; x++)
                    Plot(buf, mask, cutY, w, x0 + x + shift, y0 + y, pal.label);
            }
            // a couple of ink "text" lines + a stray dot, so it reads as printing without spelling anything
            int lines = 2 + rng.Next(2);
            for (int l = 0; l < lines; l++)
            {
                int ly = y0 + Mathf.RoundToInt((l + 1) / (float)(lines + 1) * patchH);
                int inset = 2 + rng.Next(3);
                int len = patchW - inset * 2 - rng.Next(patchW / 3 + 1);
                int shift = Mathf.RoundToInt((ly - yc) * slope);
                for (int x = 0; x < len; x++) Plot(buf, mask, cutY, w, x0 + inset + x + shift, ly, pal.ink);
            }
            Plot(buf, mask, cutY, w, x0 + patchW - 3, y0 + patchH - 3, pal.ink);
        }

        static void DrawCorner(WareSpec spec, in WarePalette pal, Color[] buf, bool[] mask, int[] cutY, int w, int h)
        {
            int size = Mathf.Max(3, Mathf.RoundToInt(Mathf.Min(w, h) * 0.28f));
            switch (spec.corner)
            {
                case CornerStyle.ColoredTriangle: // accent flag, top-left
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size - y; x++)
                            Plot(buf, mask, cutY, w, x, h - 1 - y, pal.accent);
                    break;
                case CornerStyle.Rounded: // lighten a nibble, top-right
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size - y; x++)
                            Plot(buf, mask, cutY, w, w - 1 - x, h - 1 - y, Color.Lerp(pal.bodyLight, Color.white, 0.25f));
                    break;
                case CornerStyle.CutOff: // chop the top-right corner to transparent (respecting the tear)
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size - y; x++)
                        {
                            int px = w - 1 - x, py = h - 1 - y;
                            if (py < cutY[px]) buf[py * w + px] = Clear;
                        }
                    break;
            }
        }

        // ── 6) burn the torn edge ────────────────────────────────────────────────────────────────────

        static void BurnEdge(Color[] buf, bool[] mask, int[] cutY, int w, int h, System.Random dmgRng)
        {
            var burns = new[]
            {
                new Color(0.10f, 0.09f, 0.08f), new Color(0.16f, 0.11f, 0.07f),
                new Color(0.05f, 0.05f, 0.05f), new Color(0.22f, 0.14f, 0.08f),
            };
            for (int x = 0; x < w; x++)
            {
                int cut = cutY[x];
                if (cut >= h) continue; // this column wasn't torn
                var burn = burns[dmgRng.Next(burns.Length)];
                for (int d = 1; d <= 2; d++)
                {
                    int y = cut - d;
                    if (y < 0) break;
                    int idx = y * w + x;
                    if (!mask[idx] || buf[idx].a <= 0f) continue;
                    buf[idx] = Color.Lerp(buf[idx], burn, d == 1 ? 0.72f : 0.4f);
                }
            }
        }

        // ── low-level plotting (all clip to mask + the damage cut) ──────────────────────────────────

        static void Plot(Color[] buf, bool[] mask, int[] cutY, int w, int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= w) return;
            int idx = y * w + x;
            if (idx < 0 || idx >= buf.Length) return;
            if (!mask[idx] || y >= cutY[x]) return; // outside silhouette or above the tear
            buf[idx] = c;
        }

        static void HLine(Color[] buf, bool[] mask, int[] cutY, int w, int y, int x0, int x1, Color c)
        {
            for (int x = x0; x < x1; x++) Plot(buf, mask, cutY, w, x, y, c);
        }

        static void VLine(Color[] buf, bool[] mask, int[] cutY, int w, int h, int x, int y0, int y1, Color c)
        {
            for (int y = y0; y < y1; y++) Plot(buf, mask, cutY, w, x, y, c);
        }

        static void Disc(Color[] buf, bool[] mask, int[] cutY, int w, int h, float cx, float cy, float r, Color c)
        {
            int minx = Mathf.Max(0, Mathf.FloorToInt(cx - r)), maxx = Mathf.Min(w - 1, Mathf.CeilToInt(cx + r));
            int miny = Mathf.Max(0, Mathf.FloorToInt(cy - r)), maxy = Mathf.Min(h - 1, Mathf.CeilToInt(cy + r));
            float r2 = r * r;
            for (int y = miny; y <= maxy; y++)
                for (int x = minx; x <= maxx; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    if (dx * dx + dy * dy <= r2) Plot(buf, mask, cutY, w, x, y, c);
                }
        }
    }
}
