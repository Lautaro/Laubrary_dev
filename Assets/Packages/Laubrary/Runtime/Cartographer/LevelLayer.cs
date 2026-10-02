using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

namespace Laubrary.Cartographer
{
    /// One drawing layer of a LevelAsset: which tileset it paints from, how it renders, and how it collides.
    /// Pure data on the asset — the Tilemap that renders it is built (and owned) by the LevelInstance, so a
    /// level can exist, be browsed and be previewed without any scene at all.
    [System.Serializable]
    public class LevelLayer
    {
        [Header("Identity")]
        [Tooltip("Name paints, prop cells and decals address this layer by, e.g. Terrain or Structures. " +
                 "Unique within the level.")]
        public string name = "Terrain";

        [Tooltip("The palette this layer paints from. The window's palette shows this tileset while the " +
                 "layer is active.")]
        public Tileset tileset;

        [Tooltip("THE LAYER'S BACKGROUND. This tile fills every cell of this layer that has nothing painted " +
                 "on it, inside the level's bounds — so a floor layer needs no painting at all. It cannot be " +
                 "erased cell by cell: painting simply COVERS it, and erasing that paint uncovers it again. " +
                 "Set it by right-clicking a tile in the Cartographer's Tileset box.")]
        [FormerlySerializedAs("defaultTile")]
        public LevelTile backgroundTile;

        [Header("Rendering")]
        [Tooltip("Draw order among the level's layers. Higher draws in front. Ignored when Y Sort is on — " +
                 "that layer joins the actor band instead, and position decides.")]
        public int sortingOrder;

        [Tooltip("Sort this layer's tiles against the ACTORS by world Y, so a character walks in front of " +
                 "the bottom of a tall object and behind its top. Turn it on for anything the player can be " +
                 "on both sides of — shelves, walls, furniture. Costs a little performance: each tile is " +
                 "sorted on its own instead of the layer being drawn as one block.")]
        public bool ySort;

        [Tooltip("Layer opacity IN THE GAME, multiplied into every tile's colour. 1 is opaque. A shipped " +
                 "setting, not a preview dial: LevelInstance writes it into the built Tilemap's colour, so a " +
                 "layer left at 0.5 here ships at 0.5. To dim a layer only while authoring, use Editor " +
                 "Opacity below.")]
        [Range(0f, 1f)] public float opacity = 1f;

        [Header("Authoring")]
        [Tooltip("Whether the layer draws IN THE GAME. LevelInstance disables the built Tilemap's renderer " +
                 "for a hidden layer, so this ships — it is not a preview toggle (that is Editor Hidden " +
                 "below).  ⚠️ It hides the layer, it does not disable it: colliders are built from 'Solid' " +
                 "and never consult this, so hiding a SOLID layer leaves walls that still block the player.")]
        public bool visible = true;

        [Tooltip("A locked layer cannot be painted on. Protects a finished layer from stray clicks.")]
        public bool locked;

        [Tooltip("EDITING ONLY — this never reaches the game. Hides the layer in the Cartographer's canvas so " +
                 "you can see what is underneath it. LevelInstance does not read this field at all: the built " +
                 "scene and the shipped game still draw the layer. For 'hidden in the game too', use Visible.")]
        public bool editorHidden;

        [Tooltip("EDITING ONLY — this never reaches the game. Dims the layer in the Cartographer's canvas " +
                 "while you work on another one. MULTIPLIED with the runtime Opacity rather than replacing " +
                 "it, so a layer that ships at 0.5 and is dimmed to 0.5 here draws at 0.25 while editing and " +
                 "still ships at 0.5.")]
        [Range(0f, 1f)] public float editorOpacity = 1f;

        [Header("Collision")]
        [Tooltip("Whether this layer's tiles are solid. Drives the colliders the level builds for it.")]
        public bool solid;

        [Tooltip("Solid only from above — you land on it but jump up through it. Side-scrolling platforms.")]
        public bool oneWay;

        [Tooltip("Grid: every solid tile collides as a full square. Sprite: each tile collides on its own " +
                 "sprite outline, which is how a triangular tile becomes a walkable slope.")]
        public Tile.ColliderType colliderShape = Tile.ColliderType.Grid;

        [Tooltip("Optional. When set, only cells whose tile or source prop carries this tag actually " +
                 "collide — tags NARROW a solid layer, never widen a non-solid one, so there is one place " +
                 "to look when something blocks.")]
        public TileTag solidTag;

        [Header("Gameplay")]
        [Tooltip("Labels this whole layer carries. Cartographer stores them and never interprets them.")]
        public List<TileTag> tags = new();
    }
}
