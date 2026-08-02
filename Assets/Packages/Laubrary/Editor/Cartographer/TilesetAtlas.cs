using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Laubrary.Cartographer.Editor
{
    /// The PLUCK target: each tileset owns a small atlas PNG per cell size, holding ONLY curated art.
    /// Source sheets are never sliced and never have to live in the project — the builder reads their
    /// pixels and appends the chosen cells here. One shared texture per tileset (rather than one PNG per
    /// tile) keeps a painted level batchable at runtime.
    ///
    /// Append-only by design: deleting a tile leaves its atlas cell orphaned rather than risking another
    /// tile's coordinates. Orphans cost a few transparent pixels until a future compaction pass; broken
    /// references would cost trust.
    internal static class TilesetAtlas
    {
        const int Columns = 16;

        static string PathOf(Tileset set, int cellSize)
        {
            string dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(set))?.Replace('\\', '/');
            return $"{dir}/{set.name} Atlas {cellSize}.png";
        }

        /// True when `texturePath` is an atlas belonging to `set` — used to keep sheet-recovery heuristics
        /// from mistaking a tileset's own atlas for the source sheet it was plucked from.
        public static bool IsAtlasOf(Tileset set, string texturePath)
        {
            if (set == null || string.IsNullOrEmpty(texturePath)) return false;
            string name = Path.GetFileName(texturePath);
            return name.StartsWith(set.name + " Atlas ", System.StringComparison.Ordinal);
        }

        /// Append pixel blocks (cellSize² each, bottom-up as GetPixels32 hands them out) as new sprites.
        /// ONE file write and ONE import per call, however many cells — minting an 8-frame animation must
        /// not cost 8 imports. Returns the created sprites in block order; null when the tileset has no
        /// asset path yet or the atlas file is unreadable.
        public static Sprite[] AddCells(Tileset set, int cellSize, List<Color32[]> blocks, string baseName)
        {
            if (set == null || blocks == null || blocks.Count == 0 || cellSize <= 0) return null;
            string path = PathOf(set, cellSize);
            if (path.StartsWith("/")) return null;   // tileset not saved to disk yet

            // The working image comes from the FILE bytes, not the imported texture — LoadImage always
            // yields readable pixels, so the asset's import settings never constrain this writer.
            var work = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            bool fresh = !File.Exists(path);
            if (fresh)
            {
                work.Reinitialize(Columns * cellSize, cellSize);
                work.SetPixels32(new Color32[work.width * work.height]);
            }
            else if (!work.LoadImage(File.ReadAllBytes(path)))
            {
                Object.DestroyImmediate(work);
                return null;
            }

            // Existing rects are occupancy AND identity — they must come through any growth byte-identical
            // in pixels and untouched in name/spriteID, or every tile already plucked breaks.
            var rects = new List<SpriteRect>();
            if (!fresh && AssetImporter.GetAtPath(path) is TextureImporter existingImporter)
            {
                var f = new SpriteDataProviderFactories();
                f.Init();
                var p = f.GetSpriteEditorDataProviderFromObject(existingImporter);
                p.InitSpriteEditorDataProvider();
                rects.AddRange(p.GetSpriteRects());
            }

            int next = 0;
            foreach (var r in rects)
            {
                int rowTop = (work.height - (int)r.rect.y - cellSize) / cellSize;
                int idx = rowTop * Columns + (int)r.rect.x / cellSize;
                next = Mathf.Max(next, idx + 1);
            }

            // Grow DOWNWARD with old content anchored at the top. In bottom-up texture space that means
            // old pixels and old rects both shift up by the added height — coordinates move, identity stays.
            int rowsNeeded = Mathf.CeilToInt((next + blocks.Count) / (float)Columns);
            int newH = Mathf.Max(work.height, rowsNeeded * cellSize);
            if (newH > work.height)
            {
                int delta = newH - work.height;
                var grown = new Texture2D(Columns * cellSize, newH, TextureFormat.RGBA32, false)
                    { hideFlags = HideFlags.HideAndDontSave };
                grown.SetPixels32(new Color32[grown.width * grown.height]);
                grown.SetPixels32(0, delta, work.width, work.height, work.GetPixels32());
                Object.DestroyImmediate(work);
                work = grown;
                foreach (var r in rects) { var rr = r.rect; rr.y += delta; r.rect = rr; }
            }

            var names = new HashSet<string>();
            foreach (var r in rects) names.Add(r.name);

            var made = new List<SpriteRect>();
            for (int i = 0; i < blocks.Count; i++)
            {
                int idx = next + i;
                int px = idx % Columns * cellSize;
                int py = work.height - (idx / Columns + 1) * cellSize;
                work.SetPixels32(px, py, cellSize, cellSize, blocks[i]);

                string wanted = blocks.Count == 1 ? baseName : $"{baseName}_{i + 1}";
                string unique = wanted;
                for (int bump = 2; !names.Add(unique); bump++) unique = $"{wanted}_{bump}";

                var sr = new SpriteRect
                {
                    name = unique,
                    rect = new Rect(px, py, cellSize, cellSize),
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    spriteID = GUID.Generate(),
                };
                rects.Add(sr);
                made.Add(sr);
            }

            File.WriteAllBytes(path, work.EncodeToPNG());
            Object.DestroyImmediate(work);

            // A fresh atlas needs to exist as an asset (and carry pixel-art-correct settings) before sprite
            // rects can be authored onto it. Direct importer setting writes and provider writes go through
            // separate serialization, so they get separate imports — only on creation, where it is cheap.
            if (fresh)
            {
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Multiple;
                imp.spritePixelsPerUnit = cellSize;   // one tile = one level cell; a host project's import policy may re-law this
                imp.filterMode = FilterMode.Point;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.mipmapEnabled = false;
                imp.maxTextureSize = 16384;
                imp.SaveAndReimport();
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            provider.SetSpriteRects(rects.ToArray());
            var nameId = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (nameId != null)
            {
                var pairs = new List<SpriteNameFileIdPair>();
                foreach (var sr in rects) pairs.Add(new SpriteNameFileIdPair(sr.name, sr.spriteID));
                nameId.SetNameFileIdPairs(pairs);
            }
            provider.Apply();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var byName = new Dictionary<string, Sprite>();
            foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                if (o is Sprite s) byName[s.name] = s;
            var result = new Sprite[made.Count];
            for (int i = 0; i < made.Count; i++) byName.TryGetValue(made[i].name, out result[i]);
            return result;
        }

        /// Undo a just-made AddCells: erase those sprites' rects and pixels again. Exists so a blocked
        /// drag-drop mint can revert ATOMICALLY — matching by name is safe because names are unique per atlas.
        public static void RemoveCells(Tileset set, int cellSize, IEnumerable<Sprite> sprites)
        {
            if (set == null || sprites == null) return;
            string path = PathOf(set, cellSize);
            if (!File.Exists(path) || AssetImporter.GetAtPath(path) is not TextureImporter importer) return;

            var doomed = new HashSet<string>();
            foreach (var s in sprites) if (s != null) doomed.Add(s.name);
            if (doomed.Count == 0) return;

            var work = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            if (!work.LoadImage(File.ReadAllBytes(path))) { Object.DestroyImmediate(work); return; }

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();

            var keep = new List<SpriteRect>();
            var clear = new Color32[cellSize * cellSize];
            foreach (var r in provider.GetSpriteRects())
            {
                if (!doomed.Contains(r.name)) { keep.Add(r); continue; }
                work.SetPixels32((int)r.rect.x, (int)r.rect.y, cellSize, cellSize, clear);
            }

            File.WriteAllBytes(path, work.EncodeToPNG());
            Object.DestroyImmediate(work);

            provider.SetSpriteRects(keep.ToArray());
            var nameId = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (nameId != null)
            {
                var pairs = new List<SpriteNameFileIdPair>();
                foreach (var sr in keep) pairs.Add(new SpriteNameFileIdPair(sr.name, sr.spriteID));
                nameId.SetNameFileIdPairs(pairs);
            }
            provider.Apply();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
    }
}
