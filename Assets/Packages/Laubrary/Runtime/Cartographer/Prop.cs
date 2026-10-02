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

        [Tooltip("What this prop DOES in the game, in one line, for whoever is building a level. A prop that " +
                 "spawns something, gates something or mutates the map at procgen time is invisible work — " +
                 "the level author has to be told. Leave empty and the editor falls back to describing the " +
                 "prop's prefab behaviours itself.")]
        [TextArea(1, 3)] public string description;

        [Tooltip("Colour the editor marks this prop's cells with when they draw NO tile. An invisible prop is " +
                 "an unverifiable prop — a marker must occupy the map wherever pixels do not. Leave the alpha " +
                 "at zero to get a stable colour derived from the prop's name.")]
        public Color editorColor = new(0f, 0f, 0f, 0f);

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
                 "pass runs — LevelProcgen.Run hands every ILevelMutator on its prefabs a turn at rewriting " +
                 "the level data before it builds. A prop ticked here whose prefabs declare no mutator does " +
                 "nothing, and the pass says so out loud.")]
        public bool procgen;

        [Tooltip("This prop's tiles are an AUTHORING marker — drawn in the editor so the level author can see " +
                 "and pick where it sits, and left out of the level in Play. For invisible logic such as a " +
                 "player start or an enemy spawner, whose prefab does the work and whose tile would otherwise " +
                 "show up in the game as a stray square. Its prefabs, spots and tags are untouched.")]
        public bool markerOnly;

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

        /// The colour the editor marks this prop's tile-less cells with: the authored one, or — while the
        /// author has not picked any — a saturated hue derived from the name, so two invisible props sitting
        /// next to each other are still tellable apart without anyone having to configure that.
        public Color MarkerColor
        {
            get
            {
                if (editorColor.a > 0.001f) return editorColor;
                unchecked
                {
                    uint h = 2166136261u;
                    string key = string.IsNullOrEmpty(name) ? displayName ?? "" : name;
                    foreach (char c in key) h = (h ^ c) * 16777619u;
                    return Color.HSVToRGB(h % 360u / 360f, 0.65f, 1f);
                }
            }
        }

        /// True once ANY cell of this prop draws a tile. False means the prop is pure metadata — a procgen
        /// mutator, a spawn point, a trigger — and the editor must mark its footprint some other way.
        public bool HasVisual
        {
            get
            {
                if (cells == null) return false;
                foreach (var c in cells) if (c != null && c.tile != null) return true;
                return false;
            }
        }

        /// One line telling a level author what placing this does: the authored description when there is
        /// one, otherwise assembled from what the prop demonstrably DOES — its procgen flag and whatever
        /// PropBehaviours ride its prefabs, each asked to describe itself. Derived rather than remembered,
        /// so a behaviour someone adds later cannot silently become invisible work.
        public string Explain()
        {
            if (!string.IsNullOrWhiteSpace(description)) return description.Trim();

            var parts = new List<string>();
            if (prefabChoices != null)
                foreach (var choice in prefabChoices)
                {
                    if (choice?.prefab == null) continue;
                    foreach (var b in choice.prefab.GetComponents<PropBehaviour>())
                    {
                        if (b == null) continue;
                        string d = b.Describe();
                        if (!string.IsNullOrWhiteSpace(d) && !parts.Contains(d)) parts.Add(d.Trim());
                    }
                    // A mutator need not be a PropBehaviour — it is the INTERFACE that declares one, so ask
                    // for it directly or a plain-component mutator would explain itself to nobody.
                    foreach (var m in choice.prefab.GetComponents<ILevelMutator>())
                    {
                        if (m == null) continue;
                        string d = m.DescribeMutation();
                        if (!string.IsNullOrWhiteSpace(d) && !parts.Contains(d)) parts.Add(d.Trim());
                    }
                }

            if (parts.Count > 0) return string.Join(" · ", parts);
            if (procgen) return "Marked procgen but declares no mutator — running the pass changes nothing.";
            return HasVisual ? "" : "Places nothing and does nothing — an empty prop.";
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
        ///
        /// ⚠️ TWIN: `Laubrary.MetaMapper.MetaMapCells.TransformOffset` duplicates this maths, because the
        /// MetaMapper module references nothing (that is what lets every tool reference IT). The two must
        /// stay in step — change one, change both. Their agreement across every rotation × mirror was
        /// verified by probe when the twin was written, but there is NO standing test holding them
        /// together: nothing will fail if they drift. Writing one is owed.
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
                    (c.offset.y - bounds.yMin) * cell, cell);
            }
            tex.Apply();
            return tex;
        }

        public bool CanAnimatePreview => false;
        public float PreviewFps => 0f;
        public void UpdateAnimatedPreview(Texture2D tex, double time) { }
    }
}
