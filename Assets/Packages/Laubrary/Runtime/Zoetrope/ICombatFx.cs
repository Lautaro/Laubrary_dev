using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// A pluggable one-shot combat effect played at a world point — a hit flash, a death burst, a muzzle flash, a
    /// projectile impact. Concrete implementations are <c>[Serializable]</c> and live in OPTIONAL bridge modules
    /// (e.g. <c>Zoetrope.Pyre</c> plays a Pyre blast + Chunks debris), so Zoetrope's core depends on Combat2D
    /// ONLY. A project plugs in whatever presentation tools it actually has; a Def with no effect just leaves the
    /// slot null. Assigned via <c>[SerializeReference]</c> on the Defs.
    /// </summary>
    public interface ICombatFx
    {
        /// True when there's nothing to play (so callers can skip spawning a GameObject).
        bool IsEmpty { get; }

        /// Play the effect at <paramref name="worldPos"/>; <paramref name="directionDeg"/> aims directional effects
        /// (NaN = omni-directional).
        void Play(Vector2 worldPos, float directionDeg = float.NaN);

        /// Same as <see cref="Play"/>, but returns the spawned instance's Transform so a caller can keep
        /// repositioning it (see <see cref="FxFollowTarget"/>) — or null if this effect has nothing single,
        /// ongoing to hand back (e.g. a chunk burst scatters into several independently-moving pieces; there's
        /// no one Transform to follow).
        Transform PlayFollowable(Vector2 worldPos, float directionDeg = float.NaN);
    }
}
