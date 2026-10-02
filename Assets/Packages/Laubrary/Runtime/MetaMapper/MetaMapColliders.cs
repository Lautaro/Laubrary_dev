using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.MetaMapper
{
    /// <summary>
    /// Shapes layer → Unity 2D colliders. Builds, on request and never on its own: Laubrary attaches no meaning
    /// to a layer, so nothing here decides WHICH layer is a collision footprint or WHEN colliders exist. The
    /// game asks ("build the 'feet' layer on this body as a solid circle"), this does the geometry.
    ///
    /// THE CONVERSION is the caller's description of where map space sits in the host's local space — a
    /// function from a map position to a local position. It must be AXIS-ALIGNED (scale, flip and offset;
    /// no rotation, no skew): a box collider has no rotation of its own, so a rotated conversion could only be
    /// approximated. Both ready-made conversions are: <see cref="LocalUnits"/> (identity) and
    /// <see cref="SpritePixels"/> (the sprite binding's own maths, so a collider lands exactly where
    /// <see cref="MetaMapSprite.ToWorld"/> puts a mark).
    /// </summary>
    public static class MetaMapColliders
    {
        /// <summary>LocalUnits map space IS the host's local frame — the conversion is the identity.</summary>
        public static readonly Func<Vector2, Vector2> LocalUnits = p => p;

        /// <summary>SpritePixels map space for a sprite drawn by a renderer on the host itself (no child
        /// offset): pixels from the rect's bottom-left → local units about the pivot, mirrored on flipX. The
        /// same maths as <see cref="MetaMapSprite.ToWorld"/>, minus the transform (colliders live in local
        /// space and the transform applies itself).</summary>
        public static Func<Vector2, Vector2> SpritePixels(Sprite sprite, bool flipX = false)
            => p => (Vector2)MetaMapSprite.ToWorld(sprite, null, flipX, p);

        /// <summary>
        /// Add one collider per shape of a SHAPES layer to <paramref name="host"/>: a BoxCollider2D per rect, a
        /// CircleCollider2D per circle, sized and offset through <paramref name="mapToLocal"/>. Returns the
        /// colliders in authored shape order (empty when the layer is missing, empty, or not a Shapes layer).
        ///
        /// <paramref name="physicsLayer"/> &lt; 0 keeps the colliders on the host. A layer index that differs
        /// from the host's own puts them on a new child object on that physics layer (named after the map layer,
        /// identity local transform), because Unity has no per-collider layer — that is how a body's "feet"
        /// collide with walls while its hurt box does not. The child is the colliders' <c>gameObject</c>;
        /// destroying it removes them all.
        /// </summary>
        public static List<Collider2D> Build(GameObject host, MetaMapData data, string layerId,
            Func<Vector2, Vector2> mapToLocal, bool isTrigger, int physicsLayer = -1, int frame = 0)
        {
            var built = new List<Collider2D>();
            if (host == null || data == null || mapToLocal == null) return built;

            var shapes = new List<MetaShape>();
            if (data.GetShapes(layerId, shapes, frame) == 0) return built;

            GameObject target = host;
            if (physicsLayer >= 0 && physicsLayer != host.layer)
            {
                target = new GameObject(string.IsNullOrEmpty(layerId) ? "Shapes" : layerId) { layer = physicsLayer };
                target.transform.SetParent(host.transform, false);
            }

            foreach (var sh in shapes)
            {
                ToLocal(sh, mapToLocal, out var center, out var size);
                if (sh.kind == ShapeKind.Circle)
                {
                    var c = target.AddComponent<CircleCollider2D>();
                    c.offset = center;
                    c.radius = size.x * 0.5f;
                    c.isTrigger = isTrigger;
                    built.Add(c);
                }
                else
                {
                    var b = target.AddComponent<BoxCollider2D>();
                    b.offset = center;
                    b.size = size;
                    b.isTrigger = isTrigger;
                    built.Add(b);
                }
            }
            return built;
        }

        /// <summary>One shape through an axis-aligned conversion: its centre, and its full extent (a circle's
        /// diameter in x). The extent is measured between two converted CORNERS, so a flip (negative scale)
        /// never produces a negative size, and a circle takes its diameter from the x axis — a non-uniform
        /// conversion cannot make an ellipse, so it does not pretend to.</summary>
        public static void ToLocal(MetaShape shape, Func<Vector2, Vector2> mapToLocal, out Vector2 center,
            out Vector2 size)
        {
            center = default; size = default;
            if (shape == null || mapToLocal == null) return;
            Vector2 half = shape.size * 0.5f;
            Vector2 a = mapToLocal(shape.center - half);
            Vector2 b = mapToLocal(shape.center + half);
            center = (a + b) * 0.5f;
            size = new Vector2(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
            if (shape.kind == ShapeKind.Circle) size.y = size.x;
        }
    }

    /// <summary>A shape placed in the WORLD — what a placement-aware query hands back (see
    /// <see cref="MetaMapCells.ShapeToWorld"/>). <see cref="size"/> is axis-aligned in world space: a
    /// quarter-turned rect comes back with its width and height already swapped.</summary>
    public struct MetaWorldShape
    {
        public string name;
        public ShapeKind kind;
        public Vector3 center;
        /// <summary>Full world extent; a circle's DIAMETER in x (y mirrors it).</summary>
        public Vector2 size;

        public float Radius => size.x * 0.5f;
    }
}
