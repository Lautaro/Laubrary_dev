using System.Collections.Generic;
using Laubrary.PreviewKit;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Laubrary.Cartographer
{
    /// One cell of a Prop: which tile, where, and on which named layer of the level it belongs.
    [System.Serializable]
    public class PropCell
    {
        [Tooltip("Position within the prop, in grid cells, relative to the prop's own origin.")]
        public Vector2Int offset;

        [Tooltip("The tile drawn in this cell.")]
        public TileBase tile;

        [Tooltip("Which named layer of the level this cell is stamped onto — e.g. Terrain or Structures.")]
        public string layer = CartographerLevel.TerrainLayer;
    }

    /// A named point a Prop exposes to gameplay and to scripted sequences. A sequence asks for "landing-pad"
    /// and gets a world position, without either side knowing anything about the other's types — this is the
    /// entire connection between a level and an Interlude, kept deliberately narrow.
    [System.Serializable]
    public class PropSpot
    {
        [Tooltip("The name a sequence or gameplay script looks this point up by. Unique within a level.")]
        public string spotName = "spot";

        [Tooltip("Position within the prop, in grid cells from the prop's origin. Fractions are allowed, so a " +
                 "spot can sit mid-cell rather than snapping to a corner.")]
        public Vector2 offset;
    }

    /// One of several prefabs a Prop may resolve to when it is placed. Procgen rolls the weights; a hand-placed
    /// prop takes the first entry unless the author picks another.
    [System.Serializable]
    public class PropPrefabChoice
    {
        [Tooltip("Spawned as a child of the level when this prop is placed. Leave empty for a purely visual prop.")]
        public GameObject prefab;

        [Tooltip("Relative likelihood of being chosen. Higher is more likely; 0 disables this entry.")]
        [Min(0f)] public float weight = 1f;
    }

    /// A reusable multi-cell tile stamp — a building, a platform, a rock formation — plus whatever gameplay
    /// meaning it carries. This is the piece the Tile Palette cannot express: Unity paints one tile at a time,
    /// a Prop places a whole arrangement with its tags, its spawn hook, and its named spots intact.
    ///
    /// Props double as "structures": a structure is simply a prop whose cells target the Structures layer, so
    /// there is one concept here rather than two asset types that differ only by where they land.
    [CreateAssetMenu(menuName = "Laubrary/Cartographer/Prop", fileName = "Prop")]
    public class Prop : ScriptableObject, IVisualPreview
    {
        [Header("Identity")]
        [Tooltip("Name shown in browsers and pickers.")]
        public string displayName = "New Prop";

        [Header("Cells")]
        [Tooltip("The tiles this prop stamps, each at its own offset and on its own layer.")]
        public List<PropCell> cells = new();

        [Header("Gameplay")]
        [Tooltip("Labels this prop carries — traversability, hazards, and anything else the project wires up. " +
                 "Cartographer stores them and never interprets them.")]
        public List<TileTag> tags = new();

        [Tooltip("Prefabs this prop may spawn when placed. Several entries means the choice is rolled from the " +
                 "level's seed, which is how one prop becomes 'some enemy spawner from this set'.")]
        public List<PropPrefabChoice> prefabChoices = new();

        [Tooltip("Marks this prop as a procgen prop: inert in a normal build, it acts only when the procgen " +
                 "pass runs — LevelProcgen.Run hands its prefab behaviours OnProcgen so they can mutate the " +
                 "level data before it builds.")]
        public bool procgen;

        [Header("Spots")]
        [Tooltip("Named points this prop publishes to the level, for gameplay and scripted sequences to find.")]
        public List<PropSpot> spots = new();

        /// Cell-space bounds covering every cell, or a single cell at the origin when the prop is empty.
        public RectInt CellBounds
        {
            get
            {
                if (cells == null || cells.Count == 0) return new RectInt(0, 0, 1, 1);

                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                foreach (var c in cells)
                {
                    if (c == null) continue;
                    minX = Mathf.Min(minX, c.offset.x); maxX = Mathf.Max(maxX, c.offset.x);
                    minY = Mathf.Min(minY, c.offset.y); maxY = Mathf.Max(maxY, c.offset.y);
                }
                if (minX > maxX) return new RectInt(0, 0, 1, 1);

                return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            }
        }

        /// True if any of this prop's tags is `tag`.
        public bool HasTag(TileTag tag)
        {
            if (tag == null || tags == null) return false;
            for (int i = 0; i < tags.Count; i++) if (tags[i] == tag) return true;
            return false;
        }

        /// Transform a prop-local cell offset by `rotation` quarter-turns anticlockwise and an optional X
        /// mirror. Mirroring is applied first so a mirrored-then-rotated stamp matches what the preview drew.
        /// The canonical copy — placement, resolution and the stamper all call this one.
        public static Vector2Int TransformOffset(Vector2Int offset, int rotation, bool mirrorX)
        {
            if (mirrorX) offset.x = -offset.x;
            int turns = ((rotation % 4) + 4) % 4;
            for (int i = 0; i < turns; i++) offset = new Vector2Int(-offset.y, offset.x);
            return offset;
        }

        /// Same transform for a fractional (spot) offset.
        public static Vector2 TransformOffset(Vector2 offset, int rotation, bool mirrorX)
        {
            if (mirrorX) offset.x = -offset.x;
            int turns = ((rotation % 4) + 4) % 4;
            for (int i = 0; i < turns; i++) offset = new Vector2(-offset.y, offset.x);
            return offset;
        }

        // IVisualPreview — the prop's own cells drawn at their real offsets, through the shared tile-to-pixels
        // path the authoring windows will use. Static: a tile arrangement has nothing to animate.
        public Texture2D RenderPreviewTexture()
        {
            if (cells == null || cells.Count == 0) return null;

            Sprite reference = null;
            foreach (var c in cells)
            {
                if (c == null) continue;
                reference = CartographerPreview.SpriteOf(c.tile);
                if (reference != null) break;
            }
            if (reference == null) return null;

            var bounds = CellBounds;
            int cell = CartographerPreview.CellPixels(reference);
            int w = bounds.width * cell, h = bounds.height * cell;
            if (w <= 0 || h <= 0 || w > CartographerPreview.MaxSide || h > CartographerPreview.MaxSide) return null;

            var tex = CartographerPreview.NewCanvas(w, h);
            foreach (var c in cells)
            {
                if (c == null) continue;
                var sprite = CartographerPreview.SpriteOf(c.tile);
                if (sprite == null) continue;

                CartographerPreview.Blit(tex, sprite,
                    (c.offset.x - bounds.xMin) * cell,
                    (c.offset.y - bounds.yMin) * cell);
            }
            tex.Apply();
            return tex;
        }

        public bool CanAnimatePreview => false;
        public float PreviewFps => 0f;
        public void UpdateAnimatedPreview(Texture2D tex, double time) { }
    }
}
