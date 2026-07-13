using UnityEngine;

namespace Laubrary.Launimator
{
    /// <summary>One painted meta-cell as a world-space quad (BL→BR→TR→TL) with a colour already tinted by its
    /// value. Produced by <see cref="ZonedAnimationPlayer.CollectMetaCellQuads"/> for debug visualisation.</summary>
    public struct MetaCellQuad
    {
        public Vector3 bl, br, tr, tl;
        public Color color;
    }
}
