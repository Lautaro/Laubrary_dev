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
            if (blocks == null || blocks.Count == 0) return null;
            var names = new string[blocks.Count];
            for (int i = 0; i < blocks.Count; i++) names[i] = blocks.Count == 1 ? baseName : $"{baseName}_{i + 1}";
            return AddCells(set, cellSize, blocks, names);
        }

        /// Per-block-names overload: one gesture minting cells for SEVERAL differently-named tiles (fork
        /// batches) can still collapse into a single file write + import. Each wanted name is unique-ified
        /// against the atlas exactly like the baseName path.
        public static Sprite[] AddCells(Tileset set, int cellSize, List<Color32[]> blocks, IReadOnlyList<string> blockNames)
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

                string wanted = blockNames[i];
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
            if (fresh) AssetDatabase.ImportAsset(path);
            EnsureImportSettings(path, cellSize);

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

        /// The import settings an atlas MUST have, applied on every write rather than only at creation.
        ///
        /// ☠️ ONE TILE IS ONE CELL. That is the whole contract of a tileset, and the thing that enforces it
        /// is `spritePixelsPerUnit == cellSize`: a 32-pixel tile at PPU 32 is one unit wide, exactly like a
        /// 16-pixel tile at PPU 16. Get it wrong and the tile does not become blurry — it becomes the WRONG
        /// SIZE. A 32px atlas left at PPU 16 draws every tile two units across, so it visually covers four
        /// cells while only ONE of them is painted: the other three look filled, erase nothing, and cannot
        /// be painted over. Reported 2026-08-03 as "when i paint a 32x tile it makes 4 tiles, but only the
        /// bottom left is erasable… 16x tiles work great". Resolution is a quality choice; footprint is not.
        ///
        /// These were previously applied only when the atlas was CREATED, so an atlas that existed before
        /// the rule — or was created at another cell size — kept the wrong PPU forever with no way to notice.
        /// Applying them on every write is self-healing and costs an import only when something differs.
        ///
        /// FullRect, not Tight: a tight mesh makes `Sprite.textureRect` the outline's bounding box rather
        /// than the declared slice, which is how partially-filled tiles ended up mis-placed in every preview
        /// (see CartographerPreview.Blit). A grid tile is a rectangle by definition.
        public static void EnsureImportSettings(string path, int cellSize)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter imp) return;

            // ⚠️ ORDER MATTERS. TextureImporterSettings is a SNAPSHOT of nearly everything, spritePixelsPerUnit
            // included, so `SetTextureSettings(read-before-you-changed-it)` silently reverts any property you
            // had already written on the importer. That is not hypothetical: it ate the PPU fix on the first
            // attempt and reported success. Everything that lives on the settings object is therefore set ON
            // the settings object, written ONCE, and only then are the importer-only properties touched.
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);

            bool changed = false;
            void Need(bool ok, System.Action fix) { if (!ok) { fix(); changed = true; } }

            bool settingsDirty = false;
            if (settings.spriteMeshType != SpriteMeshType.FullRect) { settings.spriteMeshType = SpriteMeshType.FullRect; settingsDirty = true; }

            // ☠️ ONE TILE IS ONE CELL, and PPU == cellSize is what enforces it: a 32-pixel tile at PPU 32 is
            // one unit wide, exactly like a 16-pixel tile at PPU 16. Get this wrong and a tile does not
            // become blurry, it becomes the WRONG SIZE — a 32px atlas left at PPU 16 draws every tile two
            // units across, covering four grid cells while only ONE is painted, so three of them look filled
            // but erase nothing and cannot be painted over (reported 2026-08-03).
            //
            // ⚠️ A host project with a global pixel-art import policy will FIGHT this, because such a policy
            // is an AssetPostprocessor and reverts whatever is written here on the same reimport — silently,
            // reporting success. If tiles come out the wrong size, look for that policy first and give the
            // atlas an exemption there; the file name carries the cell size precisely so a policy can.
            if (!Mathf.Approximately(settings.spritePixelsPerUnit, cellSize)) { settings.spritePixelsPerUnit = cellSize; settingsDirty = true; }
            if (settings.textureType != TextureImporterType.Sprite) { settings.textureType = TextureImporterType.Sprite; settingsDirty = true; }
            if (settings.spriteMode != (int)SpriteImportMode.Multiple) { settings.spriteMode = (int)SpriteImportMode.Multiple; settingsDirty = true; }
            if (settings.mipmapEnabled) { settings.mipmapEnabled = false; settingsDirty = true; }
            if (settings.filterMode != FilterMode.Point) { settings.filterMode = FilterMode.Point; settingsDirty = true; }
            if (settingsDirty) { imp.SetTextureSettings(settings); changed = true; }

            // Importer-only, so safe to write after the snapshot.
            Need(imp.textureCompression == TextureImporterCompression.Uncompressed,
                 () => imp.textureCompression = TextureImporterCompression.Uncompressed);
            Need(imp.maxTextureSize >= 16384, () => imp.maxTextureSize = 16384);

            if (changed) imp.SaveAndReimport();
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

        /// Read sprites' pixel blocks back OUT of their texture files — the fork-side complement of
        /// AddCells. File bytes via LoadImage (FlipCells' pattern), so import settings never constrain the
        /// read — and the sprite does NOT need to live on a tileset's atlas: a legacy sheet-resident
        /// sprite reads from its own texture's file, which is how forking also turns a legacy tile
        /// atlas-native. One decode per texture file, however many sprites. Returns blocks parallel to
        /// `sprites` (bottom-up rows, AddCells-ready); a slot is null — and counted in `skipped` — only
        /// when its source file is genuinely unreadable or the rect falls outside the decoded file.
        public static Color32[][] ReadCells(IReadOnlyList<Sprite> sprites, out int skipped)
        {
            skipped = 0;
            var result = new Color32[sprites?.Count ?? 0][];
            if (sprites == null || sprites.Count == 0) return result;

            var byPath = new Dictionary<string, List<int>>();
            for (int i = 0; i < sprites.Count; i++)
            {
                var s = sprites[i];
                string path = s != null && s.texture != null ? AssetDatabase.GetAssetPath(s.texture) : null;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) { skipped++; continue; }
                if (!byPath.TryGetValue(path, out var list)) byPath[path] = list = new List<int>();
                list.Add(i);
            }

            foreach (var kv in byPath)
            {
                var work = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                if (!work.LoadImage(File.ReadAllBytes(kv.Key)))
                {
                    Object.DestroyImmediate(work);
                    skipped += kv.Value.Count;
                    continue;
                }
                var px = work.GetPixels32();
                foreach (int i in kv.Value)
                {
                    var r = sprites[i].rect;
                    int x0 = Mathf.RoundToInt(r.x), y0 = Mathf.RoundToInt(r.y);
                    int bw = Mathf.RoundToInt(r.width), bh = Mathf.RoundToInt(r.height);
                    if (bw <= 0 || bh <= 0 || x0 < 0 || y0 < 0 || x0 + bw > work.width || y0 + bh > work.height)
                    {
                        skipped++;
                        continue;
                    }
                    var block = new Color32[bw * bh];
                    for (int y = 0; y < bh; y++)
                        System.Array.Copy(px, (y0 + y) * work.width + x0, block, y * bw, bw);
                    result[i] = block;
                }
                Object.DestroyImmediate(work);
            }
            return result;
        }

        /// True when this sprite's pixels live on one of `set`'s own atlas textures — i.e. FlipCells may
        /// rewrite them. Legacy pre-pluck tiles still reference sub-sprites of a SOURCE sheet, which the
        /// tileset does not own and must never vandalise.
        public static bool OwnsSprite(Tileset set, Sprite s) =>
            s != null && s.texture != null && IsAtlasOf(set, AssetDatabase.GetAssetPath(s.texture));

        /// Mirror the given sprites' pixel blocks IN PLACE inside the tileset's atlas file(s), horizontally
        /// or vertically. Sprites not on one of this tileset's atlases (see OwnsSprite) are skipped and
        /// counted, never touched. ONE file write + ONE reimport per atlas file per call, however many
        /// sprites. Rects and spriteIDs are untouched — only pixels move, so every existing reference
        /// (tiles, painted levels) simply shows the flip. A PNG rewrite sits outside Unity's Undo; the
        /// operation is exactly SELF-INVERSE instead — flipping again restores the original bytes.
        /// Returns the number of sprites flipped.
        public static int FlipCells(Tileset set, IEnumerable<Sprite> sprites, bool horizontal, out int skipped)
        {
            skipped = 0;
            if (set == null || sprites == null) return 0;

            // Group by atlas file — a tileset owns one atlas per cell size, and each file gets one write.
            // Dedupe first: an animated tile's variants[0] IS its animation[0], and flipping the same
            // sprite twice would be a silent no-op.
            var byPath = new Dictionary<string, List<Sprite>>();
            var seen = new HashSet<Sprite>();
            foreach (var s in sprites)
            {
                if (s == null || !seen.Add(s)) continue;
                string path = s.texture != null ? AssetDatabase.GetAssetPath(s.texture) : null;
                if (!IsAtlasOf(set, path) || !File.Exists(path)) { skipped++; continue; }
                if (!byPath.TryGetValue(path, out var list)) byPath[path] = list = new List<Sprite>();
                list.Add(s);
            }

            int flipped = 0;
            foreach (var kv in byPath)
            {
                var work = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                if (!work.LoadImage(File.ReadAllBytes(kv.Key)))
                {
                    Object.DestroyImmediate(work);
                    skipped += kv.Value.Count;
                    continue;
                }

                var px = work.GetPixels32();
                foreach (var s in kv.Value)
                {
                    var r = s.rect;
                    int x0 = Mathf.RoundToInt(r.x), y0 = Mathf.RoundToInt(r.y);
                    int bw = Mathf.RoundToInt(r.width), bh = Mathf.RoundToInt(r.height);
                    if (x0 < 0 || y0 < 0 || x0 + bw > work.width || y0 + bh > work.height) { skipped++; continue; }
                    MirrorBlock(px, work.width, x0, y0, bw, bh, horizontal);
                    flipped++;
                }
                work.SetPixels32(px);
                File.WriteAllBytes(kv.Key, work.EncodeToPNG());
                Object.DestroyImmediate(work);
                AssetDatabase.ImportAsset(kv.Key, ImportAssetOptions.ForceUpdate);
            }
            return flipped;
        }

        /// Mirror one rect of a bottom-up pixel array in place. Pure swaps — applying it twice restores
        /// the original array exactly, which is what makes FlipCells its own undo.
        static void MirrorBlock(Color32[] px, int texW, int x0, int y0, int bw, int bh, bool horizontal)
        {
            if (horizontal)
                for (int y = 0; y < bh; y++)
                {
                    int row = (y0 + y) * texW + x0;
                    for (int x = 0; x < bw / 2; x++)
                        (px[row + x], px[row + bw - 1 - x]) = (px[row + bw - 1 - x], px[row + x]);
                }
            else
                for (int y = 0; y < bh / 2; y++)
                {
                    int a = (y0 + y) * texW + x0, b = (y0 + bh - 1 - y) * texW + x0;
                    for (int x = 0; x < bw; x++)
                        (px[a + x], px[b + x]) = (px[b + x], px[a + x]);
                }
        }
    }
}
