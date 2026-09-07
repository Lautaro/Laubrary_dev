using UnityEngine;

namespace Laubrary.Pyre
{
    /// <summary>What an optional Pyre anchor is: a single point (its base / start / centre) or a point plus a
    /// facing (a vector — where the thing points as authored).</summary>
    public enum PyreAnchorKind { Position, Vector }

    /// <summary>
    /// The one place the optional anchor on a <see cref="Pyre"/> is turned into a spawn transform. The anchor
    /// does nothing by itself — it is the author telling a placing system where this Pyre's base / start /
    /// centre is and, for a Vector anchor, which way it faces. A Pyre with no anchor keeps its implicit origin:
    /// the canvas centre, which is the sprite pivot the renderer bakes with.
    ///
    /// <para>Conventions: <see cref="Pyre.anchorOrigin"/> is normalized 0..1 across the canvas, bottom-left
    /// origin (the same convention Launimator's <c>VectorMetaFrame.origin</c> uses); <see cref="Pyre.anchorDirection"/>
    /// is a local unit vector, (1,0) = the sprite's +X. A placing system that mirrors the sprite
    /// (<c>SpriteRenderer.flipX</c>) mirrors the anchor with it, so a left-facing shot reads as a MIRRORED
    /// effect rather than one rotated past 90° and drawn upside down.</para>
    /// </summary>
    public static class PyreAnchor
    {
        /// The anchor point in the sprite's own units, measured from its pivot (the canvas centre), before any
        /// flip / rotation / scale. Zero when the Pyre has no anchor.
        public static Vector2 AnchorLocal(Pyre spec)
        {
            if (spec == null || !spec.anchorEnabled) return Vector2.zero;
            float ppu = Mathf.Max(0.01f, spec.pixelsPerUnit);
            return new Vector2((spec.anchorOrigin.x - 0.5f) * spec.Width / ppu,
                               (spec.anchorOrigin.y - 0.5f) * spec.Height / ppu);
        }

        /// The local direction the placing system should treat as this Pyre's forward: the Vector anchor's
        /// direction when there is one, otherwise the sprite's +X (the convention every event rotation used
        /// before anchors existed).
        public static Vector2 ForwardLocal(Pyre spec)
            => spec != null && spec.anchorEnabled && spec.anchorKind == PyreAnchorKind.Vector
               && spec.anchorDirection.sqrMagnitude > 1e-6f
                ? spec.anchorDirection.normalized : Vector2.right;

        /// <summary>The Z rotation (degrees) that points <paramref name="forwardLocal"/> along the world aim
        /// <paramref name="aimDeg"/>. NaN aim = no rotation (NaN out). With <paramref name="flipX"/> the local
        /// forward is mirrored first, because the sprite will be drawn mirrored: a right-facing flash aimed at
        /// 180° comes out as flip + 0°, not flip + 180°.</summary>
        public static float RotationDeg(Vector2 forwardLocal, float aimDeg, bool flipX)
        {
            if (float.IsNaN(aimDeg)) return float.NaN;
            Vector2 f = forwardLocal.sqrMagnitude > 1e-6f ? forwardLocal : Vector2.right;
            if (flipX) f.x = -f.x;
            float forwardDeg = Mathf.Atan2(f.y, f.x) * Mathf.Rad2Deg;
            return Mathf.DeltaAngle(0f, aimDeg - forwardDeg);
        }

        /// <summary>Where the transform must sit so that the anchor point lands exactly on
        /// <paramref name="spawnPoint"/>, given the flip / rotation / uniform scale the sprite will be drawn
        /// with (a transform's local→world is position + R · S · local).</summary>
        public static Vector2 SpawnPosition(Vector2 spawnPoint, Vector2 anchorLocal, float rotationDeg, bool flipX, float scale)
        {
            Vector2 a = anchorLocal * scale;
            if (flipX) a.x = -a.x;
            float r = float.IsNaN(rotationDeg) ? 0f : rotationDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            var rotated = new Vector2(a.x * c - a.y * s, a.x * s + a.y * c);
            return spawnPoint - rotated;
        }

        /// <summary>Place a spawned Pyre: its anchor (or centre, when it has none) on <paramref name="spawnPoint"/>,
        /// its forward pointing along <paramref name="aimDeg"/> (NaN = upright), mirrored when
        /// <paramref name="flipX"/>, at a uniform <paramref name="scale"/>. The caller still sets the
        /// renderer's own <c>flipX</c>; this only writes the transform.</summary>
        public static void Place(Transform t, Pyre spec, Vector2 spawnPoint, float aimDeg, bool flipX, float scale = 1f)
        {
            float rot = RotationDeg(ForwardLocal(spec), aimDeg, flipX);
            Vector2 pos = SpawnPosition(spawnPoint, AnchorLocal(spec), rot, flipX, scale);
            t.position = new Vector3(pos.x, pos.y, 0f);
            t.rotation = float.IsNaN(rot) ? Quaternion.identity : Quaternion.Euler(0f, 0f, rot);
            t.localScale = Vector3.one * scale;
        }
    }
}
