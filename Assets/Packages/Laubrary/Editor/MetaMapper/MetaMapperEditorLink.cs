using Laubrary.AssetKit.Editor;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Laubrary.MetaMapper.Editor
{
    /// Wires MetaMapper into the rest of the editor, in the one place that is allowed to know about both:
    ///   • <c>LauAssetEditors</c> Open/Create, so a MetaMap picked in ANY LauAsset field gets a working ✎ pen
    ///     and a New button (the <c>RegisterOpen</c> precedent, same shape as every other tool's *EditorLink);
    ///   • double-click-to-open in the Project window;
    ///   • <see cref="MetaMap.ThumbnailProvider"/> — the editor-side composite (subject WITH its markers over
    ///     it) that the runtime class cannot build alone, because resolving a subject needs the provider
    ///     registry and the registry is editor-only by nature.
    [InitializeOnLoad]
    static class MetaMapperEditorLink
    {
        static MetaMapperEditorLink()
        {
            LauAssetEditors.RegisterOpen<MetaMap>(m => MetaMapperWindow.OpenFor(m));
            LauAssetEditors.RegisterCreate<MetaMap>((name, folder) => AssetLibrary<MetaMap>.Create(name, folder));
            MetaMap.ThumbnailProvider = Compose;
            MetaSubjectProviders.EnsureBuiltIns();
        }

        [OnOpenAsset]
        static bool OnOpen(int instanceId, int line)
        {
            // EntityIdToObject, not the obsolete InstanceIDToObject: EntityId converts implicitly from the
            // int this callback is still handed, so the modern API takes it unchanged.
            var map = EditorUtility.EntityIdToObject(instanceId) as MetaMap;
            if (map == null) return false;
            MetaMapperWindow.OpenFor(map);
            return true;
        }

        const int MinThumb = 96;

        /// The subject's frame 0 with the map's markers drawn over it. Null means "no subject I can resolve" —
        /// the caller falls back to MetaMap's own marker-only card, which is deliberately still informative
        /// (it draws a colour bar per declared layer).
        static Texture2D Compose(MetaMap map)
        {
            if (map == null) return null;
            var visual = MetaSubjectProviders.Resolve(map.subject);
            if (visual == null) return null;

            try
            {
                var src = visual.FrameAt(0);
                if (src == null) return null;

                Color32[] srcPx;
                try { srcPx = src.GetPixels32(); }
                catch { return null; }   // a provider handed back a non-readable texture; not our thumbnail to draw

                int w = src.width, h = src.height;
                int zoom = Mathf.Clamp(Mathf.FloorToInt(MinThumb / (float)Mathf.Max(w, h)), 1, 8);
                int ow = w * zoom, oh = h * zoom;

                var outPx = new Color32[ow * oh];
                for (int y = 0; y < oh; y++)
                    for (int x = 0; x < ow; x++)
                        outPx[y * ow + x] = srcPx[(y / zoom) * w + (x / zoom)];

                DrawMarkers(map.Data, visual, outPx, ow, oh, zoom);

                var tex = new Texture2D(ow, oh, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
                tex.SetPixels32(outPx);
                tex.Apply();
                return tex;
            }
            finally { visual.Dispose(); }
        }

        static void DrawMarkers(MetaMapData d, MetaSubjectVisual visual, Color32[] px, int ow, int oh, int zoom)
        {
            if (d?.layers == null) return;
            for (int li = 0; li < d.layers.Count; li++)
            {
                var L = d.layers[li];
                var e = L?.EntryAt(0);
                if (e == null) continue;

                if (L.kind == LayerKind.Mask && e.MaskUsable)
                {
                    // Mask cell (0,0) is the FOOTPRINT's corner, not map (0,0) — the clump case, where the two
                    // differ by the whole height.
                    Vector2 cs = d.MaskCellToMapScale(e);
                    Vector2 fm = d.footprintMin;
                    for (int y = 0; y < e.maskH; y++)
                        for (int x = 0; x < e.maskW; x++)
                        {
                            int v = e.MaskGet(x, y);
                            if (v <= 0) continue;
                            var lo = visual.MapToTexture(fm + new Vector2(x * cs.x, y * cs.y)) * zoom;
                            var hi = visual.MapToTexture(fm + new Vector2((x + 1) * cs.x, (y + 1) * cs.y)) * zoom;
                            Blend(px, ow, oh, lo, hi, MetaPalette.CellColor(L.color, v), 0.55f);
                        }
                }

                if (L.kind == LayerKind.Shapes && e.shapes != null)
                    foreach (var sh in e.shapes)
                    {
                        if (sh == null) continue;
                        var lo = visual.MapToTexture(sh.center - sh.size * 0.5f) * zoom;
                        var hi = visual.MapToTexture(sh.center + sh.size * 0.5f) * zoom;
                        if (sh.kind == ShapeKind.Circle)
                            BlendDisc(px, ow, oh, (lo + hi) * 0.5f, Mathf.Abs(hi.x - lo.x) * 0.5f, L.color, 0.45f);
                        else
                            Blend(px, ow, oh, lo, hi, L.color, 0.45f);
                    }

                if (e.marks == null) continue;
                for (int i = 0; i < e.marks.Count; i++)
                {
                    var m = e.marks[i];
                    if (m == null) continue;
                    // THE AFFINE, unchanged: texturePx = originPx + mapPos × pixelsPerUnit, +y up on both
                    // sides. The only flip is the row index below, because a Color32[] counts rows from 0 at
                    // the BOTTOM — which happens to match, so there is nothing to invert here at all.
                    var t = visual.MapToTexture(m.pos) * zoom;
                    int r = Mathf.Max(1, zoom);
                    Blend(px, ow, oh, new Vector2(t.x - r - 1, t.y - r - 1), new Vector2(t.x + r + 1, t.y + r + 1),
                        new Color(0f, 0f, 0f, 1f), 0.7f);
                    Blend(px, ow, oh, new Vector2(t.x - r, t.y - r), new Vector2(t.x + r, t.y + r), L.color, 1f);
                }
            }
        }

        static void BlendDisc(Color32[] px, int w, int h, Vector2 c, float r, Color color, float alpha)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(c.x - r), 0, w), x1 = Mathf.Clamp(Mathf.CeilToInt(c.x + r), 0, w);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(c.y - r), 0, h), y1 = Mathf.Clamp(Mathf.CeilToInt(c.y + r), 0, h);
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    float dx = x + 0.5f - c.x, dy = y + 0.5f - c.y;
                    if (dx * dx + dy * dy <= r * r)
                        Blend(px, w, h, new Vector2(x, y), new Vector2(x + 1, y + 1), color, alpha);
                }
        }

        static void Blend(Color32[] px, int w, int h, Vector2 lo, Vector2 hi, Color color, float alpha)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(lo.x, hi.x)), 0, w);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(lo.x, hi.x)), 0, w);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(lo.y, hi.y)), 0, h);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(lo.y, hi.y)), 0, h);
            float a = Mathf.Clamp01(alpha * color.a);
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    int i = y * w + x;
                    Color under = px[i];
                    var over = Color.Lerp(under, new Color(color.r, color.g, color.b, 1f), a);
                    over.a = Mathf.Max(under.a, a);
                    px[i] = over;
                }
        }
    }
}
