using System.Collections.Generic;
using UnityEngine;
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

        [Tooltip("When set, every unpainted cell inside the level's bounds shows this tile — a floor layer " +
                 "needs no painting at all. Painted cells and placed props draw over it.")]
        public LevelTile defaultTile;

        [Header("Rendering")]
        [Tooltip("Draw order among the level's layers. Higher draws in front.")]
        public int sortingOrder;

        [Tooltip("Layer opacity, multiplied into every tile's colour. 1 is opaque.")]
        [Range(0f, 1f)] public float opacity = 1f;

        [Header("Authoring")]
        [Tooltip("Whether the layer draws, in the editor and at runtime. Hiding a layer is an authoring aid, " +
                 "not a gameplay switch.")]
        public bool visible = true;

        [Tooltip("A locked layer cannot be painted on. Protects a finished layer from stray clicks.")]
        public bool locked;

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
