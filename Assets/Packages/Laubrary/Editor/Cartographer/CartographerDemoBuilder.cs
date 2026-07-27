using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Laubrary.Cartographer.Editor
{
    /// Builds a small set of demo assets so Cartographer's types can be seen and exercised before its authoring
    /// windows exist — a tag, three tiles, a Clump shaped like a little building, and a Biome holding them.
    ///
    /// Generates its own placeholder tile art rather than depending on any sheet being present, so it works in a
    /// bare project. Writes into the HOST project's Assets/, never the package, and never overwrites: a second
    /// run reuses whatever is already there, so tweaks to the demo assets survive re-running it.
    public static class CartographerDemoBuilder
    {
        const string Root = "Assets/Demos/CartographerDemo";
        const int TilePx = 16;

        [MenuItem("Laubrary/Cartographer/Build Demo Assets")]
        public static void Build()
        {
            Directory.CreateDirectory(Root);

            // Three placeholder tiles: solid wall, lighter roof, speckled ground.
            var wall = MakeTile("Wall", new Color32(92, 84, 112, 255), new Color32(66, 60, 82, 255), false);
            var roof = MakeTile("Roof", new Color32(178, 96, 82, 255), new Color32(140, 72, 62, 255), false);
            var ground = MakeTile("Ground", new Color32(74, 110, 70, 255), new Color32(62, 94, 58, 255), true);

            var solid = LoadOrCreate<ClumpTag>($"{Root}/Untraversable.asset", t =>
            {
                t.description = "Blocks movement. The consuming project decides what that means mechanically.";
                t.editorColor = new Color(0.9f, 0.35f, 0.3f);
            });

            // A little building: a 4x2 body of wall with a 4x1 roof on top, and a door spot at its foot.
            var hut = LoadOrCreate<Clump>($"{Root}/Hut.asset", c =>
            {
                c.displayName = "Hut";
                c.cells = new List<ClumpCell>();
                for (int x = 0; x < 4; x++)
                {
                    for (int y = 0; y < 2; y++)
                        c.cells.Add(new ClumpCell { offset = new Vector2Int(x, y), tile = wall, layer = CartographerLevel.TerrainLayer });
                    c.cells.Add(new ClumpCell { offset = new Vector2Int(x, 2), tile = roof, layer = "Structures" });
                }
                c.tags = new List<ClumpTag> { solid };
                c.spots = new List<ClumpSpot> { new ClumpSpot { spotName = "hut-door", offset = new Vector2(1.5f, 0f) } };
            });

            LoadOrCreate<CartographerBiome>($"{Root}/Settlement.asset", b =>
            {
                b.displayName = "Settlement";
                b.terrainTiles = new List<TileBase> { ground, wall, roof };
                b.clumps = new List<Clump> { hut };
                b.tint = new Color(1f, 0.96f, 0.88f);
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Cartographer] Demo assets ready in {Root} — Untraversable tag, 3 tiles, Hut clump, Settlement biome.");
        }

        /// A flat tile with a darker border, optionally speckled, saved as a real imported sprite so it behaves
        /// exactly like authored tile art would.
        static Tile MakeTile(string name, Color32 fill, Color32 edge, bool speckle)
        {
            string pngPath = $"{Root}/{name}.png";
            if (!File.Exists(pngPath))
            {
                var tex = new Texture2D(TilePx, TilePx, TextureFormat.RGBA32, false);
                var rng = new System.Random(name.GetHashCode());
                for (int y = 0; y < TilePx; y++)
                    for (int x = 0; x < TilePx; x++)
                    {
                        bool border = x == 0 || y == 0 || x == TilePx - 1 || y == TilePx - 1;
                        var c = border ? edge : fill;
                        if (!border && speckle && rng.NextDouble() < 0.12) c = edge;
                        tex.SetPixel(x, y, c);
                    }
                tex.Apply();
                File.WriteAllBytes(pngPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
            }

            // Point filter + uncompressed + readable, so the sprite survives as crisp pixel art AND can be read
            // back by the preview renderer.
            var importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.filterMode = FilterMode.Point;
                importer.spritePixelsPerUnit = TilePx;
                importer.mipmapEnabled = false;
                importer.isReadable = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
            return LoadOrCreate<Tile>($"{Root}/{name}.asset", t => t.sprite = sprite);
        }

        static T LoadOrCreate<T>(string path, System.Action<T> configure) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            var created = ScriptableObject.CreateInstance<T>();
            configure(created);
            AssetDatabase.CreateAsset(created, path);
            return created;
        }
    }
}
