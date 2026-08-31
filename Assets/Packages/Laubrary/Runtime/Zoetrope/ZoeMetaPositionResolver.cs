using UnityEngine;
using Laubrary.SpriteFx;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// Zoetrope's own implementation of <see cref="IExternalPositionResolver"/>: resolves a named MetaLayer
    /// (Point or Vector mode — whichever is actually painted, same "ask the data" convention
    /// <see cref="MuzzleTracker"/>/<see cref="MuzzleVectorTracker"/> already use) into the LOCAL half-frame-space
    /// a <see cref="RelightModifier"/> expects, so a fake light can ride wherever a painted muzzle (or any other
    /// named layer) points without SpriteFx ever knowing MetaLayers exist.
    ///
    /// The conversion is the reverse of what <see cref="MuzzleTracker"/> does: that moves a WORLD transform to a
    /// WORLD point the view reports; this turns that same WORLD point into a NORMALIZED position within
    /// <paramref name="target"/>'s own currently-displayed sprite rect, then into half-frame units (centre =
    /// (0,0), the shorter axis reaches ±1 at its edges) — the exact space <see cref="RelightModifier.lightX"/>/
    /// <c>lightY</c> already use, so a live-fed value lands exactly where a painted one would for the same spot.
    /// </summary>
    public class ZoeMetaPositionResolver : IExternalPositionResolver
    {
        readonly IAnimatedView _view;
        readonly SpriteRenderer _target;

        public ZoeMetaPositionResolver(IAnimatedView view, SpriteRenderer target)
        {
            _view = view;
            _target = target;
        }

        public bool TryResolve(string key, out Vector2 localPosition)
        {
            localPosition = default;
            if (_view == null || _target == null || _target.sprite == null || string.IsNullOrEmpty(key))
                return false;

            Vector2 worldPos;
            switch (_view.GetMetaLayerKind(key))
            {
                case MetaLayerKind.Point:
                    if (!_view.TryGetMetaPointNearest(key, out worldPos)) return false;
                    break;
                case MetaLayerKind.Vector:
                    if (!_view.TryGetMetaVectorNearest(key, out worldPos, out _, out _)) return false;
                    break;
                default:
                    return false;   // no such layer authored anywhere on this view
            }

            return TryWorldToHalfFrame(worldPos, _target, out localPosition);
        }

        /// The reverse of the WORLD conversion <see cref="IAnimatedView"/>'s samplers already apply: project a
        /// world point into `target`'s local space via its own transform, normalize against the CURRENTLY
        /// displayed sprite's bounds (so it tracks a composite part's own frame, not some fixed rect), then
        /// rescale from 0..1 UV (bottom-left origin — the same convention Launimator's own MetaLayer authoring
        /// uses) into half-frame units. Ratio-based, so it needs no pixels-per-unit constant: units cancel.
        static bool TryWorldToHalfFrame(Vector2 worldPos, SpriteRenderer target, out Vector2 halfFramePos)
        {
            halfFramePos = default;
            Bounds b = target.sprite.bounds;   // local space, already centred on the sprite's own pivot
            if (b.size.x <= 0f || b.size.y <= 0f) return false;

            Vector3 local = target.transform.InverseTransformPoint(new Vector3(worldPos.x, worldPos.y, 0f));
            float u = (local.x - b.min.x) / b.size.x;
            float v = (local.y - b.min.y) / b.size.y;

            float half = Mathf.Max(1e-4f, Mathf.Min(b.size.x, b.size.y) * 0.5f);
            halfFramePos = new Vector2((u - 0.5f) * b.size.x / half, (v - 0.5f) * b.size.y / half);
            return true;
        }
    }
}
