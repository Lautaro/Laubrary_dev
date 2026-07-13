using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// A simple regular-grid auto-slicer convenience (no CV). Given a texture asset and a
    /// grid (by rows/cols or by cell size), it configures the importer for Multiple sprites
    /// with Point filtering and writes one sprite rect per cell via the modern
    /// ISpriteEditorDataProvider. Track B owns the hard irregular-sheet CV; this is just the
    /// clean-uniform-grid shortcut.
    /// </summary>
    public static class GridSlicer
    {
        public enum PivotMode { Center, BottomCenter, TopLeft, Custom }

        public struct GridSpec
        {
            public int cellWidth;
            public int cellHeight;
            public int padding;     // pixels between cells
            public int offsetX;     // left margin
            public int offsetY;     // top margin (from top of texture)
            public PivotMode pivot;
            public Vector2 customPivot;
            public float pixelsPerUnit;
        }

        public static GridSpec FromRowsCols(int rows, int cols, int texWidth, int texHeight, float ppu = 16f)
        {
            return new GridSpec
            {
                cellWidth = Mathf.Max(1, texWidth / Mathf.Max(1, cols)),
                cellHeight = Mathf.Max(1, texHeight / Mathf.Max(1, rows)),
                padding = 0,
                offsetX = 0,
                offsetY = 0,
                pivot = PivotMode.Center,
                pixelsPerUnit = ppu
            };
        }

        /// <summary>
        /// Slice the texture at <paramref name="assetPath"/> into a regular grid. Returns the
        /// resulting Sprite sub-assets in row-major (top-to-bottom, left-to-right) order.
        /// </summary>
        public static List<Sprite> Slice(string assetPath, GridSpec spec)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                throw new System.InvalidOperationException($"No TextureImporter at '{assetPath}'.");

            // Ensure the texture is imported readable first so we can read its dimensions.
            importer.textureType = TextureImporterType.Sprite;
            importer.isReadable = true;
            importer.SaveAndReimport();

            // Texture dimensions after import.
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            int texW = tex != null ? tex.width : 0;
            int texH = tex != null ? tex.height : 0;

            var rects = BuildRects(spec, texW, texH);

            // The rect-apply path is shared with the Region Slicer.
            string baseName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            return RegionSlicer.Apply(
                assetPath, rects, spec.pixelsPerUnit, spec.pivot, spec.customPivot,
                i => $"{baseName}_{i:000}");
        }

        private static List<Rect> BuildRects(GridSpec spec, int texW, int texH)
        {
            var rects = new List<Rect>();
            int cw = Mathf.Max(1, spec.cellWidth);
            int ch = Mathf.Max(1, spec.cellHeight);
            int pad = Mathf.Max(0, spec.padding);

            // Sprite rects use bottom-left origin; we iterate visually top-to-bottom.
            for (int y = spec.offsetY; y + ch <= texH; y += ch + pad)
            {
                for (int x = spec.offsetX; x + cw <= texW; x += cw + pad)
                {
                    // Convert top-based y to bottom-based for the Rect.
                    float bottom = texH - (y + ch);
                    rects.Add(new Rect(x, bottom, cw, ch));
                }
            }
            return rects;
        }

        public static List<Sprite> LoadSprites(string assetPath)
        {
            var result = new List<Sprite>();
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                if (obj is Sprite sp)
                    result.Add(sp);

            // Row-major ordering: top-to-bottom, then left-to-right.
            result.Sort((a, b) =>
            {
                float ay = a.rect.yMax, by = b.rect.yMax;
                if (!Mathf.Approximately(ay, by)) return by.CompareTo(ay);
                return a.rect.xMin.CompareTo(b.rect.xMin);
            });
            return result;
        }
    }
}
