using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Cartographer
{
    /// A free sprite placed on a layer at any position, rotation and scale — NOT bound to the grid. Kept as
    /// its own concept rather than smuggled into the tile system, because a tile's identity is "it occupies
    /// cell (x,y)" and a decal's is "it sits at this arbitrary transform"; forcing one to be the other makes
    /// both worse. A decal with a prefab is a PROP rather than scenery.
    [System.Serializable]
    public class Decal
    {
        [Tooltip("The sprite drawn, or the first frame when an animation is set.")]
        public Sprite sprite;

        [Tooltip("When set, the decal cycles through these sprites instead of showing the single sprite.")]
        public List<Sprite> animation = new();

        [Tooltip("Animation speed in frames per second.")]
        [Min(0.01f)] public float animationFps = 6f;

        [Tooltip("Position in the level's local space, in grid units from the grid origin.")]
        public Vector2 position;

        [Tooltip("Rotation in degrees, anticlockwise.")]
        public float rotation;

        [Tooltip("Scale applied to the sprite. (1, 1) draws it at its imported size.")]
        public Vector2 scale = Vector2.one;

        [Tooltip("The level layer this decal belongs to. It inherits that layer's sorting, so 'behind the " +
                 "wall layer' is expressible by layer choice alone.")]
        public string layer = "";

        [Tooltip("Draw order among decals on the same layer. Higher draws in front.")]
        public int sortingOrder;

        [Tooltip("Labels this decal carries. Cartographer stores them and never interprets them.")]
        public List<TileTag> tags = new();

        [Tooltip("Optional. When set, the level spawns this prefab at the decal's transform instead of a " +
                 "bare SpriteRenderer — which is what makes a decal a prop rather than scenery.")]
        public GameObject prefab;

        [Tooltip("Whether a human placed this decal or a generator did. Regeneration replaces only " +
                 "generated elements and never touches authored ones.")]
        public Origin origin = Origin.Authored;

        /// True when this decal animates.
        public bool IsAnimated => animation != null && animation.Count > 1;
    }
}
