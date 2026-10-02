using System.Collections.Generic;
using Laubrary.PreviewKit;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

namespace Laubrary.Cartographer
{
    /// A reusable set of tiles and props that belong together, plus how that set looks — the level-building
    /// equivalent of OutBurner's RoadZoneDefinition, which is where this pattern came from.
    ///
    /// A Biome is authoring-side vocabulary, not a place: it says what MAY appear, never where. A Room is what
    /// places it. Several Rooms can draw on the same Biome, which is the whole reason the two are separate.
    [CreateAssetMenu(menuName = "Laubrary/Cartographer/Biome", fileName = "Biome")]
    public class CartographerBiome : ScriptableObject, IVisualPreview
    {
        [Header("Identity")]
        [Tooltip("Name shown in browsers and pickers.")]
        public string displayName = "New Biome";

        [Header("Tiles")]
        [Tooltip("The palettes this biome offers. A level of this biome paints from these tilesets — the " +
                 "window filters its palette by them, which is the whole point of the association.")]
        public List<Tileset> tilesets = new();

        [Tooltip("The tiles generation may use to fill ground and walls here. Legacy — the generator moves " +
                 "onto tilesets when it is ported to LevelAsset.")]
        public List<TileBase> terrainTiles = new();

        [Tooltip("The props allowed to appear in this biome. A prop not listed here is never placed, however " +
                 "the generator is tuned.")]
        [FormerlySerializedAs("clumps")] public List<Prop> props = new();

        [Header("Look")]
        [Tooltip("Multiplied into this biome's tiles when the level is built, for cheap palette variation " +
                 "between biomes that share a tileset.")]
        public Color tint = Color.white;

        [Tooltip("Backdrop shown behind the level in this biome. Optional.")]
        public Sprite background;

        /// True if `prop` is allowed here.
        public bool Allows(Prop prop)
        {
            if (prop == null || props == null) return false;
            for (int i = 0; i < props.Count; i++) if (props[i] == prop) return true;
            return false;
        }

        // IVisualPreview — a row of this biome's terrain tiles, tinted as the biome tints them, through the same
        // tile-to-pixels path Prop previews use. Enough to tell two biomes apart at a glance in a browser.
        public Texture2D RenderPreviewTexture()
        {
            if (terrainTiles == null || terrainTiles.Count == 0) return null;

            var sprites = new List<Sprite>();
            foreach (var t in terrainTiles)
            {
                var s = CartographerPreview.SpriteOf(t);
                if (s != null) sprites.Add(s);
            }
            if (sprites.Count == 0) return null;

            int cell = CartographerPreview.CellPixels(sprites[0]);
            int w = sprites.Count * cell;
            if (w <= 0 || w > CartographerPreview.MaxSide || cell > CartographerPreview.MaxSide) return null;

            var tex = CartographerPreview.NewCanvas(w, cell);
            for (int i = 0; i < sprites.Count; i++) CartographerPreview.Blit(tex, sprites[i], i * cell, 0, cell);

            if (tint != Color.white)
            {
                var px = tex.GetPixels();
                for (int i = 0; i < px.Length; i++) px[i] *= tint;
                tex.SetPixels(px);
            }

            tex.Apply();
            return tex;
        }

        public bool CanAnimatePreview => false;
        public float PreviewFps => 0f;
        public void UpdateAnimatedPreview(Texture2D tex, double time) { }
    }
}
