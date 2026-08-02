using System.Collections.Generic;
using Laubrary.PreviewKit;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Laubrary.Cartographer
{
    /// How painting picks among a LevelTile's interchangeable variants. Resolution happens at PAINT time and
    /// the chosen index is stored per cell — never at draw time, where a chunk refresh would visibly re-roll
    /// tiles as the camera moves, and where RoundRobin/NeverRepeatLast have no "previous cell" to consult.
    public enum VariantPolicy
    {
        /// Any variant, uniformly, from a hash of the painted cell — the same cell always rolls the same.
        Random,
        /// Cycle through the variants in order as cells are painted.
        RoundRobin,
        /// Random, but never the variant the previous paint chose.
        NeverRepeatLast,
        /// Random, biased by the weights list.
        Weighted,
    }

    /// One tile — the atom a level is painted with. Owns its interchangeable variants, its animation and its
    /// gameplay tags, so one asset type answers "a tile", "a group of interchangeable tiles", "an animated
    /// tile" and "a tile that means something to gameplay" without any of them being a special case.
    ///
    /// Transparency is the ABSENCE of a tile: an empty cell draws nothing, and there is deliberately no
    /// "blank tile" — Unity already has one and it is null.
    [CreateAssetMenu(menuName = "Laubrary/Cartographer/Level Tile", fileName = "LevelTile")]
    public class LevelTile : TileBase, IVisualPreview
    {
        [Header("Identity")]
        [Tooltip("Name shown in palettes and pickers.")]
        public string displayName = "New Tile";

        [Header("Variants")]
        [Tooltip("The sprites this tile may draw with. One entry is a plain tile; several make an " +
                 "interchangeable group — painting picks one by the policy and stores the choice per cell.")]
        public List<Sprite> variants = new();

        [Tooltip("How painting picks among the variants. Ignored when there is only one.")]
        public VariantPolicy variantPolicy = VariantPolicy.Random;

        [Tooltip("Relative likelihood per variant, used only by the Weighted policy. Entries beyond the " +
                 "variant count are ignored; missing entries count as 1.")]
        public List<float> weights = new();

        [Header("Animation")]
        [Tooltip("When set, the tile cycles through these sprites instead of showing a variant. An animated " +
                 "tile ignores its variants — one axis of variety per tile keeps painting predictable.")]
        public List<Sprite> animation = new();

        [Tooltip("Animation speed in frames per second, on a Tilemap whose Animation Frame Rate is 1 (which " +
                 "is what LevelInstance builds).")]
        [Min(0.01f)] public float animationFps = 6f;

        [Header("Collision")]
        [Tooltip("Grid: collides as a full square. Sprite: collides on the sprite's outline, which is how a " +
                 "triangular tile becomes a walkable slope. None: never collides, whatever the layer says.")]
        public Tile.ColliderType colliderShape = Tile.ColliderType.Grid;

        [Header("Gameplay")]
        [Tooltip("Labels this tile carries. Cartographer stores them and never interprets them.")]
        public List<TileTag> tags = new();

        [Header("Provenance")]
        [Tooltip("Content hash of the source sheet this tile was plucked from. Identity follows the " +
                 "PIXELS — the builder uses it to tint already-plucked cells even after the source file " +
                 "is renamed, moved, or lives outside the project entirely.")]
        public string sourceSheetMd5;

        [Tooltip("Where the source sheet lived when this tile was plucked — lineage for humans, and the " +
                 "builder's way to reopen the sheet a tileset was curated from.")]
        public string sourceSheetPath;

        [Tooltip("Which grid cells of the source sheet this tile consumed (col, row from top-left).")]
        public List<Vector2Int> sourceCells = new();

        /// True when this tile animates rather than showing a variant.
        public bool IsAnimated => animation != null && animation.Count > 1;

        /// The sprite for a stored variant index, clamped so a stale index (variants were edited after the
        /// paint) degrades to a valid sprite rather than a hole.
        public Sprite SpriteOfVariant(int index)
        {
            if (variants == null || variants.Count == 0) return null;
            return variants[Mathf.Clamp(index, 0, variants.Count - 1)];
        }

        /// True if any of this tile's tags is `tag`.
        public bool HasTag(TileTag tag)
        {
            if (tag == null || tags == null) return false;
            for (int i = 0; i < tags.Count; i++) if (tags[i] == tag) return true;
            return false;
        }

        public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        {
            tileData.sprite = IsAnimated ? animation[0]
                : SpriteOfVariant(LevelTileVariants.VariantAt(tilemap, position));
            tileData.color = Color.white;
            tileData.transform = Matrix4x4.identity;
            tileData.colliderType = colliderShape;
            // Left unlocked so the level's collider pass can set per-cell collider types without fighting
            // the tile, the same way the legacy level build does.
            tileData.flags = TileFlags.None;
        }

        public override bool GetTileAnimationData(Vector3Int position, ITilemap tilemap,
            ref TileAnimationData tileAnimationData)
        {
            if (!IsAnimated) return false;
            tileAnimationData.animatedSprites = animation.ToArray();
            tileAnimationData.animationSpeed = animationFps;
            tileAnimationData.animationStartTime = 0f;
            return true;
        }

        // IVisualPreview — the first variant (or first animation frame), read through PreviewTex so imported,
        // non-readable art previews correctly. Animated tiles animate in browsers too.
        public Texture2D RenderPreviewTexture()
        {
            var s = IsAnimated ? animation[0] : SpriteOfVariant(0);
            return PreviewTex.CropSprite(s);
        }

        public bool CanAnimatePreview => IsAnimated;
        public float PreviewFps => animationFps;

        public void UpdateAnimatedPreview(Texture2D tex, double time)
        {
            if (!IsAnimated || tex == null) return;
            int frame = (int)(time * animationFps) % animation.Count;
            PreviewTex.BlitInto(tex, animation[frame]);
        }
    }

    /// Answers "which variant was painted at this cell" while a Tilemap draws. LevelInstance registers each
    /// Tilemap it builds against its level's stored paints; a Tilemap nobody registered falls back to variant
    /// zero. Keyed by instance ID so a destroyed Tilemap can never keep a registration alive.
    public static class LevelTileVariants
    {
        public interface ISource
        {
            bool TryGetVariant(Vector3Int cell, out int variantIndex);
        }

        static readonly Dictionary<int, ISource> sources = new();

        public static void Register(Tilemap map, ISource source)
        {
            if (map != null && source != null) sources[map.GetInstanceID()] = source;
        }

        public static void Unregister(Tilemap map)
        {
            if (map != null) sources.Remove(map.GetInstanceID());
        }

        /// The stored variant index for a cell, or 0 when nothing is registered for this Tilemap.
        public static int VariantAt(ITilemap tilemap, Vector3Int cell)
        {
            if (tilemap == null) return 0;
            var map = tilemap.GetComponent<Tilemap>();
            if (map == null) return 0;
            return sources.TryGetValue(map.GetInstanceID(), out var s) && s.TryGetVariant(cell, out int v) ? v : 0;
        }
    }
}
