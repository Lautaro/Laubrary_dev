using System.Collections.Generic;
using System.IO;
using Laubrary.Launimator;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// Packs a reel version's per-animation baked frames into ONE shared, readable atlas containing only
    /// the unique frames the reel actually uses (identical frames — same pixels, size and pivot — are
    /// stored once and referenced by every animation that needs them). Run after <see cref="AtlasBaker"/> has
    /// produced each animation's frames: this reads those frames' pixels, dedups, shelf-packs them, and
    /// repoints every <see cref="AnimationDef.frames"/> at the packed sprites. The result is a single texture
    /// per version — fewer draw-call switches when a sprite-swap player flips frames, smaller on disk, and one
    /// place for pixel-reading consumers (e.g. procedural destruction) to sample. The atlas is import-flagged
    /// readable so those consumers can call GetPixels.
    /// </summary>
    public static class ReelAtlasPacker
    {
        private const int Padding = 1;          // transparent gutter between cells
        private const int MaxRowWidth = 2048;   // wrap to a new shelf past this width

        /// <summary>
        /// Pack <paramref name="animations"/>' baked frames into <paramref name="atlasAssetPath"/>, repoint
        /// each animation's frames at the packed sprites, and return the packed atlas texture (null on
        /// failure, with <paramref name="error"/> set). Each animation must already have its <c>frames</c>
        /// populated by <see cref="AtlasBaker"/>.
        /// </summary>
        public static Texture2D Pack(
            List<AnimationDef> animations, string atlasAssetPath, float ppu, out string error)
        {
            error = null;

            // 1) Gather every frame as a pixel block + its normalized pivot; dedup identical ones.
            var unique = new List<Cell>();
            var bySignature = new Dictionary<string, int>();          // signature -> index into 'unique'
            var srcCache = new Dictionary<Texture2D, Color32[]>();    // whole-texture pixel cache
            var frameToCell = new Dictionary<(AnimationDef, int), int>();

            foreach (var def in animations)
            {
                if (def?.frames == null) continue;
                for (int i = 0; i < def.frames.Count; i++)
                {
                    var spr = def.frames[i];
                    if (spr == null) { error = $"'{def.name}' frame {i} is null"; return null; }
                    if (!TryReadBlock(spr, srcCache, out var px, out int w, out int h, out var pivot))
                    {
                        error = $"'{def.name}' frame {i}: source atlas unreadable";
                        return null;
                    }

                    string sig = Signature(px, w, h, pivot);
                    if (!bySignature.TryGetValue(sig, out int idx))
                    {
                        idx = unique.Count;
                        bySignature[sig] = idx;
                        unique.Add(new Cell { px = px, w = w, h = h, pivot = pivot });
                    }
                    frameToCell[(def, i)] = idx;
                }
            }
            if (unique.Count == 0) { error = "no frames to pack"; return null; }

            // 2) Shelf-pack the unique cells (sorted tallest-first for tighter shelves).
            var order = new List<int>(unique.Count);
            for (int i = 0; i < unique.Count; i++) order.Add(i);
            order.Sort((a, b) => unique[b].h.CompareTo(unique[a].h));

            int penX = Padding, penY = Padding, shelfH = 0, atlasW = 0;
            foreach (int i in order)
            {
                var c = unique[i];
                if (penX + c.w + Padding > MaxRowWidth && penX > Padding)
                {
                    penX = Padding;
                    penY += shelfH + Padding;
                    shelfH = 0;
                }
                unique[i] = new Cell { px = c.px, w = c.w, h = c.h, pivot = c.pivot, x = penX, y = penY };
                penX += c.w + Padding;
                shelfH = Mathf.Max(shelfH, c.h);
                atlasW = Mathf.Max(atlasW, penX);
            }
            int atlasH = penY + shelfH + Padding;

            // 3) Compose the atlas pixels (bottom-left origin, row-major — matches Get/SetPixels32).
            var atlas = new Color32[atlasW * atlasH]; // transparent by default
            foreach (var c in unique)
                for (int y = 0; y < c.h; y++)
                    for (int x = 0; x < c.w; x++)
                        atlas[(c.y + y) * atlasW + (c.x + x)] = c.px[y * c.w + x];

            // 4) Write + import the PNG.
            var tex = new Texture2D(atlasW, atlasH, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(atlas);
                tex.Apply();
                string sysPath = ToSystemPath(atlasAssetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(sysPath));
                File.WriteAllBytes(sysPath, tex.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(tex); }
            AssetDatabase.ImportAsset(atlasAssetPath, ImportAssetOptions.ForceSynchronousImport);

            // 5) Slice into one sprite per unique cell (stable index names + per-cell pivots).
            var rects = new List<Rect>(unique.Count);
            var pivots = new List<Vector2>(unique.Count);
            var names = new List<string>(unique.Count);
            for (int i = 0; i < unique.Count; i++)
            {
                var c = unique[i];
                rects.Add(new Rect(c.x, c.y, c.w, c.h));
                pivots.Add(c.pivot);
                names.Add($"cell_{i:000}");
            }
            RegionSlicer.Apply(atlasAssetPath, rects, pivots, ppu, idx => names[idx], new Vector2(0.5f, 0f));

            // 6) Resolve packed sprites by name and repoint every animation's frames at them.
            var byName = new Dictionary<string, Sprite>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(atlasAssetPath))
                if (o is Sprite sp) byName[sp.name] = sp;

            var packedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasAssetPath);
            foreach (var def in animations)
            {
                if (def?.frames == null) continue;
                for (int i = 0; i < def.frames.Count; i++)
                {
                    int cellIdx = frameToCell[(def, i)];
                    def.frames[i] = byName.TryGetValue($"cell_{cellIdx:000}", out var s) ? s : null;
                }
                def.atlas = packedTex;
            }
            return packedTex;
        }

        private struct Cell
        {
            public Color32[] px;
            public int w, h, x, y;
            public Vector2 pivot;
        }

        private static bool TryReadBlock(
            Sprite spr, Dictionary<Texture2D, Color32[]> cache,
            out Color32[] block, out int w, out int h, out Vector2 pivot)
        {
            block = null; w = h = 0; pivot = new Vector2(0.5f, 0f);
            var tex = spr.texture;
            if (tex == null) return false;

            if (!cache.TryGetValue(tex, out var all))
            {
                try { all = tex.GetPixels32(); } catch { return false; }
                cache[tex] = all;
            }

            // Use the FULL sprite rect, not textureRect: the baker pads each frame with transparency so its
            // registration pivot sits at a uniform point, and that padding IS the alignment. textureRect is the
            // tight (trimmed) content box under the default Tight mesh, which would drop the padding and shift
            // every frame by its own margin — the classic playback "wobble".
            Rect r = spr.rect;
            int rx = Mathf.RoundToInt(r.x), ry = Mathf.RoundToInt(r.y);
            w = Mathf.RoundToInt(r.width); h = Mathf.RoundToInt(r.height);
            if (w <= 0 || h <= 0) return false;
            if (rx < 0 || ry < 0 || rx + w > tex.width || ry + h > tex.height) return false;

            block = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    block[y * w + x] = all[(ry + y) * tex.width + (rx + x)];

            // Sprite.pivot is in pixels relative to the rect's bottom-left (the same full rect read above).
            pivot = new Vector2(spr.pivot.x / w, spr.pivot.y / h);
            return true;
        }

        private static string Signature(Color32[] px, int w, int h, Vector2 pivot)
        {
            // FNV-1a over dims + pivot + pixels. Strong enough that a full-byte tiebreak is unnecessary here.
            ulong hash = 1469598103934665603UL;
            void Mix(int v) { hash = (hash ^ (byte)v) * 1099511628211UL; hash = (hash ^ (byte)(v >> 8)) * 1099511628211UL; }
            Mix(w); Mix(h);
            Mix(Mathf.RoundToInt(pivot.x * 1000f)); Mix(Mathf.RoundToInt(pivot.y * 1000f));
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                hash = (hash ^ c.r) * 1099511628211UL;
                hash = (hash ^ c.g) * 1099511628211UL;
                hash = (hash ^ c.b) * 1099511628211UL;
                hash = (hash ^ c.a) * 1099511628211UL;
            }
            return $"{w}x{h}:{hash:x}";
        }

        private static string ToSystemPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }
    }
}
