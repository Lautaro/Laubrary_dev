using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// A pluggable one-shot combat effect played at a world point — a hit flash, a death burst, a muzzle flash, a
    /// projectile impact. Concrete implementations are <c>[Serializable]</c> and live in OPTIONAL bridge modules
    /// (e.g. <c>Zoetrope.Pyre</c> plays a Pyre blast + Chunks debris), so Zoetrope's core depends on Combat2D
    /// ONLY. A project plugs in whatever presentation tools it actually has; a Def with no effect just leaves the
    /// slot null. Assigned via <c>[SerializeReference]</c> on the Defs.
    ///
    /// This is the "spawn VFX at a world point" kind of <see cref="IEffect"/>: an <c>ICombatFx</c> IS an
    /// <c>IEffect</c>, so a <c>[SerializeReference] IEffect</c> slot holds any existing <c>ICombatFx</c> instance
    /// unchanged (the asset stores the concrete class name, which is untouched by this relationship). Its
    /// <see cref="IEffect.Apply"/> is bridged by default to <see cref="Play"/> at the context's resolved position
    /// + direction, so every existing implementation keeps working without a line changed.
    /// </summary>
    public interface ICombatFx : IEffect
    {
        // IsEmpty is inherited from IEffect (same contract) — implementations already satisfy it.

        /// Play the effect at <paramref name="worldPos"/>; <paramref name="directionDeg"/> aims directional effects
        /// (NaN = omni-directional).
        void Play(Vector2 worldPos, float directionDeg = float.NaN);

        /// Same as <see cref="Play"/>, but returns the spawned instance's Transform so a caller can keep
        /// repositioning it (see <see cref="FxFollowTarget"/>) — or null if this effect has nothing single,
        /// ongoing to hand back (e.g. a chunk burst scatters into several independently-moving pieces; there's
        /// no one Transform to follow).
        Transform PlayFollowable(Vector2 worldPos, float directionDeg = float.NaN, bool flipX = false);

        /// Play the effect ORIENTED: its spawned visual turned so its forward points along
        /// <paramref name="aimDeg"/> (NaN = upright) and mirrored when <paramref name="flipX"/> — what a muzzle
        /// flash wants, where <see cref="Play"/> only AIMS a directional burst and leaves a spawned sprite
        /// upright. Default bridges to <see cref="Play"/>, so an effect with nothing to turn behaves as before.
        void PlayOriented(Vector2 worldPos, float aimDeg, bool flipX) => Play(worldPos, aimDeg);

        /// Default <see cref="IEffect"/> bridge: an ICombatFx applies by spawning at the context's resolved
        /// position + direction — the point its picked placement param resolved to. A default interface
        /// implementation so every existing ICombatFx satisfies <see cref="IEffect"/> with no change.
        void IEffect.Apply(EventContext ctx) => Play(ctx.Position, ctx.DirectionDeg);
    }
}
