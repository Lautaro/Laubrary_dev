using System.Collections.Generic;
using System.IO;
using System.Linq;
using Laubrary.Launimator;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// Makes a lauminary version SELF-CONTAINED: the moment a sprite cell is used in an animation, its pixels are
    /// extracted into a project-owned source texture INSIDE the version's own <c>Source/</c> folder, and the
    /// recipe is re-pointed at that owned source. After that the original rip sheet is a re-discovery convenience
    /// only — delete it and the lauminary still edits, re-bakes, commits, and travels with its folder.
    ///
    /// This is "detach-on-use" (ASEPRITE_PLAN.md → Self-containment, Option B, Phase 1). It is the PNG baseline:
    /// no schema change, no new bake path — the rewritten recipe still feeds <see cref="AtlasBaker"/> unchanged.
    /// The background colour key is baked to real alpha during extraction (the owned source is clean RGBA), so
    /// the per-animation <c>bgKey</c> is cleared.
    ///
    /// Idempotent: a frame already sourced from this version's own <c>Source/</c> folder is "internal" and is
    /// simply re-packed; if NOTHING is external, the pass is a no-op. The same machinery re-owns a committed
    /// snapshot from the draft's source (the draft's <c>Source/</c> is "external" to <c>vN/</c>).
    /// </summary>
    public static class LauminarySources
    {
        // Matches the importer cap set in ConfigureSource, so a packed sheet always imports at full size.
        private const int MaxSourceRowWidth = 8192;

        /// <summary>The owned source texture for a version, holding every cell its animations use.</summary>
        public static string SourceFolder(string versionFolder) => $"{versionFolder}/Source";
        public static string SourcePath(string versionFolder, string lauminaryName)
            => $"{SourceFolder(versionFolder)}/{LauminaryBuilder.Sanitize(lauminaryName)}_src.png";

        /// <summary>
        /// Re-own every animation frame whose pixels live outside this version's <c>Source/</c> folder. Returns the
        /// number of frames repointed (0 = nothing to do / already self-contained). In <paramref name="strict"/>
        /// mode (used on commit) an unreadable source throws; otherwise unreadable frames are left external and a
        /// warning is logged.
        /// </summary>
        public static int Detach(LauminaryVersion version, string versionFolder, string lauminaryName, bool strict = false)
        {
            if (version == null || version.animations == null) return 0;
            string srcFolder = SourceFolder(versionFolder);

            // Does anything actually point outside our own Source/ folder? If not, skip entirely.
            bool anyExternal = false;
            foreach (var def in version.animations)
                if (def?.recipe != null)
                    foreach (var f in def.recipe)
                        if (IsExternal(f.sourceTextureGuid, srcFolder)) { anyExternal = true; break; }
            if (!anyExternal) return 0;

            var pixelCache = new Dictionary<string, (Color32[] px, int w, int h)>();

            // ── Pass A: extract every frame's keyed block; dedup identical (source, rect, key) into shared slots ──
            var slotKeyToIndex = new Dictionary<string, int>();
            var slotBlocks = new List<(Color32[] px, int w, int h)>();
            // per (anim, frameIndex) → slot key, so Pass C can repoint without re-extracting.
            var frameKeys = new Dictionary<(int a, int f), string>();
            int detached = 0;

            for (int a = 0; a < version.animations.Count; a++)
            {
                var def = version.animations[a];
                if (def?.recipe == null) continue;
                var key = new RegionSlicer.ColorKey
                {
                    enabled = def.bgKeyEnabled, color = def.bgKey, tolerance = def.bgKeyTolerance
                };
                for (int fi = 0; fi < def.recipe.Count; fi++)
                {
                    var fr = def.recipe[fi];
                    bool external = IsExternal(fr.sourceTextureGuid, srcFolder);
                    if (external) detached++;

                    if (!TryGetPixels(fr.sourceTextureGuid, pixelCache, out var px, out int tw, out int th))
                    {
                        if (strict)
                            throw new System.InvalidOperationException(
                                $"Cannot make '{lauminaryName}' self-contained: animation '{def.name}' frame {fi} " +
                                $"sources texture {fr.sourceTextureGuid}, which is unreadable/missing.");
                        Debug.LogWarning($"Launimator: detach skipped '{def.name}' frame {fi} — source {fr.sourceTextureGuid} unreadable.");
                        continue; // leave it external; nothing we can do
                    }

                    // Only key-out the background for frames that still carry a live bg key (external rips). An
                    // already-internal owned frame has true alpha, so its key is off — reading it is identity.
                    var keyForFrame = external ? key : default;
                    Color32 kc = keyForFrame.color;
                    string keyTag = keyForFrame.enabled ? $"{kc.r}:{kc.g}:{kc.b}:{keyForFrame.tolerance}" : "-";
                    string slotKey = $"{fr.sourceTextureGuid}|{RectKey(fr.cell)}|{keyTag}";
                    frameKeys[(a, fi)] = slotKey;

                    if (!slotKeyToIndex.ContainsKey(slotKey))
                    {
                        var block = ExtractBlock(px, tw, th, fr.cell, keyForFrame, out int bw, out int bh);
                        slotKeyToIndex[slotKey] = slotBlocks.Count;
                        slotBlocks.Add((block, bw, bh));
                    }
                }
            }

            if (slotBlocks.Count == 0) return 0;

            // ── Pass B: pack unique slots into rows (each slot keeps its own size; row 0 = bottom). Rows wrap at
            // MaxSourceRowWidth so the sheet stays inside the texture size limit — a single long strip of a few
            // dozen frames would otherwise exceed it and be silently downscaled on import. ──
            var slotRects = new Rect[slotBlocks.Count];
            int rowX = 0, rowY = 0, rowH = 0, ownedW = 0;
            for (int s = 0; s < slotBlocks.Count; s++)
            {
                var (_, bw, bh) = slotBlocks[s];
                if (rowX > 0 && rowX + bw > MaxSourceRowWidth) { rowY += rowH; rowX = 0; rowH = 0; }
                slotRects[s] = new Rect(rowX, rowY, bw, bh);
                rowX += bw;
                rowH = Mathf.Max(rowH, bh);
                ownedW = Mathf.Max(ownedW, rowX);
            }
            int ownedH = rowY + rowH;
            var canvas = new Color32[ownedW * ownedH];
            for (int s = 0; s < slotBlocks.Count; s++)
            {
                var (px, bw, bh) = slotBlocks[s];
                int ox = (int)slotRects[s].x, oy = (int)slotRects[s].y;
                for (int y = 0; y < bh; y++)
                    for (int x = 0; x < bw; x++)
                        canvas[(oy + y) * ownedW + (ox + x)] = px[y * bw + x]; // both bottom-up
            }

            // ── Pass C: write the owned source texture and resolve its GUID ──
            LauminaryBuilder.EnsureFolder(srcFolder);
            string ownedPath = SourcePath(versionFolder, lauminaryName);
            WritePng(canvas, ownedW, ownedH, ownedPath);
            AssetDatabase.ImportAsset(ownedPath, ImportAssetOptions.ForceSynchronousImport);
            ConfigureSource(ownedPath);
            string ownedGuid = AssetDatabase.AssetPathToGUID(ownedPath);

            // ── Pass D: repoint every recipe frame at its slot in the owned source; clear now-baked bg keys ──
            for (int a = 0; a < version.animations.Count; a++)
            {
                var def = version.animations[a];
                if (def?.recipe == null) continue;
                bool touched = false;
                for (int fi = 0; fi < def.recipe.Count; fi++)
                {
                    if (!frameKeys.TryGetValue((a, fi), out string slotKey)) continue; // was unreadable → left external
                    var rect = slotRects[slotKeyToIndex[slotKey]];
                    var fr = def.recipe[fi];
                    fr.sourceTextureGuid = ownedGuid;
                    fr.cell = rect;
                    touched = true;
                }
                if (touched)
                {
                    def.bgKeyEnabled = false;          // alpha is baked into the owned source now
                    def.sourceTextureGuid = ownedGuid; // primary source for re-editing in the Builder
                }
            }

            return detached;
        }

        /// <summary>True if a frame's source GUID resolves to a path OUTSIDE this version's Source/ folder
        /// (a rip sheet, the _Edits folder, or another version's owned source). Empty/unresolved counts as
        /// external so it gets re-owned (or flagged) rather than silently trusted.</summary>
        private static bool IsExternal(string guid, string srcFolder)
        {
            if (string.IsNullOrEmpty(guid)) return true;
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(p)) return true;
            return !p.Replace('\\', '/').StartsWith(srcFolder + "/", System.StringComparison.OrdinalIgnoreCase);
        }

        private static string RectKey(Rect c) =>
            $"{Mathf.RoundToInt(c.x)}:{Mathf.RoundToInt(c.y)}:{Mathf.RoundToInt(c.width)}:{Mathf.RoundToInt(c.height)}";

        /// <summary>Extract a cell as a bottom-up RGBA block, keying the background out to transparency.</summary>
        private static Color32[] ExtractBlock(
            Color32[] px, int texW, int texH, Rect cell, RegionSlicer.ColorKey key, out int bw, out int bh)
        {
            int x0 = Mathf.RoundToInt(cell.x), y0 = Mathf.RoundToInt(cell.y);
            bw = Mathf.Max(1, Mathf.RoundToInt(cell.width));
            bh = Mathf.Max(1, Mathf.RoundToInt(cell.height));
            var b = new Color32[bw * bh];
            for (int y = 0; y < bh; y++)
                for (int x = 0; x < bw; x++)
                {
                    int sx = x0 + x, sy = y0 + y;
                    Color32 c = (sx >= 0 && sx < texW && sy >= 0 && sy < texH) ? px[sy * texW + sx] : new Color32(0, 0, 0, 0);
                    b[y * bw + x] = key.IsBackground(c) ? new Color32(0, 0, 0, 0) : c;
                }
            return b;
        }

        private static bool TryGetPixels(
            string guid, Dictionary<string, (Color32[] px, int w, int h)> cache, out Color32[] px, out int w, out int h)
        {
            px = null; w = h = 0;
            if (string.IsNullOrEmpty(guid)) return false;
            if (cache.TryGetValue(guid, out var hit)) { px = hit.px; w = hit.w; h = hit.h; return px != null; }

            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) { cache[guid] = (null, 0, 0); return false; }

            if (AssetImporter.GetAtPath(path) is TextureImporter ti && !ti.isReadable)
            { ti.isReadable = true; ti.SaveAndReimport(); }

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) { cache[guid] = (null, 0, 0); return false; }
            try { px = tex.GetPixels32(); } catch { px = null; }
            w = tex.width; h = tex.height;
            cache[guid] = (px, w, h);
            return px != null;
        }

        private static void WritePng(Color32[] px, int w, int h, string assetPath)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try { tex.SetPixels32(px); tex.Apply(); File.WriteAllBytes(ToSystemPath(assetPath), tex.EncodeToPNG()); }
            finally { Object.DestroyImmediate(tex); }
        }

        /// <summary>Owned source must be a readable, uncompressed, point-filtered Texture2D the baker can sample.</summary>
        private static void ConfigureSource(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
            ti.textureType = TextureImporterType.Default;
            // Pixels must come back exactly as written: a Default texture otherwise rescales a non-power-of-two
            // sheet to the nearest power of two and caps it at 2048, and the baker would read the resampled copy.
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 8192;
            ti.isReadable = true;
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.SaveAndReimport();
        }

        private static string ToSystemPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }
    }
}
