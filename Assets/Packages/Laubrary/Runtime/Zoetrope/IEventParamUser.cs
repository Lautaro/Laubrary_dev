using System;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// Which of a Zoe event's typed in-params an <see cref="IEffect"/> reads. The Zoe-event editor shows ONLY the
    /// param pickers an effect declares here, so the authoring UI stays clean: a Play-Reel effect (reads none)
    /// shows no pickers, a Pushback shows Direction + Scalar, a Spawn-Pyre shows Position + Scalar, a Spawn-Chunk
    /// all three. Flags, so an effect can read any combination.
    /// </summary>
    [Flags]
    public enum EventParam
    {
        None = 0,
        /// The effect reads the event's resolved POSITION — its <see cref="FxPlacementType"/> picker feeds
        /// <see cref="EventContext.Position"/>.
        Position = 1 << 0,
        /// The effect reads the event's resolved DIRECTION — its <see cref="DirectionParam"/> picker feeds
        /// <see cref="EventContext.DirectionDeg"/>.
        Direction = 1 << 1,
        /// The effect reads the event's resolved SCALAR — its <see cref="ScalarParam"/> picker feeds
        /// <see cref="EventContext.Scalar"/>.
        Scalar = 1 << 2,
    }

    /// <summary>
    /// Optional on an <see cref="IEffect"/>: declares which of the event's typed in-params it reads, so the
    /// Zoe-event editor exposes exactly those pickers (Position / Direction / Scalar) and hides the rest — the
    /// clean-UI rule from ZOE_EVENTS_DESIGN.md step 4 ("only show the pickers an effect actually uses").
    ///
    /// An effect that does NOT implement this is treated as reading NONE — EXCEPT a legacy <see cref="ICombatFx"/>
    /// (a spawn-VFX-at-a-point effect), which the editor defaults to Position + Direction so existing placement
    /// authoring on e.g. <c>PyreChunksFx</c> is unchanged. This is purely an EDITOR HINT: it adds no serialized
    /// data, is never read at runtime, and each effect's <see cref="IEffect.Apply"/> already only touches the
    /// resolved values it needs — declaring them here just keeps the picker chrome honest.
    /// </summary>
    public interface IEventParamUser
    {
        /// Which typed in-params this effect reads from the <see cref="EventContext"/>.
        EventParam UsedParams { get; }
    }
}
